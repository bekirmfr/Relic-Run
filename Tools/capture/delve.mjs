/*
 * Delve corpus — the shipped run loop.
 *
 *   node Tools/capture/delve.mjs
 *
 * WHY THIS EXISTS ALONGSIDE THE FIGHT CORPUS
 *
 * The run corpus used to come out of balanceRuns, the Balance Lab's headless harness. That was
 * the only run loop that could be driven without a browser, and it was lifted verbatim, so what
 * it recorded was true — of the Lab. It is not the game. The Lab walks a floor as
 * breath, event, shop-or-(draft, fight); the game walks it as draft, fight, event, breath, and
 * it rolls the NEXT floor's pack the moment a fight ends, before that floor's draft is offered.
 * Same seeds, different stream, different run. And the Lab's bazaar is a sketch: one deal, no
 * Merchant's Thumb discount, awakenings keyed by relic rather than by the copy in hand.
 *
 * So the UI is driven instead, the way versus.mjs drives a match. startRun, pickRelic, fight,
 * playNext, descend, finishDescend, proceedDescend, chooseEvent, the bazaar and revive are all
 * lifted; presentation is stubbed; and the callbacks the animation would have run are drained
 * in place, because some of them draw.
 *
 * THE REDUCED-MOTION PATH
 *
 * As in versus, the recording takes the branch where presentation does not touch the seeded
 * stream. One consequence is worth naming rather than discovering: on the animated path the
 * bazaar floor rolls the next floor's pack, and on the reduced path it does not — so floor 8
 * fights the pack that was rolled for floor 7 and never used.
 *
 * WHAT IS A DECISION AND WHAT IS A RULE
 *
 * The driver below is a player, not a rule: which relic it drafts, when it pays to reroll, what
 * it asks the bazaar for, whether it takes a revive. All of that is RECORDED, and the port
 * replays it. Everything else — where events land, what an outcome does, the breath, the prices,
 * the offers, the packs — has to come out of the port.
 *
 * Runs are shaped by the delver's LEVEL, because that is the only dial the shipped entry point
 * has: level sets the starting purse, the stat bonuses, the breath, the third draft choice at
 * ten and the second bazaar deal at fifteen.
 */

import { writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { buildEngine } from "./engine.mjs";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "corpus");

const api = buildEngine();
api.setReducedMotion(true);

/** XP that lands exactly on a level, so a run's setup is stated rather than approximated. */
function xpForLevel(level) {
  let acc = 0;
  for (let l = 2; l <= level; l++) acc += Math.round(1000 * Math.pow(1.2, l - 2));
  return acc;
}

/** Everything about a run's state the port has to get right. Integers and ids only. */
function snap(G) {
  const awake = [];
  for (const k of Object.keys(G.awake || {})) if (G.awake[k]) awake.push(+k);
  awake.sort((a, b) => a - b);

  return {
    floor: G.floor | 0,
    php: G.php | 0,
    pmax: G.pmax | 0,
    gold: G.gold | 0,
    atkB: G.atkB | 0,
    defB: G.defB | 0,
    spdB: G.spdB | 0,
    luckB: G.luckB | 0,
    adrenaline: G.adrenaline | 0,
    midasBonus: G.midasBonus | 0,
    rerolls: G.rerolls | 0,
    kills: G.kills | 0,
    strikeTot: G._strikeTot | 0,
    anvilB: G._anvilB | 0,
    chaliceG: G._chaliceG | 0,
    debtLeft: G._debtLeft | 0,
    duelF: G._duelF | 0,
    glassBroken: !!G._glassBroken,
    soilUsed: !!G._soilUsed,
    revived: !!G.revived,
    items: G.items.slice(),
    awake,
    pending: G.pending ? { floor: G.pending.floor | 0, gold: G.pending.gold | 0 } : null,
  };
}

/** What a pack was worth. Composition is gated by packs.json; this catches a different roll. */
const packOf = (pack) => ({
  foes: pack.length,
  hp: pack.reduce((n, e) => n + (e.hp | 0), 0),
  atk: pack.reduce((n, e) => n + (e.atk | 0), 0),
  drop: pack.reduce((n, e) => n + (e.drop | 0), 0),
});

