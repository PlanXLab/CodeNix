# PowerShell profile for the CodeNix portable environment

try {
    [Console]::InputEncoding  = [System.Text.Encoding]::UTF8
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
} catch {
    Write-Warning "Console encoding could not be configured: $($_.Exception.Message)"
}

$Base         = $PSScriptRoot
$Bin          = "$Base\bin"
$Modules      = "$Base\Modules"
$InstallRoot  = [System.IO.Path]::GetFullPath("$Base\..\..\..")
$PortableData = "$InstallRoot\data"
$CacheDir     = "$PortableData\cache\pwsh-init"

$UnixModDir = "$Modules\modern-unix-win"
$UnixBin    = $null
$modernUnixVersion = $null
if ([System.IO.Directory]::Exists($UnixModDir)) {
    $versions = foreach ($directory in [System.IO.Directory]::GetDirectories($UnixModDir)) {
        $version = $null
        if ([version]::TryParse([System.IO.Path]::GetFileName($directory), [ref]$version) -and
            [System.IO.Directory]::Exists("$directory\bin")) {
            $version
        }
    }
    $modernUnixVersion = $versions | Sort-Object -Descending | Select-Object -First 1
    if ($modernUnixVersion) {
        $UnixBin = "$UnixModDir\$modernUnixVersion\bin"
    }
}

