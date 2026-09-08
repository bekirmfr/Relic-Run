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
    /// The fallbacks are the uncomfortable part and are deliberately a separate list. Neither
    /// shipped face can draw Japanese, Chinese or Arabic — not partially, not most of it: a
    /// tenth of Chinese, an eighth of Japanese, half of Arabic. Three of the eight languages
    /// have nothing to render with, which is a decision about what the game looks like rather
    /// than a bug to fix quietly, so the slot exists, sits empty, and is measured by
    /// <see cref="Legibility"/> rather than discovered on somebody's phone.
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
        [Tooltip("Consulted for anything the faces above cannot draw. Empty, and measured.")]
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
        public void Rebind(IList<Face> faces)
        {
            var kept = new Face[faces == null ? 0 : faces.Count];
            for (int i = 0; i < kept.Length; i++) kept[i] = faces[i];

            _faces = kept;
        }
    }
}
