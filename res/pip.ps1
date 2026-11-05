<#
.SYNOPSIS
    Run uv pip commands against the active virtual environment or CodeNix's global Python environment.
#>
param(
    [Parameter(Position=0, Mandatory=$false)]
    [string]$Command,

    [Parameter(ValueFromRemainingArguments=$true)]
    [string[]]$RemainingArgs
)

function Get-PortableDataDirectory {
    if (-not [string]::IsNullOrWhiteSpace($env:VSCODE_PORTABLE)) {
        return [System.IO.Path]::GetFullPath($env:VSCODE_PORTABLE)
    }

    $directory = $PSScriptRoot
    for ($i = 0; $i -lt 3; $i++) {
        $directory = Split-Path -Parent $directory
        if ([string]::IsNullOrWhiteSpace($directory)) {
            throw "Could not determine the CodeNix data directory from '$PSScriptRoot'."
        }
    }
    return [System.IO.Path]::GetFullPath($directory)
}

function ConvertTo-PipArgumentList {
    param([AllowEmptyString()][string]$InputText)

    $arguments = New-Object 'System.Collections.Generic.List[string]'
    $argumentBuilder = New-Object System.Text.StringBuilder
    $quoteCharacter = [char]0
    $argumentStarted = $false

    for ($characterIndex = 0; $characterIndex -lt $InputText.Length; $characterIndex++) {
        $currentCharacter = $InputText[$characterIndex]
        if ($quoteCharacter -ne [char]0) {
            if ($currentCharacter -eq $quoteCharacter) {
                $quoteCharacter = [char]0
                continue
            }
            [void]$argumentBuilder.Append($currentCharacter)
            continue
        }

        if ($currentCharacter -eq [char]34 -or $currentCharacter -eq [char]39) {
            $quoteCharacter = $currentCharacter
            $argumentStarted = $true
            continue
        }
        if ([char]::IsWhiteSpace($currentCharacter)) {
            if ($argumentStarted) {
                [void]$arguments.Add($argumentBuilder.ToString())
                [void]$argumentBuilder.Clear()
                $argumentStarted = $false
            }
            continue
        }

        [void]$argumentBuilder.Append($currentCharacter)
        $argumentStarted = $true
    }

    if ($quoteCharacter -ne [char]0) {
        throw 'The install arguments contain an unmatched quote.'
    }
    if ($argumentStarted) {
        [void]$arguments.Add($argumentBuilder.ToString())
    }
    return $arguments.ToArray()
}

function Invoke-Uv {
    param([string[]]$Arguments)

    & $script:UvExecutable @Arguments
}

$dataDirectory = Get-PortableDataDirectory
$script:UvExecutable = Join-Path $dataDirectory 'lib\uv\uv.exe'
$globalEnvironmentDirectory = Join-Path $dataDirectory 'venvs\global'
$usingActiveEnvironment = -not [string]::IsNullOrWhiteSpace($env:VIRTUAL_ENV)
$pythonEnvironmentDirectory = if ($usingActiveEnvironment) {
    [System.IO.Path]::GetFullPath($env:VIRTUAL_ENV)
} else {
    $globalEnvironmentDirectory
}
$pythonExecutable = Join-Path $pythonEnvironmentDirectory 'Scripts\python.exe'
$pythonConfiguration = Join-Path $pythonEnvironmentDirectory 'pyvenv.cfg'

if (-not (Test-Path -LiteralPath $script:UvExecutable -PathType Leaf)) {
    throw "CodeNix uv was not found at '$script:UvExecutable'."
}

if ([string]::IsNullOrWhiteSpace($Command)) {
    Invoke-Uv @('pip', '--help')
    return
}

if ($Command -eq 'help') {
    if ($RemainingArgs.Count -gt 0) {
        Invoke-Uv (@('pip', $RemainingArgs[0], '--help') + @($RemainingArgs | Select-Object -Skip 1))
    } else {
        Invoke-Uv @('pip', '--help')
    }
    return
}

if ($Command.StartsWith('-')) {
    Invoke-Uv (@('pip', $Command) + @($RemainingArgs))
    return
}

if ($Command -eq 'install' -and $RemainingArgs.Count -eq 0) {
    $installInput = Read-Host 'Enter a package or install target (press Enter to cancel)'
    if ([string]::IsNullOrWhiteSpace($installInput)) {
        Write-Output 'Install cancelled: no package was specified.'
        return
    }

    $RemainingArgs = @(ConvertTo-PipArgumentList $installInput)
    if ($RemainingArgs.Count -eq 0) {
        Write-Output 'Install cancelled: no package was specified.'
        return
    }
}

if (-not (Test-Path -LiteralPath $pythonExecutable -PathType Leaf) -or
    -not (Test-Path -LiteralPath $pythonConfiguration -PathType Leaf)) {
    if ($usingActiveEnvironment) {
        throw "The active Python virtual environment is missing or incomplete at '$pythonEnvironmentDirectory'."
    }
    throw "CodeNix's global Python environment is missing or incomplete at '$dataDirectory\venvs\global'. Run the CodeNix installer to repair it."
}

Invoke-Uv (@('pip', $Command, '--no-config', '--python', $pythonExecutable) + @($RemainingArgs))
