using System.Collections.Generic;
using System.Globalization;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Meta;
using RelicRun.Core.Run;

namespace RelicRun.Core.Presentation
{
    /// <summary>One hall's tile in the grid.</summary>
    public struct HallTile
    {
        public int Tier;

        /// <summary>Whether this hall can be delved at all.</summary>
        public bool Unlocked;

        /// <summary>Whether the delver has been past it. Cleared implies unlocked.</summary>
        public bool Cleared;

        /// <summary>Whether this is the one the detail panel is describing.</summary>
        public bool Chosen;

        /// <summary>The word under the number, or empty for a hall still sealed.</summary>
        public string Tag;
    }

    /// <summary>One line of the selected hall's numbers.</summary>
    public struct HallStat
    {
        public string Label;
        public string Value;
    }

    /// <summary>
    /// What the panel says about the hall the delver is looking at.
    /// </summary>
    /// <remarks>
    /// A sealed hall gets a different shape rather than a greyed-out version of the same one: no
    /// numbers, no relics, no name — the point of sealing it is that the delver does not know
    /// what is down there, and a panel that showed the boss's hit points and called it a mystery
    /// would be telling them anyway.
    /// </remarks>
    public struct HallDetail
    {
        public int Tier;

        /// <summary>Whether this hall is still shut. Everything below reads differently if so.</summary>
        public bool Sealed;

        public string Title;

        public string Lore;

        public IReadOnlyList<HallStat> Stats;

        /// <summary>
        /// What the hall's king carries, by id.
        /// </summary>
        /// <remarks>
        /// Ids rather than names, because a name has a language and Core does not get to pick
        /// one. The screen looks each up in the delver's own locale.
        /// </remarks>
        public IReadOnlyList<RelicId> BossRelics;

        /// <summary>Whether pressing the button starts a delve.</summary>
        public bool CanDelve;
    }

    /// <summary>Everything the dungeon list shows.</summary>
    public struct LevelsCard
    {
        public IReadOnlyList<HallTile> Halls;

        /// <summary>Which hall the panel is describing.</summary>
        public int Chosen;

        public HallDetail Detail;
    }

    /// <summary>
    /// The dungeon list, worked out.
    /// </summary>
    /// <remarks>
    /// Two questions, and they are not the same one. Which halls exist and what state each is in
    /// is cheap and is asked of every hall. What is actually DOWN one of them is expensive — it
    /// builds a pack of foes — and is asked only of the hall being looked at.
    ///
    /// The captions are English, for the reason <see cref="TitleCard"/>'s are: the source writes
    /// them as literals in its markup and they never pass through its translation function. The
    /// relic names are the exception and are deliberately not here — those DO come from the
    /// translator, so the screen looks them up and Core hands over ids.
    /// </remarks>
    public static class LevelsCards
    {
        /// <summary>What a hall the delver has been past says under its number.</summary>
        public const string ClearedTag = "CLEARED";

        /// <summary>And one they may enter but have not finished.</summary>
        public const string OpenTag = "OPEN";

        /// <summary>The king every hall's deepest floor belongs to.</summary>
        public const string KingName = "The Hoard-King";

        /// <summary>What is said where a relic list would be, when the king carries none.</summary>
        public const string NoRelics = "none — raw strength only";

        /// <summary>What the button says when the hall can be entered.</summary>
        /// <remarks>
        /// The arrow is a plain U+203A, which both shipped faces cover. That is worth being sure
        /// of rather than assuming: the arrows in the TRANSLATED strings are inside the range
        /// Locale.Clean strips, so they never reach a face at all — and a literal like this one
        /// is not cleaned, so its arrow is really drawn. The tests hold it against the face.
        /// </remarks>
        public const string DelveLabel = "DELVE ›";

        /// <summary>And what it says when it cannot.</summary>
        public const string SealedLabel = "SEALED";

        /// <summary>
        /// The seed the boss preview is built from. The source's number.
        /// </summary>
        /// <remarks>
        /// It changes nothing this screen shows, and that is worth writing down rather than
        /// leaving for somebody to rediscover. The pack generator does not roll for the king: one
        /// species owns each floor, and its hit points, attack, armour and speed all come out of
        /// the floor's own formulas. Feeding it any seed at all produces species 9 with 126 hit
        /// points. What the roll decides is which species fill the guard slots BEHIND him, and
        /// this screen does not draw them.
        ///
        /// So the constant is the source's call preserved, not a contract — mutation testing
        /// confirmed a mutant that seeds from the hall's own number cannot be told apart from
        /// this, and there is no honest test that kills it. It stops being cosmetic the moment a
        /// preview shows anything the roll actually decides, which is why it is named rather than
        /// inlined.
        ///
        /// It is NOT the seed the delve runs on. That comes from the run, and the pack a delver
        /// actually meets is rolled then.
        /// </remarks>
        public const uint PreviewSeed = 7;

