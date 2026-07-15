using Dalamud.Interface;
using Eikon.Navigation;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Age and community guidelines gate. Both confirmations are required before continuing to sign in.
// Serves as the consent record (the server persists it in phase C).
internal sealed class AgeGuidelinesScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;

    private bool ageConfirmed;
    private bool guidelinesConfirmed;

    public AgeGuidelinesScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
    }

    public Screen Id => Screen.AgeGuidelines;

    public bool Chrome => false;

    public void Draw()
    {
        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(20f);
        var contentWidth = avail.X - (pad * 2f);

        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

        ImGui.Dummy(new Vector2(0f, Ui.Px(28f)));
        this.kit.CenteredFramedIcon(avail.X, FontAwesomeIcon.ShieldAlt.ToIconString(), Ui.Px(48f));
        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        Ui.CenteredText(avail.X, this.fonts.Title, Palette.TextPrimary, "Before you start");
        ImGui.Dummy(new Vector2(0f, Ui.Px(4f)));
        Ui.CenteredText(avail.X, this.fonts.Caption, Palette.TextSecondary, "Eikon is an 18+ space. A few ground rules.");

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        ImGui.SetCursorPosX(pad);
        this.DrawRules(contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        ImGui.SetCursorPosX(pad);
        this.ageConfirmed = this.DrawCheck("##age", this.ageConfirmed, "I'm 18 or older.");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        ImGui.SetCursorPosX(pad);
        this.guidelinesConfirmed = this.DrawCheck("##terms", this.guidelinesConfirmed, "I agree to the Community Guidelines and Privacy.");

        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        ImGui.SetCursorPosX(pad);
        if (this.ageConfirmed && this.guidelinesConfirmed)
        {
            if (this.kit.PrimaryButton("##agree", "Agree and continue", contentWidth))
                this.router.Navigate(Screen.Onboarding);
        }
        else
        {
            this.kit.SecondaryButton("##agree_off", "Agree and continue", contentWidth);
        }
    }

    private bool DrawCheck(string id, bool value, string label)
    {
        var result = this.kit.Checkbox(id, value);
        ImGui.SameLine(0f, Ui.Px(9f));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
            ImGui.TextUnformatted(label);
        return result;
    }

    private void DrawRules(float width)
    {
        using (ImRaii.PushColor(ImGuiCol.ChildBg, Palette.Surface1))
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 0f))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(Ui.Px(14f), Ui.Px(14f))))
        using (var box = ImRaii.Child("rules", new Vector2(width, Ui.Px(140f)), true))
        {
            if (!box.Success)
                return;

            this.Rule("No minors and no childlike depictions,", " ever. This includes Lalafell in NSFW.");
            ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
            this.Rule("Consent and respect.", " No harassment, no unsolicited explicit content.");
            ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
            this.Rule("Eikon stays in game and on Discord.", " No IRL meetups.");
        }
    }

    // Each rule reads as a bright lead phrase and a muted remainder that wraps after it.
    private void Rule(string lead, string rest)
    {
        using (this.fonts.Caption.Push())
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
                ImGui.TextUnformatted(lead);
            ImGui.SameLine(0f, 0f);
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
                ImGui.TextWrapped(rest);
        }
    }
}
