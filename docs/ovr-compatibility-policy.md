# Pinned Meta XR/OVR compatibility policy

Status: compatibility retention for the HandUMI research preview; this is not
a completed OpenXR API migration.

## Pinned stack and profiles

The supported source/build stack is immutable at:

- Unity `6000.0.76f1` (`6f7f9e1c9e8a`)
- Meta XR SDK Core `74.0.1`
- Meta XR SDK Interaction OVR `74.0.2`
- Unity OpenXR plug-in `1.15.0`
- Unity XR Management `4.5.4` (locked transitive)
- Unity Input System `1.14.1`
- Unity Test Framework requested as `1.1.33` and resolved by Unity 6 to the
  editor-builtin `1.6.0`
- Android ARM64, Vulkan, IL2CPP, minimum/target API 32, UnityPlayerActivity

`Packages/manifest.json`, `Packages/packages-lock.json`, and
`ProjectSettings/ProjectVersion.txt` are the pinning mechanism. The Unity gate
rejects a different editor. Package-lock changes require a reviewed
differential-validation run.

Two profiles are preserved:

- controller-compatible: `com.handumi.questapp`, version `0.2.1`, controller
  poses/buttons, hand/controller visuals, TCP 65432 and UDP 42000;
- Body Probe diagnostic: `com.handumi.questapp.bodyprobe`, version `0.1.2`,
  packet-v2 body observations, BODY_TRACKING runtime permission, passthrough,
  and the same controller/timing wire contracts.

The Android loader is OpenXR, but source-level controller, timing, body,
passthrough, permission, and lifecycle behavior still calls OVR/Meta XR APIs.
Removing those calls without device parity evidence would risk the preserved
protocol and behavior, so the project retains them for this preview.

## Quest and Horizon OS assumption

The software assumes a vendor-supported Meta Quest 3 or Quest 3S runtime that
exposes OpenXR Stage space and the Meta body, controller, passthrough, and
permission capabilities requested by the pinned packages. The software-only
supported Horizon OS matrix is intentionally empty: no exact Horizon OS build
has passed the required physical lifecycle/body/controller parity run. Every
hardware attestation must record the exact headset model and Horizon OS/build;
that exact pair becomes evidence for the tested artifact only, not a blanket
OS support claim. Quest 2/Pro and future headset generations are unqualified.

## Security updates, EOL, and rollback

Security fixes that do not change public behavior are backported to this
branch. A Meta/Unity package update is never accepted solely to silence a
scanner: it must pass the complete software gate and physical differential
validation. Distributed APKs remain trusted-network research-preview tools;
TCP/UDP are unauthenticated plaintext and must not be exposed to untrusted LANs.

Pinned OVR retention ends no later than **2027-01-31**, or earlier when any of
these occurs:

- Unity 6000.0 LTS or Meta XR 74 no longer receives a required security fix;
- a high/critical vulnerability cannot be mitigated without upgrading;
- a vendor-supported Horizon OS no longer loads the pinned stack;
- Meta removes a required OVR API or BODY_TRACKING behavior;
- the OpenXR replacement passes the differential suite below.

The rollback anchor is commit `f735b80`. Restore that immutable source in a
new worktree (never rewrite the published branch), install Unity 6000.0.76f1,
and use `scripts/run_unity_gate.py --android both`. Rollback candidates remain
unsigned/non-release artifacts until an authorized owner performs signing and
publication.

## Required differential validation before replacement

An OpenXR-native replacement must demonstrate, on the same headset/OS and
against the pinned build:

- controller pose coordinate/sign conventions and every serialized button;
- monotonic/newline-delimited TCP payloads and UDP clock byte layouts;
- body joint set, location flags, invalid-data clearing, source timestamps,
  reconnect sequence/epoch behavior, and permission denial/regrant;
- passthrough composition, package identities, pause/resume, focus recovery,
  scene reload, duplicate-listener prevention, and sleep/wake recovery;
- build/install side-by-side behavior and rollback compatibility.

EditMode and PlayMode tests are necessary but not sufficient. A maintainer must
attach physical Quest evidence before the replacement is declared equivalent.
Body poses and timing remain platform estimates and diagnostic measurements;
they are not anatomical, medical, ergonomic, safety-qualified, or scientifically
validated, and they must never drive a physical safety interlock.
