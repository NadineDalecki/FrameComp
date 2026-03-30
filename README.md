# VidComp

Simple Windows app for comparing two videos frame by frame.

## What It Does

- Load two videos by dragging them onto the left and right panes
- Show videos side by side or stacked
- Click a pane to make it the active video
- Step the active video with arrow keys
- Hold `Shift` to step 20 frames at a time
- Hold `Ctrl` with arrow keys to step both videos together
- Use the shared bottom timeline to scrub both videos together
- Play both videos in sync with `Space` or the `Play Both` button
- Adjust playback speed with the speed selector
- Zoom the active video with the mouse wheel
- Add markers with `M`
- Select markers in the marker row and remove them with `Delete`
- Save project state, including loaded videos and markers
- Reopen existing projects from the startup project picker

## Controls

- `Left / Right / Up / Down`: move the active video by 1 frame
- `Shift + Arrow`: move the active video by 20 frames
- `Ctrl + Arrow`: move both videos by 1 frame
- `Ctrl + Shift + Arrow`: move both videos by 20 frames
- `Space`: play or pause both videos
- `M`: add a marker on the active video
- `Delete`: remove the selected marker on the active video
- `Mouse wheel`: zoom the hovered video pane

## Project Layout

- Main app source: [VideoFrameComparer](D:\Programming\VidComp\VideoFrameComparer)
- Startup project files: [Projects](D:\Programming\VidComp\Projects)
- Publish script: [publish.ps1](D:\Programming\VidComp\publish.ps1)

Project files are stored as JSON in `D:\Programming\VidComp\Projects`.

## Run From Source

```powershell
cd D:\Programming\VidComp
$env:DOTNET_CLI_HOME='D:\Programming\VidComp\.dotnet-home'
$env:NUGET_PACKAGES='D:\Programming\VidComp\.nuget\packages'
dotnet run --project .\VideoFrameComparer\VideoFrameComparer.csproj
```

## Publish EXE

```powershell
cd D:\Programming\VidComp
.\publish.ps1
```

The published app is copied to the root `D:\Programming\VidComp` folder and also kept in `D:\Programming\VidComp\publish\win-x64`.
