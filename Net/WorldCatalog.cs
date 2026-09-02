using System.Threading;
using Dalamud.Plugin.Services;

namespace Eikon.Net;

// FFXIV world list for the World/DC picker, fetched once from /api/worlds and cached. Loads in the
// background so the UI never blocks; screens read Ready and DataCenters each frame.
internal sealed class WorldCatalog
{
    public sealed record World(int Id, string Name);

    public sealed record Dc(int Id, string Name, string Region, IReadOnlyList<World> Worlds);

    private readonly IApiClient api;
    private readonly IPluginLog log;
    private volatile bool loading;

    public WorldCatalog(IApiClient api, IPluginLog log)
    {
        this.api = api;
        this.log = log;
    }

    public bool Ready { get; private set; }

    public IReadOnlyList<Dc> DataCenters { get; private set; } = Array.Empty<Dc>();

    public void EnsureLoaded()
    {
        if (this.Ready || this.loading)
            return;
        this.loading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var res = await this.api.GetWorldsAsync(CancellationToken.None);
                this.DataCenters = res.DataCenters
                    .Select(d => new Dc((int)d.Id, d.Name, d.Region.ToString(), d.Worlds.Select(w => new World((int)w.Id, w.Name)).ToList()))
                    .ToList();
                this.Ready = true;
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Failed to load world catalog.");
            }
            finally
            {
                this.loading = false;
            }
        });
    }

    public string WorldName(int worldId)
    {
        foreach (var dc in this.DataCenters)
            foreach (var w in dc.Worlds)
                if (w.Id == worldId)
                    return $"{dc.Name} · {w.Name}";
        return "Pick a world";
    }

    public Dc? DcOfWorld(int worldId)
    {
        foreach (var dc in this.DataCenters)
            foreach (var w in dc.Worlds)
                if (w.Id == worldId)
                    return dc;
        return null;
    }

    public Dc? DcById(int dcId)
    {
        foreach (var dc in this.DataCenters)
            if (dc.Id == dcId)
                return dc;
        return null;
    }

    // Region strings arrive as the generated enum's names ("Na", "Eu", "Jp", "Oce").
    public static string RegionCode(string region) => region switch
    {
        "NorthAmerica" or "North America" or "NA" or "Na" => "NA",
        "Europe" or "EU" or "Eu" => "EU",
        "Japan" or "JP" or "Jp" => "JP",
        "Oceania" or "OCE" or "Oce" => "OCE",
        _ => (region.Length > 3 ? region[..3] : region).ToUpperInvariant(),
    };

    public static string RegionName(string region) => RegionCode(region) switch
    {
        "NA" => "North America",
        "EU" => "Europe",
        "JP" => "Japan",
        "OCE" => "Oceania",
        var other => other,
    };

    public static readonly string[] RegionOrder = { "NA", "EU", "JP", "OCE" };
}
