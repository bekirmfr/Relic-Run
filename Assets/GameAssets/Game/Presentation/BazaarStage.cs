using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Core.Run;
using RelicRun.Game.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The Hoard Bazaar: five relics, whatever can be woken, and one deal.
    /// </summary>
    /// <remarks>
    /// The only floor of a run with no fight on it, and the only place gold becomes anything.
    /// Everything a delver has hoarded since floor one is spent here or carried home unspent,
    /// which is why the purse sits beside the title rather than in a corner.
    ///
    /// It is asked REPEATEDLY. The engine offers the shelf, takes one deal, and offers what is
    /// left — so this screen is redrawn between deals and the shelf it is handed is already the
    /// shelf that remains. Nothing here counts deals or removes rows; both are the run's answer,
    /// and a screen that kept its own tally would disagree with the engine the first time a
    /// Merchant's Thumb bought a second one.
    /// </remarks>
    public sealed class BazaarStage : RunStage
    {
        [SerializeField] private TMP_Text _kicker;
        [SerializeField] private TMP_Text _title;

        [Tooltip("The purse. It sits beside the title because this is the screen that spends it.")]
        [SerializeField] private TMP_Text _gold;

        [SerializeField] private TMP_Text _sub;

        [Header("For sale")]
        [SerializeField] private TMP_Text _buyTitle;
        [SerializeField] private RectTransform _wares;
        [SerializeField] private Button _ware;

        [Header("Awaken")]
        [SerializeField] private GameObject _wakeShelf;
        [SerializeField] private TMP_Text _wakeTitle;
        [SerializeField] private RectTransform _wakings;
        [SerializeField] private Button _waking;

        [Tooltip("Everything, in one column. Rebuilt after a redraw so rows find their height.")]
        [SerializeField] private RectTransform _column;

        [Header("The way out")]
        [SerializeField] private Button _leave;
        [SerializeField] private TMP_Text _leaveLabel;
        [SerializeField] private GameContent _content;

        /// <summary>
        /// The bazaar's name, and the sentence explaining its one rule. English.
        /// </summary>
        /// <remarks>
        /// Prose, and the source ships it in English to all eight languages with no key behind
        /// it. The port adds keys for the chrome around it and leaves this where the source left
        /// it — see the note on translating these screens in <c>docs/run-plan.md</c>.
        /// </remarks>
        public const string Name = "THE HOARD BAZAAR";

        public const string Rule = "ONE deal per visit: buy a new relic, or awaken one you " +
                                   "carry — an awakened relic counts as two copies. Choose well.";

        private static readonly Color Coin = new Color(0.890f, 0.702f, 0.255f);

        private static readonly Color Ink = new Color(0.906f, 0.878f, 0.824f);

        private static readonly Color Faint = new Color(0.545f, 0.506f, 0.447f);

        private static readonly Color Quiet = new Color(0.396f, 0.361f, 0.306f);

        /// <summary>What a row that cannot be afforded is dimmed to.</summary>
        /// <remarks>
        /// The source's forty-five per cent. Dimmed rather than hidden, because what a delver
        /// wants to know at nineteen gold is what they are nineteen gold short OF.
        /// </remarks>
        private const float Dimmed = 0.45f;

        private readonly List<Button> _shelf = new List<Button>();

        private readonly List<Button> _woken = new List<Button>();

        public override AskKind Answers
        {
            get { return AskKind.Bazaar; }
        }

        private void Awake()
        {
            if (_leave != null) _leave.onClick.AddListener(() => Decide(Walk()));
        }

        /// <summary>Opens the shelf, or what is left of it.</summary>
        public override void Draw(Ask ask, RunState run)
        {
            BazaarCard card = BazaarCards.Of(ask.Floor, ask.Offer, ask.Awakenable, run);

            Write(_kicker, Say("relicShop", "n", Count(card.Floor)), Coin);
            Write(_title, Name, Ink);
            Write(_gold, Count(card.Gold) + "g", Coin);
            Write(_sub, Rule, Faint);

            Write(_buyTitle, Say("forSale", "n", Count(card.BuyPrice)), Quiet);
            Write(_wakeTitle, Say("awakenRelic", "n", Count(card.WakePrice)), Coin);

            Wares(card);
            Wakings(card);

            // The way out says something different to a delver who cannot buy anything: there is
            // no decision left to make, and calling it LEAVE THE SHOP invites them to keep
            // looking for one.
            Write(_leaveLabel, card.Anything ? Say("leaveShop") : Say("notEnoughGold"),
                card.Anything ? Ink : Faint);

            Settle();
        }

        /// <summary>
        /// Makes the rows as tall as what was just written on them.
        /// </summary>
        /// <remarks>
        /// Twice, and it is not superstition. A row's height comes from its description, and a
        /// description's height comes from how wide it was allowed to be — so the first pass
        /// settles the widths and the second is the one that can ask a wrapped line how tall it
        /// came to. Run once, the longest rows lost their last line: five relics on a shelf with
        /// FLESH and GUARD sliced off the bottom of them.
        ///
        /// Immediately rather than at the end of the frame, because the scene is about to wait on
        /// a press and a shelf that settles a frame later settles after the delver has looked.
        /// </remarks>
        private void Settle()
        {
            if (_column == null) return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(_column);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_column);
        }

        /// <summary>The five on the shelf, spawned once and redressed between deals.</summary>
        private void Wares(BazaarCard card)
        {
            if (_ware == null || _wares == null) return;

            Fit(_shelf, _ware, _wares, card.Wares.Count);

            for (var i = 0; i < _shelf.Count; i++)
            {
                bool used = i < card.Wares.Count;

                _shelf[i].gameObject.SetActive(used);

                if (!used) continue;

                Ware ware = card.Wares[i];

                Dress(_shelf[i], ware.Shown.NameKey, ware.Shown.Name,
                    ware.Shown.WhatKey, ware.Shown.What, Under(ware.Shown),
                    Count(card.BuyPrice) + "g", ware.Afford, ware.Shown.Relic);

                // Captured per row. The shelf is redrawn after every deal, so a listener that
                // asked which relic its row was showing would buy whatever it had become.
                RelicId buying = ware.Shown.Relic;
                bool afford = ware.Afford;

                _shelf[i].onClick.RemoveAllListeners();
                _shelf[i].onClick.AddListener(() =>
                {
                    if (afford) Decide(new Answer { Deal = BazaarDeal.Buy(buying) });
                });
            }
        }

        /// <summary>
        /// The copies that could be woken, which is usually none.
        /// </summary>
        /// <remarks>
        /// The whole shelf is taken off the screen when it is empty rather than left as a
        /// heading over nothing. It empties for two different reasons — a delver carrying no
        /// duplicate, and a delver who has already woken one this visit — and both mean the same
        /// thing to somebody looking at it.
        /// </remarks>
        private void Wakings(BazaarCard card)
        {
            if (_wakeShelf != null) _wakeShelf.SetActive(card.Wakings.Count > 0);

            if (_waking == null || _wakings == null) return;

            Fit(_woken, _waking, _wakings, card.Wakings.Count);

            for (var i = 0; i < _woken.Count; i++)
            {
                bool used = i < card.Wakings.Count;

                _woken[i].gameObject.SetActive(used);

                if (!used) continue;

                Waking waking = card.Wakings[i];

                Dress(_woken[i], waking.NameKey, waking.Name, null, waking.Promise, null,
                    Count(card.WakePrice) + "g", waking.Afford, waking.Relic);

                // The SLOT, not the relic. A delver carrying three Thorn Vests wakes one of
                // them, and which one is a number rather than a name.
                int slot = waking.Slot;
                bool afford = waking.Afford;

                _woken[i].onClick.RemoveAllListeners();
                _woken[i].onClick.AddListener(() =>
                {
                    if (afford) Decide(new Answer { Deal = BazaarDeal.Awaken(slot) });
                });
            }
        }

        /// <summary>Spawns rows until there are enough of them.</summary>
        private static void Fit(List<Button> made, Button template, RectTransform under, int want)
        {
            while (made.Count < want)
            {
                Button one = Instantiate(template, under);

                one.gameObject.SetActive(true);
                made.Add(one);
            }
        }

        /// <summary>One row: a picture, a name, what it does, and what it costs.</summary>
        private void Dress(Button row, string nameKey, string name, string whatKey, string what,
            string under, string price, bool afford, RelicId relic)
        {
            var texts = row.GetComponentsInChildren<TMP_Text>(true);

            if (texts.Length > 0)
            {
                texts[0].text = Word(nameKey, name);
                texts[0].color = Ink;
            }

            if (texts.Length > 1)
            {
                texts[1].text = Word(whatKey, what);
                texts[1].color = Faint;
            }

            if (texts.Length > 2)
            {
                texts[2].text = under ?? string.Empty;
                texts[2].color = Quiet;
            }

            if (texts.Length > 3)
            {
                texts[3].text = price;
                texts[3].color = Coin;
            }

            // Dimmed rather than disabled, so the row still reads. A Button left interactable
            // would also still take the press — which is why the listener checks too.
            var fade = row.GetComponent<CanvasGroup>();

            if (fade != null) fade.alpha = afford ? 1f : Dimmed;

            row.interactable = afford;

            Image icon = Icon(row);

            if (icon == null || _content == null || _content.RelicIcons == null) return;

            Sprite drawn = _content.RelicIcons.For(relic);

            icon.sprite = drawn;
            icon.enabled = drawn != null;
        }

        /// <summary>
        /// The row's picture, which is the first image on it that is not the row itself.
        /// </summary>
        /// <remarks>
        /// Asking for the first image outright gets the BUTTON's own ground, which sits on the
        /// same object. The draft learned this the hard way and the shelf inherits the lesson.
        /// </remarks>
        private static Image Icon(Button row)
        {
            var images = row.GetComponentsInChildren<Image>(true);

            foreach (Image one in images)
            {
                if (one != row.targetGraphic) return one;
            }

            return null;
        }

        /// <summary>The line under a ware: its family, its wheel, and how many are held.</summary>
        private string Under(Offered offered)
        {
            var said = new StringBuilder();

            said.Append(offered.Family.ToString().ToUpperInvariant());

            if (offered.Wheel != null)
            {
                said.Append("  ").Append(offered.Wheel[0]).Append(" › ").Append(offered.Wheel[1]);
            }

            if (offered.Owned > 0) said.Append("  HELD ").Append(offered.Owned);

            return said.ToString();
        }

        /// <summary>Leaving, which is what the run reads as "no more deals".</summary>
        private static Answer Walk()
        {
            return new Answer { Deal = BazaarDeal.Walk };
        }

        private static void Write(TMP_Text text, string what, Color ink)
        {
            if (text == null) return;

            text.text = what;
            text.color = ink;
        }

        /// <summary>A translated word, or the English one for something nobody translated.</summary>
        private string Word(string key, string english)
        {
            if (key == null) return english ?? string.Empty;

            return Say(key);
        }

        private static string Count(int number)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }
    }
}
