/*
 * Palette corpus — the colour maths behind a delver's appearance.
 *
 *   node Tools/capture/palette.mjs
 *
 * A hero is drawn as a stack of images whose pixels are not colours but ROLE KEYS: one letter
 * saying "this pixel is hair", "this is the dark side of the outfit". A palette turns those keys
 * into colours, and a shader looks them up. That is what lets the Changing Room recolour a
 * delver live without touching a single sprite.
 *
 * A family gives its base colour and derives the other two: the dark side is the base with its
 * lightness multiplied down, its hue rotated a little and its saturation pushed up, and the
 * light side is the same in reverse. Twenty-six families make seventy-eight keys, and seven
 * fixed colours — white, black and five greys — are never recoloured at all.
 *
 * WHY IT IS RECORDED AT ALL
 *
 * The derivation runs through HSL and back, which is float arithmetic that lands on a byte. Most
 * of the time a difference of one part in a billion is absorbed by the rounding; occasionally it
 * is not, and the two ports would disagree on one channel of one colour. Recording the answers
 * is cheaper than arguing about it.
 *
 * The multipliers are recorded as THOUSANDTHS rather than decimals, so this file holds no float
 * literals — the rule everywhere else in the corpus, and the reason none of it has ever diverged
 * on a parsed decimal. Both sides divide by a thousand, which is exact.
 */

import { writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { buildEngine } from "./engine.mjs";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "corpus");

const api = buildEngine();

/* A spread of hexes: the family bases, the fixed colours, the channel extremes, and the greys
   that have no hue at all — which is the branch a saturation of zero takes. */
const HEXES = [];
for (const f of api.FAMILIES) HEXES.push(f.hex);
for (const key of Object.keys(api.SOLOS)) HEXES.push(api.SOLOS[key]);
HEXES.push("#000000", "#FFFFFF", "#010101", "#FEFEFE", "#FF0000", "#00FF00", "#0000FF",
           "#7F7F7F", "#808080", "#010203", "#FDFEFF", "#123456", "#ABCDEF");

/* Multiplying every channel, which is the simpler of the two and is used for the flat shades. */
const shades = [];
for (const hex of HEXES) {
  for (const thousandths of [0, 250, 500, 700, 900, 1000, 1100, 1320, 2000, 4000]) {
    shades.push({ hex, mul: thousandths, out: api.shade(hex, thousandths / 1000) });
  }
}

/* And the real one: lightness scaled, hue rotated, saturation scaled. The ranges bracket what
   the shade defaults use, and go past them on both sides so a clamp has somewhere to bite. */
const shifts = [];
for (const hex of HEXES) {
  for (const mult of [0, 300, 700, 1000, 1320, 2500]) {
    for (const hue of [-180, -12, 0, 10, 90, 359, 360, 720]) {
      for (const sat of [0, 900, 1000, 1120, 3000]) {
        shifts.push({
          hex, mult, hue, sat,
          out: api.applyShade(hex, mult / 1000, hue, sat / 1000),
        });
      }
    }
  }
}

/* The palette a delver gets when they have chosen nothing: every family's three keys derived
   from its own base, plus the fixed colours. This pins the family table and the shade defaults
   together with the maths. */
const roles = [];
for (const f of api.FAMILIES) {
  const p = api.famParams(f.base);
  roles.push({ key: f.base, family: f.name, kind: "base", hex: f.hex });
  roles.push({
    key: f.dark, family: f.name, kind: "dark",
    hex: api.applyShade(f.hex, p.darkMult, p.darkHue, p.darkSat),
  });
  roles.push({
    key: f.light, family: f.name, kind: "light",
    hex: api.applyShade(f.hex, p.lightMult, p.lightHue, p.lightSat),
  });
}

for (const key of Object.keys(api.SOLOS)) {
  roles.push({ key, family: "fixed", kind: "solo", hex: api.SOLOS[key] });
}

mkdirSync(OUT, { recursive: true });
const json = JSON.stringify({ shades, shifts, roles });
writeFileSync(join(OUT, "palette.json"), json, "utf8");

const distinct = new Set(roles.map((r) => r.hex));
console.log("\npalette corpus");
console.log("  → palette.json       " + String(roles.length).padStart(4) + " roles  " +
            (Buffer.byteLength(json) / 1024).toFixed(0) + " KB");
console.log("  " + shades.length + " flat shades · " + shifts.length + " hue-and-lightness shifts");
console.log("  " + api.FAMILIES.length + " families, " + Object.keys(api.SOLOS).length +
            " fixed, " + distinct.size + " distinct colours");
