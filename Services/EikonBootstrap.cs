using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Eikon.Crypto;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.Notifications;
using Eikon.Windows;

namespace Eikon.Services;

// Owns the runtime wiring: the slash command and the UiBuilder hooks. The container creates this
// after its dependencies, and disposes it first when the provider is torn down, so handlers are
// removed before the windows themselves are disposed.
internal sealed class EikonBootstrap : IDisposable
{
    private const string CommandName = BuildInfo.Command;

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly WindowSystem windowSystem;
    private readonly MainWindow mainWindow;
    private readonly OrbWindow orbWindow;
    private readonly NotificationWindow notificationWindow;
    private readonly NotificationService notifications;
    private readonly KeyVault keyVault;
    private readonly ScreenRouter router;
    private readonly Selection selection;
    private readonly Media media;
    private readonly WindowController windowController;

    public EikonBootstrap(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IPluginLog log,
        WindowSystem windowSystem,
        MainWindow mainWindow,
        OrbWindow orbWindow,
        NotificationWindow notificationWindow,
        NotificationService notifications,
        KeyVault keyVault,
        ScreenRouter router,
        Selection selection,
        Media media,
        WindowController windowController)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.windowSystem = windowSystem;
        this.mainWindow = mainWindow;
        this.orbWindow = orbWindow;
        this.notificationWindow = notificationWindow;
        this.notifications = notifications;
        this.keyVault = keyVault;
        this.router = router;
        this.selection = selection;
        this.media = media;
        this.windowController = windowController;

        this.windowSystem.AddWindow(this.mainWindow);
        this.windowSystem.AddWindow(this.orbWindow);
        this.windowSystem.AddWindow(this.notificationWindow);

        // Minimize collapses the app to the orb; tapping the orb restores it. Close (title-bar X or
        // right-clicking the orb) hides everything until the slash command or the installer's Open
        // button reopens it. The signals are shared so the main title bar, the chat header, and the
        // orb all raise them through the same path.
        this.windowController.MinimizeRequested += this.Minimize;
        this.windowController.CloseRequested += this.Close;
        this.orbWindow.RestoreRequested += this.Restore;
        this.orbWindow.CloseRequested += this.Close;
        this.notifications.OpenRequested += this.OpenNotification;

        this.commandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open Eikon.",
        });

        this.pluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        this.pluginInterface.UiBuilder.Draw += this.media.Draw;
        this.pluginInterface.UiBuilder.OpenMainUi += this.OpenMain;

        log.Information("Eikon bootstrapped.");
    }

    public void Dispose()
    {
        this.windowController.MinimizeRequested -= this.Minimize;
        this.windowController.CloseRequested -= this.Close;
        this.orbWindow.RestoreRequested -= this.Restore;
        this.orbWindow.CloseRequested -= this.Close;
        this.notifications.OpenRequested -= this.OpenNotification;
        this.pluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        this.pluginInterface.UiBuilder.Draw -= this.media.Draw;
        this.pluginInterface.UiBuilder.OpenMainUi -= this.OpenMain;
        this.commandManager.RemoveHandler(CommandName);
        this.windowSystem.RemoveAllWindows();
    }

    // The slash command toggles between the app and the minimized orb.
    private void OnCommand(string command, string arguments)
    {
        if (this.mainWindow.IsOpen)
            this.Minimize();
        else
            this.Restore();
    }

    private void OpenMain() => this.Restore();

    private void Minimize()
    {
        // With "require passphrase on this PC" on, minimizing locks the vault so reopening prompts for
        // it again (like locking your phone). With auto-unlock on, it just hides.
        if (!this.keyVault.AutoUnlockEnabled && this.keyVault.IsUnlocked)
            this.keyVault.Lock();

        this.mainWindow.IsOpen = false;
        this.orbWindow.IsOpen = true;
    }

    // Fully close: no window, no orb. Locks the vault under the same rule as minimize. The session
    // persists and the relay stays connected, so message toasts still arrive (tapping one restores);
    // closing is hiding the app, not logging out.
    private void Close()
    {
        if (!this.keyVault.AutoUnlockEnabled && this.keyVault.IsUnlocked)
            this.keyVault.Lock();

        this.mainWindow.IsOpen = false;
        this.orbWindow.IsOpen = false;
    }

    private void Restore()
    {
        this.orbWindow.IsOpen = false;
        this.mainWindow.IsOpen = true;

        // A locked vault with an existing identity means the passphrase is required before the app is
        // usable (set on minimize, or a fresh re-login).
        if (this.keyVault.HasIdentity && !this.keyVault.IsUnlocked)
            this.router.Navigate(Screen.Unlock);
    }

    // Tapping a notification restores the app and opens the toast's target (passphrase first if locked:
    // Restore routes to Unlock and we don't override it).
    private void OpenNotification(NotificationToast toast)
    {
        this.Restore();
        if (!this.keyVault.IsUnlocked)
            return;

        switch (toast.Kind)
        {
            case ToastKind.AlbumRequest:
                this.router.Navigate(Screen.AlbumRequests);
                break;
            case ToastKind.AlbumApproved:
                this.selection.ProfileUserId = toast.Peer;
                this.selection.ProfileDisplayName = toast.Name;
                this.selection.AlbumId = toast.AlbumId;
                this.selection.AlbumName = toast.AlbumName ?? string.Empty;
                this.selection.AlbumReturn = Screen.ProfileDetail;
                this.router.Navigate(Screen.AlbumViewer);
                break;
            default:
                this.selection.ProfileUserId = toast.Peer;
                this.selection.ProfileDisplayName = toast.Name;
                this.router.Navigate(Screen.Chat);
                break;
        }
    }
}
