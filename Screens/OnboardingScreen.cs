using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Crypto;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Onboarding flow. Six steps with a progress header, a scrollable body, and Back and Continue
// navigation. State is local for now; it is persisted and sent to the server in phase C, and the
// sign in is a stub until the Discord flow lands. Step layout follows SCREENS section 1.
internal sealed class OnboardingScreen : IScreen
{
    private const int StepCount = 6;

    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly AuthService auth;
    private readonly KeyVault keyVault;
    private readonly WorldCatalog catalog;
    private readonly ProfileService profiles;
    private readonly PhotoManager photos;

    private int selectedDc = -1;
    private int selectedWorldId;

    private int step;
    private string passphrase = string.Empty;
    private bool revealPassphrase;
    private string displayName = string.Empty;
    private int pronoun;
    private int gender;
    private int age = 25;
    private readonly bool[] races = new bool[Options.Races.Length];
    private readonly bool[] tribes = new bool[Options.Tribes.Length];
    private readonly bool[] lookingFor = new bool[Options.LookingFor.Length];
    private bool nsfwEnabled;
    private int position = 2;
    private int role = 2;
    private int size = 2;
    private readonly bool[] meet = new bool[Options.Meet.Length];
    private readonly bool[] kinks = new bool[Options.Kinks.Length];
    private volatile bool finalizing;
    private string? saveError;

