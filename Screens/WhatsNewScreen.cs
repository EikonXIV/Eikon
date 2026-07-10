using System.Diagnostics;
using Dalamud.Interface;
using Eikon.Config;
using Eikon.Content;
using Eikon.Crypto;
using Eikon.Navigation;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// "What's new" release notes (warm-editorial), grouped-changelog style. Shows once automatically after
// an update (MaybeAutoPresent, driven each frame from MainWindow) and is openable any time from
// Settings. Own header, scrollable body, footer. Content is the bundled ReleaseNotes.
internal sealed class WhatsNewScreen : IScreen
{
    private const string ChangelogUrl = "https://eikon.chat/changelog";

    private readonly ScreenRouter router;
    private readonly UiFonts fonts;
    private readonly Kit kit;
    private readonly Configuration config;
    private readonly KeyVault keyVault;

    // updateMode: shown by an update (Got it footer, only unseen releases). Otherwise the screen was
    // opened from Settings (back chevron, full history, no footer, no version write).
    private bool updateMode;
    private bool checkedThisSession;

    public WhatsNewScreen(ScreenRouter router, UiFonts fonts, Kit kit, Configuration config, KeyVault keyVault)
    {
        this.router = router;
        this.fonts = fonts;
        this.kit = kit;
        this.config = config;
        this.keyVault = keyVault;
    }

    public Screen Id => Screen.WhatsNew;

    public bool Chrome => false;

    // Called every frame from MainWindow. Once per session, when the app is usable and on the grid,
    // decide whether an update happened since the member last looked. A fresh install (no stored version)
    // is recorded silently and never shows the screen.
    public void MaybeAutoPresent()
    {
        if (this.checkedThisSession || !this.keyVault.IsUnlocked || this.router.Current != Screen.Grid)
            return;

        this.checkedThisSession = true;

        if (this.config.LastSeenVersion is null)
        {
            this.config.LastSeenVersion = PluginVersion.Display;
            this.config.Save();
            return;
        }

        if (PluginVersion.Current > PluginVersion.Parse(this.config.LastSeenVersion))
        {
            this.updateMode = true;
            this.router.Navigate(Screen.WhatsNew);
        }
    }

    public void Draw()
    {
        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(16f);
        var headerHeight = Ui.Px(52f);
        var footerHeight = this.updateMode ? Ui.Px(92f) : 0f;

        this.DrawHeader(avail.X, pad, headerHeight);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("whatsnew_body", new Vector2(avail.X, avail.Y - headerHeight - footerHeight)))
        {
            if (body.Success)
            {
                ImGui.Indent(pad);
                var width = avail.X - (pad * 2f);
                ImGui.Dummy(new Vector2(0f, Ui.Px(4f)));

                var seen = this.updateMode ? PluginVersion.Parse(this.config.LastSeenVersion) : null;
                var releases = ReleaseNotes.Since(seen).ToList();
                if (releases.Count == 0)
                    this.CaughtUp(width);
                else
                    for (var i = 0; i < releases.Count; i++)
                        this.DrawRelease(width, releases[i], i == 0, i < releases.Count - 1);

                ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
                ImGui.Unindent(pad);
            }
        }

