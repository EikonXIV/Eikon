using Dalamud.Interface;
using Eikon.Navigation;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Font picker (Settings > Font). A single column of specimen cards, one per typeface set: the set's name
// is drawn in its own display face and a shared sample line in its body face, so the member compares the
// real letterforms rather than a label. Tapping a card applies it to the whole app immediately and
// persists. Its own header + back chevron, so it takes the full window like the theme picker.
internal sealed class TypefaceScreen : IScreen
{
    // One shared body line across every card, so the faces are compared on identical text. Caps, commas,
    // a period and digits exercise the parts that read "thin" or "squished".
    private const string Sample = "Clear at a glance, day or night. 28.";

    private readonly ScreenRouter router;
    private readonly UiFonts fonts;
    private readonly ThemeService theme;

    public TypefaceScreen(ScreenRouter router, UiFonts fonts, ThemeService theme)
    {
        this.router = router;
        this.fonts = fonts;
        this.theme = theme;
    }

    public Screen Id => Screen.Typeface;

    public bool Chrome => false;

    public void Draw()
    {
        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(16f);
        var headerHeight = Ui.Px(52f);
        var contentWidth = avail.X - (pad * 2f);

        this.DrawHeader(avail.X, pad, headerHeight);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("font_body", new Vector2(avail.X, avail.Y - headerHeight), false, ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            if (!body.Success)
                return;

            ImGui.Indent(pad);
            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
            {
                ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
                this.DrawIntro(contentWidth);
                ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));

                var sets = this.fonts.Sets;
                for (var i = 0; i < sets.Count; i++)
                {
                    this.DrawCard(sets[i], contentWidth);
                    if (i != sets.Count - 1)
                        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
                }

                ImGui.Dummy(new Vector2(0f, Ui.Px(20f)));
            }

            ImGui.Unindent(pad);
        }
    }

    private void DrawIntro(float contentWidth)
    {
        const string hint = "Pick the typeface that reads best for you. It applies across the whole app.";
        var pos = ImGui.GetCursorScreenPos();
        Ui.TextWrappedAt(ImGui.GetWindowDrawList(), this.fonts.Caption, pos, Palette.TextMuted.U32(), hint, contentWidth);
        ImGui.Dummy(new Vector2(contentWidth, Ui.MeasureWrapped(this.fonts.Caption, hint, contentWidth).Y));
    }

    // A specimen card: the set name in its display face, the shared sample in its body face, the set's
    // descriptor in the mono eyebrow, an accent border + "ON" pill when it is the active set.
    private void DrawCard(FontSet set, float width)
    {
        var (display, sample) = this.fonts.Specimen(set.Id);
        var nameH = Ui.Measure(display, set.Name).Y;
        var sampleH = Ui.Measure(sample, Sample).Y;
        var tag = set.Tag.ToUpperInvariant();
        var tagH = Ui.Measure(this.fonts.Eyebrow, tag).Y;

        var padX = Ui.Px(14f);
        var padY = Ui.Px(13f);
        var cardH = padY + nameH + Ui.Px(7f) + sampleH + Ui.Px(9f) + tagH + padY;

        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##font_" + set.Id, new Vector2(width, cardH));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var selected = this.fonts.IsSelected(set.Id);

        if (hovered && !selected)
            dl.AddRectFilled(pos, pos + new Vector2(width, cardH), Palette.WithAlpha(Palette.Overlay, 0.04f).U32());

        var textX = pos.X + padX;
        var y = pos.Y + padY;
        Ui.TextAt(dl, display, new Vector2(textX, y), Palette.TextPrimary.U32(), set.Name);
        y += nameH + Ui.Px(7f);
        Ui.TextAt(dl, sample, new Vector2(textX, y), Palette.TextSecondary.U32(), Sample);
        y += sampleH + Ui.Px(9f);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(textX, y), Palette.TextMuted.U32(), tag);

        var borderCol = selected ? this.theme.Accent : Palette.Border;
        dl.AddRect(pos, pos + new Vector2(width, cardH), borderCol.U32(), 0f, ImDrawFlags.None, selected ? Ui.Px(1.5f) : 1f);

        if (selected)
        {
            const string on = "ON";
            var os = Ui.Measure(this.fonts.Eyebrow, on);
            var bpad = new Vector2(Ui.Px(5f), Ui.Px(3f));
            var bsize = os + (bpad * 2f);
            var bpos = new Vector2((pos.X + width) - Ui.Px(10f) - bsize.X, pos.Y + Ui.Px(11f));
            dl.AddRectFilled(bpos, bpos + bsize, this.theme.Accent.U32());
            Ui.TextAt(dl, this.fonts.Eyebrow, bpos + bpad, Palette.Paper.U32(), on);
        }

        if (clicked)
            this.fonts.SetFontSet(set.Id);
    }

    private void DrawHeader(float fullWidth, float pad, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var midY = origin.Y + (height * 0.5f);

        var back = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, back);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##font_back", backSize))
            this.router.Navigate(Screen.Settings);
        Ui.TextAt(dl, this.fonts.Icon, ImGui.GetItemRectMin(), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32(), back);

        const string title = "FONT";
        var titleSize = Ui.Measure(this.fonts.Eyebrow, title);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(origin.X + ((fullWidth - titleSize.X) * 0.5f), midY - (titleSize.Y * 0.5f)), Palette.TextSecondary.U32(), title);

        dl.AddLine(new Vector2(origin.X, origin.Y + height), new Vector2(origin.X + fullWidth, origin.Y + height), Palette.Border.U32(), 1f);
    }
}
