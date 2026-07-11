# Reconstructed Unity scripts

These scripts are intended as the closest practical source-level representation recoverable from the APK. They are not a byte-for-byte decompilation and are not the original project.

## Confirmed project baseline

- Unity `6000.0.76f1`
- Meta XR Core SDK `74.0.1`
- Meta XR Interaction OVR `74.0.2`
- Input System `1.14.1`
- Universal Render Pipeline `17.0.4`
- OpenXR `1.15.0`
- Android / Quest, IL2CPP, target SDK 32

## Scene wiring inferred from serialized data

1. Add an `OVRCameraRig`.
2. Add `PoseSender` to a GameObject and assign the rig plus an optional TextMeshPro status label.
3. Add `TimeSyncServer` to a GameObject.
4. Add `QuestRefresh120` to a startup GameObject.
5. Optionally add `SafeTrackingOrigin`.
6. Permit `android.permission.INTERNET` and build for the supported Quest family.

`PoseSender` opens TCP `65432`; the PC connects to the headset. `TimeSyncServer` listens on UDP `42000`.

## Known approximation points

- The original scene/prefab hierarchy and UI controller are not recoverable as C# source.
- The exact fallback text returned by `GetLocalIPAddress()` is uncertain.
- `SafeTrackingOrigin`'s exact retry timing is approximated.
- Meta XR API surface changes may require tiny edits if these files are compiled against a different SDK version.
