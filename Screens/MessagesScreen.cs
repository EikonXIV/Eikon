using Dalamud.Interface;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Messages inbox. A list of conversation rows backed by /api/conversations (metadata only; the relay
// can't read contents). The preview line comes from the locally decrypted thread. Tapping a row opens
// the chat with that peer. An online-now strip appears once presence is wired.
internal sealed class MessagesScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly InboxService inbox;
    private readonly Selection selection;
    private readonly PhotoService photoSvc;

    public MessagesScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, InboxService inbox, Selection selection, PhotoService photoSvc)
    {
        this.router = router;
        this.theme = theme;
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
        var contentWidth = ImGui.GetContentRegionAvail().X - Ui.Px(16f);
        this.inbox.EnsureLoaded();
        var conversations = this.inbox.Conversations;

        if (conversations.Count == 0)
        {
            if (!this.inbox.Loaded)
            {
                ImGui.Dummy(new Vector2(0f, Ui.Px(40f)));
                Ui.CenteredText(contentWidth, this.fonts.Caption, Palette.TextMuted, "Loading...");
                return;
            }

            ImGui.Dummy(new Vector2(0f, Ui.Px(56f)));
            this.kit.EmptyState(FontAwesomeIcon.CommentDots.ToIconString(), "No messages yet", "Say hi to someone from the grid.", contentWidth);
            var buttonWidth = Ui.Px(180f);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((contentWidth - buttonWidth) * 0.5f));
            if (this.kit.PrimaryButton("##empty_browse", "Browse the grid", buttonWidth))
                this.router.Navigate(Screen.Grid);
            return;
        }

        var requests = conversations.Where(c => c.IsRequest).ToList();
        var threads = conversations.Where(c => !c.IsRequest).ToList();

        // Message requests: people you haven't replied to yet. Replying accepts; you can also block.
        if (requests.Count > 0)
        {
            this.kit.SectionLabel($"Requests ({requests.Count})");
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            foreach (var request in requests)
                if (this.DrawRow(request, contentWidth))
                    this.Open(request);
            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        }

        var online = threads.Where(c => c.Online).ToList();
        if (online.Count > 0)
        {
            this.kit.SectionLabel("Online now");
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            this.DrawOnlineStrip(online);
            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
        }

        foreach (var conversation in threads)
            if (this.DrawRow(conversation, contentWidth))
                this.Open(conversation);
    }

    private void Open(ConversationSummaryDto conversation)
    {
        this.selection.ProfileUserId = conversation.UserId;
        this.selection.ProfileDisplayName = conversation.DisplayName;
        this.router.Navigate(Screen.Chat);
    }

    private void DrawOnlineStrip(IReadOnlyList<ConversationSummaryDto> online)
    {
        var first = true;
        foreach (var conversation in online)
        {
            if (!first)
                ImGui.SameLine(0f, Ui.Px(12f));
            first = false;
            if (this.DrawAvatarChip(conversation))
                this.Open(conversation);
        }
    }

    private bool DrawAvatarChip(ConversationSummaryDto conversation)
    {
        var avatar = Ui.Px(44f);
        var name = conversation.DisplayName;
        var nameSize = Ui.Measure(this.fonts.Caption, name);
        var width = MathF.Max(avatar, nameSize.X);
        var size = new Vector2(width, avatar + Ui.Px(4f) + nameSize.Y);

        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##on_" + conversation.UserId, size);
        var drawList = ImGui.GetWindowDrawList();
        var centerX = pos.X + (width * 0.5f);
        var avatarCenter = new Vector2(centerX, pos.Y + (avatar * 0.5f));

        this.DrawAvatar(drawList, avatarCenter, avatar * 0.5f, conversation.MainPhotoId, Initial(name));

        var dot = new Vector2(centerX + (avatar * 0.5f) - Ui.Px(5f), pos.Y + avatar - Ui.Px(6f));
        drawList.AddCircleFilled(dot, Ui.Px(5f), this.theme.Accent.U32(), 12);
        drawList.AddCircle(dot, Ui.Px(5f), Palette.Bg.U32(), 12, Ui.Px(1.5f));

        Ui.TextAt(drawList, this.fonts.Caption,
            new Vector2(centerX - (nameSize.X * 0.5f), pos.Y + avatar + Ui.Px(4f)),
            Palette.TextSecondary.U32(), name);

        return clicked;
    }

    private bool DrawRow(ConversationSummaryDto conversation, float width)
    {
        var rowHeight = Ui.Px(60f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##conv_" + conversation.UserId, new Vector2(width, rowHeight));
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();

        // Faint rounded fill on hover, painted behind the avatar and text, so the list reads as tappable
        // rows to match the hover feedback elsewhere (header controls, the chat peer tap).
        if (hovered)
            drawList.AddRectFilled(pos, pos + new Vector2(width, rowHeight), Palette.WithAlpha(Palette.White, 0.045f).U32(), Ui.Px(12f));

        var radius = Ui.Px(23f);
        var avatarCenter = new Vector2(pos.X + radius, pos.Y + (rowHeight * 0.5f));
        this.DrawAvatar(drawList, avatarCenter, radius, conversation.MainPhotoId, Initial(conversation.DisplayName));
        if (conversation.Online)
        {
            var dot = new Vector2(avatarCenter.X + radius - Ui.Px(4f), avatarCenter.Y + radius - Ui.Px(4f));
            drawList.AddCircleFilled(dot, Ui.Px(5f), this.theme.Accent.U32(), 12);
            drawList.AddCircle(dot, Ui.Px(5f), Palette.Bg.U32(), 12, Ui.Px(1.5f));
        }

        var textX = pos.X + (radius * 2f) + Ui.Px(12f);
        var nameSize = Ui.Measure(this.fonts.Body, conversation.DisplayName);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(textX, pos.Y + Ui.Px(12f)), Palette.TextPrimary.U32(), conversation.DisplayName);
        if (conversation.Verified)
        {
            var glyph = FontAwesomeIcon.CheckCircle.ToIconString();
            var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
            Ui.TextAt(drawList, this.fonts.Icon,
                new Vector2(textX + nameSize.X + Ui.Px(6f), pos.Y + Ui.Px(12f) + ((nameSize.Y - glyphSize.Y) * 0.5f)),
                this.theme.Accent.U32(), glyph);
        }

        var unread = conversation.Unread > 0;
        var previewColor = (unread ? Palette.TextSecondary : Palette.TextMuted).U32();
        var preview = this.Fit(this.Preview(conversation), (pos.X + width) - textX - Ui.Px(44f));
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(textX, pos.Y + Ui.Px(34f)), previewColor, preview);

        var time = Ago(conversation.LastMessageAt);
        if (time.Length > 0)
        {
            var timeSize = Ui.Measure(this.fonts.Caption, time);
            var timeColor = (unread ? this.theme.Accent : Palette.TextMuted).U32();
            Ui.TextAt(drawList, this.fonts.Caption, new Vector2(pos.X + width - timeSize.X, pos.Y + Ui.Px(12f)), timeColor, time);
        }

        if (unread)
        {
            var badge = conversation.Unread.ToString();
            var badgeTextSize = Ui.Measure(this.fonts.Caption, badge);
            var badgeWidth = MathF.Max(Ui.Px(18f), badgeTextSize.X + Ui.Px(8f));
            var badgeHeight = Ui.Px(18f);
            var badgePos = new Vector2(pos.X + width - badgeWidth, pos.Y + Ui.Px(34f));
            drawList.AddRectFilled(badgePos, badgePos + new Vector2(badgeWidth, badgeHeight), this.theme.AccentDeep.U32(), badgeHeight * 0.5f);
            Ui.TextAt(drawList, this.fonts.Caption,
                new Vector2(badgePos.X + ((badgeWidth - badgeTextSize.X) * 0.5f), badgePos.Y + ((badgeHeight - badgeTextSize.Y) * 0.5f)),
                this.theme.OnAccent.U32(), badge);
        }

        drawList.AddLine(new Vector2(pos.X, pos.Y + rowHeight), new Vector2(pos.X + width, pos.Y + rowHeight), Palette.Border.U32(), 1f);
        return clicked;
    }

    private string Preview(ConversationSummaryDto conversation)
    {
        var last = this.inbox.Preview(conversation.UserId);
        if (last is not null)
            return last.Mine ? "You: " + OneLine(last.Text) : OneLine(last.Text);
        return conversation.Unread > 0 ? "New message" : "Say hi";
    }

    // Flatten newlines/whitespace runs to single spaces so a multi-line message previews on one line.
    private static string OneLine(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // Truncate with an ellipsis to fit the row, so a long preview doesn't run under the time and badge.
    private string Fit(string text, float maxWidth)
    {
        if (maxWidth <= 0f || Ui.Measure(this.fonts.Caption, text).X <= maxWidth)
            return text;
        const string ellipsis = "...";
        var ellipsisWidth = Ui.Measure(this.fonts.Caption, ellipsis).X;
        var n = text.Length;
        while (n > 0 && Ui.Measure(this.fonts.Caption, text[..n]).X + ellipsisWidth > maxWidth)
            n--;
        return text[..n].TrimEnd() + ellipsis;
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
        Ui.TextAt(drawList, this.fonts.Body,
            new Vector2(center.X - (initialSize.X * 0.5f), center.Y - (initialSize.Y * 0.5f)),
            Palette.TextSecondary.U32(), initial);
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
