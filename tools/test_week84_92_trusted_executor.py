from __future__ import annotations

from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import hashlib
import inspect
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from unittest import mock

from tools import week84_92_trusted_executor as executor
from tools import week84_92_provider_turn_harness as provider_harness


DEFAULT_POLICY_MODULE_CODE = """import os
import sys
import unittest

class TrustedSemanticFixture(unittest.TestCase):
    def test_provider_environment_is_scrubbed(self):
        self.assertNotIn("OPENAI_API_KEY", os.environ)
"""

DEFAULT_POLICY_RUNNER = """
suite = unittest.defaultTestLoader.loadTestsFromTestCase(TrustedSemanticFixture)
result = unittest.TextTestRunner(stream=sys.stdout, verbosity=0).run(suite)
raise SystemExit(0 if result.wasSuccessful() else 1)
"""
DEFAULT_POLICY_CODE = DEFAULT_POLICY_MODULE_CODE + DEFAULT_POLICY_RUNNER
POLICY_REDACTED_INVOCATION = (
    executor.SEMANTIC_REDACTED_INVOCATION
)


def trusted_git_policy() -> dict[str, object]:
    return {
        "policyId": executor.TRUSTED_GIT_POLICY_ID,
        "executableRole": executor.TRUSTED_GIT_EXECUTABLE_ROLE,
        "version": executor.TRUSTED_GIT_VERSION,
        "executableBytes": executor.TRUSTED_GIT_EXECUTABLE_BYTES,
        "executableSha256": executor.TRUSTED_GIT_EXECUTABLE_SHA256,
        "shellAllowed": False,
    }


def trusted_test_policy(
    entry_sources: list[dict[str, str]],
    read_sources: list[dict[str, str]],
) -> dict[str, object]:
    return {
        "policyId": executor.TRUSTED_TEST_POLICY_ID,
        "commandId": executor.TRUSTED_TEST_COMMAND_ID,
        "executableRole": executor.TRUSTED_TEST_EXECUTABLE_ROLE,
        "arguments": list(executor._SEMANTIC_ARGUMENTS),
        "redactedInvocation": POLICY_REDACTED_INVOCATION,
        "countsSource": executor.TRUSTED_TEST_COUNTS_SOURCE,
        "shellAllowed": False,
        "loaderProtocol": executor.SEMANTIC_LOADER_PROTOCOL,
        "entrySources": entry_sources,
        "readSources": read_sources,
    }


def trusted_argv(_code: str = DEFAULT_POLICY_CODE) -> tuple[str, ...]:
    return (sys.executable, *executor._SEMANTIC_ARGUMENTS)


def trusted_provider_policy(scenario_behavior: str = "exact") -> dict[str, object]:
    source = Path(executor.__file__).with_name(
        "week84_92_provider_turn_harness.py"
    ).read_bytes()
    scenario_raw = _scenario_source(scenario_behavior).encode("utf-8")
    return {
        "policyId": executor.TRUSTED_PROVIDER_POLICY_ID,
        "executableRole": executor.TRUSTED_TEST_EXECUTABLE_ROLE,
        "sourcePath": executor.TRUSTED_PROVIDER_HARNESS_PATH,
        "sourceSha256": hashlib.sha256(source).hexdigest(),
        "argumentsTemplate": [
            "-I",
            "-S",
            "-E",
            "-B",
            "-X",
            "utf8",
            "{sourcePath}",
            "--descriptor",
            "{descriptorPath}",
            "--observation",
            "{observationPath}",
        ],
        "redactedInvocation": executor.TRUSTED_PROVIDER_REDACTED_INVOCATION,
        "shellAllowed": False,
        "allowedBatchSizes": {
            "provider-read-only": [1],
            "provider-recovery": [2],
            "provider-resource": [6],
            "controlled-write": [1],
        },
        "observedRequestProtocol": executor.PROVIDER_OBSERVED_REQUEST_PROTOCOL,
        "scenarioSources": {
            phase: {
                "path": path,
                "sha256": hashlib.sha256(scenario_raw).hexdigest(),
            }
            for phase, path in executor.PROVIDER_SCENARIO_PATHS.items()
        },
    }


def trusted_product_policy() -> dict[str, object]:
    return {
        "policyId": executor.TRUSTED_PRODUCT_POLICY_ID,
        "executableRole": executor.ISOLATED_PYTHON_EXECUTABLE_ROLE,
        "scriptRoot": executor.TRUSTED_PRODUCT_SCRIPT_ROOT,
        "argumentsTemplate": list(executor._PRODUCT_TEMPLATE_ARGUMENTS),
        "redactedInvocationTemplate": (
            "python-current -I -S -E -B -X utf8 -c "
            "<week84-92-in-memory-command-adapter-v1:{commandId}>"
        ),
        "shellAllowed": False,
    }


def _git(root: Path, *arguments: str) -> str:
    git_executable, _identity = executor._trusted_git_identity(root)
    result = subprocess.run(
        [str(git_executable), *arguments],
        cwd=root,
        env=executor._git_environment(git_executable),
        stdin=subprocess.DEVNULL,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        shell=False,
        check=False,
    )
    if result.returncode != 0:
        raise AssertionError("temporary Git repository setup failed")
    return result.stdout.decode("ascii").strip()


def _past_timestamp(minutes: int = 10) -> str:
    return (
        (datetime.now(timezone.utc) - timedelta(minutes=minutes))
        .isoformat(timespec="microseconds")
        .replace("+00:00", "Z")
    )


def _parts(relative: str) -> Path:
    return Path(*relative.split("/"))


def _week_lane(gate_id: str) -> tuple[int, str]:
    week = int(gate_id[1:3])
    if week == 84:
        return week, "baseline"
    if week == 90:
        return week, "integration"
    if week == 91:
        return week, "hardening"
    if week == 92:
        return week, "acceptance"
    return week, "renderer" if "-R" in gate_id else "cli"


def _product_result_line(
    *,
    discovered: int = 1,
    passed: int = 1,
    failed: int = 0,
    skipped: int = 0,
    status: str = "Passed",
) -> str:
    payload = {
        "protocol": executor.PRODUCT_COMMAND_RESULT_PROTOCOL,
        "status": status,
        "counts": {
            "discovered": discovered,
            "passed": passed,
            "failed": failed,
            "skipped": skipped,
        },
    }
    return (
        executor.PRODUCT_COMMAND_RESULT_MARKER
        + executor.canonical_json_bytes(payload).decode("utf-8")
    )


def _scenario_source(behavior: str) -> str:
    return f"""import json
import os
import time
from pathlib import Path
from urllib import error, request

descriptor = json.loads(Path(os.environ["CAICLI_PROVIDER_DESCRIPTOR"]).read_text("utf-8"))
if os.environ.get("OPENAI_API_KEY") != "caicli-loopback-only":
    raise SystemExit(41)
count = len(descriptor["reservations"])
behavior = {behavior!r}
if behavior == "missing":
    count -= 1
elif behavior == "extra":
    count += 1
for ordinal in range(max(count, 0)):
    body = json.dumps({{"ordinal": ordinal + 1}}, sort_keys=True).encode("utf-8")
    outbound = request.Request(
        os.environ["OPENAI_BASE_URL"].rstrip("/") + "/chat/completions",
        data=body,
        headers={{"Content-Type": "application/json"}},
        method="POST",
    )
    try:
        with request.urlopen(outbound, timeout=10) as response:
            started = time.monotonic()
            first = response.read(1)
            if behavior == "stream" and (not first or time.monotonic() - started > 1.0):
                raise SystemExit(42)
            response.read()
    except error.HTTPError as failure:
        failure.read()
        if not (behavior == "extra" and failure.code == 429):
            raise
if behavior == "drift":
    Path("candidate.txt").write_text("drifted\\n", encoding="utf-8")
raise SystemExit(0)
"""


@contextmanager
def mocked_provider_child(
    repository: "TemporaryGoalRepository", behavior: str = "exact"
):
    def run(
        command: object,
        *,
        cwd: Path,
        environment: object,
        timeout_seconds: float,
    ) -> int:
        del cwd, environment, timeout_seconds
        arguments = list(command)  # type: ignore[arg-type]
        descriptor_path = arguments[arguments.index("--descriptor") + 1]
        observation_path = arguments[arguments.index("--observation") + 1]
        descriptor = json.loads(
            (repository.root / _parts(descriptor_path)).read_text("utf-8")
        )
        reservations = descriptor["reservations"]
        count = len(reservations)
        if behavior == "missing":
            count -= 1
        elif behavior == "extra":
            count += 1
        requests = []
        for index in range(max(count, 0)):
            reservation = reservations[min(index, len(reservations) - 1)]
            requests.append(
                {
                    "reservationId": reservation["reservationId"],
                    "reservationSequence": reservation["reservationSequence"],
                    "requestOrdinal": index + 1,
                    "requestSha256": hashlib.sha256(
                        f"request-{index + 1}".encode("ascii")
                    ).hexdigest(),
                    "status": "Succeeded",
                }
            )
        observation = {
            "schemaVersion": executor.SCHEMA_VERSION,
            "protocol": executor.PROVIDER_OBSERVED_REQUEST_PROTOCOL,
            "batchId": descriptor["batchId"],
            "gateId": descriptor["gateId"],
            "phase": descriptor["phase"],
            "productCandidate": descriptor["productCandidate"],
            "attemptId": descriptor["attemptId"],
            "runId": descriptor["runId"],
            "packageIdentityEvidence": descriptor["packageIdentityEvidence"],
            "providerBoundaryDecision": descriptor["providerBoundaryDecision"],
            "requests": requests,
        }
        repository.write_json(observation_path, observation)
        observation_raw = (repository.root / _parts(observation_path)).read_bytes()
        if behavior in {"missing", "extra"}:
            return 0
        identity_path = descriptor["packageIdentityEvidence"]["path"]
        identity_raw = (repository.root / _parts(identity_path)).read_bytes()
        identity = json.loads(identity_raw.decode("utf-8"))
        tree = identity["tree"]
        entries = {item["path"]: item for item in tree["entries"]}
        started = _past_timestamp(1)
        exited = _past_timestamp(0)
        launches = {
            "desktop": {
                "imagePath": executor.FIXED_PACKAGE_ENTRYPOINT,
                "imageSha256": entries[executor.FIXED_PACKAGE_ENTRYPOINT]["sha256"],
                "pid": max(os.getpid(), 1),
                "startedAt": started,
                "exitedAt": exited,
                "exitCode": 0,
            },
            "appHost": {
                "imagePath": executor.FIXED_APPHOST_PATH,
                "imageSha256": entries[executor.FIXED_APPHOST_PATH]["sha256"],
                "pid": max(os.getpid() + 1, 2),
                "startedAt": started,
                "exitedAt": exited,
                "exitCode": 0,
            },
        }
        stage = {
            "packageTreeRootSha256": tree["treeRootSha256"],
            "entryCount": tree["entryCount"],
            "totalBytes": tree["totalBytes"],
        }
        receipt = {
            "schemaVersion": executor.SCHEMA_VERSION,
            "protocol": executor.PACKAGE_LAUNCH_RECEIPT_PROTOCOL,
            "status": "Passed",
            "goalId": executor.GOAL_ID,
            "gateId": descriptor["gateId"],
            "productCandidate": descriptor["productCandidate"],
            "phase": descriptor["phase"],
            "profileOrdinal": descriptor["profileOrdinal"],
            "batchId": descriptor["batchId"],
            "attemptId": descriptor["attemptId"],
            "runId": descriptor["runId"],
            "runTokenSha256": descriptor["runTokenSha256"],
            "packageTreeRootSha256": tree["treeRootSha256"],
            "boundaryDecision": descriptor["providerBoundaryDecision"],
            "sourcePackage": {
                "identityPath": identity_path,
                "identityRawSha256": hashlib.sha256(identity_raw).hexdigest(),
                "packageRoot": identity["packageRoot"],
                "packageTreeRootSha256": tree["treeRootSha256"],
                "entryCount": tree["entryCount"],
                "totalBytes": tree["totalBytes"],
                "entrypoint": identity["entrypoint"]["path"],
                "argv": identity["entrypoint"]["argv"],
                "appHostPath": identity["appHostPath"],
                "appHostSha256": entries[executor.FIXED_APPHOST_PATH]["sha256"],
            },
            "stagedPackageBefore": stage,
            "stagedPackageAfter": stage,
            "launches": launches,
            "containment": {
                "attachedBeforeRelease": True,
                "processIsolated": False,
                "allEgressIsolated": False,
                "sandboxPolicySha256": None,
                "strongCanaryEvidence": None,
            },
            "observedRequestCount": len(reservations),
            "scenarioAssertions": {
                "desktopUiObserved": True,
                "productBehaviorPassed": True,
                "recoveryListenerObserved": descriptor["phase"]
                == "provider-recovery",
                "details": {"fixture": "executor-unit-test"},
            },
            "cleanup": {"status": "Passed", "processDelta": 0, "temporaryDelta": 0},
            "gatewayObservation": {
                "path": observation_path,
                "rawSha256": hashlib.sha256(observation_raw).hexdigest(),
                "requestCount": len(reservations),
                "reservationIds": [item["reservationId"] for item in reservations],
            },
            "scenarioSource": descriptor["scenarioSource"],
            "driverSource": descriptor["driverSource"],
        }
        repository.write_json(descriptor["packageLaunchReceiptPath"], receipt)
        if behavior == "drift":
            repository.write_text("candidate.txt", "drifted\n")
        return 0

    with mock.patch.object(executor, "_run_child_no_capture", side_effect=run):
        yield


