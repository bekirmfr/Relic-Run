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

## Four relics the port rewrote

Three relics had an awakened half that was written down and could never fire: the bazaar only
wakes what STACKS, and a Merchant's Thumb, a Second Stomach and a Hollow Idol do not. Mutating
those branches proved it — nothing could kill them, because nothing could reach them. Rather
than delete rules the game clearly wanted, each was given a way to happen.

### The flag that was asked two questions

The source's `stack` decides both whether a draft may offer a second copy AND whether the bazaar
may wake one. Those are different questions, and the Merchant's Thumb is the case that forces
them apart: it is worth exactly one copy — a second discounts nothing further — and yet its
awakened half buys a second deal at the very counter that could never have sold it.

So `RunRules` now answers them separately. `Stacks` is the draft's question and `Wakeable` is the
bazaar's; unstated, the second follows the first, which is the source's behaviour. The Thumb is
the only relic where they differ, and the discount is half rather than a fifth, because one relic
slot spent entirely on prices has to be worth the slot.

### What the other three became

- **A Second Stomach stacks, and each copy doubles the breather.** Awakened, a breather that
  would overheal is not wasted: the surplus stretches the pool instead, a point per copy held,
  and the breather then fills what it just made. That fixes the relic getting *weaker* the more
  you invest in it — doubling nothing is nothing — and it revives, in a form that can fire, the
  flat point of max HP the source granted at every gate and could never reach.
- **A Hollow Idol stacks.** Awakened, it stops hedging: instead of counting once toward all
  eight families, it throws three more behind the one the delver leans on, which is usually the
  difference between a tier and the next one up. Ties go to whichever family comes first in the
  enum — arbitrary, but it must not depend on the order relics were drafted in.

The Idol's rule lives in `SetCounts` and is read by BOTH the fight and the stat ledger, on
purpose: a set tier the fight granted and the sheet did not show would be a lie on the character
sheet. The source has exactly that lie — its engine credits an awakened Idol twice and its
ledger credits it once — and `CombatRules.DelveAsRecorded` is what keeps the corpus replaying it.

### Where the gates could not follow

A corpus can only ever pin the answer that was recorded, so every one of these is asked directly
as well. Two needed the code opened up before they could be asked at all:

- **`DelveRun.Rest`** was pulled out of the gate. The order is the whole rule — the room is read
  before the stretch, or the stretch measures the space it just made and every gate grows the
  pool — and a private method walked only by whole runs could not be asked about ordering.
- **`HollowIdolTests`** reads the Flesh set, because it announces itself: reaching three of a
  kind thickens the hero and says so in the log, so whether a tier was reached is visible in the
  event stream rather than inferred from a total. One hand proves a woken Idol OPENS a set the
  hand could not reach; another proves it backs one family rather than all of them, which is the
  only thing separating the shipped rule from the source's.

Twenty-four mutations of the four, all twenty-four die.

### One flag, one mode

Whether a relic stacks, and whether the bazaar will wake one, are facts about the RELIC. They
were mode-dependent for a while by accident: a duel read them off the generated catalog while a
delve read them off `RunRules`, so a Debt of Flesh stacked on one side of the game and not the
other. `VersusMatch` carries the same rules object a run does now, and
`ARelicStacksTheSameWayInBothModes` walks all fifty relics through both.

The versus offer had also grown its own copy of the delve's availability filter — the same rule
written twice, which is how the two drifted apart in the first place. It calls
`RelicDraft.Available` now.

What a relic DOES in a duel is a different question, and the source answers it deliberately:
twelve relics are tuned per mode in `RelicTuning`, for three reasons. A duel has no dungeon
economy, so the relics that swell loot or collect on a kill have nothing to work with. A duel
runs twenty rounds against thirteen floors, so what compounds gets throttled or allowed to climb
further. And a duel has a symmetric opponent, which is most of why a Famine Bell is worth
carrying there. That is design, and it stays.

### Still to do

The relic descriptions are extracted from the source and still describe the source: the Thumb's
says twenty per cent, the Stomach's says nothing about stacking. Nothing reads them yet, so
nothing is wrong today — but they are display text, and display text that contradicts the rules
is worse than none. They want an override layer, which lands with localisation.

## The palette, which is the one part of Phase 8 that is Core

A hero is drawn as a stack of images whose pixels are not colours but ROLE KEYS: one letter
meaning "hair", or "the dark side of the outfit". A palette turns keys into colours and a shader
looks them up, which is what lets the Changing Room recolour a delver live without touching a
single sprite. Twenty-six families give three keys each, and seven fixed colours — white, black
and five greys — are never recoloured at all.

A family states one colour and derives the other two through HSL and back: the dark side has its
lightness multiplied down, its hue rotated a little and its saturation pushed up; the light side
is the same in reverse. That is float arithmetic landing on a byte, so it is gated rather than
trusted — nearly always the rounding absorbs any difference between two languages, and
occasionally it does not. Rounding goes through `JsMath`, because a value landing exactly on a
half is the one place the two disagree by default.

The multipliers are recorded as THOUSANDTHS, so `palette.json` holds no float literals — the rule
everywhere else in the corpus, and the reason none of it has ever diverged on a parsed decimal.
Both sides divide by a thousand, which is exact.

