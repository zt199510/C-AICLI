"""Append-only integrity checks for the Week84-92 Goal evidence.

This module is intentionally independent from
``validate-week84-92-goal-evidence.py`` and uses only the Python standard
library.  Public validation functions accept ``pathlib.Path`` values and
return ``IntegrityIssue`` objects.  Issues contain only stable error codes and
repository-relative locations; parsed values and exception text are never
included, so a malformed file cannot make validation echo a secret.

Canonical JSON
==============

Entry hashes use UTF-8 JSON with ``ensure_ascii=False``, ``sort_keys=True`` and
``separators=(",", ":")``.  ``entrySha256`` is removed before hashing.

``first-failure-ledger.json`` has exactly these top-level fields::

    schemaVersion, goalId, hashAlgorithm, entryCount,
    lastEntrySha256, entries

Each failure entry has exactly::

    sequence, failureId, observedAt, checkpoint, lane, gateId, phase,
    classification, summary, evidenceRefs, previousEntrySha256,
    entrySha256

``user-decision-ledger.json`` uses the same top-level envelope.  Each decision
entry has exactly::

    sequence, decisionRequestId, requestCommit, acceptanceId, candidate, manifestSha256,
    challengeCode, userResponseCanonical, userMessageSha256, decidedAt,
    status, confirmedBy, previousEntrySha256, entrySha256

The three acceptance IDs are fixed to W86, W89 and W92.  ``confirmedBy``
must be ``User`` and status is ``Passed`` or ``Failed``.  The last entry for an
acceptance in the applicable ledger prefix is authoritative: a later
``Failed`` decision revokes an earlier ``Passed`` decision.  Before its
checkpoint an acceptance in a handoff must remain ``NotRun``.  At or after its
checkpoint, a handoff must exactly match the latest decision in the ledger
prefix frozen by its pre-handoff snapshot; the acceptance at the handoff's own
checkpoint is also bound to that handoff's product candidate.  The central
Goal must match the latest decision in the complete current ledger.

Every decision also requires a pre-committed challenge request at
``docs_md/weekly/84_92_user_acceptance_requests/<decisionRequestId>.json``.
The request has exactly ``schemaVersion``, ``goalId``, ``acceptanceId``,
``checkpoint``, ``decisionRequestId``, ``candidate``, ``manifestSha256``,
``challengeCode``, ``requestedAt`` and ``status``.  The decision's
``requestCommit`` must equal that request's unique first-add commit.  Its first-add Git blob,
index blob and current bytes must be identical, and the candidate must be a
strict ancestor of the request's first-add commit.  The user interaction
contract requires the reply to echo ``challengeCode``.  These checks prove
only that an immutable, candidate-bound request preceded the recorded
decision; they do not cryptographically prove who authored a chat message.

Pre-handoff ``goal-control-snapshot.json`` files are located beside their
handoff.  A handoff binds the snapshot's raw SHA-256.  The snapshot's
first-failure ledger count/last hash must be a real prefix of the current
ledger, and its provider ledger fields, active checkpoint, product candidate
and checkpoint commit must agree with the handoff.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from functools import wraps
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
from typing import Any, Iterable, Mapping, Sequence

try:
    from tools import week84_92_trusted_executor as trusted_executor
except ImportError:  # direct execution from the tools directory
    import week84_92_trusted_executor as trusted_executor


SCHEMA_VERSION = "1.0.0"
GOAL_ID = "c-aicli-cli-desktop-refactor-w84-w92"
HASH_ALGORITHM = "sha256-canonical-json-v1"
FAILURE_LEDGER_PATH = (
    "artifacts/week84-92-goal-control/first-failure-ledger.json"
)
USER_DECISION_LEDGER_PATH = (
    "artifacts/week84-92-goal-control/user-decision-ledger.json"
)
MAX_JSON_BYTES = 16 * 1024 * 1024
HEX_SHA256 = re.compile(r"^[0-9a-f]{64}$")
GATE_ID = re.compile(r"^W(8[4-9]|9[0-2])-[GRC][0-9]+$")
CHECKPOINT = re.compile(r"^W(8[4-9]|9[0-2])$")
LANES = {
    "baseline",
    "renderer",
    "cli",
    "integration",
    "hardening",
    "acceptance",
}
CLASSIFICATIONS = {
    "Product",
    "Test",
    "Harness",
    "Observer",
    "Environment",
    "Authorization",
    "Unknown",
}
ACCEPTANCE_WEEKS = {
    "W86-USER-VISUAL": 86,
    "W89-USER-VISUAL": 89,
    "W92-USER-VISUAL": 92,
}
USER_ACCEPTANCE_REQUEST_ROOT = PurePosixPath(
    "docs_md/weekly/84_92_user_acceptance_requests"
)
DECISION_REQUEST_ID = re.compile(r"^UA-[A-Z0-9][A-Z0-9-]{2,63}$")
CHALLENGE_CODE = re.compile(r"^CH-[A-Z0-9]{8,32}$")
REQUEST_FIELDS = {
    "schemaVersion",
    "goalId",
    "acceptanceId",
    "checkpoint",
    "decisionRequestId",
    "candidate",
    "manifestSha256",
    "challengeCode",
    "requestedAt",
    "status",
}
ENVELOPE_FIELDS = {
    "schemaVersion",
    "goalId",
    "hashAlgorithm",
    "entryCount",
    "lastEntrySha256",
    "entries",
}
FAILURE_ENTRY_FIELDS = {
    "sequence",
    "failureId",
    "observedAt",
    "checkpoint",
    "lane",
    "gateId",
    "phase",
    "classification",
    "summary",
    "evidenceRefs",
    "previousEntrySha256",
    "entrySha256",
}
DECISION_ENTRY_FIELDS = {
    "sequence",
    "decisionRequestId",
    "requestCommit",
    "acceptanceId",
    "candidate",
    "manifestSha256",
    "challengeCode",
    "userResponseCanonical",
    "userMessageSha256",
    "decidedAt",
    "status",
    "confirmedBy",
    "previousEntrySha256",
    "entrySha256",
}


def canonical_user_response(
    decision_request_id: str,
    challenge_code: str,
    status: str,
) -> str:
    """Return the exact user echo string whose UTF-8 bytes are hashed."""

    return f"{decision_request_id} {challenge_code} {status}"


@dataclass(frozen=True, order=True)
class IntegrityIssue:
    """A redacted, deterministic integrity failure."""

    code: str
    location: str

    def __str__(self) -> str:
        return f"[{self.code}] {self.location}"


@dataclass(frozen=True)
class LedgerView:
    """Validated ledger facts used by the cross-file checks."""

    raw_sha256: str
    entries: tuple[Mapping[str, Any], ...]
    entry_hashes: tuple[str, ...]

    @property
    def entry_count(self) -> int:
        return len(self.entries)

    @property
    def last_entry_sha256(self) -> str | None:
        return self.entry_hashes[-1] if self.entry_hashes else None


class _DuplicateKey(ValueError):
    pass


def canonical_json_bytes(value: Any) -> bytes:
    """Return the frozen canonical JSON encoding."""

    return json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")


def canonical_entry_sha256(entry: Mapping[str, Any]) -> str:
    """Hash an entry after removing its own hash field."""

    payload = dict(entry)
    payload.pop("entrySha256", None)
    return hashlib.sha256(canonical_json_bytes(payload)).hexdigest()


def raw_sha256(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def _object_no_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise _DuplicateKey
        result[key] = value
    return result


def _label(path: Path, repo_root: Path) -> str:
    try:
        return path.resolve().relative_to(repo_root.resolve()).as_posix()
    except (OSError, ValueError):
        return "<outside-repository>"


def _inside_repo(path: Path, repo_root: Path) -> bool:
    try:
        path.resolve().relative_to(repo_root.resolve())
        return True
    except (OSError, ValueError):
        return False


def _read_json(
    path: Path,
    repo_root: Path,
) -> tuple[Any | None, bytes | None, list[IntegrityIssue]]:
    location = _label(path, repo_root)
    if not _inside_repo(path, repo_root):
        return None, None, [IntegrityIssue("PATH_OUTSIDE_REPOSITORY", location)]
    try:
        payload = path.read_bytes()
    except OSError:
        return None, None, [IntegrityIssue("FILE_UNREADABLE", location)]
    if len(payload) > MAX_JSON_BYTES:
        return None, None, [IntegrityIssue("FILE_TOO_LARGE", location)]
    try:
        value = json.loads(
            payload.decode("utf-8"),
            object_pairs_hook=_object_no_duplicates,
        )
    except (UnicodeDecodeError, json.JSONDecodeError, _DuplicateKey):
        return None, None, [IntegrityIssue("JSON_INVALID", location)]
    return value, payload, []


def _is_sha(value: Any) -> bool:
    return isinstance(value, str) and HEX_SHA256.fullmatch(value) is not None


def _is_nonempty(value: Any) -> bool:
    return isinstance(value, str) and bool(value)


def _is_relative_reference(value: Any) -> bool:
    if not _is_nonempty(value):
        return False
    path = PurePosixPath(str(value).replace("\\", "/"))
    return not path.is_absolute() and ".." not in path.parts


def _git_bytes(
    repo_root: Path,
    arguments: Sequence[str],
) -> tuple[int | None, bytes | None]:
    try:
        command = trusted_executor._git_command(repo_root, arguments)
        executable, before_identity = trusted_executor._trusted_git_identity(
            repo_root
        )
        completed = subprocess.run(
            command,
            cwd=repo_root,
            env=trusted_executor._git_environment(executable),
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            shell=False,
            check=False,
            timeout=10,
        )
        after_executable, after_identity = (
            trusted_executor._trusted_git_identity(repo_root)
        )
        if executable != after_executable or before_identity != after_identity:
            return None, None
    except (
        OSError,
        subprocess.SubprocessError,
        trusted_executor.ExecutorError,
    ):
        return None, None
    return completed.returncode, completed.stdout


def _trusted_git_validation_bundle(function):
    """Raw-pin Git across one complete integrity validation invocation."""

    @wraps(function)
    def guarded(*args, **kwargs):
        root_value = kwargs.get("repo_root", args[0] if args else None)
        root = Path(root_value).resolve()
        try:
            before_path, before_identity = trusted_executor._trusted_git_identity(
                root, rehash=True
            )
        except (trusted_executor.ExecutorError, OSError):
            return [IntegrityIssue("TRUSTED_GIT_TOOL_IDENTITY", "trusted-git")]
        try:
            result = function(*args, **kwargs)
        finally:
            try:
                after_path, after_identity = (
                    trusted_executor._trusted_git_identity(root, rehash=True)
                )
            except (trusted_executor.ExecutorError, OSError):
                after_path, after_identity = None, None
        if after_path != before_path or after_identity != before_identity:
            result = list(result)
            result.append(
                IntegrityIssue("TRUSTED_GIT_TOOL_IDENTITY", "trusted-git")
            )
        return sorted(set(result))

    return guarded


def _is_reparse(path: Path) -> bool:
    try:
        attributes = getattr(os.lstat(path), "st_file_attributes", 0)
    except OSError:
        return True
    return path.is_symlink() or bool(attributes & 0x400)


def _expected_git_directory(repo_root: Path) -> Path | None:
    control = repo_root / ".git"
    if _is_reparse(control):
        return None
    if control.is_dir():
        return control.resolve()
    if not control.is_file():
        return None
    try:
        raw = control.read_bytes()
        if len(raw) > 4096 or b"\x00" in raw:
            return None
        line = raw.decode("utf-8").strip()
    except (OSError, UnicodeError):
        return None
    if not line.startswith("gitdir: ") or "\n" in line or "\r" in line:
        return None
    target = Path(line[8:])
    if not target.is_absolute():
        target = control.parent / target
    try:
        resolved = target.resolve(strict=True)
    except (OSError, RuntimeError):
        return None
    if not resolved.is_dir() or _is_reparse(resolved):
        return None
    return resolved


def _git_repository_is_trusted(repo_root: Path) -> bool:
    expected_git = _expected_git_directory(repo_root)
    if expected_git is None:
        return False
    queries = {
        "top": ("rev-parse", "--show-toplevel"),
        "git": ("rev-parse", "--absolute-git-dir"),
        "common": ("rev-parse", "--git-common-dir"),
        "format": ("rev-parse", "--show-object-format"),
        "shallow": ("rev-parse", "--is-shallow-repository"),
        "replace": ("for-each-ref", "--format=%(refname)", "refs/replace/"),
    }
    values: dict[str, str] = {}
    for name, arguments in queries.items():
        code, raw = _git_bytes(repo_root, arguments)
        if code != 0 or raw is None:
            return False
        try:
            values[name] = raw.decode("utf-8").strip()
        except UnicodeError:
            return False
    try:
        top = Path(values["top"]).resolve(strict=True)
        actual_git = Path(values["git"]).resolve(strict=True)
        common_value = Path(values["common"])
        common = (
            common_value
            if common_value.is_absolute()
            else repo_root / common_value
        ).resolve(strict=True)
    except (OSError, RuntimeError, ValueError):
        return False
    if (
        top != repo_root
        or actual_git != expected_git
        or not common.is_dir()
        or _is_reparse(common)
        or values["format"] != "sha1"
        or values["shallow"] != "false"
        or values["replace"]
        or (common / "shallow").exists()
        or (common / "info" / "grafts").exists()
        or (common / "objects" / "info" / "alternates").exists()
    ):
        return False
    return True


def _request_relative_path(decision_request_id: Any) -> PurePosixPath | None:
    if (
        not isinstance(decision_request_id, str)
        or DECISION_REQUEST_ID.fullmatch(decision_request_id) is None
    ):
        return None
    return USER_ACCEPTANCE_REQUEST_ROOT / f"{decision_request_id}.json"


def _timestamp(value: Any) -> datetime | None:
    if not isinstance(value, str) or not value:
        return None
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    return parsed if parsed.utcoffset() is not None else None


def _mapping(value: Any) -> Mapping[str, Any]:
    return value if isinstance(value, Mapping) else {}


def _validate_envelope(
    data: Any,
    location: str,
    entry_fields: set[str],
) -> tuple[list[Mapping[str, Any]], list[IntegrityIssue]]:
    issues: list[IntegrityIssue] = []
    if not isinstance(data, Mapping):
        return [], [IntegrityIssue("LEDGER_NOT_OBJECT", location)]
    if set(data) != ENVELOPE_FIELDS:
        issues.append(IntegrityIssue("LEDGER_TOP_FIELDS", location))
    if data.get("schemaVersion") != SCHEMA_VERSION:
        issues.append(IntegrityIssue("LEDGER_SCHEMA_VERSION", location))
    if data.get("goalId") != GOAL_ID:
        issues.append(IntegrityIssue("LEDGER_GOAL_ID", location))
    if data.get("hashAlgorithm") != HASH_ALGORITHM:
        issues.append(IntegrityIssue("LEDGER_HASH_ALGORITHM", location))
    raw_entries = data.get("entries")
    if not isinstance(raw_entries, list):
        issues.append(IntegrityIssue("LEDGER_ENTRIES_TYPE", location))
        return [], issues
    entries = [entry for entry in raw_entries if isinstance(entry, Mapping)]
    if len(entries) != len(raw_entries):
        issues.append(IntegrityIssue("LEDGER_ENTRY_TYPE", location))
    if data.get("entryCount") != len(raw_entries):
        issues.append(IntegrityIssue("LEDGER_ENTRY_COUNT", location))

    previous: str | None = None
    hashes: list[str] = []
    for index, entry in enumerate(entries, start=1):
        entry_location = f"{location}#/entries/{index - 1}"
        if set(entry) != entry_fields:
            issues.append(IntegrityIssue("LEDGER_ENTRY_FIELDS", entry_location))
        if entry.get("sequence") != index:
            issues.append(IntegrityIssue("LEDGER_SEQUENCE", entry_location))
        if entry.get("previousEntrySha256") != previous:
            issues.append(IntegrityIssue("LEDGER_PREVIOUS_HASH", entry_location))
        declared = entry.get("entrySha256")
        calculated = canonical_entry_sha256(entry)
        if not _is_sha(declared) or declared != calculated:
            issues.append(IntegrityIssue("LEDGER_ENTRY_HASH", entry_location))
        previous = declared if _is_sha(declared) else calculated
        hashes.append(calculated)
    expected_last = hashes[-1] if hashes else None
    if data.get("lastEntrySha256") != expected_last:
        issues.append(IntegrityIssue("LEDGER_LAST_HASH", location))
    return entries, issues


def _validate_failure_entries(
    entries: Sequence[Mapping[str, Any]],
    location: str,
) -> list[IntegrityIssue]:
    issues: list[IntegrityIssue] = []
    failure_ids: set[str] = set()
    for index, entry in enumerate(entries):
        item_location = f"{location}#/entries/{index}"
        failure_id = entry.get("failureId")
        if not _is_nonempty(failure_id) or failure_id in failure_ids:
            issues.append(IntegrityIssue("FAILURE_ID", item_location))
        elif isinstance(failure_id, str):
            failure_ids.add(failure_id)
        checkpoint = entry.get("checkpoint")
        gate_id = entry.get("gateId")
        if not isinstance(checkpoint, str) or CHECKPOINT.fullmatch(checkpoint) is None:
            issues.append(IntegrityIssue("FAILURE_CHECKPOINT", item_location))
        if not isinstance(gate_id, str) or GATE_ID.fullmatch(gate_id) is None:
            issues.append(IntegrityIssue("FAILURE_GATE_ID", item_location))
        elif isinstance(checkpoint, str) and not gate_id.startswith(f"{checkpoint}-"):
            issues.append(IntegrityIssue("FAILURE_GATE_CHECKPOINT", item_location))
        if entry.get("lane") not in LANES:
            issues.append(IntegrityIssue("FAILURE_LANE", item_location))
        for field in ("observedAt", "phase", "summary"):
            if not _is_nonempty(entry.get(field)):
                issues.append(
                    IntegrityIssue(f"FAILURE_{field.upper()}", item_location)
                )
        if entry.get("classification") not in CLASSIFICATIONS:
            issues.append(IntegrityIssue("FAILURE_CLASSIFICATION", item_location))
        refs = entry.get("evidenceRefs")
        if (
            not isinstance(refs, list)
            or not refs
            or len(refs) != len(set(refs))
            or any(not _is_relative_reference(ref) for ref in refs)
        ):
            issues.append(IntegrityIssue("FAILURE_EVIDENCE_REFS", item_location))
    return issues


def load_first_failure_ledger(
    repo_root: Path,
    ledger_path: Path,
) -> tuple[LedgerView | None, list[IntegrityIssue]]:
    """Load and strictly validate the current first-failure ledger."""

    data, payload, issues = _read_json(ledger_path, repo_root)
    location = _label(ledger_path, repo_root)
    if issues:
        return None, issues
    entries, envelope_issues = _validate_envelope(
        data,
        location,
        FAILURE_ENTRY_FIELDS,
    )
    issues.extend(envelope_issues)
    issues.extend(_validate_failure_entries(entries, location))
    if issues or payload is None:
        return None, sorted(set(issues))
    hashes = tuple(canonical_entry_sha256(entry) for entry in entries)
    return LedgerView(raw_sha256(payload), tuple(entries), hashes), []


def _failure_projection(value: Mapping[str, Any]) -> dict[str, Any]:
    return {
        "failureId": value.get("failureId"),
        "observedAt": value.get("observedAt"),
        "checkpoint": value.get("checkpoint"),
        "lane": value.get("lane"),
        "gateId": value.get("gateId"),
        "phase": value.get("phase"),
        "classification": value.get("classification"),
        "summary": value.get("summary"),
        "evidenceRefs": value.get("evidenceRefs"),
    }


def validate_first_failure_integrity(
    repo_root: Path,
    goal_state_path: Path,
    ledger_path: Path,
    gate_paths: Sequence[Path],
) -> tuple[LedgerView | None, list[IntegrityIssue]]:
    """Validate raw central binding and the Gate↔ledger bijection."""

    view, issues = load_first_failure_ledger(repo_root, ledger_path)
    goal, _goal_payload, goal_issues = _read_json(goal_state_path, repo_root)
    issues.extend(goal_issues)
    if view is None or not isinstance(goal, Mapping):
        return view, sorted(set(issues))

    location = _label(goal_state_path, repo_root)
    binding = _mapping(goal.get("firstFailureLedger"))
    expected_binding = {
        "path": FAILURE_LEDGER_PATH,
        "sha256": view.raw_sha256,
        "entryCount": view.entry_count,
        "lastEntrySha256": view.last_entry_sha256,
    }
    if set(binding) != set(expected_binding):
        issues.append(IntegrityIssue("FAILURE_CENTRAL_BINDING_FIELDS", location))
    for field, expected in expected_binding.items():
        if binding.get(field) != expected:
            issues.append(
                IntegrityIssue(f"FAILURE_CENTRAL_{field.upper()}", location)
            )

    ledger_by_id = {
        str(entry.get("failureId")): _failure_projection(entry)
        for entry in view.entries
    }
    gate_by_id: dict[str, dict[str, Any]] = {}
    for gate_path in gate_paths:
        gate, _payload, gate_issues = _read_json(gate_path, repo_root)
        issues.extend(gate_issues)
        gate_location = _label(gate_path, repo_root)
        if not isinstance(gate, Mapping):
            continue
        failure = gate.get("firstFailure")
        if failure is None:
            continue
        if not isinstance(failure, Mapping):
            issues.append(IntegrityIssue("GATE_FIRST_FAILURE_TYPE", gate_location))
            continue
        projection = {
            "failureId": failure.get("failureId"),
            "observedAt": failure.get("observedAt"),
            "checkpoint": gate.get("checkpoint"),
            "lane": gate.get("lane"),
            "gateId": gate.get("gateId"),
            "phase": failure.get("phase"),
            "classification": failure.get("classification"),
            "summary": failure.get("summary"),
            "evidenceRefs": failure.get("evidenceRefs"),
        }
        failure_id = projection["failureId"]
        if not isinstance(failure_id, str) or failure_id in gate_by_id:
            issues.append(IntegrityIssue("GATE_FAILURE_ID", gate_location))
            continue
        gate_by_id[failure_id] = projection
    if set(gate_by_id) != set(ledger_by_id):
        issues.append(IntegrityIssue("FAILURE_GATE_LEDGER_SET", location))
    for failure_id in set(gate_by_id) & set(ledger_by_id):
        if gate_by_id[failure_id] != ledger_by_id[failure_id]:
            issues.append(IntegrityIssue("FAILURE_GATE_LEDGER_BINDING", location))
    return view, sorted(set(issues))


def _safe_bound_path(
    repo_root: Path,
    value: Any,
    owner_path: Path,
) -> Path | None:
    if not _is_relative_reference(value):
        return None
    resolved = (repo_root / PurePosixPath(str(value))).resolve()
    if not _inside_repo(resolved, repo_root):
        return None
    if resolved.name != "goal-control-snapshot.json":
        return None
    if resolved.parent != owner_path.resolve().parent:
        return None
    return resolved


def validate_handoff_snapshots(
    repo_root: Path,
    handoff_paths: Sequence[Path],
    failure_ledger: LedgerView,
    user_decision_ledger: LedgerView | None = None,
) -> list[IntegrityIssue]:
    """Validate every handoff's pre-handoff snapshot and ledger prefix."""

    issues: list[IntegrityIssue] = []
    for handoff_path in handoff_paths:
        handoff, _payload, handoff_issues = _read_json(handoff_path, repo_root)
        issues.extend(handoff_issues)
        location = _label(handoff_path, repo_root)
        if not isinstance(handoff, Mapping):
            continue
        binding = _mapping(handoff.get("goalControlBinding"))
        snapshot_path = _safe_bound_path(
            repo_root,
            binding.get("path"),
            handoff_path,
        )
        if snapshot_path is None:
            issues.append(IntegrityIssue("SNAPSHOT_PATH", location))
            continue
        snapshot, snapshot_payload, snapshot_issues = _read_json(
            snapshot_path,
            repo_root,
        )
        issues.extend(snapshot_issues)
        if not isinstance(snapshot, Mapping) or snapshot_payload is None:
            continue
        if binding.get("sha256") != raw_sha256(snapshot_payload):
            issues.append(IntegrityIssue("SNAPSHOT_RAW_HASH", location))

        failure_binding = _mapping(snapshot.get("firstFailureLedger"))
        count = failure_binding.get("entryCount")
        last = failure_binding.get("lastEntrySha256")
        if set(failure_binding) != {
            "path",
            "sha256",
            "entryCount",
            "lastEntrySha256",
        }:
            issues.append(
                IntegrityIssue("SNAPSHOT_FAILURE_BINDING_FIELDS", location)
            )
        if (
            not isinstance(count, int)
            or isinstance(count, bool)
            or count < 0
            or count > failure_ledger.entry_count
        ):
            issues.append(IntegrityIssue("SNAPSHOT_FAILURE_PREFIX_COUNT", location))
        else:
            expected_last = (
                failure_ledger.entry_hashes[count - 1] if count else None
            )
            if last != expected_last:
                issues.append(
                    IntegrityIssue("SNAPSHOT_FAILURE_PREFIX_HASH", location)
                )
            snapshot_ledger_sha = failure_binding.get("sha256")
            if not _is_sha(snapshot_ledger_sha):
                issues.append(IntegrityIssue("SNAPSHOT_FAILURE_RAW_HASH", location))
            elif (
                count == failure_ledger.entry_count
                and snapshot_ledger_sha != failure_ledger.raw_sha256
            ):
                issues.append(IntegrityIssue("SNAPSHOT_FAILURE_RAW_HASH", location))
        if failure_binding.get("path") != FAILURE_LEDGER_PATH:
            issues.append(IntegrityIssue("SNAPSHOT_FAILURE_PATH", location))

        if user_decision_ledger is not None:
            decision_binding = _mapping(snapshot.get("userDecisionLedger"))
            decision_count = decision_binding.get("entryCount")
            decision_last = decision_binding.get("lastEntrySha256")
            if set(decision_binding) != {
                "path",
                "sha256",
                "entryCount",
                "lastEntrySha256",
            }:
                issues.append(
                    IntegrityIssue("SNAPSHOT_DECISION_BINDING_FIELDS", location)
                )
            if (
                not isinstance(decision_count, int)
                or isinstance(decision_count, bool)
                or decision_count < 0
                or decision_count > user_decision_ledger.entry_count
            ):
                issues.append(
                    IntegrityIssue("SNAPSHOT_DECISION_PREFIX_COUNT", location)
                )
            else:
                expected_decision_last = (
                    user_decision_ledger.entry_hashes[decision_count - 1]
                    if decision_count
                    else None
                )
                if decision_last != expected_decision_last:
                    issues.append(
                        IntegrityIssue("SNAPSHOT_DECISION_PREFIX_HASH", location)
                    )
                decision_raw = decision_binding.get("sha256")
                if not _is_sha(decision_raw):
                    issues.append(
                        IntegrityIssue("SNAPSHOT_DECISION_RAW_HASH", location)
                    )
                elif (
                    decision_count == user_decision_ledger.entry_count
                    and decision_raw != user_decision_ledger.raw_sha256
                ):
                    issues.append(
                        IntegrityIssue("SNAPSHOT_DECISION_RAW_HASH", location)
                    )
            if decision_binding.get("path") != USER_DECISION_LEDGER_PATH:
                issues.append(IntegrityIssue("SNAPSHOT_DECISION_PATH", location))

        provider = _mapping(
            _mapping(snapshot.get("authorization")).get("provider")
        )
        provider_pairs = {
            "ledgerPath": "providerLedgerPath",
            "ledgerSha256": "providerLedgerSha256",
            "ledgerSequence": "providerLedgerSequence",
        }
        for snapshot_field, handoff_field in provider_pairs.items():
            if provider.get(snapshot_field) != binding.get(handoff_field):
                issues.append(
                    IntegrityIssue(
                        f"SNAPSHOT_PROVIDER_{snapshot_field.upper()}",
                        location,
                    )
                )

        snapshot_identity = _mapping(snapshot.get("identity"))
        handoff_identity = _mapping(handoff.get("identity"))
        for field in ("productCandidate", "checkpointCommit"):
            if snapshot_identity.get(field) != handoff_identity.get(field):
                issues.append(
                    IntegrityIssue(f"SNAPSHOT_IDENTITY_{field.upper()}", location)
                )
        active_checkpoint = _mapping(snapshot.get("execution")).get(
            "activeCheckpoint"
        )
        if active_checkpoint != handoff.get("checkpoint"):
            issues.append(IntegrityIssue("SNAPSHOT_CHECKPOINT", location))
        if snapshot.get("goalId") != handoff.get("goalId"):
            issues.append(IntegrityIssue("SNAPSHOT_GOAL_ID", location))
    return sorted(set(issues))


