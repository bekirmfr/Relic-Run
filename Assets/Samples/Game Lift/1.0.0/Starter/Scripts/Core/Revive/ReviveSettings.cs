using System.Collections.Generic;
using GameLift.Currency;
using NaughtyAttributes;
using UnityEngine;

namespace GameLift.Revive
{
    [CreateAssetMenu(fileName = "ReviveSettings", menuName = "Game Lift/Revive/ReviveSettings")]
    public class ReviveSettings : ScriptableObject
    {
        [field: SerializeField, Dropdown("GetCurrencyIds")] public string CurrencyId { get; private set; }
        [field: SerializeField] public int ReviveCost { get; private set; } = 900;
        [field: SerializeField] public int ReviveCostIncrement { get; private set; } = 400;

        private List<string> GetCurrencyIds()
        {
            return CurrencyIds.GetCurrencyIds();
        }
    }
}