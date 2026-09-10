using System.Collections.Generic;
using System.Globalization;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Stats;

namespace RelicRun.Core.Presentation
{
    /// <summary>One of the four numbers along the top of a foe's card.</summary>
    public struct FoeStat
    {
        /// <summary>What it is called. English.</summary>
        public string Label;

        /// <summary>What it says, already written out — or a question mark.</summary>
        public string Value;
    }

    /// <summary>Everything a foe's card shows.</summary>
    public struct EnemyCard
    {
        public int Species;

        /// <summary>Which column of the sheet draws it: guard, elite, or boss.</summary>
        public int Variant;

        /// <summary>The key its name is translated under.</summary>
        public string NameKey;

        /// <summary>The line over the picture. English.</summary>
        public string Kicker;

        /// <summary>What colour that line is, as <c>#RRGGBB</c>.</summary>
        public string KickerHex;

        /// <summary>Its card, already quoted. English.</summary>
        public string Lore;

        /// <summary>HP, attack, speed and armour, in that order.</summary>
        public IReadOnlyList<FoeStat> Stats;

        /// <summary>What its relics DO, or a line saying there is nothing to say.</summary>
        public string Abilities;

        /// <summary>
        /// Which relics it carries, for the screen to name.
        /// </summary>
        /// <remarks>
        /// Ids rather than names, because nineteen of the fifty are translated and Core does not
        /// translate. Empty from the bestiary, where the answer is that it depends on the hall.
        /// </remarks>
        public IReadOnlyList<RelicId> Relics;

        /// <summary>What to print instead when <see cref="Relics"/> is empty. English.</summary>
        public string NoRelics;

        /// <summary>
        /// Whether the numbers are real.
        /// </summary>
        /// <remarks>
        /// False from the bestiary, where every stat is a question mark. The screen does not need
        /// to know why — it prints what it is given — but a test does, and so does anybody
        /// wondering why a dossier says nothing about how hard a rat hits.
        /// </remarks>
        public bool Measured;
    }

    /// <summary>
    /// A foe's card, worked out.
    /// </summary>
    /// <remarks>
    /// The same card in two situations that share almost nothing. From the bestiary it is a
    /// species DOSSIER: what this kind of thing is, drawn in its plainest form, with every number
    /// a question mark — because a rat in the first hall and a rat in the eighth are the same
    /// species and not the same fight, and printing one hall's numbers would be a lie about the
    /// other nine. From a fight it is THIS foe: what it has left, what it hits for, and what it
    /// is carrying.
    ///
    /// A rival delver in a duel is a third shape the source also folds in here — its own name, a
    /// motto for lore, a portrait instead of a sprite. That one waits for the run layer, which is
    /// the only thing that knows a roster.
    /// </remarks>
    public static class EnemyCards
    {
        /// <summary>The line over a species dossier.</summary>
        public const string Encountered = "BESTIARY · ENCOUNTERED";

        /// <summary>What a dossier says where a number would be.</summary>
        public const string Unmeasured = "?";

        /// <summary>And where a list of relics would be.</summary>
        public const string Varies = "— varies by dungeon —";

        /// <summary>What a foe carrying nothing says.</summary>
        public const string Nothing = "— none —";

        /// <summary>What separates two relics.</summary>
        public const string Between = " · ";

        /// <summary>The grey everything ordinary is written in.</summary>
        public const string PlainHex = "#8B8172";

        /// <summary>The gold a boss and a king are written in.</summary>
        public const string CrownHex = "#E3B341";

        /// <summary>And the violet of an elite.</summary>
        public const string EliteHex = "#B7A5F0";

        /// <summary>The species dossier, as the bestiary opens it.</summary>
        /// <param name="species">Its bestiary index.</param>
        public static EnemyCard Dossier(int species)
        {
            EnemyDef def = EnemyCatalog.Get(species);

            return new EnemyCard
            {
                Species = species,

                // The plainest column. A dossier is about the species, and drawing it in a
                // boss's armour would say this is what one always looks like.
                Variant = 0,

                NameKey = NameKey(species),
                Kicker = Encountered,
                KickerHex = PlainHex,
                Lore = Quoted(def != null ? def.Lore : null),
                Stats = Unknown(),
                Abilities = Varies,
                Relics = new List<RelicId>(),
                NoRelics = Varies,
                Measured = false,
            };
        }