The family table itself is extracted like any other content, and `HeroPalette.cs` is generated
from it, so the twenty-six families and their role keys are never typed by hand. `validate.mjs`
checks the one thing that would be silent and fatal: role keys must be UNIQUE across the whole
palette, because a pixel carries a key and nothing else says which family it belonged to.

### A gate that agreed with the port by construction

The first version of this recorder walked the family table and applied the shades ITSELF, then
compared the port against that. It passed. It was also worthless: the recorder and the port were
two implementations of the same idea, so of course they agreed, and neither of them was the
source. The real builder adds nine keys on top of the families and the fixed colours — a dark
default, the silhouette outline, and seven legacy aliases mapping tokens in older art onto the
roles that replaced them — and the port had none of them. Pixels drawn with an old token would
have come out unpainted, silently.

The corpus records `paletteFor` now, which is the source's own builder, and the port is ninety-
four keys rather than eighty-five. **A recorder that re-implements what it is recording is not a
gate**, and the only reliable tell is asking what the recorder would have to get wrong for the
test to fail.

Aliases resolve against the FINISHED palette rather than against a family's default, so a delver
who picks an eye colour gets it on the old tokens too.

### Three mutations that were equivalent, and what replaced them

Twenty-five mutations, all twenty-five die — but three had to be rewritten first, and each was
equivalent for a reason worth knowing:

- **Not clamping lightness** changes nothing, because whenever lightness exceeds one both ends of
  the hue ramp exceed one too, and every channel clamps to white either way.
- **Not wrapping a red hue below green** changes nothing, because the conversion back wraps the
  hue anyway: −x/6 and (6−x)/6 are the same angle.
- **Skipping the grey shortcut** changes nothing, because at zero saturation the hue ramp is flat
  and returns the lightness on every channel — which is what the shortcut returns.

All three are still worth writing the way the source writes them, so each was replaced with a
neighbouring mutation that is observable: half a turn instead of a full one, a grey threshold
instead of an exact zero, and a lightness that is halved rather than merely unclamped.

## Composing a delver

A hero is not a sprite. It is a stack of twelve layers — a backdrop, a body, and ten wardrobe
slots — each drawn as a 42-square grid whose characters are role keys. Composing one means
stamping the stack in paint order, resolving the shadow and highlight modifiers against whatever
is already beneath them, outlining the finished silhouette, and laying the lot over the backdrop.

**This is why the port plan's "stack twelve sprites and let a shader colour them" does not work.**
Two of the role keys are not colours at all: a shadow means "whatever is under this, one tone
darker", so what a pixel finally is depends on what was stamped before it. The outline is the
same shape of problem — it traces the finished silhouette, which nothing knows until the stack is
complete. Neither can be baked into a sprite ahead of time. So the composition happens at
runtime, into a 42×42 grid of indices, and the shader colours that instead. Seventeen hundred
pixels is nothing to compose; the sprite-stacking was an optimisation for a cost that is not
there.

### Comparing a pixel on its meaning

The source names each toned pixel with a synthetic character, handed out in the order the tones
are first met. Those characters are an artefact of the order it happened to compose in — a port
that composed differently would pick different ones and still be right — so they are not
reproduced. Each recorded frame carries a table saying what its characters MEAN, and the port is
compared on that: this pixel is the outfit's dark side, one tone darker again.

### Where the recording could not reach

483 frames and some 852,000 pixels replay exactly, and five mutations still survived it. Three
were things the shipped art simply never does, and one was a rule I had invented:

- **Tones stacking.** Nothing in eleven thousand recorded rows lays one modifier over another on
  a real pixel, so the corpus cannot say whether tones stack or only the last survives.
- **A blank space.** The grids use a full stop for nothing; the format allows a space and the
  pack never uses one. Treating a space as paint would let a garment punch its own silhouette
  out of the body.
- **A short row.** Every row in the pack is already the full width, so the padding never fires.
- **Slot defaults**, which the compositor should never have consulted. The pack carries a default
  for some slots and it belongs to whoever assembles an outfit — the Changing Room applies it, a
  lobby rolling a rival's appearance does not. Applying it at composition time would have put
  trousers on delvers drawn deliberately without them. That one was not a gap in the corpus; it
  was a rule I added that the source does not have, and only mutation found it.

Each is asked directly now, and all twenty mutations die.

### One number that is easy to read off the wrong table

A pack declares its own animation states, and the game does not animate from them. The REGISTRY
decides how long a state runs and how many frames it starts from; the pack's copy exists so a
part claiming more frames than its state allows can be rejected as malformed. Reading the pack's
numbers instead changes every animation in the game and looks, at a glance, like the more obvious
thing to do — the corpus caught it on the first run, because a state the registry does not have
at all falls back to idle and the frame counts stopped matching.

## What a delver reads

Resolving a string is a lookup, a fallback to English, a fallback to the KEY itself, placeholder
substitution, and then a clean-up. That last step is the reason this is recorded rather than
assumed: it strips emoji, arrows, technical symbols and dingbats, collapses runs of spaces, and
trims. **"← Back" reaches the screen as "Back"** — a hundred and forty-four of the twelve hundred
shipped strings change on their way out, because the game writes its buttons with icons in front
of them and none of those icons is ever drawn.

