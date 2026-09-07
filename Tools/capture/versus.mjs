/*
 * Versus corpus — the Phase 6 gate.
 *
 *   node Tools/capture/versus.mjs
 *
 * Unlike the delve, versus has no headless harness in the source: a match is driven by the UI.
 * So the UI is what gets driven. startVersus, pickRelic, fight, playNext, descend,
 * proceedDescend and the bazaar are all lifted, the presentation is stubbed, and the deferred
 * callbacks the animation would have run are drained in place — because some of them DRAW.
 *
 * THE REDUCED-MOTION PATH
 *
 * The source takes a different route through proceedDescend and playNext when
 * prefers-reduced-motion is set, skipping a pack roll and the merchant's greeting. Those are
 * seeded draws, so the same match plays out differently depending on an accessibility setting —
 * seed 424242 dies on round 8 with animation and clears the lobby without it. The port will not
 * inherit that, so the recording picks the branch where presentation does not touch the
 * gameplay stream, and this is where that choice is made.
 *
 * WHAT IS A DECISION AND WHAT IS A RULE
 *
 * Only the human's choices are recorded for the port to replay: which relic is drafted, and
 * what the bazaar is asked for. Everything else has to come out of the port.
 *
 * The rival bots' drafting is NOT a decision. It is the shipped AI — how a rival builds its
 * deck between rounds — and it draws from the seeded stream, so it belongs in Core along with
 * the rest of the lobby's rules. That is the opposite call from balanceRuns' pickScore, which
 * stands in for a human and is not ported.
 */

import { writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { buildEngine } from "./engine.mjs";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "corpus");

const api = buildEngine({ countDraws: true });
api.setReducedMotion(true);

/** A deterministic stand-in for a player: body first, then whatever is on offer. */
const WANTS = ["heart", "iron", "tooth", "whetstone", "clover", "boots", "dice", "adrenaline"];

function choose(offer) {
  for (const want of WANTS) {
    if (offer.indexOf(want) >= 0) return want;
  }

  return offer[0];
}

const side = (b) => ({
  name: b.name,
  lvl: b.lvl | 0,
  lives: b.lives | 0,
  hall: b.hall | 0,
  gold: b.gold | 0,
  relics: b.relics.slice(),
  base: {
    hp: b.base.hp | 0, atk: b.base.atk | 0, def: b.base.def | 0,
    spd: b.base.spd | 0, lck: b.base.lck | 0,
  },
});

const hero = (G) => ({
  php: G.php | 0, pmax: G.pmax | 0, gold: G.gold | 0, kills: G.kills | 0,
  lives: G.vsLives | 0,
  atkB: G.atkB | 0, defB: G.defB | 0, spdB: G.spdB | 0, luckB: G.luckB | 0,
  adrenaline: G.adrenaline | 0, midasBonus: G.midasBonus | 0,
  anvilB: G._anvilB | 0, strikeTot: G._strikeTot | 0,
  items: G.items.slice(),
  awake: Object.keys(G.awake || {}).filter((k) => G.awake[k]).map(Number).sort((a, b) => a - b),
  sockets: Object.assign({}, G.sockets || {}),
});

