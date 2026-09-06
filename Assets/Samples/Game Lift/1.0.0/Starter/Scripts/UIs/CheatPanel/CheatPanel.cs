#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using GameLift.Ads;
using GameLift.Feedbacks;
using GameLift.Popup;
using GameLift.UI.GamePopups;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace GameLift.UI.Cheat
{
    public class CheatPanel : MonoBehaviour
    {
        [SerializeField] private Button _toggleButton;
        [SerializeField] private GameObject _panelContent;
        [SerializeField] private TMP_Text _logText;

        [Header("Ad Buttons")]
        [SerializeField] private Button _showRewardedButton;
        [SerializeField] private Button _showInterstitialButton;
        [SerializeField] private Button _loadBannerButton;
        [SerializeField] private Button _hideBannerButton;

        [Header("Haptics")]
        [SerializeField] private Transform _hapticsButtonContainer;
        [SerializeField] private Button _hapticButtonPrefab;

        [Header("Game Result Popups")]
        [SerializeField] private Button _showWinPopupButton;
        [SerializeField] private Button _showLosePopupButton;
        [SerializeField] private Button _showRevivePopupButton;


        private AdsService _adsService;
        private IFeedbackService _feedbackService;
        private IPopupService _popupService;

        private readonly List<Button> _hapticButtons = new();

        [Inject]
        private void Construct(AdsService adsService, IFeedbackService feedbackService, IPopupService popupService)
        {
            _adsService = adsService;
            _feedbackService = feedbackService;
            _popupService = popupService;
        }

        private void Start()
        {
#if !(DEVELOPMENT_BUILD || UNITY_EDITOR)
            // Hide the cheat panel in non-development builds
            GameObject.SetActive(false);
            return;
#endif

            _panelContent.SetActive(false);

            _toggleButton.onClick.AddListener(TogglePanel);
            _showRewardedButton.onClick.AddListener(ShowRewarded);
            _showInterstitialButton.onClick.AddListener(ShowInterstitial);
            _loadBannerButton.onClick.AddListener(LoadBanner);
            _hideBannerButton.onClick.AddListener(HideBanner);

            _showWinPopupButton.onClick.AddListener(() => ShowPopup(GameWinPopup.PopupIdConst));
            _showLosePopupButton.onClick.AddListener(() => ShowPopup(GameLosePopup.PopupIdConst));
            _showRevivePopupButton.onClick.AddListener(() => ShowPopup(GameRevivePopup.PopupIdConst));

            CreateHapticButtons();
        }

        private void ShowPopup(string id)
        {
            _popupService.Create(id);
        }

        private void CreateHapticButtons()
        {
            foreach (HapticType hapticType in Enum.GetValues(typeof(HapticType)))
            {
                var button = Instantiate(_hapticButtonPrefab, _hapticsButtonContainer);
                var label = button.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.text = hapticType.ToString();
                }

                var type = hapticType;
                button.onClick.AddListener(() =>
                {
                    _feedbackService.PlayHaptic(type);
                    Log($"Haptic: {type}");
                });

                _hapticButtons.Add(button);
            }
        }

        private void OnDestroy()
        {
            _toggleButton.onClick.RemoveListener(TogglePanel);
            _showRewardedButton.onClick.RemoveListener(ShowRewarded);
            _showInterstitialButton.onClick.RemoveListener(ShowInterstitial);
            _loadBannerButton.onClick.RemoveListener(LoadBanner);
            _hideBannerButton.onClick.RemoveListener(HideBanner);

            foreach (var button in _hapticButtons)
            {
                if (button != null)
                    button.onClick.RemoveAllListeners();
            }
        }

        private void TogglePanel()
        {
            _panelContent.SetActive(!_panelContent.activeSelf);
        }

        private void ShowRewarded()
        {
            if (_adsService.RewardedAds.CanShowRewardedAd())
            {
                _adsService.RewardedAds.ShowRewardedAd(
                    onUserEarnedReward: () => Log("Rewarded: User earned reward"),
                    onAdClosed: () => Log("Rewarded: Ad closed")
                );
                Log("Rewarded: Showing ad...");
            }
            else
            {
                Log("Rewarded: Ad not ready");
            }
        }

        private void ShowInterstitial()
        {
            if (_adsService.InterstitialAds.CanShowInterstitialAd())
            {
                _adsService.InterstitialAds.ShowInterstitialAd(
                    onAdClose: () => Log("Interstitial: Ad closed")
                );
                Log("Interstitial: Showing ad...");
            }
            else
            {
                Log("Interstitial: Ad not ready");
            }
        }

        private void LoadBanner()
        {
            _adsService.BannerAds.LoadBannerAd();
            Log("Banner: Loading...");
        }

        private void HideBanner()
        {
            _adsService.BannerAds.HideBannerAd();
            Log("Banner: Hidden");
        }

        private void Log(string message)
        {
            Debug.Log($"[CheatPanel] {message}");
            if (_logText != null)
            {
                _logText.text = message;
            }
        }
    }
}
#endif
