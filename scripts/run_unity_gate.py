#!/usr/bin/env python3
"""Run deterministic HandUMI Unity tests and optional Android builds."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

SUPPORTED_UNITY = "6000.0.76f1"
EXPECTED_ASSEMBLIES = {
    "editmode": "HandUMIQuestApp.EditModeTests",
    "playmode": "HandUMIQuestApp.PlayModeTests",
}
BUILD_PROFILES = {
    "controller": {
        "prepare": "HandUMICompatibilityBuild.RebuildCompatibilityScene",
        "build": "HandUMICompatibilityBuild.BuildAndroid",
        "environment": "HANDUMI_APK_OUTPUT",
        "artifact": "handumi-quest-controller.apk",
        "package": "com.handumi.questapp",
    },
    "bodyprobe": {
        "prepare": "HandUMIBodyProbeBuild.RebuildScene",
        "build": "HandUMIBodyProbeBuild.BuildAndroid",
        "environment": "HANDUMI_BODY_PROBE_APK_OUTPUT",
        "artifact": "handumi-body-probe.apk",
        "package": "com.handumi.questapp.bodyprobe",
    },
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def project_version(project: Path) -> str:
    version_file = project / "ProjectSettings" / "ProjectVersion.txt"
    for line in version_file.read_text(encoding="utf-8").splitlines():
        if line.startswith("m_EditorVersion:"):
            return line.split(":", 1)[1].strip()
    raise RuntimeError(f"missing m_EditorVersion in {version_file}")


def find_unity(explicit: Path | None, project: Path) -> Path:
    version = project_version(project)
    candidates: list[Path] = []
    if explicit is not None:
        candidates.append(explicit)
    for variable in ("HANDUMI_UNITY_EDITOR", "UNITY_EDITOR_PATH"):
        if os.environ.get(variable):
            candidates.append(Path(os.environ[variable]))
    executable = shutil.which("Unity") or shutil.which("unity-editor")
    if executable:
        candidates.append(Path(executable))
    candidates.extend(
        [
            Path.home() / "Unity/Hub/Editor" / version / "Editor/Unity",
            Path("/opt/Unity/Hub/Editor") / version / "Editor/Unity",
            Path("/Applications/Unity/Hub/Editor")
            / version
            / "Unity.app/Contents/MacOS/Unity",
        ]
    )
    for candidate in candidates:
        resolved = candidate.expanduser().resolve()
        if resolved.is_file() and os.access(resolved, os.X_OK):
            return resolved
    searched = "\n  ".join(str(value) for value in candidates)
    raise RuntimeError(
        f"Unity {SUPPORTED_UNITY} was not found. Set HANDUMI_UNITY_EDITOR. "
        f"Searched:\n  {searched}"
    )


def run(command: list[str], *, log: Path, env: dict[str, str] | None = None) -> None:
    log.parent.mkdir(parents=True, exist_ok=True)
    completed = subprocess.run(command, env=env, check=False)
    if completed.returncode != 0:
        raise RuntimeError(
            f"command failed with exit {completed.returncode}; Unity log: {log}"
        )


def validate_test_report(path: Path, expected_assembly: str) -> dict[str, int | str]:
    if not path.is_file() or path.stat().st_size == 0:
        raise RuntimeError(f"Unity did not produce expected test report: {path}")
    text = path.read_text(encoding="utf-8", errors="replace")
    if expected_assembly not in text:
        raise RuntimeError(
            f"test report is missing expected assembly {expected_assembly}: {path}"
        )
    root = ET.fromstring(text)
    total = int(root.attrib.get("total", root.attrib.get("testcasecount", "0")))
    failures = int(root.attrib.get("failed", root.attrib.get("failures", "0")))
    errors = int(root.attrib.get("errors", "0"))
    skipped = int(root.attrib.get("skipped", root.attrib.get("not-run", "0")))
    if total <= 0:
        raise RuntimeError(f"test report contains no tests: {path}")
    if failures or errors:
        raise RuntimeError(
            f"Unity tests failed: total={total} failures={failures} errors={errors}"
        )
    return {
        "assembly": expected_assembly,
        "total": total,
        "failures": failures,
        "errors": errors,
        "skipped": skipped,
    }


def unity_command(unity: Path, project: Path, log: Path) -> list[str]:
    return [
        str(unity),
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(project),
        # Meta XR 74's editor assembly references an Android-scoped local in
        # common inspector code; a pristine Linux project must enter Android
        # before its first script compilation.
        "-buildTarget",
        "Android",
        "-logFile",
        str(log),
    ]


def run_tests(unity: Path, project: Path, output: Path, platform: str) -> dict[str, object]:
    report = output / f"{platform}-results.xml"
    log = output / f"{platform}-unity.log"
    command = unity_command(unity, project, log) + [
        "-runTests",
        "-testPlatform",
        platform,
        "-testResults",
        str(report),
    ]
    run(command, log=log)
    result = validate_test_report(report, EXPECTED_ASSEMBLIES[platform])
    result.update(
        {
            "report": report.name,
            "report_sha256": sha256(report),
            "log": log.name,
            "log_sha256": sha256(log),
        }
    )
    return result


def run_build(
    unity: Path, project: Path, output: Path, profile_name: str
) -> dict[str, object]:
    profile = BUILD_PROFILES[profile_name]
    artifact = output / str(profile["artifact"])
    env = dict(os.environ)
    env[str(profile["environment"])] = str(artifact)
    for phase in ("prepare", "build"):
        log = output / f"android-{profile_name}-{phase}.log"
        command = unity_command(unity, project, log) + [
            "-quit",
            "-executeMethod",
            str(profile[phase]),
        ]
        run(command, log=log, env=env)
    manifest = Path(str(artifact) + ".manifest.json")
    checksum = Path(str(artifact) + ".sha256")
    for required in (artifact, manifest, checksum):
        if not required.is_file() or required.stat().st_size == 0:
            raise RuntimeError(f"Android build output is absent: {required}")
    manifest_data = json.loads(manifest.read_text(encoding="utf-8"))
    if manifest_data.get("packageIdentifier") != profile["package"]:
        raise RuntimeError(
            f"{profile_name} package identity mismatch: "
            f"{manifest_data.get('packageIdentifier')!r}"
        )
    if manifest_data.get("artifactSha256") != sha256(artifact):
        raise RuntimeError(f"{profile_name} build manifest checksum mismatch")
    signature = "not inspected (apksigner unavailable)"
    apksigner = shutil.which("apksigner")
    if apksigner:
        verified = subprocess.run(
            [apksigner, "verify", "--print-certs", str(artifact)],
            check=False,
            capture_output=True,
            text=True,
        )
        signature = (
            verified.stdout.strip()
            if verified.returncode == 0
            else "unsigned or signature verification failed"
        )
    return {
        "profile": profile_name,
        "package_identifier": profile["package"],
        "artifact": artifact.name,
        "size_bytes": artifact.stat().st_size,
        "sha256": sha256(artifact),
        "signature_status": signature,
        "manifest": manifest.name,
        "checksum_file": checksum.name,
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project-path", type=Path, default=Path(__file__).parents[1])
    parser.add_argument("--unity", type=Path)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument(
        "--android",
        choices=("none", "controller", "bodyprobe", "both"),
        default="none",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    project = args.project_path.resolve()
    output = args.output_dir.resolve()
    output.mkdir(parents=True, exist_ok=True)
    summary: dict[str, object] = {
        "schema": "handumi_unity_gate_v1",
        "started_at": datetime.now(timezone.utc).isoformat(),
        "supported_unity": SUPPORTED_UNITY,
        "project_unity": project_version(project),
        "tests": {},
        "android_builds": [],
    }
    if summary["project_unity"] != SUPPORTED_UNITY:
        raise RuntimeError(
            f"project requires Unity {SUPPORTED_UNITY}, got {summary['project_unity']}"
        )
    unity = find_unity(args.unity, project)
    version_check = subprocess.run(
        [str(unity), "-version"], check=False, capture_output=True, text=True
    )
    version_text = (version_check.stdout + version_check.stderr).strip()
    if version_check.returncode != 0 or SUPPORTED_UNITY not in version_text:
        raise RuntimeError(
            f"Unity executable is not the supported {SUPPORTED_UNITY}: {version_text}"
        )
    summary["unity_version_output"] = version_text
    for platform in ("editmode", "playmode"):
        summary["tests"][platform] = run_tests(
            unity, project, output, platform
        )
    requested = (
        ("controller", "bodyprobe")
        if args.android == "both"
        else ()
        if args.android == "none"
        else (args.android,)
    )
    for profile in requested:
        summary["android_builds"].append(
            run_build(unity, project, output, profile)
        )
    summary["ended_at"] = datetime.now(timezone.utc).isoformat()
    summary["status"] = "passed"
    summary_path = output / "unity-gate-summary.json"
    summary_path.write_text(json.dumps(summary, indent=2, sort_keys=True) + "\n")
    print(json.dumps(summary, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"Unity gate failed: {error}", file=sys.stderr)
        raise SystemExit(1) from error
