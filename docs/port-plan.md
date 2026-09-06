# Relic Run — HTML/JS → Unity Port Plan

Source: `.port/Daily Delve.dc.html` (8,845 lines) + `strings.js` + `assets/` + `hero-pack.json`
Target: Unity 6000.0.68f1, URP, VContainer + UniTask + Addressables, on the GameLift framework package.

**Read `.port/GAME-REFERENCE.md` before touching gameplay code.** It is the authority on formulas, the
demotion ladder, and the chain/decay rules. This document is the *how we move it* plan; that one is the
*what it does* spec.

---

## 0. Locked decisions

| Decision | Choice |
|---|---|
| Modes shipping | **All** — Delve + 10 dungeons, Versus, Daily Delve, Bestiary/Relic book/Profile |
| Hero art authoring | **Stays in the JS Part Studio.** Unity consumes `hero-pack.json` only. No Part Studio port. |
| Orphan relics | **Cut.** Port the 50 reachable relics; the 28 unobtainable ones are triaged out (see §3.1). |
| Dev tooling | **Editor windows, built early.** Headless runner + log differ land in Phase 1, before relic work. |

---

## 1. Architecture invariants

These are the rules every phase is measured against. If a change violates one, it is wrong even if it works.

1. **The engine is pure C#.** `RelicRun.Core` has no reference to `UnityEngine`. It is deterministic,
   headless, and unit-testable. This is what makes the Balance Lab nearly free and the corpus diffing possible.
2. **Simulate, then replay.** Combat resolves to completion synchronously and returns an event list.
   Nothing in the presentation layer computes combat, ever.
3. **One engine.** `simulateFloor`, `simulateDuel` and the `balanceRuns` harness collapse into a single
   `CombatEngine`. Both sides are `Combatant`. Delve is an asymmetric *configuration*, not a second engine.
4. **One stat ledger.** No code path computes a stat inline. Everything reads `StatLedger`, everything
   writes labeled `{stat, source, amount}` modifiers. HUD, cards, engine, sandbox, harness — one truth.
5. **Events carry their own state.** Every `CombatEvent` embeds a full snapshot (HP, gold, counters,
   modifier ledger). The view is stateless about combat and renders purely from the event.
6. **Relics are per-copy.** Inventory is an ordered list with duplicates; sockets map *array index* →
   trigger/emitter. Never model inventory as `Dictionary<RelicId,int>` — it breaks awakening.
7. **Port control flow literally, refactor after green.** RNG draws are order-coupled: reordering two
   conditions changes outcomes. Get the corpus green first, clean up second.
8. **Content is generated, not transcribed.** 50 relics × 8 locales × 10 dungeons is extracted by script
   from the JS and imported into ScriptableObjects. No hand-typing.

---

## 2. Solution layout

