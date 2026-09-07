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

/* Compositing one colour over another, which is how a shadow or a highlight tints whatever it
   is laid on. Recorded because the alpha maths rounds, and rounding is where ports disagree. */
const overs = [];
for (const below of HEXES) {
  for (const top of ["#000000", "#FFFFFF", "#3A3328", "#F2ECDD", "#C4593C"]) {
    for (const alpha of [0, 150, 300, 450, 1000]) {
      overs.push({ below, top, alpha, out: api.hexOver(below, top, alpha / 1000) });
    }
  }
}

/* THE PALETTE ITSELF, recorded from the source's own paletteFor rather than re-derived here.
   That distinction cost something to learn: the first version of this recorder walked the
   family table and applied the shades itself, which produced a corpus the port agreed with by
   construction and said nothing about the nine keys paletteFor adds on top — a couple of fixed
   extras, and seven legacy aliases mapping tokens in old art onto the roles that replaced them.
   A recorder that re-implements what it is recording is not a gate. */
const palettes = [];

function palette(id, famHex) {
  const built = api.paletteFor(famHex);
  const roles = Object.keys(built).map((key) => ({ key, hex: built[key] }));
  palettes.push({ id, famHex, roles });
}

palette("default", {});

/* A delver who has chosen colours, which is the path every rival in a lobby takes. The overrides
   are keyed by a family's BASE key, and everything unstated keeps its default. */
palette("chosen", {
  H: "#5B4A7A", T: "#191510", C: "#9B8BD0", A: "#4A6A8A", E: "#191510",
  Z: "#2C1710", F: "#191510", O: "#E3B341", K: "#E3B08A", R: "#8B8172",
});

palette("one", { H: "#2E7ED9" });
palette("greys", { H: "#000000", K: "#FFFFFF", T: "#7F7F7F" });

mkdirSync(OUT, { recursive: true });
const json = JSON.stringify({ shades, shifts, overs, palettes });
writeFileSync(join(OUT, "palette.json"), json, "utf8");

console.log("\npalette corpus");
console.log("  → palette.json       " + String(palettes.length).padStart(4) + " palettes  " +
            (Buffer.byteLength(json) / 1024).toFixed(0) + " KB");
console.log("  " + shades.length + " flat shades · " + shifts.length + " shifts · " +
            overs.length + " composites");
console.log("  " + palettes[0].roles.length + " role keys · " + api.FAMILIES.length +
            " families · " + Object.keys(api.SOLOS).length + " fixed");
