using System.Threading;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Account deletion sheet (DELETE-ACCOUNT-PLAN.md). One modal folds the consequences, optional exit
// feedback (never required), and a confirm checkbox that gates the destructive button; on success it
// shows a short goodbye, then signs out. Mirrors ModerationFlow's modal chrome. The host (Settings)
// calls Open() from the Delete account row and Draw() every frame so the popup renders.
internal sealed class DeleteAccountFlow
{
    // Client-side feedback labels (see DELETE-ACCOUNT-PLAN.md); sent as free strings, all optional.
    private static readonly string[] Reasons =
    {
        "Met someone",
        "Taking a break",
        "Not enough people nearby",
        "Didn't feel safe",
        "Privacy concerns",
        "Something else",
    };

    // Matches the server's DeleteAccountRequest.note cap (contracts/src/dtos.ts).
    private const int NoteMaxLength = 1000;

    private enum Step { Form, Deleting, Done }

    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly IApiClient api;
    private readonly AuthService auth;
    private readonly ScreenRouter router;
    private readonly IPluginLog log;

    private bool openRequest;
    private Step step;
    private readonly HashSet<int> selectedReasons = new();
    private string note = string.Empty;
    private bool acknowledged;
    private string error = string.Empty;
    private bool finalizeSignOut;   // once deletion succeeds, sign out however the popup is dismissed

    public DeleteAccountFlow(ThemeService theme, Kit kit, UiFonts fonts, IApiClient api, AuthService auth, ScreenRouter router, IPluginLog log)
    {
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.api = api;
        this.auth = auth;
        this.router = router;
        this.log = log;
    }

    public void Open()
    {
        this.step = Step.Form;
        this.selectedReasons.Clear();
        this.note = string.Empty;
        this.acknowledged = false;
        this.error = string.Empty;
        this.finalizeSignOut = false;
        this.openRequest = true;
    }

