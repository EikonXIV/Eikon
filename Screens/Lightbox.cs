using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using Eikon.Net;
using Eikon.Services;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Full-photo viewer shown as a dimmed modal. A host opens it with a set of image paths (or a
// placeholder initial for profiles whose photos are not local files yet) and calls Draw every
// frame. Tap the side zones to page, the X to close.
internal sealed class Lightbox
{
    private readonly ThemeService theme;
    private readonly UiFonts fonts;
    private readonly Media media;
    private readonly PhotoService photos;

    private readonly List<string> paths = new();
    private readonly List<Guid> photoIds = new();   // server photo ids, fetched + cached by PhotoService (profile galleries)
    private string? placeholderInitial;
    private IDalamudTextureWrap? singleTexture;   // a pre-decrypted image (chat photos), shown as-is
    private int index;
    private bool queuedOpen;
    private bool expanded;     // false = fit-to-window; true = enlarged (the image and window both grow)

    public Lightbox(ThemeService theme, UiFonts fonts, Media media, PhotoService photos)
    {
        this.theme = theme;
        this.fonts = fonts;
        this.media = media;
        this.photos = photos;
    }

    public void Open(IReadOnlyList<string> photoPaths, int startIndex)
    {
        this.paths.Clear();
        this.paths.AddRange(photoPaths);
        this.photoIds.Clear();
        this.placeholderInitial = null;
        this.singleTexture = null;
        this.index = Math.Clamp(startIndex, 0, Math.Max(0, this.paths.Count - 1));
        this.queuedOpen = true;
    }

    // Open a profile gallery by server photo id. Each is fetched and cached by PhotoService and paged
    // in place; a photo still downloading shows a blank panel for that frame rather than a letter.
    public void OpenPhotos(IReadOnlyList<Guid> ids, int startIndex)
    {
        this.paths.Clear();
        this.photoIds.Clear();
        this.photoIds.AddRange(ids);
        this.placeholderInitial = null;
        this.singleTexture = null;
        this.index = Math.Clamp(startIndex, 0, Math.Max(0, this.photoIds.Count - 1));
        this.queuedOpen = true;
    }

    public void OpenPlaceholder(string initial)
    {
        this.paths.Clear();
        this.photoIds.Clear();
        this.placeholderInitial = initial;
        this.singleTexture = null;
        this.index = 0;
        this.queuedOpen = true;
    }

    // Open a single already-decrypted image (a chat photo); scaled to fit the bounded viewer window.
    public void OpenTexture(IDalamudTextureWrap texture)
    {
        this.paths.Clear();
        this.photoIds.Clear();
        this.placeholderInitial = null;
        this.singleTexture = texture;
        this.index = 0;
        this.queuedOpen = true;
    }

    public void Draw()
    {
        if (this.queuedOpen)
        {
            this.queuedOpen = false;
            this.ResetView();
            ImGui.OpenPopup("##lightbox");
        }

        if (!ImGui.IsPopupOpen("##lightbox"))
            return;

        var viewport = ImGui.GetMainViewport();
        var count = this.photoIds.Count > 0 ? this.photoIds.Count : this.paths.Count;

        // Resolve the current image up front so the window can be sized to fit it, instead of a fixed
        // box that letterboxes a portrait with big empty margins.
        var texture = this.singleTexture
            ?? (this.photoIds.Count > 0 ? this.photos.Texture(this.photoIds[this.index])
                : (this.placeholderInitial == null && this.paths.Count > 0 ? this.media.Load(this.paths[this.index]) : null));

        var pad = Ui.Px(12f);
        var dotsHeight = count > 1 ? Ui.Px(24f) : 0f;
        // Fit box: the modest default the viewer opens at. Enlarge box: ~95% of the screen, the cap
        // the window can grow to on click.
        var fitBox = new Vector2(
            MathF.Min(viewport.WorkSize.X * 0.7f, Ui.Px(540f)),
            (viewport.WorkSize.Y * 0.86f) - dotsHeight - (pad * 2f));
        var enlargeBox = new Vector2(
            viewport.WorkSize.X * 0.95f,
            (viewport.WorkSize.Y * 0.95f) - dotsHeight - (pad * 2f));

        const float maxUpscale = 2f;   // small images never enlarge past 2x native, so they stay sharp
        var hasZoom = false;
        Vector2 fit;
        if (texture != null)
        {
            // Fit never upscales past native, so a small image opens at 100% in a small window.
            var fitScale = MathF.Min(MathF.Min(fitBox.X / texture.Width, fitBox.Y / texture.Height), 1f);
            // Enlarged grows toward the screen, but the 2x cap keeps small images from pixelating.
            var fillScale = MathF.Min(enlargeBox.X / texture.Width, enlargeBox.Y / texture.Height);
            var enlargeScale = MathF.Min(fillScale, maxUpscale);
            // Only offer zoom when enlarging is meaningfully bigger than fit (no pointless 5% nudge).
            hasZoom = enlargeScale > fitScale * 1.1f;
            var scale = this.expanded && hasZoom ? enlargeScale : fitScale;
            fit = new Vector2(texture.Width * scale, texture.Height * scale);
        }
        else
        {
            // Not loaded yet, or a placeholder: a sensible portrait box until the photo arrives.
            fit = new Vector2(fitBox.X, MathF.Min(fitBox.Y, fitBox.X * 1.25f));
        }

        var size = new Vector2(fit.X + (pad * 2f), fit.Y + dotsHeight + (pad * 2f));
        ImGui.SetNextWindowPos(viewport.GetCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(size);
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar;
        var open = true;

        using (ImRaii.PushColor(ImGuiCol.PopupBg, Palette.Bg))
        using (ImRaii.PushColor(ImGuiCol.Border, Palette.Border))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(pad, pad)))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, Ui.Px(16f)))
        using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f))
        {
            if (!ImGui.BeginPopupModal("##lightbox", ref open, flags))
                return;

            var avail = ImGui.GetContentRegionAvail();
            var origin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var img = new Vector2(avail.X, avail.Y - dotsHeight);

            if (texture != null)
            {
                // The window is already sized to the active (fit or enlarged) scale, so fitting the
                // image to the content box reproduces that scale, centred.
                var scale = MathF.Min(img.X / texture.Width, img.Y / texture.Height);
                var drawSize = new Vector2(texture.Width * scale, texture.Height * scale);
                var imagePos = origin + ((img - drawSize) * 0.5f);
                drawList.AddImageRounded(texture.Handle, imagePos, imagePos + drawSize, Vector2.Zero, Vector2.One, 0xFFFFFFFFu, Ui.Px(10f));
            }
            else
            {
                drawList.AddRectFilled(origin, origin + img, Palette.Surface2.U32(), Ui.Px(10f));
                // Only the explicit placeholder (a profile with no photos) draws a letter; a profile
                // photo that is still downloading just shows the panel until it arrives.
                if (this.placeholderInitial is { } initial)
                {
                    var initialSize = Ui.Measure(this.fonts.Title, initial);
                    Ui.TextAt(drawList, this.fonts.Title, origin + new Vector2((img.X - initialSize.X) * 0.5f, (img.Y - initialSize.Y) * 0.5f), Palette.TextMuted.U32(), initial);
                }
            }

            // Close first so it claims the top-right corner; the paging/zoom zones take the rest.
            // (Overlapping invisible buttons resolve to the one submitted first.)
            var closeCenter = new Vector2(origin.X + avail.X - Ui.Px(14f), origin.Y + Ui.Px(14f));
            ImGui.SetCursorScreenPos(new Vector2(closeCenter.X - Ui.Px(14f), closeCenter.Y - Ui.Px(14f)));
            if (ImGui.InvisibleButton("##lb_close", new Vector2(Ui.Px(28f), Ui.Px(28f))))
                ImGui.CloseCurrentPopup();

            if (count > 1)
            {
                // Paging lives on the outer thirds; the centre band is free for the zoom toggle.
                ImGui.SetCursorScreenPos(origin);
                if (ImGui.InvisibleButton("##lb_prev", new Vector2(avail.X * 0.34f, img.Y)))
                    this.Page(-1, count);
                ImGui.SetCursorScreenPos(new Vector2(origin.X + (avail.X * 0.66f), origin.Y));
                if (ImGui.InvisibleButton("##lb_next", new Vector2(avail.X * 0.34f, img.Y)))
                    this.Page(1, count);

                // Visible paging affordances, vertically centred on the image.
                var arrowY = origin.Y + (img.Y * 0.5f);
                this.DrawArrow(drawList, new Vector2(origin.X + Ui.Px(20f), arrowY), FontAwesomeIcon.ChevronLeft);
                this.DrawArrow(drawList, new Vector2(origin.X + avail.X - Ui.Px(20f), arrowY), FontAwesomeIcon.ChevronRight);

                var dotGap = Ui.Px(12f);
                var total = (count - 1) * dotGap;
                var startX = origin.X + (avail.X * 0.5f) - (total * 0.5f);
                var y = origin.Y + img.Y + (dotsHeight * 0.5f);
                for (var i = 0; i < count; i++)
                    drawList.AddCircleFilled(new Vector2(startX + (i * dotGap), y), Ui.Px(3.5f), (i == this.index ? this.theme.Secondary.Base : Palette.Border).U32(), 12);
            }

            // Click to grow / shrink. The hit area is the centre band for a gallery (edges page) or the
            // whole image for a single photo; only offered when there is room to enlarge.
            if (hasZoom)
            {
                var zoneX = count > 1 ? origin.X + (avail.X * 0.34f) : origin.X;
                var zoneW = count > 1 ? avail.X * 0.32f : avail.X;
                ImGui.SetCursorScreenPos(new Vector2(zoneX, origin.Y));
                if (ImGui.InvisibleButton("##lb_zoom", new Vector2(zoneW, img.Y)))
                    this.expanded = !this.expanded;
                if (ImGui.IsItemHovered())
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                // A small magnifier hint so the click-to-enlarge is discoverable.
                var hint = (this.expanded ? FontAwesomeIcon.SearchMinus : FontAwesomeIcon.SearchPlus).ToIconString();
                var hintCenter = count > 1
                    ? new Vector2(origin.X + (avail.X * 0.5f), origin.Y + img.Y - Ui.Px(16f))
                    : new Vector2(origin.X + avail.X - Ui.Px(16f), origin.Y + img.Y - Ui.Px(16f));
                drawList.AddCircleFilled(hintCenter, Ui.Px(13f), Palette.Scrim.U32(), 16);
                var hintSize = Ui.Measure(this.fonts.Icon, hint);
                Ui.TextAt(drawList, this.fonts.Icon, hintCenter - (hintSize * 0.5f), Palette.White.U32(), hint);
            }

            drawList.AddCircleFilled(closeCenter, Ui.Px(13f), Palette.Scrim.U32(), 16);
            var closeGlyph = FontAwesomeIcon.Times.ToIconString();
            var closeSize = Ui.Measure(this.fonts.Icon, closeGlyph);
            Ui.TextAt(drawList, this.fonts.Icon, new Vector2(closeCenter.X - (closeSize.X * 0.5f), closeCenter.Y - (closeSize.Y * 0.5f)), Palette.White.U32(), closeGlyph);

            ImGui.EndPopup();
        }
    }

    // A paging affordance: a chevron on a dark scrim disc so it stays legible over any photo. Purely
    // visual; the wide invisible zones behind it take the taps.
    private void DrawArrow(ImDrawListPtr drawList, Vector2 center, FontAwesomeIcon icon)
    {
        drawList.AddCircleFilled(center, Ui.Px(15f), Palette.Scrim.U32(), 20);
        var glyph = icon.ToIconString();
        var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(drawList, this.fonts.Icon, center - (glyphSize * 0.5f), Palette.White.U32(), glyph);
    }

    private void ResetView() => this.expanded = false;

    // Page the gallery and snap back to fit, so a new photo never opens mid-enlarge.
    private void Page(int direction, int count)
    {
        this.index = (this.index + direction + count) % count;
        this.ResetView();
    }
}
