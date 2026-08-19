using Dalamud.Interface;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Data center travel picker (warm-editorial), reached from the grid's scope row or Settings. Full bleed
// like the filter sheet: a back / DATA CENTER TRAVEL / HOME ONLY header, one chip block per region with
// the home data center pinned on, and a sticky footer (Home only + Travel). Travel applies the draft
// through TravelService, which persists it and arms the grid's crossing.
internal sealed class DcTravelScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly WorldCatalog catalog;
    private readonly ProfileService profiles;
    private readonly TravelService travel;
    private readonly Selection selection;

    private readonly HashSet<int> draft = new();
    private bool entered;

    public DcTravelScreen(ScreenRouter router, Kit kit, UiFonts fonts, WorldCatalog catalog, ProfileService profiles, TravelService travel, Selection selection)
    {
        this.router = router;
        this.kit = kit;
        this.fonts = fonts;
        this.catalog = catalog;
        this.profiles = profiles;
        this.travel = travel;
        this.selection = selection;
    }

    public Screen Id => Screen.DcTravel;

    public bool Chrome => false;

    public void Draw()
    {
        if (!this.entered)
        {
            this.entered = true;
            this.catalog.EnsureLoaded();
            this.profiles.EnsureLoaded();
            this.draft.Clear();
            foreach (var id in this.travel.DcIds)
                this.draft.Add(id);
        }

        var avail = ImGui.GetContentRegionAvail();
        var headerHeight = Ui.Px(44f);
        var footerHeight = Ui.Px(68f);

        this.DrawHeader(avail.X, headerHeight);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("dct_body", new Vector2(avail.X, avail.Y - headerHeight - footerHeight), false, ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            if (body.Success)
                this.DrawBlocks(ImGui.GetContentRegionAvail().X);
        }

        this.DrawFooter(avail, footerHeight);
    }

    private int HomeId => this.travel.HomeDc?.Id ?? 0;

    private int AwayCount => this.draft.Count(id => id != this.HomeId);

    private void DrawHeader(float fullWidth, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var midY = origin.Y + (height * 0.5f);
        var pad = Ui.Px(14f);

        var backGlyph = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, backGlyph);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##dct_back", backSize))
            this.Leave();
        Ui.TextAt(dl, this.fonts.Icon, ImGui.GetItemRectMin(), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32(), backGlyph);

        var away = this.AwayCount;
        var title = away > 0 ? $"DATA CENTER TRAVEL · +{away}" : "DATA CENTER TRAVEL";
        var titleSize = Ui.Measure(this.fonts.Eyebrow, title);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(origin.X + ((fullWidth - titleSize.X) * 0.5f), midY - (titleSize.Y * 0.5f)), Palette.TextSecondary.U32(), title);

        const string clear = "HOME ONLY";
        var clearSize = Ui.Measure(this.fonts.Eyebrow, clear);
        ImGui.SetCursorScreenPos(new Vector2((origin.X + fullWidth) - pad - clearSize.X, midY - (clearSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##dct_clear", clearSize))
            this.draft.Clear();
        Ui.TextAt(dl, this.fonts.Eyebrow, ImGui.GetItemRectMin(), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextMuted).U32(), clear);

        dl.AddLine(new Vector2(origin.X, origin.Y + height), new Vector2(origin.X + fullWidth, origin.Y + height), Palette.Border.U32(), 1f);
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + height));
    }

    private void DrawBlocks(float fullWidth)
    {
        var pad = Ui.Px(20f);
        var contentWidth = fullWidth - (pad * 2f);

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        ImGui.Indent(pad);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentWidth);
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextWrapped("Your home data center is always included. Pick others to browse alongside it, in any region.");
        ImGui.PopTextWrapPos();
        ImGui.Unindent(pad);

        if (!this.catalog.Ready)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(24f)));
            Ui.CenteredText(fullWidth, this.fonts.Caption, Palette.TextMuted, "Loading worlds…");
            return;
        }

        var homeId = this.HomeId;
        var homeRegion = this.travel.HomeDc is { } home ? WorldCatalog.RegionCode(home.Region) : null;
        var regions = WorldCatalog.RegionOrder.OrderBy(r => r == homeRegion ? 0 : 1).ToList();
        foreach (var region in regions)
        {
            var dcs = this.catalog.DataCenters.Where(d => WorldCatalog.RegionCode(d.Region) == region).ToList();
            if (dcs.Count == 0)
                continue;

            var title = region == homeRegion ? $"{WorldCatalog.RegionName(region)} · Home" : WorldCatalog.RegionName(region);
            this.BlockTop(title, pad);
            ImGui.Indent(pad);
            var clicked = this.kit.ChipFlow(
                $"dct_{region}",
                dcs.Select(d => d.Id == homeId ? $"{d.Name} · Home" : d.Name).ToList(),
                i => dcs[i].Id == homeId || this.draft.Contains(dcs[i].Id),
                contentWidth,
                showCheck: true,
                disabled: i => dcs[i].Id == homeId);
            ImGui.Unindent(pad);
            if (clicked >= 0 && dcs[clicked].Id != homeId)
            {
                if (!this.draft.Remove(dcs[clicked].Id))
                    this.draft.Add(dcs[clicked].Id);
            }

            if (region != regions[^1])
                this.BlockBottom(fullWidth);
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
    }

    private void BlockTop(string title, float pad)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        var pos = ImGui.GetCursorScreenPos();
        Ui.TextAt(ImGui.GetWindowDrawList(), this.fonts.Eyebrow, new Vector2(pos.X + pad, pos.Y), Palette.TextSecondary.U32(), title.ToUpperInvariant());
        ImGui.Dummy(new Vector2(0f, Ui.Measure(this.fonts.Eyebrow, "X").Y + Ui.Px(12f)));
    }

    private void BlockBottom(float fullWidth)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        var pos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(pos, new Vector2(pos.X + fullWidth, pos.Y), Palette.Border.U32(), 1f);
        ImGui.Dummy(new Vector2(0f, 1f));
    }

    private void DrawFooter(Vector2 avail, float height)
    {
        var pad = Ui.Px(20f);
        var gap = Ui.Px(10f);
        var dl = ImGui.GetWindowDrawList();

        ImGui.SetCursorPos(new Vector2(0f, avail.Y - height));
        var top = ImGui.GetCursorScreenPos();
        dl.AddLine(top, new Vector2(top.X + avail.X, top.Y), Palette.Border.U32(), 1f);

        var width = avail.X - (pad * 2f) - gap;
        var homeWidth = MathF.Floor(width * 0.4f);
        var goWidth = width - homeWidth;
        ImGui.SetCursorPos(new Vector2(pad, (avail.Y - height) + Ui.Px(15f)));
        if (this.kit.SecondaryButton("##dct_home", "Home only", homeWidth))
        {
            this.travel.Apply(Array.Empty<int>());
            this.Leave();
            return;
        }

        ImGui.SameLine(0f, gap);
        if (this.kit.PrimaryButton("##dct_go", "Travel", goWidth))
        {
            this.travel.Apply(this.draft);
            this.Leave();
        }
    }

    private void Leave()
    {
        this.entered = false;
        this.router.Navigate(this.selection.TravelReturn);
    }
}
