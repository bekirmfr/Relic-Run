using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using UnityEngine;

namespace RelicRun.Game.Data
{
    /// <summary>
    /// A binding of content ids to sprites.
    /// </summary>
    /// <remarks>
    /// This is the whole of what a ScriptableObject is for in this project. The rules, the
    /// tables and the numbers are generated into C# and tested in a second outside Unity; what
    /// cannot be is which <c>.png</c> a relic draws with, because a sprite is a Unity asset and
    /// only Unity can hold a reference to one. So the assets live here and nothing else does.
    ///
    /// The list is filled by the importer rather than by dragging, and then checked: every id
    /// the catalogs will ask for, bound exactly once, to something. <see cref="BindingAudit"/>
    /// states that rule and is tested without the Editor; this class only does the two things
    /// that need the Editor — hold the references, and say whether each one resolved.
    /// </remarks>
    public abstract class SpriteBook : ScriptableObject
    {
        /// <summary>One row: an id from the catalogs, and the sprite drawn for it.</summary>
        [Serializable]
        public struct Entry
        {
            public string Id;
            public Sprite Sprite;

            public Entry(string id, Sprite sprite)
            {
                Id = id;
                Sprite = sprite;
            }
        }

        [SerializeField]
        [Tooltip("Filled by Tools ▸ Relic Run ▸ Import Art. Editing by hand is allowed but audited.")]
        private Entry[] _entries = new Entry[0];

        [NonSerialized] private Dictionary<string, Sprite> _byId;

        /// <summary>What this binds, for the audit's message: "relics", "halls".</summary>
        public abstract string What { get; }

        /// <summary>Every id the game will ask this book for.</summary>
        public abstract IReadOnlyList<string> Needed { get; }

        /// <summary>Ids known to have no art on purpose. Empty for most books.</summary>
        public virtual IReadOnlyList<string> Excused { get { return NothingExcused; } }

        private static readonly string[] NothingExcused = new string[0];

        public IReadOnlyList<Entry> Entries { get { return _entries; } }

        /// <summary>
        /// The sprite for an id, or null when there is none.
        /// </summary>
        /// <remarks>
        /// Null rather than a placeholder. A caller that wants a question mark where the art
        /// should be can draw one; a book that handed out a placeholder would make the audit the
        /// only way to ever find out, and audits are read once.
        /// </remarks>
        public Sprite Get(string id)
        {
            if (_byId == null) Index();

            Sprite sprite;
            return id != null && _byId.TryGetValue(id, out sprite) ? sprite : null;
        }

        public bool Has(string id) { return Get(id) != null; }

        /// <summary>Whether this book is complete and exact. See <see cref="BindingAudit"/>.</summary>
        public BindingAudit Audit()
        {
            var bound = new List<Binding>(_entries.Length);
            for (int i = 0; i < _entries.Length; i++)
            {
                // A destroyed or unassigned asset compares equal to null through Unity's own
                // operator, which is why this is written out rather than passed as a reference.
                bound.Add(new Binding(_entries[i].Id, _entries[i].Sprite != null));
            }

            return BindingAudit.Of(What, Needed, bound, Excused);
        }

        /// <summary>Replaces everything this book holds. The importer's one way in.</summary>
        public void Rebind(IList<Entry> entries)
        {
            var kept = new Entry[entries == null ? 0 : entries.Count];
            for (int i = 0; i < kept.Length; i++) kept[i] = entries[i];

            _entries = kept;
            _byId = null;
        }

        private void Index()
        {
            _byId = new Dictionary<string, Sprite>(_entries.Length);
            for (int i = 0; i < _entries.Length; i++)
            {
                string id = _entries[i].Id;
                if (string.IsNullOrEmpty(id)) continue;

                // First wins, so a duplicate row cannot change what is drawn by being reordered.
                // That it exists at all is the audit's business, not this lookup's.
                if (!_byId.ContainsKey(id)) _byId[id] = _entries[i].Sprite;
            }
        }

        private void OnEnable() { _byId = null; }

        private void OnValidate() { _byId = null; }
    }
}