```
Assets/GameAssets/
├─ Core/                          RelicRun.Core.asmdef   ← NO UnityEngine reference
│  ├─ Determinism/
│  │  ├─ Mulberry32.cs                mulberry32
│  │  ├─ JsMath.cs                    JS round/floor/imul semantics
│  │  └─ DailySeed.cs                 dailySeed
│  ├─ Stats/
│  │  ├─ StatLedger.cs                heroStatRows + heroStatOf
│  │  ├─ StatModifier.cs              {stat, source, amount, tag}
│  │  └─ Defense.cs                   applyDef  (pct model, flat kept for the lab)
│  ├─ Combat/
│  │  ├─ CombatEngine.cs              the ATB loop + pack iteration
│  │  ├─ CombatBus.cs                 DealDamage / Heal / GainGold / EmitLuck / FireTrigger / FireEmitter
│  │  ├─ ChainContext.cs              depth, per-relic decay, cap, provenance
│  │  ├─ Combatant.cs                 BOTH sides
│  │  ├─ CombatEvent.cs               struct: type, amount, relic, copyIndex, depth, tick, snapshot
│  │  ├─ CombatSnapshot.cs
│  │  ├─ CombatEventType.cs           enum — never strings
│  │  └─ CarryState.cs                _carry: floor-scoped state for revive continuity
│  ├─ Relics/
│  │  ├─ IRelicEffect.cs
│  │  ├─ RelicRegistry.cs
│  │  ├─ SetBonuses.cs                KIND_META tiers 3/5/7
│  │  └─ Effects/*.cs                 one file per relic (50)
│  ├─ Run/
│  │  ├─ EnemyPackGenerator.cs        packFor + LADDER + pools
│  │  ├─ DelveRun.cs                  floors, draft, descend/cash-out, breather, revive
│  │  ├─ VersusRun.cs                 rounds, lives, roster, bot drafting
│  │  ├─ Bazaar.cs                    buy / awaken / socket
│  │  ├─ DungeonEvents.cs             the 12 events + choice outcomes
│  │  └─ Progression.cs               META: xp, levels, perks, rewardMult
│  └─ Content/                        POCOs: RelicDef, DungeonDef, EnemyDef, EventDef, KindDef
│
├─ Game/                          RelicRun.Game.asmdef   ← Core + Unity
│  ├─ Data/                           ScriptableObject wrappers → project into Core POCOs
│  ├─ Services/
│  │  ├─ DungeonService.cs            fills the existing stub
│  │  ├─ RelicRunSaveModel.cs         Store → ISaveService
│  │  └─ RelicRunAnalytics.cs         Telemetry taxonomy → AnalyticsService
│  ├─ Presentation/
│  │  ├─ CombatPlaybackController.cs  consumes CombatEvent[] on UniTask timers
│  │  ├─ CombatView.cs                Apply(CombatEvent) — pure render
│  │  ├─ HeroCompositor.cs            stacks role-indexed sprites + palette shader
│  │  └─ HallPanController.cs
│  └─ UI/                             12 screens + 9 modals
│
├─ Editor/                        RelicRun.Editor.asmdef
│  ├─ Importers/                      JSON → ScriptableObjects, hero-pack → atlas
│  ├─ BalanceLab/                     headless runner + GUI
│  ├─ Sandbox/
│  └─ CorpusDiffer/                   replay captured JS logs against Core
│
└─ Tests/                         RelicRun.Tests.asmdef  ← EditMode, references Core
```

Plus, outside `Assets/`:

```
Tools/
├─ extract/          parses .port/Daily Delve.dc.html → JSON content files
├─ capture/          drives the HTML build headless, exports the golden corpus
└─ out/              generated JSON (checked in; regenerable)
```

---

## 3. Keep / cut inventory

### 3.1 Cut — with reasons

| What | Size | Why |
|---|---|---|
| `support.js` (DC runtime) | 1,911 ln | React template compiler. Not game code. |
| Part Studio + `StudioOps` + `Studio` + `SYNTH` + migrations | ~1,900 ln | Authoring stays in the browser. Unity reads the exported pack. |
| 28 orphan relics + their engine branches | ~28 effects | In `NEWR` and referenced by the engine, but absent from `ITEMS` → unreachable. Never playtested. |
| `simulateDuel` as a second engine | 339 ln | Merged into `CombatEngine`. |
| `balanceRuns` internal run-loop | ~180 ln | Replaced by calling the real engine headlessly. |
| `renderVals` | 1,429 ln | Replaced by per-screen view-models. |
| Template markup + inline CSS | ~1,900 ln | Replaced by uGUI prefabs + CanvasScaler layouts. |
| Legacy glyph bitmaps: `B`, `PAL`, `glyphBits`, `glyph`, `hero16`, `HERO_BASE`, `BASE32`/`HERO32` migration, `up2`, `up2Sparse` | ~200 ln | Superseded by `relic-icons.png` and the enemy sheet. |
| Verified-dead symbols: `ENEMY_CELL`, `NATIVE_TRIGGER`, `ENEMY_ART` (empty), `composeFrame32` (retired), `customRoles`, `slotFallback`, `freeRoleKey`, `purgeMaterialHex`, `purgeStaleStudio`, `migrateProp`, `migrateArmProp`, `toneTok` | — | Zero or one reference; several marked retired in-source. |
| `heroSprite` / `enemySprite` / `loadEnemyPixels` canvas + data-URL caches | ~120 ln | Replaced by sprite atlases. |
| `SFX` WebAudio synth | 30 ln | 8 clips through `IAudioService`. |
| `Store` localStorage impl | 10 ln | Seam kept, implementation replaced by `ISaveService`. |
| `Telemetry` transport (PostHog/beacon) | ~60 ln | Event taxonomy kept, transport replaced by `AnalyticsService`. |

