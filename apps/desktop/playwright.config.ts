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
    { name: "week80-memory-diagnosis", testMatch: /week80-memory-diagnosis\.spec\.ts/ },
    {
      name: "week80-provider-memory",
      testMatch: /week80-provider-memory\.spec\.ts/,
      use: { trace: "off", screenshot: "off", video: "off" },
    },
    {
      name: "week81-reload-diagnosis",
      testMatch: /week81-reload-diagnosis\.spec\.ts/,
      use: { trace: "off", screenshot: "off", video: "off" },
    },
  ],
});
