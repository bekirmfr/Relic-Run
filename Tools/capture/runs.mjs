/*
 * Run corpus — the Phase 5 gate.
 *
 *   node Tools/capture/runs.mjs
 *
 * Records full 13-floor delves out of the game's own headless run loop (balanceRuns),
 * observed through the read-only hooks in instrument.mjs. The port replays each run and
 * must reproduce every recorded state.
 *
 * WHAT IS UNDER TEST, AND WHAT IS NOT
 *
 * balanceRuns carries a greedy bot: pickScore ranks a draft, eventChoice reads an event's
 * hint text, punchOf decides whether the bazaar awakens or buys. None of that is a game
 * rule — it is the Balance Lab's stand-in for a player — so none of it is ported. The
 * corpus records the DECISIONS it made, and the port replays them.
 *
 * What the port must reproduce is everything around a decision: where events land, what a
 * choice does to the run, the breath heal, the bazaar's prices, the reroll ladder, what a
 * pickup does, and which relics the offer holds.
 *
 * An offer is recorded as a SET, sorted alphabetically. Which relics weightedRelic drew is
 * game code; the order they are shown in is pickScore's ranking, which is the bot's.
 *
 * Combat is not re-recorded here — the fight corpus already gates it event for event. A run
 * records the state either side of each fight, which is what catches a fight that went
 * differently inside a run.
 *
 * Two things about the packs a run fights. balanceRuns hands packFor a stat CURVE rather than
 * a dungeon, so these foes carry no boss relics and no dungeon-level scaling — the real game
 * passes {bossRelics, ghoolemBoss} instead. That is deliberate here: pack COMPOSITION is
 * gated by packs.json, which does use the dungeons, and keeping the two apart is what stops a
 * run-loop failure from being a pack bug wearing a disguise. The Lab's dungeon-level
 * multiplier is not exercised at all, because nothing in the game ever applies it.
 */

import { writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { buildEngine } from "./engine.mjs";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "corpus");

const api = buildEngine({ instrumentRuns: true });
const engine = new api.Engine();

/** Everything about a run's state that the port has to get right. Integers and ids only. */
function snap(st) {
  const awake = {};
  for (const k of Object.keys(st.awake || {})) {
    if (st.awake[k] > 0) awake[k] = st.awake[k] | 0;
  }

  return {
    php: st.php | 0,
    pmax: st.pmax | 0,
    gold: st.gold | 0,
    atkB: st.atkB | 0,
    defB: st.defB | 0,
    spdB: st.spdB | 0,
    luckB: st.luckB | 0,
    adrenaline: st.adrenaline | 0,
    midasBonus: st.midasBonus | 0,
    rerolls: st.rerolls | 0,
    kills: st.kills | 0,
    strikeTot: st._strikeTot | 0,
    anvilB: st._anvilB | 0,
    chaliceG: st._chaliceG | 0,
    debtLeft: st._debtLeft | 0,
    glassBroken: !!st._glassBroken,
    soilUsed: !!st._soilUsed,
    items: st.items.slice(),
    awake,
    pending: st.pending ? { floor: st.pending.floor | 0, gold: st.pending.gold | 0 } : null,
  };
}

/** The set of relics an offer held. Order is the bot's ranking, so it is discarded. */
const offerSet = (offer) => offer.slice().sort();

/**
 * Parameter sets. Each is a different shape of run — a fragile hero, a rich one, a deeper
 * dungeon, a hero who starts with relics already in hand — so the loop is exercised across
 * more than one path through the bazaar and the reroll ladder.
 */
const SETS = [
  { name: "base", runs: 40, P: { seed: 0xBA1A } },
  { name: "frail", runs: 24, P: { seed: 0x5EED, baseHp: 60, breath: 2 } },
  { name: "rich", runs: 24, P: { seed: 0xC0FFEE, baseGold: 250, baseHp: 120 } },
  { name: "deep", runs: 24, P: { seed: 0xDEE9, baseHp: 140, baseAtk: 8, baseDef: 3 } },
  { name: "kitted", runs: 24, P: { seed: 0x1A2B, startKit: ["clover", "tooth", "boots"], baseGold: 80 } },
  { name: "wide", runs: 24, P: { seed: 0x7A17, draftChoices: 5, baseLck: 25 } },

  /* The harness's greed leaves whole outcomes unrecorded: it always refuses the Chained
     Ghost's locket, never rerolls with a Merchant's Thumb in hand, and never thinks a Debt of
     Flesh worth awakening. These steer it. Choosing is not a rule, so steering the chooser
     changes nothing that is under test — it only reaches the rules the greedy line never
     walks past. */
  { name: "choice0", runs: 20, P: { seed: 0x0C01, forceChoice: 0, baseGold: 120 } },
  { name: "choice1", runs: 20, P: { seed: 0x0C02, forceChoice: 1, baseGold: 120 } },
  { name: "choice2", runs: 20, P: { seed: 0x0C03, forceChoice: 2, baseGold: 120 } },

  /* Every event that costs speed, taken, so the -10 floor an event cannot drag a stat past
     is actually reached: the Ghost's locket and the Spike Trap both cost 5, and robbing the
     dead miner costs 3 more. */
  /* The floor an event cannot drag a stat past needs THREE speed losses in one run — the
     Ghost's locket and the Spike Trap for five each, and a failed robbery of the dead miner
     for three more. Only about one run in 126 places all three, so the seed is chosen rather
     than left to chance: 0x128BA lands the triple four times in its first thirty, and sixteen times in 120. */
  {
    name: "slowed",
    runs: 120,
    P: { seed: 0x128BA, baseGold: 150, forceChoice: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0] },
  },

  { name: "thumb", runs: 20, P: { seed: 0x7B00, baseGold: 600, startKit: ["merchantthumb"] } },

  /* A Debt of Flesh pays out in health at every counter, and twice as much once awakened —
     but only a hero who is actually hurt can tell the two apart, so the breather is cut to
     one and the purse left deep enough to keep buying. */
  {
    name: "debt",
    runs: 40,
    P: {
      seed: 0xDEB7, baseGold: 800, breath: 1,
      startKit: ["debtflesh"], forceAwaken: "debtflesh",
    },
  },
];

