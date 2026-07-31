import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const desktopRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

export function contrastRatio(foreground, background) {
  const values = [foreground, background].map((value) => {
    const channels = value.replace("#", "").match(/.{2}/g)?.map((channel) => Number.parseInt(channel, 16) / 255);
    if (!channels || channels.length !== 3 || channels.some(Number.isNaN)) throw new Error(`Invalid contrast color: ${value}`);
    const [red, green, blue] = channels.map((channel) => channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4);
    return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
  });
  return (Math.max(...values) + 0.05) / (Math.min(...values) + 0.05);
}

export async function checkAccessibilityStyles(stylesPath = path.join(desktopRoot, "src", "renderer", "styles.css")) {
  const styles = await readFile(stylesPath, "utf8");
  const tokenStyles = await readFile(path.join(desktopRoot, "src", "renderer", "shared", "theme", "tokens.css"), "utf8");
  for (const required of ["@media (prefers-reduced-motion:reduce)", "@media (forced-colors:active)", "button:focus-visible", "animation:none", "color:#626a70", "background:#f0f1f2"]) {
    if (!styles.includes(required)) throw new Error(`Accessibility stylesheet is missing: ${required}`);
  }
  for (const required of [
    "--color-text", "--color-text-muted", "--color-text-disabled", "--color-accent",
    "--color-warning", "--color-warning-soft", "--color-danger", "--color-danger-soft",
    "--color-focus", "--duration-pulse",
  ]) {
    if (!tokenStyles.includes(`${required}:`)) throw new Error(`Semantic token stylesheet is missing: ${required}`);
  }
  const token = (name) => {
    const match = new RegExp(`${name}:\\s*(#[0-9a-fA-F]{6})`, "u").exec(tokenStyles);
    if (!match) throw new Error(`Semantic color token is invalid: ${name}`);
    return match[1];
  };
  const pairs = [
    ["body", token("--color-text"), token("--color-canvas"), 4.5],
    ["secondary", token("--color-text-muted"), token("--color-surface"), 4.5],
    ["disabled-control", token("--color-text-disabled"), token("--color-surface-muted"), 4.5],
    ["success-chip", token("--color-success"), token("--color-success-soft"), 4.5],
    ["failure-chip", token("--color-danger"), token("--color-danger-soft"), 4.5],
    ["warning", token("--color-warning"), token("--color-warning-soft"), 4.5],
    ["terminal", "#e5eaed", "#171b1e", 4.5],
    ["focus-indicator", token("--color-focus"), token("--color-surface-raised"), 3],
  ].map(([name, foreground, background, minimum]) => ({ name, foreground, background, minimum, ratio: contrastRatio(foreground, background) }));
  const failures = pairs.filter((pair) => pair.ratio < pair.minimum);
  if (failures.length) throw new Error(`WCAG contrast check failed: ${failures.map((pair) => pair.name).join(", ")}`);
  return { schemaVersion: 1, status: "Passed", standard: "WCAG 2.x AA", semanticTokenCount: 10, pairs };
}

if (path.resolve(process.argv[1] ?? "") === fileURLToPath(import.meta.url)) {
  process.stdout.write(`${JSON.stringify(await checkAccessibilityStyles(), null, 2)}\n`);
}
