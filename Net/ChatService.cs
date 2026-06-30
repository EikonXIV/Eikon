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
        public bool Nsfw;                     // image marked sensitive by the sender -> blur until revealed
        public string? ImageId;              // local sealed-image id (see ChatMediaCache)
        public string? OutEnvelope;          // image: the ratchet payload, kept so we can resend on a re-handshake
        public DateTimeOffset? SentAt;       // sent (local clock) or received (server createdAt); null for messages from before this field existed
    }

    // Prefix marking a ratchet message whose plaintext is an image envelope (else it's plain text). A
    // control char that won't occur in normal chat text, so existing text messages are unaffected.
    private const string ImageMagic = "img:";

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

    public IReadOnlyList<Message> Thread(Guid peer)
    {
        lock (this.gate)
        {
            this.EnsureLoaded();
            return this.threads.TryGetValue(peer, out var list) ? list.ToList() : new List<Message>();
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
                // A non-initial message we can't decrypt and hold no session for is a desync (our peer
                // and we are out of sync). Ack it so it stops redelivering, and ask the sender to
                // re-handshake; their client will resend on a fresh session. (An undecryptable *initial*
                // is likely forged, so we leave it queued and stay quiet.)
                if (!this.crypto.HasSession(dto.SenderId) && !MessageCrypto.IsInitialHeader(dto.Header))
                {
                    this.relay.Ack(dto.Id);
                    this.RequestRekey(dto.SenderId);
                }

                return;   // otherwise leave it queued for redelivery, don't ack
            }

            // An image message carries an envelope (storage key + blob key) we must fetch + decrypt
            // before showing it. If that fails, don't ack so the relay redelivers it later.
            Message message;
            if (text.StartsWith(ImageMagic, StringComparison.Ordinal))
            {
                var built = await this.BuildImageMessage(dto, text);
                if (built is null)
                    return;
                message = built;
            }
            else
            {
                message = new Message { Mine = false, Text = text, State = MessageState.Delivered, MessageId = dto.Id, SentAt = dto.CreatedAt };
            }

            lock (this.gate)
            {
                this.EnsureLoaded();
                if (this.seen.Add(dto.Id))
                {
                    this.GetThread(dto.SenderId).Add(message);
                    this.Save();
                }
            }

            // Ack on every successful decrypt (new or duplicate) so the relay stops redelivering.
            this.relay.Ack(dto.Id);
        });
    }

    private async Task<Message?> BuildImageMessage(EncryptedMessageDto dto, string envelopeText)
    {
        try
        {
            using var doc = JsonDocument.Parse(envelopeText[ImageMagic.Length..]);
            var root = doc.RootElement;
            var storageKey = root.GetProperty("sk").GetString()!;
            var key = Convert.FromBase64String(root.GetProperty("k").GetString()!);
            var nsfw = root.TryGetProperty("nsfw", out var n) && n.ValueKind == JsonValueKind.True;
            var caption = root.TryGetProperty("cap", out var c) ? c.GetString() ?? string.Empty : string.Empty;

            var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
            if (string.IsNullOrEmpty(token))
                return null;
            var url = await this.api.ChatMediaViewUrlAsync(token, storageKey, CancellationToken.None);
            var blob = await this.api.DownloadBytesAsync(url, CancellationToken.None);
            var bytes = CryptoLib.DecryptBlob(key, blob);
            if (bytes is null)
                return null;

            var imageId = dto.Id.ToString();
            this.media.Save(imageId, bytes);
            return new Message
            {
                Mine = false, Text = caption, State = MessageState.Delivered, MessageId = dto.Id,
                IsImage = true, Nsfw = nsfw, ImageId = imageId, SentAt = dto.CreatedAt,
            };
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Receiving image failed.");
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
        var payload = message.IsImage ? message.OutEnvelope : message.Text;
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
                        list.Add(new Message { Mine = m.Mine, Text = m.Text, State = (MessageState)m.State, MessageId = m.MessageId, IsImage = m.IsImage, Nsfw = m.Nsfw, ImageId = m.ImageId, SentAt = m.SentAt });
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
                dto[peer.ToString()] = list.ConvertAll(m => new MessageDto { Mine = m.Mine, Text = m.Text, State = (int)m.State, MessageId = m.MessageId, IsImage = m.IsImage, Nsfw = m.Nsfw, ImageId = m.ImageId, SentAt = m.SentAt });
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
        public DateTimeOffset? SentAt { get; set; }
    }
}
