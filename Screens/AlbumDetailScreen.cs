using System.Linq;
using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.Services;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Owner's album detail: the name field, the photo grid with a cover marker and an add tile, and the
// per-photo menu. Album photos go live on upload (no review); tapping a photo opens a menu to view it,
// set it as the cover, or remove it. Visibility toggles from the header, and private albums carry an
// access row through to sharing. Deleting an album lives in the albums list. Reached from there too.
internal sealed class AlbumDetailScreen : IScreen
{
    private const int MaxAlbumPhotos = 24;   // mirrors the server cap in albums/routes.ts

    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly AlbumService albums;
    private readonly Selection selection;
    private readonly Lightbox lightbox;
    private readonly Media media;

    private bool openPhotoMenu;
    private Vector2 photoMenuPos;
    private Guid photoMenuId;
    private bool openAdd;
    private string? pendingPath;
    private Guid? nameFor;
    private string nameText = string.Empty;

    public AlbumDetailScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, AlbumService albums, Selection selection, Lightbox lightbox, Media media)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.albums = albums;
        this.selection = selection;
        this.lightbox = lightbox;
        this.media = media;
    }

    public Screen Id => Screen.AlbumDetail;

    public bool Chrome => false;

    public void Draw()
    {
        var id = this.selection.AlbumId;
        if (id is null)
        {
            this.router.Navigate(this.selection.AlbumReturn);
            return;
        }

        this.albums.EnsureLoaded();
        var album = this.albums.Mine.FirstOrDefault(a => a.Id == id.Value);
        var name = album?.Name ?? this.selection.AlbumName;

        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(16f);
        var headerHeight = Ui.Px(52f);
        this.DrawHeader(avail.X, pad, headerHeight, name, album);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("album_detail_body", new Vector2(avail.X, avail.Y - headerHeight)))
        {
            if (body.Success && album is { } al)
            {
                ImGui.Indent(pad);
                using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
                    this.DrawBody(al, avail.X - (pad * 2f));
                ImGui.Unindent(pad);
            }
        }

        this.DrawPhotoMenu(id.Value);
        this.DrawAddDialog(id.Value);
        this.lightbox.Draw();
    }

    private void DrawBody(AlbumDto album, float contentWidth)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));

        this.kit.SectionLabel("Album name");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.DrawNameField(album, contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.DrawMetaRow(album, contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        if (album.Visibility == AlbumVisibilityEnum.Private)
        {
            this.DrawAccessRow(album, contentWidth);
            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        }

        this.DrawGrid(album, contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(13f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextWrapped(album.Visibility == AlbumVisibilityEnum.Public
                ? "Public albums show on your profile. Tap a photo to view it, set it as the cover, or remove it."
                : "Only people you unlock this album for can see it. Tap a photo to view it, set it as the cover, or remove it.");

        ImGui.Dummy(new Vector2(0f, Ui.Px(20f)));
    }

    // Renaming is inline: the field commits when it loses focus after an edit, and an empty name snaps
    // back rather than clearing the album's name.
    private void DrawNameField(AlbumDto album, float contentWidth)
    {
        if (this.nameFor != album.Id)
        {
            this.nameFor = album.Id;
            this.nameText = album.Name;
        }

        this.kit.TextField("##ad_name", ref this.nameText, "Album name", contentWidth);
        if (!ImGui.IsItemDeactivatedAfterEdit())
            return;

        var trimmed = this.nameText.Trim();
        if (trimmed.Length > 0 && trimmed != album.Name)
            this.albums.Rename(album.Id, trimmed);
        else
            this.nameText = album.Name;
    }

    private void DrawMetaRow(AlbumDto album, float contentWidth)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var count = this.albums.Photos(album.Id).Count;

        var photos = count == 1 ? "1 photo" : $"{count} photos";
        var visibility = album.Visibility == AlbumVisibilityEnum.Public ? "public" : "private";
        Ui.TextAt(drawList, this.fonts.Caption, origin, Palette.TextMuted.U32(), $"{photos} · {visibility}");

        var tally = $"{count:00}/{MaxAlbumPhotos}";
        var ts = Ui.Measure(this.fonts.Mono, tally);
        Ui.TextAt(drawList, this.fonts.Mono, new Vector2(origin.X + contentWidth - ts.X, origin.Y), Palette.TextMuted.U32(), tally);

        ImGui.Dummy(new Vector2(contentWidth, Ui.Measure(this.fonts.Caption, "X").Y));
    }

    // Private albums tap through to the access sheet (share, requests, revoke).
    private void DrawAccessRow(AlbumDto album, float contentWidth)
    {
        var height = Ui.Px(46f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##ad_access", new Vector2(contentWidth, height));
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();
        var max = pos + new Vector2(contentWidth, height);

        drawList.AddRectFilled(pos, max, Palette.WithAlpha(this.theme.Accent, hovered ? 0.16f : 0.12f).U32());
        drawList.AddRect(pos, max, Palette.WithAlpha(this.theme.Accent, 0.22f).U32(), 0f, ImDrawFlags.None, 1f);

        var midY = pos.Y + (height * 0.5f);
        var users = FontAwesomeIcon.Users.ToIconString();
        var us = Ui.Measure(this.fonts.Icon, users);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(pos.X + Ui.Px(13f), midY - (us.Y * 0.5f)), this.theme.AccentText.U32(), users);

        var label = album.SharedCount switch
        {
            0 => "Not shared yet",
            1 => "Shared with 1 person",
            _ => $"Shared with {album.SharedCount}",
        };
        var ls = Ui.Measure(this.fonts.Body, label);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(pos.X + Ui.Px(40f), midY - (ls.Y * 0.5f)), Palette.TextPrimary.U32(), label);

        var chevron = FontAwesomeIcon.ChevronRight.ToIconString();
        var chs = Ui.Measure(this.fonts.Icon, chevron);
        var chevronX = pos.X + contentWidth - Ui.Px(13f) - chs.X;
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(chevronX, midY - (chs.Y * 0.5f)), Palette.TextMuted.U32(), chevron);

        if (album.RequestCount > 0)
        {
            var badge = album.RequestCount == 1 ? "1 request" : $"{album.RequestCount} requests";
            var bs = Ui.Measure(this.fonts.Caption, badge);
            var bx = chevronX - Ui.Px(10f) - bs.X - Ui.Px(14f);
            drawList.AddRectFilled(new Vector2(bx, midY - Ui.Px(9f)), new Vector2(bx + bs.X + Ui.Px(14f), midY + Ui.Px(9f)), Palette.DangerFill.U32());
            Ui.TextAt(drawList, this.fonts.Caption, new Vector2(bx + Ui.Px(7f), midY - (bs.Y * 0.5f)), Palette.White.U32(), badge);
        }

        if (clicked)
            this.router.Navigate(Screen.AlbumAccess);
    }

    private void DrawHeader(float fullWidth, float pad, float height, string name, AlbumDto? album)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var midY = origin.Y + (height * 0.5f);

        var back = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, back);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##ad_back", backSize))
            this.router.Navigate(this.selection.AlbumReturn);
        Ui.TextAt(drawList, this.fonts.Icon, ImGui.GetItemRectMin(), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32(), back);

        var title = $"ALBUM · {name.ToUpperInvariant()}";
        var titleSize = Ui.Measure(this.fonts.Eyebrow, title);
        Ui.TextAt(drawList, this.fonts.Eyebrow, new Vector2(origin.X + ((fullWidth - titleSize.X) * 0.5f), midY - (titleSize.Y * 0.5f)), Palette.TextSecondary.U32(), title);

        if (album is { } al)
        {
            var button = Ui.Px(30f);
            var topLeft = new Vector2(origin.X + fullWidth - pad - button, midY - (button * 0.5f));
            var isPublic = al.Visibility == AlbumVisibilityEnum.Public;
            var glyph = (isPublic ? FontAwesomeIcon.Eye : FontAwesomeIcon.Lock).ToIconString();
            if (this.kit.RowIconButton(drawList, "##ad_vis", glyph, topLeft, button))
                this.albums.SetVisibility(al.Id, isPublic ? "private" : "public");
        }

        drawList.AddLine(new Vector2(origin.X, origin.Y + height), new Vector2(origin.X + fullWidth, origin.Y + height), Palette.Border.U32(), 1f);
    }

    private void DrawGrid(AlbumDto album, float contentWidth)
    {
        var photos = this.albums.Photos(album.Id);
        var coverId = album.CoverPhotoId ?? (photos.Count > 0 ? photos[0].Id : (Guid?)null);
        const int columns = 3;
        var gap = Ui.Px(7f);
        var tile = (contentWidth - (gap * (columns - 1))) / columns;
        var size = new Vector2(tile, tile);

        var col = 0;
        for (var i = 0; i < photos.Count; i++)
        {
            if (col % columns != 0)
                ImGui.SameLine(0f, gap);
            this.DrawPhotoTile(album.Id, photos[i].Id, photos[i].Id == coverId, i + 1, size);
            col++;
            if (col % columns == 0)
                ImGui.Dummy(new Vector2(0f, gap));
        }

        if (col % columns != 0)
            ImGui.SameLine(0f, gap);
        this.DrawAddTile(size, photos.Count >= MaxAlbumPhotos);
    }

    private void DrawPhotoTile(Guid albumId, Guid photoId, bool isCover, int index, Vector2 size)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##ad_p_" + photoId, size);
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();
        var max = pos + size;
        var texture = this.albums.Texture(albumId, photoId);

        drawList.AddRectFilled(pos, max, Palette.Surface2.U32());
        if (texture is { Width: > 0, Height: > 0 })
        {
            var (uvMin, uvMax) = Ui.CoverUv(texture.Width, texture.Height, size.X / size.Y);
            drawList.AddImage(texture.Handle, pos, max, uvMin, uvMax);
        }
        else
        {
            var glyph = FontAwesomeIcon.Image.ToIconString();
            var gs = Ui.Measure(this.fonts.Icon, glyph);
            Ui.TextAt(drawList, this.fonts.Icon, pos + ((size - gs) * 0.5f), Palette.TextMuted.U32(), glyph);
        }

        if (hovered)
            drawList.AddRectFilled(pos, max, Palette.WithAlpha(Palette.Scrim, 0.3f).U32());
        drawList.AddRect(pos, max, (hovered ? Palette.BorderStrong : Palette.Border).U32(), 0f, ImDrawFlags.None, 1f);

        // Position index, mono, on a scrim chip so it reads over any photo.
        var number = $"{index:00}";
        var ns = Ui.Measure(this.fonts.Mono, number);
        var numberPos = new Vector2(max.X - Ui.Px(5f) - ns.X, pos.Y + Ui.Px(6f));
        drawList.AddRectFilled(numberPos - new Vector2(Ui.Px(4f), Ui.Px(3f)), numberPos + ns + new Vector2(Ui.Px(4f), Ui.Px(3f)), Palette.WithAlpha(Palette.Scrim, 0.72f).U32());
        Ui.TextAt(drawList, this.fonts.Mono, numberPos, Palette.White.U32(), number);

        if (isCover)
        {
            const string cover = "COVER";
            var cs = Ui.Measure(this.fonts.Eyebrow, cover);
            var chipPos = new Vector2(pos.X + Ui.Px(5f), pos.Y + Ui.Px(6f));
            var chipPad = new Vector2(Ui.Px(5f), Ui.Px(3f));
            drawList.AddRectFilled(chipPos, chipPos + cs + (chipPad * 2f), this.theme.Accent.U32());
            Ui.TextAt(drawList, this.fonts.Eyebrow, chipPos + chipPad, Palette.Paper.U32(), cover);
        }

        if (clicked)
        {
            this.photoMenuId = photoId;
            this.photoMenuPos = new Vector2(pos.X, pos.Y + size.Y);
            this.openPhotoMenu = true;
        }
    }

    private void DrawAddTile(Vector2 size, bool atCap)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##ad_add", size);
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();
        var tint = atCap ? Palette.TextMuted : (hovered ? Palette.TextPrimary : Palette.TextSecondary);

        if (hovered && !atCap)
            drawList.AddRectFilled(pos, pos + size, Palette.WithAlpha(Palette.Overlay, 0.04f).U32());
        drawList.AddRect(pos, pos + size, Palette.WithAlpha(Palette.Overlay, atCap ? 0.1f : 0.2f).U32(), 0f, ImDrawFlags.None, 1f);

        var glyph = (atCap ? FontAwesomeIcon.Lock : FontAwesomeIcon.Plus).ToIconString();
        var gs = Ui.Measure(this.fonts.Icon, glyph);
        var label = atCap ? "FULL" : "ADD";
        var ls = Ui.Measure(this.fonts.Eyebrow, label);
        var center = pos + (size * 0.5f);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(center.X - (gs.X * 0.5f), center.Y - gs.Y), tint.U32(), glyph);
        Ui.TextAt(drawList, this.fonts.Eyebrow, new Vector2(center.X - (ls.X * 0.5f), center.Y + Ui.Px(5f)), tint.U32(), label);

        if (clicked)
        {
            if (atCap)
                this.openAdd = true;   // the dialog explains the limit
            else
                this.media.PickImage(p => { this.pendingPath = p; this.openAdd = true; });
        }
    }

    private void DrawPhotoMenu(Guid albumId)
    {
        if (this.openPhotoMenu)
        {
            this.openPhotoMenu = false;
            ImGui.OpenPopup("##ad_photo_menu");
        }
        ImGui.SetNextWindowPos(this.photoMenuPos, ImGuiCond.Always);
        using (this.MenuStyle())
        {
            if (!ImGui.BeginPopup("##ad_photo_menu"))
                return;
            if (this.MenuRow(FontAwesomeIcon.Expand, "View", false))
            {
                if (this.albums.Texture(albumId, this.photoMenuId) is { } tex)
                    this.lightbox.OpenTexture(tex);
                ImGui.CloseCurrentPopup();
            }
            if (this.MenuRow(FontAwesomeIcon.Star, "Set as cover", false))
            {
                this.albums.SetCover(albumId, this.photoMenuId);
                ImGui.CloseCurrentPopup();
            }
            if (this.MenuRow(FontAwesomeIcon.TrashAlt, "Remove from album", true))
            {
                this.albums.RemovePhoto(albumId, this.photoMenuId);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private void DrawAddDialog(Guid albumId)
    {
        if (this.openAdd)
        {
            this.openAdd = false;
            ImGui.OpenPopup("##ad_add_dialog");
        }
        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + (ImGui.GetWindowSize() * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(Ui.Px(320f), 0f));
        var open = true;
        using (this.DialogStyle())
        {
            if (!ImGui.BeginPopupModal("##ad_add_dialog", ref open, DialogFlags))
                return;

            var width = ImGui.GetContentRegionAvail().X;

            // Full album: explain rather than add. The server also rejects, but this keeps the picker
            // from opening onto a dead end.
            if (this.albums.Photos(albumId).Count >= MaxAlbumPhotos)
            {
                Ui.CenteredText(width, this.fonts.Title, Palette.TextPrimary, "Album is full");
                ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
                using (this.fonts.Caption.Push())
                using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
                    ImGui.TextWrapped($"An album holds up to {MaxAlbumPhotos} photos. Remove one to add another.");
                ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
                if (this.kit.SecondaryButton("##ad_add_close", "Close", width))
                {
                    this.pendingPath = null;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
                return;
            }

            Ui.CenteredText(width, this.fonts.Title, Palette.TextPrimary, "Add photo");
            ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
            if (this.pendingPath != null && this.media.Load(this.pendingPath) is { Width: > 0, Height: > 0 } tex)
            {
                var scale = MathF.Min(MathF.Min(width / tex.Width, Ui.Px(200f) / tex.Height), 1f);
                var w = tex.Width * scale;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((width - w) * 0.5f));
                ImGui.Image(tex.Handle, new Vector2(w, tex.Height * scale));
            }
            ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
            Ui.CenteredText(width, this.fonts.Caption, Palette.TextMuted, "Visible right away to anyone you unlock this album for.");

            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
            var half = (width - Ui.Px(10f)) * 0.5f;
            if (this.kit.SecondaryButton("##ad_add_cancel", "Cancel", half))
            {
                this.pendingPath = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine(0f, Ui.Px(10f));
            if (this.kit.PrimaryButton("##ad_add_ok", "Add to album", half) && this.pendingPath != null)
            {
                var bytes = ImageCrop.ResizeJpeg(this.pendingPath, 1280);
                this.albums.AddPhoto(albumId, bytes, "image/jpeg");
                this.pendingPath = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private const ImGuiWindowFlags DialogFlags =
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize;

    // Popup chrome shared by the menu and the dialog: a surface-1 card with a hairline border.
    private IDisposable MenuStyle()
    {
        var disposables = new List<IDisposable>
        {
            ImRaii.PushColor(ImGuiCol.PopupBg, Palette.Surface1),
            ImRaii.PushColor(ImGuiCol.Border, Palette.Border),
            ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(Ui.Px(6f), Ui.Px(6f))),
            ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 0f),
            ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f),
        };
        return new Composite(disposables);
    }

    private IDisposable DialogStyle()
    {
        var disposables = new List<IDisposable>
        {
            ImRaii.PushColor(ImGuiCol.PopupBg, Palette.Surface1),
            ImRaii.PushColor(ImGuiCol.Border, Palette.Border),
            ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(Ui.Px(18f), Ui.Px(18f))),
            ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 0f),
            ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f),
        };
        return new Composite(disposables);
    }

    private bool MenuRow(FontAwesomeIcon icon, string label, bool danger)
    {
        var width = Ui.Px(198f);
        var height = Ui.Px(36f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##menu_" + label, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();
        if (hovered)
            drawList.AddRectFilled(pos, pos + new Vector2(width, height), Palette.WithAlpha(Palette.Overlay, 0.05f).U32());
        var color = danger ? Palette.Danger : Palette.TextSecondary;
        var glyph = icon.ToIconString();
        var gs = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(pos.X + Ui.Px(10f), pos.Y + ((height - gs.Y) * 0.5f)), color.U32(), glyph);
        var ls = Ui.Measure(this.fonts.Body, label);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(pos.X + Ui.Px(36f), pos.Y + ((height - ls.Y) * 0.5f)), (danger ? color : Palette.TextPrimary).U32(), label);
        return clicked;
    }

    private sealed class Composite : IDisposable
    {
        private readonly List<IDisposable> items;
        public Composite(List<IDisposable> items) => this.items = items;
        public void Dispose()
        {
            for (var i = this.items.Count - 1; i >= 0; i--)
                this.items[i].Dispose();
        }
    }
}
