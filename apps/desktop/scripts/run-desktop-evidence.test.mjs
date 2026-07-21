import { describe, expect, it } from "vitest";
import { evaluateRun } from "./run-desktop-evidence.mjs";

describe("Desktop source-bound evidence", () => {
  it("requires the exact packaged smoke count and cleanup", () => {
    expect(evaluateRun("smoke", 0, { expected: 8, unexpected: 0, flaky: 0, skipped: 0 }, { processDelta: 0, tempDelta: 0 }).passed).toBe(true);
    expect(evaluateRun("smoke", 0, { expected: 7, unexpected: 0, flaky: 0, skipped: 0 }, { processDelta: 0, tempDelta: 0 }).passed).toBe(false);
    expect(evaluateRun("smoke", 0, { expected: 8, unexpected: 0, flaky: 0, skipped: 0 }, { processDelta: 1, tempDelta: 0 }).passed).toBe(false);
  });

  it("requires both unpacked and packaged accessibility hardening", () => {
    expect(evaluateRun("accessibility", 0, { expected: 2, unexpected: 0, flaky: 0, skipped: 0 }, { processDelta: 0, tempDelta: 0 }).passed).toBe(true);
    expect(evaluateRun("accessibility", 1, { expected: 1, unexpected: 1, flaky: 0, skipped: 0 }, { processDelta: 0, tempDelta: 0 }).passed).toBe(false);
  });
});
