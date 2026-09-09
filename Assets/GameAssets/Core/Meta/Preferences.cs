using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RelicRun.Core.Meta
{
    /// <summary>
    /// What a delver has chosen, as distinct from what they have earned.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="SaveState"/> on purpose, and the line between them is not
    /// arbitrary: progression is what the game gives you and settings are what you tell the game.
    /// Nothing in the rules reads anything here — a fight does not care what language it is
    /// narrated in — and keeping them apart is what lets a delver reset their progress without
    /// losing their name, and change their language without touching their gold.
    ///
    /// The source keeps both in the same key-value store and tells them apart by remembering to.
    /// </remarks>
    public sealed class Preferences
    {
        /// <summary>
        /// What the delver calls themselves, or null if they have never said.
        /// </summary>
        /// <remarks>
        /// Null and empty are different here and the difference is a screen: null is a delver who
        /// has not been asked yet, and the source opens on the welcome card for exactly that.
        /// Empty is a delver who was asked and cleared the box.
        /// </remarks>
        public string Name;

        /// <summary>The locale to read in, or null to work it out from the device.</summary>
        public string Language;

        public bool Muted;

        /// <summary>Whether the supporter pack has been bought.</summary>
        public bool Supporter;

        /// <summary>The hall the delve screen opens on. Remembered, not earned.</summary>
        public int Tier = 1;

        /// <summary>The arena hall the staging screen opens on.</summary>
        public int VersusHall = 1;

        /// <summary>Whether this delver has never been asked their name.</summary>
        /// <remarks>
        /// The source's <c>firstLaunch</c>, and it is asked of the name rather than kept as a
        /// flag of its own — a flag would be a second thing to keep true, and the two would
        /// disagree the first time a save was restored from somewhere.
        /// </remarks>
        public bool NeverAsked
        {
            get { return Name == null; }
        }
    }

    /// <summary>
    /// A delver's choices, as text and back.
    /// </summary>
    /// <remarks>
    /// The same line format as the save, through the same <see cref="Lines"/>, so a name written
    /// here and a name written on the board are escaped identically. That is the whole reason the
    /// format is shared: two escapers would be two chances to mangle the one field a delver types
    /// themselves, and only the delver with a backslash in their name would ever find out.
    /// </remarks>
    public static class PreferenceCodec
    {
        /// <summary>
        /// A name the source stored under an auto-naming scheme it no longer uses.
        /// </summary>
        /// <remarks>
        /// Ported because the alternative is a delver arriving with a name that reads like a
        /// bug — the source clears it and asks again, and so does this. Kept as the source spells
        /// it, prefix and all, since the point is to recognise what an older build wrote.
        /// </remarks>
        public const string AutoNamed = AutoNamedPrefix + "#";

        /// <summary>
        /// The prefix alone, which is what a score row is checked against.
        /// </summary>
        /// <remarks>
        /// The source tests the two places differently — the delver's own name against the whole
        /// marker, a board row against the prefix — and both are here rather than spelled twice,
        /// because a board keeps whatever name was current when a run was banked and the two
        /// checks have to agree about what an auto-name looks like.
        /// </remarks>
        public const string AutoNamedPrefix = "autoNameWord";

        /// <summary>Every field of <see cref="Preferences"/> this codec carries, by name.</summary>
        /// <remarks>
        /// Reflected over in the tests, for the reason <see cref="SaveCodec.Fields"/> is: a
        /// setting added and not carried does not fail, it just never sticks, and a delver who
        /// mutes the game and finds it loud again next time has nothing to report but a feeling.
        /// </remarks>
        public static readonly IReadOnlyList<string> Fields = new[]
        {
            "Name", "Language", "Muted", "Supporter", "Tier", "VersusHall",
        };

        public static string Write(Preferences prefs)
        {
            if (prefs == null) return string.Empty;

            var text = new StringBuilder();

            // Absent rather than empty when never set, because null and empty mean different
            // things here and a written empty line would turn the first into the second.
            if (prefs.Name != null) Lines.Put(text, "name", Lines.Escaped(prefs.Name));
            if (prefs.Language != null) Lines.Put(text, "lang", Lines.Escaped(prefs.Language));

            Lines.Put(text, "mute", prefs.Muted ? "1" : "0");
            Lines.Put(text, "supporter", prefs.Supporter ? "1" : "0");
            Lines.Put(text, "tier", prefs.Tier.ToString(CultureInfo.InvariantCulture));
            Lines.Put(text, "vsHall", prefs.VersusHall.ToString(CultureInfo.InvariantCulture));

            return text.ToString();
        }

        /// <summary>A delver's choices, read back. Never throws, never returns null.</summary>
        public static Preferences Read(string text)
        {
            var prefs = new Preferences();

            foreach (Record record in Lines.Of(text).Records)
            {
                switch (record.Key)
                {
                    case "name": prefs.Name = Lines.Plain(record.Value); break;
                    case "lang": prefs.Language = Lines.Plain(record.Value); break;
                    case "mute": prefs.Muted = record.Value == "1"; break;
                    case "supporter": prefs.Supporter = record.Value == "1"; break;
                    case "tier": prefs.Tier = Whole(record.Value); break;
                    case "vsHall": prefs.VersusHall = Whole(record.Value); break;
                }
            }

            // A name from the scheme the source abandoned is not a name. Cleared to null rather
            // than to empty, so the delver is ASKED again instead of being left anonymous.
            if (prefs.Name != null && prefs.Name.StartsWith(AutoNamed)) prefs.Name = null;

            // Neither hall can be below the first. A zero is what an unreadable line leaves, and
            // a screen opened on hall zero has nothing to show.
            if (prefs.Tier < 1) prefs.Tier = 1;
            if (prefs.VersusHall < 1) prefs.VersusHall = 1;

            return prefs;
        }

        /// <summary>A number, or nothing.</summary>
        /// <remarks>
        /// Unlike the save, a torn setting is not counted. Nothing here is earned, so the honest
        /// answer to an unreadable one is the default and no fuss — reporting damage over a
        /// remembered hall number would train a delver to ignore the message that matters.
        ///
        /// It used to take a fallback to return instead of zero, and that fallback was
        /// unobservable: every number this reads is clamped below, and the clamp already lands on
        /// the same value. Mutation testing found it — the mutant that returned zero survived
        /// everything, because it could not be told apart. Two mechanisms guaranteeing one rule
        /// is one mechanism and one decoration, and the decoration is the one that gets trusted
        /// by mistake.
        /// </remarks>
        private static int Whole(string value)
        {
            int number;

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out number) ? number : 0;
        }
    }
}
