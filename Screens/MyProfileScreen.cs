using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// My profile and its editors. The main view is a photo manager plus tappable rows; tapping a row
// swaps the body for the matching editor subview (text, long text, single select, stepper,
// multi-select chips, or the after-dark sub-screen). State is local and committed on Save;
// persistence and upload land in later phases.
internal sealed class MyProfileScreen : IScreen
{
    private enum Editor
    {
        None, DisplayName, Bio, Pronouns, Gender, Age, Race, World, LookingFor, Into, Tribe, AfterDark,
    }

    private readonly ScreenRouter router;
    private readonly ThemeService theme;
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
    private int editDc = -1;
    private int editWorldId;

    // Profile state (mock defaults).
    private string displayName = "Akos";
    private string bio = "Balmung. Raid nerd by day, softer after dark. Say hi.";
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

    // Editor scratch (so Cancel discards).
    private Editor editor = Editor.None;
    private float listScrollY;          // profile list scroll, captured on open so we can restore it on back
    private bool restoreListScroll;
    private string editText = string.Empty;
    private bool bioWrapped;
    private int editSingle;
    private string editCustom = string.Empty;
    private int editAge;
    private bool[] editMulti = System.Array.Empty<bool>();
    private bool adNsfw;
    private int adPos, adRole, adSize;
    private bool[] adMeet = System.Array.Empty<bool>();
    private bool[] adKinks = System.Array.Empty<bool>();

