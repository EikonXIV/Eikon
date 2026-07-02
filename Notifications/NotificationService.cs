using System.Collections.Concurrent;
using Eikon.Config;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.Windows;

namespace Eikon.Notifications;

// What a toast is about, which decides its subtitle and where a tap goes.
internal enum ToastKind
{
    Message,          // a new chat message (coalesced by peer)
    AlbumRequest,     // someone asked to see one of your albums -> the requests screen
    AlbumApproved,    // an owner approved your request -> the album viewer
}

// One pending toast. Message toasts coalesce per peer: repeats bump Count and push ExpiresAt forward
// rather than stacking. Album toasts are keyed by peer + album and carry the album so a tap can open it.
// Timing uses Environment.TickCount64 (thread-safe, no ImGui).
internal sealed class NotificationToast
{
    public ToastKind Kind = ToastKind.Message;
    public Guid Peer;
    public string Name = "New message";
    public string? Subtitle;      // null for messages (rendered from Count); set for album toasts
    public Guid? AlbumId;
    public string? AlbumName;
    public int Count;
    public long ExpiresAt;
}

// Drives toasts. The relay's receive task only enqueues cheap, thread-safe payloads (a sender id for
// messages, a notice for album events); all decisions, name lookups, and the sound happen on the UI
// thread in Tick() so ImGui and game calls stay on the right thread. Discreet by design: a toast shows
// the other person's name and a short subtitle, never message content.
internal sealed class NotificationService
{
    private const int LifetimeMs = 5000;
    private const int SoundCooldownMs = 4000;
    private const int MaxToasts = 3;

    private readonly Configuration config;
    private readonly InboxService inbox;
    private readonly ScreenRouter router;
    private readonly Selection selection;
    private readonly MainWindow mainWindow;
    private readonly SoundService sound;
    private readonly ConcurrentQueue<Guid> pending = new();
    private readonly ConcurrentQueue<(ToastKind Kind, AlbumNotice Notice)> albumPending = new();
    private readonly List<NotificationToast> toasts = new();
    private long lastSoundAt;

    public NotificationService(Configuration config, RelayClient relay, ChatService chat, InboxService inbox, ScreenRouter router, Selection selection, MainWindow mainWindow, SoundService sound)
    {
        this.config = config;
        this.inbox = inbox;
        this.router = router;
        this.selection = selection;
        this.mainWindow = mainWindow;
        this.sound = sound;

        relay.MessageReceived += m => this.pending.Enqueue(m.SenderId);
        relay.AlbumRequestReceived += n => this.albumPending.Enqueue((ToastKind.AlbumRequest, n));
        relay.AlbumGranted += n => this.albumPending.Enqueue((ToastKind.AlbumApproved, n));
        chat.Start();   // keep the relay running so notifications fire even before Messages is opened
    }

    // Raised when a toast is clicked; the host restores the app and opens the toast's target.
    public event Action<NotificationToast>? OpenRequested;

    public IReadOnlyList<NotificationToast> Toasts => this.toasts;

    // UI thread, every frame. Drains incoming events into coalesced toasts, prunes expired ones, and
    // plays the sound (rate-limited). Returns whether any toast is visible.
    public bool Tick()
    {
        this.inbox.EnsureLoaded();   // keep conversation names available for the toast title
        var now = Environment.TickCount64;
        var shown = false;
        while (this.pending.TryDequeue(out var peer))
        {
            if (!this.config.NotificationsEnabled) continue;
            if (this.config.MutedConversations.Contains(peer.ToString())) continue;
            if (this.IsViewing(peer)) continue;

            var name = this.NameOf(peer);
            var existing = this.toasts.Find(t => t.Kind == ToastKind.Message && t.Peer == peer);
            if (existing != null)
            {
                existing.Count++;
                existing.Name = name;
                existing.ExpiresAt = now + LifetimeMs;
            }
            else
            {
                this.Add(new NotificationToast { Peer = peer, Name = name, Count = 1, ExpiresAt = now + LifetimeMs });
            }

            shown = true;
        }

        while (this.albumPending.TryDequeue(out var item))
        {
            if (!this.config.NotificationsEnabled) continue;
            var (kind, notice) = item;
            if (this.IsViewingAlbums(kind, notice.AlbumId)) continue;

            var subtitle = kind == ToastKind.AlbumRequest ? "Requested album access" : "Unlocked an album for you";
            var existing = this.toasts.Find(t => t.Kind == kind && t.Peer == notice.PeerId && t.AlbumId == notice.AlbumId);
            if (existing != null)
            {
                existing.ExpiresAt = now + LifetimeMs;
            }
            else
            {
                this.Add(new NotificationToast
                {
                    Kind = kind, Peer = notice.PeerId, Name = notice.PeerName, Subtitle = subtitle,
                    AlbumId = notice.AlbumId, AlbumName = notice.AlbumName, Count = 1, ExpiresAt = now + LifetimeMs,
                });
            }

            shown = true;
        }

        this.toasts.RemoveAll(t => t.ExpiresAt <= now);

        if (shown && this.config.NotificationSoundEnabled && now - this.lastSoundAt > SoundCooldownMs)
        {
            this.lastSoundAt = now;
            this.sound.Play(this.config.NotificationVolume);
        }

        return this.toasts.Count > 0;
    }

    public void Open(NotificationToast toast)
    {
        this.toasts.RemoveAll(t => t.Kind == toast.Kind && t.Peer == toast.Peer && t.AlbumId == toast.AlbumId);
        this.OpenRequested?.Invoke(toast);
    }

    private void Add(NotificationToast toast)
    {
        this.toasts.Add(toast);
        if (this.toasts.Count > MaxToasts)
            this.toasts.RemoveAt(0);
    }

    private bool IsViewing(Guid peer) =>
        this.mainWindow.IsOpen && this.router.Current == Screen.Chat && this.selection.ProfileUserId == peer;

    // Suppress an album toast when the member is already looking at where it would take them: the
    // requests screen for a new request, or that album's viewer for an approval.
    private bool IsViewingAlbums(ToastKind kind, Guid albumId)
    {
        if (!this.mainWindow.IsOpen)
            return false;
        return kind == ToastKind.AlbumRequest
            ? this.router.Current == Screen.AlbumRequests
            : this.router.Current == Screen.AlbumViewer && this.selection.AlbumId == albumId;
    }

    private string NameOf(Guid peer)
    {
        foreach (var c in this.inbox.Conversations)
            if (c.UserId == peer)
                return c.DisplayName;
        return "New message";
    }
}
