using GameLift.Scene;
using UnityEditor;
using UnityEngine;

namespace RelicRun.Editor.Importers
{
    /// <summary>
    /// Lets go of the scene prefabs when play mode ends.
    /// </summary>
    /// <remarks>
    /// A <c>SceneConfig</c> holds an <c>AssetReference</c>, and an AssetReference caches the
    /// Addressables handle it loaded — ON THE ASSET, which outlives play mode. So a scene that
    /// was loaded and not released stays "loaded" after the game has stopped, and the next
    /// attempt to load it throws:
    ///
    ///     Attempting to load AssetReference that has already been loaded.
    ///
    /// <c>SceneService</c> releases properly through its own <c>Clear</c>, so this never bites a
    /// build. It bites the EDITOR, whenever play mode ends with a scene still up — which is every
    /// time somebody presses stop, and every time a scene fails on its way out. The symptom is
    /// the worst kind: the next session's button does nothing at all, because the service catches
    /// the exception, logs it as an ordinary message, and returns null.
    ///
    /// That cost an afternoon once. The handle was live on the fight's config from a session
    /// where the fight had been the startup scene, and every later press of PLAY loaded nothing
    /// and said nothing.
    /// </remarks>
    [InitializeOnLoad]
    public static class SceneHandles
    {
        /// <summary>Where the service looks for what it may load.</summary>
        private const string SettingsPath = "Assets/Samples/Game Lift/1.0.0/Starter/" +
            "ScriptableObjects/SceneServiceSettings/SceneServiceSettings.asset";

        static SceneHandles()
        {
            EditorApplication.playModeStateChanged += Changed;
        }

        private static void Changed(PlayModeStateChange state)
        {
            // On the way OUT, and on the way in. Out is the leak itself; in is for a handle that
            // leaked before this existed, or that survived a domain reload, and it costs one
            // pass over a list of two.
            if (state != PlayModeStateChange.EnteredEditMode &&
                state != PlayModeStateChange.ExitingEditMode)
            {
                return;
            }

            Release();
        }

        /// <summary>Releases every scene reference that is still holding one.</summary>
        public static void Release()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SceneServiceSettings>(SettingsPath);

            if (settings == null || settings.SceneConfigs == null) return;

            foreach (SceneConfig config in settings.SceneConfigs)
            {
                if (config == null || config.SceneReference == null) continue;
                if (!config.SceneReference.IsValid()) continue;

                config.SceneReference.ReleaseAsset();

                Debug.Log("let go of " + config.name + ", which was still holding its scene from " +
                          "a previous session", config);
            }
        }
    }
}
