# Unity release gate

This repository uses a repository-owned self-hosted gate because Unity editor
licensing and Android modules are not available on the public hosted runner.
The gate is non-interactive after the runner has a valid Unity entitlement.
No Unity license file, serial, access token, Android keystore, or signing
credential may be stored in this repository or uploaded as an artifact.

## Runner contract

- Linux self-hosted labels: `unity-6000.0.76f1` and `android`.
- Unity editor: exactly `6000.0.76f1`, including Android Build Support,
  Android SDK/NDK, and OpenJDK modules selected by that editor.
- Python 3.10 or newer.
- A runner-scoped `HANDUMI_UNITY_EDITOR` variable pointing to the editor
  executable. `UNITY_EDITOR_PATH` is also accepted locally.
- A valid machine- or user-scoped Unity license prepared outside the checkout.
- No Meta credentials are required for compilation or tests.

Run tests without building Android:

```bash
python3 scripts/run_unity_gate.py \
  --output-dir /tmp/handumi-unity-gate --android none
```

Run the complete software gate, including both package identities:

```bash
python3 scripts/run_unity_gate.py \
  --output-dir /tmp/handumi-unity-gate --android both
```

The script validates the editor revision before opening the project. It runs
the `HandUMIQuestApp.EditModeTests` and `HandUMIQuestApp.PlayModeTests`
assemblies in batch mode, requires nonempty XML reports, and fails for a Unity
nonzero exit, compilation error, absent report, absent expected assembly, zero
tests, or any failed/error test. Reports are `editmode-results.xml` and
`playmode-results.xml`; full editor output is retained in matching Unity logs.

For Android, the script reconstructs then builds the controller-compatible
`com.handumi.questapp` application and isolated
`com.handumi.questapp.bodyprobe` diagnostic application. It requires each APK,
build manifest, checksum file, package identity, and matching SHA-256. The
machine-readable `unity-gate-summary.json` records counts, report/log hashes,
artifact sizes and hashes, and whether `apksigner` could inspect a signature.
No artifact is a release merely because this gate passes.

The manually dispatched workflow retains reports, logs, summaries, and build
candidates for 30 days. A maintainer attests a run by recording the workflow
URL, immutable commit, runner labels, summary SHA-256, per-assembly test counts,
and Android artifact hashes in the release task journal. Missing licensing,
Android modules, or signing are external blockers; compilation and test
failures are software defects.

Local runs and self-hosted runs use the same command and report schema. Physical
Quest install, sleep/wake, radio, thermal, controller/body parity, and Meta
permission execution remain separate hardware gates even when this software
gate passes.
