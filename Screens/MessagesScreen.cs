using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Messages inbox (warm-editorial). An "Inbox / Messages" header over conversation rows backed by
// /api/conversations (metadata only; the relay can't read contents). Previews come from the locally
// decrypted thread. Message requests get their own labelled group. Tapping a row opens the chat.
internal sealed class MessagesScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly InboxService inbox;
    private readonly Selection selection;
    private readonly PhotoService photoSvc;

    public MessagesScreen(ScreenRouter router, Kit kit, UiFonts fonts, InboxService inbox, Selection selection, PhotoService photoSvc)
    {
        this.router = router;
        this.kit = kit;
        this.fonts = fonts;
        this.inbox = inbox;
        this.selection = selection;
        this.photoSvc = photoSvc;
    }

    public Screen Id => Screen.Messages;

    public bool Chrome => true;

    public void Draw()
    {
        var fullWidth = ImGui.GetContentRegionAvail().X;
        this.inbox.EnsureLoaded();
        var conversations = this.inbox.Conversations;
        var threads = conversations.Where(c => !c.IsRequest).ToList();
        var requests = conversations.Where(c => c.IsRequest).ToList();

        this.DrawHeader(fullWidth, threads.Count);

        if (conversations.Count == 0)
        {
            if (!this.inbox.Loaded)
            {
                ImGui.Dummy(new Vector2(0f, Ui.Px(40f)));
                Ui.CenteredText(fullWidth, this.fonts.Caption, Palette.TextMuted, "Loading…");
                return;
            }

            ImGui.Dummy(new Vector2(0f, Ui.Px(48f)));
            this.kit.EmptyState(FontAwesomeIcon.CommentDots.ToIconString(), "No messages yet", "Say hi to someone from the grid.", fullWidth);
            var buttonWidth = Ui.Px(180f);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((fullWidth - buttonWidth) * 0.5f));
            if (this.kit.PrimaryButton("##empty_browse", "Browse the grid", buttonWidth))
                this.router.Navigate(Screen.Grid);
            return;
        }

        using var scroll = ImRaii.Child("inbox_scroll", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.NoScrollbar);
        if (!scroll.Success)
            return;

        var pad = Ui.Px(20f);
        if (requests.Count > 0)
        {
            this.SectionEyebrow($"Requests · {requests.Count}", pad);
            foreach (var request in requests)
                if (this.DrawRow(request, fullWidth, pad))
                    this.Open(request);
            ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        }

        foreach (var conversation in threads)
            if (this.DrawRow(conversation, fullWidth, pad))
                this.Open(conversation);
    }

    private void DrawHeader(float fullWidth, int count)
    {
        var pad = Ui.Px(20f);
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        var y = origin.Y + Ui.Px(18f);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(origin.X + pad, y), Palette.TextSecondary.U32(), "INBOX");
        y += Ui.Measure(this.fonts.Eyebrow, "X").Y + Ui.Px(4f);

        var titleSize = Ui.Measure(this.fonts.SerifTitle, "Messages");
        Ui.TextAt(dl, this.fonts.SerifTitle, new Vector2(origin.X + pad, y), Palette.TextPrimary.U32(), "Messages");
        var countStr = count.ToString("D2");
        var countSize = Ui.Measure(this.fonts.Mono, countStr);
        Ui.TextAt(dl, this.fonts.Mono, new Vector2((origin.X + fullWidth - pad) - countSize.X, (y + titleSize.Y) - countSize.Y), Palette.TextMuted.U32(), countStr);

        var bottom = y + titleSize.Y + Ui.Px(16f);
        dl.AddLine(new Vector2(origin.X, bottom), new Vector2(origin.X + fullWidth, bottom), Palette.Border.U32(), 1f);
        ImGui.SetCursorScreenPos(new Vector2(origin.X, bottom + 1f));
    }

    private void SectionEyebrow(string text, float pad)
    {
        var pos = ImGui.GetCursorScreenPos();
        Ui.TextAt(ImGui.GetWindowDrawList(), this.fonts.Eyebrow, new Vector2(pos.X + pad, pos.Y + Ui.Px(16f)), Palette.TextSecondary.U32(), text.ToUpperInvariant());
        ImGui.Dummy(new Vector2(0f, Ui.Px(16f) + Ui.Measure(this.fonts.Eyebrow, "X").Y + Ui.Px(8f)));
    }

    private void Open(ConversationSummaryDto conversation)
    {
        this.selection.ProfileUserId = conversation.UserId;
        this.selection.ProfileDisplayName = conversation.DisplayName;
        this.router.Navigate(Screen.Chat);
    }

    private bool DrawRow(ConversationSummaryDto conversation, float fullWidth, float pad)
    {
        var rowHeight = Ui.Px(72f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##conv_" + conversation.UserId, new Vector2(fullWidth, rowHeight));
        var dl = ImGui.GetWindowDrawList();
        if (ImGui.IsItemHovered())
            dl.AddRectFilled(pos, pos + new Vector2(fullWidth, rowHeight), Palette.WithAlpha(Palette.White, 0.04f).U32());

        var av = Ui.Px(48f);
        var amin = new Vector2(pos.X + pad, pos.Y + ((rowHeight - av) * 0.5f));
        this.DrawAvatar(dl, amin, av, conversation.MainPhotoId, Initial(conversation.DisplayName));
        var dotColor = (conversation.Online ? Palette.Online : Palette.Afk).U32();
        var dotCenter = new Vector2((amin.X + av) - Ui.Px(4f), (amin.Y + av) - Ui.Px(4f));
        dl.AddCircleFilled(dotCenter, Ui.Px(5f), dotColor, 12);
        dl.AddCircle(dotCenter, Ui.Px(5f), Palette.Bg.U32(), 12, Ui.Px(2f));

        var textX = amin.X + av + Ui.Px(14f);
        var textRight = (pos.X + fullWidth) - pad;

        var time = Ago(conversation.LastMessageAt);
        var timeWidth = 0f;
        if (time.Length > 0)
        {
            var timeSize = Ui.Measure(this.fonts.Mono, time);
            timeWidth = timeSize.X + Ui.Px(10f);
            Ui.TextAt(dl, this.fonts.Mono, new Vector2(textRight - timeSize.X, pos.Y + Ui.Px(17f)), Palette.TextMuted.U32(), time);
        }

        var name = this.Fit(conversation.DisplayName, textRight - textX - timeWidth, this.fonts.SerifName);
        Ui.TextAt(dl, this.fonts.SerifName, new Vector2(textX, pos.Y + Ui.Px(13f)), Palette.TextPrimary.U32(), name);

        var unread = conversation.Unread > 0;
        var badgeRight = textRight;
        if (unread)
        {
            var badge = conversation.Unread > 99 ? "99+" : conversation.Unread.ToString();
            var badgeText = Ui.Measure(this.fonts.Mono, badge);
            var badgeWidth = MathF.Max(Ui.Px(16f), badgeText.X + Ui.Px(8f));
            var badgeHeight = Ui.Px(16f);
            var badgePos = new Vector2(textRight - badgeWidth, pos.Y + Ui.Px(44f));
            dl.AddRectFilled(badgePos, badgePos + new Vector2(badgeWidth, badgeHeight), Palette.Signal.U32(), badgeHeight * 0.5f);
            Ui.TextAt(dl, this.fonts.Mono, new Vector2(badgePos.X + ((badgeWidth - badgeText.X) * 0.5f), badgePos.Y + ((badgeHeight - badgeText.Y) * 0.5f)), Palette.Paper.U32(), badge);
            badgeRight = badgePos.X - Ui.Px(8f);
        }

        var previewColor = (unread ? Palette.TextPrimary : Palette.TextMuted).U32();
        var preview = this.Fit(this.Preview(conversation), badgeRight - textX, this.fonts.Caption);
        Ui.TextAt(dl, this.fonts.Caption, new Vector2(textX, pos.Y + Ui.Px(44f)), previewColor, preview);

        dl.AddLine(new Vector2(pos.X, pos.Y + rowHeight), new Vector2(pos.X + fullWidth, pos.Y + rowHeight), Palette.Border.U32(), 1f);
        return clicked;
    }

    private string Preview(ConversationSummaryDto conversation)
    {
        var last = this.inbox.Preview(conversation.UserId);
        if (last is not null)
            return last.Mine ? "You: " + OneLine(last.Text) : OneLine(last.Text);
        return conversation.Unread > 0 ? "New message" : "Say hi";
    }

    private static string OneLine(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private string Fit(string text, float maxWidth, IFontHandle font)
    {
        if (maxWidth <= 0f || Ui.Measure(font, text).X <= maxWidth)
            return text;
        const string ellipsis = "…";
        var ellipsisWidth = Ui.Measure(font, ellipsis).X;
        var n = text.Length;
        while (n > 0 && Ui.Measure(font, text[..n]).X + ellipsisWidth > maxWidth)
            n--;
        return text[..n].TrimEnd() + ellipsis;
    }

    private void DrawAvatar(ImDrawListPtr dl, Vector2 min, float size, Guid? photoId, string initial)
    {
        var max = min + new Vector2(size, size);
        var texture = photoId is { } id ? this.photoSvc.Texture(id) : null;
        if (texture != null)
        {
            var (uvMin, uvMax) = Ui.CoverUv(texture.Width, texture.Height, 1f);
            dl.AddImage(texture.Handle, min, max, uvMin, uvMax);
            return;
        }

        dl.AddRectFilled(min, max, Palette.Surface2.U32());
        var initialSize = Ui.Measure(this.fonts.SerifName, initial);
        Ui.TextAt(dl, this.fonts.SerifName, ((min + max) * 0.5f) - (initialSize * 0.5f), Palette.TextSecondary.U32(), initial);
    }

    private static string Initial(string name) => name.Length > 0 ? name[..1].ToUpperInvariant() : "?";

    private static string Ago(DateTimeOffset? at)
    {
        if (at is null)
            return string.Empty;
        var d = DateTimeOffset.UtcNow - at.Value;
        if (d.TotalMinutes < 1) return "now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h";
        if (d.TotalDays < 7) return $"{(int)d.TotalDays}d";
        return at.Value.LocalDateTime.ToString("MMM d");
    }
}
