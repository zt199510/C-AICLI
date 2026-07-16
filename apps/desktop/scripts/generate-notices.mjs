import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const desktopRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const lockPath = path.join(desktopRoot, "package-lock.json");
const noticePath = path.join(desktopRoot, "THIRD_PARTY_NOTICES.md");
const checkOnly = process.argv.includes("--check");
const lock = JSON.parse(await readFile(lockPath, "utf8"));
const allowedLicenses = new Set([
  "0BSD",
  "Apache-2.0",
  "BlueOak-1.0.0",
  "BSD-2-Clause",
  "BSD-3-Clause",
  "ISC",
  "MIT",
  "MPL-2.0",
  "(MIT OR CC0-1.0)",
]);

function packageName(packagePath) {
  const marker = "node_modules/";
  const index = packagePath.lastIndexOf(marker);
  return index < 0 ? packagePath : packagePath.slice(index + marker.length);
}

const dependencies = Object.entries(lock.packages)
  .filter(([packagePath]) => packagePath.includes("node_modules/"))
  .map(([packagePath, metadata]) => ({
    name: packageName(packagePath),
    version: metadata.version ?? "unknown",
    license: metadata.license ?? "UNDECLARED",
    resolved: metadata.resolved ?? "",
  }))
  .sort((left, right) =>
    left.name.localeCompare(right.name) || left.version.localeCompare(right.version),
  );

for (const dependency of dependencies) {
  if (!allowedLicenses.has(dependency.license)) {
    throw new Error(
      `Dependency license requires review: ${dependency.name}@${dependency.version} (${dependency.license})`,
    );
  }
  if (!dependency.resolved.startsWith("https://registry.npmjs.org/")) {
    throw new Error(`Dependency is not registry-pinned: ${dependency.name}@${dependency.version}`);
  }
}

const counts = new Map();
for (const dependency of dependencies) {
  counts.set(dependency.license, (counts.get(dependency.license) ?? 0) + 1);
}

const lines = [
  "# C-AICLI Desktop Third-Party Dependency Inventory",
  "",
  "Generated from `package-lock.json`. Do not edit manually.",
  "",
  `- Packages: ${dependencies.length}`,
  `- Registry sources outside npmjs.org: 0`,
  `- License summary: ${[...counts.entries()]
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([license, count]) => `${license} (${count})`)
    .join(", ")}`,
  "",
  "The packaged Electron runtime also carries Electron's `LICENSE` and Chromium's `LICENSES.chromium.html` files next to the executable.",
  "",
  "| Package | Version | License |",
  "|---|---:|---|",
];

for (const dependency of dependencies) {
  lines.push(`| ${dependency.name} | ${dependency.version} | ${dependency.license} |`);
}

const expected = `${lines.join("\n")}\n`;
if (checkOnly) {
  const actual = await readFile(noticePath, "utf8").catch(() => "");
  if (actual !== expected) throw new Error("THIRD_PARTY_NOTICES.md is stale.");
} else {
  await writeFile(noticePath, expected, "utf8");
}
