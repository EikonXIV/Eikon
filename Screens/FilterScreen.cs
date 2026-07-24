using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Discovery filter sheet (warm-editorial). Full bleed: a back / FILTERS / RESET header, hairline-
// separated facet blocks (scope segments, an online-only switch row, tag-chip clouds, twin age
// steppers, an After dark serif divider), and a sticky ink CTA carrying a live first-page result
// count previewed against the server as the draft changes.
internal sealed class FilterScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly DiscoveryService discovery;

    private int tier;
    private bool onlineOnly;
    private int ageMin = 18;
    private int ageMax = 99;
    private readonly bool[] lookingFor = new bool[Options.LookingFor.Length];
    private readonly bool[] tribes = new bool[Options.Tribes.Length];
    private readonly bool[] genders = new bool[Options.Genders.Length];
    private readonly bool[] positions = new bool[Options.Positions.Length];
    private readonly bool[] meet = new bool[Options.Meet.Length];
    private readonly bool[] kinks = new bool[Options.Kinks.Length];

    private bool entered;           // seeded from the live query when the screen was opened
    private double previewDueAt;    // a draft change happened; run the preview once this time passes

    public FilterScreen(ScreenRouter router, Kit kit, UiFonts fonts, DiscoveryService discovery)
    {
        this.router = router;
        this.kit = kit;
        this.fonts = fonts;
        this.discovery = discovery;
    }

    public Screen Id => Screen.Filter;

    public bool Chrome => false;

    public void Draw()
    {
        if (!this.entered)
        {
            // Opening the sheet adopts the grid's live tier/online so the draft starts from what is
            // actually showing, then previews the untouched draft for the CTA count.
            this.entered = true;
            this.tier = this.discovery.TierIndex;
            this.onlineOnly = this.discovery.OnlineOnly;
            this.discovery.Preview(this.BuildQuery());
        }

        // Debounced live count: any draft change arms the timer; fire one preview once edits pause.
        if (this.previewDueAt > 0.0 && ImGui.GetTime() >= this.previewDueAt)
        {
            this.previewDueAt = 0.0;
            this.discovery.Preview(this.BuildQuery());
        }

        var avail = ImGui.GetContentRegionAvail();
        var headerHeight = Ui.Px(44f);
        var footerHeight = Ui.Px(68f);

        this.DrawHeader(avail.X, headerHeight);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("filter_body", new Vector2(avail.X, avail.Y - headerHeight - footerHeight), false, ImGuiWindowFlags.NoScrollbar))
        {
            if (body.Success)
                this.DrawBlocks(avail.X);
        }

        this.DrawFooter(avail, footerHeight);
    }

    // ---- header ----

    private void DrawHeader(float fullWidth, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var midY = origin.Y + (height * 0.5f);
        var pad = Ui.Px(14f);

        var backGlyph = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, backGlyph);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##filter_back", backSize))
            this.Leave(Screen.Grid);
        Ui.TextAt(dl, this.fonts.Icon, ImGui.GetItemRectMin(), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32(), backGlyph);

        var active = this.ActiveCount();
        var title = active > 0 ? $"FILTERS · {active}" : "FILTERS";
        var titleSize = Ui.Measure(this.fonts.Eyebrow, title);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(origin.X + ((fullWidth - titleSize.X) * 0.5f), midY - (titleSize.Y * 0.5f)), Palette.TextSecondary.U32(), title);

        const string reset = "RESET";
        var resetSize = Ui.Measure(this.fonts.Eyebrow, reset);
        ImGui.SetCursorScreenPos(new Vector2((origin.X + fullWidth) - pad - resetSize.X, midY - (resetSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##filter_reset", resetSize))
            this.Reset();
        Ui.TextAt(dl, this.fonts.Eyebrow, ImGui.GetItemRectMin(), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextMuted).U32(), reset);

        dl.AddLine(new Vector2(origin.X, origin.Y + height), new Vector2(origin.X + fullWidth, origin.Y + height), Palette.Border.U32(), 1f);
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + height));
    }

    // ---- blocks ----

    private void DrawBlocks(float fullWidth)
    {
        var pad = Ui.Px(20f);
        var contentWidth = fullWidth - (pad * 2f);

        this.BlockTop("Scope", pad);
        this.DrawScope(pad, contentWidth);
        this.BlockBottom(fullWidth);

        this.DrawOnlineRow(fullWidth, pad);

        this.BlockTop("Looking for", pad);
        this.Toggle(this.lookingFor, this.ChipCloud("f_lf", Options.LookingFor, this.lookingFor, pad, contentWidth));
        this.BlockBottom(fullWidth);

        this.BlockTop("Age", pad);
        this.DrawAge(pad, contentWidth);
        this.BlockBottom(fullWidth);

        this.BlockTop("Body / tribe", pad);
        this.Toggle(this.tribes, this.ChipCloud("f_tribe", Options.Tribes, this.tribes, pad, contentWidth));
        this.BlockBottom(fullWidth);

        this.BlockTop("Gender", pad);
        this.Toggle(this.genders, this.ChipCloud("f_gn", Options.Genders, this.genders, pad, contentWidth));
        this.BlockBottom(fullWidth);

        this.DrawAfterDarkHeading(pad, contentWidth);

        this.BlockTop("Position", pad);
        this.Toggle(this.positions, this.ChipCloud("f_pos", Options.Positions, this.positions, pad, contentWidth));
        this.BlockBottom(fullWidth);

        this.BlockTop("Meet", pad);
        this.Toggle(this.meet, this.ChipCloud("f_meet", Options.Meet, this.meet, pad, contentWidth));
        this.BlockBottom(fullWidth);

        // The last block draws no closing hairline: the footer's top border is the divider there,
        // and two rules would stack.
        this.BlockTop("Kinks", pad);
        this.Toggle(this.kinks, this.ChipCloud("f_kink", Options.Kinks, this.kinks, pad, contentWidth));

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
    }

    // Eyebrow block heading, padded in from the full-bleed edge.
    private void BlockTop(string title, float pad)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        var pos = ImGui.GetCursorScreenPos();
        Ui.TextAt(ImGui.GetWindowDrawList(), this.fonts.Eyebrow, new Vector2(pos.X + pad, pos.Y), Palette.TextSecondary.U32(), title.ToUpperInvariant());
        ImGui.Dummy(new Vector2(0f, Ui.Measure(this.fonts.Eyebrow, "X").Y + Ui.Px(12f)));
    }

    // Full-bleed hairline closing a block.
    private void BlockBottom(float fullWidth)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        var pos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(pos, new Vector2(pos.X + fullWidth, pos.Y), Palette.Border.U32(), 1f);
        ImGui.Dummy(new Vector2(0f, 1f));
    }

    // Three bordered segments sharing internal hairlines; the active one fills with ink.
    private void DrawScope(float pad, float contentWidth)
    {
        var labels = new[] { "WORLD", "DC", "REGION" };
        var height = Ui.Px(40f);
        var origin = ImGui.GetCursorScreenPos();
        var left = origin.X + pad;
        var segWidth = contentWidth / 3f;
        var dl = ImGui.GetWindowDrawList();

        for (var i = 0; i < labels.Length; i++)
        {
            var pos = new Vector2(left + (i * segWidth), origin.Y);
            ImGui.SetCursorScreenPos(pos);
            if (ImGui.InvisibleButton("##scope" + i, new Vector2(segWidth, height)) && this.tier != i)
            {
                this.tier = i;
                this.Changed();
            }

            var hovered = ImGui.IsItemHovered();
            if (this.tier == i)
                dl.AddRectFilled(pos, pos + new Vector2(segWidth, height), Palette.TextPrimary.U32(), 0f);
            var textColor = this.tier == i ? Palette.Paper : (hovered ? Palette.TextPrimary : Palette.TextMuted);
            var textSize = Ui.Measure(this.fonts.Eyebrow, labels[i]);
            Ui.TextAt(dl, this.fonts.Eyebrow, pos + ((new Vector2(segWidth, height) - textSize) * 0.5f), textColor.U32(), labels[i]);
            if (i > 0)
                dl.AddLine(pos, new Vector2(pos.X, pos.Y + height), Palette.Border.U32(), 1f);
        }

        dl.AddRect(new Vector2(left, origin.Y), new Vector2(left + contentWidth, origin.Y + height), Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + height));
    }

    // Full-width tap row: green dot + label on the left, the switch on the right.
    private void DrawOnlineRow(float fullWidth, float pad)
    {
        var height = Ui.Px(52f);
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        ImGui.SetCursorScreenPos(origin);
        if (ImGui.InvisibleButton("##f_online_row", new Vector2(fullWidth, height)))
        {
            this.onlineOnly = !this.onlineOnly;
            this.Changed();
        }

        if (ImGui.IsItemHovered())
            dl.AddRectFilled(origin, origin + new Vector2(fullWidth, height), Palette.WithAlpha(Palette.Overlay, 0.03f).U32());

        var midY = origin.Y + (height * 0.5f);
        dl.AddCircleFilled(new Vector2(origin.X + pad + Ui.Px(3f), midY), Ui.Px(3f), Palette.Online.U32(), 12);
        const string label = "Online now only";
        var labelSize = Ui.Measure(this.fonts.Caption, label);
        Ui.TextAt(dl, this.fonts.Caption, new Vector2(origin.X + pad + Ui.Px(14f), midY - (labelSize.Y * 0.5f)), Palette.TextPrimary.U32(), label);

        DrawSwitch(dl, new Vector2((origin.X + fullWidth) - pad - Ui.Px(36f), midY - Ui.Px(10f)), this.onlineOnly);

        dl.AddLine(new Vector2(origin.X, origin.Y + height), new Vector2(origin.X + fullWidth, origin.Y + height), Palette.Border.U32(), 1f);
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + height + 1f));
    }

    // The switch visual only; the whole row is the tap target. Mirrors Kit.Toggle's editorial look.
    private static void DrawSwitch(ImDrawListPtr dl, Vector2 pos, bool value)
    {
        var size = new Vector2(Ui.Px(36f), Ui.Px(20f));
        if (value)
        {
            dl.AddRectFilled(pos, pos + size, Palette.TextPrimary.U32(), size.Y * 0.5f);
        }
        else
        {
            dl.AddRectFilled(pos, pos + size, Palette.Surface2.U32(), size.Y * 0.5f);
            dl.AddRect(pos, pos + size, Palette.BorderStrong.U32(), size.Y * 0.5f, ImDrawFlags.None, 1f);
        }

        var knobRadius = (size.Y * 0.5f) - Ui.Px(2f);
        var knobX = value ? pos.X + size.X - knobRadius - Ui.Px(2f) : pos.X + knobRadius + Ui.Px(2f);
        dl.AddCircleFilled(new Vector2(knobX, pos.Y + (size.Y * 0.5f)), knobRadius, (value ? Palette.Paper : Palette.TextMuted).U32(), 16);
    }

    private int ChipCloud(string id, string[] labels, bool[] flags, float pad, float contentWidth)
    {
        ImGui.Indent(pad);
        var hit = this.kit.ChipFlow(id, labels, i => flags[i], contentWidth);
        ImGui.Unindent(pad);
        return hit;
    }

    // Twin bordered steppers: MINIMUM / MAXIMUM eyebrow, minus, a serif count, plus. Cross-clamped so
    // the range can never invert.
    private void DrawAge(float pad, float contentWidth)
    {
        var gap = Ui.Px(12f);
        var cellWidth = (contentWidth - gap) * 0.5f;
        var origin = ImGui.GetCursorScreenPos();
        var left = origin.X + pad;

        var newMin = this.AgeCell("##f_agemin", new Vector2(left, origin.Y), cellWidth, "MINIMUM", this.ageMin, 18, this.ageMax);
        var newMax = this.AgeCell("##f_agemax", new Vector2(left + cellWidth + gap, origin.Y), cellWidth, "MAXIMUM", this.ageMax, this.ageMin, 99);
        if (newMin != this.ageMin || newMax != this.ageMax)
        {
            this.ageMin = newMin;
            this.ageMax = newMax;
            this.Changed();
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + Ui.Px(68f)));
    }

    private int AgeCell(string id, Vector2 pos, float width, string label, int value, int min, int max)
    {
        var height = Ui.Px(68f);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRect(pos, pos + new Vector2(width, height), Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);
        Ui.TextAt(dl, this.fonts.Eyebrow, pos + new Vector2(Ui.Px(12f), Ui.Px(8f)), Palette.TextSecondary.U32(), label);

        // The typeable serif value goes first so a blur from clicking a step control commits before it.
        var rowY = pos.Y + Ui.Px(28f);
        var result = this.kit.SerifNumberField(id, value, min, max, new Vector2(pos.X, rowY), width, Ui.Px(40f));

        if (this.StepButton(id + "_dec", new Vector2(pos.X, rowY), FontAwesomeIcon.Minus, result > min))
            result = Math.Max(min, result - 1);
        if (this.StepButton(id + "_inc", new Vector2((pos.X + width) - Ui.Px(40f), rowY), FontAwesomeIcon.Plus, result < max))
            result = Math.Min(max, result + 1);
        return result;
    }

    private bool StepButton(string id, Vector2 pos, FontAwesomeIcon icon, bool enabled)
    {
        var box = Ui.Px(40f);
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, new Vector2(box, box)) && enabled;
        var hovered = ImGui.IsItemHovered();
        var glyph = icon.ToIconString();
        var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
        var color = !enabled ? Palette.WithAlpha(Palette.TextMuted, 0.3f) : (hovered ? Palette.TextPrimary : Palette.TextMuted);
        Ui.TextAt(ImGui.GetWindowDrawList(), this.fonts.Icon, pos + ((new Vector2(box, box) - glyphSize) * 0.5f), color.U32(), glyph);
        return clicked;
    }

    // Serif "After dark" with a mono 18+ beside it, over its own hairline: the section divider between
    // the everyday facets and the adult set.
    private void DrawAfterDarkHeading(float pad, float contentWidth)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(24f)));
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        const string title = "After dark";
        var titleSize = Ui.Measure(this.fonts.Title, title);
        Ui.TextAt(dl, this.fonts.Title, new Vector2(pos.X + pad, pos.Y), Palette.TextPrimary.U32(), title);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(pos.X + pad + titleSize.X + Ui.Px(8f), (pos.Y + titleSize.Y) - Ui.Measure(this.fonts.Eyebrow, "18+").Y - Ui.Px(3f)), Palette.TextMuted.U32(), "18+");
        var lineY = pos.Y + titleSize.Y + Ui.Px(6f);
        dl.AddLine(new Vector2(pos.X + pad, lineY), new Vector2(pos.X + pad + contentWidth, lineY), Palette.Border.U32(), 1f);
        ImGui.Dummy(new Vector2(0f, titleSize.Y + Ui.Px(7f)));
    }

    // ---- footer ----

    private void DrawFooter(Vector2 avail, float height)
    {
        var pad = Ui.Px(20f);
        var dl = ImGui.GetWindowDrawList();

        ImGui.SetCursorPos(new Vector2(0f, avail.Y - height));
        var top = ImGui.GetCursorScreenPos();
        dl.AddLine(top, new Vector2(top.X + avail.X, top.Y), Palette.Border.U32(), 1f);

        ImGui.SetCursorPos(new Vector2(pad, (avail.Y - height) + Ui.Px(12f)));
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(avail.X - (pad * 2f), Ui.Px(44f));
        var clicked = ImGui.InvisibleButton("##filter_apply", size);
        var fill = ImGui.IsItemHovered() ? Palette.Overlay : Palette.TextPrimary;
        dl.AddRectFilled(pos, pos + size, fill.U32(), 0f);

        var count = this.discovery.PreviewCount + (this.discovery.PreviewMore ? "+" : string.Empty);
        var label = $"SHOW RESULTS  ·  {count}";
        var labelSize = Ui.Measure(this.fonts.Eyebrow, label);
        Ui.TextAt(dl, this.fonts.Eyebrow, pos + ((size - labelSize) * 0.5f), Palette.Paper.U32(), label);

        if (clicked)
        {
            this.discovery.Apply(this.BuildQuery());
            this.Leave(Screen.Grid);
        }
    }

    // ---- state ----

    private DiscoverQuery BuildQuery() => new()
    {
        Tier = DiscoveryService.TierOrder[this.tier],
        OnlineOnly = this.onlineOnly,
        LookingFor = ProfileMapper.LookingForOf(this.lookingFor),
        Tribes = ProfileMapper.TribesOf(this.tribes),
        Genders = ProfileMapper.GendersOf(this.genders),
        Races = new List<RaceElement>(),
        Positions = ProfileMapper.PositionsOf(this.positions),
        Kinks = Options.Kinks.Where((_, i) => this.kinks[i]).ToList(),
        AgeMin = this.ageMin,
        AgeMax = this.ageMax,
    };

    private void Toggle(bool[] set, int clicked)
    {
        if (clicked < 0)
            return;
        set[clicked] = !set[clicked];
        this.Changed();
    }

    // Facets that differ from the defaults, for the header's FILTERS · N. Scope is a view, not a filter.
    private int ActiveCount()
    {
        var n = 0;
        if (this.onlineOnly) n++;
        if (this.ageMin != 18 || this.ageMax != 99) n++;
        n += this.lookingFor.Count(v => v) + this.tribes.Count(v => v) + this.genders.Count(v => v);
        n += this.positions.Count(v => v) + this.meet.Count(v => v) + this.kinks.Count(v => v);
        return n;
    }

    private void Changed() => this.previewDueAt = ImGui.GetTime() + 0.4;

    private void Leave(Screen to)
    {
        this.entered = false;
        this.previewDueAt = 0.0;
        this.router.Navigate(to);
    }

    private void Reset()
    {
        this.tier = 0;
        this.onlineOnly = false;
        this.ageMin = 18;
        this.ageMax = 99;
        Array.Clear(this.lookingFor);
        Array.Clear(this.tribes);
        Array.Clear(this.genders);
        Array.Clear(this.positions);
        Array.Clear(this.meet);
        Array.Clear(this.kinks);
        this.Changed();
    }
}
