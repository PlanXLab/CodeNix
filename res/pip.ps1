<#
.SYNOPSIS
  Run uv pip commands against CodeNix's global Python environment.
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

function Invoke-Uv {
    param([string[]]$Arguments)

    & $script:UvExecutable @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "uv exited with code ${exitCode}: uv $($Arguments -join ' ')"
    }
}

$dataDirectory = Get-PortableDataDirectory
$script:UvExecutable = Join-Path $dataDirectory 'lib\uv\uv.exe'
$pythonExecutable = Join-Path $dataDirectory 'venvs\global\Scripts\python.exe'
$pythonConfiguration = Join-Path $dataDirectory 'venvs\global\pyvenv.cfg'

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

if (-not (Test-Path -LiteralPath $pythonExecutable -PathType Leaf) -or
    -not (Test-Path -LiteralPath $pythonConfiguration -PathType Leaf)) {
    throw "CodeNix's global Python environment is missing or incomplete at '$dataDirectory\venvs\global'. Run the CodeNix installer to repair it."
}

Invoke-Uv (@('pip', $Command, '--no-config', '--python', $pythonExecutable) + @($RemainingArgs))
