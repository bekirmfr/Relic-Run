using System;
using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Core.Stats;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// What a delver reads while a fight happens.
    /// </summary>
    /// <remarks>
    /// The log is the fairest test of invariant 5 — an event carries its own snapshot, so the
    /// view-model needs no memory of the fight at all. Hand it any event out of any fight in any
    /// order and it says the same thing, which is asserted directly rather than assumed.
    ///
    /// There is nothing to diff against: the source's <c>lineFor</c> is a component method the
    /// lifter cannot slice, and the corpus records events rather than the words they become. So
    /// the gates are the ones that can be stated — every event kind says SOMETHING, every
    /// localisation key it asks for exists, and every one of the 34,000 recorded events survives
    /// being turned into a line.
    /// </remarks>
    [TestFixture]
    public class CombatLogTests
    {
        private static readonly IReadOnlyList<RelicId> NoRelics = new RelicId[0];
        private static readonly IReadOnlyList<StatModifier> NoMods = new StatModifier[0];

        private static Locale _english;

        [OneTimeSetUp]
        public void LoadEnglish()
        {
            _english = new Locale("en", Corpus.LocaleTable("en"));
        }

        private static CombatEvent An(CombatEventType type, int amount = 7, string source = "you",
            int depth = 0, bool? foe = null, bool? crit = null, int enemyIndex = 0)
        {
            var state = new CombatSnapshot(1, 100, 50, 0, 50, 5, EnemyRank.Guard, 0, 25, 10,
                0, enemyIndex, 0, NoRelics, NoMods, default(CombatCounters));

            return new CombatEvent(type, depth, state, amount, source, RelicId.None, null, crit,
                null, foe);
        }

        private CombatLog Reading(string rival = null) { return new CombatLog(_english, rival); }

        /* ---------- totality ---------- */

        /// <summary>
        /// Every kind of event a fight can produce says something.
        /// </summary>
        /// <remarks>
        /// A missing case in the switch falls through to silence, which on screen is a fight
        /// where something visibly happened and the log did not mention it. Silence has to be a
        /// decision, and the only kind it is a decision for is one nothing emits.
        /// </remarks>
        [Test]
        public void EveryEventKindSaysSomething()
        {
            CombatLog log = Reading();
            var silent = new List<string>();

            foreach (CombatEventType type in Enum.GetValues(typeof(CombatEventType)))
            {
                CombatLine line = log.For(An(type));
                if (!line.Shown) silent.Add(type.ToString());
            }

            Assert.That(silent, Is.Empty,
                "these produce no line at all: " + string.Join(", ", silent));
        }

        /// <summary>Every key the log asks for exists in English.</summary>
        /// <remarks>
        /// <see cref="Locale"/> falls back to showing the key itself, which is right — a missing
        /// string should be visible rather than blank — and is also why a typo here would reach
        /// a delver as <c>logYouStrike</c> instead of crashing in a test.
        /// </remarks>
        [Test]
        public void EveryKeyTheLogAsksForExists()
        {
            Dictionary<string, string> english = Corpus.LocaleTable("en");

            foreach (string key in CombatLog.Keys)
            {
                Assert.That(english.ContainsKey(key), Is.True, key + " is not in en.json");
            }

            for (int i = 0; i < EnemyCatalog.All.Count; i++)
            {
                Assert.That(english.ContainsKey("en" + i), Is.True,
                    "the bestiary has no name for species " + i);
            }
        }

        /// <summary>No line comes out as its own key, which is what a missing string looks like.</summary>
        [Test]
        public void NoLineIsAKeyThatNobodyWrote()
        {
            CombatLog log = Reading();

            foreach (CombatEventType type in Enum.GetValues(typeof(CombatEventType)))
            {
                CombatLine line = log.For(An(type));

                foreach (string key in CombatLog.Keys)
                {
                    Assert.That(line.Text, Is.Not.EqualTo(key),
                        type + " came out as the key " + key);
                }
            }
        }

        /* ---------- what the lines say ---------- */

        [Test]
        public void TheDelversOwnStrikeReadsAsTheirs()
        {
            CombatLine line = Reading().For(An(CombatEventType.EnemyDamage, 12, "you"));

            Assert.That(line.Kind, Is.EqualTo(LineKind.You));
            Assert.That(line.Text, Does.Contain("12"));
        }

        /// <summary>A relic of the delver's is a chain line, not a strike.</summary>
        [Test]
        public void ARelicsDamageReadsAsAChain()
        {
            CombatLine line = Reading().For(An(CombatEventType.EnemyDamage, 3, "Thorn Vest"));

            Assert.That(line.Kind, Is.EqualTo(LineKind.Chain));
            Assert.That(line.Text, Does.Contain("Thorn Vest"));
            Assert.That(line.Text, Does.Contain("3"));
        }

        /// <summary>
        /// Being hit reads three ways, and only one of them is localised.
        /// </summary>
        /// <remarks>
        /// A foe's thorns is not the foe hitting you and a critical is not an ordinary blow, so
        /// the source separates all three. That the middle one is English and the last is not is
        /// the tracked gap, stated here where it can be seen rather than left to be discovered
        /// in Japanese.
        /// </remarks>
        [Test]
        public void BeingHitReadsThreeWays()
        {
            CombatLog log = Reading();

            CombatLine ordinary = log.For(An(CombatEventType.PlayerDamage, 9));
            CombatLine critical = log.For(An(CombatEventType.PlayerDamage, 9, crit: true));
            CombatLine thorns = log.For(An(CombatEventType.PlayerDamage, 9, foe: true));

            Assert.That(ordinary.Kind, Is.EqualTo(LineKind.Hurt));
            Assert.That(critical.Kind, Is.EqualTo(LineKind.Hurt));
            Assert.That(thorns.Kind, Is.EqualTo(LineKind.EnemyChain), "thorns are the foe's relic");

            Assert.That(critical.Text, Does.Contain("CRITICAL"));
            Assert.That(ordinary.Text, Is.Not.EqualTo(critical.Text));
            Assert.That(thorns.Text, Does.Contain("Thorn Vest"));
        }

        /// <summary>Each species is called by its own name.</summary>
        /// <remarks>
        /// Obvious until it is not: every line naming a foe reads the bestiary by index, and an
        /// index dropped on the floor gives every creature in the game the first one's name. The
        /// log would still make sense — it would just be about a rat, always.
        /// </remarks>
        [Test]
        public void EachSpeciesIsCalledByItsOwnName()
        {
            CombatLog log = Reading();
            var named = new HashSet<string>();

            for (int species = 0; species < EnemyCatalog.All.Count; species++)
            {
                CombatLine line = log.For(An(CombatEventType.Kill, enemyIndex: species));

                Assert.That(named.Add(line.Text), Is.True,
                    "species " + species + " is called what another one is called");
            }
        }

        /// <summary>A relic's heal is the relic's, not the delver's.</summary>
        /// <remarks>
        /// The same line for both would take the credit from the Alchemist's Vial and give it to
        /// the person wearing it — which is most of what a delver is reading the log to find out.
        /// </remarks>
        [Test]
        public void AHealFromARelicIsTheRelicsToClaim()
        {
            CombatLog log = Reading();

            CombatLine own = log.For(An(CombatEventType.Heal, 6, "you"));
            CombatLine relic = log.For(An(CombatEventType.Heal, 6, "Alchemist's Vial"));

            Assert.That(own.Text, Is.Not.EqualTo(relic.Text));
            Assert.That(relic.Text, Does.Contain("Alchemist's Vial"));
            Assert.That(own.Text, Does.Not.Contain("Alchemist's Vial"));
        }

        /// <summary>A foe entering reads cleanly, with no gap where a number would go.</summary>
        /// <remarks>
        /// The line is "{i} {n} blocks the way" and nothing fills the first slot, so it arrives
        /// with a space in front of it. <see cref="Locale.Clean"/> takes that off — which is why
        /// the extra trim the source needs is dead code here rather than a safeguard.
        /// </remarks>
        [Test]
        public void AFoeEnteringHasNoGapInFrontOfIt()
        {
            CombatLine line = Reading().For(An(CombatEventType.Enter));

            Assert.That(line.Text, Is.EqualTo(line.Text.Trim()));
            Assert.That(line.Text, Does.Not.StartWith(" "));
        }

        /// <summary>A duel is against somebody with a name.</summary>
        /// <remarks>
        /// Calling a rival "Skeleton" because their avatar happens to be one would read as a bug
        /// rather than as flavour, so a rival's own name displaces the bestiary's everywhere.
        /// </remarks>
        [Test]
        public void ARivalIsCalledByTheirName()
        {
            CombatLine delve = Reading().For(An(CombatEventType.Kill, enemyIndex: 3));
            CombatLine duel = Reading("Mara").For(An(CombatEventType.Kill, enemyIndex: 3));

            Assert.That(duel.Text, Does.Contain("Mara"));
            Assert.That(delve.Text, Is.Not.EqualTo(duel.Text));
            Assert.That(delve.Text, Does.Not.Contain("Mara"));
        }

        [Test]
        public void ARivalsRelicsAreTheirsToo()
        {
            CombatLine anonymous = Reading().For(An(CombatEventType.EnemyFury, 4));
            CombatLine named = Reading("Mara").For(An(CombatEventType.EnemyFury, 4));

            Assert.That(anonymous.Text, Does.StartWith("Rival"));
            Assert.That(named.Text, Does.StartWith("Mara"));
            Assert.That(named.Text, Does.Contain("Berserker Charm"));
        }

        /// <summary>The depth of a chain is carried, not drawn.</summary>
        /// <remarks>
        /// The source prefixes up to three arrows and then stops counting. That is a decision
        /// about a strip of UI and belongs where the strip is, so the view-model reports the
        /// number and says nothing about arrows.
        /// </remarks>
        [Test]
        public void ChainDepthIsCarriedRatherThanDrawn()
        {
            CombatLine deep = Reading().For(An(CombatEventType.Heal, 5, "Alchemist's Vial", 3));

            Assert.That(deep.Depth, Is.EqualTo(3));
            Assert.That(deep.Text, Does.Not.Contain("↳"));
        }

        /* ---------- the tracked gap ---------- */

        /// <summary>
        /// The lines whose SENTENCE is English whatever language the game is in.
        /// </summary>
        /// <remarks>
        /// The sentence, not the whole line: several of these embed the foe's name, and the
        /// bestiary IS translated, so the line changes while the words around the name do not.
        /// A delver reading Japanese sees a Japanese rat sidestepping in English, which is worse
        /// than a line that is simply untranslated. This was written as a whole-line comparison
        /// first and failed against a locale that reverses its strings — the name came back
        /// reversed and the sentence did not, which is how the half-and-half came to light.
        ///
        /// Asserted as the thing that is currently true, the way <c>validate.mjs</c> tracks its
        /// four gaps. Localising one of these breaks this test, which is how whoever does that
        /// work knows when they are finished.
        /// </remarks>
        [Test]
        public void NineKindsKeepTheirEnglishWhateverLanguageTheGameIsIn()
        {
            var stubborn = new Dictionary<CombatEventType, string>
            {
                { CombatEventType.EnemyMiss, "sidesteps your strike!" },
                { CombatEventType.EnemyHeal, "heals" },
                { CombatEventType.EnemySlow, "staggers the foe" },
                { CombatEventType.Momentum, "rolls: +" },
                { CombatEventType.EnemyFury, "rages: +" },
                { CombatEventType.DashOpen, "first strike!" },
                { CombatEventType.EnemyDashOpen, "they strike first!" },
                { CombatEventType.Luck, "a lucky break" },
                { CombatEventType.First, "you" },
            };

            Assert.That(CombatLog.English.Count, Is.EqualTo(stubborn.Count),
                "the list and this table disagree about how many lines are English");

            var log = new CombatLog(new Locale("xx", Backwards(), Corpus.LocaleTable("en")));

            foreach (CombatEventType type in CombatLog.English)
            {
                Assert.That(stubborn.ContainsKey(type), Is.True, type + " is not in this table");
                Assert.That(log.For(An(type)).Text, Does.Contain(stubborn[type]),
                    type + " translated after all, so take it off the list");
            }
        }

        /// <summary>And the ones that are not.</summary>
        [Test]
        public void EverythingElseChangesWithTheLanguage()
        {
            var backwards = new Locale("xx", Backwards(), Corpus.LocaleTable("en"));
            var log = new CombatLog(backwards);
            var plain = new CombatLog(_english);

            var stubborn = new List<string>();
            foreach (CombatEventType type in Enum.GetValues(typeof(CombatEventType)))
            {
                if (Contains(CombatLog.English, type)) continue;
                if (log.For(An(type)).Text == plain.For(An(type)).Text) stubborn.Add(type.ToString());
            }

            Assert.That(stubborn, Is.Empty,
                "these read the same in every language and are not on the list: " +
                string.Join(", ", stubborn));
        }

        /// <summary>Being hit is localised for an ordinary blow and English for a hard one.</summary>
        [Test]
        public void BeingHitIsHalfLocalised()
        {
            var backwards = new CombatLog(new Locale("xx", Backwards(), Corpus.LocaleTable("en")));
            var plain = Reading();

            Assert.That(backwards.For(An(CombatEventType.PlayerDamage, 9)).Text,
                Is.Not.EqualTo(plain.For(An(CombatEventType.PlayerDamage, 9)).Text),
                "an ordinary blow translates");

            Assert.That(backwards.For(An(CombatEventType.PlayerDamage, 9, crit: true)).Text,
                Does.Contain("lands a CRITICAL hit for"),
                "and a critical one keeps its English around the translated name");
        }

        /// <summary>
        /// A relic with a locale key is named in the delver's language; one without is not.
        /// </summary>
        /// <remarks>
        /// Nineteen of the fifty have a key. The other thirty-one were added to the source's
        /// expansion table and are English everywhere, which is the same tracked gap
        /// <c>validate.mjs</c> records — carried here rather than papered over, because showing
        /// the shipped English name is better than showing a key nobody wrote.
        /// </remarks>
        [Test]
        public void ARelicIsNamedInTheDelversLanguageWhenThereIsOne()
        {
            var translated = 0;
            var english = 0;

            foreach (RelicTextDef relic in RelicText.All)
            {
                if (relic.Translated) translated++;
                else english++;

                Assert.That(relic.Name, Is.Not.Null.And.Not.Empty, relic.Id.ToString());
            }

            Assert.That(translated, Is.EqualTo(19));
            Assert.That(english, Is.EqualTo(31), "the untranslated set has changed size");

            // The Berserker Charm has a key, so the line naming it moves with the language.
            Assert.That(RelicText.Get(RelicId.BerserkerCharm).Translated, Is.True);

            var backwards = new CombatLog(new Locale("xx", Backwards(), Corpus.LocaleTable("en")));
            Assert.That(backwards.For(An(CombatEventType.EnemyFury, 4)).Text,
                Does.Not.Contain("Berserker Charm"),
                "the relic's name did not follow the language");
        }

        /* ---------- a real fight ---------- */

        /// <summary>
        /// Every event of a recorded fight becomes a line, and none of them is empty.
        /// </summary>
        /// <remarks>
        /// The synthetic cases above ask about one event of each kind. This asks about the
        /// combinations a real fight produces — a heal from a source with an apostrophe in its
        /// name, a strike at depth four, a kill of a species nothing else exercises.
        /// </remarks>
        [Test]
        public void EveryEventOfARecordedFightBecomesALine()
        {
            CombatLog log = Reading();
            var kinds = new HashSet<LineKind>();
            int lines = 0;

            foreach (CombatEvent shown in ARecordedFight())
            {
                CombatLine line = log.For(shown);

                Assert.That(line.Shown, Is.True, shown + " produced nothing to read");
                Assert.That(line.Text.Trim(), Is.EqualTo(line.Text), "a line with loose ends");

                kinds.Add(line.Kind);
                lines++;
            }

            Assert.That(lines, Is.GreaterThan(20));
            Assert.That(kinds.Count, Is.GreaterThan(3), "a fight this dull is not a fair test");
        }

        /// <summary>The same event always reads the same way, whenever it is asked about.</summary>
        /// <remarks>
        /// Invariant 5, stated as a test. An event carries its own snapshot, so a log that
        /// remembered anything about the fight would be able to disagree with itself — and the
        /// place that would show is a replay, where the same fight read differently the second
        /// time.
        /// </remarks>
        [Test]
        public void TheLogRemembersNothingAboutTheFight()
        {
            CombatLog log = Reading();
            IReadOnlyList<CombatEvent> fight = ARecordedFight();

            var forwards = new List<string>();
            foreach (CombatEvent shown in fight) forwards.Add(log.For(shown).ToString());

            var fresh = Reading();
            for (int i = fight.Count - 1; i >= 0; i--)
            {
                Assert.That(fresh.For(fight[i]).ToString(), Is.EqualTo(forwards[i]),
                    "event " + i + " reads differently out of order");
            }
        }

        /* ---------- the awkward callers ---------- */

        [Test]
        public void NoLocaleAtAllShowsTheKeysRatherThanCrashing()
        {
            var log = new CombatLog(null);
            CombatLine line = log.For(An(CombatEventType.Death));

            Assert.That(line.Kind, Is.EqualTo(LineKind.Death));
            Assert.That(line.Text, Is.EqualTo("logDeath"), "visible, and obviously wrong");
        }

        [Test]
        public void AnEventWithNoAmountReadsAsNone()
        {
            var state = new CombatSnapshot(1, 100, 50, 0, 50, 5, EnemyRank.Guard, 0, 25, 10,
                0, 0, 0, NoRelics, NoMods, default(CombatCounters));
            var nothing = new CombatEvent(CombatEventType.Gold, 0, state, null, "Coin Magnet");

            Assert.That(Reading().For(nothing).Text, Does.Contain("0"));
        }

        /* ---------- helpers ---------- */

        /// <summary>An English table with every value reversed. A language nobody speaks.</summary>
        /// <remarks>
        /// Better than a real second language for this: every key differs from English, so a
        /// line that comes back unchanged is a line that never reached the locale — which is
        /// exactly the question being asked.
        /// </remarks>
        private static Dictionary<string, string> Backwards()
        {
            var table = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> line in Corpus.LocaleTable("en"))
            {
                var flipped = line.Value.ToCharArray();
                Array.Reverse(flipped);
                table[line.Key] = "~" + new string(flipped);
            }

            return table;
        }

        private static bool Contains(IReadOnlyList<CombatEventType> types, CombatEventType type)
        {
            for (int i = 0; i < types.Count; i++)
            {
                if (types[i] == type) return true;
            }

            return false;
        }

        private static IReadOnlyList<CombatEvent> ARecordedFight()
        {
            List<CorpusFight.Case> cases = CorpusFight.Load("mixed.json");
            CorpusFight.Case busiest = null;

            for (int i = 0; i < cases.Count && i < 40; i++)
            {
                if (busiest == null || cases[i].Events.Count > busiest.Events.Count) busiest = cases[i];
            }

            var engine = new CombatEngine(CombatRules.DelveAsRecorded());
            return engine.ResolveFloor(busiest.Hero, busiest.Pack,
                new Core.Determinism.Mulberry32(busiest.FightSeed)).Events;
        }
    }
}
