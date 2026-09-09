using RelicRun.Editor.Importers;
using RelicRun.Game.Data;
using RelicRun.Game.Presentation;
using UnityEditor;
using UnityEngine;

namespace RelicRun.Editor
{
    /// <summary>
    /// One window that sets up a fight and runs it.
    /// </summary>
    /// <remarks>
    /// Everything this does was already possible, and that was the problem. Watching a custom
    /// fight meant knowing that the settings are an asset rather than a component, that the asset
    /// is in <c>Content</c>, that the scene has to have been built, that it has to be registered,
    /// and that startup has to be pointed at it rather than at the menu. Five things, none of
    /// them guessable, all of them silent when missed — a menu that opens on an empty canvas
    /// looks exactly like a game that is broken.
    ///
    /// So the window is not new capability. It is the same capability with the sequence written
    /// down, and every step it can take on your behalf offered as a button next to the sentence
    /// explaining why it is needed. A tool nobody has to remember how to use is worth more than
    /// a tool that is merely correct.
    ///
    /// The fight itself is edited INLINE here rather than reimplemented: the asset's own
    /// inspector is drawn in the window, so there is one description of a fight and one place its
    /// fields are laid out.
    /// </remarks>
    public sealed class FightLab : EditorWindow
    {
        private FightSettings _fight;
        private UnityEditor.Editor _inspector;
        private Vector2 _scroll;

        [MenuItem("Tools/Relic Run/Fight Lab", priority = 80)]
        public static void Open()
        {
            GetWindow<FightLab>("Fight Lab").Show();
        }

        private void OnEnable()
        {
            minSize = new Vector2(340f, 420f);
            _fight = Wired() ?? AssetDatabase.LoadAssetAtPath<FightSettings>(
                FightSceneBuilder.FightAsset);
        }

        private void OnDisable()
        {
            if (_inspector != null) DestroyImmediate(_inspector);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            Ready();
            EditorGUILayout.Space();
            Which();
            EditorGUILayout.Space();
            Fields();

            EditorGUILayout.EndScrollView();

            Run();
        }

        /* ---------- what is stopping you ---------- */

        /// <summary>
        /// Everything that has to be true before a fight can be watched, and a button for each.
        /// </summary>
        /// <remarks>
        /// Stated as a list rather than discovered by pressing Play and getting a black screen.
        /// Each of these fails silently on its own: no content and the screen draws nothing, no
        /// scene and there is nothing to load, no registration and <c>LoadScene</c> returns
        /// without a word, wrong startup and the menu opens instead — and the menu is empty, so
        /// it looks like a crash.
        /// </remarks>
        private void Ready()
        {
            EditorGUILayout.LabelField("Before it will run", EditorStyles.boldLabel);

            bool content = AssetDatabase.LoadAssetAtPath<GameContent>(
                ContentPaths.GameContentAsset) != null;

            Step(content, "Content is imported",
                "The art, the fonts and the pacing. Without it the fight draws nothing.",
                "Import Content", ContentImporter.Import);

            var scene = AssetDatabase.LoadAssetAtPath<GameObject>(FightSceneBuilder.ScenePath);
            bool built = scene != null && scene.GetComponentInChildren<FightHarness>(true) != null;

            Step(built, "The scene has a fight in it",
                "Build Fight Scene assembles the screen and wires it up.",
                "Build Fight Scene", FightSceneBuilder.Build);

            bool opens = FightSceneBuilder.OpensOnTheFight();

            Step(opens, "Play opens on the fight",
                "Otherwise it opens on the menu scene, which is empty — so it looks broken " +
                "rather than looking like a menu.",
                "Open on the fight", FightSceneBuilder.OpenOnTheFight);
        }

        private void Step(bool done, string what, string why, string fix, System.Action doIt)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(done ? "✓" : "✗", GUILayout.Width(16f));
                EditorGUILayout.LabelField(what, done ? EditorStyles.label : EditorStyles.boldLabel);

                using (new EditorGUI.DisabledScope(done))
                {
                    if (GUILayout.Button(fix, GUILayout.Width(140f)))
                    {
                        doIt();
                        Repaint();
                    }
                }
            }

