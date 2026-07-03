using System.Threading;
using Dalamud.Interface;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Shown after sign-in when the account is soft-deleted but still inside the grace window
// (DELETE-ACCOUNT-PLAN.md). Offers to restore and pick up where they left off, or to bring the
// deletion forward. Non-chrome, like the age gate. AuthService routes away on success.
internal sealed class RestoreAccountScreen : IScreen
{
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly AuthService auth;

    private bool busy;
    private string error = string.Empty;

    public RestoreAccountScreen(ThemeService theme, Kit kit, UiFonts fonts, AuthService auth)
    {
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.auth = auth;
    }

    public Screen Id => Screen.RestoreAccount;

    public bool Chrome => false;

    public void Draw()
    {
        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(20f);
        var contentWidth = avail.X - (pad * 2f);

        ImGui.Dummy(new Vector2(0f, Ui.Px(40f)));
        Ui.CenteredText(avail.X, this.fonts.Icon, this.theme.Accent, FontAwesomeIcon.Undo.ToIconString());
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        Ui.CenteredText(avail.X, this.fonts.Title, Palette.TextPrimary, "Welcome back");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));

        ImGui.SetCursorPosX(pad);
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentWidth);
            ImGui.TextWrapped(this.BodyText());
            ImGui.PopTextWrapPos();
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(20f)));
        ImGui.SetCursorPosX(pad);
        if (this.busy)
        {
            this.kit.SecondaryButton("##restore_busy", "Working...", contentWidth);
            return;
        }

        if (this.kit.PrimaryButton("##restore_yes", "Restore my account", contentWidth))
            this.Run(restore: true);

        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        ImGui.SetCursorPosX(pad);
        if (this.kit.DangerButton("##restore_deletenow", "Delete now instead", contentWidth))
            this.Run(restore: false);

        if (this.error.Length > 0)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
            ImGui.SetCursorPosX(pad);
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.Danger))
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentWidth);
                ImGui.TextWrapped(this.error);
                ImGui.PopTextWrapPos();
            }
        }
    }

    private string BodyText()
    {
        var days = this.DaysLeft();
        var window = days <= 0 ? "soon" : days == 1 ? "in 1 day" : $"in {days} days";
        return $"Your account is set to delete {window}. Restore it and pick up where you left off?";
    }

    private int DaysLeft()
    {
        return DateTimeOffset.TryParse(this.auth.DeletionPendingUntil, out var until)
            ? Math.Max(0, (int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalDays))
            : 0;
    }

    private void Run(bool restore)
    {
        this.busy = true;
        this.error = string.Empty;
        _ = Task.Run(async () =>
        {
            var ok = restore
                ? await this.auth.RestoreAsync(CancellationToken.None)
                : await this.auth.ConfirmDeleteNowAsync(CancellationToken.None);
            if (!ok)
            {
                this.error = restore
                    ? "Couldn't restore your account. Try signing in again."
                    : "Couldn't complete deletion. Try again.";
                this.busy = false;
            }
            // On success AuthService navigates away from this screen.
        });
    }
}
