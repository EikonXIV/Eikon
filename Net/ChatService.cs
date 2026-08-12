using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Dalamud.Plugin.Services;
using Eikon.Contracts;
using Eikon.Crypto;
using Eikon.Services;
using CryptoLib = Eikon.Crypto.Crypto;

namespace Eikon.Net;

internal enum MessageState
{
    Pending,
    Sent,
    Delivered,
    Failed,
}

// Orchestrates messaging: encrypts outgoing text and hands ciphertext to the relay, decrypts
// incoming ciphertext into per-peer threads, and tracks delivery. The relay and server only ever
// see ciphertext.
internal sealed class ChatService
{
    public sealed class Message
    {
        public bool Mine;
        public string Text = string.Empty;   // the message text, or the image caption
        public MessageState State;
        public string? ClientMsgId;
        public Guid? MessageId;
        public bool IsImage;
        public bool IsAlbum;                  // an album share card (album id + name + photo count)
        public Guid? AlbumId;
        public string? AlbumName;
        public int AlbumCount;
        public bool IsEvent;                  // an event share card (a snapshot rendered without access)
        public Guid EventId;
        public EventKindElement EventKind;
        public string? EventTitle;
        public string? EventBannerPreset;
        public DateTimeOffset EventStartsAt;
        public string? EventClock;
        public string? EventTzLabel;
        public string? EventLocation;
        public long EventAttending;
        public long? EventCapacity;
        public bool Nsfw;                     // image marked sensitive by the sender -> blur until revealed
        public string? ImageId;              // local sealed-image id (see ChatMediaCache)
        // Where the photo blob lives and the key that opens it, kept so the download can be retried long
        // after the message arrived. Receiving no longer blocks on the fetch, so these are the only way
        // back to the bytes. Null on messages received before this field existed.
        public string? MediaKey;
        public string? MediaBlobKey;
        public string? OutEnvelope;          // image: the ratchet payload, kept so we can resend on a re-handshake
        public DateTimeOffset? SentAt;       // sent (local clock) or received (server createdAt); null for messages from before this field existed
    }

    // Prefix marking a ratchet message whose plaintext is an image envelope (else it's plain text). A
    // control char that won't occur in normal chat text, so existing text messages are unaffected.
    private const string ImageMagic = "img:";
    // Prefix marking a ratchet message whose plaintext is an album share card. Same idea as
    // ImageMagic: an album-less client shows the raw envelope; an album-aware one renders a card.
    private const string AlbumMagic = "album:";
    // Prefix marking a ratchet message whose plaintext is an event share card. Same idea as AlbumMagic.
    private const string EventMagic = "event:";

    private readonly RelayClient relay;
    private readonly MessageCrypto crypto;
    private readonly KeyVault vault;
    private readonly IApiClient api;
    private readonly AuthService auth;
    private readonly ChatMediaCache media;
    private readonly IPluginLog log;
    private readonly object gate = new();
    private readonly Dictionary<Guid, List<Message>> threads = new();
    private readonly Dictionary<string, Message> pending = new();
    private readonly HashSet<Guid> seen = new();
    private readonly Dictionary<Guid, DateTime> rekeySentAt = new();      // debounce outgoing rekey requests
    private readonly Dictionary<Guid, DateTime> rekeyHandledAt = new();   // debounce inbound rekey handling
    private static readonly TimeSpan RekeyDebounce = TimeSpan.FromSeconds(5);

    // On-demand image fetches: what's in flight, how many tries each has had, and when to try next.
    private readonly object mediaGate = new();
    private readonly HashSet<string> mediaFetching = new();
    private readonly Dictionary<string, int> mediaAttempts = new();
    private readonly Dictionary<string, DateTime> mediaRetryAfter = new();
    private const int MediaMaxAttempts = 4;
    private readonly string historyPath;
    private bool started;
    private bool historyLoaded;
    private bool historyLoadFailed;   // file exists but couldn't be read/decrypted -> don't overwrite it

