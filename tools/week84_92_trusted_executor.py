#!/usr/bin/env python3
"""Fail-closed execution boundary for the Week 84-92 Goal.

This module owns the two pieces of evidence that cannot safely be reconstructed
after the fact:

* provider turns are reserved, durably and atomically, *before* a child starts;
* test output is captured by a frozen runner and hash-bound to its provenance.

The implementation intentionally uses only the Python standard library.  It is
Windows-first, uses a separate cross-process ledger lock, never invokes a shell,
and never includes child environment values or untrusted input in diagnostics.
"""

from __future__ import annotations

import argparse
import ast
import base64
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import secrets
import signal
import stat
import subprocess
import sys
import sysconfig
import threading
import time
from typing import Any, BinaryIO, Mapping, Sequence
import unicodedata


SCHEMA_VERSION = "1.0.0"
GOAL_ID = "c-aicli-cli-desktop-refactor-w84-w92"
PROVIDER_MAX_TURNS = 120
PROVIDER_JOURNAL_VERSION = "week84-92-provider-runtime-v1"
RUNNER_ID = "week84-92-trusted-executor-v1"
RUNNER_SOURCE_PATH = "tools/week84_92_trusted_executor.py"
CONTROL_BRANCH_REF = "refs/heads/codex/week84-92-refactor"
RENDERER_BRANCH_REF = "refs/heads/codex/week84-92-renderer"
CLI_BRANCH_REF = "refs/heads/codex/week84-92-cli"
INTEGRATION_BRANCH_REF = "refs/heads/codex/week84-92-integration"
_GATE_LANE_BRANCHES = {
    "baseline": CONTROL_BRANCH_REF,
    "renderer": RENDERER_BRANCH_REF,
    "cli": CLI_BRANCH_REF,
    "integration": INTEGRATION_BRANCH_REF,
    "hardening": INTEGRATION_BRANCH_REF,
    "acceptance": INTEGRATION_BRANCH_REF,
}
TRUSTED_TEST_POLICY_NAME = "trusted-test-command"
TRUSTED_TEST_POLICY_ID = "week84-92-semantic-test-v1"
TRUSTED_TEST_COMMAND_ID = "goal-evidence-semantic-validate"
TRUSTED_TEST_EXECUTABLE_ROLE = "python-current-isolated"
TRUSTED_TEST_COUNTS_SOURCE = "python-unittest-output"
SEMANTIC_LOADER_PROTOCOL = "week84-92-isolated-repo-tools-loader-v1"
SEMANTIC_SITE_MODULE_PROTOCOL = "week84-92-semantic-site-modules-v1"
SEMANTIC_EXECUTION_CAPABILITY = (
    "cooperative-sealed-sources-complete-artifacts-and-site-module-inventory"
)
SEMANTIC_REPO_ROOT_ENV = "CAICLI_GOAL_REPO_ROOT"
TRUSTED_GIT_POLICY_NAME = "trusted-git"
TRUSTED_GIT_POLICY_ID = "week84-92-git-tool-v1"
TRUSTED_GIT_EXECUTABLE_ROLE = "git-frozen-core"
TRUSTED_GIT_VERSION = "2.47.1.windows.2"
TRUSTED_GIT_EXECUTABLE_BYTES = 4_151_144
TRUSTED_GIT_EXECUTABLE_SHA256 = (
    "99aa707073dc30ae0f2259dac777041e69ee90bbb8977174a1832a708b1fbb61"
)
TRUSTED_GIT_CORE_PATH = r"C:\Program Files\Git\mingw64\bin\git.exe"
TRUSTED_PROVIDER_POLICY_NAME = "trusted-provider-command"
TRUSTED_PROVIDER_POLICY_ID = "week84-92-provider-turn-v1"
TRUSTED_PROVIDER_HARNESS_PATH = "tools/week84_92_provider_turn_harness.py"
TRUSTED_PROVIDER_REDACTED_INVOCATION = (
    "python-current -I -S -E -B -X utf8 <trusted-provider-command> "
    "--descriptor <trusted-descriptor> --observation <trusted-observation>"
)
TRUSTED_PRODUCT_POLICY_NAME = "trusted-product-command"
TRUSTED_PRODUCT_POLICY_ID = "week84-92-product-command-v1"
TRUSTED_PRODUCT_SCRIPT_ROOT = "tools/week84_92_commands"
PRODUCT_COMMAND_RESULT_PROTOCOL = "week84-92-product-command-result-v1"
PRODUCT_COMMAND_RESULT_MARKER = "CAICLI_PRODUCT_COMMAND_RESULT="
ISOLATED_PYTHON_EXECUTABLE_ROLE = "python-current-isolated"
ADAPTER_EXECUTION_CAPABILITY = "cooperative-sealed-imports"
ADAPTER_COUNTS_SOURCE = "adapter-observation-only"
COMMAND_VERIFIER_POLICY_ID = "week84-92-prior-sealed-command-verifier-v1"
COMMAND_VERIFIER_DESCRIPTOR_PROTOCOL = (
    "week84-92-command-verifier-descriptor-v1"
)
COMMAND_VERIFIER_RESULT_PROTOCOL = "week84-92-command-verifier-result-v1"
COMMAND_VERIFIER_RESULT_MARKER = "CAICLI_COMMAND_VERIFIER_RESULT="
COMMAND_SELECTION_PROTOCOL = "week84-92-command-selection-v1"
COMMAND_EXECUTION_PROTOCOL = "week84-92-command-execution-v1"
COMMAND_SELECTION_MODE = "cooperative-sealed-imports"
COMMAND_DISCOVERY_POLICY_ID = "week84-92-exact-control-unittest-v1"
_RUNTIME_INPUT_KINDS = frozenset(
    {
        "goal-state",
        "entry-snapshot",
        "handoff",
        "handoff-verification",
        "gate-evidence",
    }
)
SEMANTIC_ENTRY_SOURCES: tuple[tuple[str, str], ...] = (
    ("tools.week84_92_goal_integrity", "tools/week84_92_goal_integrity.py"),
    ("tools.week84_92_evidence_anchor", "tools/week84_92_evidence_anchor.py"),
    ("tools.week84_92_trusted_executor", "tools/week84_92_trusted_executor.py"),
    (
        "tools.week84_92_provider_turn_harness",
        "tools/week84_92_provider_turn_harness.py",
    ),
    (
        "tools.week84_92_goal_validator",
        "tools/validate-week84-92-goal-evidence.py",
    ),
    (
        "tools.test_validate_week84_92_goal_evidence",
        "tools/test_validate_week84_92_goal_evidence.py",
    ),
    (
        "tools.test_week84_92_goal_integrity",
        "tools/test_week84_92_goal_integrity.py",
    ),
    (
        "tools.test_week84_92_gate_requirements",
        "tools/test_week84_92_gate_requirements.py",
    ),
    (
        "tools.test_week84_92_evidence_anchor",
        "tools/test_week84_92_evidence_anchor.py",
    ),
    (
        "tools.test_week84_92_trusted_executor",
        "tools/test_week84_92_trusted_executor.py",
    ),
)
SEMANTIC_TEST_NAMES: tuple[str, ...] = tuple(
    module
    for module, _path in SEMANTIC_ENTRY_SOURCES
    if module.startswith("tools.test_")
)
SEMANTIC_READ_SOURCES: tuple[str, ...] = (
    "tools/week84_92_commands/goal-contract-unittest.py",
    "tools/week84_92_commands/goal-control-schema-validate.py",
    "tools/week84_92_commands/prior-handoff-schema-validate.py",
    "tools/week84_92_commands/trusted-executor-unittest.py",
    "tools/week84_92_commands/week83-lineage-identity-verify.py",
)
SEMANTIC_BOOTSTRAP_SOURCES: tuple[str, ...] = (
    "docs_md/weekly/.gitattributes",
    "docs_md/plans/.gitattributes",
    "docs_md/plans/07_cli_desktop_experience_refactor.plan.md",
    "docs_md/weekly/84_92_week_cli_desktop_experience_refactor_schedule.md",
    "docs_md/weekly/84_92_week_gate_result.schema.json",
    "docs_md/weekly/84_92_week_goal_control.schema.json",
    "docs_md/weekly/84_92_week_goal_execution_contract.md",
    "docs_md/weekly/84_92_week_handoff.schema.json",
    "docs_md/weekly/84_week_renderer_listener_retention_refactor_baseline.plan.md",
    "docs_md/weekly/85_week_cli_composition_diagnostics_modules.plan.md",
    "docs_md/weekly/85_week_renderer_feature_boundaries_state_split.plan.md",
    "docs_md/weekly/86_week_chat_first_shell_design_system.plan.md",
    "docs_md/weekly/86_week_cli_jobs_review_session_modules.plan.md",
    "docs_md/weekly/87_week_cli_exec_skills_queue_modules.plan.md",
    "docs_md/weekly/87_week_conversation_projection_timeline.plan.md",
    "docs_md/weekly/88_week_cli_packs_artifacts_modules.plan.md",
    "docs_md/weekly/88_week_composer_inline_approval_task_controls.plan.md",
    "docs_md/weekly/89_week_cli_automation_pipeline_compatibility.plan.md",
    "docs_md/weekly/89_week_context_panel_terminal_review_workspace.plan.md",
    "docs_md/weekly/90_week_cli_desktop_cross_lane_integration.plan.md",
    "docs_md/weekly/91_week_refactor_security_resource_hardening.plan.md",
    "docs_md/weekly/92_week_refactor_final_acceptance.plan.md",
    "docs_md/weekly/84_92_week_gate_requirements.json",
    "tools/.gitattributes",
    "tools/test_validate_week84_92_goal_evidence.py",
    "tools/test_week84_92_evidence_anchor.py",
    "tools/test_week84_92_gate_requirements.py",
    "tools/test_week84_92_goal_integrity.py",
    "tools/test_week84_92_trusted_executor.py",
    "tools/week84_92_provider_turn_harness.py",
    "tools/week84_92_trusted_executor.py",
    "tools/validate-week84-92-goal-evidence.py",
    "tools/week84_92_evidence_anchor.py",
    "tools/week84_92_goal_integrity.py",
    "tools/week84_92_commands/goal-contract-unittest.py",
    "tools/week84_92_commands/trusted-executor-unittest.py",
    "tools/week84_92_commands/goal-control-schema-validate.py",
    "tools/week84_92_commands/prior-handoff-schema-validate.py",
    "tools/week84_92_commands/week83-lineage-identity-verify.py",
    "tools/week84_92_provider_scenarios/provider_read_only.py",
    "tools/week84_92_provider_scenarios/provider_recovery.py",
    "tools/week84_92_provider_scenarios/provider_resource.py",
    "tools/week84_92_provider_scenarios/controlled_write.py",
    "docs_md/weekly/84_92_command_control/W84-G0.json",
)
SEMANTIC_SITE_ALLOWED_ROOTS: tuple[tuple[str, str], ...] = (
    ("attr", "directory"),
    ("attrs", "directory"),
    ("idna", "directory"),
    ("jsonpointer.py", "file"),
    ("jsonschema", "directory"),
    ("jsonschema_specifications", "directory"),
    ("referencing", "directory"),
    ("rfc3339_validator.py", "file"),
    ("rfc3986_validator.py", "file"),
    ("rpds", "directory"),
    ("six.py", "file"),
)
_W84_G0_RUNTIME_INPUTS: Mapping[str, tuple[tuple[str, str], ...]] = {
    "goal-contract-unittest": (
        (
            "artifacts/week83-approval-projection-remediation/"
            "week84-handoff.json",
            "handoff",
        ),
        (
            "artifacts/week84-92-goal-control/goal-state.json",
            "goal-state",
        ),
        (
            "artifacts/week84-renderer-listener-retention/"
            "gate-evidence/W84-G0/entry.json",
            "entry-snapshot",
        ),
        (
            "artifacts/week84-renderer-listener-retention/gate-evidence/"
            "W84-G0/week83-handoff-verification.json",
            "handoff-verification",
        ),
    ),
    "trusted-executor-unittest": (),
    "goal-control-schema-validate": (
        (
            "artifacts/week84-92-goal-control/goal-state.json",
            "goal-state",
        ),
    ),
    "prior-handoff-schema-validate": (
        (
            "artifacts/week83-approval-projection-remediation/"
            "week84-handoff.json",
            "handoff",
        ),
        (
            "artifacts/week84-renderer-listener-retention/gate-evidence/"
            "W84-G0/week83-handoff-verification.json",
            "handoff-verification",
        ),
    ),
    "week83-lineage-identity-verify": (
        (
            "artifacts/week84-renderer-listener-retention/"
            "gate-evidence/W84-G0/entry.json",
            "entry-snapshot",
        ),
    ),
}
PROVIDER_LEDGER_PATH = (
    "artifacts/week84-92-goal-control/provider-turn-ledger.json"
)
PROVIDER_LEDGER_LOCK_PATH = (
    "artifacts/week84-92-goal-control/.provider-turn-ledger.lock"
)
PROVIDER_RUNTIME_JOURNAL_PATH = (
    "artifacts/week84-92-goal-control/provider-runtime-journal.json"
)
PROVIDER_RUNTIME_JOURNAL_VERSION = "week84-92-provider-execution-v1"
PROVIDER_DESCRIPTOR_PROTOCOL = "week84-92-provider-batch-descriptor-v1"
PROVIDER_OBSERVED_REQUEST_PROTOCOL = "week84-92-observed-provider-request-v1"
PACKAGE_TREE_IDENTITY_PROTOCOL = "week84-92-package-tree-identity-v1"
PACKAGE_BUILD_RECEIPT_PROTOCOL = "week84-92-package-build-receipt-v1"
PACKAGE_LAUNCH_RECEIPT_PROTOCOL = "week84-92-package-launch-receipt-v1"
PROVIDER_BOUNDARY_DECISION_PROTOCOL = (
    "week84-92-provider-boundary-decision-v1"
)
PACKAGE_TREE_ALGORITHM = "sha256-canonical-package-tree-v1"
ARTIFACT_INPUT_TREE_ALGORITHM = (
    "sha256-canonical-goal-artifact-multi-root-v1"
)
ARTIFACT_INPUT_TREE_MODE = "cooperative-frozen-goal-artifact-multi-root"
SEMANTIC_ARTIFACT_INPUT_ROOTS: tuple[str, ...] = tuple(
    sorted(
        (
            "artifacts/week83-approval-projection-remediation",
            "artifacts/week84-92-goal-control",
            "artifacts/week84-renderer-listener-retention",
            "artifacts/week85-renderer-feature-boundaries",
            "artifacts/week85-cli-composition",
            "artifacts/week86-renderer-chat-first-shell",
            "artifacts/week86-cli-jobs-review-session",
            "artifacts/week87-renderer-conversation-projection",
            "artifacts/week87-cli-exec-skills-queue",
            "artifacts/week88-renderer-composer-approval",
            "artifacts/week88-cli-packs-artifacts",
            "artifacts/week89-context-review-workspace",
            "artifacts/week89-cli-automation-pipeline",
            "artifacts/week90-cli-desktop-integration",
            "artifacts/week91-refactor-hardening",
            "artifacts/week92-refactor-acceptance",
            "artifacts/week84-92-evidence-store/sha256",
        ),
        key=lambda value: value.encode("utf-8"),
    )
)
W84_G0_POST_SEMANTIC_OUTPUTS: tuple[str, ...] = (
    "artifacts/week84-renderer-listener-retention/gate-evidence/"
    "W84-G0/goal-contract-unittest.json",
    "artifacts/week84-renderer-listener-retention/gate-evidence/"
    "W84-G0/trusted-executor-unittest.json",
    "artifacts/week84-renderer-listener-retention/gates/W84-G0.json",
)
GOAL_STATE_PATH = "artifacts/week84-92-goal-control/goal-state.json"
SEMANTIC_SITE_TREE_ALGORITHM = "sha256-canonical-semantic-site-modules-v1"
FIXED_PACKAGE_ROOT = "apps/desktop/out/C-AICLI Desktop-win32-x64"
FIXED_PACKAGE_ENTRYPOINT = "caicli-desktop.exe"
FIXED_PACKAGE_ARGV = ("--disable-gpu",)
FIXED_APPHOST_PATH = "resources/apphost/CSharpAiCli.AppHost.exe"
MAX_PACKAGE_ENTRY_COUNT = 20_000
MAX_PACKAGE_FILE_BYTES = 2 * 1024 * 1024 * 1024
MAX_PACKAGE_TOTAL_BYTES = 8 * 1024 * 1024 * 1024
_BOOTSTRAP_EXECUTION_PATH = os.environ.get("PATH", "")
_TRUSTED_GIT_CACHE_LOCK = threading.Lock()
_TRUSTED_GIT_CACHE: tuple[Path, tuple[int, int, int, int], dict[str, Any]] | None = None
COOPERATIVE_TRUST_STATEMENT = (
    "Only harness-mediated requests have a provable hard turn cap; "
    "candidate trust is an external trust root."
)
GATE_REQUIREMENTS_PATH = "docs_md/weekly/84_92_week_gate_requirements.json"
COMMAND_CONTROL_PROTOCOL = "week84-92-command-control-v1"
PROVIDER_ENV_PATH = ".env.local"
PROVIDER_ENV_KEYS = (
    "OPENAI_MODEL",
    "OPENAI_BASE_URL",
    "OPENAI_API_KEY",
)

PROVIDER_PHASE_LAYOUT: dict[str, tuple[str, ...]] = {
    "W84-G6": (
        "provider-read-only",
        "provider-recovery",
        "provider-recovery",
    ),
    "W84-G7": ("provider-resource",) * 30,
    "W84-G8": ("controlled-write",),
    "W92-G7": (
        "provider-read-only",
        "provider-recovery",
        "provider-recovery",
        *(("provider-resource",) * 30),
        "controlled-write",
    ),
}

CONTROLLED_WRITE_TOMBSTONES: dict[str, str] = {
    "W84-G8": "docs_md/weekly/84_92_controlled_write_tombstones/W84-G8.json",
    "W92-G7": "docs_md/weekly/84_92_controlled_write_tombstones/W92-G7.json",
}
CONTROLLED_WRITE_AUTHORIZATION_ROOT = (
    "docs_md/weekly/84_92_controlled_write_authorizations"
)
CONTROLLED_WRITE_PREAUTHORIZATIONS: dict[str, str] = {
    gate_id: f"{CONTROLLED_WRITE_AUTHORIZATION_ROOT}/{gate_id}/preauthorization.json"
    for gate_id in CONTROLLED_WRITE_TOMBSTONES
}
CONTROLLED_WRITE_APPROVAL_DECISIONS: dict[str, dict[str, str]] = {
    gate_id: {
        action: (
            f"{CONTROLLED_WRITE_AUTHORIZATION_ROOT}/{gate_id}/"
            f"approval-{action.replace('_', '-')}.json"
        )
        for action in ("apply_patch", "shell")
    }
    for gate_id in CONTROLLED_WRITE_TOMBSTONES
}
CONTROLLED_WRITE_PREAUTHORIZATION_PROTOCOL = (
    "week84-92-controlled-write-preauthorization-v1"
)
CONTROLLED_WRITE_APPROVAL_PROTOCOL = (
    "week84-92-controlled-write-approval-decision-v1"
)
CONTROLLED_WRITE_HARNESS_OWNER = "week84-92-provider-harness-v1"
CONTROLLED_WRITE_TRANSITION = {
    "onlyChangedPath": "result.txt",
    "fromValue": "fail",
    "toValue": "pass",
}
CONTROLLED_WRITE_VALIDATION = {
    "executable": "dotnet",
    "arguments": ["msbuild", "Week82Gate.proj", "-target:Test", "-nologo"],
    "redactedInvocation": "dotnet msbuild Week82Gate.proj -target:Test -nologo",
    "commandCount": 1,
    "dotnetSdk": "9.0.308",
}
CONTROLLED_WRITE_PRECONDITIONS: dict[str, tuple[str, ...]] = {
    "W84-G8": tuple(f"W84-G{index}" for index in range(8)),
    "W92-G7": tuple(f"W92-G{index}" for index in range(7)),
}
CONTROLLED_DESCENDANT_GATES = {
    "W84-G8": "W84-G8",
    "W84-G9": "W84-G8",
    "W92-G7": "W92-G7",
    "W92-G8": "W92-G7",
    "W92-G9": "W92-G7",
}
MANUAL_DESCENDANT_GATES = {
    "W86-R7": ("W86-USER-VISUAL", "W86"),
    "W89-R5": ("W89-USER-VISUAL", "W89"),
    "W89-R6": ("W89-USER-VISUAL", "W89"),
    "W89-R7": ("W89-USER-VISUAL", "W89"),
    "W92-G9": ("W92-USER-VISUAL", "W92"),
}
USER_ACCEPTANCE_REQUEST_ROOT = (
    "docs_md/weekly/84_92_user_acceptance_requests"
)
_USER_ACCEPTANCE_REQUEST_KEYS = frozenset(
    {
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
)
_DECISION_REQUEST_ID = re.compile(r"^UA-[A-Z0-9][A-Z0-9-]{2,63}$")
_CHALLENGE_CODE = re.compile(r"^CH-[A-Z0-9]{8,32}$")
CANONICAL_GATE_RESULT_PATHS: dict[str, str] = {
    **{
        f"W84-G{index}": (
            "artifacts/week84-renderer-listener-retention/gates/"
            f"W84-G{index}.json"
        )
        for index in range(10)
    },
    **{
        f"W92-G{index}": (
            "artifacts/week92-refactor-acceptance/gates/"
            f"W92-G{index}.json"
        )
        for index in range(10)
    },
}
PROVIDER_ARTIFACT_ROOTS = {
    "W84-G6": "artifacts/week84-renderer-listener-retention",
    "W84-G7": "artifacts/week84-renderer-listener-retention",
    "W84-G8": "artifacts/week84-renderer-listener-retention",
    "W92-G7": "artifacts/week92-refactor-acceptance",
}
PROVIDER_SCENARIO_PATHS = {
    "provider-read-only": (
        "tools/week84_92_provider_scenarios/provider_read_only.py"
    ),
    "provider-recovery": (
        "tools/week84_92_provider_scenarios/provider_recovery.py"
    ),
    "provider-resource": (
        "tools/week84_92_provider_scenarios/provider_resource.py"
    ),
    "controlled-write": (
        "tools/week84_92_provider_scenarios/controlled_write.py"
    ),
}
PROVIDER_DRIVER_PATHS = {
    "provider-read-only": (
        "tools/week84_92_provider_drivers/provider_read_only.mjs"
    ),
    "provider-recovery": (
        "tools/week84_92_provider_drivers/provider_recovery.mjs"
    ),
    "provider-resource": (
        "tools/week84_92_provider_drivers/provider_resource.mjs"
    ),
    "controlled-write": (
        "tools/week84_92_provider_drivers/controlled_write.mjs"
    ),
}
PROVIDER_BOUNDARY_DECISION_PATHS = {
    "W84-G6": "docs_md/weekly/84_92_provider_boundary_decisions/W84-G6.json",
    "W84-G7": "docs_md/weekly/84_92_provider_boundary_decisions/W84-G6.json",
    "W84-G8": "docs_md/weekly/84_92_provider_boundary_decisions/W84-G6.json",
    "W92-G7": "docs_md/weekly/84_92_provider_boundary_decisions/W92-G7.json",
}
PROVIDER_BOUNDARY_GATES = {
    "W84-G6": "W84-G6",
    "W84-G7": "W84-G6",
    "W84-G8": "W84-G6",
    "W92-G7": "W92-G7",
}
PROVIDER_BOUNDARY_ALLOWED_GATES = {
    "W84-G6": ("W84-G6", "W84-G7", "W84-G8"),
    "W92-G7": ("W92-G7",),
}
PROVIDER_BOUNDARY_ALLOWED_SCOPES = (
    "provider-read-only",
    "provider-recovery",
    "provider-resource",
    "controlled-write",
)

_PROVIDER_ENTRY_KEYS = frozenset(
    {
        "sequence",
        "eventType",
        "reservationId",
        "gateId",
        "phase",
        "productCandidate",
        "attemptId",
        "runId",
        "reservedAt",
        "previousEntrySha256",
        "entrySha256",
    }
)
_EVENT_COMMON_KEYS = frozenset(
    {
        "attemptEventSequence",
        "eventType",
        "previousAttemptEventSha256",
        "attemptEventSha256",
    }
)
_TURN_COMPLETED_KEYS = _EVENT_COMMON_KEYS | {
    "reservationId",
    "outcome",
    "completedAt",
}
_ATTEMPT_FINISHED_KEYS = _EVENT_COMMON_KEYS | {
    "gateId",
    "productCandidate",
    "attemptId",
    "outcome",
    "reservationSequenceStart",
    "reservationSequenceEnd",
    "finishedAt",
}
_LEDGER_KEYS = frozenset(
    {
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
)
_RUNTIME_JOURNAL_KEYS = frozenset(
    {
        "schemaVersion",
        "journalVersion",
        "goalId",
        "eventCount",
        "lastEventSha256",
        "events",
    }
)
_RUNTIME_EVENT_KEYS = frozenset(
    {
        "sequence",
        "eventType",
        "reservationId",
        "reservationSequence",
        "gateId",
        "phase",
        "productCandidate",
        "attemptId",
        "runId",
        "batchId",
        "batchSize",
        "commandPolicy",
        "argvSha256",
        "harnessSource",
        "driverSource",
        "descriptor",
        "observation",
        "providerBoundaryDecision",
        "packageIdentityEvidence",
        "packageLaunchReceipt",
        "observedRequest",
        "checkoutIdentity",
        "startedAt",
        "finishedAt",
        "exitCode",
        "outcome",
        "previousEventSha256",
        "eventSha256",
    }
)
_RUNTIME_POLICY_KEYS = frozenset({"policyId", "sha256"})
_SOURCE_BINDING_KEYS = frozenset(
    {"path", "sha256", "gitBlobSha", "controlRevision"}
)
_PATH_HASH_KEYS = frozenset({"path", "sha256"})
_PACKAGE_IDENTITY_BINDING_KEYS = frozenset(
    {"path", "sha256", "treeRootSha256", "entrypoint", "argv"}
)
_BOUNDARY_BINDING_KEYS = frozenset(
    {
        "path",
        "sha256",
        "firstAddCommit",
        "decisionId",
        "mode",
        "packageTreeRootSha256",
    }
)
_LAUNCH_RECEIPT_BINDING_KEYS = frozenset(
    {
        "path",
        "sha256",
        "phase",
        "profileOrdinal",
        "batchId",
        "packageTreeRootSha256",
    }
)
_OBSERVATION_BINDING_KEYS = frozenset({"path", "sha256", "status"})
_CHECKOUT_IDENTITY_KEYS = frozenset(
    {"mode", "productCandidate", "expectedHead", "before", "after"}
)
_CHECKOUT_SNAPSHOT_KEYS = frozenset(
    {"headCommit", "treeObjectId", "gitStatusPorcelainV1", "trackedStatus"}
)
_OBSERVED_REQUEST_KEYS = frozenset(
    {
        "reservationId",
        "reservationSequence",
        "requestOrdinal",
        "requestSha256",
        "status",
    }
)
_PROVIDER_DESCRIPTOR_KEYS = frozenset(
    {
        "schemaVersion",
        "protocol",
        "goalId",
        "batchId",
        "gateId",
        "phase",
        "profileOrdinal",
        "productCandidate",
        "attemptId",
        "runId",
        "artifactRoot",
        "packageIdentityEvidence",
        "providerBoundaryDecision",
        "scenarioPath",
        "scenarioSource",
        "driverPath",
        "driverSource",
        "packageLaunchReceiptPath",
        "stagingRelativeRoot",
        "runTokenSha256",
        "controlledWriteTombstone",
        "controlledWritePreauthorization",
        "reservations",
    }
)
_DESCRIPTOR_RESERVATION_KEYS = frozenset(
    {"reservationId", "reservationSequence", "turnOrdinal"}
)
_OBSERVATION_KEYS = frozenset(
    {
        "schemaVersion",
        "protocol",
        "batchId",
        "gateId",
        "phase",
        "productCandidate",
        "attemptId",
        "runId",
        "packageIdentityEvidence",
        "providerBoundaryDecision",
        "requests",
    }
)
_PACKAGE_TREE_ENTRY_KEYS = frozenset({"path", "mode", "bytes", "sha256"})
_PACKAGE_TREE_KEYS = frozenset(
    {
        "algorithm",
        "entryCount",
        "totalBytes",
        "treeRootSha256",
        "entries",
    }
)
_PACKAGE_ENTRYPOINT_KEYS = frozenset({"path", "argv"})
_PACKAGE_BUILD_BINDING_KEYS = frozenset({"path", "rawSha256"})
_TRUSTED_COMMAND_REPORT_BINDING_KEYS = frozenset({"path", "rawSha256"})
_PACKAGE_IDENTITY_KEYS = frozenset(
    {
        "schemaVersion",
        "protocol",
        "status",
        "goalId",
        "productCandidate",
        "packageRoot",
        "buildReceipt",
        "tree",
        "entrypoint",
        "appHostPath",
    }
)
_PACKAGE_BUILD_RECEIPT_KEYS = frozenset(
    {
        "schemaVersion",
        "protocol",
        "status",
        "goalId",
        "productCandidate",
        "sourceTreeObjectId",
        "packageRoot",
        "treeRootSha256",
        "entryCount",
        "totalBytes",
        "entrypoint",
        "argv",
        "appHostPath",
        "commandId",
        "trustedCommandReport",
        "startedAt",
        "finishedAt",
        "exitCode",
    }
)
_BOUNDARY_DECISION_KEYS = frozenset(
    {
        "schemaVersion",
        "protocol",
        "goalId",
        "decisionId",
        "boundaryGate",
        "mode",
        "productCandidate",
        "packageIdentity",
        "allowedGates",
        "allowedScopes",
        "authorizedTurns",
        "maxTurns",
        "userChallengeReceipt",
        "issuedAt",
        "expiresAt",
        "revoked",
        "strongCanaries",
        "cooperativeTrustStatement",
    }
)
_BOUNDARY_PACKAGE_KEYS = frozenset(
    {
        "path",
        "rawSha256",
        "packageRoot",
        "treeRootSha256",
        "entryCount",
        "totalBytes",
        "entrypoint",
        "argv",
        "appHostPath",
    }
)
_BOUNDARY_CHALLENGE_KEYS = frozenset(
    {
        "requestId",
        "challengeCode",
        "response",
        "rawResponseSha256",
        "confirmedBy",
        "decidedAt",
    }
)
_STRONG_CANARY_KEYS = frozenset(
    {
        "receiptPath",
        "rawSha256",
        "sandboxPolicySha256",
        "filesystemPassed",
        "networkPassed",
        "processPassed",
    }
)
_LAUNCH_RECEIPT_REQUIRED_KEYS = frozenset(
    {
        "goalId",
        "gateId",
        "productCandidate",
        "phase",
        "profileOrdinal",
        "batchId",
        "packageTreeRootSha256",
        "boundaryDecision",
        "sourcePackage",
        "stagedPackageBefore",
        "stagedPackageAfter",
        "launches",
        "containment",
        "observedRequestCount",
        "scenarioAssertions",
        "cleanup",
    }
)
_TOMBSTONE_KEYS = frozenset(
    {
        "schemaVersion",
        "goalId",
        "gateId",
        "productCandidate",
        "leaseId",
        "nonceSha256",
        "issuedAt",
        "consumedAt",
        "preconditionGateBindings",
        "preauthorizationBinding",
    }
)
_PRECONDITION_BINDING_KEYS = frozenset(
    {
        "gateId",
        "productCandidate",
        "resultPath",
        "resultSha256",
        "status",
        "finishedAt",
    }
)
_PREAUTHORIZATION_KEYS = frozenset(
    {
        "schemaVersion",
        "protocol",
        "goalId",
        "preauthorizationId",
        "gateId",
        "status",
        "productCandidate",
        "candidateBinding",
        "preconditionGateBindings",
        "harnessWorkspaceIdentity",
        "writeTransition",
        "validationCommand",
        "approvalDecisionBindings",
        "authorizedAt",
    }
)
_PREAUTHORIZATION_CANDIDATE_KEYS = frozenset(
    {"productCandidate", "treeObjectId"}
)
_WORKSPACE_IDENTITY_KEYS = frozenset(
    {
        "workspaceId",
        "workspaceName",
        "workspaceRelativePath",
        "owner",
        "scenarioPath",
        "cleanupRequired",
    }
)
_WRITE_TRANSITION_KEYS = frozenset(CONTROLLED_WRITE_TRANSITION)
_VALIDATION_COMMAND_KEYS = frozenset(CONTROLLED_WRITE_VALIDATION)
_APPROVAL_BINDING_KEYS = frozenset(
    {"approvalId", "action", "path", "sha256", "commit"}
)
_APPROVAL_DECISION_KEYS = frozenset(
    {
        "schemaVersion",
        "protocol",
        "goalId",
        "preauthorizationId",
        "gateId",
        "productCandidate",
        "approvalId",
        "action",
        "decision",
        "durable",
        "decisionBindings",
        "decidedAt",
    }
)
_DECISION_BINDING_KEYS = frozenset(
    {
        "candidateBindingSha256",
        "preconditionGateBindingsSha256",
        "harnessWorkspaceIdentitySha256",
        "writeTransitionSha256",
        "validationCommandSha256",
    }
)
_IMMUTABLE_BINDING_KEYS = frozenset({"path", "sha256", "commit"})
_COUNT_KEYS = (
    "discovered",
    "passed",
    "failed",
    "skipped",
    "notRun",
    "notApplicable",
)
_PRODUCT_COUNT_KEYS = frozenset({"discovered", "passed", "failed", "skipped"})
_TRUSTED_TEST_POLICY_KEYS = frozenset(
    {
        "policyId",
        "commandId",
        "executableRole",
        "arguments",
        "redactedInvocation",
        "countsSource",
        "shellAllowed",
        "loaderProtocol",
        "entrySources",
        "readSources",
    }
)
_SEMANTIC_ENTRY_SOURCE_KEYS = frozenset({"moduleName", "path", "sha256"})
_SEMANTIC_READ_SOURCE_KEYS = frozenset({"path", "sha256"})
_SEMANTIC_BOOTSTRAP_SOURCE_KEYS = frozenset({"path", "sha256", "gitBlobSha"})
_TRUSTED_PROVIDER_POLICY_KEYS = frozenset(
    {
        "policyId",
        "executableRole",
        "sourcePath",
        "sourceSha256",
        "scenarioSources",
        "argumentsTemplate",
        "redactedInvocation",
        "shellAllowed",
        "allowedBatchSizes",
        "observedRequestProtocol",
    }
)
_TRUSTED_GIT_POLICY_KEYS = frozenset(
    {
        "policyId",
        "executableRole",
        "version",
        "executableBytes",
        "executableSha256",
        "shellAllowed",
    }
)
_PROVIDER_SCENARIO_POLICY_KEYS = frozenset({"path", "sha256"})
_COMMAND_CONTROL_KEYS = frozenset(
    {
        "schemaVersion",
        "protocol",
        "goalId",
        "gateId",
        "sourceTrust",
        "preparedFromRevision",
        "sources",
        "sourceTrees",
        "productionProjection",
        "runtimeDrivers",
    }
)
_RUNTIME_DRIVER_KEYS = frozenset(
    {"phase", "path", "sha256", "gitBlobSha", "originControlRevision"}
)
_COMMAND_SOURCE_KEYS = frozenset(
    {
        "commandId",
        "role",
        "path",
        "sha256",
        "gitBlobSha",
        "origin",
        "originControlRevision",
        "verification",
    }
)
_COMMAND_SOURCE_TREE_KEYS = frozenset(
    {
        "commandId",
        "role",
        "rootPath",
        "gitTreeSha",
        "entryCount",
        "totalBytes",
        "contentRootSha256",
        "origin",
        "originControlRevision",
        "includePolicy",
        "preparedPaths",
    }
)
_COMMAND_CONTROL_ROLES = frozenset(
    {"adapter", "oracle", "test", "fixture", "parser"}
)
_COMMAND_CONTROL_ORIGINS = frozenset(
    {"prepared", "bootstrap", "prior-control"}
)
_COMMAND_CONTROL_ROLE_POLICY = {
    "requiredAll": ["adapter"],
    "requiredAny": [["oracle", "test"]],
    "optional": ["fixture", "parser"],
}
_SOURCE_TREE_POLICY_ROOTS = {
    "csharp-test-tree-v1": "src/CSharpAiCli.Tests",
    "desktop-unit-test-tree-v1": "apps/desktop/src",
    "desktop-e2e-test-tree-v1": "apps/desktop/e2e",
}
_VERIFICATION_KEYS = frozenset({"arguments", "verifiesCommandIds"})
_TOOL_IDENTITY_KEYS = frozenset(
    {
        "executableRole",
        "locationRole",
        "version",
        "executableSha256",
        "runtimeTreeSha256",
        "runtimeTrustMode",
    }
)
_VERIFIED_ADAPTER_KEYS = frozenset(
    {
        "path",
        "sha256",
        "commandId",
        "exitCode",
        "checkoutIdentitySha256",
        "stdoutSha256",
        "stderrSha256",
    }
)
_SELECTION_SUMMARY_KEYS = frozenset(
    {
        "mode",
        "runnerKind",
        "discoveryPolicyId",
        "selectionOrigin",
        "manifestPath",
        "manifestSha256",
        "selectedCaseCount",
        "selectedCaseRootSha256",
        "selectedSourceCount",
        "selectedSourceRootSha256",
        "zeroSelectionAllowed",
    }
)
_EXECUTION_RECONCILIATION_KEYS = frozenset(
    {
        "manifestPath",
        "manifestSha256",
        "executedCaseCount",
        "executedCaseRootSha256",
        "matchesSelection",
    }
)
_TRUSTED_PRODUCT_POLICY_KEYS = frozenset(
    {
        "policyId",
        "executableRole",
        "scriptRoot",
        "argumentsTemplate",
        "redactedInvocationTemplate",
        "shellAllowed",
    }
)
_PROVIDER_TEMPLATE_ARGUMENTS = (
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
)
_PROVIDER_BATCH_SIZES = {
    "provider-read-only": (1,),
    "provider-recovery": (2,),
    "provider-resource": (6,),
    "controlled-write": (1,),
}
_ADAPTER_WRAPPER_SOURCE = r'''import builtins
import hashlib
import importlib.abc
import importlib.util
import io
import json
import os
import sys
import types

LIMIT = 8 * 1024 * 1024
PROTOCOL = "week84-92-adapter-invocation-v1"

class DuplicateKey(ValueError):
    pass

def pairs(values):
    result = {}
    for key, value in values:
        if key in result:
            raise DuplicateKey(key)
        result[key] = value
    return result

def canonical(value):
    return json.dumps(
        value, ensure_ascii=False, sort_keys=True, separators=(",", ":"),
        allow_nan=False,
    ).encode("utf-8")

def reject():
    raise SystemExit(126)

def relative(value):
    if not isinstance(value, str) or not value or "\\" in value:
        reject()
    parts = value.split("/")
    if any(part in {"", ".", ".."} for part in parts):
        reject()
    return parts

def read_bound(root, path, expected):
    parts = relative(path)
    target = os.path.realpath(os.path.join(root, *parts))
    try:
        if os.path.commonpath((root, target)) != root or os.path.islink(target):
            reject()
        with open(target, "rb") as handle:
            raw = handle.read(LIMIT + 1)
    except OSError:
        reject()
    if len(raw) > LIMIT or hashlib.sha256(raw).hexdigest() != expected:
        reject()
    return target, raw

if len(sys.argv) != 3:
    reject()
script_path = sys.argv[1]
script_sha = sys.argv[2]
root = os.path.realpath(os.getcwd())
script_target, script_raw = read_bound(root, script_path, script_sha)
invocation_raw = sys.stdin.buffer.read(LIMIT + 1)
try:
    invocation = json.loads(
        invocation_raw.decode("utf-8"), object_pairs_hook=pairs
    )
except (UnicodeDecodeError, ValueError, DuplicateKey):
    reject()
if canonical(invocation) != invocation_raw or set(invocation) != {
    "protocol", "sourceDependencies", "runtimeInputs"
} or invocation.get("protocol") != PROTOCOL:
    reject()
dependencies = invocation.get("sourceDependencies")
runtime_inputs = invocation.get("runtimeInputs")
if (
    not isinstance(dependencies, list)
    or any(
        not isinstance(item, dict)
        or set(item) != {"role", "path", "sha256", "gitBlobSha", "controlRevision"}
        or item.get("role") not in {"adapter", "oracle", "test", "fixture", "parser"}
        for item in dependencies
    )
    or not isinstance(runtime_inputs, list)
    or any(
        not isinstance(item, dict)
        or set(item) != {"path", "sha256", "kind"}
        or item.get("kind") not in {
            "goal-state", "entry-snapshot", "handoff",
            "handoff-verification", "gate-evidence",
        }
        for item in runtime_inputs
    )
):
    reject()
allowed_repo_reads = {script_target: script_sha}
bindings = {}
for item in dependencies:
    target, raw = read_bound(root, item["path"], item["sha256"])
    allowed_repo_reads[target] = item["sha256"]
    if item["path"].endswith(".py"):
        name = item["path"][:-3].replace("/", ".")
        if name in bindings and bindings[name][:2] != (item["path"], item["sha256"]):
            reject()
        bindings[name] = (item["path"], item["sha256"], raw)
for item in runtime_inputs:
    target, raw = read_bound(root, item["path"], item["sha256"])
    allowed_repo_reads[target] = hashlib.sha256(raw).hexdigest()
for module_name, (path, _expected, _raw) in tuple(bindings.items()):
    components = module_name.split(".")[:-1]
    for index in range(1, len(components) + 1):
        parent = ".".join(components[:index])
        if parent not in sys.modules:
            package = types.ModuleType(parent)
            package.__package__ = parent
            package.__path__ = [os.path.join(root, *components[:index])]
            sys.modules[parent] = package

class Loader(importlib.abc.Loader):
    def __init__(self, name):
        self.name = name
    def create_module(self, spec):
        return None
    def exec_module(self, module):
        path, expected, frozen = bindings[self.name]
        _target, current = read_bound(root, path, expected)
        if current != frozen:
            reject()
        module.__file__ = path
        module.__package__ = self.name.rpartition(".")[0]
        exec(compile(current.decode("utf-8"), path, "exec", dont_inherit=True), module.__dict__)

class Finder(importlib.abc.MetaPathFinder):
    def find_spec(self, fullname, path=None, target=None):
        if fullname in bindings:
            return importlib.util.spec_from_loader(fullname, Loader(fullname))
        local_file = os.path.join(root, *fullname.split(".")) + ".py"
        local_package = os.path.join(root, *fullname.split("."), "__init__.py")
        if os.path.isfile(local_file) or os.path.isfile(local_package):
            raise ImportError("undeclared repository-local import")
        return None

sys.meta_path.insert(0, Finder())
real_open = builtins.open
real_io_open = io.open
def guarded(real, file, mode, args, kwargs):
    if isinstance(file, int):
        return real(file, mode, *args, **kwargs)
    value = os.fspath(file)
    absolute = os.path.realpath(
        value if os.path.isabs(value) else os.path.join(root, value)
    )
    try:
        inside = os.path.commonpath((root, absolute)) == root
    except ValueError:
        inside = False
    if inside and (
        absolute not in allowed_repo_reads or any(flag in mode for flag in "wax+")
    ):
        raise PermissionError("undeclared repository-local file access")
    return real(file, mode, *args, **kwargs)
def guarded_open(file, mode="r", *args, **kwargs):
    return guarded(real_open, file, mode, args, kwargs)
def guarded_io_open(file, mode="r", *args, **kwargs):
    return guarded(real_io_open, file, mode, args, kwargs)
builtins.open = guarded_open
io.open = guarded_io_open
try:
    source = script_raw.decode("utf-8")
except UnicodeDecodeError:
    reject()
namespace = {
    "__name__": "__main__", "__file__": script_path,
    "__package__": None, "__cached__": None, "__spec__": None,
}
sys.argv = [script_path]
exec(compile(source, script_path, "exec", dont_inherit=True), namespace, namespace)
'''

_VERIFIER_WRAPPER_SOURCE = r'''import builtins
import hashlib
import importlib.abc
import importlib.util
import io
import json
import os
import sys
import types
import unittest

LIMIT = 8 * 1024 * 1024
DESCRIPTOR_PROTOCOL = "week84-92-command-verifier-descriptor-v1"
SELECTION_PROTOCOL = "week84-92-command-selection-v1"
RESULT_PROTOCOL = "week84-92-command-verifier-result-v1"
RESULT_MARKER = "CAICLI_COMMAND_VERIFIER_RESULT="

class DuplicateKey(ValueError):
    pass

def pairs(values):
    result = {}
    for key, value in values:
        if key in result:
            raise DuplicateKey(key)
        result[key] = value
    return result

def canonical(value):
    return json.dumps(
        value, ensure_ascii=False, sort_keys=True, separators=(",", ":"),
        allow_nan=False,
    ).encode("utf-8")

def reject():
    raise SystemExit(126)

def relative(value):
    if not isinstance(value, str) or not value or "\\" in value:
        reject()
    parts = value.split("/")
    if any(part in {"", ".", ".."} for part in parts):
        reject()
    return parts

def read_bound(root, path, expected):
    parts = relative(path)
    target = os.path.realpath(os.path.join(root, *parts))
    try:
        if os.path.commonpath((root, target)) != root or os.path.islink(target):
            reject()
        with open(target, "rb") as handle:
            raw = handle.read(LIMIT + 1)
    except OSError:
        reject()
    if len(raw) > LIMIT or hashlib.sha256(raw).hexdigest() != expected:
        reject()
    return raw

raw_descriptor = sys.stdin.buffer.read(LIMIT + 1)
if not raw_descriptor or len(raw_descriptor) > LIMIT:
    reject()
try:
    descriptor = json.loads(raw_descriptor.decode("utf-8"), object_pairs_hook=pairs)
except (UnicodeDecodeError, ValueError, DuplicateKey):
    reject()
if canonical(descriptor) != raw_descriptor or set(descriptor) != {
    "schemaVersion", "protocol", "goalId", "gateId", "productCandidate",
    "attemptId", "evidenceRoot", "commandRole", "commandControlBinding", "verifierSource",
    "sourceDependencies", "sourceTrees", "arguments", "verifiesCommandIds",
    "adapterReports", "checkoutIdentitySha256", "toolIdentity",
    "gitToolIdentity", "selectedDiscovery", "runtimeInputs",
} or descriptor.get("protocol") != DESCRIPTOR_PROTOCOL:
    reject()
root = os.path.realpath(os.getcwd())
evidence_value = descriptor.get("evidenceRoot")
if not isinstance(evidence_value, str) or not os.path.isabs(evidence_value):
    reject()
evidence_root = os.path.realpath(evidence_value)
if evidence_root != os.path.abspath(evidence_value) or not os.path.isdir(evidence_root):
    reject()
selection_summary = descriptor.get("selectedDiscovery")
if not isinstance(selection_summary, dict) or set(selection_summary) != {
    "mode", "runnerKind", "discoveryPolicyId", "selectionOrigin",
    "manifestPath", "manifestSha256", "selectedCaseCount",
    "selectedCaseRootSha256", "selectedSourceCount", "selectedSourceRootSha256",
    "zeroSelectionAllowed",
} or selection_summary.get("mode") != "cooperative-sealed-imports" or (
    selection_summary.get("runnerKind") != "python-unittest"
) or selection_summary.get("selectionOrigin") != "exact-control-arguments" or (
    selection_summary.get("zeroSelectionAllowed") is not False
):
    reject()
selection_raw = read_bound(
    evidence_root, selection_summary.get("manifestPath"),
    selection_summary.get("manifestSha256"),
)
try:
    selection = json.loads(selection_raw.decode("utf-8"), object_pairs_hook=pairs)
except (UnicodeDecodeError, ValueError, DuplicateKey):
    reject()
if canonical(selection) != selection_raw or set(selection) != {
    "schemaVersion", "protocol", "goalId", "gateId", "commandId",
    "productCandidate", "controlRevision", "runnerKind", "selectionOrigin",
    "verifierArgvSha256", "selectedCases", "selectedCaseCount",
    "selectedCaseRootSha256", "selectedSources", "selectedSourceCount",
    "selectedSourceRootSha256", "gitToolIdentity", "createdAt",
} or selection.get("protocol") != SELECTION_PROTOCOL:
    reject()
control = descriptor.get("commandControlBinding")
adapter_reports = descriptor.get("adapterReports")
arguments = descriptor.get("arguments")
verifies = descriptor.get("verifiesCommandIds")
source = descriptor.get("verifierSource")
git_identity = descriptor.get("gitToolIdentity")
if (
    not isinstance(control, dict)
    or selection.get("controlRevision") != control.get("controlRevision")
    or selection.get("goalId") != descriptor.get("goalId")
    or selection.get("gateId") != descriptor.get("gateId")
    or selection.get("productCandidate") != descriptor.get("productCandidate")
    or selection.get("runnerKind") != selection_summary.get("runnerKind")
    or selection.get("selectionOrigin") != selection_summary.get("selectionOrigin")
    or not isinstance(arguments, list) or not arguments
    or len(set(arguments)) != len(arguments)
    or not isinstance(verifies, list) or not verifies
    or not isinstance(source, dict)
    or not isinstance(adapter_reports, list) or len(adapter_reports) != 1
    or not isinstance(git_identity, dict)
    or set(git_identity) != {
        "policyId", "policySha256", "before", "after", "matchesBefore"
    }
    or git_identity.get("matchesBefore") is not True
    or git_identity.get("before") != git_identity.get("after")
    or selection.get("gitToolIdentity") != git_identity
):
    reject()
selected_cases = selection.get("selectedCases")
selected_sources = selection.get("selectedSources")
if not isinstance(selected_cases, list) or not isinstance(selected_sources, list):
    reject()
if (
    selection.get("selectedCaseCount") != len(selected_cases)
    or selection.get("selectedCaseRootSha256") != hashlib.sha256(canonical(selected_cases)).hexdigest()
    or selection.get("selectedSourceCount") != len(selected_sources)
    or selection.get("selectedSourceRootSha256") != hashlib.sha256(canonical(selected_sources)).hexdigest()
    or any(not isinstance(item, dict) or set(item) != {"caseId", "projectId", "sourcePath"} for item in selected_cases)
    or any(not isinstance(item, dict) or set(item) != {"path", "sha256", "gitBlobSha", "sourceTreeContentRootSha256"} for item in selected_sources)
):
    reject()
case_ids = [item.get("caseId") for item in selected_cases]
if len(set(case_ids)) != len(case_ids) or any(arg not in case_ids for arg in arguments):
    reject()
selection_projection = {
    "manifestPath": selection_summary.get("manifestPath"),
    "manifestSha256": selection_summary.get("manifestSha256"),
    "selectedCaseCount": selection.get("selectedCaseCount"),
    "selectedCaseRootSha256": selection.get("selectedCaseRootSha256"),
    "selectedSourceCount": selection.get("selectedSourceCount"),
    "selectedSourceRootSha256": selection.get("selectedSourceRootSha256"),
}
for key, value in selection_projection.items():
    if selection_summary.get(key) != value:
        reject()
adapter = adapter_reports[0]
if not isinstance(adapter, dict) or set(adapter) != {
    "path", "sha256", "commandId", "exitCode", "checkoutIdentitySha256",
    "stdoutSha256", "stderrSha256",
} or adapter.get("commandId") not in verifies:
    reject()
adapter_raw = read_bound(evidence_root, adapter.get("path"), adapter.get("sha256"))
try:
    adapter_doc = json.loads(adapter_raw.decode("utf-8"), object_pairs_hook=pairs)
except (UnicodeDecodeError, ValueError, DuplicateKey):
    reject()
if canonical(adapter_doc) != adapter_raw or (
    adapter_doc.get("gateId") != descriptor.get("gateId")
    or adapter_doc.get("productCandidate") != descriptor.get("productCandidate")
    or adapter_doc.get("commandId") != adapter.get("commandId")
    or adapter_doc.get("attemptId") != descriptor.get("attemptId")
    or adapter_doc.get("commandControlBinding") != control
    or adapter_doc.get("exitCode") != adapter.get("exitCode")
    or hashlib.sha256(canonical(adapter_doc.get("checkoutIdentity"))).hexdigest()
       != adapter.get("checkoutIdentitySha256")
    or adapter_doc.get("stdout", {}).get("sha256") != adapter.get("stdoutSha256")
    or adapter_doc.get("stderr", {}).get("sha256") != adapter.get("stderrSha256")
    or adapter.get("checkoutIdentitySha256") != descriptor.get("checkoutIdentitySha256")
):
    reject()

bindings = {}
if not isinstance(source, dict) or set(source) != {
    "path", "sha256", "gitBlobSha", "controlRevision"
}:
    reject()
dependencies = descriptor.get("sourceDependencies")
source_trees = descriptor.get("sourceTrees")
runtime_inputs = descriptor.get("runtimeInputs")
if (
    not isinstance(dependencies, list)
    or any(
        not isinstance(item, dict)
        or set(item) != {"role", "path", "sha256", "gitBlobSha", "controlRevision"}
        or item.get("role") not in {"adapter", "oracle", "test", "fixture", "parser"}
        for item in dependencies
    )
    or not isinstance(source_trees, list)
    or any(
        not isinstance(item, dict)
        or set(item) != {
            "role", "rootPath", "gitTreeSha", "entryCount", "totalBytes",
            "contentRootSha256", "controlRevision", "includePolicy",
        }
        or item.get("role") != "fixture"
        for item in source_trees
    )
    or not isinstance(runtime_inputs, list)
    or any(
        not isinstance(item, dict)
        or set(item) != {"path", "sha256", "kind"}
        or item.get("kind") not in {
            "goal-state", "entry-snapshot", "handoff",
            "handoff-verification", "gate-evidence",
        }
        for item in runtime_inputs
    )
):
    reject()
allowed_repo_reads = {}
for item in [source, *dependencies]:
    if not isinstance(item.get("path"), str):
        reject()
    path = item["path"]
    expected = item.get("sha256")
    raw = read_bound(root, path, expected)
    allowed_repo_reads[os.path.realpath(os.path.join(root, *relative(path)))] = expected
    if path.endswith(".py"):
        module_name = path[:-3].replace("/", ".")
        existing = bindings.get(module_name)
        if existing is not None and existing != (path, expected):
            reject()
        bindings[module_name] = (path, expected, raw)
for item in runtime_inputs:
    runtime_raw = read_bound(root, item.get("path"), item.get("sha256"))
    allowed_repo_reads[
        os.path.realpath(os.path.join(root, *relative(item["path"])))
    ] = hashlib.sha256(runtime_raw).hexdigest()
source_module = source.get("path", "")[:-3].replace("/", ".")
if source_module not in bindings or any(
    not isinstance(arg, str) or not arg.startswith(source_module + ".")
    for arg in arguments
):
    reject()

for module_name, (path, _expected, _raw) in tuple(bindings.items()):
    components = module_name.split(".")[:-1]
    for index in range(1, len(components) + 1):
        parent = ".".join(components[:index])
        if parent not in sys.modules:
            package = types.ModuleType(parent)
            package.__package__ = parent
            package.__path__ = [os.path.join(root, *components[:index])]
            sys.modules[parent] = package

class Loader(importlib.abc.Loader):
    def __init__(self, name):
        self.name = name
    def create_module(self, spec):
        return None
    def exec_module(self, module):
        path, expected, frozen = bindings[self.name]
        current = read_bound(root, path, expected)
        if current != frozen:
            reject()
        module.__file__ = path
        module.__package__ = self.name.rpartition(".")[0]
        exec(compile(current.decode("utf-8"), path, "exec", dont_inherit=True), module.__dict__)

class Finder(importlib.abc.MetaPathFinder):
    def find_spec(self, fullname, path=None, target=None):
        if fullname in bindings:
            return importlib.util.spec_from_loader(fullname, Loader(fullname))
        local_file = os.path.join(root, *fullname.split(".")) + ".py"
        local_package = os.path.join(root, *fullname.split("."), "__init__.py")
        if os.path.isfile(local_file) or os.path.isfile(local_package):
            raise ImportError("undeclared repository-local import")
        return None

sys.meta_path.insert(0, Finder())

real_open = builtins.open
real_io_open = io.open
def guarded_open(file, mode="r", *args, **kwargs):
    if isinstance(file, int):
        return real_open(file, mode, *args, **kwargs)
    try:
        value = os.fspath(file)
    except TypeError:
        reject()
    absolute = os.path.realpath(
        value if os.path.isabs(value) else os.path.join(root, value)
    )
    try:
        inside = os.path.commonpath((root, absolute)) == root
    except ValueError:
        inside = False
    if inside and (
        absolute not in allowed_repo_reads or any(flag in mode for flag in "wax+")
    ):
        raise PermissionError("undeclared repository-local file access")
    return real_open(file, mode, *args, **kwargs)
def guarded_io_open(file, mode="r", *args, **kwargs):
    if isinstance(file, int):
        return real_io_open(file, mode, *args, **kwargs)
    try:
        value = os.fspath(file)
    except TypeError:
        reject()
    absolute = os.path.realpath(
        value if os.path.isabs(value) else os.path.join(root, value)
    )
    try:
        inside = os.path.commonpath((root, absolute)) == root
    except ValueError:
        inside = False
    if inside and (
        absolute not in allowed_repo_reads or any(flag in mode for flag in "wax+")
    ):
        raise PermissionError("undeclared repository-local file access")
    return real_io_open(file, mode, *args, **kwargs)
builtins.open = guarded_open
io.open = guarded_io_open

class RecordingResult(unittest.TextTestResult):
    def startTest(self, test):
        super().startTest(test)
        self._current_id = test.id()
    def addSuccess(self, test):
        super().addSuccess(test)
        statuses.append({"caseId": test.id(), "status": "Passed"})
    def addFailure(self, test, err):
        super().addFailure(test, err)
        statuses.append({"caseId": test.id(), "status": "Failed"})
    def addError(self, test, err):
        super().addError(test, err)
        statuses.append({"caseId": test.id(), "status": "Failed"})
    def addSkip(self, test, reason):
        super().addSkip(test, reason)
        statuses.append({"caseId": test.id(), "status": "Skipped"})
    def addExpectedFailure(self, test, err):
        super().addExpectedFailure(test, err)
        statuses.append({"caseId": test.id(), "status": "Skipped"})
    def addUnexpectedSuccess(self, test):
        super().addUnexpectedSuccess(test)
        statuses.append({"caseId": test.id(), "status": "Failed"})

suite = unittest.defaultTestLoader.loadTestsFromNames(arguments)
def flatten(value):
    if isinstance(value, unittest.TestSuite):
        result = []
        for child in value:
            result.extend(flatten(child))
        return result
    return [value]
discovered = [test.id() for test in flatten(suite)]
if discovered != arguments:
    reject()
statuses = []
result = unittest.TextTestRunner(
    stream=sys.stdout, verbosity=1, resultclass=RecordingResult
).run(suite)
if [item["caseId"] for item in statuses] != discovered:
    reject()
passed = sum(item["status"] == "Passed" for item in statuses)
failed = sum(item["status"] == "Failed" for item in statuses)
skipped = sum(item["status"] == "Skipped" for item in statuses)
success = result.wasSuccessful() and failed == 0 and skipped == 0
payload = {
    "protocol": RESULT_PROTOCOL,
    "status": "Passed" if success else "Failed",
    "executedCases": statuses,
    "counts": {
        "discovered": len(statuses), "passed": passed,
        "failed": failed, "skipped": skipped,
    },
}
print(RESULT_MARKER + canonical(payload).decode("utf-8"))
raise SystemExit(0 if success else 1)
'''

_SEMANTIC_SITE_PROBE_SOURCE = r'''
import hashlib
import json
import os
import sys
import sysconfig

PROTOCOL = "week84-92-semantic-site-modules-v1"
MARKER = "CAICLI_SEMANTIC_SITE_MODULES="
sys.dont_write_bytecode = True
purelib_value = sysconfig.get_path("purelib")
if not isinstance(purelib_value, str) or not os.path.isabs(purelib_value):
    raise SystemExit(97)
purelib = os.path.realpath(purelib_value)
if purelib != os.path.abspath(purelib_value) or not os.path.isdir(purelib):
    raise SystemExit(97)
sys.path.append(purelib)
import jsonschema

paths = set()
for module in tuple(sys.modules.values()):
    for attribute in ("__file__", "__cached__"):
        value = getattr(module, attribute, None)
        if not isinstance(value, str):
            continue
        absolute = os.path.realpath(value)
        try:
            inside = os.path.commonpath((purelib, absolute)) == purelib
        except ValueError:
            inside = False
        if not inside or not os.path.isfile(absolute):
            continue
        relative = os.path.relpath(absolute, purelib).replace(os.sep, "/")
        if relative.startswith("../") or relative in {"", ".", ".."}:
            raise SystemExit(97)
        paths.add(relative)
payload = {"protocol": PROTOCOL, "paths": sorted(paths)}
print(MARKER + json.dumps(payload, ensure_ascii=False, sort_keys=True,
                          separators=(",", ":"), allow_nan=False))
'''

_SEMANTIC_WRAPPER_SOURCE = r'''
import builtins
import hashlib
import importlib.machinery
import importlib.util
import json
import io
import os
from pathlib import Path
import sys
import sysconfig
import tempfile
import types
import unittest

LOADER_PROTOCOL = "week84-92-isolated-repo-tools-loader-v1"
SITE_PROTOCOL = "week84-92-semantic-site-modules-v1"
SITE_MARKER = "CAICLI_SEMANTIC_SITE_MODULES="
ROOT_ENV = "CAICLI_GOAL_REPO_ROOT"
ENTRIES = (
    ("tools.week84_92_goal_integrity", "tools/week84_92_goal_integrity.py"),
    ("tools.week84_92_evidence_anchor", "tools/week84_92_evidence_anchor.py"),
    ("tools.week84_92_trusted_executor", "tools/week84_92_trusted_executor.py"),
    ("tools.week84_92_provider_turn_harness", "tools/week84_92_provider_turn_harness.py"),
    ("tools.week84_92_goal_validator", "tools/validate-week84-92-goal-evidence.py"),
    ("tools.test_validate_week84_92_goal_evidence", "tools/test_validate_week84_92_goal_evidence.py"),
    ("tools.test_week84_92_goal_integrity", "tools/test_week84_92_goal_integrity.py"),
    ("tools.test_week84_92_gate_requirements", "tools/test_week84_92_gate_requirements.py"),
    ("tools.test_week84_92_evidence_anchor", "tools/test_week84_92_evidence_anchor.py"),
    ("tools.test_week84_92_trusted_executor", "tools/test_week84_92_trusted_executor.py"),
)
READ_SOURCES = (
    "tools/week84_92_commands/goal-contract-unittest.py",
    "tools/week84_92_commands/goal-control-schema-validate.py",
    "tools/week84_92_commands/prior-handoff-schema-validate.py",
    "tools/week84_92_commands/trusted-executor-unittest.py",
    "tools/week84_92_commands/week83-lineage-identity-verify.py",
)
TEST_NAMES = tuple(name for name, _path in ENTRIES if name.startswith("tools.test_"))
ALLOWED_SITE_ROOTS = (
    ("attr", "directory"), ("attrs", "directory"),
    ("idna", "directory"), ("jsonpointer.py", "file"),
    ("jsonschema", "directory"),
    ("jsonschema_specifications", "directory"),
    ("referencing", "directory"),
    ("rfc3339_validator.py", "file"),
    ("rfc3986_validator.py", "file"),
    ("rpds", "directory"), ("six.py", "file"),
)
sys.dont_write_bytecode = True

def canonical(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True,
                      separators=(",", ":"), allow_nan=False).encode("utf-8")

raw_descriptor = sys.stdin.buffer.read(1024 * 1024 + 1)
if len(raw_descriptor) > 1024 * 1024:
    raise SystemExit(97)
try:
    descriptor = json.loads(raw_descriptor.decode("utf-8"))
except (UnicodeDecodeError, json.JSONDecodeError):
    raise SystemExit(97)
expected_descriptor_keys = {
    "protocol", "repoRoot", "entrySources", "readSources"
}
if (
    not isinstance(descriptor, dict)
    or set(descriptor) != expected_descriptor_keys
    or raw_descriptor != canonical(descriptor)
    or descriptor.get("protocol") != LOADER_PROTOCOL
):
    raise SystemExit(97)
declared_sources = descriptor.get("entrySources")
if not isinstance(declared_sources, list) or len(declared_sources) != len(ENTRIES):
    raise SystemExit(97)
declared_reads = descriptor.get("readSources")
if not isinstance(declared_reads, list) or len(declared_reads) != len(READ_SOURCES):
    raise SystemExit(97)

root_value = os.environ.pop(ROOT_ENV, None)
if (
    not isinstance(root_value, str)
    or root_value != descriptor.get("repoRoot")
    or not os.path.isabs(root_value)
):
    raise SystemExit(97)
root = Path(os.path.realpath(root_value))
if str(root) != os.path.abspath(root_value) or not root.is_dir():
    raise SystemExit(97)
tool_root = root / "tools"
if not tool_root.is_dir() or tool_root.is_symlink():
    raise SystemExit(97)

purelib_value = sysconfig.get_path("purelib")
if not isinstance(purelib_value, str) or not os.path.isabs(purelib_value):
    raise SystemExit(97)
purelib = os.path.realpath(purelib_value)
if purelib != os.path.abspath(purelib_value) or not os.path.isdir(purelib):
    raise SystemExit(97)
try:
    if os.path.commonpath((str(root), purelib)) == str(root):
        raise SystemExit(97)
except ValueError:
    pass
pycache_guard = tempfile.TemporaryDirectory(prefix="caicli-semantic-pycache-")
pycache_root = os.path.realpath(pycache_guard.name)
try:
    if os.path.commonpath((str(root), pycache_root)) == str(root):
        raise SystemExit(97)
except ValueError:
    pass
sys.pycache_prefix = pycache_root
sys.path.append(purelib)
import jsonschema

tools_package = types.ModuleType("tools")
tools_package.__package__ = "tools"
tools_package.__path__ = []
tools_package.__spec__ = importlib.machinery.ModuleSpec(
    "tools", loader=None, is_package=True
)
sys.modules["tools"] = tools_package

source_by_path = {}
for declared, expected in zip(declared_sources, ENTRIES):
    if (
        not isinstance(declared, dict)
        or set(declared) != {"moduleName", "path", "sha256"}
        or declared.get("moduleName") != expected[0]
        or declared.get("path") != expected[1]
        or not isinstance(declared.get("sha256"), str)
        or len(declared["sha256"]) != 64
        or any(character not in "0123456789abcdef" for character in declared["sha256"])
    ):
        raise SystemExit(97)
    source_by_path[expected[1]] = declared
for declared, expected_path in zip(declared_reads, READ_SOURCES):
    if (
        not isinstance(declared, dict)
        or set(declared) != {"path", "sha256"}
        or declared.get("path") != expected_path
        or not isinstance(declared.get("sha256"), str)
        or len(declared["sha256"]) != 64
        or any(character not in "0123456789abcdef" for character in declared["sha256"])
    ):
        raise SystemExit(97)
    source_by_path[expected_path] = declared

real_open = open
real_spec_from_file_location = importlib.util.spec_from_file_location

def read_declared(relative):
    declared = source_by_path.get(relative)
    if declared is None:
        raise SystemExit(97)
    target = root.joinpath(*relative.split("/"))
    if (
        not target.is_file()
        or target.is_symlink()
        or target.resolve() != target
        or target.parent.resolve() != target.parent
    ):
        raise SystemExit(97)
    with real_open(target, "rb") as handle:
        raw = handle.read(16 * 1024 * 1024 + 1)
    if (
        len(raw) > 16 * 1024 * 1024
        or hashlib.sha256(raw).hexdigest() != declared["sha256"]
    ):
        raise SystemExit(97)
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        raise SystemExit(97)
    return target, text

class RawDeclaredLoader:
    def __init__(self, name, relative):
        self.name = name
        self.relative = relative
    def create_module(self, spec):
        return None
    def exec_module(self, module):
        target, text = read_declared(self.relative)
        module.__file__ = str(target)
        exec(compile(text, str(target), "exec"), module.__dict__)

def guarded_spec_from_file_location(name, location, *args, **kwargs):
    absolute = Path(os.path.realpath(os.fspath(location)))
    try:
        inside = os.path.commonpath((str(root), str(absolute))) == str(root)
    except ValueError:
        inside = False
    if not inside:
        return real_spec_from_file_location(name, location, *args, **kwargs)
    relative = absolute.relative_to(root).as_posix()
    declared = source_by_path.get(relative)
    if declared is None:
        raise ImportError("undeclared repository-local source")
    loader = RawDeclaredLoader(name, relative)
    spec = importlib.machinery.ModuleSpec(name, loader, origin=str(absolute))
    spec.has_location = True
    return spec

importlib.util.spec_from_file_location = guarded_spec_from_file_location

loaded = {}
for module_name, relative in ENTRIES:
    target, text = read_declared(relative)
    loader = RawDeclaredLoader(module_name, relative)
    spec = importlib.machinery.ModuleSpec(module_name, loader, origin=str(target))
    spec.has_location = True
    module = importlib.util.module_from_spec(spec)
    sys.modules[module_name] = module
    setattr(tools_package, module_name.rsplit(".", 1)[1], module)
    loader.exec_module(module)
    if Path(module.__file__).resolve() != target:
        raise SystemExit(97)
    loaded[module_name] = module

real_builtin_open = builtins.open
real_io_open = io.open
def guarded_repo_python_open(real_function, file, mode="r", *args, **kwargs):
    if isinstance(file, int):
        return real_function(file, mode, *args, **kwargs)
    try:
        absolute = Path(os.path.realpath(os.fspath(file)))
        inside = os.path.commonpath((str(root), str(absolute))) == str(root)
    except (TypeError, ValueError):
        inside = False
    if inside and absolute.suffix.casefold() == ".py":
        relative = absolute.relative_to(root).as_posix()
        if any(flag in mode for flag in "wax+") or relative not in source_by_path:
            raise PermissionError("undeclared repository-local Python access")
        read_declared(relative)
    return real_function(file, mode, *args, **kwargs)
def guarded_builtin_open(file, mode="r", *args, **kwargs):
    return guarded_repo_python_open(real_builtin_open, file, mode, *args, **kwargs)
def guarded_io_open(file, mode="r", *args, **kwargs):
    return guarded_repo_python_open(real_io_open, file, mode, *args, **kwargs)
builtins.open = guarded_builtin_open
io.open = guarded_io_open

validator = loaded["tools.week84_92_goal_validator"]
semantic_exit = validator.run_cli(["--repo-root", str(root)])
suite = unittest.defaultTestLoader.loadTestsFromNames(list(TEST_NAMES))
result = unittest.TextTestRunner(stream=sys.stdout, verbosity=1).run(suite)

for _module_name, relative in ENTRIES:
    read_declared(relative)
for relative in READ_SOURCES:
    read_declared(relative)

paths = set()
allowed_module_tops = {
    "attr", "attrs", "idna", "jsonpointer", "jsonschema",
    "jsonschema_specifications", "referencing", "rfc3339_validator",
    "rfc3986_validator", "rpds", "six",
}
def allowed_site_relative(relative):
    return any(
        relative == root_name
        if root_kind == "file"
        else relative.startswith(root_name + "/")
        for root_name, root_kind in ALLOWED_SITE_ROOTS
    )
for required_name in sorted(allowed_module_tops):
    required_module = sys.modules.get(required_name)
    if required_module is None:
        raise SystemExit(97)
    required_spec = getattr(required_module, "__spec__", None)
    required_origin = getattr(required_spec, "origin", None)
    if not isinstance(required_origin, str):
        raise SystemExit(97)
    required_absolute = os.path.realpath(required_origin)
    try:
        required_inside = (
            os.path.commonpath((purelib, required_absolute)) == purelib
        )
    except ValueError:
        required_inside = False
    if not required_inside or not os.path.isfile(required_absolute):
        raise SystemExit(97)

for module in tuple(sys.modules.values()):
    spec = getattr(module, "__spec__", None)
    candidates = [getattr(module, "__file__", None),
                  getattr(module, "__cached__", None),
                  getattr(spec, "origin", None)]
    for value in candidates:
        if not isinstance(value, str):
            continue
        if value in {"built-in", "frozen", "namespace"}:
            continue
        absolute = os.path.realpath(value)
        try:
            inside = os.path.commonpath((purelib, absolute)) == purelib
        except ValueError:
            inside = False
        if not inside:
            continue
        if not os.path.isfile(absolute):
            raise SystemExit(97)
        relative = os.path.relpath(absolute, purelib).replace(os.sep, "/")
        if (
            relative.startswith("../")
            or relative in {"", ".", ".."}
            or not allowed_site_relative(relative)
        ):
            raise SystemExit(97)
        paths.add(relative)
    locations = []
    module_path = getattr(module, "__path__", None)
    if module_path is not None:
        locations.extend(list(module_path))
    spec_locations = getattr(spec, "submodule_search_locations", None)
    if spec_locations is not None:
        locations.extend(list(spec_locations))
    for value in locations:
        if not isinstance(value, str):
            continue
        absolute = os.path.realpath(value)
        try:
            inside = os.path.commonpath((purelib, absolute)) == purelib
        except ValueError:
            inside = False
        if not inside:
            continue
        if not os.path.isdir(absolute):
            raise SystemExit(97)
        relative = os.path.relpath(absolute, purelib).replace(os.sep, "/")
        if not any(
            root_kind == "directory"
            and (relative == root_name or relative.startswith(root_name + "/"))
            for root_name, root_kind in ALLOWED_SITE_ROOTS
        ):
            raise SystemExit(97)
payload = {"protocol": SITE_PROTOCOL, "paths": sorted(paths)}
print(SITE_MARKER + json.dumps(payload, ensure_ascii=False, sort_keys=True,
                               separators=(",", ":"), allow_nan=False))
pycache_guard.cleanup()
raise SystemExit(0 if semantic_exit == 0 and result.wasSuccessful() else 1)
'''

_SEMANTIC_ARGUMENTS = (
    "-I",
    "-S",
    "-E",
    "-B",
    "-X",
    "utf8",
    "-c",
    _SEMANTIC_WRAPPER_SOURCE,
)
SEMANTIC_REDACTED_INVOCATION = (
    "python-current -I -S -E -B -X utf8 -c "
    "<week84-92-isolated-repo-tools-loader-v1>"
)

_PRODUCT_TEMPLATE_ARGUMENTS = (
    "-I",
    "-S",
    "-E",
    "-B",
    "-X",
    "utf8",
    "-c",
    _ADAPTER_WRAPPER_SOURCE,
    "{scriptPath}",
    "{scriptSha256}",
)
_VERIFIER_ARGUMENTS = (
    "-I",
    "-S",
    "-E",
    "-B",
    "-X",
    "utf8",
    "-c",
    _VERIFIER_WRAPPER_SOURCE,
)
_COMMAND_SLUG = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
_SAFE_ATTEMPT_ID = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+){0,15}$")

_SAFE_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")
_SAFE_RUN_ID = _SAFE_ID
_SHA1 = re.compile(r"^[0-9a-f]{40}$")
_SHA256 = re.compile(r"^[0-9a-f]{64}$")
_LEASE_IDS = {
    "W84-G8": re.compile(r"^CW-W84-[A-Z0-9]{8,32}$"),
    "W92-G7": re.compile(r"^CW-W92-[A-Z0-9]{8,32}$"),
}
_PREAUTHORIZATION_IDS = {
    "W84-G8": re.compile(r"^CWPA-W84-[A-Z0-9]{8,32}$"),
    "W92-G7": re.compile(r"^CWPA-W92-[A-Z0-9]{8,32}$"),
}
_WORKSPACE_IDS = {
    "W84-G8": re.compile(r"^CWW-W84-[A-Z0-9]{8,32}$"),
    "W92-G7": re.compile(r"^CWW-W92-[A-Z0-9]{8,32}$"),
}
_APPROVAL_IDS = {
    gate_id: {
        action: re.compile(
            rf"^CWAD-{'W84' if gate_id == 'W84-G8' else 'W92'}-"
            rf"{'PATCH' if action == 'apply_patch' else 'SHELL'}-[A-Z0-9]{{8,32}}$"
        )
        for action in ("apply_patch", "shell")
    }
    for gate_id in CONTROLLED_WRITE_TOMBSTONES
}
_SECRET_PATTERNS = (
    re.compile(r"(?i)(?:OPENAI_API_KEY|API[_-]?KEY)\s*[:=]\s*\S+"),
    re.compile(r"(?i)authorization\s*:\s*bearer\s+\S+"),
    re.compile(r"(?<![A-Za-z0-9])sk-[A-Za-z0-9_-]{8,}"),
    re.compile(r"(?i)(?:password|secret|access[_-]?token)\s*[:=]\s*\S+"),
    re.compile(r"(?<![A-Za-z0-9])(?:ghp_|github_pat_|xox[baprs]-)[A-Za-z0-9_-]{8,}"),
    re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
)
_SENSITIVE_ENV_NAME = re.compile(
    r"(?i)(?:api[_-]?key|password|secret|token|credential|private[_-]?key)"
)

MAX_JSON_BYTES = 8 * 1024 * 1024
MAX_ENV_BYTES = 64 * 1024
MAX_TEST_STREAM_BYTES = 4 * 1024 * 1024
MAX_CONTAINED_CHILD_REQUEST_BYTES = 128 * 1024
DEFAULT_CHILD_TIMEOUT_SECONDS = 30 * 60
LOCK_TIMEOUT_SECONDS = 30.0
_REPARSE_ATTRIBUTE = 0x400

# Bootstrap-owned code runs first and blocks on stdin.  The parent assigns this
# trampoline to a kill-on-close Windows Job before releasing any candidate
# command.  Job breakaway is not enabled, so descendants inherit containment.
_CONTAINED_CHILD_PROTOCOL = "week84-92-contained-child-v1"
_CONTAINED_CHILD_TRAMPOLINE = r'''import json
import base64
import hashlib
import os
import subprocess
import sys

LIMIT = 131072
PROTOCOL = "week84-92-contained-child-v1"

def reject():
    raise SystemExit(126)

raw = sys.stdin.buffer.readline(LIMIT + 1)
if not raw.endswith(b"\n") or len(raw) > LIMIT:
    reject()
if sys.stdin.buffer.read(1) != b"":
    reject()
try:
    document = json.loads(raw[:-1].decode("utf-8"))
except (UnicodeDecodeError, ValueError):
    reject()
if (
    not isinstance(document, dict)
    or set(document) != {"command", "protocol", "stdinBase64", "stdinSha256"}
    or document.get("protocol") != PROTOCOL
):
    reject()
command = document.get("command")
stdin_base64 = document.get("stdinBase64")
stdin_sha256 = document.get("stdinSha256")
if (
    not isinstance(command, list)
    or not command
    or any(not isinstance(item, str) or not item or "\x00" in item for item in command)
    or not isinstance(stdin_base64, str)
    or not isinstance(stdin_sha256, str)
    or len(stdin_sha256) != 64
):
    reject()
try:
    stdin_raw = base64.b64decode(stdin_base64.encode("ascii"), validate=True)
except (UnicodeEncodeError, ValueError):
    reject()
if hashlib.sha256(stdin_raw).hexdigest() != stdin_sha256:
    reject()
canonical = json.dumps(
    document,
    ensure_ascii=False,
    sort_keys=True,
    separators=(",", ":"),
    allow_nan=False,
).encode("utf-8")
if canonical != raw[:-1]:
    reject()
kwargs = {
    "stdin": subprocess.PIPE if stdin_raw else subprocess.DEVNULL,
    "shell": False,
    "close_fds": True,
}
if os.name == "nt":
    kwargs["creationflags"] = subprocess.CREATE_NO_WINDOW
try:
    child = subprocess.Popen(command, **kwargs)
except OSError:
    raise SystemExit(127)
try:
    if stdin_raw:
        code = child.communicate(input=stdin_raw)[0]
        code = child.returncode
    else:
        code = child.wait()
except BaseException:
    try:
        child.kill()
        child.wait()
    except BaseException:
        pass
    raise
raise SystemExit(code if isinstance(code, int) and 0 <= code <= 255 else 127)
'''


class ExecutorError(RuntimeError):
    """Sanitised, stable failure from the trusted execution boundary."""

    def __init__(self, code: str):
        self.code = code
        super().__init__(f"trusted executor failure [{code}]")


class _DuplicateKeyError(ValueError):
    pass


@dataclass(frozen=True)
class CheckoutSnapshot:
    head_commit: str
    tree_object_id: str
    git_status_porcelain_v1: str

    @property
    def clean(self) -> bool:
        return self.git_status_porcelain_v1 == ""

    def as_document(self) -> dict[str, str]:
        return {
            "headCommit": self.head_commit,
            "treeObjectId": self.tree_object_id,
            "gitStatusPorcelainV1": self.git_status_porcelain_v1,
            "trackedStatus": "Clean" if self.clean else "Dirty",
        }


@dataclass(frozen=True)
class _RepositoryContext:
    candidate_root: Path
    candidate_git_dir: Path
    common_dir: Path
    candidate_branch_ref: str
    control_root: Path
    control_git_dir: Path


@dataclass(frozen=True)
class ReservationBatch:
    gate_id: str
    product_candidate: str
    attempt_id: str
    run_id: str
    phases: tuple[str, ...]
    reservation_ids: tuple[str, ...]
    reservation_sequences: tuple[int, ...]


@dataclass(frozen=True)
class ProviderRunResult:
    exit_code: int
    batch: ReservationBatch
    attempt_finished: bool
    used_turns: int
    remaining_turns: int
    runtime_journal_path: str = PROVIDER_RUNTIME_JOURNAL_PATH


@dataclass(frozen=True)
class PackageTreeBinding:
    path: str
    sha256: str
    package_root: str
    tree_root_sha256: str
    entry_count: int
    total_bytes: int
    entrypoint: str
    entrypoint_sha256: str
    argv: tuple[str, ...]
    apphost_path: str
    apphost_sha256: str

    def descriptor_binding(self) -> dict[str, Any]:
        return {
            "path": self.path,
            "sha256": self.sha256,
            "treeRootSha256": self.tree_root_sha256,
            "entrypoint": self.entrypoint,
            "argv": list(self.argv),
        }


@dataclass(frozen=True)
class ProviderBoundaryBinding:
    path: str
    sha256: str
    first_add_commit: str
    decision_id: str
    mode: str
    package_tree_root_sha256: str
    sandbox_policy_sha256: str | None

    def descriptor_binding(self) -> dict[str, Any]:
        return {
            "path": self.path,
            "sha256": self.sha256,
            "firstAddCommit": self.first_add_commit,
            "decisionId": self.decision_id,
            "mode": self.mode,
            "packageTreeRootSha256": self.package_tree_root_sha256,
        }


@dataclass(frozen=True)
class PreauthorizationBinding:
    path: str
    sha256: str
    commit: str
    precondition_gate_bindings: tuple[dict[str, Any], ...]
    decision_paths: tuple[str, ...]


@dataclass(frozen=True)
class TombstoneBinding:
    path: str
    sha256: str
    commit: str
    preauthorization: PreauthorizationBinding


@dataclass(frozen=True)
class TestRunResult:
    exit_code: int
    report_path: str
    stdout_path: str
    stderr_path: str
    report_sha256: str
    attempt_id: str
    test_counts: dict[str, int]


@dataclass(frozen=True)
class ProductRunResult:
    exit_code: int
    report_path: str
    stdout_path: str
    stderr_path: str
    report_sha256: str
    verifier_report_paths: tuple[str, ...] = ()
    verifier_report_sha256s: tuple[str, ...] = ()
    selection_path: str | None = None
    selection_sha256: str | None = None
    execution_path: str | None = None
    execution_sha256: str | None = None


@dataclass(frozen=True)
class CommandControlBinding:
    document: dict[str, Any]
    binding: dict[str, Any]
    sources: tuple[dict[str, Any], ...]
    source_trees: tuple[dict[str, Any], ...]


def canonical_json_bytes(value: Any) -> bytes:
    """Return the frozen canonical JSON representation."""

    return json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
        allow_nan=False,
    ).encode("utf-8")


def provider_entry_sha256(entry: Mapping[str, Any]) -> str:
    payload = dict(entry)
    payload.pop("entrySha256", None)
    return hashlib.sha256(canonical_json_bytes(payload)).hexdigest()


def attempt_event_sha256(event: Mapping[str, Any]) -> str:
    payload = dict(event)
    payload.pop("attemptEventSha256", None)
    return hashlib.sha256(canonical_json_bytes(payload)).hexdigest()


def runtime_event_sha256(event: Mapping[str, Any]) -> str:
    payload = dict(event)
    payload.pop("eventSha256", None)
    return hashlib.sha256(canonical_json_bytes(payload)).hexdigest()


def _pairs_without_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise _DuplicateKeyError
        result[key] = value
    return result


def _read_json_bytes(raw: bytes, error_code: str) -> Any:
    if len(raw) > MAX_JSON_BYTES:
        raise ExecutorError(error_code)
    try:
        text = raw.decode("utf-8")
        return json.loads(text, object_pairs_hook=_pairs_without_duplicates)
    except (UnicodeDecodeError, json.JSONDecodeError, _DuplicateKeyError):
        raise ExecutorError(error_code) from None


def _utc_now() -> str:
    return (
        datetime.now(timezone.utc)
        .isoformat(timespec="microseconds")
        .replace("+00:00", "Z")
    )


def _parse_timestamp(value: Any, error_code: str) -> datetime:
    if not isinstance(value, str) or not value.endswith("Z"):
        raise ExecutorError(error_code)
    try:
        parsed = datetime.fromisoformat(value[:-1] + "+00:00")
    except ValueError:
        raise ExecutorError(error_code) from None
    if parsed.tzinfo is None or parsed.utcoffset() != timedelta(0):
        raise ExecutorError(error_code)
    return parsed


def _timestamp_after(value: Any) -> str:
    prior = _parse_timestamp(value, "LEDGER_TIMESTAMP")
    current = datetime.now(timezone.utc)
    if current <= prior:
        current = prior + timedelta(microseconds=1)
    return current.isoformat(timespec="microseconds").replace("+00:00", "Z")


def _is_int(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _is_reparse_or_link(path: Path) -> bool:
    try:
        info = os.lstat(path)
    except OSError:
        raise ExecutorError("PATH_INSPECTION") from None
    if stat.S_ISLNK(info.st_mode):
        return True
    return bool(getattr(info, "st_file_attributes", 0) & _REPARSE_ATTRIBUTE)


def _path_is_within(parent: Path, child: Path) -> bool:
    try:
        return os.path.commonpath((str(parent), str(child))) == str(parent)
    except ValueError:
        return False


def _trusted_git_candidates() -> tuple[Path, ...]:
    values = [Path(TRUSTED_GIT_CORE_PATH)]
    executable_names = ("git.exe",) if os.name == "nt" else ("git",)
    for raw_directory in _BOOTSTRAP_EXECUTION_PATH.split(os.pathsep):
        if not raw_directory:
            continue
        directory = Path(raw_directory)
        if not directory.is_absolute():
            continue
        values.extend(directory / name for name in executable_names)
    unique: list[Path] = []
    seen: set[str] = set()
    for value in values:
        key = os.path.normcase(str(value))
        if key not in seen:
            seen.add(key)
            unique.append(value)
    return tuple(unique)


def _read_trusted_git_candidate(path: Path) -> tuple[bytes, tuple[int, int, int, int]]:
    flags = os.O_RDONLY
    if hasattr(os, "O_BINARY"):
        flags |= os.O_BINARY
    if hasattr(os, "O_NOFOLLOW"):
        flags |= os.O_NOFOLLOW
    descriptor: int | None = None
    try:
        descriptor = os.open(path, flags)
        opened_before = os.fstat(descriptor)
        lexical_before = os.lstat(path)
        if (
            not stat.S_ISREG(opened_before.st_mode)
            or not os.path.samestat(opened_before, lexical_before)
            or opened_before.st_size != TRUSTED_GIT_EXECUTABLE_BYTES
            or _is_reparse_or_link(path)
        ):
            raise ExecutorError("GIT_TOOL_IDENTITY")
        raw = bytearray()
        while len(raw) <= TRUSTED_GIT_EXECUTABLE_BYTES:
            block = os.read(descriptor, 1024 * 1024)
            if not block:
                break
            raw.extend(block)
        opened_after = os.fstat(descriptor)
        lexical_after = os.lstat(path)
        if (
            len(raw) != TRUSTED_GIT_EXECUTABLE_BYTES
            or not os.path.samestat(opened_before, opened_after)
            or not os.path.samestat(opened_after, lexical_after)
            or getattr(opened_before, "st_mtime_ns", None)
            != getattr(opened_after, "st_mtime_ns", None)
        ):
            raise ExecutorError("GIT_TOOL_IDENTITY")
    except ExecutorError:
        raise
    except OSError:
        raise ExecutorError("GIT_TOOL_IDENTITY") from None
    finally:
        if descriptor is not None:
            os.close(descriptor)
    identity = (
        int(opened_after.st_dev),
        int(opened_after.st_ino),
        int(opened_after.st_size),
        int(getattr(opened_after, "st_mtime_ns", 0)),
    )
    return bytes(raw), identity


def _trusted_git_identity(
    repo_root: Path,
    *,
    rehash: bool = False,
) -> tuple[Path, dict[str, Any]]:
    global _TRUSTED_GIT_CACHE

    lexical_repo = Path(os.path.abspath(repo_root))
    lexical_cwd = Path(os.path.abspath(Path.cwd()))
    with _TRUSTED_GIT_CACHE_LOCK:
        cached = _TRUSTED_GIT_CACHE
        if cached is not None and not rehash:
            path, expected_stat, document = cached
            try:
                current = os.lstat(path)
                current_stat = (
                    int(current.st_dev),
                    int(current.st_ino),
                    int(current.st_size),
                    int(getattr(current, "st_mtime_ns", 0)),
                )
            except OSError:
                raise ExecutorError("GIT_TOOL_IDENTITY") from None
            if (
                current_stat != expected_stat
                or _path_is_within(lexical_repo, path)
                or _path_is_within(lexical_cwd, path)
                or _is_reparse_or_link(path)
            ):
                raise ExecutorError("GIT_TOOL_IDENTITY")
            return path, dict(document)

        for candidate in _trusted_git_candidates():
            try:
                lexical = Path(os.path.abspath(candidate))
                resolved = lexical.resolve(strict=True)
                if (
                    lexical != resolved
                    or _path_is_within(lexical_repo, resolved)
                    or _path_is_within(lexical_cwd, resolved)
                    or not resolved.is_file()
                ):
                    continue
                for ancestor in (resolved, *resolved.parents[:-1]):
                    if _is_reparse_or_link(ancestor):
                        raise ExecutorError("GIT_TOOL_IDENTITY")
                raw, stat_identity = _read_trusted_git_candidate(resolved)
            except (ExecutorError, OSError):
                continue
            digest = hashlib.sha256(raw).hexdigest()
            if digest != TRUSTED_GIT_EXECUTABLE_SHA256:
                continue
            environment = {
                key: value
                for key, value in os.environ.items()
                if not key.upper().startswith("GIT_") and key.casefold() != "path"
            }
            environment["PATH"] = str(resolved.parent)
            try:
                version_result = subprocess.run(
                    [str(resolved), "--version"],
                    cwd=resolved.parent,
                    env=environment,
                    stdin=subprocess.DEVNULL,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.DEVNULL,
                    shell=False,
                    timeout=10,
                    check=False,
                )
                version_text = version_result.stdout.decode("ascii").strip()
            except (OSError, subprocess.SubprocessError, UnicodeDecodeError):
                continue
            if (
                version_result.returncode != 0
                or version_text != f"git version {TRUSTED_GIT_VERSION}"
            ):
                continue
            document = {
                "executableRole": TRUSTED_GIT_EXECUTABLE_ROLE,
                "locationRole": "pinned-core-git-outside-repository",
                "version": TRUSTED_GIT_VERSION,
                "executableBytes": TRUSTED_GIT_EXECUTABLE_BYTES,
                "executableSha256": TRUSTED_GIT_EXECUTABLE_SHA256,
            }
            _TRUSTED_GIT_CACHE = (resolved, stat_identity, document)
            return resolved, dict(document)
    raise ExecutorError("GIT_TOOL_UNAVAILABLE")


def _git_environment(git_executable: Path) -> dict[str, str]:
    child = {
        key: value
        for key, value in os.environ.items()
        if not key.upper().startswith("GIT_") and key.casefold() != "path"
    }
    child.update(
        {
            "GIT_ATTR_NOSYSTEM": "1",
            "GIT_CONFIG_GLOBAL": os.devnull,
            "GIT_CONFIG_NOSYSTEM": "1",
            "GIT_CONFIG_SYSTEM": os.devnull,
            "GIT_NO_REPLACE_OBJECTS": "1",
            "GIT_NO_LAZY_FETCH": "1",
            "GIT_OPTIONAL_LOCKS": "0",
            "GIT_TERMINAL_PROMPT": "0",
            "PATH": str(git_executable.parent),
        }
    )
    return child


def _git_command(repo_root: Path, arguments: Sequence[str]) -> list[str]:
    git_dir = _verify_git_metadata_paths(repo_root)
    git_executable, _identity = _trusted_git_identity(repo_root)
    hardened_arguments = list(arguments)
    if hardened_arguments and hardened_arguments[0] in {
        "diff",
        "diff-files",
        "diff-index",
        "diff-tree",
    }:
        hardened_arguments[1:1] = ["--no-ext-diff", "--no-textconv"]
    return [
        str(git_executable),
        f"--git-dir={git_dir}",
        f"--work-tree={repo_root}",
        "-c",
        "core.quotepath=false",
        "-c",
        "core.fsmonitor=false",
        "-c",
        "core.untrackedCache=false",
        "-c",
        f"core.attributesFile={os.devnull}",
        "-c",
        "diff.external=",
        "-c",
        "diff.trustExitCode=false",
        "-c",
        "submodule.recurse=false",
        "-c",
        "submodule.active=",
        "-c",
        "protocol.allow=never",
        *hardened_arguments,
    ]


def _git_metadata_path_is_present(path: Path) -> bool:
    return path.exists() or path.is_symlink()


def _read_git_pointer(path: Path, code: str) -> str:
    try:
        if (
            not path.is_file()
            or _is_reparse_or_link(path)
            or not stat.S_ISREG(os.lstat(path).st_mode)
            or os.lstat(path).st_size > 4096
        ):
            raise ExecutorError(code)
        raw = path.read_bytes()
    except OSError:
        raise ExecutorError(code) from None
    try:
        text = raw.decode("utf-8").strip()
    except UnicodeDecodeError:
        raise ExecutorError(code) from None
    if not text or "\x00" in text or len(text.splitlines()) != 1:
        raise ExecutorError(code)
    return text


def _resolved_git_path(base: Path, value: str) -> Path:
    requested = Path(value)
    if not requested.is_absolute():
        requested = base / requested
    lexical = Path(os.path.abspath(requested))
    try:
        resolved = lexical.resolve(strict=True)
    except OSError:
        raise ExecutorError("GIT_REPOSITORY_TRUST") from None
    if os.path.normcase(str(lexical)) != os.path.normcase(str(resolved)):
        raise ExecutorError("GIT_REPOSITORY_TRUST")
    return resolved


def _git_layout(repo_root: Path) -> tuple[Path, Path]:
    marker = repo_root / ".git"
    if marker.is_dir():
        if _is_reparse_or_link(marker):
            raise ExecutorError("GIT_REPOSITORY_TRUST")
        git_dir = _resolved_git_path(repo_root, str(marker))
        common_dir = git_dir
        if _git_metadata_path_is_present(git_dir / "commondir"):
            raise ExecutorError("GIT_REPOSITORY_TRUST")
    elif marker.is_file() and not _is_reparse_or_link(marker):
        pointer = _read_git_pointer(marker, "GIT_REPOSITORY_TRUST")
        if not pointer.startswith("gitdir: "):
            raise ExecutorError("GIT_REPOSITORY_TRUST")
        git_dir = _resolved_git_path(repo_root, pointer[8:])
        if not git_dir.is_dir() or _is_reparse_or_link(git_dir):
            raise ExecutorError("GIT_REPOSITORY_TRUST")
        common_value = _read_git_pointer(
            git_dir / "commondir", "GIT_REPOSITORY_TRUST"
        )
        common_dir = _resolved_git_path(git_dir, common_value)
        if (
            not common_dir.is_dir()
            or _is_reparse_or_link(common_dir)
            or git_dir.parent != common_dir / "worktrees"
        ):
            raise ExecutorError("GIT_REPOSITORY_TRUST")
        backlink = _read_git_pointer(
            git_dir / "gitdir", "GIT_REPOSITORY_TRUST"
        )
        backlink_path = Path(os.path.abspath(Path(backlink)))
        if os.path.normcase(str(backlink_path)) != os.path.normcase(str(marker)):
            raise ExecutorError("GIT_REPOSITORY_TRUST")
    else:
        raise ExecutorError("GIT_REPOSITORY_TRUST")
    return git_dir, common_dir


def _verify_git_metadata_paths(repo_root: Path) -> Path:
    git_dir, common_dir = _git_layout(repo_root)
    required_regular = (common_dir / "config", common_dir / "HEAD", git_dir / "HEAD")
    required_directories = (common_dir / "objects", common_dir / "refs")
    for path in required_regular:
        if (
            not path.is_file()
            or _is_reparse_or_link(path)
            or not stat.S_ISREG(os.lstat(path).st_mode)
        ):
            raise ExecutorError("GIT_REPOSITORY_TRUST")
    for path in required_directories:
        if not path.is_dir() or _is_reparse_or_link(path):
            raise ExecutorError("GIT_REPOSITORY_TRUST")
    index_path = git_dir / "index"
    if _git_metadata_path_is_present(index_path) and (
        not index_path.is_file()
        or _is_reparse_or_link(index_path)
        or not stat.S_ISREG(os.lstat(index_path).st_mode)
    ):
        raise ExecutorError("GIT_REPOSITORY_TRUST")
    forbidden = (
        common_dir / "config.worktree",
        common_dir / "shallow",
        common_dir / "info" / "grafts",
        common_dir / "objects" / "info" / "alternates",
    )
    if any(_git_metadata_path_is_present(path) for path in forbidden):
        raise ExecutorError("GIT_REPOSITORY_TRUST")
    replace_root = common_dir / "refs" / "replace"
    if _git_metadata_path_is_present(replace_root):
        if not replace_root.is_dir() or _is_reparse_or_link(replace_root):
            raise ExecutorError("GIT_REPOSITORY_TRUST")
        try:
            with os.scandir(replace_root) as entries:
                if any(entries):
                    raise ExecutorError("GIT_REPOSITORY_TRUST")
        except OSError:
            raise ExecutorError("GIT_REPOSITORY_TRUST") from None
    packed_refs = common_dir / "packed-refs"
    if _git_metadata_path_is_present(packed_refs):
        if (
            not packed_refs.is_file()
            or _is_reparse_or_link(packed_refs)
            or not stat.S_ISREG(os.lstat(packed_refs).st_mode)
        ):
            raise ExecutorError("GIT_REPOSITORY_TRUST")
        try:
            raw = packed_refs.read_bytes()
        except OSError:
            raise ExecutorError("GIT_REPOSITORY_TRUST") from None
        if len(raw) > MAX_JSON_BYTES or b"refs/replace/" in raw:
            raise ExecutorError("GIT_REPOSITORY_TRUST")
    return git_dir


def _git_context_query(repo_root: Path, arguments: Sequence[str]) -> bytes:
    _path, before_identity = _trusted_git_identity(repo_root)
    command = _git_command(repo_root, arguments)
    try:
        result = subprocess.run(
            command,
            cwd=repo_root,
            env=_git_environment(Path(command[0])),
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            shell=False,
            timeout=30,
            check=False,
        )
    except (OSError, subprocess.SubprocessError):
        raise ExecutorError("GIT_UNAVAILABLE") from None
    if result.returncode != 0 or len(result.stdout) > MAX_JSON_BYTES:
        raise ExecutorError("GIT_REPOSITORY_TRUST")
    _path, after_identity = _trusted_git_identity(repo_root)
    if after_identity != before_identity:
        raise ExecutorError("GIT_TOOL_DRIFT")
    return result.stdout


def _verify_git_local_configuration(repo_root: Path) -> None:
    """Reject repository configuration that can execute or hide external work."""

    raw = _git_context_query(
        repo_root,
        ["config", "--local", "--null", "--name-only", "--no-includes", "--list"],
    )
    try:
        keys = [item.decode("ascii") for item in raw.split(b"\0") if item]
    except UnicodeDecodeError:
        raise ExecutorError("GIT_CONFIG_POLICY") from None
    forbidden_exact = {
        "core.alternaterefscommand",
        "core.attributesfile",
        "core.fsmonitor",
        "core.hookspath",
        "core.sshcommand",
        "core.untrackedcache",
        "diff.external",
        "diff.trustexitcode",
        "protocol.allow",
        "submodule.recurse",
        "extensions.partialclone",
    }
    forbidden_prefixes = (
        "alias.",
        "diff.",
        "filter.",
        "include.",
        "includeif.",
        "protocol.",
    )
    for key in keys:
        folded = key.casefold()
        if (
            not key
            or folded != key
            or re.fullmatch(r"[a-z0-9][a-z0-9.-]*", key) is None
            or folded in forbidden_exact
            or any(folded.startswith(prefix) for prefix in forbidden_prefixes)
            or re.fullmatch(
                r"remote\..+\.(?:promisor|partialclonefilter)", folded
            )
            is not None
        ):
            raise ExecutorError("GIT_CONFIG_POLICY")


def _validate_safe_gitattributes(raw: bytes) -> str:
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        raise ExecutorError("GIT_ATTRIBUTES_POLICY") from None
    if "\x00" in text or "\r" in text.replace("\r\n", ""):
        raise ExecutorError("GIT_ATTRIBUTES_POLICY")
    normalised = text.replace("\r\n", "\n")
    for line in normalised.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        if (
            stripped.startswith("[attr]")
            or "\\" in stripped
            or '"' in stripped
            or "'" in stripped
        ):
            raise ExecutorError("GIT_ATTRIBUTES_POLICY")
        tokens = stripped.split()
        if len(tokens) < 2:
            raise ExecutorError("GIT_ATTRIBUTES_POLICY")
        for token in tokens[1:]:
            match = re.fullmatch(
                r"(?P<prefix>[-!]?)(?P<name>[A-Za-z0-9][A-Za-z0-9_.-]*)"
                r"(?:=(?P<value>[^\s]+))?",
                token,
            )
            if match is None:
                raise ExecutorError("GIT_ATTRIBUTES_POLICY")
            prefix = match.group("prefix")
            name = match.group("name").casefold()
            value = match.group("value")
            if name == "text":
                if value not in {None, "auto"} or (prefix and value is not None):
                    raise ExecutorError("GIT_ATTRIBUTES_POLICY")
            elif name == "eol":
                if prefix or value not in {"lf", "crlf"}:
                    raise ExecutorError("GIT_ATTRIBUTES_POLICY")
            else:
                # In particular: filter/process, diff/external drivers,
                # working-tree-encoding, ident, merge, and custom macros.
                raise ExecutorError("GIT_ATTRIBUTES_POLICY")
    return normalised


def _verify_repository_attribute_policy(repo_root: Path) -> None:
    raw_tree = _git_context_query(repo_root, ["ls-tree", "-r", "-z", "HEAD"])
    if any(
        record.startswith(b"160000 commit ")
        for record in raw_tree.split(b"\0")
        if record
    ):
        raise ExecutorError("GIT_SUBMODULE_UNSUPPORTED")
    raw_names = _git_context_query(
        repo_root, ["ls-tree", "-r", "--name-only", "-z", "HEAD"]
    )
    attributes_paths: list[str] = []
    worktree_attribute_candidates: set[str] = {".gitattributes"}
    for raw_path in raw_names.split(b"\0"):
        if not raw_path:
            continue
        try:
            relative = raw_path.decode("utf-8")
        except UnicodeDecodeError:
            raise ExecutorError("GIT_ATTRIBUTES_POLICY") from None
        try:
            canonical = PurePosixPath(*_relative_parts(relative)).as_posix()
        except ExecutorError:
            raise ExecutorError("GIT_ATTRIBUTES_POLICY") from None
        if canonical != relative:
            raise ExecutorError("GIT_ATTRIBUTES_POLICY")
        for parent in PurePosixPath(relative).parents:
            if parent == PurePosixPath("."):
                worktree_attribute_candidates.add(".gitattributes")
            else:
                worktree_attribute_candidates.add(
                    f"{parent.as_posix()}/.gitattributes"
                )
        basename = PurePosixPath(relative).name
        if basename.casefold() == ".gitattributes":
            if basename != ".gitattributes":
                raise ExecutorError("GIT_ATTRIBUTES_POLICY")
            attributes_paths.append(relative)
    tracked_paths = set(attributes_paths)
    for relative in worktree_attribute_candidates:
        lexical = repo_root.joinpath(*PurePosixPath(relative).parts)
        if lexical.exists() or lexical.is_symlink():
            attributes_paths.append(relative)
    attributes_paths = list(set(attributes_paths))
    for relative in sorted(attributes_paths, key=lambda value: value.encode("utf-8")):
        worktree_raw = _read_fixed_bytes(repo_root, relative, limit=MAX_JSON_BYTES)
        worktree_attributes = _validate_safe_gitattributes(worktree_raw)
        if relative in tracked_paths:
            tree_raw = _git_context_query(
                repo_root, ["ls-tree", "HEAD", "--", relative]
            )
            try:
                tree_line = tree_raw.decode("utf-8").strip()
            except UnicodeDecodeError:
                raise ExecutorError("GIT_ATTRIBUTES_POLICY") from None
            match = re.fullmatch(r"100644 blob ([0-9a-f]{40})\t.+", tree_line)
            if match is None:
                raise ExecutorError("GIT_ATTRIBUTES_POLICY")
            blob_raw = _git_context_query(
                repo_root, ["cat-file", "blob", match.group(1)]
            )
            if _validate_safe_gitattributes(blob_raw) != worktree_attributes:
                raise ExecutorError("GIT_ATTRIBUTES_POLICY")
    _git_dir, common_dir = _git_layout(repo_root)
    info_attributes = common_dir / "info" / "attributes"
    if _git_metadata_path_is_present(info_attributes):
        try:
            if (
                not info_attributes.is_file()
                or _is_reparse_or_link(info_attributes)
                or not stat.S_ISREG(os.lstat(info_attributes).st_mode)
                or info_attributes.read_bytes().strip()
            ):
                raise ExecutorError("GIT_ATTRIBUTES_POLICY")
        except OSError:
            raise ExecutorError("GIT_ATTRIBUTES_POLICY") from None


def _parse_worktree_list(raw: bytes) -> list[dict[str, str]]:
    records: list[dict[str, str]] = []
    try:
        blocks = raw.decode("utf-8").split("\x00\x00")
    except UnicodeDecodeError:
        raise ExecutorError("GIT_WORKTREE_TOPOLOGY") from None
    for block in blocks:
        if not block:
            continue
        fields = [field for field in block.split("\x00") if field]
        if not fields or not fields[0].startswith("worktree "):
            raise ExecutorError("GIT_WORKTREE_TOPOLOGY")
        record: dict[str, str] = {"worktree": fields[0][9:]}
        for field in fields[1:]:
            key, separator, value = field.partition(" ")
            if not key or key in record:
                raise ExecutorError("GIT_WORKTREE_TOPOLOGY")
            record[key] = value if separator else ""
        if _SHA1.fullmatch(record.get("HEAD", "")) is None:
            raise ExecutorError("GIT_WORKTREE_TOPOLOGY")
        records.append(record)
    if not records:
        raise ExecutorError("GIT_WORKTREE_TOPOLOGY")
    return records


def _worktree_record_path(record: Mapping[str, str]) -> Path:
    raw = record.get("worktree")
    if not isinstance(raw, str) or not raw:
        raise ExecutorError("GIT_WORKTREE_TOPOLOGY")
    lexical = Path(os.path.abspath(raw))
    try:
        resolved = lexical.resolve(strict=True)
    except OSError:
        raise ExecutorError("GIT_WORKTREE_TOPOLOGY") from None
    if (
        not resolved.is_dir()
        or _is_reparse_or_link(lexical)
        or os.path.normcase(str(lexical)) != os.path.normcase(str(resolved))
    ):
        raise ExecutorError("GIT_WORKTREE_TOPOLOGY")
    return resolved


def _verify_git_repository_context(repo_root: Path) -> _RepositoryContext:
    git_dir = _verify_git_metadata_paths(repo_root)
    _candidate_git_dir, common_dir = _git_layout(repo_root)
    checks = (
        (["rev-parse", "--absolute-git-dir"], str(git_dir)),
        (["rev-parse", "--show-toplevel"], str(repo_root)),
        (["rev-parse", "--is-inside-work-tree"], "true"),
        (["rev-parse", "--is-bare-repository"], "false"),
        (["rev-parse", "--is-shallow-repository"], "false"),
        (["rev-parse", "--show-object-format"], "sha1"),
    )
    for arguments, expected in checks:
        try:
            actual = _git_context_query(repo_root, arguments).decode("utf-8").strip()
        except UnicodeDecodeError:
            raise ExecutorError("GIT_REPOSITORY_TRUST") from None
        if arguments[-1] in {"--absolute-git-dir", "--show-toplevel"}:
            actual_path = Path(os.path.abspath(actual))
            try:
                actual = str(actual_path.resolve(strict=True))
            except OSError:
                raise ExecutorError("GIT_REPOSITORY_TRUST") from None
            expected = str(Path(expected).resolve(strict=True))
            if os.path.normcase(actual) != os.path.normcase(expected):
                raise ExecutorError("GIT_REPOSITORY_TRUST")
        elif actual != expected:
            raise ExecutorError("GIT_REPOSITORY_TRUST")
    try:
        candidate_branch = _git_context_query(
            repo_root, ["symbolic-ref", "-q", "HEAD"]
        ).decode("ascii").strip()
    except UnicodeDecodeError:
        raise ExecutorError("GIT_WORKTREE_TOPOLOGY") from None
    if candidate_branch not in {
        CONTROL_BRANCH_REF,
        RENDERER_BRANCH_REF,
        CLI_BRANCH_REF,
        INTEGRATION_BRANCH_REF,
    }:
        raise ExecutorError("GIT_BRANCH_SCOPE")
    records = _parse_worktree_list(
        _git_context_query(repo_root, ["worktree", "list", "--porcelain", "-z"])
    )
    candidate_records = [
        record
        for record in records
        if os.path.normcase(str(_worktree_record_path(record)))
        == os.path.normcase(str(repo_root))
    ]
    control_records = [
        record for record in records if record.get("branch") == CONTROL_BRANCH_REF
    ]
    if (
        len(candidate_records) != 1
        or candidate_records[0].get("branch") != candidate_branch
        or "locked" in candidate_records[0]
        or "prunable" in candidate_records[0]
        or len(control_records) != 1
        or "locked" in control_records[0]
        or "prunable" in control_records[0]
    ):
        raise ExecutorError("GIT_WORKTREE_TOPOLOGY")
    control_root = _worktree_record_path(control_records[0])
    control_git_dir = _verify_git_metadata_paths(control_root)
    _unused, control_common_dir = _git_layout(control_root)
    if os.path.normcase(str(control_common_dir)) != os.path.normcase(str(common_dir)):
        raise ExecutorError("GIT_WORKTREE_TOPOLOGY")
    try:
        control_branch = _git_context_query(
            control_root, ["symbolic-ref", "-q", "HEAD"]
        ).decode("ascii").strip()
    except UnicodeDecodeError:
        raise ExecutorError("GIT_WORKTREE_TOPOLOGY") from None
    if control_branch != CONTROL_BRANCH_REF:
        raise ExecutorError("GIT_WORKTREE_TOPOLOGY")
    try:
        candidate_head = _git_context_query(repo_root, ["rev-parse", "HEAD"]).decode(
            "ascii"
        ).strip()
        control_head = _git_context_query(control_root, ["rev-parse", "HEAD"]).decode(
            "ascii"
        ).strip()
    except UnicodeDecodeError:
        raise ExecutorError("GIT_WORKTREE_TOPOLOGY") from None
    if (
        candidate_head != candidate_records[0]["HEAD"]
        or control_head != control_records[0]["HEAD"]
    ):
        raise ExecutorError("GIT_WORKTREE_TOPOLOGY")
    checked_roots: set[str] = set()
    for checked_root in (repo_root, control_root):
        key = os.path.normcase(str(checked_root))
        if key in checked_roots:
            continue
        checked_roots.add(key)
        _verify_git_local_configuration(checked_root)
        _verify_repository_attribute_policy(checked_root)
    return _RepositoryContext(
        candidate_root=repo_root,
        candidate_git_dir=git_dir,
        common_dir=common_dir,
        candidate_branch_ref=candidate_branch,
        control_root=control_root,
        control_git_dir=control_git_dir,
    )


def _repository_context(repo_root: Path) -> _RepositoryContext:
    return _verify_git_repository_context(repo_root)


def _normalise_repo_root(repo_root: str | os.PathLike[str]) -> Path:
    lexical = Path(repo_root)
    if not lexical.is_absolute():
        lexical = Path.cwd() / lexical
    lexical = Path(os.path.abspath(lexical))
    if not lexical.exists() or not lexical.is_dir() or _is_reparse_or_link(lexical):
        raise ExecutorError("REPO_ROOT")
    try:
        resolved = lexical.resolve(strict=True)
    except OSError:
        raise ExecutorError("REPO_ROOT") from None
    if os.path.normcase(str(resolved)) != os.path.normcase(str(lexical)):
        raise ExecutorError("REPO_ROOT")
    _verify_git_repository_context(resolved)
    return resolved


def _relative_parts(relative_path: str) -> tuple[str, ...]:
    if not isinstance(relative_path, str) or not relative_path or "\\" in relative_path:
        raise ExecutorError("UNSAFE_PATH")
    parsed = PurePosixPath(relative_path)
    if parsed.is_absolute() or not parsed.parts or any(
        part in {"", ".", ".."} for part in parsed.parts
    ):
        raise ExecutorError("UNSAFE_PATH")
    return tuple(parsed.parts)


def _safe_repo_path(
    repo_root: Path,
    relative_path: str,
    *,
    create_parents: bool = False,
    allow_missing_leaf: bool = False,
) -> Path:
    parts = _relative_parts(relative_path)
    current = repo_root
    for index, part in enumerate(parts):
        current = current / part
        is_leaf = index == len(parts) - 1
        if current.exists() or current.is_symlink():
            if _is_reparse_or_link(current):
                raise ExecutorError("UNSAFE_PATH")
            if is_leaf:
                if not current.is_file():
                    raise ExecutorError("UNSAFE_PATH")
            elif not current.is_dir():
                raise ExecutorError("UNSAFE_PATH")
            continue
        if is_leaf:
            if not allow_missing_leaf:
                raise ExecutorError("MISSING_PATH")
        elif create_parents:
            try:
                os.mkdir(current)
            except FileExistsError:
                pass
            except OSError:
                raise ExecutorError("PATH_CREATE") from None
            if not current.is_dir() or _is_reparse_or_link(current):
                raise ExecutorError("UNSAFE_PATH")
        else:
            raise ExecutorError("MISSING_PATH")
    return current


def _read_fixed_bytes(repo_root: Path, relative_path: str, *, limit: int) -> bytes:
    path = _safe_repo_path(repo_root, relative_path)
    flags = os.O_RDONLY
    if hasattr(os, "O_BINARY"):
        flags |= os.O_BINARY
    if hasattr(os, "O_NOFOLLOW"):
        flags |= os.O_NOFOLLOW
    descriptor: int | None = None
    try:
        descriptor = os.open(path, flags)
        opened = os.fstat(descriptor)
        lexical = os.lstat(path)
        if (
            not os.path.samestat(opened, lexical)
            or _is_reparse_or_link(path)
            or not stat.S_ISREG(opened.st_mode)
        ):
            raise ExecutorError("UNSAFE_PATH")
        if opened.st_size > limit:
            raise ExecutorError("FILE_LIMIT")
        chunks: list[bytes] = []
        remaining = limit + 1
        while remaining > 0:
            block = os.read(descriptor, min(65536, remaining))
            if not block:
                break
            chunks.append(block)
            remaining -= len(block)
        raw = b"".join(chunks)
    except ExecutorError:
        raise
    except OSError:
        raise ExecutorError("FILE_READ") from None
    finally:
        if descriptor is not None:
            try:
                os.close(descriptor)
            except OSError:
                raise ExecutorError("FILE_READ") from None
    if len(raw) > limit:
        raise ExecutorError("FILE_LIMIT")
    return raw


def _exclusive_open(repo_root: Path, relative_path: str) -> tuple[Path, BinaryIO]:
    path = _safe_repo_path(
        repo_root,
        relative_path,
        create_parents=True,
        allow_missing_leaf=True,
    )
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
    if hasattr(os, "O_BINARY"):
        flags |= os.O_BINARY
    if hasattr(os, "O_NOFOLLOW"):
        flags |= os.O_NOFOLLOW
    try:
        descriptor = os.open(path, flags, 0o600)
    except FileExistsError:
        raise ExecutorError("EXCLUSIVE_PATH_EXISTS") from None
    except OSError:
        raise ExecutorError("EXCLUSIVE_PATH_CREATE") from None
    try:
        if (
            not os.path.samestat(os.fstat(descriptor), os.lstat(path))
            or _is_reparse_or_link(path)
        ):
            os.close(descriptor)
            raise ExecutorError("UNSAFE_PATH")
    except ExecutorError:
        raise
    except OSError:
        os.close(descriptor)
        raise ExecutorError("UNSAFE_PATH") from None
    return path, os.fdopen(descriptor, "wb", buffering=0)


def _write_open_file(handle: BinaryIO, raw: bytes) -> None:
    try:
        handle.write(raw)
        handle.flush()
        os.fsync(handle.fileno())
    except OSError:
        raise ExecutorError("DURABLE_WRITE") from None


def _verify_open_artifact_identity(
    path: Path,
    handle: BinaryIO,
    *,
    expected_size: int,
) -> os.stat_result:
    """Bind an open evidence handle to its current lexical path."""

    try:
        opened = os.fstat(handle.fileno())
        lexical = os.lstat(path)
    except (OSError, ValueError):
        raise ExecutorError("EVIDENCE_PATH_RACE") from None
    if (
        not os.path.samestat(opened, lexical)
        or _is_reparse_or_link(path)
        or not stat.S_ISREG(opened.st_mode)
        or opened.st_size != expected_size
        or getattr(opened, "st_nlink", 1) != 1
    ):
        raise ExecutorError("EVIDENCE_PATH_RACE")
    return opened


def _verify_reopened_artifact(
    path: Path,
    *,
    expected_identity: os.stat_result,
    expected_raw: bytes,
) -> None:
    flags = os.O_RDONLY
    if hasattr(os, "O_BINARY"):
        flags |= os.O_BINARY
    if hasattr(os, "O_NOFOLLOW"):
        flags |= os.O_NOFOLLOW
    descriptor: int | None = None
    try:
        descriptor = os.open(path, flags)
        reopened = os.fstat(descriptor)
        lexical = os.lstat(path)
        if (
            not os.path.samestat(reopened, lexical)
            or not os.path.samestat(reopened, expected_identity)
            or _is_reparse_or_link(path)
            or not stat.S_ISREG(reopened.st_mode)
            or reopened.st_size != len(expected_raw)
            or getattr(reopened, "st_nlink", 1) != 1
        ):
            raise ExecutorError("EVIDENCE_PATH_RACE")
        remaining = len(expected_raw) + 1
        chunks: list[bytes] = []
        while remaining > 0:
            block = os.read(descriptor, min(65536, remaining))
            if not block:
                break
            chunks.append(block)
            remaining -= len(block)
        if b"".join(chunks) != expected_raw:
            raise ExecutorError("EVIDENCE_PATH_RACE")
    except ExecutorError:
        raise
    except OSError:
        raise ExecutorError("EVIDENCE_PATH_RACE") from None
    finally:
        if descriptor is not None:
            try:
                os.close(descriptor)
            except OSError:
                raise ExecutorError("EVIDENCE_PATH_RACE") from None


def _seal_exclusive_artifact(path: Path, handle: BinaryIO, raw: bytes) -> None:
    _verify_open_artifact_identity(path, handle, expected_size=0)
    _write_open_file(handle, raw)
    identity = _verify_open_artifact_identity(
        path,
        handle,
        expected_size=len(raw),
    )
    try:
        handle.close()
    except OSError:
        raise ExecutorError("DURABLE_WRITE") from None
    _verify_reopened_artifact(
        path,
        expected_identity=identity,
        expected_raw=raw,
    )


def _atomic_write_json(repo_root: Path, relative_path: str, value: Any) -> None:
    destination = _safe_repo_path(
        repo_root,
        relative_path,
        create_parents=True,
        allow_missing_leaf=True,
    )
    if destination.exists() and _is_reparse_or_link(destination):
        raise ExecutorError("UNSAFE_PATH")
    raw = canonical_json_bytes(value)
    temporary: Path | None = None
    descriptor: int | None = None
    for _attempt in range(8):
        candidate = destination.parent / (
            f".{destination.name}.{secrets.token_hex(12)}.tmp"
        )
        flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
        if hasattr(os, "O_BINARY"):
            flags |= os.O_BINARY
        if hasattr(os, "O_NOFOLLOW"):
            flags |= os.O_NOFOLLOW
        try:
            descriptor = os.open(candidate, flags, 0o600)
            if (
                not os.path.samestat(os.fstat(descriptor), os.lstat(candidate))
                or _is_reparse_or_link(candidate)
            ):
                os.close(descriptor)
                descriptor = None
                raise ExecutorError("UNSAFE_PATH")
            temporary = candidate
            break
        except FileExistsError:
            continue
        except OSError:
            raise ExecutorError("ATOMIC_WRITE") from None
    if temporary is None or descriptor is None:
        raise ExecutorError("ATOMIC_WRITE")
    try:
        with os.fdopen(descriptor, "wb", buffering=0) as handle:
            handle.write(raw)
            handle.flush()
            os.fsync(handle.fileno())
        if destination.exists() and _is_reparse_or_link(destination):
            raise ExecutorError("UNSAFE_PATH")
        os.replace(temporary, destination)
        temporary = None
        if os.name != "nt":
            directory_fd = os.open(destination.parent, os.O_RDONLY)
            try:
                os.fsync(directory_fd)
            finally:
                os.close(directory_fd)
    except ExecutorError:
        raise
    except OSError:
        raise ExecutorError("ATOMIC_WRITE") from None
    finally:
        if temporary is not None:
            try:
                temporary.unlink()
            except OSError:
                pass


class _FileLock:
    """A fixed-path advisory lock shared by all trusted-executor processes."""

    def __init__(self, repo_root: Path):
        self._repo_root = repo_root
        self._handle: BinaryIO | None = None

    def __enter__(self) -> "_FileLock":
        path = _safe_repo_path(
            self._repo_root,
            PROVIDER_LEDGER_LOCK_PATH,
            create_parents=True,
            allow_missing_leaf=True,
        )
        if path.exists() and _is_reparse_or_link(path):
            raise ExecutorError("LOCK_PATH")
        flags = os.O_RDWR | os.O_CREAT
        if hasattr(os, "O_BINARY"):
            flags |= os.O_BINARY
        if hasattr(os, "O_NOFOLLOW"):
            flags |= os.O_NOFOLLOW
        try:
            descriptor = os.open(path, flags, 0o600)
            handle = os.fdopen(descriptor, "r+b", buffering=0)
            opened = os.fstat(handle.fileno())
            lexical = os.lstat(path)
            if not os.path.samestat(opened, lexical) or _is_reparse_or_link(path):
                handle.close()
                raise ExecutorError("LOCK_PATH")
            if opened.st_size == 0:
                handle.write(b"0")
                handle.flush()
                os.fsync(handle.fileno())
            handle.seek(0)
        except ExecutorError:
            raise
        except OSError:
            raise ExecutorError("LOCK_OPEN") from None

        deadline = time.monotonic() + LOCK_TIMEOUT_SECONDS
        while True:
            try:
                if os.name == "nt":
                    import msvcrt

                    handle.seek(0)
                    msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
                else:
                    import fcntl

                    fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
                break
            except OSError:
                if time.monotonic() >= deadline:
                    handle.close()
                    raise ExecutorError("LOCK_TIMEOUT") from None
                time.sleep(0.05)
        self._handle = handle
        return self

    def __exit__(self, _type: Any, _value: Any, _traceback: Any) -> None:
        handle = self._handle
        self._handle = None
        if handle is None:
            return
        try:
            if os.name == "nt":
                import msvcrt

                handle.seek(0)
                msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                import fcntl

                fcntl.flock(handle.fileno(), fcntl.LOCK_UN)
        finally:
            handle.close()


def _new_ledger() -> dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "journalVersion": PROVIDER_JOURNAL_VERSION,
        "goalId": GOAL_ID,
        "maxTurns": PROVIDER_MAX_TURNS,
        "usedTurns": 0,
        "remainingTurns": PROVIDER_MAX_TURNS,
        "ledgerSequence": 0,
        "entries": [],
        "attemptEventCount": 0,
        "lastAttemptEventSha256": None,
        "attemptEvents": [],
    }


def _new_runtime_journal() -> dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "journalVersion": PROVIDER_RUNTIME_JOURNAL_VERSION,
        "goalId": GOAL_ID,
        "eventCount": 0,
        "lastEventSha256": None,
        "events": [],
    }


def _validate_runtime_journal(journal: Any) -> None:
    if not isinstance(journal, dict) or frozenset(journal) != _RUNTIME_JOURNAL_KEYS:
        raise ExecutorError("RUNTIME_JOURNAL_SHAPE")
    events = journal.get("events")
    if (
        journal.get("schemaVersion") != SCHEMA_VERSION
        or journal.get("journalVersion") != PROVIDER_RUNTIME_JOURNAL_VERSION
        or journal.get("goalId") != GOAL_ID
        or not isinstance(events, list)
        or journal.get("eventCount") != len(events)
    ):
        raise ExecutorError("RUNTIME_JOURNAL_SUMMARY")
    previous: str | None = None
    reservations: set[str] = set()
    previous_time: datetime | None = None
    groups: list[list[dict[str, Any]]] = []
    seen_batches: set[str] = set()
    for index, event in enumerate(events, start=1):
        if not isinstance(event, dict) or frozenset(event) != _RUNTIME_EVENT_KEYS:
            raise ExecutorError("RUNTIME_EVENT_SHAPE")
        reservation_id = _validate_safe_id(
            event.get("reservationId"), "RUNTIME_RESERVATION"
        )
        batch_id = _validate_safe_id(event.get("batchId"), "RUNTIME_BATCH")
        started = _parse_timestamp(event.get("startedAt"), "RUNTIME_TIME")
        finished = _parse_timestamp(event.get("finishedAt"), "RUNTIME_TIME")
        phase = event.get("phase")
        gate_id = event.get("gateId")
        policy = event.get("commandPolicy")
        harness = event.get("harnessSource")
        driver = event.get("driverSource")
        descriptor = event.get("descriptor")
        observation = event.get("observation")
        boundary = event.get("providerBoundaryDecision")
        package = event.get("packageIdentityEvidence")
        launch_receipt = event.get("packageLaunchReceipt")
        checkout = event.get("checkoutIdentity")
        request = event.get("observedRequest")
        if (
            event.get("sequence") != index
            or event.get("eventType") != "ObservedProviderRequest"
            or reservation_id in reservations
            or event.get("previousEventSha256") != previous
            or event.get("eventSha256") != runtime_event_sha256(event)
            or not _is_int(event.get("reservationSequence"))
            or event["reservationSequence"] < 1
            or gate_id not in PROVIDER_PHASE_LAYOUT
            or phase not in _PROVIDER_BATCH_SIZES
            or phase not in PROVIDER_PHASE_LAYOUT[gate_id]
            or event.get("batchSize") != _PROVIDER_BATCH_SIZES[phase][0]
            or event.get("outcome") not in {"Succeeded", "Failed"}
            or not _is_int(event.get("exitCode"))
            or started >= finished
            or (
                previous_time is not None
                and started < previous_time
                and (
                    not groups
                    or groups[-1][0].get("batchId") != batch_id
                )
            )
            or not _valid_runtime_policy(policy)
            or not _valid_optional_sha256(event.get("argvSha256"))
            or not _valid_runtime_source(harness)
            or not _valid_driver_source(driver, phase=phase)
            or not _valid_path_hash(descriptor)
            or not _valid_observation_binding(observation)
            or not _valid_boundary_binding(boundary)
            or not _valid_package_identity_binding(package)
            or not _valid_launch_receipt_binding(
                launch_receipt,
                phase=phase,
                batch_id=batch_id,
            )
            or not _valid_runtime_checkout(
                checkout,
                product_candidate=event.get("productCandidate"),
                success=event.get("outcome") == "Succeeded",
            )
            or (
                request is not None
                and not _valid_observed_request(
                    request,
                    reservation_id=reservation_id,
                    reservation_sequence=event["reservationSequence"],
                )
            )
            or (
                event.get("outcome") == "Succeeded"
                and (
                    event.get("exitCode") != 0
                    or policy is None
                    or event.get("argvSha256") is None
                    or harness is None
                    or driver is None
                    or descriptor is None
                    or observation is None
                    or boundary is None
                    or package is None
                    or launch_receipt is None
                    or checkout is None
                    or observation.get("status") != "Accepted"
                    or request is None
                    or request.get("status") != "Succeeded"
                )
            )
        ):
            raise ExecutorError("RUNTIME_EVENT")
        if not groups or groups[-1][0]["batchId"] != batch_id:
            if batch_id in seen_batches:
                raise ExecutorError("RUNTIME_BATCH_ORDER")
            groups.append([])
            seen_batches.add(batch_id)
        groups[-1].append(event)
        reservations.add(reservation_id)
        previous = event["eventSha256"]
        previous_time = finished
    for group in groups:
        first = group[0]
        if len(group) != first["batchSize"]:
            raise ExecutorError("RUNTIME_BATCH_SIZE")
        common_fields = (
            "batchId",
            "batchSize",
            "gateId",
            "phase",
            "productCandidate",
            "attemptId",
            "runId",
            "commandPolicy",
            "argvSha256",
            "harnessSource",
            "driverSource",
            "descriptor",
            "observation",
            "providerBoundaryDecision",
            "packageIdentityEvidence",
            "packageLaunchReceipt",
            "checkoutIdentity",
            "startedAt",
            "finishedAt",
            "exitCode",
            "outcome",
        )
        if any(
            any(item[field] != first[field] for field in common_fields)
            for item in group[1:]
        ):
            raise ExecutorError("RUNTIME_BATCH_BINDING")
        for ordinal, item in enumerate(group, start=1):
            request = item["observedRequest"]
            if request is not None and request.get("requestOrdinal") != ordinal:
                raise ExecutorError("RUNTIME_BATCH_ORDER")
    if journal.get("lastEventSha256") != previous:
        raise ExecutorError("RUNTIME_JOURNAL_SUMMARY")


def _valid_checkout_snapshot(value: Any, *, success: bool) -> bool:
    if not isinstance(value, dict) or frozenset(value) != _CHECKOUT_SNAPSHOT_KEYS:
        return False
    if success:
        return (
            isinstance(value.get("headCommit"), str)
            and _SHA1.fullmatch(value["headCommit"]) is not None
            and isinstance(value.get("treeObjectId"), str)
            and _SHA1.fullmatch(value["treeObjectId"]) is not None
            and value.get("gitStatusPorcelainV1") == ""
            and value.get("trackedStatus") == "Clean"
        )
    return (
        value.get("trackedStatus") in {"Clean", "Dirty", "Unavailable"}
        and value.get("gitStatusPorcelainV1")
        in {"", "<redacted-dirty>", "<unavailable>"}
    )


def _valid_optional_sha256(value: Any) -> bool:
    return value is None or (
        isinstance(value, str) and _SHA256.fullmatch(value) is not None
    )


def _valid_runtime_policy(value: Any) -> bool:
    return value is None or (
        isinstance(value, dict)
        and frozenset(value) == _RUNTIME_POLICY_KEYS
        and value.get("policyId") == TRUSTED_PROVIDER_POLICY_ID
        and isinstance(value.get("sha256"), str)
        and _SHA256.fullmatch(value["sha256"]) is not None
    )


def _valid_runtime_source(value: Any) -> bool:
    return value is None or (
        isinstance(value, dict)
        and frozenset(value) == _SOURCE_BINDING_KEYS
        and value.get("path") == TRUSTED_PROVIDER_HARNESS_PATH
        and isinstance(value.get("sha256"), str)
        and _SHA256.fullmatch(value["sha256"]) is not None
        and isinstance(value.get("gitBlobSha"), str)
        and _SHA1.fullmatch(value["gitBlobSha"]) is not None
        and isinstance(value.get("controlRevision"), str)
        and _SHA1.fullmatch(value["controlRevision"]) is not None
    )


def _valid_driver_source(value: Any, *, phase: Any) -> bool:
    return value is None or (
        isinstance(phase, str)
        and phase in PROVIDER_DRIVER_PATHS
        and isinstance(value, dict)
        and frozenset(value) == _SOURCE_BINDING_KEYS
        and value.get("path") == PROVIDER_DRIVER_PATHS[phase]
        and isinstance(value.get("sha256"), str)
        and _SHA256.fullmatch(value["sha256"]) is not None
        and isinstance(value.get("gitBlobSha"), str)
        and _SHA1.fullmatch(value["gitBlobSha"]) is not None
        and isinstance(value.get("controlRevision"), str)
        and _SHA1.fullmatch(value["controlRevision"]) is not None
    )


def _valid_boundary_binding(value: Any) -> bool:
    return value is None or (
        isinstance(value, dict)
        and frozenset(value) == _BOUNDARY_BINDING_KEYS
        and isinstance(value.get("path"), str)
        and value["path"] in set(PROVIDER_BOUNDARY_DECISION_PATHS.values())
        and isinstance(value.get("sha256"), str)
        and _SHA256.fullmatch(value["sha256"]) is not None
        and isinstance(value.get("firstAddCommit"), str)
        and _SHA1.fullmatch(value["firstAddCommit"]) is not None
        and isinstance(value.get("decisionId"), str)
        and _SAFE_ID.fullmatch(value["decisionId"]) is not None
        and value.get("mode") in {"strong-isolation", "cooperative-candidate"}
        and isinstance(value.get("packageTreeRootSha256"), str)
        and _SHA256.fullmatch(value["packageTreeRootSha256"]) is not None
    )


def _valid_package_identity_binding(value: Any) -> bool:
    return value is None or (
        isinstance(value, dict)
        and frozenset(value) == _PACKAGE_IDENTITY_BINDING_KEYS
        and isinstance(value.get("path"), str)
        and isinstance(value.get("sha256"), str)
        and _SHA256.fullmatch(value["sha256"]) is not None
        and isinstance(value.get("treeRootSha256"), str)
        and _SHA256.fullmatch(value["treeRootSha256"]) is not None
        and value.get("entrypoint") == FIXED_PACKAGE_ENTRYPOINT
        and value.get("argv") == list(FIXED_PACKAGE_ARGV)
    )


def _valid_launch_receipt_binding(
    value: Any,
    *,
    phase: Any,
    batch_id: str,
) -> bool:
    return value is None or (
        isinstance(value, dict)
        and frozenset(value) == _LAUNCH_RECEIPT_BINDING_KEYS
        and isinstance(value.get("path"), str)
        and isinstance(value.get("sha256"), str)
        and _SHA256.fullmatch(value["sha256"]) is not None
        and value.get("phase") == phase
        and (
            (
                phase == "provider-resource"
                and _is_int(value.get("profileOrdinal"))
                and 1 <= value["profileOrdinal"] <= 5
            )
            or (
                phase != "provider-resource"
                and value.get("profileOrdinal") is None
            )
        )
        and value.get("batchId") == batch_id
        and isinstance(value.get("packageTreeRootSha256"), str)
        and _SHA256.fullmatch(value["packageTreeRootSha256"]) is not None
    )


def _valid_path_hash(value: Any) -> bool:
    return value is None or (
        isinstance(value, dict)
        and frozenset(value) == _PATH_HASH_KEYS
        and isinstance(value.get("path"), str)
        and isinstance(value.get("sha256"), str)
        and _SHA256.fullmatch(value["sha256"]) is not None
    )


def _valid_observation_binding(value: Any) -> bool:
    return value is None or (
        isinstance(value, dict)
        and frozenset(value) == _OBSERVATION_BINDING_KEYS
        and isinstance(value.get("path"), str)
        and value.get("status") in {"Accepted", "Rejected", "Missing"}
        and _valid_optional_sha256(value.get("sha256"))
    )


def _valid_runtime_checkout(
    value: Any,
    *,
    product_candidate: Any,
    success: bool,
) -> bool:
    return value is None or (
        isinstance(value, dict)
        and frozenset(value) == _CHECKOUT_IDENTITY_KEYS
        and value.get("productCandidate") == product_candidate
        and value.get("mode")
        in {
            "candidate-exact",
            "controlled-tombstone-descendant",
            "manual-challenge-descendant",
            "controlled-and-manual-descendant",
            "provider-boundary-descendant",
            "provider-boundary-and-controlled-descendant",
        }
        and isinstance(value.get("expectedHead"), str)
        and _SHA1.fullmatch(value["expectedHead"]) is not None
        and _valid_checkout_snapshot(value.get("before"), success=True)
        and _valid_checkout_snapshot(value.get("after"), success=success)
    )


def _valid_observed_request(
    value: Any,
    *,
    reservation_id: str,
    reservation_sequence: int,
) -> bool:
    return (
        isinstance(value, dict)
        and frozenset(value) == _OBSERVED_REQUEST_KEYS
        and value.get("reservationId") == reservation_id
        and value.get("reservationSequence") == reservation_sequence
        and _is_int(value.get("requestOrdinal"))
        and value["requestOrdinal"] >= 1
        and isinstance(value.get("requestSha256"), str)
        and _SHA256.fullmatch(value["requestSha256"]) is not None
        and value.get("status") in {"Succeeded", "Failed"}
    )


def _load_runtime_journal_locked(repo_root: Path) -> dict[str, Any]:
    try:
        raw = _read_fixed_bytes(
            repo_root,
            PROVIDER_RUNTIME_JOURNAL_PATH,
            limit=MAX_JSON_BYTES,
        )
    except ExecutorError as error:
        if error.code == "MISSING_PATH":
            journal = _new_runtime_journal()
            _atomic_write_json(repo_root, PROVIDER_RUNTIME_JOURNAL_PATH, journal)
            return journal
        raise
    journal = _read_json_bytes(raw, "RUNTIME_JOURNAL_JSON")
    _validate_runtime_journal(journal)
    return journal


def _append_runtime_events_locked(
    repo_root: Path,
    new_events: Sequence[dict[str, Any]],
) -> dict[str, Any]:
    journal = _load_runtime_journal_locked(repo_root)
    existing = {item.get("reservationId") for item in journal["events"]}
    requested = [item.get("reservationId") for item in new_events]
    if (
        not new_events
        or len(set(requested)) != len(requested)
        or any(item in existing for item in requested)
    ):
        raise ExecutorError("RUNTIME_RESERVATION_REPLAY")
    previous = journal["lastEventSha256"]
    for event in new_events:
        detached = dict(event)
        detached["sequence"] = len(journal["events"]) + 1
        detached["previousEventSha256"] = previous
        detached["eventSha256"] = runtime_event_sha256(detached)
        journal["events"].append(detached)
        previous = detached["eventSha256"]
    journal["eventCount"] = len(journal["events"])
    journal["lastEventSha256"] = previous
    _validate_runtime_journal(journal)
    _atomic_write_json(repo_root, PROVIDER_RUNTIME_JOURNAL_PATH, journal)
    return journal


def _ensure_failed_runtime_receipts_locked(
    repo_root: Path,
    batch: ReservationBatch,
) -> dict[str, Any]:
    journal = _load_runtime_journal_locked(repo_root)
    existing = [
        event
        for event in journal["events"]
        if event.get("reservationId") in batch.reservation_ids
    ]
    if existing:
        if (
            len(existing) == len(batch.reservation_ids)
            and tuple(event.get("reservationId") for event in existing)
            == batch.reservation_ids
        ):
            return journal
        raise ExecutorError("RUNTIME_PARTIAL_BATCH")
    digest = hashlib.sha256(
        canonical_json_bytes(
            {
                "reservationIds": list(batch.reservation_ids),
                "reservationSequences": list(batch.reservation_sequences),
            }
        )
    ).hexdigest()[:16]
    batch_id = f"failed-{batch.reservation_sequences[0]}-{digest}"
    started_at = _utc_now()
    finished_at = _timestamp_after(started_at)
    events = [
        {
            "eventType": "ObservedProviderRequest",
            "reservationId": reservation_id,
            "reservationSequence": reservation_sequence,
            "gateId": batch.gate_id,
            "phase": phase,
            "productCandidate": batch.product_candidate,
            "attemptId": batch.attempt_id,
            "runId": batch.run_id,
            "batchId": batch_id,
            "batchSize": len(batch.reservation_ids),
            "commandPolicy": None,
            "argvSha256": None,
            "harnessSource": None,
            "driverSource": None,
            "descriptor": None,
            "observation": None,
            "providerBoundaryDecision": None,
            "packageIdentityEvidence": None,
            "packageLaunchReceipt": None,
            "observedRequest": None,
            "checkoutIdentity": None,
            "startedAt": started_at,
            "finishedAt": finished_at,
            "exitCode": 127,
            "outcome": "Failed",
        }
        for reservation_id, reservation_sequence, phase in zip(
            batch.reservation_ids,
            batch.reservation_sequences,
            batch.phases,
            strict=True,
        )
    ]
    return _append_runtime_events_locked(repo_root, events)


def _load_ledger_locked(repo_root: Path) -> dict[str, Any]:
    try:
        raw = _read_fixed_bytes(repo_root, PROVIDER_LEDGER_PATH, limit=MAX_JSON_BYTES)
    except ExecutorError as error:
        if error.code == "MISSING_PATH":
            ledger = _new_ledger()
            _atomic_write_json(repo_root, PROVIDER_LEDGER_PATH, ledger)
            return ledger
        raise
    ledger = _read_json_bytes(raw, "LEDGER_JSON")
    _validate_ledger(ledger)
    return ledger


def _validate_safe_id(value: Any, code: str) -> str:
    if not isinstance(value, str) or _SAFE_ID.fullmatch(value) is None:
        raise ExecutorError(code)
    return value


def _validate_candidate(value: Any) -> str:
    if not isinstance(value, str) or _SHA1.fullmatch(value) is None:
        raise ExecutorError("PRODUCT_CANDIDATE")
    return value


def _validate_ledger(ledger: Any) -> None:
    if not isinstance(ledger, dict) or frozenset(ledger) != _LEDGER_KEYS:
        raise ExecutorError("LEDGER_SHAPE")
    entries = ledger.get("entries")
    events = ledger.get("attemptEvents")
    if (
        ledger.get("schemaVersion") != SCHEMA_VERSION
        or ledger.get("journalVersion") != PROVIDER_JOURNAL_VERSION
        or ledger.get("goalId") != GOAL_ID
        or ledger.get("maxTurns") != PROVIDER_MAX_TURNS
        or not isinstance(entries, list)
        or not isinstance(events, list)
        or ledger.get("usedTurns") != len(entries)
        or ledger.get("remainingTurns") != PROVIDER_MAX_TURNS - len(entries)
        or ledger.get("ledgerSequence") != len(entries)
        or ledger.get("attemptEventCount") != len(events)
        or len(entries) > PROVIDER_MAX_TURNS
    ):
        raise ExecutorError("LEDGER_SUMMARY")

    previous_hash: str | None = None
    reservations: dict[str, dict[str, Any]] = {}
    attempts: dict[str, list[dict[str, Any]]] = {}
    attempt_owners: dict[str, tuple[str, str]] = {}
    controlled_by_gate: dict[str, int] = {}
    previous_reserved_at: datetime | None = None
    for index, entry in enumerate(entries, start=1):
        if not isinstance(entry, dict) or frozenset(entry) != _PROVIDER_ENTRY_KEYS:
            raise ExecutorError("LEDGER_ENTRY_SHAPE")
        gate_id = entry.get("gateId")
        phase = entry.get("phase")
        reservation_id = _validate_safe_id(
            entry.get("reservationId"), "RESERVATION_ID"
        )
        attempt_id = _validate_safe_id(entry.get("attemptId"), "ATTEMPT_ID")
        _validate_safe_id(entry.get("runId"), "RUN_ID")
        candidate = _validate_candidate(entry.get("productCandidate"))
        recorded = _parse_timestamp(entry.get("reservedAt"), "LEDGER_TIMESTAMP")
        if previous_reserved_at is not None and recorded < previous_reserved_at:
            raise ExecutorError("LEDGER_TIMESTAMP")
        previous_reserved_at = recorded
        if (
            entry.get("sequence") != index
            or entry.get("eventType") != "TurnReserved"
            or gate_id not in PROVIDER_PHASE_LAYOUT
            or phase not in PROVIDER_PHASE_LAYOUT[gate_id]
            or entry.get("previousEntrySha256") != previous_hash
            or entry.get("entrySha256") != provider_entry_sha256(entry)
            or reservation_id in reservations
        ):
            raise ExecutorError("LEDGER_ENTRY")
        owner = attempt_owners.setdefault(attempt_id, (gate_id, candidate))
        if owner != (gate_id, candidate):
            raise ExecutorError("ATTEMPT_OWNER")
        reservations[reservation_id] = entry
        attempts.setdefault(attempt_id, []).append(entry)
        if phase == "controlled-write":
            controlled_by_gate[gate_id] = controlled_by_gate.get(gate_id, 0) + 1
            if controlled_by_gate[gate_id] > 1:
                raise ExecutorError("CONTROLLED_WRITE_REPLAY")
        previous_hash = entry["entrySha256"]

    previous_event_hash: str | None = None
    completions: dict[str, dict[str, Any]] = {}
    finished: dict[str, dict[str, Any]] = {}
    previous_event_time: datetime | None = None
    for index, event in enumerate(events, start=1):
        if not isinstance(event, dict):
            raise ExecutorError("ATTEMPT_EVENT_SHAPE")
        event_type = event.get("eventType")
        expected_keys = (
            _TURN_COMPLETED_KEYS
            if event_type == "TurnCompleted"
            else _ATTEMPT_FINISHED_KEYS
            if event_type == "AttemptFinished"
            else frozenset()
        )
        if frozenset(event) != expected_keys:
            raise ExecutorError("ATTEMPT_EVENT_SHAPE")
        if (
            event.get("attemptEventSequence") != index
            or event.get("previousAttemptEventSha256") != previous_event_hash
            or event.get("attemptEventSha256") != attempt_event_sha256(event)
        ):
            raise ExecutorError("ATTEMPT_EVENT_CHAIN")
        event_time = _parse_timestamp(
            event.get("completedAt")
            if event_type == "TurnCompleted"
            else event.get("finishedAt"),
            "ATTEMPT_EVENT_TIME",
        )
        if previous_event_time is not None and event_time <= previous_event_time:
            raise ExecutorError("ATTEMPT_EVENT_TIME")
        previous_event_time = event_time
        if event_type == "TurnCompleted":
            reservation_id = event.get("reservationId")
            if (
                reservation_id not in reservations
                or reservation_id in completions
                or event.get("outcome") not in {"Succeeded", "Failed"}
                or event_time
                <= _parse_timestamp(
                    reservations[reservation_id]["reservedAt"],
                    "ATTEMPT_EVENT_TIME",
                )
            ):
                raise ExecutorError("TURN_COMPLETION")
            completions[reservation_id] = event
        else:
            attempt_id = _validate_safe_id(event.get("attemptId"), "ATTEMPT_ID")
            attempt_entries = attempts.get(attempt_id)
            if not attempt_entries or attempt_id in finished:
                raise ExecutorError("ATTEMPT_FINISH")
            owner = attempt_owners[attempt_id]
            if (
                event.get("gateId") != owner[0]
                or event.get("productCandidate") != owner[1]
                or event.get("outcome") not in {"Passed", "Failed"}
                or event.get("reservationSequenceStart")
                != attempt_entries[0]["sequence"]
                or event.get("reservationSequenceEnd")
                != attempt_entries[-1]["sequence"]
                or any(
                    item["reservationId"] not in completions
                    for item in attempt_entries
                )
                or event_time
                <= max(
                    _parse_timestamp(
                        completions[item["reservationId"]]["completedAt"],
                        "ATTEMPT_EVENT_TIME",
                    )
                    for item in attempt_entries
                )
            ):
                raise ExecutorError("ATTEMPT_FINISH")
            phases = tuple(item["phase"] for item in attempt_entries)
            layout = PROVIDER_PHASE_LAYOUT[owner[0]]
            if phases != layout[: len(phases)]:
                raise ExecutorError("ATTEMPT_PHASE_PREFIX")
            outcomes = {
                completions[item["reservationId"]]["outcome"]
                for item in attempt_entries
            }
            if event.get("outcome") == "Passed" and (
                phases != layout or outcomes != {"Succeeded"}
            ):
                raise ExecutorError("ATTEMPT_SUCCESS")
            finished[attempt_id] = event
        previous_event_hash = event["attemptEventSha256"]

    if ledger.get("lastAttemptEventSha256") != previous_event_hash:
        raise ExecutorError("ATTEMPT_EVENT_SUMMARY")

    # Every attempt is always a frozen prefix, including an unfinished one.
    for attempt_id, attempt_entries in attempts.items():
        phases = tuple(item["phase"] for item in attempt_entries)
        layout = PROVIDER_PHASE_LAYOUT[attempt_entries[0]["gateId"]]
        sequences = [item["sequence"] for item in attempt_entries]
        if (
            phases != layout[: len(phases)]
            or sequences
            != list(range(sequences[0], sequences[0] + len(sequences)))
        ):
            raise ExecutorError("ATTEMPT_PHASE_PREFIX")
        finished_event = finished.get(attempt_id)
        if finished_event is not None and (
            finished_event["reservationSequenceEnd"]
            != attempt_entries[-1]["sequence"]
        ):
            raise ExecutorError("ATTEMPT_REOPENED")
    if sum(attempt_id not in finished for attempt_id in attempts) > 1:
        raise ExecutorError("MULTIPLE_OPEN_ATTEMPTS")


def read_provider_ledger(repo_root: str | os.PathLike[str]) -> dict[str, Any]:
    root = _normalise_repo_root(repo_root)
    control_root = _repository_context(root).control_root
    with _FileLock(control_root):
        ledger = _load_ledger_locked(control_root)
        # Return a detached value so callers cannot mutate our validated object.
        return json.loads(canonical_json_bytes(ledger).decode("utf-8"))


def _attempt_views(
    ledger: Mapping[str, Any],
) -> tuple[
    dict[str, list[dict[str, Any]]],
    dict[str, dict[str, Any]],
    dict[str, dict[str, Any]],
]:
    attempts: dict[str, list[dict[str, Any]]] = {}
    reservations: dict[str, dict[str, Any]] = {}
    for item in ledger["entries"]:
        attempts.setdefault(item["attemptId"], []).append(item)
        reservations[item["reservationId"]] = item
    completions: dict[str, dict[str, Any]] = {}
    finished: dict[str, dict[str, Any]] = {}
    for event in ledger["attemptEvents"]:
        if event["eventType"] == "TurnCompleted":
            completions[event["reservationId"]] = event
        else:
            finished[event["attemptId"]] = event
    return attempts, completions, finished


def _append_attempt_event(ledger: dict[str, Any], event: dict[str, Any]) -> None:
    event["attemptEventSequence"] = len(ledger["attemptEvents"]) + 1
    event["previousAttemptEventSha256"] = ledger["lastAttemptEventSha256"]
    event["attemptEventSha256"] = attempt_event_sha256(event)
    ledger["attemptEvents"].append(event)
    ledger["attemptEventCount"] = len(ledger["attemptEvents"])
    ledger["lastAttemptEventSha256"] = event["attemptEventSha256"]


def _append_attempt_finished(
    ledger: dict[str, Any],
    attempt_entries: Sequence[Mapping[str, Any]],
    outcome: str,
) -> None:
    last_event_time = (
        ledger["attemptEvents"][-1].get(
            "completedAt", ledger["attemptEvents"][-1].get("finishedAt")
        )
        if ledger["attemptEvents"]
        else attempt_entries[-1]["reservedAt"]
    )
    _append_attempt_event(
        ledger,
        {
            "eventType": "AttemptFinished",
            "gateId": attempt_entries[0]["gateId"],
            "productCandidate": attempt_entries[0]["productCandidate"],
            "attemptId": attempt_entries[0]["attemptId"],
            "outcome": outcome,
            "reservationSequenceStart": attempt_entries[0]["sequence"],
            "reservationSequenceEnd": attempt_entries[-1]["sequence"],
            "finishedAt": _timestamp_after(last_event_time),
        },
    )


def _git(
    repo_root: Path,
    arguments: Sequence[str],
    *,
    check: bool = True,
) -> subprocess.CompletedProcess[bytes]:
    _verify_git_metadata_paths(repo_root)
    operation = arguments[0] if arguments else ""
    if operation in {
        "check-attr",
        "diff",
        "diff-files",
        "diff-index",
        "diff-tree",
        "status",
        "submodule",
    }:
        _verify_git_local_configuration(repo_root)
        _verify_repository_attribute_policy(repo_root)
    _path, before_identity = _trusted_git_identity(repo_root)
    command = _git_command(repo_root, arguments)
    try:
        result = subprocess.run(
            command,
            cwd=repo_root,
            env=_git_environment(Path(command[0])),
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            shell=False,
            timeout=30,
            check=False,
        )
    except (OSError, subprocess.SubprocessError):
        raise ExecutorError("GIT_UNAVAILABLE") from None
    if check and result.returncode != 0:
        raise ExecutorError("GIT_STATE")
    if len(result.stdout) > MAX_JSON_BYTES:
        raise ExecutorError("GIT_OUTPUT_LIMIT")
    _path, after_identity = _trusted_git_identity(repo_root)
    if after_identity != before_identity:
        raise ExecutorError("GIT_TOOL_DRIFT")
    _verify_git_metadata_paths(repo_root)
    return result


def _verify_candidate_commit(repo_root: Path, candidate: str) -> None:
    _validate_candidate(candidate)
    result = _git(
        repo_root,
        ["rev-parse", "--verify", f"{candidate}^{{commit}}"],
    )
    try:
        resolved = result.stdout.decode("ascii").strip()
    except UnicodeDecodeError:
        raise ExecutorError("PRODUCT_CANDIDATE") from None
    if resolved != candidate:
        raise ExecutorError("PRODUCT_CANDIDATE")


def _capture_checkout_snapshot(repo_root: Path) -> CheckoutSnapshot:
    head_lines = _decode_git_lines(
        _git(repo_root, ["rev-parse", "HEAD"]).stdout,
        "CHECKOUT_IDENTITY",
    )
    tree_lines = _decode_git_lines(
        _git(repo_root, ["rev-parse", "HEAD^{tree}"]).stdout,
        "CHECKOUT_IDENTITY",
    )
    status = _git(
        repo_root,
        [
            "status",
            "--porcelain=v1",
            "--untracked-files=all",
            "--ignore-submodules=none",
        ],
    ).stdout
    if (
        len(head_lines) != 1
        or _SHA1.fullmatch(head_lines[0]) is None
        or len(tree_lines) != 1
        or _SHA1.fullmatch(tree_lines[0]) is None
    ):
        raise ExecutorError("CHECKOUT_IDENTITY")
    return CheckoutSnapshot(
        head_commit=head_lines[0],
        tree_object_id=tree_lines[0],
        # Never persist attacker-controlled filenames from porcelain output.
        git_status_porcelain_v1="" if status == b"" else "<redacted-dirty>",
    )


def _require_checkout(
    repo_root: Path,
    *,
    expected_head: str,
) -> CheckoutSnapshot:
    snapshot = _capture_checkout_snapshot(repo_root)
    if snapshot.head_commit != expected_head:
        raise ExecutorError("CHECKOUT_HEAD")
    if not snapshot.clean:
        raise ExecutorError("CHECKOUT_DIRTY")
    return snapshot


def _checkout_document(
    *,
    mode: str,
    product_candidate: str,
    expected_head: str,
    before: CheckoutSnapshot,
    after: CheckoutSnapshot | None,
) -> dict[str, Any]:
    return {
        "mode": mode,
        "productCandidate": product_candidate,
        "expectedHead": expected_head,
        "before": before.as_document(),
        "after": (
            after.as_document()
            if after is not None
            else {
                "headCommit": None,
                "treeObjectId": None,
                "gitStatusPorcelainV1": "<unavailable>",
                "trackedStatus": "Unavailable",
            }
        ),
    }


def _checkout_unchanged(
    before: CheckoutSnapshot,
    after: CheckoutSnapshot | None,
    expected_head: str,
) -> bool:
    return (
        after is not None
        and after.clean
        and after.head_commit == expected_head
        and after == before
    )


def _candidate_source_binding(
    repo_root: Path,
    *,
    product_candidate: str,
    source_path: str,
    expected_sha256: str | None = None,
) -> dict[str, str]:
    raw = _read_fixed_bytes(repo_root, source_path, limit=MAX_JSON_BYTES)
    raw_sha = hashlib.sha256(raw).hexdigest()
    if expected_sha256 is not None and raw_sha != expected_sha256:
        raise ExecutorError("COMMAND_SOURCE_HASH")
    tree_line = _git(
        repo_root,
        ["ls-tree", product_candidate, "--", source_path],
    ).stdout
    try:
        decoded = tree_line.decode("utf-8").strip()
    except UnicodeDecodeError:
        raise ExecutorError("COMMAND_SOURCE_GIT") from None
    match = re.fullmatch(r"(100644|100755) blob ([0-9a-f]{40})\t.+", decoded)
    if match is None:
        raise ExecutorError("COMMAND_SOURCE_GIT")
    blob_id = match.group(2)
    if _git(repo_root, ["cat-file", "blob", blob_id]).stdout != raw:
        raise ExecutorError("COMMAND_SOURCE_IDENTITY")
    return {
        "path": source_path,
        "sha256": raw_sha,
        "gitBlobSha": blob_id,
    }


def _bootstrap_source_binding(
    repo_root: Path,
    *,
    product_candidate: str,
    source_path: str,
    expected_sha256: str,
) -> dict[str, str]:
    """Bind a control source to its immutable first-add blob and candidate."""

    raw = _read_fixed_bytes(repo_root, source_path, limit=MAX_JSON_BYTES)
    raw_sha = hashlib.sha256(raw).hexdigest()
    if raw_sha != expected_sha256:
        raise ExecutorError("BOOTSTRAP_SOURCE_HASH")
    status = _git(
        repo_root,
        ["status", "--porcelain=v1", "--untracked-files=all", "--", source_path],
    )
    if status.stdout:
        raise ExecutorError("BOOTSTRAP_SOURCE_DIRTY")
    history = _decode_git_lines(
        _git(repo_root, ["log", "--format=%H", "--follow", "--", source_path]).stdout,
        "BOOTSTRAP_SOURCE_HISTORY",
    )
    additions = _decode_git_lines(
        _git(
            repo_root,
            ["log", "--diff-filter=A", "--format=%H", "--", source_path],
        ).stdout,
        "BOOTSTRAP_SOURCE_HISTORY",
    )
    if len(history) != 1 or additions != history:
        raise ExecutorError("BOOTSTRAP_SOURCE_HISTORY")
    control_revision = history[0]
    ancestry = _git(
        repo_root,
        ["merge-base", "--is-ancestor", control_revision, product_candidate],
        check=False,
    )
    if ancestry.returncode != 0:
        raise ExecutorError("BOOTSTRAP_SOURCE_ANCESTRY")
    first_tree = _git(
        repo_root,
        ["ls-tree", control_revision, "--", source_path],
    ).stdout
    candidate_tree = _git(
        repo_root,
        ["ls-tree", product_candidate, "--", source_path],
    ).stdout
    head_tree = _git(repo_root, ["ls-tree", "HEAD", "--", source_path]).stdout
    index_tree = _git(repo_root, ["ls-files", "--stage", "--", source_path]).stdout
    try:
        first_line = first_tree.decode("utf-8").strip()
        candidate_line = candidate_tree.decode("utf-8").strip()
        head_line = head_tree.decode("utf-8").strip()
        index_line = index_tree.decode("utf-8").strip()
    except UnicodeDecodeError:
        raise ExecutorError("BOOTSTRAP_SOURCE_GIT") from None
    match = re.fullmatch(r"100644 blob ([0-9a-f]{40})\t.+", first_line)
    if match is None:
        raise ExecutorError("BOOTSTRAP_SOURCE_GIT")
    blob_id = match.group(1)
    expected_tree_suffix = f"blob {blob_id}\t"
    if (
        not candidate_line.startswith(f"100644 {expected_tree_suffix}")
        or not head_line.startswith(f"100644 {expected_tree_suffix}")
        or not index_line.startswith(f"100644 {blob_id} 0\t")
        or _git(repo_root, ["cat-file", "blob", blob_id]).stdout != raw
    ):
        raise ExecutorError("BOOTSTRAP_SOURCE_IDENTITY")
    return {
        "path": source_path,
        "sha256": raw_sha,
        "gitBlobSha": blob_id,
        "controlRevision": control_revision,
    }


def _bootstrap_control_preflight(
    repo_root: Path,
    *,
    product_candidate: str,
) -> tuple[dict[str, Any], dict[str, str], dict[str, str]]:
    """Verify the manifest and executing runner are immutable bootstrap blobs."""

    manifest_raw = _read_fixed_bytes(
        repo_root,
        GATE_REQUIREMENTS_PATH,
        limit=MAX_JSON_BYTES,
    )
    manifest_binding = _bootstrap_source_binding(
        repo_root,
        product_candidate=product_candidate,
        source_path=GATE_REQUIREMENTS_PATH,
        expected_sha256=hashlib.sha256(manifest_raw).hexdigest(),
    )
    runner_raw = _read_fixed_bytes(repo_root, RUNNER_SOURCE_PATH, limit=MAX_JSON_BYTES)
    try:
        executing_raw = Path(__file__).resolve(strict=True).read_bytes()
    except OSError:
        raise ExecutorError("RUNNER_SOURCE") from None
    if runner_raw != executing_raw:
        raise ExecutorError("RUNNER_SOURCE")
    runner_binding = _bootstrap_source_binding(
        repo_root,
        product_candidate=product_candidate,
        source_path=RUNNER_SOURCE_PATH,
        expected_sha256=hashlib.sha256(runner_raw).hexdigest(),
    )
    manifest = _load_requirements(repo_root)
    _trusted_git_policy(repo_root, manifest)
    return manifest, manifest_binding, runner_binding


def _load_requirements(repo_root: Path) -> dict[str, Any]:
    raw = _read_fixed_bytes(repo_root, GATE_REQUIREMENTS_PATH, limit=MAX_JSON_BYTES)
    document = _read_json_bytes(raw, "REQUIREMENTS_JSON")
    if (
        not isinstance(document, dict)
        or document.get("schemaVersion") != SCHEMA_VERSION
        or document.get("goalId") != GOAL_ID
        or not isinstance(document.get("gates"), list)
    ):
        raise ExecutorError("REQUIREMENTS_SHAPE")
    identifiers: set[str] = set()
    for gate in document["gates"]:
        if not isinstance(gate, dict) or not isinstance(gate.get("gateId"), str):
            raise ExecutorError("REQUIREMENTS_SHAPE")
        if gate["gateId"] in identifiers:
            raise ExecutorError("REQUIREMENTS_DUPLICATE")
        identifiers.add(gate["gateId"])
    return document


def _gate_lane_context(
    repo_root: Path,
    manifest: Mapping[str, Any],
    gate_id: str,
) -> _RepositoryContext:
    matches = [
        gate
        for gate in manifest.get("gates", [])
        if isinstance(gate, dict) and gate.get("gateId") == gate_id
    ]
    if len(matches) != 1:
        raise ExecutorError("GATE_NOT_IN_REQUIREMENTS")
    gate = matches[0]
    week = gate.get("week")
    lane = gate.get("lane")
    expected_lane: str | None
    if week == 84:
        expected_lane = "baseline"
    elif _is_int(week) and 85 <= week <= 89:
        expected_lane = lane if lane in {"renderer", "cli"} else None
    elif week == 90:
        expected_lane = "integration"
    elif week == 91:
        expected_lane = "hardening"
    elif week == 92:
        expected_lane = "acceptance"
    else:
        expected_lane = None
    if (
        expected_lane is None
        or lane != expected_lane
        or not gate_id.startswith(f"W{week}-")
    ):
        raise ExecutorError("GATE_LANE_MANIFEST")
    context = _repository_context(repo_root)
    if context.candidate_branch_ref != _GATE_LANE_BRANCHES[expected_lane]:
        raise ExecutorError("GATE_LANE_BRANCH")
    return context


def _requirement_for_gate(repo_root: Path, gate_id: str) -> dict[str, Any]:
    manifest = _load_requirements(repo_root)
    matches = [gate for gate in manifest["gates"] if gate.get("gateId") == gate_id]
    if len(matches) != 1:
        raise ExecutorError("GATE_NOT_IN_REQUIREMENTS")
    return matches[0]


def _trusted_git_policy(
    repo_root: Path,
    manifest: Mapping[str, Any] | None = None,
) -> tuple[dict[str, Any], str, dict[str, Any]]:
    manifest = _load_requirements(repo_root) if manifest is None else manifest
    policies = manifest.get("frozenPolicies")
    policy = (
        policies.get(TRUSTED_GIT_POLICY_NAME)
        if isinstance(policies, dict)
        else None
    )
    if (
        not isinstance(policy, dict)
        or frozenset(policy) != _TRUSTED_GIT_POLICY_KEYS
        or policy.get("policyId") != TRUSTED_GIT_POLICY_ID
        or policy.get("executableRole") != TRUSTED_GIT_EXECUTABLE_ROLE
        or policy.get("version") != TRUSTED_GIT_VERSION
        or policy.get("executableBytes") != TRUSTED_GIT_EXECUTABLE_BYTES
        or policy.get("executableSha256") != TRUSTED_GIT_EXECUTABLE_SHA256
        or policy.get("shellAllowed") is not False
    ):
        raise ExecutorError("GIT_TOOL_POLICY")
    _path, identity = _trusted_git_identity(repo_root, rehash=True)
    detached = json.loads(canonical_json_bytes(policy).decode("utf-8"))
    return (
        detached,
        hashlib.sha256(canonical_json_bytes(policy)).hexdigest(),
        identity,
    )


def _git_tool_identity_record(
    *,
    policy_sha256: str,
    before: Mapping[str, Any],
    after: Mapping[str, Any] | None,
) -> dict[str, Any]:
    return {
        "policyId": TRUSTED_GIT_POLICY_ID,
        "policySha256": policy_sha256,
        "before": dict(before),
        "after": dict(after) if after is not None else None,
        "matchesBefore": after == before,
    }


def _trusted_test_policy(
    repo_root: Path, manifest: Mapping[str, Any] | None = None
) -> tuple[dict[str, Any], str]:
    manifest = _load_requirements(repo_root) if manifest is None else manifest
    policies = manifest.get("frozenPolicies")
    policy = (
        policies.get(TRUSTED_TEST_POLICY_NAME)
        if isinstance(policies, dict)
        else None
    )
    if not isinstance(policy, dict) or frozenset(policy) != _TRUSTED_TEST_POLICY_KEYS:
        raise ExecutorError("COMMAND_POLICY_SHAPE")
    arguments = policy.get("arguments")
    redacted = policy.get("redactedInvocation")
    entry_sources = policy.get("entrySources")
    read_sources = policy.get("readSources")
    if (
        policy.get("policyId") != TRUSTED_TEST_POLICY_ID
        or policy.get("commandId") != TRUSTED_TEST_COMMAND_ID
        or policy.get("executableRole") != TRUSTED_TEST_EXECUTABLE_ROLE
        or policy.get("countsSource") != TRUSTED_TEST_COUNTS_SOURCE
        or policy.get("shellAllowed") is not False
        or arguments != list(_SEMANTIC_ARGUMENTS)
        or policy.get("loaderProtocol") != SEMANTIC_LOADER_PROTOCOL
        or not isinstance(entry_sources, list)
        or len(entry_sources) != len(SEMANTIC_ENTRY_SOURCES)
        or any(
            not isinstance(binding, dict)
            or frozenset(binding) != _SEMANTIC_ENTRY_SOURCE_KEYS
            or binding.get("moduleName") != expected[0]
            or binding.get("path") != expected[1]
            or not isinstance(binding.get("sha256"), str)
            or _SHA256.fullmatch(binding["sha256"]) is None
            for binding, expected in zip(
                entry_sources, SEMANTIC_ENTRY_SOURCES, strict=True
            )
        )
        or not isinstance(read_sources, list)
        or len(read_sources) != len(SEMANTIC_READ_SOURCES)
        or any(
            not isinstance(binding, dict)
            or frozenset(binding) != _SEMANTIC_READ_SOURCE_KEYS
            or binding.get("path") != expected_path
            or not isinstance(binding.get("sha256"), str)
            or _SHA256.fullmatch(binding["sha256"]) is None
            for binding, expected_path in zip(
                read_sources, SEMANTIC_READ_SOURCES, strict=True
            )
        )
        or any(not isinstance(item, str) or "\x00" in item for item in arguments)
        or redacted != SEMANTIC_REDACTED_INVOCATION
        or any(_contains_secret(item) for item in arguments)
    ):
        raise ExecutorError("COMMAND_POLICY")
    detached = json.loads(canonical_json_bytes(policy).decode("utf-8"))
    return detached, hashlib.sha256(canonical_json_bytes(policy)).hexdigest()


def _validate_policy_command(
    argv: Sequence[str], policy: Mapping[str, Any]
) -> tuple[list[str], str]:
    command = _validate_argv(argv)
    try:
        requested = Path(command[0]).resolve(strict=True)
        current = Path(sys.executable).resolve(strict=True)
    except OSError:
        raise ExecutorError("COMMAND_POLICY_EXECUTABLE") from None
    if (
        not Path(command[0]).is_absolute()
        or os.path.normcase(str(requested)) != os.path.normcase(str(current))
        or command[1:] != policy.get("arguments")
    ):
        raise ExecutorError("COMMAND_POLICY_COMMAND")
    identity = {
        "executableRole": TRUSTED_TEST_EXECUTABLE_ROLE,
        "arguments": list(command[1:]),
    }
    return command, hashlib.sha256(canonical_json_bytes(identity)).hexdigest()


def _semantic_source_bindings(
    repo_root: Path,
    *,
    product_candidate: str,
    policy: Mapping[str, Any],
) -> dict[str, list[dict[str, str]]]:
    declared = policy.get("entrySources")
    if not isinstance(declared, list):
        raise ExecutorError("SEMANTIC_SOURCE_POLICY")
    result: list[dict[str, str]] = []
    for item, expected in zip(declared, SEMANTIC_ENTRY_SOURCES, strict=True):
        if (
            not isinstance(item, dict)
            or item.get("moduleName") != expected[0]
            or item.get("path") != expected[1]
        ):
            raise ExecutorError("SEMANTIC_SOURCE_POLICY")
        path = expected[1]
        _raw_safe_source_attributes(repo_root, path, "SEMANTIC_SOURCE_ATTRIBUTES")
        binding = _candidate_source_binding(
            repo_root,
            product_candidate=product_candidate,
            source_path=path,
            expected_sha256=item.get("sha256"),
        )
        head_line = _git(repo_root, ["ls-tree", "HEAD", "--", path]).stdout
        index_line = _git(repo_root, ["ls-files", "--stage", "--", path]).stdout
        expected_tree = f"100644 blob {binding['gitBlobSha']}\t".encode("ascii")
        expected_index = f"100644 {binding['gitBlobSha']} 0\t".encode("ascii")
        if not head_line.startswith(expected_tree) or not index_line.startswith(
            expected_index
        ):
            raise ExecutorError("SEMANTIC_SOURCE_IDENTITY")
        result.append({"moduleName": expected[0], **binding})
    declared_reads = policy.get("readSources")
    if not isinstance(declared_reads, list):
        raise ExecutorError("SEMANTIC_SOURCE_POLICY")
    read_result: list[dict[str, str]] = []
    for item, expected_path in zip(
        declared_reads, SEMANTIC_READ_SOURCES, strict=True
    ):
        if not isinstance(item, dict) or item.get("path") != expected_path:
            raise ExecutorError("SEMANTIC_SOURCE_POLICY")
        _raw_safe_source_attributes(
            repo_root, expected_path, "SEMANTIC_SOURCE_ATTRIBUTES"
        )
        binding = _candidate_source_binding(
            repo_root,
            product_candidate=product_candidate,
            source_path=expected_path,
            expected_sha256=item.get("sha256"),
        )
        head_line = _git(
            repo_root, ["ls-tree", "HEAD", "--", expected_path]
        ).stdout
        index_line = _git(
            repo_root, ["ls-files", "--stage", "--", expected_path]
        ).stdout
        expected_tree = f"100644 blob {binding['gitBlobSha']}\t".encode("ascii")
        expected_index = f"100644 {binding['gitBlobSha']} 0\t".encode("ascii")
        if not head_line.startswith(expected_tree) or not index_line.startswith(
            expected_index
        ):
            raise ExecutorError("SEMANTIC_SOURCE_IDENTITY")
        read_result.append(binding)
    bootstrap_result: list[dict[str, str]] = []
    for path in SEMANTIC_BOOTSTRAP_SOURCES:
        binding = _candidate_source_binding(
            repo_root,
            product_candidate=product_candidate,
            source_path=path,
        )
        head_line = _git(repo_root, ["ls-tree", "HEAD", "--", path]).stdout
        index_line = _git(
            repo_root, ["ls-files", "--stage", "--", path]
        ).stdout
        expected_tree = f"100644 blob {binding['gitBlobSha']}\t".encode("ascii")
        expected_index = f"100644 {binding['gitBlobSha']} 0\t".encode("ascii")
        if not head_line.startswith(expected_tree) or not index_line.startswith(
            expected_index
        ):
            raise ExecutorError("SEMANTIC_SOURCE_IDENTITY")
        bootstrap_result.append(binding)
    return {
        "entrySources": result,
        "readSources": read_result,
        "bootstrapSources": bootstrap_result,
    }


def _verify_semantic_source_bindings(
    repo_root: Path,
    bindings: Mapping[str, Sequence[Mapping[str, Any]]],
) -> None:
    for item in (
        *bindings.get("entrySources", ()),
        *bindings.get("readSources", ()),
        *bindings.get("bootstrapSources", ()),
    ):
        path = item.get("path")
        expected = item.get("sha256")
        if not isinstance(path, str) or not isinstance(expected, str):
            raise ExecutorError("SEMANTIC_SOURCE_IDENTITY")
        raw = _read_fixed_bytes(repo_root, path, limit=MAX_JSON_BYTES)
        if hashlib.sha256(raw).hexdigest() != expected:
            raise ExecutorError("SEMANTIC_SOURCE_DRIFT")


def _trusted_provider_policy(
    repo_root: Path, manifest: Mapping[str, Any] | None = None
) -> tuple[dict[str, Any], str]:
    manifest = _load_requirements(repo_root) if manifest is None else manifest
    policies = manifest.get("frozenPolicies")
    policy = (
        policies.get(TRUSTED_PROVIDER_POLICY_NAME)
        if isinstance(policies, dict)
        else None
    )
    scenario_sources = policy.get("scenarioSources") if isinstance(policy, dict) else None
    if (
        not isinstance(policy, dict)
        or frozenset(policy) != _TRUSTED_PROVIDER_POLICY_KEYS
        or policy.get("policyId") != TRUSTED_PROVIDER_POLICY_ID
        or policy.get("executableRole") != TRUSTED_TEST_EXECUTABLE_ROLE
        or policy.get("sourcePath") != TRUSTED_PROVIDER_HARNESS_PATH
        or not isinstance(policy.get("sourceSha256"), str)
        or _SHA256.fullmatch(policy["sourceSha256"]) is None
        or not isinstance(scenario_sources, dict)
        or set(scenario_sources) != set(PROVIDER_SCENARIO_PATHS)
        or any(
            not isinstance(binding, dict)
            or frozenset(binding) != _PROVIDER_SCENARIO_POLICY_KEYS
            or binding.get("path") != PROVIDER_SCENARIO_PATHS[phase]
            or not isinstance(binding.get("sha256"), str)
            or _SHA256.fullmatch(binding["sha256"]) is None
            for phase, binding in scenario_sources.items()
        )
        or policy.get("argumentsTemplate") != list(_PROVIDER_TEMPLATE_ARGUMENTS)
        or policy.get("redactedInvocation")
        != TRUSTED_PROVIDER_REDACTED_INVOCATION
        or policy.get("shellAllowed") is not False
        or policy.get("allowedBatchSizes")
        != {key: list(value) for key, value in _PROVIDER_BATCH_SIZES.items()}
        or policy.get("observedRequestProtocol")
        != PROVIDER_OBSERVED_REQUEST_PROTOCOL
    ):
        raise ExecutorError("PROVIDER_COMMAND_POLICY")
    detached = json.loads(canonical_json_bytes(policy).decode("utf-8"))
    return detached, hashlib.sha256(canonical_json_bytes(policy)).hexdigest()


def _provider_policy_command(
    policy: Mapping[str, Any],
    *,
    descriptor_path: str,
    observation_path: str,
) -> tuple[list[str], str]:
    values = {
        "{sourcePath}": TRUSTED_PROVIDER_HARNESS_PATH,
        "{descriptorPath}": descriptor_path,
        "{observationPath}": observation_path,
    }
    template = policy.get("argumentsTemplate")
    if template != list(_PROVIDER_TEMPLATE_ARGUMENTS):
        raise ExecutorError("PROVIDER_COMMAND_POLICY")
    arguments = [values.get(item, item) for item in template]
    command = [str(Path(sys.executable).resolve(strict=True)), *arguments]
    _validate_argv(command)
    identity = {
        "executableRole": TRUSTED_TEST_EXECUTABLE_ROLE,
        "arguments": arguments,
    }
    return command, hashlib.sha256(canonical_json_bytes(identity)).hexdigest()


def _trusted_product_policy(
    repo_root: Path, manifest: Mapping[str, Any] | None = None
) -> tuple[dict[str, Any], str]:
    manifest = _load_requirements(repo_root) if manifest is None else manifest
    policies = manifest.get("frozenPolicies")
    policy = (
        policies.get(TRUSTED_PRODUCT_POLICY_NAME)
        if isinstance(policies, dict)
        else None
    )
    expected_redacted = (
        "python-current -I -S -E -B -X utf8 -c "
        "<week84-92-in-memory-command-adapter-v1:{commandId}>"
    )
    if (
        not isinstance(policy, dict)
        or frozenset(policy) != _TRUSTED_PRODUCT_POLICY_KEYS
        or policy.get("policyId") != TRUSTED_PRODUCT_POLICY_ID
        or policy.get("executableRole") != ISOLATED_PYTHON_EXECUTABLE_ROLE
        or policy.get("scriptRoot") != TRUSTED_PRODUCT_SCRIPT_ROOT
        or policy.get("argumentsTemplate") != list(_PRODUCT_TEMPLATE_ARGUMENTS)
        or policy.get("redactedInvocationTemplate") != expected_redacted
        or policy.get("shellAllowed") is not False
    ):
        raise ExecutorError("PRODUCT_COMMAND_POLICY")
    detached = json.loads(canonical_json_bytes(policy).decode("utf-8"))
    return detached, hashlib.sha256(canonical_json_bytes(policy)).hexdigest()


def _product_policy_command(
    policy: Mapping[str, Any],
    *,
    script_path: str,
    script_sha256: str,
) -> tuple[list[str], str]:
    if policy.get("argumentsTemplate") != list(_PRODUCT_TEMPLATE_ARGUMENTS):
        raise ExecutorError("PRODUCT_COMMAND_POLICY")
    values = {"{scriptPath}": script_path, "{scriptSha256}": script_sha256}
    arguments = [values.get(item, item) for item in _PRODUCT_TEMPLATE_ARGUMENTS]
    try:
        executable = str(Path(sys.executable).resolve(strict=True))
    except OSError:
        raise ExecutorError("COMMAND_POLICY_EXECUTABLE") from None
    command = _validate_argv([executable, *arguments])
    identity = {
        "executableRole": ISOLATED_PYTHON_EXECUTABLE_ROLE,
        "arguments": arguments,
    }
    return command, hashlib.sha256(canonical_json_bytes(identity)).hexdigest()


def _verifier_policy() -> tuple[dict[str, Any], str]:
    policy = {
        "policyId": COMMAND_VERIFIER_POLICY_ID,
        "executableRole": ISOLATED_PYTHON_EXECUTABLE_ROLE,
        "descriptorProtocol": COMMAND_VERIFIER_DESCRIPTOR_PROTOCOL,
        "countsSource": "python-unittest-output",
        "shellAllowed": False,
    }
    return policy, hashlib.sha256(canonical_json_bytes(policy)).hexdigest()


def _verifier_policy_command() -> tuple[list[str], str]:
    try:
        executable = str(Path(sys.executable).resolve(strict=True))
    except OSError:
        raise ExecutorError("COMMAND_POLICY_EXECUTABLE") from None
    arguments = list(_VERIFIER_ARGUMENTS)
    command = _validate_argv([executable, *arguments])
    identity = {
        "executableRole": ISOLATED_PYTHON_EXECUTABLE_ROLE,
        "arguments": arguments,
    }
    return command, hashlib.sha256(canonical_json_bytes(identity)).hexdigest()


def _python_tool_identity() -> dict[str, Any]:
    try:
        executable = Path(sys.executable).resolve(strict=True)
        raw = executable.read_bytes()
    except OSError:
        raise ExecutorError("PYTHON_RUNTIME_IDENTITY") from None
    return {
        "executableRole": ISOLATED_PYTHON_EXECUTABLE_ROLE,
        "locationRole": "current-process-interpreter",
        "version": (
            f"{sys.version_info.major}.{sys.version_info.minor}."
            f"{sys.version_info.micro}"
        ),
        "executableSha256": hashlib.sha256(raw).hexdigest(),
        "runtimeTreeSha256": None,
        "runtimeTrustMode": "external-pinned-interpreter-root",
    }


def _gate_local_report_paths(
    *,
    gate_id: str,
    result_path: str,
    report_path: str,
    expected_basename: str,
    reserved_basenames: Sequence[str],
) -> tuple[str, str, str]:
    result_parts = _relative_parts(result_path)
    result_posix = PurePosixPath(*result_parts)
    if result_posix.suffix != ".json" or result_posix.parent.name != "gates":
        raise ExecutorError("RESULT_PATH")
    group_root = result_posix.parent.parent
    report_parts = _relative_parts(report_path)
    report_posix = PurePosixPath(*report_parts)
    expected_parent = group_root / "gate-evidence" / gate_id
    if (
        report_posix.suffix != ".json"
        or report_posix.parent != expected_parent
        or report_posix.name != expected_basename
        or report_posix.name in set(reserved_basenames)
    ):
        raise ExecutorError("REPORT_PATH")
    return (
        report_posix.as_posix(),
        report_posix.with_suffix(".stdout.log").as_posix(),
        report_posix.with_suffix(".stderr.log").as_posix(),
    )


def _gate_local_manifest_path(
    *,
    gate_id: str,
    result_path: str,
    manifest_path: str,
    expected_basename: str,
    reserved_basenames: Sequence[str],
) -> str:
    result_posix = PurePosixPath(*_relative_parts(result_path))
    candidate = PurePosixPath(*_relative_parts(manifest_path))
    expected_parent = result_posix.parent.parent / "gate-evidence" / gate_id
    if (
        result_posix.suffix != ".json"
        or result_posix.parent.name != "gates"
        or candidate.parent != expected_parent
        or candidate.name != expected_basename
        or candidate.name in set(reserved_basenames)
    ):
        raise ExecutorError("REPORT_PATH")
    return candidate.as_posix()


def _precondition_bindings(
    repo_root: Path,
    gate_id: str,
    product_candidate: str,
    consumed_at: str,
) -> list[dict[str, Any]]:
    manifest = _load_requirements(repo_root)
    requirements = {
        gate.get("gateId"): gate
        for gate in manifest["gates"]
        if isinstance(gate, dict)
    }
    expected_ids = CONTROLLED_WRITE_PRECONDITIONS[gate_id]
    consumed_time = _parse_timestamp(consumed_at, "LEASE_TIME")
    bindings: list[dict[str, Any]] = []
    for prerequisite_id in expected_ids:
        requirement = requirements.get(prerequisite_id)
        if not isinstance(requirement, dict):
            raise ExecutorError("PRECONDITION_REQUIREMENTS")
        result_path = requirement.get("resultPath")
        if result_path != CANONICAL_GATE_RESULT_PATHS[prerequisite_id]:
            raise ExecutorError("PRECONDITION_REQUIREMENTS")
        raw = _read_fixed_bytes(repo_root, result_path, limit=MAX_JSON_BYTES)
        gate = _read_json_bytes(raw, "PRECONDITION_JSON")
        identity = gate.get("identity") if isinstance(gate, dict) else None
        finished_at = gate.get("finishedAt") if isinstance(gate, dict) else None
        prerequisite_candidate = (
            identity.get("productCandidate") if isinstance(identity, dict) else None
        )
        if (
            not isinstance(gate, dict)
            or gate.get("gateId") != prerequisite_id
            or gate.get("status") != "Passed"
            or not isinstance(identity, dict)
            or not isinstance(prerequisite_candidate, str)
            or _SHA1.fullmatch(prerequisite_candidate) is None
            or _parse_timestamp(finished_at, "PRECONDITION_TIME") >= consumed_time
        ):
            raise ExecutorError("PRECONDITION_NOT_PASSED")
        _verify_candidate_commit(repo_root, prerequisite_candidate)
        ancestry = _git(
            repo_root,
            [
                "merge-base",
                "--is-ancestor",
                prerequisite_candidate,
                product_candidate,
            ],
            check=False,
        )
        if ancestry.returncode != 0:
            raise ExecutorError("PRECONDITION_CANDIDATE_ANCESTRY")
        bindings.append(
            {
                "gateId": prerequisite_id,
                "productCandidate": prerequisite_candidate,
                "resultPath": result_path,
                "resultSha256": hashlib.sha256(raw).hexdigest(),
                "status": "Passed",
                "finishedAt": finished_at,
            }
        )
    return bindings


def _immutable_control_document(
    repo_root: Path,
    *,
    relative_path: str,
    product_candidate: str,
    json_code: str,
    history_code: str,
) -> tuple[dict[str, Any], bytes, str]:
    raw = _read_fixed_bytes(repo_root, relative_path, limit=MAX_JSON_BYTES)
    document = _read_json_bytes(raw, json_code)
    if not isinstance(document, dict) or raw != canonical_json_bytes(document):
        raise ExecutorError(json_code)
    if _git(
        repo_root,
        ["status", "--porcelain=v1", "--untracked-files=all", "--", relative_path],
    ).stdout:
        raise ExecutorError(f"{history_code}_DIRTY")
    _git(repo_root, ["ls-files", "--error-unmatch", "--", relative_path])
    history = _decode_git_lines(
        _git(repo_root, ["log", "--format=%H", "--follow", "--", relative_path]).stdout,
        history_code,
    )
    additions = _decode_git_lines(
        _git(
            repo_root,
            ["log", "--diff-filter=A", "--format=%H", "--", relative_path],
        ).stdout,
        history_code,
    )
    if len(history) != 1 or additions != history:
        raise ExecutorError(history_code)
    commit = history[0]
    head_raw = _git(repo_root, ["show", f"HEAD:{relative_path}"]).stdout
    first_raw = _git(repo_root, ["show", f"{commit}:{relative_path}"]).stdout
    index_raw = _git(repo_root, ["show", f":{relative_path}"]).stdout
    index_lines = _decode_git_lines(
        _git(repo_root, ["ls-files", "--stage", "--", relative_path]).stdout,
        history_code,
    )
    if (
        raw != first_raw
        or head_raw != first_raw
        or index_raw != first_raw
        or len(index_lines) != 1
        or re.fullmatch(r"100644 [0-9a-f]{40} 0\t.+", index_lines[0]) is None
    ):
        raise ExecutorError(f"{history_code}_IDENTITY")
    ancestry = _git(
        repo_root,
        ["merge-base", "--is-ancestor", product_candidate, commit],
        check=False,
    )
    if ancestry.returncode != 0 or product_candidate == commit:
        raise ExecutorError(f"{history_code}_ANCESTRY")
    return document, raw, commit


def _decision_bindings(
    *,
    candidate_binding: Mapping[str, Any],
    prerequisite_bindings: Sequence[Mapping[str, Any]],
    workspace_identity: Mapping[str, Any],
    write_transition: Mapping[str, Any],
    validation_command: Mapping[str, Any],
) -> dict[str, str]:
    values = {
        "candidateBindingSha256": candidate_binding,
        "preconditionGateBindingsSha256": list(prerequisite_bindings),
        "harnessWorkspaceIdentitySha256": workspace_identity,
        "writeTransitionSha256": write_transition,
        "validationCommandSha256": validation_command,
    }
    return {
        key: hashlib.sha256(canonical_json_bytes(value)).hexdigest()
        for key, value in values.items()
    }


def verify_controlled_write_preauthorization(
    repo_root: str | os.PathLike[str],
    *,
    gate_id: str,
    product_candidate: str,
    before_time: str,
) -> PreauthorizationBinding:
    """Verify immutable approval decisions before a controlled-write lease exists."""

    root = _normalise_repo_root(repo_root)
    if gate_id not in CONTROLLED_WRITE_PREAUTHORIZATIONS:
        raise ExecutorError("CONTROLLED_GATE")
    _verify_candidate_commit(root, product_candidate)
    relative_path = CONTROLLED_WRITE_PREAUTHORIZATIONS[gate_id]
    preauthorization, raw, commit = _immutable_control_document(
        root,
        relative_path=relative_path,
        product_candidate=product_candidate,
        json_code="CONTROLLED_PREAUTHORIZATION_JSON",
        history_code="CONTROLLED_PREAUTHORIZATION_HISTORY",
    )
    authorized_at = _parse_timestamp(
        preauthorization.get("authorizedAt"),
        "CONTROLLED_PREAUTHORIZATION_TIME",
    )
    cutoff = _parse_timestamp(before_time, "CONTROLLED_PREAUTHORIZATION_TIME")
    candidate_binding = preauthorization.get("candidateBinding")
    prerequisites = preauthorization.get("preconditionGateBindings")
    workspace = preauthorization.get("harnessWorkspaceIdentity")
    transition = preauthorization.get("writeTransition")
    validation = preauthorization.get("validationCommand")
    approvals = preauthorization.get("approvalDecisionBindings")
    preauthorization_id = preauthorization.get("preauthorizationId")
    candidate_tree = _decode_git_lines(
        _git(root, ["rev-parse", f"{product_candidate}^{{tree}}"]).stdout,
        "CONTROLLED_PREAUTHORIZATION_CANDIDATE",
    )
    week = "84" if gate_id == "W84-G8" else "92"
    if (
        frozenset(preauthorization) != _PREAUTHORIZATION_KEYS
        or preauthorization.get("schemaVersion") != SCHEMA_VERSION
        or preauthorization.get("protocol")
        != CONTROLLED_WRITE_PREAUTHORIZATION_PROTOCOL
        or preauthorization.get("goalId") != GOAL_ID
        or preauthorization.get("gateId") != gate_id
        or preauthorization.get("status") != "Authorized"
        or preauthorization.get("productCandidate") != product_candidate
        or not isinstance(preauthorization_id, str)
        or _PREAUTHORIZATION_IDS[gate_id].fullmatch(preauthorization_id) is None
        or authorized_at > cutoff
        or authorized_at > datetime.now(timezone.utc) + timedelta(seconds=1)
        or not isinstance(candidate_binding, dict)
        or frozenset(candidate_binding) != _PREAUTHORIZATION_CANDIDATE_KEYS
        or candidate_binding.get("productCandidate") != product_candidate
        or len(candidate_tree) != 1
        or candidate_binding.get("treeObjectId") != candidate_tree[0]
        or not isinstance(prerequisites, list)
        or not isinstance(workspace, dict)
        or frozenset(workspace) != _WORKSPACE_IDENTITY_KEYS
        or not isinstance(transition, dict)
        or frozenset(transition) != _WRITE_TRANSITION_KEYS
        or transition != CONTROLLED_WRITE_TRANSITION
        or not isinstance(validation, dict)
        or frozenset(validation) != _VALIDATION_COMMAND_KEYS
        or validation != CONTROLLED_WRITE_VALIDATION
        or not isinstance(approvals, list)
        or len(approvals) != 2
    ):
        raise ExecutorError("CONTROLLED_PREAUTHORIZATION_BINDING")
    workspace_name = workspace.get("workspaceName")
    expected_workspace_path = (
        f"{PROVIDER_ARTIFACT_ROOTS[gate_id]}/controlled-write-workspaces/"
        f"{workspace_name}"
    )
    if (
        not isinstance(workspace.get("workspaceId"), str)
        or _WORKSPACE_IDS[gate_id].fullmatch(workspace["workspaceId"]) is None
        or not isinstance(workspace_name, str)
        or re.fullmatch(
            rf"caicli-week{week}-write-[A-Za-z0-9][A-Za-z0-9._-]{{0,63}}",
            workspace_name,
        )
        is None
        or workspace.get("workspaceRelativePath") != expected_workspace_path
        or workspace.get("owner") != CONTROLLED_WRITE_HARNESS_OWNER
        or workspace.get("scenarioPath")
        != PROVIDER_SCENARIO_PATHS["controlled-write"]
        or workspace.get("cleanupRequired") is not True
    ):
        raise ExecutorError("CONTROLLED_WORKSPACE_BINDING")
    expected_prerequisites = _precondition_bindings(
        root,
        gate_id,
        product_candidate,
        preauthorization["authorizedAt"],
    )
    if prerequisites != expected_prerequisites or any(
        not isinstance(item, dict)
        or frozenset(item) != _PRECONDITION_BINDING_KEYS
        for item in prerequisites
    ):
        raise ExecutorError("CONTROLLED_PREAUTHORIZATION_PRECONDITION")
    expected_decision_bindings = _decision_bindings(
        candidate_binding=candidate_binding,
        prerequisite_bindings=prerequisites,
        workspace_identity=workspace,
        write_transition=transition,
        validation_command=validation,
    )
    decision_paths: list[str] = []
    approval_ids: set[str] = set()
    for index, action in enumerate(("apply_patch", "shell")):
        binding = approvals[index]
        expected_path = CONTROLLED_WRITE_APPROVAL_DECISIONS[gate_id][action]
        if (
            not isinstance(binding, dict)
            or frozenset(binding) != _APPROVAL_BINDING_KEYS
            or binding.get("action") != action
            or binding.get("path") != expected_path
            or not isinstance(binding.get("approvalId"), str)
            or _APPROVAL_IDS[gate_id][action].fullmatch(binding["approvalId"])
            is None
            or not isinstance(binding.get("sha256"), str)
            or _SHA256.fullmatch(binding["sha256"]) is None
            or not isinstance(binding.get("commit"), str)
            or _SHA1.fullmatch(binding["commit"]) is None
        ):
            raise ExecutorError("CONTROLLED_APPROVAL_BINDING")
        decision, decision_raw, decision_commit = _immutable_control_document(
            root,
            relative_path=expected_path,
            product_candidate=product_candidate,
            json_code="CONTROLLED_APPROVAL_JSON",
            history_code="CONTROLLED_APPROVAL_HISTORY",
        )
        decided_at = _parse_timestamp(
            decision.get("decidedAt"),
            "CONTROLLED_APPROVAL_TIME",
        )
        decision_ancestry = _git(
            root,
            ["merge-base", "--is-ancestor", decision_commit, commit],
            check=False,
        )
        if (
            frozenset(decision) != _APPROVAL_DECISION_KEYS
            or decision.get("schemaVersion") != SCHEMA_VERSION
            or decision.get("protocol") != CONTROLLED_WRITE_APPROVAL_PROTOCOL
            or decision.get("goalId") != GOAL_ID
            or decision.get("preauthorizationId") != preauthorization_id
            or decision.get("gateId") != gate_id
            or decision.get("productCandidate") != product_candidate
            or decision.get("approvalId") != binding["approvalId"]
            or decision.get("action") != action
            or decision.get("decision") != "Approve"
            or decision.get("durable") is not True
            or not isinstance(decision.get("decisionBindings"), dict)
            or frozenset(decision["decisionBindings"])
            != _DECISION_BINDING_KEYS
            or decision["decisionBindings"] != expected_decision_bindings
            or decided_at >= authorized_at
            or binding["sha256"] != hashlib.sha256(decision_raw).hexdigest()
            or binding["commit"] != decision_commit
            or decision_ancestry.returncode != 0
            or decision_commit == commit
            or binding["approvalId"] in approval_ids
        ):
            raise ExecutorError("CONTROLLED_APPROVAL_DECISION")
        approval_ids.add(binding["approvalId"])
        decision_paths.append(expected_path)
    return PreauthorizationBinding(
        path=relative_path,
        sha256=hashlib.sha256(raw).hexdigest(),
        commit=commit,
        precondition_gate_bindings=tuple(dict(item) for item in prerequisites),
        decision_paths=tuple(decision_paths),
    )


def consume_controlled_write_lease(
    repo_root: str | os.PathLike[str],
    *,
    gate_id: str,
    product_candidate: str,
    lease_id: str | None = None,
    nonce: bytes | None = None,
    issued_at: str | None = None,
) -> dict[str, Any]:
    """Consume a candidate-bound lease by exclusively creating its tombstone.

    This function deliberately does not stage or commit the tombstone.  The
    controlled provider segment remains unavailable until a human-controlled
    Git commit makes the immutable tombstone part of the audited history.
    """

    root = _normalise_repo_root(repo_root)
    if gate_id not in CONTROLLED_WRITE_TOMBSTONES:
        raise ExecutorError("CONTROLLED_GATE")
    _verify_candidate_commit(root, product_candidate)
    manifest, _manifest_binding, _runner_binding = _bootstrap_control_preflight(
        root, product_candidate=product_candidate
    )
    _gate_lane_context(root, manifest, gate_id)
    target_requirement = _requirement_for_gate(root, gate_id)
    if (
        target_requirement.get("controlledWrite") is not True
        or target_requirement.get("resultPath")
        != CANONICAL_GATE_RESULT_PATHS[gate_id]
    ):
        raise ExecutorError("CONTROLLED_REQUIREMENT")
    if lease_id is None:
        week = "W84" if gate_id == "W84-G8" else "W92"
        lease_id = f"CW-{week}-{secrets.token_hex(8).upper()}"
    if _LEASE_IDS[gate_id].fullmatch(lease_id) is None:
        raise ExecutorError("LEASE_ID")
    nonce_value = secrets.token_bytes(32) if nonce is None else nonce
    if not isinstance(nonce_value, bytes) or len(nonce_value) < 16:
        raise ExecutorError("LEASE_NONCE")
    issued_value = issued_at or _utc_now()
    issued_time = _parse_timestamp(issued_value, "LEASE_TIME")
    wall_clock = datetime.now(timezone.utc)
    if issued_time > wall_clock:
        raise ExecutorError("LEASE_TIME")
    consumed_value = _utc_now()
    consumed_time = _parse_timestamp(consumed_value, "LEASE_TIME")
    if consumed_time <= issued_time:
        consumed_time = issued_time + timedelta(microseconds=1)
        consumed_value = consumed_time.isoformat(timespec="microseconds").replace(
            "+00:00", "Z"
        )
    if consumed_time > datetime.now(timezone.utc) + timedelta(seconds=1):
        raise ExecutorError("LEASE_TIME")
    preauthorization = verify_controlled_write_preauthorization(
        root,
        gate_id=gate_id,
        product_candidate=product_candidate,
        before_time=issued_value,
    )
    bindings = [dict(item) for item in preauthorization.precondition_gate_bindings]
    tombstone = {
        "schemaVersion": SCHEMA_VERSION,
        "goalId": GOAL_ID,
        "gateId": gate_id,
        "productCandidate": product_candidate,
        "leaseId": lease_id,
        "nonceSha256": hashlib.sha256(nonce_value).hexdigest(),
        "issuedAt": issued_value,
        "consumedAt": consumed_value,
        "preconditionGateBindings": bindings,
        "preauthorizationBinding": {
            "path": preauthorization.path,
            "sha256": preauthorization.sha256,
            "commit": preauthorization.commit,
        },
    }
    path, handle = _exclusive_open(root, CONTROLLED_WRITE_TOMBSTONES[gate_id])
    try:
        _write_open_file(handle, canonical_json_bytes(tombstone))
    finally:
        handle.close()
    # The nonce itself is intentionally never persisted or returned.
    return json.loads(canonical_json_bytes(tombstone).decode("utf-8"))


def _decode_git_lines(raw: bytes, code: str) -> list[str]:
    try:
        return [line for line in raw.decode("ascii").splitlines() if line]
    except UnicodeDecodeError:
        raise ExecutorError(code) from None


def verify_controlled_write_tombstone(
    repo_root: str | os.PathLike[str],
    *,
    gate_id: str,
    product_candidate: str,
) -> TombstoneBinding:
    root = _normalise_repo_root(repo_root)
    if gate_id not in CONTROLLED_WRITE_TOMBSTONES:
        raise ExecutorError("CONTROLLED_GATE")
    _verify_candidate_commit(root, product_candidate)
    relative_path = CONTROLLED_WRITE_TOMBSTONES[gate_id]
    raw = _read_fixed_bytes(root, relative_path, limit=MAX_JSON_BYTES)
    tombstone = _read_json_bytes(raw, "TOMBSTONE_JSON")
    if not isinstance(tombstone, dict) or frozenset(tombstone) != _TOMBSTONE_KEYS:
        raise ExecutorError("TOMBSTONE_SHAPE")
    issued = _parse_timestamp(tombstone.get("issuedAt"), "LEASE_TIME")
    consumed = _parse_timestamp(tombstone.get("consumedAt"), "LEASE_TIME")
    bindings = tombstone.get("preconditionGateBindings")
    preauthorization_binding = tombstone.get("preauthorizationBinding")
    if (
        tombstone.get("schemaVersion") != SCHEMA_VERSION
        or tombstone.get("goalId") != GOAL_ID
        or tombstone.get("gateId") != gate_id
        or tombstone.get("productCandidate") != product_candidate
        or not isinstance(tombstone.get("leaseId"), str)
        or _LEASE_IDS[gate_id].fullmatch(tombstone["leaseId"]) is None
        or not isinstance(tombstone.get("nonceSha256"), str)
        or _SHA256.fullmatch(tombstone["nonceSha256"]) is None
        or issued >= consumed
        or consumed > datetime.now(timezone.utc) + timedelta(seconds=1)
        or not isinstance(bindings, list)
        or not isinstance(preauthorization_binding, dict)
        or frozenset(preauthorization_binding) != _IMMUTABLE_BINDING_KEYS
        or not isinstance(preauthorization_binding.get("path"), str)
        or not isinstance(preauthorization_binding.get("sha256"), str)
        or _SHA256.fullmatch(preauthorization_binding["sha256"]) is None
        or not isinstance(preauthorization_binding.get("commit"), str)
        or _SHA1.fullmatch(preauthorization_binding["commit"]) is None
    ):
        raise ExecutorError("TOMBSTONE_BINDING")
    preauthorization = verify_controlled_write_preauthorization(
        root,
        gate_id=gate_id,
        product_candidate=product_candidate,
        before_time=tombstone["issuedAt"],
    )
    expected_preauthorization_binding = {
        "path": preauthorization.path,
        "sha256": preauthorization.sha256,
        "commit": preauthorization.commit,
    }
    expected_bindings = [
        dict(item) for item in preauthorization.precondition_gate_bindings
    ]
    if bindings != expected_bindings or any(
        not isinstance(item, dict)
        or frozenset(item) != _PRECONDITION_BINDING_KEYS
        for item in bindings
    ) or preauthorization_binding != expected_preauthorization_binding:
        raise ExecutorError("TOMBSTONE_PRECONDITION")

    status = _git(
        root,
        ["status", "--porcelain=v1", "--untracked-files=all", "--", relative_path],
    )
    if status.stdout:
        raise ExecutorError("TOMBSTONE_DIRTY")
    _git(root, ["ls-files", "--error-unmatch", "--", relative_path])
    history = _decode_git_lines(
        _git(root, ["log", "--format=%H", "--follow", "--", relative_path]).stdout,
        "TOMBSTONE_HISTORY",
    )
    additions = _decode_git_lines(
        _git(
            root,
            ["log", "--diff-filter=A", "--format=%H", "--", relative_path],
        ).stdout,
        "TOMBSTONE_HISTORY",
    )
    if len(history) != 1 or additions != history:
        raise ExecutorError("TOMBSTONE_HISTORY")
    first_commit = history[0]
    head_blob = _decode_git_lines(
        _git(root, ["rev-parse", f"HEAD:{relative_path}"]).stdout,
        "TOMBSTONE_GIT",
    )
    first_blob = _decode_git_lines(
        _git(root, ["rev-parse", f"{first_commit}:{relative_path}"]).stdout,
        "TOMBSTONE_GIT",
    )
    index_lines = _decode_git_lines(
        _git(root, ["ls-files", "--stage", "--", relative_path]).stdout,
        "TOMBSTONE_GIT",
    )
    if len(head_blob) != 1 or len(first_blob) != 1 or len(index_lines) != 1:
        raise ExecutorError("TOMBSTONE_GIT")
    match = re.fullmatch(r"[0-7]{6} ([0-9a-f]{40}) 0\t.+", index_lines[0])
    if (
        match is None
        or head_blob[0] != first_blob[0]
        or match.group(1) != first_blob[0]
    ):
        raise ExecutorError("TOMBSTONE_GIT")
    blob_raw = _git(root, ["cat-file", "blob", first_blob[0]]).stdout
    if blob_raw != raw:
        raise ExecutorError("TOMBSTONE_RAW_IDENTITY")
    ancestry = _git(
        root,
        ["merge-base", "--is-ancestor", product_candidate, first_commit],
        check=False,
    )
    if ancestry.returncode != 0 or product_candidate == first_commit:
        raise ExecutorError("TOMBSTONE_ANCESTRY")
    return TombstoneBinding(
        path=relative_path,
        sha256=hashlib.sha256(raw).hexdigest(),
        commit=first_commit,
        preauthorization=preauthorization,
    )


def _immutable_request_binding(
    repo_root: Path,
    *,
    relative_path: str,
    product_candidate: str,
    acceptance_id: str,
    checkpoint: str,
) -> dict[str, str]:
    parts = _relative_parts(relative_path)
    relative = PurePosixPath(*parts).as_posix()
    root_prefix = f"{USER_ACCEPTANCE_REQUEST_ROOT}/"
    if (
        not relative.startswith(root_prefix)
        or PurePosixPath(relative).parent.as_posix()
        != USER_ACCEPTANCE_REQUEST_ROOT
        or PurePosixPath(relative).suffix != ".json"
    ):
        raise ExecutorError("MANUAL_REQUEST_PATH")
    raw = _read_fixed_bytes(repo_root, relative, limit=MAX_JSON_BYTES)
    document = _read_json_bytes(raw, "MANUAL_REQUEST_JSON")
    request_id = document.get("decisionRequestId") if isinstance(document, dict) else None
    if (
        not isinstance(document, dict)
        or frozenset(document) != _USER_ACCEPTANCE_REQUEST_KEYS
        or document.get("schemaVersion") != SCHEMA_VERSION
        or document.get("goalId") != GOAL_ID
        or document.get("acceptanceId") != acceptance_id
        or document.get("checkpoint") != checkpoint
        or document.get("candidate") != product_candidate
        or document.get("status") != "AwaitingUser"
        or not isinstance(request_id, str)
        or _DECISION_REQUEST_ID.fullmatch(request_id) is None
        or PurePosixPath(relative).name != f"{request_id}.json"
        or not isinstance(document.get("manifestSha256"), str)
        or _SHA256.fullmatch(document["manifestSha256"]) is None
        or not isinstance(document.get("challengeCode"), str)
        or _CHALLENGE_CODE.fullmatch(document["challengeCode"]) is None
    ):
        raise ExecutorError("MANUAL_REQUEST_BINDING")
    _parse_timestamp(document.get("requestedAt"), "MANUAL_REQUEST_TIME")
    first_add = _decode_git_lines(
        _git(
            repo_root,
            ["log", "--diff-filter=A", "--format=%H", "--reverse", "--", relative],
        ).stdout,
        "MANUAL_REQUEST_GIT",
    )
    history = _decode_git_lines(
        _git(
            repo_root,
            ["log", "--format=%H", "--reverse", "--", relative],
        ).stdout,
        "MANUAL_REQUEST_GIT",
    )
    if (
        len(first_add) != 1
        or _SHA1.fullmatch(first_add[0]) is None
        or history != first_add
    ):
        raise ExecutorError("MANUAL_REQUEST_HISTORY")
    commit = first_add[0]
    ancestry = _git(
        repo_root,
        ["merge-base", "--is-ancestor", product_candidate, commit],
        check=False,
    )
    if ancestry.returncode != 0 or commit == product_candidate:
        raise ExecutorError("MANUAL_REQUEST_ANCESTRY")
    head_raw = _git(repo_root, ["show", f"HEAD:{relative}"]).stdout
    first_raw = _git(repo_root, ["show", f"{commit}:{relative}"]).stdout
    index_raw = _git(repo_root, ["show", f":{relative}"]).stdout
    if raw != first_raw or head_raw != first_raw or index_raw != first_raw:
        raise ExecutorError("MANUAL_REQUEST_IDENTITY")
    return {
        "path": relative,
        "sha256": hashlib.sha256(raw).hexdigest(),
        "commit": commit,
    }


def _descendant_changed_paths(
    repo_root: Path,
    *,
    product_candidate: str,
    head_commit: str,
) -> tuple[str, ...]:
    ancestry = _git(
        repo_root,
        ["merge-base", "--is-ancestor", product_candidate, head_commit],
        check=False,
    )
    if ancestry.returncode != 0 or product_candidate == head_commit:
        raise ExecutorError("CHECKOUT_ANCESTRY")
    commit_lines = _decode_git_lines(
        _git(
            repo_root,
            ["rev-list", "--reverse", "--parents", f"{product_candidate}..{head_commit}"],
        ).stdout,
        "CHECKOUT_HISTORY",
    )
    if not commit_lines:
        raise ExecutorError("CHECKOUT_HISTORY")
    changed: list[str] = []
    expected_parent = product_candidate
    for line in commit_lines:
        words = line.split(" ")
        if (
            len(words) != 2
            or _SHA1.fullmatch(words[0]) is None
            or words[1] != expected_parent
        ):
            raise ExecutorError("CHECKOUT_HISTORY")
        paths = _decode_git_lines(
            _git(
                repo_root,
                ["diff-tree", "--no-commit-id", "--name-only", "-r", words[0]],
            ).stdout,
            "CHECKOUT_HISTORY",
        )
        if not paths:
            raise ExecutorError("CHECKOUT_HISTORY")
        changed.extend(paths)
        expected_parent = words[0]
    if expected_parent != head_commit:
        raise ExecutorError("CHECKOUT_HISTORY")
    return tuple(changed)


def _gate_checkout_preflight(
    repo_root: Path,
    *,
    gate_id: str,
    product_candidate: str,
) -> tuple[str, str, CheckoutSnapshot]:
    _verify_candidate_commit(repo_root, product_candidate)
    initial = _capture_checkout_snapshot(repo_root)
    if not initial.clean:
        raise ExecutorError("CHECKOUT_DIRTY")
    controlled_gate = CONTROLLED_DESCENDANT_GATES.get(gate_id)
    manual = MANUAL_DESCENDANT_GATES.get(gate_id)
    boundary_path = PROVIDER_BOUNDARY_DECISION_PATHS.get(gate_id)
    if controlled_gate is None and manual is None and boundary_path is None:
        if initial.head_commit != product_candidate:
            raise ExecutorError("CHECKOUT_HEAD")
        return product_candidate, "candidate-exact", initial

    changed = _descendant_changed_paths(
        repo_root,
        product_candidate=product_candidate,
        head_commit=initial.head_commit,
    )
    allowed: set[str] = set()
    if boundary_path is not None:
        _decision, _raw, _commit = _provider_boundary_immutable_binding(
            repo_root,
            gate_id=gate_id,
            product_candidate=product_candidate,
        )
        allowed.add(boundary_path)
    if controlled_gate is not None:
        binding = verify_controlled_write_tombstone(
            repo_root,
            gate_id=controlled_gate,
            product_candidate=product_candidate,
        )
        allowed.add(binding.path)
        allowed.add(binding.preauthorization.path)
        allowed.update(binding.preauthorization.decision_paths)

    if manual is not None:
        acceptance_id, checkpoint = manual
        request_paths = sorted(
            {
                path
                for path in changed
                if path.startswith(f"{USER_ACCEPTANCE_REQUEST_ROOT}/")
            }
        )
        if not request_paths:
            raise ExecutorError("MANUAL_REQUEST_MISSING")
        for request_path in request_paths:
            binding = _immutable_request_binding(
                repo_root,
                relative_path=request_path,
                product_candidate=product_candidate,
                acceptance_id=acceptance_id,
                checkpoint=checkpoint,
            )
            allowed.add(binding["path"])

    if set(changed) != allowed:
        raise ExecutorError("CHECKOUT_DESCENDANT_SCOPE")
    if boundary_path is not None and controlled_gate is not None:
        mode = "provider-boundary-and-controlled-descendant"
    elif boundary_path is not None:
        mode = "provider-boundary-descendant"
    elif controlled_gate is not None and manual is not None:
        mode = "controlled-and-manual-descendant"
    elif controlled_gate is not None:
        mode = "controlled-tombstone-descendant"
    else:
        mode = "manual-challenge-descendant"
    return initial.head_commit, mode, initial


def _provider_checkout_preflight(
    repo_root: Path,
    *,
    gate_id: str,
    phase: str,
    product_candidate: str,
) -> tuple[str, str, CheckoutSnapshot]:
    if phase not in PROVIDER_SCENARIO_PATHS:
        raise ExecutorError("SEGMENT_PHASES")
    return _gate_checkout_preflight(
        repo_root,
        gate_id=gate_id,
        product_candidate=product_candidate,
    )


def _raw_safe_source_attributes(
    repo_root: Path,
    relative_path: str,
    code: str = "PROVIDER_DRIVER_ATTRIBUTES",
) -> None:
    output = _git(
        repo_root,
        [
            "check-attr",
            "text",
            "filter",
            "working-tree-encoding",
            "ident",
            "--",
            relative_path,
        ],
    ).stdout
    lines = _decode_git_lines(output, code)
    observed: dict[str, str] = {}
    for line in lines:
        fields = line.rsplit(": ", 2)
        if len(fields) == 3:
            observed[fields[1]] = fields[2]
    if observed != {
        "text": "unset",
        "filter": "unspecified",
        "working-tree-encoding": "unspecified",
        "ident": "unspecified",
    }:
        raise ExecutorError(code)
    info_path_raw = _git(
        repo_root, ["rev-parse", "--git-path", "info/attributes"]
    ).stdout
    try:
        info_value = info_path_raw.decode("utf-8").strip()
    except UnicodeDecodeError:
        raise ExecutorError(code) from None
    info_path = Path(info_value)
    if not info_path.is_absolute():
        info_path = repo_root / info_path
    if info_path.exists() or info_path.is_symlink():
        if _is_reparse_or_link(info_path) or not info_path.is_file():
            raise ExecutorError(code)
        try:
            info_lines = info_path.read_text(
                encoding="utf-8", errors="replace"
            ).splitlines()
        except OSError:
            raise ExecutorError(code) from None
        forbidden = {"filter", "working-tree-encoding", "ident"}
        for line in info_lines:
            stripped = line.strip()
            if not stripped or stripped.startswith("#"):
                continue
            tokens = stripped.split()
            defined = {
                token.lstrip("-!").split("=", 1)[0]
                for token in tokens[1:]
            }
            if defined & forbidden:
                raise ExecutorError(code)


def _single_add_revision(repo_root: Path, relative_path: str) -> str:
    additions = _decode_git_lines(
        _git(
            repo_root,
            ["log", "--diff-filter=A", "--format=%H", "--", relative_path],
        ).stdout,
        "COMMAND_CONTROL_HISTORY",
    )
    if len(additions) != 1:
        raise ExecutorError("COMMAND_CONTROL_HISTORY")
    return additions[0]


def _revision_blob(
    repo_root: Path,
    revision: str,
    relative_path: str,
    code: str,
) -> tuple[str, bytes]:
    line_raw = _git(
        repo_root, ["ls-tree", revision, "--", relative_path]
    ).stdout
    try:
        line = line_raw.decode("utf-8").strip()
    except UnicodeDecodeError:
        raise ExecutorError(code) from None
    match = re.fullmatch(r"100644 blob ([0-9a-f]{40})\t.+", line)
    if match is None:
        raise ExecutorError(code)
    blob = match.group(1)
    raw = _git(repo_root, ["cat-file", "blob", blob]).stdout
    return blob, raw


def _command_control_requirement(
    manifest: Mapping[str, Any], gate_id: str
) -> tuple[dict[str, Any], dict[str, Any]]:
    matches = [
        item
        for item in manifest.get("gates", [])
        if isinstance(item, dict) and item.get("gateId") == gate_id
    ]
    requirement = matches[0] if len(matches) == 1 else None
    policy = requirement.get("commandControl") if isinstance(requirement, dict) else None
    if (
        not isinstance(requirement, dict)
        or not isinstance(policy, dict)
        or frozenset(policy)
        != {"path", "sourceTrust", "predecessorMode", "rolePolicy"}
        or policy.get("path")
        != f"docs_md/weekly/84_92_command_control/{gate_id}.json"
        or policy.get("sourceTrust")
        not in {"prior-sealed", "bootstrap-external-review"}
        or policy.get("predecessorMode")
        not in {
            "w84-bootstrap-exception",
            "entry-base",
            "prior-gate",
            "w90-integration-entry-base",
        }
        or policy.get("rolePolicy") != _COMMAND_CONTROL_ROLE_POLICY
    ):
        raise ExecutorError("COMMAND_CONTROL_POLICY")
    return requirement, policy


def _origin_control_declares_source(
    repo_root: Path,
    *,
    origin_revision: str,
    source: Mapping[str, Any],
) -> bool:
    changed = _decode_git_lines(
        _git(
            repo_root,
            [
                "diff-tree",
                "--no-commit-id",
                "--name-only",
                "-r",
                origin_revision,
            ],
        ).stdout,
        "COMMAND_SOURCE_ORIGIN",
    )
    controls = [
        path
        for path in changed
        if path.startswith("docs_md/weekly/84_92_command_control/")
        and path.endswith(".json")
    ]
    if len(controls) != 1:
        return False
    try:
        raw = _git(
            repo_root, ["show", f"{origin_revision}:{controls[0]}"]
        ).stdout
        document = _read_json_bytes(raw, "COMMAND_SOURCE_ORIGIN")
    except ExecutorError:
        return False
    sources = document.get("sources") if isinstance(document, dict) else None
    if not isinstance(sources, list):
        return False
    matches = [
        item
        for item in sources
        if isinstance(item, dict)
        and item.get("path") == source.get("path")
        and item.get("role") == source.get("role")
        and item.get("sha256") == source.get("sha256")
        and item.get("gitBlobSha") == source.get("gitBlobSha")
    ]
    return len(matches) == 1


def _sealed_command_source_binding(
    repo_root: Path,
    *,
    gate_id: str,
    product_candidate: str,
    control_revision: str,
    prepared_from_revision: str,
    source: Mapping[str, Any],
) -> dict[str, str]:
    if frozenset(source) != _COMMAND_SOURCE_KEYS:
        raise ExecutorError("COMMAND_SOURCE_SHAPE")
    path = source.get("path")
    role = source.get("role")
    origin = source.get("origin")
    origin_revision = source.get("originControlRevision")
    if (
        not isinstance(path, str)
        or not path
        or role not in _COMMAND_CONTROL_ROLES
        or origin not in _COMMAND_CONTROL_ORIGINS
        or not isinstance(source.get("sha256"), str)
        or _SHA256.fullmatch(source["sha256"]) is None
        or not isinstance(source.get("gitBlobSha"), str)
        or _SHA1.fullmatch(source["gitBlobSha"]) is None
    ):
        raise ExecutorError("COMMAND_SOURCE_SHAPE")
    raw = _read_fixed_bytes(repo_root, path, limit=MAX_JSON_BYTES)
    control_blob, control_raw = _revision_blob(
        repo_root, control_revision, path, "COMMAND_SOURCE_IDENTITY"
    )
    candidate_blob, candidate_raw = _revision_blob(
        repo_root, product_candidate, path, "COMMAND_SOURCE_IDENTITY"
    )
    head_blob, head_raw = _revision_blob(
        repo_root, "HEAD", path, "COMMAND_SOURCE_IDENTITY"
    )
    index_line = _git(
        repo_root, ["ls-files", "--stage", "--", path]
    ).stdout
    try:
        index_text = index_line.decode("utf-8").strip()
    except UnicodeDecodeError:
        raise ExecutorError("COMMAND_SOURCE_IDENTITY") from None
    if (
        hashlib.sha256(raw).hexdigest() != source["sha256"]
        or control_blob != source["gitBlobSha"]
        or candidate_blob != control_blob
        or head_blob != control_blob
        or not index_text.startswith(f"100644 {control_blob} 0\t")
        or control_raw != raw
        or candidate_raw != raw
        or head_raw != raw
        or _git(
            repo_root,
            ["status", "--porcelain=v1", "--untracked-files=all", "--", path],
        ).stdout
        or _git(
            repo_root,
            [
                "log",
                "--format=%H",
                f"{control_revision}..{product_candidate}",
                "--",
                path,
            ],
        ).stdout
    ):
        raise ExecutorError("COMMAND_SOURCE_IDENTITY")
    _raw_safe_source_attributes(repo_root, path, "COMMAND_SOURCE_ATTRIBUTES")
    if origin == "bootstrap":
        if (
            gate_id != "W84-G0"
            or origin_revision is not None
            or product_candidate != control_revision
            or _single_add_revision(repo_root, path) != control_revision
        ):
            raise ExecutorError("COMMAND_SOURCE_ORIGIN")
        effective_revision = control_revision
    elif origin == "prepared":
        if origin_revision is not None:
            raise ExecutorError("COMMAND_SOURCE_ORIGIN")
        parent_tree = _git(
            repo_root, ["ls-tree", prepared_from_revision, "--", path]
        ).stdout
        if parent_tree:
            if role != "fixture":
                raise ExecutorError("COMMAND_SOURCE_ORIGIN")
            parent_blob, _parent_raw = _revision_blob(
                repo_root,
                prepared_from_revision,
                path,
                "COMMAND_SOURCE_ORIGIN",
            )
            if parent_blob == control_blob:
                raise ExecutorError("COMMAND_SOURCE_ORIGIN")
        elif _single_add_revision(repo_root, path) != control_revision:
            raise ExecutorError("COMMAND_SOURCE_ORIGIN")
        effective_revision = control_revision
    else:
        if (
            not isinstance(origin_revision, str)
            or _SHA1.fullmatch(origin_revision) is None
            or origin_revision == control_revision
            or _git(
                repo_root,
                [
                    "merge-base",
                    "--is-ancestor",
                    origin_revision,
                    prepared_from_revision,
                ],
                check=False,
            ).returncode
            != 0
            or _git(
                repo_root,
                [
                    "log",
                    "--format=%H",
                    f"{origin_revision}..{control_revision}",
                    "--",
                    path,
                ],
            ).stdout
            or not _origin_control_declares_source(
                repo_root,
                origin_revision=origin_revision,
                source=source,
            )
        ):
            raise ExecutorError("COMMAND_SOURCE_ORIGIN")
        origin_blob, origin_raw = _revision_blob(
            repo_root, origin_revision, path, "COMMAND_SOURCE_ORIGIN"
        )
        if origin_blob != control_blob or origin_raw != raw:
            raise ExecutorError("COMMAND_SOURCE_ORIGIN")
        effective_revision = origin_revision
    return {
        "path": path,
        "sha256": source["sha256"],
        "gitBlobSha": source["gitBlobSha"],
        "controlRevision": effective_revision,
    }


def _local_python_references(
    repo_root: Path,
    path: str,
    raw: bytes,
) -> set[str]:
    if not path.endswith(".py"):
        return set()
    try:
        tree = ast.parse(raw.decode("utf-8"), filename=path)
    except (UnicodeDecodeError, SyntaxError):
        raise ExecutorError("COMMAND_SOURCE_PARSE") from None
    references: set[str] = set()
    for node in ast.walk(tree):
        candidates: list[str] = []
        if isinstance(node, ast.Import):
            candidates.extend(alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.level == 0 and node.module:
            candidates.append(node.module)
        elif isinstance(node, ast.Constant) and isinstance(node.value, str):
            value = node.value
            if (
                "/" in value
                or "\\" in value
                or re.fullmatch(
                    r"[A-Za-z0-9_.-]+\.(?:json|jsonl|txt|md|py|toml|ya?ml)",
                    value,
                )
                is not None
            ):
                candidate = value.replace("\\", "/")
                try:
                    target = repo_root.joinpath(*_relative_parts(candidate))
                except ExecutorError:
                    continue
                if target.is_file():
                    references.add(candidate)
            elif re.fullmatch(r"[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+", value):
                candidates.append(value)
        for module in candidates:
            components = module.split(".")
            for length in range(len(components), 0, -1):
                candidate = "/".join(components[:length]) + ".py"
                if repo_root.joinpath(*_relative_parts(candidate)).is_file():
                    references.add(candidate)
                    break
    return references


def _command_control_preflight(
    repo_root: Path,
    *,
    manifest: Mapping[str, Any],
    gate_id: str,
    product_candidate: str,
    command_id: str,
) -> CommandControlBinding:
    requirement, policy = _command_control_requirement(manifest, gate_id)
    control_path = policy["path"]
    raw = _read_fixed_bytes(repo_root, control_path, limit=MAX_JSON_BYTES)
    document = _read_json_bytes(raw, "COMMAND_CONTROL_JSON")
    if (
        not isinstance(document, dict)
        or raw != canonical_json_bytes(document)
        or frozenset(document) != _COMMAND_CONTROL_KEYS
        or document.get("schemaVersion") != SCHEMA_VERSION
        or document.get("protocol") != COMMAND_CONTROL_PROTOCOL
        or document.get("goalId") != GOAL_ID
        or document.get("gateId") != gate_id
        or document.get("sourceTrust") != policy["sourceTrust"]
        or not isinstance(document.get("preparedFromRevision"), str)
        or _SHA1.fullmatch(document["preparedFromRevision"]) is None
        or not isinstance(document.get("sources"), list)
        or not document["sources"]
        or not isinstance(document.get("sourceTrees"), list)
        or not isinstance(document.get("runtimeDrivers"), list)
    ):
        raise ExecutorError("COMMAND_CONTROL_SHAPE")
    control_source = _bootstrap_source_binding(
        repo_root,
        product_candidate=product_candidate,
        source_path=control_path,
        expected_sha256=hashlib.sha256(raw).hexdigest(),
    )
    control_revision = control_source["controlRevision"]
    prepared = document["preparedFromRevision"]
    parent_line = _decode_git_lines(
        _git(
            repo_root, ["rev-list", "--parents", "-n", "1", control_revision]
        ).stdout,
        "COMMAND_CONTROL_PARENT",
    )
    if parent_line != [f"{control_revision} {prepared}"]:
        raise ExecutorError("COMMAND_CONTROL_PARENT")
    bootstrap = (
        gate_id == "W84-G0"
        and policy["sourceTrust"] == "bootstrap-external-review"
        and policy["predecessorMode"] == "w84-bootstrap-exception"
    )
    if bootstrap:
        if product_candidate != control_revision:
            raise ExecutorError("COMMAND_CONTROL_ANCESTRY")
    elif (
        policy["sourceTrust"] != "prior-sealed"
        or policy["predecessorMode"] == "w84-bootstrap-exception"
        or product_candidate == control_revision
        or _git(
            repo_root,
            ["merge-base", "--is-ancestor", control_revision, product_candidate],
            check=False,
        ).returncode
        != 0
    ):
        raise ExecutorError("COMMAND_CONTROL_ANCESTRY")
    sources = document["sources"]
    if any(not isinstance(item, dict) for item in sources):
        raise ExecutorError("COMMAND_SOURCE_SHAPE")
    role_order = {
        role: index
        for index, role in enumerate(
            ("adapter", "oracle", "test", "fixture", "parser")
        )
    }
    projection = [
        (
            str(item.get("commandId")),
            role_order.get(str(item.get("role")), 99),
            str(item.get("path")),
        )
        for item in sources
    ]
    if projection != sorted(projection) or len(set(projection)) != len(projection):
        raise ExecutorError("COMMAND_SOURCE_ORDER")
    required_commands = requirement.get("requiredCommandIds")
    product_commands = {
        item
        for item in required_commands
        if isinstance(required_commands, list)
        and isinstance(item, str)
        and item != TRUSTED_TEST_COMMAND_ID
    } if isinstance(required_commands, list) else set()
    if command_id not in product_commands:
        raise ExecutorError("COMMAND_NOT_IN_REQUIREMENTS")
    bindings_by_index: list[dict[str, str]] = []
    raw_by_path: dict[str, bytes] = {}
    for item in sources:
        if item.get("commandId") not in product_commands:
            raise ExecutorError("COMMAND_SOURCE_SHAPE")
        role = item.get("role")
        verification = item.get("verification")
        if role in {"adapter", "fixture", "parser"}:
            if verification is not None:
                raise ExecutorError("COMMAND_VERIFICATION_SHAPE")
        else:
            module = str(item.get("path", ""))
            module = module[:-3].replace("/", ".") if module.endswith(".py") else ""
            arguments = (
                verification.get("arguments")
                if isinstance(verification, dict)
                else None
            )
            verifies = (
                verification.get("verifiesCommandIds")
                if isinstance(verification, dict)
                else None
            )
            if (
                not isinstance(verification, dict)
                or frozenset(verification) != _VERIFICATION_KEYS
                or not isinstance(arguments, list)
                or not arguments
                or len(set(arguments)) != len(arguments)
                or any(
                    not isinstance(argument, str)
                    or not argument.startswith(f"{module}.")
                    for argument in arguments
                )
                or not isinstance(verifies, list)
                or not verifies
                or len(set(verifies)) != len(verifies)
                or any(value not in product_commands for value in verifies)
            ):
                raise ExecutorError("COMMAND_VERIFICATION_SHAPE")
        binding = _sealed_command_source_binding(
            repo_root,
            gate_id=gate_id,
            product_candidate=product_candidate,
            control_revision=control_revision,
            prepared_from_revision=prepared,
            source=item,
        )
        bindings_by_index.append(binding)
        raw_by_path.setdefault(binding["path"], _read_fixed_bytes(
            repo_root, binding["path"], limit=MAX_JSON_BYTES
        ))
    command_indexes = [
        index for index, item in enumerate(sources) if item.get("commandId") == command_id
    ]
    adapters = [
        index for index in command_indexes if sources[index].get("role") == "adapter"
    ]
    verifiers = [
        index
        for index in command_indexes
        if sources[index].get("role") in {"oracle", "test"}
        and command_id
        in sources[index]["verification"]["verifiesCommandIds"]
    ]
    expected_adapter = f"{TRUSTED_PRODUCT_SCRIPT_ROOT}/{command_id}.py"
    if (
        len(adapters) != 1
        or sources[adapters[0]].get("path") != expected_adapter
        or not verifiers
        or any(sources[index].get("path") == expected_adapter for index in verifiers)
    ):
        raise ExecutorError("COMMAND_ROLE_POLICY")
    forbidden_marker = re.compile(rb"CAICLI_[A-Z0-9_]*(?:RESULT|COUNTS)[A-Z0-9_]*=")
    if forbidden_marker.search(raw_by_path[expected_adapter]):
        raise ExecutorError("ADAPTER_SELF_ATTESTATION")
    declared_paths = {str(item.get("path")) for item in sources}
    allowed_runtime_paths = {
        path for path, _kind in _W84_G0_RUNTIME_INPUTS.get(command_id, ())
    } if gate_id == "W84-G0" else set()
    for index in command_indexes:
        for reference in _local_python_references(
            repo_root,
            str(sources[index].get("path")),
            raw_by_path[str(sources[index].get("path"))],
        ):
            if reference.startswith("artifacts/"):
                if gate_id == "W84-G0" and reference not in allowed_runtime_paths:
                    raise ExecutorError("RUNTIME_INPUT_POLICY")
                continue
            if reference not in declared_paths:
                raise ExecutorError("COMMAND_SOURCE_DEPENDENCY")
    source_trees = document["sourceTrees"]
    if source_trees:
        # Future dotnet/vitest/playwright authority stays fail closed until its
        # independent discovery/result reconciliation runner is implemented.
        raise ExecutorError("COMMAND_RUNNER_UNSUPPORTED")
    prepared_paths = {
        str(item.get("path"))
        for item in sources
        if item.get("origin") in {"prepared", "bootstrap"}
    }
    changed = set(
        _decode_git_lines(
            _git(
                repo_root,
                [
                    "diff-tree",
                    "--no-commit-id",
                    "--name-only",
                    "-r",
                    control_revision,
                ],
            ).stdout,
            "COMMAND_CONTROL_DIFF",
        )
    )
    if changed != {control_path, *prepared_paths}:
        raise ExecutorError("COMMAND_CONTROL_DIFF")
    binding = {
        "path": control_path,
        "sha256": control_source["sha256"],
        "gitBlobSha": control_source["gitBlobSha"],
        "controlRevision": control_revision,
        "preparedFromRevision": prepared,
        "sourceTrust": document["sourceTrust"],
    }
    detached_sources = tuple(
        {
            **json.loads(canonical_json_bytes(sources[index]).decode("utf-8")),
            "_binding": bindings_by_index[index],
        }
        for index in command_indexes
    )
    return CommandControlBinding(
        document=json.loads(canonical_json_bytes(document).decode("utf-8")),
        binding=binding,
        sources=detached_sources,
        source_trees=(),
    )


def _runtime_input_bindings(
    repo_root: Path,
    *,
    gate_id: str,
    command_id: str,
    control: CommandControlBinding,
) -> list[dict[str, str]]:
    if gate_id == "W84-G0":
        declared = _W84_G0_RUNTIME_INPUTS.get(command_id)
        if declared is None:
            raise ExecutorError("RUNTIME_INPUT_POLICY")
    else:
        declared = ()
        artifact_literal = re.compile(
            rb"artifacts/[A-Za-z0-9_.\-/]+(?:\.json|\.jsonl)"
        )
        for source in control.sources:
            raw = _read_fixed_bytes(
                repo_root, source["path"], limit=MAX_JSON_BYTES
            )
            if artifact_literal.search(raw):
                raise ExecutorError("RUNTIME_INPUT_POLICY")
    result: list[dict[str, str]] = []
    for path, kind in sorted(declared):
        if kind not in _RUNTIME_INPUT_KINDS:
            raise ExecutorError("RUNTIME_INPUT_POLICY")
        raw = _read_fixed_bytes(repo_root, path, limit=MAX_JSON_BYTES)
        result.append(
            {
                "path": path,
                "sha256": hashlib.sha256(raw).hexdigest(),
                "kind": kind,
            }
        )
    return result


def _verify_runtime_inputs(
    repo_root: Path,
    bindings: Sequence[Mapping[str, Any]],
) -> None:
    if [str(item.get("path")) for item in bindings] != sorted(
        str(item.get("path")) for item in bindings
    ):
        raise ExecutorError("RUNTIME_INPUT_ORDER")
    for item in bindings:
        if (
            not isinstance(item, Mapping)
            or frozenset(item) != {"path", "sha256", "kind"}
            or item.get("kind") not in _RUNTIME_INPUT_KINDS
            or not isinstance(item.get("path"), str)
            or not isinstance(item.get("sha256"), str)
            or _SHA256.fullmatch(item["sha256"]) is None
        ):
            raise ExecutorError("RUNTIME_INPUT_SHAPE")
        raw = _read_fixed_bytes(repo_root, item["path"], limit=MAX_JSON_BYTES)
        if hashlib.sha256(raw).hexdigest() != item["sha256"]:
            raise ExecutorError("RUNTIME_INPUT_DRIFT")


def _provider_runtime_driver_binding(
    repo_root: Path,
    *,
    manifest: Mapping[str, Any],
    gate_id: str,
    product_candidate: str,
    phase: str,
) -> dict[str, str]:
    requirements = [
        item
        for item in manifest.get("gates", [])
        if isinstance(item, dict) and item.get("gateId") == gate_id
    ]
    command_control = (
        requirements[0].get("commandControl") if len(requirements) == 1 else None
    )
    if (
        not isinstance(command_control, dict)
        or not isinstance(command_control.get("path"), str)
        or not isinstance(command_control.get("sourceTrust"), str)
    ):
        raise ExecutorError("PROVIDER_DRIVER_CONTROL")
    control_path = command_control["path"]
    raw = _read_fixed_bytes(repo_root, control_path, limit=MAX_JSON_BYTES)
    document = _read_json_bytes(raw, "PROVIDER_DRIVER_CONTROL")
    if (
        not isinstance(document, dict)
        or raw != canonical_json_bytes(document)
        or frozenset(document) != _COMMAND_CONTROL_KEYS
        or document.get("schemaVersion") != SCHEMA_VERSION
        or document.get("protocol") != COMMAND_CONTROL_PROTOCOL
        or document.get("goalId") != GOAL_ID
        or document.get("gateId") != gate_id
        or document.get("sourceTrust") != command_control["sourceTrust"]
        or not isinstance(document.get("preparedFromRevision"), str)
        or _SHA1.fullmatch(document["preparedFromRevision"]) is None
        or not isinstance(document.get("sources"), list)
        or not document["sources"]
        or not isinstance(document.get("sourceTrees"), list)
        or not isinstance(document.get("runtimeDrivers"), list)
    ):
        raise ExecutorError("PROVIDER_DRIVER_CONTROL")
    control_source = _bootstrap_source_binding(
        repo_root,
        product_candidate=product_candidate,
        source_path=control_path,
        expected_sha256=hashlib.sha256(raw).hexdigest(),
    )
    expected_phases = [
        candidate_phase
        for candidate_phase in PROVIDER_DRIVER_PATHS
        if candidate_phase in set(PROVIDER_PHASE_LAYOUT[gate_id])
    ]
    runtime_drivers = document["runtimeDrivers"]
    if (
        len(runtime_drivers) != len(expected_phases)
        or any(not isinstance(item, dict) for item in runtime_drivers)
        or [item.get("phase") for item in runtime_drivers] != expected_phases
        or any(frozenset(item) != _RUNTIME_DRIVER_KEYS for item in runtime_drivers)
    ):
        raise ExecutorError("PROVIDER_DRIVER_CONTROL")
    matches = [item for item in runtime_drivers if item.get("phase") == phase]
    if len(matches) != 1:
        raise ExecutorError("PROVIDER_DRIVER_SOURCE")
    declared = matches[0]
    driver_path = PROVIDER_DRIVER_PATHS[phase]
    if (
        declared.get("path") != driver_path
        or not isinstance(declared.get("sha256"), str)
        or _SHA256.fullmatch(declared["sha256"]) is None
        or not isinstance(declared.get("gitBlobSha"), str)
        or _SHA1.fullmatch(declared["gitBlobSha"]) is None
        or not isinstance(declared.get("originControlRevision"), str)
        or _SHA1.fullmatch(declared["originControlRevision"]) is None
    ):
        raise ExecutorError("PROVIDER_DRIVER_SOURCE")
    try:
        driver_raw = _read_fixed_bytes(repo_root, driver_path, limit=MAX_JSON_BYTES)
        if re.search(
            rb"(?:from\s*['\"]\.|import\s*\(\s*['\"]\.|require\s*\(\s*['\"]\.)",
            driver_raw,
        ):
            raise ExecutorError("PROVIDER_DRIVER_IMPORT")
        binding = _bootstrap_source_binding(
            repo_root,
            product_candidate=product_candidate,
            source_path=driver_path,
            expected_sha256=declared["sha256"],
        )
        if (
            binding["gitBlobSha"] != declared["gitBlobSha"]
            or binding["controlRevision"]
            != declared["originControlRevision"]
            or _git(
                repo_root,
                ["show", f"{control_source['controlRevision']}:{driver_path}"],
            ).stdout
            != driver_raw
        ):
            raise ExecutorError("PROVIDER_DRIVER_SOURCE")
        origin_revision = declared["originControlRevision"]
        prepared_revision = document["preparedFromRevision"]
        ancestry = _git(
            repo_root,
            [
                "merge-base",
                "--is-ancestor",
                origin_revision,
                prepared_revision,
            ],
            check=False,
        )
        if (
            ancestry.returncode != 0
            or origin_revision == control_source["controlRevision"]
        ):
            raise ExecutorError("PROVIDER_DRIVER_SOURCE")
        if _git(
            repo_root,
            [
                "log",
                "--format=%H",
                f"{origin_revision}..{product_candidate}",
                "--",
                driver_path,
            ],
        ).stdout:
            raise ExecutorError("PROVIDER_DRIVER_SOURCE")
        _raw_safe_source_attributes(repo_root, driver_path)
    except ExecutorError as error:
        if error.code in {
            "MISSING_PATH",
            "BOOTSTRAP_SOURCE_HASH",
            "BOOTSTRAP_SOURCE_GIT",
            "BOOTSTRAP_SOURCE_IDENTITY",
            "BOOTSTRAP_SOURCE_HISTORY",
            "BOOTSTRAP_SOURCE_ANCESTRY",
            "BOOTSTRAP_SOURCE_DIRTY",
        }:
            raise ExecutorError("PROVIDER_DRIVER_SOURCE") from None
        raise
    return binding


def _provider_control_source_preflight(
    repo_root: Path,
    *,
    gate_id: str,
    product_candidate: str,
    phase: str,
) -> tuple[
    dict[str, Any],
    str,
    dict[str, str],
    dict[str, str],
    dict[str, str],
]:
    if phase not in PROVIDER_SCENARIO_PATHS:
        raise ExecutorError("SEGMENT_PHASES")
    manifest, _manifest_binding, _runner_binding = _bootstrap_control_preflight(
        repo_root,
        product_candidate=product_candidate,
    )
    _gate_lane_context(repo_root, manifest, gate_id)
    policy, policy_sha = _trusted_provider_policy(repo_root, manifest)
    harness = _bootstrap_source_binding(
        repo_root,
        product_candidate=product_candidate,
        source_path=TRUSTED_PROVIDER_HARNESS_PATH,
        expected_sha256=policy["sourceSha256"],
    )
    scenario_bindings: dict[str, dict[str, str]] = {}
    for scenario_phase, scenario_path in PROVIDER_SCENARIO_PATHS.items():
        source_policy = policy["scenarioSources"][scenario_phase]
        scenario_bindings[scenario_phase] = _bootstrap_source_binding(
            repo_root,
            product_candidate=product_candidate,
            source_path=scenario_path,
            expected_sha256=source_policy["sha256"],
        )
    driver_binding = _provider_runtime_driver_binding(
        repo_root,
        manifest=manifest,
        gate_id=gate_id,
        product_candidate=product_candidate,
        phase=phase,
    )
    return (
        policy,
        policy_sha,
        harness,
        scenario_bindings[phase],
        driver_binding,
    )


def reserve_provider_segment(
    repo_root: str | os.PathLike[str],
    *,
    gate_id: str,
    product_candidate: str,
    attempt_id: str,
    run_id: str,
    phases: Sequence[str],
    finish_success: bool = False,
) -> ReservationBatch:
    """Atomically reserve every turn in a child segment before it can start."""

    root = _normalise_repo_root(repo_root)
    control_root = _repository_context(root).control_root
    if gate_id not in PROVIDER_PHASE_LAYOUT:
        raise ExecutorError("PROVIDER_GATE")
    _verify_candidate_commit(root, product_candidate)
    _validate_safe_id(attempt_id, "ATTEMPT_ID")
    _validate_safe_id(run_id, "RUN_ID")
    phase_tuple = tuple(phases)
    if (
        not phase_tuple
        or any(not isinstance(item, str) for item in phase_tuple)
        or len(set(phase_tuple)) != 1
        or phase_tuple[0] not in _PROVIDER_BATCH_SIZES
        or len(phase_tuple) not in _PROVIDER_BATCH_SIZES[phase_tuple[0]]
    ):
        raise ExecutorError("SEGMENT_PHASES")
    control_root = _repository_context(root).control_root
    with _FileLock(control_root):
        _provider_checkout_preflight(
            root,
            gate_id=gate_id,
            phase=phase_tuple[0],
            product_candidate=product_candidate,
        )
        _provider_control_source_preflight(
            root,
            gate_id=gate_id,
            product_candidate=product_candidate,
            phase=phase_tuple[0],
        )
        package = _package_identity_evidence_binding(
            root,
            control_root,
            gate_id=gate_id,
            product_candidate=product_candidate,
            evidence_path=(
                f"{PROVIDER_ARTIFACT_ROOTS[gate_id]}/package-identity.json"
            ),
        )
        verify_provider_boundary_decision(
            root,
            control_root,
            gate_id=gate_id,
            product_candidate=product_candidate,
            package=package,
        )
        ledger = _load_ledger_locked(control_root)
        attempts, completions, finished = _attempt_views(ledger)
        existing = attempts.get(attempt_id, [])
        if existing and (
            existing[0]["gateId"] != gate_id
            or existing[0]["productCandidate"] != product_candidate
        ):
            raise ExecutorError("ATTEMPT_OWNER")
        if attempt_id in finished:
            raise ExecutorError("ATTEMPT_REOPENED")
        for other_attempt_id in attempts:
            if other_attempt_id != attempt_id and other_attempt_id not in finished:
                raise ExecutorError("OPEN_ATTEMPT_EXISTS")
        if any(
            item["reservationId"] not in completions for item in existing
        ):
            raise ExecutorError("RECOVERY_REQUIRED")
        layout = PROVIDER_PHASE_LAYOUT[gate_id]
        prior_phases = tuple(item["phase"] for item in existing)
        if prior_phases != layout[: len(prior_phases)]:
            raise ExecutorError("ATTEMPT_PHASE_PREFIX")
        remaining = layout[len(prior_phases) :]
        if phase_tuple != remaining[: len(phase_tuple)]:
            raise ExecutorError("SEGMENT_PHASES")
        # Preserve enough budget for the entire frozen remainder.  A partial
        # run must never strand an otherwise valid attempt at the global cap.
        if ledger["remainingTurns"] < len(remaining):
            raise ExecutorError("PROVIDER_CAP")
        if finish_success and prior_phases + phase_tuple != layout:
            raise ExecutorError("INCOMPLETE_SUCCESS")
        if ledger["usedTurns"] + len(phase_tuple) > PROVIDER_MAX_TURNS:
            raise ExecutorError("PROVIDER_CAP")
        if "controlled-write" in phase_tuple:
            if any(
                item["gateId"] == gate_id
                and item["phase"] == "controlled-write"
                for item in ledger["entries"]
            ):
                raise ExecutorError("CONTROLLED_WRITE_REPLAY")
        reservation_ids: list[str] = []
        reservation_sequences: list[int] = []
        previous_hash = (
            ledger["entries"][-1]["entrySha256"] if ledger["entries"] else None
        )
        previous_time = (
            ledger["entries"][-1]["reservedAt"] if ledger["entries"] else None
        )
        for phase in phase_tuple:
            sequence = len(ledger["entries"]) + 1
            reserved_at = _utc_now() if previous_time is None else _timestamp_after(previous_time)
            reservation_id = f"turn-{sequence}-{secrets.token_hex(12)}"
            entry = {
                "sequence": sequence,
                "eventType": "TurnReserved",
                "reservationId": reservation_id,
                "gateId": gate_id,
                "phase": phase,
                "productCandidate": product_candidate,
                "attemptId": attempt_id,
                "runId": run_id,
                "reservedAt": reserved_at,
                "previousEntrySha256": previous_hash,
            }
            entry["entrySha256"] = provider_entry_sha256(entry)
            ledger["entries"].append(entry)
            reservation_ids.append(reservation_id)
            reservation_sequences.append(sequence)
            previous_hash = entry["entrySha256"]
            previous_time = reserved_at
        ledger["usedTurns"] = len(ledger["entries"])
        ledger["remainingTurns"] = PROVIDER_MAX_TURNS - ledger["usedTurns"]
        ledger["ledgerSequence"] = ledger["usedTurns"]
        _validate_ledger(ledger)
        _atomic_write_json(control_root, PROVIDER_LEDGER_PATH, ledger)
        return ReservationBatch(
            gate_id=gate_id,
            product_candidate=product_candidate,
            attempt_id=attempt_id,
            run_id=run_id,
            phases=phase_tuple,
            reservation_ids=tuple(reservation_ids),
            reservation_sequences=tuple(reservation_sequences),
        )


def complete_provider_segment(
    repo_root: str | os.PathLike[str],
    batch: ReservationBatch,
    *,
    child_succeeded: bool,
    finish_success: bool = False,
) -> dict[str, Any]:
    root = _normalise_repo_root(repo_root)
    control_root = _repository_context(root).control_root
    with _FileLock(control_root):
        ledger = _load_ledger_locked(control_root)
        _load_runtime_journal_locked(control_root)
        attempts, completions, finished = _attempt_views(ledger)
        attempt_entries = attempts.get(batch.attempt_id)
        if not attempt_entries or batch.attempt_id in finished:
            raise ExecutorError("SEGMENT_COMPLETION")
        selected = [
            item
            for item in attempt_entries
            if item["reservationId"] in set(batch.reservation_ids)
        ]
        if (
            tuple(item["reservationId"] for item in selected)
            != batch.reservation_ids
            or tuple(item["sequence"] for item in selected)
            != batch.reservation_sequences
            or tuple(item["phase"] for item in selected) != batch.phases
            or any(item["reservationId"] in completions for item in selected)
            or any(
                item["gateId"] != batch.gate_id
                or item["productCandidate"] != batch.product_candidate
                or item["runId"] != batch.run_id
                for item in selected
            )
        ):
            raise ExecutorError("SEGMENT_COMPLETION")
        if child_succeeded:
            runtime_journal = _load_runtime_journal_locked(control_root)
            runtime_matches = [
                event
                for event in runtime_journal["events"]
                if event.get("reservationId") in batch.reservation_ids
            ]
            if (
                len(runtime_matches) != len(selected)
                or tuple(
                    event.get("reservationId") for event in runtime_matches
                )
                != batch.reservation_ids
                or tuple(
                    event.get("reservationSequence") for event in runtime_matches
                )
                != batch.reservation_sequences
                or tuple(event.get("phase") for event in runtime_matches)
                != batch.phases
                or len({event.get("batchId") for event in runtime_matches}) != 1
                or any(
                    event.get("outcome") != "Succeeded"
                    or event.get("batchSize") != len(selected)
                    or event.get("gateId") != batch.gate_id
                    or event.get("productCandidate") != batch.product_candidate
                    or event.get("attemptId") != batch.attempt_id
                    or event.get("runId") != batch.run_id
                    or not isinstance(event.get("observedRequest"), dict)
                    or event["observedRequest"].get("reservationId")
                    != event.get("reservationId")
                    or event["observedRequest"].get("status") != "Succeeded"
                    for event in runtime_matches
                )
            ):
                raise ExecutorError("RUNTIME_RECEIPT_REQUIRED")
        layout = PROVIDER_PHASE_LAYOUT[batch.gate_id]
        all_phases = tuple(item["phase"] for item in attempt_entries)
        if finish_success and (not child_succeeded or all_phases != layout):
            raise ExecutorError("INCOMPLETE_SUCCESS")

        if not child_succeeded:
            _ensure_failed_runtime_receipts_locked(control_root, batch)

        last_time = (
            ledger["attemptEvents"][-1].get(
                "completedAt", ledger["attemptEvents"][-1].get("finishedAt")
            )
            if ledger["attemptEvents"]
            else selected[0]["reservedAt"]
        )
        outcome = "Succeeded" if child_succeeded else "Failed"
        for item in selected:
            completed_at = _timestamp_after(last_time)
            _append_attempt_event(
                ledger,
                {
                    "eventType": "TurnCompleted",
                    "reservationId": item["reservationId"],
                    "outcome": outcome,
                    "completedAt": completed_at,
                },
            )
            last_time = completed_at
        if not child_succeeded:
            _append_attempt_finished(ledger, attempt_entries, "Failed")
        elif finish_success:
            _append_attempt_finished(ledger, attempt_entries, "Passed")
        _validate_ledger(ledger)
        _atomic_write_json(control_root, PROVIDER_LEDGER_PATH, ledger)
        return json.loads(canonical_json_bytes(ledger).decode("utf-8"))


def recover_open_provider_attempts(
    repo_root: str | os.PathLike[str],
) -> dict[str, Any]:
    """Fail every unfinished attempt and charge every already-reserved turn."""

    root = _normalise_repo_root(repo_root)
    control_root = _repository_context(root).control_root
    with _FileLock(control_root):
        ledger = _load_ledger_locked(control_root)
        _load_runtime_journal_locked(control_root)
        attempts, completions, finished = _attempt_views(ledger)
        open_attempts = sorted(
            (
                entries
                for attempt_id, entries in attempts.items()
                if attempt_id not in finished
            ),
            key=lambda items: items[0]["sequence"],
        )
        for attempt_entries in open_attempts:
            pending = [
                item
                for item in attempt_entries
                if item["reservationId"] not in completions
            ]
            if pending:
                phase = pending[0]["phase"]
                if (
                    any(
                        item["phase"] != phase
                        or item["runId"] != pending[0]["runId"]
                        for item in pending
                    )
                    or len(pending) != _PROVIDER_BATCH_SIZES[phase][0]
                ):
                    raise ExecutorError("RECOVERY_BATCH_SHAPE")
                recovery_batch = ReservationBatch(
                    gate_id=pending[0]["gateId"],
                    product_candidate=pending[0]["productCandidate"],
                    attempt_id=pending[0]["attemptId"],
                    run_id=pending[0]["runId"],
                    phases=tuple(item["phase"] for item in pending),
                    reservation_ids=tuple(
                        item["reservationId"] for item in pending
                    ),
                    reservation_sequences=tuple(item["sequence"] for item in pending),
                )
                _ensure_failed_runtime_receipts_locked(control_root, recovery_batch)
            last_time = (
                ledger["attemptEvents"][-1].get(
                    "completedAt", ledger["attemptEvents"][-1].get("finishedAt")
                )
                if ledger["attemptEvents"]
                else attempt_entries[0]["reservedAt"]
            )
            for item in attempt_entries:
                if item["reservationId"] in completions:
                    continue
                completed_at = _timestamp_after(last_time)
                event = {
                    "eventType": "TurnCompleted",
                    "reservationId": item["reservationId"],
                    "outcome": "Failed",
                    "completedAt": completed_at,
                }
                _append_attempt_event(ledger, event)
                completions[item["reservationId"]] = event
                last_time = completed_at
            _append_attempt_finished(ledger, attempt_entries, "Failed")
        _validate_ledger(ledger)
        if open_attempts:
            _atomic_write_json(control_root, PROVIDER_LEDGER_PATH, ledger)
        return json.loads(canonical_json_bytes(ledger).decode("utf-8"))


def _clean_child_environment(base: Mapping[str, str] | None = None) -> dict[str, str]:
    source = os.environ if base is None else base
    blocked = {key.casefold() for key in PROVIDER_ENV_KEYS}
    return {key: value for key, value in source.items() if key.casefold() not in blocked}


def _test_child_environment(repo_root: Path | None = None) -> dict[str, str]:
    child = _clean_child_environment()
    blocked = {
        "pythonpath",
        "pythonhome",
        "pythonstartup",
        "pythoninspect",
        "pythonwarnings",
        "pythonbreakpoint",
    }
    child = {
        key: value
        for key, value in child.items()
        if key.casefold() not in blocked
        and key.casefold() != "path"
        and key.casefold() != SEMANTIC_REPO_ROOT_ENV.casefold()
        and _SENSITIVE_ENV_NAME.search(key) is None
    }
    child["PATH"] = _BOOTSTRAP_EXECUTION_PATH
    child["PYTHONNOUSERSITE"] = "1"
    child["PYTHONDONTWRITEBYTECODE"] = "1"
    if repo_root is not None:
        git_executable, _identity = _trusted_git_identity(repo_root)
        child["PATH"] = str(git_executable.parent)
        child[SEMANTIC_REPO_ROOT_ENV] = str(repo_root)
    return child


def _load_provider_environment(
    repo_root: Path, env_file: str | os.PathLike[str]
) -> dict[str, str]:
    requested = Path(env_file)
    if not requested.is_absolute():
        requested = repo_root / requested
    requested = Path(os.path.abspath(requested))
    canonical = repo_root / PROVIDER_ENV_PATH
    if os.path.normcase(str(requested)) != os.path.normcase(str(canonical)):
        raise ExecutorError("PROVIDER_ENV_PATH")
    tracked = _git(
        repo_root,
        ["ls-files", "--error-unmatch", "--", PROVIDER_ENV_PATH],
        check=False,
    )
    ignored = _git(
        repo_root,
        ["check-ignore", "-q", "--", PROVIDER_ENV_PATH],
        check=False,
    )
    history = _git(
        repo_root,
        ["log", "--all", "--format=%H", "--", PROVIDER_ENV_PATH],
        check=False,
    )
    if tracked.returncode == 0 or ignored.returncode != 0 or history.stdout:
        raise ExecutorError("PROVIDER_ENV_STORAGE")
    raw = _read_fixed_bytes(repo_root, PROVIDER_ENV_PATH, limit=MAX_ENV_BYTES)
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        raise ExecutorError("PROVIDER_ENV_FORMAT") from None
    parsed: dict[str, str] = {}
    allowed = set(PROVIDER_ENV_KEYS)
    for raw_line in text.splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        if line.startswith("export "):
            line = line[7:].lstrip()
        if "=" not in line:
            raise ExecutorError("PROVIDER_ENV_FORMAT")
        key, value = line.split("=", 1)
        key = key.strip()
        if key not in allowed:
            raise ExecutorError("PROVIDER_ENV_FORMAT")
        if key in parsed:
            raise ExecutorError("PROVIDER_ENV_FORMAT")
        value = value.strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in {"'", '"'}:
            value = value[1:-1]
        if not value or "\x00" in value or "\r" in value or "\n" in value:
            raise ExecutorError("PROVIDER_ENV_FORMAT")
        parsed[key] = value
    if set(parsed) != allowed:
        raise ExecutorError("PROVIDER_ENV_MISSING")
    safe_names = {
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
    child = {
        key: value
        for key, value in os.environ.items()
        if key.casefold() in safe_names
    }
    child["PYTHONNOUSERSITE"] = "1"
    child["PYTHONDONTWRITEBYTECODE"] = "1"
    child.update(parsed)
    return child


def _validate_argv(argv: Sequence[str]) -> list[str]:
    if (
        not isinstance(argv, Sequence)
        or isinstance(argv, (str, bytes))
        or not argv
        or any(not isinstance(item, str) or not item or "\x00" in item for item in argv)
    ):
        raise ExecutorError("CHILD_COMMAND")
    result = list(argv)
    if any(_contains_secret(item) for item in result):
        raise ExecutorError("CHILD_COMMAND_SECRET")
    return result


def _creation_flags() -> int:
    if os.name != "nt":
        return 0
    return subprocess.CREATE_NO_WINDOW | subprocess.CREATE_NEW_PROCESS_GROUP


def _provider_job_name(environment: Mapping[str, str]) -> str | None:
    token = environment.get("CAICLI_PROVIDER_RUN_TOKEN")
    if token is None:
        return None
    if re.fullmatch(r"[0-9a-f]{64}", token) is None:
        raise ExecutorError("CHILD_CONTAINMENT")
    digest = hashlib.sha256(token.encode("ascii")).hexdigest()
    return f"Local\\CAICLI-W8492-{digest[:32]}"


def _attach_windows_job(
    process: subprocess.Popen[Any], job_name: str | None = None
) -> int | None:
    if os.name != "nt":
        return None
    try:
        import ctypes
        from ctypes import wintypes

        class _BasicLimit(ctypes.Structure):
            _fields_ = [
                ("PerProcessUserTimeLimit", ctypes.c_longlong),
                ("PerJobUserTimeLimit", ctypes.c_longlong),
                ("LimitFlags", wintypes.DWORD),
                ("MinimumWorkingSetSize", ctypes.c_size_t),
                ("MaximumWorkingSetSize", ctypes.c_size_t),
                ("ActiveProcessLimit", wintypes.DWORD),
                ("Affinity", ctypes.c_size_t),
                ("PriorityClass", wintypes.DWORD),
                ("SchedulingClass", wintypes.DWORD),
            ]

        class _IoCounters(ctypes.Structure):
            _fields_ = [
                ("ReadOperationCount", ctypes.c_ulonglong),
                ("WriteOperationCount", ctypes.c_ulonglong),
                ("OtherOperationCount", ctypes.c_ulonglong),
                ("ReadTransferCount", ctypes.c_ulonglong),
                ("WriteTransferCount", ctypes.c_ulonglong),
                ("OtherTransferCount", ctypes.c_ulonglong),
            ]

        class _ExtendedLimit(ctypes.Structure):
            _fields_ = [
                ("BasicLimitInformation", _BasicLimit),
                ("IoInfo", _IoCounters),
                ("ProcessMemoryLimit", ctypes.c_size_t),
                ("JobMemoryLimit", ctypes.c_size_t),
                ("PeakProcessMemoryUsed", ctypes.c_size_t),
                ("PeakJobMemoryUsed", ctypes.c_size_t),
            ]

        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
        kernel.CreateJobObjectW.restype = wintypes.HANDLE
        kernel.SetInformationJobObject.argtypes = [
            wintypes.HANDLE,
            ctypes.c_int,
            ctypes.c_void_p,
            wintypes.DWORD,
        ]
        kernel.SetInformationJobObject.restype = wintypes.BOOL
        kernel.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
        kernel.AssignProcessToJobObject.restype = wintypes.BOOL
        ctypes.set_last_error(0)
        job = kernel.CreateJobObjectW(None, job_name)
        if not job:
            raise OSError
        if job_name is not None and ctypes.get_last_error() == 183:
            kernel.CloseHandle(job)
            raise OSError
        information = _ExtendedLimit()
        information.BasicLimitInformation.LimitFlags = 0x00002000
        if not kernel.SetInformationJobObject(
            job, 9, ctypes.byref(information), ctypes.sizeof(information)
        ) or not kernel.AssignProcessToJobObject(job, wintypes.HANDLE(process._handle)):
            kernel.CloseHandle(job)
            raise OSError
        return int(job)
    except (AttributeError, OSError, TypeError, ValueError):
        try:
            process.kill()
            process.wait(timeout=5)
        except (OSError, subprocess.SubprocessError):
            pass
        raise ExecutorError("CHILD_CONTAINMENT") from None


def _start_contained_child(
    command: Sequence[str],
    *,
    cwd: Path,
    environment: Mapping[str, str],
    stdout: Any,
    stderr: Any,
    stdin_bytes: bytes = b"",
) -> tuple[subprocess.Popen[Any], int | None]:
    """Start trusted bootstrap code, attach it, then release candidate code.

    Candidate bytes are never executed between ``Popen`` and Windows Job
    assignment.  On an attach or signalling failure only the in-memory frozen
    trampoline has run.
    """

    validated_command = _validate_argv(command)
    if not isinstance(stdin_bytes, bytes) or len(stdin_bytes) > MAX_JSON_BYTES:
        raise ExecutorError("CHILD_INPUT")
    request = canonical_json_bytes(
        {
            "protocol": _CONTAINED_CHILD_PROTOCOL,
            "command": validated_command,
            "stdinBase64": base64.b64encode(stdin_bytes).decode("ascii"),
            "stdinSha256": hashlib.sha256(stdin_bytes).hexdigest(),
        }
    ) + b"\n"
    if len(request) > MAX_CONTAINED_CHILD_REQUEST_BYTES:
        raise ExecutorError("CHILD_COMMAND")
    process = subprocess.Popen(
        [sys.executable, "-I", "-S", "-E", "-c", _CONTAINED_CHILD_TRAMPOLINE],
        cwd=cwd,
        env=dict(environment),
        stdin=subprocess.PIPE,
        stdout=stdout,
        stderr=stderr,
        shell=False,
        creationflags=_creation_flags(),
        start_new_session=os.name != "nt",
        close_fds=True,
    )
    windows_job: int | None = None
    try:
        job_name = _provider_job_name(environment)
        windows_job = (
            _attach_windows_job(process)
            if job_name is None
            else _attach_windows_job(process, job_name)
        )
        if process.stdin is None:
            raise ExecutorError("CHILD_CONTAINMENT")
        process.stdin.write(request)
        process.stdin.flush()
        process.stdin.close()
    except (BrokenPipeError, OSError, ExecutorError):
        if process.stdin is not None:
            try:
                process.stdin.close()
            except OSError:
                pass
        _terminate_child_tree(process, windows_job)
        try:
            process.wait(timeout=5)
        except subprocess.SubprocessError:
            _terminate_child_tree(process, windows_job)
        _release_child_tree(process, windows_job)
        raise ExecutorError("CHILD_CONTAINMENT") from None
    return process, windows_job


def _terminate_child_tree(
    process: subprocess.Popen[Any],
    windows_job: int | None,
) -> None:
    if os.name == "nt" and windows_job is not None:
        try:
            import ctypes
            from ctypes import wintypes

            kernel = ctypes.WinDLL("kernel32", use_last_error=True)
            kernel.TerminateJobObject.argtypes = [wintypes.HANDLE, wintypes.UINT]
            kernel.TerminateJobObject.restype = wintypes.BOOL
            kernel.TerminateJobObject(wintypes.HANDLE(windows_job), 1)
            return
        except (AttributeError, OSError):
            pass
    elif os.name != "nt":
        try:
            os.killpg(process.pid, signal.SIGKILL)
            return
        except OSError:
            pass
    try:
        process.kill()
    except OSError:
        pass


def _release_child_tree(
    process: subprocess.Popen[Any],
    windows_job: int | None,
) -> None:
    if os.name == "nt" and windows_job is not None:
        try:
            import ctypes
            from ctypes import wintypes

            kernel = ctypes.WinDLL("kernel32", use_last_error=True)
            kernel.CloseHandle.argtypes = [wintypes.HANDLE]
            kernel.CloseHandle.restype = wintypes.BOOL
            kernel.CloseHandle(wintypes.HANDLE(windows_job))
        except (AttributeError, OSError):
            pass
    elif os.name != "nt":
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except OSError:
            pass


def _run_child_no_capture(
    command: Sequence[str],
    *,
    cwd: Path,
    environment: Mapping[str, str],
    timeout_seconds: float,
) -> int:
    try:
        process, windows_job = _start_contained_child(
            command,
            cwd=cwd,
            environment=environment,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
    except (OSError, ExecutorError):
        return 127
    try:
        try:
            return int(process.wait(timeout=timeout_seconds))
        except subprocess.TimeoutExpired:
            _terminate_child_tree(process, windows_job)
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                _terminate_child_tree(process, windows_job)
                process.wait()
            return 127
    finally:
        _release_child_tree(process, windows_job)


def _safe_directory(repo_root: Path, relative_path: str, code: str) -> Path:
    current = repo_root
    for part in _relative_parts(relative_path):
        current = current / part
        try:
            if (
                not current.is_dir()
                or _is_reparse_or_link(current)
                or not stat.S_ISDIR(os.lstat(current).st_mode)
            ):
                raise ExecutorError(code)
        except OSError:
            raise ExecutorError(code) from None
    return current


def _windows_stream_names(path: Path) -> tuple[str, ...]:
    if os.name != "nt":
        return ("::$DATA",)
    try:
        import ctypes
        from ctypes import wintypes

        class _StreamData(ctypes.Structure):
            _fields_ = [
                ("StreamSize", ctypes.c_longlong),
                ("StreamName", wintypes.WCHAR * 296),
            ]

        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.FindFirstStreamW.argtypes = [
            wintypes.LPCWSTR,
            ctypes.c_int,
            ctypes.POINTER(_StreamData),
            wintypes.DWORD,
        ]
        kernel.FindFirstStreamW.restype = wintypes.HANDLE
        kernel.FindNextStreamW.argtypes = [
            wintypes.HANDLE,
            ctypes.POINTER(_StreamData),
        ]
        kernel.FindNextStreamW.restype = wintypes.BOOL
        kernel.FindClose.argtypes = [wintypes.HANDLE]
        kernel.FindClose.restype = wintypes.BOOL
        data = _StreamData()
        handle = kernel.FindFirstStreamW(str(path), 0, ctypes.byref(data), 0)
        invalid = wintypes.HANDLE(-1).value
        if handle == invalid:
            error = ctypes.get_last_error()
            if error in {2, 38}:
                return ()
            raise OSError(error, "FindFirstStreamW")
        names: list[str] = []
        try:
            names.append(str(data.StreamName))
            while kernel.FindNextStreamW(handle, ctypes.byref(data)):
                names.append(str(data.StreamName))
            if ctypes.get_last_error() not in {0, 38}:
                raise OSError(ctypes.get_last_error(), "FindNextStreamW")
        finally:
            kernel.FindClose(handle)
        return tuple(names)
    except (AttributeError, OSError, TypeError, ValueError):
        raise ExecutorError("PACKAGE_ADS_INSPECTION") from None


def _reject_package_ads(path: Path) -> None:
    streams = _windows_stream_names(path)
    if any(name.casefold() != "::$data" for name in streams):
        raise ExecutorError("PACKAGE_ADS")


def _canonical_package_mode(info: os.stat_result) -> str:
    if os.name == "nt":
        return "100644"
    return "100755" if info.st_mode & 0o111 else "100644"


def _package_tree_root(entries: Sequence[Mapping[str, Any]]) -> str:
    body = bytearray(PACKAGE_TREE_ALGORITHM.encode("ascii") + b"\0")
    for entry in entries:
        body.extend(str(entry["path"]).encode("utf-8"))
        body.extend(b"\0")
        body.extend(str(entry["mode"]).encode("ascii"))
        body.extend(b"\0")
        body.extend(str(entry["bytes"]).encode("ascii"))
        body.extend(b"\0")
        body.extend(str(entry["sha256"]).encode("ascii"))
        body.extend(b"\n")
    return hashlib.sha256(body).hexdigest()


def _package_segment_is_sensitive(value: str) -> bool:
    folded = value.casefold()
    return (
        folded in {".git", ".env", ".env.local", "credentials", "id_rsa"}
        or folded.startswith(".env.")
        or folded.endswith((".pem", ".key", ".pfx", ".p12"))
    )


def _package_inventory(package_root: Path) -> dict[str, Any]:
    try:
        lexical = Path(os.path.abspath(package_root))
        resolved = lexical.resolve(strict=True)
        if (
            lexical != resolved
            or not resolved.is_dir()
            or _is_reparse_or_link(lexical)
        ):
            raise ExecutorError("PACKAGE_ROOT")
    except OSError:
        raise ExecutorError("PACKAGE_ROOT") from None
    entries: list[dict[str, Any]] = []
    casefolded: dict[str, str] = {}
    identities: set[tuple[int, int]] = set()

    def register_path(relative: str) -> None:
        if (
            not relative
            or len(relative.encode("utf-8")) > 4096
            or unicodedata.normalize("NFC", relative) != relative
            or ":" in relative
            or "\\" in relative
        ):
            raise ExecutorError("PACKAGE_PATH")
        for segment in relative.split("/"):
            if (
                not segment
                or segment in {".", ".."}
                or segment.endswith((".", " "))
                or _package_segment_is_sensitive(segment)
            ):
                raise ExecutorError("PACKAGE_SENSITIVE_PATH")
        folded = relative.casefold()
        prior = casefolded.get(folded)
        if prior is not None and prior != relative:
            raise ExecutorError("PACKAGE_CASE_COLLISION")
        casefolded[folded] = relative

    def visit(directory: Path, prefix: str) -> None:
        _reject_package_ads(directory)
        try:
            children = sorted(
                tuple(os.scandir(directory)),
                key=lambda item: item.name.encode("utf-8"),
            )
        except (OSError, UnicodeEncodeError):
            raise ExecutorError("PACKAGE_TREE_READ") from None
        for child in children:
            relative = f"{prefix}/{child.name}" if prefix else child.name
            register_path(relative)
            path = directory / child.name
            try:
                info = os.lstat(path)
            except OSError:
                raise ExecutorError("PACKAGE_TREE_READ") from None
            if _is_reparse_or_link(path) or stat.S_ISLNK(info.st_mode):
                raise ExecutorError("PACKAGE_REPARSE")
            _reject_package_ads(path)
            if stat.S_ISDIR(info.st_mode):
                visit(path, relative)
                continue
            if not stat.S_ISREG(info.st_mode):
                raise ExecutorError("PACKAGE_NONREGULAR")
            if info.st_nlink != 1:
                raise ExecutorError("PACKAGE_HARDLINK")
            identity = (int(info.st_dev), int(info.st_ino))
            if identity in identities:
                raise ExecutorError("PACKAGE_HARDLINK")
            identities.add(identity)
            if not (0 <= info.st_size <= MAX_PACKAGE_FILE_BYTES):
                raise ExecutorError("PACKAGE_FILE_LIMIT")
            flags = os.O_RDONLY
            if hasattr(os, "O_BINARY"):
                flags |= os.O_BINARY
            if hasattr(os, "O_NOFOLLOW"):
                flags |= os.O_NOFOLLOW
            descriptor: int | None = None
            try:
                descriptor = os.open(path, flags)
                opened_before = os.fstat(descriptor)
                if (
                    not stat.S_ISREG(opened_before.st_mode)
                    or not os.path.samestat(opened_before, info)
                    or opened_before.st_nlink != 1
                ):
                    raise ExecutorError("PACKAGE_FILE_RACE")
                digest = hashlib.sha256()
                size = 0
                while True:
                    block = os.read(descriptor, 1024 * 1024)
                    if not block:
                        break
                    digest.update(block)
                    size += len(block)
                    if size > MAX_PACKAGE_FILE_BYTES:
                        raise ExecutorError("PACKAGE_FILE_LIMIT")
                opened_after = os.fstat(descriptor)
                lexical_after = os.lstat(path)
                if (
                    not os.path.samestat(opened_before, opened_after)
                    or not os.path.samestat(opened_after, lexical_after)
                    or opened_before.st_size != opened_after.st_size
                    or getattr(opened_before, "st_mtime_ns", None)
                    != getattr(opened_after, "st_mtime_ns", None)
                    or size != opened_after.st_size
                    or _is_reparse_or_link(path)
                ):
                    raise ExecutorError("PACKAGE_FILE_RACE")
            except ExecutorError:
                raise
            except OSError:
                raise ExecutorError("PACKAGE_FILE_READ") from None
            finally:
                if descriptor is not None:
                    os.close(descriptor)
            entries.append(
                {
                    "path": relative,
                    "mode": _canonical_package_mode(info),
                    "bytes": size,
                    "sha256": digest.hexdigest(),
                }
            )
            if len(entries) > MAX_PACKAGE_ENTRY_COUNT:
                raise ExecutorError("PACKAGE_ENTRY_LIMIT")

    visit(resolved, "")
    entries.sort(key=lambda item: item["path"].encode("utf-8"))
    total = sum(item["bytes"] for item in entries)
    if not entries or total > MAX_PACKAGE_TOTAL_BYTES:
        raise ExecutorError("PACKAGE_TOTAL_LIMIT")
    return {
        "algorithm": PACKAGE_TREE_ALGORITHM,
        "entryCount": len(entries),
        "totalBytes": total,
        "treeRootSha256": _package_tree_root(entries),
        "entries": entries,
    }


def _semantic_post_output_paths(
    *,
    gate_id: str,
    result_path: str,
) -> tuple[str, ...]:
    paths = {PurePosixPath(*_relative_parts(result_path)).as_posix()}
    if gate_id == "W84-G0":
        if result_path != W84_G0_POST_SEMANTIC_OUTPUTS[-1]:
            raise ExecutorError("ARTIFACT_INPUT_EXCLUSION")
        paths.update(W84_G0_POST_SEMANTIC_OUTPUTS)
    if any(
        not any(path.startswith(root + "/") for root in SEMANTIC_ARTIFACT_INPUT_ROOTS)
        for path in paths
    ):
        raise ExecutorError("ARTIFACT_INPUT_EXCLUSION")
    return tuple(sorted(paths, key=lambda value: value.encode("utf-8")))


def _post_semantic_output_binding(repo_root: Path, relative_path: str) -> dict[str, Any]:
    current = repo_root
    parts = _relative_parts(relative_path)
    for index, part in enumerate(parts):
        current = current / part
        present = current.exists() or current.is_symlink()
        if not present:
            return {
                "path": relative_path,
                "before": {"exists": False, "bytes": None, "sha256": None},
            }
        try:
            info = os.lstat(current)
        except OSError:
            raise ExecutorError("POST_SEMANTIC_OUTPUT_BINDING") from None
        if _is_reparse_or_link(current):
            raise ExecutorError("POST_SEMANTIC_OUTPUT_BINDING")
        if index < len(parts) - 1:
            if not stat.S_ISDIR(info.st_mode):
                raise ExecutorError("POST_SEMANTIC_OUTPUT_BINDING")
            continue
        if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1:
            raise ExecutorError("POST_SEMANTIC_OUTPUT_BINDING")
        raw = _read_fixed_bytes(repo_root, relative_path, limit=MAX_JSON_BYTES)
        try:
            after = os.lstat(current)
        except OSError:
            raise ExecutorError("POST_SEMANTIC_OUTPUT_BINDING") from None
        if (
            not os.path.samestat(info, after)
            or info.st_size != after.st_size
            or getattr(info, "st_mtime_ns", None)
            != getattr(after, "st_mtime_ns", None)
            or len(raw) != after.st_size
            or after.st_nlink != 1
        ):
            raise ExecutorError("POST_SEMANTIC_OUTPUT_BINDING")
        return {
            "path": relative_path,
            "before": {
                "exists": True,
                "bytes": len(raw),
                "sha256": hashlib.sha256(raw).hexdigest(),
            },
        }
    raise ExecutorError("POST_SEMANTIC_OUTPUT_BINDING")


def _artifact_input_tree_inventory(
    repo_root: Path,
    *,
    excluded_paths: Sequence[str],
    content_overrides: Mapping[str, bytes] | None = None,
) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    """Hash the existing frozen Goal artifact roots without scanning history."""

    excluded: set[str] = set()
    for value in excluded_paths:
        canonical = PurePosixPath(*_relative_parts(value)).as_posix()
        if not any(
            canonical.startswith(root_path + "/")
            for root_path in SEMANTIC_ARTIFACT_INPUT_ROOTS
        ):
            raise ExecutorError("ARTIFACT_INPUT_EXCLUSION")
        excluded.add(canonical)
    if len(excluded) != len(excluded_paths):
        raise ExecutorError("ARTIFACT_INPUT_EXCLUSION")
    overrides = dict(content_overrides or {})
    if set(overrides) - {GOAL_STATE_PATH}:
        raise ExecutorError("ARTIFACT_INPUT_OVERRIDE")
    for override_path, override_raw in overrides.items():
        if not isinstance(override_raw, bytes) or len(override_raw) > MAX_JSON_BYTES:
            raise ExecutorError("ARTIFACT_INPUT_OVERRIDE")
        override_document = _read_json_bytes(
            override_raw, "ARTIFACT_INPUT_OVERRIDE"
        )
        if (
            not isinstance(override_document, dict)
            or canonical_json_bytes(override_document) != override_raw
            or _contains_secret(override_raw.decode("utf-8"))
        ):
            raise ExecutorError("ARTIFACT_INPUT_OVERRIDE")
    excluded_ancestors = {
        parent.as_posix()
        for value in excluded
        for parent in PurePosixPath(value).parents
        if parent.as_posix() not in {".", "artifacts"}
    }

    scoped_roots: list[dict[str, Any]] = []
    casefolded: dict[str, str] = {}
    identities: set[tuple[int, int]] = set()
    entry_count = 0
    total_bytes = 0

    def existing_root(relative_root: str) -> Path | None:
        current = repo_root
        for part in _relative_parts(relative_root):
            current = current / part
            if not current.exists() and not current.is_symlink():
                return None
            try:
                info = os.lstat(current)
            except OSError:
                raise ExecutorError("ARTIFACT_INPUT_TREE_READ") from None
            if (
                _is_reparse_or_link(current)
                or not stat.S_ISDIR(info.st_mode)
                or Path(os.path.abspath(current)).resolve(strict=True) != current
            ):
                raise ExecutorError("ARTIFACT_INPUT_ROOT")
        return current

    def register(relative: str) -> None:
        if (
            not relative
            or len(relative.encode("utf-8")) > 4096
            or unicodedata.normalize("NFC", relative) != relative
            or ":" in relative
            or "\\" in relative
        ):
            raise ExecutorError("ARTIFACT_INPUT_PATH")
        for segment in relative.split("/"):
            if (
                not segment
                or segment in {".", ".."}
                or segment.endswith((".", " "))
                or _package_segment_is_sensitive(segment)
            ):
                # In particular, never open or hash .env.local-like files.
                raise ExecutorError("ARTIFACT_INPUT_SENSITIVE_PATH")
        folded = relative.casefold()
        prior = casefolded.get(folded)
        if prior is not None and prior != relative:
            raise ExecutorError("ARTIFACT_INPUT_CASE_COLLISION")
        casefolded[folded] = relative

    def visit(
        directory: Path,
        *,
        root_path: str,
        prefix: str,
        entries: list[dict[str, Any]],
    ) -> None:
        nonlocal entry_count, total_bytes
        _reject_package_ads(directory)
        try:
            children = sorted(
                tuple(os.scandir(directory)),
                key=lambda item: item.name.encode("utf-8"),
            )
        except (OSError, UnicodeEncodeError):
            raise ExecutorError("ARTIFACT_INPUT_TREE_READ") from None
        for child in children:
            root_relative = f"{prefix}/{child.name}" if prefix else child.name
            relative = f"{root_path}/{root_relative}"
            register(relative)
            path = directory / child.name
            try:
                info = os.lstat(path)
            except OSError:
                raise ExecutorError("ARTIFACT_INPUT_TREE_READ") from None
            if _is_reparse_or_link(path) or stat.S_ISLNK(info.st_mode):
                raise ExecutorError("ARTIFACT_INPUT_REPARSE")
            _reject_package_ads(path)
            if stat.S_ISDIR(info.st_mode):
                if relative not in excluded_ancestors:
                    entries.append(
                        {
                            "path": root_relative,
                            "kind": "directory",
                            "bytes": 0,
                            "sha256": None,
                        }
                    )
                    entry_count += 1
                    if entry_count > MAX_PACKAGE_ENTRY_COUNT:
                        raise ExecutorError("ARTIFACT_INPUT_ENTRY_LIMIT")
                visit(
                    path,
                    root_path=root_path,
                    prefix=root_relative,
                    entries=entries,
                )
                continue
            if not stat.S_ISREG(info.st_mode):
                raise ExecutorError("ARTIFACT_INPUT_NONREGULAR")
            if relative in excluded:
                continue
            if info.st_nlink != 1:
                raise ExecutorError("ARTIFACT_INPUT_HARDLINK")
            identity = (int(info.st_dev), int(info.st_ino))
            if identity in identities:
                raise ExecutorError("ARTIFACT_INPUT_HARDLINK")
            identities.add(identity)
            if not (0 <= info.st_size <= MAX_PACKAGE_FILE_BYTES):
                raise ExecutorError("ARTIFACT_INPUT_FILE_LIMIT")
            flags = os.O_RDONLY
            if hasattr(os, "O_BINARY"):
                flags |= os.O_BINARY
            if hasattr(os, "O_NOFOLLOW"):
                flags |= os.O_NOFOLLOW
            descriptor: int | None = None
            try:
                descriptor = os.open(path, flags)
                opened_before = os.fstat(descriptor)
                if (
                    not stat.S_ISREG(opened_before.st_mode)
                    or not os.path.samestat(opened_before, info)
                    or opened_before.st_nlink != 1
                ):
                    raise ExecutorError("ARTIFACT_INPUT_FILE_RACE")
                digest = hashlib.sha256()
                size = 0
                while True:
                    block = os.read(descriptor, 1024 * 1024)
                    if not block:
                        break
                    digest.update(block)
                    size += len(block)
                    if size > MAX_PACKAGE_FILE_BYTES:
                        raise ExecutorError("ARTIFACT_INPUT_FILE_LIMIT")
                opened_after = os.fstat(descriptor)
                lexical_after = os.lstat(path)
                if (
                    not os.path.samestat(opened_before, opened_after)
                    or not os.path.samestat(opened_after, lexical_after)
                    or opened_before.st_size != opened_after.st_size
                    or getattr(opened_before, "st_mtime_ns", None)
                    != getattr(opened_after, "st_mtime_ns", None)
                    or size != opened_after.st_size
                    or _is_reparse_or_link(path)
                ):
                    raise ExecutorError("ARTIFACT_INPUT_FILE_RACE")
            except ExecutorError:
                raise
            except OSError:
                raise ExecutorError("ARTIFACT_INPUT_FILE_READ") from None
            finally:
                if descriptor is not None:
                    os.close(descriptor)
            bound_raw = overrides.get(relative)
            bound_size = len(bound_raw) if bound_raw is not None else size
            bound_sha256 = (
                hashlib.sha256(bound_raw).hexdigest()
                if bound_raw is not None
                else digest.hexdigest()
            )
            entries.append(
                {
                    "path": root_relative,
                    "kind": "file",
                    "bytes": bound_size,
                    "sha256": bound_sha256,
                }
            )
            entry_count += 1
            total_bytes += bound_size
            if entry_count > MAX_PACKAGE_ENTRY_COUNT:
                raise ExecutorError("ARTIFACT_INPUT_ENTRY_LIMIT")
            if total_bytes > MAX_PACKAGE_TOTAL_BYTES:
                raise ExecutorError("ARTIFACT_INPUT_TOTAL_LIMIT")

    for relative_root in SEMANTIC_ARTIFACT_INPUT_ROOTS:
        root = existing_root(relative_root)
        if root is None:
            continue
        entries: list[dict[str, Any]] = []
        visit(
            root,
            root_path=relative_root,
            prefix="",
            entries=entries,
        )
        entries.sort(key=lambda item: item["path"].encode("utf-8"))
        root_total = sum(item["bytes"] for item in entries)
        root_content = hashlib.sha256(
            ARTIFACT_INPUT_TREE_ALGORITHM.encode("ascii")
            + b"\0root\0"
            + relative_root.encode("utf-8")
            + b"\0"
            + canonical_json_bytes(entries)
        ).hexdigest()
        scoped_roots.append(
            {
                "rootPath": relative_root,
                "entryCount": len(entries),
                "totalBytes": root_total,
                "contentRootSha256": root_content,
            }
        )

    content_root = hashlib.sha256(
        ARTIFACT_INPUT_TREE_ALGORITHM.encode("ascii")
        + b"\0multi-root\0"
        + canonical_json_bytes(scoped_roots)
    ).hexdigest()
    return scoped_roots, {
        "rootCount": len(scoped_roots),
        "entryCount": entry_count,
        "totalBytes": total_bytes,
        "contentRootSha256": content_root,
    }


def _semantic_purelib_root(repo_root: Path) -> Path:
    value = sysconfig.get_path("purelib")
    if not isinstance(value, str) or not os.path.isabs(value):
        raise ExecutorError("SEMANTIC_PURELIB_ROOT")
    lexical = Path(os.path.abspath(value))
    try:
        resolved = lexical.resolve(strict=True)
    except OSError:
        raise ExecutorError("SEMANTIC_PURELIB_ROOT") from None
    if (
        lexical != resolved
        or not resolved.is_dir()
        or _path_is_within(repo_root, resolved)
        or _is_reparse_or_link(resolved)
    ):
        raise ExecutorError("SEMANTIC_PURELIB_ROOT")
    return resolved


def _semantic_site_dependency_inventory(
    repo_root: Path,
) -> tuple[dict[str, Any], dict[str, dict[str, Any]]]:
    purelib = _semantic_purelib_root(repo_root)
    entries: list[dict[str, Any]] = []
    identities: set[tuple[int, int]] = set()

    def add_file(path: Path, relative: str) -> None:
        try:
            info = os.lstat(path)
        except OSError:
            raise ExecutorError("SEMANTIC_SITE_TREE_READ") from None
        if (
            _is_reparse_or_link(path)
            or not stat.S_ISREG(info.st_mode)
            or not (0 <= info.st_size <= MAX_PACKAGE_FILE_BYTES)
        ):
            raise ExecutorError("SEMANTIC_SITE_FILE")
        identity = (int(info.st_dev), int(info.st_ino))
        if identity in identities:
            raise ExecutorError("SEMANTIC_SITE_HARDLINK")
        identities.add(identity)
        flags = os.O_RDONLY
        if hasattr(os, "O_BINARY"):
            flags |= os.O_BINARY
        if hasattr(os, "O_NOFOLLOW"):
            flags |= os.O_NOFOLLOW
        descriptor: int | None = None
        try:
            descriptor = os.open(path, flags)
            opened_before = os.fstat(descriptor)
            if not os.path.samestat(opened_before, info):
                raise ExecutorError("SEMANTIC_SITE_FILE_RACE")
            digest = hashlib.sha256()
            size = 0
            while True:
                block = os.read(descriptor, 1024 * 1024)
                if not block:
                    break
                digest.update(block)
                size += len(block)
                if size > MAX_PACKAGE_FILE_BYTES:
                    raise ExecutorError("SEMANTIC_SITE_FILE_LIMIT")
            opened_after = os.fstat(descriptor)
            lexical_after = os.lstat(path)
            if (
                not os.path.samestat(opened_before, opened_after)
                or not os.path.samestat(opened_after, lexical_after)
                or opened_before.st_size != opened_after.st_size
                or getattr(opened_before, "st_mtime_ns", None)
                != getattr(opened_after, "st_mtime_ns", None)
                or size != opened_after.st_size
            ):
                raise ExecutorError("SEMANTIC_SITE_FILE_RACE")
        except ExecutorError:
            raise
        except OSError:
            raise ExecutorError("SEMANTIC_SITE_TREE_READ") from None
        finally:
            if descriptor is not None:
                os.close(descriptor)
        entries.append(
            {"path": relative, "bytes": size, "sha256": digest.hexdigest()}
        )

    def visit(directory: Path, prefix: str) -> None:
        if _is_reparse_or_link(directory) or not directory.is_dir():
            raise ExecutorError("SEMANTIC_SITE_DIRECTORY")
        try:
            children = sorted(
                tuple(os.scandir(directory)),
                key=lambda item: item.name.encode("utf-8"),
            )
        except (OSError, UnicodeEncodeError):
            raise ExecutorError("SEMANTIC_SITE_TREE_READ") from None
        for child in children:
            relative = f"{prefix}/{child.name}"
            path = directory / child.name
            try:
                info = os.lstat(path)
            except OSError:
                raise ExecutorError("SEMANTIC_SITE_TREE_READ") from None
            if _is_reparse_or_link(path):
                raise ExecutorError("SEMANTIC_SITE_REPARSE")
            if stat.S_ISDIR(info.st_mode):
                visit(path, relative)
            elif stat.S_ISREG(info.st_mode):
                add_file(path, relative)
            else:
                raise ExecutorError("SEMANTIC_SITE_NONREGULAR")
            if len(entries) > MAX_PACKAGE_ENTRY_COUNT:
                raise ExecutorError("SEMANTIC_SITE_ENTRY_LIMIT")

    for relative, kind in SEMANTIC_SITE_ALLOWED_ROOTS:
        path = purelib.joinpath(*relative.split("/"))
        if kind == "directory":
            visit(path, relative)
        else:
            add_file(path, relative)
    entries.sort(key=lambda item: item["path"].encode("utf-8"))
    total = sum(item["bytes"] for item in entries)
    if total > MAX_PACKAGE_TOTAL_BYTES:
        raise ExecutorError("SEMANTIC_SITE_TOTAL_LIMIT")
    content_root = hashlib.sha256(
        SEMANTIC_SITE_TREE_ALGORITHM.encode("ascii")
        + b"\0"
        + canonical_json_bytes(entries)
    ).hexdigest()
    summary = {
        "entryCount": len(entries),
        "totalBytes": total,
        "contentRootSha256": content_root,
    }
    return summary, {item["path"]: item for item in entries}


def _extract_semantic_site_marker(
    stdout_raw: bytes,
) -> tuple[bytes, tuple[str, ...]]:
    marker = b"CAICLI_SEMANTIC_SITE_MODULES="
    found: list[tuple[int, bytes]] = []
    lines = stdout_raw.splitlines(keepends=True)
    for index, line in enumerate(lines):
        stripped = line.rstrip(b"\r\n")
        if stripped.startswith(marker):
            found.append((index, stripped[len(marker) :]))
    if len(found) != 1:
        raise ExecutorError("SEMANTIC_SITE_MARKER")
    index, raw = found[0]
    value = _read_json_bytes(raw, "SEMANTIC_SITE_MARKER")
    paths = value.get("paths") if isinstance(value, dict) else None
    if (
        not isinstance(value, dict)
        or raw != canonical_json_bytes(value)
        or set(value) != {"protocol", "paths"}
        or value.get("protocol") != SEMANTIC_SITE_MODULE_PROTOCOL
        or not isinstance(paths, list)
        or not paths
        or paths != sorted(paths)
        or len(paths) != len(set(paths))
        or any(
            not isinstance(path, str)
            or PurePosixPath(path).is_absolute()
            or any(part in {"", ".", ".."} for part in PurePosixPath(path).parts)
            for path in paths
        )
    ):
        raise ExecutorError("SEMANTIC_SITE_MARKER")
    clean = b"".join(line for offset, line in enumerate(lines) if offset != index)
    return clean, tuple(paths)


def _semantic_loaded_site_summary(
    paths: Sequence[str],
    inventory: Mapping[str, Mapping[str, Any]],
) -> dict[str, Any]:
    try:
        entries = [dict(inventory[path]) for path in paths]
    except KeyError:
        raise ExecutorError("SEMANTIC_SITE_LOADED_FILE") from None
    total = sum(int(item["bytes"]) for item in entries)
    content_root = hashlib.sha256(
        SEMANTIC_SITE_TREE_ALGORITHM.encode("ascii")
        + b"\0loaded\0"
        + canonical_json_bytes(entries)
    ).hexdigest()
    return {
        "entryCount": len(entries),
        "totalBytes": total,
        "contentRootSha256": content_root,
    }


def _validate_trusted_build_report(
    control_root: Path,
    *,
    binding: Mapping[str, Any],
    product_candidate: str,
    command_id: str,
) -> None:
    if (
        not isinstance(binding, Mapping)
        or frozenset(binding) != _TRUSTED_COMMAND_REPORT_BINDING_KEYS
        or not isinstance(binding.get("path"), str)
        or not isinstance(binding.get("rawSha256"), str)
        or _SHA256.fullmatch(binding["rawSha256"]) is None
    ):
        raise ExecutorError("PACKAGE_BUILD_REPORT")
    report_path = PurePosixPath(*_relative_parts(binding["path"])).as_posix()
    if "/gate-evidence/" not in report_path or not report_path.endswith(
        ".trusted-product-report.json"
    ):
        raise ExecutorError("PACKAGE_BUILD_REPORT")
    raw = _read_fixed_bytes(control_root, report_path, limit=MAX_JSON_BYTES)
    report = _read_json_bytes(raw, "PACKAGE_BUILD_REPORT")
    checkout = report.get("checkoutIdentity") if isinstance(report, dict) else None
    before = checkout.get("before") if isinstance(checkout, dict) else None
    after = checkout.get("after") if isinstance(checkout, dict) else None
    policy = report.get("commandPolicy") if isinstance(report, dict) else None
    counts = report.get("counts") if isinstance(report, dict) else None
    if (
        hashlib.sha256(raw).hexdigest() != binding["rawSha256"]
        or raw != canonical_json_bytes(report)
        or report.get("schemaVersion") != SCHEMA_VERSION
        or report.get("runnerId") != RUNNER_ID
        or report.get("productCandidate") != product_candidate
        or report.get("commandId") != command_id
        or report.get("exitCode") != 0
        or report.get("resultProtocol") != PRODUCT_COMMAND_RESULT_PROTOCOL
        or not isinstance(policy, dict)
        or policy.get("policyId") != TRUSTED_PRODUCT_POLICY_ID
        or not isinstance(counts, dict)
        or counts.get("failed") != 0
        or not isinstance(checkout, dict)
        or checkout.get("productCandidate") != product_candidate
        or not isinstance(before, dict)
        or not isinstance(after, dict)
        or before.get("headCommit") != checkout.get("expectedHead")
        or after != before
        or before.get("trackedStatus") != "Clean"
    ):
        raise ExecutorError("PACKAGE_BUILD_REPORT")


def _validate_package_build_receipt(
    candidate_root: Path,
    control_root: Path,
    *,
    gate_id: str,
    product_candidate: str,
    binding: Mapping[str, Any],
    tree: Mapping[str, Any],
) -> None:
    artifact_root = PROVIDER_ARTIFACT_ROOTS[gate_id]
    if (
        not isinstance(binding, Mapping)
        or frozenset(binding) != _PACKAGE_BUILD_BINDING_KEYS
        or binding.get("path") != f"{artifact_root}/package-build-receipt.json"
        or not isinstance(binding.get("rawSha256"), str)
        or _SHA256.fullmatch(binding["rawSha256"]) is None
    ):
        raise ExecutorError("PACKAGE_BUILD_RECEIPT")
    raw = _read_fixed_bytes(control_root, binding["path"], limit=MAX_JSON_BYTES)
    receipt = _read_json_bytes(raw, "PACKAGE_BUILD_RECEIPT")
    try:
        started = _parse_timestamp(receipt.get("startedAt"), "PACKAGE_BUILD_TIME")
        finished = _parse_timestamp(receipt.get("finishedAt"), "PACKAGE_BUILD_TIME")
    except AttributeError:
        raise ExecutorError("PACKAGE_BUILD_RECEIPT") from None
    tree_ids = _decode_git_lines(
        _git(candidate_root, ["rev-parse", f"{product_candidate}^{{tree}}"] ).stdout,
        "PACKAGE_BUILD_CANDIDATE",
    )
    command_id = receipt.get("commandId") if isinstance(receipt, dict) else None
    if (
        not isinstance(receipt, dict)
        or frozenset(receipt) != _PACKAGE_BUILD_RECEIPT_KEYS
        or hashlib.sha256(raw).hexdigest() != binding["rawSha256"]
        or raw != canonical_json_bytes(receipt)
        or receipt.get("schemaVersion") != SCHEMA_VERSION
        or receipt.get("protocol") != PACKAGE_BUILD_RECEIPT_PROTOCOL
        or receipt.get("status") != "Passed"
        or receipt.get("goalId") != GOAL_ID
        or receipt.get("productCandidate") != product_candidate
        or len(tree_ids) != 1
        or receipt.get("sourceTreeObjectId") != tree_ids[0]
        or receipt.get("packageRoot") != FIXED_PACKAGE_ROOT
        or receipt.get("treeRootSha256") != tree.get("treeRootSha256")
        or receipt.get("entryCount") != tree.get("entryCount")
        or receipt.get("totalBytes") != tree.get("totalBytes")
        or receipt.get("entrypoint") != FIXED_PACKAGE_ENTRYPOINT
        or receipt.get("argv") != list(FIXED_PACKAGE_ARGV)
        or receipt.get("appHostPath") != FIXED_APPHOST_PATH
        or command_id not in {
            "desktop-package-rebuild",
            "package-identity-inventory",
            "package-inventory-verify",
            "independent-build-root-a",
            "independent-build-root-b",
        }
        or receipt.get("exitCode") != 0
        or started >= finished
    ):
        raise ExecutorError("PACKAGE_BUILD_RECEIPT")
    _validate_trusted_build_report(
        control_root,
        binding=receipt["trustedCommandReport"],
        product_candidate=product_candidate,
        command_id=command_id,
    )


def _package_identity_evidence_binding(
    candidate_root: Path,
    control_root: Path,
    *,
    gate_id: str,
    product_candidate: str,
    evidence_path: str,
) -> PackageTreeBinding:
    artifact_root = PROVIDER_ARTIFACT_ROOTS[gate_id]
    relative = PurePosixPath(*_relative_parts(evidence_path)).as_posix()
    if relative != f"{artifact_root}/package-identity.json":
        raise ExecutorError("PACKAGE_IDENTITY_PATH")
    raw = _read_fixed_bytes(control_root, relative, limit=MAX_JSON_BYTES)
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        raise ExecutorError("PACKAGE_IDENTITY_JSON") from None
    if _contains_secret(text):
        raise ExecutorError("PACKAGE_IDENTITY_SECRET")
    document = _read_json_bytes(raw, "PACKAGE_IDENTITY_JSON")
    legacy_package_identity = (
        document.get("packageIdentity") if isinstance(document, dict) else None
    )
    if isinstance(legacy_package_identity, dict):
        legacy_binary_path = legacy_package_identity.get("path")
        allowed_legacy_paths = {
            FIXED_PACKAGE_ENTRYPOINT,
            f"{FIXED_PACKAGE_ROOT}/{FIXED_PACKAGE_ENTRYPOINT}",
        }
        if (
            not isinstance(legacy_binary_path, str)
            or legacy_binary_path.replace("\\", "/") not in allowed_legacy_paths
        ):
            # Reject legacy/substituted binary metadata before any code can
            # consider the referenced path.  In particular, an evidence file
            # must never redirect package validation toward .env.local or an
            # arbitrary artifact.
            raise ExecutorError("PACKAGE_BINARY_PATH")
    tree = document.get("tree") if isinstance(document, dict) else None
    entrypoint = document.get("entrypoint") if isinstance(document, dict) else None
    if (
        not isinstance(document, dict)
        or frozenset(document) != _PACKAGE_IDENTITY_KEYS
        or raw != canonical_json_bytes(document)
        or document.get("schemaVersion") != SCHEMA_VERSION
        or document.get("protocol") != PACKAGE_TREE_IDENTITY_PROTOCOL
        or document.get("status") != "Passed"
        or document.get("goalId") != GOAL_ID
        or document.get("productCandidate") != product_candidate
        or document.get("packageRoot") != FIXED_PACKAGE_ROOT
        or document.get("appHostPath") != FIXED_APPHOST_PATH
        or not isinstance(entrypoint, dict)
        or frozenset(entrypoint) != _PACKAGE_ENTRYPOINT_KEYS
        or entrypoint.get("path") != FIXED_PACKAGE_ENTRYPOINT
        or entrypoint.get("argv") != list(FIXED_PACKAGE_ARGV)
        or not isinstance(tree, dict)
        or frozenset(tree) != _PACKAGE_TREE_KEYS
        or tree.get("algorithm") != PACKAGE_TREE_ALGORITHM
        or not _is_int(tree.get("entryCount"))
        or not 0 < tree["entryCount"] <= MAX_PACKAGE_ENTRY_COUNT
        or not _is_int(tree.get("totalBytes"))
        or not 0 < tree["totalBytes"] <= MAX_PACKAGE_TOTAL_BYTES
        or not isinstance(tree.get("treeRootSha256"), str)
        or _SHA256.fullmatch(tree["treeRootSha256"]) is None
        or not isinstance(tree.get("entries"), list)
        or len(tree["entries"]) != tree["entryCount"]
        or any(
            not isinstance(item, dict)
            or frozenset(item) != _PACKAGE_TREE_ENTRY_KEYS
            for item in tree["entries"]
        )
    ):
        raise ExecutorError("PACKAGE_IDENTITY_BINDING")
    package_root = _safe_directory(
        candidate_root, FIXED_PACKAGE_ROOT, "PACKAGE_ROOT"
    )
    actual_tree = _package_inventory(package_root)
    if actual_tree != tree:
        raise ExecutorError("PACKAGE_TREE_IDENTITY")
    entry_map = {item["path"]: item for item in tree["entries"]}
    desktop = entry_map.get(FIXED_PACKAGE_ENTRYPOINT)
    apphost = entry_map.get(FIXED_APPHOST_PATH)
    if not isinstance(desktop, dict) or not isinstance(apphost, dict):
        raise ExecutorError("PACKAGE_ENTRYPOINT")
    _validate_package_build_receipt(
        candidate_root,
        control_root,
        gate_id=gate_id,
        product_candidate=product_candidate,
        binding=document["buildReceipt"],
        tree=tree,
    )
    return PackageTreeBinding(
        path=relative,
        sha256=hashlib.sha256(raw).hexdigest(),
        package_root=FIXED_PACKAGE_ROOT,
        tree_root_sha256=tree["treeRootSha256"],
        entry_count=tree["entryCount"],
        total_bytes=tree["totalBytes"],
        entrypoint=FIXED_PACKAGE_ENTRYPOINT,
        entrypoint_sha256=desktop["sha256"],
        argv=FIXED_PACKAGE_ARGV,
        apphost_path=FIXED_APPHOST_PATH,
        apphost_sha256=apphost["sha256"],
    )


def _provider_boundary_immutable_binding(
    candidate_root: Path,
    *,
    gate_id: str,
    product_candidate: str,
) -> tuple[dict[str, Any], bytes, str]:
    relative = PROVIDER_BOUNDARY_DECISION_PATHS.get(gate_id)
    if relative is None:
        raise ExecutorError("PROVIDER_BOUNDARY_GATE")
    return _immutable_control_document(
        candidate_root,
        relative_path=relative,
        product_candidate=product_candidate,
        json_code="PROVIDER_BOUNDARY_JSON",
        history_code="PROVIDER_BOUNDARY_HISTORY",
    )


def _validate_strong_canaries(
    control_root: Path,
    *,
    value: Any,
    decision_id: str,
    product_candidate: str,
    artifact_root: str,
) -> str:
    if (
        not isinstance(value, dict)
        or frozenset(value) != _STRONG_CANARY_KEYS
        or not isinstance(value.get("receiptPath"), str)
        or not value["receiptPath"].startswith(f"{artifact_root}/")
        or not isinstance(value.get("rawSha256"), str)
        or _SHA256.fullmatch(value["rawSha256"]) is None
        or not isinstance(value.get("sandboxPolicySha256"), str)
        or _SHA256.fullmatch(value["sandboxPolicySha256"]) is None
        or value.get("filesystemPassed") is not True
        or value.get("networkPassed") is not True
        or value.get("processPassed") is not True
    ):
        raise ExecutorError("PROVIDER_STRONG_CANARIES")
    relative = PurePosixPath(*_relative_parts(value["receiptPath"])).as_posix()
    raw = _read_fixed_bytes(control_root, relative, limit=MAX_JSON_BYTES)
    receipt = _read_json_bytes(raw, "PROVIDER_STRONG_CANARY_RECEIPT")
    if (
        hashlib.sha256(raw).hexdigest() != value["rawSha256"]
        or raw != canonical_json_bytes(receipt)
        or not isinstance(receipt, dict)
        or receipt.get("schemaVersion") != SCHEMA_VERSION
        or receipt.get("protocol")
        != "week84-92-strong-isolation-canary-v1"
        or receipt.get("status") != "Passed"
        or receipt.get("goalId") != GOAL_ID
        or receipt.get("decisionId") != decision_id
        or receipt.get("mode") != "strong-isolation"
        or receipt.get("productCandidate") != product_candidate
        or receipt.get("sandboxPolicySha256")
        != value["sandboxPolicySha256"]
        or receipt.get("filesystemPassed") is not True
        or receipt.get("networkPassed") is not True
        or receipt.get("processPassed") is not True
    ):
        raise ExecutorError("PROVIDER_STRONG_CANARIES")
    return value["sandboxPolicySha256"]


def verify_provider_boundary_decision(
    candidate_root: str | os.PathLike[str],
    control_root: str | os.PathLike[str],
    *,
    gate_id: str,
    product_candidate: str,
    package: PackageTreeBinding,
) -> ProviderBoundaryBinding:
    """Verify immutable A6 authority before any credential read or turn reserve."""

    candidate = _normalise_repo_root(candidate_root)
    control = Path(control_root).resolve(strict=True)
    decision, raw, first_add = _provider_boundary_immutable_binding(
        candidate,
        gate_id=gate_id,
        product_candidate=product_candidate,
    )
    boundary_gate = PROVIDER_BOUNDARY_GATES[gate_id]
    expected_allowed_gates = list(PROVIDER_BOUNDARY_ALLOWED_GATES[boundary_gate])
    package_value = decision.get("packageIdentity")
    challenge = decision.get("userChallengeReceipt")
    decision_id = decision.get("decisionId")
    mode = decision.get("mode")
    now = datetime.now(timezone.utc)
    issued = _parse_timestamp(decision.get("issuedAt"), "PROVIDER_BOUNDARY_TIME")
    expires = _parse_timestamp(decision.get("expiresAt"), "PROVIDER_BOUNDARY_TIME")
    decided = _parse_timestamp(
        challenge.get("decidedAt") if isinstance(challenge, dict) else None,
        "PROVIDER_BOUNDARY_TIME",
    )
    expected_package = {
        "path": package.path,
        "rawSha256": package.sha256,
        "packageRoot": package.package_root,
        "treeRootSha256": package.tree_root_sha256,
        "entryCount": package.entry_count,
        "totalBytes": package.total_bytes,
        "entrypoint": package.entrypoint,
        "argv": list(package.argv),
        "appHostPath": package.apphost_path,
    }
    if (
        frozenset(decision) != _BOUNDARY_DECISION_KEYS
        or decision.get("schemaVersion") != SCHEMA_VERSION
        or decision.get("protocol") != PROVIDER_BOUNDARY_DECISION_PROTOCOL
        or decision.get("goalId") != GOAL_ID
        or not isinstance(decision_id, str)
        or _SAFE_ID.fullmatch(decision_id) is None
        or decision.get("boundaryGate") != boundary_gate
        or mode not in {"strong-isolation", "cooperative-candidate"}
        or decision.get("productCandidate") != product_candidate
        or not isinstance(package_value, dict)
        or frozenset(package_value) != _BOUNDARY_PACKAGE_KEYS
        or package_value != expected_package
        or decision.get("allowedGates") != expected_allowed_gates
        or decision.get("allowedScopes")
        != list(PROVIDER_BOUNDARY_ALLOWED_SCOPES)
        or decision.get("authorizedTurns") != 34
        or decision.get("maxTurns") != PROVIDER_MAX_TURNS
        or decision.get("revoked") is not False
        or not isinstance(challenge, dict)
        or frozenset(challenge) != _BOUNDARY_CHALLENGE_KEYS
        or not isinstance(challenge.get("requestId"), str)
        or _SAFE_ID.fullmatch(challenge["requestId"]) is None
        or not isinstance(challenge.get("challengeCode"), str)
        or _CHALLENGE_CODE.fullmatch(challenge["challengeCode"]) is None
        or challenge.get("confirmedBy") != "User"
        or challenge.get("response")
        != (
            f"{challenge.get('requestId')} {challenge.get('challengeCode')} "
            "Approved"
        )
        or challenge.get("rawResponseSha256")
        != hashlib.sha256(challenge["response"].encode("utf-8")).hexdigest()
        or decided > issued
        or issued >= expires
        or issued > now + timedelta(seconds=1)
        or expires <= now
    ):
        raise ExecutorError("PROVIDER_BOUNDARY_BINDING")
    try:
        commit_time_raw = _git(
            candidate, ["show", "-s", "--format=%cI", first_add]
        ).stdout.decode("ascii").strip()
        commit_time = datetime.fromisoformat(commit_time_raw.replace("Z", "+00:00"))
    except (UnicodeDecodeError, ValueError):
        raise ExecutorError("PROVIDER_BOUNDARY_HISTORY") from None
    if commit_time.tzinfo is None or issued > commit_time.astimezone(timezone.utc):
        raise ExecutorError("PROVIDER_BOUNDARY_TIME")
    if mode == "cooperative-candidate":
        if (
            decision.get("strongCanaries") is not None
            or decision.get("cooperativeTrustStatement")
            != COOPERATIVE_TRUST_STATEMENT
        ):
            raise ExecutorError("PROVIDER_BOUNDARY_MODE")
        policy_sha: str | None = None
    else:
        if decision.get("cooperativeTrustStatement") is not None:
            raise ExecutorError("PROVIDER_BOUNDARY_MODE")
        policy_sha = _validate_strong_canaries(
            control,
            value=decision.get("strongCanaries"),
            decision_id=decision_id,
            product_candidate=product_candidate,
            artifact_root=PROVIDER_ARTIFACT_ROOTS[gate_id],
        )
    return ProviderBoundaryBinding(
        path=PROVIDER_BOUNDARY_DECISION_PATHS[gate_id],
        sha256=hashlib.sha256(raw).hexdigest(),
        first_add_commit=first_add,
        decision_id=decision_id,
        mode=mode,
        package_tree_root_sha256=package.tree_root_sha256,
        sandbox_policy_sha256=policy_sha,
    )


def _provider_batches(phases: Sequence[str]) -> tuple[tuple[str, ...], ...]:
    phase_tuple = tuple(phases)
    if not phase_tuple or any(not isinstance(item, str) for item in phase_tuple):
        raise ExecutorError("SEGMENT_PHASES")
    batches: list[tuple[str, ...]] = []
    index = 0
    while index < len(phase_tuple):
        phase = phase_tuple[index]
        sizes = _PROVIDER_BATCH_SIZES.get(phase)
        if sizes is None or len(sizes) != 1:
            raise ExecutorError("SEGMENT_PHASES")
        size = sizes[0]
        batch = phase_tuple[index : index + size]
        if batch != (phase,) * size:
            raise ExecutorError("SEGMENT_PHASES")
        batches.append(batch)
        index += size
    return tuple(batches)


def _validate_provider_run_phase_slice(
    gate_id: str,
    requested_phases: Sequence[str],
) -> None:
    """Reject impossible run plans without reading or creating a ledger."""

    requested = tuple(requested_phases)
    layout = PROVIDER_PHASE_LAYOUT[gate_id]
    if not any(
        requested == layout[start : start + len(requested)]
        for start in range(0, len(layout) - len(requested) + 1)
    ):
        raise ExecutorError("SEGMENT_PHASES")


def _preflight_provider_run_plan(
    repo_root: Path,
    *,
    gate_id: str,
    product_candidate: str,
    attempt_id: str,
    requested_phases: Sequence[str],
    finish_success: bool,
) -> None:
    requested = tuple(requested_phases)
    with _FileLock(repo_root):
        ledger = _load_ledger_locked(repo_root)
        attempts, completions, finished = _attempt_views(ledger)
        existing = attempts.get(attempt_id, [])
        if existing and (
            existing[0]["gateId"] != gate_id
            or existing[0]["productCandidate"] != product_candidate
        ):
            raise ExecutorError("ATTEMPT_OWNER")
        if attempt_id in finished:
            raise ExecutorError("ATTEMPT_REOPENED")
        if any(
            other_id != attempt_id and other_id not in finished
            for other_id in attempts
        ):
            raise ExecutorError("OPEN_ATTEMPT_EXISTS")
        if any(item["reservationId"] not in completions for item in existing):
            raise ExecutorError("RECOVERY_REQUIRED")
        layout = PROVIDER_PHASE_LAYOUT[gate_id]
        prior = tuple(item["phase"] for item in existing)
        if prior != layout[: len(prior)]:
            raise ExecutorError("ATTEMPT_PHASE_PREFIX")
        remaining = layout[len(prior) :]
        if requested != remaining[: len(requested)]:
            raise ExecutorError("SEGMENT_PHASES")
        if finish_success and requested != remaining:
            raise ExecutorError("INCOMPLETE_SUCCESS")
        if ledger["remainingTurns"] < len(remaining):
            raise ExecutorError("PROVIDER_CAP")


def _exclusive_write_json(
    repo_root: Path,
    relative_path: str,
    value: Any,
) -> bytes:
    raw = canonical_json_bytes(value)
    _path, handle = _exclusive_open(repo_root, relative_path)
    try:
        _write_open_file(handle, raw)
    finally:
        handle.close()
    return raw


def _provider_descriptor(
    repo_root: Path,
    *,
    batch: ReservationBatch,
    batch_id: str,
    package_binding: PackageTreeBinding,
    boundary_binding: ProviderBoundaryBinding,
    scenario_source: Mapping[str, str],
    driver_source: Mapping[str, str],
    launch_receipt_path: str,
    staging_relative_root: str,
    run_token_sha256: str,
    turn_ordinals: Sequence[int],
    profile_ordinal: int | None,
) -> dict[str, Any]:
    phase = batch.phases[0]
    if (
        (phase == "provider-resource") != (profile_ordinal is not None)
        or (
            profile_ordinal is not None
            and not (1 <= profile_ordinal <= 5)
        )
    ):
        raise ExecutorError("PROVIDER_PROFILE")
    controlled: dict[str, str] | None = None
    preauthorization: dict[str, str] | None = None
    controlled_gate = CONTROLLED_DESCENDANT_GATES.get(batch.gate_id)
    if controlled_gate is not None:
        binding = verify_controlled_write_tombstone(
            repo_root,
            gate_id=controlled_gate,
            product_candidate=batch.product_candidate,
        )
        controlled = {
            "path": binding.path,
            "sha256": binding.sha256,
            "commit": binding.commit,
        }
        preauthorization = {
            "path": binding.preauthorization.path,
            "sha256": binding.preauthorization.sha256,
            "commit": binding.preauthorization.commit,
        }
    return {
        "schemaVersion": SCHEMA_VERSION,
        "protocol": PROVIDER_DESCRIPTOR_PROTOCOL,
        "goalId": GOAL_ID,
        "batchId": batch_id,
        "gateId": batch.gate_id,
        "phase": phase,
        "profileOrdinal": profile_ordinal,
        "productCandidate": batch.product_candidate,
        "attemptId": batch.attempt_id,
        "runId": batch.run_id,
        "artifactRoot": PROVIDER_ARTIFACT_ROOTS[batch.gate_id],
        "packageIdentityEvidence": package_binding.descriptor_binding(),
        "providerBoundaryDecision": boundary_binding.descriptor_binding(),
        "scenarioPath": PROVIDER_SCENARIO_PATHS[phase],
        "scenarioSource": dict(scenario_source),
        "driverPath": PROVIDER_DRIVER_PATHS[phase],
        "driverSource": dict(driver_source),
        "packageLaunchReceiptPath": launch_receipt_path,
        "stagingRelativeRoot": staging_relative_root,
        "runTokenSha256": run_token_sha256,
        "controlledWriteTombstone": controlled,
        "controlledWritePreauthorization": preauthorization,
        "reservations": [
            {
                "reservationId": reservation_id,
                "reservationSequence": reservation_sequence,
                "turnOrdinal": turn_ordinal,
            }
            for reservation_id, reservation_sequence, turn_ordinal in zip(
                batch.reservation_ids,
                batch.reservation_sequences,
                turn_ordinals,
                strict=True,
            )
        ],
    }


def _validate_provider_observation(
    raw: bytes,
    *,
    descriptor: Mapping[str, Any],
) -> list[dict[str, Any]]:
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        raise ExecutorError("PROVIDER_OBSERVATION_UTF8") from None
    if _contains_secret(text):
        raise ExecutorError("PROVIDER_OBSERVATION_SECRET")
    document = _read_json_bytes(raw, "PROVIDER_OBSERVATION_JSON")
    if (
        not isinstance(document, dict)
        or frozenset(document) != _OBSERVATION_KEYS
        or document.get("schemaVersion") != SCHEMA_VERSION
        or document.get("protocol") != PROVIDER_OBSERVED_REQUEST_PROTOCOL
        or any(
            document.get(field) != descriptor.get(field)
            for field in (
                "batchId",
                "gateId",
                "phase",
                "productCandidate",
                "attemptId",
                "runId",
                "packageIdentityEvidence",
                "providerBoundaryDecision",
            )
        )
        or not isinstance(document.get("requests"), list)
    ):
        raise ExecutorError("PROVIDER_OBSERVATION_BINDING")
    reservations = descriptor["reservations"]
    requests = document["requests"]
    if len(requests) != len(reservations):
        raise ExecutorError("PROVIDER_OBSERVATION_COUNT")
    seen: set[str] = set()
    accepted: list[dict[str, Any]] = []
    for ordinal, (request, reservation) in enumerate(
        zip(requests, reservations, strict=True), start=1
    ):
        reservation_id = reservation["reservationId"]
        if (
            not _valid_observed_request(
                request,
                reservation_id=reservation_id,
                reservation_sequence=reservation["reservationSequence"],
            )
            or request["requestOrdinal"] != ordinal
            or request["status"] != "Succeeded"
            or reservation_id in seen
        ):
            raise ExecutorError("PROVIDER_OBSERVATION_REQUEST")
        seen.add(reservation_id)
        accepted.append(dict(request))
    return accepted


def _validate_package_launch_receipt(
    raw: bytes,
    *,
    descriptor: Mapping[str, Any],
    package: PackageTreeBinding,
    boundary: ProviderBoundaryBinding,
    receipt_path: str,
    observation_path: str,
    observation_raw: bytes,
) -> dict[str, Any]:
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        raise ExecutorError("PACKAGE_LAUNCH_RECEIPT_UTF8") from None
    if _contains_secret(text):
        raise ExecutorError("PACKAGE_LAUNCH_RECEIPT_SECRET")
    receipt = _read_json_bytes(raw, "PACKAGE_LAUNCH_RECEIPT_JSON")
    required = _LAUNCH_RECEIPT_REQUIRED_KEYS | {
        "schemaVersion",
        "protocol",
        "status",
        "attemptId",
        "runId",
        "runTokenSha256",
        "gatewayObservation",
        "scenarioSource",
        "driverSource",
    }
    source = receipt.get("sourcePackage") if isinstance(receipt, dict) else None
    staged_before = (
        receipt.get("stagedPackageBefore") if isinstance(receipt, dict) else None
    )
    staged_after = (
        receipt.get("stagedPackageAfter") if isinstance(receipt, dict) else None
    )
    launches = receipt.get("launches") if isinstance(receipt, dict) else None
    desktop = launches.get("desktop") if isinstance(launches, dict) else None
    apphost = launches.get("appHost") if isinstance(launches, dict) else None
    containment = receipt.get("containment") if isinstance(receipt, dict) else None
    assertions = (
        receipt.get("scenarioAssertions") if isinstance(receipt, dict) else None
    )
    cleanup = receipt.get("cleanup") if isinstance(receipt, dict) else None
    gateway = (
        receipt.get("gatewayObservation") if isinstance(receipt, dict) else None
    )
    expected_source = {
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
    expected_stage = {
        "packageTreeRootSha256": package.tree_root_sha256,
        "entryCount": package.entry_count,
        "totalBytes": package.total_bytes,
    }
    expected_reservations = [
        item["reservationId"] for item in descriptor["reservations"]
    ]
    if (
        not isinstance(receipt, dict)
        or frozenset(receipt) != required
        or raw != canonical_json_bytes(receipt)
        or receipt.get("schemaVersion") != SCHEMA_VERSION
        or receipt.get("protocol") != PACKAGE_LAUNCH_RECEIPT_PROTOCOL
        or receipt.get("status") != "Passed"
        or any(
            receipt.get(field) != descriptor.get(field)
            for field in (
                "gateId",
                "productCandidate",
                "phase",
                "profileOrdinal",
                "batchId",
                "attemptId",
                "runId",
                "runTokenSha256",
            )
        )
        or receipt.get("goalId") != GOAL_ID
        or receipt.get("packageTreeRootSha256") != package.tree_root_sha256
        or receipt.get("boundaryDecision") != boundary.descriptor_binding()
        or receipt.get("scenarioSource") != descriptor.get("scenarioSource")
        or receipt.get("driverSource") != descriptor.get("driverSource")
        or source != expected_source
        or staged_before != expected_stage
        or staged_after != expected_stage
        or not isinstance(launches, dict)
        or frozenset(launches) != {"desktop", "appHost"}
        or not isinstance(desktop, dict)
        or not isinstance(apphost, dict)
        or frozenset(desktop)
        != {"imagePath", "imageSha256", "pid", "startedAt", "exitedAt", "exitCode"}
        or frozenset(apphost)
        != {"imagePath", "imageSha256", "pid", "startedAt", "exitedAt", "exitCode"}
        or desktop.get("imagePath") != package.entrypoint
        or desktop.get("imageSha256") != package.entrypoint_sha256
        or apphost.get("imagePath") != package.apphost_path
        or apphost.get("imageSha256") != package.apphost_sha256
        or any(
            not _is_int(item.get("pid")) or item["pid"] <= 0
            for item in (desktop, apphost)
        )
        or any(item.get("exitCode") != 0 for item in (desktop, apphost))
        or not isinstance(containment, dict)
        or frozenset(containment)
        != {
            "attachedBeforeRelease",
            "processIsolated",
            "allEgressIsolated",
            "sandboxPolicySha256",
            "strongCanaryEvidence",
        }
        or containment.get("attachedBeforeRelease") is not True
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
        or not isinstance(cleanup, dict)
        or cleanup
        != {"status": "Passed", "processDelta": 0, "temporaryDelta": 0}
        or receipt.get("observedRequestCount") != len(expected_reservations)
        or not isinstance(gateway, dict)
        or gateway
        != {
            "path": observation_path,
            "rawSha256": hashlib.sha256(observation_raw).hexdigest(),
            "requestCount": len(expected_reservations),
            "reservationIds": expected_reservations,
        }
        or PurePosixPath(*_relative_parts(receipt_path)).as_posix()
        != descriptor.get("packageLaunchReceiptPath")
    ):
        raise ExecutorError("PACKAGE_LAUNCH_RECEIPT_BINDING")
    for item in (desktop, apphost):
        started = _parse_timestamp(item["startedAt"], "PACKAGE_LAUNCH_TIME")
        exited = _parse_timestamp(item["exitedAt"], "PACKAGE_LAUNCH_TIME")
        if started >= exited:
            raise ExecutorError("PACKAGE_LAUNCH_TIME")
    if boundary.mode == "cooperative-candidate":
        if (
            containment.get("processIsolated") is not False
            or containment.get("allEgressIsolated") is not False
            or containment.get("sandboxPolicySha256") is not None
            or containment.get("strongCanaryEvidence") is not None
        ):
            raise ExecutorError("PACKAGE_LAUNCH_COOPERATIVE_CLAIM")
    elif (
        containment.get("processIsolated") is not True
        or containment.get("allEgressIsolated") is not True
        or containment.get("sandboxPolicySha256")
        != boundary.sandbox_policy_sha256
        or containment.get("strongCanaryEvidence") is None
    ):
        raise ExecutorError("PACKAGE_LAUNCH_STRONG_CLAIM")
    return {
        "path": receipt_path,
        "sha256": hashlib.sha256(raw).hexdigest(),
        "phase": descriptor["phase"],
        "profileOrdinal": descriptor.get("profileOrdinal"),
        "batchId": descriptor["batchId"],
        "packageTreeRootSha256": package.tree_root_sha256,
    }


def run_provider_segment(
    repo_root: str | os.PathLike[str],
    *,
    gate_id: str,
    product_candidate: str,
    attempt_id: str,
    run_id: str,
    phases: Sequence[str],
    package_identity_evidence_path: str,
    finish_success: bool = False,
    timeout_seconds: float = DEFAULT_CHILD_TIMEOUT_SECONDS,
) -> ProviderRunResult:
    """Run exact frozen provider batches and journal observed requests."""

    root = _normalise_repo_root(repo_root)
    control_root = _repository_context(root).control_root
    if gate_id not in PROVIDER_PHASE_LAYOUT:
        raise ExecutorError("PROVIDER_GATE")
    _verify_candidate_commit(root, product_candidate)
    _validate_safe_id(attempt_id, "ATTEMPT_ID")
    _validate_safe_id(run_id, "RUN_ID")
    batches = _provider_batches(phases)
    flattened_phases = tuple(
        phase for batch_phases in batches for phase in batch_phases
    )
    _validate_provider_run_phase_slice(gate_id, flattened_phases)
    if not isinstance(timeout_seconds, (int, float)) or not (0 < timeout_seconds <= 86400):
        raise ExecutorError("CHILD_TIMEOUT")
    _gate_checkout_preflight(
        root,
        gate_id=gate_id,
        product_candidate=product_candidate,
    )
    for phase in dict.fromkeys(
        phase for batch_phases in batches for phase in batch_phases
    ):
        _provider_control_source_preflight(
            root,
            gate_id=gate_id,
            product_candidate=product_candidate,
            phase=phase,
        )
    package_binding = _package_identity_evidence_binding(
        root,
        control_root,
        gate_id=gate_id,
        product_candidate=product_candidate,
        evidence_path=package_identity_evidence_path,
    )
    boundary_binding = verify_provider_boundary_decision(
        root,
        control_root,
        gate_id=gate_id,
        product_candidate=product_candidate,
        package=package_binding,
    )
    if boundary_binding.mode == "strong-isolation":
        # This Windows executor currently supplies inherited Job cleanup, not
        # filesystem/network/process isolation.  Never relabel it as strong or
        # silently downgrade a strong decision after credentials are loaded.
        raise ExecutorError("STRONG_ISOLATION_UNAVAILABLE")
    _preflight_provider_run_plan(
        control_root,
        gate_id=gate_id,
        product_candidate=product_candidate,
        attempt_id=attempt_id,
        requested_phases=flattened_phases,
        finish_success=finish_success,
    )
    child_environment = _load_provider_environment(control_root, PROVIDER_ENV_PATH)
    last_batch: ReservationBatch | None = None
    ledger: dict[str, Any] | None = None
    exit_code = 0
    for batch_index, batch_phases in enumerate(batches):
        phase = batch_phases[0]
        expected_head, checkout_mode, _snapshot = _provider_checkout_preflight(
            root,
            gate_id=gate_id,
            phase=phase,
            product_candidate=product_candidate,
        )
        (
            policy,
            policy_sha,
            harness_source,
            scenario_source,
            driver_source,
        ) = _provider_control_source_preflight(
            root,
            gate_id=gate_id,
            product_candidate=product_candidate,
            phase=phase,
        )
        package_binding = _package_identity_evidence_binding(
            root,
            control_root,
            gate_id=gate_id,
            product_candidate=product_candidate,
            evidence_path=package_identity_evidence_path,
        )
        boundary_binding = verify_provider_boundary_decision(
            root,
            control_root,
            gate_id=gate_id,
            product_candidate=product_candidate,
            package=package_binding,
        )
        final_batch = batch_index == len(batches) - 1
        batch = reserve_provider_segment(
            root,
            gate_id=gate_id,
            product_candidate=product_candidate,
            attempt_id=attempt_id,
            run_id=run_id,
            phases=batch_phases,
            finish_success=finish_success and final_batch,
        )
        last_batch = batch
        try:
            current_ledger = read_provider_ledger(root)
            attempt_entries = [
                entry
                for entry in current_ledger["entries"]
                if entry.get("attemptId") == attempt_id
            ]
            ordinal_by_id = {
                entry["reservationId"]: ordinal
                for ordinal, entry in enumerate(attempt_entries, start=1)
            }
            turn_ordinals = tuple(
                ordinal_by_id[item] for item in batch.reservation_ids
            )
            prior_resource_count = sum(
                entry["phase"] == "provider-resource"
                and entry["sequence"] < batch.reservation_sequences[0]
                for entry in attempt_entries
            )
            profile_ordinal = (
                prior_resource_count // _PROVIDER_BATCH_SIZES[phase][0] + 1
                if phase == "provider-resource"
                else None
            )
            batch_id = f"batch-{batch.reservation_sequences[0]}-{secrets.token_hex(12)}"
            descriptor_relative = (
                f"{PROVIDER_ARTIFACT_ROOTS[gate_id]}/provider-runtime/"
                f"{gate_id}/{batch_id}.descriptor.json"
            )
            observation_relative = (
                f"{PROVIDER_ARTIFACT_ROOTS[gate_id]}/provider-runtime/"
                f"{gate_id}/{batch_id}.observation.json"
            )
            launch_receipt_relative = (
                f"{PROVIDER_ARTIFACT_ROOTS[gate_id]}/provider-runtime/"
                f"{gate_id}/{batch_id}.package-launch.json"
            )
            staging_relative_root = (
                "artifacts/week84-92-goal-control/provider-staging/"
                f"{gate_id}/{batch_id}"
            )
            run_token = secrets.token_hex(32)
            run_token_sha256 = hashlib.sha256(run_token.encode("ascii")).hexdigest()
            descriptor = _provider_descriptor(
                root,
                batch=batch,
                batch_id=batch_id,
                package_binding=package_binding,
                boundary_binding=boundary_binding,
                scenario_source=scenario_source,
                driver_source=driver_source,
                launch_receipt_path=launch_receipt_relative,
                staging_relative_root=staging_relative_root,
                run_token_sha256=run_token_sha256,
                turn_ordinals=turn_ordinals,
                profile_ordinal=profile_ordinal,
            )
            if frozenset(descriptor) != _PROVIDER_DESCRIPTOR_KEYS:
                raise ExecutorError("PROVIDER_DESCRIPTOR")
            descriptor_raw = _exclusive_write_json(
                control_root,
                descriptor_relative,
                descriptor,
            )
            observation_target = _safe_repo_path(
                control_root,
                observation_relative,
                allow_missing_leaf=True,
            )
            if observation_target.exists() or observation_target.is_symlink():
                raise ExecutorError("PROVIDER_OBSERVATION_EXISTS")
            launch_receipt_target = _safe_repo_path(
                control_root,
                launch_receipt_relative,
                allow_missing_leaf=True,
            )
            if launch_receipt_target.exists() or launch_receipt_target.is_symlink():
                raise ExecutorError("PACKAGE_LAUNCH_RECEIPT_EXISTS")
            command, argv_sha = _provider_policy_command(
                policy,
                descriptor_path=descriptor_relative,
                observation_path=observation_relative,
            )
            checkout_before = _require_checkout(root, expected_head=expected_head)
            started_at = _utc_now()
            batch_environment = dict(child_environment)
            batch_environment["CAICLI_PROVIDER_RUN_TOKEN"] = run_token
            exit_code = _run_child_no_capture(
                command,
                cwd=root,
                environment=batch_environment,
                timeout_seconds=float(timeout_seconds),
            )
            run_token = ""
            batch_environment.pop("CAICLI_PROVIDER_RUN_TOKEN", None)
            observation_raw: bytes | None = None
            observed_requests: list[dict[str, Any]] | None = None
            observation_status = "Missing"
            try:
                observation_raw = _read_fixed_bytes(
                    control_root,
                    observation_relative,
                    limit=MAX_JSON_BYTES,
                )
                observed_requests = _validate_provider_observation(
                    observation_raw,
                    descriptor=descriptor,
                )
                observation_status = "Accepted"
            except ExecutorError as error:
                if error.code != "MISSING_PATH" and observation_raw is not None:
                    observation_status = "Rejected"
                if exit_code == 0:
                    exit_code = 126
            launch_receipt_raw: bytes | None = None
            launch_receipt_binding: dict[str, Any] | None = None
            try:
                launch_receipt_raw = _read_fixed_bytes(
                    control_root,
                    launch_receipt_relative,
                    limit=MAX_JSON_BYTES,
                )
                if observation_raw is None or observed_requests is None:
                    raise ExecutorError("PACKAGE_LAUNCH_OBSERVATION")
                launch_receipt_binding = _validate_package_launch_receipt(
                    launch_receipt_raw,
                    descriptor=descriptor,
                    package=package_binding,
                    boundary=boundary_binding,
                    receipt_path=launch_receipt_relative,
                    observation_path=observation_relative,
                    observation_raw=observation_raw,
                )
            except ExecutorError:
                if exit_code == 0:
                    exit_code = 125
            try:
                post_package = _package_identity_evidence_binding(
                    root,
                    control_root,
                    gate_id=gate_id,
                    product_candidate=product_candidate,
                    evidence_path=package_identity_evidence_path,
                )
                post_boundary = verify_provider_boundary_decision(
                    root,
                    control_root,
                    gate_id=gate_id,
                    product_candidate=product_candidate,
                    package=post_package,
                )
                package_ok = (
                    post_package == package_binding
                    and post_boundary == boundary_binding
                )
            except ExecutorError:
                package_ok = False
            if not package_ok and exit_code == 0:
                exit_code = 123
            try:
                checkout_after = _capture_checkout_snapshot(root)
            except ExecutorError:
                checkout_after = None
            checkout_ok = _checkout_unchanged(
                checkout_before,
                checkout_after,
                expected_head,
            )
            if not checkout_ok and exit_code == 0:
                exit_code = 124
            succeeded = (
                exit_code == 0
                and checkout_ok
                and observation_status == "Accepted"
                and observed_requests is not None
                and launch_receipt_binding is not None
                and package_ok
            )
            finished_at = _timestamp_after(started_at)
            checkout_document = _checkout_document(
                mode=checkout_mode,
                product_candidate=product_candidate,
                expected_head=expected_head,
                before=checkout_before,
                after=checkout_after,
            )
            descriptor_binding = {
                "path": descriptor_relative,
                "sha256": hashlib.sha256(descriptor_raw).hexdigest(),
            }
            observation_binding = {
                "path": observation_relative,
                "sha256": (
                    hashlib.sha256(observation_raw).hexdigest()
                    if observation_raw is not None
                    else None
                ),
                "status": observation_status,
            }
            runtime_events: list[dict[str, Any]] = []
            for ordinal, (reservation_id, reservation_sequence) in enumerate(
                zip(
                    batch.reservation_ids,
                    batch.reservation_sequences,
                    strict=True,
                ),
                start=1,
            ):
                request = (
                    observed_requests[ordinal - 1]
                    if observed_requests is not None
                    else None
                )
                runtime_events.append(
                    {
                        "eventType": "ObservedProviderRequest",
                        "reservationId": reservation_id,
                        "reservationSequence": reservation_sequence,
                        "gateId": gate_id,
                        "phase": phase,
                        "productCandidate": product_candidate,
                        "attemptId": attempt_id,
                        "runId": run_id,
                        "batchId": batch_id,
                        "batchSize": len(batch.reservation_ids),
                        "commandPolicy": {
                            "policyId": TRUSTED_PROVIDER_POLICY_ID,
                            "sha256": policy_sha,
                        },
                        "argvSha256": argv_sha,
                        "harnessSource": harness_source,
                        "driverSource": driver_source,
                        "descriptor": descriptor_binding,
                        "observation": observation_binding,
                        "providerBoundaryDecision": (
                            boundary_binding.descriptor_binding()
                        ),
                        "packageIdentityEvidence": (
                            package_binding.descriptor_binding()
                        ),
                        "packageLaunchReceipt": launch_receipt_binding,
                        "observedRequest": request,
                        "checkoutIdentity": checkout_document,
                        "startedAt": started_at,
                        "finishedAt": finished_at,
                        "exitCode": exit_code,
                        "outcome": "Succeeded" if succeeded else "Failed",
                    }
                )
            with _FileLock(control_root):
                locked_ledger = _load_ledger_locked(control_root)
                matching = [
                    entry
                    for entry in locked_ledger["entries"]
                    if entry.get("reservationId") in batch.reservation_ids
                ]
                _attempts, completions, _finished = _attempt_views(locked_ledger)
                if (
                    tuple(item["reservationId"] for item in matching)
                    != batch.reservation_ids
                    or any(item in completions for item in batch.reservation_ids)
                ):
                    raise ExecutorError("RUNTIME_RESERVATION_BINDING")
                _append_runtime_events_locked(control_root, runtime_events)
            ledger = complete_provider_segment(
                root,
                batch,
                child_succeeded=succeeded,
                finish_success=finish_success and final_batch and succeeded,
            )
            if not succeeded:
                break
        except ExecutorError:
            try:
                complete_provider_segment(root, batch, child_succeeded=False)
            except ExecutorError:
                pass
            raise
    if last_batch is None or ledger is None:
        raise ExecutorError("SEGMENT_PHASES")
    return ProviderRunResult(
        exit_code=exit_code,
        batch=last_batch,
        attempt_finished=exit_code != 0 or finish_success,
        used_turns=ledger["usedTurns"],
        remaining_turns=ledger["remainingTurns"],
    )


def _contains_secret(text: str) -> bool:
    if any(pattern.search(text) is not None for pattern in _SECRET_PATTERNS):
        return True
    for key, value in os.environ.items():
        if (
            _SENSITIVE_ENV_NAME.search(key) is not None
            and len(value) >= 8
            and value in text
        ):
            return True
    return False


def _validate_counts(test_counts: Mapping[str, Any]) -> dict[str, int]:
    if not isinstance(test_counts, Mapping) or set(test_counts) != set(_COUNT_KEYS):
        raise ExecutorError("TEST_COUNTS")
    result = {key: test_counts.get(key) for key in _COUNT_KEYS}
    if any(not _is_int(value) or value < 0 for value in result.values()):
        raise ExecutorError("TEST_COUNTS")
    if result["discovered"] != sum(
        result[key] for key in _COUNT_KEYS if key != "discovered"
    ):
        raise ExecutorError("TEST_COUNTS")
    return result  # type: ignore[return-value]


def _bounded_child_capture(
    command: Sequence[str],
    *,
    cwd: Path,
    environment: Mapping[str, str],
    timeout_seconds: float,
    stdin_bytes: bytes = b"",
) -> tuple[int, bytes, bytes, bool]:
    try:
        process, windows_job = _start_contained_child(
            command,
            cwd=cwd,
            environment=environment,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            stdin_bytes=stdin_bytes,
        )
    except OSError:
        return 127, b"", b"", False
    except ExecutorError:
        return 127, b"", b"", True
    assert process.stdout is not None and process.stderr is not None
    buffers = [bytearray(), bytearray()]
    overflow = threading.Event()

    def reader(stream: BinaryIO, output: bytearray) -> None:
        try:
            while True:
                block = stream.read(65536)
                if not block:
                    return
                remaining = MAX_TEST_STREAM_BYTES - len(output)
                if remaining > 0:
                    output.extend(block[:remaining])
                if len(block) > remaining:
                    overflow.set()
                    return
        except OSError:
            overflow.set()

    threads = [
        threading.Thread(target=reader, args=(process.stdout, buffers[0]), daemon=True),
        threading.Thread(target=reader, args=(process.stderr, buffers[1]), daemon=True),
    ]
    for thread in threads:
        thread.start()
    deadline = time.monotonic() + timeout_seconds
    timed_out = False
    while process.poll() is None:
        if overflow.is_set() or time.monotonic() >= deadline:
            timed_out = time.monotonic() >= deadline
            _terminate_child_tree(process, windows_job)
            break
        time.sleep(0.01)
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        _terminate_child_tree(process, windows_job)
        process.wait()
    _release_child_tree(process, windows_job)
    for thread in threads:
        thread.join(timeout=5)
    alive = any(thread.is_alive() for thread in threads)
    try:
        process.stdout.close()
        process.stderr.close()
    except OSError:
        pass
    if alive:
        for thread in threads:
            thread.join(timeout=1)
    rejected = overflow.is_set() or timed_out or alive
    return int(process.returncode), bytes(buffers[0]), bytes(buffers[1]), rejected


def _safe_redacted_invocation(value: Any) -> str:
    if (
        not isinstance(value, str)
        or not value
        or len(value.encode("utf-8")) > 16 * 1024
        or _contains_secret(value)
    ):
        raise ExecutorError("REDACTED_INVOCATION")
    return value


def _failure_counts(original: Mapping[str, int]) -> dict[str, int]:
    discovered = max(original["discovered"], 1)
    return {
        "discovered": discovered,
        "passed": 0,
        "failed": 1,
        "skipped": 0,
        "notRun": discovered - 1,
        "notApplicable": 0,
    }


_COUNT_MARKER = "CAICLI_TEST_COUNTS="
_UNITTEST_RAN = re.compile(r"(?m)^Ran ([0-9]+) tests? in [^\r\n]+$")
_UNITTEST_OK = re.compile(r"^OK(?: \(([^\r\n]*)\))?$")


def _derive_test_counts(
    stdout_text: str, exit_code: int
) -> tuple[dict[str, int], str]:
    """Derive counts from the captured bytes, never from caller assertions."""

    lines = stdout_text.splitlines()
    marker_indexes = [
        index for index, line in enumerate(lines) if line.startswith(_COUNT_MARKER)
    ]
    if marker_indexes:
        if marker_indexes != [len(lines) - 1]:
            raise ExecutorError("TEST_COUNTS_UNPARSEABLE")
        encoded = lines[-1][len(_COUNT_MARKER) :]
        try:
            payload = json.loads(encoded, object_pairs_hook=_pairs_without_duplicates)
        except (json.JSONDecodeError, _DuplicateKeyError):
            raise ExecutorError("TEST_COUNTS_UNPARSEABLE") from None
        derived = _validate_counts(payload)
        if canonical_json_bytes(derived).decode("utf-8") != encoded:
            raise ExecutorError("TEST_COUNTS_UNPARSEABLE")
        if (exit_code == 0) != (
            derived["failed"] == 0 and derived["notRun"] == 0
        ):
            raise ExecutorError("TEST_COUNTS_OUTCOME")
        return derived, "caicli-test-counts-marker-v1"

    normalised_stdout = stdout_text.replace("\r\n", "\n").replace("\r", "\n")
    matches = list(_UNITTEST_RAN.finditer(normalised_stdout))
    if len(matches) != 1:
        raise ExecutorError("TEST_COUNTS_UNPARSEABLE")
    discovered = int(matches[0].group(1))
    trailing = [
        line.strip()
        for line in normalised_stdout[matches[0].end() :].splitlines()
    ]
    trailing = [line for line in trailing if line]
    if not trailing:
        raise ExecutorError("TEST_COUNTS_UNPARSEABLE")
    ok = _UNITTEST_OK.fullmatch(trailing[-1])
    if ok is None or exit_code != 0:
        raise ExecutorError("TEST_COUNTS_OUTCOME")
    skipped = 0
    summary = ok.group(1)
    if summary:
        values: dict[str, int] = {}
        for component in summary.split(", "):
            if "=" not in component:
                raise ExecutorError("TEST_COUNTS_UNPARSEABLE")
            key, raw_value = component.split("=", 1)
            if key in values or not raw_value.isdigit():
                raise ExecutorError("TEST_COUNTS_UNPARSEABLE")
            values[key] = int(raw_value)
        if set(values) - {"skipped", "expected failures"}:
            raise ExecutorError("TEST_COUNTS_UNPARSEABLE")
        skipped = values.get("skipped", 0)
    if skipped > discovered:
        raise ExecutorError("TEST_COUNTS_UNPARSEABLE")
    return (
        {
            "discovered": discovered,
            "passed": discovered - skipped,
            "failed": 0,
            "skipped": skipped,
            "notRun": 0,
            "notApplicable": 0,
        },
        "python-unittest-output",
    )


def _derive_product_result(
    stdout_text: str,
    exit_code: int,
) -> dict[str, int]:
    lines = stdout_text.replace("\r\n", "\n").replace("\r", "\n").splitlines()
    indexes = [
        index
        for index, line in enumerate(lines)
        if line.startswith(PRODUCT_COMMAND_RESULT_MARKER)
    ]
    if indexes != [len(lines) - 1]:
        raise ExecutorError("PRODUCT_RESULT_MISSING")
    encoded = lines[-1][len(PRODUCT_COMMAND_RESULT_MARKER) :]
    try:
        document = json.loads(encoded, object_pairs_hook=_pairs_without_duplicates)
    except (json.JSONDecodeError, _DuplicateKeyError):
        raise ExecutorError("PRODUCT_RESULT_JSON") from None
    counts = document.get("counts") if isinstance(document, dict) else None
    if (
        not isinstance(document, dict)
        or frozenset(document) != {"protocol", "status", "counts"}
        or document.get("protocol") != PRODUCT_COMMAND_RESULT_PROTOCOL
        or document.get("status") not in {"Passed", "Failed"}
        or not isinstance(counts, dict)
        or frozenset(counts) != _PRODUCT_COUNT_KEYS
        or any(not _is_int(value) or value < 0 for value in counts.values())
        or counts["discovered"]
        != counts["passed"] + counts["failed"] + counts["skipped"]
        or (document["status"] == "Passed")
        != (exit_code == 0 and counts["failed"] == 0)
        or canonical_json_bytes(document).decode("utf-8") != encoded
    ):
        raise ExecutorError("PRODUCT_RESULT_BINDING")
    return {key: counts[key] for key in sorted(_PRODUCT_COUNT_KEYS)}


def _derive_verifier_result(
    stdout_text: str,
    exit_code: int,
) -> tuple[dict[str, int], list[dict[str, str]], str]:
    lines = stdout_text.replace("\r\n", "\n").replace("\r", "\n").splitlines()
    indexes = [
        index
        for index, line in enumerate(lines)
        if line.startswith(COMMAND_VERIFIER_RESULT_MARKER)
    ]
    if indexes != [len(lines) - 1]:
        raise ExecutorError("VERIFIER_RESULT_MISSING")
    encoded = lines[-1][len(COMMAND_VERIFIER_RESULT_MARKER) :]
    try:
        document = json.loads(encoded, object_pairs_hook=_pairs_without_duplicates)
    except (json.JSONDecodeError, _DuplicateKeyError):
        raise ExecutorError("VERIFIER_RESULT_JSON") from None
    counts = document.get("counts") if isinstance(document, dict) else None
    executed = (
        document.get("executedCases") if isinstance(document, dict) else None
    )
    if (
        not isinstance(document, dict)
        or frozenset(document) != {"protocol", "status", "executedCases", "counts"}
        or document.get("protocol") != COMMAND_VERIFIER_RESULT_PROTOCOL
        or document.get("status") not in {"Passed", "Failed"}
        or not isinstance(counts, dict)
        or frozenset(counts) != _PRODUCT_COUNT_KEYS
        or any(not _is_int(value) or value < 0 for value in counts.values())
        or counts["discovered"]
        != counts["passed"] + counts["failed"] + counts["skipped"]
        or not isinstance(executed, list)
        or any(
            not isinstance(item, dict)
            or frozenset(item) != {"caseId", "status"}
            or not isinstance(item.get("caseId"), str)
            or not item["caseId"]
            or item.get("status") not in {"Passed", "Failed", "Skipped"}
            for item in executed
        )
        or len({item["caseId"] for item in executed}) != len(executed)
        or counts["discovered"] != len(executed)
        or counts["passed"]
        != sum(item["status"] == "Passed" for item in executed)
        or counts["failed"]
        != sum(item["status"] == "Failed" for item in executed)
        or counts["skipped"]
        != sum(item["status"] == "Skipped" for item in executed)
        or (document["status"] == "Passed")
        != (
            exit_code == 0
            and counts["failed"] == 0
            and counts["skipped"] == 0
        )
        or canonical_json_bytes(document).decode("utf-8") != encoded
    ):
        raise ExecutorError("VERIFIER_RESULT_BINDING")
    return (
        {key: counts[key] for key in sorted(_PRODUCT_COUNT_KEYS)},
        [dict(item) for item in executed],
        str(document["status"]),
    )


def _length_prefixed_sha256(blocks: Sequence[bytes]) -> str:
    digest = hashlib.sha256()
    for block in blocks:
        digest.update(len(block).to_bytes(8, "big"))
        digest.update(block)
    return digest.hexdigest()


def run_product_command(
    repo_root: str | os.PathLike[str],
    *,
    gate_id: str,
    product_candidate: str,
    command_id: str,
    attempt_id: str,
    report_path: str,
    selection_path: str,
    execution_path: str,
    timeout_seconds: float = DEFAULT_CHILD_TIMEOUT_SECONDS,
) -> ProductRunResult:
    """Observe one sealed adapter, then require independent sealed verifiers."""

    root = _normalise_repo_root(repo_root)
    _verify_candidate_commit(root, product_candidate)
    manifest, manifest_binding, runner_binding = _bootstrap_control_preflight(
        root,
        product_candidate=product_candidate,
    )
    repository_context = _gate_lane_context(root, manifest, gate_id)
    evidence_root = repository_context.control_root
    _git_policy, git_policy_sha, git_tool_bundle_before = _trusted_git_policy(
        root, manifest
    )
    if (
        not isinstance(command_id, str)
        or _COMMAND_SLUG.fullmatch(command_id) is None
        or command_id == TRUSTED_TEST_COMMAND_ID
    ):
        raise ExecutorError("PRODUCT_COMMAND_ID")
    if (
        not isinstance(attempt_id, str)
        or _SAFE_ATTEMPT_ID.fullmatch(attempt_id) is None
    ):
        raise ExecutorError("REPORT_ATTEMPT_ID")
    if not isinstance(timeout_seconds, (int, float)) or not (0 < timeout_seconds <= 86400):
        raise ExecutorError("CHILD_TIMEOUT")
    expected_head, checkout_mode, checkout_before = _gate_checkout_preflight(
        root,
        gate_id=gate_id,
        product_candidate=product_candidate,
    )
    matching_requirements = [
        item for item in manifest["gates"] if item.get("gateId") == gate_id
    ]
    if len(matching_requirements) != 1:
        raise ExecutorError("GATE_NOT_IN_REQUIREMENTS")
    requirement = matching_requirements[0]
    required_commands = requirement.get("requiredCommandIds")
    required_basenames = requirement.get("requiredEvidenceBasenames", [])
    result_path = requirement.get("resultPath")
    if (
        not isinstance(required_commands, list)
        or required_commands.count(command_id) != 1
        or not isinstance(required_basenames, list)
        or any(not isinstance(item, str) for item in required_basenames)
        or not isinstance(result_path, str)
    ):
        raise ExecutorError("COMMAND_NOT_IN_REQUIREMENTS")
    control = _command_control_preflight(
        root,
        manifest=manifest,
        gate_id=gate_id,
        product_candidate=product_candidate,
        command_id=command_id,
    )
    command_sources = list(control.sources)
    adapters = [item for item in command_sources if item.get("role") == "adapter"]
    verifiers = [
        item
        for item in command_sources
        if item.get("role") in {"oracle", "test"}
        and command_id in item["verification"]["verifiesCommandIds"]
    ]
    if len(adapters) != 1 or not verifiers:
        raise ExecutorError("COMMAND_ROLE_POLICY")
    adapter = adapters[0]
    script_source = dict(adapter["_binding"])
    script_path = script_source["path"]
    policy, policy_sha = _trusted_product_policy(root, manifest)
    command, argv_sha = _product_policy_command(
        policy,
        script_path=script_path,
        script_sha256=script_source["sha256"],
    )
    redacted_template = policy["redactedInvocationTemplate"]
    redacted = _safe_redacted_invocation(
        redacted_template.replace("{commandId}", command_id)
    )
    report_path, stdout_relative, stderr_relative = _gate_local_report_paths(
        gate_id=gate_id,
        result_path=result_path,
        report_path=report_path,
        expected_basename=f"{command_id}.{attempt_id}.adapter-observation-report.json",
        reserved_basenames=required_basenames,
    )
    selection_path = _gate_local_manifest_path(
        gate_id=gate_id,
        result_path=result_path,
        manifest_path=selection_path,
        expected_basename=f"{command_id}.{attempt_id}.selection.json",
        reserved_basenames=required_basenames,
    )
    execution_path = _gate_local_manifest_path(
        gate_id=gate_id,
        result_path=result_path,
        manifest_path=execution_path,
        expected_basename=f"{command_id}.{attempt_id}.execution.json",
        reserved_basenames=required_basenames,
    )
    verifier_paths: list[tuple[str, str, str]] = []
    for ordinal, verifier in enumerate(verifiers, start=1):
        role = str(verifier["role"])
        basename = (
            f"{command_id}.{attempt_id}.verifier-{ordinal:02d}-{role}-"
            "verification-report.json"
        )
        verifier_paths.append(
            _gate_local_report_paths(
                gate_id=gate_id,
                result_path=result_path,
                report_path=(
                    f"{PurePosixPath(report_path).parent.as_posix()}/{basename}"
                ),
                expected_basename=basename,
                reserved_basenames=required_basenames,
            )
        )
    all_relatives = [
        report_path,
        stdout_relative,
        stderr_relative,
        selection_path,
        execution_path,
        *[path for group in verifier_paths for path in group],
    ]
    if len(set(all_relatives)) != len(all_relatives):
        raise ExecutorError("REPORT_PATH")
    opened: dict[str, tuple[Path, BinaryIO]] = {}
    try:
        for relative in all_relatives:
            opened[relative] = _exclusive_open(evidence_root, relative)
    except ExecutorError:
        for path, handle in opened.values():
            handle.close()
            try:
                path.unlink()
            except OSError:
                pass
        raise
    def seal(relative: str, raw: bytes) -> None:
        path, handle = opened[relative]
        _seal_exclusive_artifact(path, handle, raw)

    def cleanup_empty() -> None:
        for path, handle in opened.values():
            if handle.closed:
                continue
            try:
                identity = _verify_open_artifact_identity(
                    path, handle, expected_size=0
                )
                del identity
                handle.close()
                path.unlink()
            except (ExecutorError, OSError):
                try:
                    handle.close()
                except OSError:
                    pass

    try:
        tool_identity = _python_tool_identity()
        if frozenset(tool_identity) != _TOOL_IDENTITY_KEYS:
            raise ExecutorError("PYTHON_RUNTIME_IDENTITY")
        runtime_inputs = _runtime_input_bindings(
            root,
            gate_id=gate_id,
            command_id=command_id,
            control=control,
        )
        _verify_runtime_inputs(root, runtime_inputs)
        verifier_command, verifier_argv_sha = _verifier_policy_command()
        verifier_policy, verifier_policy_sha = _verifier_policy()
        selected_cases: list[dict[str, Any]] = []
        selected_sources: list[dict[str, Any]] = []
        selected_source_paths: set[str] = set()
        for verifier in verifiers:
            binding = verifier["_binding"]
            for argument in verifier["verification"]["arguments"]:
                selected_cases.append(
                    {
                        "caseId": argument,
                        "projectId": None,
                        "sourcePath": binding["path"],
                    }
                )
            if binding["path"] not in selected_source_paths:
                selected_source_paths.add(binding["path"])
                selected_sources.append(
                    {
                        "path": binding["path"],
                        "sha256": binding["sha256"],
                        "gitBlobSha": binding["gitBlobSha"],
                        "sourceTreeContentRootSha256": None,
                    }
                )
        selected_ids = [item["caseId"] for item in selected_cases]
        if not selected_ids or len(set(selected_ids)) != len(selected_ids):
            raise ExecutorError("COMMAND_SELECTION")
        _git_path, git_tool_selection_after = _trusted_git_identity(
            root, rehash=True
        )
        selection_git_identity = _git_tool_identity_record(
            policy_sha256=git_policy_sha,
            before=git_tool_bundle_before,
            after=git_tool_selection_after,
        )
        if not selection_git_identity["matchesBefore"]:
            raise ExecutorError("GIT_TOOL_DRIFT")
        selection = {
            "schemaVersion": SCHEMA_VERSION,
            "protocol": COMMAND_SELECTION_PROTOCOL,
            "goalId": GOAL_ID,
            "gateId": gate_id,
            "commandId": command_id,
            "productCandidate": product_candidate,
            "controlRevision": control.binding["controlRevision"],
            "runnerKind": "python-unittest",
            "selectionOrigin": "exact-control-arguments",
            "verifierArgvSha256": verifier_argv_sha,
            "selectedCases": selected_cases,
            "selectedCaseCount": len(selected_cases),
            "selectedCaseRootSha256": hashlib.sha256(
                canonical_json_bytes(selected_cases)
            ).hexdigest(),
            "selectedSources": selected_sources,
            "selectedSourceCount": len(selected_sources),
            "selectedSourceRootSha256": hashlib.sha256(
                canonical_json_bytes(selected_sources)
            ).hexdigest(),
            "gitToolIdentity": selection_git_identity,
            "createdAt": _utc_now(),
        }
        selection_raw = canonical_json_bytes(selection)
        seal(selection_path, selection_raw)
        selection_summary = {
            "mode": COMMAND_SELECTION_MODE,
            "runnerKind": "python-unittest",
            "discoveryPolicyId": COMMAND_DISCOVERY_POLICY_ID,
            "selectionOrigin": "exact-control-arguments",
            "manifestPath": selection_path,
            "manifestSha256": hashlib.sha256(selection_raw).hexdigest(),
            "selectedCaseCount": selection["selectedCaseCount"],
            "selectedCaseRootSha256": selection["selectedCaseRootSha256"],
            "selectedSourceCount": selection["selectedSourceCount"],
            "selectedSourceRootSha256": selection["selectedSourceRootSha256"],
            "zeroSelectionAllowed": False,
        }
        adapter_dependencies = [
            {
                "role": item["role"],
                **dict(item["_binding"]),
            }
            for item in command_sources
            if item is not adapter
        ]
        adapter_invocation = canonical_json_bytes(
            {
                "protocol": "week84-92-adapter-invocation-v1",
                "sourceDependencies": adapter_dependencies,
                "runtimeInputs": runtime_inputs,
            }
        )
        _git_path, git_tool_adapter_before = _trusted_git_identity(
            root, rehash=True
        )
        started_at = _utc_now()
        exit_code, stdout_raw, stderr_raw, capture_rejected = _bounded_child_capture(
            command,
            cwd=root,
            environment=_test_child_environment(),
            timeout_seconds=float(timeout_seconds),
            stdin_bytes=adapter_invocation,
        )
        try:
            _git_path, git_tool_adapter_after = _trusted_git_identity(
                root, rehash=True
            )
        except ExecutorError:
            git_tool_adapter_after = None
        adapter_git_identity = _git_tool_identity_record(
            policy_sha256=git_policy_sha,
            before=git_tool_adapter_before,
            after=git_tool_adapter_after,
        )
        try:
            checkout_after = _capture_checkout_snapshot(root)
        except ExecutorError:
            checkout_after = None
        checkout_drift = not _checkout_unchanged(
            checkout_before, checkout_after, expected_head
        )
        try:
            stdout_text = stdout_raw.decode("utf-8")
            stderr_text = stderr_raw.decode("utf-8")
        except UnicodeDecodeError:
            stdout_text = ""
            stderr_text = ""
            capture_rejected = True
        if _contains_secret(stdout_text) or _contains_secret(stderr_text):
            capture_rejected = True
        rejection_code: str | None = None
        if capture_rejected:
            stdout_raw = b"trusted executor rejected captured output\n"
            stderr_raw = b""
            exit_code = 125
            rejection_code = "PRODUCT_CAPTURE_REJECTED"
        elif not adapter_git_identity["matchesBefore"]:
            rejection_code = "GIT_TOOL_DRIFT"
        elif checkout_drift:
            rejection_code = "CHECKOUT_DRIFT"
        elif re.search(
            r"CAICLI_[A-Z0-9_]*(?:RESULT|COUNTS)[A-Z0-9_]*=",
            stdout_text + "\n" + stderr_text,
        ):
            rejection_code = "ADAPTER_SELF_ATTESTATION"
        elif exit_code != 0:
            rejection_code = "ADAPTER_EXIT"
        _verify_runtime_inputs(root, runtime_inputs)
        finished_at = _timestamp_after(started_at)
        checkout_document = _checkout_document(
            mode=checkout_mode,
            product_candidate=product_candidate,
            expected_head=expected_head,
            before=checkout_before,
            after=checkout_after,
        )
        adapter_report = {
            "schemaVersion": SCHEMA_VERSION,
            "runnerId": RUNNER_ID,
            "runnerSource": runner_binding,
            "requirementsSource": manifest_binding,
            "gateId": gate_id,
            "productCandidate": product_candidate,
            "commandId": command_id,
            "attemptId": attempt_id,
            "commandRole": "adapter",
            "commandControlBinding": control.binding,
            "commandPolicy": {
                "policyId": TRUSTED_PRODUCT_POLICY_ID,
                "sha256": policy_sha,
            },
            "argvSha256": argv_sha,
            "scriptSource": script_source,
            "checkoutIdentity": checkout_document,
            "toolIdentity": tool_identity,
            "gitToolIdentity": adapter_git_identity,
            "executionCapability": ADAPTER_EXECUTION_CAPABILITY,
            "runtimeInputs": runtime_inputs,
            "redactedInvocation": redacted,
            "invocationSha256": hashlib.sha256(redacted.encode("utf-8")).hexdigest(),
            "startedAt": started_at,
            "finishedAt": finished_at,
            "exitCode": exit_code,
            "stdout": {
                "path": stdout_relative,
                "sha256": hashlib.sha256(stdout_raw).hexdigest(),
            },
            "stderr": {
                "path": stderr_relative,
                "sha256": hashlib.sha256(stderr_raw).hexdigest(),
            },
            "resultProtocol": None,
            "counts": {key: 0 for key in sorted(_PRODUCT_COUNT_KEYS)},
            "countsSource": ADAPTER_COUNTS_SOURCE,
            "verifiedAdapterReports": [],
            "verificationDescriptorSha256": None,
        }
        adapter_report_raw = canonical_json_bytes(adapter_report)
        seal(stdout_relative, stdout_raw)
        seal(stderr_relative, stderr_raw)
        seal(report_path, adapter_report_raw)
        adapter_summary = {
            "path": report_path,
            "sha256": hashlib.sha256(adapter_report_raw).hexdigest(),
            "commandId": command_id,
            "exitCode": exit_code,
            "checkoutIdentitySha256": hashlib.sha256(
                canonical_json_bytes(checkout_document)
            ).hexdigest(),
            "stdoutSha256": adapter_report["stdout"]["sha256"],
            "stderrSha256": adapter_report["stderr"]["sha256"],
        }
        if rejection_code is not None:
            raise ExecutorError(rejection_code)

        verifier_records: list[dict[str, Any]] = []
        for ordinal, (verifier, paths) in enumerate(
            zip(verifiers, verifier_paths, strict=True), start=1
        ):
            verifier_binding = dict(verifier["_binding"])
            dependencies = [
                {"role": item["role"], **dict(item["_binding"])}
                for item in command_sources
                if item is not verifier
            ]
            _git_path, git_tool_verifier_before = _trusted_git_identity(
                root, rehash=True
            )
            descriptor = {
                "schemaVersion": SCHEMA_VERSION,
                "protocol": COMMAND_VERIFIER_DESCRIPTOR_PROTOCOL,
                "goalId": GOAL_ID,
                "gateId": gate_id,
                "productCandidate": product_candidate,
                "attemptId": attempt_id,
                "evidenceRoot": str(evidence_root),
                "commandRole": verifier["role"],
                "commandControlBinding": control.binding,
                "verifierSource": verifier_binding,
                "sourceDependencies": dependencies,
                "sourceTrees": [],
                "arguments": list(verifier["verification"]["arguments"]),
                "verifiesCommandIds": list(
                    verifier["verification"]["verifiesCommandIds"]
                ),
                "adapterReports": [adapter_summary],
                "checkoutIdentitySha256": adapter_summary[
                    "checkoutIdentitySha256"
                ],
                "toolIdentity": tool_identity,
                "gitToolIdentity": selection_git_identity,
                "selectedDiscovery": selection_summary,
                "runtimeInputs": runtime_inputs,
            }
            descriptor_raw = canonical_json_bytes(descriptor)
            verifier_before = _capture_checkout_snapshot(root)
            verifier_started = _timestamp_after(finished_at)
            child_exit, child_stdout, child_stderr, child_rejected = (
                _bounded_child_capture(
                    verifier_command,
                    cwd=root,
                    environment=_test_child_environment(),
                    timeout_seconds=float(timeout_seconds),
                    stdin_bytes=descriptor_raw,
                )
            )
            try:
                _git_path, git_tool_verifier_after = _trusted_git_identity(
                    root, rehash=True
                )
            except ExecutorError:
                git_tool_verifier_after = None
            verifier_git_identity = _git_tool_identity_record(
                policy_sha256=git_policy_sha,
                before=git_tool_verifier_before,
                after=git_tool_verifier_after,
            )
            try:
                verifier_after = _capture_checkout_snapshot(root)
            except ExecutorError:
                verifier_after = None
            record_error: str | None = None
            if not verifier_git_identity["matchesBefore"]:
                record_error = "GIT_TOOL_DRIFT"
            elif not _checkout_unchanged(
                verifier_before, verifier_after, expected_head
            ):
                record_error = "CHECKOUT_DRIFT"
            try:
                child_stdout_text = child_stdout.decode("utf-8")
                child_stderr_text = child_stderr.decode("utf-8")
            except UnicodeDecodeError:
                child_stdout_text = ""
                child_stderr_text = ""
                child_rejected = True
            if (
                _contains_secret(child_stdout_text)
                or _contains_secret(child_stderr_text)
            ):
                child_rejected = True
            if child_rejected:
                child_stdout = b"trusted executor rejected captured output\n"
                child_stderr = b""
                child_exit = 125
                record_error = "VERIFIER_CAPTURE_REJECTED"
            counts = {key: 0 for key in sorted(_PRODUCT_COUNT_KEYS)}
            executed_cases: list[dict[str, str]] = []
            status = "Failed"
            if record_error is None:
                try:
                    counts, executed_cases, status = _derive_verifier_result(
                        child_stdout_text, child_exit
                    )
                except ExecutorError as error:
                    record_error = error.code
            verifier_finished = _timestamp_after(verifier_started)
            verifier_records.append(
                {
                    "ordinal": ordinal,
                    "source": verifier_binding,
                    "role": verifier["role"],
                    "descriptorRaw": descriptor_raw,
                    "arguments": list(verifier["verification"]["arguments"]),
                    "exitCode": child_exit,
                    "stdout": child_stdout,
                    "stderr": child_stderr,
                    "counts": counts,
                    "executedCases": executed_cases,
                    "status": status,
                    "error": record_error,
                    "startedAt": verifier_started,
                    "finishedAt": verifier_finished,
                    "checkoutIdentity": _checkout_document(
                        mode=checkout_mode,
                        product_candidate=product_candidate,
                        expected_head=expected_head,
                        before=verifier_before,
                        after=verifier_after,
                    ),
                    "gitToolIdentity": verifier_git_identity,
                    "paths": paths,
                }
            )
            _verify_runtime_inputs(root, runtime_inputs)
        _command_control_preflight(
            root,
            manifest=manifest,
            gate_id=gate_id,
            product_candidate=product_candidate,
            command_id=command_id,
        )
        try:
            _git_path, git_tool_bundle_after = _trusted_git_identity(
                root, rehash=True
            )
        except ExecutorError:
            git_tool_bundle_after = None
        execution_git_identity = _git_tool_identity_record(
            policy_sha256=git_policy_sha,
            before=git_tool_bundle_before,
            after=git_tool_bundle_after,
        )
        executed_cases = [
            item
            for record in verifier_records
            for item in record["executedCases"]
        ]
        executed_ids = [item["caseId"] for item in executed_cases]
        matches_selection = executed_ids == selected_ids
        passed = sum(item["status"] == "Passed" for item in executed_cases)
        failed = sum(item["status"] == "Failed" for item in executed_cases)
        skipped = sum(item["status"] == "Skipped" for item in executed_cases)
        execution_exit = (
            0
            if matches_selection
            and passed == len(selected_ids)
            and failed == 0
            and skipped == 0
            and execution_git_identity["matchesBefore"]
            and all(
                record["exitCode"] == 0
                and record["status"] == "Passed"
                and record["error"] is None
                for record in verifier_records
            )
            else 1
        )
        execution = {
            "schemaVersion": SCHEMA_VERSION,
            "protocol": COMMAND_EXECUTION_PROTOCOL,
            "goalId": GOAL_ID,
            "gateId": gate_id,
            "commandId": command_id,
            "productCandidate": product_candidate,
            "controlRevision": control.binding["controlRevision"],
            "runnerKind": "python-unittest",
            "selectionManifest": {
                "path": selection_path,
                "sha256": hashlib.sha256(selection_raw).hexdigest(),
            },
            "startedAt": verifier_records[0]["startedAt"],
            "completedAt": verifier_records[-1]["finishedAt"],
            "exitCode": execution_exit,
            "executedCases": executed_cases,
            "executedCaseCount": len(executed_cases),
            "passed": passed,
            "failed": failed,
            "skipped": skipped,
            "executedCaseRootSha256": hashlib.sha256(
                canonical_json_bytes(executed_cases)
            ).hexdigest(),
            "stdoutSha256": _length_prefixed_sha256(
                [record["stdout"] for record in verifier_records]
            ),
            "stderrSha256": _length_prefixed_sha256(
                [record["stderr"] for record in verifier_records]
            ),
            "gitToolIdentity": execution_git_identity,
        }
        execution_raw = canonical_json_bytes(execution)
        seal(execution_path, execution_raw)
        reconciliation = {
            "manifestPath": execution_path,
            "manifestSha256": hashlib.sha256(execution_raw).hexdigest(),
            "executedCaseCount": execution["executedCaseCount"],
            "executedCaseRootSha256": execution[
                "executedCaseRootSha256"
            ],
            "matchesSelection": matches_selection,
        }
        verifier_report_relatives: list[str] = []
        verifier_report_sha256s: list[str] = []
        for record in verifier_records:
            verifier_report_path, verifier_stdout_path, verifier_stderr_path = record[
                "paths"
            ]
            verifier_redacted = (
                "python-current -I -S -E -B -X utf8 -c "
                "<week84-92-in-memory-command-verifier-v1>"
            )
            verifier_report = {
                "schemaVersion": SCHEMA_VERSION,
                "runnerId": RUNNER_ID,
                "runnerSource": runner_binding,
                "requirementsSource": manifest_binding,
                "gateId": gate_id,
                "productCandidate": product_candidate,
                "commandId": command_id,
                "attemptId": attempt_id,
                "commandRole": record["role"],
                "commandControlBinding": control.binding,
                "commandPolicy": {
                    "policyId": COMMAND_VERIFIER_POLICY_ID,
                    "sha256": verifier_policy_sha,
                },
                "argvSha256": verifier_argv_sha,
                "scriptSource": record["source"],
                "checkoutIdentity": record["checkoutIdentity"],
                "toolIdentity": tool_identity,
                "gitToolIdentity": record["gitToolIdentity"],
                "executionCapability": ADAPTER_EXECUTION_CAPABILITY,
                "runtimeInputs": runtime_inputs,
                "redactedInvocation": verifier_redacted,
                "invocationSha256": hashlib.sha256(
                    verifier_redacted.encode("utf-8")
                ).hexdigest(),
                "startedAt": record["startedAt"],
                "finishedAt": record["finishedAt"],
                "exitCode": record["exitCode"],
                "stdout": {
                    "path": verifier_stdout_path,
                    "sha256": hashlib.sha256(record["stdout"]).hexdigest(),
                },
                "stderr": {
                    "path": verifier_stderr_path,
                    "sha256": hashlib.sha256(record["stderr"]).hexdigest(),
                },
                "resultProtocol": COMMAND_VERIFIER_RESULT_PROTOCOL,
                "counts": record["counts"],
                "countsSource": "python-unittest-output",
                "verifiedAdapterReports": [adapter_summary],
                "verificationDescriptorSha256": hashlib.sha256(
                    record["descriptorRaw"]
                ).hexdigest(),
                "executionReconciliation": reconciliation,
            }
            verifier_report_raw = canonical_json_bytes(verifier_report)
            seal(verifier_stdout_path, record["stdout"])
            seal(verifier_stderr_path, record["stderr"])
            seal(verifier_report_path, verifier_report_raw)
            verifier_report_relatives.append(verifier_report_path)
            verifier_report_sha256s.append(
                hashlib.sha256(verifier_report_raw).hexdigest()
            )
        if execution_exit != 0:
            fallback_error = (
                "GIT_TOOL_DRIFT"
                if not execution_git_identity["matchesBefore"]
                else "VERIFIER_FAILED"
            )
            first_error = next(
                (
                    record["error"]
                    for record in verifier_records
                    if record["error"] is not None
                ),
                fallback_error,
            )
            raise ExecutorError(first_error)
        return ProductRunResult(
            exit_code=0,
            report_path=report_path,
            stdout_path=stdout_relative,
            stderr_path=stderr_relative,
            report_sha256=hashlib.sha256(adapter_report_raw).hexdigest(),
            verifier_report_paths=tuple(verifier_report_relatives),
            verifier_report_sha256s=tuple(verifier_report_sha256s),
            selection_path=selection_path,
            selection_sha256=hashlib.sha256(selection_raw).hexdigest(),
            execution_path=execution_path,
            execution_sha256=hashlib.sha256(execution_raw).hexdigest(),
        )
    finally:
        cleanup_empty()


def run_test(
    repo_root: str | os.PathLike[str],
    *,
    gate_id: str,
    product_candidate: str,
    command_id: str,
    attempt_id: str,
    argv: Sequence[str],
    redacted_invocation: str,
    report_path: str,
    test_counts: Mapping[str, Any],
    timeout_seconds: float = DEFAULT_CHILD_TIMEOUT_SECONDS,
) -> TestRunResult:
    """Run a manifest-authorised test command and write exclusive provenance."""

    root = _normalise_repo_root(repo_root)
    _verify_candidate_commit(root, product_candidate)
    manifest, manifest_binding, runner_binding = _bootstrap_control_preflight(
        root,
        product_candidate=product_candidate,
    )
    repository_context = _gate_lane_context(root, manifest, gate_id)
    evidence_root = repository_context.control_root
    expected_head, checkout_mode, checkout_before = _gate_checkout_preflight(
        root,
        gate_id=gate_id,
        product_candidate=product_candidate,
    )
    redacted = _safe_redacted_invocation(redacted_invocation)
    counts = _validate_counts(test_counts)
    if (
        not isinstance(attempt_id, str)
        or _SAFE_ATTEMPT_ID.fullmatch(attempt_id) is None
    ):
        raise ExecutorError("REPORT_ATTEMPT_ID")
    if not isinstance(timeout_seconds, (int, float)) or not (0 < timeout_seconds <= 86400):
        raise ExecutorError("CHILD_TIMEOUT")
    matching_requirements = [
        item for item in manifest["gates"] if item.get("gateId") == gate_id
    ]
    if len(matching_requirements) != 1:
        raise ExecutorError("GATE_NOT_IN_REQUIREMENTS")
    requirement = matching_requirements[0]
    _git_policy, git_policy_sha, _git_policy_identity = _trusted_git_policy(
        root, manifest
    )
    command_policy, command_policy_sha = _trusted_test_policy(root, manifest)
    required_commands = requirement.get("requiredCommandIds")
    required_basenames = requirement.get("requiredEvidenceBasenames", [])
    result_path = requirement.get("resultPath")
    if (
        not isinstance(required_commands, list)
        or command_id != TRUSTED_TEST_COMMAND_ID
        or command_policy.get("commandId") != command_id
        or required_commands.count(command_id) != 1
        or not isinstance(required_basenames, list)
        or any(not isinstance(item, str) for item in required_basenames)
        or not isinstance(result_path, str)
    ):
        raise ExecutorError("COMMAND_NOT_IN_REQUIREMENTS")
    if redacted != command_policy.get("redactedInvocation"):
        raise ExecutorError("COMMAND_POLICY_REDACTION")
    command, argv_sha = _validate_policy_command(argv, command_policy)
    semantic_sources = _semantic_source_bindings(
        root,
        product_candidate=product_candidate,
        policy=command_policy,
    )
    semantic_descriptor = {
        "protocol": SEMANTIC_LOADER_PROTOCOL,
        "repoRoot": str(root),
        "entrySources": [
            {
                "moduleName": item["moduleName"],
                "path": item["path"],
                "sha256": item["sha256"],
            }
            for item in semantic_sources["entrySources"]
        ],
        "readSources": [
            {"path": item["path"], "sha256": item["sha256"]}
            for item in semantic_sources["readSources"]
        ],
    }
    semantic_descriptor_raw = canonical_json_bytes(semantic_descriptor)
    python_tool_identity = _python_tool_identity()
    _git_path, git_tool_before = _trusted_git_identity(root, rehash=True)
    site_tree_before, site_entries_before = (
        _semantic_site_dependency_inventory(root)
    )
    report_path, stdout_relative, stderr_relative = _gate_local_report_paths(
        gate_id=gate_id,
        result_path=result_path,
        report_path=report_path,
        expected_basename=(
            f"{TRUSTED_TEST_COMMAND_ID}.{attempt_id}.trusted-test-report.json"
        ),
        reserved_basenames=required_basenames,
    )
    goal_state_raw = _read_fixed_bytes(
        evidence_root, GOAL_STATE_PATH, limit=MAX_JSON_BYTES
    )
    goal_state_document = _read_json_bytes(
        goal_state_raw, "GOAL_STATE_SNAPSHOT_JSON"
    )
    if (
        not isinstance(goal_state_document, dict)
        or canonical_json_bytes(goal_state_document) != goal_state_raw
        or _contains_secret(goal_state_raw.decode("utf-8"))
    ):
        raise ExecutorError("GOAL_STATE_SNAPSHOT_JSON")
    goal_state_snapshot_path = _gate_local_manifest_path(
        gate_id=gate_id,
        result_path=result_path,
        manifest_path=(
            f"{PurePosixPath(report_path).parent.as_posix()}/"
            f"{TRUSTED_TEST_COMMAND_ID}.{attempt_id}.pre-goal-state.json"
        ),
        expected_basename=(
            f"{TRUSTED_TEST_COMMAND_ID}.{attempt_id}.pre-goal-state.json"
        ),
        reserved_basenames=required_basenames,
    )
    post_semantic_paths = _semantic_post_output_paths(
        gate_id=gate_id,
        result_path=result_path,
    )
    post_semantic_outputs = [
        _post_semantic_output_binding(evidence_root, path)
        for path in post_semantic_paths
    ]
    if any(item["before"]["exists"] for item in post_semantic_outputs):
        raise ExecutorError("POST_SEMANTIC_OUTPUT_EXISTS")

    opened: list[tuple[Path, BinaryIO]] = []
    snapshot_file: tuple[Path, BinaryIO] | None = None
    try:
        for relative in (report_path, stdout_relative, stderr_relative):
            opened.append(_exclusive_open(evidence_root, relative))
        snapshot_file = _exclusive_open(evidence_root, goal_state_snapshot_path)
    except ExecutorError:
        if snapshot_file is not None:
            snapshot_file[1].close()
            try:
                snapshot_file[0].unlink()
            except OSError:
                pass
        for path, handle in opened:
            handle.close()
            try:
                path.unlink()
            except OSError:
                pass
        raise
    report_handle = opened[0][1]
    stdout_handle = opened[1][1]
    stderr_handle = opened[2][1]
    try:
        for path, handle in opened:
            _verify_open_artifact_identity(path, handle, expected_size=0)
        _seal_exclusive_artifact(
            snapshot_file[0], snapshot_file[1], goal_state_raw  # type: ignore[index]
        )
    except ExecutorError:
        if snapshot_file is not None and not snapshot_file[1].closed:
            snapshot_file[1].close()
            try:
                snapshot_file[0].unlink()
            except OSError:
                pass
        for _path, handle in opened:
            handle.close()
        raise

    artifact_exclusions = tuple(
        sorted(
            (
                report_path,
                stdout_relative,
                stderr_relative,
                *post_semantic_paths,
            ),
            key=lambda value: value.encode("utf-8"),
        )
    )
    try:
        runtime_roots_before, runtime_tree_before = _artifact_input_tree_inventory(
            evidence_root,
            excluded_paths=artifact_exclusions,
        )
    except ExecutorError:
        for path, handle in opened:
            handle.close()
            try:
                if path.stat().st_size == 0:
                    path.unlink()
            except OSError:
                pass
        raise

    started_at = _utc_now()
    exit_code, stdout_raw, stderr_raw, capture_rejected = _bounded_child_capture(
        command,
        cwd=root,
        environment=_test_child_environment(root),
        timeout_seconds=float(timeout_seconds),
        stdin_bytes=semantic_descriptor_raw,
    )
    try:
        post_semantic_outputs_after = [
            _post_semantic_output_binding(evidence_root, path)
            for path in post_semantic_paths
        ]
    except ExecutorError:
        post_semantic_outputs_after = None
    post_semantic_outputs_unchanged = (
        post_semantic_outputs_after == post_semantic_outputs
    )
    try:
        live_goal_state_after = _read_fixed_bytes(
            evidence_root, GOAL_STATE_PATH, limit=MAX_JSON_BYTES
        )
        snapshot_after = _read_fixed_bytes(
            evidence_root, goal_state_snapshot_path, limit=MAX_JSON_BYTES
        )
    except ExecutorError:
        goal_state_stable_during_child = False
    else:
        goal_state_stable_during_child = (
            live_goal_state_after == goal_state_raw
            and snapshot_after == goal_state_raw
        )
    post_semantic_report_outputs = [
        {
            "path": before["path"],
            "mode": "exclusive-create-after-semantic",
            "before": before["before"],
            "stableDuringChild": (
                after == before
                if post_semantic_outputs_after is not None
                else False
            ),
        }
        for before, after in zip(
            post_semantic_outputs,
            post_semantic_outputs_after or [None] * len(post_semantic_outputs),
            strict=True,
        )
    ]
    try:
        for path, handle in opened:
            _verify_open_artifact_identity(path, handle, expected_size=0)
    except ExecutorError:
        for _path, handle in opened:
            handle.close()
        raise
    site_marker_error: str | None = None
    loaded_site_paths: tuple[str, ...] = ()
    if not capture_rejected:
        try:
            stdout_raw, loaded_site_paths = _extract_semantic_site_marker(stdout_raw)
        except ExecutorError as error:
            site_marker_error = error.code
    try:
        _verify_semantic_source_bindings(root, semantic_sources)
        semantic_source_unchanged = True
    except ExecutorError:
        semantic_source_unchanged = False
    try:
        site_tree_after, site_entries_after = _semantic_site_dependency_inventory(root)
    except ExecutorError:
        site_tree_after = None
        site_entries_after = {}
    site_tree_unchanged = (
        site_tree_after == site_tree_before
        and site_entries_after == site_entries_before
    )
    loaded_site_summary: dict[str, Any] | None = None
    if site_marker_error is None and loaded_site_paths:
        try:
            before_loaded = _semantic_loaded_site_summary(
                loaded_site_paths, site_entries_before
            )
            after_loaded = _semantic_loaded_site_summary(
                loaded_site_paths, site_entries_after
            )
            if before_loaded != after_loaded:
                raise ExecutorError("SEMANTIC_SITE_LOADED_FILE")
            loaded_site_summary = before_loaded
        except ExecutorError as error:
            site_marker_error = error.code
    try:
        _git_path, git_tool_after = _trusted_git_identity(root, rehash=True)
    except ExecutorError:
        git_tool_after = None
    git_tool_unchanged = git_tool_after == git_tool_before
    try:
        runtime_roots_after, runtime_tree_after = _artifact_input_tree_inventory(
            evidence_root,
            excluded_paths=artifact_exclusions,
        )
    except ExecutorError:
        runtime_roots_after = None
        runtime_tree_after = None
    runtime_tree_unchanged = (
        runtime_roots_after == runtime_roots_before
        and runtime_tree_after == runtime_tree_before
    )
    try:
        checkout_after = _capture_checkout_snapshot(root)
    except ExecutorError:
        checkout_after = None
    checkout_drift = not _checkout_unchanged(
        checkout_before,
        checkout_after,
        expected_head,
    )
    try:
        stdout_text = stdout_raw.decode("utf-8")
        stderr_text = stderr_raw.decode("utf-8")
    except UnicodeDecodeError:
        stdout_text = ""
        stderr_text = ""
        capture_rejected = True
    if _contains_secret(stdout_text) or _contains_secret(stderr_text):
        capture_rejected = True
    report_counts = counts
    counts_source = "trusted-executor-rejected"
    rejection_code: str | None = None
    if capture_rejected:
        stdout_raw = b"trusted executor rejected captured output\n"
        stderr_raw = b""
        exit_code = 125
        report_counts = _failure_counts(counts)
        rejection_code = "TEST_CAPTURE_REJECTED"
    elif site_marker_error is not None:
        exit_code = 122
        report_counts = _failure_counts(counts)
        rejection_code = site_marker_error
    elif not semantic_source_unchanged:
        exit_code = 121
        report_counts = _failure_counts(counts)
        rejection_code = "SEMANTIC_SOURCE_DRIFT"
    elif not site_tree_unchanged:
        exit_code = 120
        report_counts = _failure_counts(counts)
        rejection_code = "SEMANTIC_SITE_DEPENDENCY_DRIFT"
    elif not git_tool_unchanged:
        exit_code = 119
        report_counts = _failure_counts(counts)
        rejection_code = "GIT_TOOL_DRIFT"
    elif not goal_state_stable_during_child:
        exit_code = 117
        report_counts = _failure_counts(counts)
        rejection_code = "GOAL_STATE_DRIFT"
    elif not post_semantic_outputs_unchanged:
        exit_code = 118
        report_counts = _failure_counts(counts)
        rejection_code = "POST_SEMANTIC_OUTPUT_DRIFT"
    elif not runtime_tree_unchanged:
        exit_code = 123
        report_counts = _failure_counts(counts)
        rejection_code = "RUNTIME_INPUT_TREE_DRIFT"
    elif checkout_drift:
        exit_code = 124
        report_counts = _failure_counts(counts)
        rejection_code = "CHECKOUT_DRIFT"
    else:
        try:
            derived_counts, counts_source = _derive_test_counts(stdout_text, exit_code)
        except ExecutorError as error:
            rejection_code = error.code
            exit_code = 126
            report_counts = _failure_counts(counts)
        else:
            if counts_source != command_policy.get("countsSource"):
                rejection_code = "TEST_COUNTS_SOURCE"
                exit_code = 126
                report_counts = _failure_counts(counts)
                counts_source = "trusted-executor-rejected"
            elif derived_counts != counts:
                rejection_code = "TEST_COUNTS_MISMATCH"
                exit_code = 126
                report_counts = _failure_counts(counts)
            else:
                report_counts = derived_counts
    finished_at = _timestamp_after(started_at)
    report = {
        "schemaVersion": SCHEMA_VERSION,
        "runnerId": RUNNER_ID,
        "runnerSource": runner_binding,
        "requirementsSource": manifest_binding,
        "gateId": gate_id,
        "productCandidate": product_candidate,
        "commandId": command_id,
        "attemptId": attempt_id,
        "commandPolicy": {
            "policyId": TRUSTED_TEST_POLICY_ID,
            "sha256": command_policy_sha,
        },
        "argvSha256": argv_sha,
        "semanticLoaderDescriptorSha256": hashlib.sha256(
            semantic_descriptor_raw
        ).hexdigest(),
        "semanticSources": semantic_sources,
        "checkoutIdentity": _checkout_document(
            mode=checkout_mode,
            product_candidate=product_candidate,
            expected_head=expected_head,
            before=checkout_before,
            after=checkout_after,
        ),
        "runtimeInputTree": {
            "mode": ARTIFACT_INPUT_TREE_MODE,
            "contentRootAlgorithm": ARTIFACT_INPUT_TREE_ALGORITHM,
            "scopedRoots": runtime_roots_before,
            "excludedPaths": list(artifact_exclusions),
            "preGoalStateSnapshot": {
                "path": goal_state_snapshot_path,
                "bytes": len(goal_state_raw),
                "sha256": hashlib.sha256(goal_state_raw).hexdigest(),
                "stableDuringChild": goal_state_stable_during_child,
            },
            "postSemanticOutputs": post_semantic_report_outputs,
            "before": runtime_tree_before,
            "after": runtime_tree_after,
            "matchesBefore": runtime_tree_unchanged,
        },
        "semanticDependencyTree": {
            "mode": "isolated-s-explicit-purelib-allowlist",
            "rootLocationRole": "sysconfig-purelib-outside-repository",
            "contentRootAlgorithm": SEMANTIC_SITE_TREE_ALGORITHM,
            "allowedRoots": [
                {"path": path, "kind": kind}
                for path, kind in SEMANTIC_SITE_ALLOWED_ROOTS
            ],
            "before": site_tree_before,
            "after": site_tree_after,
            "matchesBefore": site_tree_unchanged,
            "loadedFiles": loaded_site_summary,
        },
        "toolIdentity": python_tool_identity,
        "gitToolIdentity": _git_tool_identity_record(
            policy_sha256=git_policy_sha,
            before=git_tool_before,
            after=git_tool_after,
        ),
        "executionCapability": SEMANTIC_EXECUTION_CAPABILITY,
        "redactedInvocation": redacted,
        "invocationSha256": hashlib.sha256(redacted.encode("utf-8")).hexdigest(),
        "startedAt": started_at,
        "finishedAt": finished_at,
        "exitCode": exit_code,
        "stdout": {
            "path": stdout_relative,
            "sha256": hashlib.sha256(stdout_raw).hexdigest(),
        },
        "stderr": {
            "path": stderr_relative,
            "sha256": hashlib.sha256(stderr_raw).hexdigest(),
        },
        "testCounts": report_counts,
        "countsSource": counts_source,
    }
    report_raw = canonical_json_bytes(report)
    try:
        _seal_exclusive_artifact(opened[1][0], stdout_handle, stdout_raw)
        _seal_exclusive_artifact(opened[2][0], stderr_handle, stderr_raw)
        _seal_exclusive_artifact(opened[0][0], report_handle, report_raw)
    finally:
        for _path, handle in opened:
            handle.close()

    if rejection_code is not None:
        raise ExecutorError(rejection_code)
    return TestRunResult(
        exit_code=exit_code,
        report_path=report_path,
        stdout_path=stdout_relative,
        stderr_path=stderr_relative,
        report_sha256=hashlib.sha256(report_raw).hexdigest(),
        attempt_id=attempt_id,
        test_counts=dict(report_counts),
    )


class _SafeArgumentParser(argparse.ArgumentParser):
    def error(self, _message: str) -> None:
        self.exit(2, "trusted executor: invalid command line\n")


def _build_parser() -> argparse.ArgumentParser:
    parser = _SafeArgumentParser(description="Week84-92 trusted executor")
    parser.add_argument("--repo-root", default=".")
    subparsers = parser.add_subparsers(
        dest="action", required=True, parser_class=_SafeArgumentParser
    )

    provider = subparsers.add_parser("provider-run")
    provider.add_argument("--gate-id", required=True)
    provider.add_argument("--product-candidate", required=True)
    provider.add_argument("--attempt-id", required=True)
    provider.add_argument("--run-id", required=True)
    provider.add_argument("--phase", action="append", required=True)
    provider.add_argument("--package-identity-evidence", required=True)
    provider.add_argument("--finish-success", action="store_true")
    provider.add_argument("--timeout-seconds", type=float, default=DEFAULT_CHILD_TIMEOUT_SECONDS)

    subparsers.add_parser("recover-provider")

    consume = subparsers.add_parser("lease-consume")
    consume.add_argument("--gate-id", required=True)
    consume.add_argument("--product-candidate", required=True)
    consume.add_argument("--lease-id")

    verify = subparsers.add_parser("lease-verify")
    verify.add_argument("--gate-id", required=True)
    verify.add_argument("--product-candidate", required=True)

    test = subparsers.add_parser("run-test")
    test.add_argument("--gate-id", required=True)
    test.add_argument("--product-candidate", required=True)
    test.add_argument("--command-id", required=True)
    test.add_argument("--attempt-id", required=True)
    test.add_argument("--redacted-invocation", required=True)
    test.add_argument("--report-path", required=True)
    for key in _COUNT_KEYS:
        test.add_argument(f"--{key}", type=int, required=True)
    test.add_argument("--timeout-seconds", type=float, default=DEFAULT_CHILD_TIMEOUT_SECONDS)
    test.add_argument("command", nargs=argparse.REMAINDER)

    product = subparsers.add_parser("run-product-command")
    product.add_argument("--gate-id", required=True)
    product.add_argument("--product-candidate", required=True)
    product.add_argument("--command-id", required=True)
    product.add_argument("--attempt-id", required=True)
    product.add_argument("--report-path", required=True)
    product.add_argument("--selection-path", required=True)
    product.add_argument("--execution-path", required=True)
    product.add_argument(
        "--timeout-seconds",
        type=float,
        default=DEFAULT_CHILD_TIMEOUT_SECONDS,
    )
    return parser


def _remainder_command(value: Sequence[str]) -> list[str]:
    command = list(value)
    if command and command[0] == "--":
        command = command[1:]
    return _validate_argv(command)


def main(argv: Sequence[str] | None = None) -> int:
    parser = _build_parser()
    arguments = parser.parse_args(argv)
    try:
        if arguments.action == "provider-run":
            result = run_provider_segment(
                arguments.repo_root,
                gate_id=arguments.gate_id,
                product_candidate=arguments.product_candidate,
                attempt_id=arguments.attempt_id,
                run_id=arguments.run_id,
                phases=arguments.phase,
                package_identity_evidence_path=(
                    arguments.package_identity_evidence
                ),
                finish_success=arguments.finish_success,
                timeout_seconds=arguments.timeout_seconds,
            )
            if result.exit_code != 0:
                print("trusted executor: provider child failed", file=sys.stderr)
                return 1
            print("trusted executor: provider segment recorded")
            return 0
        if arguments.action == "recover-provider":
            recover_open_provider_attempts(arguments.repo_root)
            print("trusted executor: provider recovery recorded")
            return 0
        if arguments.action == "lease-consume":
            consume_controlled_write_lease(
                arguments.repo_root,
                gate_id=arguments.gate_id,
                product_candidate=arguments.product_candidate,
                lease_id=arguments.lease_id,
            )
            print("trusted executor: controlled-write lease consumed")
            return 0
        if arguments.action == "lease-verify":
            verify_controlled_write_tombstone(
                arguments.repo_root,
                gate_id=arguments.gate_id,
                product_candidate=arguments.product_candidate,
            )
            print("trusted executor: controlled-write tombstone verified")
            return 0
        if arguments.action == "run-test":
            counts = {key: getattr(arguments, key) for key in _COUNT_KEYS}
            result = run_test(
                arguments.repo_root,
                gate_id=arguments.gate_id,
                product_candidate=arguments.product_candidate,
                command_id=arguments.command_id,
                attempt_id=arguments.attempt_id,
                argv=_remainder_command(arguments.command),
                redacted_invocation=arguments.redacted_invocation,
                report_path=arguments.report_path,
                test_counts=counts,
                timeout_seconds=arguments.timeout_seconds,
            )
            if result.exit_code != 0:
                print("trusted executor: test child failed", file=sys.stderr)
                return 1
            print("trusted executor: test provenance recorded")
            return 0
        if arguments.action == "run-product-command":
            result = run_product_command(
                arguments.repo_root,
                gate_id=arguments.gate_id,
                product_candidate=arguments.product_candidate,
                command_id=arguments.command_id,
                attempt_id=arguments.attempt_id,
                report_path=arguments.report_path,
                selection_path=arguments.selection_path,
                execution_path=arguments.execution_path,
                timeout_seconds=arguments.timeout_seconds,
            )
            if result.exit_code != 0:
                print("trusted executor: product child failed", file=sys.stderr)
                return 1
            print("trusted executor: product provenance recorded")
            return 0
        raise ExecutorError("ACTION")
    except ExecutorError as error:
        print(str(error), file=sys.stderr)
        return 2
    except KeyboardInterrupt:
        print("trusted executor failure [INTERRUPTED]", file=sys.stderr)
        return 130
    except Exception:
        # CLI diagnostics are deliberately fixed: neither child arguments nor
        # environment/file contents may escape through an unexpected traceback.
        print("trusted executor failure [INTERNAL]", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
