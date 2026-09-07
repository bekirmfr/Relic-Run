# Testing

Two runners, **one set of source files**. `RelicRun.Core` sets `noEngineReferences`, so it is
ordinary C# and needs no Unity to run — which is what makes the fast loop possible.

## Fast loop — `dotnet test`

```bash
dotnet test Tools/dotnet/RelicRun.Core.Tests
```

Runs in about a second, needs no editor, works in CI, and can be run by an agent. This is the
loop that verifies each phase against the golden corpus while it is being written.

`Tools/dotnet/RelicRun.Core.Tests.csproj` **compiles** the Unity sources by glob — it never copies
them:

```xml
<Compile Include="../../../Assets/GameAssets/Core/**/*.cs" />
<Compile Include="../../../Assets/GameAssets/Tests/**/*.cs" />
```

## Parity check — Unity Test Runner

In the editor: **Window → General → Test Runner → EditMode → Run All**.

Headless (close the editor first — Unity will not take the project lock twice):

```bash
"C:/Program Files/Unity/Hub/Editor/6000.0.68f1/Editor/Unity.exe" -runTests -batchmode -projectPath . -testPlatform EditMode -testResults Logs/tests.xml
```

Run this at phase boundaries. It confirms the same code compiles and passes inside Unity, and it
is the only way to test the `Game` and `Editor` assemblies once those exist.

## Two rules that keep the arrangement honest

**C# 9 only.** The csproj pins `<LangVersion>9.0</LangVersion>` because that is what Unity 6
compiles. Without the pin, `dotnet` would accept C# 13 and let newer syntax through, and the
failure would surface in Unity much later and far from its cause. If `dotnet test` rejects your
syntax, Unity would have too.

**Tests stay Unity-free.** Everything under `Assets/GameAssets/Tests/` is compiled by both
runners, so it must not reference `UnityEngine`. Tests that genuinely need Unity belong in a
separate assembly, added when the `Game` layer arrives.

**Corpus values are integers.** Never record a float as the expected value of a gate. Unity's
JSON parser is not correctly rounded: it read `0.1853655439335853` back one ULP high, failing the
RNG gate against a port that was bit-perfect, while .NET 9's correctly-rounded parser passed and
hid it. The generator is recorded as its raw 32-bit output instead — a draw is exactly
`raw / 2^32`, so the integer is lossless and every parser agrees. All 446,999 numbers across the
corpus are now integers; keep it that way when adding cases.

JSON goes through Newtonsoft on both sides — Unity supplies it as
`com.unity.nuget.newtonsoft-json`, the csproj references the identical NuGet package — so no
conditional compilation is needed anywhere.

## Where a mode difference lives

A relic that behaves differently in versus is tuned at the relic, in
`Core/Content/RelicTuning.cs`, not in the engine. That table is where to look to answer "what
does Rabbit's Foot do in versus?", and it means the relic layer never branches on the mode.

It is per-mode DATA on one relic, not a second relic. A separate `altar_versus` id would have
to fork the card name, the icon cell, both localisation keys and the relic's draft-pool
membership — and the corpus records provenance by id, so 36 loadout entries and 203 events in
the duel corpus alone say `altar`. Changing pool membership would also change which relics the
draft samples, moving the RNG stream and invalidating the recordings the port is verified
against.

## What is actually shared

`CombatEngine` and `DuelEngine` still exist as two classes, but neither holds a combat rule any
more. Both are adapters: they bind one mode's state to the shared layer and implement
`ICombatBus`. Both sides of a fight are an `ICombatActor` — a delve foe is a `FoeActor` that
reads its own relics, not a stat block — so the shared rules never ask which mode they are in.

| shared | file |
| --- | --- |
| every relic activation | `RelicEffects.Apply` |
| heal, gold, luck, the Rabbit's reaction | `CombatPrimitives` |
| a side's whole turn, the riposte, the free action | `CombatTurn` |
| landing a blow, mitigating it, the dodge, the kill, the execution, refusing death, the defender's answers | `CombatDamage` |
| sockets: emitter strength, firing a copy, firing a trigger | `SocketFiring` |
| the ATB gauges and the opening | `AtbScheduler` |

`CombatDamage.Mitigate` is one ladder for every blow in the game, and it reads the **defender's**
own relics. That is the whole reason the delve looked for a long time as though it needed a
second, simpler version: the JS wrote the ladder out twice there — once in `enemyHits` with the
hero defending, once in `dealDmg` with the foe — while the duel wrote it once and used it both
ways. A foe carries no Guard set and no Padded Hide, so the same ladder collapses to armor when
the foe is the one being hit.

