using Eikon.Navigation;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// A chrome screen that shows a title and a short note. Used for the nav tabs that are not built
// yet, so routing is exercised end to end before the real screens land.
internal sealed class PlaceholderScreen : IScreen
{
    private readonly UiFonts fonts;
    private readonly string title;
    private readonly string note;

    public PlaceholderScreen(Screen id, string title, string note, UiFonts fonts)
    {
        this.Id = id;
        this.title = title;
        this.note = note;
        this.fonts = fonts;
    }

    public Screen Id { get; }

    public bool Chrome => true;

    public void Draw()
    {
        using (this.fonts.Title.Push())
            ImGui.TextUnformatted(this.title);
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
            ImGui.TextWrapped(this.note);
    }
}
