using System;
using GameLift.Buttons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace GameLift.UI.LevelPath
{
    public class LevelPathUINode : BaseButton
    {
        [Header("Level Path UI Node")]
        [SerializeField] private Image _background;
        [SerializeField] private TMP_Text _levelText;

        [Header("Next Level Colors")]
        [SerializeField] private Color _nextLevelFillColor;
        [SerializeField] private Color _nextLevelTextColor;

        private Action _onClick;

        private bool _isORiginalsSet = false;
        private Color _originalFillColor;
        private Color _originalTextColor;

        public void Initialize(int levelIndex, System.Action onClickAction, bool isNextLevel = false)
        {
            _levelText.text = (levelIndex + 1).ToString();
            _onClick += onClickAction;

            if (!_isORiginalsSet)
            {
                _originalFillColor = _background.color;
                _originalTextColor = _levelText.color;
                _isORiginalsSet = true;
            }

            if (isNextLevel)
            {
                _background.color = _nextLevelFillColor;
                _levelText.color = _nextLevelTextColor;
            }
            else
            {
                _background.color = _originalFillColor;
                _levelText.color = _originalTextColor;
            }
        }

        public override void BaseOnClick()
        {
            base.BaseOnClick();
            _onClick?.Invoke();
        }
    }
}