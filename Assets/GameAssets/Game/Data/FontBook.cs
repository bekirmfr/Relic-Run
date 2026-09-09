using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using TMPro;
using UnityEngine;

namespace RelicRun.Game.Data
{
    /// <summary>
    /// The faces the game is set in.
    /// </summary>
    /// <remarks>
    /// Bound by ROLE rather than by typeface, because the role is what the game means. Two
    /// hundred and forty of the two hundred and forty-nine styled spans in the source are the
    /// same pixel face — Silkscreen — and the rest are prose, in Space Grotesk. Naming them
    /// <c>ui</c> and <c>display</c> means changing which typeface fills a role is a line in the
    /// importer rather than a rename across every screen.
    ///
    /// That indirection earned itself. The first pass filled both roles with faces the source
    /// does not use anywhere, and putting the right ones in was those two lines and nothing
    /// else — no screen, no widget and no test had to learn a new typeface name.
    ///
    /// The fallbacks are a separate list because they are a different kind of thing. FIVE of the
    /// eight languages need them. Neither face can draw Japanese, Chinese or Arabic — not
    /// partially: a tenth of Chinese, an eighth of Japanese, half of Arabic. Neither has any
    /// Cyrillic, so Russian too. And Silkscreen reaches 94% of Turkish, which is worse than it
    /// sounds: it has no dotless i and no breve, and Turkish uses both constantly.
    ///
    /// So a delver reading Japanese gets their platform's Japanese face beside a pixel face from
    /// 2001, and a Turkish one gets two letters in a different face from the rest of the word.
    /// Both are compromises somebody chose rather than accidents: the alternative was ten to
    /// sixteen megabytes of bundled CJK, and the source makes the same trade by falling through
    /// to <c>monospace</c>.
    ///
    /// A fallback resolves by FAMILY NAME on the device, so what it finds depends on what is
    /// installed there. That is the whole point of it and also its limit, and it is why the
    /// list is serialized: the importer fills in what the machine it runs on can see, and a
    /// platform with different families can have them added here by hand.
    /// </remarks>
    [CreateAssetMenu(menuName = "Relic Run/Fonts", fileName = "Fonts")]
    public sealed class FontBook : ScriptableObject
    {
        /// <summary>Nearly everything: buttons, labels, numbers. A pixel face.</summary>
        public const string Ui = "ui";

        /// <summary>Titles and the few places the game speaks rather than labels.</summary>
        public const string Display = "display";

        /// <summary>The roles a book must fill.</summary>
        public static readonly IReadOnlyList<string> Roles = new[] { Ui, Display };

        /// <summary>
        /// Whether a role is drawn as pixel art, which decides how its face is baked.
        /// </summary>
        /// <remarks>
        /// The two roles want opposite treatment and it is not a close call. Silkscreen is drawn
        /// on an eight-pixel grid: baked as a bitmap at exactly that, point filtered, no padding,
        /// so a whole pixel stays a whole pixel and <see cref="Core.Presentation.PixelScale"/>
        /// keeps the canvas at a whole multiple of it. Space Grotesk is an outline face for
        /// prose: baked as a distance field, padded so the field has somewhere to live, and
        /// filtered — which is the only way it stays clean at sizes nobody chose in advance.
        ///
        /// Here rather than in the importer, because it is a fact about what the role IS. It was
        /// briefly in both, which is one place too many: the importer knew, the test that gates
        /// the bake did not, and the test went on asserting that both faces were bitmaps for as
        /// long as it took somebody to run it.
        /// </remarks>
        public static bool IsPixelArt(string role)
        {
            return role == Ui;
        }

        [Serializable]
        public struct Face
        {
            [Tooltip("ui or display.")]
            public string Role;

            public TMP_FontAsset Font;

            public Face(string role, TMP_FontAsset font)
            {
                Role = role;
                Font = font;
            }
        }

        [SerializeField]
        [Tooltip("Filled by Tools ▸ Relic Run ▸ Import Content.")]
        private Face[] _faces = new Face[0];

        [SerializeField]
        [Tooltip("System faces, resolved by family name on the device, for what the faces above " +
                 "cannot draw — Japanese, Chinese and Arabic. Add a platform's own families here.")]
        private TMP_FontAsset[] _fallbacks = new TMP_FontAsset[0];

        public IReadOnlyList<Face> Faces { get { return _faces; } }

        public IReadOnlyList<TMP_FontAsset> Fallbacks { get { return _fallbacks; } }

        /// <summary>The face for a role, or null when nothing fills it.</summary>
        public TMP_FontAsset For(string role)
        {
            for (int i = 0; i < _faces.Length; i++)
            {
                if (_faces[i].Role == role) return _faces[i].Font;
            }

            return null;
        }

        /// <summary>Every role filled exactly once, by something. See <see cref="BindingAudit"/>.</summary>
        public BindingAudit Audit()
        {
            var bound = new List<Binding>(_faces.Length);
            for (int i = 0; i < _faces.Length; i++)
            {
                bound.Add(new Binding(_faces[i].Role, _faces[i].Font != null));
            }

            return BindingAudit.Of("fonts", Roles, bound);
        }

        /// <summary>Replaces everything this book holds. The importer's one way in.</summary>
        public void Rebind(IList<Face> faces, IList<TMP_FontAsset> fallbacks)
        {
            var kept = new Face[faces == null ? 0 : faces.Count];
            for (int i = 0; i < kept.Length; i++) kept[i] = faces[i];

            var borrowed = new TMP_FontAsset[fallbacks == null ? 0 : fallbacks.Count];
            for (int i = 0; i < borrowed.Length; i++) borrowed[i] = fallbacks[i];

            _faces = kept;
            _fallbacks = borrowed;
        }
    }
}
