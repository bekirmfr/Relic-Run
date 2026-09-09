/*
 * Relic prose corpus — what the untranslated relics say about themselves.
 *
 *   node Tools/capture/relics.mjs
 *
 * The game's fifty relics are described in two different places and the split is the source's,
 * not an accident of the port. Nineteen of them are translated: their name and description live
 * in the locale tables under it_<id>_n and it_<id>_d, in all eight languages. The other
 * thirty-one live in one object called NEWR, in English, with four fields each — a name, a
 * description, a line of flavour, and a one-line summary of what the relic does passively.
 *
 * RelicText already carries the names. It does not carry any of the rest, which is why the relic
 * book could not be built: nearly two thirds of its entries had nothing to say.
 *
 * All four fields are read even though only the description is wanted today. The read costs the
 * same and the alternative is coming back to re-derive the same regular expression when the
 * draft screen wants the flavour line.
 */

import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "corpus");
const SOURCE = join(ROOT, ".port", "Daily Delve.dc.html");

const html = readFileSync(SOURCE, "utf8");

const block = html.match(/const NEWR = \{([\s\S]*?)\n\};/);
if (!block) throw new Error("no NEWR object — has the source been restructured?");

/** One entry per `id: { ... }` at the start of a line. */
const entries = block[1].split(/\n(?=\s{2}\w+:\s*\{)/).filter(e => /\w+:\s*\{/.test(e));

if (!entries.length) throw new Error("NEWR parsed as empty");

/** A named string field's contents, escapes resolved. */
function field(entry, key) {
  const found = entry.match(new RegExp('\\b' + key + ':\\s*"((?:[^"\\\\]|\\\\.)*)"'));
  return found ? JSON.parse('"' + found[1] + '"') : null;
}

const relics = entries.map(entry => {
  const id = entry.match(/(\w+):\s*\{/)[1];

  const relic = {
    id,
    name: field(entry, "n"),
    what: field(entry, "d"),
    flavour: field(entry, "fl"),
    passive: field(entry, "pas"),
  };

  /*
   * The name and the description are required. Flavour and the passive line are not — a few
   * relics genuinely have neither, and demanding them would fail on the source being correct.
   */
  for (const key of ["name", "what"]) {
    if (!relic[key]) throw new Error("relic " + id + " has no " + key);
  }

  return relic;
});

/* Ids are the key everything else joins on, so a duplicate would silently overwrite an entry. */
const seen = new Set();
for (const relic of relics) {
  if (seen.has(relic.id)) throw new Error("relic " + relic.id + " appears twice");
  seen.add(relic.id);
}

mkdirSync(OUT, { recursive: true });
const json = JSON.stringify({ relics });
writeFileSync(join(OUT, "relics.json"), json, "utf8");

const flavoured = relics.filter(r => r.flavour).length;

console.log("\nrelic prose corpus");
console.log("  → relics.json        " + json.length + " bytes");
console.log("  " + relics.length + " untranslated relics, " + flavoured + " with flavour, " +
            relics.reduce((n, r) => n + r.what.length, 0) + " characters of description");
