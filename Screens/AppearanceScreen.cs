using System.Linq;
using Dalamud.Interface;
using Eikon.Navigation;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Theme picker (Settings > Appearance). The catalog is grouped into collapsible sections - the editorial
// bases, the solid sets, the blends and the pride flags - because a flat list of every theme is a long
// scroll to nothing. Each card is a mini-app rendered in that theme's own palette; tapping one applies it
// to the whole app immediately and persists. Its own header + back chevron, so it takes the full window.
internal sealed class AppearanceScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;

    private readonly HashSet<string> expanded = new();
    private bool opened;

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
        // Open on the section holding the active theme, so the current pick is visible without hunting
        // for it, and leave the rest closed.
        if (!this.opened)
        {
            this.opened = true;
            this.expanded.Add(this.theme.Current.Group);
        }

        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(16f);
        var headerHeight = Ui.Px(52f);
        var contentWidth = avail.X - (pad * 2f);

        this.DrawHeader(avail.X, pad, headerHeight);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("theme_body", new Vector2(avail.X, avail.Y - headerHeight)))
        {
            if (!body.Success)
                return;

            ImGui.Indent(pad);
            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
            {
                ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
                foreach (var group in Themes.Groups)
                    this.DrawSection(group, contentWidth);
                ImGui.Dummy(new Vector2(0f, Ui.Px(20f)));
            }

            ImGui.Unindent(pad);
        }
    }

    private void DrawSection(string group, float contentWidth)
    {
        var defs = this.theme.All.Where(d => d.Group == group).ToList();
        if (defs.Count == 0)
            return;

        var isOpen = this.expanded.Contains(group);
        if (this.DrawSectionHeader(group, defs, isOpen, contentWidth) && !this.expanded.Remove(group))
            this.expanded.Add(group);

        if (!isOpen)
            return;

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.DrawGrid(defs, contentWidth, Columns(group), Ui.Px(PreviewHeight(group)));
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
    }

    // Chevron, section name and a count, over a hairline. The whole row toggles.
    private bool DrawSectionHeader(string group, IReadOnlyList<ThemeDef> defs, bool isOpen, float contentWidth)
    {
        var rowHeight = Ui.Px(42f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##sec_" + group, new Vector2(contentWidth, rowHeight));
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();
        var midY = pos.Y + (rowHeight * 0.5f);

        if (hovered)
            drawList.AddRectFilled(pos, pos + new Vector2(contentWidth, rowHeight), Palette.WithAlpha(Palette.Overlay, 0.04f).U32());

        var chevron = (isOpen ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight).ToIconString();
        var cs = Ui.Measure(this.fonts.Icon, chevron);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(pos.X, midY - (cs.Y * 0.5f)), (hovered ? Palette.TextPrimary : Palette.TextMuted).U32(), chevron);

        var label = group.ToUpperInvariant();
        var ls = Ui.Measure(this.fonts.Eyebrow, label);
        Ui.TextAt(drawList, this.fonts.Eyebrow, new Vector2(pos.X + Ui.Px(22f), midY - (ls.Y * 0.5f)), Palette.TextSecondary.U32(), label);

        // A closed section still shows whether the active theme lives in it.
        if (!isOpen && defs.Any(d => this.theme.IsSelected(d.Id)))
            drawList.AddCircleFilled(new Vector2(pos.X + Ui.Px(28f) + ls.X, midY), Ui.Px(3f), this.theme.Accent.U32(), 10);

        var count = $"{defs.Count:00}";
        var ns = Ui.Measure(this.fonts.Mono, count);
        Ui.TextAt(drawList, this.fonts.Mono, new Vector2(pos.X + contentWidth - ns.X, midY - (ns.Y * 0.5f)), Palette.TextMuted.U32(), count);

        drawList.AddLine(new Vector2(pos.X, pos.Y + rowHeight), new Vector2(pos.X + contentWidth, pos.Y + rowHeight), Palette.Border.U32(), 1f);
        return clicked;
    }

    private static int Columns(string group) =>
        group is Themes.GroupEditorial or Themes.GroupPride or Themes.GroupBlends ? 2 : 3;

    private static float PreviewHeight(string group) => group switch
    {
        Themes.GroupEditorial => 88f,
        Themes.GroupPride => 80f,
        Themes.GroupBlends => 80f,
        _ => 58f,
    };

    private void DrawGrid(IReadOnlyList<ThemeDef> defs, float contentWidth, int columns, float previewH)
    {
        var gap = Ui.Px(10f);
        var cellW = (contentWidth - (gap * (columns - 1))) / columns;
        for (var i = 0; i < defs.Count; i++)
        {
            if (i % columns != 0)
                ImGui.SameLine(0f, gap);
            this.DrawCard(defs[i], cellW, previewH);
            if (i % columns == columns - 1 && i != defs.Count - 1)
                ImGui.Dummy(new Vector2(0f, gap));
        }
    }

    private void DrawCard(ThemeDef def, float cellW, float previewH)
    {
        var nameH = Ui.Measure(this.fonts.Body, def.Name).Y;
        var tagH = Ui.Measure(this.fonts.Caption, def.Tag).Y;
        var cardH = previewH + Ui.Px(10f) + nameH + Ui.Px(2f) + tagH + Ui.Px(10f);

        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##th_" + def.Id, new Vector2(cellW, cardH));
        var dl = ImGui.GetWindowDrawList();
        var selected = this.theme.IsSelected(def.Id);

        this.DrawPreview(dl, pos, new Vector2(cellW, previewH), def);
        dl.AddLine(new Vector2(pos.X, pos.Y + previewH), new Vector2(pos.X + cellW, pos.Y + previewH), Palette.Border.U32(), 1f);

        var borderCol = selected ? this.theme.Accent : Palette.Border;
        dl.AddRect(pos, new Vector2(pos.X + cellW, pos.Y + cardH), borderCol.U32(), 0f, ImDrawFlags.None, selected ? Ui.Px(1.5f) : 1f);

        // Selected badge: an "ON" pill in the accent, over the top-right of the preview.
        if (selected)
        {
            const string on = "ON";
            var os = Ui.Measure(this.fonts.Eyebrow, on);
            var bpad = new Vector2(Ui.Px(5f), Ui.Px(3f));
            var bsize = os + (bpad * 2f);
            var bpos = new Vector2((pos.X + cellW) - Ui.Px(8f) - bsize.X, pos.Y + Ui.Px(8f));
            dl.AddRectFilled(bpos, bpos + bsize, this.theme.Accent.U32());
            Ui.TextAt(dl, this.fonts.Eyebrow, bpos + bpad, Palette.Paper.U32(), on);
        }

        var textX = pos.X + Ui.Px(10f);
        var textY = pos.Y + previewH + Ui.Px(10f);
        Ui.TextAt(dl, this.fonts.Body, new Vector2(textX, textY), Palette.TextPrimary.U32(), def.Name);
        Ui.TextAt(dl, this.fonts.Caption, new Vector2(textX, textY + nameH + Ui.Px(2f)), Palette.TextMuted.U32(), def.Tag);

        if (clicked)
            this.theme.SetTheme(def.Id);
    }

    // A mini app rendered in the card theme's own swatches [bg, panel, ink, accent]: window dots, a couple
    // of text lines, an accent pill and dot. Anything carrying a stripe - flags and blends - gets it as a
    // top ribbon.
    private void DrawPreview(ImDrawListPtr dl, Vector2 pos, Vector2 size, ThemeDef def)
    {
        var bg = def.Swatches[0];
        var ink = def.Swatches[2];
        var accent = def.Swatches[3];
        dl.AddRectFilled(pos, pos + size, bg.U32());

        var topY = pos.Y;
        if (def.Stripes.Count > 1)
        {
            var barH = Ui.Px(5f);
            Ui.FlagBar(dl, pos, size.X, def.Stripes, barH);
            topY = pos.Y + barH;
        }

        var pad = Ui.Px(8f);
        var innerX = pos.X + pad;
        var innerW = size.X - (pad * 2f);
        var y = topY + pad;

        for (var i = 0; i < 3; i++)
            dl.AddCircleFilled(new Vector2(innerX + Ui.Px(3f) + (i * Ui.Px(7f)), y + Ui.Px(3f)), Ui.Px(2f), Palette.WithAlpha(ink, 0.35f).U32(), 10);

        var lineY = y + Ui.Px(15f);
        dl.AddRectFilled(new Vector2(innerX, lineY), new Vector2(innerX + (innerW * 0.62f), lineY + Ui.Px(3f)), Palette.WithAlpha(ink, 0.6f).U32());
        dl.AddRectFilled(new Vector2(innerX, lineY + Ui.Px(8f)), new Vector2(innerX + (innerW * 0.44f), lineY + Ui.Px(11f)), Palette.WithAlpha(ink, 0.3f).U32());

        var pillW = Ui.Px(16f);
        dl.AddRectFilled(new Vector2((pos.X + size.X) - pad - pillW, lineY), new Vector2((pos.X + size.X) - pad, lineY + Ui.Px(8f)), accent.U32());

        var dotY = lineY + Ui.Px(20f);
        dl.AddCircleFilled(new Vector2(innerX + Ui.Px(2f), dotY + Ui.Px(2f)), Ui.Px(2.5f), accent.U32(), 10);
        dl.AddRectFilled(new Vector2(innerX + Ui.Px(9f), dotY), new Vector2(innerX + (innerW * 0.5f), dotY + Ui.Px(3f)), Palette.WithAlpha(ink, 0.4f).U32());
    }

    private void DrawHeader(float fullWidth, float pad, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var midY = origin.Y + (height * 0.5f);

        var back = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, back);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##theme_back", backSize))
            this.router.Navigate(Screen.Settings);
        Ui.TextAt(dl, this.fonts.Icon, ImGui.GetItemRectMin(), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32(), back);

        const string title = "THEME";
        var titleSize = Ui.Measure(this.fonts.Eyebrow, title);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(origin.X + ((fullWidth - titleSize.X) * 0.5f), midY - (titleSize.Y * 0.5f)), Palette.TextSecondary.U32(), title);

        dl.AddLine(new Vector2(origin.X, origin.Y + height), new Vector2(origin.X + fullWidth, origin.Y + height), Palette.Border.U32(), 1f);
    }
}
