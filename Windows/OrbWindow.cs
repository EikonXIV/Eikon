using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Eikon.Crypto;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Windows;

// Minimized launcher. When the main window is hidden, this small floating phone stays on screen: drag
// it anywhere, tap it to reopen the app, right-click it to close the app fully (no orb). It's drawn
// from primitives (rounded body, lit screen, speaker and home-bar lines, plus an unread-count badge -
// or a lock symbol while the vault is locked). The window is borderless and transparent so only the
// phone shows; it stays movable because the body (a Dummy, not a button) is draggable, and a tap is
// told from a drag by how far the cursor moved between press and release.
internal sealed class OrbWindow : Window
{
    private readonly ThemeService theme;
    private readonly UiFonts fonts;
    private readonly InboxService inbox;
    private readonly KeyVault vault;
    private Vector2 pressPos;
    private double lastRefreshAt;

    public OrbWindow(ThemeService theme, UiFonts fonts, InboxService inbox, KeyVault vault)
        : base("Eikon##orb",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.theme = theme;
        this.fonts = fonts;
        this.inbox = inbox;
        this.vault = vault;
        this.IsOpen = false;
        this.Position = new Vector2(80f, 80f);
        this.PositionCondition = ImGuiCond.FirstUseEver;
    }

    public event Action? RestoreRequested;

    public event Action? CloseRequested;

    public override void Draw()
    {
        // Keep the relay alive and the inbox fresh while minimized: EnsureLoaded refetches whenever an
        // incoming message invalidates the cache, and a periodic refresh covers anything missed.
        this.inbox.EnsureLoaded();
        var time = ImGui.GetTime();
        if (time - this.lastRefreshAt > 10.0)
        {
            this.lastRefreshAt = time;
            this.inbox.Refresh();
        }
        long unread = 0;
        foreach (var c in this.inbox.Conversations)
            unread += c.Unread;

        var w = Ui.Px(48f);
        var h = Ui.Px(80f);
        ImGui.Dummy(new Vector2(w, h));
        var min = ImGui.GetItemRectMin();
        var max = min + new Vector2(w, h);

        var drawList = ImGui.GetWindowDrawList();
        var vp = ImGui.GetMainViewport();
        drawList.PushClipRect(vp.Pos, vp.Pos + vp.Size, false);   // let the shadow + badge spill past the window rect

        var bodyRound = Ui.Px(13f);

        // soft drop shadow
        for (var i = 4; i >= 1; i--)
        {
            var s = i * Ui.Px(1.6f);
            drawList.AddRectFilled(new Vector2(min.X - s, min.Y - s + Ui.Px(2f)), new Vector2(max.X + s, max.Y + s + Ui.Px(2f)), new Vector4(0f, 0f, 0f, 0.05f * (5 - i)).U32(), bodyRound + s);
        }

        // phone body + hairline edge
        drawList.AddRectFilled(min, max, Palette.Surface2.U32(), bodyRound);
        drawList.AddRect(min, max, Palette.WithAlpha(Palette.White, 0.12f).U32(), bodyRound, ImDrawFlags.None, Ui.Px(1f));

        // speaker slit
        var spkW = Ui.Px(14f);
        var spkH = Ui.Px(3f);
        var spkMin = new Vector2(min.X + ((w - spkW) * 0.5f), min.Y + Ui.Px(6f));
        drawList.AddRectFilled(spkMin, spkMin + new Vector2(spkW, spkH), Palette.WithAlpha(Palette.White, 0.22f).U32(), spkH * 0.5f);

        // lit screen
        var screenMin = new Vector2(min.X + Ui.Px(7f), min.Y + Ui.Px(12f));
        var screenMax = new Vector2(max.X - Ui.Px(7f), max.Y - Ui.Px(11f));
        drawList.AddRectFilled(screenMin, screenMax, this.theme.AccentDeep.U32(), Ui.Px(8f));

        // The Eikon mark on the lit screen: stone tablet outline (white) with a glowing accent core.
        Ui.AetherCore(drawList, (screenMin + screenMax) * 0.5f, Ui.Px(40f),
            Palette.WithAlpha(Palette.White, 0.92f).U32(), this.theme.AccentText.U32());

        // home indicator
        var homeW = Ui.Px(18f);
        var homeH = Ui.Px(3f);
        var homeMin = new Vector2(min.X + ((w - homeW) * 0.5f), max.Y - Ui.Px(7f));
        drawList.AddRectFilled(homeMin, homeMin + new Vector2(homeW, homeH), Palette.WithAlpha(Palette.White, 0.18f).U32(), homeH * 0.5f);

        // Top-right corner: a lock symbol while the vault is locked, otherwise the unread count.
        var badgeCenter = new Vector2(max.X - Ui.Px(1f), min.Y + Ui.Px(1f));
        if (!this.vault.IsUnlocked)
        {
            var d = Ui.Px(22f);
            drawList.AddCircleFilled(badgeCenter, d * 0.5f, Palette.Bg.U32(), 16);
            drawList.AddCircle(badgeCenter, d * 0.5f, Palette.WithAlpha(Palette.White, 0.25f).U32(), 16, Ui.Px(1.5f));
            var lockGlyph = FontAwesomeIcon.Lock.ToIconString();
            var lockSize = Ui.Measure(this.fonts.Icon, lockGlyph);
            Ui.TextAt(drawList, this.fonts.Icon, badgeCenter - (lockSize * 0.5f), Palette.TextSecondary.U32(), lockGlyph);
        }
        else if (unread > 0)
        {
            var label = unread > 99 ? "99+" : unread.ToString();
            var labelSize = Ui.Measure(this.fonts.Caption, label);
            var badgeH = Ui.Px(20f);
            var badgeW = MathF.Max(badgeH, labelSize.X + Ui.Px(10f));
            var badgeMin = badgeCenter - new Vector2(badgeW * 0.5f, badgeH * 0.5f);
            var badgeMax = badgeMin + new Vector2(badgeW, badgeH);
            drawList.AddRectFilled(badgeMin, badgeMax, Palette.Danger.U32(), badgeH * 0.5f);
            drawList.AddRect(badgeMin, badgeMax, Palette.Bg.U32(), badgeH * 0.5f, ImDrawFlags.None, Ui.Px(2f));
            Ui.TextAt(drawList, this.fonts.Caption, badgeMin + ((new Vector2(badgeW, badgeH) - labelSize) * 0.5f), Palette.White.U32(), label);
        }

        drawList.PopClipRect();

        // The window drags on its body; a near-stationary press/release is a tap -> reopen. A
        // right-click closes the app fully (no orb); the tooltip is what teaches that gesture.
        if (ImGui.IsWindowHovered())
        {
            using (this.fonts.Caption.Push())
                ImGui.SetTooltip("Tap to open. Right-click to close.");
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                this.pressPos = ImGui.GetIO().MousePos;
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && Vector2.Distance(ImGui.GetIO().MousePos, this.pressPos) < Ui.Px(6f))
                this.RestoreRequested?.Invoke();
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                this.CloseRequested?.Invoke();
        }
    }
}
