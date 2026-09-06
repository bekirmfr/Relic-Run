using GameLift.UI.IapShop;
using GameLift.UI.LevelPath;
using GameLift.UI.MainMenu.NavigationBar;
using GameLift.UI.SettingsPopup;
using VContainer;
using VContainer.Unity;

namespace GameLift.UI.MainMenu
{
    public class MainMenuLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<NavigationBarController>();
            builder.RegisterComponentInHierarchy<IapShopUI>();

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            builder.RegisterComponentInHierarchy<Cheat.CheatPanel>();
#endif

            builder.RegisterComponentInHierarchy<Currency.CurrencyBar>();
            builder.RegisterComponentInHierarchy<SettingsButton>();
            builder.RegisterComponentInHierarchy<HomeUI>();
        }
    }
}