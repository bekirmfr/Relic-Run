using System;
using GameLift.Levels;
using GameLift.Purchasing;
using GameLift.Signal;
using UnityEngine;
using VContainer.Unity;

namespace GameLift.Analytics
{
    public class AnalyticsService : IStartable, IDisposable
    {
        private readonly ISignalBus _signalBus;

        public AnalyticsService(ISignalBus signalBus)
        {
            _signalBus = signalBus;
        }

        public void Start()
        {
            _signalBus.Get<OnLevelStartedSignal>().Subscribe(OnLevelStarted);
            _signalBus.Get<OnLevelCompletedSignal>().Subscribe(OnLevelCompleted);
            _signalBus.Get<OnPurchaseCompletedSignal>().Subscribe(OnPurchaseCompleted);

        }

        public void Dispose()
        {
            _signalBus.Get<OnLevelStartedSignal>().Unsubscribe(OnLevelStarted);
            _signalBus.Get<OnLevelCompletedSignal>().Unsubscribe(OnLevelCompleted);
            _signalBus.Get<OnPurchaseCompletedSignal>().Unsubscribe(OnPurchaseCompleted);
        }

        private void OnLevelStarted(int level)
        {
            /*Parameter[] parameters = {
                new Parameter("level_index", level)
            };
            FirebaseAnalytics.LogEvent("level_start", parameters);*/

            Debug.Log($"Logged level_start event for level {level}");
        }

        private void OnLevelCompleted(int level, bool success)
        {
            /*Parameter[] parameters = {
                new Parameter("level_index", level),
                new Parameter("success", success ? 1 : 0) // Firebase doesn't use bool, use int or string
            };
            FirebaseAnalytics.LogEvent("level_end", parameters);*/

            Debug.Log($"Logged level_end event for level {level} with success: {success}");
        }

        private void OnPurchaseCompleted(UnityEngine.Purchasing.Product product)
        {
            if (product != null)
            {
                /*Parameter[] parameters = {
                    new Parameter("product_id", product.definition.id),
                    new Parameter("price", (float)product.metadata.localizedPrice),
                    new Parameter("currency", product.metadata.isoCurrencyCode)
                };
                FirebaseAnalytics.LogEvent("purchase_completed", parameters);*/
            }
        }
    }
}