            if (!done) EditorGUILayout.HelpBox(why, MessageType.Info);
        }

        /* ---------- which fight ---------- */

        /// <summary>
        /// Which fight, and the offer to make another one.
        /// </summary>
        /// <remarks>
        /// Duplicating is the point of the asset. A fight that reproduces something is worth
        /// keeping, and keeping it should not mean losing the one before it.
        /// </remarks>
        private void Which()
        {
            EditorGUILayout.LabelField("The fight", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _fight = (FightSettings)EditorGUILayout.ObjectField(_fight, typeof(FightSettings),
                    false);

                if (GUILayout.Button("Duplicate", GUILayout.Width(90f))) Duplicate();
            }

            FightSettings wired = Wired();

            if (_fight != null && wired != _fight)
            {
                EditorGUILayout.HelpBox(
                    "The scene is set up to show " + (wired == null ? "nothing" : wired.name) +
                    ". Pressing Play would show that, not this.", MessageType.Warning);

                if (GUILayout.Button("Show this one instead")) Use(_fight);
            }
        }

        private void Duplicate()
        {
            if (_fight == null) return;

            string from = AssetDatabase.GetAssetPath(_fight);
            string to = AssetDatabase.GenerateUniqueAssetPath(from);

            if (!AssetDatabase.CopyAsset(from, to)) return;

            AssetDatabase.SaveAssets();

            _fight = AssetDatabase.LoadAssetAtPath<FightSettings>(to);

            // Left unwired on purpose. Duplicating is how you keep the fight you already have,
            // so switching to the copy should be a thing you then choose rather than a thing
            // that happened to you.
            Debug.Log("made " + to + ". Press \"Show this one instead\" to run it.", _fight);
        }

        /* ---------- the fight's own fields ---------- */

        private void Fields()
        {
            if (_fight == null)
            {
                EditorGUILayout.HelpBox(
                    "No fight settings. Build Fight Scene makes one, or make one from " +
                    "Create > Relic Run > Fight.", MessageType.Info);

                return;
            }

            if (_inspector == null || _inspector.target != _fight)
            {
                if (_inspector != null) DestroyImmediate(_inspector);
                _inspector = UnityEditor.Editor.CreateEditor(_fight);
            }

            // The asset's own inspector, so there is one description of a fight rather than a
            // second layout here to keep in step with it.
            _inspector.OnInspectorGUI();
        }

        /* ---------- running it ---------- */

        private void Run()
        {
            EditorGUILayout.Space();

            if (Application.isPlaying)
            {
                if (GUILayout.Button("Stop", GUILayout.Height(32f)))
                {
                    EditorApplication.isPlaying = false;
                }

                return;
            }

            using (new EditorGUI.DisabledScope(_fight == null))
            {
                if (!GUILayout.Button("Play this fight", GUILayout.Height(32f))) return;

                // Everything the checklist offers, done in order, so that pressing the one
                // obvious button is enough. A tool that lists what is wrong and then refuses to
                // act on it has only moved the remembering somewhere else.
                if (Wired() != _fight) Use(_fight);

                FightSceneBuilder.OpenOnTheFight();
                AssetDatabase.SaveAssets();

                EditorApplication.EnterPlaymode();
            }
        }

        /* ---------- the scene's own wiring ---------- */

        /// <summary>Which fight the built scene is actually set up to show.</summary>
        private static FightSettings Wired()
        {
            var scene = AssetDatabase.LoadAssetAtPath<GameObject>(FightSceneBuilder.ScenePath);
            if (scene == null) return null;

            FightHarness harness = scene.GetComponentInChildren<FightHarness>(true);
            if (harness == null) return null;

            SerializedProperty found = new SerializedObject(harness).FindProperty("_fight");

            return found == null ? null : found.objectReferenceValue as FightSettings;
        }

        /// <summary>
        /// Points the built scene at a fight.
        /// </summary>
        /// <remarks>
        /// Through <c>LoadPrefabContents</c>, because the scene is a prefab asset and writing to
        /// a component fetched straight out of one does not stick. Wiring a reference is the one
        /// thing a generator may do to authored data, and it is all this does — the fight itself
        /// is never written to.
        /// </remarks>
        private static void Use(FightSettings fight)
        {
            GameObject scene = PrefabUtility.LoadPrefabContents(FightSceneBuilder.ScenePath);
            if (scene == null) return;

            try
            {
                FightHarness harness = scene.GetComponentInChildren<FightHarness>(true);

                if (harness == null)
                {
                    Debug.LogError("the scene has no fight in it — run Build Fight Scene first");
                    return;
                }

                var found = new SerializedObject(harness);
                found.FindProperty("_fight").objectReferenceValue = fight;
                found.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(scene, FightSceneBuilder.ScenePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(scene);
            }
        }
    }
}
