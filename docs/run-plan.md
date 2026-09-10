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
| **cash out** | **none** |

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

**0 · Cover the cash-out branch.** See above: no recorded run ever walks out at a gate, so that
path has no oracle. An hour, and it has to come first.

**1 · The engine turns inside out.** `Ask`, `Delve`, `Resolve` as a driver over it. No UI. Done when
the corpus replays through both front doors identically. *This is the risky step and it is first,
because everything after it is shaped by it.*

**2 · A fight that counts.** The summary reaches the save — runs, kills, gold banked, XP, species
seen — and routes to the `Over` and `Xp` screens, which already exist with cards behind them. No run
loop yet. Done when a fight changes the profile.

**3 · The run scene, with two stages.** One scene, a stage machine, a floor rail, and `draft` and
`combat`. `Delve` drives it. Done when PLAY drafts a relic, fights floor one, drafts again and
fights floor two.

**4 · The delver in the fight.** A hero sprite from the compositor that already exists, the foe
queue, the intro banner. This is the step that makes the fight look like the source's.

**5 · The gate stages.** `travel` and `decide` — the breather, the floor rail advancing, cash out or
descend. Done when a run can be walked out of with gold banked.

**6 · The rest.** `event`, `shop` / `merchant` with socket picking, `revive`, `pause`. Each is a
stage the engine already answers and a screen that does not exist.

**7 · Versus.** Staging currently orders a DELVE. `VersusRun` is gated and unbuilt; it wants the
same treatment as steps 1 and 3.

Steps 1 and 2 are small and independent. Step 3 is the big one. Steps 4–6 are mostly screen work
against an engine that already answers.

---

## 8. What needs deciding

1. **Iterator or thread** — §4C or §4B. My recommendation is the iterator, and the argument that
   decides it is not elegance but blast radius: a threading mistake here is a hang, and a hang in
   this project has twice cost more than the feature it was in.
2. **Is WebGL ever a target?** If yes, §4B is out entirely and there is nothing to discuss. The
   source game is a web build; the port has been aimed at a phone.
3. **Should a run in progress survive the app closing?** The source cannot do this — a delve lives
   in memory and closing the tab ends it. The iterator makes it possible; whether it is wanted
   changes what `Delve` has to be able to write down.
4. **How much of the fight screen is in scope for step 4** — the hero sprite alone, or the queue and
   intro banner with it. The first is an afternoon; all three is closer to the fight screen the
   source actually has.
