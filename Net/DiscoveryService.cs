using System.Threading;
using Dalamud.Plugin.Services;
using Eikon.Contracts;

namespace Eikon.Net;

// Backs the discovery grid and owns the current query (the single source of tier/online/filters).
// The grid drives tier/online; the filter sheet applies the full facet set. Re-fetches on any change.
internal sealed class DiscoveryService
{
    private readonly IApiClient api;
    private readonly AuthService auth;
    private readonly IPluginLog log;
    private DiscoverQuery query = Default();
    private bool fetchedOnce;
    private string? nextCursor;
    private int epoch;

    public DiscoveryService(IApiClient api, AuthService auth, IPluginLog log)
    {
        this.api = api;
        this.auth = auth;
        this.log = log;
    }

    public bool Loading { get; private set; }

    public bool HasMore => this.nextCursor != null;

    // Segmented-control order for the proximity tiers. The generated Tier enum is alphabetical
    // (Dc, Region, World), so map explicitly rather than casting an index to the enum.
    public static readonly Tier[] TierOrder = { Tier.World, Tier.Dc, Tier.Region };

    public Tier Tier { get; private set; } = Tier.World;

    public int TierIndex => Math.Max(0, Array.IndexOf(TierOrder, this.Tier));

    public bool OnlineOnly { get; private set; }

    public IReadOnlyList<BasicProfileDto> Profiles { get; private set; } = Array.Empty<BasicProfileDto>();

    public void EnsureInitial()
    {
        if (!this.fetchedOnce)
            this.Fetch();
    }

    public void SetTier(Tier tier)
    {
        if (this.fetchedOnce && tier == this.Tier)
            return;
        this.Tier = tier;
        this.query.Tier = tier;
        this.Fetch();
    }

    public void SetOnline(bool online)
    {
        if (this.fetchedOnce && online == this.OnlineOnly)
            return;
        this.OnlineOnly = online;
        this.query.OnlineOnly = online;
        this.Fetch();
    }

    // Apply a full query from the filter sheet (preserves whatever tier/online it carries).
    public void Apply(DiscoverQuery next)
    {
        this.query = next;
        this.Tier = next.Tier ?? Tier.World;
        this.OnlineOnly = next.OnlineOnly == true;
        this.Fetch();
    }

    public void Reset() => this.Apply(Default());

    private static DiscoverQuery Default() => new()
    {
        Tier = Tier.World,
        OnlineOnly = false,
        LookingFor = new List<LookingForElement>(),
        Tribes = new List<TribeElement>(),
        Genders = new List<GenderElement>(),
        Races = new List<RaceElement>(),
        Positions = new List<PositionElement>(),
        Kinks = new List<string>(),
        AgeMin = 18,
        AgeMax = 120,
    };

    // Fresh query: reset paging and replace the grid. The epoch guards against a slow response from a
    // superseded query (fast tier/filter switches) clobbering the current one.
    private void Fetch()
    {
        this.fetchedOnce = true;
        this.Loading = true;
        var myEpoch = ++this.epoch;
        this.query.Cursor = null!;
        var snapshot = Clone(this.query);
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                {
                    if (myEpoch == this.epoch)
                    {
                        this.Profiles = Array.Empty<BasicProfileDto>();
                        this.nextCursor = null;
                    }

                    return;
                }

                var result = await this.api.DiscoverAsync(token, snapshot, CancellationToken.None);
                if (myEpoch != this.epoch)
                    return;
                this.Profiles = result.Profiles ?? new List<BasicProfileDto>();
                this.nextCursor = result.NextCursor;
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Discover failed.");
                if (myEpoch == this.epoch)
                {
                    this.Profiles = Array.Empty<BasicProfileDto>();
                    this.nextCursor = null;
                }
            }
            finally
            {
                if (myEpoch == this.epoch)
                    this.Loading = false;
            }
        });
    }

    // Append the next page, following the server cursor. No-op while a fetch is in flight or once the
    // roster cap is reached (cursor null); skips its result if a fresh query superseded it.
    public void LoadMore()
    {
        if (this.Loading || this.nextCursor == null)
            return;
        this.Loading = true;
        var myEpoch = this.epoch;
        var snapshot = Clone(this.query);
        snapshot.Cursor = this.nextCursor;
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                    return;

                var result = await this.api.DiscoverAsync(token, snapshot, CancellationToken.None);
                if (myEpoch != this.epoch)
                    return;
                var merged = new List<BasicProfileDto>(this.Profiles);
                if (result.Profiles != null)
                    merged.AddRange(result.Profiles);
                this.Profiles = merged;
                this.nextCursor = result.NextCursor;
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Discover load-more failed.");
            }
            finally
            {
                if (myEpoch == this.epoch)
                    this.Loading = false;
            }
        });
    }

    private static DiscoverQuery Clone(DiscoverQuery q) => new()
    {
        Tier = q.Tier,
        OnlineOnly = q.OnlineOnly,
        LookingFor = q.LookingFor,
        Tribes = q.Tribes,
        Genders = q.Genders,
        Races = q.Races,
        Positions = q.Positions,
        Kinks = q.Kinks,
        AgeMin = q.AgeMin,
        AgeMax = q.AgeMax,
        Cursor = q.Cursor,
    };
}
