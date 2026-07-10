using Dalamud.Interface;
using Eikon.Config;
using Eikon.Contracts;
using Eikon.Net;
using Eikon.Services;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Shared block and report flow (SCREENS section 9). A host screen calls Open or OpenReport from its
// overflow action, then calls Draw every frame so the popups render. The action menu leads to a
// block confirm dialog or a report sheet; the report sheet ends on a confirmation that also offers
// to block. Enforcement is server-side in phase C; this is the client surface.
internal sealed class ModerationFlow
{
    private static readonly string[] Reasons =
    {
        "Harassment or abuse",
        "Spam or scam",
        "Impersonation or catfishing",
        "Nonconsensual or illegal content",
        "Underage or childlike content",
        "Something else",
    };

    private static readonly ReportReasonEnum[] ReasonWire =
    {
        ReportReasonEnum.Harassment,
        ReportReasonEnum.Spam,
        ReportReasonEnum.Impersonation,
        ReportReasonEnum.NonconsensualIllegal,
        ReportReasonEnum.UnderageChildlike,
        ReportReasonEnum.Other,
    };

    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly SafetyService safety;
    private readonly ChatService chat;
    private readonly Configuration config;
    private readonly ModerationKeyService moderationKeys;
    private readonly Media media;

    private Guid targetId;
    private string target = string.Empty;
    private Vector2 menuAnchor;   // screen pos of the trigger button's bottom-right; the menu hangs from it
    private Action? onViewProfile;    // optional "View profile" row (only shown when the host provides it)
    private Action? onSharedMedia;    // optional "Shared media" row (chat only)
    private Action? onShareAlbum;     // optional "Share an album" row (chat only)
    private Action? onSafetyNumber;   // optional "Safety number" row (chat only)
    private bool chatActions;         // host is the chat screen: show the chat-wallpaper rows
    private bool openMenu, openBlock, openReport;
    private int reason = -1;
    private bool includeMessages;
    private bool submitted;

    public ModerationFlow(ThemeService theme, Kit kit, UiFonts fonts, SafetyService safety, ChatService chat, Configuration config, ModerationKeyService moderationKeys, Media media)
    {
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.safety = safety;
        this.chat = chat;
        this.config = config;
        this.moderationKeys = moderationKeys;
        this.media = media;
    }

    public void Open(Guid userId, string name, Vector2 anchor, Action? onViewProfile = null, Action? onSafetyNumber = null, bool chatActions = false, Action? onSharedMedia = null, Action? onShareAlbum = null)
    {
        this.targetId = userId;
        this.target = name;
        this.menuAnchor = anchor;
        this.onViewProfile = onViewProfile;
        this.onSharedMedia = onSharedMedia;
        this.onShareAlbum = onShareAlbum;
        this.onSafetyNumber = onSafetyNumber;
        this.chatActions = chatActions;
        this.openMenu = true;
        this.moderationKeys.EnsureLoaded();   // warm the verified seal key before a report is sealed
    }

    public void OpenReport(Guid userId, string name)
    {
        this.targetId = userId;
        this.target = name;
        this.ResetReport();
        this.openReport = true;
        this.moderationKeys.EnsureLoaded();
    }

    public void Draw()
    {
        if (this.openMenu)
        {
            this.openMenu = false;
            ImGui.OpenPopup("##mod_menu");
        }

        if (this.openBlock)
        {
            this.openBlock = false;
            ImGui.OpenPopup("##mod_block");
        }

        if (this.openReport)
        {
            this.openReport = false;
            ImGui.OpenPopup("##mod_report");
        }

        this.DrawMenu();
        this.DrawBlock();
        this.DrawReport();
    }

    private void ResetReport()
    {
        this.reason = -1;
        this.includeMessages = false;
        this.submitted = false;
    }

