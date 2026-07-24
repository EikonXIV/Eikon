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

    private static readonly (string Label, string Detail)[] Rules =
    {
        ("No minors", "No childlike depictions, ever. This includes Lalafell in NSFW."),
        ("Consent and respect", "No harassment, no unsolicited explicit content."),
        ("No IRL meetups", "Eikon stays in game and on Discord."),
    };

    private void DrawRules(float width)
    {
        var boxPad = Ui.Px(14f);
        var inner = width - (boxPad * 2f);

        using (ImRaii.PushColor(ImGuiCol.ChildBg, Palette.Surface1))
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 0f))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(boxPad, boxPad)))
        using (var box = ImRaii.Child("rules", new Vector2(width, this.RulesHeight(inner) + (boxPad * 2f)), true))
        {
            if (!box.Success)
                return;

            for (var i = 0; i < Rules.Length; i++)
            {
                if (i > 0)
                    ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
                this.Rule(Rules[i].Label, Rules[i].Detail);
            }
        }
    }

    // Measured with the same raw font metrics the stacked labels and wrapped details draw with, so the
    // box is exactly tall enough at any text size and never clips the last line.
    private float RulesHeight(float inner)
    {
        using (this.fonts.Caption.Push())
        {
            var height = 0f;
            for (var i = 0; i < Rules.Length; i++)
            {
                if (i > 0)
                    height += Ui.Px(12f);
                height += ImGui.GetTextLineHeight() + Ui.Px(3f);
                height += ImGui.CalcTextSize(Rules[i].Detail, false, inner).Y;
            }

            return height;
        }
    }

    // Each rule stacks: a bright label on its own line, the muted detail wrapped left-aligned beneath it.
    private void Rule(string label, string detail)
    {
        using (this.fonts.Caption.Push())
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
                ImGui.TextUnformatted(label);
            ImGui.Dummy(new Vector2(0f, Ui.Px(3f)));
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
                ImGui.TextWrapped(detail);
        }
    }
}
