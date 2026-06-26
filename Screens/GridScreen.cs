using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Discovery grid. Proximity toggle and filter chips above a two column grid of portrait tiles backed
// by /api/discover. Photo thumbnails load in the media workstream; tiles show an initial for now.
internal sealed class GridScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly DiscoveryService discovery;
    private readonly Selection selection;
    private readonly PhotoService photoSvc;

    public GridScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, DiscoveryService discovery, Selection selection, PhotoService photoSvc)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.discovery = discovery;
        this.selection = selection;
        this.photoSvc = photoSvc;
    }

    public Screen Id => Screen.Grid;

    public bool Chrome => true;

    public void Draw()
    {
        var contentWidth = ImGui.GetContentRegionAvail().X - Ui.Px(16f);
        this.discovery.EnsureInitial();

        var tierIdx = this.kit.Segmented("##grid_tier", new[] { "World", "DC", "Region" }, this.discovery.TierIndex, contentWidth);
        if (DiscoveryService.TierOrder[tierIdx] != this.discovery.Tier)
            this.discovery.SetTier(DiscoveryService.TierOrder[tierIdx]);

        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        if (this.kit.Chip("##grid_online", "Online now", this.discovery.OnlineOnly))
            this.discovery.SetOnline(!this.discovery.OnlineOnly);
        ImGui.SameLine(0f, Ui.Px(6f));
        if (this.kit.Chip("##grid_tags", "Tags", false))
            this.router.Navigate(Screen.Filter);
        ImGui.SameLine(0f, Ui.Px(6f));
        if (this.kit.Chip("##grid_favs", "Favorites", false))
            this.router.Navigate(Screen.Favorites);

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));

        if (this.discovery.Loading && this.discovery.Profiles.Count == 0)
        {
            this.DrawStatus(contentWidth, "Finding people...");
            return;
        }

        var shown = this.DrawGrid(contentWidth);
        if (shown == 0)
            this.DrawEmpty(contentWidth);
    }

    private int DrawGrid(float contentWidth)
    {
        var gap = Ui.Px(8f);
        var tileWidth = (contentWidth - gap) / 2f;
        var size = new Vector2(tileWidth, tileWidth * 1.6f);

        var shown = 0;
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap)))
        {
            foreach (var profile in this.discovery.Profiles)
            {
                if (shown % 2 != 0)
                    ImGui.SameLine(0f, gap);

                if (this.DrawTile(profile, size))
                {
                    this.selection.ProfileUserId = profile.UserId;
                    this.selection.ProfileDisplayName = profile.DisplayName;
                    this.router.Navigate(Screen.ProfileDetail);
                }

                shown++;
            }
        }

        return shown;
    }

    private void DrawStatus(float contentWidth, string text)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(40f)));
        Ui.CenteredText(contentWidth, this.fonts.Caption, Palette.TextMuted, text);
    }

    private void DrawEmpty(float contentWidth)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(36f)));
        var buttonWidth = Ui.Px(180f);
        if (this.discovery.Tier == Tier.World)
        {
            this.kit.EmptyState(FontAwesomeIcon.Compass.ToIconString(), "Quiet on your world", "No one nearby right now. Try the wider Data Center pool.", contentWidth);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((contentWidth - buttonWidth) * 0.5f));
            if (this.kit.PrimaryButton("##empty_dc", "Switch to DC", buttonWidth))
                this.discovery.SetTier(Tier.Dc);
        }
        else if (this.discovery.Tier == Tier.Dc)
        {
            this.kit.EmptyState(FontAwesomeIcon.Compass.ToIconString(), "Quiet on your data center", "Widen to your whole region to reach other data centers.", contentWidth);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((contentWidth - buttonWidth) * 0.5f));
            if (this.kit.PrimaryButton("##empty_region", "Switch to Region", buttonWidth))
                this.discovery.SetTier(Tier.Region);
        }
        else
        {
            this.kit.EmptyState(FontAwesomeIcon.SlidersH.ToIconString(), "No one matches", "Loosen your filters to see more people.", contentWidth);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((contentWidth - buttonWidth) * 0.5f));
            if (this.kit.SecondaryButton("##empty_reset", "Reset filters", buttonWidth))
                this.discovery.Reset();
        }
    }

    private bool DrawTile(DiscoverResultProfile profile, Vector2 size)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##tile_{profile.UserId}", size);
        var drawList = ImGui.GetWindowDrawList();
        var rounding = Ui.Px(12f);

        drawList.AddRectFilled(pos, pos + size, Palette.Surface2.U32(), rounding);

        var texture = profile.MainPhotoId is { } photoId ? this.photoSvc.Texture(photoId) : null;
        if (texture != null)
        {
            var (uvMin, uvMax) = Ui.CoverUv(texture.Width, texture.Height, size.X / size.Y);
            drawList.AddImageRounded(texture.Handle, pos, pos + size, uvMin, uvMax, 0xFFFFFFFFu, rounding);
        }
        else
        {
            var initial = profile.DisplayName.Length > 0 ? profile.DisplayName[..1].ToUpperInvariant() : "?";
            var initialSize = Ui.Measure(this.fonts.Title, initial);
            Ui.TextAt(drawList, this.fonts.Title,
                pos + new Vector2((size.X - initialSize.X) * 0.5f, (size.Y * 0.42f) - (initialSize.Y * 0.5f)),
                Palette.TextMuted.U32(), initial);
        }

        if (profile.Online)
            drawList.AddCircleFilled(pos + new Vector2(Ui.Px(12f), Ui.Px(12f)), Ui.Px(5f), this.theme.Accent.U32(), 16);

        var badge = Badge(profile);
        if (badge is not null)
            this.DrawBadge(drawList, pos, size, badge);

        var scrimHeight = Ui.Px(42f);
        var scrimTop = pos + new Vector2(0f, size.Y - scrimHeight);
        drawList.AddRectFilled(scrimTop, pos + size, Palette.Scrim.U32(), rounding, ImDrawFlags.RoundCornersBottom);

        var nameSize = Ui.Measure(this.fonts.Caption, profile.DisplayName);
        Ui.TextAt(drawList, this.fonts.Caption, scrimTop + new Vector2(Ui.Px(8f), Ui.Px(5f)), Palette.White.U32(), profile.DisplayName);
        Ui.TextAt(drawList, this.fonts.Caption,
            scrimTop + new Vector2(Ui.Px(8f), Ui.Px(5f) + nameSize.Y),
            Palette.WithAlpha(Palette.White, 0.75f).U32(), profile.World);

        return clicked;
    }

    private static string? Badge(DiscoverResultProfile profile)
    {
        if (profile.LookingFor is null)
            return null;
        if (profile.LookingFor.Contains(LookingForElement.RightNow))
            return "now";
        if (profile.LookingFor.Contains(LookingForElement.Rp))
            return "RP";
        return null;
    }

    private void DrawBadge(ImDrawListPtr drawList, Vector2 tilePos, Vector2 tileSize, string label)
    {
        var textSize = Ui.Measure(this.fonts.Caption, label);
        var badgeSize = new Vector2(textSize.X + Ui.Px(12f), textSize.Y + Ui.Px(4f));
        var pos = tilePos + new Vector2(tileSize.X - badgeSize.X - Ui.Px(6f), Ui.Px(6f));
        var rounding = Ui.Px(6f);

        if (label == "now")
        {
            drawList.AddRectFilled(pos, pos + badgeSize, this.theme.AccentTint.U32(), rounding);
            Ui.TextAt(drawList, this.fonts.Caption, pos + new Vector2(Ui.Px(6f), Ui.Px(2f)), this.theme.AccentText.U32(), label);
        }
        else
        {
            drawList.AddRect(pos, pos + badgeSize, Palette.WithAlpha(Palette.White, 0.35f).U32(), rounding, ImDrawFlags.None, 1f);
            Ui.TextAt(drawList, this.fonts.Caption, pos + new Vector2(Ui.Px(6f), Ui.Px(2f)), Palette.White.U32(), label);
        }
    }
}
