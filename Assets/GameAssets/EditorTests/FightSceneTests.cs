using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Presentation;
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
            Assert.That(Find<RelicTray>(), Is.Not.Null, "nothing in the scene draws the shelf");
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
            Filled(Find<RelicTray>());
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
        /// The canvas scales by a whole number, and something keeps it that way.
        /// </summary>
        /// <remarks>
        /// This test used to assert the opposite — Scale With Screen Size, which is the sensible
        /// default for almost every interface and is wrong for this one. It gives whatever
        /// fraction makes the reference fit, about 1.118 on a common phone, and the ui face is a
        /// bitmap baked on an eight-pixel grid. At 1.118 times, some rows of a glyph get five
        /// screen pixels and the next gets six. That is what "the fonts look ugly" turned out to
        /// mean, and it survived a change of typeface because it was never about the typeface.
        ///
        /// Both halves are asked. The mode alone is not enough: <c>ConstantPixelSize</c> with
        /// nothing maintaining the factor is a canvas frozen at whatever the last person typed,
        /// which is right on one screen and wrong on all the others.
        /// </remarks>
        [Test]
        public void TheCanvasScalesByAWholeNumberOfPixels()
        {
            var scaler = Find<UnityEngine.UI.CanvasScaler>();

            Assert.That(scaler, Is.Not.Null);
            Assert.That(scaler.uiScaleMode,
                Is.EqualTo(UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize),
                "a fractional scale smears a face baked on a pixel grid");

            Assert.That(scaler.scaleFactor, Is.EqualTo(Mathf.Round(scaler.scaleFactor)),
                "the authored factor is already fractional");

            PixelCanvas keeper = Find<PixelCanvas>();

            Assert.That(keeper, Is.Not.Null,
                "nothing recomputes the factor, so it is frozen at whatever was authored");

            // Present is not the same as running, and the difference is invisible: a disabled
            // component never gets OnEnable, so the canvas silently keeps the authored factor
            // and looks correct on exactly one screen.
            Assert.That(keeper.enabled, Is.True, "the pixel scale keeper is disabled");
            Assert.That(keeper.gameObject.activeSelf, Is.True,
                "the pixel scale keeper is on an inactive object");

            Assert.That(keeper.GetComponent<UnityEngine.UI.CanvasScaler>(), Is.Not.Null,
                "the keeper is not on the object it scales");
        }

        /// <summary>
        /// Every text in the scene is a size the baked face can actually draw.
        /// </summary>
        /// <remarks>
        /// The other half of the same rule, and the half a person breaks by accident. The ui face
        /// is baked at eight pixels, so a size of twenty draws it at two and a half times and one
        /// row in every two has to round. Twenty is not a silly number to type — that is exactly
        /// why this is asked of the built asset rather than trusted to the builder, which is
        /// where somebody will eventually type it.
        ///
        /// Walked, not listed, for the same reason the references are.
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

                // Width from the panel, never a number. A whole scale factor gives the bigger
                // screen the smaller canvas — 540 units at 2x, 480 at 3x — so a bar with an
                // authored width is a different fraction of the screen on every device. For the
                // one element read as a proportion, that is telling each delver something
                // different about how much trouble they are in.
                var rect = (RectTransform)bar.transform;

                Assert.That(rect.anchorMin.x, Is.EqualTo(0f),
                    bar.name + " does not start at its panel's left edge");
                Assert.That(rect.anchorMax.x, Is.EqualTo(1f),
                    bar.name + " has an authored width instead of its panel's");
                Assert.That(rect.sizeDelta.x, Is.EqualTo(0f),
                    bar.name + " insets itself from the panel that already holds the margin");
            }
        }

        /// <summary>
        /// The log clips rather than squashing what it cannot fit.
        /// </summary>
        /// <remarks>
        /// A <c>VerticalLayoutGroup</c> handed more children than fit does not overflow — it
        /// divides the space it has. So sixty lines in three hundred units were allotted five
        /// each and drew through one another, and the log became an unreadable smear exactly when
        /// there was most to read.
        ///
        /// The fix has three parts and all three are needed, which is why all three are asked
        /// for. The panel masks, so anything past its edge is cut off. The lines live in a child
        /// that sizes ITSELF to its content, so the column is as tall as it needs to be rather
        /// than as tall as it is allowed. And that child is pinned to the BOTTOM, so it grows
        /// upward out of view and the newest line stays where the eye is.
        /// </remarks>
        [Test]
        public void TheLogClipsRatherThanSquashing()
        {
            RectTransform panel = Named("Log");

            Assert.That(panel, Is.Not.Null, "no log panel");
            Assert.That(panel.GetComponent<UnityEngine.UI.RectMask2D>(), Is.Not.Null,
                "the log does not clip, so long fights overflow it instead of scrolling");

            Assert.That(panel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>(), Is.Null,
                "the layout group is on the clipping panel, so it will squash to fit rather " +
                "than overflow and be cut off");

            RectTransform lines = Named("Lines");

            Assert.That(lines, Is.Not.Null, "nothing inside the log holds the lines");
            Assert.That(lines.parent, Is.EqualTo(panel), "the lines are not inside the mask");

            var group = lines.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            Assert.That(group, Is.Not.Null);

            var fitter = lines.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            Assert.That(fitter, Is.Not.Null, "the column cannot grow, so it will squash");
            Assert.That(fitter.verticalFit,
                Is.EqualTo(UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize));

            Assert.That(lines.pivot.y, Is.EqualTo(0f),
                "the column grows from the wrong end, so old lines stay and new ones are cut off");
            Assert.That(lines.anchorMin.y, Is.EqualTo(0f));
            Assert.That(lines.anchorMax.y, Is.EqualTo(0f));
        }

        /// <summary>The foe says what it is, and what it is carrying.</summary>
        /// <remarks>
        /// A red bar and a name were all there was. How hard it hits, how fast it moves and what
        /// it is wearing were all invisible — and the relics are the half that changes what the
        /// fight MEANS, since a foe with Thorn Vest punishes a delver for the thing they are
        /// otherwise supposed to do.
        /// </remarks>
        [Test]
        public void TheFoeShowsItsNumbersAndItsRelics()
        {
            var view = Find<CombatView>();
            var found = new SerializedObject(view);

            Assert.That(found.FindProperty("_enemyStats").objectReferenceValue, Is.Not.Null,
                "nothing shows the foe's stats");
            Assert.That(found.FindProperty("_enemyRelics").objectReferenceValue, Is.Not.Null,
                "nothing shows what the foe is carrying");

            var trays = _scene.GetComponentsInChildren<RelicTray>(true);

            Assert.That(trays.Length, Is.EqualTo(2),
                "one shelf for the delver and one for the foe, and found " + trays.Length);
        }

        private RectTransform Named(string name)
        {
            foreach (RectTransform each in _scene.GetComponentsInChildren<RectTransform>(true))
            {
                if (each.name == name) return each;
            }

            return null;
        }

        /// <summary>
        /// The three stacked panels share one margin, so they share one left edge.
        /// </summary>
        /// <remarks>
        /// Foe, Delver and Log are the full-width column of the screen and each was free to pick
        /// its own width. They did: the log was authored at 450 units against panels that
        /// stretched, which put its left edge somewhere different from the bars' on every device,
        /// because a whole scale factor gives each screen a different number of units.
        ///
        /// The log's width does more than line things up — it decides where a line WRAPS. Fixed,
        /// a translated line breaks in a different place on every device and whether it fits at
        /// all is settled by whichever phone somebody happened to test on.
        ///
        /// Purse is deliberately not in this list: it is a badge in the top corner, and a badge
        /// that stretched across the screen would be a mistake of a different kind.
        /// </remarks>
        [Test]
        public void TheStackedPanelsShareOneMargin()
        {
            foreach (string name in new[] { "Foe", "Delver", "Log" })
            {
                RectTransform panel = Named(name);

                Assert.That(panel, Is.Not.Null, "no " + name + " panel in the built fight");

                Assert.That(panel.anchorMin.x, Is.EqualTo(0f), name + " does not start at the left");
                Assert.That(panel.anchorMax.x, Is.EqualTo(1f), name + " has an authored width");
                Assert.That(panel.sizeDelta.x, Is.EqualTo(-40f),
                    name + " keeps a different margin from the panels it stacks with");
            }
        }

        /// <summary>
        /// What a person edits is not inside what the generator rebuilds.
        /// </summary>
        /// <remarks>
        /// This is a regression, and the failure was silent in the worst way. The fight's settings
        /// — seed, stats, shelf, opposition — began as fields on <see cref="FightHarness"/>, which
        /// lives inside the <c>Fight</c> child that <c>Build Fight Scene</c> DELETES and adds back
        /// from scratch. So everything typed into the inspector survived until the next rebuild
        /// and then quietly reverted, and the harness went on showing a fight nobody had asked
        /// for, with nothing anywhere to say a setting had been thrown away.
        ///
        /// The rule this asserts is the general one: a generator may wire a reference to authored
        /// data and must never own it. Asked structurally rather than by running the builder,
        /// because a test that rebuilt the scene to prove the point would be a test that rewrites
        /// the project to check it is not rewritten.
        /// </remarks>
        [Test]
        public void WhatAPersonEditsSurvivesARebuild()
        {
            var harness = Find<FightHarness>();

            Assert.That(harness, Is.Not.Null);

            var found = new SerializedObject(harness).FindProperty("_fight");

            Assert.That(found, Is.Not.Null, "the harness holds no fight settings");
            Assert.That(found.objectReferenceValue, Is.Not.Null,
                "no fight is wired — rebuild with Tools > Relic Run > Build Fight Scene");

            string path = AssetDatabase.GetAssetPath(found.objectReferenceValue);

            Assert.That(path, Is.Not.Empty,
                "the fight settings are not an asset at all, so they live and die with the scene");

            Assert.That(path, Is.Not.EqualTo(FightSceneBuilder.ScenePath),
                "the settings are inside the very prefab the builder rebuilds, so every edit to " +
                "them is thrown away by the next rebuild");

            Assert.That(path, Is.EqualTo(FightSceneBuilder.FightAsset));
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

            // Whichever way the Start In The Fight toggle is set, startup has to land somewhere.
            // AppStartupOrchestrator reads DefaultSceneConfig.SceneKey without asking whether it
            // is there, so an unset default is not a game that opens on nothing — it is a game
            // that throws on its first line, and a black screen says nothing about why.
            Assert.That(listed.DefaultSceneConfig, Is.Not.Null,
                "nothing is set to start, and startup dereferences it");

            Assert.That(listed.SceneConfigs, Does.Contain(listed.DefaultSceneConfig),
                "startup opens on a config the service cannot look up");
        }

        /// <summary>
        /// Nothing in the scene starts the fight except the scene.
        /// </summary>
        /// <remarks>
        /// <c>SceneService</c> instantiates the prefab and then awaits <c>Initialize</c>, so a
        /// component that also begins work from <c>Awake</c> or <c>Start</c> does it twice —
        /// once on its own and once when asked. That happened: the harness self-started, and
        /// two fights ran over one view, spawning every log line twice. The doubled log read as
        /// a fight in which every blow landed twice, which is a plausible-looking wrong answer
        /// and so took a screenshot to notice.
        ///
        /// Asked by reflection because the rule is about the ABSENCE of a method, and there is
        /// no other way to ask about something that is not there. The two names are Unity's and
        /// cannot drift.
        /// </remarks>
        [Test]
        public void TheHarnessDoesNotStartItself()
        {
            const System.Reflection.BindingFlags Declared =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly;

            foreach (string magic in new[] { "Awake", "Start" })
            {
                Assert.That(typeof(FightHarness).GetMethod(magic, Declared), Is.Null,
                    "FightHarness." + magic + " runs on its own as well as when the scene asks, " +
                    "so the fight would play twice over one view");
            }
        }

        /// <summary>
        /// A relic slot's three gauges all actually fill.
        /// </summary>
        /// <remarks>
        /// The same trap as the health bars and worse here. An <c>Image</c> left on Simple ignores
        /// <c>fillAmount</c> and sits there FULL, and on a charge bar full means "about to fire" —
        /// so every relic on the shelf would look permanently one strike from going off.
        ///
        /// The directions matter as much as the type. Charge and hairline run along the bottom
        /// left to right because they fill toward something; the uses bar runs down the right side
        /// because a budget empties rather than fills, and one drawn the other way would read as a
        /// relic getting stronger as it ran out.
        /// </remarks>
        [Test]
        public void EveryGaugeOnASlotIsAFillingBar()
        {
            var slot = AssetDatabase.LoadAssetAtPath<GameObject>(FightSceneBuilder.SlotPrefab);

            Assert.That(slot, Is.Not.Null, "no relic slot — rebuild the fight scene");

            var along = new[] { "Charge", "Hairline" };
            var found = new List<string>();

            foreach (UnityEngine.UI.Image bar in
                     slot.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (bar.name != "Charge" && bar.name != "Hairline" && bar.name != "Uses") continue;

                found.Add(bar.name);

                Assert.That(bar.type, Is.EqualTo(UnityEngine.UI.Image.Type.Filled),
                    bar.name + " would ignore fillAmount and sit there full, which on a charge " +
                    "bar means every relic looks one strike from firing");

                bool sideways = System.Array.IndexOf(along, bar.name) >= 0;

                Assert.That(bar.fillMethod,
                    Is.EqualTo(sideways
                        ? UnityEngine.UI.Image.FillMethod.Horizontal
                        : UnityEngine.UI.Image.FillMethod.Vertical),
                    bar.name + " fills the wrong way");
            }

            Assert.That(found, Is.EquivalentTo(new[] { "Charge", "Hairline", "Uses" }),
                "a slot draws two cadences and a budget, and found: " + string.Join(", ", found));
        }

        /// <summary>The three prefabs the view spawns exist and carry their text.</summary>
        [Test]
        public void TheSpawnedPrefabsAreWholeToo()
        {
            var flier = AssetDatabase.LoadAssetAtPath<GameObject>(FightSceneBuilder.FlierPrefab);
            var line = AssetDatabase.LoadAssetAtPath<GameObject>(FightSceneBuilder.LinePrefab);
            var slot = AssetDatabase.LoadAssetAtPath<GameObject>(FightSceneBuilder.SlotPrefab);

            Assert.That(flier, Is.Not.Null, "no flying number to spawn");
            Assert.That(line, Is.Not.Null, "no log line to spawn");
            Assert.That(slot, Is.Not.Null, "no relic slot to spawn");

            Filled(slot.GetComponent<RelicSlot>());

            // These live outside the scene and are spawned into it, so the walk above never sees
            // them — and between them they are most of the text a delver actually reads.
            foreach (GameObject spawned in new[] { flier, line })
            {
                foreach (TMPro.TMP_Text text in spawned.GetComponentsInChildren<TMPro.TMP_Text>(true))
                {
                    Assert.That(text.fontSize % PixelScale.Grid, Is.Zero,
                        spawned.name + " draws at " + text.fontSize +
                        ", a fraction of the size the face was baked at");
                }
            }

            Filled(flier.GetComponent<FlyingNumber>());
            Filled(line.GetComponent<LogLine>());
        }
    }
}
