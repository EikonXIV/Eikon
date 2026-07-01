using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;

namespace Eikon.UI;

// Small UI helpers shared across the custom drawing code.
internal static class Ui
{
    // Scale a hardcoded pixel value by the global HUD scale so layouts hold at any scale.
    public static float Px(float value) => value * ImGuiHelpers.GlobalScale;

    // Measure text in a specific font. Falls back to the current font if the handle is null.
    public static Vector2 Measure(IFontHandle? font, string text)
    {
        if (font is null)
            return ImGui.CalcTextSize(text);

        using (font.Push())
            return ImGui.CalcTextSize(text);
    }

    // Draw text into a draw list with a specific font, so we keep both pixel placement and the
    // intended type scale.
    public static void TextAt(ImDrawListPtr drawList, IFontHandle? font, Vector2 pos, uint color, string text)
    {
        if (font is null)
        {
            drawList.AddText(pos, color, text);
            return;
        }

        using (font.Push())
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos, color, text);
    }

    // Draw one line of text horizontally centered in a container of the given width, using a
    // specific font and color, advancing the layout cursor like a normal item.
    public static void CenteredText(float containerWidth, IFontHandle? font, Vector4 color, string text)
    {
        var width = Measure(font, text).X;
        ImGui.SetCursorPosX(MathF.Max(0f, (containerWidth - width) * 0.5f));
        using (font?.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(text);
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
}
