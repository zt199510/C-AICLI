from __future__ import annotations

from copy import deepcopy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from tools import week84_92_goal_integrity as integrity
from tools import week84_92_trusted_executor as trusted_executor


FAILURE_PATH = Path(integrity.FAILURE_LEDGER_PATH)
DECISION_PATH = Path(integrity.USER_DECISION_LEDGER_PATH)
GOAL_PATH = Path("artifacts/week84-92-goal-control/goal-state.json")
GATE_PATH = Path(
    "artifacts/week86-renderer-chat-first-shell/gates/W86-R0.json"
)
HANDOFF_PATH = Path(
    "artifacts/week86-renderer-chat-first-shell/week86-renderer-handoff.json"
)
SNAPSHOT_PATH = HANDOFF_PATH.parent / "goal-control-snapshot.json"
CANDIDATE = "c" * 40
CHECKPOINT_COMMIT = "d" * 40
PROVIDER_SHA = "e" * 64
MANIFEST_SHA = "a" * 64


def challenge_for(request_id: str) -> str:
    return "CH-" + hashlib.sha256(request_id.encode("utf-8")).hexdigest()[
        :12
    ].upper()


def response_fields(request_id: str, status: str) -> dict[str, str]:
    challenge = challenge_for(request_id)
    response = integrity.canonical_user_response(
        request_id,
        challenge,
        status,
    )
    return {
        "challengeCode": challenge,
        "userResponseCanonical": response,
        "userMessageSha256": hashlib.sha256(response.encode("utf-8")).hexdigest(),
    }


INITIAL_RESPONSE = response_fields("UA-W86-001", "Passed")
USER_MESSAGE_SHA = INITIAL_RESPONSE["userMessageSha256"]


def ledger_entry(entry: dict, previous: str | None) -> dict:
    result = deepcopy(entry)
    result["previousEntrySha256"] = previous
    result["entrySha256"] = integrity.canonical_entry_sha256(result)
    return result


def ledger(entries: list[dict]) -> dict:
    previous = None
    chained = []
    for source in entries:
        source = deepcopy(source)
        if "acceptanceId" in source:
            source.update(
                response_fields(
                    str(source["decisionRequestId"]),
                    str(source["status"]),
                )
            )
        item = ledger_entry(source, previous)
        chained.append(item)
        previous = item["entrySha256"]
    return {
        "schemaVersion": integrity.SCHEMA_VERSION,
        "goalId": integrity.GOAL_ID,
        "hashAlgorithm": integrity.HASH_ALGORITHM,
        "entryCount": len(chained),
        "lastEntrySha256": previous,
        "entries": chained,
    }


def failure_source() -> dict:
    return {
        "sequence": 1,
        "failureId": "FF-W86-R0-001",
        "observedAt": "2026-07-28T10:00:00Z",
        "checkpoint": "W86",
        "lane": "renderer",
        "gateId": "W86-R0",
        "phase": "entry",
        "classification": "Product",
        "summary": "redacted deterministic failure",
        "evidenceRefs": [
            "artifacts/week86-renderer-chat-first-shell/first-failure.json"
        ],
    }


def decision_source(candidate: str = CANDIDATE) -> dict:
    result = {
        "sequence": 1,
        "decisionRequestId": "UA-W86-001",
        "requestCommit": "0" * 40,
        "acceptanceId": "W86-USER-VISUAL",
        "candidate": candidate,
        "manifestSha256": MANIFEST_SHA,
        "decidedAt": "2026-07-28T10:05:00Z",
        "status": "Passed",
        "confirmedBy": "User",
    }
    result.update(response_fields(result["decisionRequestId"], result["status"]))
    return result


def request_source(entry: dict, **overrides: object) -> dict:
    acceptance_id = entry["acceptanceId"]
    checkpoint = {
        "W86-USER-VISUAL": "W86",
        "W89-USER-VISUAL": "W89",
        "W92-USER-VISUAL": "W92",
    }[acceptance_id]
    result = {
        "schemaVersion": integrity.SCHEMA_VERSION,
        "goalId": integrity.GOAL_ID,
        "acceptanceId": acceptance_id,
        "checkpoint": checkpoint,
        "decisionRequestId": entry["decisionRequestId"],
        "candidate": entry["candidate"],
        "manifestSha256": entry["manifestSha256"],
        "challengeCode": challenge_for(entry["decisionRequestId"]),
        "requestedAt": "2026-07-28T10:04:00Z",
        "status": "AwaitingUser",
    }
    result.update(overrides)
    return result


