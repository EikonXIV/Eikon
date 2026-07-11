using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Profile detail (warm-editorial). A back/title/overflow rail, a 4:5 hero with a gradient and the
// two-tone name overlaid, then hairline-separated sections: data table, about, interests, looking-for
// (signal tags), the gated after-dark block, and the peer's albums. A pinned Message + favorite bar.
internal sealed class ProfileDetailScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly UiFonts fonts;
    private readonly ModerationFlow moderation;
    private readonly Lightbox lightbox;
    private readonly ProfileDetailService details;
    private readonly Selection selection;
    private readonly PhotoService photoSvc;
    private readonly SafetyService safety;
    private readonly SessionStore session;
    private readonly AlbumService albums;

    private ProfileDetailDto current = null!;
    private bool isSelf;
    private Guid favFor;
    private bool favorited;
    private int heroIndex;

    public ProfileDetailScreen(ScreenRouter router, UiFonts fonts, ModerationFlow moderation, Lightbox lightbox, ProfileDetailService details, Selection selection, PhotoService photoSvc, SafetyService safety, SessionStore session, AlbumService albums)
    {
        this.router = router;
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
        var headerHeight = Ui.Px(44f);
        var actionHeight = Ui.Px(64f);

        this.DrawHeader(avail.X, loaded);

        if (loaded is null)
        {
            ImGui.SetCursorPos(new Vector2(0f, headerHeight + Ui.Px(120f)));
            Ui.CenteredText(avail.X, this.fonts.Caption, Palette.TextMuted, "Loading profile…");
            return;
        }

        this.current = loaded;
        if (this.favFor != this.current.UserId)
        {
            this.favFor = this.current.UserId;
            this.favorited = this.current.Favorited;
            this.heroIndex = 0;
            this.albums.InvalidatePeer(this.current.UserId);
        }

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var content = ImRaii.Child("pd_content", new Vector2(avail.X, avail.Y - headerHeight - actionHeight), false, ImGuiWindowFlags.NoScrollbar))
        {
            if (content.Success)
            {
                this.DrawHero(avail.X);
                this.DrawInfo(avail.X);
            }
        }

        ImGui.SetCursorPos(new Vector2(Ui.Px(12f), avail.Y - actionHeight + Ui.Px(12f)));
        this.DrawActions(avail.X - (Ui.Px(12f) * 2f));

        this.moderation.Draw();
        this.lightbox.Draw();
    }

    private void DrawHeader(float fullWidth, ProfileDetailDto? loaded)
    {
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var midY = origin.Y + Ui.Px(22f);
        var pad = Ui.Px(14f);

        var back = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, back);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##pd_back", backSize))
            this.router.Navigate(this.selection.ProfileReturn);
        Ui.TextAt(dl, this.fonts.Icon, ImGui.GetItemRectMin(), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32(), back);

        var name = this.selection.ProfileDisplayName ?? this.current?.DisplayName ?? string.Empty;
        var handle = Handle(name, this.current?.World);
        var title = $"PROFILE · {handle}";
        var titleSize = Ui.Measure(this.fonts.Eyebrow, title);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(origin.X + ((fullWidth - titleSize.X) * 0.5f), midY - (titleSize.Y * 0.5f)), Palette.TextSecondary.U32(), title);

        if (loaded is not null && !this.isSelf)
        {
            var more = FontAwesomeIcon.EllipsisH.ToIconString();
            var moreSize = Ui.Measure(this.fonts.Icon, more);
            ImGui.SetCursorScreenPos(new Vector2((origin.X + fullWidth - pad) - moreSize.X, midY - (moreSize.Y * 0.5f)));
            if (ImGui.InvisibleButton("##pd_more", moreSize))
                this.moderation.Open(loaded.UserId, loaded.DisplayName, ImGui.GetItemRectMax());
            Ui.TextAt(dl, this.fonts.Icon, ImGui.GetItemRectMin(), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32(), more);
        }

        dl.AddLine(new Vector2(origin.X, origin.Y + Ui.Px(43f)), new Vector2(origin.X + fullWidth, origin.Y + Ui.Px(43f)), Palette.Border.U32(), 1f);
    }

    private void DrawHero(float fullWidth)
    {
        var heroHeight = fullWidth * 1.25f;
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(pos, pos + new Vector2(fullWidth, heroHeight), Palette.Surface2.U32());

        var photos = this.current.PhotoIds;
        var count = photos.Count;
        this.heroIndex = count > 0 ? Math.Clamp(this.heroIndex, 0, count - 1) : 0;

        var photoId = count > 0 ? photos[this.heroIndex] : this.current.MainPhotoId;
        var texture = photoId is { } id ? this.photoSvc.Texture(id) : null;
        if (texture != null)
        {
            var (uvMin, uvMax) = Ui.CoverUv(texture.Width, texture.Height, fullWidth / heroHeight, offsetY: 0.2f);
            dl.AddImage(texture.Handle, pos, pos + new Vector2(fullWidth, heroHeight), uvMin, uvMax);
        }
        else
        {
            var initial = this.current.DisplayName.Length > 0 ? this.current.DisplayName[..1].ToUpperInvariant() : "?";
            var initialSize = Ui.Measure(this.fonts.SerifTitle, initial);
            Ui.TextAt(dl, this.fonts.SerifTitle, pos + new Vector2((fullWidth - initialSize.X) * 0.5f, (heroHeight * 0.35f) - (initialSize.Y * 0.5f)), Palette.TextMuted.U32(), initial);
        }

        if (count > 1)
        {
            this.DrawPhotoDots(dl, pos, fullWidth, heroHeight, count, this.heroIndex);
            var arrowY = pos.Y + (heroHeight * 0.5f);
            this.DrawPhotoArrow(dl, new Vector2(pos.X + Ui.Px(24f), arrowY), FontAwesomeIcon.ChevronLeft);
            this.DrawPhotoArrow(dl, new Vector2((pos.X + fullWidth) - Ui.Px(24f), arrowY), FontAwesomeIcon.ChevronRight);
        }

        // Gradient + overlaid status eyebrow and the two-tone name, anchored bottom-left.
        var gradTop = pos + new Vector2(0f, heroHeight * 0.55f);
        var clear = Palette.WithAlpha(Palette.Bg, 0f).U32();
        var solid = Palette.WithAlpha(Palette.Bg, 0.96f).U32();
        dl.AddRectFilledMultiColor(gradTop, pos + new Vector2(fullWidth, heroHeight), clear, clear, solid, solid);

        var ox = pos.X + Ui.Px(20f);
        var (first, rest) = SplitName(this.current.DisplayName);
        var firstH = Ui.Measure(this.fonts.SerifTitle, first).Y;
        var restH = rest.Length > 0 ? Ui.Measure(this.fonts.SerifItalicTitle, rest).Y : 0f;
        var restY = (pos.Y + heroHeight) - Ui.Px(20f) - restH;
        var firstY = restY - firstH + Ui.Px(4f);
        if (rest.Length > 0)
            Ui.TextAt(dl, this.fonts.SerifItalicTitle, new Vector2(ox, restY), Palette.Signal.U32(), rest);
        Ui.TextAt(dl, this.fonts.SerifTitle, new Vector2(ox, firstY), Palette.TextPrimary.U32(), first);

        var status = this.current.Online ? "ONLINE NOW" : string.Empty;
        if (status.Length > 0)
        {
            var eH = Ui.Measure(this.fonts.Eyebrow, status).Y;
            var ey = firstY - Ui.Px(8f) - eH;
            dl.AddCircleFilled(new Vector2(ox + Ui.Px(3f), ey + (eH * 0.5f)), Ui.Px(3f), Palette.Online.U32(), 12);
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(ox + Ui.Px(13f), ey), Palette.WithAlpha(Palette.TextPrimary, 0.85f).U32(), status);
        }

        // Paging taps: left/right thirds step photos, centre opens the viewer; one photo opens on tap.
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

    private void DrawInfo(float fullWidth)
    {
        this.DrawDataTable(fullWidth);

        var pad = Ui.Px(20f);
        var innerWidth = fullWidth - (pad * 2f);
        ImGui.Indent(pad);

        if (!string.IsNullOrWhiteSpace(this.current.Bio))
        {
            this.SectionTop();
            this.Eyebrow("About");
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.WithAlpha(Palette.TextPrimary, 0.9f)))
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + innerWidth);
                ImGui.TextUnformatted(this.current.Bio);
                ImGui.PopTextWrapPos();
            }
            this.SectionBottom(fullWidth);
        }

        if (this.current.Interests.Count > 0)
            this.TagSection(fullWidth, innerWidth, "Interests", this.current.Interests.ToArray(), signal: false);

        if (this.current.LookingFor.Count > 0)
            this.TagSection(fullWidth, innerWidth, "Looking for", ProfileMapper.Labels(this.current.LookingFor).ToArray(), signal: true);

        if (this.current.AfterDark is { } ad)
            this.DrawAfterDark(fullWidth, innerWidth, ad);

        if (!this.isSelf)
            this.DrawAlbums(fullWidth, innerWidth);

        ImGui.Unindent(pad);
        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
    }

    private void DrawDataTable(float fullWidth)
    {
        var proximity = this.current.Proximity switch
        {
            Proximity.SameWorld => "Same world",
            Proximity.SameDc => "Same DC",
            _ => "Same region",
        };
        var pronoun = this.current.PronounCustom is { Length: > 0 } pc ? pc : ProfileMapper.Label(this.current.Pronoun);
        var race = string.Join(" / ", ProfileMapper.Labels(this.current.Races));

        var cells = new (string Label, string Value, bool Mono)[]
        {
            ("Age", this.current.Age.ToString(), true),
            ("Gender", ProfileMapper.Label(this.current.Gender), false),
            ("Race", race.Length > 0 ? race : "—", false),
            ("Pronouns", pronoun, false),
            ("World", this.current.World, false),
            ("Data Center", this.current.Dc, false),
            ("Proximity", proximity, false),
        };

        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var cellW = fullWidth / 2f;
        var cellH = Ui.Px(54f);
        var pad = Ui.Px(20f);
        var rows = (cells.Length + 1) / 2;

        for (var i = 0; i < cells.Length; i++)
        {
            var col = i % 2;
            var row = i / 2;
            var lone = (i == cells.Length - 1) && (col == 0);   // an odd final cell spans the full width
            var cx = origin.X + (col * cellW);
            var cy = origin.Y + (row * cellH);
            var w = lone ? fullWidth : cellW;
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(cx + pad, cy + Ui.Px(12f)), Palette.TextSecondary.U32(), cells[i].Label.ToUpperInvariant());
            var valueFont = cells[i].Mono ? this.fonts.Eyebrow : this.fonts.Label;
            Ui.TextAt(dl, valueFont, new Vector2(cx + pad, cy + Ui.Px(28f)), Palette.TextPrimary.U32(), cells[i].Value);
            if (col == 0 && !lone)
                dl.AddLine(new Vector2(cx + cellW, cy), new Vector2(cx + cellW, cy + cellH), Palette.Border.U32(), 1f);
            dl.AddLine(new Vector2(cx, cy + cellH), new Vector2(cx + w, cy + cellH), Palette.Border.U32(), 1f);
        }

        ImGui.Dummy(new Vector2(fullWidth, rows * cellH));
    }

    private void TagSection(float fullWidth, float innerWidth, string label, IReadOnlyList<string> labels, bool signal)
    {
        this.SectionTop();
        this.Eyebrow(label);
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.DrawTagFlow(labels, signal, innerWidth);
        this.SectionBottom(fullWidth);
    }

    private void DrawTagFlow(IReadOnlyList<string> labels, bool signal, float innerWidth)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var gap = Ui.Px(6f);
        var h = Ui.Px(28f);
        var x = origin.X;
        var y = origin.Y;
        var rows = 1;

        foreach (var label in labels)
        {
            var ts = Ui.Measure(this.fonts.LabelSmall, label);
            var w = ts.X + Ui.Px(20f);
            if (x > origin.X && (x + w) > (origin.X + innerWidth))
            {
                x = origin.X;
                y += h + gap;
                rows++;
            }

            var pos = new Vector2(x, y);
            if (signal)
            {
                dl.AddRectFilled(pos, pos + new Vector2(w, h), Palette.WithAlpha(Palette.Signal, 0.10f).U32());
                dl.AddRect(pos, pos + new Vector2(w, h), Palette.WithAlpha(Palette.Signal, 0.40f).U32(), 0f, ImDrawFlags.None, 1f);
                Ui.TextAt(dl, this.fonts.LabelSmall, pos + new Vector2(Ui.Px(10f), (h - ts.Y) * 0.5f), Palette.Signal.U32(), label);
            }
            else
            {
                dl.AddRect(pos, pos + new Vector2(w, h), Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);
                Ui.TextAt(dl, this.fonts.LabelSmall, pos + new Vector2(Ui.Px(10f), (h - ts.Y) * 0.5f), Palette.TextSecondary.U32(), label);
            }

            x += w + gap;
        }

        ImGui.Dummy(new Vector2(innerWidth, (rows * h) + ((rows - 1) * gap)));
    }

    private void DrawAfterDark(float fullWidth, float innerWidth, AfterDarkDto ad)
    {
        this.SectionTop();

        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var moon = FontAwesomeIcon.Moon.ToIconString();
        var moonSize = Ui.Measure(this.fonts.Icon, moon);
        Ui.TextAt(dl, this.fonts.Icon, origin, Palette.Signal.U32(), moon);
        var labelX = origin.X + moonSize.X + Ui.Px(8f);
        var eSize = Ui.Measure(this.fonts.Eyebrow, "AFTER DARK");
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(labelX, origin.Y + ((moonSize.Y - eSize.Y) * 0.5f)), Palette.TextSecondary.U32(), "AFTER DARK");
        var pillX = labelX + eSize.X + Ui.Px(10f);
        var pillText = Ui.Measure(this.fonts.Eyebrow, "18+");
        var pillSize = new Vector2(pillText.X + Ui.Px(10f), moonSize.Y);
        var pillPos = new Vector2(pillX, origin.Y);
        dl.AddRectFilled(pillPos, pillPos + pillSize, Palette.WithAlpha(Palette.Signal, 0.10f).U32());
        dl.AddRect(pillPos, pillPos + pillSize, Palette.WithAlpha(Palette.Signal, 0.40f).U32(), 0f, ImDrawFlags.None, 1f);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(pillPos.X + Ui.Px(5f), pillPos.Y + ((pillSize.Y - pillText.Y) * 0.5f)), Palette.Signal.U32(), "18+");
        ImGui.Dummy(new Vector2(0f, moonSize.Y));

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.AfterDarkRow("Position", ad.Position is { } p ? ProfileMapper.Label(p) : "—");
        this.AfterDarkRow("Role", ad.Role is { } r ? ProfileMapper.Label(r) : "—");
        this.AfterDarkRow("Size", ad.Size is { } s ? ProfileMapper.Label(s) : "—");
        this.AfterDarkRow("Meet", ad.Meet.Count > 0 ? string.Join(" · ", ad.Meet.Select(ProfileMapper.Label)) : "—");

        if (ad.Kinks.Count > 0)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
            this.Eyebrow("Kinks");
            ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
            this.DrawTagFlow(ad.Kinks.ToArray(), signal: true, innerWidth);
        }

        this.SectionBottom(fullWidth);
    }

    private void AfterDarkRow(string label, string value)
    {
        using (this.fonts.LabelSmall.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextUnformatted(label);
        ImGui.SameLine(Ui.Px(96f));
        using (this.fonts.LabelSmall.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
            ImGui.TextUnformatted(value);
        ImGui.Dummy(new Vector2(0f, Ui.Px(4f)));
    }

    private void DrawAlbums(float fullWidth, float innerWidth)
    {
        if (this.selection.ProfileUserId is not { } userId)
            return;
        var peerAlbums = this.albums.PeerAlbums(userId);
        if (peerAlbums.Count == 0)
            return;

        this.SectionTop();
        this.Eyebrow("Albums");
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        foreach (var album in peerAlbums)
            this.DrawAlbumRow(userId, album, innerWidth);
        this.SectionBottom(fullWidth);
    }

    private void DrawAlbumRow(Guid userId, PeerAlbumDto album, float innerWidth)
    {
        var rowH = Ui.Px(56f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##pd_album_" + album.Id, new Vector2(innerWidth, rowH));
        var dl = ImGui.GetWindowDrawList();

        var viewable = album.Access is PeerAlbumAccessEnum.Public or PeerAlbumAccessEnum.Granted;
        var thumb = Ui.Px(44f);
        var tmin = new Vector2(pos.X, pos.Y + ((rowH - thumb) * 0.5f));
        var tmax = tmin + new Vector2(thumb, thumb);
        dl.AddRectFilled(tmin, tmax, Palette.Surface2.U32());
        dl.AddRect(tmin, tmax, Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);
        var tex = viewable && album.CoverPhotoId is { } cover ? this.albums.Texture(album.Id, cover) : null;
        if (tex is { Width: > 0, Height: > 0 })
        {
            var (uvMin, uvMax) = Ui.CoverUv(tex.Width, tex.Height, 1f);
            dl.AddImage(tex.Handle, tmin, tmax, uvMin, uvMax);
        }
        else
        {
            var glyph = (viewable ? FontAwesomeIcon.Star : FontAwesomeIcon.Lock).ToIconString();
            var gs = Ui.Measure(this.fonts.Icon, glyph);
            Ui.TextAt(dl, this.fonts.Icon, ((tmin + tmax) * 0.5f) - (gs * 0.5f), Palette.TextMuted.U32(), glyph);
        }

        var textX = pos.X + thumb + Ui.Px(12f);
        Ui.TextAt(dl, this.fonts.Label, new Vector2(textX, pos.Y + Ui.Px(11f)), Palette.TextPrimary.U32(), album.Name);
        var vis = album.Access == PeerAlbumAccessEnum.Public ? "public" : "private";
        var meta = album.PhotoCount == 1 ? $"1 photo · {vis}" : $"{album.PhotoCount} photos · {vis}";
        Ui.TextAt(dl, this.fonts.LabelSmall, new Vector2(textX, pos.Y + Ui.Px(30f)), Palette.TextMuted.U32(), meta);

        var midY = pos.Y + (rowH * 0.5f);
        if (viewable)
        {
            const string label = "View";
            var ls = Ui.Measure(this.fonts.LabelSmall, label);
            var chevron = FontAwesomeIcon.ChevronRight.ToIconString();
            var chs = Ui.Measure(this.fonts.Icon, chevron);
            var ax = (pos.X + innerWidth) - ls.X - Ui.Px(6f) - chs.X;
            Ui.TextAt(dl, this.fonts.LabelSmall, new Vector2(ax, midY - (ls.Y * 0.5f)), Palette.Signal.U32(), label);
            Ui.TextAt(dl, this.fonts.Icon, new Vector2(ax + ls.X + Ui.Px(6f), midY - (chs.Y * 0.5f)), Palette.Signal.U32(), chevron);
        }
        else if (album.Access == PeerAlbumAccessEnum.Requested)
        {
            const string label = "Requested";
            var ls = Ui.Measure(this.fonts.LabelSmall, label);
            Ui.TextAt(dl, this.fonts.LabelSmall, new Vector2((pos.X + innerWidth) - ls.X, midY - (ls.Y * 0.5f)), Palette.TextMuted.U32(), label);
        }
        else
        {
            const string label = "Request access";
            var ls = Ui.Measure(this.fonts.LabelSmall, label);
            var pillW = ls.X + Ui.Px(20f);
            var pillH = Ui.Px(28f);
            var pillPos = new Vector2((pos.X + innerWidth) - pillW, midY - (pillH * 0.5f));
            dl.AddRect(pillPos, pillPos + new Vector2(pillW, pillH), Palette.WithAlpha(Palette.Signal, 0.40f).U32(), 0f, ImDrawFlags.None, 1f);
            Ui.TextAt(dl, this.fonts.LabelSmall, new Vector2(pillPos.X + Ui.Px(10f), pillPos.Y + ((pillH - ls.Y) * 0.5f)), Palette.Signal.U32(), label);
        }

        if (clicked)
        {
            if (viewable)
            {
                this.selection.AlbumId = album.Id;
                this.selection.AlbumName = album.Name;
                this.selection.AlbumReturn = Screen.ProfileDetail;
                this.router.Navigate(Screen.AlbumViewer);
            }
            else if (album.Access == PeerAlbumAccessEnum.Locked)
            {
                this.albums.RequestAccess(album.Id, userId);
            }
            else if (album.Access == PeerAlbumAccessEnum.Requested)
            {
                this.albums.InvalidatePeer(userId);
            }
        }
    }

    private void DrawActions(float contentWidth)
    {
        if (this.isSelf)
        {
            if (this.CreamButton("##pd_self_back", "Back to my profile", contentWidth, Ui.Px(44f)))
                this.router.Navigate(Screen.MyProfile);
            return;
        }

        var square = Ui.Px(44f);
        var gap = Ui.Px(8f);
        var messageWidth = contentWidth - square - gap;

        if (this.CreamButton("##pd_message", "Send a message", messageWidth, square))
        {
            this.selection.ProfileUserId = this.current.UserId;
            this.selection.ProfileDisplayName = this.current.DisplayName;
            this.router.Navigate(Screen.Chat);
        }

        ImGui.SameLine(0f, gap);
        if (this.SquareButton("##pd_fav", FontAwesomeIcon.Bookmark, square, this.favorited))
        {
            this.favorited = !this.favorited;
            this.safety.Favorite(this.current.UserId, this.favorited);
        }
    }

    private bool CreamButton(string id, string label, float width, float height)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(width, height));
        var dl = ImGui.GetWindowDrawList();
        var bg = (ImGui.IsItemHovered() ? Palette.WithAlpha(Palette.TextPrimary, 0.9f) : Palette.TextPrimary).U32();
        dl.AddRectFilled(pos, pos + new Vector2(width, height), bg);
        var ts = Ui.Measure(this.fonts.Label, label);
        Ui.TextAt(dl, this.fonts.Label, pos + new Vector2((width - ts.X) * 0.5f, (height - ts.Y) * 0.5f), Palette.Paper.U32(), label);
        return clicked;
    }

    private bool SquareButton(string id, FontAwesomeIcon icon, float size, bool active)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var dl = ImGui.GetWindowDrawList();
        if (active)
            dl.AddRectFilled(pos, pos + new Vector2(size, size), Palette.WithAlpha(Palette.Signal, 0.10f).U32());
        var border = active ? Palette.Signal : (ImGui.IsItemHovered() ? Palette.BorderStrong : Palette.Border);
        dl.AddRect(pos, pos + new Vector2(size, size), border.U32(), 0f, ImDrawFlags.None, 1f);
        var glyph = icon.ToIconString();
        var gs = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(dl, this.fonts.Icon, pos + new Vector2((size - gs.X) * 0.5f, (size - gs.Y) * 0.5f), (active ? Palette.Signal : Palette.TextSecondary).U32(), glyph);
        return clicked;
    }

    private void Eyebrow(string text)
    {
        using (this.fonts.Eyebrow.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
            ImGui.TextUnformatted(text.ToUpperInvariant());
    }

    private void SectionTop() => ImGui.Dummy(new Vector2(0f, Ui.Px(22f)));

    private void SectionBottom(float fullWidth)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(22f)));
        var y = ImGui.GetCursorScreenPos().Y;
        var wx = ImGui.GetWindowPos().X;
        ImGui.GetWindowDrawList().AddLine(new Vector2(wx, y), new Vector2(wx + fullWidth, y), Palette.Border.U32(), 1f);
        ImGui.Dummy(new Vector2(0f, 1f));
    }

    private static (string First, string Surname) SplitName(string name)
    {
        var sp = name.IndexOf(' ');
        return sp > 0 ? (name[..sp], name[(sp + 1)..]) : (name, string.Empty);
    }

    private static string Handle(string name, string? world)
    {
        var sp = name.IndexOf(' ');
        var first = (sp > 0 ? name[..sp] : name).ToLowerInvariant();
        return world is { Length: > 0 } ? $"{first}.{world.ToLowerInvariant()}" : first;
    }

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
        var y = (heroPos.Y + Ui.Px(14f));

        for (var i = 0; i < count; i++)
        {
            var x = startX + (i * (dotWidth + gap));
            var color = i == activeIndex ? Palette.Signal : Palette.WithAlpha(Palette.White, 0.4f);
            drawList.AddRectFilled(new Vector2(x, y), new Vector2(x + dotWidth, y + dotHeight), color.U32());
        }
    }
}
