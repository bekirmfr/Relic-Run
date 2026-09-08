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
    /// hundred and thirty-eight of the two hundred and forty-seven styled spans in the source
    /// are the same pixel face; the rest are prose. Naming them <c>ui</c> and <c>display</c>
    /// means changing which typeface fills a role is a line in the importer rather than a
    /// rename across every screen.
    ///
    /// The fallbacks are a separate list because they are a different kind of thing. Neither
    /// shipped face can draw Japanese, Chinese or Arabic — not partially: a tenth of Chinese, an
    /// eighth of Japanese, half of Arabic — so those three borrow the reader's own system font.
    /// A delver reading Japanese gets their platform's Japanese face beside a 1983 pixel face,
    /// which is a compromise somebody chose rather than an accident: the alternative was ten to
    /// sixteen megabytes of bundled CJK for three languages.
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
