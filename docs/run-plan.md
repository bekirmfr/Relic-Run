# The run layer — how a delve becomes a screen

The fight scene fights what it is ordered to and hands back a result. What it is not is a RUN: there
is no draft before it, no gate after it, and no floor two. This is the plan for the rest of it.

Read `docs/port-plan.md` for the port's invariants and `docs/testing.md` for what is gated and how.

---

## 1. Where we actually are

Three separate things exist and only one of them is joined up.

**The engine is finished and gated.** `DelveRun.Resolve` walks a whole run — draft, fight, event,
gate, bazaar, revive, cash-out — and `Tools/corpus/delve.json` holds 516 recorded runs and 13,472
steps that replay event for event. Every piece a stage needs is public and tested: `RelicDraft`,
`DungeonEvents`, `Pickup`, `EnemyPackGenerator`, `RunOutcome`, `Progression`.

**The fight screen is one stage of nine.** The source's IN RUN section is not a screen, it is a
stage machine: a floor rail across the top and then `draft`, `combat`, `event`, `shop`, `merchant`,
`travel`, `decide`, `revive`. The port has `combat`, and not all of it — the delver has no sprite at
all (`CombatView` has no `_heroArt` field, so the hero compositor that round-trips 93 delvers pixel
for pixel is unused by the fight), and there is no queue, no intro banner, no pause.

**Nothing a fight produces reaches the save.** `FightOrder.Last` is written by `FightScene` and read
by nobody. No XP, no gold banked, no run counted, no species marked seen — a delver's profile is
identical before and after.

---

## 2. The one constraint

`DelveRun.Resolve` is the most heavily gated code in the project. Whatever drives a run
interactively has to produce, for the same seed and the same decisions, exactly what it produces —
or the corpus stops meaning anything.

That is not a reason to leave it alone. It is the reason changing it is SAFE: a mechanical
restructure with 516 recorded runs as the oracle is the cheapest kind of risky change. What is
dangerous is writing something NEW beside it.

---

## 3. The problem

`Resolve` is a pull model. It runs a `while (true)` loop and calls the player when it wants an
answer:

```csharp
RelicId pick = choices.Draft(run, offer);
if (choices.CashOut(run, run.Floor)) { … }
BazaarDeal deal = choices.Bazaar(run, offer, awakenable);
```

A screen cannot answer that. `IRunChoices.Draft` must return a relic before the method returns, and
the player's answer arrives seconds later on a different frame. Everything below is a way of turning
that call inside out.

---

## 4. Four ways, and what each costs

### A. Resolve up front, then replay the decisions

Decide the whole run before the player sees it and play it back. Dead on arrival: the player MAKES
the decisions. It is how the Balance Lab works and it is why the Balance Lab is not a game.

### B. Run the engine on a worker thread and block the choices

`Resolve` runs off the main thread; `IRunChoices.Draft` posts a question to the UI and parks on a
semaphore until it is answered.

*For:* the gated code is untouched — the shipped path IS the recorded path, by construction.
Core is genuinely thread-safe for this: the assembly has no reference to `UnityEngine` (enforced by
the asmdef and `Tools/check/asmrefs.py`), and the only mutable statics anywhere in it are the four
lazily-built id lists in `ContentIds`, which are idempotent.

*Against:* threads in a Unity game, and an abandoned run means unparking a blocked thread and
propagating a cancellation out through the engine. A mistake shows up as a hang — and this project
has now paid twice for how expensive an unexplained stall is to diagnose here. It also forecloses
WebGL permanently, which is the platform the source game runs on.

### C. Turn `Resolve` into an iterator

`Resolve` becomes an iterator that yields at each decision point; the caller answers and pumps it.
`yield` cannot cross a method boundary, so `Draft`, `Gap`, `Bazaar`, `Fight` and `Revive` each
become `IEnumerable<Ask>` and the parent re-yields them.

