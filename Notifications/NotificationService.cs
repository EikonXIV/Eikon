using System.Collections.Concurrent;
using Eikon.Config;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.Windows;

namespace Eikon.Notifications;

// One pending toast for a peer. Coalesced: repeated messages bump Count and push ExpiresAt forward
// rather than stacking new toasts. Timing uses Environment.TickCount64 (thread-safe, no ImGui).
internal sealed class NotificationToast
{
    public Guid Peer;
    public string Name = "New message";
    public int Count;
    public long ExpiresAt;
}

// Drives new-message notifications. The relay's receive task only enqueues the sender id (cheap,
// thread-safe); all decisions, name lookups, and the sound happen on the UI thread in Tick() so ImGui
// and game calls stay on the right thread. Discreet by design: a toast shows the sender's name and a
// count, never message content.
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
        chat.Start();   // keep the relay running so notifications fire even before Messages is opened
    }

    // Raised when a toast is clicked; the host restores the app and opens the conversation.
    public event Action<Guid, string>? OpenRequested;

    public IReadOnlyList<NotificationToast> Toasts => this.toasts;

    // UI thread, every frame. Drains incoming senders into coalesced toasts, prunes expired ones, and
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
            var existing = this.toasts.Find(t => t.Peer == peer);
            if (existing != null)
            {
                existing.Count++;
                existing.Name = name;
                existing.ExpiresAt = now + LifetimeMs;
            }
            else
            {
                this.toasts.Add(new NotificationToast { Peer = peer, Name = name, Count = 1, ExpiresAt = now + LifetimeMs });
                if (this.toasts.Count > MaxToasts)
                    this.toasts.RemoveAt(0);
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

    public void Open(Guid peer)
    {
        var name = this.toasts.Find(t => t.Peer == peer)?.Name ?? this.NameOf(peer);
        this.toasts.RemoveAll(t => t.Peer == peer);
        this.OpenRequested?.Invoke(peer, name);
    }

    private bool IsViewing(Guid peer) =>
        this.mainWindow.IsOpen && this.router.Current == Screen.Chat && this.selection.ProfileUserId == peer;

    private string NameOf(Guid peer)
    {
        foreach (var c in this.inbox.Conversations)
            if (c.UserId == peer)
                return c.DisplayName;
        return "New message";
    }
}
