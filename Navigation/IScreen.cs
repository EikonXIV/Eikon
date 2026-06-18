namespace Eikon.Navigation;

// A routable screen. The window looks screens up by Id and draws the current one. Chrome screens
// render inside the header and bottom nav shell; non chrome screens (the invite gate, onboarding)
// take the whole window.
internal interface IScreen
{
    Screen Id { get; }

    bool Chrome { get; }

    void Draw();
}