        if (this.updateMode)
            this.DrawFooter(avail, pad, footerHeight);
    }

    private void DrawHeader(float fullWidth, float pad, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var midY = origin.Y + (height * 0.5f);
        var textX = origin.X + pad;

        if (!this.updateMode)
        {
            var backGlyph = FontAwesomeIcon.ChevronLeft.ToIconString();
            var backSize = Ui.Measure(this.fonts.Icon, backGlyph);
            ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
            if (ImGui.InvisibleButton("##wn_back", backSize))
                this.router.Navigate(Screen.Settings);
            Ui.TextAt(drawList, this.fonts.Icon, ImGui.GetItemRectMin(), Palette.TextSecondary.U32(), backGlyph);
            textX = origin.X + pad + backSize.X + Ui.Px(12f);
        }

        const string eyebrow = "WHAT'S NEW";
        var eyebrowSize = Ui.Measure(this.fonts.Eyebrow, eyebrow);
        Ui.TextAt(drawList, this.fonts.Eyebrow, new Vector2(textX, midY - (eyebrowSize.Y * 0.5f)), Palette.TextSecondary.U32(), eyebrow);

        var version = "V" + PluginVersion.Display;
        var versionSize = Ui.Measure(this.fonts.Mono, version);
        Ui.TextAt(drawList, this.fonts.Mono, new Vector2(origin.X + fullWidth - pad - versionSize.X, midY - (versionSize.Y * 0.5f)), Palette.TextMuted.U32(), version);

        drawList.AddLine(new Vector2(origin.X, origin.Y + height), new Vector2(origin.X + fullWidth, origin.Y + height), Palette.Border.U32(), 1f);
    }

    private void DrawRelease(float width, Release release, bool latest, bool divider)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(latest ? 8f : 6f)));

        var drawList = ImGui.GetWindowDrawList();
        var rowPos = ImGui.GetCursorScreenPos();
        var v = release.Version;
        var header = v.Build == 0 ? $"Version {v.Major}.{v.Minor}" : $"Version {v.Major}.{v.Minor}.{v.Build}";
        var headerSize = Ui.Measure(this.fonts.SerifTitle, header);
        Ui.TextAt(drawList, this.fonts.SerifTitle, rowPos, Palette.TextPrimary.U32(), header);

        var dateSize = Ui.Measure(this.fonts.Mono, release.Date);
        Ui.TextAt(drawList, this.fonts.Mono,
            new Vector2(rowPos.X + headerSize.X + Ui.Px(10f), rowPos.Y + headerSize.Y - dateSize.Y - Ui.Px(4f)),
            Palette.TextMuted.U32(), release.Date);

        if (latest)
        {
            const string tag = "LATEST";
            var tagText = Ui.Measure(this.fonts.Eyebrow, tag);
            var tagPad = new Vector2(Ui.Px(8f), Ui.Px(3f));
            var tagSize = tagText + (tagPad * 2f);
            var tagPos = new Vector2(rowPos.X + width - tagSize.X, rowPos.Y + ((headerSize.Y - tagSize.Y) * 0.5f));
            drawList.AddRect(tagPos, tagPos + tagSize, Palette.Signal.U32(), 0f, ImDrawFlags.None, 1f);
            Ui.TextAt(drawList, this.fonts.Eyebrow, tagPos + tagPad, Palette.Signal.U32(), tag);
        }

        ImGui.Dummy(new Vector2(width, headerSize.Y));

        this.Group(width, "New", release.New, Palette.Signal);
        this.Group(width, "Improved", release.Improved, Palette.Signal);
        this.Group(width, "Fixed", release.Fixed, Palette.TextMuted);

        if (divider)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
            var dividerPos = ImGui.GetCursorScreenPos();
            drawList.AddLine(dividerPos, new Vector2(dividerPos.X + width, dividerPos.Y), Palette.Border.U32(), 1f);
            ImGui.Dummy(new Vector2(0f, Ui.Px(4f)));
        }
    }

    private void Group(float width, string label, string[] items, Vector4 markerColor)
    {
        if (items.Length == 0)
            return;

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        var pos = ImGui.GetCursorScreenPos();
        Ui.TextAt(ImGui.GetWindowDrawList(), this.fonts.Eyebrow, pos, Palette.TextSecondary.U32(), label.ToUpperInvariant());
        ImGui.Dummy(new Vector2(0f, Ui.Measure(this.fonts.Eyebrow, label).Y + Ui.Px(8f)));

        foreach (var item in items)
            this.Bullet(width, item, markerColor);
    }

    private void Bullet(float width, string text, Vector4 markerColor)
    {
        var startX = ImGui.GetCursorPosX();
        var indent = Ui.Px(18f);

        // Small filled square marker, aligned to the first text line; the text wraps beside it.
        var markPos = ImGui.GetCursorScreenPos();
        var square = Ui.Px(4f);
        var markY = markPos.Y + Ui.Px(6f);
        ImGui.GetWindowDrawList().AddRectFilled(
            new Vector2(markPos.X + Ui.Px(2f), markY), new Vector2(markPos.X + Ui.Px(2f) + square, markY + square), markerColor.U32());

        ImGui.SetCursorPosX(startX + indent);
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
        {
            ImGui.PushTextWrapPos(startX + width);
            ImGui.TextWrapped(text);
            ImGui.PopTextWrapPos();
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
    }

    private void CaughtUp(float width)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(24f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextWrapped("You're all caught up.");
            ImGui.PopTextWrapPos();
        }
    }

    private void DrawFooter(Vector2 avail, float pad, float height)
    {
        var width = avail.X - (pad * 2f);
        var top = avail.Y - height + Ui.Px(12f);
        var drawList = ImGui.GetWindowDrawList();

        // Gold "Got it" CTA: a solid signal fill with a near-black tracked-caps label.
        ImGui.SetCursorPos(new Vector2(pad, top));
        var buttonPos = ImGui.GetCursorScreenPos();
        var buttonSize = new Vector2(width, Ui.Px(48f));
        if (ImGui.InvisibleButton("##wn_got", buttonSize))
            this.Dismiss();
        drawList.AddRectFilled(buttonPos, buttonPos + buttonSize, Palette.Signal.U32());
        if (ImGui.IsItemHovered())
            drawList.AddRectFilled(buttonPos, buttonPos + buttonSize, Palette.WithAlpha(Palette.White, 0.10f).U32());
        const string gotIt = "GOT IT";
        var gotSize = Ui.Measure(this.fonts.Eyebrow, gotIt);
        Ui.TextAt(drawList, this.fonts.Eyebrow, buttonPos + ((buttonSize - gotSize) * 0.5f), Palette.Paper.U32(), gotIt);

        const string link = "FULL CHANGELOG  →";
        var linkSize = Ui.Measure(this.fonts.Eyebrow, link);
        ImGui.SetCursorPos(new Vector2(pad + ((width - linkSize.X) * 0.5f), top + Ui.Px(60f)));
        if (ImGui.InvisibleButton("##wn_changelog", linkSize))
            OpenChangelog();
        Ui.TextAt(drawList, this.fonts.Eyebrow, ImGui.GetItemRectMin(),
            (ImGui.IsItemHovered() ? Palette.TextSecondary : Palette.TextMuted).U32(), link);
    }

    private void Dismiss()
    {
        this.config.LastSeenVersion = PluginVersion.Display;
        this.config.Save();
        this.updateMode = false;
        this.router.Navigate(Screen.Grid);
    }

    private static void OpenChangelog()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ChangelogUrl) { UseShellExecute = true });
        }
        catch
        {
            // Opening the browser is best-effort; a failure just means no changelog page this time.
        }
    }
}
