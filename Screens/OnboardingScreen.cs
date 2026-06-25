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

        // Progress header.
        ImGui.SetCursorPos(new Vector2(pad, Ui.Px(14f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextUnformatted($"Step {this.step + 1} of {StepCount}");
        ImGui.SetCursorPos(new Vector2(pad, Ui.Px(36f)));
        this.kit.ProgressSegments(this.step + 1, StepCount, contentWidth);

        // Scrollable body.
        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("ob_content", new Vector2(avail.X, avail.Y - headerHeight - navHeight)))
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
        var coreBox = Ui.Px(56f);
        var coreOrigin = ImGui.GetCursorScreenPos();
        Ui.AetherCore(ImGui.GetWindowDrawList(),
            new Vector2(coreOrigin.X + (fullWidth * 0.5f), coreOrigin.Y + (coreBox * 0.5f)), coreBox,
            Palette.WithAlpha(this.theme.Accent, 0.7f).U32(), this.theme.Accent.U32());
        ImGui.Dummy(new Vector2(fullWidth, coreBox));
        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        Ui.CenteredText(fullWidth, this.fonts.Title, Palette.TextPrimary, "Welcome to Eikon");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        Ui.CenteredText(fullWidth, this.fonts.Caption, Palette.TextSecondary, "18+ dating & social for FFXIV");
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        Ui.CenteredText(fullWidth, this.fonts.Caption, Palette.TextMuted, "We use Discord to sign in. We never see your password.");

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
                this.auth.Message.StartsWith("Couldn't", StringComparison.Ordinal))
            {
                ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
                if (this.kit.SecondaryButton("##ob_copylink", "Copy sign-in link", fullWidth))
                    ImGui.SetClipboardText(url);
            }
        }
    }

    private void DrawPassphrase(float contentWidth)
    {
        this.kit.SectionLabel("Set a passphrase");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.kit.PasswordField("##ob_pass", ref this.passphrase, contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        var strength = Math.Min(this.passphrase.Length / 14f, 1f);
        this.kit.Meter(strength);
        ImGui.Dummy(new Vector2(0f, Ui.Px(5f)));
        var (label, color) = strength < 0.4f
            ? ("Weak", Palette.TextMuted)
            : strength < 0.8f ? ("Okay", this.theme.AccentText) : ("Strong", this.theme.AccentText);
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(label);

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextWrapped("Only you can unlock your chats. We can't reset it, so keep it safe.");
    }

    private void DrawIdentity(float contentWidth)
    {
        this.kit.TextField("##ob_name", ref this.displayName, "Display name", contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel("Pronouns");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var p = this.kit.ChipFlow("ob_pn", Options.Pronouns, i => i == this.pronoun, contentWidth);
        if (p >= 0)
            this.pronoun = p;

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel("Gender");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var g = this.kit.ChipFlow("ob_gn", Options.Genders, i => i == this.gender, contentWidth);
        if (g >= 0)
            this.gender = g;

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel("Age");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        this.age = this.kit.Stepper("##ob_age", this.age, 18, 99);

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.DrawWorldPicker(contentWidth);
    }

    private void DrawWorldPicker(float contentWidth)
    {
        this.kit.SectionLabel("Home world");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        this.catalog.EnsureLoaded();
        if (!this.catalog.Ready)
        {
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
                ImGui.TextUnformatted("Loading worlds...");
            return;
        }

        var dcs = this.catalog.DataCenters;
        var dc = this.kit.ChipFlow("ob_dc", dcs.Select(d => d.Name).ToArray(), i => i == this.selectedDc, contentWidth);
        if (dc >= 0)
        {
            this.selectedDc = dc;
            this.selectedWorldId = 0;
        }

        if (this.selectedDc >= 0 && this.selectedDc < dcs.Count)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            var worlds = dcs[this.selectedDc].Worlds;
            var w = this.kit.ChipFlow("ob_world", worlds.Select(x => x.Name).ToArray(), i => worlds[i].Id == this.selectedWorldId, contentWidth);
            if (w >= 0)
                this.selectedWorldId = worlds[w].Id;
        }
    }

    private void DrawVibe(float contentWidth)
    {
        this.kit.SectionLabel("Body / tribe");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var t = this.kit.ChipFlow("ob_tribe", Options.Tribes, i => this.tribes[i], contentWidth);
        if (t >= 0)
            this.tribes[t] = !this.tribes[t];

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel("Race");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var r = this.kit.ChipFlow("ob_race", Options.Races, i => this.races[i], contentWidth);
        if (r >= 0)
            this.races[r] = !this.races[r];

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel("Looking for");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var l = this.kit.ChipFlow("ob_lf", Options.LookingFor, i => this.lookingFor[i], contentWidth);
        if (l >= 0)
            this.lookingFor[l] = !this.lookingFor[l];
    }

    private void DrawAfterDark(float contentWidth)
    {
        using (this.fonts.Body.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
            ImGui.TextUnformatted("Enable after dark (18+)");
        ImGui.SameLine(0f, Ui.Px(10f));
        this.nsfwEnabled = this.kit.Toggle("##ob_nsfw", this.nsfwEnabled);

        ImGui.Dummy(new Vector2(0f, Ui.Px(4f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextWrapped("Only shown to people who have NSFW turned on.");

        if (!this.nsfwEnabled)
            return;

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.kit.SectionLabel("Position");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var pos = this.kit.ChipFlow("ob_pos", Options.Positions, i => i == this.position, contentWidth);
        if (pos >= 0)
            this.position = pos;

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel("Role");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var ro = this.kit.ChipFlow("ob_role", Options.Roles, i => i == this.role, contentWidth);
        if (ro >= 0)
            this.role = ro;

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel("Size (optional)");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var sz = this.kit.ChipFlow("ob_size", Options.Sizes, i => i == this.size, contentWidth);
        if (sz >= 0)
            this.size = sz;

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel("Meet");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var m = this.kit.ChipFlow("ob_meet", Options.Meet, i => this.meet[i], contentWidth);
        if (m >= 0)
            this.meet[m] = !this.meet[m];

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.kit.SectionLabel("Kinks");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var k = this.kit.ChipFlow("ob_kink", Options.Kinks, i => this.kinks[i], contentWidth);
        if (k >= 0)
            this.kinks[k] = !this.kinks[k];
    }

    private void DrawPhotos(float contentWidth)
    {
        this.kit.SectionLabel("Add photos");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.photos.DrawGrid(contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextUnformatted("Up to 6, reviewed before they go live.");
    }

    private void DrawNav(float contentWidth)
    {
        if (this.step == 0)
        {
            switch (this.auth.Phase)
            {
                case AuthPhase.LoggedIn:
                    if (this.kit.PrimaryButton("##ob_continue", "Continue", contentWidth))
                        this.step = 1;
                    break;
                case AuthPhase.Authorizing:
                    if (this.kit.SecondaryButton("##ob_cancel", "Cancel sign-in", contentWidth))
                        this.auth.Cancel();
                    break;
                default:
                    if (this.kit.PrimaryButton("##ob_signin", "Continue with Discord", contentWidth))
                        this.auth.StartLogin();
                    break;
            }

            return;
        }

        var backWidth = Ui.Px(84f);
        var gap = Ui.Px(8f);
        if (this.kit.SecondaryButton("##ob_back", "Back", backWidth))
            this.step--;

        ImGui.SameLine(0f, gap);
        var forwardWidth = contentWidth - backWidth - gap;
        var last = this.step == StepCount - 1;
        if (this.CanAdvance())
        {
            if (this.kit.PrimaryButton("##ob_forward", last ? "Finalize" : "Continue", forwardWidth))
            {
                if (last)
                    this.Finalize();
                else
                    this.step++;
            }
        }
        else
        {
            this.kit.SecondaryButton("##ob_forward_off", last ? "Finalize" : "Continue", forwardWidth);
        }
    }

    // Create (or unlock) the local key vault from the passphrase and publish the public bundle,
    // then enter the app. Key material never leaves the device; only public keys are published.
    private void Finalize()
    {
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

        this.profiles.Save(this.BuildProfile());
        this.router.Navigate(Screen.Grid);
    }

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
        NsfwEnabled = this.nsfwEnabled,
        AfterDark = this.nsfwEnabled
            ? new SaveProfileRequestAfterDark
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