function Add-PortablePath ([string]$Name, [string[]]$Directories) {
    $entries = [Environment]::GetEnvironmentVariable($Name, 'Process') -split ';'
    $paths = [System.Collections.Generic.List[string]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in @($Directories) + $entries) {
        if (-not [string]::IsNullOrWhiteSpace($entry) -and $seen.Add($entry.Trim().Trim('"').TrimEnd('\', '/'))) {
            $paths.Add($entry)
        }
    }
    [Environment]::SetEnvironmentVariable($Name, ($paths -join ';'), 'Process')
}

Add-PortablePath 'Path' @($Bin, $UnixBin)
Add-PortablePath 'PSModulePath' @($Modules)

$pythonStorage = @{
    UV_PYTHON_INSTALL_DIR = "$PortableData\lib\python"
    UV_CACHE_DIR = "$PortableData\cache\uv"
    UV_PYTHON_CACHE_DIR = "$PortableData\cache\python"
    UV_TOOL_DIR = "$PortableData\lib\uv-tools"
    UV_TOOL_BIN_DIR = "$PortableData\bin"
    UV_CREDENTIALS_DIR = "$PortableData\uv\credentials"
    UV_PYTHON_BIN_DIR = "$PortableData\bin"
    UV_PYTHON_INSTALL_BIN = '0'
    UV_PYTHON_INSTALL_REGISTRY = '0'
    UV_PYTHON_NO_REGISTRY = '1'
    UV_PYTHON_PREFERENCE = 'only-managed'
    UV_LINK_MODE = 'hardlink'
}
foreach ($name in $pythonStorage.Keys) {
    [Environment]::SetEnvironmentVariable($name, $pythonStorage[$name], 'Process')
}
Add-PortablePath 'Path' @("$PortableData\lib\uv", "$PortableData\bin")
if ($env:VIRTUAL_ENV -and [System.IO.Directory]::Exists("$env:VIRTUAL_ENV\Scripts")) {
    Add-PortablePath 'Path' @("$env:VIRTUAL_ENV\Scripts")
}

function Get-PortableInitScript ([string]$CachePath, [scriptblock]$GetCacheKey, [scriptblock]$Generate) {
    $header = "# CodeNix init: $(& $GetCacheKey)"
    if ([System.IO.File]::Exists($CachePath)) {
        try {
            $cachedScript = [System.IO.File]::ReadAllText($CachePath)
            if ($cachedScript.StartsWith($header + [Environment]::NewLine, [StringComparison]::Ordinal)) {
                return [scriptblock]::Create($cachedScript)
            }
        } catch {
            Write-Warning "Ignoring invalid initialization cache: $CachePath"
        }
    }

    $scriptText = (& $Generate) -join [Environment]::NewLine
    if ([string]::IsNullOrWhiteSpace($scriptText)) {
        throw "No initialization script was generated for $CachePath"
    }
    $header = "# CodeNix init: $(& $GetCacheKey)"
    $scriptText = $header + [Environment]::NewLine + $scriptText
    $initScript = [scriptblock]::Create($scriptText)
    $tempPath = "$CachePath.$PID.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        [void][System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($CachePath))
        [System.IO.File]::WriteAllText($tempPath, $scriptText, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::Move($tempPath, $CachePath, $true)
    } catch {
        Write-Warning "Initialization cache could not be saved: $($_.Exception.Message)"
    } finally {
        if ([System.IO.File]::Exists($tempPath)) { [System.IO.File]::Delete($tempPath) }
    }
    return $initScript
}

function Get-PortableFileStamp ([string]$Path) {
    $file = [System.IO.FileInfo]::new($Path)
    return "$Path|$($file.Length)|$($file.LastWriteTimeUtc.Ticks)|$($PSVersionTable.PSVersion)"
}

$OmpTheme            = "$PortableData\lib\pwsh\ohmyposh\themes\tos-therm.omp.json"
$OhMyPoshExe         = "$Bin\oh-my-posh.exe"
$OmpCacheDir         = "$PortableData\cache\oh-my-posh"
$env:POSH_CACHE_PATH = $OmpCacheDir
$OmpInitCache        = "$CacheDir\omp-init-v4.ps1"

if ([System.IO.File]::Exists($OhMyPoshExe) -and [System.IO.File]::Exists($OmpTheme)) {
    try {
        Remove-Item Env:\POSH_SESSION_ID -ErrorAction SilentlyContinue
        [void][System.IO.Directory]::CreateDirectory($OmpCacheDir)
        $ompInitScript = Get-PortableInitScript $OmpInitCache {
            "$(Get-PortableFileStamp $OhMyPoshExe)|$(Get-PortableFileStamp $OmpTheme)|$OmpCacheDir"
        } {
            $themeBytes = [System.IO.File]::ReadAllBytes($OmpTheme)
            if ($themeBytes.Length -ge 3 -and $themeBytes[0] -eq 0xEF -and $themeBytes[1] -eq 0xBB -and $themeBytes[2] -eq 0xBF) {
                $themeTempPath = "$OmpTheme.$PID.$([guid]::NewGuid().ToString('N')).tmp"
                try {
                    [System.IO.File]::WriteAllText($themeTempPath,
                        [System.Text.Encoding]::UTF8.GetString($themeBytes, 3, $themeBytes.Length - 3),
                        [System.Text.UTF8Encoding]::new($false))
                    [System.IO.File]::Move($themeTempPath, $OmpTheme, $true)
                } finally {
                    if ([System.IO.File]::Exists($themeTempPath)) { [System.IO.File]::Delete($themeTempPath) }
                }
            }
            $output = & $OhMyPoshExe init pwsh --config $OmpTheme --print
            if ($LASTEXITCODE -ne 0) { throw "Oh My Posh exited with code $LASTEXITCODE" }
            $output
        }
        . $ompInitScript
        $env:POSH_SESSION_ID = [guid]::NewGuid().ToString()
    } catch {
        Write-Warning "Oh My Posh initialization failed: $($_.Exception.Message)"
    }
}

# Cached zoxide Initialization
$zoxideInitCache = "$CacheDir\zoxide-init-v2.ps1"
$zoxideCommand = Get-Command zoxide -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($zoxideCommand) {
    try {
        $zoxideInitScript = Get-PortableInitScript $zoxideInitCache {
            "$(Get-PortableFileStamp $zoxideCommand.Source)|$PortableData"
        } {
            $output = & $zoxideCommand.Source init powershell
            if ($LASTEXITCODE -ne 0) { throw "zoxide exited with code $LASTEXITCODE" }
            $output
        }
        . $zoxideInitScript
    } catch {
        Write-Warning "zoxide initialization failed: $($_.Exception.Message)"
    }
}

function Invoke-CodeNixLsd {
    $directories = @(foreach ($argument in $args) {
        if ($argument -isnot [string] -or $argument.StartsWith('-')) { continue }
        try {
            $path = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($argument)
            if ([System.IO.Directory]::Exists($path)) { $path }
        } catch {}
    })
    if ($directories.Count -eq 0) {
        $directories = @($ExecutionContext.SessionState.Path.CurrentFileSystemLocation.ProviderPath)
    }
    $rootListing = $false
    foreach ($directory in $directories) {
        if ($directory.TrimEnd('\', '/') -eq [System.IO.Path]::GetPathRoot($directory).TrimEnd('\', '/')) {
            $rootListing = $true
            break
        }
    }
    $options = @(if ($rootListing) {
        foreach ($name in @('pagefile.sys', 'swapfile.sys', 'hiberfil.sys', 'System Volume Information',
            'WindowsApps', '$RECYCLE.BIN', 'Config.Msi', 'DumpStack.log', 'DumpStack.log.tmp')) {
            '--ignore-glob'
            $name
        }
    })
    & lsd @options @args
}

if (Get-Command lsd -CommandType Application -ErrorAction SilentlyContinue) {
    Remove-Item Alias:ls -ErrorAction SilentlyContinue
    function ls { Invoke-CodeNixLsd @args }
    function ll { Invoke-CodeNixLsd -l @args }
    function la { Invoke-CodeNixLsd -la @args }
}

$env:BAT_THEME = 'ansi'
$portableAliases = @{
    cat = 'bat'; dig = 'dog'; grep = 'rg'; find = 'fd'; ps = 'procs'
    top = 'btm'; du = 'dust'; df = 'duf'; ping = 'gping'; http = 'xh'; sed = 'sd'
}
foreach ($aliasName in $portableAliases.Keys) {
    $application = Get-Command $portableAliases[$aliasName] -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($application) { Set-Alias -Name $aliasName -Value $application.Source -Force }
}

# Configure PSReadLine
$HistoryPath = "$PortableData\user-data\PowerShell\History.txt"
$HistoryDir  = [System.IO.Path]::GetDirectoryName($HistoryPath)
$PSReadLineReady = $false
try {
    Import-Module PSReadLine -ErrorAction Stop
    [void][System.IO.Directory]::CreateDirectory($HistoryDir)

    Set-PSReadLineOption -EditMode Emacs `
                     -PredictionSource HistoryAndPlugin `
                     -PredictionViewStyle InlineView `
                     -HistoryNoDuplicates `
                     -HistorySearchCursorMovesToEnd `
                     -HistorySaveStyle SaveIncrementally `
                     -HistorySavePath $HistoryPath -ErrorAction Stop
    $PSReadLineReady = $true
} catch {
    Write-Warning "PSReadLine configuration failed: $($_.Exception.Message)"
}

$script:CodeNixPSFzfModule = $null
function Get-CodeNixHistory ([object[]]$Items = [Microsoft.PowerShell.PSConsoleReadLine]::GetHistoryItems()) {
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($index = $Items.Count - 1; $index -ge 0; $index--) {
        $command = $Items[$index].CommandLine
        if (-not [string]::IsNullOrWhiteSpace($command) -and $seen.Add($command)) { $command }
    }
}

function Select-CodeNixHistory ([string]$Query, [string[]]$History = @(Get-CodeNixHistory)) {
    if ($History.Count -eq 0) { return }
    $choices = for ($index = 0; $index -lt $History.Count; $index++) {
        "$index`t$($History[$index] -replace '\r?\n', ' ')"
    }
    $selection = $choices | Invoke-Fzf -Query $Query -NoSort `
        -Layout reverse -Height '60%' -Bind 'ctrl-r:toggle-sort'
    if ($selection -and $selection -match '^(\d+)\t') {
        $selectedIndex = [int]$matches[1]
        if ($selectedIndex -lt $History.Count) { $History[$selectedIndex] }
    }
}

function Invoke-CodeNixHistorySearch {
    $line = $null
    $cursor = 0
    [Microsoft.PowerShell.PSConsoleReadLine]::GetBufferState([ref]$line, [ref]$cursor)
    $selection = Select-CodeNixHistory $line
    if ($null -ne $selection) {
        [Microsoft.PowerShell.PSConsoleReadLine]::Replace(0, $line.Length, $selection)
        [Microsoft.PowerShell.PSConsoleReadLine]::EndOfLine()
    }
}

function Invoke-LazyPsFzf ([string]$TargetKey) {
    try {
        if (-not $script:CodeNixPSFzfModule) {
            if (-not (Get-Command fzf -CommandType Application -ErrorAction SilentlyContinue)) {
                throw 'fzf is not installed or is not in PATH.'
            }
            $module = Import-Module PSFzf -Global -PassThru -ErrorAction Stop
            Set-PsFzfOption -PsReadlineChordProvider 'Ctrl+t' `
                            -PsReadlineChordReverseHistory 'Ctrl+r' -ErrorAction Stop
            Set-PSReadLineKeyHandler -Key 'Ctrl+r' -BriefDescription CodeNixHistorySearch -ScriptBlock { Invoke-LazyPsFzf 'Ctrl+r' }
            $script:CodeNixPSFzfModule = $module
        }
        if ($TargetKey -ieq 'Ctrl+r') { Invoke-CodeNixHistorySearch }
        elseif ($TargetKey -ieq 'Ctrl+t') {
            & $script:CodeNixPSFzfModule { Invoke-FzfPsReadlineHandlerProvider }
        }
    } catch {
        Write-Warning "PSFzf is unavailable: $($_.Exception.Message)"
        if ($TargetKey -ieq 'Ctrl+r') {
            [Microsoft.PowerShell.PSConsoleReadLine]::ReverseSearchHistory()
        }
    }
}

function Get-CodeNixPathCompletion ([string]$Word, [switch]$DirectoriesOnly) {
    $path = $Word
    if ($path.StartsWith("'")) {
        $path = $path.Substring(1)
        if ($path.EndsWith("'")) { $path = $path.Substring(0, $path.Length - 1) }
        $path = $path.Replace("''", "'")
    } elseif ($path.StartsWith('"')) {
        $path = $path.Substring(1)
        if ($path.EndsWith('"')) { $path = $path.Substring(0, $path.Length - 1) }
    }
    $separator = if ($path.Contains('/')) { '/' } else { '\' }
    $path = $path.Replace('/', '\')
    try {
        $relativeBase = $null
        $relativePrefix = ''
        if ($path -eq '~' -or $path.StartsWith('~\')) {
            $parent = $HOME
            $remaining = $path.Substring(1).TrimStart('\')
            $relativeBase = $parent
            $relativePrefix = '~\'
        } elseif ($path -match '^[A-Za-z]:') {
            $driveRoot = $path.Substring(0, 2) + '\'
            $parent = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($driveRoot)
            $remaining = $path.Substring(2).TrimStart('\')
            $relativeBase = $parent
            $relativePrefix = $driveRoot
        } else {
            $root = [System.IO.Path]::GetPathRoot($path)
            $parent = if ($root) {
                $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($root)
            } else {
                $ExecutionContext.SessionState.Path.CurrentFileSystemLocation.ProviderPath
            }
            $remaining = $path.Substring($root.Length)
            if (-not $root) {
                $relativeBase = $parent
                if ($path.StartsWith('.\')) { $relativePrefix = '.\' }
            }
        }
        $parts = @($remaining.Split('\', [StringSplitOptions]::RemoveEmptyEntries))
        $parents = @($parent)
        $intermediateCount = if ($path.EndsWith('\')) { $parts.Count } else { [Math]::Max(0, $parts.Count - 1) }
        for ($index = 0; $index -lt $intermediateCount; $index++) {
            $part = $parts[$index]
            $parents = @(foreach ($directory in $parents) {
                if ($part -eq '.') { $directory }
                elseif ($part -eq '..') { [System.IO.Path]::GetFullPath("$directory\..") }
                elseif ([System.IO.Directory]::Exists("$directory\$part")) { "$($directory.TrimEnd('\'))\$part" }
                else {
                    Get-ChildItem -LiteralPath $directory -Directory -Force -ErrorAction SilentlyContinue |
                        Where-Object { $_.Name.StartsWith($part, [StringComparison]::OrdinalIgnoreCase) } |
                        Select-Object -ExpandProperty FullName
                }
            })
            if ($parents.Count -eq 0) { return }
        }
        $prefix = if ($parts.Count -gt $intermediateCount) { $parts[-1] } else { '' }
        $items = foreach ($directory in $parents) {
            Get-ChildItem -LiteralPath $directory -Directory:$DirectoriesOnly -Force -ErrorAction SilentlyContinue |
                Where-Object { $_.Name.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) }
        }
        foreach ($item in ($items | Sort-Object FullName -Unique)) {
            $text = if ($null -ne $relativeBase) {
                $relativePrefix + [System.IO.Path]::GetRelativePath($relativeBase, $item.FullName)
            } else { $item.FullName }
            $text = $text.Replace('\', $separator)
            $kind = [System.Management.Automation.CompletionResultType]::ProviderItem
            if ($item.PSIsContainer) {
                $text += $separator
                $kind = [System.Management.Automation.CompletionResultType]::ProviderContainer
            }
            if ($text -match '[^\w\\/:.\-+]') { $text = "'$($text.Replace("'", "''"))'" }
            [System.Management.Automation.CompletionResult]::new($text, $item.Name, $kind, $item.FullName)
        }
    } catch { return }
}

$pathCompleter = {
    param($CommandName, $ParameterName, $WordToComplete, $CommandAst, $FakeBoundParameters)
    Get-CodeNixPathCompletion $WordToComplete -DirectoriesOnly:($CommandName -in @('cd', 'chdir', 'sl', 'pushd', 'Set-Location', 'Push-Location'))
}
foreach ($parameterName in @('Path', 'LiteralPath')) {
    Register-ArgumentCompleter -CommandName Set-Location, Push-Location, Get-ChildItem, Get-Item, Get-Content, Set-Content, Add-Content, Remove-Item, Copy-Item, Move-Item `
        -ParameterName $parameterName -ScriptBlock $pathCompleter
}

if ($PSReadLineReady) {
    Set-PSReadLineKeyHandler -Key 'Ctrl+t' -ScriptBlock { Invoke-LazyPsFzf 'Ctrl+t' }
    Set-PSReadLineKeyHandler -Key 'Ctrl+r' -BriefDescription CodeNixHistorySearch -ScriptBlock { Invoke-LazyPsFzf 'Ctrl+r' }

    Set-PSReadLineKeyHandler -Key Ctrl+a -Function BeginningOfLine
    Set-PSReadLineKeyHandler -Key Ctrl+e -Function EndOfLine
    Set-PSReadLineKeyHandler -Key Alt+b  -Function BackwardWord
    Set-PSReadLineKeyHandler -Key Alt+f  -Function ForwardWord
    Set-PSReadLineKeyHandler -Key Ctrl+u -Function BackwardKillLine
    Set-PSReadLineKeyHandler -Key Ctrl+k -Function KillLine
    Set-PSReadLineKeyHandler -Key Tab    -Function MenuComplete
    Set-PSReadLineKeyHandler -Key Shift+Tab -Function TabCompletePrevious
    Set-PSReadLineKeyHandler -Key UpArrow -Function HistorySearchBackward
    Set-PSReadLineKeyHandler -Key DownArrow -Function HistorySearchForward
    Set-PSReadLineKeyHandler -Key Ctrl+b -Function BackwardChar
    Set-PSReadLineKeyHandler -Key Ctrl+f -Function ForwardChar
    Set-PSReadLineKeyHandler -Key Ctrl+w -Function UnixWordRubout
    Set-PSReadLineKeyHandler -Key Alt+Backspace -Function BackwardKillWord
    Set-PSReadLineKeyHandler -Key Alt+d -Function KillWord
    Set-PSReadLineKeyHandler -Key Ctrl+y -Function Yank
    Set-PSReadLineKeyHandler -Key Ctrl+l -Function ClearScreen
    Set-PSReadLineKeyHandler -Key Ctrl+_ -Function Undo
}

# Helper Functions & .py Runner
function which ($command) {
    Get-Command -Name $command -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_ -is [System.Management.Automation.AliasInfo]) { $_ = $_.ResolvedCommand }
        if ($_ -is [System.Management.Automation.ApplicationInfo]) { $_.Path }
        elseif ($_) { $_.Definition }
    }
}

function clh {
    $histPath = (Get-PSReadLineOption).HistorySavePath
    if ($histPath -and [System.IO.File]::Exists($histPath)) { Remove-Item -LiteralPath $histPath -Force }
    [Microsoft.PowerShell.PSConsoleReadLine]::ClearHistory()
    Clear-History
}

function ..   { Set-Location .. }
function ...  { Set-Location ../.. }
function .... { Set-Location ../../.. }

function Repair-CodeNixPythonEnvironment ([string]$EnvironmentPath) {
    $configuration = Join-Path $EnvironmentPath 'pyvenv.cfg'
    if (-not [System.IO.File]::Exists($configuration)) { throw "Not a Python virtual environment: $EnvironmentPath" }
    $values = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($configuration)) {
        $separator = $line.IndexOf('=')
        if ($separator -gt 0) { $values[$line.Substring(0, $separator).Trim()] = $line.Substring($separator + 1).Trim() }
    }
    $store = [System.IO.Path]::GetFullPath($env:UV_PYTHON_INSTALL_DIR).TrimEnd('\') + '\'
    if ($values.home -and $values.home.Replace('/', '\').Contains('\data\lib\python\') -and
        -not [System.IO.Path]::GetFullPath($values.home).StartsWith($store, [StringComparison]::OrdinalIgnoreCase) -and
        $values.version_info -match '^3\.\d+\.\d+$') {
        $runtime = Join-Path $store "cpython-$($values.version_info)-windows-x86_64-none\python.exe"
        $uv = "$PortableData\lib\uv\uv.exe"
        if ([System.IO.File]::Exists($runtime) -and [System.IO.File]::Exists($uv)) {
            & $uv venv --python $values.version_info --relocatable --allow-existing --no-python-downloads --no-config --no-project $EnvironmentPath | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Could not rebase virtual environment: $EnvironmentPath" }
        }
    }
}

function Get-CodeNixPythonEnvironment ([string]$FilePath) {
    if ($env:VIRTUAL_ENV) {
        $environmentPath = [System.IO.Path]::GetFullPath($env:VIRTUAL_ENV)
    } else {
        $directory = [System.IO.DirectoryInfo]::new([System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($FilePath)))
        $environmentPath = $null
        while ($directory) {
            $candidate = Join-Path $directory.FullName '.venv'
            if ([System.IO.File]::Exists((Join-Path $candidate 'pyvenv.cfg'))) {
                $environmentPath = $candidate
                break
            }
            if ([System.IO.File]::Exists((Join-Path $directory.FullName 'pyproject.toml'))) { break }
            $directory = $directory.Parent
        }
    }
    if (-not $environmentPath) { throw 'No project virtual environment. Create or activate one with uv before running Python.' }
    Repair-CodeNixPythonEnvironment $environmentPath
    $python = Join-Path $environmentPath 'Scripts\python.exe'
    if (-not [System.IO.File]::Exists($python)) { throw "The project virtual environment is unavailable: $environmentPath" }
    return $environmentPath
}

function Get-PyRunner ([string]$FilePath) {
    $environmentPath = Get-CodeNixPythonEnvironment $FilePath
    $reader = $null
    try {
        $reader = [System.IO.File]::OpenText($FilePath)
        $firstLine = $reader.ReadLine()
        if ($null -ne $firstLine -and $firstLine -match '^#!\s*(?:\S*[\\/])?(?:env\s+(?:-S\s+)?)?replx(?:\.exe)?(?:\s|$)') {
            $replx = Join-Path $environmentPath 'Scripts\replx.exe'
            if (-not [System.IO.File]::Exists($replx)) { throw 'replx is not installed in the project virtual environment.' }
            return $replx
        }
    } finally {
        if ($null -ne $reader) { $reader.Dispose() }
    }
    return (Join-Path $environmentPath 'Scripts\python.exe')
}

function Invoke-CodeNixPythonScript {
    param([string]$FilePath)
    $runner = Get-PyRunner $FilePath
    & $runner $FilePath @args
}

if (-not (Get-Variable -Name CodeNixCommandNotFoundAction -Scope Script -ErrorAction SilentlyContinue)) {
    $previousCommandNotFoundAction = $ExecutionContext.InvokeCommand.CommandNotFoundAction
    $script:CodeNixCommandNotFoundAction = {
        param($CommandName, $CommandLookupEventArgs)
        $cleanName = $CommandName -replace '^__py_exec__:', ''
        if ($cleanName -like '*.py') {
            try {
                $targetFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($cleanName)
                if ([System.IO.File]::Exists($targetFile)) {
                    $CommandLookupEventArgs.CommandScriptBlock = { Invoke-CodeNixPythonScript $targetFile @args }.GetNewClosure()
                    $CommandLookupEventArgs.StopSearch = $true
                    return
                }
            } catch {}
        }
        if ($previousCommandNotFoundAction) {
            $previousCommandNotFoundAction.Invoke($CommandName, $CommandLookupEventArgs)
        }
    }.GetNewClosure()
}
$ExecutionContext.InvokeCommand.CommandNotFoundAction = $script:CodeNixCommandNotFoundAction

function ConvertTo-PyCommandLine ([string]$Line) {
    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput($Line, [ref]$tokens, [ref]$parseErrors)
    $start = $Line.Length - $Line.TrimStart().Length
    $fileExpression = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
            $node.Extent.StartOffset -eq $start
    }, $true)
    if (-not $fileExpression -or -not $fileExpression.Value.EndsWith('.py', [StringComparison]::OrdinalIgnoreCase)) {
        return $Line
    }
    try {
        $targetFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($fileExpression.Value)
        if (-not [System.IO.File]::Exists($targetFile)) { return $Line }
    } catch { return $Line }
    $replacement = "& 'Invoke-CodeNixPythonScript' '$($targetFile.Replace("'", "''"))'"
    return $Line.Substring(0, $start) + $replacement + $Line.Substring($fileExpression.Extent.EndOffset)
}

$script:CodeNixAcceptLine = { [Microsoft.PowerShell.PSConsoleReadLine]::AcceptLine() }
$ompModule = (Get-Command Set-TransientPrompt -CommandType Function -ErrorAction SilentlyContinue |
    Select-Object -First 1).Module
if ($ompModule -and $ompInitScript -is [scriptblock]) {
    $enterFactory = $ompInitScript.Ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq 'New-EnterKeyHandler'
    }, $true)
    if ($enterFactory) {
        $ompAcceptLine = & $ompModule {
            param([scriptblock]$Factory)
            . $Factory
            New-EnterKeyHandler { [Microsoft.PowerShell.PSConsoleReadLine]::AcceptLine() } $script:Streaming
        } ([scriptblock]::Create($enterFactory.Extent.Text))
        if ($ompAcceptLine -is [scriptblock]) { $script:CodeNixAcceptLine = $ompAcceptLine }
    }
}

if ($PSReadLineReady) {
    function global:PSConsoleHostReadLine {
        [System.Diagnostics.DebuggerHidden()]
        param()

        $lastRunStatus = $?
        Microsoft.PowerShell.Core\Set-StrictMode -Off
        $line = [Microsoft.PowerShell.PSConsoleReadLine]::ReadLine($host.Runspace, $ExecutionContext, $lastRunStatus)
        if ($null -ne $line) { ConvertTo-PyCommandLine $line }
    }

    Set-PSReadLineKeyHandler -Key Enter -ScriptBlock {
        & $script:CodeNixAcceptLine
    }
}

# Help command '??'
function global:?? {
    if (-not $UnixBin -or -not [System.IO.Directory]::Exists($UnixBin)) {
        Write-Host 'modern-unix-win binaries are not installed.' -ForegroundColor Red
        return
    }
    $modernUnixBin = $UnixBin
    if ([System.IO.Directory]::Exists($modernUnixBin)) {
        $descriptions = @{
            "bat"       = @{ desc = "A cat(1) clone with syntax highlighting and Git integration"; alias = "cat" }
            "broot"     = @{ desc = "A tree explorer and a customizable launcher"; alias = $null }
            "btm"       = @{ desc = "A customizable cross-platform graphical process/system monitor"; alias = "top" }
            "cheat"     = @{ desc = "Create and view interactive cheatsheets on the command-line"; alias = $null }
            "curlie"    = @{ desc = "The power of curl with the ease of use of httpie (curl frontend)"; alias = $null }
            "delta"     = @{ desc = "A viewer for git and diff output"; alias = $null }
            "dog"       = @{ desc = "Command-line DNS client"; alias = "dig" }
            "duf"       = @{ desc = "Disk Usage/Free Utility - a better df alternative"; alias = "df" }
            "dust"      = @{ desc = "A more intuitive version of du in rust"; alias = "du" }
            "fd"        = @{ desc = "A simple, fast and user-friendly alternative to find"; alias = "find" }
            "fzf"       = @{ desc = "A command-line fuzzy finder"; alias = $null }
            "gping"     = @{ desc = "Ping, but with a graph"; alias = "ping" }
            "hyperfine" = @{ desc = "A command-line benchmarking tool"; alias = $null }
            "jq"        = @{ desc = "Command-line JSON processor"; alias = $null }
            "lsd"       = @{ desc = "An ls command with a lot of pretty colors and some other stuff"; alias = "ls, ll, la" }
            "procs"     = @{ desc = "A modern replacement for ps"; alias = "ps" }
            "rg"        = @{ desc = "Recursively searches the current directory for a regex pattern"; alias = "grep" }
            "sd"        = @{ desc = "Intuitive find & replace CLI (sed alternative)"; alias = "sed" }
            "xh"        = @{ desc = "A friendly and fast tool for sending HTTP requests"; alias = "http" }
            "zoxide"    = @{ desc = "A smarter cd command for your terminal"; alias = "z" }
        }

        Write-Host "`nModern Unix Commands (modern-unix-win v$modernUnixVersion):" -ForegroundColor Cyan
        Write-Host ("=" * 80) -ForegroundColor DarkGray

        Get-ChildItem -LiteralPath $modernUnixBin -File -Filter '*.exe' | Sort-Object Name | ForEach-Object {
            $name = $_.BaseName
            $info = $descriptions[$name]
            if ($info) {
                Write-Host ("  " + $name.PadRight(12)) -ForegroundColor Green -NoNewline
                if ($info.alias) {
                    Write-Host ("[" + $info.alias + "]").PadRight(14) -ForegroundColor Yellow -NoNewline
                } else {
                    Write-Host (" " * 14) -NoNewline
                }
                Write-Host $info.desc -ForegroundColor Gray
            }
        }
        Write-Host ""
    }
}
