# FrameComp

Simple Windows app for comparing two videos frame by frame.

## Features

- Drag one video into each pane
- View videos side by side or stacked
- Select the active video by clicking its pane
- Step the active video with arrow keys
- Hold `Shift` to step 20 frames
- Hold `Ctrl` with arrow keys to step both videos together
- Scrub both videos with the shared bottom timeline
- Play or pause both videos with `Space` or `Play Both`
- Change playback speed
- Zoom a video with the mouse wheel
- Add markers with `M`
- Select markers in the marker row and delete them with `Delete`
- Save comparison projects and reopen them from the startup picker

## Download And Run

The easiest way for normal users is a GitHub Release zip.

1. Go to the repository releases page.
2. Download the latest Windows zip.
3. Extract the zip to any folder.
4. Run `VideoFrameComparer.exe`.

No separate .NET install should be needed for the published `win-x64` build.

## Controls

- `Left / Right / Up / Down`: move the active video by 1 frame
- `Shift + Arrow`: move the active video by 20 frames
- `Ctrl + Arrow`: move both videos by 1 frame
- `Ctrl + Shift + Arrow`: move both videos by 20 frames
- `Space`: play or pause both videos
- `M`: add a marker on the active video
- `Delete`: remove the selected marker on the active video
- `Mouse wheel`: zoom the hovered video pane

## Project Files

Comparison projects are stored as JSON files in the local `Projects` folder beside the app.

## Run From Source

```powershell
cd D:\Programming\VidComp
$env:DOTNET_CLI_HOME='D:\Programming\VidComp\.dotnet-home'
$env:NUGET_PACKAGES='D:\Programming\VidComp\.nuget\packages'
dotnet run --project .\VideoFrameComparer\VideoFrameComparer.csproj
```

## Publish Locally

```powershell
cd D:\Programming\VidComp
.\publish.ps1
```

That creates the runnable app in:

- `D:\Programming\VidComp\publish\win-x64`
- and copies the same runtime files to `D:\Programming\VidComp`

## Create A Release Zip

```powershell
cd D:\Programming\VidComp
.\package-release.ps1
```

That creates a release archive in `D:\Programming\VidComp\release`.
