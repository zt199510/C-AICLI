#!/usr/bin/env python3
"""Fail-closed semantic validator for the Week84-92 Goal evidence set.

The three JSON Schemas validate individual document shapes.  This program adds
the cross-document checks that JSON Schema cannot express: the fixed 104-Gate
registry, exact week/lane membership, roll-up arithmetic, provider accounting,
controlled-write limits, handoff readiness, and the real closure of a Complete
Goal.

Provider ledger format
----------------------
There is currently no separate ledger schema.  The validator therefore freezes
the runtime ledger itself.  ``entries`` is an append-only reservation chain with
one ``TurnReserved`` entry per charged provider turn.  ``attemptEvents`` is an
independent append-only chain containing exactly one ``TurnCompleted`` event per
reservation and one ``AttemptFinished`` event per closed attempt.  Reserving a
turn charges the 120-turn cap even when the turn, attempt, or process later
fails.  A Passed provider Gate identifies one final successful attempt; only
that attempt must equal the frozen complete phase layout, while every earlier
failed attempt must be a non-empty prefix of that layout.

Exit status is 0 for a valid evidence set, 1 for validation failures, and 2 for
an unavailable validation engine or invalid command-line/runtime setup.
"""

from __future__ import annotations

import argparse
import ast
from collections import Counter, defaultdict
from contextlib import redirect_stderr, redirect_stdout
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import struct
import subprocess
import sys
import tempfile
from typing import Any, Iterable, Mapping, Sequence
import unicodedata
import zlib

try:
    from tools import week84_92_goal_integrity as goal_integrity
except ImportError:  # direct execution from the tools directory
    import week84_92_goal_integrity as goal_integrity

try:
    from tools import week84_92_evidence_anchor as evidence_anchor
except ImportError:  # direct execution from the tools directory
    import week84_92_evidence_anchor as evidence_anchor

try:
    from tools import week84_92_trusted_executor as trusted_executor
except ImportError:  # direct execution from the tools directory
    import week84_92_trusted_executor as trusted_executor


GOAL_ID = "c-aicli-cli-desktop-refactor-w84-w92"
REGISTRY_VERSION = "w84-w92-gates-v1"
PROVIDER_BUDGET = 120
PROVIDER_SECRET_DISPOSITION = (
    "frozen-counting-gateway-only-redacted-from-output-logs-evidence-and-commits"
)
SCHEMA_VERSION = "1.0.0"
SCHEMA_FILENAMES = {
    "goal": "84_92_week_goal_control.schema.json",
    "gate": "84_92_week_gate_result.schema.json",
    "handoff": "84_92_week_handoff.schema.json",
}
CONTROLLED_WRITE_GATES = {84: "W84-G8", 92: "W92-G7"}
FINAL_HANDOFF_PATH = (
    "artifacts/week92-refactor-acceptance/week92-acceptance-handoff.json"
)
W84_CANONICAL_HANDOFF_PATH = (
    "artifacts/week84-renderer-listener-retention/week84-baseline-handoff.json"
)
W84_COMPAT_HANDOFF_PATH = (
    "artifacts/week84-renderer-listener-retention/week85-refactor-handoff.json"
)
LEDGER_PATH = "artifacts/week84-92-goal-control/provider-turn-ledger.json"
PROVIDER_RUNTIME_JOURNAL_PATH = (
    "artifacts/week84-92-goal-control/provider-runtime-journal.json"
)
PROVIDER_RUNTIME_JOURNAL_VERSION = "week84-92-provider-execution-v1"
PROVIDER_ENV_PATH = ".env.local"
GATE_REQUIREMENTS_PATH = "docs_md/weekly/84_92_week_gate_requirements.json"
GATE_REQUIREMENTS_VERSION = "w84-w92-gate-requirements-v1"
TRUSTED_GIT_POLICY_ID = "week84-92-git-tool-v1"
TRUSTED_GIT_EXECUTABLE_ROLE = "git-frozen-core"
TRUSTED_GIT_VERSION = "2.47.1.windows.2"
TRUSTED_GIT_EXECUTABLE_BYTES = 4_151_144
TRUSTED_GIT_EXECUTABLE_SHA256 = (
    "99aa707073dc30ae0f2259dac777041e69ee90bbb8977174a1832a708b1fbb61"
)
TRUSTED_GIT_WINDOWS_LOCATOR = Path(
    r"C:\Program Files\Git\mingw64\bin\git.exe"
)
# Do not embed a literal digest of GATE_REQUIREMENTS_PATH here.  The manifest
# freezes this validator's source digest, so a reverse validator -> manifest
# digest edge would create an unsatisfiable hash cycle.  Authority flows only
# sources -> manifest -> W84 bootstrap control: Git first-add identity and raw
# worktree/index/commit equality below freeze the manifest bytes.
BOOTSTRAP_CONTROL_PATHS: tuple[str, ...] = (
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
W84_G0_CONTROL_PATH = "docs_md/weekly/84_92_command_control/W84-G0.json"
W84_G0_ADAPTER_PATH_BY_COMMAND: Mapping[str, str] = {
    "goal-contract-unittest": (
        "tools/week84_92_commands/goal-contract-unittest.py"
    ),
    "trusted-executor-unittest": (
        "tools/week84_92_commands/trusted-executor-unittest.py"
    ),
    "goal-control-schema-validate": (
        "tools/week84_92_commands/goal-control-schema-validate.py"
    ),
    "prior-handoff-schema-validate": (
        "tools/week84_92_commands/prior-handoff-schema-validate.py"
    ),
    "week83-lineage-identity-verify": (
        "tools/week84_92_commands/week83-lineage-identity-verify.py"
    ),
}
W84_G0_VERIFIER_PATH = "tools/test_validate_week84_92_goal_evidence.py"
W84_G0_VERIFICATION_ARGUMENT_BY_COMMAND: Mapping[str, str] = {
    "goal-contract-unittest": (
        "tools.test_validate_week84_92_goal_evidence."
        "Week84To92GoalEvidenceTests."
        "test_w84_verifier_goal_contract_runner_targets_exact_full_control_suite"
    ),
    "trusted-executor-unittest": (
        "tools.test_validate_week84_92_goal_evidence."
        "Week84To92GoalEvidenceTests."
        "test_w84_verifier_trusted_executor_runner_targets_exact_suite"
    ),
    "goal-control-schema-validate": (
        "tools.test_validate_week84_92_goal_evidence."
        "Week84To92GoalEvidenceTests."
        "test_w84_verifier_goal_control_schema_and_state"
    ),
    "prior-handoff-schema-validate": (
        "tools.test_validate_week84_92_goal_evidence."
        "Week84To92GoalEvidenceTests."
        "test_w84_verifier_week83_handoff_schema_and_blocked_facts"
    ),
    "week83-lineage-identity-verify": (
        "tools.test_validate_week84_92_goal_evidence."
        "Week84To92GoalEvidenceTests."
        "test_w84_verifier_week83_bootstrap_lineage_and_product_diff"
    ),
}
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
ISOLATED_PYTHON_EXECUTABLE_ROLE = "python-current-isolated"
ADAPTER_EXECUTION_CAPABILITY = "cooperative-sealed-imports"
ADAPTER_COUNTS_SOURCE = "adapter-observation-only"
COMMAND_RUNTIME_INPUT_KINDS = frozenset(
    {
        "goal-state",
        "entry-snapshot",
        "handoff",
        "handoff-verification",
        "gate-evidence",
    }
)
W84_G0_RUNTIME_INPUT_LAYOUT: Mapping[
    str, tuple[tuple[str, str], ...]
] = {
    "goal-contract-unittest": (
        (
            "artifacts/week83-approval-projection-remediation/week84-handoff.json",
            "handoff",
        ),
        (
            "artifacts/week84-92-goal-control/goal-state.json",
            "goal-state",
        ),
        (
            "artifacts/week84-renderer-listener-retention/gate-evidence/"
            "W84-G0/entry.json",
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
            "artifacts/week83-approval-projection-remediation/week84-handoff.json",
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
            "artifacts/week84-renderer-listener-retention/gate-evidence/"
            "W84-G0/entry.json",
            "entry-snapshot",
        ),
    ),
}
W84_G0_PARSER_PATHS = frozenset(
    {
        "tools/validate-week84-92-goal-evidence.py",
        "tools/week84_92_goal_integrity.py",
        "tools/week84_92_evidence_anchor.py",
        "tools/week84_92_trusted_executor.py",
        "tools/week84_92_provider_turn_harness.py",
    }
)
# W84-G0 is the sole self-sealing bootstrap exception.  Every command binds
# the complete immutable bootstrap corpus, excluding only the impossible
# self-referential command-control document.  This deliberately over-binds
# the five workloads so dynamic unittest discovery, manifest/plan reads, and
# provider fixtures cannot escape the command-to-source proof graph.
W84_G0_SOURCE_UNIVERSE: tuple[str, ...] = tuple(
    path for path in BOOTSTRAP_CONTROL_PATHS if path != W84_G0_CONTROL_PATH
)
MAX_COMMAND_CONTROL_SOURCES_PER_COMMAND = 64
MAX_COMMAND_CONTROL_LITERAL_DEPENDENCIES = 128
MAX_COMMAND_CONTROL_SOURCE_TREE_ENTRIES = 512
MAX_COMMAND_CONTROL_SOURCE_TREE_BYTES = 64 * 1024 * 1024
MAX_COMMAND_CONTROL_PRODUCTION_PROJECTION_ENTRIES = 20_000
COMMAND_CONTROL_PRODUCTION_PROJECTION_PROTOCOL = (
    "week84-production-projection-v1"
)
COMMAND_CONTROL_EQUAL_CANDIDATE_GATE_KINDS: Mapping[str, str] = {
    "W84-G1": "regression-baseline",
    "W84-G2": "ownership-classification",
}
COMMAND_CONTROL_SOURCE_TREE_POLICIES: Mapping[str, str] = {
    # Each policy includes root/ancestor configuration (for example the root
    # .gitattributes or SDK/build files) in addition to a narrower test tree.
    # The structural Git anchor is therefore the repository root; the selected
    # subset remains independently fixed by contentRootSha256.
    "csharp-test-tree-v1": ".",
    "desktop-unit-test-tree-v1": ".",
    "desktop-e2e-test-tree-v1": ".",
}
COMMAND_CONTROL_CSHARP_TEST_ROOT = PurePosixPath("src/CSharpAiCli.Tests")
COMMAND_CONTROL_DESKTOP_UNIT_ROOT = PurePosixPath("apps/desktop/src")
COMMAND_CONTROL_DESKTOP_E2E_ROOT = PurePosixPath("apps/desktop/e2e")
COMMAND_CONTROL_TEST_CONFIG_PATHS = frozenset(
    {
        "src/CSharpAiCli.Tests/CSharpAiCli.Tests.csproj",
        "apps/desktop/playwright.config.ts",
        "apps/desktop/src/renderer/test-setup.ts",
        "apps/desktop/e2e/fixtures.ts",
        "apps/desktop/e2e/desktop-harness.ts",
        "apps/desktop/e2e/fixture-main.cjs",
        "apps/desktop/e2e/fixture-preload.cjs",
    }
)
COMMAND_CONTROL_FORBIDDEN_SOURCE_BASENAMES = frozenset(
    {
        "package-lock.json",
        "packages.lock.json",
        "pnpm-lock.yaml",
        "yarn.lock",
        "global.json",
        "Directory.Build.props",
        "sitecustomize.py",
        "usercustomize.py",
    }
)
HEX_SHA256 = re.compile(r"^[0-9a-f]{64}$")
STATUSES = ("Passed", "Failed", "NotRun", "NotApplicable")


# The IDs are policy, rather than data learned solely from the schemas.  The
# validator also extracts both schemas' registries and requires all three views
# to agree, so an accidental schema edit cannot silently redefine completion.
CANONICAL_GROUP_LAYOUT: tuple[tuple[int, str, str, int], ...] = (
    (84, "baseline", "G", 10),
    (85, "renderer", "R", 8),
    (85, "cli", "C", 6),
    (86, "renderer", "R", 8),
    (86, "cli", "C", 6),
    (87, "renderer", "R", 6),
    (87, "cli", "C", 6),
    (88, "renderer", "R", 6),
    (88, "cli", "C", 6),
    (89, "renderer", "R", 8),
    (89, "cli", "C", 8),
    (90, "integration", "G", 8),
    (91, "hardening", "G", 8),
    (92, "acceptance", "G", 10),
)

GOAL_ARTIFACT_DIR_BY_GROUP: dict[tuple[int, str], str] = {
    (84, "baseline"): "artifacts/week84-renderer-listener-retention",
    (85, "renderer"): "artifacts/week85-renderer-feature-boundaries",
    (85, "cli"): "artifacts/week85-cli-composition",
    (86, "renderer"): "artifacts/week86-renderer-chat-first-shell",
    (86, "cli"): "artifacts/week86-cli-jobs-review-session",
    (87, "renderer"): "artifacts/week87-renderer-conversation-projection",
    (87, "cli"): "artifacts/week87-cli-exec-skills-queue",
    (88, "renderer"): "artifacts/week88-renderer-composer-approval",
    (88, "cli"): "artifacts/week88-cli-packs-artifacts",
    (89, "renderer"): "artifacts/week89-context-review-workspace",
    (89, "cli"): "artifacts/week89-cli-automation-pipeline",
    (90, "integration"): "artifacts/week90-cli-desktop-integration",
    (91, "hardening"): "artifacts/week91-refactor-hardening",
    (92, "acceptance"): "artifacts/week92-refactor-acceptance",
}

CANONICAL_HANDOFF_BY_GROUP: dict[tuple[int, str], str] = {
    (84, "baseline"): W84_CANONICAL_HANDOFF_PATH,
    (85, "renderer"): "artifacts/week85-renderer-feature-boundaries/week85-renderer-handoff.json",
    (85, "cli"): "artifacts/week85-cli-composition/week85-cli-handoff.json",
    (86, "renderer"): "artifacts/week86-renderer-chat-first-shell/week86-renderer-handoff.json",
    (86, "cli"): "artifacts/week86-cli-jobs-review-session/week86-cli-handoff.json",
    (87, "renderer"): "artifacts/week87-renderer-conversation-projection/week87-renderer-handoff.json",
    (87, "cli"): "artifacts/week87-cli-exec-skills-queue/week87-cli-handoff.json",
    (88, "renderer"): "artifacts/week88-renderer-composer-approval/week88-renderer-handoff.json",
    (88, "cli"): "artifacts/week88-cli-packs-artifacts/week88-cli-handoff.json",
    (89, "renderer"): "artifacts/week89-context-review-workspace/week89-renderer-handoff.json",
    (89, "cli"): "artifacts/week89-cli-automation-pipeline/week89-cli-handoff.json",
    (90, "integration"): "artifacts/week90-cli-desktop-integration/week90-integration-handoff.json",
    (91, "hardening"): "artifacts/week91-refactor-hardening/week91-hardening-handoff.json",
    (92, "acceptance"): FINAL_HANDOFF_PATH,
}
CANONICAL_HANDOFF_PATHS = tuple(
    CANONICAL_HANDOFF_BY_GROUP[(week, lane)]
    for week, lane, _prefix, _count in CANONICAL_GROUP_LAYOUT
)

HANDOFF_PARENT_GROUPS: dict[tuple[int, str], tuple[tuple[int, str], ...]] = {
    (84, "baseline"): (),
    (85, "renderer"): ((84, "baseline"),),
    (85, "cli"): ((84, "baseline"),),
    (86, "renderer"): ((85, "renderer"),),
    (86, "cli"): ((85, "cli"),),
    (87, "renderer"): ((86, "renderer"),),
    (87, "cli"): ((86, "cli"),),
    (88, "renderer"): ((87, "renderer"),),
    (88, "cli"): ((87, "cli"),),
    (89, "renderer"): ((88, "renderer"),),
    (89, "cli"): ((88, "cli"),),
    (90, "integration"): ((89, "renderer"), (89, "cli")),
    (91, "hardening"): ((90, "integration"),),
    (92, "acceptance"): ((91, "hardening"),),
}

# These Gates exercise real provider-backed behavior in the frozen plans.  A
# Passed result with zero consumed turns is never acceptable.  Week84 also has
# named receipts, allowing stronger profile-completeness checks.
PROVIDER_GATE_REQUIREMENTS: dict[str, dict[str, Any]] = {
    "W84-G6": {
        "minTurns": 3,
        "scopes": {"provider-read-only", "provider-recovery"},
        "receipts": {"provider-readonly.json", "provider-recovery.json"},
    },
    "W84-G7": {
        "minTurns": 30,
        "scopes": {"provider-resource"},
        "receipts": {f"provider-resource-profile-{index}.json" for index in range(1, 6)},
    },
    "W84-G8": {
        "minTurns": 1,
        "scopes": {"controlled-write"},
        "receipts": {"controlled-write.json"},
    },
    "W92-G7": {
        "minTurns": 34,
        "scopes": {
            "provider-read-only",
            "provider-recovery",
            "provider-resource",
            "controlled-write",
        },
        "receipts": {
            "provider-readonly.json",
            "provider-recovery.json",
            *(f"provider-resource-profile-{index}.json" for index in range(1, 6)),
            "controlled-write.json",
        },
    },
}

PROVIDER_PHASE_LAYOUT: dict[str, tuple[tuple[str, int], ...]] = {
    "W84-G6": (("provider-read-only", 1), ("provider-recovery", 2)),
    "W84-G7": (("provider-resource", 30),),
    "W84-G8": (("controlled-write", 1),),
    "W92-G7": (
        ("provider-read-only", 1),
        ("provider-recovery", 2),
        ("provider-resource", 30),
        ("controlled-write", 1),
    ),
}
PROVIDER_PHASE_COUNTS: dict[str, Counter[str]] = {
    gate_id: Counter(dict(layout))
    for gate_id, layout in PROVIDER_PHASE_LAYOUT.items()
}
REAL_PROVIDER_GATE_IDS = frozenset(PROVIDER_PHASE_LAYOUT)
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
PROVIDER_LEDGER_BINDING_BASENAME = "provider-ledger-binding.json"
PROVIDER_SCOPES = frozenset(
    {
        "provider-read-only",
        "provider-recovery",
        "provider-resource",
        "controlled-write",
    }
)
PROVIDER_ATTEMPT_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")
PROVIDER_ATTEMPT_OUTCOMES = frozenset({"Failed", "Passed"})
PROVIDER_RESERVATION_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")
PROVIDER_TURN_OUTCOMES = frozenset({"Succeeded", "Failed"})
TEST_RUNNER_ID = "week84-92-trusted-executor-v1"
TEST_RUNNER_PATH = "tools/week84_92_trusted_executor.py"
TRUSTED_PRODUCT_POLICY_ID = "week84-92-product-command-v1"
TRUSTED_PRODUCT_SCRIPT_ROOT = "tools/week84_92_commands"
TRUSTED_PRODUCT_RESULT_PROTOCOL = "week84-92-product-command-result-v1"
TRUSTED_PRODUCT_COUNTS_SOURCE = "trusted-product-command-result-v1"
COMMAND_CONTROL_ROOT = "docs_md/weekly/84_92_command_control"
COMMAND_CONTROL_PROTOCOL = "week84-92-command-control-v1"
COMMAND_CONTROL_SOURCE_TRUST = frozenset(
    {"prior-sealed", "bootstrap-external-review"}
)
COMMAND_CONTROL_PREDECESSOR_MODES = frozenset(
    {
        "w84-bootstrap-exception",
        "entry-base",
        "prior-gate",
        "w90-integration-entry-base",
    }
)
COMMAND_CONTROL_ROLES = frozenset(
    {"adapter", "oracle", "test", "fixture", "parser"}
)
COMMAND_CONTROL_ORIGINS = frozenset(
    {"prepared", "bootstrap", "prior-control"}
)
COMMAND_CONTROL_ROLE_POLICY = {
    "requiredAll": ["adapter"],
    "requiredAny": [["oracle", "test"]],
    "optional": ["fixture", "parser"],
}
W84_BOOTSTRAP_PARENT = "e6b5c7f71d06a22c70bf534e6764bcb96ef06337"
W84_WEEK83_PRODUCT_CANDIDATE = "ccf9d82c9fa76c201876ee01d3849902989091e9"
W84_WEEK83_DOCUMENTATION_CLOSURE = (
    "7cd1eac2b3ba5f8b9aa9dd6263dcb83d9dd66cd3"
)
W84_G0_ENTRY_PATH = (
    "artifacts/week84-renderer-listener-retention/"
    "gate-evidence/W84-G0/entry.json"
)
W84_G0_BASELINE_IDENTITY_PATH = (
    "artifacts/week84-renderer-listener-retention/"
    "gate-evidence/W84-G0/baseline-identity.json"
)
W84_G0_HANDOFF_VERIFICATION_PATH = (
    "artifacts/week84-renderer-listener-retention/"
    "gate-evidence/W84-G0/week83-handoff-verification.json"
)
W84_G0_GOAL_SUMMARY_PATH = (
    "artifacts/week84-renderer-listener-retention/"
    "gate-evidence/W84-G0/goal-contract-unittest.json"
)
W84_G0_EXECUTOR_SUMMARY_PATH = (
    "artifacts/week84-renderer-listener-retention/"
    "gate-evidence/W84-G0/trusted-executor-unittest.json"
)
W84_G0_GATE_PATH = (
    "artifacts/week84-renderer-listener-retention/gates/W84-G0.json"
)
W84_G0_OVERLAY_PROTOCOL = "week84-g0-overlay-validation-v1"
W84_G0_INITIAL_OVERLAY_PATHS: tuple[str, ...] = (
    goal_integrity.FAILURE_LEDGER_PATH,
    goal_integrity.USER_DECISION_LEDGER_PATH,
    LEDGER_PATH,
    PROVIDER_RUNTIME_JOURNAL_PATH,
    W84_G0_ENTRY_PATH,
    W84_G0_HANDOFF_VERIFICATION_PATH,
    W84_G0_BASELINE_IDENTITY_PATH,
    trusted_executor.GOAL_STATE_PATH,
)
W84_G0_FINAL_OVERLAY_PATHS: tuple[str, ...] = (
    W84_G0_GOAL_SUMMARY_PATH,
    W84_G0_EXECUTOR_SUMMARY_PATH,
    trusted_executor.GOAL_STATE_PATH,
    W84_G0_GATE_PATH,
)
W84_G0_OVERLAY_CHECKS: tuple[str, ...] = (
    "schemas",
    "fullSemantics",
    "gitProvenance",
    "commandProvenance",
    "semanticProvenance",
    "secretScan",
    "goalTransition",
    "requirementsBinding",
    "integrity",
    "cleanupReceipts",
    "temporalMonotonicity",
)
W84_G0_OVERLAY_TRANSACTION_ID = re.compile(r"^[0-9a-f]{32}$")
W84_G0_OVERLAY_MAX_FILE_BYTES = 8 * 1024 * 1024
W84_G0_OVERLAY_MAX_FILES = 4096
W84_G0_OVERLAY_MAX_TOTAL_BYTES = 128 * 1024 * 1024
W84_WEEK83_PACKAGE_IDENTITY_PATH = (
    "artifacts/week83-approval-projection-remediation/package-identity.json"
)
W84_WEEK83_PACKAGE_IDENTITY_SHA256 = (
    "3212fb4220c2820be53458b72e00df66625ce80a77bbeba6b8a4f9fb366627b0"
)
W84_PRODUCT_INPUT_PATHS: tuple[str, ...] = (
    "src",
    "apps/desktop/src",
    "apps/desktop/index.html",
    "apps/desktop/package.json",
    "apps/desktop/package-lock.json",
    "apps/desktop/tsconfig.json",
    "apps/desktop/vite.config.ts",
    "Directory.Build.props",
    "global.json",
)
TRUSTED_PROVIDER_POLICY_ID = "week84-92-provider-turn-v1"
TRUSTED_PROVIDER_HARNESS_PATH = "tools/week84_92_provider_turn_harness.py"
TRUSTED_PROVIDER_REDACTED_INVOCATION = (
    "python-current -I -S -E -B -X utf8 <trusted-provider-command> "
    "--descriptor <trusted-descriptor> --observation <trusted-observation>"
)
PROVIDER_DESCRIPTOR_PROTOCOL = "week84-92-provider-batch-descriptor-v1"
PROVIDER_OBSERVED_REQUEST_PROTOCOL = "week84-92-observed-provider-request-v1"
PROVIDER_BATCH_SIZES = {
    "provider-read-only": 1,
    "provider-recovery": 2,
    "provider-resource": 6,
    "controlled-write": 1,
}
PROVIDER_SCENARIO_PATHS = {
    "provider-read-only": "tools/week84_92_provider_scenarios/provider_read_only.py",
    "provider-recovery": "tools/week84_92_provider_scenarios/provider_recovery.py",
    "provider-resource": "tools/week84_92_provider_scenarios/provider_resource.py",
    "controlled-write": "tools/week84_92_provider_scenarios/controlled_write.py",
}
PROVIDER_DRIVER_PATHS = {
    "provider-read-only": "tools/week84_92_provider_drivers/provider_read_only.mjs",
    "provider-recovery": "tools/week84_92_provider_drivers/provider_recovery.mjs",
    "provider-resource": "tools/week84_92_provider_drivers/provider_resource.mjs",
    "controlled-write": "tools/week84_92_provider_drivers/controlled_write.mjs",
}
PROVIDER_BOUNDARY_MODES = (
    "strong-isolation",
    "cooperative-candidate",
)
PROVIDER_BOUNDARY_DECISION_PATHS = {
    "W84-G6": "docs_md/weekly/84_92_provider_boundary_decisions/W84-G6.json",
    "W84-G7": "docs_md/weekly/84_92_provider_boundary_decisions/W84-G6.json",
    "W84-G8": "docs_md/weekly/84_92_provider_boundary_decisions/W84-G6.json",
    "W92-G7": "docs_md/weekly/84_92_provider_boundary_decisions/W92-G7.json",
}
PROVIDER_BOUNDARY_DECISION_POLICIES = {
    "docs_md/weekly/84_92_provider_boundary_decisions/W84-G6.json": {
        "boundaryGateId": "W84-G6",
        "allowedGates": ["W84-G6", "W84-G7", "W84-G8"],
        "allowedScopes": [
            "provider-read-only",
            "provider-recovery",
            "provider-resource",
            "controlled-write",
        ],
        "authorizedTurns": 34,
    },
    "docs_md/weekly/84_92_provider_boundary_decisions/W92-G7.json": {
        "boundaryGateId": "W92-G7",
        "allowedGates": ["W92-G7"],
        "allowedScopes": [
            "provider-read-only",
            "provider-recovery",
            "provider-resource",
            "controlled-write",
        ],
        "authorizedTurns": 34,
    },
}
PROVIDER_PACKAGE_ENTRYPOINT = "caicli-desktop.exe"
PROVIDER_PACKAGE_ARGV = ["--disable-gpu"]
PROVIDER_PACKAGE_APPHOST_PATH = "resources/apphost/CSharpAiCli.AppHost.exe"
PROVIDER_LAUNCH_LAYOUT: dict[
    str, tuple[tuple[str, int | None, int], ...]
] = {
    "W84-G6": (
        ("provider-read-only", None, 1),
        ("provider-recovery", None, 2),
    ),
    "W84-G7": tuple(
        ("provider-resource", profile, 6) for profile in range(1, 6)
    ),
    "W84-G8": (("controlled-write", None, 1),),
    "W92-G7": (
        ("provider-read-only", None, 1),
        ("provider-recovery", None, 2),
        *(("provider-resource", profile, 6) for profile in range(1, 6)),
        ("controlled-write", None, 1),
    ),
}
PRODUCT_COMMAND_SLUG = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
UNITTEST_EXACT_NAME = re.compile(
    r"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*){2,}$"
)
SAFE_REPORT_ATTEMPT_ID = re.compile(
    r"^[a-z0-9]+(?:-[a-z0-9]+){0,15}$"
)
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
USER_ACCEPTANCE_REQUEST_KEYS = {
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
DECISION_REQUEST_ID = re.compile(r"^UA-[A-Z0-9][A-Z0-9-]{2,63}$")
CHALLENGE_CODE = re.compile(r"^CH-[A-Z0-9]{8,32}$")
TEST_COUNT_SOURCES = frozenset(
    {"python-unittest-output", "caicli-test-counts-marker-v1"}
)
TEST_REPORT_COUNT_KEYS = (
    "discovered",
    "passed",
    "failed",
    "skipped",
    "notRun",
    "notApplicable",
)
FROZEN_UNITTEST_SEPARATOR = "-" * 70
_FROZEN_UNITTEST_RESULT = re.compile(
    r"(?m)^-{70}\n"
    r"Ran ([0-9]+) (test|tests) in ([0-9]+\.[0-9]{3})s\n\n"
    r"(OK(?: \(skipped=([0-9]+)\))?)\n\Z"
)
_UNITTEST_RAN_CANDIDATE = re.compile(r"(?m)^Ran(?: .*)?$")
_UNITTEST_RESULT_CANDIDATE = re.compile(
    r"(?m)^(?:OK(?: .*)?|FAILED(?: .*)?)$"
)
PROVIDER_JOURNAL_VERSION = "week84-92-provider-runtime-v1"
CONTROLLED_WRITE_TOMBSTONE_DIR = (
    "docs_md/weekly/84_92_controlled_write_tombstones"
)

MANUAL_GATE_REQUIREMENTS: dict[str, dict[str, Any]] = {
    "W86-R7": {
        "acceptanceId": "W86-USER-VISUAL",
        "receipt": "user-visual-confirmation.json",
        "manifest": "screenshot-manifest.json",
        "hashField": "screenshotManifestSha256",
    },
    "W89-R5": {
        "acceptanceId": "W89-USER-VISUAL",
        "receipt": "visual-acceptance.json",
        "manifest": "screenshot-manifest.json",
        "hashField": "screenshotManifestSha256",
    },
    "W92-G9": {
        "acceptanceId": "W92-USER-VISUAL",
        "receipt": "visual-acceptance.json",
        "manifest": "screenshot-manifest.json",
        "hashField": "screenshotManifestSha256",
    },
}

W91_CHECKLIST_REVISION = "w91-narrator-operator-ux-v1"
W91_NARRATOR_CHECKLIST_IDS = (
    "screen-reader-start-navigation",
    "workspace-thread-conversation-landmarks",
    "streaming-status-and-tool-disclosure",
    "approval-prompt-and-decision",
    "error-recovery-and-focus-return",
    "context-drawer-review-terminal",
)
W91_OPERATOR_UX_CHECKLIST_IDS = (
    "keyboard-only-primary-flow",
    "escape-focus-return",
    "zoom-high-contrast-reduced-motion",
    "long-text-narrow-viewport",
)
W92_VISUAL_VIEWPORTS = ("1440x900", "1024x768", "800x900")
W92_VISUAL_FIXTURES = (
    "workspace-thread",
    "streaming-tools",
    "waiting-approval",
    "failed-recovery",
    "context-review-terminal",
)
W92_VISUAL_ARTIFACT_TYPES = (
    "screenshot",
    "dom-snapshot",
    "a11y-snapshot",
)


def _canonical_gate_ids() -> tuple[str, ...]:
    return tuple(
        f"W{week}-{prefix}{index}"
        for week, _lane, prefix, count in CANONICAL_GROUP_LAYOUT
        for index in range(count)
    )


CANONICAL_GATE_IDS = _canonical_gate_ids()


@dataclass(frozen=True)
class GateGroup:
    week: int
    lane: str
    gate_ids: tuple[str, ...]
    result_pattern: str | None = None

    @property
    def checkpoint(self) -> str:
        return f"W{self.week}"

    @property
    def key(self) -> tuple[int, str]:
        return (self.week, self.lane)


@dataclass(frozen=True)
class Contract:
    registry: tuple[str, ...]
    groups: tuple[GateGroup, ...]

    @property
    def gate_to_group(self) -> dict[str, GateGroup]:
        return {
            gate_id: group for group in self.groups for gate_id in group.gate_ids
        }

    @property
    def group_by_key(self) -> dict[tuple[int, str], GateGroup]:
        return {group.key: group for group in self.groups}


@dataclass(frozen=True)
class Document:
    path: Path
    relative_path: str
    data: Any
    sha256: str


@dataclass(frozen=True)
class _ValidationRoots:
    """Internally derived candidate/control checkout roots.

    The CLI exposes only the candidate root.  The control root is accepted
    only when it is the unique trusted control-branch worktree derived by the
    frozen executor.
    """

    candidate_root: Path
    control_root: Path
    repository_context: Any | None


class DuplicateJsonKey(ValueError):
    """Raised when a JSON object contains the same key more than once."""


class Problems:
    def __init__(self) -> None:
        self.items: list[str] = []

    def add(self, code: str, location: str, message: str) -> None:
        self.items.append(f"[{code}] {location}: {message}")

    def extend(self, values: Iterable[str]) -> None:
        self.items.extend(values)

    def __bool__(self) -> bool:
        return bool(self.items)


def _no_duplicate_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise DuplicateJsonKey(f"duplicate JSON key {key!r}")
        result[key] = value
    return result


def _json_bytes(value: Any) -> bytes:
    """Stable bytes used only by unit fixtures, not for on-disk file hashes."""
    return json.dumps(
        value, ensure_ascii=False, sort_keys=True, separators=(",", ":")
    ).encode("utf-8")


def document_from_data(relative_path: str, data: Any) -> Document:
    """Create an in-memory document; useful for semantic unit tests."""
    payload = _json_bytes(data)
    return Document(
        path=Path(relative_path),
        relative_path=relative_path.replace("\\", "/"),
        data=data,
        sha256=hashlib.sha256(payload).hexdigest(),
    )


def _relative_to_repo(repo_root: Path, path: Path) -> str:
    try:
        return path.resolve().relative_to(repo_root.resolve()).as_posix()
    except ValueError:
        return path.resolve().as_posix()


def read_document(repo_root: Path, path: Path, problems: Problems) -> Document | None:
    try:
        raw = path.read_bytes()
    except OSError:
        problems.add(
            "FILE_READ",
            _relative_to_repo(repo_root, path),
            "document bytes could not be read",
        )
        return None
    try:
        text = raw.decode("utf-8-sig")
        data = json.loads(text, object_pairs_hook=_no_duplicate_object)
    except (UnicodeDecodeError, json.JSONDecodeError, DuplicateJsonKey):
        # Parser exception strings may contain an attacker-controlled duplicate
        # key or token.  Diagnostics are deliberately fixed and secret-safe.
        problems.add(
            "JSON_PARSE",
            _relative_to_repo(repo_root, path),
            "document is not unique-key UTF-8 JSON",
        )
        return None
    return Document(
        path=path,
        relative_path=_relative_to_repo(repo_root, path),
        data=data,
        sha256=hashlib.sha256(raw).hexdigest(),
    )


def _canonical_groups_without_patterns() -> tuple[GateGroup, ...]:
    groups: list[GateGroup] = []
    for week, lane, prefix, count in CANONICAL_GROUP_LAYOUT:
        groups.append(
            GateGroup(
                week=week,
                lane=lane,
                gate_ids=tuple(f"W{week}-{prefix}{i}" for i in range(count)),
            )
        )
    return tuple(groups)


def _walk_json(value: Any) -> Iterable[Any]:
    yield value
    if isinstance(value, Mapping):
        for child in value.values():
            yield from _walk_json(child)
    elif isinstance(value, list):
        for child in value:
            yield from _walk_json(child)


def extract_contract(
    goal_schema: Mapping[str, Any],
    gate_schema: Mapping[str, Any],
    problems: Problems,
) -> Contract:
    """Extract current schema fields and bind them to the frozen registry."""
    prefix_items = (
        goal_schema.get("properties", {})
        .get("gateRegistry", {})
        .get("prefixItems", [])
    )
    schema_registry = tuple(
        item.get("const") if isinstance(item, Mapping) else None
        for item in prefix_items
    )
    if schema_registry != CANONICAL_GATE_IDS:
        problems.add(
            "SCHEMA_REGISTRY",
            SCHEMA_FILENAMES["goal"],
            "gateRegistry prefixItems do not equal the frozen 104-Gate registry",
        )

    extracted: dict[tuple[int, str], GateGroup] = {}
    for node in _walk_json(gate_schema):
        if not isinstance(node, Mapping):
            continue
        properties = node.get("properties")
        if not isinstance(properties, Mapping):
            continue
        gate_rule = properties.get("gateId")
        week_rule = properties.get("week")
        lane_rule = properties.get("lane")
        if not (
            isinstance(gate_rule, Mapping)
            and isinstance(gate_rule.get("enum"), list)
            and isinstance(week_rule, Mapping)
            and isinstance(week_rule.get("const"), int)
            and isinstance(lane_rule, Mapping)
            and isinstance(lane_rule.get("const"), str)
        ):
            continue
        ids = tuple(gate_rule["enum"])
        if not ids or not all(isinstance(item, str) for item in ids):
            continue
        result_rule = properties.get("resultPath", {})
        result_pattern = (
            result_rule.get("pattern") if isinstance(result_rule, Mapping) else None
        )
        group = GateGroup(
            week=week_rule["const"],
            lane=lane_rule["const"],
            gate_ids=ids,
            result_pattern=result_pattern,
        )
        existing = extracted.get(group.key)
        if existing is not None and existing != group:
            problems.add(
                "SCHEMA_GROUP_DUPLICATE",
                SCHEMA_FILENAMES["gate"],
                f"conflicting definitions for W{group.week}/{group.lane}",
            )
        extracted[group.key] = group

    canonical = _canonical_groups_without_patterns()
    groups: list[GateGroup] = []
    for expected in canonical:
        actual = extracted.get(expected.key)
        if actual is None:
            problems.add(
                "SCHEMA_GROUP_MISSING",
                SCHEMA_FILENAMES["gate"],
                f"missing Gate group W{expected.week}/{expected.lane}",
            )
            groups.append(expected)
            continue
        if actual.gate_ids != expected.gate_ids:
            problems.add(
                "SCHEMA_GROUP_IDS",
                SCHEMA_FILENAMES["gate"],
                f"W{expected.week}/{expected.lane} IDs differ from frozen policy",
            )
        groups.append(
            GateGroup(
                week=expected.week,
                lane=expected.lane,
                gate_ids=expected.gate_ids,
                result_pattern=actual.result_pattern,
            )
        )
    extra = sorted(set(extracted) - {group.key for group in canonical})
    if extra:
        problems.add(
            "SCHEMA_GROUP_EXTRA",
            SCHEMA_FILENAMES["gate"],
            f"unexpected Gate groups: {extra}",
        )
    union = tuple(gate_id for group in groups for gate_id in group.gate_ids)
    if union != CANONICAL_GATE_IDS or len(set(union)) != 104:
        problems.add(
            "SCHEMA_GATE_UNION",
            SCHEMA_FILENAMES["gate"],
            "Gate groups are not an exact unique 104-ID registry",
        )
    return Contract(registry=CANONICAL_GATE_IDS, groups=tuple(groups))


def _is_int(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _timestamp(value: Any) -> datetime | None:
    if not isinstance(value, str) or not value:
        return None
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    return parsed if parsed.utcoffset() is not None else None


def _utc_now_z() -> str:
    return (
        datetime.now(timezone.utc)
        .isoformat(timespec="microseconds")
        .replace("+00:00", "Z")
    )


def _mapping(value: Any) -> Mapping[str, Any]:
    return value if isinstance(value, Mapping) else {}


def _list(value: Any) -> list[Any]:
    return value if isinstance(value, list) else []


def _check_balanced_counts(
    problems: Problems,
    location: str,
    counts: Any,
    total_key: str,
    category_keys: Sequence[str],
) -> None:
    if not isinstance(counts, Mapping):
        problems.add("COUNTS_TYPE", location, "counts must be an object")
        return
    keys = (total_key, *category_keys)
    if not all(_is_int(counts.get(key)) and counts.get(key, -1) >= 0 for key in keys):
        problems.add("COUNTS_VALUE", location, f"non-negative integers required: {keys}")
        return
    expected = sum(int(counts[key]) for key in category_keys)
    if counts[total_key] != expected:
        problems.add(
            "COUNTS_BALANCE",
            location,
            f"{total_key}={counts[total_key]} but categories sum to {expected}",
        )
    if counts.get("countsBalanced") is not True:
        problems.add("COUNTS_FLAG", location, "countsBalanced must be true")


def _check_cleanup(
    problems: Problems, location: str, cleanup: Any, ready: bool
) -> None:
    if not isinstance(cleanup, Mapping):
        problems.add("CLEANUP_TYPE", location, "cleanup must be an object")
        return
    if cleanup.get("countsBalanced") is not True:
        problems.add("CLEANUP_COUNTS", location, "countsBalanced must be true")
    gate_shape = "ownedProcessesRemaining" in cleanup
    handoff_shape = "trackedResidueCount" in cleanup
    if gate_shape:
        remaining_fields = (
            "ownedProcessesRemaining",
            "ownedTempPathsRemaining",
            "configMutationsRemaining",
        )
        if cleanup.get("unownedProcessTouched") is not False:
            problems.add("CLEANUP_UNOWNED_PROCESS", location, "unowned process was touched")
        if cleanup.get("unownedPathTouched") is not False:
            problems.add("CLEANUP_UNOWNED_PATH", location, "unowned path was touched")
    elif handoff_shape:
        remaining_fields = (
            "trackedResidueCount",
            "untrackedResidueCount",
            "tempArtifactCount",
            "processResidueCount",
        )
    else:
        problems.add("CLEANUP_SHAPE", location, "unrecognized cleanup accounting fields")
        return
    if ready or cleanup.get("status") == "Passed":
        allowed_statuses = {"Passed"} if ready else {"Passed", "NotApplicable"}
        if cleanup.get("status") not in allowed_statuses:
            problems.add("CLEANUP_STATUS", location, "ready evidence requires Passed cleanup")
        for field in remaining_fields:
            if cleanup.get(field) != 0:
                problems.add("CLEANUP_REMAINDER", location, f"{field} must be zero")


def _check_p0_p1(
    problems: Problems, location: str, issues: Any, require_zero: bool
) -> None:
    if not isinstance(issues, Mapping):
        problems.add("ISSUES_TYPE", location, "openIssues must be an object")
        return
    for key in ("p0", "p1"):
        value = issues.get(key)
        if not _is_int(value) or value < 0:
            problems.add("ISSUES_VALUE", location, f"{key} must be a non-negative integer")
        elif require_zero and value != 0:
            problems.add("OPEN_HIGH_PRIORITY", location, f"{key} must be zero")


def _status_counter(items: Iterable[Mapping[str, Any]]) -> Counter[str]:
    return Counter(str(item.get("status")) for item in items)


def _result_path_matches_document(document: Document, result_path: Any) -> bool:
    return isinstance(result_path, str) and result_path.replace("\\", "/") == document.relative_path


def _safe_relative_evidence_path(
    repo_root: Path | None,
    value: Any,
    problems: Problems,
    location: str,
) -> Path | None:
    if not isinstance(value, str) or not value:
        problems.add("EVIDENCE_PATH", location, "evidence path must be a non-empty string")
        return None
    candidate = Path(value)
    if not candidate.parts:
        problems.add("EVIDENCE_PATH", location, "evidence path must name a file")
        return None
    if candidate.is_absolute() or candidate.drive or candidate.root:
        problems.add("EVIDENCE_PATH_ABSOLUTE", location, "evidence path must be repository-relative")
        return None
    if ".." in candidate.parts:
        problems.add("EVIDENCE_PATH_ESCAPE", location, "evidence path cannot contain parent traversal")
        return None
    if repo_root is None:
        return candidate
    lexical_root = Path(os.path.abspath(repo_root))
    lexical = lexical_root.joinpath(*candidate.parts)
    try:
        lexical.relative_to(lexical_root)
    except ValueError:
        problems.add("EVIDENCE_PATH_ESCAPE", location, "evidence path resolves outside repository")
        return None

    # Evidence bytes must have one lexical identity.  Resolving first would
    # hide an in-repository symlink/junction, while a hard link would allow the
    # same inode to be mutated through an unbound alias.  Walk the lexical path
    # with lstat and fail closed on every existing reparse component.  A
    # missing final path remains returnable so its caller can emit the specific
    # missing-evidence error, but all existing parents must be safe directories.
    current = lexical_root
    parts = candidate.parts
    for index, part in enumerate(parts):
        current = current / part
        try:
            info = os.lstat(current)
        except FileNotFoundError:
            # Once a component is absent, no descendant can exist through the
            # lexical path.  The already-walked parent is the controlled root.
            return lexical
        except OSError:
            problems.add(
                "EVIDENCE_PATH_IDENTITY",
                location,
                "evidence path identity could not be verified",
            )
            return None
        is_reparse = stat.S_ISLNK(info.st_mode) or bool(
            getattr(info, "st_file_attributes", 0) & 0x400
        )
        if is_reparse:
            problems.add(
                "EVIDENCE_PATH_REPARSE",
                location,
                "evidence paths cannot contain symlinks, junctions, or reparse points",
            )
            return None
        final = index == len(parts) - 1
        if not final and not stat.S_ISDIR(info.st_mode):
            problems.add(
                "EVIDENCE_PATH_PARENT",
                location,
                "every existing evidence parent must be a directory",
            )
            return None
        if final:
            if not stat.S_ISREG(info.st_mode):
                problems.add(
                    "EVIDENCE_PATH_TYPE",
                    location,
                    "existing evidence paths must be regular files",
                )
                return None
            if int(getattr(info, "st_nlink", 1)) != 1:
                problems.add(
                    "EVIDENCE_PATH_HARDLINK",
                    location,
                    "evidence files must have exactly one filesystem link",
                )
                return None
    return lexical


def _looks_redacted(value: str) -> bool:
    normalized = value.strip().lower()
    return normalized in {
        "<redacted>",
        "[redacted]",
        "redacted",
        "***",
        "****",
        "unset",
        "not-set",
    }


SECRET_PATTERNS = (
    re.compile(r"\bsk-(?:proj-)?[A-Za-z0-9_-]{16,}"),
    re.compile(r"\bBearer\s+[A-Za-z0-9._~+/-]{12,}", re.IGNORECASE),
    re.compile(r"\bOPENAI_API_KEY\s*[:=]\s*(?!<redacted>|\[redacted\]|redacted|\*{3,})\S+", re.IGNORECASE),
    re.compile(r"\bOPENAI_MODEL\s*[:=]\s*(?!<redacted>|\[redacted\]|redacted|\*{3,})\S+", re.IGNORECASE),
    re.compile(r"\bOPENAI_BASE_URL\s*[:=]\s*(?!<redacted>|\[redacted\]|redacted|\*{3,})\S+", re.IGNORECASE),
)
SENSITIVE_KEY = re.compile(
    r"^(?:api[_-]?key|access[_-]?token|refresh[_-]?token|password|passwd|secret|"
    r"openai[_-]?(?:api[_-]?key|model|base[_-]?url)|model|base[_-]?url|provider[_-]?config)$",
    re.IGNORECASE,
)


def _scan_value_for_secrets(
    value: Any,
    problems: Problems,
    location: str,
) -> None:
    if isinstance(value, Mapping):
        for key, child in value.items():
            child_location = f"{location}/{key}"
            if (
                isinstance(key, str)
                and SENSITIVE_KEY.fullmatch(key)
                and (
                    (
                        isinstance(child, str)
                        and child
                        and not _looks_redacted(child)
                    )
                    or (
                        re.fullmatch(r"provider[_-]?config", key, re.IGNORECASE)
                        is not None
                        and isinstance(child, (Mapping, list))
                        and any(
                            isinstance(item, str)
                            and item
                            and not _looks_redacted(item)
                            for item in _walk_json(child)
                        )
                    )
                )
            ):
                problems.add("SECRET_DETECTED", child_location, "non-redacted secret-like field detected")
            _scan_value_for_secrets(child, problems, child_location)
    elif isinstance(value, list):
        for index, child in enumerate(value):
            _scan_value_for_secrets(child, problems, f"{location}/{index}")
    elif isinstance(value, str) and any(pattern.search(value) for pattern in SECRET_PATTERNS):
        problems.add("SECRET_DETECTED", location, "secret-like value detected")


def _scan_text_evidence_for_secrets(
    path: Path,
    kind: Any,
    problems: Problems,
    location: str,
) -> None:
    try:
        raw = path.read_bytes()
    except OSError:
        return
    # Scan byte-preserving ASCII/UTF-8 and the two UTF-16 byte orders.  This
    # catches secret text embedded in metadata as well as deliberately encoded
    # text evidence.  Pixel/audio content still requires the explicit redaction
    # manifest checked below; the validator does not claim OCR coverage.
    embedded_text: list[bytes] = []
    if kind == "screenshot" and path.suffix.lower() == ".png":
        _valid_png, embedded_text = _parse_png_container(raw)
    decoded_views = _decoded_secret_views(raw)
    for value in embedded_text:
        decoded_views += _decoded_secret_views(value)
    if any(
        pattern.search(text)
        for text in decoded_views
        for pattern in SECRET_PATTERNS
    ):
        problems.add("SECRET_DETECTED", location, "secret-like value detected in evidence file")
    if kind not in {"screenshot", "video"}:
        try:
            raw.decode("utf-8")
        except UnicodeDecodeError:
            problems.add(
                "EVIDENCE_TEXT_ENCODING",
                location,
                "non-visual evidence must be UTF-8; binary evidence must use an approved visual kind",
            )


def _decoded_secret_views(raw: bytes) -> tuple[str, ...]:
    views = [
        raw.decode("utf-8", errors="ignore"),
        raw.decode("latin-1", errors="ignore"),
    ]
    for offset in (0, 1):
        candidate = raw[offset:]
        views.append(candidate.decode("utf-16-le", errors="ignore"))
        views.append(candidate.decode("utf-16-be", errors="ignore"))
    return tuple(views)


MAX_VISUAL_METADATA_BYTES = 1024 * 1024


def _bounded_zlib_decompress(raw: bytes) -> bytes | None:
    try:
        decompressor = zlib.decompressobj()
        decoded = decompressor.decompress(raw, MAX_VISUAL_METADATA_BYTES + 1)
        if (
            len(decoded) > MAX_VISUAL_METADATA_BYTES
            or decompressor.unconsumed_tail
            or decompressor.unused_data
            or not decompressor.eof
        ):
            return None
        decoded += decompressor.flush(
            MAX_VISUAL_METADATA_BYTES + 1 - len(decoded)
        )
    except (zlib.error, ValueError):
        return None
    return decoded if len(decoded) <= MAX_VISUAL_METADATA_BYTES else None


def _parse_png_container(raw: bytes) -> tuple[bool, list[bytes]]:
    if not raw.startswith(b"\x89PNG\r\n\x1a\n"):
        return False, []
    offset = 8
    chunk_index = 0
    seen_ihdr = False
    seen_idat = False
    seen_iend = False
    embedded_text: list[bytes] = []
    while offset < len(raw):
        if len(raw) - offset < 12:
            return False, []
        length = struct.unpack(">I", raw[offset : offset + 4])[0]
        chunk_type = raw[offset + 4 : offset + 8]
        data_start = offset + 8
        data_end = data_start + length
        chunk_end = data_end + 4
        if data_end < data_start or chunk_end > len(raw):
            return False, []
        chunk_data = raw[data_start:data_end]
        declared_crc = struct.unpack(">I", raw[data_end:chunk_end])[0]
        if (
            len(chunk_type) != 4
            or any(not (65 <= value <= 90 or 97 <= value <= 122) for value in chunk_type)
            or (zlib.crc32(chunk_type + chunk_data) & 0xFFFFFFFF) != declared_crc
        ):
            return False, []
        if chunk_type == b"IHDR":
            if chunk_index != 0 or seen_ihdr or length != 13:
                return False, []
            width, height = struct.unpack(">II", chunk_data[:8])
            if width == 0 or height == 0:
                return False, []
            seen_ihdr = True
        elif not seen_ihdr:
            return False, []
        elif chunk_type == b"IDAT":
            if seen_iend:
                return False, []
            seen_idat = True
        elif chunk_type == b"IEND":
            if seen_iend or length != 0 or not seen_idat:
                return False, []
            seen_iend = True
            if chunk_end != len(raw):
                return False, []
        elif chunk_type == b"tEXt":
            if b"\0" not in chunk_data:
                return False, []
            embedded_text.append(chunk_data)
        elif chunk_type == b"zTXt":
            if b"\0" not in chunk_data:
                return False, []
            keyword, compressed = chunk_data.split(b"\0", 1)
            if not keyword or not compressed or compressed[0] != 0:
                return False, []
            decoded = _bounded_zlib_decompress(compressed[1:])
            if decoded is None:
                return False, []
            embedded_text.extend((keyword, decoded))
        elif chunk_type == b"iTXt":
            try:
                keyword, remainder = chunk_data.split(b"\0", 1)
                compression_flag = remainder[0]
                compression_method = remainder[1]
                language, remainder = remainder[2:].split(b"\0", 1)
                translated_keyword, text_value = remainder.split(b"\0", 1)
            except (ValueError, IndexError):
                return False, []
            if (
                not keyword
                or compression_flag not in (0, 1)
                or compression_method != 0
            ):
                return False, []
            if compression_flag == 1:
                decoded = _bounded_zlib_decompress(text_value)
                if decoded is None:
                    return False, []
                text_value = decoded
            embedded_text.extend(
                (keyword, language, translated_keyword, text_value)
            )
        offset = chunk_end
        chunk_index += 1
        if seen_iend:
            break
    return bool(seen_ihdr and seen_idat and seen_iend and offset == len(raw)), embedded_text


def _read_ebml_vint(
    raw: bytes, offset: int, *, identifier: bool
) -> tuple[int, int, bool] | None:
    if offset >= len(raw) or raw[offset] == 0:
        return None
    first = raw[offset]
    mask = 0x80
    length = 1
    while length <= 8 and not (first & mask):
        mask >>= 1
        length += 1
    maximum_length = 4 if identifier else 8
    if length > maximum_length or offset + length > len(raw):
        return None
    value = int.from_bytes(raw[offset : offset + length], "big")
    unknown = False
    if not identifier:
        value &= (1 << (7 * length)) - 1
        unknown = value == (1 << (7 * length)) - 1
    return value, length, unknown


def _read_ebml_element(
    raw: bytes, offset: int, limit: int
) -> tuple[int, int, int] | None:
    identifier = _read_ebml_vint(raw, offset, identifier=True)
    if identifier is None:
        return None
    element_id, id_length, _ignored = identifier
    size_value = _read_ebml_vint(raw, offset + id_length, identifier=False)
    if size_value is None:
        return None
    payload_size, size_length, unknown = size_value
    if unknown:
        return None
    payload_start = offset + id_length + size_length
    payload_end = payload_start + payload_size
    if payload_start > limit or payload_end < payload_start or payload_end > limit:
        return None
    return element_id, payload_start, payload_end


def _parse_webm_container(raw: bytes) -> bool:
    offset = 0
    first = True
    segment_seen = False
    header_count = 0
    tracks_seen = False
    cluster_seen = False
    while offset < len(raw):
        element = _read_ebml_element(raw, offset, len(raw))
        if element is None:
            return False
        element_id, payload_start, payload_end = element
        if first and element_id != 0x1A45DFA3:
            return False
        first = False
        if element_id == 0x1A45DFA3:
            header_count += 1
            if header_count > 1:
                return False
        if element_id == 0x18538067:
            if segment_seen:
                return False
            segment_seen = True
            child_offset = payload_start
            while child_offset < payload_end:
                child = _read_ebml_element(raw, child_offset, payload_end)
                if child is None:
                    return False
                child_id, _child_start, child_end = child
                tracks_seen = tracks_seen or child_id == 0x1654AE6B
                cluster_seen = cluster_seen or child_id == 0x1F43B675
                child_offset = child_end
            if child_offset != payload_end:
                return False
        offset = payload_end
    return bool(
        not first
        and offset == len(raw)
        and segment_seen
        and header_count == 1
        and tracks_seen
        and cluster_seen
    )


MP4_CONTAINER_BOXES = frozenset(
    {
        b"moov",
        b"trak",
        b"mdia",
        b"minf",
        b"stbl",
        b"edts",
        b"dinf",
        b"mvex",
        b"moof",
        b"traf",
        b"mfra",
        b"udta",
    }
)


def _parse_mp4_boxes(raw: bytes, start: int, end: int, *, top: bool) -> tuple[bool, list[bytes]]:
    offset = start
    box_types: list[bytes] = []
    while offset < end:
        if end - offset < 8:
            return False, box_types
        size32 = struct.unpack(">I", raw[offset : offset + 4])[0]
        box_type = raw[offset + 4 : offset + 8]
        header_size = 8
        if size32 == 1:
            if end - offset < 16:
                return False, box_types
            box_size = struct.unpack(">Q", raw[offset + 8 : offset + 16])[0]
            header_size = 16
        elif size32 == 0:
            return False, box_types
        else:
            box_size = size32
        box_end = offset + box_size
        if box_size < header_size or box_end < offset or box_end > end:
            return False, box_types
        box_types.append(box_type)
        payload_start = offset + header_size
        if box_type in MP4_CONTAINER_BOXES:
            valid, _children = _parse_mp4_boxes(
                raw, payload_start, box_end, top=False
            )
            if not valid:
                return False, box_types
        if top and not box_types[:-1] and box_type == b"ftyp":
            if box_size < header_size + 8:
                return False, box_types
        offset = box_end
    return offset == end, box_types


def _parse_mp4_container(raw: bytes) -> bool:
    valid, box_types = _parse_mp4_boxes(raw, 0, len(raw), top=True)
    return bool(
        valid
        and box_types
        and box_types[0] == b"ftyp"
        and b"moov" in box_types
        and b"mdat" in box_types
    )


def _visual_magic_matches(kind: Any, path: Path, raw: bytes) -> bool:
    suffix = path.suffix.lower()
    if kind == "screenshot":
        return suffix == ".png" and _parse_png_container(raw)[0]
    if kind == "video":
        return (
            suffix == ".webm" and _parse_webm_container(raw)
        ) or (
            suffix == ".mp4" and _parse_mp4_container(raw)
        )
    return True


def _validate_visual_redaction_manifest(
    data: Mapping[str, Any],
    evidence_by_id: Mapping[str, Mapping[str, Any]],
    repo_root: Path | None,
    problems: Problems,
    location: str,
) -> None:
    visual_items = [
        item
        for item in evidence_by_id.values()
        if item.get("kind") in {"screenshot", "video"}
    ]
    if not visual_items:
        return
    manifest_names = {
        "screenshot": "screenshot-manifest.json",
        "video": "video-manifest.json",
    }
    candidate = _mapping(data.get("identity")).get("productCandidate")
    for visual_kind in {str(item.get("kind")) for item in visual_items}:
        expected_name = manifest_names[visual_kind]
        manifest_matches = [
            item
            for item in evidence_by_id.values()
            if Path(str(item.get("path"))).name == expected_name
            and item.get("kind") in {"json", "manual-attestation"}
        ]
        manifest_evidence = manifest_matches[0] if len(manifest_matches) == 1 else None
        manifest_location = f"{location}#/visual-redaction/{visual_kind}"
        if manifest_evidence is None or repo_root is None:
            problems.add(
                "VISUAL_REDACTION_MANIFEST",
                manifest_location,
                "visual evidence requires its fixed redaction manifest",
            )
            continue
        resolved = _safe_relative_evidence_path(
            repo_root,
            manifest_evidence.get("path"),
            problems,
            manifest_location,
        )
        if resolved is None or not resolved.is_file():
            continue
        document = read_document(repo_root, resolved, problems)
        if document is None or not isinstance(document.data, Mapping):
            continue
        manifest = document.data
        attestation = _mapping(manifest.get("redactionAttestation"))
        if (
            manifest.get("schemaVersion") != SCHEMA_VERSION
            or manifest.get("gateId") != data.get("gateId")
            or manifest.get("productCandidate") != candidate
            or attestation.get("status") != "Passed"
            or attestation.get("reviewedBy") != "GoalTestOperator"
            or attestation.get("secretsVisible") is not False
            or attestation.get("absolutePathsVisible") is not False
        ):
            problems.add(
                "VISUAL_REDACTION_ATTESTATION",
                manifest_location,
                "visual redaction attestation is incomplete or not candidate-bound",
            )
        declared_files = _list(manifest.get("files"))
        expected_files = sorted(
            (
                str(item.get("path")),
                str(item.get("sha256")),
            )
            for item in visual_items
            if item.get("kind") == visual_kind
        )
        relevant_declared_files = [
            item
            for item in declared_files
            if _mapping(item).get("artifactType", visual_kind) == visual_kind
        ]
        actual_files = sorted(
            (
                str(_mapping(item).get("path")),
                str(_mapping(item).get("sha256")),
            )
            for item in relevant_declared_files
            if _mapping(item).get("redacted") is True
        )
        if (
            actual_files != expected_files
            or len(actual_files) != len(relevant_declared_files)
        ):
            problems.add(
                "VISUAL_REDACTION_BINDING",
                manifest_location,
                "visual redaction manifest must bind every visual evidence hash exactly",
            )


def _check_gate_evidence(
    data: Mapping[str, Any],
    location: str,
    repo_root: Path | None,
    problems: Problems,
) -> dict[str, Mapping[str, Any]]:
    evidence_items = _list(data.get("evidence"))
    evidence_by_id: dict[str, Mapping[str, Any]] = {}
    evidence_basename_counts: Counter[str] = Counter()
    evidence_path_counts: Counter[tuple[str, ...]] = Counter()
    for index, evidence in enumerate(evidence_items):
        evidence_location = f"{location}#/evidence/{index}"
        if not isinstance(evidence, Mapping):
            problems.add("EVIDENCE_TYPE", evidence_location, "evidence must be an object")
            continue
        evidence_id = evidence.get("evidenceId")
        if not isinstance(evidence_id, str) or not evidence_id:
            problems.add("EVIDENCE_ID", evidence_location, "evidenceId must be non-empty")
            continue
        if evidence_id in evidence_by_id:
            problems.add(
                "EVIDENCE_ID_DUPLICATE",
                evidence_location,
                "evidenceId must be unique within the Gate",
            )
            continue
        evidence_by_id[evidence_id] = evidence
        normalised_parts = _normalised_repo_parts(evidence.get("path"))
        if normalised_parts:
            evidence_basename_counts[normalised_parts[-1]] += 1
            evidence_path_counts[normalised_parts] += 1
        resolved = _safe_relative_evidence_path(
            repo_root,
            evidence.get("path"),
            problems,
            f"{evidence_location}/path",
        )
        declared_hash = evidence.get("sha256")
        if not isinstance(declared_hash, str) or HEX_SHA256.fullmatch(declared_hash.lower()) is None:
            problems.add("EVIDENCE_SHA256", evidence_location, "sha256 must be a non-null SHA-256")
        if repo_root is not None and resolved is not None:
            if not resolved.is_file():
                problems.add("EVIDENCE_MISSING", evidence_location, "evidence file does not exist")
            else:
                raw_evidence = resolved.read_bytes()
                actual_hash = hashlib.sha256(raw_evidence).hexdigest()
                if not isinstance(declared_hash, str) or actual_hash != declared_hash.lower():
                    problems.add("EVIDENCE_HASH", evidence_location, "evidence SHA-256 does not match file bytes")
                if not _visual_magic_matches(
                    evidence.get("kind"), resolved, raw_evidence
                ):
                    problems.add(
                        "EVIDENCE_VISUAL_FORMAT",
                        evidence_location,
                        "visual evidence extension and file signature do not match an approved format",
                    )
                _scan_text_evidence_for_secrets(
                    resolved,
                    evidence.get("kind"),
                    problems,
                    evidence_location,
                )
                if evidence.get("kind") == "json":
                    evidence_document = read_document(repo_root, resolved, problems)
                    if evidence_document is not None:
                        _scan_value_for_secrets(
                            evidence_document.data,
                            problems,
                            f"{evidence_location}/content",
                        )

    if any(count != 1 for count in evidence_basename_counts.values()):
        problems.add(
            "EVIDENCE_BASENAME_DUPLICATE",
            location,
            "evidence basenames must be unique within a Gate",
        )
    if any(count != 1 for count in evidence_path_counts.values()):
        problems.add(
            "EVIDENCE_PATH_DUPLICATE",
            location,
            "evidence paths must be unique within a Gate",
        )

    _validate_visual_redaction_manifest(
        data,
        evidence_by_id,
        repo_root,
        problems,
        location,
    )

    def check_refs(owner: Any, owner_location: str) -> None:
        if not isinstance(owner, Mapping):
            return
        refs = owner.get("evidenceRefs")
        if refs is None:
            return
        if not isinstance(refs, list):
            problems.add("EVIDENCE_REFS", owner_location, "evidenceRefs must be an array")
            return
        for ref in refs:
            if not isinstance(ref, str) or ref not in evidence_by_id:
                problems.add("EVIDENCE_REF_UNRESOLVED", owner_location, "evidenceRefs must resolve to this Gate's evidenceId")

    for index, command in enumerate(_list(data.get("commands"))):
        check_refs(command, f"{location}#/commands/{index}")
    for index, assertion in enumerate(_list(data.get("acceptanceAssertions"))):
        check_refs(assertion, f"{location}#/acceptanceAssertions/{index}")
    check_refs(data.get("cleanup"), f"{location}#/cleanup")
    check_refs(data.get("firstFailure"), f"{location}#/firstFailure")

    first_failure = data.get("firstFailure")
    preserving = {
        evidence_id
        for evidence_id, evidence in evidence_by_id.items()
        if evidence.get("preservesFirstFailure") is True
    }
    if isinstance(first_failure, Mapping):
        failure_refs = set(_list(first_failure.get("evidenceRefs")))
        if first_failure.get("preserved") is not True or not (failure_refs & preserving):
            problems.add("FIRST_FAILURE_PRESERVATION", location, "firstFailure must reference preserving evidence")
    elif preserving:
        problems.add("FIRST_FAILURE_ORPHANED", location, "preserving evidence exists but firstFailure is null")
    return evidence_by_id


def validate_gate(
    document: Document,
    contract: Contract,
    problems: Problems,
    repo_root: Path | None = None,
) -> None:
    data = document.data
    location = document.relative_path
    if not isinstance(data, Mapping):
        problems.add("GATE_TYPE", location, "Gate result must be an object")
        return
    gate_id = data.get("gateId")
    group = contract.gate_to_group.get(gate_id) if isinstance(gate_id, str) else None
    if group is None:
        problems.add("GATE_ID", location, "Gate ID is not in the frozen registry")
        _scan_value_for_secrets(data, problems, f"{location}#")
        return
    if data.get("week") != group.week:
        problems.add("GATE_WEEK", location, f"{gate_id} must use week {group.week}")
    if data.get("checkpoint") != group.checkpoint:
        problems.add("GATE_CHECKPOINT", location, f"{gate_id} must use {group.checkpoint}")
    if data.get("lane") != group.lane:
        problems.add("GATE_LANE", location, f"{gate_id} must use lane {group.lane}")
    if Path(location).name != f"{gate_id}.json":
        problems.add("GATE_FILENAME", location, f"filename must be {gate_id}.json")
    if not _result_path_matches_document(document, data.get("resultPath")):
        problems.add("GATE_RESULT_PATH", location, "resultPath does not name this file")
    if group.result_pattern and isinstance(data.get("resultPath"), str):
        try:
            if re.fullmatch(group.result_pattern, data["resultPath"]) is None:
                problems.add("GATE_PATH_POLICY", location, "resultPath is outside its frozen group path")
        except re.error:
            problems.add("SCHEMA_PATTERN", location, "invalid frozen schema resultPath pattern")

    _scan_value_for_secrets(data, problems, f"{location}#")
    evidence_by_id = _check_gate_evidence(data, location, repo_root, problems)
    if data.get("status") == "Passed" and not evidence_by_id:
        problems.add("EVIDENCE_REQUIRED", location, "Passed Gate requires hashed evidence")

    status = data.get("status")
    required = data.get("requiredGate") is True
    if status == "NotApplicable" and required:
        problems.add("REQUIRED_NOT_APPLICABLE", location, "required Gate cannot be NotApplicable")

    commands = _list(data.get("commands"))
    command_ids: set[str] = set()
    for index, command in enumerate(commands):
        command_location = f"{location}#/commands/{index}"
        if not isinstance(command, Mapping):
            problems.add("COMMAND_TYPE", command_location, "command must be an object")
            continue
        command_id = command.get("commandId")
        if not isinstance(command_id, str) or not command_id:
            problems.add("COMMAND_ID", command_location, "commandId must be non-empty")
        elif command_id in command_ids:
            problems.add("COMMAND_ID_DUPLICATE", command_location, "commandId must be unique within the Gate")
        else:
            command_ids.add(command_id)
        command_status = command.get("status")
        exit_code = command.get("exitCode")
        if command_status == "Passed" and exit_code != 0:
            problems.add("COMMAND_EXIT", command_location, "Passed command requires exitCode 0")
        if command_status == "Failed" and (not _is_int(exit_code) or exit_code == 0):
            problems.add("COMMAND_EXIT", command_location, "Failed command requires a non-zero exitCode")
        if command_status in {"NotRun", "NotApplicable"} and exit_code is not None:
            problems.add("COMMAND_EXIT", command_location, f"{command_status} command requires null exitCode")

    command_counts = data.get("commandCounts")
    _check_balanced_counts(
        problems,
        f"{location}#/commandCounts",
        command_counts,
        "total",
        ("passed", "failed", "notRun", "notApplicable"),
    )
    if isinstance(command_counts, Mapping):
        observed = _status_counter(item for item in commands if isinstance(item, Mapping))
        expected = {
            "total": len(commands),
            "passed": observed["Passed"],
            "failed": observed["Failed"],
            "notRun": observed["NotRun"],
            "notApplicable": observed["NotApplicable"],
        }
        for key, value in expected.items():
            if command_counts.get(key) != value:
                problems.add(
                    "COMMAND_COUNTS_ACTUAL",
                    f"{location}#/commandCounts",
                    f"{key}={command_counts.get(key)!r}, actual {value}",
                )
    if status == "Passed":
        passed_commands = [
            command
            for command in commands
            if isinstance(command, Mapping) and command.get("status") == "Passed"
        ]
        if not passed_commands:
            problems.add(
                "PASSED_GATE_COMMAND_REQUIRED",
                location,
                "every Passed required Gate must include at least one Passed command",
            )

    _check_balanced_counts(
        problems,
        f"{location}#/testCounts",
        data.get("testCounts"),
        "discovered",
        ("passed", "failed", "skipped", "notRun", "notApplicable"),
    )

    assertions = _list(data.get("acceptanceAssertions"))
    assertion_ids: set[str] = set()
    assertion_statuses: list[Any] = []
    for index, assertion in enumerate(assertions):
        assertion_location = f"{location}#/acceptanceAssertions/{index}"
        if not isinstance(assertion, Mapping):
            problems.add("ASSERTION_TYPE", assertion_location, "assertion must be an object")
            continue
        assertion_id = assertion.get("assertionId")
        if not isinstance(assertion_id, str) or not assertion_id:
            problems.add("ASSERTION_ID", assertion_location, "assertionId must be non-empty")
        elif assertion_id in assertion_ids:
            problems.add("ASSERTION_ID_DUPLICATE", assertion_location, "assertionId must be unique within the Gate")
        else:
            assertion_ids.add(assertion_id)
        assertion_statuses.append(assertion.get("status"))
    if status == "Passed" and any(item != "Passed" for item in assertion_statuses):
        problems.add("ASSERTION_STATUS", location, "Passed Gate requires every assertion Passed")
    if status == "Failed" and "Failed" not in assertion_statuses:
        problems.add("ASSERTION_STATUS", location, "Failed Gate requires a Failed assertion")
    if status in {"NotRun", "NotApplicable"} and any(
        item != status for item in assertion_statuses
    ):
        problems.add("ASSERTION_STATUS", location, f"{status} Gate requires all assertions {status}")

    ready = status == "Passed"
    _check_cleanup(problems, f"{location}#/cleanup", data.get("cleanup"), ready=False)
    if ready:
        cleanup = _mapping(data.get("cleanup"))
        if cleanup.get("status") not in {"Passed", "NotApplicable"}:
            problems.add("GATE_CLEANUP", location, "Passed Gate requires Passed/NotApplicable cleanup")
        for field in (
            "ownedProcessesRemaining",
            "ownedTempPathsRemaining",
            "configMutationsRemaining",
        ):
            if cleanup.get(field) != 0:
                problems.add("GATE_CLEANUP", location, f"Passed Gate requires {field}=0")
    _check_p0_p1(problems, f"{location}#/openIssues", data.get("openIssues"), ready)

    authorization = _mapping(data.get("authorization"))
    before = authorization.get("providerTurnsBefore")
    consumed = authorization.get("providerTurnsConsumed")
    after = authorization.get("providerTurnsAfter")
    seq_before = authorization.get("providerSequenceBefore")
    seq_after = authorization.get("providerSequenceAfter")
    if all(_is_int(item) for item in (before, consumed, after)):
        if before + consumed != after:
            problems.add("PROVIDER_ARITHMETIC", location, "providerTurnsBefore + consumed must equal after")
    else:
        problems.add("PROVIDER_ARITHMETIC", location, "provider turn fields must be integers")
    if all(_is_int(item) for item in (seq_before, consumed, seq_after)):
        if seq_before + consumed != seq_after:
            problems.add("PROVIDER_SEQUENCE", location, "provider sequence delta must equal consumed turns")
    else:
        problems.add("PROVIDER_SEQUENCE", location, "provider sequence fields must be integers")
    if authorization.get("providerTurnBudget") != PROVIDER_BUDGET:
        problems.add("PROVIDER_BUDGET", location, "providerTurnBudget must be 120")

    write_used = authorization.get("controlledWriteUsed") is True
    write_count = authorization.get("controlledWriteExecutionCount")
    allowed_gate = gate_id in CONTROLLED_WRITE_GATES.values()
    if write_used and not allowed_gate:
        problems.add("CONTROLLED_WRITE_SCOPE", location, "controlled write is allowed only at W84-G8/W92-G7")
    if write_used:
        if write_count != 1:
            problems.add("CONTROLLED_WRITE_COUNT", location, "used controlled write requires executionCount=1")
        if authorization.get("controlledWriteShapeVerified") is not True:
            problems.add("CONTROLLED_WRITE_SHAPE", location, "controlled write shape was not verified")
        if authorization.get("controlledWriteNonWritePreconditionsPassed") is not True:
            problems.add("CONTROLLED_WRITE_PRECONDITION", location, "non-write preconditions were not Passed")
        if not _list(authorization.get("controlledWriteEvidenceRefs")):
            problems.add("CONTROLLED_WRITE_EVIDENCE", location, "controlled write evidence is missing")
    else:
        if write_count != 0:
            problems.add("CONTROLLED_WRITE_COUNT", location, "unused controlled write requires executionCount=0")
        if _list(authorization.get("controlledWriteEvidenceRefs")):
            problems.add("CONTROLLED_WRITE_EVIDENCE", location, "unused controlled write cannot have evidence refs")
    if allowed_gate and status == "Passed" and not write_used:
        problems.add("CONTROLLED_WRITE_REQUIRED", location, f"Passed {gate_id} must execute its controlled write")


def _normalised_repo_parts(value: Any) -> tuple[str, ...] | None:
    if not isinstance(value, str) or not value:
        return None
    candidate = PurePosixPath(value.replace("\\", "/"))
    if candidate.is_absolute() or ".." in candidate.parts:
        return None
    return candidate.parts


def _payload_count_container(payload: Mapping[str, Any]) -> Mapping[str, Any] | None:
    candidates = (
        payload.get("testCounts"),
        payload.get("counts"),
        payload.get("summary"),
        payload,
    )
    for candidate in candidates:
        if not isinstance(candidate, Mapping):
            continue
        if (
            ("discovered" in candidate or "total" in candidate)
            and "passed" in candidate
            and "failed" in candidate
        ):
            return candidate
    return None


def _normalise_payload_test_counts(
    counts: Mapping[str, Any],
    location: str,
    problems: Problems,
) -> dict[str, int] | None:
    discovered = counts.get("discovered", counts.get("total"))
    values = {
        "discovered": discovered,
        "passed": counts.get("passed"),
        "failed": counts.get("failed"),
        "skipped": counts.get("skipped", 0),
        "notRun": counts.get("notRun", 0),
        "notApplicable": counts.get("notApplicable", 0),
    }
    if any(not _is_int(value) or value < 0 for value in values.values()):
        problems.add(
            "REQUIREMENTS_EVIDENCE_TEST_REPORT_SHAPE",
            location,
            "test-report counts must be non-negative integers",
        )
        return None
    expected_total = sum(
        values[key]
        for key in ("passed", "failed", "skipped", "notRun", "notApplicable")
    )
    if values["discovered"] != expected_total:
        problems.add(
            "REQUIREMENTS_EVIDENCE_TEST_REPORT_SHAPE",
            location,
            "test-report discovered/total does not balance with result categories",
        )
        return None
    if values["failed"] != 0 or values["notRun"] != 0:
        problems.add(
            "REQUIREMENTS_EVIDENCE_TEST_REPORT_STATUS",
            location,
            "Passed Gate test-report must have failed=0 and notRun=0",
        )
        return None
    return values


def _expected_semantic_runtime_binding(
    repo_root: Path,
    product_candidate: Any,
    relative_path: str,
    problems: Problems,
    location: str,
) -> dict[str, str] | None:
    path = _safe_relative_evidence_path(
        repo_root, relative_path, problems, location
    )
    try:
        raw = path.read_bytes() if path is not None else None
    except OSError:
        raw = None
    candidate_raw = (
        _git_bytes(
            repo_root,
            ("show", f"{product_candidate}:{relative_path}"),
        )
        if isinstance(product_candidate, str)
        else None
    )
    blob = (
        _git_stdout(
            repo_root,
            ("rev-parse", f"{product_candidate}:{relative_path}"),
        )
        if isinstance(product_candidate, str)
        else None
    )
    control_revision = _single_add_commit(repo_root, relative_path)
    if (
        raw is None
        or candidate_raw != raw
        or not isinstance(blob, str)
        or re.fullmatch(r"[0-9a-f]{40}", blob) is None
        or control_revision is None
        or _git_return_code(
            repo_root,
            (
                "merge-base",
                "--is-ancestor",
                control_revision,
                str(product_candidate),
            ),
        )
        != 0
        or _git_bytes(repo_root, ("show", f"HEAD:{relative_path}")) != raw
        or _git_bytes(repo_root, ("show", f":{relative_path}")) != raw
        or _git_mode(repo_root, ("ls-tree", "HEAD", "--", relative_path))
        != "100644"
        or _git_mode(
            repo_root, ("ls-files", "--stage", "--", relative_path)
        )
        != "100644"
    ):
        problems.add(
            "TEST_REPORT_SEMANTIC_SOURCE",
            location,
            "semantic source must equal its candidate, HEAD, index, and immutable control blob",
        )
        return None
    return {
        "path": relative_path,
        "sha256": hashlib.sha256(raw).hexdigest(),
        "gitBlobSha": blob,
        "controlRevision": control_revision,
    }


def _validate_semantic_sources(
    semantic_sources: Any,
    descriptor_sha256: Any,
    trusted_test_policy: Mapping[str, Any],
    product_candidate: Any,
    repo_root: Path,
    problems: Problems,
    location: str,
) -> bool:
    if not isinstance(semantic_sources, Mapping) or set(semantic_sources) != {
        "entrySources",
        "readSources",
        "bootstrapSources",
    }:
        problems.add(
            "TEST_REPORT_SEMANTIC_SOURCES",
            location,
            "semanticSources must use the exact frozen three-list envelope",
        )
        return False
    declared_entries = _list(trusted_test_policy.get("entrySources"))
    declared_reads = _list(trusted_test_policy.get("readSources"))
    expected_entries: list[dict[str, str]] = []
    for index, expected in enumerate(
        getattr(trusted_executor, "SEMANTIC_ENTRY_SOURCES", ())
    ):
        if index >= len(declared_entries):
            continue
        policy_item = _mapping(declared_entries[index])
        module_name, relative_path = expected
        binding = _expected_semantic_runtime_binding(
            repo_root,
            product_candidate,
            relative_path,
            problems,
            f"{location}/entrySources/{index}",
        )
        if binding is not None:
            expected_entries.append(
                {"moduleName": module_name, **binding}
            )
        if (
            policy_item
            != {
                "moduleName": module_name,
                "path": relative_path,
                "sha256": binding.get("sha256") if binding else None,
            }
        ):
            problems.add(
                "TEST_REPORT_SEMANTIC_POLICY_SOURCE",
                f"{location}/entrySources/{index}",
                "trusted-test entry source policy differs from current candidate bytes",
            )
    expected_reads: list[dict[str, str]] = []
    for index, relative_path in enumerate(
        getattr(trusted_executor, "SEMANTIC_READ_SOURCES", ())
    ):
        if index >= len(declared_reads):
            continue
        policy_item = _mapping(declared_reads[index])
        binding = _expected_semantic_runtime_binding(
            repo_root,
            product_candidate,
            relative_path,
            problems,
            f"{location}/readSources/{index}",
        )
        if binding is not None:
            expected_reads.append(binding)
        if policy_item != {
            "path": relative_path,
            "sha256": binding.get("sha256") if binding else None,
        }:
            problems.add(
                "TEST_REPORT_SEMANTIC_POLICY_SOURCE",
                f"{location}/readSources/{index}",
                "trusted-test read source policy differs from current candidate bytes",
            )
    expected_bootstrap: list[dict[str, str]] = []
    bootstrap_paths = tuple(
        getattr(trusted_executor, "SEMANTIC_BOOTSTRAP_SOURCES", ())
    )
    if bootstrap_paths != BOOTSTRAP_CONTROL_PATHS:
        problems.add(
            "TEST_REPORT_SEMANTIC_BOOTSTRAP_POLICY",
            location,
            "executor and validator bootstrap source orders must be identical",
        )
    for index, relative_path in enumerate(bootstrap_paths):
        binding = _expected_semantic_runtime_binding(
            repo_root,
            product_candidate,
            relative_path,
            problems,
            f"{location}/bootstrapSources/{index}",
        )
        if binding is not None:
            expected_bootstrap.append(binding)
    expected_sources = {
        "entrySources": expected_entries,
        "readSources": expected_reads,
        "bootstrapSources": expected_bootstrap,
    }
    descriptor = {
        "protocol": trusted_test_policy.get("loaderProtocol"),
        "repoRoot": str(repo_root.resolve()),
        "entrySources": [
            {
                "moduleName": item["moduleName"],
                "path": item["path"],
                "sha256": item["sha256"],
            }
            for item in expected_entries
        ],
        "readSources": [
            {"path": item["path"], "sha256": item["sha256"]}
            for item in expected_reads
        ],
    }
    valid = (
        len(declared_entries) == len(expected_entries)
        and len(declared_reads) == len(expected_reads)
        and semantic_sources == expected_sources
        and descriptor_sha256
        == hashlib.sha256(_json_bytes(descriptor)).hexdigest()
    )
    if not valid:
        problems.add(
            "TEST_REPORT_SEMANTIC_SOURCES",
            location,
            "semantic report must bind exact loaded/read/bootstrap sources and loader descriptor",
        )
    return valid


def _validate_semantic_dependency_tree(
    value: Any,
    repo_root: Path,
    problems: Problems,
    location: str,
) -> bool:
    try:
        current, _entries = (
            trusted_executor._semantic_site_dependency_inventory(repo_root)
        )
    except (trusted_executor.ExecutorError, OSError):
        current = None
    dependency = _mapping(value)
    loaded = _mapping(dependency.get("loadedFiles"))
    expected_allowed = [
        {"path": path, "kind": kind}
        for path, kind in getattr(
            trusted_executor, "SEMANTIC_SITE_ALLOWED_ROOTS", ()
        )
    ]
    valid = (
        set(dependency)
        == {
            "mode",
            "rootLocationRole",
            "contentRootAlgorithm",
            "allowedRoots",
            "before",
            "after",
            "matchesBefore",
            "loadedFiles",
        }
        and dependency.get("mode") == "isolated-s-explicit-purelib-allowlist"
        and dependency.get("rootLocationRole")
        == "sysconfig-purelib-outside-repository"
        and dependency.get("contentRootAlgorithm")
        == getattr(trusted_executor, "SEMANTIC_SITE_TREE_ALGORITHM", None)
        and dependency.get("allowedRoots") == expected_allowed
        and current is not None
        and dependency.get("before") == current
        and dependency.get("after") == current
        and dependency.get("matchesBefore") is True
        and set(loaded) == {"entryCount", "totalBytes", "contentRootSha256"}
        and _is_int(loaded.get("entryCount"))
        and 0 < loaded.get("entryCount", 0) <= current.get("entryCount", -1)
        and _is_int(loaded.get("totalBytes"))
        and 0 < loaded.get("totalBytes", 0) <= current.get("totalBytes", -1)
        and isinstance(loaded.get("contentRootSha256"), str)
        and HEX_SHA256.fullmatch(str(loaded.get("contentRootSha256")))
        is not None
    )
    if not valid:
        problems.add(
            "TEST_REPORT_SEMANTIC_DEPENDENCY_TREE",
            location,
            "semantic dependency tree must match the current explicit purelib allowlist inventory",
        )
    return valid


def _expected_git_tool_identity_record(
    trusted_git_policy: Mapping[str, Any],
    repo_root: Path,
) -> dict[str, Any] | None:
    try:
        _path, current = trusted_executor._trusted_git_identity(repo_root)
    except (trusted_executor.ExecutorError, OSError):
        return None
    return {
        "policyId": TRUSTED_GIT_POLICY_ID,
        "policySha256": hashlib.sha256(
            _json_bytes(trusted_git_policy)
        ).hexdigest(),
        "before": current,
        "after": current,
        "matchesBefore": True,
    }


def _validate_frozen_git_tool_identity(
    value: Any,
    trusted_git_policy: Mapping[str, Any],
    repo_root: Path,
    problems: Problems,
    location: str,
    code: str,
) -> bool:
    record = _mapping(value)
    expected = _expected_git_tool_identity_record(
        trusted_git_policy, repo_root
    )
    valid = record == expected
    if not valid:
        problems.add(
            code,
            location,
            "artifact must bind one unchanged frozen Git policy and executable identity",
        )
    return valid


def _validate_semantic_git_tool_identity(
    value: Any,
    trusted_git_policy: Mapping[str, Any],
    repo_root: Path,
    problems: Problems,
    location: str,
) -> bool:
    return _validate_frozen_git_tool_identity(
        value,
        trusted_git_policy,
        repo_root,
        problems,
        location,
        "TEST_REPORT_GIT_TOOL_IDENTITY",
    )


def _validate_goal_state_post_semantic_transition(
    snapshot_goal: Mapping[str, Any],
    gate: Mapping[str, Any],
    repo_root: Path,
    problems: Problems,
    location: str,
) -> bool:
    """Accept only the orchestrator's exact one-Passed-Gate Goal update."""

    goal_path = _safe_relative_evidence_path(
        repo_root,
        trusted_executor.GOAL_STATE_PATH,
        problems,
        f"{location}/runtimeInputTree/liveGoalState",
    )
    live_raw: bytes | None = None
    live_goal: Any = None
    if goal_path is not None and goal_path.is_file():
        try:
            live_raw = goal_path.read_bytes()
            live_goal = json.loads(
                live_raw.decode("utf-8"),
                object_pairs_hook=_no_duplicate_object,
            )
        except (
            OSError,
            UnicodeDecodeError,
            json.JSONDecodeError,
            DuplicateJsonKey,
        ):
            live_raw = None
            live_goal = None
    if (
        live_raw is None
        or not isinstance(live_goal, Mapping)
        or live_raw != _json_bytes(live_goal)
        or trusted_executor._contains_secret(live_raw.decode("utf-8"))
        or gate.get("status") != "Passed"
    ):
        problems.add(
            "TEST_REPORT_GOAL_STATE_TRANSITION",
            location,
            "live Goal state must be canonical, secret-free, and reflect one Passed Gate",
        )
        return False

    gate_id = gate.get("gateId")
    gate_rollup_keys = {
        "total",
        "passed",
        "failed",
        "notRun",
        "notApplicable",
        "countsBalanced",
    }
    command_rollup_keys = gate_rollup_keys
    test_rollup_keys = {
        "discovered",
        "passed",
        "failed",
        "skipped",
        "notRun",
        "notApplicable",
        "countsBalanced",
    }
    before_gate = _mapping(snapshot_goal.get("gateRollup"))
    after_gate = _mapping(live_goal.get("gateRollup"))
    before_command = _mapping(snapshot_goal.get("commandRollup"))
    after_command = _mapping(live_goal.get("commandRollup"))
    before_test = _mapping(snapshot_goal.get("testRollup"))
    after_test = _mapping(live_goal.get("testRollup"))
    gate_commands = _mapping(gate.get("commandCounts"))
    gate_tests = _mapping(gate.get("testCounts"))

    def balanced(mapping: Mapping[str, Any], expected: set[str]) -> bool:
        return (
            set(mapping) == expected
            and mapping.get("countsBalanced") is True
            and all(
                _is_int(mapping.get(key)) and mapping.get(key, -1) >= 0
                for key in expected - {"countsBalanced"}
            )
        )

    transition_valid = all(
        (
            balanced(before_gate, gate_rollup_keys),
            balanced(after_gate, gate_rollup_keys),
            balanced(before_command, command_rollup_keys),
            balanced(after_command, command_rollup_keys),
            balanced(before_test, test_rollup_keys),
            balanced(after_test, test_rollup_keys),
            balanced(gate_commands, command_rollup_keys),
            balanced(gate_tests, test_rollup_keys),
        )
    )
    if transition_valid:
        expected_gate = dict(before_gate)
        expected_gate["passed"] += 1
        expected_gate["notRun"] -= 1
        expected_command = {
            key: (
                True
                if key == "countsBalanced"
                else before_command[key] + gate_commands[key]
            )
            for key in command_rollup_keys
        }
        expected_test = {
            key: (
                True
                if key == "countsBalanced"
                else before_test[key] + gate_tests[key]
            )
            for key in test_rollup_keys
        }
        transition_valid = (
            expected_gate == after_gate
            and expected_command == after_command
            and expected_test == after_test
            and live_goal.get("updatedAt") == gate.get("finishedAt")
        )

    # All fields outside the five common orchestrator update roots are byte-
    # semantic invariants.  W84-G0 alone additionally advances the W84 entry
    # projection; replacing that one value before comparison makes every
    # unknown JSON-pointer difference fail closed.
    before_invariants = json.loads(_json_bytes(snapshot_goal).decode("utf-8"))
    after_invariants = json.loads(_json_bytes(live_goal).decode("utf-8"))
    for field in (
        "gateRollup",
        "commandRollup",
        "testRollup",
        "nextAction",
        "updatedAt",
    ):
        before_invariants.pop(field, None)
        after_invariants.pop(field, None)
    if gate_id == "W84-G0":
        before_entry = _mapping(
            _mapping(_mapping(snapshot_goal.get("execution")).get("weekStates")).get(
                "W84"
            )
        ).get("entryGate")
        after_entry = _mapping(
            _mapping(_mapping(live_goal.get("execution")).get("weekStates")).get(
                "W84"
            )
        ).get("entryGate")
        try:
            before_invariants["execution"]["weekStates"]["W84"][
                "entryGate"
            ] = after_entry
        except (KeyError, TypeError):
            transition_valid = False
        transition_valid = transition_valid and (
            before_entry == "NotRun"
            and after_entry == "Passed"
            and before_gate
            == {
                "total": 104,
                "passed": 0,
                "failed": 0,
                "notRun": 104,
                "notApplicable": 0,
                "countsBalanced": True,
            }
            and after_gate
            == {
                "total": 104,
                "passed": 1,
                "failed": 0,
                "notRun": 103,
                "notApplicable": 0,
                "countsBalanced": True,
            }
            and before_command
            == {
                "total": 0,
                "passed": 0,
                "failed": 0,
                "notRun": 0,
                "notApplicable": 0,
                "countsBalanced": True,
            }
            and before_test
            == {
                "discovered": 0,
                "passed": 0,
                "failed": 0,
                "skipped": 0,
                "notRun": 0,
                "notApplicable": 0,
                "countsBalanced": True,
            }
            and live_goal.get("nextAction")
            == {
                "kind": "RunGate",
                "owner": "GoalAgent",
                "checkpoint": "W84",
                "summary": "Run W84-G1 deterministic listener baseline.",
                "blockedBy": [],
            }
        )
    elif gate_id == "W92-G8":
        transition_valid = transition_valid and live_goal.get("nextAction") == {
            "kind": "AwaitUserConfirmation",
            "owner": "User",
            "checkpoint": "W92",
            "summary": "Await W92-G9 final visual acceptance.",
            "blockedBy": ["W92-G9"],
        }
    transition_valid = transition_valid and before_invariants == after_invariants
    if not transition_valid:
        problems.add(
            "TEST_REPORT_GOAL_STATE_TRANSITION",
            location,
            "snapshot to live Goal state must be the exact one-Gate NotRun-to-Passed orchestrator transition",
        )
    return transition_valid


def _validate_semantic_runtime_input_tree(
    value: Any,
    payload: Mapping[str, Any],
    payload_document: Document,
    gate: Mapping[str, Any],
    repo_root: Path,
    problems: Problems,
    location: str,
) -> bool:
    """Reconstruct the sealed semantic artifact preimage from its snapshot.

    The trusted child runs before the Gate result and the W84 bootstrap
    summaries exist.  Final validation therefore cannot hash the live tree
    directly: it must exclude those post-semantic outputs and virtually bind
    the central Goal file to the exclusive pre-child snapshot.  The executor's
    inventory routine performs the same lexical, reparse, hard-link, race,
    size, path, and sensitive-name checks while accepting only that one
    canonical Goal-state byte override.
    """

    tree = _mapping(value)
    expected_keys = {
        "mode",
        "contentRootAlgorithm",
        "scopedRoots",
        "excludedPaths",
        "preGoalStateSnapshot",
        "postSemanticOutputs",
        "before",
        "after",
        "matchesBefore",
    }
    command_id = payload.get("commandId")
    attempt_id = payload.get("attemptId")
    gate_id = gate.get("gateId")
    result_path = gate.get("resultPath")
    stream_paths = [
        _mapping(payload.get(stream_name)).get("path")
        for stream_name in ("stdout", "stderr")
    ]
    valid = True
    try:
        post_output_paths = trusted_executor._semantic_post_output_paths(
            gate_id=str(gate_id),
            result_path=str(result_path),
        )
    except (trusted_executor.ExecutorError, TypeError, ValueError):
        post_output_paths = ()
        valid = False
    report_parent = PurePosixPath(payload_document.relative_path).parent
    expected_snapshot_path = (
        report_parent
        / f"{command_id}.{attempt_id}.pre-goal-state.json"
    ).as_posix()
    raw_exclusions = [
        payload_document.relative_path,
        *stream_paths,
        *post_output_paths,
    ]
    if any(not isinstance(item, str) or not item for item in raw_exclusions):
        expected_exclusions: list[str] = []
        valid = False
    else:
        try:
            expected_exclusions = sorted(
                [
                    PurePosixPath(*_normalised_repo_parts(item)).as_posix()
                    for item in raw_exclusions
                    if _normalised_repo_parts(item) is not None
                ],
                key=lambda item: item.encode("utf-8"),
            )
        except (TypeError, UnicodeEncodeError):
            expected_exclusions = []
            valid = False
        if len(expected_exclusions) != len(raw_exclusions):
            valid = False

    snapshot = _mapping(tree.get("preGoalStateSnapshot"))
    snapshot_path = _safe_relative_evidence_path(
        repo_root,
        snapshot.get("path"),
        problems,
        f"{location}/runtimeInputTree/preGoalStateSnapshot/path",
    )
    snapshot_raw: bytes | None = None
    snapshot_document: Any = None
    if snapshot_path is not None and snapshot_path.is_file():
        try:
            snapshot_raw = snapshot_path.read_bytes()
            snapshot_document = json.loads(
                snapshot_raw.decode("utf-8"),
                object_pairs_hook=_no_duplicate_object,
            )
        except (
            OSError,
            UnicodeDecodeError,
            json.JSONDecodeError,
            DuplicateJsonKey,
        ):
            snapshot_raw = None
            snapshot_document = None
    snapshot_valid = bool(
        set(snapshot) == {"path", "bytes", "sha256", "stableDuringChild"}
        and snapshot.get("path") == expected_snapshot_path
        and snapshot.get("stableDuringChild") is True
        and snapshot_raw is not None
        and isinstance(snapshot_document, Mapping)
        and snapshot_raw == _json_bytes(snapshot_document)
        and snapshot.get("bytes") == len(snapshot_raw)
        and snapshot.get("sha256")
        == hashlib.sha256(snapshot_raw).hexdigest()
        and not trusted_executor._contains_secret(snapshot_raw.decode("utf-8"))
    )
    if not snapshot_valid:
        problems.add(
            "TEST_REPORT_GOAL_STATE_PREIMAGE",
            location,
            "semantic runtime tree must bind the exact canonical secret-free exclusive pre-Goal snapshot",
        )
        valid = False
    elif not _validate_goal_state_post_semantic_transition(
        snapshot_document,
        gate,
        repo_root,
        problems,
        location,
    ):
        valid = False

    expected_post_outputs = [
        {
            "path": path,
            "mode": "exclusive-create-after-semantic",
            "before": {"exists": False, "bytes": None, "sha256": None},
            "stableDuringChild": True,
        }
        for path in post_output_paths
    ]
    if tree.get("postSemanticOutputs") != expected_post_outputs:
        problems.add(
            "TEST_REPORT_POST_SEMANTIC_OUTPUTS",
            location,
            "semantic report must declare the exact absent-and-stable post-semantic output set",
        )
        valid = False
    for index, path in enumerate(post_output_paths):
        resolved = _safe_relative_evidence_path(
            repo_root,
            path,
            problems,
            f"{location}/runtimeInputTree/postSemanticOutputs/{index}/path",
        )
        if resolved is None or not resolved.is_file():
            problems.add(
                "TEST_REPORT_POST_SEMANTIC_OUTPUT_MISSING",
                location,
                "every declared post-semantic output must exist at final validation",
            )
            valid = False

    inventory_roots: Any = None
    inventory_summary: Any = None
    if snapshot_valid:
        try:
            inventory_roots, inventory_summary = (
                trusted_executor._artifact_input_tree_inventory(
                    repo_root,
                    excluded_paths=expected_exclusions,
                    content_overrides={
                        trusted_executor.GOAL_STATE_PATH: snapshot_raw
                    },
                )
            )
        except (trusted_executor.ExecutorError, OSError, TypeError, ValueError):
            inventory_roots = None
            inventory_summary = None
    if (
        set(tree) != expected_keys
        or tree.get("mode")
        != getattr(trusted_executor, "ARTIFACT_INPUT_TREE_MODE", None)
        or tree.get("contentRootAlgorithm")
        != getattr(trusted_executor, "ARTIFACT_INPUT_TREE_ALGORITHM", None)
        or tree.get("excludedPaths") != expected_exclusions
        or tree.get("scopedRoots") != inventory_roots
        or tree.get("before") != inventory_summary
        or tree.get("after") != inventory_summary
        or tree.get("matchesBefore") is not True
    ):
        problems.add(
            "TEST_REPORT_RUNTIME_INPUT_TREE",
            location,
            "semantic report must exactly match the independently reconstructed frozen artifact preimage",
        )
        valid = False
    return valid


def _parse_frozen_unittest_stdout(raw: bytes) -> dict[str, int] | None:
    """Derive counts from the one canonical frozen TextTestRunner footer.

    The semantic wrapper uses ``TextTestRunner(stream=sys.stdout, verbosity=1)``.
    Its stdout may contain the validator's own output before the test progress,
    but it must end in exactly one canonical unittest separator/result block.
    Caller-supplied report fields are deliberately not consulted here.
    """

    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        return None
    # TextTestRunner writes newline-delimited text.  Accept the two byte forms
    # produced by the frozen runner on supported platforms, but do not turn a
    # bare carriage return into a line boundary: doing so would let caller-made
    # bytes become a valid-looking unittest footer only after parsing.
    if "\r" in text.replace("\r\n", ""):
        return None
    text = text.replace("\r\n", "\n")
    matches = list(_FROZEN_UNITTEST_RESULT.finditer(text))
    if len(matches) != 1:
        return None
    match = matches[0]
    if (
        text.splitlines().count(FROZEN_UNITTEST_SEPARATOR) != 1
        or len(_UNITTEST_RAN_CANDIDATE.findall(text)) != 1
        or len(_UNITTEST_RESULT_CANDIDATE.findall(text)) != 1
    ):
        return None
    discovered = int(match.group(1))
    noun = match.group(2)
    if discovered <= 0 or noun != ("test" if discovered == 1 else "tests"):
        return None
    skipped_raw = match.group(5)
    skipped = int(skipped_raw) if skipped_raw is not None else 0
    if (skipped_raw is not None and skipped == 0) or skipped > discovered:
        return None
    return {
        "discovered": discovered,
        "passed": discovered - skipped,
        "failed": 0,
        "skipped": skipped,
        "notRun": 0,
        "notApplicable": 0,
    }


def _validate_frozen_unittest_report_counts(
    payload: Mapping[str, Any],
    stdout_raw: bytes,
    problems: Problems,
    location: str,
) -> bool:
    derived_counts = _parse_frozen_unittest_stdout(stdout_raw)
    if derived_counts is None:
        problems.add(
            "TEST_REPORT_STDOUT_FORMAT",
            f"{location}/stdout",
            "stdout must end in one exact frozen unittest success result",
        )
        return False
    declared_counts = payload.get("testCounts")
    if (
        payload.get("countsSource") != "python-unittest-output"
        or not isinstance(declared_counts, Mapping)
        or set(declared_counts) != set(TEST_REPORT_COUNT_KEYS)
        or any(
            not _is_int(declared_counts.get(key))
            for key in TEST_REPORT_COUNT_KEYS
        )
        or {
            key: declared_counts.get(key) for key in TEST_REPORT_COUNT_KEYS
        }
        != derived_counts
    ):
        problems.add(
            "TEST_REPORT_STDOUT_COUNTS",
            f"{location}/testCounts",
            "testCounts must exactly equal counts independently derived from bound stdout",
        )
        return False
    return True


def _validate_test_report_provenance(
    payload: Mapping[str, Any],
    payload_document: Document,
    gate: Mapping[str, Any],
    repo_root: Path,
    problems: Problems,
    location: str,
    trusted_runner_policy: Mapping[str, Any],
    trusted_test_policy: Mapping[str, Any],
    trusted_git_policy: Mapping[str, Any],
    required_evidence_basenames: set[str],
    *,
    control_root: Path | None = None,
) -> bool:
    evidence_root = control_root or repo_root
    expected_keys = {
        "schemaVersion",
        "runnerId",
        "runnerSource",
        "requirementsSource",
        "gateId",
        "productCandidate",
        "commandId",
        "attemptId",
        "commandPolicy",
        "argvSha256",
        "semanticLoaderDescriptorSha256",
        "semanticSources",
        "checkoutIdentity",
        "runtimeInputTree",
        "semanticDependencyTree",
        "toolIdentity",
        "gitToolIdentity",
        "executionCapability",
        "redactedInvocation",
        "invocationSha256",
        "startedAt",
        "finishedAt",
        "exitCode",
        "stdout",
        "stderr",
        "testCounts",
        "countsSource",
    }
    command_id = payload.get("commandId")
    attempt_id = payload.get("attemptId")
    redacted_invocation = payload.get("redactedInvocation")
    started_at = _timestamp(payload.get("startedAt"))
    finished_at = _timestamp(payload.get("finishedAt"))
    invocation_sha = payload.get("invocationSha256")
    gate_id = gate.get("gateId")
    product_candidate = _mapping(gate.get("identity")).get("productCandidate")
    runner_source = payload.get("runnerSource")
    requirements_source = payload.get("requirementsSource")
    command_policy_binding = payload.get("commandPolicy")
    policy_sha = hashlib.sha256(_json_bytes(trusted_test_policy)).hexdigest()
    expected_argv_sha = hashlib.sha256(
        _json_bytes(
            {
                "executableRole": trusted_test_policy.get("executableRole"),
                "arguments": trusted_test_policy.get("arguments"),
            }
        )
    ).hexdigest()
    gate_started_at = _timestamp(gate.get("startedAt"))
    gate_finished_at = _timestamp(gate.get("finishedAt"))
    valid = True
    if (
        set(payload) != expected_keys
        or payload.get("schemaVersion") != SCHEMA_VERSION
        or payload.get("runnerId") != TEST_RUNNER_ID
        or not isinstance(command_id, str)
        or not command_id
        or not isinstance(attempt_id, str)
        or SAFE_REPORT_ATTEMPT_ID.fullmatch(attempt_id) is None
        or not isinstance(redacted_invocation, str)
        or not redacted_invocation
        or not isinstance(invocation_sha, str)
        or invocation_sha
        != hashlib.sha256(redacted_invocation.encode("utf-8")).hexdigest()
        or started_at is None
        or finished_at is None
        or started_at > finished_at
        or payload.get("exitCode") != 0
        or payload.get("countsSource") not in TEST_COUNT_SOURCES
        or gate_started_at is None
        or gate_finished_at is None
        or started_at is None
        or finished_at is None
        or not (gate_started_at <= started_at <= finished_at <= gate_finished_at)
    ):
        problems.add(
            "TEST_REPORT_PROVENANCE",
            location,
            "test-report requires a successful frozen-runner invocation envelope",
        )
        valid = False

    report_parts = _normalised_repo_parts(payload_document.relative_path)
    result_parts = _normalised_repo_parts(gate.get("resultPath"))
    artifact_parts = result_parts[:-2] if result_parts is not None else None
    expected_parent = (
        (*artifact_parts, "gate-evidence", str(gate_id))
        if artifact_parts is not None
        else None
    )
    expected_basename = (
        f"{command_id}.{attempt_id}.trusted-test-report.json"
    )
    if (
        report_parts is None
        or expected_parent is None
        or tuple(report_parts[:-1]) != expected_parent
        or report_parts[-1] != expected_basename
        or report_parts[-1] in required_evidence_basenames
    ):
        problems.add(
            "TEST_REPORT_PATH",
            location,
            "test report must use its reserved attempt-bound Gate-local basename",
        )
        valid = False

    if (
        command_id != trusted_test_policy.get("commandId")
        or redacted_invocation != trusted_test_policy.get("redactedInvocation")
        or payload.get("countsSource") != trusted_test_policy.get("countsSource")
        or command_policy_binding
        != {
            "policyId": trusted_test_policy.get("policyId"),
            "sha256": policy_sha,
        }
        or payload.get("argvSha256") != expected_argv_sha
    ):
        problems.add(
            "TEST_REPORT_COMMAND_POLICY",
            location,
            "test-report must bind the exact frozen semantic argv policy",
        )
        valid = False

    if (
        payload.get("gateId") != gate_id
        or payload.get("productCandidate") != product_candidate
    ):
        problems.add(
            "TEST_REPORT_IDENTITY_BINDING",
            location,
            "test-report must bind its exact Gate and product candidate",
        )
        valid = False

    if not _validate_product_checkout_identity(
        payload.get("checkoutIdentity"),
        str(gate_id),
        product_candidate,
        repo_root,
        problems,
        location,
    ):
        valid = False

    if not _validate_semantic_sources(
        payload.get("semanticSources"),
        payload.get("semanticLoaderDescriptorSha256"),
        trusted_test_policy,
        product_candidate,
        repo_root,
        problems,
        location,
    ):
        valid = False
    if not _validate_semantic_dependency_tree(
        payload.get("semanticDependencyTree"),
        repo_root,
        problems,
        location,
    ):
        valid = False
    if not _validate_semantic_runtime_input_tree(
        payload.get("runtimeInputTree"),
        payload,
        payload_document,
        gate,
        evidence_root,
        problems,
        location,
    ):
        valid = False
    if payload.get("toolIdentity") != _expected_python_tool_identity():
        problems.add(
            "TEST_REPORT_TOOL_IDENTITY",
            location,
            "semantic report must bind the current external pinned interpreter root",
        )
        valid = False
    if not _validate_semantic_git_tool_identity(
        payload.get("gitToolIdentity"),
        trusted_git_policy,
        repo_root,
        problems,
        location,
    ):
        valid = False
    if payload.get("executionCapability") != getattr(
        trusted_executor, "SEMANTIC_EXECUTION_CAPABILITY", None
    ):
        problems.add(
            "TEST_REPORT_EXECUTION_CAPABILITY",
            location,
            "semantic report must declare the exact cooperative sealed-source capability",
        )
        valid = False

    if (
        trusted_runner_policy.get("runnerId") != TEST_RUNNER_ID
        or trusted_runner_policy.get("sourcePath") != TEST_RUNNER_PATH
        or trusted_runner_policy.get("shellAllowed") is not False
    ):
        problems.add(
            "TEST_REPORT_RUNNER_SOURCE",
            location,
            "test-report must bind the current raw frozen executor source",
        )
        valid = False
    if not _validate_runtime_source_binding(
        runner_source,
        TEST_RUNNER_PATH,
        trusted_runner_policy.get("sourceSha256"),
        product_candidate,
        repo_root,
        problems,
        location,
        "TEST_REPORT_RUNNER",
    ):
        valid = False
    try:
        requirements_sha = hashlib.sha256(
            (repo_root / GATE_REQUIREMENTS_PATH).read_bytes()
        ).hexdigest()
    except OSError:
        requirements_sha = None
    if not _validate_runtime_source_binding(
        requirements_source,
        GATE_REQUIREMENTS_PATH,
        requirements_sha,
        product_candidate,
        repo_root,
        problems,
        location,
        "TEST_REPORT_REQUIREMENTS_SOURCE",
    ):
        valid = False

    matching_commands = [
        command
        for command in _list(gate.get("commands"))
        if isinstance(command, Mapping) and command.get("commandId") == command_id
    ]
    if (
        len(matching_commands) != 1
        or matching_commands[0].get("status") != "Passed"
        or matching_commands[0].get("exitCode") != 0
        or matching_commands[0].get("testCountSource") is not True
        or matching_commands[0].get("redactedCommand") != redacted_invocation
        or matching_commands[0].get("startedAt") != payload.get("startedAt")
        or matching_commands[0].get("finishedAt") != payload.get("finishedAt")
    ):
        problems.add(
            "TEST_REPORT_COMMAND_BINDING",
            location,
            "test-report invocation must exactly bind one test-count-source Gate command",
        )
        valid = False

    resolved_stream_paths: dict[str, Path] = {}
    stdout_raw: bytes | None = None
    for stream_name in ("stdout", "stderr"):
        stream = _mapping(payload.get(stream_name))
        stream_path = _safe_relative_evidence_path(
            evidence_root,
            stream.get("path"),
            problems,
            f"{location}/{stream_name}/path",
        )
        declared_sha = stream.get("sha256")
        if (
            stream_path is None
            or not stream_path.is_file()
            or not isinstance(declared_sha, str)
            or HEX_SHA256.fullmatch(declared_sha) is None
        ):
            problems.add(
                "TEST_REPORT_STREAM",
                f"{location}/{stream_name}",
                "test-report stream must be an existing hash-bound UTF-8 file",
            )
            valid = False
            continue
        resolved_stream_paths[stream_name] = stream_path.resolve()
        if stream_path.parent.resolve() != payload_document.path.parent.resolve():
            problems.add(
                "TEST_REPORT_STREAM_PATH",
                f"{location}/{stream_name}",
                "test-report streams must be direct children beside the report artifact",
            )
            valid = False
        raw = stream_path.read_bytes()
        if stream_name == "stdout":
            stdout_raw = raw
        if hashlib.sha256(raw).hexdigest() != declared_sha:
            problems.add(
                "TEST_REPORT_STREAM_HASH",
                f"{location}/{stream_name}",
                "test-report stream hash does not match file bytes",
            )
            valid = False
        _scan_text_evidence_for_secrets(
            stream_path,
            "text-log",
            problems,
            f"{location}/{stream_name}",
        )
    if (
        resolved_stream_paths.get("stdout") is not None
        and resolved_stream_paths.get("stdout")
        == resolved_stream_paths.get("stderr")
    ):
        problems.add(
            "TEST_REPORT_STREAM_PATH",
            location,
            "stdout and stderr must be distinct direct-child artifacts",
        )
        valid = False
    if stdout_raw is not None and not _validate_frozen_unittest_report_counts(
        payload,
        stdout_raw,
        problems,
        location,
    ):
        valid = False
    return valid


def _descendant_changed_paths(
    repo_root: Path,
    product_candidate: str,
    expected_head: str,
) -> tuple[str, ...] | None:
    if (
        product_candidate == expected_head
        or _git_return_code(
            repo_root,
            ("merge-base", "--is-ancestor", product_candidate, expected_head),
        )
        != 0
    ):
        return None
    history = _git_stdout(
        repo_root,
        (
            "rev-list",
            "--reverse",
            "--parents",
            f"{product_candidate}..{expected_head}",
        ),
    )
    if history is None:
        return None
    expected_parent = product_candidate
    changed: list[str] = []
    commits = [line for line in history.splitlines() if line]
    if not commits:
        return None
    for line in commits:
        words = line.split(" ")
        if (
            len(words) != 2
            or re.fullmatch(r"[0-9a-f]{40}", words[0]) is None
            or words[1] != expected_parent
        ):
            return None
        paths = _git_stdout(
            repo_root,
            (
                "diff-tree",
                "--no-commit-id",
                "--name-only",
                "-r",
                words[0],
            ),
        )
        path_values = (
            [value.replace("\\", "/") for value in paths.splitlines() if value]
            if paths is not None
            else []
        )
        if not path_values:
            return None
        changed.extend(path_values)
        expected_parent = words[0]
    return tuple(changed) if expected_parent == expected_head else None


def _validate_manual_request_control(
    repo_root: Path,
    relative_path: str,
    product_candidate: str,
    acceptance_id: str,
    checkpoint: str,
    problems: Problems,
    location: str,
) -> bool:
    path_parts = _normalised_repo_parts(relative_path)
    if (
        path_parts is None
        or "/".join(path_parts[:-1]) != USER_ACCEPTANCE_REQUEST_ROOT
        or not path_parts[-1].endswith(".json")
    ):
        problems.add(
            "PRODUCT_REPORT_MANUAL_REQUEST",
            location,
            "manual descendant requires a canonical immutable request path",
        )
        return False
    document = read_document(repo_root, repo_root / relative_path, problems)
    request = _mapping(document.data) if document is not None else {}
    request_id = request.get("decisionRequestId")
    valid = True
    if (
        set(request) != USER_ACCEPTANCE_REQUEST_KEYS
        or request.get("schemaVersion") != SCHEMA_VERSION
        or request.get("goalId") != GOAL_ID
        or request.get("acceptanceId") != acceptance_id
        or request.get("checkpoint") != checkpoint
        or request.get("candidate") != product_candidate
        or request.get("status") != "AwaitingUser"
        or not isinstance(request_id, str)
        or DECISION_REQUEST_ID.fullmatch(request_id) is None
        or path_parts[-1] != f"{request_id}.json"
        or not isinstance(request.get("manifestSha256"), str)
        or HEX_SHA256.fullmatch(str(request.get("manifestSha256"))) is None
        or not isinstance(request.get("challengeCode"), str)
        or CHALLENGE_CODE.fullmatch(str(request.get("challengeCode"))) is None
        or _timestamp(request.get("requestedAt")) is None
    ):
        problems.add(
            "PRODUCT_REPORT_MANUAL_REQUEST",
            location,
            "manual request does not bind the exact Gate candidate/challenge",
        )
        valid = False
    if not _validate_single_add_immutable_blob(
        repo_root,
        relative_path,
        problems,
        "PRODUCT_REPORT_MANUAL_REQUEST",
    ):
        valid = False
    first_commit = _single_add_commit(repo_root, relative_path)
    if (
        first_commit is None
        or first_commit == product_candidate
        or _git_return_code(
            repo_root,
            ("merge-base", "--is-ancestor", product_candidate, first_commit),
        )
        != 0
    ):
        problems.add(
            "PRODUCT_REPORT_MANUAL_REQUEST_ANCESTRY",
            location,
            "manual request first-add commit must strictly descend from the candidate",
        )
        valid = False
    return valid


def _validate_controlled_authorization_bundle(
    repo_root: Path,
    controlled_gate: str,
    product_candidate: str,
    problems: Problems,
    location: str,
) -> tuple[set[str], dict[str, str], dict[str, str]] | None:
    """Mirror the frozen executor's exact controlled authorization bundle."""

    try:
        binding = trusted_executor.verify_controlled_write_tombstone(
            repo_root,
            gate_id=controlled_gate,
            product_candidate=product_candidate,
        )
    except trusted_executor.ExecutorError:
        problems.add(
            "PRODUCT_REPORT_CONTROLLED_AUTHORIZATION",
            location,
            "controlled descendant authorization bundle is invalid",
        )
        return None
    paths = {
        binding.path,
        binding.preauthorization.path,
        *binding.preauthorization.decision_paths,
    }
    valid = True
    for relative_path in sorted(paths):
        if not _validate_single_add_immutable_blob(
            repo_root,
            relative_path,
            problems,
            "PRODUCT_REPORT_CONTROLLED_AUTHORIZATION",
        ):
            valid = False
    if not valid:
        return None
    return (
        paths,
        {
            "path": binding.path,
            "sha256": binding.sha256,
            "commit": binding.commit,
        },
        {
            "path": binding.preauthorization.path,
            "sha256": binding.preauthorization.sha256,
            "commit": binding.preauthorization.commit,
        },
    )


def _validate_product_checkout_identity(
    checkout: Any,
    gate_id: str,
    product_candidate: Any,
    repo_root: Path,
    problems: Problems,
    location: str,
    controlled_bundle_cache: dict[
        tuple[str, str],
        tuple[set[str], dict[str, str], dict[str, str]] | None,
    ]
    | None = None,
) -> bool:
    valid = True
    if not isinstance(checkout, Mapping) or set(checkout) != {
        "mode",
        "productCandidate",
        "expectedHead",
        "before",
        "after",
    }:
        problems.add(
            "PRODUCT_REPORT_CHECKOUT_SHAPE",
            location,
            "product report requires the exact checkout identity envelope",
        )
        return False
    before = checkout.get("before")
    after = checkout.get("after")
    snapshot_keys = {
        "headCommit",
        "treeObjectId",
        "gitStatusPorcelainV1",
        "trackedStatus",
    }
    expected_head = checkout.get("expectedHead")
    if (
        not isinstance(product_candidate, str)
        or re.fullmatch(r"[0-9a-f]{40}", product_candidate) is None
        or checkout.get("productCandidate") != product_candidate
        or not isinstance(expected_head, str)
        or re.fullmatch(r"[0-9a-f]{40}", expected_head) is None
        or not isinstance(before, Mapping)
        or not isinstance(after, Mapping)
        or set(before) != snapshot_keys
        or set(after) != snapshot_keys
        or before != after
        or before.get("headCommit") != expected_head
        or before.get("gitStatusPorcelainV1") != ""
        or before.get("trackedStatus") != "Clean"
    ):
        problems.add(
            "PRODUCT_REPORT_CHECKOUT_IDENTITY",
            location,
            "checkout before/after must be identical clean snapshots at the expected head",
        )
        valid = False

    controlled_gate = CONTROLLED_DESCENDANT_GATES.get(gate_id)
    manual = MANUAL_DESCENDANT_GATES.get(gate_id)
    expected_mode = (
        "controlled-and-manual-descendant"
        if controlled_gate is not None and manual is not None
        else "controlled-tombstone-descendant"
        if controlled_gate is not None
        else "manual-challenge-descendant"
        if manual is not None
        else "candidate-exact"
    )
    if checkout.get("mode") != expected_mode:
        problems.add(
            "PRODUCT_REPORT_CHECKOUT_MODE",
            location,
            "checkout mode does not match the frozen Gate execution mode",
        )
        valid = False

    if expected_mode == "candidate-exact":
        if expected_head != product_candidate:
            problems.add(
                "PRODUCT_REPORT_CHECKOUT_SCOPE",
                location,
                "candidate-exact Gate must execute directly at the product candidate",
            )
            valid = False
    else:
        changed = (
            _descendant_changed_paths(repo_root, product_candidate, expected_head)
            if isinstance(product_candidate, str)
            and isinstance(expected_head, str)
            else None
        )
        allowed: set[str] = set()
        if controlled_gate is not None:
            controlled_key = (controlled_gate, str(product_candidate))
            if (
                controlled_bundle_cache is not None
                and controlled_key in controlled_bundle_cache
            ):
                controlled_bundle = controlled_bundle_cache[controlled_key]
            else:
                controlled_bundle = _validate_controlled_authorization_bundle(
                    repo_root,
                    controlled_gate,
                    str(product_candidate),
                    problems,
                    location,
                )
                if controlled_bundle_cache is not None:
                    controlled_bundle_cache[controlled_key] = controlled_bundle
            if controlled_bundle is None:
                valid = False
            else:
                allowed.update(controlled_bundle[0])
        if manual is not None and changed is not None:
            acceptance_id, checkpoint = manual
            request_paths = sorted(
                {
                    path
                    for path in changed
                    if path.startswith(f"{USER_ACCEPTANCE_REQUEST_ROOT}/")
                }
            )
            if not request_paths:
                problems.add(
                    "PRODUCT_REPORT_MANUAL_REQUEST",
                    location,
                    "manual descendant Gate requires an immutable challenge request",
                )
                valid = False
            for request_path in request_paths:
                allowed.add(request_path)
                if not _validate_manual_request_control(
                    repo_root,
                    request_path,
                    str(product_candidate),
                    acceptance_id,
                    checkpoint,
                    problems,
                    location,
                ):
                    valid = False
        if changed is None or set(changed) != allowed:
            problems.add(
                "PRODUCT_REPORT_CHECKOUT_SCOPE",
                location,
                "candidate descendants may contain only their immutable authorization/tombstone/request controls",
            )
            valid = False

    expected_tree = (
        _git_stdout(repo_root, ("rev-parse", f"{expected_head}^{{tree}}"))
        if isinstance(expected_head, str)
        else None
    )
    if (
        not isinstance(before, Mapping)
        or before.get("treeObjectId") != expected_tree
        or not isinstance(expected_tree, str)
        or re.fullmatch(r"[0-9a-f]{40}", expected_tree) is None
    ):
        problems.add(
            "PRODUCT_REPORT_CHECKOUT_TREE",
            location,
            "checkout tree object does not match the expected execution commit",
        )
        valid = False
    return valid


def _length_prefixed_blocks_sha256(blocks: Sequence[bytes]) -> str:
    digest = hashlib.sha256()
    for block in blocks:
        digest.update(len(block).to_bytes(8, "big"))
        digest.update(block)
    return digest.hexdigest()


def _expected_python_tool_identity() -> dict[str, Any] | None:
    try:
        executable = Path(sys.executable).resolve(strict=True)
        raw = executable.read_bytes()
    except OSError:
        return None
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


def _expected_runtime_input_bindings(
    repo_root: Path,
    gate_id: str,
    command_id: str,
) -> list[dict[str, str]] | None:
    layout = (
        W84_G0_RUNTIME_INPUT_LAYOUT.get(command_id)
        if gate_id == "W84-G0"
        else ()
    )
    if layout is None:
        return None
    result: list[dict[str, str]] = []
    for relative_path, kind in sorted(layout):
        if kind not in COMMAND_RUNTIME_INPUT_KINDS:
            return None
        target = _safe_relative_evidence_path(
            repo_root,
            relative_path,
            Problems(),
            relative_path,
        )
        try:
            raw = target.read_bytes() if target is not None else None
        except OSError:
            raw = None
        if raw is None:
            return None
        result.append(
            {
                "path": relative_path,
                "sha256": hashlib.sha256(raw).hexdigest(),
                "kind": kind,
            }
        )
    return result


def _command_control_report_context(
    repo_root: Path,
    gate: Mapping[str, Any],
    command_id: str,
    problems: Problems,
    location: str,
) -> tuple[
    Mapping[str, Any],
    Mapping[str, Any],
    list[Mapping[str, Any]],
    Mapping[str, Any],
    list[Mapping[str, Any]],
] | None:
    binding = gate.get("commandControlBinding")
    if not isinstance(binding, Mapping):
        problems.add(
            "PRODUCT_REPORT_CONTROL_BINDING",
            location,
            "two-stage reports require the Gate's exact command-control binding",
        )
        return None
    control_path = _safe_relative_evidence_path(
        repo_root,
        binding.get("path"),
        problems,
        f"{location}/commandControlBinding/path",
    )
    control_document = (
        read_document(repo_root, control_path, problems)
        if control_path is not None and control_path.is_file()
        else None
    )
    control = (
        control_document.data if control_document is not None else None
    )
    try:
        control_raw = control_path.read_bytes() if control_path is not None else None
    except OSError:
        control_raw = None
    if (
        not isinstance(control, Mapping)
        or not isinstance(control_raw, bytes)
        or control_raw != _json_bytes(control)
        or binding.get("sha256") != hashlib.sha256(control_raw).hexdigest()
        or control.get("gateId") != gate.get("gateId")
        or control.get("protocol") != COMMAND_CONTROL_PROTOCOL
        or control.get("preparedFromRevision")
        != binding.get("preparedFromRevision")
        or not isinstance(binding.get("controlRevision"), str)
        or re.fullmatch(r"[0-9a-f]{40}", str(binding.get("controlRevision")))
        is None
    ):
        problems.add(
            "PRODUCT_REPORT_CONTROL_BINDING",
            location,
            "two-stage reports require canonical command-control bytes and identities",
        )
        return None
    sources = [
        item
        for item in _list(control.get("sources"))
        if isinstance(item, Mapping) and item.get("commandId") == command_id
    ]
    adapters = [item for item in sources if item.get("role") == "adapter"]
    verifiers = [
        item
        for item in sources
        if item.get("role") in {"oracle", "test"}
        and command_id
        in _list(_mapping(item.get("verification")).get("verifiesCommandIds"))
    ]
    source_trees = [
        item
        for item in _list(control.get("sourceTrees"))
        if isinstance(item, Mapping) and item.get("commandId") == command_id
    ]
    if len(adapters) != 1 or not verifiers:
        problems.add(
            "PRODUCT_REPORT_CONTROL_ROLES",
            location,
            "two-stage reports require one adapter and at least one ordered verifier",
        )
        return None
    return control, binding, sources, adapters[0], verifiers


def _report_source_binding(
    source: Mapping[str, Any], control_revision: str
) -> dict[str, Any]:
    return {
        "path": source.get("path"),
        "sha256": source.get("sha256"),
        "gitBlobSha": source.get("gitBlobSha"),
        "controlRevision": control_revision,
    }


def _report_source_tree_binding(
    source_tree: Mapping[str, Any], control_revision: str
) -> dict[str, Any]:
    return {
        "role": source_tree.get("role"),
        "rootPath": source_tree.get("rootPath"),
        "gitTreeSha": source_tree.get("gitTreeSha"),
        "entryCount": source_tree.get("entryCount"),
        "totalBytes": source_tree.get("totalBytes"),
        "contentRootSha256": source_tree.get("contentRootSha256"),
        "controlRevision": control_revision,
        "includePolicy": source_tree.get("includePolicy"),
    }


def _read_two_stage_stream(
    repo_root: Path,
    report_path: str,
    payload: Mapping[str, Any],
    stream_name: str,
    problems: Problems,
    location: str,
) -> bytes | None:
    expected = PurePosixPath(report_path).with_suffix(
        f".{stream_name}.log"
    ).as_posix()
    stream = payload.get(stream_name)
    resolved = _safe_relative_evidence_path(
        repo_root,
        _mapping(stream).get("path"),
        problems,
        f"{location}/{stream_name}/path",
    )
    try:
        raw = resolved.read_bytes() if resolved is not None else None
    except OSError:
        raw = None
    if (
        not isinstance(stream, Mapping)
        or set(stream) != {"path", "sha256"}
        or stream.get("path") != expected
        or raw is None
        or stream.get("sha256") != hashlib.sha256(raw or b"").hexdigest()
    ):
        problems.add(
            "PRODUCT_REPORT_STREAM",
            location,
            "two-stage stdout/stderr must be raw-hash-bound report siblings",
        )
        return None
    _scan_text_evidence_for_secrets(
        resolved,
        "log",
        problems,
        f"{location}/{stream_name}",
    )
    return raw


def _validate_product_command_report(
    payload_document: Document,
    evidence: Mapping[str, Any],
    gate: Mapping[str, Any],
    command: Mapping[str, Any],
    command_id: str,
    repo_root: Path,
    problems: Problems,
    location: str,
    trusted_runner_policy: Mapping[str, Any],
    trusted_product_policy: Mapping[str, Any],
    required_evidence_basenames: set[str],
) -> dict[str, int] | None:
    payload = payload_document.data
    expected_keys = {
        "schemaVersion",
        "runnerId",
        "runnerSource",
        "requirementsSource",
        "gateId",
        "productCandidate",
        "commandId",
        "attemptId",
        "commandPolicy",
        "argvSha256",
        "scriptSource",
        "checkoutIdentity",
        "redactedInvocation",
        "invocationSha256",
        "startedAt",
        "finishedAt",
        "exitCode",
        "resultProtocol",
        "counts",
        "countsSource",
        "stdout",
        "stderr",
    }
    if not isinstance(payload, Mapping) or set(payload) != expected_keys:
        problems.add(
            "PRODUCT_REPORT_SHAPE",
            location,
            "product command report must use the exact frozen envelope",
        )
        return None
    valid = True
    gate_id = gate.get("gateId")
    candidate = _mapping(gate.get("identity")).get("productCandidate")
    attempt_id = payload.get("attemptId")
    policy_sha = hashlib.sha256(_json_bytes(trusted_product_policy)).hexdigest()
    expected_script_path = (
        f"{TRUSTED_PRODUCT_SCRIPT_ROOT}/{command_id}.py"
    )
    expected_arguments = [
        {
            "{scriptPath}": expected_script_path,
            "{scriptSha256}": _mapping(payload.get("scriptSource")).get(
                "sha256"
            ),
        }.get(argument, argument)
        for argument in trusted_executor._PRODUCT_TEMPLATE_ARGUMENTS
    ]
    expected_argv_sha = hashlib.sha256(
        _json_bytes(
            {
                "executableRole": ISOLATED_PYTHON_EXECUTABLE_ROLE,
                "arguments": expected_arguments,
            }
        )
    ).hexdigest()
    expected_redacted = str(
        trusted_product_policy.get("redactedInvocationTemplate", "")
    ).replace("{commandId}", command_id)
    started_at = _timestamp(payload.get("startedAt"))
    finished_at = _timestamp(payload.get("finishedAt"))
    gate_started = _timestamp(gate.get("startedAt"))
    gate_finished = _timestamp(gate.get("finishedAt"))
    if (
        payload.get("schemaVersion") != SCHEMA_VERSION
        or payload.get("runnerId") != TEST_RUNNER_ID
        or payload.get("gateId") != gate_id
        or payload.get("productCandidate") != candidate
        or payload.get("commandId") != command_id
        or not isinstance(attempt_id, str)
        or SAFE_REPORT_ATTEMPT_ID.fullmatch(attempt_id) is None
        or payload.get("commandPolicy")
        != {
            "policyId": TRUSTED_PRODUCT_POLICY_ID,
            "sha256": policy_sha,
        }
        or payload.get("argvSha256") != expected_argv_sha
        or payload.get("redactedInvocation") != expected_redacted
        or payload.get("invocationSha256")
        != hashlib.sha256(expected_redacted.encode("utf-8")).hexdigest()
        or payload.get("exitCode") != 0
        or started_at is None
        or finished_at is None
        or gate_started is None
        or gate_finished is None
        or not (gate_started <= started_at < finished_at <= gate_finished)
        or command.get("status") != "Passed"
        or command.get("exitCode") != 0
        or command.get("redactedCommand") != expected_redacted
        or command.get("startedAt") != payload.get("startedAt")
        or command.get("finishedAt") != payload.get("finishedAt")
    ):
        problems.add(
            "PRODUCT_REPORT_BINDING",
            location,
            "product report must exactly bind its successful Gate command and policy",
        )
        valid = False

    runner_source = payload.get("runnerSource")
    if not _validate_runtime_source_binding(
        runner_source,
        TEST_RUNNER_PATH,
        trusted_runner_policy.get("sourceSha256"),
        candidate,
        repo_root,
        problems,
        location,
        "PRODUCT_REPORT_RUNNER_SOURCE",
    ):
        problems.add(
            "PRODUCT_REPORT_RUNNER_SOURCE",
            location,
            "product report must bind the current frozen trusted executor",
        )
        valid = False
    try:
        requirements_sha = hashlib.sha256(
            (repo_root / GATE_REQUIREMENTS_PATH).read_bytes()
        ).hexdigest()
    except OSError:
        requirements_sha = None
    if not _validate_runtime_source_binding(
        payload.get("requirementsSource"),
        GATE_REQUIREMENTS_PATH,
        requirements_sha,
        candidate,
        repo_root,
        problems,
        location,
        "PRODUCT_REPORT_REQUIREMENTS_SOURCE",
    ):
        valid = False

    script_source = payload.get("scriptSource")
    script_path = repo_root / expected_script_path
    try:
        script_raw = script_path.read_bytes()
        script_is_link = script_path.is_symlink()
    except OSError:
        script_raw = None
        script_is_link = False
    tree_line = (
        _git_stdout(
            repo_root,
            ("ls-tree", str(candidate), "--", expected_script_path),
        )
        if isinstance(candidate, str)
        else None
    )
    tree_match = re.fullmatch(
        r"(100644|100755) blob ([0-9a-f]{40})\t.+",
        tree_line or "",
    )
    candidate_raw = (
        _git_bytes(repo_root, ("show", f"{candidate}:{expected_script_path}"))
        if isinstance(candidate, str)
        else None
    )
    if (
        PRODUCT_COMMAND_SLUG.fullmatch(command_id) is None
        or not isinstance(script_source, Mapping)
        or set(script_source) != {"path", "sha256", "gitBlobSha"}
        or script_source.get("path") != expected_script_path
        or script_raw is None
        or script_is_link
        or candidate_raw != script_raw
        or tree_match is None
        or script_source.get("gitBlobSha") != (
            tree_match.group(2) if tree_match is not None else None
        )
        or script_source.get("sha256")
        != hashlib.sha256(script_raw or b"").hexdigest()
    ):
        problems.add(
            "PRODUCT_REPORT_SCRIPT_SOURCE",
            location,
            "product report script must equal its derived candidate Git blob",
        )
        valid = False

    if not _validate_product_checkout_identity(
        payload.get("checkoutIdentity"),
        str(gate_id),
        candidate,
        repo_root,
        problems,
        location,
    ):
        valid = False

    report_parts = _normalised_repo_parts(evidence.get("path"))
    result_parts = _normalised_repo_parts(gate.get("resultPath"))
    artifact_parts = result_parts[:-2] if result_parts is not None else None
    expected_report_parent = (
        (*artifact_parts, "gate-evidence", str(gate_id))
        if artifact_parts is not None
        else None
    )
    if (
        report_parts is None
        or expected_report_parent is None
        or tuple(report_parts[:-1]) != expected_report_parent
        or report_parts[-1]
        != f"{command_id}.{attempt_id}.trusted-product-report.json"
        or report_parts[-1] in required_evidence_basenames
        or evidence.get("kind") != "json"
        or evidence.get("sha256") != payload_document.sha256
    ):
        problems.add(
            "PRODUCT_REPORT_PATH",
            location,
            "product report must use its reserved attempt-bound Gate-local basename",
        )
        valid = False
    report_path = PurePosixPath(*(report_parts or ("invalid.json",)))
    stdout_expected = report_path.with_suffix(".stdout.log").as_posix()
    stderr_expected = report_path.with_suffix(".stderr.log").as_posix()
    stream_raw: dict[str, bytes] = {}
    for stream_name, expected_path in (
        ("stdout", stdout_expected),
        ("stderr", stderr_expected),
    ):
        stream = payload.get(stream_name)
        resolved = _safe_relative_evidence_path(
            repo_root,
            _mapping(stream).get("path"),
            problems,
            f"{location}/{stream_name}/path",
        )
        try:
            raw = resolved.read_bytes() if resolved is not None else None
        except OSError:
            raw = None
        if (
            not isinstance(stream, Mapping)
            or set(stream) != {"path", "sha256"}
            or stream.get("path") != expected_path
            or raw is None
            or stream.get("sha256") != hashlib.sha256(raw or b"").hexdigest()
        ):
            problems.add(
                "PRODUCT_REPORT_STREAM",
                location,
                "stdout/stderr must be distinct raw-hash-bound sibling streams",
            )
            valid = False
        else:
            stream_raw[stream_name] = raw
            _scan_text_evidence_for_secrets(
                resolved,
                "log",
                problems,
                f"{location}/{stream_name}",
            )

    counts = payload.get("counts")
    counts_valid = (
        isinstance(counts, Mapping)
        and set(counts) == {"discovered", "passed", "failed", "skipped"}
        and all(_is_int(value) and value >= 0 for value in counts.values())
        and counts.get("discovered")
        == counts.get("passed") + counts.get("failed") + counts.get("skipped")
        and counts.get("failed") == 0
    )
    marker_valid = False
    stdout_raw = stream_raw.get("stdout")
    if stdout_raw is not None:
        try:
            stdout_text = stdout_raw.decode("utf-8")
        except UnicodeDecodeError:
            stdout_text = ""
        marker_prefix = "CAICLI_PRODUCT_COMMAND_RESULT="
        lines = stdout_text.splitlines()
        marker_indexes = [
            index for index, line in enumerate(lines) if line.startswith(marker_prefix)
        ]
        if marker_indexes == [len(lines) - 1] and lines:
            encoded = lines[-1][len(marker_prefix) :]
            try:
                marker = json.loads(encoded, object_pairs_hook=_no_duplicate_object)
            except (json.JSONDecodeError, DuplicateJsonKey):
                marker = None
            expected_marker = {
                "protocol": TRUSTED_PRODUCT_RESULT_PROTOCOL,
                "status": "Passed",
                "counts": counts,
            }
            marker_valid = (
                marker == expected_marker
                and encoded == _json_bytes(expected_marker).decode("utf-8")
            )
    if (
        not counts_valid
        or payload.get("resultProtocol") != TRUSTED_PRODUCT_RESULT_PROTOCOL
        or payload.get("countsSource") != TRUSTED_PRODUCT_COUNTS_SOURCE
        or not marker_valid
    ):
        problems.add(
            "PRODUCT_REPORT_COUNTS",
            location,
            "product counts must come from the exact canonical stdout result protocol",
        )
        valid = False
    return dict(counts) if valid and isinstance(counts, Mapping) else None


def _validate_product_command_bundle(
    referenced_documents: Sequence[
        tuple[str, Mapping[str, Any], Document]
    ],
    gate: Mapping[str, Any],
    command: Mapping[str, Any],
    command_id: str,
    repo_root: Path,
    problems: Problems,
    location: str,
    trusted_runner_policy: Mapping[str, Any],
    trusted_product_policy: Mapping[str, Any],
    trusted_git_policy: Mapping[str, Any],
    required_evidence_basenames: set[str],
    *,
    control_root: Path | None = None,
    descriptor_evidence_root: Path | None = None,
) -> dict[str, int] | None:
    """Validate adapter -> verifier -> selection/execution reconciliation."""

    evidence_root = control_root or repo_root
    context = _command_control_report_context(
        repo_root, gate, command_id, problems, location
    )
    if context is None:
        return None
    control, control_binding, command_sources, adapter_source, verifier_sources = context
    control_revision = str(control_binding.get("controlRevision"))
    source_trees = [
        item
        for item in _list(control.get("sourceTrees"))
        if isinstance(item, Mapping) and item.get("commandId") == command_id
    ]
    reports: list[tuple[str, Mapping[str, Any], Document]] = []
    selections: list[tuple[str, Mapping[str, Any], Document]] = []
    executions: list[tuple[str, Mapping[str, Any], Document]] = []
    for evidence_id, evidence, document in referenced_documents:
        payload = document.data
        if not isinstance(payload, Mapping) or payload.get("commandId") != command_id:
            continue
        if payload.get("protocol") == COMMAND_SELECTION_PROTOCOL:
            selections.append((evidence_id, evidence, document))
        elif payload.get("protocol") == COMMAND_EXECUTION_PROTOCOL:
            executions.append((evidence_id, evidence, document))
        elif payload.get("runnerId") == TEST_RUNNER_ID:
            reports.append((evidence_id, evidence, document))
    adapter_reports = [
        item for item in reports if _mapping(item[2].data).get("commandRole") == "adapter"
    ]
    verifier_reports = [
        item
        for item in reports
        if _mapping(item[2].data).get("commandRole") in {"oracle", "test"}
    ]
    if (
        len(adapter_reports) != 1
        or len(verifier_reports) != len(verifier_sources)
        or len(selections) != 1
        or len(executions) != 1
    ):
        problems.add(
            "PRODUCT_REPORT_BUNDLE",
            location,
            "each product command must reference one adapter, every ordered verifier, one selection, and one execution manifest",
        )
        return None

    adapter_id, adapter_evidence, adapter_document = adapter_reports[0]
    adapter_payload = _mapping(adapter_document.data)
    attempt_id = adapter_payload.get("attemptId")
    gate_id = str(gate.get("gateId"))
    candidate = _mapping(gate.get("identity")).get("productCandidate")
    if (
        not isinstance(attempt_id, str)
        or SAFE_REPORT_ATTEMPT_ID.fullmatch(attempt_id) is None
        or any(
            _mapping(item[2].data).get("attemptId") != attempt_id
            for item in reports
        )
    ):
        problems.add(
            "PRODUCT_REPORT_ATTEMPT",
            location,
            "all adapter/verifier reports must share one safe attemptId",
        )
        return None

    result_parts = _normalised_repo_parts(gate.get("resultPath"))
    expected_parent = (
        (*result_parts[:-2], "gate-evidence", gate_id)
        if result_parts is not None and len(result_parts) >= 3
        else None
    )

    def document_identity_valid(
        evidence: Mapping[str, Any],
        document: Document,
        expected_basename: str,
        code: str,
    ) -> bool:
        parts = _normalised_repo_parts(evidence.get("path"))
        try:
            raw = document.path.read_bytes()
        except OSError:
            raw = None
        valid = (
            expected_parent is not None
            and parts is not None
            and tuple(parts[:-1]) == expected_parent
            and parts[-1] == expected_basename
            and parts[-1] not in required_evidence_basenames
            and evidence.get("kind") == "json"
            and evidence.get("sha256") == document.sha256
            and document.relative_path == evidence.get("path")
            and isinstance(raw, bytes)
            and raw == _json_bytes(document.data)
        )
        if not valid:
            problems.add(
                code,
                location,
                "two-stage artifact must use canonical bytes and its exact attempt-bound Gate-local path",
            )
        return valid

    selection_id, selection_evidence, selection_document = selections[0]
    selection = _mapping(selection_document.data)
    selection_basename = f"{command_id}.{attempt_id}.selection.json"
    selection_path = str(selection_evidence.get("path"))
    document_identity_valid(
        selection_evidence,
        selection_document,
        selection_basename,
        "PRODUCT_SELECTION_PATH",
    )
    verifier_argv_arguments = list(
        getattr(trusted_executor, "_VERIFIER_ARGUMENTS", ())
    )
    expected_verifier_argv_sha = hashlib.sha256(
        _json_bytes(
            {
                "executableRole": ISOLATED_PYTHON_EXECUTABLE_ROLE,
                "arguments": verifier_argv_arguments,
            }
        )
    ).hexdigest()
    expected_cases = [
        {
            "caseId": argument,
            "projectId": None,
            "sourcePath": source.get("path"),
        }
        for source in verifier_sources
        for argument in _list(_mapping(source.get("verification")).get("arguments"))
    ]
    expected_selected_sources: list[dict[str, Any]] = []
    selected_source_paths: set[str] = set()
    for source in verifier_sources:
        path = str(source.get("path"))
        if path in selected_source_paths:
            continue
        selected_source_paths.add(path)
        expected_selected_sources.append(
            {
                **_report_source_binding(source, control_revision),
                "sourceTreeContentRootSha256": None,
            }
        )
    selection_keys = {
        "schemaVersion",
        "protocol",
        "goalId",
        "gateId",
        "commandId",
        "productCandidate",
        "controlRevision",
        "runnerKind",
        "selectionOrigin",
        "verifierArgvSha256",
        "selectedCases",
        "selectedCaseCount",
        "selectedCaseRootSha256",
        "selectedSources",
        "selectedSourceCount",
        "selectedSourceRootSha256",
        "gitToolIdentity",
        "createdAt",
    }
    selection_created = _timestamp(selection.get("createdAt"))
    gate_started = _timestamp(gate.get("startedAt"))
    gate_finished = _timestamp(gate.get("finishedAt"))
    git_tool_identity = _expected_git_tool_identity_record(
        trusted_git_policy, repo_root
    )
    selection_valid = (
        set(selection) == selection_keys
        and selection.get("schemaVersion") == SCHEMA_VERSION
        and selection.get("protocol") == COMMAND_SELECTION_PROTOCOL
        and selection.get("goalId") == GOAL_ID
        and selection.get("gateId") == gate_id
        and selection.get("commandId") == command_id
        and selection.get("productCandidate") == candidate
        and selection.get("controlRevision") == control_revision
        and selection.get("runnerKind") == "python-unittest"
        and selection.get("selectionOrigin") == "exact-control-arguments"
        and selection.get("verifierArgvSha256") == expected_verifier_argv_sha
        and selection.get("selectedCases") == expected_cases
        and len({item["caseId"] for item in expected_cases}) == len(expected_cases)
        and selection.get("selectedCaseCount") == len(expected_cases)
        and selection.get("selectedCaseRootSha256")
        == hashlib.sha256(_json_bytes(expected_cases)).hexdigest()
        and selection.get("selectedSources") == expected_selected_sources
        and selection.get("selectedSourceCount") == len(expected_selected_sources)
        and selection.get("selectedSourceRootSha256")
        == hashlib.sha256(_json_bytes(expected_selected_sources)).hexdigest()
        and selection.get("gitToolIdentity") == git_tool_identity
        and selection_created is not None
        and gate_started is not None
        and gate_finished is not None
        and gate_started <= selection_created <= gate_finished
    )
    if not selection_valid:
        problems.add(
            "PRODUCT_SELECTION_BINDING",
            location,
            "selection manifest must equal the ordered sealed verifier cases and sources",
        )

    selection_summary = {
        "mode": COMMAND_SELECTION_MODE,
        "runnerKind": "python-unittest",
        "discoveryPolicyId": COMMAND_DISCOVERY_POLICY_ID,
        "selectionOrigin": "exact-control-arguments",
        "manifestPath": selection_path,
        "manifestSha256": selection_document.sha256,
        "selectedCaseCount": selection.get("selectedCaseCount"),
        "selectedCaseRootSha256": selection.get("selectedCaseRootSha256"),
        "selectedSourceCount": selection.get("selectedSourceCount"),
        "selectedSourceRootSha256": selection.get("selectedSourceRootSha256"),
        "zeroSelectionAllowed": False,
    }
    runtime_inputs = _expected_runtime_input_bindings(
        evidence_root, gate_id, command_id
    )
    tool_identity = _expected_python_tool_identity()
    if (
        runtime_inputs is None
        or tool_identity is None
        or git_tool_identity is None
    ):
        problems.add(
            "PRODUCT_REPORT_RUNTIME_INPUT",
            location,
            "every declared runtime input and current isolated Python identity must be available",
        )
        return None

    common_report_keys = {
        "schemaVersion",
        "runnerId",
        "runnerSource",
        "requirementsSource",
        "gateId",
        "productCandidate",
        "commandId",
        "attemptId",
        "commandRole",
        "commandControlBinding",
        "commandPolicy",
        "argvSha256",
        "scriptSource",
        "checkoutIdentity",
        "toolIdentity",
        "gitToolIdentity",
        "executionCapability",
        "runtimeInputs",
        "redactedInvocation",
        "invocationSha256",
        "startedAt",
        "finishedAt",
        "exitCode",
        "stdout",
        "stderr",
        "resultProtocol",
        "counts",
        "countsSource",
        "verifiedAdapterReports",
        "verificationDescriptorSha256",
    }
    try:
        requirements_sha = hashlib.sha256(
            (repo_root / GATE_REQUIREMENTS_PATH).read_bytes()
        ).hexdigest()
    except OSError:
        requirements_sha = None
    report_streams: dict[str, tuple[bytes, bytes]] = {}
    report_times: dict[str, tuple[datetime, datetime]] = {}

    def validate_common_report(
        evidence_id: str,
        evidence: Mapping[str, Any],
        document: Document,
        expected_source: Mapping[str, Any],
        expected_role: str,
        expected_basename: str,
        expected_policy: Mapping[str, Any],
        expected_argv_sha: str,
        expected_redacted: str,
        *,
        verifier: bool,
    ) -> bool:
        payload = _mapping(document.data)
        keys = set(common_report_keys)
        if verifier:
            keys.add("executionReconciliation")
        valid = document_identity_valid(
            evidence,
            document,
            expected_basename,
            "PRODUCT_REPORT_PATH",
        )
        started = _timestamp(payload.get("startedAt"))
        finished = _timestamp(payload.get("finishedAt"))
        if (
            set(payload) != keys
            or payload.get("schemaVersion") != SCHEMA_VERSION
            or payload.get("runnerId") != TEST_RUNNER_ID
            or payload.get("gateId") != gate_id
            or payload.get("productCandidate") != candidate
            or payload.get("commandId") != command_id
            or payload.get("attemptId") != attempt_id
            or payload.get("commandRole") != expected_role
            or payload.get("commandControlBinding") != control_binding
            or payload.get("commandPolicy")
            != {
                "policyId": expected_policy.get("policyId"),
                "sha256": hashlib.sha256(
                    _json_bytes(expected_policy)
                ).hexdigest(),
            }
            or payload.get("argvSha256") != expected_argv_sha
            or payload.get("scriptSource")
            != _report_source_binding(expected_source, control_revision)
            or payload.get("toolIdentity") != tool_identity
            or payload.get("gitToolIdentity") != git_tool_identity
            or payload.get("executionCapability")
            != ADAPTER_EXECUTION_CAPABILITY
            or payload.get("runtimeInputs") != runtime_inputs
            or payload.get("redactedInvocation") != expected_redacted
            or payload.get("invocationSha256")
            != hashlib.sha256(expected_redacted.encode("utf-8")).hexdigest()
            or payload.get("exitCode") != 0
            or started is None
            or finished is None
            or gate_started is None
            or gate_finished is None
            or not (gate_started <= started < finished <= gate_finished)
        ):
            problems.add(
                "PRODUCT_REPORT_BINDING",
                f"{location}/{evidence_id}",
                "two-stage report envelope must exactly bind its control, role, policy, runtime inputs, and Gate window",
            )
            valid = False
        if not _validate_runtime_source_binding(
            payload.get("runnerSource"),
            TEST_RUNNER_PATH,
            trusted_runner_policy.get("sourceSha256"),
            candidate,
            repo_root,
            problems,
            f"{location}/{evidence_id}",
            "PRODUCT_REPORT_RUNNER_SOURCE",
        ):
            valid = False
        if not _validate_runtime_source_binding(
            payload.get("requirementsSource"),
            GATE_REQUIREMENTS_PATH,
            requirements_sha,
            candidate,
            repo_root,
            problems,
            f"{location}/{evidence_id}",
            "PRODUCT_REPORT_REQUIREMENTS_SOURCE",
        ):
            valid = False
        if not _validate_product_checkout_identity(
            payload.get("checkoutIdentity"),
            gate_id,
            candidate,
            repo_root,
            problems,
            f"{location}/{evidence_id}",
        ):
            valid = False
        report_path = str(evidence.get("path"))
        stdout_raw = _read_two_stage_stream(
            evidence_root,
            report_path,
            payload,
            "stdout",
            problems,
            f"{location}/{evidence_id}",
        )
        stderr_raw = _read_two_stage_stream(
            evidence_root,
            report_path,
            payload,
            "stderr",
            problems,
            f"{location}/{evidence_id}",
        )
        if stdout_raw is None or stderr_raw is None:
            valid = False
        else:
            report_streams[evidence_id] = (stdout_raw, stderr_raw)
        if started is not None and finished is not None:
            report_times[evidence_id] = (started, finished)
        return valid

    adapter_template = list(
        getattr(trusted_executor, "_PRODUCT_TEMPLATE_ARGUMENTS", ())
    )
    adapter_source_binding = _report_source_binding(
        adapter_source, control_revision
    )
    adapter_arguments = [
        {
            "{scriptPath}": str(adapter_source.get("path")),
            "{scriptSha256}": str(adapter_source.get("sha256")),
        }.get(argument, argument)
        for argument in adapter_template
    ]
    expected_adapter_argv_sha = hashlib.sha256(
        _json_bytes(
            {
                "executableRole": ISOLATED_PYTHON_EXECUTABLE_ROLE,
                "arguments": adapter_arguments,
            }
        )
    ).hexdigest()
    adapter_redacted = str(
        trusted_product_policy.get("redactedInvocationTemplate", "")
    ).replace("{commandId}", command_id)
    adapter_valid = validate_common_report(
        adapter_id,
        adapter_evidence,
        adapter_document,
        adapter_source,
        "adapter",
        f"{command_id}.{attempt_id}.adapter-observation-report.json",
        trusted_product_policy,
        expected_adapter_argv_sha,
        adapter_redacted,
        verifier=False,
    )
    zero_counts = {
        "discovered": 0,
        "failed": 0,
        "passed": 0,
        "skipped": 0,
    }
    adapter_stream = report_streams.get(adapter_id)
    if (
        adapter_payload.get("resultProtocol") is not None
        or adapter_payload.get("counts") != zero_counts
        or adapter_payload.get("countsSource") != ADAPTER_COUNTS_SOURCE
        or adapter_payload.get("verifiedAdapterReports") != []
        or adapter_payload.get("verificationDescriptorSha256") is not None
        or (
            adapter_stream is not None
            and re.search(
                rb"CAICLI_[A-Z0-9_]*(?:RESULT|COUNTS)[A-Z0-9_]*=",
                adapter_stream[0] + b"\n" + adapter_stream[1],
            )
        )
    ):
        problems.add(
            "PRODUCT_ADAPTER_OBSERVATION",
            location,
            "adapter must remain observation-only with zero counts and no self-attestation marker",
        )
        adapter_valid = False

    adapter_summary = {
        "path": str(adapter_evidence.get("path")),
        "sha256": adapter_document.sha256,
        "commandId": command_id,
        "exitCode": adapter_payload.get("exitCode"),
        "checkoutIdentitySha256": hashlib.sha256(
            _json_bytes(adapter_payload.get("checkoutIdentity"))
        ).hexdigest(),
        "stdoutSha256": _mapping(adapter_payload.get("stdout")).get("sha256"),
        "stderrSha256": _mapping(adapter_payload.get("stderr")).get("sha256"),
    }
    verifier_policy = {
        "policyId": COMMAND_VERIFIER_POLICY_ID,
        "executableRole": ISOLATED_PYTHON_EXECUTABLE_ROLE,
        "descriptorProtocol": COMMAND_VERIFIER_DESCRIPTOR_PROTOCOL,
        "countsSource": "python-unittest-output",
        "shellAllowed": False,
    }
    verifier_redacted = (
        "python-current -I -S -E -B -X utf8 -c "
        "<week84-92-in-memory-command-verifier-v1>"
    )
    expected_tree_dependencies = [
        _report_source_tree_binding(item, control_revision)
        for item in source_trees
    ]
    ordered_verifier_records: list[
        tuple[
            str,
            Mapping[str, Any],
            Document,
            Mapping[str, Any],
            list[dict[str, str]],
        ]
    ] = []
    verifier_valid = True
    for ordinal, verifier_source in enumerate(verifier_sources, start=1):
        role = str(verifier_source.get("role"))
        expected_basename = (
            f"{command_id}.{attempt_id}.verifier-{ordinal:02d}-{role}-"
            "verification-report.json"
        )
        matches = [
            item
            for item in verifier_reports
            if PurePosixPath(str(item[1].get("path"))).name == expected_basename
        ]
        if len(matches) != 1:
            problems.add(
                "PRODUCT_VERIFIER_ORDER",
                location,
                "every sealed verifier requires its exact control-order ordinal report",
            )
            verifier_valid = False
            continue
        evidence_id, evidence, document = matches[0]
        payload = _mapping(document.data)
        valid = validate_common_report(
            evidence_id,
            evidence,
            document,
            verifier_source,
            role,
            expected_basename,
            verifier_policy,
            expected_verifier_argv_sha,
            verifier_redacted,
            verifier=True,
        )
        counts = payload.get("counts")
        stdout_raw = (
            report_streams[evidence_id][0]
            if evidence_id in report_streams
            else None
        )
        marker = None
        encoded = None
        if stdout_raw is not None:
            try:
                lines = stdout_raw.decode("utf-8").splitlines()
            except UnicodeDecodeError:
                lines = []
            prefixes = [
                index
                for index, line in enumerate(lines)
                if line.startswith(COMMAND_VERIFIER_RESULT_MARKER)
            ]
            if prefixes == [len(lines) - 1] and lines:
                encoded = lines[-1][len(COMMAND_VERIFIER_RESULT_MARKER) :]
                try:
                    marker = json.loads(
                        encoded, object_pairs_hook=_no_duplicate_object
                    )
                except (json.JSONDecodeError, DuplicateJsonKey):
                    marker = None
        executed_cases = (
            marker.get("executedCases")
            if isinstance(marker, Mapping)
            else None
        )
        expected_arguments = list(
            _list(
                _mapping(verifier_source.get("verification")).get("arguments")
            )
        )
        marker_valid = (
            isinstance(marker, Mapping)
            and set(marker) == {"protocol", "status", "executedCases", "counts"}
            and marker.get("protocol") == COMMAND_VERIFIER_RESULT_PROTOCOL
            and marker.get("status") == "Passed"
            and isinstance(executed_cases, list)
            and executed_cases
            and all(
                isinstance(item, Mapping)
                and set(item) == {"caseId", "status"}
                and item.get("status") == "Passed"
                for item in executed_cases
            )
            and [item.get("caseId") for item in executed_cases]
            == expected_arguments
            and isinstance(counts, Mapping)
            and set(counts) == {"discovered", "passed", "failed", "skipped"}
            and all(_is_int(value) and value >= 0 for value in counts.values())
            and counts
            == {
                "discovered": len(expected_arguments),
                "passed": len(expected_arguments),
                "failed": 0,
                "skipped": 0,
            }
            and marker.get("counts") == counts
            and isinstance(encoded, str)
            and encoded == _json_bytes(marker).decode("utf-8")
        )
        dependencies = [
            {
                "role": item.get("role"),
                **_report_source_binding(item, control_revision),
            }
            for item in command_sources
            if item is not verifier_source
        ]
        descriptor = {
            "schemaVersion": SCHEMA_VERSION,
            "protocol": COMMAND_VERIFIER_DESCRIPTOR_PROTOCOL,
            "goalId": GOAL_ID,
            "gateId": gate_id,
            "productCandidate": candidate,
            "attemptId": attempt_id,
            "commandRole": role,
            "commandControlBinding": control_binding,
            "verifierSource": _report_source_binding(
                verifier_source, control_revision
            ),
            "sourceDependencies": dependencies,
            "sourceTrees": expected_tree_dependencies,
            "arguments": expected_arguments,
            "verifiesCommandIds": list(
                _list(
                    _mapping(verifier_source.get("verification")).get(
                        "verifiesCommandIds"
                    )
                )
            ),
            "adapterReports": [adapter_summary],
            "checkoutIdentitySha256": adapter_summary[
                "checkoutIdentitySha256"
            ],
            "toolIdentity": tool_identity,
            "gitToolIdentity": git_tool_identity,
            "evidenceRoot": str(
                (descriptor_evidence_root or evidence_root).resolve(strict=True)
            ),
            "selectedDiscovery": selection_summary,
            "runtimeInputs": runtime_inputs,
        }
        if (
            payload.get("resultProtocol") != COMMAND_VERIFIER_RESULT_PROTOCOL
            or payload.get("countsSource") != "python-unittest-output"
            or payload.get("verifiedAdapterReports") != [adapter_summary]
            or payload.get("verificationDescriptorSha256")
            != hashlib.sha256(_json_bytes(descriptor)).hexdigest()
            or not marker_valid
        ):
            problems.add(
                "PRODUCT_VERIFIER_BINDING",
                f"{location}/{evidence_id}",
                "verifier report must reconcile its sealed descriptor, adapter observation, exact cases, and canonical result marker",
            )
            valid = False
        verifier_valid = verifier_valid and valid
        ordered_verifier_records.append(
            (
                evidence_id,
                evidence,
                document,
                payload,
                [dict(item) for item in _list(executed_cases)],
            )
        )

    execution_id, execution_evidence, execution_document = executions[0]
    execution = _mapping(execution_document.data)
    execution_basename = f"{command_id}.{attempt_id}.execution.json"
    document_identity_valid(
        execution_evidence,
        execution_document,
        execution_basename,
        "PRODUCT_EXECUTION_PATH",
    )
    executed_cases = [
        item
        for _evidence_id, _evidence, _document, _payload, cases in ordered_verifier_records
        for item in cases
    ]
    verifier_stdout_blocks = [
        report_streams[evidence_id][0]
        for evidence_id, *_rest in ordered_verifier_records
        if evidence_id in report_streams
    ]
    verifier_stderr_blocks = [
        report_streams[evidence_id][1]
        for evidence_id, *_rest in ordered_verifier_records
        if evidence_id in report_streams
    ]
    execution_keys = {
        "schemaVersion",
        "protocol",
        "goalId",
        "gateId",
        "commandId",
        "productCandidate",
        "controlRevision",
        "runnerKind",
        "selectionManifest",
        "startedAt",
        "completedAt",
        "exitCode",
        "executedCases",
        "executedCaseCount",
        "passed",
        "failed",
        "skipped",
        "executedCaseRootSha256",
        "stdoutSha256",
        "stderrSha256",
        "gitToolIdentity",
    }
    first_verifier_time = (
        report_times.get(ordered_verifier_records[0][0])
        if ordered_verifier_records
        else None
    )
    last_verifier_time = (
        report_times.get(ordered_verifier_records[-1][0])
        if ordered_verifier_records
        else None
    )
    execution_valid = (
        set(execution) == execution_keys
        and execution.get("schemaVersion") == SCHEMA_VERSION
        and execution.get("protocol") == COMMAND_EXECUTION_PROTOCOL
        and execution.get("goalId") == GOAL_ID
        and execution.get("gateId") == gate_id
        and execution.get("commandId") == command_id
        and execution.get("productCandidate") == candidate
        and execution.get("controlRevision") == control_revision
        and execution.get("runnerKind") == "python-unittest"
        and execution.get("selectionManifest")
        == {"path": selection_path, "sha256": selection_document.sha256}
        and first_verifier_time is not None
        and last_verifier_time is not None
        and execution.get("startedAt")
        == _mapping(ordered_verifier_records[0][3]).get("startedAt")
        and execution.get("completedAt")
        == _mapping(ordered_verifier_records[-1][3]).get("finishedAt")
        and execution.get("exitCode") == 0
        and execution.get("executedCases") == executed_cases
        and [item.get("caseId") for item in executed_cases]
        == [item.get("caseId") for item in expected_cases]
        and execution.get("executedCaseCount") == len(executed_cases)
        and execution.get("passed") == len(executed_cases)
        and execution.get("failed") == 0
        and execution.get("skipped") == 0
        and execution.get("executedCaseRootSha256")
        == hashlib.sha256(_json_bytes(executed_cases)).hexdigest()
        and len(verifier_stdout_blocks) == len(verifier_sources)
        and len(verifier_stderr_blocks) == len(verifier_sources)
        and execution.get("stdoutSha256")
        == _length_prefixed_blocks_sha256(verifier_stdout_blocks)
        and execution.get("stderrSha256")
        == _length_prefixed_blocks_sha256(verifier_stderr_blocks)
        and execution.get("gitToolIdentity") == git_tool_identity
    )
    if not execution_valid:
        problems.add(
            "PRODUCT_EXECUTION_BINDING",
            location,
            "execution manifest must exactly reconcile selected and independently executed cases and streams",
        )
    expected_reconciliation = {
        "manifestPath": str(execution_evidence.get("path")),
        "manifestSha256": execution_document.sha256,
        "executedCaseCount": len(executed_cases),
        "executedCaseRootSha256": hashlib.sha256(
            _json_bytes(executed_cases)
        ).hexdigest(),
        "matchesSelection": True,
    }
    if any(
        payload.get("executionReconciliation") != expected_reconciliation
        for _evidence_id, _evidence, _document, payload, _cases
        in ordered_verifier_records
    ):
        problems.add(
            "PRODUCT_EXECUTION_RECONCILIATION",
            location,
            "every verifier report must bind the same successful execution manifest",
        )
        execution_valid = False

    ordered_times = [
        report_times.get(adapter_id),
        *[
            report_times.get(evidence_id)
            for evidence_id, *_rest in ordered_verifier_records
        ],
    ]
    temporal_valid = all(item is not None for item in ordered_times)
    if temporal_valid:
        concrete_times = [item for item in ordered_times if item is not None]
        temporal_valid = all(
            concrete_times[index][1] <= concrete_times[index + 1][0]
            for index in range(len(concrete_times) - 1)
        )
    referenced_ids = {
        item
        for item in _list(command.get("evidenceRefs"))
        if isinstance(item, str)
    }
    required_bundle_ids = {
        adapter_id,
        selection_id,
        execution_id,
        *[
            evidence_id
            for evidence_id, *_rest in ordered_verifier_records
        ],
    }
    if (
        not temporal_valid
        or not required_bundle_ids.issubset(referenced_ids)
        or command.get("status") != "Passed"
        or command.get("exitCode") != 0
        or command.get("redactedCommand") != adapter_redacted
        or command.get("startedAt") != adapter_payload.get("startedAt")
        or not ordered_verifier_records
        or command.get("finishedAt")
        != _mapping(ordered_verifier_records[-1][3]).get("finishedAt")
        or command.get("testCountSource") is not True
    ):
        problems.add(
            "PRODUCT_COMMAND_RECONCILIATION",
            location,
            "Gate command must span adapter then ordered verifiers and reference every two-stage artifact",
        )
        temporal_valid = False

    valid = (
        adapter_valid
        and verifier_valid
        and selection_valid
        and execution_valid
        and temporal_valid
        and len(ordered_verifier_records) == len(verifier_sources)
    )
    return (
        {
            "discovered": len(executed_cases),
            "passed": len(executed_cases),
            "failed": 0,
            "skipped": 0,
        }
        if valid
        else None
    )


def _valid_product_bundle_summary(
    command_id: str,
    bundle_documents: Sequence[
        tuple[str, Mapping[str, Any], Document]
    ],
) -> dict[str, Any] | None:
    """Project one already-validated W84 bundle into its canonical summary."""

    adapters = [
        (evidence, document)
        for _evidence_id, evidence, document in bundle_documents
        if _mapping(document.data).get("commandRole") == "adapter"
    ]
    verifiers = sorted(
        [
            (evidence, document)
            for _evidence_id, evidence, document in bundle_documents
            if _mapping(document.data).get("commandRole") in {"oracle", "test"}
        ],
        key=lambda item: str(item[0].get("path")),
    )
    test_name = W84_G0_VERIFICATION_ARGUMENT_BY_COMMAND.get(command_id)
    if len(adapters) != 1 or len(verifiers) != 1 or test_name is None:
        return None
    adapter_evidence, adapter_document = adapters[0]
    verifier_evidence, verifier_document = verifiers[0]
    adapter = _mapping(adapter_document.data)
    verifier = _mapping(verifier_document.data)
    if (
        adapter.get("exitCode") != 0
        or adapter.get("countsSource") != ADAPTER_COUNTS_SOURCE
        or verifier.get("exitCode") != 0
        or verifier.get("resultProtocol") != COMMAND_VERIFIER_RESULT_PROTOCOL
    ):
        return None
    return {
        "commandId": command_id,
        "adapterReportPath": adapter_evidence.get("path"),
        "adapterReportSha256": adapter_document.sha256,
        "verifierReportPath": verifier_evidence.get("path"),
        "verifierReportSha256": verifier_document.sha256,
        "verifierTestName": test_name,
        "status": "Passed",
    }


def _validate_w84_g0_baseline_identity(
    *,
    repo_root: Path,
    gate: Mapping[str, Any],
    evidence: Sequence[Mapping[str, Any]],
    problems: Problems,
    location: str,
    control_root: Path | None = None,
) -> bool:
    """Validate the exact W84 bootstrap/package identity projection receipt."""

    evidence_root = control_root or repo_root

    def unique_evidence(
        expected_path: str,
        expected_kind: str,
        code: str,
    ) -> tuple[Mapping[str, Any], Document, bytes] | None:
        matches = [item for item in evidence if item.get("path") == expected_path]
        if len(matches) != 1 or matches[0].get("kind") != expected_kind:
            problems.add(
                code,
                location,
                f"W84-G0 requires one {expected_kind} evidence item at {expected_path}",
            )
            return None
        item = matches[0]
        resolved = _safe_relative_evidence_path(
            evidence_root,
            expected_path,
            problems,
            f"{location}/{PurePosixPath(expected_path).name}",
        )
        document = (
            read_document(evidence_root, resolved, problems)
            if resolved is not None and resolved.is_file()
            else None
        )
        try:
            raw = resolved.read_bytes() if resolved is not None else None
        except OSError:
            raw = None
        if (
            document is None
            or raw is None
            or item.get("sha256") != hashlib.sha256(raw).hexdigest()
            or item.get("sha256") != document.sha256
        ):
            problems.add(
                code,
                location,
                "W84-G0 identity evidence must raw-hash-bind one readable JSON file",
            )
            return None
        return item, document, raw

    entry_record = unique_evidence(
        W84_G0_ENTRY_PATH,
        "plan-reference",
        "W84_G0_ENTRY_IDENTITY",
    )
    baseline_record = unique_evidence(
        W84_G0_BASELINE_IDENTITY_PATH,
        "identity",
        "W84_G0_BASELINE_IDENTITY",
    )
    package_path = _safe_relative_evidence_path(
        evidence_root,
        W84_WEEK83_PACKAGE_IDENTITY_PATH,
        problems,
        f"{location}/week83-package-identity",
    )
    package_document = (
        read_document(evidence_root, package_path, problems)
        if package_path is not None and package_path.is_file()
        else None
    )
    try:
        package_raw = package_path.read_bytes() if package_path is not None else None
    except OSError:
        package_raw = None
    if (
        package_document is None
        or package_raw is None
        or hashlib.sha256(package_raw).hexdigest()
        != W84_WEEK83_PACKAGE_IDENTITY_SHA256
    ):
        problems.add(
            "W84_G0_WEEK83_PACKAGE_IDENTITY",
            location,
            "Week83 package identity must match its frozen raw SHA-256",
        )
        return False
    if entry_record is None or baseline_record is None:
        return False

    _entry_item, entry_document, entry_raw = entry_record
    _baseline_item, baseline_document, baseline_raw = baseline_record
    entry = _mapping(entry_document.data)
    package = _mapping(package_document.data)
    package_values = _mapping(package.get("package"))
    renderer = _mapping(package.get("rendererBundle"))
    candidate = _mapping(gate.get("identity")).get("productCandidate")
    git_diff_clean = bool(
        isinstance(candidate, str)
        and _git_return_code(
            repo_root,
            (
                "diff",
                "--no-ext-diff",
                "--no-textconv",
                "--quiet",
                W84_WEEK83_PRODUCT_CANDIDATE,
                candidate,
                "--",
                *W84_PRODUCT_INPUT_PATHS,
            ),
        )
        == 0
    )
    source_valid = (
        entry.get("schemaVersion") == SCHEMA_VERSION
        and entry.get("gateId") == "W84-G0"
        and entry.get("status") == "Passed"
        and entry.get("goalBootstrapRevision") == candidate
        and entry.get("productCandidate") == candidate
        and entry.get("week83ProductCandidate")
        == W84_WEEK83_PRODUCT_CANDIDATE
        and entry.get("week83DocumentationClosure")
        == W84_WEEK83_DOCUMENTATION_CLOSURE
        and entry.get("bootstrapParentRevision") == W84_BOOTSTRAP_PARENT
        and entry.get("productInputPaths") == list(W84_PRODUCT_INPUT_PATHS)
        and entry.get("productInputDiffCount") == 0
        and package.get("evidenceKind") == "package-identity"
        and package.get("status") == "Passed"
        and package.get("exactCleanProductCandidate")
        == W84_WEEK83_PRODUCT_CANDIDATE
        and package.get("sourceDirtyAtPackageBuild") is False
        and git_diff_clean
    )
    expected = {
        "schemaVersion": SCHEMA_VERSION,
        "gateId": "W84-G0",
        "status": "Passed",
        "productCandidate": candidate,
        "goalBootstrapRevision": candidate,
        "week83ProductCandidate": W84_WEEK83_PRODUCT_CANDIDATE,
        "week83DocumentationClosure": W84_WEEK83_DOCUMENTATION_CLOSURE,
        "bootstrapParentRevision": W84_BOOTSTRAP_PARENT,
        "entrySource": {
            "path": W84_G0_ENTRY_PATH,
            "sha256": hashlib.sha256(entry_raw).hexdigest(),
        },
        "week83PackageIdentitySource": {
            "path": W84_WEEK83_PACKAGE_IDENTITY_PATH,
            "sha256": W84_WEEK83_PACKAGE_IDENTITY_SHA256,
        },
        "productInputEquivalence": {
            "fromRevision": W84_WEEK83_PRODUCT_CANDIDATE,
            "toRevision": candidate,
            "paths": list(W84_PRODUCT_INPUT_PATHS),
            "differenceCount": 0,
        },
        "packageIdentity": {
            "desktopPackage": {
                "sha256": package_values.get("desktopSha256"),
                "bytes": package_values.get("desktopBytes"),
            },
            "packageTree": {
                "sha256": package_values.get("treeSha256"),
                "bytes": package_values.get("treeBytes"),
                "fileCount": package_values.get("fileCount"),
            },
            "appAsar": {
                "sha256": package_values.get("appAsarSha256"),
                "bytes": package_values.get("appAsarBytes"),
            },
            "appHost": {
                "sha256": package_values.get("appHostSha256"),
                "bytes": package_values.get("appHostBytes"),
            },
            "rendererBundle": {
                "name": renderer.get("name"),
                "sha256": renderer.get("sha256"),
            },
        },
    }
    valid = bool(
        source_valid
        and baseline_document.data == expected
        and baseline_raw == _json_bytes(expected)
    )
    if not valid:
        problems.add(
            "W84_G0_BASELINE_IDENTITY_CONTRACT",
            location,
            "baseline-identity must exactly project the frozen entry and Week83 package/tree/app.asar/AppHost/Renderer identities",
        )
    return valid


def _canonical_summary_document(
    repo_root: Path,
    evidence: Sequence[Mapping[str, Any]],
    basename: str,
    problems: Problems,
    location: str,
) -> Document | None:
    matches = [
        item
        for item in evidence
        if PurePosixPath(str(item.get("path", ""))).name == basename
    ]
    if len(matches) != 1 or matches[0].get("kind") != "json":
        problems.add(
            "W84_G0_SUMMARY_SET",
            location,
            "W84-G0 requires exactly one JSON evidence item for each canonical summary",
        )
        return None
    item = matches[0]
    path = _safe_relative_evidence_path(
        repo_root, item.get("path"), problems, f"{location}/{basename}"
    )
    document = (
        read_document(repo_root, path, problems)
        if path is not None and path.is_file()
        else None
    )
    try:
        raw = path.read_bytes() if path is not None else None
    except OSError:
        raw = None
    if (
        document is None
        or not isinstance(document.data, Mapping)
        or item.get("sha256") != document.sha256
        or raw != _json_bytes(document.data)
    ):
        problems.add(
            "W84_G0_SUMMARY_BYTES",
            location,
            "canonical W84-G0 summaries must be raw-hash-bound canonical JSON",
        )
        return None
    return document


def _validate_w84_g0_canonical_summaries(
    *,
    repo_root: Path,
    gate: Mapping[str, Any],
    requirement: Mapping[str, Any],
    evidence: Sequence[Mapping[str, Any]],
    semantic_report: Document | None,
    product_bundles: Mapping[str, Mapping[str, Any]],
    trusted_runner_policy: Mapping[str, Any],
    trusted_test_policy: Mapping[str, Any],
    trusted_product_policy: Mapping[str, Any],
    trusted_git_policy: Mapping[str, Any],
    problems: Problems,
    location: str,
    control_root: Path | None = None,
) -> None:
    """Reconcile the two human-facing W84-G0 summaries to trusted artifacts."""

    evidence_root = control_root or repo_root
    goal_summary = _canonical_summary_document(
        evidence_root,
        evidence,
        "goal-contract-unittest.json",
        problems,
        location,
    )
    executor_summary = _canonical_summary_document(
        evidence_root,
        evidence,
        "trusted-executor-unittest.json",
        problems,
        location,
    )
    if semantic_report is None:
        problems.add(
            "W84_G0_SUMMARY_SEMANTIC_REPORT",
            location,
            "canonical W84-G0 summaries require the unique validated semantic report",
        )
        return
    semantic = _mapping(semantic_report.data)
    semantic_command_id = trusted_test_policy.get("commandId")
    candidate = _mapping(gate.get("identity")).get("productCandidate")
    semantic_test_counts = semantic.get("testCounts")
    expected_goal = {
        "schemaVersion": SCHEMA_VERSION,
        "goalId": GOAL_ID,
        "gateId": "W84-G0",
        "status": "Passed",
        "productCandidate": candidate,
        "semanticCommandId": semantic_command_id,
        "semanticAttemptId": semantic.get("attemptId"),
        "trustedTestReportPath": semantic_report.relative_path,
        "trustedTestReportSha256": semantic_report.sha256,
        "countsSource": semantic.get("countsSource"),
        "testCounts": semantic_test_counts,
    }
    if goal_summary is None or goal_summary.data != expected_goal:
        problems.add(
            "W84_G0_GOAL_SUMMARY_BINDING",
            location,
            "goal-contract summary must exactly bind the semantic report and Gate counts",
        )

    command_order = [
        item
        for item in _list(requirement.get("requiredCommandIds"))
        if isinstance(item, str) and item != semantic_command_id
    ]
    expected_pairs = [product_bundles.get(command_id) for command_id in command_order]
    if any(item is None for item in expected_pairs):
        problems.add(
            "W84_G0_SUMMARY_PRODUCT_BUNDLES",
            location,
            "executor summary requires every validated W84-G0 adapter/verifier pair",
        )
    policy_inputs = {
        "trustedExecutor": (
            trusted_runner_policy.get("runnerId"), trusted_runner_policy
        ),
        "trustedTest": (
            trusted_test_policy.get("policyId"), trusted_test_policy
        ),
        "trustedProduct": (
            trusted_product_policy.get("policyId"), trusted_product_policy
        ),
        "trustedGit": (
            trusted_git_policy.get("policyId"), trusted_git_policy
        ),
    }
    expected_policies = {
        field: {
            "identityId": identity_id,
            "sha256": hashlib.sha256(_json_bytes(policy)).hexdigest(),
        }
        for field, (identity_id, policy) in policy_inputs.items()
    }
    expected_executor = {
        "schemaVersion": SCHEMA_VERSION,
        "goalId": GOAL_ID,
        "gateId": "W84-G0",
        "status": "Passed",
        "productCandidate": candidate,
        "executorSource": semantic.get("runnerSource"),
        "commandControl": gate.get("commandControlBinding"),
        "trustedPolicies": expected_policies,
        "semanticReport": {
            "path": semantic_report.relative_path,
            "sha256": semantic_report.sha256,
            "commandId": semantic_command_id,
            "attemptId": semantic.get("attemptId"),
            "countsSource": semantic.get("countsSource"),
            "testCounts": semantic_test_counts,
            "status": "Passed",
        },
        "adapterVerifierPairs": expected_pairs,
    }
    if executor_summary is None or executor_summary.data != expected_executor:
        problems.add(
            "W84_G0_EXECUTOR_SUMMARY_BINDING",
            location,
            "trusted-executor summary must exactly bind policies, semantic report, control, and all five product pairs",
        )


def _validate_requirement_evidence_payload(
    evidence: Mapping[str, Any],
    gate: Mapping[str, Any],
    repo_root: Path | None,
    minimum_tests: int,
    problems: Problems,
    location: str,
    trusted_runner_policy: Mapping[str, Any],
    trusted_test_policy: Mapping[str, Any],
    trusted_git_policy: Mapping[str, Any],
    required_evidence_basenames: set[str],
    *,
    control_root: Path | None = None,
) -> bool:
    """Validate the minimum machine-readable shape of required JSON evidence.

    Returns true only when this item is a test-report whose canonical counts
    exactly bind the Gate's declared testCounts.
    """
    if repo_root is None:
        return False
    evidence_root = control_root or repo_root
    resolved = _safe_relative_evidence_path(
        evidence_root,
        evidence.get("path"),
        problems,
        f"{location}/path",
    )
    if resolved is None:
        return False
    payload_document = read_document(evidence_root, resolved, problems)
    if payload_document is None:
        problems.add(
            "REQUIREMENTS_EVIDENCE_PAYLOAD_READ",
            location,
            "required JSON evidence must be readable JSON",
        )
        return False
    payload = payload_document.data
    if not isinstance(payload, Mapping):
        problems.add(
            "REQUIREMENTS_EVIDENCE_PAYLOAD_TYPE",
            location,
            "required JSON evidence payload must be an object",
        )
        return False

    gate_id = gate.get("gateId")
    candidate = _mapping(gate.get("identity")).get("productCandidate")
    kind = evidence.get("kind")
    count_container = _payload_count_container(payload)
    normalised_counts: dict[str, int] | None = None
    if count_container is not None:
        normalised_counts = _normalise_payload_test_counts(
            count_container,
            location,
            problems,
        )

    if kind == "test-report":
        provenance_valid = _validate_test_report_provenance(
            payload,
            payload_document,
            gate,
            repo_root,
            problems,
            location,
            trusted_runner_policy,
            trusted_test_policy,
            trusted_git_policy,
            required_evidence_basenames,
            control_root=evidence_root,
        )
        if normalised_counts is None:
            if count_container is None:
                problems.add(
                    "REQUIREMENTS_EVIDENCE_TEST_REPORT_SHAPE",
                    location,
                    "test-report JSON must contain discovered/total, passed, and failed counts",
                )
            return False
        gate_counts = _mapping(gate.get("testCounts"))
        expected_counts = {
            key: gate_counts.get(key)
            for key in (
                "discovered",
                "passed",
                "failed",
                "skipped",
                "notRun",
                "notApplicable",
            )
        }
        if normalised_counts != expected_counts:
            problems.add(
                "REQUIREMENTS_EVIDENCE_TEST_REPORT_BINDING",
                location,
                "test-report counts must exactly match the Gate testCounts",
            )
            return False
        if normalised_counts["passed"] < minimum_tests:
            problems.add(
                "REQUIREMENTS_EVIDENCE_TEST_REPORT_STATUS",
                location,
                "test-report passed count is below the frozen Gate minimum",
            )
            return False
        return provenance_valid

    anchored = False
    if "gateId" in payload:
        if payload.get("gateId") != gate_id:
            problems.add(
                "REQUIREMENTS_EVIDENCE_PAYLOAD_BINDING",
                location,
                "required evidence gateId does not match its Gate",
            )
        else:
            anchored = True

    candidate_values = [
        payload.get("productCandidate"),
        _mapping(payload.get("identity")).get("productCandidate"),
        _mapping(payload.get("candidateIdentity")).get("productCandidate"),
    ]
    candidate_values = [value for value in candidate_values if value is not None]
    if candidate_values:
        if not isinstance(candidate, str) or any(
            value != candidate for value in candidate_values
        ):
            problems.add(
                "REQUIREMENTS_EVIDENCE_PAYLOAD_BINDING",
                location,
                "required evidence productCandidate does not match its Gate",
            )
        else:
            anchored = True

    entries = payload.get("entries")
    if isinstance(entries, list) and any(
        isinstance(entry, Mapping)
        and entry.get("gateId") == gate_id
        and entry.get("productCandidate") == candidate
        for entry in entries
    ):
        anchored = True

    if "status" in payload:
        if payload.get("status") != "Passed":
            problems.add(
                "REQUIREMENTS_EVIDENCE_PAYLOAD_STATUS",
                location,
                "required evidence status must be Passed",
            )
        else:
            anchored = True
    if payload.get("ok") is True or payload.get("passed") is True:
        anchored = True
    if payload.get("decision") in {
        "ReadyForNextCheckpoint",
        "GoalComplete",
        "RefactorAccepted",
    }:
        anchored = True
    if normalised_counts is not None:
        anchored = True

    if not anchored:
        problems.add(
            "REQUIREMENTS_EVIDENCE_PAYLOAD_BINDING",
            location,
            "required JSON evidence must bind Gate/candidate/success status or balanced test counts",
        )
    return False


def _expected_command_control_policy(
    contract: Contract,
    gate_id: str,
) -> dict[str, Any] | None:
    group = contract.gate_to_group.get(gate_id)
    if group is None:
        return None
    gate_index = group.gate_ids.index(gate_id)
    if gate_id == "W84-G0":
        predecessor_mode = "w84-bootstrap-exception"
        source_trust = "bootstrap-external-review"
    elif gate_id == "W90-G0":
        predecessor_mode = "w90-integration-entry-base"
        source_trust = "prior-sealed"
    elif gate_index == 0:
        predecessor_mode = "entry-base"
        source_trust = "prior-sealed"
    else:
        predecessor_mode = "prior-gate"
        source_trust = "prior-sealed"
    return {
        "path": f"{COMMAND_CONTROL_ROOT}/{gate_id}.json",
        "sourceTrust": source_trust,
        "predecessorMode": predecessor_mode,
        "rolePolicy": COMMAND_CONTROL_ROLE_POLICY,
    }


def _git_commit_changed_paths(
    repo_root: Path,
    revision: str,
) -> set[str] | None:
    output = _git_stdout(
        repo_root,
        (
            "diff-tree",
            "--no-commit-id",
            "--name-only",
            "-r",
            revision,
        ),
    )
    if output is None:
        return None
    return {
        value.replace("\\", "/")
        for value in output.splitlines()
        if value
    }


def _git_raw_diff_entries(
    repo_root: Path,
    before_revision: str,
    after_revision: str,
) -> dict[str, tuple[str, str, str, str, str]] | None:
    """Return exact non-rename raw Git changes keyed by safe UTF-8 path."""

    raw = _git_bytes(
        repo_root,
        (
            "diff-tree",
            "-r",
            "--raw",
            "--no-abbrev",
            "--no-renames",
            "-z",
            before_revision,
            after_revision,
        ),
    )
    if raw is None:
        return None
    records = raw.split(b"\0")
    if records and records[-1] == b"":
        records.pop()
    if len(records) % 2 != 0:
        return None
    result: dict[str, tuple[str, str, str, str, str]] = {}
    for index in range(0, len(records), 2):
        try:
            header = records[index].decode("ascii")
            path = records[index + 1].decode("utf-8")
        except UnicodeDecodeError:
            return None
        match = re.fullmatch(
            r":([0-7]{6}) ([0-7]{6}) ([0-9a-f]{40}) ([0-9a-f]{40}) ([A-Z])",
            header,
        )
        if match is None or not _is_exact_safe_source_path(path) or path in result:
            return None
        result[path] = (
            match.group(1),
            match.group(2),
            match.group(3),
            match.group(4),
            match.group(5),
        )
    return result


def _git_touched_paths_between(
    repo_root: Path,
    ancestor: str,
    descendant: str,
) -> set[str] | None:
    output = _git_stdout(
        repo_root,
        (
            "log",
            "--format=",
            "--name-only",
            f"{ancestor}..{descendant}",
        ),
    )
    if output is None:
        return None
    return {
        value.replace("\\", "/")
        for value in output.splitlines()
        if value
    }


def _command_control_handoff_candidate(
    handoff_by_group: Mapping[tuple[int, str], Document],
    group_key: tuple[int, str],
) -> Any:
    document = handoff_by_group.get(group_key)
    return (
        _mapping(_mapping(document.data).get("identity")).get(
            "productCandidate"
        )
        if document is not None
        else None
    )


def _expected_command_predecessor(
    gate_id: str,
    mode: str,
    contract: Contract,
    gate_by_id: Mapping[str, Document],
    handoff_by_group: Mapping[tuple[int, str], Document],
    problems: Problems,
    location: str,
) -> str | None:
    group = contract.gate_to_group.get(gate_id)
    if group is None:
        return None
    if mode == "w84-bootstrap-exception":
        return W84_BOOTSTRAP_PARENT
    if mode == "prior-gate":
        index = group.gate_ids.index(gate_id)
        if index == 0:
            problems.add(
                "COMMAND_CONTROL_PREDECESSOR",
                location,
                "prior-gate mode requires an immediately preceding Gate",
            )
            return None
        predecessor = gate_by_id.get(group.gate_ids[index - 1])
        value = _mapping(
            _mapping(predecessor.data if predecessor is not None else {}).get(
                "identity"
            )
        ).get("productCandidate")
        return value if isinstance(value, str) else None

    parent_groups = HANDOFF_PARENT_GROUPS.get(group.key, ())
    parent_candidates = [
        _command_control_handoff_candidate(handoff_by_group, parent)
        for parent in parent_groups
    ]
    if mode == "entry-base":
        if len(parent_candidates) != 1 or not isinstance(
            parent_candidates[0], str
        ):
            problems.add(
                "COMMAND_CONTROL_ENTRY_BASE",
                location,
                "entry-base requires one canonical ready parent handoff candidate",
            )
            return None
        return parent_candidates[0]
    if mode != "w90-integration-entry-base":
        return None
    if (
        len(parent_candidates) != 2
        or any(not isinstance(value, str) for value in parent_candidates)
    ):
        problems.add(
            "COMMAND_CONTROL_INTEGRATION_BASE",
            location,
            "W90 entry base requires both W89 lane handoff candidates",
        )
        return None
    # The prepared revision itself is declared by the control document.  Its
    # exact two parents are checked by the caller against these candidates.
    return "|".join(str(value) for value in parent_candidates)


def _command_source_local_imports(
    repo_root: Path,
    relative_path: str,
    raw: bytes,
) -> set[str]:
    try:
        tree = ast.parse(raw.decode("utf-8"), filename=relative_path)
    except (UnicodeDecodeError, SyntaxError):
        return set()
    parent = PurePosixPath(relative_path).parent
    candidates: set[str] = set()

    def add_module(module: str, base: PurePosixPath | None = None) -> None:
        module_parts = tuple(part for part in module.split(".") if part)
        if not module_parts:
            return
        roots = [PurePosixPath(*module_parts)]
        if base is not None:
            roots.insert(0, base.joinpath(*module_parts))
        for root in roots:
            for candidate in (
                root.with_suffix(".py"),
                root / "__init__.py",
            ):
                value = candidate.as_posix()
                if (repo_root / value).is_file():
                    candidates.add(value)

    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for alias in node.names:
                add_module(alias.name, parent)
        elif isinstance(node, ast.ImportFrom):
            base = parent
            for _ in range(max(node.level - 1, 0)):
                base = base.parent
            module = node.module or ""
            if module:
                add_module(module, base if node.level else None)
            for alias in node.names:
                if alias.name != "*":
                    combined = f"{module}.{alias.name}" if module else alias.name
                    add_module(combined, base if node.level else None)
    return candidates


def _normalise_literal_repo_path(value: str) -> str | None:
    candidate = value.replace("\\", "/")
    path = PurePosixPath(candidate)
    if path.is_absolute() or any(part in {"", ".", ".."} for part in path.parts):
        if candidate in {"", "."}:
            return ""
        return None
    return path.as_posix()


def _is_exact_safe_source_path(value: Any) -> bool:
    if (
        not isinstance(value, str)
        or not value
        or "\\" in value
        or any(ord(character) < 32 or ord(character) == 127 for character in value)
    ):
        return False
    path = PurePosixPath(value)
    if (
        path.is_absolute()
        or path.as_posix() != value
        or any(part in {"", ".", ".."} for part in path.parts)
    ):
        return False
    for part in path.parts:
        stem = part.split(".", 1)[0].casefold()
        if stem in {"con", "prn", "aux", "nul"} or re.fullmatch(
            r"(?:com|lpt)[1-9]", stem
        ):
            return False
    return True


def _path_is_below(path: PurePosixPath, root: PurePosixPath) -> bool:
    try:
        path.relative_to(root)
    except ValueError:
        return False
    return path != root


def _command_control_source_path_allowed(
    gate_id: str,
    item: Mapping[str, Any],
) -> bool:
    path_value = item.get("path")
    role = item.get("role")
    command_id = item.get("commandId")
    if not _is_exact_safe_source_path(path_value):
        return False
    assert isinstance(path_value, str)
    path = PurePosixPath(path_value)
    if path.name in COMMAND_CONTROL_FORBIDDEN_SOURCE_BASENAMES:
        return False
    if path.suffix == ".pth":
        return False
    if role == "adapter":
        return (
            isinstance(command_id, str)
            and path_value
            == f"{TRUSTED_PRODUCT_SCRIPT_ROOT}/{command_id}.py"
        )
    if role in {"oracle", "test"}:
        # Verifier authority currently belongs only to exact Python unittest
        # sources executed by the isolated trusted wrapper.  C#/TS test bytes
        # remain sealed subjects (fixture/parser) until a separate runner
        # policy is implemented and provenance-bound.
        return path_value.startswith("tools/") and path.suffix == ".py"
    if role not in {"fixture", "parser"}:
        return False
    if path_value.startswith("tools/"):
        return True
    if gate_id == "W84-G0" and path_value in W84_G0_SOURCE_UNIVERSE:
        return (
            item.get("origin") == "bootstrap"
            and item.get("originControlRevision") is None
            and item.get("verification") is None
        )
    if path_value in COMMAND_CONTROL_TEST_CONFIG_PATHS:
        return True
    if _path_is_below(path, COMMAND_CONTROL_CSHARP_TEST_ROOT):
        return (
            path.suffix in {".cs", ".csproj"}
            and not any(part.casefold() in {"bin", "obj"} for part in path.parts)
        )
    if _path_is_below(path, COMMAND_CONTROL_DESKTOP_UNIT_ROOT):
        return path.name.endswith((".test.ts", ".test.tsx"))
    if _path_is_below(path, COMMAND_CONTROL_DESKTOP_E2E_ROOT):
        return path.name.endswith(".spec.ts")
    return False


def _path_has_reparse_component(repo_root: Path, relative_path: str) -> bool:
    current = repo_root
    for part in PurePosixPath(relative_path).parts:
        current = current / part
        try:
            status = os.lstat(current)
        except OSError:
            return True
        if current.is_symlink() or bool(
            getattr(status, "st_file_attributes", 0) & 0x400
        ):
            return True
    return False


def _source_tree_base_path_selected(include_policy: str, relative_path: str) -> bool:
    path = PurePosixPath(relative_path)
    name = path.name
    if not _is_exact_safe_source_path(relative_path):
        return False
    if name == ".gitattributes":
        return False
    if include_policy == "csharp-test-tree-v1":
        if _path_is_below(path, COMMAND_CONTROL_CSHARP_TEST_ROOT):
            return not any(
                part.casefold() in {"bin", "obj"} for part in path.parts
            )
        return (
            relative_path in {"global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.Config"}
            or (
                len(path.parts) == 1
                and name.endswith((".sln", ".slnx"))
            )
            or (
                path.parts
                and path.parts[0] == "src"
                and name.endswith(".csproj")
            )
        )
    if include_policy == "desktop-unit-test-tree-v1":
        return (
            (
                _path_is_below(path, COMMAND_CONTROL_DESKTOP_UNIT_ROOT)
                and name.endswith((".test.ts", ".test.tsx"))
            )
            or relative_path
            in {
                "apps/desktop/src/renderer/test-setup.ts",
                "apps/desktop/package.json",
                "apps/desktop/package-lock.json",
                "apps/desktop/tsconfig.json",
                "apps/desktop/vite.config.ts",
                "apps/desktop/vitest.config.ts",
            }
        )
    if include_policy == "desktop-e2e-test-tree-v1":
        return (
            _path_is_below(path, COMMAND_CONTROL_DESKTOP_E2E_ROOT)
            or relative_path
            in {
                "apps/desktop/package.json",
                "apps/desktop/package-lock.json",
                "apps/desktop/tsconfig.json",
                "apps/desktop/vite.config.ts",
                "apps/desktop/vitest.config.ts",
                "apps/desktop/playwright.config.ts",
            }
        )
    return False


def _source_tree_path_selected(
    include_policy: str,
    relative_path: str,
    selected_targets: Sequence[str] = (),
) -> bool:
    """Select policy inputs plus only .gitattributes that can affect them."""

    if _source_tree_base_path_selected(include_policy, relative_path):
        return True
    if (
        not _is_exact_safe_source_path(relative_path)
        or PurePosixPath(relative_path).name != ".gitattributes"
    ):
        return False
    attribute_parent = PurePosixPath(relative_path).parent
    parent_parts = () if relative_path == ".gitattributes" else attribute_parent.parts
    return any(
        len(PurePosixPath(target).parts) > len(parent_parts)
        and PurePosixPath(target).parts[: len(parent_parts)] == parent_parts
        for target in selected_targets
        if _source_tree_base_path_selected(include_policy, target)
    )


def _source_tree_prepared_path_allowed(
    include_policy: str, relative_path: str
) -> bool:
    """Limit reviewed control changes to non-production test/harness bytes."""

    if not _is_exact_safe_source_path(relative_path):
        return False
    path = PurePosixPath(relative_path)
    if include_policy == "csharp-test-tree-v1":
        return _path_is_below(path, COMMAND_CONTROL_CSHARP_TEST_ROOT)
    if include_policy == "desktop-unit-test-tree-v1":
        return (
            _path_is_below(path, COMMAND_CONTROL_DESKTOP_UNIT_ROOT)
            and path.name.endswith((".test.ts", ".test.tsx"))
        ) or relative_path in {
            "apps/desktop/src/renderer/test-setup.ts",
            "apps/desktop/vitest.config.ts",
        }
    if include_policy == "desktop-e2e-test-tree-v1":
        return _path_is_below(
            path, COMMAND_CONTROL_DESKTOP_E2E_ROOT
        ) or relative_path == "apps/desktop/playwright.config.ts"
    return False


def _parse_git_tree_inventory(raw: bytes) -> list[tuple[str, str, str]] | None:
    result: list[tuple[str, str, str]] = []
    for record in raw.split(b"\0"):
        if not record:
            continue
        try:
            header, encoded_path = record.split(b"\t", 1)
            mode, object_type, object_id = header.decode("ascii").split(" ")
            path = encoded_path.decode("utf-8")
        except (ValueError, UnicodeDecodeError):
            return None
        if (
            re.fullmatch(r"[0-7]{6}", mode) is None
            or object_type not in {"blob", "commit"}
            or re.fullmatch(r"[0-9a-f]{40}", object_id) is None
            or not _is_exact_safe_source_path(path)
        ):
            return None
        result.append((mode, object_id, path))
    return result


def _source_tree_revision_inventory(
    repo_root: Path,
    revision: str,
    include_policy: str,
) -> list[dict[str, Any]] | None:
    raw_tree = _git_bytes(repo_root, ("ls-tree", "-r", "-z", revision))
    parsed = _parse_git_tree_inventory(raw_tree or b"") if raw_tree is not None else None
    if parsed is None:
        return None
    selected_targets = [
        path
        for _mode, _blob, path in parsed
        if _source_tree_base_path_selected(include_policy, path)
    ]
    selected = [
        (mode, blob, path)
        for mode, blob, path in parsed
        if _source_tree_path_selected(include_policy, path, selected_targets)
    ]
    if (
        not selected
        or len(selected) > MAX_COMMAND_CONTROL_SOURCE_TREE_ENTRIES
        or any(mode != "100644" for mode, _blob, _path in selected)
    ):
        return None
    collision_keys = [
        unicodedata.normalize("NFC", path).casefold()
        for _mode, _blob, path in selected
    ]
    if len(set(collision_keys)) != len(collision_keys):
        return None
    blob_contents = _git_cat_file_blobs(
        repo_root, [blob for _mode, blob, _path in selected]
    )
    if blob_contents is None:
        return None
    result: list[dict[str, Any]] = []
    total_bytes = 0
    for _mode, blob, path in sorted(selected, key=lambda value: value[2]):
        raw = blob_contents.get(blob)
        if raw is None:
            return None
        total_bytes += len(raw)
        if total_bytes > MAX_COMMAND_CONTROL_SOURCE_TREE_BYTES:
            return None
        result.append(
            {
                "path": path,
                "sha256": hashlib.sha256(raw).hexdigest(),
                "gitBlobSha": blob,
                "bytes": len(raw),
            }
        )
    return result


def _source_tree_worktree_inventory(
    repo_root: Path,
    revision_inventory: Sequence[Mapping[str, Any]],
) -> list[dict[str, Any]] | None:
    result: list[dict[str, Any]] = []
    for expected in revision_inventory:
        path = expected.get("path")
        if not isinstance(path, str) or _path_has_reparse_component(repo_root, path):
            return None
        target = repo_root / PurePosixPath(path)
        try:
            raw = target.read_bytes()
        except OSError:
            return None
        result.append(
            {
                "path": path,
                "sha256": hashlib.sha256(raw).hexdigest(),
                # The Git blob is independently fixed by the revision/index
                # inventory.  contentRootSha256 intentionally binds the bytes
                # actually materialized for the test runner, which may differ
                # under text:auto on Windows.
                "gitBlobSha": expected.get("gitBlobSha"),
                "bytes": len(raw),
            }
        )
    return result


def _source_tree_index_inventory(
    repo_root: Path,
    include_policy: str,
) -> list[dict[str, Any]] | None:
    raw_index = _git_bytes(repo_root, ("ls-files", "--stage", "-z"))
    if raw_index is None:
        return None
    parsed: list[tuple[str, str, str, str]] = []
    for record in raw_index.split(b"\0"):
        if not record:
            continue
        try:
            header, encoded_path = record.split(b"\t", 1)
            mode, blob, stage = header.decode("ascii").split(" ")
            path = encoded_path.decode("utf-8")
        except (ValueError, UnicodeDecodeError):
            return None
        if (
            not _is_exact_safe_source_path(path)
            or re.fullmatch(r"[0-7]{6}", mode) is None
            or re.fullmatch(r"[0-9a-f]{40}", blob) is None
        ):
            return None
        parsed.append((mode, blob, stage, path))
    selected_targets = [
        path
        for _mode, _blob, _stage, path in parsed
        if _source_tree_base_path_selected(include_policy, path)
    ]
    selected: list[tuple[str, str]] = []
    for mode, blob, stage, path in parsed:
        if _source_tree_path_selected(include_policy, path, selected_targets):
            if mode != "100644" or stage != "0":
                return None
            selected.append((blob, path))
    blob_contents = _git_cat_file_blobs(repo_root, [blob for blob, _path in selected])
    if blob_contents is None:
        return None
    result: list[dict[str, Any]] = []
    for blob, path in sorted(selected, key=lambda value: value[1]):
        raw = blob_contents.get(blob)
        if raw is None:
            return None
        result.append(
            {
                "path": path,
                "sha256": hashlib.sha256(raw).hexdigest(),
                "gitBlobSha": blob,
                "bytes": len(raw),
            }
        )
    return result


def _source_tree_content_root(inventory: Sequence[Mapping[str, Any]]) -> str:
    return hashlib.sha256(_json_bytes(list(inventory))).hexdigest()


def _command_control_production_projection_inventory(
    repo_root: Path,
    revision: str,
    excluded_paths: set[str],
) -> list[dict[str, str]] | None:
    """Project one Git tree after removing the exact reviewed control inputs."""

    if any(not _is_exact_safe_source_path(path) for path in excluded_paths):
        return None
    raw_tree = _git_bytes(repo_root, ("ls-tree", "-r", "-z", revision))
    parsed = _parse_git_tree_inventory(raw_tree or b"") if raw_tree is not None else None
    if parsed is None:
        return None
    projected = [
        {
            "path": path,
            "mode": mode,
            "objectType": "commit" if mode == "160000" else "blob",
            "gitObjectId": object_id,
        }
        for mode, object_id, path in parsed
        if path not in excluded_paths
    ]
    if (
        len(projected) > MAX_COMMAND_CONTROL_PRODUCTION_PROJECTION_ENTRIES
        or len(
            {
                unicodedata.normalize("NFC", item["path"]).casefold()
                for item in projected
            }
        )
        != len(projected)
    ):
        return None
    return sorted(projected, key=lambda item: item["path"])


def _command_control_production_projection_root(
    inventory: Sequence[Mapping[str, Any]],
) -> str:
    return hashlib.sha256(_json_bytes(list(inventory))).hexdigest()


def _validate_command_control_source_tree(
    repo_root: Path,
    tree: Mapping[str, Any],
    control_revision: str,
    prepared_from_revision: str,
    product_candidate: str,
    product_commands: Sequence[str],
    current_gate_id: str,
    gate_by_id: Mapping[str, Document],
    problems: Problems,
    location: str,
) -> set[str]:
    expected_keys = {
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
    include_policy = tree.get("includePolicy")
    expected_root = COMMAND_CONTROL_SOURCE_TREE_POLICIES.get(str(include_policy))
    origin = tree.get("origin")
    origin_revision = tree.get("originControlRevision")
    prepared_paths = tree.get("preparedPaths")
    if (
        set(tree) != expected_keys
        or tree.get("commandId") not in product_commands
        or tree.get("role") != "fixture"
        or tree.get("rootPath") != expected_root
        or origin not in {"prepared", "prior-control"}
        or not isinstance(prepared_paths, list)
        or any(
            not isinstance(path, str)
            or not _source_tree_prepared_path_allowed(str(include_policy), path)
            for path in prepared_paths
        )
        or prepared_paths != sorted(set(prepared_paths))
        or (
            origin == "prepared"
            and (origin_revision is not None or not prepared_paths)
        )
        or (
            origin == "prior-control"
            and (
                prepared_paths != []
                or
                not isinstance(origin_revision, str)
                or re.fullmatch(r"[0-9a-f]{40}", origin_revision) is None
                or _git_return_code(
                    repo_root,
                    (
                        "merge-base",
                        "--is-ancestor",
                        origin_revision,
                        prepared_from_revision,
                    ),
                )
                != 0
            )
        )
    ):
        problems.add(
            "COMMAND_CONTROL_SOURCE_TREE_SHAPE",
            location,
            "sourceTree must use one exact non-authority policy with sorted prepared A/M paths or an empty prior-control path list",
        )
        return set()
    root_tree_sha = _git_stdout(
        repo_root,
        (
            "rev-parse",
            (
                f"{control_revision}^{{tree}}"
                if expected_root == "."
                else f"{control_revision}:{expected_root}"
            ),
        ),
    )
    control_inventory = _source_tree_revision_inventory(
        repo_root, control_revision, str(include_policy)
    )
    candidate_inventory = _source_tree_revision_inventory(
        repo_root, product_candidate, str(include_policy)
    )
    head_inventory = _source_tree_revision_inventory(
        repo_root, "HEAD", str(include_policy)
    )
    index_inventory = _source_tree_index_inventory(repo_root, str(include_policy))
    worktree_inventory = (
        _source_tree_worktree_inventory(repo_root, control_inventory)
        if control_inventory is not None
        else None
    )
    untracked_raw = _git_bytes(
        repo_root,
        ("ls-files", "--others", "--exclude-standard", "-z"),
    )
    ignored_raw = _git_bytes(
        repo_root,
        ("ls-files", "--others", "--ignored", "--exclude-standard", "-z"),
    )
    extra_paths: set[str] = set()
    selected_targets = [
        str(item.get("path"))
        for item in (control_inventory or [])
        if _source_tree_base_path_selected(
            str(include_policy), str(item.get("path"))
        )
    ]
    for raw in (untracked_raw, ignored_raw):
        if raw is None:
            extra_paths.add("<git-unavailable>")
            continue
        for encoded in raw.split(b"\0"):
            if not encoded:
                continue
            try:
                path = encoded.decode("utf-8")
            except UnicodeDecodeError:
                extra_paths.add("<invalid-utf8>")
                continue
            if _source_tree_path_selected(
                str(include_policy), path, selected_targets
            ):
                extra_paths.add(path)
    total_bytes = (
        sum(int(item.get("bytes", -1)) for item in worktree_inventory)
        if worktree_inventory is not None
        else -1
    )
    attributes_safe = _git_attributes_are_materialization_safe_batch(
        repo_root,
        [str(item.get("path")) for item in (control_inventory or [])],
        problems,
        "COMMAND_CONTROL_SOURCE_TREE",
    )
    raw_changes = _git_raw_diff_entries(
        repo_root, prepared_from_revision, control_revision
    )
    selected_changes = {
        path: change
        for path, change in (raw_changes or {}).items()
        if _source_tree_path_selected(
            str(include_policy), path, selected_targets
        )
    }
    control_blob_by_path = {
        str(item.get("path")): str(item.get("gitBlobSha"))
        for item in (control_inventory or [])
    }
    prepared_change_valid = (
        raw_changes is not None
        and set(prepared_paths) == set(selected_changes)
        and all(
            (
                change[4] == "A"
                and change[0] == "000000"
                and change[1] == "100644"
                and change[2] == "0" * 40
                and control_blob_by_path.get(path) == change[3]
            )
            or (
                change[4] == "M"
                and change[0] == "100644"
                and change[1] == "100644"
                and change[2] != change[3]
                and control_blob_by_path.get(path) == change[3]
            )
            for path, change in selected_changes.items()
        )
    )
    if origin == "prior-control":
        prepared_change_valid = raw_changes is not None and not selected_changes

    valid = (
        isinstance(root_tree_sha, str)
        and re.fullmatch(r"[0-9a-f]{40}", root_tree_sha) is not None
        and tree.get("gitTreeSha") == root_tree_sha
        and _git_stdout(repo_root, ("cat-file", "-t", root_tree_sha)) == "tree"
        and control_inventory is not None
        and candidate_inventory == control_inventory
        and head_inventory == control_inventory
        and index_inventory == control_inventory
        and worktree_inventory is not None
        and [
            (item.get("path"), item.get("gitBlobSha"))
            for item in worktree_inventory
        ]
        == [
            (item.get("path"), item.get("gitBlobSha"))
            for item in control_inventory
        ]
        and attributes_safe
        and not extra_paths
        and prepared_change_valid
        and tree.get("entryCount") == len(control_inventory or [])
        and tree.get("totalBytes") == total_bytes
        and tree.get("contentRootSha256")
        == _source_tree_content_root(worktree_inventory or [])
        and _git_stdout(
            repo_root,
            (
                "log",
                "--format=%H",
                f"{control_revision}..{product_candidate}",
                "--",
                *[str(item.get("path")) for item in (control_inventory or [])],
            ),
        )
        == ""
    )
    if origin == "prior-control":
        origin_control = _canonical_prior_control_document(
            repo_root,
            str(origin_revision),
            current_gate_id,
            gate_by_id,
            problems,
            location,
        )
        matches = [
            item
            for item in _list(
                origin_control.get("sourceTrees")
                if isinstance(origin_control, Mapping)
                else None
            )
            if isinstance(item, Mapping)
            and all(
                item.get(key) == tree.get(key)
                for key in {
                    "commandId",
                    "role",
                    "rootPath",
                    "gitTreeSha",
                    "entryCount",
                    "totalBytes",
                    "contentRootSha256",
                    "includePolicy",
                }
            )
        ]
        valid = valid and len(matches) == 1
    if not valid:
        problems.add(
            "COMMAND_CONTROL_SOURCE_TREE_IDENTITY",
            location,
            "sourceTree must bind one collision-free tracked 100644 inventory unchanged across control, candidate, HEAD, index materialization, and worktree",
        )
        return set()
    return {str(item.get("path")) for item in control_inventory or []}


def _literal_string_values(
    node: ast.AST,
    bindings: Mapping[str, set[str]],
    *,
    depth: int = 0,
) -> set[str] | None:
    if depth > 16:
        return None
    if isinstance(node, ast.Constant) and isinstance(node.value, str):
        return {node.value}
    if isinstance(node, ast.Name):
        values = bindings.get(node.id)
        return set(values) if values is not None else None
    if isinstance(node, (ast.Tuple, ast.List, ast.Set)):
        result: set[str] = set()
        for element in node.elts:
            values = _literal_string_values(element, bindings, depth=depth + 1)
            if values is None:
                return None
            result.update(values)
            if len(result) > MAX_COMMAND_CONTROL_LITERAL_DEPENDENCIES:
                return None
        return result
    return None


def _canonical_prior_control_document(
    repo_root: Path,
    origin_revision: str,
    current_gate_id: str,
    gate_by_id: Mapping[str, Document],
    problems: Problems,
    location: str,
) -> Mapping[str, Any] | None:
    """Resolve an origin only through an earlier canonical Passed Gate."""

    try:
        current_index = CANONICAL_GATE_IDS.index(current_gate_id)
    except ValueError:
        current_index = -1
    matches: list[tuple[str, Mapping[str, Any], Mapping[str, Any]]] = []
    for gate_id in CANONICAL_GATE_IDS[: max(current_index, 0)]:
        document = gate_by_id.get(gate_id)
        gate = document.data if document is not None else None
        if not isinstance(gate, Mapping) or gate.get("status") != "Passed":
            continue
        binding = gate.get("commandControlBinding")
        if (
            isinstance(binding, Mapping)
            and binding.get("controlRevision") == origin_revision
        ):
            matches.append((gate_id, gate, binding))
    if len(matches) != 1:
        problems.add(
            "COMMAND_CONTROL_PRIOR_REGISTRY",
            location,
            "prior-control revision must resolve to exactly one earlier canonical Passed Gate",
        )
        return None
    gate_id, gate, binding = matches[0]
    expected_path = f"{COMMAND_CONTROL_ROOT}/{gate_id}.json"
    raw = _git_bytes(repo_root, ("show", f"{origin_revision}:{expected_path}"))
    tree_line = _git_stdout(
        repo_root, ("ls-tree", origin_revision, "--", expected_path)
    )
    tree_match = re.fullmatch(
        r"100644 blob ([0-9a-f]{40})\t.+", tree_line or ""
    )
    try:
        control = (
            json.loads(
                (raw or b"").decode("utf-8"),
                object_pairs_hook=_no_duplicate_object,
            )
            if raw is not None
            else None
        )
    except (UnicodeDecodeError, json.JSONDecodeError, DuplicateJsonKey):
        control = None
    candidate = _mapping(gate.get("identity")).get("productCandidate")
    history = _git_stdout(
        repo_root,
        ("log", "--all", "--format=%H", "--reverse", "--", expected_path),
    )
    parent_line = _git_stdout(
        repo_root, ("rev-list", "--parents", "-n", "1", origin_revision)
    )
    prepared = binding.get("preparedFromRevision")
    valid = (
        set(binding)
        == {
            "path",
            "sha256",
            "gitBlobSha",
            "controlRevision",
            "preparedFromRevision",
            "sourceTrust",
        }
        and binding.get("path") == expected_path
        and isinstance(raw, bytes)
        and binding.get("sha256") == hashlib.sha256(raw or b"").hexdigest()
        and tree_match is not None
        and binding.get("gitBlobSha") == tree_match.group(1)
        and _single_add_commit(repo_root, expected_path) == origin_revision
        and history == origin_revision
        and isinstance(control, Mapping)
        and raw == _json_bytes(control)
        and control.get("schemaVersion") == SCHEMA_VERSION
        and control.get("protocol") == COMMAND_CONTROL_PROTOCOL
        and control.get("goalId") == GOAL_ID
        and control.get("gateId") == gate_id
        and control.get("preparedFromRevision") == prepared
        and isinstance(prepared, str)
        and (parent_line or "").split() == [origin_revision, prepared]
        and isinstance(candidate, str)
        and _git_return_code(
            repo_root,
            ("merge-base", "--is-ancestor", origin_revision, candidate),
        )
        == 0
    )
    if not valid:
        problems.add(
            "COMMAND_CONTROL_PRIOR_REGISTRY",
            location,
            "prior-control Gate binding, canonical blob, direct parent, and candidate ancestry must all match",
        )
        return None
    return control


def _literal_path_values(
    node: ast.AST,
    relative_path: str,
    string_bindings: Mapping[str, set[str]],
    path_bindings: Mapping[str, set[str]],
    *,
    depth: int = 0,
) -> set[str] | None:
    if depth > 16:
        return None
    if isinstance(node, ast.Constant) and isinstance(node.value, str):
        normalised = _normalise_literal_repo_path(node.value)
        return {normalised} if normalised is not None else None
    if isinstance(node, ast.Name):
        if node.id == "__file__":
            return {relative_path}
        values = path_bindings.get(node.id)
        if values is not None:
            return set(values)
        strings = string_bindings.get(node.id)
        if strings is None:
            return None
        result = {
            normalised
            for value in strings
            if (normalised := _normalise_literal_repo_path(value)) is not None
        }
        return result if len(result) == len(strings) else None
    if isinstance(node, ast.Attribute) and node.attr == "parent":
        bases = _literal_path_values(
            node.value,
            relative_path,
            string_bindings,
            path_bindings,
            depth=depth + 1,
        )
        if bases is None:
            return None
        return {
            "" if PurePosixPath(value).parent == PurePosixPath(".")
            else PurePosixPath(value).parent.as_posix()
            for value in bases
        }
    if (
        isinstance(node, ast.Subscript)
        and isinstance(node.value, ast.Attribute)
        and node.value.attr == "parents"
        and isinstance(node.slice, ast.Constant)
        and _is_int(node.slice.value)
        and node.slice.value >= 0
    ):
        bases = _literal_path_values(
            node.value.value,
            relative_path,
            string_bindings,
            path_bindings,
            depth=depth + 1,
        )
        if bases is None:
            return None
        result: set[str] = set()
        for value in bases:
            parents = PurePosixPath(value).parents
            if node.slice.value >= len(parents):
                return None
            parent = parents[node.slice.value]
            result.add("" if parent == PurePosixPath(".") else parent.as_posix())
        return result
    if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Div):
        bases = _literal_path_values(
            node.left,
            relative_path,
            string_bindings,
            path_bindings,
            depth=depth + 1,
        )
        children = _literal_string_values(
            node.right, string_bindings, depth=depth + 1
        )
        if bases is None or children is None:
            return None
        result: set[str] = set()
        for base in bases:
            for child in children:
                joined = PurePosixPath(base) / child
                normalised = _normalise_literal_repo_path(joined.as_posix())
                if normalised is None:
                    return None
                result.add(normalised)
                if len(result) > MAX_COMMAND_CONTROL_LITERAL_DEPENDENCIES:
                    return None
        return result
    if isinstance(node, ast.Call):
        call_name = (
            node.func.id
            if isinstance(node.func, ast.Name)
            else node.func.attr
            if isinstance(node.func, ast.Attribute)
            else None
        )
        if call_name == "Path" and len(node.args) == 1 and not node.keywords:
            return _literal_path_values(
                node.args[0],
                relative_path,
                string_bindings,
                path_bindings,
                depth=depth + 1,
            )
        if isinstance(node.func, ast.Attribute) and call_name in {
            "resolve",
            "absolute",
        } and not node.args and not node.keywords:
            return _literal_path_values(
                node.func.value,
                relative_path,
                string_bindings,
                path_bindings,
                depth=depth + 1,
            )
        if isinstance(node.func, ast.Attribute) and call_name == "with_name":
            bases = _literal_path_values(
                node.func.value,
                relative_path,
                string_bindings,
                path_bindings,
                depth=depth + 1,
            )
            names = (
                _literal_string_values(
                    node.args[0], string_bindings, depth=depth + 1
                )
                if len(node.args) == 1 and not node.keywords
                else None
            )
            if bases is None or names is None:
                return None
            result: set[str] = set()
            for base in bases:
                for name in names:
                    try:
                        replaced = PurePosixPath(base).with_name(name)
                    except ValueError:
                        return None
                    normalised = _normalise_literal_repo_path(replaced.as_posix())
                    if normalised is None:
                        return None
                    result.add(normalised)
            return result
        if isinstance(node.func, ast.Attribute) and call_name == "joinpath":
            bases = _literal_path_values(
                node.func.value,
                relative_path,
                string_bindings,
                path_bindings,
                depth=depth + 1,
            )
            components = [
                _literal_string_values(arg, string_bindings, depth=depth + 1)
                for arg in node.args
            ]
            if bases is None or not components or any(value is None for value in components):
                return None
            result = set(bases)
            for values in components:
                assert values is not None
                next_result: set[str] = set()
                for base in result:
                    for value in values:
                        normalised = _normalise_literal_repo_path(
                            (PurePosixPath(base) / value).as_posix()
                        )
                        if normalised is None:
                            return None
                        next_result.add(normalised)
                result = next_result
                if len(result) > MAX_COMMAND_CONTROL_LITERAL_DEPENDENCIES:
                    return None
            return result
    return None


def _repo_module_source_path(repo_root: Path, module_name: str) -> str | None:
    parts = tuple(part for part in module_name.split(".") if part)
    if not parts:
        return None
    for length in range(len(parts), 0, -1):
        root = PurePosixPath(*parts[:length])
        for candidate in (root.with_suffix(".py"), root / "__init__.py"):
            value = candidate.as_posix()
            if (repo_root / value).is_file():
                return value
    return None


def _command_source_literal_dependencies(
    repo_root: Path,
    relative_path: str,
    raw: bytes,
) -> tuple[set[str], bool]:
    """Extract literal-safe repo-local dynamic loads without executing code."""

    try:
        tree = ast.parse(raw.decode("utf-8"), filename=relative_path)
    except (UnicodeDecodeError, SyntaxError):
        return set(), True

    assignments: list[tuple[str, ast.AST]] = []
    for node in ast.walk(tree):
        if isinstance(node, (ast.Assign, ast.AnnAssign)):
            value = node.value
            targets = node.targets if isinstance(node, ast.Assign) else [node.target]
            for target in targets:
                if isinstance(target, ast.Name) and value is not None:
                    assignments.append((target.id, value))
        elif isinstance(node, ast.For) and isinstance(node.target, ast.Name):
            assignments.append((node.target.id, node.iter))

    string_bindings: dict[str, set[str]] = {}
    path_bindings: dict[str, set[str]] = {}
    for _ in range(16):
        changed = False
        for name, value in assignments:
            strings = _literal_string_values(value, string_bindings)
            if strings:
                previous = string_bindings.setdefault(name, set())
                before = len(previous)
                previous.update(strings)
                if len(previous) > MAX_COMMAND_CONTROL_LITERAL_DEPENDENCIES:
                    return set(), True
                changed = changed or len(previous) != before
            paths = _literal_path_values(
                value,
                relative_path,
                string_bindings,
                path_bindings,
            )
            if paths:
                previous_paths = path_bindings.setdefault(name, set())
                before_paths = len(previous_paths)
                previous_paths.update(paths)
                if len(previous_paths) > MAX_COMMAND_CONTROL_LITERAL_DEPENDENCIES:
                    return set(), True
                changed = changed or len(previous_paths) != before_paths
        if not changed:
            break

    dynamic_aliases: dict[str, str] = {}
    for name, value in assignments:
        if isinstance(value, ast.Attribute) and value.attr in {
            "import_module",
            "run_module",
            "run_path",
            "spec_from_file_location",
            "loadTestsFromName",
            "loadTestsFromNames",
            "discover",
            "SourceFileLoader",
        }:
            dynamic_aliases[name] = value.attr

    dependencies = _command_source_local_imports(repo_root, relative_path, raw)
    unresolved = any(
        isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef))
        and node.name == "load_tests"
        for node in ast.walk(tree)
    )
    for node in ast.walk(tree):
        if not isinstance(node, ast.Call):
            continue
        call_name = (
            node.func.id
            if isinstance(node.func, ast.Name)
            else node.func.attr
            if isinstance(node.func, ast.Attribute)
            else None
        )
        call_name = dynamic_aliases.get(str(call_name), call_name)
        if call_name in {"eval", "exec", "SourceFileLoader", "discover"}:
            unresolved = True
            continue
        if call_name == "getattr":
            attribute_names = (
                _literal_string_values(node.args[1], string_bindings)
                if len(node.args) >= 2
                else None
            )
            if attribute_names is None or attribute_names & {
                "import_module",
                "run_module",
                "run_path",
                "spec_from_file_location",
                "loadTestsFromName",
                "loadTestsFromNames",
                "discover",
                "SourceFileLoader",
            }:
                unresolved = True
            continue
        if call_name in {
            "loadTestsFromName",
            "loadTestsFromNames",
            "import_module",
            "run_module",
            "__import__",
        }:
            modules = (
                _literal_string_values(node.args[0], string_bindings)
                if node.args
                else None
            )
            if modules is None:
                unresolved = True
                continue
            for module in modules:
                dependency = _repo_module_source_path(repo_root, module)
                if dependency is not None:
                    dependencies.add(dependency)
                elif module == "tools" or module.startswith("tools."):
                    unresolved = True
        elif call_name in {"spec_from_file_location", "run_path"}:
            path_index = 1 if call_name == "spec_from_file_location" else 0
            paths = (
                _literal_path_values(
                    node.args[path_index],
                    relative_path,
                    string_bindings,
                    path_bindings,
                )
                if len(node.args) > path_index
                else None
            )
            if paths is None:
                unresolved = True
                continue
            for path in paths:
                if path and path.endswith(".py") and (repo_root / path).is_file():
                    dependencies.add(path)
                else:
                    unresolved = True
        if len(dependencies) > MAX_COMMAND_CONTROL_LITERAL_DEPENDENCIES:
            return set(), True
    dependencies.discard(relative_path)
    return dependencies, unresolved


def _command_source_literal_artifact_paths(
    relative_path: str,
    raw: bytes,
) -> tuple[set[str], bool]:
    """Collect literal ``artifacts/...`` paths without executing command code.

    This deliberately complements, rather than replaces, the isolated adapter's
    guarded-open policy.  The static pass makes a newly added literal artifact
    input visible while the runtime guard remains authoritative for aliases,
    generated paths, and other dynamic access that cannot be proved here.
    """

    try:
        tree = ast.parse(raw.decode("utf-8"), filename=relative_path)
    except (UnicodeDecodeError, SyntaxError):
        return set(), True

    assignments: list[tuple[str, ast.AST]] = []
    for node in ast.walk(tree):
        if isinstance(node, (ast.Assign, ast.AnnAssign)):
            value = node.value
            targets = node.targets if isinstance(node, ast.Assign) else [node.target]
            for target in targets:
                if isinstance(target, ast.Name) and value is not None:
                    assignments.append((target.id, value))
        elif isinstance(node, ast.For) and isinstance(node.target, ast.Name):
            assignments.append((node.target.id, node.iter))

    string_bindings: dict[str, set[str]] = {}
    path_bindings: dict[str, set[str]] = {}
    for _ in range(16):
        changed = False
        for name, value in assignments:
            strings = _literal_string_values(value, string_bindings)
            if strings:
                prior = string_bindings.setdefault(name, set())
                size = len(prior)
                prior.update(strings)
                if len(prior) > MAX_COMMAND_CONTROL_LITERAL_DEPENDENCIES:
                    return set(), True
                changed = changed or len(prior) != size
            paths = _literal_path_values(
                value,
                relative_path,
                string_bindings,
                path_bindings,
            )
            if paths:
                prior_paths = path_bindings.setdefault(name, set())
                size = len(prior_paths)
                prior_paths.update(paths)
                if len(prior_paths) > MAX_COMMAND_CONTROL_LITERAL_DEPENDENCIES:
                    return set(), True
                changed = changed or len(prior_paths) != size
        if not changed:
            break

    discovered: set[str] = set()
    for values in (*string_bindings.values(), *path_bindings.values()):
        for value in values:
            normalised = _normalise_literal_repo_path(value)
            if normalised is not None and normalised.startswith("artifacts/"):
                discovered.add(normalised)
                if len(discovered) > MAX_COMMAND_CONTROL_LITERAL_DEPENDENCIES:
                    return set(), True
    return discovered, False


def _validate_command_source_dependency_closure(
    repo_root: Path,
    gate_id: str,
    command_id: str,
    source_items: Sequence[Mapping[str, Any]],
    raw_by_path: Mapping[str, bytes],
    problems: Problems,
    location: str,
) -> None:
    command_sources = [
        item for item in source_items if item.get("commandId") == command_id
    ]
    declared_paths = {str(item.get("path")) for item in command_sources}
    if len(command_sources) > MAX_COMMAND_CONTROL_SOURCES_PER_COMMAND:
        problems.add(
            "COMMAND_CONTROL_DEPENDENCY_LIMIT",
            location,
            "command source inventory exceeds the frozen per-command limit",
        )
        return
    pending = sorted(
        str(item.get("path"))
        for item in command_sources
        if item.get("role") in {"adapter", "oracle", "test"}
    )
    visited: set[str] = set()

    if gate_id == "W84-G0":
        adapters = [
            item for item in command_sources if item.get("role") == "adapter"
        ]
        expected_runtime_paths = {
            path
            for path, _kind in W84_G0_RUNTIME_INPUT_LAYOUT.get(command_id, ())
        }
        adapter_path = (
            str(adapters[0].get("path")) if len(adapters) == 1 else ""
        )
        adapter_raw = raw_by_path.get(adapter_path)
        literal_runtime_paths, runtime_unresolved = (
            _command_source_literal_artifact_paths(adapter_path, adapter_raw)
            if adapter_raw is not None
            else (set(), True)
        )
        # goal-contract-unittest delegates to five sealed test modules; its four
        # repository artifact inputs are therefore fixed by the frozen layout
        # and enforced by the isolated runtime open guard.  The other four W84
        # adapters declare their artifact inputs directly and must be exact.
        runtime_paths_valid = (
            literal_runtime_paths <= expected_runtime_paths
            if command_id == "goal-contract-unittest"
            else literal_runtime_paths == expected_runtime_paths
        )
        if runtime_unresolved or not runtime_paths_valid:
            problems.add(
                "COMMAND_CONTROL_RUNTIME_INPUT_DISCOVERY",
                location,
                "W84 adapter literal artifact inputs must match the frozen runtime-input layout",
            )

    while pending:
        source_path = pending.pop(0)
        if source_path in visited:
            continue
        visited.add(source_path)
        if len(visited) > MAX_COMMAND_CONTROL_LITERAL_DEPENDENCIES:
            problems.add(
                "COMMAND_CONTROL_DEPENDENCY_LIMIT",
                location,
                "recursive command dependency closure exceeds its frozen limit",
            )
            return
        source_raw = raw_by_path.get(source_path)
        if source_raw is None or not source_path.endswith(".py"):
            continue
        dependencies, unresolved = _command_source_literal_dependencies(
            repo_root,
            source_path,
            source_raw,
        )
        if unresolved:
            problems.add(
                "COMMAND_CONTROL_UNRESOLVED_DEPENDENCY",
                location,
                "repo-local dynamic source loading must resolve from literal-safe syntax",
            )
        undeclared = sorted(dependencies - declared_paths)
        if undeclared:
            problems.add(
                "COMMAND_CONTROL_UNDECLARED_DEPENDENCY",
                location,
                "recursive repo-local source dependency is absent from this command's sealed inventory",
            )
        for dependency in sorted(dependencies & declared_paths):
            if dependency not in visited:
                pending.append(dependency)


def _validate_command_control_source(
    repo_root: Path,
    source: Mapping[str, Any],
    control_revision: str,
    prepared_from_revision: str,
    product_candidate: str,
    current_gate_id: str,
    gate_by_id: Mapping[str, Document],
    problems: Problems,
    location: str,
) -> bytes | None:
    relative_path = source.get("path")
    if not isinstance(relative_path, str):
        return None
    path = _safe_relative_evidence_path(
        repo_root,
        relative_path,
        problems,
        f"{location}#/path",
    )
    try:
        raw = path.read_bytes() if path is not None else None
    except OSError:
        raw = None
    tree_line = _git_stdout(
        repo_root,
        ("ls-tree", control_revision, "--", relative_path),
    )
    tree_match = re.fullmatch(
        r"100644 blob ([0-9a-f]{40})\t.+", tree_line or ""
    )
    blob_sha = tree_match.group(1) if tree_match is not None else None
    control_raw = _git_bytes(
        repo_root, ("show", f"{control_revision}:{relative_path}")
    )
    candidate_raw = _git_bytes(
        repo_root, ("show", f"{product_candidate}:{relative_path}")
    )
    head_raw = _git_bytes(repo_root, ("show", f"HEAD:{relative_path}"))
    index_raw = _git_bytes(repo_root, ("show", f":{relative_path}"))
    dirty = _git_stdout(
        repo_root,
        (
            "status",
            "--porcelain=v1",
            "--untracked-files=all",
            "--",
            relative_path,
        ),
    )
    history_after_control = _git_stdout(
        repo_root,
        ("log", "--format=%H", f"{control_revision}..HEAD", "--", relative_path),
    )
    valid = (
        raw is not None
        and path is not None
        and _is_exact_safe_source_path(relative_path)
        and not _path_has_reparse_component(repo_root, relative_path)
        and tree_match is not None
        and source.get("sha256") == hashlib.sha256(raw or b"").hexdigest()
        and source.get("gitBlobSha") == blob_sha
        and control_raw == raw
        and candidate_raw == raw
        and head_raw == raw
        and index_raw == raw
        and dirty == ""
        and history_after_control == ""
        and _git_mode(
            repo_root, ("ls-files", "--stage", "--", relative_path)
        )
        == "100644"
    )
    origin = source.get("origin")
    origin_revision = source.get("originControlRevision")
    if origin == "prior-control":
        origin_raw = (
            _git_bytes(repo_root, ("show", f"{origin_revision}:{relative_path}"))
            if isinstance(origin_revision, str)
            else None
        )
        origin_control = (
            _canonical_prior_control_document(
                repo_root,
                origin_revision,
                current_gate_id,
                gate_by_id,
                problems,
                location,
            )
            if isinstance(origin_revision, str)
            else None
        )
        origin_matches = [
            item
            for item in _list(
                origin_control.get("sources")
                if isinstance(origin_control, Mapping)
                else None
            )
            if isinstance(item, Mapping)
            and all(
                item.get(key) == source.get(key)
                for key in {
                    "commandId",
                    "role",
                    "path",
                    "sha256",
                    "gitBlobSha",
                    "verification",
                }
            )
        ]
        valid = (
            valid
            and isinstance(origin_revision, str)
            and re.fullmatch(r"[0-9a-f]{40}", origin_revision) is not None
            and _git_return_code(
                repo_root,
                (
                    "merge-base",
                    "--is-ancestor",
                    origin_revision,
                    prepared_from_revision,
                ),
            )
            == 0
            and origin_raw == raw
            and _git_stdout(
                repo_root,
                (
                    "log",
                    "--format=%H",
                    f"{origin_revision}..{control_revision}",
                    "--",
                    relative_path,
                ),
            )
            == ""
            and len(origin_matches) == 1
        )
    elif origin in {"prepared", "bootstrap"}:
        predecessor_line = _git_stdout(
            repo_root,
            ("ls-tree", prepared_from_revision, "--", relative_path),
        )
        predecessor_match = re.fullmatch(
            r"100644 blob ([0-9a-f]{40})\t.+",
            predecessor_line or "",
        )
        raw_diff = _git_stdout(
            repo_root,
            (
                "diff-tree",
                "-r",
                "--raw",
                "--no-abbrev",
                "--no-renames",
                prepared_from_revision,
                control_revision,
                "--",
                relative_path,
            ),
        )
        add_match = re.fullmatch(
            r":000000 100644 0{40} ([0-9a-f]{40}) A\t.+",
            raw_diff or "",
        )
        update_match = re.fullmatch(
            r":100644 100644 ([0-9a-f]{40}) ([0-9a-f]{40}) M\t.+",
            raw_diff or "",
        )
        prepared_add = (
            predecessor_match is None
            and add_match is not None
            and add_match.group(1) == blob_sha
            and _single_add_commit(repo_root, relative_path) == control_revision
        )
        prepared_update = (
            origin == "prepared"
            and source.get("role") == "fixture"
            and predecessor_match is not None
            and update_match is not None
            and update_match.group(1) == predecessor_match.group(1)
            and update_match.group(2) == blob_sha
            and update_match.group(1) != update_match.group(2)
        )
        # Bootstrap remains add-only.  Normal control revisions may update an
        # existing non-authority fixture, with P fixing the old blob and the
        # unique raw M diff fixing the reviewed replacement.  Adapters,
        # parsers, and verifier-authority sources cannot use this exception.
        valid = (
            valid
            and origin_revision is None
            and (prepared_add or prepared_update)
        )
    else:
        valid = False
    if not _git_attributes_are_raw_safe(
        repo_root,
        relative_path,
        problems,
        "COMMAND_CONTROL_SOURCE",
    ):
        valid = False
    if not valid:
        problems.add(
            "COMMAND_CONTROL_SOURCE_IDENTITY",
            location,
            "declared source must keep its exact 100644 raw blob from control revision through candidate, index, and HEAD",
        )
        return None
    return raw


def _validate_command_control_runtime_driver(
    repo_root: Path,
    driver: Mapping[str, Any],
    control_revision: str,
    prepared_from_revision: str,
    product_candidate: str,
    problems: Problems,
    location: str,
) -> bool:
    phase = driver.get("phase")
    expected_path = PROVIDER_DRIVER_PATHS.get(str(phase))
    relative_path = driver.get("path")
    origin_revision = driver.get("originControlRevision")
    if (
        expected_path is None
        or relative_path != expected_path
        or not isinstance(origin_revision, str)
        or re.fullmatch(r"[0-9a-f]{40}", origin_revision) is None
    ):
        problems.add(
            "COMMAND_CONTROL_RUNTIME_DRIVER_SHAPE",
            location,
            "runtime driver must bind its exact frozen provider phase and origin revision",
        )
        return False

    path = _safe_relative_evidence_path(
        repo_root,
        relative_path,
        problems,
        f"{location}#/path",
    )
    try:
        raw = path.read_bytes() if path is not None else None
    except OSError:
        raw = None
    tree_line = _git_stdout(
        repo_root,
        ("ls-tree", control_revision, "--", relative_path),
    )
    tree_match = re.fullmatch(
        r"100644 blob ([0-9a-f]{40})\t.+", tree_line or ""
    )
    control_raw = _git_bytes(
        repo_root, ("show", f"{control_revision}:{relative_path}")
    )
    candidate_raw = _git_bytes(
        repo_root, ("show", f"{product_candidate}:{relative_path}")
    )
    head_raw = _git_bytes(repo_root, ("show", f"HEAD:{relative_path}"))
    index_raw = _git_bytes(repo_root, ("show", f":{relative_path}"))
    dirty = _git_stdout(
        repo_root,
        (
            "status",
            "--porcelain=v1",
            "--untracked-files=all",
            "--",
            relative_path,
        ),
    )
    origin_is_prior = (
        _git_return_code(
            repo_root,
            (
                "merge-base",
                "--is-ancestor",
                origin_revision,
                prepared_from_revision,
            ),
        )
        == 0
    )
    valid = (
        raw is not None
        and path is not None
        and _is_exact_safe_source_path(relative_path)
        and not _path_has_reparse_component(repo_root, relative_path)
        and tree_match is not None
        and driver.get("sha256") == hashlib.sha256(raw or b"").hexdigest()
        and driver.get("gitBlobSha")
        == (tree_match.group(1) if tree_match is not None else None)
        and control_raw == raw
        and candidate_raw == raw
        and head_raw == raw
        and index_raw == raw
        and dirty == ""
        and _single_add_commit(repo_root, relative_path) == origin_revision
        and origin_revision != control_revision
        and origin_is_prior
        and _git_stdout(
            repo_root,
            (
                "log",
                "--format=%H",
                f"{origin_revision}..{product_candidate}",
                "--",
                relative_path,
            ),
        )
        == ""
        and _git_mode(
            repo_root, ("ls-files", "--stage", "--", relative_path)
        )
        == "100644"
    )
    origin_changed_paths = _git_commit_changed_paths(repo_root, origin_revision)
    origin_control_paths = sorted(
        path_value
        for path_value in (origin_changed_paths or set())
        if path_value.startswith(f"{COMMAND_CONTROL_ROOT}/")
        and path_value.endswith(".json")
    )
    origin_control_raw = (
        _git_bytes(
            repo_root,
            ("show", f"{origin_revision}:{origin_control_paths[0]}"),
        )
        if len(origin_control_paths) == 1
        else None
    )
    try:
        origin_control = (
            json.loads(
                (origin_control_raw or b"").decode("utf-8"),
                object_pairs_hook=_no_duplicate_object,
            )
            if origin_control_raw is not None
            else None
        )
    except (UnicodeDecodeError, json.JSONDecodeError, DuplicateJsonKey):
        origin_control = None
    origin_sources = (
        origin_control.get("sources")
        if isinstance(origin_control, Mapping)
        else None
    )
    origin_binding_matches = [
        source
        for source in _list(origin_sources)
        if isinstance(source, Mapping)
        and source.get("path") == relative_path
        and source.get("role") in {"fixture", "parser"}
        and source.get("sha256") == driver.get("sha256")
        and source.get("gitBlobSha") == driver.get("gitBlobSha")
    ]
    valid = (
        valid
        and isinstance(origin_control, Mapping)
        and origin_control.get("protocol") == COMMAND_CONTROL_PROTOCOL
        and len(origin_binding_matches) == 1
    )
    if not _git_attributes_are_raw_safe(
        repo_root,
        relative_path,
        problems,
        "COMMAND_CONTROL_RUNTIME_DRIVER",
    ):
        valid = False
    if not valid:
        problems.add(
            "COMMAND_CONTROL_RUNTIME_DRIVER_IDENTITY",
            location,
            "runtime driver must be fixture/parser sealed by one strict-prior command control and remain its unique raw-safe first-add blob through candidate, index, and HEAD",
        )
    return valid


def _w84_g0_expected_source_projection() -> tuple[tuple[str, str, str], ...]:
    projected: list[tuple[str, str, str]] = []
    role_order = {
        role: index
        for index, role in enumerate(
            ("adapter", "oracle", "test", "fixture", "parser")
        )
    }
    for command_id, adapter_path in W84_G0_ADAPTER_PATH_BY_COMMAND.items():
        for path in W84_G0_SOURCE_UNIVERSE:
            if path == adapter_path:
                role = "adapter"
            elif path == W84_G0_VERIFIER_PATH:
                role = "test"
            elif path in W84_G0_PARSER_PATHS:
                role = "parser"
            else:
                role = "fixture"
            projected.append((command_id, role, path))
    return tuple(
        sorted(
            projected,
            key=lambda value: (value[0], role_order[value[1]], value[2]),
        )
    )


def _validate_command_control_document(
    repo_root: Path,
    document: Document,
    gate: Mapping[str, Any],
    requirement: Mapping[str, Any],
    contract: Contract,
    gate_by_id: Mapping[str, Document],
    handoff_by_group: Mapping[tuple[int, str], Document],
    semantic_command_id: Any,
    problems: Problems,
) -> None:
    gate_id = str(gate.get("gateId"))
    location = document.relative_path
    policy = _mapping(requirement.get("commandControl"))
    binding = gate.get("commandControlBinding")
    expected_path = f"{COMMAND_CONTROL_ROOT}/{gate_id}.json"
    if not isinstance(binding, Mapping) or set(binding) != {
        "path",
        "sha256",
        "gitBlobSha",
        "controlRevision",
        "preparedFromRevision",
        "sourceTrust",
    }:
        problems.add(
            "COMMAND_CONTROL_BINDING",
            location,
            "Passed Gate requires its exact command-control binding",
        )
        return
    control_revision = binding.get("controlRevision")
    prepared_from_revision = binding.get("preparedFromRevision")
    product_candidate = _mapping(gate.get("identity")).get("productCandidate")
    if (
        binding.get("path") != expected_path
        or binding.get("path") != policy.get("path")
        or binding.get("sourceTrust") != policy.get("sourceTrust")
        or not isinstance(control_revision, str)
        or re.fullmatch(r"[0-9a-f]{40}", control_revision) is None
        or not isinstance(prepared_from_revision, str)
        or re.fullmatch(r"[0-9a-f]{40}", prepared_from_revision) is None
        or not isinstance(product_candidate, str)
        or re.fullmatch(r"[0-9a-f]{40}", product_candidate) is None
    ):
        problems.add(
            "COMMAND_CONTROL_BINDING",
            location,
            "command-control binding does not match Gate policy and candidate identities",
        )
        return
    control_path = repo_root / expected_path
    control_document = read_document(repo_root, control_path, problems)
    control = (
        _mapping(control_document.data) if control_document is not None else {}
    )
    try:
        control_raw = control_path.read_bytes()
    except OSError:
        control_raw = None
    first_add = _single_add_commit(repo_root, expected_path)
    tree_line = _git_stdout(
        repo_root, ("ls-tree", control_revision, "--", expected_path)
    )
    tree_match = re.fullmatch(
        r"100644 blob ([0-9a-f]{40})\t.+", tree_line or ""
    )
    if (
        control_document is None
        or control_raw is None
        or control_raw != _json_bytes(control)
        or binding.get("sha256") != hashlib.sha256(control_raw or b"").hexdigest()
        or binding.get("gitBlobSha")
        != (tree_match.group(1) if tree_match is not None else None)
        or first_add != control_revision
        or not _validate_single_add_immutable_blob(
            repo_root,
            expected_path,
            problems,
            "COMMAND_CONTROL_DOCUMENT",
        )
    ):
        problems.add(
            "COMMAND_CONTROL_DOCUMENT_IDENTITY",
            location,
            "control document must be canonical and equal its unique immutable first-add blob",
        )

    sources = control.get("sources")
    source_trees = control.get("sourceTrees")
    runtime_drivers = control.get("runtimeDrivers")
    production_projection = control.get("productionProjection")
    expected_control_keys = {
        "schemaVersion",
        "protocol",
        "goalId",
        "gateId",
        "sourceTrust",
        "preparedFromRevision",
        "sources",
        "sourceTrees",
        "runtimeDrivers",
        "productionProjection",
    }
    if (
        set(control) != expected_control_keys
        or control.get("schemaVersion") != SCHEMA_VERSION
        or control.get("protocol") != COMMAND_CONTROL_PROTOCOL
        or control.get("goalId") != GOAL_ID
        or control.get("gateId") != gate_id
        or control.get("sourceTrust") != binding.get("sourceTrust")
        or control.get("preparedFromRevision") != prepared_from_revision
        or not isinstance(sources, list)
        or not sources
        or not isinstance(source_trees, list)
        or not isinstance(runtime_drivers, list)
    ):
        problems.add(
            "COMMAND_CONTROL_DOCUMENT_SHAPE",
            location,
            "control document must exactly bind Gate, trust, predecessor, and ordered sources",
        )
        return

    mode = str(policy.get("predecessorMode"))
    expected_predecessor = _expected_command_predecessor(
        gate_id,
        mode,
        contract,
        gate_by_id,
        handoff_by_group,
        problems,
        location,
    )
    if mode == "w90-integration-entry-base":
        expected_parents = (
            expected_predecessor.split("|")
            if isinstance(expected_predecessor, str)
            else []
        )
        predecessor_line = _git_stdout(
            repo_root,
            ("rev-list", "--parents", "-n", "1", prepared_from_revision),
        )
        predecessor_words = (predecessor_line or "").split()
        if (
            len(predecessor_words) != 3
            or predecessor_words[0] != prepared_from_revision
            or predecessor_words[1:] != expected_parents
        ):
            problems.add(
                "COMMAND_CONTROL_INTEGRATION_BASE",
                location,
                "W90 prepared revision must be the exact two-parent W89 lane integration base",
            )
    elif prepared_from_revision != expected_predecessor:
        problems.add(
            "COMMAND_CONTROL_PREDECESSOR",
            location,
            "preparedFromRevision does not equal the frozen direct predecessor",
        )

    parent_line = _git_stdout(
        repo_root,
        ("rev-list", "--parents", "-n", "1", control_revision),
    )
    if (parent_line or "").split() != [
        control_revision,
        prepared_from_revision,
    ]:
        problems.add(
            "COMMAND_CONTROL_DIRECT_PARENT",
            location,
            "control revision must be a non-merge direct child of preparedFromRevision",
        )

    source_keys = {
        "commandId",
        "role",
        "path",
        "sha256",
        "gitBlobSha",
        "origin",
        "originControlRevision",
        "verification",
    }
    source_items = [item for item in sources if isinstance(item, Mapping)]
    source_tree_items = [
        item for item in _list(source_trees) if isinstance(item, Mapping)
    ]
    product_commands = sorted(
        command_id
        for command_id in _list(requirement.get("requiredCommandIds"))
        if isinstance(command_id, str) and command_id != semantic_command_id
    )
    order = {role: index for index, role in enumerate(
        ("adapter", "oracle", "test", "fixture", "parser")
    )}
    projected_order = [
        (
            str(item.get("commandId")),
            order.get(str(item.get("role")), 99),
            str(item.get("path")),
        )
        for item in source_items
    ]
    if (
        len(source_items) != len(sources)
        or any(
            set(item) != source_keys
            or item.get("commandId") not in product_commands
            or item.get("role") not in COMMAND_CONTROL_ROLES
            or item.get("origin") not in COMMAND_CONTROL_ORIGINS
            or not isinstance(item.get("path"), str)
            or not _command_control_source_path_allowed(gate_id, item)
            or not isinstance(item.get("sha256"), str)
            or HEX_SHA256.fullmatch(str(item.get("sha256"))) is None
            or not isinstance(item.get("gitBlobSha"), str)
            or re.fullmatch(r"[0-9a-f]{40}", str(item.get("gitBlobSha"))) is None
            for item in source_items
        )
        or projected_order != sorted(projected_order)
        or len(set(projected_order)) != len(projected_order)
    ):
        problems.add(
            "COMMAND_CONTROL_SOURCE_SHAPE",
            location,
            "sources must be exact, ordered, unique command/role/path bindings under tools or the exact W84 bootstrap dependency allowlist",
        )

    for index, item in enumerate(source_items):
        role = item.get("role")
        verification = item.get("verification")
        verification_location = f"{location}#/sources/{index}/verification"
        if role in {"adapter", "fixture", "parser"}:
            if verification is not None:
                problems.add(
                    "COMMAND_CONTROL_VERIFICATION_SHAPE",
                    verification_location,
                    "adapter, fixture, and parser sources must not claim verifier authority",
                )
            continue
        arguments = (
            verification.get("arguments")
            if isinstance(verification, Mapping)
            else None
        )
        verifies = (
            verification.get("verifiesCommandIds")
            if isinstance(verification, Mapping)
            else None
        )
        source_module = str(item.get("path", ""))
        if source_module.endswith(".py"):
            source_module = source_module[:-3].replace("/", ".")
        if (
            not isinstance(verification, Mapping)
            or set(verification) != {"arguments", "verifiesCommandIds"}
            or not isinstance(arguments, list)
            or not arguments
            or any(
                not isinstance(argument, str)
                or UNITTEST_EXACT_NAME.fullmatch(argument) is None
                or not argument.startswith(f"{source_module}.")
                for argument in arguments
            )
            or len(set(arguments)) != len(arguments)
            or not isinstance(verifies, list)
            or not verifies
            or any(command_id not in product_commands for command_id in verifies)
            or len(set(verifies)) != len(verifies)
        ):
            problems.add(
                "COMMAND_CONTROL_VERIFICATION_SHAPE",
                verification_location,
                "oracle/test verification must contain unique exact unittest names from its sealed source and exact product command IDs",
            )

    if gate_id == "W84-G0":
        expected_projection = list(_w84_g0_expected_source_projection())
        expected_test_verification = {
            command_id: {
                "arguments": [argument],
                "verifiesCommandIds": [command_id],
            }
            for command_id, argument
            in W84_G0_VERIFICATION_ARGUMENT_BY_COMMAND.items()
        }
        if (
            product_commands != sorted(W84_G0_ADAPTER_PATH_BY_COMMAND)
            or projected_order != expected_projection
            or source_trees != []
            or any(
                item.get("origin") != "bootstrap"
                or item.get("originControlRevision") is not None
                or (
                    item.get("verification")
                    != expected_test_verification.get(str(item.get("commandId")))
                    if item.get("role") == "test"
                    else item.get("verification") is not None
                )
                for item in source_items
            )
        ):
            problems.add(
                "COMMAND_CONTROL_W84_SOURCE_CLOSURE",
                location,
                "W84-G0 must bind the exact 43-path bootstrap universe for every product command with its frozen adapter, verifier, fixture, and parser roles",
            )

    source_tree_projection = [
        (
            str(item.get("commandId")),
            str(item.get("role")),
            str(item.get("rootPath")),
            str(item.get("includePolicy")),
        )
        for item in source_tree_items
    ]
    if (
        len(source_tree_items) != len(_list(source_trees))
        or source_tree_projection != sorted(source_tree_projection)
        or len(set(source_tree_projection)) != len(source_tree_projection)
    ):
        problems.add(
            "COMMAND_CONTROL_SOURCE_TREE_ORDER",
            location,
            "sourceTrees must be ordered unique command/role/root/policy bindings",
        )

    runtime_driver_source_items = [
        item
        for item in source_items
        if item.get("path") in set(PROVIDER_DRIVER_PATHS.values())
    ]
    if gate_id == "W84-G5":
        if (
            {item.get("path") for item in runtime_driver_source_items}
            != set(PROVIDER_DRIVER_PATHS.values())
            or len(runtime_driver_source_items) != len(PROVIDER_DRIVER_PATHS)
            or any(
                item.get("role") not in {"fixture", "parser"}
                or item.get("origin") != "prepared"
                or item.get("originControlRevision") is not None
                or item.get("verification") is not None
                for item in runtime_driver_source_items
            )
        ):
            problems.add(
                "COMMAND_CONTROL_RUNTIME_DRIVER_PRESEAL",
                location,
                "W84-G5 must first-add all four runtime drivers solely as prepared fixture/parser sources",
            )
    elif runtime_driver_source_items:
        problems.add(
            "COMMAND_CONTROL_RUNTIME_DRIVER_PRESEAL",
            location,
            "runtime drivers belong only to the W84-G5 source preseal and later provider runtimeDrivers bindings",
        )

    for command_id in product_commands:
        command_sources = [
            item for item in source_items if item.get("commandId") == command_id
        ]
        adapters = [
            item for item in command_sources if item.get("role") == "adapter"
        ]
        oracle_or_test = [
            item
            for item in command_sources
            if item.get("role") in {"oracle", "test"}
            and command_id
            in _list(_mapping(item.get("verification")).get("verifiesCommandIds"))
        ]
        expected_adapter_path = (
            f"{TRUSTED_PRODUCT_SCRIPT_ROOT}/{command_id}.py"
        )
        if (
            len(adapters) != 1
            or adapters[0].get("path") != expected_adapter_path
            or not oracle_or_test
        ):
            problems.add(
                "COMMAND_CONTROL_ROLE_POLICY",
                location,
                "every product command requires one derived adapter and at least one independent oracle or test",
            )

    driver_items = [
        item for item in runtime_drivers if isinstance(item, Mapping)
    ]
    expected_driver_phases = [
        phase
        for phase in PROVIDER_DRIVER_PATHS
        if phase in {value[0] for value in PROVIDER_LAUNCH_LAYOUT.get(gate_id, ())}
    ]
    driver_keys = {
        "phase",
        "path",
        "sha256",
        "gitBlobSha",
        "originControlRevision",
    }
    if (
        len(driver_items) != len(runtime_drivers)
        or [item.get("phase") for item in driver_items] != expected_driver_phases
        or any(set(item) != driver_keys for item in driver_items)
    ):
        problems.add(
            "COMMAND_CONTROL_RUNTIME_DRIVER_SHAPE",
            location,
            "runtimeDrivers must be empty for non-provider Gates and exact phase-ordered bindings for provider Gates",
        )
    valid_driver_paths: set[str] = set()
    for index, driver in enumerate(driver_items):
        if _validate_command_control_runtime_driver(
            repo_root,
            driver,
            control_revision,
            prepared_from_revision,
            product_candidate,
            problems,
            f"{location}#/runtimeDrivers/{index}",
        ):
            driver_path = str(driver.get("path"))
            valid_driver_paths.add(driver_path)

    path_identities: dict[str, tuple[Any, ...]] = {}
    raw_by_path: dict[str, bytes] = {}
    for index, item in enumerate(source_items):
        relative_path = str(item.get("path"))
        identity = (
            item.get("sha256"),
            item.get("gitBlobSha"),
            item.get("origin"),
            item.get("originControlRevision"),
        )
        if relative_path in path_identities and path_identities[relative_path] != identity:
            problems.add(
                "COMMAND_CONTROL_SHARED_SOURCE",
                location,
                "reused source paths must repeat one identical blob/origin binding",
            )
            continue
        path_identities[relative_path] = identity
        if relative_path not in raw_by_path:
            raw = _validate_command_control_source(
                repo_root,
                item,
                control_revision,
                prepared_from_revision,
                product_candidate,
                gate_id,
                gate_by_id,
                problems,
                f"{location}#/sources/{index}",
            )
            if raw is not None:
                raw_by_path[relative_path] = raw

    valid_source_tree_paths: set[str] = set()
    prepared_source_tree_paths: set[str] = set()
    for index, tree_item in enumerate(source_tree_items):
        selected_paths = _validate_command_control_source_tree(
            repo_root,
            tree_item,
            control_revision,
            prepared_from_revision,
            product_candidate,
            product_commands,
            gate_id,
            gate_by_id,
            problems,
            f"{location}#/sourceTrees/{index}",
        )
        valid_source_tree_paths.update(selected_paths)
        if tree_item.get("origin") == "prepared":
            prepared_source_tree_paths.update(
                path
                for path in _list(tree_item.get("preparedPaths"))
                if isinstance(path, str)
            )

    for source in source_items:
        source_path = str(source.get("path"))
        source_raw = raw_by_path.get(source_path)
        if (
            source.get("role") == "adapter"
            and source_raw is not None
            and b"CAICLI_PRODUCT_COMMAND_RESULT=" in source_raw
        ):
            problems.add(
                "COMMAND_CONTROL_DIRECT_RESULT",
                location,
                "adapter may not manufacture the trusted product result marker",
            )
    for command_id in product_commands:
        _validate_command_source_dependency_closure(
            repo_root,
            gate_id,
            command_id,
            source_items,
            raw_by_path,
            problems,
            location,
        )

    changed_paths = _git_commit_changed_paths(repo_root, control_revision)
    prepared_paths = {
        str(item.get("path"))
        for item in source_items
        if item.get("origin") == "prepared"
    }
    bootstrap_paths = {
        str(item.get("path"))
        for item in source_items
        if item.get("origin") == "bootstrap"
    }
    if mode == "w84-bootstrap-exception":
        expected_changed_paths = set(BOOTSTRAP_CONTROL_PATHS)
        if (
            prepared_paths
            or any(item.get("origin") != "bootstrap" for item in source_items)
            or control_revision != product_candidate
            or prepared_from_revision != W84_BOOTSTRAP_PARENT
            or production_projection is not None
        ):
            problems.add(
                "COMMAND_CONTROL_BOOTSTRAP_EXCEPTION",
                location,
                "W84-G0 alone must use bootstrap sources at candidate B with its frozen parent",
            )
    else:
        expected_changed_paths = {
            expected_path,
            *prepared_paths,
            *prepared_source_tree_paths,
        }
        if bootstrap_paths:
            problems.add(
                "COMMAND_CONTROL_BOOTSTRAP_SCOPE",
                location,
                "bootstrap source origin is allowed only for W84-G0",
            )
        candidate_equals_control = control_revision == product_candidate
        equal_candidate_allowed = (
            COMMAND_CONTROL_EQUAL_CANDIDATE_GATE_KINDS.get(gate_id)
            == requirement.get("gateKind")
        )
        if candidate_equals_control and equal_candidate_allowed:
            excluded_paths = sorted(expected_changed_paths)
            baseline_projection = _command_control_production_projection_inventory(
                repo_root, prepared_from_revision, set(excluded_paths)
            )
            control_projection = _command_control_production_projection_inventory(
                repo_root, control_revision, set(excluded_paths)
            )
            projection_root = (
                _command_control_production_projection_root(control_projection)
                if control_projection is not None
                else None
            )
            if (
                not isinstance(production_projection, Mapping)
                or set(production_projection)
                != {
                    "protocol",
                    "baselineRevision",
                    "excludedPaths",
                    "entryCount",
                    "productionProjectionRoot",
                }
                or production_projection.get("protocol")
                != COMMAND_CONTROL_PRODUCTION_PROJECTION_PROTOCOL
                or production_projection.get("baselineRevision")
                != prepared_from_revision
                or production_projection.get("excludedPaths") != excluded_paths
                or baseline_projection is None
                or control_projection != baseline_projection
                or production_projection.get("entryCount")
                != len(control_projection or [])
                or production_projection.get("productionProjectionRoot")
                != projection_root
            ):
                problems.add(
                    "COMMAND_CONTROL_PRODUCTION_PROJECTION",
                    location,
                    "W84 diagnostic C==Q requires one exact production projection equal to its prior candidate after only approved control/test/harness exclusions",
                )
        else:
            if production_projection is not None:
                problems.add(
                    "COMMAND_CONTROL_PRODUCTION_PROJECTION",
                    location,
                    "productionProjection is allowed only for the exact W84-G1/G2 diagnostic C==Q exception",
                )
            if (
                candidate_equals_control
                or _git_return_code(
                    repo_root,
                    (
                        "merge-base",
                        "--is-ancestor",
                        control_revision,
                        product_candidate,
                    ),
                )
                != 0
            ):
                problems.add(
                    "COMMAND_CONTROL_CANDIDATE_ANCESTRY",
                    location,
                    "Gate product candidate must strictly descend from control except exact projected W84-G1/G2 diagnostics",
                )
    if changed_paths != expected_changed_paths:
        problems.add(
            "COMMAND_CONTROL_CONTROL_DIFF",
            location,
            "control revision diff must contain exactly its control document and newly prepared sources",
        )

    touched_after_control = _git_touched_paths_between(
        repo_root, control_revision, product_candidate
    )
    protected_paths = {
        expected_path,
        *path_identities,
        *valid_source_tree_paths,
        *valid_driver_paths,
        *BOOTSTRAP_CONTROL_PATHS,
    }
    forbidden_after = set()
    if touched_after_control is None:
        forbidden_after.add("<git-unavailable>")
    else:
        forbidden_after.update(touched_after_control & protected_paths)
        forbidden_after.update(
            path
            for path in touched_after_control
            if PurePosixPath(path).name == ".gitattributes"
            or PurePosixPath(path).name
            in {
                "package-lock.json",
                "packages.lock.json",
                "pnpm-lock.yaml",
                "yarn.lock",
                "global.json",
                "Directory.Build.props",
            }
        )
    if forbidden_after:
        problems.add(
            "COMMAND_CONTROL_POST_CONTROL_MUTATION",
            location,
            "candidate history changed a sealed source, control, attributes, lock, or bootstrap harness path",
        )


def validate_command_controls(
    manifest: Mapping[str, Any],
    gate_documents: Sequence[Document],
    handoff_documents: Sequence[Document],
    contract: Contract,
    repo_root: Path,
    problems: Problems,
) -> None:
    requirements = {
        item.get("gateId"): item
        for item in _list(manifest.get("gates"))
        if isinstance(item, Mapping) and isinstance(item.get("gateId"), str)
    }
    gate_by_id = {
        str(document.data.get("gateId")): document
        for document in gate_documents
        if isinstance(document.data, Mapping)
    }
    handoff_by_group = {
        (int(document.data.get("week")), str(document.data.get("lane"))): document
        for document in handoff_documents
        if isinstance(document.data, Mapping)
        and isinstance(document.data.get("week"), int)
        and isinstance(document.data.get("lane"), str)
        and document.relative_path
        == CANONICAL_HANDOFF_BY_GROUP.get(
            (int(document.data.get("week")), str(document.data.get("lane")))
        )
    }
    semantic_command_id = _mapping(
        _mapping(manifest.get("frozenPolicies")).get("trusted-test-command")
    ).get("commandId")
    for gate_id, document in gate_by_id.items():
        gate = _mapping(document.data)
        if gate.get("status") != "Passed":
            if gate.get("commandControlBinding") is not None:
                problems.add(
                    "COMMAND_CONTROL_NOTRUN_BINDING",
                    document.relative_path,
                    "non-Passed Gate cannot claim a successful command-control binding",
                )
            continue
        requirement = requirements.get(gate_id)
        if not isinstance(requirement, Mapping):
            continue
        _validate_command_control_document(
            repo_root,
            document,
            gate,
            requirement,
            contract,
            gate_by_id,
            handoff_by_group,
            semantic_command_id,
            problems,
        )


def _validate_requirements_manifest_git_identity(
    repo_root: Path,
    manifest_document: Document,
    problems: Problems,
) -> bool:
    """Root manifest authority in candidate-B Git identity without a hash cycle."""

    valid = True
    if manifest_document.relative_path != GATE_REQUIREMENTS_PATH:
        problems.add(
            "REQUIREMENTS_MANIFEST_PATH",
            manifest_document.relative_path,
            "requirements manifest must use its one canonical repository path",
        )
        return False
    if not _validate_single_add_immutable_blob(
        repo_root,
        GATE_REQUIREMENTS_PATH,
        problems,
        "REQUIREMENTS_MANIFEST",
    ):
        valid = False
    first_add = _single_add_commit(repo_root, GATE_REQUIREMENTS_PATH)
    parent_line = (
        _git_stdout(
            repo_root,
            ("rev-list", "--parents", "-n", "1", str(first_add)),
        )
        if first_add is not None
        else None
    )
    parent_fields = parent_line.split() if parent_line is not None else []
    if (
        first_add is None
        or parent_fields != [first_add, W84_BOOTSTRAP_PARENT]
        or _git_commit_changed_paths(repo_root, first_add)
        != set(BOOTSTRAP_CONTROL_PATHS)
    ):
        problems.add(
            "REQUIREMENTS_MANIFEST_BOOTSTRAP",
            GATE_REQUIREMENTS_PATH,
            "manifest must be first-added by the exact single-parent W84 bootstrap control commit",
        )
        valid = False
    first_raw = (
        _git_bytes(repo_root, ("show", f"{first_add}:{GATE_REQUIREMENTS_PATH}"))
        if first_add is not None
        else None
    )
    if (
        first_raw is None
        or hashlib.sha256(first_raw).hexdigest() != manifest_document.sha256
    ):
        problems.add(
            "REQUIREMENTS_MANIFEST_CANDIDATE_B",
            GATE_REQUIREMENTS_PATH,
            "parsed manifest bytes must equal the candidate-B bootstrap Git blob",
        )
        valid = False
    return valid


def validate_gate_requirements_manifest(
    manifest_document: Document | None,
    goal_document: Document | None,
    gate_documents: Sequence[Document],
    contract: Contract,
    problems: Problems,
    repo_root: Path | None = None,
    handoff_documents: Sequence[Document] | None = None,
    control_root: Path | None = None,
    descriptor_evidence_root: Path | None = None,
) -> None:
    """Bind every Gate to its tracked, Gate-specific acceptance contract."""
    evidence_root = control_root or repo_root
    if manifest_document is None:
        problems.add(
            "REQUIREMENTS_MANIFEST_MISSING",
            GATE_REQUIREMENTS_PATH,
            "tracked Gate requirements manifest is required",
        )
        return
    manifest = manifest_document.data
    if not isinstance(manifest, Mapping):
        problems.add(
            "REQUIREMENTS_MANIFEST_TYPE",
            GATE_REQUIREMENTS_PATH,
            "Gate requirements manifest must be an object",
        )
        return
    if repo_root is not None:
        _validate_requirements_manifest_git_identity(
            repo_root.resolve(), manifest_document, problems
        )
    if (
        manifest.get("schemaVersion") != SCHEMA_VERSION
        or manifest.get("registryVersion") != GATE_REQUIREMENTS_VERSION
        or manifest.get("goalId") != GOAL_ID
        or manifest.get("gateCount") != len(contract.registry)
    ):
        problems.add(
            "REQUIREMENTS_MANIFEST_HEADER",
            GATE_REQUIREMENTS_PATH,
            "Gate requirements manifest header is not frozen",
        )
    trusted_runner_policy = _mapping(
        _mapping(manifest.get("frozenPolicies")).get("trusted-executor")
    )
    def frozen_source_sha(relative_path: str) -> str | None:
        if repo_root is None:
            return None
        try:
            return hashlib.sha256(
                (repo_root / relative_path).read_bytes()
            ).hexdigest()
        except OSError:
            return None

    current_runner_sha = frozen_source_sha(TEST_RUNNER_PATH)
    if (
        set(trusted_runner_policy)
        != {"runnerId", "sourcePath", "sourceSha256", "shellAllowed"}
        or trusted_runner_policy.get("runnerId") != TEST_RUNNER_ID
        or trusted_runner_policy.get("sourcePath") != TEST_RUNNER_PATH
        or trusted_runner_policy.get("shellAllowed") is not False
        or not isinstance(trusted_runner_policy.get("sourceSha256"), str)
        or HEX_SHA256.fullmatch(
            str(trusted_runner_policy.get("sourceSha256"))
        )
        is None
        or (
            current_runner_sha is not None
            and trusted_runner_policy.get("sourceSha256")
            != current_runner_sha
        )
    ):
        problems.add(
            "REQUIREMENTS_TRUSTED_EXECUTOR_POLICY",
            GATE_REQUIREMENTS_PATH,
            "manifest must freeze the exact non-shell trusted executor source",
        )
    trusted_test_policy = _mapping(
        _mapping(manifest.get("frozenPolicies")).get("trusted-test-command")
    )
    expected_test_entry_sources = [
        {
            "moduleName": module_name,
            "path": path,
            "sha256": frozen_source_sha(path),
        }
        for module_name, path in trusted_executor.SEMANTIC_ENTRY_SOURCES
    ]
    expected_test_read_sources = [
        {"path": path, "sha256": frozen_source_sha(path)}
        for path in trusted_executor.SEMANTIC_READ_SOURCES
    ]
    declared_test_entries = _list(trusted_test_policy.get("entrySources"))
    declared_test_reads = _list(trusted_test_policy.get("readSources"))
    entry_sources_valid = bool(
        len(declared_test_entries) == len(expected_test_entry_sources)
        and all(
            isinstance(actual, Mapping)
            and set(actual) == {"moduleName", "path", "sha256"}
            and actual.get("moduleName") == expected["moduleName"]
            and actual.get("path") == expected["path"]
            and isinstance(actual.get("sha256"), str)
            and HEX_SHA256.fullmatch(str(actual.get("sha256"))) is not None
            and (
                expected["sha256"] is None
                or actual.get("sha256") == expected["sha256"]
            )
            for actual, expected in zip(
                declared_test_entries,
                expected_test_entry_sources,
                strict=True,
            )
        )
    )
    read_sources_valid = bool(
        len(declared_test_reads) == len(expected_test_read_sources)
        and all(
            isinstance(actual, Mapping)
            and set(actual) == {"path", "sha256"}
            and actual.get("path") == expected["path"]
            and isinstance(actual.get("sha256"), str)
            and HEX_SHA256.fullmatch(str(actual.get("sha256"))) is not None
            and (
                expected["sha256"] is None
                or actual.get("sha256") == expected["sha256"]
            )
            for actual, expected in zip(
                declared_test_reads,
                expected_test_read_sources,
                strict=True,
            )
        )
    )
    if (
        set(trusted_test_policy)
        != {
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
        or trusted_test_policy.get("policyId")
        != trusted_executor.TRUSTED_TEST_POLICY_ID
        or trusted_test_policy.get("commandId")
        != trusted_executor.TRUSTED_TEST_COMMAND_ID
        or trusted_test_policy.get("executableRole")
        != ISOLATED_PYTHON_EXECUTABLE_ROLE
        or trusted_test_policy.get("arguments")
        != list(trusted_executor._SEMANTIC_ARGUMENTS)
        or trusted_test_policy.get("redactedInvocation")
        != trusted_executor.SEMANTIC_REDACTED_INVOCATION
        or trusted_test_policy.get("countsSource")
        != trusted_executor.TRUSTED_TEST_COUNTS_SOURCE
        or trusted_test_policy.get("shellAllowed") is not False
        or trusted_test_policy.get("loaderProtocol")
        != trusted_executor.SEMANTIC_LOADER_PROTOCOL
        or not entry_sources_valid
        or not read_sources_valid
    ):
        problems.add(
            "REQUIREMENTS_TRUSTED_TEST_POLICY",
            GATE_REQUIREMENTS_PATH,
            "manifest must freeze one exact non-shell semantic test argv policy",
        )
    trusted_provider_policy = _mapping(
        _mapping(manifest.get("frozenPolicies")).get(
            "trusted-provider-command"
        )
    )
    if (
        set(trusted_provider_policy)
        != {
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
        or trusted_provider_policy.get("policyId")
        != TRUSTED_PROVIDER_POLICY_ID
        or trusted_provider_policy.get("executableRole")
        != ISOLATED_PYTHON_EXECUTABLE_ROLE
        or trusted_provider_policy.get("sourcePath")
        != TRUSTED_PROVIDER_HARNESS_PATH
        or not isinstance(trusted_provider_policy.get("sourceSha256"), str)
        or HEX_SHA256.fullmatch(
            str(trusted_provider_policy.get("sourceSha256"))
        )
        is None
        or (
            repo_root is not None
            and frozen_source_sha(TRUSTED_PROVIDER_HARNESS_PATH) is not None
            and trusted_provider_policy.get("sourceSha256")
            != frozen_source_sha(TRUSTED_PROVIDER_HARNESS_PATH)
        )
        or not isinstance(
            trusted_provider_policy.get("scenarioSources"), Mapping
        )
        or set(_mapping(trusted_provider_policy.get("scenarioSources")))
        != set(PROVIDER_SCENARIO_PATHS)
        or any(
            not isinstance(binding, Mapping)
            or set(binding) != {"path", "sha256"}
            or binding.get("path") != PROVIDER_SCENARIO_PATHS[phase]
            or not isinstance(binding.get("sha256"), str)
            or HEX_SHA256.fullmatch(str(binding.get("sha256"))) is None
            for phase, binding in _mapping(
                trusted_provider_policy.get("scenarioSources")
            ).items()
        )
        or any(
            repo_root is not None
            and isinstance(binding, Mapping)
            and isinstance(binding.get("path"), str)
            and frozen_source_sha(str(binding.get("path"))) is not None
            and binding.get("sha256")
            != frozen_source_sha(str(binding.get("path")))
            for binding in _mapping(
                trusted_provider_policy.get("scenarioSources")
            ).values()
        )
        or trusted_provider_policy.get("argumentsTemplate")
        != list(trusted_executor._PROVIDER_TEMPLATE_ARGUMENTS)
        or trusted_provider_policy.get("redactedInvocation")
        != TRUSTED_PROVIDER_REDACTED_INVOCATION
        or trusted_provider_policy.get("shellAllowed") is not False
        or trusted_provider_policy.get("allowedBatchSizes")
        != {key: [value] for key, value in PROVIDER_BATCH_SIZES.items()}
        or trusted_provider_policy.get("observedRequestProtocol")
        != PROVIDER_OBSERVED_REQUEST_PROTOCOL
    ):
        problems.add(
            "REQUIREMENTS_TRUSTED_PROVIDER_POLICY",
            GATE_REQUIREMENTS_PATH,
            "manifest must freeze the exact observed provider batch policy",
        )
    trusted_product_policy = _mapping(
        _mapping(manifest.get("frozenPolicies")).get(
            "trusted-product-command"
        )
    )
    trusted_git_policy = _mapping(
        _mapping(manifest.get("frozenPolicies")).get("trusted-git")
    )
    if (
        set(trusted_product_policy)
        != {
            "policyId",
            "executableRole",
            "scriptRoot",
            "argumentsTemplate",
            "redactedInvocationTemplate",
            "shellAllowed",
        }
        or trusted_product_policy.get("policyId")
        != TRUSTED_PRODUCT_POLICY_ID
        or trusted_product_policy.get("executableRole")
        != ISOLATED_PYTHON_EXECUTABLE_ROLE
        or trusted_product_policy.get("scriptRoot")
        != TRUSTED_PRODUCT_SCRIPT_ROOT
        or trusted_product_policy.get("argumentsTemplate")
        != list(trusted_executor._PRODUCT_TEMPLATE_ARGUMENTS)
        or trusted_product_policy.get("redactedInvocationTemplate")
        != (
            "python-current -I -S -E -B -X utf8 -c "
            "<week84-92-in-memory-command-adapter-v1:{commandId}>"
        )
        or trusted_product_policy.get("shellAllowed") is not False
    ):
        problems.add(
            "REQUIREMENTS_TRUSTED_PRODUCT_POLICY",
            GATE_REQUIREMENTS_PATH,
            "manifest must freeze the exact non-shell derived product command policy",
        )
    if (
        set(trusted_git_policy)
        != {
            "policyId",
            "executableRole",
            "version",
            "executableBytes",
            "executableSha256",
            "shellAllowed",
        }
        or trusted_git_policy.get("policyId") != TRUSTED_GIT_POLICY_ID
        or trusted_git_policy.get("executableRole")
        != TRUSTED_GIT_EXECUTABLE_ROLE
        or trusted_git_policy.get("version") != TRUSTED_GIT_VERSION
        or trusted_git_policy.get("executableBytes")
        != TRUSTED_GIT_EXECUTABLE_BYTES
        or trusted_git_policy.get("executableSha256")
        != TRUSTED_GIT_EXECUTABLE_SHA256
        or trusted_git_policy.get("shellAllowed") is not False
    ):
        problems.add(
            "REQUIREMENTS_TRUSTED_GIT_POLICY",
            GATE_REQUIREMENTS_PATH,
            "manifest must freeze the exact non-shell external Git executable policy",
        )
    requirements = _list(manifest.get("gates"))
    requirement_ids = [
        item.get("gateId") for item in requirements if isinstance(item, Mapping)
    ]
    if requirement_ids != list(contract.registry) or len(requirements) != len(
        contract.registry
    ):
        problems.add(
            "REQUIREMENTS_MANIFEST_REGISTRY",
            GATE_REQUIREMENTS_PATH,
            "Gate requirements must be the exact ordered 104-Gate registry",
        )
    requirement_by_id = {
        str(item.get("gateId")): item
        for item in requirements
        if isinstance(item, Mapping) and isinstance(item.get("gateId"), str)
    }
    w84_g5_flags = set(
        _list(
            _mapping(
                _mapping(requirement_by_id.get("W84-G5")).get("frozen")
            ).get("compatibilityFlags")
        )
    )
    if "provider-runtime-drivers-first-add-sealed" not in w84_g5_flags:
        problems.add(
            "REQUIREMENTS_PROVIDER_DRIVER_PRESEAL",
            GATE_REQUIREMENTS_PATH,
            "W84-G5 must pre-seal all provider runtime drivers before their first use",
        )
    for provider_gate_id in PROVIDER_GATE_REQUIREMENTS:
        provider_flags = set(
            _list(
                _mapping(
                    _mapping(requirement_by_id.get(provider_gate_id)).get("frozen")
                ).get("compatibilityFlags")
            )
        )
        if "provider-runtime-drivers-prior-sealed" not in provider_flags:
            problems.add(
                "REQUIREMENTS_PROVIDER_DRIVER_PRESEAL",
                GATE_REQUIREMENTS_PATH,
                f"{provider_gate_id} must consume only strict-prior sealed runtime drivers",
            )
    by_id: dict[str, Mapping[str, Any]] = {}
    for item in requirements:
        if not isinstance(item, Mapping):
            continue
        gate_id = item.get("gateId")
        if isinstance(gate_id, str) and gate_id not in by_id:
            by_id[gate_id] = item
        expected_command_control = (
            _expected_command_control_policy(contract, gate_id)
            if isinstance(gate_id, str)
            else None
        )
        if expected_command_control is None or item.get(
            "commandControl"
        ) != expected_command_control:
            problems.add(
                "REQUIREMENTS_COMMAND_CONTROL_POLICY",
                GATE_REQUIREMENTS_PATH,
                "each Gate must freeze its exact command-control path, trust, predecessor mode, and role policy",
            )
        minimum_tests = item.get("minimumTestCount")
        required_kinds = set(_list(item.get("requiredEvidenceKinds")))
        if (
            _is_int(minimum_tests)
            and minimum_tests > 0
            and "test-report" not in required_kinds
        ):
            problems.add(
                "REQUIREMENTS_TEST_REPORT_POLICY",
                GATE_REQUIREMENTS_PATH,
                "every positive minimumTestCount Gate must require a test-report",
            )

    if goal_document is not None and isinstance(goal_document.data, Mapping):
        binding = _mapping(goal_document.data.get("gateRequirements"))
        expected_binding = {
            "path": GATE_REQUIREMENTS_PATH,
            "sha256": manifest_document.sha256,
            "registryVersion": GATE_REQUIREMENTS_VERSION,
            "gateCount": len(contract.registry),
        }
        if binding != expected_binding:
            problems.add(
                "REQUIREMENTS_GOAL_BINDING",
                goal_document.relative_path,
                "central Goal does not bind the tracked requirements manifest bytes",
            )

    manual_ids = {
        gate_id: requirement["acceptanceId"]
        for gate_id, requirement in MANUAL_GATE_REQUIREMENTS.items()
    }
    for document in gate_documents:
        gate = _mapping(document.data)
        gate_id = gate.get("gateId")
        requirement = by_id.get(str(gate_id))
        if requirement is None:
            problems.add(
                "REQUIREMENTS_GATE_MISSING",
                document.relative_path,
                "Gate has no frozen requirements entry",
            )
            continue
        requirement_sha = hashlib.sha256(_json_bytes(requirement)).hexdigest()
        expected_binding = {
            "path": GATE_REQUIREMENTS_PATH,
            "manifestSha256": manifest_document.sha256,
            "registryVersion": GATE_REQUIREMENTS_VERSION,
            "gateRequirementSha256": requirement_sha,
        }
        if _mapping(gate.get("requirementsBinding")) != expected_binding:
            problems.add(
                "REQUIREMENTS_GATE_BINDING",
                document.relative_path,
                "Gate does not bind its exact tracked requirement entry",
            )
        group = contract.gate_to_group.get(str(gate_id))
        if group is None or any(
            (
                requirement.get("week") != group.week,
                requirement.get("lane") != group.lane,
                requirement.get("resultPath") != document.relative_path,
            )
        ):
            problems.add(
                "REQUIREMENTS_GATE_IDENTITY",
                document.relative_path,
                "Gate requirement week/lane/path does not match the Gate document",
            )

        provider_requirement = requirement.get("providerRequirement")
        expected_provider = PROVIDER_GATE_REQUIREMENTS.get(str(gate_id))
        if expected_provider is None:
            if provider_requirement is not None:
                problems.add(
                    "REQUIREMENTS_PROVIDER_POLICY",
                    document.relative_path,
                    "non-provider Gate claims a provider requirement",
                )
        elif not isinstance(provider_requirement, Mapping):
            problems.add(
                "REQUIREMENTS_PROVIDER_POLICY",
                document.relative_path,
                "provider Gate is missing its frozen provider requirement",
            )
        else:
            receipt_names = set(_list(provider_requirement.get("receiptBasenames")))
            boundary_path = PROVIDER_BOUNDARY_DECISION_PATHS.get(str(gate_id))
            launch_layout = PROVIDER_LAUNCH_LAYOUT.get(str(gate_id), ())
            if (
                set(provider_requirement)
                != {
                    "minimumTurns",
                    "scopes",
                    "receiptBasenames",
                    "consecutiveProfileCount",
                    "warmupTurnsPerProfile",
                    "measuredTurnsPerProfile",
                    "boundaryDecisionPath",
                    "allowedBoundaryModes",
                    "packageLaunchReceiptCount",
                }
                or provider_requirement.get("minimumTurns")
                != expected_provider["minTurns"]
                or set(_list(provider_requirement.get("scopes")))
                != set(expected_provider["scopes"])
                or receipt_names != set(expected_provider.get("receipts", set()))
                or provider_requirement.get("boundaryDecisionPath")
                != boundary_path
                or provider_requirement.get("allowedBoundaryModes")
                != list(PROVIDER_BOUNDARY_MODES)
                or provider_requirement.get("packageLaunchReceiptCount")
                != len(launch_layout)
            ):
                problems.add(
                    "REQUIREMENTS_PROVIDER_POLICY",
                    document.relative_path,
                    "provider requirement differs from the frozen validator policy",
                )
            compatibility_flags = set(
                _list(_mapping(requirement.get("frozen")).get("compatibilityFlags"))
            )
            if (
                not {
                    "declared-provider-boundary-mode",
                    "exact-package-tree-launched",
                    "package-launch-receipt",
                    "provider-runtime-drivers-prior-sealed",
                }.issubset(compatibility_flags)
                or compatibility_flags
                & {
                    "isolated-packaged-child",
                    "all-egress-mediated",
                    "isolated-child-secret-redaction",
                }
            ):
                problems.add(
                    "REQUIREMENTS_PROVIDER_BOUNDARY_FLAGS",
                    document.relative_path,
                    "provider Gates must declare neutral boundary mode and exact package-launch provenance without an unproven isolation claim",
                )
        expected_controlled = str(gate_id) in CONTROLLED_WRITE_GATES.values()
        if requirement.get("controlledWrite") is not expected_controlled:
            problems.add(
                "REQUIREMENTS_CONTROLLED_WRITE_POLICY",
                document.relative_path,
                "controlled-write requirement differs from the authorized Gate set",
            )
        if requirement.get("manualAcceptanceId") != manual_ids.get(str(gate_id)):
            problems.add(
                "REQUIREMENTS_MANUAL_POLICY",
                document.relative_path,
                "manual acceptance requirement differs from the frozen Gate set",
            )

        if gate.get("status") != "Passed":
            continue
        commands = [
            item for item in _list(gate.get("commands")) if isinstance(item, Mapping)
        ]
        commands_by_id = {str(item.get("commandId")): item for item in commands}
        required_commands = {
            item
            for item in _list(requirement.get("requiredCommandIds"))
            if isinstance(item, str)
        }
        required_names = {
            item
            for item in _list(
                requirement.get("requiredEvidenceBasenames")
            )
            if isinstance(item, str)
        }
        minimum_commands = requirement.get("minimumCommandCount")
        if not _is_int(minimum_commands) or len(commands) < minimum_commands:
            problems.add(
                "REQUIREMENTS_COMMAND_COUNT",
                document.relative_path,
                "Passed Gate executed fewer than its frozen minimum commands",
            )
        if not required_commands.issubset(commands_by_id) or any(
            _mapping(commands_by_id.get(command_id)).get("status") != "Passed"
            for command_id in required_commands
        ):
            problems.add(
                "REQUIREMENTS_COMMAND_IDS",
                document.relative_path,
                "Passed Gate lacks a required successful semantic command",
            )
        evidence = [
            item for item in _list(gate.get("evidence")) if isinstance(item, Mapping)
        ]
        evidence_by_id = {
            item.get("evidenceId"): item
            for item in evidence
            if isinstance(item.get("evidenceId"), str) and item.get("evidenceId")
        }

        def require_resolved_refs(
            owner: Mapping[str, Any] | None,
            code: str,
            owner_kind: str,
        ) -> None:
            refs = owner.get("evidenceRefs") if isinstance(owner, Mapping) else None
            if (
                not isinstance(refs, list)
                or not refs
                or any(
                    not isinstance(ref, str) or ref not in evidence_by_id
                    for ref in refs
                )
            ):
                problems.add(
                    code,
                    document.relative_path,
                    f"required {owner_kind} must reference existing Gate evidence",
                )

        for command_id in required_commands:
            require_resolved_refs(
                commands_by_id.get(command_id),
                "REQUIREMENTS_COMMAND_EVIDENCE",
                f"command {command_id}",
            )

        product_report_counts: list[dict[str, int]] = []
        product_bundle_summaries: dict[str, dict[str, Any]] = {}
        semantic_report_document: Document | None = None
        semantic_command_id = trusted_test_policy.get("commandId")
        if repo_root is not None and evidence_root is not None:
            for command_id in sorted(required_commands):
                command = _mapping(commands_by_id.get(command_id))
                refs = {
                    ref
                    for ref in _list(command.get("evidenceRefs"))
                    if isinstance(ref, str)
                }
                referenced = [
                    (evidence_id, evidence_by_id[evidence_id])
                    for evidence_id in refs
                    if evidence_id in evidence_by_id
                ]
                if command_id == semantic_command_id:
                    semantic_reports = [
                        (evidence_id, item)
                        for evidence_id, item in referenced
                        if item.get("kind") == "test-report"
                    ]
                    if len(semantic_reports) != 1:
                        problems.add(
                            "REQUIREMENTS_SEMANTIC_COMMAND_REPORT",
                            document.relative_path,
                            "semantic control command must reference its unique trusted test-report",
                        )
                    else:
                        _semantic_id, semantic_item = semantic_reports[0]
                        semantic_path = _safe_relative_evidence_path(
                            evidence_root,
                            semantic_item.get("path"),
                            problems,
                            f"{document.relative_path}#/commands/{command_id}/semantic-report",
                        )
                        semantic_document = (
                            read_document(
                                evidence_root, semantic_path, problems
                            )
                            if semantic_path is not None
                            and semantic_path.is_file()
                            else None
                        )
                        if (
                            semantic_document is not None
                            and semantic_item.get("sha256")
                            == semantic_document.sha256
                            and _mapping(semantic_document.data).get(
                                "commandId"
                            )
                            == semantic_command_id
                        ):
                            semantic_report_document = semantic_document
                        else:
                            problems.add(
                                "REQUIREMENTS_SEMANTIC_COMMAND_REPORT",
                                document.relative_path,
                                "semantic report path/hash/command binding is invalid",
                            )
                    continue

                bundle_documents: list[
                    tuple[str, Mapping[str, Any], Document]
                ] = []
                for evidence_id, referenced_item in referenced:
                    if referenced_item.get("kind") != "json":
                        continue
                    resolved = _safe_relative_evidence_path(
                        evidence_root,
                        referenced_item.get("path"),
                        problems,
                        f"{document.relative_path}#/commands/{command_id}/evidence",
                    )
                    if resolved is None or not resolved.is_file():
                        continue
                    report_document = read_document(
                        evidence_root, resolved, problems
                    )
                    report = (
                        _mapping(report_document.data)
                        if report_document is not None
                        else {}
                    )
                    if (
                        report_document is not None
                        and report.get("commandId") == command_id
                    ):
                        bundle_documents.append(
                            (evidence_id, referenced_item, report_document)
                        )
                counts = _validate_product_command_bundle(
                    bundle_documents,
                    gate,
                    command,
                    command_id,
                    repo_root,
                    problems,
                    f"{document.relative_path}#/commands/{command_id}",
                    trusted_runner_policy,
                    trusted_product_policy,
                    trusted_git_policy,
                    required_names,
                    control_root=evidence_root,
                    descriptor_evidence_root=descriptor_evidence_root,
                )
                if counts is not None:
                    product_report_counts.append(counts)
                    if gate_id == "W84-G0":
                        bundle_summary = _valid_product_bundle_summary(
                            command_id, bundle_documents
                        )
                        if bundle_summary is not None:
                            product_bundle_summaries[command_id] = bundle_summary

            if gate_id == "W84-G0":
                _validate_w84_g0_baseline_identity(
                    repo_root=repo_root,
                    gate=gate,
                    evidence=evidence,
                    problems=problems,
                    location=document.relative_path,
                    control_root=evidence_root,
                )
                _validate_w84_g0_canonical_summaries(
                    repo_root=repo_root,
                    gate=gate,
                    requirement=requirement,
                    evidence=evidence,
                    semantic_report=semantic_report_document,
                    product_bundles=product_bundle_summaries,
                    trusted_runner_policy=trusted_runner_policy,
                    trusted_test_policy=trusted_test_policy,
                    trusted_product_policy=trusted_product_policy,
                    trusted_git_policy=trusted_git_policy,
                    problems=problems,
                    location=document.relative_path,
                    control_root=evidence_root,
                )

        test_counts = _mapping(gate.get("testCounts"))
        discovered = test_counts.get("discovered")
        passed_tests = test_counts.get("passed")
        minimum_tests = requirement.get("minimumTestCount")
        if (
            not _is_int(discovered)
            or not _is_int(passed_tests)
            or not _is_int(minimum_tests)
            or discovered < minimum_tests
            or passed_tests < minimum_tests
        ):
            problems.add(
                "REQUIREMENTS_TEST_COUNT",
                document.relative_path,
                "Passed Gate discovered/passed counts are below its frozen minimum acceptance-case count",
            )
        if (
            repo_root is not None
            and _is_int(minimum_tests)
            and minimum_tests > 0
            and gate_id not in {"W84-G0", "W92-G8"}
        ):
            product_discovered = sum(
                item["discovered"] for item in product_report_counts
            )
            product_passed = sum(
                item["passed"] for item in product_report_counts
            )
            if (
                product_discovered < minimum_tests
                or product_passed < minimum_tests
            ):
                problems.add(
                    "REQUIREMENTS_PRODUCT_TEST_COUNT",
                    document.relative_path,
                    "Gate minimum tests must come from trusted product command result reports",
                )
        assertions = [
            item
            for item in _list(gate.get("acceptanceAssertions"))
            if isinstance(item, Mapping)
        ]
        assertions_by_id = {
            str(item.get("assertionId")): item for item in assertions
        }
        required_assertions = {
            item
            for item in _list(requirement.get("requiredAssertionIds"))
            if isinstance(item, str)
        }
        if not required_assertions.issubset(assertions_by_id) or any(
            _mapping(assertions_by_id.get(assertion_id)).get("status") != "Passed"
            for assertion_id in required_assertions
        ):
            problems.add(
                "REQUIREMENTS_ASSERTION_IDS",
                document.relative_path,
                "Passed Gate lacks a required successful acceptance assertion",
            )
        for assertion_id in required_assertions:
            require_resolved_refs(
                assertions_by_id.get(assertion_id),
                "REQUIREMENTS_ASSERTION_EVIDENCE",
                f"assertion {assertion_id}",
            )

        required_kinds = {
            item
            for item in _list(requirement.get("requiredEvidenceKinds"))
            if isinstance(item, str)
        }
        evidence_kinds: set[str] = set()
        evidence_names: set[str] = set()
        evidence_name_counts: Counter[str] = Counter()
        canonical_evidence_kinds: set[str] = set()
        result_parts = _normalised_repo_parts(requirement.get("resultPath"))
        artifact_parts = (
            result_parts[:-2]
            if result_parts is not None and len(result_parts) >= 3
            else None
        )
        artifact_root = (
            (evidence_root.joinpath(*artifact_parts)).resolve()
            if evidence_root is not None and artifact_parts is not None
            else None
        )
        test_report_bound = False
        for index, item in enumerate(evidence):
            item_location = f"{document.relative_path}#/evidence/{index}"
            kind = item.get("kind")
            if isinstance(kind, str):
                evidence_kinds.add(kind)
            else:
                problems.add(
                    "REQUIREMENTS_EVIDENCE_KIND_TYPE",
                    item_location,
                    "evidence kind must be a string",
                )
            path_parts = _normalised_repo_parts(item.get("path"))
            basename = path_parts[-1] if path_parts else None
            if basename is not None:
                evidence_names.add(basename)
                evidence_name_counts[basename] += 1
            reserved_report = bool(
                isinstance(basename, str)
                and (
                    re.fullmatch(
                        rf"{re.escape(str(semantic_command_id))}\."
                        rf"[a-z0-9]+(?:-[a-z0-9]+){{0,15}}\."
                        r"trusted-test-report\.json",
                        basename,
                    )
                    is not None
                    or re.fullmatch(
                        r"[a-z0-9]+(?:-[a-z0-9]+)*\."
                        r"[a-z0-9]+(?:-[a-z0-9]+){0,15}\."
                        r"trusted-product-report\.json",
                        basename,
                    )
                    is not None
                )
            )
            required_item = basename in required_names or (
                isinstance(kind, str) and kind in required_kinds
            ) or reserved_report
            if not required_item:
                continue
            expected_parts = (
                (
                    *artifact_parts,
                    "provider-ledger-bindings",
                    str(gate_id),
                    PROVIDER_LEDGER_BINDING_BASENAME,
                )
                if artifact_parts is not None
                and basename == PROVIDER_LEDGER_BINDING_BASENAME
                else (
                    *artifact_parts,
                    "gate-evidence",
                    str(gate_id),
                    str(basename),
                )
                if artifact_parts is not None and basename is not None
                else None
            )
            in_artifact = bool(
                expected_parts is not None
                and path_parts is not None
                and tuple(path_parts) == tuple(expected_parts)
            )
            if in_artifact and isinstance(kind, str):
                canonical_evidence_kinds.add(kind)
            if (
                in_artifact
                and evidence_root is not None
                and artifact_root is not None
            ):
                resolved = _safe_relative_evidence_path(
                    evidence_root,
                    item.get("path"),
                    problems,
                    f"{item_location}/path",
                )
                if resolved is None:
                    in_artifact = False
                else:
                    try:
                        resolved.relative_to(artifact_root)
                    except ValueError:
                        in_artifact = False
            if not in_artifact:
                problems.add(
                    "REQUIREMENTS_EVIDENCE_PATH",
                    item_location,
                    "required evidence must use its exact canonical Gate-local path",
                )

            is_json_payload = (
                (isinstance(kind, str) and kind in {"json", "test-report"})
                or (isinstance(basename, str) and basename.lower().endswith(".json"))
            )
            if is_json_payload and in_artifact:
                test_report_bound = (
                    _validate_requirement_evidence_payload(
                        item,
                        gate,
                        repo_root,
                        minimum_tests if _is_int(minimum_tests) else 0,
                        problems,
                        item_location,
                        trusted_runner_policy,
                        trusted_test_policy,
                        trusted_git_policy,
                        required_names,
                        control_root=evidence_root,
                    )
                    or test_report_bound
                )

        if not required_kinds.issubset(canonical_evidence_kinds):
            problems.add(
                "REQUIREMENTS_EVIDENCE_KINDS",
                document.relative_path,
                "Passed Gate lacks a required evidence kind",
            )
        if any(evidence_name_counts.get(name) != 1 for name in required_names):
            problems.add(
                "REQUIREMENTS_EVIDENCE_FILES",
                document.relative_path,
                "Passed Gate requires every frozen evidence basename exactly once",
            )
        if repo_root is not None and "test-report" in required_kinds and not test_report_bound:
            problems.add(
                "REQUIREMENTS_EVIDENCE_TEST_REPORT_BINDING",
                document.relative_path,
                "Passed Gate requires a test-report whose counts bind its Gate testCounts",
            )

    if repo_root is not None and handoff_documents is not None:
        validate_command_controls(
            manifest,
            gate_documents,
            handoff_documents,
            contract,
            repo_root,
            problems,
        )


def _unique_gate_documents(
    gate_documents: Sequence[Document], problems: Problems
) -> dict[str, Document]:
    result: dict[str, Document] = {}
    for document in gate_documents:
        data = _mapping(document.data)
        gate_id = data.get("gateId")
        if not isinstance(gate_id, str):
            continue
        previous = result.get(gate_id)
        if previous is not None:
            problems.add(
                "GATE_DUPLICATE",
                document.relative_path,
                "a Gate ID is supplied by more than one result document",
            )
            continue
        result[gate_id] = document
    return result


def validate_global_evidence_ids(
    gate_by_id: Mapping[str, Document], problems: Problems
) -> None:
    """Keep every evidence reference unambiguous across the whole Goal."""
    owners: dict[str, tuple[str, str]] = {}
    path_owners: dict[tuple[str, ...], str] = {}
    for gate_id, document in gate_by_id.items():
        for evidence in _list(_mapping(document.data).get("evidence")):
            if not isinstance(evidence, Mapping):
                continue
            evidence_id = evidence.get("evidenceId")
            if not isinstance(evidence_id, str) or not evidence_id:
                continue
            previous = owners.get(evidence_id)
            if previous is not None:
                problems.add(
                    "EVIDENCE_ID_GLOBAL_DUPLICATE",
                    document.relative_path,
                    "evidenceId must be unique across the Goal",
                )
            else:
                owners[evidence_id] = (gate_id, document.relative_path)
            path_parts = _normalised_repo_parts(evidence.get("path"))
            if path_parts is None:
                continue
            previous_gate = path_owners.get(path_parts)
            if previous_gate is not None and previous_gate != gate_id:
                problems.add(
                    "EVIDENCE_PATH_GLOBAL_DUPLICATE",
                    document.relative_path,
                    "one evidence path cannot be shared by multiple Gates",
                )
            else:
                path_owners[path_parts] = gate_id


def _sum_rollup(
    documents: Iterable[Document], field: str, keys: Sequence[str]
) -> dict[str, int]:
    result = {key: 0 for key in keys}
    for document in documents:
        counts = _mapping(_mapping(document.data).get(field))
        for key in keys:
            value = counts.get(key)
            if _is_int(value):
                result[key] += value
    return result


def _compare_rollup(
    problems: Problems,
    location: str,
    actual: Any,
    expected: Mapping[str, int],
) -> None:
    if not isinstance(actual, Mapping):
        problems.add("ROLLUP_TYPE", location, "rollup must be an object")
        return
    for key, expected_value in expected.items():
        if actual.get(key) != expected_value:
            problems.add(
                "ROLLUP_ACTUAL",
                location,
                f"{key}={actual.get(key)!r}, actual evidence requires {expected_value}",
            )


def _provider_summary(goal: Mapping[str, Any]) -> Mapping[str, Any]:
    return _mapping(_mapping(_mapping(goal.get("authorization")).get("provider")))


def _controlled_summary(goal: Mapping[str, Any]) -> Mapping[str, Any]:
    return _mapping(
        _mapping(_mapping(goal.get("authorization")).get("controlledWrite")).get("executions")
    )


def _first_present(mapping: Mapping[str, Any], names: Sequence[str]) -> Any:
    for name in names:
        if name in mapping:
            return mapping[name]
    return None


def provider_entry_sha256(entry: Mapping[str, Any]) -> str:
    """Hash a ledger entry's canonical payload, excluding only its own hash."""
    payload = {key: value for key, value in entry.items() if key != "entrySha256"}
    return hashlib.sha256(_json_bytes(payload)).hexdigest()


def provider_attempt_event_sha256(event: Mapping[str, Any]) -> str:
    """Hash an attempt event's canonical payload, excluding only its own hash."""
    payload = {
        key: value
        for key, value in event.items()
        if key != "attemptEventSha256"
    }
    return hashlib.sha256(_json_bytes(payload)).hexdigest()


def provider_reservation_prefix_sha256(
    entries: Sequence[Mapping[str, Any]],
    entry_count: int,
) -> str:
    """Hash the exact append-stable reservation prefix at a Gate boundary."""

    return evidence_anchor.ledger_prefix_sha256(
        "providerReservations",
        LEDGER_PATH,
        entries,
        entry_count,
    )


def provider_attempt_event_prefix_sha256(
    attempt_events: Sequence[Mapping[str, Any]],
    attempt_event_count: int,
) -> str:
    """Hash the exact append-stable attempt-event prefix at a Gate boundary."""

    return evidence_anchor.attempt_event_prefix_sha256(
        LEDGER_PATH,
        attempt_events,
        attempt_event_count,
    )


def provider_combined_prefix_sha256(
    entries: Sequence[Mapping[str, Any]],
    entry_count: int,
    attempt_events: Sequence[Mapping[str, Any]],
    attempt_event_count: int,
) -> str:
    """Hash both provider chains together at the same Gate boundary."""

    return evidence_anchor.ledger_prefix_sha256(
        "provider",
        LEDGER_PATH,
        entries,
        entry_count,
        attempt_events=attempt_events,
        attempt_event_count=attempt_event_count,
    )


def provider_runtime_event_sha256(event: Mapping[str, Any]) -> str:
    payload = {
        key: value for key, value in event.items() if key != "eventSha256"
    }
    return hashlib.sha256(_json_bytes(payload)).hexdigest()


def provider_runtime_prefix_sha256(
    events: Sequence[Mapping[str, Any]], event_count: int
) -> str:
    selected = list(events[:event_count])
    payload = {
        "kind": "providerRuntime",
        "path": PROVIDER_RUNTIME_JOURNAL_PATH,
        "eventCount": event_count,
        "lastEventSha256": (
            selected[-1].get("eventSha256") if selected else None
        ),
        "events": selected,
    }
    return hashlib.sha256(_json_bytes(payload)).hexdigest()


def _expected_provider_phases(gate_id: str) -> list[str]:
    return [
        phase
        for phase, count in PROVIDER_PHASE_LAYOUT.get(gate_id, ())
        for _index in range(count)
    ]


def _partition_provider_attempts(
    gate_id: str,
    entries: Sequence[Mapping[str, Any]],
    finish_events_by_attempt: Mapping[str, Mapping[str, Any]],
    problems: Problems,
    global_attempt_owners: dict[str, str],
) -> list[tuple[str, str | None, list[Mapping[str, Any]], Mapping[str, Any] | None]]:
    """Return validated contiguous attempts without echoing ledger values."""

    attempts: list[
        tuple[str, str | None, list[Mapping[str, Any]], Mapping[str, Any] | None]
    ] = []
    seen_in_gate: set[str] = set()
    current_id: str | None = None
    current_entries: list[Mapping[str, Any]] = []

    def finish_current() -> None:
        nonlocal current_id, current_entries
        if current_id is None:
            return
        finish_event = finish_events_by_attempt.get(current_id)
        outcome = finish_event.get("outcome") if finish_event is not None else None
        attempts.append((current_id, outcome, current_entries, finish_event))
        current_id = None
        current_entries = []

    for entry in entries:
        sequence = entry.get("sequence")
        entry_location = (
            f"{LEDGER_PATH}#/entries/{sequence - 1}"
            if _is_int(sequence) and sequence > 0
            else f"{LEDGER_PATH}#/entries"
        )
        attempt_id = entry.get("attemptId")
        if (
            not isinstance(attempt_id, str)
            or PROVIDER_ATTEMPT_ID.fullmatch(attempt_id) is None
        ):
            problems.add(
                "PROVIDER_ATTEMPT_ID",
                entry_location,
                "entry requires a safe non-empty attemptId",
            )
            # Keep the malformed entry isolated; never stringify its value.
            attempt_id = f"<invalid-{sequence}>"

        owner = global_attempt_owners.get(attempt_id)
        if owner is None:
            global_attempt_owners[attempt_id] = gate_id
        elif owner != gate_id:
            problems.add(
                "PROVIDER_ATTEMPT_OWNER",
                entry_location,
                "attemptId cannot be reused across provider Gates",
            )

        if current_id is None:
            if attempt_id in seen_in_gate:
                problems.add(
                    "PROVIDER_ATTEMPT_REOPENED",
                    entry_location,
                    "a closed attempt cannot be reopened",
                )
            seen_in_gate.add(attempt_id)
            current_id = attempt_id
        elif attempt_id != current_id:
            finish_current()
            if attempt_id in seen_in_gate:
                problems.add(
                    "PROVIDER_ATTEMPT_REOPENED",
                    entry_location,
                    "a closed attempt cannot be reopened",
                )
            seen_in_gate.add(attempt_id)
            current_id = attempt_id
        current_entries.append(entry)
    finish_current()

    expected = _expected_provider_phases(gate_id)
    for _attempt_id, outcome, attempt_entries, _finish_event in attempts:
        phases = [str(entry.get("phase")) for entry in attempt_entries]
        if outcome == "Passed" and phases != expected:
            problems.add(
                "PROVIDER_SUCCESS_PHASE_LAYOUT",
                LEDGER_PATH,
                "successful attempt does not equal the frozen complete phase layout",
            )
        if outcome in {"Failed", None} and (
            not phases or phases != expected[: len(phases)]
        ):
            problems.add(
                "PROVIDER_ATTEMPT_PHASE_PREFIX",
                LEDGER_PATH,
                "failed attempt must be a non-empty frozen phase-layout prefix",
            )
    return attempts


def _receipt_phase_slice(
    gate_id: str, receipt_name: str
) -> tuple[str, int, int] | None:
    phases = _expected_provider_phases(gate_id)
    if receipt_name == "provider-readonly.json":
        phase, ordinal, count = "provider-read-only", 0, 1
    elif receipt_name == "provider-recovery.json":
        phase, ordinal, count = "provider-recovery", 0, 2
    elif receipt_name == "controlled-write.json":
        phase, ordinal, count = "controlled-write", 0, 1
    else:
        match = re.fullmatch(r"provider-resource-profile-([1-5])\.json", receipt_name)
        if match is None:
            return None
        phase, ordinal, count = "provider-resource", int(match.group(1)) - 1, 6
    phase_indexes = [index for index, value in enumerate(phases) if value == phase]
    start = ordinal * count
    selected = phase_indexes[start : start + count]
    if len(selected) != count:
        return None
    return phase, selected[0], selected[-1]


def _trusted_provider_policy_from_repo(
    repo_root: Path, problems: Problems
) -> Mapping[str, Any]:
    manifest = read_document(
        repo_root, repo_root / GATE_REQUIREMENTS_PATH, problems
    )
    if manifest is None or not isinstance(manifest.data, Mapping):
        problems.add(
            "PROVIDER_RUNTIME_POLICY",
            GATE_REQUIREMENTS_PATH,
            "provider runtime policy manifest is unavailable",
        )
        return {}
    return _mapping(
        _mapping(manifest.data.get("frozenPolicies")).get(
            "trusted-provider-command"
        )
    )


def _validate_runtime_source_binding(
    source: Any,
    expected_path: str,
    expected_sha256: Any,
    product_candidate: Any,
    repo_root: Path,
    problems: Problems,
    location: str,
    code: str,
) -> bool:
    """Bind a provider source to its immutable control revision and candidate."""

    try:
        raw = (repo_root / expected_path).read_bytes()
    except OSError:
        raw = None
    tree_line = (
        _git_stdout(
            repo_root,
            ("ls-tree", str(product_candidate), "--", expected_path),
        )
        if isinstance(product_candidate, str)
        else None
    )
    tree_match = re.fullmatch(
        r"(100644|100755) blob ([0-9a-f]{40})\t.+", tree_line or ""
    )
    candidate_raw = (
        _git_bytes(repo_root, ("show", f"{product_candidate}:{expected_path}"))
        if isinstance(product_candidate, str)
        else None
    )
    control_revision = _single_add_commit(repo_root, expected_path)
    control_is_candidate_ancestor = (
        isinstance(product_candidate, str)
        and control_revision is not None
        and _git_return_code(
            repo_root,
            ("merge-base", "--is-ancestor", control_revision, product_candidate),
        )
        == 0
    )
    valid = (
        isinstance(source, Mapping)
        and set(source)
        == {"path", "sha256", "gitBlobSha", "controlRevision"}
        and source.get("path") == expected_path
        and raw is not None
        and candidate_raw == raw
        and tree_match is not None
        and source.get("gitBlobSha")
        == (tree_match.group(2) if tree_match is not None else None)
        and source.get("sha256") == hashlib.sha256(raw or b"").hexdigest()
        and source.get("sha256") == expected_sha256
        and source.get("controlRevision") == control_revision
        and control_is_candidate_ancestor
    )
    if not _validate_single_add_immutable_blob(
        repo_root,
        expected_path,
        problems,
        code,
    ):
        valid = False
    if not valid:
        problems.add(
            code,
            location,
            "provider source must equal its immutable first-add candidate Git blob",
        )
    return valid


def _validate_runtime_harness_source(
    source: Any,
    product_candidate: Any,
    policy: Mapping[str, Any],
    repo_root: Path,
    problems: Problems,
    location: str,
) -> bool:
    return _validate_runtime_source_binding(
        source,
        TRUSTED_PROVIDER_HARNESS_PATH,
        policy.get("sourceSha256"),
        product_candidate,
        repo_root,
        problems,
        location,
        "PROVIDER_RUNTIME_HARNESS_SOURCE",
    )


def _validate_runtime_checkout(
    checkout: Any,
    gate_id: str,
    candidate: Any,
    succeeded: bool,
    repo_root: Path,
    problems: Problems,
    location: str,
    controlled_bundle_cache: dict[
        tuple[str, str],
        tuple[set[str], dict[str, str], dict[str, str]] | None,
    ],
) -> bool:
    if not isinstance(checkout, Mapping):
        problems.add(
            "PROVIDER_RUNTIME_CHECKOUT",
            location,
            "runtime event requires checkout identity",
        )
        return False
    before = checkout.get("before")
    after = checkout.get("after")
    projected = dict(checkout)
    projected["after"] = before
    valid = _validate_product_checkout_identity(
        projected,
        gate_id,
        candidate,
        repo_root,
        problems,
        location,
        controlled_bundle_cache,
    )
    snapshot_keys = {
        "headCommit",
        "treeObjectId",
        "gitStatusPorcelainV1",
        "trackedStatus",
    }
    if succeeded:
        after_valid = after == before
    else:
        after_valid = (
            isinstance(after, Mapping)
            and set(after) == snapshot_keys
            and after.get("trackedStatus")
            in {"Clean", "Dirty", "Unavailable"}
            and after.get("gitStatusPorcelainV1")
            in {"", "<redacted-dirty>", "<unavailable>"}
        )
    if not after_valid:
        problems.add(
            "PROVIDER_RUNTIME_CHECKOUT",
            location,
            "runtime checkout after-state is incompatible with its outcome",
        )
        valid = False
    return valid


def _validate_provider_runtime_journal(
    goal: Mapping[str, Any],
    gate_by_id: Mapping[str, Document],
    entries: Sequence[Mapping[str, Any]],
    reservation_by_id: Mapping[str, Mapping[str, Any]],
    completion_by_reservation: Mapping[str, Mapping[str, Any]],
    complete: bool,
    repo_root: Path | None,
    problems: Problems,
    *,
    candidate_root: Path | None = None,
) -> tuple[list[Mapping[str, Any]], dict[str, Mapping[str, Any]]]:
    if repo_root is None:
        return [], {}
    source_root = candidate_root or repo_root
    runtime_document = read_document(
        repo_root,
        repo_root / PROVIDER_RUNTIME_JOURNAL_PATH,
        problems,
    )
    if runtime_document is None or not isinstance(
        runtime_document.data, Mapping
    ):
        problems.add(
            "PROVIDER_RUNTIME_JOURNAL_MISSING",
            PROVIDER_RUNTIME_JOURNAL_PATH,
            "canonical provider runtime journal is required",
        )
        return [], {}
    journal = runtime_document.data
    runtime_events = journal.get("events")
    expected_journal_keys = {
        "schemaVersion",
        "journalVersion",
        "goalId",
        "eventCount",
        "lastEventSha256",
        "events",
    }
    if (
        set(journal) != expected_journal_keys
        or journal.get("schemaVersion") != SCHEMA_VERSION
        or journal.get("journalVersion")
        != PROVIDER_RUNTIME_JOURNAL_VERSION
        or journal.get("goalId") != GOAL_ID
        or not isinstance(runtime_events, list)
        or journal.get("eventCount")
        != (len(runtime_events) if isinstance(runtime_events, list) else None)
        or runtime_document.sha256
        != hashlib.sha256(_json_bytes(journal)).hexdigest()
    ):
        problems.add(
            "PROVIDER_RUNTIME_JOURNAL_SHAPE",
            runtime_document.relative_path,
            "runtime journal must use the exact canonical frozen append-only envelope",
        )
    if not isinstance(runtime_events, list):
        return [], {}
    provider = _provider_summary(goal)
    if (
        provider.get("runtimeJournalPath") != PROVIDER_RUNTIME_JOURNAL_PATH
        or provider.get("runtimeJournalSha256") != runtime_document.sha256
        or provider.get("runtimeEventCount") != len(runtime_events)
        or provider.get("lastRuntimeEventSha256")
        != journal.get("lastEventSha256")
    ):
        problems.add(
            "PROVIDER_RUNTIME_GOAL_BINDING",
            runtime_document.relative_path,
            "central Goal must bind the current provider runtime journal",
        )

    policy = _trusted_provider_policy_from_repo(source_root, problems)
    policy_sha = hashlib.sha256(_json_bytes(policy)).hexdigest()
    expected_event_keys = {
        "sequence",
        "eventType",
        "reservationId",
        "reservationSequence",
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
        "descriptor",
        "observation",
        "observedRequest",
        "checkoutIdentity",
        "startedAt",
        "finishedAt",
        "exitCode",
        "outcome",
        "previousEventSha256",
        "eventSha256",
    }
    runtime_by_reservation: dict[str, Mapping[str, Any]] = {}
    previous_hash: str | None = None
    previous_time: datetime | None = None
    previous_batch_id: str | None = None
    batches: list[list[Mapping[str, Any]]] = []
    seen_batch_ids: set[str] = set()
    for index, event in enumerate(runtime_events, start=1):
        location = f"{PROVIDER_RUNTIME_JOURNAL_PATH}#/events/{index - 1}"
        if not isinstance(event, Mapping):
            problems.add(
                "PROVIDER_RUNTIME_EVENT_SHAPE",
                location,
                "runtime event must be an object",
            )
            continue
        if set(event) != expected_event_keys:
            problems.add(
                "PROVIDER_RUNTIME_EVENT_SHAPE",
                location,
                "runtime event must use the exact observed-request envelope",
            )
        event_hash = event.get("eventSha256")
        if (
            event.get("sequence") != index
            or event.get("eventType") != "ObservedProviderRequest"
            or event.get("previousEventSha256") != previous_hash
            or not isinstance(event_hash, str)
            or HEX_SHA256.fullmatch(event_hash) is None
            or event_hash != provider_runtime_event_sha256(event)
        ):
            problems.add(
                "PROVIDER_RUNTIME_EVENT_CHAIN",
                location,
                "runtime event sequence/hash chain is invalid",
            )
        previous_hash = event_hash if isinstance(event_hash, str) else None
        reservation_id = event.get("reservationId")
        reservation = (
            reservation_by_id.get(reservation_id)
            if isinstance(reservation_id, str)
            else None
        )
        if (
            not isinstance(reservation_id, str)
            or reservation is None
            or reservation_id in runtime_by_reservation
            or event.get("reservationSequence") != reservation.get("sequence")
            or any(
                event.get(field) != reservation.get(field)
                for field in (
                    "gateId",
                    "phase",
                    "productCandidate",
                    "attemptId",
                    "runId",
                )
            )
        ):
            problems.add(
                "PROVIDER_RUNTIME_RESERVATION",
                location,
                "runtime event must uniquely bind its exact reservation",
            )
        else:
            runtime_by_reservation[reservation_id] = event
        started_at = _timestamp(event.get("startedAt"))
        finished_at = _timestamp(event.get("finishedAt"))
        reserved_at = _timestamp(
            reservation.get("reservedAt")
            if isinstance(reservation, Mapping)
            else None
        )
        completion = completion_by_reservation.get(str(reservation_id), {})
        completed_at = _timestamp(completion.get("completedAt"))
        gate_document = gate_by_id.get(str(event.get("gateId")))
        gate = _mapping(
            gate_document.data if gate_document is not None else {}
        )
        gate_started = _timestamp(gate.get("startedAt"))
        gate_finished = _timestamp(gate.get("finishedAt"))
        expected_outcome = completion.get("outcome")
        if (
            started_at is None
            or finished_at is None
            or reserved_at is None
            or completed_at is None
            or gate_started is None
            or gate_finished is None
            or not (
                gate_started
                <= reserved_at
                <= started_at
                < finished_at
                <= completed_at
                <= gate_finished
            )
            or (
                previous_time is not None
                and event.get("batchId") != previous_batch_id
                and started_at < previous_time
            )
            or event.get("outcome") != expected_outcome
            or (event.get("outcome") == "Succeeded")
            != (event.get("exitCode") == 0)
        ):
            problems.add(
                "PROVIDER_RUNTIME_OUTCOME_TIME",
                location,
                "runtime execution must occur inside reservation/completion and match its outcome",
            )
        if (
            finished_at is not None
            and event.get("batchId") != previous_batch_id
        ):
            previous_time = finished_at
        previous_batch_id = str(event.get("batchId"))
        request = event.get("observedRequest")
        if request is not None and (
            not isinstance(request, Mapping)
            or set(request)
            != {
                "reservationId",
                "reservationSequence",
                "requestOrdinal",
                "requestSha256",
                "status",
            }
            or request.get("reservationId") != reservation_id
            or request.get("reservationSequence")
            != event.get("reservationSequence")
            or not _is_int(request.get("requestOrdinal"))
            or request.get("requestOrdinal") < 1
            or not isinstance(request.get("requestSha256"), str)
            or HEX_SHA256.fullmatch(str(request.get("requestSha256"))) is None
            or request.get("status") not in {"Succeeded", "Failed"}
        ):
            problems.add(
                "PROVIDER_RUNTIME_OBSERVED_REQUEST",
                location,
                "observed request must bind its exact reservation and ordinal",
            )
        observation = _mapping(event.get("observation"))
        if event.get("outcome") == "Succeeded" and (
            request is None
            or _mapping(request).get("status") != "Succeeded"
            or observation.get("status") != "Accepted"
        ):
            problems.add(
                "PROVIDER_RUNTIME_SUCCESS",
                location,
                "successful runtime event requires accepted observed request evidence",
            )
        batch_id = event.get("batchId")
        if not batches or batches[-1][0].get("batchId") != batch_id:
            if not isinstance(batch_id, str) or batch_id in seen_batch_ids:
                problems.add(
                    "PROVIDER_RUNTIME_BATCH_ORDER",
                    location,
                    "runtime batch IDs must be unique contiguous groups",
                )
            batches.append([])
            if isinstance(batch_id, str):
                seen_batch_ids.add(batch_id)
        batches[-1].append(event)
    if journal.get("lastEventSha256") != previous_hash:
        problems.add(
            "PROVIDER_RUNTIME_JOURNAL_LAST_HASH",
            runtime_document.relative_path,
            "lastEventSha256 must equal the final runtime event hash",
        )

    resource_batch_ordinals: Counter[tuple[str, str]] = Counter()
    checkout_cache: set[bytes] = set()
    source_cache: set[tuple[str, str]] = set()
    controlled_bundle_cache: dict[
        tuple[str, str],
        tuple[set[str], dict[str, str], dict[str, str]] | None,
    ] = {}
    for batch in batches:
        if not batch:
            continue
        first = batch[0]
        batch_id = first.get("batchId")
        phase = first.get("phase")
        gate_id = str(first.get("gateId"))
        attempt_id = str(first.get("attemptId"))
        batch_size = PROVIDER_BATCH_SIZES.get(str(phase))
        first_reservation_sequence = first.get("reservationSequence")
        expected_reservation_sequences = (
            list(
                range(
                    first_reservation_sequence,
                    first_reservation_sequence + batch_size,
                )
            )
            if _is_int(first_reservation_sequence)
            and batch_size is not None
            else None
        )
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
            "descriptor",
            "observation",
            "checkoutIdentity",
            "startedAt",
            "finishedAt",
            "exitCode",
            "outcome",
        )
        if (
            batch_size is None
            or len(batch) != batch_size
            or first.get("batchSize") != batch_size
            or not isinstance(batch_id, str)
            or (
                re.fullmatch(
                    rf"batch-{first_reservation_sequence}-[0-9a-f]{{24}}",
                    batch_id,
                )
                is None
                and not (
                    first.get("outcome") == "Failed"
                    and re.fullmatch(
                        rf"failed-{first_reservation_sequence}-[0-9a-f]{{16}}",
                        batch_id,
                    )
                    is not None
                )
            )
            or any(
                any(event.get(field) != first.get(field) for field in common_fields)
                for event in batch[1:]
            )
            or [event.get("reservationSequence") for event in batch]
            != expected_reservation_sequences
        ):
            problems.add(
                "PROVIDER_RUNTIME_BATCH_BINDING",
                PROVIDER_RUNTIME_JOURNAL_PATH,
                "runtime batch must be one exact contiguous frozen phase batch",
            )
        for ordinal, event in enumerate(batch, start=1):
            request = event.get("observedRequest")
            if request is not None and _mapping(request).get(
                "requestOrdinal"
            ) != ordinal:
                problems.add(
                    "PROVIDER_RUNTIME_BATCH_ORDER",
                    PROVIDER_RUNTIME_JOURNAL_PATH,
                    "observed request ordinals must match batch order",
                )

        command_policy = first.get("commandPolicy")
        descriptor_binding = _mapping(first.get("descriptor"))
        observation_binding = _mapping(first.get("observation"))
        artifact_root = GOAL_ARTIFACT_DIR_BY_GROUP.get(
            (
                _mapping(
                    gate_by_id.get(gate_id).data
                    if gate_by_id.get(gate_id) is not None
                    else {}
                ).get("week"),
                _mapping(
                    gate_by_id.get(gate_id).data
                    if gate_by_id.get(gate_id) is not None
                    else {}
                ).get("lane"),
            )
        )
        expected_descriptor_path = (
            f"{artifact_root}/provider-runtime/{gate_id}/{batch_id}.descriptor.json"
            if isinstance(artifact_root, str)
            else None
        )
        expected_observation_path = (
            f"{artifact_root}/provider-runtime/{gate_id}/{batch_id}.observation.json"
            if isinstance(artifact_root, str)
            else None
        )
        provider_values = {
            "{sourcePath}": TRUSTED_PROVIDER_HARNESS_PATH,
            "{descriptorPath}": expected_descriptor_path,
            "{observationPath}": expected_observation_path,
        }
        expected_arguments = [
            provider_values.get(argument, argument)
            for argument in trusted_executor._PROVIDER_TEMPLATE_ARGUMENTS
        ]
        expected_argv_sha = hashlib.sha256(
            _json_bytes(
                {
                    "executableRole": ISOLATED_PYTHON_EXECUTABLE_ROLE,
                    "arguments": expected_arguments,
                }
            )
        ).hexdigest()
        succeeded_batch = first.get("outcome") == "Succeeded"
        if (
            (
                command_policy is not None
                and (
                    not isinstance(command_policy, Mapping)
                    or set(command_policy) != {"policyId", "sha256"}
                    or command_policy
                    != {
                        "policyId": TRUSTED_PROVIDER_POLICY_ID,
                        "sha256": policy_sha,
                    }
                )
            )
            or (
                first.get("argvSha256") is not None
                and first.get("argvSha256") != expected_argv_sha
            )
            or (
                first.get("descriptor") is not None
                and descriptor_binding.get("path")
                != expected_descriptor_path
            )
            or (
                first.get("observation") is not None
                and observation_binding.get("path")
                != expected_observation_path
            )
            or (
                succeeded_batch
                and any(
                    first.get(field) is None
                    for field in (
                        "commandPolicy",
                        "argvSha256",
                        "harnessSource",
                        "descriptor",
                        "observation",
                        "checkoutIdentity",
                        "observedRequest",
                    )
                )
            )
        ):
            problems.add(
                "PROVIDER_RUNTIME_POLICY_BINDING",
                PROVIDER_RUNTIME_JOURNAL_PATH,
                "runtime batch must bind the exact frozen policy and descriptor argv",
            )

        candidate = first.get("productCandidate")
        harness_source = first.get("harnessSource")
        source_key = (str(candidate), _json_bytes(harness_source).hex())
        if harness_source is not None and source_key not in source_cache:
            _validate_runtime_harness_source(
                first.get("harnessSource"),
                candidate,
                policy,
                source_root,
                problems,
                PROVIDER_RUNTIME_JOURNAL_PATH,
            )
            source_cache.add(source_key)
        checkout_identity = first.get("checkoutIdentity")
        checkout_key = _json_bytes(checkout_identity)
        if checkout_identity is not None and checkout_key not in checkout_cache:
            _validate_runtime_checkout(
                first.get("checkoutIdentity"),
                gate_id,
                candidate,
                first.get("outcome") == "Succeeded",
                source_root,
                problems,
                PROVIDER_RUNTIME_JOURNAL_PATH,
                controlled_bundle_cache,
            )
            checkout_cache.add(checkout_key)

        descriptor_present = first.get("descriptor") is not None
        descriptor_path = (
            repo_root / str(expected_descriptor_path)
            if descriptor_present and expected_descriptor_path is not None
            else None
        )
        try:
            descriptor_raw = (
                descriptor_path.read_bytes() if descriptor_path is not None else None
            )
        except OSError:
            descriptor_raw = None
        descriptor_document = (
            read_document(repo_root, descriptor_path, problems)
            if descriptor_path is not None and descriptor_path.is_file()
            else None
        )
        descriptor = (
            _mapping(descriptor_document.data)
            if descriptor_document is not None
            else {}
        )
        expected_descriptor_keys = {
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
            "scenarioPath",
            "scenarioSource",
            "controlledWriteTombstone",
            "controlledWritePreauthorization",
            "reservations",
        }
        attempt_entries = [
            entry
            for entry in entries
            if entry.get("attemptId") == attempt_id
            and entry.get("gateId") == gate_id
        ]
        ordinal_by_reservation = {
            entry.get("reservationId"): ordinal
            for ordinal, entry in enumerate(attempt_entries, start=1)
        }
        expected_reservations = [
            {
                "reservationId": event.get("reservationId"),
                "reservationSequence": event.get("reservationSequence"),
                "turnOrdinal": ordinal_by_reservation.get(
                    event.get("reservationId")
                ),
            }
            for event in batch
        ]
        profile_key = (gate_id, attempt_id)
        expected_profile = None
        if phase == "provider-resource":
            resource_batch_ordinals[profile_key] += 1
            expected_profile = resource_batch_ordinals[profile_key]
        expected_controlled: Any = None
        expected_preauthorization: Any = None
        controlled_gate = CONTROLLED_DESCENDANT_GATES.get(gate_id)
        if controlled_gate is not None:
            controlled_key = (controlled_gate, str(candidate))
            if controlled_key not in controlled_bundle_cache:
                controlled_bundle_cache[controlled_key] = (
                    _validate_controlled_authorization_bundle(
                        source_root,
                        controlled_gate,
                        str(candidate),
                        problems,
                        PROVIDER_RUNTIME_JOURNAL_PATH,
                    )
                )
            controlled_bundle = controlled_bundle_cache[controlled_key]
            if controlled_bundle is not None:
                expected_controlled = controlled_bundle[1]
                expected_preauthorization = controlled_bundle[2]
        scenario_source_valid = True
        if descriptor_present:
            expected_scenario_path = PROVIDER_SCENARIO_PATHS.get(str(phase))
            scenario_policy_binding = _mapping(
                _mapping(policy.get("scenarioSources")).get(str(phase))
            )
            scenario_source = descriptor.get("scenarioSource")
            scenario_source_key = (
                str(candidate),
                _json_bytes(scenario_source).hex(),
            )
            if (
                expected_scenario_path is None
                or scenario_policy_binding.get("path")
                != expected_scenario_path
            ):
                scenario_source_valid = False
            elif scenario_source_key not in source_cache:
                scenario_source_valid = _validate_runtime_source_binding(
                    scenario_source,
                    expected_scenario_path,
                    scenario_policy_binding.get("sha256"),
                    candidate,
                    source_root,
                    problems,
                    PROVIDER_RUNTIME_JOURNAL_PATH,
                    "PROVIDER_RUNTIME_SCENARIO_SOURCE",
                )
                source_cache.add(scenario_source_key)
        if descriptor_present and (
            descriptor_raw is None
            or descriptor_binding.get("sha256")
            != hashlib.sha256(descriptor_raw or b"").hexdigest()
            or set(descriptor) != expected_descriptor_keys
            or descriptor.get("schemaVersion") != SCHEMA_VERSION
            or descriptor.get("protocol") != PROVIDER_DESCRIPTOR_PROTOCOL
            or descriptor.get("goalId") != GOAL_ID
            or descriptor.get("batchId") != batch_id
            or descriptor.get("gateId") != gate_id
            or descriptor.get("phase") != phase
            or descriptor.get("profileOrdinal") != expected_profile
            or descriptor.get("productCandidate") != candidate
            or descriptor.get("attemptId") != attempt_id
            or descriptor.get("runId") != first.get("runId")
            or descriptor.get("artifactRoot") != artifact_root
            or descriptor.get("scenarioPath")
            != PROVIDER_SCENARIO_PATHS.get(str(phase))
            or not scenario_source_valid
            or descriptor.get("controlledWriteTombstone")
            != expected_controlled
            or descriptor.get("controlledWritePreauthorization")
            != expected_preauthorization
            or descriptor.get("reservations") != expected_reservations
        ):
            problems.add(
                "PROVIDER_RUNTIME_DESCRIPTOR",
                PROVIDER_RUNTIME_JOURNAL_PATH,
                "runtime descriptor must exactly bind its batch reservations and scenario",
            )
        if descriptor_present:
            package_binding = _mapping(
                descriptor.get("packageIdentityEvidence")
            )
            package_path = _safe_relative_evidence_path(
                repo_root,
                package_binding.get("path"),
                problems,
                f"{PROVIDER_RUNTIME_JOURNAL_PATH}#/packageIdentityEvidence",
            )
            try:
                package_raw = (
                    package_path.read_bytes()
                    if package_path is not None
                    else None
                )
            except OSError:
                package_raw = None
            if (
                set(package_binding) != {"path", "sha256"}
                or package_raw is None
                or package_binding.get("sha256")
                != hashlib.sha256(package_raw or b"").hexdigest()
            ):
                problems.add(
                    "PROVIDER_RUNTIME_PACKAGE_IDENTITY",
                    PROVIDER_RUNTIME_JOURNAL_PATH,
                    "runtime descriptor package identity evidence must be raw-hash-bound",
                )

        observation_present = first.get("observation") is not None
        observation_status = observation_binding.get("status")
        observation_path = (
            repo_root / str(expected_observation_path)
            if observation_present and expected_observation_path is not None
            else None
        )
        try:
            observation_raw = (
                observation_path.read_bytes()
                if observation_path is not None and observation_path.is_file()
                else None
            )
        except OSError:
            observation_raw = None
        observation_document = (
            read_document(repo_root, observation_path, problems)
            if observation_raw is not None and observation_path is not None
            else None
        )
        observation = (
            _mapping(observation_document.data)
            if observation_document is not None
            else {}
        )
        expected_observation = {
            "schemaVersion": SCHEMA_VERSION,
            "protocol": PROVIDER_OBSERVED_REQUEST_PROTOCOL,
            "batchId": batch_id,
            "gateId": gate_id,
            "phase": phase,
            "productCandidate": candidate,
            "attemptId": attempt_id,
            "runId": first.get("runId"),
            "packageIdentityEvidence": descriptor.get(
                "packageIdentityEvidence"
            ),
            "requests": [event.get("observedRequest") for event in batch],
        }
        if observation_present and (
            set(observation_binding) != {"path", "sha256", "status"}
            or observation_status not in {"Accepted", "Rejected", "Missing"}
            or (
                observation_raw is None
                and not (
                    observation_status == "Missing"
                    and observation_binding.get("sha256") is None
                )
            )
            or (
                observation_raw is not None
                and observation_binding.get("sha256")
                != hashlib.sha256(observation_raw).hexdigest()
            )
            or (
                observation_status == "Accepted"
                and observation != expected_observation
            )
        ):
            problems.add(
                "PROVIDER_RUNTIME_OBSERVATION",
                PROVIDER_RUNTIME_JOURNAL_PATH,
                "runtime observation must exactly bind accepted request evidence or an explicit failure",
            )

    missing_runtime = [
        entry
        for entry in entries
        if str(entry.get("reservationId")) not in runtime_by_reservation
        and (
            complete
            or _mapping(
                gate_by_id.get(str(entry.get("gateId"))).data
                if gate_by_id.get(str(entry.get("gateId"))) is not None
                else {}
            ).get("status")
            == "Passed"
        )
    ]
    if missing_runtime or len(runtime_by_reservation) > len(entries):
        problems.add(
            "PROVIDER_RUNTIME_CARDINALITY",
            runtime_document.relative_path,
            "every reservation of a Passed/Complete provider Gate requires one runtime event",
        )
    return list(runtime_events), runtime_by_reservation


def _validate_receipt_ledger_ranges(
    gate_by_id: Mapping[str, Document],
    successful_entries_by_gate: Mapping[str, list[Mapping[str, Any]]],
    completion_by_reservation: Mapping[str, Mapping[str, Any]],
    runtime_by_reservation: Mapping[str, Mapping[str, Any]],
    repo_root: Path,
    problems: Problems,
) -> None:
    for gate_id, requirement in PROVIDER_GATE_REQUIREMENTS.items():
        document = gate_by_id.get(gate_id)
        if document is None:
            continue
        gate = _mapping(document.data)
        authorization = _mapping(gate.get("authorization"))
        successful_attempt_id = authorization.get("providerSuccessfulAttemptId")
        candidate = _mapping(gate.get("identity")).get("productCandidate")
        gate_entries = successful_entries_by_gate.get(gate_id, [])
        evidence_by_name = {
            Path(str(evidence.get("path"))).name: evidence
            for evidence in _list(gate.get("evidence"))
            if isinstance(evidence, Mapping)
        }
        for receipt_name in sorted(set(requirement.get("receipts", set()))):
            phase_slice = _receipt_phase_slice(gate_id, receipt_name)
            evidence = evidence_by_name.get(receipt_name)
            if phase_slice is None or evidence is None:
                continue
            receipt_path = _safe_relative_evidence_path(
                repo_root,
                evidence.get("path"),
                problems,
                f"{document.relative_path}#/provider-ledger-receipt/{receipt_name}",
            )
            if receipt_path is None or not receipt_path.is_file():
                continue
            receipt_document = read_document(repo_root, receipt_path, problems)
            if receipt_document is None or not isinstance(receipt_document.data, Mapping):
                continue
            receipt = receipt_document.data
            phase, relative_start, relative_end = phase_slice
            ledger_slice = gate_entries[relative_start : relative_end + 1]
            expected_start = (
                ledger_slice[0].get("sequence") if ledger_slice else None
            )
            expected_end = (
                ledger_slice[-1].get("sequence") if ledger_slice else None
            )
            if (
                receipt.get("sequenceStart") != expected_start
                or receipt.get("sequenceEnd") != expected_end
                or receipt.get("reservationIds")
                != [entry.get("reservationId") for entry in ledger_slice]
            ):
                problems.add(
                    "PROVIDER_RECEIPT_SEQUENCE",
                    receipt_document.relative_path,
                    "receipt sequence range does not match its Gate ledger range",
                )
            if receipt.get("productCandidate") != candidate:
                problems.add(
                    "PROVIDER_RECEIPT_CANDIDATE",
                    receipt_document.relative_path,
                    "receipt is not bound to the Gate product candidate",
                )
            if receipt.get("gateId") != gate_id or receipt.get("phase") != phase:
                problems.add(
                    "PROVIDER_RECEIPT_BINDING",
                    receipt_document.relative_path,
                    "receipt Gate/phase binding does not match its ledger range",
                )
            if (
                not isinstance(successful_attempt_id, str)
                or receipt.get("attemptId") != successful_attempt_id
                or any(
                    entry.get("attemptId") != successful_attempt_id
                    for entry in ledger_slice
                )
            ):
                problems.add(
                    "PROVIDER_RECEIPT_ATTEMPT",
                    receipt_document.relative_path,
                    "receipt does not bind the final successful provider attempt",
                )
            runtime_slice = [
                runtime_by_reservation.get(str(entry.get("reservationId")))
                for entry in ledger_slice
            ]
            if (
                len(runtime_slice) != len(ledger_slice)
                or any(not isinstance(event, Mapping) for event in runtime_slice)
                or any(
                    event.get("outcome") != "Succeeded"
                    or event.get("gateId") != gate_id
                    or event.get("phase") != phase
                    or event.get("productCandidate") != candidate
                    or event.get("attemptId") != successful_attempt_id
                    or event.get("runId") != receipt.get(
                        "continuousRunId"
                        if receipt_name.startswith("provider-resource-profile-")
                        else "runId"
                    )
                    for event in runtime_slice
                    if isinstance(event, Mapping)
                )
            ):
                problems.add(
                    "PROVIDER_RECEIPT_RUNTIME_BINDING",
                    receipt_document.relative_path,
                    "receipt reservations must bind their successful observed runtime events",
                )
            if receipt_name == "controlled-write.json":
                reservation = ledger_slice[0] if ledger_slice else {}
                completion = completion_by_reservation.get(
                    str(reservation.get("reservationId")), {}
                )
                reserved_at = _timestamp(reservation.get("reservedAt"))
                completed_at = _timestamp(completion.get("completedAt"))
                write_started_at = _timestamp(receipt.get("writeStartedAt"))
                if (
                    reserved_at is None
                    or completed_at is None
                    or write_started_at is None
                    or not (reserved_at <= write_started_at < completed_at)
                ):
                    problems.add(
                        "PROVIDER_RECEIPT_TIME",
                        receipt_document.relative_path,
                        "controlled-write time must be inside its reserved/completed turn",
                    )
            run_field = (
                "continuousRunId"
                if receipt_name.startswith("provider-resource-profile-")
                else "runId"
            )
            receipt_run_id = receipt.get(run_field)
            if (
                not isinstance(receipt_run_id, str)
                or not receipt_run_id
                or len(ledger_slice) != relative_end - relative_start + 1
                or any(entry.get("runId") != receipt_run_id for entry in ledger_slice)
            ):
                problems.add(
                    "PROVIDER_RECEIPT_RUN",
                    receipt_document.relative_path,
                    "receipt runId does not match every ledger entry in its range",
                )


def _validate_handoff_runtime_snapshots(
    handoff_documents: Sequence[Document],
    runtime_events: Sequence[Mapping[str, Any]],
    repo_root: Path,
    problems: Problems,
) -> None:
    """Bind every immutable handoff snapshot to its runtime-journal prefix."""

    for handoff_document in handoff_documents:
        handoff = _mapping(handoff_document.data)
        binding = _mapping(handoff.get("goalControlBinding"))
        snapshot_relative = binding.get("path")
        if not isinstance(snapshot_relative, str):
            continue
        snapshot_path = _safe_repo_path(
            repo_root,
            snapshot_relative,
            problems,
            f"{handoff_document.relative_path}#/goalControlBinding/path",
        )
        if snapshot_path is None or not snapshot_path.is_file():
            continue
        snapshot_document = read_document(repo_root, snapshot_path, problems)
        if snapshot_document is None:
            continue
        snapshot = _mapping(snapshot_document.data)
        provider = _mapping(
            _mapping(snapshot.get("authorization")).get("provider")
        )
        runtime_count = provider.get("runtimeEventCount")
        provider_sequence = provider.get("ledgerSequence")
        expected_last = (
            runtime_events[runtime_count - 1].get("eventSha256")
            if _is_int(runtime_count)
            and 0 < runtime_count <= len(runtime_events)
            else None
        )
        prefix_document = (
            {
                "schemaVersion": SCHEMA_VERSION,
                "journalVersion": PROVIDER_RUNTIME_JOURNAL_VERSION,
                "goalId": GOAL_ID,
                "eventCount": runtime_count,
                "lastEventSha256": expected_last,
                "events": list(runtime_events[:runtime_count]),
            }
            if _is_int(runtime_count)
            and 0 <= runtime_count <= len(runtime_events)
            else None
        )
        expected_raw_sha = (
            hashlib.sha256(_json_bytes(prefix_document)).hexdigest()
            if prefix_document is not None
            else None
        )
        if (
            provider.get("runtimeJournalPath")
            != PROVIDER_RUNTIME_JOURNAL_PATH
            or not _is_int(runtime_count)
            or runtime_count < 0
            or runtime_count > len(runtime_events)
            or runtime_count != provider_sequence
            or runtime_count != binding.get("providerLedgerSequence")
            or provider.get("lastRuntimeEventSha256") != expected_last
            or provider.get("runtimeJournalSha256") != expected_raw_sha
        ):
            problems.add(
                "HANDOFF_RUNTIME_SNAPSHOT",
                snapshot_document.relative_path,
                "handoff snapshot must bind the exact canonical runtime-journal prefix at its provider frontier",
            )


def _provider_attempt_event_prefix_count(
    attempt_events: Sequence[Mapping[str, Any]],
    reservation_by_id: Mapping[str, Mapping[str, Any]],
    provider_sequence_after: int,
    location: str,
    problems: Problems,
) -> int:
    """Return the only event-prefix boundary closed by a reservation prefix."""

    prefix_count = 0
    outside_prefix_seen = False
    for event in attempt_events:
        event_type = event.get("eventType")
        if event_type == "TurnCompleted":
            reservation = reservation_by_id.get(str(event.get("reservationId")))
            reservation_end = (
                reservation.get("sequence")
                if isinstance(reservation, Mapping)
                else None
            )
        elif event_type == "AttemptFinished":
            reservation_end = event.get("reservationSequenceEnd")
        else:
            reservation_end = None
        inside_prefix = (
            _is_int(reservation_end)
            and reservation_end <= provider_sequence_after
        )
        if inside_prefix and outside_prefix_seen:
            problems.add(
                "PROVIDER_LEDGER_BINDING_EVENT_ORDER",
                location,
                "events closed by a Gate reservation prefix must themselves form a prefix",
            )
        if not outside_prefix_seen and inside_prefix:
            prefix_count += 1
        else:
            outside_prefix_seen = True
    return prefix_count


def _provider_runtime_prefix_count(
    runtime_events: Sequence[Mapping[str, Any]],
    provider_sequence_after: int,
    location: str,
    problems: Problems,
) -> int:
    prefix_count = 0
    outside_prefix_seen = False
    for event in runtime_events:
        reservation_sequence = event.get("reservationSequence")
        inside_prefix = (
            _is_int(reservation_sequence)
            and reservation_sequence <= provider_sequence_after
        )
        if inside_prefix and outside_prefix_seen:
            problems.add(
                "PROVIDER_RUNTIME_PREFIX_ORDER",
                location,
                "runtime events closed by a Gate frontier must form a prefix",
            )
        if not outside_prefix_seen and inside_prefix:
            prefix_count += 1
        else:
            outside_prefix_seen = True
    return prefix_count


def _validate_provider_ledger_bindings(
    gate_by_id: Mapping[str, Document],
    entries: Sequence[Mapping[str, Any]],
    attempt_events: Sequence[Mapping[str, Any]],
    runtime_events: Sequence[Mapping[str, Any]],
    reservation_by_id: Mapping[str, Mapping[str, Any]],
    canonical_frontiers: Mapping[str, tuple[int, int]],
    repo_root: Path | None,
    problems: Problems,
) -> None:
    """Validate Gate-local projections of the append-only provider ledger."""

    expected_wrapper_keys = {
        "schemaVersion",
        "goalId",
        "gateId",
        "productCandidate",
        "status",
        "ledgerPath",
        "providerSequenceBefore",
        "providerSequenceAfter",
        "attemptEventCount",
        "lastAttemptEventSha256",
        "reservationPrefixSha256",
        "attemptEventPrefixSha256",
        "combinedPrefixSha256",
        "runtimeJournalPath",
        "runtimeEventCount",
        "lastRuntimeEventSha256",
        "runtimePrefixSha256",
    }
    for gate_id, document in gate_by_id.items():
        gate = _mapping(document.data)
        status = gate.get("status")
        authorization = _mapping(gate.get("authorization"))
        sequence_before, sequence_after = canonical_frontiers.get(
            gate_id, (-1, -1)
        )
        expected_event_count = _provider_attempt_event_prefix_count(
            attempt_events,
            reservation_by_id,
            sequence_after,
            document.relative_path,
            problems,
        )
        expected_last_event_hash = (
            attempt_events[expected_event_count - 1].get(
                "attemptEventSha256"
            )
            if expected_event_count
            else None
        )
        expected_reservation_hash = provider_reservation_prefix_sha256(
            entries,
            sequence_after,
        )
        expected_event_hash = provider_attempt_event_prefix_sha256(
            attempt_events,
            expected_event_count,
        )
        expected_combined_hash = provider_combined_prefix_sha256(
            entries,
            sequence_after,
            attempt_events,
            expected_event_count,
        )
        expected_runtime_count = _provider_runtime_prefix_count(
            runtime_events,
            sequence_after,
            document.relative_path,
            problems,
        )
        expected_last_runtime_hash = (
            runtime_events[expected_runtime_count - 1].get("eventSha256")
            if expected_runtime_count
            else None
        )
        expected_runtime_hash = provider_runtime_prefix_sha256(
            runtime_events, expected_runtime_count
        )
        declared_prefix_hash = authorization.get(
            "providerLedgerPrefixSha256"
        )
        if (
            not isinstance(declared_prefix_hash, str)
            or HEX_SHA256.fullmatch(declared_prefix_hash) is None
            or declared_prefix_hash != expected_combined_hash
        ):
            problems.add(
                "PROVIDER_LEDGER_BINDING_AUTHORIZATION",
                document.relative_path,
                "authorization.providerLedgerPrefixSha256 must equal the canonical Gate frontier",
            )
        matches = [
            evidence
            for evidence in _list(gate.get("evidence"))
            if isinstance(evidence, Mapping)
            and (_normalised_repo_parts(evidence.get("path")) or ("",))[-1]
            == PROVIDER_LEDGER_BINDING_BASENAME
        ]
        if gate_id not in PROVIDER_LEDGER_BINDING_GATE_IDS:
            if matches:
                problems.add(
                    "PROVIDER_LEDGER_BINDING_UNAUTHORIZED",
                    document.relative_path,
                    "provider ledger bindings are allowed only on the ten frozen Gates",
                )
            continue

        if status == "Passed" and len(matches) != 1:
            problems.add(
                "PROVIDER_LEDGER_BINDING_CARDINALITY",
                document.relative_path,
                "Passed ledger-binding Gate requires exactly one provider-ledger-binding.json",
            )
        elif len(matches) > 1:
            problems.add(
                "PROVIDER_LEDGER_BINDING_CARDINALITY",
                document.relative_path,
                "Gate may contain at most one provider-ledger-binding.json",
            )
        group_root = GOAL_ARTIFACT_DIR_BY_GROUP.get(
            (gate.get("week"), gate.get("lane"))
        )
        expected_binding_path = (
            f"{group_root}/provider-ledger-bindings/{gate_id}/"
            f"{PROVIDER_LEDGER_BINDING_BASENAME}"
            if isinstance(group_root, str)
            else None
        )
        if len(matches) == 1 and (
            matches[0].get("path") != expected_binding_path
        ):
            problems.add(
                "PROVIDER_LEDGER_BINDING_PATH",
                document.relative_path,
                "provider ledger binding must use its exact canonical Gate-local path",
            )
        if len(matches) != 1 or repo_root is None:
            continue

        evidence = matches[0]
        binding_path = _safe_relative_evidence_path(
            repo_root,
            evidence.get("path"),
            problems,
            f"{document.relative_path}#/provider-ledger-binding",
        )
        if binding_path is None or not binding_path.is_file():
            problems.add(
                "PROVIDER_LEDGER_BINDING_READ",
                document.relative_path,
                "provider ledger binding file is missing",
            )
            continue
        binding_document = read_document(repo_root, binding_path, problems)
        if binding_document is None or not isinstance(
            binding_document.data, Mapping
        ):
            problems.add(
                "PROVIDER_LEDGER_BINDING_READ",
                document.relative_path,
                "provider ledger binding must be unique-key UTF-8 JSON",
            )
            continue
        if evidence.get("kind") != "json" or (
            evidence.get("sha256") != binding_document.sha256
        ):
            problems.add(
                "PROVIDER_LEDGER_BINDING_EVIDENCE",
                binding_document.relative_path,
                "binding evidence kind/hash must match its raw JSON bytes",
            )

        binding = binding_document.data
        if set(binding) != expected_wrapper_keys:
            problems.add(
                "PROVIDER_LEDGER_BINDING_SHAPE",
                binding_document.relative_path,
                "provider ledger binding must use the exact frozen wrapper",
            )
        if (
            sequence_before < 0
            or sequence_after < sequence_before
            or sequence_after > len(entries)
        ):
            problems.add(
                "PROVIDER_LEDGER_BINDING_RANGE",
                binding_document.relative_path,
                "Gate authorization requires a valid canonical provider sequence range",
            )
            continue
        expected_identity = {
            "schemaVersion": SCHEMA_VERSION,
            "goalId": GOAL_ID,
            "gateId": gate_id,
            "productCandidate": _mapping(gate.get("identity")).get(
                "productCandidate"
            ),
            "status": status,
            "ledgerPath": LEDGER_PATH,
            "providerSequenceBefore": sequence_before,
            "providerSequenceAfter": sequence_after,
            "attemptEventCount": expected_event_count,
            "lastAttemptEventSha256": expected_last_event_hash,
            "reservationPrefixSha256": expected_reservation_hash,
            "attemptEventPrefixSha256": expected_event_hash,
            "combinedPrefixSha256": expected_combined_hash,
            "runtimeJournalPath": PROVIDER_RUNTIME_JOURNAL_PATH,
            "runtimeEventCount": expected_runtime_count,
            "lastRuntimeEventSha256": expected_last_runtime_hash,
            "runtimePrefixSha256": expected_runtime_hash,
        }
        if any(binding.get(key) != value for key, value in expected_identity.items()):
            problems.add(
                "PROVIDER_LEDGER_BINDING_PREFIX",
                binding_document.relative_path,
                "binding does not equal the canonical reservation/event prefixes at this Gate boundary",
            )
        if (
            binding.get("combinedPrefixSha256") != declared_prefix_hash
            or declared_prefix_hash != expected_combined_hash
        ):
            problems.add(
                "PROVIDER_LEDGER_BINDING_AUTHORIZATION",
                binding_document.relative_path,
                "Gate authorization must bind the recomputed combined provider prefix",
            )
        if gate_id in REAL_PROVIDER_GATE_IDS and status == "Passed":
            successful_attempt_id = authorization.get(
                "providerSuccessfulAttemptId"
            )
            if not any(
                event.get("eventType") == "AttemptFinished"
                and event.get("attemptId") == successful_attempt_id
                and event.get("outcome") == "Passed"
                for event in attempt_events[:expected_event_count]
            ):
                problems.add(
                    "PROVIDER_LEDGER_BINDING_SUCCESS_ATTEMPT",
                    binding_document.relative_path,
                    "provider Gate prefix must include its named successful AttemptFinished event",
                )
            if expected_runtime_count != sequence_after:
                problems.add(
                    "PROVIDER_LEDGER_BINDING_RUNTIME_CARDINALITY",
                    binding_document.relative_path,
                    "Passed provider Gate prefix requires one runtime event per reservation",
                )


def validate_provider_accounting(
    goal: Mapping[str, Any],
    gate_by_id: Mapping[str, Document],
    handoff_documents: Sequence[Document],
    ledger_document: Document | None,
    complete: bool,
    problems: Problems,
    repo_root: Path | None = None,
    control_root: Path | None = None,
) -> None:
    evidence_root = control_root or repo_root
    provider = _provider_summary(goal)
    used = provider.get("usedTurns")
    remaining = provider.get("remainingTurns")
    sequence = provider.get("ledgerSequence")
    if not all(_is_int(item) for item in (used, remaining, sequence)):
        problems.add("PROVIDER_GOAL", "goal-state.json#/authorization/provider", "used/remaining/sequence must be integers")
        return
    if (
        used < 0
        or used > PROVIDER_BUDGET
        or remaining < 0
        or remaining > PROVIDER_BUDGET
        or sequence != used
        or used + remaining != PROVIDER_BUDGET
    ):
        problems.add("PROVIDER_GOAL", "goal-state.json#/authorization/provider", "usedTurns + remainingTurns must equal 120")
    if provider.get("maxTurns") != PROVIDER_BUDGET:
        problems.add("PROVIDER_GOAL", "goal-state.json#/authorization/provider", "maxTurns must be 120")
    if provider.get("secretDisposition") != PROVIDER_SECRET_DISPOSITION:
        problems.add(
            "PROVIDER_SECRET_DISPOSITION",
            "goal-state.json#/authorization/provider/secretDisposition",
            "provider secrets may enter only the frozen counting gateway; cooperative child isolation must not be claimed",
        )
    if provider.get("ledgerPath") != LEDGER_PATH:
        problems.add(
            "PROVIDER_LEDGER_PATH",
            "goal-state.json#/authorization/provider/ledgerPath",
            "Goal provider ledgerPath must use the canonical Goal ledger",
        )
    if complete and used < 68:
        problems.add(
            "PROVIDER_COMPLETE_MINIMUM",
            "goal-state.json#/authorization/provider",
            "Complete Goal requires at least the frozen 68 provider turns",
        )

    events: list[tuple[int, int, int, int, str, str]] = []
    consumed_by_gate: Counter[str] = Counter()
    for gate_id, document in gate_by_id.items():
        authorization = _mapping(_mapping(document.data).get("authorization"))
        before = authorization.get("providerTurnsBefore")
        consumed = authorization.get("providerTurnsConsumed")
        after = authorization.get("providerTurnsAfter")
        seq_before = authorization.get("providerSequenceBefore")
        seq_after = authorization.get("providerSequenceAfter")
        if not all(_is_int(item) for item in (before, consumed, after, seq_before, seq_after)):
            continue
        if consumed > 0:
            events.append((seq_before, seq_after, before, after, gate_id, document.relative_path))
            consumed_by_gate[gate_id] += consumed
        ledger_path = authorization.get("providerLedgerPath")
        if ledger_path != LEDGER_PATH:
            problems.add("PROVIDER_LEDGER_PATH", document.relative_path, f"providerLedgerPath must be {LEDGER_PATH}")

    events.sort(key=lambda item: (item[0], item[1], item[4]))
    turn_cursor = 0
    sequence_cursor = 0
    for seq_before, seq_after, before, after, gate_id, location in events:
        if seq_before != sequence_cursor:
            problems.add("PROVIDER_LEDGER_GAP", location, f"{gate_id} sequence starts {seq_before}, expected {sequence_cursor}")
        if before != turn_cursor:
            problems.add("PROVIDER_TURN_GAP", location, f"{gate_id} turns start {before}, expected {turn_cursor}")
        sequence_cursor = seq_after
        turn_cursor = after
    if turn_cursor != used:
        problems.add("PROVIDER_GOAL_MISMATCH", "goal-state.json#/authorization/provider", f"usedTurns={used}, Gate ledger ends at {turn_cursor}")
    if sequence_cursor != sequence:
        problems.add("PROVIDER_GOAL_MISMATCH", "goal-state.json#/authorization/provider", f"ledgerSequence={sequence}, Gate ledger ends at {sequence_cursor}")

    if ledger_document is None:
        problems.add("PROVIDER_LEDGER_MISSING", LEDGER_PATH, "provider ledger file is required")
        return
    if ledger_document.relative_path != LEDGER_PATH:
        problems.add(
            "PROVIDER_LEDGER_PATH",
            ledger_document.relative_path,
            "provider ledger document is not at the canonical Goal path",
        )
    if provider.get("ledgerSha256") != ledger_document.sha256:
        problems.add("PROVIDER_LEDGER_HASH", LEDGER_PATH, "Goal ledgerSha256 does not match file bytes")
    ledger = ledger_document.data
    if not isinstance(ledger, Mapping):
        problems.add("PROVIDER_LEDGER_TYPE", LEDGER_PATH, "ledger must be an object")
        return
    if ledger.get("goalId") != GOAL_ID:
        problems.add("PROVIDER_LEDGER_GOAL", LEDGER_PATH, "ledger goalId mismatch")
    expected_ledger_keys = {
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
    if (
        set(ledger) != expected_ledger_keys
        or ledger.get("schemaVersion") != SCHEMA_VERSION
        or ledger.get("journalVersion") != PROVIDER_JOURNAL_VERSION
    ):
        problems.add(
            "PROVIDER_LEDGER_SHAPE",
            LEDGER_PATH,
            "ledger must use the exact frozen reservation/event envelope",
        )
    ledger_budget = ledger.get("maxTurns")
    ledger_used = ledger.get("usedTurns")
    ledger_remaining = ledger.get("remainingTurns")
    ledger_sequence = ledger.get("ledgerSequence")
    if ledger_budget != PROVIDER_BUDGET:
        problems.add("PROVIDER_LEDGER_SUMMARY", LEDGER_PATH, "ledger budget must be 120")
    if ledger_used != used or ledger_remaining != remaining or ledger_sequence != sequence:
        problems.add("PROVIDER_LEDGER_SUMMARY", LEDGER_PATH, "ledger summary does not match Goal provider summary")
    entries = ledger.get("entries")
    if not isinstance(entries, list):
        problems.add("PROVIDER_LEDGER_ENTRIES", LEDGER_PATH, "entries must be an array")
        return
    ledger_counts: Counter[str] = Counter()
    entries_by_gate: defaultdict[str, list[Mapping[str, Any]]] = defaultdict(list)
    previous_hash: str | None = None
    previous_reserved_at: datetime | None = None
    reservation_by_id: dict[str, Mapping[str, Any]] = {}
    reservation_ids: set[str] = set()
    expected_entry_keys = {
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
    for index, entry in enumerate(entries, start=1):
        entry_location = f"{LEDGER_PATH}#/entries/{index - 1}"
        if not isinstance(entry, Mapping):
            problems.add("PROVIDER_LEDGER_ENTRY", entry_location, "entry must be an object")
            continue
        if set(entry) != expected_entry_keys or entry.get("eventType") != "TurnReserved":
            problems.add(
                "PROVIDER_LEDGER_ENTRY_SHAPE",
                entry_location,
                "entry must be one exact TurnReserved envelope",
            )
        if entry.get("sequence") != index:
            problems.add("PROVIDER_LEDGER_SEQUENCE", entry_location, f"sequence must be {index}")
        reservation_id = entry.get("reservationId")
        if (
            not isinstance(reservation_id, str)
            or PROVIDER_RESERVATION_ID.fullmatch(reservation_id) is None
        ):
            problems.add(
                "PROVIDER_RESERVATION_ID",
                entry_location,
                "reservation requires a safe non-empty reservationId",
            )
        elif reservation_id in reservation_ids:
            problems.add(
                "PROVIDER_RESERVATION_DUPLICATE",
                entry_location,
                "reservationId must be globally unique",
            )
        else:
            reservation_ids.add(reservation_id)
            reservation_by_id[reservation_id] = entry
        gate_id = entry.get("gateId")
        if gate_id not in PROVIDER_GATE_REQUIREMENTS:
            problems.add(
                "PROVIDER_LEDGER_GATE",
                entry_location,
                "provider entry Gate is outside the four authorized provider Gates",
            )
        else:
            ledger_counts[str(gate_id)] += 1
            entries_by_gate[str(gate_id)].append(entry)
            gate_document = gate_by_id.get(str(gate_id))
            gate = _mapping(gate_document.data) if gate_document is not None else {}
            phase = entry.get("phase")
            allowed_phases = set(
                PROVIDER_GATE_REQUIREMENTS[str(gate_id)].get("scopes", set())
            )
            if not isinstance(phase, str) or phase not in allowed_phases:
                problems.add(
                    "PROVIDER_LEDGER_PHASE",
                    entry_location,
                    "entry phase is outside its Gate's frozen provider scopes",
                )
            expected_candidate = _mapping(gate.get("identity")).get("productCandidate")
            if (
                not isinstance(expected_candidate, str)
                or entry.get("productCandidate") != expected_candidate
            ):
                problems.add(
                    "PROVIDER_LEDGER_CANDIDATE",
                    entry_location,
                    "entry is not bound to the Gate product candidate",
                )
            if not isinstance(entry.get("runId"), str) or not entry.get("runId"):
                problems.add(
                    "PROVIDER_LEDGER_RUN",
                    entry_location,
                    "entry requires a non-empty runId",
                )
            elif PROVIDER_ATTEMPT_ID.fullmatch(str(entry.get("runId"))) is None:
                problems.add(
                    "PROVIDER_LEDGER_RUN",
                    entry_location,
                    "entry runId must use the frozen safe identifier form",
                )
        reserved_at = _timestamp(entry.get("reservedAt"))
        if reserved_at is None:
            problems.add(
                "PROVIDER_LEDGER_TIME",
                entry_location,
                "entry requires an absolute reservedAt timestamp",
            )
        elif previous_reserved_at is not None and reserved_at < previous_reserved_at:
            problems.add(
                "PROVIDER_LEDGER_TIME",
                entry_location,
                "provider reservation timestamps must be non-decreasing",
            )
        if reserved_at is not None:
            previous_reserved_at = reserved_at
        gate_document_for_time = gate_by_id.get(str(gate_id))
        gate_for_time = _mapping(
            gate_document_for_time.data
            if gate_document_for_time is not None
            else {}
        )
        gate_started_at = _timestamp(gate_for_time.get("startedAt"))
        gate_finished_at = _timestamp(gate_for_time.get("finishedAt"))
        if (
            reserved_at is None
            or gate_started_at is None
            or gate_finished_at is None
            or not (gate_started_at <= reserved_at <= gate_finished_at)
        ):
            problems.add(
                "PROVIDER_RESERVATION_GATE_WINDOW",
                entry_location,
                "reservation timestamp must be inside its Gate execution window",
            )
        entry_hash = entry.get("entrySha256")
        previous_entry_hash = entry.get("previousEntrySha256")
        if "previousEntrySha256" not in entry:
            problems.add(
                "PROVIDER_HASH_CHAIN",
                entry_location,
                "every entry must include previousEntrySha256",
            )
        if not isinstance(entry_hash, str) or HEX_SHA256.fullmatch(entry_hash) is None:
            problems.add("PROVIDER_ENTRY_HASH", entry_location, "entrySha256 must be lowercase SHA-256")
        else:
            calculated_hash = provider_entry_sha256(entry)
            if entry_hash != calculated_hash:
                problems.add(
                    "PROVIDER_ENTRY_HASH",
                    entry_location,
                    "entrySha256 does not match the canonical entry payload",
                )
        if index > 1 and previous_entry_hash != previous_hash:
            problems.add("PROVIDER_HASH_CHAIN", entry_location, "previousEntrySha256 does not match prior entry")
        if index == 1 and previous_entry_hash is not None:
            problems.add("PROVIDER_HASH_CHAIN", entry_location, "first entry previousEntrySha256 must be null")
        previous_hash = entry_hash if isinstance(entry_hash, str) else None
    if len(entries) != used or len(entries) != sequence:
        problems.add("PROVIDER_LEDGER_LENGTH", LEDGER_PATH, f"entries={len(entries)}, usedTurns={used}, ledgerSequence={sequence}")
    if ledger_counts != consumed_by_gate:
        problems.add(
            "PROVIDER_LEDGER_COUNTS",
            LEDGER_PATH,
            "provider entry counts do not match Gate consumption",
        )
    canonical_frontiers: dict[str, tuple[int, int]] = {}
    canonical_cursor = 0
    for gate_id in CANONICAL_GATE_IDS:
        expected_consumed = ledger_counts.get(gate_id, 0)
        expected_before = canonical_cursor
        expected_after = expected_before + expected_consumed
        canonical_frontiers[gate_id] = (expected_before, expected_after)
        document = gate_by_id.get(gate_id)
        if document is not None:
            authorization = _mapping(
                _mapping(document.data).get("authorization")
            )
            actual_frontier = (
                authorization.get("providerTurnsBefore"),
                authorization.get("providerTurnsConsumed"),
                authorization.get("providerTurnsAfter"),
                authorization.get("providerSequenceBefore"),
                authorization.get("providerSequenceAfter"),
            )
            expected_frontier = (
                expected_before,
                expected_consumed,
                expected_after,
                expected_before,
                expected_after,
            )
            if actual_frontier != expected_frontier:
                problems.add(
                    "PROVIDER_GATE_FRONTIER",
                    document.relative_path,
                    "Gate provider before/consumed/after fields must follow the canonical reservation frontier",
                )
        canonical_cursor = expected_after
    if canonical_cursor != len(entries):
        problems.add(
            "PROVIDER_GATE_FRONTIER",
            LEDGER_PATH,
            "canonical Gate frontiers must account for every provider reservation",
        )
    attempt_events = ledger.get("attemptEvents")
    if not isinstance(attempt_events, list):
        problems.add(
            "PROVIDER_ATTEMPT_EVENTS",
            LEDGER_PATH,
            "attemptEvents must be an array",
        )
        return
    if ledger.get("attemptEventCount") != len(attempt_events):
        problems.add(
            "PROVIDER_ATTEMPT_EVENT_COUNT",
            LEDGER_PATH,
            "attemptEventCount must equal the event array length",
        )

    completion_events_by_reservation: defaultdict[
        str, list[Mapping[str, Any]]
    ] = defaultdict(list)
    finish_events_by_attempt_list: defaultdict[
        str, list[Mapping[str, Any]]
    ] = defaultdict(list)
    previous_event_hash: str | None = None
    previous_event_time: datetime | None = None
    turn_completed_keys = {
        "attemptEventSequence",
        "eventType",
        "reservationId",
        "outcome",
        "completedAt",
        "previousAttemptEventSha256",
        "attemptEventSha256",
    }
    attempt_finished_keys = {
        "attemptEventSequence",
        "eventType",
        "gateId",
        "productCandidate",
        "attemptId",
        "outcome",
        "reservationSequenceStart",
        "reservationSequenceEnd",
        "finishedAt",
        "previousAttemptEventSha256",
        "attemptEventSha256",
    }
    for index, event in enumerate(attempt_events, start=1):
        event_location = f"{LEDGER_PATH}#/attemptEvents/{index - 1}"
        if not isinstance(event, Mapping):
            problems.add(
                "PROVIDER_ATTEMPT_EVENT",
                event_location,
                "attempt event must be an object",
            )
            continue
        event_type = event.get("eventType")
        expected_keys = (
            turn_completed_keys
            if event_type == "TurnCompleted"
            else attempt_finished_keys
            if event_type == "AttemptFinished"
            else set()
        )
        if not expected_keys or set(event) != expected_keys:
            problems.add(
                "PROVIDER_ATTEMPT_EVENT_SHAPE",
                event_location,
                "attempt event must use one exact frozen event envelope",
            )
        if event.get("attemptEventSequence") != index:
            problems.add(
                "PROVIDER_ATTEMPT_EVENT_SEQUENCE",
                event_location,
                f"attemptEventSequence must be {index}",
            )
        event_hash = event.get("attemptEventSha256")
        previous_declared = event.get("previousAttemptEventSha256")
        if "previousAttemptEventSha256" not in event:
            problems.add(
                "PROVIDER_ATTEMPT_EVENT_CHAIN",
                event_location,
                "every attempt event must include previousAttemptEventSha256",
            )
        if not isinstance(event_hash, str) or HEX_SHA256.fullmatch(event_hash) is None:
            problems.add(
                "PROVIDER_ATTEMPT_EVENT_HASH",
                event_location,
                "attemptEventSha256 must be lowercase SHA-256",
            )
        elif event_hash != provider_attempt_event_sha256(event):
            problems.add(
                "PROVIDER_ATTEMPT_EVENT_HASH",
                event_location,
                "attemptEventSha256 does not match the canonical event payload",
            )
        if index == 1 and previous_declared is not None:
            problems.add(
                "PROVIDER_ATTEMPT_EVENT_CHAIN",
                event_location,
                "first attempt event previousAttemptEventSha256 must be null",
            )
        if index > 1 and previous_declared != previous_event_hash:
            problems.add(
                "PROVIDER_ATTEMPT_EVENT_CHAIN",
                event_location,
                "previousAttemptEventSha256 does not match the prior event",
            )
        previous_event_hash = event_hash if isinstance(event_hash, str) else None

        event_time = _timestamp(
            event.get("completedAt")
            if event_type == "TurnCompleted"
            else event.get("finishedAt")
        )
        if event_time is None:
            problems.add(
                "PROVIDER_ATTEMPT_EVENT_TIME",
                event_location,
                "attempt event requires an absolute event timestamp",
            )
        elif previous_event_time is not None and event_time < previous_event_time:
            problems.add(
                "PROVIDER_ATTEMPT_EVENT_TIME",
                event_location,
                "attempt event timestamps must be non-decreasing",
            )
        if event_time is not None:
            previous_event_time = event_time

        if event_type == "TurnCompleted":
            reservation_id = event.get("reservationId")
            if not isinstance(reservation_id, str) or reservation_id not in reservation_by_id:
                problems.add(
                    "PROVIDER_COMPLETION_RESERVATION",
                    event_location,
                    "TurnCompleted must bind an existing reservation",
                )
            else:
                completion_events_by_reservation[reservation_id].append(event)
                reserved_at = _timestamp(reservation_by_id[reservation_id].get("reservedAt"))
                if (
                    event.get("outcome") not in PROVIDER_TURN_OUTCOMES
                    or reserved_at is None
                    or event_time is None
                    or event_time <= reserved_at
                ):
                    problems.add(
                        "PROVIDER_COMPLETION_OUTCOME_TIME",
                        event_location,
                        "TurnCompleted needs a valid outcome strictly after reservation",
                    )
                reservation_gate_id = reservation_by_id[reservation_id].get("gateId")
                completion_gate_document = gate_by_id.get(str(reservation_gate_id))
                completion_gate = _mapping(
                    completion_gate_document.data
                    if completion_gate_document is not None
                    else {}
                )
                completion_gate_started = _timestamp(
                    completion_gate.get("startedAt")
                )
                completion_gate_finished = _timestamp(
                    completion_gate.get("finishedAt")
                )
                if (
                    event_time is None
                    or completion_gate_started is None
                    or completion_gate_finished is None
                    or not (
                        completion_gate_started
                        <= event_time
                        <= completion_gate_finished
                    )
                ):
                    problems.add(
                        "PROVIDER_COMPLETION_GATE_WINDOW",
                        event_location,
                        "completion timestamp must be inside its Gate execution window",
                    )
        elif event_type == "AttemptFinished":
            attempt_id = event.get("attemptId")
            if (
                not isinstance(attempt_id, str)
                or PROVIDER_ATTEMPT_ID.fullmatch(attempt_id) is None
                or event.get("outcome") not in PROVIDER_ATTEMPT_OUTCOMES
            ):
                problems.add(
                    "PROVIDER_ATTEMPT_FINISHED",
                    event_location,
                    "AttemptFinished requires a safe attemptId and Passed/Failed outcome",
                )
            else:
                finish_events_by_attempt_list[attempt_id].append(event)

    if ledger.get("lastAttemptEventSha256") != previous_event_hash:
        problems.add(
            "PROVIDER_ATTEMPT_EVENT_LAST_HASH",
            LEDGER_PATH,
            "lastAttemptEventSha256 does not match the final attempt event",
        )
    if (
        provider.get("attemptEventCount") != len(attempt_events)
        or provider.get("lastAttemptEventSha256") != previous_event_hash
    ):
        problems.add(
            "PROVIDER_GOAL_EVENT_BINDING",
            "goal-state.json#/authorization/provider",
            "central Goal must bind the current attempt event count and final hash",
        )

    completion_by_reservation: dict[str, Mapping[str, Any]] = {}
    for reservation_id, reservation in reservation_by_id.items():
        completions = completion_events_by_reservation.get(reservation_id, [])
        if len(completions) != 1:
            sequence_value = reservation.get("sequence")
            problems.add(
                "PROVIDER_COMPLETION_CARDINALITY",
                f"{LEDGER_PATH}#/entries/{sequence_value - 1}"
                if _is_int(sequence_value) and sequence_value > 0
                else LEDGER_PATH,
                "every reservation requires exactly one TurnCompleted event",
            )
        elif completions:
            completion_by_reservation[reservation_id] = completions[0]

    finish_events_by_attempt: dict[str, Mapping[str, Any]] = {}
    for attempt_id, finish_events in finish_events_by_attempt_list.items():
        if len(finish_events) != 1:
            problems.add(
                "PROVIDER_ATTEMPT_FINISH_CARDINALITY",
                LEDGER_PATH,
                "each attempt may have exactly one AttemptFinished event",
            )
        elif finish_events:
            finish_events_by_attempt[attempt_id] = finish_events[0]

    global_attempt_owners: dict[str, str] = {}
    successful_entries_by_gate: dict[str, list[Mapping[str, Any]]] = {}
    all_attempts: list[
        tuple[str, str, str | None, list[Mapping[str, Any]], Mapping[str, Any] | None]
    ] = []
    for gate_id in REAL_PROVIDER_GATE_IDS:
        gate_entries = entries_by_gate.get(gate_id, [])
        attempts = _partition_provider_attempts(
            gate_id,
            gate_entries,
            finish_events_by_attempt,
            problems,
            global_attempt_owners,
        )
        all_attempts.extend(
            (gate_id, attempt_id, outcome, items, finish_event)
            for attempt_id, outcome, items, finish_event in attempts
        )
        document = gate_by_id.get(gate_id)
        gate = _mapping(document.data) if document is not None else {}
        authorization = _mapping(gate.get("authorization"))
        status = gate.get("status")
        successful_id = authorization.get("providerSuccessfulAttemptId")
        passed_attempts = [attempt for attempt in attempts if attempt[1] == "Passed"]
        if status == "Passed":
            if (
                len(passed_attempts) != 1
                or not attempts
                or attempts[-1][1] != "Passed"
                or successful_id != attempts[-1][0]
                or any(
                    outcome != "Failed"
                    for _aid, outcome, _items, _finish in attempts[:-1]
                )
            ):
                problems.add(
                    "PROVIDER_SUCCESS_ATTEMPT",
                    document.relative_path if document is not None else LEDGER_PATH,
                    "Passed provider Gate requires Failed* followed by one named final successful attempt",
                )
            elif passed_attempts:
                successful_entries_by_gate[gate_id] = list(passed_attempts[0][2])
        else:
            if passed_attempts or successful_id is not None:
                problems.add(
                    "PROVIDER_SUCCESS_ATTEMPT",
                    document.relative_path if document is not None else LEDGER_PATH,
                    "non-Passed provider Gate cannot retain a successful attempt",
                )

        if document is not None:
            sequence_before = authorization.get("providerSequenceBefore")
            sequence_after = authorization.get("providerSequenceAfter")
            expected_sequences = (
                list(range(sequence_before + 1, sequence_after + 1))
                if _is_int(sequence_before) and _is_int(sequence_after)
                else []
            )
            actual_sequences = [entry.get("sequence") for entry in gate_entries]
            if actual_sequences != expected_sequences:
                problems.add(
                    "PROVIDER_GATE_RANGE",
                    document.relative_path,
                    "Gate provider entries do not exactly fill its declared ledger sequence range",
                )
            observed_provider_scopes = {
                str(entry.get("phase")) for entry in gate_entries
            }
            declared_provider_scopes = set(
                _list(authorization.get("scopesUsed"))
            ) & PROVIDER_SCOPES
            if observed_provider_scopes != declared_provider_scopes:
                problems.add(
                    "PROVIDER_SCOPE_BINDING",
                    document.relative_path,
                    "declared provider scopes do not equal observed ledger phases",
                )
            controlled_turns = sum(
                1 for entry in gate_entries if entry.get("phase") == "controlled-write"
            )
            write_count = authorization.get("controlledWriteExecutionCount")
            if controlled_turns > 1 or (
                _is_int(write_count) and controlled_turns != write_count
            ):
                problems.add(
                    "PROVIDER_CONTROLLED_WRITE_LIMIT",
                    document.relative_path,
                    "controlled-write provider phase must reconcile to the single-use execution count",
                )

        for _attempt_id, outcome, attempt_entries, finish_event in attempts:
            if finish_event is None:
                continue
            first_sequence = attempt_entries[0].get("sequence") if attempt_entries else None
            last_sequence = attempt_entries[-1].get("sequence") if attempt_entries else None
            completion_times = [
                _timestamp(
                    completion_by_reservation.get(
                        str(entry.get("reservationId")), {}
                    ).get("completedAt")
                )
                for entry in attempt_entries
            ]
            finished_at = _timestamp(finish_event.get("finishedAt"))
            if (
                finish_event.get("gateId") != gate_id
                or finish_event.get("productCandidate")
                != _mapping(gate.get("identity")).get("productCandidate")
                or finish_event.get("reservationSequenceStart") != first_sequence
                or finish_event.get("reservationSequenceEnd") != last_sequence
                or any(value is None for value in completion_times)
                or finished_at is None
                or any(
                    finished_at <= value
                    for value in completion_times
                    if value is not None
                )
                or _timestamp(gate.get("startedAt")) is None
                or _timestamp(gate.get("finishedAt")) is None
                or finished_at is None
                or not (
                    _timestamp(gate.get("startedAt"))
                    <= finished_at
                    <= _timestamp(gate.get("finishedAt"))
                )
            ):
                problems.add(
                    "PROVIDER_ATTEMPT_FINISH_BINDING",
                    document.relative_path if document is not None else LEDGER_PATH,
                    "AttemptFinished must bind its exact Gate/candidate/range after all completions",
                )
            if outcome == "Passed" and any(
                completion_by_reservation.get(
                    str(entry.get("reservationId")), {}
                ).get("outcome")
                != "Succeeded"
                for entry in attempt_entries
            ):
                problems.add(
                    "PROVIDER_SUCCESS_TURN_OUTCOME",
                    document.relative_path if document is not None else LEDGER_PATH,
                    "a Passed attempt requires every reserved turn to succeed",
                )

        controlled_attempt_indexes = [
            index
            for index, (_aid, _outcome, attempt_entries, _finish) in enumerate(attempts)
            if any(entry.get("phase") == "controlled-write" for entry in attempt_entries)
        ]
        if len(controlled_attempt_indexes) > 1:
            problems.add(
                "PROVIDER_CONTROLLED_WRITE_LIMIT",
                document.relative_path if document is not None else LEDGER_PATH,
                "controlled-write may be reserved at most once in a Gate's full history",
            )
        if controlled_attempt_indexes:
            controlled_index = controlled_attempt_indexes[0]
            controlled_outcome = attempts[controlled_index][1]
            if controlled_outcome != "Passed" and controlled_index != len(attempts) - 1:
                problems.add(
                    "PROVIDER_CONTROLLED_WRITE_RETRY",
                    document.relative_path if document is not None else LEDGER_PATH,
                    "a failed or open controlled-write attempt cannot be retried",
                )

    observed_attempt_ids = {
        attempt_id for _gate_id, attempt_id, _outcome, _items, _finish in all_attempts
    }
    for attempt_id in finish_events_by_attempt:
        if attempt_id not in observed_attempt_ids:
            problems.add(
                "PROVIDER_ATTEMPT_FINISH_ORPHAN",
                LEDGER_PATH,
                "AttemptFinished cannot exist without reserved turns",
            )

    open_attempts = [attempt for attempt in all_attempts if attempt[4] is None]
    if open_attempts:
        open_gate_id, _open_id, _outcome, open_entries, _finish = open_attempts[-1]
        open_document = gate_by_id.get(open_gate_id)
        open_gate = _mapping(open_document.data if open_document is not None else {})
        is_last_attempt = bool(entries) and bool(open_entries) and (
            entries[-1].get("attemptId") == open_entries[-1].get("attemptId")
            and entries[-1].get("gateId") == open_gate_id
        )
        if (
            len(open_attempts) != 1
            or goal.get("status") != "Active"
            or open_gate.get("status") == "Passed"
            or not is_last_attempt
        ):
            problems.add(
                "PROVIDER_OPEN_ATTEMPT",
                LEDGER_PATH,
                "only the unique final attempt of the current Active non-Passed Gate may remain open",
            )

    if previous_hash is None and entries:
        problems.add(
            "PROVIDER_HASH_CHAIN",
            LEDGER_PATH,
            "reservation chain did not produce a valid final hash",
        )

    runtime_events, runtime_by_reservation = (
        _validate_provider_runtime_journal(
            goal,
            gate_by_id,
            entries,
            reservation_by_id,
            completion_by_reservation,
            complete,
            evidence_root,
            problems,
            candidate_root=repo_root,
        )
    )
    if evidence_root is not None:
        _validate_handoff_runtime_snapshots(
            handoff_documents,
            runtime_events,
            evidence_root,
            problems,
        )

    _validate_provider_ledger_bindings(
        gate_by_id,
        entries,
        attempt_events,
        runtime_events,
        reservation_by_id,
        canonical_frontiers,
        evidence_root,
        problems,
    )

    if evidence_root is not None:
        _validate_receipt_ledger_ranges(
            gate_by_id,
            successful_entries_by_gate,
            completion_by_reservation,
            runtime_by_reservation,
            evidence_root,
            problems,
        )

    for document in handoff_documents:
        handoff = _mapping(document.data)
        budget = _mapping(handoff.get("authorizationBudget"))
        h_used = budget.get("providerTurnsUsed")
        h_remaining = budget.get("providerTurnsRemaining")
        if _is_int(h_used) and _is_int(h_remaining) and h_used + h_remaining != PROVIDER_BUDGET:
            problems.add("HANDOFF_PROVIDER_BUDGET", document.relative_path, "provider used + remaining must equal 120")
        binding = _mapping(handoff.get("goalControlBinding"))
        bound_sequence = binding.get("providerLedgerSequence")
        if _is_int(bound_sequence) and bound_sequence > sequence:
            problems.add("HANDOFF_PROVIDER_SEQUENCE", document.relative_path, "handoff ledger sequence exceeds Goal")
        if handoff.get("decision") == "GoalComplete":
            if h_used != used or h_remaining != remaining or bound_sequence != sequence:
                problems.add("HANDOFF_PROVIDER_FINAL", document.relative_path, "GoalComplete provider summary is not current")
            if binding.get("providerLedgerSha256") != ledger_document.sha256:
                problems.add("HANDOFF_PROVIDER_HASH", document.relative_path, "GoalComplete ledger hash does not match file")


def _expected_write_count(gate_by_id: Mapping[str, Document], gate_id: str) -> int:
    document = gate_by_id.get(gate_id)
    if document is None:
        return 0
    authorization = _mapping(_mapping(document.data).get("authorization"))
    value = authorization.get("controlledWriteExecutionCount")
    return value if _is_int(value) else 0


def validate_controlled_writes(
    goal: Mapping[str, Any],
    gate_by_id: Mapping[str, Document],
    contract: Contract,
    complete: bool,
    problems: Problems,
) -> None:
    executions = _controlled_summary(goal)
    for week, gate_id in CONTROLLED_WRITE_GATES.items():
        key = f"w{week}"
        summary = _mapping(executions.get(key))
        actual_count = _expected_write_count(gate_by_id, gate_id)
        if summary.get("gateId") != gate_id or summary.get("week") != week:
            problems.add("CONTROLLED_WRITE_GOAL", f"goal-state.json#/authorization/controlledWrite/executions/{key}", "wrong gate/week binding")
        if summary.get("executionCount") != actual_count:
            problems.add("CONTROLLED_WRITE_GOAL", f"goal-state.json#/authorization/controlledWrite/executions/{key}", f"executionCount does not match {gate_id}")
        if actual_count > 1:
            problems.add("CONTROLLED_WRITE_LIMIT", gate_id, "controlled write executed more than once")
        if complete:
            if actual_count != 1 or summary.get("status") != "Passed":
                problems.add("CONTROLLED_WRITE_COMPLETE", gate_id, "Complete Goal requires exactly one Passed execution")
            if summary.get("shapeVerified") is not True or summary.get("nonWritePreconditionsPassed") is not True:
                problems.add("CONTROLLED_WRITE_COMPLETE", gate_id, "Complete Goal requires verified shape and preconditions")

        document = gate_by_id.get(gate_id)
        if document is not None and _mapping(_mapping(document.data).get("authorization")).get("controlledWriteUsed") is True:
            group = contract.gate_to_group[gate_id]
            gate_index = group.gate_ids.index(gate_id)
            for prerequisite_id in group.gate_ids[:gate_index]:
                prerequisite = gate_by_id.get(prerequisite_id)
                if prerequisite is None or _mapping(prerequisite.data).get("status") != "Passed":
                    problems.add("CONTROLLED_WRITE_ORDER", document.relative_path, f"non-write prerequisite {prerequisite_id} is not Passed")


def _provider_launch_receipt_basename(
    phase: str,
    profile_ordinal: int | None,
) -> str:
    suffix = (
        f"{phase}-profile-{profile_ordinal}"
        if profile_ordinal is not None
        else phase
    )
    return f"package-launch-{suffix}.json"


def _read_hash_bound_json(
    repo_root: Path,
    relative_path: Any,
    expected_sha256: Any,
    problems: Problems,
    location: str,
    code: str,
) -> tuple[Mapping[str, Any], bytes] | None:
    path = _safe_relative_evidence_path(
        repo_root,
        relative_path,
        problems,
        f"{location}#/path",
    )
    if path is None or not path.is_file() or path.is_symlink():
        problems.add(code, location, "hash-bound JSON path must be a regular file")
        return None
    try:
        raw = path.read_bytes()
    except OSError:
        problems.add(code, location, "hash-bound JSON bytes could not be read")
        return None
    document = read_document(repo_root, path, problems)
    data = _mapping(document.data) if document is not None else {}
    if (
        document is None
        or not isinstance(expected_sha256, str)
        or HEX_SHA256.fullmatch(expected_sha256) is None
        or hashlib.sha256(raw).hexdigest() != expected_sha256
        or raw != _json_bytes(data)
    ):
        problems.add(
            code,
            location,
            "hash-bound JSON must use canonical bytes matching its declared SHA-256",
        )
        return None
    _scan_value_for_secrets(data, problems, f"{relative_path}#")
    return data, raw


def _package_manifest_entry_sha256(
    package_document: Mapping[str, Any],
    relative_path: str,
) -> str | None:
    identity = _mapping(package_document.get("packageIdentity"))
    entries = identity.get("entries", package_document.get("entries"))
    matches = [
        item.get("sha256")
        for item in _list(entries)
        if isinstance(item, Mapping)
        and str(item.get("path", "")).replace("\\", "/").casefold()
        == relative_path.casefold()
        and isinstance(item.get("sha256"), str)
    ]
    return str(matches[0]) if len(matches) == 1 else None


def _validate_provider_boundary_decision(
    repo_root: Path,
    gate_document: Document,
    binding: Mapping[str, Any],
    problems: Problems,
) -> tuple[Mapping[str, Any], Mapping[str, Any]] | None:
    gate = _mapping(gate_document.data)
    gate_id = str(gate.get("gateId"))
    location = f"{gate_document.relative_path}#/authorization/providerBoundaryDecision"
    expected_path = PROVIDER_BOUNDARY_DECISION_PATHS.get(gate_id)
    binding_keys = {
        "path",
        "sha256",
        "firstAddCommit",
        "decisionId",
        "mode",
        "packageTreeRootSha256",
    }
    first_add = binding.get("firstAddCommit")
    candidate = _mapping(gate.get("identity")).get("productCandidate")
    if (
        set(binding) != binding_keys
        or binding.get("path") != expected_path
        or binding.get("mode") not in PROVIDER_BOUNDARY_MODES
        or not isinstance(binding.get("decisionId"), str)
        or PROVIDER_ATTEMPT_ID.fullmatch(str(binding.get("decisionId"))) is None
        or not isinstance(binding.get("sha256"), str)
        or HEX_SHA256.fullmatch(str(binding.get("sha256"))) is None
        or not isinstance(binding.get("packageTreeRootSha256"), str)
        or HEX_SHA256.fullmatch(str(binding.get("packageTreeRootSha256"))) is None
        or not isinstance(first_add, str)
        or re.fullmatch(r"[0-9a-f]{40}", first_add) is None
        or not isinstance(candidate, str)
        or re.fullmatch(r"[0-9a-f]{40}", candidate) is None
    ):
        problems.add(
            "PROVIDER_BOUNDARY_BINDING",
            location,
            "provider boundary decision binding is not exact",
        )
        return None
    loaded = _read_hash_bound_json(
        repo_root,
        binding.get("path"),
        binding.get("sha256"),
        problems,
        location,
        "PROVIDER_BOUNDARY_DECISION",
    )
    if loaded is None:
        return None
    decision, _raw = loaded
    if (
        _single_add_commit(repo_root, str(expected_path)) != first_add
        or not _validate_single_add_immutable_blob(
            repo_root,
            str(expected_path),
            problems,
            "PROVIDER_BOUNDARY_DECISION",
        )
        or first_add == candidate
        or _git_return_code(
            repo_root,
            ("merge-base", "--is-ancestor", candidate, first_add),
        )
        != 0
    ):
        problems.add(
            "PROVIDER_BOUNDARY_HISTORY",
            location,
            "decision must be an immutable unique first-add descendant of its exact product candidate",
        )

    policy = _mapping(PROVIDER_BOUNDARY_DECISION_POLICIES.get(str(expected_path)))
    decision_keys = {
        "goalId",
        "decisionId",
        "boundaryGateId",
        "mode",
        "productCandidate",
        "packageIdentity",
        "allowedGates",
        "allowedScopes",
        "authorizedTurns",
        "maxTurns",
        "userChallenge",
        "issuedAt",
        "expiresAt",
        "revokedAt",
        "strongCanaries",
    }
    package_binding = _mapping(decision.get("packageIdentity"))
    challenge = _mapping(decision.get("userChallenge"))
    issued_at = _timestamp(decision.get("issuedAt"))
    expires_at = _timestamp(decision.get("expiresAt"))
    gate_started_at = _timestamp(gate.get("startedAt"))
    gate_finished_at = _timestamp(gate.get("finishedAt"))
    valid = True
    if (
        set(decision) != decision_keys
        or decision.get("goalId") != GOAL_ID
        or decision.get("decisionId") != binding.get("decisionId")
        or decision.get("boundaryGateId") != policy.get("boundaryGateId")
        or decision.get("mode") != binding.get("mode")
        or decision.get("productCandidate") != candidate
        or decision.get("allowedGates") != policy.get("allowedGates")
        or decision.get("allowedScopes") != policy.get("allowedScopes")
        or decision.get("authorizedTurns") != policy.get("authorizedTurns")
        or decision.get("maxTurns") != PROVIDER_BUDGET
        or decision.get("revokedAt") is not None
        or issued_at is None
        or expires_at is None
        or issued_at >= expires_at
        or gate_started_at is None
        or gate_finished_at is None
        or not (issued_at <= gate_started_at <= gate_finished_at <= expires_at)
        or set(package_binding)
        != {"path", "sha256", "treeRootSha256", "entrypoint", "argv"}
        or package_binding.get("treeRootSha256")
        != binding.get("packageTreeRootSha256")
        or package_binding.get("entrypoint") != PROVIDER_PACKAGE_ENTRYPOINT
        or package_binding.get("argv") != PROVIDER_PACKAGE_ARGV
        or not isinstance(package_binding.get("path"), str)
        or PurePosixPath(str(package_binding.get("path"))).name
        != "package-identity.json"
        or not isinstance(package_binding.get("sha256"), str)
        or HEX_SHA256.fullmatch(str(package_binding.get("sha256"))) is None
        or set(challenge)
        != {"requestId", "challengeCode", "responseSha256", "confirmedBy"}
        or not isinstance(challenge.get("requestId"), str)
        or re.fullmatch(r"BR-[0-9]{3,}", str(challenge.get("requestId"))) is None
        or not isinstance(challenge.get("challengeCode"), str)
        or CHALLENGE_CODE.fullmatch(str(challenge.get("challengeCode"))) is None
        or not isinstance(challenge.get("responseSha256"), str)
        or HEX_SHA256.fullmatch(str(challenge.get("responseSha256"))) is None
        or challenge.get("confirmedBy") != "User"
    ):
        problems.add(
            "PROVIDER_BOUNDARY_DECISION_SHAPE",
            location,
            "decision does not bind exact user authorization, candidate, package, scope, turn, and lifetime policy",
        )
        valid = False

    strong_canaries = decision.get("strongCanaries")
    if binding.get("mode") == "cooperative-candidate":
        if strong_canaries is not None:
            problems.add(
                "PROVIDER_BOUNDARY_COOPERATIVE_CLAIM",
                location,
                "cooperative-candidate decision cannot claim strong isolation canaries",
            )
            valid = False
    else:
        strong = _mapping(strong_canaries)
        if set(strong) != {
            "sandboxPolicySha256",
            "filesystemReceipt",
            "networkReceipt",
            "processReceipt",
        } or HEX_SHA256.fullmatch(str(strong.get("sandboxPolicySha256"))) is None:
            problems.add(
                "PROVIDER_BOUNDARY_STRONG_CANARIES",
                location,
                "strong-isolation requires sandbox policy plus filesystem/network/process canary receipts",
            )
            valid = False
        else:
            for canary_name in (
                "filesystemReceipt",
                "networkReceipt",
                "processReceipt",
            ):
                canary = _mapping(strong.get(canary_name))
                if set(canary) != {"path", "sha256"} or _read_hash_bound_json(
                    repo_root,
                    canary.get("path"),
                    canary.get("sha256"),
                    problems,
                    f"{location}#/strongCanaries/{canary_name}",
                    "PROVIDER_BOUNDARY_STRONG_CANARY",
                ) is None:
                    valid = False

    package_loaded = _read_hash_bound_json(
        repo_root,
        package_binding.get("path"),
        package_binding.get("sha256"),
        problems,
        f"{location}#/packageIdentity",
        "PROVIDER_PACKAGE_IDENTITY",
    )
    if package_loaded is None:
        return None
    package_document, _package_raw = package_loaded
    package_identity = _mapping(package_document.get("packageIdentity"))
    package_tree_root = package_identity.get(
        "treeRootSha256", package_document.get("treeRootSha256")
    )
    package_entrypoint = package_identity.get(
        "entrypoint", package_document.get("entrypoint")
    )
    package_argv = package_identity.get("argv", package_document.get("argv"))
    if (
        package_document.get("productCandidate") != candidate
        or package_tree_root != binding.get("packageTreeRootSha256")
        or package_entrypoint != PROVIDER_PACKAGE_ENTRYPOINT
        or package_argv != PROVIDER_PACKAGE_ARGV
        or _package_manifest_entry_sha256(
            package_document, PROVIDER_PACKAGE_ENTRYPOINT
        )
        is None
        or _package_manifest_entry_sha256(
            package_document, PROVIDER_PACKAGE_APPHOST_PATH
        )
        is None
    ):
        problems.add(
            "PROVIDER_PACKAGE_IDENTITY",
            location,
            "package identity must bind the exact candidate tree, Desktop entrypoint, and packaged AppHost child",
        )
        valid = False
    return (decision, package_document) if valid else None


def _validate_provider_launch_receipt(
    repo_root: Path,
    gate_document: Document,
    binding: Mapping[str, Any],
    expected_layout: tuple[str, int | None, int],
    decision: Mapping[str, Any],
    package_document: Mapping[str, Any],
    problems: Problems,
) -> None:
    gate = _mapping(gate_document.data)
    gate_id = str(gate.get("gateId"))
    phase, profile_ordinal, batch_size = expected_layout
    location = (
        f"{gate_document.relative_path}#/authorization/packageLaunchReceipts/"
        f"{phase}:{profile_ordinal}"
    )
    loaded = _read_hash_bound_json(
        repo_root,
        binding.get("path"),
        binding.get("sha256"),
        problems,
        location,
        "PROVIDER_PACKAGE_LAUNCH_RECEIPT",
    )
    if loaded is None:
        return
    receipt, _raw = loaded
    required_top = {
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
    candidate = _mapping(gate.get("identity")).get("productCandidate")
    boundary = _mapping(receipt.get("boundaryDecision"))
    source = _mapping(receipt.get("sourcePackage"))
    staged_before = _mapping(receipt.get("stagedPackageBefore"))
    staged_after = _mapping(receipt.get("stagedPackageAfter"))
    launches = _mapping(receipt.get("launches"))
    desktop = _mapping(launches.get("desktop"))
    app_host = _mapping(launches.get("appHost"))
    containment = _mapping(receipt.get("containment"))
    assertions = _mapping(receipt.get("scenarioAssertions"))
    cleanup = _mapping(receipt.get("cleanup"))
    package_identity = _mapping(package_document.get("packageIdentity"))
    entry_count = package_identity.get(
        "entryCount", package_document.get("entryCount")
    )
    total_bytes = package_identity.get(
        "totalBytes", package_document.get("totalBytes")
    )
    package_root = package_identity.get(
        "packageRoot", package_identity.get("path")
    )
    package_binding = _mapping(decision.get("packageIdentity"))
    tree_root = binding.get("packageTreeRootSha256")
    valid = True
    if (
        not required_top.issubset(receipt)
        or ("status" in receipt and receipt.get("status") != "Passed")
        or receipt.get("goalId") != GOAL_ID
        or receipt.get("gateId") != gate_id
        or receipt.get("productCandidate") != candidate
        or receipt.get("phase") != phase
        or receipt.get("profileOrdinal") != profile_ordinal
        or receipt.get("batchId") != binding.get("batchId")
        or receipt.get("packageTreeRootSha256") != tree_root
        or set(boundary) != {"decisionId", "mode"}
        or boundary.get("decisionId") != decision.get("decisionId")
        or boundary.get("mode") != decision.get("mode")
        or set(source)
        != {
            "identityPath",
            "identityRawSha256",
            "packageRoot",
            "packageTreeRootSha256",
            "entryCount",
            "totalBytes",
            "entrypoint",
            "argv",
            "appHostPath",
            "appHostSha256",
        }
        or source.get("identityPath") != package_binding.get("path")
        or source.get("identityRawSha256") != package_binding.get("sha256")
        or source.get("packageRoot") != package_root
        or source.get("packageTreeRootSha256") != tree_root
        or source.get("entryCount") != entry_count
        or source.get("totalBytes") != total_bytes
        or source.get("entrypoint") != PROVIDER_PACKAGE_ENTRYPOINT
        or source.get("argv") != PROVIDER_PACKAGE_ARGV
        or source.get("appHostPath") != PROVIDER_PACKAGE_APPHOST_PATH
        or source.get("appHostSha256")
        != _package_manifest_entry_sha256(
            package_document, PROVIDER_PACKAGE_APPHOST_PATH
        )
        or not _is_int(entry_count)
        or entry_count < 2
        or not _is_int(total_bytes)
        or total_bytes <= 0
    ):
        problems.add(
            "PROVIDER_PACKAGE_LAUNCH_SOURCE",
            location,
            "launch receipt must bind the exact candidate package tree, Desktop entrypoint, and AppHost image",
        )
        valid = False

    expected_stage = {
        "packageTreeRootSha256": tree_root,
        "entryCount": entry_count,
        "totalBytes": total_bytes,
    }
    if staged_before != expected_stage or staged_after != expected_stage:
        problems.add(
            "PROVIDER_PACKAGE_LAUNCH_STAGE",
            location,
            "staged package tree must match before and after the actual launch",
        )
        valid = False

    launch_keys = {
        "imagePath",
        "imageSha256",
        "pid",
        "startedAt",
        "exitedAt",
        "exitCode",
    }
    desktop_start = _timestamp(desktop.get("startedAt"))
    desktop_exit = _timestamp(desktop.get("exitedAt"))
    app_host_start = _timestamp(app_host.get("startedAt"))
    app_host_exit = _timestamp(app_host.get("exitedAt"))
    desktop_image_path = str(desktop.get("imagePath", "")).replace("\\", "/")
    app_host_image_path = str(app_host.get("imagePath", "")).replace("\\", "/")
    desktop_sha = _package_manifest_entry_sha256(
        package_document, PROVIDER_PACKAGE_ENTRYPOINT
    )
    app_host_sha = _package_manifest_entry_sha256(
        package_document, PROVIDER_PACKAGE_APPHOST_PATH
    )
    if (
        set(launches) != {"desktop", "appHost"}
        or set(desktop) != launch_keys
        or set(app_host) != launch_keys
        or not (
            desktop_image_path == PROVIDER_PACKAGE_ENTRYPOINT
            or desktop_image_path.endswith(f"/{PROVIDER_PACKAGE_ENTRYPOINT}")
        )
        or not (
            app_host_image_path == PROVIDER_PACKAGE_APPHOST_PATH
            or app_host_image_path.endswith(f"/{PROVIDER_PACKAGE_APPHOST_PATH}")
        )
        or desktop.get("imageSha256") != desktop_sha
        or app_host.get("imageSha256") != app_host_sha
        or not _is_int(desktop.get("pid"))
        or desktop.get("pid") <= 0
        or not _is_int(app_host.get("pid"))
        or app_host.get("pid") <= 0
        or desktop.get("pid") == app_host.get("pid")
        or desktop_start is None
        or desktop_exit is None
        or app_host_start is None
        or app_host_exit is None
        or not (
            desktop_start
            <= app_host_start
            <= app_host_exit
            <= desktop_exit
        )
        or desktop.get("exitCode") != 0
        or app_host.get("exitCode") != 0
    ):
        problems.add(
            "PROVIDER_PACKAGE_LAUNCH_PROCESS",
            location,
            "receipt must prove successful Desktop launch plus packaged AppHost descendant identity",
        )
        valid = False

    containment_keys = {
        "attachedBeforeRelease",
        "processIsolated",
        "allEgressIsolated",
        "sandboxPolicySha256",
        "strongCanaryEvidence",
    }
    mode = decision.get("mode")
    containment_valid = (
        set(containment) == containment_keys
        and containment.get("attachedBeforeRelease") is True
    )
    if mode == "cooperative-candidate":
        containment_valid = (
            containment_valid
            and containment.get("processIsolated") is False
            and containment.get("allEgressIsolated") is False
            and containment.get("strongCanaryEvidence") is None
        )
    else:
        containment_valid = (
            containment_valid
            and containment.get("processIsolated") is True
            and containment.get("allEgressIsolated") is True
            and isinstance(containment.get("sandboxPolicySha256"), str)
            and HEX_SHA256.fullmatch(
                str(containment.get("sandboxPolicySha256"))
            )
            is not None
            and bool(containment.get("strongCanaryEvidence"))
        )
    if not containment_valid:
        problems.add(
            "PROVIDER_PACKAGE_LAUNCH_CONTAINMENT",
            location,
            "containment receipt is inconsistent with the authorized boundary mode",
        )
        valid = False

    if (
        receipt.get("observedRequestCount") != batch_size
        or set(assertions)
        != {
            "desktopUiObserved",
            "productBehaviorPassed",
            "recoveryListenerObserved",
            "details",
        }
        or assertions.get("desktopUiObserved") is not True
        or assertions.get("productBehaviorPassed") is not True
        or not isinstance(assertions.get("recoveryListenerObserved"), bool)
        or (
            phase == "provider-recovery"
            and assertions.get("recoveryListenerObserved") is not True
        )
        or not isinstance(assertions.get("details"), Mapping)
        or not assertions.get("details")
    ):
        problems.add(
            "PROVIDER_PACKAGE_LAUNCH_ASSERTIONS",
            location,
            "launch receipt must reconcile requests and actual Desktop UI/product/recovery listener assertions",
        )
        valid = False
    if cleanup != {"status": "Passed", "processDelta": 0, "temporaryDelta": 0}:
        problems.add(
            "PROVIDER_PACKAGE_LAUNCH_CLEANUP",
            location,
            "package launch cleanup must be Passed with zero owned process/temp delta",
        )
        valid = False
    if not valid:
        return


def _provider_runtime_batch_projection(
    repo_root: Path,
    gate_id: str,
    attempt_id: str,
    problems: Problems,
) -> list[tuple[str, str, int]] | None:
    runtime_path = repo_root / PROVIDER_RUNTIME_JOURNAL_PATH
    runtime_document = read_document(repo_root, runtime_path, problems)
    if runtime_document is None:
        return None
    events = [
        event
        for event in _list(_mapping(runtime_document.data).get("events"))
        if isinstance(event, Mapping)
        and event.get("gateId") == gate_id
        and event.get("attemptId") == attempt_id
        and event.get("outcome") == "Succeeded"
    ]
    batches: list[list[Mapping[str, Any]]] = []
    for event in events:
        if not batches or batches[-1][0].get("batchId") != event.get("batchId"):
            batches.append([])
        batches[-1].append(event)
    result: list[tuple[str, str, int]] = []
    for batch in batches:
        batch_id = batch[0].get("batchId")
        phase = batch[0].get("phase")
        if not isinstance(batch_id, str) or not isinstance(phase, str):
            return None
        result.append((batch_id, phase, len(batch)))
    return result


def validate_provider_boundaries(
    gate_by_id: Mapping[str, Document],
    repo_root: Path | None,
    problems: Problems,
    *,
    candidate_root: Path | None = None,
) -> None:
    source_root = candidate_root or repo_root
    shared_w84_binding: Mapping[str, Any] | None = None
    for gate_id, document in gate_by_id.items():
        gate = _mapping(document.data)
        authorization = _mapping(gate.get("authorization"))
        binding_value = authorization.get("providerBoundaryDecision")
        receipts_value = authorization.get("packageLaunchReceipts")
        provider_gate = gate_id in REAL_PROVIDER_GATE_IDS
        if not provider_gate:
            if binding_value is not None or receipts_value != []:
                problems.add(
                    "PROVIDER_BOUNDARY_UNAUTHORIZED",
                    document.relative_path,
                    "non-provider Gate must use null boundary decision and no package launch receipts",
                )
            continue
        if gate.get("status") in {"NotRun", "NotApplicable"}:
            if binding_value is not None or receipts_value != []:
                problems.add(
                    "PROVIDER_BOUNDARY_NOTRUN",
                    document.relative_path,
                    "NotRun/NotApplicable provider Gate cannot claim a boundary launch",
                )
            continue
        if gate.get("status") != "Passed" and binding_value is None:
            continue
        binding = _mapping(binding_value)
        receipts = [
            item for item in _list(receipts_value) if isinstance(item, Mapping)
        ]
        layout = PROVIDER_LAUNCH_LAYOUT[gate_id]
        if gate.get("status") == "Passed" and (
            not isinstance(binding_value, Mapping)
            or not isinstance(receipts_value, list)
            or len(receipts) != len(receipts_value)
            or len(receipts) != len(layout)
        ):
            problems.add(
                "PROVIDER_BOUNDARY_REQUIRED",
                document.relative_path,
                "Passed provider Gate requires its exact boundary decision and complete package launch batch list",
            )
            continue
        if not isinstance(binding_value, Mapping):
            problems.add(
                "PROVIDER_BOUNDARY_BINDING",
                document.relative_path,
                "provider launch receipts require a boundary decision binding",
            )
            continue
        tree_root = binding.get("packageTreeRootSha256")
        result_parts = _normalised_repo_parts(gate.get("resultPath"))
        artifact_parts = result_parts[:-2] if result_parts is not None else None
        expected_parent = (
            (*artifact_parts, "gate-evidence", gate_id)
            if artifact_parts is not None
            else None
        )
        observed_batch_ids: set[str] = set()
        receipt_shape_valid = len(receipts) <= len(layout)
        for index, receipt in enumerate(receipts):
            if index >= len(layout):
                receipt_shape_valid = False
                break
            phase, profile_ordinal, _batch_size = layout[index]
            receipt_parts = _normalised_repo_parts(receipt.get("path"))
            expected_basename = _provider_launch_receipt_basename(
                phase, profile_ordinal
            )
            batch_id = receipt.get("batchId")
            if (
                set(receipt)
                != {
                    "path",
                    "sha256",
                    "phase",
                    "profileOrdinal",
                    "batchId",
                    "packageTreeRootSha256",
                }
                or receipt.get("phase") != phase
                or receipt.get("profileOrdinal") != profile_ordinal
                or receipt.get("packageTreeRootSha256") != tree_root
                or not isinstance(receipt.get("sha256"), str)
                or HEX_SHA256.fullmatch(str(receipt.get("sha256"))) is None
                or not isinstance(batch_id, str)
                or PROVIDER_ATTEMPT_ID.fullmatch(batch_id) is None
                or batch_id in observed_batch_ids
                or receipt_parts is None
                or expected_parent is None
                or tuple(receipt_parts[:-1]) != expected_parent
                or receipt_parts[-1] != expected_basename
            ):
                receipt_shape_valid = False
            if isinstance(batch_id, str):
                observed_batch_ids.add(batch_id)
        if not receipt_shape_valid:
            problems.add(
                "PROVIDER_PACKAGE_LAUNCH_BINDING",
                document.relative_path,
                "package launch bindings must be exact, ordered, unique, Gate-local, and match the frozen batch layout",
            )
        if gate_id.startswith("W84-"):
            if shared_w84_binding is None:
                shared_w84_binding = binding
            elif binding != shared_w84_binding:
                problems.add(
                    "PROVIDER_BOUNDARY_SHARED_W84",
                    document.relative_path,
                    "W84-G6/G7/G8 must reuse one identical immutable W84 boundary decision",
                )
        if repo_root is None:
            continue
        decision_bundle = _validate_provider_boundary_decision(
            source_root,
            document,
            binding,
            problems,
        )
        if decision_bundle is None:
            continue
        decision, package_document = decision_bundle
        for index, receipt in enumerate(receipts[: len(layout)]):
            _validate_provider_launch_receipt(
                repo_root,
                document,
                receipt,
                layout[index],
                decision,
                package_document,
                problems,
            )
        attempt_id = authorization.get("providerSuccessfulAttemptId")
        if gate.get("status") == "Passed" and isinstance(attempt_id, str):
            runtime_batches = _provider_runtime_batch_projection(
                repo_root,
                gate_id,
                attempt_id,
                problems,
            )
            receipt_batches = [
                (
                    str(receipt.get("batchId")),
                    str(receipt.get("phase")),
                    layout[index][2],
                )
                for index, receipt in enumerate(receipts[: len(layout)])
            ]
            if runtime_batches != receipt_batches:
                problems.add(
                    "PROVIDER_PACKAGE_LAUNCH_RUNTIME",
                    document.relative_path,
                    "package launch receipts must exactly match successful runtime journal batches",
                )


def validate_provider_gate_requirements(
    gate_by_id: Mapping[str, Document],
    repo_root: Path | None,
    problems: Problems,
) -> None:
    for document in gate_by_id.values():
        gate = _mapping(document.data)
        gate_id = gate.get("gateId")
        authorization = _mapping(gate.get("authorization"))
        scopes = set(_list(authorization.get("scopesUsed")))
        consumed = authorization.get("providerTurnsConsumed")
        successful_attempt_id = authorization.get("providerSuccessfulAttemptId")
        provider_scope_use = scopes & PROVIDER_SCOPES
        if gate_id not in REAL_PROVIDER_GATE_IDS:
            if consumed != 0 or provider_scope_use or successful_attempt_id is not None:
                problems.add(
                    "PROVIDER_GATE_UNAUTHORIZED",
                    document.relative_path,
                    "only W84-G6/G7/G8 and W92-G7 may consume real provider turns or scopes",
                )
            continue
        if provider_scope_use and (not _is_int(consumed) or consumed <= 0):
            problems.add(
                "PROVIDER_SCOPE_ZERO_TURN",
                document.relative_path,
                "provider-backed scope requires real consumed turns",
            )
        if _is_int(consumed) and consumed > 0 and not provider_scope_use:
            problems.add(
                "PROVIDER_TURN_WITHOUT_SCOPE",
                document.relative_path,
                "consumed provider turns require a provider-backed scope",
            )
        if gate.get("status") in {"NotRun", "NotApplicable"} and (
            consumed != 0 or provider_scope_use or successful_attempt_id is not None
        ):
            problems.add(
                "PROVIDER_NOTRUN_CONSUMPTION",
                document.relative_path,
                "NotRun/NotApplicable provider Gate cannot consume turns or name an attempt",
            )
    for gate_id, requirement in PROVIDER_GATE_REQUIREMENTS.items():
        document = gate_by_id.get(gate_id)
        if document is None:
            continue
        gate = _mapping(document.data)
        if gate.get("status") != "Passed":
            continue
        authorization = _mapping(gate.get("authorization"))
        if not isinstance(
            authorization.get("providerSuccessfulAttemptId"), str
        ):
            problems.add(
                "PROVIDER_SUCCESS_ATTEMPT",
                document.relative_path,
                "Passed provider Gate requires providerSuccessfulAttemptId",
            )
        consumed = authorization.get("providerTurnsConsumed")
        minimum = requirement["minTurns"]
        if not _is_int(consumed) or consumed < minimum:
            problems.add(
                "PROVIDER_REQUIRED_TURNS",
                document.relative_path,
                f"Passed {gate_id} requires at least {minimum} real provider turn(s)",
            )
        scopes = set(_list(authorization.get("scopesUsed")))
        missing_scopes = set(requirement.get("scopes", set())) - scopes
        extra_provider_scopes = (scopes & PROVIDER_SCOPES) - set(
            requirement.get("scopes", set())
        )
        if missing_scopes or extra_provider_scopes:
            problems.add(
                "PROVIDER_REQUIRED_SCOPE",
                document.relative_path,
                "Passed provider Gate scopes must exactly match its frozen provider scopes",
            )
        required_receipts = set(requirement.get("receipts", set()))
        if required_receipts:
            receipt_evidence = {
                Path(str(evidence.get("path"))).name: evidence
                for evidence in _list(gate.get("evidence"))
                if isinstance(evidence, Mapping)
            }
            missing_receipts = sorted(required_receipts - set(receipt_evidence))
            if missing_receipts:
                problems.add(
                    "PROVIDER_RECEIPT_MISSING",
                    document.relative_path,
                    "Passed provider Gate is missing one or more frozen provider receipts",
                )
            if repo_root is not None:
                run_ids: set[str] = set()
                for receipt_name in sorted(required_receipts & set(receipt_evidence)):
                    evidence = receipt_evidence[receipt_name]
                    receipt_path = _safe_relative_evidence_path(
                        repo_root,
                        evidence.get("path"),
                        problems,
                        f"{document.relative_path}#/provider-receipt/{receipt_name}",
                    )
                    if receipt_path is None or not receipt_path.is_file():
                        continue
                    receipt_document = read_document(repo_root, receipt_path, problems)
                    receipt = (
                        _mapping(receipt_document.data)
                        if receipt_document is not None
                        else {}
                    )
                    if receipt.get("status") != "Passed":
                        problems.add("PROVIDER_RECEIPT", receipt_name, "provider receipt status must be Passed")
                    expected_turns = {
                        "provider-readonly.json": 1,
                        "provider-recovery.json": 2,
                        "controlled-write.json": 1,
                    }.get(receipt_name, 6)
                    turns = _receipt_value(receipt, "turnsConsumed", "turnCount")
                    if turns != expected_turns:
                        problems.add("PROVIDER_RECEIPT_TURNS", receipt_name, f"receipt must prove exactly {expected_turns} turns")
                    profile_match = re.fullmatch(
                        r"provider-resource-profile-([1-5])\.json",
                        receipt_name,
                    )
                    if profile_match:
                        ordinal = int(profile_match.group(1))
                        if receipt.get("profileOrdinal") != ordinal:
                            problems.add("PROVIDER_PROFILE_ORDINAL", receipt_name, f"profileOrdinal must be {ordinal}")
                        if receipt.get("warmupTurns") != 1 or receipt.get("measuredTurns") != 5:
                            problems.add("PROVIDER_PROFILE_SHAPE", receipt_name, "profile must contain 1 warm-up + 5 measured turns")
                        run_id = receipt.get("continuousRunId")
                        if not isinstance(run_id, str) or not run_id:
                            problems.add("PROVIDER_PROFILE_RUN", receipt_name, "continuousRunId is required")
                        else:
                            run_ids.add(run_id)
                if gate_id in {"W84-G7", "W92-G7"} and len(run_ids) != 1:
                    problems.add("PROVIDER_PROFILE_CONTINUITY", document.relative_path, "five resource profiles must share one continuousRunId")


def _receipt_value(receipt: Mapping[str, Any], *names: str) -> Any:
    for name in names:
        if name in receipt:
            return receipt[name]
    return None


def _validate_controlled_write_tombstone(
    receipt: Mapping[str, Any],
    gate_id: str,
    product_candidate: Any,
    precondition_bindings: Sequence[Any],
    repo_root: Path | None,
    problems: Problems,
    location: str,
) -> None:
    binding = receipt.get("controlledWriteTombstone")
    expected_path = f"{CONTROLLED_WRITE_TOMBSTONE_DIR}/{gate_id}.json"
    if (
        not isinstance(binding, Mapping)
        or set(binding) != {"path", "sha256", "commit"}
        or binding.get("path") != expected_path
        or not isinstance(binding.get("sha256"), str)
        or HEX_SHA256.fullmatch(str(binding.get("sha256"))) is None
        or not isinstance(binding.get("commit"), str)
        or re.fullmatch(r"[0-9a-f]{40}", str(binding.get("commit"))) is None
    ):
        problems.add(
            "CONTROLLED_WRITE_TOMBSTONE_BINDING",
            location,
            "receipt must bind the fixed tombstone path, raw SHA-256, and commit",
        )
        return
    if repo_root is None:
        return

    tombstone_path = _safe_relative_evidence_path(
        repo_root,
        expected_path,
        problems,
        f"{location}#/controlledWriteTombstone/path",
    )
    if tombstone_path is None or not tombstone_path.is_file():
        problems.add(
            "CONTROLLED_WRITE_TOMBSTONE_MISSING",
            expected_path,
            "controlled-write tombstone must exist",
        )
        return
    tombstone_document = read_document(repo_root, tombstone_path, problems)
    if tombstone_document is None or not isinstance(tombstone_document.data, Mapping):
        problems.add(
            "CONTROLLED_WRITE_TOMBSTONE_SHAPE",
            expected_path,
            "controlled-write tombstone must be a JSON object",
        )
        return
    tombstone = tombstone_document.data
    expected_keys = {
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
    lease = _mapping(receipt.get("singleUseLease"))
    preauthorization_binding = tombstone.get("preauthorizationBinding")
    if (
        set(tombstone) != expected_keys
        or tombstone.get("schemaVersion") != SCHEMA_VERSION
        or tombstone.get("goalId") != GOAL_ID
        or tombstone.get("gateId") != gate_id
        or tombstone.get("productCandidate") != product_candidate
        or tombstone.get("leaseId") != lease.get("leaseId")
        or tombstone.get("nonceSha256") != lease.get("nonceSha256")
        or tombstone.get("issuedAt") != lease.get("issuedAt")
        or tombstone.get("consumedAt") != lease.get("consumedAt")
        or tombstone.get("preconditionGateBindings") != list(precondition_bindings)
        or not isinstance(preauthorization_binding, Mapping)
        or set(_mapping(preauthorization_binding))
        != {"path", "sha256", "commit"}
    ):
        problems.add(
            "CONTROLLED_WRITE_TOMBSTONE_SHAPE",
            expected_path,
            "tombstone must exactly bind Gate, candidate, lease, and preconditions",
        )
    controlled_bundle = (
        _validate_controlled_authorization_bundle(
            repo_root,
            gate_id,
            str(product_candidate),
            problems,
            location,
        )
        if isinstance(product_candidate, str)
        else None
    )
    if controlled_bundle is None:
        problems.add(
            "CONTROLLED_WRITE_PREAUTHORIZATION",
            expected_path,
            "tombstone requires its exact immutable preauthorization and approval decisions",
        )
    elif (
        binding != controlled_bundle[1]
        or preauthorization_binding != controlled_bundle[2]
    ):
        problems.add(
            "CONTROLLED_WRITE_PREAUTHORIZATION",
            expected_path,
            "tombstone/preauthorization bindings do not match their immutable control bundle",
        )
    try:
        current_raw = tombstone_path.read_bytes()
    except OSError:
        current_raw = b""
    if hashlib.sha256(current_raw).hexdigest() != binding.get("sha256"):
        problems.add(
            "CONTROLLED_WRITE_TOMBSTONE_HASH",
            expected_path,
            "tombstone binding does not match current raw bytes",
        )

    tracked = _git_stdout(
        repo_root, ("ls-files", "--error-unmatch", "--", expected_path)
    )
    dirty = _git_stdout(
        repo_root,
        ("status", "--porcelain=v1", "--untracked-files=all", "--", expected_path),
    )
    first_commit = _single_add_commit(repo_root, expected_path)
    history = _git_stdout(
        repo_root, ("log", "--format=%H", "--reverse", "--", expected_path)
    )
    if tracked is None or dirty is None or first_commit is None or history is None:
        problems.add(
            "CONTROLLED_WRITE_TOMBSTONE_GIT",
            expected_path,
            "tombstone must be tracked with exactly one readable first-add commit",
        )
        return
    history_commits = [value for value in history.splitlines() if value]
    first_raw = _git_bytes(repo_root, ("show", f"{first_commit}:{expected_path}"))
    head_raw = _git_bytes(repo_root, ("show", f"HEAD:{expected_path}"))
    index_raw = _git_bytes(repo_root, ("show", f":{expected_path}"))
    if history_commits != [first_commit]:
        problems.add(
            "CONTROLLED_WRITE_TOMBSTONE_HISTORY",
            expected_path,
            "tombstone changed after its exact first-add commit",
        )
    if (
        dirty
        or first_raw is None
        or first_raw != current_raw
        or head_raw != first_raw
        or index_raw != first_raw
    ):
        problems.add(
            "CONTROLLED_WRITE_TOMBSTONE_IMMUTABLE",
            expected_path,
            "worktree, index, HEAD, and first-add tombstone bytes must match",
        )
    declared_commit = str(binding.get("commit"))
    if declared_commit != first_commit:
        problems.add(
            "CONTROLLED_WRITE_TOMBSTONE_COMMIT",
            expected_path,
            "receipt commit must be the tombstone's only first-add commit",
        )
    if (
        not isinstance(product_candidate, str)
        or product_candidate == declared_commit
        or _git_return_code(
            repo_root,
            ("merge-base", "--is-ancestor", str(product_candidate), declared_commit),
        )
        != 0
    ):
        problems.add(
            "CONTROLLED_WRITE_TOMBSTONE_ANCESTRY",
            expected_path,
            "product candidate must be a strict ancestor of the tombstone commit",
        )


def _validate_controlled_write_receipt_payload(
    receipt: Mapping[str, Any],
    location: str,
    gate_id: str,
    week: int,
    product_candidate: Any,
    successful_attempt_id: Any,
    gate_by_id: Mapping[str, Document],
    problems: Problems,
    repo_root: Path | None = None,
) -> None:
    alias_groups = (
        ("workspaceName", "workspace"),
        ("harnessOwned", "workspaceOwnedByHarness"),
        ("onlyFile", "targetFile"),
        ("fromValue", "before"),
        ("toValue", "after"),
        ("validationCommand", "redactedValidationCommand"),
        ("validationCommandCount", "commandCount"),
        ("turnsConsumed", "turnCount"),
        ("durableApprovalCount", "approvalCount"),
        ("nonWritePreconditionsPassed", "allNonWriteGatesPassed"),
        ("executionCount", "writeExecutionCount"),
    )
    if any(sum(name in receipt for name in names) > 1 for names in alias_groups):
        problems.add(
            "CONTROLLED_WRITE_ALIAS_COLLISION",
            location,
            "controlled-write receipt cannot declare canonical and alias fields together",
        )
    if receipt.get("status") != "Passed":
        problems.add("CONTROLLED_WRITE_RECEIPT", location, "receipt status must be Passed")
    if receipt.get("gateId") != gate_id or receipt.get("week") != week:
        problems.add("CONTROLLED_WRITE_RECEIPT", location, "receipt has the wrong Gate/week binding")
    if (
        not isinstance(product_candidate, str)
        or not product_candidate
        or receipt.get("productCandidate") != product_candidate
    ):
        problems.add("CONTROLLED_WRITE_RECEIPT", location, "receipt is not bound to the exact product candidate")
    if (
        not isinstance(successful_attempt_id, str)
        or receipt.get("attemptId") != successful_attempt_id
    ):
        problems.add(
            "CONTROLLED_WRITE_RECEIPT",
            location,
            "receipt must bind the Gate's final successful provider attempt",
        )
    write_started_at = _timestamp(receipt.get("writeStartedAt"))
    if write_started_at is None:
        problems.add(
            "CONTROLLED_WRITE_RECEIPT",
            location,
            "receipt requires an absolute writeStartedAt timestamp",
        )
    lease = _mapping(receipt.get("singleUseLease"))
    lease_issued_at = _timestamp(lease.get("issuedAt"))
    lease_consumed_at = _timestamp(lease.get("consumedAt"))
    gate_document = gate_by_id.get(gate_id)
    gate_data = _mapping(gate_document.data if gate_document is not None else {})
    gate_started_at = _timestamp(gate_data.get("startedAt"))
    gate_finished_at = _timestamp(gate_data.get("finishedAt"))
    if (
        lease.get("status") != "Consumed"
        or lease.get("gateId") != gate_id
        or lease.get("productCandidate") != product_candidate
        or lease.get("executionCount") != 1
        or not isinstance(lease.get("leaseId"), str)
        or re.fullmatch(r"CW-(?:W84|W92)-[A-Z0-9]{8,32}", str(lease.get("leaseId")))
        is None
        or not isinstance(lease.get("nonceSha256"), str)
        or HEX_SHA256.fullmatch(str(lease.get("nonceSha256"))) is None
        or lease_issued_at is None
        or lease_consumed_at is None
        or write_started_at is None
        or gate_started_at is None
        or gate_finished_at is None
        or not (
            gate_started_at
            <= lease_issued_at
            < lease_consumed_at
            <= write_started_at
            <= gate_finished_at
        )
    ):
        problems.add(
            "CONTROLLED_WRITE_LEASE",
            location,
            "controlled write lease/tombstone times must be ordered inside the Gate",
        )

    group = next(
        (
            candidate_group
            for candidate_group in _canonical_groups_without_patterns()
            if gate_id in candidate_group.gate_ids
        ),
        None,
    )
    expected_prerequisite_ids = (
        list(group.gate_ids[: group.gate_ids.index(gate_id)])
        if group is not None
        else []
    )
    bindings = _list(receipt.get("preconditionGateBindings"))
    actual_ids = [
        _mapping(binding).get("gateId") for binding in bindings
    ]
    if actual_ids != expected_prerequisite_ids:
        problems.add(
            "CONTROLLED_WRITE_PRECONDITION_SET",
            location,
            "receipt must bind every preceding non-write Gate in canonical order",
        )
    for index, prerequisite_id in enumerate(expected_prerequisite_ids):
        binding = _mapping(bindings[index] if index < len(bindings) else {})
        prerequisite = gate_by_id.get(prerequisite_id)
        prerequisite_data = _mapping(
            prerequisite.data if prerequisite is not None else {}
        )
        prerequisite_candidate = _mapping(
            prerequisite_data.get("identity")
        ).get("productCandidate")
        prerequisite_finished_at = _timestamp(prerequisite_data.get("finishedAt"))
        candidate_ancestry_valid = (
            repo_root is None
            or (
                isinstance(prerequisite_candidate, str)
                and isinstance(product_candidate, str)
                and _git_return_code(
                    repo_root,
                    (
                        "merge-base",
                        "--is-ancestor",
                        prerequisite_candidate,
                        product_candidate,
                    ),
                )
                == 0
            )
        )
        if (
            prerequisite is None
            or set(binding)
            != {
                "gateId",
                "productCandidate",
                "resultPath",
                "resultSha256",
                "status",
                "finishedAt",
            }
            or binding.get("gateId") != prerequisite_id
            or binding.get("productCandidate") != prerequisite_candidate
            or binding.get("resultPath") != prerequisite.relative_path
            or binding.get("resultSha256") != prerequisite.sha256
            or binding.get("status") != "Passed"
            or prerequisite_data.get("status") != "Passed"
            or binding.get("finishedAt") != prerequisite_data.get("finishedAt")
            or prerequisite_finished_at is None
            or write_started_at is None
            or prerequisite_finished_at >= write_started_at
            or not candidate_ancestry_valid
        ):
            problems.add(
                "CONTROLLED_WRITE_PRECONDITION_BINDING",
                f"{location}#/preconditionGateBindings/{index}",
                "precondition binding must match a Passed Gate completed before the write",
            )
    _validate_controlled_write_tombstone(
        receipt,
        gate_id,
        product_candidate,
        bindings,
        repo_root,
        problems,
        location,
    )
    workspace = _receipt_value(receipt, "workspaceName", "workspace")
    if (
        not isinstance(workspace, str)
        or "/" in workspace
        or "\\" in workspace
        or re.fullmatch(
            rf"caicli-week{week}-write-[A-Za-z0-9._-]+", workspace
        )
        is None
    ):
        problems.add("CONTROLLED_WRITE_RECEIPT", location, "invalid or unredacted harness workspace name")
    owned = _receipt_value(receipt, "harnessOwned", "workspaceOwnedByHarness")
    if owned is not True:
        problems.add("CONTROLLED_WRITE_RECEIPT", location, "workspace ownership is not harness-owned")
    files = receipt.get("files")
    only_file = _receipt_value(receipt, "onlyFile", "targetFile")
    if only_file != "result.txt" or files != ["result.txt"]:
        problems.add("CONTROLLED_WRITE_RECEIPT", location, "only result.txt may be written")
    if _receipt_value(receipt, "fromValue", "before") != "fail":
        problems.add("CONTROLLED_WRITE_RECEIPT", location, "receipt must prove result.txt fail before write")
    if _receipt_value(receipt, "toValue", "after") != "pass":
        problems.add("CONTROLLED_WRITE_RECEIPT", location, "receipt must prove result.txt pass after write")
    command = _receipt_value(receipt, "validationCommand", "redactedValidationCommand")
    command_count = _receipt_value(receipt, "validationCommandCount", "commandCount")
    if (
        command != "dotnet msbuild Week82Gate.proj -target:Test -nologo"
        or command_count != 1
    ):
        problems.add("CONTROLLED_WRITE_RECEIPT", location, "exactly one frozen dotnet msbuild validation command is required")
    if receipt.get("dotnetSdk") != "9.0.308":
        problems.add("CONTROLLED_WRITE_RECEIPT", location, "controlled write must use frozen .NET SDK 9.0.308")
    if _receipt_value(receipt, "turnsConsumed", "turnCount") != 1:
        problems.add("CONTROLLED_WRITE_RECEIPT", location, "controlled write receipt must prove one provider turn")
    approvals = receipt.get("durableApprovals")
    approval_count = _receipt_value(receipt, "durableApprovalCount", "approvalCount")
    if (
        approval_count != 2
        or not isinstance(approvals, list)
        or len(approvals) != 2
        or not all(isinstance(item, Mapping) for item in approvals)
    ):
        problems.add("CONTROLLED_WRITE_RECEIPT", location, "exactly two durable approvals are required")
    if isinstance(approvals, list):
        approval_items = [item for item in approvals if isinstance(item, Mapping)]
        approval_ids = [item.get("approvalId") for item in approval_items]
        if len(approval_ids) != 2 or None in approval_ids or len(set(approval_ids)) != 2:
            problems.add("CONTROLLED_WRITE_RECEIPT", location, "durable approvals must have two distinct IDs")
        for item in approval_items:
            if (
                item.get("decision") != "Approve"
                or item.get("durable") is not True
                or item.get("usedOnce") is not True
                or item.get("replayed") is not False
            ):
                problems.add(
                    "CONTROLLED_WRITE_RECEIPT",
                    location,
                    "each durable approval must be approved, used once, and never replayed",
                )
        if {item.get("action") for item in approval_items} != {
            "apply_patch",
            "shell",
        }:
            problems.add(
                "CONTROLLED_WRITE_RECEIPT",
                location,
                "durable approvals must separately authorize apply_patch and shell",
            )
    if _receipt_value(
        receipt,
        "nonWritePreconditionsPassed",
        "allNonWriteGatesPassed",
    ) is not True:
        problems.add("CONTROLLED_WRITE_RECEIPT", location, "non-write preconditions are not proven Passed")
    if _receipt_value(receipt, "executionCount", "writeExecutionCount") != 1:
        problems.add("CONTROLLED_WRITE_RECEIPT", location, "receipt must prove exactly one write execution")
    cleanup = receipt.get("cleanup")
    cleanup_fields = (
        "ownedProcessesRemaining",
        "ownedTempPathsRemaining",
        "configMutationsRemaining",
        "residueCount",
    )
    cleanup_passed = (
        isinstance(cleanup, Mapping)
        and cleanup.get("status") == "Passed"
        and all(field in cleanup and cleanup.get(field) == 0 for field in cleanup_fields)
    )
    if not cleanup_passed:
        problems.add("CONTROLLED_WRITE_RECEIPT", location, "owned workspace cleanup is not proven Passed")


def validate_controlled_write_receipts(
    gate_by_id: Mapping[str, Document],
    repo_root: Path | None,
    problems: Problems,
    *,
    candidate_root: Path | None = None,
) -> None:
    for gate_id in CONTROLLED_WRITE_GATES.values():
        document = gate_by_id.get(gate_id)
        if document is None:
            continue
        gate = _mapping(document.data)
        authorization = _mapping(gate.get("authorization"))
        if authorization.get("controlledWriteUsed") is not True:
            continue
        controlled_refs = set(_list(authorization.get("controlledWriteEvidenceRefs")))
        referenced_json = [
            evidence
            for evidence in _list(gate.get("evidence"))
            if isinstance(evidence, Mapping)
            and evidence.get("evidenceId") in controlled_refs
            and evidence.get("kind") == "json"
        ]
        evidence_items = [
            evidence
            for evidence in referenced_json
            if Path(str(evidence.get("path"))).name == "controlled-write.json"
        ]
        if gate_id == "W92-G7" and not evidence_items and len(referenced_json) == 1:
            evidence_items = referenced_json
        if len(evidence_items) != 1:
            problems.add(
                "CONTROLLED_WRITE_RECEIPT_MISSING",
                document.relative_path,
                "controlled write requires exactly one controlled-write.json receipt",
            )
            continue
        receipt_evidence = evidence_items[0]
        receipt_id = receipt_evidence.get("evidenceId")
        if receipt_id not in controlled_refs:
            problems.add(
                "CONTROLLED_WRITE_RECEIPT_REF",
                document.relative_path,
                "controlledWriteEvidenceRefs must reference the receipt evidenceId",
            )
        if repo_root is None:
            continue
        receipt_path = _safe_relative_evidence_path(
            repo_root,
            receipt_evidence.get("path"),
            problems,
            f"{document.relative_path}#/controlled-write-receipt",
        )
        if receipt_path is None or not receipt_path.is_file():
            continue
        receipt_document = read_document(repo_root, receipt_path, problems)
        if receipt_document is None or not isinstance(receipt_document.data, Mapping):
            problems.add("CONTROLLED_WRITE_RECEIPT", document.relative_path, "receipt must be a JSON object")
            continue
        _validate_controlled_write_receipt_payload(
            receipt_document.data,
            receipt_document.relative_path,
            gate_id,
            int(gate.get("week")) if _is_int(gate.get("week")) else -1,
            _mapping(gate.get("identity")).get("productCandidate"),
            authorization.get("providerSuccessfulAttemptId"),
            gate_by_id,
            problems,
            candidate_root or repo_root,
        )

    if repo_root is not None:
        _validate_global_controlled_runtime_ids(gate_by_id, repo_root, problems)


def _walk_controlled_runtime_ids(
    value: Any,
    keys: frozenset[str],
) -> Iterable[tuple[str, Any]]:
    if isinstance(value, Mapping):
        for key, item in value.items():
            if key in keys:
                yield key, item
            yield from _walk_controlled_runtime_ids(item, keys)
    elif isinstance(value, list):
        for item in value:
            yield from _walk_controlled_runtime_ids(item, keys)


def _validate_global_controlled_runtime_ids(
    gate_by_id: Mapping[str, Document],
    repo_root: Path,
    problems: Problems,
) -> None:
    """Reject replayed lease, nonce, or approval IDs across all JSON evidence."""

    keys = frozenset({"leaseId", "nonceSha256", "approvalId"})
    seen: dict[tuple[str, str], str] = {}
    for gate_id in CANONICAL_GATE_IDS:
        document = gate_by_id.get(gate_id)
        if document is None:
            continue
        for evidence in _list(_mapping(document.data).get("evidence")):
            if (
                not isinstance(evidence, Mapping)
                or (
                    evidence.get("kind") not in {
                        "json",
                        "test-report",
                        "manual-attestation",
                    }
                    and not str(evidence.get("path", "")).lower().endswith(
                        ".json"
                    )
                )
            ):
                continue
            evidence_path = _safe_relative_evidence_path(
                repo_root,
                evidence.get("path"),
                problems,
                f"{document.relative_path}#/runtime-id-scan",
            )
            if evidence_path is None or not evidence_path.is_file():
                continue
            evidence_document = read_document(repo_root, evidence_path, problems)
            if evidence_document is None:
                continue
            for key, value in _walk_controlled_runtime_ids(
                evidence_document.data, keys
            ):
                if not isinstance(value, str) or not value:
                    problems.add(
                        "CONTROLLED_RUNTIME_ID_SHAPE",
                        evidence_document.relative_path,
                        "lease, nonce, and approval IDs must be non-empty strings",
                    )
                    continue
                identity = (key, value)
                if identity in seen:
                    problems.add(
                        "CONTROLLED_RUNTIME_ID_REPLAY",
                        evidence_document.relative_path,
                        "lease, nonce, and approval IDs must be globally unique across evidence",
                    )
                else:
                    seen[identity] = evidence_document.relative_path


def _validate_w91_checklist_payload(
    payload: Mapping[str, Any],
    expected_ids: Sequence[str],
    product_candidate: Any,
    gate_started_at: datetime | None,
    gate_finished_at: datetime | None,
    problems: Problems,
    location: str,
) -> None:
    if (
        set(payload)
        != {
            "checklistRevision",
            "productCandidate",
            "performedBy",
            "status",
            "items",
        }
        or payload.get("checklistRevision") != W91_CHECKLIST_REVISION
        or payload.get("productCandidate") != product_candidate
        or payload.get("performedBy") != "GoalTestOperator"
        or payload.get("status") != "Passed"
    ):
        problems.add(
            "W91_OPERATOR_CHECKLIST_HEADER",
            location,
            "operator checklist header must bind the frozen revision and exact candidate",
        )
    items = payload.get("items")
    if not isinstance(items, list) or len(items) != len(expected_ids):
        problems.add(
            "W91_OPERATOR_CHECKLIST_SET",
            location,
            "operator checklist must contain its exact ordered item set",
        )
        return
    actual_ids = [
        item.get("checklistId") if isinstance(item, Mapping) else None
        for item in items
    ]
    if actual_ids != list(expected_ids):
        problems.add(
            "W91_OPERATOR_CHECKLIST_SET",
            location,
            "operator checklist must contain its exact ordered item set",
        )
    expected_item_keys = {
        "checklistId",
        "status",
        "observations",
        "observedAt",
        "issuePointers",
    }
    for index, item in enumerate(items):
        item_location = f"{location}#/items/{index}"
        item_mapping = _mapping(item)
        observed_at = _timestamp(item_mapping.get("observedAt"))
        observations = item_mapping.get("observations")
        if (
            not isinstance(item, Mapping)
            or set(item) != expected_item_keys
            or item_mapping.get("status") != "Passed"
            or not isinstance(observations, str)
            or not observations.strip()
            or observed_at is None
            or gate_started_at is None
            or gate_finished_at is None
            or not (gate_started_at <= observed_at <= gate_finished_at)
            or item_mapping.get("issuePointers") != []
        ):
            problems.add(
                "W91_OPERATOR_CHECKLIST_ITEM",
                item_location,
                "each checklist item requires a real in-Gate Passed observation and no issues",
            )


def validate_w91_operator_evidence(
    gate_by_id: Mapping[str, Document],
    repo_root: Path | None,
    problems: Problems,
) -> None:
    document = gate_by_id.get("W91-G2")
    if document is None:
        return
    gate = _mapping(document.data)
    if gate.get("status") != "Passed":
        return
    authorization = _mapping(gate.get("authorization"))
    if set(_list(authorization.get("scopesUsed"))) != {
        "operator-narrator-manual-ux"
    }:
        problems.add(
            "W91_OPERATOR_SCOPE",
            document.relative_path,
            "Passed W91-G2 requires only the Goal-owned operator Narrator/UX scope",
        )
    expected_by_name = {
        "narrator-manual.json": W91_NARRATOR_CHECKLIST_IDS,
        "operator-ux-attestation.json": W91_OPERATOR_UX_CHECKLIST_IDS,
    }
    evidence_items = [
        item
        for item in _list(gate.get("evidence"))
        if isinstance(item, Mapping)
    ]
    candidate = _mapping(gate.get("identity")).get("productCandidate")
    gate_started_at = _timestamp(gate.get("startedAt"))
    gate_finished_at = _timestamp(gate.get("finishedAt"))
    for basename, expected_ids in expected_by_name.items():
        matches = [
            item
            for item in evidence_items
            if Path(str(item.get("path"))).name == basename
            and item.get("kind") in {"json", "manual-attestation"}
        ]
        if len(matches) != 1:
            problems.add(
                "W91_OPERATOR_EVIDENCE_SET",
                document.relative_path,
                "Passed W91-G2 requires one exact Narrator and operator UX evidence file",
            )
            continue
        if repo_root is None:
            continue
        path = _safe_relative_evidence_path(
            repo_root,
            matches[0].get("path"),
            problems,
            f"{document.relative_path}#/w91-operator/{basename}",
        )
        payload_document = (
            read_document(repo_root, path, problems)
            if path is not None and path.is_file()
            else None
        )
        if payload_document is None or not isinstance(
            payload_document.data, Mapping
        ):
            problems.add(
                "W91_OPERATOR_EVIDENCE_READ",
                document.relative_path,
                "operator checklist evidence must be readable JSON",
            )
            continue
        _validate_w91_checklist_payload(
            payload_document.data,
            expected_ids,
            candidate,
            gate_started_at,
            gate_finished_at,
            problems,
            payload_document.relative_path,
        )


def _expected_w91_visual_bindings(
    gate_by_id: Mapping[str, Document], repo_root: Path
) -> tuple[list[dict[str, Any]], bool]:
    w91_document = gate_by_id.get("W91-G2")
    w91_gate = _mapping(w91_document.data if w91_document is not None else {})
    evidence = [
        item
        for item in _list(w91_gate.get("evidence"))
        if isinstance(item, Mapping)
    ]
    bindings: list[dict[str, Any]] = []
    valid = w91_document is not None and w91_gate.get("status") == "Passed"
    for basename in ("narrator-manual.json", "operator-ux-attestation.json"):
        matches = [
            item
            for item in evidence
            if Path(str(item.get("path"))).name == basename
        ]
        if len(matches) != 1:
            valid = False
            bindings.append({"path": None, "sha256": None})
        else:
            bindings.append(
                {"path": matches[0].get("path"), "sha256": matches[0].get("sha256")}
            )
    anchor_relative = "docs_md/weekly/84_92_evidence_anchors/w91-hardening.json"
    anchor_path = repo_root / anchor_relative
    anchor_sha = (
        hashlib.sha256(anchor_path.read_bytes()).hexdigest()
        if anchor_path.is_file()
        else None
    )
    if anchor_sha is None:
        valid = False
    bindings.append({"path": anchor_relative, "sha256": anchor_sha})
    return bindings, valid


def _validate_w92_narrator_manifest_binding(
    manifest: Mapping[str, Any],
    gate_by_id: Mapping[str, Document],
    repo_root: Path,
    problems: Problems,
    location: str,
) -> None:
    expected, valid = _expected_w91_visual_bindings(gate_by_id, repo_root)
    if not valid or manifest.get("w91EvidenceBindings") != expected:
        problems.add(
            "W92_NARRATOR_REVIEW_BINDING",
            location,
            "W92 manifest must bind the Passed W91 operator/Narrator evidence and hardening anchor",
        )


def _validate_w92_visual_manifest(
    manifest: Mapping[str, Any],
    gate_document: Document,
    gate_by_id: Mapping[str, Document],
    repo_root: Path,
    problems: Problems,
    location: str,
) -> None:
    gate = _mapping(gate_document.data)
    candidate = _mapping(gate.get("identity")).get("productCandidate")
    expected_top = {
        "schemaVersion",
        "manifestRole",
        "gateId",
        "productCandidate",
        "viewports",
        "fixtures",
        "redactionAttestation",
        "files",
        "w91EvidenceBindings",
    }
    if (
        set(manifest) != expected_top
        or manifest.get("schemaVersion") != SCHEMA_VERSION
        or manifest.get("manifestRole") != "week92-final-user-visual"
        or manifest.get("gateId") != "W92-G9"
        or manifest.get("productCandidate") != candidate
        or manifest.get("viewports") != list(W92_VISUAL_VIEWPORTS)
        or manifest.get("fixtures") != list(W92_VISUAL_FIXTURES)
    ):
        problems.add(
            "W92_VISUAL_MANIFEST_HEADER",
            location,
            "Week92 visual manifest must use the exact frozen role, candidate, viewports, and fixtures",
        )
    attestation = _mapping(manifest.get("redactionAttestation"))
    reviewed_at = _timestamp(attestation.get("reviewedAt"))
    gate_started_at = _timestamp(gate.get("startedAt"))
    gate_finished_at = _timestamp(gate.get("finishedAt"))
    if (
        set(attestation)
        != {
            "status",
            "reviewedBy",
            "secretsVisible",
            "absolutePathsVisible",
            "reviewedAt",
        }
        or attestation.get("status") != "Passed"
        or attestation.get("reviewedBy") != "GoalTestOperator"
        or attestation.get("secretsVisible") is not False
        or attestation.get("absolutePathsVisible") is not False
        or reviewed_at is None
        or gate_started_at is None
        or gate_finished_at is None
        or not (gate_started_at <= reviewed_at <= gate_finished_at)
    ):
        problems.add(
            "W92_VISUAL_REDACTION_ATTESTATION",
            location,
            "Week92 redaction review must be complete and inside the Gate window",
        )
    files = manifest.get("files")
    expected_count = (
        len(W92_VISUAL_VIEWPORTS)
        * len(W92_VISUAL_FIXTURES)
        * len(W92_VISUAL_ARTIFACT_TYPES)
    )
    if not isinstance(files, list) or len(files) != expected_count:
        problems.add(
            "W92_VISUAL_FILE_COUNT",
            location,
            "Week92 visual manifest requires exactly 15 PNG and 30 supporting snapshots",
        )
        files = _list(files)
    expected_item_keys = {
        "path",
        "sha256",
        "redacted",
        "artifactType",
        "viewport",
        "fixture",
    }
    expected_matrix = {
        (artifact_type, viewport, fixture)
        for artifact_type in W92_VISUAL_ARTIFACT_TYPES
        for viewport in W92_VISUAL_VIEWPORTS
        for fixture in W92_VISUAL_FIXTURES
    }
    actual_matrix: list[tuple[Any, Any, Any]] = []
    paths: list[str] = []
    basenames: list[str] = []
    screenshot_bindings: list[tuple[str, str]] = []
    for index, item in enumerate(files):
        item_location = f"{location}#/files/{index}"
        item_mapping = _mapping(item)
        path_value = item_mapping.get("path")
        sha_value = item_mapping.get("sha256")
        artifact_type = item_mapping.get("artifactType")
        viewport = item_mapping.get("viewport")
        fixture = item_mapping.get("fixture")
        if (
            not isinstance(item, Mapping)
            or set(item) != expected_item_keys
            or item_mapping.get("redacted") is not True
            or not isinstance(path_value, str)
            or not isinstance(sha_value, str)
            or HEX_SHA256.fullmatch(sha_value) is None
        ):
            problems.add(
                "W92_VISUAL_FILE_SHAPE",
                item_location,
                "visual file entry must use the exact redacted matrix binding shape",
            )
        actual_matrix.append((artifact_type, viewport, fixture))
        if isinstance(path_value, str):
            paths.append(path_value)
            basenames.append(Path(path_value).name)
        resolved = _safe_relative_evidence_path(
            repo_root, path_value, problems, f"{item_location}/path"
        )
        if resolved is None or not resolved.is_file():
            problems.add(
                "W92_VISUAL_FILE_MISSING",
                item_location,
                "manifest-bound visual artifact must exist",
            )
            continue
        raw = resolved.read_bytes()
        if hashlib.sha256(raw).hexdigest() != sha_value:
            problems.add(
                "W92_VISUAL_FILE_HASH",
                item_location,
                "manifest-bound visual artifact raw hash does not match",
            )
        if artifact_type == "screenshot":
            screenshot_bindings.append((str(path_value), str(sha_value)))
            if not _visual_magic_matches("screenshot", resolved, raw):
                problems.add(
                    "W92_VISUAL_SCREENSHOT_FORMAT",
                    item_location,
                    "Week92 screenshot must be a PNG",
                )
        elif artifact_type in {"dom-snapshot", "a11y-snapshot"}:
            support_document = read_document(repo_root, resolved, problems)
            support = _mapping(
                support_document.data if support_document is not None else {}
            )
            if (
                not support
                or support.get("productCandidate") != candidate
                or support.get("viewport") != viewport
                or support.get("fixture") != fixture
                or support.get("artifactType") != artifact_type
            ):
                problems.add(
                    "W92_VISUAL_SUPPORT_BINDING",
                    item_location,
                    "support snapshot must be a non-empty self-bound JSON object",
                )
    if set(actual_matrix) != expected_matrix or len(actual_matrix) != len(
        set(actual_matrix)
    ):
        problems.add(
            "W92_VISUAL_MATRIX",
            location,
            "Week92 visual files must cover every artifact/viewport/fixture combination exactly once",
        )
    if len(paths) != len(set(paths)) or len(basenames) != len(set(basenames)):
        problems.add(
            "W92_VISUAL_FILE_UNIQUENESS",
            location,
            "Week92 visual paths and basenames must be globally unique",
        )
    gate_screenshots = sorted(
        (
            str(item.get("path")),
            str(item.get("sha256")),
        )
        for item in _list(gate.get("evidence"))
        if isinstance(item, Mapping) and item.get("kind") == "screenshot"
    )
    if sorted(screenshot_bindings) != gate_screenshots or len(gate_screenshots) != 15:
        problems.add(
            "W92_VISUAL_SCREENSHOT_EVIDENCE",
            location,
            "all 15 PNG files must exactly bind the Gate screenshot evidence",
        )
    _validate_w92_narrator_manifest_binding(
        manifest, gate_by_id, repo_root, problems, location
    )


def validate_manual_acceptance_receipts(
    gate_by_id: Mapping[str, Document],
    handoff_documents: Sequence[Document],
    repo_root: Path | None,
    problems: Problems,
) -> None:
    for gate_id, requirement in MANUAL_GATE_REQUIREMENTS.items():
        document = gate_by_id.get(gate_id)
        if document is None:
            continue
        gate = _mapping(document.data)
        if gate.get("status") != "Passed":
            continue
        evidence_items = [
            evidence
            for evidence in _list(gate.get("evidence"))
            if isinstance(evidence, Mapping)
        ]
        receipt_matches = [
            evidence
            for evidence in evidence_items
            if Path(str(evidence.get("path"))).name == requirement["receipt"]
        ]
        manifest_matches = [
            evidence
            for evidence in evidence_items
            if Path(str(evidence.get("path"))).name == requirement["manifest"]
        ]
        receipt_evidence = receipt_matches[0] if len(receipt_matches) == 1 else None
        manifest_evidence = manifest_matches[0] if len(manifest_matches) == 1 else None
        if (
            receipt_evidence is None
            or receipt_evidence.get("kind") != "json"
        ):
            problems.add(
                "MANUAL_ACCEPTANCE_RECEIPT_MISSING",
                document.relative_path,
                "Passed manual Gate requires its hash-bound JSON user receipt",
            )
            continue
        if manifest_evidence is None:
            problems.add(
                "MANUAL_ACCEPTANCE_MANIFEST_MISSING",
                document.relative_path,
                "manual acceptance receipt requires its screenshot/checklist manifest",
            )
            continue
        if repo_root is None:
            continue
        receipt_path = _safe_relative_evidence_path(
            repo_root,
            receipt_evidence.get("path"),
            problems,
            f"{document.relative_path}#/manual-acceptance-receipt",
        )
        if receipt_path is None or not receipt_path.is_file():
            continue
        receipt_document = read_document(repo_root, receipt_path, problems)
        if receipt_document is None or not isinstance(receipt_document.data, Mapping):
            problems.add(
                "MANUAL_ACCEPTANCE_RECEIPT",
                document.relative_path,
                "manual acceptance receipt must be a JSON object",
            )
            continue
        receipt = receipt_document.data
        candidate = _mapping(gate.get("identity")).get("productCandidate")
        required_bindings = {
            "acceptanceId": requirement["acceptanceId"],
            "status": "Passed",
            "confirmedBy": "User",
            "productCandidate": candidate,
        }
        for field, expected in required_bindings.items():
            if receipt.get(field) != expected:
                problems.add(
                    "MANUAL_ACCEPTANCE_RECEIPT",
                    receipt_document.relative_path,
                    f"manual receipt field {field} is not bound to the accepted candidate",
                )
        for field in ("decidedAt", "userStatementSummary"):
            if not isinstance(receipt.get(field), str) or not receipt.get(field):
                problems.add(
                    "MANUAL_ACCEPTANCE_RECEIPT",
                    receipt_document.relative_path,
                    f"manual receipt requires non-empty {field}",
                )
        decision_request_id = receipt.get("decisionRequestId")
        challenge_code = receipt.get("challengeCode")
        user_response = receipt.get("userResponseCanonical")
        user_message_sha = receipt.get("userMessageSha256")
        if not isinstance(decision_request_id, str) or not decision_request_id:
            problems.add(
                "MANUAL_ACCEPTANCE_DECISION_BINDING",
                receipt_document.relative_path,
                "manual receipt requires a decisionRequestId",
            )
        if (
            not isinstance(challenge_code, str)
            or goal_integrity.CHALLENGE_CODE.fullmatch(challenge_code) is None
            or not isinstance(user_response, str)
            or user_response
            != goal_integrity.canonical_user_response(
                str(decision_request_id),
                challenge_code if isinstance(challenge_code, str) else "",
                "Passed",
            )
            or hashlib.sha256(user_response.encode("utf-8")).hexdigest()
            != user_message_sha
        ):
            problems.add(
                "MANUAL_ACCEPTANCE_RESPONSE_BINDING",
                receipt_document.relative_path,
                "manual receipt does not bind the exact challenge response",
            )
        if not isinstance(user_message_sha, str) or HEX_SHA256.fullmatch(
            user_message_sha.lower()
        ) is None:
            problems.add(
                "MANUAL_ACCEPTANCE_DECISION_BINDING",
                receipt_document.relative_path,
                "manual receipt requires a redacted userMessageSha256",
            )
        manifest_id = manifest_evidence.get("evidenceId")
        receipt_refs = _list(receipt.get("evidenceRefs"))
        gate_evidence_ids = {
            evidence.get("evidenceId") for evidence in evidence_items
        }
        if (
            not isinstance(manifest_id, str)
            or manifest_id not in receipt_refs
            or any(ref not in gate_evidence_ids for ref in receipt_refs)
        ):
            problems.add(
                "MANUAL_ACCEPTANCE_EVIDENCE",
                receipt_document.relative_path,
                "manual receipt evidenceRefs must resolve and include its manifest",
            )
        if receipt.get(requirement["hashField"]) != manifest_evidence.get("sha256"):
            problems.add(
                "MANUAL_ACCEPTANCE_MANIFEST_HASH",
                receipt_document.relative_path,
                "manual receipt manifest hash does not match Gate evidence",
            )
        manifest_path = _safe_relative_evidence_path(
            repo_root,
            manifest_evidence.get("path"),
            problems,
            f"{document.relative_path}#/manual-acceptance-manifest",
        )
        manifest_document = (
            read_document(repo_root, manifest_path, problems)
            if manifest_path is not None and manifest_path.is_file()
            else None
        )
        if gate_id == "W92-G9":
            if manifest_document is None or not isinstance(
                manifest_document.data, Mapping
            ):
                problems.add(
                    "W92_NARRATOR_REVIEW_BINDING",
                    document.relative_path,
                    "W92 screenshot manifest must be readable candidate-bound JSON",
                )
            else:
                _validate_w92_visual_manifest(
                    manifest_document.data,
                    document,
                    gate_by_id,
                    repo_root,
                    problems,
                    manifest_document.relative_path,
                )

        decision_view, decision_issues = goal_integrity.load_user_decision_ledger(
            repo_root,
            repo_root / goal_integrity.USER_DECISION_LEDGER_PATH,
        )
        for issue in decision_issues:
            problems.add(
                issue.code,
                issue.location,
                "user decision ledger validation failed",
            )
        if decision_view is not None:
            exact_decisions = [
                entry
                for entry in decision_view.entries
                if entry.get("decisionRequestId") == decision_request_id
                and entry.get("acceptanceId") == requirement["acceptanceId"]
                and entry.get("candidate") == candidate
                and entry.get("manifestSha256") == manifest_evidence.get("sha256")
                and entry.get("challengeCode") == challenge_code
                and entry.get("userResponseCanonical") == user_response
                and entry.get("userMessageSha256") == user_message_sha
                and entry.get("decidedAt") == receipt.get("decidedAt")
                and entry.get("status") == "Passed"
                and entry.get("confirmedBy") == "User"
            ]
            if len(exact_decisions) != 1:
                problems.add(
                    "MANUAL_ACCEPTANCE_DECISION_BINDING",
                    receipt_document.relative_path,
                    "manual receipt is not anchored by one exact user decision",
                )

        matching_handoffs = [
            handoff
            for handoff in handoff_documents
            if _mapping(handoff.data).get("week") == gate.get("week")
            and _mapping(handoff.data).get("lane") == gate.get("lane")
            and _mapping(handoff.data).get("decision")
            in {"ReadyForNextCheckpoint", "GoalComplete"}
        ]
        for handoff_document in matching_handoffs:
            handoff_acceptances = {
                item.get("acceptanceId"): item
                for item in _list(_mapping(handoff_document.data).get("userAcceptances"))
                if isinstance(item, Mapping)
            }
            handoff_record = handoff_acceptances.get(requirement["acceptanceId"])
            if handoff_record is None:
                problems.add(
                    "MANUAL_ACCEPTANCE_HANDOFF",
                    handoff_document.relative_path,
                    "Ready handoff omits the Gate's user acceptance",
                )
                continue
            for field in (
                "acceptanceId",
                "status",
                "decidedAt",
                "decisionRequestId",
                "challengeCode",
                "userResponseCanonical",
                "userMessageSha256",
                "confirmedBy",
                "userStatementSummary",
            ):
                if handoff_record.get(field) != receipt.get(field):
                    problems.add(
                        "MANUAL_ACCEPTANCE_HANDOFF",
                        handoff_document.relative_path,
                        f"handoff acceptance field {field} differs from the user receipt",
                    )
            if handoff_record.get("manifestSha256") != manifest_evidence.get(
                "sha256"
            ):
                problems.add(
                    "MANUAL_ACCEPTANCE_HANDOFF",
                    handoff_document.relative_path,
                    "handoff acceptance manifestSha256 differs from Gate evidence",
                )
            if set(_list(handoff_record.get("evidenceRefs"))) != set(receipt_refs):
                problems.add(
                    "MANUAL_ACCEPTANCE_HANDOFF",
                    handoff_document.relative_path,
                    "handoff acceptance evidenceRefs differ from the user receipt",
                )


def validate_handoff(
    document: Document,
    contract: Contract,
    gate_by_id: Mapping[str, Document],
    problems: Problems,
) -> None:
    data = document.data
    location = document.relative_path
    if not isinstance(data, Mapping):
        problems.add("HANDOFF_TYPE", location, "handoff must be an object")
        return
    week = data.get("week")
    lane = data.get("lane")
    group = contract.group_by_key.get((week, lane)) if _is_int(week) and isinstance(lane, str) else None
    if group is None:
        problems.add("HANDOFF_GROUP", location, f"invalid week/lane {week!r}/{lane!r}")
        return
    if data.get("checkpoint") != group.checkpoint:
        problems.add("HANDOFF_CHECKPOINT", location, f"checkpoint must be {group.checkpoint}")
    allowed_paths = {CANONICAL_HANDOFF_BY_GROUP[group.key]}
    if group.key == (84, "baseline"):
        allowed_paths.add(W84_COMPAT_HANDOFF_PATH)
    if location not in allowed_paths:
        problems.add(
            "HANDOFF_PATH_POLICY",
            location,
            "handoff file is not at its canonical Goal artifact path",
        )

    refs = _list(data.get("gateResults"))
    ref_by_id: dict[str, Mapping[str, Any]] = {}
    for index, ref in enumerate(refs):
        ref_location = f"{location}#/gateResults/{index}"
        if not isinstance(ref, Mapping):
            problems.add("HANDOFF_GATE_REF", ref_location, "Gate reference must be an object")
            continue
        gate_id = ref.get("gateId")
        if not isinstance(gate_id, str):
            problems.add("HANDOFF_GATE_ID", ref_location, "gateId must be a string")
            continue
        if gate_id in ref_by_id:
            problems.add("HANDOFF_GATE_DUPLICATE", ref_location, f"duplicate {gate_id}")
            continue
        ref_by_id[gate_id] = ref
        if gate_id not in group.gate_ids:
            problems.add("HANDOFF_GATE_GROUP", ref_location, f"{gate_id} does not belong to W{week}/{lane}")
        gate_document = gate_by_id.get(gate_id)
        if gate_document is None:
            problems.add("HANDOFF_GATE_MISSING", ref_location, f"Gate evidence {gate_id} is missing")
            continue
        gate = _mapping(gate_document.data)
        expected_failure = _mapping(gate.get("firstFailure")).get("failureId") or None
        comparisons = {
            "required": gate.get("requiredGate"),
            "status": gate.get("status"),
            "resultPath": gate.get("resultPath"),
            "resultSha256": gate_document.sha256,
            "firstFailureId": expected_failure,
        }
        for key, expected in comparisons.items():
            if ref.get(key) != expected:
                code = "HANDOFF_GATE_HASH" if key == "resultSha256" else "HANDOFF_GATE_BINDING"
                problems.add(code, ref_location, f"{key} does not match {gate_id}")

    observed = _status_counter(ref_by_id.values())
    expected_counts = {
        "total": len(ref_by_id),
        "passed": observed["Passed"],
        "failed": observed["Failed"],
        "notRun": observed["NotRun"],
        "notApplicable": observed["NotApplicable"],
    }
    _check_balanced_counts(
        problems,
        f"{location}#/gateCounts",
        data.get("gateCounts"),
        "total",
        ("passed", "failed", "notRun", "notApplicable"),
    )
    _compare_rollup(problems, f"{location}#/gateCounts", data.get("gateCounts"), expected_counts)

    expected_failures: dict[str, dict[str, Any]] = {}
    for gate_id in group.gate_ids:
        gate_document = gate_by_id.get(gate_id)
        if gate_document is None:
            continue
        gate = _mapping(gate_document.data)
        failure = gate.get("firstFailure")
        if not isinstance(failure, Mapping):
            continue
        failure_id = failure.get("failureId")
        if not isinstance(failure_id, str) or not failure_id:
            continue
        expected_failures[failure_id] = {
            "failureId": failure_id,
            "gateId": gate_id,
            "classification": failure.get("classification"),
            "evidenceRefs": set(_list(failure.get("evidenceRefs"))),
            "preserved": True,
            "resolved": gate.get("status") == "Passed",
        }
    actual_failures: dict[str, Mapping[str, Any]] = {}
    for index, failure_ref in enumerate(_list(data.get("firstFailures"))):
        failure_location = f"{location}#/firstFailures/{index}"
        if not isinstance(failure_ref, Mapping):
            problems.add("HANDOFF_FIRST_FAILURE", failure_location, "first failure reference must be an object")
            continue
        failure_id = failure_ref.get("failureId")
        if not isinstance(failure_id, str) or failure_id in actual_failures:
            problems.add("HANDOFF_FIRST_FAILURE_DUPLICATE", failure_location, "failureId must be unique")
            continue
        actual_failures[failure_id] = failure_ref
    if set(actual_failures) != set(expected_failures):
        problems.add(
            "HANDOFF_FIRST_FAILURE_SET",
            location,
            "firstFailures must exactly cover all non-null Gate firstFailure records",
        )
    for failure_id, expected in expected_failures.items():
        actual = actual_failures.get(failure_id)
        if actual is None:
            continue
        for key in ("failureId", "gateId", "classification", "preserved", "resolved"):
            if actual.get(key) != expected[key]:
                problems.add(
                    "HANDOFF_FIRST_FAILURE_BINDING",
                    location,
                    f"{failure_id} field {key} does not match Gate firstFailure",
                )
        if set(_list(actual.get("evidenceRefs"))) != expected["evidenceRefs"]:
            problems.add(
                "HANDOFF_FIRST_FAILURE_BINDING",
                location,
                f"{failure_id} evidenceRefs do not match Gate firstFailure",
            )

    decision = data.get("decision")
    ready = decision in {"ReadyForNextCheckpoint", "GoalComplete"}
    identity = _mapping(data.get("identity"))
    git_checkpoint = data.get("gitCheckpoint")
    if not isinstance(git_checkpoint, Mapping):
        problems.add(
            "HANDOFF_GIT_CHECKPOINT_TYPE",
            f"{location}#/gitCheckpoint",
            "handoff gitCheckpoint must be an object",
        )
    else:
        checkpoint_branch = git_checkpoint.get("branch")
        identity_branch = identity.get("branch")
        if (
            not isinstance(checkpoint_branch, str)
            or not isinstance(identity_branch, str)
            or checkpoint_branch != identity_branch
        ):
            problems.add(
                "HANDOFF_GIT_CHECKPOINT_BRANCH",
                f"{location}#/gitCheckpoint/branch",
                "gitCheckpoint branch must exactly match identity.branch",
            )
        checkpoint_commit = git_checkpoint.get("checkpointCommit")
        if ready and (
            not isinstance(checkpoint_commit, str)
            or not checkpoint_commit
            or checkpoint_commit != identity.get("checkpointCommit")
        ):
            problems.add(
                "HANDOFF_GIT_CHECKPOINT_BINDING",
                f"{location}#/gitCheckpoint/checkpointCommit",
                "Ready/GoalComplete gitCheckpoint must equal identity.checkpointCommit",
            )
    if ready:
        if set(ref_by_id) != set(group.gate_ids) or len(ref_by_id) != len(group.gate_ids):
            missing = sorted(set(group.gate_ids) - set(ref_by_id))
            extra = sorted(set(ref_by_id) - set(group.gate_ids))
            problems.add("HANDOFF_GATE_SET", location, f"Ready handoff needs exact group; missing={missing}, extra={extra}")
        if data.get("requiredGatesSatisfied") is not True:
            problems.add("HANDOFF_READY_FLAG", location, "Ready handoff requires requiredGatesSatisfied=true")
        for gate_id in group.gate_ids:
            gate = _mapping(_mapping(gate_by_id.get(gate_id).data) if gate_by_id.get(gate_id) else {})
            if gate.get("status") != "Passed":
                problems.add("HANDOFF_NOT_READY", location, f"{gate_id} is not Passed")
            if gate.get("requiredGate") is not True:
                problems.add("HANDOFF_REQUIRED", location, f"{gate_id} must be a required Gate")
        _check_cleanup(problems, f"{location}#/cleanup", data.get("cleanup"), ready=True)
        _check_p0_p1(problems, f"{location}#/openIssues", data.get("openIssues"), True)
    else:
        _check_cleanup(problems, f"{location}#/cleanup", data.get("cleanup"), ready=False)
        _check_p0_p1(problems, f"{location}#/openIssues", data.get("openIssues"), False)

    acceptances = _list(data.get("userAcceptances"))
    acceptance_ids = [
        item.get("acceptanceId") for item in acceptances if isinstance(item, Mapping)
    ]
    if len(acceptance_ids) != len(set(acceptance_ids)):
        problems.add("HANDOFF_ACCEPTANCE_DUPLICATE", location, "duplicate user acceptance IDs")
    if decision == "GoalComplete":
        required_acceptances = {
            "W86-USER-VISUAL",
            "W89-USER-VISUAL",
            "W92-USER-VISUAL",
        }
        passed = {
            item.get("acceptanceId")
            for item in acceptances
            if isinstance(item, Mapping) and item.get("status") == "Passed"
        }
        if passed != required_acceptances:
            problems.add("HANDOFF_FINAL_ACCEPTANCE", location, "GoalComplete requires all three user acceptances Passed")
        if week != 92 or lane != "acceptance" or data.get("finalDecision") != "RefactorAccepted":
            problems.add("HANDOFF_GOAL_COMPLETE", location, "GoalComplete must be W92 acceptance / RefactorAccepted")

    _scan_value_for_secrets(data, problems, f"{location}#")

    budget = _mapping(data.get("authorizationBudget"))
    used = budget.get("providerTurnsUsed")
    remaining = budget.get("providerTurnsRemaining")
    if not all(_is_int(item) for item in (used, remaining)) or used + remaining != PROVIDER_BUDGET:
        problems.add("HANDOFF_PROVIDER_BUDGET", location, "provider used + remaining must equal 120")
    write_gate = CONTROLLED_WRITE_GATES.get(group.week)
    expected_write = bool(
        write_gate
        and write_gate in gate_by_id
        and _mapping(_mapping(gate_by_id[write_gate].data).get("authorization")).get("controlledWriteUsed") is True
    )
    if budget.get("controlledWriteUsedThisWeek") is not expected_write:
        problems.add("HANDOFF_CONTROLLED_WRITE", location, "controlledWriteUsedThisWeek does not match Gate evidence")
    actual_write_count = _expected_write_count(gate_by_id, write_gate) if write_gate else 0
    if budget.get("controlledWriteExecutionCountThisWeek") != actual_write_count:
        problems.add("HANDOFF_CONTROLLED_WRITE", location, "controlledWriteExecutionCountThisWeek does not match Gate evidence")
    if expected_write:
        if budget.get("controlledWriteShapeVerified") is not True:
            problems.add("HANDOFF_CONTROLLED_WRITE", location, "controlled write shape must be verified")
        if budget.get("controlledWriteNonWritePreconditionsPassed") is not True:
            problems.add("HANDOFF_CONTROLLED_WRITE", location, "controlled write preconditions must be Passed")
        if not _list(budget.get("controlledWriteEvidenceRefs")):
            problems.add("HANDOFF_CONTROLLED_WRITE", location, "controlled write evidence refs are missing")
    else:
        if budget.get("controlledWriteExecutionCountThisWeek") not in (0, None):
            problems.add("HANDOFF_CONTROLLED_WRITE", location, "non-write week must record zero executions")
        if _list(budget.get("controlledWriteEvidenceRefs")):
            problems.add("HANDOFF_CONTROLLED_WRITE", location, "non-write week cannot claim write evidence")

    history = _mapping(budget.get("controlledWriteHistory"))
    for history_week, history_gate in CONTROLLED_WRITE_GATES.items():
        record = _mapping(history.get(f"w{history_week}"))
        gate_document = gate_by_id.get(history_gate)
        gate_auth = _mapping(_mapping(gate_document.data).get("authorization")) if gate_document else {}
        visible_at_handoff = history_week <= group.week
        history_count = (
            gate_auth.get("controlledWriteExecutionCount", 0)
            if visible_at_handoff
            else 0
        )
        expected_status = (
            _mapping(gate_document.data).get("status")
            if gate_document is not None and visible_at_handoff
            else "NotRun"
        )
        if record.get("gateId") != history_gate or record.get("week") != history_week:
            problems.add("HANDOFF_WRITE_HISTORY", location, f"w{history_week} history has wrong gate/week")
        if record.get("executionCount") != history_count:
            problems.add("HANDOFF_WRITE_HISTORY", location, f"w{history_week} execution count does not match Gate")
        if history_count == 1:
            if record.get("status") != expected_status:
                problems.add("HANDOFF_WRITE_HISTORY", location, f"w{history_week} status does not match Gate")
            if record.get("shapeVerified") is not True or record.get("nonWritePreconditionsPassed") is not True:
                problems.add("HANDOFF_WRITE_HISTORY", location, f"w{history_week} write verification is incomplete")


def validate_week84_handoff_alias(
    handoff_documents: Sequence[Document],
    problems: Problems,
) -> None:
    canonical = [
        item
        for item in handoff_documents
        if item.relative_path == W84_CANONICAL_HANDOFF_PATH
    ]
    compatibility = [
        item
        for item in handoff_documents
        if item.relative_path == W84_COMPAT_HANDOFF_PATH
    ]
    if not canonical and not compatibility:
        return
    if len(canonical) != 1 or len(compatibility) != 1:
        problems.add(
            "W84_HANDOFF_ALIAS_SET",
            W84_CANONICAL_HANDOFF_PATH,
            "canonical and compatibility handoffs must both exist exactly once",
        )
        return
    if _json_bytes(canonical[0].data) != _json_bytes(compatibility[0].data):
        problems.add(
            "W84_HANDOFF_ALIAS_MISMATCH",
            W84_COMPAT_HANDOFF_PATH,
            "compatibility handoff is not canonically equivalent to Week84 baseline handoff",
        )


def validate_handoff_parent_chain(
    handoff_documents: Sequence[Document],
    problems: Problems,
) -> None:
    canonical_by_path = {
        document.relative_path: document
        for document in handoff_documents
        if document.relative_path in CANONICAL_HANDOFF_PATHS
    }
    for path, document in canonical_by_path.items():
        handoff = _mapping(document.data)
        key = (handoff.get("week"), handoff.get("lane"))
        expected_groups = HANDOFF_PARENT_GROUPS.get(key)
        if expected_groups is None:
            continue
        expected_paths = {
            CANONICAL_HANDOFF_BY_GROUP[parent] for parent in expected_groups
        }
        refs = _list(handoff.get("parentHandoffs"))
        refs_by_path: dict[str, Mapping[str, Any]] = {}
        for index, ref in enumerate(refs):
            location = f"{path}#/parentHandoffs/{index}"
            if not isinstance(ref, Mapping):
                problems.add(
                    "HANDOFF_PARENT_TYPE",
                    location,
                    "parent handoff reference must be an object",
                )
                continue
            parent_path = ref.get("path")
            if not isinstance(parent_path, str) or parent_path in refs_by_path:
                problems.add(
                    "HANDOFF_PARENT_DUPLICATE",
                    location,
                    "parent handoff path must be a unique string",
                )
                continue
            refs_by_path[parent_path] = ref
        if set(refs_by_path) != expected_paths:
            problems.add(
                "HANDOFF_PARENT_SET",
                path,
                "parentHandoffs do not match the frozen direct predecessor set",
            )
        for parent_group in expected_groups:
            parent_path = CANONICAL_HANDOFF_BY_GROUP[parent_group]
            ref = refs_by_path.get(parent_path)
            parent = canonical_by_path.get(parent_path)
            if ref is None:
                continue
            if parent is None:
                problems.add(
                    "HANDOFF_PARENT_MISSING",
                    path,
                    "referenced canonical parent handoff is missing",
                )
                continue
            parent_identity = _mapping(_mapping(parent.data).get("identity"))
            expected = {
                "week": parent_group[0],
                "lane": parent_group[1],
                "sha256": parent.sha256,
                "productCandidate": parent_identity.get("productCandidate"),
            }
            for field, value in expected.items():
                if ref.get(field) != value:
                    problems.add(
                        "HANDOFF_PARENT_BINDING",
                        path,
                        f"parent handoff {field} does not match canonical bytes/identity",
                    )


_TRUSTED_GIT_CACHE: tuple[Path, tuple[int, int, int, int]] | None = None


def _path_is_within(path: Path, root: Path) -> bool:
    try:
        path.relative_to(root)
    except ValueError:
        return False
    return True


def _absolute_path_has_reparse_component(path: Path) -> bool:
    current = path
    while True:
        try:
            info = os.lstat(current)
        except OSError:
            return True
        if stat.S_ISLNK(info.st_mode) or bool(
            getattr(info, "st_file_attributes", 0) & 0x400
        ):
            return True
        if current.parent == current:
            return False
        current = current.parent


def _trusted_git_stat_signature(path: Path) -> tuple[int, int, int, int] | None:
    try:
        info = os.stat(path)
    except OSError:
        return None
    if not stat.S_ISREG(info.st_mode):
        return None
    return (
        int(info.st_dev),
        int(info.st_ino),
        int(info.st_size),
        int(getattr(info, "st_mtime_ns", 0)),
    )


def _trusted_git_candidates() -> tuple[Path, ...]:
    candidates = [TRUSTED_GIT_WINDOWS_LOCATOR]
    for directory in os.environ.get("PATH", "").split(os.pathsep):
        if not directory:
            continue
        candidates.append(Path(directory) / "git.exe")
    unique: list[Path] = []
    seen: set[str] = set()
    for candidate in candidates:
        key = os.path.normcase(os.path.abspath(candidate))
        if key not in seen:
            seen.add(key)
            unique.append(candidate)
    return tuple(unique)


def _resolve_trusted_git(repo_root: Path) -> Path | None:
    """Resolve the pinned Git core by bytes, never by PATH precedence alone."""

    try:
        executable, identity = trusted_executor._trusted_git_identity(repo_root)
    except (trusted_executor.ExecutorError, OSError):
        return None
    expected = {
        "executableRole": TRUSTED_GIT_EXECUTABLE_ROLE,
        "locationRole": "pinned-core-git-outside-repository",
        "version": TRUSTED_GIT_VERSION,
        "executableBytes": TRUSTED_GIT_EXECUTABLE_BYTES,
        "executableSha256": TRUSTED_GIT_EXECUTABLE_SHA256,
    }
    return executable if identity == expected else None


def _trusted_git_identity(repo_root: Path) -> dict[str, Any] | None:
    executable = _resolve_trusted_git(repo_root)
    if executable is None:
        return None
    return {
        "executableRole": TRUSTED_GIT_EXECUTABLE_ROLE,
        "locationRole": "pinned-core-git-outside-repository",
        "version": TRUSTED_GIT_VERSION,
        "executableBytes": TRUSTED_GIT_EXECUTABLE_BYTES,
        "executableSha256": TRUSTED_GIT_EXECUTABLE_SHA256,
    }


def _trusted_git_command(repo_root: Path, arguments: Sequence[str]) -> list[str] | None:
    try:
        before_executable, before_identity = (
            trusted_executor._trusted_git_identity(repo_root)
        )
        command = trusted_executor._git_command(repo_root, arguments)
    except (trusted_executor.ExecutorError, OSError):
        return None
    executable = _resolve_trusted_git(repo_root)
    return (
        command
        if executable is not None
        and executable == before_executable
        and command[0] == str(executable)
        and before_identity == _trusted_git_identity(repo_root)
        else None
    )


def _trusted_git_unchanged(repo_root: Path, executable: Path) -> bool:
    try:
        after_executable, after_identity = (
            trusted_executor._trusted_git_identity(repo_root)
        )
    except (trusted_executor.ExecutorError, OSError):
        return False
    return after_executable == executable and after_identity == _trusted_git_identity(
        repo_root
    )


def _scrubbed_git_environment(git_executable: Path) -> dict[str, str]:
    """Return a minimal environment that cannot redirect Git identity."""

    allowed = (
        "SYSTEMROOT",
        "WINDIR",
        "COMSPEC",
        "PATHEXT",
        "TEMP",
        "TMP",
        "TMPDIR",
        "LANG",
        "LC_ALL",
        "TZ",
    )
    environment = {
        key: os.environ[key]
        for key in allowed
        if key in os.environ
    }
    environment.update(
        {
            "GIT_ATTR_NOSYSTEM": "1",
            "GIT_NO_REPLACE_OBJECTS": "1",
            "GIT_CONFIG_NOSYSTEM": "1",
            "GIT_CONFIG_GLOBAL": os.devnull,
            "GIT_CONFIG_SYSTEM": os.devnull,
            "GIT_NO_LAZY_FETCH": "1",
            "GIT_OPTIONAL_LOCKS": "0",
            "GIT_TERMINAL_PROMPT": "0",
            "GCM_INTERACTIVE": "Never",
            "PATH": str(git_executable.parent),
        }
    )
    return environment


def _git_return_code(repo_root: Path, arguments: Sequence[str]) -> int | None:
    command = _trusted_git_command(repo_root, arguments)
    if command is None:
        return None
    try:
        completed = subprocess.run(
            command,
            cwd=repo_root,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
            timeout=10,
            env=_scrubbed_git_environment(Path(command[0])),
        )
    except (OSError, subprocess.SubprocessError):
        return None
    if not _trusted_git_unchanged(repo_root, Path(command[0])):
        return None
    return completed.returncode


def _git_stdout(repo_root: Path, arguments: Sequence[str]) -> str | None:
    command = _trusted_git_command(repo_root, arguments)
    if command is None:
        return None
    try:
        completed = subprocess.run(
            command,
            cwd=repo_root,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            check=False,
            timeout=10,
            text=True,
            encoding="utf-8",
            errors="replace",
            env=_scrubbed_git_environment(Path(command[0])),
        )
    except (OSError, subprocess.SubprocessError):
        return None
    if not _trusted_git_unchanged(repo_root, Path(command[0])):
        return None
    if completed.returncode != 0:
        return None
    return completed.stdout.strip()


def _git_bytes(repo_root: Path, arguments: Sequence[str]) -> bytes | None:
    command = _trusted_git_command(repo_root, arguments)
    if command is None:
        return None
    try:
        completed = subprocess.run(
            command,
            cwd=repo_root,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            check=False,
            timeout=10,
            env=_scrubbed_git_environment(Path(command[0])),
        )
    except (OSError, subprocess.SubprocessError):
        return None
    if not _trusted_git_unchanged(repo_root, Path(command[0])):
        return None
    if completed.returncode != 0:
        return None
    return completed.stdout


def _git_bytes_with_input(
    repo_root: Path,
    arguments: Sequence[str],
    input_bytes: bytes,
    *,
    timeout_seconds: int = 30,
) -> bytes | None:
    """Run one scrubbed Git command with bounded binary stdin/stdout."""

    command = _trusted_git_command(repo_root, arguments)
    if command is None:
        return None
    try:
        completed = subprocess.run(
            command,
            cwd=repo_root,
            input=input_bytes,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            check=False,
            timeout=timeout_seconds,
            env=_scrubbed_git_environment(Path(command[0])),
        )
    except (OSError, subprocess.SubprocessError):
        return None
    if not _trusted_git_unchanged(repo_root, Path(command[0])):
        return None
    if completed.returncode != 0:
        return None
    return completed.stdout


def _git_cat_file_blobs(
    repo_root: Path, object_ids: Sequence[str]
) -> dict[str, bytes] | None:
    """Read a bounded set of Git blobs in one ``cat-file --batch`` process."""

    unique_ids = list(dict.fromkeys(object_ids))
    if (
        not unique_ids
        or len(unique_ids) > MAX_COMMAND_CONTROL_SOURCE_TREE_ENTRIES
        or any(re.fullmatch(r"[0-9a-f]{40}", value) is None for value in unique_ids)
    ):
        return None
    output = _git_bytes_with_input(
        repo_root,
        ("cat-file", "--batch"),
        b"".join(value.encode("ascii") + b"\n" for value in unique_ids),
    )
    if output is None:
        return None
    cursor = 0
    result: dict[str, bytes] = {}
    for expected_id in unique_ids:
        header_end = output.find(b"\n", cursor)
        if header_end < 0:
            return None
        try:
            object_id, object_type, size_text = output[cursor:header_end].decode(
                "ascii"
            ).split(" ")
            size = int(size_text)
        except (UnicodeDecodeError, ValueError):
            return None
        body_start = header_end + 1
        body_end = body_start + size
        if (
            object_id != expected_id
            or object_type != "blob"
            or size < 0
            or size > MAX_COMMAND_CONTROL_SOURCE_TREE_BYTES
            or body_end >= len(output)
            or output[body_end : body_end + 1] != b"\n"
        ):
            return None
        result[expected_id] = output[body_start:body_end]
        cursor = body_end + 1
    return result if cursor == len(output) else None


def validate_git_repository_trust(
    repo_root: Path,
    problems: Problems,
    repository_context: Any | None = None,
) -> bool:
    """Reject redirected, non-SHA-1, shallow, or history-rewritten repos."""

    initial_problem_count = len(problems.items)
    location = str(repo_root)
    try:
        bundle_executable, bundle_identity = (
            trusted_executor._trusted_git_identity(repo_root, rehash=True)
        )
    except (trusted_executor.ExecutorError, OSError):
        problems.add(
            "GIT_TRUST_TOOL_IDENTITY",
            location,
            "trusted Git core must match its frozen raw executable identity",
        )
        return False
    try:
        canonical_root = repo_root.resolve(strict=True)
        context = (
            trusted_executor._repository_context(canonical_root)
            if repository_context is None
            else repository_context
        )
        if context.candidate_root != canonical_root:
            raise trusted_executor.ExecutorError("GIT_WORKTREE_TOPOLOGY")
        expected_git_dir = context.candidate_git_dir
        common_dir = context.common_dir
    except (OSError, trusted_executor.ExecutorError, AttributeError):
        canonical_root = repo_root
        expected_git_dir = canonical_root / ".git"
        common_dir = expected_git_dir
    top_level = _git_stdout(canonical_root, ("rev-parse", "--show-toplevel"))
    absolute_git_dir = _git_stdout(
        canonical_root, ("rev-parse", "--absolute-git-dir")
    )
    inside = _git_stdout(
        canonical_root, ("rev-parse", "--is-inside-work-tree")
    )
    identity_valid = False
    try:
        identity_valid = (
            top_level is not None
            and Path(top_level).resolve(strict=True) == canonical_root
            and absolute_git_dir is not None
            and Path(absolute_git_dir).resolve(strict=True)
            == expected_git_dir.resolve(strict=True)
            and expected_git_dir.is_dir()
            and not expected_git_dir.is_symlink()
            and inside == "true"
        )
    except OSError:
        identity_valid = False
    if not identity_valid:
        problems.add(
            "GIT_TRUST_REPOSITORY_IDENTITY",
            location,
            "Git top-level and absolute Git directory must be this repository and its .git directory",
        )

    object_format = _git_stdout(
        canonical_root, ("rev-parse", "--show-object-format")
    )
    if object_format != "sha1":
        problems.add(
            "GIT_TRUST_OBJECT_FORMAT",
            location,
            "Week84-92 commit and blob identities require a SHA-1 Git object database",
        )

    shallow = _git_stdout(
        canonical_root, ("rev-parse", "--is-shallow-repository")
    )
    if shallow != "false":
        problems.add(
            "GIT_TRUST_SHALLOW",
            location,
            "shallow repositories cannot prove complete immutable history",
        )

    replace_refs = _git_stdout(
        canonical_root,
        ("for-each-ref", "--format=%(refname)", "refs/replace"),
    )
    if replace_refs is None or replace_refs:
        problems.add(
            "GIT_TRUST_REPLACE_REFS",
            location,
            "replace refs are forbidden for evidence identity validation",
        )

    grafts_path = common_dir / "info" / "grafts"
    if grafts_path.exists():
        problems.add(
            "GIT_TRUST_GRAFTS",
            str(grafts_path),
            "legacy grafts are forbidden for evidence history validation",
        )

    partial_clone_config = _git_return_code(
        canonical_root,
        (
            "config",
            "--local",
            "--get-regexp",
            r"^(extensions\.partialclone|remote\..*\.(promisor|partialclonefilter))$",
        ),
    )
    if partial_clone_config != 1:
        problems.add(
            "GIT_TRUST_PARTIAL_CLONE",
            location,
            "partial-clone, promisor, and lazy-fetch repository configuration is forbidden",
        )

    head_tree = _git_stdout(
        canonical_root, ("ls-tree", "-r", "HEAD")
    )
    if head_tree is None or any(
        line.startswith("160000 commit ")
        for line in head_tree.splitlines()
    ):
        problems.add(
            "GIT_TRUST_SUBMODULE",
            location,
            "candidate trees cannot contain mode-160000 gitlinks",
        )

    tracked_env = _git_return_code(
        canonical_root,
        ("ls-files", "--error-unmatch", "--", PROVIDER_ENV_PATH),
    )
    if tracked_env != 1:
        problems.add(
            "GIT_TRUST_PROVIDER_ENV_TRACKED",
            PROVIDER_ENV_PATH,
            "provider environment file must never be tracked",
        )
    ignored_env = _git_return_code(
        canonical_root,
        ("check-ignore", "--quiet", "--", PROVIDER_ENV_PATH),
    )
    if ignored_env != 0:
        problems.add(
            "GIT_TRUST_PROVIDER_ENV_IGNORE",
            PROVIDER_ENV_PATH,
            "provider environment file must match a Git ignore rule",
        )
    historical_env = _git_stdout(
        canonical_root,
        ("log", "--all", "--format=%H", "--", PROVIDER_ENV_PATH),
    )
    if historical_env is None or historical_env:
        problems.add(
            "GIT_TRUST_PROVIDER_ENV_HISTORY",
            PROVIDER_ENV_PATH,
            "provider environment file must never appear in reachable Git history",
        )
    try:
        after_executable, after_identity = (
            trusted_executor._trusted_git_identity(repo_root, rehash=True)
        )
    except (trusted_executor.ExecutorError, OSError):
        after_executable, after_identity = None, None
    if (
        after_executable != bundle_executable
        or after_identity != bundle_identity
    ):
        problems.add(
            "GIT_TRUST_TOOL_IDENTITY",
            location,
            "trusted Git core changed during repository trust validation",
        )
    return len(problems.items) == initial_problem_count


def _single_add_commit(repo_root: Path, relative_path: str) -> str | None:
    add_history = _git_stdout(
        repo_root,
        (
            "log",
            "--all",
            "--diff-filter=A",
            "--format=%H",
            "--reverse",
            "--",
            relative_path,
        ),
    )
    if add_history is None:
        return None
    commits = [value for value in add_history.splitlines() if value]
    return commits[0] if len(commits) == 1 else None


def _git_mode(repo_root: Path, arguments: Sequence[str]) -> str | None:
    output = _git_stdout(repo_root, arguments)
    if output is None:
        return None
    lines = [line for line in output.splitlines() if line]
    if len(lines) != 1:
        return None
    fields = lines[0].split(maxsplit=1)
    return fields[0] if fields else None


def _git_attributes_are_raw_safe(
    repo_root: Path,
    relative_path: str,
    problems: Problems,
    code_prefix: str,
) -> bool:
    """Require raw-byte checkout semantics with no content transforms."""

    output = _git_stdout(
        repo_root,
        (
            "check-attr",
            "text",
            "filter",
            "working-tree-encoding",
            "ident",
            "--",
            relative_path,
        ),
    )
    valid = True
    observed: dict[str, str] = {}
    if output is not None:
        for line in output.splitlines():
            fields = line.rsplit(": ", 2)
            if len(fields) == 3:
                observed[fields[1]] = fields[2]
    if output is None or observed != {
        "text": "unset",
        "filter": "unspecified",
        "working-tree-encoding": "unspecified",
        "ident": "unspecified",
    }:
        problems.add(
            f"{code_prefix}_ATTRIBUTES",
            relative_path,
            "path must be -text with filter, working-tree-encoding, and ident unspecified",
        )
        valid = False

    info_path_value = _git_stdout(
        repo_root, ("rev-parse", "--git-path", "info/attributes")
    )
    if info_path_value is None:
        problems.add(
            f"{code_prefix}_ATTRIBUTES",
            relative_path,
            "repository-local attributes could not be inspected",
        )
        return False
    info_path = Path(info_path_value)
    if not info_path.is_absolute():
        info_path = repo_root / info_path
    if info_path.exists():
        try:
            info_lines = info_path.read_text(
                encoding="utf-8", errors="replace"
            ).splitlines()
        except OSError:
            problems.add(
                f"{code_prefix}_ATTRIBUTES",
                relative_path,
                "repository-local attributes could not be read",
            )
            return False
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
                problems.add(
                    f"{code_prefix}_INFO_ATTRIBUTES",
                    relative_path,
                    "repository-local info/attributes may not define content transforms",
                )
                valid = False
                break
    return valid


def _git_attributes_are_materialization_safe(
    repo_root: Path,
    relative_path: str,
    problems: Problems,
    code_prefix: str,
) -> bool:
    """Allow text normalization only when the observed worktree bytes bind.

    Test source trees commonly use Git's default text:auto behavior.  Their
    control/candidate/index/worktree SHA-256 identities are compared directly,
    so text classification itself need not be unset; executable transforms
    remain forbidden.
    """

    output = _git_stdout(
        repo_root,
        (
            "check-attr",
            "filter",
            "working-tree-encoding",
            "ident",
            "--",
            relative_path,
        ),
    )
    observed: dict[str, str] = {}
    if output is not None:
        for line in output.splitlines():
            fields = line.rsplit(": ", 2)
            if len(fields) == 3:
                observed[fields[1]] = fields[2]
    valid = observed == {
        "filter": "unspecified",
        "working-tree-encoding": "unspecified",
        "ident": "unspecified",
    }
    if not valid:
        problems.add(
            f"{code_prefix}_ATTRIBUTES",
            relative_path,
            "sourceTree path may use text:auto but cannot use filter, working-tree-encoding, or ident",
        )
    info_path_value = _git_stdout(
        repo_root, ("rev-parse", "--git-path", "info/attributes")
    )
    if info_path_value is None:
        return False
    info_path = Path(info_path_value)
    if not info_path.is_absolute():
        info_path = repo_root / info_path
    if info_path.exists():
        try:
            lines = info_path.read_text(
                encoding="utf-8", errors="replace"
            ).splitlines()
        except OSError:
            return False
        forbidden = {"filter", "working-tree-encoding", "ident"}
        if any(
            {
                token.lstrip("-!").split("=", 1)[0]
                for token in line.strip().split()[1:]
            }
            & forbidden
            for line in lines
            if line.strip() and not line.lstrip().startswith("#")
        ):
            problems.add(
                f"{code_prefix}_INFO_ATTRIBUTES",
                relative_path,
                "repository-local info/attributes cannot transform sourceTree materialization",
            )
            valid = False
    return valid


def _git_attributes_are_materialization_safe_batch(
    repo_root: Path,
    relative_paths: Sequence[str],
    problems: Problems,
    code_prefix: str,
) -> bool:
    """Batch-check executable Git attributes for a sealed source inventory."""

    paths = list(relative_paths)
    if (
        not paths
        or len(paths) > MAX_COMMAND_CONTROL_SOURCE_TREE_ENTRIES
        or len(set(paths)) != len(paths)
        or any(not _is_exact_safe_source_path(path) for path in paths)
    ):
        problems.add(
            f"{code_prefix}_ATTRIBUTES",
            str(repo_root),
            "sourceTree attribute input must be a non-empty bounded unique safe path list",
        )
        return False
    output = _git_bytes_with_input(
        repo_root,
        (
            "check-attr",
            "-z",
            "filter",
            "working-tree-encoding",
            "ident",
            "--stdin",
        ),
        b"".join(path.encode("utf-8") + b"\0" for path in paths),
    )
    expected_attributes = {
        "filter": "unspecified",
        "working-tree-encoding": "unspecified",
        "ident": "unspecified",
    }
    observed_by_path: dict[str, dict[str, str]] = {path: {} for path in paths}
    valid = output is not None
    fields = (output or b"").split(b"\0")
    if fields and fields[-1] == b"":
        fields.pop()
    if len(fields) != len(paths) * len(expected_attributes) * 3:
        valid = False
    else:
        for index in range(0, len(fields), 3):
            try:
                path = fields[index].decode("utf-8")
                attribute = fields[index + 1].decode("ascii")
                value = fields[index + 2].decode("utf-8")
            except UnicodeDecodeError:
                valid = False
                continue
            observed = observed_by_path.get(path)
            if (
                observed is None
                or attribute not in expected_attributes
                or attribute in observed
            ):
                valid = False
                continue
            observed[attribute] = value
    invalid_paths = [
        path
        for path in paths
        if observed_by_path.get(path) != expected_attributes
    ]
    if invalid_paths:
        valid = False
    if not valid:
        problems.add(
            f"{code_prefix}_ATTRIBUTES",
            invalid_paths[0] if invalid_paths else str(repo_root),
            "sourceTree paths may use text:auto but cannot use filter, working-tree-encoding, or ident",
        )

    info_path_value = _git_stdout(
        repo_root, ("rev-parse", "--git-path", "info/attributes")
    )
    if info_path_value is None:
        return False
    info_path = Path(info_path_value)
    if not info_path.is_absolute():
        info_path = repo_root / info_path
    if info_path.exists():
        try:
            lines = info_path.read_text(
                encoding="utf-8", errors="replace"
            ).splitlines()
        except OSError:
            return False
        forbidden = {"filter", "working-tree-encoding", "ident"}
        if any(
            {
                token.lstrip("-!").split("=", 1)[0]
                for token in line.strip().split()[1:]
            }
            & forbidden
            for line in lines
            if line.strip() and not line.lstrip().startswith("#")
        ):
            problems.add(
                f"{code_prefix}_INFO_ATTRIBUTES",
                str(info_path),
                "repository-local info/attributes cannot transform sourceTree materialization",
            )
            valid = False
    return valid


def _validate_single_add_immutable_blob(
    repo_root: Path,
    relative_path: str,
    problems: Problems,
    code_prefix: str,
) -> bool:
    """Validate one regular raw-byte path against its only first-add Git blob."""

    tracked = _git_stdout(
        repo_root, ("ls-files", "--error-unmatch", "--", relative_path)
    )
    dirty = _git_stdout(
        repo_root,
        ("status", "--porcelain=v1", "--untracked-files=all", "--", relative_path),
    )
    first_commit = _single_add_commit(repo_root, relative_path)
    history = _git_stdout(
        repo_root,
        ("log", "--all", "--format=%H", "--reverse", "--", relative_path),
    )
    if tracked is None or dirty is None or first_commit is None or history is None:
        problems.add(
            f"{code_prefix}_GIT",
            relative_path,
            "path must be tracked with one readable first-add commit",
        )
        return False
    history_commits = [value for value in history.splitlines() if value]
    first_raw = _git_bytes(
        repo_root, ("show", f"{first_commit}:{relative_path}")
    )
    head_raw = _git_bytes(repo_root, ("show", f"HEAD:{relative_path}"))
    index_raw = _git_bytes(repo_root, ("show", f":{relative_path}"))
    try:
        worktree_path = repo_root / relative_path
        worktree_raw = worktree_path.read_bytes()
        worktree_is_symlink = worktree_path.is_symlink()
    except OSError:
        worktree_raw = None
        worktree_is_symlink = False
    first_mode = _git_mode(
        repo_root, ("ls-tree", first_commit, "--", relative_path)
    )
    head_mode = _git_mode(repo_root, ("ls-tree", "HEAD", "--", relative_path))
    index_mode = _git_mode(
        repo_root, ("ls-files", "--stage", "--", relative_path)
    )
    valid = True
    if history_commits != [first_commit]:
        problems.add(
            f"{code_prefix}_HISTORY",
            relative_path,
            "path changed after its single first-add commit",
        )
        valid = False
    if dirty or first_raw is None or (
        head_raw != first_raw
        or index_raw != first_raw
        or worktree_raw != first_raw
    ):
        problems.add(
            f"{code_prefix}_IMMUTABLE",
            relative_path,
            "raw worktree, index, HEAD, and first-add bytes must match",
        )
        valid = False
    if (
        worktree_is_symlink
        or first_mode != "100644"
        or head_mode != "100644"
        or index_mode != "100644"
    ):
        problems.add(
            f"{code_prefix}_MODE",
            relative_path,
            "path must remain a regular non-executable 100644 file",
        )
        valid = False
    if not _git_attributes_are_raw_safe(
        repo_root, relative_path, problems, code_prefix
    ):
        valid = False
    return valid


def validate_bootstrap_control_plane(
    repo_root: Path,
    problems: Problems,
) -> None:
    """Pin all bootstrap policy/code paths to their single first-add blobs.

    Git history and an independent reviewer remain the external trust root: a
    malicious replacement of this validator could remove its own checks.  With
    the frozen validator executing, however, later clean commits cannot weaken
    plans, schemas, helpers, or tests unnoticed.
    """

    for relative_path in BOOTSTRAP_CONTROL_PATHS:
        location = relative_path
        tracked = _git_stdout(
            repo_root, ("ls-files", "--error-unmatch", "--", relative_path)
        )
        dirty = _git_stdout(
            repo_root,
            (
                "status",
                "--porcelain=v1",
                "--untracked-files=all",
                "--",
                relative_path,
            ),
        )
        add_history = _git_stdout(
            repo_root,
            (
                "log",
                "--diff-filter=A",
                "--format=%H",
                "--reverse",
                "--",
                relative_path,
            ),
        )
        if tracked is None or dirty is None or add_history is None:
            problems.add(
                "BOOTSTRAP_CONTROL_GIT",
                location,
                "bootstrap control path must be tracked in a readable Git repository",
            )
            continue
        add_commits = [value for value in add_history.splitlines() if value]
        if len(add_commits) != 1:
            problems.add(
                "BOOTSTRAP_CONTROL_HISTORY",
                location,
                "bootstrap control path requires exactly one historical add",
            )
            continue
        first_commit = add_commits[0]
        all_history = _git_stdout(
            repo_root,
            ("log", "--format=%H", "--reverse", "--", relative_path),
        )
        history_commits = (
            [value for value in all_history.splitlines() if value]
            if all_history is not None
            else []
        )
        first_raw = _git_bytes(
            repo_root, ("show", f"{first_commit}:{relative_path}")
        )
        head_raw = _git_bytes(
            repo_root, ("show", f"HEAD:{relative_path}")
        )
        index_raw = _git_bytes(
            repo_root, ("show", f":{relative_path}")
        )
        try:
            worktree_path = repo_root / relative_path
            worktree_raw = worktree_path.read_bytes()
            worktree_is_symlink = worktree_path.is_symlink()
        except OSError:
            worktree_raw = None
            worktree_is_symlink = False
        first_mode = _git_mode(
            repo_root, ("ls-tree", first_commit, "--", relative_path)
        )
        head_mode = _git_mode(
            repo_root, ("ls-tree", "HEAD", "--", relative_path)
        )
        index_mode = _git_mode(
            repo_root, ("ls-files", "--stage", "--", relative_path)
        )
        if history_commits != [first_commit]:
            problems.add(
                "BOOTSTRAP_CONTROL_HISTORY_MUTATION",
                location,
                "bootstrap control path changed after its first-add commit",
            )
        if dirty or first_raw is None or (
            head_raw != first_raw
            or index_raw != first_raw
            or worktree_raw != first_raw
        ):
            problems.add(
                "BOOTSTRAP_CONTROL_IMMUTABLE",
                location,
                "raw worktree, index, HEAD, and first-add control bytes must match",
            )
        if (
            worktree_is_symlink
            or first_mode != "100644"
            or head_mode != "100644"
            or index_mode != "100644"
        ):
            problems.add(
                "BOOTSTRAP_CONTROL_MODE",
                location,
                "bootstrap control path must remain a regular non-executable 100644 file",
            )
        _git_attributes_are_raw_safe(
            repo_root,
            relative_path,
            problems,
            "BOOTSTRAP_CONTROL",
        )


def _validate_current_complete_checkout(
    goal: Mapping[str, Any],
    repo_root: Path,
    problems: Problems,
) -> None:
    """Bind a Complete central Goal to the actual clean integration checkout."""
    identity = _mapping(goal.get("identity"))
    expected_head = identity.get("sourceHead")
    expected_branch = identity.get("integrationBranch")
    actual_head = _git_stdout(repo_root, ("rev-parse", "HEAD"))
    actual_branch = _git_stdout(repo_root, ("branch", "--show-current"))
    dirty = _git_stdout(
        repo_root,
        ("status", "--porcelain=v1", "--untracked-files=all"),
    )
    if actual_head is None or actual_branch is None or dirty is None:
        problems.add(
            "COMPLETE_CHECKOUT_GIT",
            "goal-state.json#/identity",
            "Git checkout identity could not be verified",
        )
        return
    if expected_head != actual_head:
        problems.add(
            "COMPLETE_CHECKOUT_HEAD",
            "goal-state.json#/identity/sourceHead",
            "Complete Goal sourceHead does not equal the current HEAD",
        )
    if expected_branch != actual_branch:
        problems.add(
            "COMPLETE_CHECKOUT_BRANCH",
            "goal-state.json#/identity/integrationBranch",
            "Complete Goal integrationBranch does not equal the current branch",
        )
    if identity.get("dirtyState") != "Clean" or dirty:
        problems.add(
            "COMPLETE_CHECKOUT_DIRTY",
            "goal-state.json#/identity/dirtyState",
            "Complete Goal requires an actually clean current worktree",
        )


def _validate_git_identities(
    identities: Sequence[tuple[str, Mapping[str, Any]]],
    candidate_by_group: Mapping[tuple[int, str], Any],
    repo_root: Path,
    problems: Problems,
    gate_candidates_by_group: Mapping[
        tuple[int, str], Sequence[tuple[str, Any]]
    ]
    | None = None,
) -> None:
    repository_check = _git_return_code(repo_root, ("rev-parse", "--git-dir"))
    if repository_check != 0:
        problems.add(
            "GIT_IDENTITY_REPOSITORY",
            "identity",
            "repository-aware identity validation could not open the Git repository",
        )
        return

    exists_cache: dict[str, bool] = {}
    submodule_free_cache: dict[str, bool | None] = {}
    ancestor_cache: dict[tuple[str, str], bool | None] = {}

    def commit_exists(value: Any, location: str, field: str) -> bool:
        if not isinstance(value, str) or re.fullmatch(r"[0-9a-f]{40}", value) is None:
            problems.add(
                "IDENTITY_COMMIT_FORMAT",
                f"{location}#/identity/{field}",
                "identity value must be a lowercase 40-hex Git commit",
            )
            return False
        if value not in exists_cache:
            exists_cache[value] = _git_return_code(
                repo_root, ("cat-file", "-e", f"{value}^{{commit}}")
            ) == 0
        if not exists_cache[value]:
            problems.add(
                "IDENTITY_COMMIT_MISSING",
                f"{location}#/identity/{field}",
                "identity value does not resolve to a Git commit",
            )
            return False
        if value not in submodule_free_cache:
            tree = _git_stdout(repo_root, ("ls-tree", "-r", value))
            submodule_free_cache[value] = (
                None
                if tree is None
                else not any(
                    line.startswith("160000 commit ")
                    for line in tree.splitlines()
                )
            )
        if submodule_free_cache[value] is not True:
            problems.add(
                "IDENTITY_SUBMODULE",
                f"{location}#/identity/{field}",
                "identity commit trees cannot contain mode-160000 gitlinks",
            )
            return False
        return True

    def is_ancestor(ancestor: str, descendant: str, location: str) -> bool:
        key = (ancestor, descendant)
        if key not in ancestor_cache:
            code = _git_return_code(
                repo_root, ("merge-base", "--is-ancestor", ancestor, descendant)
            )
            ancestor_cache[key] = True if code == 0 else False if code == 1 else None
        result = ancestor_cache[key]
        if result is None:
            problems.add(
                "GIT_IDENTITY_ENGINE",
                location,
                "Git ancestry validation could not complete",
            )
            return False
        return result

    for location, identity in identities:
        source_head = identity.get("sourceHead")
        source_valid = commit_exists(source_head, location, "sourceHead")
        valid_values: dict[str, str] = {}
        for field in ("baseline", "productCandidate", "checkpointCommit"):
            value = identity.get(field)
            if value is None:
                continue
            value_valid = commit_exists(value, location, field)
            if value_valid:
                valid_values[field] = value
            if (
                value_valid
                and source_valid
                and not is_ancestor(value, source_head, location)
            ):
                problems.add(
                    "IDENTITY_DOCUMENT_ANCESTRY",
                    f"{location}#/identity/{field}",
                    f"{field} must be an ancestor-or-equal of sourceHead",
                )
        product_candidate = valid_values.get("productCandidate")
        checkpoint_commit = valid_values.get("checkpointCommit")
        if (
            product_candidate is not None
            and checkpoint_commit is not None
            and not is_ancestor(product_candidate, checkpoint_commit, location)
        ):
            problems.add(
                "IDENTITY_CHECKPOINT_ANCESTRY",
                f"{location}#/identity/checkpointCommit",
                "checkpointCommit must descend from-or-equal productCandidate",
            )

    for group_key, ordered_candidates in (
        gate_candidates_by_group or {}
    ).items():
        for (
            prior_gate_id,
            prior_candidate,
        ), (
            current_gate_id,
            current_candidate,
        ) in zip(ordered_candidates, ordered_candidates[1:]):
            if not (
                isinstance(prior_candidate, str)
                and isinstance(current_candidate, str)
                and re.fullmatch(r"[0-9a-f]{40}", prior_candidate)
                and re.fullmatch(r"[0-9a-f]{40}", current_candidate)
                and exists_cache.get(prior_candidate) is not False
                and exists_cache.get(current_candidate) is not False
            ):
                continue
            if not is_ancestor(
                prior_candidate,
                current_candidate,
                f"W{group_key[0]}/{group_key[1]}/{current_gate_id}",
            ):
                problems.add(
                    "CANDIDATE_GATE_ANCESTRY",
                    current_gate_id,
                    f"candidate must descend from-or-equal {prior_gate_id}",
                )

    lineage_pairs = (
        ((84, "baseline"), (85, "renderer")),
        ((84, "baseline"), (85, "cli")),
        ((85, "renderer"), (86, "renderer")),
        ((86, "renderer"), (87, "renderer")),
        ((87, "renderer"), (88, "renderer")),
        ((88, "renderer"), (89, "renderer")),
        ((85, "cli"), (86, "cli")),
        ((86, "cli"), (87, "cli")),
        ((87, "cli"), (88, "cli")),
        ((88, "cli"), (89, "cli")),
        ((89, "renderer"), (90, "integration")),
        ((89, "cli"), (90, "integration")),
        ((90, "integration"), (91, "hardening")),
        ((91, "hardening"), (92, "acceptance")),
    )
    for predecessor, successor in lineage_pairs:
        ancestor = candidate_by_group.get(predecessor)
        descendant = candidate_by_group.get(successor)
        if not isinstance(ancestor, str) or not isinstance(descendant, str):
            continue
        if not (
            re.fullmatch(r"[0-9a-f]{40}", ancestor)
            and re.fullmatch(r"[0-9a-f]{40}", descendant)
        ):
            continue
        if exists_cache.get(ancestor) is False or exists_cache.get(descendant) is False:
            continue
        if not is_ancestor(ancestor, descendant, f"W{successor[0]}/{successor[1]}"):
            problems.add(
                "IDENTITY_CROSS_GROUP_ANCESTRY",
                f"W{successor[0]}/{successor[1]}",
                f"candidate is not descended from W{predecessor[0]}/{predecessor[1]}",
            )


def _validate_handoff_git_checkpoints(
    handoff_documents: Sequence[Document],
    repo_root: Path,
    problems: Problems,
) -> None:
    """Validate the handoff-specific checkpoint commits against the real repo."""
    if _git_return_code(repo_root, ("rev-parse", "--git-dir")) != 0:
        problems.add(
            "HANDOFF_GIT_CHECKPOINT_REPOSITORY",
            "gitCheckpoint",
            "Git checkpoint validation could not open the repository",
        )
        return

    exists_cache: dict[str, bool] = {}
    ancestor_cache: dict[tuple[str, str], bool | None] = {}

    def commit_exists(value: Any, location: str) -> bool:
        if not isinstance(value, str) or re.fullmatch(r"[0-9a-f]{40}", value) is None:
            problems.add(
                "HANDOFF_GIT_CHECKPOINT_COMMIT_FORMAT",
                location,
                "checkpoint value must be a lowercase 40-hex Git commit",
            )
            return False
        if value not in exists_cache:
            exists_cache[value] = _git_return_code(
                repo_root,
                ("cat-file", "-e", f"{value}^{{commit}}"),
            ) == 0
        if not exists_cache[value]:
            problems.add(
                "HANDOFF_GIT_CHECKPOINT_COMMIT_MISSING",
                location,
                "checkpoint value does not resolve to a Git commit",
            )
            return False
        return True

    def is_ancestor(ancestor: str, descendant: str, location: str) -> bool:
        key = (ancestor, descendant)
        if key not in ancestor_cache:
            code = _git_return_code(
                repo_root,
                ("merge-base", "--is-ancestor", ancestor, descendant),
            )
            ancestor_cache[key] = True if code == 0 else False if code == 1 else None
        result = ancestor_cache[key]
        if result is None:
            problems.add(
                "HANDOFF_GIT_CHECKPOINT_ENGINE",
                location,
                "Git checkpoint ancestry validation could not complete",
            )
            return False
        return result

    for document in handoff_documents:
        handoff = _mapping(document.data)
        checkpoint = handoff.get("gitCheckpoint")
        if not isinstance(checkpoint, Mapping):
            continue
        identity = _mapping(handoff.get("identity"))
        candidate = identity.get("productCandidate")
        candidate_valid = (
            isinstance(candidate, str)
            and re.fullmatch(r"[0-9a-f]{40}", candidate) is not None
            and _git_return_code(
                repo_root,
                ("cat-file", "-e", f"{candidate}^{{commit}}"),
            )
            == 0
        )
        for field in ("checkpointCommit", "localMergeCommit"):
            value = checkpoint.get(field)
            if value is None:
                continue
            location = f"{document.relative_path}#/gitCheckpoint/{field}"
            value_valid = commit_exists(value, location)
            if (
                value_valid
                and candidate_valid
                and not is_ancestor(candidate, value, location)
            ):
                problems.add(
                    "HANDOFF_GIT_CHECKPOINT_ANCESTRY",
                    location,
                    f"{field} must descend from-or-equal the handoff productCandidate",
                )


def validate_candidate_identity(
    goal: Mapping[str, Any],
    gate_by_id: Mapping[str, Document],
    handoff_documents: Sequence[Document],
    contract: Contract,
    complete: bool,
    problems: Problems,
    repo_root: Path | None = None,
) -> None:
    handoffs_by_group: defaultdict[tuple[int, str], list[Document]] = defaultdict(list)
    for document in handoff_documents:
        handoff = _mapping(document.data)
        key = (handoff.get("week"), handoff.get("lane"))
        if key in contract.group_by_key and handoff.get("decision") in {
            "ReadyForNextCheckpoint",
            "GoalComplete",
        }:
            handoffs_by_group[key].append(document)

    candidate_by_group: dict[tuple[int, str], Any] = {}
    gate_candidates_by_group: dict[
        tuple[int, str], list[tuple[str, Any]]
    ] = {}
    for group in contract.groups:
        ordered_gate_candidates = [
            (
                gate_id,
                _mapping(
                    _mapping(gate_by_id[gate_id].data).get("identity")
                ).get("productCandidate"),
            )
            for gate_id in group.gate_ids
            if gate_id in gate_by_id
        ]
        gate_candidates_by_group[group.key] = ordered_gate_candidates
        ready_handoffs = handoffs_by_group.get(group.key, [])
        if not ready_handoffs:
            continue
        if (
            len(ordered_gate_candidates) != len(group.gate_ids)
            or any(candidate is None for _gate_id, candidate in ordered_gate_candidates)
        ):
            problems.add(
                "CANDIDATE_GATE_MISMATCH",
                f"W{group.week}/{group.lane}",
                "Ready Gate set must bind one non-null execution candidate per canonical Gate",
            )
            continue
        closure_gate_id, closure_candidate = ordered_gate_candidates[-1]
        handoff_candidate_values = [
            _mapping(_mapping(handoff_document.data).get("identity")).get(
                "productCandidate"
            )
            for handoff_document in ready_handoffs
        ]
        handoff_candidates = {
            value for value in handoff_candidate_values if isinstance(value, str)
        }
        if (
            len(handoff_candidates) != 1
            or any(
                not isinstance(value, str)
                for value in handoff_candidate_values
            )
        ):
            problems.add(
                "CANDIDATE_HANDOFF_MISMATCH",
                f"W{group.week}/{group.lane}",
                "ready handoffs must agree on one final group candidate",
            )
            continue
        candidate = next(iter(handoff_candidates))
        candidate_by_group[group.key] = candidate
        for handoff_document in ready_handoffs:
            handoff_candidate = _mapping(
                _mapping(handoff_document.data).get("identity")
            ).get("productCandidate")
            if handoff_candidate != closure_candidate:
                problems.add(
                    "CANDIDATE_HANDOFF_MISMATCH",
                    handoff_document.relative_path,
                    f"handoff final candidate must equal closure Gate {closure_gate_id}",
                )
            if repo_root is not None:
                snapshot_relative = _mapping(
                    _mapping(handoff_document.data).get("goalControlBinding")
                ).get("path")
                if not isinstance(snapshot_relative, str):
                    continue
                snapshot_path = _safe_repo_path(
                    repo_root,
                    snapshot_relative,
                    problems,
                    f"{handoff_document.relative_path}#/goalControlBinding/path",
                )
                if snapshot_path is not None and snapshot_path.is_file():
                    snapshot_document = read_document(
                        repo_root, snapshot_path, problems
                    )
                    snapshot_candidate = (
                        _mapping(
                            _mapping(
                                snapshot_document.data
                                if snapshot_document is not None
                                else {}
                            ).get("identity")
                        ).get("productCandidate")
                    )
                    if snapshot_candidate != handoff_candidate:
                        problems.add(
                            "CANDIDATE_SNAPSHOT_MISMATCH",
                            (
                                snapshot_document.relative_path
                                if snapshot_document is not None
                                else str(snapshot_relative)
                            ),
                            "snapshot candidate must equal its final handoff candidate",
                        )

    if repo_root is not None:
        identities: list[tuple[str, Mapping[str, Any]]] = [
            ("goal-state.json", _mapping(goal.get("identity")))
        ]
        identities.extend(
            (document.relative_path, _mapping(_mapping(document.data).get("identity")))
            for document in gate_by_id.values()
        )
        identities.extend(
            (document.relative_path, _mapping(_mapping(document.data).get("identity")))
            for document in handoff_documents
        )
        _validate_git_identities(
            identities,
            candidate_by_group,
            repo_root,
            problems,
            gate_candidates_by_group,
        )
        _validate_handoff_git_checkpoints(
            handoff_documents,
            repo_root,
            problems,
        )

    if complete:
        goal_candidate = _mapping(goal.get("identity")).get("productCandidate")
        if not isinstance(goal_candidate, str) or not goal_candidate:
            problems.add(
                "COMPLETE_CANDIDATE_MISSING",
                "goal-state.json#/identity/productCandidate",
                "Complete Goal requires a non-null productCandidate",
            )
            return
        final_handoffs = [
            document
            for document in handoff_documents
            if document.relative_path == FINAL_HANDOFF_PATH
        ]
        for document in final_handoffs:
            if _mapping(_mapping(document.data).get("identity")).get(
                "productCandidate"
            ) != goal_candidate:
                problems.add(
                    "COMPLETE_CANDIDATE_MISMATCH",
                    document.relative_path,
                    "final handoff productCandidate differs from central Goal",
                )
        closure_gate_id = contract.group_by_key[(92, "acceptance")].gate_ids[-1]
        closure_document = gate_by_id.get(closure_gate_id)
        if closure_document is not None:
            if _mapping(_mapping(closure_document.data).get("identity")).get(
                "productCandidate"
            ) != goal_candidate:
                problems.add(
                    "COMPLETE_CANDIDATE_MISMATCH",
                    closure_document.relative_path,
                    "W92 closure Gate productCandidate differs from central Goal",
                )


def _aggregate_progress_status(values: Sequence[Any]) -> str:
    if any(value == "Failed" for value in values):
        return "Failed"
    if values and all(value == "Passed" for value in values):
        return "Passed"
    return "NotRun"


def _group_id(group: GateGroup) -> str:
    return f"w{group.week}-{group.lane}"


def validate_active_progression(
    goal: Mapping[str, Any],
    contract: Contract,
    gate_by_id: Mapping[str, Document],
    handoff_documents: Sequence[Document],
    sealed_group_ids: set[str],
    pre_seal_group: str | None,
    problems: Problems,
    location: str,
) -> None:
    """Derive the only legal Active/Blocked frontier from immutable evidence."""

    canonical_handoffs: dict[tuple[int, str], Document] = {}
    for document in handoff_documents:
        data = _mapping(document.data)
        key = (data.get("week"), data.get("lane"))
        if (
            key in contract.group_by_key
            and document.relative_path == CANONICAL_HANDOFF_BY_GROUP[key]
            and data.get("decision") in {"ReadyForNextCheckpoint", "GoalComplete"}
        ):
            canonical_handoffs[key] = document

    known_group_ids = {_group_id(group) for group in contract.groups}
    if not sealed_group_ids.issubset(known_group_ids):
        problems.add(
            "PROGRESS_ANCHOR_SET",
            location,
            "sealed evidence contains a group outside the frozen DAG",
        )

    groups_by_week: defaultdict[int, list[GateGroup]] = defaultdict(list)
    for group in contract.groups:
        groups_by_week[group.week].append(group)
        group_id = _group_id(group)
        present_ids = [gate_id for gate_id in group.gate_ids if gate_id in gate_by_id]
        if present_ids != list(group.gate_ids[: len(present_ids)]):
            problems.add(
                "PROGRESS_GATE_PREFIX",
                f"W{group.week}/{group.lane}",
                "Gate result files must be a canonical contiguous prefix",
            )
        for index, gate_id in enumerate(present_ids[1:], start=1):
            predecessor = gate_by_id.get(group.gate_ids[index - 1])
            if predecessor is None or _mapping(predecessor.data).get("status") != "Passed":
                problems.add(
                    "PROGRESS_GATE_ORDER",
                    gate_by_id[gate_id].relative_path,
                    "a Gate cannot exist before its immediate predecessor Passed",
                )

        started = bool(present_ids) or group.key in canonical_handoffs
        if started:
            parent_ids = {
                f"w{week}-{lane}"
                for week, lane in HANDOFF_PARENT_GROUPS[group.key]
            }
            if not parent_ids.issubset(sealed_group_ids):
                problems.add(
                    "PROGRESS_PARENT_ANCHOR",
                    f"W{group.week}/{group.lane}",
                    "group execution requires every direct parent handoff and anchor",
                )
            earlier_ids = {
                _group_id(candidate)
                for candidate in contract.groups
                if candidate.week < group.week
            }
            if not earlier_ids.issubset(sealed_group_ids):
                problems.add(
                    "PROGRESS_CHECKPOINT_BARRIER",
                    f"W{group.week}/{group.lane}",
                    "a later checkpoint cannot start before every earlier checkpoint group is sealed",
                )

        handoff = canonical_handoffs.get(group.key)
        if group_id in sealed_group_ids and handoff is None:
            problems.add(
                "PROGRESS_SEALED_HANDOFF",
                f"W{group.week}/{group.lane}",
                "sealed group requires its canonical ready handoff",
            )
        if handoff is not None and group_id not in sealed_group_ids:
            if group_id != pre_seal_group:
                problems.add(
                    "PROGRESS_UNSEALED_HANDOFF",
                    handoff.relative_path,
                    "ready handoff may remain unsealed only in its explicit pre-seal run",
                )

    unsealed = [
        group for group in contract.groups if _group_id(group) not in sealed_group_ids
    ]
    if not unsealed:
        problems.add(
            "PROGRESS_ACTIVE_AFTER_SEAL",
            location,
            "Active/Blocked Goal cannot remain after every group is sealed",
        )
        return
    frontier_week = min(group.week for group in unsealed)
    frontier = [group for group in unsealed if group.week == frontier_week]
    execution = _mapping(goal.get("execution"))
    expected_checkpoint = f"W{frontier_week}"
    expected_lanes = [group.lane for group in frontier]
    if execution.get("activeCheckpoint") != expected_checkpoint:
        problems.add(
            "PROGRESS_ACTIVE_CHECKPOINT",
            f"{location}#/execution/activeCheckpoint",
            "activeCheckpoint does not match the lowest unsealed checkpoint",
        )
    if execution.get("activeLanes") != expected_lanes:
        problems.add(
            "PROGRESS_ACTIVE_LANES",
            f"{location}#/execution/activeLanes",
            "activeLanes do not match the unsealed checkpoint frontier",
        )

    week_states = _mapping(execution.get("weekStates"))
    for week in range(84, 93):
        week_groups = groups_by_week[week]
        entry_values = [
            _mapping(gate_by_id.get(group.gate_ids[0]).data).get("status")
            if gate_by_id.get(group.gate_ids[0]) is not None
            else "NotRun"
            for group in week_groups
        ]
        exit_values = [
            _mapping(gate_by_id.get(group.gate_ids[-1]).data).get("status")
            if gate_by_id.get(group.gate_ids[-1]) is not None
            else "NotRun"
            for group in week_groups
        ]
        expected_handoffs = [
            CANONICAL_HANDOFF_BY_GROUP[group.key]
            for group in week_groups
            if group.key in canonical_handoffs
        ]
        state = _mapping(week_states.get(f"W{week}"))
        if state.get("entryGate") != _aggregate_progress_status(entry_values):
            problems.add(
                "PROGRESS_WEEK_ENTRY",
                f"{location}#/execution/weekStates/W{week}/entryGate",
                "week entry state does not match its lane Entry Gates",
            )
        if state.get("exitGate") != _aggregate_progress_status(exit_values):
            problems.add(
                "PROGRESS_WEEK_EXIT",
                f"{location}#/execution/weekStates/W{week}/exitGate",
                "week exit state does not match its lane Exit Gates",
            )
        if state.get("handoffs") != expected_handoffs:
            problems.add(
                "PROGRESS_WEEK_HANDOFFS",
                f"{location}#/execution/weekStates/W{week}/handoffs",
                "week handoff list does not match canonical ready handoffs",
            )
        all_ready = len(expected_handoffs) == len(week_groups)
        review = state.get("review")
        if (all_ready and not isinstance(review, str)) or (
            not all_ready and review is not None
        ):
            problems.add(
                "PROGRESS_WEEK_REVIEW",
                f"{location}#/execution/weekStates/W{week}/review",
                "week review must exist exactly when all week handoffs are ready",
            )

    lane_states = _mapping(execution.get("laneStates"))
    logical_lanes = ("baseline", "renderer", "cli", "integration", "hardening", "acceptance")
    integration_sealed = "w90-integration" in sealed_group_ids
    for lane in logical_lanes:
        lane_groups = [group for group in contract.groups if group.lane == lane]
        lane_sealed = [
            _group_id(group) in sealed_group_ids for group in lane_groups
        ]
        all_lane_sealed = all(lane_sealed)
        active_groups = [group for group in frontier if group.lane == lane]
        if all_lane_sealed:
            expected_state = (
                "Integrated"
                if lane in {"renderer", "cli"} and integration_sealed
                else "ReadyForIntegration"
                if lane in {"renderer", "cli"}
                else "Complete"
            )
        elif active_groups:
            active_group = active_groups[0]
            active_gate_statuses = [
                _mapping(gate_by_id[gate_id].data).get("status")
                for gate_id in active_group.gate_ids
                if gate_id in gate_by_id
            ]
            waiting_for_user = bool(active_gate_statuses) and any(
                gate_id in MANUAL_GATE_REQUIREMENTS
                and gate_id in gate_by_id
                and _mapping(gate_by_id[gate_id].data).get("status") == "NotRun"
                for gate_id in active_group.gate_ids
            )
            expected_state = (
                "Blocked"
                if goal.get("status") == "Blocked" or "Failed" in active_gate_statuses
                else "WaitingUser"
                if waiting_for_user
                else "Active"
            )
        elif any(lane_sealed):
            # A parallel lane that already sealed an earlier checkpoint is
            # still in progress while its sibling finishes the shared week;
            # calling it NotStarted would erase durable progress.
            expected_state = "Active"
        else:
            expected_state = "NotStarted"
        state = _mapping(lane_states.get(lane))
        if state.get("state") != expected_state:
            problems.add(
                "PROGRESS_LANE_STATE",
                f"{location}#/execution/laneStates/{lane}/state",
                "lane state does not match the evidence frontier",
            )
        lane_handoffs = [
            canonical_handoffs[group.key]
            for group in lane_groups
            if group.key in canonical_handoffs
        ]
        expected_last_handoff = (
            lane_handoffs[-1].relative_path if lane_handoffs else None
        )
        if state.get("lastHandoff") != expected_last_handoff:
            problems.add(
                "PROGRESS_LANE_HANDOFF",
                f"{location}#/execution/laneStates/{lane}/lastHandoff",
                "lane lastHandoff does not match its latest canonical handoff",
            )


def validate_goal(
    goal_document: Document,
    contract: Contract,
    gate_by_id: Mapping[str, Document],
    handoff_documents: Sequence[Document],
    require_complete: bool,
    sealed_group_ids: set[str],
    pre_seal_group: str | None,
    problems: Problems,
) -> bool:
    goal = goal_document.data
    location = goal_document.relative_path
    if not isinstance(goal, Mapping):
        problems.add("GOAL_TYPE", location, "Goal control must be an object")
        return False
    registry = goal.get("gateRegistry")
    if registry != list(contract.registry):
        problems.add("GOAL_REGISTRY", location, "gateRegistry must be the exact ordered 104-ID registry")
    if isinstance(registry, list) and len(registry) != len(set(str(item) for item in registry)):
        problems.add("GOAL_REGISTRY_DUPLICATE", location, "gateRegistry contains duplicate IDs")

    status = goal.get("status")
    complete = status == "Complete"
    if require_complete and not complete:
        problems.add("GOAL_NOT_COMPLETE", location, "--require-complete requires central status Complete")

    gate_statuses = Counter()
    for gate_id in contract.registry:
        document = gate_by_id.get(gate_id)
        gate_statuses[
            _mapping(document.data).get("status") if document is not None else "NotRun"
        ] += 1
    expected_gate_rollup = {
        "total": 104,
        "passed": gate_statuses["Passed"],
        "failed": gate_statuses["Failed"],
        "notRun": gate_statuses["NotRun"],
        "notApplicable": gate_statuses["NotApplicable"],
    }
    _check_balanced_counts(
        problems,
        f"{location}#/gateRollup",
        goal.get("gateRollup"),
        "total",
        ("passed", "failed", "notRun", "notApplicable"),
    )
    _compare_rollup(problems, f"{location}#/gateRollup", goal.get("gateRollup"), expected_gate_rollup)

    command_keys = ("total", "passed", "failed", "notRun", "notApplicable")
    expected_command = _sum_rollup(gate_by_id.values(), "commandCounts", command_keys)
    _check_balanced_counts(
        problems,
        f"{location}#/commandRollup",
        goal.get("commandRollup"),
        "total",
        ("passed", "failed", "notRun", "notApplicable"),
    )
    _compare_rollup(problems, f"{location}#/commandRollup", goal.get("commandRollup"), expected_command)

    test_keys = ("discovered", "passed", "failed", "skipped", "notRun", "notApplicable")
    expected_test = _sum_rollup(gate_by_id.values(), "testCounts", test_keys)
    _check_balanced_counts(
        problems,
        f"{location}#/testRollup",
        goal.get("testRollup"),
        "discovered",
        ("passed", "failed", "skipped", "notRun", "notApplicable"),
    )
    _compare_rollup(problems, f"{location}#/testRollup", goal.get("testRollup"), expected_test)
    _check_cleanup(problems, f"{location}#/cleanup", goal.get("cleanup"), ready=complete)
    _check_p0_p1(problems, f"{location}#/openIssues", goal.get("openIssues"), complete)

    open_failures: list[tuple[str, str, Mapping[str, Any], Mapping[str, Any]]] = []
    for gate_id, document in gate_by_id.items():
        gate = _mapping(document.data)
        failure = gate.get("firstFailure")
        if gate.get("status") == "Failed" and isinstance(failure, Mapping):
            observed = failure.get("observedAt")
            open_failures.append(
                (
                    observed if isinstance(observed, str) else "",
                    gate_id,
                    gate,
                    failure,
                )
            )
    central_failure = goal.get("openFirstFailure")
    if not open_failures:
        if central_failure is not None:
            problems.add("CENTRAL_FIRST_FAILURE_STALE", location, "openFirstFailure must be null when no Gate is Failed")
    elif not isinstance(central_failure, Mapping):
        problems.add("CENTRAL_FIRST_FAILURE_MISSING", location, "a Failed Gate requires central openFirstFailure")
    else:
        _observed, expected_gate_id, expected_gate, expected_failure = sorted(open_failures)[0]
        expected_binding = {
            "failureId": expected_failure.get("failureId"),
            "observedAt": expected_failure.get("observedAt"),
            "checkpoint": expected_gate.get("checkpoint"),
            "lane": expected_gate.get("lane"),
            "gateId": expected_gate_id,
            "phase": expected_failure.get("phase"),
            "summary": expected_failure.get("summary"),
            "classification": expected_failure.get("classification"),
            "preserved": True,
            "supersededBy": expected_failure.get("supersededBy"),
        }
        for key, expected in expected_binding.items():
            if central_failure.get(key) != expected:
                problems.add("CENTRAL_FIRST_FAILURE_BINDING", location, f"openFirstFailure field {key} does not match earliest Failed Gate")
        if set(_list(central_failure.get("evidenceRefs"))) != set(
            _list(expected_failure.get("evidenceRefs"))
        ):
            problems.add("CENTRAL_FIRST_FAILURE_BINDING", location, "openFirstFailure evidenceRefs do not match Gate")

    if not complete:
        validate_active_progression(
            goal,
            contract,
            gate_by_id,
            handoff_documents,
            sealed_group_ids,
            pre_seal_group,
            problems,
            location,
        )
        goal_complete_handoffs = [
            document
            for document in handoff_documents
            if _mapping(document.data).get("decision") == "GoalComplete"
        ]
        if goal_complete_handoffs and not (
            pre_seal_group == "w92-acceptance"
            and len(goal_complete_handoffs) == 1
            and goal_complete_handoffs[0].relative_path == FINAL_HANDOFF_PATH
        ):
            problems.add(
                "PREMATURE_GOAL_COMPLETE",
                location,
                "GoalComplete handoff is allowed before central Complete only in W92 pre-seal mode",
            )
        return False

    if set(gate_by_id) != set(contract.registry) or len(gate_by_id) != 104:
        problems.add(
            "COMPLETE_GATE_SET",
            location,
            "Complete requires exactly the frozen 104 Gate result files",
        )
    for gate_id in contract.registry:
        document = gate_by_id.get(gate_id)
        if document is None or _mapping(document.data).get("status") != "Passed":
            problems.add("COMPLETE_GATE_STATUS", location, f"{gate_id} is not Passed")
    manual = _mapping(goal.get("manualAcceptances"))
    expected_manual = {
        "w86Visual",
        "w89Visual",
        "w91NarratorManualUx",
        "w92Visual",
    }
    if {key for key in expected_manual if manual.get(key) == "Passed"} != expected_manual:
        problems.add("COMPLETE_MANUAL", location, "all four central manual acceptances must be Passed")
    if goal.get("finalDecision") != "RefactorAccepted":
        problems.add("COMPLETE_DECISION", location, "Complete requires finalDecision=RefactorAccepted")

    final_handoffs = [
        document
        for document in handoff_documents
        if document.relative_path == FINAL_HANDOFF_PATH
        and _mapping(document.data).get("decision") == "GoalComplete"
    ]
    if len(final_handoffs) != 1:
        problems.add("COMPLETE_FINAL_HANDOFF", location, "exactly one final GoalComplete handoff is required")
    else:
        final_binding = _mapping(goal.get("finalHandoff"))
        if final_binding.get("path") != FINAL_HANDOFF_PATH:
            problems.add("COMPLETE_FINAL_HANDOFF", location, "central finalHandoff path mismatch")
        if final_binding.get("sha256") != final_handoffs[0].sha256:
            problems.add("COMPLETE_FINAL_HASH", location, "central finalHandoff hash does not match file bytes")
        if final_binding.get("decision") != "GoalComplete" or final_binding.get("finalDecision") != "RefactorAccepted":
            problems.add("COMPLETE_FINAL_HANDOFF", location, "central finalHandoff decision mismatch")

    covered_groups = {
        (_mapping(document.data).get("week"), _mapping(document.data).get("lane"))
        for document in handoff_documents
        if _mapping(document.data).get("decision") in {"ReadyForNextCheckpoint", "GoalComplete"}
    }
    missing_groups = sorted(set(contract.group_by_key) - covered_groups)
    if missing_groups:
        problems.add("COMPLETE_HANDOFF_COVERAGE", location, f"missing Ready handoff groups: {missing_groups}")

    execution = _mapping(goal.get("execution"))
    week_states = _mapping(execution.get("weekStates"))
    for week in range(84, 93):
        state = _mapping(week_states.get(f"W{week}"))
        if state.get("entryGate") != "Passed" or state.get("exitGate") != "Passed":
            problems.add("COMPLETE_WEEK_STATE", location, f"W{week} entry/exit must both be Passed")
    return True


def validate_semantics(
    goal_document: Document,
    gate_documents: Sequence[Document],
    handoff_documents: Sequence[Document],
    ledger_document: Document | None,
    contract: Contract,
    require_complete: bool = False,
    repo_root: Path | None = None,
    control_root: Path | None = None,
    sealed_group_ids: set[str] | None = None,
    pre_seal_group: str | None = None,
) -> list[str]:
    """Validate already-loaded documents and return deterministic problems."""
    evidence_root = control_root or repo_root
    problems = Problems()
    for document in gate_documents:
        validate_gate(document, contract, problems, repo_root=evidence_root)
    gate_by_id = _unique_gate_documents(gate_documents, problems)
    validate_global_evidence_ids(gate_by_id, problems)
    for document in handoff_documents:
        validate_handoff(document, contract, gate_by_id, problems)
    validate_week84_handoff_alias(handoff_documents, problems)
    validate_handoff_parent_chain(handoff_documents, problems)
    complete = validate_goal(
        goal_document,
        contract,
        gate_by_id,
        handoff_documents,
        require_complete,
        sealed_group_ids or set(),
        pre_seal_group,
        problems,
    )
    goal = _mapping(goal_document.data)
    if complete and repo_root is not None:
        _validate_current_complete_checkout(goal, repo_root, problems)
    _scan_value_for_secrets(goal, problems, f"{goal_document.relative_path}#")
    if ledger_document is not None:
        _scan_value_for_secrets(
            ledger_document.data,
            problems,
            f"{ledger_document.relative_path}#",
        )
    validate_provider_gate_requirements(gate_by_id, evidence_root, problems)
    validate_provider_boundaries(
        gate_by_id,
        evidence_root,
        problems,
        candidate_root=repo_root,
    )
    validate_controlled_writes(goal, gate_by_id, contract, complete, problems)
    validate_controlled_write_receipts(
        gate_by_id,
        evidence_root,
        problems,
        candidate_root=repo_root,
    )
    validate_w91_operator_evidence(gate_by_id, evidence_root, problems)
    validate_manual_acceptance_receipts(
        gate_by_id,
        handoff_documents,
        evidence_root,
        problems,
    )
    validate_provider_accounting(
        goal,
        gate_by_id,
        handoff_documents,
        ledger_document,
        complete,
        problems,
        repo_root=repo_root,
        control_root=evidence_root,
    )
    validate_candidate_identity(
        goal,
        gate_by_id,
        handoff_documents,
        contract,
        complete,
        problems,
        repo_root=repo_root,
    )
    return sorted(problems.items)


def _safe_repo_path(repo_root: Path, value: str, problems: Problems, label: str) -> Path | None:
    candidate = Path(value)
    if candidate.is_absolute():
        problems.add("PATH_ABSOLUTE", label, "evidence paths must be repository-relative")
        return None
    resolved = (repo_root / candidate).resolve()
    try:
        resolved.relative_to(repo_root.resolve())
    except ValueError:
        problems.add("PATH_ESCAPE", label, "path escapes repository")
        return None
    return resolved


def _load_schemas(schema_dir: Path, repo_root: Path, problems: Problems) -> dict[str, Document]:
    result: dict[str, Document] = {}
    for kind, filename in SCHEMA_FILENAMES.items():
        document = read_document(repo_root, schema_dir / filename, problems)
        if document is not None:
            result[kind] = document
    return result


def _schema_validate(
    document: Document,
    schema_document: Document,
    problems: Problems,
    validator_class: Any,
    format_checker: Any,
) -> None:
    try:
        validator_class.check_schema(schema_document.data)
        validator = validator_class(schema_document.data, format_checker=format_checker)
        errors = sorted(validator.iter_errors(document.data), key=lambda item: list(item.absolute_path))
        for error in errors:
            pointer = "".join(f"/{part}" for part in error.absolute_path)
            schema_pointer = "/".join(str(part) for part in error.absolute_schema_path)
            problems.add(
                "SCHEMA",
                f"{document.relative_path}#{pointer}",
                f"validation failed at schema keyword {error.validator!r} ({schema_pointer})",
            )
    except Exception as exc:  # reference resolution and malformed-schema errors fail closed
        problems.add(
            "SCHEMA_ENGINE",
            document.relative_path,
            f"schema validation could not complete ({type(exc).__name__})",
        )


def _resolve_cli_path(
    repo_root: Path,
    value: str,
    problems: Problems,
    label: str,
) -> Path | None:
    path = Path(value)
    resolved = (path if path.is_absolute() else repo_root / path).resolve()
    try:
        resolved.relative_to(repo_root.resolve())
    except ValueError:
        problems.add("PATH_ESCAPE", label, "CLI input path escapes repository")
        return None
    return resolved


def _derive_validation_roots(
    value: str | os.PathLike[str],
    problems: Problems,
) -> _ValidationRoots:
    """Derive the unique control worktree from one candidate-root input."""

    lexical = Path(value)
    if not lexical.is_absolute():
        lexical = Path.cwd() / lexical
    candidate = Path(os.path.abspath(lexical))
    try:
        candidate = trusted_executor._normalise_repo_root(candidate)
        context = trusted_executor._repository_context(candidate)
        if context.candidate_root != candidate:
            raise trusted_executor.ExecutorError("GIT_WORKTREE_TOPOLOGY")
    except (trusted_executor.ExecutorError, OSError, RuntimeError):
        # Repository trust owns the fail-closed diagnostic.  Keeping root
        # derivation side-effect free preserves same-root callers and test
        # harnesses that deliberately substitute that trust boundary.
        return _ValidationRoots(candidate, candidate, None)
    return _ValidationRoots(candidate, context.control_root, context)


def _artifact_child(artifacts_dir: Path, repository_relative: str) -> Path:
    relative = Path(repository_relative)
    try:
        suffix = relative.relative_to("artifacts")
    except ValueError as exc:  # constants are audited and this must never drift silently
        raise ValueError(f"not an artifacts path: {repository_relative}") from exc
    return artifacts_dir / suffix


def discover_goal_evidence_paths(
    repo_root: Path,
    artifacts_dir: Path,
    problems: Problems,
) -> tuple[list[Path], list[Path]]:
    """Discover only the 14 Goal artifact directories and approved handoffs."""
    gate_paths: list[Path] = []
    for relative_dir in GOAL_ARTIFACT_DIR_BY_GROUP.values():
        gates_dir = _artifact_child(artifacts_dir, relative_dir) / "gates"
        if gates_dir.is_dir():
            gate_paths.extend(sorted(gates_dir.glob("*.json")))

    handoff_paths: list[Path] = []
    for relative_path in (*CANONICAL_HANDOFF_PATHS, W84_COMPAT_HANDOFF_PATH):
        candidate = _artifact_child(artifacts_dir, relative_path)
        if candidate.is_file():
            handoff_paths.append(candidate)

    safe_gates: list[Path] = []
    safe_handoffs: list[Path] = []
    for kind, paths, destination in (
        ("Gate", gate_paths, safe_gates),
        ("handoff", handoff_paths, safe_handoffs),
    ):
        for path in paths:
            resolved = path.resolve()
            try:
                resolved.relative_to(repo_root.resolve())
            except ValueError:
                problems.add("PATH_ESCAPE", str(path), f"discovered {kind} path escapes repository")
                continue
            destination.append(resolved)
    return safe_gates, safe_handoffs


def _w84_g0_overlay_sha256(documents: Mapping[str, bytes]) -> str:
    """Hash an overlay exactly as the external trusted builder does."""

    digest = hashlib.sha256()
    digest.update(W84_G0_OVERLAY_PROTOCOL.encode("ascii") + b"\0")
    for path in sorted(documents, key=lambda value: value.encode("utf-8")):
        path_raw = path.encode("utf-8")
        raw = documents[path]
        digest.update(len(path_raw).to_bytes(8, "big"))
        digest.update(path_raw)
        digest.update(len(raw).to_bytes(8, "big"))
        digest.update(raw)
    return digest.hexdigest()


def _w84_g0_overlay_problem_codes(problems: Iterable[str]) -> set[str]:
    codes: set[str] = set()
    for item in problems:
        match = re.match(r"^\[([A-Z0-9_]+)\]", item)
        codes.add(match.group(1) if match is not None else "OVERLAY_VALIDATION")
    return codes


def _w84_g0_overlay_regular_bytes(
    root: Path,
    relative: str,
    *,
    allow_missing: bool,
) -> bytes | None:
    local_problems = Problems()
    path = _safe_relative_evidence_path(
        root,
        relative,
        local_problems,
        "overlay",
    )
    if path is None:
        raise RuntimeError("OVERLAY_PATH_UNSAFE")
    if not os.path.lexists(path):
        if allow_missing:
            return None
        raise RuntimeError("OVERLAY_FILE_MISSING")
    try:
        before = os.stat(path, follow_symlinks=False)
        if (
            not stat.S_ISREG(before.st_mode)
            or int(getattr(before, "st_nlink", 1)) != 1
            or before.st_size > W84_G0_OVERLAY_MAX_FILE_BYTES
        ):
            raise RuntimeError("OVERLAY_FILE_UNSAFE")
        raw = path.read_bytes()
        after = os.stat(path, follow_symlinks=False)
    except OSError:
        raise RuntimeError("OVERLAY_FILE_READ") from None
    identity = lambda value: (
        int(value.st_dev),
        int(value.st_ino),
        int(value.st_size),
        int(getattr(value, "st_mtime_ns", 0)),
    )
    if len(raw) != before.st_size or identity(before) != identity(after):
        raise RuntimeError("OVERLAY_FILE_DRIFT")
    return raw


def _w84_g0_overlay_scope_snapshot(
    root: Path,
    *,
    copy_root: Path | None = None,
) -> str:
    """Snapshot/copy only frozen Goal artifact roots with bounded raw reads."""

    rows: list[tuple[str, int, str]] = []
    seen: set[str] = set()
    total_bytes = 0
    for relative_root in trusted_executor.SEMANTIC_ARTIFACT_INPUT_ROOTS:
        source_root = root.joinpath(*PurePosixPath(relative_root).parts)
        if not os.path.lexists(source_root):
            continue
        try:
            root_info = os.lstat(source_root)
        except OSError:
            raise RuntimeError("OVERLAY_SCOPE_READ") from None
        if (
            not stat.S_ISDIR(root_info.st_mode)
            or stat.S_ISLNK(root_info.st_mode)
            or bool(getattr(root_info, "st_file_attributes", 0) & 0x400)
        ):
            raise RuntimeError("OVERLAY_SCOPE_UNSAFE")
        if copy_root is not None:
            copy_root.joinpath(*PurePosixPath(relative_root).parts).mkdir(
                parents=True,
                exist_ok=True,
            )
        stack = [source_root]
        while stack:
            directory = stack.pop()
            try:
                entries = sorted(
                    os.scandir(directory),
                    key=lambda entry: entry.name.encode("utf-8"),
                )
            except OSError:
                raise RuntimeError("OVERLAY_SCOPE_READ") from None
            for entry in entries:
                try:
                    # DirEntry.stat().st_nlink can be zero on Windows even for
                    # an ordinary single-link file; direct stat is reliable.
                    info = os.stat(entry.path, follow_symlinks=False)
                except OSError:
                    raise RuntimeError("OVERLAY_SCOPE_READ") from None
                if entry.is_symlink() or bool(
                    getattr(info, "st_file_attributes", 0) & 0x400
                ):
                    raise RuntimeError("OVERLAY_SCOPE_REPARSE")
                source = Path(entry.path)
                if stat.S_ISDIR(info.st_mode):
                    stack.append(source)
                    if copy_root is not None:
                        relative_directory = source.relative_to(root)
                        (copy_root / relative_directory).mkdir(
                            parents=True,
                            exist_ok=True,
                        )
                    continue
                if (
                    not stat.S_ISREG(info.st_mode)
                    or int(getattr(info, "st_nlink", 1)) != 1
                    or info.st_size > W84_G0_OVERLAY_MAX_FILE_BYTES
                ):
                    raise RuntimeError("OVERLAY_SCOPE_FILE_UNSAFE")
                relative = source.relative_to(root).as_posix()
                if relative in seen:
                    continue
                seen.add(relative)
                raw = _w84_g0_overlay_regular_bytes(
                    root,
                    relative,
                    allow_missing=False,
                )
                assert raw is not None
                total_bytes += len(raw)
                if (
                    len(rows) >= W84_G0_OVERLAY_MAX_FILES
                    or total_bytes > W84_G0_OVERLAY_MAX_TOTAL_BYTES
                ):
                    raise RuntimeError("OVERLAY_SCOPE_LIMIT")
                rows.append((relative, len(raw), hashlib.sha256(raw).hexdigest()))
                if copy_root is not None:
                    target = copy_root.joinpath(*PurePosixPath(relative).parts)
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(raw)
    rows.sort(key=lambda item: item[0].encode("utf-8"))
    return hashlib.sha256(_json_bytes(rows)).hexdigest()


def _w84_g0_overlay_write_stage(
    stage_root: Path,
    documents: Mapping[str, bytes],
) -> None:
    for relative, raw in documents.items():
        target = stage_root.joinpath(*PurePosixPath(relative).parts)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(raw)


def _w84_g0_overlay_parse_document(raw: bytes) -> Mapping[str, Any] | None:
    try:
        value = json.loads(
            raw.decode("utf-8"),
            object_pairs_hook=_no_duplicate_object,
        )
    except (UnicodeDecodeError, json.JSONDecodeError, DuplicateJsonKey):
        return None
    if not isinstance(value, Mapping) or raw != _json_bytes(value):
        return None
    return value


def _validate_w84_g0_overlay_impl(
    *,
    candidate_root: Path,
    control_root: Path,
    phase: str,
    product_candidate: str,
    overlay_documents: Mapping[str, bytes],
    expected_preimages: Mapping[str, bytes | None],
    transaction_id: str,
) -> Mapping[str, Any]:
    """Validate a complete W84-G0 publication view without publishing it.

    This is intentionally a non-CLI bootstrap API.  Tracked authority is read
    from ``candidate_root``; ignored evidence is copied into a bounded temporary
    control view and overlaid there.  The real control root remains the
    descriptor identity used to reconstruct product-verifier provenance.
    """

    checks = {name: False for name in W84_G0_OVERLAY_CHECKS}
    errors: set[str] = set()
    safe_documents: dict[str, bytes] = {}
    overlay_sha256 = "0" * 64
    expected_paths = (
        W84_G0_INITIAL_OVERLAY_PATHS
        if phase == "initial"
        else W84_G0_FINAL_OVERLAY_PATHS
        if phase == "final"
        else ()
    )

    def reject(code: str) -> None:
        if re.fullmatch(r"[A-Z][A-Z0-9_]{2,63}", code):
            errors.add(code)
        else:
            errors.add("OVERLAY_VALIDATION")

    def result() -> Mapping[str, Any]:
        ordered_errors = sorted(errors, key=lambda value: value.encode("ascii"))
        ok = not ordered_errors and all(value is True for value in checks.values())
        return {
            "protocol": W84_G0_OVERLAY_PROTOCOL,
            "ok": ok,
            "phase": phase,
            "productCandidate": product_candidate,
            "transactionId": transaction_id,
            "overlaySha256": overlay_sha256,
            "checks": checks,
            "errors": ordered_errors,
        }

    if not expected_paths:
        reject("OVERLAY_PHASE")
    if (
        not isinstance(product_candidate, str)
        or re.fullmatch(r"[0-9a-f]{40}", product_candidate) is None
        or not isinstance(transaction_id, str)
        or W84_G0_OVERLAY_TRANSACTION_ID.fullmatch(transaction_id) is None
    ):
        reject("OVERLAY_IDENTITY")
    if not isinstance(overlay_documents, Mapping):
        reject("OVERLAY_DOCUMENTS_TYPE")
    else:
        for path, raw in overlay_documents.items():
            if not isinstance(path, str) or not isinstance(raw, bytes):
                reject("OVERLAY_DOCUMENT_TYPE")
                continue
            safe_documents[path] = raw
        try:
            overlay_sha256 = _w84_g0_overlay_sha256(safe_documents)
        except (UnicodeError, OverflowError, TypeError, ValueError):
            reject("OVERLAY_HASH")
    if set(safe_documents) != set(expected_paths) or len(safe_documents) != len(
        expected_paths
    ):
        reject("OVERLAY_PATH_SET")
    if not isinstance(expected_preimages, Mapping) or set(expected_preimages) != set(
        expected_paths
    ):
        reject("OVERLAY_PREIMAGE_SET")
    else:
        for raw in expected_preimages.values():
            if raw is not None and not isinstance(raw, bytes):
                reject("OVERLAY_PREIMAGE_TYPE")
                break
    if errors:
        return result()

    parsed_documents: dict[str, Mapping[str, Any]] = {}
    secret_problems = Problems()
    for path in expected_paths:
        raw = safe_documents[path]
        if not raw or len(raw) > W84_G0_OVERLAY_MAX_FILE_BYTES:
            reject("OVERLAY_DOCUMENT_SIZE")
            continue
        document = _w84_g0_overlay_parse_document(raw)
        if document is None:
            reject("OVERLAY_DOCUMENT_CANONICAL_JSON")
            continue
        parsed_documents[path] = document
        _scan_value_for_secrets(document, secret_problems, "overlay")
        if any(
            pattern.search(view)
            for view in _decoded_secret_views(raw)
            for pattern in SECRET_PATTERNS
        ):
            reject("SECRET_DETECTED")
    secret_codes = _w84_g0_overlay_problem_codes(secret_problems.items)
    errors.update(secret_codes)
    checks["secretScan"] = not secret_codes and "SECRET_DETECTED" not in errors
    if len(parsed_documents) != len(expected_paths):
        return result()

    candidate: Path | None = None
    control: Path | None = None
    repository_context: Any = None
    git_problems = Problems()
    git_identity_before: tuple[Path, Mapping[str, Any]] | None = None
    try:
        if not isinstance(candidate_root, Path) or not isinstance(control_root, Path):
            raise trusted_executor.ExecutorError("REPOSITORY_CONTEXT")
        candidate = trusted_executor._normalise_repo_root(candidate_root)
        repository_context = trusted_executor._repository_context(candidate)
        control = repository_context.control_root
        if (
            candidate != candidate_root
            or control != control_root
            or repository_context.candidate_root != candidate
        ):
            raise trusted_executor.ExecutorError("GIT_WORKTREE_TOPOLOGY")
        validate_git_repository_trust(candidate, git_problems, repository_context)
        validate_bootstrap_control_plane(candidate, git_problems)
        if _git_stdout(candidate, ("rev-parse", "HEAD")) != product_candidate:
            git_problems.add(
                "OVERLAY_PRODUCT_CANDIDATE",
                "candidate",
                "candidate identity mismatch",
            )
        git_path, git_identity = trusted_executor._trusted_git_identity(
            candidate,
            rehash=True,
        )
        git_identity_before = (git_path, git_identity)
    except (trusted_executor.ExecutorError, OSError, RuntimeError):
        reject("OVERLAY_REPOSITORY_CONTEXT")
    git_codes = _w84_g0_overlay_problem_codes(git_problems.items)
    errors.update(git_codes)
    if candidate is None or control is None or repository_context is None:
        return result()

    preimage_goal = expected_preimages.get(trusted_executor.GOAL_STATE_PATH)
    if phase == "initial":
        if any(value is not None for value in expected_preimages.values()):
            reject("OVERLAY_INITIAL_PREIMAGE")
    else:
        if (
            not isinstance(preimage_goal, bytes)
            or any(
                value is not None
                for path, value in expected_preimages.items()
                if path != trusted_executor.GOAL_STATE_PATH
            )
            or _w84_g0_overlay_parse_document(preimage_goal) is None
            or preimage_goal == safe_documents[trusted_executor.GOAL_STATE_PATH]
        ):
            reject("OVERLAY_FINAL_PREIMAGE")

    live_states: list[str] = []
    live_before: dict[str, bytes | None] = {}
    try:
        for path in expected_paths:
            live = _w84_g0_overlay_regular_bytes(
                control,
                path,
                allow_missing=True,
            )
            live_before[path] = live
            preimage = expected_preimages[path]
            if live == safe_documents[path]:
                live_states.append("overlay")
            elif live == preimage:
                live_states.append("preimage")
            else:
                reject("OVERLAY_PREIMAGE_DRIFT")
        preimage_seen = False
        for state in live_states:
            if state == "preimage":
                preimage_seen = True
            elif preimage_seen:
                reject("OVERLAY_PUBLICATION_ORDER")
                break
        live_scope_before = _w84_g0_overlay_scope_snapshot(control)
    except RuntimeError as exc:
        reject(str(exc))
        live_scope_before = None
    if errors:
        return result()

    temporal_problems = Problems()
    goal_transition_problems = Problems()
    goal = parsed_documents[trusted_executor.GOAL_STATE_PATH]
    goal_identity = _mapping(goal.get("identity"))
    goal_updated = _timestamp(goal.get("updatedAt"))
    now = datetime.now(timezone.utc)
    if (
        goal_identity.get("productCandidate") != product_candidate
        or goal.get("status") != "Active"
    ):
        goal_transition_problems.add(
            "OVERLAY_GOAL_IDENTITY",
            "goal",
            "Goal identity transition is invalid",
        )
    if (
        not isinstance(goal.get("updatedAt"), str)
        or not str(goal.get("updatedAt")).endswith("Z")
        or goal_updated is None
        or goal_updated > now + timedelta(minutes=2)
    ):
        temporal_problems.add(
            "OVERLAY_TEMPORAL",
            "goal",
            "Goal timestamp is invalid",
        )
    execution = _mapping(goal.get("execution"))
    if phase == "initial":
        if (
            execution.get("activeCheckpoint") != "W84"
            or execution.get("activeLanes") != ["baseline"]
        ):
            goal_transition_problems.add(
                "OVERLAY_GOAL_INITIAL",
                "goal",
                "initial Goal frontier is invalid",
            )
    else:
        old_goal = _w84_g0_overlay_parse_document(preimage_goal or b"")
        gate = parsed_documents[W84_G0_GATE_PATH]
        old_updated = _timestamp(
            old_goal.get("updatedAt") if old_goal is not None else None
        )
        gate_started = _timestamp(gate.get("startedAt"))
        gate_finished = _timestamp(gate.get("finishedAt"))
        if (
            old_goal is None
            or _mapping(old_goal.get("identity")).get("productCandidate")
            != product_candidate
            or gate.get("gateId") != "W84-G0"
            or gate.get("resultPath") != W84_G0_GATE_PATH
            or gate.get("status") != "Passed"
            or _mapping(gate.get("identity")).get("productCandidate")
            != product_candidate
        ):
            goal_transition_problems.add(
                "OVERLAY_GOAL_FINAL",
                "goal",
                "final Goal/Gate transition is invalid",
            )
        if (
            old_updated is None
            or gate_started is None
            or gate_finished is None
            or goal_updated is None
            or not (old_updated <= gate_started <= gate_finished)
            or goal_updated != gate_finished
        ):
            temporal_problems.add(
                "OVERLAY_TEMPORAL",
                "goal",
                "final timestamps are not monotonic",
            )

    goal_codes = _w84_g0_overlay_problem_codes(goal_transition_problems.items)
    temporal_codes = _w84_g0_overlay_problem_codes(temporal_problems.items)
    errors.update(goal_codes)
    errors.update(temporal_codes)
    checks["goalTransition"] = not goal_codes
    checks["temporalMonotonicity"] = not temporal_codes

    schema_problems = Problems()
    requirement_problems = Problems()
    semantic_problems = Problems()
    integrity_codes: set[str] = set()
    anchor_codes: set[str] = set()
    initial_identity_problems = Problems()
    stage_guard: tempfile.TemporaryDirectory[str] | None = None
    try:
        stage_guard = tempfile.TemporaryDirectory(
            prefix="c-aicli-w84-g0-overlay-",
            dir=repository_context.common_dir,
        )
        stage_root = Path(stage_guard.name) / "control-view"
        stage_root.mkdir()
        copied_scope = _w84_g0_overlay_scope_snapshot(
            control,
            copy_root=stage_root,
        )
        staged_scope = _w84_g0_overlay_scope_snapshot(stage_root)
        if copied_scope != live_scope_before or staged_scope != copied_scope:
            reject("OVERLAY_SCOPE_DRIFT")
        _w84_g0_overlay_write_stage(stage_root, safe_documents)

        try:
            from jsonschema import Draft202012Validator, FormatChecker
        except ImportError:
            reject("OVERLAY_SCHEMA_ENGINE")
            Draft202012Validator = None  # type: ignore[assignment,misc]
            FormatChecker = None  # type: ignore[assignment,misc]

        schemas = _load_schemas(
            candidate / "docs_md/weekly",
            candidate,
            schema_problems,
        )
        contract = (
            extract_contract(
                _mapping(schemas["goal"].data),
                _mapping(schemas["gate"].data),
                schema_problems,
            )
            if set(schemas) == set(SCHEMA_FILENAMES)
            else Contract(
                CANONICAL_GATE_IDS,
                _canonical_groups_without_patterns(),
            )
        )
        goal_document = read_document(
            stage_root,
            stage_root / trusted_executor.GOAL_STATE_PATH,
            schema_problems,
        )
        gate_paths, handoff_paths = discover_goal_evidence_paths(
            stage_root,
            stage_root / "artifacts",
            schema_problems,
        )
        gate_documents = [
            document
            for path in gate_paths
            if (document := read_document(stage_root, path, schema_problems))
            is not None
        ]
        handoff_documents = [
            document
            for path in handoff_paths
            if (document := read_document(stage_root, path, schema_problems))
            is not None
        ]
        gate_ids = [
            _mapping(document.data).get("gateId") for document in gate_documents
        ]
        if gate_ids != ([] if phase == "initial" else ["W84-G0"]):
            schema_problems.add(
                "OVERLAY_GATE_SET",
                "overlay",
                "overlay Gate set is invalid",
            )
        if handoff_documents:
            schema_problems.add(
                "OVERLAY_HANDOFF_SET",
                "overlay",
                "W84-G0 publication cannot include a handoff",
            )

        requirements_document = read_document(
            candidate,
            candidate / GATE_REQUIREMENTS_PATH,
            requirement_problems,
        )
        ledger_document = read_document(
            stage_root,
            stage_root / LEDGER_PATH,
            semantic_problems,
        )
        if (
            Draft202012Validator is not None
            and FormatChecker is not None
            and goal_document is not None
        ):
            checker = FormatChecker()
            if "goal" in schemas:
                _schema_validate(
                    goal_document,
                    schemas["goal"],
                    schema_problems,
                    Draft202012Validator,
                    checker,
                )
            if "gate" in schemas:
                for document in gate_documents:
                    _schema_validate(
                        document,
                        schemas["gate"],
                        schema_problems,
                        Draft202012Validator,
                        checker,
                    )

        if goal_document is not None:
            validate_gate_requirements_manifest(
                requirements_document,
                goal_document,
                gate_documents,
                contract,
                requirement_problems,
                repo_root=candidate,
                control_root=stage_root,
                descriptor_evidence_root=control,
                handoff_documents=handoff_documents,
            )
            semantic_problems.extend(
                validate_semantics(
                    goal_document,
                    gate_documents,
                    handoff_documents,
                    ledger_document,
                    contract,
                    require_complete=False,
                    repo_root=candidate,
                    control_root=stage_root,
                    sealed_group_ids=set(),
                )
            )
            integrity_issues = goal_integrity.validate_goal_integrity(
                repo_root=candidate,
                control_root=stage_root,
                goal_state_path=stage_root / trusted_executor.GOAL_STATE_PATH,
                failure_ledger_path=(
                    stage_root / goal_integrity.FAILURE_LEDGER_PATH
                ),
                user_decision_ledger_path=(
                    stage_root / goal_integrity.USER_DECISION_LEDGER_PATH
                ),
                gate_paths=gate_paths,
                handoff_paths=handoff_paths,
            )
            integrity_codes.update(issue.code for issue in integrity_issues)
            anchor_issues = evidence_anchor.validate_evidence_anchors(
                candidate,
                control_root=stage_root,
                complete=False,
            )
            anchor_codes.update(issue.code for issue in anchor_issues)

        if phase == "initial":
            initial_evidence = [
                {
                    "path": W84_G0_ENTRY_PATH,
                    "kind": "plan-reference",
                    "sha256": hashlib.sha256(
                        safe_documents[W84_G0_ENTRY_PATH]
                    ).hexdigest(),
                },
                {
                    "path": W84_G0_BASELINE_IDENTITY_PATH,
                    "kind": "identity",
                    "sha256": hashlib.sha256(
                        safe_documents[W84_G0_BASELINE_IDENTITY_PATH]
                    ).hexdigest(),
                },
            ]
            _validate_w84_g0_baseline_identity(
                repo_root=candidate,
                control_root=stage_root,
                gate={"identity": {"productCandidate": product_candidate}},
                evidence=initial_evidence,
                problems=initial_identity_problems,
                location="W84-G0",
            )
    except (OSError, RuntimeError, trusted_executor.ExecutorError) as exc:
        code = str(exc)
        reject(code if re.fullmatch(r"[A-Z][A-Z0-9_]{2,63}", code) else "OVERLAY_STAGE")
    finally:
        if stage_guard is not None:
            try:
                stage_guard.cleanup()
            except OSError:
                reject("OVERLAY_STAGE_CLEANUP")

    schema_codes = _w84_g0_overlay_problem_codes(schema_problems.items)
    requirement_codes = _w84_g0_overlay_problem_codes(requirement_problems.items)
    semantic_codes = _w84_g0_overlay_problem_codes(semantic_problems.items)
    initial_codes = _w84_g0_overlay_problem_codes(initial_identity_problems.items)
    errors.update(schema_codes)
    errors.update(requirement_codes)
    errors.update(semantic_codes)
    errors.update(integrity_codes)
    errors.update(anchor_codes)
    errors.update(initial_codes)

    checks["schemas"] = not schema_codes
    checks["requirementsBinding"] = not requirement_codes
    checks["integrity"] = not integrity_codes and not anchor_codes
    provenance_codes = requirement_codes | semantic_codes | initial_codes
    command_codes = {
        code
        for code in provenance_codes
        if code.startswith(("PRODUCT_", "COMMAND_", "W84_G0_"))
    }
    semantic_report_codes = {
        code
        for code in provenance_codes
        if code.startswith(("TEST_REPORT_", "SEMANTIC_"))
        or code == "W84_G0_SUMMARY_SEMANTIC_REPORT"
    }
    checks["commandProvenance"] = phase == "initial" or not command_codes
    checks["semanticProvenance"] = (
        phase == "initial" or not semantic_report_codes
    )
    cleanup_codes = {
        code
        for code in semantic_codes | requirement_codes
        if "CLEANUP" in code or "RESIDUE" in code
    }
    checks["cleanupReceipts"] = not cleanup_codes
    temporal_validation_codes = {
        code
        for code in semantic_codes | requirement_codes | integrity_codes
        if any(token in code for token in ("TIME", "WINDOW", "TEMPORAL"))
    }
    if temporal_validation_codes:
        checks["temporalMonotonicity"] = False
    goal_validation_codes = {
        code
        for code in semantic_codes | requirement_codes | integrity_codes
        if any(token in code for token in ("FRONTIER", "GOAL_STATE_TRANSITION"))
    }
    if goal_validation_codes:
        checks["goalTransition"] = False
    secret_validation_codes = {
        code
        for code in semantic_codes | requirement_codes | integrity_codes
        if "SECRET" in code
    }
    if secret_validation_codes:
        checks["secretScan"] = False
    checks["fullSemantics"] = not (
        schema_codes
        or requirement_codes
        or semantic_codes
        or integrity_codes
        or anchor_codes
        or initial_codes
    )

    try:
        live_scope_after = _w84_g0_overlay_scope_snapshot(control)
        live_after = {
            path: _w84_g0_overlay_regular_bytes(
                control,
                path,
                allow_missing=True,
            )
            for path in expected_paths
        }
        git_after_path, git_after_identity = trusted_executor._trusted_git_identity(
            candidate,
            rehash=True,
        )
        if (
            live_scope_after != live_scope_before
            or live_after != live_before
            or git_identity_before != (git_after_path, git_after_identity)
        ):
            reject("OVERLAY_LIVE_TREE_MUTATION")
    except (OSError, RuntimeError, trusted_executor.ExecutorError):
        reject("OVERLAY_POSTCONDITION")
    checks["gitProvenance"] = not git_codes and not any(
        code
        in {
            "OVERLAY_REPOSITORY_CONTEXT",
            "OVERLAY_LIVE_TREE_MUTATION",
            "OVERLAY_POSTCONDITION",
        }
        for code in errors
    )

    # A check that became false through a categorized validator failure always
    # carries at least one stable code.  Never return a silent negative result.
    for name, passed in checks.items():
        if not passed and not errors:
            reject(f"OVERLAY_{name.upper()}")
    return result()


def validate_w84_g0_overlay(
    *,
    candidate_root: Path,
    control_root: Path,
    phase: str,
    product_candidate: str,
    overlay_documents: Mapping[str, bytes],
    expected_preimages: Mapping[str, bytes | None],
    transaction_id: str,
) -> Mapping[str, Any]:
    """Fail-closed, non-CLI entry point for W84-G0 overlay validation."""

    try:
        return _validate_w84_g0_overlay_impl(
            candidate_root=candidate_root,
            control_root=control_root,
            phase=phase,
            product_candidate=product_candidate,
            overlay_documents=overlay_documents,
            expected_preimages=expected_preimages,
            transaction_id=transaction_id,
        )
    except Exception:
        # Untrusted canonical/schema payloads and racing filesystem inputs must
        # never escape this protocol as Python exceptions.  Keep this boundary
        # deliberately terse: callers receive codes only, never local paths or
        # exception text.
        overlay_sha256 = "0" * 64
        try:
            safe_documents = {
                path: raw
                for path, raw in overlay_documents.items()
                if isinstance(path, str) and isinstance(raw, bytes)
            }
            if len(safe_documents) == len(overlay_documents):
                overlay_sha256 = _w84_g0_overlay_sha256(safe_documents)
        except Exception:
            pass
        return {
            "protocol": W84_G0_OVERLAY_PROTOCOL,
            "ok": False,
            "phase": phase,
            "productCandidate": product_candidate,
            "transactionId": transaction_id,
            "overlaySha256": overlay_sha256,
            "checks": {name: False for name in W84_G0_OVERLAY_CHECKS},
            "errors": ["OVERLAY_INTERNAL"],
        }


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", default=".", help="repository root (default: current directory)")
    parser.add_argument(
        "--schema-dir",
        default="docs_md/weekly",
        help="directory containing the three Week84-92 schemas",
    )
    parser.add_argument(
        "--goal-state",
        default="artifacts/week84-92-goal-control/goal-state.json",
        help="central Goal control JSON",
    )
    parser.add_argument(
        "--artifacts-dir",
        default="artifacts",
        help="artifact tree used for automatic Gate/handoff discovery",
    )
    parser.add_argument("--gate-file", action="append", default=[], help="explicit Gate JSON (repeatable)")
    parser.add_argument("--handoff-file", action="append", default=[], help="explicit handoff JSON (repeatable)")
    parser.add_argument("--require-complete", action="store_true", help="also require central status Complete")
    parser.add_argument(
        "--pre-seal-group",
        choices=[group.group_id for group in evidence_anchor.CANONICAL_GROUPS],
        default=None,
        help="allow exactly one fresh ready group to lack its anchor while reviewing the pre-seal state",
    )
    parser.add_argument("--json", action="store_true", dest="json_output", help="emit machine-readable summary")
    return parser


def _run_cli_without_git_bundle_guard(
    argv: Sequence[str] | None = None,
) -> int:
    args = build_parser().parse_args(argv)
    try:
        from jsonschema import Draft202012Validator, FormatChecker
    except ImportError as exc:
        message = f"jsonschema is required; validation fails closed: {exc}"
        if args.json_output:
            print(json.dumps({"ok": False, "exitCode": 2, "errors": [message]}))
        else:
            print(f"ERROR: {message}", file=sys.stderr)
        return 2

    requested_root = Path(args.repo_root).resolve()
    if not requested_root.is_dir():
        print(f"ERROR: repository root does not exist: {requested_root}", file=sys.stderr)
        return 2
    problems = Problems()
    roots = _derive_validation_roots(args.repo_root, problems)
    candidate_root = roots.candidate_root
    control_root = roots.control_root
    validate_git_repository_trust(
        candidate_root,
        problems,
        roots.repository_context,
    )
    preseal_started_at: str | None = None
    preseal_source_bindings: Mapping[str, Mapping[str, str]] | None = None
    if args.pre_seal_group is not None:
        preseal_started_at = _utc_now_z()
        try:
            preseal_source_bindings = (
                evidence_anchor.capture_preseal_source_bindings(candidate_root)
            )
        except evidence_anchor.AnchorBuildError as error:
            problems.add(
                error.code,
                error.location,
                "trusted pre-seal source capture failed",
            )
    validate_bootstrap_control_plane(candidate_root, problems)
    if args.require_complete and args.pre_seal_group is not None:
        problems.add(
            "CLI_MODE_CONFLICT",
            "command-line",
            "--require-complete and --pre-seal-group are mutually exclusive",
        )
    schema_dir = _resolve_cli_path(
        candidate_root, args.schema_dir, problems, "--schema-dir"
    )
    canonical_schema_dir = (candidate_root / "docs_md/weekly").resolve()
    if schema_dir is not None and schema_dir != canonical_schema_dir:
        problems.add(
            "FORMAL_SCHEMA_PATH",
            "--schema-dir",
            "formal validation requires the tracked canonical schema directory",
        )
    schemas = (
        _load_schemas(schema_dir, candidate_root, problems)
        if schema_dir is not None
        else {}
    )
    if set(schemas) == set(SCHEMA_FILENAMES):
        contract = extract_contract(
            _mapping(schemas["goal"].data), _mapping(schemas["gate"].data), problems
        )
    else:
        contract = Contract(CANONICAL_GATE_IDS, _canonical_groups_without_patterns())
    requirements_document = read_document(
        candidate_root,
        candidate_root / GATE_REQUIREMENTS_PATH,
        problems,
    )

    goal_path = _resolve_cli_path(
        control_root, args.goal_state, problems, "--goal-state"
    )
    canonical_goal_path = (
        control_root / "artifacts/week84-92-goal-control/goal-state.json"
    ).resolve()
    if goal_path is not None and goal_path != canonical_goal_path:
        problems.add(
            "FORMAL_GOAL_PATH",
            "--goal-state",
            "formal validation requires the canonical central Goal state",
        )
    goal_document = (
        read_document(control_root, goal_path, problems)
        if goal_path is not None
        else None
    )
    artifacts_dir = _resolve_cli_path(
        control_root, args.artifacts_dir, problems, "--artifacts-dir"
    )
    canonical_artifacts_dir = (control_root / "artifacts").resolve()
    if artifacts_dir is not None and artifacts_dir != canonical_artifacts_dir:
        problems.add(
            "FORMAL_ARTIFACTS_PATH",
            "--artifacts-dir",
            "formal validation requires the canonical artifacts root",
        )
    if args.gate_file:
        gate_paths = [
            path
            for item in args.gate_file
            if (
                path := _resolve_cli_path(
                    control_root, item, problems, "--gate-file"
                )
            )
            is not None
        ]
        discovered_handoffs: list[Path] = []
    elif artifacts_dir is not None and artifacts_dir.is_dir():
        gate_paths, discovered_handoffs = discover_goal_evidence_paths(
            control_root, artifacts_dir, problems
        )
    else:
        gate_paths, discovered_handoffs = [], []
    if args.handoff_file:
        handoff_paths = [
            path
            for item in args.handoff_file
            if (
                path := _resolve_cli_path(
                    control_root, item, problems, "--handoff-file"
                )
            )
            is not None
        ]
    else:
        if args.gate_file and artifacts_dir is not None and artifacts_dir.is_dir():
            _ignored_gates, discovered_handoffs = discover_goal_evidence_paths(
                control_root, artifacts_dir, problems
            )
        handoff_paths = discovered_handoffs
    gate_documents = [
        document
        for path in gate_paths
        if (document := read_document(control_root, path, problems)) is not None
    ]
    handoff_documents = [
        document
        for path in handoff_paths
        if (document := read_document(control_root, path, problems)) is not None
    ]
    snapshot_documents_by_path: dict[Path, Document] = {}
    for handoff_document in handoff_documents:
        snapshot_value = _mapping(
            _mapping(handoff_document.data).get("goalControlBinding")
        ).get("path")
        if not isinstance(snapshot_value, str):
            continue
        snapshot_path = _safe_repo_path(
            control_root,
            snapshot_value,
            problems,
            f"{handoff_document.relative_path}#/goalControlBinding/path",
        )
        if snapshot_path is None or snapshot_path in snapshot_documents_by_path:
            continue
        snapshot_document = read_document(control_root, snapshot_path, problems)
        if snapshot_document is not None:
            snapshot_documents_by_path[snapshot_path] = snapshot_document

    ledger_document: Document | None = None
    if goal_document is not None and isinstance(goal_document.data, Mapping):
        provider = _provider_summary(goal_document.data)
        ledger_value = provider.get("ledgerPath", LEDGER_PATH)
        if isinstance(ledger_value, str):
            ledger_path = _safe_repo_path(control_root, ledger_value, problems, "goal provider ledgerPath")
            if ledger_path is not None:
                ledger_document = read_document(control_root, ledger_path, problems)
        else:
            problems.add("PROVIDER_LEDGER_PATH", goal_document.relative_path, "ledgerPath must be a string")

    if goal_path is not None:
        integrity_issues = goal_integrity.validate_goal_integrity(
            repo_root=candidate_root,
            control_root=control_root,
            goal_state_path=goal_path,
            failure_ledger_path=(
                control_root / goal_integrity.FAILURE_LEDGER_PATH
            ),
            user_decision_ledger_path=(
                control_root / goal_integrity.USER_DECISION_LEDGER_PATH
            ),
            gate_paths=gate_paths,
            handoff_paths=handoff_paths,
        )
        for issue in integrity_issues:
            problems.add(
                issue.code,
                issue.location,
                "append-only integrity validation failed",
            )
    goal_status = (
        _mapping(goal_document.data).get("status")
        if goal_document is not None
        else None
    )
    if goal_status == "Complete" and args.pre_seal_group is not None:
        problems.add(
            "CLI_MODE_CONFLICT",
            "command-line",
            "pre-seal mode requires central status Active or Blocked",
        )
    anchor_complete = args.require_complete or goal_status == "Complete"
    anchor_arguments: dict[str, Any] = {
        "complete": anchor_complete,
        "allow_unsealed_ready_group": args.pre_seal_group,
    }
    if control_root != candidate_root:
        anchor_arguments["control_root"] = control_root
    anchor_issues = evidence_anchor.validate_evidence_anchors(
        candidate_root,
        **anchor_arguments,
    )
    for issue in anchor_issues:
        problems.add(
            issue.code,
            issue.location,
            "immutable evidence-anchor validation failed",
        )
    registry_issues = evidence_anchor.validate_anchor_registry(
        control_root,
        complete=anchor_complete,
    )
    for issue in registry_issues:
        problems.add(
            issue.code,
            issue.location,
            "immutable anchor-registry/CAS validation failed",
        )
    # Partial/active mode permits future registry entries to be absent, but an
    # already materialised anchor is never allowed to exist without its exact
    # registry/bundle pair.  This also covers every prior group during pre-seal.
    for spec in evidence_anchor.CANONICAL_GROUPS:
        anchor_path = candidate_root.joinpath(
            *PurePosixPath(spec.anchor_path).parts
        )
        if not (anchor_path.exists() or anchor_path.is_symlink()):
            continue
        registry_path = evidence_anchor.registry_path_for_group(spec.group_id)
        bundle_path = evidence_anchor.bundle_path_for_group(spec.group_id)
        registry_target = control_root.joinpath(
            *PurePosixPath(registry_path).parts
        )
        bundle_target = control_root.joinpath(
            *PurePosixPath(bundle_path).parts
        )
        if not (
            (registry_target.exists() or registry_target.is_symlink())
            and (bundle_target.exists() or bundle_target.is_symlink())
        ):
            problems.add(
                "REGISTRY_MISSING",
                registry_path,
                "every materialised immutable anchor requires its registry and CAS bundle",
            )
    validate_gate_requirements_manifest(
        requirements_document,
        goal_document,
        gate_documents,
        contract,
        problems,
        repo_root=candidate_root,
        control_root=control_root,
        handoff_documents=handoff_documents,
    )

    checker = FormatChecker()
    if goal_document is not None and "goal" in schemas:
        _schema_validate(goal_document, schemas["goal"], problems, Draft202012Validator, checker)
    if "goal" in schemas:
        for snapshot_document in snapshot_documents_by_path.values():
            _schema_validate(
                snapshot_document,
                schemas["goal"],
                problems,
                Draft202012Validator,
                checker,
            )
    if "gate" in schemas:
        for document in gate_documents:
            _schema_validate(document, schemas["gate"], problems, Draft202012Validator, checker)
    if "handoff" in schemas:
        for document in handoff_documents:
            _schema_validate(document, schemas["handoff"], problems, Draft202012Validator, checker)

    if goal_document is not None:
        sealed_group_ids = {
            group.group_id
            for group in evidence_anchor.CANONICAL_GROUPS
            if (candidate_root / group.anchor_path).is_file()
        }
        problems.extend(
            validate_semantics(
                goal_document,
                gate_documents,
                handoff_documents,
                ledger_document,
                contract,
                require_complete=args.require_complete,
                repo_root=candidate_root,
                control_root=control_root,
                sealed_group_ids=sealed_group_ids,
                pre_seal_group=args.pre_seal_group,
            )
        )
    if (
        not problems
        and args.pre_seal_group is not None
        and preseal_started_at is not None
        and preseal_source_bindings is not None
        and goal_document is not None
    ):
        product_candidate = _mapping(
            _mapping(goal_document.data).get("identity")
        ).get("productCandidate")
        if not isinstance(product_candidate, str):
            problems.add(
                "BUILD_PRESEAL_IDENTITY",
                goal_document.relative_path,
                "trusted pre-seal requires a product candidate",
            )
        else:
            try:
                evidence_anchor.write_preseal_receipt_document(
                    candidate_root,
                    args.pre_seal_group,
                    product_candidate,
                    control_root=control_root,
                    started_at=preseal_started_at,
                    finished_at=_utc_now_z(),
                    exit_code=0,
                    expected_source_bindings=preseal_source_bindings,
                )
            except evidence_anchor.AnchorBuildError as error:
                problems.add(
                    error.code,
                    error.location,
                    "trusted pre-seal receipt creation failed",
                )
    errors = sorted(set(problems.items))
    ok = not errors
    if args.json_output:
        print(
            json.dumps(
                {
                    "ok": ok,
                    "exitCode": 0 if ok else 1,
                    "goal": goal_document.relative_path if goal_document else None,
                    "gateFiles": len(gate_documents),
                    "handoffFiles": len(handoff_documents),
                    "errors": errors,
                },
                ensure_ascii=False,
                indent=2,
            )
        )
    elif ok:
        print(
            f"OK: schema + semantic validation passed "
            f"({len(gate_documents)} Gate files, {len(handoff_documents)} handoffs)."
        )
    else:
        print(f"FAIL: {len(errors)} validation problem(s).", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
    return 0 if ok else 1


def run_cli(argv: Sequence[str] | None = None) -> int:
    """Run one validation bundle under a raw pre/post trusted-Git pin."""

    parsed = build_parser().parse_args(argv)
    root = Path(parsed.repo_root).resolve()
    if not root.is_dir():
        return _run_cli_without_git_bundle_guard(argv)
    try:
        before_path, before_identity = trusted_executor._trusted_git_identity(
            root, rehash=True
        )
    except (trusted_executor.ExecutorError, OSError):
        message = "trusted Git core failed its raw pre-validation identity pin"
        if parsed.json_output:
            print(
                json.dumps(
                    {
                        "ok": False,
                        "exitCode": 1,
                        "errors": [message],
                    }
                )
            )
        else:
            print(f"ERROR: {message}", file=sys.stderr)
        return 1

    captured_stdout = io.StringIO()
    captured_stderr = io.StringIO()
    try:
        with redirect_stdout(captured_stdout), redirect_stderr(captured_stderr):
            exit_code = _run_cli_without_git_bundle_guard(argv)
    finally:
        try:
            after_path, after_identity = trusted_executor._trusted_git_identity(
                root, rehash=True
            )
        except (trusted_executor.ExecutorError, OSError):
            after_path, after_identity = None, None
    if after_path != before_path or after_identity != before_identity:
        message = "trusted Git core changed during the validation bundle"
        if parsed.json_output:
            print(
                json.dumps(
                    {
                        "ok": False,
                        "exitCode": 1,
                        "errors": [message],
                    }
                )
            )
        else:
            print(f"ERROR: {message}", file=sys.stderr)
        return 1
    sys.stdout.write(captured_stdout.getvalue())
    sys.stderr.write(captured_stderr.getvalue())
    return exit_code


if __name__ == "__main__":
    raise SystemExit(run_cli())
