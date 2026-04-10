$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$distOutput = Join-Path $root "dist\\FrameComp"
$releaseDir = Join-Path $root "release"
$zipPath = Join-Path $releaseDir "FrameComp-win-x64.zip"

if (-not (Test-Path $distOutput)) {
  throw "Dist output not found at $distOutput. Run publish.ps1 first."
}

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

if (Test-Path $zipPath) {
  Remove-Item -LiteralPath $zipPath -Force
}

# Ensure release zip always contains an empty Projects folder for first run.
$projectsDir = Join-Path $distOutput "Projects"
New-Item -ItemType Directory -Force -Path $projectsDir | Out-Null
$projectsKeepFile = Join-Path $projectsDir "KEEP_PROJECTS_FOLDER.txt"
Set-Content -LiteralPath $projectsKeepFile -Value "This file keeps the Projects folder in release archives." -NoNewline

# Package as a single top-level FrameComp folder (avoids dumping hundreds of DLLs into the unzip target root).
Compress-Archive -LiteralPath $distOutput -DestinationPath $zipPath -Force

Write-Host ""
Write-Host "Created release archive at $zipPath"
