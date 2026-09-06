using GameLift.Ads;
using GameLift.Buttons;
using GameLift.Currency;
using GameLift.Popup;
using GameLift.Revive;
using GameLift.Scene;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace GameLift.UI.GamePopups
{
    public class GameRevivePopup : PopupBase
    {
        [SerializeField] private BaseButton _watchAdButton;

        [Header("Pay With Coin")]
        [SerializeField] private BaseButton _payWithCoinButton;
        [SerializeField] private TextMeshProUGUI _payWithCoinButtonText;
        [SerializeField] private Image _payWithCoinButtonIcon;
        [SerializeField] private BaseButton _giveUpButton;

        private ICurrencyService _currencyService;
        private AdsService _adsService;
        private ReviveController _reviveController;
        private ISceneService _sceneService;
        private IPopupService _popupService;
        private bool _isRevived;

        private const float ReviveExtraTime = 21f;

        public override string PopupId => PopupIdConst;
        public const string PopupIdConst = "game_revive_popup";

        public bool IsRevived => _isRevived;

        [Inject]
        private void Construct(ICurrencyService currencyService, AdsService adsService, ReviveController reviveController, ISceneService sceneService,
           IPopupService popupService, IObjectResolver resolver)
        {
            _currencyService = currencyService;
            _adsService = adsService;
            _reviveController = reviveController;
            _sceneService = sceneService;
            _popupService = popupService;
            _isRevived = false;

            CurrencyConfig currency = _currencyService.GetCurrencyConfig(_reviveController.GetCurrencyId());

            if (currency != null)
            {
                _payWithCoinButtonText.text = _reviveController.GetCurrentReviveCost().ToString();
                _payWithCoinButtonIcon.sprite = currency.currencySprite;
            }
            else
            {
                _payWithCoinButton.Button.interactable = false;
            }

            resolver.Inject(_payWithCoinButton);
            resolver.Inject(_watchAdButton);
            resolver.Inject(_giveUpButton);

            _payWithCoinButton.Button.onClick.AddListener(OnPayWithCoinButtonClicked);
            _watchAdButton.Button.onClick.AddListener(OnWatchAdButtonClicked);

            _giveUpButton.Button.onClick.AddListener(() =>
            {
                Disappear();
                _sceneService.LoadScene(SceneKeys.MenuScene);
            });
        }

        private void OnPayWithCoinButtonClicked()
        {
            // pay with coins to revive
            int reviveCost = _reviveController.GetCurrentReviveCost();
            if (_currencyService.TryPurchase(CurrencyIds.Gold, reviveCost))
            {
                _reviveController.IncrementPayedReviveCount();

                Revive();
            }
            else if (_popupService.Get<IapShop.IapShopUI>() == null)
            {
                var popup = _popupService.Create<IapShop.IapShopUI>(true);
                _popupService.BringFront(popup);
            }
        }

        private void OnWatchAdButtonClicked()
        {
            // watch an ad to revive
            if (_adsService.RewardedAds.CanShowRewardedAd())
            {
                _watchAdButton.Button.interactable = false;

                _adsService.RewardedAds.ShowRewardedAd(
                    onUserEarnedReward: () =>
                    {
                        Revive();
                    },
                    onAdClosed: () =>
                    {
                        _watchAdButton.Button.interactable = true;

                        // player failed to watch ad
                        Debug.Log("Ad was closed before completion. Player did not earn revive.");
                    });
            }
        }

        private void Revive()
        {
            _payWithCoinButton.Button.interactable = false;
            _watchAdButton.Button.interactable = false;

            _isRevived = true;
            Disappear();

            // add extra time to the timer or add health here
        }
    }
}