const runs = [];

for (const set of SETS) {
  let cur = null;
  let rerollsBefore = 0;

  const P = Object.assign({}, set.P, {
    // Take the game's own pack path rather than the Balance Lab's formula table. See
    // instrument.mjs; the two round differently and only this one is ever played.
    packConfig: {},

    onRun(run, st, evAt) {
      const eventFloors = {};
      for (const k of Object.keys(evAt)) eventFloors[k] = evAt[k] | 0;

      cur = {
        id: set.name + "/" + run,
        set: set.name,
        params: set.P,
        seed: (set.P.seed + run * 977) >>> 0,
        eventFloors,
        start: snap(st),
        steps: [],
        end: null,
      };
      rerollsBefore = 0;
      runs.push(cur);
    },

    onEvent(run, f, ev, choice, st, error) {
      // An outcome that threw did nothing, and recording that as truth would gate the port
      // against a hole in the lift rather than against the game.
      if (error) {
        throw new Error(`event ${ev} choice ${choice} threw while recording ${set.name}/${run} ` +
                        `on floor ${f}: ${error && error.message}`);
      }

      cur.steps.push({ f, kind: "event", ev: ev | 0, choice: choice | 0, state: snap(st) });
    },

    onShop(run, f, offer, deal, dealId, st) {
      cur.steps.push({
        f, kind: "shop", offer: offerSet(offer), deal, dealId: dealId || null, state: snap(st),
      });
    },

    onPick(run, f, pick, offer, st) {
      const rerolls = (st.rerolls | 0) - rerollsBefore;
      rerollsBefore = st.rerolls | 0;
      cur.steps.push({
        f, kind: "pick", offer: offerSet(offer), rerolls, pick, state: snap(st),
      });
    },

    onFight(run, f, pack, events, st) {
      cur.steps.push({
        f,
        kind: "fight",
        foes: pack.length,
        events: events.length,
        // What the pack was worth. Composition is gated by packs.json, but a run that loots
        // a different amount needs to say so here rather than surfacing as a gold mismatch
        // three steps later.
        drop: pack.reduce((n, e) => n + (e.drop | 0), 0),
        hp: pack.reduce((n, e) => n + (e.hp | 0), 0),
        state: snap(st),
      });
    },

    onEnd(run, st, dead, deadAt, deadTo) {
      cur.end = { dead: !!dead, floor: dead ? deadAt | 0 : 0, to: deadTo || "", state: snap(st) };
    },
  });

  engine.balanceRuns(set.runs, P);
}

mkdirSync(OUT, { recursive: true });
const json = JSON.stringify(runs);
writeFileSync(join(OUT, "runs.json"), json, "utf8");

const steps = runs.reduce((n, r) => n + r.steps.length, 0);
const died = runs.filter((r) => r.end.dead).length;
console.log("\nrun corpus");
console.log(`  → runs.json          ${String(runs.length).padStart(4)} runs  ` +
            `${(Buffer.byteLength(json) / 1024).toFixed(0)} KB`);
console.log(`  ${steps} steps · ${died} deaths · ${runs.length - died} clears`);

const kinds = {};
for (const r of runs) for (const s of r.steps) kinds[s.kind] = (kinds[s.kind] || 0) + 1;
console.log("  " + Object.entries(kinds).map(([k, n]) => k + " " + n).join(" · "));

const seen = new Set();
for (const r of runs) for (const k of Object.keys(r.eventFloors)) seen.add(r.eventFloors[k]);
console.log(`  events reached: ${seen.size} of ${api.EVENTS.length}`);
