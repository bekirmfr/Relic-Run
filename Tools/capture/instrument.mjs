/*
 * Trace hooks for the game's own run loop.
 *
 * balanceRuns is a complete headless 13-floor delve — event placement, the real event
 * table, the bazaar, the draft with its reroll ladder, and combat — but it only returns
 * aggregates. The corpus needs a per-floor trace.
 *
 * The obvious move is to re-walk the loop in the recorder. That is the wrong move: a
 * transcription that drifts records the RECORDER's run, and the port would then be
 * verified against a mistake. So the loop itself is instrumented instead, by injecting
 * read-only callbacks at six points. The recorder observes the game's run; it does not
 * reimplement it.
 *
 * Every hook must be free of side effects, and above all must not touch `rng` or `er`.
 * The run is a single RNG stream, and one stray draw would move everything after it.
 */

/** Injects `P.on*` callbacks into lifted balanceRuns source. */
export function instrumentBalanceRuns(src) {
  const at = (anchor, replacement) => {
    const first = src.indexOf(anchor);
    if (first < 0) throw new Error("instrument: anchor not found:\n  " + anchor);
    if (src.indexOf(anchor, first + 1) >= 0) {
      throw new Error("instrument: anchor is not unique:\n  " + anchor);
    }

    src = src.slice(0, first) + replacement + src.slice(first + anchor.length);
  };

  // 1. The run is set up: seeds drawn, events placed. Nothing has happened yet.
  at("for (let i = 0; i < nEv; i++) evAt[gaps[i]] = evIdx[i];",
     "for (let i = 0; i < nEv; i++) evAt[gaps[i]] = evIdx[i];\n" +
     "      if (P.onRun) P.onRun(run, st, evAt);");

  // 2. A between-floor event. The chosen index is what the port replays, so it is pulled
  //    into a name rather than left inline — but the hook fires AFTER the outcome has run
  //    and the stat floors have been applied, because what the port has to reproduce is
  //    the state the choice left behind, not the state it was made in.
  at("const ch = def.choices[eventChoice(def, st)];",
     "const __fc = Array.isArray(P.forceChoice) ? P.forceChoice[evAt[f]] : P.forceChoice;\n" +
     "          const __ci = __fc != null\n" +
     "            ? Math.min(__fc, def.choices.length - 1)\n" +
     "            : eventChoice(def, st);\n" +
     "          const ch = def.choices[__ci];");

  at("st.pmax = Math.max(1, st.pmax); st.php = Math.min(st.pmax, st.php);",
     "st.pmax = Math.max(1, st.pmax); st.php = Math.min(st.pmax, st.php);\n" +
     "          if (P.onEvent) P.onEvent(run, f, evAt[f], __ci, st, __evErr);");

  // The source swallows anything an outcome throws. That is right for a game — a broken event
  // should not end a run — but it is dangerous while lifting, because an identifier the lift
  // forgot makes the outcome silently do NOTHING, and the corpus would record the no-op as
  // truth. That is not hypothetical: luckRoll was missing at first, and four of the twelve
  // events quietly stopped rolling. The catch stays, so control flow is untouched; the error
  // is handed to the recorder, which refuses to write a corpus that contains one.
  at("try { ch.run(st, er, this); } catch (e) { }",
     "let __evErr = null;\n" +
     "          try { ch.run(st, er, this); } catch (e) { __evErr = e; }");

  // 3. The bazaar, reported once with whichever of its three outcomes happened. The port
  //    replays the DECISION — awaken this, buy that, or walk away — because choosing is
  //    the harness's greed, not a game rule; what is under test is the price, what a
  //    purchase does, and the offer itself, which is game code.
  at("if (f === SHOP_FLOOR) {   /* shop: greedy buys */",
     "if (f === SHOP_FLOOR) {   /* shop: greedy buys */\n" +
     '          let __deal = "none", __dealId = null;');

  // Snapshot the offer before a purchase shifts an item off it.
  at("st.awake = st.awake || {};",
     "st.awake = st.awake || {};\n" +
     "          const __offer = offer.slice();");

  at('res.sockPicks["awaken:" + bestAw.id] = (res.sockPicks["awaken:" + bestAw.id] || 0) + 1;',
     'res.sockPicks["awaken:" + bestAw.id] = (res.sockPicks["awaken:" + bestAw.id] || 0) + 1;\n' +
     '            __deal = "awaken"; __dealId = bestAw.id;');

  at("res.picks[id] = (res.picks[id] || 0) + 1;",
     "res.picks[id] = (res.picks[id] || 0) + 1;\n" +
     '            __deal = "buy"; __dealId = id;');

  // The harness's greed decides what the bazaar does and which event choice is taken, and
  // both are stand-ins for a player rather than rules. That means whole outcomes can go
  // unrecorded simply because the bot never wants them — the Chained Ghost's locket is
  // always refused, and nothing it holds is ever worth awakening twice. Steering the bot is
  // how those get reached; it changes no rule, only who is playing.
  at("let bestAw = null, bestPS = 5;",
     "let bestAw = null, bestPS = 5;\n" +
     "          if (P.forceAwaken) {\n" +
     "            const __t = cand.filter(c => c.id === P.forceAwaken)[0];\n" +
     "            if (__t) { bestAw = __t; bestPS = Infinity; }\n" +
     "          }");

  at("continue;   /* no combat on shop floor */",
     "if (P.onShop) P.onShop(run, f, __offer, __deal, __dealId, st);\n" +
     "          continue;   /* no combat on shop floor */");

  // 4. A relic was drafted, after any rerolls.
  at("applyPickup(st, pick); res.picks[pick] = (res.picks[pick] || 0) + 1;",
     "applyPickup(st, pick); res.picks[pick] = (res.picks[pick] || 0) + 1;\n" +
     "        if (P.onPick) P.onPick(run, f, pick, offer, st);");

  // 5. The pack. balanceRuns prices its foes through the Balance Lab's editable formula
  //    table, and the game does not — it calls packFor with a dungeon and no curve. The two
  //    paths round differently, so recording the Lab's packs would gate the port against
  //    numbers the game never produces. Left alone, floor 10 alone loots ten gold more.
  //
  //    So the recorder chooses. Passing {} takes the game's own default path; the Lab's
  //    curve is still what balanceRuns computes for its own report, and is untouched.
  at("const pk = packFor(f, rng, curve);",
     "const pk = packFor(f, rng, P.packConfig || curve);");

  // 6. The floor's fight resolved.
  at("const ev = this.simulateFloor(st, pk, rng);",
     "const ev = this.simulateFloor(st, pk, rng);\n" +
     "        if (P.onFight) P.onFight(run, f, pk, ev, st);");

  // 6. The run ended, win or death.
  at("if (!dead) { res.wins++; res.winHp.push(st.php); res.winGold.push(st.gold); }",
     "if (P.onEnd) P.onEnd(run, st, dead, deadAt, deadTo);\n" +
     "      if (!dead) { res.wins++; res.winHp.push(st.php); res.winGold.push(st.gold); }");

  return src;
}
