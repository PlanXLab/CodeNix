param([switch]$Clean, [switch]$Rebuild)
$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dist = Join-Path $root 'dist'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler is required.' }
if (($Clean -or $Rebuild) -and (Test-Path -LiteralPath $dist)) { Remove-Item -LiteralPath $dist -Recurse -Force }
if ($Clean -and -not $Rebuild) { return }
[void][System.IO.Directory]::CreateDirectory($dist)
$references = @(
    'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
    'System.Net.Http.dll', 'System.Web.Extensions.dll', 'System.IO.Compression.dll',
    'System.IO.Compression.FileSystem.dll', 'Microsoft.CSharp.dll'
) | ForEach-Object { '/reference:' + $_ }
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/langversion:5', "/win32icon:$(Join-Path $root 'res\codenix.ico')") + $references
$common = Join-Path $root 'VSCodePortableCommon.cs'
$launcher = Join-Path $dist 'launcher.exe'
$installer = Join-Path $dist 'CodeNix.exe'
$launcherSources = @('LauncherProgram.cs', 'launcher.cs', 'UpgradeForm.Designer.cs') | ForEach-Object { Join-Path $root $_ }
$installerSources = @('Program.cs', 'installer.cs', 'InstallForm.Designer.cs') | ForEach-Object { Join-Path $root $_ }
foreach ($component in @('Launcher', 'Installer')) {
    $assemblyInfo = Join-Path $root "Properties\AssemblyInfo.$component.cs"
    if (Test-Path -LiteralPath $assemblyInfo) {
        if ($component -eq 'Launcher') { $launcherSources += $assemblyInfo } else { $installerSources += $assemblyInfo }
    }
}
& $compiler @arguments "/out:$launcher" $common @launcherSources
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
$resources = @("/resource:$launcher,launcher.exe")
foreach ($fileName in @('Microsoft.PowerShell_profile.ps1', 'settings.json', '0xProtoNerdFont-Regular.ttf', 'DalseoHealingMedium.ttf', 'tos-therm.omp.json')) {
    $file = Join-Path $root "res\$fileName"
    if (-not (Test-Path -LiteralPath $file)) { throw "Required build resource is missing: $file" }
    $resources += "/resource:$file,$fileName"
}
& $compiler @arguments "/out:$installer" $common @installerSources @resources
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
Write-Output "Built $installer with embedded launcher.exe."