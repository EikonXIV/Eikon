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
// underline scope tabs with density + filter tools, a pill filter row, then a scrolling grid of square
// portrait tiles backed by /api/discover. Job/age tags await a server field; tiles show name + world.
internal sealed class GridScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly DiscoveryService discovery;
    private readonly Selection selection;
    private readonly PhotoService photoSvc;
    private readonly Configuration config;

    public GridScreen(ScreenRouter router, Kit kit, UiFonts fonts, DiscoveryService discovery, Selection selection, PhotoService photoSvc, Configuration config)
    {
        this.router = router;
        this.kit = kit;
        this.fonts = fonts;
        this.discovery = discovery;
        this.selection = selection;
        this.photoSvc = photoSvc;
        this.config = config;
    }

    public Screen Id => Screen.Grid;

    public bool Chrome => true;

    public void Draw()
    {
        this.discovery.EnsureInitial();

        var avail = ImGui.GetContentRegionAvail();
        var width = avail.X;
        var pad = Ui.Px(20f);

        this.DrawHeader(width, pad);
        this.DrawChips(width, pad);

        var bodyAvail = ImGui.GetContentRegionAvail();
        using var scroll = ImRaii.Child("grid_scroll", bodyAvail, false, ImGuiWindowFlags.NoScrollbar);
        if (!scroll.Success)
            return;

        if (this.discovery.Loading && this.discovery.Profiles.Count == 0)
        {
            this.DrawStatus(bodyAvail.X, "Finding people…");
            return;
        }

        var compact = this.config.GridLayout == 1;
        var shown = this.DrawGrid(bodyAvail.X, compact);
        if (shown == 0)
        {
            this.DrawEmpty(bodyAvail.X);
            return;
        }

        // Infinite scroll: pull the next page as the viewer nears the bottom of the grid scroll region.
        if (this.discovery.HasMore)
        {
            if (this.discovery.Loading)
                this.DrawStatus(bodyAvail.X, "Loading more…");
            else if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - Ui.Px(240f))
                this.discovery.LoadMore();
        }
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

        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(left, y), Palette.TextSecondary.U32(), "DISCOVER");
        const string scope = "IN SCOPE";
        var scopeW = Ui.Measure(this.fonts.Eyebrow, scope).X;
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(right - scopeW, y), Palette.TextSecondary.U32(), scope);
        y += eyebrowH + Ui.Px(4f);

        const string nearby = "Nearby ";
        var nearbyW = Ui.Measure(this.fonts.SerifTitle, nearby).X;
        Ui.TextAt(dl, this.fonts.SerifTitle, new Vector2(left, y), Palette.TextPrimary.U32(), nearby);
        Ui.TextAt(dl, this.fonts.SerifItalicTitle, new Vector2(left + nearbyW, y), Palette.TextSecondary.U32(), "adventurers");
        var count = this.discovery.Profiles.Count.ToString("N0");
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
        if (this.PillChip(dl, "##chip_tags", "Tags", false, false, ref x, y, chipH))
            this.router.Navigate(Screen.Filter);
        if (this.PillChip(dl, "##chip_favs", "Favorites", false, false, ref x, y, chipH))
            this.router.Navigate(Screen.Favorites);

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

    private int DrawGrid(float childWidth, bool compact)
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
            foreach (var profile in this.discovery.Profiles)
            {
                if (shown % columns != 0)
                    ImGui.SameLine(0f, gap);

                if (this.DrawTile(profile, size, compact))
                {
                    this.selection.ProfileUserId = profile.UserId;
                    this.selection.ProfileDisplayName = profile.DisplayName;
                    this.router.Navigate(Screen.ProfileDetail);
                }

                shown++;
            }
        }

        ImGui.Unindent(pad);
        ImGui.Dummy(new Vector2(0f, pad));
        return shown;
    }

    private bool DrawTile(BasicProfileDto profile, Vector2 size, bool compact)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##tile_{profile.UserId}", size);
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();

        // Square surface (editorial radius is 0).
        dl.AddRectFilled(pos, pos + size, Palette.Surface2.U32());

        var texture = profile.MainPhotoId is { } photoId ? this.photoSvc.Texture(photoId) : null;
        if (texture != null)
        {
            var (uvMin, uvMax) = Ui.CoverUv(texture.Width, texture.Height, size.X / size.Y);
            dl.AddImage(texture.Handle, pos, pos + size, uvMin, uvMax, (hovered ? Palette.WithAlpha(Palette.White, 0.9f) : Palette.White).U32());
        }
        else
        {
            var initial = profile.DisplayName.Length > 0 ? profile.DisplayName[..1].ToUpperInvariant() : "?";
            var initialSize = Ui.Measure(this.fonts.SerifTitle, initial);
            Ui.TextAt(dl, this.fonts.SerifTitle,
                pos + new Vector2((size.X - initialSize.X) * 0.5f, (size.Y * 0.4f) - (initialSize.Y * 0.5f)),
                Palette.TextMuted.U32(), initial);
        }

        // Presence dot, top-right: green online, grey otherwise.
        var dotInset = Ui.Px(compact ? 9f : 11f);
        var dotColor = (profile.Online ? Palette.Online : Palette.Afk).U32();
        dl.AddCircleFilled(pos + new Vector2(size.X - dotInset, dotInset), Ui.Px(compact ? 4f : 5f), dotColor, 16);

        // Bottom gradient, panel fading up to transparent, so text reads on any photo.
        var gradHeight = compact ? Ui.Px(40f) : Ui.Px(64f);
        var gradTop = pos + new Vector2(0f, size.Y - gradHeight);
        var clear = Palette.WithAlpha(Palette.Bg, 0f).U32();
        var solid = Palette.WithAlpha(Palette.Bg, 0.95f).U32();
        dl.AddRectFilledMultiColor(gradTop, pos + size, clear, clear, solid, solid);

        var innerPad = Ui.Px(compact ? 6f : 10f);
        var nameFont = compact ? this.fonts.Caption : this.fonts.Body;
        var name = this.Fit(FirstName(profile.DisplayName), size.X - (innerPad * 2f), nameFont);

        if (compact)
        {
            var nameSize = Ui.Measure(nameFont, name);
            Ui.TextAt(dl, nameFont, new Vector2(pos.X + innerPad, (pos.Y + size.Y - innerPad) - nameSize.Y), Palette.TextPrimary.U32(), name);
            return clicked;
        }

        var world = profile.World;
        var worldSize = Ui.Measure(this.fonts.Eyebrow, world);
        var nameSizeStd = Ui.Measure(nameFont, name);
        var baseY = pos.Y + size.Y - innerPad;
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(pos.X + innerPad, baseY - worldSize.Y), Palette.TextSecondary.U32(), world);
        Ui.TextAt(dl, nameFont, new Vector2(pos.X + innerPad, (baseY - worldSize.Y - nameSizeStd.Y) - Ui.Px(1f)), Palette.TextPrimary.U32(), name);
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

    private void DrawEmpty(float width)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(36f)));
        var buttonWidth = Ui.Px(180f);
        if (this.discovery.Tier == Tier.World)
        {
            this.kit.EmptyState(FontAwesomeIcon.Compass.ToIconString(), "Quiet on your world", "No one nearby right now. Try the wider Data Center pool.", width);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((width - buttonWidth) * 0.5f));
            if (this.kit.PrimaryButton("##empty_dc", "Switch to DC", buttonWidth))
                this.discovery.SetTier(Tier.Dc);
        }
        else if (this.discovery.Tier == Tier.Dc)
        {
            this.kit.EmptyState(FontAwesomeIcon.Compass.ToIconString(), "Quiet on your data center", "Widen to your whole region to reach other data centers.", width);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((width - buttonWidth) * 0.5f));
            if (this.kit.PrimaryButton("##empty_region", "Switch to Region", buttonWidth))
                this.discovery.SetTier(Tier.Region);
        }
        else
        {
            this.kit.EmptyState(FontAwesomeIcon.SlidersH.ToIconString(), "No one matches", "Loosen your filters to see more people.", width);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((width - buttonWidth) * 0.5f));
            if (this.kit.SecondaryButton("##empty_reset", "Reset filters", buttonWidth))
                this.discovery.Reset();
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
