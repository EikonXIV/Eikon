using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Eikon.Config;
using Eikon.Notifications;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Windows;

// Floating notification toasts. Always "open" but only drawn when there are toasts (PreOpenCheck ticks
// the service and gates drawing). Borderless and transparent, positioned at the user's chosen corner;
// each toast is a clickable card that opens that conversation.
internal sealed class NotificationWindow : Window
{
    private readonly NotificationService notifications;
    private readonly Configuration config;
    private readonly ThemeService theme;
    private readonly UiFonts fonts;

    public NotificationWindow(NotificationService notifications, Configuration config, ThemeService theme, UiFonts fonts)
        : base("Eikon##notifications",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.notifications = notifications;
        this.config = config;
        this.theme = theme;
        this.fonts = fonts;
        this.IsOpen = true;            // DrawConditions decides whether anything actually draws
        this.RespectCloseHotkey = false;
        this.DisableWindowSounds = true;
    }

    private bool hasToasts;

    // Runs every frame: tick the service (drain/coalesce/prune/sound) and remember whether to draw.
    public override void PreOpenCheck() => this.hasToasts = this.notifications.Tick();

    public override bool DrawConditions() => this.hasToasts;

    public override void PreDraw()
    {
        var vp = ImGui.GetMainViewport();
        var margin = Ui.Px(16f);
        var corner = Math.Clamp(this.config.NotificationCorner, 0, 5);
        var vert = corner / 3;    // 0 top, 1 bottom
        var horiz = corner % 3;   // 0 left, 1 center, 2 right
        var x = horiz == 0 ? vp.WorkPos.X + margin
            : horiz == 1 ? vp.WorkPos.X + (vp.WorkSize.X * 0.5f)
            : vp.WorkPos.X + vp.WorkSize.X - margin;
        var y = vert == 0 ? vp.WorkPos.Y + margin : vp.WorkPos.Y + vp.WorkSize.Y - margin;
        var pivot = new Vector2(horiz == 0 ? 0f : horiz == 1 ? 0.5f : 1f, vert == 0 ? 0f : 1f);
        ImGui.SetNextWindowPos(new Vector2(x, y), ImGuiCond.Always, pivot);
    }

    public override void Draw()
    {
        var width = Ui.Px(252f);
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Ui.Px(8f))))
        {
            // Snapshot so a click (which mutates the toast list) doesn't disturb iteration.
            foreach (var toast in this.notifications.Toasts.ToList())
                this.DrawToast(toast, width);
        }
    }

    private void DrawToast(NotificationToast toast, float width)
    {
        var height = Ui.Px(56f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##ntf_{toast.Peer}_{(int)toast.Kind}_{toast.AlbumId}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();
        var rounding = Ui.Px(12f);

        // soft drop shadow (outside the window clip)
        var vp = ImGui.GetMainViewport();
        drawList.PushClipRect(vp.Pos, vp.Pos + vp.Size, false);
        for (var i = 4; i >= 1; i--)
        {
            var s = i * Ui.Px(1.6f);
            drawList.AddRectFilled(new Vector2(pos.X - s, pos.Y - s + Ui.Px(2f)), new Vector2(pos.X + width + s, pos.Y + height + s + Ui.Px(2f)), new Vector4(0f, 0f, 0f, 0.05f * (5 - i)).U32(), rounding + s);
        }
        drawList.PopClipRect();

        drawList.AddRectFilled(pos, pos + new Vector2(width, height), (hovered ? Palette.Surface2 : Palette.Surface1).U32(), rounding);
        drawList.AddRect(pos, pos + new Vector2(width, height), Palette.Border.U32(), rounding, ImDrawFlags.None, 1f);

        var radius = Ui.Px(19f);
        var center = new Vector2(pos.X + Ui.Px(13f) + radius, pos.Y + (height * 0.5f));
        drawList.AddCircleFilled(center, radius, Palette.Surface2.U32(), 24);
        var initial = toast.Name.Length > 0 ? toast.Name[..1].ToUpperInvariant() : "?";
        var initialSize = Ui.Measure(this.fonts.Body, initial);
        Ui.TextAt(drawList, this.fonts.Body, center - (initialSize * 0.5f), Palette.TextSecondary.U32(), initial);

        // count badge on the avatar when coalesced
        if (toast.Count > 1)
        {
            var label = toast.Count > 99 ? "99+" : toast.Count.ToString();
            var labelSize = Ui.Measure(this.fonts.Caption, label);
            var bh = Ui.Px(17f);
            var bw = MathF.Max(bh, labelSize.X + Ui.Px(7f));
            var bc = center + new Vector2(radius - Ui.Px(2f), -(radius - Ui.Px(2f)));
            var bmin = bc - new Vector2(bw * 0.5f, bh * 0.5f);
            drawList.AddRectFilled(bmin, bmin + new Vector2(bw, bh), Palette.Danger.U32(), bh * 0.5f);
            drawList.AddRect(bmin, bmin + new Vector2(bw, bh), Palette.Surface1.U32(), bh * 0.5f, ImDrawFlags.None, Ui.Px(2f));
            Ui.TextAt(drawList, this.fonts.Caption, bmin + ((new Vector2(bw, bh) - labelSize) * 0.5f), Palette.White.U32(), label);
        }

        var textX = pos.X + Ui.Px(13f) + (radius * 2f) + Ui.Px(11f);
        var name = this.Fit(toast.Name, (pos.X + width) - textX - Ui.Px(12f));
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(textX, pos.Y + Ui.Px(11f)), Palette.TextPrimary.U32(), name);
        var sub = toast.Subtitle ?? (toast.Count > 1 ? $"{toast.Count} new messages" : "New message");
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(textX, pos.Y + Ui.Px(31f)), Palette.TextSecondary.U32(), sub);

        if (clicked)
            this.notifications.Open(toast);
    }

    private string Fit(string text, float maxWidth)
    {
        if (maxWidth <= 0f || Ui.Measure(this.fonts.Body, text).X <= maxWidth)
            return text;
        const string ellipsis = "...";
        var ellipsisWidth = Ui.Measure(this.fonts.Body, ellipsis).X;
        var n = text.Length;
        while (n > 0 && Ui.Measure(this.fonts.Body, text[..n]).X + ellipsisWidth > maxWidth)
            n--;
        return text[..n].TrimEnd() + ellipsis;
    }
}