def _validate_decision_entries(
    entries: Sequence[Mapping[str, Any]],
    location: str,
) -> list[IntegrityIssue]:
    issues: list[IntegrityIssue] = []
    request_ids: set[str] = set()
    latest_status_by_acceptance: dict[str, str] = {}
    last_target_week = 0
    for index, entry in enumerate(entries):
        item_location = f"{location}#/entries/{index}"
        request_id = entry.get("decisionRequestId")
        if (
            not isinstance(request_id, str)
            or DECISION_REQUEST_ID.fullmatch(request_id) is None
            or request_id in request_ids
        ):
            issues.append(IntegrityIssue("DECISION_REQUEST_ID", item_location))
        else:
            request_ids.add(request_id)
        acceptance_id = entry.get("acceptanceId")
        if acceptance_id not in ACCEPTANCE_WEEKS:
            issues.append(IntegrityIssue("DECISION_ACCEPTANCE_ID", item_location))
        else:
            target_week = ACCEPTANCE_WEEKS[str(acceptance_id)]
            if target_week < last_target_week:
                issues.append(IntegrityIssue("DECISION_CHECKPOINT_ORDER", item_location))
            if target_week > last_target_week:
                prior_ids = {
                    item_id
                    for item_id, week in ACCEPTANCE_WEEKS.items()
                    if week < target_week
                }
                if any(
                    latest_status_by_acceptance.get(item_id) != "Passed"
                    for item_id in prior_ids
                ):
                    issues.append(
                        IntegrityIssue("DECISION_PRIOR_ACCEPTANCE", item_location)
                    )
                last_target_week = target_week
        if not re.fullmatch(r"[0-9a-f]{40}", str(entry.get("candidate", ""))):
            issues.append(IntegrityIssue("DECISION_CANDIDATE", item_location))
        for field in ("manifestSha256", "userMessageSha256"):
            if not _is_sha(entry.get(field)):
                issues.append(
                    IntegrityIssue(f"DECISION_{field.upper()}", item_location)
                )
        if entry.get("status") not in {"Passed", "Failed"}:
            issues.append(IntegrityIssue("DECISION_STATUS", item_location))
        elif acceptance_id in ACCEPTANCE_WEEKS:
            latest_status_by_acceptance[str(acceptance_id)] = str(
                entry.get("status")
            )
        challenge_code = entry.get("challengeCode")
        if (
            not isinstance(challenge_code, str)
            or CHALLENGE_CODE.fullmatch(challenge_code) is None
        ):
            issues.append(IntegrityIssue("DECISION_CHALLENGE", item_location))
        response = entry.get("userResponseCanonical")
        expected_response = (
            canonical_user_response(
                request_id,
                challenge_code,
                str(entry.get("status")),
            )
            if isinstance(request_id, str)
            and isinstance(challenge_code, str)
            and entry.get("status") in {"Passed", "Failed"}
            else None
        )
        if not isinstance(response, str) or response != expected_response:
            issues.append(
                IntegrityIssue("DECISION_RESPONSE_BINDING", item_location)
            )
        elif hashlib.sha256(response.encode("utf-8")).hexdigest() != entry.get(
            "userMessageSha256"
        ):
            issues.append(
                IntegrityIssue("DECISION_RESPONSE_HASH", item_location)
            )
        if not _is_nonempty(entry.get("decidedAt")):
            issues.append(IntegrityIssue("DECISION_DECIDED_AT", item_location))
        if entry.get("confirmedBy") != "User":
            issues.append(IntegrityIssue("DECISION_CONFIRMED_BY", item_location))
    return issues


