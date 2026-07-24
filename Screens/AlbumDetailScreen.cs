using System.Linq;
using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.Services;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Owner's album detail: the access bar, the photo grid with an add tile and a cover marker, and the
// per-photo and album menus. Album photos go live on upload (no review); tapping a photo opens a menu
// to view it, set it as the cover, or remove it. Reached from the albums manager.
internal sealed class AlbumDetailScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly AlbumService albums;
    private readonly Selection selection;
    private readonly Lightbox lightbox;
    private readonly Media media;
    private readonly WindowController windowController;

    private bool openOverflow;
    private Vector2 overflowPos;
    private bool openPhotoMenu;
    private Vector2 photoMenuPos;
    private Guid photoMenuId;
    private bool openAdd;
    private string? pendingPath;
    private bool openRename;
    private string renameText = string.Empty;
    private bool openDelete;

    public AlbumDetailScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, AlbumService albums, Selection selection, Lightbox lightbox, Media media, WindowController windowController)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.albums = albums;
        this.selection = selection;
        this.lightbox = lightbox;
        this.media = media;
        this.windowController = windowController;
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
        var headerHeight = Ui.Px(54f);
        this.DrawHeader(avail.X, pad, name, album is null);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("album_detail_body", new Vector2(avail.X, avail.Y - headerHeight)))
        {
            if (body.Success && album is { } al)
            {
                ImGui.Indent(pad);
                var contentWidth = avail.X - (pad * 2f);
                ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));

                this.DrawAccessBar(al, contentWidth);
                ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
                this.DrawGrid(al, contentWidth);

                ImGui.Dummy(new Vector2(0f, Ui.Px(13f)));
                using (this.fonts.Caption.Push())
                using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
                    ImGui.TextWrapped(al.Visibility == AlbumVisibilityEnum.Public
                        ? "Public albums show on your profile. No requests needed."
                        : "Star marks the cover. Only people you unlock this album for can see it.");

                ImGui.Unindent(pad);
            }
        }

        this.DrawOverflowMenu(album);
        this.DrawPhotoMenu(id.Value);
        this.DrawAddDialog(id.Value);
        this.DrawRenameDialog(id.Value);
        this.DrawDeleteDialog(id.Value, name, album);
        this.lightbox.Draw();
    }

    private void DrawHeader(float fullWidth, float pad, string name, bool loading)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var midY = origin.Y + Ui.Px(27f);

        var back = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, back);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##ad_back", backSize))
            this.router.Navigate(this.selection.AlbumReturn);
        Ui.TextAt(drawList, this.fonts.Icon, ImGui.GetItemRectMin(), Palette.TextSecondary.U32(), back);

        var titleSize = Ui.Measure(this.fonts.Body, name);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(origin.X + ((fullWidth - titleSize.X) * 0.5f), midY - (titleSize.Y * 0.5f)), Palette.TextPrimary.U32(), name);

        // Right side, corner inward: minimize (always, collapses to the orb), a hairline, then the
        // overflow menu once the album has loaded.
        var btn = Ui.Px(30f);
        var minTL = new Vector2(origin.X + fullWidth - pad - btn, midY - (btn * 0.5f));
        if (this.kit.HeaderIconButton(drawList, "##ad_min", FontAwesomeIcon.Minus.ToIconString(), minTL, btn))
            this.windowController.Minimize();
        if (!loading)
        {
            var divX = minTL.X - Ui.Px(8f);
            drawList.AddLine(new Vector2(divX, midY - Ui.Px(9f)), new Vector2(divX, midY + Ui.Px(9f)), Palette.Border.U32(), 1f);
            var dotsTL = new Vector2(divX - Ui.Px(8f) - btn, midY - (btn * 0.5f));
            if (this.kit.HeaderIconButton(drawList, "##ad_overflow", FontAwesomeIcon.EllipsisH.ToIconString(), dotsTL, btn))
            {
                this.overflowPos = new Vector2(dotsTL.X + btn, dotsTL.Y + btn + Ui.Px(4f));
                this.openOverflow = true;
            }
        }

        drawList.AddLine(new Vector2(origin.X, origin.Y + Ui.Px(53f)), new Vector2(origin.X + fullWidth, origin.Y + Ui.Px(53f)), Palette.Border.U32(), 1f);
    }

    private void DrawAccessBar(AlbumDto album, float contentWidth)
    {
        var height = Ui.Px(46f);
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        if (album.Visibility == AlbumVisibilityEnum.Public)
        {
            drawList.AddRectFilled(pos, pos + new Vector2(contentWidth, height), Palette.Surface1.U32(), Ui.Px(11f));
            drawList.AddRect(pos, pos + new Vector2(contentWidth, height), Palette.Border.U32(), Ui.Px(11f), ImDrawFlags.None, 1f);
            var globe = FontAwesomeIcon.Globe.ToIconString();
            Ui.TextAt(drawList, this.fonts.Icon, new Vector2(pos.X + Ui.Px(13f), pos.Y + (height * 0.5f) - (Ui.Measure(this.fonts.Icon, globe).Y * 0.5f)), new Vector4(0.56f, 0.84f, 0.65f, 1f).U32(), globe);
            Ui.TextAt(drawList, this.fonts.Body, new Vector2(pos.X + Ui.Px(40f), pos.Y + Ui.Px(8f)), Palette.TextPrimary.U32(), "Public");
            Ui.TextAt(drawList, this.fonts.Caption, new Vector2(pos.X + Ui.Px(40f), pos.Y + Ui.Px(26f)), Palette.TextMuted.U32(), "Anyone who can see your profile can view");
            ImGui.Dummy(new Vector2(contentWidth, height));
            return;
        }

        // Private: the bar taps through to the access sheet (share, requests, revoke).
        var clicked = ImGui.InvisibleButton("##ad_accessbar", new Vector2(contentWidth, height));
        drawList.AddRectFilled(pos, pos + new Vector2(contentWidth, height), Palette.WithAlpha(this.theme.Accent, 0.12f).U32(), Ui.Px(11f));
        drawList.AddRect(pos, pos + new Vector2(contentWidth, height), Palette.WithAlpha(this.theme.Accent, 0.22f).U32(), Ui.Px(11f), ImDrawFlags.None, 1f);
        var users = FontAwesomeIcon.Users.ToIconString();
        var barMidY = pos.Y + (height * 0.5f);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(pos.X + Ui.Px(13f), barMidY - (Ui.Measure(this.fonts.Icon, users).Y * 0.5f)), this.theme.AccentText.U32(), users);
        var barLabel = album.SharedCount == 1 ? "Shared with 1 person" : $"Shared with {album.SharedCount}";
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(pos.X + Ui.Px(40f), barMidY - (Ui.Measure(this.fonts.Body, barLabel).Y * 0.5f)), Palette.TextPrimary.U32(), barLabel);

        var chevron = FontAwesomeIcon.ChevronRight.ToIconString();
        var chs = Ui.Measure(this.fonts.Icon, chevron);
        var chX = pos.X + contentWidth - Ui.Px(13f) - chs.X;
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(chX, barMidY - (chs.Y * 0.5f)), Palette.TextMuted.U32(), chevron);
        if (album.RequestCount > 0)
        {
            var badge = album.RequestCount == 1 ? "1 request" : $"{album.RequestCount} requests";
            var bs = Ui.Measure(this.fonts.Caption, badge);
            var bx = chX - Ui.Px(10f) - bs.X - Ui.Px(14f);
            drawList.AddRectFilled(new Vector2(bx, barMidY - Ui.Px(9f)), new Vector2(bx + bs.X + Ui.Px(14f), barMidY + Ui.Px(9f)), Palette.DangerFill.U32(), Ui.Px(9f));
            Ui.TextAt(drawList, this.fonts.Caption, new Vector2(bx + Ui.Px(7f), barMidY - (bs.Y * 0.5f)), Palette.White.U32(), badge);
        }

        if (clicked)
            this.router.Navigate(Screen.AlbumAccess);
    }

    private void DrawGrid(AlbumDto album, float contentWidth)
    {
        var photos = this.albums.Photos(album.Id);
        var coverId = album.CoverPhotoId ?? (photos.Count > 0 ? photos[0].Id : (Guid?)null);
        const int columns = 3;
        var gap = Ui.Px(7f);
        var tile = (contentWidth - (gap * (columns - 1))) / columns;
        var size = new Vector2(tile, tile);

        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap)))
        {
            this.DrawAddTile(size, photos.Count >= MaxAlbumPhotos);
            var col = 1;
            foreach (var photo in photos)
            {
                if (col % columns != 0)
                    ImGui.SameLine(0f, gap);
                this.DrawPhotoTile(album.Id, photo.Id, photo.Id == coverId, size);
                col++;
            }
        }
    }

    private const int MaxAlbumPhotos = 24;   // mirrors the server cap in albums/routes.ts

    private void DrawAddTile(Vector2 size, bool atCap)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##ad_add", size);
        var drawList = ImGui.GetWindowDrawList();
        var tint = atCap ? Palette.TextMuted : Palette.TextSecondary;
        drawList.AddRect(pos, pos + size, Palette.WithAlpha(Palette.Overlay, atCap ? 0.1f : 0.2f).U32(), Ui.Px(9f), ImDrawFlags.None, 1f);
        var glyph = (atCap ? FontAwesomeIcon.Lock : FontAwesomeIcon.Plus).ToIconString();
        var gs = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(drawList, this.fonts.Icon, pos + (size * 0.5f) - (gs * 0.5f), tint.U32(), glyph);
        if (clicked)
        {
            if (atCap)
                this.openAdd = true;   // the dialog explains the limit
            else
                this.media.PickImage(p => { this.pendingPath = p; this.openAdd = true; });
        }
    }

    private void DrawPhotoTile(Guid albumId, Guid photoId, bool isCover, Vector2 size)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##ad_p_" + photoId, size);
        var drawList = ImGui.GetWindowDrawList();
        var rounding = Ui.Px(9f);
        var tex = this.albums.Texture(albumId, photoId);
        drawList.AddRectFilled(pos, pos + size, Palette.Surface2.U32(), rounding);
        if (tex is { Width: > 0, Height: > 0 })
        {
            var (uvMin, uvMax) = Ui.CoverUv(tex.Width, tex.Height, size.X / size.Y);
            drawList.AddImageRounded(tex.Handle, pos, pos + size, uvMin, uvMax, 0xFFFFFFFFu, rounding);
        }
        else
        {
            var glyph = FontAwesomeIcon.Image.ToIconString();
            var gs = Ui.Measure(this.fonts.Icon, glyph);
            Ui.TextAt(drawList, this.fonts.Icon, pos + (size * 0.5f) - (gs * 0.5f), Palette.TextMuted.U32(), glyph);
        }

        if (isCover)
        {
            var star = FontAwesomeIcon.Star.ToIconString();
            var ss = Ui.Measure(this.fonts.Icon, star);
            var radius = (MathF.Max(ss.X, ss.Y) * 0.5f) + Ui.Px(4f);
            var center = new Vector2(pos.X + size.X - radius - Ui.Px(5f), pos.Y + radius + Ui.Px(5f));
            drawList.AddCircleFilled(center, radius, Palette.WithAlpha(Palette.Bg, 0.66f).U32(), 16);
            Ui.TextAt(drawList, this.fonts.Icon, center - (ss * 0.5f), this.theme.AccentText.U32(), star);
        }

        if (clicked)
        {
            this.photoMenuId = photoId;
            this.photoMenuPos = new Vector2(pos.X, pos.Y + size.Y);
            this.openPhotoMenu = true;
        }
    }

    private void DrawOverflowMenu(AlbumDto? album)
    {
        if (this.openOverflow)
        {
            this.openOverflow = false;
            ImGui.OpenPopup("##ad_overflow_menu");
        }
        ImGui.SetNextWindowPos(this.overflowPos, ImGuiCond.Always, new Vector2(1f, 0f));
        using (this.MenuStyle())
        {
            if (!ImGui.BeginPopup("##ad_overflow_menu"))
                return;
            if (album is { } al)
            {
                if (this.MenuRow(FontAwesomeIcon.Pen, "Rename album", false))
                {
                    this.renameText = al.Name;
                    this.openRename = true;
                    ImGui.CloseCurrentPopup();
                }
                var makePublic = al.Visibility == AlbumVisibilityEnum.Private;
                if (this.MenuRow(makePublic ? FontAwesomeIcon.Globe : FontAwesomeIcon.Lock, makePublic ? "Make public" : "Make private", false))
                {
                    this.albums.SetVisibility(al.Id, makePublic ? "public" : "private");
                    ImGui.CloseCurrentPopup();
                }
                if (this.MenuRow(FontAwesomeIcon.TrashAlt, "Delete album", true))
                {
                    this.openDelete = true;
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.EndPopup();
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

    private void DrawRenameDialog(Guid albumId)
    {
        if (this.openRename)
        {
            this.openRename = false;
            ImGui.OpenPopup("##ad_rename");
        }
        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + (ImGui.GetWindowSize() * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        var open = true;
        using (this.DialogStyle())
        {
            if (!ImGui.BeginPopupModal("##ad_rename", ref open, DialogFlags))
                return;

            var width = Ui.Px(288f);
            ImGui.Dummy(new Vector2(width, 0f));
            Ui.CenteredText(width, this.fonts.Title, Palette.TextPrimary, "Rename album");
            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
            this.kit.TextField("##ad_rename_name", ref this.renameText, "Album name", width);
            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
            var half = (width - Ui.Px(10f)) * 0.5f;
            if (this.kit.SecondaryButton("##ad_rename_cancel", "Cancel", half))
                ImGui.CloseCurrentPopup();
            ImGui.SameLine(0f, Ui.Px(10f));
            if (this.kit.PrimaryButton("##ad_rename_ok", "Save", half) && this.renameText.Trim().Length > 0)
            {
                this.albums.Rename(albumId, this.renameText.Trim());
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawDeleteDialog(Guid albumId, string name, AlbumDto? album)
    {
        if (this.openDelete)
        {
            this.openDelete = false;
            ImGui.OpenPopup("##ad_delete");
        }
        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + (ImGui.GetWindowSize() * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        var open = true;
        using (this.DialogStyle())
        {
            if (!ImGui.BeginPopupModal("##ad_delete", ref open, DialogFlags))
                return;

            var width = Ui.Px(288f);
            ImGui.Dummy(new Vector2(width, 0f));
            Ui.CenteredText(width, this.fonts.Title, Palette.TextPrimary, $"Delete {name}?");
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            var shared = album?.SharedCount ?? 0;
            var body = shared > 0
                ? $"This deletes the album and revokes access for {shared}. Your photos are removed with it."
                : "This deletes the album and its photos.";
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
                ImGui.TextWrapped(body);

            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
            var half = (width - Ui.Px(10f)) * 0.5f;
            if (this.kit.SecondaryButton("##ad_del_cancel", "Cancel", half))
                ImGui.CloseCurrentPopup();
            ImGui.SameLine(0f, Ui.Px(10f));
            if (this.kit.DangerButton("##ad_del_ok", "Delete", half))
            {
                this.albums.Delete(albumId);
                ImGui.CloseCurrentPopup();
                this.router.Navigate(this.selection.AlbumReturn);
            }

            ImGui.EndPopup();
        }
    }

    private const ImGuiWindowFlags DialogFlags =
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize;

    // Popup chrome shared by the two menus: a surface-1 card with a hairline border.
    private IDisposable MenuStyle()
    {
        var disposables = new List<IDisposable>
        {
            ImRaii.PushColor(ImGuiCol.PopupBg, Palette.Surface1),
            ImRaii.PushColor(ImGuiCol.Border, Palette.Border),
            ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(Ui.Px(6f), Ui.Px(6f))),
            ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, Ui.Px(12f)),
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
            ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, Ui.Px(16f)),
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
            drawList.AddRectFilled(pos, pos + new Vector2(width, height), Palette.WithAlpha(Palette.Overlay, 0.05f).U32(), Ui.Px(8f));
        var color = danger ? new Vector4(0.95f, 0.7f, 0.73f, 1f) : Palette.TextSecondary;
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
