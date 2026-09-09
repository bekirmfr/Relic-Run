/*
 * Hall corpus — the ten dungeons, as the source writes them.
 *
 *   node Tools/capture/halls.mjs
 *
 * The numbers were ported in Phase 5 and are already gated. The PROSE was not: each hall carries
 * a one-line blurb the run screen shows on arrival and a paragraph of lore the dungeon list
 * shows beside its stats, and neither reached the port. Both are English literals in the
 * source's DUNGEONS array — they never pass through its translation function — so there is
 * nothing to translate and everything to transcribe.
 *
 * Transcribing is the problem. Ten paragraphs typed by hand into a C# file is ten chances to
 * drop a word nobody will ever notice is missing, and prose is the one kind of content where a
 * mistake reads as intentional. So they are READ, and DungeonCatalog is asserted against what
 * was read.
 *
 * The numbers are captured alongside them even though they are already gated elsewhere. They
 * cost nothing, and they are what proves the entries lined up: a capture that matched the lore
 * to the wrong hall would still produce ten plausible paragraphs.
 */

import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "corpus");
const SOURCE = join(ROOT, ".port", "Daily Delve.dc.html");

const html = readFileSync(SOURCE, "utf8");

/** The array itself, from its declaration to the line that closes it. */
const block = html.match(/const DUNGEONS = \[([\s\S]*?)\n\];/);
if (!block) throw new Error("no DUNGEONS array — has the source been restructured?");

/*
 * One entry per `{ id: N, ... }`. Split on the id rather than on braces, because the entries
 * contain braces of their own and a brace counter here would be a parser nobody asked for.
 */
const entries = block[1].split(/(?=\{\s*id:\s*\d+,)/).filter(e => /id:\s*\d+/.test(e));

if (entries.length !== 10) throw new Error("expected 10 halls, read " + entries.length);

/** A JS string literal's contents, with its escapes resolved. */
function text(entry, key) {
  const found = entry.match(new RegExp(key + ':\\s*"((?:[^"\\\\]|\\\\.)*)"'));
  if (!found) return null;

  return JSON.parse('"' + found[1] + '"');
}

function number(entry, key) {
  const found = entry.match(new RegExp(key + ":\\s*([\\d.]+)"));
  return found ? Number(found[1]) : null;
}

const halls = entries.map(entry => {
  const relics = entry.match(/bossRelics:\s*\[([^\]]*)\]/);

  const hall = {
    tier: number(entry, "id"),
    level: number(entry, "lvl"),
    name: text(entry, "name"),
    art: text(entry, "art"),
    multiplier: number(entry, "mult"),
    bossRelics: relics && relics[1].trim()
      ? relics[1].split(",").map(r => r.trim().replace(/^"|"$/g, "")).filter(Boolean)
      : [],
    ghoolemBoss: /ghoolemBoss:\s*true/.test(entry),
    blurb: text(entry, "fl"),
    lore: text(entry, "lore"),
  };

  /*
   * Every field is required. A regular expression that quietly returned null would give a hall
   * with no lore, which looks exactly like a hall whose lore is still to be written.
   */
  for (const key of ["tier", "level", "name", "art", "multiplier", "blurb", "lore"]) {
    if (hall[key] === null || hall[key] === undefined) {
      throw new Error("hall " + hall.tier + " has no " + key);
    }
  }

  if (hall.lore.length < 40) throw new Error("hall " + hall.tier + " has suspiciously short lore");

  return hall;
});

/* In order, and each tier once. A split that dropped one would still produce a plausible file. */
halls.forEach((hall, i) => {
  if (hall.tier !== i + 1) throw new Error("halls are out of order at " + hall.tier);
});

mkdirSync(OUT, { recursive: true });
const json = JSON.stringify({ halls });
writeFileSync(join(OUT, "halls.json"), json, "utf8");

console.log("\nhall corpus");
console.log("  → halls.json        " + json.length + " bytes");
console.log("  " + halls.length + " halls, " +
            halls.reduce((n, h) => n + h.lore.length + h.blurb.length, 0) + " characters of prose");