*For:* single-threaded, no deadlocks, WebGL-safe, and the UI drives the clock — which solves a
problem the thread model has to work around, that combat playback takes twelve seconds and the
engine must not run ahead. It also makes a run RESUMABLE, which the source cannot do: close the tab
mid-delve and the run is gone.

*Against:* it restructures roughly 300 lines of the most carefully commented code in the project.

### D. Write a driver beside `Resolve`

Sequence the public pieces from the Game layer: roll the offer, ask, apply, build the pack, fight,
gap, gate, bazaar.

*Against:* two spellings of one procedure, which is precisely what `Bout` was built to remove, and
the drift would be subtle. Order is load-bearing here — *"the pack for the floor AHEAD is rolled the
moment a fight ends"* — and getting it wrong makes every run diverge while every test still passes,
because the tests gate `Resolve` and not the driver.

---

## 5. Recommendation: C, kept behind the door `Resolve` already is

The iterator becomes the implementation, and `Resolve` becomes a thin batch driver over it:

```csharp
public sealed class Delve
{
    public RunState State { get; }
    public Ask Pending { get; }        // null once it has finished
    public RunEnding Ending { get; }
    public bool Finished { get; }

    public void Answer();              // applies Pending's answer, advances to the next Ask
}

// unchanged signature, unchanged behaviour — now three lines over Delve
public static RunState Resolve(uint seed, RunSetup setup, IRunChoices choices,
    IRunObserver observer = null, RunRules rules = null, CombatRules combat = null)
```

One implementation, two front doors. The corpus keeps gating the shared core rather than a copy of
it, `CareerTests` / `RunReviveTests` / `RunRuleTests` compile and pass untouched, and the screen
drives the same engine the recordings were made from.

The asks are a small closed set, each carrying what the screen needs to draw and a slot for the
answer:

| Ask | Carries | Answer |
|---|---|---|
| `Draft` | the offer | which relic |
| `Reroll` | the offer, the price | yes or no |
| `Event` | the event and its choices | which choice |
| `Bazaar` | the wares, which copies can be woken | one deal, or walk |
| `Revive` | the floor | yes or no |
| `CashOut` | the floor | yes or no |
| `Fight` | the pack and the finished event list | nothing — a pump point, so playback owns the clock |

`Fight` answering nothing is the important one. The engine resolves the floor and then STOPS,
holding a finished event list, until the screen has finished reading it out. That is the port's
second invariant — simulate, then replay — expressed as control flow rather than as a convention.

---

## 6. The gate

Whatever is built, the acceptance test is the same and it already has its data:

> Replay `delve.json` through the interactive path. For every recorded run, feeding the recorded
> decisions to `Delve.Answer` must produce the same `RunState` and the same events as
> `DelveRun.Resolve` produces for the same seed.

`DelveRunTests.Replay` already implements both `IRunChoices` and `IRunObserver` against that file,
so the new test is that class driving `Delve` instead of being called by `Resolve`. If the
restructure is wrong, 516 runs say so on the first press.

### What the oracle covers, and the one thing it does not

The 13,472 recorded steps are not evenly spread, and it is worth knowing which branches they hold
down before leaning on them:

| Step | Recorded |
|---|---|
| draft | 5,688 |
| fight | 5,688 |
| event | 1,459 |
| shop | 515 |
| revive | 122 |
| **cash out** | **none** — now covered by `RunCashOutTests` |

Every recorded run descends until it dies or clears — the chooser that made them returns `false` to
`CashOut`, and so does every other chooser in the test suite. `RunEnding.CashedOut` is tested where
it is SCORED and settled, and nowhere where it is decided.

So the cash-out branch of `Resolve` is the one piece of it that would be restructured with no oracle
underneath, and it is a branch that ends runs and banks gold. **Step 0 of the work below is to cover
it** — a handful of recorded runs that walk out at a gate, or a hand-written test that drives
`Resolve` with a chooser that says yes. It is an hour, and it is the difference between a
restructure that is verified and one that is merely careful.

