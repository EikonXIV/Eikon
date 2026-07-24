using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Eikon.Crypto;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Windows;

// Minimized launcher. When the main window is hidden, this small floating tomestone stays on screen:
// drag it anywhere, tap it to reopen the app, right-click it to close the app fully (no orb). It's
// drawn from primitives (an accent plate with the Eikon diamond knocked out of it, plus an
// unread-count badge - or a lock symbol while the vault is locked). The window is borderless and
// transparent so only the plate shows; it stays movable because the body (a Dummy, not a button) is
// draggable, and a tap is told from a drag by how far the cursor moved between press and release.
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
        var h = Ui.Px(72f);
        ImGui.Dummy(new Vector2(w, h));
        var min = ImGui.GetItemRectMin();
        var max = min + new Vector2(w, h);

        var drawList = ImGui.GetWindowDrawList();
        var vp = ImGui.GetMainViewport();
        drawList.PushClipRect(vp.Pos, vp.Pos + vp.Size, false);   // let the shadow + badge spill past the window rect

        var bodyRound = Ui.Px(13f);

        // One soft seat rather than a stacked shadow: enough to lift the plate off bright scenery
        // without the soft-UI depth the rest of the editorial surface has dropped.
        drawList.AddRectFilled(
            new Vector2(min.X - Ui.Px(2f), min.Y - Ui.Px(1f)),
            new Vector2(max.X + Ui.Px(2f), max.Y + Ui.Px(3f)),
            new Vector4(0f, 0f, 0f, 0.38f).U32(),
            bodyRound + Ui.Px(2f));

        // The plate is a solid accent tomestone with the mark's diamond knocked out of it, and the gem
        // punching back through in the plate color. Every color is a theme token, so the launcher
        // recolors with the picked theme: OnAccent is whichever of paper or white reads on that accent,
        // which is what keeps the cut legible on light themes as well as dark ones.
        var locked = !this.vault.IsUnlocked;
        var plate = locked ? Palette.WithAlpha(this.theme.Accent, 0.55f) : this.theme.Accent;
        var cut = locked ? Palette.WithAlpha(this.theme.OnAccent, 0.62f) : this.theme.OnAccent;

        drawList.AddRectFilled(min, max, plate.U32(), bodyRound);

        var center = (min + max) * 0.5f;
        var armX = w * 0.325f;
        var armY = h * 0.233f;
        drawList.AddQuadFilled(
            new Vector2(center.X, center.Y - armY),
            new Vector2(center.X + armX, center.Y),
            new Vector2(center.X, center.Y + armY),
            new Vector2(center.X - armX, center.Y),
            cut.U32());
        drawList.AddCircleFilled(center, w * 0.08f, plate.U32(), 16);

        // Top-right corner: a lock symbol while the vault is locked, otherwise the unread count. Both sit
        // on the theme's own ground with an accent ring, so they read against the plate and against
        // whatever is behind the orb, on light and dark themes alike. Red is deliberately not used here:
        // on an accent-filled plate the ground is the stronger contrast, and red is kept for destructive
        // actions elsewhere.
        var badgeCenter = new Vector2(max.X - Ui.Px(1f), min.Y + Ui.Px(1f));
        if (locked)
        {
            var d = Ui.Px(22f);
            drawList.AddCircleFilled(badgeCenter, d * 0.5f, Palette.Bg.U32(), 16);
            drawList.AddCircle(badgeCenter, d * 0.5f, Palette.WithAlpha(this.theme.Accent, 0.55f).U32(), 16, Ui.Px(1.4f));
            var lockGlyph = FontAwesomeIcon.Lock.ToIconString();
            var lockSize = Ui.Measure(this.fonts.Icon, lockGlyph);
            Ui.TextAt(drawList, this.fonts.Icon, badgeCenter - (lockSize * 0.5f), Palette.TextPrimary.U32(), lockGlyph);
        }
        else if (unread > 0)
        {
            var label = unread > 99 ? "99+" : unread.ToString();
            var labelSize = Ui.Measure(this.fonts.Caption, label);
            var badgeH = Ui.Px(20f);
            var badgeW = MathF.Max(badgeH, labelSize.X + Ui.Px(10f));
            var badgeMin = badgeCenter - new Vector2(badgeW * 0.5f, badgeH * 0.5f);
            var badgeMax = badgeMin + new Vector2(badgeW, badgeH);
            drawList.AddRectFilled(badgeMin, badgeMax, Palette.Bg.U32(), badgeH * 0.5f);
            drawList.AddRect(badgeMin, badgeMax, this.theme.Accent.U32(), badgeH * 0.5f, ImDrawFlags.None, Ui.Px(1.4f));
            Ui.TextAt(drawList, this.fonts.Caption, badgeMin + ((new Vector2(badgeW, badgeH) - labelSize) * 0.5f), Palette.TextPrimary.U32(), label);
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
