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
 *
 * WHAT THE RUN LEAVES BEHIND
 *
 * finishRun also BANKS: it counts the run, counts a clear, piles the purse into a lifetime
 * total and into the wallet, pays the XP, keeps a personal best, files a leaderboard row,
 * unlocks the next hall and remembers the backdrop. All of that is a rule and all of it is
 * recorded here, as the whole save store either side of the call.
 *
 * A leaderboard row carries a timestamp. It is not recorded and not compared: it orders
 * nothing — the board sorts on score alone — and a clock is not a rule.
 */

import { writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { buildEngine } from "./engine.mjs";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "corpus");

const api = buildEngine();
const engine = new api.Engine();

/** The keys a finished run writes, which is exactly what the port has to reproduce. */
const BANKED_KEYS = ["dd.runs", "dd.clears", "dd.goldLife", "dd.gold", "dd.best", "dd.xp",
                     "dd.unlocked", "dd.vsCrowns"];

/** finishRun banks into the save store, so each case states the store it starts from. */
function reset(unlocked, xp, store) {
  store = store || {};
  api.setUnlocked(unlocked);
  api.Store.set("dd.xp", xp);
  for (const k of BANKED_KEYS) {
    if (k !== "dd.xp" && k !== "dd.unlocked") api.Store.set(k, store[k] || 0);
  }

  if (store["dd.unlocked"]) api.setUnlocked(store["dd.unlocked"]);
  api.Store.set("dd.daily." + DAILY_SEED, store["dd.daily"] || 0);
  api.Store.set("dd.scores", (store["dd.scores"] || []).map((e) => Object.assign({}, e)));
  api.Store.set("dd.arts", (store["dd.arts"] || []).slice());

  // The game boots its UI state from the store, and the personal best is compared against the
  // STATE rather than the store — so seeding it is what lets the corpus tell the two apart.
  engine.state = { sparks: 0, best: api.Store.get("dd.best", 0) | 0, name: "you" };
}

/** The day a recorded daily is filed under. Fixed, so the corpus does not depend on the clock. */
const DAILY_SEED = 20260907;

/** The save, as the port has to leave it. A leaderboard row's clock is not part of the rule. */
function saved() {
  const out = {};
  for (const k of BANKED_KEYS) out[k] = api.Store.get(k, 0) | 0;
  out["dd.unlocked"] = api.META.unlocked();
  out["dd.arts"] = (api.Store.get("dd.arts", []) || []).slice();
  out["dd.scores"] = (api.Store.get("dd.scores", []) || []).map((e) => ({
    score: e.score | 0, floor: e.floor | 0, kills: e.kills | 0, relics: e.relics | 0, name: e.name,
  }));

  // A Daily Delve keeps its own best, filed under the day rather than against the career.
  out["dd.daily"] = api.Store.get("dd.daily." + DAILY_SEED, 0) | 0;
  return out;
}

function outcome(id, { how, mode, gold, floor, kills, items, awake, tier, unlocked, xp,
                      daily = false, store = null }) {
  reset(unlocked, xp, store);

  const slots = {};
  for (const slot of awake) slots[slot] = true;

  const G = {
    mode, gold, floor, kills, tier, startMs: 0, rerolls: 0, isDaily: daily,
    seed: daily ? DAILY_SEED : 0,
    items: items.slice(), awake: slots, sandbox: false,
  };

  const before = saved();
  engine.G = G;
  engine.state.isBest = false;
  engine.finishRun(how);

  return {
    id,
    input: {
      how, mode, gold, floor, kills, items: items.slice(), awake: awake.slice(), tier,
      unlocked, xp, daily, store: before,
    },
    banked: G.banked | 0,
    score: G.score | 0,
    stars: G.stars | 0,
    xpGain: G.xpGain | 0,
    fullGold: G.fullGold | 0,

    // Whether the run beat the standing best. Without it, beating and MATCHING a best are the
    // same recording — the number lands on the same value either way — and the rule that says
    // a tie is not a record has nothing holding it.
    isBest: !!engine.state.isBest,
    store: saved(),
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

/* What a run leaves behind. A save that is already deep in a career banks differently from an
   empty one: the wallet and the lifetime purse accumulate, the personal best only moves when it
   is beaten, the board holds ten and drops the eleventh, a hall unlocks once and a backdrop is
   remembered once. Each of those needs a store that already has something in it. */
const BOARD = [];
for (let i = 0; i < 14; i++) {
  BOARD.push({ score: 100 * (14 - i), floor: 13 - i, kills: 40 - i, relics: 9, name: "you" });
}

for (let i = 0; i < 12; i++) {
  const deep = {
    "dd.runs": 40 + i, "dd.clears": 3 + i, "dd.goldLife": 900 + 137 * i,
    "dd.gold": 500 + 91 * i, "dd.best": 300 * i, "dd.vsCrowns": i % 4,
    "dd.unlocked": 1 + (i % 5),
    // Boards of every size up to full and past it: a board that is already ten deep is the
    // only one that can say which end the eleventh row falls off.
    "dd.scores": BOARD.slice(0, i),
    "dd.arts": i % 2 === 0 ? [] : ["assets/hall-hoard.png"],
  };

  for (const how of HOWS) {
    for (const mode of ["delve", "versus"]) {
      cases.push(outcome(`bank/${i}/${mode}/${how}`, {
        how, mode, gold: 60 * i + 13, floor: 4 + i, kills: 3 * i,
        items: i % 3 === 0 ? ["piggy"] : [], awake: i % 6 === 0 ? [0] : [],
        tier: 1 + (i % 5), unlocked: 1 + (i % 5), xp: 900 * i, store: deep,
      }));
    }
  }
}

/* A best is beaten, not matched — which only a run scoring EXACTLY the standing best can say.
   The score is not known in advance, so each of these is scored once against an empty save and
   then run again against a save holding that very number, and a board already carrying it. */
for (let i = 0; i < 8; i++) {
  const shape = {
    how: "cashout", mode: "delve", gold: 90 * i + 31, floor: 3 + i, kills: 4 * i,
    items: [], awake: [], tier: 1 + (i % 4), unlocked: 4, xp: 700 * i,
  };

  const scored = outcome(`tie/probe/${i}`, shape).score;
  cases.push(outcome(`tie/${i}`, Object.assign({}, shape, {
    store: {
      "dd.best": scored,
      "dd.scores": [{ score: scored, floor: 9, kills: 9, relics: 9, name: "someone else" }],
    },
  })));
}

/* A Daily Delve keeps its own best, filed under the day's seed rather than against the run —
   including the day that has already been played better, where it must not move. */
for (const gold of [0, 120, 640]) {
  for (const how of HOWS) {
    for (const already of [0, 500, 5000]) {
      cases.push(outcome(`daily/${how}/g${gold}/had${already}`, {
        how, mode: "delve", gold, floor: 10, kills: 22, items: [], awake: [],
        tier: 1, unlocked: 3, xp: 4200, daily: true, store: { "dd.daily": already },
      }));
    }
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