    public void Draw()
    {
        if (this.openRequest)
        {
            this.openRequest = false;
            ImGui.OpenPopup("##delete_account");
        }

        ImGui.SetNextWindowPos(HostCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize;
        var open = true;

        bool visible;
        using (ImRaii.PushColor(ImGuiCol.PopupBg, Palette.Surface1))
        using (ImRaii.PushColor(ImGuiCol.Border, Palette.Border))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(Ui.Px(18f), Ui.Px(18f))))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, Ui.Px(16f)))
        using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f))
        {
            visible = ImGui.BeginPopupModal("##delete_account", ref open, flags);
            if (visible)
            {
                var width = Ui.Px(300f);
                ImGui.Dummy(new Vector2(width, 0f));   // lock the content width; the modal auto-fits height
                if (this.step == Step.Done)
                    this.DrawDone(width);
                else
                    this.DrawForm(width);
                ImGui.EndPopup();
            }
        }

        // The goodbye popup was dismissed (Close, Esc, whatever) after a successful delete: finalize the
        // sign-out exactly once. Routing away from Settings tears down this flow's draw, so do it here.
        if (!visible && this.finalizeSignOut)
        {
            this.finalizeSignOut = false;
            this.auth.SignOut();
            this.router.Navigate(Screen.AgeGuidelines);
        }
    }

    private void DrawForm(float width)
    {
        this.IconBadge(width, FontAwesomeIcon.TrashAlt, Palette.Danger);
        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        Ui.CenteredText(width, this.fonts.Title, Palette.TextPrimary, "Delete your account?");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
            ImGui.TextWrapped("Your profile comes down right away. Photos, albums, and messages are scheduled for permanent deletion in 30 days - sign back in before then to restore everything.");

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.Divider(width);
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));

        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextUnformatted("Anything we could've done better? Optional.");
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));

        var clicked = this.kit.ChipFlow("##del_reason_", Reasons, i => this.selectedReasons.Contains(i), width);
        if (clicked >= 0 && !this.selectedReasons.Add(clicked))
            this.selectedReasons.Remove(clicked);

        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.kit.TextField("##del_note", ref this.note, "Anything else? (optional)", width);
        if (this.note.Length > NoteMaxLength)   // keep within the server's note cap so submit can't 400
            this.note = this.note[..NoteMaxLength];

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.acknowledged = this.AckRow();

        if (this.error.Length > 0)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.Danger))
                ImGui.TextWrapped(this.error);
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        if (this.step == Step.Deleting)
        {
            this.kit.SecondaryButton("##del_progress", "Deleting your account...", width);
            return;
        }

        var half = (width - Ui.Px(10f)) * 0.5f;
        if (this.kit.SecondaryButton("##del_cancel", "Cancel", half))
            ImGui.CloseCurrentPopup();
        ImGui.SameLine(0f, Ui.Px(10f));
        if (this.acknowledged)
        {
            if (this.kit.DangerButton("##del_confirm", "Delete account", half))
                this.Submit();
        }
        else
        {
            // Gated: the confirm checkbox is unticked, so the destructive action is not yet available.
            this.kit.SecondaryButton("##del_confirm_off", "Delete account", half);
        }
    }

    private void DrawDone(float width)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        this.IconBadge(width, FontAwesomeIcon.Heart, this.theme.Accent);
        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        Ui.CenteredText(width, this.fonts.Title, Palette.TextPrimary, "Your account's been deleted");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
            ImGui.TextWrapped("Changed your mind? Sign back in within 30 days to restore it. After that it's gone for good. Thanks for spending time on Eikon.");

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        if (this.kit.PrimaryButton("##del_close", "Close", width))
            ImGui.CloseCurrentPopup();   // Draw() finalizes the sign-out once the popup is gone
    }

    // Checkbox + label confirm gate, mirroring AgeGuidelinesScreen's DrawCheck.
    private bool AckRow()
    {
        var next = this.kit.Checkbox("##del_ack", this.acknowledged);
        ImGui.SameLine(0f, Ui.Px(9f));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
            ImGui.TextUnformatted("I understand my account will be deleted.");
        return next;
    }

    private void Submit()
    {
        this.step = Step.Deleting;
        this.error = string.Empty;
        var reasons = this.selectedReasons.OrderBy(i => i).Select(i => Reasons[i]).ToList();
        var noteText = this.note;
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                {
                    this.error = "You're not signed in.";
                    this.step = Step.Form;
                    return;
                }
                await this.api.DeleteAccountAsync(token, reasons, noteText, CancellationToken.None);
                this.finalizeSignOut = true;
                this.step = Step.Done;
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Account deletion failed.");
                this.error = "Couldn't delete your account. Try again.";
                this.step = Step.Form;
            }
        });
    }

    // Center of the host window, so the modal floats inside the app rather than the game screen.
    private static Vector2 HostCenter() => ImGui.GetWindowPos() + (ImGui.GetWindowSize() * 0.5f);

    private void IconBadge(float contentWidth, FontAwesomeIcon icon, Vector4 color)
    {
        var diameter = Ui.Px(52f);
        var pos = ImGui.GetCursorScreenPos();
        var center = new Vector2(pos.X + (contentWidth * 0.5f), pos.Y + (diameter * 0.5f));
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddCircleFilled(center, diameter * 0.5f, Palette.WithAlpha(color, 0.14f).U32(), 32);
        var glyph = icon.ToIconString();
        var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(center.X - (glyphSize.X * 0.5f), center.Y - (glyphSize.Y * 0.5f)), color.U32(), glyph);
        ImGui.Dummy(new Vector2(contentWidth, diameter));
    }

    private void Divider(float width)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(new Vector2(pos.X, pos.Y), new Vector2(pos.X + width, pos.Y), Palette.Border.U32(), 1f);
        ImGui.Dummy(new Vector2(width, 1f));
    }
}
