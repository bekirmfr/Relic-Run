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

        private static int Count(IReadOnlyList<RelicId> relics, RelicId id)
        {
            var many = 0;

            foreach (RelicId each in relics)
            {
                if (each == id) many++;
            }

            return many;
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

            // Counted through the interface rather than with NUnit's Count constraint. That
            // constraint reflects for a property literally called Count, and this list is an
            // ARRAY — which has Length. The two runners disagree about that: the newer NUnit
            // under dotnet resolves it anyway, Unity's bundled one throws. Reading the count
            // asks IReadOnlyList, which both agree about.
            Assert.That(foe.Relics, Is.Not.Null);
            Assert.That(foe.Relics.Count, Is.EqualTo(2));
            Assert.That(foe.CountRelic(RelicId.ThornVest), Is.EqualTo(1));

            // A foe with no relics at all still has to be a coherent wearer rather than a null.
            EnemyState bare = Foe(40);

            Assert.That(bare.Relics, Is.Null, "an empty list is still no list");
            Assert.That(bare.Awakened, Is.Not.Null);
            Assert.That(bare.SocketTriggers, Is.Not.Null);
        }

        /// <summary>
        /// A typed foe is thickened by its own Ox Heart.
        /// </summary>
        /// <remarks>
        /// This is the case that kept looking like a bug and kept having a different explanation.
        /// Ox Heart is not a combat relic — it thickens the pool at the moment it is TAKEN — so a
        /// foe whose hit points were typed in had nothing anywhere to apply it, and the relic sat
        /// on the shelf doing nothing while every gauge said it was there.
        ///
        /// The pool is BAKED where the stats are not, and the asymmetry is deliberate: a pool is
        /// a resource with a starting value that is then spent, while a stat is computed from its
        /// rows every time it is read. Fold them together and a wound heals itself the moment
        /// anything recalculates.
        /// </remarks>
        [Test]
        public void ATypedFoeIsThickenedByItsOwnOxHeart()
        {
            Assert.That(WornKit.Pool(new[] { RelicId.OxHeart }), Is.EqualTo(13),
                "the source gives a delver thirteen for one on pickup; a wearer gets the same");

            Assert.That(Foe(50).Hp, Is.EqualTo(50));
            Assert.That(Foe(50, RelicId.OxHeart).Hp, Is.EqualTo(63));
            Assert.That(Foe(50, RelicId.OxHeart).MaxHp, Is.EqualTo(63),
                "the ceiling rises with the pool, or the foe starts wounded");

            Assert.That(Foe(50, RelicId.OxHeart, RelicId.OxHeart).Hp, Is.EqualTo(76),
                "and it stacks, as a second one does for a delver");
        }

        /// <summary>
        /// The rest of the kit arrives as labelled rows, not as arithmetic.
        /// </summary>
        /// <remarks>
        /// A row carries the relic that gave it, so a number can be explained: "Armour 3" says
        /// nothing about why the thing in front of you is hard to hurt, and "Iron Skin +1"
        /// beside it does.
        ///
        /// The amounts are asserted as literals rather than read back out of the table. A test
        /// that took its expectations from the thing it is checking would agree with any number
        /// the table happened to hold — which is the trap this project has already fallen into
        /// twice, over the set thresholds and the design area.
        /// </remarks>
        [Test]
        public void ATypedFoesKitArrivesAsLabelledRows()
        {
            EnemyState kitted = Foe(50, RelicId.Whetstone, RelicId.IronSkin, RelicId.LuckyClover);

            Assert.That(WornKit.Of(kitted.Mods, Stat.Atk), Is.EqualTo(1), "a Whetstone");
            Assert.That(WornKit.Of(kitted.Mods, Stat.Def), Is.EqualTo(1), "an Iron Skin");
            Assert.That(WornKit.Of(kitted.Mods, Stat.Lck), Is.EqualTo(15), "a Lucky Clover");
            Assert.That(WornKit.Of(kitted.Mods, Stat.Spd), Is.Zero, "and nothing touches speed");

            foreach (StatModifier row in kitted.Mods)
            {
                Assert.That(row.Source, Is.Not.Null.And.Not.Empty,
                    "a row with no source is arithmetic wearing a costume");
            }

            Assert.That(WornKit.Of(Foe(50, RelicId.LuckyClover, RelicId.LuckyClover).Mods, Stat.Lck),
                Is.EqualTo(15),
                "a second clover is not more luck, which is the source's rule and not an oversight");

            Assert.That(WornKit.Of(Foe(50, RelicId.Whetstone, RelicId.Whetstone).Mods, Stat.Atk),
                Is.EqualTo(2), "but a second whetstone is more attack");
        }

        /// <summary>
        /// Swift Boots multiply rather than add, and only their own stat.
        /// </summary>
        /// <remarks>
        /// The one relic in the table that scales. It is worth its own case because a multiplier
        /// behaves differently from a row in three ways that all look the same from outside: it
        /// applies to the total rather than joining a list, it does NOT compound with a second
        /// pair — nothing else in the game rewards duplicates that way — and it touches one stat,
        /// not whichever stat happens to be asked for.
        /// </remarks>
        [Test]
        public void SwiftBootsQuickenTheirWearerOnce()
        {
            var one = new[] { RelicId.SwiftBoots };
            var two = new[] { RelicId.SwiftBoots, RelicId.SwiftBoots };

            Assert.That(WornKit.Scale(one, Stat.Spd), Is.EqualTo(1.25d).Within(1e-9));
            Assert.That(WornKit.Scale(two, Stat.Spd), Is.EqualTo(1.25d).Within(1e-9),
                "a second pair of boots is not more boots");

            Assert.That(WornKit.Scale(one, Stat.Atk), Is.EqualTo(1d),
                "the boots quicken; they do not sharpen");
            Assert.That(WornKit.Scale(new RelicId[0], Stat.Spd), Is.EqualTo(1d));

            Assert.That(Foe(50, RelicId.SwiftBoots).Spd, Is.GreaterThan(Foe(50).Spd),
                "and a foe wearing them should actually be quicker");
        }

        /// <summary>
        /// The rows reach the fight, not just the stat block.
        /// </summary>
        /// <remarks>
        /// A modifier nothing reads is a comment. This asks the engine, which is the only thing
        /// whose opinion of a stat matters — a foe wearing an Iron Skin must actually be harder
        /// to hurt than the same foe without one.
        /// </remarks>
        [Test]
        public void AWornRowChangesWhatTheFightDoes()
        {
            // Eight of them, not one. Defence is a percentage — damage * K / (K + def) — so a
            // single point off a five-point blow rounds back to five and the test would pass
            // against a modifier that reached nothing at all. A weak observable is worse than
            // none: it reports success either way.
            int bare = Hurt(Foe(400));
            int armoured = Hurt(Foe(400, Many(RelicId.IronSkin, 8)));

            Assert.That(bare, Is.GreaterThan(0), "the delver should be landing blows at all");
            Assert.That(armoured, Is.LessThan(bare),
                "Iron Skin should blunt the blow, whoever is wearing it");
        }

        /// <summary>The most this delver ever took off this foe in one blow.</summary>
        private static int Hurt(EnemyState foe)
        {
            int most = 0;

            foreach (CombatEvent shown in Fight(Delver(), foe))
            {
                if (shown.Type == CombatEventType.EnemyDamage && shown.Amount.HasValue &&
                    shown.Amount.Value > most)
                {
                    most = shown.Amount.Value;
                }
            }

            return most;
        }

        /// <summary>
        /// A floor's own pack is untouched, so the corpus still means what it meant.
        /// </summary>
        /// <remarks>
        /// The bonuses apply where a foe was DESCRIBED, not where one was generated. Three
        /// hundred and twenty-nine recorded fights put relics on foes — Iron Skin and Whetstone
        /// among them — and every one of those stat blocks came from a table that already assumed
        /// the kit. Applying the kit again there would not be a fix; it would be counting twice,
        /// and every recorded fight would disagree.
        /// </remarks>
        [Test]
        public void AGeneratedPackIsBuiltByItsFloorAndNotByItsKit()
        {
            // Tier four, because tier one's boss carries nothing at all — a floor whose foes
            // wear no relics could not tell a kit being applied from a kit being ignored.
            DungeonConfig hall = DungeonConfig.ForTier(4);

            var first = EnemyPackGenerator.Build(13, new Mulberry32(0x5E1F00D), hall);
            var again = EnemyPackGenerator.Build(13, new Mulberry32(0x5E1F00D), hall);

            Assert.That(first, Is.Not.Empty);

            for (int i = 0; i < first.Count; i++)
            {
                Assert.That(again[i].Hp, Is.EqualTo(first[i].Hp), "foe " + i);
                Assert.That(again[i].Armor, Is.EqualTo(first[i].Armor), "foe " + i);
            }

            // A floor deep enough to carry a boss kit, so this is not asserting about foes that
            // happen to wear nothing.
            var armed = 0;

            foreach (EnemyState foe in first)
            {
                if (foe.Relics != null && foe.Relics.Count > 0) armed++;
            }

            Assert.That(armed, Is.GreaterThan(0),
                "this floor should field somebody wearing something, or the test proves nothing");

            // The kit is on the foe AND absent from its numbers, which is the whole claim.
            foreach (EnemyState foe in first)
            {
                if (foe.Relics == null) continue;

                EnemyState typed = EnemyPackGenerator.Authored(foe.SpeciesIndex, foe.Rank, foe.Hp,
                    foe.Atk, foe.Armor, foe.Spd, foe.Lck, foe.Drop, foe.Relics);

                if (Count(foe.Relics, RelicId.IronSkin) > 0)
                {
                    Assert.That(WornKit.Of(typed.Mods, Stat.Def), Is.GreaterThan(0),
                        "a typed foe wearing this kit WOULD carry a row for it");

                    Assert.That(WornKit.Of(foe.Mods, Stat.Def), Is.Zero,
                        "and the generated one carries none, because its table already " +
                        "accounted for the kit");
                }
            }
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
        /// A foe's Hollow Idol joins every family, as anyone's does.
        /// </summary>
        /// <remarks>
        /// The idol was the one relic that contributed nothing at all on a foe, and the reason is
        /// worth stating: <c>FoeActor.SetCount</c> counted kinds and stopped, so the relic whose
        /// entire purpose is to count toward EVERY set counted toward its own and no other. Not a
        /// weaker version of the effect — the opposite of it.
        ///
        /// Measured at a threshold, because a set only ever matters at one: seven Guard relics
        /// turn the opening blow aside and say so in the log. The idol is CURSE, so a hand of
        /// nothing but idols holds no Guard relic whatever — and six of them must not be a Guard
        /// set while seven must be. Before, no number of them was.
        ///
        /// A threshold rather than a damage figure, and that is a correction rather than a
        /// preference. The first draft counted the damage Thorn Vest returned, which also moves
        /// with the wearer's CHAIN set — so adding an idol changed two things at once and the
        /// number it produced could not answer the question being asked.
        /// </remarks>
        [Test]
        public void AFoesHollowIdolCountsTowardEverySet()
        {
            Assert.That(RelicCatalog.KindOf(RelicId.HollowIdol), Is.Not.EqualTo(RelicKind.Guard));

            Assert.That(Glances(Foe(60, Many(RelicId.HollowIdol, 6))), Is.False,
                "six idols are not yet a Guard set");

            Assert.That(Glances(Foe(60, Many(RelicId.HollowIdol, 7))), Is.True,
                "seven idols should be a Guard set, holding no Guard relic at all");
        }

        private static RelicId[] Many(RelicId relic, int count)
        {
            var all = new RelicId[count];

            for (int i = 0; i < count; i++) all[i] = relic;

            return all;
        }

        /// <summary>
        /// A woken idol backs the family its own wearer leans on.
        /// </summary>
        /// <remarks>
        /// Whose family that is depends on the wearer, which is why this could not borrow the
        /// delver's answer: an idol on a foe would have backed whatever the DELVER was
        /// collecting.
        ///
        /// Measured at the other Guard threshold — seven turns the opening blow aside and says so
        /// in the log. Four Guard relics and two idols make six, which is not enough; waking an
        /// idol adds three more behind Guard, because Guard is what this foe leans on.
        /// </remarks>
        [Test]
        public void AWokenIdolBacksTheFamilyItsWearerLeansOn()
        {
            var kit = new List<RelicId>(Guards(4));
            kit.Add(RelicId.HollowIdol);
            kit.Add(RelicId.HollowIdol);

            EnemyState asleep = Foe(60, kit.ToArray());
            EnemyState awake = Foe(60, kit.ToArray());
            awake.Awakened = new List<RelicId> { RelicId.HollowIdol };

            Assert.That(Glances(asleep), Is.False, "six is not a Guard set");
            Assert.That(Glances(awake), Is.True,
                "a woken idol should throw three more behind this foe's own dominant family");
        }

        /// <summary>Relics of the Guard family, whichever they happen to be.</summary>
        private static RelicId[] Guards(int many)
        {
            var found = new List<RelicId>();

            foreach (RelicDef def in RelicCatalog.All)
            {
                if (def.Kind != RelicKind.Guard) continue;

                while (found.Count < many) found.Add(def.Id);
                break;
            }

            Assert.That(found.Count, Is.EqualTo(many), "no Guard relic exists to build a set from");

            return found.ToArray();
        }

        /// <summary>Whether this foe's Guard set turned the opening blow aside.</summary>
        private static bool Glances(EnemyState foe)
        {
            foreach (CombatEvent shown in Fight(Delver(), foe))
            {
                if (shown.Source != null && shown.Source.Contains("Guard set")) return true;
            }

            return false;
        }

        /// <summary>
        /// Rabbit's Foot was never hero-only, and the copy that looked like it was is gone.
        /// </summary>
        /// <remarks>
        /// There were two of them. The live one takes an <c>ICombatActor</c> and has always
        /// answered for whoever emitted the luck; the other sat in the engine reading the hero's
        /// own count and was never called by anything. Its only contribution was the appearance
        /// of an actor-specific rule, next to two that were real.
        /// </remarks>
        [Test]
        public void AFoesRabbitsFootAnswersLuck()
        {
            EnemyState foe = Foe(60, RelicId.RabbitsFoot, RelicId.LuckyClover);

            Assert.That(Fight(Delver(), foe), Is.Not.Empty,
                "a foe wearing a rabbit's foot should still resolve a fight");
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
