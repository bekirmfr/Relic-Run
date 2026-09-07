/*
 * Run-outcome corpus — what a finished delve is worth.
 *
 *   node Tools/capture/outcomes.mjs
 *
 * finishRun is the game's scoring, wrapped in presentation: sound, telemetry, a screen
 * transition and a gold counter that animates. The arithmetic inside it — what the purse banks,
 * what the run scores, how many stars it earns and how much XP it pays — is Core's, and it is
 * lifted rather than transcribed, with the presentation stubbed out.
 *
 * Awakenings are recorded as the inventory SLOTS that are awake, not as relic ids. That is how
 * the game stores them — a copy is awakened, not a relic — and finishRun reads them through
 * awakeById, which counts slots into per-id totals before anything else sees them. The Balance
 * Lab's own loop keys them by id instead, which is equivalent only because it never awakens the
 * same relic twice; scoring is not so forgiving, and passing ids here silently reads as nothing
 * awake at all.
 *
 * Everything recorded here is an integer. `relevance` is the one fraction in the calculation
 * and it is deliberately NOT recorded: it exists only to scale XP, so recording the resulting
 * xpGain pins it exactly while keeping the corpus free of float literals.
 */

import { writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { buildEngine } from "./engine.mjs";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "corpus");

const api = buildEngine();
const engine = new api.Engine();
engine.setState = () => {};

/** finishRun banks into the save store, so each case starts from a clean one. */
function reset(unlocked, xp) {
  api.setUnlocked(unlocked);
  api.Store.set("dd.xp", xp);
  for (const k of ["dd.runs", "dd.clears", "dd.goldLife", "dd.gold", "dd.best"]) {
    api.Store.set(k, 0);
  }

  api.Store.set("dd.scores", []);
  api.Store.set("dd.arts", []);
  engine.state = { sparks: 0, best: 0, name: "you" };
}

function outcome(id, { how, mode, gold, floor, kills, items, awake, tier, unlocked, xp }) {
  reset(unlocked, xp);

  const slots = {};
  for (const slot of awake) slots[slot] = true;

  const G = {
    mode, gold, floor, kills, tier, startMs: 0, rerolls: 0,
    items: items.slice(), awake: slots, sandbox: false,
  };

  engine.G = G;
  engine.finishRun(how);

  return {
    id,
    input: { how, mode, gold, floor, kills, items: items.slice(), awake: awake.slice(), tier, unlocked, xp },
    banked: G.banked | 0,
    score: G.score | 0,
    stars: G.stars | 0,
    xpGain: G.xpGain | 0,
    fullGold: G.fullGold | 0,
  };
}

const cases = [];
const HOWS = ["died", "cashout", "cleared"];
const PIGGY = [
  { name: "none", items: [], awake: [] },
  { name: "piggy", items: ["piggy"], awake: [] },
  { name: "piggyAwake", items: ["piggy"], awake: [0] },
];

/* Every floor against every ending: this is what pins the star thresholds, which sit at
   fractions of the way down (0.4, 0.6 for a cash-out, 0.72) and can only be read off a sweep. */
for (const how of HOWS) {
  for (let floor = 1; floor <= 13; floor++) {
    cases.push(outcome(`stars/${how}/f${floor}`, {
      how, mode: "delve", gold: 120, floor, kills: floor * 3,
      items: [], awake: [], tier: 1, unlocked: 1, xp: 0,
    }));
  }
}

/* The Piggy Bank takes its cut of a cash-out and nothing else. */
for (const how of HOWS) {
  for (const p of PIGGY) {
    for (const gold of [0, 1, 37, 250, 999, 4321]) {
      cases.push(outcome(`piggy/${how}/${p.name}/g${gold}`, {
        how, mode: "delve", gold, floor: 9, kills: 20,
        items: p.items, awake: p.awake, tier: 1, unlocked: 1, xp: 0,
      }));
    }
  }
}

