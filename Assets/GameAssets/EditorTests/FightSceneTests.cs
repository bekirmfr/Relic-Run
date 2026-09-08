using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Editor.Importers;
using RelicRun.Game.Presentation;
using UnityEditor;
using UnityEngine;

namespace RelicRun.Tests.Editor
{
    /// <summary>
    /// The fight, wired into the game scene.
    /// </summary>
    /// <remarks>
    /// A scene here is a PREFAB under <c>Assets/Scenes/</c> — <c>Corescene.unity</c> is empty and
    /// the GameLift package's <c>SceneService</c> loads scene prefabs by key. So this asks its
    /// questions of an asset rather than of an open scene, which is also why it can ask them at
    /// all without entering play mode.
    ///
    /// A prefab is where a mistake is silent by construction: an empty reference looks exactly
    /// like one nobody has got round to, and <see cref="CombatView"/> needs fifteen. So the same
    /// rule the content bindings live under applies here — everything fillable is filled — and it
    /// is asked GENERICALLY, by walking every object reference the component has rather than by
    /// naming them. A field added tomorrow and forgotten fails this without anybody remembering
    /// to come back and add it.
    /// </remarks>
    [TestFixture]
    public class FightSceneTests
    {
        private GameObject _scene;

        [OneTimeSetUp]
        public void LoadTheScene()
        {
            _scene = AssetDatabase.LoadAssetAtPath<GameObject>(FightSceneBuilder.ScenePath);

            Assert.That(_scene, Is.Not.Null,
                FightSceneBuilder.ScenePath + " is missing — this project keeps its scenes as " +
                "prefabs, and this one should already exist");
        }

        private T Find<T>() where T : Component
        {
            return _scene.GetComponentInChildren<T>(true);
        }

        /// <summary>
        /// The scene answers to the service that loads it.
        /// </summary>
        /// <remarks>
        /// On the ROOT, because <c>SceneService</c> asks the object it instantiates for an
        /// <c>ISceneObject</c> and does not go hunting through the children. A screen that
        /// implemented it one level down would load, sit there, and never be initialised or
        /// cleared — which looks like a screen that simply does nothing.
        /// </remarks>
        [Test]
        public void TheSceneRootIsSomethingTheServiceCanLoad()
        {
            Assert.That(_scene.GetComponent<GameLift.Scene.ISceneObject>(), Is.Not.Null,
                "the root implements no ISceneObject, so nothing will initialise it");

            Assert.That(_scene.GetComponent<FightScene>(), Is.Not.Null);
        }

        [Test]
        public void TheSceneHasAViewAndSomethingToDriveIt()
        {
            Assert.That(Find<CombatView>(), Is.Not.Null, "nothing in the scene draws a fight");
            Assert.That(Find<FightHarness>(), Is.Not.Null, "nothing in the scene starts one");
            Assert.That(Find<Canvas>(), Is.Not.Null, "no canvas to draw on");
        }

        /// <summary>
        /// Every reference the fight needs is filled.
        /// </summary>
        /// <remarks>
        /// Walked rather than listed. Naming the fields here would mean the builder and the test
        /// both had to be remembered, and the entire reason the builder exists is that
        /// remembering fifteen things is what people are bad at.
        /// </remarks>
        [Test]
        public void EverythingTheSceneNeedsIsWired()
        {
            Filled(_scene.GetComponent<FightScene>());
            Filled(Find<CombatView>());
            Filled(Find<FightHarness>());
        }

        private static void Filled(Component component)
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
        /// The canvas scales rather than assuming a screen.
        /// </summary>
        /// <remarks>
        /// Left on Constant Pixel Size it looks right on the machine it was built on and wrong on
        /// every other, which is the sort of thing nobody notices until a screenshot arrives from
        /// a tablet.
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

