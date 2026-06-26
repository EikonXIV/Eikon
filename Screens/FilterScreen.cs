using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Discovery filter. Opened from the grid Tags chip. Full bleed with a header, a scrollable facet
// list, and a Show results button that returns to the grid. State is local for now and is applied
// to the server query in phase C. The after dark facets gate on the user's NSFW setting later.
internal sealed class FilterScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
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

    public FilterScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, DiscoveryService discovery)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.discovery = discovery;
    }

    public Screen Id => Screen.Filter;

    public bool Chrome => false;

    public void Draw()
    {
        var avail = ImGui.GetContentRegionAvail();
        var origin = ImGui.GetCursorScreenPos();
        var pad = Ui.Px(16f);
        var contentWidth = avail.X - (pad * 2f);
        var headerHeight = Ui.Px(54f);
        var footerHeight = Ui.Px(64f);

        this.DrawHeader(origin, avail.X, pad);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("filter_body", new Vector2(avail.X, avail.Y - headerHeight - footerHeight)))
        {
            if (body.Success)
            {
                ImGui.Indent(pad);
                using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
                    this.DrawFacets(contentWidth);
                ImGui.Unindent(pad);
            }
        }

        ImGui.SetCursorPos(new Vector2(pad, avail.Y - footerHeight + Ui.Px(12f)));
        if (this.kit.PrimaryButton("##filter_apply", "Show results", contentWidth))
        {
            this.discovery.Apply(this.BuildQuery());
            this.router.Navigate(Screen.Grid);
        }
    }

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

    private void DrawHeader(Vector2 origin, float fullWidth, float pad)
    {
        var drawList = ImGui.GetWindowDrawList();
        var midY = Ui.Px(27f);

        // Back to the grid without applying any changes.
        var backGlyph = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, backGlyph);
        ImGui.SetCursorPos(new Vector2(pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##filter_back", backSize))
            this.router.Navigate(Screen.Grid);
        Ui.TextAt(drawList, this.fonts.Icon, ImGui.GetItemRectMin(), Palette.TextSecondary.U32(), backGlyph);

        var titleSize = Ui.Measure(this.fonts.Title, "Filters");
        ImGui.SetCursorPos(new Vector2(pad + backSize.X + Ui.Px(12f), midY - (titleSize.Y * 0.5f)));
        using (this.fonts.Title.Push())
            ImGui.TextUnformatted("Filters");

        const string reset = "Reset";
        var resetSize = Ui.Measure(this.fonts.Caption, reset);
        ImGui.SetCursorPos(new Vector2(fullWidth - pad - resetSize.X, midY - (resetSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##filter_reset", resetSize))
            this.Reset();
        Ui.TextAt(drawList, this.fonts.Caption, ImGui.GetItemRectMin(), this.theme.Accent.U32(), reset);

        drawList.AddLine(
            new Vector2(origin.X, origin.Y + Ui.Px(53f)),
            new Vector2(origin.X + fullWidth, origin.Y + Ui.Px(53f)),
            Palette.Border.U32(), 1f);
    }

    private void DrawFacets(float contentWidth)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));   // breathing room below the header
        this.tier = this.kit.Segmented("##f_tier", new[] { "World", "DC", "Region" }, this.tier, contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        var onlineRowX = ImGui.GetCursorPosX();
        using (this.fonts.Body.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
            ImGui.TextUnformatted("Online now only");
        ImGui.SameLine();
        ImGui.SetCursorPosX(onlineRowX + contentWidth - Ui.Px(38f));   // right-align the toggle
        this.onlineOnly = this.kit.Toggle("##f_online", this.onlineOnly);

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.kit.SectionLabel("Looking for");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        Toggle(this.lookingFor, this.kit.ChipFlow("f_lf", Options.LookingFor, i => this.lookingFor[i], contentWidth));

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.kit.SectionLabel("Minimum age");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        this.ageMin = this.kit.Stepper("##f_agemin", this.ageMin, 18, 99);
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.kit.SectionLabel("Maximum age");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        this.ageMax = this.kit.Stepper("##f_agemax", this.ageMax, 18, 99);

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.kit.SectionLabel("Body / tribe");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        Toggle(this.tribes, this.kit.ChipFlow("f_tribe", Options.Tribes, i => this.tribes[i], contentWidth));

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.kit.SectionLabel("Gender");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        Toggle(this.genders, this.kit.ChipFlow("f_gn", Options.Genders, i => this.genders[i], contentWidth));

        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        using (this.fonts.Body.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
            ImGui.TextUnformatted("After dark (18+)");

        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.kit.SectionLabel("Position");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        Toggle(this.positions, this.kit.ChipFlow("f_pos", Options.Positions, i => this.positions[i], contentWidth));

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel("Meet");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        Toggle(this.meet, this.kit.ChipFlow("f_meet", Options.Meet, i => this.meet[i], contentWidth));

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel("Kinks");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        Toggle(this.kinks, this.kit.ChipFlow("f_kink", Options.Kinks, i => this.kinks[i], contentWidth));

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
    }

    private static void Toggle(bool[] set, int clicked)
    {
        if (clicked >= 0)
            set[clicked] = !set[clicked];
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
    }
}