def load_user_decision_ledger(
    repo_root: Path,
    ledger_path: Path,
) -> tuple[LedgerView | None, list[IntegrityIssue]]:
    """Load and strictly validate the append-only user decision ledger."""

    data, payload, issues = _read_json(ledger_path, repo_root)
    location = _label(ledger_path, repo_root)
    if issues:
        return None, issues
    entries, envelope_issues = _validate_envelope(
        data,
        location,
        DECISION_ENTRY_FIELDS,
    )
    issues.extend(envelope_issues)
    issues.extend(_validate_decision_entries(entries, location))
    if issues or payload is None:
        return None, sorted(set(issues))
    hashes = tuple(canonical_entry_sha256(entry) for entry in entries)
    return LedgerView(raw_sha256(payload), tuple(entries), hashes), []


def validate_user_acceptance_requests(
    repo_root: Path,
    entries: Sequence[Mapping[str, Any]],
) -> list[IntegrityIssue]:
    """Validate immutable candidate-bound requests for decision entries."""

    issues: list[IntegrityIssue] = []
    if not entries:
        return issues
    root = repo_root.resolve()
    if not _git_repository_is_trusted(root):
        return [IntegrityIssue("DECISION_REQUEST_GIT", "<repository>")]

    for entry in entries:
        request_id = entry.get("decisionRequestId")
        relative = _request_relative_path(request_id)
        if relative is None:
            issues.append(
                IntegrityIssue(
                    "DECISION_REQUEST_PATH",
                    USER_ACCEPTANCE_REQUEST_ROOT.as_posix(),
                )
            )
            continue
        relative_text = relative.as_posix()
        request_path = root / relative
        location = relative_text
        if not _inside_repo(request_path, root) or not request_path.is_file():
            issues.append(IntegrityIssue("DECISION_REQUEST_MISSING", location))
            continue
        request, current_payload, request_issues = _read_json(
            request_path,
            root,
        )
        issues.extend(request_issues)
        if not isinstance(request, Mapping) or current_payload is None:
            issues.append(IntegrityIssue("DECISION_REQUEST_INVALID", location))
            continue
        if set(request) != REQUEST_FIELDS:
            issues.append(IntegrityIssue("DECISION_REQUEST_FIELDS", location))
        expected = {
            "schemaVersion": SCHEMA_VERSION,
            "goalId": GOAL_ID,
            "acceptanceId": entry.get("acceptanceId"),
            "checkpoint": (
                f"W{ACCEPTANCE_WEEKS[str(entry.get('acceptanceId'))]}"
                if entry.get("acceptanceId") in ACCEPTANCE_WEEKS
                else None
            ),
            "decisionRequestId": request_id,
            "candidate": entry.get("candidate"),
            "manifestSha256": entry.get("manifestSha256"),
            "status": "AwaitingUser",
        }
        if any(request.get(field) != value for field, value in expected.items()):
            issues.append(IntegrityIssue("DECISION_REQUEST_BINDING", location))
        if (
            not isinstance(request.get("challengeCode"), str)
            or CHALLENGE_CODE.fullmatch(str(request.get("challengeCode"))) is None
        ):
            issues.append(IntegrityIssue("DECISION_REQUEST_CHALLENGE", location))
        elif request.get("challengeCode") != entry.get("challengeCode"):
            issues.append(IntegrityIssue("DECISION_REQUEST_BINDING", location))
        requested_at = _timestamp(request.get("requestedAt"))
        decided_at = _timestamp(entry.get("decidedAt"))
        if (
            requested_at is None
            or decided_at is None
            or requested_at >= decided_at
        ):
            issues.append(IntegrityIssue("DECISION_REQUEST_ORDER", location))

        tracked_code, _tracked_output = _git_bytes(
            root,
            ("ls-files", "--error-unmatch", "--", relative_text),
        )
        if tracked_code != 0:
            issues.append(IntegrityIssue("DECISION_REQUEST_NOT_TRACKED", location))
            continue
        log_code, log_output = _git_bytes(
            root,
            (
                "log",
                "--diff-filter=A",
                "--format=%H",
                "--reverse",
                "--",
                relative_text,
            ),
        )
        commits = (
            log_output.decode("ascii", errors="ignore").splitlines()
            if log_code == 0 and log_output is not None
            else []
        )
        if (
            len(commits) != 1
            or re.fullmatch(r"[0-9a-f]{40}", commits[0]) is None
        ):
            issues.append(
                IntegrityIssue("DECISION_REQUEST_FIRST_COMMIT", location)
            )
            continue
        first_commit = commits[0]
        if entry.get("requestCommit") != first_commit:
            issues.append(
                IntegrityIssue("DECISION_REQUEST_COMMIT_BINDING", location)
            )
        commit_time_code, commit_time_output = _git_bytes(
            root,
            ("show", "-s", "--format=%cI", first_commit),
        )
        try:
            commit_time_text = (
                commit_time_output.decode("ascii").strip()
                if commit_time_code == 0 and commit_time_output is not None
                else None
            )
        except UnicodeDecodeError:
            commit_time_text = None
        request_commit_at = _timestamp(commit_time_text)
        if (
            request_commit_at is None
            or decided_at is None
            or request_commit_at >= decided_at
        ):
            issues.append(
                IntegrityIssue("DECISION_REQUEST_COMMIT_ORDER", location)
            )
        history_code, history_output = _git_bytes(
            root,
            (
                "log",
                "--format=%H",
                "--reverse",
                "--",
                relative_text,
            ),
        )
        history_commits = (
            history_output.decode("ascii", errors="ignore").splitlines()
            if history_code == 0 and history_output is not None
            else []
        )
        if history_commits != [first_commit]:
            issues.append(
                IntegrityIssue("DECISION_REQUEST_HISTORY_MUTATION", location)
            )
        blob_code, first_payload = _git_bytes(
            root,
            ("show", f"{first_commit}:{relative_text}"),
        )
        index_code, index_payload = _git_bytes(
            root,
            ("show", f":{relative_text}"),
        )
        head_code, head_payload = _git_bytes(
            root,
            ("show", f"HEAD:{relative_text}"),
        )
        if blob_code != 0 or first_payload is None:
            issues.append(IntegrityIssue("DECISION_REQUEST_FIRST_BLOB", location))
            continue
        if current_payload != first_payload:
            issues.append(IntegrityIssue("DECISION_REQUEST_REWRITTEN", location))
        if index_code != 0 or index_payload != first_payload:
            issues.append(
                IntegrityIssue("DECISION_REQUEST_INDEX_REWRITTEN", location)
            )
        if head_code != 0 or head_payload != first_payload:
            issues.append(
                IntegrityIssue("DECISION_REQUEST_HEAD_REWRITTEN", location)
            )
        candidate = entry.get("candidate")
        if (
            not isinstance(candidate, str)
            or candidate == first_commit
        ):
            issues.append(
                IntegrityIssue("DECISION_REQUEST_CANDIDATE_ANCESTRY", location)
            )
        else:
            ancestor_code, _ancestor_output = _git_bytes(
                root,
                (
                    "merge-base",
                    "--is-ancestor",
                    candidate,
                    first_commit,
                ),
            )
            if ancestor_code != 0:
                issues.append(
                    IntegrityIssue(
                        "DECISION_REQUEST_CANDIDATE_ANCESTRY",
                        location,
                    )
                )
    return sorted(set(issues))


