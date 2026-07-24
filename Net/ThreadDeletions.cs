using Eikon.Config;
using Eikon.Contracts;

namespace Eikon.Net;

// Deleting a thread from the inbox. Local by design: the server keeps a delivery queue that ages out
// after the retention window, not an archive, and a durable per-conversation "deleted, and when" row
// there would be exactly the metadata trail that policy exists to destroy. The decrypted history is
// local too, so a permanent delete is a local operation by nature.
//
// A delete records the thread's last message time as a watermark. Anything newer means the peer has
// written since, so the thread comes back to Messages rather than going quiet in a folder the member
// may never open again.
internal sealed class ThreadDeletions
{
    private readonly Configuration config;
    private readonly ChatService chat;

    public ThreadDeletions(Configuration config, ChatService chat)
    {
        this.config = config;
        this.chat = chat;
    }

    // Called once per inbox pass, before the lists are split, so IsDeleted stays a plain lookup.
    public void Sync(IReadOnlyList<ConversationSummaryDto> conversations)
    {
        var changed = false;
        foreach (var conversation in conversations)
        {
            if (!this.config.DeletedConversations.TryGetValue(Key(conversation.UserId), out var watermark))
                continue;

            if (conversation.LastMessageAt is { } at && at.ToUnixTimeSeconds() > watermark)
                changed |= this.config.DeletedConversations.Remove(Key(conversation.UserId));
        }

        if (changed)
            this.config.Save();
    }

    public bool IsDeleted(Guid peer) => this.config.DeletedConversations.ContainsKey(Key(peer));

    public void Delete(ConversationSummaryDto conversation)
    {
        this.config.DeletedConversations[Key(conversation.UserId)] = conversation.LastMessageAt?.ToUnixTimeSeconds() ?? 0L;
        this.config.Save();
    }

    public void Restore(Guid peer)
    {
        if (this.config.DeletedConversations.Remove(Key(peer)))
            this.config.Save();
    }

    // Permanent: the decrypted history goes for good. The thread stays hidden rather than leaving the
    // list, because the conversation still exists server-side until its messages age out - dropping the
    // watermark now would only make it reappear in Messages with nothing in it. The watermark moves to
    // now so a message that lands after this still brings the thread back.
    public void Purge(Guid peer)
    {
        this.chat.ForgetThread(peer);
        this.config.DeletedConversations[Key(peer)] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        this.config.Save();
    }

    private static string Key(Guid peer) => peer.ToString();
}
