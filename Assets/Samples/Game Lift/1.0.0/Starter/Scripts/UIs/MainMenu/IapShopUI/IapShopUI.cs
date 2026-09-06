using System;
using GameLift.Buttons;
using GameLift.Pooling;
using GameLift.Popup;
using GameLift.Purchasing;
using GameLift.Signal;
using TMPro;
using UnityEngine;
using UnityEngine.Purchasing;
using VContainer;

namespace GameLift.UI.IapShop
{
    public class IapShopUI : PopupBase
    {
        [SerializeField] private TMP_Text _shopTitleText;
        [SerializeField] private IapShopItem _iapShopItemPrefab;
        [SerializeField] private IapShopItem _premiumIapShopItemPrefab;
        [SerializeField] private Transform _container;
        [SerializeField] private GameObject _loadingIndicator;
        [SerializeField] private BaseButton _closeButton;

        private IPurchasingService _purchasingService;
        private IPools _pools;
        private IObjectResolver _resolver;
        private ISignalBus _signalBus;
        private bool _isStarted = false;
        private bool _isItemsGenerated = false;

        public override string PopupId => PopupIdConst;

        public const string PopupIdConst = "iap_shop_ui";

        [Inject]
        private void Construct(IPurchasingService purchasingService, IPools pools, IObjectResolver resolver, ISignalBus signalBus)
        {
            _purchasingService = purchasingService;
            _pools = pools;
            _resolver = resolver;

            _signalBus = signalBus;

            _isStarted = true;

            if (_purchasingService.IsInitialized)
            {
                GenerateIapItems();
            }
            else
            {
                OnPurchasingServiceNotReady();
            }

            PostAppear += InitializePopup;
        }

        private void InitializePopup()
        {
            _closeButton.gameObject.SetActive(true);
            _closeButton.transform.SetAsLastSibling();
            _resolver.Inject(_closeButton);
            _closeButton.Button.onClick.AddListener(OnCloseButtonClicked);
        }

        private void OnCloseButtonClicked()
        {
            Disappear();
        }

        void OnEnable()
        {
            if (_isStarted)
            {
                if (_purchasingService.IsInitialized)
                {
                    GenerateIapItems();
                }
                else
                {
                    OnPurchasingServiceNotReady();
                }
            }
        }

        void OnDisable()
        {
            CancelInvoke(nameof(CheckPurchasingServiceInitialization));
            _loadingIndicator.SetActive(false);
        }

        private void GenerateIapItems()
        {
            if (_isItemsGenerated) return;

            _isItemsGenerated = true;

            var products = _purchasingService.GetAllProducts();
            var configs = _purchasingService.GetAllProductConfigs();

            foreach (var product in products)
            {
                var config = configs.Find(c => c.ProductId == product.definition.id);
                if (config == null) continue;
                CreateIapItem(product, config);
            }
        }

        private void CreateIapItem(Product product, ProductConfig config)
        {
            var item = _pools.Spawn(config.ShopItemType == ShopItemType.Premium ? _premiumIapShopItemPrefab : _iapShopItemPrefab, _container);
            _resolver.Inject(item);
            item.Initialize(product, config);
        }

        private void OnPurchasingServiceNotReady()
        {
            _loadingIndicator.SetActive(true);
            // check if purchasing service is initialized every second until it is initialized, then generate iap items
            InvokeRepeating(nameof(CheckPurchasingServiceInitialization), 0f, 1f);
        }

        private void CheckPurchasingServiceInitialization()
        {
            if (_purchasingService.IsInitialized)
            {
                CancelInvoke(nameof(CheckPurchasingServiceInitialization));
                _loadingIndicator.SetActive(false);
                GenerateIapItems();
            }
        }
    }
}