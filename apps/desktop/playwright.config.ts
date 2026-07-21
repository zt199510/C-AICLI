import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  timeout: 30_000,
  expect: { timeout: 8_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [["list"]],
  use: { trace: "retain-on-failure", screenshot: "only-on-failure" },
  projects: [
    { name: "unpacked", testMatch: /(?:read-only-shell|desktop-recovery|long-session|week76-hardening)\.spec\.ts/ },
    { name: "packaged", testMatch: /(?:read-only-shell|desktop-recovery|week76-hardening)\.spec\.ts/ },
  ],
});
