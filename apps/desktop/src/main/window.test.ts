import { describe, expect, it } from "vitest";
import { EXACT_DEVELOPMENT_URL, resolveDevelopmentUrl } from "./security";

describe("desktop renderer target", () => {
  it("allows only the exact loopback development URL", () => {
    expect(resolveDevelopmentUrl(undefined)).toBeNull();
    expect(resolveDevelopmentUrl(EXACT_DEVELOPMENT_URL)).toBe(EXACT_DEVELOPMENT_URL);
    for (const value of [
      "http://localhost:5173/",
      "http://127.0.0.1:5174/",
      "http://127.0.0.1:5173/path",
      "https://127.0.0.1:5173/",
    ]) expect(() => resolveDevelopmentUrl(value)).toThrow("Invalid development renderer URL");
  });
});
