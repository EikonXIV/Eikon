using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Windows;

// The single Eikon window. It applies the dark skin to the window frame, then routes to the
// current screen. Chrome screens render between the header and the bottom nav; non chrome screens
// (the invite gate, onboarding) take the whole window.
internal sealed class MainWindow : Window, IDisposable
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly UiFonts fonts;
    private readonly InboxService inbox;
    private readonly WindowController windowController;
    private readonly Dictionary<Screen, IScreen> screensById;

    private int pushedColors;
    private int pushedVars;
    private double lastInboxRefresh;

    public MainWindow(ScreenRouter router, ThemeService theme, UiFonts fonts, InboxService inbox, WindowController windowController, IEnumerable<IScreen> screens)
        : base("Eikon##main",
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar)
    {
        this.router = router;
        this.theme = theme;
        this.fonts = fonts;
        this.inbox = inbox;
        this.windowController = windowController;
        this.screensById = screens.ToDictionary(s => s.Id);

        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(340, 560),
            MaximumSize = new Vector2(560, 1100),
        };
        this.Size = new Vector2(360, 660);
        this.SizeCondition = ImGuiCond.FirstUseEver;
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

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Ui.Px(12f));
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
        var screen = this.screensById.GetValueOrDefault(this.router.Current);
        if (screen is null || screen.Chrome)
            this.DrawWithChrome(screen);
        else
            this.DrawFull(screen);
    }

    private void DrawWithChrome(IScreen? screen)
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
        var headerHeight = Ui.Px(46f);
        var navHeight = Ui.Px(56f);
        var bodyHeight = avail.Y - headerHeight - navHeight;

        this.DrawHeader(headerHeight, avail.X);

        using (var body = ImRaii.Child("body", new Vector2(avail.X, bodyHeight)))
        {
            if (body.Success)
            {
                ImGui.SetCursorPosY(Ui.Px(14f));
                ImGui.Indent(Ui.Px(16f));
                screen?.Draw();
                ImGui.Unindent(Ui.Px(16f));
            }
        }

        this.DrawBottomNav(navHeight);
    }

    private void DrawFull(IScreen screen)
    {
        var avail = ImGui.GetContentRegionAvail();
        using (var body = ImRaii.Child("full", avail))
        {
            if (body.Success)
                screen.Draw();
        }
    }

    private void DrawHeader(float height, float width)
    {
        var startY = ImGui.GetCursorPosY();
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var padX = Ui.Px(16f);

        drawList.AddLine(
            new Vector2(origin.X, origin.Y + height),
            new Vector2(origin.X + width, origin.Y + height),
            Palette.Border.U32(), 1f);

        const string title = "Eikon";
        var titleSize = Ui.Measure(this.fonts.Title, title);
        Ui.TextAt(drawList, this.fonts.Title,
            new Vector2(origin.X + padX, origin.Y + ((height - titleSize.Y) * 0.5f)),
            Palette.TextPrimary.U32(), title);

        // Minimize to the floating orb (top-right).
        var btn = new Vector2(Ui.Px(30f), Ui.Px(30f));
        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - padX - btn.X, origin.Y + ((height - btn.Y) * 0.5f)));
        if (ImGui.InvisibleButton("##minimize", btn))
            this.windowController.Minimize();
        var hovered = ImGui.IsItemHovered();
        var btnMin = ImGui.GetItemRectMin();
        if (hovered)
            drawList.AddRectFilled(btnMin, btnMin + btn, Palette.WithAlpha(Palette.White, 0.06f).U32(), Ui.Px(8f));
        var glyph = FontAwesomeIcon.Minus.ToIconString();
        var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(drawList, this.fonts.Icon, btnMin + ((btn - glyphSize) * 0.5f), (hovered ? Palette.TextSecondary : Palette.TextMuted).U32(), glyph);

        ImGui.SetCursorPosY(startY + height);
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

        var items = new (Screen Screen, FontAwesomeIcon Icon, string Label)[]
        {
            (Screen.Grid, FontAwesomeIcon.Th, "Grid"),
            (Screen.Messages, FontAwesomeIcon.CommentDots, "Messages"),
            (Screen.MyProfile, FontAwesomeIcon.User, "Profile"),
            (Screen.Settings, FontAwesomeIcon.Cog, "Settings"),
        };

        long unread = 0;
        foreach (var c in this.inbox.Conversations)
            unread += c.Unread;

        var gap = Ui.Px(3f);
        var cellWidth = winSize.X / items.Length;
        for (var i = 0; i < items.Length; i++)
        {
            var x = winPos.X + (i * cellWidth);
            ImGui.SetCursorScreenPos(new Vector2(x, bandTop));
            if (ImGui.InvisibleButton($"##nav{i}", new Vector2(cellWidth, height)))
                this.router.Navigate(items[i].Screen);

            var active = this.router.Current == items[i].Screen;
            var color = (active
                ? this.theme.Accent
                : ImGui.IsItemHovered() ? Palette.TextSecondary : Palette.TextMuted).U32();

            var glyph = items[i].Icon.ToIconString();
            var iconSize = Ui.Measure(this.fonts.Icon, glyph);
            var labelSize = Ui.Measure(this.fonts.Caption, items[i].Label);
            var blockHeight = iconSize.Y + gap + labelSize.Y;
            var top = bandTop + ((height - blockHeight) * 0.5f);
            var centerX = x + (cellWidth * 0.5f);

            Ui.TextAt(drawList, this.fonts.Icon, new Vector2(centerX - (iconSize.X * 0.5f), top), color, glyph);
            Ui.TextAt(drawList, this.fonts.Caption,
                new Vector2(centerX - (labelSize.X * 0.5f), top + iconSize.Y + gap), color, items[i].Label);

            // Unread badge on the Messages tab, mirroring the minimized orb's count.
            if (items[i].Screen == Screen.Messages && unread > 0)
                this.DrawNavBadge(drawList, new Vector2(centerX + (iconSize.X * 0.5f) + Ui.Px(3f), top - Ui.Px(1f)), unread);
        }
    }

    // Small red count badge anchored at the top-right of a nav icon (the Messages unread indicator).
    private void DrawNavBadge(ImDrawListPtr drawList, Vector2 anchor, long count)
    {
        var label = count > 99 ? "99+" : count.ToString();
        var labelSize = Ui.Measure(this.fonts.Caption, label);
        var badgeH = Ui.Px(15f);
        var badgeW = MathF.Max(badgeH, labelSize.X + Ui.Px(7f));
        var min = new Vector2(anchor.X - (badgeW * 0.5f), anchor.Y - (badgeH * 0.5f));
        var max = min + new Vector2(badgeW, badgeH);
        drawList.AddRectFilled(min, max, Palette.Danger.U32(), badgeH * 0.5f);
        drawList.AddRect(min, max, Palette.Bg.U32(), badgeH * 0.5f, ImDrawFlags.None, Ui.Px(1.5f));
        Ui.TextAt(drawList, this.fonts.Caption, min + ((new Vector2(badgeW, badgeH) - labelSize) * 0.5f), Palette.White.U32(), label);
    }
}
