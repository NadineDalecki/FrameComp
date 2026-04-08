$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishOutput = Join-Path $root "publish\win-x64"
$releaseDir = Join-Path $root "release"
$zipPath = Join-Path $releaseDir "FrameComp-win-x64.zip"

if (-not (Test-Path $publishOutput)) {
  throw "Publish output not found at $publishOutput. Run publish.ps1 first."
}

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

if (Test-Path $zipPath) {
  Remove-Item -LiteralPath $zipPath -Force
}

# Ensure release zip always contains an empty Projects folder for first run.
$projectsDir = Join-Path $publishOutput "Projects"
New-Item -ItemType Directory -Force -Path $projectsDir | Out-Null
$projectsKeepFile = Join-Path $projectsDir "KEEP_PROJECTS_FOLDER.txt"
Set-Content -LiteralPath $projectsKeepFile -Value "This file keeps the Projects folder in release archives." -NoNewline

Compress-Archive -Path (Join-Path $publishOutput "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host ""
Write-Host "Created release archive at $zipPath"