---

## 7. The work, in order

Each step is meant to leave the game in a state somebody can press.

**0 · Cover the cash-out branch. — DONE.** `RunCashOutTests`, five cases: leaving ends the run
where it stood, the question comes after every fight and nowhere else, leaving skips the gate it
stands at, the bazaar is behind that gate, and a Piggy Bank pays a quarter more on the way out.

It found something on the way, which is the argument for having written it: **the bazaar floor is
never fought.** The gate loop walks through floor seven and out the other side without rolling a
pack, so there is no fight there, no cash-out question, and a delver who means to leave "after
seven" leaves after eight. A cleared run fights twelve floors, not thirteen. That is load-bearing
ordering nothing had written down, and it is exactly what a restructure would have broken while
still looking like a game.

**1 · The engine turns inside out. — DONE.** `Ask`, `Answer`, `Delve`, and `Resolve` reduced to
three lines over it. The corpus replays through it unchanged — 516 runs, event for event — which is
the whole gate, because `Resolve` now IS a caller of the interactive path rather than a rival to it.

Two things the corpus could not cover, so `DelveTests` does: a run is resumed by replaying its
answers, from EVERY stop and not just one; and a fought floor stops the run holding the resolved
fight, so playback owns the clock.

`DelveRun` kept the arithmetic and lost the walk — 698 lines to 415. What is left there is what a
floor DOES; what moved is only the order, which is the part that had to become interruptible.

**2 · A fight that counts. — DONE, and it turned out to be a whole run.** With `Delve` in place the
cheap version was cheaper than the planned one: the scene walks the REAL run loop and answers the
stops it has no screen for — the first relic, the first choice, no deals, no revive — saying so
once in the log. So PLAY now walks a delve floor by floor, settles it through `Career.Settle`, and
returns the delver to an end screen.

Measured, first try: nine floors over three and a half minutes, `405 points, 405 experience, 0
banked`, died on floor nine. The profile went from `runs=0 best=0 xp=0 seen=0` to `runs=2 best=505
xp=910 seen=10`, and the menu opened on THE DARK KEPT YOU with the experience screen behind it.

The auto-answers are the scaffold, and every one of them is a line a screen deletes. What is NOT a
scaffold is anything under them: it is the gated run loop, the gated scoring, and the gated
banking.

Two things fell out of it. `Bout` is gone — it was a one-floor fight built two commits earlier, and
once `Delve` seeded its own delver the two were a second spelling of the same thing, which is the
fault this plan was written to avoid. And `HallView` was loading its four-and-a-half-megabyte
backdrop once per FLOOR, which nothing noticed while a screen only ever showed one fight.

**3 · The run scene, with two stages. — DONE.** `FightScene` is a stage machine now: it draws the
rail, routes each stop to whichever `RunStage` says it handles that kind, and waits there until the
delver has decided. `AskKind.Fought` is the one stop that is not a question — it is read out and
answered when the reading is over. Stops with no stage are still answered dully and said once, but
the draft's line is gone from `Plainly`.

Two Core pieces came first, so the screen decides nothing: `FloorRails.Of` says which floors are
marked, which are behind, how big each node is drawn and where the events sit, and `DraftCards.Of`
says what is on offer and — the only line on a card that decides anything — which held relics each
would CHAIN with. Nine tests, all first try.

`DraftStage` answers both `Draft` and `Reroll`, because they are one table with a different question
over it; that is what `RunStage.Handles` is for. Measured end to end: PLAY → DELVE → *take one
relic* → picked Wind Anklet over Momentum Bead → floor one fought → floor two, with the reroll
offered at ten gold.

Two things fell out of it. A card built from authored offsets printed a three-line description
straight through the relic's name, so the card lays itself out — which is what any of this has to
do for a language that is not English. And the reroll button had no key at all: the source writes
that label into its markup rather than its string table, so the port adds keys of its own now
(`ADDED` in `extract.mjs`), in all eight languages, gated by a test that finds them by difference
rather than by a list anybody has to maintain.