        /// <summary>
        /// The dungeon list, for this delver, looking at this hall.
        /// </summary>
        /// <param name="chosen">
        /// Which hall to describe. Clamped into the catalog, and zero means "the deepest one they
        /// have opened" — which is where the source puts the cursor when the screen opens.
        /// </param>
        public static LevelsCard Of(SaveState save, int chosen)
        {
            if (save == null) save = new SaveState();

            int frontier = Career.Frontier(save);
            int count = DungeonCatalog.All.Count;

            // Career.Frontier already floors itself at one, so a delver who has unlocked nothing
            // still lands on hall one here. There WAS a guard below this line for that; mutation
            // testing showed it could not fire, because the only route to a value under one is
            // `chosen <= 0`, and that route reads the frontier instead. Dead, and deleted — an
            // unreachable guard is one somebody later trusts.
            int at = chosen <= 0 ? frontier : chosen;
            if (at > count) at = count;

            var tiles = new List<HallTile>(count);

            for (var tier = 1; tier <= count; tier++)
            {
                bool unlocked = tier <= frontier;
                bool cleared = tier < frontier;

                tiles.Add(new HallTile
                {
                    Tier = tier,
                    Unlocked = unlocked,
                    Cleared = cleared,
                    Chosen = tier == at,
                    Tag = cleared ? ClearedTag : unlocked ? OpenTag : string.Empty,
                });
            }

            return new LevelsCard
            {
                Halls = tiles,
                Chosen = at,
                Detail = Describe(save, at, frontier),
            };
        }

        /// <summary>What one hall is, or what little is known about it.</summary>
        private static HallDetail Describe(SaveState save, int tier, int frontier)
        {
            if (tier > frontier)
            {
                return new HallDetail
                {
                    Tier = tier,
                    Sealed = true,
                    Title = "DUNGEON " + Number(tier),
                    Lore = "Sealed. Clear Dungeon " + Number(tier - 1) +
                           " to learn what waits here.",
                    Stats = new HallStat[0],
                    BossRelics = new RelicId[0],
                    CanDelve = false,
                };
            }

            DungeonDef hall = DungeonCatalog.Get(tier);
            EnemyState king = King(hall);

            double multiplier = hall.Multiplier;

            return new HallDetail
            {
                Tier = tier,
                Sealed = false,
                Title = hall.Name.ToUpperInvariant(),
                Lore = hall.Lore,
                BossRelics = hall.BossRelics,
                CanDelve = true,

                Stats = new[]
                {
                    // Hit points and attack carry the hall's multiplier; armour and speed do not,
                    // which is the source's arrangement and not a rounding of it. A deeper hall
                    // makes its king hit harder and last longer without making it nimbler.
                    Stat("HP", Scaled(king.MaxHp, multiplier)),
                    Stat("ATK", Scaled(king.Atk, multiplier)),
                    Stat("DEF", Number(king.Armor)),
                    Stat("SPD", Number(king.Spd)),

                    // Two multipliers, spelled two different ways, and both spellings are the
                    // source's. The score multiplier is a property of the hall and reads as one:
                    // 1.1, not 1.10. The experience rate is a rate and always shows its
                    // hundredths, because the difference between 0.80 and 0.8 on a screen full
                    // of rates is a delver wondering which one they misread.
                    Stat("SCORE", Times(Rounded(multiplier))),
                    Stat("FOR LV", Number(hall.Level)),
                    Stat("XP RATE", Times(Rate(Progression.RewardMultiplier(tier, frontier)))),
                },
            };
        }

        /// <summary>
        /// The king waiting on the deepest floor, as a preview.
        /// </summary>
        /// <remarks>
        /// Built from the fixed seed and WITHOUT the hall's multiplier, because the multiplier is
        /// applied to the two numbers that carry it rather than to the whole pack. Handing the
        /// generator a multiplied config would scale the king's gold as well, which nothing on
        /// this screen shows and which would then disagree with the delve itself.
        /// </remarks>
        private static EnemyState King(DungeonDef hall)
        {
            var preview = new DungeonConfig
            {
                Multiplier = 1.0,
                BossRelics = hall.BossRelics,
                GhoolemBoss = hall.GhoolemBoss,
            };

            List<EnemyState> pack = EnemyPackGenerator.Build(
                EnemyPackGenerator.MaxFloor, new Mulberry32(PreviewSeed), preview);

            return pack[pack.Count - 1];
        }

        private static HallStat Stat(string label, string value)
        {
            return new HallStat { Label = label, Value = value };
        }

        /// <summary>A number the hall's multiplier applies to, rounded the way the game rounds.</summary>
        private static string Scaled(int value, double multiplier)
        {
            return Number(JsMath.RoundToInt(value * multiplier));
        }

        private static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A multiplier as the source prints it: rounded to two places, then written out.
        /// </summary>
        /// <remarks>
        /// Two steps, and they are not the same step. The ROUNDING is the source's arithmetic —
        /// <c>Math.round(M * 100) / 100</c>, half-up, which is what JsMath.Round is for. The
        /// WRITING is what JavaScript does to a number on its way into a string: as few digits as
        /// say it exactly, so 1.1 is "1.1" and 1 is "1".
        ///
        /// It used to round and then format with "0.##", which rounds again — two mechanisms for
        /// one rule, and mutation testing found it the way it always finds them: the mutant that
        /// removed the first one survived, because the second was quietly doing its job.
        /// </remarks>
        private static string Rounded(double value)
        {
            double two = JsMath.Round(value * 100d) / 100d;

            return two.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A rate as the source prints it: always two decimals.</summary>
        private static string Rate(double value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A multiplier with its sign in front.
        /// </summary>
        /// <remarks>
        /// A real multiplication sign rather than a lowercase x. It is what the source uses, it
        /// is inside the range the pixel face covers, and the tests hold every line on this screen
        /// against that face — raw, since none of these go through the translator that would
        /// otherwise strip anything it could not draw.
        /// </remarks>
        private static string Times(string value)
        {
            return "×" + value;
        }
    }
}
