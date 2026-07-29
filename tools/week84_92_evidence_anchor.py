"""Tracked evidence anchors for the Week84-92 refactor Goal.

The Goal's detailed evidence lives under ignored ``artifacts/`` directories.
An ignored file can be replaced without leaving a Git diff, so each completed
Week/lane group is sealed by one small tracked JSON anchor at the fixed path::

    docs_md/weekly/84_92_evidence_anchors/<group-id>.json

The anchor binds the raw bytes of every Gate result, the pre-handoff Goal
snapshot, the canonical handoff, and (for Week84) its compatibility alias.
Mutable append-only ledgers are bound as checkpoint prefixes: the snapshot's
raw ledger hash/count is retained, while canonical digests of the first N
hash-chained entries remain verifiable after later checkpoints append data.
The provider runtime ledger has two independent append-only chains--turn
reservations and attempt events--and its anchor prefix binds both chains.

Anchors deliberately do not contain an anchor commit SHA.  The validator asks
Git for the commit that first added the fixed anchor path, treats that commit's
blob as immutable, and proves that ``productCandidate`` is an ancestor of that
first-add commit.  Worktree, index, and HEAD must all still contain the exact
first-add bytes.  This avoids an impossible self-referential commit hash while
preventing a later clean commit from silently resealing replaced evidence.

This module is independent, standard-library only, fail-closed, and
secret-safe.  Public issues contain stable codes and repository-relative
locations only; parsed values, file contents, command output, and exception
text are never included.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from datetime import datetime, timezone
from functools import wraps
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import subprocess
import sys
import tempfile
from typing import Any, Iterable, Mapping, Sequence

try:
    from tools import week84_92_trusted_executor as trusted_executor
except ImportError:  # direct execution from the tools directory
    import week84_92_trusted_executor as trusted_executor


SCHEMA_VERSION = "1.0.0"
REGISTRY_VERSION = "w84-w92-evidence-anchors-v1"
GOAL_ID = "c-aicli-cli-desktop-refactor-w84-w92"
ANCHOR_DIRECTORY = "docs_md/weekly/84_92_evidence_anchors"
FIRST_FAILURE_LEDGER_PATH = (
    "artifacts/week84-92-goal-control/first-failure-ledger.json"
)
USER_DECISION_LEDGER_PATH = (
    "artifacts/week84-92-goal-control/user-decision-ledger.json"
)
PROVIDER_LEDGER_PATH = (
    "artifacts/week84-92-goal-control/provider-turn-ledger.json"
)
PROVIDER_RUNTIME_JOURNAL_PATH = (
    "artifacts/week84-92-goal-control/provider-runtime-journal.json"
)
PROVIDER_JOURNAL_VERSION = "week84-92-provider-runtime-v1"
PROVIDER_RUNTIME_JOURNAL_VERSION = "week84-92-provider-execution-v1"
PROVIDER_MAX_TURNS = 120
PRESEAL_RECEIPT_VERSION = "week84-92-preseal-receipt-v1"
VALIDATOR_SOURCE_PATH = "tools/validate-week84-92-goal-evidence.py"
ANCHOR_SOURCE_PATH = "tools/week84_92_evidence_anchor.py"
REQUIREMENTS_MANIFEST_PATH = (
    "docs_md/weekly/84_92_week_gate_requirements.json"
)
MAX_JSON_BYTES = 16 * 1024 * 1024
MAX_EVIDENCE_BYTES = 1024 * 1024 * 1024
HEX_SHA256 = re.compile(r"^[0-9a-f]{64}$")
GIT_COMMIT = re.compile(r"^[0-9a-f]{40}$")
ANCHOR_REGISTRY_PROTOCOL = "week84-92-anchor-registry-v1"
EVIDENCE_BUNDLE_PROTOCOL = "week84-92-evidence-bundle-v1"
ANCHOR_REGISTRY_DIRECTORY = "docs_md/weekly/84_92_anchor_registry"
EVIDENCE_STORE_ROOT = "artifacts/week84-92-evidence-store/sha256"
CONTROL_BRANCH_REF = "refs/heads/codex/week84-92-refactor"
RENDERER_BRANCH_REF = "refs/heads/codex/week84-92-renderer"
CLI_BRANCH_REF = "refs/heads/codex/week84-92-cli"
INTEGRATION_BRANCH_REF = "refs/heads/codex/week84-92-integration"
SEALED_REF_ROOT = "refs/codex/week84-92/sealed"
MAX_BUNDLE_ENTRIES = 4096
_WINDOWS_DEVICE_NAME = re.compile(
    r"^(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?$",
    re.IGNORECASE,
)
_CHECKPOINT_KIND_ROOTS = {
    "provider-boundary-decision": (
        "docs_md/weekly/84_92_provider_boundary_decisions",
    ),
    "manual-acceptance-request": (
        "docs_md/weekly/84_92_user_acceptance_requests",
    ),
    "controlled-write-approval": (
        "docs_md/weekly/84_92_controlled_write_authorizations",
    ),
    "controlled-write-preauthorization": (
        "docs_md/weekly/84_92_controlled_write_authorizations",
    ),
    "controlled-write-tombstone": (
        "docs_md/weekly/84_92_controlled_write_tombstones",
    ),
}


@dataclass(frozen=True)
class GroupSpec:
    """Frozen identity and paths for one canonical Week/lane group."""

    group_id: str
    week: int
    lane: str
    gate_prefix: str
    gate_count: int
    artifact_directory: str
    handoff_path: str
    alias_path: str | None
    parent_group_ids: tuple[str, ...]

    @property
    def gate_ids(self) -> tuple[str, ...]:
        return tuple(
            f"W{self.week}-{self.gate_prefix}{index}"
            for index in range(self.gate_count)
        )

    @property
    def gate_paths(self) -> tuple[str, ...]:
        return tuple(
            f"{self.artifact_directory}/gates/{gate_id}.json"
            for gate_id in self.gate_ids
        )

    @property
    def snapshot_path(self) -> str:
        return f"{self.artifact_directory}/goal-control-snapshot.json"

    @property
    def preseal_receipt_path(self) -> str:
        return f"{self.artifact_directory}/preseal-receipt.json"

    @property
    def anchor_path(self) -> str:
        return f"{ANCHOR_DIRECTORY}/{self.group_id}.json"


CANONICAL_GROUPS: tuple[GroupSpec, ...] = (
    GroupSpec(
        "w84-baseline",
        84,
        "baseline",
        "G",
        10,
        "artifacts/week84-renderer-listener-retention",
        "artifacts/week84-renderer-listener-retention/week84-baseline-handoff.json",
        "artifacts/week84-renderer-listener-retention/week85-refactor-handoff.json",
        (),
    ),
    GroupSpec(
        "w85-renderer",
        85,
        "renderer",
        "R",
        8,
        "artifacts/week85-renderer-feature-boundaries",
        "artifacts/week85-renderer-feature-boundaries/week85-renderer-handoff.json",
        None,
        ("w84-baseline",),
    ),
    GroupSpec(
        "w85-cli",
        85,
        "cli",
        "C",
        6,
        "artifacts/week85-cli-composition",
        "artifacts/week85-cli-composition/week85-cli-handoff.json",
        None,
        ("w84-baseline",),
    ),
    GroupSpec(
        "w86-renderer",
        86,
        "renderer",
        "R",
        8,
        "artifacts/week86-renderer-chat-first-shell",
        "artifacts/week86-renderer-chat-first-shell/week86-renderer-handoff.json",
        None,
        ("w85-renderer",),
    ),
    GroupSpec(
        "w86-cli",
        86,
        "cli",
        "C",
        6,
        "artifacts/week86-cli-jobs-review-session",
        "artifacts/week86-cli-jobs-review-session/week86-cli-handoff.json",
        None,
        ("w85-cli",),
    ),
    GroupSpec(
        "w87-renderer",
        87,
        "renderer",
        "R",
        6,
        "artifacts/week87-renderer-conversation-projection",
        "artifacts/week87-renderer-conversation-projection/week87-renderer-handoff.json",
        None,
        ("w86-renderer",),
    ),
    GroupSpec(
        "w87-cli",
        87,
        "cli",
        "C",
        6,
        "artifacts/week87-cli-exec-skills-queue",
        "artifacts/week87-cli-exec-skills-queue/week87-cli-handoff.json",
        None,
        ("w86-cli",),
    ),
    GroupSpec(
        "w88-renderer",
        88,
        "renderer",
        "R",
        6,
        "artifacts/week88-renderer-composer-approval",
        "artifacts/week88-renderer-composer-approval/week88-renderer-handoff.json",
        None,
        ("w87-renderer",),
    ),
    GroupSpec(
        "w88-cli",
        88,
        "cli",
        "C",
        6,
        "artifacts/week88-cli-packs-artifacts",
        "artifacts/week88-cli-packs-artifacts/week88-cli-handoff.json",
        None,
        ("w87-cli",),
    ),
    GroupSpec(
        "w89-renderer",
        89,
        "renderer",
        "R",
        8,
        "artifacts/week89-context-review-workspace",
        "artifacts/week89-context-review-workspace/week89-renderer-handoff.json",
        None,
        ("w88-renderer",),
    ),
    GroupSpec(
        "w89-cli",
        89,
        "cli",
        "C",
        8,
        "artifacts/week89-cli-automation-pipeline",
        "artifacts/week89-cli-automation-pipeline/week89-cli-handoff.json",
        None,
        ("w88-cli",),
    ),
    GroupSpec(
        "w90-integration",
        90,
        "integration",
        "G",
        8,
        "artifacts/week90-cli-desktop-integration",
        "artifacts/week90-cli-desktop-integration/week90-integration-handoff.json",
        None,
        ("w89-renderer", "w89-cli"),
    ),
    GroupSpec(
        "w91-hardening",
        91,
        "hardening",
        "G",
        8,
        "artifacts/week91-refactor-hardening",
        "artifacts/week91-refactor-hardening/week91-hardening-handoff.json",
        None,
        ("w90-integration",),
    ),
    GroupSpec(
        "w92-acceptance",
        92,
        "acceptance",
        "G",
        10,
        "artifacts/week92-refactor-acceptance",
        "artifacts/week92-refactor-acceptance/week92-acceptance-handoff.json",
        None,
        ("w91-hardening",),
    ),
)
GROUP_BY_ID = {group.group_id: group for group in CANONICAL_GROUPS}
ANCHOR_PATH_TO_GROUP = {group.anchor_path: group for group in CANONICAL_GROUPS}
GROUP_SEQUENCE = {
    group.group_id: index
    for index, group in enumerate(CANONICAL_GROUPS, start=1)
}
GROUP_BRANCH_REFS = {
    group.group_id: (
        RENDERER_BRANCH_REF
        if group.lane == "renderer"
        else CLI_BRANCH_REF
        if group.lane == "cli"
        else INTEGRATION_BRANCH_REF
        if group.week >= 90
        else CONTROL_BRANCH_REF
    )
    for group in CANONICAL_GROUPS
}
ENTRY_BARRIER_GROUPS = {
    "w84-baseline": (),
    "w85-renderer": ("w84-baseline",),
    "w85-cli": ("w84-baseline",),
    "w86-renderer": ("w85-renderer", "w85-cli"),
    "w86-cli": ("w85-renderer", "w85-cli"),
    "w87-renderer": ("w86-renderer", "w86-cli"),
    "w87-cli": ("w86-renderer", "w86-cli"),
    "w88-renderer": ("w87-renderer", "w87-cli"),
    "w88-cli": ("w87-renderer", "w87-cli"),
    "w89-renderer": ("w88-renderer", "w88-cli"),
    "w89-cli": ("w88-renderer", "w88-cli"),
    "w90-integration": ("w89-renderer", "w89-cli"),
    "w91-hardening": ("w90-integration",),
    "w92-acceptance": ("w91-hardening",),
}

REGISTRY_FIELDS = {
    "schemaVersion",
    "protocol",
    "goalId",
    "sequence",
    "groupId",
    "week",
    "lane",
    "objectFormat",
    "laneBinding",
    "anchorBinding",
    "preservationRef",
    "checkpointDelta",
    "evidenceBundle",
    "ledgerPrefixes",
    "protocolParents",
    "entryBarrier",
    "previousRegistry",
}
LANE_BINDING_FIELDS = {
    "branchRef",
    "entryBaseCommit",
    "productCandidate",
    "candidateTree",
    "sealCommit",
    "sealTree",
}
ANCHOR_BINDING_FIELDS = {
    "path",
    "commit",
    "parentCommit",
    "tree",
    "gitBlobSha",
    "sha256",
}
PRESERVATION_REF_FIELDS = {"name", "targetCommit"}
CHECKPOINT_DELTA_FIELDS = {
    "kind",
    "path",
    "firstAddCommit",
    "gitBlobSha",
    "sha256",
}
EVIDENCE_BUNDLE_BINDING_FIELDS = {
    "path",
    "sha256",
    "entryCount",
    "totalBytes",
    "contentRootSha256",
}
REGISTRY_DEPENDENCY_FIELDS = {"groupId", "path", "sha256", "commit"}
BUNDLE_FIELDS = {
    "schemaVersion",
    "protocol",
    "goalId",
    "groupId",
    "sourceAnchor",
    "entryCount",
    "totalBytes",
    "contentRootSha256",
    "entries",
    "ledgerPrefixes",
}
BUNDLE_SOURCE_ANCHOR_FIELDS = {"path", "commit", "gitBlobSha", "sha256"}
BUNDLE_ENTRY_FIELDS = {
    "canonicalPath",
    "contentKind",
    "mode",
    "bytes",
    "sha256",
    "storeObject",
}


@dataclass(frozen=True)
class RegistryBuildResult:
    """Documents and CAS objects prepared for one immutable registry entry."""

    registry: Mapping[str, Any]
    bundle: Mapping[str, Any]
    registry_path: str
    bundle_path: str
    store_objects: tuple[str, ...]


@dataclass(frozen=True, order=True)
class AnchorIssue:
    """A deterministic, redacted evidence-anchor validation issue."""

    code: str
    location: str

    def __str__(self) -> str:
        return f"[{self.code}] {self.location}"


class AnchorBuildError(RuntimeError):
    """Secret-safe failure raised by the pure anchor document builder."""

    def __init__(self, code: str, location: str):
        self.code = code
        self.location = location
        super().__init__(f"[{code}] {location}")


class _DuplicateKey(ValueError):
    pass


@dataclass(frozen=True)
class _Document:
    relative_path: str
    data: Any
    raw: bytes
    sha256: str


@dataclass(frozen=True)
class _LedgerView:
    kind: str
    path: str
    raw_sha256: str
    entries: tuple[Mapping[str, Any], ...]
    entry_hashes: tuple[str, ...]
    attempt_events: tuple[Mapping[str, Any], ...] = ()
    attempt_event_hashes: tuple[str, ...] = ()

    @property
    def entry_count(self) -> int:
        return len(self.entries)

    @property
    def last_entry_sha256(self) -> str | None:
        return self.entry_hashes[-1] if self.entry_hashes else None

    @property
    def attempt_event_count(self) -> int:
        return len(self.attempt_events)

    @property
    def last_attempt_event_sha256(self) -> str | None:
        return self.attempt_event_hashes[-1] if self.attempt_event_hashes else None


@dataclass(frozen=True)
class _SnapshotLedgerValues:
    path: str
    raw_sha256: str
    entry_count: int
    last_entry_sha256: str | None
    attempt_event_count: int | None = None
    last_attempt_event_sha256: str | None = None


@dataclass(frozen=True)
class _AnchorRecord:
    spec: GroupSpec
    document: _Document
    data: Mapping[str, Any]
    product_candidate: str | None
    ledger_counts: Mapping[str, int]
    provider_attempt_event_count: int | None


ANCHOR_FIELDS = {
    "schemaVersion",
    "registryVersion",
    "goalId",
    "groupId",
    "week",
    "lane",
    "productCandidate",
    "gates",
    "goalControlSnapshot",
    "canonicalHandoff",
    "compatibilityHandoffAlias",
    "ledgerPrefixes",
    "parentAnchors",
    "presealReceipt",
}
ARTIFACT_BINDING_FIELDS = {"path", "sha256"}
GATE_BINDING_FIELDS = {"gateId", "path", "sha256", "evidence"}
EVIDENCE_BINDING_FIELDS = {"evidenceId", "path", "sha256"}
LEDGER_PREFIX_FIELDS = {
    "path",
    "rawFileSha256",
    "entryCount",
    "lastEntrySha256",
    "prefixSha256",
}
PROVIDER_LEDGER_PREFIX_FIELDS = LEDGER_PREFIX_FIELDS | {
    "attemptEventCount",
    "lastAttemptEventSha256",
    "attemptEventPrefixSha256",
}
PARENT_BINDING_FIELDS = {"groupId", "path", "sha256"}
PRESEAL_RECEIPT_FIELDS = {
    "schemaVersion",
    "receiptVersion",
    "registryVersion",
    "goalId",
    "groupId",
    "week",
    "lane",
    "productCandidate",
    "validatorSource",
    "anchorSource",
    "requirementsManifest",
    "startedAt",
    "finishedAt",
    "exitCode",
    "gates",
    "goalControlSnapshot",
    "canonicalHandoff",
    "compatibilityHandoffAlias",
    "ledgerPrefixes",
    "parentAnchors",
}
PRESEAL_SOURCE_PATHS = {
    "validatorSource": VALIDATOR_SOURCE_PATH,
    "anchorSource": ANCHOR_SOURCE_PATH,
    "requirementsManifest": REQUIREMENTS_MANIFEST_PATH,
}
PRESEAL_BOUND_ANCHOR_FIELDS = {
    "schemaVersion",
    "registryVersion",
    "goalId",
    "groupId",
    "week",
    "lane",
    "productCandidate",
    "gates",
    "goalControlSnapshot",
    "canonicalHandoff",
    "compatibilityHandoffAlias",
    "ledgerPrefixes",
    "parentAnchors",
}
LEDGER_PATHS = {
    "firstFailure": FIRST_FAILURE_LEDGER_PATH,
    "userDecision": USER_DECISION_LEDGER_PATH,
    "provider": PROVIDER_LEDGER_PATH,
    "providerRuntime": PROVIDER_RUNTIME_JOURNAL_PATH,
}
PROVIDER_LEDGER_FIELDS = {
    "schemaVersion",
    "journalVersion",
    "goalId",
    "maxTurns",
    "usedTurns",
    "remainingTurns",
    "ledgerSequence",
    "entries",
    "attemptEventCount",
    "lastAttemptEventSha256",
    "attemptEvents",
}
PROVIDER_RUNTIME_JOURNAL_FIELDS = {
    "schemaVersion",
    "journalVersion",
    "goalId",
    "eventCount",
    "lastEventSha256",
    "events",
}
PROVIDER_LEDGER_BINDING_GATE_IDS = frozenset(
    {
        "W84-G6",
        "W84-G7",
        "W84-G8",
        "W90-G0",
        "W91-G0",
        "W91-G4",
        "W91-G7",
        "W92-G0",
        "W92-G7",
        "W92-G9",
    }
)


def canonical_json_bytes(value: Any) -> bytes:
    """Return the frozen canonical JSON encoding used by ledger hashes."""

    return json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")


def canonical_entry_sha256(entry: Mapping[str, Any]) -> str:
    """Hash a ledger entry after excluding only its own hash field."""

    payload = dict(entry)
    payload.pop("entrySha256", None)
    return hashlib.sha256(canonical_json_bytes(payload)).hexdigest()


def canonical_attempt_event_sha256(event: Mapping[str, Any]) -> str:
    """Hash an attempt event after excluding only its own hash field."""

    payload = dict(event)
    payload.pop("attemptEventSha256", None)
    return hashlib.sha256(canonical_json_bytes(payload)).hexdigest()


def canonical_runtime_event_sha256(event: Mapping[str, Any]) -> str:
    """Hash one provider execution event excluding only its own hash."""

    payload = dict(event)
    payload.pop("eventSha256", None)
    return hashlib.sha256(canonical_json_bytes(payload)).hexdigest()


def attempt_event_prefix_sha256(
    path: str,
    attempt_events: Sequence[Mapping[str, Any]],
    attempt_event_count: int,
) -> str:
    """Hash the exact append-stable first N provider attempt events."""

    selected = list(attempt_events[:attempt_event_count])
    last_hash = (
        selected[-1].get("attemptEventSha256") if selected else None
    )
    payload = {
        "kind": "providerAttemptEvents",
        "path": path,
        "attemptEventCount": attempt_event_count,
        "lastAttemptEventSha256": last_hash,
        "attemptEvents": selected,
    }
    return hashlib.sha256(canonical_json_bytes(payload)).hexdigest()


def ledger_prefix_sha256(
    kind: str,
    path: str,
    entries: Sequence[Mapping[str, Any]],
    entry_count: int,
    *,
    attempt_events: Sequence[Mapping[str, Any]] = (),
    attempt_event_count: int = 0,
) -> str:
    """Hash an exact append-stable ledger projection.

    Provider projections include both the reservation and attempt-event
    prefixes so neither chain can be rewritten independently.
    """

    selected = list(entries[:entry_count])
    if kind == "providerRuntime":
        payload = {
            "kind": kind,
            "path": path,
            "eventCount": entry_count,
            "lastEventSha256": (
                selected[-1].get("eventSha256") if selected else None
            ),
            "events": selected,
        }
    else:
        payload = {
            "kind": kind,
            "path": path,
            "entryCount": entry_count,
            "lastEntrySha256": (
                selected[-1].get("entrySha256") if selected else None
            ),
            "entries": selected,
        }
    if kind == "provider":
        selected_events = list(attempt_events[:attempt_event_count])
        payload.update(
            {
                "attemptEventCount": attempt_event_count,
                "lastAttemptEventSha256": (
                    selected_events[-1].get("attemptEventSha256")
                    if selected_events
                    else None
                ),
                "attemptEvents": selected_events,
            }
        )
    return hashlib.sha256(canonical_json_bytes(payload)).hexdigest()


def issue_codes(issues: Iterable[AnchorIssue]) -> set[str]:
    """Return stable codes without exposing any parsed input."""

    return {issue.code for issue in issues}


def _pairs_no_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise _DuplicateKey(key)
        result[key] = value
    return result


def _mapping(value: Any) -> Mapping[str, Any]:
    return value if isinstance(value, Mapping) else {}


def _list(value: Any) -> list[Any]:
    return value if isinstance(value, list) else []


def _safe_path(repo_root: Path, relative_path: str) -> Path | None:
    try:
        pure = PurePosixPath(relative_path)
    except (TypeError, ValueError):
        return None
    if pure.is_absolute() or not pure.parts or ".." in pure.parts:
        return None
    root = repo_root.resolve()
    candidate = root.joinpath(*pure.parts)
    try:
        resolved = candidate.resolve(strict=False)
        resolved.relative_to(root)
    except (OSError, ValueError):
        return None
    current = root
    for part in pure.parts:
        current = current / part
        try:
            if current.is_symlink():
                return None
        except OSError:
            return None
    return candidate


def _evidence_path_allowed(
    spec: GroupSpec,
    gate_id: str,
    relative_path: Any,
) -> bool:
    if not isinstance(relative_path, str) or "\\" in relative_path:
        return False
    try:
        pure = PurePosixPath(relative_path)
    except (TypeError, ValueError):
        return False
    if (
        pure.is_absolute()
        or not pure.parts
        or ".." in pure.parts
        or str(pure) != relative_path
    ):
        return False
    gate_root = PurePosixPath(
        spec.artifact_directory,
        "gate-evidence",
        gate_id,
    )
    if (
        len(pure.parts) > len(gate_root.parts)
        and pure.parts[: len(gate_root.parts)] == gate_root.parts
    ):
        return True
    provider_binding_path = (
        f"{spec.artifact_directory}/provider-ledger-bindings/{gate_id}/"
        "provider-ledger-binding.json"
    )
    return (
        gate_id in PROVIDER_LEDGER_BINDING_GATE_IDS
        and relative_path == provider_binding_path
    )


def _raw_file_sha256(
    repo_root: Path,
    relative_path: str,
    issues: list[AnchorIssue],
    *,
    missing_code: str,
    unsafe_code: str,
    too_large_code: str,
    unstable_code: str,
    issue_location: str | None = None,
) -> str | None:
    location = issue_location or relative_path
    path = _safe_path(repo_root, relative_path)
    if path is None:
        issues.append(AnchorIssue(unsafe_code, location))
        return None
    try:
        if not path.is_file():
            issues.append(AnchorIssue(missing_code, location))
            return None
        digest = hashlib.sha256()
        with path.open("rb") as handle:
            before = os.fstat(handle.fileno())
            if before.st_size > MAX_EVIDENCE_BYTES:
                issues.append(AnchorIssue(too_large_code, location))
                return None
            while chunk := handle.read(1024 * 1024):
                digest.update(chunk)
            after = os.fstat(handle.fileno())
        if (
            before.st_size != after.st_size
            or before.st_mtime_ns != after.st_mtime_ns
        ):
            issues.append(AnchorIssue(unstable_code, location))
            return None
        return digest.hexdigest()
    except OSError:
        issues.append(AnchorIssue(missing_code, location))
        return None


def _builder_evidence_sha256(
    repo_root: Path,
    relative_path: str,
    location: str,
) -> str:
    issues: list[AnchorIssue] = []
    sha256 = _raw_file_sha256(
        repo_root,
        relative_path,
        issues,
        missing_code="BUILD_GATE_EVIDENCE_MISSING",
        unsafe_code="BUILD_GATE_EVIDENCE_PATH",
        too_large_code="BUILD_GATE_EVIDENCE_TOO_LARGE",
        unstable_code="BUILD_GATE_EVIDENCE_UNSTABLE",
        issue_location=location,
    )
    if sha256 is None:
        issue = issues[0] if issues else AnchorIssue(
            "BUILD_GATE_EVIDENCE_INVALID", relative_path
        )
        raise AnchorBuildError(issue.code, issue.location)
    return sha256


def _read_bytes(
    repo_root: Path,
    relative_path: str,
    issues: list[AnchorIssue],
    *,
    missing_code: str,
    unsafe_code: str,
) -> bytes | None:
    path = _safe_path(repo_root, relative_path)
    if path is None:
        issues.append(AnchorIssue(unsafe_code, relative_path))
        return None
    try:
        if not path.is_file():
            issues.append(AnchorIssue(missing_code, relative_path))
            return None
        size = path.stat().st_size
        if size > MAX_JSON_BYTES:
            issues.append(AnchorIssue("ANCHOR_FILE_TOO_LARGE", relative_path))
            return None
        return path.read_bytes()
    except OSError:
        issues.append(AnchorIssue(missing_code, relative_path))
        return None


def _read_document(
    repo_root: Path,
    relative_path: str,
    issues: list[AnchorIssue],
    *,
    missing_code: str,
    json_code: str,
) -> _Document | None:
    raw = _read_bytes(
        repo_root,
        relative_path,
        issues,
        missing_code=missing_code,
        unsafe_code="ANCHOR_PATH_UNSAFE",
    )
    if raw is None:
        return None
    try:
        data = json.loads(
            raw.decode("utf-8"),
            object_pairs_hook=_pairs_no_duplicates,
        )
    except (UnicodeDecodeError, json.JSONDecodeError, _DuplicateKey):
        issues.append(AnchorIssue(json_code, relative_path))
        return None
    return _Document(
        relative_path=relative_path,
        data=data,
        raw=raw,
        sha256=hashlib.sha256(raw).hexdigest(),
    )


def _builder_document(repo_root: Path, relative_path: str) -> _Document:
    issues: list[AnchorIssue] = []
    document = _read_document(
        repo_root,
        relative_path,
        issues,
        missing_code="BUILD_INPUT_MISSING",
        json_code="BUILD_INPUT_JSON",
    )
    if document is None:
        issue = issues[0] if issues else AnchorIssue(
            "BUILD_INPUT_INVALID", relative_path
        )
        raise AnchorBuildError(issue.code, issue.location)
    return document


def _builder_raw_binding(repo_root: Path, relative_path: str) -> dict[str, str]:
    issues: list[AnchorIssue] = []
    raw = _read_bytes(
        repo_root,
        relative_path,
        issues,
        missing_code="BUILD_PRESEAL_SOURCE_MISSING",
        unsafe_code="BUILD_PRESEAL_SOURCE_UNSAFE",
    )
    if raw is None:
        issue = issues[0] if issues else AnchorIssue(
            "BUILD_PRESEAL_SOURCE_INVALID", relative_path
        )
        code = (
            "BUILD_PRESEAL_SOURCE_INVALID"
            if issue.code == "ANCHOR_FILE_TOO_LARGE"
            else issue.code
        )
        raise AnchorBuildError(code, issue.location)
    return {
        "path": relative_path,
        "sha256": hashlib.sha256(raw).hexdigest(),
    }


def capture_preseal_source_bindings(
    repo_root: Path,
) -> dict[str, dict[str, str]]:
    """Capture the three control-plane sources at trusted pre-seal start."""

    root = repo_root.resolve()
    return {
        field: _builder_raw_binding(root, path)
        for field, path in PRESEAL_SOURCE_PATHS.items()
    }


def _parse_preseal_timestamp(value: Any) -> datetime | None:
    if not isinstance(value, str) or not value.endswith("Z"):
        return None
    try:
        parsed = datetime.fromisoformat(f"{value[:-1]}+00:00")
    except ValueError:
        return None
    if parsed.tzinfo is None or parsed.utcoffset() != timezone.utc.utcoffset(None):
        return None
    return parsed


def _preseal_interval_is_valid(started_at: Any, finished_at: Any) -> bool:
    started = _parse_preseal_timestamp(started_at)
    finished = _parse_preseal_timestamp(finished_at)
    return started is not None and finished is not None and started <= finished


def _product_candidate(data: Any) -> str | None:
    candidate = _mapping(_mapping(data).get("identity")).get(
        "productCandidate"
    )
    return candidate if isinstance(candidate, str) else None


def _load_ledger(
    repo_root: Path,
    kind: str,
    issues: list[AnchorIssue],
) -> _LedgerView | None:
    relative_path = LEDGER_PATHS[kind]
    document = _read_document(
        repo_root,
        relative_path,
        issues,
        missing_code="ANCHOR_LEDGER_MISSING",
        json_code="ANCHOR_LEDGER_JSON",
    )
    if document is None:
        return None
    data = _mapping(document.data)
    entries_value = (
        data.get("events") if kind == "providerRuntime" else data.get("entries")
    )
    attempt_events_value: list[Any] = []
    if kind == "provider":
        raw_attempt_events = data.get("attemptEvents")
        if (
            set(data) != PROVIDER_LEDGER_FIELDS
            or data.get("schemaVersion") != SCHEMA_VERSION
            or data.get("journalVersion") != PROVIDER_JOURNAL_VERSION
            or data.get("goalId") != GOAL_ID
            or data.get("maxTurns") != PROVIDER_MAX_TURNS
            or not isinstance(entries_value, list)
            or not isinstance(raw_attempt_events, list)
        ):
            issues.append(AnchorIssue("ANCHOR_LEDGER_FORMAT", relative_path))
            return None
        attempt_events_value = raw_attempt_events
    elif kind == "providerRuntime":
        if (
            set(data) != PROVIDER_RUNTIME_JOURNAL_FIELDS
            or data.get("schemaVersion") != SCHEMA_VERSION
            or data.get("journalVersion")
            != PROVIDER_RUNTIME_JOURNAL_VERSION
            or data.get("goalId") != GOAL_ID
            or not isinstance(entries_value, list)
        ):
            issues.append(AnchorIssue("ANCHOR_LEDGER_FORMAT", relative_path))
            return None
    elif not isinstance(entries_value, list):
        issues.append(AnchorIssue("ANCHOR_LEDGER_FORMAT", relative_path))
        return None
    assert isinstance(entries_value, list)
    entries: list[Mapping[str, Any]] = []
    hashes: list[str] = []
    previous: str | None = None
    valid = True
    for index, value in enumerate(entries_value, start=1):
        collection = "events" if kind == "providerRuntime" else "entries"
        location = f"{relative_path}#/{collection}/{index - 1}"
        if not isinstance(value, Mapping):
            issues.append(AnchorIssue("ANCHOR_LEDGER_ENTRY", location))
            valid = False
            continue
        entry = dict(value)
        hash_field = (
            "eventSha256" if kind == "providerRuntime" else "entrySha256"
        )
        previous_field = (
            "previousEventSha256"
            if kind == "providerRuntime"
            else "previousEntrySha256"
        )
        entry_hash = entry.get(hash_field)
        calculated_hash = (
            canonical_runtime_event_sha256(entry)
            if kind == "providerRuntime"
            else canonical_entry_sha256(entry)
        )
        if (
            entry.get("sequence") != index
            or entry.get(previous_field) != previous
            or not isinstance(entry_hash, str)
            or HEX_SHA256.fullmatch(entry_hash) is None
            or calculated_hash != entry_hash
        ):
            issues.append(AnchorIssue("ANCHOR_LEDGER_CHAIN", location))
            valid = False
        entries.append(entry)
        hashes.append(entry_hash if isinstance(entry_hash, str) else "")
        previous = entry_hash if isinstance(entry_hash, str) else None

    expected_count_field = {
        "provider": "ledgerSequence",
        "providerRuntime": "eventCount",
    }.get(kind, "entryCount")
    if data.get(expected_count_field) != len(entries_value):
        issues.append(AnchorIssue("ANCHOR_LEDGER_COUNT", relative_path))
        valid = False
    if kind == "provider" and (
        data.get("usedTurns") != len(entries_value)
        or data.get("remainingTurns")
        != PROVIDER_MAX_TURNS - len(entries_value)
        or len(entries_value) > PROVIDER_MAX_TURNS
    ):
        issues.append(AnchorIssue("ANCHOR_LEDGER_COUNT", relative_path))
        valid = False
    expected_last = hashes[-1] if hashes else None
    expected_last_field = (
        "lastEventSha256"
        if kind == "providerRuntime"
        else "lastEntrySha256"
    )
    if kind != "provider" and data.get(expected_last_field) != expected_last:
        issues.append(AnchorIssue("ANCHOR_LEDGER_LAST", relative_path))
        valid = False

    attempt_events: list[Mapping[str, Any]] = []
    attempt_event_hashes: list[str] = []
    previous_attempt_event_hash: str | None = None
    if kind == "provider":
        for index, value in enumerate(attempt_events_value, start=1):
            location = f"{relative_path}#/attemptEvents/{index - 1}"
            if not isinstance(value, Mapping):
                issues.append(
                    AnchorIssue("ANCHOR_LEDGER_ATTEMPT_EVENT", location)
                )
                valid = False
                continue
            event = dict(value)
            event_hash = event.get("attemptEventSha256")
            if (
                event.get("attemptEventSequence") != index
                or event.get("previousAttemptEventSha256")
                != previous_attempt_event_hash
                or not isinstance(event_hash, str)
                or HEX_SHA256.fullmatch(event_hash) is None
                or canonical_attempt_event_sha256(event) != event_hash
            ):
                issues.append(
                    AnchorIssue("ANCHOR_LEDGER_ATTEMPT_EVENT_CHAIN", location)
                )
                valid = False
            attempt_events.append(event)
            attempt_event_hashes.append(
                event_hash if isinstance(event_hash, str) else ""
            )
            previous_attempt_event_hash = (
                event_hash if isinstance(event_hash, str) else None
            )
        if data.get("attemptEventCount") != len(attempt_events_value):
            issues.append(
                AnchorIssue("ANCHOR_LEDGER_ATTEMPT_EVENT_COUNT", relative_path)
            )
            valid = False
        if data.get("lastAttemptEventSha256") != previous_attempt_event_hash:
            issues.append(
                AnchorIssue("ANCHOR_LEDGER_ATTEMPT_EVENT_LAST", relative_path)
            )
            valid = False
    if not valid:
        return None
    return _LedgerView(
        kind=kind,
        path=relative_path,
        raw_sha256=document.sha256,
        entries=tuple(entries),
        entry_hashes=tuple(hashes),
        attempt_events=tuple(attempt_events),
        attempt_event_hashes=tuple(attempt_event_hashes),
    )


def _snapshot_ledger_values(
    snapshot: Mapping[str, Any],
    kind: str,
) -> _SnapshotLedgerValues | None:
    if kind == "firstFailure":
        binding = _mapping(snapshot.get("firstFailureLedger"))
        path = binding.get("path")
        raw_sha = binding.get("sha256")
        count = binding.get("entryCount")
        last = binding.get("lastEntrySha256")
    elif kind == "userDecision":
        binding = _mapping(snapshot.get("userDecisionLedger"))
        path = binding.get("path")
        raw_sha = binding.get("sha256")
        count = binding.get("entryCount")
        last = binding.get("lastEntrySha256")
    elif kind == "provider":
        binding = _mapping(
            _mapping(snapshot.get("authorization")).get("provider")
        )
        path = binding.get("ledgerPath")
        raw_sha = binding.get("ledgerSha256")
        count = binding.get("ledgerSequence")
        last = None
    elif kind == "providerRuntime":
        binding = _mapping(
            _mapping(snapshot.get("authorization")).get("provider")
        )
        path = binding.get("runtimeJournalPath")
        raw_sha = binding.get("runtimeJournalSha256")
        count = binding.get("runtimeEventCount")
        last = binding.get("lastRuntimeEventSha256")
    else:
        return None
    attempt_event_count: int | None = None
    last_attempt_event_sha: str | None = None
    if kind == "provider":
        attempt_event_count = binding.get("attemptEventCount")
        last_attempt_event_sha = binding.get("lastAttemptEventSha256")
    if (
        path != LEDGER_PATHS[kind]
        or not isinstance(raw_sha, str)
        or HEX_SHA256.fullmatch(raw_sha) is None
        or not isinstance(count, int)
        or isinstance(count, bool)
        or count < 0
        or (
            kind != "provider"
            and not (
                (count == 0 and last is None)
                or (
                    count > 0
                    and isinstance(last, str)
                    and HEX_SHA256.fullmatch(last) is not None
                )
            )
        )
        or (
            kind == "provider"
            and (
                not isinstance(attempt_event_count, int)
                or isinstance(attempt_event_count, bool)
                or attempt_event_count < 0
                or not (
                    (attempt_event_count == 0 and last_attempt_event_sha is None)
                    or (
                        attempt_event_count > 0
                        and isinstance(last_attempt_event_sha, str)
                        and HEX_SHA256.fullmatch(last_attempt_event_sha)
                        is not None
                    )
                )
            )
        )
    ):
        return None
    return _SnapshotLedgerValues(
        path=path,
        raw_sha256=raw_sha,
        entry_count=count,
        last_entry_sha256=last,
        attempt_event_count=attempt_event_count,
        last_attempt_event_sha256=last_attempt_event_sha,
    )


def _runtime_prefix_values(binding: Mapping[str, Any]) -> _SnapshotLedgerValues | None:
    path = binding.get("path")
    raw_sha = binding.get("rawFileSha256")
    count = binding.get("entryCount")
    last = binding.get("lastEntrySha256")
    if (
        path != PROVIDER_RUNTIME_JOURNAL_PATH
        or not isinstance(raw_sha, str)
        or HEX_SHA256.fullmatch(raw_sha) is None
        or not isinstance(count, int)
        or isinstance(count, bool)
        or count < 0
        or not (
            (count == 0 and last is None)
            or (
                count > 0
                and isinstance(last, str)
                and HEX_SHA256.fullmatch(last) is not None
            )
        )
    ):
        return None
    return _SnapshotLedgerValues(
        path=path,
        raw_sha256=raw_sha,
        entry_count=count,
        last_entry_sha256=last,
    )


def _binding_document(
    repo_root: Path,
    binding: Any,
    expected_path: str,
    location: str,
    issues: list[AnchorIssue],
) -> _Document | None:
    value = _mapping(binding)
    if set(value) != ARTIFACT_BINDING_FIELDS:
        issues.append(AnchorIssue("ANCHOR_BINDING_FIELDS", location))
    if value.get("path") != expected_path:
        issues.append(AnchorIssue("ANCHOR_BINDING_PATH", location))
    expected_sha = value.get("sha256")
    if not isinstance(expected_sha, str) or HEX_SHA256.fullmatch(expected_sha) is None:
        issues.append(AnchorIssue("ANCHOR_BINDING_SHA", location))
    document = _read_document(
        repo_root,
        expected_path,
        issues,
        missing_code="ANCHOR_ARTIFACT_MISSING",
        json_code="ANCHOR_ARTIFACT_JSON",
    )
    if (
        document is not None
        and isinstance(expected_sha, str)
        and document.sha256 != expected_sha
    ):
        issues.append(AnchorIssue("ANCHOR_ARTIFACT_HASH", location))
    return document


def _preseal_receipt_document(
    repo_root: Path,
    spec: GroupSpec,
    binding: Any,
    issues: list[AnchorIssue],
) -> _Document | None:
    location = f"{spec.anchor_path}#/presealReceipt"
    value = _mapping(binding)
    if set(value) != ARTIFACT_BINDING_FIELDS:
        issues.append(AnchorIssue("ANCHOR_PRESEAL_RECEIPT_BINDING", location))
    if value.get("path") != spec.preseal_receipt_path:
        issues.append(AnchorIssue("ANCHOR_PRESEAL_RECEIPT_BINDING", location))
    expected_sha = value.get("sha256")
    if not isinstance(expected_sha, str) or HEX_SHA256.fullmatch(expected_sha) is None:
        issues.append(AnchorIssue("ANCHOR_PRESEAL_RECEIPT_BINDING", location))
    document = _read_document(
        repo_root,
        spec.preseal_receipt_path,
        issues,
        missing_code="ANCHOR_PRESEAL_RECEIPT_MISSING",
        json_code="ANCHOR_PRESEAL_RECEIPT_JSON",
    )
    if (
        document is not None
        and isinstance(expected_sha, str)
        and document.sha256 != expected_sha
    ):
        issues.append(AnchorIssue("ANCHOR_PRESEAL_RECEIPT_HASH", location))
    return document


def _validate_preseal_source_binding(
    repo_root: Path,
    binding: Any,
    expected_path: str,
    location: str,
    issues: list[AnchorIssue],
) -> None:
    value = _mapping(binding)
    if (
        set(value) != ARTIFACT_BINDING_FIELDS
        or value.get("path") != expected_path
    ):
        issues.append(AnchorIssue("ANCHOR_PRESEAL_SOURCE_BINDING", location))
    expected_sha = value.get("sha256")
    if not isinstance(expected_sha, str) or HEX_SHA256.fullmatch(expected_sha) is None:
        issues.append(AnchorIssue("ANCHOR_PRESEAL_SOURCE_BINDING", location))
    raw = _read_bytes(
        repo_root,
        expected_path,
        issues,
        missing_code="ANCHOR_PRESEAL_SOURCE_MISSING",
        unsafe_code="ANCHOR_PATH_UNSAFE",
    )
    if (
        raw is not None
        and isinstance(expected_sha, str)
        and hashlib.sha256(raw).hexdigest() != expected_sha
    ):
        issues.append(AnchorIssue("ANCHOR_PRESEAL_SOURCE_HASH", location))


def _validate_gate_evidence_bindings(
    repo_root: Path,
    spec: GroupSpec,
    gate_id: str,
    gate: Mapping[str, Any],
    gate_binding: Mapping[str, Any],
    location: str,
    claimed_paths: set[str],
    issues: list[AnchorIssue],
) -> None:
    declared_items_value = gate.get("evidence")
    declared_items = (
        declared_items_value if isinstance(declared_items_value, list) else []
    )
    binding_items = _list(gate_binding.get("evidence"))
    declared_ids = [
        value.get("evidenceId") if isinstance(value, Mapping) else None
        for value in declared_items
    ]
    binding_ids = [
        value.get("evidenceId") if isinstance(value, Mapping) else None
        for value in binding_items
    ]
    if not declared_items or binding_ids != declared_ids:
        issues.append(
            AnchorIssue("ANCHOR_GATE_EVIDENCE_SET", f"{location}/evidence")
        )
    seen_ids: set[str] = set()
    for index, declared_item in enumerate(declared_items):
        pointer = f"{location}/evidence/{index}"
        declared = _mapping(declared_item)
        binding = _mapping(
            binding_items[index] if index < len(binding_items) else {}
        )
        evidence_id = declared.get("evidenceId")
        relative_path = declared.get("path")
        declared_sha = declared.get("sha256")
        if set(binding) != EVIDENCE_BINDING_FIELDS:
            issues.append(AnchorIssue("ANCHOR_GATE_EVIDENCE_BINDING", pointer))
        if (
            not isinstance(evidence_id, str)
            or not evidence_id
            or evidence_id in seen_ids
            or not isinstance(relative_path, str)
            or not isinstance(declared_sha, str)
            or HEX_SHA256.fullmatch(declared_sha) is None
            or binding.get("evidenceId") != evidence_id
            or binding.get("path") != relative_path
            or binding.get("sha256") != declared_sha
        ):
            issues.append(AnchorIssue("ANCHOR_GATE_EVIDENCE_BINDING", pointer))
        if isinstance(evidence_id, str):
            seen_ids.add(evidence_id)
        if not isinstance(relative_path, str):
            continue
        if relative_path in claimed_paths:
            issues.append(
                AnchorIssue("ANCHOR_GATE_EVIDENCE_PATH_REUSED", pointer)
            )
        else:
            claimed_paths.add(relative_path)
        if not _evidence_path_allowed(spec, gate_id, relative_path):
            issues.append(AnchorIssue("ANCHOR_GATE_EVIDENCE_PATH", pointer))
            continue
        actual_sha = _raw_file_sha256(
            repo_root,
            relative_path,
            issues,
            missing_code="ANCHOR_GATE_EVIDENCE_MISSING",
            unsafe_code="ANCHOR_GATE_EVIDENCE_PATH",
            too_large_code="ANCHOR_GATE_EVIDENCE_TOO_LARGE",
            unstable_code="ANCHOR_GATE_EVIDENCE_UNSTABLE",
            issue_location=pointer,
        )
        expected_sha = binding.get("sha256")
        if (
            actual_sha is not None
            and (
                not isinstance(expected_sha, str)
                or actual_sha != expected_sha
                or actual_sha != declared_sha
            )
        ):
            issues.append(AnchorIssue("ANCHOR_GATE_EVIDENCE_HASH", pointer))


def _validate_group_artifacts(
    evidence_root: Path,
    spec: GroupSpec,
    anchor: Mapping[str, Any],
    candidate: str | None,
    issues: list[AnchorIssue],
    *,
    candidate_root: Path,
) -> tuple[_Document | None, Mapping[str, Any], list[Mapping[str, Any]]]:
    anchor_path = spec.anchor_path
    gate_bindings = _list(anchor.get("gates"))
    gate_ids = [
        value.get("gateId") if isinstance(value, Mapping) else None
        for value in gate_bindings
    ]
    if gate_ids != list(spec.gate_ids):
        issues.append(AnchorIssue("ANCHOR_GATE_SET", f"{anchor_path}#/gates"))
    normalized_gate_bindings: list[Mapping[str, Any]] = []
    claimed_evidence_paths: set[str] = set()
    gate_candidates: list[str | None] = []
    for index, (gate_id, gate_path) in enumerate(
        zip(spec.gate_ids, spec.gate_paths)
    ):
        location = f"{anchor_path}#/gates/{index}"
        binding = gate_bindings[index] if index < len(gate_bindings) else {}
        value = _mapping(binding)
        normalized_gate_bindings.append(value)
        if set(value) != GATE_BINDING_FIELDS:
            issues.append(AnchorIssue("ANCHOR_GATE_BINDING", location))
        if value.get("gateId") != gate_id or value.get("path") != gate_path:
            issues.append(AnchorIssue("ANCHOR_GATE_BINDING", location))
        expected_sha = value.get("sha256")
        if not isinstance(expected_sha, str) or HEX_SHA256.fullmatch(expected_sha) is None:
            issues.append(AnchorIssue("ANCHOR_GATE_BINDING", location))
        gate_document = _read_document(
            evidence_root,
            gate_path,
            issues,
            missing_code="ANCHOR_ARTIFACT_MISSING",
            json_code="ANCHOR_ARTIFACT_JSON",
        )
        if gate_document is None:
            continue
        if isinstance(expected_sha, str) and gate_document.sha256 != expected_sha:
            issues.append(AnchorIssue("ANCHOR_ARTIFACT_HASH", location))
        gate = _mapping(gate_document.data)
        if (
            gate.get("week") != spec.week
            or gate.get("lane") != spec.lane
            or gate.get("gateId") != gate_id
            or gate.get("resultPath") != gate_path
        ):
            issues.append(AnchorIssue("ANCHOR_GATE_IDENTITY", location))
        if gate.get("status") != "Passed":
            issues.append(AnchorIssue("ANCHOR_GATE_STATUS", location))
        gate_candidate = _product_candidate(gate)
        gate_candidates.append(gate_candidate)
        if (
            candidate is None
            or gate_candidate is None
            or GIT_COMMIT.fullmatch(gate_candidate) is None
            or not _is_ancestor(candidate_root, gate_candidate, candidate)
        ):
            issues.append(AnchorIssue("ANCHOR_PRODUCT_CANDIDATE", location))
        _validate_gate_evidence_bindings(
            evidence_root,
            spec,
            gate_id,
            gate,
            value,
            location,
            claimed_evidence_paths,
            issues,
        )

    for index in range(1, len(gate_candidates)):
        previous_candidate = gate_candidates[index - 1]
        gate_candidate = gate_candidates[index]
        if (
            previous_candidate is None
            or gate_candidate is None
            or not _is_ancestor(
                candidate_root,
                previous_candidate,
                gate_candidate,
            )
        ):
            issues.append(
                AnchorIssue(
                    "ANCHOR_GATE_CANDIDATE_ORDER",
                    f"{anchor_path}#/gates/{index}",
                )
            )
    if not gate_candidates or gate_candidates[-1] != candidate:
        issues.append(
            AnchorIssue(
                "ANCHOR_FINAL_GATE_CANDIDATE",
                f"{anchor_path}#/gates",
            )
        )

    snapshot_document = _binding_document(
        evidence_root,
        anchor.get("goalControlSnapshot"),
        spec.snapshot_path,
        f"{anchor_path}#/goalControlSnapshot",
        issues,
    )
    snapshot = _mapping(snapshot_document.data) if snapshot_document else {}
    if snapshot_document is not None and (
        candidate is None or _product_candidate(snapshot) != candidate
    ):
        issues.append(
            AnchorIssue(
                "ANCHOR_PRODUCT_CANDIDATE",
                f"{anchor_path}#/goalControlSnapshot",
            )
        )

    handoff_document = _binding_document(
        evidence_root,
        anchor.get("canonicalHandoff"),
        spec.handoff_path,
        f"{anchor_path}#/canonicalHandoff",
        issues,
    )
    handoff = _mapping(handoff_document.data) if handoff_document else {}
    if handoff_document is not None:
        if (
            handoff.get("week") != spec.week
            or handoff.get("lane") != spec.lane
            or candidate is None
            or _product_candidate(handoff) != candidate
        ):
            issues.append(
                AnchorIssue(
                    "ANCHOR_HANDOFF_IDENTITY",
                    f"{anchor_path}#/canonicalHandoff",
                )
            )
        if (
            handoff.get("decision")
            not in {"ReadyForNextCheckpoint", "GoalComplete"}
            or handoff.get("requiredGatesSatisfied") is not True
        ):
            issues.append(
                AnchorIssue(
                    "ANCHOR_HANDOFF_STATUS",
                    f"{anchor_path}#/canonicalHandoff",
                )
            )
        references = _list(handoff.get("gateResults"))
        reference_ids = [
            item.get("gateId") if isinstance(item, Mapping) else None
            for item in references
        ]
        if reference_ids != list(spec.gate_ids):
            issues.append(
                AnchorIssue(
                    "ANCHOR_HANDOFF_GATE_SET",
                    f"{anchor_path}#/canonicalHandoff",
                )
            )
        for index, (gate_id, gate_path) in enumerate(
            zip(spec.gate_ids, spec.gate_paths)
        ):
            if index >= len(references):
                break
            reference = _mapping(references[index])
            binding = (
                normalized_gate_bindings[index]
                if index < len(normalized_gate_bindings)
                else {}
            )
            if (
                reference.get("gateId") != gate_id
                or reference.get("resultPath") != gate_path
                or reference.get("resultSha256") != binding.get("sha256")
            ):
                issues.append(
                    AnchorIssue(
                        "ANCHOR_HANDOFF_GATE_BINDING",
                        f"{anchor_path}#/canonicalHandoff/gateResults/{index}",
                    )
                )

    alias_value = anchor.get("compatibilityHandoffAlias")
    if spec.alias_path is None:
        if alias_value is not None:
            issues.append(
                AnchorIssue(
                    "ANCHOR_ALIAS_UNEXPECTED",
                    f"{anchor_path}#/compatibilityHandoffAlias",
                )
            )
    else:
        alias_document = _binding_document(
            evidence_root,
            alias_value,
            spec.alias_path,
            f"{anchor_path}#/compatibilityHandoffAlias",
            issues,
        )
        if alias_document is not None:
            if handoff_document is not None and alias_document.data != handoff_document.data:
                issues.append(
                    AnchorIssue(
                        "ANCHOR_ALIAS_NOT_EQUIVALENT",
                        f"{anchor_path}#/compatibilityHandoffAlias",
                    )
                )
            if candidate is None or _product_candidate(alias_document.data) != candidate:
                issues.append(
                    AnchorIssue(
                        "ANCHOR_PRODUCT_CANDIDATE",
                        f"{anchor_path}#/compatibilityHandoffAlias",
                    )
                )
    return snapshot_document, snapshot, normalized_gate_bindings


def _validate_ledger_prefixes(
    repo_root: Path,
    spec: GroupSpec,
    anchor: Mapping[str, Any],
    snapshot: Mapping[str, Any],
    ledger_cache: dict[str, _LedgerView | None],
    issues: list[AnchorIssue],
) -> tuple[dict[str, int], int | None]:
    anchor_path = spec.anchor_path
    prefixes = _mapping(anchor.get("ledgerPrefixes"))
    if set(prefixes) != set(LEDGER_PATHS):
        issues.append(
            AnchorIssue("ANCHOR_LEDGER_SET", f"{anchor_path}#/ledgerPrefixes")
        )
    counts: dict[str, int] = {}
    provider_attempt_event_count: int | None = None
    for kind, expected_path in LEDGER_PATHS.items():
        location = f"{anchor_path}#/ledgerPrefixes/{kind}"
        binding = _mapping(prefixes.get(kind))
        expected_fields = (
            PROVIDER_LEDGER_PREFIX_FIELDS
            if kind == "provider"
            else LEDGER_PREFIX_FIELDS
        )
        if set(binding) != expected_fields:
            issues.append(AnchorIssue("ANCHOR_LEDGER_BINDING", location))
        snapshot_values = _snapshot_ledger_values(snapshot, kind)
        if snapshot_values is None:
            issues.append(AnchorIssue("ANCHOR_SNAPSHOT_LEDGER", location))
            continue
        path = snapshot_values.path
        raw_sha = snapshot_values.raw_sha256
        count = snapshot_values.entry_count
        snapshot_last = snapshot_values.last_entry_sha256
        counts[kind] = count
        if (
            binding.get("path") != path
            or binding.get("rawFileSha256") != raw_sha
            or binding.get("entryCount") != count
        ):
            issues.append(AnchorIssue("ANCHOR_LEDGER_BINDING", location))
        attempt_event_count = snapshot_values.attempt_event_count
        snapshot_attempt_event_last = (
            snapshot_values.last_attempt_event_sha256
        )
        if kind == "provider":
            provider_attempt_event_count = attempt_event_count
            if (
                binding.get("attemptEventCount") != attempt_event_count
                or binding.get("lastAttemptEventSha256")
                != snapshot_attempt_event_last
            ):
                issues.append(AnchorIssue("ANCHOR_LEDGER_BINDING", location))
        if kind not in ledger_cache:
            ledger_cache[kind] = _load_ledger(repo_root, kind, issues)
        ledger = ledger_cache[kind]
        if ledger is None:
            continue
        if count > ledger.entry_count:
            issues.append(AnchorIssue("ANCHOR_LEDGER_PREFIX", location))
            continue
        expected_last = ledger.entry_hashes[count - 1] if count else None
        if snapshot_last is not None and snapshot_last != expected_last:
            issues.append(AnchorIssue("ANCHOR_SNAPSHOT_LEDGER", location))
        if binding.get("lastEntrySha256") != expected_last:
            issues.append(AnchorIssue("ANCHOR_LEDGER_PREFIX", location))
        if kind == "provider":
            if (
                attempt_event_count is None
                or attempt_event_count > ledger.attempt_event_count
            ):
                issues.append(AnchorIssue("ANCHOR_LEDGER_PREFIX", location))
                continue
            expected_attempt_event_last = (
                ledger.attempt_event_hashes[attempt_event_count - 1]
                if attempt_event_count
                else None
            )
            if snapshot_attempt_event_last != expected_attempt_event_last:
                issues.append(AnchorIssue("ANCHOR_SNAPSHOT_LEDGER", location))
            if (
                binding.get("lastAttemptEventSha256")
                != expected_attempt_event_last
            ):
                issues.append(AnchorIssue("ANCHOR_LEDGER_PREFIX", location))
            expected_attempt_event_prefix = attempt_event_prefix_sha256(
                expected_path,
                ledger.attempt_events,
                attempt_event_count,
            )
            if (
                binding.get("attemptEventPrefixSha256")
                != expected_attempt_event_prefix
            ):
                issues.append(AnchorIssue("ANCHOR_LEDGER_PREFIX", location))
        expected_prefix = ledger_prefix_sha256(
            kind,
            expected_path,
            ledger.entries,
            count,
            attempt_events=(
                ledger.attempt_events if kind == "provider" else ()
            ),
            attempt_event_count=(
                attempt_event_count
                if kind == "provider" and attempt_event_count is not None
                else 0
            ),
        )
        if binding.get("prefixSha256") != expected_prefix:
            issues.append(AnchorIssue("ANCHOR_LEDGER_PREFIX", location))
        prefix_is_current_full_ledger = ledger.entry_count == count and (
            kind != "provider"
            or ledger.attempt_event_count == attempt_event_count
        )
        if prefix_is_current_full_ledger and ledger.raw_sha256 != raw_sha:
            issues.append(AnchorIssue("ANCHOR_LEDGER_RAW", location))
    return counts, provider_attempt_event_count


def _validate_preseal_receipt(
    evidence_root: Path,
    spec: GroupSpec,
    anchor: Mapping[str, Any],
    issues: list[AnchorIssue],
    *,
    candidate_root: Path,
) -> None:
    receipt_document = _preseal_receipt_document(
        evidence_root,
        spec,
        anchor.get("presealReceipt"),
        issues,
    )
    if receipt_document is None:
        return
    receipt_path = spec.preseal_receipt_path
    receipt = _mapping(receipt_document.data)
    if set(receipt) != PRESEAL_RECEIPT_FIELDS:
        issues.append(AnchorIssue("ANCHOR_PRESEAL_RECEIPT_FIELDS", receipt_path))
    if (
        receipt.get("schemaVersion") != SCHEMA_VERSION
        or receipt.get("receiptVersion") != PRESEAL_RECEIPT_VERSION
        or receipt.get("registryVersion") != REGISTRY_VERSION
        or receipt.get("goalId") != GOAL_ID
        or receipt.get("groupId") != spec.group_id
        or receipt.get("week") != spec.week
        or receipt.get("lane") != spec.lane
    ):
        issues.append(AnchorIssue("ANCHOR_PRESEAL_RECEIPT_IDENTITY", receipt_path))
    if not _preseal_interval_is_valid(
        receipt.get("startedAt"), receipt.get("finishedAt")
    ):
        issues.append(
            AnchorIssue("ANCHOR_PRESEAL_RECEIPT_TIMESTAMPS", receipt_path)
        )
    if type(receipt.get("exitCode")) is not int or receipt.get("exitCode") != 0:
        issues.append(AnchorIssue("ANCHOR_PRESEAL_RECEIPT_EXIT", receipt_path))
    for field, expected_path in PRESEAL_SOURCE_PATHS.items():
        _validate_preseal_source_binding(
            candidate_root,
            receipt.get(field),
            expected_path,
            f"{receipt_path}#/{field}",
            issues,
        )
    for field in sorted(PRESEAL_BOUND_ANCHOR_FIELDS):
        if receipt.get(field) != anchor.get(field):
            issues.append(
                AnchorIssue(
                    "ANCHOR_PRESEAL_RECEIPT_STALE",
                    f"{receipt_path}#/{field}",
                )
            )


def _validate_anchor_document(
    candidate_root: Path,
    evidence_root: Path,
    spec: GroupSpec,
    ledger_cache: dict[str, _LedgerView | None],
    issues: list[AnchorIssue],
) -> _AnchorRecord | None:
    document = _read_document(
        candidate_root,
        spec.anchor_path,
        issues,
        missing_code="ANCHOR_MISSING",
        json_code="ANCHOR_JSON",
    )
    if document is None:
        return None
    anchor = _mapping(document.data)
    if set(anchor) != ANCHOR_FIELDS:
        issues.append(AnchorIssue("ANCHOR_FIELDS", spec.anchor_path))
    if (
        anchor.get("schemaVersion") != SCHEMA_VERSION
        or anchor.get("registryVersion") != REGISTRY_VERSION
        or anchor.get("goalId") != GOAL_ID
        or anchor.get("groupId") != spec.group_id
        or anchor.get("week") != spec.week
        or anchor.get("lane") != spec.lane
    ):
        issues.append(AnchorIssue("ANCHOR_IDENTITY", spec.anchor_path))
    candidate = anchor.get("productCandidate")
    if not isinstance(candidate, str) or GIT_COMMIT.fullmatch(candidate) is None:
        issues.append(AnchorIssue("ANCHOR_PRODUCT_CANDIDATE", spec.anchor_path))
        candidate = None

    _snapshot_document, snapshot, _gate_bindings = _validate_group_artifacts(
        evidence_root,
        spec,
        anchor,
        candidate,
        issues,
        candidate_root=candidate_root,
    )
    ledger_counts, provider_attempt_event_count = _validate_ledger_prefixes(
        evidence_root,
        spec,
        anchor,
        snapshot,
        ledger_cache,
        issues,
    )
    _validate_preseal_receipt(
        evidence_root,
        spec,
        anchor,
        issues,
        candidate_root=candidate_root,
    )
    return _AnchorRecord(
        spec=spec,
        document=document,
        data=anchor,
        product_candidate=candidate,
        ledger_counts=ledger_counts,
        provider_attempt_event_count=provider_attempt_event_count,
    )


def _git(
    repo_root: Path,
    arguments: Sequence[str],
) -> tuple[int, bytes]:
    try:
        command = trusted_executor._git_command(repo_root, arguments)
        executable, before_identity = trusted_executor._trusted_git_identity(
            repo_root
        )
        result = subprocess.run(
            command,
            cwd=repo_root,
            env=trusted_executor._git_environment(executable),
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            shell=False,
            check=False,
            timeout=15,
        )
        after_executable, after_identity = (
            trusted_executor._trusted_git_identity(repo_root)
        )
        if executable != after_executable or before_identity != after_identity:
            return 127, b""
    except (
        OSError,
        subprocess.SubprocessError,
        trusted_executor.ExecutorError,
    ):
        return 127, b""
    return result.returncode, result.stdout


def _trusted_git_validation_bundle(function):
    """Raw-pin Git across one complete anchor validation invocation."""

    @wraps(function)
    def guarded(repo_root: Path, *args, **kwargs):
        root = Path(repo_root).resolve()
        try:
            before_path, before_identity = trusted_executor._trusted_git_identity(
                root, rehash=True
            )
        except (trusted_executor.ExecutorError, OSError):
            return [AnchorIssue("TRUSTED_GIT_TOOL_IDENTITY", "trusted-git")]
        try:
            result = function(repo_root, *args, **kwargs)
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
                AnchorIssue("TRUSTED_GIT_TOOL_IDENTITY", "trusted-git")
            )
        return sorted(set(result))

    return guarded


def _git_path_is_reparse(path: Path) -> bool:
    try:
        attributes = getattr(os.lstat(path), "st_file_attributes", 0)
    except OSError:
        return True
    return path.is_symlink() or bool(attributes & 0x400)


def _expected_git_directory(repo_root: Path) -> Path | None:
    control = repo_root / ".git"
    if _git_path_is_reparse(control):
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
    if not resolved.is_dir() or _git_path_is_reparse(resolved):
        return None
    return resolved


def _git_repository_is_trusted(repo_root: Path) -> bool:
    expected_git = _expected_git_directory(repo_root)
    if expected_git is None:
        return False
    queries = {
        "top": ["rev-parse", "--show-toplevel"],
        "git": ["rev-parse", "--absolute-git-dir"],
        "common": ["rev-parse", "--git-common-dir"],
        "format": ["rev-parse", "--show-object-format"],
        "shallow": ["rev-parse", "--is-shallow-repository"],
        "replace": ["for-each-ref", "--format=%(refname)", "refs/replace/"],
    }
    values: dict[str, str] = {}
    for name, arguments in queries.items():
        code, raw = _git(repo_root, arguments)
        if code != 0:
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
    return not (
        top != repo_root
        or actual_git != expected_git
        or not common.is_dir()
        or _git_path_is_reparse(common)
        or values["format"] != "sha1"
        or values["shallow"] != "false"
        or bool(values["replace"])
        or (common / "shallow").exists()
        or (common / "info" / "grafts").exists()
        or (common / "objects" / "info" / "alternates").exists()
    )


def _is_ancestor(repo_root: Path, ancestor: str, descendant: str) -> bool:
    if GIT_COMMIT.fullmatch(ancestor) is None or GIT_COMMIT.fullmatch(descendant) is None:
        return False
    return _git(
        repo_root,
        ["merge-base", "--is-ancestor", ancestor, descendant],
    )[0] == 0


def _validate_git_anchor(
    repo_root: Path,
    record: _AnchorRecord,
    issues: list[AnchorIssue],
) -> str | None:
    path = record.spec.anchor_path
    if _git(repo_root, ["ls-files", "--error-unmatch", "--", path])[0] != 0:
        issues.append(AnchorIssue("ANCHOR_GIT_UNTRACKED", path))
        return None
    status_code, status_output = _git(
        repo_root,
        ["status", "--porcelain=v1", "--untracked-files=all", "--", path],
    )
    if status_code != 0:
        issues.append(AnchorIssue("ANCHOR_GIT_STATUS", path))
        return None
    if status_output.strip():
        issues.append(AnchorIssue("ANCHOR_GIT_DIRTY", path))

    index_code, index_blob_id_raw = _git(repo_root, ["rev-parse", f":{path}"])
    index_blob_id = index_blob_id_raw.decode("ascii", errors="ignore").strip()
    index_bytes: bytes | None = None
    if index_code != 0 or not re.fullmatch(r"[0-9a-f]{40,64}", index_blob_id):
        issues.append(AnchorIssue("ANCHOR_GIT_INDEX", path))
    else:
        index_read_code, index_bytes_value = _git(
            repo_root,
            ["cat-file", "blob", index_blob_id],
        )
        if index_read_code != 0:
            issues.append(AnchorIssue("ANCHOR_GIT_INDEX", path))
        else:
            index_bytes = index_bytes_value
            if index_bytes != record.document.raw:
                issues.append(AnchorIssue("ANCHOR_GIT_INDEX_BLOB", path))

    blob_code, blob_id_raw = _git(repo_root, ["rev-parse", f"HEAD:{path}"])
    blob_id = blob_id_raw.decode("ascii", errors="ignore").strip()
    head_bytes: bytes | None = None
    if blob_code != 0 or not re.fullmatch(r"[0-9a-f]{40,64}", blob_id):
        issues.append(AnchorIssue("ANCHOR_GIT_UNCOMMITTED", path))
        return None
    head_code, head_bytes_value = _git(repo_root, ["cat-file", "blob", blob_id])
    if head_code != 0:
        issues.append(AnchorIssue("ANCHOR_GIT_HEAD_BLOB", path))
    else:
        head_bytes = head_bytes_value
        if head_bytes != record.document.raw:
            issues.append(AnchorIssue("ANCHOR_GIT_HEAD_BLOB", path))

    add_code, add_commits_raw = _git(
        repo_root,
        ["log", "--diff-filter=A", "--format=%H", "--reverse", "--", path],
    )
    add_commits = [
        value
        for value in add_commits_raw.decode("ascii", errors="ignore").splitlines()
        if value
    ]
    if (
        add_code != 0
        or len(add_commits) != 1
        or GIT_COMMIT.fullmatch(add_commits[0]) is None
    ):
        issues.append(AnchorIssue("ANCHOR_GIT_HISTORY", path))
        return None
    anchor_commit = add_commits[0]
    history_code, history_commits_raw = _git(
        repo_root,
        ["log", "--format=%H", "--reverse", "--", path],
    )
    history_commits = [
        value
        for value in history_commits_raw.decode("ascii", errors="ignore").splitlines()
        if value
    ]
    if history_code != 0 or history_commits != [anchor_commit]:
        issues.append(AnchorIssue("ANCHOR_GIT_HISTORY_MUTATION", path))

    first_blob_code, first_blob_id_raw = _git(
        repo_root,
        ["rev-parse", f"{anchor_commit}:{path}"],
    )
    first_blob_id = first_blob_id_raw.decode("ascii", errors="ignore").strip()
    if (
        first_blob_code != 0
        or not re.fullmatch(r"[0-9a-f]{40,64}", first_blob_id)
    ):
        issues.append(AnchorIssue("ANCHOR_GIT_HISTORY", path))
        return None
    first_read_code, first_bytes = _git(
        repo_root,
        ["cat-file", "blob", first_blob_id],
    )
    if first_read_code != 0:
        issues.append(AnchorIssue("ANCHOR_GIT_HISTORY", path))
        return None
    if (
        record.document.raw != first_bytes
        or index_bytes != first_bytes
        or head_bytes != first_bytes
    ):
        issues.append(AnchorIssue("ANCHOR_GIT_IMMUTABLE", path))

    candidate = record.product_candidate
    if candidate is None:
        return anchor_commit
    if _git(repo_root, ["cat-file", "-e", f"{candidate}^{{commit}}"]) [0] != 0:
        issues.append(AnchorIssue("ANCHOR_PRODUCT_COMMIT", path))
    elif not _is_ancestor(repo_root, candidate, anchor_commit):
        issues.append(AnchorIssue("ANCHOR_PRODUCT_ANCESTRY", path))
    return anchor_commit


def _validate_parent_dag(
    repo_root: Path,
    records: Mapping[str, _AnchorRecord],
    anchor_commits: Mapping[str, str],
    issues: list[AnchorIssue],
) -> None:
    for group_id, record in records.items():
        spec = record.spec
        location = f"{spec.anchor_path}#/parentAnchors"
        parents = _list(record.data.get("parentAnchors"))
        parent_ids = [
            value.get("groupId") if isinstance(value, Mapping) else None
            for value in parents
        ]
        if parent_ids != list(spec.parent_group_ids):
            issues.append(AnchorIssue("ANCHOR_PARENT_SET", location))
        for index, parent_group_id in enumerate(spec.parent_group_ids):
            pointer = f"{location}/{index}"
            value = _mapping(parents[index] if index < len(parents) else {})
            parent_spec = GROUP_BY_ID[parent_group_id]
            if set(value) != PARENT_BINDING_FIELDS:
                issues.append(AnchorIssue("ANCHOR_PARENT_BINDING", pointer))
            if (
                value.get("groupId") != parent_group_id
                or value.get("path") != parent_spec.anchor_path
            ):
                issues.append(AnchorIssue("ANCHOR_PARENT_BINDING", pointer))
            parent = records.get(parent_group_id)
            if parent is None:
                issues.append(AnchorIssue("ANCHOR_PARENT_MISSING", pointer))
                continue
            if value.get("sha256") != parent.document.sha256:
                issues.append(AnchorIssue("ANCHOR_PARENT_HASH", pointer))
            parent_commit = anchor_commits.get(parent_group_id)
            child_commit = anchor_commits.get(group_id)
            if (
                parent_commit is not None
                and child_commit is not None
                and (
                    parent_commit == child_commit
                    or not _is_ancestor(
                        repo_root,
                        parent_commit,
                        child_commit,
                    )
                )
            ):
                issues.append(
                    AnchorIssue("ANCHOR_PARENT_COMMIT_ANCESTRY", pointer)
                )
            for kind in LEDGER_PATHS:
                parent_count = parent.ledger_counts.get(kind)
                child_count = record.ledger_counts.get(kind)
                regressed = (
                    parent_count is None
                    or child_count is None
                    or child_count < parent_count
                )
                if kind == "provider":
                    parent_attempt_event_count = (
                        parent.provider_attempt_event_count
                    )
                    child_attempt_event_count = (
                        record.provider_attempt_event_count
                    )
                    regressed = regressed or (
                        parent_attempt_event_count is None
                        or child_attempt_event_count is None
                        or child_attempt_event_count
                        < parent_attempt_event_count
                    )
                if regressed:
                    issues.append(
                        AnchorIssue(
                            "ANCHOR_PARENT_LEDGER_PREFIX",
                            f"{pointer}/ledgerPrefixes/{kind}",
                        )
                    )
            if (
                parent.product_candidate is not None
                and record.product_candidate is not None
                and not _is_ancestor(
                    repo_root,
                    parent.product_candidate,
                    record.product_candidate,
                )
            ):
                issues.append(
                    AnchorIssue("ANCHOR_PARENT_PRODUCT_ANCESTRY", pointer)
                )


def _unexpected_anchor_files(repo_root: Path) -> bool:
    directory = _safe_path(repo_root, ANCHOR_DIRECTORY)
    if directory is None:
        lexical = repo_root.joinpath(*PurePosixPath(ANCHOR_DIRECTORY).parts)
        try:
            return lexical.exists() or lexical.is_symlink()
        except OSError:
            return True
    if not directory.exists():
        return False
    if not directory.is_dir() or directory.is_symlink():
        return True
    expected_names = {f"{group.group_id}.json" for group in CANONICAL_GROUPS}
    try:
        return any(
            child.name not in expected_names
            or child.is_symlink()
            or not child.is_file()
            for child in directory.iterdir()
        )
    except OSError:
        return True


def _ready_handoff_group_ids(
    repo_root: Path,
    issues: list[AnchorIssue],
) -> set[str]:
    """Return groups whose canonical handoff already claims completion."""

    completed: set[str] = set()
    for spec in CANONICAL_GROUPS:
        path = _safe_path(repo_root, spec.handoff_path)
        if path is None:
            issues.append(AnchorIssue("ANCHOR_PATH_UNSAFE", spec.handoff_path))
            continue
        try:
            present = path.is_file()
        except OSError:
            present = False
        if not present:
            continue
        document = _read_document(
            repo_root,
            spec.handoff_path,
            issues,
            missing_code="ANCHOR_READY_HANDOFF_MISSING",
            json_code="ANCHOR_READY_HANDOFF_JSON",
        )
        if document is None:
            continue
        handoff = _mapping(document.data)
        if handoff.get("decision") in {
            "ReadyForNextCheckpoint",
            "GoalComplete",
        }:
            completed.add(spec.group_id)
    return completed


def _historically_added_anchor_paths(
    repo_root: Path,
    issues: list[AnchorIssue],
) -> set[str]:
    """Return fixed anchor paths ever added on the current HEAD history."""

    repository_code, _repository_output = _git(
        repo_root,
        ["rev-parse", "--git-dir"],
    )
    if repository_code != 0:
        return set()
    history_code, history_output = _git(
        repo_root,
        [
            "log",
            "--diff-filter=A",
            "--format=",
            "--name-only",
            "--",
            ANCHOR_DIRECTORY,
        ],
    )
    if history_code != 0:
        issues.append(AnchorIssue("ANCHOR_GIT_HISTORY", ANCHOR_DIRECTORY))
        return set()
    expected = set(ANCHOR_PATH_TO_GROUP)
    return {
        line.strip().replace("\\", "/")
        for line in history_output.decode("utf-8", errors="ignore").splitlines()
        if line.strip().replace("\\", "/") in expected
    }


@_trusted_git_validation_bundle
def validate_evidence_anchors(
    repo_root: Path,
    *,
    control_root: Path | None = None,
    complete: bool = False,
    allow_unsealed_ready_group: str | None = None,
) -> list[AnchorIssue]:
    """Validate the fixed tracked anchor DAG and all bound ignored evidence.

    ``complete=False`` accepts an empty or partial downward-closed DAG.
    ``complete=True`` requires all fourteen canonical groups.
    ``allow_unsealed_ready_group`` is the one-shot pre-seal mode: exactly that
    ready group may still lack its anchor while the caller reviews the complete
    handoff and builds the immutable anchor.  Historical deletion is never
    exempted, and every parent anchor remains mandatory.
    """

    root = repo_root.resolve()
    evidence_root = (control_root or repo_root).resolve()
    issues: list[AnchorIssue] = []
    if not _git_repository_is_trusted(root):
        issues.append(AnchorIssue("ANCHOR_GIT_REPOSITORY", ANCHOR_DIRECTORY))
    if _unexpected_anchor_files(root):
        issues.append(AnchorIssue("ANCHOR_UNEXPECTED", ANCHOR_DIRECTORY))
    required_group_ids = _ready_handoff_group_ids(evidence_root, issues)
    historical_anchor_paths = _historically_added_anchor_paths(root, issues)
    preseal_spec = (
        GROUP_BY_ID.get(allow_unsealed_ready_group)
        if allow_unsealed_ready_group is not None
        else None
    )
    if allow_unsealed_ready_group is not None and preseal_spec is None:
        issues.append(AnchorIssue("ANCHOR_PRESEAL_GROUP", ANCHOR_DIRECTORY))
    if preseal_spec is not None and preseal_spec.group_id not in required_group_ids:
        issues.append(
            AnchorIssue("ANCHOR_PRESEAL_NOT_READY", preseal_spec.handoff_path)
        )

    present_specs: list[GroupSpec] = []
    for spec in CANONICAL_GROUPS:
        path = _safe_path(root, spec.anchor_path)
        present = path is not None and path.is_file()
        if present:
            present_specs.append(spec)
            if preseal_spec is not None and spec.group_id == preseal_spec.group_id:
                issues.append(
                    AnchorIssue("ANCHOR_PRESEAL_ALREADY_SEALED", spec.anchor_path)
                )
        elif spec.anchor_path in historical_anchor_paths:
            issues.append(AnchorIssue("ANCHOR_GIT_DELETED", spec.anchor_path))
        elif complete or (
            spec.group_id in required_group_ids
            and (preseal_spec is None or spec.group_id != preseal_spec.group_id)
        ):
            issues.append(AnchorIssue("ANCHOR_MISSING", spec.anchor_path))

    if preseal_spec is not None:
        present_ids = {spec.group_id for spec in present_specs}
        for parent_group_id in preseal_spec.parent_group_ids:
            if parent_group_id not in present_ids:
                issues.append(
                    AnchorIssue(
                        "ANCHOR_PRESEAL_PARENT_MISSING",
                        GROUP_BY_ID[parent_group_id].anchor_path,
                    )
                )

    ledger_cache: dict[str, _LedgerView | None] = {}
    records: dict[str, _AnchorRecord] = {}
    for spec in present_specs:
        record = _validate_anchor_document(
            root,
            evidence_root,
            spec,
            ledger_cache,
            issues,
        )
        if record is not None:
            records[spec.group_id] = record

    anchor_commits: dict[str, str] = {}
    for group_id, record in records.items():
        anchor_commit = _validate_git_anchor(root, record, issues)
        if anchor_commit is not None:
            anchor_commits[group_id] = anchor_commit
    _validate_parent_dag(root, records, anchor_commits, issues)
    return sorted(set(issues))


def _builder_ledger_view(repo_root: Path, kind: str) -> _LedgerView:
    issues: list[AnchorIssue] = []
    view = _load_ledger(repo_root, kind, issues)
    if view is None:
        issue = issues[0] if issues else AnchorIssue(
            "BUILD_LEDGER_INVALID", LEDGER_PATHS[kind]
        )
        raise AnchorBuildError(issue.code, issue.location)
    return view


def _builder_binding(document: _Document) -> dict[str, Any]:
    return {"path": document.relative_path, "sha256": document.sha256}


def _builder_gate_evidence_bindings(
    repo_root: Path,
    spec: GroupSpec,
    gate_id: str,
    gate: Mapping[str, Any],
    claimed_paths: set[str],
) -> list[dict[str, str]]:
    evidence_items = gate.get("evidence")
    if not isinstance(evidence_items, list) or not evidence_items:
        raise AnchorBuildError(
            "BUILD_GATE_EVIDENCE_SET",
            f"{spec.artifact_directory}/gates/{gate_id}.json#/evidence",
        )
    bindings: list[dict[str, str]] = []
    evidence_ids: set[str] = set()
    for index, item in enumerate(evidence_items):
        location = f"{spec.artifact_directory}/gates/{gate_id}.json#/evidence/{index}"
        evidence = _mapping(item)
        evidence_id = evidence.get("evidenceId")
        relative_path = evidence.get("path")
        declared_sha = evidence.get("sha256")
        if (
            not isinstance(evidence_id, str)
            or not evidence_id
            or evidence_id in evidence_ids
            or not isinstance(relative_path, str)
            or not isinstance(declared_sha, str)
            or HEX_SHA256.fullmatch(declared_sha) is None
        ):
            raise AnchorBuildError("BUILD_GATE_EVIDENCE_BINDING", location)
        if relative_path in claimed_paths:
            raise AnchorBuildError("BUILD_GATE_EVIDENCE_PATH_REUSED", location)
        if not _evidence_path_allowed(spec, gate_id, relative_path):
            raise AnchorBuildError("BUILD_GATE_EVIDENCE_PATH", location)
        actual_sha = _builder_evidence_sha256(
            repo_root,
            relative_path,
            location,
        )
        if actual_sha != declared_sha:
            raise AnchorBuildError("BUILD_GATE_EVIDENCE_HASH", location)
        evidence_ids.add(evidence_id)
        claimed_paths.add(relative_path)
        bindings.append(
            {
                "evidenceId": evidence_id,
                "path": relative_path,
                "sha256": actual_sha,
            }
        )
    return bindings


def _build_seal_payload(
    repo_root: Path,
    group_id: str,
    product_candidate: str,
    *,
    control_root: Path | None = None,
) -> dict[str, Any]:
    """Build the receipt/anchor fields that bind one current group state."""

    candidate_root = repo_root.resolve()
    evidence_root = (control_root or repo_root).resolve()
    spec = GROUP_BY_ID.get(group_id)
    if spec is None:
        raise AnchorBuildError("BUILD_GROUP_UNKNOWN", ANCHOR_DIRECTORY)
    if GIT_COMMIT.fullmatch(product_candidate) is None:
        raise AnchorBuildError("BUILD_PRODUCT_CANDIDATE", spec.anchor_path)

    gate_bindings: list[dict[str, Any]] = []
    gate_documents: list[_Document] = []
    gate_candidates: list[str] = []
    claimed_evidence_paths: set[str] = set()
    for gate_id, gate_path in zip(spec.gate_ids, spec.gate_paths):
        document = _builder_document(evidence_root, gate_path)
        gate = _mapping(document.data)
        if (
            gate.get("week") != spec.week
            or gate.get("lane") != spec.lane
            or gate.get("gateId") != gate_id
            or gate.get("resultPath") != gate_path
            or gate.get("status") != "Passed"
        ):
            raise AnchorBuildError("BUILD_GATE_IDENTITY", gate_path)
        gate_candidate = _product_candidate(gate)
        if (
            gate_candidate is None
            or GIT_COMMIT.fullmatch(gate_candidate) is None
            or not _is_ancestor(
                candidate_root, gate_candidate, product_candidate
            )
            or (
                gate_candidates
                and not _is_ancestor(
                    candidate_root, gate_candidates[-1], gate_candidate
                )
            )
        ):
            raise AnchorBuildError("BUILD_GATE_CANDIDATE_ORDER", gate_path)
        gate_candidates.append(gate_candidate)
        evidence_bindings = _builder_gate_evidence_bindings(
            evidence_root,
            spec,
            gate_id,
            gate,
            claimed_evidence_paths,
        )
        gate_documents.append(document)
        gate_bindings.append(
            {
                "gateId": gate_id,
                "path": gate_path,
                "sha256": document.sha256,
                "evidence": evidence_bindings,
            }
        )

    if not gate_candidates or gate_candidates[-1] != product_candidate:
        raise AnchorBuildError("BUILD_FINAL_GATE_CANDIDATE", spec.anchor_path)

    snapshot_document = _builder_document(evidence_root, spec.snapshot_path)
    snapshot = _mapping(snapshot_document.data)
    if _product_candidate(snapshot) != product_candidate:
        raise AnchorBuildError("BUILD_SNAPSHOT_IDENTITY", spec.snapshot_path)

    handoff_document = _builder_document(evidence_root, spec.handoff_path)
    handoff = _mapping(handoff_document.data)
    if (
        handoff.get("week") != spec.week
        or handoff.get("lane") != spec.lane
        or handoff.get("decision")
        not in {"ReadyForNextCheckpoint", "GoalComplete"}
        or handoff.get("requiredGatesSatisfied") is not True
        or _product_candidate(handoff) != product_candidate
    ):
        raise AnchorBuildError("BUILD_HANDOFF_IDENTITY", spec.handoff_path)
    references = _list(handoff.get("gateResults"))
    if [
        item.get("gateId") if isinstance(item, Mapping) else None
        for item in references
    ] != list(spec.gate_ids):
        raise AnchorBuildError("BUILD_HANDOFF_GATE_SET", spec.handoff_path)
    for index, (gate_id, gate_path, gate_document) in enumerate(
        zip(spec.gate_ids, spec.gate_paths, gate_documents)
    ):
        reference = _mapping(references[index])
        if (
            reference.get("gateId") != gate_id
            or reference.get("resultPath") != gate_path
            or reference.get("resultSha256") != gate_document.sha256
        ):
            raise AnchorBuildError("BUILD_HANDOFF_GATE_BINDING", spec.handoff_path)

    alias_binding: dict[str, Any] | None = None
    if spec.alias_path is not None:
        alias_document = _builder_document(evidence_root, spec.alias_path)
        if (
            alias_document.data != handoff_document.data
            or _product_candidate(alias_document.data) != product_candidate
        ):
            raise AnchorBuildError("BUILD_ALIAS", spec.alias_path)
        alias_binding = _builder_binding(alias_document)

    ledger_prefixes: dict[str, dict[str, Any]] = {}
    for kind, path in LEDGER_PATHS.items():
        ledger = _builder_ledger_view(evidence_root, kind)
        snapshot_values = _snapshot_ledger_values(snapshot, kind)
        if snapshot_values is None:
            raise AnchorBuildError(
                "BUILD_SNAPSHOT_LEDGER", spec.snapshot_path
            )
        snapshot_path = snapshot_values.path
        raw_sha = snapshot_values.raw_sha256
        count = snapshot_values.entry_count
        snapshot_last = snapshot_values.last_entry_sha256
        attempt_event_count = snapshot_values.attempt_event_count
        snapshot_attempt_event_last = (
            snapshot_values.last_attempt_event_sha256
        )
        if count > ledger.entry_count:
            raise AnchorBuildError("BUILD_LEDGER_PREFIX", path)
        last = ledger.entry_hashes[count - 1] if count else None
        if snapshot_last is not None and snapshot_last != last:
            raise AnchorBuildError("BUILD_SNAPSHOT_LEDGER", spec.snapshot_path)
        attempt_event_last: str | None = None
        if kind == "provider":
            if (
                attempt_event_count is None
                or attempt_event_count > ledger.attempt_event_count
            ):
                raise AnchorBuildError("BUILD_LEDGER_PREFIX", path)
            attempt_event_last = (
                ledger.attempt_event_hashes[attempt_event_count - 1]
                if attempt_event_count
                else None
            )
            if snapshot_attempt_event_last != attempt_event_last:
                raise AnchorBuildError(
                    "BUILD_SNAPSHOT_LEDGER", spec.snapshot_path
                )
        if ledger.raw_sha256 != raw_sha:
            raise AnchorBuildError("BUILD_LEDGER_RAW", path)
        binding = {
            "path": snapshot_path,
            "rawFileSha256": raw_sha,
            "entryCount": count,
            "lastEntrySha256": last,
            "prefixSha256": ledger_prefix_sha256(
                kind,
                path,
                ledger.entries,
                count,
                attempt_events=(
                    ledger.attempt_events if kind == "provider" else ()
                ),
                attempt_event_count=(
                    attempt_event_count
                    if kind == "provider" and attempt_event_count is not None
                    else 0
                ),
            ),
        }
        if kind == "provider":
            assert attempt_event_count is not None
            binding.update(
                {
                    "attemptEventCount": attempt_event_count,
                    "lastAttemptEventSha256": attempt_event_last,
                    "attemptEventPrefixSha256": attempt_event_prefix_sha256(
                        path,
                        ledger.attempt_events,
                        attempt_event_count,
                    ),
                }
            )
        ledger_prefixes[kind] = binding

    parent_anchors: list[dict[str, Any]] = []
    for parent_group_id in spec.parent_group_ids:
        parent_spec = GROUP_BY_ID[parent_group_id]
        parent_document = _builder_document(
            candidate_root, parent_spec.anchor_path
        )
        parent_anchors.append(
            {
                "groupId": parent_group_id,
                "path": parent_spec.anchor_path,
                "sha256": parent_document.sha256,
            }
        )

    return {
        "schemaVersion": SCHEMA_VERSION,
        "registryVersion": REGISTRY_VERSION,
        "goalId": GOAL_ID,
        "groupId": spec.group_id,
        "week": spec.week,
        "lane": spec.lane,
        "productCandidate": product_candidate,
        "gates": gate_bindings,
        "goalControlSnapshot": _builder_binding(snapshot_document),
        "canonicalHandoff": _builder_binding(handoff_document),
        "compatibilityHandoffAlias": alias_binding,
        "ledgerPrefixes": ledger_prefixes,
        "parentAnchors": parent_anchors,
    }


def _compose_preseal_receipt(
    seal_payload: Mapping[str, Any],
    source_bindings: Mapping[str, Mapping[str, str]],
    *,
    started_at: str,
    finished_at: str,
    exit_code: int,
) -> dict[str, Any]:
    return {
        "schemaVersion": seal_payload["schemaVersion"],
        "receiptVersion": PRESEAL_RECEIPT_VERSION,
        "registryVersion": seal_payload["registryVersion"],
        "goalId": seal_payload["goalId"],
        "groupId": seal_payload["groupId"],
        "week": seal_payload["week"],
        "lane": seal_payload["lane"],
        "productCandidate": seal_payload["productCandidate"],
        "validatorSource": source_bindings["validatorSource"],
        "anchorSource": source_bindings["anchorSource"],
        "requirementsManifest": source_bindings["requirementsManifest"],
        "startedAt": started_at,
        "finishedAt": finished_at,
        "exitCode": exit_code,
        "gates": seal_payload["gates"],
        "goalControlSnapshot": seal_payload["goalControlSnapshot"],
        "canonicalHandoff": seal_payload["canonicalHandoff"],
        "compatibilityHandoffAlias": seal_payload[
            "compatibilityHandoffAlias"
        ],
        "ledgerPrefixes": seal_payload["ledgerPrefixes"],
        "parentAnchors": seal_payload["parentAnchors"],
    }


def _builder_preseal_receipt(
    repo_root: Path,
    spec: GroupSpec,
    seal_payload: Mapping[str, Any],
    *,
    control_root: Path | None = None,
) -> _Document:
    evidence_root = (control_root or repo_root).resolve()
    try:
        document = _builder_document(evidence_root, spec.preseal_receipt_path)
    except AnchorBuildError as error:
        code = {
            "BUILD_INPUT_MISSING": "BUILD_PRESEAL_RECEIPT_MISSING",
            "BUILD_INPUT_JSON": "BUILD_PRESEAL_RECEIPT_JSON",
        }.get(error.code, "BUILD_PRESEAL_RECEIPT_INVALID")
        raise AnchorBuildError(code, spec.preseal_receipt_path) from None
    receipt = _mapping(document.data)
    if set(receipt) != PRESEAL_RECEIPT_FIELDS:
        raise AnchorBuildError(
            "BUILD_PRESEAL_RECEIPT_FIELDS", spec.preseal_receipt_path
        )
    started_at = receipt.get("startedAt")
    finished_at = receipt.get("finishedAt")
    if not _preseal_interval_is_valid(started_at, finished_at):
        raise AnchorBuildError(
            "BUILD_PRESEAL_RECEIPT_TIMESTAMPS", spec.preseal_receipt_path
        )
    exit_code = receipt.get("exitCode")
    if type(exit_code) is not int or exit_code != 0:
        raise AnchorBuildError(
            "BUILD_PRESEAL_RECEIPT_EXIT", spec.preseal_receipt_path
        )
    assert isinstance(started_at, str)
    assert isinstance(finished_at, str)
    source_bindings = capture_preseal_source_bindings(repo_root)
    expected = _compose_preseal_receipt(
        seal_payload,
        source_bindings,
        started_at=started_at,
        finished_at=finished_at,
        exit_code=exit_code,
    )
    if receipt != expected:
        raise AnchorBuildError(
            "BUILD_PRESEAL_RECEIPT_STALE", spec.preseal_receipt_path
        )
    return document


def build_preseal_receipt_document(
    repo_root: Path,
    group_id: str,
    product_candidate: str,
    *,
    control_root: Path | None = None,
    started_at: str,
    finished_at: str,
    exit_code: int,
) -> dict[str, Any]:
    """Build the canonical receipt after a trusted pre-seal validation run.

    The trusted caller supplies the validator run interval and successful exit
    code, then writes this document at ``GroupSpec.preseal_receipt_path``.
    This helper never writes files and refuses non-zero or malformed run data.
    """

    spec = GROUP_BY_ID.get(group_id)
    if spec is None:
        raise AnchorBuildError("BUILD_GROUP_UNKNOWN", ANCHOR_DIRECTORY)
    if not _preseal_interval_is_valid(started_at, finished_at):
        raise AnchorBuildError(
            "BUILD_PRESEAL_RECEIPT_TIMESTAMPS", spec.preseal_receipt_path
        )
    if type(exit_code) is not int or exit_code != 0:
        raise AnchorBuildError(
            "BUILD_PRESEAL_RECEIPT_EXIT", spec.preseal_receipt_path
        )
    root = repo_root.resolve()
    seal_payload = _build_seal_payload(
        root,
        group_id,
        product_candidate,
        control_root=control_root,
    )
    source_bindings = capture_preseal_source_bindings(root)
    return _compose_preseal_receipt(
        seal_payload,
        source_bindings,
        started_at=started_at,
        finished_at=finished_at,
        exit_code=exit_code,
    )


def write_preseal_receipt_document(
    repo_root: Path,
    group_id: str,
    product_candidate: str,
    *,
    control_root: Path | None = None,
    started_at: str,
    finished_at: str,
    exit_code: int,
    expected_source_bindings: Mapping[str, Mapping[str, str]],
) -> dict[str, Any]:
    """Atomically write a trusted receipt if start/end sources are identical."""

    root = repo_root.resolve()
    evidence_root = (control_root or repo_root).resolve()
    spec = GROUP_BY_ID.get(group_id)
    if spec is None:
        raise AnchorBuildError("BUILD_GROUP_UNKNOWN", ANCHOR_DIRECTORY)
    document = build_preseal_receipt_document(
        root,
        group_id,
        product_candidate,
        control_root=evidence_root,
        started_at=started_at,
        finished_at=finished_at,
        exit_code=exit_code,
    )
    if (
        set(expected_source_bindings) != set(PRESEAL_SOURCE_PATHS)
        or any(
            _mapping(expected_source_bindings.get(field))
            != document.get(field)
            for field in PRESEAL_SOURCE_PATHS
        )
    ):
        raise AnchorBuildError(
            "BUILD_PRESEAL_SOURCE_DRIFT", spec.preseal_receipt_path
        )
    target = _safe_path(evidence_root, spec.preseal_receipt_path)
    if target is None:
        raise AnchorBuildError(
            "BUILD_PRESEAL_RECEIPT_PATH", spec.preseal_receipt_path
        )
    payload = (
        json.dumps(
            document,
            ensure_ascii=False,
            sort_keys=True,
            indent=2,
        )
        + "\n"
    ).encode("utf-8")
    temporary_path: Path | None = None
    try:
        if not target.parent.is_dir():
            raise OSError
        with tempfile.NamedTemporaryFile(
            mode="wb",
            dir=target.parent,
            prefix=".preseal-receipt-",
            suffix=".tmp",
            delete=False,
        ) as handle:
            temporary_path = Path(handle.name)
            handle.write(payload)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary_path, target)
        temporary_path = None
    except OSError:
        raise AnchorBuildError(
            "BUILD_PRESEAL_RECEIPT_WRITE", spec.preseal_receipt_path
        ) from None
    finally:
        if temporary_path is not None:
            try:
                temporary_path.unlink(missing_ok=True)
            except OSError:
                pass
    return document


def build_anchor_document(
    repo_root: Path,
    group_id: str,
    product_candidate: str,
    *,
    control_root: Path | None = None,
) -> dict[str, Any]:
    """Build, but do not write, one canonical anchor document.

    A successful trusted pre-seal receipt at its fixed Gate-external path is
    mandatory.  Every bound input is recomputed before the receipt is accepted,
    so skipped validation or any post-pre-seal byte drift fails before anchor
    creation.  The caller still reviews, writes, stages, and commits the result.
    """

    root = repo_root.resolve()
    spec = GROUP_BY_ID.get(group_id)
    if spec is None:
        raise AnchorBuildError("BUILD_GROUP_UNKNOWN", ANCHOR_DIRECTORY)
    evidence_root = (control_root or repo_root).resolve()
    seal_payload = _build_seal_payload(
        root,
        group_id,
        product_candidate,
        control_root=evidence_root,
    )
    receipt_document = _builder_preseal_receipt(
        root,
        spec,
        seal_payload,
        control_root=evidence_root,
    )
    return {
        **seal_payload,
        "presealReceipt": _builder_binding(receipt_document),
    }


def registry_path_for_group(group_id: str) -> str:
    """Return the one tracked registry path for ``group_id``."""

    if group_id not in GROUP_BY_ID:
        raise AnchorBuildError("REGISTRY_GROUP_UNKNOWN", ANCHOR_REGISTRY_DIRECTORY)
    return f"{ANCHOR_REGISTRY_DIRECTORY}/{group_id}.json"


def bundle_path_for_group(group_id: str) -> str:
    """Return the one tracked bundle-manifest path for ``group_id``."""

    if group_id not in GROUP_BY_ID:
        raise AnchorBuildError("REGISTRY_GROUP_UNKNOWN", ANCHOR_REGISTRY_DIRECTORY)
    return f"{ANCHOR_REGISTRY_DIRECTORY}/{group_id}.bundle.json"


def sealed_ref_for_group(group_id: str) -> str:
    if group_id not in GROUP_BY_ID:
        raise AnchorBuildError("REGISTRY_GROUP_UNKNOWN", ANCHOR_REGISTRY_DIRECTORY)
    return f"{SEALED_REF_ROOT}/{group_id}"


def _canonical_artifact_path(value: Any) -> str | None:
    """Validate a portable path, including Windows ADS/device hazards."""

    if (
        not isinstance(value, str)
        or not value
        or "\\" in value
        or ":" in value
        or any(ord(character) < 32 for character in value)
    ):
        return None
    try:
        pure = PurePosixPath(value)
    except (TypeError, ValueError):
        return None
    if (
        pure.is_absolute()
        or not pure.parts
        or str(pure) != value
        or any(part in {"", ".", ".."} for part in pure.parts)
        or any(part.endswith((" ", ".")) for part in pure.parts)
        or any(_WINDOWS_DEVICE_NAME.fullmatch(part) for part in pure.parts)
    ):
        return None
    return value


def _strict_repo_path(
    repo_root: Path,
    relative_path: str,
    *,
    allow_missing_leaf: bool = False,
    create_parents: bool = False,
) -> Path:
    canonical = _canonical_artifact_path(relative_path)
    if canonical is None:
        raise AnchorBuildError("REGISTRY_PATH_UNSAFE", str(relative_path))
    root = repo_root.resolve(strict=True)
    current = root
    parts = PurePosixPath(canonical).parts
    for index, part in enumerate(parts):
        current = current / part
        leaf = index == len(parts) - 1
        present = current.exists() or current.is_symlink()
        if present:
            if _git_path_is_reparse(current):
                raise AnchorBuildError("REGISTRY_PATH_REPARSE", canonical)
            if leaf:
                if not current.is_file():
                    raise AnchorBuildError("REGISTRY_PATH_TYPE", canonical)
            elif not current.is_dir():
                raise AnchorBuildError("REGISTRY_PATH_TYPE", canonical)
            continue
        if leaf:
            if not allow_missing_leaf:
                raise AnchorBuildError("REGISTRY_PATH_MISSING", canonical)
            continue
        if not create_parents:
            raise AnchorBuildError("REGISTRY_PATH_MISSING", canonical)
        try:
            current.mkdir()
        except FileExistsError:
            pass
        except OSError:
            raise AnchorBuildError("REGISTRY_PATH_CREATE", canonical) from None
        if not current.is_dir() or _git_path_is_reparse(current):
            raise AnchorBuildError("REGISTRY_PATH_REPARSE", canonical)
    try:
        current.resolve(strict=False).relative_to(root)
    except (OSError, ValueError):
        raise AnchorBuildError("REGISTRY_PATH_UNSAFE", canonical) from None
    return current


def _trusted_regular_bytes(
    repo_root: Path,
    relative_path: str,
    *,
    expected_sha256: str | None = None,
    limit: int = MAX_EVIDENCE_BYTES,
) -> bytes:
    """Read one no-link, single-link regular file through a stable handle."""

    path = _strict_repo_path(repo_root, relative_path)
    flags = os.O_RDONLY
    if hasattr(os, "O_BINARY"):
        flags |= os.O_BINARY
    if hasattr(os, "O_NOFOLLOW"):
        flags |= os.O_NOFOLLOW
    descriptor: int | None = None
    try:
        descriptor = os.open(path, flags)
        before = os.fstat(descriptor)
        lexical_before = os.lstat(path)
        if (
            not stat.S_ISREG(before.st_mode)
            or not os.path.samestat(before, lexical_before)
            or _git_path_is_reparse(path)
            or getattr(before, "st_nlink", 1) != 1
            or before.st_size > limit
        ):
            raise AnchorBuildError("REGISTRY_SOURCE_UNSAFE", relative_path)
        chunks: list[bytes] = []
        remaining = limit + 1
        while remaining:
            block = os.read(descriptor, min(1024 * 1024, remaining))
            if not block:
                break
            chunks.append(block)
            remaining -= len(block)
        raw = b"".join(chunks)
        after = os.fstat(descriptor)
        lexical_after = os.lstat(path)
        if (
            len(raw) > limit
            or not os.path.samestat(before, after)
            or not os.path.samestat(after, lexical_after)
            or before.st_size != after.st_size
            or before.st_mtime_ns != after.st_mtime_ns
            or getattr(after, "st_nlink", 1) != 1
            or _git_path_is_reparse(path)
        ):
            raise AnchorBuildError("REGISTRY_SOURCE_DRIFT", relative_path)
    except AnchorBuildError:
        raise
    except OSError:
        raise AnchorBuildError("REGISTRY_SOURCE_READ", relative_path) from None
    finally:
        if descriptor is not None:
            try:
                os.close(descriptor)
            except OSError:
                raise AnchorBuildError("REGISTRY_SOURCE_READ", relative_path) from None
    digest = hashlib.sha256(raw).hexdigest()
    if expected_sha256 is not None and digest != expected_sha256:
        raise AnchorBuildError("REGISTRY_SOURCE_HASH", relative_path)
    return raw


def _pretty_json_bytes(value: Any) -> bytes:
    return (
        json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2) + "\n"
    ).encode("utf-8")


def _parse_json_object(raw: bytes, code: str, location: str) -> Mapping[str, Any]:
    try:
        value = json.loads(raw.decode("utf-8"), object_pairs_hook=_pairs_no_duplicates)
    except (UnicodeDecodeError, json.JSONDecodeError, _DuplicateKey):
        raise AnchorBuildError(code, location) from None
    if not isinstance(value, Mapping):
        raise AnchorBuildError(code, location)
    return value


def _git_text(repo_root: Path, arguments: Sequence[str], code: str) -> str:
    return_code, raw = _git(repo_root, arguments)
    if return_code != 0:
        raise AnchorBuildError(code, arguments[-1] if arguments else "git")
    try:
        return raw.decode("utf-8").strip()
    except UnicodeDecodeError:
        raise AnchorBuildError(code, arguments[-1] if arguments else "git") from None


def _git_ref_target(repo_root: Path, ref_name: str, code: str) -> str:
    value = _git_text(repo_root, ["rev-parse", "--verify", f"{ref_name}^{{commit}}"], code)
    if GIT_COMMIT.fullmatch(value) is None:
        raise AnchorBuildError(code, ref_name)
    return value


def _git_commit_parents(repo_root: Path, commit: str) -> tuple[str, ...]:
    line = _git_text(
        repo_root,
        ["rev-list", "--parents", "-n", "1", commit],
        "REGISTRY_COMMIT_MISSING",
    )
    values = line.split()
    if not values or values[0] != commit or any(
        GIT_COMMIT.fullmatch(value) is None for value in values
    ):
        raise AnchorBuildError("REGISTRY_COMMIT_SHAPE", commit)
    return tuple(values[1:])


def _git_commit_tree(repo_root: Path, commit: str) -> str:
    value = _git_text(
        repo_root,
        ["rev-parse", "--verify", f"{commit}^{{tree}}"],
        "REGISTRY_TREE_MISSING",
    )
    if GIT_COMMIT.fullmatch(value) is None:
        raise AnchorBuildError("REGISTRY_TREE_MISSING", commit)
    return value


def _git_tree_map(repo_root: Path, commit: str) -> dict[str, tuple[str, str]]:
    code, raw = _git(repo_root, ["ls-tree", "-r", "-z", commit])
    if code != 0:
        raise AnchorBuildError("REGISTRY_TREE_MISSING", commit)
    result: dict[str, tuple[str, str]] = {}
    for record in raw.split(b"\x00"):
        if not record:
            continue
        try:
            metadata, encoded_path = record.split(b"\t", 1)
            mode, object_type, object_id = metadata.decode("ascii").split()
            path = encoded_path.decode("utf-8")
        except (ValueError, UnicodeDecodeError):
            raise AnchorBuildError("REGISTRY_TREE_FORMAT", commit) from None
        if (
            object_type != "blob"
            or not re.fullmatch(r"[0-9a-f]{40}", object_id)
            or path in result
        ):
            raise AnchorBuildError("REGISTRY_TREE_FORMAT", commit)
        result[path] = (mode, object_id)
    return result


def _git_blob_binding(
    repo_root: Path,
    commit: str,
    relative_path: str,
    *,
    required_mode: str = "100644",
) -> tuple[str, bytes]:
    canonical = _canonical_artifact_path(relative_path)
    if canonical is None:
        raise AnchorBuildError("REGISTRY_PATH_UNSAFE", str(relative_path))
    tree = _git_tree_map(repo_root, commit)
    binding = tree.get(canonical)
    if binding is None or binding[0] != required_mode:
        raise AnchorBuildError("REGISTRY_GIT_BLOB", canonical)
    code, raw = _git(repo_root, ["cat-file", "blob", binding[1]])
    if code != 0:
        raise AnchorBuildError("REGISTRY_GIT_BLOB", canonical)
    return binding[1], raw


def _tree_changes(
    before: Mapping[str, tuple[str, str]],
    after: Mapping[str, tuple[str, str]],
) -> dict[str, tuple[tuple[str, str] | None, tuple[str, str] | None]]:
    return {
        path: (before.get(path), after.get(path))
        for path in sorted(set(before) | set(after))
        if before.get(path) != after.get(path)
    }


def _is_ancestor_or_equal(repo_root: Path, ancestor: str, descendant: str) -> bool:
    return ancestor == descendant or _is_ancestor(repo_root, ancestor, descendant)


def _require_control_root(repo_root: Path) -> Path:
    root = repo_root.resolve(strict=True)
    if not (root / ".git").is_dir() or not _git_repository_is_trusted(root):
        raise AnchorBuildError("REGISTRY_CONTROL_ROOT", ANCHOR_REGISTRY_DIRECTORY)
    branch = _git_text(
        root,
        ["symbolic-ref", "-q", "HEAD"],
        "REGISTRY_CONTROL_BRANCH",
    )
    if branch != CONTROL_BRANCH_REF:
        raise AnchorBuildError("REGISTRY_CONTROL_BRANCH", ANCHOR_REGISTRY_DIRECTORY)
    head = _git_ref_target(root, "HEAD", "REGISTRY_CONTROL_BRANCH")
    control = _git_ref_target(root, CONTROL_BRANCH_REF, "REGISTRY_CONTROL_BRANCH")
    if head != control:
        raise AnchorBuildError("REGISTRY_CONTROL_BRANCH", ANCHOR_REGISTRY_DIRECTORY)
    return root


def _checkpoint_path_allowed(kind: str, relative_path: str) -> bool:
    roots = _CHECKPOINT_KIND_ROOTS.get(kind)
    canonical = _canonical_artifact_path(relative_path)
    if roots is None or canonical is None or not canonical.endswith(".json"):
        return False
    pure = PurePosixPath(canonical)
    return any(
        len(pure.parts) > len(PurePosixPath(root).parts)
        and pure.parts[: len(PurePosixPath(root).parts)]
        == PurePosixPath(root).parts
        for root in roots
    )


def _checkpoint_delta_bindings(
    repo_root: Path,
    product_candidate: str,
    seal_commit: str,
    checkpoint_delta: Sequence[tuple[str, str]],
) -> list[dict[str, str]]:
    expected: dict[str, str] = {}
    casefolded: set[str] = set()
    for item in checkpoint_delta:
        if (
            not isinstance(item, tuple)
            or len(item) != 2
            or not isinstance(item[0], str)
            or not isinstance(item[1], str)
        ):
            raise AnchorBuildError("REGISTRY_CHECKPOINT_INPUT", str(item))
        kind, path = item
        if (
            path in expected
            or path.casefold() in casefolded
            or not _checkpoint_path_allowed(kind, path)
        ):
            raise AnchorBuildError("REGISTRY_CHECKPOINT_PATH", path)
        expected[path] = kind
        casefolded.add(path.casefold())

    if not expected:
        if seal_commit != product_candidate:
            raise AnchorBuildError("REGISTRY_SEAL_DELTA", seal_commit)
        return []
    if seal_commit == product_candidate or not _is_ancestor(
        repo_root, product_candidate, seal_commit
    ):
        raise AnchorBuildError("REGISTRY_SEAL_ANCESTRY", seal_commit)

    reverse_chain: list[str] = []
    current = seal_commit
    for _index in range(256):
        if current == product_candidate:
            break
        parents = _git_commit_parents(repo_root, current)
        if len(parents) != 1:
            raise AnchorBuildError("REGISTRY_SEAL_NONLINEAR", current)
        reverse_chain.append(current)
        current = parents[0]
    else:
        raise AnchorBuildError("REGISTRY_SEAL_CHAIN_LIMIT", seal_commit)
    if current != product_candidate:
        raise AnchorBuildError("REGISTRY_SEAL_ANCESTRY", seal_commit)

    additions: dict[str, str] = {}
    parent = product_candidate
    for commit in reversed(reverse_chain):
        before = _git_tree_map(repo_root, parent)
        after = _git_tree_map(repo_root, commit)
        changes = _tree_changes(before, after)
        if not changes:
            raise AnchorBuildError("REGISTRY_SEAL_EMPTY_COMMIT", commit)
        for path, (prior, current_binding) in changes.items():
            if (
                path not in expected
                or path in additions
                or prior is not None
                or current_binding is None
                or current_binding[0] != "100644"
            ):
                raise AnchorBuildError("REGISTRY_SEAL_DELTA", path)
            additions[path] = commit
        parent = commit
    if set(additions) != set(expected):
        raise AnchorBuildError("REGISTRY_SEAL_DELTA", seal_commit)

    product_tree = _git_tree_map(repo_root, product_candidate)
    seal_tree = _git_tree_map(repo_root, seal_commit)
    if {
        path: binding
        for path, binding in product_tree.items()
        if path not in expected
    } != {
        path: binding for path, binding in seal_tree.items() if path not in expected
    }:
        raise AnchorBuildError("REGISTRY_SEAL_TREE", seal_commit)

    result: list[dict[str, str]] = []
    for commit in reversed(reverse_chain):
        for path in sorted(path for path, added_at in additions.items() if added_at == commit):
            history_code, history_raw = _git(
                repo_root,
                [
                    "log",
                    "--all",
                    "--full-history",
                    "--diff-filter=A",
                    "--format=%H",
                    "--",
                    path,
                ],
            )
            historical_adds = {
                value
                for value in history_raw.decode("ascii", errors="ignore").splitlines()
                if value
            }
            if history_code != 0 or historical_adds != {commit}:
                raise AnchorBuildError("REGISTRY_CHECKPOINT_FIRST_ADD", path)
            blob_id, raw = _git_blob_binding(repo_root, commit, path)
            result.append(
                {
                    "kind": expected[path],
                    "path": path,
                    "firstAddCommit": commit,
                    "gitBlobSha": blob_id,
                    "sha256": hashlib.sha256(raw).hexdigest(),
                }
            )
    return result


def _merge_preserves_contributions(
    repo_root: Path,
    merge_commit: str,
    first_parent: str,
    second_parent: str,
) -> bool:
    bases_raw = _git_text(
        repo_root,
        ["merge-base", "--all", first_parent, second_parent],
        "REGISTRY_MERGE_BASE",
    )
    bases = tuple(value for value in bases_raw.splitlines() if value)
    if len(bases) != 1 or GIT_COMMIT.fullmatch(bases[0]) is None:
        return False
    base_tree = _git_tree_map(repo_root, bases[0])
    first_tree = _git_tree_map(repo_root, first_parent)
    second_tree = _git_tree_map(repo_root, second_parent)
    merge_tree = _git_tree_map(repo_root, merge_commit)
    for path in set(base_tree) | set(first_tree) | set(second_tree) | set(merge_tree):
        base_value = base_tree.get(path)
        first_value = first_tree.get(path)
        second_value = second_tree.get(path)
        merged_value = merge_tree.get(path)
        if first_value == second_value:
            if merged_value != first_value:
                return False
        elif first_value == base_value:
            if merged_value != second_value:
                return False
        elif second_value == base_value:
            if merged_value != first_value:
                return False
        else:
            # Conflict resolution needs a separately frozen manifest.  This
            # core intentionally rejects unresolved policy rather than guess.
            return False
    return True


def _committed_control_document(
    repo_root: Path,
    path: str,
    *,
    json_code: str,
) -> tuple[str, bytes, Mapping[str, Any]]:
    raw = _trusted_regular_bytes(repo_root, path, limit=MAX_JSON_BYTES)
    document = _parse_json_object(raw, json_code, path)
    code, additions_raw = _git(
        repo_root,
        [
            "log",
            "--all",
            "--full-history",
            "--diff-filter=A",
            "--format=%H",
            "--",
            path,
        ],
    )
    additions = tuple(
        value
        for value in additions_raw.decode("ascii", errors="ignore").splitlines()
        if value
    )
    if code != 0 or len(set(additions)) != 1:
        raise AnchorBuildError("REGISTRY_HISTORY", path)
    commit = additions[0]
    blob_id, committed_raw = _git_blob_binding(repo_root, commit, path)
    del blob_id
    head_blob_id, head_raw = _git_blob_binding(repo_root, "HEAD", path)
    del head_blob_id
    index_code, index_blob_raw = _git(repo_root, ["rev-parse", f":{path}"])
    index_blob = index_blob_raw.decode("ascii", errors="ignore").strip()
    index_read_code, index_raw = _git(repo_root, ["cat-file", "blob", index_blob])
    history_code, history_raw = _git(
        repo_root,
        ["log", "--format=%H", "--", path],
    )
    history = tuple(
        value
        for value in history_raw.decode("ascii", errors="ignore").splitlines()
        if value
    )
    if (
        index_code != 0
        or index_read_code != 0
        or history_code != 0
        or history != (commit,)
        or raw != committed_raw
        or raw != head_raw
        or raw != index_raw
        or _git(
            repo_root,
            ["status", "--porcelain=v1", "--untracked-files=all", "--", path],
        )[1].strip()
    ):
        raise AnchorBuildError("REGISTRY_IMMUTABLE", path)
    return commit, raw, document


def _registry_commit_for_group(
    repo_root: Path,
    group_id: str,
) -> tuple[str, bytes, Mapping[str, Any]]:
    return _committed_control_document(
        repo_root,
        registry_path_for_group(group_id),
        json_code="REGISTRY_JSON",
    )


def _dependency_binding(
    repo_root: Path,
    group_id: str,
) -> dict[str, str]:
    commit, raw, _document = _registry_commit_for_group(repo_root, group_id)
    return {
        "groupId": group_id,
        "path": registry_path_for_group(group_id),
        "sha256": hashlib.sha256(raw).hexdigest(),
        "commit": commit,
    }


def _expected_registry_dependencies(
    repo_root: Path,
    spec: GroupSpec,
) -> tuple[list[dict[str, str]], list[dict[str, str]], dict[str, str] | None]:
    protocol_parents = [
        _dependency_binding(repo_root, group_id)
        for group_id in spec.parent_group_ids
    ]
    entry_barrier = [
        _dependency_binding(repo_root, group_id)
        for group_id in ENTRY_BARRIER_GROUPS[spec.group_id]
    ]
    sequence = GROUP_SEQUENCE[spec.group_id]
    previous = (
        None
        if sequence == 1
        else _dependency_binding(
            repo_root,
            CANONICAL_GROUPS[sequence - 2].group_id,
        )
    )
    return protocol_parents, entry_barrier, previous


def _validate_offbranch_anchor_semantics(
    repo_root: Path,
    spec: GroupSpec,
    anchor_document: Mapping[str, Any],
) -> None:
    """Revalidate an anchor blob and its canonical ignored inputs off-branch."""

    for path, kind, expected_sha256 in _anchor_bundle_sources(spec, anchor_document):
        expected = (
            None
            if kind.endswith("-ledger-prefix-snapshot")
            else expected_sha256
        )
        try:
            _trusted_regular_bytes(
                repo_root,
                path,
                expected_sha256=expected,
                limit=MAX_EVIDENCE_BYTES,
            )
        except AnchorBuildError:
            raise AnchorBuildError("REGISTRY_ARTIFACT_DRIFT", path) from None

    issues: list[AnchorIssue] = []
    candidate = anchor_document.get("productCandidate")
    normalized_candidate = candidate if isinstance(candidate, str) else None
    _snapshot_document, snapshot, _gate_bindings = _validate_group_artifacts(
        repo_root,
        spec,
        anchor_document,
        normalized_candidate,
        issues,
        candidate_root=repo_root,
    )
    _validate_ledger_prefixes(
        repo_root,
        spec,
        anchor_document,
        snapshot,
        {},
        issues,
    )
    _validate_preseal_receipt(
        repo_root,
        spec,
        anchor_document,
        issues,
        candidate_root=repo_root,
    )

    parent_values = _list(anchor_document.get("parentAnchors"))
    if len(parent_values) != len(spec.parent_group_ids):
        issues.append(
            AnchorIssue(
                "REGISTRY_ANCHOR_PARENT_BINDING",
                f"{spec.anchor_path}#/parentAnchors",
            )
        )
    for index, parent_group_id in enumerate(spec.parent_group_ids):
        parent_registry = _registry_commit_for_group(repo_root, parent_group_id)[2]
        parent_anchor = _mapping(parent_registry.get("anchorBinding"))
        expected_parent = {
            "groupId": parent_group_id,
            "path": GROUP_BY_ID[parent_group_id].anchor_path,
            "sha256": parent_anchor.get("sha256"),
        }
        actual_parent = _mapping(
            parent_values[index] if index < len(parent_values) else {}
        )
        if set(actual_parent) != PARENT_BINDING_FIELDS or actual_parent != expected_parent:
            issues.append(
                AnchorIssue(
                    "REGISTRY_ANCHOR_PARENT_BINDING",
                    f"{spec.anchor_path}#/parentAnchors/{index}",
                )
            )
    if issues:
        issue = sorted(set(issues))[0]
        raise AnchorBuildError(issue.code, issue.location)


def _validate_entry_base(
    repo_root: Path,
    spec: GroupSpec,
    entry_base_commit: str,
    product_candidate: str,
    dependency_commits: Mapping[str, str],
) -> None:
    if not _is_ancestor_or_equal(repo_root, entry_base_commit, product_candidate):
        raise AnchorBuildError("REGISTRY_ENTRY_ANCESTRY", entry_base_commit)
    if spec.group_id == "w84-baseline":
        return
    if spec.week == 85:
        expected = dependency_commits["w84-baseline"]
        if entry_base_commit != expected:
            raise AnchorBuildError("REGISTRY_ENTRY_BARRIER", entry_base_commit)
        return
    if spec.group_id == "w90-integration":
        control_tip = dependency_commits["w89-cli"]
        renderer_tip = _git_ref_target(
            repo_root,
            sealed_ref_for_group("w89-renderer"),
            "REGISTRY_ENTRY_REF",
        )
        cli_tip = _git_ref_target(
            repo_root,
            sealed_ref_for_group("w89-cli"),
            "REGISTRY_ENTRY_REF",
        )
        final_parents = _git_commit_parents(repo_root, entry_base_commit)
        if len(final_parents) != 2 or final_parents[1] != cli_tip:
            raise AnchorBuildError("REGISTRY_W90_MERGE", entry_base_commit)
        renderer_merge = final_parents[0]
        renderer_parents = _git_commit_parents(repo_root, renderer_merge)
        if renderer_parents != (control_tip, renderer_tip):
            raise AnchorBuildError("REGISTRY_W90_MERGE", renderer_merge)
        if not _merge_preserves_contributions(
            repo_root, renderer_merge, control_tip, renderer_tip
        ) or not _merge_preserves_contributions(
            repo_root, entry_base_commit, renderer_merge, cli_tip
        ):
            raise AnchorBuildError("REGISTRY_W90_CONTRIBUTION", entry_base_commit)
        return

    parent_group = spec.parent_group_ids[0]
    parent_anchor = _git_ref_target(
        repo_root,
        sealed_ref_for_group(parent_group),
        "REGISTRY_ENTRY_REF",
    )
    barrier_tip_group = ENTRY_BARRIER_GROUPS[spec.group_id][-1]
    barrier_tip = dependency_commits[barrier_tip_group]
    parents = _git_commit_parents(repo_root, entry_base_commit)
    if parents != (parent_anchor, barrier_tip):
        raise AnchorBuildError("REGISTRY_ENTRY_MERGE", entry_base_commit)
    if not _merge_preserves_contributions(
        repo_root, entry_base_commit, parent_anchor, barrier_tip
    ):
        raise AnchorBuildError("REGISTRY_ENTRY_CONTRIBUTION", entry_base_commit)


def _lane_and_anchor_bindings(
    repo_root: Path,
    spec: GroupSpec,
    *,
    entry_base_commit: str,
    product_candidate: str,
    seal_commit: str,
    checkpoint_delta: Sequence[tuple[str, str]],
    dependency_commits: Mapping[str, str],
) -> tuple[
    dict[str, str],
    dict[str, str],
    dict[str, str],
    list[dict[str, str]],
    Mapping[str, Any],
]:
    for value in (entry_base_commit, product_candidate, seal_commit):
        if GIT_COMMIT.fullmatch(value) is None or _git(
            repo_root, ["cat-file", "-e", f"{value}^{{commit}}"]
        )[0] != 0:
            raise AnchorBuildError("REGISTRY_COMMIT_MISSING", str(value))
    branch_ref = GROUP_BRANCH_REFS[spec.group_id]
    branch_tip = _git_ref_target(repo_root, branch_ref, "REGISTRY_LANE_REF")
    _validate_entry_base(
        repo_root,
        spec,
        entry_base_commit,
        product_candidate,
        dependency_commits,
    )
    if not _is_ancestor_or_equal(repo_root, product_candidate, seal_commit):
        raise AnchorBuildError("REGISTRY_SEAL_ANCESTRY", seal_commit)
    checkpoint_bindings = _checkpoint_delta_bindings(
        repo_root,
        product_candidate,
        seal_commit,
        checkpoint_delta,
    )
    anchor_ref = sealed_ref_for_group(spec.group_id)
    anchor_commit = _git_ref_target(repo_root, anchor_ref, "REGISTRY_SEALED_REF")
    if not _is_ancestor_or_equal(repo_root, anchor_commit, branch_tip):
        raise AnchorBuildError("REGISTRY_LANE_REF", branch_ref)
    if _git_commit_parents(repo_root, anchor_commit) != (seal_commit,):
        raise AnchorBuildError("REGISTRY_ANCHOR_PARENT", spec.anchor_path)
    before = _git_tree_map(repo_root, seal_commit)
    after = _git_tree_map(repo_root, anchor_commit)
    changes = _tree_changes(before, after)
    if (
        set(changes) != {spec.anchor_path}
        or changes[spec.anchor_path][0] is not None
        or changes[spec.anchor_path][1] is None
        or changes[spec.anchor_path][1][0] != "100644"
    ):
        raise AnchorBuildError("REGISTRY_ANCHOR_DELTA", spec.anchor_path)
    additions_code, additions_raw = _git(
        repo_root,
        [
            "log",
            "--all",
            "--full-history",
            "--diff-filter=A",
            "--format=%H",
            "--",
            spec.anchor_path,
        ],
    )
    additions = {
        value
        for value in additions_raw.decode("ascii", errors="ignore").splitlines()
        if value
    }
    if additions_code != 0 or additions != {anchor_commit}:
        raise AnchorBuildError("REGISTRY_ANCHOR_CHERRYPICK", spec.anchor_path)
    anchor_blob, anchor_raw = _git_blob_binding(
        repo_root,
        anchor_commit,
        spec.anchor_path,
    )
    anchor_document = _parse_json_object(
        anchor_raw,
        "REGISTRY_ANCHOR_JSON",
        spec.anchor_path,
    )
    if (
        set(anchor_document) != ANCHOR_FIELDS
        or anchor_document.get("schemaVersion") != SCHEMA_VERSION
        or anchor_document.get("registryVersion") != REGISTRY_VERSION
        or anchor_document.get("goalId") != GOAL_ID
        or anchor_document.get("groupId") != spec.group_id
        or anchor_document.get("week") != spec.week
        or anchor_document.get("lane") != spec.lane
        or anchor_document.get("productCandidate") != product_candidate
    ):
        raise AnchorBuildError("REGISTRY_ANCHOR_IDENTITY", spec.anchor_path)
    _validate_offbranch_anchor_semantics(repo_root, spec, anchor_document)

    lane_binding = {
        "branchRef": branch_ref,
        "entryBaseCommit": entry_base_commit,
        "productCandidate": product_candidate,
        "candidateTree": _git_commit_tree(repo_root, product_candidate),
        "sealCommit": seal_commit,
        "sealTree": _git_commit_tree(repo_root, seal_commit),
    }
    anchor_binding = {
        "path": spec.anchor_path,
        "commit": anchor_commit,
        "parentCommit": seal_commit,
        "tree": _git_commit_tree(repo_root, anchor_commit),
        "gitBlobSha": anchor_blob,
        "sha256": hashlib.sha256(anchor_raw).hexdigest(),
    }
    preservation_ref = {"name": anchor_ref, "targetCommit": anchor_commit}
    return (
        lane_binding,
        anchor_binding,
        preservation_ref,
        checkpoint_bindings,
        anchor_document,
    )


def _anchor_bundle_sources(
    spec: GroupSpec,
    anchor_document: Mapping[str, Any],
) -> list[tuple[str, str, str]]:
    """Return canonical path/kind/hash tuples bound by an off-branch anchor."""

    sources: list[tuple[str, str, str]] = []

    def add(path: Any, kind: str, sha256: Any) -> None:
        if (
            not isinstance(path, str)
            or _canonical_artifact_path(path) is None
            or not isinstance(sha256, str)
            or HEX_SHA256.fullmatch(sha256) is None
        ):
            raise AnchorBuildError("REGISTRY_BUNDLE_SOURCE", spec.anchor_path)
        sources.append((path, kind, sha256))

    gates = _list(anchor_document.get("gates"))
    if len(gates) != len(spec.gate_ids):
        raise AnchorBuildError("REGISTRY_BUNDLE_SOURCE", spec.anchor_path)
    for expected_gate_id, value in zip(spec.gate_ids, gates):
        gate = _mapping(value)
        if gate.get("gateId") != expected_gate_id:
            raise AnchorBuildError("REGISTRY_BUNDLE_SOURCE", spec.anchor_path)
        add(gate.get("path"), "gate-result", gate.get("sha256"))
        evidence_items = _list(gate.get("evidence"))
        if not evidence_items:
            raise AnchorBuildError("REGISTRY_BUNDLE_SOURCE", spec.anchor_path)
        for evidence_value in evidence_items:
            evidence = _mapping(evidence_value)
            add(evidence.get("path"), "gate-evidence", evidence.get("sha256"))

    fixed_bindings = (
        ("goalControlSnapshot", "goal-control-snapshot"),
        ("canonicalHandoff", "canonical-handoff"),
        ("presealReceipt", "preseal-receipt"),
    )
    for field, kind in fixed_bindings:
        binding = _mapping(anchor_document.get(field))
        add(binding.get("path"), kind, binding.get("sha256"))
    alias = anchor_document.get("compatibilityHandoffAlias")
    if alias is not None:
        alias_binding = _mapping(alias)
        add(
            alias_binding.get("path"),
            "compatibility-handoff-alias",
            alias_binding.get("sha256"),
        )

    prefixes = _mapping(anchor_document.get("ledgerPrefixes"))
    if set(prefixes) != set(LEDGER_PATHS):
        raise AnchorBuildError("REGISTRY_LEDGER_PREFIX", spec.anchor_path)
    for kind in LEDGER_PATHS:
        prefix = _mapping(prefixes.get(kind))
        add(
            prefix.get("path"),
            f"{kind}-ledger-prefix-snapshot",
            prefix.get("rawFileSha256"),
        )

    paths = [path for path, _kind, _sha256 in sources]
    if (
        len(paths) > MAX_BUNDLE_ENTRIES
        or len(paths) != len(set(paths))
        or len(paths) != len({path.casefold() for path in paths})
    ):
        raise AnchorBuildError("REGISTRY_BUNDLE_PATH_SET", spec.anchor_path)
    return sorted(sources, key=lambda item: item[0].encode("utf-8"))


def _store_object_path(sha256: str) -> str:
    if HEX_SHA256.fullmatch(sha256) is None:
        raise AnchorBuildError("REGISTRY_STORE_HASH", EVIDENCE_STORE_ROOT)
    return f"{EVIDENCE_STORE_ROOT}/{sha256[:2]}/{sha256}"


def _fsync_directory(path: Path) -> None:
    if os.name == "nt":
        return
    descriptor: int | None = None
    try:
        descriptor = os.open(path, os.O_RDONLY)
        os.fsync(descriptor)
    except OSError:
        raise AnchorBuildError("REGISTRY_STORE_DURABILITY", EVIDENCE_STORE_ROOT) from None
    finally:
        if descriptor is not None:
            os.close(descriptor)


def _publish_store_object(repo_root: Path, sha256: str, raw: bytes) -> str:
    relative_path = _store_object_path(sha256)
    if hashlib.sha256(raw).hexdigest() != sha256:
        raise AnchorBuildError("REGISTRY_STORE_HASH", relative_path)
    target = _strict_repo_path(
        repo_root,
        relative_path,
        allow_missing_leaf=True,
        create_parents=True,
    )
    if _git(repo_root, ["check-ignore", "-q", "--", relative_path])[0] != 0:
        raise AnchorBuildError("REGISTRY_STORE_NOT_IGNORED", relative_path)
    if target.exists() or target.is_symlink():
        existing = _trusted_regular_bytes(
            repo_root,
            relative_path,
            expected_sha256=sha256,
            limit=MAX_EVIDENCE_BYTES,
        )
        if existing != raw:
            raise AnchorBuildError("REGISTRY_STORE_COLLISION", relative_path)
        return relative_path

    temporary: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="wb",
            dir=target.parent,
            prefix=".evidence-store-",
            suffix=".tmp",
            delete=False,
        ) as handle:
            temporary = Path(handle.name)
            handle.write(raw)
            handle.flush()
            os.fsync(handle.fileno())
        if _git_path_is_reparse(temporary) or not temporary.is_file():
            raise AnchorBuildError("REGISTRY_STORE_TEMP", relative_path)
        try:
            os.link(temporary, target)
        except FileExistsError:
            existing = _trusted_regular_bytes(
                repo_root,
                relative_path,
                expected_sha256=sha256,
                limit=MAX_EVIDENCE_BYTES,
            )
            if existing != raw:
                raise AnchorBuildError("REGISTRY_STORE_COLLISION", relative_path)
        except OSError:
            raise AnchorBuildError("REGISTRY_STORE_PUBLISH", relative_path) from None
        temporary.unlink()
        temporary = None
        _fsync_directory(target.parent)
    finally:
        if temporary is not None:
            try:
                temporary.unlink(missing_ok=True)
            except OSError:
                pass
    published = _trusted_regular_bytes(
        repo_root,
        relative_path,
        expected_sha256=sha256,
        limit=MAX_EVIDENCE_BYTES,
    )
    if published != raw:
        raise AnchorBuildError("REGISTRY_STORE_DRIFT", relative_path)
    return relative_path


def _build_bundle_document(
    repo_root: Path,
    spec: GroupSpec,
    anchor_binding: Mapping[str, str],
    anchor_document: Mapping[str, Any],
) -> tuple[dict[str, Any], tuple[str, ...]]:
    entries: list[dict[str, Any]] = []
    store_objects: list[str] = []
    total_bytes = 0
    for canonical_path, content_kind, expected_sha in _anchor_bundle_sources(
        spec, anchor_document
    ):
        raw = _trusted_regular_bytes(
            repo_root,
            canonical_path,
            expected_sha256=expected_sha,
            limit=MAX_EVIDENCE_BYTES,
        )
        store_object = _publish_store_object(repo_root, expected_sha, raw)
        entries.append(
            {
                "canonicalPath": canonical_path,
                "contentKind": content_kind,
                "mode": "100644",
                "bytes": len(raw),
                "sha256": expected_sha,
                "storeObject": store_object,
            }
        )
        store_objects.append(store_object)
        total_bytes += len(raw)
        if total_bytes > MAX_EVIDENCE_BYTES:
            raise AnchorBuildError("REGISTRY_BUNDLE_TOO_LARGE", spec.group_id)
    content_root = hashlib.sha256(canonical_json_bytes(entries)).hexdigest()
    source_anchor = {
        "path": anchor_binding["path"],
        "commit": anchor_binding["commit"],
        "gitBlobSha": anchor_binding["gitBlobSha"],
        "sha256": anchor_binding["sha256"],
    }
    bundle = {
        "schemaVersion": SCHEMA_VERSION,
        "protocol": EVIDENCE_BUNDLE_PROTOCOL,
        "goalId": GOAL_ID,
        "groupId": spec.group_id,
        "sourceAnchor": source_anchor,
        "entryCount": len(entries),
        "totalBytes": total_bytes,
        "contentRootSha256": content_root,
        "entries": entries,
        "ledgerPrefixes": anchor_document["ledgerPrefixes"],
    }
    return bundle, tuple(store_objects)


def _write_exclusive_json(repo_root: Path, relative_path: str, value: Any) -> bytes:
    raw = _pretty_json_bytes(value)
    target = _strict_repo_path(
        repo_root,
        relative_path,
        allow_missing_leaf=True,
        create_parents=True,
    )
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
    if hasattr(os, "O_BINARY"):
        flags |= os.O_BINARY
    if hasattr(os, "O_NOFOLLOW"):
        flags |= os.O_NOFOLLOW
    descriptor: int | None = None
    try:
        descriptor = os.open(target, flags, 0o600)
        written = 0
        while written < len(raw):
            count = os.write(descriptor, raw[written:])
            if count <= 0:
                raise OSError
            written += count
        os.fsync(descriptor)
        opened = os.fstat(descriptor)
        lexical = os.lstat(target)
        if (
            not os.path.samestat(opened, lexical)
            or not stat.S_ISREG(opened.st_mode)
            or getattr(opened, "st_nlink", 1) != 1
            or _git_path_is_reparse(target)
        ):
            raise AnchorBuildError("REGISTRY_WRITE_RACE", relative_path)
    except FileExistsError:
        raise AnchorBuildError("REGISTRY_PATH_EXISTS", relative_path) from None
    except AnchorBuildError:
        raise
    except OSError:
        raise AnchorBuildError("REGISTRY_WRITE", relative_path) from None
    finally:
        if descriptor is not None:
            os.close(descriptor)
    _fsync_directory(target.parent)
    if _trusted_regular_bytes(
        repo_root,
        relative_path,
        expected_sha256=hashlib.sha256(raw).hexdigest(),
        limit=MAX_JSON_BYTES,
    ) != raw:
        raise AnchorBuildError("REGISTRY_WRITE_RACE", relative_path)
    return raw


def _present_registry_prefix(repo_root: Path) -> tuple[str, ...]:
    present: list[str] = []
    gap = False
    for spec in CANONICAL_GROUPS:
        registry_path = repo_root.joinpath(
            *PurePosixPath(registry_path_for_group(spec.group_id)).parts
        )
        bundle_path = repo_root.joinpath(
            *PurePosixPath(bundle_path_for_group(spec.group_id)).parts
        )
        registry_present = registry_path.exists() or registry_path.is_symlink()
        bundle_present = bundle_path.exists() or bundle_path.is_symlink()
        if registry_present != bundle_present:
            raise AnchorBuildError("REGISTRY_PAIR", spec.group_id)
        if registry_present:
            if gap:
                raise AnchorBuildError("REGISTRY_ORDER", spec.group_id)
            present.append(spec.group_id)
        else:
            gap = True
    return tuple(present)


def _validate_bundle_document_or_raise(
    repo_root: Path,
    spec: GroupSpec,
    bundle: Mapping[str, Any],
    anchor_binding: Mapping[str, Any],
    anchor_document: Mapping[str, Any],
) -> None:
    path = bundle_path_for_group(spec.group_id)
    if (
        set(bundle) != BUNDLE_FIELDS
        or bundle.get("schemaVersion") != SCHEMA_VERSION
        or bundle.get("protocol") != EVIDENCE_BUNDLE_PROTOCOL
        or bundle.get("goalId") != GOAL_ID
        or bundle.get("groupId") != spec.group_id
    ):
        raise AnchorBuildError("REGISTRY_BUNDLE_FIELDS", path)
    source = _mapping(bundle.get("sourceAnchor"))
    expected_source = {
        "path": anchor_binding.get("path"),
        "commit": anchor_binding.get("commit"),
        "gitBlobSha": anchor_binding.get("gitBlobSha"),
        "sha256": anchor_binding.get("sha256"),
    }
    if set(source) != BUNDLE_SOURCE_ANCHOR_FIELDS or source != expected_source:
        raise AnchorBuildError("REGISTRY_BUNDLE_ANCHOR", path)
    ledger_prefixes = _mapping(anchor_document.get("ledgerPrefixes"))
    if bundle.get("ledgerPrefixes") != ledger_prefixes:
        raise AnchorBuildError("REGISTRY_LEDGER_PREFIX", path)
    entries = bundle.get("entries")
    if not isinstance(entries, list) or len(entries) > MAX_BUNDLE_ENTRIES:
        raise AnchorBuildError("REGISTRY_BUNDLE_ENTRIES", path)
    canonical_paths: list[str] = []
    total_bytes = 0
    for index, entry_value in enumerate(entries):
        location = f"{path}#/entries/{index}"
        entry = _mapping(entry_value)
        canonical_path = entry.get("canonicalPath")
        sha256 = entry.get("sha256")
        byte_count = entry.get("bytes")
        if (
            set(entry) != BUNDLE_ENTRY_FIELDS
            or _canonical_artifact_path(canonical_path) is None
            or not isinstance(entry.get("contentKind"), str)
            or not entry["contentKind"]
            or entry.get("mode") != "100644"
            or not isinstance(sha256, str)
            or HEX_SHA256.fullmatch(sha256) is None
            or type(byte_count) is not int
            or byte_count < 0
            or entry.get("storeObject") != _store_object_path(sha256)
        ):
            raise AnchorBuildError("REGISTRY_BUNDLE_ENTRY", location)
        assert isinstance(canonical_path, str)
        store_object = entry["storeObject"]
        if _git(repo_root, ["check-ignore", "-q", "--", store_object])[0] != 0:
            raise AnchorBuildError("REGISTRY_STORE_NOT_IGNORED", location)
        try:
            raw = _trusted_regular_bytes(
                repo_root,
                store_object,
                expected_sha256=sha256,
                limit=MAX_EVIDENCE_BYTES,
            )
        except AnchorBuildError as error:
            if error.code in {
                "REGISTRY_SOURCE_HASH",
                "REGISTRY_SOURCE_DRIFT",
                "REGISTRY_SOURCE_UNSAFE",
                "REGISTRY_SOURCE_READ",
            }:
                raise AnchorBuildError("REGISTRY_STORE_HASH", location) from None
            raise
        if len(raw) != byte_count:
            raise AnchorBuildError("REGISTRY_STORE_SIZE", location)
        canonical_paths.append(canonical_path)
        total_bytes += byte_count
        if total_bytes > MAX_EVIDENCE_BYTES:
            raise AnchorBuildError("REGISTRY_BUNDLE_TOO_LARGE", path)
    if (
        canonical_paths
        != sorted(canonical_paths, key=lambda value: value.encode("utf-8"))
        or len(canonical_paths) != len(set(canonical_paths))
        or len(canonical_paths)
        != len({value.casefold() for value in canonical_paths})
        or bundle.get("entryCount") != len(entries)
        or bundle.get("totalBytes") != total_bytes
        or bundle.get("contentRootSha256")
        != hashlib.sha256(canonical_json_bytes(entries)).hexdigest()
    ):
        raise AnchorBuildError("REGISTRY_BUNDLE_SUMMARY", path)
    expected_sources = _anchor_bundle_sources(spec, anchor_document)
    actual_sources = [
        (
            str(_mapping(entry).get("canonicalPath")),
            str(_mapping(entry).get("contentKind")),
            str(_mapping(entry).get("sha256")),
        )
        for entry in entries
    ]
    if actual_sources != expected_sources:
        raise AnchorBuildError("REGISTRY_BUNDLE_SOURCE", path)


def _validate_registry_record_or_raise(
    repo_root: Path,
    spec: GroupSpec,
    prior_commits: Mapping[str, str],
) -> str:
    registry_path = registry_path_for_group(spec.group_id)
    bundle_path = bundle_path_for_group(spec.group_id)
    registry_commit, registry_raw, registry = _registry_commit_for_group(
        repo_root, spec.group_id
    )
    bundle_commit, bundle_raw, bundle = _committed_control_document(
        repo_root,
        bundle_path,
        json_code="REGISTRY_BUNDLE_JSON",
    )
    if registry_commit != bundle_commit:
        raise AnchorBuildError("REGISTRY_COMMIT_PAIR", registry_path)
    if (
        set(registry) != REGISTRY_FIELDS
        or registry.get("schemaVersion") != SCHEMA_VERSION
        or registry.get("protocol") != ANCHOR_REGISTRY_PROTOCOL
        or registry.get("goalId") != GOAL_ID
        or registry.get("sequence") != GROUP_SEQUENCE[spec.group_id]
        or registry.get("groupId") != spec.group_id
        or registry.get("week") != spec.week
        or registry.get("lane") != spec.lane
        or registry.get("objectFormat") != "sha1"
    ):
        raise AnchorBuildError("REGISTRY_FIELDS", registry_path)

    protocol_parents, entry_barrier, previous = _expected_registry_dependencies(
        repo_root, spec
    )
    if registry.get("protocolParents") != protocol_parents:
        raise AnchorBuildError("REGISTRY_PROTOCOL_PARENTS", registry_path)
    if registry.get("entryBarrier") != entry_barrier:
        raise AnchorBuildError("REGISTRY_ENTRY_BARRIER", registry_path)
    if registry.get("previousRegistry") != previous:
        raise AnchorBuildError("REGISTRY_PREVIOUS", registry_path)
    dependency_commits = {
        binding["groupId"]: binding["commit"]
        for binding in [*protocol_parents, *entry_barrier]
    }

    lane = _mapping(registry.get("laneBinding"))
    anchor_binding = _mapping(registry.get("anchorBinding"))
    preservation_ref = _mapping(registry.get("preservationRef"))
    checkpoint_values = registry.get("checkpointDelta")
    if (
        set(lane) != LANE_BINDING_FIELDS
        or set(anchor_binding) != ANCHOR_BINDING_FIELDS
        or set(preservation_ref) != PRESERVATION_REF_FIELDS
        or not isinstance(checkpoint_values, list)
        or any(
            not isinstance(item, Mapping)
            or set(item) != CHECKPOINT_DELTA_FIELDS
            for item in checkpoint_values
        )
    ):
        raise AnchorBuildError("REGISTRY_BINDINGS", registry_path)
    registered_anchor_commit = anchor_binding.get("commit")
    if (
        not isinstance(registered_anchor_commit, str)
        or _git_ref_target(
            repo_root,
            sealed_ref_for_group(spec.group_id),
            "REGISTRY_SEALED_REF",
        )
        != registered_anchor_commit
    ):
        raise AnchorBuildError("REGISTRY_SEALED_REF", registry_path)
    checkpoint_input = tuple(
        (str(item["kind"]), str(item["path"])) for item in checkpoint_values
    )
    (
        expected_lane,
        expected_anchor,
        expected_ref,
        expected_delta,
        anchor_document,
    ) = _lane_and_anchor_bindings(
        repo_root,
        spec,
        entry_base_commit=str(lane.get("entryBaseCommit")),
        product_candidate=str(lane.get("productCandidate")),
        seal_commit=str(lane.get("sealCommit")),
        checkpoint_delta=checkpoint_input,
        dependency_commits=dependency_commits,
    )
    if (
        lane != expected_lane
        or anchor_binding != expected_anchor
        or preservation_ref != expected_ref
        or checkpoint_values != expected_delta
    ):
        raise AnchorBuildError("REGISTRY_TOPOLOGY", registry_path)
    ledger_prefixes = _mapping(registry.get("ledgerPrefixes"))
    if ledger_prefixes != anchor_document.get("ledgerPrefixes"):
        raise AnchorBuildError("REGISTRY_LEDGER_PREFIX", registry_path)

    evidence_binding = _mapping(registry.get("evidenceBundle"))
    if set(evidence_binding) != EVIDENCE_BUNDLE_BINDING_FIELDS:
        raise AnchorBuildError("REGISTRY_BUNDLE_BINDING", registry_path)
    expected_evidence_binding = {
        "path": bundle_path,
        "sha256": hashlib.sha256(bundle_raw).hexdigest(),
        "entryCount": bundle.get("entryCount"),
        "totalBytes": bundle.get("totalBytes"),
        "contentRootSha256": bundle.get("contentRootSha256"),
    }
    if evidence_binding != expected_evidence_binding:
        raise AnchorBuildError("REGISTRY_BUNDLE_BINDING", registry_path)
    _validate_bundle_document_or_raise(
        repo_root,
        spec,
        bundle,
        anchor_binding,
        anchor_document,
    )

    expected_parent = (
        anchor_binding["commit"]
        if GROUP_SEQUENCE[spec.group_id] == 1
        else prior_commits[CANONICAL_GROUPS[GROUP_SEQUENCE[spec.group_id] - 2].group_id]
    )
    if _git_commit_parents(repo_root, registry_commit) != (expected_parent,):
        raise AnchorBuildError("REGISTRY_COMMIT_PARENT", registry_path)
    changes = _tree_changes(
        _git_tree_map(repo_root, expected_parent),
        _git_tree_map(repo_root, registry_commit),
    )
    expected_paths = {registry_path, bundle_path}
    if set(changes) != expected_paths or any(
        before is not None
        or after is None
        or after[0] != "100644"
        for before, after in changes.values()
    ):
        raise AnchorBuildError("REGISTRY_COMMIT_DELTA", registry_path)
    return registry_commit


def _unexpected_registry_file(repo_root: Path) -> str | None:
    directory = repo_root.joinpath(*PurePosixPath(ANCHOR_REGISTRY_DIRECTORY).parts)
    if not directory.exists() and not directory.is_symlink():
        return None
    if not directory.is_dir() or _git_path_is_reparse(directory):
        return ANCHOR_REGISTRY_DIRECTORY
    expected = {
        PurePosixPath(registry_path_for_group(spec.group_id)).name
        for spec in CANONICAL_GROUPS
    } | {
        PurePosixPath(bundle_path_for_group(spec.group_id)).name
        for spec in CANONICAL_GROUPS
    }
    try:
        for child in directory.iterdir():
            if (
                child.name not in expected
                or not child.is_file()
                or _git_path_is_reparse(child)
            ):
                return f"{ANCHOR_REGISTRY_DIRECTORY}/{child.name}"
    except OSError:
        return ANCHOR_REGISTRY_DIRECTORY
    return None


@_trusted_git_validation_bundle
def validate_anchor_registry(
    repo_root: Path,
    *,
    complete: bool = False,
) -> list[AnchorIssue]:
    """Validate the immutable off-branch anchor registry from the control root."""

    try:
        root = _require_control_root(repo_root)
    except (AnchorBuildError, OSError) as error:
        location = (
            error.location if isinstance(error, AnchorBuildError) else ANCHOR_REGISTRY_DIRECTORY
        )
        return [AnchorIssue("REGISTRY_CONTROL_ROOT", location)]
    unexpected = _unexpected_registry_file(root)
    if unexpected is not None:
        return [AnchorIssue("REGISTRY_UNEXPECTED_FILE", unexpected)]
    try:
        present = _present_registry_prefix(root)
    except AnchorBuildError as error:
        return [AnchorIssue(error.code, error.location)]
    if complete and len(present) != len(CANONICAL_GROUPS):
        missing = CANONICAL_GROUPS[len(present)].group_id
        return [AnchorIssue("REGISTRY_MISSING", registry_path_for_group(missing))]

    issues: list[AnchorIssue] = []
    commits: dict[str, str] = {}
    for group_id in present:
        spec = GROUP_BY_ID[group_id]
        try:
            commits[group_id] = _validate_registry_record_or_raise(
                root, spec, commits
            )
        except AnchorBuildError as error:
            issues.append(AnchorIssue(error.code, error.location))
            break
    return sorted(set(issues))


def coordinate_registry_entry(
    repo_root: Path,
    group_id: str,
    *,
    entry_base_commit: str,
    product_candidate: str,
    seal_commit: str,
    checkpoint_delta: Sequence[tuple[str, str]] = (),
) -> RegistryBuildResult:
    """Copy one sealed group's evidence to CAS and write its registry pair.

    The function deliberately does not stage or commit.  The caller reviews the
    two exclusive-created tracked documents, commits exactly those paths, then
    runs :func:`validate_anchor_registry` to derive and verify the first-add
    registry commit.
    """

    root = _require_control_root(repo_root)
    spec = GROUP_BY_ID.get(group_id)
    if spec is None:
        raise AnchorBuildError("REGISTRY_GROUP_UNKNOWN", ANCHOR_REGISTRY_DIRECTORY)
    existing_issues = validate_anchor_registry(root)
    if existing_issues:
        raise AnchorBuildError(existing_issues[0].code, existing_issues[0].location)
    present = _present_registry_prefix(root)
    expected_index = len(present)
    if (
        expected_index >= len(CANONICAL_GROUPS)
        or CANONICAL_GROUPS[expected_index].group_id != group_id
    ):
        raise AnchorBuildError("REGISTRY_ORDER", registry_path_for_group(group_id))
    status_code, status_raw = _git(
        root,
        ["status", "--porcelain=v1", "--untracked-files=all"],
    )
    if status_code != 0 or status_raw.strip():
        raise AnchorBuildError("REGISTRY_CONTROL_DIRTY", ANCHOR_REGISTRY_DIRECTORY)

    protocol_parents, entry_barrier, previous = _expected_registry_dependencies(
        root, spec
    )
    dependency_commits = {
        binding["groupId"]: binding["commit"]
        for binding in [*protocol_parents, *entry_barrier]
    }
    (
        lane_binding,
        anchor_binding,
        preservation_ref,
        checkpoint_bindings,
        anchor_document,
    ) = _lane_and_anchor_bindings(
        root,
        spec,
        entry_base_commit=entry_base_commit,
        product_candidate=product_candidate,
        seal_commit=seal_commit,
        checkpoint_delta=checkpoint_delta,
        dependency_commits=dependency_commits,
    )
    expected_control_head = (
        anchor_binding["commit"] if previous is None else previous["commit"]
    )
    actual_control_head = _git_ref_target(root, "HEAD", "REGISTRY_CONTROL_BRANCH")
    if actual_control_head != expected_control_head:
        raise AnchorBuildError("REGISTRY_CONTROL_PARENT", ANCHOR_REGISTRY_DIRECTORY)

    bundle, store_objects = _build_bundle_document(
        root,
        spec,
        anchor_binding,
        anchor_document,
    )
    bundle_raw = _pretty_json_bytes(bundle)
    bundle_path = bundle_path_for_group(group_id)
    registry_path = registry_path_for_group(group_id)
    registry = {
        "schemaVersion": SCHEMA_VERSION,
        "protocol": ANCHOR_REGISTRY_PROTOCOL,
        "goalId": GOAL_ID,
        "sequence": GROUP_SEQUENCE[group_id],
        "groupId": group_id,
        "week": spec.week,
        "lane": spec.lane,
        "objectFormat": "sha1",
        "laneBinding": lane_binding,
        "anchorBinding": anchor_binding,
        "preservationRef": preservation_ref,
        "checkpointDelta": checkpoint_bindings,
        "evidenceBundle": {
            "path": bundle_path,
            "sha256": hashlib.sha256(bundle_raw).hexdigest(),
            "entryCount": bundle["entryCount"],
            "totalBytes": bundle["totalBytes"],
            "contentRootSha256": bundle["contentRootSha256"],
        },
        "ledgerPrefixes": anchor_document["ledgerPrefixes"],
        "protocolParents": protocol_parents,
        "entryBarrier": entry_barrier,
        "previousRegistry": previous,
    }
    for path in (bundle_path, registry_path):
        target = _strict_repo_path(
            root, path, allow_missing_leaf=True, create_parents=True
        )
        if target.exists() or target.is_symlink():
            raise AnchorBuildError("REGISTRY_PATH_EXISTS", path)
    written_bundle = _write_exclusive_json(root, bundle_path, bundle)
    if written_bundle != bundle_raw:
        raise AnchorBuildError("REGISTRY_BUNDLE_WRITE", bundle_path)
    registry_raw = _write_exclusive_json(root, registry_path, registry)

    post_bindings = _lane_and_anchor_bindings(
        root,
        spec,
        entry_base_commit=entry_base_commit,
        product_candidate=product_candidate,
        seal_commit=seal_commit,
        checkpoint_delta=checkpoint_delta,
        dependency_commits=dependency_commits,
    )
    if post_bindings != (
        lane_binding,
        anchor_binding,
        preservation_ref,
        checkpoint_bindings,
        anchor_document,
    ):
        raise AnchorBuildError("REGISTRY_POSTWRITE_DRIFT", registry_path)
    if _git_ref_target(root, "HEAD", "REGISTRY_CONTROL_BRANCH") != expected_control_head:
        raise AnchorBuildError("REGISTRY_CONTROL_PARENT", ANCHOR_REGISTRY_DIRECTORY)
    if _trusted_regular_bytes(
        root,
        bundle_path,
        expected_sha256=hashlib.sha256(bundle_raw).hexdigest(),
        limit=MAX_JSON_BYTES,
    ) != bundle_raw or _trusted_regular_bytes(
        root,
        registry_path,
        expected_sha256=hashlib.sha256(registry_raw).hexdigest(),
        limit=MAX_JSON_BYTES,
    ) != registry_raw:
        raise AnchorBuildError("REGISTRY_POSTWRITE_DRIFT", registry_path)
    _validate_bundle_document_or_raise(
        root,
        spec,
        bundle,
        anchor_binding,
        anchor_document,
    )
    return RegistryBuildResult(
        registry=registry,
        bundle=bundle,
        registry_path=registry_path,
        bundle_path=bundle_path,
        store_objects=store_objects,
    )


class _SafeArgumentParser(argparse.ArgumentParser):
    def error(self, _message: str) -> None:
        self.exit(2, "evidence anchor: invalid command line\n")


def _cli_parser() -> argparse.ArgumentParser:
    parser = _SafeArgumentParser(
        description="Week84-92 off-branch evidence registry coordinator"
    )
    subparsers = parser.add_subparsers(dest="command", required=True)
    register = subparsers.add_parser("register", help="publish CAS and registry drafts")
    register.add_argument("--repo-root", default=".")
    register.add_argument("--group-id", required=True, choices=tuple(GROUP_BY_ID))
    register.add_argument("--entry-base-commit", required=True)
    register.add_argument("--product-candidate", required=True)
    register.add_argument("--seal-commit", required=True)
    register.add_argument(
        "--checkpoint",
        action="append",
        default=[],
        metavar="KIND=PATH",
    )
    validate = subparsers.add_parser("validate-registry", help="validate committed registry")
    validate.add_argument("--repo-root", default=".")
    validate.add_argument("--complete", action="store_true")
    return parser


def _parse_checkpoint_arguments(values: Sequence[str]) -> tuple[tuple[str, str], ...]:
    parsed: list[tuple[str, str]] = []
    for value in values:
        if not isinstance(value, str) or "=" not in value:
            raise AnchorBuildError("REGISTRY_CHECKPOINT_INPUT", "--checkpoint")
        kind, path = value.split("=", 1)
        if not kind or not path:
            raise AnchorBuildError("REGISTRY_CHECKPOINT_INPUT", "--checkpoint")
        parsed.append((kind, path))
    return tuple(parsed)


def main(argv: Sequence[str] | None = None) -> int:
    arguments = _cli_parser().parse_args(argv)
    try:
        root = Path(arguments.repo_root)
        if arguments.command == "register":
            result = coordinate_registry_entry(
                root,
                arguments.group_id,
                entry_base_commit=arguments.entry_base_commit,
                product_candidate=arguments.product_candidate,
                seal_commit=arguments.seal_commit,
                checkpoint_delta=_parse_checkpoint_arguments(arguments.checkpoint),
            )
            print(
                canonical_json_bytes(
                    {
                        "status": "Prepared",
                        "groupId": arguments.group_id,
                        "registryPath": result.registry_path,
                        "bundlePath": result.bundle_path,
                        "storeObjectCount": len(set(result.store_objects)),
                    }
                ).decode("utf-8")
            )
            return 0
        issues = validate_anchor_registry(root, complete=bool(arguments.complete))
        if issues:
            for issue in issues:
                print(str(issue), file=sys.stderr)
            return 1
        print('{"status":"Passed"}')
        return 0
    except AnchorBuildError as error:
        print(str(error), file=sys.stderr)
        return 1
    except (OSError, ValueError):
        print("[REGISTRY_INTERNAL] evidence registry", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
