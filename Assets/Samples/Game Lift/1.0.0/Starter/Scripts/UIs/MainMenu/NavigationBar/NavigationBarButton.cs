using UnityEngine;
using GameLift.Buttons;
using GameLift.Signal;
using DG.Tweening;
using TMPro;
using UnityEngine.UI;
using VContainer;

namespace GameLift.UI.MainMenu.NavigationBar
{
    public class NavigationBarButton : BaseButton
    {
        [field: SerializeField, Header("Navigation Bar Button")] public RectTransform IconContainer { get; private set; }

        [SerializeField] private Image _iconImage;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private Color _activeColor;
        [SerializeField] private Color _passiveColor;

        //[SerializeField] private GameObject _backgroundActive;
        //[SerializeField] private CanvasGroup _activeBackgroundCanvasGroup;
        //[SerializeField] private GameObject _backgroundPassive;

        public void Setup()
        {
            //_backgroundActive.SetActive(false);
            //_backgroundPassive.SetActive(true);
        }

        public virtual void OnSelected()
        {
            DOTween.Kill(this);

            //_backgroundActive.SetActive(true);
            //_backgroundPassive.SetActive(false);

            /*_activeBackgroundCanvasGroup.DOFade(1, .35f)
                .SetId(this)
                .SetLink(gameObject);*/

            _iconImage.color = _activeColor;
            _label.color = _activeColor;

            DOTween.To(() => IconContainer.offsetMin, v => IconContainer.offsetMin = v, new Vector2(IconContainer.offsetMin.x, 85), .35f)
                .SetId(this)
                .SetLink(gameObject);
            DOTween.To(() => IconContainer.offsetMax, v => IconContainer.offsetMax = v, new Vector2(IconContainer.offsetMax.x, 20), .35f)
                .SetId(this)
                .SetLink(gameObject);
        }

        public virtual void OnDeselected()
        {
            DOTween.Kill(this);

            //_backgroundPassive.SetActive(true);

            /*_activeBackgroundCanvasGroup.DOFade(0, .35f)
                .SetId(this)
                .SetLink(gameObject)
                .OnComplete(() => _backgroundActive.SetActive(false));*/

            _iconImage.color = _passiveColor;
            _label.color = _passiveColor;

            DOTween.To(() => IconContainer.offsetMin, v => IconContainer.offsetMin = v, new Vector2(IconContainer.offsetMin.x, 85), .35f)
                .SetId(this)
                .SetLink(gameObject);
            DOTween.To(() => IconContainer.offsetMax, v => IconContainer.offsetMax = v, new Vector2(IconContainer.offsetMax.x, -30), .35f)
                .SetId(this)
                .SetLink(gameObject);
        }
    }
}