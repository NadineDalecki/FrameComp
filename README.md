# FrameComp

Simple Windows app for comparing two videos frame by frame.

## Features

- Drag one video into each pane
- View videos side by side or stacked
- Select the active video by clicking its pane
- Temporarily switch a slot to a live app window source with `Use App Window`
- Return a live window slot back to its video with `Back To Video`
- Step the active video with arrow keys
- Hold `Shift` to step 20 frames
- Hold `Ctrl` with arrow keys to step both videos together
- Scrub both videos with the shared bottom timeline
- Play or pause both videos with `Space` or `Play Both`
- Toggle play/pause globally with `Page Down` (works while app is in background)
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
- `Page Down`: global play/pause toggle (background hotkey)
- `M`: add a marker on the active video
- `Delete`: remove the selected marker on the active video
- `Mouse wheel`: zoom the hovered video pane

## Live Window Source Notes

- Live window source mode is temporary and is not saved into the project JSON.
- Project video paths and markers are preserved while using live window mode.
- Timeline stepping, marker editing, and playback controls apply to video sources; live window slots are shown as a comparison view.

## Project Files

Comparison projects are stored as JSON files in the local `Projects` folder beside the app.

## Run From Source

```powershell
cd D:\Programming\FrameComp
$env:DOTNET_CLI_HOME='D:\Programming\FrameComp\.dotnet-home'
$env:NUGET_PACKAGES='D:\Programming\FrameComp\.nuget\packages'
dotnet run --project .\VideoFrameComparer\VideoFrameComparer.csproj
```

## Publish Locally

```powershell
cd D:\Programming\FrameComp
.\publish.ps1
```

That creates the runnable app in:

- `D:\Programming\FrameComp\publish\win-x64`
- and copies the same runtime files to `D:\Programming\FrameComp`

## Create A Release Zip

```powershell
cd D:\Programming\FrameComp
.\package-release.ps1
```

That creates a release archive in `D:\Programming\FrameComp\release`.
