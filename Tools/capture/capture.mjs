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

/* Targeted primitives. The random tier above never produces a fizzle or a wasted heal:
   loadouts are 2-4 relics so chains stay shallow, and the hero has always been hit before
   anything heals them. These force both — a hero fast enough to strike first heals at full
   HP, and stacked copies push chains deep enough that a decayed amount rounds to nothing.
   Appended after every other consumer of `master`, so earlier corpus files are unchanged. */
for (const id of PRIMITIVES) {
  for (const n of [1, 2, 3, 4]) {
    cases.push(delveCase(`primitives/stack/${id}x${n}`, "primitives",
      { floor: 8, items: Array(n).fill(id), level: 20 }));
  }
}
for (const floor of [1, 2, 3, 5, 8, 10, 12, 13]) {
  cases.push(delveCase(`primitives/loop/f${floor}`, "primitives",
    { floor, items: PRIMITIVES.slice(), level: 20 }));
  cases.push(delveCase(`primitives/loop2/f${floor}`, "primitives",
    { floor, items: PRIMITIVES.concat(PRIMITIVES), level: 20 }));
}

/* Decay itself needs a relic to fire TWICE inside one chain, which nothing above achieves.
   Blood Altar is the only primitive that can: a landed strike opens one chain and shares it
   between the Cutpurse spill (gold -> Vial heal -> Altar) and the Vampire Tooth heal
   (-> Altar again), so the second Altar pass is decayed. Three copies is the smallest stack
   where the decayed amount differs between the base 0.5 falloff and the CHAIN-set 0.6 —
   round(3.0) vs round(3.6) — so without these the softened decay is unobservable. */
