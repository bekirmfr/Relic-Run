using System;
using System.Collections.Generic;
using System.Text;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Game.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The dungeon list: ten halls, and what is down the one being read.
    /// </summary>
    /// <remarks>
    /// Every number and every caption comes off a <see cref="LevelsCard"/>. What is decided here
    /// is colour, and which tile is which — the tiles are built once and redressed, because ten
    /// squares rebuilt on every selection is ten objects destroyed and remade to change a border.
    /// </remarks>
    public sealed class LevelsPanel : MetaPanel
    {
        [SerializeField] private HallTileView _tile;
        [SerializeField] private RectTransform _grid;

        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _lore;
        [SerializeField] private RectTransform _stats;
        [SerializeField] private TMP_Text _statLine;
        [SerializeField] private TMP_Text _relics;

        [SerializeField] private Button _delve;
        [SerializeField] private TMP_Text _delveLabel;
        [SerializeField] private Button _back;

        private static readonly Color Ready = new Color(0.89f, 0.70f, 0.25f);

        private static readonly Color Shut = new Color(0.29f, 0.27f, 0.21f);

        private readonly List<HallTileView> _tiles = new List<HallTileView>();
        private readonly List<TMP_Text> _rows = new List<TMP_Text>();

        private SaveVault _vault;

        /// <summary>Which hall the delver is reading. Zero means the deepest one they have open.</summary>
        private int _chosen;

        public override Page Shows
        {
            get { return Page.Levels; }
        }

        private void Awake()
        {
            if (_delve != null) _delve.onClick.AddListener(Delve);
            if (_back != null) _back.onClick.AddListener(() => Go(Page.Title));
        }

        public override void Draw(SaveVault vault, DateTimeOffset now)
        {
            _vault = vault;

            // The remembered hall is a setting, so it survives leaving the screen and closing the
            // game. Read once, on the first draw, rather than every time — otherwise choosing a
            // hall and coming back would put the cursor back where it started.
            if (_chosen <= 0) _chosen = vault.Chosen.Tier;

            Show(LevelsCards.Of(vault.Earned, _chosen));
        }

        private void Show(LevelsCard card)
        {
            _chosen = card.Chosen;

            Tiles(card.Halls);
            Detail(card.Detail);
        }

        /// <summary>
        /// The grid, built once and redressed after.
        /// </summary>
        /// <remarks>
        /// The tile is a prefab and this spawns one per hall the first time. Ten halls is a
        /// number the catalog owns rather than one this screen knows, so the grid grows if the
        /// catalog does and nothing here has to be told.
        /// </remarks>
        private void Tiles(IReadOnlyList<HallTile> halls)
        {
            if (_tile == null || _grid == null) return;

            while (_tiles.Count < halls.Count)
            {
                HallTileView made = Instantiate(_tile, _grid);

                made.gameObject.SetActive(true);
                made.Picked += Pick;

                _tiles.Add(made);
            }

            for (var i = 0; i < _tiles.Count; i++)
            {
                bool used = i < halls.Count;

                _tiles[i].gameObject.SetActive(used);

                if (used) _tiles[i].Show(halls[i]);
            }
        }

        private void Detail(HallDetail hall)
        {
            Put(_title, hall.Title);
            Put(_lore, hall.Lore);
            Put(_relics, Carried(hall));

            Rows(hall.Stats);

            Put(_delveLabel, hall.CanDelve ? LevelsCards.DelveLabel : LevelsCards.SealedLabel);

            if (_delve != null)
            {
                // Not merely greyed: a sealed hall's button must not start anything. The label
                // says SEALED and the button refuses, which are two halves of one answer.
                _delve.interactable = hall.CanDelve;

                var face = _delve.targetGraphic as Image;
                if (face != null) face.color = hall.CanDelve ? Ready : Shut;
            }
        }

        /// <summary>
        /// The rows of numbers, spawned once and refilled.
        /// </summary>
        /// <remarks>
        /// Seven for an open hall and none for a sealed one, so the spare rows are hidden rather
        /// than destroyed — the delver moves between the two constantly while deciding where to
        /// go, and rebuilding seven text objects each time is work to produce the same seven text
        /// objects.
        /// </remarks>
        private void Rows(IReadOnlyList<HallStat> stats)
        {
            if (_statLine == null || _stats == null) return;

            while (_rows.Count < stats.Count)
            {
                TMP_Text made = Instantiate(_statLine, _stats);

                made.gameObject.SetActive(true);
                _rows.Add(made);
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                bool used = i < stats.Count;

                _rows[i].gameObject.SetActive(used);

                if (used) _rows[i].text = stats[i].Label + "  " + stats[i].Value;
            }
        }

        /// <summary>
        /// What the hall's king carries, by name.
        /// </summary>
        /// <remarks>
        /// The one thing on this screen Core does not hand over ready to draw, because a relic's
        /// name has a language and Core does not get to pick one. It is spelled in English here
        /// and this is the single call site — when the locale is wired through the screens, this
        /// is the line that changes and the only one.
        /// </remarks>
        private static string Carried(HallDetail hall)
        {
            if (hall.Sealed) return string.Empty;
            if (hall.BossRelics == null || hall.BossRelics.Count == 0) return LevelsCards.NoRelics;

            var names = new StringBuilder();

            foreach (RelicId relic in hall.BossRelics)
            {
                if (names.Length > 0) names.Append(" · ");

                names.Append(RelicText.Get(relic).Name);
            }

            return names.ToString();
        }

        private void Pick(int tier)
        {
            if (_vault == null) return;

            _chosen = tier;

            Show(LevelsCards.Of(_vault.Earned, _chosen));
        }

        /// <summary>
        /// Starts a delve into the hall being read.
        /// </summary>
        /// <remarks>
        /// The hall is remembered BEFORE the run starts, and committed, so a delver who closes
        /// the game mid-run comes back to the screen pointing where they were. The source does
        /// the same, and for the same reason.
        /// </remarks>
        private void Delve()
        {
            if (_vault != null)
            {
                _vault.Chosen.Tier = _chosen;
                _vault.CommitChoices();
            }

            Go(Page.Run);
        }

        private static void Put(TMP_Text text, string what)
        {
            if (text != null) text.text = what;
        }
    }
}
