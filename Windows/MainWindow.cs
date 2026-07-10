using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.Screens;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Windows;

// The single Eikon window. It applies the warm-editorial skin to the frame, then routes to the current
// screen. Chrome screens render between the title bar and the bottom nav; non-chrome screens (invite
// gate, onboarding) take the whole window. Screens own their own padding.
internal sealed class MainWindow : Window, IDisposable
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly UiFonts fonts;
    private readonly InboxService inbox;
    private readonly WindowController windowController;
    private readonly Dictionary<Screen, IScreen> screensById;
    private readonly string versionTag;

    private int pushedColors;
    private int pushedVars;
    private double lastInboxRefresh;

    public MainWindow(ScreenRouter router, ThemeService theme, UiFonts fonts, InboxService inbox, WindowController windowController, IEnumerable<IScreen> screens)
        : base("Eikon##main",
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize)
    {
        this.router = router;
        this.theme = theme;
        this.fonts = fonts;
        this.inbox = inbox;
        this.windowController = windowController;
        this.screensById = screens.ToDictionary(s => s.Id);

        var v = typeof(BuildInfo).Assembly.GetName().Version;
        this.versionTag = (v is null ? "v0" : $"v{v.Major}·{v.Minor}") + (BuildInfo.IsLocal ? " · local" : string.Empty);

        // A fixed, non-resizable, mobile-shaped frame (scaled with the UI). Min == max locks the size and
        // NoResize drops the grip; Always re-applies it so a size saved by an older build is overridden.
        var size = new Vector2(Ui.Px(400f), Ui.Px(780f));
        this.Size = size;
        this.SizeCondition = ImGuiCond.Always;
        this.SizeConstraints = new WindowSizeConstraints { MinimumSize = size, MaximumSize = size };
    }

    public void Dispose()
    {
    }

    public override void PreDraw()
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Palette.Bg);
        ImGui.PushStyleColor(ImGuiCol.Border, Palette.Border);
        ImGui.PushStyleColor(ImGuiCol.Text, Palette.TextPrimary);
        this.pushedColors = 3;

        // Editorial is square: no window rounding, a hairline border, zero padding (screens self-pad).
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        this.pushedVars = 3;
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(this.pushedVars);
        ImGui.PopStyleColor(this.pushedColors);
    }

    public override void Draw()
    {
        // Once per session, when the app is usable, the What's new screen decides whether an update
        // happened and routes to itself.
        if (this.screensById.TryGetValue(Screen.WhatsNew, out var whatsNew) && whatsNew is WhatsNewScreen gate)
            gate.MaybeAutoPresent();

        var screen = this.screensById.GetValueOrDefault(this.router.Current);
        // The title bar always stays. Only top-level tabs (Chrome) show the bottom nav; sub-views like
        // profile detail and chat load into the body below the title bar rather than taking it over.
        this.DrawShell(screen, screen is null || screen.Chrome);
    }

    private void DrawShell(IScreen? screen, bool showNav)
    {
        // Keep the inbox warm while the app is open so the Messages tab's unread badge stays live
        // (the orb does the same while minimized). EnsureLoaded refetches when a new message lands.
        this.inbox.EnsureLoaded();
        var inboxTime = ImGui.GetTime();
        if (inboxTime - this.lastInboxRefresh > 12.0)
        {
            this.lastInboxRefresh = inboxTime;
            this.inbox.Refresh();
        }

        var avail = ImGui.GetContentRegionAvail();
        var headerHeight = Ui.Px(48f);
        var navHeight = showNav ? Ui.Px(54f) : 0f;
        var stripes = this.theme.Stripes;
        var barHeight = stripes.Count > 0 ? Ui.Px(3f) : 0f;
        var bodyHeight = avail.Y - headerHeight - barHeight - navHeight;

        this.DrawHeader(headerHeight, avail.X);

        // A flag theme paints its stripe set as a thin ribbon under the header. Editorial exposes no
        // stripes, so the ribbon is the transparent 3px slot the design leaves for future flag themes.
        if (barHeight > 0f)
        {
            Ui.FlagBar(ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos(), avail.X, stripes, barHeight);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + barHeight);
        }

        using (var body = ImRaii.Child("body", new Vector2(avail.X, bodyHeight)))
        {
            if (body.Success)
                screen?.Draw();
        }

        if (showNav)
            this.DrawBottomNav(navHeight);
    }

    private void DrawHeader(float height, float width)
    {
        var startY = ImGui.GetCursorPosY();
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var padX = Ui.Px(20f);

        // Wordmark: "Eikon" in ink, a signal-gold period, then a mono version tag baseline-aligned.
        const string word = "Eikon";
        var wordSize = Ui.Measure(this.fonts.Title, word);
        var wordY = origin.Y + ((height - wordSize.Y) * 0.5f);
        var wordX = origin.X + padX;
        Ui.TextAt(drawList, this.fonts.Title, new Vector2(wordX, wordY), Palette.TextPrimary.U32(), word);

        var dotX = wordX + wordSize.X;
        Ui.TextAt(drawList, this.fonts.Title, new Vector2(dotX, wordY), Palette.Signal.U32(), ".");
        var dotWidth = Ui.Measure(this.fonts.Title, ".").X;

        var tagSize = Ui.Measure(this.fonts.Mono, this.versionTag);
        Ui.TextAt(drawList, this.fonts.Mono,
            new Vector2(dotX + dotWidth + Ui.Px(8f), (wordY + wordSize.Y) - tagSize.Y - Ui.Px(1f)),
            Palette.TextSecondary.U32(), this.versionTag);

        drawList.AddLine(
            new Vector2(origin.X, origin.Y + height),
            new Vector2(origin.X + width, origin.Y + height),
            Palette.Border.U32(), 1f);

        // Window controls (top-right): minimize to the floating orb, then close. Drawn as crisp line
        // glyphs so they stay sharp and legible at this size. Close names the slash command in its
        // tooltip so the app never looks like it just vanished.
        var btn = new Vector2(Ui.Px(28f), Ui.Px(28f));
        var gap = Ui.Px(4f);
        // Nudge up to optically match the wordmark: "Eikon" sits high in its serif line box (empty
        // descender space below it), so a geometrically centered control reads slightly low beside it.
        var btnY = (origin.Y + ((height - btn.Y) * 0.5f)) - Ui.Px(2f);
        var closeX = (origin.X + width - padX) - btn.X;
        var minX = closeX - btn.X - gap;

        ImGui.SetCursorScreenPos(new Vector2(closeX, btnY));
        if (ImGui.InvisibleButton("##close", btn))
            this.windowController.Close();
        var closeHovered = ImGui.IsItemHovered();
        if (closeHovered)
            using (this.fonts.Caption.Push())
                ImGui.SetTooltip($"Close (reopen with {BuildInfo.Command})");
        DrawCloseGlyph(drawList, ImGui.GetItemRectMin(), btn, closeHovered);

        ImGui.SetCursorScreenPos(new Vector2(minX, btnY));
        if (ImGui.InvisibleButton("##minimize", btn))
            this.windowController.Minimize();
        DrawMinimizeGlyph(drawList, ImGui.GetItemRectMin(), btn, ImGui.IsItemHovered());

        ImGui.SetCursorPosY(startY + height);
    }

    // Crisp line glyphs for the title-bar controls: muted, brightening to ink on hover.
    private static void DrawCloseGlyph(ImDrawListPtr drawList, Vector2 min, Vector2 size, bool hovered)
    {
        var c = min + (size * 0.5f);
        var r = Ui.Px(5f);
        var col = (hovered ? Palette.TextPrimary : Palette.TextSecondary).U32();
        var th = Ui.Px(1.5f);
        drawList.AddLine(new Vector2(c.X - r, c.Y - r), new Vector2(c.X + r, c.Y + r), col, th);
        drawList.AddLine(new Vector2(c.X + r, c.Y - r), new Vector2(c.X - r, c.Y + r), col, th);
    }

    private static void DrawMinimizeGlyph(ImDrawListPtr drawList, Vector2 min, Vector2 size, bool hovered)
    {
        var c = min + (size * 0.5f);
        var r = Ui.Px(5f);
        var col = (hovered ? Palette.TextPrimary : Palette.TextSecondary).U32();
        drawList.AddLine(new Vector2(c.X - r, c.Y), new Vector2(c.X + r, c.Y), col, Ui.Px(1.5f));
    }

    private void DrawBottomNav(float height)
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var bandTop = winPos.Y + winSize.Y - height;
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddLine(
            new Vector2(winPos.X, bandTop), new Vector2(winPos.X + winSize.X, bandTop),
            Palette.Border.U32(), 1f);

        var items = new (Screen Screen, string Label)[]
        {
            (Screen.Grid, "Grid"),
            (Screen.Messages, "Messages"),
            (Screen.MyProfile, "Profile"),
            (Screen.Settings, "Settings"),
        };

        long unread = 0;
        foreach (var c in this.inbox.Conversations)
            unread += c.Unread;

        var cellWidth = winSize.X / items.Length;
        for (var i = 0; i < items.Length; i++)
        {
            var x = winPos.X + (i * cellWidth);
            ImGui.SetCursorScreenPos(new Vector2(x, bandTop));
            if (ImGui.InvisibleButton($"##nav{i}", new Vector2(cellWidth, height)))
                this.router.Navigate(items[i].Screen);

            var active = this.router.Current == items[i].Screen;
            var hovered = ImGui.IsItemHovered();
            var color = (active || hovered ? Palette.TextPrimary : Palette.TextSecondary).U32();

            var label = items[i].Label;
            var labelSize = Ui.Measure(this.fonts.Label, label);
            var centerX = x + (cellWidth * 0.5f);
            var labelPos = new Vector2(centerX - (labelSize.X * 0.5f), bandTop + ((height - labelSize.Y) * 0.5f));

            // Active tab: a hairline ink tick pinned to the top of the cell, inset from the edges.
            if (active)
            {
                var inset = Ui.Px(16f);
                drawList.AddLine(
                    new Vector2(x + inset, bandTop + 1f),
                    new Vector2((x + cellWidth) - inset, bandTop + 1f),
                    Palette.TextPrimary.U32(), 1f);
            }

            Ui.TextAt(drawList, this.fonts.Label, labelPos, color, label);

            // Signal unread badge on the Messages tab, mirroring the minimized orb's count.
            if (items[i].Screen == Screen.Messages && unread > 0)
                this.DrawNavBadge(drawList, new Vector2(labelPos.X + labelSize.X + Ui.Px(6f), labelPos.Y + (labelSize.Y * 0.5f)), unread);
        }
    }

    // Small cream-gold (signal) count badge, anchored at a left-center point (the Messages unread pill).
    private void DrawNavBadge(ImDrawListPtr drawList, Vector2 leftCenter, long count)
    {
        var text = count > 99 ? "99+" : count.ToString();
        var textSize = Ui.Measure(this.fonts.Eyebrow, text);
        var badgeH = Ui.Px(15f);
        var badgeW = MathF.Max(badgeH, textSize.X + Ui.Px(8f));
        var min = new Vector2(leftCenter.X, leftCenter.Y - (badgeH * 0.5f));
        drawList.AddRectFilled(min, min + new Vector2(badgeW, badgeH), Palette.Signal.U32(), badgeH * 0.5f);
        Ui.TextAt(drawList, this.fonts.Eyebrow,
            new Vector2(min.X + ((badgeW - textSize.X) * 0.5f), min.Y + ((badgeH - textSize.Y) * 0.5f)),
            Palette.Paper.U32(), text);
    }
}
