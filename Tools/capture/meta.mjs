/*
 * Meta corpus — what the save is asked, once a run has been banked into it.
 *
 *   node Tools/capture/meta.mjs
 *
 * Two things live here that nothing else records.
 *
 * ACHIEVEMENTS are fourteen predicates over the save store, and they are shipped rules rather
 * than presentation: what has been earned is a fact about a delver, and a screen that worked it
 * out would be a second copy of it. Each is asked of a spread of saves — including saves sitting
 * exactly on a threshold, where an off-by-one shows.
 *
 * THE DAILY SEED is the UTC date read as a number, so every delver gets the same run on the same
 * day and the day turns over at one instant worldwide rather than at each player's midnight. It
 * is recorded across month ends, year ends and a leap day, because that is where a date routine
 * written by hand goes wrong.
 */

import { writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { buildEngine } from "./engine.mjs";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "corpus");

const api = buildEngine();

/** The keys an achievement can read. Everything else in the store is settings or art. */
const KEYS = ["dd.runs", "dd.clears", "dd.best", "dd.vsCrowns", "dd.goldLife", "dd.xp",
              "dd.unlocked"];

function ask(id, save) {
  for (const k of KEYS) api.Store.set(k, save[k] || 0);
  api.setUnlocked(save["dd.unlocked"] || 1);

  const earned = [];
  for (const a of api.META.ACHV) {
    if (a.ok()) earned.push(a.id);
  }

  const state = {};
  for (const k of KEYS) state[k] = save[k] || 0;
  state["dd.unlocked"] = api.META.unlocked();

  return { id, save: state, level: api.META.level(), earned };
}

const achievements = [];

// An empty save, and one that has done everything.
achievements.push(ask("meta/none", {}));
achievements.push(ask("meta/all", {
  "dd.runs": 999, "dd.clears": 99, "dd.best": 9999, "dd.vsCrowns": 99,
  "dd.goldLife": 99999, "dd.xp": 999999, "dd.unlocked": 10,
}));

/* Each threshold, and the value either side of it. An achievement that reads ">= 5" and a port
   that reads "> 5" agree on everything except the five itself. */
const EDGES = {
  "dd.runs": [0, 1, 2],
  "dd.clears": [0, 1, 2, 4, 5, 6],
  "dd.best": [0, 999, 1000, 1001, 1999, 2000, 2001],
  "dd.vsCrowns": [0, 1, 2, 4, 5, 6],
  "dd.goldLife": [0, 999, 1000, 1001],
  "dd.unlocked": [1, 2, 3, 4],
};

for (const key of Object.keys(EDGES)) {
  for (const value of EDGES[key]) {
    achievements.push(ask(`meta/${key}/${value}`, { [key]: value }));
  }
}

/* Levels either side of every gate the achievements read: 3 unlocks the Daily, 5 versus, and 5
   and 10 are earned outright. The XP for a level comes off the same table the game levels on. */
function xpForLevel(level) {
  let acc = 0;
  for (let l = 2; l <= level; l++) acc += Math.round(1000 * Math.pow(1.2, l - 2));
  return acc;
}

for (let level = 1; level <= 12; level++) {
  achievements.push(ask(`meta/level/${level}`, { "dd.xp": xpForLevel(level) }));
  achievements.push(ask(`meta/level/${level}/short`, { "dd.xp": Math.max(0, xpForLevel(level) - 1) }));
}

/* The daily seed, across the places a hand-written date routine goes wrong. */
const DAYS = [
  [2024, 1, 1], [2024, 2, 28], [2024, 2, 29], [2024, 3, 1], [2024, 12, 31],
  [2025, 1, 1], [2025, 2, 28], [2025, 3, 1], [2025, 6, 15], [2025, 10, 9],
  [2026, 9, 7], [2026, 11, 30], [2030, 12, 31], [1999, 12, 31], [2000, 1, 1],
];

const seeds = DAYS.map(([y, m, d]) => ({
  y, m, d, seed: api.dailySeed(new Date(Date.UTC(y, m - 1, d, 12, 0, 0))) >>> 0,
}));

mkdirSync(OUT, { recursive: true });
const json = JSON.stringify({ achievements, seeds });
writeFileSync(join(OUT, "meta.json"), json, "utf8");

const spread = {};
for (const c of achievements) for (const id of c.earned) spread[id] = (spread[id] || 0) + 1;

console.log("\nmeta corpus");
console.log("  → meta.json          " + String(achievements.length).padStart(4) + " saves  " +
            (Buffer.byteLength(json) / 1024).toFixed(0) + " KB");
console.log("  " + Object.keys(spread).length + " of " + api.META.ACHV.length +
            " achievements earned by at least one save · " + seeds.length + " days");
