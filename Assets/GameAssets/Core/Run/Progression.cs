using System;
using System.Collections.Generic;
using System.Text;
using RelicRun.Core.Determinism;

namespace RelicRun.Core.Run
{
    /// <summary>Everything a hero level grants.</summary>
    public readonly struct LevelBonuses
    {
        public readonly int Hp;
        public readonly int Atk;
        public readonly int Def;
        public readonly int Spd;
        public readonly int Lck;

        /// <summary>Extra HP restored by the breather between floors.</summary>
        public readonly int Breath;

        /// <summary>Gold the hero starts a run holding.</summary>
        public readonly int Gold;

        /// <summary>Extra relics offered at every draft.</summary>
        public readonly int DraftChoices;

        /// <summary>Extra deals available at the bazaar.</summary>
        public readonly int BazaarDeals;

        public LevelBonuses(int hp, int atk, int def, int spd, int lck, int breath,
            int gold, int draftChoices, int bazaarDeals)
        {
            Hp = hp;
            Atk = atk;
            Def = def;
            Spd = spd;
            Lck = lck;
            Breath = breath;
            Gold = gold;
            DraftChoices = draftChoices;
            BazaarDeals = bazaarDeals;
        }
    }

    /// <summary>
    /// Hero levelling, and how deep a run has to be to pay full rate. Port of <c>META</c>.
    /// </summary>
    /// <remarks>
    /// Two dials, deliberately separate. Levelling is a smooth stat curve with a handful of
    /// structural unlocks bolted on at 3, 5, 10, 15 and 20. The reward multiplier is something
    /// else entirely: it scales XP by how far back from the player's FRONTIER a hall sits, not
    /// by the hall's absolute depth.
    ///
    /// Anchoring on the frontier is what keeps the early game alive — a new delver's first
    /// dungeon <i>is</i> their frontier and pays full rate, while a veteran farming that same
    /// dungeon earns a tenth. Score and gold are deliberately left unscaled, so the leaderboard
    /// stays comparable between players and the purse collected is the purse banked.
    /// </remarks>
    public static class Progression
    {
        /// <summary>Levelling stops here.</summary>
        public const int MaxLevel = 20;

        /// <summary>XP multiplier by how many halls back from the frontier a run sits.</summary>
        public static readonly double[] RewardSteps = { 1.0, 0.8, 0.4, 0.2 };

        /// <summary>Rate for anything shallower than the table covers.</summary>
        public const double RewardFloor = 0.1;

        /// <summary>A structural unlock granted at a particular level.</summary>
        public readonly struct StructuralPerk
        {
            public readonly int Level;
            public readonly string Description;

            /// <summary>Which bonus it adds to, or none when the perk is purely a mode unlock.</summary>
            public readonly string Field;

            public readonly int Amount;

            public StructuralPerk(int level, string description, string field, int amount)
            {
                Level = level;
                Description = description;
                Field = field;
                Amount = amount;
            }
        }

        /// <summary>The levels that grant something beyond the stat curve.</summary>
        public static readonly StructuralPerk[] StructuralPerks =
        {
            new StructuralPerk(3, "\U0001F513 Daily Delve mode unlocked", "none", 0),
            new StructuralPerk(5, "Start with 20 gold · \U0001F513 Versus mode unlocked", "gold", 20),
            new StructuralPerk(10, "+1 draft choice (every draft)", "draft", 1),
            new StructuralPerk(15, "Second bazaar deal", "deal", 1),
            new StructuralPerk(20, "+30 more starting gold", "gold", 30),
        };

        /// <summary>XP needed to climb from <paramref name="level"/> - 1 to it. Roughly 154k to level 20.</summary>
        public static int Need(int level)
        {
            return JsMath.RoundToInt(1000 * Math.Pow(1.2, level - 2));
        }

        /// <summary>The level a total XP figure buys.</summary>
        public static int LevelFor(int xp)
        {
            int level = 1;
            int spent = 0;
            while (level < MaxLevel && xp >= spent + Need(level + 1))
            {
                spent += Need(level + 1);
                level++;
            }

            return level;
        }

        /// <summary>Fraction of the way to the next level, in [0, 1]. Always 1 at the cap.</summary>
        public static double Progress(int xp)
        {
            int level = LevelFor(xp);
            if (level >= MaxLevel)
            {
                return 1.0;
            }

            int spent = 0;
            for (int i = 2; i <= level; i++)
            {
                spent += Need(i);
            }

            return (xp - spent) / (double)Need(level + 1);
        }

        /// <summary>Everything a level grants, stat curve and structural unlocks together.</summary>
        public static LevelBonuses Bonuses(int level)
        {
            int l = Math.Max(1, level);

            int hp = 5 * (l - 1);
            int atk = JsMath.RoundToInt(l / 5.0);
            int def = JsMath.RoundToInt(l / 4.0);
            int spd = JsMath.RoundToInt(l / 5.0);
            int lck = JsMath.RoundToInt(l / 4.0);
            int breath = JsMath.RoundToInt(l / 5.0);
            int gold = 0;
            int draft = 0;
            int deal = 0;

            foreach (StructuralPerk perk in StructuralPerks)
            {
                if (l < perk.Level || perk.Amount == 0) continue;

                switch (perk.Field)
                {
                    case "gold": gold += perk.Amount; break;
                    case "draft": draft += perk.Amount; break;
                    case "deal": deal += perk.Amount; break;
                    case "hp": hp += perk.Amount; break;
                    case "atk": atk += perk.Amount; break;
                    case "def": def += perk.Amount; break;
                    case "spd": spd += perk.Amount; break;
                    case "lck": lck += perk.Amount; break;
                    case "breath": breath += perk.Amount; break;
                }
            }

            return new LevelBonuses(hp, atk, def, spd, lck, breath, gold, draft, deal);
        }

        /// <summary>
        /// What THIS level grants, as the level-up screen shows it: the delta against the level
        /// below, plus any structural unlock.
        /// </summary>
        public static string PerkDescription(int level)
        {
            LevelBonuses now = Bonuses(level);
            LevelBonuses before = Bonuses(level - 1);

            var parts = new List<string>();
            Add(parts, "HP", now.Hp - before.Hp);
            Add(parts, "ATK", now.Atk - before.Atk);
            Add(parts, "DEF", now.Def - before.Def);
            Add(parts, "SPD", now.Spd - before.Spd);
            Add(parts, "LCK", now.Lck - before.Lck);
            Add(parts, "breath heal", now.Breath - before.Breath);

            foreach (StructuralPerk perk in StructuralPerks)
            {
                if (perk.Level == level) parts.Add(perk.Description);
            }

            if (parts.Count == 0)
            {
                return "—";
            }

            var text = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) text.Append(" · ");
                text.Append(parts[i]);
            }

            return text.ToString();
        }

        private static void Add(List<string> parts, string label, int delta)
        {
            if (delta > 0)
            {
                parts.Add("+" + delta + " " + label);
            }
        }

        /// <summary>Score multiplier for a dungeon's depth. Applies to score, never to XP.</summary>
        public static double TierMultiplier(int tier)
        {
            return Math.Pow(1.1, Math.Max(1, tier) - 1);
        }

        /// <summary>
        /// XP rate for running <paramref name="tier"/> when <paramref name="frontier"/> is the
        /// deepest hall unlocked. Full rate at the frontier itself, falling away behind it.
        /// </summary>
        public static double RewardMultiplier(int tier, int frontier)
        {
            int back = frontier - Math.Max(1, tier);
            if (back < 0)
            {
                return 1.0;
            }

            return back < RewardSteps.Length ? RewardSteps[back] : RewardFloor;
        }
    }
}