/**
 * Whether a seed places all of these events, without playing the run.
 *
 * Some rules only bite when several events land together — the floor a stat cannot be dragged
 * past needs THREE speed losses in one run, and about one run in two hundred places all three.
 * Rather than record two hundred runs to reach one of them, the placement is asked first. It is
 * asked by starting the run and reading where the events went, so the shuffle stays the game's.
 */
function places(seed, level, want) {
  api.Store.set("dd.xp", xpForLevel(level));
  const engine = new api.Engine();
  engine.state = {};
  engine.startRun(seed, false, 1);

  const landed = Object.keys(engine.G.eventGaps).map((k) => engine.G.eventGaps[k]);
  return want.every((ev) => landed.indexOf(ev) >= 0);
}

/**
 * A deterministic stand-in for a player.
 *
 * `wants` is a preference list; `deal` says what to ask the bazaar for; `rerollFor` is how many
 * rerolls the delver will pay for when the shelf holds nothing they want; `event` picks an
 * outcome by index so the choices a greedy line never takes are recorded too.
 */
function play(id, seed, profile) {
  api.Store.set("dd.xp", xpForLevel(profile.level));
  api.Store.set("dd.unlocked", 1);
  api.Store.set("dd.runs", 0);
  api.Store.set("dd.clears", 0);
  api.Store.set("dd.goldLife", 0);
  api.Store.set("dd.arts", []);

  const engine = new api.Engine();
  engine.state = { sparks: profile.revive ? 150 : 0, supporter: false };
  engine.startRun(seed, false, 1);
  engine.drain();

  const G = engine.G;
  const eventFloors = {};
  for (const k of Object.keys(G.eventGaps)) eventFloors[k] = G.eventGaps[k] | 0;

  const record = {
    id,
    set: profile.name,
    seed: seed >>> 0,
    level: profile.level | 0,
    eventFloors,
    start: snap(G),
    steps: [],
    end: null,
  };

  const wanted = (offer) => {
    for (const want of profile.wants) if (offer.indexOf(want) >= 0) return want;
    return null;
  };

  const priceOf = (base) => Math.round(base * (api.count(G.items, "merchantthumb") ? 0.8 : 1));

  let guard = 0;

  while (engine.state.screen !== "over" && guard++ < 4000) {
    const stage = engine.state.stage;

    if (stage === "draft") {
      const floor = G.floor | 0;
      const breath = G.breathHealed | 0;
      let rerolls = 0;
      let offer = engine.state.offer.slice();

      while (rerolls < (profile.rerollFor || 0) && !wanted(offer)) {
        if (G.gold < engine.rerollPrice(G)) break;
        engine.rerollDraft();
        engine.drain();
        rerolls++;
        offer = engine.state.offer.slice();
      }

      const pick = wanted(offer) || offer[0];

      // pickRelic takes the relic and DEFERS the fight, so the state here is the draft's own:
      // the pickup applied, the floor's pack not yet drawn.
      engine.pickRelic(pick);
      record.steps.push({ f: floor, kind: "draft", breath, offer, rerolls, pick, state: snap(G) });

      engine.drain();

      // pickRelic runs straight into the floor's fight, and the drain plays it out.
      record.steps.push({ f: floor, kind: "fight", pack: packOf(G.pack || []), state: snap(G) });
      continue;
    }

    if (stage === "event") {
      const floor = G.floor | 0;
      const ev = engine.state.event.id | 0;
      const def = api.EVENTS[ev];
      // Preference, then affordability: the game refuses a choice the purse cannot cover, and a
      // driver that insisted would record a floor where nothing happened.
      const wants = Array.isArray(profile.event) ? (profile.event[ev] || 0) : profile.event;
      let choice = Math.min(wants, def.choices.length - 1);
      for (let k = 0; k < def.choices.length; k++) {
        const c = (choice + k) % def.choices.length;
        if (!def.choices[c].cost || G.gold >= def.choices[c].cost) { choice = c; break; }
      }

      const before = JSON.stringify(snap(G));

      engine.chooseEvent(choice);
      engine.drain();

      // An outcome that threw did nothing, and recording that as truth would gate the port
      // against a hole in the lift rather than against the game.
      if (!engine.state.event.outcome) {
        throw new Error(`event ${ev} choice ${choice} produced no outcome in ${id} on floor ${floor}`);
      }

      record.steps.push({
        f: floor, kind: "event", ev, choice,
        inert: before === JSON.stringify(snap(G)),
        state: snap(G),
      });

      engine.eventGo();
      engine.drain();
      continue;
    }

    if (stage === "shop") {
      const floor = G.floor | 0;
      const deals = [];
      let turn = 0;

      // The bazaar keeps serving until it says it is done: one deal a visit, two with an
      // awakened Merchant's Thumb, and one more again for a delver past level fifteen.
      while (!G.shopDone && turn++ < 6) {
        const offer = (G.shopOffer || []).slice();
        const awakenable = (G.shopAwake || []).map((e) => ({ id: e.id, slot: e.i }));
        const canAwaken = awakenable.length > 0 && G.gold >= priceOf(api.SHOP_UP);
        const canBuy = offer.length > 0 && G.gold >= priceOf(api.SHOP_BUY);

        // The delver wakes what they came for if it is on the shelf, and otherwise takes what is.
        let wake = awakenable[0];
        for (const want of profile.wants) {
          const match = awakenable.find((e) => e.id === want);
          if (match) { wake = match; break; }
        }

        let deal = null;
        if (profile.deal === "awaken" && canAwaken) {
          deal = { kind: "awaken", relic: wake.id, slot: wake.slot };
          engine.shopAwaken(wake.slot);
        } else if (profile.deal !== "leave" && canBuy) {
          const pick = wanted(offer) || offer[0];
          deal = { kind: "buy", relic: pick };
          engine.shopBuy(pick);
        }

        if (!deal) break;

        // shopDone is the bazaar's own answer to "that is all for this visit". Recording it is
        // what lets a replay catch a port that would have served one more.
        deals.push({ offer, awakenable, deal, done: !!G.shopDone, state: snap(G) });

        // A deal defers a shopLeave, and leaving descends straight into the next floor on this
        // path. Drop the queued walk so the visit's second deal is still a deal.
        engine._pending = [];
      }

      record.steps.push({ f: floor, kind: "shop", deals, state: snap(G) });
      engine.shopLeave();
      engine.drain();
      continue;
    }

    if (stage === "revive") {
      if (profile.revive && !G.revived) {
        const floor = G.floor | 0;
        engine.revive(false);
        engine.drain();
        record.steps.push({ f: floor, kind: "revive", pack: packOf(G.pack || []), state: snap(G) });
        continue;
      }

      engine.finishRun("died");
      engine.drain();
      continue;
    }

    if (stage === "decide") {
      if (profile.cashOutAt && G.floor >= profile.cashOutAt) {
        engine.cashOut();
        engine.drain();
        continue;
      }

      engine.descend();
      engine.drain();
      continue;
    }

    throw new Error(`${id}: nothing to do in stage ${stage}`);
  }

  record.end = { how: G.how || "unresolved", floor: G.floor | 0, stars: G.stars | 0, state: snap(G) };
  return record;
}