Twelve hundred strings across eight languages replay exactly, along with the filled placeholders,
the awkward cases, and a set of probes sitting either side of every boundary in the stripped
ranges. The probes never appear in the game; they are there so the ranges are pinned rather than
inferred from whichever emoji the translators happened to reach for.

Two bugs came out of the first run, both in the clean-up, and both from the same mistake: I had
written it as one pass that stripped, counted spaces and rebuilt in a single walk, and the index
juggling was wrong in two directions at once — an arrow survived, and a character above the basic
plane came out mangled. The source does three passes. So does the port now.

Values are passed as strings on purpose. How a number is spelled depends on a culture, and
choosing one is the caller's business; Core has no opinion and no way to have one.

### What the shipped strings cannot say

Eighteen mutations, all eighteen die — after four were replaced or covered directly. Three were
rules the shipped data never exercises: **no string repeats a placeholder** in any of the eight
languages, and **none contains a tab**. Both are ordinary things for a translator to write, so
each is asked directly. The fourth was a mutant that could not fail — it swapped a null check
that the corpus only ever fed empty strings — and was rewritten to test the case that exists.

### Two more footguns in the harness

`mutate.py` decoded test output using the console's own codepage. That was fine until a failing
test printed the strings it compared, in Arabic and Japanese, and the reader thread died mid-suite
with a decoding error rather than a test result. It reads UTF-8 now.

It also gained a way to stage single files. The tests read the shipped hero pack, which sits in a
directory of PNGs that nothing here touches and that would have been copied for every mutant.

## What a run leaves behind

Scoring says what a run was WORTH; banking is what then happens to it, and the two are apart
because a sandbox run is scored and deliberately not banked — the number is shown and nothing
persists. `RunOutcome` does the first, `RunBanking` the second, and both halves come out of the
same recorded call to `finishRun`, so `outcomes.json` carries the whole save store either side
of it.

The source spreads that store across a dozen keys read and written from wherever needs them.
`SaveState` gathers the ones that are progression into one object; settings, the wardrobe and
the sprite studio also live in that store and are deliberately NOT there, because nothing in
Core reads them and they belong with the systems that own them.

Three things needed a case built on purpose, because the obvious ones cannot reach them:

- **A best is beaten, not matched.** Only a run scoring exactly the standing best separates the
  two, and the score is not known in advance — so each `tie/*` case is scored once against an
  empty save and then run again against a save holding that very number. Even that was not
  enough on its own: writing a number that is already there changes nothing, so the save cannot
  say afterwards whether the run beat the best or tied it. `Bank` reports it instead, which is
  also what the end screen needs in order to say "new best".
- **Which end of the board the eleventh row falls off.** A board has to be already ten deep
  before a trim can be wrong, so the boards in the corpus run from empty to overfull.
- **A backdrop is remembered once.** Which needs a save that has already seen it.

Achievements are shipped rules rather than presentation: what a delver has earned is a fact
about their save, and a screen that worked it out would be a second copy of the rule. Each of
the fourteen is asked of saves sitting exactly on its threshold, which is the only place a
greater-than and a greater-or-equal disagree.

### The day, and the zone

The Daily Delve's seed is the UTC date read as a number — 20260907 — so everybody runs the same
dungeon at the same moment, and the day turns over at one instant worldwide rather than at each
player's midnight. Recorded across month ends, year ends and a leap day, which is where a date
routine written by hand goes wrong.

The port takes a `DateTimeOffset` rather than a `DateTime`, and REFUSES a `DateTime` that does
not say it is UTC. That is not fussiness: converting an unlabelled time silently means reading
the machine's own zone, two delvers on the same day getting different runs, and — worse for the
tests — a bug no test could catch without itself depending on the zone it runs in. A mutation
that removed the conversion survived everything until the ambiguity was refused at the door
instead; now the mutation that removes the refusal dies.

Twenty-two mutations of the save layer, all twenty-two die.

## A career, which is where the seams are

Every piece of the game is gated exactly by a corpus of its own. What no corpus covers is the
JOINS between them, and until now nothing joined them: a screen wanting to start a delve would
have had to know that the setup comes from the level, that the hall comes from the tier, that the
tier is bounded by what has been unlocked, and that finishing means scoring and THEN banking.
Four screens would have known it four times.

`Career` is that joining up and nothing more. It starts nothing and plays nothing — the run loop
asks a player questions through an interface, so how a UI drives it is a question for whoever
builds the UI, and deliberately not answered in Core.

One join is a genuine trap. Scoring reads the save to decide how relevant the hall still is, and
banking then moves the frontier — so banking first would score a run against the hall it had just
unlocked and quietly pay a fifth less for the best run a delver has ever had.
`ClearingTheFrontierIsScoredAtTheFrontiersRate` is what holds the order.

`ACareerHoldsTogetherAcrossManyRuns` plays sixty delves for one save and asserts INVARIANTS
rather than numbers: the wallet never falls of its own accord, a hall never closes, the frontier
never jumps two, the board never outgrows ten or falls out of order, the level moves only by as
many levels as the run said it crossed. A test that pinned the exact gold after sixty runs would
fail on any balance change and teach nobody anything; these keep meaning what they say.

They would also hold vacuously on a career that went nowhere, so the test ends by insisting one
happened: a level bought, the frontier moved, and the board full enough to have dropped a row. As
written it reaches level twelve and six halls.

