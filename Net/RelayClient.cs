using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Dalamud.Plugin.Services;
using Eikon.Config;
using Eikon.Contracts;

namespace Eikon.Net;

// Long-lived WebSocket to the relay (ARCHITECTURE 6). Connects with the access token, receives server
// frames, and sends client frames; reconnects with backoff. Only ever carries ciphertext. Events
// fire on the receive task; handlers must tolerate that.
internal sealed class RelayClient : IDisposable
{
    private readonly Configuration config;
    private readonly AuthService auth;
    private readonly IPluginLog log;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly ConcurrentQueue<string> outbox = new();
    private ClientWebSocket? socket;
    private CancellationTokenSource? cts;

    public RelayClient(Configuration config, AuthService auth, IPluginLog log)
    {
        this.config = config;
        this.auth = auth;
        this.log = log;
    }

    public bool Connected { get; private set; }

    public event Action<EncryptedMessageDto>? MessageReceived;

    public event Action<string, Guid>? Sent;

    public event Action<Guid>? Delivered;

    public event Action<Guid>? RekeyRequested;

    public void Start()
    {
        if (this.cts != null)
            return;
        this.cts = new CancellationTokenSource();
        _ = this.RunAsync(this.cts.Token);
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
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Bad relay frame.");
        }
    }

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
        var baseUrl = this.config.ServerBaseUrl.TrimEnd('/');
        var ws = baseUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase)
            ? "wss" + baseUrl[5..]
            : "ws" + baseUrl[4..];
        return new Uri($"{ws}/hub");   // token is sent in the Authorization header, not the query string
    }

    public void Dispose()
    {
        this.cts?.Cancel();
        this.cts?.Dispose();
        try { this.socket?.Dispose(); } catch { /* ignore */ }
    }
}
