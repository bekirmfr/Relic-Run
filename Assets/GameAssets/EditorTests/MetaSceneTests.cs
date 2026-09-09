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
    /// got round to, and <see cref="TitlePanel"/> needs fifteen of them.
    ///
    /// So the references are asked about GENERICALLY, by walking every object reference a
    /// component has rather than by naming them. A field added tomorrow and forgotten fails this
    /// without anybody remembering to come back and add it.
    /// </remarks>
    [TestFixture]
    public class MetaSceneTests
    {
        private GameObject _scene;

        [OneTimeSetUp]
        public void LoadTheScene()
        {
            _scene = AssetDatabase.LoadAssetAtPath<GameObject>(MetaSceneBuilder.ScenePath);

            Assert.That(_scene, Is.Not.Null,
                MetaSceneBuilder.ScenePath + " is missing — run Tools ▸ Relic Run ▸ " +
                "Build Menu Scene");
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

            Assert.That(_scene.GetComponent<MetaScene>(), Is.Not.Null);
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
        public void EveryReferenceOnTheTitleIsWired()
        {
            Filled(Find<TitlePanel>());
        }

        /// <summary>And the scene knows what it draws on.</summary>
        [Test]
        public void TheSceneKnowsItsPanels()
        {
            Filled(_scene.GetComponent<MetaScene>());
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
                string.Join(", ", missing) + " — rebuild with Tools > Relic Run > Build Menu Scene");
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
            var view = new SerializedObject(Find<TitlePanel>());
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

        /// <summary>Every reference the dungeon list needs is wired too.</summary>
        [Test]
        public void EveryReferenceOnTheDungeonListIsWired()
        {
            Filled(Find<LevelsPanel>());
        }

        /// <summary>And the mode picker's.</summary>
        [Test]
        public void EveryReferenceOnTheModePickerIsWired()
        {
            Filled(Find<ModesPanel>());
        }

        /// <summary>
        /// Both panels are listed on the scene, and each is listed once.
        /// </summary>
        /// <remarks>
        /// The scene finds panels from this array rather than by walking its children, because a
        /// hidden panel is an INACTIVE object and the cheap search does not return those. A panel
        /// missing from the list is a screen that cannot be navigated to; a panel listed twice is
        /// one that gets hidden immediately after being shown.
        /// </remarks>
        [Test]
        public void EveryPanelIsListedOnceOnTheScene()
        {
            var listed = new SerializedObject(_scene.GetComponent<MetaScene>())
                .FindProperty("_panels");

            var pages = new List<Page>();

            for (var i = 0; i < listed.arraySize; i++)
            {
                var panel = listed.GetArrayElementAtIndex(i).objectReferenceValue as MetaPanel;

                Assert.That(panel, Is.Not.Null, "panel " + i + " is empty");
                Assert.That(pages.Contains(panel.Shows), Is.False,
                    panel.Shows + " is listed twice");

                pages.Add(panel.Shows);
            }

            foreach (MetaPanel built in _scene.GetComponentsInChildren<MetaPanel>(true))
            {
                Assert.That(pages.Contains(built.Shows), Is.True,
                    built.Shows + " is in the scene but not listed, so nothing can reach it");
            }

            Assert.That(pages.Count, Is.GreaterThan(1), "only one panel was built");
        }

        /// <summary>
        /// The hall tile is a prefab and is NOT sitting in the built grid.
        /// </summary>
        /// <remarks>
        /// The panel spawns from it. A live copy left in the layout would be an eleventh hall
        /// that never redresses — always showing whatever the builder last typed into it, in a
        /// grid where every other square is real.
        /// </remarks>
        [Test]
        public void TheHallTileIsATemplateRatherThanAHall()
        {
            var tile = AssetDatabase.LoadAssetAtPath<GameObject>(MetaSceneBuilder.TilePrefab);

            Assert.That(tile, Is.Not.Null, MetaSceneBuilder.TilePrefab + " is missing");
            Assert.That(tile.GetComponent<HallTileView>(), Is.Not.Null);

            Filled(tile.GetComponent<HallTileView>());

            Assert.That(_scene.GetComponentsInChildren<HallTileView>(true), Is.Empty,
                "a tile was built into the scene, so the grid opens with a hall nobody made");
        }

        /// <summary>The rows directly under the canvas, which are what the layout is made of.</summary>
        private IEnumerable<Transform> Rows()
        {
            var rows = new List<Transform>();

            // Each panel fills the canvas; what has to take its width from the screen is what is
            // inside them. So the question is asked one level down rather than at the top.
            foreach (MetaPanel panel in _scene.GetComponentsInChildren<MetaPanel>(true))
            {
                foreach (Transform child in panel.transform) rows.Add(child);
            }

            Assert.That(rows, Is.Not.Empty, "no panels, or every panel is empty");

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
            var config = AssetDatabase.LoadAssetAtPath<SceneConfig>(MetaSceneBuilder.ConfigPath);

            Assert.That(config, Is.Not.Null, "no scene config at " + MetaSceneBuilder.ConfigPath);
            Assert.That(config.SceneKey, Is.EqualTo(SceneKeys.MenuScene));

            Assert.That(config.SceneReference, Is.Not.Null);
            Assert.That(config.SceneReference.AssetGUID, Is.Not.Null.And.Not.Empty,
                "the config names no asset, so the key resolves to nothing");

            string path = AssetDatabase.GUIDToAssetPath(config.SceneReference.AssetGUID);

            Assert.That(path, Is.EqualTo(MetaSceneBuilder.ScenePath),
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
