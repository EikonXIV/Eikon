using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Profile detail. Full bleed: a hero, the SFW vitals, the gated after-dark block, and a pinned
// action bar. Backed by the selected member's /api/profile detail. Photos load in the media
// workstream; the hero shows an initial for now.
internal sealed class ProfileDetailScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly ModerationFlow moderation;
    private readonly Lightbox lightbox;
    private readonly ProfileDetailService details;
    private readonly Selection selection;
    private readonly PhotoService photoSvc;
    private readonly SafetyService safety;
    private readonly SessionStore session;

    private ProfileDetailDto current = null!;
    private bool isSelf;   // viewing our own profile (Preview "see as others"): hide message/block, back to My profile
    private Guid favFor;   // peer the local favorite flag belongs to
    private bool favorited; // optimistic star state for the current peer
    private int heroIndex;  // which photo the hero shows / pages through, reset per profile

    private readonly AlbumService albums;

    public ProfileDetailScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, ModerationFlow moderation, Lightbox lightbox, ProfileDetailService details, Selection selection, PhotoService photoSvc, SafetyService safety, SessionStore session, AlbumService albums)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.moderation = moderation;
        this.lightbox = lightbox;
        this.details = details;
        this.selection = selection;
        this.photoSvc = photoSvc;
        this.safety = safety;
        this.session = session;
        this.albums = albums;
    }

    public Screen Id => Screen.ProfileDetail;

    public bool Chrome => false;

    public void Draw()
    {
        var userId = this.selection.ProfileUserId;
        if (userId is null)
        {
            this.router.Navigate(Screen.Grid);
            return;
        }

        this.isSelf = this.session.UserId is { } me && me == userId.Value;
        this.details.Ensure(userId.Value);
        var loaded = this.details.Current;

        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(16f);
        var contentWidth = avail.X - (pad * 2f);
        var headerHeight = Ui.Px(52f);
        var actionHeight = Ui.Px(64f);

        // Header on the host window (not the child): a back/more bar whose empty middle is the drag
        // handle, since you can only move the app from host-window empty space and the hero below
        // captures every click.
        this.DrawHeader(avail.X, pad, loaded);

        if (loaded is null)
        {
            ImGui.SetCursorPos(new Vector2(0f, headerHeight + Ui.Px(120f)));
            Ui.CenteredText(avail.X, this.fonts.Caption, Palette.TextMuted, "Loading profile...");
            return;
        }

        this.current = loaded;
        if (this.favFor != this.current.UserId)
        {
            this.favFor = this.current.UserId;
            this.favorited = this.current.Favorited;   // seed from the server; toggles stay local after
            this.heroIndex = 0;                        // start a new profile's gallery on the main photo
        }
        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var content = ImRaii.Child("pd_content", new Vector2(avail.X, avail.Y - headerHeight - actionHeight)))
        {
            if (content.Success)
            {
                this.DrawHero(avail.X);
                ImGui.Indent(pad);
                using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
                    this.DrawInfo(contentWidth);
                ImGui.Unindent(pad);
            }
        }

        ImGui.SetCursorPos(new Vector2(pad, avail.Y - actionHeight + Ui.Px(12f)));
        this.DrawActions(contentWidth);

        this.moderation.Draw();
        this.lightbox.Draw();
    }

    // Top bar on the host window. Back (left) and overflow (right) are small hit targets; the empty
    // space between them is non-interactive, so it doubles as the window drag handle. The name comes
    // from the selection so it shows even while the full profile is still loading.
    private void DrawHeader(float fullWidth, float pad, ProfileDetailDto? loaded)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var midY = origin.Y + Ui.Px(26f);

        var backGlyph = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, backGlyph);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##pd_back", backSize))
            this.router.Navigate(this.isSelf ? Screen.MyProfile : Screen.Grid);
        Ui.TextAt(drawList, this.fonts.Icon, ImGui.GetItemRectMin(), Palette.TextSecondary.U32(), backGlyph);

        var name = this.selection.ProfileDisplayName ?? string.Empty;
        if (name.Length > 0)
        {
            var nameSize = Ui.Measure(this.fonts.Body, name);
            Ui.TextAt(drawList, this.fonts.Body, new Vector2(origin.X + pad + backSize.X + Ui.Px(12f), midY - (nameSize.Y * 0.5f)), Palette.TextPrimary.U32(), name);
        }

        if (loaded is not null && !this.isSelf)
        {
            var moreGlyph = FontAwesomeIcon.EllipsisH.ToIconString();
            var moreSize = Ui.Measure(this.fonts.Icon, moreGlyph);
            ImGui.SetCursorScreenPos(new Vector2(origin.X + fullWidth - pad - moreSize.X, midY - (moreSize.Y * 0.5f)));
            if (ImGui.InvisibleButton("##pd_more", moreSize))
                this.moderation.Open(loaded.UserId, loaded.DisplayName, ImGui.GetItemRectMax());
            Ui.TextAt(drawList, this.fonts.Icon, ImGui.GetItemRectMin(), Palette.TextSecondary.U32(), moreGlyph);
        }

        drawList.AddLine(new Vector2(origin.X, origin.Y + Ui.Px(51f)), new Vector2(origin.X + fullWidth, origin.Y + Ui.Px(51f)), Palette.Border.U32(), 1f);
    }

    private void DrawHero(float fullWidth)
    {
        var heroHeight = fullWidth * 1.2f;
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(pos, pos + new Vector2(fullWidth, heroHeight), Palette.Surface2.U32());

        var photos = this.current.PhotoIds;
        var count = photos.Count;
        this.heroIndex = count > 0 ? Math.Clamp(this.heroIndex, 0, count - 1) : 0;

        var photoId = count > 0 ? photos[this.heroIndex] : this.current.MainPhotoId;
        var texture = photoId is { } id ? this.photoSvc.Texture(id) : null;
        if (texture != null)
        {
            // Bias the crop toward the top so the face is not clipped by the cover fill.
            var (uvMin, uvMax) = Ui.CoverUv(texture.Width, texture.Height, fullWidth / heroHeight, offsetY: 0.2f);
            drawList.AddImageRounded(texture.Handle, pos, pos + new Vector2(fullWidth, heroHeight), uvMin, uvMax, 0xFFFFFFFFu, 0f);
        }
        else
        {
            var initial = this.current.DisplayName.Length > 0 ? this.current.DisplayName[..1].ToUpperInvariant() : "?";
            var initialSize = Ui.Measure(this.fonts.Title, initial);
            Ui.TextAt(drawList, this.fonts.Title,
                pos + new Vector2((fullWidth - initialSize.X) * 0.5f, (heroHeight * 0.4f) - (initialSize.Y * 0.5f)),
                Palette.TextMuted.U32(), initial);
        }

        if (this.current.Online)
            this.DrawOnlinePill(drawList, pos + new Vector2(Ui.Px(12f), Ui.Px(12f)));
        if (count > 1)
        {
            this.DrawPhotoDots(drawList, pos, fullWidth, heroHeight, count, this.heroIndex);
            var arrowY = pos.Y + (heroHeight * 0.5f);
            this.DrawPhotoArrow(drawList, new Vector2(pos.X + Ui.Px(24f), arrowY), FontAwesomeIcon.ChevronLeft);
            this.DrawPhotoArrow(drawList, new Vector2(pos.X + fullWidth - Ui.Px(24f), arrowY), FontAwesomeIcon.ChevronRight);
        }

        // With several photos the left and right thirds step through them in place and the centre opens
        // the full viewer; with one photo the whole hero opens it. The zones do not overlap, so a final
        // Dummy of the full size advances the layout cursor below the hero for the info section.
        if (count > 1)
        {
            var third = fullWidth / 3f;
            ImGui.SetCursorScreenPos(pos);
            if (ImGui.InvisibleButton("##pd_hero_prev", new Vector2(third, heroHeight)))
                this.heroIndex = (this.heroIndex - 1 + count) % count;
            ImGui.SetCursorScreenPos(pos + new Vector2(third, 0f));
            if (ImGui.InvisibleButton("##pd_hero_open", new Vector2(third, heroHeight)))
                this.lightbox.OpenPhotos(photos, this.heroIndex);
            ImGui.SetCursorScreenPos(pos + new Vector2(third * 2f, 0f));
            if (ImGui.InvisibleButton("##pd_hero_next", new Vector2(fullWidth - (third * 2f), heroHeight)))
                this.heroIndex = (this.heroIndex + 1) % count;
        }
        else
        {
            ImGui.SetCursorScreenPos(pos);
            if (ImGui.InvisibleButton("##pd_hero", new Vector2(fullWidth, heroHeight)) && count > 0)
                this.lightbox.OpenPhotos(photos, this.heroIndex);
        }

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(fullWidth, heroHeight));
    }

    private void DrawInfo(float contentWidth)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.DrawNameRow();

        var height = this.current.HeightCm is { } cm ? $"  ·  {cm} cm" : string.Empty;
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        this.Caption($"{ProfileMapper.Label(this.current.Gender)}  ·  {this.current.Age}  ·  {string.Join(" / ", ProfileMapper.Labels(this.current.Races))}{height}");
        var proximity = this.current.Proximity switch
        {
            Proximity.SameWorld => "Same world",
            Proximity.SameDc => "Same DC",
            _ => "Same region",
        };
        ImGui.Dummy(new Vector2(0f, Ui.Px(2f)));
        this.Caption($"{this.current.World}  ·  {this.current.Dc}  ·  {proximity}");

        if (this.current.Tribes.Count > 0)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
            this.kit.SectionLabel("Tribe");
            ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
            this.kit.ChipFlow("pd_tribe", ProfileMapper.Labels(this.current.Tribes), _ => false, contentWidth);
        }

        if (this.current.LookingFor.Count > 0)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
            this.kit.SectionLabel("Looking for");
            ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
            this.kit.ChipFlow("pd_lf", ProfileMapper.Labels(this.current.LookingFor), _ => true, contentWidth);
        }

        if (this.current.Interests.Count > 0)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
            this.kit.SectionLabel("Into");
            ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
            this.kit.ChipFlow("pd_into", this.current.Interests.ToArray(), _ => false, contentWidth);
        }

        if (!string.IsNullOrWhiteSpace(this.current.Bio))
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
            this.kit.SectionLabel("About");
            ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
                ImGui.TextWrapped(this.current.Bio);
        }

        if (this.current.AfterDark is { } ad)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
            this.DrawAfterDark(contentWidth, ad);
        }

        if (!this.isSelf)
            this.DrawAlbums(contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
    }

    // The peer's albums: public and already-unlocked ones open the viewer; locked ones offer a request
    // (or show it is pending). Empty and hidden albums never reach here (the server filters them).
    private void DrawAlbums(float contentWidth)
    {
        if (this.selection.ProfileUserId is not { } userId)
            return;
        var peerAlbums = this.albums.PeerAlbums(userId);
        if (peerAlbums.Count == 0)
            return;

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.kit.SectionLabel("Albums");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        foreach (var album in peerAlbums)
            this.DrawAlbumRow(userId, album, contentWidth);
    }

    private void DrawAlbumRow(Guid userId, PeerAlbumDto album, float contentWidth)
    {
        var rowH = Ui.Px(60f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##pd_album_" + album.Id, new Vector2(contentWidth, rowH));
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();
        if (hovered)
            drawList.AddRectFilled(pos, pos + new Vector2(contentWidth, rowH), Palette.WithAlpha(Palette.White, 0.04f).U32(), Ui.Px(10f));

        var viewable = album.Access is PeerAlbumAccessEnum.Public or PeerAlbumAccessEnum.Granted;
        var thumb = Ui.Px(48f);
        var tmin = new Vector2(pos.X, pos.Y + ((rowH - thumb) * 0.5f));
        var tmax = tmin + new Vector2(thumb, thumb);
        drawList.AddRectFilled(tmin, tmax, Palette.Surface2.U32(), Ui.Px(10f));
        var tex = viewable && album.CoverPhotoId is { } cover ? this.albums.Texture(album.Id, cover) : null;
        if (tex is { Width: > 0, Height: > 0 })
        {
            var (uvMin, uvMax) = Ui.CoverUv(tex.Width, tex.Height, 1f);
            drawList.AddImageRounded(tex.Handle, tmin, tmax, uvMin, uvMax, 0xFFFFFFFFu, Ui.Px(10f));
        }
        else
        {
            var glyph = (viewable ? FontAwesomeIcon.Image : FontAwesomeIcon.Lock).ToIconString();
            var gs = Ui.Measure(this.fonts.Icon, glyph);
            Ui.TextAt(drawList, this.fonts.Icon, ((tmin + tmax) * 0.5f) - (gs * 0.5f), Palette.TextMuted.U32(), glyph);
        }

        var textX = pos.X + thumb + Ui.Px(12f);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(textX, pos.Y + Ui.Px(13f)), Palette.TextPrimary.U32(), album.Name);
        var vis = album.Access == PeerAlbumAccessEnum.Public ? "public" : "private";
        var meta = album.PhotoCount == 1 ? $"1 photo · {vis}" : $"{album.PhotoCount} photos · {vis}";
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(textX, pos.Y + Ui.Px(33f)), Palette.TextMuted.U32(), meta);

        var midY = pos.Y + (rowH * 0.5f);
        if (viewable)
        {
            const string label = "View";
            var ls = Ui.Measure(this.fonts.Body, label);
            var chevron = FontAwesomeIcon.ChevronRight.ToIconString();
            var chs = Ui.Measure(this.fonts.Icon, chevron);
            var ax = pos.X + contentWidth - ls.X - Ui.Px(4f) - chs.X;
            Ui.TextAt(drawList, this.fonts.Body, new Vector2(ax, midY - (ls.Y * 0.5f)), this.theme.AccentText.U32(), label);
            Ui.TextAt(drawList, this.fonts.Icon, new Vector2(ax + ls.X + Ui.Px(4f), midY - (chs.Y * 0.5f)), this.theme.AccentText.U32(), chevron);
        }
        else if (album.Access == PeerAlbumAccessEnum.Requested)
        {
            const string label = "Requested";
            var ls = Ui.Measure(this.fonts.Caption, label);
            Ui.TextAt(drawList, this.fonts.Caption, new Vector2(pos.X + contentWidth - ls.X, midY - (ls.Y * 0.5f)), Palette.TextMuted.U32(), label);
        }
        else
        {
            const string label = "Request access";
            var ls = Ui.Measure(this.fonts.Caption, label);
            var pillW = ls.X + Ui.Px(20f);
            var pillH = Ui.Px(28f);
            var pillPos = new Vector2(pos.X + contentWidth - pillW, midY - (pillH * 0.5f));
            drawList.AddRect(pillPos, pillPos + new Vector2(pillW, pillH), Palette.WithAlpha(this.theme.Accent, 0.5f).U32(), Ui.Px(8f), ImDrawFlags.None, 1f);
            Ui.TextAt(drawList, this.fonts.Caption, new Vector2(pillPos.X + Ui.Px(10f), pillPos.Y + ((pillH - ls.Y) * 0.5f)), this.theme.AccentText.U32(), label);
        }

        if (clicked)
        {
            if (viewable)
            {
                this.selection.AlbumId = album.Id;
                this.selection.AlbumName = album.Name;
                this.router.Navigate(Screen.AlbumViewer);
            }
            else if (album.Access == PeerAlbumAccessEnum.Locked)
            {
                this.albums.RequestAccess(album.Id, userId);
            }
        }
    }

    private void Caption(string text)
    {
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
            ImGui.TextUnformatted(text);
    }

    private void DrawNameRow()
    {
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var name = this.current.DisplayName;
        var nameSize = Ui.Measure(this.fonts.Title, name);
        Ui.TextAt(drawList, this.fonts.Title, pos, Palette.TextPrimary.U32(), name);

        var nextX = pos.X + nameSize.X + Ui.Px(8f);
        if (this.current.Verified)
        {
            var verified = FontAwesomeIcon.CheckCircle.ToIconString();
            var verifiedSize = Ui.Measure(this.fonts.Icon, verified);
            Ui.TextAt(drawList, this.fonts.Icon, new Vector2(nextX, pos.Y + ((nameSize.Y - verifiedSize.Y) * 0.5f)), this.theme.Accent.U32(), verified);
            nextX += verifiedSize.X + Ui.Px(8f);
        }

        var pronoun = this.current.PronounCustom is { Length: > 0 } pc ? pc : ProfileMapper.Label(this.current.Pronoun);
        var pronounSize = Ui.Measure(this.fonts.Caption, pronoun);
        var pillPad = Ui.Px(8f);
        var pillSize = new Vector2(pronounSize.X + (pillPad * 2f), pronounSize.Y + Ui.Px(4f));
        var pillPos = new Vector2(nextX, pos.Y + ((nameSize.Y - pillSize.Y) * 0.5f));
        drawList.AddRect(pillPos, pillPos + pillSize, Palette.Border.U32(), Ui.Px(8f), ImDrawFlags.None, 1f);
        Ui.TextAt(drawList, this.fonts.Caption, pillPos + new Vector2(pillPad, Ui.Px(2f)), Palette.TextSecondary.U32(), pronoun);

        ImGui.Dummy(new Vector2(0f, nameSize.Y));
    }

    private void DrawAfterDark(float contentWidth, AfterDarkDto ad)
    {
        using (ImRaii.PushColor(ImGuiCol.ChildBg, Palette.WithAlpha(this.theme.Accent, 0.06f)))
        using (ImRaii.PushColor(ImGuiCol.Border, Palette.WithAlpha(this.theme.Accent, 0.28f)))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, Ui.Px(12f)))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(Ui.Px(12f), Ui.Px(12f))))
        using (var box = ImRaii.Child("pd_afterdark", new Vector2(contentWidth, Ui.Px(196f)), true))
        {
            if (!box.Success)
                return;

            using (this.fonts.Icon.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, this.theme.Accent))
                ImGui.TextUnformatted(FontAwesomeIcon.Moon.ToIconString());
            ImGui.SameLine(0f, Ui.Px(7f));
            using (this.fonts.Body.Push())
                ImGui.TextUnformatted("After dark");
            ImGui.SameLine(0f, Ui.Px(7f));
            this.TintPill("18+");

            ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
            this.Field("Position", ad.Position is { } p ? ProfileMapper.Label(p) : "-");
            ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
            this.Field("Role", ad.Role is { } r ? ProfileMapper.Label(r) : "-");
            ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
            this.Field("Size", ad.Size is { } s ? ProfileMapper.Label(s) : "-");
            ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
            this.Field("Meet", ad.Meet.Count > 0 ? string.Join(" · ", ad.Meet.Select(ProfileMapper.Label)) : "-");

            if (ad.Kinks.Count > 0)
            {
                ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
                this.kit.SectionLabel("Kinks");
                ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
                this.kit.ChipFlow("pd_kink", ad.Kinks.ToArray(), _ => true, contentWidth - Ui.Px(24f));
            }
        }
    }

    private void Field(string label, string value)
    {
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextUnformatted(label);
        ImGui.SameLine(Ui.Px(92f));
        using (this.fonts.Body.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
            ImGui.TextUnformatted(value);
    }

    private void TintPill(string text)
    {
        var textSize = Ui.Measure(this.fonts.Caption, text);
        var size = new Vector2(textSize.X + Ui.Px(12f), textSize.Y + Ui.Px(2f));
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(size);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, pos + size, this.theme.AccentTint.U32(), Ui.Px(6f));
        Ui.TextAt(drawList, this.fonts.Caption, pos + new Vector2(Ui.Px(6f), Ui.Px(1f)), this.theme.AccentText.U32(), text);
    }

    private void DrawActions(float contentWidth)
    {
        // Self-view (Preview): no message/favorite/report on your own profile, just a way back.
        if (this.isSelf)
        {
            if (this.kit.SecondaryButton("##pd_self_back", "Back to my profile", contentWidth))
                this.router.Navigate(Screen.MyProfile);
            return;
        }

        var square = Ui.Px(38f);
        var gap = Ui.Px(10f);
        var messageWidth = contentWidth - ((square + gap) * 2f);

        if (this.kit.PrimaryButton("##pd_message", "Message", messageWidth))
        {
            this.selection.ProfileUserId = this.current.UserId;
            this.selection.ProfileDisplayName = this.current.DisplayName;
            this.router.Navigate(Screen.Chat);
        }

        ImGui.SameLine(0f, gap);
        if (this.SquareIcon("##pd_fav", FontAwesomeIcon.Star, square, this.favorited))
        {
            this.favorited = !this.favorited;
            this.safety.Favorite(this.current.UserId, this.favorited);
        }
        ImGui.SameLine(0f, gap);
        if (this.SquareIcon("##pd_report", FontAwesomeIcon.Flag, square))
            this.moderation.OpenReport(this.current.UserId, this.current.DisplayName);
    }

    private bool SquareIcon(string id, FontAwesomeIcon icon, float size, bool active = false)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var drawList = ImGui.GetWindowDrawList();
        var rounding = Ui.Px(10f);
        if (active)
            drawList.AddRectFilled(pos, pos + new Vector2(size, size), this.theme.AccentTint.U32(), rounding);
        var borderColor = active ? this.theme.Accent : (ImGui.IsItemHovered() ? Palette.TextMuted : Palette.Border);
        drawList.AddRect(pos, pos + new Vector2(size, size), borderColor.U32(), rounding, ImDrawFlags.None, 1f);
        var glyph = icon.ToIconString();
        var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
        var glyphColor = active ? this.theme.Accent : Palette.TextSecondary;
        Ui.TextAt(drawList, this.fonts.Icon, pos + new Vector2((size - glyphSize.X) * 0.5f, (size - glyphSize.Y) * 0.5f), glyphColor.U32(), glyph);
        return clicked;
    }

    private void DrawOnlinePill(ImDrawListPtr drawList, Vector2 pos)
    {
        const string label = "Online now";
        var textSize = Ui.Measure(this.fonts.Caption, label);
        var dot = Ui.Px(8f);
        var padX = Ui.Px(9f);
        var padY = Ui.Px(4f);
        var gap = Ui.Px(6f);
        var size = new Vector2(padX + dot + gap + textSize.X + padX, textSize.Y + (padY * 2f));

        drawList.AddRectFilled(pos, pos + size, Palette.Scrim.U32(), Ui.Px(8f));
        drawList.AddCircleFilled(pos + new Vector2(padX + (dot * 0.5f), size.Y * 0.5f), dot * 0.5f, this.theme.Accent.U32(), 12);
        Ui.TextAt(drawList, this.fonts.Caption, pos + new Vector2(padX + dot + gap, padY), Palette.White.U32(), label);
    }

    // A paging affordance over the hero: a chevron on a dark scrim disc so it reads over any photo.
    // Purely visual; the wide invisible zones behind it (left/right thirds) take the actual taps.
    private void DrawPhotoArrow(ImDrawListPtr drawList, Vector2 center, FontAwesomeIcon icon)
    {
        drawList.AddCircleFilled(center, Ui.Px(15f), Palette.Scrim.U32(), 20);
        var glyph = icon.ToIconString();
        var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(drawList, this.fonts.Icon, center - (glyphSize * 0.5f), Palette.White.U32(), glyph);
    }

    private void DrawPhotoDots(ImDrawListPtr drawList, Vector2 heroPos, float fullWidth, float heroHeight, int count, int activeIndex)
    {
        var dotWidth = Ui.Px(18f);
        var dotHeight = Ui.Px(3f);
        var gap = Ui.Px(5f);
        var total = (count * dotWidth) + ((count - 1) * gap);
        var startX = heroPos.X + ((fullWidth - total) * 0.5f);
        var y = heroPos.Y + heroHeight - Ui.Px(14f);

        for (var i = 0; i < count; i++)
        {
            var x = startX + (i * (dotWidth + gap));
            var color = i == activeIndex ? this.theme.Accent : Palette.WithAlpha(Palette.White, 0.4f);
            drawList.AddRectFilled(new Vector2(x, y), new Vector2(x + dotWidth, y + dotHeight), color.U32(), dotHeight * 0.5f);
        }
    }
}
