import { describe, expect, it } from "vitest";
import { percentChange, validateProfiles } from "./measure-performance.mjs";

const passingProfile = (profile) => ({
  schemaVersion: 2,
  status: "Passed",
  workload: "week77-long-session-v2",
  profile,
  retention: { idleWorkingSetPercent: 8, idlePrivateBytesPercent: 12, reloadTransientPeakWorkingSetPercent: 40 },
  cleanup: { processDelta: 0, tempDelta: 0 },
});

describe("Week 77 performance evidence", () => {
  it("requires five consecutive isolated profiles", () => {
    expect(validateProfiles([1, 2, 3, 4, 5].map(passingProfile))).toEqual({ passed: true, reason: null });
    expect(validateProfiles([1, 2, 3, 4].map(passingProfile)).passed).toBe(false);
  });

  it("retains any threshold or cleanup failure", () => {
    const profiles = [1, 2, 3, 4, 5].map(passingProfile);
    profiles[1].retention.idlePrivateBytesPercent = 15.01;
    expect(validateProfiles(profiles).passed).toBe(false);
    profiles[1] = passingProfile(2);
    profiles[3].cleanup.processDelta = 1;
    expect(validateProfiles(profiles).passed).toBe(false);
  });

  it("uses the frozen Week 66 growth denominator", () => {
    expect(percentChange(100, 115)).toBe(15);
  });
});