/* Score scales by the dungeon, and XP by how far past the frontier the dungeon sits. */
for (let tier = 1; tier <= 10; tier++) {
  for (const unlocked of [1, 3, 6, 10]) {
    for (const how of HOWS) {
      cases.push(outcome(`tier/t${tier}/u${unlocked}/${how}`, {
        how, mode: "delve", gold: 300, floor: 11, kills: 30,
        items: [], awake: [], tier, unlocked, xp: 0,
      }));
    }
  }
}

/* The dungeon's multiplier is a TABLE, not a formula. Past the fifth hall its entries are
   rounded to four places — 1.6105 where 1.1^5 is 1.61051 — so a port that computes the power
   scores a few of these differently. One case per tier could not tell: the difference only
   shows when the product lands within a twentieth of a rounding boundary. This sweeps the
   purse so that it does. */
for (let tier = 1; tier <= 10; tier++) {
  for (let gold = 0; gold < 1200; gold += 37) {
    cases.push(outcome(`mult/t${tier}/g${gold}`, {
      how: "cleared", mode: "delve", gold, floor: 13, kills: 41,
      items: [], awake: [], tier, unlocked: 10, xp: 0,
    }));
  }
}

/* A duel scores on depth and the crown alone — kills do not count, and neither does a
   Piggy Bank, because there is no cash-out to take a cut of. */
for (const how of HOWS) {
  for (let floor = 1; floor <= 9; floor += 2) {
    for (const p of PIGGY) {
      cases.push(outcome(`versus/${how}/f${floor}/${p.name}`, {
        how, mode: "versus", gold: 400, floor, kills: 25,
        items: p.items, awake: p.awake, tier: 4, unlocked: 2, xp: 0,
      }));
    }
  }
}

/* XP is paid on top of whatever is already banked, and a level can tick over mid-award. */
for (const xp of [0, 5, 340, 2500, 18000]) {
  for (const how of HOWS) {
    cases.push(outcome(`xp/${xp}/${how}`, {
      how, mode: "delve", gold: 500, floor: 13, kills: 45,
      items: [], awake: [], tier: 3, unlocked: 3, xp,
    }));
  }
}

/* A duel pays XP at full rate whatever dungeon it sits in, which only shows when the dungeon
   is one the player left behind — the delve cases above all sit at or ahead of the frontier,
   where the scaling is 1 either way. */
for (let tier = 1; tier <= 3; tier++) {
  for (const how of HOWS) {
    for (const mode of ["delve", "versus"]) {
      cases.push(outcome(`behind/${mode}/t${tier}/${how}`, {
        how, mode, gold: 260, floor: 12, kills: 28,
        items: [], awake: [], tier, unlocked: 10, xp: 0,
      }));
    }
  }
}

/* A run that scored at all pays at least one XP, however far behind the dungeon is. Reaching
   that floor needs a score small enough that scaling it rounds to nothing: a handful of gold
   walked out of the first floor, in a dungeon long since left behind. */
for (const gold of [1, 2, 3, 4, 6, 9, 14, 21]) {
  for (const floor of [1, 2]) {
    cases.push(outcome(`crumbs/g${gold}/f${floor}`, {
      how: "cashout", mode: "delve", gold, floor, kills: 0,
      items: [], awake: [], tier: 1, unlocked: 10, xp: 0,
    }));
  }
}

mkdirSync(OUT, { recursive: true });
const json = JSON.stringify(cases);
writeFileSync(join(OUT, "outcomes.json"), json, "utf8");

console.log("\noutcome corpus");
console.log(`  → outcomes.json      ${String(cases.length).padStart(4)} cases  ` +
            `${(Buffer.byteLength(json) / 1024).toFixed(0)} KB`);
const stars = {};
for (const c of cases) stars[c.stars] = (stars[c.stars] || 0) + 1;
console.log("  stars: " + Object.entries(stars).map(([k, n]) => k + "★ ×" + n).join(" · "));