What is still written per engine, and why each is real:

- **the fight loop** — a delve walks a pack with per-fight resets, carry state and revives; a
  duel resolves one symmetric pair. The scheduler underneath is shared.
- **a delve foe's crit** — Weighted Dice multiplies AFTER mitigation for a foe and before it
  for a full side, so the foe's turn applies it in `ApplyDamage`, which returns what actually
  landed so the defender answers the larger number.
- **a delve foe's Thorn Vest and Berserker Charm** — the JS deliberately gives a foe's Thorn
  Vest a flatter rule than the player's (no chain scale, no Guard-5 bonus, no emitter). They
  are monster traits that share a name with a relic, not the relic.
- **`ClaimsTheKill`** — a foe that fells the hero ends the run; it does not collect loot.

Everything else in both files is a one-line `ICombatBus` member reading or writing that mode's
state.

## Where the modes disagree

Seven rules that once separated a duel from a delve have deliberately been settled on the
delve's answer, so versus and delve now share them. `RelicTuning` lost its Iron Skin and Hare's
Drum entries entirely — those relics no longer behave differently by mode, so there is nothing
to tune.

| rule | both modes now | versus, as the JS had it |
| --- | --- | --- |
| `ArmorMeetsRelicDamage` | every blow | genuine strikes only |
| `StaggerTripsOnBeingHit` | the striker trips on its own turn | the struck side trips |
| `WhetstoneSundersOnlyPlainStrikes` | crits and relics do not sunder | everything sunders |
| `ReturnedBlowUsesTheStrikersAttack` | the striker's attack | the blow turned aside |
| `ReturnedBlowDepth` | 0 — the whole of a turn | 1 — a consequence |
| `IronSkinGlancesOneBlow` | yes | no |
| `RiposteStrikesWhenAwakened` | yes | no |

What still differs by mode lives in `CombatRules.Duel()` and is unchanged: the chain cap of 4,
the reaction order, the Stone Emitter's floor, the Curse set, the Greed emitter's cadence,
whether a kill sharpens the Edge set or fires a trigger, and whether it shares the blow's chain.

### Keeping the gate alive across a deliberate change

`Duel()` no longer reproduces the source, so the duel corpus would fail against it. That does
not mean the gate is spent — the ENGINE is still exact, only the shipped configuration moved.
`CombatRules.DuelAsRecorded()` is `Duel()` plus the seven old answers and nothing else, and the
corpus replays under it. The diff between the two rulesets IS the design change, in one place,
where it cannot silently drift.

That leaves the shipped rules untested, which is the trap: put any one of the seven back and
every corpus gate stays green. Two tests close it.

`EveryAdoptedRuleChangesADuel` reverts each of the seven on its own and requires at least one
recorded duel to play differently. A rule that can be put back with no visible effect was never
really adopted. Reverting any of the seven in `Duel()` fails it — all seven are load-bearing.

`EveryShippedDuelEndsInADeath` asserts no duel runs out the scheduler's 600 iterations with
both sides alive. Adopting `ArmorMeetsRelicDamage` means relic damage is now reduced in a duel
too, and the percentage curve floors at 1, so a chip-damage build against an armored rival is
the shape that could stall. It does not. Cutting the cap to 40 stalls 46 of 116, so the test
notices.

## Single-sourcing

