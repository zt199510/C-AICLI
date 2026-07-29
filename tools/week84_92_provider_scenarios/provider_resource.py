#!/usr/bin/env python3
"""Pinned provider-resource Desktop provider scenario launcher."""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys


EXPECTED_PHASE = "provider-resource"
EXPECTED_REQUEST_COUNT = 6
INPUT_PROTOCOL = "week84-92-provider-desktop-scenario-v1"
RESULT_PROTOCOL = "week84-92-provider-driver-result-v1"
RESULT_MARKER = "CAICLI_PROVIDER_DRIVER_RESULT="
MAX_DRIVER_OUTPUT_BYTES = 2 * 1024 * 1024
INPUT_KEYS = frozenset(
    {
        "protocol",
        "batchId",
        "phase",
        "profileOrdinal",
        "driverPath",
        "driverSha256",
        "stagedPackageRoot",
        "entrypoint",
        "argv",
        "appHostPath",
        "workspacePath",
        "candidatePackageJsonPath",
        "requestCount",
        "requestMarkers",
    }
)
RESULT_KEYS = frozenset(
    {
        "protocol",
        "batchId",
        "phase",
        "profileOrdinal",
        "status",
        "desktop",
        "appHost",
        "assertions",
        "cleanup",
    }
)


def _canonical(value: object) -> bytes:
    return json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
        allow_nan=False,
    ).encode("utf-8")


def _fail() -> "None":
    raise SystemExit(79)


def main() -> int:
    descriptor_name = os.environ.get("CAICLI_PROVIDER_DRIVER_INPUT")
    if descriptor_name != "scenario-input.json":
        _fail()
    descriptor_path = Path.cwd() / descriptor_name
    try:
        raw = descriptor_path.read_bytes()
        descriptor = json.loads(raw.decode("utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError):
        _fail()
    if (
        not isinstance(descriptor, dict)
        or frozenset(descriptor) != INPUT_KEYS
        or raw != _canonical(descriptor)
        or descriptor.get("protocol") != INPUT_PROTOCOL
        or descriptor.get("phase") != EXPECTED_PHASE
        or not isinstance(descriptor.get("driverPath"), str)
        or not isinstance(descriptor.get("driverSha256"), str)
        or descriptor.get("entrypoint") != "caicli-desktop.exe"
        or descriptor.get("argv") != ["--disable-gpu"]
        or descriptor.get("appHostPath")
        != "resources/apphost/CSharpAiCli.AppHost.exe"
        or descriptor.get("requestCount") != EXPECTED_REQUEST_COUNT
        or not isinstance(descriptor.get("requestMarkers"), list)
        or len(descriptor["requestMarkers"]) != EXPECTED_REQUEST_COUNT
        or any(
            not isinstance(item, str)
            or not item.startswith(
                f"CAICLI-W84-92-TURN:{descriptor.get('batchId')}:"
            )
            for item in descriptor["requestMarkers"]
        )
    ):
        _fail()
    driver = Path(descriptor["driverPath"])
    try:
        if (
            not driver.is_absolute()
            or not driver.is_file()
            or driver.is_symlink()
            or hashlib.sha256(driver.read_bytes()).hexdigest()
            != descriptor["driverSha256"]
        ):
            _fail()
    except OSError:
        _fail()
    node = os.environ.get("CAICLI_NODE_EXECUTABLE")
    if not node or not Path(node).is_absolute():
        _fail()
    try:
        child = subprocess.run(
            [node, str(driver)],
            cwd=Path.cwd(),
            env=dict(os.environ),
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            shell=False,
            timeout=20 * 60,
            check=False,
            creationflags=(subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0),
        )
    except (OSError, subprocess.SubprocessError):
        _fail()
    if (
        child.returncode != 0
        or len(child.stdout) > MAX_DRIVER_OUTPUT_BYTES
        or len(child.stderr) > MAX_DRIVER_OUTPUT_BYTES
    ):
        _fail()
    try:
        lines = child.stdout.decode("utf-8").splitlines()
        matches = [line for line in lines if line.startswith(RESULT_MARKER)]
        if len(matches) != 1:
            _fail()
        result = json.loads(matches[0][len(RESULT_MARKER):])
    except (UnicodeDecodeError, json.JSONDecodeError):
        _fail()
    if (
        not isinstance(result, dict)
        or frozenset(result) != RESULT_KEYS
        or result.get("protocol") != RESULT_PROTOCOL
        or result.get("batchId") != descriptor.get("batchId")
        or result.get("phase") != EXPECTED_PHASE
        or result.get("profileOrdinal") != descriptor.get("profileOrdinal")
        or result.get("status") != "Passed"
        or not isinstance(result.get("desktop"), dict)
        or frozenset(result["desktop"]) != {"pid", "exitCode"}
        or not isinstance(result.get("appHost"), dict)
        or frozenset(result["appHost"]) != {"pid", "exitCode"}
        or not isinstance(result.get("assertions"), dict)
        or frozenset(result["assertions"])
        != {
            "desktopUiObserved",
            "productBehaviorPassed",
            "recoveryListenerObserved",
            "details",
        }
        or result.get("cleanup")
        != {"status": "Passed", "processDelta": 0, "temporaryDelta": 0}
    ):
        _fail()
    sys.stdout.buffer.write(RESULT_MARKER.encode("ascii") + _canonical(result) + b"\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
