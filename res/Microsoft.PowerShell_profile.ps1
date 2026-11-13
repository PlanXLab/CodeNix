# PowerShell profile for the CodeNix portable environment (Optimized v2)

if (-not $Host.UI.SupportsVirtualTerminal -or [Console]::IsOutputRedirected) {
    return
}

# PowerShell profile for the CodeNix portable environment
try {
    [Console]::InputEncoding  = [System.Text.Encoding]::UTF8
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
} catch {}

$Base                = $PSScriptRoot
$Bin                 = "$Base\bin"
$Modules             = "$Base\Modules"
$InstallRoot         = [System.IO.Path]::GetFullPath("$Base\..\..\..")
$PortableData        = "$InstallRoot\data"
$CacheDir            = "$PortableData\cache\pwsh-init"
$OmpCacheDir         = "$PortableData\cache\oh-my-posh"
$env:POSH_CACHE_PATH = $OmpCacheDir
$env:POSH_THEME      = "$PortableData\lib\pwsh\ohmyposh\themes\tos.omp.json"
$env:BAT_THEME       = 'ansi'

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

# Unified Static Cache (.ps1 direct dot-sourcing without [scriptblock]::Create)
$UnifiedCacheFile = "$CacheDir\codenix-fast-init-v2.ps1"

if (-not [System.IO.File]::Exists($UnifiedCacheFile)) {
    [void][System.IO.Directory]::CreateDirectory($CacheDir)
    [void][System.IO.Directory]::CreateDirectory($OmpCacheDir)

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

    $sb = [System.Text.StringBuilder]::new(16384)
    [void]$sb.AppendLine("`$UnixBin = '$UnixBin'")
    [void]$sb.AppendLine("`$modernUnixVersion = '$modernUnixVersion'")
    [void]$sb.AppendLine("Add-PortablePath 'Path' @(`$Bin, `$UnixBin)")
    [void]$sb.AppendLine("Add-PortablePath 'PSModulePath' @(`$Modules)")

    # Pre-resolve modern-unix-win aliases
    $portableAliases = @{
        cat = 'bat'; dig = 'dog'; grep = 'rg'; find = 'fd'; ps = 'procs'
        top = 'btm'; du = 'dust'; df = 'duf'; ping = 'gping'; http = 'xh'; sed = 'sd'
    }
    foreach ($entry in $portableAliases.GetEnumerator()) {
        $exeName = "$($entry.Value).exe"
        $targetPath = if ($UnixBin -and [System.IO.File]::Exists("$UnixBin\$exeName")) { "$UnixBin\$exeName" }
                      elseif ([System.IO.File]::Exists("$Bin\$exeName")) { "$Bin\$exeName" }
                      else { $null }
        if ($targetPath) {
            [void]$sb.AppendLine("Set-Alias -Name '$($entry.Key)' -Value '$targetPath' -Force")
        }
    }

    # Pre-generate Oh My Posh init script
    $OmpTheme    = "$PortableData\lib\pwsh\ohmyposh\themes\tos.omp.json"
    $OhMyPoshExe = "$Bin\oh-my-posh.exe"
    if ([System.IO.File]::Exists($OhMyPoshExe) -and [System.IO.File]::Exists($OmpTheme)) {
        $themeBytes = [System.IO.File]::ReadAllBytes($OmpTheme)
        if ($themeBytes.Length -ge 3 -and $themeBytes[0] -eq 0xEF -and $themeBytes[1] -eq 0xBB -and $themeBytes[2] -eq 0xBF) {
            [System.IO.File]::WriteAllText($OmpTheme,
                [System.Text.Encoding]::UTF8.GetString($themeBytes, 3, $themeBytes.Length - 3),
                [System.Text.UTF8Encoding]::new($false))
        }
        $ompOut = (& $OhMyPoshExe init pwsh --config $OmpTheme --print) -join [Environment]::NewLine
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($ompOut)) {
            [void]$sb.AppendLine('Remove-Item Env:\POSH_SESSION_ID -ErrorAction SilentlyContinue')
            [void]$sb.AppendLine($ompOut)
            [void]$sb.AppendLine('$env:POSH_SESSION_ID = [guid]::NewGuid().ToString()')
        }
    }

    # Pre-generate Zoxide init script
    $zoxideExe = if ($UnixBin -and [System.IO.File]::Exists("$UnixBin\zoxide.exe")) { "$UnixBin\zoxide.exe" }
                 elseif ([System.IO.File]::Exists("$Bin\zoxide.exe")) { "$Bin\zoxide.exe" }
                 else { $null }
    if ($zoxideExe) {
        $zoxideOut = (& $zoxideExe init powershell) -join [Environment]::NewLine
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($zoxideOut)) {
            [void]$sb.AppendLine($zoxideOut)
        }
    }

    $tempPath = "$UnifiedCacheFile.$PID.tmp"
    [System.IO.File]::WriteAllText($tempPath, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::Move($tempPath, $UnifiedCacheFile, $true)
}

