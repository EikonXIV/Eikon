using System.Threading;
using Dalamud.Interface;
using Eikon.Crypto;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Passphrase unlock for the re-login case. A normal relaunch unlocks the vault silently with DPAPI,
// so this screen is only reached when that material is gone: after a logout or reset, or on a new
// machine. It sits between Discord sign-in and the grid (see the routing in AuthService). The member
// either enters their passphrase to unlock their messages, or resets to a fresh identity.
internal sealed class UnlockScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly KeyVault keyVault;
    private readonly ProfileService profiles;
    private readonly AuthService auth;

    private string passphrase = string.Empty;
    private bool reveal;
    private bool error;
    private volatile bool unlocking;
    private bool confirmReset;

    public UnlockScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, KeyVault keyVault, ProfileService profiles, AuthService auth)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.keyVault = keyVault;
        this.profiles = profiles;
        this.auth = auth;
    }

    public Screen Id => Screen.Unlock;

    public bool Chrome => false;

    public void Draw()
    {
        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(24f);
        var contentWidth = avail.X - (pad * 2f);

        this.profiles.EnsureLoaded();

        if (this.confirmReset)
        {
            this.DrawResetConfirm(avail, pad, contentWidth);
            return;
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(44f)));
        this.DrawBadge(avail.X, FontAwesomeIcon.Lock.ToIconString(), this.theme.AccentTint, this.theme.AccentText);

        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        Ui.CenteredText(avail.X, this.fonts.Title, Palette.TextPrimary, "Welcome back");

        var name = this.profiles.Mine?.DisplayName;
        if (!string.IsNullOrWhiteSpace(name))
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(4f)));
            Ui.CenteredText(avail.X, this.fonts.Caption, Palette.TextSecondary, $"Signed in as {name}");
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.Wrapped(pad, contentWidth, Palette.TextMuted,
            "Enter your passphrase to unlock your messages on this device.");

        // Passphrase field with a Show/Hide toggle beside it.
        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        var toggleWidth = Ui.Px(64f);
        var gap = Ui.Px(8f);
        var fieldWidth = contentWidth - toggleWidth - gap;
        ImGui.SetCursorPosX(pad);
        var submitted = this.kit.MaskedField("##unlock_pass", ref this.passphrase, fieldWidth, this.reveal);
        ImGui.SameLine(0f, gap);
        if (this.kit.SecondaryButton("##unlock_eye", this.reveal ? "Hide" : "Show", toggleWidth))
            this.reveal = !this.reveal;

        if (this.error)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            ImGui.SetCursorPosX(pad);
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.Danger))
                ImGui.TextUnformatted("That passphrase didn't work. Try again.");
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        ImGui.SetCursorPosX(pad);
        var ready = this.passphrase.Length > 0;
        if (this.unlocking)
        {
            this.kit.SecondaryButton("##unlock_busy", "Unlocking...", contentWidth);
        }
        else if (ready)
        {
            if (this.kit.PrimaryButton("##unlock_go", "Unlock", contentWidth) || submitted)
                this.BeginUnlock();
        }
        else
        {
            this.kit.SecondaryButton("##unlock_off", "Unlock", contentWidth);
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.Wrapped(pad, contentWidth, Palette.TextMuted,
            "Only you can unlock your chats. We can't reset your passphrase.");

        // Reset escape hatch, pinned near the bottom.
        var linkText = "Forgot it? Reset and start over";
        var linkSize = Ui.Measure(this.fonts.Caption, linkText);
        ImGui.SetCursorPos(new Vector2((avail.X - linkSize.X) * 0.5f, avail.Y - Ui.Px(30f)));
        if (this.TextLink("##unlock_reset", linkText, this.theme.AccentText))
            this.confirmReset = true;
    }

    private void DrawResetConfirm(Vector2 avail, float pad, float contentWidth)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(56f)));
        this.DrawBadge(avail.X, FontAwesomeIcon.Lock.ToIconString(), Palette.WithAlpha(Palette.Danger, 0.16f), Palette.Danger);

        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
        Ui.CenteredText(avail.X, this.fonts.Title, Palette.TextPrimary, "Reset and start over?");

        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        this.Wrapped(pad, contentWidth, Palette.TextSecondary,
            "This erases your keys on this device and creates a new identity. Messages you already received can no longer be read, and you'll set a new passphrase. Your account stays the same.");

        ImGui.Dummy(new Vector2(0f, Ui.Px(20f)));
        ImGui.SetCursorPosX(pad);
        if (this.kit.DangerButton("##reset_go", "Reset everything", contentWidth))
            this.DoReset();

        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        ImGui.SetCursorPosX(pad);
        if (this.kit.SecondaryButton("##reset_cancel", "Cancel", contentWidth))
            this.confirmReset = false;
    }

    private void BeginUnlock()
    {
        if (this.unlocking)
            return;
        this.error = false;
        this.unlocking = true;
        var pass = this.passphrase;
        _ = Task.Run(() =>
        {
            var ok = this.keyVault.Unlock(pass);
            this.unlocking = false;
            if (ok)
            {
                this.passphrase = string.Empty;
                this.reveal = false;
                this.auth.MaintainKeys();   // rotate/replenish prekeys now the vault is open
                this.router.Navigate(Screen.Grid);
            }
            else
            {
                this.error = true;
                this.passphrase = string.Empty;
            }
        });
    }

    private void DoReset()
    {
        this.keyVault.Reset();
        this.confirmReset = false;
        this.error = false;
        this.passphrase = string.Empty;
        this.reveal = false;
        // No identity left, so re-onboarding mints a fresh key set and passphrase.
        this.router.Navigate(Screen.Onboarding);
    }

    // An icon centered in a soft tinted circle, advancing the layout cursor past it.
    private void DrawBadge(float fullWidth, string glyph, Vector4 circle, Vector4 iconColor)
    {
        var diameter = Ui.Px(64f);
        var startLocal = ImGui.GetCursorPos();
        var startScreen = ImGui.GetCursorScreenPos();
        var centerX = startScreen.X + (fullWidth * 0.5f);
        var center = new Vector2(centerX, startScreen.Y + (diameter * 0.5f));
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddCircleFilled(center, diameter * 0.5f, circle.U32(), 32);
        var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(drawList, this.fonts.Icon,
            new Vector2(centerX - (glyphSize.X * 0.5f), center.Y - (glyphSize.Y * 0.5f)), iconColor.U32(), glyph);

        ImGui.SetCursorPos(new Vector2(startLocal.X, startLocal.Y + diameter));
    }

    private void Wrapped(float pad, float width, Vector4 color, string text)
    {
        ImGui.SetCursorPosX(pad);
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, color))
        {
            ImGui.PushTextWrapPos(pad + width);
            ImGui.TextWrapped(text);
            ImGui.PopTextWrapPos();
        }
    }

    private bool TextLink(string id, string text, Vector4 color)
    {
        var size = Ui.Measure(this.fonts.Caption, text);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var shade = ImGui.IsItemHovered() ? Palette.WithAlpha(color, 0.8f) : color;
        Ui.TextAt(ImGui.GetWindowDrawList(), this.fonts.Caption, pos, shade.U32(), text);
        return clicked;
    }
}
