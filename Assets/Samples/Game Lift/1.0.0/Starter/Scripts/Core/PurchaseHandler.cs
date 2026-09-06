using System;
using GameLift.Ads;
using GameLift.Attribution;
using GameLift.Currency;
using GameLift.Purchasing;
using GameLift.Signal;
using UnityEngine;
using UnityEngine.Purchasing;
using VContainer.Unity;

namespace GameLift.Purchasing
{
    public class PurchaseHandler : IStartable, IDisposable
    {
        private readonly ISignalBus _signalBus;
        private readonly AdsService _adsService;
        private readonly ICurrencyService _currencyService;
        private readonly IAttributionService _attributionService;

        public PurchaseHandler(ISignalBus signalBus, AdsService adsService, ICurrencyService currencyService, IAttributionService attributionService)
        {
            _signalBus = signalBus;
            _adsService = adsService;
            _currencyService = currencyService;
            _attributionService = attributionService;
        }

        public void Start()
        {
            _signalBus.Get<OnPurchaseCompletedSignal>().Subscribe(OnPurchaseCompleted);
        }

        public void Dispose()
        {
            _signalBus.Get<OnPurchaseCompletedSignal>().Unsubscribe(OnPurchaseCompleted);
        }

        private void OnPurchaseCompleted(Product product)
        {
            if (product != null)
            {
                if (product.definition.id.StartsWith("remove_ads"))
                {
                    _adsService.NoAdsBought();
                }
                else if (product.definition.id.StartsWith("coin"))
                {
                    int amount = int.Parse(product.definition.id.Split('_')[1]);

                    _currencyService.ModifyCurrency(CurrencyIds.Gold, amount, addFakeDecrease: true);
                }

                try
                {
                    _attributionService.LogPurchase((float)product.metadata.localizedPrice, product.metadata.isoCurrencyCode);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[PurchaseHandler] Failed to log purchase to attribution service: {e.Message}");
                }
            }
        }
    }
}