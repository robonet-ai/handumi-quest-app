# YubiQuestApp v0.1.0 APK analysis

## Bottom line

The uploaded file is the AIRoA **YubiQuestApp v0.1.0** Quest client. It is a Unity **IL2CPP** build, so the original C# files are not present as readable assemblies. Static analysis nevertheless recovers the exact app-script filenames, custom class and member names, serialized data schema, default ports, major runtime behavior, build/toolchain metadata, and the complete wire protocol.

The closest recoverable source-level representation is included under `reconstructed_unity/`. It is explicitly a reconstruction, not the original source repository.

## File identity

| Property | Value |
|---|---|
| APK | `yubi-quest-app-v0.1.0.apk` |
| Size | approximately 69 MB |
| SHA-256 | `1af91c35e0476b629d85f87bed33667a37a37b08ecba934596f8420069ab7174` |
| App label | `YubiQuestApp-v0.1.0` |
| Package ID | `com.UnityTechnologies.com.unity.template.urpblank` |
| Version | `0.1.0` (`versionCode` 1) |
| Main activity | `com.unity3d.player.UnityPlayerGameActivity` |
| Minimum / target SDK | 32 / 32 |
| Supported devices | Quest 2, Quest Pro, Quest 3, Quest 3S |
| Permissions | Internet; Oculus hand tracking |

The package ID is still Unity's URP blank-template identifier rather than a custom AIRoA application ID.

## Signing

The APK uses Android APK Signature Scheme v2 and is signed with a self-signed Android debug certificate:

- Subject/issuer: `C=US,O=Android,CN=Android Debug`
- Certificate SHA-256: `46:36:DF:3B:6D:94:2D:99:52:F5:43:62:38:81:10:CB:11:97:E9:88:88:4D:BE:E0:11:12:86:B7:2C:A3:17:E0`
- Validity: 2026-05-30 through 2056-05-22

No supported version-control metadata was embedded: `META-INF/version-control-info.textproto` reports `NO_SUPPORTED_VCS_FOUND`.

## Unity build

- Unity `6000.0.76f1`, branch `6000.0/staging`, changeset `6f7f9e1c9e8a`
- Scripting backend: IL2CPP
- Android / Gradle build
- Universal Render Pipeline
- Meta XR and OpenXR

Key package versions:

- `com.meta.xr.sdk.core@74.0.1`
- `com.meta.xr.sdk.interaction.ovr@74.0.2`
- `com.unity.inputsystem@1.14.1`
- `com.unity.render-pipelines.universal@17.0.4`
- `com.unity.xr.openxr@1.15.0`

The build contains `libil2cpp.so` and `global-metadata.dat`, not a recoverable `Assembly-CSharp.dll` containing C# IL.

## Exact app source filenames recovered

The following paths are embedded in IL2CPP metadata:

- `Assets/PoseSender.cs`
- `Assets/RequestFPS.cs`
- `Assets/SafeTrackingOrigin.cs`
- `Assets/TimeSyncServer.cs`
- `Assets/TutorialInfo/Scripts/Readme.cs`

Only filenames survive; original text, comments, formatting, and version history do not.

## Custom app types recovered

### `PoseData`

The exact serialized fields are:

- HMD pose: `hmdPosition`, `hmdRotation`
- Controller poses: `leftControllerPosition`, `leftControllerRotation`, `rightControllerPosition`, `rightControllerRotation`
- Pressed: `leftTriggerPressed`, `rightTriggerPressed`, `leftGripPressed`, `rightGripPressed`, `buttonAPressed`, `buttonBPressed`, `buttonXPressed`, `buttonYPressed`
- Joysticks: `leftJoystick`, `rightJoystick`
- Thumbsticks: `leftThumbstickClick`, `rightThumbstickClick`, `leftThumbstickTouched`, `rightThumbstickTouched`
- Capacitive touch: `buttonATouched`, `buttonBTouched`, `buttonXTouched`, `buttonYTouched`, `leftIndexTriggerTouched`, `rightIndexTriggerTouched`
- Timing: `ovrTimeNs`, `unityTimeNs`, `deltaTime`
- Tracking: `leftTracked`, `rightTracked`, `leftValid`, `rightValid`
- Battery: `leftBattPct`, `rightBattPct`, `hmdBattPct`, `hmdCharging`
- Menu buttons: `startPressed`, `backPressed`

### `BatteryUtils`

Recovered methods:

- `GetControllerBattery(which)`
- `GetHmdBatteryPercent()`
- `IsHmdOnExternalPower()`

