using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Albums manager: the member's own collections, reached from My Profile. An editorial list - one row
// per album carrying its cover, meta, a visibility toggle and a delete - over an inline new-album field,
// so creating one never leaves the screen. A single access-requests row sits on top when people are
// waiting. Owner-only screen.
internal sealed class AlbumsScreen : IScreen
{
    private const int MaxAlbums = 10;   // mirrors the server cap in albums/routes.ts

    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly AlbumService albums;
    private readonly Selection selection;

    private string newName = string.Empty;
    private bool openDelete;
    private Guid? deleteId;
    private string deleteName = string.Empty;

    public AlbumsScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, AlbumService albums, Selection selection)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.albums = albums;
        this.selection = selection;
    }

    public Screen Id => Screen.Albums;

    public bool Chrome => false;

    public void Draw()
    {
        this.albums.EnsureLoaded();
        this.albums.EnsureRequests();

        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(16f);
        var headerHeight = Ui.Px(52f);
        var contentWidth = avail.X - (pad * 2f);

        this.DrawHeader(avail.X, pad, headerHeight);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("albums_body", new Vector2(avail.X, avail.Y - headerHeight), false, ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            if (body.Success)
            {
                ImGui.Indent(pad);
                using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
                    this.DrawBody(contentWidth);
                ImGui.Unindent(pad);
            }
        }

        this.DrawDeleteDialog();
    }

    private void DrawBody(float contentWidth)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));

        var requests = this.albums.Requests;
        if (requests.Count > 0)
        {
            this.DrawRequestsRow(requests.Count, contentWidth);
            ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        }

        this.DrawCountRow(contentWidth);
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));

        if (this.albums.Loaded && this.albums.Mine.Count == 0)
        {
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
                ImGui.TextUnformatted("No albums yet. Name one below to start.");
        }
        else
        {
            foreach (var album in this.albums.Mine)
                this.DrawRow(album, contentWidth);
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        this.DrawNewRow(contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextWrapped("New albums start private. Unlock one for people you choose, or make it public on your profile.");

        ImGui.Dummy(new Vector2(0f, Ui.Px(20f)));
    }

    private void DrawHeader(float fullWidth, float pad, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var midY = origin.Y + (height * 0.5f);

        var back = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, back);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##albums_back", backSize))
            this.router.Navigate(Screen.MyProfile);
        Ui.TextAt(drawList, this.fonts.Icon, ImGui.GetItemRectMin(), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32(), back);

        const string title = "ALBUMS";
        var titleSize = Ui.Measure(this.fonts.Eyebrow, title);
        Ui.TextAt(drawList, this.fonts.Eyebrow, new Vector2(origin.X + ((fullWidth - titleSize.X) * 0.5f), midY - (titleSize.Y * 0.5f)), Palette.TextSecondary.U32(), title);

        drawList.AddLine(new Vector2(origin.X, origin.Y + height), new Vector2(origin.X + fullWidth, origin.Y + height), Palette.Border.U32(), 1f);
    }

    // Eyebrow left, the album count as a two-digit mono figure right.
    private void DrawCountRow(float contentWidth)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        Ui.TextAt(drawList, this.fonts.Eyebrow, origin, Palette.TextSecondary.U32(), "YOUR ALBUMS");
        var count = $"{this.albums.Mine.Count:00}";
        var cs = Ui.Measure(this.fonts.Mono, count);
        Ui.TextAt(drawList, this.fonts.Mono, new Vector2(origin.X + contentWidth - cs.X, origin.Y), Palette.TextMuted.U32(), count);
        ImGui.Dummy(new Vector2(contentWidth, Ui.Measure(this.fonts.Eyebrow, "X").Y));
    }

    private void DrawRequestsRow(int count, float width)
    {
        var rowH = Ui.Px(52f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##albums_requests", new Vector2(width, rowH));
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();
        var max = pos + new Vector2(width, rowH);

        drawList.AddRectFilled(pos, max, Palette.WithAlpha(this.theme.Accent, hovered ? 0.16f : 0.12f).U32());
        drawList.AddRect(pos, max, Palette.WithAlpha(this.theme.Accent, 0.22f).U32(), 0f, ImDrawFlags.None, 1f);

        var midY = pos.Y + (rowH * 0.5f);
        var glyph = FontAwesomeIcon.UserPlus.ToIconString();
        var gs = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(pos.X + Ui.Px(14f), midY - (gs.Y * 0.5f)), this.theme.AccentText.U32(), glyph);

        var label = count == 1 ? "1 person wants access" : $"{count} people want access";
        var ls = Ui.Measure(this.fonts.Body, label);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(pos.X + Ui.Px(44f), midY - (ls.Y * 0.5f)), Palette.TextPrimary.U32(), label);

        var chevron = FontAwesomeIcon.ChevronRight.ToIconString();
        var chs = Ui.Measure(this.fonts.Icon, chevron);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(pos.X + width - Ui.Px(14f) - chs.X, midY - (chs.Y * 0.5f)), Palette.TextMuted.U32(), chevron);

        if (clicked)
            this.router.Navigate(Screen.AlbumRequests);
    }

    private void DrawRow(AlbumDto album, float width)
    {
        var rowH = Ui.Px(64f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##album_" + album.Id, new Vector2(width, rowH));

        // The row fills the width, so the action buttons drawn after it have to be allowed to overlap or
        // they would never take the click.
        ImGui.SetItemAllowOverlap();
        var rowHovered = ImGui.IsItemHovered();
        var after = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        if (rowHovered)
            drawList.AddRectFilled(pos, pos + new Vector2(width, rowH), Palette.WithAlpha(Palette.Overlay, 0.04f).U32());

        var thumb = Ui.Px(44f);
        var thumbPos = new Vector2(pos.X, pos.Y + ((rowH - thumb) * 0.5f));
        var thumbMax = thumbPos + new Vector2(thumb, thumb);
        var texture = album.CoverPhotoId is { } cover ? this.albums.Texture(album.Id, cover) : null;
        if (texture is { Width: > 0, Height: > 0 })
        {
            var (uvMin, uvMax) = Ui.CoverUv(texture.Width, texture.Height, 1f);
            drawList.AddImage(texture.Handle, thumbPos, thumbMax, uvMin, uvMax);
        }
        else
        {
            drawList.AddRectFilled(thumbPos, thumbMax, Palette.Surface2.U32());
            var glyph = FontAwesomeIcon.Image.ToIconString();
            var gs = Ui.Measure(this.fonts.Icon, glyph);
            Ui.TextAt(drawList, this.fonts.Icon, thumbPos + ((new Vector2(thumb, thumb) - gs) * 0.5f), Palette.TextMuted.U32(), glyph);
        }

        drawList.AddRect(thumbPos, thumbMax, Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);

        var button = Ui.Px(32f);
        var deleteTL = new Vector2(pos.X + width - button, pos.Y + ((rowH - button) * 0.5f));
        var visibilityTL = new Vector2(deleteTL.X - Ui.Px(8f) - button, deleteTL.Y);

        var textX = thumbMax.X + Ui.Px(12f);
        var textWidth = visibilityTL.X - Ui.Px(10f) - textX;
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(textX, pos.Y + Ui.Px(15f)), Palette.TextPrimary.U32(), this.Fit(album.Name, textWidth));
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(textX, pos.Y + Ui.Px(35f)), Palette.TextMuted.U32(), Meta(album));

        var isPublic = album.Visibility == AlbumVisibilityEnum.Public;
        var visibilityGlyph = (isPublic ? FontAwesomeIcon.Eye : FontAwesomeIcon.Lock).ToIconString();
        if (this.kit.RowIconButton(drawList, "##album_vis_" + album.Id, visibilityGlyph, visibilityTL, button))
            this.albums.SetVisibility(album.Id, isPublic ? "private" : "public");

        if (this.kit.RowIconButton(drawList, "##album_del_" + album.Id, FontAwesomeIcon.TrashAlt.ToIconString(), deleteTL, button, Palette.Danger))
        {
            this.deleteId = album.Id;
            this.deleteName = album.Name;
            this.openDelete = true;
        }

        drawList.AddLine(new Vector2(pos.X, pos.Y + rowH), new Vector2(pos.X + width, pos.Y + rowH), Palette.Border.U32(), 1f);
        ImGui.SetCursorScreenPos(after);

        if (clicked)
        {
            this.selection.AlbumId = album.Id;
            this.selection.AlbumName = album.Name;
            this.selection.AlbumReturn = Screen.Albums;
            this.router.Navigate(Screen.AlbumDetail);
        }
    }

    // Inline creation: a name field with an Add button butted to its right edge, so the pair reads as one
    // control. At the cap the field is inert and says so.
    private void DrawNewRow(float width)
    {
        var atCap = this.albums.Mine.Count >= MaxAlbums;
        var addWidth = Ui.Px(84f);
        var fieldWidth = width - addWidth;

        this.kit.TextField("##album_new_name", ref this.newName, atCap ? $"Album limit is {MaxAlbums}" : "New album name", fieldWidth, Limits.AlbumNameMax);
        var height = ImGui.GetItemRectSize().Y;
        ImGui.SameLine(0f, 0f);

        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##album_add", new Vector2(addWidth, height));
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();
        var max = pos + new Vector2(addWidth, height);
        var ready = !atCap && this.newName.Trim().Length > 0;

        drawList.AddRectFilled(pos, max, Palette.Surface2.U32());
        if (hovered && ready)
            drawList.AddRectFilled(pos, max, Palette.WithAlpha(Palette.Overlay, 0.06f).U32());
        drawList.AddRect(pos, max, Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);

        var glyph = FontAwesomeIcon.Plus.ToIconString();
        var gs = Ui.Measure(this.fonts.Icon, glyph);
        var label = Ui.Measure(this.fonts.Caption, "Add");
        var groupWidth = gs.X + Ui.Px(6f) + label.X;
        var glyphX = pos.X + ((addWidth - groupWidth) * 0.5f);
        var midY = pos.Y + (height * 0.5f);
        var tint = ready ? Palette.TextPrimary : Palette.TextMuted;
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(glyphX, midY - (gs.Y * 0.5f)), tint.U32(), glyph);
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(glyphX + gs.X + Ui.Px(6f), midY - (label.Y * 0.5f)), tint.U32(), "Add");

        if (clicked && ready)
        {
            this.albums.Create(this.newName.Trim(), "private");
            this.newName = string.Empty;
        }
    }

    private void DrawDeleteDialog()
    {
        if (this.openDelete)
        {
            this.openDelete = false;
            ImGui.OpenPopup("##album_delete");
        }

        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + (ImGui.GetWindowSize() * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize;
        var open = true;

        using (ImRaii.PushColor(ImGuiCol.PopupBg, Palette.Surface1))
        using (ImRaii.PushColor(ImGuiCol.Border, Palette.Border))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(Ui.Px(18f), Ui.Px(18f))))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 0f))
        using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f))
        {
            if (!ImGui.BeginPopupModal("##album_delete", ref open, flags))
                return;

            var width = Ui.Px(280f);
            Ui.CenteredText(width, this.fonts.Title, Palette.TextPrimary, "Delete album?");
            ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
                ImGui.TextWrapped($"\"{this.deleteName}\" and its photos go for good, and anyone you unlocked it for loses access. This cannot be undone.");

            ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
            var half = (width - Ui.Px(10f)) * 0.5f;
            if (this.kit.SecondaryButton("##album_del_cancel", "Cancel", half))
                ImGui.CloseCurrentPopup();
            ImGui.SameLine(0f, Ui.Px(10f));
            if (this.kit.DangerButton("##album_del_confirm", "Delete", half))
            {
                if (this.deleteId is { } id)
                    this.albums.Delete(id);
                this.deleteId = null;
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
