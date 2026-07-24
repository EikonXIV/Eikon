using System.Linq;
using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Who can see a private album: share it with someone you have messaged, approve or deny the people
// asking, and revoke access. Reached from the album detail's access bar. Owner-only.
internal sealed class AlbumAccessScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly AlbumService albums;
    private readonly Selection selection;
    private readonly InboxService inbox;
    private readonly PhotoService photoSvc;

    private bool openPicker;

    public AlbumAccessScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, AlbumService albums, Selection selection, InboxService inbox, PhotoService photoSvc)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.albums = albums;
        this.selection = selection;
        this.inbox = inbox;
        this.photoSvc = photoSvc;
    }

    public Screen Id => Screen.AlbumAccess;

    public bool Chrome => false;

    public void Draw()
    {
        var albumId = this.selection.AlbumId;
        if (albumId is null)
        {
            this.router.Navigate(Screen.Albums);
            return;
        }

        this.albums.EnsureLoaded();
        this.albums.EnsureRequests();
        var album = this.albums.Mine.FirstOrDefault(a => a.Id == albumId.Value);

        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(16f);
        var headerHeight = Ui.Px(54f);
        this.DrawHeader(avail.X, pad, album?.Name ?? this.selection.AlbumName);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("album_access_body", new Vector2(avail.X, avail.Y - headerHeight)))
        {
            if (body.Success)
            {
                ImGui.Indent(pad);
                var contentWidth = avail.X - (pad * 2f);
                ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));

                if (this.kit.PrimaryButton("##acc_share", "Share with someone", contentWidth))
                    this.openPicker = true;

                var requests = this.albums.Requests.Where(r => r.AlbumId == albumId.Value).ToList();
                if (requests.Count > 0)
                {
                    ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
                    this.kit.SectionLabel($"Requests ({requests.Count})");
                    ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
                    foreach (var request in requests)
                        this.DrawRequestCard(request, contentWidth);
                }

                var grants = this.albums.Grants(albumId.Value);
                ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
                this.kit.SectionLabel($"Has access ({grants.Count})");
                ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
                if (grants.Count == 0)
                    using (this.fonts.Caption.Push())
                    using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
                        ImGui.TextUnformatted("No one yet. Share it above.");
                else
                    foreach (var grantee in grants)
                        this.DrawGranteeRow(albumId.Value, grantee, contentWidth);

                ImGui.Unindent(pad);
            }
        }

        this.DrawPicker(albumId.Value);
    }

    private void DrawHeader(float fullWidth, float pad, string name)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var midY = origin.Y + Ui.Px(27f);

        var back = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, back);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##acc_back", backSize))
            this.router.Navigate(Screen.AlbumDetail);
        Ui.TextAt(drawList, this.fonts.Icon, ImGui.GetItemRectMin(), Palette.TextSecondary.U32(), back);

        var title = $"WHO CAN SEE · {name.ToUpperInvariant()}";
        var titleSize = Ui.Measure(this.fonts.Eyebrow, title);
        Ui.TextAt(drawList, this.fonts.Eyebrow, new Vector2(origin.X + ((fullWidth - titleSize.X) * 0.5f), midY - (titleSize.Y * 0.5f)), Palette.TextSecondary.U32(), title);

        drawList.AddLine(new Vector2(origin.X, origin.Y + Ui.Px(53f)), new Vector2(origin.X + fullWidth, origin.Y + Ui.Px(53f)), Palette.Border.U32(), 1f);
    }

    private void DrawRequestCard(AlbumRequestDto request, float contentWidth)
    {
        var cardH = Ui.Px(96f);
        var pad = Ui.Px(13f);
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, pos + new Vector2(contentWidth, cardH), Palette.Surface1.U32());
        drawList.AddRect(pos, pos + new Vector2(contentWidth, cardH), Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);

        var radius = Ui.Px(18f);
        var center = new Vector2(pos.X + pad + radius, pos.Y + pad + radius);
        this.DrawAvatar(drawList, center, radius, request.Requester.MainPhotoId, request.Requester.DisplayName);
        var textX = center.X + radius + Ui.Px(11f);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(textX, pos.Y + pad), Palette.TextPrimary.U32(), request.Requester.DisplayName);
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(textX, pos.Y + pad + Ui.Px(19f)), Palette.TextMuted.U32(), request.Requester.Verified ? "Verified" : "Wants access");

        var buttonsY = pos.Y + cardH - pad - Ui.Px(32f);
        var half = (contentWidth - (pad * 2f) - Ui.Px(9f)) * 0.5f;
        ImGui.SetCursorScreenPos(new Vector2(pos.X + pad, buttonsY));
        if (this.kit.SecondaryButton("##acc_deny_" + request.Id, "Deny", half))
            this.albums.Deny(request.Id);
        ImGui.SameLine(0f, Ui.Px(9f));
        if (this.kit.PrimaryButton("##acc_approve_" + request.Id, "Approve", half))
            this.albums.Approve(request.Id);

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(contentWidth, cardH));
        ImGui.Dummy(new Vector2(0f, Ui.Px(9f)));
    }

    private void DrawGranteeRow(Guid albumId, AlbumGranteeDto grantee, float contentWidth)
    {
        var rowH = Ui.Px(52f);
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var radius = Ui.Px(17f);
        var center = new Vector2(pos.X + radius, pos.Y + (rowH * 0.5f));
        this.DrawAvatar(drawList, center, radius, grantee.MainPhotoId, grantee.DisplayName);

        var textX = center.X + radius + Ui.Px(11f);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(textX, pos.Y + Ui.Px(9f)), Palette.TextPrimary.U32(), grantee.DisplayName);
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(textX, pos.Y + Ui.Px(28f)), Palette.TextMuted.U32(), SourceLabel(grantee.Source));

        // Revoke pill on the right.
        const string label = "Revoke";
        var ls = Ui.Measure(this.fonts.Caption, label);
        var pillW = ls.X + Ui.Px(20f);
        var pillH = Ui.Px(26f);
        var pillPos = new Vector2(pos.X + contentWidth - pillW, pos.Y + ((rowH - pillH) * 0.5f));
        ImGui.SetCursorScreenPos(pillPos);
        var clicked = ImGui.InvisibleButton("##acc_revoke_" + grantee.UserId, new Vector2(pillW, pillH));
        var hovered = ImGui.IsItemHovered();
        drawList.AddRect(pillPos, pillPos + new Vector2(pillW, pillH), Palette.WithAlpha(Palette.Overlay, hovered ? 0.28f : 0.16f).U32(), 0f, ImDrawFlags.None, 1f);
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(pillPos.X + Ui.Px(10f), pillPos.Y + ((pillH - ls.Y) * 0.5f)), Palette.TextSecondary.U32(), label);
        if (clicked)
            this.albums.Revoke(albumId, grantee.UserId);

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(contentWidth, rowH));
    }

    // The people you can share with: your conversations (you have messaged them). Tapping grants access.
    private void DrawPicker(Guid albumId)
    {
        if (this.openPicker)
        {
            this.openPicker = false;
            this.inbox.EnsureLoaded();
            ImGui.OpenPopup("##acc_picker");
        }

        // Sized to the list rather than a fixed height, so a short list does not leave a tall empty card.
        var people = this.inbox.Conversations.Where(c => !c.IsRequest).ToList();
        var boxPad = Ui.Px(16f);
        var rowHeight = Ui.Px(48f);
        var titleHeight = Ui.Measure(this.fonts.Title, "Share with").Y;
        var labelHeight = Ui.Measure(this.fonts.Eyebrow, "X").Y;
        var listHeight = people.Count == 0
            ? Ui.Px(52f)
            : MathF.Min(people.Count * rowHeight, Ui.Px(240f));
        var height = (boxPad * 2f) + titleHeight + Ui.Px(8f) + labelHeight + Ui.Px(8f) + listHeight + Ui.Px(14f) + Ui.Px(38f);

        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + (ImGui.GetWindowSize() * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(Ui.Px(300f), height));
        var open = true;
        using (ImRaii.PushColor(ImGuiCol.PopupBg, Palette.Surface1))
        using (ImRaii.PushColor(ImGuiCol.Border, Palette.Border))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(boxPad, boxPad)))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 0f))
        using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f))
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
        {
            if (!ImGui.BeginPopupModal("##acc_picker", ref open, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
                return;

            var width = ImGui.GetContentRegionAvail().X;
            Ui.CenteredText(width, this.fonts.Title, Palette.TextPrimary, "Share with");
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            this.kit.SectionLabel("People you've messaged");
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));

            var granted = this.albums.Grants(albumId).Select(g => g.UserId).ToHashSet();
            using (ImRaii.Child("##acc_picker_list", new Vector2(width, listHeight)))
            {
                if (people.Count == 0)
                    using (this.fonts.Caption.Push())
                    using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
                        ImGui.TextWrapped("You can only share with people you have messaged.");
                foreach (var person in people)
                    this.DrawPickerRow(albumId, person, granted.Contains(person.UserId), width);
            }

            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
            if (this.kit.SecondaryButton("##acc_picker_close", "Close", width))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
    }

    private void DrawPickerRow(Guid albumId, ConversationSummaryDto person, bool alreadyGranted, float width)
    {
        var rowH = Ui.Px(48f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##acc_pick_" + person.UserId, new Vector2(width, rowH));
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();
        if (hovered && !alreadyGranted)
            drawList.AddRectFilled(pos, pos + new Vector2(width, rowH), Palette.WithAlpha(Palette.Overlay, 0.045f).U32());

        var radius = Ui.Px(16f);
        var center = new Vector2(pos.X + radius, pos.Y + (rowH * 0.5f));
        this.DrawAvatar(drawList, center, radius, person.MainPhotoId, person.DisplayName);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(center.X + radius + Ui.Px(11f), center.Y - (Ui.Measure(this.fonts.Body, person.DisplayName).Y * 0.5f)), Palette.TextPrimary.U32(), person.DisplayName);

        if (alreadyGranted)
        {
            var check = FontAwesomeIcon.Check.ToIconString();
            var cs = Ui.Measure(this.fonts.Icon, check);
            Ui.TextAt(drawList, this.fonts.Icon, new Vector2(pos.X + width - cs.X - Ui.Px(4f), center.Y - (cs.Y * 0.5f)), this.theme.Accent.U32(), check);
        }

        if (clicked && !alreadyGranted)
        {
            this.albums.Grant(albumId, person.UserId, "profile");
            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawAvatar(ImDrawListPtr drawList, Vector2 center, float radius, Guid? photoId, string name)
    {
        var tex = photoId is { } id ? this.photoSvc.Texture(id) : null;
        if (tex is { Width: > 0, Height: > 0 })
        {
            var (uvMin, uvMax) = Ui.CoverUv(tex.Width, tex.Height, 1f);
            drawList.AddImageRounded(tex.Handle, center - new Vector2(radius, radius), center + new Vector2(radius, radius), uvMin, uvMax, 0xFFFFFFFFu, radius);
            return;
        }
        drawList.AddCircleFilled(center, radius, Palette.Surface2.U32(), 24);
        var initial = name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
        var isz = Ui.Measure(this.fonts.Body, initial);
        Ui.TextAt(drawList, this.fonts.Body, center - (isz * 0.5f), Palette.TextSecondary.U32(), initial);
    }

    private static string SourceLabel(AlbumGrantSourceEnum source) => source switch
    {
        AlbumGrantSourceEnum.Chat => "shared in chat",
        AlbumGrantSourceEnum.Request => "approved request",
        _ => "shared from profile",
    };
}
