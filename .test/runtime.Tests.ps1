param([switch]$Network, [switch]$KeepTemp)
$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$pipScript = Join-Path $repository 'res\pip.ps1'
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('CodeNix-renewal-' + [guid]::NewGuid().ToString('N'))
[void][System.IO.Directory]::CreateDirectory($temporary)
$harness = @'
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using VSCodePortableCommon;

public static class RenewalTests
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static int Main(string[] arguments)
    {
        try
        {
            string mode = arguments[0];
            string root = Path.GetFullPath(arguments[1]);
            if (mode == "check")
            {
                var start = new ProcessStartInfo();
                start.EnvironmentVariables["UV_PYTHON"] = @"C:\foreign\python.exe";
                start.EnvironmentVariables["VIRTUAL_ENV"] = @"C:\foreign\.venv";
                start.EnvironmentVariables["ELECTRON_RUN_AS_NODE"] = "1";
                PortablePython.ApplyEnvironment(start, root);
                Require(start.EnvironmentVariables["UV_CACHE_DIR"] == Path.Combine(root, "data", "cache", "uv"), "Cache path");
                Require(start.EnvironmentVariables["UV_PYTHON_INSTALL_REGISTRY"] == "0", "Registry policy");
                Require(!start.EnvironmentVariables.ContainsKey("UV_PYTHON") && !start.EnvironmentVariables.ContainsKey("VIRTUAL_ENV"), "Interpreter isolation");
                Require(!start.EnvironmentVariables.ContainsKey("ELECTRON_RUN_AS_NODE"), "GUI process isolation");
                var launcherType = typeof(VSCodePortableLauncher.Program);
                var createStartInfo = launcherType.GetMethod("CreateVSCodeStartInfo", BindingFlags.NonPublic | BindingFlags.Static);
                var actualStart = (ProcessStartInfo)createStartInfo.Invoke(null, new object[] { root });
                Require(actualStart.EnvironmentVariables["UV_PYTHON_INSTALL_DIR"] == PortablePython.RuntimeDirectory(root), "Actual launcher runtime store");
                Require(!actualStart.EnvironmentVariables["PATH"].Contains(Path.Combine(root, "data", "lib", "python") + ";"), "No global Python PATH");
                string[] launchPaths = actualStart.EnvironmentVariables["PATH"].Split(';');
                string wrapperBin = Path.Combine(root, "data", "lib", "pwsh", "bin");
                string globalScripts = Path.Combine(root, "data", "venvs", "global", "Scripts");
                int wrapperPathIndex = Array.FindIndex(launchPaths, delegate(string entry) {
                    return string.Equals(entry.TrimEnd('\\'), wrapperBin.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                });
                int globalScriptsIndex = Array.FindIndex(launchPaths, delegate(string entry) {
                    return string.Equals(entry.TrimEnd('\\'), globalScripts.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                });
                Require(wrapperPathIndex >= 0 && globalScriptsIndex > wrapperPathIndex, "pip wrapper precedes global Scripts on PATH");
                Require(PortablePython.GlobalEnvironmentDirectory(root) == Path.Combine(root, "data", "venvs", "global"), "Global environment path");
                string debugger = Path.Combine(root, "debugger-extension");
                Directory.CreateDirectory(debugger);
                var validateExtension = launcherType.GetMethod("ValidateInstalledVSCodeExtension", BindingFlags.NonPublic | BindingFlags.Static);
                Require((bool)validateExtension.Invoke(null, new object[] { debugger, "ms-python.debugpy" }), "Debugger must not require PET");
                Require(!(bool)validateExtension.Invoke(null, new object[] { debugger, "ms-python.vscode-python-envs" }), "Environment extension must require PET");
                string downloads = "[{\"implementation\":\"cpython\",\"os\":\"windows\",\"arch\":\"x86_64\",\"variant\":\"default\",\"version\":\"3.99.0a1\"},{\"implementation\":\"cpython\",\"os\":\"windows\",\"arch\":\"x86_64\",\"variant\":\"default\",\"version\":\"3.14.3\"},{\"implementation\":\"cpython\",\"os\":\"windows\",\"arch\":\"x86_64\",\"variant\":\"default\",\"version\":\"3.14.9\"},{\"implementation\":\"cpython\",\"os\":\"windows\",\"arch\":\"x86_64\",\"variant\":\"default\",\"version\":\"3.15.1\"},{\"implementation\":\"cpython\",\"os\":\"windows\",\"arch\":\"x86_64\",\"variant\":\"default\",\"version\":\"3.15.2\"},{\"implementation\":\"cpython\",\"os\":\"linux\",\"arch\":\"x86_64\",\"variant\":\"default\",\"version\":\"3.98.0\"}]";
                Require(PortablePython.SelectLatestStablePython(downloads) == "3.15.2", "Stable Windows selection");
                Require(PortablePython.IsValidPythonVersionRequest("") &&
                    PortablePython.IsValidPythonVersionRequest("3.15") &&
                    PortablePython.IsValidPythonVersionRequest("3.15.2") &&
                    !PortablePython.IsValidPythonVersionRequest("3") &&
                    !PortablePython.IsValidPythonVersionRequest("3.15.2.1"), "Python version input validation");
                Require(PortablePython.SelectPythonVersion(downloads, "") == "3.15.2", "Blank defaults to latest Python");
                Require(PortablePython.SelectPythonVersion(downloads, "3.14") == "3.14.9", "Latest 3.14 patch selection");
                Require(PortablePython.SelectPythonVersion(downloads, "3.15") == "3.15.2", "Latest patch selection");
                Require(PortablePython.SelectPythonVersion(downloads, "3.15.1") == "3.15.1", "Exact patch selection");
                bool unavailablePythonVersionRejected = false;
                try { PortablePython.SelectPythonVersion(downloads, "3.15.3"); }
                catch (InvalidOperationException) { unavailablePythonVersionRejected = true; }
                Require(unavailablePythonVersionRejected, "Unavailable exact Python versions must be rejected");
                var selector = typeof(VSCodePortableInstaller.InstallForm).GetMethod("FindLatestStableVersion", BindingFlags.NonPublic | BindingFlags.Static);
                string marketplace = "{\"results\":[{\"extensions\":[{\"versions\":[{\"version\":\"9.0.0\",\"targetPlatform\":\"linux-x64\",\"properties\":[]},{\"version\":\"8.0.0\",\"targetPlatform\":\"win32-x64\",\"properties\":[{\"key\":\"Microsoft.VisualStudio.Code.PreRelease\",\"value\":\"true\"}]},{\"version\":\"7.0.0\",\"targetPlatform\":\"win32-x64\",\"properties\":[]}]}]}]}";
                Require((string)selector.Invoke(null, new object[] { marketplace, true }) == "7.0.0", "Stable Windows VSIX selection");
                Console.WriteLine("PASS: portable paths, registry policy, interpreter isolation, stable Python and VSIX selection");
            }
            else if (mode == "install")
                Console.WriteLine(PortablePython.InstallUvAsync(root, arguments.Length > 2 ? arguments[2] : null, CancellationToken.None).GetAwaiter().GetResult());
            else if (mode == "python")
                Console.WriteLine(PortablePython.InstallLatestPythonAsync(root, CancellationToken.None, null).GetAwaiter().GetResult());
            else if (mode == "global-venv")
                Console.WriteLine(PortablePython.EnsureGlobalEnvironmentAsync(root, arguments[2], CancellationToken.None).GetAwaiter().GetResult());
            else if (mode == "uv")
                Console.WriteLine(PortablePython.RunAsync(root, arguments[2], CancellationToken.None).GetAwaiter().GetResult());
            else if (mode == "rebase")
                Console.WriteLine(PortablePython.RebaseEnvironmentAsync(root, arguments[2], CancellationToken.None).GetAwaiter().GetResult());
            else if (mode == "rebase-all")
                PortablePython.RebaseProjectsAsync(root, message => Console.WriteLine(message)).GetAwaiter().GetResult();
            else if (mode == "probe")
            {
                var start = new ProcessStartInfo(arguments[2], "-c \"import sys,idna; print(sys.prefix != sys.base_prefix); print(idna.encode('example.com').decode())\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                PortablePython.ApplyEnvironment(start, root);
                using (var process = Process.Start(start))
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    Require(process.WaitForExit(30000), "Python probe timeout");
                    Require(process.ExitCode == 0, error.GetAwaiter().GetResult());
                    string result = output.GetAwaiter().GetResult();
                    Require(result.Contains("True") && result.Contains("example.com"), "Project environment and import");
                    Console.WriteLine("PASS: virtual environment interpreter and package import");
                }
            }
            else if (mode == "launch")
            {
                var method = typeof(VSCodePortableLauncher.Program).GetMethod("CreateVSCodeStartInfo", BindingFlags.NonPublic | BindingFlags.Static);
                var start = (ProcessStartInfo)method.Invoke(null, new object[] { root });
                start.FileName = arguments[2];
                start.Arguments = arguments[3];
                using (var process = Process.Start(start))
                {
                    if (!process.WaitForExit(120000))
                    {
                        process.Kill();
                        throw new TimeoutException("Launch integration timeout");
                    }
                    Require(process.ExitCode == 0, "Launch integration failed");
                }
                Console.WriteLine("PASS: actual launcher process environment");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }
    }
}
'@
$runnerSource = Join-Path $temporary 'RenewalTests.cs'
$runner = Join-Path $temporary 'RenewalTests.exe'
[System.IO.File]::WriteAllText($runnerSource, $harness)
$references = @('System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Net.Http.dll', 'System.Web.Extensions.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'Microsoft.CSharp.dll') | ForEach-Object { '/reference:' + $_ }
$sources = @('VSCodePortableCommon.cs', 'installer.cs', 'InstallForm.Designer.cs', 'LauncherProgram.cs', 'launcher.cs', 'UpgradeForm.Designer.cs') | ForEach-Object { Join-Path $repository $_ }
function Invoke-Check([string[]]$Arguments) {
    $output = & $runner @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Runtime check failed: $($Arguments -join ' ')" }
    return $output
}
try {
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 /langversion:5 /main:RenewalTests "/out:$runner" @references @sources $runnerSource
    if ($LASTEXITCODE -ne 0) { throw 'Runtime test harness build failed.' }
    $original = Join-Path $temporary 'portable original'
    [void][System.IO.Directory]::CreateDirectory($original)
    Invoke-Check @('check', $original)
    $settings = Get-Content -LiteralPath (Join-Path $repository 'res\settings.json') -Raw | ConvertFrom-Json
    if ($settings.'python.defaultInterpreterPath' -cne '${workspaceFolder}/.venv') { throw 'Project interpreter default was not configured.' }
    if ($settings.'python-envs.terminal.autoActivationType' -cne 'off') { throw 'Python Environments terminal auto-activation must be disabled.' }
    if ($settings.'python.terminal.activateEnvironment' -isnot [bool] -or $settings.'python.terminal.activateEnvironment' -ne $false) { throw 'Legacy Python terminal auto-activation must be disabled.' }
    $tokens = $null
    $parseErrors = $null
    $profileAst = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $repository 'res\Microsoft.PowerShell_profile.ps1'), [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
    Write-Output 'PASS: settings and PowerShell profile syntax'
    $pythonFunctions = $profileAst.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            (($node.Name -replace '^global:', '') -in @('Get-CodeNixPythonEnvironment', 'Get-PyRunner', 'python'))
    }, $true)
    foreach ($function in $pythonFunctions) {
        $functionText = $function.Extent.Text
        if (($function.Name -replace '^global:', '') -eq 'python') {
            $functionText = $functionText -replace '^function\s+(?:global:)?python\b', 'function Invoke-CodeNixTestPython'
        }
        Invoke-Expression $functionText
    }
    if ($pythonFunctions.Count -ne 3) { throw 'The profile Python routing functions could not be loaded.' }
    $savedPortableData = Get-Variable PortableData -ValueOnly -ErrorAction SilentlyContinue
    $hadPortableData = Test-Path variable:PortableData
    $savedVirtualEnv = [Environment]::GetEnvironmentVariable('VIRTUAL_ENV', 'Process')
    $profileTestRoot = Join-Path $temporary 'python-profile'
    $PortableData = Join-Path $profileTestRoot 'data'
    $globalEnvironment = Join-Path $PortableData 'venvs\global'
    $globalScripts = Join-Path $globalEnvironment 'Scripts'
    $globalPython = Join-Path $globalScripts 'python.exe'
    $activeEnvironment = Join-Path $profileTestRoot 'active'
    $activeScripts = Join-Path $activeEnvironment 'Scripts'
    $commandProcessor = Join-Path $env:SystemRoot 'System32\cmd.exe'
    try {
        foreach ($environment in @($globalEnvironment, $activeEnvironment, (Join-Path $profileTestRoot '.venv'))) {
            [void][System.IO.Directory]::CreateDirectory((Join-Path $environment 'Scripts'))
            [System.IO.File]::WriteAllText((Join-Path $environment 'pyvenv.cfg'), 'version_info = 3.12.0')
        }
        [System.IO.File]::Copy($commandProcessor, $globalPython)
        [System.IO.File]::Copy($commandProcessor, (Join-Path $activeScripts 'python.exe'))
        [Environment]::SetEnvironmentVariable('VIRTUAL_ENV', $null, 'Process')
        if ((Get-CodeNixPythonEnvironment) -cne $globalEnvironment) { throw 'Global Python was not selected by default.' }
        $globalOutput = @(Invoke-CodeNixTestPython /c echo global-default) -join ''
        if ($globalOutput.Trim() -cne 'global-default') { throw 'The python command did not invoke the global interpreter.' }
        $probeScript = Join-Path $profileTestRoot 'probe.py'
        [System.IO.File]::WriteAllText($probeScript, 'print(1)')
        if ((Get-PyRunner $probeScript) -cne $globalPython) { throw 'Ordinary scripts did not use global Python.' }
        $globalReplx = Join-Path $globalScripts 'replx.exe'
        [System.IO.File]::WriteAllText($globalReplx, '')
        [System.IO.File]::WriteAllText($probeScript, '#!/usr/bin/env -S replx --flag')
        if ((Get-PyRunner $probeScript) -cne $globalReplx) { throw 'The replx shebang did not select global replx.' }
        [Environment]::SetEnvironmentVariable('VIRTUAL_ENV', $activeEnvironment, 'Process')
        [System.IO.File]::Delete($globalPython)
        $activeOutput = @(Invoke-CodeNixTestPython /c echo active-environment) -join ''
        if ($activeOutput.Trim() -cne 'active-environment') { throw 'An active environment did not override global Python.' }
        [Environment]::SetEnvironmentVariable('VIRTUAL_ENV', (Join-Path $profileTestRoot 'missing'), 'Process')
        $invalidEnvironmentError = $null
        try { Get-CodeNixPythonEnvironment | Out-Null } catch { $invalidEnvironmentError = $_.Exception.Message }
        if ($invalidEnvironmentError -notlike 'Not a Python virtual environment:*') { throw 'An invalid active environment was not reported.' }
        Write-Output 'PASS: PowerShell profile global/active Python and replx routing'
    } finally {
        [Environment]::SetEnvironmentVariable('VIRTUAL_ENV', $savedVirtualEnv, 'Process')
        if ($hadPortableData) { $PortableData = $savedPortableData }
        else { Remove-Variable PortableData -ErrorAction SilentlyContinue }
    }
    $tokens = $null
    $parseErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($pipScript, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
    Write-Output 'PASS: pip.ps1 syntax'
    if ($Network) {
        $userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
        $machinePath = [Environment]::GetEnvironmentVariable('PATH', 'Machine')
        $uvVersion = (Invoke-Check @('install', $original) | Select-Object -Last 1).Trim()
        $pythonVersion = (Invoke-Check @('python', $original) | Select-Object -Last 1).Trim()
        Write-Output "PASS: installed uv $uvVersion and stable CPython $pythonVersion"
        foreach ($projectName in @('alpha', 'beta')) {
            $environment = Join-Path $original "projects\$projectName\.venv"
            Invoke-Check @('uv', $original, "venv --python $pythonVersion --relocatable --no-config --no-project `"$environment`"") | Out-Null
            $python = Join-Path $environment 'Scripts\python.exe'
            Invoke-Check @('uv', $original, "pip install --no-config --python `"$python`" idna markupsafe") | Out-Null
            Invoke-Check @('probe', $original, $python)
        }
        $alpha = Join-Path $original 'projects\alpha\.venv'
        $beta = Join-Path $original 'projects\beta\.venv'
        $firstId = & "$env:WINDIR\System32\fsutil.exe" file queryfileid (Join-Path $alpha 'Lib\site-packages\idna\core.py')
        $secondId = & "$env:WINDIR\System32\fsutil.exe" file queryfileid (Join-Path $beta 'Lib\site-packages\idna\core.py')
        if ($firstId -cne $secondId) { throw 'Expected same-volume hardlink package reuse.' }
        Write-Output 'PASS: independent project environments share cached package file data'
        Invoke-Check @('uv', $original, "pip uninstall --no-config --python `"$alpha\Scripts\python.exe`" idna") | Out-Null
        Invoke-Check @('probe', $original, "$beta\Scripts\python.exe")
        Invoke-Check @('uv', $original, "pip install --offline --no-config --python `"$alpha\Scripts\python.exe`" idna") | Out-Null
        $globalEnvironment = Join-Path $original 'data\venvs\global'
        Invoke-Check @('global-venv', $original, $pythonVersion) | Out-Null
        $globalPython = Join-Path $globalEnvironment 'Scripts\python.exe'
        $globalScripts = Join-Path $globalEnvironment 'Scripts'
        $pipInstallDirectory = Join-Path $original 'data\lib\pwsh\bin'
        [void][System.IO.Directory]::CreateDirectory($pipInstallDirectory)
        $pipInstallPath = Join-Path $pipInstallDirectory 'pip.ps1'
        Copy-Item -LiteralPath $pipScript -Destination $pipInstallPath -Force
        $environmentNames = @(
            'VSCODE_PORTABLE', 'UV_PYTHON_INSTALL_DIR', 'UV_CACHE_DIR', 'UV_PYTHON_CACHE_DIR',
            'UV_PYTHON_PREFERENCE', 'UV_LINK_MODE', 'UV_NO_PROGRESS', 'UV_PYTHON',
            'UV_PROJECT_ENVIRONMENT', 'PYTHONHOME', 'PYTHONPATH', 'VIRTUAL_ENV',
            'CONDA_PREFIX', 'CONDA_DEFAULT_ENV'
        )
        $previousEnvironment = @{}
        foreach ($name in $environmentNames) {
            $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        }
        $previousPath = $env:PATH
        $previousLocation = Get-Location
        try {
            $env:VSCODE_PORTABLE = $null
            $env:UV_PYTHON_INSTALL_DIR = Join-Path $original 'data\lib\python'
            $env:UV_CACHE_DIR = Join-Path $original 'data\cache\uv'
            $env:UV_PYTHON_CACHE_DIR = Join-Path $original 'data\cache\python'
            $env:UV_PYTHON_PREFERENCE = 'only-managed'
            $env:UV_LINK_MODE = 'hardlink'
            $env:UV_NO_PROGRESS = '1'
            foreach ($name in @('UV_PYTHON', 'UV_PROJECT_ENVIRONMENT', 'PYTHONHOME', 'PYTHONPATH', 'VIRTUAL_ENV', 'CONDA_PREFIX', 'CONDA_DEFAULT_ENV')) {
                [Environment]::SetEnvironmentVariable($name, $null, 'Process')
            }
            $env:PATH = $pipInstallDirectory + ';' + $globalScripts + ';' + $previousPath
            Set-Location -LiteralPath $temporary

            $resolvedPip = (Get-Command pip -ErrorAction Stop | Select-Object -First 1).Source
            if ($resolvedPip -cne $pipInstallPath) { throw 'The pip command did not resolve to the portable wrapper.' }
            $resolvedPython = (Get-Command python -ErrorAction Stop | Select-Object -First 1).Source
            if ($resolvedPython -cne $globalPython) { throw 'The python command did not resolve to the global environment.' }

            & pip install idna pygments
            if ($LASTEXITCODE -ne 0) { throw 'Global pip install failed.' }
            & pip install --offline -U idna
            if ($LASTEXITCODE -ne 0) { throw 'Global pip upgrade failed.' }
            $globalPackages = (& pip list | Out-String)
            if ($LASTEXITCODE -ne 0 -or $globalPackages -notmatch '(?im)^\s*idna\s+' -or
                $globalPackages -notmatch '(?im)^\s*pygments\s+') { throw 'Global pip list did not show installed packages.' }
            & python -c "import idna, pygments; print(idna.encode('example.com').decode())" | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Global package imports failed from an unrelated working directory.' }
            $pygmentize = (Get-Command pygmentize -ErrorAction Stop | Select-Object -First 1).Source
            if ($pygmentize -cne (Join-Path $globalScripts 'pygmentize.exe')) { throw 'The global console script was not resolved from Scripts.' }
            & pip uninstall --offline -y idna pygments
            if ($LASTEXITCODE -ne 0) { throw 'Global pip uninstall failed.' }
            if (Test-Path -LiteralPath (Join-Path $globalScripts 'pygmentize.exe')) { throw 'Global pip uninstall left the console script behind.' }
            $packagesRemoved = & python -c "import importlib.util; print(importlib.util.find_spec('idna') is None and importlib.util.find_spec('pygments') is None)"
            if ($LASTEXITCODE -ne 0 -or ($packagesRemoved | Select-Object -Last 1).Trim() -cne 'True') {
                throw 'Global pip uninstall left packages importable.'
            }
            & pip install --offline idna
            if ($LASTEXITCODE -ne 0) { throw 'Could not restore idna for relocation testing.' }
            Write-Output 'PASS: pip wrapper installs, upgrades, lists, imports, and uninstalls in the global environment'
        } finally {
            Set-Location -LiteralPath $previousLocation
            $env:PATH = $previousPath
            foreach ($name in $environmentNames) {
                [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
            }
        }
        $runtime = Join-Path $original "data\lib\python\cpython-$pythonVersion-windows-x86_64-none\python.exe"
        $before = (Get-FileHash -LiteralPath $runtime -Algorithm SHA256).Hash
        Invoke-Check @('install', $original, $uvVersion) | Out-Null
        if ((Get-FileHash -LiteralPath $runtime -Algorithm SHA256).Hash -cne $before) { throw 'uv update modified the Python runtime.' }
        Write-Output 'PASS: uv-only replacement leaves the Python runtime unchanged'
        $moved = Join-Path $temporary 'portable moved'
        & "$env:WINDIR\System32\robocopy.exe" $original $moved /E /XJ /R:0 /W:0 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw 'Portable fixture copy failed.' }
        Invoke-Check @('uv', $original, "python uninstall cpython-$pythonVersion-windows-x86_64-none --no-config") | Out-Null
        $remaining = Invoke-Check @('uv', $original, 'python list --only-installed --no-config --output-format json') | ConvertFrom-Json
        if (@($remaining).Count) { throw 'The final runtime was not removed.' }
        $rebaseOutput = Invoke-Check @('rebase-all', $moved) | Out-String
        if ($rebaseOutput -notmatch 'Rebased global Python environment' -or
            $rebaseOutput -notmatch 'Rebased project environment') { throw 'Moved global and project environments were not rebased.' }
        $movedEnvironment = Join-Path $moved 'projects\alpha\.venv'
        Invoke-Check @('probe', $moved, "$movedEnvironment\Scripts\python.exe")
        Invoke-Check @('probe', $moved, (Join-Path $moved 'data\venvs\global\Scripts\python.exe'))
        Write-Output 'PASS: final-runtime deletion and no-download relocation rebasing preserve global and project packages'
        if ([Environment]::GetEnvironmentVariable('PATH', 'User') -cne $userPath -or
            [Environment]::GetEnvironmentVariable('PATH', 'Machine') -cne $machinePath) { throw 'Persistent PATH was modified.' }
    }
} finally {
    if ($KeepTemp) { Write-Output "Retained test artifacts: $temporary" }
    else { Remove-Item -LiteralPath $temporary -Recurse -Force }
}