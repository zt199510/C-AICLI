from __future__ import annotations

import sys
import unittest


MODULES = (
    "tools.test_validate_week84_92_goal_evidence",
    "tools.test_week84_92_goal_integrity",
    "tools.test_week84_92_gate_requirements",
    "tools.test_week84_92_evidence_anchor",
    "tools.test_week84_92_trusted_executor",
)


def main() -> int:
    suite = unittest.defaultTestLoader.loadTestsFromNames(MODULES)
    result = unittest.TextTestRunner(stream=sys.stdout, verbosity=1).run(suite)
    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    raise SystemExit(main())
