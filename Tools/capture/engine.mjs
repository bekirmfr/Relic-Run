/*
 * Headless engine host.
 *
 * Assembles the lifted combat engine into a sandbox and exposes it to Node. The
 * Component methods reference `this.itemName` and `this.t` for log text; those are
 * stubbed to return stable ids, since the corpus compares event structure, not prose.
 */

import vm from "node:vm";
import { liftConst, liftFunction, liftMethod, liftMethodTo } from "./lift.mjs";
import { instrumentBalanceRuns, instrumentRunSeed } from "./instrument.mjs";

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
  // The versus lobby rolls each rival's APPEARANCE — outfit, colours, accent, motto — out of
  // the same generator that then rolls their level, hall and stats: twenty-three draws a
  // rival. Leaving the wardrobe out would not skip those draws quietly. The source wraps them
  // in try/catch, so they would throw, consume nothing, and silently shift every statline in
  // the lobby. These are lifted for their effect on the stream, not for their output.
  "HERO_LAYERS", "PACK", "FAMILIES", "DRESS_SWATCHES", "Studio",
  // The palette: the fixed colours that are never recoloured, and how a family's base hex
  // derives its dark and light companions.
  "SOLOS", "SHADE_DEFAULTS",
  // The merchant's greeting is picked with a SEEDED draw, from inside a callback the
  // animation defers. It is one line of flavour that moves every number after it.
  "MERCHANT_LINES",
];

const FUNCTIONS = [
  "mulberry32", "count", "applyDef", "heroStatRows", "heroStatOf", "heroCtxOf",
  "currentAtk", "packFor", "packGold", "setOf", "prodSetOf", "liveFor", "poolFor",
  // The run loop proper.
  "freshRunState", "weightedRelic", "applyPickup", "breathHeal", "relicSynergy",
  "compileFormula", "evRelic", "awakeById", "luckRoll",
  "uniqHex", "stackOrder", "slotList",
  // The Daily Delve's shared seed: the UTC date read as a number.
  "dailySeed",
  // The colour maths behind the palette swap: forty lines, and the whole of what the
  // Changing Room needs in order to recolour a delver live.
  "shade", "hexToHsl", "hslToHex", "applyShade", "famParams", "shadeParams",
];

const METHODS = ["simulateFloor", "simulateDuel"];

// balanceRuns is the game's own headless run loop: event placement, the real event
// table, the bazaar, the draft and its rerolls, and combat. It is lifted verbatim rather
// than re-walked in the recorder, so the run corpus records the GAME's run. The brace
// scanner cannot read it — it holds regex literals the depth count trips over — so it is
// bounded by where the next method begins instead.
const BOUNDED_METHODS = [
  "balanceRuns", "finishRun",
  // The versus lobby: how a match is set up, who is fought next, and what a round costs.
  "startRun", "draftOffer", "startVersus", "rivalPack", "versusResolve", "hallForDuel",
  "pickRelic", "fight", "descend", "finishDescend", "proceedDescend", "shopOffer",
  "bazaarFloor", "tierPack", "landFrom",
  "shopBuy", "shopAwaken", "shopSocket", "shopLeave", "spendHeal",
  // The delve's own UI path: the between-floor event, the reroll ladder and the one revive.
  // balanceRuns models all three, but it models them its own way — see delve.mjs.
  "chooseEvent", "eventGo", "rerollPrice", "rerollDraft", "revive", "cashOut",
  // playNext is the playback animation, and it is lifted for one reason: at the end of
  // the event queue it commits the fight back into the run and dispatches the next
  // phase. Its own randomness is particle positions drawn from Math.random, a different
  // generator from the seeded one, so none of it reaches the fight.
  "schedNext", "playNext",
  // Log text, playback pacing and the fight intro. None of them draws from the seeded
  // stream, so lifting them costs nothing and avoids inventing stand-ins.
  "lineFor", "evDelay", "startFight",
];

/**
 * Assembles the engine.
 *
 * `instrumentRuns` injects read-only trace hooks into balanceRuns so a recorder can
 * observe every floor. It changes no behaviour and draws no random numbers; without it
 * the loop is the source's, character for character.
 */
