/*
 * Golden corpus generator — the contract every ported phase is diffed against.
 *
 *   node Tools/capture/capture.mjs
 *
 * Runs the lifted JS combat engine over a designed spread of loadouts and records
 * (input, event stream, final state) for each case. RelicRun.Core must reproduce
 * these byte-for-byte; Tools/capture/verify.mjs re-runs the corpus against the
 * engine to prove the recording itself is reproducible.
 *
 * Pack generation uses a SEPARATE rng from the fight, and packs are recorded
 * verbatim. That isolates the combat engine from EnemyPackGenerator, so a Phase 2
 * failure can never be a Phase 5 bug wearing a disguise. Pack generation gets its
 * own corpus (packs.json) for the Phase 5 gate.
 *
 * Tiers map onto the plan's gates:
 *   bare       Phase 2 — the ATB loop with no relics at all
 *   primitives Phase 3 — chain primitives only, to exercise depth/decay/fizzle
 *   solo       Phase 4 — every relic alone, so a failure names one relic
 *   mixed      Phase 4 — duplicates, sockets and awakenings interacting
 *   duel       Phase 6 — the symmetric versus engine
 */

import { writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { buildEngine } from "./engine.mjs";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "corpus");
const api = buildEngine();
const engine = new api.Engine();

const MASTER_SEED = 0x5E1F00D;          // fixed: the corpus must regenerate identically
const master = api.mulberry32(MASTER_SEED);
const pick = (arr) => arr[Math.floor(master() * arr.length)];
const int = (n) => Math.floor(master() * n);
const seed = () => Math.floor(master() * 0xffffffff) >>> 0;

const DELVE_POOL = api.poolFor("delve");
const VERSUS_POOL = api.poolFor("versus");
const PRIMITIVES = ["alchemist", "altar", "thorns", "tooth", "magnet", "cutpurse"];
const TRIGGERS = Object.keys(api.TRIGGERS);
const EMITTERS = Object.keys(api.EMITTERS);
const COMBAT_FLOORS = [1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13];   // 7 is the bazaar

/* ---------- case construction ---------- */

function heroState({ floor, items = [], sockets = {}, awake = {}, level = 1, mode = "delve" }) {
  return {
    _strikeTot: 0, mode, awake, floor,
    php: 100 + 5 * (level - 1), pmax: 100 + 5 * (level - 1), gold: 0,
    items: items.slice(), sockets: { ...sockets },
    adrenaline: 0, midasBonus: 0, kills: 0,
    atkB: 0, defB: 0, spdB: 0, luckB: 0, furyB: 0, _carry: null,
    baseAtk: 5 + Math.round(level / 5), baseDef: Math.round(level / 4),
    baseSpd: 25 + Math.round(level / 5), baseLck: 10 + Math.round(level / 4),
    _anvilB: 0, _chaliceG: 0, _debtLeft: 0, _glassBroken: false, _soilUsed: false,
  };
}

function duelSide(name, items, sockets, awake, hp) {
  return {
    name, drop: 15, awake, base: { hp, atk: 5, def: 0, spd: 25, lck: 10 },
    php: hp, pmax: hp, gold: 0, items: items.slice(), sockets: { ...sockets },
    adrenaline: 0, midasBonus: 0, atkB: 0, defB: 0, spdB: 0, luckB: 0, kills: 0,
    _anvilB: 0, _chaliceG: 0, _debtLeft: 0, _glassBroken: false, _soilUsed: false,
    _flesh3: false, _strikeTot: 0,
  };
}

const finalOf = (st) => ({
  php: st.php, pmax: st.pmax, gold: st.gold, kills: st.kills,
  adrenaline: st.adrenaline, atkB: st.atkB, defB: st.defB, spdB: st.spdB, luckB: st.luckB,
  strikeTot: st._strikeTot, anvilB: st._anvilB, chaliceG: st._chaliceG,
  debtLeft: st._debtLeft, glassBroken: !!st._glassBroken, soilUsed: !!st._soilUsed,
});