/*
 * The delver profiles. Between them they walk every branch the loop has: a level-1 purse that
 * can never afford the bazaar and a level-20 one that can afford it twice, a delver who hoards
 * for an awakening and one who buys, every event outcome index, the reroll ladder, and the
 * relics whose rules only fire at a counter — the Thumb's discount and the Debt's payment.
 */
const SETS = [
  { name: "novice", runs: 40, level: 1, wants: ["heart", "iron", "tooth"], deal: "buy", event: 0 },
  { name: "veteran", runs: 40, level: 10, wants: ["fang", "whetstone", "adrenaline", "thorns"], deal: "awaken", event: 1, rerollFor: 2 },
  { name: "master", runs: 40, level: 20, wants: ["heart", "fang", "magnet", "dice"], deal: "buy", event: 2, rerollFor: 3, revive: true },
  { name: "thumb", runs: 40, level: 15, wants: ["merchantthumb", "clover", "boots"], deal: "buy", event: 0, rerollFor: 4 },
  { name: "debtor", runs: 40, level: 15, wants: ["debtflesh", "hollowidol", "piggy"], deal: "awaken", event: 1, rerollFor: 3 },
  { name: "walker", runs: 30, level: 12, wants: ["boots", "dash", "rabbit"], deal: "leave", event: 2, revive: true },
  { name: "oath", runs: 30, level: 8, wants: ["duelist", "stomach", "heart"], deal: "awaken", event: 0, rerollFor: 1 },
  { name: "quitter", runs: 20, level: 18, wants: ["magnet", "midas", "greed"], deal: "buy", event: 1, cashOutAt: 9 },
  { name: "frail", runs: 30, level: 3, wants: ["thorns", "iron", "tollplate"], deal: "buy", event: 2, revive: true },

  /* Every event that costs speed, taken, so the floor a stat cannot be dragged past is actually
     reached: the Chained Ghost's locket and the Rusted Spike Trap cost five each, and robbing
     the dead miner costs three more when the curse lands. All three have to be placed in the
     same run, so `require` finds seeds that place them rather than waiting for one. */
  {
    name: "slowed", runs: 30, level: 1, wants: ["boots", "dash"], deal: "leave",
    event: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0], require: [3, 6, 10],
  },

  /* A rich delver who came to the bazaar to wake something. Level twenty is the only purse
     that can pay for three pieces of business in one visit, which is the only way an awakened
     Merchant's Thumb ever buys a deal: the Thumb has to be woken by the FIRST deal, and the
     allowance is read again before the second. */
  { name: "waker", runs: 40, level: 20, wants: ["merchantthumb", "stomach", "hollowidol", "heart"], deal: "awaken", event: 0 },
  { name: "sworn", runs: 40, level: 20, wants: ["duelist", "stomach", "tooth"], deal: "awaken", event: 1 },
];

