# Quest hardware validation for REL-001

Use this runbook after the local EditMode/PlayMode suites and Android build
pass. Hardware results must be recorded separately for the released APK and
the reconstruction. They have distinct Android package IDs and can remain
installed together, but they cannot run together because both bind TCP 65432
and UDP 42000.

## 1. Connect and authorize the Quest

1. Enable Developer Mode in the Meta Horizon mobile app.
2. Connect a data-capable USB cable.
3. Put on the headset and accept the USB debugging prompt. Select the option to
   remember this workstation when appropriate.
4. Verify on Linux:

   ```bash
   adb kill-server
   adb start-server
   adb devices -l
   ```

The headset must appear with state `device`, not `unauthorized` or `offline`.
If it does not appear, try another cable/USB port and inspect `lsusb`.

Record the device/runtime context:

```bash
adb shell getprop ro.product.model
adb shell getprop ro.build.version.release
adb shell getprop ro.build.display.id
adb shell getprop ro.build.version.incremental
```

## 2. Preserve and test the released APK

Confirm the reference file before installation:

```bash
sha256sum reference-quest-app-v0.1.0.apk
# Expected:
# 1af91c35e0476b629d85f87bed33667a37b08ecba934596f8420069ab7174
```

Install and launch it:

```bash
adb install -r reference-quest-app-v0.1.0.apk
adb shell monkey -p com.UnityTechnologies.com.unity.template.urpblank 1
```

With the Quest and workstation on the same LAN, find its address and run the
standalone probe or HandUMI receiver:

```bash
adb shell ip route
# Replace the TEST-NET address with the `src` address reported above.
QUEST_IP=192.0.2.2
python standalone_probe.py "$QUEST_IP"
```

Capture at least five minutes of raw output and logs. Exercise both controllers,
all buttons/touches, controller sleep/recovery, headset removal/resume, TCP
disconnect/reconnect, and a brief Wi-Fi interruption.

## 3. Install HandUMIQuestApp side by side

The reconstruction uses the distinct package ID `com.handumi.questapp`, so it
can coexist with the reference app. Do not uninstall the reference app.
Only one app may run at a time because the wire-compatible builds use the same
ports. Force-stop the original before launching the HandUMI build:

```bash
adb install -r Builds/Android/handumi-quest-app-v0.2.1.apk
adb shell am force-stop com.UnityTechnologies.com.unity.template.urpblank
adb shell am force-stop com.handumi.questapp
adb shell monkey -p com.handumi.questapp 1
```

Put on the headset (or keep its proximity sensor active), select
`HandUMIQuestApp-v0.2.1` under App Library > Unknown Sources if necessary,
accept first-run prompts, and leave the app in the foreground. Wake both
controllers and keep them inside the headset's tracking view.

To switch back without uninstalling either app:

```bash
adb shell am force-stop com.handumi.questapp
adb shell monkey -p com.UnityTechnologies.com.unity.template.urpblank 1
```

Repeat the same capture and interaction sequence used for the released APK.
Also run a 30-minute mixed-motion/network/thermal session.

The HandUMI build intentionally uses `UnityPlayerActivity`, not
`UnityPlayerGameActivity`. Unity issue UUM-139694 reports intermittent Quest
freezes in GameActivity's native pause/resume callbacks. Confirm the lifecycle
workaround in the built manifest before the sleep/wake soak:

```bash
"$ANDROID_SDK_ROOT/cmdline-tools/latest/bin/apkanalyzer" manifest print \
  Builds/Android/handumi-quest-app-v0.2.1.apk | \
  grep 'android:name="com.unity3d.player.UnityPlayerActivity"'
```

Run at least ten sleep/wake cycles, including one sleep of five minutes or
longer, both with the TCP probe connected and disconnected. The app must resume
head tracking and controller input every time without being force-stopped.

### In-headset frontend validation

For the HandUMI reconstruction, verify the frontend before starting the soak:

1. Confirm the environment is opaque `#191919` and the centered title reads
   `HandUMI Quest App (v0.2.1)` in `#AF0000`.
2. With no TCP probe connected, confirm that no IP/status line is visible.
3. Start `standalone_probe.py`; confirm
   `Connected • IP: <QUEST_IP>:65432` appears.
   Stop the probe and confirm the IP line disappears after disconnect detection.
4. With controllers active, translate and rotate both devices and exercise
   A/B/X/Y, menu, both index triggers, both grips, both thumbsticks, and
   thumbstick clicks/touches. Record which controls visibly animate on the
   runtime-provided model and whether the packaged fallback was selected.
5. Put each controller to sleep and wake it. No stale visual may remain after
   tracking loss, and the recovered visual must return to the current pose.
6. Switch to hand tracking and articulate every finger on both hands. Confirm
   wrist/finger motion, confidence-based hiding, then switch back to controllers
   without duplicate hand/controller visuals.
7. Capture headset video plus `adb logcat -s Unity` during the transition and
   retain the APK SHA-256, Quest runtime, selected model path, and any accepted
   visual limitation with the parity evidence.

Useful diagnostics:

```bash
adb logcat -c
adb logcat -s Unity ActivityManager AndroidRuntime
adb shell dumpsys thermalservice
adb shell dumpsys package com.handumi.questapp
```

## 4. HandUMI integration

Set the Quest IP in `handumi-sw/configs/tracking_meta_quest.yaml`, then run:

```bash
cd /path/to/handumi-sw
.venv/bin/python -m handumi.tracking.meta_quest \
  --config configs/tracking_meta_quest.yaml
```

Good evidence includes a stable connection, steady pose rate appropriate for
the headset's active refresh rate, both controllers becoming tracked/valid,
moving positions, and successful UDP synchronization. Run the normal HandUMI
live and recording workflows and validate the resulting episode.

## 5. Acceptance record

For both APKs record:

- APK SHA-256 and signer certificate SHA-256.
- Quest model, Horizon OS/build, network, Unity/Meta/OpenXR versions.
- JSON keys/types, coordinate/quaternion conventions, controller mappings,
  tracking/validity transitions, battery values, and timestamps.
- Pose/device rates, UDP RTT/offset stability, disconnect recovery, observed
  gaps, errors, thermal behavior, and any accepted difference.

Legacy packets omit a sender sequence number. Do not report measured zero
packet loss from those runs; report loss as unknown and retain receive-rate and
interarrival evidence instead.
