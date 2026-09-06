using GameLift.Currency;
using TMPro;
using UnityEngine;

namespace GameLift.Revive
{
    public class ReviveController
    {
        private readonly ReviveSettings _settings;

        public int PayedReviveCount { get; private set; }

        public ReviveController(ReviveSettings settings)
        {
            _settings = settings;
        }

        public void IncrementPayedReviveCount()
        {
            PayedReviveCount++;
        }

        public int GetCurrentReviveCost()
        {
            return _settings.ReviveCost + (PayedReviveCount * _settings.ReviveCostIncrement);
        }

        public void ResetPayedReviveCount()
        {
            PayedReviveCount = 0;
        }

        public string GetCurrencyId()
        {
            return _settings.CurrencyId;
        }
    }
}