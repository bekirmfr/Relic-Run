using UnityEditor;
using UnityEngine;

namespace RelicRun.Editor
{
    /// <summary>
    /// Ends the run when scripts reload underneath it, rather than letting the game boot a
    /// second time on top of the first.
    /// </summary>
    /// <remarks>
    /// A domain reload in the middle of play mode does not restart the game — it starts a
    /// SECOND one. Everything already alive survives the reload (Corescene's contents, the
    /// pools, the scene prefab on screen, the DontDestroyOnLoad root scope), while every static
    /// field is wiped. VContainer keeps the root scope it built in a static, so with that gone
    /// its settings object concludes no root exists and instantiates the prefab again: two
    /// GameLift_LifetimeScope clones, two SaveServices writing the same file, two title screens,
    /// and — because the root prefab carries the game's one EventSystem — two of those as well.
    ///
    /// uGUI notices the last of those every single frame and says so, which is where the
    /// thousands of "There are 2 event systems in the scene" lines came from. They were the
    /// symptom that showed; the ones that did not show are worse.
    ///
    /// The second game is not the fixable half. The first one is already broken: a container is
    /// a plain C# object, so every resolver, every service and every injected reference in the
    /// surviving objects came back from the reload as null. There is nothing to continue. So the
    /// run ends, the reload proceeds in edit mode, and the next Play starts one game.
    ///
    /// Stopping when compilation STARTS is what usually does it — the reload waits for play mode
    /// to finish leaving. The check in the constructor is for the times it does not: the domain
    /// came back while still playing, both games exist, and the run ends a frame or two in
    /// instead of running on to the end of the session.
    /// </remarks>
    [InitializeOnLoad]
    internal static class PlayModeReload
    {
        static PlayModeReload()
        {
            EditorApplication.update += Watch;

            if (EditorApplication.isPlaying)
            {
                End("Scripts reloaded during play mode");
            }
        }

        private static void Watch()
        {
            if (EditorApplication.isPlaying && EditorApplication.isCompiling)
            {
                End("Scripts are compiling during play mode");
            }
        }

        private static void End(string cause)
        {
            Debug.LogWarning(
                $"{cause}, and a reload leaves the game with no container to run on. " +
                "Ending the run — press Play again once the compile finishes.");
            EditorApplication.isPlaying = false;
        }
    }
}
