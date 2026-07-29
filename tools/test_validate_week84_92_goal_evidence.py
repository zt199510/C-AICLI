from __future__ import annotations

from copy import deepcopy
from contextlib import ExitStack, redirect_stdout
import hashlib
import inspect
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch


SCRIPT = Path(__file__).with_name("validate-week84-92-goal-evidence.py")
SPEC = importlib.util.spec_from_file_location("week84_92_goal_validator", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
validator = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = validator
SPEC.loader.exec_module(validator)


ARTIFACT_DIRS = validator.GOAL_ARTIFACT_DIR_BY_GROUP
PRODUCT_CANDIDATE = "a" * 40
PROVIDER_CONSUMPTION = {
    "W84-G6": 3,
    "W84-G7": 30,
    "W84-G8": 1,
    "W92-G7": 34,
}
PROVIDER_GATE_TIMES = {
    "W84-G6": {
        "startedAt": "2026-07-28T00:00:00Z",
        "reservedAt": "2026-07-28T00:00:01Z",
        "completedAt": "2026-07-28T00:00:02Z",
        "attemptFinishedAt": "2026-07-28T00:00:03Z",
        "finishedAt": "2026-07-28T00:00:04Z",
    },
    "W84-G7": {
        "startedAt": "2026-07-28T00:00:10Z",
        "reservedAt": "2026-07-28T00:00:11Z",
        "completedAt": "2026-07-28T00:00:12Z",
        "attemptFinishedAt": "2026-07-28T00:00:13Z",
        "finishedAt": "2026-07-28T00:00:14Z",
    },
    "W84-G8": {
        "startedAt": "2026-07-28T00:00:20Z",
        "reservedAt": "2026-07-28T00:00:21Z",
        "completedAt": "2026-07-28T00:00:23Z",
        "attemptFinishedAt": "2026-07-28T00:00:24Z",
        "finishedAt": "2026-07-28T00:00:25Z",
    },
    "W92-G7": {
        "startedAt": "2026-07-28T00:00:30Z",
        "reservedAt": "2026-07-28T00:00:31Z",
        "completedAt": "2026-07-28T00:00:33Z",
        "attemptFinishedAt": "2026-07-28T00:00:34Z",
        "finishedAt": "2026-07-28T00:00:35Z",
    },
}


def fractional_timestamp(value: str, microseconds: int) -> str:
    return f"{value[:-1]}.{microseconds:06d}Z"


def clean_gate_cleanup() -> dict:
    return {
        "status": "Passed",
        "ownedProcessesRemaining": 0,
        "ownedTempPathsRemaining": 0,
        "configMutationsRemaining": 0,
        "unownedProcessTouched": False,
        "unownedPathTouched": False,
        "countsBalanced": True,
        "evidenceRefs": ["cleanup.json"],
    }


def clean_handoff_cleanup() -> dict:
    return {
        "status": "Passed",
        "trackedResidueCount": 0,
        "untrackedResidueCount": 0,
        "tempArtifactCount": 0,
        "processResidueCount": 0,
        "countsBalanced": True,
        "evidenceRefs": ["cleanup.json"],
    }


def acceptance_records() -> list[dict]:
    return [
        {"acceptanceId": "W86-USER-VISUAL", "status": "Passed"},
        {"acceptanceId": "W89-USER-VISUAL", "status": "Passed"},
        {"acceptanceId": "W92-USER-VISUAL", "status": "Passed"},
    ]


def write_record(week: int, visible: bool) -> dict:
    gate_id = validator.CONTROLLED_WRITE_GATES[week]
    return {
        "week": week,
        "gateId": gate_id,
        "status": "Passed" if visible else "NotRun",
        "executionCount": 1 if visible else 0,
        "shapeVerified": visible,
        "nonWritePreconditionsPassed": visible,
        "evidenceRefs": [f"{gate_id}-write.json"] if visible else [],
    }


def provider_prefix_projection(ledger_data: dict, sequence_after: int) -> dict:
    reservation_by_id = {
        entry["reservationId"]: entry for entry in ledger_data["entries"]
    }
    problems = validator.Problems()
    event_count = validator._provider_attempt_event_prefix_count(
        ledger_data["attemptEvents"],
        reservation_by_id,
        sequence_after,
        "test-fixture",
        problems,
    )
    if problems.items:
        raise AssertionError(problems.items)
    last_event_hash = (
        ledger_data["attemptEvents"][event_count - 1][
            "attemptEventSha256"
        ]
        if event_count
        else None
    )
    return {
        "attemptEventCount": event_count,
        "lastAttemptEventSha256": last_event_hash,
        "reservationPrefixSha256": validator.provider_reservation_prefix_sha256(
            ledger_data["entries"], sequence_after
        ),
        "attemptEventPrefixSha256": validator.provider_attempt_event_prefix_sha256(
            ledger_data["attemptEvents"], event_count
        ),
        "combinedPrefixSha256": validator.provider_combined_prefix_sha256(
            ledger_data["entries"],
            sequence_after,
            ledger_data["attemptEvents"],
            event_count,
        ),
    }


def make_fixture() -> dict:
    contract = validator.Contract(
        validator.CANONICAL_GATE_IDS,
        validator._canonical_groups_without_patterns(),
    )
    provider_accounting = {}
    ledger_entries = []
    provider_cursor = 0
    for gate_id in validator.CANONICAL_GATE_IDS:
        consumed = PROVIDER_CONSUMPTION.get(gate_id, 0)
        provider_accounting[gate_id] = (
            provider_cursor,
            consumed,
            provider_cursor + consumed,
        )
        expected_phases = validator._expected_provider_phases(gate_id)
        for offset in range(consumed):
            phase = expected_phases[offset]
            if phase == "provider-resource":
                run_id = f"{gate_id}-resource-continuous-run"
            else:
                run_id = f"{gate_id}-{phase}-run"
            entry = {
                "sequence": len(ledger_entries) + 1,
                "eventType": "TurnReserved",
                "reservationId": f"{gate_id}-reservation-{offset + 1}",
                "gateId": gate_id,
                "phase": phase,
                "productCandidate": PRODUCT_CANDIDATE,
                "attemptId": f"{gate_id}-attempt-1",
                "runId": run_id,
                "reservedAt": PROVIDER_GATE_TIMES[gate_id]["reservedAt"],
                "previousEntrySha256": (
                    ledger_entries[-1]["entrySha256"] if ledger_entries else None
                ),
            }
            entry["entrySha256"] = validator.provider_entry_sha256(entry)
            ledger_entries.append(entry)
        provider_cursor += consumed
    attempt_events = []
    previous_event_hash = None
    for gate_id in validator.CANONICAL_GATE_IDS:
        gate_entries = [entry for entry in ledger_entries if entry["gateId"] == gate_id]
        if not gate_entries:
            continue
        for entry in gate_entries:
            event = {
                "attemptEventSequence": len(attempt_events) + 1,
                "eventType": "TurnCompleted",
                "reservationId": entry["reservationId"],
                "outcome": "Succeeded",
                "completedAt": PROVIDER_GATE_TIMES[gate_id]["completedAt"],
                "previousAttemptEventSha256": previous_event_hash,
            }
            event["attemptEventSha256"] = validator.provider_attempt_event_sha256(event)
            previous_event_hash = event["attemptEventSha256"]
            attempt_events.append(event)
        event = {
            "attemptEventSequence": len(attempt_events) + 1,
            "eventType": "AttemptFinished",
            "gateId": gate_id,
            "productCandidate": PRODUCT_CANDIDATE,
            "attemptId": f"{gate_id}-attempt-1",
            "outcome": "Passed",
            "reservationSequenceStart": gate_entries[0]["sequence"],
            "reservationSequenceEnd": gate_entries[-1]["sequence"],
            "finishedAt": PROVIDER_GATE_TIMES[gate_id]["attemptFinishedAt"],
            "previousAttemptEventSha256": previous_event_hash,
        }
        event["attemptEventSha256"] = validator.provider_attempt_event_sha256(event)
        previous_event_hash = event["attemptEventSha256"]
        attempt_events.append(event)
    ledger_data = {
        "schemaVersion": validator.SCHEMA_VERSION,
        "journalVersion": validator.PROVIDER_JOURNAL_VERSION,
        "goalId": validator.GOAL_ID,
        "maxTurns": 120,
        "usedTurns": provider_cursor,
        "remainingTurns": 120 - provider_cursor,
        "ledgerSequence": provider_cursor,
        "entries": ledger_entries,
        "attemptEventCount": len(attempt_events),
        "lastAttemptEventSha256": previous_event_hash,
        "attemptEvents": attempt_events,
    }
    ledger = validator.document_from_data(validator.LEDGER_PATH, ledger_data)

    gates = []
    gates_by_id = {}
    for group in contract.groups:
        artifact_dir = ARTIFACT_DIRS[group.key]
        for gate_id in group.gate_ids:
            result_path = f"{artifact_dir}/gates/{gate_id}.json"
            write_used = gate_id in validator.CONTROLLED_WRITE_GATES.values()
            receipt_names = {
                "W84-G6": ["provider-readonly.json", "provider-recovery.json"],
                "W84-G7": [
                    f"provider-resource-profile-{index}.json"
                    for index in range(1, 6)
                ],
                "W84-G8": ["controlled-write.json"],
                "W92-G7": [
                    "provider-readonly.json",
                    "provider-recovery.json",
                    *(f"provider-resource-profile-{index}.json" for index in range(1, 6)),
                    "controlled-write.json",
                ],
                "W86-R7": ["user-visual-confirmation.json", "screenshot-manifest.json"],
                "W89-R5": ["visual-acceptance.json", "screenshot-manifest.json"],
                "W91-G2": [
                    "automated-accessibility.json",
                    "narrator-manual.json",
                    "operator-ux-attestation.json",
                ],
                "W92-G9": ["visual-acceptance.json", "screenshot-manifest.json"],
            }.get(gate_id, [f"{gate_id}-proof.json"])
            if gate_id in validator.PROVIDER_LEDGER_BINDING_GATE_IDS:
                receipt_names = [
                    *receipt_names,
                    validator.PROVIDER_LEDGER_BINDING_BASENAME,
                ]
            evidence = [
                {
                    "evidenceId": f"{gate_id}-evidence-{index}",
                    "kind": "json",
                    "path": (
                        f"{artifact_dir}/provider-ledger-bindings/"
                        f"{gate_id}/{name}"
                        if name
                        == validator.PROVIDER_LEDGER_BINDING_BASENAME
                        else f"{artifact_dir}/{name}"
                    ),
                    "sha256": "b" * 64,
                    "redacted": True,
                    "preservesFirstFailure": False,
                }
                for index, name in enumerate(receipt_names, start=1)
            ]
            primary_evidence_id = evidence[0]["evidenceId"]
            controlled_write_evidence_id = next(
                (
                    item["evidenceId"]
                    for item in evidence
                    if Path(item["path"]).name == "controlled-write.json"
                ),
                primary_evidence_id,
            )
            before, consumed, after = provider_accounting[gate_id]
            prefix_projection = provider_prefix_projection(ledger_data, after)
            scopes = ["credential-free"]
            if gate_id == "W84-G6":
                scopes = ["provider-read-only", "provider-recovery"]
            elif gate_id == "W84-G7":
                scopes = ["provider-resource"]
            elif gate_id == "W84-G8":
                scopes = ["controlled-write"]
            elif gate_id == "W92-G7":
                scopes = [
                    "provider-read-only",
                    "provider-recovery",
                    "provider-resource",
                    "controlled-write",
                ]
            elif gate_id == "W91-G2":
                scopes = ["operator-narrator-manual-ux"]
            boundary_decision = None
            package_launch_receipts = []
            if gate_id in validator.PROVIDER_LAUNCH_LAYOUT:
                boundary_path = validator.PROVIDER_BOUNDARY_DECISION_PATHS[gate_id]
                boundary_gate_id = (
                    "W84-G6" if gate_id.startswith("W84-") else "W92-G7"
                )
                package_tree_root = "e" * 64
                boundary_decision = {
                    "path": boundary_path,
                    "sha256": "d" * 64,
                    "firstAddCommit": "c" * 40,
                    "decisionId": f"boundary-{boundary_gate_id}",
                    "mode": "cooperative-candidate",
                    "packageTreeRootSha256": package_tree_root,
                }
                for phase, profile, _batch_size in validator.PROVIDER_LAUNCH_LAYOUT[
                    gate_id
                ]:
                    suffix = (
                        f"{phase}-profile-{profile}"
                        if profile is not None
                        else phase
                    )
                    package_launch_receipts.append(
                        {
                            "path": (
                                f"{artifact_dir}/gate-evidence/{gate_id}/"
                                f"package-launch-{suffix}.json"
                            ),
                            "sha256": hashlib.sha256(
                                f"{gate_id}:{suffix}".encode("utf-8")
                            ).hexdigest(),
                            "phase": phase,
                            "profileOrdinal": profile,
                            "batchId": f"batch-{gate_id.lower()}-{suffix}",
                            "packageTreeRootSha256": package_tree_root,
                        }
                    )
            data = {
                "schemaVersion": validator.SCHEMA_VERSION,
                "registryVersion": validator.REGISTRY_VERSION,
                "goalId": validator.GOAL_ID,
                "checkpoint": group.checkpoint,
                "week": group.week,
                "lane": group.lane,
                "gateId": gate_id,
                "commandControlBinding": None,
                "resultPath": result_path,
                "resultPathGateIdMatched": True,
                "requiredGate": True,
                "status": "Passed",
                "startedAt": PROVIDER_GATE_TIMES.get(
                    gate_id, {"startedAt": "2026-07-28T00:00:00Z"}
                )["startedAt"],
                "finishedAt": PROVIDER_GATE_TIMES.get(
                    gate_id, {"finishedAt": "2026-07-28T00:00:01Z"}
                )["finishedAt"],
                "identity": {
                    "sourceHead": PRODUCT_CANDIDATE,
                    "productCandidate": PRODUCT_CANDIDATE,
                    "checkpointCommit": PRODUCT_CANDIDATE,
                },
                "commands": [
                    {
                        "commandId": f"{gate_id}-command",
                        "redactedCommand": f"run {gate_id}",
                        "status": "Passed",
                        "startedAt": "2026-07-28T00:00:00Z",
                        "finishedAt": "2026-07-28T00:00:01Z",
                        "exitCode": 0,
                        "testCountSource": True,
                        "evidenceRefs": [primary_evidence_id],
                    }
                ],
                "commandCounts": {
                    "total": 1,
                    "passed": 1,
                    "failed": 0,
                    "notRun": 0,
                    "notApplicable": 0,
                    "countsBalanced": True,
                },
                "testCounts": {
                    "discovered": 1,
                    "passed": 1,
                    "failed": 0,
                    "skipped": 0,
                    "notRun": 0,
                    "notApplicable": 0,
                    "countsBalanced": True,
                },
                "acceptanceAssertions": [
                    {
                        "assertionId": f"{gate_id}-assertion",
                        "status": "Passed",
                        "evidenceRefs": [primary_evidence_id],
                    }
                ],
                "firstFailure": None,
                "cleanup": clean_gate_cleanup(),
                "evidence": evidence,
                "openIssues": {"p0": 0, "p1": 0, "other": 0},
                "authorization": {
                    "scopesUsed": scopes,
                    "providerTurnsBefore": before,
                    "providerTurnsConsumed": consumed,
                    "providerTurnsAfter": after,
                    "providerSuccessfulAttemptId": (
                        f"{gate_id}-attempt-1" if consumed else None
                    ),
                    "providerTurnBudget": 120,
                    "providerLedgerPath": validator.LEDGER_PATH,
                    "providerLedgerPrefixSha256": prefix_projection[
                        "combinedPrefixSha256"
                    ],
                    "providerSequenceBefore": before,
                    "providerSequenceAfter": after,
                    "providerTurnLedgerBalanced": True,
                    "controlledWriteUsed": write_used,
                    "controlledWriteShapeVerified": write_used,
                    "controlledWriteExecutionCount": 1 if write_used else 0,
                    "controlledWriteNonWritePreconditionsPassed": write_used,
                    "controlledWriteEvidenceRefs": [controlled_write_evidence_id] if write_used else [],
                    "providerBoundaryDecision": boundary_decision,
                    "packageLaunchReceipts": package_launch_receipts,
                },
            }
            data["cleanup"]["evidenceRefs"] = [primary_evidence_id]
            document = validator.document_from_data(result_path, data)
            gates.append(document)
            gates_by_id[gate_id] = document

    handoffs = []
    for group in contract.groups:
        final = group.key == (92, "acceptance")
        path = validator.CANONICAL_HANDOFF_BY_GROUP[group.key]
        parent_handoffs = []
        for parent_group in validator.HANDOFF_PARENT_GROUPS[group.key]:
            parent_path = validator.CANONICAL_HANDOFF_BY_GROUP[parent_group]
            parent_document = next(
                item for item in handoffs if item.relative_path == parent_path
            )
            parent_handoffs.append(
                {
                    "week": parent_group[0],
                    "lane": parent_group[1],
                    "path": parent_path,
                    "sha256": parent_document.sha256,
                    "productCandidate": parent_document.data["identity"][
                        "productCandidate"
                    ],
                }
            )
        group_used = max(provider_accounting[gate_id][2] for gate_id in group.gate_ids)
        refs = []
        for gate_id in group.gate_ids:
            gate = gates_by_id[gate_id].data
            refs.append(
                {
                    "gateId": gate_id,
                    "required": True,
                    "status": "Passed",
                    "resultPath": gate["resultPath"],
                    "resultSha256": gates_by_id[gate_id].sha256,
                    "firstFailureId": None,
                }
            )
        write_this_week = group.week in validator.CONTROLLED_WRITE_GATES
        data = {
            "schemaVersion": validator.SCHEMA_VERSION,
            "registryVersion": validator.REGISTRY_VERSION,
            "goalId": validator.GOAL_ID,
            "checkpoint": group.checkpoint,
            "week": group.week,
            "lane": group.lane,
            "decision": "GoalComplete" if final else "ReadyForNextCheckpoint",
            "finalDecision": "RefactorAccepted" if final else None,
            "gateResults": refs,
            "gateCounts": {
                "total": len(refs),
                "passed": len(refs),
                "failed": 0,
                "notRun": 0,
                "notApplicable": 0,
                "countsBalanced": True,
            },
            "requiredGatesSatisfied": True,
            "firstFailures": [],
            "parentHandoffs": parent_handoffs,
            "identity": {
                "branch": "codex/week84-92-test",
                "sourceHead": PRODUCT_CANDIDATE,
                "productCandidate": PRODUCT_CANDIDATE,
                "checkpointCommit": PRODUCT_CANDIDATE,
            },
            "gitCheckpoint": {
                "branch": "codex/week84-92-test",
                "checkpointCommit": PRODUCT_CANDIDATE,
                "localMergeCommit": None,
            },
            "cleanup": clean_handoff_cleanup(),
            "openIssues": {"p0": 0, "p1": 0, "other": 0},
            "userAcceptances": acceptance_records(),
            "authorizationBudget": {
                "providerTurnBudget": 120,
                "providerTurnsUsed": group_used,
                "providerTurnsRemaining": 120 - group_used,
                "providerTurnLedgerBalanced": True,
                "controlledWriteUsedThisWeek": write_this_week,
                "controlledWriteShapeVerified": write_this_week,
                "controlledWriteExecutionCountThisWeek": 1 if write_this_week else 0,
                "controlledWriteNonWritePreconditionsPassed": write_this_week,
                "controlledWriteEvidenceRefs": [f"W{group.week}-write.json"] if write_this_week else [],
                "controlledWriteHistory": {
                    "w84": write_record(84, True),
                    "w92": write_record(92, group.week >= 92),
                },
            },
            "goalControlBinding": {
                "providerLedgerSha256": ledger.sha256,
                "providerLedgerSequence": group_used,
            },
        }
        handoffs.append(validator.document_from_data(path, data))

    week84_canonical = next(
        item
        for item in handoffs
        if item.relative_path == validator.W84_CANONICAL_HANDOFF_PATH
    )
    handoffs.append(
        validator.document_from_data(
            validator.W84_COMPAT_HANDOFF_PATH,
            deepcopy(week84_canonical.data),
        )
    )

    final_handoff = next(
        item for item in handoffs if item.relative_path == validator.FINAL_HANDOFF_PATH
    )
    goal_data = {
        "schemaVersion": validator.SCHEMA_VERSION,
        "goalId": validator.GOAL_ID,
        "status": "Complete",
        "identity": {
            "sourceHead": PRODUCT_CANDIDATE,
            "productCandidate": PRODUCT_CANDIDATE,
            "checkpointCommit": PRODUCT_CANDIDATE,
        },
        "gateRegistry": list(validator.CANONICAL_GATE_IDS),
        "gateRollup": {
            "total": 104,
            "passed": 104,
            "failed": 0,
            "notRun": 0,
            "notApplicable": 0,
            "countsBalanced": True,
        },
        "commandRollup": {
            "total": 104,
            "passed": 104,
            "failed": 0,
            "notRun": 0,
            "notApplicable": 0,
            "countsBalanced": True,
        },
        "testRollup": {
            "discovered": 104,
            "passed": 104,
            "failed": 0,
            "skipped": 0,
            "notRun": 0,
            "notApplicable": 0,
            "countsBalanced": True,
        },
        "cleanup": clean_gate_cleanup(),
        "openIssues": {"p0": 0, "p1": 0, "other": 0},
        "manualAcceptances": {
            "w86Visual": "Passed",
            "w89Visual": "Passed",
            "w91NarratorManualUx": "Passed",
            "w92Visual": "Passed",
        },
        "finalDecision": "RefactorAccepted",
        "openFirstFailure": None,
        "finalHandoff": {
            "path": validator.FINAL_HANDOFF_PATH,
            "sha256": final_handoff.sha256,
            "decision": "GoalComplete",
            "finalDecision": "RefactorAccepted",
        },
        "authorization": {
            "provider": {
                "maxTurns": 120,
                "secretDisposition": validator.PROVIDER_SECRET_DISPOSITION,
                "usedTurns": provider_cursor,
                "remainingTurns": 120 - provider_cursor,
                "turnLedgerBalanced": True,
                "ledgerPath": validator.LEDGER_PATH,
                "ledgerSha256": ledger.sha256,
                "ledgerSequence": provider_cursor,
                "attemptEventCount": len(attempt_events),
                "lastAttemptEventSha256": previous_event_hash,
            },
            "controlledWrite": {
                "executions": {
                    "w84": write_record(84, True),
                    "w92": write_record(92, True),
                }
            },
        },
        "execution": {
            "weekStates": {
                f"W{week}": {"entryGate": "Passed", "exitGate": "Passed"}
                for week in range(84, 93)
            }
        },
    }
    goal = validator.document_from_data(
        "artifacts/week84-92-goal-control/goal-state.json", goal_data
    )
    return {
        "contract": contract,
        "ledger": ledger,
        "gates": gates,
        "handoffs": handoffs,
        "goal": goal,
    }


def validate(fixture: dict) -> list[str]:
    return validator.validate_semantics(
        fixture["goal"],
        fixture["gates"],
        fixture["handoffs"],
        fixture["ledger"],
        fixture["contract"],
        require_complete=True,
    )


def fixture_gate(fixture: dict, gate_id: str) -> validator.Document:
    return next(item for item in fixture["gates"] if item.data["gateId"] == gate_id)


def fixture_handoff(
    fixture: dict, week: int, lane: str
) -> validator.Document:
    return next(
        item
        for item in fixture["handoffs"]
        if item.data["week"] == week
        and item.data["lane"] == lane
        and item.relative_path != validator.W84_COMPAT_HANDOFF_PATH
    )


def requirements_manifest_document() -> validator.Document:
    path = SCRIPT.parents[1] / validator.GATE_REQUIREMENTS_PATH
    raw = path.read_bytes()
    return validator.Document(
        path=path,
        relative_path=validator.GATE_REQUIREMENTS_PATH,
        data=json.loads(raw.decode("utf-8")),
        sha256=hashlib.sha256(raw).hexdigest(),
    )


def _trusted_fixture_git_run(
    root: Path,
    arguments: tuple[str, ...],
    *,
    environment_overrides: dict[str, str] | None = None,
) -> subprocess.CompletedProcess[bytes]:
    """Run fixture Git through the pinned core, never PATH or the test cwd."""

    repository = root.resolve()
    executable, before_identity = (
        validator.trusted_executor._trusted_git_identity(repository)
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
    environment = validator.trusted_executor._git_environment(executable)
    if environment_overrides:
        environment.update(environment_overrides)
    completed = subprocess.run(
        command,
        cwd=repository,
        env=environment,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        shell=False,
        check=True,
        timeout=30,
    )
    after_executable, after_identity = (
        validator.trusted_executor._trusted_git_identity(repository)
    )
    if executable != after_executable or before_identity != after_identity:
        raise RuntimeError("trusted fixture Git identity changed")
    return completed


def _git(root: Path, *arguments: str) -> str:
    completed = _trusted_fixture_git_run(root, arguments)
    return completed.stdout.decode("utf-8").strip()


def prepare_frozen_runner(root: Path) -> str:
    if not (root / ".git").exists():
        _git(root, "init", "--quiet")
    runner_path = root / validator.TEST_RUNNER_PATH
    if not runner_path.exists():
        runner_path.parent.mkdir(parents=True, exist_ok=True)
        attributes_path = runner_path.parent / ".gitattributes"
        attributes_path.write_text(
            f"{runner_path.name} -text\n", encoding="utf-8"
        )
        manifest_path = root / validator.GATE_REQUIREMENTS_PATH
        manifest_path.parent.mkdir(parents=True, exist_ok=True)
        weekly_attributes = manifest_path.parent / ".gitattributes"
        weekly_attributes.write_text(
            f"{manifest_path.name} -text\n",
            encoding="utf-8",
        )
        runner_path.write_bytes(
            (SCRIPT.parents[1] / validator.TEST_RUNNER_PATH).read_bytes()
        )
        manifest_path.write_bytes(
            (SCRIPT.parents[1] / validator.GATE_REQUIREMENTS_PATH).read_bytes()
        )
        _git(
            root,
            "add",
            validator.TEST_RUNNER_PATH,
            "tools/.gitattributes",
            validator.GATE_REQUIREMENTS_PATH,
            "docs_md/weekly/.gitattributes",
        )
        _git(
            root,
            "-c",
            "user.name=Goal Validator",
            "-c",
            "user.email=validator@example.invalid",
            "commit",
            "--quiet",
            "-m",
            "add frozen executor",
        )
    return hashlib.sha256(runner_path.read_bytes()).hexdigest()


def fixture_bootstrap_source_binding(root: Path, relative_path: str) -> dict:
    raw = (root / relative_path).read_bytes()
    control_revision = _git(
        root,
        "log",
        "--diff-filter=A",
        "--format=%H",
        "--",
        relative_path,
    )
    return {
        "path": relative_path,
        "sha256": hashlib.sha256(raw).hexdigest(),
        "gitBlobSha": _git(
            root,
            "rev-parse",
            f"{control_revision}:{relative_path}",
        ),
        "controlRevision": control_revision,
    }


def fixture_trusted_test_policy(
    manifest_document: validator.Document,
    requirement: dict,
) -> dict:
    policy = deepcopy(
        manifest_document.data.get("frozenPolicies", {}).get(
            "trusted-test-command", {}
        )
    )
    if policy:
        return policy
    return {
        "policyId": "week84-92-semantic-unittest-v1",
        "commandId": requirement["requiredCommandIds"][0],
        "executableRole": "python-current",
        "arguments": [
            "-m",
            "unittest",
            "tools.test_validate_week84_92_goal_evidence",
        ],
        "redactedInvocation": (
            "python -m unittest tools.test_validate_week84_92_goal_evidence"
        ),
        "countsSource": "python-unittest-output",
        "shellAllowed": False,
    }


def fixture_trusted_product_policy(
    manifest_document: validator.Document,
) -> dict:
    policy = deepcopy(
        manifest_document.data.get("frozenPolicies", {}).get(
            "trusted-product-command", {}
        )
    )
    if policy:
        return policy
    return {
        "policyId": validator.TRUSTED_PRODUCT_POLICY_ID,
        "executableRole": "python-current",
        "scriptRoot": validator.TRUSTED_PRODUCT_SCRIPT_ROOT,
        "argumentsTemplate": ["-B", "-X", "utf8", "{scriptPath}"],
        "redactedInvocationTemplate": (
            "python-current -B -X utf8 "
            "<trusted-product-command:{commandId}>"
        ),
        "shellAllowed": False,
    }


def prepare_product_scripts(
    root: Path,
    command_ids: list[str],
) -> tuple[str, dict[str, dict[str, str]]]:
    script_root = root / validator.TRUSTED_PRODUCT_SCRIPT_ROOT
    script_root.mkdir(parents=True, exist_ok=True)
    attributes = script_root / ".gitattributes"
    attributes.write_text("*.py -text\n", encoding="utf-8")
    relative_paths = [
        f"{validator.TRUSTED_PRODUCT_SCRIPT_ROOT}/{command_id}.py"
        for command_id in command_ids
    ]
    for command_id, relative_path in zip(command_ids, relative_paths):
        script = root / relative_path
        script.write_text(
            "from __future__ import annotations\n"
            f"COMMAND_ID = {command_id!r}\n",
            encoding="utf-8",
        )
    _git(
        root,
        "add",
        f"{validator.TRUSTED_PRODUCT_SCRIPT_ROOT}/.gitattributes",
        *relative_paths,
    )
    _git(
        root,
        "-c",
        "user.name=Goal Validator",
        "-c",
        "user.email=validator@example.invalid",
        "commit",
        "--quiet",
        "-m",
        "add derived product commands",
    )
    candidate = _git(root, "rev-parse", "HEAD")
    bindings: dict[str, dict[str, str]] = {}
    for command_id, relative_path in zip(command_ids, relative_paths):
        raw = (root / relative_path).read_bytes()
        bindings[command_id] = {
            "path": relative_path,
            "sha256": hashlib.sha256(raw).hexdigest(),
            "gitBlobSha": _git(
                root, "rev-parse", f"{candidate}:{relative_path}"
            ),
        }
    return candidate, bindings


def prepare_requirements_gate(
    root: Path,
    gate_id: str = "W84-G0",
) -> tuple[dict, validator.Document, dict, validator.Document]:
    fixture = make_fixture()
    manifest_document = requirements_manifest_document()
    requirement = next(
        item
        for item in manifest_document.data["gates"]
        if item["gateId"] == gate_id
    )
    document = fixture_gate(fixture, gate_id)
    gate = document.data
    gate["requirementsBinding"] = {
        "path": validator.GATE_REQUIREMENTS_PATH,
        "manifestSha256": manifest_document.sha256,
        "registryVersion": validator.GATE_REQUIREMENTS_VERSION,
        "gateRequirementSha256": hashlib.sha256(
            validator._json_bytes(requirement)
        ).hexdigest(),
    }

    evidence_names = list(requirement["requiredEvidenceBasenames"])
    evidence_kinds = list(requirement["requiredEvidenceKinds"])
    trusted_test_policy = fixture_trusted_test_policy(
        manifest_document, requirement
    )
    runner_sha = prepare_frozen_runner(root)
    manifest_document.data["frozenPolicies"]["trusted-executor"][
        "sourceSha256"
    ] = runner_sha
    command_ids = list(requirement["requiredCommandIds"])
    if (
        "test-report" in evidence_kinds
        and trusted_test_policy["commandId"] not in command_ids
    ):
        command_ids.append(trusted_test_policy["commandId"])
    product_command_ids = [
        command_id
        for command_id in command_ids
        if command_id != trusted_test_policy["commandId"]
    ]
    product_policy = fixture_trusted_product_policy(manifest_document)
    candidate, script_bindings = prepare_product_scripts(
        root, product_command_ids
    )
    runner_binding = fixture_bootstrap_source_binding(
        root,
        validator.TEST_RUNNER_PATH,
    )
    requirements_source_binding = fixture_bootstrap_source_binding(
        root,
        validator.GATE_REQUIREMENTS_PATH,
    )
    gate["identity"].update(
        {
            "sourceHead": candidate,
            "productCandidate": candidate,
            "checkpointCommit": candidate,
        }
    )
    item_count = max(len(evidence_names), len(evidence_kinds))
    artifact_dir = requirement["resultPath"].rsplit("/gates/", 1)[0]
    test_total = max(requirement["minimumTestCount"], 1)
    gate["testCounts"] = {
        "discovered": test_total,
        "passed": test_total,
        "failed": 0,
        "skipped": 0,
        "notRun": 0,
        "notApplicable": 0,
        "countsBalanced": True,
    }
    evidence = []
    for index in range(item_count):
        name = (
            evidence_names[index]
            if index < len(evidence_names)
            else f"required-kind-{index}.json"
        )
        kind = evidence_kinds[index] if index < len(evidence_kinds) else "json"
        if kind == "test-report":
            kind = "json"
        relative_path = (
            f"{artifact_dir}/provider-ledger-bindings/{gate_id}/{name}"
            if name == validator.PROVIDER_LEDGER_BINDING_BASENAME
            else f"{artifact_dir}/gate-evidence/{gate_id}/{name}"
        )
        payload = {
            "gateId": gate_id,
            "productCandidate": gate["identity"]["productCandidate"],
            "status": "Passed",
        }
        evidence.append(
            {
                "evidenceId": f"{gate_id}-required-{index}",
                "kind": kind,
                "path": relative_path,
                "sha256": write_json_evidence(root, relative_path, payload),
                "redacted": True,
                "preservesFirstFailure": False,
            }
        )
    semantic_attempt_id = f"{gate_id.lower()}-semantic-1"
    semantic_command_id = trusted_test_policy["commandId"]
    semantic_report_path = (
        f"{artifact_dir}/gate-evidence/{gate_id}/"
        f"{semantic_command_id}.{semantic_attempt_id}.trusted-test-report.json"
    )
    semantic_stdout_path = (
        semantic_report_path.removesuffix(".json") + ".stdout.log"
    )
    semantic_stderr_path = (
        semantic_report_path.removesuffix(".json") + ".stderr.log"
    )
    semantic_noun = "test" if test_total == 1 else "tests"
    semantic_stdout_raw = (
        ("." * test_total).encode("ascii")
        + b"\n"
        + ("-" * 70).encode("ascii")
        + b"\n"
        + f"Ran {test_total} {semantic_noun} in 0.001s\n\nOK\n".encode(
            "ascii"
        )
    )
    semantic_stderr_raw = b""
    for stream_path, stream_raw in (
        (semantic_stdout_path, semantic_stdout_raw),
        (semantic_stderr_path, semantic_stderr_raw),
    ):
        absolute = root / stream_path
        absolute.parent.mkdir(parents=True, exist_ok=True)
        absolute.write_bytes(stream_raw)
    semantic_redacted = trusted_test_policy["redactedInvocation"]
    semantic_snapshot = {
        "headCommit": candidate,
        "treeObjectId": _git(root, "rev-parse", f"{candidate}^{{tree}}"),
        "gitStatusPorcelainV1": "",
        "trackedStatus": "Clean",
    }
    semantic_report = {
        "schemaVersion": validator.SCHEMA_VERSION,
        "runnerId": validator.TEST_RUNNER_ID,
        "runnerSource": runner_binding,
        "requirementsSource": requirements_source_binding,
        "gateId": gate_id,
        "productCandidate": candidate,
        "commandId": semantic_command_id,
        "attemptId": semantic_attempt_id,
        "commandPolicy": {
            "policyId": trusted_test_policy["policyId"],
            "sha256": hashlib.sha256(
                validator._json_bytes(trusted_test_policy)
            ).hexdigest(),
        },
        "argvSha256": hashlib.sha256(
            validator._json_bytes(
                {
                    "executableRole": trusted_test_policy["executableRole"],
                    "arguments": trusted_test_policy["arguments"],
                }
            )
        ).hexdigest(),
        "semanticLoaderDescriptorSha256": "0" * 64,
        "semanticSources": {},
        "checkoutIdentity": {
            "mode": "candidate-exact",
            "productCandidate": candidate,
            "expectedHead": candidate,
            "before": semantic_snapshot,
            "after": deepcopy(semantic_snapshot),
        },
        "runtimeInputTree": {},
        "semanticDependencyTree": {},
        "toolIdentity": {},
        "gitToolIdentity": {},
        "executionCapability": (
            validator.trusted_executor.SEMANTIC_EXECUTION_CAPABILITY
        ),
        "redactedInvocation": semantic_redacted,
        "invocationSha256": hashlib.sha256(
            semantic_redacted.encode("utf-8")
        ).hexdigest(),
        "startedAt": "2026-07-28T00:00:00Z",
        "finishedAt": "2026-07-28T00:00:01Z",
        "exitCode": 0,
        "stdout": {
            "path": semantic_stdout_path,
            "sha256": hashlib.sha256(semantic_stdout_raw).hexdigest(),
        },
        "stderr": {
            "path": semantic_stderr_path,
            "sha256": hashlib.sha256(semantic_stderr_raw).hexdigest(),
        },
        "testCounts": {
            key: gate["testCounts"][key]
            for key in validator.TEST_REPORT_COUNT_KEYS
        },
        "countsSource": trusted_test_policy["countsSource"],
    }
    evidence.append(
        {
            "evidenceId": f"{gate_id}-semantic-report",
            "kind": "test-report",
            "path": semantic_report_path,
            "sha256": write_json_evidence(
                root, semantic_report_path, semantic_report
            ),
            "redacted": True,
            "preservesFirstFailure": False,
        }
    )
    gate["evidence"] = evidence
    primary_evidence_id = evidence[0]["evidenceId"]
    test_report_evidence_id = next(
        (
            item["evidenceId"]
            for item in evidence
            if item["kind"] == "test-report"
        ),
        primary_evidence_id,
    )
    product_report_ids: dict[str, str] = {}
    tree_object = _git(root, "rev-parse", "HEAD^{tree}")
    product_policy_sha = hashlib.sha256(
        validator._json_bytes(product_policy)
    ).hexdigest()
    for ordinal, command_id in enumerate(product_command_ids):
        attempt_id = f"{gate_id.lower()}-product-{ordinal + 1}"
        counts = {
            "discovered": (
                requirement["minimumTestCount"]
                if ordinal == 0
                and gate_id not in {"W84-G0", "W92-G8"}
                else 0
            ),
            "passed": (
                requirement["minimumTestCount"]
                if ordinal == 0
                and gate_id not in {"W84-G0", "W92-G8"}
                else 0
            ),
            "failed": 0,
            "skipped": 0,
        }
        marker = {
            "protocol": validator.TRUSTED_PRODUCT_RESULT_PROTOCOL,
            "status": "Passed",
            "counts": counts,
        }
        stdout_raw = (
            b"product command completed\n"
            + b"CAICLI_PRODUCT_COMMAND_RESULT="
            + validator._json_bytes(marker)
            + b"\n"
        )
        stderr_raw = b""
        report_path = (
            f"{artifact_dir}/gate-evidence/{gate_id}/"
            f"{command_id}.{attempt_id}.trusted-product-report.json"
        )
        stdout_path = report_path.removesuffix(".json") + ".stdout.log"
        stderr_path = report_path.removesuffix(".json") + ".stderr.log"
        for stream_path, stream_raw in (
            (stdout_path, stdout_raw),
            (stderr_path, stderr_raw),
        ):
            absolute = root / stream_path
            absolute.parent.mkdir(parents=True, exist_ok=True)
            absolute.write_bytes(stream_raw)
        redacted = product_policy["redactedInvocationTemplate"].replace(
            "{commandId}", command_id
        )
        arguments = [
            "-B",
            "-X",
            "utf8",
            script_bindings[command_id]["path"],
        ]
        snapshot = {
            "headCommit": candidate,
            "treeObjectId": tree_object,
            "gitStatusPorcelainV1": "",
            "trackedStatus": "Clean",
        }
        report = {
            "schemaVersion": validator.SCHEMA_VERSION,
            "runnerId": validator.TEST_RUNNER_ID,
            "runnerSource": runner_binding,
            "requirementsSource": requirements_source_binding,
            "gateId": gate_id,
            "productCandidate": candidate,
            "commandId": command_id,
            "attemptId": attempt_id,
            "commandPolicy": {
                "policyId": validator.TRUSTED_PRODUCT_POLICY_ID,
                "sha256": product_policy_sha,
            },
            "argvSha256": hashlib.sha256(
                validator._json_bytes(
                    {
                        "executableRole": "python-current",
                        "arguments": arguments,
                    }
                )
            ).hexdigest(),
            "scriptSource": script_bindings[command_id],
            "checkoutIdentity": {
                "mode": "candidate-exact",
                "productCandidate": candidate,
                "expectedHead": candidate,
                "before": snapshot,
                "after": deepcopy(snapshot),
            },
            "redactedInvocation": redacted,
            "invocationSha256": hashlib.sha256(
                redacted.encode("utf-8")
            ).hexdigest(),
            "startedAt": "2026-07-28T00:00:00Z",
            "finishedAt": "2026-07-28T00:00:01Z",
            "exitCode": 0,
            "resultProtocol": validator.TRUSTED_PRODUCT_RESULT_PROTOCOL,
            "counts": counts,
            "countsSource": validator.TRUSTED_PRODUCT_COUNTS_SOURCE,
            "stdout": {
                "path": stdout_path,
                "sha256": hashlib.sha256(stdout_raw).hexdigest(),
            },
            "stderr": {
                "path": stderr_path,
                "sha256": hashlib.sha256(stderr_raw).hexdigest(),
            },
        }
        evidence_id = f"{gate_id}-product-command-{ordinal}"
        evidence.append(
            {
                "evidenceId": evidence_id,
                "kind": "json",
                "path": report_path,
                "sha256": write_json_evidence(root, report_path, report),
                "redacted": True,
                "preservesFirstFailure": False,
            }
        )
        product_report_ids[command_id] = evidence_id
    gate["commands"] = [
        {
            "commandId": command_id,
            "redactedCommand": (
                trusted_test_policy["redactedInvocation"]
                if command_id == trusted_test_policy["commandId"]
                else product_policy["redactedInvocationTemplate"].replace(
                    "{commandId}", command_id
                )
            ),
            "status": "Passed",
            "startedAt": "2026-07-28T00:00:00Z",
            "finishedAt": "2026-07-28T00:00:01Z",
            "exitCode": 0,
            "testCountSource": command_id
            == trusted_test_policy["commandId"],
            "evidenceRefs": [
                test_report_evidence_id
                if command_id == trusted_test_policy["commandId"]
                else product_report_ids[command_id]
            ],
        }
        for command_id in command_ids
    ]
    gate["commandCounts"] = {
        "total": len(gate["commands"]),
        "passed": len(gate["commands"]),
        "failed": 0,
        "notRun": 0,
        "notApplicable": 0,
        "countsBalanced": True,
    }
    gate["acceptanceAssertions"] = [
        {
            "assertionId": assertion_id,
            "status": "Passed",
            "evidenceRefs": [primary_evidence_id],
        }
        for assertion_id in requirement["requiredAssertionIds"]
    ]
    gate["cleanup"]["evidenceRefs"] = [primary_evidence_id]
    return fixture, manifest_document, requirement, document


def write_json_evidence(root: Path, relative_path: str, data: dict) -> str:
    path = root / relative_path
    path.parent.mkdir(parents=True, exist_ok=True)
    raw = validator._json_bytes(data)
    path.write_bytes(raw)
    return validator.hashlib.sha256(raw).hexdigest()


def _commit_fixture_paths(root: Path, message: str, *relative_paths: str) -> str:
    _git(root, "add", *relative_paths)
    _git(
        root,
        "-c",
        "user.name=Goal Validator",
        "-c",
        "user.email=validator@example.invalid",
        "commit",
        "--quiet",
        "-m",
        message,
    )
    return _git(root, "rev-parse", "HEAD")


def prepare_command_control_fixture(
    root: Path,
    *,
    direct_result: bool = False,
    undeclared_helper: bool = False,
    bad_predecessor: bool = False,
    merge_control: bool = False,
    rewrite_oracle: bool = False,
    cherry_pick_control: bool = False,
    static_verification_only: bool = False,
    candidate_equals_control: bool = False,
    gate_id: str = "W85-R1",
    previous_gate_id: str = "W85-R0",
    gate_kind: str | None = None,
    projection_override: dict | None = None,
    production_change_in_control: bool = False,
    corrupt_projection_root: bool = False,
) -> dict:
    """Build one real prior-gate P -> C -> Q command-control history."""

    _git(root, "init", "--quiet")
    _git(root, "symbolic-ref", "HEAD", "refs/heads/main")
    (root / "root.txt").write_text("root\n", encoding="utf-8")
    root_revision = _commit_fixture_paths(root, "root", "root.txt")

    (root / "tools").mkdir(parents=True, exist_ok=True)
    (root / "docs_md/weekly").mkdir(parents=True, exist_ok=True)
    (root / "tools/.gitattributes").write_text(
        "week84_92_commands/*.py -text\n"
        "command_control_oracles/*.py -text\n"
        "command_control_helpers/*.py -text\n",
        encoding="utf-8",
    )
    (root / "docs_md/weekly/.gitattributes").write_text(
        "84_92_command_control/*.json -text\n",
        encoding="utf-8",
    )
    (root / "product-state.txt").write_text("prepared\n", encoding="utf-8")
    prepared_paths = [
        "tools/.gitattributes",
        "docs_md/weekly/.gitattributes",
        "product-state.txt",
    ]
    helper_path = "tools/command_control_helpers/helper.py"
    helper_original = b"VALUE = 'sealed-outside-control'\n"
    if undeclared_helper:
        helper = root / helper_path
        helper.parent.mkdir(parents=True, exist_ok=True)
        helper.write_bytes(helper_original)
        prepared_paths.append(helper_path)
    predecessor = _commit_fixture_paths(root, "prepared predecessor", *prepared_paths)
    declared_predecessor = root_revision if bad_predecessor else predecessor

    side_revision = None
    if merge_control:
        _git(root, "checkout", "-b", "side", "--quiet", predecessor)
        (root / "side.txt").write_text("side\n", encoding="utf-8")
        side_revision = _commit_fixture_paths(root, "side parent", "side.txt")
        _git(root, "checkout", "main", "--quiet")
    elif cherry_pick_control:
        _git(root, "checkout", "-b", "source-control", "--quiet", predecessor)

    command_id = "sample-command"
    semantic_command_id = "goal-evidence-semantic-validate"
    adapter_path = f"{validator.TRUSTED_PRODUCT_SCRIPT_ROOT}/{command_id}.py"
    oracle_path = "tools/command_control_oracles/sample_oracle.py"
    adapter = root / adapter_path
    oracle = root / oracle_path
    adapter.parent.mkdir(parents=True, exist_ok=True)
    oracle.parent.mkdir(parents=True, exist_ok=True)
    if direct_result:
        adapter.write_bytes(
            b"print('CAICLI_PRODUCT_COMMAND_RESULT=Passed')\n"
        )
    elif undeclared_helper:
        adapter.write_bytes(
            b"from tools.command_control_helpers import helper\n"
            b"def run():\n    return helper.VALUE\n"
        )
    else:
        adapter.write_bytes(
            b"import subprocess\n"
            b"def run():\n"
            b"    return subprocess.run(['python', '-V'], check=False).returncode\n"
        )
    oracle_original = (
        b"def verify(exit_code):\n    return exit_code == 0\n"
    )
    oracle.write_bytes(oracle_original)

    def source_binding(role: str, relative_path: str) -> dict:
        raw = (root / relative_path).read_bytes()
        return {
            "commandId": command_id,
            "role": role,
            "path": relative_path,
            "sha256": hashlib.sha256(raw).hexdigest(),
            "gitBlobSha": _git(
                root, "hash-object", "--no-filters", relative_path
            ),
            "origin": "prepared",
            "originControlRevision": None,
            "verification": (
                {
                    "arguments": [
                        (
                            "tools.command_control_oracles.sample_oracle"
                            if static_verification_only
                            else "tools.command_control_oracles.sample_oracle."
                            "SampleOracleTests.test_sample"
                        )
                    ],
                    "verifiesCommandIds": [command_id],
                }
                if role in {"oracle", "test"}
                else None
            ),
        }

    sources = [
        source_binding("adapter", adapter_path),
        source_binding("oracle", oracle_path),
    ]
    control_path = f"{validator.COMMAND_CONTROL_ROOT}/{gate_id}.json"
    control_paths = [control_path, adapter_path, oracle_path]
    production_projection = None
    if candidate_equals_control:
        excluded_paths = sorted(control_paths)
        baseline_projection = (
            validator._command_control_production_projection_inventory(
                root, predecessor, set(excluded_paths)
            )
        )
        assert baseline_projection is not None
        production_projection = {
            "protocol": validator.COMMAND_CONTROL_PRODUCTION_PROJECTION_PROTOCOL,
            "baselineRevision": predecessor,
            "excludedPaths": excluded_paths,
            "entryCount": len(baseline_projection),
            "productionProjectionRoot": (
                validator._command_control_production_projection_root(
                    baseline_projection
                )
            ),
        }
        if corrupt_projection_root:
            production_projection["productionProjectionRoot"] = "f" * 64
    if projection_override is not None:
        production_projection = projection_override
    control_data = {
        "schemaVersion": validator.SCHEMA_VERSION,
        "protocol": validator.COMMAND_CONTROL_PROTOCOL,
        "goalId": validator.GOAL_ID,
        "gateId": gate_id,
        "sourceTrust": "prior-sealed",
        "preparedFromRevision": declared_predecessor,
        "sources": sources,
        "sourceTrees": [],
        "runtimeDrivers": [],
        "productionProjection": production_projection,
    }
    control = root / control_path
    control.parent.mkdir(parents=True, exist_ok=True)
    control.write_bytes(validator._json_bytes(control_data))
    committed_control_paths = list(control_paths)
    if production_change_in_control:
        (root / "product-state.txt").write_text(
            "production changed in control\n", encoding="utf-8"
        )
        committed_control_paths.append("product-state.txt")

    if merge_control:
        _git(root, "add", *committed_control_paths)
        tree = _git(root, "write-tree")
        assert side_revision is not None
        control_revision = _git(
            root,
            "-c",
            "user.name=Goal Validator",
            "-c",
            "user.email=validator@example.invalid",
            "commit-tree",
            tree,
            "-p",
            predecessor,
            "-p",
            side_revision,
            "-m",
            "merge command control",
        )
        _git(root, "update-ref", "refs/heads/main", control_revision)
        _git(root, "reset", "--hard", control_revision)
    else:
        source_control_revision = _commit_fixture_paths(
            root, "command control", *committed_control_paths
        )
        if cherry_pick_control:
            _git(root, "checkout", "main", "--quiet")
            (root / "product-state.txt").write_text(
                "advanced-before-cherry-pick\n", encoding="utf-8"
            )
            _commit_fixture_paths(
                root,
                "advance target before cherry-pick",
                "product-state.txt",
            )
            _git(
                root,
                "-c",
                "user.name=Goal Validator",
                "-c",
                "user.email=validator@example.invalid",
                "cherry-pick",
                "--quiet",
                source_control_revision,
            )
            control_revision = _git(root, "rev-parse", "HEAD")
        else:
            control_revision = source_control_revision

    if candidate_equals_control:
        candidate = control_revision
    elif rewrite_oracle:
        oracle.write_bytes(b"def verify(exit_code):\n    return False\n")
        _commit_fixture_paths(root, "mutate sealed oracle", oracle_path)
        oracle.write_bytes(oracle_original)
        candidate = _commit_fixture_paths(root, "revert sealed oracle", oracle_path)
    elif undeclared_helper:
        helper = root / helper_path
        helper.write_bytes(b"VALUE = 'mutable'\n")
        _commit_fixture_paths(root, "mutate undeclared helper", helper_path)
        helper.write_bytes(helper_original)
        candidate = _commit_fixture_paths(root, "revert undeclared helper", helper_path)
    else:
        (root / "product-state.txt").write_text("candidate\n", encoding="utf-8")
        candidate = _commit_fixture_paths(
            root, "product candidate", "product-state.txt"
        )

    control_raw = control.read_bytes()
    binding = {
        "path": control_path,
        "sha256": hashlib.sha256(control_raw).hexdigest(),
        "gitBlobSha": _git(
            root, "rev-parse", f"{control_revision}:{control_path}"
        ),
        "controlRevision": control_revision,
        "preparedFromRevision": declared_predecessor,
        "sourceTrust": "prior-sealed",
    }
    previous = validator.document_from_data(
        f"artifacts/test/gates/{previous_gate_id}.json",
        {
            "gateId": previous_gate_id,
            "status": "Passed",
            "identity": {"productCandidate": predecessor},
            "commandControlBinding": None,
        },
    )
    current = validator.document_from_data(
        f"artifacts/test/gates/{gate_id}.json",
        {
            "gateId": gate_id,
            "status": "Passed",
            "identity": {"productCandidate": candidate},
            "commandControlBinding": binding,
        },
    )
    contract = validator.Contract(
        validator.CANONICAL_GATE_IDS,
        validator._canonical_groups_without_patterns(),
    )
    requirement = {
        "gateId": gate_id,
        "gateKind": gate_kind,
        "requiredCommandIds": [command_id, semantic_command_id],
        "commandControl": validator._expected_command_control_policy(
            contract, gate_id
        ),
    }
    manifest = {
        "frozenPolicies": {
            "trusted-test-command": {"commandId": semantic_command_id}
        },
        "gates": [requirement],
    }
    return {
        "manifest": manifest,
        "gates": [previous, current],
        "contract": contract,
        "controlRevision": control_revision,
        "predecessor": predecessor,
        "candidate": candidate,
    }


def validate_command_control_fixture(root: Path, fixture: dict) -> list[str]:
    problems = validator.Problems()
    validator.validate_command_controls(
        fixture["manifest"],
        fixture["gates"],
        [],
        fixture["contract"],
        root,
        problems,
    )
    return problems.items


def prepare_source_tree_fixture(root: Path) -> dict:
    """Build a real P -> C -> Q history for one prepared C# test tree."""

    _git(root, "init", "--quiet")
    _git(root, "symbolic-ref", "HEAD", "refs/heads/main")
    _git(root, "config", "core.autocrlf", "false")
    files = {
        ".gitattributes": "* text=auto\n",
        "global.json": '{"sdk":{"version":"9.0.100"}}\n',
        "src/Product/Product.csproj": "<Project />\n",
        "src/CSharpAiCli.Tests/CSharpAiCli.Tests.csproj": "<Project />\n",
        "src/CSharpAiCli.Tests/BaselineTests.cs": "// baseline\n",
        "product.txt": "P\n",
    }
    for relative_path, contents in files.items():
        path = root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(contents, encoding="utf-8")
    predecessor = _commit_fixture_paths(
        root, "source tree predecessor", *files
    )

    modified_path = "src/CSharpAiCli.Tests/BaselineTests.cs"
    added_path = "src/CSharpAiCli.Tests/NewTests.cs"
    (root / modified_path).write_text("// reviewed modification\n", encoding="utf-8")
    (root / added_path).write_text("// reviewed addition\n", encoding="utf-8")
    control_revision = _commit_fixture_paths(
        root, "source tree control", modified_path, added_path
    )
    inventory = validator._source_tree_revision_inventory(
        root, control_revision, "csharp-test-tree-v1"
    )
    worktree_inventory = validator._source_tree_worktree_inventory(
        root, inventory or []
    )
    assert inventory is not None and worktree_inventory is not None
    tree = {
        "commandId": "sample-command",
        "role": "fixture",
        "rootPath": ".",
        "gitTreeSha": _git(root, "rev-parse", f"{control_revision}^{{tree}}"),
        "entryCount": len(inventory),
        "totalBytes": sum(item["bytes"] for item in worktree_inventory),
        "contentRootSha256": validator._source_tree_content_root(
            worktree_inventory
        ),
        "origin": "prepared",
        "originControlRevision": None,
        "includePolicy": "csharp-test-tree-v1",
        "preparedPaths": sorted([modified_path, added_path]),
    }
    (root / "product.txt").write_text("Q\n", encoding="utf-8")
    candidate = _commit_fixture_paths(root, "product candidate", "product.txt")
    return {
        "tree": tree,
        "predecessor": predecessor,
        "controlRevision": control_revision,
        "candidate": candidate,
        "selectedPaths": {item["path"] for item in inventory},
    }


def validate_source_tree_fixture(root: Path, fixture: dict) -> tuple[set[str], list[str]]:
    problems = validator.Problems()
    selected = validator._validate_command_control_source_tree(
        root,
        fixture["tree"],
        fixture["controlRevision"],
        fixture["predecessor"],
        fixture["candidate"],
        ["sample-command"],
        "W85-R1",
        {},
        problems,
        "source-tree.json",
    )
    return selected, problems.items


def prepare_runtime_driver_binding_fixture(root: Path) -> dict:
    _git(root, "init", "--quiet")
    _git(root, "symbolic-ref", "HEAD", "refs/heads/main")
    (root / "tools").mkdir(parents=True, exist_ok=True)
    (root / "docs_md/weekly/84_92_command_control").mkdir(
        parents=True,
        exist_ok=True,
    )
    (root / "tools/.gitattributes").write_text(
        "week84_92_provider_drivers/*.mjs -text\n",
        encoding="utf-8",
    )
    (root / "docs_md/weekly/.gitattributes").write_text(
        "84_92_command_control/*.json -text\n",
        encoding="utf-8",
    )
    (root / "root.txt").write_text("root\n", encoding="utf-8")
    root_revision = _commit_fixture_paths(
        root,
        "root",
        "root.txt",
        "tools/.gitattributes",
        "docs_md/weekly/.gitattributes",
    )
    phase = "provider-read-only"
    driver_path = validator.PROVIDER_DRIVER_PATHS[phase]
    driver = root / driver_path
    driver.parent.mkdir(parents=True, exist_ok=True)
    driver.write_bytes(b"export const run = () => ({ ok: true });\n")
    driver_sha = hashlib.sha256(driver.read_bytes()).hexdigest()
    driver_blob = _git(root, "hash-object", "--no-filters", driver_path)
    origin_control_path = f"{validator.COMMAND_CONTROL_ROOT}/W84-G5.json"
    origin_control = {
        "schemaVersion": validator.SCHEMA_VERSION,
        "protocol": validator.COMMAND_CONTROL_PROTOCOL,
        "goalId": validator.GOAL_ID,
        "gateId": "W84-G5",
        "sourceTrust": "prior-sealed",
        "preparedFromRevision": root_revision,
        "sources": [
            {
                "commandId": "package-identity-inventory",
                "role": "fixture",
                "path": driver_path,
                "sha256": driver_sha,
                "gitBlobSha": driver_blob,
                "origin": "prepared",
                "originControlRevision": None,
                "verification": None,
            }
        ],
        "sourceTrees": [],
        "runtimeDrivers": [],
        "productionProjection": None,
    }
    (root / origin_control_path).write_bytes(validator._json_bytes(origin_control))
    origin_revision = _commit_fixture_paths(
        root,
        "W84-G5 runtime driver preseal",
        driver_path,
        origin_control_path,
    )
    (root / "prepared.txt").write_text("prepared\n", encoding="utf-8")
    prepared_revision = _commit_fixture_paths(
        root,
        "provider gate predecessor",
        "prepared.txt",
    )
    (root / "current-control.txt").write_text("control\n", encoding="utf-8")
    control_revision = _commit_fixture_paths(
        root,
        "provider gate control",
        "current-control.txt",
    )
    (root / "candidate.txt").write_text("candidate\n", encoding="utf-8")
    candidate = _commit_fixture_paths(
        root,
        "provider candidate",
        "candidate.txt",
    )
    return {
        "phase": phase,
        "path": driver_path,
        "sha256": driver_sha,
        "gitBlobSha": driver_blob,
        "originControlRevision": origin_revision,
        "preparedFromRevision": prepared_revision,
        "controlRevision": control_revision,
        "candidate": candidate,
    }


def prepare_provider_boundary_deep_fixture(
    root: Path,
    *,
    cooperative_isolation_claim: bool = False,
) -> dict:
    _git(root, "init", "--quiet")
    _git(root, "symbolic-ref", "HEAD", "refs/heads/main")
    (root / "docs_md/weekly").mkdir(parents=True, exist_ok=True)
    (root / "docs_md/weekly/.gitattributes").write_text(
        "84_92_provider_boundary_decisions/*.json -text\n",
        encoding="utf-8",
    )
    (root / "product.txt").write_text("candidate\n", encoding="utf-8")
    candidate = _commit_fixture_paths(
        root,
        "provider product candidate",
        "docs_md/weekly/.gitattributes",
        "product.txt",
    )

    tree_root = "a" * 64
    desktop_sha = "b" * 64
    app_host_sha = "c" * 64
    package_path = (
        "artifacts/week84-renderer-listener-retention/gate-evidence/"
        "W84-G6/package-identity.json"
    )
    package_data = {
        "schemaVersion": validator.SCHEMA_VERSION,
        "status": "Passed",
        "productCandidate": candidate,
        "packageIdentity": {
            "packageRoot": "dist/win-unpacked",
            "treeRootSha256": tree_root,
            "entryCount": 2,
            "totalBytes": 300,
            "entrypoint": validator.PROVIDER_PACKAGE_ENTRYPOINT,
            "argv": validator.PROVIDER_PACKAGE_ARGV,
            "entries": [
                {
                    "path": validator.PROVIDER_PACKAGE_ENTRYPOINT,
                    "sha256": desktop_sha,
                },
                {
                    "path": validator.PROVIDER_PACKAGE_APPHOST_PATH,
                    "sha256": app_host_sha,
                },
            ],
        },
    }
    package_sha = write_json_evidence(root, package_path, package_data)
    decision_id = "PBD-W84-G6-001"
    decision_path = validator.PROVIDER_BOUNDARY_DECISION_PATHS["W84-G6"]
    decision_data = {
        "goalId": validator.GOAL_ID,
        "decisionId": decision_id,
        "boundaryGateId": "W84-G6",
        "mode": "cooperative-candidate",
        "productCandidate": candidate,
        "packageIdentity": {
            "path": package_path,
            "sha256": package_sha,
            "treeRootSha256": tree_root,
            "entrypoint": validator.PROVIDER_PACKAGE_ENTRYPOINT,
            "argv": validator.PROVIDER_PACKAGE_ARGV,
        },
        "allowedGates": ["W84-G6", "W84-G7", "W84-G8"],
        "allowedScopes": [
            "provider-read-only",
            "provider-recovery",
            "provider-resource",
            "controlled-write",
        ],
        "authorizedTurns": 34,
        "maxTurns": 120,
        "userChallenge": {
            "requestId": "BR-001",
            "challengeCode": "CH-ABCDEFGH",
            "responseSha256": "d" * 64,
            "confirmedBy": "User",
        },
        "issuedAt": "2026-07-28T00:00:00Z",
        "expiresAt": "2026-07-28T01:00:00Z",
        "revokedAt": None,
        "strongCanaries": None,
    }
    decision = root / decision_path
    decision.parent.mkdir(parents=True, exist_ok=True)
    decision.write_bytes(validator._json_bytes(decision_data))
    first_add = _commit_fixture_paths(
        root, "provider boundary decision", decision_path
    )
    decision_raw = decision.read_bytes()
    decision_binding = {
        "path": decision_path,
        "sha256": hashlib.sha256(decision_raw).hexdigest(),
        "firstAddCommit": first_add,
        "decisionId": decision_id,
        "mode": "cooperative-candidate",
        "packageTreeRootSha256": tree_root,
    }
    result_path = (
        "artifacts/week84-renderer-listener-retention/gates/W84-G6.json"
    )
    gate = validator.document_from_data(
        result_path,
        {
            "gateId": "W84-G6",
            "resultPath": result_path,
            "status": "Passed",
            "startedAt": "2026-07-28T00:10:00Z",
            "finishedAt": "2026-07-28T00:20:00Z",
            "identity": {"productCandidate": candidate},
            "authorization": {
                "providerSuccessfulAttemptId": "W84-G6-attempt-1",
                "providerBoundaryDecision": decision_binding,
                "packageLaunchReceipts": [],
            },
        },
    )

    bindings = []
    for ordinal, (phase, profile, batch_size) in enumerate(
        validator.PROVIDER_LAUNCH_LAYOUT["W84-G6"], start=1
    ):
        batch_id = f"batch-{ordinal}-0123456789abcdef01234567"
        basename = validator._provider_launch_receipt_basename(phase, profile)
        receipt_path = (
            "artifacts/week84-renderer-listener-retention/gate-evidence/"
            f"W84-G6/{basename}"
        )
        containment_claim = cooperative_isolation_claim
        receipt_data = {
            "status": "Passed",
            "goalId": validator.GOAL_ID,
            "gateId": "W84-G6",
            "productCandidate": candidate,
            "phase": phase,
            "profileOrdinal": profile,
            "batchId": batch_id,
            "packageTreeRootSha256": tree_root,
            "boundaryDecision": {
                "decisionId": decision_id,
                "mode": "cooperative-candidate",
            },
            "sourcePackage": {
                "identityPath": package_path,
                "identityRawSha256": package_sha,
                "packageRoot": "dist/win-unpacked",
                "packageTreeRootSha256": tree_root,
                "entryCount": 2,
                "totalBytes": 300,
                "entrypoint": validator.PROVIDER_PACKAGE_ENTRYPOINT,
                "argv": validator.PROVIDER_PACKAGE_ARGV,
                "appHostPath": validator.PROVIDER_PACKAGE_APPHOST_PATH,
                "appHostSha256": app_host_sha,
            },
            "stagedPackageBefore": {
                "packageTreeRootSha256": tree_root,
                "entryCount": 2,
                "totalBytes": 300,
            },
            "stagedPackageAfter": {
                "packageTreeRootSha256": tree_root,
                "entryCount": 2,
                "totalBytes": 300,
            },
            "launches": {
                "desktop": {
                    "imagePath": validator.PROVIDER_PACKAGE_ENTRYPOINT,
                    "imageSha256": desktop_sha,
                    "pid": 100,
                    "startedAt": "2026-07-28T00:11:00Z",
                    "exitedAt": "2026-07-28T00:12:00Z",
                    "exitCode": 0,
                },
                "appHost": {
                    "imagePath": validator.PROVIDER_PACKAGE_APPHOST_PATH,
                    "imageSha256": app_host_sha,
                    "pid": 101,
                    "startedAt": "2026-07-28T00:11:10Z",
                    "exitedAt": "2026-07-28T00:11:50Z",
                    "exitCode": 0,
                },
            },
            "containment": {
                "attachedBeforeRelease": True,
                "processIsolated": containment_claim,
                "allEgressIsolated": containment_claim,
                "sandboxPolicySha256": None,
                "strongCanaryEvidence": None,
            },
            "observedRequestCount": batch_size,
            "scenarioAssertions": {
                "desktopUiObserved": True,
                "productBehaviorPassed": True,
                "recoveryListenerObserved": phase == "provider-recovery",
                "details": {"scenario": phase, "status": "Passed"},
            },
            "cleanup": {
                "status": "Passed",
                "processDelta": 0,
                "temporaryDelta": 0,
            },
        }
        receipt_sha = write_json_evidence(root, receipt_path, receipt_data)
        bindings.append(
            {
                "path": receipt_path,
                "sha256": receipt_sha,
                "phase": phase,
                "profileOrdinal": profile,
                "batchId": batch_id,
                "packageTreeRootSha256": tree_root,
            }
        )
    gate.data["authorization"]["packageLaunchReceipts"] = bindings
    return {
        "gate": gate,
        "decisionBinding": decision_binding,
        "receipts": bindings,
    }


def validate_provider_boundary_deep_fixture(root: Path, fixture: dict) -> list[str]:
    problems = validator.Problems()
    bundle = validator._validate_provider_boundary_decision(
        root,
        fixture["gate"],
        fixture["decisionBinding"],
        problems,
    )
    if bundle is not None:
        decision, package_document = bundle
        for binding, layout in zip(
            fixture["receipts"],
            validator.PROVIDER_LAUNCH_LAYOUT["W84-G6"],
        ):
            validator._validate_provider_launch_receipt(
                root,
                fixture["gate"],
                binding,
                layout,
                decision,
                package_document,
                problems,
            )
    return problems.items


def _write_executor_prerequisite_results(
    root: Path,
    gate_id: str,
    prerequisite_candidate: str,
    finished_at: str,
) -> dict[str, validator.Document]:
    documents: dict[str, validator.Document] = {}
    for prerequisite_id in validator.trusted_executor.CONTROLLED_WRITE_PRECONDITIONS[
        gate_id
    ]:
        relative_path = validator.trusted_executor.CANONICAL_GATE_RESULT_PATHS[
            prerequisite_id
        ]
        data = {
            "gateId": prerequisite_id,
            "status": "Passed",
            "identity": {"productCandidate": prerequisite_candidate},
            "finishedAt": finished_at,
        }
        raw = validator._json_bytes(data)
        path = root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
        documents[prerequisite_id] = validator.Document(
            path=path,
            relative_path=relative_path,
            data=data,
            sha256=hashlib.sha256(raw).hexdigest(),
        )
    return documents


def _controlled_bundle_times(gate_id: str) -> dict[str, str]:
    if gate_id == "W84-G8":
        return {
            "prerequisiteFinishedAt": "2026-07-28T00:00:18.000000Z",
            "decidedAt": "2026-07-28T00:00:19.000000Z",
            "authorizedAt": "2026-07-28T00:00:19.500000Z",
            "issuedAt": "2026-07-28T00:00:20.500000Z",
            "consumedAt": "2026-07-28T00:00:20.750000Z",
        }
    return {
        "prerequisiteFinishedAt": "2026-07-28T00:00:28.000000Z",
        "decidedAt": "2026-07-28T00:00:29.000000Z",
        "authorizedAt": "2026-07-28T00:00:29.500000Z",
        "issuedAt": "2026-07-28T00:00:30.500000Z",
        "consumedAt": "2026-07-28T00:00:30.750000Z",
    }


def _write_controlled_authorization_bundle(
    root: Path,
    gate_id: str,
    product_candidate: str,
    prerequisite_documents: dict[str, validator.Document],
) -> dict[str, object]:
    executor = validator.trusted_executor
    week = "84" if gate_id == "W84-G8" else "92"
    times = _controlled_bundle_times(gate_id)
    prerequisite_bindings = [
        {
            "gateId": prerequisite_id,
            "productCandidate": prerequisite_documents[
                prerequisite_id
            ].data["identity"]["productCandidate"],
            "resultPath": prerequisite_documents[
                prerequisite_id
            ].relative_path,
            "resultSha256": prerequisite_documents[prerequisite_id].sha256,
            "status": "Passed",
            "finishedAt": prerequisite_documents[
                prerequisite_id
            ].data["finishedAt"],
        }
        for prerequisite_id in executor.CONTROLLED_WRITE_PRECONDITIONS[gate_id]
    ]
    candidate_binding = {
        "productCandidate": product_candidate,
        "treeObjectId": _git(
            root, "rev-parse", f"{product_candidate}^{{tree}}"
        ),
    }
    workspace_name = f"caicli-week{week}-write-test"
    workspace = {
        "workspaceId": f"CWW-W{week}-ABCDEFGH",
        "workspaceName": workspace_name,
        "workspaceRelativePath": (
            f"{executor.PROVIDER_ARTIFACT_ROOTS[gate_id]}/"
            f"controlled-write-workspaces/{workspace_name}"
        ),
        "owner": executor.CONTROLLED_WRITE_HARNESS_OWNER,
        "scenarioPath": executor.PROVIDER_SCENARIO_PATHS["controlled-write"],
        "cleanupRequired": True,
    }
    transition = deepcopy(executor.CONTROLLED_WRITE_TRANSITION)
    validation_command = deepcopy(executor.CONTROLLED_WRITE_VALIDATION)
    decision_bindings = executor._decision_bindings(
        candidate_binding=candidate_binding,
        prerequisite_bindings=prerequisite_bindings,
        workspace_identity=workspace,
        write_transition=transition,
        validation_command=validation_command,
    )
    preauthorization_id = f"CWPA-W{week}-ABCDEFGH"
    approval_documents: list[tuple[str, str, str, bytes]] = []
    for action, label in (("apply_patch", "PATCH"), ("shell", "SHELL")):
        approval_id = f"CWAD-W{week}-{label}-ABCDEFGH"
        relative_path = executor.CONTROLLED_WRITE_APPROVAL_DECISIONS[gate_id][
            action
        ]
        decision = {
            "schemaVersion": validator.SCHEMA_VERSION,
            "protocol": executor.CONTROLLED_WRITE_APPROVAL_PROTOCOL,
            "goalId": validator.GOAL_ID,
            "preauthorizationId": preauthorization_id,
            "gateId": gate_id,
            "productCandidate": product_candidate,
            "approvalId": approval_id,
            "action": action,
            "decision": "Approve",
            "durable": True,
            "decisionBindings": decision_bindings,
            "decidedAt": times["decidedAt"],
        }
        raw = validator._json_bytes(decision)
        path = root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
        approval_documents.append((action, approval_id, relative_path, raw))
    approval_commit = _commit_fixture_paths(
        root,
        f"{gate_id} durable controlled-write approvals",
        *(item[2] for item in approval_documents),
    )
    approval_bindings = [
        {
            "approvalId": approval_id,
            "action": action,
            "path": relative_path,
            "sha256": hashlib.sha256(raw).hexdigest(),
            "commit": approval_commit,
        }
        for action, approval_id, relative_path, raw in approval_documents
    ]
    preauthorization = {
        "schemaVersion": validator.SCHEMA_VERSION,
        "protocol": executor.CONTROLLED_WRITE_PREAUTHORIZATION_PROTOCOL,
        "goalId": validator.GOAL_ID,
        "preauthorizationId": preauthorization_id,
        "gateId": gate_id,
        "status": "Authorized",
        "productCandidate": product_candidate,
        "candidateBinding": candidate_binding,
        "preconditionGateBindings": prerequisite_bindings,
        "harnessWorkspaceIdentity": workspace,
        "writeTransition": transition,
        "validationCommand": validation_command,
        "approvalDecisionBindings": approval_bindings,
        "authorizedAt": times["authorizedAt"],
    }
    preauthorization_path = executor.CONTROLLED_WRITE_PREAUTHORIZATIONS[gate_id]
    preauthorization_raw = validator._json_bytes(preauthorization)
    path = root / preauthorization_path
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(preauthorization_raw)
    preauthorization_commit = _commit_fixture_paths(
        root,
        f"{gate_id} controlled-write preauthorization",
        preauthorization_path,
    )
    preauthorization_binding = {
        "path": preauthorization_path,
        "sha256": hashlib.sha256(preauthorization_raw).hexdigest(),
        "commit": preauthorization_commit,
    }
    tombstone = {
        "schemaVersion": validator.SCHEMA_VERSION,
        "goalId": validator.GOAL_ID,
        "gateId": gate_id,
        "productCandidate": product_candidate,
        "leaseId": f"CW-W{week}-ABCDEFGH",
        "nonceSha256": ("d" if week == "84" else "e") * 64,
        "issuedAt": times["issuedAt"],
        "consumedAt": times["consumedAt"],
        "preconditionGateBindings": prerequisite_bindings,
        "preauthorizationBinding": preauthorization_binding,
    }
    tombstone_path = executor.CONTROLLED_WRITE_TOMBSTONES[gate_id]
    tombstone_raw = validator._json_bytes(tombstone)
    path = root / tombstone_path
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(tombstone_raw)
    tombstone_commit = _commit_fixture_paths(
        root,
        f"{gate_id} consume controlled-write lease",
        tombstone_path,
    )
    return {
        "preconditionGateBindings": prerequisite_bindings,
        "approvalDecisionBindings": approval_bindings,
        "preauthorizationBinding": preauthorization_binding,
        "tombstone": tombstone,
        "tombstoneBinding": {
            "path": tombstone_path,
            "sha256": hashlib.sha256(tombstone_raw).hexdigest(),
            "commit": tombstone_commit,
        },
        "expectedHead": tombstone_commit,
    }


def prepare_provider_runtime_journal(root: Path, fixture: dict) -> dict:
    if not (root / ".git").exists():
        _git(
            root,
            "init",
            "--quiet",
            "--initial-branch=codex/week84-92-refactor",
        )
    seed_path = root / "fixture-seed.txt"
    seed_path.write_text("provider fixture seed\n", encoding="utf-8")
    seed_candidate = _commit_fixture_paths(
        root, "provider fixture seed", "fixture-seed.txt"
    )

    harness_path = root / validator.TRUSTED_PROVIDER_HARNESS_PATH
    harness_path.parent.mkdir(parents=True, exist_ok=True)
    harness_path.write_bytes(
        (SCRIPT.parents[1] / validator.TRUSTED_PROVIDER_HARNESS_PATH).read_bytes()
    )
    scenario_raw_by_phase: dict[str, bytes] = {}
    for phase, relative_path in validator.PROVIDER_SCENARIO_PATHS.items():
        scenario_raw = (
            "from __future__ import annotations\n"
            f"PHASE = {phase!r}\n"
        ).encode("utf-8")
        scenario_path = root / relative_path
        scenario_path.parent.mkdir(parents=True, exist_ok=True)
        scenario_path.write_bytes(scenario_raw)
        scenario_raw_by_phase[phase] = scenario_raw
    tools_attributes = harness_path.parent / ".gitattributes"
    tools_attributes.write_text(
        (
            f"{harness_path.name} -text\n"
            "week84_92_provider_scenarios/*.py -text\n"
        ),
        encoding="utf-8",
    )
    weekly_attributes = root / "docs_md/weekly/.gitattributes"
    weekly_attributes.parent.mkdir(parents=True, exist_ok=True)
    weekly_attributes.write_text(
        (
            "84_92_controlled_write_tombstones/*.json -text\n"
            "84_92_controlled_write_authorizations/*/*.json -text\n"
        ),
        encoding="utf-8",
    )
    policy = {
        "policyId": validator.TRUSTED_PROVIDER_POLICY_ID,
        "executableRole": validator.ISOLATED_PYTHON_EXECUTABLE_ROLE,
        "sourcePath": validator.TRUSTED_PROVIDER_HARNESS_PATH,
        "sourceSha256": hashlib.sha256(harness_path.read_bytes()).hexdigest(),
        "scenarioSources": {
            phase: {
                "path": validator.PROVIDER_SCENARIO_PATHS[phase],
                "sha256": hashlib.sha256(raw).hexdigest(),
            }
            for phase, raw in scenario_raw_by_phase.items()
        },
        "argumentsTemplate": list(
            validator.trusted_executor._PROVIDER_TEMPLATE_ARGUMENTS
        ),
        "redactedInvocation": validator.TRUSTED_PROVIDER_REDACTED_INVOCATION,
        "shellAllowed": False,
        "allowedBatchSizes": {
            key: [value] for key, value in validator.PROVIDER_BATCH_SIZES.items()
        },
        "observedRequestProtocol": validator.PROVIDER_OBSERVED_REQUEST_PROTOCOL,
    }
    manifest_path = root / validator.GATE_REQUIREMENTS_PATH
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_data = deepcopy(requirements_manifest_document().data)
    manifest_data.setdefault("frozenPolicies", {})[
        "trusted-provider-command"
    ] = policy
    manifest_path.write_bytes(validator._json_bytes(manifest_data))
    w84_prerequisites = _write_executor_prerequisite_results(
        root,
        "W84-G8",
        seed_candidate,
        _controlled_bundle_times("W84-G8")["prerequisiteFinishedAt"],
    )
    w92_prerequisites = _write_executor_prerequisite_results(
        root,
        "W92-G7",
        seed_candidate,
        _controlled_bundle_times("W92-G7")["prerequisiteFinishedAt"],
    )
    candidate_paths = [
        validator.TRUSTED_PROVIDER_HARNESS_PATH,
        *validator.PROVIDER_SCENARIO_PATHS.values(),
        "tools/.gitattributes",
        "docs_md/weekly/.gitattributes",
        validator.GATE_REQUIREMENTS_PATH,
        *(item.relative_path for item in w84_prerequisites.values()),
        *(item.relative_path for item in w92_prerequisites.values()),
    ]
    w84_candidate = _commit_fixture_paths(
        root, "provider candidate", *candidate_paths
    )
    controlled_bundles = {
        "W84-G8": _write_controlled_authorization_bundle(
            root,
            "W84-G8",
            w84_candidate,
            w84_prerequisites,
        )
    }
    w92_candidate = str(controlled_bundles["W84-G8"]["expectedHead"])
    controlled_bundles["W92-G7"] = _write_controlled_authorization_bundle(
        root,
        "W92-G7",
        w92_candidate,
        w92_prerequisites,
    )
    tombstone_commits = {
        gate_id: str(bundle["expectedHead"])
        for gate_id, bundle in controlled_bundles.items()
    }

    candidate_by_gate = {
        "W84-G6": w84_candidate,
        "W84-G7": w84_candidate,
        "W84-G8": w84_candidate,
        "W92-G7": w92_candidate,
    }
    ledger_data = fixture["ledger"].data
    for gate_id, candidate in candidate_by_gate.items():
        fixture_gate(fixture, gate_id).data["identity"].update(
            {
                "sourceHead": candidate,
                "productCandidate": candidate,
                "checkpointCommit": candidate,
            }
        )
        for entry in ledger_data["entries"]:
            if entry["gateId"] == gate_id:
                entry["productCandidate"] = candidate
        for event in ledger_data["attemptEvents"]:
            if (
                event.get("eventType") == "AttemptFinished"
                and event.get("gateId") == gate_id
            ):
                event["productCandidate"] = candidate
    rehash_provider_entries(ledger_data)
    previous = None
    for event in ledger_data["attemptEvents"]:
        event["previousAttemptEventSha256"] = previous
        event["attemptEventSha256"] = validator.provider_attempt_event_sha256(
            event
        )
        previous = event["attemptEventSha256"]
    ledger_data["lastAttemptEventSha256"] = previous
    fixture["ledger"] = validator.document_from_data(
        validator.LEDGER_PATH, ledger_data
    )
    provider = fixture["goal"].data["authorization"]["provider"]
    provider["ledgerSha256"] = fixture["ledger"].sha256
    provider["lastAttemptEventSha256"] = previous
    for gate_document in fixture["gates"]:
        authorization = gate_document.data["authorization"]
        try:
            projection = provider_prefix_projection(
                ledger_data, authorization["providerSequenceAfter"]
            )
        except AssertionError:
            # Intentionally malformed event-order tests cannot have a valid
            # canonical prefix; retain the prior binding and let validation
            # report the underlying journal defect.
            continue
        authorization["providerLedgerPrefixSha256"] = projection[
            "combinedPrefixSha256"
        ]

    harness_raw = harness_path.read_bytes()
    harness_sha = hashlib.sha256(harness_raw).hexdigest()
    harness_binding_by_candidate = {
        candidate: {
            "path": validator.TRUSTED_PROVIDER_HARNESS_PATH,
            "sha256": harness_sha,
            "gitBlobSha": _git(
                root,
                "rev-parse",
                f"{candidate}:{validator.TRUSTED_PROVIDER_HARNESS_PATH}",
            ),
            "controlRevision": w84_candidate,
        }
        for candidate in {w84_candidate, w92_candidate}
    }
    scenario_binding_by_candidate_and_phase = {
        (candidate, phase): {
            "path": relative_path,
            "sha256": hashlib.sha256(
                scenario_raw_by_phase[phase]
            ).hexdigest(),
            "gitBlobSha": _git(
                root,
                "rev-parse",
                f"{candidate}:{relative_path}",
            ),
            "controlRevision": w84_candidate,
        }
        for candidate in {w84_candidate, w92_candidate}
        for phase, relative_path in validator.PROVIDER_SCENARIO_PATHS.items()
    }
    checkout_by_gate: dict[str, dict] = {}
    for gate_id, candidate in candidate_by_gate.items():
        if gate_id == "W84-G8":
            expected_head = tombstone_commits["W84-G8"]
            mode = "controlled-tombstone-descendant"
        elif gate_id == "W92-G7":
            expected_head = tombstone_commits["W92-G7"]
            mode = "controlled-tombstone-descendant"
        else:
            expected_head = candidate
            mode = "candidate-exact"
        snapshot = {
            "headCommit": expected_head,
            "treeObjectId": _git(
                root, "rev-parse", f"{expected_head}^{{tree}}"
            ),
            "gitStatusPorcelainV1": "",
            "trackedStatus": "Clean",
        }
        checkout_by_gate[gate_id] = {
            "mode": mode,
            "productCandidate": candidate,
            "expectedHead": expected_head,
            "before": snapshot,
            "after": deepcopy(snapshot),
        }

    policy_sha = hashlib.sha256(validator._json_bytes(policy)).hexdigest()
    runtime_events: list[dict] = []
    previous_runtime_hash = None
    resource_ordinals: dict[tuple[str, str], int] = {}
    batch_ordinals_by_gate: dict[str, int] = {}
    entries = ledger_data["entries"]
    completion_outcome_by_reservation = {
        event["reservationId"]: event["outcome"]
        for event in ledger_data["attemptEvents"]
        if event.get("eventType") == "TurnCompleted"
    }
    cursor = 0
    while cursor < len(entries):
        first = entries[cursor]
        phase = first["phase"]
        batch_size = validator.PROVIDER_BATCH_SIZES[phase]
        batch_entries = entries[cursor : cursor + batch_size]
        batch_succeeded = all(
            completion_outcome_by_reservation.get(entry["reservationId"])
            == "Succeeded"
            for entry in batch_entries
        )
        batch_id = (
            f"batch-{first['sequence']}-{first['sequence']:024x}"
            if batch_succeeded
            else f"failed-{first['sequence']}-{first['sequence']:016x}"
        )
        gate_id = first["gateId"]
        artifact_root = validator.GOAL_ARTIFACT_DIR_BY_GROUP[
            fixture["contract"].gate_to_group[gate_id].key
        ]
        descriptor_path = (
            f"{artifact_root}/provider-runtime/{gate_id}/"
            f"{batch_id}.descriptor.json"
        )
        observation_path = descriptor_path.replace(
            ".descriptor.json", ".observation.json"
        )
        package_path = (
            f"{artifact_root}/provider-runtime/{gate_id}/package-identity.json"
        )
        package_binding = {
            "path": package_path,
            "sha256": write_json_evidence(
                root,
                package_path,
                {
                    "status": "Passed",
                    "productCandidate": first["productCandidate"],
                },
            ),
        }
        attempt_entries = [
            entry
            for entry in entries
            if entry["gateId"] == gate_id
            and entry["attemptId"] == first["attemptId"]
        ]
        ordinal_by_id = {
            entry["reservationId"]: ordinal
            for ordinal, entry in enumerate(attempt_entries, start=1)
        }
        profile_key = (gate_id, first["attemptId"])
        profile_ordinal = None
        if phase == "provider-resource":
            profile_ordinal = resource_ordinals.get(profile_key, 0) + 1
            resource_ordinals[profile_key] = profile_ordinal
        controlled = None
        controlled_preauthorization = None
        if gate_id in {"W84-G8", "W92-G7"}:
            controlled = deepcopy(
                controlled_bundles[gate_id]["tombstoneBinding"]
            )
            controlled_preauthorization = deepcopy(
                controlled_bundles[gate_id]["preauthorizationBinding"]
            )
        descriptor = {
            "schemaVersion": validator.SCHEMA_VERSION,
            "protocol": validator.PROVIDER_DESCRIPTOR_PROTOCOL,
            "goalId": validator.GOAL_ID,
            "batchId": batch_id,
            "gateId": gate_id,
            "phase": phase,
            "profileOrdinal": profile_ordinal,
            "productCandidate": first["productCandidate"],
            "attemptId": first["attemptId"],
            "runId": first["runId"],
            "artifactRoot": artifact_root,
            "packageIdentityEvidence": package_binding,
            "scenarioPath": validator.PROVIDER_SCENARIO_PATHS[phase],
            "scenarioSource": scenario_binding_by_candidate_and_phase[
                (first["productCandidate"], phase)
            ],
            "controlledWriteTombstone": controlled,
            "controlledWritePreauthorization": controlled_preauthorization,
            "reservations": [
                {
                    "reservationId": entry["reservationId"],
                    "reservationSequence": entry["sequence"],
                    "turnOrdinal": ordinal_by_id[entry["reservationId"]],
                }
                for entry in batch_entries
            ],
        }
        descriptor_sha = write_json_evidence(root, descriptor_path, descriptor)
        requests = [
            {
                "reservationId": entry["reservationId"],
                "reservationSequence": entry["sequence"],
                "requestOrdinal": ordinal,
                "requestSha256": hashlib.sha256(
                    f"{entry['reservationId']}:request".encode("utf-8")
                ).hexdigest(),
                "status": "Succeeded",
            }
            for ordinal, entry in enumerate(batch_entries, start=1)
        ]
        observation = {
            "schemaVersion": validator.SCHEMA_VERSION,
            "protocol": validator.PROVIDER_OBSERVED_REQUEST_PROTOCOL,
            "batchId": batch_id,
            "gateId": gate_id,
            "phase": phase,
            "productCandidate": first["productCandidate"],
            "attemptId": first["attemptId"],
            "runId": first["runId"],
            "packageIdentityEvidence": package_binding,
            "requests": requests,
        }
        observation_sha = write_json_evidence(
            root, observation_path, observation
        )
        argument_values = {
            "{sourcePath}": validator.TRUSTED_PROVIDER_HARNESS_PATH,
            "{descriptorPath}": descriptor_path,
            "{observationPath}": observation_path,
        }
        arguments = [
            argument_values.get(argument, argument)
            for argument in validator.trusted_executor._PROVIDER_TEMPLATE_ARGUMENTS
        ]
        argv_sha = hashlib.sha256(
            validator._json_bytes(
                {
                    "executableRole": validator.ISOLATED_PYTHON_EXECUTABLE_ROLE,
                    "arguments": arguments,
                }
            )
        ).hexdigest()
        batch_ordinal = batch_ordinals_by_gate.get(gate_id, 0)
        batch_ordinals_by_gate[gate_id] = batch_ordinal + 1
        started_at = fractional_timestamp(
            PROVIDER_GATE_TIMES[gate_id]["reservedAt"],
            batch_ordinal * 100000 + 25000,
        )
        finished_at = fractional_timestamp(
            PROVIDER_GATE_TIMES[gate_id]["reservedAt"],
            batch_ordinal * 100000 + 75000,
        )
        for entry, request in zip(batch_entries, requests, strict=True):
            event = {
                "sequence": len(runtime_events) + 1,
                "eventType": "ObservedProviderRequest",
                "reservationId": entry["reservationId"],
                "reservationSequence": entry["sequence"],
                "batchId": batch_id,
                "batchSize": batch_size,
                "gateId": gate_id,
                "phase": phase,
                "productCandidate": entry["productCandidate"],
                "attemptId": entry["attemptId"],
                "runId": entry["runId"],
                "commandPolicy": (
                    {
                        "policyId": validator.TRUSTED_PROVIDER_POLICY_ID,
                        "sha256": policy_sha,
                    }
                    if batch_succeeded
                    else None
                ),
                "argvSha256": argv_sha if batch_succeeded else None,
                "harnessSource": (
                    harness_binding_by_candidate[entry["productCandidate"]]
                    if batch_succeeded
                    else None
                ),
                "descriptor": (
                    {"path": descriptor_path, "sha256": descriptor_sha}
                    if batch_succeeded
                    else None
                ),
                "observation": (
                    {
                        "path": observation_path,
                        "sha256": observation_sha,
                        "status": "Accepted",
                    }
                    if batch_succeeded
                    else None
                ),
                "observedRequest": request if batch_succeeded else None,
                "checkoutIdentity": (
                    checkout_by_gate[gate_id] if batch_succeeded else None
                ),
                "startedAt": started_at,
                "finishedAt": finished_at,
                "exitCode": 0 if batch_succeeded else 127,
                "outcome": "Succeeded" if batch_succeeded else "Failed",
                "previousEventSha256": previous_runtime_hash,
            }
            event["eventSha256"] = validator.provider_runtime_event_sha256(event)
            previous_runtime_hash = event["eventSha256"]
            runtime_events.append(event)
        cursor += batch_size
    journal = {
        "schemaVersion": validator.SCHEMA_VERSION,
        "journalVersion": validator.PROVIDER_RUNTIME_JOURNAL_VERSION,
        "goalId": validator.GOAL_ID,
        "eventCount": len(runtime_events),
        "lastEventSha256": previous_runtime_hash,
        "events": runtime_events,
    }
    runtime_sha = write_json_evidence(
        root, validator.PROVIDER_RUNTIME_JOURNAL_PATH, journal
    )
    provider.update(
        {
            "runtimeJournalPath": validator.PROVIDER_RUNTIME_JOURNAL_PATH,
            "runtimeJournalSha256": runtime_sha,
            "runtimeEventCount": len(runtime_events),
            "lastRuntimeEventSha256": previous_runtime_hash,
        }
    )
    return journal


def write_provider_ledger_bindings(
    root: Path, fixture: dict
) -> dict[str, tuple[str, dict]]:
    runtime_path = root / validator.PROVIDER_RUNTIME_JOURNAL_PATH
    runtime_journal = (
        json.loads(runtime_path.read_text(encoding="utf-8"))
        if runtime_path.is_file()
        else prepare_provider_runtime_journal(root, fixture)
    )
    ledger_data = fixture["ledger"].data
    written: dict[str, tuple[str, dict]] = {}
    for gate_id in sorted(validator.PROVIDER_LEDGER_BINDING_GATE_IDS):
        gate = fixture_gate(fixture, gate_id).data
        authorization = gate["authorization"]
        projection = provider_prefix_projection(
            ledger_data, authorization["providerSequenceAfter"]
        )
        evidence = next(
            item
            for item in gate["evidence"]
            if Path(item["path"]).name
            == validator.PROVIDER_LEDGER_BINDING_BASENAME
        )
        artifact_root = Path(gate["resultPath"]).parent.parent.as_posix()
        relative_path = (
            f"{artifact_root}/provider-ledger-bindings/{gate_id}/"
            f"{validator.PROVIDER_LEDGER_BINDING_BASENAME}"
        )
        evidence["path"] = relative_path
        binding = {
            "schemaVersion": validator.SCHEMA_VERSION,
            "goalId": validator.GOAL_ID,
            "gateId": gate_id,
            "productCandidate": gate["identity"]["productCandidate"],
            "status": gate["status"],
            "ledgerPath": validator.LEDGER_PATH,
            "providerSequenceBefore": authorization[
                "providerSequenceBefore"
            ],
            "providerSequenceAfter": authorization[
                "providerSequenceAfter"
            ],
            **projection,
            "runtimeJournalPath": validator.PROVIDER_RUNTIME_JOURNAL_PATH,
            "runtimeEventCount": authorization["providerSequenceAfter"],
            "lastRuntimeEventSha256": (
                runtime_journal["events"][
                    authorization["providerSequenceAfter"] - 1
                ]["eventSha256"]
                if authorization["providerSequenceAfter"]
                else None
            ),
            "runtimePrefixSha256": validator.provider_runtime_prefix_sha256(
                runtime_journal["events"],
                authorization["providerSequenceAfter"],
            ),
        }
        evidence["sha256"] = write_json_evidence(
            root, relative_path, binding
        )
        authorization["providerLedgerPrefixSha256"] = projection[
            "combinedPrefixSha256"
        ]
        written[gate_id] = (relative_path, binding)
    return written


def rehash_provider_runtime_journal(root: Path, fixture: dict) -> dict:
    runtime_path = root / validator.PROVIDER_RUNTIME_JOURNAL_PATH
    runtime = json.loads(runtime_path.read_text(encoding="utf-8"))
    previous_hash = None
    for sequence, event in enumerate(runtime["events"], start=1):
        event["sequence"] = sequence
        event["previousEventSha256"] = previous_hash
        event["eventSha256"] = validator.provider_runtime_event_sha256(event)
        previous_hash = event["eventSha256"]
    runtime["eventCount"] = len(runtime["events"])
    runtime["lastEventSha256"] = previous_hash
    runtime_sha = write_json_evidence(
        root, validator.PROVIDER_RUNTIME_JOURNAL_PATH, runtime
    )
    provider = fixture["goal"].data["authorization"]["provider"]
    provider.update(
        {
            "runtimeJournalPath": validator.PROVIDER_RUNTIME_JOURNAL_PATH,
            "runtimeJournalSha256": runtime_sha,
            "runtimeEventCount": len(runtime["events"]),
            "lastRuntimeEventSha256": previous_hash,
        }
    )
    write_provider_ledger_bindings(root, fixture)
    return runtime


def rehash_provider_entries(ledger_data: dict) -> None:
    previous = None
    for entry in ledger_data["entries"]:
        entry["previousEntrySha256"] = previous
        entry["entrySha256"] = validator.provider_entry_sha256(entry)
        previous = entry["entrySha256"]


def _attempt_outcomes(ledger_data: dict) -> dict[str, str]:
    return {
        event["attemptId"]: event["outcome"]
        for event in ledger_data.get("attemptEvents", [])
        if isinstance(event, dict)
        and event.get("eventType") == "AttemptFinished"
        and isinstance(event.get("attemptId"), str)
        and event.get("outcome") in validator.PROVIDER_ATTEMPT_OUTCOMES
    }


def rebuild_provider_attempt_events(
    ledger_data: dict,
    outcomes: dict[str, str] | None = None,
    open_attempts: set[str] | None = None,
) -> None:
    resolved_outcomes = _attempt_outcomes(ledger_data)
    resolved_outcomes.update(outcomes or {})
    open_ids = open_attempts or set()
    entries = ledger_data["entries"]
    attempt_segments: list[list[dict]] = []
    for entry in entries:
        if (
            not attempt_segments
            or attempt_segments[-1][-1]["attemptId"] != entry["attemptId"]
            or attempt_segments[-1][-1]["gateId"] != entry["gateId"]
        ):
            attempt_segments.append([entry])
        else:
            attempt_segments[-1].append(entry)

    events: list[dict] = []
    previous_hash = None
    finished_ids: set[str] = set()
    gate_segment_counts: dict[str, int] = {}
    for segment in attempt_segments:
        attempt_id = segment[0]["attemptId"]
        gate_id = segment[0]["gateId"]
        segment_ordinal = gate_segment_counts.get(gate_id, 0)
        gate_segment_counts[gate_id] = segment_ordinal + 1
        completion_time = fractional_timestamp(
            PROVIDER_GATE_TIMES[gate_id]["completedAt"],
            segment_ordinal * 2,
        )
        finish_time = fractional_timestamp(
            PROVIDER_GATE_TIMES[gate_id]["completedAt"],
            segment_ordinal * 2 + 1,
        )
        outcome = resolved_outcomes.get(attempt_id, "Passed")
        for entry in segment:
            event = {
                "attemptEventSequence": len(events) + 1,
                "eventType": "TurnCompleted",
                "reservationId": entry["reservationId"],
                "outcome": "Succeeded" if outcome == "Passed" else "Failed",
                "completedAt": completion_time,
                "previousAttemptEventSha256": previous_hash,
            }
            event["attemptEventSha256"] = validator.provider_attempt_event_sha256(event)
            previous_hash = event["attemptEventSha256"]
            events.append(event)
        if attempt_id in open_ids or attempt_id in finished_ids:
            continue
        finished_ids.add(attempt_id)
        event = {
            "attemptEventSequence": len(events) + 1,
            "eventType": "AttemptFinished",
            "gateId": segment[0]["gateId"],
            "productCandidate": segment[0]["productCandidate"],
            "attemptId": attempt_id,
            "outcome": outcome,
            "reservationSequenceStart": segment[0]["sequence"],
            "reservationSequenceEnd": segment[-1]["sequence"],
            "finishedAt": finish_time,
            "previousAttemptEventSha256": previous_hash,
        }
        event["attemptEventSha256"] = validator.provider_attempt_event_sha256(event)
        previous_hash = event["attemptEventSha256"]
        events.append(event)
    ledger_data["attemptEvents"] = events
    ledger_data["attemptEventCount"] = len(events)
    ledger_data["lastAttemptEventSha256"] = previous_hash


def rehash_provider_attempt_events(fixture: dict) -> None:
    ledger_data = fixture["ledger"].data
    previous_hash = None
    for sequence, event in enumerate(ledger_data["attemptEvents"], start=1):
        event["attemptEventSequence"] = sequence
        event["previousAttemptEventSha256"] = previous_hash
        event["attemptEventSha256"] = validator.provider_attempt_event_sha256(event)
        previous_hash = event["attemptEventSha256"]
    ledger_data["attemptEventCount"] = len(ledger_data["attemptEvents"])
    ledger_data["lastAttemptEventSha256"] = previous_hash
    fixture["ledger"] = validator.document_from_data(
        validator.LEDGER_PATH, ledger_data
    )
    provider = fixture["goal"].data["authorization"]["provider"]
    provider["ledgerSha256"] = fixture["ledger"].sha256
    provider["attemptEventCount"] = len(ledger_data["attemptEvents"])
    provider["lastAttemptEventSha256"] = previous_hash
    for gate_document in fixture["gates"]:
        authorization = gate_document.data["authorization"]
        try:
            projection = provider_prefix_projection(
                ledger_data, authorization["providerSequenceAfter"]
            )
        except AssertionError:
            # An intentionally invalid global event order has no canonical
            # Gate prefix.  Preserve the old binding so semantic validation
            # can report the journal-order defect under test.
            continue
        authorization["providerLedgerPrefixSha256"] = projection[
            "combinedPrefixSha256"
        ]


def rebuild_provider_accounting(
    fixture: dict,
    outcomes: dict[str, str] | None = None,
    open_attempts: set[str] | None = None,
) -> None:
    ledger_data = fixture["ledger"].data
    entries = ledger_data["entries"]
    for sequence, entry in enumerate(entries, start=1):
        entry["sequence"] = sequence
    rehash_provider_entries(ledger_data)
    rebuild_provider_attempt_events(ledger_data, outcomes, open_attempts)

    used = len(entries)
    ledger_data.update(
        {
            "journalVersion": validator.PROVIDER_JOURNAL_VERSION,
            "maxTurns": validator.PROVIDER_BUDGET,
            "usedTurns": used,
            "remainingTurns": validator.PROVIDER_BUDGET - used,
            "ledgerSequence": used,
        }
    )
    fixture["ledger"] = validator.document_from_data(
        validator.LEDGER_PATH, ledger_data
    )

    provider = fixture["goal"].data["authorization"]["provider"]
    provider.update(
        {
            "maxTurns": validator.PROVIDER_BUDGET,
            "usedTurns": used,
            "remainingTurns": validator.PROVIDER_BUDGET - used,
            "ledgerSequence": used,
            "ledgerSha256": fixture["ledger"].sha256,
            "attemptEventCount": ledger_data["attemptEventCount"],
            "lastAttemptEventSha256": ledger_data["lastAttemptEventSha256"],
        }
    )

    cursor = 0
    for gate_id in validator.CANONICAL_GATE_IDS:
        gate_entries = [entry for entry in entries if entry["gateId"] == gate_id]
        consumed = len(gate_entries)
        authorization = fixture_gate(fixture, gate_id).data["authorization"]
        prefix_projection = provider_prefix_projection(
            ledger_data, cursor + consumed
        )
        authorization.update(
            {
                "providerTurnsBefore": cursor,
                "providerTurnsConsumed": consumed,
                "providerTurnsAfter": cursor + consumed,
                "providerSequenceBefore": cursor,
                "providerSequenceAfter": cursor + consumed,
                "providerLedgerPrefixSha256": prefix_projection[
                    "combinedPrefixSha256"
                ],
            }
        )
        cursor += consumed


def insert_provider_attempts(
    fixture: dict,
    gate_id: str,
    attempts: list[tuple[str, str, list[str]]],
) -> None:
    entries = fixture["ledger"].data["entries"]
    insertion_index = next(
        index for index, entry in enumerate(entries) if entry["gateId"] == gate_id
    )
    inserted = []
    outcomes = {}
    for attempt_id, outcome, phases in attempts:
        outcomes[attempt_id] = outcome
        for offset, phase in enumerate(phases, start=1):
            inserted.append(
                {
                    "sequence": 0,
                    "eventType": "TurnReserved",
                    "reservationId": f"{attempt_id}-reservation-{offset}",
                    "gateId": gate_id,
                    "phase": phase,
                    "productCandidate": PRODUCT_CANDIDATE,
                    "attemptId": attempt_id,
                    "runId": f"{attempt_id}-run-{offset}",
                    "reservedAt": PROVIDER_GATE_TIMES[gate_id]["reservedAt"],
                    "previousEntrySha256": None,
                }
            )
    entries[insertion_index:insertion_index] = inserted
    rebuild_provider_accounting(fixture, outcomes)


def provider_accounting_problems(
    fixture: dict, repo_root: Path | None = None
) -> list[str]:
    problems = validator.Problems()
    validator.validate_provider_accounting(
        fixture["goal"].data,
        {
            document.data["gateId"]: document
            for document in fixture["gates"]
        },
        [],
        fixture["ledger"],
        complete=False,
        problems=problems,
        repo_root=repo_root,
    )
    return problems.items


def initial_active_goal() -> dict:
    return {
        "status": "Active",
        "execution": {
            "activeCheckpoint": "W84",
            "activeLanes": ["baseline"],
            "weekStates": {
                f"W{week}": {
                    "entryGate": "NotRun",
                    "exitGate": "NotRun",
                    "handoffs": [],
                    "review": None,
                }
                for week in range(84, 93)
            },
            "laneStates": {
                lane: {
                    "state": "Active" if lane == "baseline" else "NotStarted",
                    "lastHandoff": None,
                }
                for lane in (
                    "baseline",
                    "renderer",
                    "cli",
                    "integration",
                    "hardening",
                    "acceptance",
                )
            },
        },
    }


def active_progression_problems(
    fixture: dict,
    gate_ids: tuple[str, ...] = (),
    *,
    goal: dict | None = None,
    sealed_group_ids: set[str] | None = None,
    handoffs: tuple[validator.Document, ...] = (),
) -> list[str]:
    problems = validator.Problems()
    validator.validate_active_progression(
        goal if goal is not None else initial_active_goal(),
        fixture["contract"],
        {
            gate_id: fixture_gate(fixture, gate_id)
            for gate_id in gate_ids
        },
        list(handoffs),
        sealed_group_ids or set(),
        None,
        problems,
        "artifacts/week84-92-goal-control/goal-state.json",
    )
    return problems.items


def valid_controlled_write_receipt(gate_id: str, week: int) -> dict:
    fixture = make_fixture()
    group = fixture["contract"].gate_to_group[gate_id]
    prerequisite_ids = list(group.gate_ids[: group.gate_ids.index(gate_id)])
    gates_by_id = {document.data["gateId"]: document for document in fixture["gates"]}
    successful_entries = [
        entry
        for entry in fixture["ledger"].data["entries"]
        if entry["gateId"] == gate_id
        and entry["attemptId"] == f"{gate_id}-attempt-1"
        and entry["phase"] == "controlled-write"
    ]
    time_prefix = "2026-07-28T00:00:21" if week == 84 else "2026-07-28T00:00:31"
    issued_at = "2026-07-28T00:00:20.500000Z" if week == 84 else "2026-07-28T00:00:30.500000Z"
    return {
        "status": "Passed",
        "gateId": gate_id,
        "week": week,
        "productCandidate": PRODUCT_CANDIDATE,
        "workspaceName": f"caicli-week{week}-write-test",
        "harnessOwned": True,
        "onlyFile": "result.txt",
        "files": ["result.txt"],
        "fromValue": "fail",
        "toValue": "pass",
        "validationCommand": "dotnet msbuild Week82Gate.proj -target:Test -nologo",
        "validationCommandCount": 1,
        "dotnetSdk": "9.0.308",
        "turnsConsumed": 1,
        "runId": f"{gate_id}-controlled-write-run",
        "attemptId": f"{gate_id}-attempt-1",
        "phase": "controlled-write",
        "sequenceStart": successful_entries[0]["sequence"],
        "sequenceEnd": successful_entries[-1]["sequence"],
        "reservationIds": [
            entry["reservationId"] for entry in successful_entries
        ],
        "writeStartedAt": f"{time_prefix}.500000Z",
        "singleUseLease": {
            "leaseId": f"CW-W{week}-ABCDEFGH",
            "gateId": gate_id,
            "productCandidate": PRODUCT_CANDIDATE,
            "status": "Consumed",
            "issuedAt": issued_at,
            "consumedAt": f"{time_prefix}.250000Z",
            "executionCount": 1,
            "nonceSha256": ("d" if week == 84 else "e") * 64,
        },
        "preconditionGateBindings": [
            {
                "gateId": prerequisite_id,
                "productCandidate": gates_by_id[prerequisite_id].data[
                    "identity"
                ]["productCandidate"],
                "resultPath": gates_by_id[prerequisite_id].relative_path,
                "resultSha256": gates_by_id[prerequisite_id].sha256,
                "status": "Passed",
                "finishedAt": gates_by_id[prerequisite_id].data["finishedAt"],
            }
            for prerequisite_id in prerequisite_ids
        ],
        "durableApprovalCount": 2,
        "durableApprovals": [
            {
                "approvalId": f"approval-w{week}-patch",
                "decision": "Approve",
                "action": "apply_patch",
                "durable": True,
                "usedOnce": True,
                "replayed": False,
            },
            {
                "approvalId": f"approval-w{week}-shell",
                "decision": "Approve",
                "action": "shell",
                "durable": True,
                "usedOnce": True,
                "replayed": False,
            },
        ],
        "nonWritePreconditionsPassed": True,
        "executionCount": 1,
        "controlledWriteTombstone": {
            "path": (
                f"{validator.CONTROLLED_WRITE_TOMBSTONE_DIR}/{gate_id}.json"
            ),
            "sha256": "f" * 64,
            "commit": "1" * 40,
        },
        "cleanup": {
            "status": "Passed",
            "ownedProcessesRemaining": 0,
            "ownedTempPathsRemaining": 0,
            "configMutationsRemaining": 0,
            "residueCount": 0,
        },
    }


def prepare_controlled_write_tombstone(
    root: Path,
    gate_id: str = "W84-G8",
    week: int = 84,
) -> tuple[dict, dict[str, validator.Document], str]:
    _git(
        root,
        "init",
        "--quiet",
        "--initial-branch=codex/week84-92-refactor",
    )
    seed_path = root / "fixture-seed.txt"
    seed_path.write_text("controlled fixture seed\n", encoding="utf-8")
    prerequisite_candidate = _commit_fixture_paths(
        root, "controlled fixture seed", "fixture-seed.txt"
    )
    weekly_attributes = root / "docs_md/weekly/.gitattributes"
    weekly_attributes.parent.mkdir(parents=True, exist_ok=True)
    weekly_attributes.write_text(
        (
            "84_92_controlled_write_tombstones/*.json -text\n"
            "84_92_controlled_write_authorizations/*/*.json -text\n"
        ),
        encoding="utf-8",
    )
    manifest_path = root / validator.GATE_REQUIREMENTS_PATH
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_bytes(
        validator._json_bytes(requirements_manifest_document().data)
    )
    times = _controlled_bundle_times(gate_id)
    gate_by_id = _write_executor_prerequisite_results(
        root,
        gate_id,
        prerequisite_candidate,
        times["prerequisiteFinishedAt"],
    )
    candidate = _commit_fixture_paths(
        root,
        "controlled product candidate",
        "docs_md/weekly/.gitattributes",
        validator.GATE_REQUIREMENTS_PATH,
        *(item.relative_path for item in gate_by_id.values()),
    )
    bundle = _write_controlled_authorization_bundle(
        root,
        gate_id,
        candidate,
        gate_by_id,
    )
    controlled_data = deepcopy(fixture_gate(make_fixture(), gate_id).data)
    controlled_data["identity"].update(
        {
            "sourceHead": candidate,
            "productCandidate": candidate,
            "checkpointCommit": candidate,
        }
    )
    gate_by_id[gate_id] = validator.document_from_data(
        controlled_data["resultPath"], controlled_data
    )

    receipt = valid_controlled_write_receipt(gate_id, week)
    receipt["productCandidate"] = candidate
    receipt["singleUseLease"]["productCandidate"] = candidate
    receipt["singleUseLease"]["leaseId"] = bundle["tombstone"]["leaseId"]
    receipt["singleUseLease"]["nonceSha256"] = bundle["tombstone"][
        "nonceSha256"
    ]
    receipt["singleUseLease"]["issuedAt"] = bundle["tombstone"]["issuedAt"]
    receipt["singleUseLease"]["consumedAt"] = bundle["tombstone"][
        "consumedAt"
    ]
    receipt["preconditionGateBindings"] = deepcopy(
        bundle["preconditionGateBindings"]
    )
    receipt["controlledWriteTombstone"] = deepcopy(
        bundle["tombstoneBinding"]
    )
    return receipt, gate_by_id, candidate


def w91_checklist_payload(
    checklist_ids: tuple[str, ...],
    candidate: str = PRODUCT_CANDIDATE,
) -> dict:
    return {
        "checklistRevision": validator.W91_CHECKLIST_REVISION,
        "productCandidate": candidate,
        "performedBy": "GoalTestOperator",
        "status": "Passed",
        "items": [
            {
                "checklistId": checklist_id,
                "status": "Passed",
                "observations": f"Observed {checklist_id} on the exact candidate.",
                "observedAt": "2026-07-28T00:00:00.500000Z",
                "issuePointers": [],
            }
            for checklist_id in checklist_ids
        ],
    }


def prepare_w91_operator_evidence(
    root: Path,
) -> tuple[dict, validator.Document]:
    fixture = make_fixture()
    document = fixture_gate(fixture, "W91-G2")
    by_name = {
        Path(item["path"]).name: item for item in document.data["evidence"]
    }
    for basename, checklist_ids in (
        ("narrator-manual.json", validator.W91_NARRATOR_CHECKLIST_IDS),
        (
            "operator-ux-attestation.json",
            validator.W91_OPERATOR_UX_CHECKLIST_IDS,
        ),
    ):
        evidence = by_name[basename]
        evidence["sha256"] = write_json_evidence(
            root,
            evidence["path"],
            w91_checklist_payload(checklist_ids),
        )
    return fixture, document


def png_chunk(chunk_type: bytes, payload: bytes) -> bytes:
    return (
        validator.struct.pack(">I", len(payload))
        + chunk_type
        + payload
        + validator.struct.pack(
            ">I", validator.zlib.crc32(chunk_type + payload) & 0xFFFFFFFF
        )
    )


def minimal_png(*, metadata_chunks: tuple[bytes, ...] = ()) -> bytes:
    ihdr = validator.struct.pack(">IIBBBBB", 1, 1, 8, 2, 0, 0, 0)
    idat = validator.zlib.compress(b"\x00\x00\x00\x00")
    return (
        b"\x89PNG\r\n\x1a\n"
        + png_chunk(b"IHDR", ihdr)
        + b"".join(metadata_chunks)
        + png_chunk(b"IDAT", idat)
        + png_chunk(b"IEND", b"")
    )


def prepare_w92_visual_manifest(
    root: Path,
) -> tuple[dict, validator.Document, dict]:
    fixture = make_fixture()
    document = fixture_gate(fixture, "W92-G9")
    files = []
    screenshots = []
    for viewport in validator.W92_VISUAL_VIEWPORTS:
        viewport_slug = viewport.replace("x", "-")
        for fixture_name in validator.W92_VISUAL_FIXTURES:
            for artifact_type in validator.W92_VISUAL_ARTIFACT_TYPES:
                suffix = "png" if artifact_type == "screenshot" else "json"
                basename = (
                    f"{fixture_name}-{viewport_slug}-{artifact_type}.{suffix}"
                )
                relative = f"artifacts/week92-refactor-acceptance/visual/{basename}"
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                if artifact_type == "screenshot":
                    raw = minimal_png(
                        metadata_chunks=(
                            png_chunk(
                                b"tEXt",
                                b"case\0"
                                + f"{viewport}:{fixture_name}".encode("ascii"),
                            ),
                        )
                    )
                else:
                    raw = validator._json_bytes(
                        {
                            "productCandidate": PRODUCT_CANDIDATE,
                            "viewport": viewport,
                            "fixture": fixture_name,
                            "artifactType": artifact_type,
                            "snapshot": {"nodeCount": 1},
                        }
                    )
                path.write_bytes(raw)
                sha = hashlib.sha256(raw).hexdigest()
                files.append(
                    {
                        "path": relative,
                        "sha256": sha,
                        "redacted": True,
                        "artifactType": artifact_type,
                        "viewport": viewport,
                        "fixture": fixture_name,
                    }
                )
                if artifact_type == "screenshot":
                    screenshots.append(
                        {
                            "evidenceId": f"W92-G9-shot-{len(screenshots) + 1}",
                            "kind": "screenshot",
                            "path": relative,
                            "sha256": sha,
                            "redacted": True,
                            "preservesFirstFailure": False,
                        }
                    )
    document.data["evidence"] = [
        item
        for item in document.data["evidence"]
        if item["kind"] != "screenshot"
    ] + screenshots
    anchor_relative = "docs_md/weekly/84_92_evidence_anchors/w91-hardening.json"
    anchor_path = root / anchor_relative
    anchor_path.parent.mkdir(parents=True, exist_ok=True)
    anchor_raw = validator._json_bytes({"groupId": "w91-hardening"})
    anchor_path.write_bytes(anchor_raw)
    w91_document = fixture_gate(fixture, "W91-G2")
    w91_by_name = {
        Path(item["path"]).name: item
        for item in w91_document.data["evidence"]
    }
    manifest = {
        "schemaVersion": validator.SCHEMA_VERSION,
        "manifestRole": "week92-final-user-visual",
        "gateId": "W92-G9",
        "productCandidate": PRODUCT_CANDIDATE,
        "viewports": list(validator.W92_VISUAL_VIEWPORTS),
        "fixtures": list(validator.W92_VISUAL_FIXTURES),
        "redactionAttestation": {
            "status": "Passed",
            "reviewedBy": "GoalTestOperator",
            "secretsVisible": False,
            "absolutePathsVisible": False,
            "reviewedAt": "2026-07-28T00:00:00.500000Z",
        },
        "files": files,
        "w91EvidenceBindings": [
            {
                "path": w91_by_name[basename]["path"],
                "sha256": w91_by_name[basename]["sha256"],
            }
            for basename in (
                "narrator-manual.json",
                "operator-ux-attestation.json",
            )
        ]
        + [
            {
                "path": anchor_relative,
                "sha256": hashlib.sha256(anchor_raw).hexdigest(),
            }
        ],
    }
    return fixture, document, manifest


class Week84To92GoalEvidenceTests(unittest.TestCase):
    def assert_problem(self, errors: list[str], code: str) -> None:
        self.assertTrue(
            any(item.startswith(f"[{code}]") for item in errors),
            f"missing {code} in:\n" + "\n".join(errors),
        )

    @staticmethod
    def _overlay_documents(
        phase: str,
        product_candidate: str,
    ) -> tuple[dict[str, bytes], dict[str, bytes | None]]:
        goal_path = validator.trusted_executor.GOAL_STATE_PATH
        old_goal = {
            "identity": {"productCandidate": product_candidate},
            "status": "Active",
            "updatedAt": "2026-07-28T00:00:00Z",
            "execution": {
                "activeCheckpoint": "W84",
                "activeLanes": ["baseline"],
            },
        }
        if phase == "initial":
            documents = {
                path: validator._json_bytes({})
                for path in validator.W84_G0_INITIAL_OVERLAY_PATHS
            }
            documents[goal_path] = validator._json_bytes(old_goal)
            return documents, {path: None for path in documents}

        new_goal = deepcopy(old_goal)
        new_goal["updatedAt"] = "2026-07-28T00:00:02Z"
        gate = {
            "gateId": "W84-G0",
            "resultPath": validator.W84_G0_GATE_PATH,
            "status": "Passed",
            "identity": {"productCandidate": product_candidate},
            "startedAt": "2026-07-28T00:00:01Z",
            "finishedAt": "2026-07-28T00:00:02Z",
        }
        documents = {
            path: validator._json_bytes({})
            for path in validator.W84_G0_FINAL_OVERLAY_PATHS
        }
        documents[goal_path] = validator._json_bytes(new_goal)
        documents[validator.W84_G0_GATE_PATH] = validator._json_bytes(gate)
        preimages: dict[str, bytes | None] = {path: None for path in documents}
        preimages[goal_path] = validator._json_bytes(old_goal)
        return documents, preimages

    def _run_overlay_fixture(
        self,
        phase: str,
        *,
        documents: dict[str, bytes] | None = None,
        preimages: dict[str, bytes | None] | None = None,
        live_overrides: dict[str, bytes] | None = None,
        semantic_codes: tuple[str, ...] = (),
        requirement_codes: tuple[str, ...] = (),
        repository_snapshots: tuple[
            tuple[bytes, bytes, bytes, bytes],
            tuple[bytes, bytes, bytes, bytes],
        ]
        | None = None,
    ) -> dict:
        product_candidate = "b" * 40
        if documents is None or preimages is None:
            default_documents, default_preimages = self._overlay_documents(
                phase,
                product_candidate,
            )
            documents = documents or default_documents
            preimages = preimages or default_preimages

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            candidate = root / "candidate"
            control = root / "control"
            common = root / "git-common"
            candidate.mkdir()
            control.mkdir()
            common.mkdir()
            requirements = candidate / validator.GATE_REQUIREMENTS_PATH
            requirements.parent.mkdir(parents=True)
            requirements.write_bytes(validator._json_bytes({}))
            if phase == "final":
                prior_documents, _prior_preimages = self._overlay_documents(
                    "initial",
                    product_candidate,
                )
                for relative, raw in prior_documents.items():
                    if relative == validator.trusted_executor.GOAL_STATE_PATH:
                        continue
                    target = control.joinpath(*relative.split("/"))
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(raw)
            for relative, raw in preimages.items():
                if raw is None:
                    continue
                target = control.joinpath(*relative.split("/"))
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(raw)
            for relative, raw in (live_overrides or {}).items():
                target = control.joinpath(*relative.split("/"))
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(raw)

            def snapshot() -> dict[str, bytes]:
                return {
                    path.relative_to(control).as_posix(): path.read_bytes()
                    for path in control.rglob("*")
                    if path.is_file()
                }

            before = snapshot()
            repository_context = type(
                "OverlayRepositoryContext",
                (),
                {
                    "candidate_root": candidate,
                    "control_root": control,
                    "common_dir": common,
                },
            )()

            def fake_requirements(*args, **_kwargs):
                problems = args[4]
                for code in requirement_codes:
                    problems.add(code, "overlay-fixture", "injected failure")

            semantic_failures = [
                f"[{code}] overlay-fixture: injected failure"
                for code in semantic_codes
            ]
            git_identity = (candidate / "trusted-git.exe", {"sha256": "c" * 64})
            stable_snapshot = (b"head", b"refs", b"index", b"status")
            snapshot_values = repository_snapshots or (
                stable_snapshot,
                stable_snapshot,
            )
            with ExitStack() as stack:
                stack.enter_context(
                    patch.object(
                        validator.trusted_executor,
                        "_normalise_repo_root",
                        return_value=candidate,
                    )
                )
                stack.enter_context(
                    patch.object(
                        validator.trusted_executor,
                        "_repository_context",
                        return_value=repository_context,
                    )
                )
                stack.enter_context(
                    patch.object(
                        validator.trusted_executor,
                        "_trusted_git_identity",
                        return_value=git_identity,
                    )
                )
                stack.enter_context(
                    patch.object(validator, "validate_git_repository_trust", return_value=True)
                )
                stack.enter_context(
                    patch.object(validator, "validate_bootstrap_control_plane", return_value=True)
                )
                stack.enter_context(
                    patch.object(
                        validator,
                        "_git_repository_validation_snapshot",
                        side_effect=snapshot_values,
                    )
                )
                stack.enter_context(
                    patch.object(validator, "_git_stdout", return_value=product_candidate)
                )
                stack.enter_context(patch.object(validator, "_load_schemas", return_value={}))
                stack.enter_context(
                    patch.object(
                        validator,
                        "validate_gate_requirements_manifest",
                        side_effect=fake_requirements,
                    )
                )
                stack.enter_context(
                    patch.object(
                        validator,
                        "validate_semantics",
                        return_value=semantic_failures,
                    )
                )
                stack.enter_context(
                    patch.object(
                        validator.goal_integrity,
                        "validate_goal_integrity",
                        return_value=[],
                    )
                )
                stack.enter_context(
                    patch.object(
                        validator.evidence_anchor,
                        "validate_evidence_anchors",
                        return_value=[],
                    )
                )
                stack.enter_context(
                    patch.object(
                        validator,
                        "_validate_w84_g0_baseline_identity",
                        return_value=None,
                    )
                )
                result = dict(
                    validator.validate_w84_g0_overlay(
                        candidate_root=candidate,
                        control_root=control,
                        phase=phase,
                        product_candidate=product_candidate,
                        overlay_documents=documents,
                        expected_preimages=preimages,
                        transaction_id="d" * 32,
                    )
                )
            self.assertEqual(before, snapshot())
            self.assertEqual([], list(common.iterdir()))
            return result

    def test_w84_g0_overlay_api_is_non_cli_and_contract_exact(self) -> None:
        signature = inspect.signature(validator.validate_w84_g0_overlay)
        self.assertEqual(
            [
                "candidate_root",
                "control_root",
                "phase",
                "product_candidate",
                "overlay_documents",
                "expected_preimages",
                "transaction_id",
            ],
            list(signature.parameters),
        )
        self.assertTrue(
            all(
                parameter.kind is inspect.Parameter.KEYWORD_ONLY
                for parameter in signature.parameters.values()
            )
        )
        help_text = validator.build_parser().format_help()
        self.assertNotIn("overlay", help_text.casefold())
        self.assertNotIn("--control-root", help_text)

        with patch.object(
            validator,
            "_validate_w84_g0_overlay_impl",
            side_effect=KeyError("untrusted schema shape"),
        ):
            failed = validator.validate_w84_g0_overlay(
                candidate_root=Path("."),
                control_root=Path("."),
                phase="initial",
                product_candidate="b" * 40,
                overlay_documents={},
                expected_preimages={},
                transaction_id="d" * 32,
            )
        self.assertEqual(
            {
                "protocol",
                "ok",
                "phase",
                "productCandidate",
                "transactionId",
                "overlaySha256",
                "checks",
                "errors",
            },
            set(failed),
        )
        self.assertEqual(["OVERLAY_INTERNAL"], failed["errors"])

    def test_w84_g0_overlay_hash_matches_builder_algorithm(self) -> None:
        documents = {
            "z/path.json": b"z",
            "a/path.json": b"alpha",
        }
        digest = hashlib.sha256()
        digest.update(b"week84-g0-overlay-validation-v1\0")
        for path in sorted(documents, key=lambda value: value.encode("utf-8")):
            path_raw = path.encode("utf-8")
            raw = documents[path]
            digest.update(len(path_raw).to_bytes(8, "big"))
            digest.update(path_raw)
            digest.update(len(raw).to_bytes(8, "big"))
            digest.update(raw)
        self.assertEqual(
            digest.hexdigest(),
            validator._w84_g0_overlay_sha256(documents),
        )

    def test_w84_g0_overlay_accepts_legal_initial_and_final_views(self) -> None:
        for phase in ("initial", "final"):
            with self.subTest(phase=phase):
                result = self._run_overlay_fixture(phase)
                self.assertTrue(result["ok"], result)
                self.assertEqual(
                    {
                        "protocol",
                        "ok",
                        "phase",
                        "productCandidate",
                        "transactionId",
                        "overlaySha256",
                        "checks",
                        "errors",
                    },
                    set(result),
                )
                self.assertEqual(set(validator.W84_G0_OVERLAY_CHECKS), set(result["checks"]))
                self.assertTrue(all(result["checks"].values()))
                self.assertEqual([], result["errors"])

    def test_w84_g0_overlay_query_cache_is_request_scoped_and_drift_guarded(
        self,
    ) -> None:
        stable = (b"head", b"refs", b"index", b"status")
        drifted = (b"other-head", b"refs", b"index", b"status")
        result = self._run_overlay_fixture(
            "initial",
            repository_snapshots=(stable, drifted),
        )
        self.assertFalse(result["ok"])
        self.assertIn("OVERLAY_REPOSITORY_DRIFT", result["errors"])
        self.assertFalse(result["checks"]["gitProvenance"])
        self.assertIsNone(validator._ACTIVE_GIT_QUERY_CACHE.get())

        completed = subprocess.CompletedProcess(
            args=["git"],
            returncode=0,
            stdout="same-output\n",
            stderr="",
        )
        with (
            patch.object(
                validator,
                "_trusted_git_command",
                return_value=["git", "rev-parse", "HEAD"],
            ),
            patch.object(validator, "_trusted_git_unchanged", return_value=True),
            patch.object(validator.subprocess, "run", return_value=completed) as run,
        ):
            token = validator._ACTIVE_GIT_QUERY_CACHE.set({})
            try:
                self.assertEqual(
                    "same-output",
                    validator._git_stdout(Path("."), ("rev-parse", "HEAD")),
                )
                self.assertEqual(
                    "same-output",
                    validator._git_stdout(Path("."), ("rev-parse", "HEAD")),
                )
            finally:
                validator._ACTIVE_GIT_QUERY_CACHE.reset(token)
        self.assertEqual(1, run.call_count)
        self.assertIsNone(validator._ACTIVE_GIT_QUERY_CACHE.get())

    def test_w84_g0_overlay_accepts_only_exact_publication_prefix_order(self) -> None:
        product_candidate = "b" * 40
        for phase, paths in (
            ("initial", validator.W84_G0_INITIAL_OVERLAY_PATHS),
            ("final", validator.W84_G0_FINAL_OVERLAY_PATHS),
        ):
            documents, preimages = self._overlay_documents(phase, product_candidate)
            for prefix_length in range(len(paths) + 1):
                with self.subTest(phase=phase, prefix_length=prefix_length):
                    live = {
                        path: documents[path]
                        for path in paths[:prefix_length]
                    }
                    result = self._run_overlay_fixture(
                        phase,
                        documents=documents,
                        preimages=preimages,
                        live_overrides=live,
                    )
                    self.assertTrue(result["ok"], result)

            nonprefix = {paths[1]: documents[paths[1]]}
            rejected = self._run_overlay_fixture(
                phase,
                documents=documents,
                preimages=preimages,
                live_overrides=nonprefix,
            )
            self.assertFalse(rejected["ok"])
            self.assertIn("OVERLAY_PUBLICATION_ORDER", rejected["errors"])

    def test_w84_g0_overlay_rejects_unknown_path_secret_and_preimage_drift(self) -> None:
        product_candidate = "b" * 40
        documents, preimages = self._overlay_documents("initial", product_candidate)
        unknown_documents = dict(documents)
        unknown_documents["artifacts/unknown.json"] = validator._json_bytes({})
        with tempfile.TemporaryDirectory() as directory:
            formal_root = Path(directory)
            sentinel = formal_root / "formal.json"
            sentinel.write_bytes(b"formal-bytes\n")
            unknown_result = validator.validate_w84_g0_overlay(
                candidate_root=formal_root,
                control_root=formal_root,
                phase="initial",
                product_candidate=product_candidate,
                overlay_documents=unknown_documents,
                expected_preimages=preimages,
                transaction_id="d" * 32,
            )
            self.assertEqual(b"formal-bytes\n", sentinel.read_bytes())
        self.assertIn("OVERLAY_PATH_SET", unknown_result["errors"])

        secret_documents = dict(documents)
        secret_documents[validator.W84_G0_INITIAL_OVERLAY_PATHS[0]] = validator._json_bytes(
            {"apiKey": "sk-proj-abcdefghijklmnop"}
        )
        secret_result = validator.validate_w84_g0_overlay(
            candidate_root=Path("."),
            control_root=Path("."),
            phase="initial",
            product_candidate=product_candidate,
            overlay_documents=secret_documents,
            expected_preimages=preimages,
            transaction_id="d" * 32,
        )
        self.assertFalse(secret_result["ok"])
        self.assertIn("SECRET_DETECTED", secret_result["errors"])
        self.assertFalse(secret_result["checks"]["secretScan"])

        drift_path = validator.W84_G0_INITIAL_OVERLAY_PATHS[0]
        drift_result = self._run_overlay_fixture(
            "initial",
            live_overrides={drift_path: validator._json_bytes({"drift": True})},
        )
        self.assertFalse(drift_result["ok"])
        self.assertIn("OVERLAY_PREIMAGE_DRIFT", drift_result["errors"])

    def test_w84_g0_overlay_fails_closed_on_forged_semantics_and_transition(self) -> None:
        injections = (
            ("semantic-report", ("TEST_REPORT_SEMANTIC_SOURCES",), ()),
            ("runtime-tree", ("TEST_REPORT_RUNTIME_INPUT_TREE",), ()),
            ("summary", (), ("W84_G0_SUMMARY_SEMANTIC_REPORT",)),
        )
        for name, semantic_codes, requirement_codes in injections:
            with self.subTest(name=name):
                result = self._run_overlay_fixture(
                    "final",
                    semantic_codes=semantic_codes,
                    requirement_codes=requirement_codes,
                )
                self.assertFalse(result["ok"])
                self.assertFalse(result["checks"]["fullSemantics"])

        product_candidate = "b" * 40
        documents, preimages = self._overlay_documents("final", product_candidate)
        forged_gate = json.loads(documents[validator.W84_G0_GATE_PATH])
        forged_gate["status"] = "Failed"
        forged_gate_documents = dict(documents)
        forged_gate_documents[validator.W84_G0_GATE_PATH] = validator._json_bytes(
            forged_gate
        )
        gate_result = self._run_overlay_fixture(
            "final",
            documents=forged_gate_documents,
            preimages=preimages,
        )
        self.assertFalse(gate_result["ok"])
        self.assertIn("OVERLAY_GOAL_FINAL", gate_result["errors"])
        self.assertFalse(gate_result["checks"]["goalTransition"])

        forged_goal = json.loads(documents[validator.trusted_executor.GOAL_STATE_PATH])
        forged_goal["identity"]["productCandidate"] = "e" * 40
        forged_goal_documents = dict(documents)
        forged_goal_documents[
            validator.trusted_executor.GOAL_STATE_PATH
        ] = validator._json_bytes(forged_goal)
        goal_result = self._run_overlay_fixture(
            "final",
            documents=forged_goal_documents,
            preimages=preimages,
        )
        self.assertFalse(goal_result["ok"])
        self.assertIn("OVERLAY_GOAL_IDENTITY", goal_result["errors"])
        self.assertFalse(goal_result["checks"]["goalTransition"])

    def test_complete_fixture_is_semantically_valid(self) -> None:
        self.assertEqual([], validate(make_fixture()))

    def test_candidate_root_derives_unique_linked_control_worktree(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            candidate_root = base / "candidate"
            control_root = base / "control"
            candidate_root.mkdir()
            _git(candidate_root, "init", "--quiet")
            _git(
                candidate_root,
                "symbolic-ref",
                "HEAD",
                validator.trusted_executor.CONTROL_BRANCH_REF,
            )
            (candidate_root / "tracked.txt").write_text(
                "tracked\n", encoding="utf-8"
            )
            (candidate_root / ".gitignore").write_text(
                ".env.local\nartifacts/\n", encoding="utf-8"
            )
            _commit_fixture_paths(
                candidate_root, "bootstrap", "tracked.txt", ".gitignore"
            )
            _git(
                candidate_root,
                "switch",
                "--quiet",
                "-c",
                validator.trusted_executor.RENDERER_BRANCH_REF.removeprefix(
                    "refs/heads/"
                ),
            )
            _git(
                candidate_root,
                "worktree",
                "add",
                "--quiet",
                str(control_root),
                validator.trusted_executor.CONTROL_BRANCH_REF.removeprefix(
                    "refs/heads/"
                ),
            )
            try:
                problems = validator.Problems()
                roots = validator._derive_validation_roots(
                    str(candidate_root), problems
                )
                self.assertEqual([], problems.items)
                self.assertEqual(candidate_root.resolve(), roots.candidate_root)
                self.assertEqual(control_root.resolve(), roots.control_root)
                self.assertIsNotNone(roots.repository_context)
                trust_problems = validator.Problems()
                self.assertTrue(
                    validator.validate_git_repository_trust(
                        roots.candidate_root,
                        trust_problems,
                        roots.repository_context,
                    ),
                    trust_problems.items,
                )
                self.assertEqual([], trust_problems.items)
                self.assertNotIn(
                    "--control-root",
                    validator.build_parser().format_help(),
                )
            finally:
                _git(
                    candidate_root,
                    "worktree",
                    "remove",
                    "--force",
                    str(control_root),
                )

    def test_active_future_week_gate_requires_checkpoint_barrier(self) -> None:
        fixture = make_fixture()
        errors = active_progression_problems(fixture, ("W92-G0",))
        self.assert_problem(errors, "PROGRESS_CHECKPOINT_BARRIER")

    def test_active_group_cannot_start_without_parent_anchor(self) -> None:
        fixture = make_fixture()
        errors = active_progression_problems(fixture, ("W85-R0",))
        self.assert_problem(errors, "PROGRESS_PARENT_ANCHOR")

    def test_active_group_gate_files_must_be_contiguous_prefix(self) -> None:
        fixture = make_fixture()
        errors = active_progression_problems(fixture, ("W84-G1",))
        self.assert_problem(errors, "PROGRESS_GATE_PREFIX")

    def test_active_central_frontier_matches_lowest_unsealed_group(self) -> None:
        fixture = make_fixture()
        goal = initial_active_goal()
        goal["execution"]["activeCheckpoint"] = "W85"
        goal["execution"]["activeLanes"] = ["renderer", "cli"]
        errors = active_progression_problems(fixture, goal=goal)
        self.assert_problem(errors, "PROGRESS_ACTIVE_CHECKPOINT")
        self.assert_problem(errors, "PROGRESS_ACTIVE_LANES")

    def test_partially_sealed_lane_remains_active_at_parallel_frontier(self) -> None:
        fixture = make_fixture()
        goal = initial_active_goal()
        execution = goal["execution"]
        execution["activeCheckpoint"] = "W85"
        execution["activeLanes"] = ["cli"]

        w84_handoff = fixture_handoff(fixture, 84, "baseline")
        renderer_handoff = fixture_handoff(fixture, 85, "renderer")
        execution["weekStates"]["W84"].update(
            {
                "handoffs": [w84_handoff.relative_path],
                "review": "Reviewed",
            }
        )
        execution["weekStates"]["W85"]["handoffs"] = [
            renderer_handoff.relative_path
        ]
        execution["laneStates"]["baseline"].update(
            {
                "state": "Complete",
                "lastHandoff": w84_handoff.relative_path,
            }
        )
        execution["laneStates"]["renderer"].update(
            {
                "state": "Active",
                "lastHandoff": renderer_handoff.relative_path,
            }
        )
        execution["laneStates"]["cli"]["state"] = "Active"

        errors = active_progression_problems(
            fixture,
            goal=goal,
            sealed_group_ids={"w84-baseline", "w85-renderer"},
            handoffs=(w84_handoff, renderer_handoff),
        )
        self.assertEqual([], errors)

    def test_w91_operator_hardening_is_not_a_user_acceptance(self) -> None:
        fixture = make_fixture()
        self.assertNotIn("W91-G2", validator.MANUAL_GATE_REQUIREMENTS)
        self.assertEqual(
            {
                "W86-USER-VISUAL",
                "W89-USER-VISUAL",
                "W92-USER-VISUAL",
            },
            {
                item["acceptanceId"]
                for item in fixture_handoff(fixture, 92, "acceptance").data[
                    "userAcceptances"
                ]
            },
        )
        self.assertEqual(
            "Passed",
            fixture["goal"].data["manualAcceptances"]["w91NarratorManualUx"],
        )

    def test_w91_operator_checklists_require_exact_real_observations(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture, document = prepare_w91_operator_evidence(root)
            problems = validator.Problems()
            validator.validate_w91_operator_evidence(
                {"W91-G2": document}, root, problems
            )
            self.assertEqual([], problems.items)

            narrator = next(
                item
                for item in document.data["evidence"]
                if Path(item["path"]).name == "narrator-manual.json"
            )
            path = root / narrator["path"]
            payload = json.loads(path.read_text(encoding="utf-8"))
            payload["items"][0]["checklistId"] = payload["items"][1][
                "checklistId"
            ]
            payload["items"][1]["observations"] = ""
            payload["items"][2]["observedAt"] = "2026-07-29T00:00:00Z"
            payload["items"][3]["issuePointers"] = ["ISSUE-1"]
            narrator["sha256"] = write_json_evidence(
                root, narrator["path"], payload
            )
            problems = validator.Problems()
            validator.validate_w91_operator_evidence(
                {"W91-G2": document}, root, problems
            )
            self.assert_problem(
                problems.items, "W91_OPERATOR_CHECKLIST_SET"
            )
            self.assert_problem(
                problems.items, "W91_OPERATOR_CHECKLIST_ITEM"
            )

    def test_w92_visual_manifest_requires_exact_45_file_matrix(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture, document, manifest = prepare_w92_visual_manifest(root)
            gate_by_id = {
                item.data["gateId"]: item for item in fixture["gates"]
            }
            gate_by_id["W92-G9"] = document
            problems = validator.Problems()
            validator._validate_w92_visual_manifest(
                manifest,
                document,
                gate_by_id,
                root,
                problems,
                "screenshot-manifest.json",
            )
            self.assertEqual([], problems.items)

            malformed = deepcopy(manifest)
            malformed["files"][1]["path"] = malformed["files"][0]["path"]
            malformed["files"][1]["artifactType"] = malformed["files"][0][
                "artifactType"
            ]
            malformed["files"][1]["viewport"] = malformed["files"][0][
                "viewport"
            ]
            malformed["files"][1]["fixture"] = malformed["files"][0][
                "fixture"
            ]
            malformed["w91EvidenceBindings"].reverse()
            problems = validator.Problems()
            validator._validate_w92_visual_manifest(
                malformed,
                document,
                gate_by_id,
                root,
                problems,
                "screenshot-manifest.json",
            )
            self.assert_problem(problems.items, "W92_VISUAL_MATRIX")
            self.assert_problem(
                problems.items, "W92_VISUAL_FILE_UNIQUENESS"
            )
            self.assert_problem(
                problems.items, "W92_NARRATOR_REVIEW_BINDING"
            )

    def test_evidence_basenames_and_screenshot_manifest_are_unique(self) -> None:
        fixture = make_fixture()
        document = fixture_gate(fixture, "W92-G9")
        manifest = next(
            item
            for item in document.data["evidence"]
            if Path(item["path"]).name == "screenshot-manifest.json"
        )
        duplicate = deepcopy(manifest)
        duplicate["evidenceId"] = "W92-G9-duplicate-manifest"
        duplicate["path"] = "artifacts/duplicate/screenshot-manifest.json"
        document.data["evidence"].append(duplicate)
        problems = validator.Problems()
        validator._check_gate_evidence(
            document.data, document.relative_path, None, problems
        )
        self.assert_problem(
            problems.items, "EVIDENCE_BASENAME_DUPLICATE"
        )
        problems = validator.Problems()
        validator.validate_manual_acceptance_receipts(
            {"W92-G9": document}, [], None, problems
        )
        self.assert_problem(
            problems.items, "MANUAL_ACCEPTANCE_MANIFEST_MISSING"
        )

    def test_registry_must_be_exact_and_unique(self) -> None:
        fixture = make_fixture()
        fixture["goal"].data["gateRegistry"][-1] = "W84-G0"
        errors = validate(fixture)
        self.assert_problem(errors, "GOAL_REGISTRY")
        self.assert_problem(errors, "GOAL_REGISTRY_DUPLICATE")

    def test_duplicate_gate_document_is_rejected(self) -> None:
        fixture = make_fixture()
        fixture["gates"].append(fixture["gates"][0])
        self.assert_problem(validate(fixture), "GATE_DUPLICATE")

    def test_gate_lane_and_command_counts_are_factual(self) -> None:
        fixture = make_fixture()
        gate = fixture["gates"][0].data
        gate["lane"] = "cli"
        gate["commandCounts"]["passed"] = 0
        errors = validate(fixture)
        self.assert_problem(errors, "GATE_LANE")
        self.assert_problem(errors, "COMMAND_COUNTS_ACTUAL")
        self.assert_problem(errors, "COUNTS_BALANCE")

    def test_test_counts_cleanup_and_p1_are_enforced(self) -> None:
        fixture = make_fixture()
        gate = fixture["gates"][1].data
        gate["testCounts"]["discovered"] = 2
        gate["cleanup"]["ownedTempPathsRemaining"] = 1
        gate["openIssues"]["p1"] = 1
        errors = validate(fixture)
        self.assert_problem(errors, "COUNTS_BALANCE")
        self.assert_problem(errors, "CLEANUP_REMAINDER")
        self.assert_problem(errors, "OPEN_HIGH_PRIORITY")

    def test_controlled_write_outside_two_gates_is_rejected(self) -> None:
        fixture = make_fixture()
        gate = next(item for item in fixture["gates"] if item.data["gateId"] == "W85-R0")
        auth = gate.data["authorization"]
        auth.update(
            {
                "controlledWriteUsed": True,
                "controlledWriteShapeVerified": True,
                "controlledWriteExecutionCount": 1,
                "controlledWriteNonWritePreconditionsPassed": True,
                "controlledWriteEvidenceRefs": ["illegal-write.json"],
            }
        )
        self.assert_problem(validate(fixture), "CONTROLLED_WRITE_SCOPE")

    def test_ready_handoff_requires_exact_passed_gate_set(self) -> None:
        fixture = make_fixture()
        handoff = fixture["handoffs"][0].data
        handoff["gateResults"].pop()
        handoff["gateCounts"]["total"] -= 1
        handoff["gateCounts"]["passed"] -= 1
        self.assert_problem(validate(fixture), "HANDOFF_GATE_SET")

    def test_handoff_parent_chain_binds_exact_predecessor_bytes(self) -> None:
        fixture = make_fixture()
        w90 = fixture_handoff(fixture, 90, "integration").data
        w90["parentHandoffs"][0]["sha256"] = "0" * 64
        w90["parentHandoffs"].pop()
        errors = validate(fixture)
        self.assert_problem(errors, "HANDOFF_PARENT_SET")
        self.assert_problem(errors, "HANDOFF_PARENT_BINDING")

    def test_complete_requires_all_104_gate_files(self) -> None:
        fixture = make_fixture()
        fixture["gates"].pop()
        errors = validate(fixture)
        self.assert_problem(errors, "COMPLETE_GATE_SET")
        self.assert_problem(errors, "COMPLETE_GATE_STATUS")

    def test_provider_hash_is_bound_to_raw_ledger(self) -> None:
        fixture = make_fixture()
        fixture["goal"].data["authorization"]["provider"]["ledgerSha256"] = "0" * 64
        self.assert_problem(validate(fixture), "PROVIDER_LEDGER_HASH")

    def test_provider_gate_sequence_gap_is_rejected(self) -> None:
        fixture = make_fixture()
        first = fixture["gates"][0].data["authorization"]
        second = fixture["gates"][1].data["authorization"]
        first.update(
            {
                "providerTurnsBefore": 0,
                "providerTurnsConsumed": 1,
                "providerTurnsAfter": 1,
                "providerSequenceBefore": 0,
                "providerSequenceAfter": 1,
            }
        )
        second.update(
            {
                "providerTurnsBefore": 2,
                "providerTurnsConsumed": 1,
                "providerTurnsAfter": 3,
                "providerSequenceBefore": 2,
                "providerSequenceAfter": 3,
            }
        )
        ledger_data = deepcopy(fixture["ledger"].data)
        ledger_data.update(
            {
                "usedTurns": 2,
                "remainingTurns": 118,
                "ledgerSequence": 2,
                "entries": [
                    {"sequence": 1, "gateId": "W84-G0"},
                    {"sequence": 2, "gateId": "W84-G1"},
                ],
            }
        )
        fixture["ledger"] = validator.document_from_data(validator.LEDGER_PATH, ledger_data)
        provider = fixture["goal"].data["authorization"]["provider"]
        provider.update(
            {
                "usedTurns": 2,
                "remainingTurns": 118,
                "ledgerSequence": 2,
                "ledgerSha256": fixture["ledger"].sha256,
            }
        )
        errors = validate(fixture)
        self.assert_problem(errors, "PROVIDER_LEDGER_GAP")
        self.assert_problem(errors, "PROVIDER_TURN_GAP")

    def test_final_handoff_hash_must_match_bytes(self) -> None:
        fixture = make_fixture()
        fixture["goal"].data["finalHandoff"]["sha256"] = "f" * 64
        self.assert_problem(validate(fixture), "COMPLETE_FINAL_HASH")

    def test_current_schemas_extract_the_frozen_registry(self) -> None:
        root = Path(__file__).resolve().parents[1]
        with (root / "docs_md/weekly/84_92_week_goal_control.schema.json").open(
            encoding="utf-8"
        ) as stream:
            goal_schema = validator.json.load(stream)
        with (root / "docs_md/weekly/84_92_week_gate_result.schema.json").open(
            encoding="utf-8"
        ) as stream:
            gate_schema = validator.json.load(stream)
        problems = validator.Problems()
        contract = validator.extract_contract(goal_schema, gate_schema, problems)
        self.assertEqual([], problems.items)
        self.assertEqual(104, len(contract.registry))
        self.assertEqual(14, len(contract.groups))

    def test_gate_requirements_manifest_is_bootstrap_frozen(self) -> None:
        validator_source = SCRIPT.read_text(encoding="utf-8")
        self.assertNotIn(
            "EXPECTED_GATE_REQUIREMENTS_SHA256", validator_source
        )
        manifest_path = SCRIPT.parents[1] / validator.GATE_REQUIREMENTS_PATH
        manifest_document = validator.Document(
            path=manifest_path,
            relative_path=validator.GATE_REQUIREMENTS_PATH,
            data=json.loads(manifest_path.read_text(encoding="utf-8")),
            sha256=hashlib.sha256(manifest_path.read_bytes()).hexdigest(),
        )
        fixture = make_fixture()
        problems = validator.Problems()
        validator.validate_gate_requirements_manifest(
            manifest_document,
            None,
            [],
            fixture["contract"],
            problems,
        )
        self.assertFalse(problems.items, problems.items)

        ambient_role = deepcopy(manifest_document.data)
        ambient_role["frozenPolicies"]["trusted-test-command"][
            "executableRole"
        ] = "python-current"
        ambient_document = validator.document_from_data(
            validator.GATE_REQUIREMENTS_PATH, ambient_role
        )
        ambient_problems = validator.Problems()
        validator.validate_gate_requirements_manifest(
            ambient_document,
            None,
            [],
            fixture["contract"],
            ambient_problems,
        )
        self.assert_problem(
            ambient_problems.items,
            "REQUIREMENTS_TRUSTED_TEST_POLICY",
        )

        expanded_git = deepcopy(manifest_document.data)
        expanded_git["frozenPolicies"]["trusted-git"]["locator"] = "PATH"
        expanded_document = validator.document_from_data(
            validator.GATE_REQUIREMENTS_PATH, expanded_git
        )
        expanded_problems = validator.Problems()
        validator.validate_gate_requirements_manifest(
            expanded_document,
            None,
            [],
            fixture["contract"],
            expanded_problems,
        )
        self.assert_problem(
            expanded_problems.items,
            "REQUIREMENTS_TRUSTED_GIT_POLICY",
        )

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            _git(root, "init", "--quiet")
            seed = root / "bootstrap-parent.txt"
            seed.write_text("parent\n", encoding="utf-8")
            _commit_fixture_paths(root, "bootstrap parent", seed.name)
            bootstrap_parent = _git(root, "rev-parse", "HEAD")

            target = root / validator.GATE_REQUIREMENTS_PATH
            target.parent.mkdir(parents=True)
            target.write_bytes(manifest_path.read_bytes())
            attributes_path = target.parent / ".gitattributes"
            attributes_path.write_text(
                f"{target.name} -text\n", encoding="utf-8"
            )
            _commit_fixture_paths(
                root,
                "candidate B bootstrap control",
                validator.GATE_REQUIREMENTS_PATH,
                "docs_md/weekly/.gitattributes",
            )
            isolated_document = requirements_manifest_document()
            isolated_document = validator.Document(
                path=target,
                relative_path=validator.GATE_REQUIREMENTS_PATH,
                data=isolated_document.data,
                sha256=hashlib.sha256(target.read_bytes()).hexdigest(),
            )
            with (
                patch.object(
                    validator, "W84_BOOTSTRAP_PARENT", bootstrap_parent
                ),
                patch.object(
                    validator,
                    "BOOTSTRAP_CONTROL_PATHS",
                    (
                        validator.GATE_REQUIREMENTS_PATH,
                        "docs_md/weekly/.gitattributes",
                    ),
                ),
            ):
                trusted = validator.Problems()
                validator.validate_gate_requirements_manifest(
                    isolated_document,
                    None,
                    [],
                    fixture["contract"],
                    trusted,
                    repo_root=root,
                )
                self.assertEqual([], trusted.items)

                weakened = deepcopy(isolated_document.data)
                weakened["gates"][10]["requiredCommandIds"] = []
                weakened["gates"][10]["minimumCommandCount"] = 0
                weakened_raw = validator._json_bytes(weakened)
                target.write_bytes(weakened_raw)
                weakened_document = validator.Document(
                    path=target,
                    relative_path=validator.GATE_REQUIREMENTS_PATH,
                    data=weakened,
                    sha256=hashlib.sha256(weakened_raw).hexdigest(),
                )
                weakened_problems = validator.Problems()
                validator.validate_gate_requirements_manifest(
                    weakened_document,
                    None,
                    [],
                    fixture["contract"],
                    weakened_problems,
                    repo_root=root,
                )
                self.assert_problem(
                    weakened_problems.items,
                    "REQUIREMENTS_MANIFEST_IMMUTABLE",
                )
                self.assert_problem(
                    weakened_problems.items,
                    "REQUIREMENTS_MANIFEST_CANDIDATE_B",
                )

    def test_bootstrap_control_plane_includes_executor_and_rejects_rewrites(self) -> None:
        self.assertEqual(44, len(validator.BOOTSTRAP_CONTROL_PATHS))
        self.assertIn(
            "docs_md/plans/.gitattributes",
            validator.BOOTSTRAP_CONTROL_PATHS,
        )
        self.assertIn(
            "docs_md/weekly/.gitattributes",
            validator.BOOTSTRAP_CONTROL_PATHS,
        )
        self.assertIn(
            "tools/.gitattributes",
            validator.BOOTSTRAP_CONTROL_PATHS,
        )
        self.assertIn(
            "tools/week84_92_trusted_executor.py",
            validator.BOOTSTRAP_CONTROL_PATHS,
        )
        self.assertIn(
            "tools/week84_92_provider_turn_harness.py",
            validator.BOOTSTRAP_CONTROL_PATHS,
        )
        self.assertIn(
            "tools/test_week84_92_trusted_executor.py",
            validator.BOOTSTRAP_CONTROL_PATHS,
        )
        self.assertEqual(
            set(validator.PROVIDER_SCENARIO_PATHS.values()),
            {
                path
                for path in validator.BOOTSTRAP_CONTROL_PATHS
                if path.startswith("tools/week84_92_provider_scenarios/")
            },
        )
        self.assertIn(
            "docs_md/weekly/84_92_command_control/W84-G0.json",
            validator.BOOTSTRAP_CONTROL_PATHS,
        )
        self.assertEqual(
            {
                "tools/week84_92_commands/goal-contract-unittest.py",
                "tools/week84_92_commands/trusted-executor-unittest.py",
                "tools/week84_92_commands/goal-control-schema-validate.py",
                "tools/week84_92_commands/prior-handoff-schema-validate.py",
                "tools/week84_92_commands/week83-lineage-identity-verify.py",
            },
            {
                path
                for path in validator.BOOTSTRAP_CONTROL_PATHS
                if path.startswith("tools/week84_92_commands/")
            },
        )
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            _git(root, "init", "--quiet")
            control = root / "control.txt"
            (root / ".gitattributes").write_text(
                "control.txt -text\n", encoding="utf-8"
            )
            control.write_text("frozen\n", encoding="utf-8")
            _git(root, "add", "control.txt", ".gitattributes")
            _git(
                root,
                "-c",
                "user.name=Goal Validator",
                "-c",
                "user.email=validator@example.invalid",
                "commit",
                "--quiet",
                "-m",
                "add control",
            )
            original_paths = validator.BOOTSTRAP_CONTROL_PATHS
            validator.BOOTSTRAP_CONTROL_PATHS = ("control.txt",)
            try:
                problems = validator.Problems()
                validator.validate_bootstrap_control_plane(root, problems)
                self.assertEqual([], problems.items)

                info_attributes_value = _git(
                    root, "rev-parse", "--git-path", "info/attributes"
                )
                info_attributes = Path(info_attributes_value)
                if not info_attributes.is_absolute():
                    info_attributes = root / info_attributes
                info_attributes.parent.mkdir(parents=True, exist_ok=True)
                info_attributes.write_text(
                    "control.txt filter=malicious\n", encoding="utf-8"
                )
                attribute_problems = validator.Problems()
                validator.validate_bootstrap_control_plane(
                    root, attribute_problems
                )
                self.assert_problem(
                    attribute_problems.items,
                    "BOOTSTRAP_CONTROL_INFO_ATTRIBUTES",
                )
                info_attributes.write_text("", encoding="utf-8")

                _git(root, "update-index", "--chmod=+x", "control.txt")
                mode_problems = validator.Problems()
                validator.validate_bootstrap_control_plane(root, mode_problems)
                self.assert_problem(
                    mode_problems.items, "BOOTSTRAP_CONTROL_MODE"
                )
                _git(root, "update-index", "--chmod=-x", "control.txt")

                control.write_text("mutated\n", encoding="utf-8")
                _git(root, "add", "control.txt")
                _git(
                    root,
                    "-c",
                    "user.name=Goal Validator",
                    "-c",
                    "user.email=validator@example.invalid",
                    "commit",
                    "--quiet",
                    "-m",
                    "mutate control",
                )
                control.write_text("frozen\n", encoding="utf-8")
                _git(root, "add", "control.txt")
                _git(
                    root,
                    "-c",
                    "user.name=Goal Validator",
                    "-c",
                    "user.email=validator@example.invalid",
                    "commit",
                    "--quiet",
                    "-m",
                    "revert control",
                )
                problems = validator.Problems()
                validator.validate_bootstrap_control_plane(root, problems)
                self.assert_problem(
                    problems.items, "BOOTSTRAP_CONTROL_HISTORY_MUTATION"
                )
            finally:
                validator.BOOTSTRAP_CONTROL_PATHS = original_paths

    def test_week83_lineage_bootstrap_paths_match_validator_policy(self) -> None:
        adapter_path = (
            SCRIPT.parent
            / "week84_92_commands"
            / "week83-lineage-identity-verify.py"
        )
        adapter_spec = importlib.util.spec_from_file_location(
            "week83_lineage_identity_verify",
            adapter_path,
        )
        self.assertIsNotNone(adapter_spec)
        assert adapter_spec is not None and adapter_spec.loader is not None
        adapter = importlib.util.module_from_spec(adapter_spec)
        adapter_spec.loader.exec_module(adapter)

        self.assertEqual(44, len(adapter.BOOTSTRAP_PATHS))
        self.assertEqual(
            set(validator.BOOTSTRAP_CONTROL_PATHS),
            set(adapter.BOOTSTRAP_PATHS),
        )

    def test_bootstrap_control_plane_uses_constant_git_batch_count(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            _git(root, "init", "--quiet")
            seed = root / "seed.txt"
            seed.write_text("parent\n", encoding="utf-8")
            _commit_fixture_paths(root, "parent", seed.name)
            paths = tuple(f"control-{index}.txt" for index in range(8))
            for index, relative_path in enumerate(paths):
                (root / relative_path).write_text(
                    f"frozen-{index}\n", encoding="utf-8"
                )
            (root / ".gitattributes").write_text(
                "".join(f"{path} -text\n" for path in paths),
                encoding="utf-8",
            )
            _commit_fixture_paths(
                root,
                "bootstrap batch",
                *paths,
                ".gitattributes",
            )
            original_paths = validator.BOOTSTRAP_CONTROL_PATHS
            real_run = subprocess.run
            validator.BOOTSTRAP_CONTROL_PATHS = paths
            try:
                with patch.object(
                    validator.subprocess,
                    "run",
                    side_effect=real_run,
                ) as run:
                    problems = validator.Problems()
                    validator.validate_bootstrap_control_plane(root, problems)
                self.assertEqual([], problems.items)
                self.assertLessEqual(run.call_count, 14)
            finally:
                validator.BOOTSTRAP_CONTROL_PATHS = original_paths

    def test_w84_verifier_goal_contract_runner_targets_exact_full_control_suite(
        self,
    ) -> None:
        adapter_path = (
            SCRIPT.parent / "week84_92_commands" / "goal-contract-unittest.py"
        )
        adapter_spec = importlib.util.spec_from_file_location(
            "w84_goal_contract_adapter",
            adapter_path,
        )
        self.assertIsNotNone(adapter_spec)
        assert adapter_spec is not None and adapter_spec.loader is not None
        adapter = importlib.util.module_from_spec(adapter_spec)
        adapter_spec.loader.exec_module(adapter)
        expected_modules = (
            "tools.test_validate_week84_92_goal_evidence",
            "tools.test_week84_92_goal_integrity",
            "tools.test_week84_92_gate_requirements",
            "tools.test_week84_92_evidence_anchor",
            "tools.test_week84_92_trusted_executor",
        )
        self.assertEqual(expected_modules, adapter.MODULES)
        self.assertIn(
            "loadTestsFromNames(MODULES)",
            adapter_path.read_text(encoding="utf-8"),
        )
        for module_name in expected_modules:
            self.assertGreater(
                unittest.defaultTestLoader.loadTestsFromName(
                    module_name
                ).countTestCases(),
                0,
                module_name,
            )

    def test_w84_verifier_trusted_executor_runner_targets_exact_suite(
        self,
    ) -> None:
        adapter_path = (
            SCRIPT.parent / "week84_92_commands" / "trusted-executor-unittest.py"
        )
        source = adapter_path.read_text(encoding="utf-8")
        exact_module = "tools.test_week84_92_trusted_executor"
        self.assertIn("loadTestsFromName(", source)
        self.assertEqual(1, source.count(exact_module))
        self.assertGreater(
            unittest.defaultTestLoader.loadTestsFromName(
                exact_module
            ).countTestCases(),
            0,
        )

    def test_w84_g0_canonical_summaries_exactly_reconcile_trusted_reports(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            evidence_root = (
                root
                / "artifacts/week84-renderer-listener-retention/"
                "gate-evidence/W84-G0"
            )
            evidence_root.mkdir(parents=True)
            candidate = "a" * 40
            test_counts = {
                "discovered": 5,
                "passed": 5,
                "failed": 0,
                "skipped": 0,
                "notRun": 0,
                "notApplicable": 0,
            }
            semantic_test_counts = {
                "discovered": 137,
                "passed": 137,
                "failed": 0,
                "skipped": 0,
            }
            semantic_path = (
                "artifacts/week84-renderer-listener-retention/"
                "gate-evidence/W84-G0/"
                "goal-evidence-semantic-validate.semantic-01."
                "trusted-test-report.json"
            )
            runner_source = {
                "path": validator.TEST_RUNNER_PATH,
                "sha256": "1" * 64,
                "gitBlobSha": "2" * 40,
                "controlRevision": candidate,
            }
            semantic_data = {
                "commandId": "goal-evidence-semantic-validate",
                "attemptId": "semantic-01",
                "countsSource": "python-unittest-output",
                "testCounts": semantic_test_counts,
                "runnerSource": runner_source,
            }
            semantic_raw = validator._json_bytes(semantic_data)
            semantic_document = validator.Document(
                path=root / semantic_path,
                relative_path=semantic_path,
                data=semantic_data,
                sha256=hashlib.sha256(semantic_raw).hexdigest(),
            )
            command_ids = [
                "goal-contract-unittest",
                "trusted-executor-unittest",
                "goal-control-schema-validate",
                "prior-handoff-schema-validate",
                "week83-lineage-identity-verify",
            ]
            product_bundles = {
                command_id: {
                    "commandId": command_id,
                    "adapterReportPath": f"adapter/{command_id}.json",
                    "adapterReportSha256": hashlib.sha256(
                        f"adapter:{command_id}".encode("utf-8")
                    ).hexdigest(),
                    "verifierReportPath": f"verifier/{command_id}.json",
                    "verifierReportSha256": hashlib.sha256(
                        f"verifier:{command_id}".encode("utf-8")
                    ).hexdigest(),
                    "verifierTestName": validator.W84_G0_VERIFICATION_ARGUMENT_BY_COMMAND[
                        command_id
                    ],
                    "status": "Passed",
                }
                for command_id in command_ids
            }
            runner_policy = {
                "runnerId": validator.TEST_RUNNER_ID,
                "sourcePath": validator.TEST_RUNNER_PATH,
                "sourceSha256": "1" * 64,
                "shellAllowed": False,
            }
            test_policy = {
                "policyId": "semantic-policy",
                "commandId": "goal-evidence-semantic-validate",
            }
            product_policy = {"policyId": "product-policy"}
            git_policy = {"policyId": validator.TRUSTED_GIT_POLICY_ID}
            control_binding = {
                "path": validator.W84_G0_CONTROL_PATH,
                "sha256": "3" * 64,
                "controlRevision": candidate,
            }
            gate = {
                "identity": {"productCandidate": candidate},
                "testCounts": test_counts,
                "commandControlBinding": control_binding,
            }
            requirement = {
                "requiredCommandIds": [
                    *command_ids,
                    "goal-evidence-semantic-validate",
                ]
            }
            goal_summary = {
                "schemaVersion": validator.SCHEMA_VERSION,
                "goalId": validator.GOAL_ID,
                "gateId": "W84-G0",
                "status": "Passed",
                "productCandidate": candidate,
                "semanticCommandId": "goal-evidence-semantic-validate",
                "semanticAttemptId": "semantic-01",
                "trustedTestReportPath": semantic_path,
                "trustedTestReportSha256": semantic_document.sha256,
                "countsSource": "python-unittest-output",
                "testCounts": semantic_test_counts,
            }
            policies = {
                field: {
                    "identityId": policy.get(
                        "runnerId", policy.get("policyId")
                    ),
                    "sha256": hashlib.sha256(
                        validator._json_bytes(policy)
                    ).hexdigest(),
                }
                for field, policy in {
                    "trustedExecutor": runner_policy,
                    "trustedTest": test_policy,
                    "trustedProduct": product_policy,
                    "trustedGit": git_policy,
                }.items()
            }
            executor_summary = {
                "schemaVersion": validator.SCHEMA_VERSION,
                "goalId": validator.GOAL_ID,
                "gateId": "W84-G0",
                "status": "Passed",
                "productCandidate": candidate,
                "executorSource": runner_source,
                "commandControl": control_binding,
                "trustedPolicies": policies,
                "semanticReport": {
                    "path": semantic_path,
                    "sha256": semantic_document.sha256,
                    "commandId": "goal-evidence-semantic-validate",
                    "attemptId": "semantic-01",
                    "countsSource": "python-unittest-output",
                    "testCounts": semantic_test_counts,
                    "status": "Passed",
                },
                "adapterVerifierPairs": list(product_bundles.values()),
            }

            def write_summary(name: str, value: dict) -> dict:
                relative = (
                    "artifacts/week84-renderer-listener-retention/"
                    f"gate-evidence/W84-G0/{name}"
                )
                raw = validator._json_bytes(value)
                (root / relative).write_bytes(raw)
                return {
                    "evidenceId": name,
                    "kind": "json",
                    "path": relative,
                    "sha256": hashlib.sha256(raw).hexdigest(),
                }

            evidence = [
                write_summary("goal-contract-unittest.json", goal_summary),
                write_summary(
                    "trusted-executor-unittest.json", executor_summary
                ),
            ]

            def validate_summaries() -> validator.Problems:
                problems = validator.Problems()
                validator._validate_w84_g0_canonical_summaries(
                    repo_root=root,
                    gate=gate,
                    requirement=requirement,
                    evidence=evidence,
                    semantic_report=semantic_document,
                    product_bundles=product_bundles,
                    trusted_runner_policy=runner_policy,
                    trusted_test_policy=test_policy,
                    trusted_product_policy=product_policy,
                    trusted_git_policy=git_policy,
                    problems=problems,
                    location="W84-G0",
                )
                return problems

            self.assertEqual([], validate_summaries().items)

            tampered_goal = deepcopy(goal_summary)
            tampered_goal["trustedTestReportSha256"] = "0" * 64
            evidence[0] = write_summary(
                "goal-contract-unittest.json", tampered_goal
            )
            self.assert_problem(
                validate_summaries().items,
                "W84_G0_GOAL_SUMMARY_BINDING",
            )

            evidence[0] = write_summary(
                "goal-contract-unittest.json", goal_summary
            )
            tampered_executor = deepcopy(executor_summary)
            tampered_executor["adapterVerifierPairs"][0]["verifierReports"] = [
                {
                    "testName": tampered_executor["adapterVerifierPairs"][0][
                        "verifierTestName"
                    ],
                    "path": tampered_executor["adapterVerifierPairs"][0][
                        "verifierReportPath"
                    ],
                }
            ]
            evidence[1] = write_summary(
                "trusted-executor-unittest.json", tampered_executor
            )
            self.assert_problem(
                validate_summaries().items,
                "W84_G0_EXECUTOR_SUMMARY_BINDING",
            )

            # The canonical summaries bind the unique semantic report, not the
            # Gate aggregate (which separately includes the five verifiers).
            forged_goal = deepcopy(goal_summary)
            forged_goal["testCounts"] = test_counts
            evidence[0] = write_summary(
                "goal-contract-unittest.json", forged_goal
            )
            evidence[1] = write_summary(
                "trusted-executor-unittest.json", executor_summary
            )
            self.assert_problem(
                validate_summaries().items,
                "W84_G0_GOAL_SUMMARY_BINDING",
            )

    def test_semantic_runtime_tree_reconstructs_pre_goal_and_fails_closed(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            command_id = "goal-evidence-semantic-validate"
            attempt_id = "semantic-01"
            report_path = (
                "artifacts/week84-renderer-listener-retention/"
                "gate-evidence/W84-G0/"
                f"{command_id}.{attempt_id}.trusted-test-report.json"
            )
            stdout_path = report_path.removesuffix(".json") + ".stdout.log"
            stderr_path = report_path.removesuffix(".json") + ".stderr.log"
            result_path = (
                "artifacts/week84-renderer-listener-retention/"
                "gates/W84-G0.json"
            )
            snapshot_path = (
                "artifacts/week84-renderer-listener-retention/"
                "gate-evidence/W84-G0/"
                f"{command_id}.{attempt_id}.pre-goal-state.json"
            )
            command_counts = {
                "total": 6,
                "passed": 6,
                "failed": 0,
                "notRun": 0,
                "notApplicable": 0,
                "countsBalanced": True,
            }
            test_counts = {
                "discovered": 142,
                "passed": 142,
                "failed": 0,
                "skipped": 0,
                "notRun": 0,
                "notApplicable": 0,
                "countsBalanced": True,
            }
            before_goal = {
                "schemaVersion": validator.SCHEMA_VERSION,
                "goalId": validator.GOAL_ID,
                "status": "Active",
                "identity": {"productCandidate": "a" * 40},
                "execution": {
                    "laneStates": {"baseline": {"state": "Active"}},
                    "weekStates": {
                        "W84": {
                            "entryGate": "NotRun",
                            "exitGate": "NotRun",
                        }
                    },
                },
                "gateRollup": {
                    "total": 104,
                    "passed": 0,
                    "failed": 0,
                    "notRun": 104,
                    "notApplicable": 0,
                    "countsBalanced": True,
                },
                "commandRollup": {
                    "total": 0,
                    "passed": 0,
                    "failed": 0,
                    "notRun": 0,
                    "notApplicable": 0,
                    "countsBalanced": True,
                },
                "testRollup": {
                    "discovered": 0,
                    "passed": 0,
                    "failed": 0,
                    "skipped": 0,
                    "notRun": 0,
                    "notApplicable": 0,
                    "countsBalanced": True,
                },
                "nextAction": {
                    "kind": "RunGate",
                    "owner": "GoalAgent",
                    "checkpoint": "W84",
                    "summary": "Run W84-G0 bootstrap.",
                    "blockedBy": [],
                },
                "updatedAt": "2026-07-28T00:00:00Z",
            }
            finished_at = "2026-07-28T00:01:00Z"
            after_goal = deepcopy(before_goal)
            after_goal["gateRollup"].update(
                {"passed": 1, "notRun": 103}
            )
            after_goal["commandRollup"] = deepcopy(command_counts)
            after_goal["testRollup"] = deepcopy(test_counts)
            after_goal["execution"]["weekStates"]["W84"][
                "entryGate"
            ] = "Passed"
            after_goal["nextAction"] = {
                "kind": "RunGate",
                "owner": "GoalAgent",
                "checkpoint": "W84",
                "summary": "Run W84-G1 deterministic listener baseline.",
                "blockedBy": [],
            }
            after_goal["updatedAt"] = finished_at
            gate = {
                "gateId": "W84-G0",
                "status": "Passed",
                "resultPath": result_path,
                "finishedAt": finished_at,
                "commandCounts": command_counts,
                "testCounts": test_counts,
            }
            payload = {
                "commandId": command_id,
                "attemptId": attempt_id,
                "stdout": {"path": stdout_path},
                "stderr": {"path": stderr_path},
            }

            def write(relative: str, raw: bytes) -> None:
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(raw)

            before_raw = validator._json_bytes(before_goal)
            write(validator.trusted_executor.GOAL_STATE_PATH, validator._json_bytes(after_goal))
            write(snapshot_path, before_raw)
            for relative in (report_path, stdout_path, stderr_path):
                write(relative, b"sealed\n")
            post_paths = validator.trusted_executor._semantic_post_output_paths(
                gate_id="W84-G0", result_path=result_path
            )
            for relative in post_paths:
                write(relative, b"{}")
            exclusions = sorted(
                [report_path, stdout_path, stderr_path, *post_paths],
                key=lambda value: value.encode("utf-8"),
            )
            roots, summary = (
                validator.trusted_executor._artifact_input_tree_inventory(
                    root,
                    excluded_paths=exclusions,
                    content_overrides={
                        validator.trusted_executor.GOAL_STATE_PATH: before_raw
                    },
                )
            )
            runtime_tree = {
                "mode": validator.trusted_executor.ARTIFACT_INPUT_TREE_MODE,
                "contentRootAlgorithm": (
                    validator.trusted_executor.ARTIFACT_INPUT_TREE_ALGORITHM
                ),
                "scopedRoots": roots,
                "excludedPaths": exclusions,
                "preGoalStateSnapshot": {
                    "path": snapshot_path,
                    "bytes": len(before_raw),
                    "sha256": hashlib.sha256(before_raw).hexdigest(),
                    "stableDuringChild": True,
                },
                "postSemanticOutputs": [
                    {
                        "path": path,
                        "mode": "exclusive-create-after-semantic",
                        "before": {
                            "exists": False,
                            "bytes": None,
                            "sha256": None,
                        },
                        "stableDuringChild": True,
                    }
                    for path in post_paths
                ],
                "before": summary,
                "after": summary,
                "matchesBefore": True,
            }
            report_document = validator.Document(
                path=root / report_path,
                relative_path=report_path,
                data=payload,
                sha256="0" * 64,
            )

            def validate_tree(value: dict) -> validator.Problems:
                problems = validator.Problems()
                validator._validate_semantic_runtime_input_tree(
                    value,
                    payload,
                    report_document,
                    gate,
                    root,
                    problems,
                    report_path,
                )
                return problems

            self.assertEqual([], validate_tree(runtime_tree).items)

            # A forbidden/sensitive and over-limit-shaped legacy root is
            # deliberately outside the finite semantic root set.
            legacy = root / "artifacts/legacy/.env.local"
            legacy.parent.mkdir(parents=True)
            legacy.write_text("OPENAI_API_KEY=must-not-be-read", encoding="utf-8")
            self.assertEqual([], validate_tree(runtime_tree).items)

            tampered = deepcopy(runtime_tree)
            tampered["postSemanticOutputs"][0]["before"]["exists"] = True
            self.assert_problem(
                validate_tree(tampered).items,
                "TEST_REPORT_POST_SEMANTIC_OUTPUTS",
            )

            tampered = deepcopy(runtime_tree)
            tampered["before"]["entryCount"] += 1
            self.assert_problem(
                validate_tree(tampered).items,
                "TEST_REPORT_RUNTIME_INPUT_TREE",
            )

            after_goal["identity"]["productCandidate"] = "b" * 40
            write(
                validator.trusted_executor.GOAL_STATE_PATH,
                validator._json_bytes(after_goal),
            )
            self.assert_problem(
                validate_tree(runtime_tree).items,
                "TEST_REPORT_GOAL_STATE_TRANSITION",
            )

    def test_w84_g0_baseline_identity_is_exact_raw_bound_projection(
        self,
    ) -> None:
        frozen_package_path = (
            SCRIPT.parents[1] / validator.W84_WEEK83_PACKAGE_IDENTITY_PATH
        )
        self.assertRegex(
            validator.W84_WEEK83_PACKAGE_IDENTITY_SHA256,
            validator.HEX_SHA256,
        )
        self.assertEqual(
            hashlib.sha256(frozen_package_path.read_bytes()).hexdigest(),
            validator.W84_WEEK83_PACKAGE_IDENTITY_SHA256,
        )
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            candidate = "b" * 40
            entry = {
                "schemaVersion": validator.SCHEMA_VERSION,
                "gateId": "W84-G0",
                "status": "Passed",
                "goalBootstrapRevision": candidate,
                "productCandidate": candidate,
                "week83ProductCandidate": (
                    validator.W84_WEEK83_PRODUCT_CANDIDATE
                ),
                "week83DocumentationClosure": (
                    validator.W84_WEEK83_DOCUMENTATION_CLOSURE
                ),
                "bootstrapParentRevision": validator.W84_BOOTSTRAP_PARENT,
                "productInputPaths": list(validator.W84_PRODUCT_INPUT_PATHS),
                "productInputDiffCount": 0,
            }
            package = {
                "schemaVersion": "week83-approval-projection-remediation/v1",
                "evidenceKind": "package-identity",
                "status": "Passed",
                "exactCleanProductCandidate": (
                    validator.W84_WEEK83_PRODUCT_CANDIDATE
                ),
                "sourceDirtyAtPackageBuild": False,
                "package": {
                    "desktopSha256": "A" * 64,
                    "desktopBytes": 222753280,
                    "treeSha256": "B" * 64,
                    "treeBytes": 464709225,
                    "fileCount": 78,
                    "appAsarSha256": "C" * 64,
                    "appAsarBytes": 555529,
                    "appHostSha256": "D" * 64,
                    "appHostBytes": 79941168,
                },
                "rendererBundle": {
                    "name": "index-frozen.js",
                    "sha256": "E" * 64,
                },
            }

            def write(relative: str, value: dict) -> bytes:
                raw = validator._json_bytes(value)
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(raw)
                return raw

            entry_raw = write(validator.W84_G0_ENTRY_PATH, entry)
            package_raw = write(
                validator.W84_WEEK83_PACKAGE_IDENTITY_PATH, package
            )
            package_sha = hashlib.sha256(package_raw).hexdigest()
            package_values = package["package"]
            expected = {
                "schemaVersion": validator.SCHEMA_VERSION,
                "gateId": "W84-G0",
                "status": "Passed",
                "productCandidate": candidate,
                "goalBootstrapRevision": candidate,
                "week83ProductCandidate": (
                    validator.W84_WEEK83_PRODUCT_CANDIDATE
                ),
                "week83DocumentationClosure": (
                    validator.W84_WEEK83_DOCUMENTATION_CLOSURE
                ),
                "bootstrapParentRevision": validator.W84_BOOTSTRAP_PARENT,
                "entrySource": {
                    "path": validator.W84_G0_ENTRY_PATH,
                    "sha256": hashlib.sha256(entry_raw).hexdigest(),
                },
                "week83PackageIdentitySource": {
                    "path": validator.W84_WEEK83_PACKAGE_IDENTITY_PATH,
                    "sha256": package_sha,
                },
                "productInputEquivalence": {
                    "fromRevision": validator.W84_WEEK83_PRODUCT_CANDIDATE,
                    "toRevision": candidate,
                    "paths": list(validator.W84_PRODUCT_INPUT_PATHS),
                    "differenceCount": 0,
                },
                "packageIdentity": {
                    "desktopPackage": {
                        "sha256": package_values["desktopSha256"],
                        "bytes": package_values["desktopBytes"],
                    },
                    "packageTree": {
                        "sha256": package_values["treeSha256"],
                        "bytes": package_values["treeBytes"],
                        "fileCount": package_values["fileCount"],
                    },
                    "appAsar": {
                        "sha256": package_values["appAsarSha256"],
                        "bytes": package_values["appAsarBytes"],
                    },
                    "appHost": {
                        "sha256": package_values["appHostSha256"],
                        "bytes": package_values["appHostBytes"],
                    },
                    "rendererBundle": package["rendererBundle"],
                },
            }
            baseline_raw = write(
                validator.W84_G0_BASELINE_IDENTITY_PATH, expected
            )
            evidence = [
                {
                    "kind": "plan-reference",
                    "path": validator.W84_G0_ENTRY_PATH,
                    "sha256": hashlib.sha256(entry_raw).hexdigest(),
                },
                {
                    "kind": "identity",
                    "path": validator.W84_G0_BASELINE_IDENTITY_PATH,
                    "sha256": hashlib.sha256(baseline_raw).hexdigest(),
                },
            ]
            gate = {"identity": {"productCandidate": candidate}}

            def validate_identity() -> validator.Problems:
                problems = validator.Problems()
                validator._validate_w84_g0_baseline_identity(
                    repo_root=root,
                    gate=gate,
                    evidence=evidence,
                    problems=problems,
                    location="W84-G0",
                )
                return problems

            with patch.object(
                validator,
                "W84_WEEK83_PACKAGE_IDENTITY_SHA256",
                package_sha,
            ), patch.object(validator, "_git_return_code", return_value=0):
                self.assertEqual([], validate_identity().items)

                generic = {"status": "Passed"}
                generic_raw = write(
                    validator.W84_G0_BASELINE_IDENTITY_PATH, generic
                )
                evidence[1]["sha256"] = hashlib.sha256(generic_raw).hexdigest()
                self.assert_problem(
                    validate_identity().items,
                    "W84_G0_BASELINE_IDENTITY_CONTRACT",
                )

                extra = deepcopy(expected)
                extra["selfAttested"] = True
                extra_raw = write(
                    validator.W84_G0_BASELINE_IDENTITY_PATH, extra
                )
                evidence[1]["sha256"] = hashlib.sha256(extra_raw).hexdigest()
                self.assert_problem(
                    validate_identity().items,
                    "W84_G0_BASELINE_IDENTITY_CONTRACT",
                )

                valid_raw = write(
                    validator.W84_G0_BASELINE_IDENTITY_PATH, expected
                )
                evidence[1]["sha256"] = hashlib.sha256(valid_raw).hexdigest()
                with patch.object(
                    validator, "_git_return_code", return_value=1
                ):
                    self.assert_problem(
                        validate_identity().items,
                        "W84_G0_BASELINE_IDENTITY_CONTRACT",
                    )

            package["package"]["fileCount"] = 79
            write(validator.W84_WEEK83_PACKAGE_IDENTITY_PATH, package)
            with patch.object(validator, "_git_return_code", return_value=0):
                self.assert_problem(
                    validate_identity().items,
                    "W84_G0_WEEK83_PACKAGE_IDENTITY",
                )

    def test_product_git_tool_identity_rejects_drift_and_extra_keys(
        self,
    ) -> None:
        policy = {
            "policyId": validator.TRUSTED_GIT_POLICY_ID,
            "executableRole": validator.TRUSTED_GIT_EXECUTABLE_ROLE,
            "version": validator.TRUSTED_GIT_VERSION,
            "executableBytes": validator.TRUSTED_GIT_EXECUTABLE_BYTES,
            "executableSha256": validator.TRUSTED_GIT_EXECUTABLE_SHA256,
            "shellAllowed": False,
        }
        identity = {
            "executableRole": validator.TRUSTED_GIT_EXECUTABLE_ROLE,
            "locationRole": "pinned-core-git-outside-repository",
            "version": validator.TRUSTED_GIT_VERSION,
            "executableBytes": validator.TRUSTED_GIT_EXECUTABLE_BYTES,
            "executableSha256": validator.TRUSTED_GIT_EXECUTABLE_SHA256,
        }
        expected = {
            "policyId": validator.TRUSTED_GIT_POLICY_ID,
            "policySha256": hashlib.sha256(
                validator._json_bytes(policy)
            ).hexdigest(),
            "before": identity,
            "after": identity,
            "matchesBefore": True,
        }

        def validate_identity(value: dict) -> validator.Problems:
            problems = validator.Problems()
            validator._validate_frozen_git_tool_identity(
                value,
                policy,
                Path.cwd(),
                problems,
                "product-report",
                "PRODUCT_REPORT_GIT_TOOL_IDENTITY",
            )
            return problems

        with patch.object(
            validator.trusted_executor,
            "_trusted_git_identity",
            return_value=(Path("git.exe"), identity),
        ):
            self.assertEqual([], validate_identity(expected).items)

            drifted = deepcopy(expected)
            drifted["after"] = {**identity, "executableBytes": 1}
            drifted["matchesBefore"] = False
            self.assert_problem(
                validate_identity(drifted).items,
                "PRODUCT_REPORT_GIT_TOOL_IDENTITY",
            )

            expanded = deepcopy(expected)
            expanded["locator"] = "PATH"
            self.assert_problem(
                validate_identity(expanded).items,
                "PRODUCT_REPORT_GIT_TOOL_IDENTITY",
            )

    def test_w84_verifier_goal_control_schema_and_state(self) -> None:
        root = SCRIPT.parents[1]
        schema_path = root / "docs_md/weekly/84_92_week_goal_control.schema.json"
        state_path = root / "artifacts/week84-92-goal-control/goal-state.json"
        adapter_path = (
            SCRIPT.parent
            / "week84_92_commands"
            / "goal-control-schema-validate.py"
        )
        adapter_spec = importlib.util.spec_from_file_location(
            "w84_goal_control_schema_subset",
            adapter_path,
        )
        self.assertIsNotNone(adapter_spec)
        assert adapter_spec is not None and adapter_spec.loader is not None
        adapter = importlib.util.module_from_spec(adapter_spec)
        adapter_spec.loader.exec_module(adapter)
        schema = json.loads(
            schema_path.read_bytes().decode("utf-8"),
            object_pairs_hook=validator._no_duplicate_object,
        )
        adapter.check_schema(schema)

        def subset(fragment: dict, instance: object) -> list[str]:
            return adapter.validate_instance(
                instance,
                {
                    "$schema": "https://json-schema.org/draft/2020-12/schema",
                    **fragment,
                },
            )

        self.assertEqual(
            [],
            subset(
                {
                    "$defs": {"a/b~c": {"type": "integer"}},
                    "$ref": "#/$defs/a~1b~0c",
                },
                1,
            ),
        )
        self.assertTrue(
            subset(
                {
                    "oneOf": [
                        {"type": "number"},
                        {"type": "integer"},
                    ]
                },
                1,
            )
        )
        self.assertEqual(
            [],
            subset(
                {
                    "type": "object",
                    "properties": {"kind": {"enum": ["a", "b"]}},
                    "if": {
                        "properties": {"kind": {"const": "a"}},
                        "required": ["kind"],
                    },
                    "then": {"required": ["aValue"]},
                    "else": {"required": ["bValue"]},
                },
                {"kind": "a", "aValue": 1},
            ),
        )
        self.assertTrue(
            subset(
                {
                    "type": "object",
                    "properties": {"safe": {"type": "boolean"}},
                    "additionalProperties": False,
                },
                {"safe": True, "extra": 1},
            )
        )
        self.assertEqual(
            [],
            subset({"type": "string", "format": "date-time"}, "2026-07-28T01:02:03Z"),
        )
        self.assertTrue(
            subset({"type": "string", "format": "date-time"}, "2026-02-30T01:02:03Z")
        )
        self.assertTrue(subset({"type": "string", "pattern": "^[A-Z]+$"}, "abc"))
        self.assertTrue(
            subset(
                {"type": "array", "uniqueItems": True},
                [{"value": [1]}, {"value": [1.0]}],
            )
        )
        self.assertTrue(subset({"if": True, "then": False}, "blocked"))
        self.assertTrue(subset({"type": "array", "items": False}, [1]))
        self.assertEqual([], subset({"oneOf": [True, False]}, "one"))
        with self.assertRaises(adapter.SchemaSubsetError):
            subset({"enum": [1, 1.0]}, 1)
        with self.assertRaises(adapter.SchemaSubsetError):
            subset(
                {
                    "$defs": {"bad": {"type": "integer"}},
                    "$ref": "#/$defs/~2bad",
                },
                1,
            )
        with self.assertRaises(adapter.SchemaSubsetError):
            subset({"contains": {"const": 1}}, [1])
        for raw in (b"NaN", b"Infinity", b"-Infinity", b'"\\ud800"'):
            with self.subTest(raw=raw):
                with self.assertRaises(adapter.SchemaSubsetError):
                    adapter.loads_json(raw)
        with self.assertRaises(adapter.SchemaSubsetError):
            subset({"type": "number"}, float("nan"))

        if not state_path.is_file():
            self.skipTest("W84 bootstrap goal-state artifact is not sealed yet")
        state = json.loads(
            state_path.read_bytes().decode("utf-8"),
            object_pairs_hook=validator._no_duplicate_object,
        )
        self.assertEqual([], adapter.validate_instance(state, schema))
        self.assertEqual(validator.GOAL_ID, state.get("goalId"))
        self.assertEqual(validator.SCHEMA_VERSION, state.get("schemaVersion"))
        self.assertEqual(
            validator.REGISTRY_VERSION,
            state.get("registryVersion"),
        )
        self.assertEqual(
            {"start": 84, "end": 92},
            state.get("weekRange"),
        )

    def test_w84_verifier_week83_handoff_schema_and_blocked_facts(self) -> None:
        root = SCRIPT.parents[1]
        handoff_path = (
            root
            / "artifacts/week83-approval-projection-remediation/week84-handoff.json"
        )
        verification_path = (
            root
            / "artifacts/week84-renderer-listener-retention/gate-evidence/"
            "W84-G0/week83-handoff-verification.json"
        )
        if not verification_path.is_file():
            self.skipTest("W84 Week83 handoff verification is not sealed yet")
        handoff_raw = handoff_path.read_bytes()
        handoff = json.loads(
            handoff_raw.decode("utf-8"),
            object_pairs_hook=validator._no_duplicate_object,
        )
        verification = json.loads(
            verification_path.read_bytes().decode("utf-8"),
            object_pairs_hook=validator._no_duplicate_object,
        )
        self.assertEqual(
            {
                "schemaVersion",
                "status",
                "candidateReady",
                "capturedAtUtc",
                "week83ProductCandidate",
                "week83DocumentationClosureRevision",
                "week82ProductBaseline",
                "week82ClosureRevision",
                "blockingGate",
                "blockingCategory",
                "openP0",
                "openP1",
                "packageIdentity",
                "passed",
                "failed",
                "notRun",
                "requiredRemediation",
                "preservedEvidence",
                "prohibitedShortcuts",
                "formalReleaseStatus",
            },
            set(handoff),
        )
        self.assertEqual("week84-handoff/v1", handoff["schemaVersion"])
        self.assertEqual("Blocked", handoff["status"])
        self.assertIs(False, handoff["candidateReady"])
        self.assertEqual("W83-G5", handoff["blockingGate"])
        self.assertEqual(0, handoff["openP0"])
        self.assertEqual(1, handoff["openP1"])
        self.assertEqual(1, len(handoff["failed"]))
        self.assertTrue(handoff["requiredRemediation"])
        self.assertTrue(handoff["preservedEvidence"])
        self.assertTrue(
            all(
                isinstance(value, str)
                and re.fullmatch(r"[0-9A-F]{64}", value) is not None
                for value in handoff["preservedEvidence"].values()
            )
        )
        self.assertEqual(
            {
                "schemaVersion",
                "gateId",
                "status",
                "sourcePath",
                "sourceSha256",
                "priorStatus",
                "week83ProductCandidate",
                "week83DocumentationClosure",
                "openP0",
                "openP1",
            },
            set(verification),
        )
        self.assertEqual("Passed", verification["status"])
        self.assertEqual("Blocked", verification["priorStatus"])
        self.assertEqual(
            hashlib.sha256(handoff_raw).hexdigest(),
            verification["sourceSha256"],
        )
        self.assertEqual(
            handoff["week83ProductCandidate"],
            verification["week83ProductCandidate"],
        )
        self.assertEqual(
            handoff["week83DocumentationClosureRevision"],
            verification["week83DocumentationClosure"],
        )
        for commit in (
            handoff["week83ProductCandidate"],
            handoff["week83DocumentationClosureRevision"],
        ):
            self.assertEqual(
                0,
                validator._git_return_code(
                    root, ("cat-file", "-e", f"{commit}^{{commit}}")
                ),
            )

    def test_w84_verifier_week83_bootstrap_lineage_and_product_diff(self) -> None:
        root = SCRIPT.parents[1]
        entry_path = (
            root
            / "artifacts/week84-renderer-listener-retention/gate-evidence/"
            "W84-G0/entry.json"
        )
        if not entry_path.is_file():
            self.skipTest("W84 bootstrap entry artifact is not sealed yet")
        adapter_path = (
            SCRIPT.parent
            / "week84_92_commands"
            / "week83-lineage-identity-verify.py"
        )
        adapter_spec = importlib.util.spec_from_file_location(
            "w84_lineage_adapter_for_verifier",
            adapter_path,
        )
        self.assertIsNotNone(adapter_spec)
        assert adapter_spec is not None and adapter_spec.loader is not None
        adapter = importlib.util.module_from_spec(adapter_spec)
        adapter_spec.loader.exec_module(adapter)
        entry = json.loads(
            entry_path.read_bytes().decode("utf-8"),
            object_pairs_hook=validator._no_duplicate_object,
        )
        bootstrap = entry.get("goalBootstrapRevision")
        self.assertRegex(str(bootstrap), r"^[0-9a-f]{40}$")
        self.assertEqual(validator.W84_BOOTSTRAP_PARENT, entry["bootstrapParentRevision"])
        self.assertEqual(bootstrap, entry["productCandidate"])
        self.assertEqual(adapter.WEEK83_CANDIDATE, entry["week83ProductCandidate"])
        self.assertEqual(adapter.WEEK83_DOCS, entry["week83DocumentationClosure"])
        self.assertEqual(list(adapter.BOOTSTRAP_PATHS), entry["bootstrapChangedPaths"])
        self.assertEqual(44, entry["bootstrapChangedPathCount"])
        self.assertEqual(list(adapter.PRODUCT_INPUT_PATHS), entry["productInputPaths"])
        self.assertEqual(0, entry["productInputDiffCount"])
        self.assertEqual(
            {str(path) for path in adapter.BOOTSTRAP_PATHS},
            validator._git_commit_changed_paths(root, str(bootstrap)),
        )
        self.assertEqual(
            0,
            validator._git_return_code(
                root,
                (
                    "diff",
                    "--quiet",
                    adapter.WEEK83_CANDIDATE,
                    str(bootstrap),
                    "--",
                    *adapter.PRODUCT_INPUT_PATHS,
                ),
            ),
        )
        for ancestor in (adapter.WEEK83_CANDIDATE, adapter.WEEK83_DOCS):
            self.assertEqual(
                0,
                validator._git_return_code(
                    root,
                    ("merge-base", "--is-ancestor", ancestor, str(bootstrap)),
                ),
            )
        self.assertEqual(
            f"{bootstrap} {validator.W84_BOOTSTRAP_PARENT}",
            validator._git_stdout(
                root,
                ("rev-list", "--parents", "-n", "1", str(bootstrap)),
            ),
        )

    def test_prior_sealed_command_control_accepts_exact_p_c_q_history(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = prepare_command_control_fixture(root)
            self.assertEqual(
                [],
                validate_command_control_fixture(root, fixture),
            )

    def test_w84_diagnostic_gate_accepts_control_as_candidate_with_projection(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = prepare_command_control_fixture(
                root,
                candidate_equals_control=True,
                gate_id="W84-G1",
                previous_gate_id="W84-G0",
                gate_kind="regression-baseline",
            )
            self.assertEqual(
                fixture["controlRevision"], fixture["candidate"]
            )
            self.assertEqual([], validate_command_control_fixture(root, fixture))

    def test_control_as_candidate_rejects_wrong_gate_kind_or_projection(self) -> None:
        cases = (
            ("wrong-kind", {"gate_kind": "product-change"}, "COMMAND_CONTROL_CANDIDATE_ANCESTRY"),
            ("wrong-root", {"gate_kind": "regression-baseline", "corrupt_projection_root": True}, "COMMAND_CONTROL_PRODUCTION_PROJECTION"),
            ("production-change", {"gate_kind": "regression-baseline", "production_change_in_control": True}, "COMMAND_CONTROL_PRODUCTION_PROJECTION"),
            ("merge-control", {"gate_kind": "regression-baseline", "merge_control": True}, "COMMAND_CONTROL_DIRECT_PARENT"),
        )
        for label, options, expected_code in cases:
            with self.subTest(label=label), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                fixture = prepare_command_control_fixture(
                    root,
                    candidate_equals_control=True,
                    gate_id="W84-G1",
                    previous_gate_id="W84-G0",
                    **options,
                )
                problems = validate_command_control_fixture(root, fixture)
                self.assertTrue(
                    any(expected_code in item for item in problems), problems
                )

    def test_source_tree_binds_exact_prepared_paths_and_materialized_inventory(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = prepare_source_tree_fixture(root)
            selected, problems = validate_source_tree_fixture(root, fixture)
            self.assertEqual([], problems)
            self.assertEqual(fixture["selectedPaths"], selected)
            self.assertIn(".gitattributes", selected)
            self.assertNotIn("product.txt", selected)

    def test_source_tree_rejects_inexact_prepared_path_diff(self) -> None:
        for mutation in ("missing", "extra"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                fixture = prepare_source_tree_fixture(root)
                if mutation == "missing":
                    fixture["tree"]["preparedPaths"].pop()
                else:
                    fixture["tree"]["preparedPaths"].append(
                        "src/CSharpAiCli.Tests/CSharpAiCli.Tests.csproj"
                    )
                    fixture["tree"]["preparedPaths"].sort()
                _selected, problems = validate_source_tree_fixture(root, fixture)
                self.assertTrue(
                    any("COMMAND_CONTROL_SOURCE_TREE_IDENTITY" in item for item in problems),
                    problems,
                )

    def test_source_tree_rejects_worktree_tamper_and_untracked_selected_input(
        self,
    ) -> None:
        for mutation in ("tamper", "untracked"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                fixture = prepare_source_tree_fixture(root)
                if mutation == "tamper":
                    (root / "src/CSharpAiCli.Tests/BaselineTests.cs").write_text(
                        "// worktree tamper\n", encoding="utf-8"
                    )
                else:
                    (root / "src/CSharpAiCli.Tests/UntrackedTests.cs").write_text(
                        "// untracked\n", encoding="utf-8"
                    )
                _selected, problems = validate_source_tree_fixture(root, fixture)
                self.assertTrue(
                    any("COMMAND_CONTROL_SOURCE_TREE_IDENTITY" in item for item in problems),
                    problems,
                )

    def test_source_tree_selects_only_attributes_that_can_affect_policy_inputs(
        self,
    ) -> None:
        targets = [
            "src/CSharpAiCli.Tests/Tests.cs",
            "src/Product/Product.csproj",
        ]
        self.assertTrue(
            validator._source_tree_path_selected(
                "csharp-test-tree-v1", ".gitattributes", targets
            )
        )
        self.assertTrue(
            validator._source_tree_path_selected(
                "csharp-test-tree-v1", "src/.gitattributes", targets
            )
        )
        self.assertTrue(
            validator._source_tree_path_selected(
                "csharp-test-tree-v1",
                "src/CSharpAiCli.Tests/.gitattributes",
                targets,
            )
        )
        self.assertFalse(
            validator._source_tree_path_selected(
                "csharp-test-tree-v1", "tools/.gitattributes", targets
            )
        )

    def test_prior_control_origin_resolves_only_earlier_passed_gate_registry(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = prepare_command_control_fixture(root)
            origin_gate = fixture["gates"][1]
            registry = {"W85-R1": origin_gate}
            accepted = validator.Problems()
            control = validator._canonical_prior_control_document(
                root,
                fixture["controlRevision"],
                "W85-R2",
                registry,
                accepted,
                "prior-control",
            )
            self.assertIsNotNone(control)
            self.assertEqual([], accepted.items)

            origin_gate.data["status"] = "Failed"
            rejected = validator.Problems()
            self.assertIsNone(
                validator._canonical_prior_control_document(
                    root,
                    fixture["controlRevision"],
                    "W85-R2",
                    registry,
                    rejected,
                    "prior-control",
                )
            )
            self.assertTrue(
                any("COMMAND_CONTROL_PRIOR_REGISTRY" in item for item in rejected.items),
                rejected.items,
            )

    def test_prior_control_origin_rejects_unregistered_ancestor(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = prepare_command_control_fixture(root)
            problems = validator.Problems()
            self.assertIsNone(
                validator._canonical_prior_control_document(
                    root,
                    fixture["predecessor"],
                    "W85-R2",
                    {"W85-R1": fixture["gates"][1]},
                    problems,
                    "prior-control",
                )
            )
            self.assertTrue(problems.items)

    def test_w84_command_source_projection_binds_complete_bootstrap_universe(
        self,
    ) -> None:
        projection = validator._w84_g0_expected_source_projection()
        self.assertEqual(43, len(validator.W84_G0_SOURCE_UNIVERSE))
        self.assertNotIn(
            validator.W84_G0_CONTROL_PATH,
            validator.W84_G0_SOURCE_UNIVERSE,
        )
        self.assertEqual(5 * 43, len(projection))
        for command_id in validator.W84_G0_ADAPTER_PATH_BY_COMMAND:
            command_projection = [
                (role, path)
                for projected_command, role, path in projection
                if projected_command == command_id
            ]
            self.assertEqual(
                set(validator.W84_G0_SOURCE_UNIVERSE),
                {path for _role, path in command_projection},
            )
            self.assertEqual(
                {"adapter": 1, "test": 1, "fixture": 36, "parser": 5},
                {
                    role: sum(
                        projected_role == role
                        for projected_role, _path in command_projection
                    )
                    for role in {"adapter", "test", "fixture", "parser"}
                },
            )
        self.assertEqual(
            set(validator.W84_G0_ADAPTER_PATH_BY_COMMAND),
            set(validator.W84_G0_VERIFICATION_ARGUMENT_BY_COMMAND),
        )

    def test_literal_dynamic_dependency_discovery_closes_w84_loaders(self) -> None:
        root = SCRIPT.parents[1]
        goal_adapter_path = (
            "tools/week84_92_commands/goal-contract-unittest.py"
        )
        adapter_dependencies, adapter_unresolved = (
            validator._command_source_literal_dependencies(
                root,
                goal_adapter_path,
                (root / goal_adapter_path).read_bytes(),
            )
        )
        self.assertFalse(adapter_unresolved)
        self.assertTrue(
            {
                "tools/test_validate_week84_92_goal_evidence.py",
                "tools/test_week84_92_goal_integrity.py",
                "tools/test_week84_92_gate_requirements.py",
                "tools/test_week84_92_evidence_anchor.py",
                "tools/test_week84_92_trusted_executor.py",
            }.issubset(adapter_dependencies)
        )
        verifier_dependencies, verifier_unresolved = (
            validator._command_source_literal_dependencies(
                root,
                validator.W84_G0_VERIFIER_PATH,
                (root / validator.W84_G0_VERIFIER_PATH).read_bytes(),
            )
        )
        self.assertFalse(verifier_unresolved)
        self.assertIn(
            "tools/validate-week84-92-goal-evidence.py",
            verifier_dependencies,
        )
        self.assertTrue(
            {
                "tools/week84_92_commands/goal-contract-unittest.py",
                "tools/week84_92_commands/trusted-executor-unittest.py",
                "tools/week84_92_commands/week83-lineage-identity-verify.py",
            }.issubset(verifier_dependencies)
        )

    def test_w84_literal_artifact_inputs_match_frozen_runtime_layout(self) -> None:
        root = SCRIPT.parents[1]
        expected_direct = {
            "goal-contract-unittest": set(),
            "trusted-executor-unittest": set(),
            "goal-control-schema-validate": {
                "artifacts/week84-92-goal-control/goal-state.json"
            },
            "prior-handoff-schema-validate": {
                "artifacts/week83-approval-projection-remediation/week84-handoff.json",
                "artifacts/week84-renderer-listener-retention/gate-evidence/"
                "W84-G0/week83-handoff-verification.json",
            },
            "week83-lineage-identity-verify": {
                "artifacts/week84-renderer-listener-retention/gate-evidence/"
                "W84-G0/entry.json"
            },
        }
        for command_id, adapter_path in (
            validator.W84_G0_ADAPTER_PATH_BY_COMMAND.items()
        ):
            with self.subTest(command_id=command_id):
                paths, unresolved = (
                    validator._command_source_literal_artifact_paths(
                        adapter_path,
                        (root / adapter_path).read_bytes(),
                    )
                )
                self.assertFalse(unresolved)
                self.assertEqual(expected_direct[command_id], paths)
                frozen = {
                    path
                    for path, _kind in validator.W84_G0_RUNTIME_INPUT_LAYOUT[
                        command_id
                    ]
                }
                self.assertTrue(paths <= frozen)

        injected, unresolved = validator._command_source_literal_artifact_paths(
            "tools/injected.py",
            b"from pathlib import Path\n"
            b"EXTRA = Path('artifacts/undeclared/new-input.json')\n"
            b"EXTRA.read_bytes()\n",
        )
        self.assertFalse(unresolved)
        self.assertEqual({"artifacts/undeclared/new-input.json"}, injected)

    def test_nonliteral_dynamic_repo_loader_fails_closed(self) -> None:
        dependencies, unresolved = validator._command_source_literal_dependencies(
            SCRIPT.parents[1],
            "tools/nonliteral.py",
            (
                b"import importlib.util\n"
                b"def load(path):\n"
                b"    return importlib.util.spec_from_file_location('x', path)\n"
            ),
        )
        self.assertEqual(set(), dependencies)
        self.assertTrue(unresolved)

    def test_implicit_and_aliased_dynamic_loaders_fail_closed(self) -> None:
        samples = {
            "getattr": (
                b"import importlib\n"
                b"f = getattr(importlib, 'import_module')\n"
                b"f('tools.hidden')\n"
            ),
            "alias": (
                b"import importlib\n"
                b"f = importlib.import_module\n"
                b"def load(name):\n    return f(name)\n"
            ),
            "eval": b"eval('1 + 1')\n",
            "source-loader": (
                b"from importlib.machinery import SourceFileLoader\n"
                b"SourceFileLoader('x', 'tools/x.py')\n"
            ),
            "discover": (
                b"import unittest\n"
                b"unittest.defaultTestLoader.discover('tools')\n"
            ),
            "load-tests-hook": (
                b"def load_tests(loader, tests, pattern):\n    return tests\n"
            ),
        }
        for name, raw in samples.items():
            with self.subTest(name=name):
                _dependencies, unresolved = (
                    validator._command_source_literal_dependencies(
                        SCRIPT.parents[1],
                        f"tools/{name}.py",
                        raw,
                    )
                )
                self.assertTrue(unresolved)

    def test_command_source_path_policy_separates_verifier_authority(self) -> None:
        def allowed(path: str, role: str = "fixture") -> bool:
            return validator._command_control_source_path_allowed(
                "W85-R1",
                {
                    "commandId": "sample-command",
                    "role": role,
                    "path": path,
                    "origin": "prepared",
                    "originControlRevision": None,
                    "verification": None,
                },
            )

        for path in (
            "src/CSharpAiCli.Tests/ApplicationArchitectureTests.cs",
            "src/CSharpAiCli.Tests/CSharpAiCli.Tests.csproj",
            "apps/desktop/src/renderer/App.test.tsx",
            "apps/desktop/src/main/window.test.ts",
            "apps/desktop/e2e/desktop-recovery.spec.ts",
            "apps/desktop/e2e/desktop-harness.ts",
            "apps/desktop/playwright.config.ts",
        ):
            self.assertTrue(allowed(path), path)
            self.assertFalse(allowed(path, "test"), path)
            self.assertFalse(allowed(path, "oracle"), path)
        self.assertTrue(allowed("tools/prior_verifier.py", "test"))
        for path in (
            "../tools/escape.py",
            "tools\\wrong-separator.py",
            "SRC/CSharpAiCli.Tests/ApplicationArchitectureTests.cs",
            "src/CSharpAiCli/Program.cs",
            "apps/desktop/src/renderer/App.tsx",
            "apps/desktop/package-lock.json",
            "apps/desktop/package.json",
            "apps/desktop/vite.config.ts",
            "apps/desktop/tsconfig.json",
            "tools/sitecustomize.py",
            "tools/injected.pth",
        ):
            self.assertFalse(allowed(path), path)

    def test_command_control_rejects_adapter_printing_passed_result(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = prepare_command_control_fixture(root, direct_result=True)
            self.assert_problem(
                validate_command_control_fixture(root, fixture),
                "COMMAND_CONTROL_DIRECT_RESULT",
            )

    def test_command_control_rejects_mutable_undeclared_helper_import(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = prepare_command_control_fixture(
                root, undeclared_helper=True
            )
            self.assert_problem(
                validate_command_control_fixture(root, fixture),
                "COMMAND_CONTROL_UNDECLARED_DEPENDENCY",
            )

    def test_command_control_rejects_wrong_prepared_predecessor(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = prepare_command_control_fixture(root, bad_predecessor=True)
            self.assert_problem(
                validate_command_control_fixture(root, fixture),
                "COMMAND_CONTROL_PREDECESSOR",
            )

    def test_command_control_rejects_merge_control_revision(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = prepare_command_control_fixture(root, merge_control=True)
            self.assert_problem(
                validate_command_control_fixture(root, fixture),
                "COMMAND_CONTROL_DIRECT_PARENT",
            )

    def test_command_control_rejects_post_control_oracle_rewrite_and_revert(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = prepare_command_control_fixture(root, rewrite_oracle=True)
            errors = validate_command_control_fixture(root, fixture)
            self.assert_problem(errors, "COMMAND_CONTROL_SOURCE_IDENTITY")
            self.assert_problem(errors, "COMMAND_CONTROL_POST_CONTROL_MUTATION")

    def test_command_control_rejects_cherry_picked_control_document(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = prepare_command_control_fixture(
                root, cherry_pick_control=True
            )
            self.assert_problem(
                validate_command_control_fixture(root, fixture),
                "COMMAND_CONTROL_DOCUMENT_IDENTITY",
            )

    def test_command_control_rejects_static_module_as_verifier_argument(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = prepare_command_control_fixture(
                root,
                static_verification_only=True,
            )
            self.assert_problem(
                validate_command_control_fixture(root, fixture),
                "COMMAND_CONTROL_VERIFICATION_SHAPE",
            )

    def test_runtime_driver_accepts_strict_prior_g5_fixture_preseal(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            binding = prepare_runtime_driver_binding_fixture(root)
            problems = validator.Problems()
            self.assertTrue(
                validator._validate_command_control_runtime_driver(
                    root,
                    binding,
                    binding["controlRevision"],
                    binding["preparedFromRevision"],
                    binding["candidate"],
                    problems,
                    "runtime-driver",
                )
            )
            self.assertEqual([], problems.items)

    def test_runtime_driver_rejects_current_control_self_origin(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            binding = prepare_runtime_driver_binding_fixture(root)
            binding["originControlRevision"] = binding["controlRevision"]
            problems = validator.Problems()
            self.assertFalse(
                validator._validate_command_control_runtime_driver(
                    root,
                    binding,
                    binding["controlRevision"],
                    binding["preparedFromRevision"],
                    binding["candidate"],
                    problems,
                    "runtime-driver",
                )
            )
            self.assert_problem(
                problems.items,
                "COMMAND_CONTROL_RUNTIME_DRIVER_IDENTITY",
            )

    def test_git_wrappers_ignore_ambient_repository_redirects(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            root = base / "root"
            decoy = base / "decoy"
            for repository, content in ((root, "root"), (decoy, "decoy")):
                repository.mkdir()
                _git(repository, "init", "--quiet")
                path = repository / "identity.txt"
                path.write_text(f"{content}\n", encoding="utf-8")
                (repository / ".gitignore").write_text(
                    ".env.local\n", encoding="utf-8"
                )
                _commit_fixture_paths(
                    repository,
                    f"{content} identity",
                    "identity.txt",
                    ".gitignore",
                )
            fake_index = base / "redirected.index"
            with patch.dict(
                os.environ,
                {
                    "GIT_DIR": str(decoy / ".git"),
                    "GIT_WORK_TREE": str(decoy),
                    "GIT_INDEX_FILE": str(fake_index),
                    "GIT_OBJECT_DIRECTORY": str(decoy / ".git/objects"),
                },
            ):
                observed = validator._git_stdout(
                    root, ("rev-parse", "--show-toplevel")
                )
                self.assertIsNotNone(observed)
                self.assertEqual(root.resolve(), Path(str(observed)).resolve())
                problems = validator.Problems()
                self.assertTrue(
                    validator.validate_git_repository_trust(root, problems)
                )
                self.assertEqual([], problems.items)

    def test_trusted_git_rejects_path_and_cwd_fake_executables(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            fake_root = Path(directory)
            fake_git = fake_root / "git.exe"
            fake_git.write_bytes(b"not the frozen git core\n")
            repo_root = SCRIPT.parents[1]
            executor = validator.trusted_executor
            original_cache = executor._TRUSTED_GIT_CACHE
            try:
                executor._TRUSTED_GIT_CACHE = None
                with patch.dict(
                    os.environ,
                    {"PATH": str(fake_root)},
                    clear=False,
                ):
                    resolved = validator._resolve_trusted_git(repo_root)
                self.assertIsNotNone(resolved)
                assert resolved is not None
                self.assertNotEqual(fake_git.resolve(), resolved)
                self.assertEqual(
                    validator.TRUSTED_GIT_EXECUTABLE_SHA256,
                    hashlib.sha256(resolved.read_bytes()).hexdigest(),
                )

                executor._TRUSTED_GIT_CACHE = None
                with patch.object(
                    executor,
                    "_trusted_git_candidates",
                    return_value=(fake_git,),
                ), patch.object(
                    executor.Path,
                    "cwd",
                    return_value=fake_root,
                ):
                    self.assertIsNone(validator._resolve_trusted_git(repo_root))
                    self.assertEqual(
                        (None, None),
                        validator.goal_integrity._git_bytes(
                            repo_root, ("rev-parse", "HEAD")
                        ),
                    )
                    self.assertEqual(
                        (127, b""),
                        validator.evidence_anchor._git(
                            repo_root, ("rev-parse", "HEAD")
                        ),
                    )
            finally:
                executor._TRUSTED_GIT_CACHE = original_cache

    def test_fixture_git_helper_never_executes_path_or_cwd_git(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            marker = root / "fake-git-ran.txt"
            if os.name == "nt":
                fake = root / "git.cmd"
                fake.write_text(
                    f"@echo invoked>\"{marker}\"\r\n@exit /b 0\r\n",
                    encoding="utf-8",
                )
            else:
                fake = root / "git"
                fake.write_text(
                    f"#!/bin/sh\nprintf invoked > '{marker}'\n",
                    encoding="utf-8",
                )
                fake.chmod(0o755)
            with patch.dict(os.environ, {"PATH": str(root)}, clear=False):
                _git(root, "init", "--quiet")
            self.assertTrue((root / ".git").is_dir())
            self.assertFalse(marker.exists())

    def test_git_trust_rejects_replace_refs_and_shallow_history(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            _git(root, "init", "--quiet")
            path = root / "history.txt"
            path.write_text("one\n", encoding="utf-8")
            (root / ".gitignore").write_text(
                ".env.local\n", encoding="utf-8"
            )
            first = _commit_fixture_paths(
                root, "first", "history.txt", ".gitignore"
            )
            path.write_text("two\n", encoding="utf-8")
            second = _commit_fixture_paths(root, "second", "history.txt")
            _git(root, "replace", first, second)
            replace_problems = validator.Problems()
            validator.validate_git_repository_trust(root, replace_problems)
            self.assert_problem(
                replace_problems.items, "GIT_TRUST_REPLACE_REFS"
            )
            _git(root, "replace", "-d", first)

            shallow_path = root / ".git/shallow"
            shallow_path.write_text(f"{first}\n", encoding="ascii")
            shallow_problems = validator.Problems()
            validator.validate_git_repository_trust(root, shallow_problems)
            self.assert_problem(shallow_problems.items, "GIT_TRUST_SHALLOW")
            shallow_path.unlink()

            grafts_path = root / ".git/info/grafts"
            grafts_path.parent.mkdir(parents=True, exist_ok=True)
            grafts_path.write_text(f"{second} {first}\n", encoding="ascii")
            graft_problems = validator.Problems()
            validator.validate_git_repository_trust(root, graft_problems)
            self.assert_problem(graft_problems.items, "GIT_TRUST_GRAFTS")

    def test_git_trust_rejects_partial_clone_and_gitlink_trees(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            _git(root, "init", "--quiet")
            (root / ".gitignore").write_text(
                ".env.local\n", encoding="utf-8"
            )
            tracked = root / "tracked.txt"
            tracked.write_text("tracked\n", encoding="utf-8")
            candidate = _commit_fixture_paths(
                root, "candidate", ".gitignore", "tracked.txt"
            )

            _git(root, "config", "remote.origin.promisor", "true")
            partial = validator.Problems()
            validator.validate_git_repository_trust(root, partial)
            self.assert_problem(partial.items, "GIT_TRUST_PARTIAL_CLONE")
            _git(root, "config", "--unset", "remote.origin.promisor")

            _git(
                root,
                "update-index",
                "--add",
                "--cacheinfo",
                f"160000,{candidate},vendor/dependency",
            )
            _git(
                root,
                "-c",
                "user.name=Goal Validator",
                "-c",
                "user.email=validator@example.invalid",
                "commit",
                "--quiet",
                "-m",
                "add forbidden gitlink",
            )
            gitlink = validator.Problems()
            validator.validate_git_repository_trust(root, gitlink)
            self.assert_problem(gitlink.items, "GIT_TRUST_SUBMODULE")

    def test_provider_source_binding_freezes_control_revision(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            _git(root, "init", "--quiet")
            relative_path = "tools/provider_source.py"
            source_path = root / relative_path
            source_path.parent.mkdir(parents=True, exist_ok=True)
            source_path.write_text("PHASE = 'test'\n", encoding="utf-8")
            (source_path.parent / ".gitattributes").write_text(
                "provider_source.py -text\n", encoding="utf-8"
            )
            candidate = _commit_fixture_paths(
                root,
                "add provider source",
                relative_path,
                "tools/.gitattributes",
            )
            raw = source_path.read_bytes()
            binding = {
                "path": relative_path,
                "sha256": hashlib.sha256(raw).hexdigest(),
                "gitBlobSha": _git(
                    root, "rev-parse", f"{candidate}:{relative_path}"
                ),
                "controlRevision": candidate,
            }
            problems = validator.Problems()
            self.assertTrue(
                validator._validate_runtime_source_binding(
                    binding,
                    relative_path,
                    binding["sha256"],
                    candidate,
                    root,
                    problems,
                    "provider-source-test",
                    "PROVIDER_SOURCE_TEST",
                )
            )
            self.assertEqual([], problems.items)

            binding["controlRevision"] = "0" * 40
            drift = validator.Problems()
            self.assertFalse(
                validator._validate_runtime_source_binding(
                    binding,
                    relative_path,
                    binding["sha256"],
                    candidate,
                    root,
                    drift,
                    "provider-source-test",
                    "PROVIDER_SOURCE_TEST",
                )
            )
            self.assert_problem(drift.items, "PROVIDER_SOURCE_TEST")

    def test_git_trust_rejects_tracked_or_historical_provider_env(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            _git(root, "init", "--quiet")
            (root / ".gitignore").write_text(
                ".env.local\n", encoding="utf-8"
            )
            _commit_fixture_paths(root, "ignore provider env", ".gitignore")
            clean = validator.Problems()
            self.assertTrue(
                validator.validate_git_repository_trust(root, clean)
            )
            self.assertEqual([], clean.items)

            secret = "sk-proj-provider-env-must-not-be-echoed"
            (root / validator.PROVIDER_ENV_PATH).write_text(
                f"OPENAI_API_KEY={secret}\n", encoding="utf-8"
            )
            _git(root, "add", "-f", validator.PROVIDER_ENV_PATH)
            _commit_fixture_paths(
                root, "forbidden provider env", validator.PROVIDER_ENV_PATH
            )
            tracked = validator.Problems()
            validator.validate_git_repository_trust(root, tracked)
            self.assert_problem(
                tracked.items, "GIT_TRUST_PROVIDER_ENV_TRACKED"
            )
            self.assert_problem(
                tracked.items, "GIT_TRUST_PROVIDER_ENV_HISTORY"
            )
            self.assertNotIn(secret, "\n".join(tracked.items))

            _git(root, "rm", "--quiet", validator.PROVIDER_ENV_PATH)
            _git(
                root,
                "-c",
                "user.name=Goal Validator",
                "-c",
                "user.email=validator@example.invalid",
                "commit",
                "--quiet",
                "-m",
                "remove forbidden provider env",
            )
            historical = validator.Problems()
            validator.validate_git_repository_trust(root, historical)
            self.assertNotIn(
                "GIT_TRUST_PROVIDER_ENV_TRACKED",
                "\n".join(historical.items),
            )
            self.assert_problem(
                historical.items, "GIT_TRUST_PROVIDER_ENV_HISTORY"
            )
            self.assertNotIn(secret, "\n".join(historical.items))

    def test_requirements_valid_gate_and_malformed_kind_are_schema_safe(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture, manifest, _requirement, document = prepare_requirements_gate(root)
            problems = validator.Problems()
            validator.validate_gate_requirements_manifest(
                manifest,
                None,
                [document],
                fixture["contract"],
                problems,
                repo_root=root,
            )
            self.assertEqual([], problems.items)

            document.data["evidence"][0]["kind"] = {"invalid": True}
            malformed = validator.Problems()
            validator.validate_gate_requirements_manifest(
                manifest,
                None,
                [document],
                fixture["contract"],
                malformed,
                repo_root=root,
            )
            self.assert_problem(
                malformed.items,
                "REQUIREMENTS_EVIDENCE_KIND_TYPE",
            )

    def test_requirements_minimum_counts_require_actual_passes(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture, manifest, requirement, document = prepare_requirements_gate(root)
            minimum = requirement["minimumTestCount"]
            document.data["testCounts"].update(
                {
                    "discovered": minimum,
                    "passed": 0,
                    "skipped": minimum,
                }
            )
            problems = validator.Problems()
            validator.validate_gate_requirements_manifest(
                manifest,
                None,
                [document],
                fixture["contract"],
                problems,
                repo_root=root,
            )
            self.assert_problem(problems.items, "REQUIREMENTS_TEST_COUNT")

    def test_requirements_commands_and_assertions_need_resolved_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture, manifest, _requirement, document = prepare_requirements_gate(root)
            document.data["commands"][0]["evidenceRefs"] = []
            document.data["acceptanceAssertions"][0]["evidenceRefs"] = [
                "missing-evidence"
            ]
            problems = validator.Problems()
            validator.validate_gate_requirements_manifest(
                manifest,
                None,
                [document],
                fixture["contract"],
                problems,
                repo_root=root,
            )
            self.assert_problem(
                problems.items,
                "REQUIREMENTS_COMMAND_EVIDENCE",
            )
            self.assert_problem(
                problems.items,
                "REQUIREMENTS_ASSERTION_EVIDENCE",
            )

    def test_requirements_evidence_cannot_borrow_basename_outside_gate_dir(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture, manifest, _requirement, document = prepare_requirements_gate(root)
            evidence = document.data["evidence"][0]
            evidence["path"] = f"artifacts/borrowed/{Path(evidence['path']).name}"
            evidence["sha256"] = write_json_evidence(
                root,
                evidence["path"],
                {
                    "gateId": document.data["gateId"],
                    "productCandidate": document.data["identity"]["productCandidate"],
                    "status": "Passed",
                },
            )
            problems = validator.Problems()
            validator.validate_gate_requirements_manifest(
                manifest,
                None,
                [document],
                fixture["contract"],
                problems,
                repo_root=root,
            )
            self.assert_problem(problems.items, "REQUIREMENTS_EVIDENCE_PATH")

    def test_reserved_product_report_cannot_replace_planned_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture, manifest, requirement, document = prepare_requirements_gate(
                root, "W84-G0"
            )
            report_evidence = next(
                item
                for item in document.data["evidence"]
                if item["path"].endswith(".trusted-product-report.json")
            )
            planned_basename = requirement["requiredEvidenceBasenames"][0]
            planned_evidence = next(
                item
                for item in document.data["evidence"]
                if Path(item["path"]).name == planned_basename
            )
            document.data["evidence"].remove(planned_evidence)
            report = json.loads(
                (root / report_evidence["path"]).read_text(encoding="utf-8")
            )
            replacement_path = (
                requirement["resultPath"].rsplit("/gates/", 1)[0]
                + f"/gate-evidence/W84-G0/{planned_basename}"
            )
            for stream_name in ("stdout", "stderr"):
                original = root / report[stream_name]["path"]
                replacement_stream = (
                    replacement_path.removesuffix(".json")
                    + f".{stream_name}.log"
                )
                raw = original.read_bytes()
                absolute = root / replacement_stream
                absolute.parent.mkdir(parents=True, exist_ok=True)
                absolute.write_bytes(raw)
                report[stream_name] = {
                    "path": replacement_stream,
                    "sha256": hashlib.sha256(raw).hexdigest(),
                }
            report_evidence["path"] = replacement_path
            report_evidence["sha256"] = write_json_evidence(
                root, replacement_path, report
            )
            problems = validator.Problems()
            validator.validate_gate_requirements_manifest(
                manifest,
                None,
                [document],
                fixture["contract"],
                problems,
                repo_root=root,
            )
            self.assert_problem(problems.items, "PRODUCT_REPORT_PATH")

    def test_product_counts_cannot_be_replaced_by_semantic_counts(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture, manifest, _requirement, document = prepare_requirements_gate(
                root, "W85-R1"
            )
            report_evidence = next(
                item
                for item in document.data["evidence"]
                if item["path"].endswith(".trusted-product-report.json")
                and json.loads(
                    (root / item["path"]).read_text(encoding="utf-8")
                )["counts"]["discovered"]
                > 0
            )
            report = json.loads(
                (root / report_evidence["path"]).read_text(encoding="utf-8")
            )
            report["counts"] = {
                "discovered": 0,
                "passed": 0,
                "failed": 0,
                "skipped": 0,
            }
            marker = {
                "protocol": validator.TRUSTED_PRODUCT_RESULT_PROTOCOL,
                "status": "Passed",
                "counts": report["counts"],
            }
            stdout_raw = (
                b"product command completed\n"
                + b"CAICLI_PRODUCT_COMMAND_RESULT="
                + validator._json_bytes(marker)
                + b"\n"
            )
            stdout_path = root / report["stdout"]["path"]
            stdout_path.write_bytes(stdout_raw)
            report["stdout"]["sha256"] = hashlib.sha256(
                stdout_raw
            ).hexdigest()
            report_evidence["sha256"] = write_json_evidence(
                root, report_evidence["path"], report
            )
            problems = validator.Problems()
            validator.validate_gate_requirements_manifest(
                manifest,
                None,
                [document],
                fixture["contract"],
                problems,
                repo_root=root,
            )
            self.assert_problem(
                problems.items, "REQUIREMENTS_PRODUCT_TEST_COUNT"
            )

    def test_product_report_binds_candidate_script_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture, manifest, _requirement, document = prepare_requirements_gate(
                root, "W85-R1"
            )
            report_evidence = next(
                item
                for item in document.data["evidence"]
                if item["path"].endswith(".trusted-product-report.json")
            )
            report = json.loads(
                (root / report_evidence["path"]).read_text(encoding="utf-8")
            )
            script = root / report["scriptSource"]["path"]
            script.write_bytes(script.read_bytes() + b"# local rewrite\n")
            problems = validator.Problems()
            validator.validate_gate_requirements_manifest(
                manifest,
                None,
                [document],
                fixture["contract"],
                problems,
                repo_root=root,
            )
            self.assert_problem(problems.items, "PRODUCT_REPORT_SCRIPT_SOURCE")

    def test_requirements_json_and_test_report_payloads_are_bound(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture, manifest, _requirement, document = prepare_requirements_gate(root)
            evidence = document.data["evidence"][0]
            evidence["sha256"] = write_json_evidence(root, evidence["path"], {})
            problems = validator.Problems()
            validator.validate_gate_requirements_manifest(
                manifest,
                None,
                [document],
                fixture["contract"],
                problems,
                repo_root=root,
            )
            self.assert_problem(
                problems.items,
                "REQUIREMENTS_EVIDENCE_PAYLOAD_BINDING",
            )

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture, manifest, _requirement, document = prepare_requirements_gate(
                root,
                "W85-R1",
            )
            report = next(
                item
                for item in document.data["evidence"]
                if item["kind"] == "test-report"
            )
            report["sha256"] = write_json_evidence(
                root,
                report["path"],
                {"gateId": "W85-R1", "status": "Passed"},
            )
            problems = validator.Problems()
            validator.validate_gate_requirements_manifest(
                manifest,
                None,
                [document],
                fixture["contract"],
                problems,
                repo_root=root,
            )
            self.assert_problem(
                problems.items,
                "REQUIREMENTS_EVIDENCE_TEST_REPORT_SHAPE",
            )
            self.assert_problem(
                problems.items,
                "REQUIREMENTS_EVIDENCE_TEST_REPORT_BINDING",
            )

    def test_frozen_unittest_stdout_parser_is_exact_and_unambiguous(self) -> None:
        canonical = (
            b"OK: schema + semantic validation passed.\n..s\n"
            + b"-" * 70
            + b"\nRan 3 tests in 0.125s\n\nOK (skipped=1)\n"
        )
        self.assertEqual(
            {
                "discovered": 3,
                "passed": 2,
                "failed": 0,
                "skipped": 1,
                "notRun": 0,
                "notApplicable": 0,
            },
            validator._parse_frozen_unittest_stdout(canonical),
        )
        self.assertEqual(
            {
                "discovered": 1,
                "passed": 1,
                "failed": 0,
                "skipped": 0,
                "notRun": 0,
                "notApplicable": 0,
            },
            validator._parse_frozen_unittest_stdout(
                (b".\r\n" + b"-" * 70)
                + b"\r\nRan 1 test in 0.001s\r\n\r\nOK\r\n"
            ),
        )
        malformed = (
            canonical + canonical,
            b"Ran 99 tests in 9.999s\n" + canonical,
            canonical.replace(b"OK (skipped=1)", b"OK (skipped=0)"),
            canonical.replace(
                b"OK (skipped=1)",
                b"OK (skipped=1, expected failures=1)",
            ),
            canonical.replace(b"Ran 3 tests", b"Ran 3 test"),
            canonical.replace(b"Ran 3 tests", b"Ran 0 tests"),
            canonical.replace(b"0.125s", b"0.12s"),
            canonical.replace(b"\nRan 3 tests", b"\rRan 3 tests"),
            canonical + b"trailing output\n",
        )
        for stdout_raw in malformed:
            with self.subTest(stdout=stdout_raw[-96:]):
                self.assertIsNone(
                    validator._parse_frozen_unittest_stdout(stdout_raw)
                )

    def test_forged_report_counts_cannot_override_bound_stdout(self) -> None:
        stdout_raw = (
            b"..s\n"
            + b"-" * 70
            + b"\nRan 3 tests in 0.125s\n\nOK (skipped=1)\n"
        )
        forged_report = {
            "countsSource": "python-unittest-output",
            "testCounts": {
                "discovered": 3,
                "passed": 3,
                "failed": 0,
                "skipped": 0,
                "notRun": 0,
                "notApplicable": 0,
            },
        }
        problems = validator.Problems()
        self.assertFalse(
            validator._validate_frozen_unittest_report_counts(
                forged_report,
                stdout_raw,
                problems,
                "test-report",
            )
        )
        self.assert_problem(problems.items, "TEST_REPORT_STDOUT_COUNTS")

        exact_counts = {
            "discovered": 3,
            "passed": 2,
            "failed": 0,
            "skipped": 1,
            "notRun": 0,
            "notApplicable": 0,
        }
        malformed_reports = []
        missing_field = deepcopy(forged_report)
        missing_field["testCounts"] = deepcopy(exact_counts)
        del missing_field["testCounts"]["notApplicable"]
        malformed_reports.append(missing_field)
        extra_field = deepcopy(forged_report)
        extra_field["testCounts"] = {
            **exact_counts,
            "callerClaim": 1,
        }
        malformed_reports.append(extra_field)
        boolean_count = deepcopy(forged_report)
        boolean_count["testCounts"] = {
            **exact_counts,
            "failed": False,
        }
        malformed_reports.append(boolean_count)
        wrong_source = deepcopy(forged_report)
        wrong_source["testCounts"] = deepcopy(exact_counts)
        wrong_source["countsSource"] = "caller-report"
        malformed_reports.append(wrong_source)
        for malformed_report in malformed_reports:
            with self.subTest(report=malformed_report):
                malformed_problems = validator.Problems()
                self.assertFalse(
                    validator._validate_frozen_unittest_report_counts(
                        malformed_report,
                        stdout_raw,
                        malformed_problems,
                        "test-report",
                    )
                )
                self.assert_problem(
                    malformed_problems.items,
                    "TEST_REPORT_STDOUT_COUNTS",
                )

    def test_test_report_provenance_binds_frozen_runner_and_gate_window(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            _fixture, manifest, requirement, document = prepare_requirements_gate(
                root, "W85-R1"
            )
            report_evidence = next(
                item
                for item in document.data["evidence"]
                if item["kind"] == "test-report"
            )
            report_path = root / report_evidence["path"]
            payload_document = validator.read_document(
                root, report_path, validator.Problems()
            )
            self.assertIsNotNone(payload_document)
            assert payload_document is not None
            policy = {
                "runnerId": validator.TEST_RUNNER_ID,
                "sourcePath": validator.TEST_RUNNER_PATH,
                "sourceSha256": payload_document.data["runnerSource"]["sha256"],
                "shellAllowed": False,
            }
            test_policy = fixture_trusted_test_policy(manifest, requirement)
            git_policy = deepcopy(
                manifest.data["frozenPolicies"]["trusted-git"]
            )

            def validate_report(
                payload: dict,
                report_problems: validator.Problems,
            ) -> bool:
                with (
                    patch.object(
                        validator,
                        "_validate_semantic_sources",
                        lambda *_args, **_kwargs: True,
                    ),
                    patch.object(
                        validator,
                        "_validate_semantic_dependency_tree",
                        lambda *_args, **_kwargs: True,
                    ),
                    patch.object(
                        validator,
                        "_validate_semantic_runtime_input_tree",
                        lambda *_args, **_kwargs: True,
                    ),
                    patch.object(
                        validator,
                        "_expected_python_tool_identity",
                        lambda: {},
                    ),
                    patch.object(
                        validator,
                        "_validate_semantic_git_tool_identity",
                        lambda *_args, **_kwargs: True,
                    ),
                ):
                    return validator._validate_test_report_provenance(
                        payload,
                        payload_document,
                        document.data,
                        root,
                        report_problems,
                        "test-report",
                        policy,
                        test_policy,
                        git_policy,
                        set(requirement["requiredEvidenceBasenames"]),
                    )

            problems = validator.Problems()
            self.assertTrue(
                validate_report(payload_document.data, problems),
                problems.items,
            )
            self.assertEqual([], problems.items)

            malformed = deepcopy(payload_document.data)
            malformed["countsSource"] = "untrusted-claim"
            malformed["gateId"] = "W84-G0"
            malformed["stderr"] = deepcopy(malformed["stdout"])
            malformed["commandPolicy"]["sha256"] = "0" * 64
            malformed["argvSha256"] = "0" * 64
            problems = validator.Problems()
            self.assertFalse(
                validate_report(malformed, problems)
            )
            self.assert_problem(problems.items, "TEST_REPORT_PROVENANCE")
            self.assert_problem(
                problems.items, "TEST_REPORT_COMMAND_POLICY"
            )
            self.assert_problem(
                problems.items, "TEST_REPORT_IDENTITY_BINDING"
            )
            self.assert_problem(problems.items, "TEST_REPORT_STREAM_PATH")

    def test_test_report_runner_rewrite_then_revert_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            _fixture, manifest, requirement, document = prepare_requirements_gate(
                root, "W85-R1"
            )
            report_evidence = next(
                item
                for item in document.data["evidence"]
                if item["kind"] == "test-report"
            )
            payload_document = validator.read_document(
                root, root / report_evidence["path"], validator.Problems()
            )
            assert payload_document is not None
            runner = root / validator.TEST_RUNNER_PATH
            original = runner.read_bytes()
            runner.write_bytes(original + b"\n# mutation\n")
            _git(root, "add", validator.TEST_RUNNER_PATH)
            _git(
                root,
                "-c",
                "user.name=Goal Validator",
                "-c",
                "user.email=validator@example.invalid",
                "commit",
                "--quiet",
                "-m",
                "mutate runner",
            )
            runner.write_bytes(original)
            _git(root, "add", validator.TEST_RUNNER_PATH)
            _git(
                root,
                "-c",
                "user.name=Goal Validator",
                "-c",
                "user.email=validator@example.invalid",
                "commit",
                "--quiet",
                "-m",
                "revert runner",
            )
            policy = {
                "runnerId": validator.TEST_RUNNER_ID,
                "sourcePath": validator.TEST_RUNNER_PATH,
                "sourceSha256": hashlib.sha256(original).hexdigest(),
                "shellAllowed": False,
            }
            test_policy = fixture_trusted_test_policy(manifest, requirement)
            git_policy = deepcopy(
                manifest.data["frozenPolicies"]["trusted-git"]
            )
            problems = validator.Problems()
            validator._validate_test_report_provenance(
                payload_document.data,
                payload_document,
                document.data,
                root,
                problems,
                "test-report",
                policy,
                test_policy,
                git_policy,
                set(requirement["requiredEvidenceBasenames"]),
            )
            self.assert_problem(
                problems.items, "TEST_REPORT_RUNNER_HISTORY"
            )

    def test_duplicate_json_keys_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / "duplicate.json"
            path.write_text('{"gateId":"W84-G0","gateId":"W84-G1"}', encoding="utf-8")
            problems = validator.Problems()
            self.assertIsNone(validator.read_document(root, path, problems))
            self.assert_problem(problems.items, "JSON_PARSE")

    def test_discovery_ignores_legacy_and_unapproved_handoffs(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            artifacts = root / "artifacts"
            canonical_gate = (
                root
                / validator.GOAL_ARTIFACT_DIR_BY_GROUP[(84, "baseline")]
                / "gates/W84-G0.json"
            )
            canonical_gate.parent.mkdir(parents=True)
            canonical_gate.write_text("{}", encoding="utf-8")
            for relative_path in (
                validator.W84_CANONICAL_HANDOFF_PATH,
                validator.W84_COMPAT_HANDOFF_PATH,
            ):
                path = root / relative_path
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("{}", encoding="utf-8")
            legacy_gate = artifacts / "week83-legacy/gates/W83-G0.json"
            legacy_gate.parent.mkdir(parents=True)
            legacy_gate.write_text("{}", encoding="utf-8")
            unapproved = canonical_gate.parents[1] / "legacy-handoff.json"
            unapproved.write_text("{}", encoding="utf-8")
            problems = validator.Problems()
            gates, handoffs = validator.discover_goal_evidence_paths(
                root, artifacts, problems
            )
            self.assertEqual([], problems.items)
            self.assertEqual([canonical_gate.resolve()], gates)
            self.assertEqual(
                {
                    (root / validator.W84_CANONICAL_HANDOFF_PATH).resolve(),
                    (root / validator.W84_COMPAT_HANDOFF_PATH).resolve(),
                },
                set(handoffs),
            )

    def test_week84_alias_must_be_canonically_equivalent(self) -> None:
        fixture = make_fixture()
        alias = next(
            item
            for item in fixture["handoffs"]
            if item.relative_path == validator.W84_COMPAT_HANDOFF_PATH
        )
        alias.data["openIssues"]["other"] = 1
        self.assert_problem(validate(fixture), "W84_HANDOFF_ALIAS_MISMATCH")

    def test_explicit_handoff_must_still_use_canonical_path(self) -> None:
        fixture = make_fixture()
        original = fixture_handoff(fixture, 85, "renderer")
        replacement = validator.document_from_data(
            "artifacts/week85-renderer-feature-boundaries/not-canonical.json",
            deepcopy(original.data),
        )
        fixture["handoffs"][fixture["handoffs"].index(original)] = replacement
        self.assert_problem(validate(fixture), "HANDOFF_PATH_POLICY")

    def test_cli_rejects_every_repository_escape(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            outside = root.parent / "outside-goal-evidence"
            output = io.StringIO()
            with redirect_stdout(output):
                exit_code = validator.run_cli(
                    [
                        "--repo-root",
                        str(root),
                        "--schema-dir",
                        str(outside / "schemas"),
                        "--goal-state",
                        str(outside / "goal.json"),
                        "--artifacts-dir",
                        str(outside / "artifacts"),
                        "--gate-file",
                        str(outside / "gate.json"),
                        "--handoff-file",
                        str(outside / "handoff.json"),
                        "--json",
                    ]
                )
            payload = json.loads(output.getvalue())
            self.assertEqual(1, exit_code)
            self.assertGreaterEqual(
                sum("[PATH_ESCAPE]" in item for item in payload["errors"]), 5
            )

    def test_cli_wires_complete_mode_into_anchor_and_registry_validation(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            calls: list[tuple[Path, bool]] = []
            registry_calls: list[tuple[Path, bool]] = []
            original_anchor = (
                validator.evidence_anchor.validate_evidence_anchors
            )
            original_registry = (
                validator.evidence_anchor.validate_anchor_registry
            )

            def fake_anchor_validate(
                repo_root: Path,
                *,
                complete: bool = False,
                allow_unsealed_ready_group: str | None = None,
            ) -> list:
                calls.append((repo_root, complete))
                return [
                    validator.evidence_anchor.AnchorIssue(
                        "ANCHOR_INTEGRATION_SENTINEL",
                        "docs_md/weekly/84_92_evidence_anchors",
                    )
                ]

            def fake_registry_validate(
                repo_root: Path,
                *,
                complete: bool = False,
            ) -> list:
                registry_calls.append((repo_root, complete))
                return [
                    validator.evidence_anchor.AnchorIssue(
                        "REGISTRY_MISSING",
                        "docs_md/weekly/84_92_anchor_registry/w84-baseline.json",
                    )
                ]

            validator.evidence_anchor.validate_evidence_anchors = (
                fake_anchor_validate
            )
            validator.evidence_anchor.validate_anchor_registry = (
                fake_registry_validate
            )
            try:
                output = io.StringIO()
                with redirect_stdout(output):
                    exit_code = validator.run_cli(
                        [
                            "--repo-root",
                            str(root),
                            "--require-complete",
                            "--json",
                        ]
                    )
            finally:
                validator.evidence_anchor.validate_evidence_anchors = (
                    original_anchor
                )
                validator.evidence_anchor.validate_anchor_registry = (
                    original_registry
                )

            payload = json.loads(output.getvalue())
            self.assertEqual(1, exit_code)
            self.assertEqual([(root.resolve(), True)], calls)
            self.assertEqual([(root.resolve(), True)], registry_calls)
            self.assertTrue(
                any(
                    "[ANCHOR_INTEGRATION_SENTINEL]" in item
                    for item in payload["errors"]
                )
            )
            self.assertTrue(
                any("[REGISTRY_MISSING]" in item for item in payload["errors"])
            )

    def test_cli_ordinary_mode_rejects_registry_cas_drift(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            calls: list[tuple[Path, bool]] = []

            def fake_registry_validate(
                repo_root: Path,
                *,
                complete: bool = False,
            ) -> list:
                calls.append((repo_root, complete))
                return [
                    validator.evidence_anchor.AnchorIssue(
                        "REGISTRY_ARTIFACT_DRIFT",
                        "artifacts/week84-renderer-listener-retention/"
                        "gate-evidence/W84-G0/baseline.json",
                    )
                ]

            output = io.StringIO()
            with (
                patch.object(
                    validator.evidence_anchor,
                    "validate_evidence_anchors",
                    lambda *_args, **_kwargs: [],
                ),
                patch.object(
                    validator.evidence_anchor,
                    "validate_anchor_registry",
                    fake_registry_validate,
                ),
                redirect_stdout(output),
            ):
                exit_code = validator.run_cli(
                    ["--repo-root", str(root), "--json"]
                )

            payload = json.loads(output.getvalue())
            self.assertEqual(1, exit_code)
            self.assertEqual([(root.resolve(), False)], calls)
            self.assertTrue(
                any(
                    "[REGISTRY_ARTIFACT_DRIFT]" in item
                    for item in payload["errors"]
                )
            )

    def test_cli_active_empty_anchor_registry_prefix_is_valid(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            goal = validator.Document(
                path=root
                / "artifacts/week84-92-goal-control/goal-state.json",
                relative_path=(
                    "artifacts/week84-92-goal-control/goal-state.json"
                ),
                data={
                    "status": "Active",
                    "identity": {"productCandidate": "a" * 40},
                    "authorization": {"provider": {}},
                },
                sha256="0" * 64,
            )
            calls: list[tuple[str, Path, bool]] = []

            def fake_read_document(
                _repo_root: Path,
                path: Path,
                _problems: validator.Problems,
            ) -> validator.Document | None:
                return goal if path.name == "goal-state.json" else None

            def fake_anchor_validate(
                repo_root: Path,
                *,
                complete: bool = False,
                allow_unsealed_ready_group: str | None = None,
            ) -> list:
                self.assertIsNone(allow_unsealed_ready_group)
                calls.append(("anchor", repo_root, complete))
                return []

            def fake_registry_validate(
                repo_root: Path,
                *,
                complete: bool = False,
            ) -> list:
                calls.append(("registry", repo_root, complete))
                return []

            output = io.StringIO()
            with (
                patch.object(
                    validator,
                    "validate_git_repository_trust",
                    lambda *_: True,
                ),
                patch.object(
                    validator,
                    "validate_bootstrap_control_plane",
                    lambda *_: None,
                ),
                patch.object(validator, "_load_schemas", lambda *_: {}),
                patch.object(validator, "read_document", fake_read_document),
                patch.object(
                    validator,
                    "discover_goal_evidence_paths",
                    lambda *_: ([], []),
                ),
                patch.object(
                    validator.goal_integrity,
                    "validate_goal_integrity",
                    lambda **_: [],
                ),
                patch.object(
                    validator.evidence_anchor,
                    "validate_evidence_anchors",
                    fake_anchor_validate,
                ),
                patch.object(
                    validator.evidence_anchor,
                    "validate_anchor_registry",
                    fake_registry_validate,
                ),
                patch.object(
                    validator,
                    "validate_gate_requirements_manifest",
                    lambda *_args, **_kwargs: None,
                ),
                patch.object(
                    validator,
                    "validate_semantics",
                    lambda *_args, **_kwargs: [],
                ),
                redirect_stdout(output),
            ):
                exit_code = validator.run_cli(
                    ["--repo-root", str(root), "--json"]
                )

            payload = json.loads(output.getvalue())
            self.assertEqual(0, exit_code, payload.get("errors"))
            self.assertTrue(payload["ok"])
            self.assertEqual(
                [
                    ("anchor", root.resolve(), False),
                    ("registry", root.resolve(), False),
                ],
                calls,
            )

    def test_cli_partial_mode_requires_registry_for_existing_anchor(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            spec = validator.evidence_anchor.CANONICAL_GROUPS[0]
            anchor_path = root.joinpath(
                *validator.PurePosixPath(spec.anchor_path).parts
            )
            anchor_path.parent.mkdir(parents=True)
            anchor_path.write_text("{}", encoding="utf-8")
            output = io.StringIO()
            with (
                patch.object(
                    validator.evidence_anchor,
                    "validate_evidence_anchors",
                    lambda *_args, **_kwargs: [],
                ),
                patch.object(
                    validator.evidence_anchor,
                    "validate_anchor_registry",
                    lambda *_args, **_kwargs: [],
                ),
                redirect_stdout(output),
            ):
                exit_code = validator.run_cli(
                    ["--repo-root", str(root), "--json"]
                )

            payload = json.loads(output.getvalue())
            self.assertEqual(1, exit_code)
            self.assertTrue(
                any("[REGISTRY_MISSING]" in item for item in payload["errors"])
            )

    def test_cli_preseal_captures_sources_then_writes_success_receipt(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            group_id = validator.evidence_anchor.CANONICAL_GROUPS[0].group_id
            goal = validator.Document(
                path=root
                / "artifacts/week84-92-goal-control/goal-state.json",
                relative_path=(
                    "artifacts/week84-92-goal-control/goal-state.json"
                ),
                data={
                    "status": "Active",
                    "identity": {"productCandidate": "a" * 40},
                    "authorization": {"provider": {}},
                },
                sha256="0" * 64,
            )
            order: list[str] = []
            source_bindings = {
                field: {"path": path, "sha256": "1" * 64}
                for field, path in (
                    validator.evidence_anchor.PRESEAL_SOURCE_PATHS.items()
                )
            }

            def fake_read_document(
                _repo_root: Path,
                path: Path,
                _problems: validator.Problems,
            ) -> validator.Document | None:
                return goal if path.name == "goal-state.json" else None

            def fake_capture(_repo_root: Path) -> dict:
                order.append("captured")
                return source_bindings

            def fake_semantics(*_args, **_kwargs) -> list[str]:
                order.append("validated")
                return []

            def fake_registry(
                repo_root: Path,
                *,
                complete: bool = False,
            ) -> list:
                self.assertEqual(root.resolve(), repo_root)
                self.assertFalse(complete)
                order.append("registry")
                return []

            def fake_write(
                _repo_root: Path,
                actual_group_id: str,
                product_candidate: str,
                **kwargs,
            ) -> dict:
                self.assertEqual(group_id, actual_group_id)
                self.assertEqual("a" * 40, product_candidate)
                self.assertEqual(0, kwargs["exit_code"])
                self.assertEqual(
                    source_bindings, kwargs["expected_source_bindings"]
                )
                self.assertTrue(kwargs["started_at"].endswith("Z"))
                self.assertTrue(kwargs["finished_at"].endswith("Z"))
                order.append("written")
                return {}

            output = io.StringIO()
            with (
                patch.object(
                    validator,
                    "validate_git_repository_trust",
                    lambda *_: True,
                ),
                patch.object(
                    validator, "validate_bootstrap_control_plane", lambda *_: None
                ),
                patch.object(validator, "_load_schemas", lambda *_: {}),
                patch.object(validator, "read_document", fake_read_document),
                patch.object(
                    validator,
                    "discover_goal_evidence_paths",
                    lambda *_: ([], []),
                ),
                patch.object(
                    validator.goal_integrity,
                    "validate_goal_integrity",
                    lambda **_: [],
                ),
                patch.object(
                    validator.evidence_anchor,
                    "validate_evidence_anchors",
                    lambda *_args, **_kwargs: [],
                ),
                patch.object(
                    validator.evidence_anchor,
                    "validate_anchor_registry",
                    fake_registry,
                ),
                patch.object(
                    validator,
                    "validate_gate_requirements_manifest",
                    lambda *_args, **_kwargs: None,
                ),
                patch.object(validator, "validate_semantics", fake_semantics),
                patch.object(
                    validator.evidence_anchor,
                    "capture_preseal_source_bindings",
                    fake_capture,
                ),
                patch.object(
                    validator.evidence_anchor,
                    "write_preseal_receipt_document",
                    fake_write,
                ),
                redirect_stdout(output),
            ):
                exit_code = validator.run_cli(
                    [
                        "--repo-root",
                        str(root),
                        "--pre-seal-group",
                        group_id,
                        "--json",
                    ]
            )
            self.assertEqual(0, exit_code)
            self.assertEqual(
                ["captured", "registry", "validated", "written"], order
            )

    def test_handoff_gate_hash_binds_raw_gate_document(self) -> None:
        fixture = make_fixture()
        original = fixture_gate(fixture, "W85-R0")
        replacement = validator.Document(
            path=original.path,
            relative_path=original.relative_path,
            data=original.data,
            sha256="c" * 64,
        )
        fixture["gates"][fixture["gates"].index(original)] = replacement
        self.assert_problem(validate(fixture), "HANDOFF_GATE_HASH")

    def test_evidence_hash_missing_escape_and_reference_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for case, expected_code in (
                ("hash", "EVIDENCE_HASH"),
                ("missing", "EVIDENCE_MISSING"),
                ("escape", "EVIDENCE_PATH_ESCAPE"),
                ("reference", "EVIDENCE_REF_UNRESOLVED"),
            ):
                with self.subTest(case=case):
                    fixture = make_fixture()
                    data = deepcopy(fixture_gate(fixture, "W85-R0").data)
                    evidence = data["evidence"][0]
                    if case == "hash":
                        evidence["path"] = "proof.json"
                        (root / "proof.json").write_bytes(b"actual")
                        evidence["sha256"] = "0" * 64
                    elif case == "missing":
                        evidence["path"] = "missing.json"
                        evidence["sha256"] = "0" * 64
                    elif case == "escape":
                        evidence["path"] = "../outside.json"
                        evidence["sha256"] = "0" * 64
                    else:
                        data["cleanup"]["evidenceRefs"] = ["not-a-gate-evidence-id"]
                    document = validator.document_from_data(data["resultPath"], data)
                    problems = validator.Problems()
                    validator.validate_gate(
                        document,
                        fixture["contract"],
                        problems,
                        repo_root=root,
                    )
                    self.assert_problem(problems.items, expected_code)

    def test_evidence_paths_reject_in_repo_links_and_allow_safe_missing_file(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            safe_parent = root / "artifacts" / "gate"
            safe_parent.mkdir(parents=True)
            real = safe_parent / "real.json"
            real.write_bytes(b"{}\n")

            hardlink = safe_parent / "hardlink.json"
            os.link(real, hardlink)
            hardlink_problems = validator.Problems()
            self.assertIsNone(
                validator._safe_relative_evidence_path(
                    root,
                    "artifacts/gate/hardlink.json",
                    hardlink_problems,
                    "hardlink",
                )
            )
            self.assert_problem(
                hardlink_problems.items, "EVIDENCE_PATH_HARDLINK"
            )

            missing_problems = validator.Problems()
            missing = validator._safe_relative_evidence_path(
                root,
                "artifacts/gate/not-created.json",
                missing_problems,
                "missing",
            )
            self.assertEqual(
                root / "artifacts" / "gate" / "not-created.json",
                missing,
            )
            self.assertEqual([], missing_problems.items)

            linked_directory = root / "linked-artifacts"
            try:
                linked_directory.symlink_to(
                    root / "artifacts", target_is_directory=True
                )
            except (OSError, NotImplementedError):
                return
            reparse_problems = validator.Problems()
            self.assertIsNone(
                validator._safe_relative_evidence_path(
                    root,
                    "linked-artifacts/gate/real.json",
                    reparse_problems,
                    "reparse",
                )
            )
            self.assert_problem(
                reparse_problems.items, "EVIDENCE_PATH_REPARSE"
            )

    def test_secret_detection_covers_model_base_url_and_never_echoes(self) -> None:
        secret_model = "private-model-name"
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = make_fixture()
            data = deepcopy(fixture_gate(fixture, "W85-R0").data)
            evidence = data["evidence"][0]
            evidence["path"] = "provider.json"
            evidence["sha256"] = write_json_evidence(
                root,
                evidence["path"],
                {
                    "OPENAI_MODEL": secret_model,
                    "baseUrl": "https://private.invalid/v1",
                },
            )
            document = validator.document_from_data(data["resultPath"], data)
            problems = validator.Problems()
            validator.validate_gate(
                document, fixture["contract"], problems, repo_root=root
            )
            self.assert_problem(problems.items, "SECRET_DETECTED")
            rendered = "\n".join(problems.items)
            self.assertNotIn(secret_model, rendered)
            self.assertNotIn("https://private.invalid/v1", rendered)

    def test_visual_containers_are_structurally_validated_fail_closed(self) -> None:
        png_path = Path("evidence.png")
        valid_png = minimal_png()
        self.assertTrue(
            validator._visual_magic_matches("screenshot", png_path, valid_png)
        )
        self.assertFalse(
            validator._visual_magic_matches(
                "screenshot", png_path, valid_png[:-1]
            )
        )
        self.assertFalse(
            validator._visual_magic_matches(
                "screenshot", png_path, valid_png + b"trailing"
            )
        )
        corrupt_png = bytearray(valid_png)
        corrupt_png[-5] ^= 1
        self.assertFalse(
            validator._visual_magic_matches(
                "screenshot", png_path, bytes(corrupt_png)
            )
        )

        webm = (
            bytes.fromhex("1a45dfa3")
            + b"\x80"
            + bytes.fromhex("18538067")
            + b"\x8a"
            + bytes.fromhex("1654ae6b")
            + b"\x80"
            + bytes.fromhex("1f43b675")
            + b"\x80"
        )
        self.assertTrue(
            validator._visual_magic_matches("video", Path("evidence.webm"), webm)
        )
        self.assertFalse(
            validator._visual_magic_matches(
                "video", Path("evidence.webm"), webm[:-1]
            )
        )

        def mp4_box(kind: bytes, payload: bytes = b"") -> bytes:
            return validator.struct.pack(">I", 8 + len(payload)) + kind + payload

        mp4 = (
            mp4_box(b"ftyp", b"isom\0\0\0\0")
            + mp4_box(b"moov")
            + mp4_box(b"mdat")
        )
        self.assertTrue(
            validator._visual_magic_matches("video", Path("evidence.mp4"), mp4)
        )
        self.assertFalse(
            validator._visual_magic_matches(
                "video", Path("evidence.mp4"), mp4[:-1]
            )
        )

    def test_compressed_png_and_odd_utf16_secrets_are_detected(self) -> None:
        secret = b"sk-proj-hidden-in-compressed-metadata"
        ztxt = png_chunk(
            b"zTXt",
            b"comment\0\0" + validator.zlib.compress(secret),
        )
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            png_path = root / "secret.png"
            png_path.write_bytes(minimal_png(metadata_chunks=(ztxt,)))
            problems = validator.Problems()
            validator._scan_text_evidence_for_secrets(
                png_path, "screenshot", problems, "secret.png"
            )
            self.assert_problem(problems.items, "SECRET_DETECTED")

            utf16_path = root / "odd-offset.log"
            utf16_path.write_bytes(b"x" + secret.decode("ascii").encode("utf-16-le"))
            problems = validator.Problems()
            validator._scan_text_evidence_for_secrets(
                utf16_path, "text-log", problems, "odd-offset.log"
            )
            self.assert_problem(problems.items, "SECRET_DETECTED")

    def test_schema_errors_do_not_echo_secret_instances(self) -> None:
        from jsonschema import Draft202012Validator, FormatChecker

        secret = "sk-proj-this-value-must-never-be-echoed"
        instance = validator.document_from_data("instance.json", {"apiKey": secret})
        schema = validator.document_from_data(
            "schema.json",
            {
                "type": "object",
                "properties": {"apiKey": {"const": "<redacted>"}},
            },
        )
        problems = validator.Problems()
        validator._schema_validate(
            instance,
            schema,
            problems,
            Draft202012Validator,
            FormatChecker(),
        )
        validator._scan_value_for_secrets(instance.data, problems, "instance.json#")
        self.assert_problem(problems.items, "SCHEMA")
        self.assert_problem(problems.items, "SECRET_DETECTED")
        self.assertNotIn(secret, "\n".join(problems.items))

    def test_evidence_ids_are_goal_global_unique(self) -> None:
        fixture = make_fixture()
        first = fixture_gate(fixture, "W85-R0").data["evidence"][0]["evidenceId"]
        second_gate = fixture_gate(fixture, "W85-R1").data
        old = second_gate["evidence"][0]["evidenceId"]
        second_gate["evidence"][0]["evidenceId"] = first
        second_gate["commands"][0]["evidenceRefs"] = [first]
        second_gate["acceptanceAssertions"][0]["evidenceRefs"] = [first]
        second_gate["cleanup"]["evidenceRefs"] = [first]
        self.assertNotEqual(first, old)
        self.assert_problem(validate(fixture), "EVIDENCE_ID_GLOBAL_DUPLICATE")

        fixture = make_fixture()
        first_path = fixture_gate(fixture, "W85-R0").data["evidence"][0][
            "path"
        ]
        fixture_gate(fixture, "W85-R1").data["evidence"][0][
            "path"
        ] = first_path
        self.assert_problem(
            validate(fixture), "EVIDENCE_PATH_GLOBAL_DUPLICATE"
        )

    def test_provider_zero_turn_and_frozen_minima_are_rejected(self) -> None:
        fixture = make_fixture()
        auth = fixture_gate(fixture, "W84-G6").data["authorization"]
        auth["providerTurnsConsumed"] = 0
        auth["providerTurnsAfter"] = auth["providerTurnsBefore"]
        auth["providerSequenceAfter"] = auth["providerSequenceBefore"]
        errors = validate(fixture)
        self.assert_problem(errors, "PROVIDER_SCOPE_ZERO_TURN")
        self.assert_problem(errors, "PROVIDER_REQUIRED_TURNS")

        fixture = make_fixture()
        auth = fixture_gate(fixture, "W92-G7").data["authorization"]
        auth["providerTurnsConsumed"] = 33
        self.assert_problem(validate(fixture), "PROVIDER_REQUIRED_TURNS")

    def test_provider_secret_disposition_cannot_claim_child_isolation(self) -> None:
        fixture = make_fixture()
        provider = fixture["goal"].data["authorization"]["provider"]
        self.assertEqual(
            validator.PROVIDER_SECRET_DISPOSITION,
            provider["secretDisposition"],
        )
        provider["secretDisposition"] = (
            "isolated-child-only-redacted-from-output-logs-evidence-and-commits"
        )
        self.assert_problem(
            validate(fixture), "PROVIDER_SECRET_DISPOSITION"
        )

    def test_passed_provider_gate_requires_complete_boundary_launch_bindings(
        self,
    ) -> None:
        fixture = make_fixture()
        gate = fixture_gate(fixture, "W84-G6").data
        gate["authorization"]["providerBoundaryDecision"] = None
        gate["authorization"]["packageLaunchReceipts"] = []
        self.assert_problem(validate(fixture), "PROVIDER_BOUNDARY_REQUIRED")

    def test_w84_provider_gates_share_one_boundary_decision_binding(self) -> None:
        fixture = make_fixture()
        fixture_gate(fixture, "W84-G7").data["authorization"][
            "providerBoundaryDecision"
        ]["decisionId"] = "different-boundary"
        self.assert_problem(validate(fixture), "PROVIDER_BOUNDARY_SHARED_W84")

    def test_nonprovider_gate_cannot_claim_provider_boundary_launch(self) -> None:
        fixture = make_fixture()
        source = fixture_gate(fixture, "W84-G6").data["authorization"]
        target = fixture_gate(fixture, "W85-R0").data["authorization"]
        target["providerBoundaryDecision"] = deepcopy(
            source["providerBoundaryDecision"]
        )
        target["packageLaunchReceipts"] = deepcopy(
            source["packageLaunchReceipts"]
        )
        self.assert_problem(validate(fixture), "PROVIDER_BOUNDARY_UNAUTHORIZED")

    def test_cooperative_provider_boundary_launch_proves_desktop_and_apphost(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = prepare_provider_boundary_deep_fixture(root)
            self.assertEqual(
                [],
                validate_provider_boundary_deep_fixture(root, fixture),
            )

    def test_cooperative_provider_boundary_rejects_isolation_claims(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = prepare_provider_boundary_deep_fixture(
                root, cooperative_isolation_claim=True
            )
            self.assert_problem(
                validate_provider_boundary_deep_fixture(root, fixture),
                "PROVIDER_PACKAGE_LAUNCH_CONTAINMENT",
            )

    def test_provider_package_launch_requires_real_apphost_child(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = prepare_provider_boundary_deep_fixture(root)
            binding = fixture["receipts"][0]
            receipt_path = root / binding["path"]
            receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
            del receipt["launches"]["appHost"]
            binding["sha256"] = write_json_evidence(
                root,
                binding["path"],
                receipt,
            )
            self.assert_problem(
                validate_provider_boundary_deep_fixture(root, fixture),
                "PROVIDER_PACKAGE_LAUNCH_PROCESS",
            )

    def test_provider_recovery_launch_requires_listener_observation(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = prepare_provider_boundary_deep_fixture(root)
            binding = fixture["receipts"][1]
            receipt_path = root / binding["path"]
            receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
            receipt["scenarioAssertions"]["recoveryListenerObserved"] = False
            binding["sha256"] = write_json_evidence(
                root,
                binding["path"],
                receipt,
            )
            self.assert_problem(
                validate_provider_boundary_deep_fixture(root, fixture),
                "PROVIDER_PACKAGE_LAUNCH_ASSERTIONS",
            )

    def test_provider_failed_prefix_then_final_passed_attempt_counts_every_turn(self) -> None:
        fixture = make_fixture()
        insert_provider_attempts(
            fixture,
            "W84-G6",
            [
                (
                    "W84-G6-attempt-failed-1",
                    "Failed",
                    ["provider-read-only", "provider-recovery"],
                )
            ],
        )

        self.assertEqual([], provider_accounting_problems(fixture))
        provider = fixture["goal"].data["authorization"]["provider"]
        authorization = fixture_gate(fixture, "W84-G6").data[
            "authorization"
        ]
        self.assertEqual(70, provider["usedTurns"])
        self.assertEqual(50, provider["remainingTurns"])
        self.assertEqual(5, authorization["providerTurnsConsumed"])
        self.assertEqual(5, authorization["providerTurnsAfter"])

    def test_provider_reservation_and_event_journals_are_exactly_bound(self) -> None:
        fixture = make_fixture()
        provider = fixture["goal"].data["authorization"]["provider"]
        ledger = fixture["ledger"].data
        self.assertEqual(len(ledger["entries"]), ledger["usedTurns"])
        self.assertEqual(
            len(ledger["attemptEvents"]), provider["attemptEventCount"]
        )
        self.assertEqual(
            ledger["lastAttemptEventSha256"],
            provider["lastAttemptEventSha256"],
        )
        self.assertEqual([], provider_accounting_problems(fixture))

    def test_provider_gate_local_bindings_prove_exact_canonical_prefixes(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = make_fixture()
            bindings = write_provider_ledger_bindings(root, fixture)
            self.assertEqual(
                [], provider_accounting_problems(fixture, repo_root=root)
            )

            relative_path, binding = bindings["W90-G0"]
            binding["attemptEventCount"] -= 1
            evidence = next(
                item
                for item in fixture_gate(fixture, "W90-G0").data[
                    "evidence"
                ]
                if Path(item["path"]).name
                == validator.PROVIDER_LEDGER_BINDING_BASENAME
            )
            evidence["sha256"] = write_json_evidence(
                root, relative_path, binding
            )
            self.assert_problem(
                provider_accounting_problems(fixture, repo_root=root),
                "PROVIDER_LEDGER_BINDING_PREFIX",
            )

    def test_provider_failed_runtime_batch_may_be_nullable_but_not_passed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = make_fixture()
            insert_provider_attempts(
                fixture,
                "W84-G6",
                [
                    (
                        "W84-G6-attempt-failed-runtime",
                        "Failed",
                        ["provider-read-only"],
                    )
                ],
            )
            write_provider_ledger_bindings(root, fixture)
            self.assertEqual(
                [], provider_accounting_problems(fixture, repo_root=root)
            )
            runtime_path = root / validator.PROVIDER_RUNTIME_JOURNAL_PATH
            runtime = json.loads(runtime_path.read_text(encoding="utf-8"))
            failed = [
                event
                for event in runtime["events"]
                if event["attemptId"] == "W84-G6-attempt-failed-runtime"
            ]
            self.assertTrue(failed)
            for event in failed:
                self.assertEqual("Failed", event["outcome"])
                for field in (
                    "commandPolicy",
                    "argvSha256",
                    "harnessSource",
                    "descriptor",
                    "observation",
                    "checkoutIdentity",
                    "observedRequest",
                ):
                    self.assertIsNone(event[field])

            failed[0]["outcome"] = "Succeeded"
            failed[0]["exitCode"] = 0
            failed[0]["eventSha256"] = validator.provider_runtime_event_sha256(
                failed[0]
            )
            runtime_path.write_bytes(validator._json_bytes(runtime))
            self.assert_problem(
                provider_accounting_problems(fixture, repo_root=root),
                "PROVIDER_RUNTIME_SUCCESS",
            )

    def test_handoff_snapshot_freezes_runtime_journal_prefix(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = make_fixture()
            write_provider_ledger_bindings(root, fixture)
            runtime = json.loads(
                (root / validator.PROVIDER_RUNTIME_JOURNAL_PATH).read_text(
                    encoding="utf-8"
                )
            )
            runtime_count = 3
            prefix = {
                "schemaVersion": validator.SCHEMA_VERSION,
                "journalVersion": validator.PROVIDER_RUNTIME_JOURNAL_VERSION,
                "goalId": validator.GOAL_ID,
                "eventCount": runtime_count,
                "lastEventSha256": runtime["events"][runtime_count - 1][
                    "eventSha256"
                ],
                "events": runtime["events"][:runtime_count],
            }
            snapshot_relative = "artifacts/week84-test/goal-control-snapshot.json"
            snapshot = {
                "authorization": {
                    "provider": {
                        "ledgerSequence": runtime_count,
                        "runtimeJournalPath": (
                            validator.PROVIDER_RUNTIME_JOURNAL_PATH
                        ),
                        "runtimeJournalSha256": hashlib.sha256(
                            validator._json_bytes(prefix)
                        ).hexdigest(),
                        "runtimeEventCount": runtime_count,
                        "lastRuntimeEventSha256": prefix[
                            "lastEventSha256"
                        ],
                    }
                }
            }
            write_json_evidence(root, snapshot_relative, snapshot)
            handoff = validator.document_from_data(
                "artifacts/week84-test/handoff.json",
                {
                    "goalControlBinding": {
                        "path": snapshot_relative,
                        "providerLedgerSequence": runtime_count,
                    }
                },
            )
            problems = validator.Problems()
            validator._validate_handoff_runtime_snapshots(
                [handoff], runtime["events"], root, problems
            )
            self.assertEqual([], problems.items)

            snapshot["authorization"]["provider"][
                "runtimeJournalSha256"
            ] = hashlib.sha256(
                validator._json_bytes(runtime)
            ).hexdigest()
            write_json_evidence(root, snapshot_relative, snapshot)
            drift = validator.Problems()
            validator._validate_handoff_runtime_snapshots(
                [handoff], runtime["events"], root, drift
            )
            self.assert_problem(drift.items, "HANDOFF_RUNTIME_SNAPSHOT")

    def test_provider_binding_path_and_every_gate_frontier_are_frozen(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = make_fixture()
            write_provider_ledger_bindings(root, fixture)
            evidence = next(
                item
                for item in fixture_gate(fixture, "W91-G4").data[
                    "evidence"
                ]
                if Path(item["path"]).name
                == validator.PROVIDER_LEDGER_BINDING_BASENAME
            )
            evidence["path"] = (
                "artifacts/week91-refactor-hardening/"
                "provider-ledger-binding.json"
            )
            self.assert_problem(
                provider_accounting_problems(fixture, repo_root=root),
                "PROVIDER_LEDGER_BINDING_PATH",
            )

        fixture = make_fixture()
        authorization = fixture_gate(fixture, "W90-G1").data[
            "authorization"
        ]
        authorization["providerTurnsBefore"] -= 1
        self.assert_problem(
            provider_accounting_problems(fixture),
            "PROVIDER_GATE_FRONTIER",
        )

        fixture = make_fixture()
        fixture_gate(fixture, "W85-R1").data["authorization"][
            "providerLedgerPrefixSha256"
        ] = "0" * 64
        self.assert_problem(
            provider_accounting_problems(fixture),
            "PROVIDER_LEDGER_BINDING_AUTHORIZATION",
        )

    def test_provider_receipt_attempt_matches_named_successful_attempt(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = make_fixture()
            write_provider_ledger_bindings(root, fixture)
            gate = fixture_gate(fixture, "W84-G6").data
            entry = next(
                item
                for item in fixture["ledger"].data["entries"]
                if item["gateId"] == "W84-G6"
                and item["phase"] == "provider-read-only"
            )
            evidence = next(
                item
                for item in gate["evidence"]
                if Path(item["path"]).name == "provider-readonly.json"
            )
            receipt = {
                "gateId": "W84-G6",
                "phase": "provider-read-only",
                "productCandidate": PRODUCT_CANDIDATE,
                "attemptId": "W84-G6-untrusted-attempt",
                "runId": entry["runId"],
                "sequenceStart": entry["sequence"],
                "sequenceEnd": entry["sequence"],
                "reservationIds": [entry["reservationId"]],
            }
            evidence["sha256"] = write_json_evidence(
                root, evidence["path"], receipt
            )
            self.assert_problem(
                provider_accounting_problems(fixture, repo_root=root),
                "PROVIDER_RECEIPT_ATTEMPT",
            )

    def test_every_reservation_needs_one_later_completion(self) -> None:
        fixture = make_fixture()
        events = fixture["ledger"].data["attemptEvents"]
        events.pop(0)
        rehash_provider_attempt_events(fixture)
        self.assert_problem(
            provider_accounting_problems(fixture),
            "PROVIDER_COMPLETION_CARDINALITY",
        )

        fixture = make_fixture()
        completion = fixture["ledger"].data["attemptEvents"][0]
        completion["completedAt"] = "2026-07-28T00:00:01Z"
        rehash_provider_attempt_events(fixture)
        self.assert_problem(
            provider_accounting_problems(fixture),
            "PROVIDER_COMPLETION_OUTCOME_TIME",
        )

    def test_provider_runtime_timestamps_stay_inside_gate_window(self) -> None:
        fixture = make_fixture()
        fixture["ledger"].data["entries"][0][
            "reservedAt"
        ] = "2026-07-27T23:59:59Z"
        rehash_provider_entries(fixture["ledger"].data)
        self.assert_problem(
            provider_accounting_problems(fixture),
            "PROVIDER_RESERVATION_GATE_WINDOW",
        )

        fixture = make_fixture()
        completion = fixture["ledger"].data["attemptEvents"][0]
        completion["completedAt"] = "2026-07-28T00:00:09Z"
        rehash_provider_attempt_events(fixture)
        self.assert_problem(
            provider_accounting_problems(fixture),
            "PROVIDER_COMPLETION_GATE_WINDOW",
        )

        fixture = make_fixture()
        finish = next(
            event
            for event in fixture["ledger"].data["attemptEvents"]
            if event.get("eventType") == "AttemptFinished"
            and event.get("gateId") == "W84-G6"
        )
        finish["finishedAt"] = "2026-07-28T00:00:09Z"
        rehash_provider_attempt_events(fixture)
        self.assert_problem(
            provider_accounting_problems(fixture),
            "PROVIDER_ATTEMPT_FINISH_BINDING",
        )

    def test_reservation_ids_are_global_and_times_are_monotonic(self) -> None:
        fixture = make_fixture()
        entries = fixture["ledger"].data["entries"]
        entries[1]["reservationId"] = entries[0]["reservationId"]
        rehash_provider_entries(fixture["ledger"].data)
        self.assert_problem(
            provider_accounting_problems(fixture),
            "PROVIDER_RESERVATION_DUPLICATE",
        )

        fixture = make_fixture()
        entries = fixture["ledger"].data["entries"]
        entries[1]["reservedAt"] = "2026-07-28T00:00:00Z"
        rehash_provider_entries(fixture["ledger"].data)
        self.assert_problem(
            provider_accounting_problems(fixture),
            "PROVIDER_LEDGER_TIME",
        )

    def test_attempt_finish_is_unique_and_after_all_completions(self) -> None:
        fixture = make_fixture()
        events = fixture["ledger"].data["attemptEvents"]
        finish = next(
            event
            for event in events
            if event.get("eventType") == "AttemptFinished"
        )
        events.append(deepcopy(finish))
        rehash_provider_attempt_events(fixture)
        self.assert_problem(
            provider_accounting_problems(fixture),
            "PROVIDER_ATTEMPT_FINISH_CARDINALITY",
        )

        fixture = make_fixture()
        finish = next(
            event
            for event in fixture["ledger"].data["attemptEvents"]
            if event.get("eventType") == "AttemptFinished"
        )
        finish["finishedAt"] = "2026-07-28T00:00:02Z"
        rehash_provider_attempt_events(fixture)
        self.assert_problem(
            provider_accounting_problems(fixture),
            "PROVIDER_ATTEMPT_FINISH_BINDING",
        )

    def test_only_final_active_nonpassed_attempt_may_remain_open(self) -> None:
        fixture = make_fixture()
        gate = fixture_gate(fixture, "W92-G7").data
        gate["status"] = "Failed"
        gate["authorization"]["providerSuccessfulAttemptId"] = None
        fixture["goal"].data["status"] = "Active"
        events = fixture["ledger"].data["attemptEvents"]
        events[:] = [
            event
            for event in events
            if not (
                event.get("eventType") == "AttemptFinished"
                and event.get("attemptId") == "W92-G7-attempt-1"
            )
        ]
        rehash_provider_attempt_events(fixture)
        self.assertEqual([], provider_accounting_problems(fixture))

        fixture["goal"].data["status"] = "Complete"
        self.assert_problem(
            provider_accounting_problems(fixture),
            "PROVIDER_OPEN_ATTEMPT",
        )

    def test_failed_controlled_write_attempt_cannot_be_retried(self) -> None:
        fixture = make_fixture()
        insert_provider_attempts(
            fixture,
            "W92-G7",
            [
                (
                    "W92-G7-controlled-failed",
                    "Failed",
                    validator._expected_provider_phases("W92-G7"),
                )
            ],
        )
        errors = provider_accounting_problems(fixture)
        self.assert_problem(errors, "PROVIDER_CONTROLLED_WRITE_LIMIT")
        self.assert_problem(errors, "PROVIDER_CONTROLLED_WRITE_RETRY")

    def test_reserved_failures_still_charge_the_120_turn_cap(self) -> None:
        fixture = make_fixture()
        full_layout = validator._expected_provider_phases("W92-G7")
        insert_provider_attempts(
            fixture,
            "W92-G7",
            [
                ("W92-G7-cap-failed-a", "Failed", full_layout),
                ("W92-G7-cap-failed-b", "Failed", full_layout),
            ],
        )
        self.assertGreater(fixture["ledger"].data["usedTurns"], 120)
        self.assert_problem(provider_accounting_problems(fixture), "PROVIDER_GOAL")

    def test_attempt_event_chain_and_central_binding_are_fail_closed(self) -> None:
        fixture = make_fixture()
        fixture["ledger"].data["attemptEvents"][0]["outcome"] = "Failed"
        self.assert_problem(
            provider_accounting_problems(fixture),
            "PROVIDER_ATTEMPT_EVENT_HASH",
        )

        fixture = make_fixture()
        fixture["goal"].data["authorization"]["provider"][
            "attemptEventCount"
        ] += 1
        self.assert_problem(
            provider_accounting_problems(fixture),
            "PROVIDER_GOAL_EVENT_BINDING",
        )

    def test_nonprovider_gate_cannot_own_a_reservation(self) -> None:
        fixture = make_fixture()
        fixture["ledger"].data["entries"][0]["gateId"] = "W91-G0"
        rehash_provider_entries(fixture["ledger"].data)
        self.assert_problem(
            provider_accounting_problems(fixture),
            "PROVIDER_LEDGER_GATE",
        )

    def test_provider_partial_passed_attempt_is_rejected(self) -> None:
        fixture = make_fixture()
        entries = fixture["ledger"].data["entries"]
        w84_g6_indexes = [
            index
            for index, entry in enumerate(entries)
            if entry["gateId"] == "W84-G6"
        ]
        del entries[w84_g6_indexes[-1]]
        rebuild_provider_accounting(fixture)

        self.assert_problem(
            provider_accounting_problems(fixture),
            "PROVIDER_SUCCESS_PHASE_LAYOUT",
        )

    def test_provider_closed_attempt_cannot_be_reopened(self) -> None:
        fixture = make_fixture()
        insert_provider_attempts(
            fixture,
            "W84-G6",
            [
                (
                    "W84-G6-attempt-retry-a",
                    "Failed",
                    ["provider-read-only"],
                ),
                (
                    "W84-G6-attempt-retry-b",
                    "Failed",
                    ["provider-read-only"],
                ),
                (
                    "W84-G6-attempt-retry-a",
                    "Failed",
                    ["provider-read-only"],
                ),
            ],
        )

        self.assert_problem(
            provider_accounting_problems(fixture),
            "PROVIDER_ATTEMPT_REOPENED",
        )

    def test_w91_provider_scope_and_turn_are_unauthorized(self) -> None:
        fixture = make_fixture()
        document = fixture_gate(fixture, "W91-G0")
        authorization = document.data["authorization"]
        authorization.update(
            {
                "scopesUsed": ["provider-read-only"],
                "providerTurnsConsumed": 1,
                "providerTurnsAfter": authorization["providerTurnsBefore"] + 1,
                "providerSequenceAfter": authorization[
                    "providerSequenceBefore"
                ]
                + 1,
                "providerSuccessfulAttemptId": "W91-G0-attempt-1",
            }
        )
        problems = validator.Problems()
        validator.validate_provider_gate_requirements(
            {"W91-G0": document}, None, problems
        )
        self.assert_problem(problems.items, "PROVIDER_GATE_UNAUTHORIZED")

    def test_w92_requires_complete_structured_provider_profiles(self) -> None:
        fixture = make_fixture()
        gate = fixture_gate(fixture, "W92-G7").data
        gate["evidence"] = [
            item
            for item in gate["evidence"]
            if Path(item["path"]).name != "provider-resource-profile-5.json"
        ]
        self.assert_problem(validate(fixture), "PROVIDER_RECEIPT_MISSING")

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = make_fixture()
            document = fixture_gate(fixture, "W92-G7")
            for evidence in document.data["evidence"]:
                name = Path(evidence["path"]).name
                if name == validator.PROVIDER_LEDGER_BINDING_BASENAME:
                    continue
                if name == "provider-readonly.json":
                    receipt = {"status": "Passed", "turnsConsumed": 1}
                elif name == "provider-recovery.json":
                    receipt = {"status": "Passed", "turnsConsumed": 2}
                elif name == "controlled-write.json":
                    receipt = {"status": "Passed", "turnsConsumed": 1}
                else:
                    ordinal = int(name.removesuffix(".json").rsplit("-", 1)[1])
                    receipt = {
                        "status": "Passed",
                        "turnsConsumed": 6,
                        "profileOrdinal": ordinal,
                        "warmupTurns": 1,
                        "measuredTurns": 4 if ordinal == 3 else 5,
                        "continuousRunId": "W92-G7-resource-continuous-run",
                    }
                evidence["sha256"] = write_json_evidence(
                    root, evidence["path"], receipt
                )
            problems = validator.Problems()
            validator.validate_provider_gate_requirements(
                {"W92-G7": document}, root, problems
            )
            self.assert_problem(problems.items, "PROVIDER_PROFILE_SHAPE")

    def test_provider_ledger_recomputes_hash_and_binds_candidate(self) -> None:
        fixture = make_fixture()
        fixture["ledger"].data["entries"][0]["phase"] = "provider-recovery"
        self.assert_problem(validate(fixture), "PROVIDER_ENTRY_HASH")

        fixture = make_fixture()
        fixture["ledger"].data["entries"][0]["productCandidate"] = "c" * 40
        rehash_provider_entries(fixture["ledger"].data)
        self.assert_problem(validate(fixture), "PROVIDER_LEDGER_CANDIDATE")

    def test_controlled_write_receipt_cannot_hide_extra_scope_or_replay(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = make_fixture()
            data = deepcopy(fixture_gate(fixture, "W84-G8").data)
            receipt = valid_controlled_write_receipt("W84-G8", 84)
            receipt["files"] = ["result.txt", "extra.txt"]
            receipt["validationCommand"] += " -property:Unexpected=true"
            receipt["durableApprovals"][1]["replayed"] = True
            evidence = data["evidence"][0]
            evidence["sha256"] = write_json_evidence(
                root, evidence["path"], receipt
            )
            document = validator.document_from_data(data["resultPath"], data)
            problems = validator.Problems()
            validator.validate_gate(
                document, fixture["contract"], problems, repo_root=root
            )
            validator.validate_controlled_write_receipts(
                {"W84-G8": document}, root, problems
            )
            self.assert_problem(problems.items, "CONTROLLED_WRITE_RECEIPT")

    def test_controlled_write_requires_full_approvals_and_explicit_cleanup(self) -> None:
        for case in ("approvals", "cleanup"):
            with self.subTest(case=case):
                receipt = valid_controlled_write_receipt("W84-G8", 84)
                if case == "approvals":
                    receipt.pop("durableApprovals")
                else:
                    receipt["cleanup"].pop("ownedTempPathsRemaining")
                problems = validator.Problems()
                validator._validate_controlled_write_receipt_payload(
                    receipt,
                    "controlled-write.json",
                    "W84-G8",
                    84,
                    PRODUCT_CANDIDATE,
                    "W84-G8-attempt-1",
                    {
                        document.data["gateId"]: document
                        for document in make_fixture()["gates"]
                    },
                    problems,
                )
                self.assert_problem(
                    problems.items, "CONTROLLED_WRITE_RECEIPT"
                )

    def test_controlled_write_requires_immutable_candidate_descendant_tombstone(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            receipt, gate_by_id, candidate = prepare_controlled_write_tombstone(
                root
            )
            problems = validator.Problems()
            validator._validate_controlled_write_receipt_payload(
                receipt,
                "controlled-write.json",
                "W84-G8",
                84,
                candidate,
                "W84-G8-attempt-1",
                gate_by_id,
                problems,
                root,
            )
            self.assertEqual([], problems.items)

            receipt["singleUseLease"]["consumedAt"] = (
                "2026-07-28T00:00:03Z"
            )
            problems = validator.Problems()
            validator._validate_controlled_write_receipt_payload(
                receipt,
                "controlled-write.json",
                "W84-G8",
                84,
                candidate,
                "W84-G8-attempt-1",
                gate_by_id,
                problems,
                root,
            )
            self.assert_problem(problems.items, "CONTROLLED_WRITE_LEASE")

    def test_controlled_write_tombstone_rewrite_then_revert_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            receipt, gate_by_id, candidate = prepare_controlled_write_tombstone(
                root
            )
            relative = receipt["controlledWriteTombstone"]["path"]
            path = root / relative
            original = path.read_bytes()
            path.write_bytes(original + b" ")
            _git(root, "add", relative)
            _git(
                root,
                "-c",
                "user.name=Goal Validator",
                "-c",
                "user.email=validator@example.invalid",
                "commit",
                "--quiet",
                "-m",
                "mutate tombstone",
            )
            path.write_bytes(original)
            _git(root, "add", relative)
            _git(
                root,
                "-c",
                "user.name=Goal Validator",
                "-c",
                "user.email=validator@example.invalid",
                "commit",
                "--quiet",
                "-m",
                "revert tombstone",
            )
            problems = validator.Problems()
            validator._validate_controlled_write_receipt_payload(
                receipt,
                "controlled-write.json",
                "W84-G8",
                84,
                candidate,
                "W84-G8-attempt-1",
                gate_by_id,
                problems,
                root,
            )
            self.assert_problem(
                problems.items, "CONTROLLED_WRITE_TOMBSTONE_HISTORY"
            )

    def test_controlled_write_alias_collision_is_rejected(self) -> None:
        receipt = valid_controlled_write_receipt("W84-G8", 84)
        receipt["workspace"] = receipt["workspaceName"]
        fixture = make_fixture()
        problems = validator.Problems()
        validator._validate_controlled_write_receipt_payload(
            receipt,
            "controlled-write.json",
            "W84-G8",
            84,
            PRODUCT_CANDIDATE,
            "W84-G8-attempt-1",
            {
                document.data["gateId"]: document
                for document in fixture["gates"]
            },
            problems,
        )
        self.assert_problem(
            problems.items, "CONTROLLED_WRITE_ALIAS_COLLISION"
        )

    def test_controlled_runtime_ids_are_unique_across_all_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            gate_by_id = {}
            for gate_id, suffix in (("W84-G8", "one"), ("W92-G7", "two")):
                relative = f"artifacts/{suffix}.json"
                write_json_evidence(
                    root,
                    relative,
                    {"approvalId": "shared-approval-id"},
                )
                gate_by_id[gate_id] = validator.document_from_data(
                    f"artifacts/{gate_id}.json",
                    {
                        "gateId": gate_id,
                        "evidence": [
                            {
                                "kind": "json",
                                "path": relative,
                            }
                        ],
                    },
                )
            problems = validator.Problems()
            validator._validate_global_controlled_runtime_ids(
                gate_by_id, root, problems
            )
            self.assert_problem(
                problems.items, "CONTROLLED_RUNTIME_ID_REPLAY"
            )

    def test_provider_receipt_binds_successful_reservation_ids(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = make_fixture()
            document = fixture_gate(fixture, "W84-G8")
            receipt = valid_controlled_write_receipt("W84-G8", 84)
            receipt["reservationIds"] = ["wrong-reservation"]
            evidence = next(
                item
                for item in document.data["evidence"]
                if Path(item["path"]).name == "controlled-write.json"
            )
            evidence["sha256"] = write_json_evidence(
                root, evidence["path"], receipt
            )
            problems = validator.Problems()
            validator.validate_provider_accounting(
                fixture["goal"].data,
                {
                    item.data["gateId"]: item
                    for item in fixture["gates"]
                },
                [],
                fixture["ledger"],
                False,
                problems,
                repo_root=root,
            )
            self.assert_problem(
                problems.items, "PROVIDER_RECEIPT_SEQUENCE"
            )

    def test_manual_gate_requires_hash_bound_user_receipt(self) -> None:
        fixture = make_fixture()
        gate = fixture_gate(fixture, "W86-R7").data
        gate["evidence"] = [
            item
            for item in gate["evidence"]
            if Path(item["path"]).name != "user-visual-confirmation.json"
        ]
        self.assert_problem(
            validate(fixture), "MANUAL_ACCEPTANCE_RECEIPT_MISSING"
        )

    def test_git_identity_checks_real_commits_and_cross_group_ancestry(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)

            def git(*arguments: str) -> str:
                return _git(root, *arguments)

            git("init", "--quiet")
            (root / "state.txt").write_text("one", encoding="utf-8")
            git("add", "state.txt")
            git(
                "-c",
                "user.name=Goal Validator",
                "-c",
                "user.email=validator@example.invalid",
                "commit",
                "--quiet",
                "-m",
                "one",
            )
            first = git("rev-parse", "HEAD")
            (root / "state.txt").write_text("two", encoding="utf-8")
            git("add", "state.txt")
            git(
                "-c",
                "user.name=Goal Validator",
                "-c",
                "user.email=validator@example.invalid",
                "commit",
                "--quiet",
                "-m",
                "two",
            )
            second = git("rev-parse", "HEAD")
            problems = validator.Problems()
            validator._validate_git_identities(
                [
                    (
                        "bad-document.json",
                        {
                            "sourceHead": first,
                            "baseline": first,
                            "productCandidate": second,
                            "checkpointCommit": first,
                        },
                    )
                ],
                {
                    (89, "renderer"): second,
                    (89, "cli"): second,
                    (90, "integration"): first,
                    (91, "hardening"): second,
                    (92, "acceptance"): second,
                },
                root,
                problems,
            )
            self.assert_problem(problems.items, "IDENTITY_DOCUMENT_ANCESTRY")
            self.assert_problem(problems.items, "IDENTITY_CROSS_GROUP_ANCESTRY")

    def test_group_gate_candidates_may_advance_only_by_ancestry(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            _git(root, "init", "--quiet")

            def commit(message: str, content: str) -> str:
                (root / "candidate.txt").write_text(content, encoding="utf-8")
                _git(root, "add", "candidate.txt")
                _git(
                    root,
                    "-c",
                    "user.name=Goal Validator",
                    "-c",
                    "user.email=validator@example.invalid",
                    "commit",
                    "--quiet",
                    "-m",
                    message,
                )
                return _git(root, "rev-parse", "HEAD")

            bootstrap_candidate = commit("bootstrap", "bootstrap")
            fixed_candidate = commit("fixed", "fixed")
            _git(root, "checkout", "--detach", bootstrap_candidate)
            fork_candidate = commit("fork", "fork")

            fixture = make_fixture()
            w84_group = fixture["contract"].group_by_key[(84, "baseline")]
            contract = validator.Contract(
                registry=w84_group.gate_ids,
                groups=(w84_group,),
            )
            gate_by_id = {
                gate_id: fixture_gate(fixture, gate_id)
                for gate_id in w84_group.gate_ids
            }
            handoffs = [
                document
                for document in fixture["handoffs"]
                if validator._mapping(document.data).get("week") == 84
            ]

            def bind_identity(identity: dict, candidate: str) -> None:
                identity["sourceHead"] = candidate
                if "baseline" in identity:
                    identity["baseline"] = bootstrap_candidate
                identity["productCandidate"] = candidate
                identity["checkpointCommit"] = candidate

            bind_identity(fixture["goal"].data["identity"], fixed_candidate)
            for index, gate_id in enumerate(w84_group.gate_ids):
                bind_identity(
                    gate_by_id[gate_id].data["identity"],
                    bootstrap_candidate if index < 2 else fixed_candidate,
                )
            for handoff in handoffs:
                bind_identity(handoff.data["identity"], fixed_candidate)
                handoff.data["gitCheckpoint"].update(
                    {
                        "checkpointCommit": fixed_candidate,
                        "localMergeCommit": fixed_candidate,
                    }
                )

            valid = validator.Problems()
            validator.validate_candidate_identity(
                fixture["goal"].data,
                gate_by_id,
                handoffs,
                contract,
                complete=False,
                problems=valid,
                repo_root=root,
            )
            self.assertEqual([], valid.items)

            bind_identity(
                gate_by_id["W84-G1"].data["identity"], fixed_candidate
            )
            bind_identity(
                gate_by_id["W84-G2"].data["identity"], bootstrap_candidate
            )
            reverse = validator.Problems()
            validator.validate_candidate_identity(
                fixture["goal"].data,
                gate_by_id,
                handoffs,
                contract,
                complete=False,
                problems=reverse,
                repo_root=root,
            )
            self.assert_problem(reverse.items, "CANDIDATE_GATE_ANCESTRY")

            bind_identity(
                gate_by_id["W84-G1"].data["identity"], bootstrap_candidate
            )
            bind_identity(
                gate_by_id["W84-G2"].data["identity"], fork_candidate
            )
            forked = validator.Problems()
            validator.validate_candidate_identity(
                fixture["goal"].data,
                gate_by_id,
                handoffs,
                contract,
                complete=False,
                problems=forked,
                repo_root=root,
            )
            self.assert_problem(forked.items, "CANDIDATE_GATE_ANCESTRY")

    def test_handoff_git_checkpoint_branch_and_ready_binding_are_exact(self) -> None:
        fixture = make_fixture()
        handoff = fixture_handoff(fixture, 85, "renderer").data
        handoff["gitCheckpoint"]["branch"] = "codex/wrong-branch"
        handoff["gitCheckpoint"]["checkpointCommit"] = "b" * 40
        errors = validate(fixture)
        self.assert_problem(errors, "HANDOFF_GIT_CHECKPOINT_BRANCH")
        self.assert_problem(errors, "HANDOFF_GIT_CHECKPOINT_BINDING")

    def test_handoff_git_checkpoint_commits_exist_and_descend_candidate(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)

            def git(*arguments: str) -> str:
                return _git(root, *arguments)

            def commit(message: str, content: str) -> str:
                (root / "checkpoint.txt").write_text(content, encoding="utf-8")
                git("add", "checkpoint.txt")
                git(
                    "-c",
                    "user.name=Goal Validator",
                    "-c",
                    "user.email=validator@example.invalid",
                    "commit",
                    "--quiet",
                    "-m",
                    message,
                )
                return git("rev-parse", "HEAD")

            git("init", "--quiet")
            candidate = commit("candidate", "candidate")
            checkpoint = commit("checkpoint", "checkpoint")

            fixture = make_fixture()
            original = fixture_handoff(fixture, 85, "renderer")
            data = deepcopy(original.data)
            data["identity"]["productCandidate"] = candidate
            data["identity"]["checkpointCommit"] = checkpoint
            data["gitCheckpoint"].update(
                {
                    "checkpointCommit": checkpoint,
                    "localMergeCommit": checkpoint,
                }
            )
            document = validator.document_from_data(original.relative_path, data)
            valid = validator.Problems()
            validator._validate_handoff_git_checkpoints([document], root, valid)
            self.assertEqual([], valid.items)

            missing_data = deepcopy(data)
            missing_data["identity"]["checkpointCommit"] = "f" * 40
            missing_data["gitCheckpoint"].update(
                {
                    "checkpointCommit": "f" * 40,
                    "localMergeCommit": "e" * 40,
                }
            )
            missing = validator.Problems()
            validator._validate_handoff_git_checkpoints(
                [
                    validator.document_from_data(
                        original.relative_path,
                        missing_data,
                    )
                ],
                root,
                missing,
            )
            self.assertGreaterEqual(
                sum(
                    item.startswith("[HANDOFF_GIT_CHECKPOINT_COMMIT_MISSING]")
                    for item in missing.items
                ),
                2,
            )

            git("checkout", "--orphan", "unrelated", "--quiet")
            unrelated = commit("unrelated", "unrelated")
            ancestry_data = deepcopy(data)
            ancestry_data["identity"]["checkpointCommit"] = unrelated
            ancestry_data["gitCheckpoint"].update(
                {
                    "checkpointCommit": unrelated,
                    "localMergeCommit": unrelated,
                }
            )
            ancestry = validator.Problems()
            validator._validate_handoff_git_checkpoints(
                [
                    validator.document_from_data(
                        original.relative_path,
                        ancestry_data,
                    )
                ],
                root,
                ancestry,
            )
            self.assertGreaterEqual(
                sum(
                    item.startswith("[HANDOFF_GIT_CHECKPOINT_ANCESTRY]")
                    for item in ancestry.items
                ),
                2,
            )

    def test_complete_identity_mismatch_is_rejected(self) -> None:
        fixture = make_fixture()
        fixture_handoff(fixture, 92, "acceptance").data["identity"][
            "productCandidate"
        ] = "c" * 40
        errors = validate(fixture)
        self.assert_problem(errors, "CANDIDATE_HANDOFF_MISMATCH")
        self.assert_problem(errors, "COMPLETE_CANDIDATE_MISMATCH")

    def test_first_failure_cannot_be_omitted_or_erased(self) -> None:
        fixture = make_fixture()
        gate = fixture_gate(fixture, "W85-R0").data
        evidence_id = gate["evidence"][0]["evidenceId"]
        gate["evidence"][0]["preservesFirstFailure"] = True
        gate["firstFailure"] = {
            "failureId": "failure-w85-r0",
            "observedAt": "2026-07-28T00:00:00Z",
            "commandId": gate["commands"][0]["commandId"],
            "phase": "test",
            "redactedCommand": "redacted",
            "exitCode": 1,
            "summary": "first observed failure",
            "classification": "Product",
            "preserved": True,
            "evidenceRefs": [evidence_id],
            "supersededBy": None,
        }
        self.assert_problem(validate(fixture), "HANDOFF_FIRST_FAILURE_SET")

        fixture = make_fixture()
        gate = fixture_gate(fixture, "W85-R0").data
        gate["evidence"][0]["preservesFirstFailure"] = True
        self.assert_problem(validate(fixture), "FIRST_FAILURE_ORPHANED")

    def test_passed_gate_cannot_use_zero_command_fake_green(self) -> None:
        fixture = make_fixture()
        gate = fixture_gate(fixture, "W85-R0").data
        gate["commands"] = []
        gate["commandCounts"].update(
            {"total": 0, "passed": 0, "failed": 0, "notRun": 0, "notApplicable": 0}
        )
        self.assert_problem(validate(fixture), "PASSED_GATE_COMMAND_REQUIRED")

    def test_complete_checkout_is_bound_to_real_head_branch_and_clean_state(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)

            def git(*arguments: str) -> str:
                return _git(root, *arguments)

            git("init", "--quiet")
            git("checkout", "-b", "codex/week84-92-refactor", "--quiet")
            tracked = root / "tracked.txt"
            tracked.write_text("clean", encoding="utf-8")
            git("add", "tracked.txt")
            git(
                "-c",
                "user.name=Goal Validator",
                "-c",
                "user.email=validator@example.invalid",
                "commit",
                "--quiet",
                "-m",
                "clean",
            )
            head = git("rev-parse", "HEAD")
            goal = {
                "identity": {
                    "sourceHead": head,
                    "integrationBranch": "codex/week84-92-refactor",
                    "dirtyState": "Clean",
                }
            }
            problems = validator.Problems()
            validator._validate_current_complete_checkout(goal, root, problems)
            self.assertEqual([], problems.items)

            tracked.write_text("dirty", encoding="utf-8")
            problems = validator.Problems()
            validator._validate_current_complete_checkout(goal, root, problems)
            self.assert_problem(problems.items, "COMPLETE_CHECKOUT_DIRTY")

            goal["identity"]["sourceHead"] = "0" * 40
            goal["identity"]["integrationBranch"] = "codex/not-current"
            problems = validator.Problems()
            validator._validate_current_complete_checkout(goal, root, problems)
            self.assert_problem(problems.items, "COMPLETE_CHECKOUT_HEAD")
            self.assert_problem(problems.items, "COMPLETE_CHECKOUT_BRANCH")


if __name__ == "__main__":
    unittest.main()
