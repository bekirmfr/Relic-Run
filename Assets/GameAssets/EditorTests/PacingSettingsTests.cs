using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RelicRun.Core.Presentation;
using RelicRun.Editor.Importers;
using RelicRun.Game.Data;
using UnityEditor;
using UnityEngine;

namespace RelicRun.Tests.Editor
{
    /// <summary>
    /// The authored pacing and the pacing Core understands are the same set of numbers.
    /// </summary>
    /// <remarks>
    /// Two declarations of the same thing, in two assemblies, joined by a method that copies
    /// eleven fields across. Nothing makes them agree except somebody remembering, and the way
    /// they fail is quiet: add a field to <see cref="PacingRules"/>, forget the Inspector, and
    /// the number is authored nowhere and silently keeps its default forever.
    ///
    /// So the join is checked by name rather than by hand. A field on one and not the other is
    /// a failure whichever side it is on — an unauthored rule and an unread control are the same
    /// mistake seen from opposite ends.
    /// </remarks>
    [TestFixture]
    public class PacingSettingsTests
    {
        private static List<string> Fields(System.Type type)
        {
            var names = new List<string>();
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                names.Add(field.Name);
            }

            names.Sort();
            return names;
        }

        [Test]
        public void EveryPacingRuleIsAuthoredAndEveryAuthoredNumberIsARule()
        {
            List<string> core = Fields(typeof(PacingRules));
            List<string> authored = Fields(typeof(PresentationSettings));

            Assert.That(core, Is.Not.Empty);
            Assert.That(authored, Is.EqualTo(core),
                "PresentationSettings and PacingRules have drifted apart — a rule nobody can " +
                "author, or a control nothing reads");
        }

        /// <summary>Every authored number reaches Core, and unchanged.</summary>
        /// <remarks>
        /// The names matching is not the same as the values arriving: a copy that assigned
        /// <c>BusyStepMs</c> from <c>StepMs</c> would pass the check above and quietly show
        /// every long fight at the wrong speed.
        /// </remarks>
        [Test]
        public void TheAuthoredNumbersArriveUnchanged()
        {
            var settings = ScriptableObject.CreateInstance<PresentationSettings>();

            try
            {
                // Deliberately all different, so a field copied from its neighbour shows up.
                settings.StepMs = 111;
                settings.BusyStepMs = 222;
                settings.BusyAfterEvents = 33;
                settings.ReducedStepMs = 44;
                settings.FloorStepMs = 55;
                settings.PerTickMs = 666;
                settings.BusyPerTickMs = 777;
                settings.LongestWaitMs = 8888;
                settings.ShortestGapPercent = 99;
                settings.SpeedSteps = new[] { 1, 3, 9 };
                settings.SpeedShortensTheTickWait = false;

                PacingRules rules = settings.ToPacing();

                Assert.That(rules.StepMs, Is.EqualTo(111));
                Assert.That(rules.BusyStepMs, Is.EqualTo(222));
                Assert.That(rules.BusyAfterEvents, Is.EqualTo(33));
                Assert.That(rules.ReducedStepMs, Is.EqualTo(44));
                Assert.That(rules.FloorStepMs, Is.EqualTo(55));
                Assert.That(rules.PerTickMs, Is.EqualTo(666));
                Assert.That(rules.BusyPerTickMs, Is.EqualTo(777));
                Assert.That(rules.LongestWaitMs, Is.EqualTo(8888));
                Assert.That(rules.ShortestGapPercent, Is.EqualTo(99));
                Assert.That(rules.SpeedSteps, Is.EqualTo(new[] { 1, 3, 9 }));
                Assert.That(rules.SpeedShortensTheTickWait, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        /// <summary>The asset the game will actually read paces a fight sensibly.</summary>
        /// <remarks>
        /// Not asserted against the shipped defaults — the whole point of the asset is that
        /// somebody may tune it. Asserted against being nonsense: a beat of nothing, a cap
        /// shorter than the gap it caps, a speed control that cannot be pressed.
        /// </remarks>
        [Test]
        public void TheShippedAssetPacesAFight()
        {
            var content = AssetDatabase.LoadAssetAtPath<GameContent>(ContentPaths.GameContentAsset);
            Assert.That(content, Is.Not.Null, "run Tools > Relic Run > Import Content first");
            Assert.That(content.Presentation, Is.Not.Null, "nothing authored the pacing");

            PacingRules rules = content.Presentation.ToPacing();
            Pacing pacing = Pacing.For(20, false, 1, rules);

            Assert.That(pacing.StepMs, Is.GreaterThan(0));
            Assert.That(pacing.ShortestMs, Is.GreaterThan(0), "no gap at all is not a pace");
            Assert.That(pacing.ShortestMs, Is.LessThanOrEqualTo(pacing.StepMs));
            Assert.That(rules.LongestWaitMs, Is.GreaterThan(pacing.ShortestMs),
                "the cap is shorter than the floor, so every wait is the same length");
            Assert.That(rules.SpeedSteps, Is.Not.Empty, "the speed control cycles through nothing");
            Assert.That(pacing.NextSpeed(), Is.Not.EqualTo(0));
        }
    }
}