const runs = [];
let n = 0;
for (const set of SETS) {
  for (let i = 0; i < set.runs; i++) {
    let seed = (0xD3117E + n++ * 7919) >>> 0;
    let guard = 0;
    while (set.require && !places(seed, set.level, set.require) && guard++ < 20000) {
      seed = (0xD3117E + n++ * 7919) >>> 0;
    }

    runs.push(play(set.name + "/" + i, seed, set));
  }
}

mkdirSync(OUT, { recursive: true });
const json = JSON.stringify(runs);
writeFileSync(join(OUT, "delve.json"), json, "utf8");

const steps = runs.reduce((a, r) => a + r.steps.length, 0);
const kinds = {};
for (const r of runs) for (const s of r.steps) kinds[s.kind] = (kinds[s.kind] || 0) + 1;
const hows = {};
for (const r of runs) hows[r.end.how] = (hows[r.end.how] || 0) + 1;

console.log("\ndelve corpus");
console.log("  → delve.json         " + String(runs.length).padStart(4) + " runs  " +
            (Buffer.byteLength(json) / 1024).toFixed(0) + " KB");
console.log("  " + steps + " steps · " + Object.entries(kinds).map(([k, v]) => k + " " + v).join(" · "));
console.log("  " + Object.entries(hows).map(([k, v]) => k + " " + v).join(" · "));

const seen = new Set();
for (const r of runs) for (const k of Object.keys(r.eventFloors)) seen.add(r.eventFloors[k]);
console.log("  events reached: " + seen.size + " of " + api.EVENTS.length);

const deals = {};
for (const r of runs) for (const s of r.steps) if (s.kind === "shop") for (const d of s.deals) deals[d.deal.kind] = (deals[d.deal.kind] || 0) + 1;
console.log("  bazaar: " + (Object.entries(deals).map(([k, v]) => k + " " + v).join(" · ") || "never dealt"));