Fifteen mutations of the joins, all fifteen die. Two of them survived at first for a reason worth
recording: the tests had been written in terms of `Career.DailyOpensAt` rather than the number
three, so moving the constant moved the test with it. A test phrased in terms of the thing it is
testing cannot see that thing change.

## The versus lobby

A match used to be handed a finished lobby the way a fight is handed a finished pack, because
making one spends most of its randomness on the rivals' FACES — an outfit slot by slot, a colour
per family, an accent and a motto, about two hundred and fifty draws in all — and the port has
no wardrobe.

It still has none. But nothing reads a face back: the draws are spent and the results discarded,
so a port can spend them without choosing anything. `HeroWardrobe` holds only the counts, and
everything a lobby actually produces now comes out of `VersusRun.Make` — seven delvers with a
level within one of the hero's, an opening kit of three apiece, a hall each (deeper for a
stronger rival, which is the lobby telegraphing danger before a blow is struck), a statline each,
and the offer the hero opens on.

Two things about setting one up are easy to miss and are gated:

- **A versus match starts a delve first.** `startVersus` calls `startRun`, which deals an offer
  from the DELVE pool, and then re-deals from the versus pool once the mode has changed. The
  first offer is thrown away, but it was paid for — and how many relics it held depends on the
  hero's level, because the level-ten perk adds a third choice.
- **The draw count is compared.** It is the one number that notices a wardrobe slot or a colour
  family miscounted in a way the rosters happen to survive.

### Duels in a lobby

The designed duel tier fights two hand-built sides on a level field, which is the right shape
for asking what one relic does. It is not the shape a player meets. By the eighth round of a
match both delvers carry nine relics and a pool three times what they opened on, and each is
still holding what its earlier duels left it. `duels.json` keeps the deepest duel of every
lobby, event for event.

It earned its place immediately. The source's duel stops answering the moment the answer kills: a
defender whose Thorn Vest finishes the striker never reaches the adrenaline, the marrow or the
mirror behind it. A delve works through the whole ladder regardless. Nothing separates the two
unless the defender is carrying a Thorn Vest AND something later in the ladder while the striker
is low enough to die to it — which the designed tier had never assembled, and which a round-four
duel between two delvers with six relics each did.

Versus takes the delve's answer, as it does for the seven. The blow LANDED — that is what
provoked the thorns — so an Adrenaline Gland and a Troll Marrow have already earned their
trigger, and whether the counter happened to be lethal is nothing to do with either.
`CombatRules.ReactionsStopWhenTheStrikerFalls` is the seam and the corpus replays it.

That one could not join `EveryAdoptedRuleChangesADuel`, and the reason is worth recording: under
the SHIPPED rules no recorded duel reaches the moment at all, because the other seven adopted
rules move every one of them off it first. A rule can be real, reachable in play, and still
invisible to a corpus recorded before it existed. It is asked directly instead, by one exchange
built to be fatal with a marrow live behind the thorns.

A lobby duel has no seed of its own — it is fought from the match's generator, part way through
— so the recording carries the match's seed and how far the stream had got, and a replay skips
to the same place.

### The mutations

Sixteen mutations of the lobby and the last word, all sixteen die.

One of them had to be rewritten to earn that. What an unlisted relic is worth to a rival's
opening draft only matters against the listed values, and every listed value is higher, so
moving the default from three to nought changes no pick — an equivalent mutant, and a mutant
that cannot fail is not a test. Moving it to ten, above everything the bot wants, does change
picks, and that is the mutant the suite carries.

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

## A footgun in the mutation harness

A mutant is reported as `NO ANCHOR` when its search text is not in the file, which normally means
the code moved and the mutant needs updating. It also happened to mean something else for a
while: generated files are written with CRLF and hand-written ones with LF, so any anchor
spanning more than one line matched in some files and silently missed in others.

`mutate.py` normalises line endings in its copy now. The tell was a mutant that looked
well-formed, matched when checked by hand, and reported `NO ANCHOR` anyway.

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

## The one gate with no recording behind it

Everything else here is a port, so everything else is gated by replaying recorded JavaScript.
The binding of content ids to Unity assets is not: nothing in the browser game ever bound a
relic to a `Sprite`, so there is nothing to record and the rule has to be *stated*.

It is stated in Core, in `BindingAudit`, and it says one thing — every id the catalogs will ask
for is bound exactly once, to something, and nothing is bound that the game will never ask for.
That splits cleanly across the two runners. The arithmetic over two lists of strings is
ordinary C# and runs under `dotnet test`; only the lists themselves need the Editor, because
only Unity can say whether a `Sprite` reference resolves. So a wrong answer about missing art is
a second away rather than a domain reload away.

The excuse list is the part worth being careful about. One relic — Debt of Flesh — was never
drawn an icon and falls back to a procedural glyph, so the audit has to be able to forgive it.
It is forgiven from `ContentIds.RelicsWithoutIcons`, which is read off the *generated* sheet
table rather than typed by hand: nobody can quiet a genuinely missing icon by adding a name to
a list, and drawing the missing icon retires the exception by itself. An excuse that has stopped
being true — the art arrived, or the relic was cut — is a failure of its own.

