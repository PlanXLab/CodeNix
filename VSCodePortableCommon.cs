using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace VSCodePortableCommon
{
    public static class PortablePython
    {
        public static string UvPath(string baseDir)
        {
            return Path.Combine(baseDir, "data", "lib", "uv", "uv.exe");
        }

        public static string RuntimeDirectory(string baseDir)
        {
            return Path.Combine(baseDir, "data", "lib", "python");
        }

        public static string GlobalEnvironmentDirectory(string baseDir)
        {
            return Path.Combine(baseDir, "data", "venvs", "global");
        }

        public static string GlobalPythonExecutable(string baseDir)
        {
            return Path.Combine(GlobalEnvironmentDirectory(baseDir), "Scripts", "python.exe");
        }

        public static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"").TrimEnd('\\') + "\"";
        }

        public static bool IsValidPythonVersionRequest(string requestedVersion)
        {
            if (string.IsNullOrWhiteSpace(requestedVersion))
                return true;

            string normalized = requestedVersion.Trim();
            if (!Regex.IsMatch(normalized, @"^[0-9]+\.[0-9]+(?:\.[0-9]+)?$"))
                return false;

            Version parsed;
            return Version.TryParse(normalized, out parsed);
        }

        public static string SelectLatestStablePython(string json)
        {
            return SelectPythonVersion(json, null);
        }

        public static string SelectPythonVersion(string json, string requestedVersion)
        {
            if (!IsValidPythonVersionRequest(requestedVersion))
                throw new ArgumentException("Enter a Python version in x.y or x.y.z format.", "requestedVersion");

            string normalizedRequest = string.IsNullOrWhiteSpace(requestedVersion)
                ? null
                : requestedVersion.Trim();
            Version requested = null;
            if (normalizedRequest != null && !Version.TryParse(normalizedRequest, out requested))
                throw new ArgumentException("Enter a Python version in x.y or x.y.z format.", "requestedVersion");

            List<Version> versions = GetStablePythonVersions(json);
            if (versions.Count == 0)
                throw new InvalidDataException("uv did not provide a stable Windows x64 CPython release.");

            Version selected = null;
            if (requested == null)
            {
                foreach (Version version in versions)
                {
                    if (selected == null || version > selected)
                        selected = version;
                }
            }
            else if (normalizedRequest.Split('.').Length == 2)
            {
                foreach (Version version in versions)
                {
                    if (version.Major == requested.Major && version.Minor == requested.Minor &&
                        (selected == null || version > selected))
                        selected = version;
                }
                if (selected == null)
                    throw new InvalidOperationException("No stable Windows x64 CPython release is available for Python " +
                        normalizedRequest + ".");
            }
            else
            {
                foreach (Version version in versions)
                {
                    if (version.Equals(requested))
                    {
                        selected = version;
                        break;
                    }
                }
                if (selected == null)
                    throw new InvalidOperationException("Python " + normalizedRequest +
                        " is not available as a stable Windows x64 CPython release.");
            }

            return selected.ToString();
        }

        public static async Task<string> ResolveAvailablePythonVersionAsync(string requestedVersion,
            System.Threading.CancellationToken cancellationToken)
        {
            if (!IsValidPythonVersionRequest(requestedVersion))
                throw new ArgumentException("Enter a Python version in x.y or x.y.z format.", "requestedVersion");
            if (string.IsNullOrWhiteSpace(requestedVersion))
                return null;

            const string releaseUrl = "https://api.github.com/repos/astral-sh/python-build-standalone/releases/latest";
            string releaseJson;
            using (var client = CreateClient())
                releaseJson = await CommonHelper.GetStringWithRetryAsync(client, releaseUrl, cancellationToken)
                    .ConfigureAwait(false);

            var serializer = new JavaScriptSerializer();
            var release = serializer.Deserialize<Dictionary<string, object>>(releaseJson);
            object releaseAssets;
            if (release == null || !release.TryGetValue("assets", out releaseAssets))
                throw new InvalidDataException("The Python release response did not contain assets.");

            var downloads = new List<Dictionary<string, object>>();
            foreach (var item in (System.Collections.IEnumerable)releaseAssets)
            {
                var asset = item as Dictionary<string, object>;
                if (asset == null || !asset.ContainsKey("name"))
                    continue;

                string assetName = Convert.ToString(asset["name"]);
                var match = Regex.Match(assetName,
                    @"^cpython-(3\.\d+\.\d+)\+\d+-x86_64-pc-windows-msvc-install_only(?:_stripped)?\.tar\.gz$",
                    RegexOptions.IgnoreCase);
                if (!match.Success)
                    continue;

                downloads.Add(new Dictionary<string, object>
                {
                    { "version", match.Groups[1].Value },
                    { "implementation", "cpython" },
                    { "os", "windows" },
                    { "arch", "x86_64" },
                    { "variant", "default" }
                });
            }

            if (downloads.Count == 0)
                throw new InvalidDataException("The Python release contains no stable Windows x64 CPython assets.");

            return SelectPythonVersion(serializer.Serialize(downloads), requestedVersion);
        }

        private static List<Version> GetStablePythonVersions(string json)
        {
            var serializer = new JavaScriptSerializer();
            var downloads = serializer.Deserialize<Dictionary<string, object>[]>(json);
            if (downloads == null)
                throw new InvalidDataException("uv returned an invalid Python download list.");

            var versions = new List<Version>();
            foreach (var download in downloads)
            {
                string version = Convert.ToString(download["version"]);
                Version parsed;
                if (Convert.ToString(download["implementation"]) != "cpython" ||
                    Convert.ToString(download["os"]) != "windows" ||
                    Convert.ToString(download["arch"]) != "x86_64" ||
                    Convert.ToString(download["variant"]) != "default" ||
                    !Regex.IsMatch(version, @"^3\.\d+\.\d+$") ||
                    !Version.TryParse(version, out parsed))
                    continue;
                versions.Add(parsed);
            }
            return versions;
        }

        public static async Task<string> RunAsync(string baseDir, string arguments,
            System.Threading.CancellationToken cancellationToken, Action<string> logActivity = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = UvPath(baseDir),
                Arguments = arguments,
                WorkingDirectory = baseDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            ApplyEnvironment(startInfo, baseDir);
            startInfo.EnvironmentVariables["UV_HTTP_TIMEOUT"] = "30";
            startInfo.EnvironmentVariables["UV_HTTP_RETRIES"] = "1";
            cancellationToken.ThrowIfCancellationRequested();
            using (var process = Process.Start(startInfo))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                var exited = Task.Run(() => process.WaitForExit());
                using (cancellationToken.Register(() =>
                {
                    try { if (!process.HasExited) process.Kill(); }
                    catch (InvalidOperationException) { }
                }))
                {
                    if (await Task.WhenAny(exited, Task.Delay(TimeSpan.FromMinutes(10), cancellationToken)).ConfigureAwait(false) != exited)
                    {
                        try { if (!process.HasExited) process.Kill(); }
                        catch (InvalidOperationException) { }
                        await exited.ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new TimeoutException("uv command timed out: " + arguments);
                    }
                    await exited.ConfigureAwait(false);
                    string standardOutput = await output.ConfigureAwait(false);
                    string standardError = await error.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (logActivity != null && !string.IsNullOrWhiteSpace(standardError))
                        logActivity(standardError.Trim());
                    if (process.ExitCode != 0)
                        throw new InvalidOperationException("uv failed: " + standardError.Trim());
                    return standardOutput.Trim();
                }
            }
        }

        private static HttpClient CreateClient()
        {
            CommonHelper.EnableTls12();
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CodeNix/2.0");
            return client;
        }

        public static async Task<string> GetLatestUvVersionAsync()
        {
            using (var client = CreateClient())
            {
                string json = await client.GetStringAsync("https://api.github.com/repos/astral-sh/uv/releases/latest").ConfigureAwait(false);
                var release = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                return Convert.ToString(release["tag_name"]).TrimStart('v');
            }
        }

        public static async Task<string> InstallUvAsync(string baseDir, string targetVersion,
            System.Threading.CancellationToken cancellationToken, Action<string> logActivity = null,
            Action<long, long> reportProgress = null)
        {
            if (!string.IsNullOrEmpty(targetVersion) && !Regex.IsMatch(targetVersion, @"^\d+\.\d+\.\d+$"))
                throw new ArgumentException("Invalid uv release version.", "targetVersion");
            string library = Path.Combine(baseDir, "data", "lib");
            Directory.CreateDirectory(library);
            string staging = Path.Combine(library, ".uv-stage-" + Guid.NewGuid().ToString("N"));
            string archive = Path.Combine(Path.GetTempPath(), "codenix-uv-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                using (var client = CreateClient())
                {
                    string releaseUrl = "https://api.github.com/repos/astral-sh/uv/releases/" +
                        (string.IsNullOrEmpty(targetVersion) ? "latest" : "tags/" + targetVersion);
                    if (logActivity != null)
                        logActivity("Resolving uv release metadata...");
                    string json = await CommonHelper.GetStringWithRetryAsync(client, releaseUrl, cancellationToken)
                        .ConfigureAwait(false);
                    var release = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                    string version = Convert.ToString(release["tag_name"]).TrimStart('v');
                    if (Convert.ToBoolean(release["prerelease"]) || !Regex.IsMatch(version, @"^\d+\.\d+\.\d+$"))
                        throw new InvalidDataException("A stable uv release is required.");
                    if (!string.IsNullOrEmpty(targetVersion) && version != targetVersion)
                        throw new InvalidDataException("The uv release does not match the requested version.");
                    Dictionary<string, object> asset = null;
                    foreach (var candidate in (System.Collections.IEnumerable)release["assets"])
                    {
                        var metadata = (Dictionary<string, object>)candidate;
                        if (Convert.ToString(metadata["name"]) == "uv-x86_64-pc-windows-msvc.zip")
                            asset = metadata;
                    }
                    if (asset == null || !asset.ContainsKey("digest") ||
                        !Convert.ToString(asset["digest"]).StartsWith("sha256:", StringComparison.Ordinal))
                        throw new InvalidDataException("The Windows uv archive or its SHA256 digest is missing.");
                    if (logActivity != null)
                        logActivity("Downloading uv " + version + " for Windows x64...");
                    await CommonHelper.DownloadFileWithRetryAsync(client,
                        Convert.ToString(asset["browser_download_url"]), archive, cancellationToken,
                        reportProgress).ConfigureAwait(false);
                    string digest;
                    using (var stream = File.OpenRead(archive))
                    using (var sha256 = SHA256.Create())
                        digest = "sha256:" + BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                    if (!string.Equals(digest, Convert.ToString(asset["digest"]), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The uv archive SHA256 verification failed.");
                    cancellationToken.ThrowIfCancellationRequested();
                    ZipFile.ExtractToDirectory(archive, staging);
                    if (!File.Exists(Path.Combine(staging, "uv.exe")))
                        throw new InvalidDataException("The uv archive does not contain uv.exe.");
                    CommonHelper.ReplaceDirectoryAtomically(staging, Path.GetDirectoryName(UvPath(baseDir)),
                        Path.Combine(library, ".uv-backup"), logActivity);
                    string installed = await RunAsync(baseDir, "--version", cancellationToken).ConfigureAwait(false);
                    if (!installed.StartsWith("uv " + version + " ", StringComparison.Ordinal))
                        throw new InvalidDataException("The installed uv version could not be verified.");
                    return version;
                }
            }
            finally
            {
                if (File.Exists(archive)) File.Delete(archive);
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }

        public static Task<string> InstallLatestPythonAsync(string baseDir,
            System.Threading.CancellationToken cancellationToken, Action<string> selectedVersion,
            Action<string> logActivity = null)
        {
            return InstallPythonAsync(baseDir, null, cancellationToken, selectedVersion, logActivity);
        }

        public static async Task<string> InstallPythonAsync(string baseDir, string requestedVersion,
            System.Threading.CancellationToken cancellationToken, Action<string> selectedVersion,
            Action<string> logActivity = null)
        {
            string downloads = await RunAsync(baseDir,
                "python list --only-downloads --output-format json --no-config cpython@3", cancellationToken).ConfigureAwait(false);
            string version = SelectPythonVersion(downloads, requestedVersion);
            if (selectedVersion != null) selectedVersion(version);
            await RunAsync(baseDir, "python install cpython-" + version +
                "-windows-x86_64-none --no-config --no-bin --no-registry", cancellationToken, logActivity).ConfigureAwait(false);
            return version;
        }

        public static async Task<string> EnsureGlobalEnvironmentAsync(string baseDir, string pythonVersion,
            System.Threading.CancellationToken cancellationToken, Action<string> logActivity = null)
        {
            if (string.IsNullOrWhiteSpace(pythonVersion) ||
                !Regex.IsMatch(pythonVersion, @"^[0-9]+\.[0-9]+\.[0-9]+$"))
                throw new ArgumentException("A full managed Python version is required.", "pythonVersion");

            string environmentDirectory = GlobalEnvironmentDirectory(baseDir);
            string pythonExecutable = GlobalPythonExecutable(baseDir);
            string configuration = Path.Combine(environmentDirectory, "pyvenv.cfg");
            if (Directory.Exists(environmentDirectory) &&
                File.Exists(pythonExecutable) && File.Exists(configuration))
                return environmentDirectory;

            Directory.CreateDirectory(Path.GetDirectoryName(environmentDirectory));
            await RunAsync(baseDir, "venv --python " + Quote(pythonVersion) +
                " --relocatable --allow-existing --no-python-downloads --no-config --no-project " +
                Quote(environmentDirectory), cancellationToken, logActivity).ConfigureAwait(false);
            if (!File.Exists(pythonExecutable) || !File.Exists(configuration))
                throw new InvalidDataException("uv did not create a complete global Python environment: " + environmentDirectory);
            return environmentDirectory;
        }

        public static async Task<bool> RebaseEnvironmentAsync(string baseDir, string environmentDirectory,
            System.Threading.CancellationToken cancellationToken)
        {
            string configuration = Path.Combine(environmentDirectory, "pyvenv.cfg");
            if (!File.Exists(configuration)) return false;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(configuration))
            {
                int separator = line.IndexOf('=');
                if (separator > 0) values[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
            }
            string home;
            string version;
            if (!values.TryGetValue("home", out home) || !values.TryGetValue("version_info", out version) ||
                home.Replace('/', '\\').IndexOf(@"\data\lib\python\", StringComparison.OrdinalIgnoreCase) < 0 ||
                !Regex.IsMatch(version, @"^3\.\d+\.\d+$"))
                return false;
            string runtimeStore = Path.GetFullPath(RuntimeDirectory(baseDir)).TrimEnd('\\') + "\\";
            if (Path.GetFullPath(home).StartsWith(runtimeStore, StringComparison.OrdinalIgnoreCase))
                return false;
            string python = Path.Combine(runtimeStore, "cpython-" + version + "-windows-x86_64-none", "python.exe");
            if (!File.Exists(python)) return false;
            await RunAsync(baseDir, "venv --python " + Quote(version) +
                " --relocatable --allow-existing --no-python-downloads --no-config --no-project " +
                Quote(environmentDirectory), cancellationToken).ConfigureAwait(false);
            return true;
        }

        public static async Task RebaseProjectsAsync(string baseDir, Action<string> logActivity)
        {
            if (!File.Exists(UvPath(baseDir))) return;

            string globalEnvironment = GlobalEnvironmentDirectory(baseDir);
            if (Directory.Exists(globalEnvironment))
            {
                try
                {
                    if (await RebaseEnvironmentAsync(baseDir, globalEnvironment, default(System.Threading.CancellationToken)).ConfigureAwait(false))
                        logActivity("Rebased global Python environment: " + globalEnvironment);
                }
                catch (Exception ex) { logActivity("Global Python environment was not rebased: " + globalEnvironment + ": " + ex.Message); }
            }

            string projects = Path.Combine(baseDir, "projects");
            if (!Directory.Exists(projects)) return;
            var pending = new Stack<string>();
            pending.Push(projects);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                string[] children;
                try { children = Directory.GetDirectories(directory); }
                catch (UnauthorizedAccessException ex)
                {
                    logActivity("Project directory could not be inspected: " + directory + ": " + ex.Message);
                    continue;
                }
                foreach (string child in children)
                {
                    string name = Path.GetFileName(child);
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0 ||
                        name == ".git" || name == "node_modules" || name == "__pycache__" || name == ".cache")
                        continue;
                    if (name == ".venv")
                    {
                        try
                        {
                            if (await RebaseEnvironmentAsync(baseDir, child, default(System.Threading.CancellationToken)).ConfigureAwait(false))
                                logActivity("Rebased project environment: " + child);
                        }
                        catch (Exception ex) { logActivity("Project environment was not rebased: " + child + ": " + ex.Message); }
                    }
                    else pending.Push(child);
                }
            }
        }

        public static void ApplyEnvironment(ProcessStartInfo startInfo, string baseDir)
        {
            string root = Path.GetFullPath(baseDir);
            startInfo.EnvironmentVariables["VSCODE_PORTABLE"] = Path.Combine(root, "data");
            startInfo.EnvironmentVariables["UV_PYTHON_INSTALL_DIR"] = RuntimeDirectory(root);
            startInfo.EnvironmentVariables["UV_CACHE_DIR"] = Path.Combine(root, "data", "cache", "uv");
            startInfo.EnvironmentVariables["UV_PYTHON_CACHE_DIR"] = Path.Combine(root, "data", "cache", "python");
            startInfo.EnvironmentVariables["UV_TOOL_DIR"] = Path.Combine(root, "data", "lib", "uv-tools");
            startInfo.EnvironmentVariables["UV_TOOL_BIN_DIR"] = Path.Combine(root, "data", "bin");
            startInfo.EnvironmentVariables["UV_CREDENTIALS_DIR"] = Path.Combine(root, "data", "uv", "credentials");
            startInfo.EnvironmentVariables["UV_PYTHON_BIN_DIR"] = Path.Combine(root, "data", "bin");
            startInfo.EnvironmentVariables["UV_PYTHON_INSTALL_BIN"] = "0";
            startInfo.EnvironmentVariables["UV_PYTHON_INSTALL_REGISTRY"] = "0";
            startInfo.EnvironmentVariables["UV_PYTHON_NO_REGISTRY"] = "1";
            startInfo.EnvironmentVariables["UV_PYTHON_PREFERENCE"] = "only-managed";
            startInfo.EnvironmentVariables["UV_LINK_MODE"] = "hardlink";
            startInfo.EnvironmentVariables["UV_NO_PROGRESS"] = "1";
            foreach (string name in new[]
            {
                "UV_PYTHON", "UV_PROJECT_ENVIRONMENT", "PYTHONHOME", "PYTHONPATH",
                "VIRTUAL_ENV", "CONDA_PREFIX", "CONDA_DEFAULT_ENV", "ELECTRON_RUN_AS_NODE"
            })
                startInfo.EnvironmentVariables.Remove(name);

            string uvDirectory = Path.GetDirectoryName(UvPath(root));
            string inheritedPath = startInfo.EnvironmentVariables["PATH"] ?? string.Empty;
            startInfo.EnvironmentVariables["PATH"] = uvDirectory + ";" + inheritedPath;
        }
    }

    public sealed class CodeNixProgressBar : Control
    {
        private int minimum;
        private int maximum = 100;
        private int currentValue;

        public CodeNixProgressBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.White;
            TrackColor = Color.FromArgb(224, 232, 241);
            ProgressColor = CodeNixTheme.Accent;
            BorderColor = CodeNixTheme.Border;
            AccessibleRole = AccessibleRole.ProgressBar;
            TabStop = false;
        }

        public int Minimum
        {
            get { return minimum; }
            set
            {
                minimum = value;
                if (maximum < minimum)
                    maximum = minimum;
                Value = currentValue;
            }
        }

        public int Maximum
        {
            get { return maximum; }
            set
            {
                maximum = value < minimum ? minimum : value;
                Value = currentValue;
            }
        }

        public int Value
        {
            get { return currentValue; }
            set
            {
                int boundedValue = Math.Max(minimum, Math.Min(value, maximum));
                if (currentValue == boundedValue)
                    return;
                currentValue = boundedValue;
                Invalidate();
            }
        }

        public Color TrackColor { get; set; }
        public Color ProgressColor { get; set; }
        public Color BorderColor { get; set; }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color background = Parent == null ? BackColor : Parent.BackColor;
            using (var brush = new SolidBrush(background))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle bounds = ClientRectangle;
            if (bounds.Width < 2 || bounds.Height < 2)
                return;

            bounds.Width--;
            bounds.Height--;
            int radius = Math.Min(5, bounds.Height / 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (GraphicsPath trackPath = CreateRoundedRectangle(bounds, radius))
            using (var trackBrush = new SolidBrush(TrackColor))
            using (var borderPen = new Pen(BorderColor))
            {
                e.Graphics.FillPath(trackBrush, trackPath);

                int range = maximum - minimum;
                double ratio = range == 0 ? 1.0 : (double)(currentValue - minimum) / range;
                int fillWidth = (int)Math.Round(bounds.Width * ratio);
                if (fillWidth > 0)
                {
                    Rectangle fillBounds = bounds;
                    fillBounds.Width = Math.Min(fillWidth, bounds.Width);
                    int fillRadius = Math.Min(radius, Math.Min(fillBounds.Width, fillBounds.Height) / 2);
                    using (GraphicsPath fillPath = CreateRoundedRectangle(fillBounds, fillRadius))
                    using (var progressBrush = new SolidBrush(ProgressColor))
                        e.Graphics.FillPath(progressBrush, fillPath);
                }

                e.Graphics.DrawPath(borderPen, trackPath);
            }
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return path;
            if (radius <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            int diameter = radius * 2;
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public static class CodeNixTheme
    {
        public static readonly Color Background = Color.FromArgb(238, 243, 249);
        public static readonly Color Surface = Color.White;
        public static readonly Color LogBackground = Color.FromArgb(247, 249, 252);
        public static readonly Color Border = Color.FromArgb(211, 221, 232);
        public static readonly Color Header = Color.FromArgb(35, 58, 83);
        public static readonly Color HeaderSubtitle = Color.FromArgb(218, 229, 241);
        public static readonly Color HeaderBrand = Color.FromArgb(180, 199, 219);
        public static readonly Color TextPrimary = Color.FromArgb(35, 45, 58);
        public static readonly Color TextSecondary = Color.FromArgb(83, 97, 114);
        public static readonly Color TextTertiary = Color.FromArgb(115, 130, 148);
        public static readonly Color Accent = Color.FromArgb(31, 111, 181);
        public static readonly Color AccentHover = Color.FromArgb(25, 94, 155);
        public static readonly Color AccentPressed = Color.FromArgb(19, 77, 130);
        public static readonly Color Success = Color.FromArgb(34, 125, 75);
        public static readonly Color Error = Color.FromArgb(190, 55, 55);
        public static readonly Color DisabledBackground = Color.FromArgb(226, 233, 241);

        private static readonly Font BodyFont = new Font("Segoe UI", 9F, FontStyle.Regular);
        private static readonly Font SmallFont = new Font("Segoe UI", 8.5F, FontStyle.Regular);
        private static readonly Font SmallEmphasisFont = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        private static readonly Font SectionFont = new Font("Segoe UI", 11F, FontStyle.Bold);
        private static readonly Font TitleFont = new Font("Segoe UI", 16F, FontStyle.Bold);
        private static readonly Font ButtonFont = new Font("Segoe UI", 9F, FontStyle.Bold);
        private static readonly Font LogFont = new Font("Consolas", 8.5F, FontStyle.Regular);

        public static void Apply(
            Form form,
            Panel headerPanel,
            Label titleLabel,
            Label subtitleLabel,
            Label brandLabel,
            Panel settingsPanel,
            Panel separatorPanel,
            Panel progressPanel,
            TableLayoutPanel componentTable,
            TextBox activityLog,
            Label logHeaderLabel,
            Button primaryButton,
            Button secondaryButton)
        {
            form.BackColor = Background;
            form.ForeColor = TextPrimary;
            form.Font = BodyFont;

            headerPanel.BackColor = Header;
            settingsPanel.BackColor = Surface;
            separatorPanel.BackColor = Border;
            progressPanel.BackColor = Background;
            componentTable.BackColor = Surface;

            ApplyControlDefaults(form);

            titleLabel.Font = TitleFont;
            titleLabel.ForeColor = Color.White;
            subtitleLabel.Font = SmallFont;
            subtitleLabel.ForeColor = HeaderSubtitle;
            brandLabel.Font = SmallFont;
            brandLabel.ForeColor = HeaderBrand;
            brandLabel.Text = "© PlanXLab | Dev by Atticle";
            logHeaderLabel.Font = SmallEmphasisFont;
            logHeaderLabel.ForeColor = TextTertiary;
            logHeaderLabel.Text = "ACTIVITY LOG";

            activityLog.BackColor = LogBackground;
            activityLog.BorderStyle = BorderStyle.FixedSingle;
            activityLog.Font = LogFont;
            activityLog.ForeColor = TextSecondary;

            StylePrimaryButton(primaryButton);
            StyleSecondaryButton(secondaryButton);
        }

        public static void StylePrimaryButton(Button button)
        {
            button.BackColor = Accent;
            button.ForeColor = Color.White;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = AccentHover;
            button.FlatAppearance.MouseDownBackColor = AccentPressed;
            button.Font = ButtonFont;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
        }

        public static void StyleSecondaryButton(Button button)
        {
            button.BackColor = Surface;
            button.ForeColor = TextPrimary;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Background;
            button.FlatAppearance.MouseDownBackColor = Border;
            button.Font = BodyFont;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
        }

        public static void StyleBusyButton(Button button, string text)
        {
            button.Enabled = false;
            button.Text = text;
            button.BackColor = DisabledBackground;
            button.ForeColor = TextSecondary;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.WaitCursor;
        }

        private static void ApplyControlDefaults(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                Label label = control as Label;
                if (label != null)
                {
                    string name = label.Name ?? string.Empty;
                    label.Font = BodyFont;
                    label.ForeColor = TextPrimary;

                    if (name.EndsWith("StatusLabel", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("pythonHint", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("summaryBodyLabel", StringComparison.OrdinalIgnoreCase))
                    {
                        label.Font = SmallFont;
                        label.ForeColor = TextSecondary;
                    }
                    else if (name.EndsWith("NameLabel", StringComparison.OrdinalIgnoreCase))
                    {
                        label.Font = BodyFont;
                    }
                    else if (name.Equals("summaryTitleLabel", StringComparison.OrdinalIgnoreCase))
                    {
                        label.Font = SectionFont;
                    }
                    else if (name.Equals("currentTaskLabel", StringComparison.OrdinalIgnoreCase))
                    {
                        label.Font = SmallEmphasisFont;
                        label.ForeColor = Accent;
                    }
                    else if (name.Equals("completedLabel", StringComparison.OrdinalIgnoreCase) ||
                             name.Equals("elapsedTimeLabel", StringComparison.OrdinalIgnoreCase))
                    {
                        label.Font = SmallFont;
                        label.ForeColor = TextSecondary;
                    }
                }

                TextBox textBox = control as TextBox;
                if (textBox != null)
                {
                    textBox.BackColor = Surface;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    textBox.Font = BodyFont;
                    textBox.ForeColor = TextPrimary;
                }

                TableLayoutPanel table = control as TableLayoutPanel;
                if (table != null)
                    table.BackColor = Surface;

                if (control.HasChildren)
                    ApplyControlDefaults(control);
            }
        }
    }

    /// <summary>
    /// Helper class for file extraction (C# 5 compatible)
    /// </summary>
    internal class FileData
    {
        public string Path { get; set; }
        public byte[] Data { get; set; }
    }

    internal class ZipEntryWorkItem
    {
        public string FullName { get; set; }
        public long Length { get; set; }
    }

    internal sealed class ParallelZipArchiveReader : IDisposable
    {
        private readonly FileStream zipStream;
        private readonly ZipArchive archive;

        public ParallelZipArchiveReader(string zipPath)
        {
            zipStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                CommonHelper.EXTRACT_BUFFER_SIZE, FileOptions.SequentialScan);
            archive = new ZipArchive(zipStream, ZipArchiveMode.Read, false);
        }

        public ZipArchiveEntry GetEntry(string fullName)
        {
            return archive.GetEntry(fullName);
        }

        public void Dispose()
        {
            archive.Dispose();
            zipStream.Dispose();
        }
    }

    /// <summary>
    /// Common utilities for VSCode Portable installer and launcher
    /// </summary>
    public static class CommonHelper
    {
        // Constants - Optimized buffer sizes
        public const int BUFFER_SIZE = 65536; // 64KB for better I/O performance
        public const int EXTRACT_BUFFER_SIZE = 81920; // 80KB for extraction
        public const string RuntimeInfoFileName = "codenix.info";
        public const string RuntimeLogFileName = "codenix.log";

        public static bool RemoveUtf8BomFromFile(string filePath)
        {
            byte[] contents = File.ReadAllBytes(filePath);
            if (contents.Length < 3 ||
                contents[0] != 0xEF ||
                contents[1] != 0xBB ||
                contents[2] != 0xBF)
            {
                return false;
            }

            string text = new UTF8Encoding(false, true)
                .GetString(contents, 3, contents.Length - 3)
                .TrimStart('\uFEFF');
            string tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempPath, text, new UTF8Encoding(false));
                File.Replace(tempPath, filePath, null);
                return true;
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static readonly TimeSpan DownloadIdleTimeout = TimeSpan.FromSeconds(30);

        private static bool IsTransientHttpStatusCode(int statusCode)
        {
            return statusCode == 408 || statusCode == 429 || statusCode >= 500;
        }

        private static TimeSpan GetHttpRetryDelay(int attempt,
            System.Net.Http.Headers.RetryConditionHeaderValue retryAfter)
        {
            TimeSpan delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt - 1)));
            if (retryAfter != null)
            {
                if (retryAfter.Delta.HasValue)
                    delay = retryAfter.Delta.Value;
                else if (retryAfter.Date.HasValue)
                    delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            }

            if (delay < TimeSpan.Zero)
                return TimeSpan.Zero;
            return delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay;
        }

        public static async Task<string> GetStringWithRetryAsync(HttpClient client, string url,
            System.Threading.CancellationToken cancellationToken, int maxAttempts = 4)
        {
            if (client == null)
                throw new ArgumentNullException("client");
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("A request URL is required.", "url");
            if (maxAttempts < 1)
                throw new ArgumentOutOfRangeException("maxAttempts");

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TimeSpan retryDelay = GetHttpRetryDelay(attempt, null);
                try
                {
                    HttpResponseMessage response;
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        response = await SendDownloadRequestAsync(client, request, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        int statusCode = (int)response.StatusCode;
                        if (IsTransientHttpStatusCode(statusCode) && attempt < maxAttempts)
                        {
                            retryDelay = GetHttpRetryDelay(attempt, response.Headers.RetryAfter);
                            response.Dispose();
                        }
                        else
                        {
                            response.Dispose();
                            if (IsTransientHttpStatusCode(statusCode))
                                throw new HttpRequestException("Transient HTTP response: " + statusCode + ".");
                            throw new InvalidDataException("Request failed with HTTP " + statusCode + ".");
                        }
                    }
                    else
                    {
                        using (response)
                            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (attempt == maxAttempts)
                        throw;
                }
                catch (TimeoutException)
                {
                    if (attempt == maxAttempts)
                        throw;
                }
                catch (HttpRequestException)
                {
                    if (attempt == maxAttempts)
                        throw;
                }

                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException("The HTTP request retry loop ended unexpectedly.");
        }

        public static async Task<HttpResponseMessage> SendDownloadRequestAsync(
            HttpClient client,
            HttpRequestMessage request,
            System.Threading.CancellationToken cancellationToken)
        {
            using (var timeoutSource = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutSource.CancelAfter(DownloadIdleTimeout);
                try
                {
                    return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (timeoutSource.IsCancellationRequested)
                        throw new TimeoutException("No download response was received for 30 seconds.");
                    throw;
                }
            }
        }

        public static async Task<HttpResponseMessage> SendHeadRequestWithRetryAsync(
            HttpClient client,
            string url,
            System.Threading.CancellationToken cancellationToken,
            int maxAttempts = 4)
        {
            if (client == null)
                throw new ArgumentNullException("client");
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("A request URL is required.", "url");
            if (maxAttempts < 1)
                throw new ArgumentOutOfRangeException("maxAttempts");

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TimeSpan retryDelay = GetHttpRetryDelay(attempt, null);
                try
                {
                    HttpResponseMessage response;
                    using (var request = new HttpRequestMessage(HttpMethod.Head, url))
                    {
                        response = await SendDownloadRequestAsync(client, request, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (!IsTransientHttpStatusCode((int)response.StatusCode) || attempt == maxAttempts)
                        return response;

                    retryDelay = GetHttpRetryDelay(attempt, response.Headers.RetryAfter);
                    response.Dispose();
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (attempt == maxAttempts)
                        throw;
                }
                catch (TimeoutException)
                {
                    if (attempt == maxAttempts)
                        throw;
                }
                catch (HttpRequestException)
                {
                    if (attempt == maxAttempts)
                        throw;
                }

                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException("The HTTP request retry loop ended unexpectedly.");
        }

        public static async Task<int> ReadDownloadChunkAsync(
            Stream input,
            byte[] buffer,
            int offset,
            int count,
            System.Threading.CancellationToken cancellationToken)
        {
            using (var timeoutSource = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutSource.CancelAfter(DownloadIdleTimeout);
                try
                {
                    return await input.ReadAsync(buffer, offset, count, timeoutSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (timeoutSource.IsCancellationRequested)
                        throw new TimeoutException("No download data was received for 30 seconds.");
                    throw;
                }
            }
        }

        public static async Task DownloadFileWithRetryAsync(
            HttpClient client,
            string url,
            string destPath,
            System.Threading.CancellationToken cancellationToken,
            Action<long, long> reportProgress = null,
            int maxAttempts = 2)
        {
            if (client == null)
                throw new ArgumentNullException("client");
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("A download URL is required.", "url");
            if (maxAttempts < 1)
                throw new ArgumentOutOfRangeException("maxAttempts");

            string destinationDirectory = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string partialPath = destPath + "." + Guid.NewGuid().ToString("N") + ".part";
                TimeSpan retryDelay = GetHttpRetryDelay(attempt, null);

                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    using (var response = await SendDownloadRequestAsync(client, request, cancellationToken))
                    {
                        int statusCode = (int)response.StatusCode;
                        if (!response.IsSuccessStatusCode)
                        {
                            if (IsTransientHttpStatusCode(statusCode))
                            {
                                retryDelay = GetHttpRetryDelay(attempt, response.Headers.RetryAfter);
                                throw new HttpRequestException("Transient download response: HTTP " + statusCode + ".");
                            }

                            throw new InvalidDataException("Download failed with HTTP " + statusCode + ".");
                        }

                        long? contentLength = response.Content.Headers.ContentLength;
                        if (reportProgress != null)
                            reportProgress(0, contentLength.HasValue ? contentLength.Value : 0);
                        long downloadedBytes = 0;
                        byte[] buffer = new byte[262144];
                        using (var input = await response.Content.ReadAsStreamAsync())
                        using (var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, true))
                        {
                            int bytesRead;
                            while ((bytesRead = await ReadDownloadChunkAsync(
                                input, buffer, 0, buffer.Length, cancellationToken)) > 0)
                            {
                                await output.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                                downloadedBytes += bytesRead;
                                if (reportProgress != null)
                                    reportProgress(downloadedBytes, contentLength.HasValue ? contentLength.Value : 0);
                            }
                        }

                        if (downloadedBytes == 0)
                            throw new IOException("The download returned no data.");
                        if (contentLength.HasValue && downloadedBytes != contentLength.Value)
                            throw new IOException("The downloaded size did not match the server's Content-Length.");
                    }

                    if (File.Exists(destPath))
                        File.Replace(partialPath, destPath, null);
                    else
                        File.Move(partialPath, destPath);
                    return;
                }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested ||
                        attempt == maxAttempts ||
                        !(ex is HttpRequestException || ex is IOException ||
                            ex is TaskCanceledException || ex is TimeoutException))
                    {
                        throw;
                    }
                }
                finally
                {
                    if (File.Exists(partialPath))
                        File.Delete(partialPath);
                }

                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        public static void ReplaceDirectoryAtomically(string stagingDir, string targetDir, string backupDir, Action<string> logActivity = null)
        {
            if (!Directory.Exists(stagingDir))
                throw new DirectoryNotFoundException("Staging directory not found: " + stagingDir);

            string stagingRoot = Path.GetPathRoot(Path.GetFullPath(stagingDir));
            string targetRoot = Path.GetPathRoot(Path.GetFullPath(targetDir));
            string backupRoot = Path.GetPathRoot(Path.GetFullPath(backupDir));
            if (!string.Equals(stagingRoot, targetRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(targetRoot, backupRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Staging, target, and backup directories must be on the same volume.");
            }

            string targetParent = Path.GetDirectoryName(targetDir);
            if (!string.IsNullOrEmpty(targetParent))
                Directory.CreateDirectory(targetParent);

            RecoverDirectoryReplacement(targetDir, backupDir);

            bool hadTarget = Directory.Exists(targetDir);
            if (hadTarget)
                Directory.Move(targetDir, backupDir);

            try
            {
                Directory.Move(stagingDir, targetDir);
            }
            catch (Exception commitError)
            {
                try
                {
                    if (Directory.Exists(targetDir))
                        Directory.Delete(targetDir, true);
                    if (hadTarget && Directory.Exists(backupDir))
                        Directory.Move(backupDir, targetDir);
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("Directory replacement failed and rollback also failed.", commitError, rollbackError);
                }

                throw;
            }

            if (hadTarget && Directory.Exists(backupDir))
            {
                try
                {
                    Directory.Delete(backupDir, true);
                }
                catch (IOException ex)
                {
                    if (logActivity != null)
                        logActivity("Updated directory is active, but old backup cleanup failed: " + ex.Message);
                }
                catch (UnauthorizedAccessException ex)
                {
                    if (logActivity != null)
                        logActivity("Updated directory is active, but old backup cleanup failed: " + ex.Message);
                }
            }
        }

        public static void RecoverDirectoryReplacement(string targetDir, string backupDir)
        {
            if (!Directory.Exists(backupDir))
                return;

            if (Directory.Exists(targetDir))
                Directory.Delete(backupDir, true);
            else
                Directory.Move(backupDir, targetDir);
        }

        /// <summary>
        /// Enable TLS 1.2 for HTTPS requests
        /// </summary>
        public static void EnableTls12()
        {
            System.Net.ServicePointManager.SecurityProtocol =
                System.Net.SecurityProtocolType.Tls12 |
                System.Net.SecurityProtocolType.Tls11 |
                System.Net.SecurityProtocolType.Tls;
        }

        public static void CopyDirectoryRecursive(string sourceDir, string destDir, bool overwrite = true)
        {
            Directory.CreateDirectory(destDir);

            // Get all files and copy in parallel for better performance
            var files = Directory.GetFiles(sourceDir);
            System.Threading.Tasks.Parallel.ForEach(files, file =>
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite);
            });

            // Process subdirectories recursively
            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectoryRecursive(dir, destSubDir, overwrite);
            }
        }

        public static Task ExtractZipWithProgressAsync(
            string zipPath,
            string extractPath,
            string componentName,
            int startProgress,
            int endProgress,
            Action<int> reportProgress,
            Action<string> logActivity,
            System.Threading.CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                List<ZipEntryWorkItem> fileEntries;
                long totalBytes = 0;

                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var entries = archive.Entries.ToList();
                    Directory.CreateDirectory(extractPath);

                    foreach (var entry in entries.Where(e => string.IsNullOrEmpty(e.Name)))
                    {
                        string dirPath = GetSafeExtractionPath(extractPath, entry.FullName);
                        Directory.CreateDirectory(dirPath);
                    }

                    fileEntries = entries
                        .Where(e => !string.IsNullOrEmpty(e.Name))
                        .Select(e => new ZipEntryWorkItem { FullName = e.FullName, Length = Math.Max(e.Length, 1L) })
                        .OrderByDescending(e => e.Length)
                        .ToList();

                    totalBytes = fileEntries.Sum(e => e.Length);
                }

                if (fileEntries.Count == 0)
                {
                    reportProgress(endProgress);
                    return;
                }

                int workerCount = GetParallelExtractionWorkerCount(fileEntries.Count);
                logActivity(componentName + ": Parallel extraction using " + workerCount + " worker(s) for " + fileEntries.Count + " file(s)");

                var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
                long extractedBytes = 0;
                int processedEntries = 0;
                int lastProgress = startProgress;
                object progressLock = new object();

                var options = new System.Threading.Tasks.ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = workerCount
                };

                System.Threading.Tasks.Parallel.ForEach(
                    fileEntries,
                    options,
                    () => new ParallelZipArchiveReader(zipPath),
                    (entryInfo, loopState, reader) =>
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var entry = reader.GetEntry(entryInfo.FullName);
                            if (entry == null)
                                throw new InvalidOperationException("Entry not found in archive");

                            string destPath = GetSafeExtractionPath(extractPath, entryInfo.FullName);
                            string destDir = Path.GetDirectoryName(destPath);
                            if (!string.IsNullOrEmpty(destDir))
                                Directory.CreateDirectory(destDir);

                            using (var entryStream = entry.Open())
                            using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, EXTRACT_BUFFER_SIZE))
                            {
                                entryStream.CopyTo(fileStream, EXTRACT_BUFFER_SIZE);
                            }

                            long completedBytes = System.Threading.Interlocked.Add(ref extractedBytes, entryInfo.Length);
                            int currentProgress = startProgress + (int)((endProgress - startProgress) * completedBytes / Math.Max(totalBytes, 1L));

                            if (currentProgress > lastProgress)
                            {
                                lock (progressLock)
                                {
                                    if (currentProgress > lastProgress)
                                    {
                                        reportProgress(currentProgress);
                                        lastProgress = currentProgress;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Enqueue(entryInfo.FullName + ": " + ex.Message);
                        }
                        finally
                        {
                            System.Threading.Interlocked.Increment(ref processedEntries);
                        }

                        return reader;
                    },
                    reader => reader.Dispose());

                if (!errors.IsEmpty)
                {
                    string firstError;
                    if (!errors.TryPeek(out firstError))
                        firstError = "Unknown extraction error";
                    throw new Exception(componentName + " extraction failed. First error: " + firstError);
                }

                if (endProgress > lastProgress)
                    reportProgress(endProgress);
            }, cancellationToken);
        }

        private static int GetParallelExtractionWorkerCount(int fileCount)
        {
            if (fileCount < 64)
                return 1;

            int workerCount = Math.Max(2, Environment.ProcessorCount / 2);
            workerCount = Math.Min(workerCount, 6);
            return Math.Min(workerCount, fileCount);
        }

        private static string GetSafeExtractionPath(string basePath, string entryPath)
        {
            string normalizedBasePath = Path.GetFullPath(basePath);
            if (!normalizedBasePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                normalizedBasePath += Path.DirectorySeparatorChar;

            string fullPath = Path.GetFullPath(Path.Combine(basePath, entryPath));
            if (!fullPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Entry resolves outside extraction directory: " + entryPath);

            return fullPath;
        }
    }

    /// <summary>
    /// PowerShell 7 installer/upgrader
    /// </summary>
    public class PowerShellInstaller
    {
        private readonly string baseDir;
        private readonly Action<string, string, int> updateStatus;
        private readonly Action<string> logActivity;

        public PowerShellInstaller(string baseDir, Action<string, string, int> updateStatus, Action<string> logActivity = null)
        {
            this.baseDir = baseDir;
            this.updateStatus = updateStatus;
            this.logActivity = logActivity ?? (_ => { });
        }

        public Task<KeyValuePair<string, string>> InstallAsync(string tempBaseDir, bool preserveExisting = false)
        {
            return InstallAsync(tempBaseDir, preserveExisting, default(System.Threading.CancellationToken));
        }

        public async Task<KeyValuePair<string, string>> InstallAsync(string tempBaseDir, bool preserveExisting,
            System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string componentName = "PowerShell 7";
            string version = "";
            string stagingDir = null;

            try
            {
                updateStatus(componentName, "Checking version...", 5);
                logActivity("PowerShell 7: Checking latest version...");

                CommonHelper.EnableTls12();

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "VSCode-Portable-Installer");
                    client.DefaultRequestHeaders.ConnectionClose = false; // Keep-Alive
                    client.Timeout = TimeSpan.FromMinutes(20);

                    string url = "";

                    try
                    {
                        // Use GitHub redirect (no API rate limit) to get latest version
                        using (var response = await CommonHelper.SendHeadRequestWithRetryAsync(
                            client,
                            "https://github.com/PowerShell/PowerShell/releases/latest",
                            cancellationToken).ConfigureAwait(false))
                        {
                            response.EnsureSuccessStatusCode();
                            string redirectUrl = response.RequestMessage.RequestUri.ToString();
                            var versionMatch = Regex.Match(redirectUrl, @"/releases/tag/v([\d\.]+)");
                            if (!versionMatch.Success)
                                throw new InvalidDataException("Version not found in redirect URL: " + redirectUrl);

                            version = versionMatch.Groups[1].Value;
                            url = "https://github.com/PowerShell/PowerShell/releases/download/v" + version + "/PowerShell-" + version + "-win-x64.zip";
                            logActivity("PowerShell 7: Found latest version " + version + " (via redirect)");
                        }

                        if (string.IsNullOrWhiteSpace(url))
                        {
                            throw new InvalidDataException("PowerShell release URL was not resolved.");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        string errorMsg = "Failed to check PowerShell 7 version. ";
                        errorMsg += "Internet connection required for installation. ";
                        errorMsg += "Error: " + ex.Message;

                        updateStatus(componentName, "⚠ Version check failed", 0);
                        logActivity("PowerShell 7: CRITICAL ERROR - " + errorMsg);
                        logActivity("PowerShell 7: Exception type: " + ex.GetType().Name);
                        if (ex.InnerException != null)
                            logActivity("PowerShell 7: Inner exception: " + ex.InnerException.Message);

                        throw new Exception(errorMsg);
                    }

                    updateStatus(componentName, "Downloading...", 10);
                    logActivity("PowerShell 7: Downloading " + version + "...");

                    string tempDir = Path.Combine(tempBaseDir, "pwsh7");

                    // Clean temp directory to avoid corrupted files
                    if (Directory.Exists(tempDir))
                    {
                        logActivity("PowerShell 7: Cleaning temp directory...");
                        Directory.Delete(tempDir, true);
                    }
                    Directory.CreateDirectory(tempDir);

                    string zipPath = Path.Combine(tempDir, "pwsh.zip");

                    try
                    {
                        await DownloadFileAsync(client, url, zipPath, componentName, 10, 40, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logActivity("PowerShell 7: Download failed - " + ex.Message);
                        updateStatus(componentName, "⚠ Download failed", 0);
                        throw new Exception("Download failed: " + ex.Message);
                    }

                    if (!File.Exists(zipPath) || new FileInfo(zipPath).Length == 0)
                        throw new InvalidDataException("PowerShell download was empty.");

                    string dataLibDir = Path.Combine(baseDir, "data", "lib");
                    Directory.CreateDirectory(dataLibDir);
                    string pwshDir = Path.Combine(dataLibDir, "pwsh");
                    stagingDir = Path.Combine(dataLibDir, ".pwsh-stage-" + Guid.NewGuid().ToString("N"));
                    string backupDir = Path.Combine(dataLibDir, ".pwsh-backup");

                    using (var archive = ZipFile.OpenRead(zipPath))
                    {
                        if (archive.GetEntry("pwsh.exe") == null)
                            throw new InvalidDataException("PowerShell archive does not contain pwsh.exe.");
                    }

                    updateStatus(componentName, "Extracting...", 45);
                    Directory.CreateDirectory(stagingDir);
                    await ExtractZipAsync(zipPath, stagingDir, componentName, 45, 80, cancellationToken).ConfigureAwait(false);

                    string stagedPwshExe = Path.Combine(stagingDir, "pwsh.exe");
                    if (!File.Exists(stagedPwshExe) || new FileInfo(stagedPwshExe).Length == 0)
                        throw new InvalidDataException("PowerShell extraction did not produce a valid pwsh.exe.");

                    if (preserveExisting && Directory.Exists(pwshDir))
                    {
                        logActivity("PowerShell 7: Preserving portable modules and custom tools...");
                        foreach (string dirName in new[] { "bin", "ohmyposh" })
                        {
                            string sourceDir = Path.Combine(pwshDir, dirName);
                            if (Directory.Exists(sourceDir))
                                CommonHelper.CopyDirectoryRecursive(sourceDir, Path.Combine(stagingDir, dirName));
                        }

                        foreach (string moduleName in new[] { "Terminal-Icons", "PSFzf", "modern-unix-win" })
                        {
                            string sourceDir = Path.Combine(pwshDir, "Modules", moduleName);
                            if (Directory.Exists(sourceDir))
                                CommonHelper.CopyDirectoryRecursive(sourceDir, Path.Combine(stagingDir, "Modules", moduleName));
                        }

                        string profilePath = Path.Combine(pwshDir, "Microsoft.PowerShell_profile.ps1");
                        if (File.Exists(profilePath))
                            File.Copy(profilePath, Path.Combine(stagingDir, "Microsoft.PowerShell_profile.ps1"), true);
                    }

                    var localeDirs = new[] { "cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant" };
                    foreach (string locale in localeDirs)
                    {
                        string localeDir = Path.Combine(stagingDir, locale);
                        if (Directory.Exists(localeDir))
                            Directory.Delete(localeDir, true);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    updateStatus(componentName, "Installing...", 90);
                    CommonHelper.ReplaceDirectoryAtomically(
                        stagingDir,
                        pwshDir,
                        backupDir,
                        message => logActivity("PowerShell 7: " + message));
                    logActivity("PowerShell 7: pwsh.exe installed successfully");

                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch (IOException ex)
                    {
                        logActivity("PowerShell 7: Temporary file cleanup warning - " + ex.Message);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        logActivity("PowerShell 7: Temporary file cleanup warning - " + ex.Message);
                    }

                    updateStatus(componentName, preserveExisting ? "✓ Completed" : "Core installed", preserveExisting ? 100 : 60);
                }
            }
            catch (OperationCanceledException)
            {
                version = "";
                if (!string.IsNullOrEmpty(stagingDir) && Directory.Exists(stagingDir))
                {
                    try { Directory.Delete(stagingDir, true); }
                    catch (IOException cleanupError) { logActivity("PowerShell 7: Staging cleanup warning - " + cleanupError.Message); }
                    catch (UnauthorizedAccessException cleanupError) { logActivity("PowerShell 7: Staging cleanup warning - " + cleanupError.Message); }
                }
                throw;
            }
            catch (Exception ex)
            {
                version = "";
                updateStatus(componentName, "⚠ Error: " + ex.Message, 0);
                logActivity("PowerShell 7: Error - " + ex.Message);
                if (!string.IsNullOrEmpty(stagingDir) && Directory.Exists(stagingDir))
                {
                    try { Directory.Delete(stagingDir, true); }
                    catch (IOException cleanupError) { logActivity("PowerShell 7: Staging cleanup warning - " + cleanupError.Message); }
                    catch (UnauthorizedAccessException cleanupError) { logActivity("PowerShell 7: Staging cleanup warning - " + cleanupError.Message); }
                }
            }

            return string.IsNullOrEmpty(version)
                ? new KeyValuePair<string, string>()
                : new KeyValuePair<string, string>("PWSH7_VERSION", version);
        }

        private async Task DownloadFileAsync(HttpClient client, string url, string destPath, string componentName,
            int startProgress, int endProgress, System.Threading.CancellationToken cancellationToken)
        {
            int lastProgress = startProgress;
            await CommonHelper.DownloadFileWithRetryAsync(
                client,
                url,
                destPath,
                cancellationToken,
                (downloaded, total) =>
                {
                    if (total <= 0)
                        return;
                    int progress = startProgress + (int)((endProgress - startProgress) * downloaded / total);
                    if (progress > lastProgress)
                    {
                        updateStatus(componentName, "Downloading...", Math.Min(progress, endProgress));
                        lastProgress = progress;
                    }
                });
        }

        private async Task ExtractZipAsync(string zipPath, string extractPath, string componentName,
            int startProgress, int endProgress, System.Threading.CancellationToken cancellationToken)
        {
            await CommonHelper.ExtractZipWithProgressAsync(
                zipPath,
                extractPath,
                componentName,
                startProgress,
                endProgress,
                progress => updateStatus(componentName, "Extracting...", progress),
                message => logActivity(message),
                cancellationToken);
        }
    }

    /// <summary>
    /// Oh My Posh installer/upgrader
    /// </summary>
    public class OhMyPoshInstaller
    {
        private readonly string baseDir;
        private readonly Action<string, string, int> updateStatus;
        private readonly Action<string> logActivity;

        public OhMyPoshInstaller(string baseDir, Action<string, string, int> updateStatus, Action<string> logActivity = null)
        {
            this.baseDir = baseDir;
            this.updateStatus = updateStatus;
            this.logActivity = logActivity ?? (_ => { });
        }

        public Task<KeyValuePair<string, string>> InstallAsync(string tempBaseDir)
        {
            return InstallAsync(tempBaseDir, default(System.Threading.CancellationToken));
        }

        public async Task<KeyValuePair<string, string>> InstallAsync(string tempBaseDir,
            System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string componentName = "Oh My Posh";
            string version = "";
            string tempDir = null;
            string stagedExePath = null;

            try
            {
                updateStatus(componentName, "Checking version...", 5);
                logActivity("Oh My Posh: Checking latest version...");

                string downloadUrl = "";

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "VSCode-Portable-Installer");
                    client.Timeout = TimeSpan.FromMinutes(5);

                    try
                    {
                        // Use GitHub redirect (no API rate limit) to get latest version
                        using (var response = await CommonHelper.SendHeadRequestWithRetryAsync(
                            client,
                            "https://github.com/JanDeDobbeleer/oh-my-posh/releases/latest",
                            cancellationToken).ConfigureAwait(false))
                        {
                            response.EnsureSuccessStatusCode();
                            string redirectUrl = response.RequestMessage.RequestUri.ToString();
                            var versionMatch = Regex.Match(redirectUrl, @"/releases/tag/v([\d\.]+)");
                            if (!versionMatch.Success)
                                throw new InvalidDataException("Version not found in redirect URL: " + redirectUrl);

                            version = versionMatch.Groups[1].Value;
                            downloadUrl = "https://github.com/JanDeDobbeleer/oh-my-posh/releases/download/v" + version + "/install-x64.msix";
                            logActivity("Oh My Posh: Found latest version " + version + " (via redirect)");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        string errorMsg = "Failed to check Oh My Posh version. ";
                        errorMsg += "Internet connection required for installation. ";
                        errorMsg += "Error: " + ex.Message;

                        updateStatus(componentName, "⚠ Version check failed", 0);
                        logActivity("Oh My Posh: CRITICAL ERROR - " + errorMsg);
                        logActivity("Oh My Posh: Exception type: " + ex.GetType().Name);
                        if (ex.InnerException != null)
                            logActivity("Oh My Posh: Inner exception: " + ex.InnerException.Message);

                        throw new Exception(errorMsg);
                    }
                }

                updateStatus(componentName, "Downloading...", 10);
                logActivity("Oh My Posh: Downloading " + version + "...");

                tempDir = Path.Combine(tempBaseDir, "ohmyposh");
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                string msixPath = Path.Combine(tempDir, "ohmyposh.msix");

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(20);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("CodeNix/1.0");
                    client.DefaultRequestHeaders.ConnectionClose = false; // Keep-Alive
                    await CommonHelper.DownloadFileWithRetryAsync(client, downloadUrl, msixPath, cancellationToken).ConfigureAwait(false);
                }

                if (!File.Exists(msixPath) || new FileInfo(msixPath).Length == 0)
                    throw new InvalidDataException("Oh My Posh download was empty.");

                logActivity("Oh My Posh: Download completed");
                updateStatus(componentName, "Extracting...", 55);
                logActivity("Oh My Posh: Extracting...");
                cancellationToken.ThrowIfCancellationRequested();

                string extractDir = Path.Combine(tempDir, "ohmyposh_extract");
                ZipFile.ExtractToDirectory(msixPath, extractDir);
                cancellationToken.ThrowIfCancellationRequested();

                updateStatus(componentName, "Installing...", 75);

                var exeFiles = Directory.GetFiles(extractDir, "oh-my-posh.exe", SearchOption.AllDirectories);
                if (exeFiles.Length > 0)
                {
                    updateStatus(componentName, "Replacing executable...", 85);
                    string pwshBin = Path.Combine(baseDir, "data", "lib", "pwsh", "bin");
                    Directory.CreateDirectory(pwshBin);

                    logActivity("Oh My Posh: Replacing executable...");
                    cancellationToken.ThrowIfCancellationRequested();

                    string targetExe = Path.Combine(pwshBin, "oh-my-posh.exe");
                    stagedExePath = targetExe + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.Copy(exeFiles[0], stagedExePath, true);
                    if (new FileInfo(stagedExePath).Length == 0)
                        throw new InvalidDataException("Downloaded Oh My Posh executable was empty.");
                    if (File.Exists(targetExe))
                        File.Replace(stagedExePath, targetExe, null);
                    else
                        File.Move(stagedExePath, targetExe);
                    stagedExePath = null;

                    updateStatus(componentName, "✓ Completed", 100);
                    logActivity("Oh My Posh: Installed " + version);
                }
                else
                {
                    throw new InvalidDataException("Oh My Posh package did not contain oh-my-posh.exe.");
                }

                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (IOException ex)
                {
                    logActivity("Oh My Posh: Temporary file cleanup warning - " + ex.Message);
                }
                catch (UnauthorizedAccessException ex)
                {
                    logActivity("Oh My Posh: Temporary file cleanup warning - " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                version = "";
                updateStatus(componentName, "⚠ Error: " + ex.Message, 0);
                logActivity("Oh My Posh: Error - " + ex.Message);
                if (!string.IsNullOrEmpty(stagedExePath) && File.Exists(stagedExePath))
                {
                    try { File.Delete(stagedExePath); }
                    catch (IOException cleanupError) { logActivity("Oh My Posh: Staging cleanup warning - " + cleanupError.Message); }
                    catch (UnauthorizedAccessException cleanupError) { logActivity("Oh My Posh: Staging cleanup warning - " + cleanupError.Message); }
                }
                if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); }
                    catch (IOException cleanupError) { logActivity("Oh My Posh: Temporary cleanup warning - " + cleanupError.Message); }
                    catch (UnauthorizedAccessException cleanupError) { logActivity("Oh My Posh: Temporary cleanup warning - " + cleanupError.Message); }
                }
                if (ex is OperationCanceledException)
                    throw;
            }

            return string.IsNullOrEmpty(version)
                ? new KeyValuePair<string, string>()
                : new KeyValuePair<string, string>("OHMYPOSH_VERSION", version);
        }
    }

    /// <summary>
    /// PowerShell module installer/upgrader
    /// </summary>
    public class ModuleInstaller
    {
        private readonly string baseDir;
        private readonly Action<string, string, int> updateStatus;
        private readonly Action<string> logActivity;

        public ModuleInstaller(string baseDir, Action<string, string, int> updateStatus, Action<string> logActivity = null)
        {
            this.baseDir = baseDir;
            this.updateStatus = updateStatus;
            this.logActivity = logActivity ?? (_ => { });
        }

        public Task<KeyValuePair<string, string>> InstallModuleAsync(string moduleName, string versionKey, int maxWaitSeconds = 30)
        {
            return InstallModuleAsync(moduleName, versionKey, maxWaitSeconds,
                default(System.Threading.CancellationToken));
        }

        public async Task<KeyValuePair<string, string>> InstallModuleAsync(string moduleName, string versionKey,
            int maxWaitSeconds, System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string version = "";
            string modulesPath = Path.Combine(baseDir, "data", "lib", "pwsh", "Modules");
            string tempZip = Path.Combine(Path.GetTempPath(), moduleName + "_" + Guid.NewGuid().ToString().Substring(0, 8) + ".zip");
            string extractPath = Path.Combine(Path.GetTempPath(), moduleName + "_extract_" + Guid.NewGuid().ToString().Substring(0, 8));
            string stagingModuleDir = null;

            try
            {
                updateStatus(moduleName, "Downloading...", 10);
                logActivity(moduleName + ": Downloading from PSGallery API...");

                Directory.CreateDirectory(modulesPath);
                string downloadUrl = "https://www.powershellgallery.com/api/v2/package/" + moduleName;
                bool downloadSucceeded = false;

                try
                {
                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromMinutes(5);
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("CodeNix/1.0");
                        await CommonHelper.DownloadFileWithRetryAsync(
                            client,
                            downloadUrl,
                            tempZip,
                            cancellationToken).ConfigureAwait(false);
                    }

                    downloadSucceeded = true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (TimeoutException)
                {
                    throw;
                }
                catch (Exception dlEx)
                {
                    logActivity(moduleName + ": Fast download failed, falling back to Save-Module: " + dlEx.Message);
                }

                if (!downloadSucceeded)
                    return await InstallModuleLegacyAsync(moduleName, versionKey, maxWaitSeconds, cancellationToken).ConfigureAwait(false);

                updateStatus(moduleName, "Extracting...", 50);
                logActivity(moduleName + ": Extracting package...");
                Directory.CreateDirectory(extractPath);
                bool extractSucceeded = false;

                try
                {
                    await CommonHelper.ExtractZipWithProgressAsync(
                        tempZip,
                        extractPath,
                        moduleName,
                        50,
                        80,
                        progress => updateStatus(moduleName, "Extracting...", progress),
                        logActivity,
                        cancellationToken).ConfigureAwait(false);

                    extractSucceeded = true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logActivity(moduleName + ": Fast extract failed, falling back to Save-Module: " + ex.Message);
                }

                if (!extractSucceeded)
                {
                    try { if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true); } catch { }
                    try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                    return await InstallModuleLegacyAsync(moduleName, versionKey, maxWaitSeconds, cancellationToken).ConfigureAwait(false);
                }

                // Parse nuspec or psd1 for version
                updateStatus(moduleName, "Configuring...", 80);
                string[] nuspecs = Directory.GetFiles(extractPath, "*.nuspec", SearchOption.AllDirectories);
                if (nuspecs.Length > 0)
                {
                    string nuspecText = File.ReadAllText(nuspecs[0]);
                    var match = System.Text.RegularExpressions.Regex.Match(nuspecText, @"<version>(.+?)</version>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        version = match.Groups[1].Value.Trim();
                    }
                }

                if (string.IsNullOrEmpty(version))
                {
                    string[] psd1s = Directory.GetFiles(extractPath, "*.psd1", SearchOption.AllDirectories);
                    if (psd1s.Length > 0)
                    {
                        string psd1Text = File.ReadAllText(psd1s[0]);
                        var match = System.Text.RegularExpressions.Regex.Match(psd1Text, @"ModuleVersion\s*=\s*'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            version = match.Groups[1].Value.Trim();
                        }
                        else
                        {
                            throw new InvalidDataException("Could not read the " + moduleName + " package version.");
                        }
                    }
                    else
                    {
                        throw new InvalidDataException("The " + moduleName + " package contains no module manifest.");
                    }
                }

                string finalModuleDir = Path.Combine(modulesPath, moduleName, version);
                string moduleParentDir = Path.GetDirectoryName(finalModuleDir);
                Directory.CreateDirectory(moduleParentDir);
                stagingModuleDir = Path.Combine(modulesPath, "." + moduleName + ".stage-" + Guid.NewGuid().ToString("N"));
                string backupModuleDir = Path.Combine(modulesPath, "." + moduleName + ".backup-" + version);
                cancellationToken.ThrowIfCancellationRequested();
                CommonHelper.CopyDirectoryRecursive(extractPath, stagingModuleDir);
                if (Directory.GetFiles(stagingModuleDir, "*.psd1", SearchOption.AllDirectories).Length == 0)
                    throw new InvalidDataException("The extracted " + moduleName + " package has no module manifest.");

                await EnsureModernUnixCompatibilityAsync(moduleName, stagingModuleDir, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                CommonHelper.ReplaceDirectoryAtomically(stagingModuleDir, finalModuleDir, backupModuleDir, logActivity);
                try { Directory.Delete(extractPath, true); }
                catch (IOException ex) { logActivity(moduleName + ": Temporary extraction cleanup warning - " + ex.Message); }
                catch (UnauthorizedAccessException ex) { logActivity(moduleName + ": Temporary extraction cleanup warning - " + ex.Message); }
                try { File.Delete(tempZip); }
                catch (IOException ex) { logActivity(moduleName + ": Temporary package cleanup warning - " + ex.Message); }
                catch (UnauthorizedAccessException ex) { logActivity(moduleName + ": Temporary package cleanup warning - " + ex.Message); }

                logActivity(moduleName + ": Successfully installed version " + version);
                return new KeyValuePair<string, string>(versionKey, version);
            }
            catch (Exception ex)
            {
                logActivity(moduleName + ": Installation failed - " + ex.Message);
                if (!string.IsNullOrEmpty(stagingModuleDir) && Directory.Exists(stagingModuleDir))
                {
                    try { Directory.Delete(stagingModuleDir, true); }
                    catch (IOException cleanupError) { logActivity(moduleName + ": Staging cleanup warning - " + cleanupError.Message); }
                    catch (UnauthorizedAccessException cleanupError) { logActivity(moduleName + ": Staging cleanup warning - " + cleanupError.Message); }
                }
                try { if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true); } catch { }
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                if (ex is OperationCanceledException)
                    throw;
                return new KeyValuePair<string, string>(versionKey, "");
            }
        }

        public Task<KeyValuePair<string, string>> InstallModuleLegacyAsync(string moduleName, string versionKey, int maxWaitSeconds = 30)
        {
            return InstallModuleLegacyAsync(moduleName, versionKey, maxWaitSeconds,
                default(System.Threading.CancellationToken));
        }

        public async Task<KeyValuePair<string, string>> InstallModuleLegacyAsync(string moduleName, string versionKey,
            int maxWaitSeconds, System.Threading.CancellationToken cancellationToken)
        {
                cancellationToken.ThrowIfCancellationRequested();
                string version = "";

                try
                {
                    updateStatus(moduleName, "Waiting for PowerShell...", 10);
                    logActivity(moduleName + ": Waiting for PowerShell to be ready...");

                    string pwshExe = Path.Combine(baseDir, "data", "lib", "pwsh", "pwsh.exe");
                int retries = 0;
                while (!File.Exists(pwshExe) && retries < maxWaitSeconds * 2)
                {
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                    retries++;
                }

                if (!File.Exists(pwshExe))
                {
                    updateStatus(moduleName, "⚠ PowerShell not found", 0);
                    logActivity(moduleName + ": ERROR - PowerShell not found at: " + pwshExe);
                    return new KeyValuePair<string, string>(versionKey, "");
                }

                logActivity(moduleName + ": PowerShell found at: " + pwshExe);
                updateStatus(moduleName, "Installing...", 40);
                logActivity(moduleName + ": Installing module via Save-Module...");

                string modulesPath = Path.Combine(baseDir, "data", "lib", "pwsh", "Modules");
                Directory.CreateDirectory(modulesPath);
                logActivity(moduleName + ": Modules path: " + modulesPath);

                // Install module
                updateStatus(moduleName, "Downloading...", 50);
                logActivity(moduleName + ": Executing Save-Module command...");

                var psi = new ProcessStartInfo
                {
                    FileName = pwshExe,
                    Arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -Command \"Save-Module -Name " + moduleName + " -Repository PSGallery -Path '" + modulesPath + "' -Force -Confirm:$false -ErrorAction Stop\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(psi))
                using (cancellationToken.Register(() =>
                {
                    try { if (!process.HasExited) process.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }))
                {
                    // Read output streams asynchronously to prevent deadlock
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    // Simulate progress during download (runs in background)
                    var progressTask = Task.Run(async () =>
                    {
                        int progress = 55;
                        while (!process.HasExited && progress < 85)
                        {
                            try { await Task.Delay(5000, cancellationToken).ConfigureAwait(false); }
                            catch (OperationCanceledException) { return; }
                            progress += 3;
                            if (!process.HasExited)
                            {
                                updateStatus(moduleName, "Downloading...", Math.Min(progress, 85));
                                // Only log at significant milestones to reduce clutter
                                if (progress % 15 == 0 || progress >= 70)
                                {
                                    logActivity(moduleName + ": Still downloading... (" + progress + "%)");
                                }
                            }
                        }
                    });

                    // Wait for process to complete with timeout
                    logActivity(moduleName + ": Waiting for Save-Module to complete (5 min timeout)...");

                    // Use proper async wait with timeout
                    var timeoutTask = Task.Delay(300000, cancellationToken); // 5 min timeout
                    var processTask = Task.Run(() => process.WaitForExit());
                    var completedTask = await Task.WhenAny(processTask, timeoutTask);

                    if (cancellationToken.IsCancellationRequested)
                    {
                        try { if (!process.HasExited) process.Kill(); }
                        catch (InvalidOperationException) { }
                        catch (System.ComponentModel.Win32Exception) { }
                        await processTask.ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (completedTask == timeoutTask)
                    {
                        logActivity(moduleName + ": Timeout - killing process");
                        try { process.Kill(); } catch { }
                        updateStatus(moduleName, "⚠ Timeout", 0);
                        return new KeyValuePair<string, string>(versionKey, "");
                    }

                    // Ensure process has fully exited
                    if (!process.HasExited)
                    {
                        process.WaitForExit(); // Wait without timeout for full exit
                    }

                    string output = await outputTask;
                    string error = await errorTask;

                    try { await progressTask; } catch { } // Wait for progress task to complete

                    logActivity(moduleName + ": Save-Module completed with exit code: " + process.ExitCode);

                    if (process.ExitCode != 0)
                    {
                        logActivity(moduleName + ": Save-Module failed with exit code " + process.ExitCode);
                        if (!string.IsNullOrEmpty(error))
                            logActivity(moduleName + ": Error output: " + error.Trim());
                        if (!string.IsNullOrEmpty(output))
                            logActivity(moduleName + ": Standard output: " + output.Trim());
                        throw new InvalidOperationException(moduleName + " Save-Module failed with exit code " + process.ExitCode + ".");
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(output))
                            logActivity(moduleName + ": Output: " + output.Trim());
                    }
                }

                updateStatus(moduleName, "Verifying...", 90);

                // Wait a moment for file system to sync
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);

                string moduleDir = Path.Combine(modulesPath, moduleName);
                logActivity(moduleName + ": Checking module directory: " + moduleDir);

                if (Directory.Exists(moduleDir))
                {
                    var versionDirs = Directory.GetDirectories(moduleDir);
                    logActivity(moduleName + ": Found " + versionDirs.Length + " version directories");

                    version = versionDirs
                        .Select(d => Path.GetFileName(d))
                        .OrderByDescending(ParseModuleVersion)
                        .FirstOrDefault() ?? "";

                    if (!string.IsNullOrEmpty(version))
                    {
                        await EnsureModernUnixCompatibilityAsync(moduleName, Path.Combine(moduleDir, version), cancellationToken).ConfigureAwait(false);
                        updateStatus(moduleName, "✓ Completed", 100);
                        logActivity(moduleName + ": Successfully installed version " + version);
                    }
                    else
                    {
                        updateStatus(moduleName, "⚠ Version not found", 0);
                        logActivity(moduleName + ": ERROR - No version directory found");
                    }
                }
                else
                {
                    updateStatus(moduleName, "⚠ Module not found", 0);
                    logActivity(moduleName + ": ERROR - Module directory not found: " + moduleDir);
                }
            }
            catch (Exception ex)
            {
                version = "";
                if (ex is OperationCanceledException)
                    throw;
                updateStatus(moduleName, "⚠ Error: " + ex.Message, 0);
                logActivity(moduleName + ": EXCEPTION - " + ex.Message);
                logActivity(moduleName + ": Stack trace: " + ex.StackTrace);
            }

            return string.IsNullOrEmpty(version)
                ? new KeyValuePair<string, string>()
                : new KeyValuePair<string, string>(versionKey, version);
        }

        private async Task EnsureModernUnixCompatibilityAsync(string moduleName, string moduleDirectory,
            System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(moduleName, "modern-unix-win", StringComparison.OrdinalIgnoreCase))
                return;

            string executable = Path.Combine(moduleDirectory, "bin", "lsd.exe");
            if (!File.Exists(executable) || ReadLsdVersion(executable) >= new Version(1, 2, 0))
                return;

            const string releaseUrl = "https://github.com/lsd-rs/lsd/releases/download/v1.2.0/lsd-v1.2.0-x86_64-pc-windows-msvc.zip";
            const string expectedHash = "a65b9f457153934a7ce1f7e0fe1c4922a95bb2822222126e4fbaa6ee9c0d78e5";
            string temporaryDirectory = Path.Combine(Path.GetTempPath(), "codenix-lsd-" + Guid.NewGuid().ToString("N"));
            string replacement = executable + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                logActivity(moduleName + ": Updating bundled lsd to 1.2.0 for Windows ACL compatibility...");
                Directory.CreateDirectory(temporaryDirectory);
                string archivePath = Path.Combine(temporaryDirectory, "lsd.zip");
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("CodeNix/1.0");
                    await CommonHelper.DownloadFileWithRetryAsync(client, releaseUrl, archivePath,
                        cancellationToken).ConfigureAwait(false);
                }

                using (var algorithm = System.Security.Cryptography.SHA256.Create())
                using (var stream = File.OpenRead(archivePath))
                {
                    string actualHash = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
                    if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The lsd package SHA256 checksum does not match the official release.");
                }

                string stagedExecutable = Path.Combine(temporaryDirectory, "lsd.exe");
                using (var archive = ZipFile.OpenRead(archivePath))
                {
                    var entry = archive.Entries.SingleOrDefault(item =>
                        string.Equals(Path.GetFileName(item.FullName), "lsd.exe", StringComparison.OrdinalIgnoreCase));
                    if (entry == null || entry.Length == 0)
                        throw new InvalidDataException("The lsd package does not contain a valid executable.");
                    entry.ExtractToFile(stagedExecutable);
                }
                if (ReadLsdVersion(stagedExecutable) != new Version(1, 2, 0))
                    throw new InvalidDataException("The lsd executable version does not match the verified release.");

                cancellationToken.ThrowIfCancellationRequested();
                File.Copy(stagedExecutable, replacement);
                File.Replace(replacement, executable, executable + ".legacy-backup");
                logActivity(moduleName + ": Bundled lsd upgraded to 1.2.0; original executable preserved.");
            }
            finally
            {
                if (File.Exists(replacement)) File.Delete(replacement);
                if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
            }
        }

        private static Version ReadLsdVersion(string executable)
        {
            using (var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(10000))
                {
                    process.Kill();
                    process.WaitForExit();
                    throw new TimeoutException("Timed out while checking the lsd version.");
                }
                string versionText = output.GetAwaiter().GetResult();
                string errorText = error.GetAwaiter().GetResult();
                var match = System.Text.RegularExpressions.Regex.Match(versionText, @"\blsd\s+(\d+\.\d+\.\d+)");
                if (process.ExitCode != 0 || !match.Success)
                    throw new InvalidDataException("Could not determine the bundled lsd version: " + errorText);
                return Version.Parse(match.Groups[1].Value);
            }
        }

        private static Version ParseModuleVersion(string value)
        {
            Version parsed;
            return Version.TryParse(value, out parsed) ? parsed : new Version(0, 0);
        }
    }

    public class VSCodeInstaller
    {
        private readonly string baseDir;
        private readonly Action<string, string, int> updateStatus;
        private readonly Action<string> logActivity;

        public VSCodeInstaller(string baseDir, Action<string, string, int> updateStatus, Action<string> logActivity = null)
        {
            this.baseDir = baseDir;
            this.updateStatus = updateStatus;
            this.logActivity = logActivity ?? (_ => { });
        }

        public static void RecoverInterruptedPortableUpdate(string baseDir)
        {
            string dataDir = Path.Combine(baseDir, "data");
            if (!Directory.Exists(dataDir))
                return;

            RecoverInterruptedUpdate(
                baseDir,
                Path.Combine(dataDir, ".codenix-vscode-stage"),
                Path.Combine(dataDir, ".codenix-vscode-backup"),
                Path.Combine(dataDir, ".codenix-vscode-update"));
        }

        public async Task<KeyValuePair<string, string>> UpgradeAsync(string tempBaseDir)
        {
            const string componentName = "VSCode";
            string version = "";
            string tempDir = Path.Combine(tempBaseDir, "vscode");
            string dataDir = Path.Combine(baseDir, "data");
            string stageDir = Path.Combine(dataDir, ".codenix-vscode-stage");
            string backupDir = Path.Combine(dataDir, ".codenix-vscode-backup");
            string updateMarker = Path.Combine(dataDir, ".codenix-vscode-update");

            try
            {
                CommonHelper.EnableTls12();
                Directory.CreateDirectory(dataDir);
                RecoverInterruptedUpdate(baseDir, stageDir, backupDir, updateMarker);
                if (!File.Exists(Path.Combine(baseDir, "Code.exe")))
                    throw new FileNotFoundException("The portable VS Code installation was not found.", Path.Combine(baseDir, "Code.exe"));

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(20);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("CodeNix/1.0");
                    client.DefaultRequestHeaders.ConnectionClose = false;

                    updateStatus(componentName, "Checking version...", 5);
                    logActivity("VS Code: Checking latest version...");

                    string url = "https://update.code.visualstudio.com/latest/win32-x64-archive/stable";
                    string resolvedVersion;
                    using (var response = await CommonHelper.SendHeadRequestWithRetryAsync(
                        client,
                        url,
                        default(System.Threading.CancellationToken)).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        string resolvedUrl = response.RequestMessage.RequestUri.ToString();
                        var match = Regex.Match(resolvedUrl, @"VSCode-win32-x64-([\d\.]+)\.zip");
                        if (!match.Success)
                            throw new InvalidDataException("VS Code version was not found in the resolved download URL.");
                        resolvedVersion = match.Groups[1].Value;
                    }

                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                    Directory.CreateDirectory(tempDir);

                    string zipPath = Path.Combine(tempDir, "vscode.zip");
                    string downloadUrl = "https://update.code.visualstudio.com/" + resolvedVersion + "/win32-x64-archive/stable";
                    updateStatus(componentName, "Downloading...", 10);
                    logActivity("VS Code: Downloading version " + resolvedVersion + "...");
                    await DownloadFileAsync(client, downloadUrl, zipPath, componentName, 10, 50);
                    ValidateVSCodeArchive(zipPath);

                    if (Directory.Exists(stageDir))
                        Directory.Delete(stageDir, true);
                    Directory.CreateDirectory(stageDir);
                    updateStatus(componentName, "Extracting...", 55);
                    await ExtractZipAsync(zipPath, stageDir, componentName, 55, 90);
                    ValidateStagedVSCode(stageDir);

                    updateStatus(componentName, "Installing...", 93);
                    CommitStagedVSCode(stageDir, backupDir, updateMarker);
                    version = resolvedVersion;
                    updateStatus(componentName, "✓ Completed", 100);
                    logActivity("VS Code: Update completed successfully - " + version);
                }
            }
            catch (Exception ex)
            {
                updateStatus(componentName, "⚠ Error: " + ex.Message, 0);
                logActivity("VS Code: Error - " + ex.Message);
                version = "";
                try
                {
                    RecoverInterruptedUpdate(baseDir, stageDir, backupDir, updateMarker);
                }
                catch (Exception recoveryError)
                {
                    logActivity("VS Code: Recovery failed - " + recoveryError.Message);
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); }
                    catch (IOException ex) { logActivity("VS Code: Temporary download cleanup warning - " + ex.Message); }
                    catch (UnauthorizedAccessException ex) { logActivity("VS Code: Temporary download cleanup warning - " + ex.Message); }
                }

                if (Directory.Exists(stageDir) && !File.Exists(updateMarker))
                {
                    try { Directory.Delete(stageDir, true); }
                    catch (IOException ex) { logActivity("VS Code: Staging cleanup warning - " + ex.Message); }
                    catch (UnauthorizedAccessException ex) { logActivity("VS Code: Staging cleanup warning - " + ex.Message); }
                }
            }

            return string.IsNullOrEmpty(version)
                ? new KeyValuePair<string, string>()
                : new KeyValuePair<string, string>("VSCODE_VERSION", version);
        }

        private static void ValidateVSCodeArchive(string zipPath)
        {
            if (!File.Exists(zipPath) || new FileInfo(zipPath).Length == 0)
                throw new InvalidDataException("VS Code archive download was empty.");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                if (archive.GetEntry("Code.exe") == null)
                    throw new InvalidDataException("VS Code archive does not contain Code.exe.");
            }
        }

        private static void ValidateStagedVSCode(string stageDir)
        {
            string codeExe = Path.Combine(stageDir, "Code.exe");
            if (!File.Exists(codeExe) || new FileInfo(codeExe).Length == 0)
                throw new InvalidDataException("Staged VS Code package does not contain a valid Code.exe.");
        }

        private void CommitStagedVSCode(string stageDir, string backupDir, string updateMarker)
        {
            if (Directory.Exists(backupDir))
                Directory.Delete(backupDir, true);
            Directory.CreateDirectory(backupDir);
            WriteUpdateMarker(updateMarker, "BackingUp");

            try
            {
                foreach (string entry in Directory.GetFileSystemEntries(baseDir))
                {
                    string name = Path.GetFileName(entry);
                    if (!IsReplaceableVSCodeEntry(name))
                        continue;

                    MoveEntry(entry, Path.Combine(backupDir, name));
                }

                WriteUpdateMarker(updateMarker, "Installing");
                foreach (string entry in Directory.GetFileSystemEntries(stageDir))
                {
                    string name = Path.GetFileName(entry);
                    if (!IsReplaceableVSCodeEntry(name))
                        continue;

                    MoveEntry(entry, Path.Combine(baseDir, name));
                }

                ValidateStagedVSCode(baseDir);
                WriteUpdateMarker(updateMarker, "Committed");
                try { File.Delete(updateMarker); }
                catch (IOException ex) { logActivity("VS Code: Update marker cleanup warning - " + ex.Message); }
                catch (UnauthorizedAccessException ex) { logActivity("VS Code: Update marker cleanup warning - " + ex.Message); }
            }
            catch (Exception commitError)
            {
                try
                {
                    RecoverInterruptedUpdate(baseDir, stageDir, backupDir, updateMarker);
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("VS Code update failed and rollback also failed.", commitError, rollbackError);
                }

                throw;
            }

            if (Directory.Exists(backupDir))
            {
                try { Directory.Delete(backupDir, true); }
                catch (IOException ex) { logActivity("VS Code: Old file cleanup warning - " + ex.Message); }
                catch (UnauthorizedAccessException ex) { logActivity("VS Code: Old file cleanup warning - " + ex.Message); }
            }
        }

        private static void RecoverInterruptedUpdate(string baseDir, string stageDir, string backupDir, string updateMarker)
        {
            string phase = File.Exists(updateMarker) ? File.ReadAllText(updateMarker).Trim() : string.Empty;
            bool backupExists = Directory.Exists(backupDir);

            if (string.Equals(phase, "Installing", StringComparison.OrdinalIgnoreCase) && !backupExists)
                throw new InvalidOperationException("The previous VS Code update backup is missing.");
            if (string.Equals(phase, "BackingUp", StringComparison.OrdinalIgnoreCase) &&
                !backupExists &&
                !File.Exists(Path.Combine(baseDir, "Code.exe")))
            {
                throw new InvalidOperationException("The previous VS Code update has no usable backup.");
            }

            if (string.Equals(phase, "Installing", StringComparison.OrdinalIgnoreCase))
                DeleteReplaceableVSCodeEntries(baseDir);
            else if (!string.IsNullOrEmpty(phase) &&
                     !string.Equals(phase, "BackingUp", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(phase, "Committed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unknown VS Code update recovery state: " + phase);

            bool shouldRestoreBackup =
                string.Equals(phase, "BackingUp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(phase, "Installing", StringComparison.OrdinalIgnoreCase) ||
                (string.IsNullOrEmpty(phase) && !File.Exists(Path.Combine(baseDir, "Code.exe")));

            if (backupExists && shouldRestoreBackup)
            {
                foreach (string backupEntry in Directory.GetFileSystemEntries(backupDir))
                {
                    string targetPath = Path.Combine(baseDir, Path.GetFileName(backupEntry));
                    DeletePathIfExists(targetPath);
                    MoveEntry(backupEntry, targetPath);
                }
            }
            else if (backupExists)
            {
                Directory.Delete(backupDir, true);
            }
            if (Directory.Exists(backupDir))
                Directory.Delete(backupDir, true);
            if (File.Exists(updateMarker))
                File.Delete(updateMarker);
            if (Directory.Exists(stageDir))
                Directory.Delete(stageDir, true);
        }

        private static bool IsReplaceableVSCodeEntry(string name)
        {
            return !name.Equals("data", StringComparison.OrdinalIgnoreCase) &&
                   !name.Equals("launcher.exe", StringComparison.OrdinalIgnoreCase) &&
                   !name.Equals("CodeNix.exe", StringComparison.OrdinalIgnoreCase) &&
                   !name.Equals(CommonHelper.RuntimeInfoFileName, StringComparison.OrdinalIgnoreCase) &&
                   !name.Equals(CommonHelper.RuntimeLogFileName, StringComparison.OrdinalIgnoreCase) &&
                   !name.Equals("upgrade.log", StringComparison.OrdinalIgnoreCase) &&
                   !name.StartsWith("upgrade_", StringComparison.OrdinalIgnoreCase);
        }

        private static void DeleteReplaceableVSCodeEntries(string directory)
        {
            foreach (string entry in Directory.GetFileSystemEntries(directory))
            {
                if (IsReplaceableVSCodeEntry(Path.GetFileName(entry)))
                    DeletePathIfExists(entry);
            }
        }

        private static void DeletePathIfExists(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
            else if (File.Exists(path))
                File.Delete(path);
        }

        private static void MoveEntry(string sourcePath, string targetPath)
        {
            if (Directory.Exists(sourcePath))
                Directory.Move(sourcePath, targetPath);
            else
                File.Move(sourcePath, targetPath);
        }

        private static void WriteUpdateMarker(string markerPath, string phase)
        {
            string tempPath = markerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, phase);
            if (File.Exists(markerPath))
                File.Replace(tempPath, markerPath, null);
            else
                File.Move(tempPath, markerPath);
        }

        private async Task DownloadFileAsync(HttpClient client, string url, string destPath, string componentName, int startProgress, int endProgress)
        {
            int lastProgress = startProgress;
            await CommonHelper.DownloadFileWithRetryAsync(
                client,
                url,
                destPath,
                default(System.Threading.CancellationToken),
                (downloaded, total) =>
                {
                    if (total <= 0)
                        return;
                    int progress = startProgress + (int)((endProgress - startProgress) * downloaded / total);
                    if (progress > lastProgress)
                    {
                        updateStatus(componentName, "Downloading...", Math.Min(progress, endProgress));
                        lastProgress = progress;
                    }
                });
        }

        private async Task ExtractZipAsync(string zipPath, string extractPath, string componentName, int startProgress, int endProgress)
        {
            await CommonHelper.ExtractZipWithProgressAsync(
                zipPath,
                extractPath,
                componentName,
                startProgress,
                endProgress,
                progress => updateStatus(componentName, "Extracting...", progress),
                message => logActivity(message),
                default(System.Threading.CancellationToken));
        }
    }

    /// <summary>
    /// Font manager for registry-based font installation
    /// </summary>
    public class FontManager
    {
        [DllImport("gdi32.dll")]
        private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        private const int WM_FONTCHANGE = 0x001D;
        private const int HWND_BROADCAST = 0xffff;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        public static bool EnsureFontInstalled(string fontDir, string fontFile, string fontName)
        {
            string fontPath = Path.Combine(fontDir, fontFile);
            if (!File.Exists(fontPath))
                return false;

            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts"))
                {
                    if (key == null)
                        return false;

                    string registeredPath = key.GetValue(fontName) as string;
                    if (string.Equals(registeredPath, fontPath, StringComparison.OrdinalIgnoreCase))
                        return true;

                    key.SetValue(fontName, fontPath, RegistryValueKind.String);
                }

                AddFontResourceEx(fontPath, 0, IntPtr.Zero);
                IntPtr result;
                SendMessageTimeout(new IntPtr(HWND_BROADCAST), WM_FONTCHANGE, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 2000, out result);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Shortcut manager for creating/updating Windows shortcuts
    /// </summary>
    public class ShortcutManager
    {
        public static bool CreateOrUpdateShortcut(string folder, string name, string targetPath, string iconPath, string workingDir, string arguments = "--from-shortcut", bool forceUpdate = false)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(targetPath))
                    return false;

                Directory.CreateDirectory(folder);
                string shortcutPath = Path.Combine(folder, name + ".lnk");
                bool shortcutExists = File.Exists(shortcutPath);
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                    return false;

                shell = Activator.CreateInstance(shellType);
                dynamic shellObject = shell;
                shortcut = shellObject.CreateShortcut(shortcutPath);
                dynamic shortcutObject = shortcut;

                if (!forceUpdate && shortcutExists)
                {
                    try
                    {
                        string currentTarget = shortcutObject.TargetPath as string;
                        string currentWorkingDir = shortcutObject.WorkingDirectory as string;
                        string currentIcon = shortcutObject.IconLocation as string;
                        string currentArguments = shortcutObject.Arguments as string;
                        if (PathsEqual(currentTarget, targetPath) &&
                            PathsEqual(currentWorkingDir, workingDir) &&
                            string.Equals(currentArguments, arguments ?? string.Empty, StringComparison.Ordinal) &&
                            (string.Equals(currentIcon, iconPath ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(currentIcon, iconPath + ",0", StringComparison.OrdinalIgnoreCase)))
                            return true;
                    }
                    catch (Exception)
                    {
                    }
                }

                shortcutObject.TargetPath = targetPath;
                shortcutObject.WorkingDirectory = workingDir ?? string.Empty;
                shortcutObject.IconLocation = iconPath ?? string.Empty;
                shortcutObject.Arguments = arguments ?? string.Empty;
                shortcutObject.WindowStyle = 1;
                shortcutObject.Save();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut))
                {
                    try { Marshal.ReleaseComObject(shortcut); }
                    catch (System.Runtime.InteropServices.COMException) { }
                }
                if (shell != null && Marshal.IsComObject(shell))
                {
                    try { Marshal.ReleaseComObject(shell); }
                    catch (System.Runtime.InteropServices.COMException) { }
                }
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            try
            {
                return string.Equals(
                    Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        public static bool CreateShortcuts(string baseDir, string launcherPath, string iconPath)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string shortcutName = "VSCode (Portable)";

            bool desktopReady = CreateOrReplaceShortcut(desktop, shortcutName, launcherPath, iconPath, baseDir);
            bool startMenuReady = CreateOrReplaceShortcut(startMenu, shortcutName, launcherPath, iconPath, baseDir);
            return desktopReady && startMenuReady;
        }

        public static bool CreateOrReplaceShortcut(string folder, string name, string targetPath,
            string iconPath, string workingDir, string arguments = "--from-shortcut")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(name))
                    return false;

                string shortcutPath = Path.Combine(folder, name + ".lnk");
                if (File.Exists(shortcutPath))
                    File.Delete(shortcutPath);

                return CreateOrUpdateShortcut(folder, name, targetPath, iconPath, workingDir, arguments, true);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
