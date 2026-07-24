using System.Linq;
using System.Threading;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin.Services;
using Eikon.Config;
using Eikon.Contracts;
using Eikon.Content;
using Eikon.Crypto;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.Notifications;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Settings. The headline is the live accent picker: tapping a swatch recolors the whole app
// immediately and persists. Below it is the grouped settings list from SCREENS section 19. The
// privacy toggles persist server-side (loaded from /auth/me, saved to /api/settings). Destructive
// rows use a red label.
internal sealed class SettingsScreen : IScreen
{
    private static readonly Vector4 Danger = new(0.91f, 0.36f, 0.36f, 1f);

    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly AuthService auth;
    private readonly KeyVault keyVault;
    private readonly IApiClient api;
    private readonly Configuration config;
    private readonly SoundService sound;
    private readonly IPluginLog log;
    private readonly DeleteAccountFlow deleteFlow;
    private readonly ProfileService profiles;
    private readonly PhotoService photoSvc;
    private readonly WorldCatalog catalog;

    private bool discreet;
    private bool onlyVerifiedMessage;
    private bool settingsLoaded;   // privacy prefs fetched from the server this session
    private int textStep;          // live Text size slider step; committed to config (and applied) on release
    private bool inDataCenter;     // the Data center picker sub-view is open over the main list
    private int pickerDc = -1;     // selected data center in the picker (its world list expands below)

