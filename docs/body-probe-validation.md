# HandUMI Body Probe validation

`HandUMI Body Probe` is an isolated Quest diagnostic application for INV-001.
It uses package `com.handumi.questapp.bodyprobe`, scene
`Assets/HandUMIBodyProbe/Scenes/HandUMIBodyProbe.unity`, and APK
`Builds/Android/handumi-body-probe.apk`. It can coexist with the frozen
controller application, but both use TCP 65432 and UDP 42000 and therefore must
not run simultaneously.

## Build and static verification

```bash
UNITY=/home/alvax/Unity/Hub/Editor/6000.0.76f1/Editor/Unity

"$UNITY" -batchmode -nographics -buildTarget Android \
  -projectPath "$PWD" -runTests -testPlatform EditMode \
  -testResults Builds/Verification/bodyprobe-editmode-results.xml \
  -logFile Builds/Verification/bodyprobe-editmode.log

"$UNITY" -batchmode -nographics -buildTarget Android \
  -projectPath "$PWD" -runTests -testPlatform PlayMode \
  -testResults Builds/Verification/bodyprobe-playmode-results.xml \
  -logFile Builds/Verification/bodyprobe-playmode.log

"$UNITY" -batchmode -nographics -buildTarget Android \
  -projectPath "$PWD" \
  -executeMethod HandUMIBodyProbeBuild.BuildAndroid \
  -logFile Builds/Android/bodyprobe-build.log -quit
```

The build writes an APK, JSON build manifest, and SHA-256 sidecar. Inspect the
APK before installation:

```bash
apkanalyzer manifest print Builds/Android/handumi-body-probe.apk
apksigner verify --verbose --print-certs Builds/Android/handumi-body-probe.apk
```

Require the body package, `com.oculus.permission.BODY_TRACKING`, optional
`com.oculus.software.body_tracking`, ARM64, the diagnostic scene/build identity,
and a valid signature.

## Install and one-minute smoke

```bash
adb install -r Builds/Android/handumi-body-probe.apk
adb shell am force-stop com.handumi.questapp
adb shell am force-stop com.handumi.questapp.bodyprobe
adb shell monkey -p com.handumi.questapp.bodyprobe 1
```

Put on and unlock the Quest, grant body tracking permission, and keep the app
foregrounded. The status panel must explicitly show permission, body active or
inactive, requested/active joint set, joint count, calibration, fidelity,
sender sequence, endpoint, and connection state. An inactive or unsupported
result is evidence; do not substitute old joint poses.

On the workstation:

```bash
handumi-quest-probe capture \
  --config configs/tracking_meta_quest.yaml \
  --duration-s 60 \
  --adb-health \
  --output artifacts/quest-probe/bodyprobe-smoke
```

Confirm `session_manifests.jsonl` contains the first sender record, pose records
retain all legacy flat controller/HMD fields, body packets contain exactly 84
joints (or explicitly report the 70-joint fallback), and every joint preserves
its raw location flags. Meta XR 74 exposes `OVRPlugin.BodyState.Time` rather
than raw OpenXR `XrTime`; the manifest labels this limitation so the INV-001
architecture decision can select the native fallback if necessary.

After the smoke passes, run S1, S2, S3, and S4 exactly as specified in the
companion tasktree. A human must wear the headset and perform the safe scripted
motions; S3 additionally requires the documented HandUMI controller mounting.
Quest Link L1 requires a supported Windows Meta Quest Link host or an explicit
decision that Link is outside the deployment scope.
