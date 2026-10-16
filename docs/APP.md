# Windows and Quest 3 application

## Build from source

Use Unity 6000.3.10f1. Open `unity/XRSuperspace` and preserve the pinned package
manifest/lock. Open `Assets/XRSuperspace/Scenes/SuperspaceDemo.unity`.
Install Windows Build Support for Windows, or Android Build Support with
SDK/NDK/OpenJDK for Quest 3. Dependencies are acquired separately; engine/package
caches and signing credentials are not distributed.

Run one Unity process per project. Editor tests:

```sh
Unity -batchmode -nographics -projectPath unity/XRSuperspace -runTests -testPlatform EditMode -testResults results/editmode.xml -logFile results/editmode.log
```

Replace `Unity` with your installed editor executable. Create the `results`
directory first. Build outputs must be under this checkout's `builds` directory:

```sh
Unity -batchmode -quit -projectPath unity/XRSuperspace -buildTarget Win64 -executeMethod XRSuperspace.Editor.WindowsDemoBuild.Build -aperitrace-windows-output ABSOLUTE_PATH_IN_BUILDS/AperiTrace.exe -logFile results/windows-build.log
Unity -batchmode -quit -projectPath unity/XRSuperspace -buildTarget Android -executeMethod XRSuperspace.Editor.QuestDemoBuild.Build -superspace-apk-output ABSOLUTE_PATH_IN_BUILDS/AperiTrace-quest3.apk -logFile results/quest-build.log
```

Windows builds are portable: keep the executable, data directory and Unity
runtime files together. Quest APKs produced by this build path are development
builds, not store-certified distributions. Do not commit binaries or signing keys.

## Start and explore

Start with the guided tour or open the workspace for free exploration.
Physical structure, internal selection window and finite diffraction share the
same lattice identities. Move the cyan control to change the internal offset.
Grey candidates are outside; selected points are colored. Selection is not a
right/wrong classification. A white focus identifies the same site across views.

## Windows controls

- Wheel: zoom; middle drag: pan; right drag: rotate Structure.
- G: adaptive grids in all views. Home: reset views. I: inspector.
- F: free exploration. N: next guide step. E: toggle edges. 0: reset offset.
- Arrow keys after clicking a diagram: adjust offset.
- Ctrl+S: save session. Ctrl+O: saved sessions.
- Ctrl+Shift+S: screenshot and linked state. P: pause. Esc: menu.
- Tab/Shift+Tab: focus navigation; Enter: activate.

View controls accept exact camera center and width/height; scientific precision
controls edit the actual offset instead. Inspector separates model, point and
peak quantities. Color/filter controls offer palettes, custom gradients, range,
linear/sqrt/log mapping and intensity/reciprocal-radius visibility filters.
Display filters do not remove scientific samples from numerical exports.

## Quest controls

Left controller Menu opens/closes the app menu. Aim and trigger select actions,
points or peaks; hold the cyan knob in free exploration to move it.
For hands, show open hands, aim and pinch/release to select. Pinch-hold the knob
to drag. Tracking loss cancels the grab and requires release before reselecting.
System gestures remain reserved for Quest. Use the visible Menu button when
controllers are unavailable. Focused-view thumbsticks: right zoom, left pan.

Menu pages expose guided examples, exact inspection, grid/view tools, diffraction
colors and filters, sessions, screenshots, numerical export and guarded quit.
Numeric edits use an Apply action. Pause and OS focus loss cancel manipulation;
Resume never moves the model automatically.

## Local files and accessibility

Sessions, screenshots and numerical exports are stored under Unity's persistent
application-data directory. Sessions can open the folder. App screenshots are
monoscopic app-local PNGs with scientific sidecars, not headset stereo or
passthrough captures. Save/load restores scientific state; temporary pan/zoom
and display preferences are separate from scientific parameters.

Settings include text sizes, high contrast, a blue/amber palette and reduced
motion. Screen-reader integration, sustained headset performance, thermal
behavior and commercial/store certification are not established. All UI is
English. The app does not provide cloud synchronization or telemetry.

Python is the event-resolved reference; Unity presents sampled states.
Diffraction is calculated from the finite model, not measured X-ray data.
Dimensionless coordinates and model reciprocal units are not material calibration.
