using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RelicRun.Core.Content;
using RelicRun.Core.Run;
using RelicRun.Game.Data;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The thing waiting in the gap between two floors.
    /// </summary>
    /// <remarks>
    /// The only decision in a run made BLIND. A draft shows what a relic does, the gate shows
    /// what is below, the bazaar shows a price — an event shows a hint saying what a choice
    /// costs and never what it does, and nine of the twenty-six roll for it. So this is two
    /// beats on one screen: the choosing, and then the sentence saying what came of it.
    ///
    /// That second beat is why <see cref="RunStage.Tells"/> exists. The run answers an event in
    /// one stop, the way every recorded run has, and writes the outcome back onto the stop —
    /// which this reads out afterwards while the scene waits.
    /// </remarks>
    public sealed class EventStage : RunStage
    {
        [SerializeField] private TMP_Text _kicker;
        [SerializeField] private Image _art;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _text;

        [Tooltip("The choices. One is spawned per way out of the event.")]
        [SerializeField] private RectTransform _choices;

        [SerializeField] private Button _choice;

        [Header("What came of it")]
        [SerializeField] private GameObject _outcome;
        [SerializeField] private TMP_Text _outcomeText;
        [SerializeField] private Button _onward;
        [SerializeField] private TMP_Text _onwardLabel;
        [SerializeField] private GameContent _content;

        /// <summary>The kicker over every event. The source's own constant.</summary>
        public const string KickerKey = "betweenFloors";

        /// <summary>What the button under an outcome says.</summary>
        public const string OnwardKey = "continueDescent";

        /// <summary>And what a choice says when the purse cannot cover it.</summary>
        public const string ShortKey = "notEnoughGold";

        /// <summary>What sits between a hint and the reason a choice is dim.</summary>
        public const string Between = " · ";

        /// <summary>The violet an event is announced in, which nothing else in a run wears.</summary>
        private static readonly Color Kicker = new Color(0.608f, 0.545f, 0.816f);

        private static readonly Color Ink = new Color(0.906f, 0.878f, 0.824f);

        private static readonly Color Told = new Color(0.725f, 0.690f, 0.627f);

        private static readonly Color Faint = new Color(0.545f, 0.506f, 0.447f);

        /// <summary>An outcome that went well, and one that did not.</summary>
        private static readonly Color Well = new Color(0.486f, 0.604f, 0.416f);

        private static readonly Color Badly = new Color(0.769f, 0.349f, 0.235f);

        /// <summary>How dim a choice the purse cannot cover is drawn.</summary>
        private const float Beyond = 0.4f;

        private readonly List<Button> _spawned = new List<Button>();

        /// <summary>The art this stage loaded, so it can be let go of.</summary>
        /// <remarks>
        /// An AssetReference caches its handle on the asset, so loading one twice throws. A run
        /// meets several events and the same one can turn up in two runs of a session, which is
        /// exactly the shape that bit the halls and the locale book before it.
        /// </remarks>
        private AssetReferenceSprite _held;

        private int _showing = -1;

        public override AskKind Answers
        {
            get { return AskKind.Event; }
        }

        /// <summary>An event has something to say once it has been answered.</summary>
        public override bool Tells
        {
            get { return true; }
        }

        private void Awake()
        {
            if (_onward != null) _onward.onClick.AddListener(() => Decide(new Answer()));
        }

        /// <summary>Puts the event and its choices on the screen.</summary>
        public override void Draw(Ask ask, RunState run)
        {
            DungeonEvent ev = ask.Event;

            if (ev == null) return;

            EventTextDef said = EventText.Get(ev.Index);

            Write(_kicker, Say(KickerKey), Kicker);
            Write(_title, said.Title, Ink);
            Write(_text, said.Text, Told);

            if (_outcome != null) _outcome.SetActive(false);

            if (_choices != null) _choices.gameObject.SetActive(true);

            Art(ev.Index);
            Choices(ev, said, run.Gold);
        }

        /// <summary>
        /// Reads out what the choice did.
        /// </summary>
        /// <remarks>
        /// The same screen with its lower half swapped, which is the source's arrangement: the
        /// picture and the title stay, so a delver reads the answer in the place they asked the
        /// question rather than on a card that replaced it.
        /// </remarks>
        public override void Tell(Ask ask, RunState run)
        {
            if (_choices != null) _choices.gameObject.SetActive(false);

            if (_outcome != null) _outcome.SetActive(true);

            Write(_outcomeText, Filled(ask.Outcome), ask.Outcome.Good ? Well : Badly);
            Write(_onwardLabel, Say(OnwardKey), Ink);
        }

        /// <summary>
        /// The outcome, with the relic it handed over named.
        /// </summary>
        /// <remarks>
        /// Core leaves a hole rather than a name, because nineteen of the fifty relics have names
        /// that are translated and Core does not translate. This is the only place that hole is
        /// ever filled, so a sentence reaching a delver with <c>{relic}</c> still in it means the
        /// run granted something this screen did not notice.
        /// </remarks>
        private string Filled(EventOutcome outcome)
        {
            if (outcome.Text == null) return string.Empty;

            if (outcome.Relic == RelicId.None) return outcome.Text;

            RelicTextDef text = RelicText.Get(outcome.Relic);

            string named = text == null ? string.Empty
                : text.NameKey != null ? Say(text.NameKey)
                : text.Name;

            return outcome.Text.Replace("{relic}", named);
        }

        /// <summary>
        /// The ways out, spawned once and redressed.
        /// </summary>
        /// <remarks>
        /// A choice the purse cannot cover is dimmed and made inert rather than hidden. Knowing
        /// what sixty gold would have bought is part of what the next floor's gold is FOR, and an
        /// event that quietly showed one option to a poor delver and two to a rich one would be
        /// hiding the game's economy from the people it matters most to.
        /// </remarks>
        private void Choices(DungeonEvent ev, EventTextDef said, int gold)
        {
            if (_choice == null || _choices == null) return;

            while (_spawned.Count < ev.Choices.Count)
            {
                Button made = Instantiate(_choice, _choices);

                made.gameObject.SetActive(true);
                _spawned.Add(made);
            }

            for (var i = 0; i < _spawned.Count; i++)
            {
                bool used = i < ev.Choices.Count;

                _spawned[i].gameObject.SetActive(used);

                if (!used) continue;

                int cost = ev.Choices[i].Cost;
                bool afford = cost <= 0 || gold >= cost;

                Dress(_spawned[i], said.Choices[i], afford);

                int taking = i;

                _spawned[i].interactable = afford;
                _spawned[i].onClick.RemoveAllListeners();
                _spawned[i].onClick.AddListener(() => Decide(new Answer { Choice = taking }));
            }
        }

        /// <summary>One choice: what it says, and what it costs if that is more than nothing.</summary>
        private void Dress(Button choice, EventChoiceText said, bool afford)
        {
            var texts = choice.GetComponentsInChildren<TMP_Text>(true);

            if (texts.Length > 0)
            {
                texts[0].text = said.Label;
                texts[0].color = Ink;
            }

            if (texts.Length > 1)
            {
                string hint = said.Hint;

                if (!afford)
                {
                    hint = hint.Length > 0 ? hint + Between + Say(ShortKey) : Say(ShortKey);
                }

                texts[1].gameObject.SetActive(hint.Length > 0);
                texts[1].text = hint;
                texts[1].color = Faint;
            }

            var ground = choice.targetGraphic as Graphic;

            if (ground != null)
            {
                Color was = ground.color;

                ground.color = new Color(was.r, was.g, was.b, afford ? 1f : Beyond);
            }
        }

        /// <summary>
        /// Fetches the picture for an event.
        /// </summary>
        /// <remarks>
        /// Skipped when it is already the one on screen, because an event's outcome redraws the
        /// same screen and refetching between the two beats would flicker the illustration at
        /// exactly the moment a delver is reading what happened.
        /// </remarks>
        private void Art(int index)
        {
            if (_art == null || _content == null || _content.Events == null) return;

            if (index == _showing) return;

            _showing = index;

            Fetch(_content.Events.For(index)).Forget();
        }

        private async UniTaskVoid Fetch(AssetReferenceSprite address)
        {
            if (address == null)
            {
                _art.enabled = false;
                return;
            }

            // One at a time, and only what this stage loaded itself. Twelve event pictures over
            // a run is eleven more than are ever on screen.
            if (!ReferenceEquals(address, _held)) Drop();

            bool ours = !address.IsValid();

            AsyncOperationHandle<Sprite> fetching = ours
                ? address.LoadAssetAsync<Sprite>()
                : address.OperationHandle.Convert<Sprite>();

            Sprite drawn = await fetching.Task;

            if (ours) _held = address;

            // The run may have moved on while this was in flight.
            if (this == null || _art == null) return;

            _art.sprite = drawn;
            _art.enabled = drawn != null;
        }

        /// <summary>Lets go of whatever picture this stage fetched.</summary>
        private void Drop()
        {
            if (_held == null) return;

            if (_held.IsValid()) _held.ReleaseAsset();

            _held = null;
        }

        private void OnDestroy()
        {
            Drop();
        }

        private static void Write(TMP_Text text, string what, Color ink)
        {
            if (text == null) return;

            text.text = what;
            text.color = ink;
        }
    }
}
