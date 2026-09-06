/*
 * Headless engine host.
 *
 * Assembles the lifted combat engine into a sandbox and exposes it to Node. The
 * Component methods reference `this.itemName` and `this.t` for log text; those are
 * stubbed to return stable ids, since the corpus compares event structure, not prose.
 */

import vm from "node:vm";
import { liftConst, liftFunction, liftMethod } from "./lift.mjs";

/* Module-scope declarations the engine reaches for. Order matters only for consts
   that reference each other at definition time (POOL reads ITEMS). */
const CONSTS = [
  "ITEMS", "NEWR", "POOL", "KIND_META", "TRIGGERS", "EMITTERS", "COMPONENTS",
  "MAX_FLOOR", "SHOP_FLOOR", "ENEMY_G", "ENEMY_ROW", "DUNGEONS",
];

const FUNCTIONS = [
  "mulberry32", "count", "applyDef", "heroStatRows", "heroStatOf", "heroCtxOf",
  "currentAtk", "packFor", "packGold", "setOf", "prodSetOf", "liveFor", "poolFor",
];

const METHODS = ["simulateFloor", "simulateDuel"];

export function buildEngine() {
  const parts = [
    '"use strict";',
    "const window = { __ddDefModel: 'pct' };",
    ...CONSTS.map(liftConst),
    ...FUNCTIONS.map(liftFunction),
    "class Engine {",
    "  itemName(id) { return id; }",           // log text only — corpus compares structure
    "  t(key) { return key; }",
    ...METHODS.map(liftMethod),
    "}",
    "globalThis.__api = { Engine, mulberry32, packFor, packGold, heroStatOf, heroStatRows, heroCtxOf,",
    "  applyDef, count, ITEMS, POOL, DUNGEONS, TRIGGERS, EMITTERS, MAX_FLOOR, SHOP_FLOOR, poolFor, liveFor };",
  ];

  const ctx = vm.createContext({ globalThis: {}, Math, JSON, Object, Array, Set, Map, Number, String, console });
  ctx.globalThis = ctx;
  vm.runInContext(parts.join("\n\n"), ctx, { filename: "engine.lifted.js" });
  return ctx.__api;
}
