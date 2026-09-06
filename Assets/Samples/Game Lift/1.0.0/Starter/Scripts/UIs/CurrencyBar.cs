using System.Collections.Generic;
using System.Globalization;
using DG.Tweening;
using GameLift.Buttons;
using GameLift.Currency;
using GameLift.Popup;
using GameLift.Signal;
using GameLift.UI.MainMenu.NavigationBar;
using NaughtyAttributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace GameLift.UI.Currency
{
    public class CurrencyBar : BaseButton
    {
        [field: SerializeField, Dropdown("GetCurrencyIds")] public string CurrencyId { get; private set; }

        [SerializeField] private TMP_Text _amountText;
        [SerializeField] private Image _icon;
        
        private ISignalBus _signalBus;
        private ICurrencyService _currencyService;
        private IPopupService _popupService;

        [Inject]
        private void Construct(ISignalBus signalBus, ICurrencyService currencyService, IPopupService popupService)
        {
            _signalBus = signalBus;
            _currencyService = currencyService;
            _popupService = popupService;
        }

        private void Start()
        {
            _signalBus.Get<OnCurrencyChangedUISignal>().Subscribe(OnCurrencyChanged);

            var currencyAmount = _currencyService.GetCurrency(CurrencyId);
            var config = _currencyService.GetCurrencyConfig(CurrencyId);

            _icon.sprite = config.currencySprite;
            _amountText.text = currencyAmount.ToString("N0", CultureInfo.InvariantCulture);

            //LayoutRebuilder.ForceRebuildLayoutImmediate(_rectTransform);
        }

        private void OnDestroy()
        {
            _signalBus.Get<OnCurrencyChangedUISignal>().Unsubscribe(OnCurrencyChanged);
        }

        private void OnCurrencyChanged(string currencyId, int amount)
        {
            if (currencyId == this.CurrencyId)
            {
                UpdateAmount(amount);
            }
        }

        public void UpdateAmount(int price)
        {
            _amountText.text = price.ToString("N0", CultureInfo.InvariantCulture);
            //LayoutRebuilder.ForceRebuildLayoutImmediate(_rectTransform);
        }

        public void Jump()
        {
            string id = "currencyBar_JumpScale_" + this.GetHashCode();
            DOTween.Kill(id);

            if (_icon == null && _icon.transform == null) return;

            _icon.transform.DOScale(1.2f, 0.13f)
                .From(1)
                .SetEase(Ease.OutBack)
                .OnComplete(() =>
                {
                    if (_icon == null && _icon.transform == null) return;

                    _icon.transform.DOScale(1, 0.22f)
                        .SetEase(Ease.InOutSine)
                        .SetId(id);
                })
                .SetId(id);
        }

        public override void BaseOnClick()
        {
            base.BaseOnClick();

            var navigationBar = FindFirstObjectByType<NavigationBarController>();

            if (navigationBar != null)
            {
                navigationBar.SetActive(0); // Navigate to the first page (Store)
            }
            else
            {
                var popup = _popupService.Create<IapShop.IapShopUI>();
                _popupService.BringFront(popup);
            }
        }

        private List<string> GetCurrencyIds()
        {
            return CurrencyIds.GetCurrencyIds();
        }
    }
}