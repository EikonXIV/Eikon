using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Eikon.Config;
using Eikon.Contracts;

namespace Eikon.Net;

// An album access event from the relay: the other person, and the album it concerns.
internal readonly record struct AlbumNotice(Guid PeerId, string PeerName, Guid AlbumId, string AlbumName);

// Long-lived WebSocket to the relay (ARCHITECTURE 6). Connects with the access token, receives server
// frames, and sends client frames; reconnects with backoff. Only ever carries ciphertext. Events
// fire on the receive task; handlers must tolerate that.
internal sealed class RelayClient : IDisposable
{
    private readonly string serverBaseUrl;
    private readonly ITokenProvider auth;
    private readonly ILog log;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly ConcurrentQueue<string> outbox = new();
    private ClientWebSocket? socket;
    private CancellationTokenSource? cts;
    private Task? runner;

    // Depends on the ITokenProvider and ILog seams (both resolve to the same singletons the plugin
    // registers) rather than AuthService/IPluginLog directly, so the loop can be driven under test.
    public RelayClient(Configuration config, ITokenProvider auth, ILog log)
        : this(config.ServerBaseUrl, auth, log)
    {
    }

    // Test seam: takes the relay base URL directly so the loop can be constructed without a
    // Configuration, which pulls in the Dalamud runtime. Production uses the Configuration ctor above.
    internal RelayClient(string serverBaseUrl, ITokenProvider auth, ILog log)
    {
        this.serverBaseUrl = serverBaseUrl;
        this.auth = auth;
        this.log = log;
    }

    public bool Connected { get; private set; }

    // The receive/reconnect loop task, exposed to tests so they can assert Dispose joined it (the loop
    // is fully stopped on return) rather than only signalling cancellation.
    internal Task? Runner => this.runner;

    public event Action<EncryptedMessageDto>? MessageReceived;

    public event Action<string, Guid>? Sent;

    public event Action<Guid>? Delivered;

    public event Action<Guid>? RekeyRequested;

    // Album access events pushed from the server (not chat messages): someone asked to see one of your
    // albums, or an owner approved your request. Carry the other person's name and the album so a toast
    // reads without a lookup.
    public event Action<AlbumNotice>? AlbumRequestReceived;

    public event Action<AlbumNotice>? AlbumGranted;

    public void Start()
    {
        if (this.cts != null)
            return;
        this.cts = new CancellationTokenSource();
        // Keep the handle so Dispose can join the loop; a fire-and-forget task that outlives Dispose
        // roots the plugin's load context and makes Dalamud's unload fail.
        this.runner = this.RunAsync(this.cts.Token);
    }

    public void SendMessage(string recipientId, string ciphertext, string header, string nonce, string clientMsgId)
    {
        this.Enqueue(JsonSerializer.Serialize(new { t = "send", msg = new { recipientId, ciphertext, header, nonce }, clientMsgId }));
    }

    public void Ack(Guid messageId)
    {
        this.Enqueue(JsonSerializer.Serialize(new { t = "ack", messageId }));
    }

    // Ask a peer to re-handshake (we hold no session that can decrypt their messages).
    public void Rekey(string recipientId)
    {
        this.Enqueue(JsonSerializer.Serialize(new { t = "rekey", recipientId }));
    }

