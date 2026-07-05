using Dalamud.Interface;
using Eikon.Navigation;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Appearance (Settings > Appearance). The theme picker: a grid of solid accent colors and a grid of
// pride-flag themes. Tapping either recolors the whole app immediately and persists. Reached by
// drilling in from Settings, mirroring the Blocked users screen (its own header + back chevron, so it
// takes the full window rather than the chrome shell).
internal sealed class AppearanceScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;

    public AppearanceScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
    }

    public Screen Id => Screen.Appearance;

    public bool Chrome => false;

    public void Draw()
    {
        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(16f);
        var headerHeight = Ui.Px(52f);
        var contentWidth = avail.X - (pad * 2f);

        this.DrawHeader(avail.X, pad, headerHeight);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("appearance_body", new Vector2(avail.X, avail.Y - headerHeight)))
        {
            if (!body.Success)
                return;

            ImGui.Indent(pad);
            ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));

            this.kit.SectionLabel("Colors");
            ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
            this.DrawColorSwatches(contentWidth);

            ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
            this.kit.SectionLabel("Pride flags");
            ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
            this.DrawFlagSwatches(contentWidth);

            ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
            ImGui.Unindent(pad);
        }
    }

    // The twelve solid accents, unchanged filled-circle swatches. A solid is selected only when no flag
    // theme is active (ThemeId is null), so at most one swatch highlights across both grids.
    private void DrawColorSwatches(float contentWidth)
    {
        const int columns = 6;
        var gap = Ui.Px(10f);
        var cell = (contentWidth - (gap * (columns - 1))) / columns;
        var diameter = MathF.Min(cell, Ui.Px(42f));
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap)))
        {
            for (var i = 0; i < AccentPresets.All.Count; i++)
            {
                if (i % columns != 0)
                    ImGui.SameLine();
                this.DrawColorSwatch(i, cell, diameter);
            }
        }
    }

    private void DrawColorSwatch(int index, float cell, float diameter)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##sw" + index, new Vector2(cell, diameter));
        var drawList = ImGui.GetWindowDrawList();

        var color = Palette.Rgb(AccentPresets.All[index].Rgb);
        var center = new Vector2(pos.X + (cell * 0.5f), pos.Y + (diameter * 0.5f));
        drawList.AddCircleFilled(center, (diameter * 0.5f) - Ui.Px(3f), color.U32(), 24);

        if (this.theme.ThemeId is null && this.theme.AccentIndex == index)
        {
            drawList.AddCircle(center, (diameter * 0.5f) - Ui.Px(1f), Palette.White.U32(), 24, Ui.Px(2f));
            var check = FontAwesomeIcon.Check.ToIconString();
            var checkSize = Ui.Measure(this.fonts.Icon, check);
            var onColor = Palette.Luminance(color) > 0.6f ? Palette.Bg : Palette.White;
            Ui.TextAt(drawList, this.fonts.Icon, new Vector2(center.X - (checkSize.X * 0.5f), center.Y - (checkSize.Y * 0.5f)), onColor.U32(), check);
        }

        if (clicked)
            this.theme.SetAccent(index);
    }

    // The pride flags as striped rounded-square swatches with a name below each. Three per row.
    private void DrawFlagSwatches(float contentWidth)
    {
        const int columns = 3;
        var gap = Ui.Px(12f);
        var cell = (contentWidth - (gap * (columns - 1))) / columns;
        var tile = MathF.Min(cell, Ui.Px(78f));
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap)))
        {
            var flags = this.theme.Flags;
            for (var i = 0; i < flags.Count; i++)
            {
                if (i % columns != 0)
                    ImGui.SameLine();
                this.DrawFlagSwatch(flags[i], cell, tile);
            }
        }
    }

    private void DrawFlagSwatch(ThemeSpec flag, float cell, float tile)
    {
        var nameSize = Ui.Measure(this.fonts.Caption, flag.Name);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##flag_" + flag.Id, new Vector2(cell, tile + Ui.Px(8f) + nameSize.Y));
        var drawList = ImGui.GetWindowDrawList();

        var min = new Vector2(pos.X, pos.Y);   // left-aligned tile + name, per the app's alignment rule
        var selected = this.theme.ThemeId == flag.Id;
        Ui.FlagSwatch(drawList, this.fonts.Icon, min, tile, Ui.Px(12f), flag.Stripes, flag.PrimaryHue, selected);

        var nameColor = (selected ? Palette.TextPrimary : Palette.TextSecondary).U32();
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(pos.X, min.Y + tile + Ui.Px(6f)), nameColor, flag.Name);

        if (clicked)
            this.theme.SetTheme(flag.Id);
    }

    private void DrawHeader(float fullWidth, float pad, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var midY = origin.Y + (height * 0.5f);

        var backGlyph = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, backGlyph);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##appearance_back", backSize))
            this.router.Navigate(Screen.Settings);
        Ui.TextAt(drawList, this.fonts.Icon, ImGui.GetItemRectMin(), Palette.TextSecondary.U32(), backGlyph);

        const string title = "Appearance";
        var titleSize = Ui.Measure(this.fonts.Body, title);
        Ui.TextAt(drawList, this.fonts.Body,
            new Vector2(origin.X + pad + backSize.X + Ui.Px(12f), midY - (titleSize.Y * 0.5f)),
            Palette.TextPrimary.U32(), title);

        drawList.AddLine(new Vector2(origin.X, origin.Y + height), new Vector2(origin.X + fullWidth, origin.Y + height), Palette.Border.U32(), 1f);
    }
}
