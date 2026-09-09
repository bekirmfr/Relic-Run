using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Run;
using RelicRun.Core.Stats;

namespace RelicRun.Tests
{
    /// <summary>
    /// A relic does the same thing whoever is wearing it.
    /// </summary>
    /// <remarks>
    /// What differs between a delver and a foe is REACH — what each can get hold of — never what
    /// a relic does once they have it. That was almost true already: the combat code is written
    /// against <c>ICombatActor</c> and reads relic counts through it, so a foe's Thorn Vest has
    /// always returned damage.
    ///
    /// It was untrue in four places, all of them in <c>FoeActor</c> and all of them stubs that
    /// read as facts about foes rather than as gaps. It could not wake a relic. It reported an
    /// empty inventory. It had no sockets. And it claimed every slot had already fired this beat,
    /// which on its own would have made the other three pointless.
    /// </remarks>
    [TestFixture]
    public class FoeRelicTests
    {
        private static HeroState Delver(params RelicId[] items)
        {
            RunSetup setup = RunSetup.ForLevel(1);

            return new HeroState
            {
                Floor = 1,
                Php = setup.Hp,
                Pmax = setup.Hp,
                BaseAtk = setup.Atk,
                BaseDef = setup.Def,
                BaseSpd = setup.Spd,
                BaseLck = setup.Lck,
                Items = new List<RelicId>(items),
            };
        }

        private static EnemyState Foe(int hp, params RelicId[] relics)
        {
            EnemyState made = EnemyPackGenerator.Authored(0, EnemyRank.Guard, hp, 6, 0, 25, 10, 5,
                relics);

            return made;
        }

        private static IReadOnlyList<CombatEvent> Fight(HeroState hero, EnemyState foe)
        {
            return new CombatEngine(CombatRules.Delve())
                .ResolveFloor(hero, new List<EnemyState> { foe }, new Mulberry32(0x5E1F00D))
                .Events;
        }

        private static bool Mentions(IReadOnlyList<CombatEvent> events, RelicId relic)
        {
            foreach (CombatEvent shown in events)
            {
                if (shown.Relic == relic) return true;
            }

            return false;
        }

        /// <summary>
        /// A foe can wake a relic, and waking it is what makes it work.
        /// </summary>
        /// <remarks>
        /// Iron Skin is the case worth using, because it is gated on an awakening AND it is in
        /// the boss kits the corpus records. So it was carried by bosses and inert on every one
        /// of them, and no amount of equipping could have changed that: <c>IsAwake</c> answered
        /// false before it looked at anything.
        ///
        /// Both halves are asserted. Asleep it must still do nothing — that is the same rule a
        /// delver lives under, and a foe whose relics worked without waking would be the
        /// actor-specific behaviour this is meant to remove.
        /// </remarks>
        [Test]
        public void AFoeCanWakeARelicAndOnlyThenDoesItWork()
        {
            EnemyState asleep = Foe(40, RelicId.IronSkin);
            EnemyState awake = Foe(40, RelicId.IronSkin);
            awake.Awakened = new List<RelicId> { RelicId.IronSkin };

            Assert.That(Mentions(Fight(Delver(), asleep), RelicId.IronSkin), Is.False,
                "an unwoken relic does nothing, for a foe exactly as for a delver");

            Assert.That(Mentions(Fight(Delver(), awake), RelicId.IronSkin), Is.True,
                "a woken Iron Skin should turn a blow, whoever is wearing it");
        }

        /// <summary>
        /// A foe's relics are slots, not only a tally.
        /// </summary>
        /// <remarks>
        /// The inventory read as empty, so anything working per COPY skipped a foe entirely.
        /// Counting never did — which is why this was so easy to miss: the relics that are read
        /// as counts have always worked, and they are most of them.
        /// </remarks>
        [Test]
        public void AFoeWearsItsRelicsInSlots()
        {
            EnemyState foe = Foe(40, RelicId.ThornVest, RelicId.IronSkin);

            Assert.That(foe.Relics, Is.Not.Null.And.Count.EqualTo(2));
            Assert.That(foe.CountRelic(RelicId.ThornVest), Is.EqualTo(1));

            // A foe with no relics at all still has to be a coherent wearer rather than a null.
            EnemyState bare = Foe(40);

            Assert.That(bare.Relics, Is.Null, "an empty list is still no list");
            Assert.That(bare.Awakened, Is.Not.Null);
            Assert.That(bare.SocketTriggers, Is.Not.Null);
        }

        /// <summary>
        /// The relics that were already actor-agnostic still are.
        /// </summary>
        /// <remarks>
        /// Thorn Vest is read through <c>defender.Effective</c>, so it has always worked for a
        /// foe. Asserted anyway, because the four stubs were removed underneath it and a change
        /// that fixed the broken half by breaking the working half would be no improvement.
        /// </remarks>
        [Test]
        public void AFoesThornVestStillReturnsDamage()
        {
            Assert.That(Mentions(Fight(Delver(), Foe(40, RelicId.ThornVest)), RelicId.ThornVest),
                Is.True);
        }

        /// <summary>
        /// Adding the capability changed no fight that does not use it.
        /// </summary>
        /// <remarks>
        /// The important property of this whole change, and the reason it needed no rules flag.
        /// An empty awakening set still answers no; slots with no sockets still fire nothing. So
        /// the three hundred and twenty-nine recorded fights that put relics on foes resolve
        /// exactly as they did, and the corpus goes on meaning what it meant.
        ///
        /// Checked by replaying the same fight against a foe built the old way — no awakenings,
        /// no sockets — and requiring it to be identical event for event to one built with the
        /// new fields left at their defaults.
        /// </remarks>
        [Test]
        public void AFoeThatWakesNothingFightsExactlyAsBefore()
        {
            EnemyState plain = Foe(40, RelicId.IronSkin, RelicId.Whetstone);

            EnemyState defaulted = Foe(40, RelicId.IronSkin, RelicId.Whetstone);
            defaulted.Awakened = new List<RelicId>();
            defaulted.SocketTriggers = new Dictionary<int, SocketTrigger>();
            defaulted.SocketEmitters = new Dictionary<int, SocketEmitter>();

            IReadOnlyList<CombatEvent> before = Fight(Delver(RelicId.Whetstone), plain);
            IReadOnlyList<CombatEvent> after = Fight(Delver(RelicId.Whetstone), defaulted);

            Assert.That(after.Count, Is.EqualTo(before.Count));

            for (int i = 0; i < before.Count; i++)
            {
                Assert.That(after[i].Type, Is.EqualTo(before[i].Type), "event " + i);
                Assert.That(after[i].Amount, Is.EqualTo(before[i].Amount), "event " + i);
                Assert.That(after[i].State.EnemyHp, Is.EqualTo(before[i].State.EnemyHp),
                    "event " + i);
                Assert.That(after[i].State.HeroHp, Is.EqualTo(before[i].State.HeroHp),
                    "event " + i);
            }
        }
    }
}
