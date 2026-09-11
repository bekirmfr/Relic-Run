using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using GameLift.Audio;
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

        [Tooltip("The delver's four, drawn with the same chip the foe's are.")]
        [SerializeField] private StatRow _heroStats;
        [SerializeField] private RectTransform _heroFliers;

        [Tooltip("The delver, composed from the wardrobe rather than drawn off a sheet.")]
        [SerializeField] private HeroView _heroArt;

        [Header("The foe")]
        [SerializeField] private Image _enemyHealth;
        [SerializeField] private Image _enemyGauge;
        [SerializeField] private TMP_Text _enemyName;

        [Tooltip("ATK, DEF, SPD and LCK as chips. The pool is the bar's job, not a chip's.")]
        [SerializeField] private StatRow _enemyStats;

        [Tooltip("What the foe is carrying. Icons only — the gauges would be the delver's.")]
        [SerializeField] private RelicTray _enemyRelics;
        [SerializeField] private Image _enemyArt;

        [Tooltip("The card a fight opens on. Raised on the walk to meet whoever is arriving.")]
        [SerializeField] private IntroBanner _intro;

        [Tooltip("The row of foes on this floor. Hidden when there is only one.")]
        [SerializeField] private FoeQueueView _queue;

        [Tooltip("Carries a foe from its card into the frame it is fought in.")]
        [SerializeField] private FoeFlight _flight;

        [Tooltip("The pause button and its menu. Shown only while a fight is being read out.")]
        [SerializeField] private PauseGate _pause;
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

        [Tooltip("One speck of grit or blood. Spawned by the handful, never pooled.")]
        [SerializeField] private Mote _mote;

        [Tooltip("The source keeps sixty. Older lines are gone rather than merely clipped.")]
        [SerializeField] private int _logLength = 60;

        [Header("Content")]
        [SerializeField] private GameContent _content;

        /// <summary>
        /// Whoever plays the noises, handed over rather than found or serialized.
        /// </summary>
        /// <remarks>
        /// Not an <c>[Inject]</c> method, and that is a correction rather than a preference. This
        /// project's scenes register their components in a <c>LifetimeScope</c> installer before
        /// injection reaches them — the menu does exactly that — and the fight has no installer.
        /// An attribute here would have looked like wiring and done nothing, which is the worst
        /// of both: a screen with no sound and no error.
        ///
        /// Nor a serialized field. The audio service is a run-time thing, and a reference to one
        /// in a prefab is a second way of getting one — which is how a screen ends up holding a
        /// different service from the rest of the game and nobody notices until the mute button
        /// only half works.
        /// </remarks>
        private IAudioService _audio;

        /// <summary>Hands this screen the thing that makes noise.</summary>
        public void Hear(IAudioService audio)
        {
            _audio = audio;
        }

        private IReadOnlyList<CombatEvent> _events;
        private Pacing _pacing;

        /// <summary>
        /// The numbers everything on this screen is timed by.
        /// </summary>
        /// <remarks>
        /// The pacing a FIGHT is playing at when there is one, and the authored settings when
        /// there is not. That second half matters more than it looks: the first draft of a run
        /// happens before any fight has begun, so <c>_pacing</c> is still null there — and a
        /// relic taken on floor one flew for nought seconds, which is to say it did not fly at
        /// all while every relic after it did.
        /// </remarks>
        private PacingRules Rules
        {
            get
            {
                if (_pacing != null) return _pacing.Rules;

                return _content != null && _content.Presentation != null
                    ? _content.Presentation.ToPacing()
                    : null;
            }
        }
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

        /// <summary>
        /// Whether the foe's frame is being kept empty on purpose.
        /// </summary>
        /// <remarks>
        /// True from the moment the walk sets off until whatever was announced has flown into
        /// the frame. A delver should not see the thing they are about to be introduced to
        /// standing there while they are walking toward it — the source hides the same glyph for
        /// the same span, and the announcement is worth nothing if the surprise is already up.
        /// </remarks>
        private bool _hidden;

        /// <summary>
        /// Who is on this floor, in the order they arrive.
        /// </summary>
        /// <remarks>
        /// Passed in rather than read out of the events, because the events only ever show the
        /// foe that has ARRIVED — a snapshot carries one enemy — and the row along the bottom is
        /// about the ones who have not. Null on a screen nobody handed a pack to, which draws no
        /// row at all rather than a wrong one.
        /// </remarks>
        private IReadOnlyList<EnemyState> _pack;

        /// <summary>Whether the delver has stopped to look at something.</summary>
        /// <summary>
        /// Whether the fight is stopped, which is the gate's answer rather than this view's.
        /// </summary>
        /// <remarks>
        /// Asked by the playback loop once a turn round the loop. It reads through to
        /// <see cref="PauseGate"/> so there is exactly one thing in the scene that knows whether
        /// a fight is running — a second copy of that flag is a second thing to get out of step.
        /// </remarks>
        public bool Paused
        {
            get { return _pause != null && _pause.Paused; }
        }

        /// <summary>Whether the pause button is on the screen. A fight's, and nothing else's.</summary>
        public bool Pausable
        {
            set { if (_pause != null) _pause.Offered = value; }
        }

        /// <summary>The gate itself, for whoever needs to know the delver walked out.</summary>
        public PauseGate Gate
        {
            get { return _pause; }
        }

        /// <summary>
        /// Whether the delver has pressed FIGHT on the card in front of them.
        /// </summary>
        /// <remarks>
        /// Asked by the loop, once a frame, and only while a card is up. False when there is no
        /// card and false when there is no button on it, so a screen with neither simply lets the
        /// card run its three seconds.
        /// </remarks>
        public bool Impatient
        {
            get { return _intro != null && _intro.Showing && _intro.Hurried; }
        }

        /// <summary>
        /// Hands the card a fight opens on the delver's language.
        /// </summary>
        /// <remarks>
        /// Not fetched here. Fetching strings is asynchronous and everything on this screen is
        /// drawn from a synchronous callback, so the scene that already waited for them hands
        /// them over.
        /// </remarks>
        public void Speaks(Locale words)
        {
            if (_intro != null) _intro.Words = words;
        }

        /// <summary>
        /// Takes a fight, before any of it is shown.
        /// </summary>
        /// <remarks>
        /// The whole event list, because a gauge fills over the time until its owner strikes
        /// NEXT — which is a fact about events that have not been drawn yet. Handing over one
        /// event at a time would make the bars guess.
        /// </remarks>
        public void Begin(IReadOnlyList<CombatEvent> events, Pacing pacing, CombatLog reading,
            Shelf shelf = null, bool versus = false, int hall = 1,
            IReadOnlyList<EnemyState> pack = null)
        {
            _events = events;
            _pacing = pacing;
            _reading = reading;
            _pack = pack;

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

            // Down before the first event, because a floor can open on a card and this is the
            // only place that knows a NEW fight has started. Left up, the card from the last
            // floor's last foe would be the first thing the next floor showed.
            if (_intro != null) _intro.Hide();

            if (_flight != null) _flight.Stop();

            _hidden = false;

            Dress();

            Queue(-1);

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

        /// <summary>
        /// Opens the bazaar floor: its own hall, one figure in it, and no fight.
        /// </summary>
        /// <remarks>
        /// The same shape a fought floor has — a hall with somebody in the middle of it — which
        /// is the point. A delver walks onto floor seven exactly as they walk onto any other and
        /// finds out what is there when the card comes up.
        ///
        /// ONE stride to the middle, because a floor of one foe is two strides and the merchant
        /// stands where that foe would. The second stride is the walk out, after the deal.
        /// </remarks>
        public void Trading(Sprite merchant)
        {
            _events = null;
            _pacing = _content != null && _content.Presentation != null
                ? Pacing.For(0, false, 1, _content.Presentation.ToPacing())
                : _pacing;

            if (_intro != null) _intro.Trader = merchant;

            if (_flight != null) _flight.Stop();

            _hidden = true;

            if (_queue != null) _queue.Hide();

            // One foe's worth of hall, and the bazaar's own walls.
            if (_hall != null)
            {
                _hall.Begin(1, 1, true);
                _hall.Walk();
            }

            // Nothing in the foe's frame. The merchant is met on the card and never stands in
            // the slot a thing that hits you would.
            if (_enemyArt != null) _enemyArt.enabled = false;

            if (_enemyName != null) _enemyName.text = string.Empty;

            _foeDrawn = -1;
            _foeVariant = -1;
        }

        /// <summary>Puts the merchant's card up, with whichever line this visit drew.</summary>
        public void Met(int line)
        {
            if (_intro == null) return;

            PacingRules said = Rules;

            _intro.Held = said != null ? said.IntroMs / 1000f : 0f;

            _intro.Show(IntroCards.Trader(line));
        }

        /// <summary>Takes the merchant's card down. The delver has chosen to trade.</summary>
        public void Traded()
        {
            if (_intro != null) _intro.Hide();
        }

        /// <summary>And walks the rest of the bazaar's hall, the visit being over.</summary>
        public void LeaveTrading()
        {
            if (_intro != null) _intro.Hide();

            if (_hall != null) _hall.Stretch();
        }

        /// <summary>
        /// Walks the hall down to the floor below.
        /// </summary>
        /// <remarks>
        /// Forwarded rather than reached through. The hall is this view's scenery and nothing
        /// outside it should know the hall exists — the scene knows it is descending, and this
        /// knows what descending looks like.
        /// </remarks>
        public void Descend(float seconds)
        {
            if (_hall != null) _hall.Descend(seconds);
        }

        /// <summary>
        /// Puts a newly taken relic on the shelf, and flies its picture there.
        /// </summary>
        /// <remarks>
        /// The same beat a foe gets, for the same reason: a thing that appears is a thing a
        /// delver has to go looking for, and a thing that travels is one they watched arrive. It
        /// is the only moment in a run where the shelf changes while somebody is watching it.
        ///
        /// The slot is built FIRST and then left empty. Building it after would land the flight
        /// on a shelf that has not made room yet, and the icon would settle a slot's width to the
        /// left of where it belongs.
        /// </remarks>
        public void Shelve(Shelf shelf, Sprite icon, RectTransform from, Action landed)
        {
            if (_tray == null)
            {
                if (landed != null) landed();
                return;
            }

            _tray.Begin(shelf, false);

            int at = _tray.Count - 1;

            RectTransform to = _tray.Where(at);

            PacingRules said = Rules;

            float over = said != null ? said.FlyMs / 1000f : 0f;

            if (_flight == null || icon == null || from == null || to == null || over <= 0f)
            {
                if (landed != null) landed();
                return;
            }

            // Laid out NOW, so the flight aims at where the slot actually ends up rather than at
            // wherever the row happened to be before it grew.
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_tray.transform);

            _tray.Veil(at, true);

            _flight.Fly(icon, from, to, over, () =>
            {
                _tray.Veil(at, false);

                if (landed != null) landed();
            });
        }

        /// <summary>Loads the hall below before the walk down starts, so it is seen arriving.</summary>
        public UniTask Ready(int tier, bool bazaar)
        {
            return _hall != null ? _hall.Ready(tier, bazaar) : UniTask.CompletedTask;
        }

        /// <summary>Draws one event.</summary>
        public void Show(int index, CombatEvent shown)
        {
            // The first thing anything does, so a blow is never struck behind the card that
            // announced the foe taking it — and the foe it announced is thrown into its frame on
            // the way past.
            if (_intro != null && _intro.Showing) Arrive();

            Queue(index);

            Acted(shown);

            Bars(shown.State);

            if (_events == null || index < 0 || index >= _events.Count) return;

            FightFrame frame = FightFrame.Of(_events, index, _pacing, _reading);

            Fill(_heroGauge, frame.Hero);
            Fill(_enemyGauge, frame.Enemy);

            foreach (Flier flier in frame.Fliers) Throw(flier);

            if (frame.Line.Shown) Say(frame.Line);

            Sound(frame.Sound);

            if (_tray != null) _tray.Show(shown);

            Throw(Sprays.Of(shown));
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

            // Nobody meets a foe before they are introduced. The frame is emptied as the walk
            // sets off and stays empty through the card, so what arrives in it is the thing that
            // flies out of the announcement rather than something already standing there.
            _hidden = true;

            if (_hall != null) _hall.Walk();

            Foe(_events[index].State);

            // The row FIRST, so the foe walked toward is already ringed by the time the card
            // over them goes up — rather than the row catching up a floor later.
            Queue(index);
        }

        /// <summary>
        /// Announces whoever the walk arrived at.
        /// </summary>
        /// <remarks>
        /// A separate beat from <see cref="Walk"/>, and the order matters more than it looks: the
        /// card is opaque and covers the whole screen, so raising it as the walk SET OFF played
        /// the entire approach behind it. The hall slid for three and a half seconds where nobody
        /// could see it, which made the walk read as a pause with nothing in it.
        /// </remarks>
        public void Meet(int index)
        {
            if (_events == null || index < 0 || index >= _events.Count) return;

            if (_intro == null) return;

            // Its own length, so the timer behind the button drains at the rate the loop is
            // actually holding it for rather than at a number typed in twice.
            PacingRules said = Rules;

            _intro.Held = said != null ? said.IntroMs / 1000f : 0f;

            _intro.Show(IntroCards.Of(_events[index].State));
        }

        /// <summary>
        /// Takes the card down and carries what was on it into the frame.
        /// </summary>
        /// <remarks>
        /// The join between the announcement and the fight, and the only reason the frame was
        /// kept empty. A flight that cannot be made — no flier wired, no picture, nothing to fly
        /// between — lands instantly rather than not at all, because the alternative is a foe
        /// nobody can see for the rest of the floor.
        /// </remarks>
        private void Arrive()
        {
            RectTransform from = _intro.Picture;
            Sprite drawn = _intro.Drawn;

            _intro.Hide();

            if (_flight == null)
            {
                Landed();
                return;
            }

            PacingRules said = Rules;

            float over = said != null ? said.FlyMs / 1000f : 0f;

            _flight.Fly(drawn, from, _enemyArt != null ? (RectTransform)_enemyArt.transform : null,
                over, Landed);
        }

        /// <summary>The foe is where it fights from now, so the frame may show it.</summary>
        private void Landed()
        {
            _hidden = false;

            if (_enemyArt != null) _enemyArt.enabled = _enemyArt.sprite != null;
        }

        /// <summary>Walks the rest of the hall, the floor being over.</summary>
        /// <remarks>
        /// Forwarded, like the descent. The scene knows a floor has been cleared; this knows what
        /// a cleared floor looks like.
        /// </remarks>
        public void Stretch()
        {
            if (_intro != null) _intro.Hide();

            if (_hall != null) _hall.Stretch();
        }

        /// <summary>
        /// Dresses the delver, once.
        /// </summary>
        /// <remarks>
        /// Once per SCREEN rather than once per floor. Composing a state builds three textures
        /// and a material that Unity will never collect on its own — see <see cref="HeroView"/> —
        /// so redressing every floor would leak a wardrobe a run.
        ///
        /// A missing pack is a warning and a fight with no delver in it, which is ugly and
        /// diagnosable. It is not worth stopping a run over.
        /// </remarks>
        private void Dress()
        {
            if (_heroArt == null || _heroArt.Dressed) return;

            HeroPack pack = _content != null && _content.HeroPack != null
                ? _content.HeroPack.Pack
                : null;

            if (pack == null)
            {
                Debug.LogWarning("no hero pack, so the delver is not drawn", this);
                return;
            }

            _heroArt.Wear(pack, HeroPackReader.Plain(pack), HeroPalette.Build());
        }

        /// <summary>
        /// What the delver is doing, which is whatever just happened to them.
        /// </summary>
        /// <remarks>
        /// Read off the event rather than tracked, like everything else on this screen. The two
        /// that matter are the two that are about the DELVER: a foe taking damage means the
        /// delver swung, and the delver taking it means they were hit. Everything else — gold,
        /// luck, a relic firing — leaves them standing, which is correct: a delver who lunged
        /// every time a number appeared would be lunging at their own purse.
        /// </remarks>
        private void Acted(CombatEvent shown)
        {
            if (_heroArt == null) return;

            switch (shown.Type)
            {
                case CombatEventType.EnemyDamage:
                    _heroArt.Act(HeroView.Attack);
                    break;

                case CombatEventType.PlayerDamage:
                    _heroArt.Act(HeroView.Hurt);
                    break;

                case CombatEventType.Death:
                    _heroArt.Act(HeroView.Die);
                    break;
            }
        }

        /// <summary>
        /// The row of foes, as far along as the fight is at this event.
        /// </summary>
        /// <remarks>
        /// Counted out of the events every time rather than tracked, because playback can be
        /// paused, sped up or skipped, and a counter walked along the way would be wrong in all
        /// three. Minus one means nothing has been shown yet, which is a whole pack standing.
        /// </remarks>
        private void Queue(int index)
        {
            if (_queue == null) return;

            if (_pack == null)
            {
                _queue.Hide();
                return;
            }

            _queue.Show(FoeQueues.Of(_pack, FoeQueues.Fallen(_events, index)));
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
        /// Both sides, from one description. This was a single string of rich text with the
        /// colours spliced into it, which was quick to write and could not be laid out, aligned
        /// or reused — and it existed only for the foe, so the screen said what it was fighting
        /// and not what it was fighting with.
        ///
        /// The pool is deliberately absent from both. It is read as a proportion and answered by
        /// a bar; these are read as figures and want a label beside them. Armour is labelled DEF
        /// because that is the source's word wherever a delver sees it — the engine's own name
        /// for it would make this the only place in the game that used it.
        /// </remarks>
        private void Stats(CombatSnapshot state)
        {
            if (_enemyStats != null) _enemyStats.Show(StatLines.Foe(state));
            if (_heroStats != null) _heroStats.Show(StatLines.Delver(state));
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

                // The sprite is set either way; only the SHOWING of it waits. Everything else on
                // this screen redraws from any event, so the picture has to be in place before a
                // delver could ever see the frame — what is deferred is the moment it appears.
                _enemyArt.enabled = art != null && !_hidden;
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

        /// <summary>
        /// Makes whatever noise this event makes, if it makes one.
        /// </summary>
        /// <remarks>
        /// Six of the twenty event kinds do. Which six is Core's answer and which CLIP is the
        /// audio service's — this only carries a name between them, so the screen never holds a
        /// filename and the service never holds an opinion about combat.
        ///
        /// Silence is an answer rather than a gap: most events name nothing, and asking for a
        /// clip on all of them would be twenty lookups a fight for names that do not exist.
        /// </remarks>
        private void Sound(FightSound sound)
        {
            if (_audio == null) return;

            string named = Blips.Named(sound);
            if (named == null) return;

            _audio.Play(named);
        }

        /// <summary>
        /// Throws whatever this event knocked off somebody.
        /// </summary>
        /// <remarks>
        /// Into the same anchor the flying numbers use, because it is the same place: the middle
        /// of the body something just happened to. A second anchor would be a second thing to
        /// keep level with the first.
        /// </remarks>
        private void Throw(Spray spray)
        {
            if (!spray.Any || _mote == null) return;

            RectTransform at = spray.OnDelver ? _heroFliers : _enemyFliers;
            if (at == null) return;

            for (var i = 0; i < spray.Count; i++)
            {
                Mote made = Instantiate(_mote, at);
                made.gameObject.SetActive(true);
                made.Throw(spray.Kind, spray.Shatters);
            }
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
