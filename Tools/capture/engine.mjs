/*
 * Headless engine host.
 *
 * Assembles the lifted combat engine into a sandbox and exposes it to Node. The
 * Component methods reference `this.itemName` and `this.t` for log text; those are
 * stubbed to return stable ids, since the corpus compares event structure, not prose.
 */

import vm from "node:vm";
import { liftConst, liftFunction, liftMethod, liftMethodTo } from "./lift.mjs";
import { instrumentBalanceRuns } from "./instrument.mjs";

/* Module-scope declarations the engine reaches for. Order matters only for consts
   that reference each other at definition time (POOL reads ITEMS). */
const CONSTS = [
  "ITEMS", "NEWR", "POOL", "KIND_META", "TRIGGERS", "EMITTERS", "COMPONENTS",
  "MAX_FLOOR", "SHOP_FLOOR", "ENEMY_G", "ENEMY_ROW", "DUNGEONS", "META",
  // The run loop's own content: the event table, the enemy curve formulas, and the
  // bazaar's price floor. SHOP_FLOOR's statement carries SHOP_BUY and SHOP_UP with it,
  // and EV_MIN_SPD's carries EV_MIN_DEF.
  "EVENTS", "EV_MIN_SPD", "ENEMY_STAT_FORMULAS", "ENEMY_ROLE_FORMULAS",
  "FORMULA_BASE_STATS",
];

const FUNCTIONS = [
  "mulberry32", "count", "applyDef", "heroStatRows", "heroStatOf", "heroCtxOf",
  "currentAtk", "packFor", "packGold", "setOf", "prodSetOf", "liveFor", "poolFor",
  // The run loop proper.
  "freshRunState", "weightedRelic", "applyPickup", "breathHeal", "relicSynergy",
  "compileFormula", "evRelic", "awakeById",
];

const METHODS = ["simulateFloor", "simulateDuel"];

// balanceRuns is the game's own headless run loop: event placement, the real event
// table, the bazaar, the draft and its rerolls, and combat. It is lifted verbatim rather
// than re-walked in the recorder, so the run corpus records the GAME's run. The brace
// scanner cannot read it — it holds regex literals the depth count trips over — so it is
// bounded by where the next method begins instead.
const BOUNDED_METHODS = ["balanceRuns"];

/**
 * Assembles the engine.
 *
 * `instrumentRuns` injects read-only trace hooks into balanceRuns so a recorder can
 * observe every floor. It changes no behaviour and draws no random numbers; without it
 * the loop is the source's, character for character.
 */
export function buildEngine({ instrumentRuns = false } = {}) {
  const method = (name) => {
    const text = liftMethodTo(name);
    return instrumentRuns && name === "balanceRuns" ? instrumentBalanceRuns(text) : text;
  };

  const parts = [
    '"use strict";',
    "const window = { __ddDefModel: 'pct' };",
    // META reads the save store for the player's level and frontier hall. A tiny stub stands
    // in, so progression formulas can be exercised at any point in the progression.
    "const __store = { 'dd.unlocked': 1, 'dd.xp': 0 };",
    "const Store = { get: (k, d) => (k in __store ? __store[k] : d), set: (k, v) => { __store[k] = v; } };",
    ...CONSTS.map(liftConst),
    ...FUNCTIONS.map(liftFunction),
    "class Engine {",
    "  itemName(id) { return id; }",           // log text only — corpus compares structure
    "  t(key) { return key; }",
    ...METHODS.map(liftMethod),
    ...BOUNDED_METHODS.map(method),
    "}",
    "globalThis.__api = { Engine, mulberry32, packFor, packGold, heroStatOf, heroStatRows, heroCtxOf,",
    "  applyDef, count, ITEMS, POOL, DUNGEONS, TRIGGERS, EMITTERS, MAX_FLOOR, SHOP_FLOOR, poolFor, liveFor,",
    "  META, setUnlocked: n => { __store['dd.unlocked'] = n; },",
    "  EVENTS, freshRunState, weightedRelic, applyPickup, breathHeal, relicSynergy,",
    "  compileFormula, evRelic, awakeById, ENEMY_STAT_FORMULAS, ENEMY_ROLE_FORMULAS,",
    "  SHOP_BUY, SHOP_UP, EV_MIN_SPD, EV_MIN_DEF };",
  ];

  const ctx = vm.createContext({
    globalThis: {}, Math, JSON, Object, Array, Set, Map, Number, String, console,
    // compileFormula builds enemy curves with `new Function`, so the realm needs it.
    Function, isFinite, isNaN,
  });
  ctx.globalThis = ctx;
  vm.runInContext(parts.join("\n\n"), ctx, { filename: "engine.lifted.js" });
  return ctx.__api;
}
