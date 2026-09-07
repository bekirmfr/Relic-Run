  balanceRuns(N, P) {
    P = Object.assign({ baseHp: 100, baseAtk: 5, baseDef: 0, baseSpd: 25, baseLck: 10, breath: 5, seed: 0xBA1A, draftChoices: 3, shopBuy: SHOP_BUY, shopUp: SHOP_UP, formulas: {}, startKit: [], benchAbilities: false, defModel: "", dlvl: 1, baseGold: 0 }, P || {});
    /* enemy formulas: guard baseline + role multipliers. Invalid ones fall back to live defaults. */
    const EF = {};
    const L = Math.max(1, P.dlvl | 0), LM = Math.pow(1.1, L - 1);   /* dungeon level: exposed as "l" in formulas; default multiplier when a formula ignores l */
    for (const st of ENEMY_STAT_FORMULAS.concat(ENEMY_ROLE_FORMULAS)) {
      const raw = compileFormula(P.formulas[st.id]) || compileFormula(st.def);
      const usesL = /\bl\b/i.test(String(P.formulas[st.id] || ""));
      const scaled = ["ehp", "eatk"].indexOf(st.id) >= 0 && !usesL;   /* HP/ATK get the dungeon multiplier unless the formula handles l itself */
      EF[st.id] = (f, s2, g) => raw(f, s2, g, L) * (scaled ? LM : 1);
    }
    const curve = { hpF: EF.ehp, atkF: EF.eatk, defF: EF.edef, spdF: EF.espd, lckF: EF.elck, goldF: EF.egold };
    for (const r of ["elite", "boss", "king"]) for (const t of ["hp", "atk", "def", "spd", "lck"]) curve[r + t + "F"] = EF[r + t];

    const res = { runs: N, wins: 0, deaths: {}, deathFloors: [], winHp: [], winGold: [], picks: {}, sockPicks: {}, floorHp: {}, dmgTaken: {}, fightLen: {} };
    /* advanced relic consideration: phase- and build-aware scoring.
       Layers: base value + archetype synergy + game-phase weight + HP-state urgency + diminishing stacks. */
    const pickScore = (id, st) => {
      const low = st.php / st.pmax, f = st.floor, late = f / MAX_FLOOR;   /* 0..1 game phase */
      const n = i => count(st.items, i);
      const luckStat = 10 + (st.luckB || 0);
      const luckInvest = n("clover") + n("rabbit") + (n("dice") ? 1 : 0) + (n("cutpurse") ? 1 : 0);
      const sustain = n("tooth") + n("alchemist") + n("heart");           /* healing throughput proxy */
      const spdRel = n("boots") + n("dash");
      let sc;
      switch (id) {
        /* survival core */
        case "heart": sc = 7 + (1 - low) * 10 + late * 2; break;                       /* HP matters more as hits grow */
        case "iron": sc = 6 + n("thorns") * 2 + late * 2 + (low < 0.5 ? 2 : 0); break; /* flat DEF scales with hit count */
        case "tooth": sc = 6 + spdRel * 2 + n("altar") * 2; break;                     /* per-strike heal loves speed */
        case "alchemist": sc = 4 + n("magnet") * 2 + n("altar") * 3 + (n("cutpurse") ? 2 : 0) + n("greed") * 2; break;
        case "altar": sc = 3 + Math.min(6, sustain * 2); break;                        /* needs heal throughput to matter */
        case "thorns": sc = 5 + late * 2 + n("iron"); break;                           /* more enemy hits late = more thorns */
        /* offense */
        case "whetstone": sc = 7 - late * 2 + (n("whetstone") ? -1 : 0); break;        /* early ATK compounds across the run */
        case "adrenaline": sc = 6 - (1 - low) * 2; break;                              /* permanent ATK accrual, unique */
        case "berserk": sc = 4 + (low < 0.55 ? 3 : 0) + late; break;                   /* strongest when living dangerously */
        case "dice": sc = 5 + luckStat * 0.15 + luckInvest; break;                     /* crit EV rides the luck stat */
        /* pace */
        case "boots": sc = 6 + n("tooth") * 2 + n("dice"); break;                      /* more strikes = more procs */
        case "dash": sc = 5 + late * 2; break;                                         /* free first hit vs fat late packs */
        /* luck engine */
        case "clover": sc = 8 + n("rabbit") * 2 + (n("dice") ? 2 : 0) + (n("cutpurse") ? 2 : 0); break;   /* the only door to evasion */
        case "rabbit": sc = 3 + n("clover") * 3 + (n("dice") ? 2 : 0) + (n("cutpurse") ? 1 : 0); break;
        case "cutpurse": sc = 3 + luckStat * 0.1 + n("alchemist") * 2 + (f < SHOP_FLOOR ? 2 : 0); break; /* amplifier now, not unlocker */
        /* economy — worth more while the shop is still ahead, dead weight after */
        case "magnet": sc = (f < SHOP_FLOOR ? 5 : 2) + n("alchemist") * 2; break;
        case "fang": sc = (f < SHOP_FLOOR ? 5 : 2) + n("alchemist"); break;
        case "midas": sc = 3 + (st.gold || 0) / 50 + (f < SHOP_FLOOR ? 1 : 0); break;
        case "greed": sc = (f < SHOP_FLOOR ? 3 : 1) + n("alchemist") * 2 - (low < 0.5 ? 2 : 0); break;  /* +1 hurt is real */
        case "piggy": sc = f < SHOP_FLOOR ? 3 : 0.5; break;                            /* 25% cash-out only helps pre-shop */
        default: sc = 2;
      }
      /* build-fit (set tiers + chain graph) and diminishing stacks */
      sc += relicSynergy(id, st.items);
      sc -= n(id) * 1.5;
      return sc;
    };
    /* survival-greedy event policy: mirrors how a cautious player reads each choice */
    const eventChoice = (def, st) => {
      const low = st.php / st.pmax;
      let best = -1, bestScore = -1e9;
      for (let i = 0; i < def.choices.length; i++) {
        const ch = def.choices[i];
        if (ch.cost && st.gold < ch.cost) continue;
        const h = (ch.hint || "") + "|" + ch.label;
        let sc = 0;
        if (/\+\d+ HP/.test(h)) sc += low < 0.8 ? 8 : 1;                   /* heals matter when hurt */
        if (/\+2 ATK/.test(h)) sc += 6;
        if (/\+1 DEF/.test(h)) sc += 5;
        if (/COSTS \d+ GOLD/.test(h)) sc += 2;                              /* safe paid outcomes */
        if (/RISK|50\/50|Eat it|Break the wall|Open it|Fight!|blood/i.test(h)) sc += low > 0.6 ? 3 : -6; /* gamble only when healthy */
        if (/-\d+ HP/.test(h)) sc -= low < 0.5 ? 6 : 2;
        if (/-\d+ SPD/.test(h)) sc -= 3;
        if (/Walk|Leave|Decline|Refuse|Don't|Keep moving|Pray/i.test(ch.label)) sc += 1;   /* the safe out */
        if (sc > bestScore) { bestScore = sc; best = i; }
      }
      return best < 0 ? def.choices.length - 1 : best;
    };
    for (let run = 0; run < N; run++) {
      const rng = mulberry32(P.seed + run * 977);
      const st = freshRunState({ php: P.baseHp, pmax: P.baseHp, gold: P.baseGold || 0, items: (P.startKit || []).slice(), rng,
        baseAtk: P.baseAtk, baseDef: P.baseDef, baseSpd: P.baseSpd, baseLck: P.baseLck, benchAbilities: !!P.benchAbilities, defModel: P.defModel || "" });
      /* event placement mirrors startRun: 2-4 events in gaps between floors 2-11 */
      const er = mulberry32((P.seed + run * 977) ^ 0x9E3779B9);
      const shuffle = a => { for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(er() * (i + 1)); const t2 = a[i]; a[i] = a[j]; a[j] = t2; } return a; };
      const gaps = shuffle([2, 3, 4, 5, 6, 7, 8, 9, 10, 11]), evIdx = shuffle(EVENTS.map((_, i) => i));
      const nEv = 2 + Math.floor(er() * 3), evAt = {};
      for (let i = 0; i < nEv; i++) evAt[gaps[i]] = evIdx[i];
      let dead = false, deadAt = 0, deadTo = "";
      for (let f = 1; f <= MAX_FLOOR && !dead; f++) {
        st.floor = f;
        if (f > 1) st.php = Math.min(st.pmax, st.php + breathHeal(P.breath, f) * (count(st.items, "stomach") ? 2 : 1));
        if (evAt[f] != null) {              /* between-floor event, real table + greedy policy */
          const def = EVENTS[evAt[f]];
          const ch = def.choices[eventChoice(def, st)];
          try { ch.run(st, er, this); } catch (e) { }
          st.spdB = Math.max(EV_MIN_SPD, st.spdB); st.defB = Math.max(EV_MIN_DEF, st.defB);
          st.pmax = Math.max(1, st.pmax); st.php = Math.min(st.pmax, st.php);
          res.evPicks = res.evPicks || {}; res.evPicks[def.title] = (res.evPicks[def.title] || 0) + 1;
          if (st.php <= 0) { dead = true; deadAt = f; deadTo = "event"; res.deathFloors.push(f); res.deaths[f] = (res.deaths[f] || 0) + 1; break; }
        }
        if (st.pending && st.pending.floor === f) { st.gold += st.pending.gold; st.pending = null; }   /* breath */
        if (f === SHOP_FLOOR) {   /* shop: greedy buys */
          const offer = [];
          const avail = POOL.filter(id => (ITEMS[id].stack || st.items.indexOf(id) < 0) && liveFor(id, st.items));
          while (offer.length < Math.min(5, avail.length)) { const id = weightedRelic(avail, st.items, rng); if (offer.indexOf(id) < 0) offer.push(id); }
          /* ONE deal per visit: awaken the punchiest stackable owned relic, or buy the best offer */
          const punchOf = rid => ({ fortunesedge: 2.5, quickpulse: 2.5, butchertithe: 2, giltrot: 2, tollplate: 2, embercask: 2, coinsinger: 2, hexthread: 2.5, alchemist: 2, altar: 3, tooth: 2, heart: 3, fang: 4, magnet: 3, iron: 2.5, whetstone: 2.5, adrenaline: 3, thorns: 3, rabbit: 1.5, cutpurse: 2, dice: 3, berserk: 2.5, boots: 2, dash: 2, midas: 2, greed: 2, piggy: 1, clover: 1.5 }[rid] || 1.5);
          st.awake = st.awake || {};
          const cand = st.items.map((id, i) => ({ id, i })).filter(e => ITEMS[e.id] && ITEMS[e.id].stack && !(st.awake[e.id] > 0));
          let bestAw = null, bestPS = 5;
          for (const t of cand) { const v = punchOf(t.id) * 3; if (v > bestPS) { bestPS = v; bestAw = t; } }
          const bestBuy = offer.length ? Math.max(...offer.map(o => pickScore(o, st))) : -1;
          if (st.gold >= P.shopUp && bestAw && (st.gold < P.shopBuy || bestPS >= bestBuy)) {
            st.awake[bestAw.id] = (st.awake[bestAw.id] || 0) + 1; st.gold -= P.shopUp;
            if (count(st.items, "debtflesh")) st.php = Math.min(st.pmax, st.php + (st.awake && st.awake["debtflesh"] ? 20 : 10)); res.sockPicks["awaken:" + bestAw.id] = (res.sockPicks["awaken:" + bestAw.id] || 0) + 1;
          } else if (st.gold >= P.shopBuy && offer.length) {
            offer.sort((a, b) => pickScore(b, st) - pickScore(a, st));
            const id = offer.shift();
            st.gold -= P.shopBuy; applyPickup(st, id);
            if (count(st.items, "debtflesh")) st.php = Math.min(st.pmax, st.php + (st.awake && st.awake["debtflesh"] ? 20 : 10));
            res.picks[id] = (res.picks[id] || 0) + 1;
          }
          continue;   /* no combat on shop floor */
        }
        /* draft: greedy, with rerolls — same price ladder as the game (10·2^n), but never
           dipping into the shop fund it saves before floor 7 */
        const rollOffer = () => {
          const avail = POOL.filter(id => (ITEMS[id].stack || st.items.indexOf(id) < 0) && liveFor(id, st.items));
          const offer = [];
          while (offer.length < Math.min(P.draftChoices, avail.length)) { const id = weightedRelic(avail, st.items, rng); if (offer.indexOf(id) < 0) offer.push(id); }
          offer.sort((a, b) => pickScore(b, st) - pickScore(a, st));
          return offer;
        };
        let offer = rollOffer();
        st.rerolls = st.rerolls || 0;
        const reserve = f < SHOP_FLOOR ? P.shopUp : 0;   /* save for the bazaar's best deal */
        while (pickScore(offer[0], st) < 5) {            /* neither option is good */
          const price = Math.round(10 * Math.pow(2, st.rerolls) * (count(st.items, "merchantthumb") ? 0.8 : 1));
          if (st.gold - price < reserve) break;
          st.gold -= price; st.rerolls++;
          if (count(st.items, "debtflesh")) st.php = Math.min(st.pmax, st.php + (st.awake && st.awake["debtflesh"] ? 20 : 10));
          res.rerolls = (res.rerolls || 0) + 1;
          offer = rollOffer();
        }
        const pick = offer[0];
        applyPickup(st, pick); res.picks[pick] = (res.picks[pick] || 0) + 1;
        /* fight */
        const hpBefore = st.php;
        st.furyB = 0;

        const pk = packFor(f, rng, curve);
        const ev = this.simulateFloor(st, pk, rng);
        res.foes = (res.foes || 0) + pk.length;
        res.fightLen[f] = (res.fightLen[f] || 0) + ev.length / N;
        const nStr = ev.filter(e => e.t === "edmg" && e.src === "you").length;
        res.strikes = (res.strikes || 0) + nStr;
        res.floorStrikes = res.floorStrikes || {}; res.floorStrikes[f] = (res.floorStrikes[f] || 0) + nStr;
        res.floorVisits = res.floorVisits || {}; res.floorVisits[f] = (res.floorVisits[f] || 0) + 1;
        res.dmgTaken[f] = (res.dmgTaken[f] || 0) + Math.max(0, hpBefore - st.php) / N;
        res.floorHp[f] = (res.floorHp[f] || 0) + Math.max(0, st.php) / N;
        if (st.php <= 0) {
          dead = true; deadAt = f;
          const last = ev.filter(e => e.t === "pdmg").pop();
          deadTo = last ? "enemy#" + last.eidx : "?";
        }
      }
      if (!dead) { res.wins++; res.winHp.push(st.php); res.winGold.push(st.gold); }
      else { res.deathFloors.push(deadAt); res.deaths[deadAt] = (res.deaths[deadAt] || 0) + 1; }
      /* end-of-run hero sheet (win or death), averaged for the report */
      const es = res.endStats = res.endStats || { n: 0, maxhp: 0, atk: 0, def: 0, spd: 0, lck: 0, items: 0 };
      es.n++; es.maxhp += st.pmax; const hcE = heroCtxOf(st);
      es.atk += heroStatOf(hcE, "atk"); es.def += heroStatOf(hcE, "def");
      es.spd += heroStatOf(hcE, "spd"); es.lck += heroStatOf(hcE, "lck"); es.items += st.items.length;
    }
    return res;
  }

  /* ==== DUEL LAB: headless mirror duels for stat equivalence ==== */
  duelRuns(N, seed, modA, modB, hp, bench) {
    const mk = (rng, mod) => { const b = { hp, atk: 5, def: 0, spd: 25, lck: 10 };
      if (mod) b[mod.stat] += mod.amt;
      return { name: "d", base: b, php: b.hp, pmax: b.hp, gold: 0, items: [], sockets: {}, awake: {}, adrenaline: 0, midasBonus: 0, atkB: 0, defB: 0, spdB: 0, luckB: 0, kills: 0, drop: 0, benchAbilities: !!bench }; };
    let a = 0, b = 0, d = 0;
    for (let i = 0; i < N; i++) {   /* structural de-bias: half the duels swap which side carries which mod */
      const rng = mulberry32((seed + i * 7919) >>> 0), swap = i % 2 === 1;
      const A = mk(rng, swap ? modB : modA), B = mk(rng, swap ? modA : modB);
      A.defModel = this.state.balDefFlat ? "flat" : "pct"; B.defModel = A.defModel;
      this.simulateDuel(A, B, rng);
      const aWon = B.php <= 0 && A.php > 0, bWon = A.php <= 0 && B.php > 0;
      if (aWon) { if (swap) b++; else a++; } else if (bWon) { if (swap) a++; else b++; } else d++;
    }
    return { a, b, d, n: N };
  }
  /* bisect the +X of stat that makes B even against A's +10 ATK */
  duelEquiv(stat, N, seed, hp, bench, onStep) {
    const target = 0.5, A = { stat: "atk", amt: 10 };
    let lo = 0, hi = stat === "hp" ? 400 : 120, mid = 0;
    for (let it = 0; it < 7; it++) {
      mid = (lo + hi) / 2;
      const r = this.duelRuns(N, seed + it * 131, A, { stat, amt: mid }, hp, bench);
      const winB = r.b / (r.a + r.b || 1);
      if (onStep) onStep(it, mid, winB);
      if (winB > target) hi = mid; else lo = mid;
    }
    return Math.round((lo + hi) / 2 * 10) / 10;
  }
  simulateFloor(st, pack, rng) {
    const ev = []; let cur = null, ehp = 0;
    let tkNow = 0;
    const snap = e => ev.push(Object.assign({}, e, { ridx: e.ridx != null ? e.ridx : (fireIdx == null ? undefined : fireIdx), tk: tkNow, php: st.php, ehp: Math.max(0, ehp), gold: st.gold, emax: cur ? (cur.max || cur.hp) : 1, eatk: cur ? cur.atk : 0, erank: cur ? cur.rank : "guard", earm: cur ? (cur.armor || 0) : 0, espd: cur ? (cur.spd || 25) : 25, elck: cur ? (cur.lck || 10) : 10, evar: cur ? (cur.variant || 0) : 0, erel: cur && cur.relics ? cur.relics : undefined, padr: st.adrenaline, pl: (typeof dynMods === "function" ? dynMods() : []), pcnt: { a: (typeof strikeN !== "undefined" ? strikeN : 0), h: (typeof painN !== "undefined" ? painN : 0), g: (typeof goldN !== "undefined" ? goldN : 0), st: (typeof stoneCnt !== "undefined" ? stoneCnt : 0), f: (typeof fightStrikes !== "undefined" ? fightStrikes : 0), q: (typeof qN !== "undefined" ? qN : 0), m: (typeof momN !== "undefined" ? momN : 0), r: (typeof rabbitN !== "undefined" ? rabbitN : 0), at: (st && st._strikeTot) || 0, sb: (typeof sentB !== "undefined" ? sentB : 0) }, eidx: e.eidx != null ? e.eidx : (cur ? cur.idx : 0) }));
    const sockets = st.sockets || {};
    const newCtx = () => ({ v: {} });      /* one chain context per genuine root event */
    const relicScale = (ctx, id) => { const n = (ctx.v[id] = (ctx.v[id] || 0) + 1); return Math.pow(nI("fusecoil") ? 0.75 : (SB("CHAIN") >= 7 ? 0.75 : SB("CHAIN") >= 3 ? 0.6 : 0.5), n - 1); };   /* CHAIN set (3) softens decay; Fuse Coil sharpens it */
    const CARRY = st._carry || {};   /* revive: floor-scoped state continues exactly where the hero fell */
    let furyB = CARRY.furyB || 0, stoneB = CARRY.stoneB || 0, strikeN = CARRY.strikeN || 0, painN = CARRY.painN || 0, goldN = CARRY.goldN || 0;   /* floor-persistent counters: high-frequency socketed triggers fire every 3rd occurrence */
    var galeB = CARRY.galeB || 0, luckB = CARRY.luckB || 0;   /* var: referenced by nativeEffect before the let would initialize */   /* floor-scoped stat boosts from emitters */
    var stoneCnt = CARRY.stoneCnt || 0;   /* Stone Emitter activation counter (fires every 3rd) */
    /* ---- expansion set: helpers, set bonuses, per-fight state ---- */
    const AW = st.awake || {};
    const nI = id => count(st.items, id) + ((AW[id] || 0) > 0 ? (count(st.items, "tinkerloop") ? 2 : 1) : 0);   /* Awakener's Loop: awakened count as three */
    const KC = {}; for (const it of st.items) { const kk = ITEMS[it] && ITEMS[it].kind; if (kk) KC[kk] = (KC[kk] || 0) + 1; }
    const hIdol = nI("hollowidol");
    const SB = kk => (KC[kk] || 0) + hIdol;                /* Hollow Idol: every set count +1 */
    const CAPn = () => CHAIN_CAP + (SB("CHAIN") >= 5 ? 1 : 0) - (nI("fusecoil") ? 10 : 0);
    /* the dynamic ledger: every in-fight boost is a labeled entry; reads go through heroStatOf */
    const dynMods = () => { const m = [];
      if (furyB) m.push({ stat: "atk", src: "Fury (this floor)", amt: furyB });
      if (qB) m.push({ stat: "atk", src: "Quenched Blade (this floor)", amt: qB });
      if (hdB) m.push({ stat: "atk", src: "Headsman's Notch (this floor)", amt: hdB });
      if (stoneB) m.push({ stat: "def", src: "Stone / Iron emitters (this fight)", amt: stoneB });
      if (sentB) m.push({ stat: "def", src: "Sentinel Bell (this floor)", amt: sentB });
      if (titheDef) m.push({ stat: "def", src: "Shell of the Tithe (this floor)", amt: titheDef });
      if (galeB) m.push({ stat: "spd", src: "Gale emitters (this floor)", amt: galeB });
      if (momB) m.push({ stat: "spd", src: "Momentum Bead (this fight)", amt: momB });
      if (luckB) m.push({ stat: "lck", src: "Rabbit / Gambler (this floor)", amt: luckB });
      return m; };
    const ctxOf = () => ({ items: st.items, awake: AW, mode: st.mode,
      base: { atk: st.baseAtk, def: st.baseDef, spd: st.baseSpd, lck: st.baseLck },
      run: { adrenaline: st.adrenaline || 0, midasBonus: st.midasBonus || 0, atkB: st.atkB || 0, defB: st.defB || 0, spdB: st.spdB || 0, luckB: st.luckB || 0 },
      php: st.php, pmax: st.pmax, gold: st.gold || 0, floor: st.floor || 0,
      foeRank: cur ? cur.rank : null, debtLeft: st._debtLeft || 0, glassBroken: st._glassBroken, mods: dynMods() });
    const heroLck = () => heroStatOf(ctxOf(), "lck");
    var f7used = CARRY.f7used || false, exeUsed = CARRY.exeUsed || false, gamblerUsed = CARRY.gamblerUsed || false, hdB = CARRY.hdB || 0, sparkStored = CARRY.sparkStored || false;
    var trigFired = {};                     /* per-beat guard for fresh chain roots */
    var fireIdx = null;                     /* the exact copy whose socket is firing (for per-copy fx) — declared before the first snap() */
    var titheDef = CARRY.titheDef || 0;
    if (!CARRY.titheDef && nI("titheshell") && st.gold >= 20) { st.gold -= 20;
      if (nI("debtflesh")) { const dh = Math.min(AW["debtflesh"] ? 20 : 10, st.pmax - st.php); if (dh > 0) { st.php += dh; snap({ t: "heal", amt: dh, src: this.itemName("debtflesh"), depth: 1, rid: "debtflesh" }); } }
      titheDef = 4 * nI("titheshell"); snap({ t: "first", src: this.itemName("titheshell") + " collects 20g \u2014 +" + titheDef + " DEF", depth: 0, rid: "titheshell" }); }
    else if (!CARRY.titheDef && nI("titheshell") && AW["titheshell"]) { titheDef = 4 * nI("titheshell"); snap({ t: "first", src: this.itemName("titheshell") + " waives the toll \u2014 +" + titheDef + " DEF", depth: 0, rid: "titheshell" }); }
    if (nI("titheshell")) st._tithePaid = titheDef > 0;   /* unpaid tithe = dormant shell this floor */
    if (SB("FLESH") >= 3 && !st._flesh3) { st._flesh3 = true; st.pmax += 3; st.php = Math.min(st.pmax, st.php + 3); snap({ t: "first", src: "Flesh set \u2014 +3 max HP", depth: 0 }); }
    var fEdge = false, hideLearn = 0, stumble = 0, martyrN = 0, bootsUsed = -1;
    var blockedFight = false, hitTakenFight = false, hitN = 0, ironGl = false, sentB = CARRY.sentB || 0, qB = CARRY.qB || 0, qN = CARRY.qN || 0, momB = 0, momN = 0, echoUsed = 0, catUsed = false, fightStrikes = 0, hgBoost = false;
    /* a relic ACTIVATES when its native trigger or socketed trigger fires; on activation
       its socketed emitter (if any) fires into the bus at depth+1. */
    const fireEmitAt = (idx, depth, ctx, sc) => {   /* ONE copy's socketed emitter */
      const id = st.items[idx];
      const em = sockets[idx] && EMITTERS[sockets[idx]] ? sockets[idx] : null;
      if (!em) return;
      fireIdx = idx;
      ctx = ctx || newCtx();
      let a = Math.round(EMITTERS[em].amt * (sc == null ? 1 : sc)), nm = this.itemName(id);
      if (a > 0) a += (nI("tinkerloop") ? 1 : 0) + (nI("fusecoil") ? 1 : 0);   /* Tinker's Loop / Fuse Coil: emits +1 */
      if (em === "e_def") { stoneCnt++; a = Math.max(a, EMITTERS[em].amt); }   /* Stone Emitter: +1 DEF on every activation (%DEF model absorbs the stack) */
      if (a <= 0) { snap({ t: "fizzle", depth: depth + 1 }); return; }
      if (em === "e_dmg") { if (ehp > 0) dealDmg(a, nm, depth + 1, id, ctx); }
      else if (em === "e_heal") heal(a, nm, depth + 1, id, ctx);
      else if (em === "e_gold") gainGold(a, nm, depth + 1, id, ctx);
      else if (em === "e_atk") { furyB += a; st.furyB = furyB; snap({ t: "first", src: nm + " +" + a + " ATK", depth: depth + 1, rid: id }); }
      else if (em === "e_def") { stoneB += a; snap({ t: "first", src: nm + " +" + a + " DEF", depth: depth + 1, rid: id }); }
      else if (em === "e_spd") { galeB += a; snap({ t: "first", src: nm + " +" + a + " SPD", depth: depth + 1, rid: id }); if (nI("stormlattice") && ehp > 0) dealDmg(nI("stormlattice"), this.itemName("stormlattice"), depth + 2, "stormlattice", ctx); }
      else if (em === "e_luck") { luckB += a; snap({ t: "first", src: nm + " +" + a + " LUCK", depth: depth + 1, rid: id }); }
      fireIdx = null;
    };
    const fireEmit = (id, depth, ctx, sc) => {      /* native activation: every socketed copy's emitter fires */
      for (let idx = 0; idx < st.items.length; idx++) if (st.items[idx] === id) fireEmitAt(idx, depth, ctx, sc);
    };
    /* a bus event fires every relic whose native or socketed trigger matches */
    /* provenance rule: an event caused by a RELIC (srcRid set) chains — the triggered relic
       joins at depth+1 and decays with it. A genuine event (strike loot, enemy hit, dodge,
       fight start; no srcRid) is a fresh chain root at depth 1, once per relic per beat. */
    const fireTrig = (tid, depth, exclude, srcRid, ctx) => {
      if (depth > CAPn()) return;
      for (let idx = 0; idx < st.items.length; idx++) {   /* per copy: each socketed copy is its own relic */
        const id = st.items[idx];
        if ((exclude && exclude.indexOf(id) >= 0) || sockets[idx] !== tid) continue;
        if (srcRid) {                       /* relic-caused: join the chain, decay by revisit */
          if (srcRid === id) continue;
          const sc = relicScale(ctx || (ctx = newCtx()), id);
          fireIdx = idx; nativeEffect(id, depth + 1, ctx, sc, true); fireEmitAt(idx, depth + 1, ctx, sc); fireIdx = null;
        } else {                            /* genuine: fresh chain root, once per copy per beat */
          if (trigFired[idx]) continue; trigFired[idx] = true;
          const c2 = newCtx(), sc = relicScale(c2, id);
          fireIdx = idx; nativeEffect(id, 0, c2, sc, true); fireEmitAt(idx, 0, c2, sc); fireIdx = null;
        }
      }
    };
    /* native one-shot effects, re-fireable by socketed triggers (values match the relic) */
    const nativeEffect = (id, depth, ctx, sc, viaTrig) => {
      const nm = this.itemName(id) + (viaTrig ? " \u26a1" : "");
      if (depth > CAPn()) return;
      ctx = ctx || newCtx();
      const k = viaTrig ? 1 : Math.max(1, count(st.items, id));   /* socketed re-fire: only the upgraded copy; native: every copy joins */
      const S = a => Math.round(a * k * (sc == null ? 1 : sc));
      if (id === "alchemist") heal(S(1), nm, depth + 1, id, ctx);
      else if (id === "heart") heal(S(2), nm, depth + 1, id, ctx);   /* passives: an activation effect in their theme */
      else if (id === "whetstone") { const b = S(1); if (b > 0) { furyB += b; st.furyB = furyB; snap({ t: "first", src: nm + " +" + b + " ATK", depth: depth + 1, rid: id }); } }
      else if (id === "iron") { const b = S(2); if (b > 0) { stoneB += b; snap({ t: "first", src: nm + " +" + b + " DEF", depth: depth + 1, rid: id }); } }
      else if (id === "midas") gainGold(S(2), nm, depth + 1, id, ctx);
      else if (id === "greed") gainGold(S(2), nm, depth + 1, id, ctx);
      else if (id === "piggy") gainGold(S(1), nm, depth + 1, id, ctx);
      else if (id === "berserk") { const b = S(st.php < st.pmax / 2 ? 2 : 1); if (b > 0) { furyB += b; st.furyB = furyB; snap({ t: "first", src: nm + " +" + b + " ATK", depth: depth + 1, rid: id }); } }
      else if (id === "clover") emitLuck(nm, depth + 1, id, ctx);
      else if (id === "rabbit") { const b = S(1); if (b > 0) { luckB += b; snap({ t: "first", src: nm + " +" + b + " LUCK", depth: depth + 1, rid: id }); } }
      else if (id === "cutpurse") gainGold(S(1), nm, depth + 1, id, ctx);
      else if (id === "dice") { if (ehp > 0) dealDmg(S(2), nm, depth + 1, id, ctx); }
      else if (id === "boots" || id === "dash") { const b = S(2); if (b > 0) { snap({ t: "first", src: nm + " +" + b + " SPD", depth: depth + 1, rid: id }); galeB += b; if (nI("stormlattice") && ehp > 0) dealDmg(nI("stormlattice"), this.itemName("stormlattice"), depth + 2, "stormlattice", ctx); } }
      else if (id === "altar") { if (ehp > 0) dealDmg(S(2), nm, depth + 1, id, ctx); }
      else if (id === "thorns") { if (ehp > 0) dealDmg(S(2), nm, depth + 1, id, ctx); }
      else if (id === "tooth") heal(S(1), nm, depth + 1, id, ctx);
      else if (id === "magnet") gainGold(S(2), nm, depth + 1, id, ctx);   /* trigger-activation only; its passive is amplifying drops */
      else if (id === "fang") gainGold(S(5), nm, depth + 1, id, ctx);
      else if (id === "adrenaline") { const b = S(1); if (b > 0) { furyB += b; st.furyB = furyB; snap({ t: "adren", amt: b, depth: depth + 1, rid: id }); } }   /* activation: floor-scoped ATK */
      else if (id === "sentinel") { const b = S(1); if (b > 0 && sentB < 3) { sentB = Math.min(3, sentB + b); snap({ t: "first", src: nm + " +" + b + " DEF", depth: depth + 1, rid: id }); } }
      else if (id === "sprinter") heal(S(2), nm, depth + 1, id, ctx);
      else if (id === "embercask") gainGold(S(1), nm, depth + 1, id, ctx);
      else if (id === "coinsinger") emitLuck(nm, depth + 1, id, ctx);
      else if (id === "hexthread") { if (ehp > 0) dealDmg(S(1), nm, depth + 1, id, ctx); }
      else if (id === "fortunesedge") { fEdge = true; snap({ t: "first", src: nm + " charges the blade", depth: depth + 1, rid: id }); }
      else if (id === "quickpulse") { galeB += nI(id); snap({ t: "first", src: nm + " +" + nI(id) + " SPD", depth: depth + 1, rid: id }); }
      else if (id === "butchertithe" || id === "giltrot") heal(S(1), nm, depth + 1, id, ctx);
      else if (id === "tollplate") gainGold(S(1), nm, depth + 1, id, ctx);
    };
    const emitLuck = (src, depth, rid, ctx, quiet) => {   /* luck: a valueless bus event others can consume */
      if (depth > CAPn()) { snap({ t: "fizzle", depth }); if (nI("sparkjar")) sparkStored = true; return; }
      if (!quiet) snap({ t: "luck", src, depth, rid });
      rabbitReact(depth, rid, ctx);
      if (rid !== "hexthread" && nI("hexthread") && ehp > 0) { const sc = relicScale(ctx || (ctx = newCtx()), "hexthread"); const d = Math.round(nI("hexthread") * sc); if (d > 0) dealDmg(d, this.itemName("hexthread"), depth + 1, "hexthread", ctx); }
      if (rid !== "fortunesedge" && nI("fortunesedge") && !fEdge) { fEdge = true; snap({ t: "first", src: this.itemName("fortunesedge") + " charges the blade", depth: depth + 1, rid: "fortunesedge" }); }
      if (rid !== "quickpulse" && nI("quickpulse")) { galeB += nI("quickpulse"); snap({ t: "first", src: this.itemName("quickpulse") + " +" + nI("quickpulse") + " SPD", depth: depth + 1, rid: "quickpulse" }); }
      fireTrig("t_luck", depth, "rabbit", rid, ctx);
    };
    const rabbitReact = (depth, rid, ctx) => {     /* Rabbit's Foot: +1 LCK this floor per luck signal */
      const n = count(st.items, "rabbit");
      if (n <= 0 || rid === "rabbit") return;
      ctx = ctx || newCtx();
      const sc = relicScale(ctx, "rabbit"), b = Math.round(n * sc);
      if (b > 0) { luckB += b; snap({ t: "first", src: this.itemName("rabbit") + " +" + b + " LUCK", depth: depth + 1, rid: "rabbit" }); }
      fireEmit("rabbit", depth, ctx, sc);
    };
    const gainGold = (amt, src, depth, rid, ctx) => {
      ctx = ctx || newCtx();
      if (depth > CAPn()) { snap({ t: "fizzle", depth }); if (nI("sparkjar")) sparkStored = true; return; }
      if (amt <= 0) { if (rid) { snap({ t: "fizzle", depth }); if (nI("sparkjar")) sparkStored = true; } return; }
      if (rid && nI("echobead") && (echoUsed || 0) < (AW["echobead"] ? 2 : 1)) { echoUsed = (echoUsed || 0) + 1; amt *= 2; snap({ t: "first", src: this.itemName("echobead") + " doubles it", depth, rid: "echobead" }); }
      let real = count(st.items, "greed") > 0 ? amt * 2 : amt;
      if (SB("GREED") >= 3) real = Math.round(real * 1.15);              /* GREED set (3) */
      if (nI("fortunedebt") && !AW["fortunedebt"]) real = Math.round(real * 0.9);
      real = Math.max(1, real);
      if (st._debtLeft > 0) st._debtLeft = Math.max(0, st._debtLeft - real);   /* Debtor's Chain repays itself */
      st.gold += real; snap({ t: "gold", amt: real, src, depth, rid });
      const n = nI("alchemist");
      if (n > 0) { const sc = relicScale(ctx, "alchemist"); heal(Math.round(n * sc), this.itemName("alchemist"), depth + 1, "alchemist", ctx); fireEmit("alchemist", depth, ctx, sc); }
      if (rid !== "coinsinger" && nI("coinsinger") && (rid || AW["coinsinger"])) { const sc3 = relicScale(ctx, "coinsinger"); if (Math.round(sc3) > 0) { emitLuck(this.itemName("coinsinger"), depth + 1, "coinsinger", ctx); fireEmit("coinsinger", depth, ctx, sc3); } }   /* GOLD → GRACE */
      for (const gg of ["butchertithe", "giltrot"]) if (rid && rid !== gg && nI(gg)) { const scg = relicScale(ctx, gg); const h = Math.round(nI(gg) * scg); if (h > 0) { heal(h, this.itemName(gg), depth + 1, gg, ctx); fireEmit(gg, depth, ctx, scg); } }
      goldN++; if (goldN % 3 === 0) fireTrig("t_gold", depth, "alchemist", rid, ctx);
    };
    const heal = (amt, src, depth, rid, ctx) => {
      ctx = ctx || newCtx();
      if (depth > CAPn()) { snap({ t: "fizzle", depth }); if (nI("sparkjar")) sparkStored = true; return; }
      const after = [];   /* modifiers adjust the amount now but log AFTER the heal line (parent before children) */
      if (amt > 0) {
        if (rid && rid !== "martyr" && nI("martyr")) { amt += nI("martyr"); after.push({ t: "first", src: this.itemName("martyr") + " +" + nI("martyr"), depth: depth + 1, rid: "martyr" }); }   /* Martyr's Knot: relic heals +1 */
        if (SB("FLESH") >= 5) amt += 1;                    /* FLESH set (5) */
        if (!AW["faminebell"]) amt -= nI("faminebell");                           /* Famine Bell: your heals -1 */
        if (amt > 0 && rid && nI("echobead") && (echoUsed || 0) < (AW["echobead"] ? 2 : 1)) { echoUsed = (echoUsed || 0) + 1; amt *= 2; after.push({ t: "first", src: this.itemName("echobead") + " doubles it", depth: depth + 1, rid: "echobead" }); }
      }
      if (amt <= 0) { if (rid) { snap({ t: "fizzle", depth }); if (nI("sparkjar")) sparkStored = true; } return; }
      if (nI("quenched")) { qN = (qN || 0) + 1; if (AW["quenched"] || qN % 3 === 0) { qB += nI("quenched"); after.push({ t: "first", src: this.itemName("quenched") + " +" + nI("quenched") + " ATK", depth: depth + 1, rid: "quenched" }); } }   /* Quenched Blade: every 3rd heal */
      const real = Math.min(amt, st.pmax - st.php);
      if (rid === "tooth" && AW["tooth"] && cur && (cur.max || cur.hp) > 2) {   /* awakened Tooth: drains the ceiling, not the pool */
        cur.max = Math.max(2, (cur.max || cur.hp) - 1); if (ehp > cur.max) ehp = cur.max;
        snap({ t: "first", src: this.itemName("tooth") + " takes 1 max HP", depth: depth + 1, rid: "tooth" });
      }
      if (real < amt && nI("chalice") && (st._chaliceG || 0) < (AW["chalice"] ? 20 : 10)) { const cg = AW["chalice"] ? 2 : 1; st.pmax += cg; st._chaliceG = (st._chaliceG || 0) + cg; after.push({ t: "first", src: this.itemName("chalice") + " +" + cg + " max HP", depth: depth + 1, rid: "chalice" }); }
      if (real <= 0) { snap({ t: "healfull", src, depth, rid }); for (const a of after) snap(a); return; }
      st.php += real; snap({ t: "heal", amt: real, src, depth, rid });
      for (const a of after) snap(a);
      const n = count(st.items, "altar");
      if (n > 0 && rid !== "altar") { const sc = relicScale(ctx, "altar"); if (ehp > 0) { dealDmg(Math.round(2 * n * sc), this.itemName("altar"), depth + 1, "altar", ctx); if (AW["altar"]) heal(1, this.itemName("altar"), depth + 2, "altar", ctx); } fireEmit("altar", depth, ctx, sc); }
      fireTrig("t_heal", depth, "altar", rid, ctx);
    };
    const dealDmg = (amt, src, depth, rid, ctx) => {
      ctx = ctx || newCtx();
      if (depth > CAPn()) { snap({ t: "fizzle", depth }); if (nI("sparkjar")) sparkStored = true; return; }
      if (ehp <= 0) return;
      if (amt <= 0) { if (rid) { snap({ t: "fizzle", depth }); if (nI("sparkjar")) sparkStored = true; } return; }
      if (rid && nI("echobead") && (echoUsed || 0) < (AW["echobead"] ? 2 : 1)) { echoUsed = (echoUsed || 0) + 1; amt *= 2; snap({ t: "first", src: this.itemName("echobead") + " doubles it", depth, rid: "echobead" }); }
      if (depth === 0 && src === "you" && cur.relics && cur.relics.indexOf("clover") >= 0 && rng() < (cur.lck || 10) / 100) {   /* enemies evade only with a clover of their own */
        snap({ t: "emiss", depth: 0, foe: cur.relics ? 1 : 0 });
        return;
      }
      const real = applyDef(amt, (AW["whetstone"] && count(st.items, "whetstone") && src === "you") ? 0 : (cur.armor || 0), st.defModel);
      ehp -= real; snap({ t: "edmg", amt: real, src, depth, rid });
      if (rid && rid !== "embercask" && nI("embercask")) { const sc4 = relicScale(ctx, "embercask"); const g4 = Math.round(nI("embercask") * sc4); if (g4 > 0) { gainGold(g4, this.itemName("embercask"), depth + 1, "embercask", ctx); fireEmit("embercask", depth, ctx, sc4); } }   /* BLOOD → GOLD */
      if (rid && rid !== "tollplate" && nI("tollplate")) { const sc5 = relicScale(ctx, "tollplate"); const g5 = Math.round(nI("tollplate") * sc5); if (g5 > 0) { gainGold(g5, this.itemName("tollplate"), depth + 1, "tollplate", ctx); fireEmit("tollplate", depth, ctx, sc5); } }
      if (rid && depth >= 3 && nI("resonantskull") && !ctx.v._res) { ctx.v._res = 1; heal(AW["resonantskull"] ? 2 : 1, this.itemName("resonantskull"), depth + 1, "resonantskull", ctx); }   /* deep chains hum back */
      if (ehp <= 0) { onKill(depth, rid, ctx); return; }
      if (depth === 0 && en2("thorns") > 0 && st.php > 0) {   /* rival's Thorn Vest bites back */
        const tv = en2("thorns"); st.php -= tv; snap({ t: "pdmg", amt: tv, depth: 1, rid: "thorns", foe: 1 });
        if (st.php <= 0) return;
      }
      if (!foeFury && en2("berserk") > 0 && ehp < (cur.max || cur.hp) / 2) { foeFury = true; snap({ t: "efury", amt: 2, depth: 1, rid: "berserk", foe: 1 }); }
    };
    const onKill = (depth, rid) => {
      st.kills++; snap({ t: "kill", eidx: cur.idx, depth });
      const kctx = newCtx();                /* the kill is one genuine root; all reactions share its chain */
      if (nI("headsman")) { if (AW["headsman"]) st.adrenaline += nI("headsman"); else hdB += nI("headsman"); snap({ t: "first", src: this.itemName("headsman") + " +" + nI("headsman") + (AW["headsman"] ? " ATK, permanent" : " ATK"), depth: 1, rid: "headsman" }); }
      if (SB("EDGE") >= 7) { hdB += 1; snap({ t: "first", src: "Edge set \u2014 +1 ATK", depth: 1 }); }
      if (nI("sprinter") && fightStrikes <= (AW["sprinter"] ? 8 : 6)) heal((AW["sprinter"] ? 3 : 2) * nI("sprinter"), this.itemName("sprinter"), 1, "sprinter", kctx);
      const m = nI("magnet");  /* magnet AMPLIFIES loose-coin drops: +1 each, no chain node */
      gainGold(Math.round(cur.drop * (SB("GREED") >= 7 ? 1.5 : 1)) + m, this.t("killLoot"), 0, null, kctx);
      if (nI("tollring")) { const sc2 = relicScale(kctx, "tollring"); gainGold(Math.max(1, Math.round(5 * nI("tollring") * sc2)), this.itemName("tollring"), 1, "tollring", kctx); fireEmit("tollring", 0, kctx, sc2); }
      if (m > 0) fireEmit("magnet", 0, kctx, relicScale(kctx, "magnet"));
      const n = count(st.items, "fang");
      if (n > 0) { const sc = relicScale(kctx, "fang"); gainGold(Math.round(5 * n * sc), this.itemName("fang"), 1, "fang", kctx); fireEmit("fang", 0, kctx, sc); }
      fireTrig("t_kill", 0, "magnet", null, kctx);
    };
    let adrenUsed = false;
    const en2 = id => (cur && cur.relics ? cur.relics.filter(x => x === id).length : 0);   /* rival's relics (versus) */
    let foeFury = false;
    const enemyHits = () => {
      trigFired = {};
      const hctx = newCtx();                /* the enemy's action is one genuine root */
      if ((count(st.items, "clover") > 0 || st.benchAbilities) && rng() < heroLck() / 100) {   /* evasion lives in the Clover (benchAbilities: harness-only unlock) */
        snap({ t: "miss", depth: 0, rid: count(st.items, "clover") ? "clover" : null });
        if (count(st.items, "clover")) {
          const sc = relicScale(hctx, "clover");
          emitLuck(this.itemName("clover"), 1, "clover", hctx);
          fireEmit("clover", 0, hctx, sc);
        }
        if (nI("horseshoe")) emitLuck(this.itemName("horseshoe"), 1, "horseshoe", hctx);
        if (nI("clover") && AW["clover"]) emitLuck(this.itemName("clover"), 1, "clover", hctx);
        if (nI("sentinel")) { const b = nI("sentinel"); sentB += b; snap({ t: "first", src: this.itemName("sentinel") + " +" + b + " DEF", depth: 1, rid: "sentinel" }); if (AW["sentinel"] && ehp > 0) dealDmg(1, this.itemName("sentinel"), 1, "sentinel", hctx); }
        if (nI("catwhisker") && (AW["catwhisker"] || !catUsed) && ehp > 0) { catUsed = true; dealDmg(Math.max(1, Math.round(heroStatOf(ctxOf(), "atk") / 2)), this.itemName("catwhisker"), 1, "catwhisker", hctx); }
        if (SB("LUCK") >= 7 && ehp > 0) dealDmg(2, "Luck set", 1, "clover", hctx);   /* LUCK set (7): dodges counter */
        if (nI("stutter")) { cur.spd = Math.max(10, (cur.spd || 25) - nI("stutter")); snap({ t: "eslow", amt: nI("stutter"), depth: 1, rid: "stutter" }); if (AW["stutter"]) stumble = 1; }   /* Stutterstep: every counter staggers them */
        if (nI("haredrum")) hgBoost = true;
        fireTrig("t_dodge", 0, "clover", null, hctx);
        return;
      }
      if (nI("trickster") && rng() < Math.min(0.2, 0.1 * nI("trickster"))) { snap({ t: "miss", depth: 0, rid: "trickster" }); if (AW["trickster"] && ehp > 0) dealDmg(1, this.itemName("trickster"), 1, "trickster", newCtx()); return; }
      if (SB("GUARD") >= 7 && !blockedFight) { blockedFight = true; snap({ t: "first", src: "Guard set — the first blow glances off", depth: 0 }); return; }
      let def = heroStatOf(ctxOf(), "def");
      if (stumble > 0) {   /* awakened Stutterstep: the staggered foe swings into itself */
        stumble = 0;
        const selfHit = Math.max(1, cur.atk);
        snap({ t: "first", src: this.itemName("stutter") + " \u2014 the foe trips into its own blade", depth: 0, rid: "stutter" });
        if (ehp > 0) dealDmg(selfHit, this.itemName("stutter"), 1, "stutter", newCtx());
        return;
      }
      if (AW["martyr"] && nI("martyr")) {   /* awakened Martyr's Knot: 1 in 3 blows is borne by the foe */
        martyrN = (martyrN || 0) + 1;
        if (martyrN % 3 === 0) {
          snap({ t: "first", src: this.itemName("martyr") + " bears it \u2014 the blow lands on the foe", depth: 0, rid: "martyr" });
          if (ehp > 0) dealDmg(Math.max(1, cur.atk), this.itemName("martyr"), 1, "martyr", newCtx());
          return;
        }
      }
      const greedPain = count(st.items, "greed") ? 1 : 0;
      if (greedPain && AW["greed"]) gainGold(2, this.itemName("greed"), 1, "greed", newCtx());   /* through gainGold: the coin feeds Vial/Singer/Midas like any other gold */
      let dmg = applyDef(cur.atk + (foeFury ? 2 : 0) + (greedPain && !AW["greed"] ? 1 : 0), def, st.defModel);
      if (AW["rustward"] || st.php < st.pmax / 2) dmg -= nI("rustward");
      if (AW["iron"] && nI("iron") && !ironGl) { ironGl = true; snap({ t: "first", src: this.itemName("iron") + " \u2014 the blow glances off", depth: 0, rid: "iron" }); return; }
      hitN = (hitN || 0) + 1;
      if (AW["hide"] && nI("hide") && hitN > 1 && hideLearn > 0) { dmg = Math.max(1, Math.round(dmg * 0.5)); snap({ t: "first", src: this.itemName("hide") + " has learned this blow", depth: 1, rid: "hide" }); }
      if (hitN <= 1) { hitTakenFight = true; if (AW["hide"] && nI("hide")) hideLearn = 1; if (nI("hide")) { dmg -= 2 * nI("hide"); snap({ t: "first", src: this.itemName("hide") + " softens the blow: -" + (2 * nI("hide")), depth: 1, rid: "hide" }); } }
      dmg = Math.max(1, dmg) + (nI("crackedcrown") && !AW["crackedcrown"] ? 1 : 0);
      let eCrit = false;
      if (en2("dice") > 0 && rng() < (cur.lck || 10) / 100) { eCrit = true; dmg = Math.round(dmg * 1.5); }   /* rival's Weighted Dice: LCK% crit, ×1.5 */
      st.php -= dmg; snap({ t: "pdmg", amt: dmg, depth: 0, ecrit: eCrit ? 1 : 0 });
      /* death-defiance ladder: FLESH set (once/floor) → Gravekeeper's Soil (once/run) → CURSE set explosion */
      if (st.php <= 0 && SB("FLESH") >= 7 && !f7used) { f7used = true; st.php = 1; snap({ t: "first", src: "Flesh set — you refuse to fall", depth: 0 }); }
      if (st.php <= 0 && nI("soil") && !st._soilUsed) { st._soilUsed = true; st.php = Math.max(1, Math.round(st.pmax * (AW["soil"] ? 0.5 : 0.25))); snap({ t: "first", src: this.itemName("soil") + " — the ground gives you back", depth: 0, rid: "soil" }); }
      if (st.php <= 0 && SB("CURSE") >= 7 && !st._curse7 && ehp > 0) { st._curse7 = true; dealDmg(heroStatOf(ctxOf(), "atk") * 3, "Curse set", 1, "crackedcrown", hctx); }
      if (st.php <= 0 && count(st.items, "glassedge")) { if (AW["glassedge"] && !st._glassRef) { st._glassRef = true; snap({ t: "first", src: this.itemName("glassedge") + " reforges itself", depth: 1, rid: "glassedge" }); } else st._glassBroken = true; }   /* Glass Edge shatters (awakened: one reforge) */
      if (eCrit && nI("mirrorscale") && st.php > 0 && ehp > 0) dealDmg(dmg, this.itemName("mirrorscale"), 1, "mirrorscale", hctx);
      if (st.php > 0 && st.php < st.pmax / 2 && nI("marrow")) {
        heal(2 * nI("marrow"), this.itemName("marrow"), 1, "marrow", hctx);
        if (AW["marrow"] && ehp > 0) dealDmg(2 * nI("marrow"), this.itemName("marrow"), 2, "marrow", hctx);   /* awakened: knit from their flesh */
      }
      if (en2("tooth") > 0 && ehp > 0 && ehp < (cur.max || cur.hp) && !nI("faminebell")) {   /* rival's Vampire Tooth (Famine Bell silences it) */
        const h = Math.min(en2("tooth"), (cur.max || cur.hp) - ehp);
        ehp += h; snap({ t: "eheal", amt: h, depth: 1, rid: "tooth", foe: 1 });
      }
      let a = count(st.items, "adrenaline");
      if (a > 0 && st.php > 0 && !adrenUsed) { adrenUsed = true; const sc = relicScale(hctx, "adrenaline");
        const echoed = nI("echobead") && (echoUsed || 0) < (AW["echobead"] ? 2 : 1); if (echoed) { echoUsed = (echoUsed || 0) + 1; a *= 2; }
        st.adrenaline += a; snap({ t: "adren", amt: a, depth: 1, rid: "adrenaline" });
        if (echoed) snap({ t: "first", src: this.itemName("echobead") + " doubles it", depth: 2, rid: "echobead" }); fireEmit("adrenaline", 0, hctx, sc); }
      const th = nI("thorns");
      if (th > 0 && st.php > 0) { const sc = relicScale(hctx, "thorns"); dealDmg(Math.round(2 * th * sc) + (SB("GUARD") >= 5 ? 1 : 0), this.itemName("thorns"), 1, "thorns", hctx); if (nI("gildedthorns")) gainGold(2 * nI("gildedthorns"), this.itemName("gildedthorns"), 2, "gildedthorns", hctx); fireEmit("thorns", 0, hctx, sc); }
      if (st.php > 0) { if (count(st.items, "greed")) fireEmit("greed", 0, hctx, relicScale(hctx, "greed")); painN++; if (painN % 3 === 0) fireTrig("t_hit", 0, null, null, hctx); }
    };
    const playerHits = () => {
      trigFired = {};
      fightStrikes++;
      st._strikeTot = (st._strikeTot || 0) + 1;
      if (nI("anvil") && st._strikeTot % 10 === 0 && (st._anvilB || 0) < (AW["anvil"] ? 12 : 10)) { st._anvilB = (st._anvilB || 0) + 1; st.defB = (st.defB || 0) + 1; snap({ t: "first", src: this.itemName("anvil") + " hardens: +1 DEF", depth: 1, rid: "anvil" }); }
      if (nI("leechpurse") && st.gold >= 3) { st.gold -= 3; heal(nI("leechpurse"), this.itemName("leechpurse"), 1, "leechpurse", newCtx()); }   /* Leech Purse: 3g per turn */
      const bossFoe = cur.rank === "boss" || cur.rank === "king" || cur.rank === "elite";
      if (fightStrikes === 1 && nI("sparbell")) snap({ t: "first", src: this.itemName("sparbell") + " rings: +" + (2 * nI("sparbell")) + " on the opener", depth: 1, rid: "sparbell" });
      if (fightStrikes === 1 && bossFoe && nI("duelist")) snap({ t: "first", src: this.itemName("duelist") + " \u2014 worthy blood: +" + (4 * nI("duelist")) + " ATK", depth: 1, rid: "duelist" });
      const strikeBonus = (fightStrikes === 1 ? 2 * nI("sparbell") : 0);   /* per-strike only; everything else lives in the stat ledger */
      const tryExecute = () => {   /* Executioner's Coin: once per floor, finishes a foe below 20% */
        if (!nI("executioner") || exeUsed || ehp <= 0 || ehp > (cur.max || cur.hp) * (AW["executioner"] ? 0.3 : 0.2)) return false;
        exeUsed = true;
        snap({ t: "first", src: this.itemName("executioner") + " \u2014 the sentence is carried out", depth: 0, rid: "executioner" });
        dealDmg(ehp + (cur.armor || 0), this.itemName("executioner"), 1, "executioner", newCtx());
        return true;
      };
      if (tryExecute() && ehp <= 0) return;
      if ((count(st.items, "dice") > 0 || st.benchAbilities) && rng() < heroLck() / 100) {   /* Weighted Dice: LCK% crit (benchAbilities: harness-only unlock) */
        const dctx = newCtx(), sc = relicScale(dctx, "dice");
        snap({ t: "luck", src: this.itemName("dice"), depth: 0, rid: "dice" });
        dealDmg(Math.round((heroStatOf(ctxOf(), "atk") + strikeBonus) * (AW["dice"] ? 2 : 1.5)), this.itemName("dice"), 0, "dice", dctx);
        if (nI("gambler") && !gamblerUsed) { gamblerUsed = true; luckB += 8; snap({ t: "first", src: this.itemName("gambler") + " +8 LUCK", depth: 1, rid: "gambler" }); }
        if (SB("LUCK") >= 5) emitLuck("Luck set", 1, null, dctx);   /* LUCK set (5): crits are lucky breaks */
        fireEmit("dice", 0, dctx, sc);
        emitLuck(this.itemName("dice"), 0, "dice", dctx, true);   /* full luck signal, no duplicate log line */
      } else {
        let amt = heroStatOf(ctxOf(), "atk") + strikeBonus;
        if (SB("EDGE") >= 5 && fightStrikes % 4 === 0) amt = Math.round(amt * 1.5);   /* EDGE set (5): every 4th strike */
        if (fEdge) { amt = Math.round(amt * (AW["fortunesedge"] ? 2 : 1.5)); fEdge = false; snap({ t: "first", src: this.itemName("fortunesedge") + " \u2014 charged strike!", depth: 1, rid: "fortunesedge" }); }
        dealDmg(amt, "you", 0);
      }
      if (nI("momentum")) { momN = (momN || 0) + 1; if (momN % (AW["momentum"] ? 2 : 3) === 0) { momB += nI("momentum"); snap({ t: "mom", amt: nI("momentum"), depth: 1, rid: "momentum" }); } }
      if (tryExecute() && ehp <= 0) return;
      if (sparkStored && ehp > 0 && st.php > 0) { sparkStored = false; dealDmg(AW["sparkjar"] ? 4 : 2, this.itemName("sparkjar"), 1, "sparkjar", newCtx()); }
      if (SB("PACE") >= 7 && fightStrikes % 5 === 0 && ehp > 0 && st.php > 0) { snap({ t: "first", src: "Pace set — a second strike!", depth: 0 }); dealDmg(heroStatOf(ctxOf(), "atk"), "you", 0); }
      if (ehp > 0) {
        const actx = newCtx();              /* the strike is one genuine root */
        const cp = count(st.items, "cutpurse");
        if ((cp > 0 && AW["cutpurse"]) || rng() < heroLck() * (cp > 0 ? 2 : 1) / 100) {   /* loose coins at LCK%; Hook doubles; awakened Hook: every strike */
          if (cp > 0) snap({ t: "luck", src: this.t("looseCoins"), depth: 0, rid: "cutpurse" });   /* only Hook spills are lucky breaks */
          const sc = cp > 0 ? relicScale(actx, "cutpurse") : 1;
          gainGold(1 + (cp > 0 ? Math.round(cp * sc) : 0) + nI("magnet") + (SB("GREED") >= 5 ? 1 : 0), this.t("looseCoins"), 1, cp > 0 ? "cutpurse" : null, actx);
          if (cp > 0) {
            fireEmit("cutpurse", 0, actx, sc);
            emitLuck(this.t("looseCoins"), 0, "cutpurse", actx, true);
          }
        }
        if (fightStrikes > 1 && nI("serrated")) { const sc2 = relicScale(actx, "serrated"); const b = Math.max(1, Math.round(nI("serrated") * sc2)); dealDmg(b, this.itemName("serrated"), 1, "serrated", actx); fireEmit("serrated", 0, actx, sc2); }   /* bleed from the 2nd strike on */
        const n = count(st.items, "tooth");
        if (n > 0 && ehp > 0) { const sc = relicScale(actx, "tooth"); heal(Math.round(n * sc), this.itemName("tooth"), 1, "tooth", actx); fireEmit("tooth", 0, actx, sc); }
        strikeN++;
        if (strikeN % 3 === 0) fireTrig("t_attack", 0, "tooth", null, actx);   /* socketed attack triggers fire every 3rd strike, counted across the floor */
      }
    };
    let guard = 0;
    for (let k = 0; k < pack.length && st.php > 0; k++) {
      cur = pack[k]; ehp = cur.hp;
      blockedFight = false; hitTakenFight = false; hitN = 0; ironGl = false; sentB = 0; echoUsed = 0;   /* Quenched Blade's temper (qB/qN) is floor-scoped, like Headsman's */ catUsed = false; fightStrikes = 0; hgBoost = false;
      if (AW["gambler"]) gamblerUsed = false; if (AW["adrenaline"]) adrenUsed = false;   /* per-fight state (momentum persists across the floor) */
      snap({ t: "enter", eidx: cur.idx, depth: 0, another: k > 0 });
      /* ATB gauges: 100 timeout points, each tick drains speed points; at 0 the unit attacks
         and the gauge refills to 100. Deterministic — pure integer ticks, no wall clock. */
      const dash = count(st.items, "dash") > 0;                /* Battle Dash: first strike every fight */
      const eDash = en2("dash") > 0;                           /* rival's Battle Dash */
      const GAUGE = 100, hSpdOf = () => heroStatOf(ctxOf(), "spd"), eSpdOf = () => Math.max(10, cur.spd || 25);
      trigFired = {};
      if (k === 0) fireTrig("t_floor", 0, null, null, newCtx());
      {
        if (nI("relaytotem")) {   /* Relay Totem: every fight also activates a random carried relic (awakened: two) */
          for (let rl = 0; rl < (AW["relaytotem"] ? 2 : 1); rl++) {
          const others = st.items.filter(x => x !== "relaytotem");
          if (others.length) { const rid2 = others[Math.floor(rng() * others.length)]; snap({ t: "first", src: this.itemName("relaytotem") + " relays " + this.itemName(rid2), depth: 0, rid: "relaytotem" }); nativeEffect(rid2, 0, newCtx(), 1, true); }
          }
        }
      }
      fireTrig("t_fight", 0, null, null, newCtx());
      /* Battle Dash resolves in the pre-battle window: dasher opens before gauges run; both dash → they cancel */
      let hG = (dash && !eDash) ? 0 : GAUGE, eG = (eDash && !dash) ? 0 : GAUGE;
      if (hG > 0 && nI("cavalry")) snap({ t: "first", src: this.itemName("cavalry") + " \u2014 the charge starts early", depth: 0, rid: "cavalry" });
      if (hG > 0) hG = Math.max(0, hG - (SB("PACE") >= 5 ? 25 : 0) - (nI("cavalry") ? (AW["cavalry"] ? 60 : 30) : 0));
      /* PACE set (5) + Cavalry Horn head starts */
      if (dash && eDash) snap({ t: "first", src: "Both dash — the openers cancel!", depth: 0 });
      else if (dash) snap({ t: "dashopen", src: this.itemName("dash"), depth: 0, rid: "dash" });
      else if (eDash) snap({ t: "edashopen", depth: 0, rid: "dash", foe: 1 });
      while (st.php > 0 && ehp > 0 && guard++ < 600) {
        if (eG <= 0 && hG > 0) { enemyHits(); if (st.php <= 0) break; eG += GAUGE; if (hgBoost) { hG = 0; hgBoost = false; if (AW["haredrum"] && ehp > 0) dealDmg(2, this.itemName("haredrum"), 1, "haredrum", newCtx()); snap({ t: "first", src: this.itemName("haredrum") + " — instant riposte!", depth: 0, rid: "haredrum" }); } }
        else if (hG <= 0 && eG > 0) { playerHits(); if (ehp <= 0 || st.php <= 0) break; hG += GAUGE; }
        else if (hG <= 0 && eG <= 0) { enemyHits(); if (st.php <= 0) break; eG += GAUGE; playerHits(); if (ehp <= 0 || st.php <= 0) break; hG += GAUGE; }
        else {
          if (AW["boots"] && nI("boots") && fightStrikes > 0 && fightStrikes % 4 === 0 && bootsUsed !== fightStrikes) { bootsUsed = fightStrikes; hG = 0; continue; }   /* awakened Boots: every 4th strike is instant */
          const hs = hSpdOf(), es = eSpdOf(), step = Math.min(Math.ceil(hG / hs), Math.ceil(eG / es)); hG -= hs * step; eG -= es * step; tkNow += step;
        }
      }
    }
    if (st.php <= 0) snap({ t: "death", depth: 0 });
    ev._carry = { furyB, stoneB, sentB, galeB, luckB, strikeN, painN, goldN, stoneCnt, f7used, titheDef, exeUsed, gamblerUsed, hdB, qB, qN, sparkStored };   /* state at sim end (= at death, if the hero fell) */
    return ev;
  }

  /* ==== Symmetric duel engine (versus): both sides are FULL players — items, sockets,
     counters, chains with per-side decay, set bonuses, dodge/crit/gauges. Delve keeps
     simulateFloor; this runs only rival fights so real-player matches simulate honestly.
     Side object: { name, base:{hp,atk,def,spd,lck}, php, pmax, gold, items, sockets,
     adrenaline, midasBonus, atkB, defB, spdB, luckB, kills, _anvilB, _chaliceG, _debtLeft,
     _glassBroken, _soilUsed } — A is the hero (events map to hero-centric types), B the rival. */
  simulateDuel(A, B, rng) {
    const ev = []; let tk = 0;
    const self = this;
    const snap = e => ev.push(Object.assign({ tk, php: A.php, ehp: Math.max(0, B.php), gold: A.gold, emax: B.pmax, eatk: atkOf(B), eidx: B.idx || 0, evar: B.variant || 0, erank: B.rank || "boss", earm: defOf(B), espd: spdOf(B), elck: lckOf(B), padr: A.adrenaline, pfury: A.fury || 0,
      pl: dynModsS(A), pcnt: { a: A.strikeN || 0, h: A.painN || 0, g: A.goldN || 0, st: A.stoneCnt || 0, f: A.strikes || 0, q: A.qN || 0, m: A.momN || 0, r: A.rabbitN || 0, at: A._strikeTot || 0, sb: A.sent || 0 } }, e));
    const CAPn = () => 4 - (nI(A, "fusecoil") || nI(B, "fusecoil") ? 1 : 0);
    const nI = (S, id) => S.items.filter(x => x === id).length + (((S.awake && S.awake[id]) || 0) > 0 ? (S.items.indexOf("tinkerloop") >= 0 ? 2 : 1) : 0);
    const AWv = (S, id) => !!(S.awake && S.awake[id]);
    const SB = (S, g) => S.items.filter(x => (ITEMS[x] && ITEMS[x].kind) === g).length;
    const dynModsS = S => { const m = [];
      if (S.fury) m.push({ stat: "atk", src: "Fury (this round)", amt: S.fury });
      if (S.qB) m.push({ stat: "atk", src: "Quenched Blade (this round)", amt: S.qB });
      if (S.hdB) m.push({ stat: "atk", src: "Headsman's Notch", amt: S.hdB });
      if (S.stone) m.push({ stat: "def", src: "Stone / Iron emitters (this fight)", amt: S.stone });
      if (S.sent) m.push({ stat: "def", src: "Sentinel Bell (this floor)", amt: S.sent });
      if (S.gale) m.push({ stat: "spd", src: "Gale emitters (this round)", amt: S.gale });
      if (S.mom) m.push({ stat: "spd", src: "Momentum Bead (this fight)", amt: S.mom });
      if (S.luckT) m.push({ stat: "lck", src: "Rabbit / Gambler (this fight)", amt: S.luckT });
      return m; };
    const ctxOfS = S => ({ items: S.items, awake: S.awake || {}, mode: "versus",
      base: { atk: S.base.atk, def: S.base.def, spd: S.base.spd, lck: S.base.lck },
      run: { adrenaline: S.adrenaline || 0, midasBonus: S.midasBonus || 0, atkB: S.atkB || 0, defB: S.defB || 0, spdB: S.spdB || 0, luckB: S.luckB || 0 },
      php: S.php, pmax: S.pmax, gold: S.gold || 0, floor: 0, foeRank: "boss",
      debtLeft: S._debtLeft || 0, glassBroken: S._glassBroken, mods: dynModsS(S) });
    const lckOf = S => heroStatOf(ctxOfS(S), "lck");
    const spdOf = S => heroStatOf(ctxOfS(S), "spd");
    const defOf = S => heroStatOf(ctxOfS(S), "def");
    const atkOf = S => heroStatOf(ctxOfS(S), "atk");
    const newCtx = () => ({ v: {} });
    const scaleOf = (ctx, S, id) => { const key = S.tag + id, n = (ctx.v[key] = (ctx.v[key] || 0) + 1); return Math.pow(nI(S, "fusecoil") ? 0.75 : (SB(S, "CHAIN") >= 7 ? 0.75 : SB(S, "CHAIN") >= 3 ? 0.6 : 0.5), n - 1); };
    const nameOf = (S, id) => (S.tag === "B" ? S.name + "'s " : "") + this.itemName(id);
    const proc = (S, id, txt, depth) => snap({ t: "first", src: txt, depth, rid: id, foe: S.tag === "B" ? 1 : 0 });
    /* per-side transient fight state lives ON the side object (reset per fight) */
    const resetFight = S => { S.fury = S.fury || 0; S.stone = 0; S.gale = 0; S.luckT = 0; S.mom = 0; S.momN = 0; S.qB = S.qB || 0; S.qN = S.qN || 0; S.sent = 0;   /* Quenched temper is round-scoped */ S.hdB = S.hdB || 0; S.strikes = 0; S.strikeN = 0; S.painN = 0; S.goldN = 0; S.stoneCnt = 0; S.echoUsed = false; S.catUsed = false; S.adrenUsed = false; S.exeUsed = false; S.gamblerUsed = false; S.blocked = false; S.hitTaken = false; S.sparkStored = false; S.hgBoost = false; S.f7used = false; S.curse7 = false; S.trigFired = {}; };
    const other = S => S.tag === "A" ? B : A;
    let fireIdxS = null;
    const fireEmitAt = (S, idx, depth, ctx, sc) => {
      const id = S.items[idx], em = S.sockets && S.sockets[idx] && EMITTERS[S.sockets[idx]] ? S.sockets[idx] : null;
      if (!em) return;
      ctx = ctx || newCtx();
      let a = Math.round(EMITTERS[em].amt * (sc == null ? 1 : sc));
      if (a > 0) a += (nI(S, "tinkerloop") ? 1 : 0) + (nI(S, "fusecoil") ? 1 : 0);
      if (a <= 0) { snap({ t: "fizzle", depth: depth + 1 }); return; }
      const nm = nameOf(S, id);
      if (em === "e_dmg") dealDmg(S, a, nm, depth + 1, id, ctx);
      else if (em === "e_heal") heal(S, a, nm, depth + 1, id, ctx);
      else if (em === "e_gold") gainGold(S, a, nm, depth + 1, id, ctx);
      else if (em === "e_atk") { S.fury += a; proc(S, id, nm + " +" + a + " ATK", depth + 1); }
      else if (em === "e_def") { S.stoneCnt++; const a2 = Math.max(a, 1); S.stone += a2; proc(S, id, nm + " +" + a2 + " DEF", depth + 1); }
      else if (em === "e_spd") { S.gale += a; proc(S, id, nm + " +" + a + " SPD", depth + 1); if (nI(S, "stormlattice")) dealDmg(S, nI(S, "stormlattice"), nameOf(S, "stormlattice"), depth + 2, "stormlattice", ctx); }
      else if (em === "e_luck") { S.luckT += a; proc(S, id, nm + " +" + a + " LUCK", depth + 1); }
    };
    const fireEmit = (S, id, depth, ctx, sc) => { for (let i = 0; i < S.items.length; i++) if (S.items[i] === id) fireEmitAt(S, i, depth, ctx, sc); };
    const fireTrig = (S, tid, depth, exclude, srcRid, ctx) => {
      if (depth > CAPn()) return;
      for (let idx = 0; idx < S.items.length; idx++) {
        const id = S.items[idx];
        if ((exclude && exclude.indexOf(id) >= 0) || !S.sockets || S.sockets[idx] !== tid) continue;
        if (srcRid) {
          if (srcRid === id) continue;
          const sc = scaleOf(ctx || (ctx = newCtx()), S, id);
          nativeEffect(S, id, depth + 1, ctx, sc, true); fireEmitAt(S, idx, depth + 1, ctx, sc);
        } else {
          if (S.trigFired[idx]) continue; S.trigFired[idx] = true;
          const c2 = newCtx(), sc = scaleOf(c2, S, id);
          nativeEffect(S, id, 0, c2, sc, true); fireEmitAt(S, idx, 0, c2, sc);
        }
      }
    };
    const nativeEffect = (S, id, depth, ctx, sc, viaTrig) => {
      if (depth > CAPn()) return;
      ctx = ctx || newCtx();
      const nm = nameOf(S, id) + (viaTrig ? " \u26a1" : "");
      const k = viaTrig ? 1 : Math.max(1, nI(S, id));
      const Sc = a => Math.round(a * k * (sc == null ? 1 : sc));
      if (id === "alchemist") heal(S, Sc(1), nm, depth + 1, id, ctx);
      else if (id === "heart") heal(S, Sc(2), nm, depth + 1, id, ctx);
      else if (id === "whetstone") { const b = Sc(1); if (b > 0) { S.fury += b; proc(S, id, nm + " +" + b + " ATK", depth + 1); } }
      else if (id === "iron") { const b = Sc(2); if (b > 0) { S.stone += b; proc(S, id, nm + " +" + b + " DEF", depth + 1); } }
      else if (id === "berserk") { const b = Sc(S.php < S.pmax / 2 ? 2 : 1); if (b > 0) { S.fury += b; proc(S, id, nm + " +" + b + " ATK", depth + 1); } }
      else if (id === "clover") emitLuck(S, nm, depth + 1, id, ctx);
      else if (id === "rabbit") { const b = Sc(1); if (b > 0) { S.luckT += b; proc(S, id, nm + " +" + b + " LUCK", depth + 1); } }
      else if (id === "cutpurse") gainGold(S, Sc(1), nm, depth + 1, id, ctx);
      else if (id === "dice") dealDmg(S, Sc(2), nm, depth + 1, id, ctx);
      else if (id === "boots" || id === "dash") { const b = Sc(2); if (b > 0) { S.gale += b; proc(S, id, nm + " +" + b + " SPD", depth + 1); if (nI(S, "stormlattice")) dealDmg(S, nI(S, "stormlattice"), nameOf(S, "stormlattice"), depth + 2, "stormlattice", ctx); } }
      else if (id === "altar" || id === "thorns") dealDmg(S, Sc(2), nm, depth + 1, id, ctx);
      else if (id === "tooth") heal(S, Sc(1), nm, depth + 1, id, ctx);
      else if (id === "adrenaline") { const b = Sc(1); if (b > 0) { S.fury += b; proc(S, id, nm + " +" + b + " ATK", depth + 1); } }
      else if (id === "midas") gainGold(S, Sc(2), nm, depth + 1, id, ctx);
      else if (id === "sentinel") { const b = Sc(1); const sCap2 = AWv(S, "sentinel") ? 5 : 3; if (b > 0 && S.sent < sCap2) { S.sent = Math.min(sCap2, S.sent + b); proc(S, id, nm + " +" + b + " DEF", depth + 1); } }
      else if (id === "sprinter") heal(S, Sc(2), nm, depth + 1, id, ctx);
      else if (id === "embercask") gainGold(S, Sc(1), nm, depth + 1, id, ctx);
      else if (id === "coinsinger") emitLuck(S, nm, depth + 1, id, ctx);
      else if (id === "hexthread") { if (other(S).php > 0) dealDmg(S, Sc(1), nm, depth + 1, id, ctx); }
      else if (id === "fortunesedge") { S.fEdge = true; proc(S, id, nm + " charges the blade", depth + 1); }
      else if (id === "quickpulse") { S.gale += nI(S, id); proc(S, id, nm + " +" + nI(S, id) + " SPD", depth + 1); }
      else if (id === "butchertithe" || id === "giltrot") heal(S, Sc(1), nm, depth + 1, id, ctx);
      else if (id === "tollplate") gainGold(S, Sc(1), nm, depth + 1, id, ctx);
    };
    const emitLuck = (S, src, depth, rid, ctx, quiet) => {
      if (depth > CAPn()) { snap({ t: "fizzle", depth }); if (nI(S, "sparkjar")) S.sparkStored = true; return; }
      if (!quiet && S.tag === "A") snap({ t: "luck", src, depth, rid });
      rabbitReact(S, depth, rid, ctx);
      if (rid !== "hexthread" && nI(S, "hexthread") && other(S).php > 0) { const sc = scaleOf(ctx || (ctx = newCtx()), S, "hexthread"); const d = Math.round(nI(S, "hexthread") * sc); if (d > 0) dealDmg(S, d, nameOf(S, "hexthread"), depth + 1, "hexthread", ctx); }
      if (rid !== "fortunesedge" && nI(S, "fortunesedge") && !S.fEdge) { S.fEdge = true; proc(S, "fortunesedge", nameOf(S, "fortunesedge") + " charges the blade", depth + 1); }
      if (rid !== "quickpulse" && nI(S, "quickpulse")) { S.gale += nI(S, "quickpulse"); proc(S, "quickpulse", nameOf(S, "quickpulse") + " +" + nI(S, "quickpulse") + " SPD", depth + 1); }
      fireTrig(S, "t_luck", depth, "rabbit", rid, ctx);
    };
    const rabbitReact = (S, depth, rid, ctx) => {
      const n = nI(S, "rabbit");
      if (n <= 0 || rid === "rabbit") return;
      S.rabbitN = (S.rabbitN || 0) + 1;
      if (!AWv(S, "rabbit") && S.rabbitN % 3 !== 0) return;   /* versus: every 3rd luck signal (long matches would snowball LCK) */
      ctx = ctx || newCtx();
      const sc = scaleOf(ctx, S, "rabbit"), b = Math.round(n * sc);
      if (b > 0) { S.luckT += b; proc(S, "rabbit", nameOf(S, "rabbit") + " +" + b + " LUCK", depth + 1); }
      fireEmit(S, "rabbit", depth, ctx, sc);
    };
    const gainGold = (S, amt, src, depth, rid, ctx) => {
      ctx = ctx || newCtx();
      if (depth > CAPn()) { snap({ t: "fizzle", depth }); if (nI(S, "sparkjar")) S.sparkStored = true; return; }
      if (amt <= 0) { if (rid) snap({ t: "fizzle", depth }); return; }
      if (rid && nI(S, "echobead") && !S.echoUsed) { S.echoUsed = true; amt *= 2; proc(S, "echobead", nameOf(S, "echobead") + " doubles it", depth); }
      let real = amt;
      if (SB(S, "GREED") >= 3) real = Math.round(real * 1.15);
      if (nI(S, "fortunedebt") && !AWv(S, "fortunedebt")) real = Math.round(real * 0.9);
      real = Math.max(1, real);
      if (S._debtLeft > 0) S._debtLeft = Math.max(0, S._debtLeft - real);
      S.gold += real;
      if (S.tag === "A") snap({ t: "gold", amt: real, src, depth, rid });
      else proc(S, rid || "midas", src + " +" + real + "g", depth);
      const n = nI(S, "alchemist");
      if (n > 0) { const sc = scaleOf(ctx, S, "alchemist"); heal(S, Math.round(n * sc), nameOf(S, "alchemist"), depth + 1, "alchemist", ctx); fireEmit(S, "alchemist", depth, ctx, sc); }
      if (rid !== "coinsinger" && nI(S, "coinsinger") && (rid || AWv(S, "coinsinger"))) { const sc3 = scaleOf(ctx, S, "coinsinger"); if (Math.round(sc3) > 0) { emitLuck(S, nameOf(S, "coinsinger"), depth + 1, "coinsinger", ctx); fireEmit(S, "coinsinger", depth, ctx, sc3); } }
      for (const gg of ["butchertithe", "giltrot"]) if (rid && rid !== gg && nI(S, gg)) { const scg = scaleOf(ctx, S, gg); const h = Math.round(nI(S, gg) * scg); if (h > 0) { heal(S, h, nameOf(S, gg), depth + 1, gg, ctx); fireEmit(S, gg, depth, ctx, scg); } }
      S.goldN++; if (S.goldN % 3 === 0) fireTrig(S, "t_gold", depth, "alchemist", rid, ctx);
    };
    const heal = (S, amt, src, depth, rid, ctx) => {
      ctx = ctx || newCtx();
      if (depth > CAPn()) { snap({ t: "fizzle", depth }); if (nI(S, "sparkjar")) S.sparkStored = true; return; }
      const T = other(S);
      const after = [];   /* modifiers adjust now, log after the heal line */
      if (amt > 0) {
        if (rid && rid !== "martyr" && nI(S, "martyr")) { amt += nI(S, "martyr"); after.push(() => proc(S, "martyr", nameOf(S, "martyr") + " +" + nI(S, "martyr"), depth + 1)); }
        if (SB(S, "FLESH") >= 5) amt += 1;
        amt -= nI(T, "faminebell") + (AWv(S, "faminebell") ? 0 : nI(S, "faminebell"));   /* opponent's bell starves you; your own self-taxes unless awakened */
        if (amt > 0 && rid && nI(S, "echobead") && (S.echoN || 0) < (AWv(S, "echobead") ? 2 : 1)) { S.echoN = (S.echoN || 0) + 1; amt *= 2; after.push(() => proc(S, "echobead", nameOf(S, "echobead") + " doubles it", depth + 1)); }
      }
      if (amt <= 0) { if (rid) snap({ t: "fizzle", depth }); return; }
      if (nI(S, "quenched")) { S.qN = (S.qN || 0) + 1; if (AWv(S, "quenched") || S.qN % 3 === 0) { S.qB += nI(S, "quenched"); after.push(() => proc(S, "quenched", nameOf(S, "quenched") + " +" + nI(S, "quenched") + " ATK", depth + 1)); } }
      const real = Math.min(amt, S.pmax - S.php);
      if (rid === "tooth" && AWv(S, "tooth")) { const O = other(S); if (O.pmax > 12) { O.pmax -= 1; if (O.php > O.pmax) O.php = O.pmax; proc(S, "tooth", nameOf(S, "tooth") + " takes 1 max HP", depth + 1); } }
      if (real < amt && nI(S, "chalice") && (S._chaliceG || 0) < (AWv(S, "chalice") ? 20 : 10)) { const cg = AWv(S, "chalice") ? 2 : 1; S.pmax += cg; S._chaliceG = (S._chaliceG || 0) + cg; after.push(() => proc(S, "chalice", nameOf(S, "chalice") + " +" + cg + " max HP", depth + 1)); }
      if (real <= 0) { if (S.tag === "A") snap({ t: "healfull", src, depth, rid }); for (const a of after) a(); return; }
      S.php += real;
      snap(S.tag === "A" ? { t: "heal", amt: real, src, depth, rid } : { t: "eheal", amt: real, depth, rid: rid || "tooth", foe: 1 });
      for (const a of after) a();
      const n = nI(S, "altar");
      if (n > 0 && rid !== "altar") { const sc = scaleOf(ctx, S, "altar"); dealDmg(S, Math.round(2 * n * sc), nameOf(S, "altar"), depth + 1, "altar", ctx); if (AWv(S, "altar")) heal(S, 1, nameOf(S, "altar"), depth + 2, "altar", ctx); fireEmit(S, "altar", depth, ctx, sc); }
      fireTrig(S, "t_heal", depth, "altar", rid, ctx);
    };
    const dealDmg = (S, amt, src, depth, rid, ctx) => {   /* S deals damage to the other side */
      ctx = ctx || newCtx();
      const T = other(S);
      if (depth > CAPn()) { snap({ t: "fizzle", depth }); if (nI(S, "sparkjar")) S.sparkStored = true; return; }
      if (T.php <= 0 || S.php <= 0) return;
      if (amt <= 0) { if (rid) snap({ t: "fizzle", depth }); return; }
      if (rid && nI(S, "echobead") && !S.echoUsed) { S.echoUsed = true; amt *= 2; proc(S, "echobead", nameOf(S, "echobead") + " doubles it", depth); }
      if (depth === 0 && (nI(T, "clover") > 0 || T.benchAbilities) && rng() < lckOf(T) / 100) {   /* evasion lives in the Clover (benchAbilities: harness-only) */
        snap(S.tag === "A" ? { t: "emiss", depth: 0, foe: 1 } : { t: "miss", depth: 0, rid: nI(T, "clover") ? "clover" : null });
        onDodge(T, ctx);
        return;
      }
      if (depth === 0 && nI(T, "trickster") && rng() < Math.min(0.2, 0.1 * nI(T, "trickster"))) { snap(S.tag === "A" ? { t: "emiss", depth: 0, foe: 1 } : { t: "miss", depth: 0, rid: "trickster" }); if (AWv(T, "trickster") && S.php > 0) dealDmg(T, 1, nameOf(T, "trickster"), 1, "trickster", newCtx()); return; }
      if (depth === 0 && SB(T, "GUARD") >= 7 && !T.blocked) { T.blocked = true; proc(T, null, (T.tag === "B" ? T.name + "'s " : "") + "Guard set \u2014 the first blow glances off", 0); return; }
      let real = amt;
      if (depth === 0 && T._stumble) {   /* awakened Stutterstep: the staggered side swings into itself */
        T._stumble = 0;
        proc(T, "stutter", nameOf(T, "stutter") + " \u2014 the foe trips into its own blade", 1);
        if (S.php > 0) dealDmg(T, Math.max(1, amt), nameOf(T, "stutter"), 1, "stutter", newCtx());
        return;
      }
      if (depth === 0 && AWv(T, "martyr") && nI(T, "martyr")) {   /* awakened Martyr's Knot: 1 in 3 blows lands on the striker */
        T._martyrN = (T._martyrN || 0) + 1;
        if (T._martyrN % 3 === 0) {
          proc(T, "martyr", nameOf(T, "martyr") + " bears it \u2014 the blow lands on the foe", 1);
          if (S.php > 0) dealDmg(T, Math.max(1, amt), nameOf(T, "martyr"), 1, "martyr", newCtx());
          return;
        }
      }
      if (depth === 0 && AWv(T, "greed") && nI(T, "greed")) { T.gold = (T.gold || 0) + 2; proc(T, "greed", nameOf(T, "greed") + " charges the purse: +2 gold", 1); }
      if (depth === 0) {                    /* only genuine strikes meet armor + first-hit padding */
        real = applyDef(real, (AWv(S, "whetstone") && nI(S, "whetstone")) ? 0 : defOf(T), T.defModel);
        if (AWv(T, "rustward") || T.php < T.pmax / 2) real -= nI(T, "rustward");
        T._hitN = (T._hitN || 0) + 1;
        if (AWv(T, "hide") && nI(T, "hide") && T._hitN > 1 && T._hideLearn) { real = Math.max(1, Math.round(real * 0.5)); proc(T, "hide", nameOf(T, "hide") + " has learned this blow", 1); }
        if (!T.hitTaken) { T.hitTaken = true; if (AWv(T, "hide") && nI(T, "hide")) T._hideLearn = 1; if (nI(T, "hide")) { real -= 2 * nI(T, "hide"); proc(T, "hide", nameOf(T, "hide") + " softens the blow: -" + (2 * nI(T, "hide")), 1); } }
        real = Math.max(1, real) + (nI(T, "crackedcrown") ? 1 : 0);
      } else real = Math.max(1, real);
      T.php -= real;
      snap(S.tag === "A" ? { t: "edmg", amt: real, src, depth, rid } : { t: "pdmg", amt: real, depth, rid, foe: rid ? 1 : 0, ecrit: ctx._crit && depth === 0 ? 1 : 0 });
      if (rid && rid !== "embercask" && nI(S, "embercask")) { const sc4 = scaleOf(ctx, S, "embercask"); const g4 = Math.round(nI(S, "embercask") * sc4); if (g4 > 0) { gainGold(S, g4, nameOf(S, "embercask"), depth + 1, "embercask", ctx); fireEmit(S, "embercask", depth, ctx, sc4); } }
      if (rid && rid !== "tollplate" && nI(S, "tollplate")) { const sc5 = scaleOf(ctx, S, "tollplate"); const g5 = Math.round(nI(S, "tollplate") * sc5); if (g5 > 0) { gainGold(S, g5, nameOf(S, "tollplate"), depth + 1, "tollplate", ctx); fireEmit(S, "tollplate", depth, ctx, sc5); } }
      if (rid && depth >= 3 && nI(S, "resonantskull") && !ctx.v._res) { ctx.v._res = 1; heal(S, AWv(S, "resonantskull") ? 2 : 1, nameOf(S, "resonantskull"), depth + 1, "resonantskull", ctx); }
      if (T.php <= 0) {                     /* death-defiance ladder, both sides */
        if (SB(T, "FLESH") >= 7 && !T.f7used) { T.f7used = true; T.php = 1; proc(T, null, "Flesh set \u2014 " + (T.tag === "A" ? "you refuse" : T.name + " refuses") + " to fall", 0); }
        else if (nI(T, "soil") && !T._soilUsed) { T._soilUsed = true; T.php = Math.max(1, Math.round(T.pmax * 0.25)); proc(T, "soil", nameOf(T, "soil") + " \u2014 the ground gives back", 0); }
      }
      if (T.php <= 0) {
        if (nI(T, "glassedge")) { if (AWv(T, "glassedge") && !T._glassRef) T._glassRef = true; else T._glassBroken = true; }
        onKill(S, depth, ctx); return;
      }
      if (depth === 0) {                    /* genuine hit reactions on the defender */
        const th = nI(T, "thorns");
        if (th > 0) { const tctx = newCtx(), sc = scaleOf(tctx, T, "thorns"); dealDmg(T, Math.round(2 * th * sc) + (SB(T, "GUARD") >= 5 ? 1 : 0), nameOf(T, "thorns"), 1, "thorns", tctx); if (nI(T, "gildedthorns")) gainGold(T, 2 * nI(T, "gildedthorns"), nameOf(T, "gildedthorns"), 2, "gildedthorns", tctx); fireEmit(T, "thorns", 0, tctx, sc); if (S.php <= 0) return; }
        let a2 = nI(T, "adrenaline");
        if (a2 > 0 && !T.adrenUsed) { T.adrenUsed = true; const hctx = newCtx(), sc = scaleOf(hctx, T, "adrenaline");
          const echoed2 = nI(T, "echobead") && (T.echoN || 0) < (AWv(T, "echobead") ? 2 : 1); if (echoed2) { T.echoN = (T.echoN || 0) + 1; a2 *= 2; }
          T.adrenaline += a2; snap(T.tag === "A" ? { t: "adren", amt: a2, depth: 1, rid: "adrenaline" } : { t: "first", src: nameOf(T, "adrenaline") + " +" + a2 + " ATK", depth: 1, rid: "adrenaline", foe: 1 });
          if (echoed2) proc(T, "echobead", nameOf(T, "echobead") + " doubles it", 2);
          fireEmit(T, "adrenaline", 0, hctx, sc); }
        if (T.php < T.pmax / 2 && nI(T, "marrow")) { heal(T, 2 * nI(T, "marrow"), nameOf(T, "marrow"), 1, "marrow", newCtx()); if (AWv(T, "marrow") && other(T).php > 0) dealDmg(T, 2 * nI(T, "marrow"), nameOf(T, "marrow"), 2, "marrow", newCtx()); }
        if (ctx._crit && nI(T, "mirrorscale") && T.php > 0 && S.php > 0) dealDmg(T, real, nameOf(T, "mirrorscale"), 1, "mirrorscale", newCtx());
        T.painN++; if (T.painN % 3 === 0) fireTrig(T, "t_hit", 0, null, null, newCtx());
      }
    };
    const onDodge = (T, ctx) => {           /* T dodged: its luck engine spins */
      const hctx = newCtx();
      if (nI(T, "clover")) { const sc = scaleOf(hctx, T, "clover"); emitLuck(T, nameOf(T, "clover"), 1, "clover", hctx); fireEmit(T, "clover", 0, hctx, sc); }
      if (nI(T, "horseshoe")) emitLuck(T, nameOf(T, "horseshoe"), 1, "horseshoe", hctx);
      if (nI(T, "clover") && AWv(T, "clover")) emitLuck(T, nameOf(T, "clover"), 1, "clover", hctx);
      if (nI(T, "sentinel")) { const b = nI(T, "sentinel"); T.sent += b; proc(T, "sentinel", nameOf(T, "sentinel") + " +" + b + " DEF", 1); if (AWv(T, "sentinel") && other(T).php > 0) dealDmg(T, 1, nameOf(T, "sentinel"), 1, "sentinel", hctx); }
      if (nI(T, "catwhisker") && (AWv(T, "catwhisker") || !T.catUsed)) { T.catUsed = true; dealDmg(T, Math.max(1, Math.round(atkOf(T) / 2)), nameOf(T, "catwhisker"), 1, "catwhisker", hctx); }
      if (SB(T, "LUCK") >= 7) dealDmg(T, 2, "Luck set", 1, "clover", hctx);
      const O = other(T);
      if (nI(T, "stutter")) { const stg = nI(T, "stutter"); O.base.spd = Math.max(10, (O.base.spd || 25) - stg); if (AWv(T, "stutter")) O._stumble = 1; snap({ t: "eslow", amt: stg, depth: 1, rid: "stutter", foe: T.tag === "B" ? 1 : 0 }); }
      if (nI(T, "haredrum")) T.hgBoost = true;
      fireTrig(T, "t_dodge", 0, "clover", null, hctx);
    };
    const onKill = (S, depth, ctx) => {
      S.kills = (S.kills || 0) + 1;
      if (S.tag === "A") snap({ t: "kill", eidx: B.idx || 0, depth });
      const purse = (S.tag === "A" ? (B.drop || 0) : (A.drop || 0));   /* winner's round purse: 10×round + 15 bonus */
      if (purse > 0) gainGold(S, purse, "loot", 0, null, ctx || newCtx());

      if (nI(S, "headsman")) { S.hdB += nI(S, "headsman"); proc(S, "headsman", nameOf(S, "headsman") + " +" + nI(S, "headsman") + " ATK", 1); }
    };
    const strike = S => {                   /* one genuine attack by S */
      const T = other(S);
      S.trigFired = {};
      S.strikes++;
      S._strikeTot = (S._strikeTot || 0) + 1;
      if (nI(S, "anvil") && S._strikeTot % 10 === 0 && (S._anvilB || 0) < (AWv(S, "anvil") ? 12 : 10)) { S._anvilB = (S._anvilB || 0) + 1; S.defB = (S.defB || 0) + 1; proc(S, "anvil", nameOf(S, "anvil") + " hardens: +1 DEF", 1); }
      if (nI(S, "leechpurse") && S.gold >= (AWv(S, "leechpurse") ? 2 : 3)) { S.gold -= AWv(S, "leechpurse") ? 2 : 3; heal(S, nI(S, "leechpurse") * (AWv(S, "leechpurse") ? 2 : 1), nameOf(S, "leechpurse"), 1, "leechpurse", newCtx()); }
      if (S.strikes === 1 && nI(S, "sparbell")) proc(S, "sparbell", nameOf(S, "sparbell") + " rings: +" + (2 * nI(S, "sparbell")) + " on the opener", 1);
      if (S.strikes === 1 && nI(S, "duelist")) proc(S, "duelist", nameOf(S, "duelist") + " \u2014 worthy blood: +" + (4 * nI(S, "duelist")) + " ATK", 1);
      const strikeBonus = (S.strikes === 1 ? 2 * nI(S, "sparbell") : 0);   /* per-strike only; the ledger carries the rest */
      const tryExecute = () => {
        if (!nI(S, "executioner") || S.exeUsed || T.php <= 0 || T.php > T.pmax * (AWv(S, "executioner") ? 0.15 : 0.1) * Math.min(2, nI(S, "executioner"))) return false;   /* versus: 10%/copy cap 20; awakened 15%/copy cap 30 */
        S.exeUsed = true;
        proc(S, "executioner", nameOf(S, "executioner") + " \u2014 the sentence is carried out", 0);
        dealDmg(S, T.php + defOf(T), nameOf(S, "executioner"), 1, "executioner", newCtx());
        return true;
      };
      if (tryExecute() && T.php <= 0) return;
      if ((nI(S, "dice") > 0 || S.benchAbilities) && rng() < lckOf(S) / 100) {
        const dctx = newCtx(); dctx._crit = true;
        const sc = scaleOf(dctx, S, "dice");
        if (S.tag === "A") snap({ t: "luck", src: nameOf(S, "dice"), depth: 0, rid: "dice" });
        dealDmg(S, Math.round((atkOf(S) + strikeBonus) * (AWv(S, "dice") ? 2 : 1.5)), S.tag === "A" ? nameOf(S, "dice") : null, 0, S.tag === "A" ? "dice" : null, dctx);
        if (nI(S, "gambler") && !S.gamblerUsed) { S.gamblerUsed = true; S.luckT += 8; proc(S, "gambler", nameOf(S, "gambler") + " +8 LUCK", 1); }
        if (SB(S, "LUCK") >= 5) emitLuck(S, "Luck set", 1, null, dctx);
        fireEmit(S, "dice", 0, dctx, sc);
        emitLuck(S, S.tag === "A" ? nameOf(S, "dice") : "dice", 0, "dice", dctx, true);
      } else {
        let amt = atkOf(S) + strikeBonus;
        if (SB(S, "EDGE") >= 5 && S.strikes % 4 === 0) amt = Math.round(amt * 1.5);
        if (S.fEdge) { amt = Math.round(amt * (AWv(S, "fortunesedge") ? 2 : 1.5)); S.fEdge = false; proc(S, "fortunesedge", nameOf(S, "fortunesedge") + " \u2014 charged strike!", 1); }
        dealDmg(S, amt, S.tag === "A" ? "you" : null, 0, null, newCtx());
      }
      if (nI(S, "momentum")) { S.momN = (S.momN || 0) + 1; if (S.momN % (AWv(S, "momentum") ? 2 : 3) === 0) { S.mom += nI(S, "momentum"); if (S.tag === "A") snap({ t: "mom", amt: nI(S, "momentum"), depth: 1, rid: "momentum" }); } }
      if (tryExecute() && T.php <= 0) return;
      if (S.sparkStored && T.php > 0 && S.php > 0) { S.sparkStored = false; dealDmg(S, AWv(S, "sparkjar") ? 4 : 2, nameOf(S, "sparkjar"), 1, "sparkjar", newCtx()); }
      if (SB(S, "PACE") >= 7 && S.strikes % 5 === 0 && T.php > 0 && S.php > 0) { proc(S, null, "Pace set \u2014 a second strike!", 0); dealDmg(S, atkOf(S), S.tag === "A" ? "you" : null, 0, null, newCtx()); }
      if (T.php > 0 && S.php > 0) {
        const actx = newCtx();
        const cp = nI(S, "cutpurse");
        if ((cp > 0 && AWv(S, "cutpurse")) || rng() < lckOf(S) * (cp > 0 ? 2 : 1) / 100) {   /* awakened Hook: every strike spills */
          if (cp > 0 && S.tag === "A") snap({ t: "luck", src: "loose coins", depth: 0, rid: "cutpurse" });
          const sc = cp > 0 ? scaleOf(actx, S, "cutpurse") : 1;
          gainGold(S, 1 + (cp > 0 ? Math.round(cp * sc) : 0) + (SB(S, "GREED") >= 5 ? 1 : 0), cp > 0 ? nameOf(S, "cutpurse") : "loose coins", 1, cp > 0 ? "cutpurse" : null, actx);
          if (cp > 0) {
            fireEmit(S, "cutpurse", 0, actx, sc);
            emitLuck(S, "loose coins", 0, "cutpurse", actx, true);
          }
        }
        if (S.strikes > 1 && nI(S, "serrated")) { const sc2 = scaleOf(actx, S, "serrated"); dealDmg(S, Math.max(1, Math.round(nI(S, "serrated") * sc2)), nameOf(S, "serrated"), 1, "serrated", actx); fireEmit(S, "serrated", 0, actx, sc2); }
        const n = nI(S, "tooth");
        if (n > 0 && T.php > 0) { const sc = scaleOf(actx, S, "tooth"); heal(S, Math.round(n * sc), nameOf(S, "tooth"), 1, "tooth", actx); fireEmit(S, "tooth", 0, actx, sc); }
        S.strikeN++; if (S.strikeN % 3 === 0) fireTrig(S, "t_attack", 0, "tooth", null, actx);
      }
    };
    /* ---- the duel ---- */
    A.tag = "A"; B.tag = "B"; resetFight(A); resetFight(B);
    for (const S of [A, B]) if (SB(S, "FLESH") >= 3 && !S._flesh3) { S._flesh3 = true; S.pmax += 3; S.php = Math.min(S.pmax, S.php + 3); proc(S, null, (S.tag === "B" ? S.name + "'s " : "") + "Flesh set \u2014 +3 max HP", 0); }
    snap({ t: "enter", eidx: B.idx || 0, depth: 0, another: false });
    const dashA = nI(A, "dash") > 0, dashB = nI(B, "dash") > 0;
    A.trigFired = {}; B.trigFired = {};
    for (const S of [A, B]) if (nI(S, "relaytotem")) for (let rl = 0; rl < (AWv(S, "relaytotem") ? 2 : 1); rl++) {   /* Relay Totem: every fight relays one relic (awakened: two) */
      const others = S.items.filter(x => x !== "relaytotem");
      if (others.length) { const rid2 = others[Math.floor(rng() * others.length)]; proc(S, "relaytotem", nameOf(S, "relaytotem") + " relays " + this.itemName(rid2), 0); nativeEffect(S, rid2, 0, newCtx(), 1, true); }
    }
    fireTrig(A, "t_floor", 0, null, null, newCtx()); fireTrig(B, "t_floor", 0, null, null, newCtx());
    fireTrig(A, "t_fight", 0, null, null, newCtx()); fireTrig(B, "t_fight", 0, null, null, newCtx());
    const GAUGE = 100;
    let gA = (dashA && !dashB) ? 0 : GAUGE, gB = (dashB && !dashA) ? 0 : GAUGE;
    if (gA > 0 && nI(A, "cavalry")) snap({ t: "first", src: nameOf(A, "cavalry") + " \u2014 the charge starts early", depth: 0, rid: "cavalry" });
    if (gA > 0) gA = Math.max(0, gA - (SB(A, "PACE") >= 5 ? 25 : 0) - (nI(A, "cavalry") ? (AWv(A, "cavalry") ? 60 : 30) : 0));
    if (gB > 0) gB = Math.max(0, gB - (SB(B, "PACE") >= 5 ? 25 : 0) - (nI(B, "cavalry") ? (AWv(B, "cavalry") ? 60 : 30) : 0));
    if (dashA && dashB) snap({ t: "first", src: "Both dash \u2014 the openers cancel!", depth: 0 });
    else if (dashA) snap({ t: "dashopen", src: this.itemName("dash"), depth: 0, rid: "dash" });
    else if (dashB) snap({ t: "edashopen", depth: 0, rid: "dash", foe: 1 });
    let guard = 0;
    while (A.php > 0 && B.php > 0 && guard++ < 600) {
      if (gB <= 0 && gA > 0) { strike(B); if (A.php <= 0) break; gB += GAUGE; if (A.hgBoost) { gA = 0; A.hgBoost = false; proc(A, "haredrum", nameOf(A, "haredrum") + " \u2014 instant riposte!", 0); } }
      else if (gA <= 0 && gB > 0) { strike(A); if (B.php <= 0 || A.php <= 0) break; gA += GAUGE; if (B.hgBoost) { gB = 0; B.hgBoost = false; proc(B, "haredrum", nameOf(B, "haredrum") + " \u2014 instant riposte!", 0); } }
      else if (gA <= 0 && gB <= 0) { strike(B); if (A.php <= 0) break; gB += GAUGE; strike(A); if (B.php <= 0 || A.php <= 0) break; gA += GAUGE; }
      else {
        if (AWv(A, "boots") && nI(A, "boots") && (A.strikes || 0) > 0 && (A.strikes || 0) % 4 === 0 && A._bootsUsed !== (A.strikes || 0)) { A._bootsUsed = (A.strikes || 0); gA = 0; continue; }
        if (AWv(B, "boots") && nI(B, "boots") && (B.strikes || 0) > 0 && (B.strikes || 0) % 4 === 0 && B._bootsUsed !== (B.strikes || 0)) { B._bootsUsed = (B.strikes || 0); gB = 0; continue; }
        const sa = spdOf(A), sb = spdOf(B), step = Math.min(Math.ceil(gA / sa), Math.ceil(gB / sb)); gA -= sa * step; gB -= sb * step; tk += step;
      }
    }
    if (A.php <= 0) snap({ t: "death", depth: 0 });
    ev._carry = { furyB: A.fury, stoneB: A.stone, galeB: A.gale, luckB: A.luckT, strikeN: A.strikeN, painN: A.painN, goldN: A.goldN, stoneCnt: A.stoneCnt, f7used: A.f7used, titheDef: 0, exeUsed: A.exeUsed, gamblerUsed: A.gamblerUsed, hdB: A.hdB, sparkStored: A.sparkStored };
    ev._carryB = { _strikeTot: B._strikeTot || 0, _flesh3: B._flesh3, adrenaline: B.adrenaline, gold: B.gold, pmax: B.pmax, _anvilB: B._anvilB, _chaliceG: B._chaliceG, _debtLeft: B._debtLeft, _glassBroken: B._glassBroken, _soilUsed: B._soilUsed, kills: B.kills };
    return ev;
  }

  lineFor(e) {
    const en = i => (this.G && this.G.mode === "versus" && this.G.vsFoeName) ? this.G.vsFoeName : this.t("en" + i);
    switch (e.t) {
      case "enter": return { k: "enter", text: this.t("logEnter", { i: "", n: en(e.eidx) }).trim() };
      case "edmg": return e.src === "you" ? { k: "you", text: this.t("logYouStrike", { n: e.amt }) } : { k: "chain", text: this.t("logSrcDeals", { s: e.src, n: e.amt }) };
      case "emiss": return { k: "dim", text: en(e.eidx) + " sidesteps your strike!" };
      case "eheal": return { k: "echain", text: "\u2665 " + (this.G && this.G.vsFoeName ? this.G.vsFoeName : "Rival") + "'s " + this.itemName("tooth") + " heals " + e.amt };
      case "eslow": return { k: "chain", text: this.itemName("stutter") + " staggers the foe: -" + e.amt + " SPD" };
      case "mom": return { k: "chain", text: this.itemName("momentum") + " rolls: +" + e.amt + " SPD" };
      case "efury": return { k: "echain", text: (this.G && this.G.vsFoeName ? this.G.vsFoeName : "Rival") + "'s " + this.itemName("berserk") + " rages: +" + e.amt + " ATK" };
      case "pdmg": return e.foe ? { k: "echain", text: (this.G && this.G.vsFoeName ? this.G.vsFoeName : "Rival") + "'s " + this.itemName("thorns") + " bites for " + e.amt } : (e.ecrit ? { k: "hurt", text: en(e.eidx) + " lands a CRITICAL hit for " + e.amt + "!" } : { k: "hurt", text: this.t("logHitYou", { n: en(e.eidx), a: e.amt }) });
      case "miss": return { k: "good", text: this.t("logMiss", { n: en(e.eidx) }) };
      case "first": return { k: e.foe ? "echain" : "chain", text: e.src };
      case "dashopen": return { k: "good", text: e.src + " — first strike!" };
      case "edashopen": return { k: "hurt", text: (this.G && this.G.vsFoeName ? this.G.vsFoeName : "The rival") + "'s " + this.itemName("dash") + " — they strike first!" };
      case "beat": return { k: "dim", text: "" };
      case "heal": return { k: "good", text: e.src === "you" ? this.t("logHeal", { n: e.amt }) : this.t("logSrcHeals", { s: e.src, n: e.amt }) };
      case "healfull": return { k: "dim", text: this.t("logFull", { s: e.src }) };
      case "gold": return { k: "gold", text: this.t("logGold", { n: e.amt, s: e.src }) };
      case "kill": return { k: "kill", text: this.t("logKill", { n: en(e.eidx) }) };
      case "pact": return { k: "hurt", text: this.itemName("bloodpact") + " takes " + e.amt + " HP \u2014 the pact holds" };
      case "adren": return { k: "chain", text: this.t("logAdren", { n: e.amt }) };
      case "breath": return { k: "good", text: this.t("logBreath", { n: e.amt }) };
      case "fizzle": return { k: "dim", text: this.t("logFizzle") };
      case "luck": return { k: "chain", text: "\u2726 " + e.src + ": a lucky break" };
      case "death": return { k: "death", text: this.t("logDeath") };
      default: return { k: "dim", text: "" };
    }
  }

  tierPack(floor) {  /* delve pack scaled to the chosen dungeon: tougher, better-paying, and the boss carries that dungeon's relic kit */
    const G = this.G, D = DUNGEONS[Math.max(0, Math.min(DUNGEONS.length - 1, (G.tier || 1) - 1))], M = D.mult;
    const pk = packFor(floor, G.rng, { bossRelics: D.bossRelics, ghoolemBoss: D.ghoolemBoss });
    if (M > 1) for (const e of pk) { e.hp = Math.round(e.hp * M); if (e.max) e.max = Math.round(e.max * M); e.atk = Math.round(e.atk * M); e.drop = Math.round((e.drop || 0) * M); }
    return pk;
  }
  fight(pre, skipIntro, carry) {
    const G = this.G, pack = pre || (G.mode === "versus" ? this.rivalPack(G.floor) : this.tierPack(G.floor));
    if (G.mode === "versus" && G.vsNextFoe != null) {   /* lock this fight's foe: name + relics snapshot survives round resolution */
      G.vsFoe = G.vsNextFoe; G.vsNextFoe = null;
      const b = G.vsRoster[G.vsFoe];
      G.vsFoeName = b.name; G.vsFoeLvl = b.lvl; G.vsFoeRelics = b.relics.slice();
      G.duelHall = this.hallForDuel(G, G.vsFoe);
      try { G.vsFoeAnim = composeHeroState(b.profile.combo, "idle"); } catch (e) { G.vsFoeAnim = null; }   /* composed once per fight; idleF picks the frame */
    }
    this._skipIntro = !!skipIntro;
    const railPack = (carry && G.pack && G.pack.length > pack.length) ? G.pack : pack;   /* the rail keeps the floor's index space; the engine gets the remaining foes */
    G.pack = pack;
    if (G.pending && G.pending.floor === G.floor) { this._pg = G.pending; G.pending = null; G.gold += this._pg.gold; } else this._pg = null;
    const sim = { _strikeTot: G._strikeTot || 0, mode: G.mode, awake: awakeById(G.items, G.awake), php: G.php, pmax: G.pmax, gold: G.gold, items: G.items, adrenaline: G.adrenaline, midasBonus: G.midasBonus, kills: G.kills, defB: G.defB, spdB: G.spdB, atkB: G.atkB, luckB: G.luckB || 0, sockets: G.sockets || {}, furyB: carry ? carry.furyB || 0 : 0, _carry: carry || null, baseAtk: G.baseAtk, baseDef: G.baseDef, baseSpd: G.baseSpd, baseLck: G.baseLck, _anvilB: G._anvilB || 0, _chaliceG: G._chaliceG || 0, _debtLeft: G._debtLeft || 0, _glassBroken: !!G._glassBroken, _soilUsed: !!G._soilUsed };
    let events;
    if (G.mode === "versus") {   /* symmetric duel: both sides are full players */
      const b = (G.vsRoster && G.vsRoster[G.vsFoe]) || null, e0 = pack[0], bc = (b && b.duel) || {};
      const Aside = { name: "you", drop: 15, awake: awakeById(G.items, G.awake), base: { hp: G.pmax, atk: 5, def: G.baseDef || 0, spd: G.baseSpd != null ? G.baseSpd : 25, lck: G.baseLck != null ? G.baseLck : 10 }, php: G.php, pmax: G.pmax, gold: G.gold, items: G.items.slice(), sockets: Object.assign({}, G.sockets || {}), adrenaline: G.adrenaline, midasBonus: G.midasBonus || 0, atkB: G.atkB || 0, defB: G.defB || 0, spdB: G.spdB || 0, luckB: G.luckB || 0, kills: G.kills, _anvilB: G._anvilB || 0, _chaliceG: G._chaliceG || 0, _debtLeft: G._debtLeft || 0, _glassBroken: !!G._glassBroken, _soilUsed: !!G._soilUsed, _flesh3: !!G._flesh3 };
      const Bside = { name: (b && b.name) || "Rival", drop: e0.drop || 0, idx: e0.idx, variant: e0.variant || 0, rank: e0.rank || "boss", awake: (b && b.awake) || {},
        base: b ? b.base : { hp: e0.hp, atk: e0.atk, def: e0.armor || 0, spd: e0.spd || 25, lck: e0.lck || 10 },
        php: (b && b.phpOv > 0) ? b.phpOv : Math.max(e0.hp, (bc.pmax || 0)), pmax: (b && b.pmaxOv > 0) ? b.pmaxOv : Math.max(e0.hp, (bc.pmax || 0)), gold: bc.gold || 0, items: (b ? b.relics : (e0.relics || [])).slice(), sockets: Object.assign({}, (b && b.sockets) || {}),
        adrenaline: bc.adrenaline || 0, midasBonus: (b && b.midasBonus) || 0, atkB: (b && b.atkB) || 0, defB: (b && b.defB) || 0, spdB: (b && b.spdB) || 0, luckB: (b && b.luckB) || 0,
        kills: bc.kills || 0, _anvilB: bc._anvilB || 0, _chaliceG: bc._chaliceG || 0, _debtLeft: bc._debtLeft != null ? bc._debtLeft : ((b && b.debtLeft) || 0), _glassBroken: !!bc._glassBroken, _soilUsed: !!bc._soilUsed, _flesh3: !!bc._flesh3 };
      Aside._strikeTot = G._strikeTot || 0; Bside._strikeTot = (bc._strikeTot || 0);
      events = this.simulateDuel(Aside, Bside, G.rng);
      if (b) b.duel = events._carryB;   /* the rival's persistent state survives to their next fight */
      sim.php = Aside.php; sim.pmax = Aside.pmax; sim.gold = Aside.gold; sim.adrenaline = Aside.adrenaline; sim.kills = Aside.kills; sim._strikeTot = Aside._strikeTot || 0; sim.defB = Aside.defB != null ? Aside.defB : sim.defB;
      G._anvilB = Aside._anvilB; G._chaliceG = Aside._chaliceG; G._debtLeft = Aside._debtLeft; G._glassBroken = Aside._glassBroken; G._soilUsed = Aside._soilUsed; G._flesh3 = Aside._flesh3;
    } else events = this.simulateFloor(sim, pack, G.rng);
    G.floorHist = G.floorHist || {};
    if (!G.floorHist[G.floor]) G.floorHist[G.floor] = { foes: [], log: [] };
    if (!carry) G.floorHist[G.floor].foes = pack.map(e2 => ({ idx: e2.idx, variant: e2.variant || 0, rank: e2.rank || "guard", hp: e2.hp, atk: e2.atk, armor: e2.armor || 0, spd: e2.spd || 25, lck: e2.lck || 10, relics: (e2.relics || []).slice() }));
    this._lastCarry = events._carry;   /* floor-scoped state at death, for revive continuity */
    for (let i = events.length - 1; i >= 0; i--) if (events[i].t === "enter" || events[i].t === "kill") events.splice(i + 1, 0, Object.assign({}, events[i], { t: "beat" }));
    if (this._pg) events.splice(1, 0, { t: "gold", amt: this._pg.gold, src: this._pg.src, depth: 0, php: G.php, ehp: pack[0].hp, gold: G.gold, emax: pack[0].hp, eatk: pack[0].atk, eidx: pack[0].idx, evar: pack[0].variant || 0, erank: pack[0].rank || "guard", earm: pack[0].armor || 0, espd: pack[0].spd || 25, padr: G.adrenaline });
    if (G.breathHealed > 0) {
      events.splice(1, 0, { t: "breath", amt: G.breathHealed, depth: 0, php: G.php, ehp: pack[0].hp, gold: G.gold, emax: pack[0].hp, eatk: pack[0].atk, eidx: pack[0].idx, evar: pack[0].variant || 0, erank: pack[0].rank || "guard", earm: pack[0].armor || 0, espd: pack[0].spd || 25, padr: G.adrenaline });
      if (count(G.items, "stomach")) events.splice(2, 0, { t: "first", src: this.itemName("stomach") + " doubles the breather", depth: 1, rid: "stomach", php: G.php, ehp: pack[0].hp, gold: G.gold, emax: pack[0].hp, eatk: pack[0].atk, eidx: pack[0].idx, evar: pack[0].variant || 0 });
      G.breathHealed = 0;
    }
    const reviveLines = ["you claw back out of the dark — the dust looks disappointed", "death files a complaint; you keep walking", "the reaper blinks first — back on your feet", "not today: you spit out the grave dirt and stand", "the Hoard-King's ledger marks you 'pending' — you rise anyway"];
    this.setState({ stage: "combat", paused: false, walk: 0,
      log: (carry ? this.state.log.concat({ text: "☠ the dark takes you…", k: "hurt", depth: 0 }, { text: "✦ " + reviveLines[Math.floor(Math.random() * reviveLines.length)], k: "heal", depth: 0 }).slice(-60) : []).concat(this.G && this.G._duelNote ? [{ text: "⚔ " + this.G._duelNote, k: "dim", depth: 0 }] : []),
      packDone: carry ? (this.state.packDone || 0) : 0,
      packSize: carry ? (this.state.packSize || pack.length) : pack.length,   /* keep the floor's hall length so the pan resumes mid-hall */
      hallStep: carry ? (this.state.hallStep || 0) : 0, hallSnap: carry ? 1 : 0, hBarDur: 0, eBarDur: 0, stoneB: 0, galeB: 0, dyingE: 0, dyingH: 0, dyingBoss: false, bloodE: [], bloodH: [], dustE: [], dustH: [], pack: railPack.map(e => ({ idx: e.idx, rank: e.rank || "guard", variant: e.variant || 0 })), enemy: null, fly: null });
    if (this.G) delete this.G._duelNote;
    const reduced = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const sbm = this._sbMul || 1;
    const step = Math.round((reduced ? 40 : (events.length > 34 ? 700 : 1000)) / sbm);
    this._stepBase = step * sbm;
    this._mspt = Math.round((reduced ? 0 : (events.length > 34 ? 350 : 500)) / sbm);
    this._evq = events; this._evi = 0; this._step = step; this._reduced = reduced; this._sim = sim; this._walked = -1;
    this._ptk = (this._ptk || 0) + 1;
    if (G.sandbox || this.state.sbOn) {   /* sandbox flight recorder: one log per run (a new seed starts a fresh recording) */
      if (!this._sbLog || this._sbLog.meta.seed !== G.seed || this._sbLog.meta.mode !== (G.mode || "delve")) this._sbLog = { meta: { started: new Date().toISOString(), seed: G.seed, mode: G.mode || "delve" }, fights: [] };
      this._sbLog.fights.push({
        at: new Date().toISOString(), floor: G.floor, mode: G.mode || "delve",
        player: { hp: G.php, maxHp: G.pmax, gold: G.gold, atk: currentAtk(sim), adrenaline: G.adrenaline, midas: G.midasBonus || 0,
          bonuses: { atkB: G.atkB || 0, defB: G.defB || 0, spdB: G.spdB || 0, luckB: G.luckB || 0 },
          base: { atk: G.baseAtk != null ? G.baseAtk : 5, def: G.baseDef || 0, spd: G.baseSpd != null ? G.baseSpd : 25, lck: G.baseLck != null ? G.baseLck : 10 },
          counters: { anvil: G._anvilB || 0, chalice: G._chaliceG || 0, debt: G._debtLeft || 0, glassBroken: !!G._glassBroken, soilUsed: !!G._soilUsed, kills: G.kills },
          relics: G.items.slice(), sockets: Object.assign({}, G.sockets || {}) },
        opponents: pack.map(e => ({ rank: e.rank, hp: e.hp, atk: e.atk, def: e.armor || 0, spd: e.spd || 25, lck: e.lck || 10, drop: e.drop, relics: (e.relics || []).slice(),
          name: G.mode === "versus" ? G.vsFoeName : undefined, lvl: G.mode === "versus" ? G.vsFoeLvl : undefined })),
        events: events.map(e => Object.assign({}, e)),
        combatLog: events.map(e => { try { const L = this.lineFor(e); return L && L.text ? (e.depth > 0 ? "\u21b3 ".repeat(Math.min(3, e.depth)) + L.text : L.text) : null; } catch (er) { return null; } }).filter(Boolean)
      });
    }
    this.playNext();
  }

  schedNext(ms) {   /* single-flight: only the LATEST scheduled chain may proceed */
    const seq = (this._pnSeq = (this._pnSeq || 0) + 1);
    this.later(() => { if (seq === this._pnSeq) this.playNext(this._ptk); }, ms);
  }
  playNext(tok) {
    const G = this.G, sim = this._sim, events = this._evq, reduced = this._reduced, step = this._step;
    if (tok != null && tok !== this._ptk) return;   /* a stale chain from an older fight/pause cycle */
    if (!events || this._evi >= events.length) return;
    const _tok = this._ptk;
    if (window.__pnLog) window.__pnLog.push({ tk: _tok, i: this._evi, t: Date.now() % 100000, e: events[this._evi] && events[this._evi].t });
    if ((this.state.modal || this.state.paused) && !this._sbStepOnce) { this.schedNext(180); return; }   /* inspecting a card or the pause button halts the fight */
    this._sbStepOnce = false;
    const i = this._evi++;
    const e = events[i];
    if (e.t === "enter" && e.eidx != null && (!this.G || this.G.mode !== "versus")) {   /* bestiary: mark this species as met */
      const seen = Store.get("dd.seen", {}) || {};
      if (!seen[e.eidx]) { seen[e.eidx] = 1; Store.set("dd.seen", seen); }
    }
    // walk down the hall to the next foe (the first one included: out of the left door)
    if (e.t === "enter" && !reduced && !this._skipIntro && this._walked !== i) {
      this._walked = i; this._evi = i;
      this.setState(s2 => ({ walk: (s2.walk || 0) + 1, hallStep: (s2.hallStep || 0) + 1, hallSnap: 0 }));
      this.later(() => { this.setState({ walk: 0 }); this.schedNext(0); }, 3350);
      return;
    }
    {
      {
        const L = this.lineFor(e);
        if (L.text && G.floorHist && G.floorHist[G.floor]) { const fl2 = G.floorHist[G.floor].log; fl2.push({ text: (e.depth > 0 ? "\u21b3 " : "") + L.text, k: L.k, depth: e.depth || 0 }); if (fl2.length > 240) fl2.splice(0, fl2.length - 240); }
        const stp = this._step, evq = this._evq, ii = i;
        const isHA = x => (x.t === "edmg" && x.src === "you") || x.t === "emiss";
        const isEA = x => (x.t === "pdmg" && !(x.depth > 0)) || x.t === "miss";
        const gapTo = pred => { let acc = 0; for (let j = ii + 1; j < evq.length; j++) { acc += this.evDelay(evq, j - 1); if (evq[j].t === "enter") break; if (pred(evq[j])) return acc; } return 0; };
        const dashOpens = (pred, tok) => { for (let j = ii + 1; j < evq.length; j++) { if (evq[j].t === "enter") break; if (evq[j].t === (tok || "dashopen")) return true; if (pred(evq[j])) return false; } return false; };
        /* gapTo returns 0 when the unit never attacks again this fight (it dies first) — keep the gauge
           filling at its established cadence (-1 = "restart with the previous duration") instead of
           freezing it, so a doomed unit still looks like it is winding up. */
        const barH0 = isHA(e) || e.t === "enter" || e.t === "edashopen" ? (e.t === "enter" && dashOpens(isHA) ? 0 : gapTo(isHA)) : null;
        const barE0 = isEA(e) || e.t === "enter" || e.t === "dashopen" ? (e.t === "enter" && (dashOpens(isEA) || dashOpens(isEA, "edashopen")) ? 0 : gapTo(isEA)) : null;
        const barH = (barH0 === 0 && isHA(e)) ? -1 : barH0;
        const barE = (barE0 === 0 && isEA(e)) ? -1 : barE0;
        G.php = Math.min(e.php, G.pmax || e.php); G.gold = e.gold;
        const pid = "p" + this._ptk + "_" + i;   /* deterministic: one flier per event per playback chain */
        const isCrit = e.t === "edmg" && e.rid === "dice" && !e.depth;
        let popE = e.t === "edmg" ? { id: pid, v: isCrit ? "CRIT! -" + e.amt : "-" + e.amt, crit: isCrit, chain: !isCrit && e.src !== "you", x: 26 + Math.random() * 48, y: -22 + Math.random() * 14 } : (e.t === "emiss" ? { id: pid, v: "MISS", chain: true, x: 26 + Math.random() * 48, y: -22 + Math.random() * 14 } : (e.t === "eheal" ? { id: pid, v: "+" + e.amt, heal: true, x: 26 + Math.random() * 48, y: -22 + Math.random() * 14 } : (e.t === "efury" ? { id: pid, v: "+" + e.amt + " ATK", heal: true, x: 26 + Math.random() * 48, y: -22 + Math.random() * 14 } : (e.t === "eslow" ? { id: pid, v: "-" + e.amt + " SPD", chain: true, x: 26 + Math.random() * 48, y: -22 + Math.random() * 14 } : null))));
        let popH = (e.t === "pdmg") ? { id: pid, v: (e.ecrit ? "CRIT! -" : "-") + e.amt, kind: "hurt" }
          : (e.t === "miss" ? { id: pid, v: "MISS", kind: "miss" }
          : ((e.t === "heal" || e.t === "breath") ? { id: pid, v: "+" + e.amt, kind: "heal" }
          : (e.t === "adren" ? { id: pid, v: "+" + e.amt + " ATK", kind: "buff" }
          : (e.t === "pact" ? { id: pid, v: "-" + e.amt + " HP", kind: "hurt" }
          : (e.t === "mom" ? { id: pid, v: "+" + e.amt + " SPD", kind: "buff" } : null)))));
        if (popH) { popH.x = 26 + Math.random() * 48; popH.y = -22 + Math.random() * 14; }
        let popG = e.t === "gold" ? { id: pid, v: "+" + e.amt, x: 10 + Math.random() * 70, y: -26 + Math.random() * 18 } : null;
        if (e.t === "gold") SFX.gold();
        else if (e.t === "pdmg") SFX.hurt();
        else if (e.t === "edmg") SFX.hit();
        else if (e.t === "heal" || e.t === "breath") SFX.heal();
        else if (e.t === "kill") SFX.kill();
        else if (e.t === "death") SFX.death();
        const MODS = e.pl != null ? e.pl : (this.state.mods || []);
        const msum = (stat, f) => MODS.reduce((a2, m) => a2 + (m.stat === stat && (!f || f(m)) ? m.amt : 0), 0);
        this.setState(s => ({
          log: L.text ? s.log.concat({ text: (e.depth > 0 ? "↳ " : "") + L.text, k: L.k, depth: e.depth || 0 }).slice(-60) : s.log,
          php: e.php, gold: e.gold, mods: MODS,
          atk: heroStatOf({ items: G.items, awake: awakeById(G.items, G.awake), mode: G.mode, base: { atk: G.baseAtk, def: G.baseDef, spd: G.baseSpd, lck: G.baseLck }, run: { adrenaline: e.padr != null ? e.padr : sim.adrenaline, midasBonus: G.midasBonus || 0, atkB: G.atkB || 0, defB: G.defB || 0, spdB: G.spdB || 0, luckB: G.luckB || 0 }, php: e.php, pmax: G.pmax, gold: e.gold, floor: G.floor || 0, foeRank: e.erank || (s.enemy && s.enemy.rank), debtLeft: G._debtLeft || 0, glassBroken: G._glassBroken, mods: MODS }, "atk"),
          furyB: msum("atk", m => /Fury/.test(m.src)),
          enemy: { idx: e.eidx, hp: e.ehp, max: e.emax, atk: e.eatk, rank: e.erank || "guard", armor: e.earm || 0, spd: e.espd || 25, lck: e.elck || 10, variant: e.evar || 0, relics: e.erel || (s.enemy && s.enemy.idx === e.eidx && s.enemy.relics) || undefined, fury: e.t === "efury" ? 2 : (s.enemy && s.enemy.fury) || 0 },
          stoneB: msum("def"), galeB: msum("spd"), luckB: msum("lck"), sentB: msum("def", m => /Sentinel/.test(m.src)), pqB: msum("atk", m => /Quenched|Headsman/.test(m.src)), cnt: e.pcnt || s.cnt,
          popsE: (popE && !s.popsE.some(p => p.id === popE.id)) ? s.popsE.concat(popE) : s.popsE,
          popsH: (popH && popH.kind !== "miss" && !s.popsH.some(p => p.id === popH.id)) ? s.popsH.concat(popH) : s.popsH,
          hitE2: (e.t === "pdmg" || e.t === "miss") ? (s.hitE2 || 0) + 1 : (s.hitE2 || 0),
          hitE: popE ? s.hitE + 1 : s.hitE,
          hitH: popH && popH.kind === "hurt" ? s.hitH + 1 : (e.t === "edmg" && e.src === "you" ? s.hitH + 1 : s.hitH),
          heroFxKind: e.t === "edmg" && e.src === "you" ? "atk" : (popH && popH.kind === "hurt" ? "hit" : s.heroFxKind),
          enemyFxKind: popE ? "hit" : ((e.t === "pdmg" || e.t === "miss") ? "atk" : s.enemyFxKind),
          hitG: popG ? s.hitG + 1 : s.hitG,
          packDone: e.t === "kill" ? s.packDone + 1 : s.packDone,
          dyingE: e.t === "kill" ? (s.dyingE || 0) + 1 : (e.t === "enter" ? 0 : s.dyingE),
          dyingBoss: e.t === "kill" ? (e.erank === "boss" || e.erank === "king") : s.dyingBoss,
          dyingH: e.t === "death" ? 1 : s.dyingH,
          bloodE: e.t === "kill" ? Array.from({ length: 8 }, (_, k) => ({ id: pid + "b" + k, dx: (Math.random() * 72 - 36).toFixed(0), dy: (Math.random() * 56 - 16).toFixed(0), d: (Math.random() * 0.15).toFixed(2) })) : s.bloodE,
          bloodH: e.t === "death" ? Array.from({ length: 8 }, (_, k) => ({ id: pid + "b" + k, dx: (Math.random() * 72 - 36).toFixed(0), dy: (Math.random() * 56 - 16).toFixed(0), d: (Math.random() * 0.15).toFixed(2) })) : s.bloodH,
          dustE: (e.t === "edmg" && !(e.depth > 0)) ? (s.dustE || []).slice(-8).concat(Array.from({ length: 4 }, (_, k) => ({ id: pid + "d" + k, dx: (Math.random() * 56 - 28).toFixed(0), dy: (-(6 + Math.random() * 26)).toFixed(0), d: 0 }))) : (e.t === "enter" ? [] : s.dustE),
          dustH: e.t === "pdmg" ? (s.dustH || []).slice(-8).concat(Array.from({ length: 4 }, (_, k) => ({ id: pid + "d" + k, dx: (Math.random() * 56 - 28).toFixed(0), dy: (-(6 + Math.random() * 26)).toFixed(0), d: 0 }))) : s.dustH,
          popsG: (popG && !s.popsG.some(p => p.id === popG.id)) ? s.popsG.concat(popG) : s.popsG,
          pulse: (e.rid && !e.foe) ? { id: e.rid, idx: e.ridx != null ? e.ridx : null } : null,
          foePulse: (e.rid && e.foe) ? e.rid : null,
          hBarDur: barH === null ? s.hBarDur : (barH === -1 ? (s.hBarDur || 900) : barH), hBarK: barH === null ? s.hBarK : (s.hBarK || 0) + 1,
          eBarDur: barE === null ? s.eBarDur : (barE === -1 ? (s.eBarDur || 900) : barE), eBarK: barE === null ? s.eBarK : (s.eBarK || 0) + 1
        }));
        const stampT = Date.now(); if (popE) popE.ts = stampT; if (popH) popH.ts = stampT; if (popG) popG.ts = stampT;
        /* sweep a lane only when EVERY flier in it has expired: removing one mid-flight reindexes the
           list and can remount/restart the survivors' animations (the visible "reset" glitch) */
        const sweep = key => this.setState(s2 => { const now2 = Date.now(); const arr = s2[key]; if (!arr.length || arr.some(p => now2 - (p.ts || 0) < 1250)) return null; return { [key]: [] }; });
        if (popE) this.later(() => sweep("popsE"), 1300);
        if (popH) this.later(() => sweep("popsH"), 1300);
        if (popH && popH.kind === "miss") this.later(() => this.setState(s2 => ({ popsH: s2.popsH.some(p => p.id === popH.id) ? s2.popsH : s2.popsH.concat(Object.assign({}, popH, { ts: Date.now() })) })), 260);
        if (popG) this.later(() => sweep("popsG"), 1300);
        if (i === events.length - 1) {
          this.later(() => {
            G.adrenaline = sim.adrenaline; G.kills = sim.kills;   /* native first-hit proc is permanent; only socketed activations are floor-scoped */
            G.defB = sim.defB; G.pmax = sim.pmax;   /* Anvil DEF + Chalice max-HP growth persist */
            G._anvilB = sim._anvilB || 0; G._strikeTot = sim._strikeTot || 0; G._chaliceG = sim._chaliceG || 0; G._debtLeft = sim._debtLeft || 0; G._glassBroken = !!sim._glassBroken; G._soilUsed = !!sim._soilUsed;
            if (G.mode === "versus") return this.versusResolve();
            if (G.php <= 0) {
              if (!G.revived) return this.syncHUD({ stage: "revive" });
              return this.finishRun("died");
            }
            if (G.floor >= MAX_FLOOR) { G.gold += 50; return this.finishRun("cleared"); }
            G.nextPack = this.tierPack(G.floor + 1);
            if (reduced) return this.syncHUD({ stage: "decide" });
            this.setState(s2 => ({ walk: (s2.walk || 0) + 1, hallStep: (s2.packSize || 0) + 1 }));   /* the last stretch: to the right door */
            this.later(() => { this.setState({ walk: 0 }); this.syncHUD({ stage: "decide" }); }, 3350);
          }, reduced ? 100 : (e.t === "death" ? 2000 : 600));   /* let the death animation land before the screen changes */
          return;
        }
        if (e.t === "enter" && !reduced && !this._skipIntro) {
          // pause playback: enlarged intro card with FIGHT button + auto-start
          const introToken = (this._introTok = (this._introTok || 0) + 1);
          this.setState({ intro: { idx: e.eidx, hp: e.emax, atk: e.eatk, rank: e.erank || "guard", armor: e.earm || 0, spd: e.espd || 25, lck: e.elck || 10, variant: e.evar || 0 } });
          this.later(() => { if (this._introTok === introToken) this.startFight(); }, 3000);
          return;
        }
        if (e.t === "enter" && this._skipIntro) this._skipIntro = false;
        this.schedNext(this.evDelay(evq, ii));
      }
    }
  }

  /* playback delay after event j — proportional to real battle ticks so gauge bars match gameplay */
  evDelay(evq, j) {
    if (this._reduced) return 40;
    const a = evq[j], b = evq[j + 1];
    const MIN = Math.round(this._step * 0.45);
    if (!a || !b) return MIN;
    if (a.tk != null && b.tk != null && b.tk > a.tk) return Math.min(2600, Math.max(MIN, (b.tk - a.tk) * this._mspt));
    return MIN;
  }

  startFight() {
    if (!this.state.intro || this.state.intro.merchant) return;
    this._introTok = (this._introTok || 0) + 1;
    SFX.tap();
    const intro = this.state.intro;
    let fly = null;
    if (this._runBox && this._introBox && this._enemyBox) {
      const r = this._runBox.getBoundingClientRect();
      const iR = this._introBox.getBoundingClientRect();
      const eR = this._enemyBox.getBoundingClientRect();
      if (iR.width && eR.width && this._runBox.offsetWidth) {
        const scale = r.width / this._runBox.offsetWidth;   // the run box's own render scale (desktop: 1; phone preview: --dd-scale)
        fly = {
          idx: intro.idx, variant: intro.variant || 0,
          k: (this._flyK = (this._flyK || 0) + 1),   /* stable identity per flight: re-renders must not restart it */
          /* VIEWPORT coords: the flier renders as a top-level sibling of the shell, so it is never
             clipped by the shell's overflow and needs no un-clipping hacks */
          left: eR.left, top: eR.top,
          dx: iR.left - eR.left,
          dy: iR.top - eR.top,
          sc: iR.width / 128,            // start at the intro card's size…
          endSc: eR.width / 128          // …and land at the battle frame's real size (desktop frames are scaled 1.6)
        };
      }
    }
    /* the gauges were hidden behind the intro card while their animations ran out — restart both
       so the fight opens with fresh, moving bars instead of frozen/full ones */
    this.setState(s2 => ({ intro: null, sliding: 0, fly: fly, hBarK: (s2.hBarK || 0) + 1, eBarK: (s2.eBarK || 0) + 1 }));
    this.schedNext(220);
    if (fly) this.later(() => { if (!this.state.intro) this.setState({ fly: null }); }, 540);
  }

  descend() {
    SFX.tap();
    tele("descend", { floor: this.G.floor, gold: this.G.gold, hp: this.G.php });
    if (this.G) delete this.G._duelNote;
    const reduced = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    if (!reduced && this.state.stage !== "travel") {
      this.syncHUD({ stage: "travel" });
      this.later(() => this.finishDescend(), 2400);
      return;
    }
    this.finishDescend();
  }
  finishDescend() {
    const G = this.G;
    if (G.eventGaps && G.eventGaps[G.floor] != null && !G.eventSeen[G.floor]) {
      G.eventSeen[G.floor] = 1;
      this.syncHUD({ stage: "event", event: { id: G.eventGaps[G.floor], outcome: null, good: true } });
      return;
    }
    this.proceedDescend();
  }
  proceedDescend() {
    const G = this.G;
    if (G.mode === "versus") G.pmax = Math.round(G.pmax * 1.15);   /* versus: max HP grows 15% each round */
    const AWB = awakeById(G.items, G.awake);
    if (count(G.items, "duelist") && !AWB["duelist"]) {   /* the oath shatters after 4 floors/rounds */
      G._duelF = (G._duelF || 0) + 1;
      if (G._duelF >= 4) { G.items = G.items.filter(x => x !== "duelist"); G._duelNote = "Duelist's Oath shatters \u2014 the oath is spent."; }
    }
    if (G.mode !== "versus" && AWB["stomach"] && count(G.items, "stomach")) G.pmax += 1;   /* awakened Stomach: breathers grow you */
    let breathAmt = breathHeal(5 + (G.mode !== "versus" ? META.bonuses(META.level()).breath : 0), G.floor) * (count(G.items, "stomach") ? 2 : 1);
    if (AWB["heart"] && count(G.items, "heart")) breathAmt += 2;                           /* awakened Ox Heart: +2 at the gate */
    G.breathHealed = G.mode === "versus" ? (G.pmax - G.php) : Math.min(breathAmt, G.pmax - G.php);
    G.php += G.breathHealed; G.floor++;
    if (G.php > G.pmax) { G.breathHealed -= G.php - G.pmax; G.php = G.pmax; }   /* invariant: HP never rests above max */
    if (G.mode === "versus" && count(G.items, "bloodpact")) {   /* the pact re-signs each round: a full heal can't wash it away */
      const due = Math.max(1, Math.round(G.pmax * 0.2));
      G.php = Math.max(1, G.php - due);
      G.breathHealed -= due;
    }
    if (G.floor === this.bazaarFloor()) {   /* versus bazaar: after the 4th fight */
      G.shopDone = false; G.shopDeals = 0;
      G.shopOffer = this.shopOffer();
      tele("shop_enter", { gold: G.gold });
      const reducedM = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
      if (reducedM) { this.syncHUD({ stage: "shop", log: [], event: null }); return; }
      G.nextPack = this.tierPack(G.floor + 1);   /* the bazaar floor has no fight, so roll the next floor's pack here */
      this.syncHUD({ stage: "merchant", log: [], event: null, packSize: 1, hallStep: 0, hallSnap: 1, enemy: null, intro: null });
      this.later(() => this.setState(s2 => ({ walk: (s2.walk || 0) + 1, hallStep: 1, hallSnap: 0 })), 260);   /* walk to the middle of the hall */
      this.later(() => this.setState({ walk: 0,
        enemy: { idx: 0, variant: 0, rank: "merchant", hp: 0, max: 0, atk: 0, armor: 0, spd: 0, lck: 0, merchant: true },
        intro: { merchant: true, line: MERCHANT_LINES[Math.floor(G.rng() * MERCHANT_LINES.length)] } }), 3550);
      return;
    }
    this.syncHUD({ stage: "draft", offer: this.draftOffer(), log: [], event: null, hallStep: 0, hallSnap: 1 });
  }
  shopOffer() {
    const G = this.G, r = G.rng;
    const avail = poolFor(G.mode === "versus" ? "versus" : "delve").filter(id => (ITEMS[id].stack || G.items.indexOf(id) < 0) && liveFor(id, G.items));
    const out = [];
    while (out.length < Math.min(5, avail.length)) {
      const id = weightedRelic(avail, this.G.items, r);
      if (out.indexOf(id) < 0) out.push(id);
    }
    G.awake = G.awake || {};
    const seenA = {};
    G.shopAwake = G.items.map((id, i) => ({ id, i })).filter(e => ITEMS[e.id] && ITEMS[e.id].stack && !G.awake[e.i] && !seenA[e.id] && (seenA[e.id] = 1)).slice(0, 6);
    return out;
  }
  shopBuy(id) {
    const G = this.G;
    const priceB = Math.round(SHOP_BUY * (count(G.items, "merchantthumb") ? 0.8 : 1));
    if (G.shopDone || G.gold < priceB || G.shopOffer.indexOf(id) < 0) return;
    G.shopDeals = (G.shopDeals || 0) + 1;
    G.shopDone = G.shopDeals >= (((awakeById(G.items, G.awake)["merchantthumb"]) ? 2 : 1) + (G.perkDeal || 0));
    SFX.tap(); G.gold -= priceB; this.spendHeal(G);
    const relicLand = this.landFrom(id, G.items.length);
    applyPickup(G, id);
    G.shopOffer = G.shopOffer.filter(o => o !== id || ITEMS[id].stack);   /* uniques leave the shelf */
    if (!ITEMS[id].stack) G.shopOffer = G.shopOffer.filter(o => o !== id);
    tele("shop_buy", { item: id, gold: G.gold });
    this.syncHUD({ pulse: relicLand ? null : { id, idx: G.items.length - 1 }, relicLand });
    this.later(() => this.shopLeave(), 900);
  }
  shopAwaken(idx) {                          /* awaken the copy at items[idx]: it counts as two copies */
    const G = this.G, rid = G.items[idx];
    const priceU = Math.round(SHOP_UP * (count(G.items, "merchantthumb") ? 0.8 : 1));
    if (G.shopDone || G.gold < priceU || !rid || (G.awake && G.awake[idx])) return;
    G.shopDeals = (G.shopDeals || 0) + 1;
    G.shopDone = G.shopDeals >= (((awakeById(G.items, G.awake)["merchantthumb"]) ? 2 : 1) + (G.perkDeal || 0));
    SFX.tap(); G.gold -= priceU; this.spendHeal(G);
    G.awake = G.awake || {}; G.awake[idx] = true;
    if (rid === "hollowidol") { G.pmax += 15; G.php = Math.min(G.pmax, G.php + 15); }   /* the idol fills */
    if (rid === "debtorchain" && G._debtLeft > 0) G._debtLeft = Math.ceil(G._debtLeft / 2);
    G.shopAwake = [];
    tele("shop_awaken", { relic: rid, gold: G.gold });
    this.syncHUD({ pulse: { id: rid, idx } });
    this.later(() => this.shopLeave(), 900);
  }
  shopSocket(cid, idx) {                    /* buy component cid, socket into the copy at items[idx] */
    const G = this.G, rid = G.items[idx];
    const priceU = Math.round(SHOP_UP * (count(G.items, "merchantthumb") ? 0.8 : 1));
    if (G.shopDone || G.gold < priceU || !COMPONENTS[cid] || !rid || (G.sockets || {})[idx] != null) return;
    G.shopDone = true;
    SFX.tap(); G.gold -= priceU; this.spendHeal(G);
    G.sockets = G.sockets || {};
    G.sockets[idx] = cid;
    G.shopComps = (G.shopComps || []).filter(c => c !== cid);
    tele("shop_socket", { comp: cid, relic: rid, gold: G.gold });
    this.syncHUD({ pulse: { id: rid, idx }, socketPick: null });
    this.later(() => this.shopLeave(), 900);
  }
  shopLeave() {
    if (this._left) return; this._left = 1; this.later(() => { this._left = 0; }, 1000);
    tele("shop_leave", { gold: this.G.gold, bought: !!this.G.shopDone });
    const reducedM = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    if (reducedM || this.state.stage !== "shop") return this.descend();
    /* walk the rest of the hall to the right door, then descend as always */
    this.setState({ stage: "merchant", hallStep: 1, hallSnap: 0, enemy: null });   /* remount the hall mid-way first, so the pan animates instead of snapping */
    this.later(() => this.setState(s2 => ({ walk: (s2.walk || 0) + 1, hallStep: 2 })), 90);
    this.later(() => { this.setState({ walk: 0 }); this.descend(); }, 3450);
  }
  chooseEvent(ci) {
    const G = this.G, ev = this.state.event, def = ev && EVENTS[ev.id], ch = def && def.choices[ci];
    if (!ch || ev.outcome || (ch.cost && G.gold < ch.cost)) return;
    SFX.tap();
    const goldBefore = G.gold;
    G._evLandIdx = null;
    const out = ch.run(G, G.evRng, this);
    const relicLand = G._evLandIdx != null ? this.landFrom(null, G._evLandIdx, "[data-dd-event-art]") : null;
    G._evLandIdx = null;
    if (G.gold < goldBefore) this.spendHeal(G);
    tele("floor_event", { id: ev.id, choice: ci, good: !!out.good });
    G.php = Math.max(1, Math.min(G.pmax, G.php));
    G.gold = Math.max(0, G.gold);
    G.spdB = Math.max(EV_MIN_SPD, G.spdB); G.defB = Math.max(EV_MIN_DEF, G.defB);
    this.syncHUD({ relicLand, event: Object.assign({}, ev, { outcome: out.text, good: !!out.good, outcomeArt: out.art || null }) });
  }
  eventGo() { SFX.tap(); this.proceedDescend(); }
  spendHeal(G) {   /* Debt of Flesh: any gold spend heals 10 (awakened 20) */
    if (!G || !count(G.items, "debtflesh")) return;
    const amt = (G.awake && awakeById(G.items, G.awake)["debtflesh"]) ? 20 : 10;
    const real = Math.min(amt, G.pmax - G.php);
    if (real > 0) { G.php += real; this.setState({ php: G.php }); }
  }
  heroF(id, s) {   /* Balance Lab hero stat: formula of l, typed override wins */
    const d = HERO_STAT_FORMULAS.find(x => x.id === id);
    const t = s.balHeroForm && s.balHeroForm[id] != null ? compileFormula(s.balHeroForm[id]) : null;
    const fn = t || compileFormula(d.def);
    return Math.round(fn(1, undefined, 0, s.balPlvl || 1));
  }
  rerollPrice(G) { return Math.round(10 * Math.pow(2, G.rerolls) * (count(G.items, "merchantthumb") ? 0.8 : 1)); }
  rerollDraft() {
    const G = this.G, price = this.rerollPrice(G);
    if (G.gold < price) return;
    SFX.tap(); G.gold -= price; G.rerolls++; this.spendHeal(G);
    tele("reroll", { floor: G.floor, price });
    this.syncHUD({ offer: this.draftOffer(G.evRng) });
  }
  cashOut() { SFX.tap(); tele("cashout", { floor: this.G.floor, gold: this.G.gold }); this.finishRun("cashout"); }

  bazaarFloor() { return (this.G && this.G.mode === "versus") ? 5 : SHOP_FLOOR; }
  hallArtFor(f) {   /* the bazaar floor has its own hall; everything else uses the dungeon's art */
    if (f === this.bazaarFloor()) return "assets/hall-bazaar.png";
    const G = this.G;
    if (G && G.mode === "versus" && G.duelHall)   /* versus: whoever is hosting this duel */
      return (DUNGEONS[Math.max(0, Math.min(DUNGEONS.length - 1, G.duelHall.id - 1))] || {}).art || BATTLE_BG;
    return (G && DUNGEONS[Math.max(0, Math.min(DUNGEONS.length - 1, (G.tier || 1) - 1))].art) || BATTLE_BG;
  }
  showXp() {
    SFX.tap();
    const before = Math.max(0, META.xp() - ((this.G && this.G.xpGain) || 0));
    const lvl0 = META.levelFor(before), lvlN = META.level();
    this.setState({ screen: "xp", xpShown: true, xpFill: META.progress(before), xpLvlShown: lvl0, xpCelebrate: false });
    /* no level gained: one smooth fill */
    if (lvlN <= lvl0) { this.later(() => this.setState({ xpFill: META.progress(META.xp()), xpCelebrate: true }), 450); return; }
    /* leveled: fill each bar to full, snap to empty at the new level, then settle into the remainder */
    let t = 450;
    for (let l = lvl0; l < lvlN; l++) {
      const step = l;
      this.later(() => this.setState({ xpFill: 1 }), t);                                          /* run it to the brim */
      this.later(() => this.setState({ xpFill: 0, xpLvlShown: step + 1, xpSnap: 1 }), t + 900);   /* level ticks over */
      this.later(() => this.setState({ xpSnap: 0 }), t + 960);
      t += 1000;
    }
    this.later(() => this.setState({ xpFill: META.progress(META.xp()), xpCelebrate: true }), t);   /* the leftover, then confetti */
  }
  finishRun(how) {
    const G = this.G;
    if (G.mode === "versus" && how === "cleared") Store.set("dd.vsCrowns", (Store.get("dd.vsCrowns", 0) | 0) + 1);
    let banked = G.gold;
    if (how === "died") banked = 0;
    else if (how === "cashout" && count(G.items, "piggy")) banked = Math.floor(banked * (awakeById(G.items, G.awake)["piggy"] ? 1.5 : 1.25));
    /* score doubles as XP, independent of gold: depth + kills + clearing, scaled by tier */
    /* clears land ~700 base (480 depth + ~110 kills + 150 crown); banked gold is the spread beyond 1000 */
    const score = Math.round((((G.mode === "versus") ? (G.floor - 1) * 40 + (how === "cleared" ? 150 : 0)
      : (G.floor - 1) * 40 + (G.kills || 0) * 5 + (how === "cleared" ? 150 : 0)) + banked / 2) * (DUNGEONS[Math.max(0, Math.min(DUNGEONS.length - 1, (G.tier || 1) - 1))].mult));
    /* Score is the bragging number: base x dungeon mult, never touched by the XP table, so two
       delvers' identical runs compare. The frontier table scales XP only. Gold is untouched at
       both ends — the purse you collect is the purse you bank. */
    const relevance = (G.mode === "versus" || G.sandbox) ? 1 : META.rewardMult(G.tier || 1);
    G.how = how; G.fullGold = G.gold; G.banked = banked; G.score = score;
    G.relevance = relevance;
    const score2 = score;
    const xpGain = Math.max(score > 0 ? 1 : 0, Math.round(score * relevance));
    if (!G.sandbox) {
      Store.set("dd.runs", (Store.get("dd.runs", 0) | 0) + 1);
      if (how === "cleared") { Store.set("dd.clears", (Store.get("dd.clears", 0) | 0) + 1); if (G.mode !== "versus") META.unlockNext(G.tier || 1); }
      if (banked > 0) Store.set("dd.goldLife", (Store.get("dd.goldLife", 0) | 0) + banked);
    }
    if (!G.sandbox) {   /* XP = rating, kept even in death */
      const before = META.level();
      Store.set("dd.xp", META.xp() + xpGain);
      G.xpGain = xpGain; G.lvlUps = [];
      for (let l = before + 1; l <= META.level(); l++) G.lvlUps.push("LEVEL " + l + " — " + META.perkDesc(l));
    }
    const frac = (G.floor - 1) / MAX_FLOOR;   /* stars + unlocks are presentation: every run gets them */
    G.stars = how === "cleared" ? 3 : (frac >= 0.72 || (how === "cashout" && frac >= 0.6)) ? 2 : frac >= 0.4 ? 1 : 0;
    G.unlocks = [];
    if (how === "cleared" && G.mode !== "versus") {
      const D = DUNGEONS[Math.max(0, Math.min(DUNGEONS.length - 1, (G.tier || 1) - 1))];
      const seen = Store.get("dd.arts", []) || [];
      if (seen.indexOf(D.art) < 0) { if (!G.sandbox) Store.set("dd.arts", seen.concat(D.art)); G.unlocks.push({ kind: "BACKDROP", name: D.name, art: D.art }); }
      if ((G.tier || 1) < DUNGEONS.length) G.unlocks.push({ kind: "DUNGEON", name: DUNGEONS[G.tier].name, art: DUNGEONS[G.tier].art });
    }
    tele("run_end", {
      score: score2, relevance, gold: banked, floor: G.floor, kills: G.kills, daily: !!G.isDaily, how,
      relics: G.items.length, rerolls: G.rerolls, revived: !!G.revived,
      durSec: Math.round((Date.now() - G.startMs) / 1000), runNo: Telemetry.runCount
    });
    /* economy: banked gold piles into the persistent GOLD wallet (soft currency); sparks are hard currency only */
    const gained = banked;
    const noBank = !!G.sandbox;   /* sandbox runs are tampered: nothing persists */
    if (banked > 0 && !noBank) Store.set("dd.gold", (Store.get("dd.gold", 0) | 0) + banked);
    const sparks = this.state.sparks;
    let best = this.state.best, isBest = false;
    if (score2 > best && !noBank) { best = score2; Store.set("dd.best", best); isBest = true; }
    if (G.isDaily && !noBank) { const k = "dd.daily." + G.seed; if (score2 > (Store.get(k, 0) | 0)) Store.set(k, score2); }
    if (!noBank) {
      const board = (Store.get("dd.scores", []) || []).concat({ score: score2, floor: G.floor, kills: G.kills, relics: G.items.length, name: this.state.name, ts: Date.now() })
        .sort((a, b) => b.score - a.score).slice(0, 10);
      Store.set("dd.scores", board);
    }
    this.clearTimers();
    this._ctk = (this._ctk || 0) + 1;
    this.setState({
      screen: "over", how, sparks, best, isBest, gained, shownGold: how === "died" ? G.fullGold : 0, depleting: false, shareOpen: false,
      canRecover: how === "died" && !G.isDaily && G.fullGold > 0,
      dailyNote: G.isDaily ? this.t("dailySavedLocal") : null,
      floor: G.floor, kills: G.kills, items: G.items.slice()
    });
    if (how !== "died") { this.later(() => SFX.fanfare(), 400); this.countTo(banked, 1000); }
    else this.later(() => {
      this.setState({ depleting: true });
      SFX.death();
      this.countTo(0, 1600, G.fullGold);
      this.later(() => this.setState({ depleting: false }), 1700);
    }, 900);
  }
  countTo(target, ms, from) {
    const tok = (this._ctk = (this._ctk || 0) + 1);
    const start = performance.now(), f0 = from == null ? 0 : from;
    const tick = now => {
      if (tok !== this._ctk) return;
      const p = Math.min(1, (now - start) / ms), e = 1 - Math.pow(1 - p, 3);
      this.setState({ shownGold: Math.round(f0 + (target - f0) * e) });
      if (p < 1) requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  }
  revive(viaAd) {
    const G = this.G;
    if (!G || G.revived || this.state.adBusy) return;
    const grant = () => {
      tele("revive", { via: viaAd ? "ad" : "sparks", floor: G.floor });
      G.revived = true; G.php = Math.max(1, Math.floor(G.pmax / 2));
      const remaining = (G.pack || []).slice(this.state.packDone).map(e => Object.assign({}, e));
      // resume the fight exactly where it left off: the enemy that killed you keeps its current HP
      if (remaining.length && this.state.enemy) { remaining[0].max = this.state.enemy.max; remaining[0].hp = Math.max(1, this.state.enemy.hp); }
      this.setState({ adBusy: false, php: G.php });
      SFX.heal();
      this.later(() => this.fight(remaining.length ? remaining : this.tierPack(G.floor), true, this._lastCarry), 250);
    };
    if (viaAd) {
      if (this.state.supporter) return grant();
      this.setState({ adBusy: true });
      setTimeout(grant, 1400);
    } else {
      if (this.state.sparks < REVIVE_SPARKS) return;
      const sp = this.state.sparks - REVIVE_SPARKS;
      Store.set("dd.sparks", sp);
      this.setState({ sparks: sp });
      grant();
    }
  }
  adRecover() {
    if (this.state.adBusy) return;
    tele("ad_recover", { fullGold: this.G.fullGold });
    this.setState({ adBusy: true });
    setTimeout(() => {
      const G = this.G;
      const AWG = awakeById(G.items, G.awake);
      G.banked = Math.floor(G.fullGold * (count(G.items, "smuggler") ? (AWG["smuggler"] ? 0.9 : 0.65) : 0.5));   /* Smuggler's Lining */
      if (count(G.items, "piggy")) G.banked = Math.floor(G.banked * (AWG["piggy"] ? 1.5 : 1.25));
      if (G.banked > 0 && !G.sandbox) Store.set("dd.gold", (Store.get("dd.gold", 0) | 0) + G.banked);   /* recovered purse piles into the wallet; the rating is already sealed */
      this.setState({ adBusy: false, canRecover: false, gained: G.banked });
      this.countTo(G.banked, 800, 0);
    }, this.state.supporter ? 100 : 1400);
  }
  watchAd() {
    if (this.state.adBusy) return;
    tele("ad_sparks", { supporter: !!this.state.supporter });
    const grant = () => { const s = this.state.sparks + 30; Store.set("dd.sparks", s); this.setState({ sparks: s, adBusy: false }); };
    if (this.state.supporter) return grant();
    this.setState({ adBusy: true }); setTimeout(grant, 1400);
  }
  buySupporter() {
    tele("sup_buy", {});
    const s = this.state.sparks + SUPPORTER_SPARKS;
    Store.set("dd.supporter", true); Store.set("dd.sparks", s);
    this.setState({ supporter: true, sparks: s });
  }
  shareText() {
    const G = this.G;
    const tag = G.how === "died" ? this.t("shareDied") : (G.how === "cleared" ? this.t("shareCleared") : this.t("shareCashed"));
    const head = G.isDaily ? ("RELIC RUN \u00b7 DAILY DELVE #" + (G.seed % 100000)) : ("RELIC RUN (" + this.t("sharePractice") + ")");
    return head + "\n" + this.t("shareFloor", { n: G.floor }) + " · " + tag + "\n" + this.t("shareBanked", { n: G.banked }) + "\n" + G.items.map(i => this.itemName(i)).join(" · ");
  }
  shareTo(ch) {
    const txt = this.shareText();
    tele("share", { ch });
    if (ch === "copy") { try { navigator.clipboard && navigator.clipboard.writeText(txt); } catch (e) { } return; }
    if (ch === "sys") { try { if (navigator.share) return navigator.share({ text: txt }).catch(() => { }); } catch (e) { } return this.shareTo("copy"); }
    const enc = encodeURIComponent(txt); let url = ""; try { url = encodeURIComponent(location.href); } catch (e) { }
    const urls = { x: "https://twitter.com/intent/tweet?text=" + enc, fb: "https://www.facebook.com/sharer/sharer.php?u=" + url + "&quote=" + enc, wa: "https://wa.me/?text=" + enc, tg: "https://t.me/share/url?url=" + url + "&text=" + enc };
    if (urls[ch]) { try { window.open(urls[ch], "_blank", "noopener"); } catch (e) { } }
  }
  setLang(l) { Store.set("dd.lang", l); this.setState({ lang: l }); }
  setName(v) {
    const n = String(v || "").replace(/[\u0000-\u001f<>]/g, "").trim().slice(0, 16) || this.genName();
    Store.set("dd.name", n); this.setState({ name: n });
  }

  hudCtx(s) {   /* HUD/stat-card ctx: game run state + the playback ledger */
    const G = this.G;
    return { items: G.items || [], awake: awakeById(G.items, G.awake), mode: G.mode,
      base: { atk: G.baseAtk, def: G.baseDef, spd: G.baseSpd, lck: G.baseLck },
      run: { adrenaline: G.adrenaline || 0, midasBonus: G.midasBonus || 0, atkB: G.atkB || 0, defB: G.defB || 0, spdB: G.spdB || 0, luckB: G.luckB || 0 },
      php: s.php, pmax: s.pmax, gold: s.gold || 0, floor: G.floor || 0,
      foeRank: s.enemy ? s.enemy.rank : null, debtLeft: G._debtLeft || 0, glassBroken: G._glassBroken,
      mods: s.mods || [] };
  }
  renderVals() {
    const s = this.state, accent = this.props.accent || "#E3B341";
    const pip = (on, c, w) => ({ flex: "1 1 0", height: "10px", background: on ? c : "#241F18", border: on ? "none" : "1px solid #2B261D", boxSizing: "border-box", minWidth: w || "3px", transition: "background .18s" });
    const filled = Math.ceil(Math.max(0, s.php) / s.pmax * 12);
    const eFilled = s.enemy && s.enemy.max ? Math.ceil(Math.max(0, s.enemy.hp) / s.enemy.max * 14) : 0;
    const logColor = { enter: "#8B8172", you: "#E7E0D2", hurt: "#C4593C", good: "#7C9A6A", gold: accent, chain: "#9B8BD0", echain: "#C08A8C", kill: "#E7E0D2", death: "#C4593C", dim: "#655C4E" };

    const slot = (id, i, up) => {
      const p = s.pulse, pu = !!(p && (typeof p === "object" ? (p.id === id && (p.idx == null || p.idx === i)) : p === id));
      const G = this.G || {};
      /* conditional relics grey out while their condition is unmet (dormant state) */
      const hpr = s.pmax ? s.php / s.pmax : 1, er = s.enemy ? s.enemy.rank : null;
      const AWd = G.awake ? awakeById(s.items || [], G.awake) : {};
      const dim = id === "tower" ? hpr < (AWd["tower"] ? 0.5 : 0.8)
        : id === "marrow" ? (!AWd["marrow"] && hpr >= 0.5)
        : id === "berserk" || id === "rustward" ? hpr >= 0.5
        : id === "duelist" ? (er != null && er !== "boss" && er !== "king" && er !== "elite")
        : id === "omen" ? (er != null && er !== "boss" && er !== "king")
        : id === "glassedge" ? !!G._glassBroken
        : id === "soil" ? !!G._soilUsed
        : id === "debtorchain" ? G._debtLeft > 0
        : id === "auricskin" ? (s.gold || 0) < (AWd["auricskin"] ? 25 : 50)
        : id === "leechpurse" ? (s.gold || 0) < 3
        : id === "titheshell" ? !G._tithePaid
        : id === "anvil" ? (G._anvilB || 0) >= (AWd["anvil"] ? 12 : 10)
        : id === "chalice" ? (G._chaliceG || 0) >= 10
        : false;
      /* icon badge + fuse pips come from the SAME meter as the relic card, so they can't disagree */
      const M = this.relicMeter(id, i);
      const badge = M.use ? (M.use.debt != null ? (M.use.debt > 0 ? String(M.use.debt) : "\u2713") : (M.use.left === 0 ? "\u2717" : (M.use.left <= 3 ? String(M.use.left) : ""))) : "";
      const spent = badge === "\u2717";
      const cnt = s.cnt || { a: 0, h: 0, g: 0, st: 0 };
      let fuse = M.trigNat ? M.trigNat.fill : (M.trig ? M.trig.fill : null), fuseN = M.trigNat ? M.trigNat.total : (M.trig ? M.trig.total : 3);
      const sockFuse = (M.trigNat && M.trig) ? M.trig : null;   /* both cadences: native on the main bar, attached on a second hairline */
      const kk = ITEMS[id].kind, kn = s.items.filter(x => (ITEMS[x] && ITEMS[x].kind) === kk).length;
      if (fuse == null && kk === "EDGE" && kn >= 5) { fuse = (cnt.f || 0) % 4; fuseN = 4; }
      if (fuse == null && kk === "PACE" && kn >= 7) { fuse = (cnt.f || 0) % 5; fuseN = 5; }
      const land = (s.relicLand && s.relicLand.landIdx === i) ? s.relicLand : null;
      const slotBase = { all: "unset", boxSizing: "border-box", width: "34px", height: "34px", position: "relative", border: "2px solid " + (pu ? accent : (up ? "#E3B341" : kindColor(ITEMS[id].kind))), background: "linear-gradient(180deg,#0B0906 0%,#12100B 42%," + kindColor(ITEMS[id].kind) + "38 100%)", cursor: "pointer", filter: pu ? "brightness(1.5)" : (dim ? "grayscale(1) brightness(.6)" : "none"), opacity: dim && !pu ? 0.55 : 1, boxShadow: pu ? "0 0 10px rgba(227,179,65,.7)" : "inset 0 1px 0 rgba(255,255,255,.14),0 2px 0 #0C0A07", zIndex: land ? 80 : (pu ? 60 : "auto"), transformOrigin: "top left",
        "--rlx": land ? land.dx + "px" : "0px", "--rly": land ? land.dy + "px" : "0px", "--rls": land ? land.fs.toFixed(3) : "1",
        animation: land ? "relicland .62s cubic-bezier(.45,0,.25,1) both" : (pu ? "relicfire .55s ease" : "none"), transition: "filter .25s, opacity .25s" };
      return {
        glyph: ricon(id, 30),
        glyphL: ricon(id, 50),
        slotStyleL: Object.assign({}, slotBase, { width: "58px", height: "58px" }),
        badge,
        fuseOn: fuse != null,
        /* bottom edge = charge toward the next activation (gold, fills left→right) */
        sockOn: !!sockFuse,
        sockTrack: { position: "absolute", left: "2px", right: "2px", bottom: "5px", height: "2px", background: "#241F18", pointerEvents: "none", overflow: "hidden" },
        sockTrackL: { position: "absolute", left: "3px", right: "3px", bottom: "8px", height: "3px", background: "#241F18", pointerEvents: "none", overflow: "hidden" },
        sockFill: sockFuse ? { display: "block", height: "100%", width: Math.round((sockFuse.fill / Math.max(1, sockFuse.total)) * 100) + "%", background: "#B7A5F0", transition: "width .18s" } : {},
        chargeTrack: { position: "absolute", left: "2px", right: "2px", bottom: "1px", height: "3px", background: "#241F18", pointerEvents: "none", overflow: "hidden" },
        chargeTrackL: { position: "absolute", left: "3px", right: "3px", bottom: "2px", height: "5px", background: "#241F18", pointerEvents: "none", overflow: "hidden" },
        chargeFill: { display: "block", height: "100%", width: Math.round((fuse / Math.max(1, fuseN)) * 100) + "%", background: "#E3B341", boxShadow: "0 0 3px rgba(227,179,65,.8)", transition: "width .18s" },
        /* right edge = activations left (green→red, drains top→bottom) */
        useOn: !!M.use,
        useTrack: { position: "absolute", right: "1px", top: "2px", bottom: "5px", width: "3px", background: "#241F18", pointerEvents: "none", overflow: "hidden" },
        useTrackL: { position: "absolute", right: "2px", top: "3px", bottom: "8px", width: "5px", background: "#241F18", pointerEvents: "none", overflow: "hidden" },
        useFill: M.use ? { display: "block", position: "absolute", left: 0, right: 0, bottom: 0, height: Math.round((M.use.left / Math.max(1, M.use.cap)) * 100) + "%", background: M.use.left === 0 ? "#C4593C" : M.use.left <= Math.max(1, Math.round(M.use.cap * 0.3)) ? "#E3B341" : "#7C9A6A", transition: "height .2s" } : {},
        badgeStyle: { position: "absolute", left: "-1px", top: "-1px", fontFamily: "'Silkscreen',monospace", fontSize: "6px", lineHeight: 1, padding: "1px 2px", background: "#14110C", border: badge ? "1px solid " + (spent ? "#4A2B26" : "#3A3328") : "none", color: spent ? "#C4593C" : "#E3B341", pointerEvents: "none", display: badge ? "block" : "none" },
        badgeStyleL: { position: "absolute", left: "-1px", bottom: "-1px", fontFamily: "'Silkscreen',monospace", fontSize: "9px", lineHeight: 1, padding: "1px 3px", background: "#14110C", border: badge ? "1px solid " + (spent ? "#4A2B26" : "#3A3328") : "none", color: spent ? "#C4593C" : "#E3B341", pointerEvents: "none", display: badge ? "block" : "none" },
        slotStyle: slotBase,
        info: () => this.setState({ modal: "relic", relicId: id, relicIdx: i })
      };
    };
    const tray = s.items.map((id, i) => slot(id, i, !!((s.sockets && s.sockets[i] != null) || (this.G && this.G.awake && this.G.awake[i]))));

    const chainsOf = id => {
      const it = ITEMS[id];
      const names = s.items.filter(o => o !== id && (it.reacts.some(t => ITEMS[o].emits.includes(t)) || it.emits.some(t => ITEMS[o].reacts.includes(t))));
      return Array.from(new Set(names)).map(o => this.itemName(o));
    };
    const offers = (s.offer || []).map((id, i) => {
      const c = chainsOf(id), owned = count(s.items, id);
      return {
        name: this.itemName(id), desc: this.richDesc(this.itemDesc(id)), kind: ITEMS[id].kind, kindCol: kindColor(ITEMS[id].kind),
        wheelOn: !!ITEMS[id].wheel,
        wheel: ITEMS[id].wheel ? ITEMS[id].wheel[0] + " \u2192 " + ITEMS[id].wheel[1] : "",
        wheelStyle: ITEMS[id].wheel ? { fontFamily: "'Silkscreen',monospace", fontSize: "7px", letterSpacing: "1px", padding: "2px 6px", flex: "0 0 auto", border: "1px solid #4A3F2E", color: "#E7E0D2", background: "linear-gradient(90deg," + ({ BLOOD: "#C4595C", GOLD: "#E3B341", GRACE: "#9B8BD0" })[ITEMS[id].wheel[0]] + "33," + ({ BLOOD: "#C4595C", GOLD: "#E3B341", GRACE: "#9B8BD0" })[ITEMS[id].wheel[1]] + "33)" } : {},
        glyph: ricon(id, 44, 1.34), glyphFluid: riconFluid(id), slot: rslot(id, { width: "48px", height: "48px", overflow: "hidden" }),
        owned: owned > 0, ownedText: this.t("ownedTimes", { n: owned }),
        chains: this.props.showSynergy === false ? false : c.length > 0, chainWith: c.join(" · "),
        cardStyle: { all: "unset", boxSizing: "border-box", display: "block", width: "100%", padding: "15px", background: "#1B1813", border: "2px solid #2B261D", cursor: "pointer", color: "#E7E0D2", animation: `deal .3s ${i * 0.07}s ease both` },
        rid: id,
        peek: () => { if (this.state.draftPeek !== id) this.setState({ draftPeek: id }); },
        pick: () => this.pickRelic(id)
      };
    });

    const np = this.G && this.G.nextPack ? this.G.nextPack : null;
    const lead = np ? np[np.length - 1] : null;
    const scores = Store.get("dd.scores", []) || [];
    const langNames = window.DD_LANG_NAMES || { en: "English" };
    const relic = s.relicId;

    const part = (p, col, glow, dur) => ({ position: "absolute", left: "50%", top: "50%", width: "4px", height: "4px", background: col, boxShadow: "0 0 5px " + glow, pointerEvents: "none", zIndex: 5, "--dx": p.dx + "px", "--dy": p.dy + "px", animation: "lootFly " + dur + "s cubic-bezier(.2,.6,.4,1) " + p.d + "s forwards", opacity: 0 });
    const evDef = s.stage === "event" && s.event ? EVENTS[s.event.id] : null;
    window.__ddRefresh = window.__ddRefresh || (() => { try { this.setState({}); } catch (e) { } });
    const heroPal = (() => {
      const p = studioPalette();
      /* changing-room pick (famHex.R) wins; the accent prop is only the fallback */
      const ac = (Store.get("dd.studio.famHex", {}) || {}).R || this.props.accent || "#E3B341";
      const fp = famParams("R");   /* re-derive accent dark+light from the live accent, not the default hex */
      p.R = ac;
      p.r = applyShade(ac, fp.darkMult, fp.darkHue, fp.darkSat);
      p["0"] = applyShade(ac, fp.lightMult, fp.lightHue, fp.lightSat);
      return p;
    })();
    const idleAnim = (() => { try { return composeHeroState(wardrobeCombo(), "idle"); } catch (e) { return null; } })();
    const heroBits = idleAnim && idleAnim.frames.length ? idleAnim.frames[(s.idleF || 0) % idleAnim.frames.length] : composeHero32(wardrobeCombo());
    const enemyBits = s.enemy ? (B[ENEMY_G[s.enemy.idx]] || B.ogre) : null;
    /* versus: the opponent on the battle screen IS the rival delver, not a monster */
    const vsFoeLayer = (px, which, extra) => {   /* which: "bg" | "body" | "frame" — separate elements so death FX only hits the body */
      try {
        const G2 = this.G; if (!G2 || G2.mode !== "versus" || G2.vsFoe == null) return null;
        const b = G2.vsRoster[G2.vsFoe]; if (!b || !b.profile) return null;
        const combo = b.profile.combo, pal = paletteFor(b.profile.famHex || { R: b.profile.accent });
        let rows;
        if (which === "bg") rows = composeBg32(combo);
        else if (which === "frame") rows = composeFrame32(combo);
        else {
          const anim = G2.vsFoeAnim;
          if (anim && anim.frames && anim.frames.length) {
            const bg = composeBg32(combo), fr = composeFrame32(combo);
            rows = anim.frames[(s.idleF || 0) % anim.frames.length].map((row, y) => row.split("").map((ch, x) =>
              (ch !== "." && ((bg[y] && bg[y][x] === ch) || (fr[y] && fr[y][x] === ch))) ? "." : ch).join(""));
          } else rows = composeHeroOnly32(combo);
        }
        return Object.assign({}, heroSprite(rows, pal, px), { transform: "translate(-50%,-50%) scaleX(-1)" }, extra || {});
      } catch (e) { return null; }
    };
    const vsFoeAv = (px, extra) => vsFoeLayer(px, "body", extra);
    this._shcE = this._shcE || {};
    this._shcH = this._shcH || {};
    const memoS = (cache, key, build) => {
      if (cache.key !== key) { cache.key = key; cache.val = build(); }
      return cache.val;
    };
    const shatterE = (s.dyingE && s.enemy) ? memoS(this._shcE, "E" + s.dyingE + s.enemy.idx + "_" + (s.enemy.variant || 0) + (this.G && this.G.mode === "versus" ? "v" + this.G.vsFoe : ""), () => {
      if (this.G && this.G.mode === "versus" && this.G.vsFoe != null && this.G.vsRoster[this.G.vsFoe] && this.G.vsRoster[this.G.vsFoe].profile) {
        const b = this.G.vsRoster[this.G.vsFoe];
        try {
          const mirrored = composeHeroOnly32(b.profile.combo).map(r => r.split("").reverse().join(""));   /* keep the facing-left pose; bg/frame don't shatter */
          return shatterOf(mirrored, 104 / PACK.SIZE, paletteFor(b.profile.famHex || { R: b.profile.accent }), 116, s.dyingBoss);
        } catch (e) { }
      }
      return this.enemyShatter(s.enemy.idx, s.enemy.variant, 104, s.dyingBoss) || shatterOf(enemyBits, 6.5, PAL, 116, s.dyingBoss);
    }) : null;
    const heroOnlyBits = (() => { try { return composeHeroOnly32(wardrobeCombo()); } catch (e) { return heroBits; } })();   /* no bg, no frame */
    const heroOnlyAnimBits = (() => {   /* the live pose, stripped of the bg plate and frame */
      try {
        if (!idleAnim || !idleAnim.frames.length) return heroOnlyBits;
        const bg = composeBg32(wardrobeCombo()), fr = composeFrame32(wardrobeCombo());
        const rows = idleAnim.frames[(s.idleF || 0) % idleAnim.frames.length];
        return rows.map((row, y) => row.split("").map((ch, x) =>
          (ch !== "." && ((bg[y] && bg[y][x] === ch) || (fr[y] && fr[y][x] === ch))) ? "." : ch).join(""));
      } catch (e) { return heroOnlyBits; }
    })();
    const shatterH = s.dyingH ? memoS(this._shcH, "H" + s.dyingH + "@" + PACK.SIZE, () => shatterOf(heroOnlyBits, 120 / PACK.SIZE, heroPal, 120, false, true)) : null;
    return {
      shatterEOn: !!shatterE, shatterEWrap: shatterE ? shatterE.wrap : {}, shatterE: shatterE ? shatterE.pixels : [],
      shatterHOn: !!shatterH, shatterHWrap: shatterH ? shatterH.wrap : {}, shatterH: shatterH ? shatterH.pixels : [],
      bgEntranceStyle: (s.screen !== "title" || this.props.uiStyle === "stone") ? { display: "none" } : {
        position: "absolute", left: "50%", top: "31%", transform: "translateX(-50%)",
        width: "340px", height: "353px", zIndex: 1, pointerEvents: "none",   /* above the stone backdrop (z0), below the content (z2) */
        backgroundImage: "url('" + ((window.__resources && window.__resources.bgEntrance) || "bg-entrance.png") + "')", backgroundSize: "contain", backgroundRepeat: "no-repeat",
        imageRendering: "pixelated", opacity: 1,
        maskImage: "radial-gradient(ellipse 62% 58% at 50% 46%, #000 55%, transparent 100%)",
        WebkitMaskImage: "radial-gradient(ellipse 62% 58% at 50% 46%, #000 55%, transparent 100%)"
      },
      torchGlowStyle: (s.screen !== "title" || this.props.uiStyle === "stone") ? { display: "none" } : {
        position: "absolute", left: "calc(50% + 66px)", top: "calc(31% + 101px)",
        width: "120px", height: "120px", zIndex: 0, pointerEvents: "none",
        background: "radial-gradient(circle, rgba(255,150,40,.5) 0%, rgba(230,110,30,.22) 40%, transparent 70%)",
        animation: "torchGlow 2.8s ease-in-out infinite"
      },
      torchFlameStyle: (s.screen !== "title" || this.props.uiStyle === "stone") ? { display: "none" } : {
        position: "absolute", left: "calc(50% + 66px)", top: "calc(31% + 101px)",
        width: "10px", height: "16px", zIndex: 0, pointerEvents: "none",
        background: "linear-gradient(180deg, #FFE9A0 0%, #FFB03A 45%, #E3641E 100%)",
        borderRadius: "50% 50% 42% 42%",
        filter: "blur(.4px)",
        transformOrigin: "50% 100%",
        animation: "torchFlame 0.5s steps(3) infinite"
      },
      scanStyle: this.props.scanlines === false ? { display: "none" } : { position: "fixed", inset: 0, zIndex: 99, pointerEvents: "none", willChange: "transform", transform: "translateZ(0)", backgroundImage: "repeating-linear-gradient(180deg, rgba(0,0,0,.16) 0 1px, transparent 1px 3px)", mixBlendMode: "multiply" },
      isTitle: s.screen === "title", isHow: s.screen === "how", isBoard: s.screen === "board", isModes: s.screen === "modes", isRelicBook: s.screen === "relbook",
      isTitleStone: s.screen === "title" && this.props.uiStyle === "stone", isTitleClassic: s.screen === "title" && this.props.uiStyle !== "stone",
      /* dust motes catching the torchlight — replaces the sparkles baked into the plate art */
      sparkles: (s.screen === "title" ? (this._sparks || (this._sparks = Array.from({ length: (window.innerWidth <= 520 || (navigator.hardwareConcurrency || 8) <= 4) ? 16 : 26 }, (_, i) => {
        const r = ((i * 9301 + 49297) % 233280) / 233280, r2 = ((i * 4523 + 1013) % 65536) / 65536, r3 = ((i * 7919 + 271) % 32768) / 32768;
        const sz = r3 < 0.18 ? 3 : r3 < 0.6 ? 2 : 1;
        return { key: "sp" + i, style: { position: "absolute", left: (4 + r * 92).toFixed(1) + "%", top: (16 + r2 * 78).toFixed(1) + "%",
          width: sz + "px", height: sz + "px", background: r3 < 0.3 ? "#FFF3D0" : "#FFFFFF",
          boxShadow: sz > 1 ? "0 0 " + (sz + 2) + "px rgba(255,232,178,.85)" : "none", opacity: 0,
          animation: "twinkle " + (2.6 + r2 * 4.4).toFixed(2) + "s ease-in-out " + (r * 5).toFixed(2) + "s infinite" } };
      }))) : []),
      isModesStone: s.screen === "modes" && this.props.uiStyle === "stone", isModesClassic: s.screen === "modes" && this.props.uiStyle !== "stone",
      inRun: s.screen === "run", isOver: s.screen === "over",
      isDraft: s.stage === "draft", isCombat: s.stage === "combat" || s.stage === "decide" || s.stage === "revive" || s.stage === "travel" || s.stage === "merchant", isDecide: s.stage === "decide",
      merchantInSlot: !!(s.enemy && s.enemy.merchant),
      foeStatsOn: !(s.enemy && s.enemy.merchant),
      merchantCaption: "Sells curios. Does not fight.",
      merchantPortrait: { position: "absolute", inset: 0, width: "100%", height: "100%", backgroundImage: "url('assets/merchant.png')", backgroundSize: "contain", backgroundPosition: "center bottom", backgroundRepeat: "no-repeat", imageRendering: "pixelated", animation: "breathe 2.6s ease-in-out infinite" },
      isShop: s.stage === "shop",
      deskHeroSolo: s.screen === "run" && !(s.stage === "combat" || s.stage === "decide" || s.stage === "revive" || s.stage === "travel" || s.stage === "merchant"),
      shopTitle: "THE HOARD BAZAAR", shopKicker: (this.G && this.G.mode === "versus") ? "AFTER ROUND 4 \u00b7 RELIC SHOP" : "FLOOR " + SHOP_FLOOR + " \u00b7 RELIC SHOP",
      shopSub: "ONE deal per visit: buy a new relic, or awaken one you carry \u2014 an awakened relic counts as two copies. Choose well.",
      shopBuyTitle: "FOR SALE \u00b7 " + SHOP_BUY + "g", shopUpTitle: "AWAKEN A RELIC \u00b7 " + SHOP_UP + "g", shopSocketHint: "SOCKET INTO WHICH RELIC?", shopLeaveLabel: "LEAVE THE SHOP \u25be",
      shopGoldLine: (this.G ? this.G.gold : s.gold) + "g",
      shopDone: !!(this.G && this.G.shopDone),
      shopRows: (this.G && this.G.shopOffer ? this.G.shopOffer : []).map(id => ({
        key: id, glyph: ricon(id, 42), slot: rslot(id, { width: "46px", height: "46px", flex: "0 0 auto" }), name: this.itemName(id), desc: this.richDesc(this.itemDesc(id)),
        kind: ITEMS[id].kind || "",
        kindStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "6.5px", letterSpacing: "1px", padding: "1px 5px", flex: "0 0 auto", border: "1px solid " + ((KIND_META[ITEMS[id].kind] || {}).c || "#4A3F2E") + "66", color: (KIND_META[ITEMS[id].kind] || {}).c || "#8B8172" },
        wheelOn: !!ITEMS[id].wheel,
        wheel: ITEMS[id].wheel ? ITEMS[id].wheel[0] + " \u2192 " + ITEMS[id].wheel[1] : "",
        wheelStyle: ITEMS[id].wheel ? { fontFamily: "'Silkscreen',monospace", fontSize: "6.5px", letterSpacing: "1px", padding: "1px 5px", flex: "0 0 auto", border: "1px solid #4A3F2E", color: "#E7E0D2", background: "linear-gradient(90deg," + ({ BLOOD: "#C4595C", GOLD: "#E3B341", GRACE: "#9B8BD0" })[ITEMS[id].wheel[0]] + "33," + ({ BLOOD: "#C4595C", GOLD: "#E3B341", GRACE: "#9B8BD0" })[ITEMS[id].wheel[1]] + "33)" } : {},
        buy: () => this.shopBuy(id),
        style: (() => { const ok = s.gold >= SHOP_BUY && !(this.G && this.G.shopDone); return { all: "unset", boxSizing: "border-box", display: "flex", alignItems: "center", gap: "12px", padding: "10px 12px", border: "2px solid " + (ok ? "#3A3328" : "#26211A"), cursor: ok ? "pointer" : "default", opacity: ok ? 1 : 0.45, background: "#14110C" }; })(),
        price: SHOP_BUY + "g"
      })),
      shopComps: (this.G && this.G.shopAwake ? this.G.shopAwake : []).map(e => ({
        key: "aw" + e.i, pick: () => this.shopAwaken(e.i),
        name: "\u2726 " + this.itemName(e.id), kindTag: "AWAKEN",
        desc: AWAKE_TEXT[e.id] || "Awaken this copy: its effects run deeper.",
        price: Math.round(SHOP_UP * (count(this.state.items, "merchantthumb") ? 0.8 : 1)) + "g",
        style: { all: "unset", boxSizing: "border-box", display: "flex", alignItems: "center", gap: "10px", padding: "9px 12px", border: "2px solid #3A3328", cursor: "pointer", background: "#14110C", opacity: (this.G && this.G.shopDone) ? .4 : 1 }
      })),
      shopCompsUnused: [], _shopOld: (this.G && this.G.shopComps ? [] : []).map(cid => {
        const c = COMPONENTS[cid], isTrig = !!TRIGGERS[cid];
        const picked = s.socketPick === cid;
        return {
          key: cid, name: (isTrig ? "\u25b8 " : "\u25c6 ") + c.n, desc: c.d, price: SHOP_UP + "g",
          pick: () => this.setState({ socketPick: picked ? null : cid }),
          style: { all: "unset", boxSizing: "border-box", display: "flex", alignItems: "center", gap: "12px", padding: "10px 12px",
            border: "2px solid " + (picked ? "#E3B341" : (s.gold >= SHOP_UP ? "#7A5E1D" : "#26211A")),
            cursor: s.gold >= SHOP_UP ? "pointer" : "default", opacity: s.gold >= SHOP_UP ? 1 : 0.45, background: picked ? "#221B08" : "#161208" }
        };
      }),
      socketTargets: (() => {
        if (!s.socketPick) return [];
        const seen = {}, G = this.G, socks = (G && G.sockets) || {};
        return s.items.map((id, i) => ({ id, i })).filter(e => { if (seen[e.id] || socks[e.i] != null) return false; seen[e.id] = 1; return true; }).map(e => { const id = e.id; return ({
          key: id + "@" + e.i, glyph: ricon(id, 38), slot: rslot(id, { width: "42px", height: "42px", flex: "0 0 auto" }), name: this.itemName(id),
          sock: () => this.shopSocket(s.socketPick, e.i),
          style: { all: "unset", boxSizing: "border-box", display: "flex", alignItems: "center", gap: "10px", padding: "8px 11px", border: "2px solid #3A3328", cursor: "pointer", background: "#14110C" }
        }); });
      })(),
      socketPicking: !!s.socketPick,
      socketPickName: s.socketPick && COMPONENTS[s.socketPick] ? COMPONENTS[s.socketPick].n : "",
      shopCompsOn: !!(this.G && this.G.shopAwake && this.G.shopAwake.length),
      shopLeave: () => this.shopLeave(),
      floorArrowStyle: (() => {
        if (this.G && this.G.mode === "versus") {   /* rail = the 8 delvers' halls; the arrow sits on the one hosting this duel */
          const G = this.G, host = (G.duelHall && !G.duelHall.home && G.vsFoe != null) ? G.vsFoe + 1 : 0;
          const N = 1 + (G.vsRoster ? G.vsRoster.length : 7);
          const sizes = Array.from({ length: N }, (_, i) => i === 0 ? 11 : 9);
          const totalW = sizes.reduce((a, b) => a + b, 0), nLinks = N - 1;
          const fixed = 2 + sizes.slice(0, host).reduce((a, b) => a + b, 0) + sizes[host] / 2;
          const r = nLinks ? host / nLinks : 0;
          return { position: "absolute", top: "0",
            left: "calc(" + (fixed - r * (4 + totalW)).toFixed(2) + "px + " + (r * 100).toFixed(3) + "%)",
            transform: "translateX(-50%)", width: 0, height: 0,
            borderLeft: "4px solid transparent", borderRight: "4px solid transparent",
            borderTop: "6px solid #E3B341", filter: "drop-shadow(0 0 4px rgba(227,179,65,.7))",
            transition: "left 1.2s ease-in-out", zIndex: 2 };
        }
        const f = s.floor;
        const evAhead = this.G && this.G.eventGaps && this.G.eventGaps[f] != null;
        const half = s.stage === "event" || (s.stage === "travel" && evAhead && this.G.eventSeen && !this.G.eventSeen[f]);
        const sizes = Array.from({ length: MAX_FLOOR }, (_, i) => { const cur = i === f - 1, boss = i === MAX_FLOOR - 1; return boss ? (cur ? 15 : 12) : (cur ? 11 : 7); });
        const totalW = sizes.reduce((a, b) => a + b, 0), nLinks = MAX_FLOOR - 1;
        let fixed, linkMult;
        if (half) { // midpoint of the link carrying the event diamond (between dots f-1 and f)
          fixed = 2 + sizes.slice(0, f).reduce((a, b) => a + b, 0); linkMult = f - 0.5;
        } else {
          const idx = s.stage === "travel" ? f : f - 1;
          fixed = 2 + sizes.slice(0, idx).reduce((a, b) => a + b, 0) + sizes[idx] / 2; linkMult = idx;
        }
        const r = linkMult / nLinks;
        const left = "calc(" + (fixed - r * (4 + totalW)).toFixed(2) + "px + " + (r * 100).toFixed(3) + "%)";
        return { position: "absolute", top: "0", left: left, transform: "translateX(-50%)",
          width: 0, height: 0, borderLeft: "4px solid transparent", borderRight: "4px solid transparent",
          borderTop: "6px solid #E3B341", filter: "drop-shadow(0 0 4px rgba(227,179,65,.7))",
          transition: "left 2.4s ease-in-out", zIndex: 2 };
      })(),
      isEvent: !!evDef, eventKicker: "BETWEEN FLOORS", eventTitle: evDef ? evDef.title : "", eventText: evDef ? evDef.text : "",
      eventArtStyle: (() => {
        const art = evDef && (s.event.outcome ? (s.event.outcomeArt || evDef.art) : evDef.art);
        return {
          width: "100%", height: art ? "auto" : "110px",
          alignSelf: "stretch", flex: art ? "1 1 auto" : "0 0 auto", minHeight: art ? "90px" : undefined,
          boxSizing: "border-box", marginTop: "14px",
          border: "2px solid #2B261D", background: "#12100C", position: "relative",
          display: "flex", alignItems: "center", justifyContent: "center",
          imageRendering: "pixelated", backgroundSize: art ? "contain" : "cover", backgroundPosition: "center", backgroundRepeat: "no-repeat",
          backgroundImage: art ? "url('" + art + "')" : "repeating-linear-gradient(45deg, #14110C 0 8px, #100E0A 8px 16px)",
          animation: "rise .3s .03s ease both"
        };
      })(),
      eventArtEmpty: !(evDef && (s.event.outcome ? (s.event.outcomeArt || evDef.art) : evDef.art)),
      eventChoosing: !!(evDef && !s.event.outcome), eventDone: !!(evDef && s.event.outcome), eventOutcome: evDef ? (s.event.outcome || "") : "",
      eventOutcomeStyle: { fontSize: "15px", fontWeight: 700, lineHeight: 1.55, marginTop: "24px", color: s.event && s.event.good ? "#7C9A6A" : "#C4593C", animation: "rise .3s ease both" },
      eventChoices: evDef ? evDef.choices.map((ch, i) => {
        const can = !ch.cost || s.gold >= ch.cost;
        return { label: ch.label, hint: ch.hint ? (ch.hint + (ch.cost && !can ? " · NOT ENOUGH GOLD" : "")) : "",
          go: can ? () => this.chooseEvent(i) : () => {},
          style: { all: "unset", boxSizing: "border-box", display: "block", width: "100%", padding: "14px 16px", border: "2px solid #3A3328", background: "#15120D", cursor: can ? "pointer" : "default", opacity: can ? 1 : 0.4 } };
      }) : [],
      eventGo: () => this.eventGo(), eventGoLabel: "CONTINUE THE DESCENT",
      draftDetailName: (() => { const id = s.draftPeek || (s.offer || [])[0]; return id ? this.itemName(id) : ""; })(),
      draftDetailDesc: (() => { const id = s.draftPeek || (s.offer || [])[0]; return id ? this.richDesc(this.itemDesc(id)) : ""; })(),
      draftDetailWheelOn: (() => { const id = s.draftPeek || (s.offer || [])[0]; return !!(id && ITEMS[id] && ITEMS[id].wheel); })(),
      draftDetailWheel: (() => { const id = s.draftPeek || (s.offer || [])[0]; return (id && ITEMS[id] && ITEMS[id].wheel) ? ITEMS[id].wheel[0] + " \u2192 " + ITEMS[id].wheel[1] : ""; })(),
      draftDetailWheelStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "6.5px", letterSpacing: "1px", padding: "1px 5px", flex: "0 0 auto", border: "1px solid #4A3F2E", color: "#E7E0D2" },
      reroll: () => this.rerollDraft(),
      rerollLabel: "REROLL — " + (this.G ? this.rerollPrice(this.G) : 10) + " GOLD",
      rerollSub: "YOU HAVE " + s.gold + " GOLD · PRICE DOUBLES EACH USE",
      rerollLabelUnused: "",
      rerollPriceTxt: String(this.G ? this.rerollPrice(this.G) : 10),
      rerollCoin: { width: "11px", height: "11px", display: "inline-block", backgroundImage: "url('assets/wallet-coin.png')", backgroundSize: "contain", backgroundPosition: "center", backgroundRepeat: "no-repeat", imageRendering: "pixelated" },
      rerollStyle: (this.G && s.gold >= this.rerollPrice(this.G)) ? { all: "unset", boxSizing: "border-box", display: "inline-flex", alignItems: "center", justifyContent: "center", gap: "9px", padding: "11px 20px", border: "2px dashed #3A3328", textAlign: "center", cursor: "pointer" } : { all: "unset", boxSizing: "border-box", display: "inline-flex", alignItems: "center", justifyContent: "center", gap: "9px", padding: "11px 20px", border: "2px dashed #2B261D", textAlign: "center", cursor: "default", opacity: 0.4 },

      cogGlyph: glyph("cog", 1.5),
      wordmarkStyle: { fontFamily: "'Baloo 2',sans-serif", fontSize: "42px", lineHeight: 1, fontWeight: 800, letterSpacing: "1px", color: "#F0E6D2", textShadow: s.supporter ? "0 4px 0 #241F1A, 0 3px 6px rgba(0,0,0,.95), 0 0 22px rgba(227,179,65,.55)" : "0 4px 0 #241F1A, 0 3px 6px rgba(0,0,0,.95), 0 6px 14px rgba(0,0,0,.7)" },
      tagline: this.t("tagline"), seedLabel: String(dailySeed()).slice(4), countdown: s.countdown,
      /* wallet pills always read the live stores, so banked gold/sparks show the moment you land back on the menu */
      sparks: Store.get("dd.sparks", s.sparks || 0) | 0, wallet: Store.get("dd.gold", 0) | 0,
      sparksWord: this.t("sparksWord"), best: s.best,
      dailyLabel: this.t("dailyChallenge"), dailyTag: s.dailyDone ? "DONE ✓" : "13 FLOORS",
      dailyBtnStyle: { all: "unset", boxSizing: "border-box", display: "flex", alignItems: "center", justifyContent: "space-between", padding: "17px 18px", background: s.dailyDone ? "transparent" : accent, color: s.dailyDone ? "#8B8172" : "#14120F", border: "2px solid " + (s.dailyDone ? "#3A3328" : accent), boxShadow: s.dailyDone ? "none" : "4px 4px 0 #7A5E1D", cursor: "pointer", fontWeight: 700, fontSize: "15px", letterSpacing: ".3px" },
      practiceLabel: this.t("play"), rankingsLabel: this.t("rankings"), howLabel: this.t("howToPlay"),
      backLabel: this.t("back"), closeLabel: this.t("close"), homeLabel: this.t("home"),
      startDaily: () => this.startDaily(), startPractice: () => this.startPractice(), startVersus: () => this.openStaging(),
      dailyLocked: META.level() < 3, versusLocked: META.level() < 5,
      startDailyGated: () => { if (META.level() < 3) { SFX.tap(); return; } this.startDaily(); },
      startVersusGated: () => { if (META.level() < 5) { SFX.tap(); return; } this.openStaging(); },
      dailyGateStyle: { all: "unset", boxSizing: "border-box", display: "block", cursor: META.level() < 3 ? "default" : "pointer", filter: META.level() < 3 ? "grayscale(1) brightness(.75)" : "none" },
      versusGateStyle: { all: "unset", boxSizing: "border-box", display: "block", cursor: META.level() < 5 ? "default" : "pointer", filter: META.level() < 5 ? "grayscale(1) brightness(.75)" : "none" },
      dailyMetaL: META.level() < 3 ? "LOCKED" : "TODAY · " + (s.countdown || ""),
      dailyMetaR: META.level() < 3 ? "REACH DELVER LV 3 IN DUNGEONS" : "⭐ BEST " + (s.best || 0) + " · " + (this.G && this.G.isDaily ? "IN PROGRESS" : (s.dailyDone ? "DONE TODAY" : "1 TRY")),
      versusMetaL: META.level() < 5 ? "LOCKED" : "8 DELVERS · 3 LIVES EACH",
      versusMetaR: META.level() < 5 ? "REACH DELVER LV 5" : "LAST DELVER STANDING",
      ico: (() => { const I = (c, r) => ({ position: "absolute", inset: 0, backgroundImage: "url(assets/icons.webp)", backgroundRepeat: "no-repeat", backgroundSize: "1066.67% 600%", backgroundPosition: ((70 + 200 * c) / 17.4) + "% " + ((50 + 200 * r) / 9) + "%" }); return { chest: I(0, 4), hood: I(4, 1), trophy: I(3, 4), scroll: I(5, 3), diamond: I(4, 4), coin: I(1, 4), hourglass: I(3, 0), pickaxe: I(7, 1), sword: I(6, 1), target: I(1, 0), lock: I(3, 3), crown: I(0, 0), gear: I(5, 4) }; })(),
      openHow: () => this.setState({ screen: "how" }), goTitle: () => this.setState({ screen: "title" }),
      openModes: () => { SFX.tap(); this.setState({ screen: "modes" }); },
      openLevels: () => { SFX.tap(); this.setState({ screen: "levels", lvSel: s.lvSel || META.unlocked() }); },
      /* ---- Staging Hall (versus pre-match) ---- */
      isStaging: s.screen === "staging",
      stgAssembling: true,
      stgRosterLabel: s.stgFill ? "ASSEMBLING DELVERS \u00b7 " + (s.stgTick || 0) + "s" : "8 DELVERS \u00b7 3 LIVES \u00b7 LAST ONE STANDING",
      stgTick: s.stgTick || 0,
      stgHallName: (DUNGEONS[Math.max(0, Math.min(DUNGEONS.length - 1, (s.stgHall || 1) - 1))] || {}).name || "",
      stgPreviewStyle: { flex: "0 0 auto", position: "relative", marginTop: "12px", height: "118px", border: "2px solid #6B5A3E", boxShadow: "inset 0 0 0 2px #2A2118", overflow: "hidden" },
      stgPreviewArt: { position: "absolute", inset: 0, backgroundImage: "url('" + ((DUNGEONS[Math.max(0, Math.min(DUNGEONS.length - 1, (s.stgHall || 1) - 1))] || {}).art || BATTLE_BG) + "')", backgroundSize: "cover", backgroundPosition: "center 40%", imageRendering: "pixelated" },
      stgHalls: DUNGEONS.map(D => {
        const open = D.id <= META.unlocked(), sel = (s.stgHall || 1) === D.id;
        return { key: "sh" + D.id, name: open ? D.name : "CLEAR D" + (D.id - 1),
          pick: open ? () => { SFX.tap(); this.setState({ stgHall: D.id }); } : () => {},
          style: { all: "unset", boxSizing: "border-box", flex: "0 0 auto", width: "84px", cursor: open ? "pointer" : "default",
            border: "2px solid " + (sel ? "#E3B341" : "#2B261D"), opacity: open ? 1 : 0.4, display: "block" },
          thumb: { display: "block", width: "100%", height: "44px", backgroundImage: "url('" + (D.art || BATTLE_BG) + "')", backgroundSize: "cover", backgroundPosition: "center 40%", imageRendering: "pixelated", filter: open ? "none" : "grayscale(1) brightness(.5)" },
          label: { display: "block", padding: "4px 5px", fontFamily: "'Silkscreen',monospace", fontSize: "6.5px", letterSpacing: ".5px", lineHeight: 1.3, color: sel ? "#E3B341" : "#8B8172", background: "#14120F", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" } };
      }),
      stgSlots: (() => {
        const names = ["Bramblejaw", "Mole Widow", "Grim Patty", "Sootfinger", "Old Lantern", "The Tithe", "Knucklebone"];
        const filled = s.stgFill ? Math.min(7, Math.max(0, 10 - (s.stgTick || 0))) : 0;
        return [{ key: "me", text: (s.name || "Delver") + " · LV " + META.level(), on: true }]
          .concat(names.map((n, i) => ({ key: "r" + i, text: i < filled ? n + " · LV " + Math.max(1, META.level() + (i % 3) - 1) : (s.stgFill ? "waiting\u2026" : "\u2014"), on: i < filled })))
          .map(r => ({ key: r.key, text: r.text, style: { padding: "7px 10px", border: "2px solid " + (r.on ? "#3A3328" : "#211D16"), background: r.on ? "#191510" : "transparent", fontFamily: "'Silkscreen',monospace", fontSize: "8.5px", letterSpacing: ".5px", color: r.on ? "#E7E0D2" : "#5C5648" } }));
      })(),
      leaveStaging: () => this.leaveStaging(),
      stgAction: () => s.stgFill ? this.skipStaging() : this.enterStaging(),
      stgBtnLabel: s.stgFill ? "SKIP · FILL NOW" : "ENTER THE DUNGEON",
      stgBtnStyle: { all: "unset", boxSizing: "border-box", display: "block", width: "100%", textAlign: "center", padding: "15px",
        background: s.stgFill ? "linear-gradient(180deg,#3B3122,#1E1810)" : "linear-gradient(180deg,#B08BD8,#7B5E9E)",
        color: s.stgFill ? "#EBDFC6" : "#160F1B", border: "2px solid " + (s.stgFill ? "#9C824F" : "#5A4468"),
        boxShadow: "0 5px 0 #16110A,0 8px 14px rgba(0,0,0,.55)", cursor: "pointer",
        fontFamily: "'Silkscreen',monospace", fontSize: "13px", letterSpacing: "1.5px" },
      isLevels: s.screen === "levels",
      levelCards: DUNGEONS.map(D => {
        const frontier = META.unlocked(), unlocked = D.id <= frontier, cleared = D.id < frontier, sel = (s.lvSel || frontier) === D.id;
        return {
          key: "lv" + D.id, id: D.id,
          pick: () => { SFX.tap(); this.setState({ lvSel: D.id }); },
          box: { all: "unset", boxSizing: "border-box", width: "72px", height: "72px", display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", gap: "3px", position: "relative", cursor: "pointer",
            border: "2px solid " + (sel ? "#E3B341" : unlocked ? "#4A4436" : "#2B261D"),
            background: sel ? "#2A2312" : unlocked ? "#191510" : "#131110",
            boxShadow: sel ? "3px 3px 0 rgba(0,0,0,.5)" : "none" },
          num: unlocked ? String(D.id) : "",
          numStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "19px", color: sel ? "#E3B341" : cleared ? "#7C9A6A" : "#E7E0D2" },
          lockOn: !unlocked,
          clearedOn: cleared,
          tag: cleared ? "CLEARED" : unlocked ? "OPEN" : "",
          tagStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "5.5px", letterSpacing: "1px", color: cleared ? "#7C9A6A" : "#8B8172" }
        };
      }),
      levelInfo: (() => {
        const frontier = META.unlocked(), id = Math.max(1, Math.min(DUNGEONS.length, s.lvSel || frontier));
        const D = DUNGEONS[id - 1], known = id <= frontier;
        if (!known) return { title: "DUNGEON " + id, mystery: true, lore: "Sealed. Clear Dungeon " + (id - 1) + " to learn what waits here.",
          rows: [], relics: "\u2014", kingName: "???", kingArt: {}, hasArt: false, playLabel: "SEALED", playStyle: { all: "unset", boxSizing: "border-box", flex: "0 0 auto", display: "block", width: "100%", textAlign: "center", padding: "13px", background: "#14120F", border: "2px solid #2B261D", color: "#4A4436", fontFamily: "'Silkscreen',monospace", fontSize: "10px", letterSpacing: "2px", cursor: "default" }, play: () => {} };
        const pack = packFor(MAX_FLOOR, mulberry32(7), { bossRelics: D.bossRelics });
        const king = pack[pack.length - 1], M = D.mult;
        const sc = v => Math.round((v || 0) * M);
        return {
          title: D.name.toUpperCase(), mystery: false, lore: D.lore || D.fl,
          rows: [
            { key: "hp", k: "HP", v: String(sc(king.hp)) },
            { key: "atk", k: "ATK", v: String(sc(king.atk)) },
            { key: "def", k: "DEF", v: String(king.armor || 0) },
            { key: "spd", k: "SPD", v: String(king.spd || 20) },
            { key: "sc", k: "SCORE", v: "\u00d7" + (Math.round(M * 100) / 100) },
            { key: "lv", k: "FOR LV", v: String(D.lvl || 1) },
            { key: "rw", k: "XP RATE", v: "\u00d7" + META.rewardMult(D.id).toFixed(2) }
          ],
          relics: (D.bossRelics || []).length ? D.bossRelics.map(r => this.itemName(r)).join(" \u00b7 ") : "none \u2014 raw strength only",
          kingName: "The Hoard-King",
          kingArt: enemySprite(9, 0, 46, null), hasArt: true,
          playLabel: "DELVE \u203a",
          playStyle: { all: "unset", boxSizing: "border-box", flex: "0 0 auto", display: "block", width: "100%", textAlign: "center", padding: "13px", border: "2px solid #E3B341", background: "#E3B341", color: "#14120F", fontFamily: "'Silkscreen',monospace", fontSize: "10px", letterSpacing: "2px", cursor: "pointer", boxShadow: "4px 4px 0 #6B5A24" },
          play: () => { SFX.tap(); Store.set("dd.tier", id); this.startPractice(); }
        };
      })(),
      playFeatured: (() => {   /* the title PLAY routes to the best UNLOCKED mode: daily (LV3+) > versus (LV5+, daily done) > dungeons */
        const lvl = META.level();
        if (lvl < 3) return () => { SFX.tap(); this.setState({ screen: "levels", lvSel: META.unlocked() }); };
        if (s.dailyDone) return lvl >= 5 ? () => this.openStaging() : () => { SFX.tap(); this.setState({ screen: "levels", lvSel: META.unlocked() }); };
        return () => this.startDaily();
      })(),
      featuredKicker: META.level() < 3 ? "DUNGEONS \u00b7 DAILY UNLOCKS AT LV 3" : (s.dailyDone ? (META.level() >= 5 ? "VERSUS \u00b7 8 DELVERS \u00b7 3 LIVES" : "DUNGEONS \u00b7 VERSUS UNLOCKS AT LV 5") : "TODAY'S DELVE \u00b7 ONE TRY"),
      featuredSub: META.level() < 3 ? "Clear dungeons to reach Delver LV 3." : (s.dailyDone ? (META.level() >= 5 ? "Today's delve is done \u2014 the arena is open." : "Today's delve is done \u2014 back to the dungeons.") : "Seed " + String(dailySeed()).slice(4) + " \u00b7 resets in " + s.countdown),
      statLvl: META.level(), statBest: s.best || 0, statCrowns: Store.get("dd.vsCrowns", 0) | 0,
      xpBarStyle: { width: Math.round(META.progress(META.xp()) * 100) + "%", height: "100%", background: "linear-gradient(180deg,#E3B341,#B8871F)", boxShadow: "inset 0 1px 0 rgba(255,240,190,.6)" },

      modeBanner: (() => { const B = (cx, cy) => ({ position: "absolute", inset: 0, backgroundImage: "url('assets/mode-banners.png')",
        backgroundSize: "200% 200%", backgroundPosition: (cx * 100) + "% " + (cy * 100) + "%", backgroundRepeat: "no-repeat", imageRendering: "pixelated" });
        return { daily: B(0, 0), versus: B(1, 0), dungeons: B(0, 1) }; })(),
      navIco: (() => { const I = i => ({ position: "absolute", inset: 0, backgroundImage: "url('assets/nav-icons.png')",
        backgroundSize: "400% 100%", backgroundPosition: (i * 100 / 3) + "% 0", backgroundRepeat: "no-repeat", imageRendering: "pixelated" });
        return { trophy: I(0), chest: I(1), statue: I(2), gear: I(3) }; })(),
      openRelicBook: () => { SFX.tap(); this.setState({ screen: "relbook" }); },
      openBestiary: () => { SFX.tap(); this.setState({ screen: "bestiary" }); },
      isBestiary: s.screen === "bestiary",
      bestiaryCount: (() => { const seen = Store.get("dd.seen", {}) || {}; return Object.keys(seen).length + " / " + ENEMY_G.length + " FOUND"; })(),
      bestiaryRows: ENEMY_G.map((g, i) => {
        const seen = (Store.get("dd.seen", {}) || {})[i];
        return {
          key: "be" + i,
          open: seen ? () => { SFX.tap(); this.setState({ modal: "enemy", bestIdx: i }); } : () => {},
          rowCursor: seen ? "pointer" : "default",
          name: seen ? this.t("en" + i) : "? ? ?",
          lore: seen ? "\u201c" + (ENEMY_LORE[i] || ENEMY_LORE[0]) + "\u201d" : "Not yet met. The dark keeps its own records.",
          art: seen ? enemySprite(i, 0, 46, { position: "relative", top: 0, left: 0, transform: "none" })
                    : { width: "46px", height: "46px", display: "flex", alignItems: "center", justifyContent: "center", fontFamily: "'Silkscreen',monospace", fontSize: "20px", color: "#4A4436" },
          mark: seen ? "" : "?",
          slot: { width: "56px", height: "56px", flex: "0 0 auto", boxSizing: "border-box", display: "flex", alignItems: "center", justifyContent: "center",
            border: "2px solid " + (seen ? "#6B5A3E" : "#2B261D"), background: seen ? "linear-gradient(180deg,#241E16,#141009)" : "#141009",
            filter: seen ? "none" : "grayscale(1)" },
          nameStyle: { color: seen ? "#E7E0D2" : "#5C5648", fontWeight: 700, fontSize: "14px" },
          loreStyle: { color: seen ? "#8B8172" : "#4A4436", fontSize: "11.5px", lineHeight: 1.5, marginTop: "3px", fontStyle: seen ? "normal" : "italic" }
        };
      }),
      openProfile: () => { SFX.tap(); this.setState({ screen: "profile" }); },
      isProfile: s.screen === "profile",
      profXp: META.xp(), profLvl: META.level(),
      profNext: META.level() >= 20 ? "MAX LEVEL" : Math.round(META.progress(META.xp()) * 100) + "% TO LEVEL " + (META.level() + 1),
      profXpBar: { width: Math.round(META.progress(META.xp()) * 100) + "%", height: "100%", background: "#7C9A6A" },
      profPack: s.supporter ? "SUPPORTER PACK \u2726" : "BASIC",
      profPackStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "9px", letterSpacing: "1px", padding: "4px 10px", border: "2px solid " + (s.supporter ? "#E3B341" : "#3A3328"), color: s.supporter ? "#E3B341" : "#8B8172" },
      profPerks: Array.from({ length: 19 }, (_, i) => i + 2).map(l => { const lv = +l, got = META.level() >= lv;
        return { key: "pl" + l, lvl: "L" + l, label: META.perkDesc(lv), got,
          style: { display: "flex", alignItems: "center", gap: "10px", padding: "8px 10px", border: "2px solid " + (got ? "#3E4A38" : "#211D16"), background: got ? "#151A12" : "#12100D", opacity: got ? 1 : 0.55 },
          lvlStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "9px", width: "32px", flex: "0 0 auto", color: got ? "#7C9A6A" : "#655C4E" },
          txtStyle: { fontSize: "12px", color: got ? "#E7E0D2" : "#8B8172" },
          mark: got ? "\u2713" : "\u25cb", markStyle: { marginLeft: "auto", fontFamily: "'Silkscreen',monospace", fontSize: "10px", color: got ? "#7C9A6A" : "#4A4436" } }; }),
      profAchv: META.ACHV.map(a => { const got = a.ok();
        return { key: a.id, n: a.n, d: a.d, got,
          style: { display: "flex", alignItems: "center", gap: "10px", padding: "9px 10px", border: "2px solid " + (got ? "#4A3F2E" : "#211D16"), background: got ? "#1A1610" : "#12100D", opacity: got ? 1 : 0.55 },
          nStyle: { fontSize: "12.5px", fontWeight: 700, color: got ? "#E3B341" : "#8B8172" },
          dStyle: { fontSize: "11px", color: "#8B8172", marginTop: "2px" },
          mark: got ? "\u2726" : "\u25cb", markStyle: { marginLeft: "auto", fontFamily: "'Silkscreen',monospace", fontSize: "13px", color: got ? "#E3B341" : "#4A4436", flex: "0 0 auto" } }; }),
      profAchvCount: META.ACHV.filter(a => a.ok()).length + " / " + META.ACHV.length,
      profStats: (() => { const b = META.bonuses(META.level());
        return [["HP", 100 + b.hp, b.hp], ["ATK", 5 + b.atk, b.atk], ["DEF", 0 + b.def, b.def], ["SPD", 25 + b.spd, b.spd], ["LCK", 10 + b.lck, b.lck]].map(([k, v, d]) => ({
          key: k, k, v, d: d ? "+" + d : "", dStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "7px", color: "#7C9A6A", marginTop: "2px", minHeight: "9px" } })); })(),
      bookRelics: s.screen === "relbook" ? POOL.map(id => ({ key: id, glyph: ricon(id, 40), slot: rslot(id, { width: "44px", height: "44px", flex: "0 0 auto" }), name: this.itemName(id), kind: ITEMS[id].kind, kindCol: kindColor(ITEMS[id].kind), wheelOn: !!ITEMS[id].wheel, wheel: ITEMS[id].wheel ? ITEMS[id].wheel[0] + " \u2192 " + ITEMS[id].wheel[1] : "", wheelStyle: ITEMS[id].wheel ? { fontFamily: "'Silkscreen',monospace", fontSize: "6.5px", letterSpacing: "1px", padding: "1px 5px", border: "1px solid #4A3F2E", color: "#E7E0D2", background: "linear-gradient(90deg," + ({ BLOOD: "#C4595C", GOLD: "#E3B341", GRACE: "#9B8BD0" })[ITEMS[id].wheel[0]] + "33," + ({ BLOOD: "#C4595C", GOLD: "#E3B341", GRACE: "#9B8BD0" })[ITEMS[id].wheel[1]] + "33)" } : {}, desc: this.itemDesc(id), open: () => { SFX.tap(); this.setState({ modal: "relic", relicId: id, relicIdx: null }); } })) : [],
      titleHeroGlyph: Object.assign({}, heroSprite(composeHero32(wardrobeCombo()), heroPal, 2), { width: "210%", height: "210%", top: "-10%", left: "50%", transform: "translateX(-50%)" }),
      /* supporter crown: breaks the frame's top-left corner so it reads at thumbnail size */
      supporterOn: !!s.supporter,
      avatarFrameStyle: { position: "relative", flex: "0 0 auto" },
      avatarBorder: s.supporter ? "#C9A227" : "#6B5A3E",
      crownStyleSm: { position: "absolute", left: "-13px", top: "-19px", width: "34px", height: "31px", transform: "rotate(-18deg)", backgroundImage: "url('assets/crown-supporter.png')", backgroundSize: "contain", backgroundPosition: "center", backgroundRepeat: "no-repeat", imageRendering: "pixelated", filter: "drop-shadow(0 2px 2px rgba(0,0,0,.75))", pointerEvents: "none", overflow: "hidden" },
      crownStyleMd: { position: "absolute", left: "-16px", top: "-22px", width: "42px", height: "38px", transform: "rotate(-18deg)", backgroundImage: "url('assets/crown-supporter.png')", backgroundSize: "contain", backgroundPosition: "center", backgroundRepeat: "no-repeat", imageRendering: "pixelated", filter: "drop-shadow(0 2px 2px rgba(0,0,0,.75))", pointerEvents: "none", overflow: "hidden" },
      crownGlintStyle: { position: "absolute", left: 0, top: "18%", width: "5px", height: "64%", background: "linear-gradient(90deg,rgba(255,255,255,0),rgba(255,255,255,.9),rgba(255,255,255,0))", animation: "crownGlint 6s linear infinite", pointerEvents: "none" },
      openBoard: () => this.setState({ screen: "board" }), closeBoard: () => this.setState({ screen: s.how ? "over" : "title" }),
      goHome: () => { if (this.G && this.G.xpGain > 0 && !s.xpShown && s.screen === "over") return this.showXp(); this.setState({ screen: "title" }); },
      isXpScreen: s.screen === "xp",
      xpKicker: (this.G && this.G.lvlUps && this.G.lvlUps.length && s.xpCelebrate) ? "LEVEL UP" : "EXPERIENCE GAINED",
      xpGainBig: "+" + ((this.G && this.G.xpGain) || 0),
      xpLvlNow: "LEVEL " + (s.xpLvlShown || META.level()),
      xpBarFill: { width: Math.round((s.xpFill || 0) * 100) + "%", height: "100%", background: "#7C9A6A", transition: s.xpSnap ? "none" : "width .9s cubic-bezier(.2,.7,.3,1)" },
      xpNextLabel: (s.xpLvlShown || META.level()) >= 20 ? "MAX LEVEL" : "NEXT: LEVEL " + ((s.xpLvlShown || META.level()) + 1),
      lvlUpOn: !!(this.G && this.G.lvlUps && this.G.lvlUps.length && s.xpCelebrate),
      confetti: (this.G && this.G.lvlUps && this.G.lvlUps.length && s.xpCelebrate) ? Array.from({ length: (window.innerWidth <= 520 || (navigator.hardwareConcurrency || 8) <= 4) ? 22 : 46 }, (_, i) => {
        const cols = ["#E3B341", "#7C9A6A", "#C4593C", "#9B8BD0", "#6FC6E8", "#E7E0D2"];
        const r = ((i * 9301 + 49297) % 233280) / 233280, r2 = ((i * 4523 + 1013) % 65536) / 65536;
        return { key: "cf" + i, style: { position: "absolute", left: Math.round(r * 100) + "%", top: "-4%",
          width: (3 + Math.round(r2 * 4)) + "px", height: (6 + Math.round(r * 7)) + "px", background: cols[i % cols.length],
          "--cx": Math.round((r2 - 0.5) * 130) + "px", "--cr": Math.round(360 + r * 900) + "deg",
          animation: "confetti " + (1.5 + r2 * 1.4).toFixed(2) + "s linear " + (r * 1.1).toFixed(2) + "s infinite" } };
      }) : [],
      lvlUpRows: ((this.G && this.G.lvlUps) || []).map((t, i) => ({ key: "lu" + i, text: t })),
      xpContinue: () => { SFX.tap(); this.setState({ screen: "title" }); },
      openSettings: () => this.setState({ modal: "settings" }),

      howHeadline: this.t("takeRelic"),
      howSteps: this.t("howtoBody").split("<br>").map((x, i) => ({ n: "0" + (i + 1), text: this.strip(x).trim() })),

      lbTitle: this.t("lbTitle"),
      lbTabs: [{ label: this.t("lbLocal"), style: { all: "unset", boxSizing: "border-box", flex: 1, textAlign: "center", padding: "12px 0", cursor: "default", fontFamily: "'Silkscreen',monospace", fontSize: "10px", letterSpacing: "1px", background: accent, color: "#14120F" }, go: () => { } }],
      lbRows: scores.map((r, i) => ({
        rank: String(i + 1).padStart(2, "0"), name: (!r.name || String(r.name).indexOf("autoNameWord") === 0) ? this.t("lbAnon") : r.name, score: r.score,
        meta: this.t("lbMeta", { f: r.floor, k: r.kills }),
        rowStyle: { display: "flex", alignItems: "baseline", gap: "10px", padding: "11px 8px", borderBottom: "1px solid #211D16", background: r.name === s.name ? "rgba(227,179,65,.07)" : "transparent" }
      })),
      lbEmpty: scores.length === 0, lbEmptyText: this.t("lbEmpty"),

      floorLine: (this.G && this.G.mode === "versus")
        ? ((this.G.duelHall ? (((DUNGEONS[Math.max(0, Math.min(DUNGEONS.length - 1, this.G.duelHall.id - 1))] || {}).name || "").replace(/^The /i, "").toUpperCase() + " · " + (this.G.duelHall.home ? "YOUR HALL" : String(this.G.duelHall.owner).toUpperCase() + "'S")) : "ROUND " + s.floor) + " · " + (this.G.vsRoster.filter(b2 => b2.lives > 0).length + 1))
        : this.t("floorN", { n: s.floor }) + " / " + MAX_FLOOR,
      floorNodes: (this.G && this.G.mode === "versus") ? [{ me: true, lives: this.G.vsLives }].concat(this.G.vsRoster).map((b, i) => ({
        link: i === 0 ? { display: "none" } : { flex: "1 1 0", height: "2px", background: "#2B261D", minWidth: "3px", position: "relative" },
        evDot: { display: "none" },
        dot: (() => {
          const G = this.G, hostIdx = (G.duelHall && !G.duelHall.home && G.vsFoe != null) ? G.vsFoe + 1 : 0;
          const hosting = i === hostIdx, out = !b.me && b.lives <= 0;
          const hue = b.me ? accent : ((b.profile && b.profile.accent) || "#8B8172");
          const foe = !b.me && G && G.vsFoe != null && i === G.vsFoe + 1;   /* this round's opponent */
          const lit = b.me || foe;                                          /* the two delvers in this duel */
          return { width: lit ? "11px" : "9px", height: lit ? "11px" : "9px", flex: "0 0 auto", boxSizing: "border-box",
            background: out ? "#241F18" : hue, opacity: out ? 1 : (lit ? 1 : 0.45),
            border: "1px solid " + (hosting ? "#E3B341" : foe ? "#C4593C" : b.me ? "#8B8172" : "#3A3328"), transform: "rotate(45deg)",
            boxShadow: hosting ? "0 0 7px rgba(227,179,65,.8)" : foe ? "0 0 6px rgba(196,89,60,.7)" : b.me ? "0 0 5px rgba(139,129,114,.5)" : "none",
            transition: "opacity .3s ease" };
        })()
      })) : Array.from({ length: MAX_FLOOR }, (_, i) => {
        const vs = false, VMAX = MAX_FLOOR;
        const done = i < s.floor - 1, cur = i === s.floor - 1, boss = i === VMAX - 1, shop = !vs && i === SHOP_FLOOR - 1;
        const sz = boss ? (cur ? 15 : 12) : shop ? (cur ? 13 : 10) : (cur ? 11 : 7);
        return {
          link: i === 0 ? { display: "none" } : { flex: "1 1 0", height: "2px", background: (done || cur) ? "#8B8172" : "#2B261D", minWidth: "3px", position: "relative" },
          evDot: (() => {
            if (i === 0 || !this.G || !this.G.eventGaps || this.G.eventGaps[i] == null) return { display: "none" };
            const active = s.stage === "event" && s.floor === i, passed = s.floor > i;
            return { position: "absolute", left: "50%", top: "50%", width: "7px", height: "7px", transform: "translate(-50%,-50%) rotate(45deg)",
              background: passed ? "#4A4460" : "#9B8BD0", boxShadow: active ? "0 0 9px rgba(155,139,208,.95)" : "none", zIndex: 1 };
          })(),
          click: (i + 1 <= s.floor + 1) ? () => this.openFloorNode(i + 1) : null,
          dot: {
            width: sz + "px", height: sz + "px", flex: "0 0 auto", boxSizing: "border-box",
            cursor: (i + 1 <= s.floor + 1) ? "pointer" : "default",
            background: boss ? ((done || cur) ? accent : "#241F18") : shop ? (done ? "#7A5E1D" : "#241F18") : (done ? "#8B8172" : cur ? accent : "#241F18"),
            border: boss ? ("2px solid " + (cur ? accent : "#7A5E1D")) : shop ? ("2px solid " + (cur ? accent : "#E3B341")) : ("1px solid " + (done ? "#8B8172" : cur ? accent : "#2B261D")),
            borderRadius: shop ? "50%" : "0",
            transform: boss ? "rotate(45deg)" : "none",
            boxShadow: cur ? "0 0 9px rgba(227,179,65,.65)" : "none"
          }
        };
      }),
      modeLabel: (this.G && this.G.mode === "versus") ? "R" + s.floor : s.isDaily ? "DAILY · #" + (s.seed % 100000) : "PRACTICE",
      hpPips: Array.from({ length: 12 }, (_, i) => ({ style: pip(i < filled, "#C4593C") })),
      hpLabel: Math.max(0, s.php) + " / " + s.pmax + " HP", atk: s.atk, gold: s.gold, tray, offers,
      luckV: this.G ? heroStatOf(this.hudCtx(s), "lck") : 10,
      armor: this.G ? heroStatOf(this.hudCtx(s), "def") : 0, spd: this.G ? heroStatOf(this.hudCtx(s), "spd") : 25,
      heroAtkBar: (s.stage === "combat" && !s.intro && s.hBarDur) ? { animationPlayState: (s.modal || s.paused) ? "paused" : "running", position: "absolute", inset: 0, transformOrigin: "0 50%", background: "#7C9A6A", animation: "atkdrain" + (s.hBarK % 2 ? "A" : "B") + " " + s.hBarDur + "ms linear 1 both" } : { position: "absolute", inset: 0, transformOrigin: "0 50%", background: "#7C9A6A", transform: "scaleX(0)" },
      enemyAtkBar: (s.stage === "combat" && !s.intro && s.eBarDur && !(s.enemy && s.enemy.merchant)) ? { animationPlayState: (s.modal || s.paused) ? "paused" : "running", position: "absolute", inset: 0, transformOrigin: "0 50%", background: "#C4593C", animation: "atkdrain" + (s.eBarK % 2 ? "A" : "B") + " " + s.eBarDur + "ms linear 1 both" } : { position: "absolute", inset: 0, transformOrigin: "0 50%", background: "#C4593C", transform: "scaleX(0)" },
      takeRelic: (this.G && this.G.mode === "versus" && s.floor === 1) ? (this.G.vsPicks > 1 ? "Draft 3 relics — " + this.G.vsPicks + " picks left:" : "Draft 3 relics — last pick:") : this.t("takeRelic"),
      vsStandingsOn: !!(this.G && this.G.mode === "versus" && s.stage === "decide"),
      vsStandings: (this.G && this.G.mode === "versus") ? [{ name: "You", lives: this.G.vsLives, me: true }].concat(this.G.vsRoster).sort((a, b2) => (b2.lives > 0 ? b2.lives : -1) - (a.lives > 0 ? a.lives : -1)).map((b, i) => ({
        key: "vs" + i, name: b.name + (b.lvl ? " · Lv " + b.lvl : ""),
        motto: b.me ? "That's you" : (b.profile ? b.profile.motto : ""),
        av: (() => {
          try {
            const px = 44 / PACK.SIZE;
            if (b.me) return heroSprite(heroBits, heroPal, px);
            return heroSprite(composeHero32(b.profile.combo), paletteFor(b.profile.famHex || { R: b.profile.accent }), px);
          } catch (e) { return {}; }
        })(),
        hearts: b.lives > 0 ? "\u2665".repeat(b.lives) : "OUT",
        rowStyle: { display: "flex", justifyContent: "space-between", alignItems: "center", padding: "7px 12px", border: "1px solid " + (b.me ? "#E3B341" : "#3A3328"), opacity: b.lives > 0 ? 1 : .38, background: b.me ? "rgba(227,179,65,.08)" : "rgba(20,18,15,.6)", flex: "0 0 auto" },
        nameStyle: { fontSize: "13px", color: b.me ? "#E3B341" : "#E7E0D2", fontWeight: 700, display: "block" },
        heartStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "11px", color: b.lives > 0 ? "#C4593C" : "#655C4E", letterSpacing: "2px", flex: "0 0 auto", marginLeft: "10px" }
      })) : [],
      draftFloorLine: (this.G && this.G.mode === "versus") ? (s.floor === 1 ? "Versus · 8 delvers · last one standing" : "Round " + s.floor + " · " + (this.G.vsRoster.filter(b => b.lives > 0).length + 1) + " delvers left") : this.t("floorOf", { n: s.floor, m: MAX_FLOOR }),

      floor: s.floor,
      dangerVigStyle: (() => {
        const inRun = s.screen === "run" && (s.stage === "combat" || s.stage === "decide" || s.stage === "revive");
        const ratio = s.pmax ? Math.max(0, s.php) / s.pmax : 1;
        if (!inRun || ratio > 0.35 || ratio <= 0) return { display: "none" };
        const crit = ratio <= 0.15;
        return {
          position: "absolute", inset: 0, zIndex: 9, pointerEvents: "none",
          boxShadow: crit ? "inset 0 0 90px 24px rgba(160,30,10,.55)" : "inset 0 0 70px 12px rgba(160,30,10,.32)",
          animation: "dangerVig " + (crit ? "0.9s" : "2s") + " ease-in-out infinite"
        };
      })(),
      queueOn: s.pack.length > 1,
      queue: s.pack.map((e, i) => {
        const done = i < s.packDone, cur = i === s.packDone;
        const boss = e.rank === "boss", king = e.rank === "king";
        return {
          key: "q" + i,
          tile: {
            width: "26px", height: "26px", position: "relative", flex: "0 0 auto", boxSizing: "border-box",
            background: "#191510",
            border: cur ? (king ? "2px solid " + accent : boss ? "2px solid #AEB6C0" : "2px solid " + accent) : (king ? "2px solid #7A5E1D" : boss ? "2px solid #565B63" : "1px solid #2B261D"),
            boxShadow: cur ? "0 0 8px rgba(227,179,65,.45)" : "none",
            opacity: done ? 0.85 : 1,
            filter: !done && !cur ? "brightness(.45)" : "none",
            transition: "opacity .3s, filter .3s, border-color .3s"
          },
          glyph: enemySprite(e.idx, e.variant, 24, done ? { opacity: 0.3, filter: "grayscale(1) brightness(.7)" } : null),
          strike: done ? { position: "absolute", left: "2px", right: "2px", top: "50%", height: "2px", background: "#C4593C", transform: "rotate(-38deg)", zIndex: 2 } : { display: "none" }
        };
      }),
      enemyRankLabel: s.enemy ? ((this.G && this.G.mode === "versus") ? "RIVAL · " + (this.G.vsFoeName || "DELVER").toUpperCase() + " · LV " + (this.G.vsFoeLvl || "?") : (s.enemy.rank === "king" ? "FINAL BOSS" : s.enemy.rank === "boss" ? "FLOOR BOSS" : s.enemy.rank === "elite" ? "HONOR GUARD" : "")) : "",
      enemyRankOn: !!(s.enemy && s.enemy.rank !== "guard"),
      hudNameOn: s.stage !== "combat",
      hudNameOff: s.stage === "combat",
      enemyKicker: (s.enemy && s.enemy.merchant) ? "NEUTRAL · MERCHANT" : s.enemy ? ((this.G && this.G.mode === "versus") ? "RIVAL · LV " + (this.G.vsFoeLvl || "?") : (s.enemy.rank === "king" ? "FINAL BOSS" : s.enemy.rank === "boss" ? "FLOOR BOSS" : s.enemy.rank === "elite" ? "HONOR GUARD" : "GUARD " + Math.min(s.packDone + 1, Math.max(1, s.packSize - 1)) + " / " + Math.max(1, s.packSize - 1))) : "",
      vsStyle: (s.enemy && s.enemy.merchant) ? { display: "none" } : { position: "absolute", left: s.vsLeft != null ? s.vsLeft + "px" : "50%", top: s.vsTop != null ? s.vsTop + "px" : "135px", transform: "translate(-50%,-50%)", zIndex: 2, fontFamily: "'Silkscreen',monospace", fontSize: "34px", fontWeight: 700, color: accent, textShadow: "3px 3px 0 #14120F, 0 0 22px rgba(227,179,65,.55)", letterSpacing: "-2px", pointerEvents: "none" },
      hpVal: Math.max(0, s.php) + "/" + s.pmax,
      eHpVal: (s.enemy && !s.enemy.merchant) ? Math.max(0, s.enemy.hp) + "/" + s.enemy.max : "",
      eAtkVal: (s.enemy && !s.enemy.merchant) ? s.enemy.atk : "",
      eDefVal: (s.enemy && !s.enemy.merchant) ? (s.enemy.armor || 0) : "",
      eSpdVal: (s.enemy && !s.enemy.merchant) ? (s.enemy.spd || 25) : "",
      eLckVal: (s.enemy && !s.enemy.merchant) ? (s.enemy.lck || 10) : "",
      foeRelicsOn: !!(this.G && this.G.mode === "versus" && s.enemy && s.stage === "combat" && this.G.vsFoeRelics),
      foeRelics: (this.G && this.G.mode === "versus" && this.G.vsFoeRelics ? this.G.vsFoeRelics : []).map((id, i) => ({
        key: id + i, glyph: ricon(id, 22),
        slotStyle: { all: "unset", boxSizing: "border-box", width: "24px", height: "24px", position: "relative", border: "2px solid " + (s.foePulse === id ? "#C4593C" : kindColor(ITEMS[id].kind)), background: "linear-gradient(180deg,#0B0906 0%,#12100B 45%," + kindColor(ITEMS[id].kind) + "33 100%)", boxShadow: "inset 0 1px 0 rgba(255,255,255,.12)", cursor: "pointer", display: "block" },
        info: () => this.setState({ modal: "relic", relicId: id, relicIdx: null })
      })),
      enemyRankStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "8px", letterSpacing: "1.5px", color: s.enemy && s.enemy.rank === "king" ? accent : s.enemy && s.enemy.rank === "boss" ? "#AEB6C0" : s.enemy && s.enemy.rank === "elite" ? "#9B8BD0" : "#655C4E", marginBottom: "3px" },
      crownOn: false,
      crownStyle: { position: "absolute", left: "50%", top: "-16px", width: "1px", height: "1px", transform: "translateX(-9px)" },
      crownGlyph: glyph("crown", 1.5),
      enemyName: (s.enemy && s.enemy.merchant) ? "The Merchant" : s.enemy ? ((this.G && this.G.mode === "versus" && this.G.vsFoeName) ? this.G.vsFoeName : this.t("en" + s.enemy.idx)) : "",
      enemyNameStyle: { fontSize: "15px", fontWeight: 700, letterSpacing: "-.3px", textAlign: "right", marginTop: "7px", maxWidth: "132px", lineHeight: 1.2, color: (s.enemy && s.enemy.merchant) ? "#E3B341" : "#C4593C" },
      foeBgGlyph: (s.enemy && !s.enemy.merchant) ? (vsFoeLayer(104 / PACK.SIZE, "bg", { zIndex: 0 }) || {}) : {},
      foeFrameGlyph: (s.enemy && !s.enemy.merchant) ? (vsFoeLayer(104 / PACK.SIZE, "frame", { zIndex: 2, pointerEvents: "none" }) || {}) : {},
      enemyGlyph: (s.enemy && s.enemy.merchant) ? {} : s.enemy ? (vsFoeAv(104 / PACK.SIZE, Object.assign({ zIndex: 1 }, s.dyingE ? { animation: "dieFlash " + (s.dyingBoss ? ".34s" : ".22s") + " ease forwards" } : null)) || enemySprite(s.enemy.idx, s.enemy.variant, 104, s.dyingE ? { animation: "dieFlash " + (s.dyingBoss ? ".34s" : ".22s") + " ease forwards" } : null)) : {},
      enemyGlyphWrap: { position: "absolute", inset: 0, opacity: (s.fly || s.intro) ? 0 : 1 },
      dungeonWrap: this.props.dungeonBg === false ? { display: "none" } : { position: "absolute", top: document.documentElement.getAttribute("data-dd-view") === "desktop" ? "-64px" : "-14px", left: "-18px", right: "-18px", bottom: "-14px", zIndex: -1, overflow: "hidden", pointerEvents: "none" },
      dungeonSlider: { position: "absolute", inset: 0, animation: s.stage === "travel" ? "bgDescend 2.4s ease-in-out both" : "none" },
      dungeonStyle: (() => {
        const B = this._bandNow();
        const st = B.plate;
        st.position = "absolute"; st.top = "0"; st.bottom = "0"; st.left = "0"; st.right = "0";
        const ratio = s.pmax ? Math.max(0, s.php) / s.pmax : 1;
        st.opacity = ratio <= 0.15 ? 0.4 : 1; st.transition = "opacity .4s ease";
        return st;
      })(),
      dungeonArt: (() => { const a = this._bandNow().art;
        if (s.hallSnap) a.transition = "none";   /* arriving on a new floor: place us at the left door, don't pan there */
        return a; })(),
      dungeonWash: this._bandNow().wash,
      dungeonNext: (() => {
        if (s.stage !== "travel") return { display: "none" };
        const st = this._bandNext().plate;
        st.position = "absolute"; st.left = "0"; st.right = "0"; st.top = "100%"; st.height = "100%";
        return st;
      })(),
      dungeonNextArt: s.stage === "travel" ? (() => { const a = this._bandNext().art; a.transition = "none"; return a; })() : { display: "none" },
      dungeonNextWash: s.stage === "travel" ? this._bandNext().wash : { display: "none" },
      flyOn: !!s.fly,
      flyKey: "fly" + (s.fly ? (s.fly.k || 0) : 0),
      flyWrapStyle: s.fly ? { position: "fixed", left: s.fly.left + "px", top: s.fly.top + "px", width: "128px", height: "128px", zIndex: 300, pointerEvents: "none", transformOrigin: "top left", "--fe": (s.fly.endSc || 1), "--fdx": s.fly.dx.toFixed(1) + "px", "--fdy": s.fly.dy.toFixed(1) + "px", "--fs": s.fly.sc, animation: "flyIn .52s cubic-bezier(.45,0,.25,1) both" } : {},
      flyGlyph: s.fly ? (vsFoeAv(104 / PACK.SIZE) || enemySprite(s.fly.idx, s.fly.variant, 104)) : {},
      enemyFrameStyle: { width: "128px", height: "128px", position: "relative", flex: "0 0 auto", border: s.enemy && s.enemy.rank === "king" ? "3px solid " + accent : s.enemy && s.enemy.rank === "boss" ? "3px solid #AEB6C0" : s.enemy && s.enemy.rank === "elite" ? "2px solid #9B8BD0" : "2px solid #2B261D", boxShadow: s.enemy && s.enemy.rank === "king" ? "0 0 16px rgba(227,179,65,.5)" : s.enemy && s.enemy.rank === "boss" ? "4px 4px 0 rgba(110,117,127,.45)" : "none", background: "#191510", boxSizing: "border-box", animation: s.dyingE ? ("dieFrame" + (s.dyingE % 2 ? "A" : "B") + " " + (s.dyingBoss ? "1.4s" : ".7s") + " ease forwards") : (s.hitE || s.hitE2) ? (s.enemyFxKind === "atk" ? ("lungeE" + ((s.hitE2 || 0) % 2 ? "A" : "B") + " .32s ease") : ("hitfx" + (s.hitE % 2 ? "A" : "B") + " .42s ease")) : "none" },
      partsE: (s.bloodE || []).map(p => ({ id: p.id, style: part(p, "#8E1F14", "rgba(142,31,20,.7)", .8) })).concat((s.dustE || []).map(p => ({ id: p.id, style: part(p, "#8B8172", "rgba(139,129,114,.5)", .5) }))),
      partsH: (s.bloodH || []).map(p => ({ id: p.id, style: part(p, "#8E1F14", "rgba(142,31,20,.7)", .8) })).concat((s.dustH || []).map(p => ({ id: p.id, style: part(p, "#8B8172", "rgba(139,129,114,.5)", .5) }))),
      ePips: (s.enemy && s.enemy.merchant) ? [] : Array.from({ length: 14 }, (_, i) => ({ style: pip(i < eFilled, "#C4593C") })),
      enemyStats: (s.enemy && s.enemy.merchant) ? "Sells curios. Does not fight." : s.enemy ? ((s.enemy.rank === "guard" || s.enemy.rank === "elite" ? (s.enemy.rank === "elite" ? "GUARD " : "GUARD ") + Math.min(s.packDone + 1, Math.max(1, s.packSize - 1)) + "/" + Math.max(1, s.packSize - 1) + " · " : "") + (this.t("hitsFor", { h: Math.max(0, s.enemy.hp), m: s.enemy.max, a: s.enemy.atk }) + " · DEF " + (s.enemy.armor || 0) + " · SPD " + (s.enemy.spd || 25))) : "",
      slashesE: s.popsE.filter(p => p.v.charAt(0) === "-" && !p.chain).map(p => ({
        id: "s" + p.id,
        gashWrap: { position: "absolute", left: "50%", top: "50%", width: "1px", height: "1px", animation: "gashfx .4s ease-out forwards", pointerEvents: "none", zIndex: 6 },
        gashGlyph: glyphBits(["R........", "RR.......", ".RRr.....", "..RRr....", "...RRr...", "....RRr..", ".....RRr.", "......RR.", "........R"], 3, { R: "#D9482B", r: "#8A2A16" }),
        wrap: { position: "absolute", left: "50%", top: "48%", width: "1px", height: "1px", transform: "translate(-50%,-50%)", animation: "slashfx .34s ease-out forwards", pointerEvents: "none", zIndex: 7 },
        glyph: glyphBits(["......W", ".....WW", "....WW.", "...WW..", "..WW...", ".WW....", "WW....."], 2.5, { W: p.chain ? "#B7A5F0" : "#F2EDE1" })
      })),
      slashesH: s.popsH.filter(p => p.kind === "hurt").map(p => ({
        id: "s" + p.id,
        gashWrap: { position: "absolute", left: "50%", top: "50%", width: "1px", height: "1px", animation: "gashfx .4s ease-out forwards", pointerEvents: "none", zIndex: 6 },
        gashGlyph: glyphBits(["R........", "RR.......", ".RRr.....", "..RRr....", "...RRr...", "....RRr..", ".....RRr.", "......RR.", "........R"], 3, { R: "#D9482B", r: "#8A2A16" }),
        wrap: { position: "absolute", left: "50%", top: "48%", width: "1px", height: "1px", transform: "translate(-50%,-50%)", animation: "slashfx .34s ease-out forwards", pointerEvents: "none", zIndex: 7 },
        glyph: glyphBits(["......W", ".....WW", "....WW.", "...WW..", "..WW...", ".WW....", "WW....."], 2.5, { W: "#FF6B47" })
      })),
      popsE: s.popsE.map(p => ({
        id: p.id, text: p.v,
        /* same layout/motion as the player fliers (dpoph rises within the frame box) */
        style: { position: "absolute", left: (p.x || 50) + "%", top: (p.y || 10) + "px", fontFamily: "'Silkscreen',monospace", fontSize: p.crit ? "22px" : "17px", fontWeight: 700, color: p.crit ? "#E3B341" : p.heal ? "#7C9A6A" : (p.chain ? "#B7A5F0" : "#FFB03A"), textShadow: p.crit ? "-2px 0 0 #14120F, 2px 0 0 #14120F, 0 -2px 0 #14120F, 0 2px 0 #14120F, 2px 2px 0 #14120F, 0 0 16px rgba(227,179,65,.8)" : "-2px 0 0 #14120F, 2px 0 0 #14120F, 0 -2px 0 #14120F, 0 2px 0 #14120F, 2px 2px 0 #14120F", animation: "dpoph 1.2s cubic-bezier(.2,.7,.4,1) forwards", pointerEvents: "none", zIndex: p.crit ? 9 : 8, whiteSpace: "nowrap" }
      })),
      popsH: s.popsH.map(p => ({
        id: p.id, text: p.v,
        style: { position: "absolute", left: (p.x || 50) + "%", top: (p.y || 10) + "px", fontFamily: "'Silkscreen',monospace", fontSize: p.kind === "miss" ? "14px" : "17px", fontWeight: 700, letterSpacing: p.kind === "miss" ? "1px" : "0", color: p.kind === "hurt" ? "#FF6B47" : p.kind === "buff" ? "#B7A5F0" : p.kind === "miss" ? "#D8D2C4" : "#9FD07F", textShadow: "-2px 0 0 #14120F, 2px 0 0 #14120F, 0 -2px 0 #14120F, 0 2px 0 #14120F, 2px 2px 0 #14120F", animation: "dpoph 1.2s cubic-bezier(.2,.7,.4,1) forwards", pointerEvents: "none", zIndex: 8, whiteSpace: "nowrap" }
      })),
      goldValStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "20px", lineHeight: "1", color: accent, marginTop: "3px", textShadow: "0 2px 3px rgba(0,0,0,.7), 0 0 14px rgba(227,179,65,.45)", animation: s.hitG ? ("hitfx" + (s.hitG % 2 ? "A" : "B") + " .42s ease") : "none" },
      popsG: s.popsG.map(p => ({
        id: p.id, text: p.v,
        style: { position: "absolute", left: (p.x || 50) + "%", top: (p.y || -16) + "px", fontFamily: "'Silkscreen',monospace", fontSize: "17px", fontWeight: 700, color: accent, textShadow: "-2px 0 0 #14120F, 2px 0 0 #14120F, 0 -2px 0 #14120F, 0 2px 0 #14120F, 2px 2px 0 #14120F", animation: "dpoph 1.2s cubic-bezier(.2,.7,.4,1) forwards", pointerEvents: "none", zIndex: 8, whiteSpace: "nowrap" }
      })),
      heroDieAnim: Object.assign({ position: "absolute", inset: 0, zIndex: 1 }, s.dyingH ? { animation: "dieFlash .28s ease forwards" } : {}),
      /* bg plate and frame render as their own layers: the death flash/fade never touches them */
      heroBgGlyph: Object.assign({}, heroSprite(composeBg32(wardrobeCombo()), heroPal, 120 / PACK.SIZE), { zIndex: 0 }),
      heroFrameGlyph: Object.assign({}, heroSprite(composeFrame32(wardrobeCombo()), heroPal, 120 / PACK.SIZE), { zIndex: 2, pointerEvents: "none" }),
      heroGlyph: heroSprite(heroOnlyAnimBits, heroPal, 120 / PACK.SIZE),
      heroFrameStyle: (() => {
        const low = s.pmax && s.php > 0 && s.php / s.pmax <= 0.15, mid = s.pmax && s.php > 0 && s.php / s.pmax <= 0.35;
        const fc = "#2B261D";
        return { width: "128px", height: "128px", position: "relative", flex: "0 0 auto",
          border: "2px solid " + (s.dyingH ? "#5A1F12" : low ? "#C4593C" : mid ? "#7A2E18" : fc),
          boxShadow: (low ? "0 0 12px rgba(196,89,60,.55), " : "") + "4px 4px 0 rgba(0,0,0,.45)",
          background: "#191510", boxSizing: "border-box",
          animation: s.dyingH ? "dieFrameA 1s ease forwards" : s.hitH ? (s.heroFxKind === "atk" ? ("lungeH" + (s.hitH % 2 ? "A" : "B") + " .32s ease") : ("hitfx" + (s.hitH % 2 ? "A" : "B") + " .42s ease")) : (low ? "heartbeat 0.9s ease-in-out infinite" : "none") };
      })(),
      trayOn: s.items.length > 0,
      deskRun: s.screen === "run" && !!this.G,
      deskShellCls: (s.screen === "run" && this.G && (s.stage === "combat" || s.stage === "decide" || s.stage === "travel" || s.stage === "draft" || s.stage === "event" || s.stage === "merchant" || s.stage === "shop" || s.stage === "revive")) ? ("dd-run" + (s.stage === "draft" ? " dd-drafting" : "")) : "",
      deskLogRef: el => { this._logD = el; },
      deskHpTxt: Math.max(0, s.php) + " / " + s.pmax,
      deskHpBar: { height: "100%", width: Math.max(0, Math.min(100, Math.round(100 * s.php / Math.max(1, s.pmax)))) + "%", background: s.php / Math.max(1, s.pmax) <= 0.3 ? "#C4593C" : "#7C9A6A", transition: "width .3s ease" },
      deskStatRows: (() => {
        if (s.screen !== "run" || !this.G) return [];
        const HC = this.hudCtx(s), open = { atk: "openStatAtk", def: "openStatDef", spd: "openStatSpd", lck: "openStatLck" };
        const col = { atk: "#E7E0D2", def: "#8FA8C4", spd: "#7C9A6A", lck: "#E3B341" };
        return ["atk", "def", "spd", "lck"].map(k => ({ key: "dsk" + k, k: k.toUpperCase(), v: heroStatOf(HC, k),
          vStyle: { color: col[k] }, open: () => { SFX.tap(); this.setState({ modal: "statcard", statKey: k }); } }));
      })(),
      deskFoeName: s.enemy ? (s.enemy.merchant ? "The Merchant" : ((this.G && this.G.mode === "versus" && this.G.vsFoeName) ? this.G.vsFoeName : this.t("en" + s.enemy.idx))) : "\u2014",
      deskFoeHpTxt: s.enemy && !s.enemy.merchant ? Math.max(0, s.enemy.hp) + " / " + (s.enemy.max || s.enemy.hp) : "\u2014",
      deskFoeHpBar: s.enemy && !s.enemy.merchant ? { height: "100%", width: Math.max(0, Math.min(100, Math.round(100 * s.enemy.hp / Math.max(1, s.enemy.max || s.enemy.hp)))) + "%", background: "#C4593C", transition: "width .3s ease" } : { width: "0%" },
      deskFoeStatRows: s.enemy && !s.enemy.merchant ? [["ATK", s.enemy.atk, "#E7E0D2"], ["DEF", s.enemy.armor || 0, "#8FA8C4"], ["SPD", s.enemy.spd || 25, "#7C9A6A"], ["LCK", s.enemy.lck || 10, "#E3B341"]].map(r => ({ key: "dfk" + r[0], k: r[0], v: r[1], vStyle: { color: r[2] } })) : [],
      deskFoeRelics: (() => {
        if (!s.enemy || s.enemy.merchant) return [];
        const ids = (this.G && this.G.mode === "versus" && this.G.vsFoeRelics) ? this.G.vsFoeRelics : (s.enemy.relics || []);
        return ids.map((id, i) => ({ key: "dfr" + i + id, glyph: ricon(id, 54), slot: rslot(id, { width: "62px", height: "62px", flex: "0 0 auto", position: "relative" }) }));
      })(),
      deskFoeNoRelics: !!(s.enemy && !s.enemy.merchant && !((this.G && this.G.mode === "versus" && this.G.vsFoeRelics && this.G.vsFoeRelics.length) || (s.enemy.relics || []).length)),
      deskBgArt: (() => {   /* inner pan layer: art sized to its true aspect at viewport height, so left/right edges land on the doors */
        const segs = Math.max(1, (s.packSize || 1) + 1);
        const pan = s.stage === "travel" ? 1 : Math.min(1, (s.hallStep || 0) / segs);
        const aw = "calc(100vh * " + HALL_AR.toFixed(4) + ")";
        return { position: "absolute", top: 0, left: 0, height: "100vh", width: aw,
          backgroundImage: "url('" + this.hallArtFor((s.floor || 1)) + "')", backgroundSize: "100% 100%", backgroundRepeat: "no-repeat",
          imageRendering: "pixelated", willChange: "transform",
          transform: "translate3d(calc((100vw - " + aw + ") * " + pan.toFixed(4) + "), 0, 0)",
          transition: s.hallSnap ? "none" : "transform 2.6s ease-in-out" };
      })(),
      deskStats: (() => {
        if (s.screen !== "run" || !this.G) return "";
        const HC = this.hudCtx(s);
        return "ATK " + heroStatOf(HC, "atk") + " \u00b7 DEF " + heroStatOf(HC, "def") + "\nSPD " + heroStatOf(HC, "spd") + " \u00b7 LCK " + heroStatOf(HC, "lck") + "\nHP " + (s.php != null ? s.php : this.G.php) + " / " + (s.pmax != null ? s.pmax : this.G.pmax) + "\nGOLD " + (s.gold != null ? s.gold : this.G.gold);
      })(),
      logLines: s.log.map(l => {
        const enemySide = l.k === "hurt" || l.k === "enter" || l.k === "echain";
        const center = l.k === "death";
        return {
          text: l.text,
          style: {
            fontSize: l.depth > 0 ? "12px" : "13.5px", lineHeight: 1.35, color: logColor[l.k] || "#B9B0A0",
            paddingLeft: enemySide || center ? "0" : (l.depth || 0) * 14 + "px",
            fontWeight: (l.k === "kill" || l.k === "enter" || l.k === "death") ? 700 : 400,
            alignSelf: center ? "center" : enemySide ? "flex-end" : "flex-start",
            textAlign: center ? "center" : enemySide ? "right" : "left",
            maxWidth: center ? "100%" : "82%",
            animation: "lin .2s ease both"
          }
        };
      }),
      stagePausedAttr: s.paused ? "true" : "false",
      logRef: el => {
        if (el && el !== this._log) {
          el.addEventListener("scroll", () => {   /* user scrolls up -> hold; back to bottom -> resume stick */
            this._logHeld = el.scrollHeight - el.scrollTop - el.clientHeight > 24;
          }, { passive: true });
        }
        this._log = el;
      },
      stageRef: el => { this._stage = el; },
      runRef: el => { this._runBox = el; },
      introBoxRef: el => { this._introBox = el; },
      heroBoxRef: el => { this._heroBox = el; },
      enemyBoxRef: el => { this._enemyBox = el; },

      introOn: !!s.intro,
      introIsMerchant: !!(s.intro && s.intro.merchant),
      introIsFoe: !!(s.intro && !s.intro.merchant),
      merchantLineFull: (s.intro && s.intro.line) || "",
      openBazaar: () => { SFX.tap(); this.setState({ intro: null, stage: "shop" }); },
      introName: s.intro ? ((this.G && this.G.mode === "versus" && this.G.vsFoeName) ? this.G.vsFoeName : this.t("en" + s.intro.idx)) : "",
      introGlyph: s.intro ? (vsFoeAv(232 / PACK.SIZE) || enemySprite(s.intro.idx, s.intro.variant, 232)) : {},
      introStats: s.intro ? s.intro.hp + " HP · ATK " + s.intro.atk + " · DEF " + (s.intro.armor || 0) + " · SPD " + (s.intro.spd || 25) + " · LCK " + (s.intro.lck || 10) : "",
      introRank: s.intro ? ((this.G && this.G.mode === "versus") ? "RIVAL · " + (this.G.vsFoeName || "DELVER").toUpperCase() + " · LV " + (this.G.vsFoeLvl || "?") : (s.intro.rank === "king" ? "FINAL BOSS" : s.intro.rank === "boss" ? "FLOOR BOSS" : s.intro.rank === "elite" ? "HONOR GUARD" : "")) : "",
      introRankOn: !!(s.intro && s.intro.rank !== "guard"),
      introRankColor: { fontFamily: "'Silkscreen',monospace", fontSize: "9px", letterSpacing: "2px", color: s.intro && s.intro.rank === "boss" ? "#AEB6C0" : s.intro && s.intro.rank === "king" ? accent : "#9B8BD0", marginBottom: "8px" },
      introFrameStyle: { width: "240px", height: "240px", position: "relative", margin: "0 auto", border: s.intro && s.intro.rank === "king" ? "3px solid " + accent : s.intro && s.intro.rank === "boss" ? "3px solid #AEB6C0" : "2px solid #3A3328", boxShadow: s.intro && s.intro.rank === "king" ? "0 0 24px rgba(227,179,65,.5)" : "none", background: "#191510", boxSizing: "border-box", animation: "rise .3s ease both" },
      introCrownOn: false,
      startFight: () => this.startFight(),
      isRevive: s.stage === "revive",
      pauseOn: !!s.paused,
      pauseMenuOn: !!s.paused && !!s.pauseMenu,
      openPauseMenu: () => { SFX.tap(); this.setState({ pauseMenu: true }); },
      pmResume: () => { SFX.tap(); this.resumeTimers(); this.setState({ paused: false, pauseMenu: false }); },
      pmExit: () => { SFX.tap(); this.clearTimers(); this.G = null; this.setState({ paused: false, pauseMenu: false, screen: "title", stage: "draft", modal: null, log: [], enemy: null }); },
      pmMenuBtnStyle: { all: "unset", boxSizing: "border-box", position: "absolute", right: "68px", bottom: "16px", zIndex: 9, height: "46px", padding: "0 16px", display: "flex", alignItems: "center", justifyContent: "center", border: "2px solid #E3B341", background: "#2A2314", color: "#E3B341", cursor: "pointer", fontFamily: "'Silkscreen',monospace", fontSize: "9px", letterSpacing: "1.5px" },
      pmResumeLabel: "RESUME", pmExitLabel: "EXIT GAME", pmTitle: "PAUSED",
      pmSoundLabel: this.t("soundLabel"), pmLangLabel: this.t("languageTitle"),
      pauseGlyph: s.paused ? "\u25b6" : "\u2590\u258c",
      pauseBtnStyle: { all: "unset", boxSizing: "border-box", position: "absolute", right: "14px", bottom: "16px", zIndex: 9, width: "46px", height: "46px", display: "flex", alignItems: "center", justifyContent: "center", border: "2px solid " + (s.paused ? "#E3B341" : "#3A3328"), background: s.paused ? "#2A2314" : "rgba(20,18,15,.85)", color: s.paused ? "#E3B341" : "#8B8172", cursor: "pointer", fontSize: "13px", letterSpacing: "-2px" },
      togglePause: () => { SFX.tap(); const p = !s.paused; if (p) this.pauseTimers(); else this.resumeTimers(); this.setState({ paused: p }); },
      reviveAdLabel: s.adBusy ? "…" : "WATCH AD",
      reviveCost: REVIVE_SPARKS,
      canAffordRevive: s.sparks >= REVIVE_SPARKS,
      sparkBalance: s.sparks,
      reviveAd: () => this.revive(true),
      reviveSparks: () => this.revive(false),
      acceptDeath: () => this.finishRun("died"),
      reviveSparkStyle: { all: "unset", boxSizing: "border-box", flex: 1, padding: "16px 12px", border: "2px solid " + (s.sparks >= REVIVE_SPARKS ? accent : "#3A3328"), color: s.sparks >= REVIVE_SPARKS ? accent : "#655C4E", cursor: s.sparks >= REVIVE_SPARKS ? "pointer" : "default", textAlign: "center", background: "rgba(20,18,15,.85)" },
      purseLine: this.t("purseLine", { g: s.gold }),
      nextName: lead ? this.t("en" + lead.idx) : "",
      nextPeekOn: !(this.G && this.G.mode === "versus"),
      nextGlyph: lead ? enemySprite(lead.idx, lead.variant, 30) : {},
      nextStats: np ? ((np.length > 1 ? "BEHIND " + (np.length - 1) + " GUARD" + (np.length > 2 ? "S" : "") + " · " : "") + lead.hp + " HP · ATK " + lead.atk + " · ~" + packGold(np) + " GOLD") : "",
      descendLabel: (this.G && this.G.mode === "versus") ? "NEXT ROUND →" : this.t("descendBtn", { n: s.floor + 1 }),
      decideKicker: (this.G && this.G.mode === "versus") ? "ROUND " + s.floor + (this.G.vsLastWon ? " WON" : " LOST · +" + (this.G.vsWinPay || 0) + "g · \u2665 " + this.G.vsLives + " LEFT") : "FLOOR " + s.floor + " CLEARED",
      decideWrapStyle: Object.assign({ position: "absolute", inset: 0, zIndex: 10, display: "flex", flexDirection: "column", alignItems: "stretch", padding: "26px 22px", background: "rgba(8,7,5,.88)", animation: "fade .3s ease both", overflowY: "auto" }, (this.G && this.G.mode === "versus") ? { justifyContent: "flex-start", paddingTop: "20px" } : { justifyContent: "safe center" }),
      decideTitle: (this.G && this.G.mode === "versus") ? "The lobby\nthins out." : "The stairs\ngo down.",
      cashLabel: this.t("cashOut"),
      cashKeep: s.gold,
      dieWarn: "Die below and the whole purse stays in the dark. Walk away now and carry it all home.", atRisk: s.gold,
      descend: () => this.descend(), cashOut: () => this.cashOut(),

      overKicker: (this.G && this.G.mode === "versus") ? (s.how === "cleared" ? "CHAMPION \u2014 last delver standing" : s.how === "died" ? "ELIMINATED \u2014 round " + s.floor : this.t("resCashed", { n: s.floor })) : s.how === "died" ? this.t("resDied", { n: s.floor }) : s.how === "cleared" ? this.t("resCleared") : this.t("resCashed", { n: s.floor }),
      overBanner: s.how === "died" ? "DEFEAT" : s.how === "cleared" ? "VICTORY" : "EXTRACTED",
      overBannerStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "13px", letterSpacing: "4px", textAlign: "center", padding: "10px", marginTop: "10px",
        border: "2px solid " + (s.how === "died" ? "#C4593C" : s.how === "cleared" ? "#E3B341" : "#7C9A6A"),
        background: s.how === "died" ? "rgba(196,89,60,.12)" : s.how === "cleared" ? "rgba(227,179,65,.12)" : "rgba(124,154,106,.1)",
        color: s.how === "died" ? "#C4593C" : s.how === "cleared" ? "#E3B341" : "#7C9A6A" },
      overStars: [1, 2, 3].map(i => ({ key: "st" + i, ch: i <= ((this.G && this.G.stars) || 0) ? "\u2605" : "\u2606",
        style: { fontSize: "26px", lineHeight: 1, color: i <= ((this.G && this.G.stars) || 0) ? "#E3B341" : "#3A3328" } })),
      scoreBig: String((this.G && this.G.score) || 0),
      scoreBigStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "58px", lineHeight: .9, color: s.how === "died" ? "#8B8172" : "#E3B341", textAlign: "center" },
      relOn: !!(this.G && this.G.relevance && this.G.relevance < 0.995),
      relNote: (() => { const G = this.G; if (!G || G.relevance == null) return ""; return "XP ×" + G.relevance.toFixed(2) + " (" + (G.xpGain || 0) + ") — SHALLOW HALLS GIVE LESS"; })(),
      relNoteStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "7.5px", letterSpacing: "1px", textAlign: "center", marginTop: "5px", color: "#8B8172" },
      unlockOn: !!(this.G && this.G.unlocks && this.G.unlocks.length),
      unlockRows: ((this.G && this.G.unlocks) || []).map((u, i) => ({ key: "u" + i, kind: u.kind, name: u.name,
        art: { position: "absolute", inset: 0, backgroundImage: "url('" + u.art + "')", backgroundSize: "cover", backgroundPosition: "center", imageRendering: "pixelated" } })),
      overTitle: s.how === "died" ? "The dark\nkept you." : s.how === "cleared" ? "You cleared\nthe delve." : "You walked out\nwith the purse.",
      overTitleStyle: { fontSize: "31px", fontWeight: 700, letterSpacing: "-1.3px", marginTop: "12px", lineHeight: 1.04, whiteSpace: "pre-line", color: s.how === "died" ? "#C4593C" : "#E7E0D2" },
      shownGold: s.shownGold, goldBanked: this.t("goldBanked").toUpperCase(),
      finalGoldStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "52px", lineHeight: .9, color: s.depleting ? "#C4593C" : (s.how === "died" && s.shownGold === 0 ? "#655C4E" : accent), transition: "color .3s", animation: s.depleting ? "deplete .12s linear infinite" : "none" },
      bestNote: (s.isBest ? this.t("newBest") + " \u00b7 " : "") + (s.how === "died" ? "the purse stayed in the dark." : s.how === "cleared" ? "full purse carried home." : "purse carried home."),
      bestNoteStyle: { marginTop: "9px", fontSize: "12px", color: s.isBest ? accent : (s.how === "died" ? "#C4593C" : "#7C9A6A") },
      overStats: (() => { const rows = [
        { v: s.shownGold, k: "GOLD" }, { v: s.floor, k: this.t("floorLbl").toUpperCase() },
        { v: s.kills, k: this.t("killsLbl").toUpperCase() }, { v: s.items.length, k: this.t("relicsLbl").toUpperCase() }];
        return rows.map((x, i) => ({ v: x.v, k: x.k, style: { flex: 1, padding: "13px 6px", textAlign: "center", borderRight: i < rows.length - 1 ? "2px solid #2B261D" : "none" } })); })(),
      gained: s.gained, wallet: Store.get("dd.gold", 0) | 0,
      watchAdLabel: s.adBusy ? "…" : this.t("watchAd"),
      adBtnStyle: { all: "unset", boxSizing: "border-box", padding: "8px 10px", border: "1px solid #3A3328", cursor: "pointer", fontFamily: "'Silkscreen',monospace", fontSize: "8.5px", color: s.adBusy ? "#655C4E" : "#E7E0D2", whiteSpace: "nowrap" },
      watchAd: () => this.watchAd(),
      canRecover: s.canRecover, adRecoverLabel: this.t("adRecover"), adRecover: () => this.adRecover(),
      dailyNoteOn: !!s.dailyNote, dailyNote: s.dailyNote,
      dailyNoteStyle: { marginTop: "14px", padding: "9px 12px", border: "1px solid " + accent, color: accent, fontFamily: "'Silkscreen',monospace", fontSize: "9px", textAlign: "center" },
      toggleShare: () => { if (!s.shareOpen) tele("share_open", {}); this.setState({ shareOpen: !s.shareOpen }); },
      shareOpen: s.shareOpen,
      shareBtnLabel: "SHARE",
      shareBtnSub: s.how === "died" ? "TELL THEM HOW DEEP YOU GOT" : "SAME SEED · DARE A FRIEND TO BEAT IT",

      shareBtnStyle: { all: "unset", boxSizing: "border-box", display: "block", width: "100%", textAlign: "center", padding: "15px 16px", background: accent, color: "#14120F", border: "2px solid " + accent, boxShadow: "4px 4px 0 #7A5E1D", cursor: "pointer" },
      shareTargets: [["x", "X"], ["fb", "FB"], ["wa", "WA"], ["tg", "TG"], ["copy", "COPY"], ["sys", "···"]].map(t => ({
        label: t[1], go: () => this.shareTo(t[0]),
        style: { all: "unset", boxSizing: "border-box", padding: "12px 4px", border: "2px solid #3A3328", cursor: "pointer", fontFamily: "'Silkscreen',monospace", fontSize: "9px", color: "#E7E0D2", flex: "1 1 0", textAlign: "center" }
      })),

      modalOn: !!s.modal, isSettings: s.modal === "settings", isWelcome: s.modal === "welcome", isRelic: s.modal === "relic", isSupporter: s.modal === "supporter",
      isEnemyCard: s.modal === "enemy",
      setChips: (() => {
        if (s.screen !== "run" || !s.items.length) return [];
        const KC = {}; s.items.forEach(id => { const k = ITEMS[id] && ITEMS[id].kind; if (k) KC[k] = (KC[k] || 0) + 1; });
        const idol = count(s.items, "hollowidol");
        return Object.keys(KC).sort().map(k => {
          const m = KIND_META[k]; if (!m) return null;
          const n = KC[k] + idol, tiers = Object.keys(m.tiers).map(Number).sort((a, b) => a - b);
          const active = tiers.filter(t => n >= t).length > 0;
          return {
            key: k, label: k + " " + n,
            open: () => { SFX.tap(); this.setState({ modal: "setcard", setKind: k }); },
            chipStyle: { all: "unset", boxSizing: "border-box", display: "flex", alignItems: "center", gap: "4px", padding: "3px 6px", cursor: "pointer", border: "1px solid " + m.c + (active ? "" : "44"), background: active ? m.c + "1E" : "#14110C", fontFamily: "'Silkscreen',monospace", fontSize: "7px", letterSpacing: "1px", color: active ? m.c : "#655C4E" },
            ticks: tiers.map(t => ({ key: "tk" + t, style: { width: "5px", height: "5px", background: n >= t ? m.c : "#241F18", border: n >= t ? "none" : "1px solid #2B261D", boxSizing: "border-box" } }))
          };
        }).filter(Boolean);
      })(),
      openStatHp: () => { SFX.tap(); this.setState({ modal: "statcard", statKey: "hp" }); },
      openStatAtk: () => { SFX.tap(); this.setState({ modal: "statcard", statKey: "atk" }); },
      openStatDef: () => { SFX.tap(); this.setState({ modal: "statcard", statKey: "def" }); },
      openStatSpd: () => { SFX.tap(); this.setState({ modal: "statcard", statKey: "spd" }); },
      openStatLck: () => { SFX.tap(); this.setState({ modal: "statcard", statKey: "lck" }); },
      isStatCard: s.modal === "statcard",
      statCard: (() => {
        if (s.modal !== "statcard" || !this.G) return { title: "", total: "", color: "#E7E0D2", rows: [], note: "" };
        const G = this.G, items = G.items || [], nn = id => items.filter(x => x === id).length;
        const vs = G.mode === "versus";
        const meta = { hp: ["HP", "#C4593C"], atk: ["ATTACK", "#E7E0D2"], def: ["DEFENSE", "#AEB6C0"], spd: ["SPEED", "#7C9A6A"], lck: ["LUCK", "#B9A05C"] }[s.statKey];
        if (s.statKey === "hp") {
          const rows = [], add = (name, v, always) => { if (v || always) rows.push({ key: "r" + rows.length, name, amt: (v > 0 ? "+" : "") + v, color: v >= 0 ? "#7C9A6A" : "#C4593C" }); };
          const hpBase = vs ? Math.round((100 + META.bonuses(META.level()).hp) / 2) : 100;
          rows.push({ key: "base", name: "Base max HP", amt: String(hpBase), color: "#B9B0A0" });
          add("Ox Heart", 13 * nn("heart")); add("Bone Chalice (overheal)", G._chaliceG || 0); add("Hollow Idol", nn("hollowidol") ? -15 : 0);
          if (setOf(items, "FLESH") >= 3) add("Flesh set (3)", 3);
          add(vs ? "Round growth & events" : "Events & other", s.pmax - hpBase - 13 * nn("heart") - (G._chaliceG || 0) - (nn("hollowidol") ? -15 : 0) - (setOf(items, "FLESH") >= 3 ? 3 : 0));
          return { title: meta[0], color: meta[1], total: s.php + " / " + s.pmax, rows, note: vs ? "Max HP also grows 15% each round." : "" };
        }
        /* every other stat renders the ledger directly — the same rows the engines compute with */
        const HC = this.hudCtx(s);
        const lr = heroStatRows(HC, s.statKey);
        const rows = lr.map((r, i) => ({ key: "r" + i, name: r.src, amt: (r.tag === "base" ? "" : (r.amt > 0 ? "+" : "")) + r.amt, color: r.tag === "base" ? "#B9B0A0" : r.amt >= 0 ? "#7C9A6A" : "#C4593C" }));
        const note = { atk: "Sparring Bell's opener and EDGE-set bursts apply per strike, not to the stat.",
          def: "Rust Ward and Padded Hide apply per hit; Guard set (7) blocks the first blow.",
          spd: "",
          lck: "LCK drives dodges (with Clover), crits (with Dice), coin spills, and event odds." }[s.statKey] || "";
        return { title: meta[0], color: meta[1], total: String(heroStatOf(HC, s.statKey)), rows, note };
      })(),
      isSetCard: s.modal === "setcard",
      setCard: (() => {
        const k = s.setKind, m = k && KIND_META[k];
        if (s.modal !== "setcard" || !m) return { name: "", color: "#8B8172", countText: "", tiers: [], members: "" };
        const KC = s.items.filter(id => (ITEMS[id] && ITEMS[id].kind) === k).length;
        const idol = count(s.items, "hollowidol"), n = KC + idol;
        const tiers = Object.keys(m.tiers).map(Number).sort((a, b) => a - b);
        return {
          name: k + " SET", color: m.c,
          countText: n + " relic" + (n === 1 ? "" : "s") + (idol ? " (Hollow Idol +1)" : ""),
          tiers: tiers.map(t => ({
            key: "st" + t, on: n >= t,
            rowStyle: { display: "flex", gap: "10px", alignItems: "baseline", padding: "9px 11px", border: "1px solid " + (n >= t ? m.c : "#2B261D"), background: n >= t ? m.c + "14" : "#0F0D0A", opacity: n >= t ? 1 : .68 },
            tag: "(" + t + ")", tagStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "9px", color: n >= t ? m.c : "#655C4E", flex: "0 0 30px" },
            text: m.tiers[t], textStyle: { flex: 1, fontSize: "12px", lineHeight: 1.5, color: n >= t ? "#E7E0D2" : "#655C4E" }
          })),
          members: Array.from(new Set(s.items.filter(id => (ITEMS[id] && ITEMS[id].kind) === k))).map(id => this.itemName(id)).join(" \u00b7 ")
        };
      })(),
      isFloorCard: s.modal === "floornode",
      floorCard: (() => {
        if (s.modal !== "floornode") return { kicker: "", kickerColor: "#8B8172", title: "", sub: "", foes: [], log: [], logOn: false, detailOn: false, detail: { rows: [], name: "", lore: "", relics: "", abilities: "" } };
        const G = this.G || {}, f = s.nodeFloor || 1;
        const past = f <= s.floor, hist = (G.floorHist || {})[f], isShop = f === SHOP_FLOOR;
        const tagCol = r => r === "king" || r === "boss" ? "#E3B341" : r === "elite" ? "#B7A5F0" : "#8B8172";
        const mkFoe = (fo, i, unknown) => ({
          key: "ff" + i,
          click: unknown ? null : () => { SFX.tap(); this.setState({ nodeFoe: s.nodeFoe === i ? -1 : i }); },
          frame: { all: "unset", boxSizing: "border-box", cursor: unknown ? "default" : "pointer", padding: "8px 6px", width: "72px", textAlign: "center", border: "2px solid " + (!unknown && s.nodeFoe === i ? "#E3B341" : "#2B261D"), background: "#14120F", opacity: unknown ? .72 : 1 },
          art: unknown ? {} : enemySprite(fo.idx, fo.variant, 44, null),
          unknown: !!unknown,
          tag: unknown ? "UNKNOWN" : (fo.rank || "guard").toUpperCase(),
          tagColor: unknown ? "#655C4E" : tagCol(fo.rank)
        });
        let foes = [], sub = "", logOn = false, log = [];
        const np = (G.nextPack && f === s.floor + 1) ? G.nextPack : null;
        if (past && hist && hist.foes.length) {
          foes = hist.foes.map((fo, i) => mkFoe(fo, i, false));
          log = (hist.log || []).map((l, i) => ({ key: "fl" + i, text: l.text, style: { fontSize: "11px", lineHeight: "1.6", flex: "0 0 auto", paddingLeft: (l.depth || 0) * 9 + "px", color: l.k === "hurt" || l.k === "edmg" ? "#C4593C" : l.k === "heal" ? "#7C9A6A" : l.k === "gold" ? "#E3B341" : l.k === "luck" ? "#9B8BD0" : l.k === "kill" ? "#E7E0D2" : "#8B8172" } }));
          logOn = log.length > 0;
          sub = f === s.floor ? "In order of encounter \u2014 so far." : "Fought in order of encounter.";
        } else if (past) sub = isShop ? "The Hoard Bazaar \u2014 no foes, only prices." : "No record of this floor.";
        else if (isShop) sub = "The Hoard Bazaar waits below \u2014 no foes, only prices.";
        else if (np && G.scouted) { foes = np.map((fo, i) => mkFoe(fo, i, false)); sub = "Revealed \u2014 in order of encounter."; }
        else {
          const n = np ? np.length : (f >= MAX_FLOOR ? 4 : f <= 1 ? 1 : 3);
          foes = Array.from({ length: n }, (_, i) => mkFoe(null, i, true));
          sub = "Unknown foes wait below.";
        }
        const dl = (past && hist) ? hist.foes : (np && G.scouted ? np : []);
        const dsel = dl[s.nodeFoe];
        return {
          kicker: past ? (f === s.floor ? "CURRENT FLOOR" : "CLEARED FLOOR") : "NEXT FLOOR",
          kickerColor: past ? "#8B8172" : "#9B8BD0",
          title: "FLOOR " + f + (isShop ? " \u00b7 SHOP" : ""),
          sub, foes, log, logOn,
          detailOn: !!dsel,
          detail: dsel ? {
            name: this.t("en" + dsel.idx),
            lore: "\u201c" + (ENEMY_LORE[dsel.idx] || ENEMY_LORE[0]) + "\u201d",
            rows: [
              { key: "hp", label: "HP", v: String(dsel.hp) },
              { key: "atk", label: "ATK", v: String(dsel.atk) },
              { key: "def", label: "DEF", v: String(dsel.armor || 0) },
              { key: "spd", label: "SPD", v: String(dsel.spd || 25) },
              { key: "lck", label: "LCK", v: String(dsel.lck || 10) }
            ],
            relics: (dsel.relics && dsel.relics.length) ? dsel.relics.map(id => this.itemName(id)).join(" \u00b7 ") : "\u2014 none \u2014",
            abilities: "\u2014 none \u2014"
          } : { rows: [], name: "", lore: "", relics: "", abilities: "" }
        };
      })(),
      openEnemy: () => { if (s.enemy && !s.dyingE && s.stage === "combat") { SFX.tap(); this.setState({ modal: "enemy" }); } },
      enemyCard: (() => {
        if (s.modal === "enemy" && s.bestIdx != null) {   /* opened from the bestiary: the species dossier, no live fight stats */
          const i = s.bestIdx;
          return {
            name: this.t("en" + i),
            kicker: "BESTIARY \u00b7 ENCOUNTERED", kickerColor: "#8B8172",
            profOn: false, prof: {},
            art: enemySprite(i, 0, 120, null),
            lore: "\u201c" + (ENEMY_LORE[i] || ENEMY_LORE[0]) + "\u201d",
            abilities: "\u2014 varies by dungeon \u2014", relics: "\u2014 varies by dungeon \u2014",
            rows: [
              { key: "hp", label: "HP", v: "?" }, { key: "atk", label: "ATK", v: "?" },
              { key: "spd", label: "SPD", v: "?" }, { key: "def", label: "ARMOR", v: "?" }
            ]
          };
        }
        const e = s.enemy;
        if (!e) return { name: "", rows: [] };
        const rank = e.rank || "guard";
        const vs = this.G && this.G.mode === "versus", foe = vs ? this.G.vsRoster[this.G.vsFoe] : null;
        const prof = (() => {
          if (!foe || !foe.profile) return null;
          try {
            return { av: heroSprite(composeHero32(foe.profile.combo), paletteFor(foe.profile.famHex || { R: foe.profile.accent }), 44 / PACK.SIZE), name: foe.name + " · Lv " + foe.lvl, motto: foe.profile.motto, hearts: foe.lives > 0 ? "\u2665".repeat(foe.lives) : "OUT" };
          } catch (er) { return { av: {}, name: foe.name + " · Lv " + foe.lvl, motto: foe.profile.motto, hearts: "\u2665".repeat(foe.lives) }; }
        })();
        return {
          name: (vs && foe) ? foe.name : this.t("en" + e.idx),
          kicker: vs ? (rank === "king" ? "GRAND FINAL RIVAL" : "RIVAL DELVER") : rank === "king" ? "DUNGEON KING" : rank === "boss" ? "FLOOR BOSS" : rank === "elite" ? "ELITE GUARD" : "GUARD",
          kickerColor: rank === "king" || rank === "boss" ? "#E3B341" : rank === "elite" ? "#B7A5F0" : "#8B8172",
          profOn: !!prof, prof: prof || {},
          art: (vs && prof ? heroSprite(composeHero32(foe.profile.combo), paletteFor(foe.profile.famHex || { R: foe.profile.accent }), 120 / PACK.SIZE) : null) || enemySprite(e.idx, e.variant, 120, null),
          lore: (vs && foe && foe.profile) ? "\u201c" + foe.profile.motto + "\u201d" : "\u201c" + (ENEMY_LORE[e.idx] || ENEMY_LORE[0]) + "\u201d",
          abilities: (!vs && e.relics && e.relics.length) ? e.relics.map(id => this.itemName(id) + " (" + (ITEMS[id].pas ? ITEMS[id].pas || "" : "") + ")").filter(Boolean).join(" \u00b7 ") || "\u2014 none \u2014" : "\u2014 none \u2014",
          relics: (vs && this.G.vsFoeRelics && this.G.vsFoeRelics.length) ? this.G.vsFoeRelics.map(id => this.itemName(id)).join(" · ") : ((!vs && e.relics && e.relics.length) ? e.relics.map(id => this.itemName(id)).join(" \u00b7 ") : "\u2014 none \u2014"),
          rows: [
            { key: "hp", label: "HP", v: Math.max(0, e.hp) + " / " + (e.max || e.hp) },
            { key: "atk", label: "ATK", v: String(e.atk) },
            { key: "spd", label: "SPD", v: String(e.spd || 25) },
            { key: "def", label: "ARMOR", v: String(e.armor || 0) }
          ]
        };
      })(),
      closeModal: () => this.setState({ modal: null, bestIdx: null }),
      isDressing: s.modal === "dressing",
      isBalance: s.modal === "balance",
      openBalance: () => { SFX.tap(); this.setState({ modal: "balance", balRes: null }); },
      openSandbox: () => this.openSandbox(), sbOn: !!s.sbOn,
      balTitle: "BALANCE LAB", balSub: "Plays the real game on paper: a survival-greedy hero drafts, shops, sockets, and fights the live rules. Tune the knobs, run, read the wall.",
      balRunLabel: s.balBusy ? "SIMULATING\u2026" : "\u25b6 RUN " + (s.balN || 100) + " GAMES",
      balGroups: [
        { h: "SIMULATION", keys: [
          { key: "balN", label: "RUNS", val: s.balN || 100, min: 10, max: 1000, step: 10 }
        ]},
        { h: "HERO", keys: [
          { key: "balDlvl", label: "DUNGEON LEVEL", val: s.balDlvl || 1, min: 1, max: 10, step: 1 },
          { key: "balPlvl", label: "PLAYER LEVEL", val: s.balPlvl || 1, min: 1, max: 20, step: 1 },
          { key: "balHp", label: "BASE HP", val: s.balHp || 100, min: 20, max: 200, step: 5 },
          { key: "balAtk", label: "BASE ATK", val: s.balAtk == null ? 5 : s.balAtk, min: 1, max: 30, step: 1 },
          { key: "balDef", label: "BASE DEF", val: s.balDef || 0, min: 0, max: 30, step: 1 },
          { key: "balSpd", label: "BASE SPD", val: s.balSpd == null ? 25 : s.balSpd, min: 10, max: 80, step: 1 },
          { key: "balLck", label: "BASE LCK", val: s.balLck == null ? 10 : s.balLck, min: 0, max: 80, step: 1 },
          { key: "balGold", label: "BASE GOLD", val: s.balGold || 0, min: 0, max: 200, step: 5 },
          { key: "balBreath", label: "BREATH HEAL", val: s.balBreath == null ? 5 : s.balBreath, min: 0, max: 15, step: 1 }
        ]},
        { h: "ECONOMY", keys: [
          { key: "balDraft", label: "DRAFT CHOICES", val: s.balDraft || 3, min: 1, max: 5, step: 1 },
          { key: "balBuy", label: "SHOP RELIC g", val: s.balBuy || SHOP_BUY, min: 10, max: 200, step: 10 },
          { key: "balUp", label: "SHOP SOCKET g", val: s.balUp || SHOP_UP, min: 10, max: 200, step: 10 }
        ]}
      ].map(g => ({ h: g.h, keys: g.keys.map(k => Object.assign(k, {
        note: k.prev ? (k.val === 100 ? "flat: stays " + k.prev[0] : "F1 " + k.prev[0] + " \u2192 F7 " + Math.round(k.prev[0] * Math.pow(7, k.val / 100)) + " \u2192 F13 " + Math.round(k.prev[0] * Math.pow(13, k.val / 100)) + " " + k.prev[1]) : null,
        hasNote: !!k.prev,
        dec: () => { SFX.tap(); this.setState({ [k.key]: Math.max(k.min, k.val - k.step) }); },
        inc: () => { SFX.tap(); this.setState({ [k.key]: Math.min(k.max, k.val + k.step) }); },
        set: e => { const v = parseInt(e && e.target ? e.target.value : "", 10); if (!isNaN(v)) this.setState({ [k.key]: Math.max(k.min, Math.min(k.max, v)) }); }
      })) })),
      balFormulas: ENEMY_STAT_FORMULAS.concat(ENEMY_ROLE_FORMULAS).map(st => {
        const cur = (s.balForm && s.balForm[st.id]) != null ? s.balForm[st.id] : st.def;
        const fn0 = compileFormula(cur);
        const defFn0 = compileFormula(st.def);
        /* previews price against the guard of that floor (typed formulas where valid, defaults otherwise) */
        const gf = sid => { const d = ENEMY_STAT_FORMULAS.find(x => x.id === sid); const t = s.balForm && s.balForm[sid] != null ? compileFormula(s.balForm[sid]) : null; return t || compileFormula(d.def); };
        const dHp = gf("ehp"), dAtk = gf("eatk"), dDef = gf("edef"), dSpd = gf("espd"), dLck = gf("elck");
        const statsAt = f => { const hp = Math.round(dHp(f)); return { hp, maxhp: hp, atk: Math.round(dAtk(f)), def: Math.round(dDef(f)), spd: Math.round(dSpd(f)), lck: Math.round(dLck(f)) }; };
        const roleStat = st.id.match(/^(elite|boss|king)(hp|atk|def|spd|lck)$/);
        const gVal = roleStat ? { hp: dHp, atk: dAtk, def: dDef, spd: dSpd, lck: dLck }[roleStat[2]] : null;
        const fn = fn0 ? f => fn0(f, statsAt(f), gVal ? Math.round(gVal(f)) : 0) : null;
        const defFn = defFn0 ? f => defFn0(f, statsAt(f), gVal ? Math.round(gVal(f)) : 0) : null;
        const mkChart = (fnA, fnB) => {   /* fnA = current default, fnB = typed */
          if (!fnA) return null;
          const W = 96, H = 30, N = MAX_FLOOR;
          const va = [], vb = [];
          for (let f = 1; f <= N; f++) { va.push(fnA(f)); vb.push(fnB ? fnB(f) : fnA(f)); }
          const all = va.concat(vb), lo = Math.min(...all), hi = Math.max(...all), sp = hi - lo || 1;
          const pts = v => v.map((y, i) => (Math.round(i / (N - 1) * W * 10) / 10) + "," + (Math.round((H - 3 - (y - lo) / sp * (H - 6)) * 10) / 10)).join(" ");
          return React.createElement("svg", { width: W, height: H, viewBox: "0 0 " + W + " " + H, style: { display: "block", flex: "none" } },
            React.createElement("polyline", { points: pts(va), fill: "none", stroke: "#655C4E", strokeWidth: 1.5, strokeDasharray: "3 2" }),
            React.createElement("polyline", { points: pts(vb), fill: "none", stroke: fnB ? "#7C9A6A" : "transparent", strokeWidth: 1.5 }));
        };
        const isMult = false;
        const fmt = v => isMult ? "\u00d7" + (Math.round(v * 100) / 100) : Math.round(v);
        const note = !fn ? "\u26a0 invalid formula" :
          (fn(1) === fn(13) ? "flat " + fmt(fn(1)) : "F1 " + fmt(fn(1)) + " \u2192 F7 " + fmt(fn(7)) + " \u2192 F13 " + fmt(fn(13)));
        return { id: st.id, label: st.label, val: cur, bad: !fn,
          chart: mkChart(defFn, fn), hasChart: !!defFn,
          chartNote: fn && defFn && String(cur).replace(/\s/g, "") !== String(st.def).replace(/\s/g, "") ? "vs current (dashed)" : "current",
          noteStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "7px", letterSpacing: ".5px", color: fn ? "#7C9A6A" : "#C4593C", marginTop: "3px" },
          note,
          set: e => { const v = e && e.target ? e.target.value : ""; this.setState({ balForm: Object.assign({}, s.balForm, { [st.id]: v }) }); } };
      }),
      balTab: s.balTab || "enemy",
      balTabs: ["hero", "enemy"].map(t => ({ key: t, label: t === "hero" ? "HERO SCALING" : "ENEMY SCALING",
        pick: () => { SFX.tap(); this.setState({ balTab: t }); },
        style: { all: "unset", boxSizing: "border-box", flex: 1, textAlign: "center", padding: "8px 4px", cursor: "pointer",
          fontFamily: "'Silkscreen',monospace", fontSize: "8px", letterSpacing: "1.5px",
          border: "2px solid " + ((s.balTab || "enemy") === t ? "#7C9A6A" : "#2B261D"), color: (s.balTab || "enemy") === t ? "#7C9A6A" : "#655C4E" } })),
      balTabHero: (s.balTab || "enemy") === "hero",
      balTabEnemy: (s.balTab || "enemy") !== "hero",
      balHeroFormulas: HERO_STAT_FORMULAS.map(st => {
        const cur = (s.balHeroForm && s.balHeroForm[st.id]) != null ? s.balHeroForm[st.id] : st.def;
        const fn0 = compileFormula(cur), defFn0 = compileFormula(st.def);
        const fn = fn0 ? l => fn0(1, undefined, 0, l) : null;
        const defFn = defFn0 ? l => defFn0(1, undefined, 0, l) : null;
        const mkChart = (fnA, fnB) => {
          if (!fnA) return null;
          const W = 96, H = 30, N = 20, va = [], vb = [];
          for (let l = 1; l <= N; l++) { va.push(fnA(l)); vb.push(fnB ? fnB(l) : fnA(l)); }
          const all = va.concat(vb), lo = Math.min(...all), hi = Math.max(...all), sp = hi - lo || 1;
          const pts = v => v.map((y, i) => (Math.round(i / (N - 1) * W * 10) / 10) + "," + (Math.round((H - 3 - (y - lo) / sp * (H - 6)) * 10) / 10)).join(" ");
          return React.createElement("svg", { width: W, height: H, viewBox: "0 0 " + W + " " + H, style: { display: "block", flex: "none" } },
            React.createElement("polyline", { points: pts(va), fill: "none", stroke: "#655C4E", strokeWidth: 1.5, strokeDasharray: "3 2" }),
            React.createElement("polyline", { points: pts(vb), fill: "none", stroke: fnB ? "#8FA8C4" : "transparent", strokeWidth: 1.5 }));
        };
        const note = !fn ? "\u26a0 invalid formula" :
          (fn(1) === fn(20) ? "flat " + Math.round(fn(1)) : "L1 " + Math.round(fn(1)) + " \u2192 L10 " + Math.round(fn(10)) + " \u2192 L20 " + Math.round(fn(20)));
        return { id: st.id, label: st.label + " \u00b7 formula of l", val: cur, bad: !fn,
          chart: mkChart(defFn, fn), hasChart: !!defFn,
          chartNote: fn && defFn && String(cur).replace(/\s/g, "") !== String(st.def).replace(/\s/g, "") ? "vs current (dashed)" : "current",
          noteStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "7px", letterSpacing: ".5px", color: fn ? "#8FA8C4" : "#C4593C", marginTop: "3px" },
          note,
          set: e => { const v = e && e.target ? e.target.value : ""; this.setState({ balHeroForm: Object.assign({}, s.balHeroForm, { [st.id]: v }) }); } };
      }),
      heroScaleGrid: (() => {
        const hf = id => { const d = HERO_STAT_FORMULAS.find(x => x.id === id);
          const t = s.balHeroForm && s.balHeroForm[id] != null ? compileFormula(s.balHeroForm[id]) : null;
          const fn = t || compileFormula(d.def); return l => Math.round(fn(1, undefined, 0, l)); };
        const H = hf("hhp"), A = hf("hatk"), D = hf("hdef"), SP = hf("hspd"), LK = hf("hlck");
        const rows = [["LV", "HP", "ATK", "DEF", "SPD", "LCK", "+"]].concat(
          Array.from({ length: 20 }, (_, i) => { const l = i + 1;
            return [String(l), String(H(l)), String(A(l)), String(D(l)), String(SP(l)), String(LK(l)), META.STRUCT[l] ? "\u2726" : ""]; }));
        const sel = s.balPlvl || 1;
        return rows.flatMap((r, ri) => r.map((txt, ci) => ({ key: "hs" + ri + "_" + ci, txt,
          style: { padding: "3px 2px", textAlign: "center", background: ri === 0 ? "#191510" : (ri === sel ? "rgba(124,154,106,.18)" : "transparent"),
            color: ri === 0 ? "#655C4E" : (ri === sel ? "#7C9A6A" : "#8B8172"), border: ri === sel ? "1px solid #3E4A38" : "1px solid transparent" } })));
      })(),
      balFormTitle: "ENEMY SCALING \u00b7 formula of f (floor)",
      balFormHint: "Guard baseline as formulas of f, then full per-role rows (elite/boss/king). In role rows, guard = the guard baseline value of that stat at f \u2014 e.g. guard*1.2, guard+8. Also usable: hp maxhp atk def spd lck, round/floor/sqrt/min/max/pow",
      balRun: () => {
        if (s.balBusy) return;
        SFX.tap();
        this.setState({ balBusy: true, balRes: null });
        setTimeout(() => {
          const r = this.balanceRuns(s.balN || 100, {
            baseHp: this.heroF("hhp", s), baseAtk: this.heroF("hatk", s), baseDef: this.heroF("hdef", s), baseSpd: this.heroF("hspd", s), baseGold: (s.balGold || 0) + META.bonuses(s.balPlvl || 1).gold, baseLck: this.heroF("hlck", s), breath: this.heroF("hbreath", s),
            draftChoices: (s.balDraft || 3) + META.bonuses(s.balPlvl || 1).draft, shopBuy: s.balBuy || SHOP_BUY, shopUp: s.balUp || SHOP_UP,
            formulas: s.balForm || {}, benchAbilities: !!s.balBench, defModel: s.balDefFlat ? "flat" : "pct", dlvl: s.balDlvl || 1,
            seed: (Date.now() % 65536) });
          this.setState({ balBusy: false, balRes: r });
        }, 30);
      },
      balRes: (() => {
        const r = s.balRes;
        if (!r) return null;
        const avg = a => a.length ? Math.round(a.reduce((x, y) => x + y, 0) / a.length) : 0;
        return {
          winRate: Math.round(100 * r.wins / r.runs) + "%",
          winStyle: { fontSize: "34px", fontWeight: 700, letterSpacing: "-1px", color: r.wins / r.runs >= 0.55 ? "#7C9A6A" : r.wins / r.runs >= 0.25 ? "#E3B341" : "#C4593C" },
          winSub: r.wins + " OF " + r.runs + " RUNS SURVIVE THE HOARD-KING" + (s.balBench ? " \u00b7 \u2726 DODGE+CRIT" : "") + (s.balDefFlat ? " \u00b7 FLAT DEF" : ""),
          avgWin: (r.wins ? "avg win: " + avg(r.winHp) + " hp \u00b7 " + avg(r.winGold) + "g banked \u00b7 " : "no survivors \u00b7 ") + Math.round((r.strikes || 0) / r.runs) + " strikes/run \u00b7 " + Math.round((r.foes || 0) / r.runs) + " enemies/run",
          floors: Array.from({ length: MAX_FLOOR }, (_, i) => {
            const f = i + 1, deaths = r.deaths[f] || 0, pct = deaths / r.runs;
            const dmgMax = Math.max(1, ...Object.values(r.dmgTaken || {}));
            const dmg = (r.dmgTaken || {})[f];
            return { key: "f" + f, label: f === SHOP_FLOOR ? "\u25c9" : String(f), deaths,
              bar: { width: "100%", height: Math.max(2, Math.round(pct * 90)) + "px", background: pct > 0.25 ? "#C4593C" : pct > 0.1 ? "#E3B341" : "#5C5648", alignSelf: "flex-end" },
              hp: r.floorHp[f] != null ? Math.round(r.floorHp[f]) : "\u2014",
              dmg: dmg != null ? Math.round(dmg) : "\u2014",
              dmgBar: { width: "100%", height: (dmg != null ? Math.max(2, Math.round(dmg / dmgMax * 60)) : 2) + "px", background: "#A85C50", alignSelf: "flex-end" },
              str: r.floorStrikes && r.floorVisits && r.floorVisits[f] ? Math.round(r.floorStrikes[f] / r.floorVisits[f] * 10) / 10 : "\u2014" };
          }),
          dmgTotal: Math.round(Object.values(r.dmgTaken || {}).reduce((a, b) => a + b, 0)),
          picks: Object.entries(r.picks).sort((a, b) => b[1] - a[1]).slice(0, 6).map(e => ({ key: e[0], txt: this.itemName(e[0]) + " \u00d7" + e[1] })),
          endStats: r.endStats && r.endStats.n ? (() => { const e2 = r.endStats, d = e2.n; return [
            { key: "hp", label: "MAX HP", v: Math.round(e2.maxhp / d) }, { key: "atk", label: "ATK", v: Math.round(e2.atk / d) },
            { key: "def", label: "DEF", v: Math.round(e2.def / d) }, { key: "spd", label: "SPD", v: Math.round(e2.spd / d) },
            { key: "lck", label: "LCK", v: Math.round(e2.lck / d) }, { key: "rel", label: "RELICS", v: Math.round(e2.items / d) }
          ]; })() : []
        };
      })(),
      balHasRes: !!s.balRes,
      balBusy: !!s.balBusy,
      /* ==== benchmark: frozen config + seed set; every run reports \u0394 vs it ==== */
      saveBenchmark: () => {
        if (!s.balRes) return;
        SFX.tap();
        Store.set("dd.bench", { win: s.balRes.wins / s.balRes.runs, n: s.balRes.runs, avgDeath: s.balRes.deathFloors.length ? s.balRes.deathFloors.reduce((a, b) => a + b, 0) / s.balRes.deathFloors.length : MAX_FLOOR, ts: Date.now(),
          cfg: { hp: s.balHp || 50, breath: this.heroF("hbreath", s), draft: s.balDraft || 3, form: s.balForm || {} } });
        this.forceUpdate();
      },
      benchInfo: (() => { const b = Store.get("dd.bench", null); if (!b) return null;
        return { txt: "BENCH \u00b7 " + Math.round(b.win * 100) + "% WIN \u00b7 AVG DEATH F" + (Math.round(b.avgDeath * 10) / 10) + " \u00b7 " + b.n + " RUNS" }; })(),
      benchOn: !!Store.get("dd.bench", null),
      benchDelta: (() => { const b = Store.get("dd.bench", null); if (!b || !s.balRes) return null;
        const d = (s.balRes.wins / s.balRes.runs - b.win) * 100;
        return { txt: (d >= 0 ? "+" : "") + (Math.round(d * 10) / 10) + "pp VS BENCH", style: { fontFamily: "'Silkscreen',monospace", fontSize: "9px", letterSpacing: "1px", marginTop: "4px", color: d > 1 ? "#7C9A6A" : d < -1 ? "#C4593C" : "#8D8471" } }; })(),
      hasBenchDelta: !!(Store.get("dd.bench", null) && s.balRes),
      exportLab: () => {
        SFX.tap();
        const dump = { exported: new Date().toISOString(), kind: "dd-balance-lab",
          config: { runs: s.balN || 100, baseHp: this.heroF("hhp", s), baseAtk: this.heroF("hatk", s), baseDef: this.heroF("hdef", s), baseSpd: this.heroF("hspd", s), baseGold: (s.balGold || 0) + META.bonuses(s.balPlvl || 1).gold, baseLck: this.heroF("hlck", s), breath: this.heroF("hbreath", s), draftChoices: (s.balDraft || 3) + META.bonuses(s.balPlvl || 1).draft, shopBuy: s.balBuy || SHOP_BUY, shopUp: s.balUp || SHOP_UP, formulas: s.balForm || {}, dlvl: s.balDlvl || 1, benchAbilities: !!s.balBench, defModel: s.balDefFlat ? "flat" : "pct" },
          benchmark: Store.get("dd.bench", null),
          result: s.balRes ? { wins: s.balRes.wins, runs: s.balRes.runs, deaths: s.balRes.deaths, deathFloors: s.balRes.deathFloors, floorHp: s.balRes.floorHp, floorStrikes: s.balRes.floorStrikes, floorVisits: s.balRes.floorVisits, strikes: s.balRes.strikes, foes: s.balRes.foes, winHp: s.balRes.winHp, winGold: s.balRes.winGold, picks: s.balRes.picks, sockPicks: s.balRes.sockPicks, evPicks: s.balRes.evPicks, endStats: s.balRes.endStats } : null,
          sensitivity: s.sensRes || null,
          duel: s.duelRes || null };
        const blob = new Blob([JSON.stringify(dump, null, 1)], { type: "application/json" });
        const a = document.createElement("a");
        a.href = URL.createObjectURL(blob);
        a.download = "dd-balance-lab-" + Date.now() + ".json";
        document.body.appendChild(a); a.click(); a.remove();
        setTimeout(() => URL.revokeObjectURL(a.href), 5000);
      },
      /* ==== stat sensitivity sweep: what 1 point of each stat is worth, empirically ==== */
      runSensitivity: () => {
        if (s.sensBusy) return;
        SFX.tap();
        this.setState({ sensBusy: true, sensRes: null });
        const cfg = { baseHp: this.heroF("hhp", s), baseAtk: this.heroF("hatk", s), baseDef: this.heroF("hdef", s), baseSpd: this.heroF("hspd", s), baseGold: (s.balGold || 0) + META.bonuses(s.balPlvl || 1).gold, baseLck: this.heroF("hlck", s), breath: this.heroF("hbreath", s), draftChoices: (s.balDraft || 3) + META.bonuses(s.balPlvl || 1).draft,
          shopBuy: s.balBuy || SHOP_BUY, shopUp: s.balUp || SHOP_UP, formulas: s.balForm || {}, defModel: s.balDefFlat ? "flat" : "pct", dlvl: s.balDlvl || 1 };
        const N = Math.min(500, s.balN || 200);
        const BASES = { baseHp: cfg.baseHp || 100, baseAtk: cfg.baseAtk, baseDef: cfg.baseDef, baseSpd: cfg.baseSpd, baseLck: cfg.baseLck };
        const STATS = [["baseHp", "HP"], ["baseAtk", "ATK"], ["baseDef", "DEF"], ["baseSpd", "SPD"], ["baseLck", "LCK"]];
        /* ✦ jobs: dodge + crit enabled directly (harness-only flag) — no relics, no flat-stat pollution */
        const MAGS = [1, 5, 10, 50];
        const jobs = [{ stat: null, mag: 0 }, { stat: null, mag: 0, kit: true }];
        for (const [k] of STATS) for (const m of MAGS) jobs.push({ stat: k, mag: m });
        for (const [k] of STATS) for (const m of MAGS) jobs.push({ stat: k, mag: m, kit: true });
        const out = {};
        let ji = 0;
        const step = () => {
          const j = jobs[ji];
          const p = Object.assign({}, cfg, { seed: 0xBA1A });   /* same seeds for every job: paired comparison */
          if (j.kit) p.benchAbilities = true;
          if (j.stat) p[j.stat] = BASES[j.stat] + j.mag;
          const r = this.balanceRuns(N, p);
          out[(j.stat || "base") + "_" + j.mag + (j.kit ? "_k" : "")] = { win: r.wins / N, depth: r.deathFloors.length ? r.deathFloors.reduce((a, b) => a + b, 0) / r.deathFloors.length : MAX_FLOOR };
          ji++;
          if (ji < jobs.length) { this.setState({ sensProg: Math.round(100 * ji / jobs.length) }); setTimeout(step, 15); }
          else this.setState({ sensBusy: false, sensProg: 0, sensRes: { out, n: N, stats: STATS, mags: MAGS } });
        };
        setTimeout(step, 30);
      },
      sensBusy: !!s.sensBusy, sensProg: s.sensProg || 0, sensHasRes: !!s.sensRes,
      sensRows: (() => {
        const R = s.sensRes; if (!R) return [];
        const base = R.out.base_0, baseK = R.out.base_0_k;
        const pp = x => (x >= 0 ? "+" : "") + (Math.round(x * 1000) / 10);
        const mkRow = (k, label, kit) => {
          const b0 = kit ? baseK : base, sfx = kit ? "_k" : "";
          const cells = R.mags.map(m => { const c = R.out[k + "_" + m + sfx]; const d = c ? c.win - b0.win : 0;
            return { key: k + m + sfx, txt: c ? pp(d) : "\u2014", style: { flex: 1, textAlign: "center", fontFamily: "'Silkscreen',monospace", fontSize: "11px", color: d > 0.005 ? "#7C9A6A" : d < -0.005 ? "#C4593C" : "#8D8471" } }; });
          const c10 = R.out[k + "_10" + sfx], c5 = R.out[k + "_5" + sfx];
          const per = c10 ? (c10.win - b0.win) / 10 * 100 : (c5 ? (c5.win - b0.win) / 5 * 100 : 0);
          return { key: k + sfx, label, cells, perPoint: (Math.round(per * 100) / 100) + " pp/pt" };
        };
        const rows = R.stats.map(([k, label]) => mkRow(k, label, false));
        if (baseK) for (const [k, label] of R.stats) rows.push(mkRow(k, label + "\u2726", true));
        return rows;
      })(),
      sensBase: s.sensRes ? "baseline " + Math.round(s.sensRes.out.base_0.win * 100) + "% bare" + (s.sensRes.out.base_0_k ? " \u00b7 " + Math.round(s.sensRes.out.base_0_k.win * 100) + "% kitted" : "") + " \u00b7 " + s.sensRes.n + " paired runs \u00b7 \u0394 win (pp) at +1/+5/+10/+50 \u00b7 \u2726 = dodge + crit enabled" : "",
      sensBtnLabel: s.sensBusy ? "SWEEPING\u2026 " + (s.sensProg || 0) + "%" : "RUN STAT SWEEP",
      /* ==== DUEL LAB: two delvers fight; equivalences read off the indifference point ==== */
      runDuelLab: () => {
        if (s.duelBusy) return;
        SFX.tap();
        this.setState({ duelBusy: true, duelRes: null, duelProg: "symmetry check\u2026" });
        const hp = s.balHp || 50, N = Math.min(400, s.balN || 300), bench = true;
        setTimeout(() => {
          const sym = this.duelRuns(600, 0xD0E1, null, null, hp, bench);
          const stats = [["hp", "HP"], ["def", "DEF"], ["spd", "SPD"], ["lck", "LCK"]];
          const out = [];
          let i = 0;
          const step = () => {
            if (i >= stats.length) { this.setState({ duelBusy: false, duelProg: "", duelRes: { sym, rows: out, n: N, hp } }); return; }
            const [key, label] = stats[i];
            this.setState({ duelProg: "bisecting " + label + "\u2026 (" + (i + 1) + "/" + stats.length + ")" });
            setTimeout(() => {
              const x = this.duelEquiv(key, N, 0xA11CE + i * 7, hp, bench);
              out.push({ key, label, x });
              i++; step();
            }, 20);
          };
          step();
        }, 30);
      },
      duelBusy: !!s.duelBusy, duelProg: s.duelProg || "",
      duelHasRes: !!s.duelRes,
      duelBtnLabel: s.duelBusy ? "DUELING\u2026 " + (s.duelProg || "") : "RUN DUEL EQUIVALENCE",
      duelSym: s.duelRes ? (() => { const r = s.duelRes.sym; const pa = Math.round(100 * r.a / (r.a + r.b || 1));
        return "symmetry: " + pa + "/" + (100 - pa) + " over " + r.n + " mirror duels" + (r.d ? " \u00b7 " + r.d + " draws" : "") + (Math.abs(pa - 50) > 4 ? " \u26a0 engine bias!" : " \u2713"); })() : "",
      duelRows: s.duelRes ? s.duelRes.rows.map(r => ({ key: r.key, txt: "+10 ATK \u2248 +" + r.x + " " + r.label, per: (Math.round(r.x / 10 * 10) / 10) + " " + r.label + " / ATK" })) : [],
      duelNote: s.duelRes ? "mirror delvers @ " + s.duelRes.hp + " HP \u00b7 dodge+crit enabled \u00b7 " + s.duelRes.n + " duels/step \u00b7 7-step bisection" : "",
      openDressing: () => { SFX.tap(); this.setState({ modal: "dressing" }); },
      dressTitle: "CHANGING ROOM", dressSub: "Try outfits and colors. Everything saves instantly.",
      dressWardrobeTitle: "WARDROBE", dressColorsTitle: "COLORS", dressResetLabel: "RESET", dressDoneLabel: "DONE",
      dressBgGlyph: Object.assign({}, heroSprite(composeBg32(wardrobeCombo()), heroPal, 8), { width: "100%", height: "100%", zIndex: 0 }),
      dressGlyph: Object.assign({}, heroSprite(composeHeroOnly32(wardrobeCombo()), heroPal, 8), { width: "100%", height: "100%", zIndex: 1 }),
      dressFrameGlyph: Object.assign({}, heroSprite(composeFrame32(wardrobeCombo()), heroPal, 8), { width: "100%", height: "100%", zIndex: 2, pointerEvents: "none" }),
      dressSlots: slotList().map(slot => {
        let list = Studio.itemList(slot);
        if (slotDefault(slot)) list = list.filter(id => id !== "none");   /* a defined default bans "none" */
        const w = Store.get("dd.wardrobe", {}) || {};
        let cur = list.indexOf(w[slot] || slotDefault(slot) || "none"); if (cur < 0) cur = 0;
        const shift = d => { const ww = Store.get("dd.wardrobe", {}) || {}; ww[slot] = list[(cur + d + list.length) % list.length]; Store.set("dd.wardrobe", ww); SFX.tap(); this.setState({}); };
        return { key: slot, label: slotMeta(slot).toUpperCase(), value: Studio.itemName(slot, list[cur]).toUpperCase(), prev: () => shift(-1), next: () => shift(1) };
      }),
      dressFams: (() => {
        const fh = Store.get("dd.studio.famHex", {}) || {};
        const HUES = { Red: 1, Orange: 1, Yellow: 1, Green: 1, Blue: 1, Teal: 1, Purple: 1, Pink: 1, Brown: 1 };
        const MATERIALS = { Gold: 1, Steel: 1, Leather: 1, Wood: 1, Highlight: 1, Shadow: 1 };
        const rows = FAMILIES.filter(f => !HUES[f.name] && !MATERIALS[f.name]).map(f => {
          const cur = (fh[f.base] || f.hex).toLowerCase();
          const set = hex => { const m = Store.get("dd.studio.famHex", {}) || {}; if (hex === f.hex) delete m[f.base]; else m[f.base] = hex; Store.set("dd.studio.famHex", m); SFX.tap(); this.setState({}); };
          return { key: f.base, label: f.name.toUpperCase(),
            swatches: uniqHex([f.hex].concat(DRESS_SWATCHES[f.name] || DRESS_SWATCHES.generic)).map(hex => ({ style: swatchStyle(hex, hex.toLowerCase() === cur), pick: () => set(hex) })) };
        });
        return rows;
      })(),
      dressReset: () => { Store.set("dd.wardrobe", {}); Store.set("dd.studio.famHex", {}); SFX.tap(); this.setState({}); },
      dressRandom: () => {
        const HUES = { Red: 1, Orange: 1, Yellow: 1, Green: 1, Blue: 1, Teal: 1, Purple: 1, Pink: 1, Brown: 1 };
        const MATERIALS = { Gold: 1, Steel: 1, Leather: 1, Wood: 1, Highlight: 1, Shadow: 1 };
        const w = {};
        for (const slot of slotList()) {
          let list = Studio.itemList(slot);
          if (slotDefault(slot)) list = list.filter(id => id !== "none");
          w[slot] = list[Math.floor(Math.random() * list.length)];
        }
        Store.set("dd.wardrobe", w);
        const fh = {};
        for (const f of FAMILIES) {
          if (HUES[f.name] || MATERIALS[f.name]) continue;
          const opts = uniqHex([f.hex].concat(DRESS_SWATCHES[f.name] || DRESS_SWATCHES.generic));
          const hex = opts[Math.floor(Math.random() * opts.length)];
          if (hex !== f.hex) fh[f.base] = hex;
        }
        Store.set("dd.studio.famHex", fh);
        studioBump(); SFX.tap(); this.setState({});
      },
      dressOpenLabel: "\ud83d\udc55 Changing Room",
      settingsTitle: this.t("settingsTitle"), languageTitle: this.t("languageTitle"), soundLabel: this.t("soundLabel"), nameLabel: this.t("nameLabel"),
      langs: Object.keys(window.DD_STRINGS || { en: 1 }).map(l => ({
        code: l, label: langNames[l] || l, go: () => this.setLang(l),
        style: { all: "unset", boxSizing: "border-box", textAlign: "center", padding: "12px 6px", border: "2px solid " + (s.lang === l ? accent : "#3A3328"), color: s.lang === l ? accent : "#B9B0A0", cursor: "pointer", fontSize: "13px" }
      })),
      muteLabel: this.t(s.muted ? "soundOff" : "soundOn"),
      muteStyle: { all: "unset", boxSizing: "border-box", display: "block", width: "100%", textAlign: "center", padding: "13px", border: "2px solid " + (s.muted ? "#3A3328" : accent), color: s.muted ? "#8B8172" : accent, cursor: "pointer", fontSize: "13px" },
      toggleMute: () => { const m = !s.muted; Store.set("dd.mute", m); SFX.setMuted(m); this.setState({ muted: m }); if (!m) SFX.tap(); },
      name: s.name, onName: e => this.setName(e.target.value),
      lang: s.lang, onLangSel: e => { SFX.tap(); this.setLang(e.target.value); },
      profEditing: !!s.profEdit, profNotEditing: !s.profEdit,
      profEditName: () => { SFX.tap(); this.setState({ profEdit: true }); },
      profEditDone: () => this.setState({ profEdit: false }),
      welcomeTitle: this.t("welcomeTitle"), welcomeSub: this.t("welcomeSub"), welcomeBegin: this.t("welcomeBegin"),

      relicGlyph: relic ? ricon(relic, 76) : {},
      relicSlot: relic ? rslot(relic, { width: "84px", height: "84px", margin: "0 auto" }) : {},
      relicKind: relic ? (ITEMS[relic].kind || "") : "",
      relicWheel: relic && ITEMS[relic].wheel ? ITEMS[relic].wheel[0] + " \u2192 " + ITEMS[relic].wheel[1] : "",
      relicWheelStyle: relic && ITEMS[relic].wheel ? { fontFamily: "'Silkscreen',monospace", fontSize: "8px", letterSpacing: "1px", padding: "4px 9px", border: "1px solid #4A3F2E", color: "#E7E0D2", background: "linear-gradient(90deg," + ({ BLOOD: "#C4595C", GOLD: "#E3B341", GRACE: "#9B8BD0" })[ITEMS[relic].wheel[0]] + "33," + ({ BLOOD: "#C4595C", GOLD: "#E3B341", GRACE: "#9B8BD0" })[ITEMS[relic].wheel[1]] + "33)" } : { display: "none" },
      relicKindStyle: relic ? { all: "unset", boxSizing: "border-box", cursor: KIND_META[ITEMS[relic].kind] ? "pointer" : "default", fontFamily: "'Silkscreen',monospace", fontSize: "8px", letterSpacing: "1.5px", color: kindColor(ITEMS[relic].kind), border: "1px solid " + kindColor(ITEMS[relic].kind) + "55", padding: "4px 9px" } : {},
      openRelicSet: () => { const k = relic && ITEMS[relic].kind; if (KIND_META[k]) { SFX.tap(); this.setState({ modal: "setcard", setKind: k }); } },
      relicGaugesOn: (() => this.relicGauges(relic).length > 0)(),
      relicGauges: this.relicGauges(relic),
      relicName: relic ? (((this.G && this.G.awake && this.G.awake[s.relicIdx]) ? "\u2726 " : (s.sockets && s.sockets[s.relicIdx] != null ? "\u25c6 " : "")) + this.itemName(relic)) : "",
      relicDesc: relic ? this.richDesc(((this.G && this.G.awake && this.G.awake[s.relicIdx]) ? "\u2726 AWAKENED \u2014 " + (AWAKE_TEXT[relic] || "runs deeper.") + " " : "") + this.itemDesc(relic) + (s.sockets && COMPONENTS[s.sockets[s.relicIdx]] ? " \u2014 " + COMPONENTS[s.sockets[s.relicIdx]].n + ": " + COMPONENTS[s.sockets[s.relicIdx]].d : "")) : "",
      relicOwned: relic && count(s.items, relic) > 1 ? this.t("ownedTimes", { n: count(s.items, relic) }) : "",
      relicAwakeLine: relic && AWAKE_TEXT[relic] && !(this.G && this.G.awake && this.G.awake[s.relicIdx]) ? "\u2726 AWAKENED: " + AWAKE_TEXT[relic] : "",
      relicFlavor: relic && RELIC_META[relic] ? "\u201c" + RELIC_META[relic].fl + "\u201d" : "",
      relicModes: relic ? (ITEMS[relic].modes || ["delve", "versus"]).map(m => ({ key: m, label: m === "delve" ? "\u26cf DELVE" : "\u2694 VERSUS" })) : [],
      relicRows: (() => {
        if (!relic || !RELIC_META[relic]) return [];
        const m = RELIC_META[relic], rows = [];
        if (m.pas) rows.push({ k: "PASSIVE", v: m.pas, c: "#8FA8C4" });
        if (m.nat) rows.push({ k: "TRIGGER", v: m.nat, c: "#C4906A" });
        if (m.act) rows.push({ k: m.nat ? "ON ACTIVATION" : "\u25b8 IF RELAYED", v: m.act, c: "#7C9A6A" });
        const sock = s.sockets ? s.sockets[s.relicIdx] : null;
        if (sock && COMPONENTS[sock]) rows.push({ k: TRIGGERS[sock] ? "\u25c6 SOCKETED TRIGGER" : "\u25c6 SOCKETED EMITTER", v: COMPONENTS[sock].n + " \u2014 " + COMPONENTS[sock].d, c: "#E3B341" });
        return rows.map(r => ({ key: r.k, label: r.k, text: this.richDesc(r.v), labStyle: { fontFamily: "'Silkscreen',monospace", fontSize: "7.5px", letterSpacing: "1.5px", color: r.c, flex: "0 0 92px", paddingTop: "2px", textAlign: "left" } }));
      })(),

      openSupporter: () => { tele("sup_open", {}); this.setState({ modal: "supporter" }); },
      openStudio: () => { try { window.DDStudio && window.DDStudio.open(); } catch (e) { } },
      studioTitle: "STUDIO",
      studioOpenLabel: "\ud83c\udfa8 Part Studio",
      supporterPack: this.t("supporterPack").replace("✦ ", "✦ "), supSub: this.t("supSub"),
      perks: [["spark", "gildedTitle", "gildedTitleD"], ["ban", "noAdsPerk", "noAdsPerkD"], ["coin", "startSparks", "startSparksD"]].map(p => ({
        glyph: glyph(p[0], 1.5), title: this.t(p[1]), desc: this.t(p[2])
      })),
      supPrice: this.t("supPrice"), supOwned: s.supporter, supNotOwned: !s.supporter,
      supOwnedNote: this.t("supOwnedNote"), supBuy: this.t("supBuy"), maybeLater: this.t("maybeLater"), supFine: this.t("supFine"),
      buySupporter: () => this.buySupporter()
    };
  }
}