for (const altars of [3, 4]) {
  for (const floor of [3, 8, 12]) {
    cases.push(delveCase(`primitives/decay/altar${altars}/f${floor}`, "primitives", {
      floor, level: 20,
      items: Array(altars).fill("altar").concat(["alchemist", "tooth", "cutpurse"]),
    }));
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

/* stat ledger — the Phase 1 gate for heroStatRows / heroStatOf.
   The `pl` field on combat events only carries the engine's DYNAMIC in-fight mods, so it
   does not exercise the ledger's own rows. This does: randomized loadouts crossed with the
   conditions the rows actually branch on — health fraction, gold, foe rank, mode, awakenings
   and set thresholds. Rows are recorded with their source labels, so a wrong label is caught
   as readily as a wrong number. Placed after every other consumer of `master`, so appending
   it leaves the earlier corpus files byte-identical. */
const STATS = ["atk", "def", "spd", "lck"];
const ledger = [];
for (let i = 0; i < 400; i++) {
  const n = int(9);
  const items = Array.from({ length: n }, () => pick(DELVE_POOL));
  const awake = {};
  for (const id of new Set(items)) if (master() < 0.35) awake[id] = 1;
  const pmax = 60 + int(160);
  const ctx = {
    items, awake,
    mode: master() < 0.3 ? "versus" : "delve",
    base: { atk: 5 + int(6), def: int(6), spd: 25 + int(6), lck: 10 + int(6) },
    run: {
      adrenaline: int(8), midasBonus: int(6), atkB: int(5),
      defB: int(5), spdB: int(5), luckB: int(12),
    },
    php: 1 + int(pmax), pmax,
    gold: int(400), floor: 1 + int(13),
    foeRank: pick(["guard", "elite", "boss", "king", null]),
    debtLeft: 0, glassBroken: false,
    mods: master() < 0.4
      ? [{ stat: pick(STATS), src: "Fury (this floor)", amt: 1 + int(6) },
         { stat: pick(STATS), src: "Sentinel Bell (this floor)", amt: 1 + int(3) }]
      : [],
  };
  ledger.push({
    id: `ledger/${i}`,
    ctx,
    rows: Object.fromEntries(STATS.map((s) => [s, api.heroStatRows(ctx, s)])),
    totals: Object.fromEntries(STATS.map((s) => [s, api.heroStatOf(ctx, s)])),
  });
}

/* Random loadouts never reach some rows: GREED has only six relics in the pool, so its
   tier-7 bonus is unreachable without deliberate duplicates. These targeted cases walk the
   set thresholds and the conditional boundaries directly, so every row in the ledger is
   exercised and the off-by-one cases (>= vs >) are pinned. */
const byKind = {};
for (const id of DELVE_POOL) {
  const k = api.ITEMS[id].kind;
  (byKind[k] ||= []).push(id);
}
const ledgerCase = (id, over) => {
  const ctx = {
    items: [], awake: {}, mode: "delve",
    base: { atk: 5, def: 0, spd: 25, lck: 10 },
    run: { adrenaline: 0, midasBonus: 0, atkB: 0, defB: 0, spdB: 0, luckB: 0 },
    php: 100, pmax: 100, gold: 0, floor: 1, foeRank: null,
    debtLeft: 0, glassBroken: false, mods: [],
    ...over,
  };
  ledger.push({
    id, ctx,
    rows: Object.fromEntries(STATS.map((s) => [s, api.heroStatRows(ctx, s)])),
    totals: Object.fromEntries(STATS.map((s) => [s, api.heroStatOf(ctx, s)])),
  });
};

/* set thresholds: one under, exactly on, and one over, for every kind and tier */
for (const kind of Object.keys(byKind)) {
  const stack = byKind[kind][0];
  for (const n of [2, 3, 4, 5, 6, 7, 8]) {
    ledgerCase(`ledger/set/${kind}/${n}`, { items: Array(n).fill(stack), gold: 250 });
    ledgerCase(`ledger/set/${kind}/${n}+idol`, {
      items: Array(n).fill(stack).concat("hollowidol"), gold: 250,
    });
  }
}

/* gold thresholds: Greed set (7) reads gold/100, Gilded Plate reads gold/50 or /25 */
for (const gold of [0, 49, 50, 99, 100, 149, 199, 200, 999]) {
  ledgerCase(`ledger/gold/greed/${gold}`, { items: Array(7).fill(byKind.GREED[0]), gold });
  ledgerCase(`ledger/gold/plate/${gold}`, { items: ["auricskin"], gold });
  ledgerCase(`ledger/gold/plate-awake/${gold}`, { items: ["auricskin"], awake: { auricskin: 1 }, gold });
  ledgerCase(`ledger/gold/midas/${gold}`, { items: ["midas"], awake: { midas: 1 }, gold, run: { adrenaline: 0, midasBonus: 2, atkB: 0, defB: 0, spdB: 0, luckB: 0 } });
}

/* the bloodied boundary is php < pmax/2, so exactly half must NOT count as bloodied */
for (const php of [49, 50, 51]) {
  ledgerCase(`ledger/bloodied/${php}`, { items: ["berserk"], php, pmax: 100 });
  ledgerCase(`ledger/bloodied-awake/${php}`, { items: ["berserk", "boots"], awake: { berserk: 1 }, php, pmax: 100 });
}

/* Duelist's Oath keys off rank, and off versus regardless of rank */
for (const rank of ["guard", "elite", "boss", "king", null]) {
  ledgerCase(`ledger/rank/${rank}`, { items: ["duelist"], foeRank: rank });
  ledgerCase(`ledger/rank/${rank}/versus`, { items: ["duelist"], foeRank: rank, mode: "versus" });
}

/* floors: SPD and ATK floors bite only when modifiers go deeply negative */
ledgerCase("ledger/floor/spd", { items: Array(4).fill("millstone"), base: { atk: 5, def: 0, spd: 25, lck: 10 } });
ledgerCase("ledger/floor/spd-versus", { items: Array(4).fill("millstone"), mode: "versus" });

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
write("statledger.json", ledger);

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
      "phase 1": "rng.json + defense.json + statledger.json",
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
