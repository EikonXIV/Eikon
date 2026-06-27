using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Blocked users (Settings > Blocked users). Lists the people you've blocked, each with an Unblock
// action that confirms first. Backed by /api/blocks; unblocking hits /api/unblock and refreshes.
internal sealed class BlockedUsersScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly BlockedService blocked;
    private readonly SafetyService safety;
    private readonly PhotoService photoSvc;

    private Guid pendingId;
    private string pendingName = string.Empty;
    private bool openConfirm;

    public BlockedUsersScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, BlockedService blocked, SafetyService safety, PhotoService photoSvc)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.blocked = blocked;
        this.safety = safety;
        this.photoSvc = photoSvc;
    }

    public Screen Id => Screen.Blocked;

    public bool Chrome => false;

    public void Draw()
    {
        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(16f);
        var headerHeight = Ui.Px(52f);
        var contentWidth = avail.X - (pad * 2f);

        this.DrawHeader(avail.X, pad, headerHeight);

        this.blocked.EnsureLoaded();
        var users = this.blocked.Users;

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("blocked_body", new Vector2(avail.X, avail.Y - headerHeight)))
        {
            if (body.Success)
            {
                ImGui.Indent(pad);
                if (users.Count == 0)
                {
                    this.DrawEmptyOrLoading(contentWidth);
                }
                else
                {
                    ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
                    using (this.fonts.Caption.Push())
                    using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
                    {
                        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentWidth);
                        ImGui.TextWrapped("People you block can't see your profile or message you. Unblock to allow contact again.");
                        ImGui.PopTextWrapPos();
                    }
                    ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
                    foreach (var u in users)
                        this.DrawRow(u, contentWidth);
                }
                ImGui.Unindent(pad);
            }
        }

        this.DrawConfirm();
    }

    private void DrawEmptyOrLoading(float contentWidth)
    {
        if (!this.blocked.Loaded)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(40f)));
            Ui.CenteredText(contentWidth, this.fonts.Caption, Palette.TextMuted, "Loading...");
            return;
        }
        ImGui.Dummy(new Vector2(0f, Ui.Px(60f)));
        this.kit.EmptyState(FontAwesomeIcon.ShieldAlt.ToIconString(), "No one blocked", "People you block from a profile or chat show up here so you can unblock them.", contentWidth);
    }

    private void DrawRow(User u, float width)
    {
        var rowHeight = Ui.Px(56f);
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var radius = Ui.Px(20f);
        var center = new Vector2(pos.X + radius, pos.Y + (rowHeight * 0.5f));
        this.DrawAvatar(drawList, center, radius, u.MainPhotoId, Initial(u.DisplayName));

        var textX = pos.X + (radius * 2f) + Ui.Px(12f);
        var nameSize = Ui.Measure(this.fonts.Body, u.DisplayName);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(textX, center.Y - (nameSize.Y * 0.5f)), Palette.TextPrimary.U32(), u.DisplayName);

        // Right-aligned Unblock pill. Neutral styling: unblocking is a restore, not a destructive action.
        const string label = "Unblock";
        var labelSize = Ui.Measure(this.fonts.Caption, label);
        var btnW = labelSize.X + Ui.Px(24f);
        var btnH = Ui.Px(30f);
        var btnPos = new Vector2(pos.X + width - btnW, pos.Y + ((rowHeight - btnH) * 0.5f));
        ImGui.SetCursorScreenPos(btnPos);
        var clicked = ImGui.InvisibleButton("##unbl_" + u.UserId, new Vector2(btnW, btnH));
        drawList.AddRectFilled(btnPos, btnPos + new Vector2(btnW, btnH), (ImGui.IsItemHovered() ? Palette.Surface1 : Palette.Surface2).U32(), Ui.Px(8f));
        drawList.AddRect(btnPos, btnPos + new Vector2(btnW, btnH), Palette.Border.U32(), Ui.Px(8f), ImDrawFlags.None, 1f);
        Ui.TextAt(drawList, this.fonts.Caption, btnPos + new Vector2(Ui.Px(12f), (btnH - labelSize.Y) * 0.5f), Palette.TextPrimary.U32(), label);
        if (clicked)
        {
            this.pendingId = u.UserId;
            this.pendingName = u.DisplayName;
            this.openConfirm = true;
        }

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(width, rowHeight));
        drawList.AddLine(new Vector2(pos.X, pos.Y + rowHeight), new Vector2(pos.X + width, pos.Y + rowHeight), Palette.Border.U32(), 1f);
    }

    private void DrawConfirm()
    {
        if (this.openConfirm)
        {
            this.openConfirm = false;
            ImGui.OpenPopup("##unblock_confirm");
        }

        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + (ImGui.GetWindowSize() * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize;
        var open = true;

        using (ImRaii.PushColor(ImGuiCol.PopupBg, Palette.Surface1))
        using (ImRaii.PushColor(ImGuiCol.Border, Palette.Border))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(Ui.Px(18f), Ui.Px(18f))))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, Ui.Px(16f)))
        using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f))
        {
            if (!ImGui.BeginPopupModal("##unblock_confirm", ref open, flags))
                return;

            var width = Ui.Px(264f);
            ImGui.Dummy(new Vector2(width, 0f));
            Ui.CenteredText(width, this.fonts.Title, Palette.TextPrimary, $"Unblock {this.pendingName}?");
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
                ImGui.TextWrapped("They'll be able to see your profile and message you again. You can block them again any time.");

            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
            var half = (width - Ui.Px(10f)) * 0.5f;
            if (this.kit.SecondaryButton("##unbl_cancel", "Cancel", half))
                ImGui.CloseCurrentPopup();
            ImGui.SameLine(0f, Ui.Px(10f));
            if (this.kit.PrimaryButton("##unbl_ok", "Unblock", half))
            {
                this.safety.Unblock(this.pendingId);
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawHeader(float fullWidth, float pad, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var midY = origin.Y + (height * 0.5f);

        var backGlyph = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, backGlyph);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##blocked_back", backSize))
            this.router.Navigate(Screen.Settings);
        Ui.TextAt(drawList, this.fonts.Icon, ImGui.GetItemRectMin(), Palette.TextSecondary.U32(), backGlyph);

        const string title = "Blocked users";
        var titleSize = Ui.Measure(this.fonts.Body, title);
        Ui.TextAt(drawList, this.fonts.Body,
            new Vector2(origin.X + pad + backSize.X + Ui.Px(12f), midY - (titleSize.Y * 0.5f)),
            Palette.TextPrimary.U32(), title);

        drawList.AddLine(new Vector2(origin.X, origin.Y + height), new Vector2(origin.X + fullWidth, origin.Y + height), Palette.Border.U32(), 1f);
    }

    private void DrawAvatar(ImDrawListPtr drawList, Vector2 center, float radius, Guid? photoId, string initial)
    {
        var texture = photoId is { } id ? this.photoSvc.Texture(id) : null;
        if (texture != null)
        {
            var (uvMin, uvMax) = Ui.CoverUv(texture.Width, texture.Height, 1f);
            drawList.AddImageRounded(texture.Handle, center - new Vector2(radius, radius), center + new Vector2(radius, radius), uvMin, uvMax, 0xFFFFFFFFu, radius);
            return;
        }
        drawList.AddCircleFilled(center, radius, Palette.Surface2.U32(), 24);
        var initialSize = Ui.Measure(this.fonts.Body, initial);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(center.X - (initialSize.X * 0.5f), center.Y - (initialSize.Y * 0.5f)), Palette.TextSecondary.U32(), initial);
    }

    private static string Initial(string name) => name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
}
