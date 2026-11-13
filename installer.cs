using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using VSCodePortableCommon;

namespace VSCodePortableInstaller
{
    partial class InstallForm : Form
    {
        // Constants
        private const int MAX_DOWNLOAD_TIMEOUT_MINUTES = 10;
        private const int POWERSHELL_HEADSTART_MS = 8000;
        private const int BUFFER_SIZE = 131072;  // 128KB for faster I/O
        private const int DOWNLOAD_BUFFER_SIZE = 1048576;  // 1MB for download operations (optimized)
        private const int EXTRACT_BUFFER_SIZE = 131072;  // 128KB for faster extraction
        private const int VSCODE_DOWNLOAD_RETRY_COUNT = 2;
        private const int VSCODE_EXTENSION_DOWNLOAD_CONCURRENCY = 4;
        private const int MAX_ACTIVITY_LOG_LINES = 20;
        private static readonly string[] VSCODE_PLATFORM_SENSITIVE_EXTENSIONS = new[]
        {
            // ms-python.python ships a platform-specific 'pet.exe' (Python Env Tool)
            // under python-env-tools/bin/. Without targetPlatform=win32-x64, the
            // marketplace returns the platform-neutral package which contains only
            // the Linux 'pet' binary, causing 'Python Locator failed to start'.
            "ms-python.python",
            "ms-python.vscode-python-envs",
            "ms-python.debugpy"
        };
        private static readonly string[] VSCODE_OPTIONAL_AUTO_INSTALLED_EXTENSIONS = new string[0];

        // URLs
        private const string VSCODE_LATEST_DOWNLOAD_URL = "https://update.code.visualstudio.com/latest/win32-x64-archive/stable";
        private const string VSCODE_MARKETPLACE_VSIX_URL_TEMPLATE = "https://marketplace.visualstudio.com/_apis/public/gallery/publishers/{0}/vsextensions/{1}/latest/vspackage";
        private const string VSCODE_MARKETPLACE_VSIX_VERSION_URL_TEMPLATE = "https://marketplace.visualstudio.com/_apis/public/gallery/publishers/{0}/vsextensions/{1}/{2}/vspackage";
        private const string VSCODE_MARKETPLACE_QUERY_URL = "https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery";
        private const string UTILS_RAW_BASE_URL = "https://raw.githubusercontent.com/PlanXLab/utils/main/";

        // Paths
        private const string TEMP_INSTALL_DIR = "vscode-pvs-install";

        // Win32 API for extracting icon from exe
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

        // Win32 API for borderless window dragging
        [DllImport("user32.dll")]
        static extern int ReleaseCapture();
        [DllImport("user32.dll")]
        static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        private string installDir;
        private int totalComponents;
        private int completedComponents;
        private bool installCompleted;
        private bool installStarted;
        private bool installationSucceeded;
        private bool installDirectoryWasEmptyAtStart;
        private bool pwsh7Failed;
        private bool criticalFailed;
        private bool retryOnlyPowerShell;
        private Dictionary<string, string> completedInstallVersions = new Dictionary<string, string>();
        private bool pythonPreflightInProgress;
        private bool pythonVersionIsAvailable;
        private System.Threading.CancellationTokenSource pythonVersionValidationCancellation;
        private int pythonVersionValidationGeneration;
        private string cachedPythonVersionRequest;
        private string cachedPythonVersion;
        private DateTime cachedPythonVersionCheckTime;
        private System.Diagnostics.Stopwatch installTimer;
        private DateTime installStartTime;
        private System.Threading.CancellationTokenSource installCancellationSource;
        private Task installationTask;
        private System.Windows.Forms.Timer elapsedTimer;
        private StringBuilder logFileBuffer = new StringBuilder();
        private object logFileLock = new object();
        private Queue<string> activityDisplayEntries = new Queue<string>();
        private string overallStatusOverride = "Ready to install";

        // New: collapse/expand state and stored full size
        private bool isCollapsedOnStart = true;
        private Size fullClientSize;

        public InstallForm()
        {
            totalComponents = 3;
            completedComponents = 0;
            installCompleted = false;
            installStarted = false;
            installationSucceeded = false;
            pwsh7Failed = false;

            InitializeComponent();
            CodeNixTheme.Apply(
                this,
                headerPanel,
                headerTitleLabel,
                headerSubLabel,
                label1,
                settingsPanel,
                separatorPanel,
                progressPanel,
                componentTable,
                activityLog,
                logHeaderLabel,
                installButton,
                cancelButton);

            this.AcceptButton = installButton;
            installPathTextBox.KeyDown += InputField_KeyDown;
            installPathTextBox.TextChanged += InstallPathTextBox_TextChanged;
            pythonVersionTextBox.KeyDown += InputField_KeyDown;
            pythonVersionTextBox.TextChanged += PythonVersionTextBox_TextChanged;
            installButton.Enabled = false;
            cancelButton.Enabled = true;
            UpdateInstallButtonState();
            CodeNixTheme.StylePrimaryButton(cancelButton);
            Shown += (sender, args) => PythonVersionTextBox_TextChanged(pythonVersionTextBox, EventArgs.Empty);
            UpdateOverallProgress();
            UpdateCurrentTask("Ready to install");

            // Capture full size (designer size) for later restore
            fullClientSize = this.ClientSize;

            // Set form icon (extract from exe's win32 icon resource)
            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                IntPtr hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
                if (hIcon != IntPtr.Zero)
                {
                    Icon = Icon.FromHandle(hIcon);
                }
            }
            catch { /* Ignore icon errors */ }
            // Enable dragging from header panel (borderless window)
            headerPanel.MouseDown += HeaderPanel_MouseDown;
            headerTitleLabel.MouseDown += HeaderPanel_MouseDown;
            headerSubLabel.MouseDown += HeaderPanel_MouseDown;

            // Start collapsed view so only top area (including Install button) is visible.
            // Controls tagged "progress" will be hidden and form will be resized smaller.
            CollapseFormToInstallButton();
        }

