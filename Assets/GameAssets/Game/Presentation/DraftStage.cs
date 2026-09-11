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
    /// The draft: two relics on a table and one floor's worth of consequences.
    /// </summary>
    /// <remarks>
    /// The first stage a delver actually decides anything on, and the first of the run scene's
    /// automatic answers to be deleted. What makes it a decision is not the cards but what is
    /// already on the shelf, which is why the line that matters most on each is the one saying
    /// what it would CHAIN with.
    ///
    /// A reroll is shown only when the purse can cover it, because that is the only time the run
    /// stops to ask: a delve with an empty purse walks straight past the question, so a button
    /// drawn anyway would be one that does nothing when pressed.
    /// </remarks>
    public sealed class DraftStage : RunStage
    {
        [SerializeField] private TMP_Text _title;

        [Tooltip("The cards. One is spawned per relic on offer.")]
        [SerializeField] private RectTransform _cards;

        [SerializeField] private Button _card;

        [SerializeField] private Button _reroll;
        [SerializeField] private TMP_Text _rerollLabel;
        [SerializeField] private GameContent _content;

        private static readonly Color Ink = new Color(0.90f, 0.87f, 0.80f);

        /// <summary>What a relic does, which is read but is not the heading.</summary>
        private static readonly Color Told = new Color(0.63f, 0.59f, 0.52f);

        private static readonly Color Faint = new Color(0.40f, 0.36f, 0.31f);

        private static readonly Color Chained = new Color(0.49f, 0.60f, 0.42f);

        private readonly List<Button> _spawned = new List<Button>();

        /// <summary>Which stop is on the table, so a press knows what it is answering.</summary>
        private AskKind _asking;

        private int _floor;

        /// <summary>
        /// What the delver reached for while the run was still asking about rerolls.
        /// </summary>
        /// <remarks>
        /// The whole reason this exists. One table answers TWO of the engine's questions — pay
        /// for two more? and then take one — and a delver looking at it only ever asked itself
        /// one. So reaching for a relic during the first question answers it (no) and is
        /// remembered, and the second question is answered with the same reach.
        ///
        /// Without this the first press appeared to do nothing at all: it declined the reroll,
        /// the same two relics came straight back for the draft, and the delver pressed again.
        /// </remarks>
        private RelicId _reaching;

        private bool _reached;

        private int _reachedOn;

        /// <summary>
        /// The picture the delver actually reached for, and where on the screen it was.
        /// </summary>
        /// <remarks>
        /// Kept so the icon can FLY to the shelf rather than blink onto it, the way a foe flies
        /// out of its card and into the frame it is fought in. Captured at the press, because the
        /// card is gone by the time the run has finished taking the relic — and a flight has to
        /// start where the delver was looking.
        /// </remarks>
        public Sprite Taken { get; private set; }

        public RectTransform TakenFrom { get; private set; }

        public override AskKind Answers
        {
            get { return AskKind.Draft; }
        }

        /// <summary>
        /// The draft stage answers the reroll stop too.
        /// </summary>
        /// <remarks>
        /// They are one screen. A reroll is asked with the offer already on the table — pay to
        /// see two more, or take one of these — so drawing it anywhere but on the table would ask
        /// a delver to decide about relics they cannot see.
        /// </remarks>
        public override bool Handles(AskKind kind)
        {
            return kind == AskKind.Draft || kind == AskKind.Reroll;
        }

        private void Awake()
        {
            if (_reroll != null)
            {
                // Paying for a fresh offer throws away whatever was being reached for: the
                // relics it was reaching AT are about to be replaced.
                _reroll.onClick.AddListener(() =>
                {
                    _reached = false;
                    Decide(new Answer { Yes = true });
                });
            }
        }

        /// <summary>
        /// Puts the offer on the table.
        /// </summary>
        /// <remarks>
        /// Drawn for BOTH stops it can be shown for. A draft and a reroll are the same table with
        /// a different question over it — take one, or pay for two more — and the source shows
        /// them as one screen, so the reroll button is simply present or absent.
        /// </remarks>
        public override void Draw(Ask ask, RunState run)
        {
            _asking = ask.Kind;
            _floor = ask.Floor;

            int price = DelveRun.RerollPrice(run);

            DraftCard card = DraftCards.Of(ask.Offer, run.Items, run.Gold, price, run.Rerolls);

            if (_title != null) _title.text = Say("takeRelic");

            Cards(card, run);

            if (_reroll != null)
            {
                // The reroll only exists while the run is ASKING about one. On a draft stop the
                // question has already been answered — with a no, or with as many yesses as the
                // purse allowed — and offering it again would be offering something the engine
                // has stopped listening for.
                bool offering = ask.Kind == AskKind.Reroll && card.CanReroll;

                _reroll.gameObject.SetActive(offering);

                if (offering && _rerollLabel != null)
                {
                    // The whole label, price included, comes out of the table. The source draws
                    // this one in English straight in its markup — it is not in DD_STRINGS at
                    // all — so the port adds the key itself, in all eight languages, rather than
                    // showing a Japanese delver the word REROLL.
                    _rerollLabel.text = Say("reroll", "n",
                        price.ToString(CultureInfo.InvariantCulture));
                }
            }

            Settle(ask);
        }

        /// <summary>
        /// Answers the draft with the relic the delver already reached for, if they did.
        /// </summary>
        /// <remarks>
        /// Last, after the table has been drawn, so that a reach nobody can honour leaves a
        /// screen somebody can still press. It can only be honoured while the relic is still on
        /// the table — a reroll replaces the offer, and taking something out of the old one would
        /// hand the delver a relic they never saw.
        /// </remarks>
        private void Settle(Ask ask)
        {
            if (ask.Kind != AskKind.Draft || !_reached || _reachedOn != ask.Floor) return;

            if (!Offered(ask, _reaching)) return;

            _reached = false;

            Decide(new Answer { Pick = _reaching });
        }

        /// <summary>Whether a relic is still one of the ones on the table.</summary>
        private static bool Offered(Ask ask, RelicId relic)
        {
            if (ask.Offer == null) return false;

            foreach (RelicId one in ask.Offer)
            {
                if (one == relic) return true;
            }

            return false;
        }

        /// <summary>
        /// What reaching for a relic means, which depends on what is being asked.
        /// </summary>
        /// <remarks>
        /// On a draft it is the answer. On a reroll it is TWO answers — no, and this one — of
        /// which the engine will take the first now and the second in a moment.
        /// </remarks>
        private void Take(RelicId relic)
        {
            Reached(relic);

            if (_asking == AskKind.Reroll)
            {
                _reaching = relic;
                _reached = true;
                _reachedOn = _floor;

                Decide(new Answer { Yes = false });
                return;
            }

            _reached = false;

            Decide(new Answer { Pick = relic });
        }

        /// <summary>The cards, spawned once and redressed.</summary>
        /// <remarks>
        /// How many are offered is the delver's LEVEL talking — two, and three from level ten —
        /// so nothing here counts them: the table grows if the offer does and no layout has to be
        /// told.
        /// </remarks>
        private void Cards(DraftCard card, RunState run)
        {
            if (_card == null || _cards == null) return;

            // The template itself is never one of the cards. Left showing in the scene it draws
            // as an undressed relic — a blank icon on an untinted body — above the real ones,
            // and it is an easy thing to leave on while laying a card out in the editor.
            _card.gameObject.SetActive(false);

            while (_spawned.Count < card.Offer.Count)
            {
                Button made = Instantiate(_card, _cards);

                made.gameObject.SetActive(true);
                _spawned.Add(made);
            }

            for (var i = 0; i < _spawned.Count; i++)
            {
                bool used = i < card.Offer.Count;

                _spawned[i].gameObject.SetActive(used);

                if (!used) continue;

                Offered offered = card.Offer[i];

                Dress(_spawned[i], offered, run.Gold);

                // Captured per card rather than read at press time. The table is redressed every
                // floor, and a listener that asked which relic this card was showing would take
                // whatever it had become by the time somebody pressed it.
                RelicId taking = offered.Relic;

                _spawned[i].onClick.RemoveAllListeners();
                _spawned[i].onClick.AddListener(() => Take(taking));
            }

            // Measured NOW, not at the end of the frame. A card is as tall as the words on it,
            // and the words were only just written — so a layout left to rebuild itself sizes
            // the cards from whatever they said last floor, and the line at the bottom of each
            // is drawn half outside its own card.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_cards);
        }

        /// <summary>One card: what it is, what it does, and what it would chain with.</summary>
        /// <param name="gold">
        /// What is in the purse. One relic of the fifty describes itself with a number that comes
        /// off it — see <c>RelicWords</c> — and a card drawn without it says "(now +{n})".
        /// </param>
        private void Dress(Button card, Offered offered, int gold)
        {
            Sprite drawn = _content != null && _content.RelicIcons != null
                ? _content.RelicIcons.For(offered.Relic)
                : null;

            // A card that names its own parts dresses itself. Everything below is what a card
            // WITHOUT one gets — reaching in and taking texts in the order they happen to be
            // made in, which works exactly until somebody reorders the hierarchy in the editor.
            var known = card.GetComponent<RelicCardView>();

            if (known != null)
            {
                known.Show(offered, Word(offered.NameKey, offered.Name),
                    Describes(offered.Relic, offered.WhatKey, offered.What, gold),
                    Under(offered), drawn);

                return;
            }

            var texts = card.GetComponentsInChildren<TMP_Text>(true);

            if (texts.Length > 0)
            {
                texts[0].text = Word(offered.NameKey, offered.Name);
                texts[0].color = Ink;
            }

            if (texts.Length > 1)
            {
                texts[1].text = Describes(offered.Relic, offered.WhatKey, offered.What, gold);
                texts[1].color = Told;
            }

            if (texts.Length > 2)
            {
                texts[2].text = Under(offered);

                // Green when it chains, because that is the line a delver is looking for and it
                // should be findable without reading the others.
                texts[2].color = offered.Chains.Count > 0 ? Chained : Faint;
            }

            Image icon = Icon(card);

            if (icon == null) return;

            icon.sprite = drawn;
            icon.enabled = drawn != null;
        }

        /// <summary>
        /// The card's picture, which is the first image on it that is not the card itself.
        /// </summary>
        /// <remarks>
        /// Asking for the first image outright gets the BUTTON's own ground — it sits on the same
        /// object, so it is the first thing found — and the relic's picture would be set on the
        /// thing taking the press instead. Which meant a card that lit up in the shape of a
        /// whetstone and never showed one.
        /// </remarks>
        private static Image Icon(Button card)
        {
            var images = card.GetComponentsInChildren<Image>(true);

            foreach (Image one in images)
            {
                if (one != card.targetGraphic) return one;
            }

            return null;
        }

        /// <summary>
        /// The line under a card: its family, what it chains with, and how many are held.
        /// </summary>
        /// <remarks>
        /// Three small facts on one line rather than three rows. The chain is first because it
        /// is the one that decides anything — a relic that reacts to something already on the
        /// shelf is worth several that do not — and the count is last because it is the one that
        /// is usually absent.
        /// </remarks>
        private string Under(Offered offered)
        {
            var said = new StringBuilder();

            said.Append(offered.Family.ToString().ToUpperInvariant());

            if (offered.Wheel != null)
            {
                said.Append("  ").Append(offered.Wheel[0]).Append(" › ").Append(offered.Wheel[1]);
            }

            if (offered.Chains.Count > 0)
            {
                said.Append("  CHAINS: ");

                for (var i = 0; i < offered.Chains.Count; i++)
                {
                    if (i > 0) said.Append(" · ");

                    RelicTextDef text = RelicText.Get(offered.Chains[i]);

                    if (text != null) said.Append(Word(text.NameKey, text.Name));
                }
            }

            if (offered.Owned > 0)
            {
                said.Append("  HELD ").Append(offered.Owned);
            }

            return said.ToString();
        }

        /// <summary>
        /// Remembers the picture that was reached for and the card it was on.
        /// </summary>
        /// <remarks>
        /// Found by asking the CARDS rather than by remembering which one was dressed with what:
        /// a card knows its own parts, and a delver pressing the second card should see the
        /// second card's icon leave.
        /// </remarks>
        private void Reached(RelicId relic)
        {
            Taken = null;
            TakenFrom = null;

            Sprite want = _content != null && _content.RelicIcons != null
                ? _content.RelicIcons.For(relic)
                : null;

            if (want == null) return;

            for (var i = 0; i < _spawned.Count; i++)
            {
                if (!_spawned[i].gameObject.activeSelf) continue;

                var known = _spawned[i].GetComponent<RelicCardView>();

                Image icon = known != null ? known.Icon : Icon(_spawned[i]);

                if (icon == null || icon.sprite != want) continue;

                Taken = want;
                TakenFrom = (RectTransform)icon.transform;
                return;
            }
        }

        /// <summary>A translated word, or the English one for a relic nobody translated.</summary>
        private string Word(string key, string english)
        {
            if (key == null) return english ?? string.Empty;

            return Say(key);
        }
    }
}
