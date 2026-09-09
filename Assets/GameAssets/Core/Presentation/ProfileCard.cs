using System.Collections.Generic;
using System.Globalization;
using RelicRun.Core.Meta;
using RelicRun.Core.Run;

namespace RelicRun.Core.Presentation
{
    /// <summary>One achievement, and whether this delver has it.</summary>
    public struct Badge
    {
        /// <summary>The key it is filed under, which is also how it is found again.</summary>
        public string Key;

        public string Name;

        public string What;

        public bool Earned;
    }

    /// <summary>Everything the profile shows.</summary>
    public struct ProfileCard
    {
        /// <summary>What the delver calls themselves, or empty if they never said.</summary>
        public string Name;

        public int Level;

        public int Xp;

        /// <summary>How far through this level, from nothing to one.</summary>
        public double Progress;

        /// <summary>Whether there is no level above this one.</summary>
        public bool Capped;

        /// <summary>
        /// How far to the next level, in whole per cent, or -1 at the ceiling.
        /// </summary>
        /// <remarks>
        /// A number rather than a sentence, because the sentence around it is translated. The
        /// minus is not a magic value being clever: at the top there IS no next level, and a
        /// screen showing "100% to level 21" would be promising one.
        /// </remarks>
        public int ToNext;

        /// <summary>Whether the supporter pack has been bought.</summary>
        public bool Supporter;

        public int Runs;

        public int Clears;

        /// <summary>Gold banked across every run, which is not the wallet.</summary>
        public int GoldLife;

        public int Crowns;

        public int Best;

        /// <summary>Every achievement, earned or not, in the order they are listed.</summary>
        /// <remarks>
        /// All of them, always. An achievement nobody can see is one nobody plays toward, and the
        /// source shows the locked ones with their conditions written out.
        /// </remarks>
        public IReadOnlyList<Badge> Badges;

        /// <summary>How many have been earned, so the screen can say so without counting.</summary>
        public int Won;
    }

    /// <summary>The profile, worked out.</summary>
    /// <remarks>
    /// The one screen that shows a delver everything they have done, which makes it the one place
    /// a number being wrong is most visible and least consequential. Every figure here is read
    /// from the save rather than derived a second way — <see cref="SaveState"/> already holds
    /// them, and a profile that computed its own totals would be a second opinion about the same
    /// facts.
    /// </remarks>
    public static class ProfileCards
    {
        /// <summary>What is shown where a percentage would be, when there is no level above.</summary>
        public const int AtTheTop = -1;

        public static ProfileCard Of(SaveState save, Preferences prefs)
        {
            if (save == null) save = new SaveState();

            int level = save.Level;
            bool capped = level >= Progression.MaxLevel;

            var badges = new List<Badge>(Achievements.All.Count);
            var won = 0;

            foreach (Achievement one in Achievements.All)
            {
                bool earned = one.Earned(save);
                if (earned) won++;

                badges.Add(new Badge
                {
                    Key = one.Key,
                    Name = one.Name,
                    What = one.Description,
                    Earned = earned,
                });
            }

            return new ProfileCard
            {
                Name = prefs != null && prefs.Name != null ? prefs.Name : string.Empty,
                Level = level,
                Xp = save.Xp,
                Progress = Progression.Progress(save.Xp),
                Capped = capped,

                // Rounded down, so a delver one point short of a level is never told they are
                // already there. Ninety-nine per cent that stays ninety-nine is honest; a
                // hundred that is not a level is not.
                ToNext = capped ? AtTheTop : (int)(Progression.Progress(save.Xp) * 100d),

                Supporter = prefs != null && prefs.Supporter,

                Runs = save.Runs,
                Clears = save.Clears,
                GoldLife = save.GoldLife,
                Crowns = save.VsCrowns,
                Best = save.Best,

                Badges = badges,
                Won = won,
            };
        }

        /// <summary>A number, spelled the same way everywhere.</summary>
        public static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
