#!/usr/bin/env python3
"""Validate pinned Quest package, identity, protocol, and gate contracts."""

from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EXPECTED_DIRECT = {
    "com.meta.xr.sdk.core": "74.0.1",
    "com.meta.xr.sdk.interaction.ovr": "74.0.2",
    "com.unity.inputsystem": "1.14.1",
    "com.unity.test-framework": "1.1.33",
    "com.unity.xr.openxr": "1.15.0",
}
EXPECTED_LOCK_OVERRIDES = {
    # Unity 6 resolves the requested legacy test-framework declaration to its
    # editor-builtin package while retaining the request in manifest.json.
    "com.unity.test-framework": "1.6.0",
}
EXPECTED_TRANSITIVE = {
    "com.meta.xr.sdk.interaction": "74.0.1",
    "com.unity.xr.management": "4.5.4",
}


def require_text(path: str, patterns: dict[str, str]) -> None:
    text = (ROOT / path).read_text(encoding="utf-8")
    for label, pattern in patterns.items():
        if re.search(pattern, text, re.MULTILINE) is None:
            raise RuntimeError(f"{path} is missing {label}: {pattern}")


def forbid_text(path: str, patterns: dict[str, str]) -> None:
    text = (ROOT / path).read_text(encoding="utf-8")
    for label, pattern in patterns.items():
        if re.search(pattern, text, re.MULTILINE) is not None:
            raise RuntimeError(f"{path} contains forbidden {label}: {pattern}")


def main() -> None:
    project = (ROOT / "ProjectSettings/ProjectVersion.txt").read_text()
    if "m_EditorVersion: 6000.0.76f1" not in project:
        raise RuntimeError("unsupported Unity editor revision")
    manifest = json.loads((ROOT / "Packages/manifest.json").read_text())
    locked = json.loads((ROOT / "Packages/packages-lock.json").read_text())[
        "dependencies"
    ]
    for package, version in EXPECTED_DIRECT.items():
        if manifest["dependencies"].get(package) != version:
            raise RuntimeError(f"direct package drift: {package}")
        locked_version = EXPECTED_LOCK_OVERRIDES.get(package, version)
        if locked.get(package, {}).get("version") != locked_version:
            raise RuntimeError(f"lock drift: {package}")
    for package, version in EXPECTED_TRANSITIVE.items():
        if locked.get(package, {}).get("version") != version:
            raise RuntimeError(f"transitive package drift: {package}")
    require_text(
        "Assets/Editor/ConfigureHandUMIQuestProject.cs",
        {
            "controller package": r'PackageIdentifier\s*=\s*"com\.handumi\.questapp"',
            "controller version": r'bundleVersion\s*=\s*"0\.2\.1"',
            "lifecycle-safe activity": r"AndroidApplicationEntry\.Activity",
        },
    )
    require_text(
        "Assets/Editor/ConfigureHandUMIBodyProbeProject.cs",
        {
            "body package": r'PackageIdentifier\s*=\s*"com\.handumi\.questapp\.bodyprobe"',
            "body version": r'VersionName\s*=\s*"0\.1\.2"',
            "full body": r"BodyTrackingJointSet\s*=\s*OVRPlugin\.BodyJointSet\.FullBody",
        },
    )
    forbid_text(
        "Assets/Editor/HandUMICompatibilityBuild.cs",
        {
            "local Android SDK path in artifact manifest": r"public string androidSdk",
            "local Android NDK path in artifact manifest": r"public string androidNdk",
            "local JDK path in artifact manifest": r"public string jdk",
        },
    )
    require_text(
        "Assets/HandUMIQuestApp/HandUMIWireProtocol.cs",
        {
            "TCP port": r"PosePort\s*=\s*65432",
            "UDP port": r"TimeSyncPort\s*=\s*42000",
        },
    )
    require_text(
        "Assets/Plugins/Android/AndroidManifest.xml",
        {
            "body permission": r"com\.oculus\.permission\.BODY_TRACKING",
            "Unity activity": r"com\.unity3d\.player\.UnityPlayerActivity",
        },
    )
    for platform, assembly in (
        ("EditMode", "HandUMIQuestApp.EditModeTests"),
        ("PlayMode", "HandUMIQuestApp.PlayModeTests"),
    ):
        asmdef = ROOT / f"Assets/Tests/{platform}/{assembly}.asmdef"
        if json.loads(asmdef.read_text())["name"] != assembly:
            raise RuntimeError(f"test assembly drift: {assembly}")
    result = {
        "schema": "handumi_quest_static_contract_v1",
        "unity": "6000.0.76f1",
        "direct_packages": EXPECTED_DIRECT,
        "lock_overrides": EXPECTED_LOCK_OVERRIDES,
        "transitive_packages": EXPECTED_TRANSITIVE,
        "package_identities": [
            "com.handumi.questapp",
            "com.handumi.questapp.bodyprobe",
        ],
        "test_assemblies": [
            "HandUMIQuestApp.EditModeTests",
            "HandUMIQuestApp.PlayModeTests",
        ],
        "status": "passed",
    }
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
