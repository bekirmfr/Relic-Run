using System.Collections.Generic;
using System.Text;
using RelicRun.Core.Content;
using UnityEngine;

namespace RelicRun.Game.Data
{
    /// <summary>
    /// Everything the game needs that Unity has to hold for it.
    /// </summary>
    /// <remarks>
    /// One asset, injected once, so that nothing below it reaches for
    /// <c>Resources.Load</c> and nothing has to be found by name at the moment it is drawn.
    ///
    /// It is deliberately a bag of references and not a place to put anything. The moment a
    /// number lives here it has left the tested half of the project: Core is compiled by
    /// <c>dotnet test</c> in ten seconds and a <c>.asset</c> file is not compiled at all. The
    /// rule for what belongs here is simple and worth keeping — if Unity is the only thing that
    /// can hold it, it goes here; if C# can hold it, it stays in Core.
    /// </remarks>
    [CreateAssetMenu(menuName = "Relic Run/Game Content", fileName = "GameContent")]
    public sealed class GameContent : ScriptableObject
    {
        [Header("Art")]
        [SerializeField] private RelicIconBook _relicIcons;
        [SerializeField] private HallBook _halls;
        [SerializeField] private EventBook _events;
        [SerializeField] private EnemyBook _enemies;

        [Header("Text")]
        [SerializeField] private HeroPackAsset _heroPack;
        [SerializeField] private LocaleBook _locales;

        [Header("Authored")]
        [SerializeField] private PresentationSettings _presentation;

        public RelicIconBook RelicIcons { get { return _relicIcons; } }
        public HallBook Halls { get { return _halls; } }
        public EventBook Events { get { return _events; } }
        public EnemyBook Enemies { get { return _enemies; } }
        public HeroPackAsset HeroPack { get { return _heroPack; } }
        public LocaleBook Locales { get { return _locales; } }
        public PresentationSettings Presentation { get { return _presentation; } }

        /// <summary>
        /// Points this at everything. The importer's one way in.
        /// </summary>
        /// <remarks>
        /// Seven parameters, named at the call site, rather than seven strings through a
        /// <c>SerializedObject</c>. A renamed field should break the importer where a compiler
        /// can say so, not on the next person to run it.
        /// </remarks>
        public void Bind(RelicIconBook relicIcons, HallBook halls, EventBook events,
            EnemyBook enemies, HeroPackAsset heroPack, LocaleBook locales,
            PresentationSettings presentation)
        {
            _relicIcons = relicIcons;
            _halls = halls;
            _events = events;
            _enemies = enemies;
            _heroPack = heroPack;
            _locales = locales;
            _presentation = presentation;
        }

        /// <summary>The books that bind ids to sprites, in the order worth reading them.</summary>
        public IEnumerable<SpriteBook> Books
        {
            get
            {
                if (_relicIcons != null) yield return _relicIcons;
                if (_halls != null) yield return _halls;
                if (_events != null) yield return _events;
                if (_enemies != null) yield return _enemies;
            }
        }

        /// <summary>Which of the seven references nobody filled in.</summary>
        public IReadOnlyList<string> Unbound()
        {
            var missing = new List<string>();

            if (_relicIcons == null) missing.Add("relic icons");
            if (_halls == null) missing.Add("halls");
            if (_events == null) missing.Add("events");
            if (_enemies == null) missing.Add("enemies");
            if (_heroPack == null || !_heroPack.IsBound) missing.Add("hero pack");
            if (_locales == null) missing.Add("locales");
            if (_presentation == null) missing.Add("presentation");

            return missing;
        }

        /// <summary>
        /// Everything wrong with the content, written out. Empty when there is nothing.
        /// </summary>
        /// <remarks>
        /// Reported all at once rather than one failure at a time. A person who has just run the
        /// importer wants the whole list, because they are about to go and fix all of it; giving
        /// them the first problem and then the next one after another minute of reimporting is
        /// how a check stops being run.
        /// </remarks>
        public string Audit()
        {
            var said = new StringBuilder();

            IReadOnlyList<string> unbound = Unbound();
            if (unbound.Count > 0)
            {
                said.Append(unbound.Count).Append(" of 7 references are empty: ")
                    .Append(string.Join(", ", unbound));
            }

            foreach (SpriteBook book in Books)
            {
                BindingAudit audit = book.Audit();
                if (audit.Passed) continue;

                if (said.Length > 0) said.Append('\n');
                said.Append(audit.Report());
            }

            if (_locales != null)
            {
                BindingAudit locales = _locales.Audit();
                if (!locales.Passed)
                {
                    if (said.Length > 0) said.Append('\n');
                    said.Append(locales.Report());
                }
            }

            return said.ToString();
        }
    }
}
