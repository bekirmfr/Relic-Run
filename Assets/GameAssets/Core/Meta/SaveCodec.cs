using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RelicRun.Core.Meta
{
    /// <summary>
    /// What came back from a save, and what could not be read.
    /// </summary>
    /// <remarks>
    /// The count is the reason this is a struct rather than a bare <see cref="SaveState"/>. A
    /// codec that threw on a damaged save would lose a delver's whole profile to one bad line;
    /// one that skipped quietly would lose their gold and say nothing. Skipping and counting is
    /// the honest middle — the caller can log it, and everything that did parse is kept.
    /// </remarks>
    public struct Loaded
    {
        public SaveState Save;

        /// <summary>Lines that could not be read. Nonzero means something was lost.</summary>
        public int Damaged;

        /// <summary>The version the save was written by, or zero if it did not say.</summary>
        public int Version;
    }

    /// <summary>
    /// A delver's progression, as text and back.
    /// </summary>
    /// <remarks>
    /// The source keeps this in a dozen separate store keys and the port keeps it in one object,
    /// so somewhere the two shapes have to meet. That happens here rather than in a Unity DTO,
    /// for one reason: the failure this guards is a field added to <see cref="SaveState"/> and
    /// not added to the codec. Nothing breaks, nothing logs, and the delver's gold silently
    /// stops being saved — the exact shape of bug that a round-trip test still passes through,
    /// because a field neither side writes round-trips perfectly.
    ///
    /// So the field list is public and reflected over in the tests, and the codec is in Core
    /// where <c>dotnet test</c> can reach it.
    ///
    /// The format is a line per record, <c>key value</c>, split on the FIRST space. It is not
    /// JSON: Core has no engine and no parser, and hand-rolling a JSON reader to store nine
    /// integers and three lists would be more code to get wrong than the thing it stores. Lines
    /// with an unknown key are ignored rather than refused, so a save written by a later version
    /// still loads everything an earlier one understands.
    /// </remarks>
    public static class SaveCodec
    {
        /// <summary>The version this writes. Read back so a migration has something to key on.</summary>
        /// <remarks>
        /// Written from the first save rather than added when it is first needed, because it
        /// cannot be added retroactively: a save with no version is indistinguishable from a
        /// version-one save, and that ambiguity is permanent.
        /// </remarks>
        public const int Version = 1;

        public const string VersionKey = "v";

        /// <summary>
        /// Every field of <see cref="SaveState"/> this codec carries, by its own name.
        /// </summary>
        /// <remarks>
        /// Public so the tests can hold it against the type by reflection. That test is the point
        /// of this class existing in Core at all.
        /// </remarks>
        public static readonly IReadOnlyList<string> Fields = new[]
        {
            "Runs", "Clears", "GoldLife", "Gold", "Best", "Xp", "Unlocked", "VsCrowns", "Sparks",
            "Scores", "Arts", "DailyBest", "DailyDone", "Seen",
        };

        /// <summary>A delver's progression, written out.</summary>
        /// <remarks>
        /// Deterministic, in this order, every time. A save that reordered itself between writes
        /// would make every diff of two saves unreadable, and reading a diff of two saves is how
        /// a progression bug gets found.
        /// </remarks>
        public static string Write(SaveState save)
        {
            if (save == null) return string.Empty;

            var text = new StringBuilder();

            Line(text, VersionKey, Version);

            Line(text, "runs", save.Runs);
            Line(text, "clears", save.Clears);
            Line(text, "goldLife", save.GoldLife);
            Line(text, "gold", save.Gold);
            Line(text, "best", save.Best);
            Line(text, "xp", save.Xp);
            Line(text, "unlocked", save.Unlocked);
            Line(text, "vsCrowns", save.VsCrowns);
            Line(text, "sparks", save.Sparks);

            foreach (ScoreRow row in save.Scores)
            {
                // The name goes last so that a delver who names themselves with the separator
                // costs nothing to read back. It is escaped anyway — trailing is a convenience,
                // not the guarantee.
                Line(text, "score", Number(row.Score) + "|" + Number(row.Floor) + "|" +
                                    Number(row.Kills) + "|" + Number(row.Relics) + "|" +
                                    row.Stamp.ToString(CultureInfo.InvariantCulture) + "|" +
                                    Lines.Escaped(row.Name));
            }

            foreach (string art in save.Arts) Line(text, "art", Lines.Escaped(art));

            foreach (KeyValuePair<uint, int> day in save.DailyBest)
            {
                Line(text, "daily", day.Key.ToString(CultureInfo.InvariantCulture) + "|" +
                                    Number(day.Value));
            }

            foreach (uint day in save.DailyDone)
            {
                Line(text, "done", day.ToString(CultureInfo.InvariantCulture));
            }

            foreach (int species in save.Seen) Line(text, "seen", Number(species));

            return text.ToString();
        }

        /// <summary>
        /// A delver's progression, read back.
        /// </summary>
        /// <remarks>
        /// Never throws and never returns null. Empty text is a delver who has not played, which
        /// is a first run rather than an error, and a torn file is a first run with some of it
        /// back — both are better than a screen that cannot open.
        /// </remarks>
        public static Loaded Read(string text)
        {
            Reading reading = Lines.Of(text);

            var loaded = new Loaded { Save = new SaveState(), Version = 0, Damaged = reading.Damaged };

            foreach (Record record in reading.Records)
            {
                if (!Take(ref loaded, record.Key, record.Value)) loaded.Damaged++;
            }

            // A hall below the first is not a save state, it is a delver locked out of their own
            // game. The default is the floor, and a torn line cannot take it away.
            if (loaded.Save.Unlocked < 1) loaded.Save.Unlocked = 1;

            return loaded;
        }

        /// <summary>One record, into the save. False when the line could not be read.</summary>
        /// <remarks>
        /// An unrecognised key is true, not false: it is a record from a version that knows
        /// something this one does not, and refusing it would report damage where there is none.
        /// </remarks>
        private static bool Take(ref Loaded loaded, string key, string value)
        {
            SaveState save = loaded.Save;

            switch (key)
            {
                case VersionKey: return Int(value, out loaded.Version);

                case "runs": return Int(value, out save.Runs);
                case "clears": return Int(value, out save.Clears);
                case "goldLife": return Int(value, out save.GoldLife);
                case "gold": return Int(value, out save.Gold);
                case "best": return Int(value, out save.Best);
                case "xp": return Int(value, out save.Xp);
                case "unlocked": return Int(value, out save.Unlocked);
                case "vsCrowns": return Int(value, out save.VsCrowns);
                case "sparks": return Int(value, out save.Sparks);

                case "score": return Score(save, value);
                case "art": save.Arts.Add(Lines.Plain(value)); return true;
                case "daily": return Daily(save, value);

                case "done":
                {
                    uint day;
                    if (!Seed(value, out day)) return false;
                    save.DailyDone.Add(day);
                    return true;
                }

                case "seen":
                {
                    int species;
                    if (!Int(value, out species)) return false;
                    save.Seen.Add(species);
                    return true;
                }

                default: return true;
            }
        }

        private static bool Score(SaveState save, string value)
        {
            string[] parts = value.Split('|');
            if (parts.Length != 6) return false;

            var row = new ScoreRow();

            if (!Int(parts[0], out row.Score)) return false;
            if (!Int(parts[1], out row.Floor)) return false;
            if (!Int(parts[2], out row.Kills)) return false;
            if (!Int(parts[3], out row.Relics)) return false;

            if (!long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out row.Stamp)) return false;

            row.Name = Lines.Plain(parts[5]);

            save.Scores.Add(row);
            return true;
        }

        private static bool Daily(SaveState save, string value)
        {
            string[] parts = value.Split('|');
            if (parts.Length != 2) return false;

            uint day;
            int score;

            if (!Seed(parts[0], out day)) return false;
            if (!Int(parts[1], out score)) return false;

            save.DailyBest[day] = score;
            return true;
        }

        private static void Line(StringBuilder text, string key, int value)
        {
            Line(text, key, Number(value));
        }

        private static void Line(StringBuilder text, string key, string value)
        {
            Lines.Put(text, key, value);
        }

        private static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static bool Int(string value, out int number)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out number);
        }

        /// <summary>A day's seed, which is unsigned and comes from the date.</summary>
        private static bool Seed(string value, out uint day)
        {
            return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out day);
        }
    }
}
