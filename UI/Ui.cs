using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Eikon.UI.Theme;

namespace Eikon.UI;

// Small UI helpers shared across the custom drawing code.
internal static class Ui
{
    // Extra UI zoom on top of the game HUD scale, from the member's Text size setting (1.0 = 100%). Drives
    // layout (Px) and draw-list text immediately when it changes, so a resize shows at once.
    public static float Scale { get; set; } = 1f;

    // The factor the fonts are actually rasterized at. It lags Scale during a Text size change: the atlas
    // rebuilds in the background and this catches up only when the new glyphs land. Set by UiFonts.
    public static float FontBakedScale { get; set; } = 1f;

    // Ratio to draw baked glyphs at the display size. 1.0 once the atlas has caught up (crisp); off 1.0 in
    // the brief window after a change, so draw-list text resizes instantly (soft) and then sharpens.
    private static float TextRenderScale => FontBakedScale > 0f ? Scale / FontBakedScale : 1f;

    // Scale a hardcoded pixel value by the global HUD scale and the Text size factor so layouts hold at
    // any scale and grow with the text.
    public static float Px(float value) => value * ImGuiHelpers.GlobalScale * Scale;

    // Measure text in a specific font, at the display size (baked size times the pending render ratio).
    public static Vector2 Measure(IFontHandle? font, string text)
    {
        if (font is null)
            return ImGui.CalcTextSize(text) * TextRenderScale;

        using (font.Push())
            return ImGui.CalcTextSize(text) * TextRenderScale;
    }

