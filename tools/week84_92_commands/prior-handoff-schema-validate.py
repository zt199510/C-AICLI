from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
from typing import Any

try:
    from tools import week84_92_trusted_executor as trusted_executor
except ImportError:  # direct execution from the tools directory
    import week84_92_trusted_executor as trusted_executor


WEEK83_CANDIDATE = "ccf9d82c9fa76c201876ee01d3849902989091e9"
WEEK83_DOCS = "7cd1eac2b3ba5f8b9aa9dd6263dcb83d9dd66cd3"
HANDOFF_PATH = Path(
    "artifacts/week83-approval-projection-remediation/week84-handoff.json"
)
VERIFICATION_PATH = Path(
    "artifacts/week84-renderer-listener-retention/gate-evidence/"
    "W84-G0/week83-handoff-verification.json"
)
SHA256 = re.compile(r"^[0-9A-F]{64}$")


class DuplicateKeyError(ValueError):
    pass


def no_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise DuplicateKeyError(key)
        result[key] = value
    return result


def read_json(path: Path) -> tuple[dict[str, Any], bytes]:
    raw = path.read_bytes()
    data = json.loads(raw.decode("utf-8"), object_pairs_hook=no_duplicates)
    if not isinstance(data, dict):
        raise ValueError("object required")
    return data, raw


def git_ok(repo_root: Path, *arguments: str) -> bool:
    before_path, before_identity = trusted_executor._trusted_git_identity(
        repo_root, rehash=True
    )
    completed = trusted_executor._git(
        repo_root, arguments, check=False
    )
    after_path, after_identity = trusted_executor._trusted_git_identity(
        repo_root, rehash=True
    )
    return (
        before_path == after_path
        and before_identity == after_identity
        and completed.returncode == 0
    )


def main() -> int:
    try:
        root = Path.cwd().resolve()
        handoff, raw = read_json(HANDOFF_PATH)
        verification, _verification_raw = read_json(VERIFICATION_PATH)
        handoff_sha = hashlib.sha256(raw).hexdigest()
        preserved = handoff.get("preservedEvidence")
        passed = (
            handoff.get("schemaVersion") == "week84-handoff/v1"
            and handoff.get("status") == "Blocked"
            and handoff.get("candidateReady") is False
            and handoff.get("week83ProductCandidate") == WEEK83_CANDIDATE
            and handoff.get("week83DocumentationClosureRevision") == WEEK83_DOCS
            and handoff.get("blockingGate") == "W83-G5"
            and handoff.get("openP0") == 0
            and handoff.get("openP1") == 1
            and isinstance(handoff.get("passed"), list)
            and isinstance(handoff.get("failed"), list)
            and len(handoff["failed"]) == 1
            and isinstance(handoff.get("notRun"), list)
            and isinstance(handoff.get("requiredRemediation"), list)
            and isinstance(preserved, dict)
            and bool(preserved)
            and all(
                isinstance(value, str) and SHA256.fullmatch(value) is not None
                for value in preserved.values()
            )
            and verification.get("schemaVersion") == "1.0.0"
            and verification.get("gateId") == "W84-G0"
            and verification.get("status") == "Passed"
            and verification.get("sourcePath") == HANDOFF_PATH.as_posix()
            and verification.get("sourceSha256") == handoff_sha
            and verification.get("priorStatus") == "Blocked"
            and verification.get("week83ProductCandidate") == WEEK83_CANDIDATE
            and verification.get("week83DocumentationClosure") == WEEK83_DOCS
            and verification.get("openP0") == 0
            and verification.get("openP1") == 1
            and git_ok(
                root, "cat-file", "-e", f"{WEEK83_CANDIDATE}^{{commit}}"
            )
            and git_ok(
                root, "cat-file", "-e", f"{WEEK83_DOCS}^{{commit}}"
            )
        )
    except (
        OSError,
        UnicodeError,
        json.JSONDecodeError,
        DuplicateKeyError,
        ValueError,
        subprocess.SubprocessError,
        trusted_executor.ExecutorError,
    ):
        passed = False
    print("prior Week83 handoff validation " + ("passed" if passed else "failed"))
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
