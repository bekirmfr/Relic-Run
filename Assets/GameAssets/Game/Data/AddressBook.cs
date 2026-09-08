using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace RelicRun.Game.Data
{
    /// <summary>
    /// A binding of content ids to assets that are fetched when they are wanted.
    /// </summary>
    /// <remarks>
    /// The difference between this and <see cref="SpriteBook"/> is the whole reason both exist.
    /// A direct reference is loaded when the thing holding it is loaded, so a book of ten hall
    /// backdrops puts four and a half megabytes of texture in memory the moment anything touches
    /// it — and a run visits ONE hall. An <see cref="AssetReference"/> is a GUID and nothing
    /// else, so the same book costs a few hundred bytes and the hall arrives when the delver
    /// does.
    ///
    /// Which side a book belongs on is a question about the art, not a preference. The relic
    /// sheet and the bestiary are single textures that every screen draws from, so they are
    /// direct references and always resident. The halls and the illustrations are large, many,
    /// and shown one at a time, so they are addressed.
    /// </remarks>
    public abstract class AddressBook : ScriptableObject
    {
        /// <summary>One row: an id from the catalogs, and where to find its art.</summary>
        [Serializable]
        public struct Entry
        {
            public string Id;
            public AssetReferenceSprite Art;

            public Entry(string id, AssetReferenceSprite art)
            {
                Id = id;
                Art = art;
            }
        }

        [SerializeField]
        [Tooltip("Filled by Tools ▸ Relic Run ▸ Import Content. Addresses, not pictures.")]
        private Entry[] _entries = new Entry[0];

        [NonSerialized] private Dictionary<string, AssetReferenceSprite> _byId;

        /// <summary>What this binds, for the audit's message: "halls", "events".</summary>
        public abstract string What { get; }

        /// <summary>Every id the game will ask this book for.</summary>
        public abstract IReadOnlyList<string> Needed { get; }

        public IReadOnlyList<Entry> Entries { get { return _entries; } }

        /// <summary>
        /// Where to find one id's art, or null when nothing is bound.
        /// </summary>
        /// <remarks>
        /// A reference, not a sprite. Loading it is the caller's business and is asynchronous,
        /// because that is the only honest way to say "this is on disk and may take a moment".
        /// </remarks>
        public AssetReferenceSprite Get(string id)
        {
            if (_byId == null) Index();

            AssetReferenceSprite art;
            return id != null && _byId.TryGetValue(id, out art) ? art : null;
        }

        /// <summary>Whether this book is complete and exact. See <see cref="BindingAudit"/>.</summary>
        public BindingAudit Audit()
        {
            var bound = new List<Binding>(_entries.Length);
            for (int i = 0; i < _entries.Length; i++)
            {
                AssetReferenceSprite art = _entries[i].Art;

                // A reference with no GUID behind it serializes exactly as one that has been
                // filled in, so "is there a row" and "does the row point anywhere" are two
                // different questions and only the second one matters.
                bound.Add(new Binding(_entries[i].Id, art != null && art.RuntimeKeyIsValid()));
            }

            return BindingAudit.Of(What, Needed, bound);
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
            _byId = new Dictionary<string, AssetReferenceSprite>(_entries.Length);
            for (int i = 0; i < _entries.Length; i++)
            {
                string id = _entries[i].Id;
                if (string.IsNullOrEmpty(id)) continue;

                // First wins, so a duplicate row cannot change what is drawn by being reordered.
                if (!_byId.ContainsKey(id)) _byId[id] = _entries[i].Art;
            }
        }

        private void OnEnable() { _byId = null; }

        private void OnValidate() { _byId = null; }
    }
}