        /// <summary>This foe, in this fight, as it stands.</summary>
        /// <param name="left">
        /// What it has left, which the engine holds rather than the state does — so the caller
        /// passes it. Below zero is shown as zero: a foe on its last frame is at nothing, not at
        /// minus four.
        /// </param>
        public static EnemyCard Live(EnemyState foe, int left)
        {
            if (foe == null) return Dossier(0);

            EnemyDef def = EnemyCatalog.Get(foe.SpeciesIndex);

            var relics = new List<RelicId>();

            if (foe.Relics != null) relics.AddRange(foe.Relics);

            return new EnemyCard
            {
                Species = foe.SpeciesIndex,
                Variant = foe.Variant,
                NameKey = NameKey(foe.SpeciesIndex),
                Kicker = Called(foe.Rank),
                KickerHex = Coloured(foe.Rank),
                Lore = Quoted(def != null ? def.Lore : null),
                Stats = Measured(foe, left),
                Abilities = Does(relics),
                Relics = relics,
                NoRelics = Nothing,
                Measured = true,
            };
        }

        /// <summary>What a rank is called on the card. English.</summary>
        public static string Called(EnemyRank rank)
        {
            switch (rank)
            {
                case EnemyRank.King: return "DUNGEON KING";
                case EnemyRank.Boss: return "FLOOR BOSS";
                case EnemyRank.Elite: return "ELITE GUARD";
                default: return "GUARD";
            }
        }

        /// <summary>And what colour it is written in.</summary>
        public static string Coloured(EnemyRank rank)
        {
            switch (rank)
            {
                case EnemyRank.King: return CrownHex;
                case EnemyRank.Boss: return CrownHex;
                case EnemyRank.Elite: return EliteHex;
                default: return PlainHex;
            }
        }

        /// <summary>The key a species' name is translated under.</summary>
        /// <remarks>
        /// The source numbers them <c>en0</c> upward with no gaps, and the bestiary screen reads
        /// the same keys — so this is here rather than spelled twice.
        /// </remarks>
        public static string NameKey(int species)
        {
            return "en" + species.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The four numbers, none of them known.</summary>
        private static IReadOnlyList<FoeStat> Unknown()
        {
            return new[]
            {
                new FoeStat { Label = "HP", Value = Unmeasured },
                new FoeStat { Label = "ATK", Value = Unmeasured },
                new FoeStat { Label = "SPD", Value = Unmeasured },
                new FoeStat { Label = "ARMOR", Value = Unmeasured },
            };
        }

        /// <summary>The four numbers, as this foe stands.</summary>
        private static IReadOnlyList<FoeStat> Measured(EnemyState foe, int left)
        {
            int now = left < 0 ? 0 : left;
            int most = foe.MaxHp > 0 ? foe.MaxHp : foe.Hp;

            return new[]
            {
                new FoeStat
                {
                    Label = "HP",
                    Value = Number(now) + " / " + Number(most),
                },
                new FoeStat { Label = "ATK", Value = Number(foe.Atk) },
                new FoeStat { Label = "SPD", Value = Number(foe.Spd) },
                new FoeStat { Label = "ARMOR", Value = Number(foe.Armor) },
            };
        }

        /// <summary>
        /// What its relics do, in English.
        /// </summary>
        /// <remarks>
        /// The source reads a passive off its ITEMS table, which does not have one — the passives
        /// live in a different table entirely — so every foe's abilities row came out as a list
        /// of names each followed by an empty pair of brackets, saying nothing twice. The intent
        /// is not in doubt, so the passive is read from where it actually is.
        ///
        /// A relic with no passive contributes nothing rather than an empty bracket. If none of
        /// them has one the row says so, which is true and is not what the source said.
        ///
        /// There is no early return for an empty list, and there was one until a mutation showed
        /// it could not be observed: nothing to say and nothing to say it about come out of the
        /// loop identically, and a guard that cannot change an answer is a line to keep in step
        /// for nothing.
        /// </remarks>
        private static string Does(IReadOnlyList<RelicId> relics)
        {
            if (relics == null) return Nothing;

            var said = new System.Text.StringBuilder();

            foreach (RelicId one in relics)
            {
                RelicLoreDef lore = RelicLore.Get(one);

                if (lore == null || lore.Passive == null) continue;

                if (said.Length > 0) said.Append(Between);

                said.Append(lore.Passive);
            }

            return said.Length > 0 ? said.ToString() : Nothing;
        }

        private static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The lore line, in the source's own curly quotes.</summary>
        private static string Quoted(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;

            return "“" + line + "”";
        }
    }
}