Two constants in that layer have no witness inside the project, because every number about the
art is generated from the same tables and so agrees with itself however wrong it is. The sheets
themselves are the outside witness: `TheSheetsAreTheSizeTheTablesSayTheyAre` reads the width and
height out of `relic-icons.png` and `enemies-hoard.png` and multiplies the tables out to see
whether they match. Without it, a sheet re-exported one column wider slices every icon after the
first slightly off — and reads as an art mistake rather than an arithmetic one.

The Editor half lives in `Assets/GameAssets/EditorTests` — **not** under `Assets/GameAssets/Tests`,
which the `dotnet test` project compiles wholesale. Anything under `Tests/` has to run under both
runners; anything that reads the project's own `.asset` files through `UnityEditor` cannot, so it
lives next door and runs from the Test Runner only.

What it checks is what the audit cannot. An audit can say a relic is bound to *something*; it
would pass just as happily with every icon off by one row, and the port would ship with the wrong
picture on all fifty relics. So four cells on each sheet are pinned to coordinates worked out by
hand from the source's own `RELIC_ICON` table — deliberately not recomputed from `RelicArt`,
because the importer flips the row the same way and a flip in the wrong direction would satisfy
both. Alongside them: no two ids share a cell, none is off the grid, and every texture is still
point-filtered and uncompressed, all of which are settings a person can change in the Inspector
in two seconds and not notice for a week.

## Re-encoding without a recording

`HeroIndex` is not a port. The source drew straight to CSS box-shadows and never built an index
grid, so there is nothing to diff against — but there is a recording of what every composition
*contains*, and a lossless re-encoding has to survive a round trip through it. That is the gate:
index all ninety-three recorded heroes, read every pixel back, and demand the pixel that went in.

The two ways it can go wrong are both quiet. Two different pixels sharing an index paints a
shadow in the colour of the thing beside it, which reads as a shading mistake. A table running
past a byte wraps the two hundred and fifty-seventh colour onto the first one — transparent —
and puts a hole in a delver. The busiest outfit the shipped wardrobe can assemble needs 55 of
256, and the test says the number rather than only bounding it, so a change that took it to 200
is noticed before one takes it to 260.

That busiest outfit — `full/3/idle` — has no transparent pixel anywhere in it, its backdrop
covering the whole frame. That is why index zero is RESERVED rather than allocated: numbering
from what a hero happens to contain would give that one delver a different meaning for zero
than every other, and a renderer reading a grid has nowhere to ask.

One thing the corpus cannot reach: **the deepest tone stack in the entire shipped wardrobe is
one.** Not a single pixel in any of the ninety-three compositions is toned twice, so a table
keyed on a pixel's first tone alone passes every corpus test there is — mutation found exactly
that, and nothing else would have. The compositor stacks tones without limit and lays each over
the last as transparent paint, so a doubly shadowed pixel really is darker than a singly shadowed
one; the property is stated directly instead, by hand, because the day a part is drawn with a
fold in it is the day it starts to matter. Order matters too, but only across families: two
shadows in either order mix to the same colour, a shadow under a highlight does not.

The Editor half asks what happened to the bytes on the way into a texture, which needs Unity.
Two mistakes there are invisible to anybody who does not already know what a delver looks like:
the row flip, because Core counts rows from the top as the art was drawn and a texture counts
from the bottom; and the colour space, because an sRGB grid would gamma-convert its own indices.
The flip is checked twice, once by walking the writer's own arithmetic and once by a hero built
with its top two rows deliberately bare — the second is the one that would catch a flip in the
wrong direction, since the first would agree with it.

## Five languages that borrow their fonts

The port ships two faces and eight languages, and those two facts are in tension. Measured
after `Locale.Clean` has taken the icons off the buttons:

| | en | es | fr | tr | ru | ar | ja | zh |
|---|---|---|---|---|---|---|---|---|
| Silkscreen | 100% | 100% | 100% | **94%** | 48% | 52% | 12% | 9% |
| Space Grotesk | 100% | 100% | 100% | 100% | 48% | 52% | 12% | 9% |

Turkish at 94% is the worst number on that table — not the lowest, the worst. A figure that high
survives a spot check and still puts a hole in a word, and what Silkscreen is missing is the
dotless ı and the breve, which Turkish uses constantly.

These numbers changed once, and the change looked like a regression. The first pass bundled Press
Start 2P and Jacquard 12 — faces that appear in the source **nowhere** — and Press Start 2P
happened to carry Cyrillic. Swapping to the faces the source actually asks Google Fonts for gave
the real answer: Russian borrows too. The shipped game has exactly the same hole and falls
through to `monospace` for it.

Neither face has a single Japanese or Chinese glyph in it, and both are missing half of Arabic.
The choice was between bundling ten to sixteen megabytes of CJK for three languages and
borrowing the reader's own fonts; **borrowing won**. `FontBook.Fallbacks` holds a chain of
`DynamicOS` font assets — a family name and no glyphs at all — and the device rasterises what it
needs, so a delver reading Japanese sees their platform's Japanese face beside a pixel face from 2001.

That is a compromise somebody chose, and it has a limit worth naming. A fallback resolves by
family name **on the device**, and the importer can only create one for a family the machine it
runs on can see. A build made on Windows carries Yu Gothic, Microsoft YaHei and Segoe UI; a
phone that has none of those falls through to boxes again. The list is serialized rather than
computed precisely so a platform's own families can be added by hand.

