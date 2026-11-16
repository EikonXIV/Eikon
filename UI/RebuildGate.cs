namespace Eikon.UI;

// Decides when newly seen characters justify an atlas rebuild. The rules exist to make a rebuild loop
// impossible: one build at a time, a build only when the demand has grown, a minimum gap between builds,
// and no retry after a failure until both a long backoff has passed and the demand has grown again.
internal sealed class RebuildGate
{
    public const long SettleMs = 300;
    public const long MaxWaitMs = 1500;
    public const long MinIntervalMs = 2000;
    public const long FailureBackoffMs = 60000;

    private int covered;
    private int lastDemand;
    private long changedAt;
    private long pendingSince = -1;
    private long notBefore;

    public bool InFlight { get; private set; }

    public void Cover(int demand)
    {
        this.covered = demand;
        this.pendingSince = -1;
    }

    public bool ShouldBuild(int demand, long now)
    {
        if (this.InFlight)
            return false;

        if (demand == this.covered)
        {
            this.pendingSince = -1;
            return false;
        }

        if (this.pendingSince < 0)
            this.pendingSince = now;

        if (demand != this.lastDemand)
        {
            this.lastDemand = demand;
            this.changedAt = now;
        }

        var settled = now - this.changedAt >= SettleMs || now - this.pendingSince >= MaxWaitMs;
        if (!settled || now < this.notBefore)
            return false;

        this.covered = demand;
        this.pendingSince = -1;
        this.notBefore = now + MinIntervalMs;
        this.InFlight = true;
        return true;
    }

    public void Completed(bool failed, long now)
    {
        this.InFlight = false;
        if (failed)
            this.notBefore = Math.Max(this.notBefore, now + FailureBackoffMs);
    }
}