**Triage list requirement:** before deleting, `Tools/extract` dumps the 28 orphan relics' names, card text
and engine branches to `docs/relics-cut.md` so they can be reintroduced deliberately.

### 3.2 Keep — the good implementations, ported faithfully

| What | Where it lands |
|---|---|
| Simulate-then-replay separation | `CombatEngine` → `CombatPlaybackController` |
| Integer ATB scheduler (GAUGE 100, SPD = drain rate, jump-to-next-action) | `CombatEngine` |
| Chain model: depth · per-relic decay · cap · provenance (`rid`) · beat-scoped `trigFired` | `ChainContext` + `CombatBus` |
| Per-copy relic identity + socket index map + `fireIdx` | `Combatant` + `CombatBus` |
| Cadence gates (`strikeN`/`painN`/`goldN` % 3) | `CombatBus` |
| Single stat ledger | `StatLedger` |
| Snapshot-carries-state events | `CombatEvent` |
| `_carry` floor-scoped state for exact revive | `CarryState` |
| Demotion ladder + sample-without-replacement rank pools | `EnemyPackGenerator` |
| Percentage DEF `20/(20+def)` | `Defense` |
| Relic mode gating (`modes`) + `liveFor`/`prodSetOf` liveness check | `RelicRegistry` |
| Frontier-relative XP table (`XP_STEPS`/`XP_FLOOR`) | `Progression` |
| Single storage seam | `RelicRunSaveModel` on `ISaveService` |
| Telemetry event taxonomy | `RelicRunAnalytics` |
| Flight recorder (`_sbLog` shape) | `CorpusDiffer` |

---

## 4. Phases

Each phase has a **gate** — an automated check that must pass before the next phase starts.

### Phase 0 — Extraction & scaffolding
No gameplay code.

- `Tools/extract` parses the DC file → `Tools/out/`:
  `relics.json` (50) · `dungeons.json` (10) · `events.json` (12) · `enemies.json` (13 + ladder + rank
  formulas) · `kinds.json` (8 + tier tables) · `meta.json` (levels/perks/XP) · `icons.json` (relic → sheet
  cell) · `locales/*.json` (8, from `strings.js` + `NEWR`)
- `docs/relics-cut.md` — the 28 triaged-out relics with their spec text.
- `Tools/capture` produces the golden corpus: **~200 seeded runs across both modes**, in the flight
  recorder's own shape (`{meta:{seed,mode}, fights:[{player, opponents, events, combatLog}]}`).

  > **Not via a browser.** The DC file loads `support.js` and `strings.js` but never loads React, and
  > `support.js` throws without `window.React` — the page cannot boot outside its authoring host, so
  > headless browser automation would mean reconstructing that host. Instead, `Tools/capture` lifts the
  > engine functions (`simulateFloor`, `simulateDuel`, `packFor`, `heroStatRows`, `mulberry32`) out of the
  > file and runs them in Node against small stubs for `this.itemName` / `this.t`. Fewer moving parts, no
  > browser dependency, and it is exactly what `balanceRuns` already does inside the game.
- Unity: create the four asmdefs and the folder skeleton above.

**Gate:** all JSON validates against a schema; corpus captured and committed; solution compiles.

> This phase is the highest-value hour in the project. Everything after it is verified by diffing against
> the corpus rather than by guessing.

### Phase 1 — Deterministic foundations
- `Mulberry32` (bit-exact: `unchecked`, `uint` shifts, `/ 4294967296.0`)
- `JsMath` — JS `Math.round` is half-**up**; C# `Math.Round` is banker's. One helper, used everywhere.
- `DailySeed`
- `StatLedger` + `Defense`
- `CorpusDiffer` skeleton + headless runner

**Gate:** 10,000 RNG draws match a captured JS dump exactly. Every `pl` (modifier ledger) snapshot in the
corpus reproduces from `StatLedger`.

### Phase 2 — Combat engine, zero relics
- `Combatant`, `CombatEvent`, `CombatSnapshot`, `CombatEventType` (20 kinds)
- `CombatEngine`: ATB loop, Battle Dash pre-empt, pack iteration, death, `CarryState`
- Enemies as `Combatant` with a small relic list (`thorns berserk tooth dice clover dash iron`)

