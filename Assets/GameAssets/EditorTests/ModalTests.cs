using System;
using System.Collections.Generic;
using GameLift.Popup;
using NUnit.Framework;
using RelicRun.Editor.Importers;
using RelicRun.Game.Presentation;
using UnityEditor;
using UnityEngine;

namespace RelicRun.Tests.Editor
{
    /// <summary>
    /// The modals, and the three separate things each of them has to be.
    /// </summary>
    /// <remarks>
    /// A popup opens only when all three are true: it is a prefab, it names itself with a
    /// PopupId, and it is listed in PopupSettings. <c>Create&lt;T&gt;</c> walks that list looking
    /// for an EXACT type match and returns null otherwise — no warning, no exception, nothing on
    /// the screen. A button that opens nothing and says nothing is indistinguishable from one
    /// that is broken, and this is the only place that difference can be caught.
    ///
    /// Asked over every popup type rather than one at a time, so the fifth modal is covered by
    /// the act of building it.
    /// </remarks>
    [TestFixture]
    public class ModalTests
    {
        /// <summary>Every modal the menu builds, and where its prefab lives.</summary>
        private static readonly Dictionary<Type, string> Built = new Dictionary<Type, string>
        {
            { typeof(SettingsPopup), MetaSceneBuilder.SettingsPrefab },
            { typeof(WelcomePopup), MetaSceneBuilder.WelcomePrefab },
            { typeof(RelicPopup), MetaSceneBuilder.RelicPrefab },
            { typeof(SetPopup), MetaSceneBuilder.SetPrefab },
        };

        private static IEnumerable<Type> Kinds
        {
            get { return Built.Keys; }
        }

        /// <summary>The prefab exists and carries the component it is named for.</summary>
        [Test]
        public void EveryModalIsAPrefab([ValueSource("Kinds")] Type kind)
        {
            PopupBase popup = Load(kind);

            Assert.That(popup, Is.Not.Null,
                Built[kind] + " has no " + kind.Name + " on it — rebuild with " +
                "Tools ▸ Relic Run ▸ Build Menu Scene");
        }

        /// <summary>
        /// It names itself, and no two name themselves the same.
        /// </summary>
        /// <remarks>
        /// The id is what the service files it under. Two popups sharing one would be two modals
        /// the game cannot tell apart, and the one that opened would be whichever was listed
        /// first — which is a coin toss decided by build order.
        /// </remarks>
        [Test]
        public void EveryModalNamesItselfAndNoTwoAgree()
        {
            var seen = new List<string>();

            foreach (Type kind in Kinds)
            {
                PopupBase popup = Load(kind);

                Assert.That(popup, Is.Not.Null);
                Assert.That(popup.PopupId, Is.Not.Null.And.Not.Empty,
                    kind.Name + " has no PopupId");

                Assert.That(seen.Contains(popup.PopupId), Is.False,
                    popup.PopupId + " names two different modals");

                seen.Add(popup.PopupId);
            }
        }

        /// <summary>
        /// Everything the modal can be handed is handed to it.
        /// </summary>
        /// <remarks>
        /// Walked generically rather than named, so a field added tomorrow and forgotten fails
        /// this without anybody remembering to come back. An unwired reference on a popup is
        /// silent twice over — the modal opens, and one of its lines is simply absent.
        /// </remarks>
        [Test]
        public void EveryReferenceIsWired([ValueSource("Kinds")] Type kind)
        {
            PopupBase popup = Load(kind);

            Assert.That(popup, Is.Not.Null);

            var missing = new List<string>();

            SerializedProperty property = new SerializedObject(popup).GetIterator();

            while (property.NextVisible(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (property.name == "m_Script") continue;

                if (property.objectReferenceValue == null) missing.Add(property.name);
            }

            Assert.That(missing, Is.Empty,
                kind.Name + " has " + missing.Count + " empty references: " +
                string.Join(", ", missing) + " — rebuild with Tools ▸ Relic Run ▸ Build Menu Scene");
        }

        /// <summary>
        /// Each is listed exactly once where the service looks.
        /// </summary>
        /// <remarks>
        /// Once, not at least once. A rebuild writes a new prefab, and a stale entry pointing at
        /// the old one would still be in the list — first match wins, so the game would go on
        /// opening the modal somebody built yesterday, with yesterday's references.
        /// </remarks>
        [Test]
        public void EveryModalIsListedExactlyOnce([ValueSource("Kinds")] Type kind)
        {
            var settings = AssetDatabase.LoadAssetAtPath<PopupSettings>(MetaSceneBuilder.PopupsPath);

            Assert.That(settings, Is.Not.Null, "no popup settings at " + MetaSceneBuilder.PopupsPath);
            Assert.That(settings.popupBases, Is.Not.Null, "the popup list is null");

            var listed = 0;

            foreach (PopupBase one in settings.popupBases)
            {
                if (one == null) continue;

                // Exactly this type. Create<T> matches the same way, so a subclass listed in a
                // base's place would never be found by either.
                if (one.GetType() == kind) listed++;
            }

            Assert.That(listed, Is.EqualTo(1),
                kind.Name + " is listed " + listed + " times where the service looks");
        }

        /// <summary>Nothing null sits in the list, which would be a modal that vanished.</summary>
        [Test]
        public void TheListHasNoHoles()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PopupSettings>(MetaSceneBuilder.PopupsPath);

            Assert.That(settings, Is.Not.Null);

            for (var i = 0; i < settings.popupBases.Count; i++)
            {
                Assert.That(settings.popupBases[i], Is.Not.Null,
                    "entry " + i + " of the popup list points at nothing");
            }
        }

        private static PopupBase Load(Type kind)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Built[kind]);

            Assert.That(prefab, Is.Not.Null,
                Built[kind] + " is missing — rebuild with Tools ▸ Relic Run ▸ Build Menu Scene");

            return (PopupBase)prefab.GetComponent(kind);
        }
    }
}
