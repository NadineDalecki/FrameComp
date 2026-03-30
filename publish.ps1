$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "VideoFrameComparer\VideoFrameComparer.csproj"
$output = Join-Path $root "publish\win-x64"

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"
$env:NUGET_PACKAGES = Join-Path $root ".nuget\packages"

dotnet restore $project -r win-x64
if ($LASTEXITCODE -ne 0) {
  throw "dotnet restore failed"
}

dotnet publish $project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o $output

if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed"
}

Get-ChildItem -Path $output -File | ForEach-Object {
  $destination = Join-Path $root $_.Name
  if (Test-Path $destination) {
    Remove-Item -LiteralPath $destination -Force
  }
  Copy-Item -LiteralPath $_.FullName -Destination $destination
}

Get-ChildItem -Path $output -Directory | ForEach-Object {
  $destination = Join-Path $root $_.Name
  if (Test-Path $destination) {
    Remove-Item -LiteralPath $destination -Recurse -Force
  }
  Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse
}

Write-Host ""
Write-Host "Published to $output and copied runtime files to $root"
