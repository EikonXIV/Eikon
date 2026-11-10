using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Settings > Albums > Access requests. Everyone waiting on one of your private albums, across all of
// them, each with Approve or Deny. Isolated from the albums manager's requests screen; back always
// returns to Settings.
internal sealed class AlbumRequestsSettingsScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly AlbumService albums;
    private readonly PhotoService photoSvc;

    public AlbumRequestsSettingsScreen(ScreenRouter router, Kit kit, UiFonts fonts, AlbumService albums, PhotoService photoSvc)
    {
        this.router = router;
        this.kit = kit;
        this.fonts = fonts;
        this.albums = albums;
        this.photoSvc = photoSvc;
    }

    public Screen Id => Screen.AlbumRequestsSettings;

    public bool Chrome => false;

    public void Draw()
    {
        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(16f);
        var headerHeight = Ui.Px(52f);
        var contentWidth = avail.X - (pad * 2f);

        this.DrawHeader(avail.X, pad, headerHeight);

        this.albums.EnsureRequests();
        var requests = this.albums.Requests;

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("album_requests_settings_body", new Vector2(avail.X, avail.Y - headerHeight), false, ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            if (body.Success)
            {
                ImGui.Indent(pad);
                if (requests.Count == 0)
                {
                    this.DrawEmptyOrLoading(contentWidth);
                }
                else
                {
                    ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
                    using (this.fonts.Caption.Push())
                    using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
                    {
                        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentWidth);
                        Ui.TextWrapped(Loc.T("albumRequestsSettings.hint"));
                        ImGui.PopTextWrapPos();
                    }
                    ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
                    foreach (var request in requests)
                        this.DrawCard(request, contentWidth);
                }
                ImGui.Unindent(pad);
            }
        }
    }

    private void DrawEmptyOrLoading(float contentWidth)
    {
        if (!this.albums.RequestsLoaded)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(40f)));
            Ui.CenteredText(contentWidth, this.fonts.Caption, Palette.TextMuted, Loc.T("albumRequestsSettings.loading"));
            return;
        }
        ImGui.Dummy(new Vector2(0f, Ui.Px(60f)));
        this.kit.EmptyState(FontAwesomeIcon.UserCheck.ToIconString(), Loc.T("albumRequestsSettings.empty.title"), Loc.T("albumRequestsSettings.empty.body"), contentWidth);
    }

    private void DrawHeader(float fullWidth, float pad, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var midY = origin.Y + (height * 0.5f);

        var backGlyph = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, backGlyph);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##sreq_back", backSize))
            this.router.Navigate(Screen.Settings);
        Ui.TextAt(drawList, this.fonts.Icon, ImGui.GetItemRectMin(), Palette.TextSecondary.U32(), backGlyph);

        var title = Loc.T("albumRequestsSettings.title");
        var titleSize = Ui.Measure(this.fonts.Body, title);
        Ui.TextAt(drawList, this.fonts.Body,
            new Vector2(origin.X + pad + backSize.X + Ui.Px(12f), midY - (titleSize.Y * 0.5f)),
            Palette.TextPrimary.U32(), title);

        drawList.AddLine(new Vector2(origin.X, origin.Y + height), new Vector2(origin.X + fullWidth, origin.Y + height), Palette.Border.U32(), 1f);
    }

    private void DrawCard(AlbumRequestDto request, float contentWidth)
    {
        var cardH = Ui.Px(138f);
        var pad = Ui.Px(13f);
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, pos + new Vector2(contentWidth, cardH), Palette.Surface1.U32(), Ui.Px(12f));
        drawList.AddRect(pos, pos + new Vector2(contentWidth, cardH), Palette.Border.U32(), Ui.Px(12f), ImDrawFlags.None, 1f);

        var radius = Ui.Px(18f);
        var center = new Vector2(pos.X + pad + radius, pos.Y + pad + radius);
        this.DrawAvatar(drawList, center, radius, request.Requester.MainPhotoId, Initial(request.Requester.DisplayName));

        var textX = center.X + radius + Ui.Px(11f);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(textX, pos.Y + pad), Palette.TextPrimary.U32(), request.Requester.DisplayName);
        var sub = Loc.T(request.Requester.Verified ? "albumRequestsSettings.card.verified" : "albumRequestsSettings.card.wantsAccess");
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(textX, pos.Y + pad + Ui.Px(19f)), Palette.TextMuted.U32(), sub);

        var time = Ago(request.CreatedAt);
        var timeSize = Ui.Measure(this.fonts.Caption, time);
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(pos.X + contentWidth - pad - timeSize.X, pos.Y + pad + Ui.Px(2f)), Palette.TextMuted.U32(), time);

        var lineY = pos.Y + pad + Ui.Px(42f);
        var line = Loc.T("albumRequestsSettings.card.line");
        var slot = line.IndexOf("{0}", StringComparison.Ordinal);
        var prefix = slot < 0 ? line : line[..slot];
        var suffix = slot < 0 ? string.Empty : line[(slot + 3)..];
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(pos.X + pad, lineY + Ui.Px(3f)), Palette.TextSecondary.U32(), prefix);
        var prefixW = Ui.Measure(this.fonts.Caption, prefix).X;
        var chipX = pos.X + pad + prefixW;
        var chipNameW = Ui.Measure(this.fonts.Caption, request.AlbumName).X;
        drawList.AddRectFilled(new Vector2(chipX, lineY), new Vector2(chipX + chipNameW + Ui.Px(16f), lineY + Ui.Px(22f)), Palette.Surface2.U32(), Ui.Px(8f));
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(chipX + Ui.Px(8f), lineY + Ui.Px(3f)), Palette.TextPrimary.U32(), request.AlbumName);
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(chipX + chipNameW + Ui.Px(16f), lineY + Ui.Px(3f)), Palette.TextSecondary.U32(), suffix);

        var buttonsY = pos.Y + cardH - pad - Ui.Px(38f);
        var half = (contentWidth - (pad * 2f) - Ui.Px(9f)) * 0.5f;
        ImGui.SetCursorScreenPos(new Vector2(pos.X + pad, buttonsY));
        if (this.kit.SecondaryButton("##sreq_deny_" + request.Id, Loc.T("albumRequestsSettings.card.deny"), half))
            this.albums.Deny(request.Id);
        ImGui.SameLine(0f, Ui.Px(9f));
        if (this.kit.PrimaryButton("##sreq_approve_" + request.Id, Loc.T("albumRequestsSettings.card.approve"), half))
            this.albums.Approve(request.Id);

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(contentWidth, cardH));
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
    }

    private void DrawAvatar(ImDrawListPtr drawList, Vector2 center, float radius, Guid? photoId, string initial)
    {
        var texture = photoId is { } id ? this.photoSvc.Texture(id) : null;
        if (texture is { Width: > 0, Height: > 0 })
        {
            var (uvMin, uvMax) = Ui.CoverUv(texture.Width, texture.Height, 1f);
            drawList.AddImageRounded(texture.Handle, center - new Vector2(radius, radius), center + new Vector2(radius, radius), uvMin, uvMax, 0xFFFFFFFFu, radius);
            return;
        }
        drawList.AddCircleFilled(center, radius, Palette.Surface2.U32(), 24);
        var initialSize = Ui.Measure(this.fonts.Body, initial);
        Ui.TextAt(drawList, this.fonts.Body, center - (initialSize * 0.5f), Palette.TextSecondary.U32(), initial);
    }

    private static string Initial(string name) => name.Length > 0 ? Loc.Upper(name[..1]) : "?";

    private static string Ago(DateTimeOffset at)
    {
        var d = DateTimeOffset.UtcNow - at;
        if (d.TotalMinutes < 1) return Loc.T("albumRequestsSettings.ago.now");
        if (d.TotalMinutes < 60) return Loc.T("albumRequestsSettings.ago.minutes", (int)d.TotalMinutes);
        if (d.TotalHours < 24) return Loc.T("albumRequestsSettings.ago.hours", (int)d.TotalHours);
        if (d.TotalDays < 7) return Loc.T("albumRequestsSettings.ago.days", (int)d.TotalDays);
        return at.LocalDateTime.ToString("MMM d", Loc.Culture);
    }
}
