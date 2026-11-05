using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VSCodePortableCommon;

namespace VSCodePortableLauncher
{
    class Program
    {
        private static readonly HttpClient UpdateHttpClient = CreateUpdateHttpClient();

        static readonly string[] PlatformSensitiveVSCodeExtensions = new[]
        {
            "ms-python.python", "ms-python.vscode-python-envs", "ms-python.debugpy"
        };

        static readonly string[] OptionalAutoInstalledVSCodeExtensions = new string[0];

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                ServicePointManager.DefaultConnectionLimit = 32;
                ServicePointManager.Expect100Continue = false;
                CommonHelper.EnableTls12();

                string pvsDir = AppDomain.CurrentDomain.BaseDirectory;
                using (var updateMutex = new Mutex(false, GetUpdateMutexName(pvsDir)))
                {
                    bool lockAcquired;
                    try
                    {
                        lockAcquired = updateMutex.WaitOne(0);
                    }
                    catch (AbandonedMutexException)
                    {
                        lockAcquired = true;
                    }

                    if (!lockAcquired)
                    {
                        if (args.Contains("--check-versions-internal"))
                            return 0;

                        MessageBox.Show(
                            "Another CodeNix update check or update is already running for this folder. Try again after it finishes.",
                            "CodeNix is busy",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return 1;
                    }

                    try
                    {
                        RecoverInterruptedPackageUpdates(pvsDir);

                        if (args.Contains("--init"))
                            return InitializePVS();

                        if (args.Contains("--check-versions-internal"))
                        {
                            var checkErrors = CheckVersionsAsync(pvsDir).GetAwaiter().GetResult();
                            return checkErrors.Count == 0 ? 0 : 1;
                        }

                        return RunLauncher(pvsDir);
                    }
                    finally
                    {
                        updateMutex.ReleaseMutex();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message + "\n\n" + ex.StackTrace,
                    "VSCode Portable Launcher Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }

        static string GetUpdateMutexName(string pvsDir)
        {
            string normalizedPath = Path.GetFullPath(pvsDir).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));
                return "Local\\CodeNix.Update." + BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        static HttpClient CreateUpdateHttpClient()
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CodeNix/1.0");
            client.DefaultRequestHeaders.ConnectionClose = false;
            return client;
        }

        static void RecoverInterruptedPackageUpdates(string pvsDir)
        {
            string dataLibDir = Path.Combine(pvsDir, "data", "lib");
            CommonHelper.RecoverDirectoryReplacement(
                Path.Combine(dataLibDir, "pwsh"),
                Path.Combine(dataLibDir, ".pwsh-backup"));
            CommonHelper.RecoverDirectoryReplacement(
                Path.Combine(dataLibDir, "uv"),
                Path.Combine(dataLibDir, ".uv-backup"));
            VSCodeInstaller.RecoverInterruptedPortableUpdate(pvsDir);
        }

        static int RunLauncher(string pvsDir)
        {
            string pvsInfo = Path.Combine(pvsDir, CommonHelper.RuntimeInfoFileName);
            if (File.Exists(pvsInfo))
            {
                try
                {
                    CheckRuntimeMigration(pvsDir, pvsInfo);
                }
                catch (IOException ex)
                {
                    MessageBox.Show("Could not update portable folder metadata: " + ex.Message, "CodeNix warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (UnauthorizedAccessException ex)
                {
                    MessageBox.Show("Could not update portable folder metadata: " + ex.Message, "CodeNix warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            bool shortcutsReady = ManageShortcuts(pvsDir);
            bool fontsReady = EnsureFonts(pvsDir);
            string settingsError;
            bool settingsReady = EnsurePortableSettings(pvsDir, out settingsError);
            string powerShellThemeError;
            bool powerShellThemeReady = EnsurePortablePowerShellTheme(pvsDir, out powerShellThemeError);
            if (!shortcutsReady || !fontsReady || !settingsReady || !powerShellThemeReady)
            {
                var failures = new List<string>();
                if (!shortcutsReady)
                    failures.Add("Desktop or Start Menu shortcut");
                if (!fontsReady)
                    failures.Add("portable fonts");
                if (!settingsReady)
                    failures.Add("portable VS Code settings (" + settingsError + ")");
                if (!powerShellThemeReady)
                    failures.Add("portable PowerShell theme (" + powerShellThemeError + ")");
                MessageBox.Show(
                    "CodeNix could not fully prepare this PC: " + string.Join(", ", failures.ToArray()) + ".\r\nVS Code will still be launched.",
                    "Portable setup warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            var checkErrors = CheckVersionsAsync(pvsDir).GetAwaiter().GetResult();
            if (checkErrors.Count > 0)
            {
                MessageBox.Show(
                    "Some package versions could not be checked. CodeNix will continue with the installed versions.\r\n\r\n" +
                    string.Join("\r\n", checkErrors.ToArray()),
                    "Update check incomplete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            var pendingUpgrades = GetPendingUpgradeFlags(pvsDir);

            if (pendingUpgrades.Any())
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var form = new UpgradeForm(pvsDir, pendingUpgrades);
                Application.Run(form);
            }

            return LaunchVSCode(pvsDir) ? 0 : 1;
        }

        static List<string> GetPendingUpgradeFlags(string pvsDir)
        {
            var upgradeFlags = new[]
            {
                "upgrade_uv", "upgrade_pwsh7", "upgrade_ohmyposh",
                "upgrade_psfzf", "upgrade_modern_unix", "upgrade_vscode"
            };
            return upgradeFlags.Where(flag => File.Exists(Path.Combine(pvsDir, flag))).ToList();
        }

        static bool LaunchVSCode(string pvsDir)
        {
            string codeExe = Path.Combine(pvsDir, "Code.exe");
            if (!File.Exists(codeExe))
            {
                MessageBox.Show("Code.exe was not found in this portable folder. Run CodeNix.exe to install the environment.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            Process.Start(CreateVSCodeStartInfo(pvsDir));
            return true;
        }

        static ProcessStartInfo CreateVSCodeStartInfo(string pvsDir)
        {
            PortablePython.RebaseProjectsAsync(pvsDir,
                message => Debug.WriteLine(message)).GetAwaiter().GetResult();

            string codeExe = Path.Combine(pvsDir, "Code.exe");
            string extensionsDir = Path.Combine(pvsDir, "data", "extensions");
            string userDataDir = Path.Combine(pvsDir, "data", "user-data");

            var psi = new ProcessStartInfo
            {
                FileName = codeExe,
                Arguments = "--extensions-dir \"" + extensionsDir + "\" --user-data-dir \"" + userDataDir + "\"",
                WorkingDirectory = pvsDir,
                UseShellExecute = false
            };

            ApplyPortableRuntimeEnvironment(psi, pvsDir);
            return psi;
        }

        static void ApplyPortableRuntimeEnvironment(ProcessStartInfo psi, string pvsDir)
        {
            PortablePython.ApplyEnvironment(psi, pvsDir);

            var preferredPaths = new[]
            {
                Path.Combine(pvsDir, "data", "lib", "pwsh", "bin"),
                Path.Combine(pvsDir, "data", "venvs", "global", "Scripts"),
                Path.Combine(pvsDir, "bin"),
                Path.Combine(pvsDir, "data", "bin"),
                Path.Combine(pvsDir, "data", "lib", "uv-tools", "replx", "Scripts"),
                Path.Combine(pvsDir, "data", "lib", "pwsh")
            };

            string basePath = psi.EnvironmentVariables["PATH"];
            if (string.IsNullOrEmpty(basePath))
                basePath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            var pyenvRoots = new List<string>();
            CollectIfPresent(pyenvRoots, psi.EnvironmentVariables["PYENV"]);
            CollectIfPresent(pyenvRoots, psi.EnvironmentVariables["PYENV_ROOT"]);
            CollectIfPresent(pyenvRoots, psi.EnvironmentVariables["PYENV_HOME"]);

            RemoveEnvironmentVariable(psi, "PYENV");
            RemoveEnvironmentVariable(psi, "PYENV_ROOT");
            RemoveEnvironmentVariable(psi, "PYENV_HOME");
            RemoveEnvironmentVariable(psi, "PYENV_DIR");
            RemoveEnvironmentVariable(psi, "PYENV_VERSION");
            RemoveEnvironmentVariable(psi, "PYENV_SHELL");
            RemoveEnvironmentVariable(psi, "PYENV_HOOK_PATH");

            var pathEntries = basePath
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(existing => !IsPyenvPath(existing, pyenvRoots))
                .ToList();

            for (int i = preferredPaths.Length - 1; i >= 0; i--)
            {
                string candidate = preferredPaths[i];

                bool alreadyPresent = pathEntries.Any(existing =>
                    string.Equals(existing.TrimEnd('\\'), candidate.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
                if (!alreadyPresent)
                    pathEntries.Insert(0, candidate);
            }

            psi.EnvironmentVariables["PATH"] = string.Join(";", pathEntries.ToArray());
        }

        static void CollectIfPresent(ICollection<string> values, string candidate)
        {
            if (!string.IsNullOrEmpty(candidate))
                values.Add(candidate);
        }

        static void RemoveEnvironmentVariable(ProcessStartInfo psi, string name)
        {
            if (psi.EnvironmentVariables.ContainsKey(name))
                psi.EnvironmentVariables.Remove(name);
        }

        static bool IsPyenvPath(string pathEntry, IEnumerable<string> pyenvRoots)
        {
            if (string.IsNullOrEmpty(pathEntry))
                return false;

            string normalizedEntry = pathEntry.Trim().TrimEnd('\\');
            foreach (var root in pyenvRoots)
            {
                if (string.IsNullOrEmpty(root))
                    continue;

                string normalizedRoot = root.Trim().TrimEnd('\\');
                if (normalizedEntry.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return normalizedEntry.IndexOf("\\.pyenv\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalizedEntry.IndexOf("\\pyenv-win\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool CheckRuntimeMigration(string pvsDir, string pvsInfo)
        {
            if (!File.Exists(pvsInfo))
                return false;

            var lines = File.ReadAllLines(pvsInfo).ToList();
            string currentPath = pvsDir.TrimEnd('\\');
            string currentMachineName = Environment.MachineName;
            string currentDesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string currentProgramsPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);

            string storedPath = GetInfoValue(lines, "INSTALL_PATH");
            string storedMachineName = GetInfoValue(lines, "INSTALL_HOST_MACHINE");
            string storedDesktopPath = GetInfoValue(lines, "INSTALL_USER_DESKTOP");
            string storedProgramsPath = GetInfoValue(lines, "INSTALL_START_MENU");

            bool pathChanged = !PathsEqual(currentPath, storedPath);
            bool machineChanged = !string.Equals(currentMachineName ?? string.Empty, storedMachineName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            bool desktopChanged = !PathsEqual(currentDesktopPath, storedDesktopPath);
            bool programsChanged = !PathsEqual(currentProgramsPath, storedProgramsPath);
            bool missingContext = string.IsNullOrEmpty(storedMachineName) || string.IsNullOrEmpty(storedDesktopPath) || string.IsNullOrEmpty(storedProgramsPath);

            if (!pathChanged && !machineChanged && !desktopChanged && !programsChanged && !missingContext)
                return false;

            UpsertInfoValue(lines, "INSTALL_PATH", currentPath);
            UpsertInfoValue(lines, "INSTALL_HOST_MACHINE", currentMachineName);
            UpsertInfoValue(lines, "INSTALL_USER_DESKTOP", currentDesktopPath);
            UpsertInfoValue(lines, "INSTALL_START_MENU", currentProgramsPath);

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

            return true;
        }

        static string GetInfoValue(IList<string> lines, string key)
        {
            foreach (var line in lines)
            {
                if (line.StartsWith(key + "="))
                    return line.Substring(key.Length + 1);
            }

            return null;
        }

        static void UpsertInfoValue(IList<string> lines, string key, string value)
        {
            string normalizedValue = value ?? string.Empty;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].StartsWith(key + "="))
                {
                    lines[i] = key + "=" + normalizedValue;
                    return;
                }
            }

            lines.Add(key + "=" + normalizedValue);
        }

        static bool PathsEqual(string left, string right)
        {
            string normalizedLeft = string.IsNullOrEmpty(left) ? string.Empty : left.TrimEnd('\\');
            string normalizedRight = string.IsNullOrEmpty(right) ? string.Empty : right.TrimEnd('\\');
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        static bool ManageShortcuts(string pvsDir)
        {
            string launcherPath = Path.Combine(pvsDir, "launcher.exe");
            string iconPath = Path.Combine(pvsDir, "Code.exe");

            if (!File.Exists(launcherPath) || !File.Exists(iconPath))
                return false;

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string shortcutName = "VSCode (Portable)";

            bool desktopReady = ShortcutManager.CreateOrUpdateShortcut(desktop, shortcutName, launcherPath, iconPath, pvsDir, "--from-shortcut");
            bool startMenuReady = ShortcutManager.CreateOrUpdateShortcut(startMenu, shortcutName, launcherPath, iconPath, pvsDir, "--from-shortcut");
            return desktopReady && startMenuReady;
        }

        static bool EnsureFonts(string pvsDir)
        {
            string fontDir = Path.Combine(pvsDir, "data", "lib", "fonts");

            bool nerdFontReady = FontManager.EnsureFontInstalled(fontDir, "0xProtoNerdFont-Regular.ttf", "0xProto Nerd Font Mono");
            bool dalseoFontReady = FontManager.EnsureFontInstalled(fontDir, "DalseoHealingMedium.ttf", "DalseoHealing");
            return nerdFontReady && dalseoFontReady;
        }

        static bool EnsurePortablePowerShellTheme(string pvsDir, out string error)
        {
            error = null;
            string dataLibDir = Path.Combine(pvsDir, "data", "lib");
            string powerShellDir = Path.Combine(dataLibDir, "pwsh");
            string themesDir = Path.Combine(powerShellDir, "ohmyposh", "themes");
            string themePath = Path.Combine(themesDir, "tos-therm.omp.json");
            string originDir = Path.Combine(dataLibDir, "origin");

            try
            {
                if (!File.Exists(themePath))
                {
                    string[] themeSources =
                    {
                        Path.Combine(originDir, "tos-therm.omp.json"),
                        Path.Combine(themesDir, "tos-term.omp.json"),
                        Path.Combine(originDir, "tos-term.omp.json")
                    };
                    string themeSource = themeSources.FirstOrDefault(File.Exists);
                    if (string.IsNullOrEmpty(themeSource))
                    {
                        error = "tos-therm.omp.json and its legacy theme were not found.";
                        return false;
                    }

                    Directory.CreateDirectory(themesDir);
                    string tempThemePath = themePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.Copy(themeSource, tempThemePath, false);
                        if (File.Exists(themePath))
                            File.Delete(tempThemePath);
                        else
                            File.Move(tempThemePath, themePath);
                    }
                    finally
                    {
                        if (File.Exists(tempThemePath))
                            File.Delete(tempThemePath);
                    }
                }

                CommonHelper.RemoveUtf8BomFromFile(themePath);

                string[] profilePaths =
                {
                    Path.Combine(powerShellDir, "Microsoft.PowerShell_profile.ps1"),
                    Path.Combine(originDir, "Microsoft.PowerShell_profile.ps1")
                };
                foreach (string profilePath in profilePaths)
                {
                    if (!File.Exists(profilePath))
                        continue;

                    string profileContents = File.ReadAllText(profilePath);
                    string updatedProfile = profileContents.Replace("tos-term.omp.json", "tos-therm.omp.json");
                    if (string.Equals(profileContents, updatedProfile, StringComparison.Ordinal))
                        continue;

                    string tempProfilePath = profilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllText(tempProfilePath, updatedProfile, new UTF8Encoding(false));
                        if (File.Exists(profilePath))
                            File.Replace(tempProfilePath, profilePath, null);
                        else
                            File.Move(tempProfilePath, profilePath);
                    }
                    finally
                    {
                        if (File.Exists(tempProfilePath))
                            File.Delete(tempProfilePath);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static bool EnsurePortableSettings(string pvsDir, out string error)
        {
            error = null;
            try
            {
                string settingsPath = Path.Combine(pvsDir, "data", "user-data", "User", "settings.json");
                string originSettings = Path.Combine(pvsDir, "data", "lib", "origin", "settings.json");
                string originContents = File.Exists(originSettings) ? File.ReadAllText(originSettings) : null;
                if (!File.Exists(settingsPath))
                {
                    if (originContents == null)
                    {
                        error = "settings.json was not found.";
                        return false;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                    File.Copy(originSettings, settingsPath, true);
                }

                string contents = File.ReadAllText(settingsPath);
                bool changed = false;
                string pwshExe = Path.Combine(pvsDir, "data", "lib", "pwsh", "pwsh.exe");
                contents = ReplaceManagedJsonPath(contents, "path", pwshExe, @"data\\lib\\pwsh\\pwsh.exe", ref changed);
                if (originContents != null)
                    contents = RestorePortableTerminalPath(contents, originContents, ref changed);

                string profileArgsPattern = "(\"PowerShell 7 \\(portable\\)\"\\s*:\\s*\\{[^{}]*?\"args\"\\s*:\\s*)\\[\\s*\"-NoLogo\"\\s*\\]";
                string portableProfileArgs = "[\"-NoLogo\", \"-NoProfile\", \"-NoExit\", \"-Command\", \". \\\"$env:VSCODE_PORTABLE\\\\lib\\\\pwsh\\\\Microsoft.PowerShell_profile.ps1\\\"\"]";
                Match profileArgsMatch = Regex.Match(contents, profileArgsPattern);
                if (profileArgsMatch.Success)
                {
                    contents =
                        contents.Substring(0, profileArgsMatch.Index) +
                        profileArgsMatch.Groups[1].Value +
                        portableProfileArgs +
                        contents.Substring(profileArgsMatch.Index + profileArgsMatch.Length);
                    changed = true;
                }

                if (changed)
                {
                    string tempPath = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllText(tempPath, contents, new UTF8Encoding(false));
                        if (File.Exists(settingsPath))
                            File.Replace(tempPath, settingsPath, null);
                        else
                            File.Move(tempPath, settingsPath);
                    }
                    finally
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static string RestorePortableTerminalPath(string settings, string originSettings, ref bool changed)
        {
            int settingsValueStart;
            int settingsValueLength;
            string settingsPath;
            if (!TryGetJsonStringProperty(settings, "terminal.integrated.env.windows", "PATH",
                    out settingsValueStart, out settingsValueLength, out settingsPath) ||
                !string.Equals(settingsPath, "${env:PATH}", StringComparison.Ordinal))
            {
                return settings;
            }

            int originValueStart;
            int originValueLength;
            string portablePath;
            if (!TryGetJsonStringProperty(originSettings, "terminal.integrated.env.windows", "PATH",
                    out originValueStart, out originValueLength, out portablePath))
            {
                return settings;
            }

            changed = true;
            return settings.Substring(0, settingsValueStart) +
                portablePath +
                settings.Substring(settingsValueStart + settingsValueLength);
        }

        static bool TryGetJsonStringProperty(
            string json,
            string objectName,
            string propertyName,
            out int valueStart,
            out int valueLength,
            out string value)
        {
            valueStart = 0;
            valueLength = 0;
            value = null;
            if (string.IsNullOrEmpty(json))
                return false;

            string objectToken = "\"" + objectName + "\"";
            int objectNameIndex = json.IndexOf(objectToken, StringComparison.Ordinal);
            if (objectNameIndex < 0)
                return false;

            int objectStart = json.IndexOf('{', objectNameIndex + objectToken.Length);
            if (objectStart < 0)
                return false;

            int objectEnd = FindJsonObjectEnd(json, objectStart);
            if (objectEnd < 0)
                return false;

            string objectContents = json.Substring(objectStart + 1, objectEnd - objectStart - 1);
            string propertyPattern =
                "(\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\")(?<value>(?:\\\\.|[^\"\\\\])*)\"";
            Match propertyMatch = Regex.Match(objectContents, propertyPattern);
            if (!propertyMatch.Success)
                return false;

            valueStart = objectStart + 1 + propertyMatch.Groups["value"].Index;
            valueLength = propertyMatch.Groups["value"].Length;
            value = propertyMatch.Groups["value"].Value;
            return true;
        }

        static int FindJsonObjectEnd(string json, int objectStart)
        {
            int depth = 0;
            bool insideString = false;
            bool escaped = false;
            for (int i = objectStart; i < json.Length; i++)
            {
                char current = json[i];
                if (insideString)
                {
                    if (escaped)
                        escaped = false;
                    else if (current == '\\')
                        escaped = true;
                    else if (current == '"')
                        insideString = false;
                    continue;
                }

                if (current == '"')
                    insideString = true;
                else if (current == '{')
                    depth++;
                else if (current == '}' && --depth == 0)
                    return i;
            }

            return -1;
        }

        static string ReplaceManagedJsonPath(string json, string settingName, string currentPath, string managedSuffix, ref bool changed)
        {
            string pattern = "(\"" + Regex.Escape(settingName) + "\"\\s*:\\s*\")(?<value>(?:\\\\.|[^\"\\\\])*)\"";
            MatchCollection matches = Regex.Matches(json, pattern);
            if (matches.Count == 0)
                return json;

            string escapedPath = currentPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var result = new StringBuilder(json.Length);
            int nextIndex = 0;
            foreach (Match match in matches)
            {
                result.Append(json, nextIndex, match.Index - nextIndex);
                string previousValue = match.Groups["value"].Value;
                bool isManagedValue =
                    previousValue.IndexOf("${execPath}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    previousValue.IndexOf("${env:VSCODE_PORTABLE}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    previousValue.IndexOf(managedSuffix, StringComparison.OrdinalIgnoreCase) >= 0;

                if (isManagedValue && !string.Equals(previousValue, escapedPath, StringComparison.Ordinal))
                {
                    result.Append(match.Groups[1].Value).Append(escapedPath).Append('"');
                    changed = true;
                }
                else
                {
                    result.Append(match.Value);
                }

                nextIndex = match.Index + match.Length;
            }

            result.Append(json, nextIndex, json.Length - nextIndex);
            return result.ToString();
        }

        static async Task<List<string>> CheckVersionsAsync(string pvsDir)
        {
            var errors = new List<string>();
            string pvsInfo = Path.Combine(pvsDir, CommonHelper.RuntimeInfoFileName);
            if (!File.Exists(pvsInfo))
            {
                errors.Add(CommonHelper.RuntimeInfoFileName + " was not found; package update checks were skipped.");
                return errors;
            }

            Dictionary<string, string> versions;
            try
            {
                versions = ReadVersionsFromInfo(pvsInfo);
            }
            catch (Exception ex)
            {
                errors.Add("Could not read " + CommonHelper.RuntimeInfoFileName + ": " + ex.Message);
                return errors;
            }

            object errorsLock = new object();
            var checks = new List<Task>
            {
                CheckComponentVersionAsync(pvsDir, versions, "PWSH7_VERSION", "upgrade_pwsh7", "PowerShell 7",
                    () => GetLatestGitHubRelease("PowerShell/PowerShell"), errors, errorsLock),
                CheckComponentVersionAsync(pvsDir, versions, "OHMYPOSH_VERSION", "upgrade_ohmyposh", "Oh My Posh",
                    () => GetLatestGitHubRelease("JanDeDobbeleer/oh-my-posh"), errors, errorsLock),
                CheckComponentVersionAsync(pvsDir, versions, "PSFZF_VERSION", "upgrade_psfzf", "PSFzf",
                    () => GetLatestPSGalleryVersion("PSFzf"), errors, errorsLock),
                CheckComponentVersionAsync(pvsDir, versions, "MODERN_UNIX_WIN_VERSION", "upgrade_modern_unix", "modern-unix-win",
                    () => GetLatestPSGalleryVersion("modern-unix-win"), errors, errorsLock),
                CheckComponentVersionAsync(pvsDir, versions, "VSCODE_VERSION", "upgrade_vscode", "VS Code",
                    GetLatestVSCodeVersion, errors, errorsLock),
                CheckComponentVersionAsync(pvsDir, versions, "UV_VERSION", "upgrade_uv", "uv",
                    PortablePython.GetLatestUvVersionAsync, errors, errorsLock)
            };

            await Task.WhenAll(checks);
            return errors;
        }

        static async Task CheckComponentVersionAsync(
            string pvsDir,
            Dictionary<string, string> installedVersions,
            string versionKey,
            string upgradeFlag,
            string componentName,
            Func<Task<string>> getLatestVersion,
            List<string> errors,
            object errorsLock)
        {
            string currentVersion;
            if (!installedVersions.TryGetValue(versionKey, out currentVersion) || string.IsNullOrWhiteSpace(currentVersion))
                return;

            try
            {
                string latestVersion = await getLatestVersion();
                if (string.IsNullOrWhiteSpace(latestVersion))
                    throw new InvalidDataException("The update source returned an empty version.");

                string flagPath = Path.Combine(pvsDir, upgradeFlag);
                if (IsVersionNewer(latestVersion, currentVersion))
                {
                    WriteUpgradeFlag(flagPath, latestVersion);
                }
                else if (File.Exists(flagPath))
                {
                    File.Delete(flagPath);
                }
            }
            catch (Exception ex)
            {
                lock (errorsLock)
                    errors.Add(componentName + ": " + ex.Message);
            }
        }

        static bool IsVersionNewer(string candidate, string current)
        {
            string candidateVersion = (candidate ?? string.Empty).Trim().TrimStart('v', 'V');
            string currentVersion = (current ?? string.Empty).Trim().TrimStart('v', 'V');
            if (string.Equals(candidateVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
                return false;

            string[] candidateParts = candidateVersion.Split('-');
            string[] currentParts = currentVersion.Split('-');
            int numericComparison = CompareNumericVersionParts(candidateParts[0], currentParts[0]);
            if (numericComparison != 0)
                return numericComparison > 0;

            bool candidatePrerelease = candidateParts.Length > 1;
            bool currentPrerelease = currentParts.Length > 1;
            if (candidatePrerelease != currentPrerelease)
                return !candidatePrerelease;
            if (!candidatePrerelease)
                return false;

            return string.Compare(candidateParts[1], currentParts[1], StringComparison.OrdinalIgnoreCase) > 0;
        }

        static int CompareNumericVersionParts(string left, string right)
        {
            string[] leftParts = left.Split('.');
            string[] rightParts = right.Split('.');
            int count = Math.Max(leftParts.Length, rightParts.Length);
            for (int i = 0; i < count; i++)
            {
                int leftValue = 0;
                int rightValue = 0;
                bool leftIsNumeric = i >= leftParts.Length || int.TryParse(leftParts[i], out leftValue);
                bool rightIsNumeric = i >= rightParts.Length || int.TryParse(rightParts[i], out rightValue);
                if (!leftIsNumeric || !rightIsNumeric)
                    return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
                if (leftValue != rightValue)
                    return leftValue.CompareTo(rightValue);
            }

            return 0;
        }

        static void WriteUpgradeFlag(string flagPath, string latestVersion)
        {
            string tempPath = flagPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempPath, latestVersion, Encoding.UTF8);
                if (File.Exists(flagPath))
                    File.Replace(tempPath, flagPath, null);
                else
                    File.Move(tempPath, flagPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        static Dictionary<string, string> ReadVersionsFromInfo(string pvsInfo)
        {
            var versions = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(pvsInfo))
            {
                int idx = line.IndexOf('=');
                if (idx > 0)
                {
                    versions[line.Substring(0, idx)] = line.Substring(idx + 1);
                }
            }
            return versions;
        }

        static async Task<string> GetLatestGitHubRelease(string repo)
        {
            string url = string.Format("https://api.github.com/repos/{0}/releases/latest", repo);
            string json = await UpdateHttpClient.GetStringAsync(url);
            var match = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([^\"]+)\"");
            if (!match.Success)
                throw new InvalidDataException("The GitHub release response did not contain a tag.");
            return match.Groups[1].Value;
        }

        static async Task<string> GetLatestPSGalleryVersion(string moduleName)
        {
            string packageUrl = "https://www.powershellgallery.com/api/v2/package/" + Uri.EscapeDataString(moduleName);
            using (var request = new HttpRequestMessage(HttpMethod.Get, packageUrl))
            using (var response = await UpdateHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                Uri resolvedUri = response.RequestMessage.RequestUri;
                string packageFileName = Uri.UnescapeDataString(
                    resolvedUri.Segments[resolvedUri.Segments.Length - 1]);
                var match = Regex.Match(
                    packageFileName,
                    "^" + Regex.Escape(moduleName) + @"\.(?<version>\d+(?:\.\d+){1,3}(?:-[^/?]+)?)\.nupkg$",
                    RegexOptions.IgnoreCase);
                if (!match.Success)
                    throw new InvalidDataException("The PowerShell Gallery response did not contain a package version for " + moduleName + ".");
                return match.Groups["version"].Value;
            }
        }

        static int InitializePVS()
        {
            string pvsDir = AppDomain.CurrentDomain.BaseDirectory;

            try
            {
                Console.WriteLine("Initializing PVS environment...");

                // 1. Sync VSCode extensions
                Console.WriteLine("1/6: Synchronizing VSCode extensions...");
                SyncVSCodeExtensions(pvsDir);

                // 2. Restore origin files
                Console.WriteLine("2/6: Restoring configuration files...");
                RestoreOriginFiles(pvsDir);
                string themeError;
                if (!EnsurePortablePowerShellTheme(pvsDir, out themeError))
                    Console.WriteLine("  Could not prepare Oh My Posh theme: " + themeError);

                // 3. Clean Python environment
                Console.WriteLine("3/6: Preserving user Python runtimes and project environments...");

                // 4. Install fonts and create shortcuts
                Console.WriteLine("4/6: Installing fonts and creating shortcuts...");
                InstallFontsAndShortcuts(pvsDir);

                // 5. Clean user-data (except settings.json)
                Console.WriteLine("5/6: Cleaning user data...");
                CleanUserData(pvsDir);

                // 6. Launch VSCode
                Console.WriteLine("6/6: Launching VSCode...");
                LaunchVSCodeAndExit(pvsDir);

                Console.WriteLine("PVS initialization completed successfully!");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Initialization error: " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        static void SyncVSCodeExtensions(string pvsDir)
        {
            try
            {
                string extensionsDir = Path.Combine(pvsDir, "data", "extensions");
                if (!Directory.Exists(extensionsDir))
                {
                    Console.WriteLine("  Extensions directory not found, skipping...");
                    return;
                }

                // Get installed extensions
                var installedDirs = Directory.GetDirectories(extensionsDir)
                    .Select(d => Path.GetFileName(d))
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();

                var requiredExtensions = UpgradeForm.VSCODE_EXTENSIONS.ToList();
                var extensionsToPreserve = new[]
                {
                    "ms-vscode-remote.remote-ssh",
                    "Gerrnperl.outline-map"
                };

                foreach (var required in PlatformSensitiveVSCodeExtensions)
                {
                    var invalidInstalls = installedDirs
                        .Where(installed => installed.StartsWith(required + "-", StringComparison.OrdinalIgnoreCase))
                        .Where(installed => !ValidateInstalledVSCodeExtension(Path.Combine(extensionsDir, installed), required))
                        .ToList();

                    foreach (var invalidInstall in invalidInstalls)
                    {
                        string invalidPath = Path.Combine(extensionsDir, invalidInstall);
                        Console.WriteLine("  Removing invalid extension payload: " + invalidInstall);
                        Directory.Delete(invalidPath, true);
                        installedDirs.Remove(invalidInstall);
                    }
                }

                // Find extensions to remove (not in required list)
                var toRemove = installedDirs.Where(installed =>
                {
                    return !requiredExtensions.Any(required =>
                               installed.StartsWith(required + "-", StringComparison.OrdinalIgnoreCase)) &&
                           !extensionsToPreserve.Any(preserved =>
                               installed.StartsWith(preserved + "-", StringComparison.OrdinalIgnoreCase));
                }).ToList();

                // Remove unwanted extensions
                foreach (var ext in toRemove)
                {
                    string extPath = Path.Combine(extensionsDir, ext);
                    Console.WriteLine("  Removing: " + ext);
                    Directory.Delete(extPath, true);
                }

                // Find extensions to install (required but not installed)
                var toInstall = requiredExtensions.Where(required =>
                {
                    return !installedDirs.Any(installed =>
                        installed.StartsWith(required + "-", StringComparison.OrdinalIgnoreCase));
                }).ToList();

                // Install missing extensions
                if (toInstall.Any())
                {
                    string codeExe = Path.Combine(pvsDir, "Code.exe");
                    string cliJs = ResolveVSCodeCliJsPath(pvsDir);
                    string relativeExtensionsDir = Path.Combine("data", "extensions");
                    string relativeUserDataDir = Path.Combine("data", "user-data");

                    if (!File.Exists(codeExe) || string.IsNullOrEmpty(cliJs) || !File.Exists(cliJs))
                    {
                        Console.WriteLine("  VS Code CLI not found, skipping extension install...");
                    }
                    else
                    {
                        foreach (var ext in toInstall)
                        {
                            Console.WriteLine("  Installing: " + ext);
                            var psi = new ProcessStartInfo
                            {
                                FileName = codeExe,
                                Arguments = "\"" + cliJs + "\" --extensions-dir \"" + relativeExtensionsDir + "\" --user-data-dir \"" + relativeUserDataDir + "\" --install-extension " + ext + " --force",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                WorkingDirectory = pvsDir
                            };
                            psi.EnvironmentVariables["ELECTRON_RUN_AS_NODE"] = "1";
                            psi.EnvironmentVariables["VSCODE_DEV"] = "";

                            using (var process = Process.Start(psi))
                            {
                                process.WaitForExit();
                            }
                        }
                    }
                }

                RemoveExtensionsByPrefix(extensionsDir, OptionalAutoInstalledVSCodeExtensions, "  Removing optional extension: ");

                // Delete extensions.json
                string extensionsJson = Path.Combine(extensionsDir, "extensions.json");
                if (File.Exists(extensionsJson))
                {
                    Console.WriteLine("  Deleting extensions.json");
                    File.Delete(extensionsJson);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  Extension sync error: " + ex.Message);
            }
        }

        static string ResolveVSCodeCliJsPath(string pvsDir)
        {
            string cliJs = Path.Combine(pvsDir, "resources", "app", "out", "cli.js");
            if (File.Exists(cliJs))
                return cliJs;

            var candidates = Directory.GetFiles(pvsDir, "cli.js", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(Path.Combine("resources", "app", "out", "cli.js"), StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return candidates.FirstOrDefault() ?? string.Empty;
        }

        static bool ValidateInstalledVSCodeExtension(string extensionPath, string extensionId)
        {
            if (string.IsNullOrEmpty(extensionPath) || string.IsNullOrEmpty(extensionId) || !Directory.Exists(extensionPath))
                return false;

            if (!string.Equals(extensionId, "ms-python.python", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extensionId, "ms-python.vscode-python-envs", StringComparison.OrdinalIgnoreCase))
                return true;

            string toolsDir = Path.Combine(extensionPath, "python-env-tools", "bin");
            string windowsToolPath = Path.Combine(toolsDir, "pet.exe");
            if (File.Exists(windowsToolPath))
                return true;

            string extractedToolPath = Path.Combine(toolsDir, "pet");
            if (!File.Exists(extractedToolPath) || !HasPortableExecutableHeader(extractedToolPath))
                return false;

            try
            {
                File.Copy(extractedToolPath, windowsToolPath, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        static bool HasPortableExecutableHeader(string filePath)
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

        static void RemoveExtensionsByPrefix(string extensionsDir, IEnumerable<string> extensionIds, string messagePrefix)
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
                    Console.WriteLine(messagePrefix + Path.GetFileName(installedDir));
                    Directory.Delete(installedDir, true);
                }
            }
        }

        static void RestoreOriginFiles(string pvsDir)
        {
            try
            {
                string originDir = Path.Combine(pvsDir, "data", "lib", "origin");
                if (!Directory.Exists(originDir))
                {
                    Console.WriteLine("  Origin directory not found, skipping...");
                    return;
                }

                // Restore settings.json
                string originSettings = Path.Combine(originDir, "settings.json");
                if (File.Exists(originSettings))
                {
                    string targetSettings = Path.Combine(pvsDir, "data", "user-data", "User", "settings.json");
                    Directory.CreateDirectory(Path.GetDirectoryName(targetSettings));
                    File.Copy(originSettings, targetSettings, true);
                    Console.WriteLine("  Restored: settings.json");
                }

                // Restore Microsoft.PowerShell_profile.ps1
                string originProfile = Path.Combine(originDir, "Microsoft.PowerShell_profile.ps1");
                if (File.Exists(originProfile))
                {
                    string targetProfile = Path.Combine(pvsDir, "data", "lib", "pwsh", "Microsoft.PowerShell_profile.ps1");
                    File.Copy(originProfile, targetProfile, true);
                    Console.WriteLine("  Restored: Microsoft.PowerShell_profile.ps1");
                }

                // Restore the bundled Oh My Posh theme, falling back to the legacy filename.
                string originTheme = Path.Combine(originDir, "tos-therm.omp.json");
                if (!File.Exists(originTheme))
                    originTheme = Path.Combine(originDir, "tos-term.omp.json");
                if (File.Exists(originTheme))
                {
                    string targetTheme = Path.Combine(pvsDir, "data", "lib", "pwsh", "ohmyposh", "themes", "tos-therm.omp.json");
                    Directory.CreateDirectory(Path.GetDirectoryName(targetTheme));
                    File.Copy(originTheme, targetTheme, true);
                    Console.WriteLine("  Restored: tos-therm.omp.json");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  Origin file restore error: " + ex.Message);
            }
        }

        static void InstallFontsAndShortcuts(string pvsDir)
        {
            bool fontsReady = EnsureFonts(pvsDir);
            bool shortcutsReady = ManageShortcuts(pvsDir);

            if (fontsReady)
                Console.WriteLine("  Fonts are ready");
            else
                Console.WriteLine("  One or more portable fonts could not be registered");

            if (shortcutsReady)
                Console.WriteLine("  Desktop and Start Menu shortcuts are ready");
            else
                Console.WriteLine("  Desktop or Start Menu shortcut could not be created");
        }

        static void CleanUserData(string pvsDir)
        {
            try
            {
                string userDataDir = Path.Combine(pvsDir, "data", "user-data");
                if (!Directory.Exists(userDataDir))
                    return;

                // Get settings.json path to preserve
                string settingsPath = Path.Combine(userDataDir, "User", "settings.json");

                // Delete all items except User/settings.json
                var items = Directory.GetFileSystemEntries(userDataDir);
                foreach (var item in items)
                {
                    string name = Path.GetFileName(item);

                    if (name.Equals("User", StringComparison.OrdinalIgnoreCase))
                    {
                        // Keep User folder but clean its contents except settings.json
                        var userItems = Directory.GetFileSystemEntries(item);
                        foreach (var userItem in userItems)
                        {
                            if (!userItem.Equals(settingsPath, StringComparison.OrdinalIgnoreCase))
                            {
                                if (Directory.Exists(userItem))
                                    Directory.Delete(userItem, true);
                                else
                                    File.Delete(userItem);
                            }
                        }
                    }
                    else
                    {
                        // Delete everything else
                        if (Directory.Exists(item))
                            Directory.Delete(item, true);
                        else
                            File.Delete(item);
                    }
                }

                Console.WriteLine("  Cleaned: user-data (kept settings.json)");
            }
            catch (Exception ex)
            {
                Console.WriteLine("  User data cleanup error: " + ex.Message);
            }
        }

        static void LaunchVSCodeAndExit(string pvsDir)
        {
            try
            {
                string codeExe = Path.Combine(pvsDir, "Code.exe");
                if (File.Exists(codeExe))
                {
                    var psi = CreateVSCodeStartInfo(pvsDir);

                    using (var process = Process.Start(psi))
                    {
                        // Wait a bit for VSCode to initialize
                        System.Threading.Thread.Sleep(2000);
                    }

                    Console.WriteLine("  VSCode launched");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  VSCode launch error: " + ex.Message);
            }
        }

        static async Task<string> GetLatestVSCodeVersion()
        {
            var request = (HttpWebRequest)WebRequest.Create("https://update.code.visualstudio.com/latest/win32-x64-archive/stable");
            request.Method = "HEAD";
            request.AllowAutoRedirect = false;
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;

            using (var response = (HttpWebResponse)await request.GetResponseAsync())
            {
                string redirectUrl = response.Headers["Location"];
                var match = Regex.Match(redirectUrl ?? string.Empty, @"VSCode-win32-x64-([\d\.]+)\.zip");
                if (!match.Success)
                    throw new InvalidDataException("VS Code version was not found in the update redirect.");
                return match.Groups[1].Value;
            }
        }
    }
}