The relic layer and the shared bus primitives exist once and are used by both modes, with
their differences held as data in `CombatRules`. The check that this is real rather than
cosmetic is a mutation: changing one line in `CombatPrimitives` (the Alchemist's Vial heal)
turns all four fight gates red — solo, primitives, mixed and duels. If a future change makes
that mutation break only one mode, the layer has quietly forked again.

## Reaching a rule the random tiers cannot

Two rules were untestable until the corpus was given cases built for them, and both are worth
keeping as a pattern.

The **Curse set's death blast** needs seven CURSE relics, and the pool holds five distinct
ones, so no random loadout can reach it. `edge/curse7/*` stacks deliberate duplicates on a
hero already at three HP. Removing the blast, or changing its damage from three times attack
to twice, now fails.

The **defender's reaction order** needs two answers firing on the same blow. `edge/order/*`
carries Mirror Scale, an awakened Troll Marrow and Thorn Vest together, against dungeons whose
bosses hold Weighted Dice so the crit that Mirror Scale answers actually lands. Running the
delve in the duel's order now fails on all 24 cases.

That second one also corrected a wrong conclusion: the order was first assumed to matter only
when one answer kills the target and denies a later one. It matters far more simply than that
— the event stream's order is itself compared, so any two reactions on one blow pin it. The
earlier mutation survived only because random loadouts rarely carry two reacting relics at
once, which is a statement about corpus coverage rather than about the rule.

## The run gate

`runs.json` is Phase 5's contract, and it is built differently from the fight corpus. The
source's own headless run loop, `balanceRuns`, walks a full thirteen floors over the real
content, so rather than re-walking that loop in a recorder — where a transcription that drifted
would record the RECORDER's run and the port would be verified against a mistake — it is
instrumented. Read-only hooks are injected at seven points in the lifted source. With them
installed `balanceRuns` returns byte-identical aggregates, which is the proof they change
nothing.

`balanceRuns` also carries a greedy bot: `pickScore` ranks a draft, `eventChoice` reads an
event's hint text, `punchOf` decides whether the bazaar awakens or buys. None of that is a game
rule, so none of it is ported. The corpus records the DECISIONS and the port replays them
through `IRunChoices`; what the port must reproduce is everything around a decision. An offer is
compared as a SET for the same reason — which relics `weightedRelic` drew is a rule, the order
they are ranked in is not.

Two things the recorder has to override, and both are in `instrument.mjs` with the reason:

- **The pack path.** `balanceRuns` prices foes through the Balance Lab's editable formula table;
  the game passes a dungeon and no curve. The two round differently — floor 10 alone looted ten
  gold more — so the recorder takes the game's path. Pack composition stays gated by
  `packs.json`, which is what keeps a run-loop failure from being a pack bug in disguise.
- **The bot's greed.** It always refuses the Chained Ghost's locket, never rerolls with a
  Merchant's Thumb in hand, and never awakens what it does not rate. Steering the chooser
  reaches rules the greedy line never walks past; it changes nothing under test.

The source wraps an event outcome in `try/catch`, which is right for a game and dangerous for a
lift: an identifier the lift forgot makes an outcome silently do NOTHING, and the corpus records
the no-op as truth. `luckRoll` was missing at first and four of the twelve events quietly
stopped rolling. The catch stays; the error is now handed to the recorder, which refuses to
write a corpus containing one.

### What the run gate cannot reach

A corpus only catches what its own runs happen to do, and three rules sat outside that. Rather
than wait for a run to stumble into them, `RunRuleTests` asks each one directly:

- **The breather** is invisible on the first floor, because a run starts whole and healing a
  full pool changes nothing. It is now a function that can be asked what floor 1 is worth, and
  the answer is nought — not because the health is full, but because there is no floor before it.
- **An event grant's pool** is invisible while no relic is versus-only. Asked from the versus
  side it is not: a duel must never be handed a Greedy Curse, a Second Stomach or a Merchant's
  Thumb. A companion test fails if the pools ever stop differing, so the first test cannot go
  blind without saying so.
- **A Debt of Flesh** pays double once awakened, and in the source that could never happen: the
  bazaar only awakens a relic that STACKS, and this one did not. It stacks under this port's own
  rules — see `RunRules` — which is what makes the rule real. The bazaar's own rule, that a
  relic must stack and have a copy not yet awake, is ported alongside it.

`RunRules` is where the port's answers deliberately differ from the source's, the same
arrangement as `CombatRules.DuelAsRecorded`: `Shipped()` is what the game plays and
`AsRecorded()` is what the corpus replays, so the diff between them IS the design change.

Two mutations still survive, and both are unreachable rather than untested:

| survives | why |
| --- | --- |
| the defence floor is one lower (−2 → −3) | only one event costs defence, one point, and no event repeats in a run — so the total can never pass −1, let alone −2 |
| two stars start at 0.70 rather than 0.72 | depth is a fraction of thirteen floors, so a threshold is pinned only as tightly as the gap it sits in; 0.72 lies between floor 10 (0.6923) and floor 11 (0.7692). 0.65 fails |

Both are ported as written and commented where they sit.

## Scoring a finished run

`outcomes.json` covers what a run was worth: banked gold, score, stars and XP. The source keeps
that arithmetic inside a React method, wrapped in sound, telemetry and screen transitions, so it
cannot be lifted whole the way a fight can — the recorder stubs the presentation and calls the
real thing.

Two things it forced into the open:

- **Awakenings are per COPY, not per relic.** The game stores them keyed by inventory slot and
  converts to per-id counts with `awakeById` before anything downstream sees them. The Balance
  Lab keys them by id instead, which is equivalent only because it never awakens the same relic
  twice. Passing ids to the scoring reads as nothing awake at all, which is how a Piggy Bank
  quietly stopped taking its awakened cut.
- **`relevance` is the only fraction in the calculation** and is deliberately not recorded. It
  exists to scale XP, so pinning the resulting `xpGain` pins it exactly and keeps the corpus
  free of float literals.

Nineteen mutations, eighteen die. The survivor is the two-star threshold at 0.72: depth is a
fraction of thirteen floors, so a threshold is pinned no more tightly than the gap it sits in,
and 0.72 falls between floor 10 (0.6923) and floor 11 (0.7692). Moving it to 0.70 changes
nothing; moving it to 0.65 fails. The 0.6 and 0.4 thresholds sit in narrower gaps and are
pinned.

## The revive, which has no corpus

Everything else in the run loop is diffed against a recording. The revive cannot be: the
source's lives in a React method that spends sparks or plays an advertisement, animates, and
calls back into the fight pipeline, and the Balance Lab's headless loop has no notion of being
brought back. There is nothing to record.

`RunReviveTests` checks what the rule says instead — half a pool rounded down and never less
than one, offered once per run, a run that takes it never ending sooner than one that refuses,
and a resumed fight that meets the foe that felled the hero with its wounds intact rather than
starting the floor over. That is weaker than a corpus and is called out here so it is not
mistaken for one. The numbers came off the source by reading, not by diffing.

## Mutation testing

A gate that has never gone red is not evidence of anything. Every phase so far has been
mutation-tested: break the rule deliberately, confirm the suite fails, restore. Several holes
were only found this way — the stat ledger's bloodied boundary, and the chain corpus not
exercising decay at all until loadouts were added that make one relic fire twice in a single
chain.

One Phase 6 rule is unobservable: the duel keys chain decay per SIDE as well as per relic,
but a chain object only ever belongs to one side — every cross-side reaction opens a fresh one
— so the prefix never prevents a collision. Removing it still passes. Kept as written, since a
future change that shared a chain would need it.

One Phase 4 rule is also unobservable: the per-beat guard that lets a genuine event wake each
socketed copy only once. Removing it entirely still passes, because reaching the same socket
twice inside one beat needs two kills or two qualifying strikes in a single turn, which the
corpus never produces. It is ported as written.

Two Phase 3 rules remain unobservable and are knowingly untested: Blood Altar's guard against
answering its own heal (it cannot cause a heal until awakened, in Phase 4), and whether a
relic's activation is counted when there is nothing left to hit (it changes only a third visit,
which these six relics cannot reach). Both are ported faithfully; neither has a red test behind
it yet.

