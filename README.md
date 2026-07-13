# YubiQuestApp v0.1.0 — APK recovery bundle

This bundle was produced by static analysis of `yubi-quest-app-v0.1.0.apk`.

## Important limitation

The APK was built with Unity IL2CPP. The original C# method bodies, comments, project settings, prefabs, and version-control history are **not** stored in recoverable source form. The files under `reconstructed_unity/` are an independent, best-effort behavioral reconstruction—not the original AIRoA source.

Confidence labels used in the source comments:

- **CONFIRMED** — name, field, method, constant, or protocol element recovered directly from the APK.
- **STRONGLY INFERRED** — behavior visible in native code and/or exactly matched by the public ROS bridge.
- **APPROXIMATE** — implementation chosen to reproduce observed behavior where original C# syntax was erased.

Start with `YubiQuestApp_APK_Analysis.md` and `reconstructed_unity/README.md`.

## Compatibility scene

The checked-in scene at
`Assets/YubiQuestApp/Scenes/YubiQuestCompatibility.unity` contains the minimal
reconstructed runtime: `OVRManager`, `OVRCameraRig`, `PoseSender`,
`TimeSyncServer`, `QuestRefresh120`, and `SafeTrackingOrigin`. Regenerate it
repeatably from Unity with:

```text
Tools > YubiQuest > Rebuild Compatibility Scene
```

## Automated tests

The tests under `Assets/Tests/EditMode` protect the legacy HandUMI/YUBI wire
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
YUBI_APK_OUTPUT="$PWD/Builds/Android/handumi-quest-app-v0.1.0.apk" \
  "$UNITY" \
  -batchmode -nographics -buildTarget Android \
  -projectPath "$PWD" \
  -executeMethod YubiCompatibilityBuild.BuildAndroid \
  -logFile Builds/Android/build.log \
  -quit
```

The build writes the APK plus `.manifest.json` and `.sha256` evidence files.
These outputs are intentionally gitignored.

The reconstructed APK is labeled `HandUMIQuestApp-v0.1.0` and uses package ID
`com.handumi.questapp`, so it can remain installed beside the released YUBI
app. The two apps retain the same compatibility ports (TCP 65432 and UDP
42000), so force-stop one before starting the other.

## Validation boundary

The reconstructed scene, automated tests, and local APK build do not establish
behavioral parity on Quest hardware. Follow
`docs/quest-hardware-validation.md` before treating this APK as a replacement
for data collection.
