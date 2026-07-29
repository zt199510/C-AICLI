from __future__ import annotations

from copy import deepcopy
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from tools import week84_92_evidence_anchor as anchor
from tools import week84_92_trusted_executor as trusted_executor


PRESEAL_STARTED_AT = "2026-07-28T00:03:00Z"
PRESEAL_FINISHED_AT = "2026-07-28T00:04:00Z"


def json_bytes(value: object) -> bytes:
    return (
        json.dumps(
            value,
            ensure_ascii=False,
            sort_keys=True,
            indent=2,
        )
        + "\n"
    ).encode("utf-8")


def write_json(path: Path, value: object) -> bytes:
    payload = json_bytes(value)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)
    return payload


def raw_sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def trusted_fixture_git_run(
    root: Path,
    arguments: tuple[str, ...],
) -> subprocess.CompletedProcess[bytes]:
    repository = root.resolve()
    executable, before_identity = trusted_executor._trusted_git_identity(
        repository
    )
    hardened = list(arguments)
    if hardened and hardened[0] in {
        "diff",
        "diff-files",
        "diff-index",
        "diff-tree",
    }:
        hardened[1:1] = ["--no-ext-diff", "--no-textconv"]
    command = [str(executable)]
    if (repository / ".git").exists():
        command.extend(
            [
                f"--git-dir={repository / '.git'}",
                f"--work-tree={repository}",
            ]
        )
    command.extend(
        [
            "-c",
            "core.fsmonitor=false",
            "-c",
            "core.untrackedCache=false",
            "-c",
            f"core.attributesFile={os.devnull}",
            "-c",
            "diff.external=",
            "-c",
            "submodule.recurse=false",
            "-c",
            "protocol.allow=never",
            *hardened,
        ]
    )
    completed = subprocess.run(
        command,
        cwd=repository,
        env=trusted_executor._git_environment(executable),
        stdin=subprocess.DEVNULL,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        shell=False,
        check=True,
        timeout=30,
    )
    after_executable, after_identity = trusted_executor._trusted_git_identity(
        repository
    )
    if executable != after_executable or before_identity != after_identity:
        raise RuntimeError("trusted fixture Git identity changed")
    return completed


def chained_entries(sources: list[dict[str, object]]) -> list[dict[str, object]]:
    result: list[dict[str, object]] = []
    previous: str | None = None
    for sequence, source in enumerate(sources, start=1):
        entry = deepcopy(source)
        entry["sequence"] = sequence
        entry["previousEntrySha256"] = previous
        entry["entrySha256"] = anchor.canonical_entry_sha256(entry)
        result.append(entry)
        previous = str(entry["entrySha256"])
    return result


def chained_attempt_events(
    sources: list[dict[str, object]],
) -> list[dict[str, object]]:
    result: list[dict[str, object]] = []
    previous: str | None = None
    for sequence, source in enumerate(sources, start=1):
        event = deepcopy(source)
        event["attemptEventSequence"] = sequence
        event["previousAttemptEventSha256"] = previous
        event["attemptEventSha256"] = anchor.canonical_attempt_event_sha256(
            event
        )
        result.append(event)
        previous = str(event["attemptEventSha256"])
    return result


def chained_runtime_events(
    sources: list[dict[str, object]],
) -> list[dict[str, object]]:
    result: list[dict[str, object]] = []
    previous: str | None = None
    for sequence, source in enumerate(sources, start=1):
        event = deepcopy(source)
        event["sequence"] = sequence
        event["previousEventSha256"] = previous
        event["eventSha256"] = anchor.canonical_runtime_event_sha256(event)
        result.append(event)
        previous = str(event["eventSha256"])
    return result


def provider_reservation(
    candidate: str,
    *,
    ordinal: int = 1,
) -> dict[str, object]:
    return {
        "eventType": "TurnReserved",
        "reservationId": f"reservation-{ordinal}",
        "gateId": "W84-G6",
        "phase": "provider-read-only",
        "productCandidate": candidate,
        "attemptId": f"attempt-{ordinal}",
        "runId": f"run-{ordinal}",
        "reservedAt": f"2026-07-28T00:00:{ordinal:02d}Z",
    }


def provider_completion(
    *,
    ordinal: int = 1,
    outcome: str = "Succeeded",
) -> dict[str, object]:
    return {
        "eventType": "TurnCompleted",
        "reservationId": f"reservation-{ordinal}",
        "outcome": outcome,
        "completedAt": f"2026-07-28T00:01:{ordinal:02d}Z",
    }


def provider_attempt_finished(
    candidate: str,
    *,
    ordinal: int = 1,
    outcome: str = "Passed",
) -> dict[str, object]:
    return {
        "eventType": "AttemptFinished",
        "gateId": "W84-G6",
        "productCandidate": candidate,
        "attemptId": f"attempt-{ordinal}",
        "outcome": outcome,
        "reservationSequenceStart": ordinal,
        "reservationSequenceEnd": ordinal,
        "finishedAt": f"2026-07-28T00:02:{ordinal:02d}Z",
    }


def provider_runtime_event(
    candidate: str,
    *,
    ordinal: int = 1,
) -> dict[str, object]:
    return {
        "eventType": "ProviderTurnExecuted",
        "reservationId": f"reservation-{ordinal}",
        "reservationSequence": ordinal,
        "gateId": "W84-G6",
        "phase": "provider-read-only",
        "productCandidate": candidate,
        "attemptId": f"attempt-{ordinal}",
        "runId": f"run-{ordinal}",
        "commandPolicy": {
            "policyId": "week84-92-provider-turn-v1",
            "sha256": "1" * 64,
        },
        "argvSha256": "2" * 64,
        "harnessSource": {"path": "tools/week84_92_provider_turn_harness.py"},
        "checkoutIdentity": {"before": candidate, "after": candidate},
        "startedAt": f"2026-07-28T00:03:{ordinal:02d}Z",
        "finishedAt": f"2026-07-28T00:04:{ordinal:02d}Z",
        "exitCode": 0,
        "outcome": "Succeeded",
    }


class EvidenceAnchorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name).resolve()
        self.git("init", "-q")
        self.git("config", "user.email", "evidence-anchor@example.invalid")
        self.git("config", "user.name", "Evidence Anchor Test")
        self.git("config", "core.autocrlf", "false")
        (self.root / ".gitignore").write_text("artifacts/\n", encoding="utf-8")
        (self.root / "README.md").write_text("fixture\n", encoding="utf-8")
        source_payloads = {
            anchor.VALIDATOR_SOURCE_PATH: b"# validator fixture\n",
            anchor.ANCHOR_SOURCE_PATH: b"# anchor helper fixture\n",
            anchor.REQUIREMENTS_MANIFEST_PATH: b"{}\n",
        }
        for relative_path, payload in source_payloads.items():
            path = self.root / relative_path
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(payload)
        self.git(
            "add",
            "--",
            ".gitignore",
            "README.md",
            *source_payloads,
        )
        self.git("commit", "-q", "-m", "initial product candidate")
        self.main_branch = self.git("rev-parse", "--abbrev-ref", "HEAD")
        self.product_candidate = self.git("rev-parse", "HEAD")
        self.write_ledgers()

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def git(self, *arguments: str) -> str:
        completed = trusted_fixture_git_run(self.root, arguments)
        return completed.stdout.decode("utf-8").strip()

    def write_ledgers(
        self,
        *,
        first_failure: list[dict[str, object]] | None = None,
        user_decision: list[dict[str, object]] | None = None,
        provider: list[dict[str, object]] | None = None,
        provider_attempt_events: list[dict[str, object]] | None = None,
        provider_runtime_events: list[dict[str, object]] | None = None,
    ) -> None:
        failure_entries = chained_entries(first_failure or [])
        decision_entries = chained_entries(user_decision or [])
        provider_entries = chained_entries(provider or [])
        attempt_events = chained_attempt_events(provider_attempt_events or [])
        runtime_events = chained_runtime_events(provider_runtime_events or [])
        common = {
            "schemaVersion": "1.0.0",
            "goalId": anchor.GOAL_ID,
            "hashAlgorithm": "SHA-256",
        }
        write_json(
            self.root / anchor.FIRST_FAILURE_LEDGER_PATH,
            {
                **common,
                "entryCount": len(failure_entries),
                "lastEntrySha256": (
                    failure_entries[-1]["entrySha256"]
                    if failure_entries
                    else None
                ),
                "entries": failure_entries,
            },
        )
        write_json(
            self.root / anchor.USER_DECISION_LEDGER_PATH,
            {
                **common,
                "entryCount": len(decision_entries),
                "lastEntrySha256": (
                    decision_entries[-1]["entrySha256"]
                    if decision_entries
                    else None
                ),
                "entries": decision_entries,
            },
        )
        write_json(
            self.root / anchor.PROVIDER_LEDGER_PATH,
            {
                "schemaVersion": anchor.SCHEMA_VERSION,
                "journalVersion": anchor.PROVIDER_JOURNAL_VERSION,
                "goalId": anchor.GOAL_ID,
                "maxTurns": anchor.PROVIDER_MAX_TURNS,
                "usedTurns": len(provider_entries),
                "remainingTurns": (
                    anchor.PROVIDER_MAX_TURNS - len(provider_entries)
                ),
                "ledgerSequence": len(provider_entries),
                "entries": provider_entries,
                "attemptEventCount": len(attempt_events),
                "lastAttemptEventSha256": (
                    attempt_events[-1]["attemptEventSha256"]
                    if attempt_events
                    else None
                ),
                "attemptEvents": attempt_events,
            },
        )
        write_json(
            self.root / anchor.PROVIDER_RUNTIME_JOURNAL_PATH,
            {
                "schemaVersion": anchor.SCHEMA_VERSION,
                "journalVersion": anchor.PROVIDER_RUNTIME_JOURNAL_VERSION,
                "goalId": anchor.GOAL_ID,
                "eventCount": len(runtime_events),
                "lastEventSha256": (
                    runtime_events[-1]["eventSha256"]
                    if runtime_events
                    else None
                ),
                "events": runtime_events,
            },
        )

    def ledger_snapshot(self, candidate: str) -> dict[str, object]:
        failure_path = self.root / anchor.FIRST_FAILURE_LEDGER_PATH
        decision_path = self.root / anchor.USER_DECISION_LEDGER_PATH
        provider_path = self.root / anchor.PROVIDER_LEDGER_PATH
        runtime_path = self.root / anchor.PROVIDER_RUNTIME_JOURNAL_PATH
        failure = json.loads(failure_path.read_text(encoding="utf-8"))
        decision = json.loads(decision_path.read_text(encoding="utf-8"))
        provider = json.loads(provider_path.read_text(encoding="utf-8"))
        runtime = json.loads(runtime_path.read_text(encoding="utf-8"))
        return {
            "identity": {"productCandidate": candidate},
            "firstFailureLedger": {
                "path": anchor.FIRST_FAILURE_LEDGER_PATH,
                "sha256": raw_sha256(failure_path),
                "entryCount": failure["entryCount"],
                "lastEntrySha256": failure["lastEntrySha256"],
            },
            "userDecisionLedger": {
                "path": anchor.USER_DECISION_LEDGER_PATH,
                "sha256": raw_sha256(decision_path),
                "entryCount": decision["entryCount"],
                "lastEntrySha256": decision["lastEntrySha256"],
            },
            "authorization": {
                "provider": {
                    "ledgerPath": anchor.PROVIDER_LEDGER_PATH,
                    "ledgerSha256": raw_sha256(provider_path),
                    "ledgerSequence": provider["ledgerSequence"],
                    "attemptEventCount": provider["attemptEventCount"],
                    "lastAttemptEventSha256": provider[
                        "lastAttemptEventSha256"
                    ],
                    "runtimeJournalPath": anchor.PROVIDER_RUNTIME_JOURNAL_PATH,
                    "runtimeJournalSha256": raw_sha256(runtime_path),
                    "runtimeEventCount": runtime["eventCount"],
                    "lastRuntimeEventSha256": runtime["lastEventSha256"],
                }
            },
        }

    def prepare_group(self, group_id: str, candidate: str | None = None) -> None:
        product_candidate = candidate or self.product_candidate
        spec = anchor.GROUP_BY_ID[group_id]
        gate_results: list[dict[str, str]] = []
        for gate_id, gate_path in zip(spec.gate_ids, spec.gate_paths):
            evidence_path = (
                f"{spec.artifact_directory}/gate-evidence/{gate_id}/result.txt"
            )
            evidence_payload = f"evidence for {gate_id}\n".encode("utf-8")
            resolved_evidence_path = self.root / evidence_path
            resolved_evidence_path.parent.mkdir(parents=True, exist_ok=True)
            resolved_evidence_path.write_bytes(evidence_payload)
            payload = write_json(
                self.root / gate_path,
                {
                    "week": spec.week,
                    "lane": spec.lane,
                    "gateId": gate_id,
                    "resultPath": gate_path,
                    "status": "Passed",
                    "identity": {"productCandidate": product_candidate},
                    "evidence": [
                        {
                            "evidenceId": f"{gate_id}-result",
                            "kind": "log",
                            "path": evidence_path,
                            "sha256": hashlib.sha256(
                                evidence_payload
                            ).hexdigest(),
                            "redacted": True,
                            "preservesFirstFailure": False,
                        }
                    ],
                },
            )
            gate_results.append(
                {
                    "gateId": gate_id,
                    "resultPath": gate_path,
                    "resultSha256": hashlib.sha256(payload).hexdigest(),
                }
            )
        write_json(
            self.root / spec.snapshot_path,
            self.ledger_snapshot(product_candidate),
        )
        handoff = {
            "week": spec.week,
            "lane": spec.lane,
            "decision": "ReadyForNextCheckpoint",
            "requiredGatesSatisfied": True,
            "identity": {"productCandidate": product_candidate},
            "gateResults": gate_results,
        }
        write_json(self.root / spec.handoff_path, handoff)
        if spec.alias_path is not None:
            write_json(self.root / spec.alias_path, handoff)

    def set_gate_candidate(
        self,
        group_id: str,
        gate_id: str,
        candidate: str,
    ) -> None:
        spec = anchor.GROUP_BY_ID[group_id]
        gate_index = list(spec.gate_ids).index(gate_id)
        gate_path = self.root / spec.gate_paths[gate_index]
        gate = json.loads(gate_path.read_text(encoding="utf-8"))
        gate["identity"]["productCandidate"] = candidate
        gate_raw = write_json(gate_path, gate)
        handoff_path = self.root / spec.handoff_path
        handoff = json.loads(handoff_path.read_text(encoding="utf-8"))
        handoff["gateResults"][gate_index]["resultSha256"] = hashlib.sha256(
            gate_raw
        ).hexdigest()
        write_json(handoff_path, handoff)
        if spec.alias_path is not None:
            write_json(self.root / spec.alias_path, handoff)

    def build_anchor(
        self,
        group_id: str,
        candidate: str | None = None,
    ) -> dict[str, object]:
        product_candidate = candidate or self.product_candidate
        self.prepare_group(group_id, product_candidate)
        self.write_preseal_receipt(group_id, product_candidate)
        document = anchor.build_anchor_document(
            self.root,
            group_id,
            product_candidate,
        )
        write_json(self.root / anchor.GROUP_BY_ID[group_id].anchor_path, document)
        return document

    def write_preseal_receipt(
        self,
        group_id: str,
        candidate: str | None = None,
        *,
        started_at: str = PRESEAL_STARTED_AT,
        finished_at: str = PRESEAL_FINISHED_AT,
        exit_code: int = 0,
    ) -> dict[str, object]:
        product_candidate = candidate or self.product_candidate
        source_bindings = anchor.capture_preseal_source_bindings(self.root)
        return anchor.write_preseal_receipt_document(
            self.root,
            group_id,
            product_candidate,
            started_at=started_at,
            finished_at=finished_at,
            exit_code=exit_code,
            expected_source_bindings=source_bindings,
        )

    def seal_anchors(self, message: str = "seal evidence anchors") -> None:
        self.git("add", "--", anchor.ANCHOR_DIRECTORY)
        self.git("commit", "-q", "-m", message)

    def validation_codes(self, *, complete: bool = False) -> set[str]:
        return anchor.issue_codes(
            anchor.validate_evidence_anchors(self.root, complete=complete)
        )

    def switch_to_control_branch(self) -> None:
        self.git("branch", "-M", "codex/week84-92-refactor")

    def prepare_w84_registry(
        self,
    ) -> tuple[anchor.RegistryBuildResult, str, str]:
        self.switch_to_control_branch()
        self.build_anchor("w84-baseline")
        self.seal_anchors("seal W84 baseline")
        anchor_commit = self.git("rev-parse", "HEAD")
        self.git(
            "update-ref",
            anchor.sealed_ref_for_group("w84-baseline"),
            anchor_commit,
        )
        result = anchor.coordinate_registry_entry(
            self.root,
            "w84-baseline",
            entry_base_commit=self.product_candidate,
            product_candidate=self.product_candidate,
            seal_commit=self.product_candidate,
        )
        self.git("add", "--", result.registry_path, result.bundle_path)
        self.git("commit", "-q", "-m", "register W84 baseline")
        registry_commit = self.git("rev-parse", "HEAD")
        return result, anchor_commit, registry_commit

    def prepare_w85_renderer_registry(
        self,
    ) -> tuple[anchor.RegistryBuildResult, str, str]:
        _w84, _w84_anchor, w84_registry_commit = self.prepare_w84_registry()
        self.git("checkout", "-q", "-b", "codex/week84-92-renderer")
        self.build_anchor("w85-renderer", w84_registry_commit)
        self.seal_anchors("seal W85 renderer")
        renderer_anchor = self.git("rev-parse", "HEAD")
        self.git(
            "update-ref",
            anchor.sealed_ref_for_group("w85-renderer"),
            renderer_anchor,
        )
        self.git("checkout", "-q", "codex/week84-92-refactor")
        result = anchor.coordinate_registry_entry(
            self.root,
            "w85-renderer",
            entry_base_commit=w84_registry_commit,
            product_candidate=w84_registry_commit,
            seal_commit=w84_registry_commit,
        )
        self.git("add", "--", result.registry_path, result.bundle_path)
        self.git("commit", "-q", "-m", "register W85 renderer")
        registry_commit = self.git("rev-parse", "HEAD")
        return result, renderer_anchor, registry_commit

    def commit_w84_anchor_at_seal(
        self,
        product_candidate: str,
        seal_commit: str,
    ) -> str:
        self.assertEqual(seal_commit, self.git("rev-parse", "HEAD"))
        self.build_anchor("w84-baseline", product_candidate)
        self.seal_anchors("seal W84 adversarial candidate")
        anchor_commit = self.git("rev-parse", "HEAD")
        self.git(
            "update-ref",
            anchor.sealed_ref_for_group("w84-baseline"),
            anchor_commit,
        )
        return anchor_commit

    def test_builder_is_pure_and_active_prefix_is_valid(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.prepare_group(spec.group_id)
        self.write_preseal_receipt(spec.group_id)
        anchor_path = self.root / spec.anchor_path

        document = anchor.build_anchor_document(
            self.root,
            spec.group_id,
            self.product_candidate,
        )

        self.assertFalse(anchor_path.exists())
        self.assertNotIn("anchorCommit", document)
        self.assertEqual(
            spec.preseal_receipt_path,
            document["presealReceipt"]["path"],
        )
        self.assertEqual(list(spec.gate_ids), [item["gateId"] for item in document["gates"]])
        self.assertEqual(
            anchor.PROVIDER_LEDGER_PREFIX_FIELDS,
            set(document["ledgerPrefixes"]["provider"]),
        )
        write_json(anchor_path, document)
        self.seal_anchors()
        self.assertEqual([], anchor.validate_evidence_anchors(self.root))

    def test_gate_candidates_may_advance_monotonically_to_final_candidate(
        self,
    ) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        baseline_candidate = self.product_candidate
        (self.root / "final-candidate.txt").write_text("final\n", encoding="utf-8")
        self.git("add", "--", "final-candidate.txt")
        self.git("commit", "-q", "-m", "create final candidate")
        final_candidate = self.git("rev-parse", "HEAD")
        self.prepare_group(spec.group_id, final_candidate)
        self.set_gate_candidate(
            spec.group_id,
            spec.gate_ids[0],
            baseline_candidate,
        )
        self.set_gate_candidate(
            spec.group_id,
            spec.gate_ids[1],
            baseline_candidate,
        )
        self.write_preseal_receipt(spec.group_id, final_candidate)
        document = anchor.build_anchor_document(
            self.root,
            spec.group_id,
            final_candidate,
        )
        write_json(self.root / spec.anchor_path, document)
        self.seal_anchors()

        self.assertEqual([], anchor.validate_evidence_anchors(self.root))

    def test_builder_rejects_reverse_gate_candidate_order(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        baseline_candidate = self.product_candidate
        (self.root / "later-candidate.txt").write_text("later\n", encoding="utf-8")
        self.git("add", "--", "later-candidate.txt")
        self.git("commit", "-q", "-m", "create later candidate")
        final_candidate = self.git("rev-parse", "HEAD")
        self.prepare_group(spec.group_id, final_candidate)
        self.set_gate_candidate(
            spec.group_id,
            spec.gate_ids[1],
            baseline_candidate,
        )

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            self.write_preseal_receipt(spec.group_id, final_candidate)

        self.assertEqual("BUILD_GATE_CANDIDATE_ORDER", raised.exception.code)

    def test_builder_rejects_non_ancestor_gate_candidate(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        baseline_candidate = self.product_candidate
        (self.root / "final-candidate.txt").write_text("final\n", encoding="utf-8")
        self.git("add", "--", "final-candidate.txt")
        self.git("commit", "-q", "-m", "create final candidate")
        final_candidate = self.git("rev-parse", "HEAD")
        final_branch = self.git("rev-parse", "--abbrev-ref", "HEAD")
        self.git("checkout", "-q", "-b", "sibling-candidate", baseline_candidate)
        (self.root / "sibling-candidate.txt").write_text(
            "sibling\n",
            encoding="utf-8",
        )
        self.git("add", "--", "sibling-candidate.txt")
        self.git("commit", "-q", "-m", "create sibling candidate")
        sibling_candidate = self.git("rev-parse", "HEAD")
        self.git("checkout", "-q", final_branch)
        self.prepare_group(spec.group_id, final_candidate)
        self.set_gate_candidate(
            spec.group_id,
            spec.gate_ids[0],
            sibling_candidate,
        )

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            self.write_preseal_receipt(spec.group_id, final_candidate)

        self.assertEqual("BUILD_GATE_CANDIDATE_ORDER", raised.exception.code)

    def test_preseal_receipt_has_exact_schema_and_source_bindings(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.prepare_group(spec.group_id)

        receipt = self.write_preseal_receipt(spec.group_id)

        self.assertEqual(anchor.PRESEAL_RECEIPT_FIELDS, set(receipt))
        self.assertEqual(anchor.PRESEAL_RECEIPT_VERSION, receipt["receiptVersion"])
        self.assertEqual(0, receipt["exitCode"])
        self.assertEqual(list(spec.gate_ids), [item["gateId"] for item in receipt["gates"]])
        for field, relative_path in anchor.PRESEAL_SOURCE_PATHS.items():
            self.assertEqual(relative_path, receipt[field]["path"])
            self.assertEqual(raw_sha256(self.root / relative_path), receipt[field]["sha256"])
        runtime_prefix = receipt["ledgerPrefixes"]["providerRuntime"]
        self.assertEqual(anchor.PROVIDER_RUNTIME_JOURNAL_PATH, runtime_prefix["path"])
        self.assertEqual(0, runtime_prefix["entryCount"])
        self.assertIsNone(runtime_prefix["lastEntrySha256"])
        for gate in receipt["gates"]:
            expected_root = (
                f"{spec.artifact_directory}/gate-evidence/{gate['gateId']}/"
            )
            self.assertTrue(gate["evidence"])
            self.assertTrue(
                all(
                    item["path"].startswith(expected_root)
                    for item in gate["evidence"]
                )
            )

    def test_builder_rejects_evidence_path_reused_across_gates(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.prepare_group(spec.group_id)
        first_gate = json.loads(
            (self.root / spec.gate_paths[0]).read_text(encoding="utf-8")
        )
        second_gate_path = self.root / spec.gate_paths[1]
        second_gate = json.loads(second_gate_path.read_text(encoding="utf-8"))
        second_gate["evidence"][0]["path"] = first_gate["evidence"][0]["path"]
        second_gate["evidence"][0]["sha256"] = first_gate["evidence"][0][
            "sha256"
        ]
        write_json(second_gate_path, second_gate)

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            self.write_preseal_receipt(spec.group_id)

        self.assertEqual(
            "BUILD_GATE_EVIDENCE_PATH_REUSED", raised.exception.code
        )

    def test_builder_rejects_evidence_outside_gate_local_root(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.prepare_group(spec.group_id)
        gate_path = self.root / spec.gate_paths[0]
        gate = json.loads(gate_path.read_text(encoding="utf-8"))
        shared_path = f"{spec.artifact_directory}/shared-evidence.txt"
        shared_payload = b"shared evidence\n"
        (self.root / shared_path).write_bytes(shared_payload)
        gate["evidence"][0]["path"] = shared_path
        gate["evidence"][0]["sha256"] = hashlib.sha256(
            shared_payload
        ).hexdigest()
        write_json(gate_path, gate)

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            self.write_preseal_receipt(spec.group_id)

        self.assertEqual("BUILD_GATE_EVIDENCE_PATH", raised.exception.code)

    def test_provider_ledger_binding_is_the_only_gate_local_exception(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.prepare_group(spec.group_id)
        gate_id = "W84-G6"
        gate_index = list(spec.gate_ids).index(gate_id)
        gate_path = self.root / spec.gate_paths[gate_index]
        gate = json.loads(gate_path.read_text(encoding="utf-8"))
        binding_path = (
            f"{spec.artifact_directory}/provider-ledger-bindings/{gate_id}/"
            "provider-ledger-binding.json"
        )
        binding_payload = json_bytes({"binding": gate_id})
        resolved_binding_path = self.root / binding_path
        resolved_binding_path.parent.mkdir(parents=True, exist_ok=True)
        resolved_binding_path.write_bytes(binding_payload)
        gate["evidence"].append(
            {
                "evidenceId": f"{gate_id}-provider-ledger-binding",
                "kind": "json",
                "path": binding_path,
                "sha256": hashlib.sha256(binding_payload).hexdigest(),
                "redacted": True,
                "preservesFirstFailure": False,
            }
        )
        gate_payload = write_json(gate_path, gate)
        handoff_path = self.root / spec.handoff_path
        handoff = json.loads(handoff_path.read_text(encoding="utf-8"))
        handoff["gateResults"][gate_index]["resultSha256"] = hashlib.sha256(
            gate_payload
        ).hexdigest()
        write_json(handoff_path, handoff)
        write_json(self.root / str(spec.alias_path), handoff)

        receipt = self.write_preseal_receipt(spec.group_id)
        bound_paths = {
            item["path"] for item in receipt["gates"][gate_index]["evidence"]
        }

        self.assertIn(binding_path, bound_paths)

    def test_non_provider_gate_cannot_use_provider_binding_exception(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.prepare_group(spec.group_id)
        gate_id = "W84-G0"
        gate_path = self.root / spec.gate_paths[0]
        gate = json.loads(gate_path.read_text(encoding="utf-8"))
        binding_path = (
            f"{spec.artifact_directory}/provider-ledger-bindings/{gate_id}/"
            "provider-ledger-binding.json"
        )
        binding_payload = b"{}\n"
        resolved_binding_path = self.root / binding_path
        resolved_binding_path.parent.mkdir(parents=True, exist_ok=True)
        resolved_binding_path.write_bytes(binding_payload)
        gate["evidence"][0]["path"] = binding_path
        gate["evidence"][0]["sha256"] = hashlib.sha256(
            binding_payload
        ).hexdigest()
        write_json(gate_path, gate)

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            self.write_preseal_receipt(spec.group_id)

        self.assertEqual("BUILD_GATE_EVIDENCE_PATH", raised.exception.code)

    def test_builder_rejects_skipped_preseal_receipt(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.prepare_group(spec.group_id)

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            anchor.build_anchor_document(
                self.root,
                spec.group_id,
                self.product_candidate,
            )

        self.assertEqual("BUILD_PRESEAL_RECEIPT_MISSING", raised.exception.code)

    def test_builder_rejects_post_preseal_gate_drift(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.prepare_group(spec.group_id)
        self.write_preseal_receipt(spec.group_id)
        gate_path = self.root / spec.gate_paths[0]
        gate = json.loads(gate_path.read_text(encoding="utf-8"))
        gate["postPresealMutation"] = True
        gate_bytes = write_json(gate_path, gate)
        handoff_path = self.root / spec.handoff_path
        handoff = json.loads(handoff_path.read_text(encoding="utf-8"))
        handoff["gateResults"][0]["resultSha256"] = hashlib.sha256(
            gate_bytes
        ).hexdigest()
        write_json(handoff_path, handoff)
        write_json(self.root / str(spec.alias_path), handoff)

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            anchor.build_anchor_document(
                self.root,
                spec.group_id,
                self.product_candidate,
            )

        self.assertEqual("BUILD_PRESEAL_RECEIPT_STALE", raised.exception.code)

    def test_builder_rejects_post_preseal_ledger_drift(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.prepare_group(spec.group_id)
        self.write_preseal_receipt(spec.group_id)
        self.write_ledgers(first_failure=[{"event": "post-preseal append"}])

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            anchor.build_anchor_document(
                self.root,
                spec.group_id,
                self.product_candidate,
            )

        self.assertEqual("BUILD_LEDGER_RAW", raised.exception.code)

    def test_preseal_rejects_provider_runtime_append_after_snapshot(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.prepare_group(spec.group_id)
        self.write_ledgers(
            provider_runtime_events=[
                provider_runtime_event(self.product_candidate)
            ]
        )

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            self.write_preseal_receipt(spec.group_id)

        self.assertEqual("BUILD_LEDGER_RAW", raised.exception.code)

    def test_builder_rejects_tampered_preseal_receipt(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.prepare_group(spec.group_id)
        receipt = self.write_preseal_receipt(spec.group_id)
        receipt["gates"][0]["sha256"] = "0" * 64
        write_json(self.root / spec.preseal_receipt_path, receipt)

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            anchor.build_anchor_document(
                self.root,
                spec.group_id,
                self.product_candidate,
            )

        self.assertEqual("BUILD_PRESEAL_RECEIPT_STALE", raised.exception.code)

    def test_builder_rejects_stale_preseal_source_hash(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.prepare_group(spec.group_id)
        self.write_preseal_receipt(spec.group_id)
        validator_path = self.root / anchor.VALIDATOR_SOURCE_PATH
        validator_path.write_bytes(validator_path.read_bytes() + b"# drift\n")

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            anchor.build_anchor_document(
                self.root,
                spec.group_id,
                self.product_candidate,
            )

        self.assertEqual("BUILD_PRESEAL_RECEIPT_STALE", raised.exception.code)

    def test_trusted_preseal_writer_rejects_source_drift_during_run(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.prepare_group(spec.group_id)
        source_bindings = anchor.capture_preseal_source_bindings(self.root)
        validator_path = self.root / anchor.VALIDATOR_SOURCE_PATH
        validator_path.write_bytes(validator_path.read_bytes() + b"# drift\n")

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            anchor.write_preseal_receipt_document(
                self.root,
                spec.group_id,
                self.product_candidate,
                started_at=PRESEAL_STARTED_AT,
                finished_at=PRESEAL_FINISHED_AT,
                exit_code=0,
                expected_source_bindings=source_bindings,
            )

        self.assertEqual("BUILD_PRESEAL_SOURCE_DRIFT", raised.exception.code)
        self.assertFalse((self.root / spec.preseal_receipt_path).exists())

    def test_preseal_receipt_requires_valid_interval_and_zero_exit(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.prepare_group(spec.group_id)
        cases = (
            (
                "2026-07-28T00:05:00Z",
                "2026-07-28T00:04:00Z",
                0,
                "BUILD_PRESEAL_RECEIPT_TIMESTAMPS",
            ),
            (
                PRESEAL_STARTED_AT,
                PRESEAL_FINISHED_AT,
                1,
                "BUILD_PRESEAL_RECEIPT_EXIT",
            ),
        )
        for started_at, finished_at, exit_code, expected_code in cases:
            with self.subTest(expected_code=expected_code):
                with self.assertRaises(anchor.AnchorBuildError) as raised:
                    anchor.build_preseal_receipt_document(
                        self.root,
                        spec.group_id,
                        self.product_candidate,
                        started_at=started_at,
                        finished_at=finished_at,
                        exit_code=exit_code,
                    )
                self.assertEqual(expected_code, raised.exception.code)

    def test_sealed_anchor_rejects_receipt_raw_tamper(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.build_anchor(spec.group_id)
        self.seal_anchors()
        receipt_path = self.root / spec.preseal_receipt_path
        receipt_path.write_bytes(receipt_path.read_bytes() + b" \n")

        self.assertIn("ANCHOR_PRESEAL_RECEIPT_HASH", self.validation_codes())

    def test_sealed_anchor_rejects_gate_evidence_raw_tamper(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        document = self.build_anchor(spec.group_id)
        self.seal_anchors()
        evidence_path = self.root / document["gates"][0]["evidence"][0]["path"]
        evidence_path.write_bytes(evidence_path.read_bytes() + b"tampered\n")

        self.assertIn("ANCHOR_GATE_EVIDENCE_HASH", self.validation_codes())

    def test_active_mode_allows_no_completed_groups(self) -> None:
        self.assertEqual([], anchor.validate_evidence_anchors(self.root))

    def test_ambient_git_overrides_cannot_substitute_repository(self) -> None:
        with patch.dict(
            os.environ,
            {
                "GIT_DIR": str(self.root / "not-the-repository"),
                "GIT_INDEX_FILE": str(self.root / "fake-index"),
                "GIT_OBJECT_DIRECTORY": str(self.root / "fake-objects"),
                "GIT_WORK_TREE": str(self.root / "fake-worktree"),
            },
            clear=False,
        ):
            self.assertEqual([], anchor.validate_evidence_anchors(self.root))

    def test_replace_refs_make_repository_untrusted(self) -> None:
        readme = self.root / "README.md"
        readme.write_text("fixture replacement\n", encoding="utf-8")
        self.git("add", "--", "README.md")
        self.git("commit", "-q", "-m", "replacement commit")
        replacement = self.git("rev-parse", "HEAD")
        self.git("replace", self.product_candidate, replacement)
        self.assertIn("ANCHOR_GIT_REPOSITORY", self.validation_codes())

    def test_shallow_repository_is_rejected(self) -> None:
        (self.root / ".git" / "shallow").write_text(
            f"{self.product_candidate}\n",
            encoding="ascii",
        )
        self.assertIn("ANCHOR_GIT_REPOSITORY", self.validation_codes())

    def test_grafts_or_object_alternates_make_repository_untrusted(self) -> None:
        grafts = self.root / ".git" / "info" / "grafts"
        grafts.write_text(f"{self.product_candidate}\n", encoding="ascii")
        self.assertIn("ANCHOR_GIT_REPOSITORY", self.validation_codes())
        grafts.unlink()
        alternates = self.root / ".git" / "objects" / "info" / "alternates"
        alternates.write_text(str(self.root / "fake-objects"), encoding="utf-8")
        self.assertIn("ANCHOR_GIT_REPOSITORY", self.validation_codes())

    def test_ready_handoff_requires_its_committed_anchor(self) -> None:
        self.prepare_group("w84-baseline")
        issues = anchor.validate_evidence_anchors(self.root)
        self.assertIn("ANCHOR_MISSING", anchor.issue_codes(issues))

    def test_preseal_allows_only_the_target_ready_handoff_to_lack_anchor(
        self,
    ) -> None:
        self.prepare_group("w84-baseline")

        issues = anchor.validate_evidence_anchors(
            self.root,
            allow_unsealed_ready_group="w84-baseline",
        )

        self.assertEqual([], issues)

    def test_preseal_wrong_target_does_not_exempt_ready_handoff(self) -> None:
        self.prepare_group("w84-baseline")

        issues = anchor.validate_evidence_anchors(
            self.root,
            allow_unsealed_ready_group="w85-renderer",
        )
        codes = anchor.issue_codes(issues)

        self.assertIn("ANCHOR_PRESEAL_NOT_READY", codes)
        self.assertIn("ANCHOR_MISSING", codes)

    def test_preseal_rejects_ready_group_when_parent_anchor_is_missing(
        self,
    ) -> None:
        self.prepare_group("w85-renderer")

        issues = anchor.validate_evidence_anchors(
            self.root,
            allow_unsealed_ready_group="w85-renderer",
        )

        self.assertIn(
            "ANCHOR_PRESEAL_PARENT_MISSING",
            anchor.issue_codes(issues),
        )

    def test_incomplete_group_cannot_be_sealed(self) -> None:
        self.build_anchor("w84-baseline")
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        gate_path = self.root / spec.gate_paths[0]
        gate = json.loads(gate_path.read_text(encoding="utf-8"))
        gate["status"] = "NotRun"
        write_json(gate_path, gate)
        issues = anchor.validate_evidence_anchors(self.root)
        self.assertIn("ANCHOR_GATE_STATUS", anchor.issue_codes(issues))

    def test_complete_mode_reports_every_missing_group(self) -> None:
        self.build_anchor("w84-baseline")
        self.seal_anchors()

        issues = anchor.validate_evidence_anchors(self.root, complete=True)
        missing = [issue for issue in issues if issue.code == "ANCHOR_MISSING"]

        self.assertEqual(len(anchor.CANONICAL_GROUPS) - 1, len(missing))

    def test_complete_mode_accepts_all_fourteen_groups(self) -> None:
        for spec in anchor.CANONICAL_GROUPS:
            self.build_anchor(spec.group_id)
            self.seal_anchors(f"seal {spec.group_id}")

        self.assertEqual(
            [],
            anchor.validate_evidence_anchors(self.root, complete=True),
        )

    def test_committed_anchor_and_handoff_deletion_cannot_erase_history(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.build_anchor(spec.group_id)
        self.seal_anchors()
        (self.root / spec.handoff_path).unlink()
        (self.root / str(spec.alias_path)).unlink()
        self.git("rm", "-q", "--", spec.anchor_path)
        self.git("commit", "-q", "-m", "attempt to erase checkpoint")

        self.assertIn("ANCHOR_GIT_DELETED", self.validation_codes())

    def test_parent_and_child_cannot_first_add_in_same_commit(self) -> None:
        self.build_anchor("w84-baseline")
        self.build_anchor("w85-renderer")
        self.seal_anchors("seal parent and child together")

        self.assertIn(
            "ANCHOR_PARENT_COMMIT_ANCESTRY",
            self.validation_codes(),
        )

    def test_child_without_parent_anchor_is_rejected(self) -> None:
        parent = anchor.GROUP_BY_ID["w84-baseline"]
        self.build_anchor(parent.group_id)
        self.build_anchor("w85-renderer")
        self.seal_anchors()
        self.git("rm", "-q", "--", parent.anchor_path)
        self.git("commit", "-q", "-m", "remove parent fixture")

        self.assertIn("ANCHOR_PARENT_MISSING", self.validation_codes())

    def test_parent_anchor_must_be_committed_before_its_child(self) -> None:
        parent = anchor.GROUP_BY_ID["w84-baseline"]
        child = anchor.GROUP_BY_ID["w85-renderer"]
        self.build_anchor(parent.group_id)
        self.build_anchor(child.group_id)
        self.git("add", "--", child.anchor_path)
        self.git("commit", "-q", "-m", "seal child out of order")
        self.git("add", "--", parent.anchor_path)
        self.git("commit", "-q", "-m", "late parent anchor")

        self.assertIn(
            "ANCHOR_PARENT_COMMIT_ANCESTRY",
            self.validation_codes(),
        )

    def test_rewritten_ignored_gate_is_rejected(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.build_anchor(spec.group_id)
        self.seal_anchors()
        gate_path = self.root / spec.gate_paths[0]
        gate = json.loads(gate_path.read_text(encoding="utf-8"))
        gate["replacement"] = True
        write_json(gate_path, gate)

        self.assertIn("ANCHOR_ARTIFACT_HASH", self.validation_codes())

    def test_rewritten_ignored_alias_is_rejected(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.build_anchor(spec.group_id)
        self.seal_anchors()
        alias_path = self.root / str(spec.alias_path)
        alias_path.write_bytes(alias_path.read_bytes() + b" \n")

        issues = anchor.validate_evidence_anchors(self.root)
        self.assertTrue(
            any(
                issue.code == "ANCHOR_ARTIFACT_HASH"
                and "compatibilityHandoffAlias" in issue.location
                for issue in issues
            )
        )

    def test_dirty_tracked_anchor_is_rejected(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.build_anchor(spec.group_id)
        self.seal_anchors()
        anchor_path = self.root / spec.anchor_path
        anchor_path.write_bytes(anchor_path.read_bytes() + b" \n")

        codes = self.validation_codes()
        self.assertIn("ANCHOR_GIT_DIRTY", codes)
        self.assertIn("ANCHOR_GIT_HEAD_BLOB", codes)

    def test_later_committed_anchor_rewrite_breaks_immutability(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.build_anchor(spec.group_id)
        self.seal_anchors()

        gate_path = self.root / spec.gate_paths[0]
        gate = json.loads(gate_path.read_text(encoding="utf-8"))
        gate["replacement"] = True
        replacement_gate = write_json(gate_path, gate)
        handoff_path = self.root / spec.handoff_path
        handoff = json.loads(handoff_path.read_text(encoding="utf-8"))
        handoff["gateResults"][0]["resultSha256"] = hashlib.sha256(
            replacement_gate
        ).hexdigest()
        write_json(handoff_path, handoff)
        write_json(self.root / str(spec.alias_path), handoff)

        anchor_path = self.root / spec.anchor_path
        self.write_preseal_receipt(spec.group_id)
        resealed = anchor.build_anchor_document(
            self.root,
            spec.group_id,
            self.product_candidate,
        )
        write_json(anchor_path, resealed)
        self.git("add", "--", spec.anchor_path)
        self.git("commit", "-q", "-m", "attempt to reseal replaced evidence")

        codes = self.validation_codes()
        self.assertEqual(
            {"ANCHOR_GIT_HISTORY_MUTATION", "ANCHOR_GIT_IMMUTABLE"},
            codes,
        )

    def test_committed_anchor_rewrite_then_revert_still_breaks_history(
        self,
    ) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.build_anchor(spec.group_id)
        self.seal_anchors()
        anchor_path = self.root / spec.anchor_path
        original = anchor_path.read_bytes()

        document = json.loads(original.decode("utf-8"))
        document["adversarialRewrite"] = True
        write_json(anchor_path, document)
        self.git("add", "--", spec.anchor_path)
        self.git("commit", "-q", "-m", "rewrite immutable anchor")

        anchor_path.write_bytes(original)
        self.git("add", "--", spec.anchor_path)
        self.git("commit", "-q", "-m", "revert anchor bytes")

        codes = self.validation_codes()
        self.assertEqual({"ANCHOR_GIT_HISTORY_MUTATION"}, codes)

    def test_untracked_anchor_is_rejected(self) -> None:
        self.build_anchor("w84-baseline")

        self.assertIn("ANCHOR_GIT_UNTRACKED", self.validation_codes())

    def test_exact_gate_set_is_enforced(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.build_anchor(spec.group_id)
        self.seal_anchors()
        anchor_path = self.root / spec.anchor_path
        document = json.loads(anchor_path.read_text(encoding="utf-8"))
        document["gates"].pop()
        write_json(anchor_path, document)
        self.git("add", "--", spec.anchor_path)
        self.git("commit", "-q", "-m", "malformed gate set")

        self.assertIn("ANCHOR_GATE_SET", self.validation_codes())

    def test_wrong_ledger_prefix_is_rejected(self) -> None:
        self.write_ledgers(first_failure=[{"event": "original"}])
        self.build_anchor("w84-baseline")
        self.seal_anchors()
        self.write_ledgers(first_failure=[{"event": "replacement"}])

        codes = self.validation_codes()
        self.assertIn("ANCHOR_LEDGER_PREFIX", codes)
        self.assertIn("ANCHOR_LEDGER_RAW", codes)

    def test_append_only_ledger_growth_preserves_sealed_prefix(self) -> None:
        first = {"event": "original"}
        self.write_ledgers(first_failure=[first])
        self.build_anchor("w84-baseline")
        self.seal_anchors()
        self.write_ledgers(
            first_failure=[first, {"event": "later append"}],
        )

        self.assertEqual([], anchor.validate_evidence_anchors(self.root))

    def test_provider_attempt_event_rewrite_breaks_sealed_prefix(self) -> None:
        reservation = provider_reservation(self.product_candidate)
        initial_events = [
            provider_completion(),
            provider_attempt_finished(
                self.product_candidate,
                outcome="Failed",
            ),
        ]
        self.write_ledgers(
            provider=[reservation],
            provider_attempt_events=initial_events,
        )
        self.build_anchor("w84-baseline")
        self.seal_anchors()

        rewritten_events = [
            provider_completion(outcome="Failed"),
            provider_attempt_finished(
                self.product_candidate,
                outcome="Failed",
            ),
        ]
        self.write_ledgers(
            provider=[reservation],
            provider_attempt_events=rewritten_events,
        )

        codes = self.validation_codes()
        self.assertIn("ANCHOR_LEDGER_PREFIX", codes)
        self.assertIn("ANCHOR_LEDGER_RAW", codes)

    def test_provider_attempt_event_append_preserves_sealed_prefix(self) -> None:
        reservation = provider_reservation(self.product_candidate)
        completion = provider_completion()
        self.write_ledgers(
            provider=[reservation],
            provider_attempt_events=[completion],
        )
        self.build_anchor("w84-baseline")
        self.seal_anchors()

        self.write_ledgers(
            provider=[reservation],
            provider_attempt_events=[
                completion,
                provider_attempt_finished(
                    self.product_candidate,
                    outcome="Failed",
                ),
            ],
        )

        self.assertEqual([], anchor.validate_evidence_anchors(self.root))

    def test_provider_runtime_event_rewrite_breaks_sealed_prefix(self) -> None:
        original = provider_runtime_event(self.product_candidate)
        self.write_ledgers(provider_runtime_events=[original])
        self.build_anchor("w84-baseline")
        self.seal_anchors()
        replacement = provider_runtime_event(self.product_candidate)
        replacement["outcome"] = "Failed"
        replacement["exitCode"] = 1
        self.write_ledgers(provider_runtime_events=[replacement])

        codes = self.validation_codes()
        self.assertIn("ANCHOR_LEDGER_PREFIX", codes)
        self.assertIn("ANCHOR_LEDGER_RAW", codes)

    def test_provider_runtime_event_append_preserves_sealed_prefix(self) -> None:
        first = provider_runtime_event(self.product_candidate)
        self.write_ledgers(provider_runtime_events=[first])
        self.build_anchor("w84-baseline")
        self.seal_anchors()
        self.write_ledgers(
            provider_runtime_events=[
                first,
                provider_runtime_event(self.product_candidate, ordinal=2),
            ]
        )

        self.assertEqual([], anchor.validate_evidence_anchors(self.root))

    def test_provider_runtime_prefix_uses_runtime_event_envelope(self) -> None:
        event = provider_runtime_event(self.product_candidate)
        events = chained_runtime_events([event])
        expected = hashlib.sha256(
            anchor.canonical_json_bytes(
                {
                    "kind": "providerRuntime",
                    "path": anchor.PROVIDER_RUNTIME_JOURNAL_PATH,
                    "eventCount": 1,
                    "lastEventSha256": events[0]["eventSha256"],
                    "events": events,
                }
            )
        ).hexdigest()

        self.assertEqual(
            expected,
            anchor.ledger_prefix_sha256(
                "providerRuntime",
                anchor.PROVIDER_RUNTIME_JOURNAL_PATH,
                events,
                1,
            ),
        )

    def test_provider_ledger_event_count_and_last_hash_must_match(self) -> None:
        self.write_ledgers(
            provider=[provider_reservation(self.product_candidate)],
            provider_attempt_events=[provider_completion()],
        )
        self.build_anchor("w84-baseline")
        self.seal_anchors()
        ledger_path = self.root / anchor.PROVIDER_LEDGER_PATH
        original = ledger_path.read_bytes()

        cases = (
            (
                "attemptEventCount",
                0,
                "ANCHOR_LEDGER_ATTEMPT_EVENT_COUNT",
            ),
            (
                "lastAttemptEventSha256",
                "0" * 64,
                "ANCHOR_LEDGER_ATTEMPT_EVENT_LAST",
            ),
        )
        for field, value, expected_code in cases:
            with self.subTest(field=field):
                ledger = json.loads(original.decode("utf-8"))
                ledger[field] = value
                write_json(ledger_path, ledger)
                self.assertIn(expected_code, self.validation_codes())
                ledger_path.write_bytes(original)

    def test_provider_snapshot_event_count_and_last_hash_must_match(
        self,
    ) -> None:
        self.write_ledgers(
            provider=[provider_reservation(self.product_candidate)],
            provider_attempt_events=[provider_completion()],
        )
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.prepare_group(spec.group_id)
        snapshot_path = self.root / spec.snapshot_path
        original = snapshot_path.read_bytes()

        cases = (
            ("attemptEventCount", 0),
            ("lastAttemptEventSha256", "0" * 64),
        )
        for field, value in cases:
            with self.subTest(field=field):
                snapshot = json.loads(original.decode("utf-8"))
                snapshot["authorization"]["provider"][field] = value
                write_json(snapshot_path, snapshot)
                with self.assertRaises(anchor.AnchorBuildError) as raised:
                    anchor.build_anchor_document(
                        self.root,
                        spec.group_id,
                        self.product_candidate,
                    )
                self.assertEqual("BUILD_SNAPSHOT_LEDGER", raised.exception.code)
                snapshot_path.write_bytes(original)

    def test_parent_provider_attempt_event_prefix_cannot_regress(self) -> None:
        reservations = [provider_reservation(self.product_candidate)]
        events = [provider_completion()]
        self.write_ledgers(
            provider=reservations,
            provider_attempt_events=events,
        )
        self.build_anchor("w84-baseline")
        self.seal_anchors("seal provider event parent")

        child = anchor.GROUP_BY_ID["w85-renderer"]
        self.prepare_group(child.group_id)
        snapshot_path = self.root / child.snapshot_path
        snapshot = json.loads(snapshot_path.read_text(encoding="utf-8"))
        snapshot["authorization"]["provider"]["attemptEventCount"] = 0
        snapshot["authorization"]["provider"][
            "lastAttemptEventSha256"
        ] = None
        write_json(snapshot_path, snapshot)
        self.write_preseal_receipt(child.group_id)
        child_anchor = anchor.build_anchor_document(
            self.root,
            child.group_id,
            self.product_candidate,
        )
        write_json(self.root / child.anchor_path, child_anchor)
        self.seal_anchors("seal regressed provider event child")

        self.assertIn(
            "ANCHOR_PARENT_LEDGER_PREFIX",
            self.validation_codes(),
        )

    def test_product_candidate_must_precede_first_anchor_commit(self) -> None:
        self.git("checkout", "-q", "-b", "sibling-candidate")
        (self.root / "candidate.txt").write_text("candidate\n", encoding="utf-8")
        self.git("add", "--", "candidate.txt")
        self.git("commit", "-q", "-m", "sibling product candidate")
        sibling_candidate = self.git("rev-parse", "HEAD")
        self.git("checkout", "-q", self.main_branch)
        self.build_anchor("w84-baseline", sibling_candidate)
        self.seal_anchors()

        self.assertIn("ANCHOR_PRODUCT_ANCESTRY", self.validation_codes())

    def test_failures_do_not_echo_secret_artifact_content(self) -> None:
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        self.build_anchor(spec.group_id)
        self.seal_anchors()
        secret = "NEVER_PRINT_THIS_SECRET"
        (self.root / spec.gate_paths[0]).write_bytes(
            ("{\"password\":\"" + secret + "\",").encode("utf-8")
        )

        rendered = "\n".join(
            str(issue) for issue in anchor.validate_evidence_anchors(self.root)
        )

        self.assertNotIn(secret, rendered)
        self.assertIn("ANCHOR_ARTIFACT_JSON", rendered)

    def test_offbranch_registry_and_cas_round_trip(self) -> None:
        result, anchor_commit, registry_commit = self.prepare_w84_registry()

        self.assertEqual([], anchor.validate_anchor_registry(self.root))
        self.assertEqual(set(anchor.REGISTRY_FIELDS), set(result.registry))
        self.assertEqual(set(anchor.BUNDLE_FIELDS), set(result.bundle))
        self.assertEqual(1, result.registry["sequence"])
        self.assertEqual(anchor_commit, result.registry["anchorBinding"]["commit"])
        self.assertEqual(
            [anchor_commit],
            self.git("rev-list", "--parents", "-n", "1", registry_commit).split()[1:],
        )
        self.assertEqual(
            [],
            result.registry["checkpointDelta"],
        )
        self.assertGreater(result.bundle["entryCount"], 4)
        self.assertEqual(
            hashlib.sha256(anchor.canonical_json_bytes(result.bundle["entries"])).hexdigest(),
            result.bundle["contentRootSha256"],
        )
        for entry in result.bundle["entries"]:
            store = self.root / Path(*PurePosixPath(entry["storeObject"]).parts)
            self.assertEqual(entry["sha256"], raw_sha256(store))

    def test_registry_cli_validates_committed_round_trip(self) -> None:
        self.prepare_w84_registry()
        self.assertEqual(
            0,
            anchor.main(["validate-registry", "--repo-root", str(self.root)]),
        )

    def test_second_registry_is_direct_child_of_previous_registry(self) -> None:
        result, renderer_anchor, registry_commit = (
            self.prepare_w85_renderer_registry()
        )
        previous = result.registry["previousRegistry"]

        self.assertEqual([], anchor.validate_anchor_registry(self.root))
        self.assertEqual("w84-baseline", previous["groupId"])
        self.assertEqual(
            [previous["commit"]],
            self.git("rev-list", "--parents", "-n", "1", registry_commit).split()[1:],
        )
        self.assertEqual(
            renderer_anchor,
            self.git("rev-parse", anchor.sealed_ref_for_group("w85-renderer")),
        )

    def test_product_revert_between_candidate_and_seal_is_rejected(self) -> None:
        self.switch_to_control_branch()
        product = self.root / "product.txt"
        product.write_text("candidate product\n", encoding="utf-8")
        self.git("add", "--", "product.txt")
        self.git("commit", "-q", "-m", "product candidate")
        product_candidate = self.git("rev-parse", "HEAD")
        self.git("revert", "--no-edit", product_candidate)
        seal_commit = self.git("rev-parse", "HEAD")
        self.commit_w84_anchor_at_seal(product_candidate, seal_commit)

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            anchor.coordinate_registry_entry(
                self.root,
                "w84-baseline",
                entry_base_commit=self.product_candidate,
                product_candidate=product_candidate,
                seal_commit=seal_commit,
            )

        self.assertEqual("REGISTRY_SEAL_DELTA", raised.exception.code)

    def test_ours_seal_commit_is_rejected(self) -> None:
        self.switch_to_control_branch()
        self.git("checkout", "-q", "-b", "adversarial-side")
        (self.root / "side.txt").write_text("side product\n", encoding="utf-8")
        self.git("add", "--", "side.txt")
        self.git("commit", "-q", "-m", "side product")
        side = self.git("rev-parse", "HEAD")
        self.git("checkout", "-q", "codex/week84-92-refactor")
        self.git("merge", "--no-edit", "-s", "ours", side)
        seal_commit = self.git("rev-parse", "HEAD")
        self.commit_w84_anchor_at_seal(self.product_candidate, seal_commit)

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            anchor.coordinate_registry_entry(
                self.root,
                "w84-baseline",
                entry_base_commit=self.product_candidate,
                product_candidate=self.product_candidate,
                seal_commit=seal_commit,
            )

        self.assertEqual("REGISTRY_SEAL_DELTA", raised.exception.code)

    def test_cherrypicked_anchor_is_rejected(self) -> None:
        self.build_anchor("w84-baseline")
        self.seal_anchors("original anchor")
        original_anchor = self.git("rev-parse", "HEAD")
        self.git(
            "checkout",
            "-q",
            "-b",
            "codex/week84-92-refactor",
            self.product_candidate,
        )
        self.git("cherry-pick", "--no-commit", original_anchor)
        self.git("commit", "-q", "-m", "copied anchor commit")
        copied_anchor = self.git("rev-parse", "HEAD")
        self.git(
            "update-ref",
            anchor.sealed_ref_for_group("w84-baseline"),
            copied_anchor,
        )

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            anchor.coordinate_registry_entry(
                self.root,
                "w84-baseline",
                entry_base_commit=self.product_candidate,
                product_candidate=self.product_candidate,
                seal_commit=self.product_candidate,
            )

        self.assertEqual("REGISTRY_ANCHOR_CHERRYPICK", raised.exception.code)

    def test_moved_preservation_ref_breaks_registry(self) -> None:
        _result, _anchor_commit, _registry_commit = self.prepare_w84_registry()
        self.git(
            "update-ref",
            anchor.sealed_ref_for_group("w84-baseline"),
            self.product_candidate,
        )

        self.assertIn(
            "REGISTRY_SEALED_REF",
            anchor.issue_codes(anchor.validate_anchor_registry(self.root)),
        )

    def test_cas_mutation_breaks_registry(self) -> None:
        result, _anchor_commit, _registry_commit = self.prepare_w84_registry()
        store_path = self.root / Path(
            *PurePosixPath(result.bundle["entries"][0]["storeObject"]).parts
        )
        store_path.write_bytes(store_path.read_bytes() + b"tamper")

        self.assertIn(
            "REGISTRY_STORE_HASH",
            anchor.issue_codes(anchor.validate_anchor_registry(self.root)),
        )

    def test_ignored_artifact_mutation_breaks_registry(self) -> None:
        result, _anchor_commit, _registry_commit = self.prepare_w84_registry()
        evidence_entry = next(
            entry
            for entry in result.bundle["entries"]
            if entry["contentKind"] == "gate-evidence"
        )
        evidence_path = self.root / Path(
            *PurePosixPath(evidence_entry["canonicalPath"]).parts
        )
        evidence_path.write_bytes(evidence_path.read_bytes() + b"\n")

        self.assertIn(
            "REGISTRY_ARTIFACT_DRIFT",
            anchor.issue_codes(anchor.validate_anchor_registry(self.root)),
        )

    def test_cas_store_must_remain_ignored(self) -> None:
        self.prepare_w84_registry()
        (self.root / ".gitignore").write_text("", encoding="utf-8")

        self.assertIn(
            "REGISTRY_STORE_NOT_IGNORED",
            anchor.issue_codes(anchor.validate_anchor_registry(self.root)),
        )

    def test_bundle_paths_reject_ads_device_traversal_and_case_aliases(self) -> None:
        unsafe = (
            "artifacts/x/file.txt:secret",
            "artifacts/../secret.txt",
            "C:/secret.txt",
            "artifacts/CON/value.txt",
            "artifacts/x/trailing. ",
            "artifacts\\x\\value.txt",
        )
        for value in unsafe:
            with self.subTest(value=value):
                self.assertIsNone(anchor._canonical_artifact_path(value))

        sources = [
            "artifacts/example/Case.txt",
            "artifacts/example/case.txt",
        ]
        self.assertNotEqual(len(sources), len({value.casefold() for value in sources}))

    def test_hardlinked_evidence_is_rejected_before_cas_publish(self) -> None:
        self.switch_to_control_branch()
        self.prepare_group("w84-baseline")
        spec = anchor.GROUP_BY_ID["w84-baseline"]
        gate = json.loads((self.root / spec.gate_paths[0]).read_text("utf-8"))
        evidence = self.root / Path(*PurePosixPath(gate["evidence"][0]["path"]).parts)
        alias = evidence.with_name("hardlink-source.txt")
        alias.write_bytes(evidence.read_bytes())
        evidence.unlink()
        os.link(alias, evidence)
        self.write_preseal_receipt("w84-baseline")
        document = anchor.build_anchor_document(
            self.root, "w84-baseline", self.product_candidate
        )
        write_json(self.root / spec.anchor_path, document)
        self.seal_anchors("seal hardlinked evidence")
        anchor_commit = self.git("rev-parse", "HEAD")
        self.git(
            "update-ref",
            anchor.sealed_ref_for_group("w84-baseline"),
            anchor_commit,
        )

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            anchor.coordinate_registry_entry(
                self.root,
                "w84-baseline",
                entry_base_commit=self.product_candidate,
                product_candidate=self.product_candidate,
                seal_commit=self.product_candidate,
            )

        self.assertEqual("REGISTRY_ARTIFACT_DRIFT", raised.exception.code)

    def test_ledger_drift_after_anchor_is_rejected_before_registration(self) -> None:
        self.switch_to_control_branch()
        self.build_anchor("w84-baseline")
        self.seal_anchors("seal before ledger drift")
        anchor_commit = self.git("rev-parse", "HEAD")
        self.git(
            "update-ref",
            anchor.sealed_ref_for_group("w84-baseline"),
            anchor_commit,
        )
        ledger = self.root / anchor.FIRST_FAILURE_LEDGER_PATH
        ledger.write_bytes(ledger.read_bytes() + b" \n")

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            anchor.coordinate_registry_entry(
                self.root,
                "w84-baseline",
                entry_base_commit=self.product_candidate,
                product_candidate=self.product_candidate,
                seal_commit=self.product_candidate,
            )

        self.assertEqual("ANCHOR_LEDGER_RAW", raised.exception.code)

    def test_checkpoint_delta_allows_only_exact_single_first_add(self) -> None:
        self.switch_to_control_branch()
        product_candidate = self.git("rev-parse", "HEAD")
        decision_path = "docs_md/weekly/84_92_provider_boundary_decisions/W84-G6.json"
        decision = self.root / Path(*PurePosixPath(decision_path).parts)
        decision.parent.mkdir(parents=True, exist_ok=True)
        decision.write_text("{\"status\":\"accepted\"}\n", encoding="utf-8")
        self.git("add", "--", decision_path)
        self.git("commit", "-q", "-m", "freeze provider boundary decision")
        seal_commit = self.git("rev-parse", "HEAD")
        self.commit_w84_anchor_at_seal(product_candidate, seal_commit)

        result = anchor.coordinate_registry_entry(
            self.root,
            "w84-baseline",
            entry_base_commit=product_candidate,
            product_candidate=product_candidate,
            seal_commit=seal_commit,
            checkpoint_delta=(("provider-boundary-decision", decision_path),),
        )

        self.assertEqual(1, len(result.registry["checkpointDelta"]))
        delta = result.registry["checkpointDelta"][0]
        self.assertEqual(seal_commit, delta["firstAddCommit"])
        self.assertEqual(decision_path, delta["path"])

    def test_registry_commit_with_extra_path_is_rejected(self) -> None:
        self.switch_to_control_branch()
        self.build_anchor("w84-baseline")
        self.seal_anchors("seal W84 baseline")
        anchor_commit = self.git("rev-parse", "HEAD")
        self.git(
            "update-ref",
            anchor.sealed_ref_for_group("w84-baseline"),
            anchor_commit,
        )
        result = anchor.coordinate_registry_entry(
            self.root,
            "w84-baseline",
            entry_base_commit=self.product_candidate,
            product_candidate=self.product_candidate,
            seal_commit=self.product_candidate,
        )
        extra = self.root / "unexpected-control.txt"
        extra.write_text("not registry evidence\n", encoding="utf-8")
        self.git("add", "--", result.registry_path, result.bundle_path, extra.name)
        self.git("commit", "-q", "-m", "invalid registry commit")

        self.assertIn(
            "REGISTRY_COMMIT_DELTA",
            anchor.issue_codes(anchor.validate_anchor_registry(self.root)),
        )

    def test_registry_ledger_prefix_must_equal_anchor_and_bundle(self) -> None:
        self.switch_to_control_branch()
        self.build_anchor("w84-baseline")
        self.seal_anchors("seal W84 baseline")
        anchor_commit = self.git("rev-parse", "HEAD")
        self.git(
            "update-ref",
            anchor.sealed_ref_for_group("w84-baseline"),
            anchor_commit,
        )
        result = anchor.coordinate_registry_entry(
            self.root,
            "w84-baseline",
            entry_base_commit=self.product_candidate,
            product_candidate=self.product_candidate,
            seal_commit=self.product_candidate,
        )
        registry_path = self.root / Path(
            *PurePosixPath(result.registry_path).parts
        )
        registry = json.loads(registry_path.read_text("utf-8"))
        registry["ledgerPrefixes"]["firstFailure"]["prefixSha256"] = "0" * 64
        write_json(registry_path, registry)
        self.git("add", "--", result.registry_path, result.bundle_path)
        self.git("commit", "-q", "-m", "invalid ledger prefix registry")

        self.assertIn(
            "REGISTRY_LEDGER_PREFIX",
            anchor.issue_codes(anchor.validate_anchor_registry(self.root)),
        )

    def test_checkpoint_delta_rejects_product_change_in_same_commit(self) -> None:
        self.switch_to_control_branch()
        product_candidate = self.git("rev-parse", "HEAD")
        decision_path = "docs_md/weekly/84_92_provider_boundary_decisions/W84-G6.json"
        decision = self.root / Path(*PurePosixPath(decision_path).parts)
        decision.parent.mkdir(parents=True, exist_ok=True)
        decision.write_text("{\"status\":\"accepted\"}\n", encoding="utf-8")
        (self.root / "README.md").write_text("smuggled product change\n", encoding="utf-8")
        self.git("add", "--", decision_path, "README.md")
        self.git("commit", "-q", "-m", "mixed checkpoint and product")
        seal_commit = self.git("rev-parse", "HEAD")
        self.commit_w84_anchor_at_seal(product_candidate, seal_commit)

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            anchor.coordinate_registry_entry(
                self.root,
                "w84-baseline",
                entry_base_commit=product_candidate,
                product_candidate=product_candidate,
                seal_commit=seal_commit,
                checkpoint_delta=(("provider-boundary-decision", decision_path),),
            )

        self.assertEqual("REGISTRY_SEAL_DELTA", raised.exception.code)

    def test_checkpoint_delta_rejects_historical_delete_and_readd(self) -> None:
        self.switch_to_control_branch()
        decision_path = "docs_md/weekly/84_92_provider_boundary_decisions/W84-G6.json"
        decision = self.root / Path(*PurePosixPath(decision_path).parts)
        decision.parent.mkdir(parents=True, exist_ok=True)
        decision.write_text("{\"status\":\"old\"}\n", encoding="utf-8")
        self.git("add", "--", decision_path)
        self.git("commit", "-q", "-m", "historical decision")
        decision.unlink()
        self.git("add", "--", decision_path)
        self.git("commit", "-q", "-m", "remove historical decision")
        product_candidate = self.git("rev-parse", "HEAD")
        decision.write_text("{\"status\":\"new\"}\n", encoding="utf-8")
        self.git("add", "--", decision_path)
        self.git("commit", "-q", "-m", "readd decision")
        seal_commit = self.git("rev-parse", "HEAD")
        self.commit_w84_anchor_at_seal(product_candidate, seal_commit)

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            anchor.coordinate_registry_entry(
                self.root,
                "w84-baseline",
                entry_base_commit=product_candidate,
                product_candidate=product_candidate,
                seal_commit=seal_commit,
                checkpoint_delta=(("provider-boundary-decision", decision_path),),
            )

        self.assertEqual("REGISTRY_CHECKPOINT_FIRST_ADD", raised.exception.code)

    def test_anchor_commit_with_extra_path_is_rejected(self) -> None:
        self.switch_to_control_branch()
        self.prepare_group("w84-baseline")
        self.write_preseal_receipt("w84-baseline")
        document = anchor.build_anchor_document(
            self.root, "w84-baseline", self.product_candidate
        )
        write_json(
            self.root / anchor.GROUP_BY_ID["w84-baseline"].anchor_path,
            document,
        )
        extra = self.root / "smuggled-at-anchor.txt"
        extra.write_text("extra\n", encoding="utf-8")
        self.git(
            "add",
            "--",
            anchor.GROUP_BY_ID["w84-baseline"].anchor_path,
            extra.name,
        )
        self.git("commit", "-q", "-m", "invalid anchor delta")
        anchor_commit = self.git("rev-parse", "HEAD")
        self.git(
            "update-ref",
            anchor.sealed_ref_for_group("w84-baseline"),
            anchor_commit,
        )

        with self.assertRaises(anchor.AnchorBuildError) as raised:
            anchor.coordinate_registry_entry(
                self.root,
                "w84-baseline",
                entry_base_commit=self.product_candidate,
                product_candidate=self.product_candidate,
                seal_commit=self.product_candidate,
            )

        self.assertEqual("REGISTRY_ANCHOR_DELTA", raised.exception.code)

    def test_ours_merge_fails_contribution_check(self) -> None:
        base = self.git("rev-parse", "HEAD")
        self.git("checkout", "-q", "-b", "merge-side")
        (self.root / "lane-product.txt").write_text("lane\n", encoding="utf-8")
        self.git("add", "--", "lane-product.txt")
        self.git("commit", "-q", "-m", "lane contribution")
        side = self.git("rev-parse", "HEAD")
        self.git("checkout", "-q", self.main_branch)
        self.git("merge", "--no-edit", "-s", "ours", side)
        ours_merge = self.git("rev-parse", "HEAD")

        self.assertFalse(
            anchor._merge_preserves_contributions(
                self.root,
                ours_merge,
                base,
                side,
            )
        )

    def test_merge_only_extra_path_fails_contribution_check(self) -> None:
        base = self.git("rev-parse", "HEAD")
        self.git("checkout", "-q", "-b", "merge-extra-side")
        (self.root / "side-product.txt").write_text("side\n", encoding="utf-8")
        self.git("add", "--", "side-product.txt")
        self.git("commit", "-q", "-m", "side contribution")
        side = self.git("rev-parse", "HEAD")
        self.git("checkout", "-q", self.main_branch)
        (self.root / "first-product.txt").write_text("first\n", encoding="utf-8")
        self.git("add", "--", "first-product.txt")
        self.git("commit", "-q", "-m", "first contribution")
        first = self.git("rev-parse", "HEAD")
        self.git("merge", "--no-ff", "--no-commit", side)
        (self.root / "merge-smuggled.txt").write_text("extra\n", encoding="utf-8")
        self.git("add", "--", "merge-smuggled.txt")
        self.git("commit", "-q", "-m", "merge with extra path")
        merge_commit = self.git("rev-parse", "HEAD")

        self.assertFalse(
            anchor._merge_preserves_contributions(
                self.root,
                merge_commit,
                first,
                side,
            )
        )

    def test_w90_entry_requires_exact_two_stage_contributing_merges(self) -> None:
        base = self.git("rev-parse", "HEAD")
        self.git("checkout", "-q", "-b", "w89-renderer-fixture")
        (self.root / "renderer-product.txt").write_text("renderer\n", encoding="utf-8")
        self.git("add", "--", "renderer-product.txt")
        self.git("commit", "-q", "-m", "renderer W89 tip")
        renderer_tip = self.git("rev-parse", "HEAD")
        self.git("checkout", "-q", "-b", "w89-cli-fixture", base)
        (self.root / "cli-product.txt").write_text("cli\n", encoding="utf-8")
        self.git("add", "--", "cli-product.txt")
        self.git("commit", "-q", "-m", "CLI W89 tip")
        cli_tip = self.git("rev-parse", "HEAD")
        self.git(
            "update-ref",
            anchor.sealed_ref_for_group("w89-renderer"),
            renderer_tip,
        )
        self.git(
            "update-ref",
            anchor.sealed_ref_for_group("w89-cli"),
            cli_tip,
        )

        self.git("checkout", "-q", "-b", "valid-integration", base)
        self.git("merge", "--no-ff", "--no-edit", renderer_tip)
        self.git("merge", "--no-ff", "--no-edit", cli_tip)
        valid_entry = self.git("rev-parse", "HEAD")
        anchor._validate_entry_base(
            self.root,
            anchor.GROUP_BY_ID["w90-integration"],
            valid_entry,
            valid_entry,
            {"w89-cli": base},
        )

        self.git("checkout", "-q", "-b", "ours-integration", base)
        self.git("merge", "--no-edit", "-s", "ours", renderer_tip)
        self.git("merge", "--no-edit", "-s", "ours", cli_tip)
        invalid_entry = self.git("rev-parse", "HEAD")
        with self.assertRaises(anchor.AnchorBuildError) as raised:
            anchor._validate_entry_base(
                self.root,
                anchor.GROUP_BY_ID["w90-integration"],
                invalid_entry,
                invalid_entry,
                {"w89-cli": base},
            )
        self.assertEqual("REGISTRY_W90_CONTRIBUTION", raised.exception.code)


if __name__ == "__main__":
    unittest.main()