**Gate:** every relic-less fight in the corpus replays event-for-event, including tick numbers.

### Phase 3 — Chain bus
- `ChainContext`: depth, `ctx.v[relicId]` revisit counting, `relicScale` (0.5 / 0.6 / 0.75), `CAPn`, fizzle
- `CombatBus`: the six primitives with `(amount, source, depth, relicId, ctx)`
- Provenance rule: `srcRid` set → join at depth+1 with decay; null → fresh root, once per copy per beat
- Cadence counters

**Gate:** corpus fights whose loadouts contain only chain primitives (Alchemist's Vial, Blood Altar, Thorn
Vest, Vampire Tooth) match exactly, including `fizzle` placement and depth values.

### Phase 4 — Relics (50) — the big one
- `IRelicEffect` with default-empty hooks: `OnActivate`, `OnGoldGained`, `OnHealed`, `OnDamageDealt`,
  `OnKill`, `OnHitTaken`, `OnDodge`, `ModifyStats`
- `RelicRegistry` keyed by a `RelicId` enum
- Sockets: 8 triggers × 7 emitters, per-copy
- `SetBonuses` — the 3/5/7 tiers as their own handlers (incl. the deliberate `hollowidol` double-count)

Port order: EDGE → GUARD → FLESH → PACE → LUCK → GREED → CHAIN → CURSE. Commit per kind.

**Gate:** the entire corpus replays 1:1. A relic is not done until its fights are green.

### Phase 5 — Delve run structure
- `EnemyPackGenerator`, `DelveRun`, `Bazaar`, `DungeonEvents` (12, with their choice outcomes),
  `Progression`, revive, scoring, cash-out
- `DungeonService` implements `IDungeonService`

**Gate:** full 13-floor corpus runs reproduce final floor, gold, kills, score and relic list.

### Phase 6 — Versus
- `VersusRun` on the same engine: 8-delver roster, rotating rival, 3 lives, round 5 bazaar
- Bot drafting, persistent rival stat sheets, `hallForDuel`, staging

**Gate:** corpus versus lobbies reproduce round-by-round.

> Phases 1–6 need no scene, no prefab, no sprite. The whole game is verifiable as a test suite before any
> UI exists.

### Phase 7 — Editor tooling GUI
- Balance Lab window: N runs, win %, deaths by floor, net damage per floor, stat sensitivity sweep
  (±1/5/10/50, paired seeds), duel lab (mirror symmetry + exchange rates by bisection), saveable benchmark
  with Δ, JSON export
- Sandbox window: live stat/relic/counter/flag control, live-vs-set columns, per-run log export
- Corpus differ as a test-runner target

### Phase 8 — Content into Unity
- Importers: `Tools/out/*.json` → `RelicDefSO`, `DungeonDefSO`, `EventDefSO`, `EnemyDefSO`, `LocaleSO`
- Sprite import: Point filter, no compression, Multiple mode with grid slicing —
  enemies `192×832` @ 64px (3 cols = rank, 13 rows = species) · relic icons `480×384` @ 48px (10×8) ·
  halls `650×181` · cards `50×72`
- `HeroPackImporter`: `hero-pack.json` → per-part/state/frame sprites into a SpriteAtlas, pixels encoded by
  **color-role index**; runtime `HeroCompositor` stacks 12 images in `stack` order and a `PaletteSwap`
  shader maps role index → color from a palette texture. Port only the color math
  (`hexToHsl`/`hslToHex`/`applyShade`/`shade`, ~40 lines) so Changing Room swatches recolor live.
- TMP font assets from `Jacquard12.ttf` and `PressStart2P.ttf`
- Addressables groups

**Gate:** a test asserts every ScriptableObject's content equals its source JSON.

### Phase 9 — Presentation
- `CombatPlaybackController` — `CancellationTokenSource` per fight replaces the
  `later()`/`schedNext()`/`_ptk` single-flight dance; pause becomes an await gate
- `CombatView.Apply(CombatEvent)`, hero/enemy units, damage numbers, log strip with `↳` depth indent,
  relic tray with the `relicMeter` charge/uses gauges, hall pan
- Death shatter / blood / dust via UI-Particle (replaces `pileFrom`/`shatterOf` pixel reads)
- 8 SFX clips through `IAudioService`

