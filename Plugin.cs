using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using Microsoft.Extensions.DependencyInjection;
using Eikon.Config;
using Eikon.Crypto;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.Notifications;
using Eikon.Screens;
using Eikon.Services;
using Eikon.UI;
using Eikon.UI.Theme;
using Eikon.Windows;

namespace Eikon;

// Entry point. Builds a dependency injection container, then resolves the bootstrap which wires
// the command and the windows. Services and screens are registered as singletons at startup.
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    private readonly ServiceProvider provider;

    public Plugin()
    {
        var config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // One-time move to production (Version 1 -> 2). Earlier builds defaulted to a local dev server
        // and that value persists, so an install stays stuck on loopback. Reset any loopback URL to
        // api.eikon.chat once, then leave the setting alone so a self-hoster can point it elsewhere and
        // have it stick. Release only: a local (EikonLocal) build is meant to talk to loopback, so it
        // keeps its own config untouched by this.
#if !DEBUG
        if (config.Version < 2)
        {
            if (Uri.TryCreate(config.ServerBaseUrl, UriKind.Absolute, out var configured) && configured.IsLoopback)
                config.ServerBaseUrl = "https://api.eikon.chat";
            config.Version = 2;
            config.Save();
        }
#endif

        var services = new ServiceCollection();

        // Dalamud services. These interfaces are not IDisposable, so the container will not try
        // to dispose objects that Dalamud owns.
        services.AddSingleton(PluginInterface);
        services.AddSingleton(CommandManager);
        services.AddSingleton(Log);

        // Config, theme, fonts, and the widget kit.
        services.AddSingleton(config);
        services.AddSingleton<ThemeService>();
        services.AddSingleton<UiFonts>();
        services.AddSingleton<Kit>();
        services.AddSingleton<PhotoManager>();
        services.AddSingleton<KeyVault>();
        services.AddSingleton<IApiClient, ApiClient>();
        services.AddSingleton<SessionStore>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<WorldCatalog>();
        services.AddSingleton<ProfileService>();
        services.AddSingleton<DiscoveryService>();
        services.AddSingleton<ProfileDetailService>();
        services.AddSingleton<Selection>();
        services.AddSingleton<IdentityService>();
        services.AddSingleton<MessageCrypto>();
        services.AddSingleton<RelayClient>();
        services.AddSingleton<ChatMediaCache>();
        services.AddSingleton<ChatService>();
        services.AddSingleton<InboxService>();
        services.AddSingleton<FavoritesService>();
        services.AddSingleton<BlockedService>();
        services.AddSingleton<PhotoService>();
        services.AddSingleton<AlbumService>();
        services.AddSingleton<SafetyService>();
        services.AddSingleton<ModerationKeyService>();
        services.AddSingleton<Media>();
        services.AddSingleton<Lightbox>();
        services.AddSingleton<ModerationFlow>();
        services.AddSingleton<DeleteAccountFlow>();

        // UI infrastructure. The app opens on the invite gate.
        services.AddSingleton(new ScreenRouter(Screen.AgeGuidelines));
        services.AddSingleton<WindowController>();
        services.AddSingleton(new WindowSystem(BuildInfo.WindowNamespace));

        // Screens. Registered as IScreen so the window can route to them.
        services.AddSingleton<IScreen, AgeGuidelinesScreen>();
        services.AddSingleton<IScreen, OnboardingScreen>();
        services.AddSingleton<IScreen, UnlockScreen>();
        services.AddSingleton<IScreen, RestoreAccountScreen>();
        services.AddSingleton<IScreen, GridScreen>();
        services.AddSingleton<IScreen, FilterScreen>();
        services.AddSingleton<IScreen, ProfileDetailScreen>();
        services.AddSingleton<IScreen, MessagesScreen>();
        services.AddSingleton<IScreen, ChatScreen>();
        services.AddSingleton<IScreen, SharedMediaScreen>();
        services.AddSingleton<IScreen, MyProfileScreen>();
        services.AddSingleton<IScreen, SettingsScreen>();
        services.AddSingleton<IScreen, FavoritesScreen>();
        services.AddSingleton<IScreen, GuidelinesScreen>();
        services.AddSingleton<IScreen, WhatsNewScreen>();
        services.AddSingleton<IScreen, BlockedUsersScreen>();
        services.AddSingleton<IScreen, AlbumsScreen>();
        services.AddSingleton<IScreen, AlbumDetailScreen>();
        services.AddSingleton<IScreen, AlbumRequestsScreen>();
        services.AddSingleton<IScreen, AlbumAccessScreen>();
        services.AddSingleton<IScreen, AlbumViewerScreen>();

        services.AddSingleton<SoundService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<OrbWindow>();
        services.AddSingleton<NotificationWindow>();

        // Runtime wiring. Resolving it registers the command and UI hooks; disposing the provider
        // tears everything down in reverse order.
        services.AddSingleton<EikonBootstrap>();

        this.provider = services.BuildServiceProvider();
        this.provider.GetRequiredService<EikonBootstrap>();
    }

    public void Dispose() => this.provider.Dispose();
}
