using GameLift.Buttons;
using UnityEngine;
using UnityEngine.EventSystems;
using GameLift.Signals;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
using GameLift.Signal;
using VContainer;
#endif

namespace GameLift.UI.SettingsPopup
{
    public class SettingsButton : OpenPopupButton, IPointerDownHandler, IPointerUpHandler
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private const float HoldThreshold = 1.5f;

        private ISignalBus _signalBus;
        private float _holdTimer;
        private bool _isPointerDown;
        private bool _longPressTriggered;
        private bool _showDebugMenu;

        [Inject]
        private void Construct(ISignalBus signalBus)
        {
            _signalBus = signalBus;
        }

        public override void BaseOnClick()
        {
            if (_longPressTriggered)
            {
                _longPressTriggered = false;
                return;
            }

            base.BaseOnClick();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _isPointerDown = true;
            _holdTimer = 0f;
            _longPressTriggered = false;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _isPointerDown = false;
        }

        private void Update()
        {
            if (!_isPointerDown) return;

            _holdTimer += Time.deltaTime;
            if (_holdTimer >= HoldThreshold)
            {
                _isPointerDown = false;
                _longPressTriggered = true;
                _showDebugMenu = true;
            }
        }

        private void OnGUI()
        {
            if (!_showDebugMenu) return;

            float w = 300, h = 200;
            float x = (Screen.width - w) / 2f;
            float y = (Screen.height - h) / 2f;

            GUILayout.BeginArea(new Rect(x, y, w, h), GUI.skin.box);
            GUILayout.Label("Debug Menu", new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(10);

            if (GUILayout.Button("Win Level", GUILayout.Height(40)))
            {
                _signalBus.Get<ForceEndGameSignal>().Invoke(true);
                _showDebugMenu = false;
            }

            if (GUILayout.Button("Fail Level", GUILayout.Height(40)))
            {
                _signalBus.Get<ForceEndGameSignal>().Invoke(false);
                _showDebugMenu = false;
            }

            GUILayout.Space(10);
            if (GUILayout.Button("Close", GUILayout.Height(30)))
            {
                _showDebugMenu = false;
            }

            GUILayout.EndArea();
        }
#else
        public void OnPointerDown(PointerEventData eventData) { }
        public void OnPointerUp(PointerEventData eventData) { }
#endif
    }
}