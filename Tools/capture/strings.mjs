/*
 * String corpus — what a delver actually reads.
 *
 *   node Tools/capture/strings.mjs
 *
 * Resolving a string is more than a table lookup. The source falls back to English for anything
 * a translation is missing, then to the KEY itself so a missing string is visible rather than
 * blank; it substitutes {named} placeholders; and then it strips emoji, collapses runs of
 * whitespace and trims.
 *
 * That last step is the surprising one and the reason this is recorded rather than assumed.
 * "← Back" comes out as "Back", because the arrow is in the stripped range. Half the buttons in
 * the game are written with an icon in front of them and the icon never reaches the screen — so
 * a port that skipped the stripping would show arrows and pictograms nobody has ever seen there,
 * and a port that stripped a slightly different range would drop or keep the wrong ones.
 */

import { readFileSync, readdirSync, writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { buildEngine } from "./engine.mjs";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "corpus");
const LOCALES = join(ROOT, "Tools", "out", "locales");

const api = buildEngine();

const tables = {};
const languages = readdirSync(LOCALES).filter((f) => f.endsWith(".json"))
  .map((f) => f.replace(/\.json$/, ""));

for (const lang of languages) {
  tables[lang] = JSON.parse(readFileSync(join(LOCALES, lang + ".json"), "utf8"));
}

api.setStrings(tables);
const engine = new api.Engine();

const say = (lang, key, vars) => {
  engine.state = { lang };
  return engine.translate(key, vars);
};

/* Every string in every language. Twelve hundred of them, which is the whole point: the
   stripping has to be right for Japanese and Arabic as well as for English. */
const resolved = [];
for (const lang of languages) {
  for (const key of Object.keys(tables.en)) {
    resolved.push({ lang, key, out: say(lang, key) });
  }
}

/* The placeholders, filled. A value is substituted everywhere it appears, and a placeholder
   nobody supplied is left standing — which is how a half-filled line shows up in testing
   rather than silently reading as though it were finished. */
const VALUES = { n: 7, m: 13, h: 42, a: 5, g: 250, i: 3, x: 0, s: "the Hoard" };
const filled = [];

for (const lang of languages) {
  for (const key of Object.keys(tables.en)) {
    if (!/\{\w+\}/.test(tables.en[key])) continue;

    filled.push({ lang, key, vars: VALUES, out: say(lang, key, VALUES) });
    filled.push({ lang, key, vars: { n: 1 }, out: say(lang, key, { n: 1 }) });
  }
}

/* The awkward cases, stated rather than stumbled upon: a key nobody has, a language nobody
   speaks, a value that is not a word, and a string that is nothing but decoration. */
const edges = [
  { lang: "en", key: "thisKeyDoesNotExist", vars: null },
  { lang: "xx", key: "close", vars: null },
  { lang: "fr", key: "thisKeyDoesNotExist", vars: null },
  { lang: "en", key: "floorN", vars: { n: 0 } },
  { lang: "en", key: "floorN", vars: { n: -1 } },
  { lang: "en", key: "floorN", vars: { n: "" } },
  { lang: "en", key: "floorN", vars: { nope: 5 } },
  { lang: "ja", key: "floorOf", vars: { n: 2, m: 13 } },
  { lang: "ar", key: "purseLine", vars: { g: 0 } },
].map((c) => Object.assign({}, c, { out: say(c.lang, c.key, c.vars) }));

/* And the stripping on its own, over strings built to sit either side of every boundary in the
   range. These never appear in the game; they are here so the range is pinned rather than
   inferred from whichever emoji the translators happened to use. */
const CODEPOINTS = [
  0x1efff, 0x1f000, 0x1f600, 0x1faff, 0x1fb00,
  0x218f, 0x2190, 0x21ff, 0x2200,
  0x22ff, 0x2300, 0x23ff, 0x2400,
  0x24ff, 0x2500, 0x27bf, 0x27c0,
  0x2aff, 0x2b00, 0x2bff, 0x2c00,
  0xfe0e, 0xfe0f, 0x200d,
  0x41, 0x2e, 0x3042, 0x4e2d, 0x627,
];

const stripped = [];
api.setStrings({ en: {} });

for (const cp of CODEPOINTS) {
  const ch = String.fromCodePoint(cp);
  for (const shape of ["A" + ch + "B", ch + " x", "x " + ch, ch + ch + "  y", "  " + ch + "  "]) {
    api.setStrings({ en: { probe: shape } });
    stripped.push({ codepoint: cp, shape, out: say("en", "probe") });
  }
}

api.setStrings(tables);

mkdirSync(OUT, { recursive: true });
const json = JSON.stringify({ languages, keys: Object.keys(tables.en), resolved, filled, edges, stripped });
writeFileSync(join(OUT, "strings.json"), json, "utf8");

const changed = resolved.filter((r) => r.out !== tables[r.lang][r.key]).length;

console.log("\nstring corpus");
console.log("  → strings.json       " + String(resolved.length).padStart(4) + " strings  " +
            (Buffer.byteLength(json) / 1024).toFixed(0) + " KB");
console.log("  " + languages.length + " languages x " + Object.keys(tables.en).length + " keys · " +
            changed + " changed by stripping");
console.log("  " + filled.length + " filled · " + edges.length + " edges · " +
            stripped.length + " stripping probes");
