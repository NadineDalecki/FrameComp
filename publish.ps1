$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "VideoFrameComparer\VideoFrameComparer.csproj"
$output = Join-Path $root "publish\win-x64"
$distDir = Join-Path $root "dist\\FrameComp"

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"
$env:NUGET_PACKAGES = Join-Path $root ".nuget\packages"

dotnet restore $project -r win-x64 -p:SelfContained=true
if ($LASTEXITCODE -ne 0) {
  throw "dotnet restore failed"
}

if (Test-Path $output) {
  Remove-Item -LiteralPath $output -Recurse -Force
}

dotnet publish $project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  --no-restore `
  -p:PublishSingleFile=false `
  -o $output

if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed"
}

# LibVLCSharp expects libvlc.dll and libvlccore.dll to exist under libvlc\win-x64 (and win-x86).
# The VideoLAN.LibVLC.Windows package keeps the DLLs under build\x64/build\x86, and publish
# may not copy them into our libvlc folder. Inject them explicitly.
$libVlcPackageRoot = Join-Path $root ".nuget\\packages\\videolan.libvlc.windows"
if (Test-Path $libVlcPackageRoot) {
  $libVlcVersionDir = Get-ChildItem -LiteralPath $libVlcPackageRoot -Directory |
    Sort-Object { try { [version]$_.Name } catch { [version]"0.0.0.0" } } -Descending |
    Select-Object -First 1
  if ($libVlcVersionDir) {
    $x64Src = Join-Path $libVlcVersionDir.FullName "build\\x64"
    $x86Src = Join-Path $libVlcVersionDir.FullName "build\\x86"
    $x64Dst = Join-Path $output "libvlc\\win-x64"
    $x86Dst = Join-Path $output "libvlc\\win-x86"
    New-Item -ItemType Directory -Force -Path $x64Dst | Out-Null
    New-Item -ItemType Directory -Force -Path $x86Dst | Out-Null
    foreach ($name in @("libvlc.dll", "libvlccore.dll")) {
      $src64 = Join-Path $x64Src $name
      $src86 = Join-Path $x86Src $name
      if (Test-Path $src64) { Copy-Item -LiteralPath $src64 -Destination (Join-Path $x64Dst $name) -Force }
      if (Test-Path $src86) { Copy-Item -LiteralPath $src86 -Destination (Join-Path $x86Dst $name) -Force }
    }

    # VLC modules live under plugins/. If publish didn't include them, copy from the package.
    foreach ($arch in @(@{ Src = $x64Src; Dst = $x64Dst }, @{ Src = $x86Src; Dst = $x86Dst })) {
      $pluginsSrc = Join-Path $arch.Src "plugins"
      $pluginsDst = Join-Path $arch.Dst "plugins"
      if (Test-Path $pluginsSrc) {
        New-Item -ItemType Directory -Force -Path $pluginsDst | Out-Null
        Copy-Item -Path (Join-Path $pluginsSrc "*") -Destination $pluginsDst -Recurse -Force
      }
    }
  }
}

# Ensure end users get a ready-to-use projects folder in published output.
$publishProjectsDir = Join-Path $output "Projects"
New-Item -ItemType Directory -Force -Path $publishProjectsDir | Out-Null
$projectsKeepFile = Join-Path $publishProjectsDir "KEEP_PROJECTS_FOLDER.txt"
Set-Content -LiteralPath $projectsKeepFile -Value "This file keeps the Projects folder in release archives." -NoNewline

if (Test-Path $distDir) {
  Remove-Item -LiteralPath $distDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
Copy-Item -Path (Join-Path $output "*") -Destination $distDir -Recurse -Force

# Keep root clean: create a root shortcut that launches dist\FrameComp\VideoFrameComparer.exe.
$rootExe = Join-Path $root "VideoFrameComparer.exe"
$rootShortcut = Join-Path $root "VideoFrameComparer.lnk"
$distExe = Join-Path $distDir "VideoFrameComparer.exe"
if (Test-Path -LiteralPath $rootExe) {
  Remove-Item -LiteralPath $rootExe -Force
}
if (Test-Path -LiteralPath $rootShortcut) {
  Remove-Item -LiteralPath $rootShortcut -Force
}
$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut($rootShortcut)
$shortcut.TargetPath = $distExe
$shortcut.WorkingDirectory = $distDir
$shortcut.IconLocation = "$distExe,0"
$shortcut.Save()

Write-Host ""
Write-Host "Published to $output and copied runtime files to $distDir"
Write-Host "Created root launcher shortcut at $rootShortcut"
