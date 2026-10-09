using System;
using System.Collections.Generic;
using RelicRun.Core.Presentation;
using RelicRun.Core.Run;
using RelicRun.Game.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The end of a run.
    /// </summary>
    /// <remarks>
    /// The one screen in the menu that is about something that just happened rather than about
    /// the save. It cannot draw itself from the vault, because the vault has already been
    /// written by the time it opens — the new best is in there, so asking whether this run beat
    /// the record would answer yes forever. The run tells it instead, through <see cref="Show"/>.
    ///
    /// Until the run layer exists nothing calls that, so the screen draws a placeholder rather
    /// than inventing a run. A screen showing somebody a fictional score is worse than one
    /// saying it has nothing to show.
    /// </remarks>
    public sealed class OverPanel : MetaPanel
    {
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _titleUnder;
        [SerializeField] private TMP_Text _score;
        [SerializeField] private TMP_Text _note;
        [SerializeField] private TMP_Text _rate;
        [SerializeField] private TMP_Text _stats;

        [SerializeField] private Button _home;
        [SerializeField] private Button _board;

        [Header("The purse coming home")]
        [SerializeField] private RectTransform _pill;
        [SerializeField] private TMP_Text _wallet;
        [SerializeField] private Image _coin;
        [SerializeField] private int _most = 12;
        [SerializeField] private float _flight = 0.6f;
        [SerializeField] private float _stagger = 0.07f;
        [SerializeField] private float _arc = 80f;

        private static readonly Color Ink = new Color(0.90f, 0.87f, 0.80f);

        private static readonly Color Faint = new Color(0.42f, 0.40f, 0.35f);

        private static readonly Color Lit = new Color(0.89f, 0.70f, 0.25f);

        /// <summary>What the last run came to, or nothing if none has finished this session.</summary>
        private OverCard _card;

        private bool _have;

        public override Page Shows
        {
            get { return Page.Over; }
        }

        private void Awake()
        {
            // Home is not always the title. A run that earned experience stops at the experience
            // screen on the way, which is Core's rule and is asked rather than repeated here.
            if (_home != null)
            {
                // The first press while the purse is still flying lands it rather than leaving,
                // so the wallet a delver walks away from is the one they actually hold.
                _home.onClick.AddListener(() =>
                {
                    if (_flying) Land();
                    else Go(Pages.Home(Page.Over, _have ? _card.Xp : 0, _shown));
                });
            }

            if (_coin != null) _coin.gameObject.SetActive(false);

            if (_board != null) _board.onClick.AddListener(() => Go(Page.Board));
        }

        /// <summary>Whether the experience screen has already been through.</summary>
        /// <remarks>
        /// Kept here because it is a fact about this run rather than about the save, and because
        /// the rule that reads it — stop at the experience screen once, not every time — needs
        /// somewhere to remember that once has happened.
        /// </remarks>
        private bool _shown;

        /// <summary>Hands the screen a finished run.</summary>
        public void Show(OverCard card)
        {
            _card = card;
            _have = true;
            _shown = false;
            _flown = false;

            Paint();
        }

        /// <summary>Marks the experience as having been shown, so home goes home.</summary>
        public void Spent()
        {
            _shown = true;
        }

        public override void Draw(SaveVault vault, DateTimeOffset now)
        {
            Paint();

            // The save already holds the banked purse, so the wallet before the run is what it
            // holds less what this run brought. Read at this moment and nowhere later: nothing
            // can be spent between the run ending and this screen opening.
            _held = vault.Earned.Gold;

            if (_have && !_flown && _card.Banked > 0) Fly();
            else Land();
        }

        /* ---------- the purse coming home ---------- */

        /// <summary>The wallet once everything has landed.</summary>
        private int _held;

        /// <summary>Whether this run's purse has already flown, so coming back does not fly it twice.</summary>
        private bool _flown;

        private bool _flying;
        private float _clock;
        private int _landed;
        private readonly List<RectTransform> _coins = new List<RectTransform>();

        /// <summary>Coins drawn for a purse: one per stretch of gold, never more than the screen can read.</summary>
        private int Count()
        {
            return Mathf.Clamp(_card.Banked, 1, Mathf.Max(1, _most));
        }

        private void Fly()
        {
            _flown = true;

            if (_coin == null || _pill == null)
            {
                Land();
                return;
            }

            int count = Count();

            while (_coins.Count < count)
            {
                Image made = Instantiate(_coin, _coin.transform.parent);
                _coins.Add((RectTransform)made.transform);
            }

            for (int i = 0; i < _coins.Count; i++) _coins[i].gameObject.SetActive(false);

            _flying = true;
            _clock = 0f;
            _landed = 0;

            Wallet(_held - _card.Banked);
        }

        private void Update()
        {
            if (!_flying) return;

            _clock += Time.unscaledDeltaTime;

            int count = Count();
            Vector3 from = _coin.transform.position;
            Vector3 to = _pill.position;
            int landed = 0;

            for (int i = 0; i < count; i++)
            {
                RectTransform coin = _coins[i];
                float t = (_clock - i * _stagger) / Mathf.Max(0.01f, _flight);

                if (t <= 0f) continue;

                if (t >= 1f)
                {
                    coin.gameObject.SetActive(false);
                    landed++;
                    continue;
                }

                // Up and over in an arc, eased in, so a coin leaves slowly and lands with intent.
                coin.gameObject.SetActive(true);
                float eased = t * t;
                Vector3 at = Vector3.Lerp(from, to, eased);
                at.y += Mathf.Sin(t * Mathf.PI) * _arc * _pill.lossyScale.y;
                coin.position = at;
            }

            if (landed != _landed)
            {
                _landed = landed;

                // The wallet counts up as each coin arrives, and the last one lands it exactly.
                int start = _held - _card.Banked;
                Wallet(landed >= count ? _held : start + _card.Banked * landed / count);
                _pill.localScale = Vector3.one * 1.15f;
            }
            else
            {
                _pill.localScale = Vector3.Lerp(_pill.localScale, Vector3.one,
                    Time.unscaledDeltaTime * 12f);
            }

            if (landed >= count) Land();
        }

        /// <summary>Puts the wallet where it ends, with nothing in the air.</summary>
        private void Land()
        {
            _flying = false;

            foreach (RectTransform coin in _coins)
            {
                if (coin != null) coin.gameObject.SetActive(false);
            }

            if (_pill != null) _pill.localScale = Vector3.one;

            Wallet(_held);
        }

        private void Wallet(int gold)
        {
            if (_wallet == null) return;

            _wallet.text = gold.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _wallet.color = Lit;
        }

        private void Paint()
        {
            if (!_have)
            {
                Put(_title, "NO RUN", Faint);
                Put(_titleUnder, "to show yet.", Faint);
                Put(_score, string.Empty, Ink);
                Put(_note, string.Empty, Faint);
                Put(_rate, string.Empty, Faint);
                Put(_stats, string.Empty, Faint);
                return;
            }

            Put(_title, _card.Title, Ink);
            Put(_titleUnder, _card.TitleUnder, Ink);

            Put(_score, _card.Score.ToString(), _card.Best ? Lit : Ink);
            Put(_note, _card.Note, _card.Best ? Lit : Faint);

            // Absent rather than blank when there is no rate to show: the line is an explanation,
            // and an explanation of nothing is a gap the eye stops on.
            Put(_rate, _card.Rate ?? string.Empty, Faint);

            Put(_stats, "FLOOR " + _card.Floor + "   KILLS " + _card.Kills +
                        "   GOLD " + _card.Banked + "   XP " + _card.Xp, Ink);
        }

        private static void Put(TMP_Text text, string what, Color ink)
        {
            if (text == null) return;

            text.text = what;
            text.color = ink;
        }
    }
}
