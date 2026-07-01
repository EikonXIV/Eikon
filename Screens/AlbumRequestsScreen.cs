using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Access requests: everyone waiting on one of the member's albums, across all albums, each a card with
// the requester, which album, and approve or deny. Reached from the albums manager. Owner-only.
internal sealed class AlbumRequestsScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly AlbumService albums;

    public AlbumRequestsScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, AlbumService albums)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.albums = albums;
    }

    public Screen Id => Screen.AlbumRequests;

    public bool Chrome => false;

    public void Draw()
    {
        this.albums.EnsureRequests();

        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(16f);
        var headerHeight = Ui.Px(54f);
        this.DrawHeader(avail.X, pad);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("album_requests_body", new Vector2(avail.X, avail.Y - headerHeight)))
        {
            if (body.Success)
            {
                ImGui.Indent(pad);
                var contentWidth = avail.X - (pad * 2f);
                ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));

                var requests = this.albums.Requests;
                if (requests.Count == 0)
                {
                    ImGui.Dummy(new Vector2(0f, Ui.Px(48f)));
                    this.kit.EmptyState(FontAwesomeIcon.UserCheck.ToIconString(), "No requests",
                        "When someone asks to see an album, they show up here.", contentWidth);
                }
                else
                {
                    foreach (var request in requests)
                        this.DrawCard(request, contentWidth);
                }

                ImGui.Unindent(pad);
            }
        }
    }

    private void DrawHeader(float fullWidth, float pad)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var midY = origin.Y + Ui.Px(27f);

        var back = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, back);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##areq_back", backSize))
            this.router.Navigate(Screen.Albums);
        Ui.TextAt(drawList, this.fonts.Icon, ImGui.GetItemRectMin(), Palette.TextSecondary.U32(), back);

        const string title = "Access requests";
        var titleSize = Ui.Measure(this.fonts.Body, title);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(origin.X + ((fullWidth - titleSize.X) * 0.5f), midY - (titleSize.Y * 0.5f)), Palette.TextPrimary.U32(), title);

        drawList.AddLine(new Vector2(origin.X, origin.Y + Ui.Px(53f)), new Vector2(origin.X + fullWidth, origin.Y + Ui.Px(53f)), Palette.Border.U32(), 1f);
    }

    private void DrawCard(AlbumRequestDto request, float contentWidth)
    {
        var cardH = Ui.Px(128f);
        var pad = Ui.Px(14f);
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, pos + new Vector2(contentWidth, cardH), Palette.Surface1.U32(), Ui.Px(14f));
        drawList.AddRect(pos, pos + new Vector2(contentWidth, cardH), Palette.Border.U32(), Ui.Px(14f), ImDrawFlags.None, 1f);

        // Header: avatar, name, verified sub, time.
        var radius = Ui.Px(20f);
        var avatarCenter = new Vector2(pos.X + pad + radius, pos.Y + pad + radius);
        drawList.AddCircleFilled(avatarCenter, radius, Palette.Surface2.U32(), 24);
        var initial = Initial(request.Requester.DisplayName);
        var initialSize = Ui.Measure(this.fonts.Body, initial);
        Ui.TextAt(drawList, this.fonts.Body, avatarCenter - (initialSize * 0.5f), Palette.TextSecondary.U32(), initial);

        var textX = avatarCenter.X + radius + Ui.Px(11f);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(textX, pos.Y + pad), Palette.TextPrimary.U32(), request.Requester.DisplayName);
        var sub = request.Requester.Verified ? "Verified" : "Wants access";
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(textX, pos.Y + pad + Ui.Px(19f)), Palette.TextMuted.U32(), sub);
        var time = Ago(request.CreatedAt);
        var timeSize = Ui.Measure(this.fonts.Caption, time);
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(pos.X + contentWidth - pad - timeSize.X, pos.Y + pad + Ui.Px(2f)), Palette.TextMuted.U32(), time);

        // "wants access to <album>"
        var lineY = pos.Y + pad + Ui.Px(44f);
        const string prefix = "wants access to ";
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(pos.X + pad, lineY + Ui.Px(3f)), Palette.TextSecondary.U32(), prefix);
        var prefixW = Ui.Measure(this.fonts.Caption, prefix).X;
        var chipName = request.AlbumName;
        var chipNameW = Ui.Measure(this.fonts.Caption, chipName).X;
        var chipX = pos.X + pad + prefixW;
        drawList.AddRectFilled(new Vector2(chipX, lineY), new Vector2(chipX + chipNameW + Ui.Px(16f), lineY + Ui.Px(22f)), Palette.Surface2.U32(), Ui.Px(8f));
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(chipX + Ui.Px(8f), lineY + Ui.Px(3f)), Palette.TextPrimary.U32(), chipName);

        // Approve / Deny row.
        var buttonsY = pos.Y + cardH - pad - Ui.Px(34f);
        var half = (contentWidth - (pad * 2f) - Ui.Px(9f)) * 0.5f;
        ImGui.SetCursorScreenPos(new Vector2(pos.X + pad, buttonsY));
        if (this.kit.SecondaryButton("##areq_deny_" + request.Id, "Deny", half))
            this.albums.Deny(request.Id);
        ImGui.SameLine(0f, Ui.Px(9f));
        if (this.kit.PrimaryButton("##areq_approve_" + request.Id, "Approve", half))
            this.albums.Approve(request.Id);

        // Reserve the whole card height for layout and advance to the next card.
        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(contentWidth, cardH));
        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
    }

    private static string Initial(string name) => name.Length > 0 ? name[..1].ToUpperInvariant() : "?";

    private static string Ago(DateTimeOffset at)
    {
        var d = DateTimeOffset.UtcNow - at;
        if (d.TotalMinutes < 1) return "now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h";
        if (d.TotalDays < 7) return $"{(int)d.TotalDays}d";
        return at.LocalDateTime.ToString("MMM d");
    }
}
