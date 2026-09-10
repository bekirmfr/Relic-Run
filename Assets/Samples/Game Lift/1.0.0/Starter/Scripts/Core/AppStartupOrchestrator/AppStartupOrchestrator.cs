using System.Threading;
using Cysharp.Threading.Tasks;
using GameLift.Attribution;
using GameLift.PrivacyConsent;
using GameLift.Scene;
using UnityEngine;
using VContainer.Unity;
namespace GameLift.AppStartup
{
    public class AppStartupOrchestrator : IAsyncStartable
    {
        private readonly IPrivacyConsentService _privacyConsentService;
        private readonly IAttributionService _attributionService;
        private readonly SceneFlowController _sceneFlowController;
        private readonly SceneServiceSettings _settings;
        private readonly ISceneService _sceneService;

        public AppStartupOrchestrator(IPrivacyConsentService privacyConsentService, IAttributionService attributionService, 
            ISceneService sceneService, SceneFlowController sceneFlowController, SceneServiceSettings settings)
        {
            _privacyConsentService = privacyConsentService;
            _attributionService = attributionService;
            _sceneFlowController = sceneFlowController;
            this._settings = settings;
            _sceneService = sceneService;
        }

        public async UniTask StartAsync(CancellationToken cancellation = default)
        {
            // remove loading screen here if you have loaded one
            // await _sceneService.RemoveScene(SceneKeys.LoadingScene);

            await _sceneService.LoadScene(_settings.DefaultSceneConfig.SceneKey);

            // show loading screen here if you have one
            //await _sceneService.LoadScene(SceneKeys.LoadingScene);

            // 1. Wait for the user to answer the ATT prompt
            await _privacyConsentService.RequestConsentAsync();

            // 2. Initialize attribution — non-blocking: if it fails the game still starts
#if UNITY_EDITOR
            // Except that it is not non-blocking, and the try/catch below cannot help: a task
            // that never completes never throws. In the editor on Windows, FB.Init starts and
            // does not come back — the SDK logs "The SDK is in an invalid state" every frame —
            // and this await sits here for the rest of the session.
            //
            // That would be tolerable if it only cost attribution. It does not: every Addressables
            // operation asked for afterwards stays at AsyncOperationStatus.None forever, so no
            // scene can be loaded again once startup reaches this line. The default scene is
            // already up by then, which is what makes it so hard to see — the menu works, every
            // screen in it works, and the only thing that cannot happen is going anywhere else.
            // A delver pressing PLAY gets nothing and no message.
            //
            // There is nothing to attribute an editor session to, so it is skipped rather than
            // worked around. A build runs it exactly as before.
            Debug.Log("[AppStartupOrchestrator] Attribution is skipped in the editor.");
#else
            try
            {
                await _attributionService.InitializeAsync();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AppStartupOrchestrator] Attribution init failed, continuing without attribution: {e.Message}");
            }
#endif
        }
    }
}