    // Build the sealed evidence blob for the report: a small JSON snapshot (reported user, reason,
    // time), plus the locally decrypted thread when the member opts in. Sealed to the moderation key
    // so only moderation can open it. Returns base64, or null if sealing fails.
    private string? BuildEvidence()
    {
        try
        {
            var messages = new List<object>();
            if (this.includeMessages)
                foreach (var m in this.chat.Thread(this.targetId))
                    messages.Add(new { mine = m.Mine, text = m.Text, state = m.State.ToString() });

            var snapshot = new
            {
                v = 1,
                reportedUserId = this.targetId.ToString(),
                reportedName = this.target,
                reason = this.reason >= 0 ? Reasons[this.reason] : string.Empty,
                capturedAt = DateTimeOffset.UtcNow.ToString("o"),
                includedMessages = this.includeMessages,
                messages,
            };

            var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snapshot);
            return Convert.ToBase64String(Eikon.Crypto.Crypto.Seal(this.moderationKeys.Public, json));
        }
        catch
        {
            return null;
        }
    }

    // Overflow action menu. Anchored under the trigger button and clamped inside the host window: a
    // bare BeginPopup is positioned against the whole game viewport, which is why it used to spill onto
    // the game UI. ImGui draws no shadow for popups and its square chrome was not rounding cleanly, so
    // the panel background, hairline border and soft drop shadow are drawn by hand over a transparent
    // popup. Tapping outside dismisses it (no Cancel row needed).
    private void DrawMenu()
    {
        var key = this.targetId.ToString();
        var hasBackground = this.chatActions && this.config.ChatBackgrounds.ContainsKey(key);
        var groupA = this.onViewProfile != null || this.onSharedMedia != null || this.onShareAlbum != null;
        var width = Ui.Px(206f);
        var row = Ui.Px(40f);
        var div = Ui.Px(9f);
        var height = Ui.Px(12f)                                  // window padding (top + bottom)
            + (this.onViewProfile != null ? row : 0f)
            + (this.onSharedMedia != null ? row : 0f)
            + (this.onShareAlbum != null ? row : 0f)
            + (groupA ? div : 0f)                                // divider after the view / share group
            + row                                                // mute / unmute
            + (this.chatActions ? row : 0f)                     // set / change chat background
            + (hasBackground ? row : 0f)                        // clear chat background
            + div                                                // divider before safety / block / report
            + (this.onSafetyNumber != null ? row : 0f)
            + row                                                // block
            + row;                                               // report
        var rounding = 0f;
        var winMin = ImGui.GetWindowPos();
        var winMax = winMin + ImGui.GetWindowSize();
        var x = Math.Clamp(this.menuAnchor.X - width, winMin.X + Ui.Px(8f), winMax.X - width - Ui.Px(12f));
        ImGui.SetNextWindowPos(new Vector2(x, this.menuAnchor.Y + Ui.Px(6f)), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new Vector2(width, height), ImGuiCond.Appearing);

        using (ImRaii.PushColor(ImGuiCol.PopupBg, new Vector4(0f, 0f, 0f, 0f)))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(0f, Ui.Px(6f))))
        using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 0f))
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
        {
            if (!ImGui.BeginPopup("##mod_menu"))
                return;

            var dl = ImGui.GetWindowDrawList();
            var min = ImGui.GetWindowPos();
            var max = min + ImGui.GetWindowSize();

            // Soft drop shadow. Layered rounded rects (ImGui has no blur); drawn outside the window
            // clip rect so the halo around the panel is not cut off.
            var vp = ImGui.GetMainViewport();
            dl.PushClipRect(vp.Pos, vp.Pos + vp.Size, false);
            for (var i = 6; i >= 1; i--)
            {
                var spread = i * Ui.Px(2.2f);
                var alpha = 0.18f * (1f - ((i - 1) / 6f));
                dl.AddRectFilled(
                    new Vector2(min.X - spread, min.Y - spread + Ui.Px(3f)),
                    new Vector2(max.X + spread, max.Y + spread + Ui.Px(3f)),
                    new Vector4(0f, 0f, 0f, alpha).U32(), rounding + spread);
            }
            dl.PopClipRect();

            // Panel background + hairline border.
            dl.AddRectFilled(min, max, Palette.Surface1.U32(), rounding);
            dl.AddRect(min, max, Palette.Border.U32(), rounding, ImDrawFlags.None, 1f);

            // Group 1: the things you look at (the person, the media, the albums you can share).
            if (this.onViewProfile is { } view && this.MenuRow("##mm_view", FontAwesomeIcon.UserCircle, "View profile", false, width))
            {
                ImGui.CloseCurrentPopup();
                view();
            }

            if (this.onSharedMedia is { } sharedMedia && this.MenuRow("##mm_media", FontAwesomeIcon.Images, "Shared media", false, width))
            {
                ImGui.CloseCurrentPopup();
                sharedMedia();
            }

            if (this.onShareAlbum is { } shareAlbum && this.MenuRow("##mm_album", FontAwesomeIcon.ShareAlt, "Share an album", false, width))
            {
                ImGui.CloseCurrentPopup();
                shareAlbum();
            }

            if (groupA)
                this.MenuDivider(width);

            // Group 2: per-conversation preferences.
            var muted = this.config.MutedConversations.Contains(this.targetId.ToString());
            if (this.MenuRow("##mm_mute", muted ? FontAwesomeIcon.Bell : FontAwesomeIcon.BellSlash, muted ? "Unmute notifications" : "Mute notifications", false, width))
            {
                if (muted)
                    this.config.MutedConversations.Remove(this.targetId.ToString());
                else
                    this.config.MutedConversations.Add(this.targetId.ToString());
                this.config.Save();
                ImGui.CloseCurrentPopup();
            }

            // Per-conversation chat wallpaper (chat overflow only). Picking closes the menu first, then
            // opens the file dialog - the same pick-then-act order the composer's photo attach uses, so
            // the picker never has to fight an open popup for focus.
            if (this.chatActions)
            {
                if (this.MenuRow("##mm_bg", FontAwesomeIcon.Image, hasBackground ? "Change chat background" : "Set chat background", false, width))
                {
                    ImGui.CloseCurrentPopup();
                    this.media.PickImage(p =>
                    {
                        this.config.ChatBackgrounds[key] = p;
                        this.config.Save();
                    });
                }

                if (hasBackground && this.MenuRow("##mm_bg_clear", FontAwesomeIcon.TrashAlt, "Clear chat background", false, width))
                {
                    this.config.ChatBackgrounds.Remove(key);
                    this.config.Save();
                    ImGui.CloseCurrentPopup();
                }
            }

            this.MenuDivider(width);

            // Group 3: safety and moderation.
            if (this.onSafetyNumber is { } verify && this.MenuRow("##mm_verify", FontAwesomeIcon.ShieldAlt, "Safety number", false, width))
            {
                ImGui.CloseCurrentPopup();
                verify();
            }

            if (this.MenuRow("##mm_block", FontAwesomeIcon.Ban, "Block", false, width))
            {
                ImGui.CloseCurrentPopup();
                this.openBlock = true;
            }

            if (this.MenuRow("##mm_report", FontAwesomeIcon.Flag, "Report", false, width))
            {
                ImGui.CloseCurrentPopup();
                this.ResetReport();
                this.openReport = true;
            }

            ImGui.EndPopup();
        }
    }

    // One icon + label row. Destructive rows (Block, Report) use the danger color and tint; neutral
    // rows use a faint white hover. The hover fill is inset and rounded so it clears the popup corners.
    private bool MenuRow(string id, FontAwesomeIcon icon, string label, bool destructive, float width)
    {
        var rowHeight = Ui.Px(40f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(width, rowHeight));
        var drawList = ImGui.GetWindowDrawList();

        if (ImGui.IsItemHovered())
        {
            var tint = destructive ? Palette.WithAlpha(Palette.Danger, 0.12f) : Palette.WithAlpha(Palette.White, 0.06f);
            drawList.AddRectFilled(pos + new Vector2(Ui.Px(6f), Ui.Px(1f)), pos + new Vector2(width - Ui.Px(6f), rowHeight - Ui.Px(1f)), tint.U32(), 0f);
        }

        var iconColor = (destructive ? Palette.Danger : Palette.TextSecondary).U32();
        var labelColor = (destructive ? Palette.Danger : Palette.TextPrimary).U32();
        var glyph = icon.ToIconString();
        var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(pos.X + Ui.Px(16f), pos.Y + ((rowHeight - glyphSize.Y) * 0.5f)), iconColor, glyph);
        var labelSize = Ui.Measure(this.fonts.Body, label);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(pos.X + Ui.Px(46f), pos.Y + ((rowHeight - labelSize.Y) * 0.5f)), labelColor, label);

        return clicked;
    }

    private void MenuDivider(float width)
    {
        var pos = ImGui.GetCursorScreenPos();
        var y = pos.Y + Ui.Px(4f);
        ImGui.GetWindowDrawList().AddLine(new Vector2(pos.X + Ui.Px(12f), y), new Vector2(pos.X + width - Ui.Px(12f), y), Palette.Border.U32(), 1f);
        ImGui.Dummy(new Vector2(width, Ui.Px(8f)));
    }

    private void DrawBlock()
    {
        ImGui.SetNextWindowPos(HostCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize;
        var open = true;

        using (ImRaii.PushColor(ImGuiCol.PopupBg, Palette.Surface1))
        using (ImRaii.PushColor(ImGuiCol.Border, Palette.Border))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(Ui.Px(18f), Ui.Px(18f))))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, Ui.Px(16f)))
        using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f))
        {
            if (!ImGui.BeginPopupModal("##mod_block", ref open, flags))
                return;

            var width = Ui.Px(264f);
            ImGui.Dummy(new Vector2(width, 0f));   // lock the content width; the modal auto-fits its height
            this.IconBadge(width, FontAwesomeIcon.Ban, Palette.Danger);
            ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
            Ui.CenteredText(width, this.fonts.Title, Palette.TextPrimary, $"Block {this.target}?");
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
                ImGui.TextWrapped("They won't see your profile or message you, and you won't see them. You can unblock in settings.");

            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
            var half = (width - Ui.Px(10f)) * 0.5f;
            if (this.kit.SecondaryButton("##bk_cancel", "Cancel", half))
                ImGui.CloseCurrentPopup();
            ImGui.SameLine(0f, Ui.Px(10f));
            if (this.kit.DangerButton("##bk_block", "Block", half))
            {
                this.safety.Block(this.targetId);
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawReport()
    {
        ImGui.SetNextWindowPos(HostCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize;
        var open = true;

        using (ImRaii.PushColor(ImGuiCol.PopupBg, Palette.Surface1))
        using (ImRaii.PushColor(ImGuiCol.Border, Palette.Border))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(Ui.Px(18f), Ui.Px(18f))))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, Ui.Px(16f)))
        using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f))
        {
            if (!ImGui.BeginPopupModal("##mod_report", ref open, flags))
                return;

            var width = Ui.Px(294f);
            ImGui.Dummy(new Vector2(width, 0f));   // lock the content width; the modal auto-fits its height
            if (this.submitted)
                this.DrawReportDone(width);
            else
                this.DrawReportForm(width);

            ImGui.EndPopup();
        }
    }

    private void DrawReportForm(float width)
    {
        using (this.fonts.Title.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
            ImGui.TextUnformatted($"Report {this.target}");
        ImGui.Dummy(new Vector2(0f, Ui.Px(3f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextUnformatted("Why are you reporting this?");
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));

        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Ui.Px(2f))))
            for (var i = 0; i < Reasons.Length; i++)
                this.ReasonRow(i, width);

        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        this.MenuDivider(width);
        ImGui.Dummy(new Vector2(0f, Ui.Px(4f)));
        this.includeMessages = this.IncludeRow(width);
        ImGui.Dummy(new Vector2(0f, Ui.Px(2f)));
        this.SealedNote();

        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        if (this.reason >= 0)
        {
            if (this.kit.DangerButton("##rp_submit", "Submit report", width))
            {
                this.safety.Report(this.targetId, ReasonWire[this.reason], null, this.BuildEvidence());
                this.submitted = true;
            }
        }
        else
        {
            this.kit.SecondaryButton("##rp_submit_disabled", "Submit report", width);
        }
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        if (this.kit.SecondaryButton("##rp_cancel", "Cancel", width))
            ImGui.CloseCurrentPopup();
    }

    private void DrawReportDone(float width)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        this.IconBadge(width, FontAwesomeIcon.CheckCircle, this.theme.Accent);
        ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
        Ui.CenteredText(width, this.fonts.Title, Palette.TextPrimary, "Report received");
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
            ImGui.TextWrapped("Thanks, we'll review it. You can also block this person so they can't reach you.");

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
        if (this.kit.DangerButton("##rp_block", $"Block {this.target}", width))
        {
            this.safety.Block(this.targetId);
            ImGui.CloseCurrentPopup();
        }
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
        if (this.kit.PrimaryButton("##rp_done", "Done", width))
            ImGui.CloseCurrentPopup();
    }

    // Center of the host window (the app), so modals float inside the app rather than the game screen.
    private static Vector2 HostCenter() => ImGui.GetWindowPos() + (ImGui.GetWindowSize() * 0.5f);

    // Centered tinted circle with an icon, used as the header glyph for the block/report dialogs.
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

    // Lock glyph + the sealed-evidence note, the icon aligned to the first line.
    private void SealedNote()
    {
        var lockGlyph = FontAwesomeIcon.Lock.ToIconString();
        var lockSize = Ui.Measure(this.fonts.Icon, lockGlyph);
        var pos = ImGui.GetCursorScreenPos();
        Ui.TextAt(ImGui.GetWindowDrawList(), this.fonts.Icon, new Vector2(pos.X, pos.Y + Ui.Px(1f)), Palette.TextMuted.U32(), lockGlyph);
        var indent = lockSize.X + Ui.Px(7f);
        ImGui.Indent(indent);
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextWrapped("Sealed for review. A moderator can read them without ongoing access to your chats.");
        ImGui.Unindent(indent);
    }

    private void ReasonRow(int index, float width)
    {
        var rowHeight = Ui.Px(40f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##rr" + index, new Vector2(width, rowHeight));
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();
        var selected = this.reason == index;
        var rounding = Ui.Px(10f);

        // Selection follows the theme accent; red stays reserved for the destructive Submit button.
        if (selected)
        {
            drawList.AddRectFilled(pos, pos + new Vector2(width, rowHeight), Palette.WithAlpha(this.theme.Accent, 0.12f).U32(), rounding);
            drawList.AddRect(pos, pos + new Vector2(width, rowHeight), Palette.WithAlpha(this.theme.Accent, 0.5f).U32(), rounding, ImDrawFlags.None, 1f);
        }
        else if (hovered)
        {
            drawList.AddRectFilled(pos, pos + new Vector2(width, rowHeight), Palette.WithAlpha(Palette.White, 0.05f).U32(), rounding);
        }

        var center = new Vector2(pos.X + Ui.Px(20f), pos.Y + (rowHeight * 0.5f));
        drawList.AddCircle(center, Ui.Px(9f), (selected ? this.theme.Secondary.Base : Palette.TextMuted).U32(), 16, Ui.Px(1.5f));
        if (selected)
            drawList.AddCircleFilled(center, Ui.Px(4.5f), this.theme.Secondary.Base.U32(), 12);

        var labelSize = Ui.Measure(this.fonts.Body, Reasons[index]);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(pos.X + Ui.Px(38f), center.Y - (labelSize.Y * 0.5f)), Palette.TextPrimary.U32(), Reasons[index]);

        if (clicked)
            this.reason = index;
    }

    private bool IncludeRow(float width)
    {
        var rowHeight = Ui.Px(34f);
        var localStart = ImGui.GetCursorPos();
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        const string label = "Include recent messages";
        var labelSize = Ui.Measure(this.fonts.Body, label);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(pos.X, pos.Y + ((rowHeight - labelSize.Y) * 0.5f)), Palette.TextPrimary.U32(), label);

        var toggleWidth = Ui.Px(38f);
        var toggleHeight = Ui.Px(22f);
        ImGui.SetCursorScreenPos(new Vector2(pos.X + width - toggleWidth, pos.Y + ((rowHeight - toggleHeight) * 0.5f)));
        var next = this.kit.Toggle("##rp_include", this.includeMessages);

        ImGui.SetCursorPos(new Vector2(localStart.X, localStart.Y + rowHeight));
        return next;
    }
}
