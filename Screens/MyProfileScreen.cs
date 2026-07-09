using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// My profile (warm-editorial). A read-only card (portrait, summary, about, interests) with an Edit
// button that swaps the body for a single scrolling form: the 6-slot photo manager plus inline pickers
// for every field. The form edits live state; Save pushes the whole profile, Cancel reloads it.
internal sealed class MyProfileScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly Lightbox lightbox;
    private readonly ProfileService profiles;
    private readonly WorldCatalog catalog;
    private readonly PhotoManager photos;
    private readonly Selection selection;
    private readonly SessionStore session;
    private readonly ProfileDetailService details;

    private int worldId;
    private bool applied;
    private bool editing;
    private int editDc = -1;

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

    public MyProfileScreen(ScreenRouter router, Kit kit, UiFonts fonts, Lightbox lightbox, ProfileService profiles, WorldCatalog catalog, PhotoManager photos, Selection selection, SessionStore session, ProfileDetailService details)
    {
        this.router = router;
        this.kit = kit;
        this.fonts = fonts;
        this.lightbox = lightbox;
        this.profiles = profiles;
        this.catalog = catalog;
        this.photos = photos;
        this.selection = selection;
        this.session = session;
        this.details = details;
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

        ImGui.Indent(pad);
        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        if (this.editing)
            this.DrawForm(contentWidth);
        else
            this.DrawCard(contentWidth);
        ImGui.Unindent(pad);

        this.lightbox.Draw();
    }

    // ---- card ----

    private void DrawCard(float contentWidth)
    {
        var dl = ImGui.GetWindowDrawList();

        this.Eyebrow("Your card");
        ImGui.Dummy(new Vector2(0f, Ui.Px(2f)));
        var rowX = ImGui.GetCursorPosX();
        using (this.fonts.SerifTitle.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
            ImGui.TextUnformatted("Profile");

        // Edit affordance, right-aligned on the title row.
        var editSize = Ui.Measure(this.fonts.Label, "Edit");
        ImGui.SameLine();
        ImGui.SetCursorPosX(rowX + contentWidth - editSize.X);
        if (this.TextButton("##edit", "Edit"))
            this.OpenForm();

        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));

        // Portrait + summary
        var thumb = new Vector2(Ui.Px(88f), Ui.Px(112f));
        var tpos = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(tpos, tpos + thumb, Palette.Surface2.U32());
        dl.AddRect(tpos, tpos + thumb, Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);
        var initial = this.displayName.Length > 0 ? this.displayName[..1].ToUpperInvariant() : "?";
        var initSize = Ui.Measure(this.fonts.SerifTitle, initial);
        Ui.TextAt(dl, this.fonts.SerifTitle, tpos + ((thumb - initSize) * 0.5f), Palette.TextMuted.U32(), initial);

        var textX = tpos.X + thumb.X + Ui.Px(16f);
        dl.AddCircleFilled(new Vector2(textX + Ui.Px(3f), tpos.Y + Ui.Px(9f)), Ui.Px(3f), Palette.Online.U32(), 12);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(textX + Ui.Px(13f), tpos.Y + Ui.Px(2f)), Palette.TextSecondary.U32(), "VISIBLE ON GRID");
        Ui.TextAt(dl, this.fonts.SerifTitle, new Vector2(textX, tpos.Y + Ui.Px(24f)), Palette.TextPrimary.U32(), this.displayName.Length > 0 ? this.displayName : "Your name");
        var summary = this.worldId > 0 ? this.catalog.WorldName(this.worldId) : "Set your world";
        Ui.TextAt(dl, this.fonts.Label, new Vector2(textX, tpos.Y + Ui.Px(58f)), Palette.TextSecondary.U32(), summary);

        ImGui.Dummy(thumb);
        this.SectionBottom(contentWidth);

        // Identity — server-driven so the card reflects the real profile.
        this.SectionTop();
        this.DrawCardTable(contentWidth);
        this.SectionBottom(contentWidth);

        // About
        this.SectionTop();
        this.Eyebrow("About you");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, this.bio.Length > 0 ? Palette.WithAlpha(Palette.TextPrimary, 0.9f) : Palette.TextMuted))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentWidth);
            ImGui.TextUnformatted(this.bio.Length > 0 ? this.bio : "Add a short bio from Edit.");
            ImGui.PopTextWrapPos();
        }
        this.SectionBottom(contentWidth);

        // Interests
        var picked = new List<string>();
        for (var i = 0; i < this.interests.Length; i++)
            if (this.interests[i]) picked.Add(Options.Interests[i]);
        if (picked.Count > 0)
        {
            this.SectionTop();
            this.Eyebrow("Interests");
            ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
            this.DrawTags(picked, contentWidth);
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        if (this.kit.SecondaryButton("##preview", "Preview as others see it", contentWidth) && this.session.UserId is { } me)
        {
            this.selection.ProfileUserId = me;
            this.selection.ProfileDisplayName = this.displayName;
            this.details.Invalidate();
            this.router.Navigate(Screen.ProfileDetail);
        }
        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
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

    private void Eyebrow(string text)
    {
        using (this.fonts.Eyebrow.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
            ImGui.TextUnformatted(text.ToUpperInvariant());
    }

    private void Field(string text)
    {
        using (this.fonts.Label.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
            ImGui.TextUnformatted(text);
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
    }

    private bool TextButton(string id, string label)
    {
        var pos = ImGui.GetCursorScreenPos();
        var size = Ui.Measure(this.fonts.Label, label);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        Ui.TextAt(ImGui.GetWindowDrawList(), this.fonts.Label, pos, (hovered ? Palette.TextPrimary : Palette.TextSecondary).U32(), label);
        return clicked;
    }

    private void DrawTags(IReadOnlyList<string> labels, float innerWidth)
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
            dl.AddRect(pos, pos + new Vector2(w, h), Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);
            Ui.TextAt(dl, this.fonts.LabelSmall, pos + new Vector2(Ui.Px(10f), (h - ts.Y) * 0.5f), Palette.TextSecondary.U32(), label);
            x += w + gap;
        }
        ImGui.Dummy(new Vector2(innerWidth, (rows * h) + ((rows - 1) * gap)));
    }

    private void DrawCardTable(float contentWidth)
    {
        var pronoun = this.pronounCustom.Length > 0 ? this.pronounCustom : Options.Pronouns[this.pronoun];
        var gender = this.genderCustom.Length > 0 ? this.genderCustom : Options.Genders[this.gender];
        var picked = new List<string>();
        for (var i = 0; i < this.races.Length; i++)
            if (this.races[i]) picked.Add(Options.Races[i]);
        var race = picked.Count > 0 ? string.Join(" / ", picked) : "—";

        var cells = new (string Label, string Value, bool Mono)[]
        {
            ("Age", this.age.ToString(), true),
            ("Gender", gender, false),
            ("Pronouns", pronoun, false),
            ("Race", race, false),
        };

        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var colW = contentWidth / 2f;
        var cellH = Ui.Px(48f);
        var rows = (cells.Length + 1) / 2;

        for (var i = 0; i < cells.Length; i++)
        {
            var cx = origin.X + ((i % 2) * colW);
            var cy = origin.Y + ((i / 2) * cellH);
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(cx, cy + Ui.Px(6f)), Palette.TextSecondary.U32(), cells[i].Label.ToUpperInvariant());
            var valueFont = cells[i].Mono ? this.fonts.Eyebrow : this.fonts.Label;
            Ui.TextAt(dl, valueFont, new Vector2(cx, cy + Ui.Px(22f)), Palette.TextPrimary.U32(), cells[i].Value);
        }

        ImGui.Dummy(new Vector2(contentWidth, rows * cellH));
    }

    private void SectionTop() => ImGui.Dummy(new Vector2(0f, Ui.Px(20f)));

    private void SectionBottom(float contentWidth)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(20f)));
        var y = ImGui.GetCursorScreenPos().Y;
        var wx = ImGui.GetCursorScreenPos().X;
        ImGui.GetWindowDrawList().AddLine(new Vector2(wx, y), new Vector2(wx + contentWidth, y), Palette.Border.U32(), 1f);
        ImGui.Dummy(new Vector2(0f, 1f));
    }

    private void Helper(string text)
    {
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextWrapped(text);
    }

    // ---- server state (reused) ----

    private string DcName(int world)
    {
        var idx = this.DcIndexOf(world);
        var dcs = this.catalog.DataCenters;
        return idx >= 0 && idx < dcs.Count ? dcs[idx].Name : "—";
    }

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
