using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Editor.Importers;
using RelicRun.Game.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RelicRun.Tests.Editor
{
    /// <summary>
    /// The fight scene, wired.
    /// </summary>
    /// <remarks>
    /// A scene is the one place in a Unity project where a mistake is silent by construction: an
    /// empty reference looks exactly like one nobody has got round to, and the twelve
    /// <see cref="CombatView"/> needs are twelve chances to be ninety per cent wired.
    ///
    /// So the same rule the content bindings live under applies here — everything that can be
    /// filled is filled — and it is asked GENERICALLY, by walking every object reference the
    /// component has rather than by naming them. A field added tomorrow and forgotten fails this
    /// test without anybody remembering to come back and add it.
    /// </remarks>
    [TestFixture]
    public class FightSceneTests
    {
        private Scene _scene;

        [OneTimeSetUp]
        public void OpenTheScene()
        {
            Assert.That(System.IO.File.Exists(FightSceneBuilder.ScenePath), Is.True,
                FightSceneBuilder.ScenePath + " is missing — run Tools > Relic Run > Build Fight Scene");

            _scene = EditorSceneManager.OpenScene(FightSceneBuilder.ScenePath, OpenSceneMode.Additive);
        }

        [OneTimeTearDown]
        public void CloseTheScene()
        {
            if (_scene.IsValid()) EditorSceneManager.CloseScene(_scene, true);
        }

        private T Find<T>() where T : Component
        {
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }

            return null;
        }

        [Test]
        public void TheSceneHasAViewAndSomethingToDriveIt()
        {
            Assert.That(Find<CombatView>(), Is.Not.Null, "nothing in the scene draws a fight");
            Assert.That(Find<FightHarness>(), Is.Not.Null, "nothing in the scene starts one");
            Assert.That(Find<Canvas>(), Is.Not.Null, "no canvas to draw on");
        }

        /// <summary>
        /// Every reference the view and the harness need is filled.
        /// </summary>
        /// <remarks>
        /// Walked rather than listed. Naming the fields here would mean this test and the builder
        /// both had to be remembered, and the whole reason the builder exists is that remembering
        /// twelve things is what people are bad at.
        /// </remarks>
        [Test]
        public void EverythingTheSceneNeedsIsWired()
        {
            Empty(Find<CombatView>());
            Empty(Find<FightHarness>());
        }

        private static void Empty(Component component)
        {
            Assert.That(component, Is.Not.Null);

            var missing = new List<string>();
            SerializedProperty property = new SerializedObject(component).GetIterator();

            while (property.NextVisible(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;

                // Every component carries this, and nothing wires it.
                if (property.name == "m_Script") continue;

                if (property.objectReferenceValue == null) missing.Add(property.name);
            }

            Assert.That(missing, Is.Empty,
                component.GetType().Name + " has " + missing.Count + " empty references: " +
                string.Join(", ", missing) + " — rebuild with Tools > Relic Run > Build Fight Scene");
        }

        /// <summary>
        /// The canvas is authored at one size and scales, rather than being drawn for one phone.
        /// </summary>
        /// <remarks>
        /// A canvas left on Constant Pixel Size looks right on the machine it was built on and
        /// wrong on every other, which is the sort of thing nobody notices until a screenshot
        /// arrives from a tablet.
        /// </remarks>
        [Test]
        public void TheCanvasScalesRatherThanAssumingAScreen()
        {
            var scaler = Find<UnityEngine.UI.CanvasScaler>();

            Assert.That(scaler, Is.Not.Null);
            Assert.That(scaler.uiScaleMode,
                Is.EqualTo(UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize));
            Assert.That(scaler.referenceResolution.x, Is.GreaterThan(0f));
            Assert.That(scaler.referenceResolution.y, Is.GreaterThan(0f));
        }

        /// <summary>Both bars fill rather than stretch.</summary>
        /// <remarks>
        /// An <c>Image</c> left on Simple ignores <c>fillAmount</c> entirely, so a health bar
        /// would sit permanently full while the delver died behind it. Nothing in the code can
        /// catch that — it is a setting on an asset, and this is the only place to ask.
        /// </remarks>
        [Test]
        public void EveryBarIsAFillingBar()
        {
            var bars = new List<UnityEngine.UI.Image>();

            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (UnityEngine.UI.Image image in
                         root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    if (image.name == "Health" || image.name == "Gauge") bars.Add(image);
                }
            }

            Assert.That(bars.Count, Is.EqualTo(4), "two healths and two gauges");

            foreach (UnityEngine.UI.Image bar in bars)
            {
                Assert.That(bar.type, Is.EqualTo(UnityEngine.UI.Image.Type.Filled),
                    bar.name + " would ignore fillAmount and sit there full");
                Assert.That(bar.fillMethod, Is.EqualTo(UnityEngine.UI.Image.FillMethod.Horizontal),
                    bar.name + " fills the wrong way");
            }
        }

        /// <summary>The two prefabs the view spawns exist and carry their text.</summary>
        [Test]
        public void TheSpawnedPrefabsAreWholeToo()
        {
            var flier = AssetDatabase.LoadAssetAtPath<GameObject>(FightSceneBuilder.FlierPrefab);
            var line = AssetDatabase.LoadAssetAtPath<GameObject>(FightSceneBuilder.LinePrefab);

            Assert.That(flier, Is.Not.Null, "no flying number to spawn");
            Assert.That(line, Is.Not.Null, "no log line to spawn");

            Empty(flier.GetComponent<FlyingNumber>());
            Empty(line.GetComponent<LogLine>());
        }
    }
}
