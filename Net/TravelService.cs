using Eikon.Config;

namespace Eikon.Net;

// Data center travel: the extra data centers the member browses alongside home, persisted locally,
// applied to discovery, plus the one-shot "a crossing just happened" flag the grid turns into its
// transition. Home is derived from the profile's world each time, so it is never stored in the set.
internal sealed class TravelService
{
    private readonly Configuration config;
    private readonly WorldCatalog catalog;
    private readonly ProfileService profiles;
    private readonly DiscoveryService discovery;
    private bool crossingPending;

    public TravelService(Configuration config, WorldCatalog catalog, ProfileService profiles, DiscoveryService discovery)
    {
        this.config = config;
        this.catalog = catalog;
        this.profiles = profiles;
        this.discovery = discovery;
    }

    public IReadOnlyList<int> DcIds => this.discovery.TravelDcIds;

    // The grid calls this each frame so the catalog and profile behind HomeDc/Destinations are warm by
    // the time a caption or the scope-row control needs names. Both are idempotent background loads.
    public void EnsureLoaded()
    {
        this.catalog.EnsureLoaded();
        this.profiles.EnsureLoaded();
    }

    public WorldCatalog.Dc? HomeDc => this.catalog.DcOfWorld((int)(this.profiles.Mine?.WorldId ?? 0));

    public int AwayCount
    {
        get
        {
            var home = this.HomeDc?.Id;
            return this.DcIds.Count(id => id != home);
        }
    }

    public bool Travelling => this.AwayCount > 0;

    // Home first (when known), then the travel data centers in catalog order.
    public IReadOnlyList<WorldCatalog.Dc> Destinations
    {
        get
        {
            var list = new List<WorldCatalog.Dc>();
            var home = this.HomeDc;
            if (home != null)
                list.Add(home);
            foreach (var dc in this.catalog.DataCenters)
                if (dc.Id != home?.Id && this.DcIds.Contains(dc.Id))
                    list.Add(dc);
            return list;
        }
    }

    public string DestinationCaption() =>
        string.Join(" · ", this.Destinations.Select(d => d.Name.ToUpperInvariant()));

    public string AwayNames() =>
        string.Join(", ", this.Destinations.Where(d => d.Id != this.HomeDc?.Id).Select(d => d.Name));

    // Persist and apply. Arms the crossing when discovery actually refetched on a tier the set shapes.
    public bool Apply(IEnumerable<int> dcIds)
    {
        var next = StripHome(dcIds, this.HomeDc?.Id);
        this.config.TravelDcIds = next;
        this.config.Save();
        var changed = this.discovery.SetTravel(next);
        if (changed && this.discovery.Tier != Tier.World)
            this.crossingPending = true;
        return changed;
    }

    public bool TakeCrossing()
    {
        var pending = this.crossingPending;
        this.crossingPending = false;
        return pending;
    }

    internal static List<int> StripHome(IEnumerable<int> ids, int? homeDcId) =>
        DiscoveryService.Normalize(ids.Where(id => id != homeDcId));
}
