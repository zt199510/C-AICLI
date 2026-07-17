import { expect } from "@playwright/test";
import { isThreadGetResult, isThreadListResult } from "../src/generated/desktop-contracts";

export function assertFixtureProjection(value: { list: unknown; detail: unknown }) {
  expect(isThreadListResult(value.list)).toBe(true);
  expect(isThreadGetResult(value.detail)).toBe(true);
}
