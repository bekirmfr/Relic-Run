using Cysharp.Threading.Tasks;
using GameLift.Buttons;
using GameLift.Popup;
using GameLift.Scene;
using UnityEngine;
using VContainer;

namespace GameLift.UI.GamePopups
{
    public class GameLosePopup : PopupBase
    {
        [SerializeField] private BaseButton _restartButton;
        [SerializeField] private BaseButton _continueButton;

        private ISceneService _sceneService;

        public override string PopupId => PopupIdConst;

        public const string PopupIdConst = "game_lose_popup";

        [Inject]
        private void Construct(IObjectResolver resolver, ISceneService sceneService)
        {
            _sceneService = sceneService;

            resolver.Inject(_restartButton);
            resolver.Inject(_continueButton);

            _restartButton.Button.onClick.AddListener(OnRestartButtonClicked);
            _continueButton.Button.onClick.AddListener(OnContinueButtonClicked);
        }

        private async void OnRestartButtonClicked()
        {
            _restartButton.Button.interactable = false;
            _continueButton.Button.interactable = false;

            Disappear();
            // restart the level
            await ReloadScene();
        }

        private async UniTask ReloadScene()
        {
            await _sceneService.RemoveScene(SceneKeys.GameScene);
            await _sceneService.LoadScene(SceneKeys.GameScene);
        }

        private void OnContinueButtonClicked()
        {
            _restartButton.Button.interactable = false;
            _continueButton.Button.interactable = false;
            
            Disappear();
            // switch to menu scene
            _ = _sceneService.LoadScene(SceneKeys.MenuScene);
        }
    }
}