using Dalamud.Interface;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Viewing someone else's album (public, or one they unlocked for you). A read-only photo grid that
// loads each image through the album's grant-checked signed URL; tapping a photo opens the lightbox.
// Reached from the albums section on a profile.
internal sealed class AlbumViewerScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly AlbumService albums;
    private readonly Selection selection;
    private readonly Lightbox lightbox;
    private readonly WindowController windowController;

    public AlbumViewerScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, AlbumService albums, Selection selection, Lightbox lightbox, WindowController windowController)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.albums = albums;
        this.selection = selection;
        this.lightbox = lightbox;
        this.windowController = windowController;
    }

    public Screen Id => Screen.AlbumViewer;

    public bool Chrome => false;

    public void Draw()
    {
        var albumId = this.selection.AlbumId;
        if (albumId is null)
        {
            this.router.Navigate(this.selection.AlbumReturn);
            return;
        }

        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(16f);
        var headerHeight = Ui.Px(54f);
        this.DrawHeader(avail.X, pad);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("album_viewer_body", new Vector2(avail.X, avail.Y - headerHeight)))
        {
            if (body.Success)
            {
                ImGui.Indent(pad);
                var contentWidth = avail.X - (pad * 2f);
                ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));

                var photos = this.albums.Photos(albumId.Value);
                if (photos.Count == 0)
                {
                    ImGui.Dummy(new Vector2(0f, Ui.Px(48f)));
                    this.kit.EmptyState(FontAwesomeIcon.Images.ToIconString(), "No photos", "This album is empty for now.", contentWidth);
                }
                else
                {
                    const int columns = 3;
                    var gap = Ui.Px(7f);
                    var tile = (contentWidth - (gap * (columns - 1))) / columns;
                    var size = new Vector2(tile, tile);
                    using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap)))
                    {
                        var col = 0;
                        foreach (var photo in photos)
                        {
                            if (col % columns != 0)
                                ImGui.SameLine(0f, gap);
                            this.DrawTile(albumId.Value, photo.Id, size);
                            col++;
                        }
                    }
                }

                ImGui.Unindent(pad);
            }
        }

        this.lightbox.Draw();
    }

    private void DrawHeader(float fullWidth, float pad)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var midY = origin.Y + Ui.Px(27f);

        var back = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, back);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##av_back", backSize))
            this.router.Navigate(this.selection.AlbumReturn);
        Ui.TextAt(drawList, this.fonts.Icon, ImGui.GetItemRectMin(), Palette.TextSecondary.U32(), back);

        var name = this.selection.ProfileDisplayName ?? string.Empty;
        var title = name.Length > 0 ? $"{name} · {this.selection.AlbumName}" : this.selection.AlbumName;
        var titleSize = Ui.Measure(this.fonts.Body, title);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(origin.X + ((fullWidth - titleSize.X) * 0.5f), midY - (titleSize.Y * 0.5f)), Palette.TextPrimary.U32(), title);

        var btn = Ui.Px(30f);
        var minTL = new Vector2(origin.X + fullWidth - pad - btn, midY - (btn * 0.5f));
        if (this.kit.HeaderIconButton(drawList, "##av_min", FontAwesomeIcon.Minus.ToIconString(), minTL, btn))
            this.windowController.Minimize();

        drawList.AddLine(new Vector2(origin.X, origin.Y + Ui.Px(53f)), new Vector2(origin.X + fullWidth, origin.Y + Ui.Px(53f)), Palette.Border.U32(), 1f);
    }

    private void DrawTile(Guid albumId, Guid photoId, Vector2 size)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##av_p_" + photoId, size);
        var drawList = ImGui.GetWindowDrawList();
        var rounding = Ui.Px(9f);
        var tex = this.albums.Texture(albumId, photoId);
        drawList.AddRectFilled(pos, pos + size, Palette.Surface2.U32(), rounding);
        if (tex is { Width: > 0, Height: > 0 })
        {
            var (uvMin, uvMax) = Ui.CoverUv(tex.Width, tex.Height, size.X / size.Y);
            drawList.AddImageRounded(tex.Handle, pos, pos + size, uvMin, uvMax, 0xFFFFFFFFu, rounding);
            if (clicked)
                this.lightbox.OpenTexture(tex);
        }
        else
        {
            var center = pos + (size * 0.5f);
            const string label = "Loading...";
            var ls = Ui.Measure(this.fonts.Caption, label);
            Ui.TextAt(drawList, this.fonts.Caption, center - (ls * 0.5f), Palette.TextMuted.U32(), label);
        }
    }
}
