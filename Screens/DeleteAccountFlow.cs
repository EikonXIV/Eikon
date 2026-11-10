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

    private static readonly string[] ReasonKeys =
    {
        "deleteAccount.reason.metSomeone",
        "deleteAccount.reason.takingBreak",
        "deleteAccount.reason.notEnoughPeople",
        "deleteAccount.reason.notSafe",
        "deleteAccount.reason.privacy",
        "deleteAccount.reason.other",
    };

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
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 0f))
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
        Ui.CenteredText(width, this.fonts.Title, Palette.TextPrimary, Loc.T("deleteAccount.title"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
            Ui.TextWrapped(Loc.T("deleteAccount.body"));

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.Divider(width);
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));

        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextUnformatted(Loc.T("deleteAccount.feedback.prompt"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));

        var clicked = this.kit.ChipFlow("##del_reason_", Array.ConvertAll(ReasonKeys, key => Loc.T(key)), i => this.selectedReasons.Contains(i), width);
        if (clicked >= 0 && !this.selectedReasons.Add(clicked))
            this.selectedReasons.Remove(clicked);

        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        this.kit.TextField("##del_note", ref this.note, Loc.T("deleteAccount.feedback.notePlaceholder"), width, Limits.DeleteNoteMax);

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.acknowledged = this.AckRow();

        if (this.error.Length > 0)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.Danger))
                Ui.TextWrapped(Loc.T(this.error));
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        if (this.step == Step.Deleting)
        {
            this.kit.SecondaryButton("##del_progress", Loc.T("deleteAccount.deleting"), width);
            return;
        }

        var half = (width - Ui.Px(10f)) * 0.5f;
        if (this.kit.SecondaryButton("##del_cancel", Loc.T("deleteAccount.cancel"), half))
            ImGui.CloseCurrentPopup();
        ImGui.SameLine(0f, Ui.Px(10f));
        if (this.acknowledged)
        {
            if (this.kit.DangerButton("##del_confirm", Loc.T("deleteAccount.confirm"), half))
                this.Submit();
        }
        else
        {
            // Gated: the confirm checkbox is unticked, so the destructive action is not yet available.
            this.kit.SecondaryButton("##del_confirm_off", Loc.T("deleteAccount.confirm"), half);
        }
    }

    private void DrawDone(float width)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        this.IconBadge(width, FontAwesomeIcon.Heart, this.theme.Accent);
        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        Ui.CenteredText(width, this.fonts.Title, Palette.TextPrimary, Loc.T("deleteAccount.done.title"));
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
            Ui.TextWrapped(Loc.T("deleteAccount.done.body"));

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        if (this.kit.PrimaryButton("##del_close", Loc.T("deleteAccount.done.close"), width))
            ImGui.CloseCurrentPopup();   // Draw() finalizes the sign-out once the popup is gone
    }

    // Checkbox + label confirm gate, mirroring AgeGuidelinesScreen's DrawCheck.
    private bool AckRow()
    {
        var next = this.kit.Checkbox("##del_ack", this.acknowledged);
        ImGui.SameLine(0f, Ui.Px(9f));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
            ImGui.TextUnformatted(Loc.T("deleteAccount.acknowledge"));
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
                    this.error = "deleteAccount.error.signedOut";
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
                this.error = "deleteAccount.error.failed";
                this.step = Step.Form;
            }
        });
    }

    // Center of the host window, so the modal floats inside the app rather than the game screen.
    private static Vector2 HostCenter() => ImGui.GetWindowPos() + (ImGui.GetWindowSize() * 0.5f);

    // A squared badge matching the age gate's framed icon, tinted by the state it announces.
    private void IconBadge(float contentWidth, FontAwesomeIcon icon, Vector4 color)
    {
        var box = Ui.Px(52f);
        var pos = ImGui.GetCursorScreenPos();
        var min = new Vector2(pos.X + ((contentWidth - box) * 0.5f), pos.Y);
        var max = min + new Vector2(box, box);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, max, Palette.WithAlpha(color, 0.12f).U32());
        drawList.AddRect(min, max, Palette.WithAlpha(color, 0.45f).U32(), 0f, ImDrawFlags.None, 1f);
        var glyph = icon.ToIconString();
        var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(drawList, this.fonts.Icon, min + ((new Vector2(box, box) - glyphSize) * 0.5f), color.U32(), glyph);
        ImGui.Dummy(new Vector2(contentWidth, box));
    }

    private void Divider(float width)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(new Vector2(pos.X, pos.Y), new Vector2(pos.X + width, pos.Y), Palette.Border.U32(), 1f);
        ImGui.Dummy(new Vector2(width, 1f));
    }
}
