using System.Threading;
using Dalamud.Plugin.Services;
using Eikon.Contracts;

namespace Eikon.Net;

// FFXIV housing districts (fixed), zones, and aetherytes for the Events venue picker, fetched once from
// /api/housing and cached. Loads in the background so the UI never blocks; screens read Ready and the
// lists each frame and resolve a venue's display names through ZoneName / AetheryteName / DistrictLabel.
internal sealed class EventCatalog
{
    public sealed record Zone(int Id, string Name);

    public sealed record Aetheryte(int Id, int ZoneId, string Name);

    private readonly IApiClient api;
    private readonly IPluginLog log;
    private volatile bool loading;
    private Dictionary<int, string> zoneNames = new();
    private Dictionary<int, string> aetheryteNames = new();

    public EventCatalog(IApiClient api, IPluginLog log)
    {
        this.api = api;
        this.log = log;
    }

    public bool Ready { get; private set; }

    public IReadOnlyList<Zone> Zones { get; private set; } = Array.Empty<Zone>();

    public IReadOnlyList<Aetheryte> Aetherytes { get; private set; } = Array.Empty<Aetheryte>();

    // The five residential districts, in their canonical order, with the enum value the server expects.
    public static readonly IReadOnlyList<(HousingDistrictEnum Value, string Label)> Districts = new[]
    {
        (HousingDistrictEnum.Mist, "Mist"),
        (HousingDistrictEnum.LavenderBeds, "Lavender Beds"),
        (HousingDistrictEnum.Goblet, "Goblet"),
        (HousingDistrictEnum.Shirogane, "Shirogane"),
        (HousingDistrictEnum.Empyreum, "Empyreum"),
    };

    public static string DistrictLabel(HousingDistrictEnum district)
    {
        foreach (var (value, label) in Districts)
            if (value == district)
                return label;
        return district.ToString();
    }

    public void EnsureLoaded()
    {
        if (this.Ready || this.loading)
            return;
        this.loading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var res = await this.api.GetHousingAsync(CancellationToken.None);
                this.Zones = (res.Zones ?? new List<ZoneDto>()).Select(z => new Zone((int)z.Id, z.Name)).ToList();
                this.Aetherytes = (res.Aetherytes ?? new List<AetheryteDto>()).Select(a => new Aetheryte((int)a.Id, (int)a.ZoneId, a.Name)).ToList();
                this.zoneNames = this.Zones.ToDictionary(z => z.Id, z => z.Name);
                this.aetheryteNames = this.Aetherytes.ToDictionary(a => a.Id, a => a.Name);
                this.Ready = true;
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Failed to load housing catalog.");
            }
            finally
            {
                this.loading = false;
            }
        });
    }

    public string ZoneName(int zoneId) => this.zoneNames.TryGetValue(zoneId, out var name) ? name : string.Empty;

    public string AetheryteName(int aetheryteId) => this.aetheryteNames.TryGetValue(aetheryteId, out var name) ? name : string.Empty;

    public IEnumerable<Aetheryte> AetherytesInZone(int zoneId) => this.Aetherytes.Where(a => a.ZoneId == zoneId);
}
