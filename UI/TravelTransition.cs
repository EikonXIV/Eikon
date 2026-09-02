namespace Eikon.UI;

// Phase machine for the data center travel crossing. Crossing holds for at least MinCross and until the
// fresh discovery fetch lands (so a slow server shows the aetheryte, not a stall), then Arriving fades
// the plate out while the new tiles stagger in. Pure timing; the grid does the drawing.
internal sealed class TravelTransition
{
    public enum Phase
    {
        Idle,
        Crossing,
        Arriving,
    }

    public const float MinCross = 1.1f;
    public const float MaxCross = 6f;
    public const float ArriveDuration = 0.55f;
    public const float TileStagger = 0.035f;
    public const float TileFade = 0.45f;
    public const int TileStaggerCap = 11;

    private Timeline cross;
    private Timeline arrive;
    private bool skipRequested;

    public Phase Current { get; private set; } = Phase.Idle;

    public string Caption { get; private set; } = string.Empty;

    public bool BlocksInput => this.Current == Phase.Crossing;

    public void Begin(double now, string caption)
    {
        this.Caption = caption;
        this.skipRequested = false;
        this.cross.Start(now);
        this.arrive.Stop();
        this.Current = Phase.Crossing;
    }

    public void Update(double now, bool loading)
    {
        switch (this.Current)
        {
            case Phase.Crossing:
            {
                var elapsed = this.cross.Elapsed(now);
                var ready = (elapsed >= MinCross || this.skipRequested) && !loading;
                if (ready || elapsed >= MaxCross)
                {
                    this.arrive.Start(now);
                    this.Current = Phase.Arriving;
                }

                break;
            }

            case Phase.Arriving:
                if (this.arrive.Done(now, ArriveDuration))
                {
                    this.arrive.Stop();
                    this.cross.Stop();
                    this.Current = Phase.Idle;
                }

                break;
        }
    }

    // A click during the crossing: arrive now, or as soon as the fetch lands if it is still in flight.
    public void Skip()
    {
        if (this.Current == Phase.Crossing)
            this.skipRequested = true;
    }

    public float CrossElapsed(double now) => this.cross.Elapsed(now);

    public float ArriveT(double now) => this.Current == Phase.Arriving ? this.arrive.Progress(now, ArriveDuration) : 0f;

    public float TileAlpha(int index, double now)
    {
        if (this.Current != Phase.Arriving)
            return 1f;
        var start = Math.Min(index, TileStaggerCap) * TileStagger;
        return Motion.EaseOutCubic(Motion.Segment(this.ArriveT(now), start, start + TileFade));
    }
}