def write_json(path: Path, value: object) -> bytes:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        indent=2,
    ).encode("utf-8")
    path.write_bytes(payload)
    return payload


def file_binding(relative_path: str, payload: bytes, data: dict) -> dict:
    return {
        "path": relative_path,
        "sha256": hashlib.sha256(payload).hexdigest(),
        "entryCount": data["entryCount"],
        "lastEntrySha256": data["lastEntrySha256"],
    }


def trusted_fixture_git_run(
    root: Path,
    arguments: tuple[str, ...],
    *,
    environment_overrides: dict[str, str] | None = None,
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
    environment = trusted_executor._git_environment(executable)
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
    after_executable, after_identity = trusted_executor._trusted_git_identity(
        repository
    )
    if executable != after_executable or before_identity != after_identity:
        raise RuntimeError("trusted fixture Git identity changed")
    return completed


class GoalIntegrityTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self._init_git()
        self._make_valid_fixture()

    def tearDown(self) -> None:
        self.temp.cleanup()

    def path(self, relative: Path) -> Path:
        return self.root / relative

    def git(self, *arguments: str) -> str:
        completed = trusted_fixture_git_run(self.root, arguments)
        return completed.stdout.decode("utf-8").strip()

    def _init_git(self) -> None:
        self.git("init", "-q")
        self.git("config", "user.name", "Goal Integrity Tests")
        self.git("config", "user.email", "goal-integrity@example.invalid")
        seed = self.root / "candidate.txt"
        seed.write_text("candidate-1\n", encoding="utf-8")
        self.git("add", "--", "candidate.txt")
        self.git("commit", "-q", "-m", "candidate one")
        self.candidate = self.git("rev-parse", "HEAD")

    def commit_candidate(self) -> str:
        seed = self.root / "candidate.txt"
        seed.write_text(
            seed.read_text(encoding="utf-8") + "candidate-next\n",
            encoding="utf-8",
        )
        self.git("add", "--", "candidate.txt")
        self.git("commit", "-q", "-m", "candidate next")
        return self.git("rev-parse", "HEAD")

    def request_path(self, request_id: str) -> Path:
        return self.root / integrity.USER_ACCEPTANCE_REQUEST_ROOT / (
            f"{request_id}.json"
        )

    def commit_request(
        self,
        entry: dict,
        **overrides: object,
    ) -> bytes:
        relative = (
            integrity.USER_ACCEPTANCE_REQUEST_ROOT
            / f"{entry['decisionRequestId']}.json"
        )
        payload = write_json(
            self.root / relative,
            request_source(entry, **overrides),
        )
        self.git("add", "--", relative.as_posix())
        trusted_fixture_git_run(
            self.root,
            (
                "commit",
                "-q",
                "-m",
                f"request {entry['decisionRequestId']}",
            ),
            environment_overrides={
                "GIT_AUTHOR_DATE": "2026-07-28T10:04:30Z",
                "GIT_COMMITTER_DATE": "2026-07-28T10:04:30Z",
            },
        )
        entry["requestCommit"] = self.git("rev-parse", "HEAD")
        return payload

    def read(self, relative: Path) -> dict:
        return json.loads(self.path(relative).read_text(encoding="utf-8"))

    def initial_decision_source(self) -> dict:
        entry = deepcopy(self.read(DECISION_PATH)["entries"][0])
        entry.pop("previousEntrySha256", None)
        entry.pop("entrySha256", None)
        return entry

    def write(self, relative: Path, value: object) -> bytes:
        return write_json(self.path(relative), value)

    def bind_goal_ledger(
        self,
        field: str,
        relative: Path,
        data: dict,
        payload: bytes,
    ) -> None:
        goal = self.read(GOAL_PATH)
        goal[field] = file_binding(relative.as_posix(), payload, data)
        self.write(GOAL_PATH, goal)

    def rebind_snapshot(self) -> None:
        handoff = self.read(HANDOFF_PATH)
        payload = self.path(SNAPSHOT_PATH).read_bytes()
        handoff["goalControlBinding"]["sha256"] = hashlib.sha256(
            payload
        ).hexdigest()
        self.write(HANDOFF_PATH, handoff)

    def validate(self) -> list[integrity.IntegrityIssue]:
        return integrity.validate_goal_integrity(
            repo_root=self.root,
            goal_state_path=self.path(GOAL_PATH),
            failure_ledger_path=self.path(FAILURE_PATH),
            user_decision_ledger_path=self.path(DECISION_PATH),
            gate_paths=[self.path(GATE_PATH)],
            handoff_paths=[self.path(HANDOFF_PATH)],
        )

    def codes(self) -> set[str]:
        return integrity.issue_codes(self.validate())

    def _make_valid_fixture(self) -> None:
        failure_data = ledger([failure_source()])
        failure_payload = self.write(FAILURE_PATH, failure_data)
        initial_decision = decision_source(self.candidate)
        self.commit_request(initial_decision)
        decision_data = ledger([initial_decision])
        decision_payload = self.write(DECISION_PATH, decision_data)

        gate = {
            "goalId": integrity.GOAL_ID,
            "checkpoint": "W86",
            "week": 86,
            "lane": "renderer",
            "gateId": "W86-R0",
            "firstFailure": {
                "failureId": "FF-W86-R0-001",
                "observedAt": "2026-07-28T10:00:00Z",
                "phase": "entry",
                "classification": "Product",
                "summary": "redacted deterministic failure",
                "evidenceRefs": [
                    "artifacts/week86-renderer-chat-first-shell/"
                    "first-failure.json"
                ],
            },
        }
        self.write(GATE_PATH, gate)

        goal = {
            "goalId": integrity.GOAL_ID,
            "identity": {
                "productCandidate": self.candidate,
                "checkpointCommit": CHECKPOINT_COMMIT,
            },
            "execution": {"activeCheckpoint": "W86"},
            "authorization": {
                "provider": {
                    "ledgerPath": (
                        "artifacts/week84-92-goal-control/"
                        "provider-turn-ledger.json"
                    ),
                    "ledgerSha256": PROVIDER_SHA,
                    "ledgerSequence": 34,
                }
            },
            "firstFailureLedger": file_binding(
                FAILURE_PATH.as_posix(),
                failure_payload,
                failure_data,
            ),
            "userDecisionLedger": file_binding(
                DECISION_PATH.as_posix(),
                decision_payload,
                decision_data,
            ),
            "manualAcceptances": {
                "w86Visual": "Passed",
                "w89Visual": "NotRun",
                "w92Visual": "NotRun",
            },
        }
        self.write(GOAL_PATH, goal)

        snapshot = deepcopy(goal)
        snapshot_payload = self.write(SNAPSHOT_PATH, snapshot)
        handoff = {
            "goalId": integrity.GOAL_ID,
            "checkpoint": "W86",
            "week": 86,
            "lane": "renderer",
            "identity": {
                "productCandidate": self.candidate,
                "checkpointCommit": CHECKPOINT_COMMIT,
            },
            "goalControlBinding": {
                "path": SNAPSHOT_PATH.as_posix(),
                "sha256": hashlib.sha256(snapshot_payload).hexdigest(),
                "providerLedgerPath": (
                    "artifacts/week84-92-goal-control/"
                    "provider-turn-ledger.json"
                ),
                "providerLedgerSha256": PROVIDER_SHA,
                "providerLedgerSequence": 34,
            },
            "userAcceptances": [
                {
                    "acceptanceId": "W86-USER-VISUAL",
                    "status": "Passed",
                    "decisionRequestId": "UA-W86-001",
                    "manifestSha256": MANIFEST_SHA,
                    "challengeCode": INITIAL_RESPONSE["challengeCode"],
                    "userResponseCanonical": INITIAL_RESPONSE[
                        "userResponseCanonical"
                    ],
                    "userMessageSha256": USER_MESSAGE_SHA,
                    "decidedAt": "2026-07-28T10:05:00Z",
                    "confirmedBy": "User",
                },
                {
                    "acceptanceId": "W89-USER-VISUAL",
                    "status": "NotRun",
                },
                {
                    "acceptanceId": "W92-USER-VISUAL",
                    "status": "NotRun",
                },
            ],
        }
        self.write(HANDOFF_PATH, handoff)

    def test_valid_fixture_passes(self) -> None:
        self.assertEqual([], self.validate())

    def test_ambient_git_overrides_cannot_substitute_repository(self) -> None:
        request_path = self.request_path("UA-W86-001")
        request_path.write_bytes(request_path.read_bytes() + b"\n")
        fake_index = self.root / "fake-index"
        with patch.dict(
            os.environ,
            {
                "GIT_DIR": str(self.root / "not-the-repository"),
                "GIT_INDEX_FILE": str(fake_index),
                "GIT_OBJECT_DIRECTORY": str(self.root / "fake-objects"),
                "GIT_WORK_TREE": str(self.root / "fake-worktree"),
            },
            clear=False,
        ):
            self.assertIn("DECISION_REQUEST_REWRITTEN", self.codes())

    def test_replace_refs_make_repository_untrusted(self) -> None:
        request_commit = self.read(DECISION_PATH)["entries"][0]["requestCommit"]
        self.git("replace", self.candidate, request_commit)
        self.assertIn("DECISION_REQUEST_GIT", self.codes())

    def test_shallow_repository_is_rejected(self) -> None:
        (self.root / ".git" / "shallow").write_text(
            f"{self.candidate}\n",
            encoding="ascii",
        )
        self.assertIn("DECISION_REQUEST_GIT", self.codes())

    def test_grafts_or_object_alternates_make_repository_untrusted(self) -> None:
        grafts = self.root / ".git" / "info" / "grafts"
        grafts.write_text(f"{self.candidate}\n", encoding="ascii")
        self.assertIn("DECISION_REQUEST_GIT", self.codes())
        grafts.unlink()
        alternates = self.root / ".git" / "objects" / "info" / "alternates"
        alternates.write_text(str(self.root / "fake-objects"), encoding="utf-8")
        self.assertIn("DECISION_REQUEST_GIT", self.codes())

    def test_failure_ledger_rejects_extra_top_field(self) -> None:
        data = self.read(FAILURE_PATH)
        data["unexpected"] = True
        payload = self.write(FAILURE_PATH, data)
        self.bind_goal_ledger(
            "firstFailureLedger",
            FAILURE_PATH,
            data,
            payload,
        )
        self.assertIn("LEDGER_TOP_FIELDS", self.codes())

    def test_failure_entry_tamper_is_detected(self) -> None:
        data = self.read(FAILURE_PATH)
        data["entries"][0]["summary"] = "tampered"
        self.write(FAILURE_PATH, data)
        self.assertIn("LEDGER_ENTRY_HASH", self.codes())

    def test_central_failure_ledger_raw_hash_is_bound(self) -> None:
        goal = self.read(GOAL_PATH)
        goal["firstFailureLedger"]["sha256"] = "0" * 64
        self.write(GOAL_PATH, goal)
        self.assertIn("FAILURE_CENTRAL_SHA256", self.codes())

    def test_recomputed_failure_ledger_truncation_is_detected(self) -> None:
        data = ledger([])
        payload = self.write(FAILURE_PATH, data)
        self.bind_goal_ledger(
            "firstFailureLedger",
            FAILURE_PATH,
            data,
            payload,
        )
        codes = self.codes()
        self.assertIn("FAILURE_GATE_LEDGER_SET", codes)
        self.assertIn("SNAPSHOT_FAILURE_PREFIX_COUNT", codes)

    def test_deleting_gate_first_failure_is_detected(self) -> None:
        gate = self.read(GATE_PATH)
        gate["firstFailure"] = None
        self.write(GATE_PATH, gate)
        self.assertIn("FAILURE_GATE_LEDGER_SET", self.codes())

    def test_wrong_snapshot_prefix_hash_is_detected(self) -> None:
        snapshot = self.read(SNAPSHOT_PATH)
        snapshot["firstFailureLedger"]["lastEntrySha256"] = "0" * 64
        self.write(SNAPSHOT_PATH, snapshot)
        self.rebind_snapshot()
        self.assertIn("SNAPSHOT_FAILURE_PREFIX_HASH", self.codes())

    def test_wrong_user_decision_snapshot_prefix_is_detected(self) -> None:
        snapshot = self.read(SNAPSHOT_PATH)
        snapshot["userDecisionLedger"]["lastEntrySha256"] = "0" * 64
        self.write(SNAPSHOT_PATH, snapshot)
        self.rebind_snapshot()
        self.assertIn("SNAPSHOT_DECISION_PREFIX_HASH", self.codes())

    def test_snapshot_raw_hash_tamper_is_detected(self) -> None:
        snapshot = self.read(SNAPSHOT_PATH)
        snapshot["identity"]["checkpointCommit"] = "f" * 40
        self.write(SNAPSHOT_PATH, snapshot)
        self.assertIn("SNAPSHOT_RAW_HASH", self.codes())

    def test_snapshot_must_be_beside_handoff(self) -> None:
        outside = Path("artifacts/other/goal-control-snapshot.json")
        payload = self.write(outside, self.read(SNAPSHOT_PATH))
        handoff = self.read(HANDOFF_PATH)
        handoff["goalControlBinding"]["path"] = outside.as_posix()
        handoff["goalControlBinding"]["sha256"] = hashlib.sha256(
            payload
        ).hexdigest()
        self.write(HANDOFF_PATH, handoff)
        self.assertIn("SNAPSHOT_PATH", self.codes())

    def test_snapshot_provider_and_identity_must_match_handoff(self) -> None:
        snapshot = self.read(SNAPSHOT_PATH)
        snapshot["authorization"]["provider"]["ledgerSequence"] = 33
        snapshot["identity"]["productCandidate"] = "f" * 40
        self.write(SNAPSHOT_PATH, snapshot)
        self.rebind_snapshot()
        codes = self.codes()
        self.assertIn("SNAPSHOT_PROVIDER_LEDGERSEQUENCE", codes)
        self.assertIn("SNAPSHOT_IDENTITY_PRODUCTCANDIDATE", codes)

    def test_user_decision_self_sign_is_rejected(self) -> None:
        source = decision_source()
        source["confirmedBy"] = "Agent"
        data = ledger([source])
        payload = self.write(DECISION_PATH, data)
        self.bind_goal_ledger(
            "userDecisionLedger",
            DECISION_PATH,
            data,
            payload,
        )
        self.assertIn("DECISION_CONFIRMED_BY", self.codes())

    def test_precommitted_user_acceptance_request_is_valid(self) -> None:
        self.assertEqual([], self.validate())

    def test_empty_decision_ledger_requires_no_request_or_git(self) -> None:
        with tempfile.TemporaryDirectory() as empty_root:
            self.assertEqual(
                [],
                integrity.validate_user_acceptance_requests(
                    Path(empty_root),
                    [],
                ),
            )

    def test_user_acceptance_request_missing_is_rejected(self) -> None:
        self.request_path("UA-W86-001").unlink()
        self.assertIn("DECISION_REQUEST_MISSING", self.codes())

    def test_staged_but_uncommitted_request_is_rejected(self) -> None:
        first = self.initial_decision_source()
        second = decision_source(self.candidate)
        second.update(
            {
                "sequence": 2,
                "decisionRequestId": "UA-W86-002",
                "decidedAt": "2026-07-28T10:06:00Z",
            }
        )
        relative = (
            integrity.USER_ACCEPTANCE_REQUEST_ROOT
            / "UA-W86-002.json"
        )
        write_json(self.root / relative, request_source(second))
        self.git("add", "--", relative.as_posix())
        data = ledger([first, second])
        payload = self.write(DECISION_PATH, data)
        self.bind_goal_ledger(
            "userDecisionLedger",
            DECISION_PATH,
            data,
            payload,
        )

        self.assertIn("DECISION_REQUEST_FIRST_COMMIT", self.codes())

    def test_user_acceptance_request_rewrite_is_rejected(self) -> None:
        request_path = self.request_path("UA-W86-001")
        request_path.write_bytes(request_path.read_bytes() + b"\n")
        self.assertIn("DECISION_REQUEST_REWRITTEN", self.codes())

    def test_committed_request_rewrite_with_staged_original_is_rejected(self) -> None:
        request_path = self.request_path("UA-W86-001")
        original = request_path.read_bytes()
        request = json.loads(original.decode("utf-8"))
        request["requestedAt"] = "2026-07-28T10:03:00Z"
        write_json(request_path, request)
        relative = request_path.relative_to(self.root).as_posix()
        self.git("add", "--", relative)
        self.git("commit", "-q", "-m", "rewrite request in HEAD")
        request_path.write_bytes(original)
        self.git("add", "--", relative)

        self.assertIn("DECISION_REQUEST_HEAD_REWRITTEN", self.codes())

    def test_committed_request_rewrite_then_revert_is_rejected(self) -> None:
        request_path = self.request_path("UA-W86-001")
        original = request_path.read_bytes()
        request = json.loads(original.decode("utf-8"))
        request["requestedAt"] = "2026-07-28T10:03:00Z"
        write_json(request_path, request)
        relative = request_path.relative_to(self.root).as_posix()
        self.git("add", "--", relative)
        self.git("commit", "-q", "-m", "rewrite request")
        request_path.write_bytes(original)
        self.git("add", "--", relative)
        self.git("commit", "-q", "-m", "restore original request")

        codes = self.codes()
        self.assertIn("DECISION_REQUEST_HISTORY_MUTATION", codes)
        self.assertNotIn("DECISION_REQUEST_REWRITTEN", codes)
        self.assertNotIn("DECISION_REQUEST_INDEX_REWRITTEN", codes)
        self.assertNotIn("DECISION_REQUEST_HEAD_REWRITTEN", codes)

    def test_candidate_cannot_equal_request_first_commit(self) -> None:
        entry = self.read(DECISION_PATH)["entries"][0]
        request_path = self.request_path("UA-W86-001")
        request_payload = request_path.read_bytes()
        original_git_bytes = integrity._git_bytes

        def same_commit_git(
            repo_root: Path,
            arguments: tuple[str, ...],
        ) -> tuple[int | None, bytes | None]:
            if arguments and arguments[0] == "log":
                return 0, f"{self.candidate}\n".encode("ascii")
            if (
                len(arguments) == 2
                and arguments[0] == "show"
                and arguments[1].startswith(f"{self.candidate}:")
            ):
                return 0, request_payload
            return original_git_bytes(repo_root, arguments)

        with patch.object(integrity, "_git_bytes", side_effect=same_commit_git):
            issues = integrity.validate_user_acceptance_requests(
                self.root,
                [entry],
            )
        self.assertIn(
            "DECISION_REQUEST_CANDIDATE_ANCESTRY",
            integrity.issue_codes(issues),
        )

    def test_request_candidate_mismatch_is_rejected(self) -> None:
        first = self.initial_decision_source()
        second = decision_source(self.candidate)
        second.update(
            {
                "sequence": 2,
                "decisionRequestId": "UA-W86-002",
                "decidedAt": "2026-07-28T10:06:00Z",
            }
        )
        self.commit_request(second, candidate="f" * 40)
        data = ledger([first, second])
        payload = self.write(DECISION_PATH, data)
        self.bind_goal_ledger(
            "userDecisionLedger",
            DECISION_PATH,
            data,
            payload,
        )
        self.assertIn("DECISION_REQUEST_BINDING", self.codes())

    def test_request_commit_binding_mismatch_is_rejected(self) -> None:
        data = self.read(DECISION_PATH)
        data["entries"][0]["requestCommit"] = "f" * 40
        data = ledger(
            [
                {
                    key: value
                    for key, value in data["entries"][0].items()
                    if key
                    not in {
                        "entrySha256",
                        "previousEntrySha256",
                        "userResponseCanonical",
                        "userMessageSha256",
                        "challengeCode",
                    }
                }
            ]
        )
        payload = self.write(DECISION_PATH, data)
        self.bind_goal_ledger(
            "userDecisionLedger",
            DECISION_PATH,
            data,
            payload,
        )

        self.assertIn("DECISION_REQUEST_COMMIT_BINDING", self.codes())

    def test_request_commit_after_decision_is_rejected(self) -> None:
        first = self.initial_decision_source()
        second = decision_source(self.candidate)
        second.update(
            {
                "sequence": 2,
                "decisionRequestId": "UA-W86-002",
                "decidedAt": "2026-07-28T10:04:00Z",
            }
        )
        self.commit_request(second, requestedAt="2026-07-28T10:03:00Z")
        data = ledger([first, second])
        payload = self.write(DECISION_PATH, data)
        self.bind_goal_ledger(
            "userDecisionLedger",
            DECISION_PATH,
            data,
            payload,
        )

        self.assertIn("DECISION_REQUEST_COMMIT_ORDER", self.codes())

    def test_request_manifest_mismatch_is_rejected(self) -> None:
        first = self.initial_decision_source()
        second = decision_source(self.candidate)
        second.update(
            {
                "sequence": 2,
                "decisionRequestId": "UA-W86-002",
                "decidedAt": "2026-07-28T10:06:00Z",
            }
        )
        self.commit_request(second, manifestSha256="f" * 64)
        data = ledger([first, second])
        payload = self.write(DECISION_PATH, data)
        self.bind_goal_ledger(
            "userDecisionLedger",
            DECISION_PATH,
            data,
            payload,
        )
        self.assertIn("DECISION_REQUEST_BINDING", self.codes())

    def test_empty_or_invalid_request_challenge_is_rejected(self) -> None:
        first = self.initial_decision_source()
        second = decision_source(self.candidate)
        second.update(
            {
                "sequence": 2,
                "decisionRequestId": "UA-W86-002",
                "decidedAt": "2026-07-28T10:06:00Z",
            }
        )
        self.commit_request(second, challengeCode="")
        data = ledger([first, second])
        payload = self.write(DECISION_PATH, data)
        self.bind_goal_ledger(
            "userDecisionLedger",
            DECISION_PATH,
            data,
            payload,
        )
        self.assertIn("DECISION_REQUEST_CHALLENGE", self.codes())

    def test_request_errors_never_echo_request_content(self) -> None:
        secret = "sk-request-secret-that-must-not-appear"
        self.request_path("UA-W86-001").write_text(
            '{"challengeCode":"'
            + secret
            + '","challengeCode":"duplicate"}',
            encoding="utf-8",
        )
        rendered = "\n".join(str(issue) for issue in self.validate())
        self.assertNotIn(secret, rendered)
        self.assertNotIn("duplicate", rendered)

    def test_user_decision_tamper_is_detected(self) -> None:
        data = self.read(DECISION_PATH)
        data["entries"][0]["manifestSha256"] = "f" * 64
        self.write(DECISION_PATH, data)
        self.assertIn("LEDGER_ENTRY_HASH", self.codes())

    def test_user_decision_truncation_breaks_handoff_anchor(self) -> None:
        data = ledger([])
        payload = self.write(DECISION_PATH, data)
        self.bind_goal_ledger(
            "userDecisionLedger",
            DECISION_PATH,
            data,
            payload,
        )
        self.assertIn("HANDOFF_ACCEPTANCE_UNANCHORED", self.codes())

    def test_later_failed_decision_revokes_central_passed(self) -> None:
        first = self.initial_decision_source()
        second = decision_source(self.candidate)
        second.update(
            {
                "sequence": 2,
                "decisionRequestId": "UA-W86-002",
                "decidedAt": "2026-07-28T10:06:00Z",
                "status": "Failed",
            }
        )
        self.commit_request(second)
        data = ledger([first, second])
        payload = self.write(DECISION_PATH, data)
        self.bind_goal_ledger(
            "userDecisionLedger",
            DECISION_PATH,
            data,
            payload,
        )
        self.assertIn("CENTRAL_ACCEPTANCE_LATEST", self.codes())

    def test_historical_handoff_uses_its_frozen_decision_prefix(self) -> None:
        first = self.initial_decision_source()
        second = decision_source(self.candidate)
        second.update(
            {
                "sequence": 2,
                "decisionRequestId": "UA-W86-002",
                "decidedAt": "2026-07-28T10:06:00Z",
                "status": "Failed",
            }
        )
        self.commit_request(second)
        data = ledger([first, second])
        payload = self.write(DECISION_PATH, data)
        self.bind_goal_ledger(
            "userDecisionLedger",
            DECISION_PATH,
            data,
            payload,
        )
        goal = self.read(GOAL_PATH)
        goal["manualAcceptances"]["w86Visual"] = "Failed"
        self.write(GOAL_PATH, goal)

        self.assertEqual([], self.validate())

    def test_handoff_must_use_latest_decision_in_snapshot_prefix(self) -> None:
        first = self.initial_decision_source()
        second = decision_source(self.candidate)
        second.update(
            {
                "sequence": 2,
                "decisionRequestId": "UA-W86-002",
                "decidedAt": "2026-07-28T10:06:00Z",
                "status": "Failed",
            }
        )
        self.commit_request(second)
        data = ledger([first, second])
        payload = self.write(DECISION_PATH, data)
        self.bind_goal_ledger(
            "userDecisionLedger",
            DECISION_PATH,
            data,
            payload,
        )
        goal = self.read(GOAL_PATH)
        goal["manualAcceptances"]["w86Visual"] = "Failed"
        self.write(GOAL_PATH, goal)
        snapshot = self.read(SNAPSHOT_PATH)
        snapshot["userDecisionLedger"] = file_binding(
            DECISION_PATH.as_posix(),
            payload,
            data,
        )
        self.write(SNAPSHOT_PATH, snapshot)
        self.rebind_snapshot()

        self.assertIn("HANDOFF_ACCEPTANCE_LATEST", self.codes())

    def test_handoff_accepts_exact_latest_failed_decision(self) -> None:
        first = self.initial_decision_source()
        second = decision_source(self.candidate)
        second.update(
            {
                "sequence": 2,
                "decisionRequestId": "UA-W86-002",
                "decidedAt": "2026-07-28T10:06:00Z",
                "status": "Failed",
            }
        )
        self.commit_request(second)
        data = ledger([first, second])
        payload = self.write(DECISION_PATH, data)
        self.bind_goal_ledger(
            "userDecisionLedger",
            DECISION_PATH,
            data,
            payload,
        )
        goal = self.read(GOAL_PATH)
        goal["manualAcceptances"]["w86Visual"] = "Failed"
        self.write(GOAL_PATH, goal)
        snapshot = self.read(SNAPSHOT_PATH)
        snapshot["manualAcceptances"]["w86Visual"] = "Failed"
        snapshot["userDecisionLedger"] = file_binding(
            DECISION_PATH.as_posix(),
            payload,
            data,
        )
        self.write(SNAPSHOT_PATH, snapshot)
        self.rebind_snapshot()
        handoff = self.read(HANDOFF_PATH)
        handoff["userAcceptances"][0].update(
            {
                "status": "Failed",
                "decisionRequestId": "UA-W86-002",
                "decidedAt": "2026-07-28T10:06:00Z",
                "challengeCode": data["entries"][-1]["challengeCode"],
                "userResponseCanonical": data["entries"][-1][
                    "userResponseCanonical"
                ],
                "userMessageSha256": data["entries"][-1][
                    "userMessageSha256"
                ],
            }
        )
        self.write(HANDOFF_PATH, handoff)

        self.assertEqual([], self.validate())

    def test_latest_target_decision_is_bound_to_handoff_candidate(self) -> None:
        first = self.initial_decision_source()
        new_candidate = self.commit_candidate()
        second = decision_source(new_candidate)
        second.update(
            {
                "sequence": 2,
                "decisionRequestId": "UA-W86-002",
                "decidedAt": "2026-07-28T10:06:00Z",
            }
        )
        self.commit_request(second)
        data = ledger([first, second])
        payload = self.write(DECISION_PATH, data)
        self.bind_goal_ledger(
            "userDecisionLedger",
            DECISION_PATH,
            data,
            payload,
        )
        snapshot = self.read(SNAPSHOT_PATH)
        snapshot["userDecisionLedger"] = file_binding(
            DECISION_PATH.as_posix(),
            payload,
            data,
        )
        self.write(SNAPSHOT_PATH, snapshot)
        self.rebind_snapshot()
        handoff = self.read(HANDOFF_PATH)
        record = handoff["userAcceptances"][0]
        record.update(
            {
                "decisionRequestId": "UA-W86-002",
                "decidedAt": "2026-07-28T10:06:00Z",
            }
        )
        self.write(HANDOFF_PATH, handoff)

        self.assertIn("HANDOFF_ACCEPTANCE_CANDIDATE", self.codes())

    def test_future_acceptance_cannot_be_passed_early(self) -> None:
        handoff = self.read(HANDOFF_PATH)
        handoff["userAcceptances"][2]["status"] = "Passed"
        self.write(HANDOFF_PATH, handoff)
        self.assertIn("HANDOFF_FUTURE_ACCEPTANCE", self.codes())

    def test_central_user_decision_raw_hash_is_bound(self) -> None:
        goal = self.read(GOAL_PATH)
        goal["userDecisionLedger"]["sha256"] = "0" * 64
        self.write(GOAL_PATH, goal)
        self.assertIn("DECISION_CENTRAL_SHA256", self.codes())

    def test_duplicate_request_id_is_rejected(self) -> None:
        first = decision_source()
        second = decision_source()
        second["sequence"] = 2
        second["acceptanceId"] = "W89-USER-VISUAL"
        second["candidate"] = "f" * 40
        data = ledger([first, second])
        payload = self.write(DECISION_PATH, data)
        self.bind_goal_ledger(
            "userDecisionLedger",
            DECISION_PATH,
            data,
            payload,
        )
        self.assertIn("DECISION_REQUEST_ID", self.codes())

    def test_errors_never_echo_file_content_or_secret(self) -> None:
        secret = "sk-secret-value-that-must-not-appear"
        self.path(FAILURE_PATH).write_text(
            '{"schemaVersion":"1.0.0","secret":"'
            + secret
            + '","secret":"duplicate"}',
            encoding="utf-8",
        )
        rendered = "\n".join(str(issue) for issue in self.validate())
        self.assertNotIn(secret, rendered)
        self.assertNotIn("duplicate", rendered)


if __name__ == "__main__":
    unittest.main()
