using Dalamud.Interface;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Shared media: every image exchanged in a conversation, in one place. Built from the full local
// thread (ChatService keeps the whole history), filterable by sender, NSFW images stay gated until
// tapped, and tapping a photo opens the shared lightbox. Reached from the chat overflow menu.
internal sealed class SharedMediaScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly ChatService chat;
    private readonly ChatMediaCache mediaCache;
    private readonly Lightbox lightbox;
    private readonly Selection selection;

    private Guid forPeer;                                  // peer the filter/reveal state belongs to
    private int filter;                                   // 0 = all, 1 = theirs, 2 = mine
    private readonly HashSet<string> revealed = new();    // NSFW image ids the viewer chose to reveal

    public SharedMediaScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, ChatService chat, ChatMediaCache mediaCache, Lightbox lightbox, Selection selection)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.chat = chat;
        this.mediaCache = mediaCache;
        this.lightbox = lightbox;
        this.selection = selection;
    }

    public Screen Id => Screen.SharedMedia;

    public bool Chrome => false;

    public void Draw()
    {
        var peer = this.selection.ProfileUserId;
        if (peer is null)
        {
            this.router.Navigate(Screen.Messages);
            return;
        }

        if (this.forPeer != peer.Value)
        {
            this.forPeer = peer.Value;
            this.filter = 0;
            this.revealed.Clear();
        }

        var name = this.selection.ProfileDisplayName ?? string.Empty;

        // Newest first, so the most recent photos are on top. The thread is the full conversation.
        var images = new List<ChatService.Message>();
        var thread = this.chat.Thread(peer.Value);
        for (var i = thread.Count - 1; i >= 0; i--)
            if (thread[i].IsImage && thread[i].ImageId != null)
                images.Add(thread[i]);

        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(16f);
        var headerHeight = Ui.Px(54f);
        this.DrawHeader(avail.X, pad, name, images.Count);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("sm_body", new Vector2(avail.X, avail.Y - headerHeight)))
        {
            if (body.Success)
            {
                ImGui.Indent(pad);
                var contentWidth = avail.X - (pad * 2f);

                ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
                var labels = new[] { "All", name.Length > 0 ? name : "Them", "You" };
                this.filter = this.kit.Segmented("##sm_filter", labels, this.filter, contentWidth);
                ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));

                var shown = this.DrawSections(images, contentWidth);
                if (shown == 0)
                    this.DrawEmpty(images.Count, name, contentWidth);

                ImGui.Unindent(pad);
            }
        }

        this.lightbox.Draw();
    }

    private void DrawHeader(float fullWidth, float pad, string name, int total)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var midY = origin.Y + Ui.Px(27f);

        var backGlyph = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, backGlyph);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##sm_back", backSize))
            this.router.Navigate(Screen.Chat);
        Ui.TextAt(drawList, this.fonts.Icon, ImGui.GetItemRectMin(), Palette.TextSecondary.U32(), backGlyph);

        var textX = origin.X + pad + backSize.X + Ui.Px(12f);
        const string title = "Shared media";
        var titleSize = Ui.Measure(this.fonts.Body, title);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(textX, midY - titleSize.Y), Palette.TextPrimary.U32(), title);
        var sub = total == 1 ? $"with {name} · 1 photo" : $"with {name} · {total} photos";
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(textX, midY + Ui.Px(2f)), Palette.TextMuted.U32(), sub);

        drawList.AddLine(new Vector2(origin.X, origin.Y + Ui.Px(53f)), new Vector2(origin.X + fullWidth, origin.Y + Ui.Px(53f)), Palette.Border.U32(), 1f);
    }

    // Walk the newest-first list, opening a new dated section whenever the day changes. Messages with
    // no timestamp (sent before this field existed) share one trailing "Earlier" section.
    private int DrawSections(List<ChatService.Message> images, float contentWidth)
    {
        const int columns = 3;
        var gap = Ui.Px(4f);
        var tileWidth = (contentWidth - (gap * (columns - 1))) / columns;
        var size = new Vector2(tileWidth, tileWidth);

        var shown = 0;
        var col = 0;
        object? currentKey = null;
        var first = true;

        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap)))
        {
            foreach (var message in images)
            {
                if (!this.Matches(message))
                    continue;

                var key = DayKey(message.SentAt);
                if (first || !Equals(key, currentKey))
                {
                    currentKey = key;
                    col = 0;
                    this.DrawSectionHeader(DayLabel(message.SentAt), key is string, first, contentWidth);
                    first = false;
                }

                if (col % columns != 0)
                    ImGui.SameLine(0f, gap);
                this.DrawTile(message, size);
                col++;
                shown++;
            }
        }

        return shown;
    }

    private void DrawSectionHeader(string label, bool earlier, bool first, float contentWidth)
    {
        if (!first)
            ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));

        // The untimestamped history is split off by a divider so it reads as "older, undated".
        if (earlier)
        {
            var p = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddLine(new Vector2(p.X, p.Y), new Vector2(p.X + contentWidth, p.Y), Palette.Border.U32(), 1f);
            ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
        }

        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
            ImGui.TextUnformatted(label);
        ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
    }

    // A grouping key: the local calendar day for a timestamped message, or a sentinel string for the
    // untimestamped "Earlier" bucket (so all null timestamps collapse into one section).
    private static object DayKey(DateTimeOffset? sentAt)
        => sentAt is { } t ? t.ToLocalTime().Date : "earlier";

    private static string DayLabel(DateTimeOffset? sentAt)
    {
        if (sentAt is not { } t)
            return "Earlier";
        var day = t.ToLocalTime().Date;
        var today = DateTime.Today;
        if (day == today)
            return "Today";
        if (day == today.AddDays(-1))
            return "Yesterday";
        if (day > today.AddDays(-7))
            return day.ToString("dddd");
        return day.Year == today.Year ? day.ToString("MMM d") : day.ToString("MMM d, yyyy");
    }

    private bool Matches(ChatService.Message m) => this.filter switch
    {
        1 => !m.Mine,
        2 => m.Mine,
        _ => true,
    };

    private void DrawTile(ChatService.Message message, Vector2 size)
    {
        var id = message.ImageId!;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##sm_" + id, size);
        var drawList = ImGui.GetWindowDrawList();
        var rounding = Ui.Px(8f);

        var blurred = message.Nsfw && !this.revealed.Contains(id);
        var texture = blurred ? null : this.mediaCache.Texture(id);

        drawList.AddRectFilled(pos, pos + size, Palette.Surface2.U32(), rounding);
        if (texture != null)
        {
            var (uvMin, uvMax) = Ui.CoverUv(texture.Width, texture.Height, size.X / size.Y);
            drawList.AddImageRounded(texture.Handle, pos, pos + size, uvMin, uvMax, 0xFFFFFFFFu, rounding);
        }
        else if (blurred)
        {
            var center = pos + (size * 0.5f);
            var eye = FontAwesomeIcon.EyeSlash.ToIconString();
            var es = Ui.Measure(this.fonts.Icon, eye);
            Ui.TextAt(drawList, this.fonts.Icon, new Vector2(center.X - (es.X * 0.5f), center.Y - es.Y), Palette.TextSecondary.U32(), eye);
            const string label = "NSFW";
            var ls = Ui.Measure(this.fonts.Caption, label);
            Ui.TextAt(drawList, this.fonts.Caption, new Vector2(center.X - (ls.X * 0.5f), center.Y + Ui.Px(3f)), Palette.TextMuted.U32(), label);
        }

        if (clicked)
        {
            if (blurred)
                this.revealed.Add(id);   // first tap reveals, same as the thread
            else if (texture != null)
                this.lightbox.OpenTexture(texture);
        }
    }

    private void DrawEmpty(int total, string name, float contentWidth)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(40f)));
        if (total == 0)
            this.kit.EmptyState(FontAwesomeIcon.Images.ToIconString(), "No photos yet", $"Photos you and {name} share show up here.", contentWidth);
        else
            this.kit.EmptyState(FontAwesomeIcon.Images.ToIconString(), "Nothing here", "No photos match this filter.", contentWidth);
    }
}