**Gate:** a full delve run playable end to end.

### Phase 10 — UI screens & meta
- 12 screens (`title modes levels run staging over xp board profile bestiary relbook how`) + 9 modals
  (modals through GameLift `PopupService`)
- Three layouts: phone / tablet / desktop
- 8-locale localization
- Save model, settings, name, achievements, bestiary fog-of-war, relic book, profile, local score board
- Supporter / revive / rewarded ad / IAP through `PurchasingService`, `AdsService`, `CurrencyService`,
  `ReviveController`
- Analytics through `AnalyticsService`

### Phase 11 — Hardening
Determinism check in CI, device float verification, mobile perf pass, safe area, save versioning.

---

## 5. Risk register

| Risk | Impact | Mitigation |
|---|---|---|
| **Rounding semantics** — JS `Math.round` half-up vs C# banker's rounding | Silent balance drift across every damage/heal/gold line | `JsMath.Round(x) => Math.Floor(x + 0.5)`. Note `MidpointRounding.AwayFromZero` is *also* wrong (differs on negatives). Lint for raw `Math.Round`. |
| **RNG order coupling** | Any reordered condition changes outcomes | Port control flow literally in phases 2–4; refactor only once the corpus is green |
| **Engine merge** (`simulateFloor` + `simulateDuel`) | Highest-risk single task | Do it behind the corpus; diff *both* original engines' logs against the unified one |
| **Float determinism on device** — `Math.Pow` in `packFor` | Daily seeds could diverge IL2CPP/ARM vs editor | Precompute enemy stats per floor as integers at content-build time; verify on device |
| **Per-copy socket model** | Awakening silently breaks if flattened to counts | Invariant #6; a test that awakens copy #2 only |
| **No leaderboard backend exists** | `dd.scores` is localStorage-only today; "boards" are local | Ship local boards at parity; treat an online board as separate work with its own backend decision |
| **Telemetry is unconfigured** (`cfg.posthog: null`) | Ships dark in JS | Keep the taxonomy, wire to `AnalyticsService`, decide on a provider separately |

---

## 6. Verification strategy

The corpus is the contract. It lives in `Tools/corpus/`, is regenerated by `Tools/capture/capture.mjs`
and self-checked by `Tools/capture/verify.mjs`.

| File | Cases | Gate |
|---|---|---|
| `rng.json` | 5 seeds × 1000 draws | Phase 1 |
| `bare.json` | 48 | Phase 2 — the ATB loop, no relics |
| `primitives.json` | 40 | Phase 3 — chains, decay, fizzle |
| `solo.json` | 150 | Phase 4 — every relic alone, so a failure names one relic |
| `mixed.json` | 120 | Phase 4 — duplicates, sockets, awakenings together |
| `packs.json` | 360 | Phase 5 — `EnemyPackGenerator` |
| `duel.json` | 60 | Phase 6 — the versus engine |

Two properties make it usable as a contract:

- **Packs are recorded verbatim, and fights run on a separate seed.** Combat and pack generation
  therefore fail independently — a Phase 2 failure can never be a Phase 5 bug in disguise.
- **`verify.mjs` replays every case from its recorded input alone** and passes, which proves the
  recorded input is *sufficient*. No hidden engine state leaks into a fight.

**Corpus mode.** Events were recorded with `itemName(id) => id` and `t(key) => key`, so `src` holds
relic ids and string keys rather than display names. `CombatEngine` must offer the same mode for
diffing; it keeps `src` comparable and locale-independent. Every event field is contractual — set-bonus
log lines carry no `rid`, so `src` is their only identity, and those strings are hardcoded English in
the engine and port verbatim.

`CorpusDiffer` loads each case, feeds the recorded input and seed into `CombatEngine`, and diffs the
event stream field by field, printing the first divergence with both sides' context.

This turns "did I port 50 relics correctly?" from a judgement call into red/green — and it is the reason
Phase 0 comes before every line of C#.

---

## 7. Naming

The Unity project is **Relic Run**. "Daily Delve" is a *mode* inside it, not the product. Namespaces are
`RelicRun.Core.*` and `RelicRun.Game.*`; the JS `dd.*` save-key prefix is retired in favour of the
GameLift save repository's own keying.
