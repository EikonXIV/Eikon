using System;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Eikon.Config;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Discovery grid (warm-editorial). An editorial header (Discover / Nearby adventurers / scope count),
// underline scope tabs with the data center travel control, density + filter tools, an Online now /
// Favorites pill row (full filtering lives behind the header's filter tool), then a scrolling grid of
// square portrait tiles backed by /api/discover. Job/age tags await a server field; tiles show name +
// world (data center · world for members reached by travel). A change of travel set plays the crossing
// over the grid body.
internal sealed class GridScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly DiscoveryService discovery;
    private readonly Selection selection;
    private readonly PhotoService photoSvc;
    private readonly Configuration config;
    private readonly FavoritesService favorites;
    private readonly EventsBoardView eventsView;
    private readonly TravelService travel;
    private readonly TravelTransition transition = new();

    // Favorites is a filter over the same grid, not a separate screen: the chip swaps the source list
    // and everything else - header, tiles, density - stays exactly as it is.
    private bool favoritesOnly;

    // People vs Events: a sub-tab within the Grid destination (the bottom-nav "Grid" item stays active
    // for both), so the events board is not a fifth nav item. Same idea as favoritesOnly: swap the body.
    private bool showEvents;

    // A crossing just began: jump the grid scroll to the top under the plate so the arrival starts there.
    private bool scrollToTop;

    public GridScreen(ScreenRouter router, Kit kit, UiFonts fonts, DiscoveryService discovery, Selection selection, PhotoService photoSvc, Configuration config, FavoritesService favorites, EventsBoardView eventsView, TravelService travel)
    {
        this.router = router;
        this.kit = kit;
        this.fonts = fonts;
        this.discovery = discovery;
        this.selection = selection;
        this.photoSvc = photoSvc;
        this.config = config;
        this.favorites = favorites;
        this.eventsView = eventsView;
        this.travel = travel;
    }

    public Screen Id => Screen.Grid;

    public bool Chrome => true;

    // Harness seam (Vitrine has InternalsVisibleTo): open the Events sub-tab directly for a screenshot.
    internal void ShowEventsTab()
    {
        this.showEvents = true;
        this.eventsView.EnsureInitial();
    }

    // Harness seam: play the crossing on demand so its frames can be captured.
    internal void PlayCrossing() => this.transition.Begin(ImGui.GetTime(), this.travel.DestinationCaption());

    // Whichever list the Favorites chip has selected. Favorites arrive as one list, so the paging and
    // the "finding people" status below only apply to discovery.
    private IReadOnlyList<BasicProfileDto> Source =>
        this.favoritesOnly ? this.favorites.Profiles : this.discovery.Profiles;

    public void Draw()
    {
        var pad = Ui.Px(20f);
        var fullWidth = ImGui.GetContentRegionAvail().X;
        this.DrawTopToggle(fullWidth, pad);

        if (this.showEvents)
        {
            this.eventsView.Draw(pad);
            return;
        }

        if (this.favoritesOnly)
            this.favorites.EnsureLoaded();
        else
            this.discovery.EnsureInitial();

        this.travel.EnsureLoaded();
        var now = ImGui.GetTime();
        if (this.travel.TakeCrossing())
        {
            this.transition.Begin(now, this.travel.DestinationCaption());
            this.scrollToTop = true;
        }

        this.transition.Update(now, this.discovery.Reloading);

        var avail = ImGui.GetContentRegionAvail();
        var width = avail.X;

        this.DrawHeader(width, pad);
        this.DrawChips(width, pad);

        var bodyAvail = ImGui.GetContentRegionAvail();
        using var scroll = ImRaii.Child("grid_scroll", bodyAvail, false, ImGuiWindowFlags.AlwaysVerticalScrollbar);
        if (!scroll.Success)
            return;

        if (this.scrollToTop)
        {
            ImGui.SetScrollY(0f);
            this.scrollToTop = false;
        }

        this.DrawBody(ImGui.GetContentRegionAvail().X, now);
        this.DrawCrossing(now);
    }

    private void DrawBody(float contentWidth, double now)
    {
        var loading = this.favoritesOnly ? !this.favorites.Loaded : this.discovery.Loading;
        if (loading && this.Source.Count == 0)
        {
            this.DrawStatus(contentWidth, this.favoritesOnly ? "Loading favorites…" : "Finding people…");
            return;
        }

        var compact = this.config.GridLayout == 1;
        var interactive = !this.transition.BlocksInput;
        var shown = this.DrawGrid(contentWidth, compact, interactive, now);
        if (shown == 0)
        {
            this.DrawEmpty(contentWidth, interactive);
            return;
        }

        // Infinite scroll: pull the next page as the viewer nears the bottom of the grid scroll region.
        // Favorites come back whole, so there is nothing to page. Held while a crossing plays.
        if (!this.favoritesOnly && this.discovery.HasMore && this.transition.Current == TravelTransition.Phase.Idle)
        {
            if (this.discovery.Loading)
                this.DrawStatus(contentWidth, "Loading more…");
            else if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - Ui.Px(240f))
                this.discovery.LoadMore();
        }
    }

    // People / Events sub-tab row at the very top: two serif tabs with an underline under the active one.
    // People shows the discovery grid; Events shows the board. Both keep the shell header + bottom nav.
    private void DrawTopToggle(float width, float pad)
    {
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var y = origin.Y + Ui.Px(12f);
        var tabH = Ui.Measure(this.fonts.SerifName, "People").Y;
        var x = origin.X + pad;
        var hairY = y + tabH + Ui.Px(12f);
        var activeX = 0f;
        var activeW = 0f;

        void Tab(string label, bool active, Action onClick)
        {
            var size = Ui.Measure(this.fonts.SerifName, label);
            ImGui.SetCursorScreenPos(new Vector2(x, y));
            if (ImGui.InvisibleButton($"##toptab_{label}", new Vector2(size.X, tabH)))
                onClick();
            var col = (active || ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32();
            Ui.TextAt(dl, this.fonts.SerifName, new Vector2(x, y), col, label);
            if (active)
            {
                activeX = x;
                activeW = size.X;
            }

            x += size.X + Ui.Px(30f);
        }

        Tab("People", !this.showEvents, () => this.showEvents = false);
        Tab("Events", this.showEvents, () =>
        {
            this.showEvents = true;
            this.eventsView.EnsureInitial();
        });

        // The active tab's tick sits ON the full-width rule (coincident), like the bottom nav's active
        // marker on its border: draw the faint rule, then overdraw the active segment in ink.
        dl.AddLine(new Vector2(origin.X, hairY), new Vector2(origin.X + width, hairY), Palette.Border.U32(), 1f);
        if (activeW > 0f)
            dl.AddLine(new Vector2(activeX, hairY), new Vector2(activeX + activeW, hairY), Palette.TextPrimary.U32(), Ui.Px(1.5f));
        ImGui.SetCursorScreenPos(new Vector2(origin.X, hairY));
    }

    private void DrawHeader(float width, float pad)
    {
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var left = origin.X + pad;
        var right = (origin.X + width) - pad;

        var eyebrowH = Ui.Measure(this.fonts.Eyebrow, "X").Y;
        var titleH = Ui.Measure(this.fonts.SerifTitle, "Nearby").Y;
        var tabH = Ui.Measure(this.fonts.Eyebrow, "WORLD").Y;
        var height = Ui.Px(18f) + eyebrowH + Ui.Px(4f) + titleH + Ui.Px(16f) + tabH + Ui.Px(14f);

        var y = origin.Y + Ui.Px(18f);

        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(left, y), Palette.TextSecondary.U32(), this.favoritesOnly ? "SAVED" : "DISCOVER");
        var scope = this.favoritesOnly ? "STARRED" : "IN SCOPE";
        var scopeW = Ui.Measure(this.fonts.Eyebrow, scope).X;
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(right - scopeW, y), Palette.TextSecondary.U32(), scope);
        y += eyebrowH + Ui.Px(4f);

        // Two-tone title: roman lead, italic tail. The filter swaps the words, not the treatment.
        var lead = this.favoritesOnly ? "Your " : "Nearby ";
        var tail = this.favoritesOnly ? "favorites" : "adventurers";
        var leadW = Ui.Measure(this.fonts.SerifTitle, lead).X;
        Ui.TextAt(dl, this.fonts.SerifTitle, new Vector2(left, y), Palette.TextPrimary.U32(), lead);
        Ui.TextAt(dl, this.fonts.SerifItalicTitle, new Vector2(left + leadW, y), Palette.TextSecondary.U32(), tail);
        var count = this.Source.Count.ToString("N0");
        var countSize = Ui.Measure(this.fonts.Count, count);
        Ui.TextAt(dl, this.fonts.Count, new Vector2(right - countSize.X, (y + titleH) - countSize.Y), Palette.TextPrimary.U32(), count);
        y += titleH + Ui.Px(16f);

        // Scope tabs (left), underline-active.
        var tabX = left;
        foreach (var tier in DiscoveryService.TierOrder)
        {
            var label = tier.ToString().ToUpperInvariant();
            var labelSize = Ui.Measure(this.fonts.Eyebrow, label);
            ImGui.SetCursorScreenPos(new Vector2(tabX, y));
            if (ImGui.InvisibleButton($"##tier_{tier}", new Vector2(labelSize.X, tabH)))
                this.discovery.SetTier(tier);
            var active = this.discovery.Tier == tier;
            var col = (active || ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32();
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(tabX, y), col, label);
            if (active)
                dl.AddLine(new Vector2(tabX, y + tabH + Ui.Px(3f)), new Vector2(tabX + labelSize.X, y + tabH + Ui.Px(3f)), Palette.TextPrimary.U32(), 1f);
            tabX += labelSize.X + Ui.Px(18f);
        }

        // Data center travel control after the tabs: a hairline, then the mark's diamond (gold while
        // travelling) with the count of away data centers. Opens the picker.
        tabX -= Ui.Px(6f);
        dl.AddLine(new Vector2(tabX, y), new Vector2(tabX, y + tabH), Palette.Border.U32(), 1f);
        tabX += Ui.Px(13f);
        this.DrawTravelControl(dl, new Vector2(tabX, y), tabH);

        // Tools (right): filters · density expanded · density compact.
        var tool = new Vector2(Ui.Px(24f), tabH + Ui.Px(4f));
        var toolY = y - Ui.Px(3f);
        var compact = this.config.GridLayout == 1;
        var tx = right - tool.X;
        this.ToolIcon(dl, "##dens_compact", FontAwesomeIcon.Th, new Vector2(tx, toolY), tool, compact, () => this.SetLayout(1));
        tx -= tool.X;
        this.ToolIcon(dl, "##dens_expanded", FontAwesomeIcon.ThLarge, new Vector2(tx, toolY), tool, !compact, () => this.SetLayout(0));
        tx -= Ui.Px(9f);
        dl.AddLine(new Vector2(tx, y), new Vector2(tx, y + tabH), Palette.Border.U32(), 1f);
        tx -= Ui.Px(9f) + tool.X;
        this.ToolIcon(dl, "##filters", FontAwesomeIcon.SlidersH, new Vector2(tx, toolY), tool, false, () => this.router.Navigate(Screen.Filter));

        var hairY = origin.Y + height;
        dl.AddLine(new Vector2(origin.X, hairY), new Vector2(origin.X + width, hairY), Palette.Border.U32(), 1f);
        ImGui.SetCursorScreenPos(new Vector2(origin.X, hairY));
    }

    private void DrawTravelControl(ImDrawListPtr dl, Vector2 pos, float tabH)
    {
        var travelling = this.travel.Travelling;
        var label = travelling ? $"+{this.travel.AwayCount}" : string.Empty;
        var labelSize = label.Length > 0 ? Ui.Measure(this.fonts.Eyebrow, label) : Vector2.Zero;
        var diamondH = tabH * 0.5f;
        var diamondW = diamondH * (4f / 5.5f) * 2f;
        var gap = label.Length > 0 ? Ui.Px(6f) : 0f;
        var size = new Vector2(diamondW + gap + labelSize.X + Ui.Px(4f), tabH);

        ImGui.SetCursorScreenPos(new Vector2(pos.X - Ui.Px(2f), pos.Y));
        if (ImGui.InvisibleButton("##travel_open", size))
            this.OpenTravel();
        var hovered = ImGui.IsItemHovered();

        var color = travelling ? Palette.Signal : (hovered ? Palette.TextPrimary : Palette.TextMuted);
        var center = new Vector2(pos.X + (diamondW * 0.5f), pos.Y + (tabH * 0.5f));
        Ui.Diamond(dl, center, diamondH, color.U32(), Ui.Px(1.2f));
        if (travelling)
            dl.AddCircleFilled(center, Ui.Px(1.6f), color.U32(), 8);
        if (label.Length > 0)
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(pos.X + diamondW + gap, pos.Y), (hovered ? Palette.TextPrimary : Palette.Signal).U32(), label);
    }

    private void OpenTravel()
    {
        this.selection.TravelReturn = Screen.Grid;
        this.router.Navigate(Screen.DcTravel);
    }

    private void SetLayout(int layout)
    {
        this.config.GridLayout = layout;
        this.config.Save();
    }

    private void ToolIcon(ImDrawListPtr dl, string id, FontAwesomeIcon icon, Vector2 pos, Vector2 size, bool active, Action onClick)
    {
        ImGui.SetCursorScreenPos(pos);
        if (ImGui.InvisibleButton(id, size))
            onClick();
        var col = (active || ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32();
        var glyph = icon.ToIconString();
        var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(dl, this.fonts.Icon, ImGui.GetItemRectMin() + ((size - glyphSize) * 0.5f), col, glyph);
    }

    private void DrawChips(float width, float pad)
    {
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var padY = Ui.Px(12f);
        var chipH = Ui.Px(28f);
        var y = origin.Y + padY;
        var x = origin.X + pad;

        if (this.PillChip(dl, "##chip_online", "Online now", this.discovery.OnlineOnly, true, ref x, y, chipH))
            this.discovery.SetOnline(!this.discovery.OnlineOnly);
        if (this.PillChip(dl, "##chip_favs", "Favorites", this.favoritesOnly, false, ref x, y, chipH))
            this.favoritesOnly = !this.favoritesOnly;

        // Refresh: re-pull discovery from the top so members who just came online surface. Right-aligned
        // and sized to the chip height so it sits level; spins and swallows clicks while reloading.
        var refreshPos = new Vector2((origin.X + width - pad) - chipH, y);
        if (this.kit.HeaderIconButton(dl, "##grid_refresh", FontAwesomeIcon.SyncAlt.ToIconString(), refreshPos, chipH, this.discovery.Reloading))
            this.discovery.Refresh();

        var hairY = origin.Y + padY + chipH + padY;
        dl.AddLine(new Vector2(origin.X, hairY), new Vector2(origin.X + width, hairY), Palette.Border.U32(), 1f);
        ImGui.SetCursorScreenPos(new Vector2(origin.X, hairY));
    }

    private bool PillChip(ImDrawListPtr dl, string id, string label, bool active, bool dot, ref float x, float y, float h)
    {
        var textSize = Ui.Measure(this.fonts.Label, label);
        var padX = Ui.Px(12f);
        var dotSpace = dot ? Ui.Px(13f) : 0f;
        var w = textSize.X + (padX * 2f) + dotSpace;
        var pos = new Vector2(x, y);
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
        var hovered = ImGui.IsItemHovered();
        var rounding = h * 0.5f;

        if (active)
            dl.AddRectFilled(pos, pos + new Vector2(w, h), Palette.TextPrimary.U32(), rounding);
        else
            dl.AddRect(pos, pos + new Vector2(w, h), (hovered ? Palette.BorderStrong : Palette.Border).U32(), rounding, ImDrawFlags.None, 1f);

        var tx = pos.X + padX;
        if (dot)
        {
            dl.AddCircleFilled(new Vector2(tx + Ui.Px(3f), pos.Y + (h * 0.5f)), Ui.Px(3f), Palette.Online.U32(), 12);
            tx += dotSpace;
        }

        var textCol = active ? Palette.Paper.U32() : (hovered ? Palette.TextPrimary : Palette.TextSecondary).U32();
        Ui.TextAt(dl, this.fonts.Label, new Vector2(tx, pos.Y + ((h - textSize.Y) * 0.5f)), textCol, label);
        x += w + Ui.Px(8f);
        return clicked;
    }

    private int DrawGrid(float childWidth, bool compact, bool interactive, double now)
    {
        var pad = Ui.Px(compact ? 12f : 16f);
        var gap = Ui.Px(compact ? 8f : 12f);
        var columns = compact ? 3 : 2;
        var contentWidth = childWidth - (pad * 2f);
        var tileWidth = (contentWidth - (gap * (columns - 1))) / columns;
        var size = compact ? new Vector2(tileWidth, tileWidth) : new Vector2(tileWidth, tileWidth * 4f / 3f);

        ImGui.Indent(pad);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + pad);

        var shown = 0;
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap)))
        {
            foreach (var profile in this.Source)
            {
                if (shown % columns != 0)
                    ImGui.SameLine(0f, gap);

                if (this.DrawTile(profile, size, compact, interactive, this.transition.TileAlpha(shown, now)))
                {
                    this.selection.ProfileUserId = profile.UserId;
                    this.selection.ProfileDisplayName = profile.DisplayName;
                    this.selection.ProfileReturn = Screen.Grid;
                    this.router.Navigate(Screen.ProfileDetail);
                }

                shown++;
            }
        }

        ImGui.Unindent(pad);
        ImGui.Dummy(new Vector2(0f, pad));
        return shown;
    }

    // `interactive` false draws the tile inert (no button) while the crossing plate covers it, so the
    // overlay is the only thing under the mouse. `alpha` scales every color for the arrival stagger.
    private bool DrawTile(BasicProfileDto profile, Vector2 size, bool compact, bool interactive, float alpha)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = false;
        var hovered = false;
        if (interactive)
        {
            clicked = ImGui.InvisibleButton($"##tile_{profile.UserId}", size);
            hovered = ImGui.IsItemHovered();
        }
        else
        {
            ImGui.Dummy(size);
        }

        var dl = ImGui.GetWindowDrawList();
        uint Faded(Vector4 c) => Palette.WithAlpha(c, c.W * alpha).U32();

        // Square surface (editorial radius is 0).
        dl.AddRectFilled(pos, pos + size, Faded(Palette.Surface2));

        var texture = profile.MainPhotoId is { } photoId ? this.photoSvc.Texture(photoId) : null;
        if (texture != null)
        {
            var (uvMin, uvMax) = Ui.CoverUv(texture.Width, texture.Height, size.X / size.Y);
            dl.AddImage(texture.Handle, pos, pos + size, uvMin, uvMax, Faded(hovered ? Palette.WithAlpha(Palette.White, 0.9f) : Palette.White));
        }
        else
        {
            var initial = profile.DisplayName.Length > 0 ? profile.DisplayName[..1].ToUpperInvariant() : "?";
            var initialSize = Ui.Measure(this.fonts.SerifTitle, initial);
            Ui.TextAt(dl, this.fonts.SerifTitle,
                pos + new Vector2((size.X - initialSize.X) * 0.5f, (size.Y * 0.4f) - (initialSize.Y * 0.5f)),
                Faded(Palette.TextMuted), initial);
        }

        // Presence dot, top-right: green online, grey otherwise.
        var dotInset = Ui.Px(compact ? 9f : 11f);
        var dotColor = Faded(profile.Online ? Palette.Online : Palette.Afk);
        dl.AddCircleFilled(pos + new Vector2(size.X - dotInset, dotInset), Ui.Px(compact ? 4f : 5f), dotColor, 16);

        // Bottom gradient, panel fading up to transparent, so text reads on any photo.
        var gradHeight = compact ? Ui.Px(40f) : Ui.Px(64f);
        var gradTop = pos + new Vector2(0f, size.Y - gradHeight);
        var clear = Palette.WithAlpha(Palette.Bg, 0f).U32();
        var solid = Faded(Palette.WithAlpha(Palette.Bg, 0.95f));
        dl.AddRectFilledMultiColor(gradTop, pos + size, clear, clear, solid, solid);

        var innerPad = Ui.Px(compact ? 6f : 10f);
        var nameFont = compact ? this.fonts.Caption : this.fonts.Body;
        var name = this.Fit(FirstName(profile.DisplayName), size.X - (innerPad * 2f), nameFont);

        if (compact)
        {
            var nameSize = Ui.Measure(nameFont, name);
            Ui.TextAt(dl, nameFont, new Vector2(pos.X + innerPad, (pos.Y + size.Y - innerPad) - nameSize.Y), Faded(Palette.TextPrimary), name);
            return clicked;
        }

        // Members reached through data center travel carry their data center too, so a Gilgamesh on
        // Aether reads as such next to a home-DC neighbour.
        var away = profile.Proximity == Proximity.SameRegion && !string.IsNullOrEmpty(profile.Dc);
        var world = this.Fit(away ? $"{profile.Dc} · {profile.World}" : profile.World, size.X - (innerPad * 2f), this.fonts.Eyebrow);
        var worldSize = Ui.Measure(this.fonts.Eyebrow, world);
        var nameSizeStd = Ui.Measure(nameFont, name);
        var baseY = pos.Y + size.Y - innerPad;
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(pos.X + innerPad, baseY - worldSize.Y), Faded(Palette.TextSecondary), world);
        Ui.TextAt(dl, nameFont, new Vector2(pos.X + innerPad, (baseY - worldSize.Y - nameSizeStd.Y) - Ui.Px(1f)), Faded(Palette.TextPrimary), name);
        return clicked;
    }

    private static string FirstName(string displayName)
    {
        var space = displayName.IndexOf(' ');
        return space > 0 ? displayName[..space] : displayName;
    }

    private void DrawStatus(float width, string text)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(40f)));
        Ui.CenteredText(width, this.fonts.Caption, Palette.TextMuted, text);
    }

    private void DrawEmpty(float width, bool interactive)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(36f)));
        var buttonWidth = Ui.Px(180f);
        var wideWidth = Ui.Px(240f);

        // Filtered to favorites and empty: the tier prompts below would send the member widening a pool
        // that is not the reason the grid is empty.
        if (this.favoritesOnly)
        {
            this.kit.EmptyState(FontAwesomeIcon.Star.ToIconString(), "No favorites yet", "People you star appear here for quick access.", width);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((width - buttonWidth) * 0.5f));
            if (this.kit.PrimaryButton("##empty_favs", "Browse everyone", buttonWidth) && interactive)
                this.favoritesOnly = false;
            return;
        }

        if (this.discovery.Tier == Tier.World)
        {
            this.kit.EmptyState(FontAwesomeIcon.Compass.ToIconString(), "Quiet on your world", "No one nearby right now. Try the wider Data Center pool.", width);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((width - buttonWidth) * 0.5f));
            if (this.kit.PrimaryButton("##empty_dc", "Switch to DC", buttonWidth) && interactive)
                this.discovery.SetTier(Tier.Dc);
        }
        else if (this.discovery.Tier == Tier.Dc)
        {
            this.kit.EmptyState(FontAwesomeIcon.Compass.ToIconString(), "Quiet on your data center", "Widen to your whole region, or travel to another data center.", width);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((width - buttonWidth) * 0.5f));
            if (this.kit.PrimaryButton("##empty_region", "Switch to Region", buttonWidth) && interactive)
                this.discovery.SetTier(Tier.Region);
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((width - wideWidth) * 0.5f));
            if (this.kit.SecondaryButton("##empty_travel", "Travel to another data center", wideWidth) && interactive)
                this.OpenTravel();
        }
        else
        {
            this.kit.EmptyState(FontAwesomeIcon.SlidersH.ToIconString(), "No one matches", "Loosen your filters to see more people.", width);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((width - buttonWidth) * 0.5f));
            if (this.kit.SecondaryButton("##empty_reset", "Reset filters", buttonWidth) && interactive)
                this.discovery.Reset();
        }
    }

    // The aether crossing, drawn last inside the grid child so it paints over the tiles: a plate that
    // fades in over the old roster, gold streaks converging on the mark's diamond, a landing flash,
    // square ripples that keep emitting while the fetch is in flight, and the caption stack. Arriving
    // fades all of it out while the tiles stagger in (DrawTile's alpha). Timings match the approved
    // prototype; all of it is draw-list primitives.
    private void DrawCrossing(double now)
    {
        if (this.transition.Current == TravelTransition.Phase.Idle)
            return;

        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        var c = this.transition.CrossElapsed(now);
        var a = this.transition.ArriveT(now);
        var fade = this.transition.Current == TravelTransition.Phase.Arriving ? 1f - Motion.EaseInOutCubic(Motion.Segment(a, 0f, 0.6f)) : 1f;
        var signal = Palette.Signal;
        uint Gold(float alpha) => Palette.WithAlpha(signal, alpha * fade).U32();

        var scrim = Motion.EaseOutCubic(Motion.Segment(c, 0f, 0.3f)) * fade;
        dl.AddRectFilled(min, max, Palette.WithAlpha(Palette.Bg, scrim).U32());

        var center = new Vector2((min.X + max.X) * 0.5f, ((min.Y + max.Y) * 0.5f) - Ui.Px(28f));
        var half = Ui.Px(22f);

        // Streaks: hairlines sliding in along fixed rays, staggered, fading in then out as they land.
        const int streaks = 10;
        var farRadius = Ui.Px(150f);
        var landRadius = half * 1.1f;
        var streakLength = Ui.Px(22f);
        for (var i = 0; i < streaks; i++)
        {
            var start = 0.15f + (i * 0.03f);
            if (c < start || c > start + 0.5f)
                continue;
            var p = Motion.EaseInOutCubic(Motion.Segment(c, start, start + 0.45f));
            var r = farRadius - ((farRadius - landRadius) * p);
            var alpha = 0.9f * (p < 0.3f ? p / 0.3f : p > 0.8f ? (1f - p) / 0.2f : 1f);
            var theta = (i * MathF.PI * 2f / streaks) + 0.35f;
            var dir = new Vector2(MathF.Cos(theta), MathF.Sin(theta));
            dl.AddLine(center + (dir * r), center + (dir * (r + streakLength)), Gold(alpha), Ui.Px(1f));
        }

        // The diamond scales up as the streaks arrive; a filled flash marks the landing; the gem pulses.
        var ds = Motion.Segment(c, 0.1f, 0.5f);
        if (ds > 0f)
            Ui.Diamond(dl, center, half * (0.6f + (0.4f * Motion.EaseOutCubic(ds))), Gold(Motion.EaseOutCubic(ds)), Ui.Px(1.5f));
        var flash = 1f - Motion.Segment(c, 0.6f, 0.8f);
        if (c >= 0.6f && flash > 0f)
            Ui.DiamondFilled(dl, center, half, Gold(0.35f * flash));
        if (c >= 0.4f)
        {
            var pulse = 0.5f + (0.5f * MathF.Sin(MathF.PI * 2f * (c - 0.4f) / 0.9f));
            dl.AddCircleFilled(center, Ui.Px(2.5f), Gold(pulse), 12);
        }

        // Ripples: two square outlines expanding out of the diamond, looping while the crossing holds.
        for (var k = 0; k < 2; k++)
        {
            var since = c - 0.6f - (k * 0.28f);
            if (since < 0f)
                continue;
            var r = (since % 0.9f) / 0.9f;
            Ui.Diamond(dl, center, half * (0.5f + (3f * Motion.EaseOutCubic(r))), Gold(0.7f * (1f - r)), Ui.Px(1f));
        }

        // Caption stack: eyebrow, two-tone serif line, tracked gold destinations.
        var y0 = center.Y + Ui.Px(40f);
        var eyebrowA = Motion.EaseOutCubic(Motion.Segment(c, 0.3f, 0.55f)) * fade;
        Ui.TrackedText(dl, this.fonts.Eyebrow, center.X, y0 + Ui.Px(8f), Palette.WithAlpha(Palette.TextSecondary, eyebrowA).U32(), "DATA CENTER TRAVEL", Ui.Px(3f));

        var serifA = Motion.EaseOutCubic(Motion.Segment(c, 0.4f, 0.7f)) * fade;
        const string lead = "Crossing the ";
        const string tail = "aether";
        var leadW = Ui.Measure(this.fonts.EventTitle, lead).X;
        var tailW = Ui.Measure(this.fonts.EventTitleItalic, tail).X;
        var serifX = center.X - ((leadW + tailW) * 0.5f);
        var serifY = y0 + Ui.Px(30f);
        Ui.TextAt(dl, this.fonts.EventTitle, new Vector2(serifX, serifY), Palette.WithAlpha(Palette.TextPrimary, serifA).U32(), lead);
        Ui.TextAt(dl, this.fonts.EventTitleItalic, new Vector2(serifX + leadW, serifY), Palette.WithAlpha(Palette.TextSecondary, serifA).U32(), tail);

        var listA = Motion.EaseOutCubic(Motion.Segment(c, 0.5f, 0.8f));
        var tracking = Ui.Px(4.6f - (2.6f * Motion.EaseOutCubic(Motion.Segment(c, 0.5f, 1.3f))));
        var caption = this.Fit(this.transition.Caption, (max.X - min.X) - Ui.Px(40f), this.fonts.Eyebrow);
        Ui.TrackedText(dl, this.fonts.Eyebrow, center.X, serifY + Ui.Measure(this.fonts.EventTitle, lead).Y + Ui.Px(14f), Gold(listA), caption, tracking);

        // While the plate holds, the whole body is one click target: a click skips ahead.
        if (this.transition.BlocksInput)
        {
            ImGui.SetCursorScreenPos(min);
            if (ImGui.InvisibleButton("##travel_overlay", max - min))
                this.transition.Skip();
        }
    }

    private string Fit(string text, float maxWidth, IFontHandle font)
    {
        if (Ui.Measure(font, text).X <= maxWidth)
            return text;
        var s = text;
        while (s.Length > 1 && Ui.Measure(font, s + "…").X > maxWidth)
            s = s[..^1];
        return s + "…";
    }
}
