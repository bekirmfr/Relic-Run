/*
 * Set corpus — how many of a kind make a set, and what it then counts to.
 *
 *   node Tools/capture/sets.mjs
 *
 * Four numbers: an Edge set needs five and fires every fourth strike, a Pace set needs seven and
 * fires every fifth. They look arbitrary because they are — they are the source's, and there is
 * no rule that generates them.
 *
 * Which is exactly why they are read rather than typed. Mutation testing swapped the two
 * thresholds over and every test still passed, because every test worked out what to expect from
 * the same constants it was checking. A number that agrees only with itself is not being checked
 * at all, and four of those sitting in a file marked "the source's" is how a port quietly stops
 * being one.
 *
 * Read from the tray, which is where the fallback fuse is decided. The relic card's copy of the
 * same rule is checked against it below: the source states these numbers in two places, and a
 * recorder that took either one on its own could not notice them drifting apart.
 */

import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "corpus");
const SOURCE = join(ROOT, ".port", "Daily Delve.dc.html");

const html = readFileSync(SOURCE, "utf8");

/** The tray's fallback fuse: `kk === "EDGE" && kn >= 5) { fuse = (cnt.f || 0) % 4`. */
function fromTray(kind) {
  const found = html.match(
    new RegExp(`kk === "${kind}" && kn >= (\\d+)\\)\\s*\\{\\s*fuse = \\(cnt\\.f \\|\\| 0\\) % (\\d+)`));

  if (!found) throw new Error(`no ${kind} set fuse in the tray — has the source changed?`);

  return { needs: Number(found[1]), every: Number(found[2]) };
}

/** The relic card's copy: `kk2 === "PACE" && kn2 >= 7) { const f2 = (cnt.f || 0) % 5`. */
function fromCard(kind) {
  const found = html.match(
    new RegExp(`kk2 === "${kind}" && kn2 >= (\\d+)\\)\\s*\\{\\s*const f2 = \\(cnt\\.f \\|\\| 0\\) % (\\d+)`));

  if (!found) throw new Error(`no ${kind} set gauge on the relic card`);

  return { needs: Number(found[1]), every: Number(found[2]) };
}

const sets = {};

for (const kind of ["EDGE", "PACE"]) {
  const tray = fromTray(kind);
  const card = fromCard(kind);

  if (tray.needs !== card.needs || tray.every !== card.every) {
    throw new Error(
      `the ${kind} set disagrees with itself: tray says ${tray.needs}/${tray.every}, ` +
      `card says ${card.needs}/${card.every}`);
  }

  sets[kind.toLowerCase()] = tray;
}

mkdirSync(OUT, { recursive: true });
const json = JSON.stringify({ sets });
writeFileSync(join(OUT, "sets.json"), json, "utf8");

console.log("\nset corpus");
console.log("  → sets.json          " + json.length + " bytes");
for (const [kind, set] of Object.entries(sets)) {
  console.log("  " + kind.padEnd(6) + " needs " + set.needs + ", fires every " + set.every);
}
