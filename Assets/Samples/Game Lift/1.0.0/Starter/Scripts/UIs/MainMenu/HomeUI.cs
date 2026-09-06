using GameLift.UI.LevelPath;
using UnityEngine;
using VContainer;
namespace GameLift.UI.MainMenu
{
    public class HomeUI : MonoBehaviour
    {
        [SerializeField] private LevelPathUI _levelPathUI;
        [SerializeField] private PlayButton _playButton;
        private IObjectResolver _resolver;

        [Inject]
        private void Construct(IObjectResolver resolver)
        {
            _resolver = resolver;

            _resolver.Inject(_playButton);
            _resolver.Inject(_levelPathUI);
        }
    }
}