**4 · The delver in the fight. — DONE.** Three things, and the fight looks like the source's now.

*The delver.* `HeroView` composes the hero rather than drawing one off a sheet: eleven slots of
pixel rows stacked back to front into an index grid, coloured on the GPU by a palette texture the
shader looks the index up in. All of that machinery existed and was gated — `HeroCompositor`,
`HeroIndex`, `HeroTextures`, `HeroMaterial`, the palette-swap shader — and **nothing in runtime
code had ever called any of it**. What was missing was one piece: something that could turn the
shipped `hero-pack.json` into a `HeroPack` outside a test. `HeroPackReader` is that, in Core,
because both the tests and the game need it and both must get the same delver; `Corpus.HeroPack`
now goes through it, so the parser under 500 KB of art has exactly one spelling. States are
composed on first use and kept, and destroyed by hand — a `HideAndDontSave` texture is one Unity
will never collect. A delver swings on `EnemyDamage`, flinches on `PlayerDamage`, and dies once.

*The foe queue.* `FoeQueues` gives the pack as tiles: who has fallen, who is up, what each is
ringed in. How far in is COUNTED FROM THE EVENTS rather than tracked, because playback can be
paused, sped up or skipped and a counter walked along the way would be wrong in all three. Hidden
on a floor with one foe, which is most of them.

*The card a fight opens on.* `IntroCards` says what is blocking the way and what it is carrying.
It is raised on the WALK step — the gap playback already reserves for setting off down the hall —
so it costs no new timing and a delve watched with the intro skipped never builds one. The
source's FIGHT button to dismiss it early is deliberately absent: the loop waits a fixed span for a
walk, so a button would take the card away and leave a delver looking at an empty room. It wants
the same gate `Paused` uses, which arrives with the pause screen in step 6.

Two things worth writing down. The card says something DIFFERENT from the bestiary on purpose — a
dungeon king is a `DUNGEON KING` in the book and a `FINAL BOSS` on the screen that opens the fight,
because one is a reference and the other is a threat; the obvious tidy-up is to make them one
function and there is a test standing in front of it. And the card's one sentence had no key
either, so `blocksWay` joined `reroll` in the port's own `ADDED` table.

**5 · The gate stages. — DONE.** Measured: floor one cleared, walked out at the gate, *the delve
ended on floor 1 (CashedOut): 12 points, 12 experience, **13 banked***, and the menu opened on YOU
WALKED OUT WITH THE PURSE.

*Decide.* `GateCards` is one number said three times — held, at risk, kept — because that IS the
decision and a delver reading it once tends to read it as only one of the three. There is now a
test standing in front of those three ever disagreeing.

The peek at what is below shows the foe at the **back** of the pack, not the front. That is the
opposite of what it sounds like it should be, and writing it the obvious way is what a test caught:
a pack is fought front to back with the worst of it last, so the first foe only tells a delver they
survive the next thirty seconds — which they could already guess. It also makes the source's own
wording true, since "BEHIND 2 GUARDS" describes something standing behind two other things.

The same test turned up a real wart and it is pinned rather than smoothed: **the gate above the
bazaar peeks at a pack that is never fought.** The run rolls one for the bazaar floor like any
floor's, walks the delver through a shop instead, and rolls again on the way out. That is the
source's behaviour — `BazaarRollsItsOwnPack` is the recorded rule — and peeking past it would
change which numbers the RNG draws, and every recorded run with them.

*Travel.* The walk down: stages off, the rail one floor deeper, and `PanMs` of nothing at all —
the same 2.6 seconds the hall already pans by, so walking down and walking along a floor move at
one speed. Without it the gate's two buttons cut straight to the next draft, and the rail's step is
something a delver can only notice afterwards: the difference between having chosen to go deeper
and finding oneself deeper.