$HistoryPath = "$PortableData\user-data\PowerShell\History.txt"
$historyDir  = [System.IO.Path]::GetDirectoryName($HistoryPath)
if (-not [System.IO.Directory]::Exists($historyDir)) {
    [void][System.IO.Directory]::CreateDirectory($historyDir)
}

try {
    if (-not (Get-Module PSReadLine)) {
        Import-Module PSReadLine -ErrorAction Stop
    }
    
    $predSource = if ($Host.UI.SupportsVirtualTerminal -and -not [Console]::IsOutputRedirected) { 'History' } else { 'None' }

    Set-PSReadLineOption -EditMode Emacs `
                         -PredictionSource $predSource `
                         -PredictionViewStyle InlineView `
                         -HistoryNoDuplicates `
                         -HistorySearchCursorMovesToEnd `
                         -HistorySaveStyle SaveIncrementally `
                         -HistorySavePath $HistoryPath

    Set-PSReadLineKeyHandler -Key 'Ctrl+t'  -ScriptBlock { Invoke-LazyPsFzf 'Ctrl+t' }
    Set-PSReadLineKeyHandler -Key 'Ctrl+r'  -BriefDescription CodeNixHistorySearch -ScriptBlock { Invoke-LazyPsFzf 'Ctrl+r' }
    Set-PSReadLineKeyHandler -Key Tab       -Function MenuComplete
    Set-PSReadLineKeyHandler -Key Shift+Tab -Function TabCompletePrevious
    Set-PSReadLineKeyHandler -Key UpArrow   -Function HistorySearchBackward
    Set-PSReadLineKeyHandler -Key DownArrow -Function HistorySearchForward
} catch {
    Write-Warning "PSReadLine init error: $($_.Exception.Message)"
}

. $UnifiedCacheFile

# Oh My Posh Transient Prompt
if (Get-Command -Name Enable-PoshTransientPrompt -CommandType Function -ErrorAction SilentlyContinue) {
    Enable-PoshTransientPrompt
}

# Functions, Lazy PSFzf & Completions
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

Remove-Item Alias:ls -ErrorAction SilentlyContinue
function ls { Invoke-CodeNixLsd @args }
function ll { Invoke-CodeNixLsd -l @args }
function la { Invoke-CodeNixLsd -la @args }

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
Register-ArgumentCompleter -CommandName Set-Location, Push-Location, Get-ChildItem, Get-Item, Get-Content, Set-Content, Add-Content, Remove-Item, Copy-Item, Move-Item `
    -ParameterName Path -ScriptBlock $pathCompleter
Register-ArgumentCompleter -CommandName Set-Location, Push-Location, Get-ChildItem, Get-Item, Get-Content, Set-Content, Add-Content, Remove-Item, Copy-Item, Move-Item `
    -ParameterName LiteralPath -ScriptBlock $pathCompleter

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

function Get-CodeNixPythonEnvironment {
    $isActivated = -not [string]::IsNullOrWhiteSpace($env:VIRTUAL_ENV)
    $environment = if ($isActivated) { $env:VIRTUAL_ENV } else { Join-Path $PortableData 'venvs\global' }
    if (-not [System.IO.Directory]::Exists($environment) -or
        -not [System.IO.File]::Exists((Join-Path $environment 'pyvenv.cfg'))) {
        if ($isActivated) { throw "Not a Python virtual environment: $environment" }
        throw "The CodeNix global Python environment is unavailable: $environment"
    }

    $pythonPath = Join-Path $environment 'Scripts\python.exe'
    if (-not [System.IO.File]::Exists($pythonPath)) {
        if ($isActivated) { throw "The active virtual environment is unavailable: $environment" }
        throw "The CodeNix global Python interpreter is unavailable: $pythonPath"
    }
    $environment
}

