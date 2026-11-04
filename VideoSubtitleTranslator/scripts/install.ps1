[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$InstallDir = "",
    [switch]$NoPathUpdate
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptsDir = $PSScriptRoot
$projectDir = Split-Path -Parent $scriptsDir
$repoRoot = Split-Path -Parent $projectDir
$solutionPath = Join-Path $repoRoot "VideoSubtitleTranslator.sln"

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = Join-Path $env:LOCALAPPDATA "Programs\zimu"
}

if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    throw "dotnet was not found. Install the .NET 10 SDK first."
}

Write-Host "[build] Building $Configuration configuration..."
& dotnet build $solutionPath -c $Configuration -m:1 -nodeReuse:false --nologo -v minimal
if ($LASTEXITCODE -ne 0) {
    throw "Build failed (dotnet build exited with $LASTEXITCODE)."
}

$outputDir = Join-Path $projectDir "bin\$Configuration\net10.0"
$exePath = Join-Path $outputDir "zimu.exe"
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Build output is missing: $exePath"
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

# .cmd files are read by cmd.exe using the ANSI code page, so write them that way
# to keep non-ASCII installation paths working.
$ansiEncoding = [System.Text.Encoding]::GetEncoding(
    [System.Globalization.CultureInfo]::CurrentCulture.TextInfo.ANSICodePage)

function Write-Launcher {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Target
    )

    $path = Join-Path $InstallDir $Name
    $content = "@echo off`r`n`"$Target`" %*`r`n"
    [System.IO.File]::WriteAllText($path, $content, $ansiEncoding)
    Write-Host "[ok] $path"
}

Write-Launcher -Name "zimu.cmd" -Target $exePath
Write-Launcher -Name "zm.cmd" -Target $exePath

if (-not $NoPathUpdate) {
    $userPath = [System.Environment]::GetEnvironmentVariable("Path", "User")
    $segments = @($userPath -split ";" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $alreadyListed = $false
    foreach ($segment in $segments) {
        if ([string]::Equals($segment.TrimEnd("\"), $InstallDir.TrimEnd("\"), [System.StringComparison]::OrdinalIgnoreCase)) {
            $alreadyListed = $true
            break
        }
    }

    if ($alreadyListed) {
        Write-Host "[ok] $InstallDir is already on the user PATH."
    }
    else {
        $newPath = (@($segments) + $InstallDir) -join ";"
        [System.Environment]::SetEnvironmentVariable("Path", $newPath, "User")
        Write-Host "[ok] Added $InstallDir to the user PATH. Open a new terminal to use it."
    }

    $env:Path = "$InstallDir;$env:Path"
}

# Prove the launchers work instead of only claiming they were written.
foreach ($launcher in @("zimu.cmd", "zm.cmd")) {
    $launcherPath = Join-Path $InstallDir $launcher
    $null = & $launcherPath --help 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "$launcher does not start correctly (exit code $LASTEXITCODE)."
    }
    Write-Host "[ok] $launcher --help exited with 0."
}

Write-Host ""
Write-Host "Installed $InstallDir."
Write-Host "Next steps:"
Write-Host "  1. Open a new terminal so PATH changes take effect."
Write-Host "  2. Copy VideoSubtitleTranslator\.env.example to VideoSubtitleTranslator\.env and set DEEPSEEK_API_KEY."
Write-Host "  3. Run: zimu doctor translate"
Write-Host "  4. Run: zimu translate <video URL or local path>"