class TemporaryGoalRepository:
    def __init__(self) -> None:
        self._temporary = tempfile.TemporaryDirectory()
        self._linked_worktrees: list[tuple[Path, tempfile.TemporaryDirectory[str]]] = []
        self.root = Path(self._temporary.name).resolve()
        _git(self.root, "init", "--quiet")
        _git(self.root, "symbolic-ref", "HEAD", executor.CONTROL_BRANCH_REF)
        _git(self.root, "config", "user.name", "Trusted Executor Tests")
        _git(self.root, "config", "user.email", "trusted-executor@example.invalid")
        _git(self.root, "config", "core.autocrlf", "false")
        self.write_text(".gitignore", "artifacts/\n.env.local\n")

    def close(self) -> None:
        for worktree_root, temporary in reversed(self._linked_worktrees):
            _git(self.root, "worktree", "remove", "--force", str(worktree_root))
            temporary.cleanup()
        self._temporary.cleanup()

    def switch_main_to_lane(self, branch_ref: str) -> Path:
        if branch_ref not in {
            executor.RENDERER_BRANCH_REF,
            executor.CLI_BRANCH_REF,
            executor.INTEGRATION_BRANCH_REF,
        }:
            raise AssertionError("unsupported test lane")
        _git(self.root, "switch", "--quiet", "-c", branch_ref.removeprefix("refs/heads/"))
        temporary: tempfile.TemporaryDirectory[str] = tempfile.TemporaryDirectory()
        control_root = Path(temporary.name) / "control"
        _git(
            self.root,
            "worktree",
            "add",
            "--quiet",
            str(control_root),
            executor.CONTROL_BRANCH_REF.removeprefix("refs/heads/"),
        )
        self._linked_worktrees.append((control_root, temporary))
        return control_root

    def write_bytes(self, relative: str, raw: bytes) -> None:
        path = self.root / _parts(relative)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)

    def write_json(self, relative: str, value: object) -> None:
        self.write_bytes(relative, executor.canonical_json_bytes(value))

    def write_text(self, relative: str, value: str) -> None:
        self.write_bytes(relative, value.encode("utf-8"))

    def commit_all(self, message: str) -> str:
        _git(self.root, "add", "--all")
        _git(self.root, "commit", "--quiet", "-m", message)
        return _git(self.root, "rev-parse", "HEAD")

    def add_provider_environment(self, base_url: str) -> None:
        self.write_text(
            executor.PROVIDER_ENV_PATH,
            "\n".join(
                (
                    "OPENAI_MODEL=test-model",
                    f"OPENAI_BASE_URL={base_url}",
                    "OPENAI_API_KEY=test-provider-key-value",
                    "",
                )
            ),
        )

    def _write_executor(self) -> None:
        self.write_bytes(executor.RUNNER_SOURCE_PATH, Path(executor.__file__).read_bytes())

    def _write_provider_harness(self) -> None:
        harness = Path(executor.__file__).with_name(
            "week84_92_provider_turn_harness.py"
        )
        self.write_bytes(executor.TRUSTED_PROVIDER_HARNESS_PATH, harness.read_bytes())

    def add_package_identity(self, gate_id: str, candidate: str) -> str:
        artifact_root = executor.PROVIDER_ARTIFACT_ROOTS[gate_id]
        evidence_path = f"{artifact_root}/package-identity.json"
        package_root = self.root / _parts(executor.FIXED_PACKAGE_ROOT)
        tree = executor._package_inventory(package_root)
        snapshot = {
            "headCommit": candidate,
            "treeObjectId": _git(self.root, "rev-parse", f"{candidate}^{{tree}}"),
            "gitStatusPorcelainV1": "",
            "trackedStatus": "Clean",
        }
        command_id = "desktop-package-rebuild"
        report_path = (
            f"{artifact_root}/gate-evidence/{gate_id}/"
            f"{command_id}.build.trusted-product-report.json"
        )
        report = {
            "schemaVersion": executor.SCHEMA_VERSION,
            "runnerId": executor.RUNNER_ID,
            "productCandidate": candidate,
            "commandId": command_id,
            "exitCode": 0,
            "resultProtocol": executor.PRODUCT_COMMAND_RESULT_PROTOCOL,
            "commandPolicy": {"policyId": executor.TRUSTED_PRODUCT_POLICY_ID},
            "counts": {"discovered": 1, "passed": 1, "failed": 0, "skipped": 0},
            "checkoutIdentity": {
                "mode": "candidate-exact",
                "productCandidate": candidate,
                "expectedHead": candidate,
                "before": snapshot,
                "after": snapshot,
            },
        }
        self.write_json(report_path, report)
        report_raw = (self.root / _parts(report_path)).read_bytes()
        started = _past_timestamp(3)
        finished = _past_timestamp(2)
        build_receipt_path = f"{artifact_root}/package-build-receipt.json"
        self.write_json(
            build_receipt_path,
            {
                "schemaVersion": executor.SCHEMA_VERSION,
                "protocol": executor.PACKAGE_BUILD_RECEIPT_PROTOCOL,
                "status": "Passed",
                "goalId": executor.GOAL_ID,
                "productCandidate": candidate,
                "sourceTreeObjectId": _git(
                    self.root, "rev-parse", f"{candidate}^{{tree}}"
                ),
                "packageRoot": executor.FIXED_PACKAGE_ROOT,
                "treeRootSha256": tree["treeRootSha256"],
                "entryCount": tree["entryCount"],
                "totalBytes": tree["totalBytes"],
                "entrypoint": executor.FIXED_PACKAGE_ENTRYPOINT,
                "argv": list(executor.FIXED_PACKAGE_ARGV),
                "appHostPath": executor.FIXED_APPHOST_PATH,
                "commandId": command_id,
                "trustedCommandReport": {
                    "path": report_path,
                    "rawSha256": hashlib.sha256(report_raw).hexdigest(),
                },
                "startedAt": started,
                "finishedAt": finished,
                "exitCode": 0,
            },
        )
        build_raw = (self.root / _parts(build_receipt_path)).read_bytes()
        self.write_json(
            evidence_path,
            {
                "schemaVersion": executor.SCHEMA_VERSION,
                "protocol": executor.PACKAGE_TREE_IDENTITY_PROTOCOL,
                "status": "Passed",
                "goalId": executor.GOAL_ID,
                "productCandidate": candidate,
                "packageRoot": executor.FIXED_PACKAGE_ROOT,
                "buildReceipt": {
                    "path": build_receipt_path,
                    "rawSha256": hashlib.sha256(build_raw).hexdigest(),
                },
                "tree": tree,
                "entrypoint": {
                    "path": executor.FIXED_PACKAGE_ENTRYPOINT,
                    "argv": list(executor.FIXED_PACKAGE_ARGV),
                },
                "appHostPath": executor.FIXED_APPHOST_PATH,
            },
        )
        return evidence_path

    def add_provider_boundary(
        self, gate_id: str, candidate: str, evidence_path: str
    ) -> None:
        evidence_raw = (self.root / _parts(evidence_path)).read_bytes()
        evidence = json.loads(evidence_raw.decode("utf-8"))
        tree = evidence["tree"]
        boundary_gate = executor.PROVIDER_BOUNDARY_GATES[gate_id]
        request_id = f"PB-{boundary_gate}-EXECUTOR"
        challenge = "CH-EXECUTOR1"
        response = f"{request_id} {challenge} Approved"
        issued = datetime.now(timezone.utc) - timedelta(minutes=2)
        decided = issued - timedelta(minutes=1)
        expires = issued + timedelta(days=1)
        self.write_json(
            executor.PROVIDER_BOUNDARY_DECISION_PATHS[gate_id],
            {
                "schemaVersion": executor.SCHEMA_VERSION,
                "protocol": executor.PROVIDER_BOUNDARY_DECISION_PROTOCOL,
                "goalId": executor.GOAL_ID,
                "decisionId": f"PBD-{boundary_gate}-EXECUTOR",
                "boundaryGate": boundary_gate,
                "mode": "cooperative-candidate",
                "productCandidate": candidate,
                "packageIdentity": {
                    "path": evidence_path,
                    "rawSha256": hashlib.sha256(evidence_raw).hexdigest(),
                    "packageRoot": executor.FIXED_PACKAGE_ROOT,
                    "treeRootSha256": tree["treeRootSha256"],
                    "entryCount": tree["entryCount"],
                    "totalBytes": tree["totalBytes"],
                    "entrypoint": executor.FIXED_PACKAGE_ENTRYPOINT,
                    "argv": list(executor.FIXED_PACKAGE_ARGV),
                    "appHostPath": executor.FIXED_APPHOST_PATH,
                },
                "allowedGates": list(
                    executor.PROVIDER_BOUNDARY_ALLOWED_GATES[boundary_gate]
                ),
                "allowedScopes": list(executor.PROVIDER_BOUNDARY_ALLOWED_SCOPES),
                "authorizedTurns": 34,
                "maxTurns": executor.PROVIDER_MAX_TURNS,
                "userChallengeReceipt": {
                    "requestId": request_id,
                    "challengeCode": challenge,
                    "response": response,
                    "rawResponseSha256": hashlib.sha256(
                        response.encode("utf-8")
                    ).hexdigest(),
                    "confirmedBy": "User",
                    "decidedAt": decided.isoformat(timespec="microseconds").replace(
                        "+00:00", "Z"
                    ),
                },
                "issuedAt": issued.isoformat(timespec="microseconds").replace(
                    "+00:00", "Z"
                ),
                "expiresAt": expires.isoformat(timespec="microseconds").replace(
                    "+00:00", "Z"
                ),
                "revoked": False,
                "strongCanaries": None,
                "cooperativeTrustStatement": executor.COOPERATIVE_TRUST_STATEMENT,
            },
        )
        self.commit_all("provider boundary decision")

    def prepare_provider_candidate(
        self,
        gate_id: str,
        *,
        behavior: str = "exact",
        omit_phase: str | None = None,
        delete_driver_phase: str | None = None,
        include_controlled_prerequisites: bool = False,
    ) -> tuple[str, str]:
        self._write_executor()
        self._write_provider_harness()
        self.write_text(
            "tools/.gitattributes",
            "week84_92_provider_drivers/*.mjs -text\n",
        )
        for phase, path in executor.PROVIDER_SCENARIO_PATHS.items():
            if phase != omit_phase:
                self.write_text(path, _scenario_source(behavior))
        driver_raw = "console.log('prior-sealed test driver');\n"
        for path in executor.PROVIDER_DRIVER_PATHS.values():
            self.write_text(path, driver_raw)
        artifact_root = executor.PROVIDER_ARTIFACT_ROOTS[gate_id]
        command_control_path = (
            f"docs_md/weekly/84_92_command_control/{gate_id}.json"
        )
        gate_requirements = [
            {
                "week": _week_lane(gate_id)[0],
                "lane": _week_lane(gate_id)[1],
                "gateId": gate_id,
                "resultPath": f"{artifact_root}/gates/{gate_id}.json",
                "requiredCommandIds": ["provider-harness"],
                "controlledWrite": gate_id in {"W84-G8", "W92-G7"},
                "commandControl": {
                    "path": command_control_path,
                    "sourceTrust": "prior-sealed",
                    "predecessorMode": "prior-gate",
                    "rolePolicy": {
                        "requiredAll": ["adapter"],
                        "requiredAny": [["oracle", "test"]],
                        "optional": ["fixture", "parser"],
                    },
                },
            }
        ]
        if include_controlled_prerequisites:
            gate_requirements[:0] = [
                {
                    "week": _week_lane(prerequisite)[0],
                    "lane": _week_lane(prerequisite)[1],
                    "gateId": prerequisite,
                    "resultPath": executor.CANONICAL_GATE_RESULT_PATHS[
                        prerequisite
                    ],
                    "requiredCommandIds": [f"command-{prerequisite}"],
                    "controlledWrite": False,
                }
                for prerequisite in executor.CONTROLLED_WRITE_PRECONDITIONS[gate_id]
            ]
        self.write_json(
            executor.GATE_REQUIREMENTS_PATH,
            {
                "schemaVersion": executor.SCHEMA_VERSION,
                "goalId": executor.GOAL_ID,
                "frozenPolicies": {
                    executor.TRUSTED_GIT_POLICY_NAME: trusted_git_policy(),
                    executor.TRUSTED_PROVIDER_POLICY_NAME: trusted_provider_policy(behavior),
                },
                "gates": gate_requirements,
            },
        )
        bootstrap = self.commit_all("provider bootstrap and prior drivers")
        expected_phases = [
            phase
            for phase in executor.PROVIDER_DRIVER_PATHS
            if phase in set(executor.PROVIDER_PHASE_LAYOUT[gate_id])
        ]
        runtime_drivers = []
        for phase in expected_phases:
            path = executor.PROVIDER_DRIVER_PATHS[phase]
            raw = (self.root / _parts(path)).read_bytes()
            runtime_drivers.append(
                {
                    "phase": phase,
                    "path": path,
                    "sha256": hashlib.sha256(raw).hexdigest(),
                    "gitBlobSha": _git(
                        self.root, "hash-object", "--no-filters", path
                    ),
                    "originControlRevision": bootstrap,
                }
            )
        self.write_json(
            command_control_path,
            {
                "schemaVersion": executor.SCHEMA_VERSION,
                "protocol": executor.COMMAND_CONTROL_PROTOCOL,
                "goalId": executor.GOAL_ID,
                "gateId": gate_id,
                "sourceTrust": "prior-sealed",
                "preparedFromRevision": bootstrap,
                "sources": [{"fixture": "executor-test"}],
                "sourceTrees": [],
                "productionProjection": None,
                "runtimeDrivers": runtime_drivers,
            },
        )
        self.commit_all("provider gate command control")
        if delete_driver_phase is not None:
            (self.root / _parts(executor.PROVIDER_DRIVER_PATHS[delete_driver_phase])).unlink()
        self.write_text("candidate.txt", "candidate\n")
        self.write_bytes(
            f"{executor.FIXED_PACKAGE_ROOT}/{executor.FIXED_PACKAGE_ENTRYPOINT}",
            b"MZ-fake-desktop-executable\x00",
        )
        self.write_bytes(
            f"{executor.FIXED_PACKAGE_ROOT}/{executor.FIXED_APPHOST_PATH}",
            b"MZ-fake-apphost-executable\x00",
        )
        self.write_bytes(
            f"{executor.FIXED_PACKAGE_ROOT}/resources/app.asar",
            b"fake-electron-archive\x00",
        )
        self.write_json("apps/desktop/package.json", {"name": "c-aicli-desktop"})
        candidate = self.commit_all("provider candidate")
        evidence = self.add_package_identity(gate_id, candidate)
        self.add_provider_boundary(gate_id, candidate, evidence)
        return candidate, evidence

    def prepare_test_candidate(
        self,
        *,
        code: str = DEFAULT_POLICY_CODE,
        gate_id: str = "W84-G0",
    ) -> tuple[str, str, str]:
        command_id = executor.TRUSTED_TEST_COMMAND_ID
        artifact_root = {
            "W85-R0": "artifacts/week85-renderer-feature-boundaries",
            "W85-C0": "artifacts/week85-cli-composition",
        }.get(gate_id, "artifacts/week84-renderer-listener-retention")
        self._write_executor()
        source_payloads = {
            "tools/week84_92_goal_integrity.py": "# semantic fixture dependency\n",
            "tools/week84_92_evidence_anchor.py": "# semantic fixture dependency\n",
            "tools/week84_92_provider_turn_harness.py": "# semantic fixture dependency\n",
            "tools/validate-week84-92-goal-evidence.py": (
                "def run_cli(argv=None):\n    return 0\n"
            ),
            "tools/test_validate_week84_92_goal_evidence.py": "# empty fixture suite\n",
            "tools/test_week84_92_goal_integrity.py": "# empty fixture suite\n",
            "tools/test_week84_92_gate_requirements.py": "# empty fixture suite\n",
            "tools/test_week84_92_evidence_anchor.py": "# empty fixture suite\n",
            "tools/test_week84_92_trusted_executor.py": code.replace(
                DEFAULT_POLICY_RUNNER, ""
            ),
        }
        for path, payload in source_payloads.items():
            self.write_text(path, payload)
        for path in executor.SEMANTIC_READ_SOURCES:
            self.write_text(path, f"SOURCE_NAME = {path!r}\n")
        self.write_text(
            "tools/.gitattributes",
            ".gitattributes -text\n*.py -text\nweek84_92_commands/*.py -text\n",
        )
        for path in executor.SEMANTIC_BOOTSTRAP_SOURCES:
            destination = self.root / _parts(path)
            if destination.exists():
                continue
            if path.endswith(".gitattributes"):
                self.write_text(path, ".gitattributes -text\n* -text\n")
            elif path.endswith(".json"):
                self.write_text(path, "{}\n")
            else:
                self.write_text(path, "# semantic bootstrap fixture\n")
        entry_sources = []
        for module_name, path in executor.SEMANTIC_ENTRY_SOURCES:
            raw = (self.root / _parts(path)).read_bytes()
            entry_sources.append(
                {
                    "moduleName": module_name,
                    "path": path,
                    "sha256": hashlib.sha256(raw).hexdigest(),
                }
            )
        read_sources = []
        for path in executor.SEMANTIC_READ_SOURCES:
            raw = (self.root / _parts(path)).read_bytes()
            read_sources.append(
                {"path": path, "sha256": hashlib.sha256(raw).hexdigest()}
            )
        self.write_json(
            executor.GATE_REQUIREMENTS_PATH,
            {
                "schemaVersion": executor.SCHEMA_VERSION,
                "goalId": executor.GOAL_ID,
                "frozenPolicies": {
                    executor.TRUSTED_GIT_POLICY_NAME: trusted_git_policy(),
                    executor.TRUSTED_TEST_POLICY_NAME: trusted_test_policy(
                        entry_sources, read_sources
                    ),
                },
                "gates": [
                    {
                        "week": _week_lane(gate_id)[0],
                        "lane": _week_lane(gate_id)[1],
                        "gateId": gate_id,
                        "resultPath": f"{artifact_root}/gates/{gate_id}.json",
                        "requiredCommandIds": [command_id],
                        "requiredEvidenceBasenames": ["test-results.json"],
                        "controlledWrite": False,
                    }
                ],
            },
        )
        self.write_text("candidate.txt", "candidate\n")
        self.write_json(executor.GOAL_STATE_PATH, {"fixture": "pre-semantic"})
        return gate_id, command_id, self.commit_all("test candidate")

    def prepare_product_candidate(
        self,
        *,
        gate_id: str = "W84-G1",
        command_id: str = "product-smoke",
        script: str | None = None,
        source_error: str | None = None,
    ) -> tuple[str, str, str]:
        artifact_root = "artifacts/week84-renderer-listener-retention"
        if script is None:
            script = (
                "import os\n"
                "assert 'OPENAI_API_KEY' not in os.environ\n"
                "print('adapter workload observed')\n"
            )
        self._write_executor()
        verifier_path = "tools/product_smoke_verifier.py"
        verifier_argument = (
            "tools.product_smoke_verifier.ProductSmokeVerifier."
            "test_independent_product_observation"
        )
        verifier_source = (
            "import os\n"
            "import unittest\n\n"
            "class ProductSmokeVerifier(unittest.TestCase):\n"
            "    def test_independent_product_observation(self):\n"
            "        self.assertNotIn('OPENAI_API_KEY', os.environ)\n"
        )
        self.write_text(
            "tools/.gitattributes",
            "week84_92_commands/*.py -text\nproduct_smoke_verifier.py -text\n",
        )
        self.write_json(
            executor.GATE_REQUIREMENTS_PATH,
            {
                "schemaVersion": executor.SCHEMA_VERSION,
                "goalId": executor.GOAL_ID,
                "frozenPolicies": {
                    executor.TRUSTED_GIT_POLICY_NAME: trusted_git_policy(),
                    executor.TRUSTED_PRODUCT_POLICY_NAME: trusted_product_policy(),
                },
                "gates": [
                    {
                        "week": _week_lane(gate_id)[0],
                        "lane": _week_lane(gate_id)[1],
                        "gateId": gate_id,
                        "resultPath": f"{artifact_root}/gates/{gate_id}.json",
                        "requiredCommandIds": [command_id],
                        "requiredEvidenceBasenames": [
                            "commands.json",
                            "final-summary.json",
                        ],
                        "controlledWrite": False,
                        "commandControl": {
                            "path": (
                                "docs_md/weekly/84_92_command_control/"
                                f"{gate_id}.json"
                            ),
                            "sourceTrust": "prior-sealed",
                            "predecessorMode": "prior-gate",
                            "rolePolicy": {
                                "requiredAll": ["adapter"],
                                "requiredAny": [["oracle", "test"]],
                                "optional": ["fixture", "parser"],
                            },
                        },
                    }
                ],
            },
        )
        prepared = self.commit_all("product control bootstrap")
        adapter_path = f"{executor.TRUSTED_PRODUCT_SCRIPT_ROOT}/{command_id}.py"
        self.write_text(adapter_path, script)
        self.write_text(verifier_path, verifier_source)
        sources = []
        for role, path, verification in (
            ("adapter", adapter_path, None),
            (
                "test",
                verifier_path,
                {
                    "arguments": [verifier_argument],
                    "verifiesCommandIds": [command_id],
                },
            ),
        ):
            raw = (self.root / _parts(path)).read_bytes()
            sources.append(
                {
                    "commandId": command_id,
                    "role": role,
                    "path": path,
                    "sha256": hashlib.sha256(raw).hexdigest(),
                    "gitBlobSha": _git(
                        self.root, "hash-object", "--no-filters", path
                    ),
                    "origin": "prepared",
                    "originControlRevision": None,
                    "verification": verification,
                }
            )
        if source_error == "invalid-origin":
            sources[0]["origin"] = "prior-control"
        elif source_error == "missing-verifier":
            sources = sources[:1]
        elif source_error is not None:
            raise AssertionError("unsupported source error")
        control_path = f"docs_md/weekly/84_92_command_control/{gate_id}.json"
        self.write_json(
            control_path,
            {
                "schemaVersion": executor.SCHEMA_VERSION,
                "protocol": executor.COMMAND_CONTROL_PROTOCOL,
                "goalId": executor.GOAL_ID,
                "gateId": gate_id,
                "sourceTrust": "prior-sealed",
                "preparedFromRevision": prepared,
                "sources": sources,
                "sourceTrees": [],
                "productionProjection": None,
                "runtimeDrivers": [],
            },
        )
        self.commit_all("product command control")
        self.write_text("candidate.txt", "candidate\n")
        return gate_id, command_id, self.commit_all("product candidate")

    def prepare_controlled_candidate(
        self,
        gate_id: str,
        *,
        mixed_ancestors: bool = False,
    ) -> str:
        preconditions = executor.CONTROLLED_WRITE_PRECONDITIONS[gate_id]
        artifact_root = executor.PROVIDER_ARTIFACT_ROOTS[gate_id]
        requirements: list[dict[str, object]] = []
        for prerequisite in preconditions:
            requirements.append(
                {
                    "week": _week_lane(prerequisite)[0],
                    "lane": _week_lane(prerequisite)[1],
                    "gateId": prerequisite,
                    "resultPath": f"{artifact_root}/gates/{prerequisite}.json",
                    "requiredCommandIds": [f"command-{prerequisite}"],
                    "controlledWrite": False,
                }
            )
        requirements.append(
            {
                "week": _week_lane(gate_id)[0],
                "lane": _week_lane(gate_id)[1],
                "gateId": gate_id,
                "resultPath": f"{artifact_root}/gates/{gate_id}.json",
                "requiredCommandIds": ["controlled-write-harness"],
                "controlledWrite": True,
            }
        )
        self._write_executor()
        self._write_provider_harness()
        for path in executor.PROVIDER_SCENARIO_PATHS.values():
            self.write_text(path, _scenario_source("missing"))
        self.write_json(
            executor.GATE_REQUIREMENTS_PATH,
            {
                "schemaVersion": executor.SCHEMA_VERSION,
                "goalId": executor.GOAL_ID,
                "frozenPolicies": {
                    executor.TRUSTED_GIT_POLICY_NAME: trusted_git_policy(),
                    executor.TRUSTED_PROVIDER_POLICY_NAME: trusted_provider_policy("missing"),
                },
                "gates": requirements,
            },
        )
        self.write_text("candidate.txt", "candidate\n")
        base_candidate = self.commit_all("controlled candidate base")
        if mixed_ancestors:
            self.write_text("candidate.txt", "controlled final candidate\n")
            candidate = self.commit_all("controlled candidate final")
        else:
            candidate = base_candidate
        for index, prerequisite in enumerate(preconditions):
            result_path = f"{artifact_root}/gates/{prerequisite}.json"
            prerequisite_candidate = (
                base_candidate if mixed_ancestors and index % 2 == 0 else candidate
            )
            self.write_json(
                result_path,
                {
                    "schemaVersion": executor.SCHEMA_VERSION,
                    "gateId": prerequisite,
                    "status": "Passed",
                    "identity": {"productCandidate": prerequisite_candidate},
                    "finishedAt": _past_timestamp(),
                },
            )
        return candidate

    def add_controlled_prerequisite_results(
        self,
        gate_id: str,
        candidate: str,
        *,
        mixed_ancestors: bool = False,
    ) -> None:
        artifact_root = executor.PROVIDER_ARTIFACT_ROOTS[gate_id]
        ancestor = (
            _git(self.root, "rev-parse", f"{candidate}^")
            if mixed_ancestors
            else candidate
        )
        for index, prerequisite in enumerate(
            executor.CONTROLLED_WRITE_PRECONDITIONS[gate_id]
        ):
            prerequisite_candidate = (
                ancestor if mixed_ancestors and index % 2 == 0 else candidate
            )
            self.write_json(
                f"{artifact_root}/gates/{prerequisite}.json",
                {
                    "schemaVersion": executor.SCHEMA_VERSION,
                    "gateId": prerequisite,
                    "status": "Passed",
                    "identity": {"productCandidate": prerequisite_candidate},
                    "finishedAt": _past_timestamp(),
                },
            )

    def add_controlled_preauthorization(
        self,
        gate_id: str,
        candidate: str,
        *,
        actions: tuple[str, ...] = ("apply_patch", "shell"),
        decided_at: str | None = None,
        authorized_at: str | None = None,
    ) -> dict[str, object]:
        week = "W84" if gate_id == "W84-G8" else "W92"
        decision_time = decided_at or _past_timestamp(3)
        authorization_time = authorized_at or _past_timestamp(2)
        preauthorization_id = f"CWPA-{week}-EXECUTORTEST01"
        candidate_binding = {
            "productCandidate": candidate,
            "treeObjectId": _git(self.root, "rev-parse", f"{candidate}^{{tree}}"),
        }
        prerequisites = executor._precondition_bindings(
            self.root,
            gate_id,
            candidate,
            authorization_time,
        )
        workspace_name = f"caicli-week{week[1:]}-write-executorfixture"
        workspace = {
            "workspaceId": f"CWW-{week}-EXECUTORTEST01",
            "workspaceName": workspace_name,
            "workspaceRelativePath": (
                f"{executor.PROVIDER_ARTIFACT_ROOTS[gate_id]}/"
                f"controlled-write-workspaces/{workspace_name}"
            ),
            "owner": executor.CONTROLLED_WRITE_HARNESS_OWNER,
            "scenarioPath": executor.PROVIDER_SCENARIO_PATHS["controlled-write"],
            "cleanupRequired": True,
        }
        decision_bindings = executor._decision_bindings(
            candidate_binding=candidate_binding,
            prerequisite_bindings=prerequisites,
            workspace_identity=workspace,
            write_transition=executor.CONTROLLED_WRITE_TRANSITION,
            validation_command=executor.CONTROLLED_WRITE_VALIDATION,
        )
        approval_ids = {
            "apply_patch": f"CWAD-{week}-PATCH-EXECUTORTEST01",
            "shell": f"CWAD-{week}-SHELL-EXECUTORTEST01",
        }
        for action in actions:
            self.write_json(
                executor.CONTROLLED_WRITE_APPROVAL_DECISIONS[gate_id][action],
                {
                    "schemaVersion": executor.SCHEMA_VERSION,
                    "protocol": executor.CONTROLLED_WRITE_APPROVAL_PROTOCOL,
                    "goalId": executor.GOAL_ID,
                    "preauthorizationId": preauthorization_id,
                    "gateId": gate_id,
                    "productCandidate": candidate,
                    "approvalId": approval_ids[action],
                    "action": action,
                    "decision": "Approve",
                    "durable": True,
                    "decisionBindings": decision_bindings,
                    "decidedAt": decision_time,
                },
            )
        decision_commit = self.commit_all("controlled approval decisions")
        approvals: list[dict[str, str]] = []
        for action in actions:
            decision_path = executor.CONTROLLED_WRITE_APPROVAL_DECISIONS[gate_id][action]
            decision_raw = (self.root / _parts(decision_path)).read_bytes()
            approvals.append(
                {
                    "approvalId": approval_ids[action],
                    "action": action,
                    "path": decision_path,
                    "sha256": hashlib.sha256(decision_raw).hexdigest(),
                    "commit": decision_commit,
                }
            )
        preauthorization: dict[str, object] = {
            "schemaVersion": executor.SCHEMA_VERSION,
            "protocol": executor.CONTROLLED_WRITE_PREAUTHORIZATION_PROTOCOL,
            "goalId": executor.GOAL_ID,
            "preauthorizationId": preauthorization_id,
            "gateId": gate_id,
            "status": "Authorized",
            "productCandidate": candidate,
            "candidateBinding": candidate_binding,
            "preconditionGateBindings": prerequisites,
            "harnessWorkspaceIdentity": workspace,
            "writeTransition": executor.CONTROLLED_WRITE_TRANSITION,
            "validationCommand": executor.CONTROLLED_WRITE_VALIDATION,
            "approvalDecisionBindings": approvals,
            "authorizedAt": authorization_time,
        }
        self.write_json(
            executor.CONTROLLED_WRITE_PREAUTHORIZATIONS[gate_id],
            preauthorization,
        )
        self.commit_all("controlled write preauthorization")
        return preauthorization


