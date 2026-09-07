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

`delve.json` is Phase 5's contract, and unlike the fight corpus it is recorded by DRIVING the
game rather than by handing it an input. `Tools/capture/delve.mjs` lifts `startRun`, `pickRelic`,
`fight`, `playNext`, `descend`, `finishDescend`, `proceedDescend`, `chooseEvent`, the bazaar and
`revive`, stubs the presentation, and drains the callbacks the animation would have run —
because some of them draw. Then it plays four hundred delves and writes down what happened.

It used to be recorded from `balanceRuns`, the Balance Lab's own headless loop, which was the
only run loop that could be walked before the UI was drivable. That corpus was true — of the
Lab. The Lab is not the game, and the differences were not small:

- **The order of a floor.** The Lab walks breath, event, shop-or-(draft, fight). The game walks
  draft, fight, event, breath — the event sits in the gap BEYOND a floor, not in front of it —
  and it rolls the next floor's pack the moment a fight ends, before that floor's offer is
  drawn. Same seed, different stream, different run.
- **The bazaar.** The Lab models one deal, no Merchant's Thumb discount, and awakenings keyed by
  relic. The game discounts both prices for merely holding a Thumb, allows a second deal from
  level fifteen, refunds a Hollow Idol's fifteen when it is woken, and keys awakenings by
  INVENTORY SLOT — the copy in hand, not the relic.
- **A paid reroll** draws its replacement offer from the EVENT stream, not the fight stream. So
  paying for a reroll shifts the events that follow it.
- **The Flesh set.** Its +3 max HP is guarded by a flag, once per run — but a delve builds each
  floor's fight state without copying that flag in, and never copies it back out. So a delve
  re-grants it every floor. Versus copies it and gets the rule as written; the Lab hands the run
  state straight to the fight, which is why the harness could never see this.

The driver is a player, not a rule: which relic it drafts, when it pays to reroll, what it asks
the bazaar for, whether it takes a revive, whether it walks out at a gate. All of that is
recorded and the port replays it through `IRunChoices`; everything around a decision has to come
out of the port. An offer is compared as a SET — which relics `weightedRelic` drew is a rule, the
order they are shown in is presentation.

Runs are shaped by the delver's LEVEL and by the HALL, which are the two dials `startRun` has.
Level sets the purse, the stat bonuses, the breath, the third draft choice at ten and the second
bazaar deal at fifteen. The hall scales every foe by its own multiplier and hands every boss
that hall's relic kit — and two halls replace their bosses with the Ghoolem. Thirteen profiles
spread across levels 1 to 20 and halls 1, 3, 5, 7 and 10, each with its own relic preferences,
bazaar policy and event choice, so every branch of the loop is walked by somebody.

A hall's multiplier is a TABLE and not a formula, which is worth knowing because it does not
look like one: the first five entries are exact powers of 1.1 and the rest are rounded to four
places — 1.6105 where 1.1^5 is 1.61051. The port scored runs with the power for a while and got
the deep halls a point out; `DungeonCatalog` is generated from the table instead. The source
defines the formula too, as `META.tierMult`, and never calls it.

Two things worth knowing about how the corpus reaches what it reaches:

- **Some rules need several events in one run.** The floor a stat cannot be dragged past needs
  THREE speed losses — the Chained Ghost's locket and the Rusted Spike Trap for five each, and
  robbing the dead miner for three more — and about one run in two hundred places all three.
  The `slowed` profile asks the placement first, by starting runs and reading where the events
  went, and only plays the seeds that qualify. The shuffle stays the game's.
- **The recorder records `shopDone`.** That is the bazaar's own answer to "that is all for this
  visit", and it is what lets the replay catch a port that would have served one more deal —
  otherwise a mutant that widens the allowance would simply never be asked.

The source wraps an event outcome in `try/catch`, which is right for a game and dangerous for a
lift: an identifier the lift forgot makes an outcome silently do NOTHING, and a recorder would
write the no-op down as truth. `luckRoll` was missing at first and four of the twelve events
quietly stopped rolling. The recorder now refuses to write a step whose outcome never resolved.

### The reduced-motion path

As in versus, the recording takes the branch where presentation does not reach into the seeded
stream. One consequence is worth naming: a fight ends by rolling the next floor's pack, so the
bazaar is handed one it will never fight. The source rolls a replacement while the merchant's
hall pans past — but only on the animated path. With reduced motion set, the pan is skipped and
so is the roll, and floor 8 fights the pack that was rolled for floor 7.

The port does not inherit that. `RunRules.BazaarRollsItsOwnPack` is the seam, and the gate
replays the recorded answer.

### What the run gate cannot reach

A corpus only catches what its own runs happen to do. `RunRuleTests` asks the rest directly:

- **The breather** is invisible on the first floor, because a run starts whole and healing a
  full pool changes nothing. It is a function that can be asked what a gate is worth, and there
  is no gate before floor 1 — not because the health is full, but because there is no floor
  before it.
- **An event grant's pool, and a draft offer's**, are invisible while no relic is versus-only.
  Asked from the versus side they are not: a duel must never be handed a Greedy Curse, a Second
  Stomach or a Merchant's Thumb. A companion test fails if the pools ever stop differing, so
  neither can go blind without saying so.
