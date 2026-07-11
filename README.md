# YubiQuestApp v0.1.0 — APK recovery bundle

This bundle was produced by static analysis of `yubi-quest-app-v0.1.0.apk`.

## Important limitation

The APK was built with Unity IL2CPP. The original C# method bodies, comments, project settings, prefabs, and version-control history are **not** stored in recoverable source form. The files under `reconstructed_unity/` are an independent, best-effort behavioral reconstruction—not the original AIRoA source.

Confidence labels used in the source comments:

- **CONFIRMED** — name, field, method, constant, or protocol element recovered directly from the APK.
- **STRONGLY INFERRED** — behavior visible in native code and/or exactly matched by the public ROS bridge.
- **APPROXIMATE** — implementation chosen to reproduce observed behavior where original C# syntax was erased.

Start with `YubiQuestApp_APK_Analysis.md` and `reconstructed_unity/README.md`.