    // Draw text into a draw list with a specific font, at the display size, so it resizes with the Text
    // size setting the moment it changes rather than waiting for the atlas rebuild.
    public static void TextAt(ImDrawListPtr drawList, IFontHandle? font, Vector2 pos, uint color, string text)
    {
        using (font?.Push())
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize() * TextRenderScale, pos, color, text);
    }

    // Draw text at an explicit pixel size, scaling the font's baked glyphs. Used for the Text size live
    // preview, so a sample resizes as the slider drags without rebuilding the atlas on every frame.
    public static void TextAtSized(ImDrawListPtr drawList, IFontHandle? font, Vector2 pos, float pixelSize, uint color, string text)
    {
        using (font?.Push())
            drawList.AddText(ImGui.GetFont(), pixelSize, pos, color, text);
    }

    // Draw one line of text horizontally centered in a container of the given width, using a specific font
    // and color, advancing the layout cursor like a normal item. Draw-list based so it tracks the Text size
    // ratio like the rest.
    public static void CenteredText(float containerWidth, IFontHandle? font, Vector4 color, string text)
    {
        var size = Measure(font, text);
        var pos = ImGui.GetCursorScreenPos();
        TextAt(ImGui.GetWindowDrawList(), font, new Vector2(pos.X + MathF.Max(0f, (containerWidth - size.X) * 0.5f), pos.Y), color.U32(), text);
        ImGui.Dummy(size);
    }

    // The Eikon mark: an Allagan-tomestone glyph (a stone tablet outline with a glowing diamond core
    // and a center gem) mapped from a 24-unit design box, centered on `center` and sized to `box`. Two
    // tones: `stone` draws the tablet outline, `glow` the diamond and gem. Shared by the app logo and
    // the minimized phone orb so they carry the same mark. Stroke widths scale with the box.
    public static void AetherCore(ImDrawListPtr drawList, Vector2 center, float box, uint stone, uint glow)
    {
        var g0 = center - new Vector2(box * 0.5f, box * 0.5f);
        Vector2 P(float gx, float gy) => g0 + new Vector2((gx / 24f) * box, (gy / 24f) * box);
        drawList.AddRect(P(6.5f, 3.5f), P(17.5f, 20.5f), stone, (2.5f / 24f) * box, ImDrawFlags.None, box * (1.4f / 40f));
        drawList.AddQuad(P(12f, 6.5f), P(16f, 12f), P(12f, 17.5f), P(8f, 12f), glow, box * (1.3f / 40f));
        drawList.AddCircleFilled(P(12f, 12f), (1.7f / 24f) * box, glow, 12);
    }

    // Filled rectangle with an independent radius per corner, in the order top-left, top-right,
    // bottom-right, bottom-left. ImGui's AddRectFilled only takes one radius; the chat bubbles need a
    // small "tail tuck" on their sender-side bottom corner (DESIGN/SCREENS: 14/14/14/4 and 14/14/4/14),
    // so this builds the outline as a convex path and fills it. A zero radius yields a sharp corner.
    public static void FillRectCorners(ImDrawListPtr drawList, Vector2 min, Vector2 max, uint color, float topLeft, float topRight, float bottomRight, float bottomLeft)
    {
        drawList.PathArcToFast(new Vector2(min.X + topLeft, min.Y + topLeft), topLeft, 6, 9);
        drawList.PathArcToFast(new Vector2(max.X - topRight, min.Y + topRight), topRight, 9, 12);
        drawList.PathArcToFast(new Vector2(max.X - bottomRight, max.Y - bottomRight), bottomRight, 0, 3);
        drawList.PathArcToFast(new Vector2(min.X + bottomLeft, max.Y - bottomLeft), bottomLeft, 3, 6);
        drawList.PathFillConvex(color);
    }

    // Cover-crop UVs: fill a target aspect (width/height) from an image, with optional zoom and a
    // vertical pan. Shared by the photo tiles and the profile hero.
    public static (Vector2 Min, Vector2 Max) CoverUv(float imageWidth, float imageHeight, float targetAspect, float zoom = 1f, float offsetY = 0.5f)
    {
        if (imageWidth <= 0f || imageHeight <= 0f)
            return (Vector2.Zero, Vector2.One);

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

        cropWidth /= zoom;
        cropHeight /= zoom;
        var x0 = (imageWidth - cropWidth) * 0.5f;
        var y0 = (imageHeight - cropHeight) * Math.Clamp(offsetY, 0f, 1f);
        return (
            new Vector2(x0 / imageWidth, y0 / imageHeight),
            new Vector2((x0 + cropWidth) / imageWidth, (y0 + cropHeight) / imageHeight));
    }

    // Measure wrapped text in a specific font.
    public static Vector2 MeasureWrapped(IFontHandle? font, string text, float wrapWidth)
    {
        if (font is null)
            return ImGui.CalcTextSize(text, false, wrapWidth);

        using (font.Push())
            return ImGui.CalcTextSize(text, false, wrapWidth);
    }

    // Draw wrapped text into a draw list with a specific font.
    public static void TextWrappedAt(ImDrawListPtr drawList, IFontHandle? font, Vector2 pos, uint color, string text, float wrapWidth)
    {
        if (font is null)
        {
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos, color, text, wrapWidth);
            return;
        }

        using (font.Push())
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos, color, text, wrapWidth);
    }

    // A thin horizontal band split into equal vertical stripe columns, carrying a flag theme's full
    // color set. Column x-bounds are rounded to whole pixels so no background sliver or overlap appears
    // at fractional GlobalScale and the last column lands exactly on pos.X + width. Faint white seams
    // separate columns so adjacent black / near-black stripes (Ace, Non-binary) don't merge into the
    // dark window. An empty stripe list draws nothing (solid themes have none, so no bar shows).
    public static void FlagBar(ImDrawListPtr drawList, Vector2 pos, float width, IReadOnlyList<Vector4> stripes, float height)
    {
        if (stripes is null || stripes.Count == 0 || width <= 0f || height <= 0f)
            return;

        var n = stripes.Count;
        for (var i = 0; i < n; i++)
        {
            var x0 = pos.X + MathF.Round((i * width) / n);
            var x1 = pos.X + MathF.Round(((i + 1) * width) / n);
            drawList.AddRectFilled(new Vector2(x0, pos.Y), new Vector2(x1, pos.Y + height), stripes[i].U32());
        }

        var seam = Palette.WithAlpha(Palette.White, 0.06f).U32();
        for (var i = 1; i < n; i++)
        {
            var xSeam = pos.X + MathF.Round((i * width) / n);
            drawList.AddLine(new Vector2(xSeam, pos.Y), new Vector2(xSeam, pos.Y + height), seam, 1f);
        }
    }

    // A theme swatch as a rounded square. 0 or 1 stripes draws a single solid fill (a solid preset);
    // more draws vertical stripe columns with the same rounded outer corners (via FillRectCorners on the
    // end columns) and faint white seams, so pale and near-black flag stripes stay distinct. When
    // selected, a white ring plus a check on a dark disc reads legibly over any stripe underneath.
    public static void FlagSwatch(ImDrawListPtr drawList, IFontHandle? iconFont, Vector2 min, float size, float rounding, IReadOnlyList<Vector4> stripes, Vector4 solid, bool selected)
    {
        var max = min + new Vector2(size, size);
        if (stripes is null || stripes.Count <= 1)
        {
            drawList.AddRectFilled(min, max, solid.U32(), rounding);
        }
        else
        {
            var n = stripes.Count;
            for (var i = 0; i < n; i++)
            {
                var x0 = min.X + MathF.Round((i * size) / n);
                var x1 = min.X + MathF.Round(((i + 1) * size) / n);
                var tl = i == 0 ? rounding : 0f;
                var tr = i == n - 1 ? rounding : 0f;
                FillRectCorners(drawList, new Vector2(x0, min.Y), new Vector2(x1, max.Y), stripes[i].U32(), tl, tr, tr, tl);
            }

            var seam = Palette.WithAlpha(Palette.White, 0.10f).U32();
            for (var i = 1; i < n; i++)
            {
                var xSeam = min.X + MathF.Round((i * size) / n);
                drawList.AddLine(new Vector2(xSeam, min.Y), new Vector2(xSeam, max.Y), seam, 1f);
            }
        }

        if (!selected)
            return;

        drawList.AddRect(min, max, Palette.White.U32(), rounding, ImDrawFlags.None, Px(2f));
        var check = FontAwesomeIcon.CheckCircle.ToIconString();
        var cs = Measure(iconFont, check);
        var center = new Vector2(max.X - (cs.X * 0.5f) - Px(5f), min.Y + (cs.Y * 0.5f) + Px(5f));
        drawList.AddCircleFilled(center, (MathF.Max(cs.X, cs.Y) * 0.5f) + Px(2f), Palette.Scrim.U32(), 16);
        TextAt(drawList, iconFont, new Vector2(center.X - (cs.X * 0.5f), center.Y - (cs.Y * 0.5f)), Palette.White.U32(), check);
    }
}