    public ChatService(RelayClient relay, MessageCrypto crypto, KeyVault vault, IApiClient api, AuthService auth, ChatMediaCache media, IPluginLog log)
    {
        this.relay = relay;
        this.crypto = crypto;
        this.vault = vault;
        this.api = api;
        this.auth = auth;
        this.media = media;
        this.log = log;
        this.historyPath = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "threads.bin");
    }

    public void Start()
    {
        if (this.started)
            return;
        this.started = true;
        this.relay.MessageReceived += this.OnMessage;
        this.relay.Sent += this.OnSent;
        this.relay.Delivered += this.OnDelivered;
        this.relay.RekeyRequested += this.OnRekeyRequested;
        this.relay.Start();
    }

    // A message from a peer was decrypted and stored (peer id). Raised once per message, so it's the
    // signal for anything user-facing: a raw relay frame may never become a readable message.
    public event Action<Guid>? MessageAdded;

    public IReadOnlyList<Message> Thread(Guid peer)
    {
        lock (this.gate)
        {
            this.EnsureLoaded();
            return this.threads.TryGetValue(peer, out var list) ? list.ToList() : new List<Message>();
        }
    }

    // Harness seam (Vitrine has InternalsVisibleTo): drop a ready-made message into a thread for offline
    // screenshots, without going through the encrypt/relay path.
    internal void SeedForTest(Guid peer, Message message)
    {
        lock (this.gate)
        {
            this.EnsureLoaded();
            this.GetThread(peer).Add(message);
        }
    }

    // Drop a peer's decrypted history and rewrite the store without it. This is the only durable copy -
    // the server keeps a delivery queue, not an archive - so a permanent delete happens here, not there.
    // A later message from the peer simply starts the thread again, empty.
    public void ForgetThread(Guid peer)
    {
        lock (this.gate)
        {
            this.EnsureLoaded();
            if (!this.threads.Remove(peer))
                return;

            this.Save();
        }
    }

    public void Send(Guid peer, string text)
    {
        var clientMsgId = Guid.NewGuid().ToString();
        var message = new Message { Mine = true, Text = text, State = MessageState.Pending, ClientMsgId = clientMsgId, SentAt = DateTimeOffset.UtcNow };
        lock (this.gate)
        {
            this.EnsureLoaded();
            this.GetThread(peer).Add(message);
            this.pending[clientMsgId] = message;
            this.Save();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var enc = await this.crypto.EncryptAsync(peer, text, CancellationToken.None);
                if (enc is null)
                {
                    message.State = MessageState.Failed;
                    return;
                }

                this.relay.SendMessage(peer.ToString(), enc.Value.Ciphertext, enc.Value.Header, enc.Value.Nonce, clientMsgId);
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Send failed.");
                message.State = MessageState.Failed;
            }
        });
    }

    // Share an album in chat: send a ratchet message whose plaintext is an album card envelope (album
    // id, name, photo count). Access is granted separately by the caller (before sending, for a private
    // album). The recipient renders a "View album" card. Old clients show the raw envelope.
    public void SendAlbumCard(Guid peer, Guid albumId, string name, int count)
    {
        var clientMsgId = Guid.NewGuid().ToString();
        var envelope = AlbumMagic + JsonSerializer.Serialize(new { a = albumId.ToString(), n = name, c = count });
        var message = new Message
        {
            Mine = true, State = MessageState.Pending, ClientMsgId = clientMsgId, SentAt = DateTimeOffset.UtcNow,
            IsAlbum = true, AlbumId = albumId, AlbumName = name, AlbumCount = count, OutEnvelope = envelope,
        };
        lock (this.gate)
        {
            this.EnsureLoaded();
            this.GetThread(peer).Add(message);
            this.pending[clientMsgId] = message;
            this.Save();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var enc = await this.crypto.EncryptAsync(peer, envelope, CancellationToken.None);
                if (enc is null) { message.State = MessageState.Failed; return; }
                this.relay.SendMessage(peer.ToString(), enc.Value.Ciphertext, enc.Value.Header, enc.Value.Nonce, clientMsgId);
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Sharing album failed.");
                message.State = MessageState.Failed;
            }
        });
    }

    // Share an event in chat: a ratchet message whose plaintext is an event card snapshot. The recipient
    // renders the card (banner, kind, title, time, location) without needing access to the event; a tap
    // tries to open it. Old clients show the raw envelope.
    public void SendEventCard(Guid peer, EventShare e)
    {
        var clientMsgId = Guid.NewGuid().ToString();
        var envelope = EventMagic + JsonSerializer.Serialize(new
        {
            e = e.EventId.ToString(), k = (int)e.Kind, t = e.Title, b = e.BannerPreset,
            s = e.StartsAt.ToString("o"), hc = e.HostClock, tz = e.HostTzLabel, loc = e.Location,
            at = e.Attending, cap = e.Capacity,
        });
        var message = new Message
        {
            Mine = true, State = MessageState.Pending, ClientMsgId = clientMsgId, SentAt = DateTimeOffset.UtcNow,
            IsEvent = true, EventId = e.EventId, EventKind = e.Kind, EventTitle = e.Title, EventBannerPreset = e.BannerPreset,
            EventStartsAt = e.StartsAt, EventClock = e.HostClock, EventTzLabel = e.HostTzLabel, EventLocation = e.Location,
            EventAttending = e.Attending, EventCapacity = e.Capacity, OutEnvelope = envelope,
        };
        lock (this.gate)
        {
            this.EnsureLoaded();
            this.GetThread(peer).Add(message);
            this.pending[clientMsgId] = message;
            this.Save();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var enc = await this.crypto.EncryptAsync(peer, envelope, CancellationToken.None);
                if (enc is null) { message.State = MessageState.Failed; return; }
                this.relay.SendMessage(peer.ToString(), enc.Value.Ciphertext, enc.Value.Header, enc.Value.Nonce, clientMsgId);
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Sharing event failed.");
                message.State = MessageState.Failed;
            }
        });
    }

    // Send a photo: resize + JPEG-encode, encrypt the blob under a fresh key, upload the opaque blob,
    // and send a normal ratchet message whose plaintext is the image envelope (storage key + blob key
    // + nsfw flag + caption). The relay only ever sees ciphertext.
    public void SendImage(Guid peer, string imagePath, bool nsfw, string caption)
    {
        var clientMsgId = Guid.NewGuid().ToString();
        var imageId = Guid.NewGuid().ToString();
        var message = new Message
        {
            Mine = true, Text = caption, State = MessageState.Pending, ClientMsgId = clientMsgId,
            IsImage = true, Nsfw = nsfw, ImageId = imageId, SentAt = DateTimeOffset.UtcNow,
        };
        lock (this.gate)
        {
            this.EnsureLoaded();
            this.GetThread(peer).Add(message);
            this.pending[clientMsgId] = message;
            this.Save();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token)) { message.State = MessageState.Failed; return; }

                var bytes = ImageCrop.ResizeJpeg(imagePath, 1280);
                this.media.Save(imageId, bytes);   // sealed local copy for display + history

                var key = CryptoLib.Random(32);
                var storageKey = await this.api.UploadChatMediaAsync(token, CryptoLib.EncryptBlob(key, bytes), CancellationToken.None);
                var envelope = ImageMagic + JsonSerializer.Serialize(new
                {
                    sk = storageKey, k = Convert.ToBase64String(key), nsfw, cap = caption,
                });
                message.OutEnvelope = envelope;   // retain so a re-handshake can resend it

                var enc = await this.crypto.EncryptAsync(peer, envelope, CancellationToken.None);
                if (enc is null) { message.State = MessageState.Failed; return; }
                this.relay.SendMessage(peer.ToString(), enc.Value.Ciphertext, enc.Value.Header, enc.Value.Nonce, clientMsgId);
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Send image failed.");
                message.State = MessageState.Failed;
            }
        });
    }

    private void OnMessage(EncryptedMessageDto dto)
    {
        _ = Task.Run(async () =>
        {
            var text = await this.crypto.DecryptAsync(dto.SenderId, dto, CancellationToken.None);
            if (text is null)
            {
                // We couldn't decrypt it. What to do depends on why (see DecideUndecryptable): a ratchet
                // desync recovers via ack + re-handshake; a changed peer identity (a reinstall) is acked
                // only, so it stops redelivering forever while the "identity changed" banner drives
                // re-verification (a rekey can't help, and we must never silently re-pin); a forged or
                // concurrent-handshake initial is left queued and quiet.
                var action = DecideUndecryptable(MessageCrypto.IsInitialHeader(dto.Header), this.crypto.Mismatched(dto.SenderId));
                if (action.Ack)
                    this.relay.Ack(dto.Id);
                if (action.Rekey)
                    this.RequestRekey(dto.SenderId);
                return;
            }

            // An image message carries an envelope naming the blob and the key that opens it. We only
            // parse it here; the photo itself is fetched on demand (EnsureImage) when the bubble draws.
            // Receiving must never depend on the network, or a failed fetch would drop the message
            // un-acked and the relay would redeliver it - and re-notify - forever.
            Message message;
            if (text.StartsWith(ImageMagic, StringComparison.Ordinal))
            {
                message = BuildImageMessage(dto, text)
                    ?? new Message { Mine = false, Text = text, State = MessageState.Delivered, MessageId = dto.Id, SentAt = dto.CreatedAt };
            }
            else if (text.StartsWith(AlbumMagic, StringComparison.Ordinal))
            {
                message = BuildAlbumMessage(dto, text)
                    ?? new Message { Mine = false, Text = text, State = MessageState.Delivered, MessageId = dto.Id, SentAt = dto.CreatedAt };
            }
            else if (text.StartsWith(EventMagic, StringComparison.Ordinal))
            {
                message = BuildEventMessage(dto, text)
                    ?? new Message { Mine = false, Text = text, State = MessageState.Delivered, MessageId = dto.Id, SentAt = dto.CreatedAt };
            }
            else
            {
                message = new Message { Mine = false, Text = text, State = MessageState.Delivered, MessageId = dto.Id, SentAt = dto.CreatedAt };
            }

            var added = false;
            lock (this.gate)
            {
                this.EnsureLoaded();
                if (this.seen.Add(dto.Id))
                {
                    this.GetThread(dto.SenderId).Add(message);
                    this.Save();
                    added = true;
                }
            }

            // Announce only a message that actually landed in a thread, so a frame we couldn't turn into
            // one can't raise a notification with nothing behind it (and a redelivery can't re-raise it).
            if (added)
                this.MessageAdded?.Invoke(dto.SenderId);

            // Ack on every successful decrypt (new or duplicate) so the relay stops redelivering.
            this.relay.Ack(dto.Id);
        });
    }

    // The parts of an image envelope: where the blob lives, the key that opens it, and how to present it.
    internal readonly record struct ImageEnvelope(string StorageKey, string BlobKey, bool Nsfw, string Caption);

    // Parse an image envelope. Pure (no I/O), so receiving an image can't fail on the network and is
    // unit-testable. Returns null when the payload isn't a well-formed envelope.
    internal static ImageEnvelope? ParseImageEnvelope(string envelopeText)
    {
        try
        {
            using var doc = JsonDocument.Parse(envelopeText[ImageMagic.Length..]);
            var root = doc.RootElement;
            return new ImageEnvelope(
                root.GetProperty("sk").GetString()!,
                root.GetProperty("k").GetString()!,
                root.TryGetProperty("nsfw", out var n) && n.ValueKind == JsonValueKind.True,
                root.TryGetProperty("cap", out var c) ? c.GetString() ?? string.Empty : string.Empty);
        }
        catch
        {
            return null;
        }
    }

    private static Message? BuildImageMessage(EncryptedMessageDto dto, string envelopeText)
    {
        if (ParseImageEnvelope(envelopeText) is not { } env)
            return null;
        return new Message
        {
            Mine = false, Text = env.Caption, State = MessageState.Delivered, MessageId = dto.Id,
            IsImage = true, Nsfw = env.Nsfw, ImageId = dto.Id.ToString(), SentAt = dto.CreatedAt,
            MediaKey = env.StorageKey, MediaBlobKey = env.BlobKey,
        };
    }

    // Fetch and unseal an image's blob, once, on demand. The receive path no longer does this: a photo
    // that can't be fetched now leaves a visible bubble that retries, instead of dropping the whole
    // message un-acked and having the relay redeliver it forever.
    public void EnsureImage(Message message)
    {
        if (message.ImageId is not { } imageId || message.MediaKey is not { } storageKey || message.MediaBlobKey is null)
            return;
        if (this.media.Has(imageId))
            return;

        lock (this.mediaGate)
        {
            if (this.mediaFetching.Contains(imageId))
                return;
            if (this.mediaRetryAfter.TryGetValue(imageId, out var next) && DateTime.UtcNow < next)
                return;
            this.mediaFetching.Add(imageId);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                    return;
                var url = await this.api.ChatMediaViewUrlAsync(token, storageKey, CancellationToken.None);
                var blob = await this.api.DownloadBytesAsync(url, CancellationToken.None);
                var bytes = CryptoLib.DecryptBlob(Convert.FromBase64String(message.MediaBlobKey!), blob);
                if (bytes is null)
                    return;
                this.media.Save(imageId, bytes);
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Fetching a chat image failed.");
            }
            finally
            {
                lock (this.mediaGate)
                {
                    this.mediaFetching.Remove(imageId);
                    if (!this.media.Has(imageId))
                    {
                        this.mediaAttempts.TryGetValue(imageId, out var tries);
                        this.mediaAttempts[imageId] = ++tries;
                        // Back off hard, then stop: a purged or corrupt blob is never coming back, and the
                        // bubble shows "Photo unavailable" once the attempts are spent.
                        this.mediaRetryAfter[imageId] = DateTime.UtcNow.AddSeconds(Math.Min(300, 5 * Math.Pow(4, tries)));
                    }
                }
            }
        });
    }

    // True once we've tried enough times to say the photo isn't coming (drives the placeholder bubble).
    public bool ImageUnavailable(Message message)
    {
        if (message.ImageId is not { } imageId)
            return false;
        if (this.media.Has(imageId))
            return false;
        if (message.MediaKey is null)
            return true;   // received before the key was persisted: nothing to retry with
        lock (this.mediaGate)
            return this.mediaAttempts.TryGetValue(imageId, out var tries) && tries >= MediaMaxAttempts;
    }

    // Parse an album share card envelope into a card message. Nothing to fetch: the album's photos load
    // on demand through its grant-checked route when the recipient opens it.
    private static Message? BuildAlbumMessage(EncryptedMessageDto dto, string envelopeText)
    {
        try
        {
            using var doc = JsonDocument.Parse(envelopeText[AlbumMagic.Length..]);
            var root = doc.RootElement;
            var albumId = Guid.Parse(root.GetProperty("a").GetString()!);
            var name = root.TryGetProperty("n", out var n) ? n.GetString() ?? string.Empty : string.Empty;
            var count = root.TryGetProperty("c", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
            return new Message
            {
                Mine = false, State = MessageState.Delivered, MessageId = dto.Id, SentAt = dto.CreatedAt,
                IsAlbum = true, AlbumId = albumId, AlbumName = name, AlbumCount = count,
            };
        }
        catch
        {
            return null;
        }
    }

    private static Message? BuildEventMessage(EncryptedMessageDto dto, string envelopeText)
    {
        try
        {
            using var doc = JsonDocument.Parse(envelopeText[EventMagic.Length..]);
            var root = doc.RootElement;
            return new Message
            {
                Mine = false, State = MessageState.Delivered, MessageId = dto.Id, SentAt = dto.CreatedAt, IsEvent = true,
                EventId = Guid.Parse(root.GetProperty("e").GetString()!),
                EventKind = (EventKindElement)root.GetProperty("k").GetInt32(),
                EventTitle = root.TryGetProperty("t", out var t) ? t.GetString() : null,
                EventBannerPreset = root.TryGetProperty("b", out var b) ? b.GetString() : null,
                EventStartsAt = root.TryGetProperty("s", out var s) && s.GetString() is { } iso ? DateTimeOffset.Parse(iso) : default,
                EventClock = root.TryGetProperty("hc", out var hc) ? hc.GetString() : null,
                EventTzLabel = root.TryGetProperty("tz", out var tz) ? tz.GetString() : null,
                EventLocation = root.TryGetProperty("loc", out var loc) ? loc.GetString() : null,
                EventAttending = root.TryGetProperty("at", out var at) && at.TryGetInt64(out var atv) ? atv : 0,
                EventCapacity = root.TryGetProperty("cap", out var cap) && cap.ValueKind == JsonValueKind.Number && cap.TryGetInt64(out var cv) ? cv : null,
            };
        }
        catch
        {
            return null;
        }
    }

    private void OnSent(string clientMsgId, Guid messageId)
    {
        lock (this.gate)
        {
            if (this.pending.TryGetValue(clientMsgId, out var message))
            {
                message.State = MessageState.Sent;
                message.MessageId = messageId;
                this.pending.Remove(clientMsgId);
                this.Save();
            }
        }
    }

    private void OnDelivered(Guid messageId)
    {
        lock (this.gate)
        {
            var changed = false;
            foreach (var list in this.threads.Values)
                foreach (var m in list)
                    if (m.MessageId == messageId)
                    {
                        m.State = MessageState.Delivered;
                        changed = true;
                    }
            if (changed)
                this.Save();
        }
    }

    // What to do with a message we couldn't decrypt, given whether it carries the X3DH initial bit and
    // whether the peer's identity no longer matches our pin. Split out (and internal) so the branching
    // is unit-testable without constructing ChatService.
    internal readonly record struct UndecryptableAction(bool Ack, bool Rekey);

    internal static UndecryptableAction DecideUndecryptable(bool isInitial, bool identityMismatched)
    {
        // A reinstalled peer's greeting fails our identity pin before X3DH even runs. Acking stops the
        // relay redelivering it forever (the phantom "New message" with nothing behind it). A rekey is
        // useless because the resend carries the same changed identity, and re-pinning must stay a user
        // gesture after a safety-number review, so we only ack.
        if (identityMismatched)
            return new UndecryptableAction(Ack: true, Rekey: false);

        // Any non-initial we can't read is a ratchet desync (out of sync, or the session was lost). Ack
        // to stop redelivery and ask the peer to re-handshake; their client resends on a fresh session.
        if (!isInitial)
            return new UndecryptableAction(Ack: true, Rekey: true);

        // An undecryptable initial whose identity still matches the pin is a forged/garbage frame or a
        // concurrent-handshake tie-break the peer will supersede. Leave it queued and stay quiet; the
        // server's stale-'sent' expiry reaps a genuinely dead one.
        return new UndecryptableAction(Ack: false, Rekey: false);
    }

    // Ask a peer to re-handshake (debounced), because we can't decrypt their messages.
    private void RequestRekey(Guid peer)
    {
        lock (this.gate)
        {
            if (this.rekeySentAt.TryGetValue(peer, out var last) && DateTime.UtcNow - last < RekeyDebounce)
                return;
            this.rekeySentAt[peer] = DateTime.UtcNow;
        }
        this.relay.Rekey(peer.ToString());
    }

    // A peer can't decrypt us (they lost their session). Drop our stale session and resend everything
    // not yet delivered on a fresh handshake, so the conversation recovers without any user action.
    private void OnRekeyRequested(Guid peer)
    {
        lock (this.gate)
        {
            if (this.rekeyHandledAt.TryGetValue(peer, out var last) && DateTime.UtcNow - last < RekeyDebounce)
                return;
            this.rekeyHandledAt[peer] = DateTime.UtcNow;
        }

        _ = Task.Run(async () =>
        {
            this.crypto.ResetSession(peer);
            List<Message> pending;
            lock (this.gate)
            {
                this.EnsureLoaded();
                pending = this.threads.TryGetValue(peer, out var list)
                    ? list.FindAll(m => m.Mine && m.State != MessageState.Delivered)
                    : new List<Message>();
            }

            // Sequential so the first send re-runs X3DH and the rest reuse the new session, in order.
            foreach (var message in pending)
                await this.ResendAsync(peer, message);
        });
    }

    private async Task ResendAsync(Guid peer, Message message)
    {
        var payload = message.IsImage || message.IsAlbum || message.IsEvent ? message.OutEnvelope : message.Text;
        if (string.IsNullOrEmpty(payload))
        {
            message.State = MessageState.Failed;   // image envelope wasn't retained (e.g. across a restart)
            return;
        }

        var clientMsgId = Guid.NewGuid().ToString();
        message.ClientMsgId = clientMsgId;
        message.State = MessageState.Pending;
        lock (this.gate)
            this.pending[clientMsgId] = message;

        try
        {
            var enc = await this.crypto.EncryptAsync(peer, payload, CancellationToken.None);
            if (enc is null) { message.State = MessageState.Failed; return; }
            this.relay.SendMessage(peer.ToString(), enc.Value.Ciphertext, enc.Value.Header, enc.Value.Nonce, clientMsgId);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Resend after re-handshake failed.");
            message.State = MessageState.Failed;
        }
    }

    private List<Message> GetThread(Guid peer)
    {
        if (!this.threads.TryGetValue(peer, out var list))
            this.threads[peer] = list = new List<Message>();
        return list;
    }

    // Local message history, sealed to the vault (DPAPI + vault key), so conversations survive a restart
    // without the server ever holding decryptable history. Messages are decrypted once on arrival (the
    // ratchet message key is wiped, preserving forward secrecy); only the resulting plaintext is stored,
    // encrypted at rest and unreadable on a locked/logged-out device. Caller holds the gate.
    private void EnsureLoaded()
    {
        if (this.historyLoaded || !this.vault.IsUnlocked)
            return;   // vault not ready yet: retry on the next access
        this.historyLoaded = true;

        var primary = this.historyPath;
        var backup = this.historyPath + ".bak";
        if (!File.Exists(primary) && !File.Exists(backup))
            return;   // no history yet: legitimately empty

        foreach (var path in new[] { primary, backup })
        {
            try
            {
                if (!File.Exists(path))
                    continue;
                var json = this.vault.OpenLocal(File.ReadAllBytes(path));
                if (json is null)
                    continue;
                var dto = JsonSerializer.Deserialize<Dictionary<string, List<MessageDto>>>(Encoding.UTF8.GetString(json));
                if (dto is null)
                    continue;
                foreach (var (key, msgs) in dto)
                {
                    if (!Guid.TryParse(key, out var peer))
                        continue;
                    var list = this.GetThread(peer);
                    foreach (var m in msgs)
                    {
                        list.Add(new Message { Mine = m.Mine, Text = m.Text, State = (MessageState)m.State, MessageId = m.MessageId, IsImage = m.IsImage, Nsfw = m.Nsfw, ImageId = m.ImageId, MediaKey = m.MediaKey, MediaBlobKey = m.MediaBlobKey, IsAlbum = m.IsAlbum, AlbumId = m.AlbumId, AlbumName = m.AlbumName, AlbumCount = m.AlbumCount, IsEvent = m.IsEvent, EventId = m.EventId, EventKind = (EventKindElement)m.EventKind, EventTitle = m.EventTitle, EventBannerPreset = m.EventBannerPreset, EventStartsAt = m.EventStartsAt, EventClock = m.EventClock, EventTzLabel = m.EventTzLabel, EventLocation = m.EventLocation, EventAttending = m.EventAttending, EventCapacity = m.EventCapacity, SentAt = m.SentAt });
                        if (m.MessageId is { } id)
                            this.seen.Add(id);
                    }
                }
                return;
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, $"Loading chat history from {Path.GetFileName(path)} failed.");
            }
        }

        // File exists but couldn't be read/decrypted: don't overwrite it (a reset deletes it instead).
        this.historyLoadFailed = true;
    }

    // Caller holds the gate. Atomic write (temp + replace) with a .bak, mirroring the session store.
    private void Save()
    {
        if (this.historyLoadFailed || !this.vault.IsUnlocked)
            return;
        try
        {
            var dto = new Dictionary<string, List<MessageDto>>();
            foreach (var (peer, list) in this.threads)
                dto[peer.ToString()] = list.ConvertAll(m => new MessageDto { Mine = m.Mine, Text = m.Text, State = (int)m.State, MessageId = m.MessageId, IsImage = m.IsImage, Nsfw = m.Nsfw, ImageId = m.ImageId, MediaKey = m.MediaKey, MediaBlobKey = m.MediaBlobKey, IsAlbum = m.IsAlbum, AlbumId = m.AlbumId, AlbumName = m.AlbumName, AlbumCount = m.AlbumCount, IsEvent = m.IsEvent, EventId = m.EventId, EventKind = (int)m.EventKind, EventTitle = m.EventTitle, EventBannerPreset = m.EventBannerPreset, EventStartsAt = m.EventStartsAt, EventClock = m.EventClock, EventTzLabel = m.EventTzLabel, EventLocation = m.EventLocation, EventAttending = m.EventAttending, EventCapacity = m.EventCapacity, SentAt = m.SentAt });
            var sealedBytes = this.vault.SealLocal(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto)));

            var tmp = this.historyPath + ".tmp";
            File.WriteAllBytes(tmp, sealedBytes);
            if (File.Exists(this.historyPath))
                File.Replace(tmp, this.historyPath, this.historyPath + ".bak");
            else
                File.Move(tmp, this.historyPath);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Saving chat history failed.");
        }
    }

    private sealed class MessageDto
    {
        public bool Mine { get; set; }
        public string Text { get; set; } = string.Empty;
        public int State { get; set; }
        public Guid? MessageId { get; set; }
        public bool IsImage { get; set; }
        public bool Nsfw { get; set; }
        public string? ImageId { get; set; }
        public string? MediaKey { get; set; }
        public string? MediaBlobKey { get; set; }
        public bool IsAlbum { get; set; }
        public Guid? AlbumId { get; set; }
        public string? AlbumName { get; set; }
        public int AlbumCount { get; set; }
        public bool IsEvent { get; set; }
        public Guid EventId { get; set; }
        public int EventKind { get; set; }
        public string? EventTitle { get; set; }
        public string? EventBannerPreset { get; set; }
        public DateTimeOffset EventStartsAt { get; set; }
        public string? EventClock { get; set; }
        public string? EventTzLabel { get; set; }
        public string? EventLocation { get; set; }
        public long EventAttending { get; set; }
        public long? EventCapacity { get; set; }
        public DateTimeOffset? SentAt { get; set; }
    }
}
