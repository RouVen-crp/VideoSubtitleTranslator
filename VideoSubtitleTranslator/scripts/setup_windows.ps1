[CmdletBinding()]
param(
    [string]$PythonCommand = "python",
    [switch]$SkipToolInstall
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectDir = Split-Path -Parent $PSScriptRoot
$pythonScriptsDir = Join-Path $projectDir "PythonScripts"
$venvDir = Join-Path $pythonScriptsDir "venv"
$venvPython = Join-Path $venvDir "Scripts\python.exe"
$requirementsFile = Join-Path $pythonScriptsDir "requirements.txt"

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command"
    }
}

function Install-WingetPackageIfMissing {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandName,
        [Parameter(Mandatory = $true)]
        [string]$PackageId
    )

    if (Get-Command $CommandName -ErrorAction SilentlyContinue) {
        Write-Host "[ok] $CommandName is already available."
        return
    }

    if ($SkipToolInstall) {
        throw "$CommandName is not available and -SkipToolInstall was specified."
    }

    if (-not (Get-Command "winget" -ErrorAction SilentlyContinue)) {
        throw "winget is required to install $CommandName automatically. Install App Installer first, or install $CommandName manually."
    }

    Write-Host "[install] Installing $CommandName with winget..."
    Invoke-CheckedCommand "winget" @(
        "install",
        "--id", $PackageId,
        "--exact",
        "--accept-package-agreements",
        "--accept-source-agreements"
    )
}

Write-Host "Setting up VideoSubtitleTranslator for Windows..."

Install-WingetPackageIfMissing -CommandName "yt-dlp" -PackageId "yt-dlp.yt-dlp"
Install-WingetPackageIfMissing -CommandName "ffmpeg" -PackageId "Gyan.FFmpeg"

if (-not (Get-Command $PythonCommand -ErrorAction SilentlyContinue)) {
    throw "Python command '$PythonCommand' was not found. Install Python 3 and run this script again."
}

Write-Host "[python] Checking $PythonCommand..."
Invoke-CheckedCommand $PythonCommand @("--version")

if (-not (Test-Path -LiteralPath $venvPython -PathType Leaf)) {
    Write-Host "[python] Creating virtual environment at $venvDir..."
    Invoke-CheckedCommand $PythonCommand @("-m", "venv", $venvDir)
}
else {
    Write-Host "[ok] Python virtual environment already exists."
}

Write-Host "[python] Installing Whisper dependencies..."
Invoke-CheckedCommand $venvPython @("-m", "pip", "install", "--upgrade", "pip")
Invoke-CheckedCommand $venvPython @("-m", "pip", "install", "-r", $requirementsFile)

Write-Host ""
Write-Host "Setup completed."
Write-Host "If yt-dlp or ffmpeg was installed during this run, reopen PowerShell before starting the app so PATH changes take effect."
Write-Host "Next: copy .env.example to .env, set the required environment variable, then run dotnet from the repository root."