@contextmanager
def goal_repository():
    repository = TemporaryGoalRepository()
    try:
        yield repository
    finally:
        repository.close()


@contextmanager
def provider_server():
    records: list[dict[str, object]] = []

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, _format: str, *_args: object) -> None:
            return

        def do_POST(self) -> None:  # noqa: N802
            length = int(self.headers["Content-Length"])
            body = self.rfile.read(length)
            records.append(
                {
                    "path": self.path,
                    "authorization": self.headers.get("Authorization"),
                    "bodySha256": hashlib.sha256(body).hexdigest(),
                }
            )
            response = b'{"ok":true}'
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(response)))
            self.end_headers()
            self.wfile.write(response)

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        yield f"http://127.0.0.1:{server.server_port}/v1", records
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=5)


@contextmanager
def redirect_provider_server():
    state = {"source": 0, "target": 0}

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, _format: str, *_args: object) -> None:
            return

        def do_POST(self) -> None:  # noqa: N802
            length = int(self.headers["Content-Length"])
            self.rfile.read(length)
            if self.path == "/v1/redirect-target":
                state["target"] += 1
                self.send_response(200)
                self.send_header("Content-Length", "2")
                self.end_headers()
                self.wfile.write(b"{}")
                return
            state["source"] += 1
            self.send_response(307)
            self.send_header("Location", "/v1/redirect-target")
            self.send_header("Content-Length", "0")
            self.end_headers()

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        yield f"http://127.0.0.1:{server.server_port}/v1", state
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=5)


@contextmanager
def streaming_provider_server():
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, _format: str, *_args: object) -> None:
            return

        def do_POST(self) -> None:  # noqa: N802
            length = int(self.headers["Content-Length"])
            self.rfile.read(length)
            response = b'{"stream":true}'
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(response)))
            self.end_headers()
            self.wfile.write(response[:1])
            self.wfile.flush()
            time.sleep(2.0)
            self.wfile.write(response[1:])
            self.wfile.flush()

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        yield f"http://127.0.0.1:{server.server_port}/v1"
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=5)


class ProviderHarnessBoundaryTests(unittest.TestCase):
    @staticmethod
    def _descriptor() -> dict[str, object]:
        return {
            "batchId": "batch-provider-test",
            "phase": "provider-read-only",
            "reservations": [
                {
                    "reservationId": "reservation-provider-test",
                    "reservationSequence": 1,
                    "turnOrdinal": 1,
                }
            ],
        }

    def test_responses_body_is_allowlisted_rebuilt_and_marker_bound(self) -> None:
        descriptor = self._descriptor()
        state = provider_harness._GatewayState(
            descriptor=descriptor,
            model="gpt-test",
            api_key="test-real-provider-key",
            run_token="a" * 64,
            upstream=("api.example.invalid", "https", 443, "/v1/responses", False),
            monitor=None,
        )
        marker = (
            "CAICLI-W84-92-TURN:batch-provider-test:1:"
            "reservation-provider-test"
        )
        request = {
            "model": "gpt-test",
            "input": [
                {
                    "type": "message",
                    "role": "user",
                    "content": [{"type": "input_text", "text": marker}],
                },
                {
                    "type": "function_call_output",
                    "call_id": "call-1",
                    "output": "ok",
                },
            ],
            "instructions": "trusted scenario",
            "previous_response_id": "resp-1",
            "tools": [{"type": "function", "name": "read_file"}],
            "stream": False,
        }
        with mock.patch.object(
            provider_harness,
            "_upstream_exchange",
            return_value=(200, b'{"id":"resp-ok"}', "application/json"),
        ) as exchange:
            status, _raw, _content_type = state.exchange(
                json.dumps(request).encode("utf-8")
            )
        self.assertEqual(200, status)
        sent = json.loads(exchange.call_args.kwargs["body"].decode("utf-8"))
        self.assertEqual(request, sent)
        self.assertEqual("Succeeded", state.accepted_records()[0]["status"])

    def test_synthetic_envelope_and_direct_non_apphost_reject_before_allocation(self) -> None:
        descriptor = self._descriptor()
        monitor = mock.Mock()
        monitor.authorize_apphost.return_value = False
        state = provider_harness._GatewayState(
            descriptor=descriptor,
            model="gpt-test",
            api_key="test-real-provider-key",
            run_token="a" * 64,
            upstream=("api.example.invalid", "https", 443, "/v1/responses", False),
            monitor=monitor,
        )
        handler = mock.Mock()
        handler.client_address = ("127.0.0.1", 50123)
        handler.server.server_address = ("127.0.0.1", 51234)
        with mock.patch.object(
            provider_harness, "_socket_owner_pid", return_value=os.getpid()
        ):
            self.assertFalse(state.authorize(handler))
        self.assertEqual(0, state.next_index)
        marker = (
            "CAICLI-W84-92-TURN:batch-provider-test:1:"
            "reservation-provider-test"
        )
        with self.assertRaisesRegex(provider_harness.HarnessError, "REQUEST_BINDING"):
            state.exchange(
                json.dumps(
                    {
                        "protocol": "synthetic-envelope",
                        "model": "gpt-test",
                        "input": marker,
                        "stream": False,
                    }
                ).encode("utf-8")
            )
        self.assertEqual(0, state.next_index)

    def test_buffered_response_secret_split_is_rejected(self) -> None:
        secret = provider_harness.TEST_LOOPBACK_KEY

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, _format: str, *_arguments: object) -> None:
                return

            def do_POST(self) -> None:  # noqa: N802
                size = int(self.headers.get("Content-Length", "0"))
                self.rfile.read(size)
                body = ('{"value":"' + secret + '"}').encode("utf-8")
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                split = len(body) // 2
                self.wfile.write(body[:split])
                self.wfile.flush()
                self.wfile.write(body[split:])

        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            with mock.patch.dict(
                os.environ,
                {
                    provider_harness.TEST_LOOPBACK_FLAG:
                    provider_harness.TEST_LOOPBACK_VALUE
                },
                clear=False,
            ):
                upstream = provider_harness._parse_upstream(
                    f"http://127.0.0.1:{server.server_port}/v1", secret
                )
                with self.assertRaisesRegex(
                    provider_harness.HarnessError, "UPSTREAM_RESPONSE"
                ):
                    provider_harness._upstream_exchange(
                        upstream=upstream,
                        api_key=secret,
                        body=b'{"model":"gpt-test","input":"x","stream":false}',
                        secrets=(secret,),
                    )
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=5)