    private void Enqueue(string json)
    {
        this.outbox.Enqueue(json);
        _ = this.DrainOutbox(this.cts?.Token ?? CancellationToken.None);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = 1000;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(ct);
                if (string.IsNullOrEmpty(token))
                {
                    await Task.Delay(2000, ct);
                    continue;
                }

                this.socket = new ClientWebSocket();
                this.socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
                // Keep the line warm: Railway's edge proxy closes idle sockets, and a chat connection
                // goes quiet whenever nobody is typing. Send a WebSocket ping well inside that window
                // rather than relying on the framework default.
                this.socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await this.socket.ConnectAsync(this.BuildUri(), ct);
                this.Connected = true;
                backoff = 1000;
                await this.DrainOutbox(ct);
                await this.ReceiveLoop(this.socket, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Relay connection lost.");
            }
            finally
            {
                this.Connected = false;
                try { this.socket?.Dispose(); } catch { /* ignore */ }
                this.socket = null;
            }

            // Full jitter: after a deploy, thousands of clients reconnect at once. Spreading the wait
            // randomly across the window stops them from hitting the relay in synchronized waves.
            var wait = (backoff / 2) + Random.Shared.Next((backoff / 2) + 1);
            try { await Task.Delay(wait, ct); } catch { break; }
            backoff = Math.Min(backoff * 2, 30000);
        }
    }

    private const int MaxMessageBytes = 256 * 1024;   // far above any real chat frame; relay-OOM guard

    private async Task ReceiveLoop(ClientWebSocket s, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (s.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await s.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await s.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct);
                    return;
                }

                message.Write(buffer, 0, result.Count);
                if (message.Length > MaxMessageBytes)
                {
                    // A frame larger than any legitimate chat message: drop the connection rather than
                    // keep buffering (a hostile relay can't OOM the client / crash the game).
                    await s.CloseAsync(WebSocketCloseStatus.MessageTooBig, "frame too large", ct);
                    return;
                }
            }
            while (!result.EndOfMessage);

            this.Dispatch(Encoding.UTF8.GetString(message.ToArray()));
        }
    }

    private void Dispatch(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            switch (root.GetProperty("t").GetString())
            {
                case "message":
                    this.MessageReceived?.Invoke(EncryptedMessageDto.FromJson(root.GetProperty("msg").GetRawText()));
                    break;
                case "sent":
                    this.Sent?.Invoke(root.GetProperty("clientMsgId").GetString() ?? string.Empty, root.GetProperty("messageId").GetGuid());
                    break;
                case "delivered":
                    this.Delivered?.Invoke(root.GetProperty("messageId").GetGuid());
                    break;
                case "rekey":
                    this.RekeyRequested?.Invoke(root.GetProperty("from").GetGuid());
                    break;
                case "album_request":
                    this.AlbumRequestReceived?.Invoke(ParseAlbumNotice(root));
                    break;
                case "album_grant":
                    this.AlbumGranted?.Invoke(ParseAlbumNotice(root));
                    break;
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Bad relay frame.");
        }
    }

    // internal (not private) so the pure notice parsing can be unit-tested; no behavior change.
    internal static AlbumNotice ParseAlbumNotice(JsonElement root) => new(
        root.GetProperty("from").GetGuid(),
        root.TryGetProperty("fromName", out var n) ? n.GetString() ?? "Someone" : "Someone",
        root.GetProperty("albumId").GetGuid(),
        root.TryGetProperty("albumName", out var a) ? a.GetString() ?? "an album" : "an album");

    private async Task DrainOutbox(CancellationToken ct)
    {
        if (this.socket is not { State: WebSocketState.Open })
            return;

        await this.sendLock.WaitAsync(ct);
        try
        {
            while (this.outbox.TryDequeue(out var json))
            {
                if (this.socket is not { State: WebSocketState.Open })
                {
                    this.outbox.Enqueue(json);
                    break;
                }

                await this.socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, ct);
            }
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Relay send failed.");
        }
        finally
        {
            this.sendLock.Release();
        }
    }

    private Uri BuildUri()
    {
        var baseUrl = this.serverBaseUrl.TrimEnd('/');
        var ws = baseUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase)
            ? "wss" + baseUrl[5..]
            : "ws" + baseUrl[4..];
        return new Uri($"{ws}/hub");   // token is sent in the Authorization header, not the query string
    }

    public void Dispose()
    {
        // Order matters. Cancel the loop, then dispose the socket so any in-flight ReceiveAsync /
        // ConnectAsync throws and unwinds at once instead of waiting out the keep-alive. Then block
        // briefly on the loop task: when Dalamud unloads the plugin it must be able to collect the
        // assembly's load context, and a still-running loop rooted in it makes the unload fail with
        // "Failed to unload plugin". The bounded wait means a wedged socket can't hang the game on exit.
        try { this.cts?.Cancel(); } catch (ObjectDisposedException) { /* already torn down */ }
        try { this.socket?.Dispose(); } catch { /* ignore */ }
        try { this.runner?.Wait(TimeSpan.FromSeconds(2)); } catch { /* cancellation faults are expected */ }
        this.cts?.Dispose();
        this.cts = null;
    }
}