The measurement stays asserted on both sides. `LegibilityTests` still says neither TTF can draw
those three, because that is a fact about the files and the reason the chain exists at all.
`FontAssetTests` says the chain is there, is on both faces, and borrows rather than bundles —
and deliberately does not pretend to know what is installed on somebody's phone.

`Tools/capture/fonts.mjs` records the faces' own character maps out of the TTF and nothing else.
What a language *needs* is decided in C#, from the locale tables and `Locale.Clean` — recording
the intersection would have made the answer agree with the question, which is the mistake the
palette recorder made once already.

Two things the shipped data cannot exercise, both found by mutation and both stated by hand
instead. A character above the basic plane is one character and not two halves: the tables hold
five of them and every one is an emoji the cleaning removes, so a version that read surrogate
pairs as two separate characters passed every other test. And the thirteen English characters no
pixel font has ever had are all arrows and pictograms that never reach the screen — counting them
would make every face fail for a reason nobody could act on.

## Held or fetched

Addressables is worth using here for two of the four kinds of art and not for the other two, and
the numbers say which. Uncompressed:

| | files | resident if held | wanted at once |
|---|---|---|---|
| halls | 10 | 4.5 MB | 0.45 MB — a run descends one |
| events | 12 | 2.8 MB | 0.23 MB — one is shown at a time |
| sheets | 2 | 1.3 MB | 1.3 MB — every screen draws from both |
| locales | 8 | 59 KB | 7 KB |

The trap is that addressing an asset changes nothing on its own. A direct `Sprite` reference is
loaded when the thing holding it is loaded, so a book of ten halls holding direct references puts
all four and a half megabytes in memory however carefully the groups are arranged. That is why
there are two book types rather than one flag: `SpriteBook` holds sprites and is resident,
`AddressBook` holds `AssetReference` — a GUID and nothing else — and the hall arrives when the
delver does.

The address is the content id, so a screen asks for `hall-hoard` by the same name everything else
uses. That is a convenience and a second trap: an address is a string, nothing resolves it until
run time, and a typo is invisible until somebody reaches the ninth floor and gets nothing.
`AddressedContentTests` checks the join in both directions — every id has exactly one address, no
address names an id nothing asks for, and the book's GUID is the addressed asset's GUID. It also
asserts that the two sheets are **not** addressed, so the split stays a decision rather than
becoming a drift.

## Pacing, which had to be read rather than recorded

The playback timings live inside a React component's methods rather than in a named declaration
`lift.mjs` can slice, so there is nothing to record — and a recording would anyway be a record of
how fast one machine ran on one afternoon. They are ported by hand from `evDelay` and the setup
around it, and gated by stating the rules instead.

The rule worth knowing: **playback follows the fight's own clock.** Every event carries the ATB
tick it happened on, and the wait between two events is how many ticks passed times a few hundred
milliseconds — floored at 45% of a beat so a burst still registers, capped at 2600ms so a slow
exchange is not an intermission. It is not one event per beat. That is the difference between a
flurry reading as a flurry and a fight reading as a metronome.

Two things fell out of reading it closely.

`Math.round(_step * 0.45)` is a **JsMath case with a live example**. At ×4 on a short fight the
beat is 250ms, 45% of it is 112.5 exactly, JavaScript rounds a half up to 113 and .NET rounds a
half to even and says 112. One millisecond, in the one place the arithmetic lands on a half.

And the speed control **only ever shortened half of what it should have.** The source divides both
the beat and the per-tick figure by the speed when a fight opens, then the button recomputes only
the beat — so pressing it mid-fight tightened the gaps between events on the same tick and changed
nothing about the long waits, which are most of what a delver pressing it wants skipped. The same
fight at the same speed then played at two different paces depending on whether the button had
been touched. That reads as a slip rather than a decision, so `PacingRules.Shipped()` fixes it and
`AsRecorded()` keeps it, and the diff between them is the change.

One rule is unobservable and ported as written: the source floors the beat at 30ms when the speed
CONTROL recomputes it and not when a fight opens. Applied in both places here, because it cannot
be reached in either — a fight opens at 700 or 1000, and a quarter of either is six times the
floor.

Mutation earned its keep on this one. Five of eighteen survived the first run and none of the five
was a missing test in the ordinary sense.

Two said a guard was **redundant**: `Between` opened with `if (toTick <= fromTick) return shortest`
and the floor two lines below already answered both cases — a zero gap multiplies to zero and a
backwards one to a negative, and both are under the shortest gap. Deleting the guard made the
floor load-bearing and killed both mutants at once. Nothing was added to the tests.

Two said the reduced-motion **beat and clock were never read**, because the waits answer before
they get that far. They are public, so a caller asking a reduced-motion fight how long a beat is
should be told forty rather than a thousand; asserted rather than deleted.

And one said the shipped numbers cannot tell the two roundings apart. 87.5 goes to 88 whichever
way you round, because .NET rounds a half to the nearest EVEN and 88 is even — so the only live
disagreement in the whole table is `ShortestMs`. The numbers are authored though, and a beat of
450 at ×4 lands on 112.5 where they differ, so that case is asserted with numbers nobody has
chosen yet.

## What the timer dance was hiding

