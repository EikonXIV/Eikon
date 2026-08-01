using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Eikon.Contracts;

namespace Eikon.Net;

// Backs the Events board (Browse / Hosting / Saved tabs, proximity scope, kind filters, infinite scroll)
// and owns per-event detail + uploaded-banner textures. Board paging mirrors DiscoveryService (epoch-
// guarded fresh fetch + cursor LoadMore); detail/texture caches and fire-and-forget mutations mirror
// AlbumService. The service key never reaches the client; only short-lived signed URLs do.
internal sealed class EventService : IDisposable
{
    private readonly IApiClient api;
    private readonly AuthService auth;
    private readonly IPluginLog log;
    private readonly HttpClient http = new();
    private readonly CancellationToken lifetime;

    // Board segmented control order (the generated enums are alphabetical, so map explicitly).
    public static readonly Tab[] TabOrder = { Tab.Browse, Tab.Hosting, Tab.Saved };
    public static readonly EventScopeEnum[] ScopeOrder = { EventScopeEnum.World, EventScopeEnum.Dc, EventScopeEnum.Region };

    private readonly ConcurrentDictionary<Guid, EventDto> details = new();
    private readonly ConcurrentDictionary<Guid, byte> loadingDetails = new();
    private readonly ConcurrentDictionary<Guid, byte> gated = new();   // 404'd (private, no access) -> needs a code
    private readonly ConcurrentDictionary<Guid, IDalamudTextureWrap?> banners = new();
    private readonly ConcurrentDictionary<Guid, byte> loadingBanners = new();
    private readonly ConcurrentDictionary<string, IDalamudTextureWrap?> presets = new();
    private readonly ConcurrentDictionary<string, byte> loadingPresets = new();
    private static readonly string[] PresetIds = { "lounge", "rooftops", "lakeside" };

    private Task fetchTask = Task.CompletedTask;
    private bool fetchedOnce;
    private string? nextCursor;
    private int epoch;
    private bool countsLoading;

    public EventService(IApiClient api, AuthService auth, IPluginLog log, AppLifetime lifetime)
    {
        this.api = api;
        this.auth = auth;
        this.log = log;
        this.lifetime = lifetime.Token;
    }

    public Tab Tab { get; private set; } = Tab.Browse;

    public int TabIndex => Math.Max(0, Array.IndexOf(TabOrder, this.Tab));

    public EventScopeEnum Scope { get; private set; } = EventScopeEnum.World;

    public int ScopeIndex => Math.Max(0, Array.IndexOf(ScopeOrder, this.Scope));

    public IReadOnlyList<EventKindElement> Kinds { get; private set; } = Array.Empty<EventKindElement>();

    public IReadOnlyList<EventCardDto> Events { get; private set; } = Array.Empty<EventCardDto>();

    public bool Loading { get; private set; }

    public bool Reloading { get; private set; }

    public bool HasMore => this.nextCursor != null;

    // Counts for the Hosting / Saved segmented labels, loaded once and refreshed after a mutation.
    public int? HostingCount { get; private set; }

    public int? SavedCount { get; private set; }

    public void EnsureInitial()
    {
        if (!this.fetchedOnce)
            this.Fetch();
        this.EnsureCounts();
    }

    public void EnsureCounts()
    {
        if (this.countsLoading || (this.HostingCount != null && this.SavedCount != null))
            return;
        this.countsLoading = true;
        this.Fire(async token =>
        {
            var empty = new List<EventKindElement>();
            var h = await this.api.ListEventsAsync(token, new EventsQuery { Tab = Tab.Hosting, Scope = this.Scope, Kinds = empty, Cursor = null! }, CancellationToken.None);
            this.HostingCount = h.Events?.Count ?? 0;
            var s = await this.api.ListEventsAsync(token, new EventsQuery { Tab = Tab.Saved, Scope = this.Scope, Kinds = empty, Cursor = null! }, CancellationToken.None);
            this.SavedCount = s.Events?.Count ?? 0;
        }, "Loading event counts failed.", () => this.countsLoading = false);
    }

    private void RefreshCounts()
    {
        this.HostingCount = null;
        this.SavedCount = null;
        this.EnsureCounts();
    }

    public void SetTab(Tab tab)
    {
        if (this.fetchedOnce && tab == this.Tab)
            return;
        this.Tab = tab;
        this.Fetch();
    }

    public void SetScope(EventScopeEnum scope)
    {
        if (this.fetchedOnce && scope == this.Scope)
            return;
        this.Scope = scope;
        this.Fetch();
    }

    // Toggle a kind chip in the browse filter (no-op on the other tabs; they ignore kinds).
    public void ToggleKind(EventKindElement kind)
    {
        var list = new List<EventKindElement>(this.Kinds);
        if (!list.Remove(kind))
            list.Add(kind);
        this.Kinds = list;
        this.Fetch();
    }

    public void ClearKinds()
    {
        if (this.Kinds.Count == 0)
            return;
        this.Kinds = Array.Empty<EventKindElement>();
        this.Fetch();
    }

