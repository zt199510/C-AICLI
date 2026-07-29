from __future__ import annotations

import sys
import unittest


def main() -> int:
    suite = unittest.defaultTestLoader.loadTestsFromName(
        "tools.test_week84_92_trusted_executor"
    )
    result = unittest.TextTestRunner(stream=sys.stdout, verbosity=1).run(suite)
    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    raise SystemExit(main())