The source shows a fight as a chain of `setTimeout` callbacks, and two thirds of that machinery
exists because a browser timer cannot be recalled once set. Pausing re-schedules a poll every
180ms until the delver comes back. Starting a fresh fight bumps `_ptk` so the old chain notices
it has been superseded and returns quietly. Neither is a rule about how a fight is shown; both
are scaffolding for a language problem.

A `CancellationToken` removes both, and what is left is small enough to state and test:

- **`CombatPlayback`** — a cursor with no timers in it at all. It says *show this, then hold for
  that long*, and whoever drives it does the waiting.
- **`PlaybackLoop`** — the loop, with the clock injected. A test clock answers instantly and
  writes down what it was asked for, which turns "does pausing work" from a question about
  timing into a question about a list.
- **`CombatPlaybackController`** (Game) — the two things Core cannot have: a clock made of
  frames, and the token that ends a fight nobody is watching.

Three details that are rules rather than scaffolding, and are asserted as such.

**A foe entering is announced twice.** The first time the cursor reaches an `Enter`, playback
walks the hall and does *not* advance; the second time it shows the event. That is why the source
keeps a `_walked` index — the walk is what puts the cursor back, so without a memory of having
walked, the same foe would be approached forever.

**The pause gate is checked before the next step, not after the last one.** That is the
difference between a delver who pauses to read a card finding it still on screen and finding the
next thing already drawn over it.

**The speed control takes effect from the step after the press.** A step's hold is worked out
when the step is produced, so the beat being watched when the button goes down plays out at the
length it was given. Cutting the current wait short would let a press land in the middle of a
2600ms gap and skip most of it, which reads as the button having skipped an event.

## Half a log in English

`CombatLog` is the view-model for the combat log, and the fairest test of invariant 5: an event
carries its own snapshot, so the log needs no memory of the fight. `TheLogRemembersNothingAboutTheFight`
reads a recorded fight forwards, then reads it again backwards out of a fresh log, and demands
every line come out the same.

There is nothing to diff against — the source's `lineFor` is a component method the lifter cannot
slice, and the corpus records events rather than the words they become. So the gates are the ones
that can be stated: every event kind says *something*, every localisation key it asks for exists
in `en.json`, no line comes out as its own key, and every event of a recorded fight survives being
turned into a line.

**Nine of the twenty event kinds never reach the locale at all**, and being hit is worse than
untranslated rather than better: an ordinary blow is localised, a critical one and a foe's thorns
are not, so the same event reads in two languages depending on how hard it landed. That is the
source's own arrangement, carried across rather than quietly fixed — inventing eight translations
is not a porting decision — and `CombatLog.English` names them so whoever does that work has the
list.

The test for it was written the obvious way first and failed, which is the interesting part. A
whole-line comparison against a locale that reverses its strings came back with the foe's name
reversed and the sentence around it in English: **the bestiary IS translated, so a delver reading
Japanese sees a Japanese rat sidestepping in English.** Half-and-half is worse than neither, and
only a fragment-level assertion could see it.

## A mutant can hang instead of failing

`mutate.py` had no deadline, and a mutant that stops the playback cursor advancing turns every
`while not finished` loop into a forever. One run sat wedged for an hour looking exactly like a
slow machine — no output, because the harness buffers until the suite returns.

Three fixes, and it took two attempts to get the first one right.

`mutate.py` runs each suite under a five-minute deadline and **counts a timeout as a survivor**,
because a mutant that hangs the harness is a mutant nothing killed. It also kills the process
**tree**: `dotnet test` spawns a test host, and killing only the launched process left that host
spinning on the very loop that caused the timeout — one pegged core per hung mutant, quietly
making every later mutant slower. One hang turned into an afternoon of a machine that seemed
inexplicably tired.

The bound in the tests went in the wrong place first. Counting what the screen is TOLD misses a
loop spinning on a step that shows nothing; counting what the clock is ASKED misses one that
waits for nothing. The mutant that turns the loop's exit into a `continue` does both. The guard
that works is on `Paused` — asked once per turn round the loop, whatever the step turns out to
be.

## The gauges are the fight's clock made visible

`FightFrame` turns one event into everything a screen needs: the log line, the numbers flying
off, and what each of the two attack gauges should do. All of it derived from the event list and
where in it we are, so the view holds no combat state and cannot get out of step — a screen
showing event forty knows exactly what a screen jumping straight to event forty knows.

The gauges are the part worth reading twice. Each fills over **exactly the time until its owner
strikes again**, so the bar arriving full and the blow landing are the same moment. That is only
possible because the fight is over before any of it is drawn: the gauge looks *forward* through
events nobody has seen yet and adds up the holds a delver will actually sit through.

Three rules make it work, and each one is a way it would otherwise be wrong:

- **A relic's damage is not a swing.** It happens on the delver's turn and hurts the foe, but
  counting it would reset the delver's gauge on every chain link — the bar would tick backwards
  whenever a good loadout was doing its job.
- **A foe's thorns are not the foe attacking.** Depth zero only, for the mirror reason.
- **A gauge stops looking at the next foe's entrance.** A bar that wound up across a change of
  opponent would arrive full at somebody who was not there when it started filling.

And a unit that never strikes again does not freeze — it restarts at its last cadence. It dies
first, and a frozen bar would tell the delver so a second before the game does.