        /// <summary>Both healths and both gauges fill rather than stretch.</summary>
        /// <remarks>
        /// An <c>Image</c> left on Simple ignores <c>fillAmount</c> entirely, so a health bar
        /// would sit permanently full while the delver died behind it. Nothing in the code can
        /// catch that — it is a setting on an asset, and this is the only place to ask.
        /// </remarks>
        [Test]
        public void EveryBarIsAFillingBar()
        {
            var bars = new List<UnityEngine.UI.Image>();

            foreach (UnityEngine.UI.Image image in
                     _scene.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (image.name == "Health" || image.name == "Gauge") bars.Add(image);
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

        /// <summary>
        /// Rebuilding replaces the fight and leaves the rest of the scene alone.
        /// </summary>
        /// <remarks>
        /// The builder deletes exactly one child by name. Everything else — the lifetime scope on
        /// the root, the camera, whatever somebody adds tomorrow — has to survive, because a
        /// generator that tidied up after other people would eventually tidy away something that
        /// mattered.
        /// </remarks>
        [Test]
        public void TheRestOfTheSceneIsLeftAlone()
        {
            Assert.That(_scene.GetComponent<VContainer.Unity.LifetimeScope>(), Is.Not.Null,
                "the scene's lifetime scope has gone");

            Assert.That(Find<Camera>(), Is.Not.Null, "the scene's camera has gone");

            var built = 0;
            foreach (Transform child in _scene.transform)
            {
                if (child.name == FightSceneBuilder.RootName) built++;
            }

            Assert.That(built, Is.EqualTo(1),
                "the fight is in the scene " + built + " times — a rebuild should replace it");
        }

        /// <summary>
        /// The scene is loadable: addressed, configured, and listed.
        /// </summary>
        /// <remarks>
        /// Three separate things, and all three have to be true. A prefab that is not addressable
        /// cannot be reached, because <c>SceneConfig</c> holds an <c>AssetReference</c> and
        /// nothing else. A config that names no key is a config nothing asks for. And a config
        /// the settings asset does not list is one <c>SceneService</c> will never find.
        ///
        /// Miss any one and the failure is the same shape: <c>LoadScene</c> is called, nothing
        /// happens, and nothing is said about it — which is the worst kind of wrong, and the
        /// reason this asks about all three separately rather than about the scene "working".
        /// </remarks>
        [Test]
        public void TheSceneCanActuallyBeLoaded()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameLift.Scene.SceneConfig>(
                FightSceneBuilder.ConfigPath);

            Assert.That(config, Is.Not.Null,
                "no scene config — run Tools > Relic Run > Build Fight Scene");

            Assert.That(config.SceneKey, Is.EqualTo(GameLift.Scene.SceneKeys.GameScene));
            Assert.That(config.SceneReference, Is.Not.Null);
            Assert.That(config.SceneReference.RuntimeKeyIsValid(), Is.True,
                "the config addresses nothing");

            Assert.That(AssetDatabase.GUIDToAssetPath(config.SceneReference.AssetGUID),
                Is.EqualTo(FightSceneBuilder.ScenePath),
                "the config addresses some other prefab");

            string[] settings = AssetDatabase.FindAssets("t:SceneServiceSettings");
            Assert.That(settings.Length, Is.EqualTo(1),
                "there should be exactly one settings asset; the service reads one of them and " +
                "a config in the other would look like a config that did nothing");

            var listed = AssetDatabase.LoadAssetAtPath<GameLift.Scene.SceneServiceSettings>(
                AssetDatabase.GUIDToAssetPath(settings[0]));

            Assert.That(listed.SceneConfigs, Does.Contain(config),
                "the settings asset does not list the game scene, so nothing can load it");

            // GetSceneConfig takes the first match, so a second claimant is not an error and
            // not reachable either — the scene that loads is whichever was listed first.
            var claiming = 0;
            foreach (GameLift.Scene.SceneConfig each in listed.SceneConfigs)
            {
                if (each != null && each.SceneKey == GameLift.Scene.SceneKeys.GameScene) claiming++;
            }

            Assert.That(claiming, Is.EqualTo(1),
                claiming + " configs claim " + GameLift.Scene.SceneKeys.GameScene +
                " — only the first is ever reached");
        }

        /// <summary>The two prefabs the view spawns exist and carry their text.</summary>
        [Test]
        public void TheSpawnedPrefabsAreWholeToo()
        {
            var flier = AssetDatabase.LoadAssetAtPath<GameObject>(FightSceneBuilder.FlierPrefab);
            var line = AssetDatabase.LoadAssetAtPath<GameObject>(FightSceneBuilder.LinePrefab);

            Assert.That(flier, Is.Not.Null, "no flying number to spawn");
            Assert.That(line, Is.Not.Null, "no log line to spawn");

            Filled(flier.GetComponent<FlyingNumber>());
            Filled(line.GetComponent<LogLine>());
        }
    }
}