    public void Refresh() => this.Fetch();

    internal Task FetchTask => this.fetchTask;

    private EventsQuery BuildQuery(string? cursor) => new()
    {
        Tab = this.Tab,
        Scope = this.Scope,
        Kinds = this.Tab == Tab.Browse ? new List<EventKindElement>(this.Kinds) : new List<EventKindElement>(),
        Cursor = cursor!,
    };

    private void Fetch()
    {
        this.fetchedOnce = true;
        this.Loading = true;
        this.Reloading = true;
        var myEpoch = ++this.epoch;
        var query = this.BuildQuery(null);
        this.fetchTask = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                {
                    if (myEpoch == this.epoch) { this.Events = Array.Empty<EventCardDto>(); this.nextCursor = null; }
                    return;
                }

                var result = await this.api.ListEventsAsync(token, query, CancellationToken.None);
                if (myEpoch != this.epoch)
                    return;
                this.Events = result.Events ?? new List<EventCardDto>();
                this.nextCursor = result.NextCursor;
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Listing events failed.");
                if (myEpoch == this.epoch) { this.Events = Array.Empty<EventCardDto>(); this.nextCursor = null; }
            }
            finally
            {
                if (myEpoch == this.epoch) { this.Loading = false; this.Reloading = false; }
            }
        });
    }

    public void LoadMore()
    {
        if (this.Loading || this.nextCursor == null)
            return;
        this.Loading = true;
        var myEpoch = this.epoch;
        var query = this.BuildQuery(this.nextCursor);
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                    return;
                var result = await this.api.ListEventsAsync(token, query, CancellationToken.None);
                if (myEpoch != this.epoch)
                    return;
                var merged = new List<EventCardDto>(this.Events);
                if (result.Events != null)
                    merged.AddRange(result.Events);
                this.Events = merged;
                this.nextCursor = result.NextCursor;
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Loading more events failed.");
            }
            finally
            {
                if (myEpoch == this.epoch)
                    this.Loading = false;
            }
        });
    }

    // ---- detail (cached; load on first access) -----------------------------------------------

    public EventDto? Detail(Guid eventId)
    {
        if (this.details.TryGetValue(eventId, out var e))
            return e;
        if (this.loadingDetails.TryAdd(eventId, 0))
            this.Fire(async token =>
            {
                var dto = await this.api.GetEventAsync(token, eventId.ToString(), CancellationToken.None);
                if (dto != null)
                    this.details[eventId] = dto;
            }, "Loading event detail failed.", () => this.loadingDetails.TryRemove(eventId, out _));
        return null;
    }

    public void InvalidateDetail(Guid eventId)
    {
        this.details.TryRemove(eventId, out _);
        this.banners.TryRemove(eventId, out var wrap);
        wrap?.Dispose();
    }

    // ---- banner textures (uploaded banners; presets are bundled client assets) ----------------

    public IDalamudTextureWrap? BannerTexture(Guid eventId)
    {
        if (this.banners.TryGetValue(eventId, out var wrap))
            return wrap;
        if (this.loadingBanners.TryAdd(eventId, 0))
            _ = this.LoadBanner(eventId);
        return null;
    }

    // Bundled preset banner (the create wizard's stock art), loaded once from an embedded jpg and cached.
    public IDalamudTextureWrap? PresetBanner(string presetId)
    {
        if (this.presets.TryGetValue(presetId, out var wrap))
            return wrap;
        if (this.loadingPresets.TryAdd(presetId, 0))
            _ = this.LoadPreset(presetId);
        return null;
    }

    // Deterministic fallback preset for an event with no banner set, hashed from its id (matches the web).
    public static string FallbackPreset(Guid id)
    {
        long n = 0;
        foreach (var c in id.ToString())
            n = ((n * 31) + c) % 9973;
        return PresetIds[(int)(n % PresetIds.Length)];
    }

    // The banner to draw for a card/detail: the uploaded texture, else the named/derived preset.
    public IDalamudTextureWrap? BannerFor(EventCardDto e) =>
        e.BannerUploaded ? this.BannerTexture(e.Id) : this.PresetBanner(e.BannerPreset ?? FallbackPreset(e.Id));

    private async Task LoadPreset(string presetId)
    {
        try
        {
            await using var stream = typeof(EventService).Assembly.GetManifestResourceStream($"Eikon.Events.{presetId}.jpg");
            if (stream == null) { this.presets[presetId] = null; return; }
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, this.lifetime);
            this.presets[presetId] = await Plugin.TextureProvider.CreateFromImageAsync(ms.ToArray(), cancellationToken: this.lifetime);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Loading preset banner failed.");
            this.presets[presetId] = null;
        }
    }

    private async Task LoadBanner(Guid eventId)
    {
        try
        {
            var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
            if (string.IsNullOrEmpty(token)) { this.banners[eventId] = null; return; }
            var url = await this.api.EventBannerViewUrlAsync(token, eventId.ToString(), this.lifetime);
            var bytes = await this.http.GetByteArrayAsync(url, this.lifetime);
            this.banners[eventId] = await Plugin.TextureProvider.CreateFromImageAsync(bytes, cancellationToken: this.lifetime);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Loading event banner failed.");
            this.banners[eventId] = null;
        }
    }

    // ---- mutations ----------------------------------------------------------------------------

    // Create returns the new event (or null) so the wizard can navigate straight to its detail.
    public async Task<EventDto?> CreateAsync(CreateEventRequest request)
    {
        try
        {
            var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
            if (string.IsNullOrEmpty(token))
                return null;
            var e = await this.api.CreateEventAsync(token, request, CancellationToken.None);
            this.details[e.Id] = e;
            this.Refresh();
            return e;
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Creating event failed.");
            return null;
        }
    }

    public async Task<bool> UpdateAsync(Guid eventId, UpdateEventRequest request)
    {
        try
        {
            var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
            if (string.IsNullOrEmpty(token))
                return false;
            await this.api.UpdateEventAsync(token, eventId.ToString(), request, CancellationToken.None);
            this.InvalidateDetail(eventId);
            this.Refresh();
            return true;
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Updating event failed.");
            return false;
        }
    }

    public void Cancel(Guid eventId) =>
        this.Fire(async token =>
        {
            await this.api.CancelEventAsync(token, eventId.ToString(), CancellationToken.None);
            this.InvalidateDetail(eventId);
            this.Refresh();
        }, "Cancelling event failed.");

    public void Restore(Guid eventId) =>
        this.Fire(async token =>
        {
            await this.api.RestoreEventAsync(token, eventId.ToString(), CancellationToken.None);
            this.InvalidateDetail(eventId);
            this.Refresh();
        }, "Restoring event failed.");

    public void Delete(Guid eventId) =>
        this.Fire(async token =>
        {
            await this.api.DeleteEventAsync(token, eventId.ToString(), CancellationToken.None);
            this.InvalidateDetail(eventId);
            this.Refresh();
        }, "Deleting event failed.");

    public async Task<string?> RegenerateCodeAsync(Guid eventId)
    {
        try
        {
            var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
            if (string.IsNullOrEmpty(token))
                return null;
            var code = await this.api.RegenerateEventCodeAsync(token, eventId.ToString(), CancellationToken.None);
            this.InvalidateDetail(eventId);
            return code;
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Regenerating event code failed.");
            return null;
        }
    }

    public void UploadBanner(Guid eventId, byte[] bytes, string contentType) =>
        this.Fire(async token =>
        {
            await this.api.UploadEventBannerAsync(token, eventId.ToString(), bytes, contentType, CancellationToken.None);
            this.InvalidateDetail(eventId);
        }, "Uploading event banner failed.");

    // Save/attend: optimistic count bump on the card + cached detail, then reconcile with the server.
    public void Save(Guid eventId, bool on)
    {
        this.ApplySaved(eventId, on, on ? +1 : -1);
        this.Fire(async token =>
        {
            var (attending, saved) = await this.api.SaveEventAsync(token, eventId, on, CancellationToken.None);
            this.ApplyExact(eventId, saved, attending);
        }, "Saving event failed.", this.RefreshCounts);
    }

    // Look up a private event by code (board key lookup / share-card unlock). Returns the event or null
    // on a miss. Caches the detail so the following navigation shows it without a refetch.
    public async Task<EventDto?> LookupAsync(string code)
    {
        try
        {
            var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
            if (string.IsNullOrEmpty(token))
                return null;
            var e = await this.api.LookupEventAsync(token, code, CancellationToken.None);
            if (e != null)
                this.details[e.Id] = e;
            return e;
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Event code lookup failed.");
            return null;
        }
    }

    private void ApplySaved(Guid eventId, bool saved, int delta)
    {
        foreach (var card in this.Events)
            if (card.Id == eventId)
            {
                card.SavedByMe = saved;
                card.Attending = Math.Max(0, card.Attending + delta);
            }
        if (this.details.TryGetValue(eventId, out var e))
        {
            e.SavedByMe = saved;
            e.Attending = Math.Max(0, e.Attending + delta);
        }
    }

    private void ApplyExact(Guid eventId, bool saved, long attending)
    {
        foreach (var card in this.Events)
            if (card.Id == eventId)
            {
                card.SavedByMe = saved;
                card.Attending = attending;
            }
        if (this.details.TryGetValue(eventId, out var e))
        {
            e.SavedByMe = saved;
            e.Attending = attending;
        }
    }

    private void Fire(Func<string, Task> action, string what, Action? onDone = null)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await this.auth.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(token))
                    return;
                await action(token);
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, what);
            }
            finally
            {
                onDone?.Invoke();
            }
        });
    }

    public void Dispose()
    {
        this.http.Dispose();
        foreach (var wrap in this.banners.Values)
            wrap?.Dispose();
        this.banners.Clear();
        foreach (var wrap in this.presets.Values)
            wrap?.Dispose();
        this.presets.Clear();
    }
}