function delveCase(id, tier, { floor, dungeon = 1, items, sockets, awake, level }) {
  const packRng = api.mulberry32(seed());
  const cfg = api.DUNGEONS[dungeon - 1];
  const pack = api.packFor(floor, packRng, { bossRelics: cfg.bossRelics, ghoolemBoss: !!cfg.ghoolemBoss });
  const fightSeed = seed();
  const st = heroState({ floor, items, sockets, awake, level });
  const input = JSON.parse(JSON.stringify({ hero: st, pack }));
  const events = engine.simulateFloor(st, pack, api.mulberry32(fightSeed));
  return {
    id, tier, mode: "delve", floor, dungeon, fightSeed,
    input, events: [...events], carry: events._carry, final: finalOf(st),
  };
}

function duelCase(id, { itemsA, itemsB, socketsA = {}, socketsB = {}, awakeA = {}, awakeB = {}, hp = 60 }) {
  const fightSeed = seed();
  const A = duelSide("you", itemsA, socketsA, awakeA, hp);
  const B = duelSide("Rival", itemsB, socketsB, awakeB, hp);
  const input = JSON.parse(JSON.stringify({ a: A, b: B }));
  const events = engine.simulateDuel(A, B, api.mulberry32(fightSeed));
  return {
    id, tier: "duel", mode: "versus", fightSeed, input, events: [...events],
    carryA: events._carry, carryB: events._carryB,
    final: { a: finalOf(A), b: finalOf(B) },
  };
}

/* ---------- tiers ---------- */

const cases = [];

/* bare — every combat floor of every dungeon, no relics */
for (const d of [1, 4, 7, 10]) {
  for (const floor of COMBAT_FLOORS) {
    cases.push(delveCase(`bare/d${d}/f${floor}`, "bare", { floor, dungeon: d, items: [] }));
  }
}

/* primitives — chain roots only, at depths where decay bites */
for (let i = 0; i < 40; i++) {
  const n = 2 + int(3);
  const items = Array.from({ length: n }, () => pick(PRIMITIVES));
  cases.push(delveCase(`primitives/${i}`, "primitives", { floor: pick(COMBAT_FLOORS), items }));
}

/* solo — each relic alone, three floors each, so a failure names one relic */
for (const id of DELVE_POOL) {
  for (const floor of [3, 8, 12]) {
    cases.push(delveCase(`solo/${id}/f${floor}`, "solo", { floor, items: [id], level: 5 }));
  }
}

/* mixed — duplicates, sockets, awakenings, deeper dungeons, levelled heroes */
for (let i = 0; i < 120; i++) {
  const n = 3 + int(6);
  const items = Array.from({ length: n }, () => pick(DELVE_POOL));
  const sockets = {};
  for (let k = 0; k < items.length; k++) {
    const r = master();
    if (r < 0.25) sockets[k] = pick(TRIGGERS);
    else if (r < 0.45) sockets[k] = pick(EMITTERS);
  }
  const awake = {};
  for (const id of new Set(items)) if (master() < 0.3) awake[id] = 1;
  cases.push(delveCase(`mixed/${i}`, "mixed", {
    floor: pick(COMBAT_FLOORS), dungeon: 1 + int(10), items, sockets, awake, level: 1 + int(20),
  }));
}

/* duel — symmetric versus, both sides fully equipped */
for (let i = 0; i < 60; i++) {
  const mk = () => Array.from({ length: 2 + int(6) }, () => pick(VERSUS_POOL));
  const sock = (items) => {
    const s = {};
    for (let k = 0; k < items.length; k++) if (master() < 0.3) s[k] = master() < 0.5 ? pick(TRIGGERS) : pick(EMITTERS);
    return s;
  };
  const itemsA = mk(), itemsB = mk();
  const awake = (items) => Object.fromEntries([...new Set(items)].filter(() => master() < 0.25).map((i) => [i, 1]));
  cases.push(duelCase(`duel/${i}`, {
    itemsA, itemsB, socketsA: sock(itemsA), socketsB: sock(itemsB),
    awakeA: awake(itemsA), awakeB: awake(itemsB), hp: 50 + int(60),
  }));
}

