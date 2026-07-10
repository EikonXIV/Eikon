using System.Threading;
using Dalamud.Plugin.Services;
using Eikon.Contracts;

namespace Eikon.Net;

// Loads the conversation inbox. The relay only stores ciphertext, so the server gives metadata only
// (peer, last-message time, direction, unread count); the preview line is taken from the locally
// decrypted thread held by ChatService.
internal sealed class InboxService
{
    private readonly IApiClient api;
    private readonly AuthService auth;
    private readonly ChatService chat;
    private readonly IPluginLog log;
    private bool loading;
    private bool dirty;   // invalidated while a load was in flight; chains a follow-up refetch

    public InboxService(IApiClient api, AuthService auth, ChatService chat, RelayClient relay, IPluginLog log)
    {
        this.api = api;
        this.auth = auth;
        this.chat = chat;
        this.log = log;

        // A new incoming or outgoing message may start a conversation, or flip one from request to
        // thread (a reply accepts a request), so mark the cache stale; the inbox draws each frame and
        // refetches on the next EnsureLoaded.
        relay.MessageReceived += _ => this.Invalidate();
        relay.Sent += (_, _) => this.Invalidate();
    }

    public bool Loaded { get; private set; }

    public IReadOnlyList<ConversationSummaryDto> Conversations { get; private set; } = new List<ConversationSummaryDto>();

    public void EnsureLoaded()
    {
        // Connect the relay so incoming messages land in the threads the inbox previews. Idempotent.
        this.chat.Start();
        if (this.Loaded || this.loading)
            return;
        this.Refresh();
    }

    public void Refresh()
    {
        this.loading = true;
        this.dirty = false;
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                    return;
                this.Conversations = await this.api.GetConversationsAsync(token, CancellationToken.None);
                this.Loaded = true;
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Loading conversations failed.");
            }
            finally
            {
                this.loading = false;
                // A message was sent or received while this fetch was in flight, so its result can
                // already be stale (e.g. a reply that just accepted a request). Refetch once so it lands
                // promptly instead of waiting for the periodic refresh.
                if (this.dirty && this.Loaded)
                    this.Refresh();
            }
        });
    }

    // Mark the cached inbox stale so the next draw refetches. When this lands while a load is in flight
    // the `dirty` flag chains a follow-up fetch (see Refresh) so the invalidation is never swallowed.
    private void Invalidate()
    {
        this.Loaded = false;
        this.dirty = true;
    }

    // Mark a thread read (opening the chat). Clears its unread badge server-side, then invalidates the
    // cached inbox so the next load reflects it.
    public void MarkRead(Guid peer)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                    return;
                await this.api.MarkConversationReadAsync(token, peer, CancellationToken.None);
                this.Invalidate();
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Marking conversation read failed.");
            }
        });
    }

    // The most recent locally decrypted message for the preview line, or null if none is cached this
    // session (history is not persisted, so older threads show no preview until a message arrives).
    public ChatService.Message? Preview(Guid peer)
    {
        var thread = this.chat.Thread(peer);
        return thread.Count > 0 ? thread[^1] : null;
    }
}
