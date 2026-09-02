using System.Threading;
using Eikon.Contracts;

namespace Eikon.Net;

// Backs the discovery grid and owns the current query (the single source of tier/online/filters).
// The grid drives tier/online; the filter sheet applies the full facet set. Re-fetches on any change.
internal sealed class DiscoveryService
{
    private readonly IApiClient api;
    private readonly ITokenProvider auth;
    private readonly ILog log;
    private DiscoverQuery query = Default();
    private Task fetchTask = Task.CompletedTask;
    private bool fetchedOnce;
    private string? nextCursor;
    private int epoch;
    private int previewEpoch;
    private List<int> travelDcIds;

    // `travelDcIds` seeds the data center travel set from the saved config (Plugin.cs passes it in;
    // Configuration itself stays out of here so the service is constructible without Dalamud loaded).
    public DiscoveryService(IApiClient api, ITokenProvider auth, ILog log, IEnumerable<int>? travelDcIds = null)
    {
        this.api = api;
        this.auth = auth;
        this.log = log;
        this.travelDcIds = Normalize(travelDcIds);
    }

    public bool Loading { get; private set; }

    // A fresh grid load is in flight (refresh, tier, or filter change), as opposed to a LoadMore append.
    // Drives the grid's refresh spinner without lighting it up for infinite-scroll pagination.
    public bool Reloading { get; private set; }

    public bool HasMore => this.nextCursor != null;

    // Segmented-control order for the proximity tiers. The generated Tier enum is alphabetical
    // (Dc, Region, World), so map explicitly rather than casting an index to the enum.
    public static readonly Tier[] TierOrder = { Tier.World, Tier.Dc, Tier.Region };

    public Tier Tier { get; private set; } = Tier.World;

    public int TierIndex => Math.Max(0, Array.IndexOf(TierOrder, this.Tier));

    public bool OnlineOnly { get; private set; }

    // Data center travel: extra data_centers ids browsed alongside home on the DC/Region tiers. Kept
    // outside the query so a filter Apply/Reset cannot drop it; Snapshot attaches it to every request.
    public IReadOnlyList<int> TravelDcIds => this.travelDcIds;

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

    // Change the travel set. Travelling from the World tier lands on DC (that is where the new data
    // centers show), so one fetch covers both. Returns whether a fetch was issued.
    public bool SetTravel(IReadOnlyList<int> dcIds)
    {
        var next = Normalize(dcIds);
        var switchTier = this.Tier == Tier.World && next.Count > 0;
        if (this.fetchedOnce && !switchTier && next.SequenceEqual(this.travelDcIds))
            return false;
        this.travelDcIds = next;
        if (switchTier)
        {
            this.Tier = Tier.Dc;
            this.query.Tier = Tier.Dc;
        }

        this.Fetch();
        return true;
    }

    internal static List<int> Normalize(IEnumerable<int>? ids) =>
        (ids ?? Array.Empty<int>()).Where(id => id > 0).Distinct().OrderBy(id => id).Take(16).ToList();

    // Apply a full query from the filter sheet (preserves whatever tier/online it carries).
    public void Apply(DiscoverQuery next)
    {
        this.query = next;
        this.Tier = next.Tier ?? Tier.World;
        this.OnlineOnly = next.OnlineOnly == true;
        this.Fetch();
    }

    // First-page result count for a draft query, without touching the grid's state. Feeds the filter
    // sheet's live "Show results · N" label; PreviewMore marks a count that is only the first page.
    public int PreviewCount { get; private set; }

    public bool PreviewMore { get; private set; }

    public void Preview(DiscoverQuery draft)
    {
        var myEpoch = ++this.previewEpoch;
        var snapshot = this.Snapshot(draft);
        snapshot.Cursor = null!;
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token) || myEpoch != this.previewEpoch)
                    return;
                var result = await this.api.DiscoverAsync(token, snapshot, CancellationToken.None);
                if (myEpoch != this.previewEpoch)
                    return;
                this.PreviewCount = result.Profiles?.Count ?? 0;
                this.PreviewMore = result.NextCursor != null;
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Discover preview failed.");
            }
        });
    }

    public void Reset() => this.Apply(Default());

    // Re-run the current query from the top: reset paging and rebuild the grid so members who just came
    // online surface. Preserves the active tier, online toggle, and filters.
    public void Refresh() => this.Fetch();

    // Test seam: the most recent fresh fetch, so a test can await it settling instead of polling Loading.
    internal Task FetchTask => this.fetchTask;

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
        this.Reloading = true;
        var myEpoch = ++this.epoch;
        this.query.Cursor = null!;
        var snapshot = this.Snapshot(this.query);
        this.fetchTask = Task.Run(async () =>
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
                {
                    this.Loading = false;
                    this.Reloading = false;
                }
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
        var snapshot = this.Snapshot(this.query);
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

    private DiscoverQuery Snapshot(DiscoverQuery q) => new()
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
        DcIds = this.travelDcIds.Count > 0 ? this.travelDcIds.Select(id => (long)id).ToList() : null!,
    };
}
