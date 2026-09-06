using GameLift.Ads;
using GameLift.AppStartup;
using GameLift.Attribution;
using GameLift.Audio;
using GameLift.Buttons;
using GameLift.Currency;
using GameLift.Feedbacks;
using GameLift.Levels;
using GameLift.ObjectFlowAnimator;
using GameLift.Pooling;
using GameLift.Popup;
using GameLift.PrivacyConsent;
using GameLift.Purchasing;
using GameLift.Revive;
using GameLift.Save;
using GameLift.Scene;
using GameLift.Settings;
using GameLift.Signal;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace GameLift.Installer
{
    public class GameLiftLifetimeScope : LifetimeScope
    {
        [Header("GameLift Settings")]
        [SerializeField] private LevelList _levelList;
        [SerializeField] private PurchasingSettings _purchasingSettings;
        [SerializeField] private AdsSettings _adsSettings;
        [SerializeField] private AudioServiceSettings _audioSettings;
        [SerializeField] private FeedbackServiceSettings _feedbackSettings;
        [SerializeField] private CurrencyServiceSettings _currencySettings;
        [SerializeField] private SceneServiceSettings _sceneServiceSettings;
        [Header("Popup Settings")]
        [SerializeField] private PopupSettings _popupSettings;
        [SerializeField] private Canvas _popupCanvas;
        [Header("UIFlowAnimator")]
        [SerializeField] private UIFlowAnimatorSettings _uiFlowAnimatorSettings;
        [SerializeField] private RectTransform _flowCanvas;

        [Header("Revive Settings")]
        [SerializeField] private ReviveSettings _reviveSettings;

        protected override void Configure(IContainerBuilder builder)
        {

            builder.Register<ISignalBus, SignalBus>(Lifetime.Singleton);

            builder.RegisterInstance(_sceneServiceSettings);
            builder.Register<ISceneService, SceneService>(Lifetime.Singleton);

            builder.Register<ISaveHandler, EncryptedSaveHandler>(Lifetime.Singleton);
            builder.Register<ISaveService, SaveService>(Lifetime.Singleton);

            builder.Register<IPools, Pools>(Lifetime.Singleton);

            builder.RegisterInstance(_popupSettings);
            builder.Register<IPopupService, PopupService>(Lifetime.Singleton).WithParameter(_popupCanvas);

            builder.Register<ButtonManager>(Lifetime.Singleton);

            builder.RegisterInstance(_levelList);
            builder.Register<LevelService<BaseLevelData>>(Lifetime.Singleton);

            builder.RegisterInstance(_purchasingSettings);
            builder.RegisterEntryPoint<PurchasingService>(Lifetime.Singleton).As<IPurchasingService>();

            builder.RegisterInstance(_adsSettings);
            builder.RegisterEntryPoint<AdsService>(Lifetime.Singleton).AsSelf();

            builder.RegisterInstance(_audioSettings);
            builder.RegisterEntryPoint<AudioService>(Lifetime.Singleton).As<IAudioService>();

            builder.RegisterInstance(_feedbackSettings);
            builder.RegisterEntryPoint<FeedbackService>(Lifetime.Singleton).As<IFeedbackService>();

            builder.RegisterEntryPoint<GameSettingsService>(Lifetime.Singleton).AsSelf();

            builder.RegisterInstance(_currencySettings);
            builder.Register<CurrencyService>(Lifetime.Singleton).As<ICurrencyService>();

            builder.RegisterInstance(_uiFlowAnimatorSettings);
            builder.RegisterEntryPoint<UIFlowAnimator>(Lifetime.Singleton).As<IUIFlowAnimator>().WithParameter(_flowCanvas);

            builder.RegisterInstance(_reviveSettings);
            builder.Register<ReviveController>(Lifetime.Singleton);

            builder.RegisterEntryPoint<PurchaseHandler>(Lifetime.Singleton);
            
            builder.Register<IAttributionService, AttributionService>(Lifetime.Singleton);

            builder.Register<IPrivacyConsentService, PrivacyConsentService>(Lifetime.Singleton);

            builder.Register<SceneFlowController>(Lifetime.Singleton);

            builder.RegisterEntryPoint<AppStartupOrchestrator>(Lifetime.Singleton);
        }
    }
}
