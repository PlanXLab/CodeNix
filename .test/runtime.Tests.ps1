param([switch]$Network, [switch]$KeepTemp)
$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
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
                string debugger = Path.Combine(root, "debugger-extension");
                Directory.CreateDirectory(debugger);
                var validateExtension = launcherType.GetMethod("ValidateInstalledVSCodeExtension", BindingFlags.NonPublic | BindingFlags.Static);
                Require((bool)validateExtension.Invoke(null, new object[] { debugger, "ms-python.debugpy" }), "Debugger must not require PET");
                Require(!(bool)validateExtension.Invoke(null, new object[] { debugger, "ms-python.vscode-python-envs" }), "Environment extension must require PET");
                string downloads = "[{\"implementation\":\"cpython\",\"os\":\"windows\",\"arch\":\"x86_64\",\"variant\":\"default\",\"version\":\"3.99.0a1\"},{\"implementation\":\"cpython\",\"os\":\"windows\",\"arch\":\"x86_64\",\"variant\":\"default\",\"version\":\"3.15.2\"},{\"implementation\":\"cpython\",\"os\":\"linux\",\"arch\":\"x86_64\",\"variant\":\"default\",\"version\":\"3.98.0\"}]";
                Require(PortablePython.SelectLatestStablePython(downloads) == "3.15.2", "Stable Windows selection");
                var selector = typeof(VSCodePortableInstaller.InstallForm).GetMethod("FindLatestStableVersion", BindingFlags.NonPublic | BindingFlags.Static);
                string marketplace = "{\"results\":[{\"extensions\":[{\"versions\":[{\"version\":\"9.0.0\",\"targetPlatform\":\"linux-x64\",\"properties\":[]},{\"version\":\"8.0.0\",\"targetPlatform\":\"win32-x64\",\"properties\":[{\"key\":\"Microsoft.VisualStudio.Code.PreRelease\",\"value\":\"true\"}]},{\"version\":\"7.0.0\",\"targetPlatform\":\"win32-x64\",\"properties\":[]}]}]}]}";
                Require((string)selector.Invoke(null, new object[] { marketplace, true }) == "7.0.0", "Stable Windows VSIX selection");
                Console.WriteLine("PASS: portable paths, registry policy, interpreter isolation, stable Python and VSIX selection");
            }
            else if (mode == "install")
                Console.WriteLine(PortablePython.InstallUvAsync(root, arguments.Length > 2 ? arguments[2] : null, CancellationToken.None).GetAwaiter().GetResult());
            else if (mode == "python")
                Console.WriteLine(PortablePython.InstallLatestPythonAsync(root, CancellationToken.None, null).GetAwaiter().GetResult());
            else if (mode == "uv")
                Console.WriteLine(PortablePython.RunAsync(root, arguments[2], CancellationToken.None).GetAwaiter().GetResult());
            else if (mode == "rebase")
                Console.WriteLine(PortablePython.RebaseEnvironmentAsync(root, arguments[2], CancellationToken.None).GetAwaiter().GetResult());
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
    $tokens = $null
    $parseErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile((Join-Path $repository 'res\Microsoft.PowerShell_profile.ps1'), [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
    Write-Output 'PASS: settings and PowerShell profile syntax'
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
        $movedEnvironment = Join-Path $moved 'projects\alpha\.venv'
        if ((Invoke-Check @('rebase', $moved, $movedEnvironment)).Trim() -ne 'True') { throw 'Moved project environment was not rebased.' }
        Invoke-Check @('probe', $moved, "$movedEnvironment\Scripts\python.exe")
        Write-Output 'PASS: final-runtime deletion and no-download relocation rebasing preserve project packages'
        if ([Environment]::GetEnvironmentVariable('PATH', 'User') -cne $userPath -or
            [Environment]::GetEnvironmentVariable('PATH', 'Machine') -cne $machinePath) { throw 'Persistent PATH was modified.' }
    }
} finally {
    if ($KeepTemp) { Write-Output "Retained test artifacts: $temporary" }
    else { Remove-Item -LiteralPath $temporary -Recurse -Force }
}