class ProviderLedgerTests(unittest.TestCase):
    def test_missing_prior_sealed_driver_rejects_before_secret_or_reservation(self) -> None:
        with goal_repository() as repository:
            candidate, evidence_path = repository.prepare_provider_candidate(
                "W84-G6", delete_driver_phase="provider-read-only"
            )
            with mock.patch.object(
                executor,
                "_load_provider_environment",
                side_effect=AssertionError("provider secret must not be read"),
            ) as env_loader:
                with self.assertRaisesRegex(
                    executor.ExecutorError, "PROVIDER_DRIVER_SOURCE"
                ):
                    executor.run_provider_segment(
                        repository.root,
                        gate_id="W84-G6",
                        product_candidate=candidate,
                        attempt_id="attempt-missing-driver",
                        run_id="run-missing-driver",
                        phases=("provider-read-only",),
                        package_identity_evidence_path=evidence_path,
                    )
                env_loader.assert_not_called()
            ledger_path = repository.root / _parts(executor.PROVIDER_LEDGER_PATH)
            self.assertFalse(ledger_path.exists())

    def test_strong_mode_without_executor_isolation_fails_before_secret_or_reservation(self) -> None:
        with goal_repository() as repository:
            candidate, evidence_path = repository.prepare_provider_candidate("W84-G6")
            package = executor._package_identity_evidence_binding(
                repository.root,
                repository.root,
                gate_id="W84-G6",
                product_candidate=candidate,
                evidence_path=evidence_path,
            )
            strong = executor.ProviderBoundaryBinding(
                path=executor.PROVIDER_BOUNDARY_DECISION_PATHS["W84-G6"],
                sha256="a" * 64,
                first_add_commit=_git(repository.root, "rev-parse", "HEAD"),
                decision_id="PBD-W84-G6-STRONG",
                mode="strong-isolation",
                package_tree_root_sha256=package.tree_root_sha256,
                sandbox_policy_sha256="b" * 64,
            )
            with mock.patch.object(
                executor,
                "verify_provider_boundary_decision",
                return_value=strong,
            ), mock.patch.object(
                executor,
                "_load_provider_environment",
                side_effect=AssertionError("provider secret must not be read"),
            ) as env_loader:
                with self.assertRaisesRegex(
                    executor.ExecutorError, "STRONG_ISOLATION_UNAVAILABLE"
                ):
                    executor.run_provider_segment(
                        repository.root,
                        gate_id="W84-G6",
                        product_candidate=candidate,
                        attempt_id="attempt-strong-unavailable",
                        run_id="run-strong-unavailable",
                        phases=("provider-read-only",),
                        package_identity_evidence_path=evidence_path,
                    )
                env_loader.assert_not_called()
            self.assertFalse(
                (repository.root / _parts(executor.PROVIDER_LEDGER_PATH)).exists()
            )

    def test_provider_environment_is_exact_untracked_ignored_and_never_historical(self) -> None:
        with goal_repository() as repository:
            repository.write_text("tracked.txt", "tracked\n")
            repository.commit_all("base")
            repository.add_provider_environment("http://127.0.0.1:1/v1")
            child = executor._load_provider_environment(
                repository.root, executor.PROVIDER_ENV_PATH
            )
            self.assertEqual("test-model", child["OPENAI_MODEL"])
            repository.write_text(
                executor.PROVIDER_ENV_PATH,
                (
                    "OPENAI_MODEL=test-model\n"
                    "OPENAI_BASE_URL=http://127.0.0.1:1/v1\n"
                    "OPENAI_API_KEY=test-provider-key-value\n"
                    "EXTRA_SCOPE=forbidden\n"
                ),
            )
            with self.assertRaisesRegex(executor.ExecutorError, "PROVIDER_ENV_FORMAT"):
                executor._load_provider_environment(
                    repository.root, executor.PROVIDER_ENV_PATH
                )

        with goal_repository() as repository:
            repository.add_provider_environment("http://127.0.0.1:1/v1")
            _git(repository.root, "add", "-f", executor.PROVIDER_ENV_PATH)
            _git(repository.root, "add", ".gitignore")
            _git(repository.root, "commit", "--quiet", "-m", "tracked secret")
            with mock.patch.object(
                executor,
                "_read_fixed_bytes",
                wraps=executor._read_fixed_bytes,
            ) as reader:
                with self.assertRaisesRegex(
                    executor.ExecutorError, "PROVIDER_ENV_STORAGE"
                ):
                    executor._load_provider_environment(
                        repository.root, executor.PROVIDER_ENV_PATH
                    )
                reader.assert_not_called()

        with goal_repository() as repository:
            repository.add_provider_environment("http://127.0.0.1:1/v1")
            _git(repository.root, "add", "-f", executor.PROVIDER_ENV_PATH)
            _git(repository.root, "add", ".gitignore")
            _git(repository.root, "commit", "--quiet", "-m", "historical secret")
            _git(repository.root, "rm", "--cached", executor.PROVIDER_ENV_PATH)
            _git(repository.root, "commit", "--quiet", "-m", "untrack secret")
            with mock.patch.object(
                executor,
                "_read_fixed_bytes",
                wraps=executor._read_fixed_bytes,
            ) as reader:
                with self.assertRaisesRegex(
                    executor.ExecutorError, "PROVIDER_ENV_STORAGE"
                ):
                    executor._load_provider_environment(
                        repository.root, executor.PROVIDER_ENV_PATH
                    )
                reader.assert_not_called()

    def test_package_path_substitution_rejects_before_secret_read_or_reservation(self) -> None:
        for substituted_path, substituted_raw in (
            (executor.PROVIDER_ENV_PATH, b"OPENAI_API_KEY=must-not-copy\n"),
            (
                "artifacts/week84-renderer-listener-retention/arbitrary-package.bin",
                b"not-a-desktop-package",
            ),
        ):
            with self.subTest(path=substituted_path), goal_repository() as repository:
                candidate, evidence_path = repository.prepare_provider_candidate(
                    "W84-G6"
                )
                repository.write_bytes(substituted_path, substituted_raw)
                repository.write_json(
                    evidence_path,
                    {
                        "schemaVersion": executor.SCHEMA_VERSION,
                        "status": "Passed",
                        "productCandidate": candidate,
                        "packageIdentity": {
                            "path": substituted_path,
                            "sha256": hashlib.sha256(substituted_raw).hexdigest(),
                            "bytes": len(substituted_raw),
                        },
                    },
                )
                with mock.patch.object(
                    executor,
                    "_load_provider_environment",
                    side_effect=AssertionError("secret read must be after package preflight"),
                ) as env_loader:
                    with self.assertRaisesRegex(
                        executor.ExecutorError, "PACKAGE_BINARY_PATH"
                    ):
                        executor.run_provider_segment(
                            repository.root,
                            gate_id="W84-G6",
                            product_candidate=candidate,
                            attempt_id="attempt-package-substitution",
                            run_id="run-package-substitution",
                            phases=("provider-read-only",),
                            package_identity_evidence_path=evidence_path,
                        )
                    env_loader.assert_not_called()
                self.assertFalse(
                    (repository.root / _parts(executor.PROVIDER_LEDGER_PATH)).exists()
                )

    def test_recovery_initialises_both_empty_journals_without_environment(self) -> None:
        with goal_repository() as repository:
            repository.commit_all("trusted empty recovery baseline")
            ledger = executor.recover_open_provider_attempts(repository.root)
            self.assertEqual(0, ledger["usedTurns"])
            runtime = json.loads(
                (repository.root / _parts(executor.PROVIDER_RUNTIME_JOURNAL_PATH)).read_text("utf-8")
            )
            self.assertEqual(0, runtime["eventCount"])
            self.assertFalse((repository.root / executor.PROVIDER_ENV_PATH).exists())

    def test_budget_preserves_complete_resource_attempt_and_exact_batch_shapes(self) -> None:
        with goal_repository() as repository:
            candidate, _package = repository.prepare_provider_candidate("W84-G7")
            for index in range(16):
                batch = executor.reserve_provider_segment(
                    repository.root,
                    gate_id="W84-G7",
                    product_candidate=candidate,
                    attempt_id=f"resource-failure-{index}",
                    run_id=f"resource-run-{index}",
                    phases=("provider-resource",) * 6,
                )
                executor.complete_provider_segment(
                    repository.root, batch, child_succeeded=False
                )
            ledger = executor.read_provider_ledger(repository.root)
            self.assertEqual((96, 24), (ledger["usedTurns"], ledger["remainingTurns"]))
            with self.assertRaisesRegex(executor.ExecutorError, "PROVIDER_CAP"):
                executor.reserve_provider_segment(
                    repository.root,
                    gate_id="W84-G7",
                    product_candidate=candidate,
                    attempt_id="over-cap",
                    run_id="over-cap-run",
                    phases=("provider-resource",) * 6,
                )
            with self.assertRaisesRegex(executor.ExecutorError, "SEGMENT_PHASES"):
                executor.reserve_provider_segment(
                    repository.root,
                    gate_id="W84-G7",
                    product_candidate=candidate,
                    attempt_id="partial",
                    run_id="partial-run",
                    phases=("provider-resource",),
                )

    def test_crash_recovery_charges_open_reservation(self) -> None:
        with goal_repository() as repository:
            candidate, _package = repository.prepare_provider_candidate("W84-G6")
            executor.reserve_provider_segment(
                repository.root,
                gate_id="W84-G6",
                product_candidate=candidate,
                attempt_id="crashed",
                run_id="crashed-run",
                phases=("provider-read-only",),
            )
            recovered = executor.recover_open_provider_attempts(repository.root)
            self.assertEqual(1, recovered["usedTurns"])
            self.assertEqual(
                ["Failed", "Failed"],
                [event["outcome"] for event in recovered["attemptEvents"]],
            )
            self.assertEqual(
                recovered["attemptEventCount"],
                executor.recover_open_provider_attempts(repository.root)[
                    "attemptEventCount"
                ],
            )
            runtime = json.loads(
                (repository.root / _parts(executor.PROVIDER_RUNTIME_JOURNAL_PATH)).read_text("utf-8")
            )
            self.assertEqual(1, runtime["eventCount"])
            self.assertIsNone(runtime["events"][0]["commandPolicy"])

    def test_invalid_multibatch_plan_reserves_zero_turns(self) -> None:
        with goal_repository() as repository, provider_server() as (base_url, _seen):
            candidate, package = repository.prepare_provider_candidate("W84-G6")
            repository.add_provider_environment(base_url)
            with self.assertRaisesRegex(executor.ExecutorError, "SEGMENT_PHASES"):
                executor.run_provider_segment(
                    repository.root,
                    gate_id="W84-G6",
                    product_candidate=candidate,
                    attempt_id="bad-plan",
                    run_id="bad-plan-run",
                    phases=("provider-read-only", *("provider-resource",) * 6),
                    package_identity_evidence_path=package,
                )
            self.assertEqual(0, executor.read_provider_ledger(repository.root)["usedTurns"])

    def test_exact_three_turn_run_has_observed_runtime_chain(self) -> None:
        with goal_repository() as repository:
            candidate, package = repository.prepare_provider_candidate("W84-G6")
            repository.add_provider_environment("https://api.example.invalid/v1")
            with mocked_provider_child(repository):
                result = executor.run_provider_segment(
                    repository.root,
                    gate_id="W84-G6",
                    product_candidate=candidate,
                    attempt_id="exact-attempt",
                    run_id="exact-run",
                    phases=executor.PROVIDER_PHASE_LAYOUT["W84-G6"],
                    package_identity_evidence_path=package,
                    finish_success=True,
                )
            self.assertEqual(0, result.exit_code)
            self.assertEqual(3, result.used_turns)
            ledger = executor.read_provider_ledger(repository.root)
            self.assertEqual("Passed", ledger["attemptEvents"][-1]["outcome"])
            runtime = json.loads(
                (repository.root / _parts(executor.PROVIDER_RUNTIME_JOURNAL_PATH)).read_text("utf-8")
            )
            self.assertEqual(3, runtime["eventCount"])
            self.assertEqual([1, 2, 2], [event["batchSize"] for event in runtime["events"]])
            self.assertTrue(all(event["outcome"] == "Succeeded" for event in runtime["events"]))
            all_evidence = b"".join(
                path.read_bytes()
                for path in (repository.root / "artifacts").rglob("*")
                if path.is_file()
            )
            self.assertNotIn(b"test-provider-key-value", all_evidence)

    def test_same_attempt_continues_with_exact_remaining_batch(self) -> None:
        with goal_repository() as repository:
            candidate, package = repository.prepare_provider_candidate("W84-G6")
            repository.add_provider_environment("https://api.example.invalid/v1")
            with mocked_provider_child(repository):
                first = executor.run_provider_segment(
                    repository.root,
                    gate_id="W84-G6",
                    product_candidate=candidate,
                    attempt_id="continued",
                    run_id="continued-one",
                    phases=("provider-read-only",),
                    package_identity_evidence_path=package,
                )
            self.assertFalse(first.attempt_finished)
            with mocked_provider_child(repository):
                final = executor.run_provider_segment(
                    repository.root,
                    gate_id="W84-G6",
                    product_candidate=candidate,
                    attempt_id="continued",
                    run_id="continued-two",
                    phases=("provider-recovery", "provider-recovery"),
                    package_identity_evidence_path=package,
                    finish_success=True,
                )
            self.assertEqual(0, final.exit_code)
            self.assertEqual(
                "Passed",
                executor.read_provider_ledger(repository.root)["attemptEvents"][-1]["outcome"],
            )

    def test_missing_extra_and_missing_candidate_harness_fail_closed(self) -> None:
        cases = (
            ("missing", None),
            ("extra", None),
            ("exact", "provider-read-only"),
        )
        for behavior, omitted in cases:
            with self.subTest(behavior=behavior, omitted=omitted):
                with goal_repository() as repository:
                    candidate, package = repository.prepare_provider_candidate(
                        "W84-G6", behavior=behavior, omit_phase=omitted
                    )
                    repository.add_provider_environment("https://api.example.invalid/v1")
                    if omitted is not None:
                        with self.assertRaises(executor.ExecutorError):
                            executor.run_provider_segment(
                                repository.root,
                                gate_id="W84-G6",
                                product_candidate=candidate,
                                attempt_id="failed-attempt",
                                run_id="failed-run",
                                phases=("provider-read-only",),
                                package_identity_evidence_path=package,
                            )
                        self.assertFalse(
                            (repository.root / _parts(executor.PROVIDER_LEDGER_PATH)).exists()
                        )
                        continue
                    with mocked_provider_child(repository, behavior):
                        result = executor.run_provider_segment(
                            repository.root,
                            gate_id="W84-G6",
                            product_candidate=candidate,
                            attempt_id="failed-attempt",
                            run_id="failed-run",
                            phases=("provider-read-only",),
                            package_identity_evidence_path=package,
                        )
                    self.assertNotEqual(0, result.exit_code)
                    ledger = executor.read_provider_ledger(repository.root)
                    self.assertEqual("Failed", ledger["attemptEvents"][-1]["outcome"])
                    runtime = json.loads(
                        (repository.root / _parts(executor.PROVIDER_RUNTIME_JOURNAL_PATH)).read_text("utf-8")
                    )
                    self.assertEqual("Failed", runtime["events"][-1]["outcome"])

    def test_package_tamper_and_dirty_checkout_reject_before_reservation(self) -> None:
        with goal_repository() as repository, provider_server() as (base_url, _seen):
            candidate, package = repository.prepare_provider_candidate("W84-G6")
            repository.add_provider_environment(base_url)
            package_document = json.loads((repository.root / _parts(package)).read_text("utf-8"))
            package_document["tree"]["entries"][0]["sha256"] = "0" * 64
            repository.write_json(package, package_document)
            with mock.patch.object(
                executor,
                "_load_provider_environment",
                side_effect=AssertionError("secret read must follow package validation"),
            ) as env_loader:
                with self.assertRaisesRegex(
                    executor.ExecutorError, "PACKAGE_TREE_IDENTITY"
                ):
                    executor.run_provider_segment(
                        repository.root,
                        gate_id="W84-G6",
                        product_candidate=candidate,
                        attempt_id="tamper",
                        run_id="tamper-run",
                        phases=("provider-read-only",),
                        package_identity_evidence_path=package,
                    )
                env_loader.assert_not_called()
            self.assertEqual(0, executor.read_provider_ledger(repository.root)["usedTurns"])

        with goal_repository() as repository, provider_server() as (base_url, _seen):
            candidate, package = repository.prepare_provider_candidate("W84-G6")
            repository.add_provider_environment(base_url)
            repository.write_text("candidate.txt", "dirty\n")
            with self.assertRaisesRegex(executor.ExecutorError, "CHECKOUT_DIRTY"):
                executor.run_provider_segment(
                    repository.root,
                    gate_id="W84-G6",
                    product_candidate=candidate,
                    attempt_id="dirty",
                    run_id="dirty-run",
                    phases=("provider-read-only",),
                    package_identity_evidence_path=package,
                )

    def test_mid_run_checkout_drift_marks_batch_failed(self) -> None:
        with goal_repository() as repository:
            candidate, package = repository.prepare_provider_candidate("W84-G6")
            repository.add_provider_environment("https://api.example.invalid/v1")
            with mocked_provider_child(repository, "drift"):
                result = executor.run_provider_segment(
                    repository.root,
                    gate_id="W84-G6",
                    product_candidate=candidate,
                    attempt_id="drift",
                    run_id="drift-run",
                    phases=("provider-read-only",),
                    package_identity_evidence_path=package,
                )
            self.assertEqual(124, result.exit_code)
            runtime = json.loads(
                (repository.root / _parts(executor.PROVIDER_RUNTIME_JOURNAL_PATH)).read_text("utf-8")
            )
            self.assertEqual("Failed", runtime["events"][0]["outcome"])

    def test_prelaunch_failure_gets_nonrefundable_runtime_receipt(self) -> None:
        with goal_repository() as repository, provider_server() as (base_url, _seen):
            candidate, package = repository.prepare_provider_candidate("W84-G6")
            repository.add_provider_environment(base_url)
            with mock.patch.object(
                executor,
                "_exclusive_write_json",
                side_effect=executor.ExecutorError("INJECTED_DESCRIPTOR_FAILURE"),
            ):
                with self.assertRaisesRegex(
                    executor.ExecutorError, "INJECTED_DESCRIPTOR_FAILURE"
                ):
                    executor.run_provider_segment(
                        repository.root,
                        gate_id="W84-G6",
                        product_candidate=candidate,
                        attempt_id="prelaunch-failure",
                        run_id="prelaunch-run",
                        phases=("provider-read-only",),
                        package_identity_evidence_path=package,
                    )
            ledger = executor.read_provider_ledger(repository.root)
            self.assertEqual(1, ledger["usedTurns"])
            self.assertEqual("Failed", ledger["attemptEvents"][-1]["outcome"])
            runtime = json.loads(
                (repository.root / _parts(executor.PROVIDER_RUNTIME_JOURNAL_PATH)).read_text("utf-8")
            )
            self.assertEqual(1, runtime["eventCount"])
            self.assertIsNone(runtime["events"][0]["descriptor"])
            self.assertEqual("Failed", runtime["events"][0]["outcome"])

    def test_gateway_does_not_follow_upstream_redirect(self) -> None:
        with goal_repository() as repository, redirect_provider_server() as (base_url, state):
            del repository
            with mock.patch.dict(
                os.environ,
                {
                    provider_harness.TEST_LOOPBACK_FLAG:
                    provider_harness.TEST_LOOPBACK_VALUE
                },
                clear=False,
            ):
                upstream = provider_harness._parse_upstream(
                    base_url, provider_harness.TEST_LOOPBACK_KEY
                )
                with self.assertRaisesRegex(
                    provider_harness.HarnessError, "UPSTREAM_RESPONSE"
                ):
                    provider_harness._upstream_exchange(
                        upstream=upstream,
                        api_key=provider_harness.TEST_LOOPBACK_KEY,
                        body=b'{"model":"gpt-test","input":"marker","stream":false}',
                        secrets=(provider_harness.TEST_LOOPBACK_KEY,),
                    )
            self.assertEqual({"source": 1, "target": 0}, state)

    def test_provider_api_and_cli_have_no_argv_or_env_path(self) -> None:
        parameters = inspect.signature(executor.run_provider_segment).parameters
        self.assertNotIn("argv", parameters)
        self.assertNotIn("env_file", parameters)
        marker = "sk-do-not-echo-provider-argv"
        result = subprocess.run(
            [
                sys.executable,
                str(Path(executor.__file__)),
                "provider-run",
                "--env-file",
                marker,
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            shell=False,
            check=False,
        )
        self.assertEqual(2, result.returncode)
        self.assertNotIn(marker.encode("utf-8"), result.stdout + result.stderr)


class ControlledCheckoutTests(unittest.TestCase):
    def test_preauthorization_failures_never_reserve_a_turn(self) -> None:
        with goal_repository() as repository:
            candidate = repository.prepare_controlled_candidate("W84-G8")
            with self.assertRaisesRegex(executor.ExecutorError, "MISSING_PATH"):
                executor.consume_controlled_write_lease(
                    repository.root,
                    gate_id="W84-G8",
                    product_candidate=candidate,
                    lease_id="CW-W84-NOPREAUTH01",
                    nonce=b"n" * 32,
                    issued_at=_past_timestamp(1),
                )
            self.assertEqual(
                0,
                executor.read_provider_ledger(repository.root)["usedTurns"],
            )

        with goal_repository() as repository:
            candidate = repository.prepare_controlled_candidate("W84-G8")
            repository.add_controlled_preauthorization(
                "W84-G8",
                candidate,
                actions=("apply_patch",),
            )
            with self.assertRaisesRegex(
                executor.ExecutorError,
                "CONTROLLED_PREAUTHORIZATION_BINDING",
            ):
                executor.consume_controlled_write_lease(
                    repository.root,
                    gate_id="W84-G8",
                    product_candidate=candidate,
                    lease_id="CW-W84-ONEAPPROVAL1",
                    nonce=b"a" * 32,
                    issued_at=_past_timestamp(1),
                )
            self.assertEqual(
                0,
                executor.read_provider_ledger(repository.root)["usedTurns"],
            )

        with goal_repository() as repository:
            candidate = repository.prepare_controlled_candidate("W84-G8")
            repository.add_controlled_preauthorization("W84-G8", candidate)
            executor.consume_controlled_write_lease(
                repository.root,
                gate_id="W84-G8",
                product_candidate=candidate,
                lease_id="CW-W84-HASHDRIFT01",
                nonce=b"h" * 32,
                issued_at=_past_timestamp(1),
            )
            repository.commit_all("controlled tombstone")
            preauthorization_path = repository.root / _parts(
                executor.CONTROLLED_WRITE_PREAUTHORIZATIONS["W84-G8"]
            )
            preauthorization = json.loads(
                preauthorization_path.read_text("utf-8")
            )
            preauthorization["status"] = "Drifted"
            repository.write_json(
                executor.CONTROLLED_WRITE_PREAUTHORIZATIONS["W84-G8"],
                preauthorization,
            )
            with self.assertRaisesRegex(executor.ExecutorError, "CHECKOUT_DIRTY"):
                executor.reserve_provider_segment(
                    repository.root,
                    gate_id="W84-G8",
                    product_candidate=candidate,
                    attempt_id="hash-drift",
                    run_id="hash-drift-run",
                    phases=("controlled-write",),
                )
            self.assertEqual(
                0,
                executor.read_provider_ledger(repository.root)["usedTurns"],
            )

    def test_lease_cannot_predate_durable_approval(self) -> None:
        with goal_repository() as repository:
            candidate = repository.prepare_controlled_candidate("W84-G8")
            repository.add_controlled_preauthorization("W84-G8", candidate)
            with self.assertRaisesRegex(
                executor.ExecutorError,
                "CONTROLLED_PREAUTHORIZATION_BINDING",
            ):
                executor.consume_controlled_write_lease(
                    repository.root,
                    gate_id="W84-G8",
                    product_candidate=candidate,
                    lease_id="CW-W84-EARLYLEASE01",
                    nonce=b"e" * 32,
                    issued_at=_past_timestamp(4),
                )
            self.assertEqual(
                0,
                executor.read_provider_ledger(repository.root)["usedTurns"],
            )

    def test_mixed_ancestor_prerequisites_are_accepted(self) -> None:
        with goal_repository() as repository:
            candidate, _evidence = repository.prepare_provider_candidate(
                "W84-G8", include_controlled_prerequisites=True
            )
            repository.add_controlled_prerequisite_results(
                "W84-G8",
                candidate,
                mixed_ancestors=True,
            )
            preauthorization = repository.add_controlled_preauthorization(
                "W84-G8",
                candidate,
            )
            prerequisite_candidates = {
                item["productCandidate"]
                for item in preauthorization["preconditionGateBindings"]
            }
            self.assertEqual(2, len(prerequisite_candidates))
            executor.consume_controlled_write_lease(
                repository.root,
                gate_id="W84-G8",
                product_candidate=candidate,
                lease_id="CW-W84-MIXEDANCESTOR",
                nonce=b"m" * 32,
                issued_at=_past_timestamp(1),
            )
            repository.commit_all("controlled tombstone")
            batch = executor.reserve_provider_segment(
                repository.root,
                gate_id="W84-G8",
                product_candidate=candidate,
                attempt_id="mixed-ancestor",
                run_id="mixed-ancestor-run",
                phases=("controlled-write",),
            )
            self.assertEqual(("controlled-write",), batch.phases)
            self.assertEqual(
                1,
                executor.read_provider_ledger(repository.root)["usedTurns"],
            )

    def test_reverse_and_nonancestor_prerequisites_reserve_zero(self) -> None:
        for relation in ("reverse", "nonancestor"):
            with self.subTest(relation=relation):
                with goal_repository() as repository:
                    candidate, _evidence = repository.prepare_provider_candidate(
                        "W84-G8", include_controlled_prerequisites=True
                    )
                    repository.add_controlled_prerequisite_results(
                        "W84-G8", candidate
                    )
                    repository.add_controlled_preauthorization("W84-G8", candidate)
                    executor.consume_controlled_write_lease(
                        repository.root,
                        gate_id="W84-G8",
                        product_candidate=candidate,
                        lease_id=f"CW-W84-{relation.upper()}1234",
                        nonce=(b"r" if relation == "reverse" else b"u") * 32,
                        issued_at=_past_timestamp(1),
                    )
                    repository.commit_all("controlled tombstone")
                    if relation == "reverse":
                        invalid_candidate = _git(repository.root, "rev-parse", "HEAD")
                    else:
                        tree = _git(repository.root, "rev-parse", "HEAD^{tree}")
                        invalid_candidate = _git(
                            repository.root,
                            "commit-tree",
                            tree,
                            "-m",
                            "unrelated prerequisite",
                        )
                    result_path = (
                        "artifacts/week84-renderer-listener-retention/"
                        "gates/W84-G0.json"
                    )
                    result = json.loads(
                        (repository.root / _parts(result_path)).read_text("utf-8")
                    )
                    result["identity"]["productCandidate"] = invalid_candidate
                    repository.write_json(result_path, result)
                    with self.assertRaisesRegex(
                        executor.ExecutorError,
                        "PRECONDITION_CANDIDATE_ANCESTRY",
                    ):
                        executor.reserve_provider_segment(
                            repository.root,
                            gate_id="W84-G8",
                            product_candidate=candidate,
                            attempt_id=f"bad-{relation}",
                            run_id=f"bad-{relation}-run",
                            phases=("controlled-write",),
                        )
                    self.assertEqual(
                        0,
                        executor.read_provider_ledger(repository.root)["usedTurns"],
                    )

    def test_concurrent_tombstone_consume_and_descendant_scope(self) -> None:
        with goal_repository() as repository:
            candidate = repository.prepare_controlled_candidate("W84-G8")
            repository.add_controlled_preauthorization("W84-G8", candidate)

            def consume(index: int) -> str:
                try:
                    executor.consume_controlled_write_lease(
                        repository.root,
                        gate_id="W84-G8",
                        product_candidate=candidate,
                        lease_id=f"CW-W84-CONCURRENT{index:02d}",
                        nonce=bytes([index + 1]) * 32,
                        issued_at=_past_timestamp(1),
                    )
                    return "ok"
                except executor.ExecutorError as error:
                    return error.code

            with ThreadPoolExecutor(max_workers=2) as pool:
                outcomes = list(pool.map(consume, (0, 1)))
            self.assertEqual(1, outcomes.count("ok"))
            rejected = [item for item in outcomes if item != "ok"]
            self.assertEqual(1, len(rejected))
            self.assertIn(
                rejected[0],
                {"LEASE_ALREADY_CONSUMED", "EXCLUSIVE_PATH_EXISTS"},
            )
            repository.commit_all("controlled tombstone")
            expected, mode, snapshot = executor._gate_checkout_preflight(
                repository.root,
                gate_id="W84-G9",
                product_candidate=candidate,
            )
            self.assertEqual(_git(repository.root, "rev-parse", "HEAD"), expected)
            self.assertEqual("controlled-tombstone-descendant", mode)
            self.assertTrue(snapshot.clean)

    def test_tombstone_precondition_drift_is_rejected(self) -> None:
        with goal_repository() as repository:
            candidate = repository.prepare_controlled_candidate("W84-G8")
            repository.add_controlled_preauthorization("W84-G8", candidate)
            executor.consume_controlled_write_lease(
                repository.root,
                gate_id="W84-G8",
                product_candidate=candidate,
                lease_id="CW-W84-DRIFTCHECK",
                nonce=b"d" * 32,
                issued_at=_past_timestamp(1),
            )
            repository.commit_all("controlled tombstone")
            first = repository.root / _parts(
                "artifacts/week84-renderer-listener-retention/gates/W84-G0.json"
            )
            value = json.loads(first.read_text("utf-8"))
            value["finishedAt"] = _past_timestamp(20)
            repository.write_json(
                "artifacts/week84-renderer-listener-retention/gates/W84-G0.json",
                value,
            )
            with self.assertRaisesRegex(
                executor.ExecutorError,
                "CONTROLLED_PREAUTHORIZATION_PRECONDITION",
            ):
                executor.verify_controlled_write_tombstone(
                    repository.root,
                    gate_id="W84-G8",
                    product_candidate=candidate,
                )

    def test_manual_challenge_descendant_is_exactly_scoped(self) -> None:
        with goal_repository() as repository:
            repository.write_text("candidate.txt", "candidate\n")
            candidate = repository.commit_all("candidate")
            request_id = "UA-W86-EXECUTOR"
            repository.write_json(
                f"{executor.USER_ACCEPTANCE_REQUEST_ROOT}/{request_id}.json",
                {
                    "schemaVersion": executor.SCHEMA_VERSION,
                    "goalId": executor.GOAL_ID,
                    "acceptanceId": "W86-USER-VISUAL",
                    "checkpoint": "W86",
                    "decisionRequestId": request_id,
                    "candidate": candidate,
                    "manifestSha256": "1" * 64,
                    "challengeCode": "CH-EXECUTOR12",
                    "requestedAt": _past_timestamp(1),
                    "status": "AwaitingUser",
                },
            )
            repository.commit_all("manual challenge")
            _head, mode, _snapshot = executor._gate_checkout_preflight(
                repository.root,
                gate_id="W86-R7",
                product_candidate=candidate,
            )
            self.assertEqual("manual-challenge-descendant", mode)
            repository.write_text("unexpected.txt", "unexpected\n")
            repository.commit_all("unapproved descendant")
            with self.assertRaisesRegex(executor.ExecutorError, "CHECKOUT_DESCENDANT_SCOPE"):
                executor._gate_checkout_preflight(
                    repository.root,
                    gate_id="W86-R7",
                    product_candidate=candidate,
                )

    def test_w92_combined_tombstone_and_manual_descendant(self) -> None:
        with goal_repository() as repository:
            candidate = repository.prepare_controlled_candidate("W92-G7")
            repository.switch_main_to_lane(executor.INTEGRATION_BRANCH_REF)
            repository.add_controlled_preauthorization("W92-G7", candidate)
            executor.consume_controlled_write_lease(
                repository.root,
                gate_id="W92-G7",
                product_candidate=candidate,
                lease_id="CW-W92-COMBINED12",
                nonce=b"w" * 32,
                issued_at=_past_timestamp(1),
            )
            repository.commit_all("w92 controlled tombstone")
            request_id = "UA-W92-EXECUTOR"
            repository.write_json(
                f"{executor.USER_ACCEPTANCE_REQUEST_ROOT}/{request_id}.json",
                {
                    "schemaVersion": executor.SCHEMA_VERSION,
                    "goalId": executor.GOAL_ID,
                    "acceptanceId": "W92-USER-VISUAL",
                    "checkpoint": "W92",
                    "decisionRequestId": request_id,
                    "candidate": candidate,
                    "manifestSha256": "2" * 64,
                    "challengeCode": "CH-COMBINED12",
                    "requestedAt": _past_timestamp(1),
                    "status": "AwaitingUser",
                },
            )
            repository.commit_all("w92 manual challenge")
            _head, mode, _snapshot = executor._gate_checkout_preflight(
                repository.root,
                gate_id="W92-G9",
                product_candidate=candidate,
            )
            self.assertEqual("controlled-and-manual-descendant", mode)


class TestRunnerTests(unittest.TestCase):
    def _report(self, basename: str) -> str:
        return (
            "artifacts/week84-renderer-listener-retention/"
            "gate-evidence/W84-G0/"
            f"{executor.TRUSTED_TEST_COMMAND_ID}.{basename}.trusted-test-report.json"
        )

    def _counts(self, discovered: int = 1, passed: int = 1, skipped: int = 0):
        return {
            "discovered": discovered,
            "passed": passed,
            "failed": 0,
            "skipped": skipped,
            "notRun": 0,
            "notApplicable": 0,
        }

    def test_report_binds_policy_checkout_and_streams(self) -> None:
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_test_candidate()
            result = executor.run_test(
                repository.root,
                gate_id=gate,
                product_candidate=candidate,
                command_id=command,
                attempt_id="semantic",
                argv=trusted_argv(),
                redacted_invocation=POLICY_REDACTED_INVOCATION,
                report_path=self._report("semantic"),
                test_counts=self._counts(),
            )
            report_raw = (repository.root / _parts(result.report_path)).read_bytes()
            report = json.loads(report_raw.decode("utf-8"))
            self.assertEqual(
                {
                    "schemaVersion", "runnerId", "runnerSource",
                    "requirementsSource", "gateId", "productCandidate",
                    "commandId", "attemptId", "commandPolicy", "argvSha256",
                    "semanticLoaderDescriptorSha256", "semanticSources",
                    "checkoutIdentity", "runtimeInputTree",
                    "semanticDependencyTree", "toolIdentity", "gitToolIdentity",
                    "executionCapability", "redactedInvocation",
                    "invocationSha256", "startedAt", "finishedAt", "exitCode",
                    "stdout", "stderr", "testCounts", "countsSource",
                },
                set(report),
            )
            self.assertEqual("candidate-exact", report["checkoutIdentity"]["mode"])
            self.assertEqual("", report["checkoutIdentity"]["before"]["gitStatusPorcelainV1"])
            self.assertEqual(candidate, report["checkoutIdentity"]["before"]["headCommit"])
            self.assertEqual("python-unittest-output", report["countsSource"])
            self.assertEqual(hashlib.sha256(report_raw).hexdigest(), result.report_sha256)
            self.assertEqual(
                {"entrySources", "readSources", "bootstrapSources"},
                set(report["semanticSources"]),
            )
            self.assertEqual(
                len(executor.SEMANTIC_BOOTSTRAP_SOURCES),
                len(report["semanticSources"]["bootstrapSources"]),
            )
            runtime = report["runtimeInputTree"]
            self.assertEqual(
                {
                    "mode", "contentRootAlgorithm", "scopedRoots",
                    "excludedPaths", "preGoalStateSnapshot",
                    "postSemanticOutputs", "before", "after", "matchesBefore",
                },
                set(runtime),
            )
            self.assertEqual(executor.ARTIFACT_INPUT_TREE_MODE, runtime["mode"])
            self.assertTrue(runtime["matchesBefore"])
            self.assertEqual(runtime["before"], runtime["after"])
            self.assertEqual(6, len(runtime["excludedPaths"]))
            self.assertNotIn(executor.GOAL_STATE_PATH, runtime["excludedPaths"])
            self.assertEqual(
                list(executor.W84_G0_POST_SEMANTIC_OUTPUTS),
                [item["path"] for item in runtime["postSemanticOutputs"]],
            )
            self.assertTrue(
                all(
                    item["mode"] == "exclusive-create-after-semantic"
                    and item["before"]
                    == {"exists": False, "bytes": None, "sha256": None}
                    and item["stableDuringChild"] is True
                    for item in runtime["postSemanticOutputs"]
                )
            )
            snapshot = runtime["preGoalStateSnapshot"]
            snapshot_raw = (
                repository.root / _parts(snapshot["path"])
            ).read_bytes()
            self.assertEqual(
                (repository.root / _parts(executor.GOAL_STATE_PATH)).read_bytes(),
                snapshot_raw,
            )
            self.assertEqual(hashlib.sha256(snapshot_raw).hexdigest(), snapshot["sha256"])
            self.assertTrue(snapshot["stableDuringChild"])
            repository.write_json(
                executor.GOAL_STATE_PATH,
                {"fixture": "post-semantic", "gate": "W84-G0"},
            )
            projected_roots, projected_summary = (
                executor._artifact_input_tree_inventory(
                    repository.root,
                    excluded_paths=runtime["excludedPaths"],
                    content_overrides={executor.GOAL_STATE_PATH: snapshot_raw},
                )
            )
            self.assertEqual(runtime["scopedRoots"], projected_roots)
            self.assertEqual(runtime["before"], projected_summary)
            self.assertTrue(report["gitToolIdentity"]["matchesBefore"])
            self.assertEqual(
                "external-pinned-interpreter-root",
                report["toolIdentity"]["runtimeTrustMode"],
            )
            self.assertIn(
                "Ran 1 test",
                (repository.root / _parts(result.stdout_path)).read_text("utf-8"),
            )

    def test_w85_lane_semantic_evidence_uses_unique_control_worktree(self) -> None:
        cases = (
            (
                "W85-R0",
                executor.RENDERER_BRANCH_REF,
                "artifacts/week85-renderer-feature-boundaries",
            ),
            (
                "W85-C0",
                executor.CLI_BRANCH_REF,
                "artifacts/week85-cli-composition",
            ),
        )
        for gate_id, branch_ref, artifact_root in cases:
            with self.subTest(gate=gate_id), goal_repository() as repository:
                gate, command, candidate = repository.prepare_test_candidate(
                    gate_id=gate_id
                )
                control_root = repository.switch_main_to_lane(branch_ref)
                report_path = (
                    f"{artifact_root}/gate-evidence/{gate_id}/"
                    f"{command}.lane.trusted-test-report.json"
                )

                # Candidate-local ignored Goal evidence cannot stand in for the
                # unique trusted control worktree.
                self.assertTrue(
                    (repository.root / _parts(executor.GOAL_STATE_PATH)).is_file()
                )
                with self.assertRaisesRegex(
                    executor.ExecutorError, "MISSING_PATH"
                ):
                    executor.run_test(
                        repository.root,
                        gate_id=gate,
                        product_candidate=candidate,
                        command_id=command,
                        attempt_id="lane",
                        argv=trusted_argv(),
                        redacted_invocation=POLICY_REDACTED_INVOCATION,
                        report_path=report_path,
                        test_counts=self._counts(),
                    )

                goal_raw = (
                    repository.root / _parts(executor.GOAL_STATE_PATH)
                ).read_bytes()
                control_goal = control_root / _parts(executor.GOAL_STATE_PATH)
                control_goal.parent.mkdir(parents=True, exist_ok=True)
                control_goal.write_bytes(goal_raw)
                result = executor.run_test(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="lane",
                    argv=trusted_argv(),
                    redacted_invocation=POLICY_REDACTED_INVOCATION,
                    report_path=report_path,
                    test_counts=self._counts(),
                )

                self.assertEqual(0, result.exit_code)
                self.assertFalse((repository.root / _parts(report_path)).exists())
                self.assertTrue((control_root / _parts(report_path)).is_file())
                self.assertTrue(
                    (control_root / _parts(result.stdout_path)).is_file()
                )
                self.assertFalse(
                    (repository.root / _parts(result.stdout_path)).exists()
                )

    def test_arbitrary_command_redaction_and_caller_counts_fail_closed(self) -> None:
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_test_candidate()
            with self.assertRaisesRegex(executor.ExecutorError, "COMMAND_POLICY_COMMAND"):
                executor.run_test(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="argv",
                    argv=(sys.executable, "-c", "print('forged')"),
                    redacted_invocation=POLICY_REDACTED_INVOCATION,
                    report_path=self._report("argv"),
                    test_counts=self._counts(),
                )
            with self.assertRaisesRegex(executor.ExecutorError, "COMMAND_POLICY_REDACTION"):
                executor.run_test(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="redaction",
                    argv=trusted_argv(),
                    redacted_invocation="python caller-controlled",
                    report_path=self._report("redaction"),
                    test_counts=self._counts(),
                )
            with self.assertRaisesRegex(executor.ExecutorError, "TEST_COUNTS_MISMATCH"):
                executor.run_test(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="counts",
                    argv=trusted_argv(),
                    redacted_invocation=POLICY_REDACTED_INVOCATION,
                    report_path=self._report("counts"),
                    test_counts=self._counts(discovered=2, passed=2),
                )

    def test_goal_state_and_create_only_outputs_are_stable_during_child(self) -> None:
        fake_stdout = (
            b".\n----------------------------------------------------------------------\n"
            b"Ran 1 test in 0.001s\n\nOK\n"
        )
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_test_candidate()

            def mutate_goal_state(*_args: object, **_kwargs: object):
                repository.write_json(executor.GOAL_STATE_PATH, {"mutated": True})
                return 0, fake_stdout, b"", False

            with mock.patch.object(
                executor, "_bounded_child_capture", side_effect=mutate_goal_state
            ), mock.patch.object(
                executor,
                "_extract_semantic_site_marker",
                side_effect=lambda raw: (raw, ()),
            ):
                with self.assertRaisesRegex(executor.ExecutorError, "GOAL_STATE_DRIFT"):
                    executor.run_test(
                        repository.root,
                        gate_id=gate,
                        product_candidate=candidate,
                        command_id=command,
                        attempt_id="goal-state-drift",
                        argv=trusted_argv(),
                        redacted_invocation=POLICY_REDACTED_INVOCATION,
                        report_path=self._report("goal-state-drift"),
                        test_counts=self._counts(),
                    )

        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_test_candidate()

            def occupy_summary(*_args: object, **_kwargs: object):
                repository.write_json(
                    executor.W84_G0_POST_SEMANTIC_OUTPUTS[0], {"forged": True}
                )
                return 0, fake_stdout, b"", False

            with mock.patch.object(
                executor, "_bounded_child_capture", side_effect=occupy_summary
            ), mock.patch.object(
                executor,
                "_extract_semantic_site_marker",
                side_effect=lambda raw: (raw, ()),
            ):
                with self.assertRaisesRegex(
                    executor.ExecutorError, "POST_SEMANTIC_OUTPUT_DRIFT"
                ):
                    executor.run_test(
                        repository.root,
                        gate_id=gate,
                        product_candidate=candidate,
                        command_id=command,
                        attempt_id="summary-preclaim",
                        argv=trusted_argv(),
                        redacted_invocation=POLICY_REDACTED_INVOCATION,
                        report_path=self._report("summary-preclaim"),
                        test_counts=self._counts(),
                    )

    def test_nonexcluded_goal_artifact_mutation_is_detected(self) -> None:
        fake_stdout = (
            b".\n----------------------------------------------------------------------\n"
            b"Ran 1 test in 0.001s\n\nOK\n"
        )
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_test_candidate()

            def mutate_sibling(*_args: object, **_kwargs: object):
                repository.write_json(
                    "artifacts/week84-renderer-listener-retention/"
                    "gate-evidence/W84-G0/not-an-authorized-post-output.json",
                    {"forged": True},
                )
                return 0, fake_stdout, b"", False

            with mock.patch.object(
                executor, "_bounded_child_capture", side_effect=mutate_sibling
            ), mock.patch.object(
                executor,
                "_extract_semantic_site_marker",
                side_effect=lambda raw: (raw, ()),
            ):
                with self.assertRaisesRegex(
                    executor.ExecutorError, "RUNTIME_INPUT_TREE_DRIFT"
                ):
                    executor.run_test(
                        repository.root,
                        gate_id=gate,
                        product_candidate=candidate,
                        command_id=command,
                        attempt_id="artifact-sibling",
                        argv=trusted_argv(),
                        redacted_invocation=POLICY_REDACTED_INVOCATION,
                        report_path=self._report("artifact-sibling"),
                        test_counts=self._counts(),
                    )

    def test_secret_capture_is_replaced(self) -> None:
        code = "print('s' + 'k-' + 'must-never-persist')\n" + DEFAULT_POLICY_CODE
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_test_candidate(code=code)
            report_path = self._report("secret")
            with self.assertRaisesRegex(executor.ExecutorError, "TEST_CAPTURE_REJECTED"):
                executor.run_test(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="secret",
                    argv=trusted_argv(code),
                    redacted_invocation=POLICY_REDACTED_INVOCATION,
                    report_path=report_path,
                    test_counts=self._counts(),
                )
            for path in (repository.root / "artifacts").rglob("*"):
                if path.is_file():
                    self.assertNotIn(b"sk-must-never-persist", path.read_bytes())

    def test_child_cannot_overwrite_precreated_evidence_path(self) -> None:
        report_path = self._report("replace")
        replacement_code = (
            "from pathlib import Path\n"
            f"Path({report_path!r}).write_bytes(b'forged')\n"
            + DEFAULT_POLICY_CODE
        )
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_test_candidate(
                code=replacement_code
            )
            with self.assertRaisesRegex(executor.ExecutorError, "EVIDENCE_PATH_RACE"):
                executor.run_test(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="replace",
                    argv=trusted_argv(replacement_code),
                    redacted_invocation=POLICY_REDACTED_INVOCATION,
                    report_path=report_path,
                    test_counts=self._counts(),
                )

    def test_manifest_and_runner_post_bootstrap_drift_have_no_report_side_effect(self) -> None:
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_test_candidate()
            manifest = json.loads(
                (repository.root / _parts(executor.GATE_REQUIREMENTS_PATH)).read_text(
                    "utf-8"
                )
            )
            manifest["unexpected"] = True
            repository.write_json(executor.GATE_REQUIREMENTS_PATH, manifest)
            with self.assertRaisesRegex(executor.ExecutorError, "BOOTSTRAP_SOURCE_DIRTY"):
                executor.run_test(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="manifest-drift",
                    argv=trusted_argv(),
                    redacted_invocation=POLICY_REDACTED_INVOCATION,
                    report_path=self._report("manifest-drift"),
                    test_counts=self._counts(),
                )
            self.assertFalse(
                (repository.root / _parts(self._report("manifest-drift"))).exists()
            )

        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_test_candidate()
            repository.write_text(executor.RUNNER_SOURCE_PATH, "raise SystemExit(0)\n")
            with self.assertRaisesRegex(executor.ExecutorError, "RUNNER_SOURCE"):
                executor.run_test(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="runner-drift",
                    argv=trusted_argv(),
                    redacted_invocation=POLICY_REDACTED_INVOCATION,
                    report_path=self._report("runner-drift"),
                    test_counts=self._counts(),
                )
            self.assertFalse(
                (repository.root / _parts(self._report("runner-drift"))).exists()
            )

    def test_checkout_head_dirty_and_mid_run_drift_fail_closed(self) -> None:
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_test_candidate()
            repository.write_text("later.txt", "later\n")
            repository.commit_all("later")
            with self.assertRaisesRegex(executor.ExecutorError, "CHECKOUT_HEAD"):
                executor.run_test(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="wrong-head",
                    argv=trusted_argv(),
                    redacted_invocation=POLICY_REDACTED_INVOCATION,
                    report_path=self._report("wrong-head"),
                    test_counts=self._counts(),
                )

        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_test_candidate()
            repository.write_text("candidate.txt", "dirty\n")
            with self.assertRaisesRegex(executor.ExecutorError, "CHECKOUT_DIRTY"):
                executor.run_test(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="dirty",
                    argv=trusted_argv(),
                    redacted_invocation=POLICY_REDACTED_INVOCATION,
                    report_path=self._report("dirty"),
                    test_counts=self._counts(),
                )

        drift_code = (
            "from pathlib import Path\n"
            "Path('candidate.txt').write_text('drifted\\n')\n"
            + DEFAULT_POLICY_CODE
        )
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_test_candidate(code=drift_code)
            with self.assertRaisesRegex(executor.ExecutorError, "CHECKOUT_DRIFT"):
                executor.run_test(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="mid-drift",
                    argv=trusted_argv(drift_code),
                    redacted_invocation=POLICY_REDACTED_INVOCATION,
                    report_path=self._report("mid-drift"),
                    test_counts=self._counts(),
                )

    def test_report_must_be_gate_local(self) -> None:
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_test_candidate()
            with self.assertRaisesRegex(executor.ExecutorError, "REPORT_PATH"):
                executor.run_test(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="wrong-path",
                    argv=trusted_argv(),
                    redacted_invocation=POLICY_REDACTED_INVOCATION,
                    report_path="artifacts/week84-renderer-listener-retention/root.json",
                    test_counts=self._counts(),
                )


class ProductCommandTests(unittest.TestCase):
    def _report(self, attempt: str = "product", command: str = "product-smoke") -> str:
        return (
            "artifacts/week84-renderer-listener-retention/"
            f"gate-evidence/W84-G1/{command}.{attempt}.adapter-observation-report.json"
        )

    def _selection(
        self, attempt: str = "product", command: str = "product-smoke"
    ) -> str:
        return (
            "artifacts/week84-renderer-listener-retention/"
            f"gate-evidence/W84-G1/{command}.{attempt}.selection.json"
        )

    def _execution(
        self, attempt: str = "product", command: str = "product-smoke"
    ) -> str:
        return (
            "artifacts/week84-renderer-listener-retention/"
            f"gate-evidence/W84-G1/{command}.{attempt}.execution.json"
        )

    def test_product_command_has_no_argv_and_binds_derived_counts(self) -> None:
        self.assertNotIn("argv", inspect.signature(executor.run_product_command).parameters)
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_product_candidate()
            result = executor.run_product_command(
                repository.root,
                gate_id=gate,
                product_candidate=candidate,
                command_id=command,
                attempt_id="product",
                report_path=self._report(),
                selection_path=self._selection(),
                execution_path=self._execution(),
            )
            report = json.loads((repository.root / _parts(result.report_path)).read_text("utf-8"))
            self.assertEqual(
                {"discovered": 0, "failed": 0, "passed": 0, "skipped": 0},
                report["counts"],
            )
            self.assertEqual(executor.ADAPTER_COUNTS_SOURCE, report["countsSource"])
            self.assertIsNone(report["resultProtocol"])
            self.assertEqual(1, len(result.verifier_report_paths))
            self.assertEqual("candidate-exact", report["checkoutIdentity"]["mode"])
            self.assertEqual(
                f"{executor.TRUSTED_PRODUCT_SCRIPT_ROOT}/{command}.py",
                report["scriptSource"]["path"],
            )
            self.assertTrue(report["gitToolIdentity"]["matchesBefore"])
            selection = json.loads(
                (repository.root / _parts(result.selection_path)).read_text("utf-8")
            )
            execution = json.loads(
                (repository.root / _parts(result.execution_path)).read_text("utf-8")
            )
            verifier = json.loads(
                (
                    repository.root / _parts(result.verifier_report_paths[0])
                ).read_text("utf-8")
            )
            self.assertTrue(selection["gitToolIdentity"]["matchesBefore"])
            self.assertTrue(execution["gitToolIdentity"]["matchesBefore"])
            self.assertTrue(verifier["gitToolIdentity"]["matchesBefore"])
            self.assertEqual(
                result.selection_sha256,
                hashlib.sha256(
                    (repository.root / _parts(result.selection_path)).read_bytes()
                ).hexdigest(),
            )
            self.assertEqual(
                result.execution_sha256,
                hashlib.sha256(
                    (repository.root / _parts(result.execution_path)).read_bytes()
                ).hexdigest(),
            )

    def test_unmanifested_untracked_and_missing_result_reject(self) -> None:
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_product_candidate()
            with self.assertRaisesRegex(executor.ExecutorError, "COMMAND_NOT_IN_REQUIREMENTS"):
                executor.run_product_command(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id="not-authorized",
                    attempt_id="unmanifested",
                    report_path=self._report("unmanifested", "not-authorized"),
                    selection_path=self._selection("unmanifested", "not-authorized"),
                    execution_path=self._execution("unmanifested", "not-authorized"),
                )
            repository.write_text(
                f"{executor.TRUSTED_PRODUCT_SCRIPT_ROOT}/untracked-command.py",
                f"print({_product_result_line()!r})\n",
            )
            with self.assertRaisesRegex(executor.ExecutorError, "CHECKOUT_DIRTY"):
                executor.run_product_command(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="untracked",
                    report_path=self._report("untracked", command),
                    selection_path=self._selection("untracked", command),
                    execution_path=self._execution("untracked", command),
                )

    def test_product_report_cannot_impersonate_planned_evidence(self) -> None:
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_product_candidate()
            with self.assertRaisesRegex(executor.ExecutorError, "REPORT_PATH"):
                executor.run_product_command(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="collision",
                    report_path=(
                        "artifacts/week84-renderer-listener-retention/"
                        "gate-evidence/W84-G1/commands.json"
                    ),
                    selection_path=self._selection("collision"),
                    execution_path=self._execution("collision"),
                )

    def test_product_mid_run_drift_and_noncanonical_result_reject(self) -> None:
        drift_script = (
            "from pathlib import Path\n"
            "Path('candidate' + '.txt').write_text('drifted\\n')\n"
            "print('adapter attempted write')\n"
        )
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_product_candidate(script=drift_script)
            with self.assertRaisesRegex(executor.ExecutorError, "ADAPTER_EXIT"):
                executor.run_product_command(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="drift",
                    report_path=self._report("drift"),
                    selection_path=self._selection("drift"),
                    execution_path=self._execution("drift"),
                )
            self.assertEqual(
                "candidate\n", (repository.root / "candidate.txt").read_text("utf-8")
            )

        payload = {
            "protocol": executor.PRODUCT_COMMAND_RESULT_PROTOCOL,
            "status": "Passed",
            "counts": {"discovered": 0, "passed": 0, "failed": 0, "skipped": 0},
        }
        noncanonical = json.dumps(payload, indent=2)
        script = (
            "prefix = 'CAICLI_'\n"
            "suffix = 'PRODUCT_COMMAND_RESULT='\n"
            f"print(prefix + suffix + {noncanonical!r})\n"
        )
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_product_candidate(script=script)
            with self.assertRaisesRegex(executor.ExecutorError, "ADAPTER_SELF_ATTESTATION"):
                executor.run_product_command(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="noncanonical",
                    report_path=self._report("noncanonical"),
                    selection_path=self._selection("noncanonical"),
                    execution_path=self._execution("noncanonical"),
                )

    def test_product_cli_rejects_remainder(self) -> None:
        result = subprocess.run(
            [
                sys.executable,
                str(Path(executor.__file__)),
                "run-product-command",
                "--gate-id",
                "W84-G1",
                "--product-candidate",
                "0" * 40,
                "--command-id",
                "product-smoke",
                "--attempt-id",
                "cli-attempt",
                "--report-path",
                self._report(),
                "--",
                "python",
                "-c",
                "print('arbitrary')",
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            shell=False,
            check=False,
        )
        self.assertEqual(2, result.returncode)

    def test_invalid_control_origin_dependency_and_missing_verifier_are_prechild(self) -> None:
        cases = (
            ("invalid-origin", "COMMAND_SOURCE_ORIGIN", "print('adapter')\n", None),
            ("missing-verifier", "COMMAND_ROLE_POLICY", "print('adapter')\n", None),
            (
                None,
                "COMMAND_SOURCE_DEPENDENCY",
                "import tools.undeclared_product_helper\n",
                "tools/undeclared_product_helper.py",
            ),
            (
                None,
                "COMMAND_SOURCE_DEPENDENCY",
                "from pathlib import Path\nPath('candidate.txt').read_text()\n",
                None,
            ),
        )
        for ordinal, (source_error, code, script, candidate_helper) in enumerate(cases):
            with self.subTest(code=code, ordinal=ordinal), goal_repository() as repository:
                gate, command, candidate = repository.prepare_product_candidate(
                    script=script, source_error=source_error
                )
                if candidate_helper is not None:
                    repository.write_text(candidate_helper, "VALUE = 1\n")
                    candidate = repository.commit_all("undeclared candidate helper")
                attempt = f"prechild-{ordinal}"
                with mock.patch.object(executor, "_bounded_child_capture") as child:
                    with self.assertRaisesRegex(executor.ExecutorError, code):
                        executor.run_product_command(
                            repository.root,
                            gate_id=gate,
                            product_candidate=candidate,
                            command_id=command,
                            attempt_id=attempt,
                            report_path=self._report(attempt),
                            selection_path=self._selection(attempt),
                            execution_path=self._execution(attempt),
                        )
                child.assert_not_called()

    def test_verifier_rejects_tampered_adapter_and_control_bindings(self) -> None:
        original = executor._bounded_child_capture
        mutations = (
            lambda value: value["adapterReports"][0].__setitem__("sha256", "0" * 64),
            lambda value: value["adapterReports"][0].__setitem__("commandId", "wrong-command"),
            lambda value: value.__setitem__("productCandidate", "0" * 40),
            lambda value: value["commandControlBinding"].__setitem__("sha256", "0" * 64),
        )
        for ordinal, mutate in enumerate(mutations):
            with self.subTest(ordinal=ordinal), goal_repository() as repository:
                gate, command, candidate = repository.prepare_product_candidate()
                calls = 0

                def capture(command_value, **kwargs):
                    nonlocal calls
                    calls += 1
                    if calls == 2:
                        descriptor = json.loads(kwargs["stdin_bytes"].decode("utf-8"))
                        mutate(descriptor)
                        kwargs["stdin_bytes"] = executor.canonical_json_bytes(descriptor)
                    return original(command_value, **kwargs)

                attempt = f"tamper-{ordinal}"
                with mock.patch.object(
                    executor, "_bounded_child_capture", side_effect=capture
                ):
                    with self.assertRaisesRegex(
                        executor.ExecutorError, "VERIFIER_RESULT_MISSING"
                    ):
                        executor.run_product_command(
                            repository.root,
                            gate_id=gate,
                            product_candidate=candidate,
                            command_id=command,
                            attempt_id=attempt,
                            report_path=self._report(attempt),
                            selection_path=self._selection(attempt),
                            execution_path=self._execution(attempt),
                        )

    def test_selection_execution_mismatch_is_reconciled_failed(self) -> None:
        original = executor._bounded_child_capture
        calls = 0

        def capture(command_value, **kwargs):
            nonlocal calls
            calls += 1
            if calls == 2:
                payload = {
                    "protocol": executor.COMMAND_VERIFIER_RESULT_PROTOCOL,
                    "status": "Passed",
                    "executedCases": [{"caseId": "wrong.case", "status": "Passed"}],
                    "counts": {
                        "discovered": 1,
                        "passed": 1,
                        "failed": 0,
                        "skipped": 0,
                    },
                }
                stdout = (
                    "Ran 1 test in 0.001s\n\nOK\n"
                    + executor.COMMAND_VERIFIER_RESULT_MARKER
                    + executor.canonical_json_bytes(payload).decode("utf-8")
                    + "\n"
                ).encode("utf-8")
                return 0, stdout, b"", False
            return original(command_value, **kwargs)

        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_product_candidate()
            with mock.patch.object(
                executor, "_bounded_child_capture", side_effect=capture
            ):
                with self.assertRaisesRegex(executor.ExecutorError, "VERIFIER_FAILED"):
                    executor.run_product_command(
                        repository.root,
                        gate_id=gate,
                        product_candidate=candidate,
                        command_id=command,
                        attempt_id="mismatch",
                        report_path=self._report("mismatch"),
                        selection_path=self._selection("mismatch"),
                        execution_path=self._execution("mismatch"),
                    )
            execution = json.loads(
                (repository.root / _parts(self._execution("mismatch"))).read_text("utf-8")
            )
            self.assertEqual(1, execution["exitCode"])

    def test_isolated_adapter_ignores_sitecustomize_and_pythonpath(self) -> None:
        with goal_repository() as repository:
            gate, command, _candidate = repository.prepare_product_candidate()
            marker = repository.root / "sitecustomize-ran.txt"
            repository.write_text(
                "sitecustomize.py",
                "from pathlib import Path\nPath('sitecustomize-ran.txt').write_text('ran')\n",
            )
            candidate = repository.commit_all("candidate sitecustomize trap")
            with mock.patch.dict(
                os.environ,
                {"PYTHONPATH": str(repository.root)},
                clear=False,
            ):
                executor.run_product_command(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="site-isolation",
                    report_path=self._report("site-isolation"),
                    selection_path=self._selection("site-isolation"),
                    execution_path=self._execution("site-isolation"),
                )
            self.assertFalse(marker.exists())

    def test_runtime_input_mutation_is_detected_after_adapter(self) -> None:
        script = (
            "import os\n"
            "path = 'artifacts/' + 'runtime-input.json'\n"
            "descriptor = os.open(path, os.O_WRONLY | os.O_TRUNC)\n"
            "os.write(descriptor, b'changed\\n')\n"
            "os.close(descriptor)\n"
        )
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_product_candidate(script=script)
            runtime_path = "artifacts/runtime-input.json"
            repository.write_text(runtime_path, "stable\n")
            original = (repository.root / _parts(runtime_path)).read_bytes()
            binding = {
                "path": runtime_path,
                "sha256": hashlib.sha256(original).hexdigest(),
                "kind": "gate-evidence",
            }
            with mock.patch.object(
                executor, "_runtime_input_bindings", return_value=[binding]
            ):
                with self.assertRaisesRegex(
                    executor.ExecutorError, "RUNTIME_INPUT_DRIFT"
                ):
                    executor.run_product_command(
                        repository.root,
                        gate_id=gate,
                        product_candidate=candidate,
                        command_id=command,
                        attempt_id="runtime-drift",
                        report_path=self._report("runtime-drift"),
                        selection_path=self._selection("runtime-drift"),
                        execution_path=self._execution("runtime-drift"),
                    )

    def test_timeout_kills_goal_owned_descendant_process(self) -> None:
        marker = (
            "artifacts/week84-renderer-listener-retention/"
            "timeout-descendant-ran.txt"
        )
        descendant = (
            "import time; from pathlib import Path; "
            f"time.sleep(1.5); Path({marker!r}).write_text('ran')"
        )
        script = (
            "import subprocess, sys, time\n"
            f"subprocess.Popen([sys.executable, '-B', '-c', {descendant!r}])\n"
            "time.sleep(10)\n"
        )
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_product_candidate(script=script)
            with self.assertRaisesRegex(executor.ExecutorError, "PRODUCT_CAPTURE_REJECTED"):
                executor.run_product_command(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="timeout-tree",
                    report_path=self._report("timeout-tree"),
                    selection_path=self._selection("timeout-tree"),
                    execution_path=self._execution("timeout-tree"),
                    timeout_seconds=0.3,
                )
            time.sleep(2.0)
            self.assertFalse((repository.root / _parts(marker)).exists())

    @unittest.skipUnless(os.name == "nt", "Windows Job containment regression")
    def test_immediate_detached_descendant_cannot_escape_job(self) -> None:
        marker = (
            "artifacts/week84-renderer-listener-retention/"
            "immediate-detached-descendant-ran.txt"
        )
        descendant = (
            "import time; from pathlib import Path; "
            f"time.sleep(1.0); Path({marker!r}).write_text('ran')"
        )
        script = (
            "import subprocess, sys\n"
            "flags = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP\n"
            f"subprocess.Popen([sys.executable, '-B', '-c', {descendant!r}], "
            "creationflags=flags, close_fds=True)\n"
            "print('adapter detached descendant observed')\n"
        )
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_product_candidate(script=script)
            executor.run_product_command(
                repository.root,
                gate_id=gate,
                product_candidate=candidate,
                command_id=command,
                attempt_id="detached",
                report_path=self._report("detached"),
                selection_path=self._selection("detached"),
                execution_path=self._execution("detached"),
            )
            time.sleep(1.5)
            self.assertFalse((repository.root / _parts(marker)).exists())


class ExecutionBoundaryTests(unittest.TestCase):
    def test_attach_failure_releases_no_candidate_code(self) -> None:
        with goal_repository() as repository:
            marker = repository.root / "candidate-ran.txt"
            command = (
                sys.executable,
                "-B",
                "-c",
                f"from pathlib import Path; Path({str(marker)!r}).write_text('ran')",
            )
            with mock.patch.object(
                executor,
                "_attach_windows_job",
                side_effect=executor.ExecutorError("CHILD_CONTAINMENT"),
            ):
                result = executor._run_child_no_capture(
                    command,
                    cwd=repository.root,
                    environment=executor._test_child_environment(),
                    timeout_seconds=5,
                )
            self.assertEqual(127, result)
            self.assertFalse(marker.exists())

    def test_git_ambient_repository_and_index_overrides_are_ignored(self) -> None:
        with goal_repository() as repository, goal_repository() as attacker:
            gate, command, candidate = repository.prepare_test_candidate()
            attacker.write_text("attacker.txt", "attacker\n")
            attacker.commit_all("attacker")
            with mock.patch.dict(
                os.environ,
                {
                    "GIT_DIR": str(attacker.root / ".git"),
                    "GIT_WORK_TREE": str(attacker.root),
                    "GIT_INDEX_FILE": str(attacker.root / ".git" / "index"),
                    "GIT_OBJECT_DIRECTORY": str(attacker.root / ".git" / "objects"),
                },
                clear=False,
            ):
                result = executor.run_test(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="git-env",
                    argv=trusted_argv(),
                    redacted_invocation=POLICY_REDACTED_INVOCATION,
                    report_path=(
                        "artifacts/week84-renderer-listener-retention/"
                        "gate-evidence/W84-G0/"
                        f"{command}.git-env.trusted-test-report.json"
                    ),
                    test_counts={
                        "discovered": 1,
                        "passed": 1,
                        "failed": 0,
                        "skipped": 0,
                        "notRun": 0,
                        "notApplicable": 0,
                    },
                )
            self.assertEqual(0, result.exit_code)

    def test_test_helper_and_executor_ignore_fake_path_git(self) -> None:
        with goal_repository() as repository, tempfile.TemporaryDirectory() as fake:
            repository.commit_all("initial")
            fake_root = Path(fake)
            marker = fake_root / "ambient-git-ran.txt"
            (fake_root / "git.cmd").write_text(
                f"@echo forged>{marker}\r\n@exit /b 99\r\n",
                encoding="utf-8",
            )
            with mock.patch.dict(os.environ, {"PATH": str(fake_root)}, clear=False):
                head = _git(repository.root, "rev-parse", "HEAD")
                self.assertEqual(40, len(head))
                self.assertEqual(
                    repository.root,
                    executor._normalise_repo_root(repository.root),
                )
                git_executable, _identity = executor._trusted_git_identity(
                    repository.root
                )
                self.assertEqual(
                    str(git_executable.parent),
                    executor._test_child_environment(repository.root)["PATH"],
                )
            self.assertFalse(marker.exists())

    def test_trusted_git_rejects_execution_and_partial_clone_config(self) -> None:
        unsafe = (
            ("filter.attack.process", "cmd /c exit 0"),
            ("diff.attack.command", "cmd /c exit 0"),
            ("diff.external", "cmd /c exit 0"),
            ("core.fsmonitor", "true"),
            ("core.untrackedCache", "true"),
            ("protocol.file.allow", "always"),
            ("extensions.partialClone", "origin"),
            ("remote.origin.promisor", "true"),
            ("remote.origin.partialCloneFilter", "blob:none"),
        )
        for key, value in unsafe:
            with self.subTest(key=key), goal_repository() as repository:
                repository.commit_all("initial")
                _git(repository.root, "config", key, value)
                with self.assertRaisesRegex(
                    executor.ExecutorError, "GIT_CONFIG_POLICY"
                ):
                    executor._normalise_repo_root(repository.root)

    def test_tracked_and_ancestor_gitattributes_execution_are_rejected(self) -> None:
        with goal_repository() as repository:
            repository.write_text(".gitattributes", "*.txt filter=attack\n")
            repository.write_text("sample.txt", "sample\n")
            repository.commit_all("unsafe tracked attributes")
            with self.assertRaisesRegex(
                executor.ExecutorError, "GIT_ATTRIBUTES_POLICY"
            ):
                executor._normalise_repo_root(repository.root)

        with goal_repository() as repository:
            repository.write_text("src/sample.txt", "sample\n")
            repository.commit_all("tracked descendant")
            repository.write_text("src/.gitattributes", "*.txt diff=external\n")
            with self.assertRaisesRegex(
                executor.ExecutorError, "GIT_ATTRIBUTES_POLICY"
            ):
                executor._normalise_repo_root(repository.root)

    def test_submodule_active_is_neutralized_and_gitlinks_are_rejected(self) -> None:
        with goal_repository() as repository:
            repository.write_text("sample.txt", "sample\n")
            head = repository.commit_all("initial")
            _git(repository.root, "config", "submodule.active", ".")
            self.assertEqual(
                repository.root, executor._normalise_repo_root(repository.root)
            )
            command = executor._git_command(repository.root, ["status", "--short"])
            self.assertIn("submodule.active=", command)
            _git(
                repository.root,
                "update-index",
                "--add",
                "--cacheinfo",
                f"160000,{head},vendor/sub",
            )
            _git(repository.root, "commit", "--quiet", "-m", "gitlink")
            with self.assertRaisesRegex(
                executor.ExecutorError, "GIT_SUBMODULE_UNSUPPORTED"
            ):
                executor._normalise_repo_root(repository.root)

    def test_replace_refs_and_shallow_metadata_are_rejected(self) -> None:
        with goal_repository() as repository:
            repository.write_text("one.txt", "one\n")
            first = repository.commit_all("one")
            repository.write_text("two.txt", "two\n")
            second = repository.commit_all("two")
            _git(repository.root, "replace", first, second)
            with self.assertRaisesRegex(executor.ExecutorError, "GIT_REPOSITORY_TRUST"):
                executor.read_provider_ledger(repository.root)

    def test_lane_branch_routes_product_evidence_to_unique_control_worktree(self) -> None:
        report_path = (
            "artifacts/week84-renderer-listener-retention/"
            "gate-evidence/W85-R0/product-smoke.lane.adapter-observation-report.json"
        )
        selection_path = (
            "artifacts/week84-renderer-listener-retention/"
            "gate-evidence/W85-R0/product-smoke.lane.selection.json"
        )
        execution_path = (
            "artifacts/week84-renderer-listener-retention/"
            "gate-evidence/W85-R0/product-smoke.lane.execution.json"
        )
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_product_candidate(
                gate_id="W85-R0"
            )
            control_root = repository.switch_main_to_lane(
                executor.RENDERER_BRANCH_REF
            )
            result = executor.run_product_command(
                repository.root,
                gate_id=gate,
                product_candidate=candidate,
                command_id=command,
                attempt_id="lane",
                report_path=report_path,
                selection_path=selection_path,
                execution_path=execution_path,
            )
            self.assertEqual(0, result.exit_code)
            self.assertFalse((repository.root / _parts(report_path)).exists())
            self.assertTrue((control_root / _parts(report_path)).is_file())
            ledger = executor.recover_open_provider_attempts(repository.root)
            self.assertEqual(0, ledger["usedTurns"])
            for relative in (
                executor.PROVIDER_LEDGER_PATH,
                executor.PROVIDER_RUNTIME_JOURNAL_PATH,
            ):
                self.assertFalse((repository.root / _parts(relative)).exists())
                self.assertTrue((control_root / _parts(relative)).is_file())

    def test_gate_lane_rejects_control_branch_for_renderer_gate(self) -> None:
        report_path = (
            "artifacts/week84-renderer-listener-retention/"
            "gate-evidence/W85-R0/product-smoke.wrong-lane.adapter-observation-report.json"
        )
        with goal_repository() as repository:
            gate, command, candidate = repository.prepare_product_candidate(
                gate_id="W85-R0"
            )
            with self.assertRaisesRegex(executor.ExecutorError, "GATE_LANE_BRANCH"):
                executor.run_product_command(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="wrong-lane",
                    report_path=report_path,
                    selection_path=(
                        "artifacts/week84-renderer-listener-retention/"
                        "gate-evidence/W85-R0/product-smoke.wrong-lane.selection.json"
                    ),
                    execution_path=(
                        "artifacts/week84-renderer-listener-retention/"
                        "gate-evidence/W85-R0/product-smoke.wrong-lane.execution.json"
                    ),
                )
            self.assertFalse((repository.root / _parts(report_path)).exists())

    def test_cli_and_integration_gate_branches_are_manifest_routed(self) -> None:
        for gate_id, branch_ref in (
            ("W85-C0", executor.CLI_BRANCH_REF),
            ("W92-G0", executor.INTEGRATION_BRANCH_REF),
        ):
            with self.subTest(gate=gate_id), goal_repository() as repository:
                gate, command, candidate = repository.prepare_product_candidate(
                    gate_id=gate_id
                )
                control_root = repository.switch_main_to_lane(branch_ref)
                report_path = (
                    "artifacts/week84-renderer-listener-retention/"
                    f"gate-evidence/{gate_id}/"
                    f"{command}.lane.adapter-observation-report.json"
                )
                executor.run_product_command(
                    repository.root,
                    gate_id=gate,
                    product_candidate=candidate,
                    command_id=command,
                    attempt_id="lane",
                    report_path=report_path,
                    selection_path=(
                        "artifacts/week84-renderer-listener-retention/"
                        f"gate-evidence/{gate_id}/{command}.lane.selection.json"
                    ),
                    execution_path=(
                        "artifacts/week84-renderer-listener-retention/"
                        f"gate-evidence/{gate_id}/{command}.lane.execution.json"
                    ),
                )
                self.assertFalse((repository.root / _parts(report_path)).exists())
                self.assertTrue((control_root / _parts(report_path)).is_file())

    def test_worktree_commondir_backlink_and_unique_control_are_fail_closed(self) -> None:
        with goal_repository() as repository:
            _gate, _command, candidate = repository.prepare_product_candidate(
                gate_id="W85-R0"
            )
            control_root = repository.switch_main_to_lane(
                executor.RENDERER_BRANCH_REF
            )
            control_marker = control_root / ".git"
            pointer = control_marker.read_text("utf-8").strip()
            control_git_dir = Path(pointer.removeprefix("gitdir: "))

            backlink_path = control_git_dir / "gitdir"
            backlink_raw = backlink_path.read_bytes()
            backlink_path.write_text(str(repository.root / ".git"), encoding="utf-8")
            try:
                with self.assertRaisesRegex(
                    executor.ExecutorError, "GIT_REPOSITORY_TRUST"
                ):
                    executor._normalise_repo_root(repository.root)
            finally:
                backlink_path.write_bytes(backlink_raw)

            commondir_path = control_git_dir / "commondir"
            commondir_raw = commondir_path.read_bytes()
            commondir_path.write_text(".\n", encoding="utf-8")
            try:
                with self.assertRaisesRegex(
                    executor.ExecutorError, "GIT_REPOSITORY_TRUST"
                ):
                    executor._normalise_repo_root(repository.root)
            finally:
                commondir_path.write_bytes(commondir_raw)

            temporary: tempfile.TemporaryDirectory[str] = tempfile.TemporaryDirectory()
            duplicate_root = Path(temporary.name) / "duplicate"
            _git(
                repository.root,
                "worktree",
                "add",
                "--quiet",
                "--detach",
                str(duplicate_root),
                candidate,
            )
            repository._linked_worktrees.append((duplicate_root, temporary))
            duplicate_pointer = (duplicate_root / ".git").read_text("utf-8").strip()
            duplicate_git_dir = Path(duplicate_pointer.removeprefix("gitdir: "))
            (duplicate_git_dir / "HEAD").write_text(
                f"ref: {executor.CONTROL_BRANCH_REF}\n", encoding="ascii"
            )
            with self.assertRaisesRegex(
                executor.ExecutorError, "GIT_WORKTREE_TOPOLOGY"
            ):
                executor._normalise_repo_root(repository.root)

        with goal_repository() as repository:
            repository.write_text("one.txt", "one\n")
            candidate = repository.commit_all("one")
            (repository.root / ".git" / "shallow").write_text(
                candidate + "\n", encoding="ascii"
            )
            with self.assertRaisesRegex(executor.ExecutorError, "GIT_REPOSITORY_TRUST"):
                executor.read_provider_ledger(repository.root)


class CliTests(unittest.TestCase):
    def test_help_and_invalid_input_are_sanitised(self) -> None:
        help_result = subprocess.run(
            [sys.executable, str(Path(executor.__file__)), "--help"],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            shell=False,
            check=False,
        )
        self.assertEqual(0, help_result.returncode)
        marker = "sk-do-not-echo-this-value"
        invalid = subprocess.run(
            [sys.executable, str(Path(executor.__file__)), marker],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            shell=False,
            check=False,
        )
        self.assertEqual(2, invalid.returncode)
        self.assertNotIn(marker.encode("utf-8"), invalid.stdout + invalid.stderr)


if __name__ == "__main__":
    unittest.main()