/* packs — the Phase 5 gate for EnemyPackGenerator, seed in / pack out */
const packs = [];
for (let d = 1; d <= 10; d++) {
  for (const floor of COMBAT_FLOORS) {
    for (let rep = 0; rep < 3; rep++) {
      const s = seed();
      const cfg = api.DUNGEONS[d - 1];
      packs.push({
        dungeon: d, floor, seed: s,
        pack: api.packFor(floor, api.mulberry32(s), { bossRelics: cfg.bossRelics, ghoolemBoss: !!cfg.ghoolemBoss }),
      });
    }
  }
}

/* rng — the Phase 1 gate.
   Recorded as the generator's exact 32-bit output, NOT as decimal doubles. A draw is
   exactly n / 2^32, so n is lossless and every parser agrees on an integer. Doubles are
   not safe here: written as text and read back, a value can land one ULP away in a parser
   that is not correctly rounded. Unity's does exactly that — it read 0.1853655439335853
   back one ULP high — which failed this gate against a port that was bit-perfect. Every
   other corpus file is already integers-only, so this keeps the whole corpus
   parser-independent. */
const rngCases = [1, 2, 12345, 0x5E1F00D, 20260906].map((s) => {
  const r = api.mulberry32(s);
  return { seed: s, raw: Array.from({ length: 1000 }, () => r() * 4294967296) };
});

/* defence — the Phase 1 gate for applyDef. A fixed grid, so it consumes no master
   draws and appending it leaves every other corpus file byte-identical. */
const defense = [];
for (const model of ["pct", "flat"]) {
  for (let dmg = 1; dmg <= 60; dmg++) {
    for (const def of [0, 1, 2, 3, 5, 8, 10, 12, 16, 20, 25, 30, 40, 60, 100]) {
      defense.push({ model, dmg, def, out: api.applyDef(dmg, def, model) });
    }
  }
}

/* ---------- write ---------- */

mkdirSync(OUT, { recursive: true });
const byTier = {};
for (const c of cases) (byTier[c.tier] ||= []).push(c);

const write = (name, data) => {
  writeFileSync(join(OUT, name), JSON.stringify(data), "utf8");
  const kb = (Buffer.byteLength(JSON.stringify(data)) / 1024).toFixed(0);
  console.log(`  → ${name.padEnd(20)} ${String(data.length).padStart(4)} cases  ${kb} KB`);
};

console.log("\ncorpus");
for (const [tier, list] of Object.entries(byTier)) write(`${tier}.json`, list);
write("packs.json", packs);
write("rng.json", rngCases);
write("defense.json", defense);

const totalEvents = cases.reduce((n, c) => n + c.events.length, 0);
const manifest = {
  generated: new Date().toISOString(),
  masterSeed: MASTER_SEED,
  engine: "lifted from .port/Daily Delve.dc.html",
  tiers: Object.fromEntries(Object.entries(byTier).map(([t, l]) => [t, l.length])),
  cases: cases.length,
  events: totalEvents,
  packs: packs.length,
  rngSeeds: rngCases.map((r) => r.seed),
  contract: {
    note: "Every field of every event is contractual. RelicRun.Core must reproduce all of them.",
    naming:
      "Events were recorded with itemName(id) => id and t(key) => key. The C# differ must run " +
      "the engine in the same corpus mode, so `src` contains relic ids and string keys rather " +
      "than display names. That keeps `src` comparable and locale-independent.",
    srcIsStructural:
      "Set-bonus log lines (e.g. \"Flesh set — +3 max HP\") carry no rid, so `src` is their only " +
      "identity. Those strings are hardcoded English in the engine and port verbatim.",
    isolation:
      "Packs are recorded verbatim and fights use a separate seed, so the combat engine and " +
      "EnemyPackGenerator fail independently.",
    gates: {
      "phase 1": "rng.json + defense.json",
      "phase 2": "bare.json",
      "phase 3": "primitives.json",
      "phase 4": "solo.json + mixed.json",
      "phase 5": "packs.json",
      "phase 6": "duel.json",
    },
  },
};
writeFileSync(join(OUT, "manifest.json"), JSON.stringify(manifest, null, 2) + "\n", "utf8");
console.log(`\n  ${cases.length} cases · ${totalEvents} events · ${packs.length} packs\n`);
