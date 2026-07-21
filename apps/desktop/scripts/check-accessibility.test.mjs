import { describe, expect, it } from "vitest";
import { checkAccessibilityStyles, contrastRatio } from "./check-accessibility.mjs";

describe("Desktop accessibility style gate", () => {
  it("checks WCAG AA text, state and focus contrast pairs", async () => {
    expect(contrastRatio("#626a70", "#ffffff")).toBeGreaterThanOrEqual(4.5);
    await expect(checkAccessibilityStyles()).resolves.toMatchObject({ status: "Passed", standard: "WCAG 2.x AA" });
  });
});
