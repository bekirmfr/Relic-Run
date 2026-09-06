using System.Linq;
using Cysharp.Threading.Tasks;
using GameLift.Buttons;
using GameLift.Currency;
using GameLift.ObjectFlowAnimator;
using GameLift.Purchasing;
using GameLift.Signal;
using GameLift.UI.Currency;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace GameLift.UI.IapShop
{
    public class IapShopItem : MonoBehaviour
    {
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private Image _icon;
        [SerializeField] private BaseButton _purchaseButton;
        [SerializeField] private TMP_Text _priceText;
        [SerializeField] private GameObject _badgeContainer;
        [SerializeField] private Image _badgeBackground;
        [SerializeField] private TMP_Text _badgeText;
        //[SerializeField] private Outline _itemOutline;

        private const string IapShopFlyGoldKey = "iap_shop_fly_gold";

        private UnityEngine.Purchasing.Product _product;
        private ProductConfig _config;
        private ISignalBus _signalBus;
        private ICurrencyService _currencyService;
        private IUIFlowAnimator _uiFlowAnimator;
        private IPurchasingService _purchasingService;

        [Inject]
        private void Construct(IPurchasingService purchasingService, ISignalBus signalBus, ICurrencyService currencyService,
            IUIFlowAnimator uIFlowAnimator, IObjectResolver resolver)
        {
            _purchasingService = purchasingService;
            _signalBus = signalBus;
            _currencyService = currencyService;
            _uiFlowAnimator = uIFlowAnimator;
            resolver.Inject(_purchaseButton);

            _signalBus.Get<OnPurchaseCompletedSignal>().Subscribe(OnPurchaseCompleted);
        }

        void OnDestroy()
        {
            _signalBus?.Get<OnPurchaseCompletedSignal>().Unsubscribe(OnPurchaseCompleted);
        }

        private void OnPurchaseCompleted(UnityEngine.Purchasing.Product product)
        {
            if (product != null)
            {
                if (product.definition.id == _product.definition.id)
                {
                    int amount = int.Parse(product.definition.id.Split('_')[1]);

                    FlyFakeCurrency(amount).Forget();
                }
            }
        }

        public void Initialize(UnityEngine.Purchasing.Product product, ProductConfig config)
        {
            _product = product;
            _config = config;

            _titleText.text = config.DisplayName;
            _priceText.text = $"{product.metadata.isoCurrencyCode} {product.metadata.localizedPrice}";
            _icon.sprite = config.Icon;

            var hasBadge = config.Badge != null && !string.IsNullOrEmpty(config.Badge.Text);
            _badgeContainer.SetActive(hasBadge);
            if (hasBadge)
            {
                _badgeText.text = config.Badge.Text;
                _badgeText.color = config.Badge.TextColor;
                _badgeBackground.color = config.Badge.BackgroundColor;
            }

            /*if (hasBadge)
            {
                _itemOutline.effectColor = config.Badge.BackgroundColor;
                _itemOutline.enabled = true;
            }
            else
            {
                _itemOutline.enabled = false;
            }*/

            _purchaseButton.Button.onClick.AddListener(OnPurchaseButtonClicked);
        }

        private void OnPurchaseButtonClicked()
        {
            _purchasingService.PurchaseProduct(_product.definition.id);
        }

        private async UniTask FlyFakeCurrency(int gold)
        {
            await UniTask.Delay(300); // Simulate delay for purchase processing

            var currencyBars = FindObjectsByType<CurrencyBar>(FindObjectsSortMode.None);
            var currencyBar = currencyBars.FirstOrDefault(s => s.isActiveAndEnabled == true && s.CurrencyId == CurrencyIds.Gold);

            float amountPerParticleCount = gold > 5000 ? 100 : 20;

            float particleReceiveCount = gold / amountPerParticleCount;

            var currencyConfig = _currencyService.GetCurrencyConfig(CurrencyIds.Gold);

            _uiFlowAnimator.AddNewDestinationAction(
                IapShopFlyGoldKey,
                startScreenPos: _purchaseButton.transform.position,
                endScreenPos: currencyBar.transform.position,
                sprite: currencyConfig.currencySprite,
                particleCount: (int)particleReceiveCount,
                onReceivedItem: () =>
                {
                    _currencyService.AddFakeCurrency(currencyConfig.currencyId, amountPerParticleCount);
                    if (currencyBar != null)
                        currencyBar.Jump();
                },
                onCompleted: () =>
                {
                    _currencyService.ClearFakeCurreny(CurrencyIds.Gold);
                });
        }
    }
}