using System.Collections.Generic;
using GameLift.Scene;
using NUnit.Framework;
using RelicRun.Core.Presentation;
using RelicRun.Editor.Importers;
using RelicRun.Game.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Tests.Editor
{
    /// <summary>
    /// The title, built into its own scene prefab.
    /// </summary>
    /// <remarks>
    /// The same questions the fight's scene is asked, for the same reason: a prefab is where a
    /// mistake is silent by construction. An empty reference looks exactly like one nobody has
    /// got round to, and <see cref="TitleView"/> needs fifteen of them.
    ///
    /// So the references are asked about GENERICALLY, by walking every object reference a
    /// component has rather than by naming them. A field added tomorrow and forgotten fails this
    /// without anybody remembering to come back and add it.
    /// </remarks>
    [TestFixture]
    public class TitleSceneTests
    {
        private GameObject _scene;

        [OneTimeSetUp]
        public void LoadTheScene()
        {
            _scene = AssetDatabase.LoadAssetAtPath<GameObject>(TitleSceneBuilder.ScenePath);

            Assert.That(_scene, Is.Not.Null,
                TitleSceneBuilder.ScenePath + " is missing — run Tools ▸ Relic Run ▸ " +
                "Build Title Scene");
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
        /// implemented it one level down would load, sit there, and never be initialised — which
        /// looks like a screen that simply does nothing.
        /// </remarks>
        [Test]
        public void TheSceneRootIsSomethingTheServiceCanLoad()
        {
            Assert.That(_scene.GetComponent<ISceneObject>(), Is.Not.Null,
                "the root implements no ISceneObject, so nothing will initialise it");

            Assert.That(_scene.GetComponent<TitleScene>(), Is.Not.Null);
        }

        /// <summary>
        /// The scene has a scope of its own, which is how it finds the save.
        /// </summary>
        /// <remarks>
        /// <c>SceneService</c> instantiates inside <c>LifetimeScope.EnqueueParent</c>, so a scope
        /// on this prefab is parented to the application's and can resolve what is registered
        /// there. Without one the title still opens — and shows a delver who has never played,
        /// every launch, with nothing but a warning in the log to say why.
        /// </remarks>
        [Test]
        public void TheSceneCanReachWhatTheAppRegistered()
        {
            Assert.That(_scene.GetComponent<VContainer.Unity.LifetimeScope>(), Is.Not.Null,
                "no lifetime scope, so the title cannot find the delver's save");
        }

        /// <summary>Everything fillable is filled.</summary>
        [Test]
        public void EveryReferenceOnTheViewIsWired()
        {
            Filled(Find<TitleView>());
        }

        /// <summary>And the scene knows what it draws on.</summary>
        [Test]
        public void TheSceneKnowsItsView()
        {
            Filled(_scene.GetComponent<TitleScene>());
        }

        private static void Filled(Component component)
        {
            Assert.That(component, Is.Not.Null, "the component is not in the scene at all");

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
                string.Join(", ", missing) + " — rebuild with Tools > Relic Run > Build Title Scene");
        }

        /// <summary>
        /// The canvas scales by a whole number, and something keeps it that way.
        /// </summary>
        /// <remarks>
        /// The lesson the fight paid for, asked again here rather than assumed to carry over. A
        /// fractional factor smears a face baked on an eight-pixel grid, and a second screen is a
        /// second chance to author one.
        /// </remarks>
        [Test]
        public void TheCanvasScalesByAWholeNumberOfPixels()
        {
            var scaler = Find<CanvasScaler>();

            Assert.That(scaler, Is.Not.Null);
            Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ConstantPixelSize),
                "a fractional scale smears a face baked on a pixel grid");

            Assert.That(scaler.scaleFactor, Is.EqualTo(Mathf.Round(scaler.scaleFactor)),
                "the authored factor is already fractional");

            PixelCanvas keeper = Find<PixelCanvas>();

            Assert.That(keeper, Is.Not.Null,
                "nothing recomputes the factor, so it is frozen at whatever was authored");
            Assert.That(keeper.enabled, Is.True, "the pixel scale keeper is disabled");
            Assert.That(keeper.gameObject.activeSelf, Is.True,
                "the pixel scale keeper is on an inactive object");
        }

        /// <summary>Every text on the title is a size the baked face can draw.</summary>
        /// <remarks>
        /// Walked rather than listed. The ui face is baked at eight pixels, so a size of twenty
        /// draws it at two and a half times and one row in every two has to round — and twenty is
        /// not a silly number to type, which is exactly why this is asked of the built asset.
        /// </remarks>
        [Test]
        public void EveryTextIsASizeTheFaceCanDraw()
        {
            var wrong = new List<string>();

            foreach (TMPro.TMP_Text text in _scene.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (text.fontSize % PixelScale.Grid != 0f)
                {
                    wrong.Add(text.name + " at " + text.fontSize);
                }
            }

            Assert.That(wrong, Is.Empty,
                "these draw the ui face at a fraction of the size it was baked at: " +
                string.Join(", ", wrong));
        }

        /// <summary>
        /// The level bar is a bar, not a rectangle.
        /// </summary>
        /// <remarks>
        /// An <c>Image</c> left on Simple ignores <c>fillAmount</c>, and one with NO sprite
        /// ignores its type as well — Unity falls back to a plain quad before it ever looks at
        /// the fill. Both were true of every bar in the fight for a while, and both are invisible
        /// in code: they are settings on an asset, and this is the only place to ask.
        /// </remarks>
        [Test]
        public void TheLevelBarCanActuallyShowAFraction()
        {
            var view = new SerializedObject(Find<TitleView>());
            var bar = view.FindProperty("_levelBar").objectReferenceValue as Image;

            Assert.That(bar, Is.Not.Null, "nothing shows how far through a level the delver is");

            Assert.That(bar.type, Is.EqualTo(Image.Type.Filled),
                "a Simple image ignores fillAmount, so the bar sits wherever it was authored");
            Assert.That(bar.sprite, Is.Not.Null,
                "an image with no sprite draws a plain quad and ignores its type entirely");
            Assert.That(bar.fillMethod, Is.EqualTo(Image.FillMethod.Horizontal));
        }

        /// <summary>
        /// Nothing on this screen is given a fixed width.
        /// </summary>
        /// <remarks>
        /// A whole scale factor gives a 1440-wide phone 480 canvas units and a 1080-wide one 540,
        /// so a panel with a fixed width takes a different share of each. Everything here is
        /// stretched across its parent and inset instead — which shows up as an anchor spanning
        /// the full width rather than sitting at a point.
        /// </remarks>
        [Test]
        public void EveryRowTakesItsWidthFromTheScreen()
        {
            var fixedWidth = new List<string>();

            foreach (Transform child in Rows())
            {
                var rect = (RectTransform)child;

                if (rect.anchorMin.x != 0f || rect.anchorMax.x != 1f) fixedWidth.Add(rect.name);
            }

            Assert.That(fixedWidth, Is.Empty,
                "these take a different share of the screen on every device: " +
                string.Join(", ", fixedWidth));
        }

        /// <summary>The rows directly under the canvas, which are what the layout is made of.</summary>
        private IEnumerable<Transform> Rows()
        {
            var canvas = Find<Canvas>();

            Assert.That(canvas, Is.Not.Null, "no canvas at all");

            var rows = new List<Transform>();

            foreach (Transform child in canvas.transform) rows.Add(child);

            Assert.That(rows, Is.Not.Empty, "the canvas is empty");

            return rows;
        }

        /// <summary>
        /// The scene is loadable: addressed, configured, and listed.
        /// </summary>
        /// <remarks>
        /// Three separate things, and a scene is only loadable when all three are true. Miss any
        /// one and the failure is the same shape: <c>LoadScene</c> is called, nothing happens,
        /// and nothing is said about it.
        /// </remarks>
        [Test]
        public void TheTitleIsWhatTheMenuKeyLoads()
        {
            var config = AssetDatabase.LoadAssetAtPath<SceneConfig>(TitleSceneBuilder.ConfigPath);

            Assert.That(config, Is.Not.Null, "no scene config at " + TitleSceneBuilder.ConfigPath);
            Assert.That(config.SceneKey, Is.EqualTo(SceneKeys.MenuScene));

            Assert.That(config.SceneReference, Is.Not.Null);
            Assert.That(config.SceneReference.AssetGUID, Is.Not.Null.And.Not.Empty,
                "the config names no asset, so the key resolves to nothing");

            string path = AssetDatabase.GUIDToAssetPath(config.SceneReference.AssetGUID);

            Assert.That(path, Is.EqualTo(TitleSceneBuilder.ScenePath),
                SceneKeys.MenuScene + " loads " + path + " rather than the title");
        }

        /// <summary>Only one config may claim the key the app starts on.</summary>
        /// <remarks>
        /// The service takes the FIRST config claiming a key. A second is not an error anywhere
        /// and never will be — it is simply never reached, and the scene that loads is the one
        /// somebody wrote earlier and forgot.
        /// </remarks>
        [Test]
        public void NothingElseClaimsTheMenuKey()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SceneServiceSettings>(
                "Assets/Samples/Game Lift/1.0.0/Starter/ScriptableObjects/" +
                "SceneServiceSettings/SceneServiceSettings.asset");

            Assert.That(settings, Is.Not.Null, "no scene service settings");
            Assert.That(settings.SceneConfigs, Is.Not.Null);

            var claiming = new List<string>();

            foreach (SceneConfig config in settings.SceneConfigs)
            {
                if (config == null) continue;
                if (config.SceneKey == SceneKeys.MenuScene) claiming.Add(config.name);
            }

            Assert.That(claiming.Count, Is.EqualTo(1),
                SceneKeys.MenuScene + " is claimed by " + claiming.Count + " configs: " +
                string.Join(", ", claiming));
        }
    }
}
