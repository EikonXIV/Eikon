using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Net;
using Eikon.Services;
using Eikon.UI.Theme;

namespace Eikon.UI;

// Shared photo manager: a six-slot grid of the member's own photos (Main + review-status badges and a
// delete control) plus a crop modal (scroll to zoom, drag to pan) that bakes the chosen crop into the
// uploaded JPEG. Used by both the onboarding photo step and My Profile so they edit the same set
// through PhotoService. Crop state lives here; while IsCropping the host should draw only Draw() and
// hide its own chrome.
internal sealed class PhotoManager
{
    private const float PhotoAspect = 10f / 16f;
    private static readonly Vector4 Amber = new(0.93f, 0.71f, 0.33f, 1f);
    private static readonly Vector4 Danger = new(0.91f, 0.36f, 0.36f, 1f);

    private sealed class Photo
    {
        public string Path = string.Empty;
        public float Zoom = 1f;
        public float CenterX = 0.5f;   // crop center as a fraction of the source image (0..1)
        public float CenterY = 0.5f;
    }

    private readonly PhotoService photoSvc;
    private readonly Media media;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly ThemeService theme;

    private Photo? cropPhoto;

    public PhotoManager(PhotoService photoSvc, Media media, Kit kit, UiFonts fonts, ThemeService theme)
    {
        this.photoSvc = photoSvc;
        this.media = media;
        this.kit = kit;
        this.fonts = fonts;
        this.theme = theme;
    }

    // True while the crop modal owns the content area. The host should then draw only Draw() and skip
    // its own header/nav.
    public bool IsCropping => this.cropPhoto != null;

    // Draws the crop modal when active, otherwise the six-slot grid. The caller owns the section label.
    public void Draw(float contentWidth)
    {
        if (this.cropPhoto != null)
            this.DrawCrop(contentWidth);
        else
            this.DrawGrid(contentWidth);
    }