def _latest_decisions(
    entries: Sequence[Mapping[str, Any]],
) -> dict[str, Mapping[str, Any]]:
    latest: dict[str, Mapping[str, Any]] = {}
    for entry in entries:
        acceptance_id = entry.get("acceptanceId")
        if isinstance(acceptance_id, str):
            latest[acceptance_id] = entry
    return latest


def _decision_matches_record(
    decision: Mapping[str, Any],
    record: Mapping[str, Any],
) -> bool:
    return all(
        decision.get(field) == record.get(field)
        for field in (
            "status",
            "decisionRequestId",
            "manifestSha256",
            "challengeCode",
            "userResponseCanonical",
            "userMessageSha256",
            "decidedAt",
            "confirmedBy",
        )
    )


def _handoff_decision_prefix(
    repo_root: Path,
    handoff_path: Path,
    handoff: Mapping[str, Any],
    view: LedgerView,
) -> tuple[Sequence[Mapping[str, Any]] | None, list[IntegrityIssue]]:
    """Return the decision prefix frozen by one handoff snapshot."""

    location = _label(handoff_path, repo_root)
    binding = _mapping(handoff.get("goalControlBinding"))
    snapshot_path = _safe_bound_path(
        repo_root,
        binding.get("path"),
        handoff_path,
    )
    if snapshot_path is None:
        return None, [IntegrityIssue("HANDOFF_ACCEPTANCE_SNAPSHOT", location)]
    snapshot, _payload, snapshot_issues = _read_json(snapshot_path, repo_root)
    if snapshot_issues or not isinstance(snapshot, Mapping):
        return None, [
            *snapshot_issues,
            IntegrityIssue("HANDOFF_ACCEPTANCE_SNAPSHOT", location),
        ]
    count = _mapping(snapshot.get("userDecisionLedger")).get("entryCount")
    if (
        not isinstance(count, int)
        or isinstance(count, bool)
        or count < 0
        or count > view.entry_count
    ):
        return None, [
            IntegrityIssue("HANDOFF_ACCEPTANCE_SNAPSHOT_PREFIX", location),
            IntegrityIssue("HANDOFF_ACCEPTANCE_UNANCHORED", location),
        ]
    return view.entries[:count], []