## The corpus

Tests read `Tools/corpus/`. If it is missing or you have changed the JS source:

```bash
node Tools/capture/capture.mjs    # regenerate (deterministic, fixed master seed)
node Tools/capture/verify.mjs     # prove every case replays from its recorded input
```

Note that `Tools/out/progression.json` and `Tools/corpus/progression.json` are different
files: the first is content extracted from the source, the second is recorded behaviour used
as a gate. Tests read the corpus one through `Corpus.Object`, and content through
`Corpus.ArrayFromContent`.

Content JSON is separate, and has its own gate:

```bash
node Tools/extract/extract.mjs
node Tools/extract/gen-csharp.mjs   # regenerate Core/Content/*.cs
node Tools/extract/validate.mjs
```

## Current gates

| Gate | Corpus | Status |
|---|---|---|
| Phase 0 — content | `Tools/out/` | passing, 4 tracked content gaps |
| Phase 0 — corpus | `Tools/corpus/` | passing, 783 cases replay exactly |
| Phase 1 — RNG | `rng.json` | passing, 5 seeds × 1000 raw draws bit-exact |
| Phase 1 — defence | `defense.json` | passing, 1800 grid cells |
| Phase 1 — stat ledger | `statledger.json` | passing, 566 contexts x 4 stats, 5033 labelled rows |
| Phase 2 — combat engine | `bare.json` | passing, 48 fights replay event-for-event |
| Phase 3 — chain bus | `primitives.json` | passing, 86 fights; decay, depth and wasted heals covered |
| Phase 4a — relic effects | `solo.json` | passing, all 50 relics alone across 150 fights |
| Phase 4b — sockets | `mixed.json` | passing, 120 fights with duplicates, sockets and awakenings |
| Phase 5a — enemy packs | `packs.json` | passing, 360 packs regenerate exactly |
| Phase 5b — progression | `corpus/progression.json` | passing, levels, perks and the reward table |
| Phase 6 — versus | `duel.json` | passing, 72 duels replay event-for-event |