I first wrote here that the source's sliding dungeon band could not be ported, because the hall art
is keyed by tier rather than by floor — one image per dungeon, so a descent would slide a picture
onto itself. **That was wrong**, and reading `bandPlate` properly is what showed it. The two bands
are usually the same picture, and it does not matter: the floor being left is panned to wherever
the delver fought their way to, and the floor arriving opens at its LEFT door. The slide is a walk
from one hall's far door to the next hall's near one, which is the whole story it tells. It is
ported now — see the entry below.

Eight more strings joined the port's own `ADDED` table. The gate is the most consequential screen
in the game and it was going to be entirely English otherwise.

**5a · The descent, drawn. — DONE.** Two bands and one number. `HallView` holds the floor underfoot
and the floor below it, always exactly one window apart, and a descent lifts both by one window
height — which is the source's arrangement exactly: a slider carrying both, translated up a hundred
per cent. The band arriving is drawn at pan zero, its left door, because that is what makes the
slide read as a descent rather than a jump.

The rail's marker walks with it. `FloorRailView` gained the source's little gold pointer, and it
slides one node further down over the same duration with the same easing — the two are one movement
seen twice, so easing them differently would let them drift apart in the middle and read as two
things happening at once. The marker is placed from where the nodes ACTUALLY ARE after a forced
layout pass, not from arithmetic over their sizes; the nodes resize around whichever floor is
underfoot, so every node moves whenever the rail is redrawn.

Two details that are not decoration. The rail is drawn for the floor being LEFT and only the marker
moves, because redrawing for the new floor first would resize the whole rail under a marker that
had not set off — showing the delver the destination before the journey. And the marker stops
HALFWAY when an event is waiting, because an event sits in the gap between two floors and a delver
who has walked into one has not arrived at the floor beyond it. That is also why the run can be
mid-descent with its floor not yet advanced: `Delve` yields the event before it steps through the
gate, so a pending event is exactly the signal that the walk ends in the gap.

**5b · The order of a floor. — DONE.** The three beats a floor is made of were running on top of
each other, and it took watching one properly to see it.

A foe entering is announced THREE times now, not two. Playback walks the hall toward them and does
not advance; puts their card up and still does not advance; and only then shows the event. The card
is opaque and covers the whole screen, so raising it as the walk SET OFF played the entire approach
behind it — three and a half seconds of hall sliding where nobody could see it, which made the walk
read as a pause with nothing in it. `PlaybackAction.Meet` and `PacingRules.IntroMs` are what
separate them; three tests failed on the change and were rewritten to the new order rather than
around it.

And a floor now ENDS at its door. A floor's foes are met evenly spaced short of the far door, so
the last of them falls with the delver standing in the middle of the hall; the source spends a full
stride walking the rest before it offers the gate. It is right to: the gate asks whether to go
down, and a delver should have reached the stairs before being asked. Not walked when the delver
fell — there is no stroll to the door at the end of a floor that killed you.

Measured, sampling the hall's pan against the two overlays: pan moving with the card down, pan
settled at −1580 and *then* the card up, card down and the fight running, pan −1602 → −3075 →
−3159 for the last stretch, and the gate only once it had arrived.

**5c · Nobody meets a foe before they are introduced. — DONE.** Three things that were missing
from the announcement, all reported from a delve.

The foe's frame is now kept EMPTY from the moment the walk sets off until whatever was announced
has flown into it. The card is worth nothing if the surprise is already standing there, and the
source hides the same glyph for the same span. Only the showing waits — the sprite is set as
early as ever, because everything on that screen redraws from any event.

The card's picture then FLIES into the frame: `FoeFlight` carries a copy from one rect to the
other over `FlyMs`, swelling a third again too large at sixty-two per cent of the way and settling
from there. A flight that only shrank would read as the foe retreating; swelling first reads as it
coming at you. A copy, so neither the card nor the frame has to know it exists, and a flight cut
short leaves both where they were.

