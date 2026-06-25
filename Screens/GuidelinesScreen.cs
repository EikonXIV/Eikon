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
        using var body = ImRaii.Child("guidelines_body", new Vector2(avail.X, avail.Y - headerHeight));
        if (!body.Success)
            return;

        ImGui.Indent(pad);
        var width = avail.X - (pad * 2f);

        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.Intro(width,
            "Eikon is an 18+ space for gay and bi men in FFXIV. These rules keep it safe - for you and " +
            "everyone else. Take your time, trust your gut, and protect your privacy. You never owe anyone " +
            "your personal information.");

        this.Section("You must be 18 or older");
        this.Bullet(width, "Eikon is strictly for adults. No minors, and no childlike depictions ever - including Lalafell in NSFW contexts. This is zero tolerance.");

        this.Section("Protect your privacy");
        this.Bullet(width, "Don't share identifying details: your legal name, address, workplace or school, phone number, or accounts on other platforms.");
        this.Bullet(width, "Be careful with photos. Avoid face pics or images that reveal where you live or work, especially if you are not out.");
        this.Bullet(width, "Strip the obvious tells too - landmarks, name tags, reflections, and street signs in the background.");
        this.Bullet(width, "You decide what to reveal and when. There is no rush, and silence is always an acceptable answer.");

        this.Section("Keep yourself safe");
        this.Bullet(width, "Take it slow. Trust is earned over many conversations, not promised in the first one.");
        this.Bullet(width, "Never send money or financial information. Sudden emergencies, gift cards, and investment or crypto tips are classic scams.");
        this.Bullet(width, "Be wary of anyone who pressures you for photos or personal details, or who rushes to move the conversation off Eikon.");
        this.Bullet(width, "If you ever choose to meet in person, pick a public place, tell a friend where you'll be and when, and arrange your own way home.");

        this.Section("Consent and respect");
        this.Bullet(width, "Get a clear yes before sending explicit messages or images. No unsolicited NSFW content, ever.");
        this.Bullet(width, "No harassment, hate, outing, or threats. Don't pressure anyone, and don't share private chats or images without consent.");
        this.Bullet(width, "Respect it when someone unmatches, blocks, or simply stops replying.");

        this.Section("Watch for red flags");
        this.Bullet(width, "Stories that don't add up, refusing to verify, or pushing you to keep secrets.");
        this.Bullet(width, "Anyone asking you to turn off safety features, share your account, or hand over your passphrase.");

        this.Section("Report and block");
        this.Bullet(width, "Block anyone who makes you uncomfortable - they won't be able to reach you again.");
        this.Bullet(width, "Report scams, harassment, or anyone you believe is underage. Reports are confidential and help protect everyone.");

        this.Section("Your account is yours");
        this.Bullet(width, "Keep your account and passphrase to yourself. Eikon and its staff will never ask for your Discord password.");

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

        const string title = "Community Guidelines";
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
            ImGui.TextWrapped(text);
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
            using (ImRaii.PushColor(ImGuiCol.Text, this.theme.Accent))
                ImGui.TextUnformatted("•");
            ImGui.SameLine(0f, 0f);
            ImGui.SetCursorPosX(startX + indent);
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
            {
                ImGui.PushTextWrapPos(startX + width);
                ImGui.TextWrapped(text);
                ImGui.PopTextWrapPos();
            }
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(7f)));
    }
}