The native HMD helper uses `SystemInfo.batteryLevel`, returns a byte percentage, and uses `255` when unavailable. External power is true for Unity's Charging or Full battery states.

### `PoseSender`

Recovered fields:

- `serverPort`
- `ovrCameraRig`
- `vrText`
- `server`
- `clients`
- `listenThread`
- `isRunning`

Recovered methods:

- `Start()`
- `StartServer()`
- `Update()`
- `OnApplicationQuit()`
- `GetLocalIPAddress()`
- compiler-generated TCP accept-loop lambda

The default `serverPort` in the constructor and serialized scene is `65432`.

Observed behavior:

1. Starts a `TcpListener` on all interfaces.
2. Accepts clients on a background thread.
3. Builds one `PoseData` object per Unity update.
4. Serializes with `JsonUtility.ToJson`.
5. Appends a newline, encodes UTF-8, and sends to every connected client.
6. Removes and closes failed clients.
7. Stops the listener and clients at application shutdown.

Recovered log strings include:

- `Pose server started on {0}:{1}`
- `Server running on {0}:{1}`
- `Client connected`
- `Pose server stopped`

### `TimeSyncServer`

Recovered fields and methods:

- `syncPort`, `sock`
- `Start()`, `OnDestroy()`, `OnRequest(IAsyncResult ar)`, `QuestTimeNs()`

The default port is `42000`. The native method body confirms a `UdpClient`, asynchronous receive, a 9-byte request, a 17-byte response, request ID `1`, response ID `2`, and two 64-bit timestamps. See `protocol.md` and the reconstructed source.

### `QuestRefresh120`

The class is stored in `Assets/RequestFPS.cs`. Native behavior selects:

1. 120 Hz when available;
2. otherwise 90 Hz when available;
3. otherwise 72 Hz.

It sets the runtime display frequency, adjusts the Unity target frame rate, disables v-sync, and subscribes to the Meta refresh-rate-change event. Recovered log text: `Refresh changed {0} -> {1}`.

### `SafeTrackingOrigin`

Recovered members:

- field `listenForOriginChanges`
- coroutine `Start()`
- `OnDestroy()`
- `OnTrackingOriginUpdated(_)`
- `ApplyBestOrigin()`

The build references Unity XR's `GetSupportedTrackingOriginModes`, `TrySetTrackingOriginMode`, `GetTrackingOriginMode`, and `trackingOriginUpdated`. Recovered log text:

`[SafeTrackingOrigin] Supported={0}  Tried={1}  Ok={2}  Actual={3}`

## Scene evidence

Serialized scene data sets:

- `PoseSender.serverPort = 65432`
- `TimeSyncServer.syncPort = 42000`
- `PoseSender.ovrCameraRig` to an `OVRCameraRig`
- `PoseSender.vrText` to a TextMeshPro text component

UI/object names include `IPInputField`, `IPLabel`, `ApplyButton`, `SettingsCanvas`, and the label text `IP Address`. The app also contains Meta Interaction SDK controller and hand assets.

## Wire protocol

The APK independently establishes the same legacy protocol consumed by AIRoA's public ROS bridge:

- TCP port `65432`, UTF-8 newline-delimited JSON pose frames
- UDP port `42000`, NTP-like little-endian timestamp exchange

The public bridge uses the same field keys and describes the current transport as legacy TCP/JSON, with a future `yubi_quest_app` UDP/binary transport planned.

## Security/operational observations

- No authentication, encryption, or client authorization is present in the recovered application protocol.
- Any host able to reach the headset's TCP port can receive live pose/controller input.
- UDP time synchronization accepts the simple packet-ID/timestamp request.
- Android network-security configuration disables cleartext HTTP for Android framework traffic, but it does not protect the app's raw TCP/UDP sockets.
- The APK is debug-signed and uses a template package identifier, both consistent with an internal/research sideload build rather than a store release.

Use only on a trusted local network.

## What cannot be recovered from this APK alone

- Original Git repository or commit
- Exact C# source text and comments
- Unity `.unity`, `.prefab`, `.meta`, package lock, and full project settings in authoring form
- Editor-only scripts that were stripped
- Original symbols/PDBs
- Exact license for the unpublished Quest app source

## Included artifacts

- `reconstructed_unity/Assets/*.cs` — best-effort source-level reconstruction
- `protocol.md` — wire-format specification
- `sample_pose_frame.json` — complete example payload
- `standalone_probe.py` — small TCP/UDP test client
- `evidence/AndroidManifest_decoded.xml`
- `evidence/apk_summary.json`
- `evidence/il2cpp_metadata_report.json`
- `evidence/custom_methods_disassembly.txt`
