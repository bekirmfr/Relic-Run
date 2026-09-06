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
            try
            {
                await _attributionService.InitializeAsync();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AppStartupOrchestrator] Attribution init failed, continuing without attribution: {e.Message}");
            }
        }
    }
}
