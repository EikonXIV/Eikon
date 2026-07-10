using Eikon.Net;
using Xunit;

namespace Eikon.Tests;

// The decision that fixes the phantom-unread bug (a badge and "New message" preview with nothing behind
// it): how OnMessage reacts to a message it can't decrypt. A changed peer identity is acked so it stops
// redelivering forever, but never rekeyed or auto-repinned (the "identity changed" banner drives
// re-verification, and silently re-pinning would accept a possible MITM). A ratchet desync is acked and
// rekeyed to recover. A forged / concurrent-handshake initial is left queued and quiet.
public class UndecryptableDecisionTests
{
    [Fact]
    public void Changed_identity_initial_is_acked_but_not_rekeyed()
    {
        var action = ChatService.DecideUndecryptable(isInitial: true, identityMismatched: true);
        Assert.True(action.Ack);
        Assert.False(action.Rekey);
    }

    [Fact]
    public void Changed_identity_noninitial_is_still_ack_only()
    {
        var action = ChatService.DecideUndecryptable(isInitial: false, identityMismatched: true);
        Assert.True(action.Ack);
        Assert.False(action.Rekey);
    }

    [Fact]
    public void Noninitial_desync_is_acked_and_rekeyed()
    {
        var action = ChatService.DecideUndecryptable(isInitial: false, identityMismatched: false);
        Assert.True(action.Ack);
        Assert.True(action.Rekey);
    }

    // The old behavior kept only this branch quiet; the bug was that a changed-identity initial fell
    // here too and nagged forever. This one must stay quiet (a matching-identity initial we still can't
    // open is forged/garbage or a handshake tie-break the peer supersedes).
    [Fact]
    public void Undecryptable_initial_with_matching_identity_stays_quiet()
    {
        var action = ChatService.DecideUndecryptable(isInitial: true, identityMismatched: false);
        Assert.False(action.Ack);
        Assert.False(action.Rekey);
    }
}
