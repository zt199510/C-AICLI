#!/usr/bin/env python3
"""Trusted Desktop provider gateway for the Week 84-92 Goal.

The harness stages the complete packaged Desktop tree, launches only a
prior-sealed scenario/driver pair, and exposes a loopback Responses API.  A
request consumes a pre-reserved turn only when the owning socket belongs to
the staged AppHost descendant in this run's inherited Job/process group.
Provider credentials remain in this process and are never inherited by the
Desktop, AppHost, Node, or scenario processes.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timedelta, timezone
import hashlib
from http import client as httpclient
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import socket
import ssl
import stat
import struct
import subprocess
import sys
import tempfile
import threading
import time
from typing import Any, Mapping, Sequence
from urllib import parse as urlparse

try:
    import week84_92_trusted_executor as trusted
except ModuleNotFoundError:  # Imported as tools.* by the self-test suite.
    from tools import week84_92_trusted_executor as trusted


SCHEMA_VERSION = trusted.SCHEMA_VERSION
GOAL_ID = trusted.GOAL_ID
DESCRIPTOR_PROTOCOL = trusted.PROVIDER_DESCRIPTOR_PROTOCOL
OBSERVATION_PROTOCOL = trusted.PROVIDER_OBSERVED_REQUEST_PROTOCOL
LAUNCH_RECEIPT_PROTOCOL = trusted.PACKAGE_LAUNCH_RECEIPT_PROTOCOL
SCENARIO_INPUT_PROTOCOL = "week84-92-provider-desktop-scenario-v1"
DRIVER_RESULT_PROTOCOL = "week84-92-provider-driver-result-v1"
DRIVER_RESULT_MARKER = b"CAICLI_PROVIDER_DRIVER_RESULT="
PHASE_BATCH_SIZE = {
    phase: values[0] for phase, values in trusted._PROVIDER_BATCH_SIZES.items()
}
RESPONSES_REQUEST_FIELDS = (
    "model",
    "input",
    "instructions",
    "previous_response_id",
    "tools",
    "stream",
)
MAX_JSON_BYTES = trusted.MAX_JSON_BYTES
MAX_REQUEST_BYTES = 8 * 1024 * 1024
MAX_RESPONSE_BYTES = 32 * 1024 * 1024
MAX_CHILD_OUTPUT_BYTES = 2 * 1024 * 1024
SCENARIO_TIMEOUT_SECONDS = 20 * 60.0
UPSTREAM_TIMEOUT_SECONDS = 180.0
LOCAL_API_PREFIX = "/v1"
TEST_LOOPBACK_FLAG = "CAICLI_PROVIDER_TEST_LOOPBACK"
TEST_LOOPBACK_VALUE = "credential-free"
TEST_LOOPBACK_KEY = "test-only-no-credential"
CONTROLLED_FIXTURE_PROJECT = b"""<Project>
  <Target Name="Test">
    <ReadLinesFromFile File="result.txt">
      <Output TaskParameter="Lines" ItemName="ObservedResult" />
    </ReadLinesFromFile>
    <Error Condition="'@(ObservedResult)' != 'pass'" Text="result.txt must contain pass" />
  </Target>
