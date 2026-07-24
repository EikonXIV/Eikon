using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Albums manager: the member's own collections, reached from My Profile. A single "access requests"
// entry when people are waiting, then the album grid with a "new album" card. Tapping an album opens
// its detail; the plus (or the card) opens the new-album dialog. Owner-only screen.
internal sealed class AlbumsScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly AlbumService albums;
    private readonly Selection selection;
    private readonly WindowController windowController;

    private bool openNew;
    private string newName = string.Empty;
    private int newVisibility;   // 0 = private, 1 = public

    public AlbumsScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, AlbumService albums, Selection selection, WindowController windowController)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.albums = albums;
        this.selection = selection;
        this.windowController = windowController;
    }

    public Screen Id => Screen.Albums;

    public bool Chrome => false;

    public void Draw()
    {
        this.albums.EnsureLoaded();
        this.albums.EnsureRequests();

        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(16f);
        var headerHeight = Ui.Px(54f);
        this.DrawHeader(avail.X, pad);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("albums_body", new Vector2(avail.X, avail.Y - headerHeight)))
        {
            if (body.Success)
            {
                ImGui.Indent(pad);
                var contentWidth = avail.X - (pad * 2f);
                ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));

                var requests = this.albums.Requests;
                if (requests.Count > 0)
                {
                    this.DrawRequestsEntry(requests.Count, contentWidth);
                    ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
                }

                if (this.albums.Loaded && this.albums.Mine.Count == 0 && requests.Count == 0)
                {
                    ImGui.Dummy(new Vector2(0f, Ui.Px(40f)));
                    this.kit.EmptyState(FontAwesomeIcon.LockOpen.ToIconString(), "No albums yet",
                        "Create collections to unlock for people you choose, or make public on your profile.", contentWidth);
                    var w = Ui.Px(160f);
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((contentWidth - w) * 0.5f));
                    if (this.kit.PrimaryButton("##albums_create", "Create album", w))
                        this.OpenNew();
                }
                else
                {
                    this.kit.SectionLabel("Your albums");
                    ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
                    this.DrawGrid(contentWidth);
                }

                ImGui.Unindent(pad);
            }
        }

        this.DrawNewDialog();
    }

    private void DrawHeader(float fullWidth, float pad)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var midY = origin.Y + Ui.Px(27f);

        var back = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, back);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##albums_back", backSize))
            this.router.Navigate(Screen.MyProfile);
        Ui.TextAt(drawList, this.fonts.Icon, ImGui.GetItemRectMin(), Palette.TextSecondary.U32(), back);

        const string title = "Albums";
        var titleSize = Ui.Measure(this.fonts.Body, title);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(origin.X + ((fullWidth - titleSize.X) * 0.5f), midY - (titleSize.Y * 0.5f)), Palette.TextPrimary.U32(), title);

        // Right side, corner inward: minimize (collapses to the orb, same as the main title bar), a
        // hairline, then new-album, so the window control reads apart from the screen action.
        var btn = Ui.Px(30f);
        var minTL = new Vector2(origin.X + fullWidth - pad - btn, midY - (btn * 0.5f));
        if (this.kit.HeaderIconButton(drawList, "##albums_min", FontAwesomeIcon.Minus.ToIconString(), minTL, btn))
            this.windowController.Minimize();
        var divX = minTL.X - Ui.Px(8f);
        drawList.AddLine(new Vector2(divX, midY - Ui.Px(9f)), new Vector2(divX, midY + Ui.Px(9f)), Palette.Border.U32(), 1f);
        var plusTL = new Vector2(divX - Ui.Px(8f) - btn, midY - (btn * 0.5f));
        if (this.kit.HeaderIconButton(drawList, "##albums_new", FontAwesomeIcon.Plus.ToIconString(), plusTL, btn))
            this.OpenNew();

        drawList.AddLine(new Vector2(origin.X, origin.Y + Ui.Px(53f)), new Vector2(origin.X + fullWidth, origin.Y + Ui.Px(53f)), Palette.Border.U32(), 1f);
    }

    private void DrawRequestsEntry(int count, float contentWidth)
    {
        var height = Ui.Px(60f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##albums_requests", new Vector2(contentWidth, height));
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, pos + new Vector2(contentWidth, height), Palette.Surface1.U32(), Ui.Px(13f));
        drawList.AddRect(pos, pos + new Vector2(contentWidth, height), Palette.Border.U32(), Ui.Px(13f), ImDrawFlags.None, 1f);

        var cx = pos.X + Ui.Px(13f) + Ui.Px(19f);
        var cy = pos.Y + (height * 0.5f);
        drawList.AddCircleFilled(new Vector2(cx, cy), Ui.Px(19f), this.theme.AccentTint.U32(), 24);
        var glyph = FontAwesomeIcon.UserPlus.ToIconString();
        var gs = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(cx - (gs.X * 0.5f), cy - (gs.Y * 0.5f)), this.theme.AccentText.U32(), glyph);

        var textX = cx + Ui.Px(19f) + Ui.Px(12f);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(textX, pos.Y + Ui.Px(13f)), Palette.TextPrimary.U32(), "Access requests");
        var sub = count == 1 ? "1 person wants in" : $"{count} people want in";
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(textX, pos.Y + Ui.Px(33f)), Palette.TextMuted.U32(), sub);

        var chevron = FontAwesomeIcon.ChevronRight.ToIconString();
        var chs = Ui.Measure(this.fonts.Icon, chevron);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(pos.X + contentWidth - Ui.Px(14f) - chs.X, cy - (chs.Y * 0.5f)), Palette.TextMuted.U32(), chevron);

        if (clicked)
            this.router.Navigate(Screen.AlbumRequests);
    }

    private void DrawGrid(float contentWidth)
    {
        const int columns = 2;
        var gap = Ui.Px(11f);
        var cardWidth = (contentWidth - (gap * (columns - 1))) / columns;

        var col = 0;
        foreach (var album in this.albums.Mine)
        {
            if (col % columns != 0)
                ImGui.SameLine(0f, gap);
            this.DrawCard(album, cardWidth);
            col++;
        }

        if (col % columns != 0)
            ImGui.SameLine(0f, gap);
        this.DrawNewCard(cardWidth);
    }

    private void DrawCard(AlbumDto album, float width)
    {
        var coverH = Ui.Px(84f);
        var cardH = coverH + Ui.Px(46f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##album_" + album.Id, new Vector2(width, cardH));
        var drawList = ImGui.GetWindowDrawList();
        var rounding = Ui.Px(13f);

        drawList.AddRectFilled(pos, pos + new Vector2(width, cardH), Palette.Surface1.U32(), rounding);
        drawList.AddRect(pos, pos + new Vector2(width, cardH), Palette.Border.U32(), rounding, ImDrawFlags.None, 1f);

        // Cover: the album's cover photo if set, else a neutral placeholder.
        var coverMax = new Vector2(pos.X + width, pos.Y + coverH);
        var tex = album.CoverPhotoId is { } cover ? this.albums.Texture(album.Id, cover) : null;
        if (tex is { Width: > 0, Height: > 0 })
        {
            var (uvMin, uvMax) = Ui.CoverUv(tex.Width, tex.Height, width / coverH);
            drawList.AddImageRounded(tex.Handle, pos, coverMax, uvMin, uvMax, 0xFFFFFFFFu, rounding, ImDrawFlags.RoundCornersTop);
        }
        else
        {
            drawList.AddRectFilled(pos, coverMax, Palette.Surface2.U32(), rounding, ImDrawFlags.RoundCornersTop);
            var glyph = FontAwesomeIcon.Image.ToIconString();
            var gs = Ui.Measure(this.fonts.Icon, glyph);
            Ui.TextAt(drawList, this.fonts.Icon, new Vector2(pos.X + ((width - gs.X) * 0.5f), pos.Y + ((coverH - gs.Y) * 0.5f)), Palette.TextMuted.U32(), glyph);
        }

        // Private albums carry a lock chip on the cover.
        if (album.Visibility == AlbumVisibilityEnum.Private)
        {
            var lockG = FontAwesomeIcon.Lock.ToIconString();
            var ls = Ui.Measure(this.fonts.Icon, lockG);
            var radius = (MathF.Max(ls.X, ls.Y) * 0.5f) + Ui.Px(4f);
            var center = new Vector2(pos.X + width - radius - Ui.Px(6f), pos.Y + radius + Ui.Px(6f));
            drawList.AddCircleFilled(center, radius, Palette.WithAlpha(Palette.Bg, 0.72f).U32(), 16);
            Ui.TextAt(drawList, this.fonts.Icon, center - (ls * 0.5f), Palette.TextSecondary.U32(), lockG);
        }

        Ui.TextAt(drawList, this.fonts.Body, new Vector2(pos.X + Ui.Px(11f), pos.Y + coverH + Ui.Px(8f)), Palette.TextPrimary.U32(), this.Fit(album.Name, width - Ui.Px(22f)));
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(pos.X + Ui.Px(11f), pos.Y + coverH + Ui.Px(26f)), Palette.TextMuted.U32(), Meta(album));

        if (clicked)
        {
            this.selection.AlbumId = album.Id;
            this.selection.AlbumName = album.Name;
            this.selection.AlbumReturn = Screen.Albums;
            this.router.Navigate(Screen.AlbumDetail);
        }
    }

    private const int MaxAlbums = 10;   // mirrors the server cap in albums/routes.ts

    private void DrawNewCard(float width)
    {
        var atCap = this.albums.Mine.Count >= MaxAlbums;
        var cardH = Ui.Px(84f) + Ui.Px(46f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##album_new_card", new Vector2(width, cardH));
        var drawList = ImGui.GetWindowDrawList();
        var tint = atCap ? Palette.TextMuted : Palette.TextSecondary;
        drawList.AddRect(pos, pos + new Vector2(width, cardH), Palette.WithAlpha(Palette.Overlay, atCap ? 0.10f : 0.18f).U32(), Ui.Px(13f), ImDrawFlags.None, 1f);
        var center = new Vector2(pos.X + (width * 0.5f), pos.Y + (cardH * 0.5f));
        var glyph = (atCap ? FontAwesomeIcon.Lock : FontAwesomeIcon.Plus).ToIconString();
        var gs = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(center.X - (gs.X * 0.5f), center.Y - gs.Y), tint.U32(), glyph);
        var label = atCap ? "Album limit" : "New album";
        var lls = Ui.Measure(this.fonts.Caption, label);
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(center.X - (lls.X * 0.5f), center.Y + Ui.Px(4f)), tint.U32(), label);
        if (clicked)
            this.OpenNew();
    }

    private void OpenNew()
    {
        this.newName = string.Empty;
        this.newVisibility = 0;
        this.openNew = true;
    }

    private void DrawNewDialog()
    {
        if (this.openNew)
        {
            this.openNew = false;
            ImGui.OpenPopup("##newalbum");
        }

        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + (ImGui.GetWindowSize() * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize;
        var open = true;

        using (ImRaii.PushColor(ImGuiCol.PopupBg, Palette.Surface1))
        using (ImRaii.PushColor(ImGuiCol.Border, Palette.Border))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(Ui.Px(18f), Ui.Px(18f))))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, Ui.Px(16f)))
        using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f))
        {
            if (!ImGui.BeginPopupModal("##newalbum", ref open, flags))
                return;

            var width = Ui.Px(288f);

            // At the cap the dialog explains rather than creating; both the header + and the new card
            // route here, so this is the one place that has to say it.
            if (this.albums.Mine.Count >= MaxAlbums)
            {
                Ui.CenteredText(width, this.fonts.Title, Palette.TextPrimary, "Album limit reached");
                ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
                using (this.fonts.Caption.Push())
                using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
                    ImGui.TextWrapped($"You can keep up to {MaxAlbums} albums. Delete one to make room for a new one.");
                ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
                if (this.kit.SecondaryButton("##new_close", "Close", width))
                    ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
                return;
            }

            Ui.CenteredText(width, this.fonts.Title, Palette.TextPrimary, "New album");
            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
            this.kit.TextField("##new_name", ref this.newName, "Album name", width);

            ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
            this.kit.SectionLabel("Who can see it");
            ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
            this.newVisibility = this.kit.Segmented("##new_vis", new[] { "Private", "Public" }, this.newVisibility, width);
            ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
                ImGui.TextWrapped(this.newVisibility == 0
                    ? "Only people you unlock it for. You can change this any time."
                    : "Anyone who can see your profile. You can change this any time.");

            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
            var half = (width - Ui.Px(10f)) * 0.5f;
            if (this.kit.SecondaryButton("##new_cancel", "Cancel", half))
                ImGui.CloseCurrentPopup();
            ImGui.SameLine(0f, Ui.Px(10f));
            var canCreate = this.newName.Trim().Length > 0;
            if (this.kit.PrimaryButton("##new_create", "Create", half) && canCreate)
            {
                this.albums.Create(this.newName.Trim(), this.newVisibility == 0 ? "private" : "public");
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private static string Meta(AlbumDto album)
    {
        var photos = album.PhotoCount == 1 ? "1 photo" : $"{album.PhotoCount} photos";
        if (album.Visibility == AlbumVisibilityEnum.Public)
            return photos + " · public";
        if (album.SharedCount > 0)
            return photos + $" · shared with {album.SharedCount}";
        return photos + " · private";
    }

    private string Fit(string text, float maxWidth)
    {
        if (maxWidth <= 0f || Ui.Measure(this.fonts.Body, text).X <= maxWidth)
            return text;
        const string ellipsis = "...";
        var ew = Ui.Measure(this.fonts.Body, ellipsis).X;
        var n = text.Length;
        while (n > 0 && Ui.Measure(this.fonts.Body, text[..n]).X + ew > maxWidth)
            n--;
        return text[..n].TrimEnd() + ellipsis;
    }
}
