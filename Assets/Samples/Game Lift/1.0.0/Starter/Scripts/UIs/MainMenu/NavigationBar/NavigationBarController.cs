using System.Collections.Generic;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace GameLift.UI.MainMenu.NavigationBar
{
    public class NavigationBarController : MonoBehaviour
    {
        [SerializeField] private List<NavigationBarButton> _buttons;
        [SerializeField] private List<GameObject> _pages;
        [SerializeField] private int _startIndex = 1;

        private int _activeIndex = -1;

        [Inject]
        private void Construct(IObjectResolver resolver)
        {
            InitializeButtons();

            // disable all pages at the start
            _pages.ForEach(page => page.SetActive(false));
            
            SetActive(_startIndex); // Default first active

            foreach (var button in _buttons)
            {
                resolver.InjectGameObject(button.gameObject);
            }
        }

        private void InitializeButtons()
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                int index = i;
                _buttons[i].Button.onClick.AddListener(() => OnButtonPressed(index));
                _buttons[i].Setup();
            }
        }

        private void OnButtonPressed(int index)
        {
            if (index == _activeIndex) return;

            SetActive(index);
        }

        public void SetActive(int index)
        {
            if (_activeIndex >= 0)
            {
                // Reset previous
                _buttons[_activeIndex].OnDeselected();
                _pages[_activeIndex].SetActive(false);
            }

            _activeIndex = index;

            // Set new active
            _buttons[_activeIndex].OnSelected();
            _pages[_activeIndex].SetActive(true);
        }
    }
}