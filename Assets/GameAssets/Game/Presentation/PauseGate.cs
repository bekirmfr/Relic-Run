using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The one thing in a fight a delver can do: stop it.
    /// </summary>
    /// <remarks>
    /// A delve is minutes long and a fight reads itself out at its own pace, so the only real
    /// interaction during one is the decision to look away. The gate this sits on has existed
    /// since the playback loop was written — <c>IPlaybackScreen.Paused</c>, which the loop waits
    /// on rather than polls — and this is finally the thing that sets it.
    ///
    /// NOT a <see cref="RunStage"/>, because pausing is not a question the run asks. The run does
    /// not know it happened; the reading of a floor simply stops between two events and carries
    /// on from the same one. Which is why it is safe: nothing is decided while it is up.
    ///
    /// Shown only during a fight. Every other beat of a run is already waiting for a press, so a
    /// pause button on a draft would be a button that stops nothing.
    /// </remarks>
    public sealed class PauseGate : MonoBehaviour
    {
        [Tooltip("The button in the corner. Thumb-reach, bottom right, like the source's.")]
        [SerializeField] private Button _toggle;

        [SerializeField] private TMP_Text _glyph;

        [Tooltip("Opens the menu. Only while paused — there is nothing to leave mid-blow.")]
        [SerializeField] private Button _open;

        [Header("The menu")]
        [SerializeField] private GameObject _menu;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private Button _resume;
        [SerializeField] private TMP_Text _resumeLabel;
        [SerializeField] private Button _exit;
        [SerializeField] private TMP_Text _exitLabel;

        /// <summary>What the button shows: two bars stopped, a triangle going.</summary>
        public const string Stopped = "▶";

        public const string Going = "▐▌";

        /// <summary>The source's own words, which it ships in English with no key.</summary>
        public const string Title = "PAUSED";

        public const string Resume = "RESUME";

        public const string Exit = "EXIT GAME";

        private static readonly Color Coin = new Color(0.890f, 0.702f, 0.255f);

        private static readonly Color Faint = new Color(0.545f, 0.506f, 0.447f);

        private static readonly Color Coal = new Color(0.078f, 0.071f, 0.059f);

        private static readonly Color Blood = new Color(0.769f, 0.349f, 0.235f);

        /// <summary>Whether the fight is stopped. Read by the playback loop, once a turn.</summary>
        public bool Paused { get; private set; }

        /// <summary>Raised when the delver has chosen to abandon the run.</summary>
        public event Action Left;

        /// <summary>
        /// Whether the button is on the screen at all.
        /// </summary>
        /// <remarks>
        /// Turning it off also UNPAUSES, which matters: a fight that ended while paused would
        /// otherwise leave the flag set and the next fight would open stopped, with nothing on
        /// screen to start it again.
        /// </remarks>
        public bool Offered
        {
            get { return gameObject.activeSelf; }

            set
            {
                if (!value) Stop();

                gameObject.SetActive(value);
            }
        }

        private void Awake()
        {
            if (_toggle != null) _toggle.onClick.AddListener(Toggle);

            if (_open != null) _open.onClick.AddListener(() => Menu(true));

            if (_resume != null)
            {
                _resume.onClick.AddListener(() =>
                {
                    Menu(false);
                    Stop();
                });
            }

            if (_exit != null) _exit.onClick.AddListener(Abandon);

            Dress();
            Menu(false);
        }

        /// <summary>Stops the fight, or starts it again.</summary>
        /// <remarks>
        /// Pausing does NOT open the menu — it offers it. Most pauses are somebody looking away
        /// for a moment, and a menu in front of the fight they stopped to look at would be the
        /// opposite of what they asked for.
        /// </remarks>
        private void Toggle()
        {
            Paused = !Paused;

            Menu(false);
            Dress();
        }

        /// <summary>Lets the fight go on, whatever it was doing.</summary>
        private void Stop()
        {
            Paused = false;

            Menu(false);
            Dress();
        }

        private void Menu(bool up)
        {
            if (_menu != null) _menu.SetActive(up);

            // The way IN to the menu is only offered while stopped, and never over the menu
            // itself.
            if (_open != null) _open.gameObject.SetActive(Paused && !up);
        }

        /// <summary>What the button and the menu say for the state they are in.</summary>
        private void Dress()
        {
            if (_glyph != null)
            {
                _glyph.text = Paused ? Stopped : Going;
                _glyph.color = Paused ? Coin : Faint;
            }

            if (_toggle != null)
            {
                var ring = _toggle.GetComponent<Outline>();

                if (ring != null) ring.effectColor = Paused ? Coin : new Color(0.227f, 0.200f, 0.157f);
            }

            Write(_title, Title, Coin);
            Write(_resumeLabel, Resume, Coal);
            Write(_exitLabel, Exit, Blood);
        }

        /// <summary>
        /// Walking out of the run entirely.
        /// </summary>
        /// <remarks>
        /// Everything is lost — no score, no gold banked. That is the source's own behaviour and
        /// it is the honest one: a delver who abandons a run halfway has not finished it, and
        /// paying out for an unfinished run would make quitting a strategy.
        ///
        /// Unpaused on the way out, so whatever is torn down is not torn down mid-gate.
        /// </remarks>
        private void Abandon()
        {
            Stop();

            Action left = Left;

            if (left != null) left();
        }

        private static void Write(TMP_Text text, string what, Color ink)
        {
            if (text == null) return;

            text.text = what;
            text.color = ink;
        }
    }
}
