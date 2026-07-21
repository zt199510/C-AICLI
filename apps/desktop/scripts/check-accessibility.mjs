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
  for (const required of ["@media (prefers-reduced-motion:reduce)", "@media (forced-colors:active)", "button:focus-visible", "animation:none", "color:#626a70", "background:#f0f1f2"]) {
    if (!styles.includes(required)) throw new Error(`Accessibility stylesheet is missing: ${required}`);
  }
  const pairs = [
    ["body", "#202428", "#f4f5f6", 4.5],
    ["secondary", "#686e73", "#ffffff", 4.5],
    ["muted-small-text", "#626a70", "#ffffff", 4.5],
    ["disabled-control", "#626a70", "#f0f1f2", 4.5],
    ["success-chip", "#17613a", "#dbf1e4", 4.5],
    ["failure-chip", "#8d2929", "#f8dddd", 4.5],
    ["active-chip", "#245b8c", "#deebf8", 4.5],
    ["warning", "#715c13", "#fff3cb", 4.5],
    ["terminal", "#e5eaed", "#171b1e", 4.5],
    ["focus-indicator", "#2176c7", "#ffffff", 3],
  ].map(([name, foreground, background, minimum]) => ({ name, foreground, background, minimum, ratio: contrastRatio(foreground, background) }));
  const failures = pairs.filter((pair) => pair.ratio < pair.minimum);
  if (failures.length) throw new Error(`WCAG contrast check failed: ${failures.map((pair) => pair.name).join(", ")}`);
  return { schemaVersion: 1, status: "Passed", standard: "WCAG 2.x AA", pairs };
}

if (path.resolve(process.argv[1] ?? "") === fileURLToPath(import.meta.url)) {
  process.stdout.write(`${JSON.stringify(await checkAccessibilityStyles(), null, 2)}\n`);
}
