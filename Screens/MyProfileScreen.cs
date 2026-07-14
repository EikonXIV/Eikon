using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// My profile (warm-editorial). A self-preview of the profile-detail view — "how others see you":
// a hero, the portraits grid, data table, albums, about, interests, looking-for and after-dark, pinned
// under a preview strip and above a sticky Edit CTA. Edit swaps the body for a single scrolling form:
// the 6-slot photo manager plus inline pickers for every field. Save pushes the whole profile, Cancel
// reloads it. The preview reads the server's own ProfileDetailDto for the signed-in user, so unsupported
// mockup fields (FFXIV job/role, tagline) are simply absent rather than faked.
internal sealed class MyProfileScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly Lightbox lightbox;
    private readonly ProfileService profiles;
    private readonly WorldCatalog catalog;
    private readonly PhotoManager photos;
    private readonly PhotoService photoSvc;
    private readonly Selection selection;
    private readonly SessionStore session;
    private readonly ProfileDetailService details;
    private readonly AlbumService albums;

    private int worldId;
    private bool applied;
    private bool editing;
    private int editDc = -1;
    private int heroIndex;
    private Guid previewFor;

    private string displayName = string.Empty;
    private string bio = string.Empty;
    private int pronoun;
    private string pronounCustom = string.Empty;
    private int gender;
    private string genderCustom = string.Empty;
    private int age = 27;
    private readonly bool[] races = new bool[Options.Races.Length];
    private readonly bool[] tribes = new bool[Options.Tribes.Length];
    private readonly bool[] lookingFor = new bool[Options.LookingFor.Length];
    private readonly bool[] interests = new bool[Options.Interests.Length];
    private bool nsfwEnabled = true;
    private int position = 2;
    private int roleIndex = 2;
    private int size = 1;
    private readonly bool[] meet = new bool[Options.Meet.Length];
    private readonly bool[] kinks = new bool[Options.Kinks.Length];

    public MyProfileScreen(ScreenRouter router, Kit kit, UiFonts fonts, Lightbox lightbox, ProfileService profiles, WorldCatalog catalog, PhotoManager photos, PhotoService photoSvc, Selection selection, SessionStore session, ProfileDetailService details, AlbumService albums)
    {
        this.router = router;
        this.kit = kit;
        this.fonts = fonts;
        this.lightbox = lightbox;
        this.profiles = profiles;
        this.catalog = catalog;
        this.photos = photos;
        this.photoSvc = photoSvc;
        this.selection = selection;
        this.session = session;
        this.details = details;
        this.albums = albums;
    }

    public Screen Id => Screen.MyProfile;

    public bool Chrome => true;

    public void Draw()
    {
        var pad = Ui.Px(16f);
        var contentWidth = ImGui.GetContentRegionAvail().X - (pad * 2f);

        this.profiles.EnsureLoaded();
        if (!this.applied)
        {
            if (!this.profiles.Loaded)
            {
                ImGui.Dummy(new Vector2(0f, Ui.Px(40f)));
                Ui.CenteredText(contentWidth, this.fonts.Caption, Palette.TextMuted, "Loading…");
                return;
            }

            if (this.profiles.Mine is { } mine)
                this.ApplyFromServer(mine);
            this.applied = true;
        }

        if (this.photos.IsCropping)
        {
            this.photos.Draw(contentWidth);
            return;
        }

        this.catalog.EnsureLoaded();

        if (this.editing)
        {
            ImGui.Indent(pad);
            ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
            this.DrawForm(contentWidth);
            ImGui.Unindent(pad);
        }
        else
        {
            this.DrawPreview(ImGui.GetContentRegionAvail());
        }

        this.lightbox.Draw();
    }

    // ---- preview (how others see you) ----

    private void DrawPreview(Vector2 avail)
    {
        this.photoSvc.EnsureLoaded();
        this.albums.EnsureLoaded();

        var stripH = Ui.Px(44f);
        var ctaH = Ui.Px(64f);
        var rootPos = ImGui.GetCursorScreenPos();

        this.DrawPreviewStrip(avail.X, stripH);

        ProfileDetailDto? loaded = null;
        if (this.session.UserId is { } me)
        {
            this.details.Ensure(me);
            loaded = this.details.Current;
        }

        var bodyH = avail.Y - stripH - ctaH;
        ImGui.SetCursorPos(new Vector2(0f, stripH));
        using (var body = ImRaii.Child("mp_preview", new Vector2(avail.X, bodyH), false, ImGuiWindowFlags.NoScrollbar))
        {
            if (body.Success)
            {
                if (loaded is null)
                {
                    ImGui.Dummy(new Vector2(0f, Ui.Px(80f)));
                    Ui.CenteredText(avail.X, this.fonts.Caption, Palette.TextMuted, "Loading preview…");
                }
                else
                {
                    if (this.previewFor != loaded.UserId)
                    {
                        this.previewFor = loaded.UserId;
                        this.heroIndex = 0;
                    }

                    this.DrawHero(avail.X, loaded);
                    this.DrawPortraits(avail.X, loaded);
                    this.DrawDataTable(avail.X, loaded);
                    this.DrawSections(avail.X, loaded);
                }
            }
        }

        // Sticky Edit CTA, pinned above the bottom nav with a hairline separating it from the scroll body.
        var footerY = rootPos.Y + stripH + bodyH;
        ImGui.GetWindowDrawList().AddLine(new Vector2(rootPos.X, footerY), new Vector2(rootPos.X + avail.X, footerY), Palette.Border.U32(), 1f);
        var ctaPad = Ui.Px(16f);
        ImGui.SetCursorPos(new Vector2(ctaPad, stripH + bodyH + Ui.Px(10f)));
        if (this.EditCta(avail.X - (ctaPad * 2f), Ui.Px(44f)))
            this.OpenForm();
    }

    private void DrawPreviewStrip(float fullWidth, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var pad = Ui.Px(20f);
        var midY = origin.Y + (height * 0.5f);

        const string label = "YOUR PROFILE PREVIEW · HOW OTHERS SEE YOU";
        var ls = Ui.Measure(this.fonts.Eyebrow, label);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(origin.X + pad, midY - (ls.Y * 0.5f)), Palette.TextSecondary.U32(), label);

        var pen = FontAwesomeIcon.Pen.ToIconString();
        var ps = Ui.Measure(this.fonts.Icon, pen);
        ImGui.SetCursorScreenPos(new Vector2((origin.X + fullWidth - pad) - ps.X, midY - (ps.Y * 0.5f)));
        if (ImGui.InvisibleButton("##mp_edit_top", ps))
            this.OpenForm();
        Ui.TextAt(dl, this.fonts.Icon, ImGui.GetItemRectMin(), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32(), pen);

        dl.AddLine(new Vector2(origin.X, origin.Y + height - 1f), new Vector2(origin.X + fullWidth, origin.Y + height - 1f), Palette.Border.U32(), 1f);
    }

    private void DrawHero(float fullWidth, ProfileDetailDto dto)
    {
        var heroHeight = fullWidth * 1.25f;
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(fullWidth, heroHeight), Palette.Surface2.U32());

        var photos = dto.PhotoIds;
        var count = photos.Count;
        this.heroIndex = count > 0 ? Math.Clamp(this.heroIndex, 0, count - 1) : 0;
        var photoId = count > 0 ? photos[this.heroIndex] : dto.MainPhotoId;
        var texture = photoId is { } id ? this.photoSvc.Texture(id) : null;
        if (texture != null)
        {
            var (uvMin, uvMax) = Ui.CoverUv(texture.Width, texture.Height, fullWidth / heroHeight, offsetY: 0.2f);
            dl.AddImage(texture.Handle, pos, pos + new Vector2(fullWidth, heroHeight), uvMin, uvMax);
        }
        else
        {
            var initial = dto.DisplayName.Length > 0 ? dto.DisplayName[..1].ToUpperInvariant() : "?";
            var isz = Ui.Measure(this.fonts.SerifTitle, initial);
            Ui.TextAt(dl, this.fonts.SerifTitle, pos + new Vector2((fullWidth - isz.X) * 0.5f, (heroHeight * 0.35f) - (isz.Y * 0.5f)), Palette.TextMuted.U32(), initial);
        }

        // Photo counter chip, top-right (NN / NN).
        if (count > 0)
        {
            var counter = $"{this.heroIndex + 1:00} / {count:00}";
            var cs = Ui.Measure(this.fonts.Count, counter);
            var chipPad = new Vector2(Ui.Px(8f), Ui.Px(4f));
            var chipSize = cs + (chipPad * 2f);
            var chipPos = new Vector2((pos.X + fullWidth) - Ui.Px(12f) - chipSize.X, pos.Y + Ui.Px(12f));
            dl.AddRectFilled(chipPos, chipPos + chipSize, Palette.Scrim.U32());
            Ui.TextAt(dl, this.fonts.Count, chipPos + chipPad, Palette.WithAlpha(Palette.White, 0.9f).U32(), counter);
        }

        // Gradient + overlaid status eyebrow and the two-tone name, anchored bottom-left.
        var gradTop = pos + new Vector2(0f, heroHeight * 0.55f);
        var clear = Palette.WithAlpha(Palette.Bg, 0f).U32();
        var solid = Palette.WithAlpha(Palette.Bg, 0.96f).U32();
        dl.AddRectFilledMultiColor(gradTop, pos + new Vector2(fullWidth, heroHeight), clear, clear, solid, solid);

        var ox = pos.X + Ui.Px(20f);
        var (first, rest) = SplitName(dto.DisplayName);
        var firstH = Ui.Measure(this.fonts.SerifTitle, first).Y;
        var restH = rest.Length > 0 ? Ui.Measure(this.fonts.SerifItalicTitle, rest).Y : 0f;
        var restY = (pos.Y + heroHeight) - Ui.Px(20f) - restH;
        var firstY = restY - firstH + Ui.Px(4f);
        if (rest.Length > 0)
            Ui.TextAt(dl, this.fonts.SerifItalicTitle, new Vector2(ox, restY), Palette.Signal.U32(), rest);
        Ui.TextAt(dl, this.fonts.SerifTitle, new Vector2(ox, firstY), Palette.TextPrimary.U32(), first);

        if (dto.Online)
        {
            const string status = "ONLINE NOW";
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
            if (ImGui.InvisibleButton("##mp_hero_prev", new Vector2(third, heroHeight)))
                this.heroIndex = (this.heroIndex - 1 + count) % count;
            ImGui.SetCursorScreenPos(pos + new Vector2(third, 0f));
            if (ImGui.InvisibleButton("##mp_hero_open", new Vector2(third, heroHeight)))
                this.lightbox.OpenPhotos(photos, this.heroIndex);
            ImGui.SetCursorScreenPos(pos + new Vector2(third * 2f, 0f));
            if (ImGui.InvisibleButton("##mp_hero_next", new Vector2(fullWidth - (third * 2f), heroHeight)))
                this.heroIndex = (this.heroIndex + 1) % count;
        }
        else
        {
            ImGui.SetCursorScreenPos(pos);
            if (ImGui.InvisibleButton("##mp_hero", new Vector2(fullWidth, heroHeight)) && count > 0)
                this.lightbox.OpenPhotos(photos, this.heroIndex);
        }

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(fullWidth, heroHeight));
    }

    private void DrawPortraits(float fullWidth, ProfileDetailDto dto)
    {
        var pad = Ui.Px(20f);
        var dl = ImGui.GetWindowDrawList();
        var photos = dto.PhotoIds;
        var count = photos.Count;

        ImGui.Dummy(new Vector2(0f, Ui.Px(22f)));
        var head = ImGui.GetCursorScreenPos();
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(head.X + pad, head.Y), Palette.TextSecondary.U32(), "PORTRAITS");
        var slots = $"{count:00} slots";
        var ss = Ui.Measure(this.fonts.Eyebrow, slots);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2((head.X + fullWidth) - pad - ss.X, head.Y), Palette.TextMuted.U32(), slots);
        ImGui.Dummy(new Vector2(0f, Ui.Measure(this.fonts.Eyebrow, "X").Y + Ui.Px(14f)));

        if (count == 0)
            return;

        const int cols = 3;
        var gap = Ui.Px(8f);
        var innerWidth = fullWidth - (pad * 2f);
        var tileW = (innerWidth - (gap * (cols - 1))) / cols;
        var tileH = tileW * 1.3f;
        var basePos = ImGui.GetCursorScreenPos();
        var originX = basePos.X + pad;
        var rows = (count + cols - 1) / cols;

        for (var i = 0; i < count; i++)
        {
            var col = i % cols;
            var row = i / cols;
            var tp = new Vector2(originX + (col * (tileW + gap)), basePos.Y + (row * (tileH + gap)));
            var tmax = tp + new Vector2(tileW, tileH);
            dl.AddRectFilled(tp, tmax, Palette.Surface2.U32());
            var tex = this.photoSvc.Texture(photos[i]);
            if (tex != null)
            {
                var (uvMin, uvMax) = Ui.CoverUv(tex.Width, tex.Height, tileW / tileH);
                dl.AddImage(tex.Handle, tp, tmax, uvMin, uvMax);
            }
            dl.AddRect(tp, tmax, Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);

            var num = $"{i + 1:00}";
            var ns = Ui.Measure(this.fonts.Mono, num);
            var badgePad = new Vector2(Ui.Px(5f), Ui.Px(3f));
            var badgePos = tp + new Vector2(Ui.Px(6f), Ui.Px(6f));
            dl.AddRectFilled(badgePos, badgePos + ns + (badgePad * 2f), Palette.Scrim.U32());
            Ui.TextAt(dl, this.fonts.Mono, badgePos + badgePad, Palette.WithAlpha(Palette.White, 0.85f).U32(), num);

            ImGui.SetCursorScreenPos(tp);
            if (ImGui.InvisibleButton($"##mp_portrait_{i}", new Vector2(tileW, tileH)))
                this.lightbox.OpenPhotos(photos, i);
        }

        ImGui.SetCursorScreenPos(basePos);
        ImGui.Dummy(new Vector2(fullWidth, (rows * tileH) + ((rows - 1) * gap)));
    }

    private void DrawDataTable(float fullWidth, ProfileDetailDto dto)
    {
        // Only fields the profile model actually carries — the mockup's FFXIV job/role cells have no data.
        var cells = new (string Label, string Value, bool Mono)[]
        {
            ("World", dto.World, false),
            ("Data Center", dto.Dc, false),
            ("Age", dto.Age.ToString(), true),
            ("Last Seen", dto.Online ? "Now" : LastSeen(dto.LastSeenAt), false),
        };

        ImGui.Dummy(new Vector2(0f, Ui.Px(22f)));
        this.FullRule(fullWidth);

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
            var cx = origin.X + (col * cellW);
            var cy = origin.Y + (row * cellH);
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(cx + pad, cy + Ui.Px(12f)), Palette.TextSecondary.U32(), cells[i].Label.ToUpperInvariant());
            var valueFont = cells[i].Mono ? this.fonts.Eyebrow : this.fonts.Label;
            Ui.TextAt(dl, valueFont, new Vector2(cx + pad, cy + Ui.Px(28f)), Palette.TextPrimary.U32(), cells[i].Value);
            if (col == 0)
                dl.AddLine(new Vector2(cx + cellW, cy), new Vector2(cx + cellW, cy + cellH), Palette.Border.U32(), 1f);
            dl.AddLine(new Vector2(cx, cy + cellH), new Vector2(cx + cellW, cy + cellH), Palette.Border.U32(), 1f);
        }

        ImGui.Dummy(new Vector2(fullWidth, rows * cellH));
    }

    private void DrawSections(float fullWidth, ProfileDetailDto dto)
    {
        var pad = Ui.Px(20f);
        var innerWidth = fullWidth - (pad * 2f);
        ImGui.Indent(pad);

        this.DrawAlbums(fullWidth, innerWidth);

        if (!string.IsNullOrWhiteSpace(dto.Bio))
        {
            this.SectionTop();
            this.Eyebrow("About");
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.WithAlpha(Palette.TextPrimary, 0.9f)))
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + innerWidth);
                ImGui.TextUnformatted(dto.Bio);
                ImGui.PopTextWrapPos();
            }
            this.SectionBottom(fullWidth);
        }

        if (dto.Interests.Count > 0)
            this.TagSection(fullWidth, innerWidth, "Interests", dto.Interests.ToArray(), signal: false);

        if (dto.LookingFor.Count > 0)
            this.TagSection(fullWidth, innerWidth, "Looking for", ProfileMapper.Labels(dto.LookingFor).ToArray(), signal: true);

        if (dto.AfterDark is { } ad)
            this.DrawAfterDark(fullWidth, innerWidth, ad);

        ImGui.Unindent(pad);
        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
    }

    private void DrawAlbums(float fullWidth, float innerWidth)
    {
        var mine = this.albums.Mine;
        if (mine.Count == 0)
            return;

        this.SectionTop();
        this.Eyebrow("Albums");
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        foreach (var album in mine)
            this.DrawAlbumRow(album, innerWidth);
        this.SectionBottom(fullWidth);
    }

    private void DrawAlbumRow(AlbumDto album, float innerWidth)
    {
        var rowH = Ui.Px(56f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##mp_album_" + album.Id, new Vector2(innerWidth, rowH));
        var dl = ImGui.GetWindowDrawList();

        var isPublic = album.Visibility == AlbumVisibilityEnum.Public;
        var thumb = Ui.Px(44f);
        var tmin = new Vector2(pos.X, pos.Y + ((rowH - thumb) * 0.5f));
        var tmax = tmin + new Vector2(thumb, thumb);
        dl.AddRectFilled(tmin, tmax, Palette.Surface2.U32());
        dl.AddRect(tmin, tmax, Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);
        var tex = album.CoverPhotoId is { } cover ? this.albums.Texture(album.Id, cover) : null;
        if (tex is { Width: > 0, Height: > 0 })
        {
            var (uvMin, uvMax) = Ui.CoverUv(tex.Width, tex.Height, 1f);
            dl.AddImage(tex.Handle, tmin, tmax, uvMin, uvMax);
        }
        else
        {
            var glyph = (isPublic ? FontAwesomeIcon.Star : FontAwesomeIcon.Lock).ToIconString();
            var gs = Ui.Measure(this.fonts.Icon, glyph);
            Ui.TextAt(dl, this.fonts.Icon, ((tmin + tmax) * 0.5f) - (gs * 0.5f), Palette.TextMuted.U32(), glyph);
        }

        var textX = pos.X + thumb + Ui.Px(12f);
        Ui.TextAt(dl, this.fonts.Label, new Vector2(textX, pos.Y + Ui.Px(11f)), Palette.TextPrimary.U32(), album.Name);
        var vis = isPublic ? "public" : "private";
        var meta = album.PhotoCount == 1 ? $"1 photo · {vis}" : $"{album.PhotoCount} photos · {vis}";
        Ui.TextAt(dl, this.fonts.LabelSmall, new Vector2(textX, pos.Y + Ui.Px(30f)), Palette.TextMuted.U32(), meta);

        // Self-preview: every album is viewable to me, so the affordance is always View (the public/private
        // state a visitor keys off is carried in the meta line above).
        var midY = pos.Y + (rowH * 0.5f);
        const string label = "View";
        var lsz = Ui.Measure(this.fonts.LabelSmall, label);
        var chevron = FontAwesomeIcon.ChevronRight.ToIconString();
        var chs = Ui.Measure(this.fonts.Icon, chevron);
        var ax = (pos.X + innerWidth) - lsz.X - Ui.Px(6f) - chs.X;
        Ui.TextAt(dl, this.fonts.LabelSmall, new Vector2(ax, midY - (lsz.Y * 0.5f)), Palette.Signal.U32(), label);
        Ui.TextAt(dl, this.fonts.Icon, new Vector2(ax + lsz.X + Ui.Px(6f), midY - (chs.Y * 0.5f)), Palette.Signal.U32(), chevron);

        if (clicked)
        {
            this.selection.AlbumId = album.Id;
            this.selection.AlbumName = album.Name;
            this.selection.AlbumReturn = Screen.MyProfile;
            this.router.Navigate(Screen.AlbumViewer);
        }
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
        if (ad.Position is { } p) this.AfterDarkRow("Position", ProfileMapper.Label(p));
        if (ad.Role is { } r) this.AfterDarkRow("Role", ProfileMapper.Label(r));
        if (ad.Size is { } s) this.AfterDarkRow("Size", ProfileMapper.Label(s));
        if (ad.Meet.Count > 0) this.AfterDarkRow("Meet", string.Join(" · ", ad.Meet.Select(ProfileMapper.Label)));

        if (ad.Kinks.Count > 0)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
            this.Eyebrow("Kinks");
            ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
            this.TagFlow(ad.Kinks.ToArray(), signal: true, innerWidth);
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

    private void TagSection(float fullWidth, float innerWidth, string label, IReadOnlyList<string> labels, bool signal)
    {
        this.SectionTop();
        this.Eyebrow(label);
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.TagFlow(labels, signal, innerWidth);
        this.SectionBottom(fullWidth);
    }

    private void TagFlow(IReadOnlyList<string> labels, bool signal, float innerWidth)
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

    private bool EditCta(float width, float height)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##mp_edit_cta", new Vector2(width, height));
        var dl = ImGui.GetWindowDrawList();
        var bg = (ImGui.IsItemHovered() ? Palette.WithAlpha(Palette.TextPrimary, 0.9f) : Palette.TextPrimary).U32();
        dl.AddRectFilled(pos, pos + new Vector2(width, height), bg);

        var pen = FontAwesomeIcon.Pen.ToIconString();
        const string label = "Edit profile";
        var ps = Ui.Measure(this.fonts.Icon, pen);
        var lsz = Ui.Measure(this.fonts.Label, label);
        var gap = Ui.Px(8f);
        var startX = pos.X + ((width - (ps.X + gap + lsz.X)) * 0.5f);
        Ui.TextAt(dl, this.fonts.Icon, new Vector2(startX, pos.Y + ((height - ps.Y) * 0.5f)), Palette.Paper.U32(), pen);
        Ui.TextAt(dl, this.fonts.Label, new Vector2(startX + ps.X + gap, pos.Y + ((height - lsz.Y) * 0.5f)), Palette.Paper.U32(), label);
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

    private void FullRule(float fullWidth)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(pos, new Vector2(pos.X + fullWidth, pos.Y), Palette.Border.U32(), 1f);
        ImGui.Dummy(new Vector2(0f, 1f));
    }

    private static (string First, string Surname) SplitName(string name)
    {
        var sp = name.IndexOf(' ');
        return sp > 0 ? (name[..sp], name[(sp + 1)..]) : (name, string.Empty);
    }

    private static string LastSeen(DateTimeOffset? at)
    {
        if (at is null)
            return "—";
        var d = DateTimeOffset.UtcNow - at.Value;
        if (d.TotalMinutes < 1) return "Now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h ago";
        if (d.TotalDays < 7) return $"{(int)d.TotalDays}d ago";
        return at.Value.LocalDateTime.ToString("MMM d");
    }

    // ---- form ----

    private void OpenForm()
    {
        this.editing = true;
        this.editDc = this.DcIndexOf(this.worldId);
    }

    private void CloseForm(bool save)
    {
        if (save)
            this.profiles.Save(this.BuildRequest());
        else if (this.profiles.Mine is { } mine)
            this.ApplyFromServer(mine);

        // Force the preview to refetch so it reflects what was just saved (and clears cropped/new photos).
        this.details.Invalidate();
        this.previewFor = Guid.Empty;
        this.editing = false;
    }

    private void DrawForm(float contentWidth)
    {
        // Header: cancel (left), save (right).
        var buttonWidth = Ui.Px(84f);
        var rowX = ImGui.GetCursorPosX();
        if (this.kit.SecondaryButton("##f_cancel", "Cancel", buttonWidth))
        {
            this.CloseForm(false);
            return;
        }
        ImGui.SameLine();
        ImGui.SetCursorPosX(rowX + contentWidth - buttonWidth);
        if (this.kit.PrimaryButton("##f_save", "Save", buttonWidth))
        {
            this.CloseForm(true);
            return;
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.kit.SectionLabel("Photos");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.photos.DrawGrid(contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        this.kit.SectionLabel("Identity");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.kit.TextField("##f_name", ref this.displayName, "Display name", contentWidth);
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.Field("Pronouns");
        this.Single("f_pn", Options.Pronouns, ref this.pronoun, contentWidth);
        this.kit.TextField("##f_pnc", ref this.pronounCustom, "Custom (optional)", contentWidth);
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.Field("Gender");
        this.Single("f_gn", Options.Genders, ref this.gender, contentWidth);
        this.kit.TextField("##f_gnc", ref this.genderCustom, "Custom (optional)", contentWidth);
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.Field("Age");
        this.age = this.kit.Stepper("##f_age", this.age, 18, 99);

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        this.kit.SectionLabel("World");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.DrawWorld(contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        this.kit.SectionLabel("Matching");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.Field("Looking for");
        this.Multi("f_lf", Options.LookingFor, this.lookingFor, contentWidth);
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.Field("Into");
        this.Multi("f_into", Options.Interests, this.interests, contentWidth);
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.Field("Tribe");
        this.Multi("f_tribe", Options.Tribes, this.tribes, contentWidth);
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.Field("Race");
        this.Multi("f_race", Options.Races, this.races, contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        this.kit.SectionLabel("About");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Palette.Surface2))
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(Ui.Px(12f), Ui.Px(10f))))
        using (this.fonts.Caption.Push())
            ImGui.InputTextMultiline("##f_bio", ref this.bio, 300, new Vector2(contentWidth, Ui.Px(110f)));

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        this.kit.SectionLabel("After dark");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.DrawAfterDark(contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(20f)));
        var half = (contentWidth - Ui.Px(10f)) * 0.5f;
        if (this.kit.PrimaryButton("##f_save2", "Save changes", half))
        {
            this.CloseForm(true);
            return;
        }
        ImGui.SameLine(0f, Ui.Px(10f));
        if (this.kit.SecondaryButton("##f_cancel2", "Cancel", half))
        {
            this.CloseForm(false);
            return;
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        if (this.kit.SecondaryButton("##f_preview", "Preview as others see it", contentWidth) && this.session.UserId is { } me)
        {
            this.selection.ProfileUserId = me;
            this.selection.ProfileDisplayName = this.displayName;
            this.selection.ProfileReturn = Screen.MyProfile;
            this.details.Invalidate();
            this.router.Navigate(Screen.ProfileDetail);
        }
        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
    }

    private void DrawWorld(float contentWidth)
    {
        if (!this.catalog.Ready)
        {
            this.Helper("Loading worlds…");
            return;
        }

        var dcs = this.catalog.DataCenters;
        if (this.editDc < 0)
            this.editDc = this.DcIndexOf(this.worldId);

        this.Field("Data center");
        var dc = this.kit.ChipFlow("f_dc", dcs.Select(d => d.Name).ToArray(), i => i == this.editDc, contentWidth);
        if (dc >= 0)
        {
            this.editDc = dc;
            this.worldId = 0;
        }

        if (this.editDc >= 0 && this.editDc < dcs.Count)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            this.Field("Home world");
            var worlds = dcs[this.editDc].Worlds;
            var w = this.kit.ChipFlow("f_world", worlds.Select(x => x.Name).ToArray(), i => worlds[i].Id == this.worldId, contentWidth);
            if (w >= 0)
                this.worldId = worlds[w].Id;
        }
    }

    private void DrawAfterDark(float contentWidth)
    {
        using (this.fonts.Body.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
            ImGui.TextUnformatted("Enable after dark (18+)");
        ImGui.SameLine(0f, Ui.Px(10f));
        this.nsfwEnabled = this.kit.Toggle("##f_nsfw", this.nsfwEnabled);
        if (!this.nsfwEnabled)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(4f)));
            this.Helper("Off removes the after-dark section from your profile.");
            return;
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.Field("Position");
        this.Single("f_pos", Options.Positions, ref this.position, contentWidth);
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.Field("Role");
        this.Single("f_role", Options.Roles, ref this.roleIndex, contentWidth);
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.Field("Size");
        this.Single("f_size", Options.Sizes, ref this.size, contentWidth);
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.Field("Meet");
        this.Multi("f_meet", Options.Meet, this.meet, contentWidth);
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.Field("Kinks");
        this.Multi("f_kinks", Options.Kinks, this.kinks, contentWidth);
    }

    private void Single(string id, string[] labels, ref int selected, float contentWidth)
    {
        var current = selected;
        var hit = this.kit.ChipFlow(id, labels, i => i == current, contentWidth);
        if (hit >= 0)
            selected = hit;
    }

    private void Multi(string id, string[] labels, bool[] flags, float contentWidth)
    {
        var hit = this.kit.ChipFlow(id, labels, i => flags[i], contentWidth);
        if (hit >= 0)
            flags[hit] = !flags[hit];
    }

    // ---- small pieces ----

    private void Field(string text)
    {
        using (this.fonts.Label.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
            ImGui.TextUnformatted(text);
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
    }

    private void Helper(string text)
    {
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextWrapped(text);
    }

    // ---- server state (reused) ----

    private int DcIndexOf(int world)
    {
        var dcs = this.catalog.DataCenters;
        for (var i = 0; i < dcs.Count; i++)
            foreach (var w in dcs[i].Worlds)
                if (w.Id == world)
                    return i;
        return -1;
    }

    private void ApplyFromServer(SaveProfileRequest m)
    {
        this.displayName = m.DisplayName;
        this.pronoun = ProfileMapper.IndexOfPronoun(m.Pronoun);
        this.pronounCustom = m.PronounCustom ?? string.Empty;
        this.gender = ProfileMapper.IndexOfGender(m.Gender);
        this.genderCustom = m.GenderCustom ?? string.Empty;
        this.age = (int)m.Age;
        CopyInto(this.races, ProfileMapper.FromRaces(m.Races ?? new List<RaceElement>()));
        this.worldId = (int)(m.WorldId ?? 0);
        this.bio = m.Bio ?? string.Empty;
        CopyInto(this.tribes, ProfileMapper.FromTribes(m.Tribes ?? new List<TribeElement>()));
        CopyInto(this.lookingFor, ProfileMapper.FromLookingFor(m.LookingFor ?? new List<LookingForElement>()));
        CopyInto(this.interests, ProfileMapper.FromLabels(m.Interests ?? new List<string>(), Options.Interests));
        this.nsfwEnabled = m.NsfwEnabled ?? false;
        if (m.AfterDark is { } ad)
        {
            if (ad.Position is { } p) this.position = ProfileMapper.IndexOfPosition(p);
            if (ad.Role is { } r) this.roleIndex = ProfileMapper.IndexOfRole(r);
            if (ad.Size is { } s) this.size = ProfileMapper.IndexOfSize(s);
            CopyInto(this.meet, ProfileMapper.FromMeet(ad.Meet ?? new List<MeetElement>()));
            CopyInto(this.kinks, ProfileMapper.FromLabels(ad.Kinks ?? new List<string>(), Options.Kinks));
        }
    }

    private SaveProfileRequest BuildRequest() => new()
    {
        DisplayName = this.displayName.Trim(),
        Pronoun = ProfileMapper.Pronoun(this.pronoun),
        PronounCustom = string.IsNullOrWhiteSpace(this.pronounCustom) ? null : this.pronounCustom,
        Gender = ProfileMapper.Gender(this.gender),
        GenderCustom = string.IsNullOrWhiteSpace(this.genderCustom) ? null : this.genderCustom,
        Age = this.age,
        Races = ProfileMapper.RacesOf(this.races),
        WorldId = this.worldId > 0 ? this.worldId : null,
        Tribes = ProfileMapper.TribesOf(this.tribes),
        Bio = string.IsNullOrWhiteSpace(this.bio) ? null : this.bio,
        LookingFor = ProfileMapper.LookingForOf(this.lookingFor),
        Interests = Options.Interests.Where((_, i) => this.interests[i]).ToList(),
        NsfwEnabled = this.nsfwEnabled,
        AfterDark = this.nsfwEnabled
            ? new AfterDarkDto
            {
                Position = ProfileMapper.Position(this.position),
                Role = ProfileMapper.Role(this.roleIndex),
                Size = ProfileMapper.Size(this.size),
                Meet = ProfileMapper.MeetOf(this.meet),
                Kinks = Options.Kinks.Where((_, i) => this.kinks[i]).ToList(),
            }
            : null,
    };

    private static void CopyInto(bool[] dst, bool[] src)
    {
        for (var i = 0; i < dst.Length && i < src.Length; i++)
            dst[i] = src[i];
    }
}