    public SettingsScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, AuthService auth, KeyVault keyVault, IApiClient api, Configuration config, SoundService sound, IPluginLog log, DeleteAccountFlow deleteFlow, ProfileService profiles, PhotoService photoSvc, WorldCatalog catalog)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.auth = auth;
        this.keyVault = keyVault;
        this.api = api;
        this.config = config;
        this.sound = sound;
        this.log = log;
        this.deleteFlow = deleteFlow;
        this.profiles = profiles;
        this.photoSvc = photoSvc;
        this.catalog = catalog;
        this.textStep = TextScale.NearestStepIndex(config.TextScalePercent);
    }

    public Screen Id => Screen.Settings;

    public bool Chrome => true;

    public void Draw()
    {
        var pad = Ui.Px(16f);
        var contentWidth = ImGui.GetContentRegionAvail().X - (pad * 2f);
        ImGui.Indent(pad);

        if (this.inDataCenter)
        {
            this.DrawDataCenterPicker(contentWidth);
            ImGui.Unindent(pad);
            this.deleteFlow.Draw();
            return;
        }

        this.DrawHeader(contentWidth);

        this.kit.SectionLabel("Appearance");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        if (this.DrawThemeRow("##s_appearance", contentWidth))
            this.router.Navigate(Screen.Appearance);

        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        this.kit.SectionLabel("Browse layout");
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.DrawLayoutCards(contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        this.kit.SectionLabel("Text size");
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.DrawTextSize(contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        this.kit.SectionLabel("Account");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        this.DrawAccountRow(contentWidth);
        this.NavRow("##s_discord", "Discord", "Connected", Palette.TextPrimary, true, contentWidth);

        var verifyValue = this.auth.IsVerified
            ? "Verified"
            : this.auth.VerifyState is VerifyPhase.Authorizing or VerifyPhase.Failed ? this.auth.VerifyMessage : "Verify";
        if (this.NavRow("##s_verify", "Verified character", verifyValue, Palette.TextPrimary, !this.auth.IsVerified, contentWidth)
            && !this.auth.IsVerified
            && this.auth.VerifyState != VerifyPhase.Authorizing)
        {
            this.auth.StartVerify();
        }
        if (this.NavRow("##s_logout", "Log out", string.Empty, Palette.TextPrimary, true, contentWidth))
            this.router.Navigate(Screen.AgeGuidelines);
        if (this.NavRow("##s_delete", "Delete account", string.Empty, Danger, true, contentWidth))
            this.deleteFlow.Open();

        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        this.kit.SectionLabel("Privacy");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        this.EnsureSettingsLoaded();

        var nextDiscreet = this.ToggleRow("##s_discreet", "Discreet mode", this.discreet, contentWidth);
        if (nextDiscreet != this.discreet)
        {
            this.discreet = nextDiscreet;
            this.SaveSettings();
        }
        this.Hint("Hides you from the grid; people you already talk to can still reach you.", contentWidth);

        var nextOnlyVerified = this.ToggleRow("##s_onlyverif", "Only verified can message me", this.onlyVerifiedMessage, contentWidth);
        if (nextOnlyVerified != this.onlyVerifiedMessage)
        {
            this.onlyVerifiedMessage = nextOnlyVerified;
            this.SaveSettings();
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        this.kit.SectionLabel("Notifications");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));

        var nextNotif = this.ToggleRow("##s_notif", "Notifications", this.config.NotificationsEnabled, contentWidth);
        if (nextNotif != this.config.NotificationsEnabled)
        {
            this.config.NotificationsEnabled = nextNotif;
            this.config.Save();
        }

        var nextSound = this.ToggleRow("##s_notifsound", "Notification sounds", this.config.NotificationSoundEnabled, contentWidth);
        if (nextSound != this.config.NotificationSoundEnabled)
        {
            this.config.NotificationSoundEnabled = nextSound;
            this.config.Save();
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.EyebrowValue("Sound volume", $"{this.config.NotificationVolume}%", contentWidth);
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        var volWidth = contentWidth - Ui.Px(78f);
        var nextVolume = this.kit.Slider("##s_notifvol", this.config.NotificationVolume, 0, 100, volWidth);
        if (nextVolume != this.config.NotificationVolume)
        {
            this.config.NotificationVolume = nextVolume;
            this.config.Save();
        }
        ImGui.SameLine(0f, Ui.Px(10f));
        if (this.kit.SecondaryButton("##s_notiftest", "Test", Ui.Px(68f)))
            this.sound.Play(this.config.NotificationVolume);

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.kit.SectionLabel("Where they appear");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.DrawNotifPosition(contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        this.kit.SectionLabel("Connection");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        if (this.DrawDataCenterRow(contentWidth))
            this.OpenDataCenterPicker();

        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        this.kit.SectionLabel("Content & safety");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        // After dark / NSFW is profile state (set in your profile, filtered in discovery), not a
        // device setting, so it isn't duplicated here.
        if (this.NavRow("##s_blocked", "Blocked users", string.Empty, Palette.TextPrimary, true, contentWidth))
            this.router.Navigate(Screen.Blocked);

        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        this.kit.SectionLabel("Security");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var requirePassphrase = !this.keyVault.AutoUnlockEnabled;
        var nextRequire = this.ToggleRow("##s_passlaunch", "Require passphrase on this PC", requirePassphrase, contentWidth);
        if (nextRequire != requirePassphrase && this.keyVault.IsUnlocked)
            this.keyVault.SetAutoUnlock(!nextRequire);
        this.Hint("Off unlocks automatically for your Windows account. On asks for your passphrase every launch - enable if others can use this PC under your login.", contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        this.kit.SectionLabel("About");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        if (this.NavRow("##s_guidelines", "Community Guidelines", string.Empty, Palette.TextPrimary, true, contentWidth))
            this.router.Navigate(Screen.Guidelines);
        if (this.NavRow("##s_whatsnew", "What's new", string.Empty, Palette.TextPrimary, true, contentWidth))
            this.router.Navigate(Screen.WhatsNew);
        this.NavRow("##s_version", "Version", PluginVersion.Display, Palette.TextSecondary, false, contentWidth);

        this.DrawFooter(contentWidth);

        ImGui.Unindent(pad);
        this.deleteFlow.Draw();
    }

    // ---- header / account / connection / footer ----

    private void DrawHeader(float fullWidth)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var eyebrowH = Ui.Measure(this.fonts.Eyebrow, "X").Y;
        Ui.TextAt(dl, this.fonts.Eyebrow, origin, Palette.TextSecondary.U32(), "PREFERENCES");
        var titleY = origin.Y + eyebrowH + Ui.Px(4f);
        Ui.TextAt(dl, this.fonts.SerifTitle, new Vector2(origin.X, titleY), Palette.TextPrimary.U32(), "Settings");
        var titleH = Ui.Measure(this.fonts.SerifTitle, "Settings").Y;
        var ruleY = titleY + titleH + Ui.Px(16f);
        dl.AddLine(new Vector2(origin.X, ruleY), new Vector2(origin.X + fullWidth, ruleY), Palette.Border.U32(), 1f);
        ImGui.Dummy(new Vector2(fullWidth, (ruleY - origin.Y) + Ui.Px(16f)));
    }

    private bool DrawThemeRow(string id, float contentWidth)
    {
        var rowHeight = Ui.Px(50f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(contentWidth, rowHeight));
        var dl = ImGui.GetWindowDrawList();

        var labelSize = Ui.Measure(this.fonts.Body, "Theme");
        Ui.TextAt(dl, this.fonts.Body, new Vector2(pos.X, pos.Y + ((rowHeight - labelSize.Y) * 0.5f)), Palette.TextPrimary.U32(), "Theme");

        var chev = FontAwesomeIcon.ChevronRight.ToIconString();
        var chSize = Ui.Measure(this.fonts.Icon, chev);
        var rightX = pos.X + contentWidth - chSize.X;
        Ui.TextAt(dl, this.fonts.Icon, new Vector2(rightX, pos.Y + ((rowHeight - chSize.Y) * 0.5f)), Palette.TextMuted.U32(), chev);
        rightX -= Ui.Px(8f);

        var name = this.theme.CurrentThemeName;
        var nameSize = Ui.Measure(this.fonts.Body, name);
        var nameX = rightX - nameSize.X;
        Ui.TextAt(dl, this.fonts.Body, new Vector2(nameX, pos.Y + ((rowHeight - nameSize.Y) * 0.5f)), Palette.TextSecondary.U32(), name);

        var swatchW = Ui.Px(40f);
        var swatchH = Ui.Px(16f);
        var swatchPos = new Vector2(nameX - Ui.Px(10f) - swatchW, pos.Y + ((rowHeight - swatchH) * 0.5f));
        this.DrawThemeSwatch(dl, swatchPos, swatchW, swatchH);

        dl.AddLine(new Vector2(pos.X, pos.Y + rowHeight), new Vector2(pos.X + contentWidth, pos.Y + rowHeight), Palette.Border.U32(), 1f);
        return clicked;
    }

    private void DrawThemeSwatch(ImDrawListPtr dl, Vector2 pos, float w, float h)
    {
        var stripes = this.theme.Stripes;
        if (stripes.Count > 0)
        {
            Ui.FlagBar(dl, pos, w, stripes, h);
        }
        else
        {
            var seg = w / 3f;
            dl.AddRectFilled(pos, pos + new Vector2(seg, h), Palette.Surface1.U32());
            dl.AddRectFilled(pos + new Vector2(seg, 0f), pos + new Vector2(seg * 2f, h), Palette.Surface2.U32());
            dl.AddRectFilled(pos + new Vector2(seg * 2f, 0f), pos + new Vector2(w, h), this.theme.Accent.U32());
        }

        dl.AddRect(pos, pos + new Vector2(w, h), Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);
    }

    // The signed-in member's own row: portrait, first name + age, the first line of their bio, opening
    // the Profile tab.
    private void DrawAccountRow(float contentWidth)
    {
        this.profiles.EnsureLoaded();
        this.photoSvc.EnsureLoaded();
        var mine = this.profiles.Mine;

        var rowHeight = Ui.Px(64f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##s_account", new Vector2(contentWidth, rowHeight));
        var dl = ImGui.GetWindowDrawList();

        var av = Ui.Px(44f);
        var avPos = new Vector2(pos.X, pos.Y + ((rowHeight - av) * 0.5f));
        var avSize = new Vector2(av, av);
        var main = this.photoSvc.Mine.FirstOrDefault(p => p.State == PhotoStateEnum.Approved);
        var tex = main is null ? null : this.photoSvc.Texture(main.Id);
        if (tex != null)
        {
            var (uvMin, uvMax) = Ui.CoverUv(tex.Width, tex.Height, 1f);
            dl.AddImage(tex.Handle, avPos, avPos + avSize, uvMin, uvMax);
        }
        else
        {
            dl.AddRectFilled(avPos, avPos + avSize, Palette.Surface2.U32());
            var initial = (mine?.DisplayName is { Length: > 0 } dn ? dn[..1] : "?").ToUpperInvariant();
            var isz = Ui.Measure(this.fonts.SerifName, initial);
            Ui.TextAt(dl, this.fonts.SerifName, avPos + ((avSize - isz) * 0.5f), Palette.TextMuted.U32(), initial);
        }

        dl.AddRect(avPos, avPos + avSize, Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);

        var textX = pos.X + av + Ui.Px(12f);
        var firstName = mine?.DisplayName is { Length: > 0 } nm ? FirstWord(nm) : "Your profile";
        var nameSize = Ui.Measure(this.fonts.Body, firstName);
        var bioLine = BioFirstLine(mine?.Bio);
        var blockH = nameSize.Y + (bioLine.Length > 0 ? Ui.Px(3f) + Ui.Measure(this.fonts.Caption, "X").Y : 0f);
        var topY = pos.Y + ((rowHeight - blockH) * 0.5f);

        Ui.TextAt(dl, this.fonts.Body, new Vector2(textX, topY), Palette.TextPrimary.U32(), firstName);
        if (mine is { } m)
            Ui.TextAt(dl, this.fonts.Body, new Vector2(textX + nameSize.X, topY), Palette.TextMuted.U32(), $" · {m.Age}");

        if (bioLine.Length > 0)
        {
            var maxW = contentWidth - (textX - pos.X) - Ui.Px(26f);
            Ui.TextAt(dl, this.fonts.Caption, new Vector2(textX, topY + nameSize.Y + Ui.Px(3f)), Palette.TextMuted.U32(), this.Truncate(bioLine, this.fonts.Caption, maxW));
        }

        var chev = FontAwesomeIcon.ChevronRight.ToIconString();
        var chSize = Ui.Measure(this.fonts.Icon, chev);
        Ui.TextAt(dl, this.fonts.Icon, new Vector2(pos.X + contentWidth - chSize.X, pos.Y + ((rowHeight - chSize.Y) * 0.5f)), Palette.TextMuted.U32(), chev);

        dl.AddLine(new Vector2(pos.X, pos.Y + rowHeight), new Vector2(pos.X + contentWidth, pos.Y + rowHeight), Palette.Border.U32(), 1f);
        if (clicked)
            this.router.Navigate(Screen.MyProfile);
    }

    // Connection row: the current data center + region code, opening the in-Settings picker.
    private bool DrawDataCenterRow(float contentWidth)
    {
        this.catalog.EnsureLoaded();
        this.profiles.EnsureLoaded();
        var (dcName, region) = this.CurrentDcRegion();

        var rowHeight = Ui.Px(50f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##s_dc", new Vector2(contentWidth, rowHeight));
        var dl = ImGui.GetWindowDrawList();

        var labelSize = Ui.Measure(this.fonts.Body, "Data center");
        Ui.TextAt(dl, this.fonts.Body, new Vector2(pos.X, pos.Y + ((rowHeight - labelSize.Y) * 0.5f)), Palette.TextPrimary.U32(), "Data center");

        var chev = FontAwesomeIcon.ChevronRight.ToIconString();
        var chSize = Ui.Measure(this.fonts.Icon, chev);
        var rightX = pos.X + contentWidth - chSize.X;
        Ui.TextAt(dl, this.fonts.Icon, new Vector2(rightX, pos.Y + ((rowHeight - chSize.Y) * 0.5f)), Palette.TextMuted.U32(), chev);
        rightX -= Ui.Px(8f);

        if (region.Length > 0)
        {
            var rSize = Ui.Measure(this.fonts.Eyebrow, region);
            rightX -= rSize.X;
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(rightX, pos.Y + ((rowHeight - rSize.Y) * 0.5f)), Palette.TextMuted.U32(), region);
            rightX -= Ui.Px(8f);
        }

        var dcSize = Ui.Measure(this.fonts.Body, dcName);
        Ui.TextAt(dl, this.fonts.Body, new Vector2(rightX - dcSize.X, pos.Y + ((rowHeight - dcSize.Y) * 0.5f)), Palette.TextSecondary.U32(), dcName);

        dl.AddLine(new Vector2(pos.X, pos.Y + rowHeight), new Vector2(pos.X + contentWidth, pos.Y + rowHeight), Palette.Border.U32(), 1f);
        return clicked;
    }

    private void OpenDataCenterPicker()
    {
        this.inDataCenter = true;
        this.pickerDc = this.CurrentPickerDc();
    }

    // The Data center sub-view: pick a data center, then a home world within it. Selecting a world saves
    // it straight to the profile (same field the profile editor writes) and returns to the list.
    private void DrawDataCenterPicker(float contentWidth)
    {
        this.DrawSubHeader("Data center", contentWidth);

        this.catalog.EnsureLoaded();
        if (!this.catalog.Ready)
        {
            this.Hint("Loading worlds…", contentWidth);
            return;
        }

        var dcs = this.catalog.DataCenters;
        if (this.pickerDc < 0)
            this.pickerDc = this.CurrentPickerDc();

        this.kit.SectionLabel("Data center");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var dc = this.kit.ChipFlow("s_pick_dc", dcs.Select(d => d.Name).ToArray(), i => i == this.pickerDc, contentWidth);
        if (dc >= 0)
            this.pickerDc = dc;

        if (this.pickerDc >= 0 && this.pickerDc < dcs.Count)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
            this.kit.SectionLabel("Home world");
            ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
            var worlds = dcs[this.pickerDc].Worlds;
            var currentWorld = (int)(this.profiles.Mine?.WorldId ?? 0);
            var w = this.kit.ChipFlow("s_pick_world", worlds.Select(x => x.Name).ToArray(), i => worlds[i].Id == currentWorld, contentWidth);
            if (w >= 0)
            {
                this.SaveWorld(worlds[w].Id);
                this.inDataCenter = false;
                this.pickerDc = -1;
            }
        }
    }

    private void DrawSubHeader(string title, float contentWidth)
    {
        var h = Ui.Px(44f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##s_subback", new Vector2(contentWidth, h));
        var dl = ImGui.GetWindowDrawList();
        var color = (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32();
        var cy = pos.Y + (h * 0.5f);
        var cx = pos.X + Ui.Px(2f);
        var r = Ui.Px(4f);
        dl.AddLine(new Vector2(cx + r, cy - r), new Vector2(cx, cy), color, Ui.Px(1.5f));
        dl.AddLine(new Vector2(cx, cy), new Vector2(cx + r, cy + r), color, Ui.Px(1.5f));
        var ts = Ui.Measure(this.fonts.Eyebrow, title.ToUpperInvariant());
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(cx + r + Ui.Px(10f), cy - (ts.Y * 0.5f)), Palette.TextSecondary.U32(), title.ToUpperInvariant());
        dl.AddLine(new Vector2(pos.X, pos.Y + h), new Vector2(pos.X + contentWidth, pos.Y + h), Palette.Border.U32(), 1f);
        if (clicked)
        {
            this.inDataCenter = false;
            this.pickerDc = -1;
        }

        ImGui.Dummy(new Vector2(0f, h + Ui.Px(14f)));
    }

    private void SaveWorld(int worldId)
    {
        if (this.profiles.Mine is not { } mine)
            return;
        mine.WorldId = worldId;
        this.profiles.Save(mine);
    }

    private (string Dc, string Region) CurrentDcRegion()
    {
        var worldId = (int)(this.profiles.Mine?.WorldId ?? 0);
        foreach (var dc in this.catalog.DataCenters)
            foreach (var w in dc.Worlds)
                if (w.Id == worldId)
                    return (dc.Name, RegionCode(dc.Region));
        return ("Not set", string.Empty);
    }

    private int CurrentPickerDc()
    {
        var worldId = (int)(this.profiles.Mine?.WorldId ?? 0);
        var dcs = this.catalog.DataCenters;
        for (var i = 0; i < dcs.Count; i++)
            foreach (var w in dcs[i].Worlds)
                if (w.Id == worldId)
                    return i;
        return -1;
    }

    private static string RegionCode(string region) => region switch
    {
        "NorthAmerica" or "North America" or "NA" => "NA",
        "Europe" or "EU" => "EU",
        "Japan" or "JP" => "JP",
        "Oceania" or "OCE" => "OCE",
        _ => (region.Length > 3 ? region[..3] : region).ToUpperInvariant(),
    };

    private void DrawFooter(float fullWidth)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(24f)));
        const string text = "EIKON · DALAMUD PLUGIN";
        var ts = Ui.Measure(this.fonts.Eyebrow, text);
        var pos = ImGui.GetCursorScreenPos();
        Ui.TextAt(ImGui.GetWindowDrawList(), this.fonts.Eyebrow, new Vector2(pos.X + ((fullWidth - ts.X) * 0.5f), pos.Y), Palette.TextMuted.U32(), text);
        ImGui.Dummy(new Vector2(fullWidth, ts.Y + Ui.Px(24f)));
    }

    // ---- small pieces ----

    private void Hint(string text, float contentWidth)
    {
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentWidth);
            ImGui.TextWrapped(text);
            ImGui.PopTextWrapPos();
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(2f)));
    }

    // An eyebrow label with a right-aligned mono value on one line (Sound volume 70%).
    private void EyebrowValue(string label, string value, float contentWidth)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        Ui.TextAt(dl, this.fonts.Eyebrow, origin, Palette.TextSecondary.U32(), label.ToUpperInvariant());
        var vs = Ui.Measure(this.fonts.Mono, value);
        Ui.TextAt(dl, this.fonts.Mono, new Vector2(origin.X + contentWidth - vs.X, origin.Y), Palette.TextMuted.U32(), value);
        ImGui.Dummy(new Vector2(contentWidth, Ui.Measure(this.fonts.Eyebrow, "X").Y));
    }

    // The notification corner as one bordered box: Top / Bottom over Left / Center / Right; active fills ink.
    private void DrawNotifPosition(float contentWidth)
    {
        var corner = Math.Clamp(this.config.NotificationCorner, 0, 5);
        var vert = corner / 3;
        var horiz = corner % 3;

        var rowH = Ui.Px(36f);
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        var cellW2 = contentWidth / 2f;
        var topLabels = new[] { "Top", "Bottom" };
        var newVert = vert;
        for (var i = 0; i < 2; i++)
            if (this.SegCell($"##np_v{i}", new Vector2(pos.X + (i * cellW2), pos.Y), cellW2, rowH, topLabels[i], i == vert))
                newVert = i;

        var row2Y = pos.Y + rowH;
        var cellW3 = contentWidth / 3f;
        var botLabels = new[] { "Left", "Center", "Right" };
        var newHoriz = horiz;
        for (var i = 0; i < 3; i++)
            if (this.SegCell($"##np_h{i}", new Vector2(pos.X + (i * cellW3), row2Y), cellW3, rowH, botLabels[i], i == horiz))
                newHoriz = i;

        var border = Palette.Border.U32();
        dl.AddRect(pos, new Vector2(pos.X + contentWidth, row2Y + rowH), border, 0f, ImDrawFlags.None, 1f);
        dl.AddLine(new Vector2(pos.X, row2Y), new Vector2(pos.X + contentWidth, row2Y), border, 1f);
        dl.AddLine(new Vector2(pos.X + cellW2, pos.Y), new Vector2(pos.X + cellW2, row2Y), border, 1f);
        dl.AddLine(new Vector2(pos.X + cellW3, row2Y), new Vector2(pos.X + cellW3, row2Y + rowH), border, 1f);
        dl.AddLine(new Vector2(pos.X + (cellW3 * 2f), row2Y), new Vector2(pos.X + (cellW3 * 2f), row2Y + rowH), border, 1f);

        var next = (newVert * 3) + newHoriz;
        if (next != this.config.NotificationCorner)
        {
            this.config.NotificationCorner = next;
            this.config.Save();
        }

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(contentWidth, rowH * 2f));
    }

    private bool SegCell(string id, Vector2 pos, float w, float h, string label, bool active)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
        var dl = ImGui.GetWindowDrawList();
        if (active)
            dl.AddRectFilled(pos, pos + new Vector2(w, h), Palette.TextPrimary.U32());
        var ts = Ui.Measure(this.fonts.Label, label);
        var color = active ? Palette.Paper : (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary);
        Ui.TextAt(dl, this.fonts.Label, pos + ((new Vector2(w, h) - ts) * 0.5f), color.U32(), label);
        return clicked;
    }

    private static string FirstWord(string s)
    {
        var sp = s.IndexOf(' ');
        return sp > 0 ? s[..sp] : s;
    }

    private static string BioFirstLine(string? bio)
    {
        if (string.IsNullOrWhiteSpace(bio))
            return string.Empty;
        var nl = bio.IndexOfAny(new[] { '\n', '\r' });
        return (nl >= 0 ? bio[..nl] : bio).Trim();
    }

    private string Truncate(string text, IFontHandle font, float maxWidth)
    {
        if (Ui.Measure(font, text).X <= maxWidth)
            return text;
        var s = text;
        while (s.Length > 1 && Ui.Measure(font, s + "…").X > maxWidth)
            s = s[..^1];
        return s + "…";
    }

    // Pull the privacy prefs from the server once per session so the toggles reflect saved state.
    private void EnsureSettingsLoaded()
    {
        if (this.settingsLoaded || this.auth.Phase != AuthPhase.LoggedIn)
            return;
        this.settingsLoaded = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                {
                    this.settingsLoaded = false;   // not ready yet; retry next frame
                    return;
                }
                var s = await this.api.GetSettingsAsync(token, CancellationToken.None);
                this.discreet = s.Discreet;
                this.onlyVerifiedMessage = s.OnlyVerifiedMessage;
            }
            catch (Exception ex)
            {
                this.settingsLoaded = false;
                this.log.Warning(ex, "Loading privacy settings failed.");
            }
        });
    }

    // Persist the privacy prefs (fire and forget); the toggle already shows the intended state.
    private void SaveSettings()
    {
        var d = this.discreet;
        var o = this.onlyVerifiedMessage;
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (!string.IsNullOrEmpty(token))
                    await this.api.UpdateSettingsAsync(token, d, o, CancellationToken.None);
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Saving privacy settings failed.");
            }
        });
    }

    // Two selectable cards (Expanded / Compact): an icon, a title and a subtitle; the selected one gets
    // an accent ring + check. Sets config.GridLayout, which the discovery grid reads.
    private void DrawLayoutCards(float contentWidth)
    {
        var gap = Ui.Px(10f);
        var cardWidth = (contentWidth - gap) / 2f;
        var cardHeight = Ui.Px(96f);
        this.DrawLayoutCard(0, FontAwesomeIcon.ThList, "Expanded", "Big photos, more detail", cardWidth, cardHeight);
        ImGui.SameLine(0f, gap);
        this.DrawLayoutCard(1, FontAwesomeIcon.Th, "Compact", "More profiles per screen", cardWidth, cardHeight);
    }

    private void DrawLayoutCard(int index, FontAwesomeIcon icon, string title, string subtitle, float width, float height)
    {
        var selected = Math.Clamp(this.config.GridLayout, 0, 1) == index;
        var pos = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton($"##layout_{index}", new Vector2(width, height)) && this.config.GridLayout != index)
        {
            this.config.GridLayout = index;
            this.config.Save();
        }

        var drawList = ImGui.GetWindowDrawList();
        var size = new Vector2(width, height);
        if (selected)
        {
            drawList.AddRectFilled(pos, pos + size, Palette.WithAlpha(this.theme.Accent, 0.06f).U32());
            drawList.AddRect(pos, pos + size, this.theme.Accent.U32(), 0f, ImDrawFlags.None, Ui.Px(1.5f));
        }
        else
        {
            drawList.AddRect(pos, pos + size, Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);
        }

        var pad = Ui.Px(12f);
        Ui.TextAt(drawList, this.fonts.Icon, pos + new Vector2(pad, pad), (selected ? this.theme.Accent : Palette.TextSecondary).U32(), icon.ToIconString());

        if (selected)
        {
            var check = FontAwesomeIcon.CheckCircle.ToIconString();
            var checkSize = Ui.Measure(this.fonts.Icon, check);
            Ui.TextAt(drawList, this.fonts.Icon, pos + new Vector2(width - pad - checkSize.X, pad), this.theme.Accent.U32(), check);
        }

        var titleSize = Ui.Measure(this.fonts.Body, title);
        var subLines = this.WrapCaption(subtitle, width - (pad * 2f));
        var subH = subLines.Count * Ui.Measure(this.fonts.Caption, "X").Y;
        var titleY = pos.Y + height - pad - titleSize.Y - Ui.Px(3f) - subH;
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(pos.X + pad, titleY), Palette.TextPrimary.U32(), title);

        var subColor = (selected ? Palette.TextSecondary : Palette.TextMuted).U32();
        var lineY = titleY + titleSize.Y + Ui.Px(3f);
        foreach (var line in subLines)
        {
            Ui.TextAt(drawList, this.fonts.Caption, new Vector2(pos.X + pad, lineY), subColor, line);
            lineY += Ui.Measure(this.fonts.Caption, line).Y;
        }
    }

    // Greedy word-wrap of a short caption to a pixel width, measured in the caption font.
    private List<string> WrapCaption(string text, float maxWidth)
    {
        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split(' '))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (Ui.Measure(this.fonts.Caption, candidate).X <= maxWidth)
            {
                line = candidate;
                continue;
            }

            if (line.Length > 0)
                lines.Add(line);
            line = word;
        }

        if (line.Length > 0)
            lines.Add(line);
        return lines;
    }

    // Text size: an A-cue slider snapping to fixed steps, over a live sample that resizes as you drag. The
    // sample scales by drawing at an explicit size; the real UI resize commits once on release
    // (IsItemDeactivated), and the font atlas re-rasterizes in the background.
    private void DrawTextSize(float contentWidth)
    {
        // Percent readout, right-aligned. Reflects the current step; a drag updates it the next frame.
        var pctText = $"{TextScale.Steps[this.textStep]}%";
        var pctSize = Ui.Measure(this.fonts.Caption, pctText);
        var pctPos = ImGui.GetCursorScreenPos();
        Ui.TextAt(ImGui.GetWindowDrawList(), this.fonts.Caption, new Vector2(pctPos.X + contentWidth - pctSize.X, pctPos.Y), this.theme.AccentText.U32(), pctText);
        ImGui.Dummy(new Vector2(contentWidth, pctSize.Y));
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));

        var released = this.DrawSizeSlider(contentWidth);   // updates this.textStep
        var pendingPercent = TextScale.Steps[this.textStep];
        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.DrawSizePreview(contentWidth, TextScale.ToFactor(pendingPercent));

        if (released && pendingPercent != this.config.TextScalePercent)
        {
            this.config.TextScalePercent = pendingPercent;
            Ui.Scale = TextScale.ToFactor(pendingPercent);   // instant: layout + draw-list text resize now
            this.fonts.Rebuild(Ui.Scale);                    // crisp catch-up in the background
            this.config.Save();
        }
    }

    // A small "A" and a large "A" flanking the slider (its size cues), manually centered against the row so
    // the mixed-size glyphs line up with the track. Returns true on the frame the drag ends.
    private bool DrawSizeSlider(float contentWidth)
    {
        var rowH = Ui.Px(24f);
        var rowPos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var aSmall = Ui.Measure(this.fonts.Caption, "A");
        var aLarge = Ui.Measure(this.fonts.Title, "A");
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(rowPos.X, rowPos.Y + ((rowH - aSmall.Y) * 0.5f)), Palette.TextMuted.U32(), "A");
        var aLargeX = rowPos.X + contentWidth - aLarge.X;
        Ui.TextAt(drawList, this.fonts.Title, new Vector2(aLargeX, rowPos.Y + ((rowH - aLarge.Y) * 0.5f)), Palette.TextMuted.U32(), "A");

        var gap = Ui.Px(12f);
        var sliderX = rowPos.X + aSmall.X + gap;
        var sliderW = aLargeX - gap - sliderX;
        ImGui.SetCursorScreenPos(new Vector2(sliderX, rowPos.Y + ((rowH - Ui.Px(22f)) * 0.5f)));
        this.textStep = this.kit.Slider("##s_textsize", this.textStep, 0, TextScale.Steps.Length - 1, sliderW);
        var released = ImGui.IsItemDeactivated();

        ImGui.SetCursorScreenPos(rowPos);
        ImGui.Dummy(new Vector2(contentWidth, rowH));
        return released;
    }

    // A sample profile row that resizes to the pending Text size, so the effect is visible before commit.
    private void DrawSizePreview(float contentWidth, float factor)
    {
        float Px(float designPx) => Ui.Px(designPx) / Ui.Scale * factor;   // design px at the pending factor

        var pad = Ui.Px(12f);
        var avatar = Px(38f);
        var namePx = Px(17f);
        var bioPx = Px(14f);
        var textH = namePx + Ui.Px(4f) + bioPx;
        var boxH = (pad * 2f) + MathF.Max(avatar, textH);

        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, pos + new Vector2(contentWidth, boxH), Palette.Surface1.U32());
        drawList.AddRect(pos, pos + new Vector2(contentWidth, boxH), Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);

        var av = new Vector2(pos.X + pad, pos.Y + ((boxH - avatar) * 0.5f));
        drawList.AddRectFilled(av, av + new Vector2(avatar, avatar), Palette.Surface2.U32());
        Ui.TextAtSized(drawList, this.fonts.SerifName, new Vector2(av.X + (avatar * 0.34f), av.Y + (avatar * 0.2f)), avatar * 0.54f, Palette.TextMuted.U32(), "R");

        var textX = av.X + avatar + pad;
        var textY = pos.Y + ((boxH - textH) * 0.5f);
        Ui.TextAtSized(drawList, this.fonts.Body, new Vector2(textX, textY), namePx, Palette.TextPrimary.U32(), "Rhys · 28");
        Ui.TextAtSized(drawList, this.fonts.Caption, new Vector2(textX, textY + namePx + Ui.Px(4f)), bioPx, Palette.TextSecondary.U32(), "Warm, quiet, here for real.");

        ImGui.Dummy(new Vector2(contentWidth, boxH));
    }

    private bool NavRow(string id, string label, string value, Vector4 labelColor, bool chevron, float contentWidth)
    {
        var rowHeight = Ui.Px(50f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(contentWidth, rowHeight));
        var drawList = ImGui.GetWindowDrawList();

        var labelSize = Ui.Measure(this.fonts.Body, label);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(pos.X, pos.Y + ((rowHeight - labelSize.Y) * 0.5f)), labelColor.U32(), label);

        var rightX = pos.X + contentWidth;
        if (chevron)
        {
            var glyph = FontAwesomeIcon.ChevronRight.ToIconString();
            var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
            rightX -= glyphSize.X;
            Ui.TextAt(drawList, this.fonts.Icon, new Vector2(rightX, pos.Y + ((rowHeight - glyphSize.Y) * 0.5f)), Palette.TextMuted.U32(), glyph);
            rightX -= Ui.Px(8f);
        }

        if (value.Length > 0)
        {
            var valueSize = Ui.Measure(this.fonts.Body, value);
            Ui.TextAt(drawList, this.fonts.Body, new Vector2(rightX - valueSize.X, pos.Y + ((rowHeight - valueSize.Y) * 0.5f)), Palette.TextSecondary.U32(), value);
        }

        drawList.AddLine(new Vector2(pos.X, pos.Y + rowHeight), new Vector2(pos.X + contentWidth, pos.Y + rowHeight), Palette.Border.U32(), 1f);
        return clicked;
    }

    private bool ToggleRow(string id, string label, bool value, float contentWidth)
    {
        var rowHeight = Ui.Px(50f);
        var localStart = ImGui.GetCursorPos();
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var labelSize = Ui.Measure(this.fonts.Body, label);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(pos.X, pos.Y + ((rowHeight - labelSize.Y) * 0.5f)), Palette.TextPrimary.U32(), label);

        var toggleWidth = Ui.Px(38f);
        var toggleHeight = Ui.Px(22f);
        ImGui.SetCursorScreenPos(new Vector2(pos.X + contentWidth - toggleWidth, pos.Y + ((rowHeight - toggleHeight) * 0.5f)));
        var next = this.kit.Toggle(id, value);

        drawList.AddLine(new Vector2(pos.X, pos.Y + rowHeight), new Vector2(pos.X + contentWidth, pos.Y + rowHeight), Palette.Border.U32(), 1f);
        ImGui.SetCursorPos(new Vector2(localStart.X, localStart.Y + rowHeight));
        return next;
    }
}
