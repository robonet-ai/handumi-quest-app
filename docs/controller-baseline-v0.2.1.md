# Frozen controller compatibility baseline

The body diagnostic branch starts from, but does not modify, the known-good
controller application. Its frozen source and artifact identity are:

- Git tag: `controller-baseline-v0.2.1`
- Source commit: `1597c240541510aca15d0030f35c3b9f17c8e4a2`
- Package: `com.handumi.questapp`
- Product/version: `HandUMIQuestApp-v0.2.1` / `0.2.1` (`3`)
- APK: `Builds/Android/handumi-quest-app-v0.2.1.apk`
- APK SHA-256: `30683f102fd2b86ffb85770735985d4d0ca3b88e5c9ecceef5d7ea7068906dc9`
- Signer certificate SHA-256:
  `37606830babdf9f329edef3000584c8bb5b5baad2e68fd3b25bdf989cbd2a717`
- Build manifest: `Builds/Android/handumi-quest-app-v0.2.1.apk.manifest.json`
- Unity / Meta XR Core / Meta XR Interaction OVR / OpenXR:
  `6000.0.76f1` / `74.0.1` / `74.0.2` / `1.15.0`
- Automated evidence: 12/12 Android-target EditMode and 2/2 PlayMode tests.

The tagged baseline and body probe have different package identifiers and can
remain installed together. They deliberately share TCP 65432 and UDP 42000,
so only one may run at a time.

## Roll back from the body probe

```bash
adb shell am force-stop com.handumi.questapp.bodyprobe
adb install -r Builds/Android/handumi-quest-app-v0.2.1.apk
adb shell monkey -p com.handumi.questapp 1
```

The controller APK should be rebuilt only from the frozen tag. Body work stays
on `feat/quest-body-diagnostic`, uses its separate scene and identity, and must
not be merged into the tagged controller artifact.

The broader compatibility release still needs the recorded hardware
sleep/recovery matrix and 30-minute soak before REL-001 can be closed. This
freeze preserves the exact rollback candidate; it does not invent that missing
hardware evidence.
