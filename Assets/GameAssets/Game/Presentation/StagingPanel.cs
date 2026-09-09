using System;
using System.Collections.Generic;
using RelicRun.Core.Presentation;
using RelicRun.Game.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The staging hall: where a versus match is fought, and who is in it.
    /// </summary>
    /// <remarks>
    /// The same grid of halls the dungeon list uses, down to the tile prefab — the arena borrows
    /// the same ten halls, and a delver who has learned what the squares mean in one screen
    /// should not have to learn it again in the other.
    ///
    /// What differs is the clamp. The dungeon list will describe a hall the delver cannot enter,
    /// because knowing what is down there is the reward for getting close. The arena will not
    /// offer one, because offering a hall it would then refuse is telling somebody yes and then
    /// no.
    /// </remarks>
    public sealed class StagingPanel : MetaPanel
    {
        [SerializeField] private HallTileView _tile;
        [SerializeField] private RectTransform _grid;

        [SerializeField] private TMP_Text _hall;
        [SerializeField] private TMP_Text _roster;

        [SerializeField] private Button _fight;
        [SerializeField] private TMP_Text _fightLabel;
        [SerializeField] private Button _back;

        /// <summary>What the button says when the arena can be entered.</summary>
        public const string FightLabel = "ENTER THE ARENA";

        /// <summary>And when it cannot.</summary>
        public const string ShutLabel = "LOCKED";

        private static readonly Color Ready = new Color(0.89f, 0.70f, 0.25f);

        private static readonly Color Shut = new Color(0.29f, 0.27f, 0.21f);

        private static readonly Color Ink = new Color(0.90f, 0.87f, 0.80f);

        private static readonly Color Faint = new Color(0.42f, 0.40f, 0.35f);

        /// <summary>Ink for a label sitting on the gold button, which needs to be dark.</summary>
        private static readonly Color Dark = new Color(0.08f, 0.07f, 0.06f);

        private readonly List<HallTileView> _tiles = new List<HallTileView>();

        private SaveVault _vault;
        private int _chosen;

        public override Page Shows
        {
            get { return Page.Staging; }
        }

        private void Awake()
        {
            if (_back != null) _back.onClick.AddListener(() => Go(Page.Title));
            if (_fight != null) _fight.onClick.AddListener(Enter);
        }

        public override void Draw(SaveVault vault, DateTimeOffset now)
        {
            _vault = vault;

            // The remembered arena hall is a setting of its own, kept apart from the dungeons'.
            // A delver who fights in the deepest hall and delves in the shallowest is doing two
            // different things, and one number could not hold both.
            if (_chosen <= 0) _chosen = vault.Chosen.VersusHall;

            Show(StagingCards.Of(vault.Earned, _chosen, 0));
        }

        private void Show(StagingCard card)
        {
            _chosen = card.Chosen;

            Tiles(card.Halls);

            Put(_hall, card.HallName, Ink);
            Put(_roster, card.Roster, Faint);
            Put(_fightLabel, card.Open ? FightLabel : ShutLabel, card.Open ? Dark : Faint);

            if (_fight != null)
            {
                _fight.interactable = card.Open;

                var face = _fight.targetGraphic as Image;
                if (face != null) face.color = card.Open ? Ready : Shut;
            }
        }

        private void Tiles(IReadOnlyList<ArenaHall> halls)
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

                if (!used) continue;

                ArenaHall hall = halls[i];

                // Dressed through the dungeon list's own tile, so the two screens cannot drift
                // apart about what a locked square looks like. An arena hall is never "cleared":
                // clearing belongs to the dungeons, and a tick here would claim something that
                // did not happen.
                _tiles[i].Show(new HallTile
                {
                    Tier = hall.Tier,
                    Unlocked = hall.Open,
                    Cleared = false,
                    Chosen = hall.Chosen,
                    Tag = hall.Open ? LevelsCards.OpenTag : string.Empty,
                });
            }
        }

        private void Pick(int tier)
        {
            if (_vault == null) return;

            _chosen = tier;

            Show(StagingCards.Of(_vault.Earned, _chosen, 0));
        }

        /// <summary>
        /// Enters the arena.
        /// </summary>
        /// <remarks>
        /// The hall is remembered and committed BEFORE the match starts, so a delver who closes
        /// the game mid-match comes back to the screen pointing where they were.
        ///
        /// What it goes to is the run, which is currently the fight scaffold rather than a versus
        /// match — the versus engine exists and is gated, and nothing yet drives it from a
        /// screen. This is one of the two places that changes when it does.
        /// </remarks>
        private void Enter()
        {
            if (_vault != null)
            {
                _vault.Chosen.VersusHall = _chosen;
                _vault.CommitChoices();
            }

            Go(Page.Run);
        }

        private static void Put(TMP_Text text, string what, Color ink)
        {
            if (text == null) return;

            text.text = what;
            text.color = ink;
        }
    }
}
