namespace Eikon.Navigation;

// A shared signal for collapsing the app to the floating orb. Both the main window's title-bar
// minimize and the chat screen's header minimize call Minimize(); EikonBootstrap subscribes and
// performs the actual hide-window / show-orb. It lives apart from MainWindow so a screen can ask to
// minimize without depending on the window (which already depends on every screen).
internal sealed class WindowController
{
    public event Action? MinimizeRequested;

    public void Minimize() => this.MinimizeRequested?.Invoke();
}