export function buildEngine({ instrumentRuns = false, countDraws = false } = {}) {
  const method = (name) => {
    let text = liftMethodTo(name);
    if (instrumentRuns && name === "balanceRuns") text = instrumentBalanceRuns(text);
    if (countDraws && name === "startRun") text = instrumentRunSeed(text);
    return text;
  };

  const parts = [
    '"use strict";',
    // The source asks prefers-reduced-motion and takes a DIFFERENT path when it is set —
    // different code, and different draws from the seeded generator. Which path is recorded
    // has to be a decision, so it is a switch rather than an accident of the stub.
    "const __env = { reducedMotion: false };",
    "const window = { __ddDefModel: 'pct',",
    "  matchMedia: (q) => ({ matches: __env.reducedMotion && /reduce/.test(q) }) };",
    "const SFX = new Proxy({}, { get: () => () => {} });",
    "const tele = () => {};",
    "const Telemetry = { runCount: 0 };",
    // META reads the save store for the player's level and frontier hall. A tiny stub stands
    // in, so progression formulas can be exercised at any point in the progression.
    "const __store = { 'dd.unlocked': 1, 'dd.xp': 0 };",
    "const Store = { get: (k, d) => (k in __store ? __store[k] : d), set: (k, v) => { __store[k] = v; } };",
    ...CONSTS.map(liftConst),
    ...FUNCTIONS.map(liftFunction),
    "class Engine {",
    "  itemName(id) { return id; }",           // log text only — corpus compares structure
    "  t(key) { return key; }",
    // finishRun is scoring wrapped in presentation: sound, telemetry, screen transitions and
    // a gold counter that animates. Stubbing those is what lets the arithmetic — banked gold,
    // score, stars, XP — be lifted rather than transcribed.
    "  clearTimers() {}",
    // The source defers work behind animation, and some of that work DRAWS — the merchant's
    // line, the next pack. Dropping the callbacks would quietly shorten the random stream, so
    // they are queued instead and the driver drains them where the animation would have run.
    "  later(fn, ms) { (this._pending || (this._pending = [])).push(fn); }",
    "  drain() {",
    "    let guard = 0;",
    // The guard is a stop against a callback that reschedules itself forever, not a
    // budget. A long fight defers a sweep per event and can queue thousands; cutting it
    // short leaves the fight unfinished and the recorder walks straight past the round.
    "    while (this._pending && this._pending.length && guard++ < 2000000) {",
    "      const fn = this._pending.shift();",
    "      fn();",
    "    }",
    "  }",
    "  countTo(target, ms, from) {}",
    // setState takes an object or a reducer; the lobby uses both.
    "  setState(next, done) {",
    "    const patch = typeof next === 'function' ? next(this.state) : next;",
    "    Object.assign(this.state, patch);",
    "    if (done) done();",
    "  }",
    "  syncHUD(extra) { if (extra) Object.assign(this.state, extra); }",
    ...METHODS.map(liftMethod),
    ...BOUNDED_METHODS.map(method),
    "}",
    "globalThis.__api = { Engine, mulberry32, packFor, packGold, heroStatOf, heroStatRows, heroCtxOf,",
    "  applyDef, count, ITEMS, POOL, DUNGEONS, TRIGGERS, EMITTERS, MAX_FLOOR, SHOP_FLOOR, poolFor, liveFor,",
    "  META, setUnlocked: n => { __store['dd.unlocked'] = n; },",
    "  EVENTS, freshRunState, weightedRelic, applyPickup, breathHeal, relicSynergy,",
    "  compileFormula, evRelic, awakeById, luckRoll, ENEMY_STAT_FORMULAS, ENEMY_ROLE_FORMULAS,",
    "  SHOP_BUY, SHOP_UP, EV_MIN_SPD, EV_MIN_DEF, REVIVE_SPARKS, Store,",
    "  slotList, Studio, FAMILIES, relicSynergy, dailySeed,",
  "  SOLOS, SHADE_DEFAULTS, shade, hexToHsl, hslToHex, applyShade, famParams,",
  "  setReducedMotion: v => { __env.reducedMotion = !!v; } };",
  ];

  // startVersus seeds its own match from Math.random, so a recorded lobby needs that to be
  // reproducible. The realm gets a seeded one; nothing else in the engine draws from it.
  let seedForNextLobby = 1;
  const scopedMath = Object.create(Math);
  scopedMath.random = () => {
    seedForNextLobby = (seedForNextLobby * 1664525 + 1013904223) >>> 0;
    return seedForNextLobby / 4294967296;
  };

  const ctx = vm.createContext({
    globalThis: {}, Math: scopedMath, JSON, Object, Array, Set, Map, Number, String, console,
    // compileFormula builds enemy curves with `new Function`, so the realm needs it.
    Function, isFinite, isNaN,
    // finishRun timestamps its telemetry.
    Date, Proxy,
  });
  ctx.globalThis = ctx;
  vm.runInContext(parts.join("\n\n"), ctx, { filename: "engine.lifted.js" });
  ctx.__api.seedLobby = (n) => { seedForNextLobby = n >>> 0; };
  return ctx.__api;
}