        private void CollapseFormToInstallButton()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(CollapseFormToInstallButton));
                return;
            }

            try
            {
                // Hide progress UI
                if (progressPanel != null)
                    progressPanel.Visible = false;
                if (separatorPanel != null)
                    separatorPanel.Visible = false;

                // Compute minimal height so settings area is visible with margin
                int margin = 20;
                int minimalHeight = settingsPanel.Bottom + margin;

                // Do not make it smaller than a reasonable value
                minimalHeight = Math.Max(minimalHeight, 100);

                // Set client size to full width designer had, but reduced height
                this.ClientSize = new Size(fullClientSize.Width, minimalHeight);

                // Center on screen to look polished
                this.CenterToScreen();

                isCollapsedOnStart = true;
            }
            catch { /* non-critical */ }
        }

        private void HeaderPanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            }
        }

        private void ExpandFormToFullSize()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(ExpandFormToFullSize));
                return;
            }

            try
            {
                // Make progress controls visible
                ShowProgressUI();

                // Restore full client size
                this.ClientSize = fullClientSize;

                // Center on screen again
                this.CenterToScreen();

                this.Invalidate();
                this.Update();

                isCollapsedOnStart = false;
            }
            catch { /* non-critical */ }
        }
        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select installation directory";
                dialog.ShowNewFolderButton = true;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    installPathTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void InputField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter || !installButton.Enabled)
                return;

            e.SuppressKeyPress = true;
            e.Handled = true;
            InstallButton_Click(installButton, EventArgs.Empty);
        }

        private bool ValidatePythonVersionInput()
        {
            string requestedVersion = pythonVersionTextBox.Text.Trim();
            if (PortablePython.IsValidPythonVersionRequest(requestedVersion))
                return true;

            pythonHint.Text = "Invalid version; enter x.y or x.y.z.";
            pythonHint.ForeColor = System.Drawing.Color.FromArgb(180, 55, 55);
            pythonVersionTextBox.Focus();
            pythonVersionTextBox.SelectAll();
            return false;
        }

        private void InstallPathTextBox_TextChanged(object sender, EventArgs e)
        {
            UpdateInstallButtonState();
        }

        private async void PythonVersionTextBox_TextChanged(object sender, EventArgs e)
        {
            if (installStarted)
                return;

            CancelPendingPythonVersionAvailabilityCheck();
            pythonVersionIsAvailable = false;
            UpdateInstallButtonState();
            string requestedVersion = pythonVersionTextBox.Text.Trim();
            bool useLatest = string.IsNullOrWhiteSpace(requestedVersion);
            if (!useLatest && !PortablePython.IsValidPythonVersionRequest(requestedVersion))
            {
                pythonHint.Text = "Enter a Python version in x.y or x.y.z format.";
                pythonHint.ForeColor = System.Drawing.Color.FromArgb(180, 55, 55);
                return;
            }

            int generation = pythonVersionValidationGeneration;
            var validation = new System.Threading.CancellationTokenSource();
            pythonVersionValidationCancellation = validation;
            pythonHint.Text = useLatest ? "Checking latest stable Python..." : "Checking Python availability...";
            pythonHint.ForeColor = CodeNixTheme.TextTertiary;

            try
            {
                await Task.Delay(600, validation.Token);
                string availableVersion = await GetAvailablePythonVersionAsync(requestedVersion, validation.Token);
                if (generation != pythonVersionValidationGeneration || IsDisposed)
                    return;

                pythonVersionIsAvailable = true;
                pythonHint.Text = useLatest
                    ? "Latest stable: Python " + availableVersion
                    : "Available: Python " + availableVersion;
                pythonHint.ForeColor = CodeNixTheme.Success;
                UpdateInstallButtonState();
            }
            catch (OperationCanceledException)
            {
            }
            catch (InvalidOperationException)
            {
                if (generation == pythonVersionValidationGeneration && !IsDisposed)
                {
                    pythonHint.Text = "Python " + requestedVersion + " is not available.";
                    pythonHint.ForeColor = System.Drawing.Color.FromArgb(180, 55, 55);
                    UpdateInstallButtonState();
                }
            }
            catch (Exception)
            {
                if (generation == pythonVersionValidationGeneration && !IsDisposed)
                {
                    pythonHint.Text = "Could not check availability. Check the network and retry.";
                    pythonHint.ForeColor = System.Drawing.Color.FromArgb(180, 120, 35);
                    UpdateInstallButtonState();
                }
            }
            finally
            {
                if (ReferenceEquals(pythonVersionValidationCancellation, validation))
                    pythonVersionValidationCancellation = null;
                if (generation == pythonVersionValidationGeneration && !IsDisposed)
                    UpdateInstallButtonState();
                validation.Dispose();
            }
        }

        private void UpdateInstallButtonState()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateInstallButtonState));
                return;
            }

            if (installStarted || installCompleted)
                return;

            string pathMessage;
            bool pathAvailable = IsInstallPathAvailable(installPathTextBox.Text.Trim(), out pathMessage);
            installPathHint.Text = pathMessage;
            installPathHint.ForeColor = pathAvailable
                ? System.Drawing.Color.FromArgb(80, 135, 95)
                : System.Drawing.Color.FromArgb(180, 55, 55);
            installPathTextBox.BackColor = pathAvailable
                ? System.Drawing.Color.White
                : System.Drawing.Color.FromArgb(255, 244, 244);
            installButton.Enabled = pathAvailable && pythonVersionIsAvailable && !pythonPreflightInProgress;
            if (installButton.Enabled)
                CodeNixTheme.StylePrimaryButton(installButton);
            else
                CodeNixTheme.StyleDisabledButton(installButton);
        }

        private bool IsInstallPathAvailable(string path, out string message)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                message = "Choose an installation folder.";
                return false;
            }

            if (!Regex.IsMatch(path, @"^[A-Za-z]:\\[^\\]+.*$"))
            {
                message = "Enter a path such as C:\\VSCode.";
                return false;
            }

            if (path.EndsWith("\\") && path.Length > 3)
            {
                message = "A trailing backslash is not allowed.";
                return false;
            }

            if (Directory.Exists(path) || File.Exists(path))
            {
                message = "Target path already exists; choose a new folder.";
                return false;
            }

            message = "Target folder is available.";
            return true;
        }

        private void CancelPendingPythonVersionAvailabilityCheck()
        {
            pythonVersionValidationGeneration++;
            var validation = pythonVersionValidationCancellation;
            pythonVersionValidationCancellation = null;
            if (validation != null)
                validation.Cancel();
        }

        private async Task<string> GetAvailablePythonVersionAsync(string requestedVersion,
            System.Threading.CancellationToken cancellationToken)
        {
            if (string.Equals(cachedPythonVersionRequest, requestedVersion, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(cachedPythonVersion) &&
                DateTime.UtcNow - cachedPythonVersionCheckTime < TimeSpan.FromMinutes(2))
                return cachedPythonVersion;

            string availableVersion = await PortablePython.ResolveAvailablePythonVersionAsync(
                requestedVersion, cancellationToken).ConfigureAwait(false);
            cachedPythonVersionRequest = requestedVersion;
            cachedPythonVersion = availableVersion;
            cachedPythonVersionCheckTime = DateTime.UtcNow;
            return availableVersion;
        }

        private async void InstallButton_Click(object sender, EventArgs e)
        {
            // If installation completed and no failures, Exit button closes the form
            if (installCompleted && !pwsh7Failed && !criticalFailed)
            {
                Close();
                return;
            }

            if (installCompleted && (pwsh7Failed || criticalFailed))
            {
                RetryInstallation();
                return;
            }

            if (installStarted)
                return;

            if (pythonPreflightInProgress)
                return;

            if (!ValidatePythonVersionInput())
                return;

            // New behavior: if form was collapsed at start, expand to full UI when Install is pressed
            if (isCollapsedOnStart && !installStarted)
            {
                ExpandFormToFullSize();
            }

            string path = installPathTextBox.Text.Trim();

            // Validate path format
            if (!ValidateInstallPath(path))
                return;
            string requestedPythonVersion = pythonVersionTextBox.Text.Trim();

            pythonPreflightInProgress = true;
            UpdateInstallButtonState();
            string versionToCheck = requestedPythonVersion;
            CancelPendingPythonVersionAvailabilityCheck();
            pythonHint.Text = string.IsNullOrWhiteSpace(versionToCheck)
                ? "Checking latest stable Python..."
                : "Checking Python availability...";
            pythonHint.ForeColor = CodeNixTheme.TextTertiary;
            try
            {
                requestedPythonVersion = await GetAvailablePythonVersionAsync(
                    versionToCheck, default(System.Threading.CancellationToken));
                if (!string.Equals(pythonVersionTextBox.Text.Trim(), versionToCheck,
                    StringComparison.OrdinalIgnoreCase))
                {
                    pythonHint.Text = "Python version changed. Wait for availability check.";
                    pythonHint.ForeColor = System.Drawing.Color.FromArgb(180, 120, 35);
                    return;
                }

                pythonVersionIsAvailable = true;
                pythonHint.Text = "Available: Python " + requestedPythonVersion;
                pythonHint.ForeColor = CodeNixTheme.Success;
            }
            catch (InvalidOperationException)
            {
                pythonVersionIsAvailable = false;
                pythonHint.Text = "Python " + versionToCheck + " is not available.";
                pythonHint.ForeColor = System.Drawing.Color.FromArgb(180, 55, 55);
                return;
            }
            catch (Exception ex)
            {
                pythonVersionIsAvailable = false;
                LogActivity("Python: Version availability check failed - " + ex.Message);
                pythonHint.Text = "Could not check availability. Check the network and retry.";
                pythonHint.ForeColor = System.Drawing.Color.FromArgb(180, 120, 35);
                return;
            }
            finally
            {
                pythonPreflightInProgress = false;
                if (!installStarted)
                    UpdateInstallButtonState();
            }

            installDirectoryWasEmptyAtStart = IsInstallDirectoryEmpty(path);
            cancelButton.Enabled = true;
            cancelButton.Text = "Cancel";
            CodeNixTheme.StylePrimaryButton(cancelButton);
            installDir = path;
            retryOnlyPowerShell = false;
            completedInstallVersions = new Dictionary<string, string>();
            installStarted = true;
            CancelPendingPythonVersionAvailabilityCheck();
            installationSucceeded = false;
            ResetProgressUI();

            // Initialize cancellation token for graceful cancellation
            installCancellationSource = new System.Threading.CancellationTokenSource();

            // Start performance timer
            installTimer = System.Diagnostics.Stopwatch.StartNew();
            installStartTime = DateTime.Now;
            LogActivity("=== Installation started at " + installStartTime.ToString("HH:mm:ss") + " ===");

            // Start elapsed time timer (update every second)
            StartElapsedTimer();

            // Show progress UI
            ShowProgressUI();

            // Disable path controls
            installPathTextBox.Enabled = false;
            browseButton.Enabled = false;
            // Apply clear disabled visual state to Install button
            SetInstallButtonInstallingState();
            pythonVersionTextBox.Enabled = false;

            // Start installation
            var token = installCancellationSource.Token;
            installationTask = Task.Run(() => PerformInstallation(token, requestedPythonVersion, false));
            await installationTask;
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            if (installCompleted)
            {
                if (installationSucceeded && string.Equals(cancelButton.Text, "Run", StringComparison.Ordinal))
                {
                    string launcherPath = Path.Combine(installDir, "launcher.exe");
                    try
                    {
                        if (!File.Exists(launcherPath))
                            throw new FileNotFoundException("launcher.exe was not found.", launcherPath);

                        using (var process = Process.Start(new ProcessStartInfo
                        {
                            FileName = launcherPath,
                            WorkingDirectory = installDir,
                            UseShellExecute = false
                        }))
                        {
                            if (process == null)
                                throw new InvalidOperationException("The launcher process did not start.");
                        }
                        Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Could not launch VS Code: " + ex.Message,
                            "Launch Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    Close();
                }
                return;
            }

            if (!installStarted)
            {
                Close();
                return;
            }

            if (!installCompleted)
            {
                // Ask for confirmation BEFORE cancelling
                var result = MessageBox.Show(
                    "Are you sure you want to cancel the installation?\n\n" +
                    "All downloaded files and the installation directory will be deleted.",
                    "Cancel Installation",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    // User confirmed - cancel immediately and cleanup
                    LogActivity("Installation cancelled by user");

                    // Disable buttons immediately to prevent multiple clicks
                    installButton.Enabled = false;
                    cancelButton.Enabled = false;
                    CodeNixTheme.StyleDisabledButton(installButton);
                    CodeNixTheme.StyleDisabledButton(cancelButton);

                    // Request cancellation
                    if (installCancellationSource != null && !installCancellationSource.IsCancellationRequested)
                    {
                        installCancellationSource.Cancel();
                        LogActivity("Cancellation requested for all tasks");
                    }

                    UpdateCurrentTask("Cancelling installation...");

                    // Hide main window
                    this.Hide();
                    Task taskToStop = installationTask;

                    // Show progress dialog in a separate thread
                    Task.Run(async () =>
                    {
                        Form progressDialog = null;
                        Label progressLabel = null;
                        System.Windows.Forms.Timer animationTimer = null;
                        int spinnerIndex = 0;
                        string[] spinnerChars = new string[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
                        string baseMessage = "";

                        try
                        {
                            // Create and show progress dialog on UI thread
                            Invoke(new Action(() =>
                            {
                                progressDialog = new Form
                                {
                                    Text = "Cleaning up...",
                                    Width = 400,
                                    Height = 150,
                                    FormBorderStyle = FormBorderStyle.FixedDialog,
                                    StartPosition = FormStartPosition.Manual,
                                    MaximizeBox = false,
                                    MinimizeBox = false,
                                    ControlBox = false,
                                    TopMost = true
                                };

                                // Center on main window's location
                                int x = this.Left + (this.Width - progressDialog.Width) / 2;
                                int y = this.Top + (this.Height - progressDialog.Height) / 2;
                                progressDialog.Location = new System.Drawing.Point(x, y);

                                progressLabel = new Label
                                {
                                    Text = "⠋ Stopping tasks",
                                    AutoSize = false,
                                    Width = 360,
                                    Height = 60,
                                    Left = 20,
                                    Top = 30,
                                    Font = new System.Drawing.Font("Segoe UI", 10F),
                                    TextAlign = System.Drawing.ContentAlignment.MiddleLeft
                                };

                                progressDialog.Controls.Add(progressLabel);

                                // Setup animation timer
                                baseMessage = "Stopping tasks";
                                animationTimer = new System.Windows.Forms.Timer();
                                animationTimer.Interval = 100; // Update every 100ms for smooth rotation
                                animationTimer.Tick += (tmr, evt) =>
                                {
                                    spinnerIndex = (spinnerIndex + 1) % spinnerChars.Length;
                                    progressLabel.Text = spinnerChars[spinnerIndex] + " " + baseMessage;
                                };
                                animationTimer.Start();

                                progressDialog.Show(this);
                            }));

                            LogActivity("Waiting for installation tasks to stop...");
                            if (taskToStop != null)
                            {
                                try { await taskToStop.ConfigureAwait(false); }
                                catch (OperationCanceledException) { }
                                catch (Exception ex) { LogActivity("Installation task stopped with an error: " + ex.Message); }
                            }

                            // Update progress
                            Invoke(new Action(() =>
                            {
                                if (progressLabel != null)
                                {
                                    baseMessage = "Terminating processes";
                                    spinnerIndex = 0;
                                }
                            }));

                            LogActivity("Cleaning up temporary files and installation directory...");
                            CleanupOnCancel(progressLabel, () => baseMessage, (msg) => { baseMessage = msg; spinnerIndex = 0; });

                            // Update progress
                            Invoke(new Action(() =>
                            {
                                if (progressLabel != null)
                                {
                                    baseMessage = "Cleanup completed. Exiting";
                                    spinnerIndex = 0;
                                }
                            }));

                            LogActivity("Cleanup completed. Exiting...");
                            System.Threading.Thread.Sleep(500); // Brief delay to show final message
                        }
                        catch (Exception ex)
                        {
                            LogActivity("Cleanup error: " + ex.Message);
                        }
                        finally
                        {
                            // Stop animation timer
                            try
                            {
                                if (animationTimer != null)
                                {
                                    Invoke(new Action(() =>
                                    {
                                        animationTimer.Stop();
                                        animationTimer.Dispose();
                                    }));
                                }
                            }
                            catch { }

                            // Close progress dialog
                            try
                            {
                                if (progressDialog != null)
                                {
                                    Invoke(new Action(() =>
                                    {
                                        progressDialog.Close();
                                        progressDialog.Dispose();
                                    }));
                                }
                            }
                            catch { }

                            // Force exit
                            Environment.Exit(0);
                        }
                    });
                }
                // If user clicked No, do nothing - installation continues
            }
            else
            {
                Close();
            }
        }

        private void CleanupOnCancel(Label progressLabel = null, Func<string> getBaseMessage = null,
            Action<string> setBaseMessage = null, bool preserveInstallDirectory = false)
        {
            // Kill any running VSCode or PowerShell processes from installation directory
            UpdateProgressLabel(progressLabel, "Terminating running processes", setBaseMessage);
            if (preserveInstallDirectory)
                LogActivity("Not terminating processes while preserving the partial installation directory.");
            else if (installDirectoryWasEmptyAtStart)
                KillInstallationProcesses();
            else
                LogActivity("Not terminating processes in pre-existing installation directory: " + installDir);

            // Brief delay to ensure processes are terminated
            System.Threading.Thread.Sleep(1000);

            // 1. Clean temporary directories
            UpdateProgressLabel(progressLabel, "Deleting temporary directories", setBaseMessage);
            string[] tempDirs = {
                Path.Combine(Path.GetTempPath(), "vscode-pvs-install"),
                Path.Combine(Path.GetTempPath(), "python-pvs-install"),
                Path.Combine(Path.GetTempPath(), "pwsh7-pvs-install")
            };

            foreach (string tempDir in tempDirs)
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                        LogActivity("Deleted temp directory: " + tempDir);
                    }
                }
                catch (Exception ex)
                {
                    LogActivity("Failed to delete " + tempDir + ": " + ex.Message);
                }
            }

            // 2. Delete installation directory (PVS folder)
            UpdateProgressLabel(progressLabel, "Deleting installation directory", setBaseMessage);
            if (preserveInstallDirectory && !string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
            {
                LogActivity("Preserving partial installation directory: " + installDir);
            }
            else if (installDirectoryWasEmptyAtStart && !string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
            {
                try
                {
                    // Give processes time to release file handles after being killed
                    System.Threading.Thread.Sleep(1000);

                    // Force delete with retry mechanism
                    int retries = 0;
                    while (Directory.Exists(installDir) && retries < 5)
                    {
                        try
                        {
                            Directory.Delete(installDir, true);
                            LogActivity("Deleted installation directory: " + installDir);
                            break;
                        }
                        catch (Exception ex)
                        {
                            retries++;
                            if (retries < 5)
                            {
                                UpdateProgressLabel(progressLabel, "Retrying deletion (" + retries + "/5)", setBaseMessage);
                                LogActivity("Retry " + retries + "/5: Failed to delete installation directory: " + ex.Message);

                                // Try killing processes again on each retry
                                if (retries % 2 == 0)
                                {
                                    LogActivity("Attempting to kill processes again...");
                                    KillInstallationProcesses();
                                    System.Threading.Thread.Sleep(500);
                                }
                                else
                                {
                                    System.Threading.Thread.Sleep(500);
                                }
                            }
                            else
                            {
                                LogActivity("Failed to delete installation directory after 5 retries: " + ex.Message);

                                // Try to delete individual subdirectories if full deletion fails
                                try
                                {
                                    string dataDir = Path.Combine(installDir, "data");
                                    if (Directory.Exists(dataDir))
                                    {
                                        Directory.Delete(dataDir, true);
                                        LogActivity("Deleted data directory");
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogActivity("Failed to delete installation directory: " + ex.Message);
                }
            }
            else if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
            {
                LogActivity("Preserving pre-existing installation directory: " + installDir);
            }

            LogActivity("Cleanup completed");
        }

        private void UpdateProgressLabel(Label label, string text, Action<string> setBaseMessage = null)
        {
            if (label == null)
                return;

            try
            {
                Invoke(new Action(() =>
                {
                    if (setBaseMessage != null)
                    {
                        setBaseMessage(text);
                    }
                    else
                    {
                        label.Text = text;
                    }
                }));
            }
            catch
            {
                // Ignore errors if form is closing
            }
        }

        private void KillInstallationProcesses()
        {
            if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir))
                return;

            try
            {
                string installationRoot = Path.GetFullPath(installDir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Code", "Code Helper", "pwsh", "python", "pythonw", "python3", "python3w", "uv", "oh-my-posh"
                };
                int killedCount = 0;
                var allProcesses = Process.GetProcesses();

                foreach (var process in allProcesses)
                {
                    string processName;
                    try
                    {
                        processName = process.ProcessName;
                    }
                    catch
                    {
                        process.Dispose();
                        continue;
                    }

                    if (!candidateNames.Contains(processName))
                    {
                        process.Dispose();
                        continue;
                    }

                    try
                    {
                        if (process.HasExited)
                            continue;

                        string processPath = process.MainModule.FileName;
                        if (string.IsNullOrEmpty(processPath) ||
                            !Path.GetFullPath(processPath).StartsWith(installationRoot, StringComparison.OrdinalIgnoreCase))
                            continue;

                        LogActivity("Killing installation process: " + processName + " (PID: " + process.Id + ")");
                        process.Kill();
                        if (process.WaitForExit(2000))
                            killedCount++;
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        LogActivity("Could not inspect or terminate installation process " + processName + ": " + ex.Message);
                    }
                    catch (Exception ex)
                    {
                        LogActivity("Could not terminate installation process " + processName + ": " + ex.Message);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                if (killedCount > 0)
                {
                    LogActivity("Killed " + killedCount + " process(es) from installation directory");
                }
                else
                {
                    LogActivity("No processes found running from installation directory");
                }
            }
            catch (Exception ex)
            {
                LogActivity("Error while killing processes: " + ex.Message);
            }
        }

        private bool ValidateInstallPath(string path)
        {
            string message;
            if (IsInstallPathAvailable(path, out message))
                return true;

            MessageBox.Show(message, "Invalid Installation Path",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private bool IsInstallDirectoryEmpty(string path)
        {
            if (!Directory.Exists(path))
                return true;

            try
            {
                return Directory.GetFileSystemEntries(path).Length == 0;
            }
            catch (Exception ex)
            {
                LogActivity("Could not inspect installation directory; cleanup will preserve it: " + ex.Message);
                return false;
            }
        }

        private void ShowProgressUI()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(ShowProgressUI));
                return;
            }

            // Show progress panel and separator
            if (progressPanel != null)
                progressPanel.Visible = true;
            if (separatorPanel != null)
                separatorPanel.Visible = true;
        }

        // Helper methods to clearly indicate Install button state
        private void SetInstallButtonInstallingState()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(SetInstallButtonInstallingState));
                return;
            }

            try
            {
                installButton.Text = "Installing...";
                installButton.Enabled = false;
                CodeNixTheme.StyleDisabledButton(installButton);
                installButton.Cursor = Cursors.WaitCursor;
            }
            catch { }
        }

        private void RestoreInstallButtonStyle(string text = "Install")
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => RestoreInstallButtonStyle(text)));
                return;
            }

            try
            {
                installButton.Enabled = true;
                installButton.Text = text;
                CodeNixTheme.StylePrimaryButton(installButton);
            }
            catch { }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x00800000; // WS_BORDER
                return cp;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Prevent closing during installation (unless force exit is requested)
            if (installStarted && !installCompleted && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                var result = MessageBox.Show(
                    "Installation is in progress. Are you sure you want to cancel?",
                    "Cancel Installation",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    LogActivity("Installation cancelled by user (force close)");
                    Environment.Exit(0); // Force terminate entire process
                }
            }
            base.OnFormClosing(e);
        }

        private async Task PerformInstallation(System.Threading.CancellationToken cancellationToken,
            string requestedPythonVersion, bool retryPowerShellOnly)
        {
            try
            {
                // Check for cancellation before starting
                cancellationToken.ThrowIfCancellationRequested();

                // Optimize HTTP connection settings for faster downloads
                System.Net.ServicePointManager.DefaultConnectionLimit = 10;
                System.Net.ServicePointManager.MaxServicePointIdleTime = 10000;
                System.Net.ServicePointManager.UseNagleAlgorithm = false;
                System.Net.ServicePointManager.Expect100Continue = false;
                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls12 |
                    System.Net.SecurityProtocolType.Tls11 |
                    System.Net.SecurityProtocolType.Tls;

                // Create installation directory
                if (!Directory.Exists(installDir))
                {
                    UpdateCurrentTask("Creating installation directory...");
                    Directory.CreateDirectory(installDir);
                }

                UpdateCurrentTask("Preparing downloads...");
                await Aria2Downloader.EnsureInstalledAsync(installDir, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                var allVersions = retryPowerShellOnly
                    ? new Dictionary<string, string>(completedInstallVersions)
                    : new Dictionary<string, string>();

                if (retryPowerShellOnly)
                {
                    LogActivity("Retrying only failed PowerShell components...");
                    string pwshExe = Path.Combine(installDir, "data", "lib", "pwsh", "pwsh.exe");
                    Dictionary<string, string> powerShellVersions;
                    if (allVersions.ContainsKey("PWSH7_VERSION") && File.Exists(pwshExe))
                        powerShellVersions = await InstallPowerShell7AddonsAsync(cancellationToken);
                    else
                        powerShellVersions = await InstallPowerShell7Async(cancellationToken);

                    foreach (var kvp in powerShellVersions)
                        allVersions[kvp.Key] = kvp.Value;
                }
                else
                {
                    LogActivity("Phase 1: Preparing fonts and installing Python, VS Code and PowerShell 7 in parallel...");
                    var fontsTask = Task.Run(() => InstallFonts(cancellationToken));
                    var pythonTask = InstallPythonCoreAsync(cancellationToken, requestedPythonVersion);
                    var powerShellTask = InstallPowerShell7Async(cancellationToken);
                    var vscodeTask = InstallVSCodeFlowAsync(cancellationToken);
                    var phaseResults = await Task.WhenAll(
                        fontsTask,
                        pythonTask,
                        vscodeTask,
                        powerShellTask);
                    cancellationToken.ThrowIfCancellationRequested();

                    foreach (var result in phaseResults)
                    {
                        foreach (var kvp in result)
                            allVersions[kvp.Key] = kvp.Value;
                    }
                }

                completedInstallVersions = new Dictionary<string, string>(allVersions);

                var requiredPackageVersions = new Dictionary<string, string>
                {
                    { "Fonts", "FONTS_VERSION" },
                    { "PowerShell 7", "PWSH7_VERSION" },
                    { "Oh My Posh", "OHMYPOSH_VERSION" },
                    { "PSFzf", "PSFZF_VERSION" },
                    { "modern-unix-win", "MODERN_UNIX_WIN_VERSION" },
                    { "VS Code", "VSCODE_VERSION" },
                    { "VS Code extensions", "VSCODE_EXTENSIONS_VERSION" },
                    { "Python", "PYTHON_VERSION" },
                    { "uv", "UV_VERSION" }
                };
                var failedPackages = new List<string>();

                foreach (var requiredPackage in requiredPackageVersions)
                {
                    string installedVersion;
                    if (!allVersions.TryGetValue(requiredPackage.Value, out installedVersion) ||
                        string.IsNullOrWhiteSpace(installedVersion))
                        failedPackages.Add(requiredPackage.Key);
                }

                if (failedPackages.Count > 0)
                {
                    retryOnlyPowerShell = true;
                    foreach (string failedPackage in failedPackages)
                    {
                        if (failedPackage != "PowerShell 7" &&
                            failedPackage != "Oh My Posh" &&
                            failedPackage != "PSFzf" &&
                            failedPackage != "modern-unix-win")
                        {
                            retryOnlyPowerShell = false;
                            break;
                        }
                    }

                    if (failedPackages.Contains("PowerShell 7") || failedPackages.Contains("Oh My Posh") ||
                        failedPackages.Contains("PSFzf") || failedPackages.Contains("modern-unix-win"))
                        UpdateComponentStatus("PowerShell 7", "⚠ One or more packages failed", 0);
                    if (failedPackages.Contains("VS Code") || failedPackages.Contains("VS Code extensions"))
                        UpdateComponentStatus("VSCode", "⚠ Installation failed", 0);
                    if (failedPackages.Contains("Python") || failedPackages.Contains("uv"))
                        UpdateComponentStatus("Python", "⚠ Installation failed", 0);

                    string failureMessage = "Required package installation failed: " +
                        string.Join(", ", failedPackages.ToArray());
                    UpdateCurrentTask("⚠ Installation failed - " + failedPackages.Count + " package(s)");
                    LogActivity(failureMessage);
                    throw new InvalidOperationException(failureMessage);
                }

                // Final configuration
                UpdateCurrentTask("Creating configuration files...");

                await CreatePvsInfo(allVersions);
                await CopyLauncher();
                await CreateShortcuts();

                UpdateCurrentTask("✓ Installation completed successfully!");
                LogActivity("Installation completed successfully!");
                LogActivity("Installation directory: " + installDir);

                // Stop timer and log performance
                if (installTimer != null)
                {
                    installTimer.Stop();
                    var elapsed = installTimer.Elapsed;
                    string elapsedStr = string.Format("{0:D2}:{1:D2}:{2:D2}.{3:D3}",
                        elapsed.Hours, elapsed.Minutes, elapsed.Seconds, elapsed.Milliseconds);
                    LogActivity("=== Installation completed at " + DateTime.Now.ToString("HH:mm:ss") + " ===");
                    LogActivity("*** Total installation time: " + elapsedStr + " ***");
                }

                // Stop elapsed timer
                StopElapsedTimer();

                // Save installation log to codenix.log
                SaveInstallLog();

                installationSucceeded = true;
                installCompleted = true;
                retryOnlyPowerShell = false;
                pwsh7Failed = false;
                criticalFailed = false;
                Invoke(new Action(() =>
                {
                    RestoreInstallButtonStyle("Exit");
                    cancelButton.Text = "Run";
                    cancelButton.Enabled = true;
                    CodeNixTheme.StylePrimaryButton(cancelButton);
                }));
            }
            catch (OperationCanceledException)
            {
                // Installation was cancelled - check if user wants to resume or exit
                // If this exception reaches here, user clicked Yes to cancel
                LogActivity("Installation cancelled - cleanup in progress");
                installCompleted = false; // Keep as incomplete for cleanup
            }
            catch (Exception ex)
            {
                LogActivity("Installation error: " + ex.Message);
                criticalFailed = true;
                installCompleted = true;
                installationSucceeded = false;
                if (installTimer != null)
                    installTimer.Stop();

                try
                {
                    CleanupOnCancel(preserveInstallDirectory: true);
                }
                catch (Exception cleanupException)
                {
                    LogActivity("Installation cleanup failed: " + cleanupException.Message);
                }
                SaveInstallLog(true);

                bool installFolderPreserved = !string.IsNullOrEmpty(installDir) && Directory.Exists(installDir);
                Invoke(new Action(() =>
                {
                    StopElapsedTimer();
                    pythonVersionTextBox.Enabled = true;
                    installPathTextBox.Enabled = true;
                    browseButton.Enabled = true;
                    RestoreInstallButtonStyle("Retry");
                    cancelButton.Text = "Exit";
                    cancelButton.Enabled = true;
                    CodeNixTheme.StylePrimaryButton(cancelButton);
                    UpdateCurrentTask(installFolderPreserved
                        ? "Installation failed. Partial files were preserved."
                        : "Installation failed. Temporary files were cleaned.");
                    string cleanupMessage = installFolderPreserved
                        ? "The incomplete installation folder was preserved for retry or inspection:\r\n" + installDir
                        : "Temporary files were cleaned.";
                    MessageBox.Show(this, "Installation error: " + ex.Message + "\r\n\r\n" + cleanupMessage,
                        "Installation Failed",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }));
            }
        }

        private void UpdateComponentStatus(
            string componentName,
            string status,
            int progress,
            bool isIndeterminate = false)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateComponentStatus(componentName, status, progress, isIndeterminate)));
                return;
            }

            // Determine which controls to update based on component name
            Label statusLabel = null;
            CodeNixProgressBar progressBar = null;

            switch (componentName)
            {
                case "PowerShell 7":
                    statusLabel = pwshStatusLabel;
                    progressBar = pwshProgressBar;
                    break;
                case "VSCode":
                    statusLabel = vscodeStatusLabel;
                    progressBar = vscodeProgressBar;
                    break;
                case "Python":
                    statusLabel = pythonStatusLabel;
                    progressBar = pythonProgressBar;
                    break;
                default:
                    return; // Unknown component
            }

            // Categorize for color and simplified display
            string displayStatus = status;
            string category = "Working";

            if (status.Contains("Checking") || status.Contains("Requesting"))
            { displayStatus = "Preparing"; category = "Working"; }
            else if (status.Contains("Downloading") || status.Contains("download"))
            { displayStatus = "Downloading"; category = "Downloading"; }
            else if (status.Contains("Extracting") || status.Contains("extract"))
            { displayStatus = "Installing"; category = "Working"; }
            else if (status.Contains("Installing") || status.Contains("Configuring") ||
                     status.Contains("Creating") || status.Contains("Registering") ||
                     status.Contains("Replacing") || status.Contains("Finalizing") ||
                     status.Contains("Cleaning") || status.Contains("Running") ||
                     status.Contains("Core installed") ||
                     status.Contains("Verifying"))
            { displayStatus = "Installing"; category = "Working"; }
            else if (status.StartsWith("✓") || status.Contains("Completed") || status.Contains("Already installed"))
            { displayStatus = "Completed"; category = "Completed"; }
            else if (status.StartsWith("⚠") || status.Contains("Error") ||
                     status.Contains("not found") || status.Contains("failed"))
            { displayStatus = "Failed"; category = "Failed"; }
            else if (status.Contains("Waiting"))
            { displayStatus = "Waiting"; category = "Waiting"; }

            // Color by category
            Color statusColor = CodeNixTheme.TextTertiary;

            if (category == "Waiting")
                statusColor = CodeNixTheme.TextTertiary;
            else if (category == "Downloading")
                statusColor = CodeNixTheme.Accent;
            else if (category == "Working")
                statusColor = CodeNixTheme.Accent;
            else if (category == "Completed")
                statusColor = CodeNixTheme.Success;
            else if (category == "Failed")
                statusColor = CodeNixTheme.Error;

            // Show simplified status text
            statusLabel.Text = displayStatus;
            statusLabel.ForeColor = statusColor;

            progressBar.IsIndeterminate = isIndeterminate &&
                category != "Completed" && category != "Failed";
            if (category == "Failed")
                progressBar.Value = progressBar.Minimum;
            else
                progressBar.Value = Math.Max(progressBar.Value, Math.Max(progressBar.Minimum,
                    Math.Min(progress, progressBar.Maximum)));

            overallStatusOverride = null;

            UpdateOverallProgress();
        }

        private void StartElapsedTimer()
        {
            if (elapsedTimer != null)
            {
                elapsedTimer.Stop();
                elapsedTimer.Dispose();
            }

            elapsedTimer = new System.Windows.Forms.Timer();
            elapsedTimer.Interval = 1000; // Update every 1 second
            elapsedTimer.Tick += (s, e) => UpdateElapsedTimeDisplay();
            elapsedTimer.Start();
        }

        private void StopElapsedTimer()
        {
            if (elapsedTimer != null)
            {
                elapsedTimer.Stop();
                elapsedTimer.Dispose();
                elapsedTimer = null;
            }
        }

        private void UpdateElapsedTimeDisplay()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateElapsedTimeDisplay));
                return;
            }

            if (installTimer != null && installTimer.IsRunning && elapsedTimeLabel != null)
            {
                var elapsed = installTimer.Elapsed;
                elapsedTimeLabel.Text = "Elapsed (mm:ss): " + FormatElapsedForUi(elapsed);
            }
        }

        private void UpdateOverallProgress()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateOverallProgress));
                return;
            }

            double totalProgress =
                pwshProgressBar.Value * 0.30 +
                vscodeProgressBar.Value * 0.40 +
                pythonProgressBar.Value * 0.25;
            int avgProgress = (int)Math.Min(totalProgress / 0.95, 100);
            overallProgress.Value = Math.Max(overallProgress.Value, avgProgress);

            // Count user-visible packages (progress >= 100)
            int completed = 0;
            if (pwshProgressBar.Value >= 100) completed++;
            if (vscodeProgressBar.Value >= 100) completed++;
            if (pythonProgressBar.Value >= 100) completed++;

            if (completed != completedComponents)
                completedComponents = completed;

            UpdateCompletedLabel();
            RefreshOverallStatusLabel();
        }

        private void UpdateCompletedLabel()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateCompletedLabel));
                return;
            }

            if (totalComponents > 0)
                completedLabel.Text = string.Format("Overall Progress: {0}% complete ({1}/{2} ready)", overallProgress.Value, completedComponents, totalComponents);
            else
                completedLabel.Text = string.Format("Overall Progress: {0}% complete", overallProgress.Value);
        }

        private void ResetProgressUI()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(ResetProgressUI));
                return;
            }

            pwshProgressBar.Value = 0;
            vscodeProgressBar.Value = 0;
            pythonProgressBar.Value = 0;
            pwshProgressBar.IsIndeterminate = false;
            vscodeProgressBar.IsIndeterminate = false;
            pythonProgressBar.IsIndeterminate = false;
            overallProgress.Value = 0;
            pwshStatusLabel.Text = "Waiting";
            vscodeStatusLabel.Text = "Waiting";
            pythonStatusLabel.Text = "Waiting";
            pwshStatusLabel.ForeColor = CodeNixTheme.TextTertiary;
            vscodeStatusLabel.ForeColor = CodeNixTheme.TextTertiary;
            pythonStatusLabel.ForeColor = CodeNixTheme.TextTertiary;
            completedComponents = 0;
            overallStatusOverride = null;
            UpdateOverallProgress();
        }

        private void UpdateCurrentTask(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateCurrentTask(message)));
                return;
            }

            overallStatusOverride = SimplifyOverallStatusMessage(message);
            currentTaskLabel.Text = overallStatusOverride;
        }

        private void LogActivity(string message, bool fileOnly = false)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string elapsed = "";
            if (installTimer != null && installTimer.IsRunning)
            {
                var e = installTimer.Elapsed;
                elapsed = string.Format(" +{0:D2}:{1:D2}", (int)e.TotalMinutes, e.Seconds);
            }
            string logEntry = "[" + timestamp + elapsed + "] " + message + "\r\n";

            // Always append to log file buffer
            lock (logFileLock)
            {
                logFileBuffer.Append(logEntry);
            }

            // Skip UI update if fileOnly is true (performance optimization)
            if (fileOnly) return;

            string uiMessage = SimplifyActivityMessage(message);
            if (string.IsNullOrEmpty(uiMessage))
                return;

            string uiEntry = "[" + timestamp + "] " + uiMessage;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => AppendActivityLogEntry(uiEntry)));
                return;
            }

            AppendActivityLogEntry(uiEntry);
        }

        private string FormatElapsedForUi(TimeSpan elapsed)
        {
            return string.Format("{0:D2}:{1:D2}", (int)elapsed.TotalMinutes, elapsed.Seconds);
        }

        private void RefreshOverallStatusLabel()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(RefreshOverallStatusLabel));
                return;
            }

            if (!string.IsNullOrEmpty(overallStatusOverride))
            {
                currentTaskLabel.Text = overallStatusOverride;
                return;
            }

            currentTaskLabel.Text = BuildOverallStatusMessage();
        }

        private string BuildOverallStatusMessage()
        {
            var rows = new[]
            {
                Tuple.Create("VS Code", vscodeStatusLabel.Text, vscodeProgressBar.Value, 4),
                Tuple.Create("PowerShell 7", pwshStatusLabel.Text, pwshProgressBar.Value, 3),
                Tuple.Create("Python", pythonStatusLabel.Text, pythonProgressBar.Value, 2)
            };

            var failed = rows
                .Where(row => row.Item3 < 100 && string.Equals(row.Item2, "Failed", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(row => row.Item4)
                .FirstOrDefault();
            if (failed != null)
                return "Check: one or more components need attention";

            if (completedComponents == totalComponents && totalComponents > 0)
                return "Done: Portable environment ready";

            string[] activePackages = rows
                .Where(row => row.Item3 < 100 &&
                    !string.Equals(row.Item2, "Waiting", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(row.Item2, "Failed", StringComparison.OrdinalIgnoreCase))
                .Select(row => row.Item1)
                .ToArray();
            if (activePackages.Length > 0)
                return "In progress: " + string.Join(", ", activePackages);

            if (overallProgress.Value >= 95)
                return "Now: Finalizing portable setup";

            if (overallProgress.Value > 0)
                return "Now: Installing components in parallel";

            if (installStarted && !installCompleted)
                return "Now: Preparing installation";

            return "Ready to install";
        }

        private string SimplifyOverallStatusMessage(string message)
        {
            string normalized = (message ?? "").Trim();
            if (string.IsNullOrEmpty(normalized))
                return "Ready to install";

            if (normalized.StartsWith("Now:") || normalized.StartsWith("Done:") || normalized.StartsWith("Check:") || normalized.StartsWith("Queued:"))
                return normalized;

            if (normalized.IndexOf("Creating installation directory", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Now: Preparing install directory";
            if (normalized.IndexOf("Creating configuration files", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Now: Finalizing portable setup";
            if (normalized.IndexOf("Cancelling installation", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Now: Cleaning up cancelled install";
            if (normalized.IndexOf("Retrying PowerShell 7", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Now: Retrying installation";
            if (normalized.IndexOf("Retrying failed components", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Now: Retrying installation";
            if (normalized.IndexOf("Installation completed successfully", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("All components installed successfully", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Done: Portable environment ready";
            if (normalized.IndexOf("Installation incomplete", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Check: one or more components need attention";
            if (normalized.IndexOf("Retry failed", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Check: retry failed";
            if (normalized.IndexOf("Installation error", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Check: installation error";

            return normalized.TrimStart('✓', '⚠', ' ');
        }

        private string SimplifyActivityMessage(string message)
        {
            string normalized = (message ?? "").Trim();
            if (string.IsNullOrEmpty(normalized))
                return null;

            normalized = normalized.Replace("VSCode", "VS Code");

            if (normalized.IndexOf("Stack trace:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("Downloaded version info page", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("Looking for version", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("Parallel extraction using", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("Created directory:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("pwsh.exe extracted successfully", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("Skipping recursive unblock", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("Settings created", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("Received version info -", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("pwsh.exe verified successfully", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("Download verified", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("CLI detected", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("Found launcher resource:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("Launcher extracted to:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("codenix.info created successfully", StringComparison.OrdinalIgnoreCase) >= 0)
                return null;

            if (Regex.IsMatch(normalized, @"Downloading\.\.\.\s+\d+%", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(normalized, @"VS Code:\s+\[\d+/\d+\]\s+.+\s+OK$", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(normalized, @"VS Code:\s+Final retry\s+--\s+.+\.\.\.$", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(normalized, @"VS Code:\s+Downloading VSIX\s+--", RegexOptions.IgnoreCase))
                return null;

            if (normalized.StartsWith("=== Installation started at ", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("=== Installation completed at ", StringComparison.OrdinalIgnoreCase))
                return normalized.Trim('=').Trim();

            if (normalized.StartsWith("*** Total installation time:", StringComparison.OrdinalIgnoreCase))
                return "Total time: " + FormatDurationFromLog(normalized);

            if (normalized.StartsWith("Phase 1A:", StringComparison.OrdinalIgnoreCase))
                return "Phase 1A: Fonts and Python";
            if (normalized.StartsWith("Phase 1B:", StringComparison.OrdinalIgnoreCase))
                return "Phase 1B: VS Code and PowerShell 7";

            var pythonVersionMatch = Regex.Match(normalized, @"^Python:\s+Found version\s+(.+)$", RegexOptions.IgnoreCase);
            if (pythonVersionMatch.Success)
                return "Python: Selected " + pythonVersionMatch.Groups[1].Value.Trim();

            var pipVersionMatch = Regex.Match(normalized, @"^Python:\s+Latest pip wheel\s+=\s+(.+)$", RegexOptions.IgnoreCase);
            if (pipVersionMatch.Success)
                return "Python: Selected pip " + pipVersionMatch.Groups[1].Value.Trim();

            var pipReadyMatch = Regex.Match(normalized, @"^Python:\s+pip verified\s+-\s+pip\s+([^\s]+)", RegexOptions.IgnoreCase);
            if (pipReadyMatch.Success)
                return "Python: pip ready (" + pipReadyMatch.Groups[1].Value.Trim() + ")";

            var powerShellReadyMatch = Regex.Match(normalized, @"^PowerShell 7:\s+Core installation completed\s+-\s+version\s+(.+)$", RegexOptions.IgnoreCase);
            if (powerShellReadyMatch.Success)
                return "PowerShell 7: Core ready (" + powerShellReadyMatch.Groups[1].Value.Trim() + ")";

            var genericInstalledMatch = Regex.Match(normalized, @"^(Oh My Posh|Terminal-Icons|PSFzf|modern-unix-win):\s+(Successfully installed version|Installed)\s+(.+)$", RegexOptions.IgnoreCase);
            if (genericInstalledMatch.Success)
                return genericInstalledMatch.Groups[1].Value + ": Installed " + genericInstalledMatch.Groups[3].Value.Trim();

            if (normalized.Equals("PowerShell 7: All modules and Oh My Posh installed", StringComparison.OrdinalIgnoreCase))
                return "PowerShell 7: Add-ons installed";
            if (normalized.Equals("PowerShell 7: All components installed", StringComparison.OrdinalIgnoreCase))
                return "PowerShell 7: Ready";
            if (normalized.Equals("VS Code: Core installation completed", StringComparison.OrdinalIgnoreCase))
                return "VS Code: Core ready";
            if (normalized.Equals("VS Code: Installation completed", StringComparison.OrdinalIgnoreCase))
                return "VS Code: Ready";
            if (normalized.Equals("VS Code: Installing extensions by direct VSIX extraction...", StringComparison.OrdinalIgnoreCase))
                return "VS Code: Installing extensions";

            var extensionDownloadMatch = Regex.Match(normalized, @"^VS Code:\s+Downloading latest VSIX packages in parallel for\s+(\d+)\s+extension\(s\)\.\.\.$", RegexOptions.IgnoreCase);
            if (extensionDownloadMatch.Success)
                return "VS Code: Downloading " + extensionDownloadMatch.Groups[1].Value + " extensions";

            if (normalized.Equals("Creating codenix.info file...", StringComparison.OrdinalIgnoreCase))
                return "Finalizing setup metadata";
            if (normalized.Equals("Extracting launcher.exe from embedded resources...", StringComparison.OrdinalIgnoreCase))
                return "Finalizing launcher";

            return normalized;
        }

        private string FormatDurationFromLog(string message)
        {
            var match = Regex.Match(message, @"(\d{2}):(\d{2}):(\d{2})");
            if (!match.Success)
                return message;

            int hours = int.Parse(match.Groups[1].Value);
            int minutes = int.Parse(match.Groups[2].Value);
            int seconds = int.Parse(match.Groups[3].Value);
            return string.Format("{0:D2}:{1:D2}", (hours * 60) + minutes, seconds);
        }

        private void AppendActivityLogEntry(string entry)
        {
            if (string.IsNullOrEmpty(entry))
                return;

            if (activityDisplayEntries.Count > 0 && activityDisplayEntries.Last() == entry)
                return;

            activityDisplayEntries.Enqueue(entry);
            while (activityDisplayEntries.Count > MAX_ACTIVITY_LOG_LINES)
                activityDisplayEntries.Dequeue();

            activityLog.Lines = activityDisplayEntries.ToArray();
            activityLog.SelectionStart = activityLog.Text.Length;
            activityLog.ScrollToCaret();
        }

        private void SaveInstallLog(bool preserveOutsideInstallDir = false)
        {
            try
            {
                string logPath;
                if (preserveOutsideInstallDir)
                {
                    string logDirectory = Path.Combine(Path.GetTempPath(), "CodeNix", "logs");
                    Directory.CreateDirectory(logDirectory);
                    logPath = Path.Combine(logDirectory,
                        "installation-" + installStartTime.ToString("yyyyMMdd-HHmmss") + ".log");
                }
                else
                {
                    logPath = Path.Combine(installDir, CommonHelper.RuntimeLogFileName);
                }
                string logHeader = "\r\n" + new string('=', 80) + "\r\n";
                logHeader += "Installation Session: " + installStartTime.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n";
                logHeader += new string('=', 80) + "\r\n";

                string logContent;
                lock (logFileLock)
                {
                    logContent = logFileBuffer.ToString();
                }

                // Append to existing log file or create new one
                File.AppendAllText(logPath, logHeader + logContent + "\r\n", Encoding.UTF8);
                LogActivity("Installation log saved to: " + logPath);
            }
            catch (Exception ex)
            {
                LogActivity("Failed to save installation log: " + ex.Message);
            }
        }

        private void ShowError(string message)
        {
            Invoke(new Action(() =>
            {
                MessageBox.Show(this, message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }));
        }

        private async void RetryInstallation()
        {
            if (retryOnlyPowerShell)
            {
                if (installStarted && !installCompleted)
                    return;

                if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir))
                {
                    MessageBox.Show(this, "The partial installation folder is no longer available.",
                        "Retry Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                LogActivity("=== Retrying failed PowerShell components ===");
                criticalFailed = false;
                pwsh7Failed = false;
                installCompleted = false;
                installStarted = true;
                installationSucceeded = false;
                installDirectoryWasEmptyAtStart = false;
                installPathTextBox.Enabled = false;
                browseButton.Enabled = false;
                pythonVersionTextBox.Enabled = false;
                SetInstallButtonInstallingState();
                cancelButton.Text = "Cancel";
                cancelButton.Enabled = true;
                CodeNixTheme.StylePrimaryButton(cancelButton);
                UpdateComponentStatus("PowerShell 7", "Retrying failed components...", 0, true);
                UpdateCurrentTask("Retrying PowerShell 7 components...");

                if (installCancellationSource != null)
                    installCancellationSource.Dispose();
                installCancellationSource = new System.Threading.CancellationTokenSource();
                if (installTimer != null)
                    installTimer.Stop();
                installTimer = System.Diagnostics.Stopwatch.StartNew();
                installStartTime = DateTime.Now;
                StartElapsedTimer();

                var token = installCancellationSource.Token;
                installationTask = Task.Run(() => PerformInstallation(
                    token, pythonVersionTextBox.Text.Trim(), true));
                await installationTask;
                return;
            }

            LogActivity("=== Retrying full installation ===");
            criticalFailed = false;
            pwsh7Failed = false;
            installCompleted = false;
            installStarted = false;
            installationSucceeded = false;
            RestoreInstallButtonStyle("Install");
            InstallButton_Click(installButton, EventArgs.Empty);
        }

        // Installation methods
        private Dictionary<string, string> InstallFonts(System.Threading.CancellationToken cancellationToken)
        {
            var versions = new Dictionary<string, string>();

            // Check for cancellation
            if (cancellationToken.IsCancellationRequested)
            {
                LogActivity("Fonts: Installation cancelled");
                return versions;
            }

            LogActivity("Fonts: Starting installation...");

            try
            {
                string fontDir = Path.Combine(installDir, "data", "lib", "fonts");
                Directory.CreateDirectory(fontDir);

                bool hasNerd = File.Exists(Path.Combine(fontDir, "0xProtoNerdFont-Regular.ttf"));
                bool hasDalseo = File.Exists(Path.Combine(fontDir, "DalseoHealingMedium.ttf"));

                if (hasNerd && hasDalseo)
                {
                    versions["FONTS_VERSION"] = "embedded";
                    LogActivity("Fonts: Already installed");
                    return versions;
                }

                // Extract 0xProto Nerd Font from embedded resource
                if (!hasNerd)
                {
                    LogActivity("Fonts: Extracting 0xProtoNerdFont-Regular.ttf...");
                    var assembly = Assembly.GetExecutingAssembly();
                    using (var stream = assembly.GetManifestResourceStream("0xProtoNerdFont-Regular.ttf"))
                    {
                        if (stream != null)
                        {
                            string nerdPath = Path.Combine(fontDir, "0xProtoNerdFont-Regular.ttf");
                            using (var fileStream = File.Create(nerdPath))
                            {
                                stream.CopyTo(fileStream);
                            }
                        }
                    }
                }

                // Extract DalseoHealing Font from embedded resource
                if (!hasDalseo)
                {
                    LogActivity("Fonts: Extracting DalseoHealingMedium.ttf...");
                    var assembly = Assembly.GetExecutingAssembly();
                    using (var stream = assembly.GetManifestResourceStream("DalseoHealingMedium.ttf"))
                    {
                        if (stream != null)
                        {
                            string dalseoPath = Path.Combine(fontDir, "DalseoHealingMedium.ttf");
                            using (var fileStream = File.Create(dalseoPath))
                            {
                                stream.CopyTo(fileStream);
                            }
                        }
                    }
                }

                // Register fonts in user registry
                string nerdFontPath = Path.Combine(fontDir, "0xProtoNerdFont-Regular.ttf");
                string dalseoFontPath = Path.Combine(fontDir, "DalseoHealingMedium.ttf");

                if (File.Exists(nerdFontPath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "reg",
                        Arguments = "add \"HKCU\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Fonts\" /v \"0xProto Nerd Font Mono\" /d \"" + nerdFontPath + "\" /f",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (var process = Process.Start(psi))
                    {
                        if (process == null)
                            throw new InvalidOperationException("Could not start font registration.");
                        process.WaitForExit();
                        if (process.ExitCode != 0)
                            throw new InvalidOperationException("Nerd Font registration failed.");
                    }
                }

                if (File.Exists(dalseoFontPath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "reg",
                        Arguments = "add \"HKCU\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Fonts\" /v \"DalseoHealing\" /d \"" + dalseoFontPath + "\" /f",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (var process = Process.Start(psi))
                    {
                        if (process == null)
                            throw new InvalidOperationException("Could not start font registration.");
                        process.WaitForExit();
                        if (process.ExitCode != 0)
                            throw new InvalidOperationException("Dalseo font registration failed.");
                    }
                }

                if (!File.Exists(nerdFontPath) || !File.Exists(dalseoFontPath))
                    throw new FileNotFoundException("One or more embedded fonts could not be installed.");

                versions["FONTS_VERSION"] = "embedded";
                LogActivity("Fonts: Installation completed");
            }
            catch (Exception ex)
            {
                string errMsg = ex.Message;
                if (ex.InnerException != null)
                    errMsg += " → " + ex.InnerException.Message;
                LogActivity("Fonts: FAILED - " + errMsg);
                LogActivity("Fonts: Stack trace: " + ex.StackTrace, true);
            }

            return versions;
        }

        // Wrapper methods for new parallel structure
        private async Task<Dictionary<string, string>> InstallPowerShell7Async(System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                var versions = await InstallPowerShell7CoreAsync(cancellationToken);
                if (cancellationToken.IsCancellationRequested || pwsh7Failed || !versions.ContainsKey("PWSH7_VERSION"))
                    return versions;

                var addonVersions = await InstallPowerShell7AddonsAsync(cancellationToken);
                foreach (var kvp in addonVersions)
                    versions[kvp.Key] = kvp.Value;

                return versions;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                UpdateComponentStatus("PowerShell 7", "⚠ " + ex.Message, 0);
                throw;
            }
        }

        private async Task<Dictionary<string, string>> InstallVSCodeFlowAsync(System.Threading.CancellationToken cancellationToken)
        {
            var versions = await InstallVSCodeCoreAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested || !versions.ContainsKey("VSCODE_VERSION"))
                return versions;

            var extensionVersions = await InstallVSCodeExtensionsAsync(cancellationToken);
            foreach (var kvp in extensionVersions)
                versions[kvp.Key] = kvp.Value;

            return versions;
        }

        private async Task<Dictionary<string, string>> InstallPowerShell7CoreAsync(System.Threading.CancellationToken cancellationToken)
        {
            var versions = new Dictionary<string, string>();

            // Check for cancellation
            if (cancellationToken.IsCancellationRequested)
            {
                LogActivity("PowerShell 7: Installation cancelled");
                return versions;
            }

            // STEP 1: Install PowerShell 7 core first (MUST complete before modules)
            LogActivity("PowerShell 7: Installing core...");
            var pwshResult = await InstallPowerShell7(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Check if installation failed
            if (string.IsNullOrEmpty(pwshResult.Value))
            {
                LogActivity("PowerShell 7: Installation failed - will allow retry after other components");
                UpdateComponentStatus("PowerShell 7", "⚠ Failed (will retry)", 0);
                pwsh7Failed = true;
                return versions;
            }

            versions[pwshResult.Key] = pwshResult.Value;
            LogActivity("PowerShell 7: Core installation completed - version " + pwshResult.Value);

            // Verify PowerShell 7 is installed (optimized polling)
            string pwshExe = Path.Combine(installDir, "data", "lib", "pwsh", "pwsh.exe");
            int retries = 0;
            while (!File.Exists(pwshExe) && retries < 20 && !cancellationToken.IsCancellationRequested)
            {
                if (retries == 0)
                    LogActivity("PowerShell 7: Verifying installation...");
                else if (retries % 5 == 0)
                    LogActivity("PowerShell 7: Still waiting for pwsh.exe... (" + (retries * 200) + "ms)");
                await Task.Delay(200);  // Check every 200ms instead of 2000ms
                retries++;
            }

            if (!File.Exists(pwshExe))
            {
                LogActivity("PowerShell 7: CRITICAL ERROR - pwsh.exe not found at: " + pwshExe);
                LogActivity("PowerShell 7: Installation verification failed - will allow retry after other components");
                UpdateComponentStatus("PowerShell 7", "⚠ Failed (will retry)", 0);
                pwsh7Failed = true;
                return versions;
            }

            LogActivity("PowerShell 7: pwsh.exe verified successfully");
            UpdateComponentStatus("PowerShell 7", "Downloading pip.ps1...", 58, true);
            await InstallPipWrapper(cancellationToken);
            UpdateComponentStatus("PowerShell 7", "Core installed", 60);

            return versions;
        }

        private async Task<Dictionary<string, string>> InstallPowerShell7AddonsAsync(System.Threading.CancellationToken cancellationToken)
        {
            var versions = new Dictionary<string, string>();

            if (cancellationToken.IsCancellationRequested)
            {
                LogActivity("PowerShell 7: Installation cancelled before addons");
                return versions;
            }

            string pwshExe = Path.Combine(installDir, "data", "lib", "pwsh", "pwsh.exe");
            if (!File.Exists(pwshExe))
            {
                LogActivity("PowerShell 7: Addons skipped - pwsh.exe not found");
                return versions;
            }

            LogActivity("PowerShell 7: Starting modules and Oh My Posh in parallel...");
            UpdateComponentStatus("PowerShell 7", "Installing modules...", 60, true);

            var parallelTasks = new List<Task<KeyValuePair<string, string>>>();

            parallelTasks.Add(InstallOhMyPosh(cancellationToken));
            parallelTasks.Add(InstallModule("PSFzf", "PSFZF_VERSION", cancellationToken));
            parallelTasks.Add(InstallModule("modern-unix-win", "MODERN_UNIX_WIN_VERSION", cancellationToken));

            int completedTasks = 0;
            int totalTasks = parallelTasks.Count;
            var runningTasks = new List<Task<KeyValuePair<string, string>>>(parallelTasks);
            Exception firstTaskError = null;

            while (runningTasks.Count > 0)
            {
                var completed = await Task.WhenAny(runningTasks);
                runningTasks.Remove(completed);
                completedTasks++;

                int progress = 60 + (30 * completedTasks / totalTasks);
                UpdateComponentStatus("PowerShell 7",
                    "Installing modules... (" + completedTasks + "/" + totalTasks + ")",
                    progress,
                    completedTasks < totalTasks);

                try
                {
                    var result = await completed;
                    LogActivity("PowerShell 7: Received version info - " + result.Key + " = " + result.Value);
                    if (!string.IsNullOrEmpty(result.Value))
                        versions[result.Key] = result.Value;
                }
                catch (Exception ex)
                {
                    firstTaskError = ex;
                    break;
                }
            }

            if (firstTaskError != null)
            {
                try { await Task.WhenAll(parallelTasks).ConfigureAwait(false); }
                catch { }
                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException("A PowerShell addon installation failed.", firstTaskError);
            }

            cancellationToken.ThrowIfCancellationRequested();
            LogActivity("PowerShell 7: All modules and Oh My Posh installed");

            // STEP 3: Install Oh My Posh theme after Oh My Posh is ready
            UpdateComponentStatus("PowerShell 7", "Installing theme...", 95, true);
            await InstallOhMyPoshTheme(cancellationToken);

            // STEP 4: Create PowerShell profile
            UpdateComponentStatus("PowerShell 7", "Creating profile...", 98, true);
            await CreatePowerShellProfile(cancellationToken);

            // Mark PowerShell 7 as 100% complete
            UpdateComponentStatus("PowerShell 7", "✓ Completed", 100);
            LogActivity("PowerShell 7: All components installed");

            return versions;
        }

        private async Task<Dictionary<string, string>> InstallVSCodeCoreAsync(System.Threading.CancellationToken cancellationToken)
        {
            var versions = new Dictionary<string, string>();
            string componentName = "VSCode";

            // Check for cancellation
            if (cancellationToken.IsCancellationRequested)
            {
                LogActivity("VSCode: Installation cancelled");
                return versions;
            }

            try
            {
                UpdateComponentStatus(componentName, "Checking version...", 5, true);
                LogActivity("VSCode: Checking latest version...");

                string version = "";
                string downloadUrl = VSCODE_LATEST_DOWNLOAD_URL;

                using (var versionClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
                {
                    versionClient.DefaultRequestHeaders.Add("User-Agent", "VSCode-Portable-Installer");
                    versionClient.Timeout = TimeSpan.FromMinutes(15);

                    using (var response = await CommonHelper.SendHeadRequestWithRetryAsync(
                        versionClient,
                        VSCODE_LATEST_DOWNLOAD_URL,
                        cancellationToken).ConfigureAwait(false))
                    {
                        string location = response.Headers.Location != null
                            ? response.Headers.Location.ToString()
                            : "";

                        var match = Regex.Match(location, @"VSCode-win32-x64-([\d\.]+)\.zip");
                        if (!match.Success)
                        {
                            LogActivity("VSCode: Could not determine version from redirect, falling back to latest download URL");
                        }
                        else
                        {
                            version = match.Groups[1].Value;
                            downloadUrl = "https://update.code.visualstudio.com/" + version + "/win32-x64-archive/stable";
                            LogActivity("VSCode: Version = " + version);
                        }
                    }
                }

                string tempDir = Path.Combine(Path.GetTempPath(), "vscode-pvs-install", "vscode");
                // Clean up temp dir - if locked, use a new unique path
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); }
                    catch
                    {
                        tempDir = Path.Combine(Path.GetTempPath(), "vscode-pvs-install", "vscode-" + DateTime.Now.Ticks);
                        LogActivity("VSCode: Previous temp locked, using: " + tempDir);
                    }
                }
                Directory.CreateDirectory(tempDir);
                string zipPath = Path.Combine(tempDir, "vscode.zip");

                UpdateComponentStatus(componentName, "Downloading...", 10, true);
                if (!string.IsNullOrEmpty(version))
                    LogActivity("VSCode: Downloading version " + version + "...");
                else
                    LogActivity("VSCode: Downloading latest stable archive...");

                bool downloadSucceeded = false;
                for (int attempt = 1; attempt <= VSCODE_DOWNLOAD_RETRY_COUNT && !downloadSucceeded; attempt++)
                {
                    if (attempt > 1)
                    {
                        LogActivity("VSCode: Retrying download (attempt " + attempt + "/" + VSCODE_DOWNLOAD_RETRY_COUNT + ")...");
                        try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
                    }

                    try
                    {
                        await DownloadFileWithProgress(downloadUrl, zipPath, componentName, 10, 55,
                            cancellationToken);
                        downloadSucceeded = File.Exists(zipPath) && new FileInfo(zipPath).Length >= 1000000;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        LogActivity("VSCode: Download attempt " + attempt + " failed - " + ex.Message);
                    }
                }

                if (!downloadSucceeded)
                {
                    LogActivity("VSCode: Download file missing or too small (" + (File.Exists(zipPath) ? new FileInfo(zipPath).Length + " bytes" : "not found") + ")");
                    UpdateComponentStatus(componentName, "⚠ Download failed (file too small)", 0);
                    return versions;
                }

                LogActivity("VSCode: Download completed (" + (new FileInfo(zipPath).Length / 1048576) + " MB)");
                UpdateComponentStatus(componentName, "Extracting...", 56, true);
                LogActivity("VSCode: Extracting directly to target directory...");

                // Extract directly to target directory (no intermediate copy)
                await ExtractZipWithProgress(zipPath, installDir, componentName, 56, 74, cancellationToken);

                LogActivity("VSCode: Extraction completed");
                UpdateComponentStatus(componentName, "Creating settings...", 76, true);
                LogActivity("VSCode: Creating settings.json...");
                await CreateVSCodeSettings(cancellationToken);
                LogActivity("VSCode: Settings created");

                UpdateComponentStatus(componentName, "Finalizing...", 79, true);
                LogActivity("VSCode: Skipping recursive unblock (streamed extraction produces local files)");

                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);

                if (string.IsNullOrEmpty(version))
                    version = DetectInstalledVSCodeVersion();

                UpdateComponentStatus(componentName, "Core installed", 80);
                LogActivity("VSCode: Core installation completed");
                versions["VSCODE_VERSION"] = version;
            }
            catch (Exception ex)
            {
                string errMsg = ex.Message;
                if (ex.InnerException != null)
                    errMsg += " → " + ex.InnerException.Message;
                UpdateComponentStatus(componentName, "⚠ " + errMsg, 0);
                LogActivity("VSCode: FAILED - " + errMsg);
                LogActivity("VSCode: Stack trace: " + ex.StackTrace, true);
            }

            return versions;
        }

        private string DetectInstalledVSCodeVersion()
        {
            try
            {
                string codeExe = Path.Combine(installDir, "Code.exe");
                if (!File.Exists(codeExe))
                    return "latest";

                var info = FileVersionInfo.GetVersionInfo(codeExe);
                if (!string.IsNullOrEmpty(info.ProductVersion))
                    return info.ProductVersion;
                if (!string.IsNullOrEmpty(info.FileVersion))
                    return info.FileVersion;
            }
            catch { }

            return "latest";
        }

        private async Task<Dictionary<string, string>> InstallVSCodeExtensionsAsync(System.Threading.CancellationToken cancellationToken)
        {
            var versions = new Dictionary<string, string>();

            if (cancellationToken.IsCancellationRequested)
            {
                LogActivity("VSCode: Extension installation cancelled");
                return versions;
            }

            UpdateComponentStatus("VSCode", "Installing extensions...", 80, true);
            await InstallVSCodeExtensions("VSCode", cancellationToken);
            versions["VSCODE_EXTENSIONS_VERSION"] = "installed";
            UpdateComponentStatus("VSCode", "✓ Completed", 100);
            LogActivity("VSCode: Installation completed");

            return versions;
        }

        private async Task<Dictionary<string, string>> InstallPythonCoreAsync(
            System.Threading.CancellationToken cancellationToken, string requestedPythonVersion)
        {
            var versions = new Dictionary<string, string>();
            try
            {
                UpdateComponentStatus("Python", "Checking uv release...", 5, true);
                UpdateCurrentTask("Checking the uv release...");
                LogActivity("Python: Installing latest stable uv...");
                string uvVersion = await PortablePython.InstallUvAsync(installDir, null, cancellationToken,
                    message => LogActivity("uv: " + message),
                    (downloaded, total) =>
                    {
                        if (total <= 0)
                        {
                            UpdateComponentStatus("Python", "Downloading uv...", 8, true);
                            UpdateCurrentTask("Downloading uv...");
                            return;
                        }
                        int progress = 8 + (int)(32.0 * downloaded / total);
                        UpdateComponentStatus("Python", "Downloading uv...", Math.Min(progress, 40), downloaded == 0);
                        UpdateCurrentTask("Downloading uv...");
                    });
                versions["UV_VERSION"] = uvVersion;
                UpdateComponentStatus("Python", "Resolving CPython version...", 45, true);
                UpdateCurrentTask("Resolving the requested CPython version...");
                string pythonVersion = await PortablePython.InstallPythonAsync(installDir, requestedPythonVersion, cancellationToken,
                    version =>
                    {
                        LogActivity("Python: Selected stable CPython " + version);
                        UpdateComponentStatus("Python", "Installing CPython " + version + "...", 55, true);
                        UpdateCurrentTask("Installing CPython " + version + " (network speed may affect duration)...");
                        if (!IsDisposed && IsHandleCreated)
                           Invoke(new Action(() =>
                           {
                               pythonVersionTextBox.Text = version;
                               pythonHint.Text = string.Empty;
                           }));
                    }, message => LogActivity("Python: " + message),
                    progress =>
                    {
                        int mappedProgress = 55 + (int)(25.0 * progress / 100);
                        UpdateComponentStatus("Python", "Downloading CPython " + cachedPythonVersion + "...",
                            mappedProgress);
                        UpdateCurrentTask("Downloading CPython " + cachedPythonVersion + "...");
                    });
                UpdateComponentStatus("Python", "Creating global pip environment...", 85, true);
                UpdateCurrentTask("Creating the global Python environment...");
                string globalEnvironment = await PortablePython.EnsureGlobalEnvironmentAsync(installDir, pythonVersion,
                    cancellationToken, message => LogActivity("Python: " + message));
                LogActivity("Python: Global pip environment ready at " + globalEnvironment);
                versions["PYTHON_VERSION"] = pythonVersion;
                Directory.CreateDirectory(Path.Combine(installDir, "projects"));
                UpdateComponentStatus("Python", "Completed", 100);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LogActivity("Python: Installation failed - " + ex.Message);
                UpdateComponentStatus("Python", "Failed: " + ex.Message, 0);
            }
            return versions;
        }

        private async Task<KeyValuePair<string, string>> InstallPowerShell7(System.Threading.CancellationToken cancellationToken)
        {
            var installer = new PowerShellInstaller(
                installDir,
                (name, status, progress) =>
                {
                    bool isIndeterminate =
                        status.IndexOf("Checking", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (status.IndexOf("Downloading", StringComparison.OrdinalIgnoreCase) >= 0 && progress <= 10) ||
                        (status.IndexOf("Extracting", StringComparison.OrdinalIgnoreCase) >= 0 && progress <= 45) ||
                        (status.IndexOf("Installing", StringComparison.OrdinalIgnoreCase) >= 0 && progress >= 90) ||
                        status.IndexOf("Replacing", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (name == "PowerShell 7")
                    {
                        int mappedProgress = (int)Math.Round(progress * 0.60);
                        UpdateComponentStatus(name, status, mappedProgress, isIndeterminate);
                    }
                    else
                    {
                        UpdateComponentStatus(name, status, progress, isIndeterminate);
                    }
                },
                (message) => LogActivity(message)
            );

            return await installer.InstallAsync(Path.Combine(Path.GetTempPath(), TEMP_INSTALL_DIR), false, cancellationToken);
        }

        private async Task<KeyValuePair<string, string>> InstallOhMyPosh(System.Threading.CancellationToken cancellationToken)
        {
            var installer = new OhMyPoshInstaller(
                installDir,
                (name, status, progress) => UpdateComponentStatus(name, status, progress),
                (message) => LogActivity(message)
            );

            return await installer.InstallAsync(Path.Combine(Path.GetTempPath(), TEMP_INSTALL_DIR), cancellationToken);
        }

        private string GetOhMyPoshVersion(string exePath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process.WaitForExit(5000))
                    {
                        return process.StandardOutput.ReadToEnd().Trim();
                    }
                }
            }
            catch { }
            return "";
        }

        private async Task<KeyValuePair<string, string>> InstallModule(string moduleName, string versionKey, System.Threading.CancellationToken cancellationToken)
        {
            var installer = new ModuleInstaller(
                installDir,
                (name, status, progress) => UpdateComponentStatus(name, status, progress),
                (message) => LogActivity(message)
            );

            return await installer.InstallModuleAsync(moduleName, versionKey, 30, cancellationToken);
        }

        private async Task InstallOhMyPoshTheme(System.Threading.CancellationToken cancellationToken)
        {
            string themesDir = Path.Combine(installDir, "data", "lib", "pwsh", "ohmyposh", "themes");
            string themePath = Path.Combine(themesDir, "tos.omp.json");
            await DownloadUtilsFileAsync("tos.omp.json", themePath, cancellationToken);
            CommonHelper.RemoveUtf8BomFromFile(themePath);

            string originDir = Path.Combine(installDir, "data", "lib", "origin");
            Directory.CreateDirectory(originDir);
            File.Copy(themePath, Path.Combine(originDir, "tos.omp.json"), true);
            LogActivity("Oh My Posh: Theme installed");
        }

        private async Task CreatePowerShellProfile(System.Threading.CancellationToken cancellationToken)
        {
            string profilePath = Path.Combine(installDir, "data", "lib", "pwsh", "Microsoft.PowerShell_profile.ps1");
            await DownloadUtilsFileAsync("Microsoft.PowerShell_profile.ps1", profilePath, cancellationToken);

            string originDir = Path.Combine(installDir, "data", "lib", "origin");
            Directory.CreateDirectory(originDir);
            File.Copy(profilePath, Path.Combine(originDir, "Microsoft.PowerShell_profile.ps1"), true);
            LogActivity("PowerShell 7: Profile installed");
        }

        private async Task InstallVSCodeExtensions(string componentName, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
            cancellationToken.ThrowIfCancellationRequested();
                LogActivity("VSCode: Installing extensions in batch with retry...");

                string codeExe = Path.Combine(installDir, "Code.exe");
                string cliJs = Path.Combine(installDir, "resources", "app", "out", "cli.js");

                if (!File.Exists(codeExe))
                {
                    var codeCandidates = Directory.GetFiles(installDir, "Code.exe", SearchOption.AllDirectories);
                    if (codeCandidates.Length > 0)
                        codeExe = codeCandidates[0];
                }

                if (!File.Exists(cliJs))
                {
                    var cliCandidates = Directory.GetFiles(installDir, "cli.js", SearchOption.AllDirectories)
                        .Where(p => p.EndsWith(Path.Combine("resources", "app", "out", "cli.js"), StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    if (cliCandidates.Length > 0)
                        cliJs = cliCandidates[0];
                }

                bool cliAvailable = File.Exists(codeExe) && File.Exists(cliJs);
                if (cliAvailable)
                    LogActivity("VSCode: CLI detected -- Code=" + codeExe + ", cli.js=" + cliJs + " (fallback available)");
                else
                    LogActivity("VSCode: CLI tools not found; using direct extraction only");

                // Use absolute paths to avoid resolution issues
                string extensionsDir = Path.Combine(installDir, "data", "extensions");
                string userDataDir = Path.Combine(installDir, "data", "user-data");

                Directory.CreateDirectory(extensionsDir);
                Directory.CreateDirectory(userDataDir);

                var extensions = new List<string>
                {
                    "teabyii.ayu",
                    "ms-python.python",
                    "ms-python.vscode-pylance",
                    "ms-python.vscode-python-envs",
                    "ms-python.debugpy",
                    "KevinRose.vsc-python-indent",
                    "usernamehw.errorlens"
                };

                int installed = 0;
                int total = extensions.Count;
                var failedExtensions = new List<string>();
                string tempVsixDir = Path.Combine(Path.GetTempPath(), TEMP_INSTALL_DIR, "vsix-cache");
                if (Directory.Exists(tempVsixDir))
                {
                    try { Directory.Delete(tempVsixDir, true); }
                    catch { }
                }
                Directory.CreateDirectory(tempVsixDir);

                UpdateComponentStatus(componentName, "Downloading extensions...", 82, true);
                LogActivity("VSCode: Downloading latest VSIX packages in parallel for " + total + " extension(s)...");
                var downloadedPackages = await DownloadVSCodeExtensionPackagesAsync(extensions, tempVsixDir, componentName, cancellationToken);

                UpdateComponentStatus(componentName, "Installing extensions (local)...", 86, true);
                LogActivity("VSCode: Installing extensions by direct VSIX extraction...");

                foreach (var ext in extensions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool success = false;
                    string localPackagePath;
                    if (downloadedPackages.TryGetValue(ext, out localPackagePath))
                        success = TryExtractVSCodeExtensionPackage(localPackagePath, extensionsDir, ext);

                    if (!success)
                        success = IsVSCodeExtensionInstalled(extensionsDir, ext);

                    if (!success)
                        failedExtensions.Add(ext);

                    installed++;
                    int progress = 80 + (int)(16.0 * installed / total);
                    UpdateComponentStatus(componentName,
                        "Installing extensions (" + installed + "/" + total + ")...",
                        progress,
                        installed < total);
                    LogActivity("VSCode: [" + installed + "/" + total + "] " + ext + (success ? " OK" : " PENDING RETRY"));
                }

                // Final retry pass for failed extensions after network recovery delay
                if (failedExtensions.Count > 0)
                {
                    if (cliAvailable)
                    {
                        LogActivity("VSCode: Retrying " + failedExtensions.Count + " missing extension(s) via CLI...");
                        await Task.Delay(1500, cancellationToken);

                        var stillFailed = new List<string>();
                        foreach (var ext in failedExtensions)
                        {
                            bool success = false;

                            try
                            {
                                LogActivity("VSCode: Final retry -- " + ext + "...");

                                string retryInput;
                                if (ShouldRetryVSCodeExtensionById(ext) || !downloadedPackages.TryGetValue(ext, out retryInput))
                                    retryInput = ext;

                                string retryArguments = BuildVSCodeExtensionInstallArguments(cliJs, extensionsDir, userDataDir, new[] { retryInput });
                                var retryResult = await RunVSCodeCliProcess(codeExe, retryArguments, 600000, cancellationToken);
                                bool isInstalled = IsVSCodeExtensionInstalled(extensionsDir, ext);

                                if (isInstalled || (retryResult.Item4 && retryResult.Item1 == 0))
                                {
                                    success = true;
                                    LogActivity("VSCode: Final retry -- " + ext + " OK");
                                }
                                else
                                {
                                    LogActivity("VSCode: Final retry -- " + ext + " FAILED");
                                }
                            }
                            catch (Exception ex)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                    throw new OperationCanceledException(cancellationToken);
                                LogActivity("VSCode: Final retry exception for " + ext + " -- " + ex.Message);
                            }

                            if (!success)
                                stillFailed.Add(ext);

                            await Task.Delay(300, cancellationToken);
                        }

                        if (stillFailed.Count > 0)
                        {
                            LogActivity("VSCode: " + stillFailed.Count + " extension(s) could not be installed: " + string.Join(", ", stillFailed));
                            LogActivity("VSCode: These can be installed manually from within VS Code");
                        }
                        else
                        {
                            LogActivity("VSCode: All previously failed extensions recovered successfully");
                        }
                    }
                    else
                    {
                        // CLI not available: re-download and retry direct extraction
                        LogActivity("VSCode: CLI unavailable -- re-downloading " + failedExtensions.Count + " failed extension(s) for retry...");
                        await Task.Delay(2000, cancellationToken);

                        var stillFailed = new List<string>();
                        foreach (var ext in failedExtensions)
                        {
                            bool success = false;
                            try
                            {
                                        string vsixPath = Path.Combine(tempVsixDir, ext.Replace('.', '_') + "_retry.vsix");
                                    var retryHandler = new HttpClientHandler
                                    {
                                        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
                                    };
                                    using (var retryClient = new HttpClient(retryHandler))
                                    using (var retryCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                                    {
                                        retryClient.Timeout = TimeSpan.FromMinutes(MAX_DOWNLOAD_TIMEOUT_MINUTES);
                                        retryClient.DefaultRequestHeaders.Add("User-Agent", "VSCode-Portable-Installer");
                                        var retryToken = retryCts.Token;
                                        string packageUrl = await BuildVSCodeMarketplaceVsixUrlAsync(retryClient, ext, retryToken);
                                        if (!string.IsNullOrEmpty(packageUrl))
                                        {
                                                await CommonHelper.DownloadFileWithRetryAsync(
                                                    retryClient, packageUrl, vsixPath, retryToken);
                                            if (File.Exists(vsixPath) && new FileInfo(vsixPath).Length > 0 && IsValidZipArchive(vsixPath))
                                            {
                                                success = TryExtractVSCodeExtensionPackage(vsixPath, extensionsDir, ext);
                                                if (!success)
                                                    success = IsVSCodeExtensionInstalled(extensionsDir, ext);
                                            }
                                        }
                                    }
                            }
                            catch (OperationCanceledException)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                    throw;
                                LogActivity("VSCode: Re-download retry timeout (120s) for " + ext);
                            }
                            catch (TimeoutException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                LogActivity("VSCode: Re-download retry failed for " + ext + " -- " + ex.Message);
                            }
                            if (success)
                                LogActivity("VSCode: Re-download retry -- " + ext + " OK");
                            else
                                stillFailed.Add(ext);
                            await Task.Delay(300, cancellationToken);
                        }

                        if (stillFailed.Count > 0)
                        {
                            LogActivity("VSCode: " + stillFailed.Count + " extension(s) could not be installed: " + string.Join(", ", stillFailed));
                            LogActivity("VSCode: These can be installed manually from within VS Code");
                        }
                        else
                        {
                            LogActivity("VSCode: All previously failed extensions recovered successfully");
                        }
                    }
                }

                var missingExtensions = extensions
                    .Where(ext => !IsVSCodeExtensionInstalled(extensionsDir, ext))
                    .ToArray();
                if (missingExtensions.Length > 0)
                    throw new InvalidOperationException("VS Code extensions failed to install: " +
                        string.Join(", ", missingExtensions));

                RemoveVSCodeExtensionsByPrefix(extensionsDir, VSCODE_OPTIONAL_AUTO_INSTALLED_EXTENSIONS);

                LogActivity("VSCode: All extensions processed (" + installed + "/" + total + ")");

                // Remove extensions.json so VSCode regenerates it cleanly on first launch
                string extensionsJsonPath = Path.Combine(extensionsDir, "extensions.json");
                if (File.Exists(extensionsJsonPath))
                {
                    try { File.Delete(extensionsJsonPath); } catch { }
                }

                if (Directory.Exists(tempVsixDir))
                {
                    try { Directory.Delete(tempVsixDir, true); } catch { }
                }
            }
            catch (Exception ex)
            {
                LogActivity("VSCode: Extension installation error -- " + ex.Message);
                UpdateComponentStatus(componentName, "⚠ " + ex.Message, 0);
                throw;
            }
        }

        private string BuildVSCodeExtensionInstallArguments(string cliJs, string extensionsDir, string userDataDir, IEnumerable<string> extensions)
        {
            var args = new StringBuilder();
            args.Append("\"").Append(cliJs).Append("\"");
            args.Append(" --extensions-dir \"").Append(extensionsDir).Append("\"");
            args.Append(" --user-data-dir \"").Append(userDataDir).Append("\"");

            foreach (var ext in extensions)
                args.Append(" --install-extension \"").Append(ext).Append("\"");

            args.Append(" --force");
            return args.ToString();
        }

        private bool TryExtractVSCodeExtensionPackage(string vsixPath, string extensionsDir, string extensionId)
        {
            try
            {
                Tuple<string, string, string> packageInfo;
                string destinationDir = null;
                using (var archive = ZipFile.OpenRead(vsixPath))
                {
                    packageInfo = ReadVSCodeExtensionPackageInfo(archive, extensionId);
                    if (packageInfo == null)
                    {
                        LogActivity("VSCode: Extension metadata parse failed -- " + extensionId);
                        return false;
                    }

                    destinationDir = Path.Combine(extensionsDir, packageInfo.Item1 + "." + packageInfo.Item2 + "-" + packageInfo.Item3);
                    if (Directory.Exists(destinationDir))
                    {
                        try { Directory.Delete(destinationDir, true); } catch { }
                    }
                    Directory.CreateDirectory(destinationDir);

                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.FullName.StartsWith("extension/", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string relativePath = entry.FullName.Substring("extension/".Length);
                        if (string.IsNullOrEmpty(relativePath))
                            continue;

                        string destinationPath = GetSafeExtractionDestination(destinationDir, relativePath);
                        if (destinationPath == null)
                            continue;

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destinationPath);
                            continue;
                        }

                        string destinationParent = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(destinationParent))
                            Directory.CreateDirectory(destinationParent);

                        using (var input = entry.Open())
                        using (var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, EXTRACT_BUFFER_SIZE, false))
                        {
                            input.CopyTo(output, EXTRACT_BUFFER_SIZE);
                        }
                    }
                }

                if (!ValidateExtractedVSCodeExtension(destinationDir, extensionId))
                {
                    try { Directory.Delete(destinationDir, true); } catch { }
                    LogActivity("VSCode: Direct extraction validation failed -- " + extensionId);
                    return false;
                }

                bool installed = IsVSCodeExtensionInstalled(extensionsDir, extensionId);
                if (!installed)
                    LogActivity("VSCode: Direct extraction verification failed -- " + extensionId);
                return installed;
            }
            catch (Exception ex)
            {
                LogActivity("VSCode: Direct extraction failed -- " + extensionId + " -- " + ex.Message);
                return false;
            }
        }

        private bool ShouldRetryVSCodeExtensionById(string extensionId)
        {
            return VSCODE_PLATFORM_SENSITIVE_EXTENSIONS.Any(ext =>
                string.Equals(ext, extensionId, StringComparison.OrdinalIgnoreCase));
        }

        private bool ValidateExtractedVSCodeExtension(string destinationDir, string extensionId)
        {
            if (string.IsNullOrEmpty(destinationDir) || string.IsNullOrEmpty(extensionId))
                return false;

            if (!string.Equals(extensionId, "ms-python.python", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extensionId, "ms-python.vscode-python-envs", StringComparison.OrdinalIgnoreCase))
                return true;

            string toolsDir = Path.Combine(destinationDir, "python-env-tools", "bin");
            string windowsToolPath = Path.Combine(toolsDir, "pet.exe");
            if (File.Exists(windowsToolPath))
                return true;

            string extractedToolPath = Path.Combine(toolsDir, "pet");
            if (!File.Exists(extractedToolPath))
            {
                LogActivity("VSCode: Python environment tools missing after extraction -- " + extensionId);
                return false;
            }

            if (!HasPortableExecutableHeader(extractedToolPath))
            {
                LogActivity("VSCode: Python environment tools payload is not Windows-compatible -- " + extensionId);
                return false;
            }

            try
            {
                File.Copy(extractedToolPath, windowsToolPath, true);
                return true;
            }
            catch (Exception ex)
            {
                LogActivity("VSCode: Failed to materialize pet.exe -- " + extensionId + " -- " + ex.Message);
                return false;
            }
        }

        private bool HasPortableExecutableHeader(string filePath)
        {
            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length < 2)
                        return false;

                    int first = stream.ReadByte();
                    int second = stream.ReadByte();
                    return first == 'M' && second == 'Z';
                }
            }
            catch
            {
                return false;
            }
        }

        private void RemoveVSCodeExtensionsByPrefix(string extensionsDir, IEnumerable<string> extensionIds)
        {
            if (string.IsNullOrEmpty(extensionsDir) || !Directory.Exists(extensionsDir) || extensionIds == null)
                return;

            foreach (var extensionId in extensionIds)
            {
                if (string.IsNullOrEmpty(extensionId))
                    continue;

                foreach (var installedDir in Directory.GetDirectories(extensionsDir)
                    .Where(dir => Path.GetFileName(dir).StartsWith(extensionId + "-", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        Directory.Delete(installedDir, true);
                        LogActivity("VSCode: Removed optional auto-installed extension -- " + Path.GetFileName(installedDir));
                    }
                    catch (Exception ex)
                    {
                        LogActivity("VSCode: Failed to remove optional extension -- " + Path.GetFileName(installedDir) + " -- " + ex.Message);
                    }
                }
            }
        }

        private Tuple<string, string, string> ReadVSCodeExtensionPackageInfo(ZipArchive archive, string extensionId)
        {
            var packageEntry = archive.GetEntry("extension/package.json");
            if (packageEntry == null)
                return null;

            using (var reader = new StreamReader(packageEntry.Open()))
            {
                string packageJson = reader.ReadToEnd();
                string publisher = Regex.Match(packageJson, "\"publisher\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
                string name = Regex.Match(packageJson, "\"name\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
                string version = Regex.Match(packageJson, "\"version\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;

                if (string.IsNullOrEmpty(publisher) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(version))
                {
                    if (!string.IsNullOrEmpty(extensionId))
                    {
                        int dotIndex = extensionId.IndexOf('.');
                        if (dotIndex > 0 && dotIndex < extensionId.Length - 1)
                        {
                            if (string.IsNullOrEmpty(publisher))
                                publisher = extensionId.Substring(0, dotIndex);
                            if (string.IsNullOrEmpty(name))
                                name = extensionId.Substring(dotIndex + 1);
                        }
                    }
                }

                if (string.IsNullOrEmpty(publisher) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(version))
                    return null;

                return Tuple.Create(publisher, name, version);
            }
        }

        private string GetSafeExtractionDestination(string rootPath, string relativePath)
        {
            string fullRoot = Path.GetFullPath(rootPath);
            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                fullRoot += Path.DirectorySeparatorChar;

            string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                return null;

            return fullPath;
        }

        private async Task<Dictionary<string, string>> DownloadVSCodeExtensionPackagesAsync(
            IList<string> extensions, string tempVsixDir, string componentName,
            System.Threading.CancellationToken cancellationToken)
        {
            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var resultLock = new object();
            var semaphore = new System.Threading.SemaphoreSlim(VSCODE_EXTENSION_DOWNLOAD_CONCURRENCY, VSCODE_EXTENSION_DOWNLOAD_CONCURRENCY);
            int completed = 0;
            int total = extensions.Count;

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };

            using (var client = new HttpClient(handler))
            {
                client.Timeout = TimeSpan.FromMinutes(MAX_DOWNLOAD_TIMEOUT_MINUTES);
                client.DefaultRequestHeaders.ConnectionClose = false;
                client.DefaultRequestHeaders.Add("User-Agent", "VSCode-Portable-Installer");

                var downloadTasks = extensions.Select(async ext =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    using (var perExtCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        var perExtToken = perExtCts.Token;
                        try
                        {
                            string packageUrl = await BuildVSCodeMarketplaceVsixUrlAsync(client, ext, perExtToken);
                            if (string.IsNullOrEmpty(packageUrl))
                            {
                                LogActivity("VSCode: VSIX URL build failed -- " + ext);
                                return;
                            }

                            string vsixPath = Path.Combine(tempVsixDir, ext.Replace('.', '_') + ".vsix");
                            LogActivity("VSCode: Downloading VSIX -- " + ext);

                            await CommonHelper.DownloadFileWithRetryAsync(
                                client, packageUrl, vsixPath, perExtToken).ConfigureAwait(false);

                            if (File.Exists(vsixPath) && new FileInfo(vsixPath).Length > 0)
                            {
                                if (IsValidZipArchive(vsixPath))
                                {
                                    lock (resultLock)
                                    {
                                        results[ext] = vsixPath;
                                    }
                                }
                                else
                                {
                                    LogActivity("VSCode: VSIX validation failed -- " + ext + " -- downloaded file is not a valid zip archive");
                                }
                            }
                            else
                            {
                                LogActivity("VSCode: VSIX download produced empty file -- " + ext);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            if (cancellationToken.IsCancellationRequested)
                                throw;
                            LogActivity("VSCode: VSIX download timeout (45s) -- " + ext + " -- will retry later");
                        }
                        catch (Exception ex)
                        {
                            LogActivity("VSCode: VSIX download failed -- " + ext + " -- " + ex.Message);
                        }
                        finally
                        {
                            int currentCompleted;
                            lock (resultLock)
                            {
                                completed++;
                                currentCompleted = completed;
                            }

                            int progress = 82 + (int)(4.0 * currentCompleted / total);
                            UpdateComponentStatus(componentName,
                                "Downloading extensions (" + currentCompleted + "/" + total + ")...",
                                progress,
                                currentCompleted < total);
                            semaphore.Release();
                        }
                    }
                }).ToArray();

                await Task.WhenAll(downloadTasks);
            }

            LogActivity("VSCode: Downloaded " + results.Count + "/" + total + " VSIX package(s)");
            return results;
        }

        private bool IsValidZipArchive(string archivePath)
        {
            try
            {
                using (var archive = ZipFile.OpenRead(archivePath))
                {
                    return archive.Entries.Count > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> BuildVSCodeMarketplaceVsixUrlAsync(HttpClient client, string extensionId, System.Threading.CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(extensionId))
                return "";

            int separatorIndex = extensionId.IndexOf('.');
            if (separatorIndex <= 0 || separatorIndex >= extensionId.Length - 1)
                return "";

            string publisher = extensionId.Substring(0, separatorIndex);
            string extensionName = extensionId.Substring(separatorIndex + 1);

            try
            {
                string stableVersion = await QueryLatestStableExtensionVersion(client, publisher, extensionName, cancellationToken);
                if (!string.IsNullOrEmpty(stableVersion))
                {
                    string versionUrl = string.Format(VSCODE_MARKETPLACE_VSIX_VERSION_URL_TEMPLATE, publisher, extensionName, stableVersion);
                    if (ShouldRetryVSCodeExtensionById(extensionId))
                        versionUrl += "?targetPlatform=win32-x64";
                    return versionUrl;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogActivity("VSCode: Stable version query failed for " + extensionId + " -- " + ex.Message);
            }

            // Fallback to latest URL
            string fallbackUrl = string.Format(VSCODE_MARKETPLACE_VSIX_URL_TEMPLATE, publisher, extensionName);
            if (ShouldRetryVSCodeExtensionById(extensionId))
                fallbackUrl += "?targetPlatform=win32-x64";
            return fallbackUrl;
        }

        private async Task<string> QueryLatestStableExtensionVersion(HttpClient client, string publisher, string name, System.Threading.CancellationToken cancellationToken)
        {
            string body = "{\"filters\":[{\"criteria\":[{\"filterType\":7,\"value\":\"" + publisher + "." + name + "\"}],\"pageNumber\":1,\"pageSize\":1,\"sortBy\":0,\"sortOrder\":0}],\"assetTypes\":[],\"flags\":17}";
            using (var request = new HttpRequestMessage(HttpMethod.Post, VSCODE_MARKETPLACE_QUERY_URL))
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                request.Headers.TryAddWithoutValidation("Accept", "application/json;api-version=3.0-preview.1");
                using (var response = await client.SendAsync(request, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync();
                    return FindLatestStableVersion(json, ShouldRetryVSCodeExtensionById(publisher + "." + name));
                }
            }
        }

        private static string FindLatestStableVersion(string json, bool requireWindowsPlatform)
        {
            var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                .Deserialize<Dictionary<string, object>>(json);
            var result = (Dictionary<string, object>)((System.Collections.IList)root["results"])[0];
            var extension = (Dictionary<string, object>)((System.Collections.IList)result["extensions"])[0];
            foreach (var item in (System.Collections.IEnumerable)extension["versions"])
            {
                var version = (Dictionary<string, object>)item;
                object platform;
                if (requireWindowsPlatform && (!version.TryGetValue("targetPlatform", out platform) ||
                    Convert.ToString(platform) != "win32-x64"))
                    continue;
                bool prerelease = false;
                object properties;
                if (version.TryGetValue("properties", out properties))
                {
                    foreach (var property in (System.Collections.IEnumerable)properties)
                    {
                        var metadata = (Dictionary<string, object>)property;
                        if (Convert.ToString(metadata["key"]) == "Microsoft.VisualStudio.Code.PreRelease" &&
                            string.Equals(Convert.ToString(metadata["value"]), "true", StringComparison.OrdinalIgnoreCase))
                            prerelease = true;
                    }
                }
                if (!prerelease) return Convert.ToString(version["version"]);
            }
            return null;
        }

        private string BuildVSCodeMarketplaceVsixUrl(string extensionId)
        {
            if (string.IsNullOrEmpty(extensionId))
                return "";

            int separatorIndex = extensionId.IndexOf('.');
            if (separatorIndex <= 0 || separatorIndex >= extensionId.Length - 1)
                return "";

            string publisher = extensionId.Substring(0, separatorIndex);
            string extensionName = extensionId.Substring(separatorIndex + 1);
            string url = string.Format(VSCODE_MARKETPLACE_VSIX_URL_TEMPLATE, publisher, extensionName);
            if (ShouldRetryVSCodeExtensionById(extensionId))
                url += "?targetPlatform=win32-x64";
            return url;
        }

        private bool IsVSCodeExtensionInstalled(string extensionsDir, string extensionId)
        {
            return Directory.GetDirectories(extensionsDir)
                .Any(d => Path.GetFileName(d).StartsWith(extensionId + "-", StringComparison.OrdinalIgnoreCase));
        }

        private async Task<Tuple<int, string, string, bool>> RunVSCodeCliProcess(string codeExe, string arguments,
            int timeoutMs, System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var psi = new ProcessStartInfo
            {
                FileName = codeExe,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = installDir
            };

            psi.EnvironmentVariables["ELECTRON_RUN_AS_NODE"] = "1";
            psi.EnvironmentVariables["VSCODE_DEV"] = "";

            using (var process = Process.Start(psi))
            {
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                using (cancellationToken.Register(() =>
                {
                    try { if (!process.HasExited) process.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }))
                {
                    bool finished = await Task.Run(() => process.WaitForExit(timeoutMs)).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!finished)
                    {
                        try { process.Kill(); } catch { }
                    }

                    string output = await outputTask.ConfigureAwait(false);
                    string error = await errorTask.ConfigureAwait(false);
                    int exitCode = finished ? process.ExitCode : -1;
                    return Tuple.Create(exitCode, output, error, finished);
                }
            }
        }

        private async Task CreateVSCodeSettings(System.Threading.CancellationToken cancellationToken)
        {
            string settingsPath = Path.Combine(installDir, "data", "user-data", "User", "settings.json");
            await DownloadUtilsFileAsync("settings.json", settingsPath, cancellationToken);

            string originDir = Path.Combine(installDir, "data", "lib", "origin");
            Directory.CreateDirectory(originDir);
            File.Copy(settingsPath, Path.Combine(originDir, "settings.json"), true);
        }

        private async Task DownloadFileWithProgress(string url, string destinationPath,
            string componentName, int startProgress, int endProgress, System.Threading.CancellationToken cancellationToken)
        {
            int lastReportedProgress = startProgress;
            int lastLoggedPercentage = 0;
            await Aria2Downloader.DownloadFileAsync(installDir, url, destinationPath, componentName,
                cancellationToken, percentage =>
                {
                    int currentProgress = startProgress +
                        (int)((endProgress - startProgress) * percentage / 100.0);
                    if (currentProgress <= lastReportedProgress)
                        return;

                    UpdateComponentStatus(componentName,
                        string.Format("Downloading... {0}%", percentage), currentProgress);
                    if (percentage >= lastLoggedPercentage + 10)
                    {
                        LogActivity(string.Format("{0}: Downloading... {1}%", componentName, percentage),
                            fileOnly: true);
                        lastLoggedPercentage = percentage;
                    }
                    lastReportedProgress = currentProgress;
                }).ConfigureAwait(false);
        }

        private Task ExtractZipWithProgress(string zipPath, string extractPath,
            string componentName, int startProgress, int endProgress, System.Threading.CancellationToken cancellationToken)
        {
            return CommonHelper.ExtractZipWithProgressAsync(
                zipPath,
                extractPath,
                componentName,
                startProgress,
                endProgress,
                progress => UpdateComponentStatus(componentName, "Extracting...", progress),
                message => LogActivity(message),
                cancellationToken);
        }

        private async Task CreatePvsInfo(Dictionary<string, string> versions)
        {
            UpdateCurrentTask("Creating codenix.info...");
            LogActivity("Creating codenix.info file...");

            string pvsInfo = Path.Combine(installDir, CommonHelper.RuntimeInfoFileName);
            var lines = new List<string>();

            lines.Add("INSTALL_PATH=" + installDir);
            lines.Add("INSTALL_HOST_MACHINE=" + Environment.MachineName);
            lines.Add("INSTALL_USER_DESKTOP=" + Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            lines.Add("INSTALL_START_MENU=" + Environment.GetFolderPath(Environment.SpecialFolder.Programs));

            string[] order = {
                "PWSH7_VERSION",
                "OHMYPOSH_VERSION",
                "PSFZF_VERSION",
                "MODERN_UNIX_WIN_VERSION",
                "VSCODE_VERSION",
                "PYTHON_VERSION",
                "UV_VERSION"
            };

            foreach (var key in order)
            {
                if (versions.ContainsKey(key))
                    lines.Add(key + "=" + versions[key]);
            }

            string tempPath = pvsInfo + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(tempPath, lines, Encoding.UTF8);
                if (File.Exists(pvsInfo))
                    File.Replace(tempPath, pvsInfo, null);
                else
                    File.Move(tempPath, pvsInfo);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }

            LogActivity("codenix.info created successfully at: " + pvsInfo);
            await Task.CompletedTask;
        }

        private async Task CreateShortcuts()
        {
            UpdateCurrentTask("Creating shortcuts...");

            string launcherPath = Path.Combine(installDir, "launcher.exe");
            string iconPath = Path.Combine(installDir, "Code.exe");

            if (!ShortcutManager.CreateShortcuts(installDir, launcherPath, iconPath))
                LogActivity("Warning: one or more shortcuts could not be created.");

            await Task.CompletedTask;
        }

        private async Task CopyLauncher()
        {
            try
            {
                UpdateCurrentTask("Extracting launcher...");
                LogActivity("Extracting launcher.exe from embedded resources...");

                var assembly = Assembly.GetExecutingAssembly();
                string launcherDest = Path.Combine(installDir, "launcher.exe");

                // Try multiple resource name patterns
                string[] resourceNames = new[] {
                    "launcher.exe",
                    "VSCodePortableInstaller.launcher.exe",
                    "res.launcher.exe"
                };

                System.IO.Stream stream = null;
                foreach (var name in resourceNames)
                {
                    stream = assembly.GetManifestResourceStream(name);
                    if (stream != null)
                    {
                        LogActivity("Found launcher resource: " + name);
                        break;
                    }
                }

                if (stream == null)
                {
                    // List all embedded resources for debugging
                    var allResources = assembly.GetManifestResourceNames();
                    LogActivity("Available embedded resources:");
                    foreach (var res in allResources)
                    {
                        LogActivity("  - " + res);
                    }
                    throw new Exception("launcher.exe not found in embedded resources");
                }

                string tempPath = launcherDest + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (stream)
                    using (var fileStream = File.Create(tempPath))
                    {
                        stream.CopyTo(fileStream);
                    }

                    if (new FileInfo(tempPath).Length == 0)
                        throw new InvalidDataException("Embedded launcher resource is empty.");
                    if (File.Exists(launcherDest))
                        File.Replace(tempPath, launcherDest, null);
                    else
                        File.Move(tempPath, launcherDest);
                    LogActivity("Launcher extracted to: " + launcherDest);
                }
                finally
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogActivity("Error copying launcher: " + ex.Message);
                throw;
            }
        }

        private async Task InstallPipWrapper(System.Threading.CancellationToken cancellationToken)
        {
            string binDirectory = Path.Combine(installDir, "data", "lib", "pwsh", "bin");
            string scriptPath = Path.Combine(binDirectory, "pip.ps1");
            await DownloadUtilsFileAsync("pip.ps1", scriptPath, cancellationToken);
            LogActivity("PowerShell 7: Installed pip.ps1 at " + scriptPath);
        }

        private async Task DownloadUtilsFileAsync(
            string fileName,
            string destinationPath,
            System.Threading.CancellationToken cancellationToken)
        {
            string downloadUrl = UTILS_RAW_BASE_URL + Uri.EscapeDataString(fileName);
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(MAX_DOWNLOAD_TIMEOUT_MINUTES);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("CodeNix-Installer");
                await CommonHelper.DownloadFileWithRetryAsync(
                    client, downloadUrl, destinationPath, cancellationToken).ConfigureAwait(false);
            }
        }

    }
}
