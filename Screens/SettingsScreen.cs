using System.Threading;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using Eikon.Config;
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

    // Muted fill for the mini layout-preview tiles in the unselected Browse-layout card (#26313F).
    private static readonly Vector4 PreviewTile = new(0.149f, 0.192f, 0.247f, 1f);

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

    private bool discreet;
    private bool onlyVerifiedMessage;
    private bool settingsLoaded;   // privacy prefs fetched from the server this session

    public SettingsScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, AuthService auth, KeyVault keyVault, IApiClient api, Configuration config, SoundService sound, IPluginLog log, DeleteAccountFlow deleteFlow)
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
    }

    public Screen Id => Screen.Settings;

    public bool Chrome => true;

    public void Draw()
    {
        var contentWidth = ImGui.GetContentRegionAvail().X - Ui.Px(16f);

        this.kit.SectionLabel("Theme color");
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.DrawSwatches(contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        this.kit.SectionLabel("Browse layout");
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.DrawLayoutCards(contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        this.kit.SectionLabel("Account");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
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

        var nextOnlyVerified = this.ToggleRow("##s_onlyverif", "Only verified can message me", this.onlyVerifiedMessage, contentWidth);
        if (nextOnlyVerified != this.onlyVerifiedMessage)
        {
            this.onlyVerifiedMessage = nextOnlyVerified;
            this.SaveSettings();
        }
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentWidth);
            ImGui.TextWrapped("Discreet mode hides you from the grid; people you already talk to can still reach you.");
            ImGui.PopTextWrapPos();
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

        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextUnformatted($"Sound volume ({this.config.NotificationVolume}%)");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        var nextVolume = this.kit.Slider("##s_notifvol", this.config.NotificationVolume, 0, 100, contentWidth);
        if (nextVolume != this.config.NotificationVolume)
        {
            this.config.NotificationVolume = nextVolume;
            this.config.Save();
        }
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        if (this.kit.SecondaryButton("##s_notiftest", "Test sound", Ui.Px(120f)))
            this.sound.Play(this.config.NotificationVolume);

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextUnformatted("Where they appear");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var corner = Math.Clamp(this.config.NotificationCorner, 0, 5);
        var nextVert = this.kit.Segmented("##s_notif_v", new[] { "Top", "Bottom" }, corner / 3, contentWidth);
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        var nextHoriz = this.kit.Segmented("##s_notif_h", new[] { "Left", "Center", "Right" }, corner % 3, contentWidth);
        var nextCorner = (nextVert * 3) + nextHoriz;
        if (nextCorner != this.config.NotificationCorner)
        {
            this.config.NotificationCorner = nextCorner;
            this.config.Save();
        }

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
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentWidth);
            ImGui.TextWrapped("Off (default): unlocks automatically for your Windows account. On: asks for your passphrase every launch - turn on if others can use this PC under your Windows login.");
            ImGui.PopTextWrapPos();
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        this.kit.SectionLabel("About");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        if (this.NavRow("##s_guidelines", "Community Guidelines", string.Empty, Palette.TextPrimary, true, contentWidth))
            this.router.Navigate(Screen.Guidelines);
        this.NavRow("##s_version", "Version", "1.0.0", Palette.TextSecondary, false, contentWidth);

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));

        this.deleteFlow.Draw();
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

    private void DrawSwatches(float contentWidth)
    {
        const int columns = 6;
        var gap = Ui.Px(10f);
        var cell = (contentWidth - (gap * (columns - 1))) / columns;
        var diameter = MathF.Min(cell, Ui.Px(42f));
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap)))
        {
            for (var i = 0; i < AccentPresets.All.Count; i++)
            {
                if (i % columns != 0)
                    ImGui.SameLine();
                this.DrawSwatch(i, cell, diameter);
            }
        }
    }

    private void DrawSwatch(int index, float cell, float diameter)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##sw" + index, new Vector2(cell, diameter));
        var drawList = ImGui.GetWindowDrawList();

        var color = Palette.Rgb(AccentPresets.All[index].Rgb);
        var center = new Vector2(pos.X + (cell * 0.5f), pos.Y + (diameter * 0.5f));
        drawList.AddCircleFilled(center, (diameter * 0.5f) - Ui.Px(3f), color.U32(), 24);

        if (this.theme.AccentIndex == index)
        {
            drawList.AddCircle(center, (diameter * 0.5f) - Ui.Px(1f), Palette.White.U32(), 24, Ui.Px(2f));
            var check = FontAwesomeIcon.Check.ToIconString();
            var checkSize = Ui.Measure(this.fonts.Icon, check);
            var onColor = Palette.Luminance(color) > 0.6f ? Palette.Bg : Palette.White;
            Ui.TextAt(drawList, this.fonts.Icon, new Vector2(center.X - (checkSize.X * 0.5f), center.Y - (checkSize.Y * 0.5f)), onColor.U32(), check);
        }

        if (clicked)
            this.theme.SetAccent(index);
    }

    // Two selectable cards (Expanded / Compact), each with a mini grid-preview, a title and a
    // subtitle; the selected one gets an accent ring + check. Sets config.GridLayout, which the
    // discovery grid reads. Drawn with draw lists to match the rest of Settings.
    private void DrawLayoutCards(float contentWidth)
    {
        var gap = Ui.Px(10f);
        var cardWidth = (contentWidth - gap) / 2f;
        var cardHeight = Ui.Px(120f);
        this.DrawLayoutCard(0, "Expanded", "Big photos, more detail", cardWidth, cardHeight);
        ImGui.SameLine(0f, gap);
        this.DrawLayoutCard(1, "Compact", "More profiles per screen", cardWidth, cardHeight);
    }

    private void DrawLayoutCard(int index, string title, string subtitle, float width, float height)
    {
        var selected = Math.Clamp(this.config.GridLayout, 0, 1) == index;
        var pos = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton($"##layout_{index}", new Vector2(width, height)) && this.config.GridLayout != index)
        {
            this.config.GridLayout = index;
            this.config.Save();
        }

        var drawList = ImGui.GetWindowDrawList();
        var rounding = Ui.Px(12f);
        var size = new Vector2(width, height);

        var bg = selected ? Palette.WithAlpha(this.theme.Accent, 0.09f) : Palette.Surface1;
        drawList.AddRectFilled(pos, pos + size, bg.U32(), rounding);
        if (selected)
            drawList.AddRect(pos, pos + size, this.theme.Accent.U32(), rounding, ImDrawFlags.None, Ui.Px(1.5f));
        else
            drawList.AddRect(pos, pos + size, Palette.Border.U32(), rounding, ImDrawFlags.None, 1f);

        var pad = Ui.Px(11f);
        var previewHeight = Ui.Px(44f);
        var previewWidth = width - (pad * 2f);
        var previewPos = pos + new Vector2(pad, pad);
        var tileColor = selected ? Palette.WithAlpha(this.theme.Accent, 0.45f) : PreviewTile;
        if (index == 0)
            DrawPreviewTiles(drawList, previewPos, previewWidth, previewHeight, 2, 1, tileColor);
        else
            DrawPreviewTiles(drawList, previewPos, previewWidth, previewHeight, 3, 2, tileColor);

        if (selected)
        {
            var check = FontAwesomeIcon.CheckCircle.ToIconString();
            var checkSize = Ui.Measure(this.fonts.Icon, check);
            Ui.TextAt(drawList, this.fonts.Icon, pos + new Vector2(width - pad - checkSize.X, pad), this.theme.Accent.U32(), check);
        }

        var titlePos = pos + new Vector2(pad, pad + previewHeight + Ui.Px(9f));
        Ui.TextAt(drawList, this.fonts.Body, titlePos, Palette.TextPrimary.U32(), title);
        var titleSize = Ui.Measure(this.fonts.Body, title);

        var subColor = (selected ? Palette.TextSecondary : Palette.TextMuted).U32();
        var lineY = titlePos.Y + titleSize.Y + Ui.Px(3f);
        foreach (var line in this.WrapCaption(subtitle, previewWidth))
        {
            Ui.TextAt(drawList, this.fonts.Caption, new Vector2(titlePos.X, lineY), subColor, line);
            lineY += Ui.Measure(this.fonts.Caption, line).Y;
        }
    }

    private static void DrawPreviewTiles(ImDrawListPtr drawList, Vector2 pos, float width, float height, int cols, int rows, Vector4 color)
    {
        var gap = Ui.Px(rows > 1 ? 3f : 4f);
        var tileWidth = (width - (gap * (cols - 1))) / cols;
        var tileHeight = (height - (gap * (rows - 1))) / rows;
        var rounding = Ui.Px(3f);
        for (var row = 0; row < rows; row++)
        for (var col = 0; col < cols; col++)
        {
            var tilePos = pos + new Vector2(col * (tileWidth + gap), row * (tileHeight + gap));
            drawList.AddRectFilled(tilePos, tilePos + new Vector2(tileWidth, tileHeight), color.U32(), rounding);
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
