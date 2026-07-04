namespace Eikon.Navigation;

// Shared signals for the window chrome. Minimize collapses the app to the floating orb; Close hides
// everything (no orb) until the slash command or the installer's Open button reopens it. The main
// window's title bar and screens drawing their own header raise these; EikonBootstrap subscribes and
// performs the actual show/hide. It lives apart from MainWindow so a screen can ask without depending
// on the window (which already depends on every screen).
internal sealed class WindowController
{
    public event Action? MinimizeRequested;

    public event Action? CloseRequested;

    public void Minimize() => this.MinimizeRequested?.Invoke();

    public void Close() => this.CloseRequested?.Invoke();
}
