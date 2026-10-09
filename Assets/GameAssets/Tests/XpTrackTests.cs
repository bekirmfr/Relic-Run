using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Presentation;
using RelicRun.Core.Run;

namespace RelicRun.Tests
{
    /// <summary>The path the experience bar takes at the end of a run.</summary>
    [TestFixture]
    public class XpTrackTests
    {
        /// <summary>Experience that buys exactly <paramref name="level"/>, with nothing over.</summary>
        private static int Floor(int level)
        {
            int xp = 0;
            for (int i = 2; i <= level; i++) xp += Progression.Need(i);
            return xp;
        }

        [Test]
        public void ARunThatCrossesNothingIsOneStretch()
        {
            int start = Floor(3) + 10;
            List<XpLeg> legs = XpTracks.Of(start, 50);

            Assert.That(legs.Count, Is.EqualTo(1));
            Assert.That(legs[0].Level, Is.EqualTo(3));
            Assert.That(legs[0].From, Is.EqualTo(Progression.Progress(start)));
            Assert.That(legs[0].To, Is.EqualTo(Progression.Progress(start + 50)));
            Assert.That(legs[0].Reaches, Is.EqualTo(0));
            Assert.That(legs[0].Unlocks, Is.Empty);
        }

        /// <summary>
        /// Two levels in one run is two fills and a part, each fill announcing the level it
        /// reaches — not one bar sweeping past a label that has stopped being true.
        /// </summary>
        [Test]
        public void EachLevelCrossedIsItsOwnFill()
        {
            int start = Floor(2) + 100;
            int earned = Floor(4) + 7 - start;
            List<XpLeg> legs = XpTracks.Of(start, earned);

            Assert.That(legs.Count, Is.EqualTo(3));

            Assert.That(legs[0].Level, Is.EqualTo(2));
            Assert.That(legs[0].To, Is.EqualTo(1d));
            Assert.That(legs[0].Reaches, Is.EqualTo(3));

            Assert.That(legs[1].Level, Is.EqualTo(3));
            Assert.That(legs[1].From, Is.EqualTo(0d));
            Assert.That(legs[1].Reaches, Is.EqualTo(4));

            Assert.That(legs[2].Level, Is.EqualTo(4));
            Assert.That(legs[2].From, Is.EqualTo(0d));
            Assert.That(legs[2].Reaches, Is.EqualTo(0));
        }

        /// <summary>Level three opens the Daily, and the bar arriving there says so.</summary>
        [Test]
        public void ArrivingHandsOverThatLevelsPerk()
        {
            List<XpLeg> legs = XpTracks.Of(Floor(3) - 1, 1);

            Assert.That(legs[0].Reaches, Is.EqualTo(3));
            Assert.That(legs[0].Unlocks[0].Kind, Is.EqualTo(UnlockKind.Perk));
            Assert.That(legs[0].Unlocks[0].Text, Is.EqualTo(Progression.PerkDescription(3)));
            Assert.That(legs[0].Unlocks[0].Text, Does.Contain("Daily Delve"));
        }

        /// <summary>
        /// Relics and wardrobe pieces are not granted by anything yet; when they are, they ride
        /// the same arrival as the perk.
        /// </summary>
        [Test]
        public void MoreUnlocksFollowThePerk()
        {
            List<XpLeg> legs = XpTracks.Of(Floor(3) - 1, 1, level => new[]
            {
                new Unlock { Kind = UnlockKind.Wardrobe, Text = "hood " + level, Key = "hood" },
            });

            Assert.That(legs[0].Unlocks.Count, Is.EqualTo(2));
            Assert.That(legs[0].Unlocks[1].Kind, Is.EqualTo(UnlockKind.Wardrobe));
            Assert.That(legs[0].Unlocks[1].Text, Is.EqualTo("hood 3"));
        }

        [Test]
        public void TheCeilingIsAFullBarGoingNowhere()
        {
            List<XpLeg> legs = XpTracks.Of(Floor(Progression.MaxLevel) + 500, 900);

            Assert.That(legs.Count, Is.EqualTo(1));
            Assert.That(legs[0].Level, Is.EqualTo(Progression.MaxLevel));
            Assert.That(legs[0].From, Is.EqualTo(1d));
            Assert.That(legs[0].To, Is.EqualTo(1d));
        }

        [Test]
        public void NothingEarnedStandsStill()
        {
            int start = Floor(5) + 40;
            List<XpLeg> legs = XpTracks.Of(start, 0);

            Assert.That(legs.Count, Is.EqualTo(1));
            Assert.That(legs[0].From, Is.EqualTo(legs[0].To));
        }
    }
}
