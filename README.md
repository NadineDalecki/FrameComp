# FrameComp

Windows app for frame-accurate comparison of two videos (or one video + one live app window).

## Features

- Side-by-side or stacked comparison layout
- Top-bar icon actions:
  - use app window / back to video
  - overlay mode
  - save screenshot
  - export combined video
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
- Live app window capture per side
- Alignment mode for visual overlay checks (available when both sides have content)
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

For local/dev runs in this repository, use:

- `dist\FrameComp\VideoFrameComparer.exe`

Project JSON files are expected in the repository `Projects` folder.

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
- Combined video export default filename:
  - `side-by-side_<videoA>_<videoB>_<YYYY-MM-DD>.mp4` or `stacked_<videoA>_<videoB>_<YYYY-MM-DD>.mp4`
- Screenshot default filename:
  - `side-by-side_<videoA>_<videoB>_<YYYY-MM-DD>.png` or `stacked_<videoA>_<videoB>_<YYYY-MM-DD>.png`

## Run From Source

```powershell
cd C:\path\to\FrameComp
$env:DOTNET_CLI_HOME='C:\path\to\FrameComp\.dotnet-home'
$env:NUGET_PACKAGES='C:\path\to\FrameComp\.nuget\packages'
dotnet run --project .\VideoFrameComparer\VideoFrameComparer.csproj
```

## Publish Locally

```powershell
cd C:\path\to\FrameComp
.\publish.ps1
```

That creates the runnable app in:

- `C:\path\to\FrameComp\publish\win-x64`
- `C:\path\to\FrameComp\dist\FrameComp`

If you also want a launchable copy in the repo root, run:

```powershell
.\publish.ps1 -CopyRuntimeToRepoRoot
```

## Create A Release Zip

```powershell
cd C:\path\to\FrameComp
.\package-release.ps1
```

That creates a release archive in `C:\path\to\FrameComp\release`.