    public OnboardingScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, AuthService auth, KeyVault keyVault, WorldCatalog catalog, ProfileService profiles, PhotoManager photos)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.auth = auth;
        this.keyVault = keyVault;
        this.catalog = catalog;
        this.profiles = profiles;
        this.photos = photos;
    }

    public Screen Id => Screen.Onboarding;

    public bool Chrome => false;

    public void Draw()
    {
        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(16f);
        var contentWidth = avail.X - (pad * 2f);

        // The photo crop modal takes over the whole step (its own scroll/drag handling), so draw it
        // alone without the step header or nav.
        if (this.photos.IsCropping)
        {
            ImGui.SetCursorPos(new Vector2(pad, Ui.Px(14f)));
            this.photos.Draw(contentWidth);
            return;
        }

        var headerHeight = Ui.Px(52f);
        var navHeight = Ui.Px(60f);

        // Progress header: mono "STEP N OF 6" left, an "EIKON" wordmark right, segments below.
        ImGui.SetCursorPos(new Vector2(pad, Ui.Px(16f)));
        using (this.fonts.Eyebrow.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
            ImGui.TextUnformatted(Loc.T("onboarding.step", this.step + 1, StepCount));
        const string wordmark = "EIKON";
        var wordmarkWidth = Ui.Measure(this.fonts.Eyebrow, wordmark).X;
        ImGui.SetCursorPos(new Vector2(avail.X - pad - wordmarkWidth, Ui.Px(16f)));
        using (this.fonts.Eyebrow.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextUnformatted(wordmark);
        ImGui.SetCursorPos(new Vector2(pad, Ui.Px(40f)));
        this.kit.ProgressSegments(this.step + 1, StepCount, contentWidth);

        // Scrollable body.
        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("ob_content", new Vector2(avail.X, avail.Y - headerHeight - navHeight), false, ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            if (body.Success)
            {
                ImGui.Indent(pad);
                using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
                    this.DrawStep(contentWidth, avail.X);
                ImGui.Unindent(pad);
            }
        }

        // Navigation.
        ImGui.SetCursorPos(new Vector2(pad, avail.Y - navHeight + Ui.Px(12f)));
        this.DrawNav(contentWidth);
    }

    private void DrawStep(float contentWidth, float fullWidth)
    {
        switch (this.step)
        {
            case 0: this.DrawSignIn(fullWidth); break;
            case 1: this.DrawPassphrase(contentWidth); break;
            case 2: this.DrawIdentity(contentWidth); break;
            case 3: this.DrawVibe(contentWidth); break;
            case 4: this.DrawAfterDark(contentWidth); break;
            case 5: this.DrawPhotos(contentWidth); break;
        }
    }

    private void DrawSignIn(float fullWidth)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(48f)));
        this.kit.CenteredFramedIcon(fullWidth, FontAwesomeIcon.Magic.ToIconString(), Ui.Px(52f));
        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        Ui.CenteredText(fullWidth, this.fonts.Title, Palette.TextPrimary, Loc.T("onboarding.signIn.title"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        Ui.CenteredText(fullWidth, this.fonts.Caption, Palette.TextPrimary, Loc.T("onboarding.signIn.tagline"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        Ui.CenteredText(fullWidth, this.fonts.Caption, Palette.TextMuted, Loc.T("onboarding.signIn.discord"));

        if (this.auth.Phase != AuthPhase.LoggedOut)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
            var color = this.auth.Phase switch
            {
                AuthPhase.Failed => new Vector4(0.91f, 0.36f, 0.36f, 1f),
                AuthPhase.LoggedIn => this.theme.Accent,
                _ => Palette.TextSecondary,
            };
            Ui.CenteredText(fullWidth, this.fonts.Caption, color, this.auth.Message);

            // Fallback when the browser could not be launched automatically: let the user copy the
            // sign-in link and open it themselves. Polling keeps running, so the sign-in still completes.
            if (this.auth.Phase == AuthPhase.Authorizing && this.auth.AuthorizeUrl is { Length: > 0 } url &&
                this.auth.BrowserOpenFailed)
            {
                ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
                if (this.kit.SecondaryButton("##ob_copylink", Loc.T("onboarding.signIn.copyLink"), fullWidth))
                    ImGui.SetClipboardText(url);
            }
        }
    }

    private void DrawPassphrase(float contentWidth)
    {
        this.kit.SectionLabel(Loc.T("onboarding.passphrase.title"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.kit.PasswordField("##ob_pass", ref this.passphrase, contentWidth, ref this.revealPassphrase, Loc.T("onboarding.passphrase.hint"));

        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        var strength = Math.Min(this.passphrase.Length / 14f, 1f);
        this.kit.Meter(strength);
        ImGui.Dummy(new Vector2(0f, Ui.Px(5f)));
        var (label, color) = strength < 0.4f
            ? (Loc.T("onboarding.passphrase.weak"), Palette.TextMuted)
            : strength < 0.8f ? (Loc.T("onboarding.passphrase.okay"), this.theme.AccentText) : (Loc.T("onboarding.passphrase.strong"), this.theme.AccentText);
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(label);

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            Ui.TextWrapped(Loc.T("onboarding.passphrase.note"));
    }

    private void DrawIdentity(float contentWidth)
    {
        this.kit.SectionLabel(Loc.T("onboarding.identity.displayName"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        this.kit.TextField("##ob_name", ref this.displayName, Loc.T("onboarding.identity.displayName.hint"), contentWidth, Limits.DisplayNameMax);

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel(Loc.T("onboarding.identity.pronouns"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var p = this.kit.ChipFlow("ob_pn", Options.Labels(Options.Pronouns), i => i == this.pronoun, contentWidth, showCheck: false);
        if (p >= 0)
            this.pronoun = p;

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel(Loc.T("onboarding.identity.gender"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var g = this.kit.ChipFlow("ob_gn", Options.Labels(Options.Genders), i => i == this.gender, contentWidth, showCheck: false);
        if (g >= 0)
            this.gender = g;

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel(Loc.T("onboarding.identity.age"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        this.age = this.kit.Stepper("##ob_age", this.age, 18, 99);

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.DrawWorldPicker(contentWidth);
    }

    private void DrawWorldPicker(float contentWidth)
    {
        this.kit.SectionLabel(Loc.T("onboarding.identity.dataCenter"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        this.catalog.EnsureLoaded();
        if (!this.catalog.Ready)
        {
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
                ImGui.TextUnformatted(Loc.T("onboarding.identity.loadingWorlds"));
            return;
        }

        var dcs = this.catalog.DataCenters;
        var dc = this.kit.ChipFlow("ob_dc", dcs.Select(d => d.Name).ToArray(), i => i == this.selectedDc, contentWidth, showCheck: false);
        if (dc >= 0)
        {
            this.selectedDc = dc;
            this.selectedWorldId = 0;
        }

        if (this.selectedDc >= 0 && this.selectedDc < dcs.Count)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
            this.kit.SectionLabel(Loc.T("onboarding.identity.world"));
            ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
            var worlds = dcs[this.selectedDc].Worlds;
            var w = this.kit.ChipFlow("ob_world", worlds.Select(x => x.Name).ToArray(), i => worlds[i].Id == this.selectedWorldId, contentWidth, showCheck: false);
            if (w >= 0)
                this.selectedWorldId = worlds[w].Id;
        }
    }

    private void DrawVibe(float contentWidth)
    {
        this.kit.SectionLabel(Loc.T("onboarding.vibe.tribe", Limits.TribesMax));
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        this.Multi("ob_tribe", Options.Tribes, this.tribes, contentWidth, Limits.TribesMax);

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel(Loc.T("onboarding.vibe.race", Limits.RacesMax));
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        this.Multi("ob_race", Options.Races, this.races, contentWidth, Limits.RacesMax);
        if (!this.races.Any(r => r))
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
                ImGui.TextUnformatted(Loc.T("onboarding.vibe.raceRequired"));
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel(Loc.T("onboarding.vibe.lookingFor"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var l = this.kit.ChipFlow("ob_lf", Options.Labels(Options.LookingFor), i => this.lookingFor[i], contentWidth, showCheck: false);
        if (l >= 0)
            this.lookingFor[l] = !this.lookingFor[l];
    }

    private void DrawAfterDark(float contentWidth)
    {
        if (this.IsLalafell)
        {
            this.nsfwEnabled = false;
            using (this.fonts.SerifName.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
                ImGui.TextUnformatted(Loc.T("onboarding.afterDark.title"));
            ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
                Ui.TextWrapped(Loc.T("onboarding.afterDark.lalafell"));
            return;
        }

        using (this.fonts.SerifName.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
            ImGui.TextUnformatted(Loc.T("onboarding.afterDark.enable"));
        ImGui.SameLine(0f, Ui.Px(6f));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextUnformatted(Loc.T("onboarding.afterDark.ageTag"));
        ImGui.SameLine();
        ImGui.SetCursorPosX(Ui.Px(16f) + contentWidth - Ui.Px(36f));
        this.nsfwEnabled = this.kit.Toggle("##ob_nsfw", this.nsfwEnabled);

        ImGui.Dummy(new Vector2(0f, Ui.Px(4f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            Ui.TextWrapped(Loc.T("onboarding.afterDark.note"));

        if (!this.nsfwEnabled)
            return;

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.kit.SectionLabel(Loc.T("onboarding.afterDark.position"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var pos = this.kit.ChipFlow("ob_pos", Options.Labels(Options.Positions), i => i == this.position, contentWidth, showCheck: false);
        if (pos >= 0)
            this.position = pos;

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel(Loc.T("onboarding.afterDark.role"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var ro = this.kit.ChipFlow("ob_role", Options.Labels(Options.Roles), i => i == this.role, contentWidth, showCheck: false);
        if (ro >= 0)
            this.role = ro;

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel(Loc.T("onboarding.afterDark.size"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var sz = this.kit.ChipFlow("ob_size", Options.Labels(Options.Sizes), i => i == this.size, contentWidth, showCheck: false);
        if (sz >= 0)
            this.size = sz;

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel(Loc.T("onboarding.afterDark.meet"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var m = this.kit.ChipFlow("ob_meet", Options.Labels(Options.Meet), i => this.meet[i], contentWidth, showCheck: false);
        if (m >= 0)
            this.meet[m] = !this.meet[m];

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel(Loc.T("onboarding.afterDark.kinks"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var k = this.kit.ChipFlow("ob_kink", Options.Labels(Options.Kinks), i => this.kinks[i], contentWidth, showCheck: false);
        if (k >= 0)
            this.kinks[k] = !this.kinks[k];
    }

    private void DrawPhotos(float contentWidth)
    {
        this.kit.SectionLabel(Loc.T("onboarding.photos.title"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.photos.DrawGrid(contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextUnformatted(Loc.T("onboarding.photos.note"));

        if (this.saveError is { } error)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.Danger))
                Ui.TextWrapped(Loc.T(error));
        }
    }

    private void DrawNav(float contentWidth)
    {
        var backWidth = Ui.Px(84f);
        var gap = Ui.Px(8f);
        var forwardWidth = contentWidth - backWidth - gap;

        // Back is always present. On the sign-in step it returns to the age gate; otherwise it steps back.
        if (this.BackButton(backWidth))
        {
            if (this.step == 0)
                this.router.Navigate(Screen.AgeGuidelines);
            else
                this.step--;
        }
        ImGui.SameLine(0f, gap);

        if (this.step == 0)
        {
            switch (this.auth.Phase)
            {
                case AuthPhase.LoggedIn:
                    if (this.kit.PrimaryButton("##ob_continue", Loc.T("onboarding.nav.continue"), forwardWidth))
                        this.step = 1;
                    break;
                case AuthPhase.Authorizing:
                    if (this.kit.SecondaryButton("##ob_cancel", Loc.T("onboarding.nav.cancelSignIn"), forwardWidth))
                        this.auth.Cancel();
                    break;
                default:
                    if (this.kit.PrimaryButton("##ob_signin", Loc.T("onboarding.nav.discord"), forwardWidth))
                        this.auth.StartLogin();
                    break;
            }

            return;
        }

        var last = this.step == StepCount - 1;
        if (this.finalizing)
        {
            this.kit.SecondaryButton("##ob_forward_busy", Loc.T("onboarding.nav.saving"), forwardWidth);
        }
        else if (this.CanAdvance())
        {
            if (this.kit.PrimaryButton("##ob_forward", Loc.T(last ? (this.saveError is null ? "onboarding.nav.finalize" : "onboarding.nav.retry") : "onboarding.nav.continue"), forwardWidth))
            {
                if (last)
                    this.Complete();
                else
                    this.step++;
            }
        }
        else
        {
            this.kit.SecondaryButton("##ob_forward_off", Loc.T(last ? "onboarding.nav.finalize" : "onboarding.nav.continue"), forwardWidth);
        }
    }

    // Borderless back control: a thin drawn chevron (matching the title-bar's line glyphs) + "Back",
    // muted, brightening on hover.
    private bool BackButton(float width)
    {
        var height = Ui.Px(38f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##ob_back", new Vector2(width, height));
        var drawList = ImGui.GetWindowDrawList();
        var color = (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32();
        var cy = pos.Y + (height * 0.5f);

        var cx = pos.X + Ui.Px(4f);
        var r = Ui.Px(4f);
        drawList.AddLine(new Vector2(cx + r, cy - r), new Vector2(cx, cy), color, Ui.Px(1.5f));
        drawList.AddLine(new Vector2(cx, cy), new Vector2(cx + r, cy + r), color, Ui.Px(1.5f));

        var label = Loc.T("onboarding.nav.back");
        var labelSize = Ui.Measure(this.fonts.Label, label);
        Ui.TextAt(drawList, this.fonts.Label, new Vector2(cx + r + Ui.Px(9f), cy - (labelSize.Y * 0.5f)), color, label);
        return clicked;
    }

    // Create (or unlock) the local key vault from the passphrase and publish the public bundle,
    // then save the profile and enter the app. Key material never leaves the device; only public keys
    // are published. The app is only entered once the server has the profile: without that row the
    // member is invisible everywhere, so a failed save stays on this step with a retry.
    private void Complete()
    {
        if (this.finalizing)
            return;

        try
        {
            if (!this.keyVault.HasIdentity)
                this.keyVault.CreateIdentity(this.passphrase);
            else if (!this.keyVault.IsUnlocked)
                this.keyVault.Unlock(this.passphrase);

            if (this.keyVault.IsUnlocked)
                this.auth.PublishKeys(this.keyVault.PublicBundle());
        }
        catch
        {
            // Native crypto load is the M0 spike; if it fails we still enter the app (messaging is
            // unavailable until the vault initializes). Surfaced properly in C3.
        }

        this.finalizing = true;
        this.saveError = null;
        var request = this.BuildProfile();
        _ = Task.Run(async () =>
        {
            try
            {
                if (await this.profiles.SaveAsync(request, System.Threading.CancellationToken.None))
                    this.router.Navigate(Screen.Grid);
                else
                    this.saveError = "onboarding.saveFailed";
            }
            finally
            {
                this.finalizing = false;
            }
        });
    }

    // Shared multi-select with a cap: at the cap the unselected chips grey out and stop responding.
    private void Multi(string id, string[] labels, bool[] flags, float contentWidth, int max)
    {
        var atCap = flags.Count(f => f) >= max;
        var hit = this.kit.ChipFlow(id, Options.Labels(labels), i => flags[i], contentWidth, showCheck: false, disabled: atCap ? i => !flags[i] : null);
        if (hit >= 0 && (flags[hit] || !atCap))
            flags[hit] = !flags[hit];
    }

    private bool IsLalafell => this.races[ProfileMapper.IndexOfRace(RaceElement.Lalafell)];

    private SaveProfileRequest BuildProfile() => new()
    {
        DisplayName = this.displayName.Trim(),
        Pronoun = ProfileMapper.Pronoun(this.pronoun),
        Gender = ProfileMapper.Gender(this.gender),
        Age = this.age,
        Races = ProfileMapper.RacesOf(this.races),
        WorldId = this.selectedWorldId,
        Tribes = ProfileMapper.TribesOf(this.tribes),
        LookingFor = ProfileMapper.LookingForOf(this.lookingFor),
        Interests = new List<string>(),
        NsfwEnabled = this.nsfwEnabled && !this.IsLalafell,
        AfterDark = this.nsfwEnabled && !this.IsLalafell
            ? new AfterDarkDto
            {
                Position = ProfileMapper.Position(this.position),
                Role = ProfileMapper.Role(this.role),
                Size = ProfileMapper.Size(this.size),
                Meet = ProfileMapper.MeetOf(this.meet),
                Kinks = Options.Kinks.Where((_, i) => this.kinks[i]).ToList(),
            }
            : null,
    };

    private bool CanAdvance() => this.step switch
    {
        1 => this.passphrase.Trim().Length >= 8,
        2 => this.displayName.Trim().Length > 0 && this.selectedWorldId > 0,
        3 => this.races.Any(x => x),   // at least one race before leaving the vibe step
        _ => true,
    };
}
