using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Run;

namespace RelicRun.Core.Presentation
{
    /// <summary>One relic on the bazaar's shelf, and whether the purse can reach it.</summary>
    public struct Ware
    {
        /// <summary>
        /// The relic, described exactly as the draft describes it.
        /// </summary>
        /// <remarks>
        /// The same <see cref="Offered"/> the draft table is built from, rather than a second
        /// description of the same relic. A delver moves between those two screens inside one
        /// run and the shelf is where they spend forty gold on a decision — so the two agreeing
        /// is worth having by construction rather than by a test that notices when they stop.
        /// </remarks>
        public Offered Shown;

        /// <summary>Whether the purse covers it.</summary>
        public bool Afford;
    }

    /// <summary>One copy on the shelf that could be woken.</summary>
    public struct Waking
    {
        /// <summary>
        /// Which inventory slot holds it.
        /// </summary>
        /// <remarks>
        /// The COPY, not the relic. A delver carrying three Thorn Vests wakes one of them, and
        /// the engine is told which by its position on the shelf — so this is the number the
        /// answer carries and the number a button has to remember.
        /// </remarks>
        public int Slot;

        public RelicId Relic;

        public string NameKey;

        public string Name;

        /// <summary>What waking it does, in English. Never empty.</summary>
        /// <remarks>
        /// The source's <c>AWAKE_TEXT</c>, which is the same prose the relic's own card promises
        /// under <c>AWAKENED:</c>. Untranslated there and untranslated here.
        ///
        /// Most relics have none — the lore carries awakened prose for the ones whose behaviour
        /// changes in a way worth a sentence, and nulls for the rest. The source answers those
        /// with a line of its own rather than a blank row, and so does <see cref="BazaarCards.Deeper"/>:
        /// a row offering a sixty-gold deal with nothing written on it is a row nobody presses.
        /// </remarks>
        public string Promise;

        /// <summary>Whether the purse covers it.</summary>
        public bool Afford;
    }

    /// <summary>Everything the bazaar shows.</summary>
    public struct BazaarCard
    {
        public int Floor;

        public int Gold;

        /// <summary>What a relic off the shelf costs, after whatever a Thumb shaves off.</summary>
        public int BuyPrice;

        /// <summary>And what waking a copy costs.</summary>
        public int WakePrice;

        public IReadOnlyList<Ware> Wares;

        public IReadOnlyList<Waking> Wakings;

        /// <summary>Whether anything on either shelf can actually be bought.</summary>
        /// <remarks>
        /// False for a delver who walked in with nine gold, which is a real way to arrive: the
        /// bazaar is floor seven and nothing makes a delver save for it. A screen that did not
        /// know this would offer five relics in full colour and do nothing when pressed.
        /// </remarks>
        public bool Anything;
    }

    /// <summary>
    /// The bazaar: five relics, whatever can be woken, and one deal.
    /// </summary>
    /// <remarks>
    /// The only floor of a run with no fight on it, and the only place gold becomes anything.
    /// Everything a delver has been hoarding since floor one is spent here or carried home
    /// unspent, which is why the purse sits at the top of the screen rather than in a corner.
    ///
    /// ONE deal a visit — two for a delver who woke a Merchant's Thumb. The engine counts them
    /// and simply stops asking, so nothing here counts: the shelf a screen is handed is already
    /// the shelf that is left, and an awakening shelf that has closed arrives empty.
    /// </remarks>
    public static class BazaarCards
    {
        /// <summary>What waking says when the relic has no line of its own.</summary>
        /// <remarks>
        /// The source's own fallback, word for word. Every relic on the waking shelf CAN be
        /// woken — the shelf is built from what the delver carries in pairs — but only some of
        /// them have prose describing what that does, so the rest get this.
        /// </remarks>
        public const string Deeper = "Awaken this copy: its effects run deeper.";

        /// <summary>The bazaar as it stands, for the shelf the run is offering.</summary>
        public static BazaarCard Of(int floor, IReadOnlyList<RelicId> offer,
            IReadOnlyList<int> awakenable, RunState run)
        {
            int buy = DelveRun.PriceOf(run, DelveRun.BuyPrice);
            int wake = DelveRun.PriceOf(run, DelveRun.AwakenPrice);

            var wares = new List<Ware>();
            var wakings = new List<Waking>();

            bool canBuy = run.Gold >= buy;
            bool canWake = run.Gold >= wake;

            if (offer != null)
            {
                foreach (RelicId one in offer)
                {
                    wares.Add(new Ware
                    {
                        Shown = DraftCards.One(one, run.Items),
                        Afford = canBuy,
                    });
                }
            }

            if (awakenable != null)
            {
                foreach (int slot in awakenable)
                {
                    if (slot < 0 || slot >= run.Items.Count) continue;

                    wakings.Add(Wake(slot, run.Items[slot], canWake));
                }
            }

            return new BazaarCard
            {
                Floor = floor,
                Gold = run.Gold,
                BuyPrice = buy,
                WakePrice = wake,
                Wares = wares,
                Wakings = wakings,
                Anything = (wares.Count > 0 && canBuy) || (wakings.Count > 0 && canWake),
            };
        }

        /// <summary>One copy on the waking shelf.</summary>
        public static Waking Wake(int slot, RelicId relic, bool afford)
        {
            RelicTextDef text = RelicText.Get(relic);
            RelicLoreDef lore = RelicLore.Get(relic);

            return new Waking
            {
                Slot = slot,
                Relic = relic,
                NameKey = text != null ? text.NameKey : null,
                Name = text != null ? text.Name : string.Empty,
                Promise = lore != null && lore.Awake != null ? lore.Awake : Deeper,
                Afford = afford,
            };
        }
    }
}
