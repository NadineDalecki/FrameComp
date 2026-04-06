# FrameComp

Windows app for frame-accurate comparison of two videos (or one video + one live app window).

## Features

- Side-by-side or stacked comparison layout
- Shared bottom timeline with:
  - playhead dragging
  - clip bar dragging (with marker snap)
  - marker snap feedback while scrubbing
- Frame stepping and synchronized stepping
- Playback controls with speed selector (`25%` to `200%`)
- Marker support:
  - add marker (`M`)
  - select/delete marker (`Delete`)
  - marker snapping while scrubbing and clip moving
- Shared comments:
  - add comment at current timeline (`C`)
  - click comment to jump
  - inline delete icon per comment
- Live app window capture per side (`Use App Window` / `Back To Video`)
- Alignment mode for visual overlay checks (shown when live window mode is active)
- Built-in help:
  - top-right `?` button
  - `F1` shortcut
  - contextual tooltips
- Startup project picker with:
  - open selected project
  - new project (`+`)
  - rename project (pencil)
  - delete project (trash, with confirmation)

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
- `C`: add a shared timeline comment
- `F1`: open help overlay
- `Mouse wheel`: zoom the hovered video pane

## Live Window Source Notes

- Live window source mode is temporary and is not saved into the project JSON.
- Project video paths and markers are preserved while using live window mode.
- Timeline stepping, marker editing, and playback controls apply to video sources; live window slots are shown as a comparison view.

## Project Files

Comparison projects are stored as JSON files in the local `Projects` folder beside the app.

## Notes

- On fresh startup with no comments, the comments sidebar starts collapsed.
- App startup speed defaults to `100%`.
- Status line format is `Paused/Playing <speed>% speed | ...`.

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
