namespace Eikon.Navigation;

// Thread safe holder for the current screen. Screens navigate through this rather than calling
// each other, so the draw loop reads one value per frame and renders the matching screen.
internal sealed class ScreenRouter
{
    private readonly object gate = new();
    private Screen current;

    public ScreenRouter(Screen initial) => this.current = initial;

    public Screen Current
    {
        get
        {
            lock (this.gate)
                return this.current;
        }
    }

    public void Navigate(Screen screen)
    {
        lock (this.gate)
            this.current = screen;
    }
}