And the card carries a FIGHT button with the timer draining behind it — one slab rather than a
button beside a bar, so there is nothing to look between. Pressing it genuinely shortens the wait
rather than hiding the card and leaving a delver looking at an empty room, which needed the loop to
learn a wait it can be talked out of: `IPlaybackClock.Wait(ms, cut, token)` and
`IPlaybackScreen.Impatient`, asked once a frame and only while a card is up. Everything else in a
fight runs on the fight's own clock, and skipping THAT is what the speed control is for.

Still missing, and now the only part that is: the bazaar's own hall. The source gives that floor
`hall-bazaar.png` and the port has not imported it, so descending into the bazaar arrives in the
dungeon's own hall. That is the line in `HallView.Descend` that changes when the art lands.

**6 · The rest.** `event` — DONE — then `shop` / `merchant`, `revive`, `pause`. Each is a stage the
engine already answers and a screen that does not exist.

A correction to this line before anything else: **the source's socket picking is dead code.**
`socketPick` is never assigned — the only thing that could set it sits behind a ternary whose
branches are both the empty array — so `socketPicking` is always false, the picker never renders,
and `shopSocket` is unreachable. The socket/component system was replaced by AWAKENING, which is
what Core already ports (`DealKind.Awaken`). So the bazaar is wares and awakening, and there is
nothing to pick a socket into.

**6a · The event. — DONE.** Two beats on one screen, which is the shape the source has: the
picture and the title stay put while the choices give way to the sentence saying what one of them
did. That second beat is why `RunStage.Tells` exists — the run answers an event in ONE stop, the
way every recorded run answers one, and writes the outcome back onto the stop for the screen to
read out while the scene waits.

The prose is generated (`EventText.cs`, from the same extraction the logic was hand-ported from)
and the outcome sentences are not: they live in `DungeonEvents.cs` beside the branches they
describe, because a line reading "+25 gold" is a claim about a branch and belongs where somebody
changing that branch will see it. Nine of the twenty-six choices fork on a roll.

Two tests failed on their first run and both were the test being wrong, which is the point of
writing them: a free choice can still rob you — the thief takes ten gold from a delver who fights
him for nothing — and a priced choice can name its price in the LABEL rather than the hint, which
is what the Imp's Dice does. What replaced them are the invariants that are actually true: a
priced choice shows its number somewhere a delver reads, and an event can hurt but never kill.

**On translating these screens.** The source ships the event, shop, merchant and revive screens
entirely in hardcoded English — no locale keys at all, in any of the eight languages. The port
adds keys for the UI chrome around them (`betweenFloors`, `continueDescent`, `notEnoughGold`) and
leaves the PROSE in English, exactly as the source does. Twelve titles, twelve paragraphs,
twenty-six labels and hints and some forty outcome sentences is not a thing to machine-translate
into Arabic and Japanese unreviewed, and doing so would look like eight shipped languages while
being one shipped language and seven guesses.

**7 · Versus.** Staging currently orders a DELVE. `VersusRun` is gated and unbuilt; it wants the
same treatment as steps 1 and 3.

Steps 1 and 2 are small and independent. Step 3 is the big one. Steps 4–6 are mostly screen work
against an engine that already answers.

---

## 8. What needs deciding

1. ~~**Iterator or thread**~~ **Iterator.** Done.
2. ~~**Is WebGL ever a target?**~~ **Not currently** — and the iterator keeps the door open anyway.
3. ~~**Should a run in progress survive the app closing?**~~ **Yes** — and it costs almost nothing.
   A C# iterator's state cannot be serialised, so a run is not SAVED, it is REPLAYED: the whole of
   a run in progress is its seed and its list of answers, a few dozen bytes, and resuming is
   feeding them back. A whole run replays in about four milliseconds. There is no engine state to
   write down and so no save format to version, which is the rarest kind of answer to this
   question.
4. ~~**How much of the fight screen is in scope for step 4**~~ **All of it** — the hero sprite, the
   queue and the intro banner.
