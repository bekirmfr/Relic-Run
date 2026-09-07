/*
 * Hero corpus — composing a delver, pixel by pixel.
 *
 *   node Tools/capture/hero.mjs
 *
 * A hero is not one sprite. It is a stack of twelve layers — a backdrop, a body, and ten
 * wardrobe slots — each drawn as a grid whose characters are ROLE KEYS rather than colours.
 * Composing one means stamping the stack in paint order, resolving the shadow and highlight
 * modifiers against whatever is already underneath them, outlining the silhouette, and handing
 * the result to a palette.
 *
 * WHY THE MODIFIERS MAKE THIS MORE THAN A STACK OF SPRITES
 *
 * Two of the role keys are not colours at all. A shadow pixel means "whatever is beneath this,
 * one tone darker", and a highlight means the reverse — so what a pixel finally is depends on
 * what was stamped before it. The outline is the same shape of problem: it is drawn around the
 * finished silhouette, which nothing knows until the stack is complete. Neither can be baked
 * into a sprite ahead of time, and neither can be done by drawing twelve sprites on top of one
 * another. The composition has to happen, and it has to happen here.
 *
 * HOW A TONED PIXEL IS RECORDED
 *
 * The source names each toned pixel with a synthetic character, handed out in the order the
 * tones are first encountered. Those characters are an implementation detail — a port that
 * composed in a different order would pick different ones and still be right — so they are NOT
 * recorded as themselves. Each frame carries a `tones` table saying what its synthetic
 * characters mean, and the port is compared on the MEANING: this pixel is the outfit's dark
 * side, one tone darker again.
 */

import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { buildEngine } from "./engine.mjs";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "corpus");

const api = buildEngine();

const pack = JSON.parse(readFileSync(join(ROOT, ".port", "hero-pack.json"), "utf8"));
const valid = api.validatePack(pack);
if (!valid.ok) throw new Error("the shipped hero pack does not validate: " + valid.errors.join(", "));

const imported = api.importPack(pack);
if (!imported.ok) throw new Error("the shipped hero pack would not import");

const SIZE = api.PACK.SIZE;
const STACK = api.stackOrder();
const SLOTS = STACK.filter((s) => s !== "base");

/** What each slot can wear, from the pack itself rather than from a list written here. */
const wardrobe = {};
for (const slot of SLOTS) wardrobe[slot] = ["none"];
for (const part of pack.parts) {
  if (part.id !== "none" && wardrobe[part.slot] && wardrobe[part.slot].indexOf(part.id) < 0) {
    wardrobe[part.slot].push(part.id);
  }
}

const STATES = Object.keys(pack.states).concat(["static"]);

/**
 * One composed frame, with its synthetic tone characters explained.
 *
 * A frame's rows are recorded verbatim. Anything in them that is not a role key is a tone, and
 * `tones` says which role it darkens or lightens and by how much.
 */
function frameOf(rows) {
  const tones = {};

  for (const row of rows) {
    for (const ch of row) {
      if (api.SYNTH.byTok && api.SYNTH.byTok[ch] && !tones[ch]) {
        const t = api.SYNTH.byTok[ch];
        tones[ch] = { src: t.src, mode: t.mode };
      }
    }
  }

  return { rows, tones };
}

const composed = [];

function compose(id, combo, state) {
  const out = api.composeHeroState(combo, state);
  composed.push({
    id, combo, state,
    ms: out.ms | 0,
    mode: out.mode,
    frames: out.frames.map(frameOf),
  });
}

/** Nothing worn: the base body alone, in every state it has. */
const bare = {};
for (const slot of SLOTS) bare[slot] = "none";
for (const state of STATES) compose("bare/" + state, bare, state);

/* Each garment on its own, over the bare body. One slot at a time is what separates a part that
   is drawn wrong from a stack that is ordered wrong. */
for (const slot of SLOTS) {
  for (const id of wardrobe[slot]) {
    if (id === "none") continue;
    const combo = Object.assign({}, bare, { [slot]: id });
    compose("solo/" + slot + "/" + id, combo, "idle");
  }
}

/* Fully dressed, which is where the modifiers and the outline actually bite: a shadow pixel on
   a cape lands on whatever the torso put there. */
function dressed(pick) {
  const combo = {};
  for (const slot of SLOTS) {
    const options = wardrobe[slot];
    combo[slot] = options[pick(slot, options)];
  }

  return combo;
}

let n = 0;
const spin = () => (n = (n * 1664525 + 1013904223) >>> 0) / 4294967296;

for (let i = 0; i < 12; i++) {
  n = 0x5EED + i * 7919;
  const combo = dressed((slot, options) => Math.floor(spin() * options.length));
  for (const state of ["idle", "walk", "attack", "die"]) {
    compose("dressed/" + i + "/" + state, combo, state);
  }
}

/* And everything a slot has, worn at once wherever possible — the deepest stack the pack can
   make, so nothing later in the paint order goes unexercised. */
for (let i = 0; i < 4; i++) {
  const combo = dressed((slot, options) => Math.min(options.length - 1, i + 1));
  for (const state of ["idle", "hurt"]) compose("full/" + i + "/" + state, combo, state);
}

mkdirSync(OUT, { recursive: true });
const json = JSON.stringify({
  size: SIZE,
  stack: STACK,
  // The REGISTRY's states, not the pack's. The pack declares its own so it can be validated
  // against them; the registry is what decides how long a state runs and how many frames it
  // starts from, and a pack drawn with more simply animates fully.
  states: api.PACK.STATES,
  famHex: pack.famHex,
  shadeParams: pack.shadeParams,
  composed,
});
writeFileSync(join(OUT, "hero.json"), json, "utf8");

const frames = composed.reduce((a, c) => a + c.frames.length, 0);
const toned = composed.reduce(
  (a, c) => a + c.frames.reduce((b, f) => b + Object.keys(f.tones).length, 0), 0);

console.log("\nhero corpus");
console.log("  → hero.json          " + String(composed.length).padStart(4) + " compositions  " +
            (Buffer.byteLength(json) / 1024).toFixed(0) + " KB");
console.log("  " + frames + " frames at " + SIZE + "x" + SIZE + " · " + toned + " toned pixels named");
console.log("  " + SLOTS.length + " slots · " + STATES.length + " states · " +
            pack.parts.length + " parts in the pack");
