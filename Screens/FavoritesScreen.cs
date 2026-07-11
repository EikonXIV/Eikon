using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Favorites (SCREENS section 20). A grid of starred profiles backed by /api/favorites, reached from
// the grid. Tapping a tile opens the profile. Falls back to an empty state.
internal sealed class FavoritesScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly FavoritesService favorites;
    private readonly Selection selection;
    private readonly PhotoService photoSvc;

    public FavoritesScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, FavoritesService favorites, Selection selection, PhotoService photoSvc)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.favorites = favorites;
        this.selection = selection;
        this.photoSvc = photoSvc;
    }

    public Screen Id => Screen.Favorites;

    public bool Chrome => true;

    public void Draw()
    {
        var contentWidth = ImGui.GetContentRegionAvail().X - Ui.Px(16f);
        this.favorites.EnsureLoaded();
        var profiles = this.favorites.Profiles;

        this.kit.SectionLabel("Favorites");
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));

        if (profiles.Count == 0)
        {
            if (!this.favorites.Loaded)
            {
                ImGui.Dummy(new Vector2(0f, Ui.Px(40f)));
                Ui.CenteredText(contentWidth, this.fonts.Caption, Palette.TextMuted, "Loading...");
                return;
            }

            ImGui.Dummy(new Vector2(0f, Ui.Px(48f)));
            this.kit.EmptyState(FontAwesomeIcon.Star.ToIconString(), "No favorites yet", "People you star appear here for quick access.", contentWidth);
            var buttonWidth = Ui.Px(180f);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((contentWidth - buttonWidth) * 0.5f));
            if (this.kit.PrimaryButton("##fav_browse", "Browse the grid", buttonWidth))
                this.router.Navigate(Screen.Grid);
            return;
        }

        var gap = Ui.Px(8f);
        var tileWidth = (contentWidth - gap) / 2f;
        var size = new Vector2(tileWidth, tileWidth * 1.6f);
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap)))
        {
            for (var i = 0; i < profiles.Count; i++)
            {
                if (i % 2 != 0)
                    ImGui.SameLine(0f, gap);
                if (this.DrawTile(profiles[i], size))
                {
                    this.selection.ProfileUserId = profiles[i].UserId;
                    this.selection.ProfileDisplayName = profiles[i].DisplayName;
                    this.selection.ProfileReturn = Screen.Favorites;
                    this.router.Navigate(Screen.ProfileDetail);
                }
            }
        }
    }

    private bool DrawTile(BasicProfileDto profile, Vector2 size)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##fav_" + profile.UserId, size);
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
            drawList.AddCircleFilled(pos + new Vector2(Ui.Px(12f), Ui.Px(12f)), Ui.Px(5f), this.theme.Secondary.Base.U32(), 16);

        var star = FontAwesomeIcon.Star.ToIconString();
        var starSize = Ui.Measure(this.fonts.Icon, star);
        Ui.TextAt(drawList, this.fonts.Icon, pos + new Vector2(size.X - starSize.X - Ui.Px(8f), Ui.Px(8f)), this.theme.Secondary.Base.U32(), star);

        var scrimHeight = Ui.Px(42f);
        var scrimTop = pos + new Vector2(0f, size.Y - scrimHeight);
        drawList.AddRectFilled(scrimTop, pos + size, Palette.Scrim.U32(), rounding, ImDrawFlags.RoundCornersBottom);

        var nameSize = Ui.Measure(this.fonts.Caption, profile.DisplayName);
        Ui.TextAt(drawList, this.fonts.Caption, scrimTop + new Vector2(Ui.Px(8f), Ui.Px(5f)), Palette.White.U32(), profile.DisplayName);
        Ui.TextAt(drawList, this.fonts.Caption, scrimTop + new Vector2(Ui.Px(8f), Ui.Px(5f) + nameSize.Y), Palette.TextSecondary.U32(), profile.World);

        return clicked;
    }
}
