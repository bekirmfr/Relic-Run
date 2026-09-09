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
        [SerializeField] private Image _enemyArt;
        [SerializeField] private RectTransform _enemyFliers;

        [Header("The purse")]
        [SerializeField] private TMP_Text _gold;
        [SerializeField] private RectTransform _goldFliers;

        [Header("The log")]
        [SerializeField] private RelicTray _tray;

        [SerializeField] private RectTransform _log;
        [SerializeField] private FlyingNumber _flier;
        [SerializeField] private LogLine _line;

        [Tooltip("How many lines the log keeps. The source keeps 240.")]
        [SerializeField] private int _logLength = 240;

        [Header("Content")]
        [SerializeField] private GameContent _content;

        private IReadOnlyList<CombatEvent> _events;
        private Pacing _pacing;
        private CombatLog _reading;
        private int _heroMax;
        private readonly List<LogLine> _lines = new List<LogLine>();

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
            Shelf shelf = null, bool versus = false)
        {
            _events = events;
            _pacing = pacing;
            _reading = reading;

            // Once, because the shelf does not change during a floor. Everything that DOES change
            // reaches the tray through the snapshot on each event.
            if (_tray != null) _tray.Begin(shelf, versus);

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

        /// <summary>Sets off down the hall to meet whoever is entering.</summary>
        /// <remarks>
        /// Not implemented as motion yet — the hall pan is its own piece of work. What matters
        /// now is that the foe on screen becomes the one being walked to, so a delver watching
        /// sees the right creature when the fight starts.
        /// </remarks>
        public void Walk(int index)
        {
            if (_events == null || index < 0 || index >= _events.Count) return;

            Foe(_events[index].State);
        }

        /* ---------- drawing ---------- */

        private void Bars(CombatSnapshot state)
        {
            Fraction(_heroHealth, state.HeroHp, HeroMax(state));
            Fraction(_enemyHealth, state.EnemyHp, state.EnemyMaxHp);

            if (_heroHealthText != null) _heroHealthText.text = state.HeroHp.ToString();
            if (_gold != null) _gold.text = state.Gold.ToString();

            Foe(state);
        }

        private void Foe(CombatSnapshot state)
        {
            if (_content == null) return;

            if (_enemyArt != null && _content.Enemies != null)
            {
                Sprite art = _content.Enemies.For(state.EnemyIndex, state.EnemyVariant);
                _enemyArt.sprite = art;
                _enemyArt.enabled = art != null;
            }

            if (_enemyName != null) _enemyName.text = Named(state.EnemyIndex);
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
