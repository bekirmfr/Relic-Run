/*
 * Foe corpus — the bestiary's prose, as the source writes it.
 *
 *   node Tools/capture/foes.mjs
 *
 * The same gap the halls had, found the same way. Each species carries a one-line card the
 * bestiary shows once a delver has met it, and the port carried only the sprite indices. The
 * source is explicit that these are English regardless of locale — its own comment says "EN,
 * like the components" — so there is nothing to translate and everything to transcribe.
 *
 * Which is why they are read. Thirteen lines typed by hand is thirteen chances to drop a word,
 * and a bestiary entry with a word missing reads as a bestiary entry.
 *
 * The species NAMES are not here, and that is not an oversight: those DO go through the
 * translator, under the keys en0..en12, and they live in the locale tables already.
 */

import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "corpus");
const SOURCE = join(ROOT, ".port", "Daily Delve.dc.html");

const html = readFileSync(SOURCE, "utf8");

const block = html.match(/const ENEMY_LORE = \[([\s\S]*?)\n\];/);
if (!block) throw new Error("no ENEMY_LORE array — has the source been restructured?");

/** Every double-quoted string in the array, escapes resolved. */
const lore = [...block[1].matchAll(/"((?:[^"\\]|\\.)*)"/g)].map(m => JSON.parse('"' + m[1] + '"'));

if (!lore.length) throw new Error("ENEMY_LORE is empty");

lore.forEach((line, i) => {
  if (line.length < 20) throw new Error("species " + i + " has suspiciously short lore: " + line);
});

/*
 * How many species the game HAS, read separately so the two can be held against each other. A
 * lore array one short of the roster is a bestiary whose last entry quietly shows the first
 * one's card — which reads perfectly and is wrong.
 */
const roster = html.match(/const ENEMY_G = \[([^\]]*)\]/);
const species = roster ? roster[1].split(",").filter(k => k.trim()).length : lore.length;

if (species !== lore.length) {
  throw new Error("the game has " + species + " species and " + lore.length + " lines of lore");
}

mkdirSync(OUT, { recursive: true });
const json = JSON.stringify({ lore });
writeFileSync(join(OUT, "foes.json"), json, "utf8");

console.log("\nfoe corpus");
console.log("  → foes.json          " + json.length + " bytes");
console.log("  " + lore.length + " species, " +
            lore.reduce((n, l) => n + l.length, 0) + " characters of prose");
