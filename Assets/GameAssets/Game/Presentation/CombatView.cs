using System.Collections.Generic;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Game.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// Draws a fight. Decides nothing about it.
    /// </summary>
    /// <remarks>
    /// Everything on screen comes out of <see cref="FightFrame"/>, which is derived from the
    /// event and the events after it and nothing else. So this holds no combat state: no current
    /// HP, no whose-turn-is-it, no how-far-along-are-we. It cannot get out of step with the fight
    /// because it is not keeping step — it is being told, one event at a time.
    ///
    /// That is invariant 5, and it is worth more than it sounds. The source's view accumulated
    /// state as it played, which is why a revive mid-fight needed <c>_carry</c> to put the screen
    /// back together and why pausing had a whole mechanism behind it. Here a screen showing event
    /// forty knows exactly what a screen jumping straight to event forty knows.
    /// </remarks>
    public sealed class CombatView : MonoBehaviour, IPlaybackScreen
    {
        [Header("The delver")]
        [SerializeField] private Image _heroHealth;
        [SerializeField] private Image _heroGauge;
        [SerializeField] private TMP_Text _heroHealthText;
        [SerializeField] private RectTransform _heroFliers;

        [Header("The foe")]
        [SerializeField] private Image _enemyHealth;
        [SerializeField] private Image _enemyGauge;
        [SerializeField] private TMP_Text _enemyName;

        [Tooltip("HP, ATK, DEF, SPD, LCK. The delver's are on their own panel; these were nowhere.")]
        [SerializeField] private TMP_Text _enemyStats;

        [Tooltip("What the foe is carrying. Icons only — the gauges would be the delver's.")]
        [SerializeField] private RelicTray _enemyRelics;
        [SerializeField] private Image _enemyArt;
        [SerializeField] private RectTransform _enemyFliers;

        [Header("The purse")]
        [SerializeField] private TMP_Text _gold;
        [SerializeField] private RectTransform _goldFliers;

        [Header("The log")]
        [Tooltip("The hall behind everything, which slides one stride per foe.")]
        [SerializeField] private HallView _hall;

        [SerializeField] private RelicTray _tray;

        [SerializeField] private RectTransform _log;
        [SerializeField] private FlyingNumber _flier;
        [SerializeField] private LogLine _line;

        [Tooltip("The source keeps sixty. Older lines are gone rather than merely clipped.")]
        [SerializeField] private int _logLength = 60;

        [Header("Content")]
        [SerializeField] private GameContent _content;

        private IReadOnlyList<CombatEvent> _events;
        private Pacing _pacing;
        private CombatLog _reading;
        private int _heroMax;
        private readonly List<LogLine> _lines = new List<LogLine>();

        /// <summary>Which foe is currently drawn, so a redraw only happens when one arrives.</summary>
        /// <remarks>
        /// Minus one rather than zero: zero is a real species, and a first foe of species zero
        /// would otherwise never be drawn at all.
        /// </remarks>
        private int _foeDrawn = -1;
        private int _foeVariant = -1;

        /// <summary>Whether the delver has stopped to look at something.</summary>
        public bool Paused { get; set; }

        /// <summary>
        /// Takes a fight, before any of it is shown.
        /// </summary>
        /// <remarks>
        /// The whole event list, because a gauge fills over the time until its owner strikes
        /// NEXT — which is a fact about events that have not been drawn yet. Handing over one
        /// event at a time would make the bars guess.
        /// </remarks>
        public void Begin(IReadOnlyList<CombatEvent> events, Pacing pacing, CombatLog reading,
            Shelf shelf = null, bool versus = false, int hall = 1)
        {
            _events = events;
            _pacing = pacing;
            _reading = reading;

            // Once, because the shelf does not change during a floor. Everything that DOES change
            // reaches the tray through the snapshot on each event.
            if (_tray != null) _tray.Begin(shelf, versus);

            // Counted from the events rather than passed in, because the number of foes IS the
            // number of arrivals — and a pack that gains a foe mid-floor gains an Enter with it,
            // so the strides stay even without anybody remembering to say so.
            if (_hall != null) _hall.Begin(Arrivals(events), hall);

            _heroMax = 0;
            if (events != null)
            {
                foreach (CombatEvent shown in events)
                {
                    if (shown.State.HeroHp > _heroMax) _heroMax = shown.State.HeroHp;
                }
            }

            foreach (LogLine line in _lines) Destroy(line.gameObject);
            _lines.Clear();

            _foeDrawn = -1;
            _foeVariant = -1;

            // Emptied, because a Filled image has to be authored full or there is nothing to see
            // while building the prefab. Left alone, both gauges would sit at full until the
            // first event moved them — and a full attack gauge means "about to strike", so the
            // fight would open by claiming both sides were mid-swing.
            Empty(_heroGauge);
            Empty(_enemyGauge);
        }

        private static void Empty(Image gauge)
        {
            if (gauge == null) return;

            var winding = gauge.GetComponent<WindingGauge>();
            if (winding != null) winding.Stop();

            gauge.fillAmount = 0f;
        }

        /// <summary>Draws one event.</summary>
        public void Show(int index, CombatEvent shown)
        {
            Bars(shown.State);

            if (_events == null || index < 0 || index >= _events.Count) return;

            FightFrame frame = FightFrame.Of(_events, index, _pacing, _reading);

            Fill(_heroGauge, frame.Hero);
            Fill(_enemyGauge, frame.Enemy);

            foreach (Flier flier in frame.Fliers) Throw(flier);

            if (frame.Line.Shown) Say(frame.Line);

            if (_tray != null) _tray.Show(shown);
        }

        /// <summary>How many foes turn up on this floor, which is how many arrivals it has.</summary>
        private static int Arrivals(IReadOnlyList<CombatEvent> events)
        {
            if (events == null) return 0;

            var many = 0;

            foreach (CombatEvent shown in events)
            {
                if (shown.Type == CombatEventType.Enter) many++;
            }

            return many;
        }

        /// <summary>Sets off down the hall to meet whoever is entering.</summary>
        /// <remarks>
        /// Two things at once, and the order matters: the hall sets off, and the foe waiting at
        /// the end of it becomes the one on screen. Drawing the foe first would have it standing
        /// in the old room for a frame.
        /// </remarks>
        public void Walk(int index)
        {
            if (_events == null || index < 0 || index >= _events.Count) return;

            if (_hall != null) _hall.Walk();

            Foe(_events[index].State);
        }

        /* ---------- drawing ---------- */

        private void Bars(CombatSnapshot state)
        {
            Fraction(_heroHealth, state.HeroHp, HeroMax(state));
            Fraction(_enemyHealth, state.EnemyHp, state.EnemyMaxHp);

            if (_heroHealthText != null) _heroHealthText.text = state.HeroHp.ToString();
            if (_gold != null) _gold.text = state.Gold.ToString();

            Stats(state);
            Foe(state);
        }

        /// <summary>
        /// What the foe is, in numbers.
        /// </summary>
        /// <remarks>
        /// The delver's stats are on their own panel and the foe's were nowhere at all, so the
        /// only thing on screen about the thing hitting you was a red bar and a name. Armour is
        /// labelled DEF because that is what the source calls it where a delver reads it, and a
        /// screen that used the engine's word for it would be the only place in the game that
        /// did.
        ///
        /// One text rather than ten, coloured with rich text. The colours are the source's and
        /// they live here, like every other colour in this layer — a view-model handing out hex
        /// values would be choosing the palette from inside Core.
        /// </remarks>
        private void Stats(CombatSnapshot state)
        {
            if (_enemyStats == null) return;

            _enemyStats.text =
                Stat("HP", state.EnemyHp + "/" + state.EnemyMaxHp, "C4593C") +
                Stat("ATK", state.EnemyAtk.ToString(), "E7E0D2") +
                Stat("DEF", state.EnemyArmor.ToString(), "AEB6C0") +
                Stat("SPD", state.EnemySpd.ToString(), "7C9A6A") +
                Stat("LCK", state.EnemyLck.ToString(), "E3B341");
        }

        private static string Stat(string name, string value, string colour)
        {
            return "<color=#8B8172>" + name + "</color> <color=#" + colour + ">" + value +
                   "</color>  ";
        }

        /// <summary>
        /// Draws whoever is standing there, and only when it changes.
        /// </summary>
        /// <remarks>
        /// Guarded because this used to run on every event: a sprite lookup and, now, a relic row
        /// that would be torn down and rebuilt two hundred times a fight — losing any flash it
        /// was in the middle of and doing it to draw the same thing again.
        /// </remarks>
        private void Foe(CombatSnapshot state)
        {
            if (_content == null) return;
            if (state.EnemyIndex == _foeDrawn && state.EnemyVariant == _foeVariant) return;

            _foeDrawn = state.EnemyIndex;
            _foeVariant = state.EnemyVariant;

            Carrying(state);

            if (_enemyArt != null && _content.Enemies != null)
            {
                Sprite art = _content.Enemies.For(state.EnemyIndex, state.EnemyVariant);
                _enemyArt.sprite = art;
                _enemyArt.enabled = art != null;
            }

            if (_enemyName != null) _enemyName.text = Named(state.EnemyIndex);
        }

        /// <summary>
        /// The foe's relics, as icons and nothing else.
        /// </summary>
        /// <remarks>
        /// No gauges, deliberately. A relic meter is built from the counters in the snapshot, and
        /// those counters are the DELVER's — how many times they have struck, been hit, taken
        /// gold. Drawing them under a foe's relic would be showing the delver's progress on
        /// somebody else's equipment, which is worse than showing nothing: it would look like
        /// information.
        ///
        /// A foe carries relics, not copies with sockets: nothing bolts a trigger to a bat. So
        /// the shelf is built plainly, and because the tray blanks every gauge when it binds, a
        /// tray that is begun and never shown is exactly the row of icons this wants.
        /// </remarks>
        private void Carrying(CombatSnapshot state)
        {
            if (_enemyRelics == null) return;

            var copies = new List<RelicCopy>();

            if (state.EnemyRelics != null)
            {
                foreach (RelicId relic in state.EnemyRelics) copies.Add(new RelicCopy(relic));
            }

            _enemyRelics.Begin(new Shelf(copies), false);
        }

        private string Named(int species)
        {
            if (species < 0 || species >= EnemyCatalog.All.Count) return "";

            return EnemyCatalog.Get(species).Key;
        }

        /// <summary>
        /// A health bar, which is a fraction and not a number.
        /// </summary>
        /// <remarks>
        /// Guarded against a maximum of nothing. A foe whose recorded maximum is zero would make
        /// this a division by zero, and a bar showing NaN renders as an empty bar — the same as
        /// a dead foe, and wrong in the one place a delver is looking.
        /// </remarks>
        private static void Fraction(Image bar, int now, int most)
        {
            if (bar == null) return;

            bar.fillAmount = most <= 0 ? 0f : Mathf.Clamp01(now / (float)most);
        }

        /// <summary>
        /// The delver's ceiling, which no snapshot carries directly.
        /// </summary>
        /// <remarks>
        /// An event records the delver's HP and the FOE's maximum, never the delver's own. The
        /// highest HP seen across the fight is the best available answer, and is right unless the
        /// ceiling itself rises mid-fight — an Ox Heart drafted between blows, which cannot
        /// happen. Left as a question rather than hidden: if the snapshot ever carries the
        /// ceiling, read that instead and delete this.
        ///
        /// Worked out once when the fight arrives rather than per event. Scanning every event to
        /// draw every event is a fight's length squared, which is nothing at three hundred events
        /// and is still the wrong shape.
        /// </remarks>
        private int HeroMax(CombatSnapshot state)
        {
            return _heroMax > 0 ? _heroMax : state.HeroHp;
        }

        /// <summary>
        /// Sets a gauge filling over the time until its owner strikes again.
        /// </summary>
        /// <remarks>
        /// A duration of nothing means the blow is already landing, so the bar is simply full.
        /// <see cref="Gauge.KeepTheCadence"/> means this unit never strikes again — it dies
        /// first — and the bar restarts at whatever it last used, so a doomed unit keeps winding
        /// up rather than freezing and giving the ending away.
        /// </remarks>
        private void Fill(Image gauge, Gauge fill)
        {
            if (gauge == null || !fill.Changed) return;

            var winding = gauge.GetComponent<WindingGauge>();
            if (winding == null) winding = gauge.gameObject.AddComponent<WindingGauge>();

            if (fill.Ms == Gauge.KeepTheCadence) winding.Again();
            else winding.Over(fill.Ms);
        }

        private void Throw(Flier flier)
        {
            if (_flier == null) return;

            RectTransform where = flier.Where == FlierAt.Hero ? _heroFliers
                : flier.Where == FlierAt.Gold ? _goldFliers
                : _enemyFliers;

            if (where == null) return;

            FlyingNumber thrown = Instantiate(_flier, where);
            thrown.Say(flier.Kind, flier.Text);
        }

        /// <summary>
        /// Adds a line to the log, and forgets the oldest when there are too many.
        /// </summary>
        /// <remarks>
        /// The source keeps two hundred and forty and trims from the front. A log that kept
        /// everything would be a delve-long list of every blow, which is both a memory leak and
        /// nothing anybody scrolls.
        /// </remarks>
        private void Say(CombatLine line)
        {
            if (_line == null || _log == null) return;

            LogLine added = Instantiate(_line, _log);
            added.Read(line);
            _lines.Add(added);

            while (_lines.Count > _logLength)
            {
                Destroy(_lines[0].gameObject);
                _lines.RemoveAt(0);
            }
        }
    }
}
