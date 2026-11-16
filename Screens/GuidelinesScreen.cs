using Dalamud.Interface;
using Eikon.Navigation;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Read-only Community Guidelines, opened from Settings. The onboarding consent gate (with the age and
// agreement checkboxes that lead into onboarding) lives in AgeGuidelinesScreen; this screen only shows
// the rules with a Back button, so viewing them from Settings doesn't drop the member into onboarding.
internal sealed class GuidelinesScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly UiFonts fonts;

    public GuidelinesScreen(ScreenRouter router, ThemeService theme, UiFonts fonts)
    {
        this.router = router;
        this.theme = theme;
        this.fonts = fonts;
    }

    public Screen Id => Screen.Guidelines;

    public bool Chrome => false;

    public void Draw()
    {
        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(16f);
        var headerHeight = Ui.Px(52f);

        this.DrawHeader(avail.X, pad, headerHeight);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using var body = ImRaii.Child("guidelines_body", new Vector2(avail.X, avail.Y - headerHeight), false, ImGuiWindowFlags.AlwaysVerticalScrollbar);
        if (!body.Success)
            return;

        ImGui.Indent(pad);
        var width = avail.X - (pad * 2f);

        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.Intro(width, Loc.T("guidelines.intro"));

        this.Section(Loc.T("guidelines.adults.title"));
        this.Bullet(width, Loc.T("guidelines.adults.rule"));

        this.Section(Loc.T("guidelines.privacy.title"));
        this.Bullet(width, Loc.T("guidelines.privacy.details"));
        this.Bullet(width, Loc.T("guidelines.privacy.photos"));
        this.Bullet(width, Loc.T("guidelines.privacy.tells"));
        this.Bullet(width, Loc.T("guidelines.privacy.pace"));

        this.Section(Loc.T("guidelines.safety.title"));
        this.Bullet(width, Loc.T("guidelines.safety.slow"));
        this.Bullet(width, Loc.T("guidelines.safety.money"));
        this.Bullet(width, Loc.T("guidelines.safety.pressure"));
        this.Bullet(width, Loc.T("guidelines.safety.meeting"));

        this.Section(Loc.T("guidelines.respect.title"));
        this.Bullet(width, Loc.T("guidelines.respect.consent"));
        this.Bullet(width, Loc.T("guidelines.respect.harassment"));
        this.Bullet(width, Loc.T("guidelines.respect.boundaries"));

        this.Section(Loc.T("guidelines.redFlags.title"));
        this.Bullet(width, Loc.T("guidelines.redFlags.stories"));
        this.Bullet(width, Loc.T("guidelines.redFlags.requests"));

        this.Section(Loc.T("guidelines.report.title"));
        this.Bullet(width, Loc.T("guidelines.report.block"));
        this.Bullet(width, Loc.T("guidelines.report.report"));

        this.Section(Loc.T("guidelines.account.title"));
        this.Bullet(width, Loc.T("guidelines.account.secret"));

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        ImGui.Unindent(pad);
    }

    private void DrawHeader(float fullWidth, float pad, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var midY = origin.Y + (height * 0.5f);

        var backGlyph = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, backGlyph);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##guidelines_back", backSize))
            this.router.Navigate(Screen.Settings);
        Ui.TextAt(drawList, this.fonts.Icon, ImGui.GetItemRectMin(), Palette.TextSecondary.U32(), backGlyph);

        var title = Loc.T("guidelines.title");
        var titleSize = Ui.Measure(this.fonts.Body, title);
        Ui.TextAt(drawList, this.fonts.Body,
            new Vector2(origin.X + pad + backSize.X + Ui.Px(12f), midY - (titleSize.Y * 0.5f)),
            Palette.TextPrimary.U32(), title);

        drawList.AddLine(
            new Vector2(origin.X, origin.Y + height), new Vector2(origin.X + fullWidth, origin.Y + height),
            Palette.Border.U32(), 1f);
    }

    private void Intro(float width, string text)
    {
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            Ui.TextWrapped(text);
            ImGui.PopTextWrapPos();
        }
    }

    private void Section(string title)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        using (this.fonts.Body.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
            ImGui.TextUnformatted(title);
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
    }

    private void Bullet(float width, string text)
    {
        var startX = ImGui.GetCursorPosX();
        var indent = Ui.Px(16f);

        using (this.fonts.Caption.Push())
        {
            using (ImRaii.PushColor(ImGuiCol.Text, this.theme.Secondary.Text))
                ImGui.TextUnformatted("•");
            ImGui.SameLine(0f, 0f);
            ImGui.SetCursorPosX(startX + indent);
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
            {
                ImGui.PushTextWrapPos(startX + width);
                Ui.TextWrapped(text);
                ImGui.PopTextWrapPos();
            }
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(7f)));
    }
}