def validate_user_decision_integrity(
    repo_root: Path,
    goal_state_path: Path,
    decision_ledger_path: Path,
    handoff_paths: Sequence[Path],
    *,
    candidate_root: Path | None = None,
) -> tuple[LedgerView | None, list[IntegrityIssue]]:
    """Validate decisions and reject self-signing or early acceptance."""

    source_root = (candidate_root or repo_root).resolve()
    view, issues = load_user_decision_ledger(repo_root, decision_ledger_path)
    goal, _payload, goal_issues = _read_json(goal_state_path, repo_root)
    issues.extend(goal_issues)
    if view is None:
        return None, sorted(set(issues))
    issues.extend(validate_user_acceptance_requests(source_root, view.entries))

    if isinstance(goal, Mapping):
        goal_location = _label(goal_state_path, repo_root)
        binding = _mapping(goal.get("userDecisionLedger"))
        expected_binding = {
            "path": USER_DECISION_LEDGER_PATH,
            "sha256": view.raw_sha256,
            "entryCount": view.entry_count,
            "lastEntrySha256": view.last_entry_sha256,
        }
        if set(binding) != set(expected_binding):
            issues.append(
                IntegrityIssue("DECISION_CENTRAL_BINDING_FIELDS", goal_location)
            )
        for field, expected in expected_binding.items():
            if binding.get(field) != expected:
                issues.append(
                    IntegrityIssue(
                        f"DECISION_CENTRAL_{field.upper()}",
                        goal_location,
                    )
                )

    current_latest = _latest_decisions(view.entries)

    for handoff_path in handoff_paths:
        handoff, _handoff_payload, handoff_issues = _read_json(
            handoff_path,
            repo_root,
        )
        issues.extend(handoff_issues)
        location = _label(handoff_path, repo_root)
        if not isinstance(handoff, Mapping):
            continue
        prefix, prefix_issues = _handoff_decision_prefix(
            repo_root,
            handoff_path,
            handoff,
            view,
        )
        issues.extend(prefix_issues)
        if prefix is None:
            continue
        handoff_latest = _latest_decisions(prefix)
        week = handoff.get("week")
        if not isinstance(week, int) or isinstance(week, bool):
            issues.append(IntegrityIssue("HANDOFF_WEEK", location))
            continue
        candidate = _mapping(handoff.get("identity")).get("productCandidate")
        acceptances = {
            item.get("acceptanceId"): item
            for item in handoff.get("userAcceptances", [])
            if isinstance(item, Mapping)
        } if isinstance(handoff.get("userAcceptances"), list) else {}
        for acceptance_id, target_week in ACCEPTANCE_WEEKS.items():
            record = acceptances.get(acceptance_id)
            if not isinstance(record, Mapping):
                issues.append(IntegrityIssue("HANDOFF_ACCEPTANCE_MISSING", location))
                continue
            status = record.get("status")
            if week < target_week:
                if status != "NotRun":
                    issues.append(
                        IntegrityIssue("HANDOFF_FUTURE_ACCEPTANCE", location)
                    )
                continue
            latest = handoff_latest.get(acceptance_id)
            if latest is None:
                if status != "NotRun":
                    issues.append(
                        IntegrityIssue("HANDOFF_ACCEPTANCE_UNANCHORED", location)
                    )
                continue
            if not _decision_matches_record(latest, record):
                issues.append(
                    IntegrityIssue("HANDOFF_ACCEPTANCE_LATEST", location)
                )
            if (
                week == target_week
                and latest.get("candidate") != candidate
            ):
                issues.append(
                    IntegrityIssue("HANDOFF_ACCEPTANCE_CANDIDATE", location)
                )

    if isinstance(goal, Mapping):
        central = _mapping(goal.get("manualAcceptances"))
        for acceptance_id, target_week in ACCEPTANCE_WEEKS.items():
            field = {
                86: "w86Visual",
                89: "w89Visual",
                92: "w92Visual",
            }[target_week]
            status = central.get(field)
            latest = current_latest.get(acceptance_id)
            expected_status = (
                latest.get("status") if latest is not None else "NotRun"
            )
            if status != expected_status:
                issues.append(
                    IntegrityIssue(
                        "CENTRAL_ACCEPTANCE_LATEST",
                        _label(goal_state_path, repo_root),
                    )
                )
    return view, sorted(set(issues))


