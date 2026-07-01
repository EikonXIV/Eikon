using Dalamud.Interface;
using Eikon.Config;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.Services;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Chat thread. Header with the peer, an end-to-end note, the message bubbles, and a composer. Text and
// images are encrypted before they leave the client (ChatService); the relay only carries ciphertext.
internal sealed class ChatScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly ThemeService theme;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly ModerationFlow moderation;
    private readonly ChatService chat;
    private readonly Selection selection;
    private readonly IdentityService identity;
    private readonly Media media;
    private readonly ChatMediaCache mediaCache;
    private readonly Lightbox lightbox;
    private readonly InboxService inbox;
    private readonly PhotoService photoSvc;
    private readonly Configuration config;
    private readonly WindowController windowController;

    private Guid markReadFor;       // peer we last marked read
    private int markReadCount = -1; // thread length at that point, so new messages while open re-mark
    private double presencePolledAt; // last time we refreshed the inbox for the header presence dot
    private bool refocusComposer;    // re-focus the message field next frame (after an Enter send)
    private string draft = string.Empty;
    private bool openSafety;
    private Guid? scrollPeer;       // chat currently anchored to the bottom
    private int scrollCount = -1;   // message count last time we scrolled, to detect new messages
    private bool stickBottom;       // keep re-anchoring to the newest message until the user scrolls up
    private int snapFrames;         // frames left to jump to the bottom instantly (on open) before easing
    private bool showJump;          // show the jump-to-latest button (scrolled up past the threshold)
    private string? pendingImagePath;   // image picked, awaiting send confirmation
    private bool pendingNsfw;
    private bool openImagePopup;
    private readonly HashSet<string> revealed = new();   // NSFW image ids the viewer chose to reveal

    public ChatScreen(ScreenRouter router, ThemeService theme, Kit kit, UiFonts fonts, ModerationFlow moderation, ChatService chat, Selection selection, IdentityService identity, Media media, ChatMediaCache mediaCache, Lightbox lightbox, InboxService inbox, PhotoService photoSvc, Configuration config, WindowController windowController)
    {
        this.router = router;
        this.theme = theme;
        this.kit = kit;
        this.fonts = fonts;
        this.moderation = moderation;
        this.chat = chat;
        this.selection = selection;
        this.identity = identity;
        this.media = media;
        this.mediaCache = mediaCache;
        this.lightbox = lightbox;
        this.inbox = inbox;
        this.photoSvc = photoSvc;
        this.config = config;
        this.windowController = windowController;
    }

    public Screen Id => Screen.Chat;

    public bool Chrome => false;

    public void Draw()
    {
        var peer = this.selection.ProfileUserId;
        if (peer is null)
        {
            this.router.Navigate(Screen.Messages);
            return;
        }

        this.chat.Start();
        var name = this.selection.ProfileDisplayName;
        var thread = this.chat.Thread(peer.Value);

        // Keep the inbox loaded (its conversation rows carry the peer's presence for the header dot) and
        // refresh it periodically so the dot stays roughly live while sitting in the chat.
        this.inbox.EnsureLoaded();
        var now = ImGui.GetTime();
        if (now - this.presencePolledAt > 25.0)
        {
            this.presencePolledAt = now;
            this.inbox.Refresh();
        }

        // Viewing the thread marks it read (clears the inbox unread badge). Re-mark when it grows so
        // messages that arrive while you are looking at the chat do not show as unread later.
        if (this.markReadFor != peer.Value || this.markReadCount != thread.Count)
        {
            this.markReadFor = peer.Value;
            this.markReadCount = thread.Count;
            this.inbox.MarkRead(peer.Value);
        }

        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(16f);
        var headerHeight = Ui.Px(56f);

        // The composer grows with the draft: one line normally, taller as Shift+Enter adds lines
        // (capped at 5), then back to one line once it is sent.
        var lineH = Ui.Measure(this.fonts.Body, "A").Y;
        var composerLines = 1;
        foreach (var ch in this.draft)
            if (ch == '\n') composerLines++;
        composerLines = Math.Clamp(composerLines, 1, 5);
        var fieldHeight = (composerLines * lineH) + Ui.Px(20f);
        var composerHeight = fieldHeight + Ui.Px(18f);

        this.DrawHeader(avail.X, pad, name, peer.Value);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using (var body = ImRaii.Child("chat_thread", new Vector2(avail.X, avail.Y - headerHeight - composerHeight)))
        {
            if (body.Success)
            {
                this.DrawBackground(peer.Value);
                ImGui.Indent(pad);
                var contentWidth = avail.X - (pad * 2f);
                ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
                this.DrawE2ENote(contentWidth, peer.Value);
                ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
                using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Ui.Px(6f))))
                {
                    // Day separators appear where timestamps exist; messages from before timestamps
                    // were added sit above a single "earlier messages" divider, undated.
                    DateTime? lastDay = null;
                    var seenStamped = false;
                    for (var i = 0; i < thread.Count; i++)
                    {
                        var message = thread[i];
                        if (message.SentAt is { } sa)
                        {
                            if (!seenStamped)
                            {
                                seenStamped = true;
                                lastDay = null;
                                if (i > 0)
                                    this.DrawEarlierDivider(contentWidth);
                            }

                            var day = sa.ToLocalTime().Date;
                            if (lastDay != day)
                            {
                                this.DrawDaySeparator(DayLabel(sa), contentWidth);
                                lastDay = day;
                            }
                        }

                        // Time shows on the last message of a same-sender burst; the very last outgoing
                        // message folds its time into the delivery receipt instead.
                        var isLast = i == thread.Count - 1;
                        var showTime = message.SentAt != null && !(isLast && message.Mine) && BurstEnds(thread, i);
                        this.DrawBubble(message, contentWidth, showTime);
                    }

                    if (thread.Count > 0 && thread[^1].Mine)
                        this.DrawReceipt(thread[^1], contentWidth);

                    // Breathing room so the newest message (or its receipt) is not flush against the
                    // composer; part of the scroll content, so stick-to-bottom keeps it visible.
                    ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));

                    // Stick to the newest message. A one-shot scroll lands short: the child layout
                    // is still settling on the opening frame, and image bubbles change height when
                    // they finish loading (placeholder square -> real aspect). So hold a "pinned"
                    // intent and re-anchor to the bottom while it is set. Opening a chat pins it; a new
                    // message follows only if you are already pinned (or you sent it), so reading
                    // history is not interrupted.
                    var peerChanged = this.scrollPeer != peer.Value;
                    var grew = thread.Count != this.scrollCount;
                    this.scrollPeer = peer.Value;
                    if (peerChanged)
                    {
                        this.stickBottom = true;
                        this.snapFrames = 6;   // settle instantly when first opening the thread
                    }
                    else if (grew && (this.stickBottom || (thread.Count > 0 && thread[^1].Mine)))
                    {
                        this.stickBottom = true;
                    }
                    this.scrollCount = thread.Count;

                    if (this.stickBottom)
                    {
                        if (ImGui.IsWindowHovered() && ImGui.GetIO().MouseWheel > 0f)
                        {
                            this.stickBottom = false;   // scrolling up to read history releases the pin
                        }
                        else if (this.snapFrames > 0)
                        {
                            this.snapFrames--;
                            ImGui.SetScrollHereY(1f);    // instant jump on open (no scroll-through animation)
                        }
                        else
                        {
                            // Ease toward the bottom instead of teleporting (smooths the jump button and
                            // new-message follow). Frame-rate independent: converge ~exponentially.
                            var target = ImGui.GetScrollMaxY();
                            var cur = ImGui.GetScrollY();
                            var t = 1f - MathF.Exp(-ImGui.GetIO().DeltaTime * 18f);
                            var next = cur + ((target - cur) * t);
                            if (target - next < 0.5f)
                                next = target;
                            ImGui.SetScrollY(next);
                        }
                    }
                    else if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - Ui.Px(4f))
                    {
                        this.stickBottom = true;         // returned to the bottom -> follow again
                    }

                    // Offer a jump-to-latest button once scrolled up a few messages from the bottom.
                    // It is drawn here, inside the scroll child, so it paints over the messages and is
                    // clickable: a child window always renders on top of its parent, so a button drawn
                    // on the parent would be hidden behind the bubbles.
                    this.showJump = !this.stickBottom && (ImGui.GetScrollMaxY() - ImGui.GetScrollY()) > Ui.Px(200f);
                    this.DrawJumpToLatest();
                }
                ImGui.Unindent(pad);
            }
        }

        this.DrawComposer(pad, avail, composerHeight, fieldHeight, peer.Value);
        this.moderation.Draw();
        this.lightbox.Draw();
        this.DrawSafetyNumber(avail.X, name, peer.Value);
        this.DrawImagePopup(peer.Value);
    }

    // Per-conversation chat wallpaper (config.ChatBackgrounds; a local file the viewer picked, never
    // uploaded or sent). Drawn in screen space pinned to the thread viewport so it stays put while the
    // messages scroll over it, behind everything, with the standard scrim on top so the end-to-end note
    // and receipts keep their contrast over a bright image. A path that no longer loads (file moved or
    // deleted) silently falls back to the plain background. Resolved through Dalamud's shared texture
    // cache each frame, the same as avatars and image bubbles, so there is nothing to dispose here.
    private void DrawBackground(Guid peer)
    {
        if (!this.config.ChatBackgrounds.TryGetValue(peer.ToString(), out var path) || string.IsNullOrEmpty(path))
            return;

        var tex = this.media.Load(path);
        if (tex is not { Width: > 0, Height: > 0 })
            return;

        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        if (winSize.X <= 0f || winSize.Y <= 0f)
            return;

        var drawList = ImGui.GetWindowDrawList();
        var (uvMin, uvMax) = Ui.CoverUv(tex.Width, tex.Height, winSize.X / winSize.Y);
        drawList.AddImageRounded(tex.Handle, winPos, winPos + winSize, uvMin, uvMax, 0xFFFFFFFFu, 0f);
        drawList.AddRectFilled(winPos, winPos + winSize, Palette.Scrim.U32());
    }

    private void DrawHeader(float fullWidth, float pad, string name, Guid peer)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var midY = origin.Y + Ui.Px(28f);

        var backGlyph = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, backGlyph);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##chat_back", backSize))
            this.router.Navigate(Screen.Messages);
        Ui.TextAt(drawList, this.fonts.Icon, ImGui.GetItemRectMin(), Palette.TextSecondary.U32(), backGlyph);

        // Right side, from the corner inward: minimize (the same control as the main window's title bar,
        // collapsing the app to the orb), then the overflow menu, with a hairline between them so the
        // window control reads apart from the chat actions.
        var btn = Ui.Px(30f);
        var minTL = new Vector2(origin.X + fullWidth - pad - btn, midY - (btn * 0.5f));
        if (this.HeaderIconButton(drawList, "##chat_min", FontAwesomeIcon.Minus, minTL, btn))
            this.windowController.Minimize();

        var divX = minTL.X - Ui.Px(8f);
        drawList.AddLine(new Vector2(divX, midY - Ui.Px(9f)), new Vector2(divX, midY + Ui.Px(9f)), Palette.Border.U32(), 1f);

        var moreTL = new Vector2(divX - Ui.Px(8f) - btn, midY - (btn * 0.5f));
        if (this.HeaderIconButton(drawList, "##chat_more", FontAwesomeIcon.EllipsisH, moreTL, btn))
            this.moderation.Open(peer, name, new Vector2(moreTL.X + btn, moreTL.Y + btn),
                () =>
                {
                    this.selection.ProfileUserId = peer;
                    this.selection.ProfileDisplayName = name;
                    this.router.Navigate(Screen.ProfileDetail);
                },
                () => this.openSafety = true,
                chatActions: true,
                onSharedMedia: () =>
                {
                    this.selection.ProfileUserId = peer;
                    this.selection.ProfileDisplayName = name;
                    this.router.Navigate(Screen.SharedMedia);
                });

        var radius = Ui.Px(16f);

        // Avatar and name double as a tap target that opens the profile (also offered in the overflow
        // menu). Submitted before the avatar/name are painted so its hover highlight sits behind them.
        var tapX = origin.X + pad + backSize.X + Ui.Px(6f);
        var tapRight = moreTL.X - Ui.Px(6f);
        var tapW = tapRight - tapX;
        var tapClicked = false;
        if (tapW > Ui.Px(40f))
        {
            ImGui.SetCursorScreenPos(new Vector2(tapX, midY - Ui.Px(18f)));
            tapClicked = ImGui.InvisibleButton("##chat_peer", new Vector2(tapW, Ui.Px(36f)));
            if (ImGui.IsItemHovered())
                drawList.AddRectFilled(new Vector2(tapX, midY - Ui.Px(18f)), new Vector2(tapRight, midY + Ui.Px(18f)), Palette.WithAlpha(Palette.White, 0.04f).U32(), Ui.Px(10f));
        }

        var avatarCenter = new Vector2(origin.X + pad + backSize.X + Ui.Px(12f) + radius, midY);

        // Photo and presence both come from the cached inbox row (the chat keeps the inbox loaded).
        var online = false;
        Guid? photoId = null;
        foreach (var c in this.inbox.Conversations)
            if (c.UserId == peer) { online = c.Online; photoId = c.MainPhotoId; break; }

        var avatarTex = photoId is { } pid ? this.photoSvc.Texture(pid) : null;
        if (avatarTex != null)
        {
            var (uvMin, uvMax) = Ui.CoverUv(avatarTex.Width, avatarTex.Height, 1f);
            drawList.AddImageRounded(avatarTex.Handle, avatarCenter - new Vector2(radius, radius), avatarCenter + new Vector2(radius, radius), uvMin, uvMax, 0xFFFFFFFFu, radius);
        }
        else
        {
            drawList.AddCircleFilled(avatarCenter, radius, Palette.Surface2.U32(), 24);
            var initial = name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
            var initialSize = Ui.Measure(this.fonts.Caption, initial);
            Ui.TextAt(drawList, this.fonts.Caption,
                new Vector2(avatarCenter.X - (initialSize.X * 0.5f), avatarCenter.Y - (initialSize.Y * 0.5f)),
                Palette.TextSecondary.U32(), initial);
        }

        // Presence dot on the avatar: accent when online, muted when offline.
        var dot = avatarCenter + new Vector2(radius - Ui.Px(3f), radius - Ui.Px(3f));
        drawList.AddCircleFilled(dot, Ui.Px(4.5f), (online ? this.theme.Accent : Palette.TextMuted).U32(), 12);
        drawList.AddCircle(dot, Ui.Px(4.5f), Palette.Bg.U32(), 12, Ui.Px(1.5f));

        var nameX = avatarCenter.X + radius + Ui.Px(10f);
        var nameSize = Ui.Measure(this.fonts.Body, name);
        Ui.TextAt(drawList, this.fonts.Body, new Vector2(nameX, midY - (nameSize.Y * 0.5f)), Palette.TextPrimary.U32(), name);

        if (tapClicked)
        {
            this.selection.ProfileUserId = peer;
            this.selection.ProfileDisplayName = name;
            this.router.Navigate(Screen.ProfileDetail);
        }

        drawList.AddLine(new Vector2(origin.X, origin.Y + Ui.Px(56f)), new Vector2(origin.X + fullWidth, origin.Y + Ui.Px(56f)), Palette.Border.U32(), 1f);
    }

    // A 30px header icon button that mirrors the main window's title-bar minimize chrome: a faint hover
    // fill and a muted glyph that brightens on hover. Used for both the overflow and minimize controls.
    private bool HeaderIconButton(ImDrawListPtr drawList, string id, FontAwesomeIcon icon, Vector2 topLeft, float size)
    {
        ImGui.SetCursorScreenPos(topLeft);
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();
        var min = ImGui.GetItemRectMin();
        if (hovered)
            drawList.AddRectFilled(min, min + new Vector2(size, size), Palette.WithAlpha(Palette.White, 0.06f).U32(), Ui.Px(8f));
        var glyph = icon.ToIconString();
        var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(min.X + ((size - glyphSize.X) * 0.5f), min.Y + ((size - glyphSize.Y) * 0.5f)), (hovered ? Palette.TextSecondary : Palette.TextMuted).U32(), glyph);
        return clicked;
    }

    private void DrawE2ENote(float contentWidth, Guid peer)
    {
        var mismatched = this.identity.Mismatched(peer);
        var verified = !mismatched && this.identity.IsVerified(peer);
        string note;
        FontAwesomeIcon glyphIcon;
        uint color;
        if (mismatched)
        {
            note = "Safety identity changed - tap to review";
            glyphIcon = FontAwesomeIcon.ExclamationTriangle;
            color = Palette.Danger.U32();
        }
        else if (verified)
        {
            note = "Encrypted - identity verified";
            glyphIcon = FontAwesomeIcon.CheckCircle;
            color = this.theme.Accent.U32();
        }
        else
        {
            note = "Encrypted - tap to verify";
            glyphIcon = FontAwesomeIcon.Lock;
            color = Palette.TextMuted.U32();
        }

        var glyph = glyphIcon.ToIconString();
        var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
        var noteSize = Ui.Measure(this.fonts.Caption, note);
        var total = glyphSize.X + Ui.Px(5f) + noteSize.X;
        var height = MathF.Max(glyphSize.Y, noteSize.Y);

        var pos = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton("##e2e_verify", new Vector2(contentWidth, height)))
            this.openSafety = true;

        var x = pos.X + ((contentWidth - total) * 0.5f);
        var drawList = ImGui.GetWindowDrawList();
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(x, pos.Y), color, glyph);
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(x + glyphSize.X + Ui.Px(5f), pos.Y + ((glyphSize.Y - noteSize.Y) * 0.5f)), color, note);
    }

    // Safety-number sheet: the two members compare this number out of band (voice/in person); if it
    // matches, there is no man-in-the-middle. The code is shown large in a grid so it's easy to read
    // aloud and compare. Marking verified upgrades the pinned identity.
    private void DrawSafetyNumber(float fullWidth, string name, Guid peer)
    {
        if (this.openSafety)
        {
            this.openSafety = false;
            ImGui.OpenPopup("##safety");
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
            if (!ImGui.BeginPopupModal("##safety", ref open, flags))
                return;

            var width = Ui.Px(300f);
            ImGui.Dummy(new Vector2(width, 0f));   // lock the width; height auto-fits the grid

            var mismatched = this.identity.Mismatched(peer);
            this.IconBadge(width, mismatched ? FontAwesomeIcon.ExclamationTriangle : FontAwesomeIcon.ShieldAlt, mismatched ? Palette.Danger : this.theme.Accent);
            ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));
            Ui.CenteredText(width, this.fonts.Title, Palette.TextPrimary, "Safety number");
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
                ImGui.TextWrapped($"Compare this with {name} over a channel you trust (voice or in person). If it matches on both sides, no one is intercepting your messages.");

            if (mismatched)
            {
                ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
                using (this.fonts.Caption.Push())
                using (ImRaii.PushColor(ImGuiCol.Text, Palette.Danger))
                    ImGui.TextWrapped("This person's safety identity changed since you last saw it. That can be a new device - or someone intercepting. Re-verify the number below out of band before trusting it.");
            }

            ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
            var number = this.identity.SafetyNumber(peer);
            if (number is null)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
                    ImGui.TextWrapped("Send a message first so keys are exchanged, then come back to verify.");
            }
            else
            {
                this.DrawSafetyDigits(width, number);
                ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
                if (this.identity.IsVerified(peer))
                    this.DrawVerifiedBadge(width);
                else if (this.kit.PrimaryButton("##safety_verify", "Mark as verified", width))
                    this.identity.MarkVerified(peer);
            }

            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            if (mismatched && this.kit.DangerButton("##safety_reset", "Reset this contact's identity", width))
            {
                this.identity.ForgetPin(peer);   // accept the change; the next message re-pins it
                ImGui.CloseCurrentPopup();
            }

            ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));
            if (this.kit.SecondaryButton("##safety_close", "Close", width))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
    }

    // Accent (or danger) tinted circle with an icon, centered: the safety sheet's header glyph.
    private void IconBadge(float contentWidth, FontAwesomeIcon icon, Vector4 color)
    {
        var diameter = Ui.Px(50f);
        var pos = ImGui.GetCursorScreenPos();
        var center = new Vector2(pos.X + (contentWidth * 0.5f), pos.Y + (diameter * 0.5f));
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddCircleFilled(center, diameter * 0.5f, Palette.WithAlpha(color, 0.14f).U32(), 32);
        var glyph = icon.ToIconString();
        var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(center.X - (glyphSize.X * 0.5f), center.Y - (glyphSize.Y * 0.5f)), color.U32(), glyph);
        ImGui.Dummy(new Vector2(contentWidth, diameter));
    }

    // The safety number shown large, in a panel, as a 4-column grid of its space-separated groups.
    private void DrawSafetyDigits(float width, string number)
    {
        var groups = number.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (groups.Length == 0)
            return;
        const int cols = 4;
        var rows = (groups.Length + cols - 1) / cols;
        var pad = Ui.Px(14f);
        var cellH = Ui.Px(32f);
        var panelH = (pad * 2f) + (rows * cellH);

        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, pos + new Vector2(width, panelH), Palette.Bg.U32(), Ui.Px(12f));
        drawList.AddRect(pos, pos + new Vector2(width, panelH), Palette.Border.U32(), Ui.Px(12f), ImDrawFlags.None, 1f);

        var cellW = (width - (pad * 2f)) / cols;
        for (var i = 0; i < groups.Length; i++)
        {
            var gs = Ui.Measure(this.fonts.Title, groups[i]);
            var cx = pos.X + pad + ((i % cols) * cellW) + ((cellW - gs.X) * 0.5f);
            var cy = pos.Y + pad + ((i / cols) * cellH) + ((cellH - gs.Y) * 0.5f);
            Ui.TextAt(drawList, this.fonts.Title, new Vector2(cx, cy), Palette.TextPrimary.U32(), groups[i]);
        }

        ImGui.Dummy(new Vector2(width, panelH));
    }

    // Centered "Verified" with a check, shown once the member confirms the number matches.
    private void DrawVerifiedBadge(float width)
    {
        var glyph = FontAwesomeIcon.CheckCircle.ToIconString();
        var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
        const string label = "Verified";
        var labelSize = Ui.Measure(this.fonts.Caption, label);
        var total = glyphSize.X + Ui.Px(7f) + labelSize.X;
        var pos = ImGui.GetCursorScreenPos();
        var x = pos.X + ((width - total) * 0.5f);
        var drawList = ImGui.GetWindowDrawList();
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(x, pos.Y), this.theme.Accent.U32(), glyph);
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(x + glyphSize.X + Ui.Px(7f), pos.Y + ((glyphSize.Y - labelSize.Y) * 0.5f)), this.theme.Accent.U32(), label);
        ImGui.Dummy(new Vector2(width, glyphSize.Y));
    }

    private void DrawBubble(ChatService.Message message, float contentWidth, bool showTime)
    {
        if (message.IsImage)
        {
            this.DrawImageBubble(message, contentWidth, showTime);
            return;
        }

        var maxWidth = contentWidth * 0.76f;
        var padX = Ui.Px(11f);
        var padY = Ui.Px(8f);
        var wrap = maxWidth - (padX * 2f);
        var textSize = Ui.MeasureWrapped(this.fonts.Body, message.Text, wrap);
        var bubbleWidth = textSize.X + (padX * 2f);
        var bubbleHeight = textSize.Y + (padY * 2f);

        var leftX = ImGui.GetCursorScreenPos().X;
        var top = ImGui.GetCursorScreenPos().Y;
        var x = message.Mine ? leftX + contentWidth - bubbleWidth : leftX;
        var pos = new Vector2(x, top);

        var drawList = ImGui.GetWindowDrawList();
        var background = (message.Mine ? this.theme.AccentDeep : Palette.Surface2).U32();
        var foreground = (message.Mine ? this.theme.OnAccent : Palette.TextPrimary).U32();

        // Tail tuck: the bubble's bottom corner on its own side is tightened (14 -> 4) so it reads as
        // "coming from" that side. Mine (right) tucks bottom-right; the peer's (left) tucks bottom-left.
        var big = Ui.Px(14f);
        var tuck = Ui.Px(4f);
        var max = pos + new Vector2(bubbleWidth, bubbleHeight);
        if (message.Mine)
            Ui.FillRectCorners(drawList, pos, max, background, big, big, tuck, big);
        else
            Ui.FillRectCorners(drawList, pos, max, background, big, big, big, tuck);

        Ui.TextWrappedAt(drawList, this.fonts.Body, pos + new Vector2(padX, padY), foreground, message.Text, wrap);

        var extra = this.DrawBubbleTime(message, showTime, leftX, top + bubbleHeight, contentWidth);
        ImGui.Dummy(new Vector2(contentWidth, bubbleHeight + extra));
    }

    private void DrawImageBubble(ChatService.Message message, float contentWidth, bool showTime)
    {
        var maxW = MathF.Min(contentWidth * 0.62f, Ui.Px(220f));
        var rounding = Ui.Px(14f);
        var id = message.ImageId ?? "img";
        var blurred = message.Nsfw && message.ImageId != null && !this.revealed.Contains(message.ImageId);
        var texture = (!blurred && message.ImageId != null) ? this.mediaCache.Texture(message.ImageId) : null;

        float w, h;
        if (texture is { Width: > 0, Height: > 0 })
        {
            var scale = MathF.Min(MathF.Min(maxW / texture.Width, Ui.Px(260f) / texture.Height), 1f);
            w = texture.Width * scale;
            h = texture.Height * scale;
        }
        else
        {
            w = maxW;
            h = maxW;   // square placeholder while loading or blurred
        }

        var hasCaption = !string.IsNullOrEmpty(message.Text);
        var capWrap = w - Ui.Px(4f);
        var capSize = hasCaption ? Ui.MeasureWrapped(this.fonts.Body, message.Text, capWrap) : Vector2.Zero;
        var totalH = h + (hasCaption ? capSize.Y + Ui.Px(6f) : 0f);

        var leftX = ImGui.GetCursorScreenPos().X;
        var top = ImGui.GetCursorScreenPos().Y;
        var x = message.Mine ? leftX + contentWidth - w : leftX;
        var pos = new Vector2(x, top);
        var drawList = ImGui.GetWindowDrawList();

        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##img_" + id, new Vector2(w, h));
        ImGui.SetItemAllowOverlap();   // let the floating jump-to-latest button (drawn later) win the click

        if (texture != null)
        {
            drawList.AddImageRounded(texture.Handle, pos, pos + new Vector2(w, h), Vector2.Zero, Vector2.One, 0xFFFFFFFFu, rounding);
        }
        else
        {
            drawList.AddRectFilled(pos, pos + new Vector2(w, h), Palette.Surface2.U32(), rounding);
            var center = new Vector2(pos.X + (w * 0.5f), pos.Y + (h * 0.5f));
            if (blurred)
            {
                var eye = FontAwesomeIcon.EyeSlash.ToIconString();
                var es = Ui.Measure(this.fonts.Icon, eye);
                Ui.TextAt(drawList, this.fonts.Icon, new Vector2(center.X - (es.X * 0.5f), center.Y - es.Y), Palette.TextSecondary.U32(), eye);
                var label = "Tap to reveal";
                var ls = Ui.Measure(this.fonts.Caption, label);
                Ui.TextAt(drawList, this.fonts.Caption, new Vector2(center.X - (ls.X * 0.5f), center.Y + Ui.Px(4f)), Palette.TextSecondary.U32(), label);
            }
            else
            {
                var label = "Loading...";
                var ls = Ui.Measure(this.fonts.Caption, label);
                Ui.TextAt(drawList, this.fonts.Caption, new Vector2(center.X - (ls.X * 0.5f), center.Y - (ls.Y * 0.5f)), Palette.TextMuted.U32(), label);
            }
        }

        if (clicked && blurred && message.ImageId != null)
            this.revealed.Add(message.ImageId);   // first tap reveals
        else if (clicked && texture != null)
            this.lightbox.OpenTexture(texture);   // tap a shown image -> full-size viewer

        if (hasCaption)
            Ui.TextWrappedAt(drawList, this.fonts.Body, new Vector2(x, top + h + Ui.Px(6f)), Palette.TextPrimary.U32(), message.Text, capWrap);

        var extra = this.DrawBubbleTime(message, showTime, leftX, top + totalH, contentWidth);
        ImGui.SetCursorScreenPos(new Vector2(leftX, top));
        ImGui.Dummy(new Vector2(contentWidth, totalH + extra));
    }

    private void DrawReceipt(ChatService.Message message, float contentWidth)
    {
        var label = message.State switch
        {
            MessageState.Pending => "Sending...",
            MessageState.Sent => "Sent",
            MessageState.Delivered => "Delivered",
            MessageState.Failed => "Failed to send",
            _ => string.Empty,
        };
        if (message.SentAt is { } sa && label.Length > 0)
            label = $"{sa.ToLocalTime():t} · {label}";
        var color = message.State == MessageState.Failed ? new Vector4(0.91f, 0.36f, 0.36f, 1f) : Palette.TextMuted;
        var size = Ui.Measure(this.fonts.Caption, label);
        var pos = ImGui.GetCursorScreenPos();
        Ui.TextAt(ImGui.GetWindowDrawList(), this.fonts.Caption, new Vector2(pos.X + contentWidth - size.X, pos.Y + Ui.Px(2f)), color.U32(), label);
        ImGui.Dummy(new Vector2(contentWidth, size.Y + Ui.Px(2f)));
    }

    // The small muted time under the last bubble of a burst, aligned to the bubble's side. Returns the
    // extra height it consumed so the caller can extend the layout dummy.
    private float DrawBubbleTime(ChatService.Message message, bool showTime, float leftX, float y, float contentWidth)
    {
        if (!showTime || message.SentAt is not { } sa)
            return 0f;
        var time = sa.ToLocalTime().ToString("t");
        var ts = Ui.Measure(this.fonts.Caption, time);
        var x = message.Mine ? leftX + contentWidth - ts.X : leftX;
        Ui.TextAt(ImGui.GetWindowDrawList(), this.fonts.Caption, new Vector2(x, y + Ui.Px(3f)), Palette.TextMuted.U32(), time);
        return ts.Y + Ui.Px(3f);
    }

    // A burst ends at the last message, or when the next message flips sender, loses its timestamp, or
    // crosses into a new day.
    private static bool BurstEnds(IReadOnlyList<ChatService.Message> thread, int i)
    {
        if (i == thread.Count - 1)
            return true;
        var cur = thread[i];
        var next = thread[i + 1];
        if (cur.Mine != next.Mine || next.SentAt is null)
            return true;
        return cur.SentAt is { } a && next.SentAt is { } b && a.ToLocalTime().Date != b.ToLocalTime().Date;
    }

    private void DrawDaySeparator(string label, float contentWidth) => this.DrawSeparator(label, Palette.TextSecondary, contentWidth);

    private void DrawEarlierDivider(float contentWidth) => this.DrawSeparator("earlier messages", Palette.TextMuted, contentWidth);

    // Centered label with a hairline to each side: the day markers and the pre-timestamp boundary.
    private void DrawSeparator(string label, Vector4 textColor, float contentWidth)
    {
        var blockHeight = Ui.Px(26f);
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var ls = Ui.Measure(this.fonts.Caption, label);
        var midY = pos.Y + (blockHeight * 0.5f);
        var cx = pos.X + (contentWidth * 0.5f);
        var halfLabel = (ls.X * 0.5f) + Ui.Px(10f);
        drawList.AddLine(new Vector2(pos.X + Ui.Px(6f), midY), new Vector2(cx - halfLabel, midY), Palette.Border.U32(), 1f);
        drawList.AddLine(new Vector2(cx + halfLabel, midY), new Vector2(pos.X + contentWidth - Ui.Px(6f), midY), Palette.Border.U32(), 1f);
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(cx - (ls.X * 0.5f), midY - (ls.Y * 0.5f)), textColor.U32(), label);
        ImGui.Dummy(new Vector2(contentWidth, blockHeight));
    }

    private static string DayLabel(DateTimeOffset sentAt)
    {
        var day = sentAt.ToLocalTime().Date;
        var today = DateTime.Today;
        if (day == today)
            return "Today";
        if (day == today.AddDays(-1))
            return "Yesterday";
        if (day > today.AddDays(-7))
            return day.ToString("dddd");
        return day.Year == today.Year ? day.ToString("MMM d") : day.ToString("MMM d, yyyy");
    }

    // Floating jump-to-latest button. Called from inside the scroll child so it paints over the
    // bubbles and is clickable. Pinned to the child's bottom-right viewport corner via screen coords
    // (independent of scroll), clear of the scrollbar. Tapping re-arms the bottom pin so the scroll
    // logic snaps down next frame.
    private void DrawJumpToLatest()
    {
        if (!this.showJump)
            return;

        var diameter = Ui.Px(34f);
        var margin = Ui.Px(12f);
        var winPos = ImGui.GetWindowPos();
        var rightEdge = winPos.X + ImGui.GetWindowContentRegionMax().X;   // left of the scrollbar
        var bottomEdge = winPos.Y + ImGui.GetWindowSize().Y;
        var topLeft = new Vector2(rightEdge - diameter, bottomEdge - margin - diameter);

        // Place the hit target at the pinned spot, then restore the cursor so this overlay does not
        // grow the scroll content.
        var saved = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(topLeft);
        var clicked = ImGui.InvisibleButton("##chat_jump", new Vector2(diameter, diameter));
        var hovered = ImGui.IsItemHovered();
        ImGui.SetCursorScreenPos(saved);

        var drawList = ImGui.GetWindowDrawList();
        var center = topLeft + new Vector2(diameter * 0.5f, diameter * 0.5f);
        drawList.AddCircleFilled(center, diameter * 0.5f, (hovered ? Palette.Surface1 : Palette.Surface2).U32(), 24);
        drawList.AddCircle(center, diameter * 0.5f, Palette.Border.U32(), 24, Ui.Px(1f));

        var glyph = FontAwesomeIcon.ChevronDown.ToIconString();
        var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(center.X - (glyphSize.X * 0.5f), center.Y - (glyphSize.Y * 0.5f)), Palette.TextPrimary.U32(), glyph);

        if (clicked)
        {
            this.stickBottom = true;   // re-pin; the thread's scroll logic snaps to the bottom next frame
            this.showJump = false;
        }
    }

    private void DrawComposer(float pad, Vector2 avail, float composerHeight, float fieldHeight, Guid peer)
    {
        var sendDiameter = Ui.Px(38f);
        var attachBox = Ui.Px(38f);
        var gap = Ui.Px(8f);

        var attachGlyph = FontAwesomeIcon.Image.ToIconString();
        var attachSize = Ui.Measure(this.fonts.Icon, attachGlyph);
        var fieldWidth = (avail.X - (pad * 2f)) - attachBox - gap - sendDiameter - gap;

        var fieldTop = avail.Y - composerHeight + Ui.Px(9f);
        var fieldBottom = fieldTop + fieldHeight;
        var drawList = ImGui.GetWindowDrawList();

        // Hairline across the top of the composer band, mirroring the header's divider, so the input
        // reads as anchored chrome rather than floating over the scrolling thread. It rides up with the
        // band as the field grows.
        ImGui.SetCursorPos(new Vector2(0f, avail.Y - composerHeight));
        var barTop = ImGui.GetCursorScreenPos();
        drawList.AddLine(barTop, new Vector2(barTop.X + avail.X, barTop.Y), Palette.Border.U32(), 1f);

        // Attach + send sit on the field's bottom line, so they stay put as it grows upward. Both carry a
        // header-style hover state so the two most-used controls feel as live as the header chrome.
        ImGui.SetCursorPos(new Vector2(pad, fieldBottom - attachBox));
        var attachPos = ImGui.GetCursorScreenPos();
        var attachClicked = ImGui.InvisibleButton("##chat_attach", new Vector2(attachBox, attachBox));
        var attachHover = ImGui.IsItemHovered();
        if (attachClicked)
            this.media.PickImage(p => { this.pendingImagePath = p; this.pendingNsfw = false; this.openImagePopup = true; });
        if (attachHover)
            drawList.AddRectFilled(attachPos, attachPos + new Vector2(attachBox, attachBox), Palette.WithAlpha(Palette.White, 0.06f).U32(), Ui.Px(10f));
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(attachPos.X + ((attachBox - attachSize.X) * 0.5f), attachPos.Y + ((attachBox - attachSize.Y) * 0.5f)), (attachHover ? Palette.TextPrimary : Palette.TextSecondary).U32(), attachGlyph);

        ImGui.SetCursorPos(new Vector2(pad + attachBox + gap, fieldTop));
        var enterSend = this.kit.ComposerField("##chat_draft", ref this.draft, "Message", fieldWidth, fieldHeight, this.refocusComposer);
        this.refocusComposer = false;

        ImGui.SetCursorPos(new Vector2(avail.X - pad - sendDiameter, fieldBottom - sendDiameter));
        var sendPos = ImGui.GetCursorScreenPos();
        var clickSend = ImGui.InvisibleButton("##chat_send", new Vector2(sendDiameter, sendDiameter));
        var sendHover = ImGui.IsItemHovered();
        var text = this.draft.Trim();
        var actionable = text.Length > 0;
        if (actionable && (enterSend || clickSend))
        {
            this.chat.Send(peer, text);
            this.draft = string.Empty;
            if (enterSend)
                this.refocusComposer = true;   // keep typing without re-clicking after an Enter send
        }
        else if (enterSend)
        {
            this.draft = string.Empty;
            this.refocusComposer = true;
        }

        // Send reflects whether there is anything to send: a muted disc when the field is empty (tapping
        // it does nothing), the solid accent once there is text, brightening further on hover.
        var sendCenter = sendPos + new Vector2(sendDiameter * 0.5f, sendDiameter * 0.5f);
        var sendFill = actionable ? (sendHover ? this.theme.Accent : this.theme.AccentDeep) : Palette.Surface2;
        drawList.AddCircleFilled(sendCenter, sendDiameter * 0.5f, sendFill.U32(), 24);
        var sendGlyph = FontAwesomeIcon.PaperPlane.ToIconString();
        var sendSize = Ui.Measure(this.fonts.Icon, sendGlyph);
        var sendGlyphColor = actionable ? this.theme.OnAccent : Palette.TextMuted;
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(sendCenter.X - (sendSize.X * 0.5f), sendCenter.Y - (sendSize.Y * 0.5f)), sendGlyphColor.U32(), sendGlyph);
    }

    private void DrawImagePopup(Guid peer)
    {
        if (this.openImagePopup)
        {
            this.openImagePopup = false;
            ImGui.OpenPopup("##sendimg");
        }

        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + (ImGui.GetWindowSize() * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(Ui.Px(330f), Ui.Px(450f)));
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
        var open = true;

        using (ImRaii.PushColor(ImGuiCol.PopupBg, Palette.Surface1))
        using (ImRaii.PushColor(ImGuiCol.Border, Palette.Border))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(Ui.Px(18f), Ui.Px(18f))))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, Ui.Px(16f)))
        using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f))
        {
            if (!ImGui.BeginPopupModal("##sendimg", ref open, flags))
                return;

            var width = ImGui.GetContentRegionAvail().X;
            Ui.CenteredText(width, this.fonts.Title, Palette.TextPrimary, "Send photo");
            ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));

            if (this.pendingImagePath != null)
            {
                var tex = this.media.Load(this.pendingImagePath);
                if (tex is { Width: > 0, Height: > 0 })
                {
                    var scale = MathF.Min(MathF.Min(width / tex.Width, Ui.Px(220f) / tex.Height), 1f);
                    var w = tex.Width * scale;
                    var h = tex.Height * scale;
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((width - w) * 0.5f));
                    ImGui.Image(tex.Handle, new Vector2(w, h));
                }
            }

            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
            this.pendingNsfw = this.kit.Checkbox("##img_nsfw", this.pendingNsfw);
            ImGui.SameLine(0f, Ui.Px(9f));
            using (this.fonts.Caption.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
                ImGui.TextUnformatted("Mark NSFW (blurred until tapped)");

            ImGui.Dummy(new Vector2(0f, Ui.Px(12f)));
            this.kit.TextField("##img_caption", ref this.draft, "Caption (optional)", width);

            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));
            var half = (width - Ui.Px(10f)) * 0.5f;
            if (this.kit.SecondaryButton("##img_cancel", "Cancel", half))
            {
                this.pendingImagePath = null;
                this.draft = string.Empty;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine(0f, Ui.Px(10f));
            if (this.kit.PrimaryButton("##img_send", "Send", half) && this.pendingImagePath != null)
            {
                this.chat.SendImage(peer, this.pendingImagePath, this.pendingNsfw, this.draft.Trim());
                this.pendingImagePath = null;
                this.draft = string.Empty;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }
}
