from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
from typing import Any, Sequence

try:
    from tools import week84_92_trusted_executor as trusted_executor
except ImportError:  # direct execution from the tools directory
    import week84_92_trusted_executor as trusted_executor


WEEK83_CANDIDATE = "ccf9d82c9fa76c201876ee01d3849902989091e9"
WEEK83_DOCS = "7cd1eac2b3ba5f8b9aa9dd6263dcb83d9dd66cd3"
BOOTSTRAP_PARENT = "e6b5c7f71d06a22c70bf534e6764bcb96ef06337"
ENTRY_PATH = Path(
    "artifacts/week84-renderer-listener-retention/gate-evidence/W84-G0/entry.json"
)
PRODUCT_INPUT_PATHS = (
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
BOOTSTRAP_PATHS = (
    "docs_md/plans/.gitattributes",
    "docs_md/plans/07_cli_desktop_experience_refactor.plan.md",
    "docs_md/weekly/.gitattributes",
    "docs_md/weekly/84_92_week_cli_desktop_experience_refactor_schedule.md",
    "docs_md/weekly/84_92_week_gate_requirements.json",
    "docs_md/weekly/84_92_week_gate_result.schema.json",
    "docs_md/weekly/84_92_week_goal_control.schema.json",
    "docs_md/weekly/84_92_week_goal_execution_contract.md",
    "docs_md/weekly/84_92_week_handoff.schema.json",
    "docs_md/weekly/84_92_command_control/W84-G0.json",
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
    "tools/.gitattributes",
    "tools/test_validate_week84_92_goal_evidence.py",
    "tools/test_week84_92_evidence_anchor.py",
    "tools/test_week84_92_gate_requirements.py",
    "tools/test_week84_92_goal_integrity.py",
    "tools/test_week84_92_trusted_executor.py",
    "tools/validate-week84-92-goal-evidence.py",
    "tools/week84_92_commands/goal-contract-unittest.py",
    "tools/week84_92_commands/goal-control-schema-validate.py",
    "tools/week84_92_commands/prior-handoff-schema-validate.py",
    "tools/week84_92_commands/trusted-executor-unittest.py",
    "tools/week84_92_commands/week83-lineage-identity-verify.py",
    "tools/week84_92_evidence_anchor.py",
    "tools/week84_92_goal_integrity.py",
    "tools/week84_92_provider_scenarios/controlled_write.py",
    "tools/week84_92_provider_scenarios/provider_read_only.py",
    "tools/week84_92_provider_scenarios/provider_recovery.py",
    "tools/week84_92_provider_scenarios/provider_resource.py",
    "tools/week84_92_provider_turn_harness.py",
    "tools/week84_92_trusted_executor.py",
)


class DuplicateKeyError(ValueError):
    pass


def no_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise DuplicateKeyError(key)
        result[key] = value
    return result


def git(
    repo_root: Path, arguments: Sequence[str]
) -> subprocess.CompletedProcess[bytes]:
    before_path, before_identity = trusted_executor._trusted_git_identity(
        repo_root, rehash=True
    )
    result = trusted_executor._git(repo_root, arguments, check=False)
    after_path, after_identity = trusted_executor._trusted_git_identity(
        repo_root, rehash=True
    )
    if before_path != after_path or before_identity != after_identity:
        raise trusted_executor.ExecutorError("GIT_TOOL_IDENTITY")
    return result


def ancestor(repo_root: Path, older: str, newer: str) -> bool:
    return (
        git(repo_root, ("merge-base", "--is-ancestor", older, newer)).returncode
        == 0
    )


def is_reparse(path: Path) -> bool:
    try:
        attributes = getattr(os.lstat(path), "st_file_attributes", 0)
    except OSError:
        return True
    return path.is_symlink() or bool(attributes & 0x400)


def main() -> int:
    try:
        root = Path.cwd().resolve()
        git_control = root / ".git"
        entry = json.loads(
            ENTRY_PATH.read_text(encoding="utf-8"),
            object_pairs_hook=no_duplicates,
        )
        head_result = git(root, ("rev-parse", "HEAD"))
        head = head_result.stdout.decode("ascii").strip()
        top_result = git(root, ("rev-parse", "--show-toplevel"))
        git_dir_result = git(root, ("rev-parse", "--absolute-git-dir"))
        object_format_result = git(root, ("rev-parse", "--show-object-format"))
        shallow_result = git(root, ("rev-parse", "--is-shallow-repository"))
        replace_result = git(
            root,
            ("for-each-ref", "--format=%(refname)", "refs/replace/")
        )
        top = Path(top_result.stdout.decode("utf-8").strip()).resolve(strict=True)
        git_dir = Path(
            git_dir_result.stdout.decode("utf-8").strip()
        ).resolve(strict=True)
        bootstrap = entry.get("goalBootstrapRevision")
        parent_line = git(
            root, ("rev-list", "--parents", "-n", "1", str(bootstrap))
        )
        parents = parent_line.stdout.decode("ascii").strip().split()
        changed = git(
            root,
            (
                "diff",
                "--no-ext-diff",
                "--no-textconv",
                "--name-only",
                BOOTSTRAP_PARENT,
                str(bootstrap),
            ),
        )
        changed_paths = tuple(
            sorted(
                path
                for path in changed.stdout.decode("utf-8").splitlines()
                if path
            )
        )
        product_diff = git(
            root,
            (
                "diff",
                "--no-ext-diff",
                "--no-textconv",
                "--quiet",
                WEEK83_CANDIDATE,
                str(bootstrap),
                "--",
                *PRODUCT_INPUT_PATHS,
            )
        )
        passed = (
            isinstance(entry, dict)
            and entry.get("schemaVersion") == "1.0.0"
            and entry.get("gateId") == "W84-G0"
            and entry.get("status") == "Passed"
            and entry.get("productCandidate") == bootstrap
            and entry.get("week83ProductCandidate") == WEEK83_CANDIDATE
            and entry.get("week83DocumentationClosure") == WEEK83_DOCS
            and entry.get("bootstrapParentRevision") == BOOTSTRAP_PARENT
            and isinstance(bootstrap, str)
            and head == bootstrap
            and entry.get("bootstrapChangedPaths") == list(BOOTSTRAP_PATHS)
            and entry.get("bootstrapChangedPathCount") == len(BOOTSTRAP_PATHS)
            and entry.get("productInputPaths") == list(PRODUCT_INPUT_PATHS)
            and entry.get("productInputDiffCount") == 0
            and head_result.returncode == 0
            and top_result.returncode == 0
            and git_dir_result.returncode == 0
            and object_format_result.returncode == 0
            and shallow_result.returncode == 0
            and replace_result.returncode == 0
            and top == root
            and git_control.is_dir()
            and not is_reparse(git_control)
            and git_dir == git_control.resolve(strict=True)
            and object_format_result.stdout.decode("ascii").strip() == "sha1"
            and shallow_result.stdout.decode("ascii").strip() == "false"
            and not replace_result.stdout.strip()
            and not (git_control / "shallow").exists()
            and not (git_control / "info" / "grafts").exists()
            and not (git_control / "objects" / "info" / "alternates").exists()
            and parent_line.returncode == 0
            and parents == [bootstrap, BOOTSTRAP_PARENT]
            and changed.returncode == 0
            and changed_paths == tuple(sorted(BOOTSTRAP_PATHS))
            and product_diff.returncode == 0
            and ancestor(root, WEEK83_CANDIDATE, str(bootstrap))
            and ancestor(root, WEEK83_DOCS, str(bootstrap))
        )
    except (
        OSError,
        UnicodeError,
        json.JSONDecodeError,
        DuplicateKeyError,
        subprocess.SubprocessError,
        trusted_executor.ExecutorError,
    ):
        passed = False
    print("Week83/bootstrap lineage identity " + ("passed" if passed else "failed"))
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