- **A Debt of Flesh** pays double once awakened, and in the source that could never happen: the
  bazaar only awakens a relic that STACKS, and this one did not. It stacks under this port's own
  rules — see `RunRules` — which is what makes the rule real.
- **An awakening follows the copy it was bought for.** Slots are positional and the source never
  re-keys them, so an oath shattering out of slot one hands slot three's awakening to whatever
  slid into slot two. Reachable in play, but it needs an oath drafted late enough to outlive the
  bazaar with an awakening bought behind it, which no arbitrary seed obliged.

`RunRules` is where the port's answers deliberately differ from the source's, the same
arrangement as `CombatRules.DuelAsRecorded`: `Shipped()` is what the game plays and
`AsRecorded()` is what the corpus replays, so the diff between them IS the design change.
`EveryRuleThePortKeepsChangesADelve` reverts each one on its own and fails if any delve plays
the same either way — a rule that can be put back with no effect was never a decision.

### Two guards removed on purpose

The source floors an event's defence loss at −2 as well as its speed loss at −10, and floors the
purse at zero after every outcome. Neither is reachable:

- Nothing can spend more than ONE defence in a run — only the unclaimed chest costs any, and
  events are drawn without replacement.
- Every outcome that costs gold spends exactly the cost its choice declares, and a choice the
  purse cannot cover is never offered.

Removing a guard silently is how a guard comes to be missing when it is needed, so two tests
hold the assumptions open. `NoRunCanLoseEnoughDefenceToNeedAFloor` sums each event's worst case
and fails if it passes −1; making the chest cost two, or giving the Spike Trap a defence cost as
well, both fail it. `NoEventCanSpendMoreGoldThanItAsksFor` resolves every choice with exactly its
stated cost in the purse and fails if any leaves it in the red. Adding such an event therefore
says so, rather than quietly leaving runs to sink past a floor that used to be there.

### The five mutations that survive

Fifty-six mutations of the run layer, fifty-one die.

| survives | why |
| --- | --- |
| the breather may overfill the pool | equivalent. The clamp that follows subtracts the overflow from both the health and the recorded breath, restoring exactly what the `Min` would have produced |
| an awakened Second Stomach does not grow the pool | a Second Stomach does not stack, and the bazaar only wakes what stacks, so the branch cannot fire in the source |
| an awakened Merchant's Thumb buys no second deal | same: a Thumb does not stack |
| waking a Hollow Idol does not fill it | same: an idol does not stack |
| two stars start at 0.70 rather than 0.72 | depth is a fraction of thirteen floors, so a threshold is pinned only as tightly as the gap it sits in; 0.72 lies between floor 10 (0.6923) and floor 11 (0.7692), and every value in that range is the same rule. 0.65 crosses floor 10 and fails |

The middle three are the same shape as the Debt of Flesh before this port let it stack: a rule
that is written, is reachable from the code, and can never fire because the only counter that
awakens anything refuses relics that do not stack. Making any of them stack would make its
awakened half real, and the mutant would start dying. Each is commented where it sits.

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

## Running mutations

    python Tools/mutate/mutate.py Tools/mutate/run-layer.json

**Never mutate the project in place.** Unity watches the filesystem and recompiles what it
finds, and a mutation that lives only for the two seconds a test run takes is long enough for
the editor to catch it. It will then report a failure in code that has already been put back —
a bug that does not exist, in a file that is correct on disk and in git. That happened, and cost
a round trip to work out.

So the harness copies the sources it needs somewhere else and mutates the copy. The project is
never written to, which is a stronger guarantee than restoring carefully: there is nothing to
restore. It also runs the unmutated copy first and refuses to go on if that does not pass, since
a broken copy would report every mutant as killed.

A suite is a JSON list of `{name, file, find, replace}`. Each is applied alone and must make the
tests FAIL.

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
| Phase 0 — corpus | `Tools/corpus/` | passing, 1653 cases replay exactly |
| Phase 1 — RNG | `rng.json` | passing, 5 seeds × 1000 raw draws bit-exact |
| Phase 1 — defence | `defense.json` | passing, 1800 grid cells |
| Phase 1 — stat ledger | `statledger.json` | passing, 566 contexts x 4 stats, 5033 labelled rows |
| Phase 2 — combat engine | `bare.json` | passing, 48 fights replay event-for-event |
| Phase 3 — chain bus | `primitives.json` | passing, 86 fights; decay, depth and wasted heals covered |
| Phase 4a — relic effects | `solo.json` | passing, all 50 relics alone across 150 fights |
| Phase 4b — sockets | `mixed.json` | passing, 120 fights with duplicates, sockets and awakenings |
| Phase 5a — enemy packs | `packs.json` | passing, 360 packs regenerate exactly |
| Phase 5b — progression | `corpus/progression.json` | passing, levels, perks and the reward table |
| Phase 5c — the delve loop | `delve.json` | passing, 516 runs and 13,472 steps replay exactly |
| Phase 5d — scoring | `outcomes.json` | passing, 637 finished runs across all ten halls |
| Phase 6a — duels | `duel.json` | passing, 132 duels replay event-for-event |
| Phase 6b — the versus match | `versus.json` | passing, 120 matches and 710 rounds |
| Phase 6c — synergy | `synergy.json` | passing, 1200 loadouts scored exactly |