@_trusted_git_validation_bundle
def validate_goal_integrity(
    *,
    repo_root: Path,
    control_root: Path | None = None,
    goal_state_path: Path,
    failure_ledger_path: Path,
    user_decision_ledger_path: Path,
    gate_paths: Sequence[Path],
    handoff_paths: Sequence[Path],
) -> list[IntegrityIssue]:
    """Run all independent Week84-92 append-only integrity checks.

    ``repo_root`` is the trusted candidate checkout used only for Git-backed
    request identity.  Ignored Goal evidence is read below ``control_root``;
    omitting it retains the Week84 single-worktree contract.
    """

    candidate = repo_root.resolve()
    evidence = (control_root or repo_root).resolve()
    failure_view, issues = validate_first_failure_integrity(
        evidence,
        goal_state_path,
        failure_ledger_path,
        gate_paths,
    )
    decision_view, decision_issues = validate_user_decision_integrity(
        evidence,
        goal_state_path,
        user_decision_ledger_path,
        handoff_paths,
        candidate_root=candidate,
    )
    issues.extend(decision_issues)
    if failure_view is not None:
        issues.extend(
            validate_handoff_snapshots(
                evidence,
                handoff_paths,
                failure_view,
                decision_view,
            )
        )
    return sorted(set(issues))


def issue_codes(issues: Iterable[IntegrityIssue]) -> set[str]:
    """Convenience helper for callers and tests."""

    return {issue.code for issue in issues}