    public void DrawGrid(float contentWidth)
    {
        this.photoSvc.EnsureLoaded();
        var gap = Ui.Px(8f);
        var tileWidth = (contentWidth - (gap * 2f)) / 3f;
        var tileHeight = tileWidth * 1.6f;
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap)))
        {
            for (var i = 0; i < 6; i++)
            {
                if (i % 3 != 0)
                    ImGui.SameLine();
                this.DrawPhotoSlot(i, tileWidth, tileHeight);
            }
        }

        if (this.photoSvc.Mine.Count >= 2)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
                ImGui.TextUnformatted("Tap a star to set your main photo.");
        }
    }

    private void DrawPhotoSlot(int index, float width, float height)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##photo" + index, new Vector2(width, height));
        var drawList = ImGui.GetWindowDrawList();
        var max = pos + new Vector2(width, height);
        var rounding = Ui.Px(12f);

        if (index >= this.photoSvc.Mine.Count)
        {
            drawList.AddRectFilled(pos, max, Palette.Surface1.U32(), rounding);
            drawList.AddRect(pos, max, Palette.Border.U32(), rounding, ImDrawFlags.None, 1.5f);
            var plus = FontAwesomeIcon.Plus.ToIconString();
            var plusSize = Ui.Measure(this.fonts.Icon, plus);
            Ui.TextAt(drawList, this.fonts.Icon, new Vector2(pos.X + ((width - plusSize.X) * 0.5f), pos.Y + ((height - plusSize.Y) * 0.5f)), Palette.TextMuted.U32(), plus);
            if (clicked && this.photoSvc.Mine.Count < 6)
                this.media.PickImage(path => this.cropPhoto = new Photo { Path = path });
            return;
        }

        var photo = this.photoSvc.Mine[index];
        var texture = this.photoSvc.Texture(photo.Id);
        if (texture != null)
        {
            var (uvMin, uvMax) = CoverUv(texture.Width, texture.Height, width / height, 1f, 0.5f, 0.5f);
            drawList.AddImageRounded(texture.Handle, pos, max, uvMin, uvMax, 0xFFFFFFFFu, rounding);
        }
        else
        {
            drawList.AddRectFilled(pos, max, Palette.Surface2.U32(), rounding);
        }

        var starCenter = pos + new Vector2(Ui.Px(16f), Ui.Px(16f));
        this.DrawStar(drawList, starCenter, index == 0);

        var (statusGlyph, statusColor) = photo.State switch
        {
            PhotoStateEnum.Approved => (FontAwesomeIcon.CheckCircle.ToIconString(), this.theme.Accent),
            PhotoStateEnum.Rejected => (FontAwesomeIcon.TimesCircle.ToIconString(), Danger),
            _ => (FontAwesomeIcon.Clock.ToIconString(), Amber),
        };
        var statusSize = Ui.Measure(this.fonts.Icon, statusGlyph);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(max.X - statusSize.X - Ui.Px(6f), pos.Y + Ui.Px(6f)), statusColor.U32(), statusGlyph);

        var deleteGlyph = FontAwesomeIcon.Times.ToIconString();
        var deleteSize = Ui.Measure(this.fonts.Icon, deleteGlyph);
        var deleteCenter = new Vector2(max.X - Ui.Px(13f), max.Y - Ui.Px(13f));
        drawList.AddCircleFilled(deleteCenter, Ui.Px(10f), Palette.Bg.U32(), 16);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(deleteCenter.X - (deleteSize.X * 0.5f), deleteCenter.Y - (deleteSize.Y * 0.5f)), Palette.TextSecondary.U32(), deleteGlyph);

        if (clicked)
        {
            var mouse = ImGui.GetMousePos();
            if (Hit(mouse, deleteCenter, Ui.Px(12f)))
                this.photoSvc.Delete(photo.Id);
            else if (index != 0 && Hit(mouse, starCenter, Ui.Px(14f)))
                this.photoSvc.SetMain(photo.Id);   // promote: the server gives it the lowest ordinal
        }
    }

    private void DrawCrop(float contentWidth)
    {
        var photo = this.cropPhoto!;
        var rowStart = ImGui.GetCursorPosX();
        var buttonWidth = Ui.Px(96f);
        if (this.kit.SecondaryButton("##crop_cancel", "Cancel", buttonWidth))
        {
            this.cropPhoto = null;
            return;
        }

        ImGui.SameLine();
        ImGui.SetCursorPosX(rowStart + contentWidth - buttonWidth);
        var useClicked = this.kit.PrimaryButton("##crop_use", "Use photo", buttonWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.kit.SectionLabel("Crop to fit");
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));

        // Fill the available space with the largest 10:16 frame that fits (bounded by width and the
        // remaining height, leaving room for the hint line below).
        var availForFrame = ImGui.GetContentRegionAvail().Y - Ui.Px(40f);
        var frameWidth = MathF.Min(contentWidth, MathF.Max(0f, availForFrame) * PhotoAspect);
        if (frameWidth < Ui.Px(160f))
            frameWidth = Ui.Px(160f);
        var frameHeight = frameWidth / PhotoAspect;
        var origin = ImGui.GetCursorScreenPos();
        var framePos = new Vector2(origin.X + ((contentWidth - frameWidth) * 0.5f), origin.Y);
        var frameMax = framePos + new Vector2(frameWidth, frameHeight);
        var drawList = ImGui.GetWindowDrawList();

        // An invisible button over the frame captures scroll (zoom) and click-drag (pan).
        ImGui.SetCursorScreenPos(framePos);
        ImGui.InvisibleButton("##crop_area", new Vector2(frameWidth, frameHeight));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        if (hovered || active)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

        var texture = this.media.Load(photo.Path);
        if (texture != null)
        {
            if (hovered && ImGui.GetIO().MouseWheel is var wheel and not 0f)
            {
                photo.Zoom = Math.Clamp(photo.Zoom * MathF.Pow(1.1f, wheel), 1f, 4f);
                this.ClampCenter(photo, texture.Width, texture.Height);
            }

            if (active && ImGui.GetIO().MouseDelta is var d && (d.X != 0f || d.Y != 0f))
            {
                var (cw, ch) = CropSize(texture.Width, texture.Height, PhotoAspect, photo.Zoom);
                // Move the crop opposite the drag so the image follows the cursor; scale screen px -> image fraction.
                photo.CenterX -= d.X * (cw / frameWidth) / texture.Width;
                photo.CenterY -= d.Y * (ch / frameHeight) / texture.Height;
                this.ClampCenter(photo, texture.Width, texture.Height);
            }

            var (uvMin, uvMax) = CoverUv(texture.Width, texture.Height, PhotoAspect, photo.Zoom, photo.CenterX, photo.CenterY);
            drawList.AddImageRounded(texture.Handle, framePos, frameMax, uvMin, uvMax, 0xFFFFFFFFu, Ui.Px(12f));
        }
        else
        {
            drawList.AddRectFilled(framePos, frameMax, Palette.Surface2.U32(), Ui.Px(12f));
        }
        drawList.AddRect(framePos, frameMax, Palette.Border.U32(), Ui.Px(12f), ImDrawFlags.None, 1f);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, framePos.Y + frameHeight + Ui.Px(12f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextUnformatted("Scroll to zoom  ·  drag to reposition");

        if (useClicked)
        {
            try
            {
                // Bake the chosen crop (zoom + pan) into the uploaded image instead of sending the
                // original, so the stored photo matches what the member framed.
                this.photoSvc.Upload(ImageCrop.ToJpeg(photo.Path, PhotoAspect, photo.Zoom, photo.CenterX, photo.CenterY), "image/jpeg");
            }
            catch
            {
                // Unreadable/unsupported file; drop silently.
            }

            this.cropPhoto = null;
        }
    }

    // Crop rectangle size (image px) that fills the target aspect, shrunk by zoom (1 = fit).
    private static (float Width, float Height) CropSize(float imageWidth, float imageHeight, float targetAspect, float zoom)
    {
        float cropWidth, cropHeight;
        if (imageWidth / imageHeight > targetAspect)
        {
            cropHeight = imageHeight;
            cropWidth = imageHeight * targetAspect;
        }
        else
        {
            cropWidth = imageWidth;
            cropHeight = imageWidth / targetAspect;
        }

        return (cropWidth / zoom, cropHeight / zoom);
    }

    // Keep the crop center far enough from the edges that the crop window stays inside the image.
    private void ClampCenter(Photo photo, float imageWidth, float imageHeight)
    {
        var (cw, ch) = CropSize(imageWidth, imageHeight, PhotoAspect, photo.Zoom);
        var halfX = (cw * 0.5f) / imageWidth;
        var halfY = (ch * 0.5f) / imageHeight;
        photo.CenterX = Math.Clamp(photo.CenterX, halfX, 1f - halfX);
        photo.CenterY = Math.Clamp(photo.CenterY, halfY, 1f - halfY);
    }

    // Cover-crop UVs for the target aspect at a zoom + crop center (center as a 0..1 image fraction).
    private static (Vector2 Min, Vector2 Max) CoverUv(float imageWidth, float imageHeight, float targetAspect, float zoom, float centerX, float centerY)
    {
        if (imageWidth <= 0f || imageHeight <= 0f)
            return (Vector2.Zero, Vector2.One);

        var (cw, ch) = CropSize(imageWidth, imageHeight, targetAspect, zoom);
        var x0 = Math.Clamp((centerX * imageWidth) - (cw * 0.5f), 0f, imageWidth - cw);
        var y0 = Math.Clamp((centerY * imageHeight) - (ch * 0.5f), 0f, imageHeight - ch);
        return (
            new Vector2(x0 / imageWidth, y0 / imageHeight),
            new Vector2((x0 + cw) / imageWidth, (y0 + ch) / imageHeight));
    }

    // Per-photo main control: a star top-left. Accent on the main photo (lowest ordinal), muted on the
    // rest; tapping a muted one promotes it. Solid glyph either way (the icon font has no outline star),
    // so the accent vs muted tint carries the state.
    private void DrawStar(ImDrawListPtr drawList, Vector2 center, bool isMain)
    {
        var bg = isMain ? Palette.WithAlpha(this.theme.Accent, 0.20f) : Palette.Scrim;
        drawList.AddCircleFilled(center, Ui.Px(13f), bg.U32(), 16);
        var glyph = FontAwesomeIcon.Star.ToIconString();
        var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
        var color = isMain ? this.theme.Accent : Palette.WithAlpha(Palette.White, 0.72f);
        Ui.TextAt(drawList, this.fonts.Icon, center - (glyphSize * 0.5f), color.U32(), glyph);
    }

    private static bool Hit(Vector2 point, Vector2 center, float radius)
    {
        var d = point - center;
        return (d.X * d.X) + (d.Y * d.Y) <= radius * radius;
    }

    private void DrawPill(ImDrawListPtr drawList, Vector2 at, string text, Vector4 background, Vector4 foreground)
    {
        var textSize = Ui.Measure(this.fonts.Caption, text);
        var padX = Ui.Px(6f);
        var padY = Ui.Px(2f);
        var size = new Vector2(textSize.X + (padX * 2f), textSize.Y + (padY * 2f));
        drawList.AddRectFilled(at, at + size, background.U32(), size.Y * 0.5f);
        Ui.TextAt(drawList, this.fonts.Caption, at + new Vector2(padX, padY), foreground.U32(), text);
    }
}
