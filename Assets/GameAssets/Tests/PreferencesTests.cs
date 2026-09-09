using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Core.Meta;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// What a delver chose, kept and read back.
    /// </summary>
    /// <remarks>
    /// Settings fail more quietly than progression does. Nothing is lost that a delver can point
    /// at — the game is simply loud again, or in the wrong language again, and the only report is
    /// that it feels broken.
    /// </remarks>
    [TestFixture]
    public class PreferencesTests
    {
        /// <summary>Every setting is carried by the codec.</summary>
        /// <remarks>
        /// The same reflection gate the save has, for the same reason: a setting added and not
        /// carried does not fail, it just never sticks.
        /// </remarks>
        [Test]
        public void EverySettingIsCarried()
        {
            var carried = new List<string>(PreferenceCodec.Fields);

            foreach (FieldInfo field in typeof(Preferences).GetFields(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.That(carried, Does.Contain(field.Name),
                    "Preferences." + field.Name + " is not in the codec — it will never stick");
            }

            foreach (string name in carried)
            {
                Assert.That(typeof(Preferences).GetField(name), Is.Not.Null,
                    "the codec claims to carry " + name + ", which Preferences does not have");
            }
        }

        /// <summary>Everything chosen survives the trip.</summary>
        [Test]
        public void WhatWasChosenComesBack()
        {
            var chose = new Preferences
            {
                Name = "Bekir", Language = "tr", Muted = true, Supporter = true,
                Tier = 7, VersusHall = 3,
            };

            Preferences back = PreferenceCodec.Read(PreferenceCodec.Write(chose));

            Assert.That(back.Name, Is.EqualTo("Bekir"));
            Assert.That(back.Language, Is.EqualTo("tr"));
            Assert.That(back.Muted, Is.True);
            Assert.That(back.Supporter, Is.True);
            Assert.That(back.Tier, Is.EqualTo(7));
            Assert.That(back.VersusHall, Is.EqualTo(3));
        }

        /// <summary>
        /// A name that was never given and a name that was cleared are different.
        /// </summary>
        /// <remarks>
        /// The difference is a screen. Null opens the welcome card; empty is a delver who saw it
        /// and left the box blank, and showing it to them again every launch would be the game
        /// refusing to take no for an answer.
        /// </remarks>
        [Test]
        public void NeverNamedAndNamedNothingAreNotTheSame()
        {
            Preferences never = PreferenceCodec.Read(PreferenceCodec.Write(new Preferences()));

            Assert.That(never.Name, Is.Null);
            Assert.That(never.NeverAsked, Is.True);

            Preferences blank = PreferenceCodec.Read(
                PreferenceCodec.Write(new Preferences { Name = string.Empty }));

            Assert.That(blank.Name, Is.Empty);
            Assert.That(blank.NeverAsked, Is.False, "they were asked, and they said nothing");
        }

        /// <summary>A name from the scheme the source abandoned is cleared and asked again.</summary>
        [Test]
        public void AnAutoNamedDelverIsAskedAgain()
        {
            Preferences back = PreferenceCodec.Read(
                "name " + PreferenceCodec.AutoNamed + "0042\n");

            Assert.That(back.Name, Is.Null, "cleared to null, not to empty, so they are asked");
            Assert.That(back.NeverAsked, Is.True);

            Assert.That(PreferenceCodec.Read("name Bekir\n").Name, Is.EqualTo("Bekir"),
                "and a real name is left alone");
        }

        /// <summary>A name survives whatever is in it, and by the same escaping the board uses.</summary>
        /// <remarks>
        /// The reason the line format is shared rather than written twice. If these two ever
        /// escaped differently, the delver whose name has a backslash in it would see one spelling
        /// on the title and another on the board.
        /// </remarks>
        [Test]
        public void ANameIsEscapedTheSameWayTheBoardEscapesIt()
        {
            foreach (string name in new[]
            {
                "a|b", "line\nbreak", "back\\slash", "trailing\\", "Ünlü Kâşif", "🗡️",
            })
            {
                Preferences back = PreferenceCodec.Read(
                    PreferenceCodec.Write(new Preferences { Name = name }));

                Assert.That(back.Name, Is.EqualTo(name));

                var save = new SaveState();
                save.Scores.Add(new ScoreRow { Name = name });

                Assert.That(SaveCodec.Read(SaveCodec.Write(save)).Save.Scores[0].Name,
                    Is.EqualTo(back.Name), "the two spell [" + name + "] differently");
            }
        }

        /// <summary>Nothing saved is a delver with the defaults.</summary>
        [Test]
        public void NothingSavedIsTheDefaults()
        {
            foreach (string nothing in new[] { null, "", "\n" })
            {
                Preferences prefs = PreferenceCodec.Read(nothing);

                Assert.That(prefs, Is.Not.Null);
                Assert.That(prefs.NeverAsked, Is.True);
                Assert.That(prefs.Muted, Is.False, "the game starts audible");
                Assert.That(prefs.Language, Is.Null, "unset, so the device gets asked");
                Assert.That(prefs.Tier, Is.EqualTo(1));
                Assert.That(prefs.VersusHall, Is.EqualTo(1));
            }
        }

        /// <summary>Neither remembered hall can be below the first.</summary>
        [Test]
        public void ARememberedHallIsNeverBelowTheFirst()
        {
            Preferences prefs = PreferenceCodec.Read("tier 0\nvsHall -2\n");

            Assert.That(prefs.Tier, Is.EqualTo(1));
            Assert.That(prefs.VersusHall, Is.EqualTo(1));

            Assert.That(PreferenceCodec.Read("tier notanumber\n").Tier, Is.EqualTo(1),
                "an unreadable one is the default, not a zero");
        }

        /// <summary>What the delver chose wins, if the game still has it.</summary>
        [Test]
        public void AChosenLanguageWins()
        {
            Assert.That(Languages.Pick("tr", new[] { "en-US" }, Shipped), Is.EqualTo("tr"));

            Assert.That(Languages.Pick("de", new[] { "fr-CA" }, Shipped), Is.EqualTo("fr"),
                "a language this build does not ship falls through to the device");
        }

        /// <summary>
        /// With nothing chosen, the device is asked, best first.
        /// </summary>
        /// <remarks>
        /// The step that matters most and is easiest to leave out, because it only ever shows up
        /// for delvers who do not read English — who are exactly the ones least able to go and
        /// find the language setting.
        /// </remarks>
        [Test]
        public void TheDeviceIsAskedInOrder()
        {
            Assert.That(Languages.Pick(null, new[] { "de", "ja", "fr" }, Shipped),
                Is.EqualTo("ja"), "the first one the game actually ships");

            Assert.That(Languages.Pick(null, new[] { "pt-BR", "es-419" }, Shipped),
                Is.EqualTo("es"), "a region is not a language");
        }

        /// <summary>A tag's region is dropped, whatever case it arrives in.</summary>
        /// <remarks>
        /// The uppercase case is not decoration. Lowercasing under Turkish rules turns "I" into a
        /// dotless "ı", so a device asking for "IT" would match nothing at all — and a Turkish
        /// delver is precisely who would be running those rules.
        /// </remarks>
        [Test]
        public void ATagIsReducedToItsLanguage()
        {
            Assert.That(Languages.Base("zh-Hans-CN"), Is.EqualTo("zh"));
            Assert.That(Languages.Base("EN"), Is.EqualTo("en"));
            Assert.That(Languages.Base("fr"), Is.EqualTo("fr"));
            Assert.That(Languages.Base(""), Is.Empty);
            Assert.That(Languages.Base(null), Is.Empty);

            Assert.That(Languages.Pick(null, new[] { "TR-tr" }, Shipped), Is.EqualTo("tr"));
        }

        /// <summary>English when nothing else fits, and never nothing.</summary>
        [Test]
        public void EnglishIsWhatIsLeft()
        {
            Assert.That(Languages.Pick(null, null, Shipped), Is.EqualTo("en"));
            Assert.That(Languages.Pick(null, new string[0], Shipped), Is.EqualTo("en"));
            Assert.That(Languages.Pick("de", new[] { "de-AT" }, Shipped), Is.EqualTo("en"));
            Assert.That(Languages.Pick(null, new[] { "de" }, new string[0]), Is.EqualTo("en"));
        }

        /// <summary>
        /// The languages the game actually ships, read from the strings corpus.
        /// </summary>
        /// <remarks>
        /// Read rather than typed, because a list typed here would agree only with itself: a
        /// translation dropped from the build would leave every case in this file passing against
        /// a language that no longer exists, which is precisely the case the picker is for.
        /// </remarks>
        private static string[] Shipped
        {
            get
            {
                if (_shipped != null) return _shipped;

                var found = new List<string>();

                foreach (JToken language in Corpus.Object("strings.json")["languages"])
                {
                    found.Add(language.Value<string>());
                }

                Assert.That(found, Does.Contain(Languages.Fallback),
                    "the fallback language is not among the ones the game ships");

                _shipped = found.ToArray();
                return _shipped;
            }
        }

        private static string[] _shipped;
    }
}