The holds are summed rather than the ticks, deliberately: the holds are what a delver waits
through, capped and floored and divided by whatever speed they chose. A gauge timed off raw ticks
would drift away from the fight the moment anybody pressed the speed control.

**The randomness stays in the view.** The source scatters each flying number so two in a row do
not overlap. A view-model that rolled dice would make the same fight look different on replay,
which is the one thing this layer exists to prevent — `TheSameEventAlwaysDrawsTheSame` says so.

## One copy, not one relic

`RelicMeter` is the relic tray's view-model, and it is where invariant 6 shows up in the
interface. A socket is bolted to an inventory SLOT, so the second Whetstone on the shelf can be
counting toward something the first is not — which is the whole reason inventory is an ordered
list with duplicates rather than a tally.

Two clocks run at once and both are shown. An Anvil Heart counts strikes toward a sharpening
while a socket on the same copy counts strikes toward something else entirely; merging them would
show one number for two clocks, and a delver watching the gauge they could see would be surprised
by the other.

Three of the source's gauge branches are for relics **the draft cannot offer** — Blood Chalice,
Glass Edge and Debtor's Chain are among the twenty-eight cut orphans. Not ported. The *shape* of
the debt gauge is kept, because one that counts up toward a reckoning is not one that counts down
toward nothing and showing either as the other would read as good news.

And silence is a rule. Six of the twenty event kinds make a noise; the rest say nothing, because
a chain of eight relics firing would otherwise be eight noises on top of one another and a delver
would learn nothing from any of them.

## A scene is where a mistake is silent

**A scene in this project is a prefab.** `Corescene.unity` is empty and stays empty; the GameLift
package's `SceneService` loads scene PREFABS from `Assets/Scenes/` by key, through a `SceneConfig`
that addresses one and an `ISceneObject` on its root. Getting that wrong is easy and quiet — a
screen whose `ISceneObject` sits one level down loads, sits there, and is never initialised or
cleared, which looks exactly like a screen that does nothing.

So `Tools ▸ Relic Run ▸ Build Fight Scene` edits `GameScene.prefab` — which already carries the
game's `LifetimeScope` and camera — rather than writing a second scene beside it. It replaces
exactly one child by name and leaves everything else alone, because a generator that tidied up
after other people would eventually tidy away something that mattered.

Same reasoning as the content importer: a hand-assembled hierarchy is one nobody can diff, nobody
can rebuild after a mistake, and nobody can describe except by opening it.

`FightSceneTests` asks of the prefab what `BindingAudit` asks of assets — everything fillable is
filled — and asks it **generically**, by walking every object reference the component has rather
than by naming them. Naming them would mean the builder and the test both had to be remembered,
and the entire reason the builder exists is that remembering fifteen things is what people are
bad at.

Two of its checks can only be answered by an asset, never by code. The canvas must scale rather
than assume a screen: left on Constant Pixel Size it looks right on the machine it was built on
and wrong on every other. And all four bars must be **Filled** images — one left on Simple
ignores `fillAmount` entirely and sits there full while the delver dies behind it.

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
| Phase 0 — corpus | `Tools/corpus/` | passing, 1789 cases replay exactly |
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
| Phase 5d — scoring and banking | `outcomes.json` | passing, 744 finished runs across all ten halls |
| Phase 5e — the save | `meta.json` | passing, 56 saves, 14 achievements, 15 days |
| Phase 5f — a career | none — invariants | passing, 60 runs to level 12 and six halls |
| Phase 6a — duels | `duel.json` | passing, 148 duels replay event-for-event |
| Phase 6b — duels in a lobby | `duels.json` | passing, 120 deep duels, 11,774 events |
| Phase 6c — the versus lobby | `versus.json` | passing, 120 lobbies made from a seed |
| Phase 6d — the versus match | `versus.json` | passing, 120 matches and 733 rounds |
| Phase 6e — synergy | `synergy.json` | passing, 1200 loadouts scored exactly |
| Phase 8a — the palette | `palette.json` | passing, 12,650 colours and four 94-key palettes |
| Phase 8b — composing a hero | `hero.json` | passing, 483 frames and ~852,000 pixels |
| Phase 8c — what a delver reads | `strings.json` | passing, 1224 strings in 8 languages |
| Phase 8d — content bindings | none — invariants | passing, 50 relics, 10 halls, 12 events, 39 foes |
| Phase 8e — the assets themselves | none — invariants | Test Runner only; audits the real `.asset` files |
| Phase 8f — a delver, indexed | `hero.json` | passing, 93 heroes round-trip pixel for pixel |
| Phase 8g — what a face can draw | `fonts.json` | passing, 2 faces × 8 languages · ja/zh/ar borrow the system's |
| Phase 8h — what is fetched, not held | none — invariants | Test Runner only; 30 addresses against the catalogs |
| Phase 9a — how long a fight takes | none — ported by hand | passing, the ATB clock drives playback |
| Phase 9b — walking a finished fight | none — invariants | passing, the stepper and the loop |
| Phase 9c — what a delver reads | none — invariants | passing, 20 event kinds · **9 lines English-only** |
| Phase 9d — what a screen draws | none — invariants | passing, the fliers and the two gauges |
| Phase 9e — what a relic has left | none — invariants | passing, two clocks and a budget per copy |
| Phase 9f — the fight scene | none — invariants | Test Runner only; every reference wired |