</Project>
"""
_SAFE_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")
_SHA1 = re.compile(r"^[0-9a-f]{40}$")
_SHA256 = re.compile(r"^[0-9a-f]{64}$")
_SECRET_PATTERNS = (
    re.compile(r"(?i)(?:api[_-]?key|password|secret|access[_-]?token)\s*[:=]\s*\S+"),
    re.compile(r"(?i)authorization\s*:\s*bearer\s+\S+"),
    re.compile(r"(?<![A-Za-z0-9])sk-[A-Za-z0-9_-]{8,}"),
    re.compile(r"(?<![A-Za-z0-9])(?:ghp_|github_pat_|xox[baprs]-)[A-Za-z0-9_-]{8,}"),
    re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
)


class HarnessError(RuntimeError):
    def __init__(self, code: str, exit_code: int = 79):
        self.code = code
        self.exit_code = exit_code
        super().__init__(f"provider harness failure [{code}]")


class _DuplicateKeyError(ValueError):
    pass


def _pairs_without_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise _DuplicateKeyError(key)
        result[key] = value
    return result


def _canonical(value: Any) -> bytes:
    return json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
        allow_nan=False,
    ).encode("utf-8")


def _utc_now() -> str:
    return (
        datetime.now(timezone.utc)
        .isoformat(timespec="microseconds")
        .replace("+00:00", "Z")
    )


def _is_int(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _safe_id(value: Any) -> bool:
    return isinstance(value, str) and _SAFE_ID.fullmatch(value) is not None


def _relative(value: Any) -> str:
    try:
        return PurePosixPath(*trusted._relative_parts(value)).as_posix()
    except trusted.ExecutorError:
        raise HarnessError("UNSAFE_PATH") from None


def _read_json(root: Path, relative: str, code: str) -> tuple[dict[str, Any], bytes]:
    try:
        raw = trusted._read_fixed_bytes(root, relative, limit=MAX_JSON_BYTES)
        value = json.loads(
            raw.decode("utf-8"), object_pairs_hook=_pairs_without_duplicates
        )
    except (
        trusted.ExecutorError,
        UnicodeDecodeError,
        json.JSONDecodeError,
        _DuplicateKeyError,
    ):
        raise HarnessError(code) from None
    if not isinstance(value, dict) or raw != _canonical(value):
        raise HarnessError(code)
    return value, raw


def _contains_secret(value: bytes | str, secrets: Sequence[str]) -> bool:
    if isinstance(value, bytes):
        try:
            text = value.decode("utf-8")
        except UnicodeDecodeError:
            return True
    else:
        text = value
    if any(secret and secret in text for secret in secrets):
        return True
    return any(pattern.search(text) is not None for pattern in _SECRET_PATTERNS)


def _validate_json_complexity(value: Any, *, depth: int = 0) -> None:
    if depth > 64:
        raise HarnessError("REQUEST_JSON")
    if value is None or isinstance(value, (bool, int, float, str)):
        if isinstance(value, str) and len(value.encode("utf-8")) > MAX_REQUEST_BYTES:
            raise HarnessError("REQUEST_JSON")
        return
    if isinstance(value, list):
        if len(value) > 20_000:
            raise HarnessError("REQUEST_JSON")
        for item in value:
            _validate_json_complexity(item, depth=depth + 1)
        return
    if isinstance(value, dict):
        if len(value) > 20_000 or any(not isinstance(key, str) for key in value):
            raise HarnessError("REQUEST_JSON")
        for item in value.values():
            _validate_json_complexity(item, depth=depth + 1)
        return
    raise HarnessError("REQUEST_JSON")


def _contains_marker(value: Any, marker: str) -> bool:
    if isinstance(value, str):
        return marker in value
    if isinstance(value, list):
        return any(_contains_marker(item, marker) for item in value)
    if isinstance(value, dict):
        return any(_contains_marker(item, marker) for item in value.values())
    return False


def _descriptor_shape(
    descriptor: Mapping[str, Any],
    *,
    descriptor_path: str,
    observation_path: str,
) -> None:
    gate_id = descriptor.get("gateId")
    phase = descriptor.get("phase")
    batch_id = descriptor.get("batchId")
    reservations = descriptor.get("reservations")
    if (
        frozenset(descriptor) != trusted._PROVIDER_DESCRIPTOR_KEYS
        or descriptor.get("schemaVersion") != SCHEMA_VERSION
        or descriptor.get("protocol") != DESCRIPTOR_PROTOCOL
        or descriptor.get("goalId") != GOAL_ID
        or gate_id not in trusted.PROVIDER_PHASE_LAYOUT
        or phase not in PHASE_BATCH_SIZE
        or phase not in trusted.PROVIDER_PHASE_LAYOUT[gate_id]
        or not _safe_id(batch_id)
        or not _safe_id(descriptor.get("attemptId"))
        or not _safe_id(descriptor.get("runId"))
        or not isinstance(descriptor.get("productCandidate"), str)
        or _SHA1.fullmatch(descriptor["productCandidate"]) is None
        or descriptor.get("artifactRoot")
        != trusted.PROVIDER_ARTIFACT_ROOTS[gate_id]
        or descriptor.get("scenarioPath")
        != trusted.PROVIDER_SCENARIO_PATHS[phase]
        or descriptor.get("driverPath") != trusted.PROVIDER_DRIVER_PATHS[phase]
        or not isinstance(descriptor.get("runTokenSha256"), str)
        or _SHA256.fullmatch(descriptor["runTokenSha256"]) is None
        or not isinstance(reservations, list)
        or len(reservations) != PHASE_BATCH_SIZE[phase]
    ):
        raise HarnessError("DESCRIPTOR_BINDING")
    profile = descriptor.get("profileOrdinal")
    if (
        phase == "provider-resource"
        and (not _is_int(profile) or not 1 <= profile <= 5)
    ) or (phase != "provider-resource" and profile is not None):
        raise HarnessError("DESCRIPTOR_PROFILE")
    expected_directory = (
        f"{trusted.PROVIDER_ARTIFACT_ROOTS[gate_id]}/provider-runtime/{gate_id}"
    )
    if (
        PurePosixPath(_relative(descriptor_path)).parent.as_posix()
        != expected_directory
        or PurePosixPath(descriptor_path).name != f"{batch_id}.descriptor.json"
        or PurePosixPath(_relative(observation_path)).parent.as_posix()
        != expected_directory
        or PurePosixPath(observation_path).name != f"{batch_id}.observation.json"
        or descriptor.get("packageLaunchReceiptPath")
        != f"{expected_directory}/{batch_id}.package-launch.json"
        or descriptor.get("stagingRelativeRoot")
        != (
            "artifacts/week84-92-goal-control/provider-staging/"
            f"{gate_id}/{batch_id}"
        )
    ):
        raise HarnessError("DESCRIPTOR_PATH")
    previous_sequence: int | None = None
    previous_ordinal: int | None = None
    seen: set[str] = set()
    for reservation in reservations:
        if (
            not isinstance(reservation, dict)
            or frozenset(reservation) != trusted._DESCRIPTOR_RESERVATION_KEYS
            or not _safe_id(reservation.get("reservationId"))
            or reservation["reservationId"] in seen
            or not _is_int(reservation.get("reservationSequence"))
            or reservation["reservationSequence"] < 1
            or not _is_int(reservation.get("turnOrdinal"))
            or reservation["turnOrdinal"] < 1
            or (
                previous_sequence is not None
                and reservation["reservationSequence"] != previous_sequence + 1
            )
            or (
                previous_ordinal is not None
                and reservation["turnOrdinal"] != previous_ordinal + 1
            )
        ):
            raise HarnessError("DESCRIPTOR_RESERVATIONS")
        seen.add(reservation["reservationId"])
        previous_sequence = reservation["reservationSequence"]
        previous_ordinal = reservation["turnOrdinal"]


def _preflight(
    candidate_root: Path,
    control_root: Path,
    descriptor: Mapping[str, Any],
) -> tuple[
    trusted.PackageTreeBinding,
    trusted.ProviderBoundaryBinding,
    dict[str, Any],
]:
    gate_id = str(descriptor["gateId"])
    phase = str(descriptor["phase"])
    candidate = str(descriptor["productCandidate"])
    try:
        _policy, _policy_sha, _harness, scenario, driver = (
            trusted._provider_control_source_preflight(
                candidate_root,
                gate_id=gate_id,
                product_candidate=candidate,
                phase=phase,
            )
        )
        if (
            scenario != descriptor.get("scenarioSource")
            or driver != descriptor.get("driverSource")
        ):
            raise HarnessError("DESCRIPTOR_SOURCE")
        package = trusted._package_identity_evidence_binding(
            candidate_root,
            control_root,
            gate_id=gate_id,
            product_candidate=candidate,
            evidence_path=str(descriptor["packageIdentityEvidence"]["path"]),
        )
        if package.descriptor_binding() != descriptor.get("packageIdentityEvidence"):
            raise HarnessError("DESCRIPTOR_PACKAGE")
        boundary = trusted.verify_provider_boundary_decision(
            candidate_root,
            control_root,
            gate_id=gate_id,
            product_candidate=candidate,
            package=package,
        )
        if (
            boundary.descriptor_binding()
            != descriptor.get("providerBoundaryDecision")
            or boundary.mode != "cooperative-candidate"
        ):
            raise HarnessError("DESCRIPTOR_BOUNDARY")
        controlled_gate = trusted.CONTROLLED_DESCENDANT_GATES.get(gate_id)
        if controlled_gate is None:
            if (
                descriptor.get("controlledWriteTombstone") is not None
                or descriptor.get("controlledWritePreauthorization") is not None
            ):
                raise HarnessError("DESCRIPTOR_CONTROLLED")
        else:
            tombstone = trusted.verify_controlled_write_tombstone(
                candidate_root,
                gate_id=controlled_gate,
                product_candidate=candidate,
            )
            if descriptor.get("controlledWriteTombstone") != {
                "path": tombstone.path,
                "sha256": tombstone.sha256,
                "commit": tombstone.commit,
            } or descriptor.get("controlledWritePreauthorization") != {
                "path": tombstone.preauthorization.path,
                "sha256": tombstone.preauthorization.sha256,
                "commit": tombstone.preauthorization.commit,
            }:
                raise HarnessError("DESCRIPTOR_CONTROLLED")
        ledger = trusted.read_provider_ledger(candidate_root)
    except trusted.ExecutorError as error:
        raise HarnessError(f"TRUSTED_{error.code}") from None
    entries = {
        item["reservationId"]: item
        for item in ledger["entries"]
        if item.get("reservationId")
        in {value["reservationId"] for value in descriptor["reservations"]}
    }
    completed = {
        item.get("reservationId")
        for item in ledger["attemptEvents"]
        if item.get("eventType") == "TurnCompleted"
    }
    for reservation in descriptor["reservations"]:
        entry = entries.get(reservation["reservationId"])
        if (
            not isinstance(entry, dict)
            or entry.get("sequence") != reservation["reservationSequence"]
            or entry.get("gateId") != gate_id
            or entry.get("phase") != phase
            or entry.get("productCandidate") != candidate
            or entry.get("attemptId") != descriptor["attemptId"]
            or entry.get("runId") != descriptor["runId"]
            or reservation["reservationId"] in completed
        ):
            raise HarnessError("DESCRIPTOR_LEDGER")
    source_inventory = trusted._package_inventory(
        trusted._safe_directory(
            candidate_root, package.package_root, "PACKAGE_ROOT"
        )
    )
    if (
        source_inventory["treeRootSha256"] != package.tree_root_sha256
        or source_inventory["entryCount"] != package.entry_count
        or source_inventory["totalBytes"] != package.total_bytes
    ):
        raise HarnessError("PACKAGE_TREE")
    return package, boundary, source_inventory


def _provider_environment(descriptor: Mapping[str, Any]) -> tuple[str, str, str, str]:
    run_token = os.environ.get("CAICLI_PROVIDER_RUN_TOKEN", "")
    model = os.environ.get("OPENAI_MODEL", "")
    base_url = os.environ.get("OPENAI_BASE_URL", "")
    api_key = os.environ.get("OPENAI_API_KEY", "")
    if (
        re.fullmatch(r"[0-9a-f]{64}", run_token) is None
        or hashlib.sha256(run_token.encode("ascii")).hexdigest()
        != descriptor["runTokenSha256"]
        or not model
        or not base_url
        or not api_key
        or any("\x00" in item or "\r" in item or "\n" in item for item in (model, base_url, api_key))
    ):
        raise HarnessError("PROVIDER_ENVIRONMENT")
    return model, base_url, api_key, run_token


def _mkdir_chain(root: Path, relative: str) -> Path:
    current = root
    for part in trusted._relative_parts(relative):
        current = current / part
        if current.exists() or current.is_symlink():
            if trusted._is_reparse_or_link(current) or not current.is_dir():
                raise HarnessError("STAGING_PATH")
        else:
            try:
                os.mkdir(current)
            except OSError:
                raise HarnessError("STAGING_PATH") from None
    return current


def _write_exclusive(path: Path, raw: bytes, mode: int = 0o600) -> None:
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
    if hasattr(os, "O_BINARY"):
        flags |= os.O_BINARY
    if hasattr(os, "O_NOFOLLOW"):
        flags |= os.O_NOFOLLOW
    descriptor: int | None = None
    try:
        descriptor = os.open(path, flags, mode)
        offset = 0
        while offset < len(raw):
            written = os.write(descriptor, raw[offset:])
            if written <= 0:
                raise OSError
            offset += written
        os.fsync(descriptor)
    except OSError:
        raise HarnessError("STAGING_WRITE") from None
    finally:
        if descriptor is not None:
            os.close(descriptor)


def _copy_package(
    source_root: Path,
    destination_root: Path,
    inventory: Mapping[str, Any],
) -> None:
    try:
        os.mkdir(destination_root)
    except OSError:
        raise HarnessError("STAGING_EXISTS") from None
    made: set[Path] = {destination_root}
    for item in inventory["entries"]:
        relative = PurePosixPath(item["path"])
        parent = destination_root
        for part in relative.parts[:-1]:
            parent = parent / part
            if parent not in made:
                try:
                    os.mkdir(parent)
                except FileExistsError:
                    if not parent.is_dir() or trusted._is_reparse_or_link(parent):
                        raise HarnessError("STAGING_COPY")
                except OSError:
                    raise HarnessError("STAGING_COPY") from None
                made.add(parent)
        source = source_root.joinpath(*relative.parts)
        destination = destination_root.joinpath(*relative.parts)
        source_fd: int | None = None
        destination_fd: int | None = None
        try:
            source_flags = os.O_RDONLY
            destination_flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
            if hasattr(os, "O_BINARY"):
                source_flags |= os.O_BINARY
                destination_flags |= os.O_BINARY
            if hasattr(os, "O_NOFOLLOW"):
                source_flags |= os.O_NOFOLLOW
                destination_flags |= os.O_NOFOLLOW
            source_fd = os.open(source, source_flags)
            source_before = os.fstat(source_fd)
            if (
                not stat.S_ISREG(source_before.st_mode)
                or source_before.st_nlink != 1
                or source_before.st_size != item["bytes"]
                or not os.path.samestat(source_before, os.lstat(source))
                or trusted._is_reparse_or_link(source)
            ):
                raise HarnessError("STAGING_COPY")
            destination_fd = os.open(
                destination,
                destination_flags,
                0o700 if item["mode"] == "100755" else 0o600,
            )
            digest = hashlib.sha256()
            copied = 0
            while True:
                block = os.read(source_fd, 1024 * 1024)
                if not block:
                    break
                digest.update(block)
                copied += len(block)
                offset = 0
                while offset < len(block):
                    written = os.write(destination_fd, block[offset:])
                    if written <= 0:
                        raise OSError
                    offset += written
            os.fsync(destination_fd)
            source_after = os.fstat(source_fd)
            if (
                copied != item["bytes"]
                or digest.hexdigest() != item["sha256"]
                or not os.path.samestat(source_before, source_after)
                or source_before.st_size != source_after.st_size
                or getattr(source_before, "st_mtime_ns", None)
                != getattr(source_after, "st_mtime_ns", None)
                or not os.path.samestat(source_after, os.lstat(source))
            ):
                raise HarnessError("STAGING_COPY")
        except HarnessError:
            raise
        except OSError:
            raise HarnessError("STAGING_COPY") from None
        finally:
            if destination_fd is not None:
                os.close(destination_fd)
            if source_fd is not None:
                os.close(source_fd)
        if os.name != "nt":
            os.chmod(destination, 0o700 if item["mode"] == "100755" else 0o600)
    try:
        staged = trusted._package_inventory(destination_root)
    except trusted.ExecutorError:
        raise HarnessError("STAGING_TREE") from None
    if staged != inventory:
        raise HarnessError("STAGING_TREE")


def _controlled_workspace(
    candidate_root: Path,
    control_root: Path,
    descriptor: Mapping[str, Any],
) -> Path | None:
    if descriptor["phase"] != "controlled-write":
        return None
    binding = descriptor["controlledWritePreauthorization"]
    document, raw = _read_json(candidate_root, binding["path"], "CONTROLLED_WORKSPACE")
    if hashlib.sha256(raw).hexdigest() != binding["sha256"]:
        raise HarnessError("CONTROLLED_WORKSPACE")
    identity = document.get("harnessWorkspaceIdentity")
    if not isinstance(identity, dict):
        raise HarnessError("CONTROLLED_WORKSPACE")
    relative = _relative(identity.get("workspaceRelativePath"))
    path = control_root.joinpath(*PurePosixPath(relative).parts)
    parent_relative = PurePosixPath(relative).parent.as_posix()
    parent = _mkdir_chain(control_root, parent_relative)
    if path.parent != parent or path.exists() or path.is_symlink():
        raise HarnessError("CONTROLLED_WORKSPACE_EXISTS")
    try:
        os.mkdir(path)
    except OSError:
        raise HarnessError("CONTROLLED_WORKSPACE") from None
    _write_exclusive(path / "result.txt", b"fail\n")
    _write_exclusive(path / "Week82Gate.proj", CONTROLLED_FIXTURE_PROJECT)
    return path


def _verify_controlled_transition(
    workspace: Path,
    *,
    secrets: Sequence[str],
) -> dict[str, Any]:
    result_path = workspace / "result.txt"
    project_path = workspace / "Week82Gate.proj"
    try:
        if (
            trusted._is_reparse_or_link(result_path)
            or trusted._is_reparse_or_link(project_path)
            or result_path.read_bytes().decode("utf-8").splitlines() != ["pass"]
            or project_path.read_bytes() != CONTROLLED_FIXTURE_PROJECT
        ):
            raise HarnessError("CONTROLLED_TRANSITION")
    except (OSError, UnicodeDecodeError):
        raise HarnessError("CONTROLLED_TRANSITION") from None
    safe_names = {
        "path",
        "systemroot",
        "windir",
        "comspec",
        "pathext",
        "temp",
        "tmp",
        "dotnet_root",
        "dotnet_host_path",
    }
    environment = {
        key: value for key, value in os.environ.items() if key.casefold() in safe_names
    }
    validation = trusted.CONTROLLED_WRITE_VALIDATION
    try:
        completed = subprocess.run(
            [validation["executable"], *validation["arguments"]],
            cwd=workspace,
            env=environment,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            shell=False,
            timeout=180,
            check=False,
            creationflags=(subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0),
        )
    except (OSError, subprocess.SubprocessError):
        raise HarnessError("CONTROLLED_VALIDATION") from None
    if (
        completed.returncode != 0
        or len(completed.stdout) > MAX_CHILD_OUTPUT_BYTES
        or len(completed.stderr) > MAX_CHILD_OUTPUT_BYTES
        or _contains_secret(completed.stdout + completed.stderr, secrets)
    ):
        raise HarnessError("CONTROLLED_VALIDATION")
    return {
        "controlledTransitionObserved": True,
        "validationCommandSha256": hashlib.sha256(
            _canonical(validation)
        ).hexdigest(),
        "validationExitCode": 0,
    }


def _remove_tree(path: Path, *, allowed_parent: Path) -> bool:
    try:
        if path.parent != allowed_parent or not path.exists() or path.is_symlink():
            return False

        def remove(current: Path) -> None:
            if current.is_symlink() or trusted._is_reparse_or_link(current):
                try:
                    current.unlink()
                except IsADirectoryError:
                    os.rmdir(current)
                return
            if current.is_dir():
                for child in tuple(os.scandir(current)):
                    remove(current / child.name)
                os.rmdir(current)
            else:
                current.unlink()

        remove(path)
        return not path.exists() and not path.is_symlink()
    except OSError:
        return False


def _process_snapshot() -> dict[int, int]:
    if os.name == "nt":
        return _windows_process_snapshot()
    result: dict[int, int] = {}
    proc = Path("/proc")
    if not proc.is_dir():
        return result
    for child in proc.iterdir():
        if not child.name.isdigit():
            continue
        try:
            raw = (child / "stat").read_text("ascii")
            fields = raw[raw.rfind(")") + 2 :].split()
            result[int(child.name)] = int(fields[1])
        except (OSError, ValueError, IndexError):
            continue
    return result


def _windows_process_snapshot() -> dict[int, int]:
    import ctypes
    from ctypes import wintypes

    class PROCESSENTRY32W(ctypes.Structure):
        _fields_ = [
            ("dwSize", wintypes.DWORD),
            ("cntUsage", wintypes.DWORD),
            ("th32ProcessID", wintypes.DWORD),
            ("th32DefaultHeapID", ctypes.c_size_t),
            ("th32ModuleID", wintypes.DWORD),
            ("cntThreads", wintypes.DWORD),
            ("th32ParentProcessID", wintypes.DWORD),
            ("pcPriClassBase", ctypes.c_long),
            ("dwFlags", wintypes.DWORD),
            ("szExeFile", wintypes.WCHAR * 260),
        ]

    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
    kernel.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
    kernel.Process32FirstW.argtypes = [
        wintypes.HANDLE,
        ctypes.POINTER(PROCESSENTRY32W),
    ]
    kernel.Process32FirstW.restype = wintypes.BOOL
    kernel.Process32NextW.argtypes = [
        wintypes.HANDLE,
        ctypes.POINTER(PROCESSENTRY32W),
    ]
    kernel.Process32NextW.restype = wintypes.BOOL
    snapshot = kernel.CreateToolhelp32Snapshot(0x00000002, 0)
    if snapshot == wintypes.HANDLE(-1).value:
        raise HarnessError("PROCESS_SNAPSHOT")
    result: dict[int, int] = {}
    try:
        entry = PROCESSENTRY32W()
        entry.dwSize = ctypes.sizeof(entry)
        present = kernel.Process32FirstW(snapshot, ctypes.byref(entry))
        while present:
            result[int(entry.th32ProcessID)] = int(entry.th32ParentProcessID)
            present = kernel.Process32NextW(snapshot, ctypes.byref(entry))
    finally:
        kernel.CloseHandle(snapshot)
    return result


def _is_descendant(pid: int, ancestor: int, parents: Mapping[int, int]) -> bool:
    seen: set[int] = set()
    current = pid
    while current > 0 and current not in seen:
        if current == ancestor:
            return True
        seen.add(current)
        current = parents.get(current, 0)
    return False


def _process_image(pid: int) -> tuple[str, int | None]:
    if os.name != "nt":
        try:
            return str((Path("/proc") / str(pid) / "exe").resolve(strict=True)), None
        except OSError:
            raise HarnessError("PROCESS_IMAGE") from None
    import ctypes
    from ctypes import wintypes

    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    handle = kernel.OpenProcess(0x1000 | 0x00100000, False, pid)
    if not handle:
        raise HarnessError("PROCESS_IMAGE")
    buffer = ctypes.create_unicode_buffer(32768)
    length = wintypes.DWORD(len(buffer))
    kernel.QueryFullProcessImageNameW.argtypes = [
        wintypes.HANDLE,
        wintypes.DWORD,
        wintypes.LPWSTR,
        ctypes.POINTER(wintypes.DWORD),
    ]
    kernel.QueryFullProcessImageNameW.restype = wintypes.BOOL
    if not kernel.QueryFullProcessImageNameW(handle, 0, buffer, ctypes.byref(length)):
        kernel.CloseHandle(handle)
        raise HarnessError("PROCESS_IMAGE")
    return buffer.value, int(handle)


def _same_path(left: str | Path, right: str | Path) -> bool:
    try:
        left_path = Path(left).resolve(strict=True)
        right_path = Path(right).resolve(strict=True)
    except OSError:
        return False
    return os.path.normcase(str(left_path)) == os.path.normcase(str(right_path))


def _filetime_text(value: Any) -> str:
    ticks = (int(value.dwHighDateTime) << 32) | int(value.dwLowDateTime)
    seconds = (ticks - 116444736000000000) / 10_000_000
    return (
        datetime.fromtimestamp(seconds, timezone.utc)
        .isoformat(timespec="microseconds")
        .replace("+00:00", "Z")
    )


class _JobMembership:
    def __init__(self, run_token: str):
        self.handle: int | None = None
        self.process_group = os.getpgrp() if os.name != "nt" else None
        if os.name == "nt":
            import ctypes
            from ctypes import wintypes

            name = trusted._provider_job_name(
                {"CAICLI_PROVIDER_RUN_TOKEN": run_token}
            )
            kernel = ctypes.WinDLL("kernel32", use_last_error=True)
            kernel.OpenJobObjectW.argtypes = [
                wintypes.DWORD,
                wintypes.BOOL,
                wintypes.LPCWSTR,
            ]
            kernel.OpenJobObjectW.restype = wintypes.HANDLE
            handle = kernel.OpenJobObjectW(0x0004, False, name)
            if not handle:
                raise HarnessError("JOB_MEMBERSHIP")
            self.handle = int(handle)
        if not self.contains(os.getpid()):
            self.close()
            raise HarnessError("JOB_MEMBERSHIP")

    def contains(self, pid: int, process_handle: int | None = None) -> bool:
        if os.name != "nt":
            try:
                return os.getpgid(pid) == self.process_group
            except OSError:
                return False
        import ctypes
        from ctypes import wintypes

        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        owned = process_handle
        if owned is None:
            kernel.OpenProcess.argtypes = [
                wintypes.DWORD,
                wintypes.BOOL,
                wintypes.DWORD,
            ]
            kernel.OpenProcess.restype = wintypes.HANDLE
            opened = kernel.OpenProcess(0x1000, False, pid)
            if not opened:
                return False
            owned = int(opened)
        result = wintypes.BOOL()
        kernel.IsProcessInJob.argtypes = [
            wintypes.HANDLE,
            wintypes.HANDLE,
            ctypes.POINTER(wintypes.BOOL),
        ]
        kernel.IsProcessInJob.restype = wintypes.BOOL
        valid = bool(
            kernel.IsProcessInJob(
                wintypes.HANDLE(owned),
                wintypes.HANDLE(self.handle),
                ctypes.byref(result),
            )
        ) and bool(result.value)
        if process_handle is None:
            kernel.CloseHandle(wintypes.HANDLE(owned))
        return valid

    def close(self) -> None:
        if os.name == "nt" and self.handle is not None:
            import ctypes
            from ctypes import wintypes

            ctypes.WinDLL("kernel32", use_last_error=True).CloseHandle(
                wintypes.HANDLE(self.handle)
            )
            self.handle = None

    def members(self) -> set[int]:
        if os.name != "nt":
            result: set[int] = set()
            for pid in _process_snapshot():
                try:
                    if os.getpgid(pid) == self.process_group:
                        result.add(pid)
                except OSError:
                    continue
            return result
        import ctypes
        from ctypes import wintypes

        buffer = ctypes.create_string_buffer(1024 * 1024)
        returned = wintypes.DWORD()
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.QueryInformationJobObject.argtypes = [
            wintypes.HANDLE,
            ctypes.c_int,
            ctypes.c_void_p,
            wintypes.DWORD,
            ctypes.POINTER(wintypes.DWORD),
        ]
        kernel.QueryInformationJobObject.restype = wintypes.BOOL
        if not kernel.QueryInformationJobObject(
            wintypes.HANDLE(self.handle),
            3,
            buffer,
            len(buffer),
            ctypes.byref(returned),
        ):
            raise HarnessError("JOB_MEMBERSHIP")
        _assigned, listed = struct.unpack_from("<II", buffer.raw, 0)
        pointer_size = ctypes.sizeof(ctypes.c_size_t)
        if listed > (len(buffer) - 8) // pointer_size:
            raise HarnessError("JOB_MEMBERSHIP")
        return {
            int.from_bytes(
                buffer.raw[8 + index * pointer_size : 8 + (index + 1) * pointer_size],
                byteorder=sys.byteorder,
            )
            for index in range(listed)
        }


class _ProcessMonitor:
    def __init__(
        self,
        *,
        scenario_pid: int,
        desktop_path: Path,
        apphost_path: Path,
        job: _JobMembership,
    ):
        self.scenario_pid = scenario_pid
        self.desktop_path = desktop_path
        self.apphost_path = apphost_path
        self.job = job
        self.observed: dict[int, dict[str, Any]] = {}
        self.gateway_apphosts: set[int] = set()
        self.lock = threading.Lock()

    def _observe(self, pid: int, expected: Path) -> bool:
        with self.lock:
            if pid in self.observed:
                return _same_path(self.observed[pid]["image"], expected)
        try:
            image, handle = _process_image(pid)
        except HarnessError:
            return False
        if not _same_path(image, expected) or not self.job.contains(pid, handle):
            if os.name == "nt" and handle is not None:
                import ctypes
                from ctypes import wintypes

                ctypes.WinDLL("kernel32").CloseHandle(wintypes.HANDLE(handle))
            return False
        record = {
            "image": image,
            "handle": handle,
            "observedAt": _utc_now(),
        }
        with self.lock:
            self.observed.setdefault(pid, record)
        return True

    def refresh(self) -> None:
        parents = _process_snapshot()
        for pid in tuple(parents):
            if pid == self.scenario_pid or not _is_descendant(
                pid, self.scenario_pid, parents
            ):
                continue
            try:
                image, handle = _process_image(pid)
            except HarnessError:
                continue
            expected: Path | None = None
            if _same_path(image, self.desktop_path):
                expected = self.desktop_path
            elif _same_path(image, self.apphost_path):
                expected = self.apphost_path
            if expected is not None and self.job.contains(pid, handle):
                with self.lock:
                    self.observed.setdefault(
                        pid,
                        {
                            "image": image,
                            "handle": handle,
                            "observedAt": _utc_now(),
                        },
                    )
                    handle = None
            if os.name == "nt" and handle is not None:
                import ctypes
                from ctypes import wintypes

                ctypes.WinDLL("kernel32").CloseHandle(wintypes.HANDLE(handle))

    def authorize_apphost(self, pid: int) -> bool:
        parents = _process_snapshot()
        if not _is_descendant(pid, self.scenario_pid, parents):
            return False
        if not self._observe(pid, self.apphost_path):
            return False
        with self.lock:
            self.gateway_apphosts.add(pid)
        return True

    def _exit_record(self, pid: int, expected: Path, declared_exit: int) -> dict[str, Any]:
        if not self._observe(pid, expected):
            raise HarnessError("SCENARIO_PROCESS_IDENTITY")
        with self.lock:
            record = dict(self.observed[pid])
        started = record["observedAt"]
        exited = _utc_now()
        actual_exit = declared_exit
        handle = record.get("handle")
        if os.name == "nt" and handle is not None:
            import ctypes
            from ctypes import wintypes

            class FILETIME(ctypes.Structure):
                _fields_ = [
                    ("dwLowDateTime", wintypes.DWORD),
                    ("dwHighDateTime", wintypes.DWORD),
                ]

            kernel = ctypes.WinDLL("kernel32", use_last_error=True)
            if kernel.WaitForSingleObject(wintypes.HANDLE(handle), 10_000) != 0:
                raise HarnessError("SCENARIO_PROCESS_ACTIVE")
            creation = FILETIME()
            exit_time = FILETIME()
            kernel_time = FILETIME()
            user_time = FILETIME()
            code = wintypes.DWORD()
            if not kernel.GetProcessTimes(
                wintypes.HANDLE(handle),
                ctypes.byref(creation),
                ctypes.byref(exit_time),
                ctypes.byref(kernel_time),
                ctypes.byref(user_time),
            ) or not kernel.GetExitCodeProcess(
                wintypes.HANDLE(handle), ctypes.byref(code)
            ):
                raise HarnessError("SCENARIO_PROCESS_EXIT")
            started = _filetime_text(creation)
            exited = _filetime_text(exit_time)
            actual_exit = int(code.value)
        if actual_exit != declared_exit or actual_exit != 0:
            raise HarnessError("SCENARIO_PROCESS_EXIT")
        if trusted._parse_timestamp(started, "PROCESS_TIME") >= trusted._parse_timestamp(
            exited, "PROCESS_TIME"
        ):
            exited = (
                trusted._parse_timestamp(started, "PROCESS_TIME")
                + timedelta(microseconds=1)
            ).isoformat(timespec="microseconds").replace("+00:00", "Z")
        return {
            "imagePath": (
                trusted.FIXED_PACKAGE_ENTRYPOINT
                if expected == self.desktop_path
                else trusted.FIXED_APPHOST_PATH
            ),
            "imageSha256": hashlib.sha256(expected.read_bytes()).hexdigest(),
            "pid": pid,
            "startedAt": started,
            "exitedAt": exited,
            "exitCode": actual_exit,
        }

    def finalize(self, result: Mapping[str, Any]) -> dict[str, Any]:
        desktop = result.get("desktop")
        apphost = result.get("appHost")
        if (
            not isinstance(desktop, dict)
            or frozenset(desktop) != {"pid", "exitCode"}
            or not isinstance(apphost, dict)
            or frozenset(apphost) != {"pid", "exitCode"}
            or any(
                not _is_int(item.get("pid")) or item["pid"] <= 0
                for item in (desktop, apphost)
            )
            or any(not _is_int(item.get("exitCode")) for item in (desktop, apphost))
            or apphost["pid"] not in self.gateway_apphosts
            or self.gateway_apphosts != {apphost["pid"]}
        ):
            raise HarnessError("SCENARIO_PROCESS_RESULT")
        return {
            "desktop": self._exit_record(
                desktop["pid"], self.desktop_path, desktop["exitCode"]
            ),
            "appHost": self._exit_record(
                apphost["pid"], self.apphost_path, apphost["exitCode"]
            ),
        }

    def process_delta(self) -> int:
        return len(self.job.members() - {os.getpid()})

    def close(self) -> None:
        if os.name != "nt":
            return
        import ctypes
        from ctypes import wintypes

        kernel = ctypes.WinDLL("kernel32")
        with self.lock:
            handles = {
                item.get("handle")
                for item in self.observed.values()
                if item.get("handle") is not None
            }
        for handle in handles:
            kernel.CloseHandle(wintypes.HANDLE(handle))


def _socket_owner_pid(
    client_address: tuple[str, int],
    server_address: tuple[str, int],
) -> int | None:
    if os.name == "nt":
        return _windows_socket_owner_pid(client_address, server_address)
    return _proc_socket_owner_pid(client_address, server_address)


def _windows_socket_owner_pid(
    client_address: tuple[str, int],
    server_address: tuple[str, int],
) -> int | None:
    import ctypes
    from ctypes import wintypes

    class ROW(ctypes.Structure):
        _fields_ = [
            ("state", wintypes.DWORD),
            ("localAddr", wintypes.DWORD),
            ("localPort", wintypes.DWORD),
            ("remoteAddr", wintypes.DWORD),
            ("remotePort", wintypes.DWORD),
            ("pid", wintypes.DWORD),
        ]

    iphlp = ctypes.WinDLL("iphlpapi", use_last_error=True)
    size = wintypes.ULONG(0)
    result = iphlp.GetExtendedTcpTable(
        None, ctypes.byref(size), False, socket.AF_INET, 5, 0
    )
    if result not in {0, 122} or size.value <= 4:
        return None
    buffer = ctypes.create_string_buffer(size.value)
    if iphlp.GetExtendedTcpTable(
        buffer, ctypes.byref(size), False, socket.AF_INET, 5, 0
    ) != 0:
        return None
    count = struct.unpack_from("<I", buffer.raw, 0)[0]
    offset = ctypes.sizeof(wintypes.DWORD)
    row_size = ctypes.sizeof(ROW)
    for index in range(count):
        row = ROW.from_buffer_copy(buffer.raw, offset + index * row_size)
        local_ip = socket.inet_ntoa(struct.pack("<I", row.localAddr))
        remote_ip = socket.inet_ntoa(struct.pack("<I", row.remoteAddr))
        local_port = socket.ntohs(int(row.localPort) & 0xFFFF)
        remote_port = socket.ntohs(int(row.remotePort) & 0xFFFF)
        if (
            local_ip == client_address[0]
            and local_port == client_address[1]
            and remote_ip == server_address[0]
            and remote_port == server_address[1]
        ):
            return int(row.pid)
    return None


def _proc_socket_owner_pid(
    client_address: tuple[str, int],
    server_address: tuple[str, int],
) -> int | None:
    target_inode: str | None = None
    try:
        lines = Path("/proc/net/tcp").read_text("ascii").splitlines()[1:]
        for line in lines:
            fields = line.split()
            local, remote = fields[1], fields[2]
            local_addr, local_port = local.split(":")
            remote_addr, remote_port = remote.split(":")
            local_ip = socket.inet_ntoa(bytes.fromhex(local_addr)[::-1])
            remote_ip = socket.inet_ntoa(bytes.fromhex(remote_addr)[::-1])
            if (
                local_ip == client_address[0]
                and int(local_port, 16) == client_address[1]
                and remote_ip == server_address[0]
                and int(remote_port, 16) == server_address[1]
            ):
                target_inode = fields[9]
                break
    except (OSError, ValueError, IndexError):
        return None
    if target_inode is None:
        return None
    needle = f"socket:[{target_inode}]"
    for process in Path("/proc").iterdir():
        if not process.name.isdigit():
            continue
        try:
            for descriptor in (process / "fd").iterdir():
                try:
                    if os.readlink(descriptor) == needle:
                        return int(process.name)
                except OSError:
                    continue
        except OSError:
            continue
    return None


def _parse_upstream(base_url: str, api_key: str) -> tuple[str, str, int, str, bool]:
    parsed = urlparse.urlsplit(base_url)
    test_loopback = os.environ.get(TEST_LOOPBACK_FLAG) == TEST_LOOPBACK_VALUE
    is_loopback = parsed.hostname in {"127.0.0.1", "::1", "localhost"}
    if (
        not parsed.hostname
        or parsed.username is not None
        or parsed.password is not None
        or parsed.query
        or parsed.fragment
        or (
            parsed.scheme != "https"
            and not (
                test_loopback
                and is_loopback
                and parsed.scheme == "http"
                and api_key == TEST_LOOPBACK_KEY
            )
        )
    ):
        raise HarnessError("UPSTREAM_URL")
    port = parsed.port or (443 if parsed.scheme == "https" else 80)
    base_path = parsed.path.rstrip("/")
    if not base_path or not base_path.startswith("/") or "//" in base_path:
        raise HarnessError("UPSTREAM_URL")
    return parsed.hostname, parsed.scheme, port, f"{base_path}/responses", test_loopback


def _upstream_exchange(
    *,
    upstream: tuple[str, str, int, str, bool],
    api_key: str,
    body: bytes,
    secrets: Sequence[str],
) -> tuple[int, bytes, str]:
    host, scheme, port, path, _test = upstream
    connection: httpclient.HTTPConnection
    if scheme == "https":
        connection = httpclient.HTTPSConnection(
            host,
            port,
            timeout=UPSTREAM_TIMEOUT_SECONDS,
            context=ssl.create_default_context(),
        )
    else:
        connection = httpclient.HTTPConnection(
            host, port, timeout=UPSTREAM_TIMEOUT_SECONDS
        )
    try:
        connection.request(
            "POST",
            path,
            body=body,
            headers={
                "Authorization": f"Bearer {api_key}",
                "Content-Type": "application/json",
                "Accept": "application/json",
                "User-Agent": "C-AICLI-Week84-92-Trusted-Gateway/1",
            },
        )
        response = connection.getresponse()
        if response.getheader("Content-Encoding") not in {None, "", "identity"}:
            raise HarnessError("UPSTREAM_ENCODING")
        announced = response.getheader("Content-Length")
        if announced is not None and (
            not announced.isdigit() or int(announced) > MAX_RESPONSE_BYTES
        ):
            raise HarnessError("UPSTREAM_RESPONSE_LIMIT")
        blocks: list[bytes] = []
        size = 0
        while True:
            block = response.read(min(65536, MAX_RESPONSE_BYTES + 1 - size))
            if not block:
                break
            blocks.append(block)
            size += len(block)
            if size > MAX_RESPONSE_BYTES:
                raise HarnessError("UPSTREAM_RESPONSE_LIMIT")
        raw = b"".join(blocks)
        content_type = response.getheader("Content-Type") or ""
    except (OSError, httpclient.HTTPException, ssl.SSLError):
        raise HarnessError("UPSTREAM_TRANSPORT") from None
    finally:
        connection.close()
    if "application/json" not in content_type.casefold() or _contains_secret(
        raw, secrets
    ):
        raise HarnessError("UPSTREAM_RESPONSE")
    try:
        decoded = json.loads(
            raw.decode("utf-8"), object_pairs_hook=_pairs_without_duplicates
        )
        _validate_json_complexity(decoded)
        decoded_text = _canonical(decoded).decode("utf-8")
    except (
        UnicodeDecodeError,
        json.JSONDecodeError,
        _DuplicateKeyError,
        RecursionError,
    ):
        raise HarnessError("UPSTREAM_RESPONSE") from None
    if _contains_secret(decoded_text, secrets):
        raise HarnessError("UPSTREAM_RESPONSE")
    return int(response.status), raw, content_type


class _GatewayState:
    def __init__(
        self,
        *,
        descriptor: Mapping[str, Any],
        model: str,
        api_key: str,
        run_token: str,
        upstream: tuple[str, str, int, str, bool],
        monitor: _ProcessMonitor | None,
    ):
        self.descriptor = descriptor
        self.model = model
        self.api_key = api_key
        self.run_token = run_token
        self.upstream = upstream
        self.monitor = monitor
        self.records: list[dict[str, Any] | None] = [
            None for _ in descriptor["reservations"]
        ]
        self.next_index = 0
        self.violation = False
        self.exchange_lock = threading.Lock()

    def authorize(self, handler: BaseHTTPRequestHandler) -> bool:
        client = (str(handler.client_address[0]), int(handler.client_address[1]))
        server = (str(handler.server.server_address[0]), int(handler.server.server_address[1]))
        pid = _socket_owner_pid(client, server)
        monitor = self.monitor
        valid = (
            pid is not None
            and monitor is not None
            and monitor.authorize_apphost(pid)
        )
        if not valid:
            self.violation = True
        return valid

    def exchange(self, body: bytes) -> tuple[int, bytes, str]:
        with self.exchange_lock:
            if self.next_index >= len(self.records):
                self.violation = True
                raise HarnessError("REQUEST_CAP")
            index = self.next_index
            reservation = self.descriptor["reservations"][index]
            marker = (
                f"CAICLI-W84-92-TURN:{self.descriptor['batchId']}:"
                f"{index + 1}:{reservation['reservationId']}"
            )
            try:
                value = json.loads(
                    body.decode("utf-8"), object_pairs_hook=_pairs_without_duplicates
                )
                _validate_json_complexity(value)
            except (
                UnicodeDecodeError,
                json.JSONDecodeError,
                _DuplicateKeyError,
                RecursionError,
            ):
                raise HarnessError("REQUEST_JSON") from None
            if (
                not isinstance(value, dict)
                or not value
                or not set(value).issubset(RESPONSES_REQUEST_FIELDS)
                or "model" not in value
                or "input" not in value
                or value.get("model") != self.model
                or value.get("stream") is not False
                or (
                    not _contains_marker(value.get("input"), marker)
                    and not _contains_marker(value.get("instructions"), marker)
                )
                or _contains_secret(body, (self.api_key, self.run_token))
            ):
                raise HarnessError("REQUEST_BINDING")
            outbound = {
                field: value[field]
                for field in RESPONSES_REQUEST_FIELDS
                if field in value
            }
            outbound_raw = _canonical(outbound)
            request_sha = hashlib.sha256(outbound_raw).hexdigest()
            self.next_index += 1
            status = "Failed"
            try:
                upstream_status, response_raw, content_type = _upstream_exchange(
                    upstream=self.upstream,
                    api_key=self.api_key,
                    body=outbound_raw,
                    secrets=(self.api_key, self.run_token),
                )
                if 200 <= upstream_status < 300:
                    status = "Succeeded"
                return upstream_status, response_raw, content_type
            finally:
                self.records[index] = {
                    "reservationId": reservation["reservationId"],
                    "reservationSequence": reservation["reservationSequence"],
                    "requestOrdinal": index + 1,
                    "requestSha256": request_sha,
                    "status": status,
                }

    def accepted_records(self) -> list[dict[str, Any]]:
        if (
            self.violation
            or self.next_index != len(self.records)
            or any(item is None or item.get("status") != "Succeeded" for item in self.records)
        ):
            raise HarnessError("REQUEST_OBSERVATION")
        return [dict(item) for item in self.records if item is not None]


def _handler_type(state: _GatewayState) -> type[BaseHTTPRequestHandler]:
    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, _format: str, *_arguments: Any) -> None:
            return

        def _reject(self, status: int = 403) -> None:
            body = b'{"error":"request rejected"}'
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Connection", "close")
            self.end_headers()
            self.wfile.write(body)

        def do_POST(self) -> None:  # noqa: N802
            if not state.authorize(self):
                self._reject()
                return
            if (
                self.path != f"{LOCAL_API_PREFIX}/responses"
                or self.headers.get("Transfer-Encoding") is not None
                or self.headers.get("Authorization")
                != f"Bearer {state.run_token}"
                or self.headers.get("Content-Type", "").split(";", 1)[0].strip().casefold()
                != "application/json"
            ):
                state.violation = True
                self._reject()
                return
            length_text = self.headers.get("Content-Length")
            if (
                length_text is None
                or not length_text.isdigit()
                or not 0 < int(length_text) <= MAX_REQUEST_BYTES
            ):
                state.violation = True
                self._reject(413)
                return
            body = self.rfile.read(int(length_text))
            if len(body) != int(length_text):
                state.violation = True
                self._reject()
                return
            try:
                status, response, content_type = state.exchange(body)
            except HarnessError:
                state.violation = True
                self._reject(502)
                return
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(response)))
            self.send_header("Connection", "close")
            self.end_headers()
            self.wfile.write(response)

        def do_GET(self) -> None:  # noqa: N802
            state.violation = True
            self._reject(405)

        do_PUT = do_GET
        do_PATCH = do_GET
        do_DELETE = do_GET

    return Handler


class _GatewayServer(ThreadingHTTPServer):
    daemon_threads = False
    allow_reuse_address = False


def _scenario_environment(
    *,
    model: str,
    local_base_url: str,
    run_token: str,
    node: Path,
) -> dict[str, str]:
    safe = {
        "path",
        "systemroot",
        "windir",
        "comspec",
        "pathext",
        "temp",
        "tmp",
        "lang",
        "lc_all",
    }
    environment = {
        key: value for key, value in os.environ.items() if key.casefold() in safe
    }
    environment.update(
        {
            "PYTHONNOUSERSITE": "1",
            "PYTHONDONTWRITEBYTECODE": "1",
            "OPENAI_MODEL": model,
            "OPENAI_BASE_URL": local_base_url,
            "OPENAI_API_KEY": run_token,
            "CAICLI_PROVIDER_DRIVER_INPUT": "scenario-input.json",
            "CAICLI_NODE_EXECUTABLE": str(node),
        }
    )
    return environment


def _find_node(environment: Mapping[str, str]) -> Path:
    path_value = next(
        (value for key, value in environment.items() if key.casefold() == "path"),
        "",
    )
    value = shutil.which("node.exe" if os.name == "nt" else "node", path=path_value)
    if not value:
        raise HarnessError("NODE_RUNTIME")
    path = Path(value)
    try:
        resolved = path.resolve(strict=True)
    except OSError:
        raise HarnessError("NODE_RUNTIME") from None
    if not resolved.is_file() or trusted._is_reparse_or_link(path):
        raise HarnessError("NODE_RUNTIME")
    return resolved


def _parse_driver_result(
    stdout: bytes,
    stderr: bytes,
    descriptor: Mapping[str, Any],
    secrets: Sequence[str],
) -> dict[str, Any]:
    if (
        len(stdout) > MAX_CHILD_OUTPUT_BYTES
        or len(stderr) > MAX_CHILD_OUTPUT_BYTES
        or _contains_secret(stdout + stderr, secrets)
    ):
        raise HarnessError("SCENARIO_OUTPUT")
    lines = stdout.splitlines()
    matches = [line for line in lines if line.startswith(DRIVER_RESULT_MARKER)]
    if len(matches) != 1:
        raise HarnessError("SCENARIO_RESULT")
    try:
        raw = matches[0][len(DRIVER_RESULT_MARKER) :]
        result = json.loads(
            raw.decode("utf-8"), object_pairs_hook=_pairs_without_duplicates
        )
    except (
        UnicodeDecodeError,
        json.JSONDecodeError,
        _DuplicateKeyError,
    ):
        raise HarnessError("SCENARIO_RESULT") from None
    assertions = result.get("assertions") if isinstance(result, dict) else None
    cleanup = result.get("cleanup") if isinstance(result, dict) else None
    if (
        not isinstance(result, dict)
        or _canonical(result) != raw
        or frozenset(result)
        != {
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
        or result.get("protocol") != DRIVER_RESULT_PROTOCOL
        or result.get("batchId") != descriptor["batchId"]
        or result.get("phase") != descriptor["phase"]
        or result.get("profileOrdinal") != descriptor.get("profileOrdinal")
        or result.get("status") != "Passed"
        or not isinstance(assertions, dict)
        or frozenset(assertions)
        != {
            "desktopUiObserved",
            "productBehaviorPassed",
            "recoveryListenerObserved",
            "details",
        }
        or assertions.get("desktopUiObserved") is not True
        or assertions.get("productBehaviorPassed") is not True
        or assertions.get("recoveryListenerObserved")
        is not (descriptor["phase"] == "provider-recovery")
        or not isinstance(assertions.get("details"), dict)
        or cleanup != {"status": "Passed", "processDelta": 0, "temporaryDelta": 0}
        or _contains_secret(raw, secrets)
    ):
        raise HarnessError("SCENARIO_RESULT")
    return result


def _run_scenario(
    *,
    scenario_path: Path,
    operator_root: Path,
    environment: Mapping[str, str],
    monitor_factory: Any,
    timeout: float,
    secrets: Sequence[str],
    descriptor: Mapping[str, Any],
) -> tuple[dict[str, Any], _ProcessMonitor]:
    with tempfile.TemporaryFile(dir=operator_root) as stdout_file, tempfile.TemporaryFile(
        dir=operator_root
    ) as stderr_file:
        try:
            process = subprocess.Popen(
                [
                    sys.executable,
                    "-I",
                    "-S",
                    "-E",
                    "-B",
                    "-X",
                    "utf8",
                    str(scenario_path),
                ],
                cwd=operator_root,
                env=dict(environment),
                stdin=subprocess.DEVNULL,
                stdout=stdout_file,
                stderr=stderr_file,
                shell=False,
                creationflags=(subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0),
                close_fds=True,
            )
        except OSError:
            raise HarnessError("SCENARIO_START") from None
        monitor = monitor_factory(process.pid)
        deadline = time.monotonic() + timeout
        while process.poll() is None:
            monitor.refresh()
            if (
                os.fstat(stdout_file.fileno()).st_size > MAX_CHILD_OUTPUT_BYTES
                or os.fstat(stderr_file.fileno()).st_size > MAX_CHILD_OUTPUT_BYTES
            ):
                process.kill()
                process.wait(timeout=10)
                raise HarnessError("SCENARIO_OUTPUT")
            if time.monotonic() >= deadline:
                process.kill()
                process.wait(timeout=10)
                raise HarnessError("SCENARIO_TIMEOUT")
            time.sleep(0.05)
        monitor.refresh()
        try:
            process.wait(timeout=10)
        except subprocess.SubprocessError:
            raise HarnessError("SCENARIO_EXIT") from None
        stdout_file.seek(0)
        stderr_file.seek(0)
        stdout = stdout_file.read(MAX_CHILD_OUTPUT_BYTES + 1)
        stderr = stderr_file.read(MAX_CHILD_OUTPUT_BYTES + 1)
        if process.returncode != 0:
            raise HarnessError("SCENARIO_EXIT")
    return _parse_driver_result(stdout, stderr, descriptor, secrets), monitor


def _write_evidence(
    control_root: Path,
    *,
    descriptor: Mapping[str, Any],
    observation_path: str,
    records: Sequence[Mapping[str, Any]],
    package: trusted.PackageTreeBinding,
    boundary: trusted.ProviderBoundaryBinding,
    inventory: Mapping[str, Any],
    launches: Mapping[str, Any],
    assertions: Mapping[str, Any],
) -> None:
    observation = {
        "schemaVersion": SCHEMA_VERSION,
        "protocol": OBSERVATION_PROTOCOL,
        "batchId": descriptor["batchId"],
        "gateId": descriptor["gateId"],
        "phase": descriptor["phase"],
        "productCandidate": descriptor["productCandidate"],
        "attemptId": descriptor["attemptId"],
        "runId": descriptor["runId"],
        "packageIdentityEvidence": descriptor["packageIdentityEvidence"],
        "providerBoundaryDecision": descriptor["providerBoundaryDecision"],
        "requests": list(records),
    }
    try:
        observation_raw = trusted._exclusive_write_json(
            control_root, observation_path, observation
        )
    except trusted.ExecutorError:
        raise HarnessError("OBSERVATION_WRITE") from None
    source = {
        "identityPath": package.path,
        "identityRawSha256": package.sha256,
        "packageRoot": package.package_root,
        "packageTreeRootSha256": package.tree_root_sha256,
        "entryCount": package.entry_count,
        "totalBytes": package.total_bytes,
        "entrypoint": package.entrypoint,
        "argv": list(package.argv),
        "appHostPath": package.apphost_path,
        "appHostSha256": package.apphost_sha256,
    }
    stage = {
        "packageTreeRootSha256": inventory["treeRootSha256"],
        "entryCount": inventory["entryCount"],
        "totalBytes": inventory["totalBytes"],
    }
    receipt = {
        "schemaVersion": SCHEMA_VERSION,
        "protocol": LAUNCH_RECEIPT_PROTOCOL,
        "status": "Passed",
        "goalId": GOAL_ID,
        "gateId": descriptor["gateId"],
        "productCandidate": descriptor["productCandidate"],
        "phase": descriptor["phase"],
        "profileOrdinal": descriptor.get("profileOrdinal"),
        "batchId": descriptor["batchId"],
        "attemptId": descriptor["attemptId"],
        "runId": descriptor["runId"],
        "runTokenSha256": descriptor["runTokenSha256"],
        "packageTreeRootSha256": package.tree_root_sha256,
        "boundaryDecision": boundary.descriptor_binding(),
        "sourcePackage": source,
        "stagedPackageBefore": stage,
        "stagedPackageAfter": stage,
        "launches": dict(launches),
        "containment": {
            "attachedBeforeRelease": True,
            "processIsolated": False,
            "allEgressIsolated": False,
            "sandboxPolicySha256": None,
            "strongCanaryEvidence": None,
        },
        "observedRequestCount": len(records),
        "scenarioAssertions": dict(assertions),
        "cleanup": {"status": "Passed", "processDelta": 0, "temporaryDelta": 0},
        "gatewayObservation": {
            "path": observation_path,
            "rawSha256": hashlib.sha256(observation_raw).hexdigest(),
            "requestCount": len(records),
            "reservationIds": [
                item["reservationId"] for item in descriptor["reservations"]
            ],
        },
        "scenarioSource": descriptor["scenarioSource"],
        "driverSource": descriptor["driverSource"],
    }
    try:
        trusted._exclusive_write_json(
            control_root, descriptor["packageLaunchReceiptPath"], receipt
        )
    except trusted.ExecutorError:
        raise HarnessError("LAUNCH_RECEIPT_WRITE") from None


def _run(
    candidate_root: Path,
    control_root: Path,
    descriptor: Mapping[str, Any],
    observation_path: str,
    package: trusted.PackageTreeBinding,
    boundary: trusted.ProviderBoundaryBinding,
    inventory: Mapping[str, Any],
    model: str,
    base_url: str,
    api_key: str,
    run_token: str,
) -> None:
    staging_relative = str(descriptor["stagingRelativeRoot"])
    staging_parent_relative = PurePosixPath(staging_relative).parent.as_posix()
    staging_parent = _mkdir_chain(control_root, staging_parent_relative)
    staging_root = control_root.joinpath(*PurePosixPath(staging_relative).parts)
    if staging_root.exists() or staging_root.is_symlink():
        raise HarnessError("STAGING_EXISTS")
    try:
        os.mkdir(staging_root)
    except OSError:
        raise HarnessError("STAGING_PATH") from None
    controlled_workspace: Path | None = None
    monitor: _ProcessMonitor | None = None
    job: _JobMembership | None = None
    server: _GatewayServer | None = None
    server_thread: threading.Thread | None = None
    completed = False
    try:
        package_root = staging_root / "package"
        operator_root = staging_root / "operator"
        os.mkdir(operator_root)
        source_package_root = candidate_root.joinpath(
            *PurePosixPath(package.package_root).parts
        )
        _copy_package(source_package_root, package_root, inventory)
        scenario_raw = trusted._read_fixed_bytes(
            candidate_root, descriptor["scenarioPath"], limit=MAX_JSON_BYTES
        )
        driver_raw = trusted._read_fixed_bytes(
            candidate_root, descriptor["driverPath"], limit=MAX_JSON_BYTES
        )
        if (
            hashlib.sha256(scenario_raw).hexdigest()
            != descriptor["scenarioSource"]["sha256"]
            or hashlib.sha256(driver_raw).hexdigest()
            != descriptor["driverSource"]["sha256"]
        ):
            raise HarnessError("STAGING_SOURCE")
        scenario_path = operator_root / "scenario.py"
        driver_path = operator_root / "driver.mjs"
        _write_exclusive(scenario_path, scenario_raw)
        _write_exclusive(driver_path, driver_raw)
        controlled_workspace = _controlled_workspace(
            candidate_root, control_root, descriptor
        )
        workspace_path = controlled_workspace or (operator_root / "workspace")
        if controlled_workspace is None:
            os.mkdir(workspace_path)
        markers = [
            (
                f"CAICLI-W84-92-TURN:{descriptor['batchId']}:{index}:"
                f"{reservation['reservationId']}"
            )
            for index, reservation in enumerate(
                descriptor["reservations"], start=1
            )
        ]
        scenario_input = {
            "protocol": SCENARIO_INPUT_PROTOCOL,
            "batchId": descriptor["batchId"],
            "phase": descriptor["phase"],
            "profileOrdinal": descriptor.get("profileOrdinal"),
            "driverPath": str(driver_path.resolve(strict=True)),
            "driverSha256": hashlib.sha256(driver_raw).hexdigest(),
            "stagedPackageRoot": str(package_root.resolve(strict=True)),
            "entrypoint": package.entrypoint,
            "argv": list(package.argv),
            "appHostPath": package.apphost_path,
            "workspacePath": str(workspace_path.resolve(strict=True)),
            "candidatePackageJsonPath": str(
                (candidate_root / "apps" / "desktop" / "package.json").resolve(
                    strict=True
                )
            ),
            "requestCount": len(descriptor["reservations"]),
            "requestMarkers": markers,
        }
        _write_exclusive(
            operator_root / "scenario-input.json", _canonical(scenario_input)
        )
        upstream = _parse_upstream(base_url, api_key)
        job = _JobMembership(run_token)
        desktop_path = package_root.joinpath(
            *PurePosixPath(package.entrypoint).parts
        )
        apphost_path = package_root.joinpath(
            *PurePosixPath(package.apphost_path).parts
        )
        state = _GatewayState(
            descriptor=descriptor,
            model=model,
            api_key=api_key,
            run_token=run_token,
            upstream=upstream,
            monitor=None,
        )
        server = _GatewayServer(("127.0.0.1", 0), _handler_type(state))
        local_base = (
            f"http://127.0.0.1:{server.server_address[1]}{LOCAL_API_PREFIX}"
        )
        node = _find_node(os.environ)
        environment = _scenario_environment(
            model=model,
            local_base_url=local_base,
            run_token=run_token,
            node=node,
        )

        def make_monitor(scenario_pid: int) -> _ProcessMonitor:
            monitor_value = _ProcessMonitor(
                scenario_pid=scenario_pid,
                desktop_path=desktop_path,
                apphost_path=apphost_path,
                job=job,
            )
            state.monitor = monitor_value
            return monitor_value

        server_thread = threading.Thread(
            target=server.serve_forever,
            name="caicli-provider-gateway",
            daemon=False,
        )
        server_thread.start()
        result, monitor = _run_scenario(
            scenario_path=scenario_path,
            operator_root=operator_root,
            environment=environment,
            monitor_factory=make_monitor,
            timeout=SCENARIO_TIMEOUT_SECONDS,
            secrets=(api_key, run_token),
            descriptor=descriptor,
        )
        server.shutdown()
        server.server_close()
        server_thread.join(timeout=10)
        if server_thread.is_alive():
            raise HarnessError("GATEWAY_SHUTDOWN")
        server = None
        records = state.accepted_records()
        launches = monitor.finalize(result)
        if monitor.process_delta() != 0:
            raise HarnessError("SCENARIO_PROCESS_DELTA")
        try:
            staged_after = trusted._package_inventory(package_root)
        except trusted.ExecutorError:
            raise HarnessError("STAGING_AFTER") from None
        if staged_after != inventory:
            raise HarnessError("STAGING_AFTER")
        assertions = dict(result["assertions"])
        assertions["details"] = dict(assertions["details"])
        if controlled_workspace is not None:
            assertions["details"].update(
                _verify_controlled_transition(
                    controlled_workspace,
                    secrets=(api_key, run_token),
                )
            )
        if not _remove_tree(staging_root, allowed_parent=staging_parent):
            raise HarnessError("STAGING_CLEANUP")
        if controlled_workspace is not None:
            controlled_parent = controlled_workspace.parent
            if not _remove_tree(
                controlled_workspace, allowed_parent=controlled_parent
            ):
                raise HarnessError("CONTROLLED_CLEANUP")
            controlled_workspace = None
        completed = True
        _write_evidence(
            control_root,
            descriptor=descriptor,
            observation_path=observation_path,
            records=records,
            package=package,
            boundary=boundary,
            inventory=inventory,
            launches=launches,
            assertions=assertions,
        )
    finally:
        if server is not None:
            try:
                server.shutdown()
                server.server_close()
            except OSError:
                pass
        if server_thread is not None and server_thread.is_alive():
            server_thread.join(timeout=10)
        if monitor is not None:
            monitor.close()
        if job is not None:
            job.close()
        if not completed and staging_root.exists() and staging_root.parent == staging_parent:
            _remove_tree(staging_root, allowed_parent=staging_parent)
        if controlled_workspace is not None and controlled_workspace.exists():
            _remove_tree(
                controlled_workspace, allowed_parent=controlled_workspace.parent
            )


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--descriptor", required=True)
    parser.add_argument("--observation", required=True)
    try:
        arguments = parser.parse_args(argv)
        candidate_root = trusted._normalise_repo_root(Path.cwd())
        context = trusted._repository_context(candidate_root)
        control_root = context.control_root
        descriptor_path = _relative(arguments.descriptor)
        observation_path = _relative(arguments.observation)
        descriptor, _raw = _read_json(
            control_root, descriptor_path, "DESCRIPTOR_JSON"
        )
        _descriptor_shape(
            descriptor,
            descriptor_path=descriptor_path,
            observation_path=observation_path,
        )
        package, boundary, inventory = _preflight(
            candidate_root, control_root, descriptor
        )
        model, base_url, api_key, run_token = _provider_environment(descriptor)
        _run(
            candidate_root,
            control_root,
            descriptor,
            observation_path,
            package,
            boundary,
            inventory,
            model,
            base_url,
            api_key,
            run_token,
        )
        return 0
    except (
        HarnessError,
        trusted.ExecutorError,
        OSError,
        ValueError,
        TypeError,
    ) as error:
        code = error.code if isinstance(error, HarnessError) else "FAIL_CLOSED"
        exit_code = error.exit_code if isinstance(error, HarnessError) else 79
        sys.stderr.write(f"provider harness failure [{code}]\n")
        return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
