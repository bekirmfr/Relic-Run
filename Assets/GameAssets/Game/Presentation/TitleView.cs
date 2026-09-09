using System;
using RelicRun.Core.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The title screen, drawn.
    /// </summary>
    /// <remarks>
    /// It works out nothing. Every string and every number it shows arrives on a
    /// <see cref="TitleCard"/> that Core built, and the only decisions here are the ones a widget
    /// is allowed to make: what colour a locked banner is, and how wide a bar looks.
    ///
    /// That division is the same one the fight uses, and it is what makes the title testable at
    /// all — "what does a level-two delver see" is a question about a struct, answered by
    /// <c>dotnet test</c>, rather than a question about a screen nobody can open on a build
    /// server.
    /// </remarks>
    public sealed class TitleView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _level;
        [SerializeField] private Image _levelBar;

        [SerializeField] private TMP_Text _best;
        [SerializeField] private TMP_Text _crowns;

        [SerializeField] private TMP_Text _kicker;
        [SerializeField] private TMP_Text _sub;
        [SerializeField] private Button _play;

        [SerializeField] private Button _daily;
        [SerializeField] private TMP_Text _dailyLeft;
        [SerializeField] private TMP_Text _dailyRight;

        [SerializeField] private Button _versus;
        [SerializeField] private TMP_Text _versusLeft;
        [SerializeField] private TMP_Text _versusRight;

        [SerializeField] private Button _how;

        /// <summary>
        /// What a shut mode looks like.
        /// </summary>
        /// <remarks>
        /// Dimmed rather than hidden, because a delver who cannot see the arena has no reason to
        /// keep playing toward it. The source shows both banners from the first launch and writes
        /// the level that opens them across the front — the lock IS the advertisement.
        ///
        /// The colours live here rather than in Core for the reason every colour in this port
        /// does: a view-model that carried them could not be read by anything that was not a
        /// screen.
        /// </remarks>
        private static readonly Color Open = new Color(0.90f, 0.87f, 0.80f);

        private static readonly Color Shut = new Color(0.55f, 0.52f, 0.46f);

        /// <summary>Pressed when the delver wants to play whatever is being offered.</summary>
        public event Action Played;

        /// <summary>Pressed on the Daily banner.</summary>
        public event Action ChoseDaily;

        /// <summary>Pressed on the Versus banner.</summary>
        public event Action ChoseVersus;

        /// <summary>Pressed on the how-to-play button.</summary>
        public event Action AskedHow;

        private void Awake()
        {
            Listen(_play, () => Played);
            Listen(_daily, () => ChoseDaily);
            Listen(_versus, () => ChoseVersus);
            Listen(_how, () => AskedHow);
        }

        /// <summary>
        /// Puts a card on the screen.
        /// </summary>
        /// <remarks>
        /// Called whenever anything changes rather than every frame, except for the clock — the
        /// countdown moves once a second and the rest of the card is rebuilt with it, because a
        /// card is cheap and a screen that updated only the parts it thought had changed would
        /// eventually be wrong about one of them.
        /// </remarks>
        public void Show(TitleCard card)
        {
            Put(_name, card.Name);
            Put(_level, "LV " + card.Level);

            if (_levelBar != null)
            {
                // A capped delver gets a full bar rather than a fraction of a level that does not
                // exist, which is the alternative reading of a progress of zero at the top.
                _levelBar.fillAmount = card.Capped ? 1f : Mathf.Clamp01((float)card.Progress);
            }

            Put(_best, "BEST " + card.Best);
            Put(_crowns, "CROWNS " + card.Crowns);

            Put(_kicker, card.Kicker);
            Put(_sub, card.Sub);

            Dress(_daily, _dailyLeft, _dailyRight, card.Daily);
            Dress(_versus, _versusLeft, _versusRight, card.Versus);
        }

        /// <summary>
        /// A banner, in the state its mode is in.
        /// </summary>
        /// <remarks>
        /// A shut banner is still PRESSABLE. The source lets a delver open a locked mode and be
        /// told why it is locked, which is more use than a button that does nothing — a button
        /// that does nothing is indistinguishable from one that is broken.
        /// </remarks>
        private static void Dress(Button button, TMP_Text left, TMP_Text right, ModeLine line)
        {
            Put(left, line.Left);
            Put(right, line.Right);

            Color ink = line.Open ? Open : Shut;

            if (left != null) left.color = ink;
            if (right != null) right.color = ink;

            if (button != null) button.interactable = true;
        }

        private static void Put(TMP_Text text, string what)
        {
            if (text != null) text.text = what;
        }

        /// <summary>
        /// Hooks a button up to whichever handler is attached at the moment it is pressed.
        /// </summary>
        /// <remarks>
        /// The indirection is not decoration. Subscribing the event directly would capture
        /// whoever was listening when <c>Awake</c> ran, which is nobody — the scene wires its
        /// handlers up afterwards, and a button bound too early is a button that silently does
        /// nothing for the whole life of the screen.
        /// </remarks>
        private static void Listen(Button button, Func<Action> handler)
        {
            if (button == null) return;

            button.onClick.AddListener(() =>
            {
                Action now = handler();
                if (now != null) now();
            });
        }
    }
}
