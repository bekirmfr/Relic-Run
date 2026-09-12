using System.Collections.Generic;
using RelicRun.Core.Content;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The delver, drawn.
    /// </summary>
    /// <remarks>
    /// Every other picture in this game is a sprite off a sheet. This one is composed: eleven
    /// slots of pixel rows stacked back to front into an INDEX grid — one byte a pixel, saying
    /// which palette entry — and coloured on the GPU by a palette texture the shader looks the
    /// index up in. That is the source's own arrangement and it is what makes a wardrobe possible
    /// at all: recolouring a delver is a 256-pixel texture rewrite rather than a recomposite.
    ///
    /// A state is composed the first time it is asked for and kept. Composing is cheap — a
    /// thirty-two square, a handful of frames — but it is not free, and a delver hit forty times
    /// a floor would otherwise pay for it forty times.
    ///
    /// Nothing here decides anything about the fight. It is told which state to be in, exactly
    /// like <see cref="CombatView"/> is told which event to draw.
    /// </remarks>
    public sealed class HeroView : MonoBehaviour
    {
        [Tooltip("Where the delver is drawn. Its material is made here, not authored.")]
        [SerializeField] private RawImage _art;

        /// <summary>Standing about, which is what a delver does between blows.</summary>
        public const string Idle = "idle";

        /// <summary>Swinging.</summary>
        public const string Attack = "attack";

        /// <summary>Taking one.</summary>
        public const string Hurt = "hurt";

        /// <summary>And the one that does not go back to idle.</summary>
        public const string Die = "die";

        /// <summary>What has been composed so far, by state.</summary>
        private readonly Dictionary<string, Drawn> _known = new Dictionary<string, Drawn>();

        private HeroPack _pack;
        private IReadOnlyDictionary<string, string> _worn;
        private IReadOnlyDictionary<char, Rgb> _palette;

        private Drawn _playing;
        private string _state;
        private int _frame;
        private float _since;

        /// <summary>Whether there is a delver to draw at all.</summary>
        public bool Dressed
        {
            get { return _pack != null; }
        }

        /// <summary>
        /// Dresses the delver and puts them on their feet.
        /// </summary>
        /// <remarks>
        /// The outfit is passed in rather than read off the pack, because it is the delver's and
        /// the pack is only what they could wear. There is no wardrobe in the save yet, so today
        /// every caller hands over <c>HeroPackReader.Plain</c> — and when there is one, this line
        /// does not change.
        /// </remarks>
        public void Wear(HeroPack pack, IReadOnlyDictionary<string, string> worn,
            IReadOnlyDictionary<char, Rgb> palette)
        {
            Forget();

            _pack = pack;
            _worn = worn;
            _palette = palette ?? HeroPalette.Build();

            if (_art != null) _art.enabled = pack != null;

            Act(Idle);
        }

        /// <summary>
        /// Puts the delver into a state, from the top.
        /// </summary>
        /// <remarks>
        /// Asking for the state already playing RESTARTS it, which is what a delver struck twice
        /// in a row should look like. The alternative — ignoring the second blow because the
        /// first is still animating — reads as the second one having missed.
        /// </remarks>
        public void Act(string state)
        {
            if (_pack == null || string.IsNullOrEmpty(state)) return;

            Drawn drawn = Composed(state);

            if (drawn == null) return;

            _playing = drawn;
            _state = state;
            _frame = 0;
            _since = 0f;

            Draw();
        }

        /// <summary>
        /// The next frame, when it is due.
        /// </summary>
        /// <remarks>
        /// Unscaled time. The fight's pace is the content's, not the engine's, and a delver whose
        /// animation slowed down because something set <c>Time.timeScale</c> would be out of step
        /// with a log that was not.
        /// </remarks>
        private void Update()
        {
            if (_playing == null || _playing.Hero.Frames.Count < 2) return;

            _since += Time.unscaledDeltaTime * 1000f;

            int ms = _playing.Hero.Ms > 0 ? _playing.Hero.Ms : 100;

            if (_since < ms) return;

            _since -= ms;

            int next = _frame + 1;

            if (next < _playing.Hero.Frames.Count)
            {
                _frame = next;
                Draw();
                return;
            }

            // The end of a run-once state. Dying stays dead — a delver folding back into their
            // idle stance after the run has ended is the kind of thing nobody reports and
            // everybody notices.
            if (_playing.Hero.Mode == "loop")
            {
                _frame = 0;
                Draw();
                return;
            }

            // And dying ends with nobody there. The animation is watched to its last frame and
            // THEN the delver is gone, which is the difference between having died and having
            // been deleted — a body left folded on the floor while the end screen comes up reads
            // as the run having stopped rather than as the delver having lost.
            if (_state == Die)
            {
                Gone();
                return;
            }

            if (_state != Idle) Act(Idle);
        }

        /// <summary>
        /// Takes the delver off the screen, the dying being over.
        /// </summary>
        /// <remarks>
        /// The texture is dropped rather than the object disabled, so whatever is drawn here next
        /// starts from nothing — a <c>RawImage</c> keeps its last texture, and a delver who began
        /// their next run wearing the final frame of their last death would be a hard thing to
        /// explain.
        /// </remarks>
        private void Gone()
        {
            _playing = null;
            _state = null;
            _frame = 0;

            if (_art == null) return;

            _art.texture = null;
            _art.enabled = false;
        }

        /// <summary>Which slice of the grid is on screen.</summary>
        private void Draw()
        {
            if (_art == null || _playing == null) return;

            _art.enabled = true;
            _art.texture = _playing.Grid;
            _art.material = _playing.Paint;
            _art.uvRect = HeroTextures.FrameUv(_playing.Hero, _frame);
        }

        /// <summary>A state, composed once and kept.</summary>
        private Drawn Composed(string state)
        {
            Drawn found;

            if (_known.TryGetValue(state, out found)) return found;

            ComposedHero made = HeroCompositor.Compose(_pack, _worn, state);

            if (made == null || made.Frames.Count == 0) return null;

            HeroIndex hero = HeroIndex.Of(made);

            var drawn = new Drawn
            {
                Hero = hero,
                Grid = HeroTextures.Grid(hero),
                Palette = HeroTextures.Palette(hero, _palette),
            };

            drawn.Paint = HeroMaterial.For(drawn.Palette);

            _known[state] = drawn;

            return drawn;
        }

        /// <summary>
        /// Throws away every texture and material this made.
        /// </summary>
        /// <remarks>
        /// <c>HideAndDontSave</c> is what keeps a generated texture out of the scene and out of a
        /// build; it is also what stops Unity from ever collecting it. So everything made here is
        /// destroyed by hand, and a delver redressed twice does not leave the first one behind.
        /// </remarks>
        private void Forget()
        {
            foreach (KeyValuePair<string, Drawn> one in _known) one.Value.Drop();

            _known.Clear();

            _playing = null;
            _state = null;

            if (_art != null)
            {
                _art.texture = null;
                _art.material = null;
            }
        }

        private void OnDestroy()
        {
            Forget();
        }

        /// <summary>One state, ready to draw.</summary>
        private sealed class Drawn
        {
            public HeroIndex Hero;
            public Texture2D Grid;
            public Texture2D Palette;
            public Material Paint;

            public void Drop()
            {
                if (Paint != null) Object.Destroy(Paint);
                if (Palette != null) Object.Destroy(Palette);
                if (Grid != null) Object.Destroy(Grid);
            }
        }
    }
}