    public MyProfileScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, Lightbox lightbox, ProfileService profiles, WorldCatalog catalog, PhotoManager photos, Selection selection, SessionStore session, ProfileDetailService details)
    {
        this.router = router;
        this.theme = theme;
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
        var contentWidth = ImGui.GetContentRegionAvail().X - Ui.Px(16f);
        if (this.photos.IsCropping)
        {
            this.photos.Draw(contentWidth);
            return;
        }

        if (this.editor != Editor.None)
        {
            this.DrawEditor(contentWidth);
            return;
        }

        // Returning from an editor: jump back to where the list was, not the top.
        if (this.restoreListScroll)
        {
            ImGui.SetScrollY(this.listScrollY);
            this.restoreListScroll = false;
        }

        this.profiles.EnsureLoaded();
        this.catalog.EnsureLoaded();
        if (!this.applied && this.profiles.Mine is { } mine)
        {
            this.ApplyFromServer(mine);
            this.applied = true;
        }

        this.DrawPreviewButton(contentWidth);
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.kit.SectionLabel("Photos");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.photos.DrawGrid(contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        this.kit.SectionLabel("About you");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        if (this.SettingRow("##r_name", "Display name", this.displayName, contentWidth)) this.Open(Editor.DisplayName);
        if (this.SettingRow("##r_pn", "Pronouns", this.PronounValue(), contentWidth)) this.Open(Editor.Pronouns);
        if (this.SettingRow("##r_gn", "Gender", this.GenderValue(), contentWidth)) this.Open(Editor.Gender);
        if (this.SettingRow("##r_age", "Age", this.age.ToString(), contentWidth)) this.Open(Editor.Age);
        if (this.SettingRow("##r_race", "Race", this.RaceValue(), contentWidth)) this.Open(Editor.Race);
        if (this.SettingRow("##r_world", "Home world", this.WorldValue(), contentWidth)) this.Open(Editor.World);
        if (this.SettingRow("##r_bio", "Bio", this.bio.Length > 0 ? this.bio : "Add", contentWidth)) this.Open(Editor.Bio);

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        this.kit.SectionLabel("Matching");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        if (this.SettingRow("##r_lf", "Looking for", Summary(Options.LookingFor, this.lookingFor), contentWidth)) this.Open(Editor.LookingFor);
        if (this.SettingRow("##r_into", "Into", Summary(Options.Interests, this.interests), contentWidth)) this.Open(Editor.Into);
        if (this.SettingRow("##r_tribe", "Tribe", Summary(Options.Tribes, this.tribes), contentWidth)) this.Open(Editor.Tribe);

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        this.kit.SectionLabel("After dark");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        if (this.SettingRow("##r_ad", "After dark", this.nsfwEnabled ? this.AfterDarkSummary() : "Off", contentWidth)) this.Open(Editor.AfterDark);

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        this.lightbox.Draw();
    }

    // ---- main view pieces ----

    private void DrawPreviewButton(float contentWidth)
    {
        var width = Ui.Px(110f);
        var start = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(start + contentWidth - width);
        // Open profile detail in self-view ("see as others"). Needs the selection set to our own id;
        // invalidate the detail cache so edits made this session are reflected.
        if (this.kit.SecondaryButton("##preview", "Preview", width) && this.session.UserId is { } me)
        {
            this.selection.ProfileUserId = me;
            this.selection.ProfileDisplayName = this.displayName;
            this.details.Invalidate();
            this.router.Navigate(Screen.ProfileDetail);
        }
    }

    private bool SettingRow(string id, string label, string value, float contentWidth)
    {
        var rowHeight = Ui.Px(50f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(contentWidth, rowHeight));
        var drawList = ImGui.GetWindowDrawList();

        var labelSize = Ui.Measure(this.fonts.Body, label);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(pos.X, pos.Y + ((rowHeight - labelSize.Y) * 0.5f)), Palette.TextPrimary.U32(), label);

        var chevron = FontAwesomeIcon.ChevronRight.ToIconString();
        var chevronSize = Ui.Measure(this.fonts.Icon, chevron);
        var chevronX = pos.X + contentWidth - chevronSize.X;
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(chevronX, pos.Y + ((rowHeight - chevronSize.Y) * 0.5f)), Palette.TextMuted.U32(), chevron);

        var maxValueWidth = MathF.Max(Ui.Px(40f), contentWidth - labelSize.X - Ui.Px(16f) - chevronSize.X - Ui.Px(10f));
        var shown = this.Fit(value, maxValueWidth);
        var valueSize = Ui.Measure(this.fonts.Body, shown);
        var valueColor = (value == "Add" ? this.theme.Accent : Palette.TextSecondary).U32();
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(chevronX - Ui.Px(10f) - valueSize.X, pos.Y + ((rowHeight - valueSize.Y) * 0.5f)), valueColor, shown);

        drawList.AddLine(new Vector2(pos.X, pos.Y + rowHeight), new Vector2(pos.X + contentWidth, pos.Y + rowHeight), Palette.Border.U32(), 1f);
        return clicked;
    }

    private string Fit(string text, float maxWidth)
    {
        if (Ui.Measure(this.fonts.Body, text).X <= maxWidth)
            return text;
        var s = text;
        while (s.Length > 1 && Ui.Measure(this.fonts.Body, s + "…").X > maxWidth)
            s = s[..^1];
        return s + "…";
    }

    // ---- editor ----

    private void DrawEditor(float contentWidth)
    {
        var rowStart = ImGui.GetCursorPosX();
        var buttonWidth = Ui.Px(96f);
        if (this.kit.SecondaryButton("##ed_cancel", "Cancel", buttonWidth))
        {
            this.editor = Editor.None;
            this.restoreListScroll = true;
            return;
        }

        ImGui.SameLine();
        ImGui.SetCursorPosX(rowStart + contentWidth - buttonWidth);
        if (this.kit.PrimaryButton("##ed_save", "Save", buttonWidth))
        {
            this.Save();
            return;
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.kit.SectionLabel(this.EditorTitle());
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));

        switch (this.editor)
        {
            case Editor.DisplayName:
                this.kit.TextField("##ed_name", ref this.editText, "Display name", contentWidth);
                ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
                this.Helper($"{this.editText.Length}/20  Shown to others. Not your character name.");
                break;
            case Editor.Bio:
            {
                // InputTextMultiline does not word-wrap, so we keep a wrapped view in the field and
                // collapse the soft wraps back to spaces on save (see Save).
                var wrapWidth = contentWidth - Ui.Px(24f);
                if (!this.bioWrapped)
                {
                    this.editText = this.WordWrap(this.editText, wrapWidth);
                    this.bioWrapped = true;
                }

                bool changed;
                using (ImRaii.PushColor(ImGuiCol.FrameBg, Palette.Surface1))
                using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
                using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, Ui.Px(10f)))
                using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(Ui.Px(12f), Ui.Px(10f))))
                using (this.fonts.Body.Push())
                    changed = ImGui.InputTextMultiline("##ed_bio", ref this.editText, 360, new Vector2(contentWidth, Ui.Px(130f)));

                if (changed)
                    this.editText = this.WordWrap(this.editText, wrapWidth);

                ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
                this.Helper($"{CollapseNewlines(this.editText).Length}/300  Shown on your profile.");
                break;
            }
            case Editor.Pronouns:
                this.DrawSingle("ed_pn", Options.Pronouns, contentWidth);
                this.DrawCustom(contentWidth);
                break;
            case Editor.Gender:
                this.DrawSingle("ed_gn", Options.Genders, contentWidth);
                this.DrawCustom(contentWidth);
                break;
            case Editor.Race:
                this.DrawMulti("ed_race", Options.Races, contentWidth);
                break;
            case Editor.World:
                this.DrawWorldEditor(contentWidth);
                break;
            case Editor.Age:
                this.editAge = this.kit.Stepper("##ed_age", this.editAge, 18, 99);
                break;
            case Editor.LookingFor:
                this.DrawMulti("ed_lf", Options.LookingFor, contentWidth);
                break;
            case Editor.Into:
                this.DrawMulti("ed_into", Options.Interests, contentWidth);
                break;
            case Editor.Tribe:
                this.DrawMulti("ed_tribe", Options.Tribes, contentWidth);
                break;
            case Editor.AfterDark:
                this.DrawAfterDarkEditor(contentWidth);
                break;
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
    }

    private void DrawSingle(string idPrefix, string[] labels, float contentWidth)
    {
        var selected = this.kit.ChipFlow(idPrefix, labels, i => i == this.editSingle, contentWidth);
        if (selected >= 0)
            this.editSingle = selected;
    }

    private void DrawCustom(float contentWidth)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel("Custom (optional)");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        this.kit.TextField("##ed_custom", ref this.editCustom, "Your own words", contentWidth);
    }

    private void DrawMulti(string idPrefix, string[] labels, float contentWidth)
    {
        var count = 0;
        foreach (var on in this.editMulti)
            if (on) count++;
        this.Helper($"{count} selected  Pick all that apply.");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        var hit = this.kit.ChipFlow(idPrefix, labels, i => this.editMulti[i], contentWidth);
        if (hit >= 0)
            this.editMulti[hit] = !this.editMulti[hit];
    }

    private void DrawAfterDarkEditor(float contentWidth)
    {
        using (this.fonts.Body.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
            ImGui.TextUnformatted("Enable after dark (18+)");
        ImGui.SameLine(0f, Ui.Px(10f));
        this.adNsfw = this.kit.Toggle("##ad_en", this.adNsfw);
        ImGui.Dummy(new Vector2(0f, Ui.Px(4f)));
        this.Helper("Off removes the after-dark section from your profile.");

        if (!this.adNsfw)
            return;

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel("Position");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var p = this.kit.ChipFlow("ad_pos", Options.Positions, i => i == this.adPos, contentWidth);
        if (p >= 0) this.adPos = p;

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel("Role");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var r = this.kit.ChipFlow("ad_role", Options.Roles, i => i == this.adRole, contentWidth);
        if (r >= 0) this.adRole = r;

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel("Size (optional)");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var s = this.kit.ChipFlow("ad_size", Options.Sizes, i => i == this.adSize, contentWidth);
        if (s >= 0) this.adSize = s;

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel("Meet");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var m = this.kit.ChipFlow("ad_meet", Options.Meet, i => this.adMeet[i], contentWidth);
        if (m >= 0) this.adMeet[m] = !this.adMeet[m];

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel("Kinks");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var k = this.kit.ChipFlow("ad_kink", Options.Kinks, i => this.adKinks[i], contentWidth);
        if (k >= 0) this.adKinks[k] = !this.adKinks[k];
    }

    private void Helper(string text)
    {
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextWrapped(text);
    }

    // ---- state helpers ----

    private void Open(Editor target)
    {
        // Remember where we were in the list so closing the editor returns there, not the top.
        this.listScrollY = ImGui.GetScrollY();
        this.editor = target;
        switch (target)
        {
            case Editor.DisplayName: this.editText = this.displayName; break;
            case Editor.Bio: this.editText = this.bio; this.bioWrapped = false; break;
            case Editor.Pronouns: this.editSingle = this.pronoun; this.editCustom = this.pronounCustom; break;
            case Editor.Gender: this.editSingle = this.gender; this.editCustom = this.genderCustom; break;
            case Editor.Race: this.editMulti = (bool[])this.races.Clone(); break;
            case Editor.World:
                this.catalog.EnsureLoaded();
                this.editWorldId = this.worldId;
                this.editDc = this.DcIndexOf(this.worldId);
                break;
            case Editor.Age: this.editAge = this.age; break;
            case Editor.LookingFor: this.editMulti = (bool[])this.lookingFor.Clone(); break;
            case Editor.Into: this.editMulti = (bool[])this.interests.Clone(); break;
            case Editor.Tribe: this.editMulti = (bool[])this.tribes.Clone(); break;
            case Editor.AfterDark:
                this.adNsfw = this.nsfwEnabled;
                this.adPos = this.position; this.adRole = this.roleIndex; this.adSize = this.size;
                this.adMeet = (bool[])this.meet.Clone();
                this.adKinks = (bool[])this.kinks.Clone();
                break;
        }
    }

    private void Save()
    {
        switch (this.editor)
        {
            case Editor.DisplayName: this.displayName = this.editText.Trim(); break;
            case Editor.Bio: this.bio = CollapseNewlines(this.editText).Trim(); break;
            case Editor.Pronouns: this.pronoun = this.editSingle; this.pronounCustom = this.editCustom.Trim(); break;
            case Editor.Gender: this.gender = this.editSingle; this.genderCustom = this.editCustom.Trim(); break;
            case Editor.Race: System.Array.Copy(this.editMulti, this.races, this.races.Length); break;
            case Editor.World: this.worldId = this.editWorldId; break;
            case Editor.Age: this.age = this.editAge; break;
            case Editor.LookingFor: System.Array.Copy(this.editMulti, this.lookingFor, this.lookingFor.Length); break;
            case Editor.Into: System.Array.Copy(this.editMulti, this.interests, this.interests.Length); break;
            case Editor.Tribe: System.Array.Copy(this.editMulti, this.tribes, this.tribes.Length); break;
            case Editor.AfterDark:
                this.nsfwEnabled = this.adNsfw;
                this.position = this.adPos; this.roleIndex = this.adRole; this.size = this.adSize;
                System.Array.Copy(this.adMeet, this.meet, this.meet.Length);
                System.Array.Copy(this.adKinks, this.kinks, this.kinks.Length);
                break;
        }

        this.PushToServer();
        this.editor = Editor.None;
        this.restoreListScroll = true;
    }

    private string EditorTitle() => this.editor switch
    {
        Editor.DisplayName => "Display name",
        Editor.Bio => "Bio",
        Editor.Pronouns => "Pronouns",
        Editor.Gender => "Gender",
        Editor.Race => "Race",
        Editor.World => "Home world",
        Editor.Age => "Age",
        Editor.LookingFor => "Looking for",
        Editor.Into => "Into",
        Editor.Tribe => "Tribe",
        Editor.AfterDark => "After dark",
        _ => string.Empty,
    };

    private string PronounValue() => this.pronounCustom.Length > 0 ? this.pronounCustom : Options.Pronouns[this.pronoun];

    private string GenderValue() => this.genderCustom.Length > 0 ? this.genderCustom : Options.Genders[this.gender];

    private string RaceValue()
    {
        var sel = new List<string>();
        for (var i = 0; i < this.races.Length; i++)
            if (this.races[i]) sel.Add(Options.Races[i]);
        return sel.Count > 0 ? string.Join(", ", sel) : "Add";
    }

    private string AfterDarkSummary() => $"{Options.Positions[this.position]} · {Options.Roles[this.roleIndex]}";

    private string WorldValue() => this.worldId > 0 ? this.catalog.WorldName(this.worldId) : "Add";

    private int DcIndexOf(int world)
    {
        var dcs = this.catalog.DataCenters;
        for (var i = 0; i < dcs.Count; i++)
            foreach (var w in dcs[i].Worlds)
                if (w.Id == world)
                    return i;
        return -1;
    }

    private void DrawWorldEditor(float contentWidth)
    {
        this.catalog.EnsureLoaded();
        if (!this.catalog.Ready)
        {
            this.Helper("Loading worlds...");
            return;
        }

        var dcs = this.catalog.DataCenters;
        var dc = this.kit.ChipFlow("ed_dc", dcs.Select(d => d.Name).ToArray(), i => i == this.editDc, contentWidth);
        if (dc >= 0)
        {
            this.editDc = dc;
            this.editWorldId = 0;
        }

        if (this.editDc >= 0 && this.editDc < dcs.Count)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            var worlds = dcs[this.editDc].Worlds;
            var w = this.kit.ChipFlow("ed_world", worlds.Select(x => x.Name).ToArray(), i => worlds[i].Id == this.editWorldId, contentWidth);
            if (w >= 0)
                this.editWorldId = worlds[w].Id;
        }
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

    private void PushToServer() => this.profiles.Save(this.BuildRequest());

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
            ? new SaveProfileRequestAfterDark
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

    // Greedy word wrap to a pixel width using the body font. Idempotent: it first flattens any
    // existing soft wraps, so it can run on every edit without corrupting the text.
    private string WordWrap(string text, float maxWidth)
    {
        var flat = CollapseNewlines(text);
        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in flat.Split(' '))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (Ui.Measure(this.fonts.Body, candidate).X <= maxWidth)
            {
                line = candidate;
                continue;
            }

            if (line.Length > 0)
                lines.Add(line);
            line = word;
        }

        lines.Add(line);
        return string.Join("\n", lines);
    }

    private static string CollapseNewlines(string text) =>
        text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

    private static string Summary(string[] labels, bool[] flags)
    {
        var picked = new List<string>();
        for (var i = 0; i < flags.Length; i++)
            if (flags[i]) picked.Add(labels[i]);
        if (picked.Count == 0)
            return "Add";
        if (picked.Count <= 2)
            return string.Join(", ", picked);
        return $"{picked[0]}, {picked[1]} +{picked.Count - 2}";
    }
}
