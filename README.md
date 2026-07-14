# HandUMI Quest App v0.2.1

HandUMI Quest App is a reconstruction of YubiQuestApp based on its `.apk`.

## Important limitation

The APK was built with Unity IL2CPP. The original C# method bodies, comments, project settings, prefabs, and version-control history are **not** stored in recoverable source form. The files under `reconstructed_unity/` are an independent, best-effort behavioral reconstruction—not the original AIRoA source.

Confidence labels used in the source comments:

- **CONFIRMED** — name, field, method, constant, or protocol element recovered directly from the APK.
- **STRONGLY INFERRED** — behavior visible in native code and/or exactly matched by the public ROS bridge.
- **APPROXIMATE** — implementation chosen to reproduce observed behavior where original C# syntax was erased.

Start with `HandUMIQuestApp_APK_Analysis.md` and `reconstructed_unity/README.md`.

## Compatibility scene

The checked-in scene at
`Assets/HandUMIQuestApp/Scenes/HandUMIQuestCompatibility.unity` contains the minimal
reconstructed runtime: `OVRManager`, `OVRCameraRig`, `PoseSender`,
`TimeSyncServer`, `QuestRefresh120`, and `SafeTrackingOrigin`. Regenerate it
repeatably from Unity with:

```text
Tools > HandUMI Quest > Rebuild Compatibility Scene
```

## Automated tests

The tests under `Assets/Tests/EditMode` protect the legacy HandUMI wire
contract: JSON field names and framing, TCP/UDP ports, time-sync byte layout,
the side-by-side Android package/version identity, and scene wiring. PlayMode tests
exercise TCP reconnect, UDP request handling, and socket release on teardown.

Run them with Unity `6000.0.76f1` and the Android target selected:

```bash
/path/to/Unity \
  -batchmode -nographics -buildTarget Android \
  -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -testResults TestResults.xml \
  -logFile TestRunner.log
```

Repeat with `-testPlatform PlayMode` and different result/log filenames.

Do not add `-quit`; the Unity Test Framework exits the editor after the run.
Meta XR SDK 74 has Linux-editor code that only compiles here when Android is
the active target.

## Build the compatibility APK

Prerequisites on Linux:

- Unity `6000.0.76f1` with Android Build Support and IL2CPP.
- Android SDK, NDK (the recovered toolchain uses `27.2.12479018`), JDK 17,
  and SDK CMake `3.22.1`.
- `ANDROID_SDK_ROOT` (or `ANDROID_HOME`), `ANDROID_NDK_ROOT` when the NDK is
  outside the SDK, and `JAVA_HOME` when Java is outside a standard location.

Install the required Android CMake package when necessary:

```bash
"$ANDROID_SDK_ROOT/cmdline-tools/latest/bin/sdkmanager" \
  --install "cmake;3.22.1"
```

Build from the command line:

```bash
UNITY=/path/to/Unity
HANDUMI_APK_OUTPUT="$PWD/Builds/Android/handumi-quest-app-v0.2.1.apk" \
  "$UNITY" \
  -batchmode -nographics -buildTarget Android \
  -projectPath "$PWD" \
  -executeMethod HandUMICompatibilityBuild.BuildAndroid \
  -logFile Builds/Android/build.log \
  -quit
```

The build writes the APK plus `.manifest.json` and `.sha256` evidence files.
These outputs are intentionally gitignored.

The APK is labeled `HandUMIQuestApp-v0.2.1` and uses package ID
`com.handumi.questapp`. It retains the compatibility ports (TCP 65432 and UDP
42000); force-stop any other compatible app before starting it.

## Validation boundary

The reconstructed scene, automated tests, and local APK build do not establish
behavioral parity on Quest hardware. Follow
`docs/quest-hardware-validation.md` before treating this APK as a replacement
for data collection.

## References and Acknowledgments

- UMI: Chi et al., "Universal Manipulation Interface: In-The-Wild Robot Teaching Without In-The-Wild Robots," RSS 2024. Project · Paper
- YUBI: Ohkawa et al., "YUBI: Yielding Universal Bidigital Interface for Bimanual Dexterous Manipulation at Scale," 2026. Project · Paper · Software
- Meta Quest support uses YubiQuestApp and adapts the yubi-sw protocol and coordinate conversion. PICO support uses XRoboToolkit.
- Core software: LeRobot, PyRoki, Viser, Rerun, and MuJoCo.
- Robot assets: Almond Axol and AgileX Piper ROS, both MIT.

HandUMI is not affiliated with or endorsed by Meta, PICO, AgileX, AIRoA/YUBI, Almond, or Hugging Face. All trademarks belong to their respective owners.