function Get-PyRunner ([string]$FilePath) {
    $environment = Get-CodeNixPythonEnvironment
    $reader = [System.IO.File]::OpenText($FilePath)
    try { $firstLine = $reader.ReadLine() }
    finally { $reader.Dispose() }

    if ($null -ne $firstLine -and
        $firstLine -match '^#!\s*(?:\S*[\\/])?(?:env\s+(?:-S\s+)?)?replx(?:\.exe)?(?:\s|$)') {
        $runner = Join-Path $environment 'Scripts\replx.exe'
        if (-not [System.IO.File]::Exists($runner)) {
            throw "replx is not installed in the selected Python environment: $runner"
        }
        return $runner
    }
    Join-Path $environment 'Scripts\python.exe'
}

function Invoke-CodeNixPythonScript {
    param([string]$FilePath)
    $fullPath = [System.IO.Path]::GetFullPath($FilePath)
    $runner = Get-PyRunner $fullPath
    & $runner $fullPath @args
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

function global:python {
    $targetScript = $args | Where-Object { $_ -is [string] -and $_ -like '*.py' -and [System.IO.File]::Exists($_) } | Select-Object -First 1
    if ($targetScript) {
        $remainingArgs = @($args | Where-Object { $_ -ne $targetScript })
        Invoke-CodeNixPythonScript $targetScript @remainingArgs
    } else {
        $runner = Join-Path (Get-CodeNixPythonEnvironment) 'Scripts\python.exe'
        & $runner @args
    }
}

function global:py {
    python @args
}

Remove-Alias -Name rm, rmdir, cp, mv, mkdir, touch, ? -Force -ErrorAction SilentlyContinue

function Get-UnixStyleArgs {
    param([array]$Arguments)
    $flags = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $targets = [System.Collections.Generic.List[string]]::new()
    foreach ($arg in $Arguments) {
        if ($arg -is [string] -and $arg -match '^-[rfRF]+$') {
            foreach ($char in $arg.TrimStart('-').GetEnumerator()) { $flags.Add([string]$char) }
        } else {
            $targets.Add($arg)
        }
    }
    return [pscustomobject]@{ Flags = $flags; Targets = $targets }
}

function rm {
    $parsed = Get-UnixStyleArgs $args
    if ($parsed.Targets.Count -eq 0) { return }
    $params = @{ Path = $parsed.Targets }
    if ($parsed.Flags.Contains('r') -or $parsed.Flags.Contains('R')) { $params['Recurse'] = $true }
    if ($parsed.Flags.Contains('f')) {
        $params['Force'] = $true
        $params['ErrorAction'] = 'SilentlyContinue'
    }
    Remove-Item @params
}

function rmdir {
    rm @args
}

function cp {
    $parsed = Get-UnixStyleArgs $args
    if ($parsed.Targets.Count -lt 2) { return }
    $params = @{
        Path        = $parsed.Targets[0..($parsed.Targets.Count - 2)]
        Destination = $parsed.Targets[-1]
    }
    if ($parsed.Flags.Contains('r') -or $parsed.Flags.Contains('R')) { $params['Recurse'] = $true }
    if ($parsed.Flags.Contains('f')) { $params['Force'] = $true }
    Copy-Item @params
}

function mv {
    $parsed = Get-UnixStyleArgs $args
    if ($parsed.Targets.Count -lt 2) { return }
    $params = @{
        Path        = $parsed.Targets[0..($parsed.Targets.Count - 2)]
        Destination = $parsed.Targets[-1]
    }
    if ($parsed.Flags.Contains('f')) { $params['Force'] = $true }
    Move-Item @params
}

function mkdir {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
        [string[]]$Paths
    )
    foreach ($path in $Paths) {
        New-Item -ItemType Directory -Force -Path $path | Out-Null
    }
}

function touch {
    $parsed = Get-UnixStyleArgs $args
    if ($parsed.Targets.Count -eq 0) { return }
    
    foreach ($path in $parsed.Targets) {
        if (Test-Path -LiteralPath $path) {
            (Get-Item -LiteralPath $path).LastWriteTime = Get-Date
        } else {
            New-Item -ItemType File -Force -Path $path | Out-Null
        }
    }
}

function global:? {
    if (-not $UnixBin -or -not [System.IO.Directory]::Exists($UnixBin)) {
        Write-Host 'modern-unix-win binaries are not installed.' -ForegroundColor Red
        return
    }
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

    Get-ChildItem -LiteralPath $UnixBin -File -Filter '*.exe' | Sort-Object Name | ForEach-Object {
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