function match(id, seed, wants) {
  const engine = new api.Engine();
  engine.state = {};
  api.seedLobby(seed);
  engine.startVersus();

  const G = engine.G;

  // Building the lobby spends around 250 draws, and most of them go on the rivals' LOOKS: ten
  // wardrobe slots and eleven colour families each, plus an accent and a motto. The port has
  // no wardrobe, so it cannot spend them — and it does not need to. The roster the source
  // produced is recorded verbatim, the way packs are, and the number of draws the setup
  // consumed goes with it, so a match can resume the stream exactly where setup left it.
  // Roster GENERATION is then a separate problem, and lands with the wardrobe.
  const setupDraws = engine._draws;
  const record = {
    id,
    seed,
    start: { hero: hero(G), hall: G.vsHall | 0, roster: G.vsRoster.map(side) },
    rounds: [],
    bazaar: null,
    end: null,
  };

  let picks = [];
  let guard = 0;

  while (engine.state.screen !== "over" && guard++ < 400) {
    const stage = engine.state.stage;

    if (stage === "draft") {
      const offer = engine.state.offer.slice();
      const pick = choose(offer);
      picks.push({ offer, pick });
      engine.pickRelic(pick);
      engine.drain();
      continue;
    }

    if (stage === "shop") {
      // One deal a visit, unless a Merchant's Thumb is awake. What the shopper wants is a
      // decision like any other, so it is recorded — and it varies by match, or awakening
      // would be the only path the corpus ever walks.
      const offer = (G.shopOffer || []).slice();
      const awakenable = (G.shopAwake || []).map((e) => ({ id: e.id, slot: e.i }));
      let action = { kind: "leave" };

      const canAwaken = awakenable.length > 0 && G.gold >= api.SHOP_UP;
      const canBuy = offer.length > 0 && G.gold >= api.SHOP_BUY;

      if (wants === "awaken" && canAwaken) {
        action = { kind: "awaken", relic: awakenable[0].id, slot: awakenable[0].slot };
        engine.shopAwaken(awakenable[0].slot);
      } else if (wants !== "leave" && canBuy) {
        action = { kind: "buy", relic: offer[0] };
        engine.shopBuy(offer[0]);
      } else if (wants === "awaken" && canBuy) {
        action = { kind: "buy", relic: offer[0] };
        engine.shopBuy(offer[0]);
      } else {
        engine.shopLeave();
      }

      record.bazaar = { round: G.floor | 0, offer, awakenable, action, after: hero(G) };
      engine.drain();
      continue;
    }

    if (stage === "decide") {
      record.rounds.push({
        round: G.floor | 0,
        picks,
        foe: G.vsFoe | 0,
        foeName: G.vsRoster[G.vsFoe] ? G.vsRoster[G.vsFoe].name : null,
        won: !!G.vsLastWon,
        hero: hero(G),
        roster: G.vsRoster.map(side),
      });

      picks = [];
      engine.descend();
      engine.drain();
      continue;
    }

    // Any other stage is presentation the drain should already have carried past.
    engine.descend();
    engine.drain();
  }

  record.setupDraws = setupDraws;
  record.end = {
    how: G.how || "unresolved",
    round: G.floor | 0,
    score: G.score | 0,
    banked: G.banked | 0,
    stars: G.stars | 0,
    hero: hero(G),
    roster: G.vsRoster.map(side),
  };

  return record;
}

const matches = [];
for (let i = 0; i < 120; i++) {
  // Cycle what the shopper wants, so buying and walking away are recorded too.
  const wants = ["awaken", "buy", "leave"][i % 3];
  matches.push(match("versus/" + i, (0x5E1F00D + i * 7919) >>> 0, wants));
}

mkdirSync(OUT, { recursive: true });
const json = JSON.stringify(matches);
writeFileSync(join(OUT, "versus.json"), json, "utf8");

const rounds = matches.reduce((n, m) => n + m.rounds.length, 0);
const cleared = matches.filter((m) => m.end.how === "cleared").length;
const shopped = matches.filter((m) => m.bazaar).length;

console.log("\nversus corpus");
console.log(`  → versus.json        ${String(matches.length).padStart(4)} matches  ` +
            `${(Buffer.byteLength(json) / 1024).toFixed(0)} KB`);
console.log(`  ${rounds} rounds · ${cleared} cleared · ${shopped} reached the bazaar`);

const deals = {};
for (const m of matches) if (m.bazaar) deals[m.bazaar.action.kind] = (deals[m.bazaar.action.kind] || 0) + 1;
console.log("  bazaar: " + (Object.entries(deals).map(([k, n]) => k + " " + n).join(" · ") || "never reached"));
