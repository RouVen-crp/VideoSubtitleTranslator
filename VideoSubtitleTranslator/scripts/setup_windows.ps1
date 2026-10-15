[CmdletBinding()]
param(
    [string]$PythonCommand = "",
    [switch]$SkipToolInstall,
    [switch]$RecreateVenv
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectDir = Split-Path -Parent $PSScriptRoot
$pythonScriptsDir = Join-Path $projectDir "PythonScripts"
$venvDir = Join-Path $pythonScriptsDir "venv"
$venvPython = Join-Path $venvDir "Scripts\python.exe"
$requirementsFile = Join-Path $pythonScriptsDir "requirements.txt"

if ([string]::IsNullOrWhiteSpace($PythonCommand)) {
    $PythonCommand = if (Get-Command "py" -ErrorAction SilentlyContinue) { "py" } else { "python" }
}

function Update-ProcessPath {
    $machinePath = [System.Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [System.Environment]::GetEnvironmentVariable("Path", "User")
    $pathSegments = @($machinePath, $userPath, $env:Path) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $env:Path = [string]::Join(";", $pathSegments)
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,
        [Parameter(Mandatory = $true)]
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
    Invoke-CheckedCommand -Command "winget" -Arguments @(
        "install",
        "--id", $PackageId,
        "--exact",
        "--accept-package-agreements",
        "--accept-source-agreements"
    )
}

Write-Host "Setting up VideoSubtitleTranslator for Windows..."

# Long-running terminals inherit an old PATH. Refresh it so rerunning the script
# can see packages that winget installed during an earlier invocation.
Update-ProcessPath

Install-WingetPackageIfMissing -CommandName "yt-dlp" -PackageId "yt-dlp.yt-dlp"
Update-ProcessPath
Install-WingetPackageIfMissing -CommandName "ffmpeg" -PackageId "Gyan.FFmpeg"
Update-ProcessPath

if (-not (Get-Command $PythonCommand -ErrorAction SilentlyContinue)) {
    throw "Python command '$PythonCommand' was not found. Install Python 3 and run this script again."
}

Write-Host "[python] Checking $PythonCommand..."
Invoke-CheckedCommand -Command $PythonCommand -Arguments @("--version")

if ($RecreateVenv -and (Test-Path -LiteralPath $venvDir)) {
    $resolvedVenvDir = [System.IO.Path]::GetFullPath($venvDir)
    $resolvedPythonScriptsDir = [System.IO.Path]::GetFullPath($pythonScriptsDir) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedVenvDir.StartsWith($resolvedPythonScriptsDir, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a virtual environment outside $pythonScriptsDir."
    }

    Write-Host "[python] Removing the existing generated virtual environment..."
    Remove-Item -LiteralPath $resolvedVenvDir -Recurse -Force
}

if (-not (Test-Path -LiteralPath $venvPython -PathType Leaf)) {
    Write-Host "[python] Creating virtual environment at $venvDir..."
    Invoke-CheckedCommand -Command $PythonCommand -Arguments @("-m", "venv", $venvDir)
}
else {
    Write-Host "[ok] Python virtual environment already exists."
}

Write-Host "[python] Installing Whisper dependencies..."
Invoke-CheckedCommand -Command $venvPython -Arguments @("-m", "pip", "install", "--upgrade", "pip")
Invoke-CheckedCommand -Command $venvPython -Arguments @("-m", "pip", "install", "-r", $requirementsFile)

Write-Host "[python] Verifying Whisper imports..."
Invoke-CheckedCommand -Command $venvPython -Arguments @(
    "-c",
    "import whisper, srt, torch; print('Whisper dependencies are ready (torch ' + torch.__version__ + ').')"
)

Write-Host ""
Write-Host "Setup completed."
Write-Host "If yt-dlp or ffmpeg was installed during this run, reopen PowerShell before starting the app so PATH changes take effect."
Write-Host "Next: copy .env.example to .env, set the required environment variable, then run dotnet from the repository root."
