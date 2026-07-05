using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Eikon.Config;
using Eikon.Contracts;
using Eikon.Crypto;

namespace Eikon.Net;

// HTTP client for the Eikon control plane. Kept behind an interface so the auth and (later) API
// layers depend on the contract, not on HttpClient. Responses are parsed with the generated
// contract types (which carry their own JSON converters); request bodies are small and built here.
internal interface IApiClient
{
    Task<LoginStartResponse> LoginStartAsync(CancellationToken ct);

    Task<LoginPollResponse> LoginPollAsync(string txnId, string pollSecret, CancellationToken ct);

    Task<SessionTokens> RefreshAsync(string refreshToken, CancellationToken ct);

    Task<VerifyStartResponse> VerifyStartAsync(string accessToken, CancellationToken ct);

    Task<VerifyPollResponse> VerifyPollAsync(string txnId, CancellationToken ct);

    Task<bool> GetVerifiedAsync(string accessToken, CancellationToken ct);

    Task<(bool Discreet, bool OnlyVerifiedMessage)> GetSettingsAsync(string accessToken, CancellationToken ct);

    Task UpdateSettingsAsync(string accessToken, bool discreet, bool onlyVerifiedMessage, CancellationToken ct);

    // Reads /auth/me. Returns the HTTP status and, when the account is soft-deleted within the grace
    // window, deletionPendingUntil. A 401/403 means the session is not usable (deleted past grace,
    // suspended, or bad token), so the caller signs out instead of entering an app that will 401.
    Task<(int Status, string? DeletionPendingUntil)> GetMeAsync(string accessToken, CancellationToken ct);

    Task DeleteAccountAsync(string accessToken, IReadOnlyList<string>? reasons, string? note, CancellationToken ct);

    Task RestoreAccountAsync(string accessToken, CancellationToken ct);

    Task DeleteNowAsync(string accessToken, CancellationToken ct);

    Task PublishKeysAsync(string accessToken, PublicKeyBundle bundle, CancellationToken ct);

    Task<WorldCatalogResponse> GetWorldsAsync(CancellationToken ct);

    Task<ModerationKeyResponse?> GetModerationKeyAsync(CancellationToken ct);

    Task SaveProfileAsync(string accessToken, SaveProfileRequest profile, CancellationToken ct);

    Task<SaveProfileRequest?> GetMyProfileAsync(string accessToken, CancellationToken ct);

    Task<DiscoverResult> DiscoverAsync(string accessToken, DiscoverQuery query, CancellationToken ct);

    Task<ProfileDetailDto> GetProfileAsync(string accessToken, string userId, CancellationToken ct);

    Task PublishOneTimePreKeysAsync(string accessToken, IReadOnlyList<PreKeyPublic> keys, CancellationToken ct);

    Task<KeyBundleDto> GetKeyBundleAsync(string accessToken, string userId, CancellationToken ct);

    Task<(string Ed25519Pub, string X25519Pub, string X25519Sig)> GetIdentityAsync(string accessToken, string userId, CancellationToken ct);

    Task<PhotoDto> UploadPhotoAsync(string accessToken, byte[] image, string contentType, CancellationToken ct);

    Task<List<PhotoDto>> ListPhotosAsync(string accessToken, CancellationToken ct);

    Task<string> PhotoViewUrlAsync(string accessToken, string photoId, CancellationToken ct);

    Task DeletePhotoAsync(string accessToken, string photoId, CancellationToken ct);

    Task SetMainPhotoAsync(string accessToken, string photoId, CancellationToken ct);

    Task<string> UploadChatMediaAsync(string accessToken, byte[] blob, CancellationToken ct);

    Task<string> ChatMediaViewUrlAsync(string accessToken, string storageKey, CancellationToken ct);

    Task<byte[]> DownloadBytesAsync(string url, CancellationToken ct);

    Task BlockAsync(string accessToken, Guid targetId, CancellationToken ct);

    Task UnblockAsync(string accessToken, Guid targetId, CancellationToken ct);

    Task<List<User>> GetBlockedAsync(string accessToken, CancellationToken ct);

    Task ReportAsync(string accessToken, ReportRequest request, CancellationToken ct);

    Task FavoriteAsync(string accessToken, Guid targetId, bool on, CancellationToken ct);

    Task<List<ConversationSummaryDto>> GetConversationsAsync(string accessToken, CancellationToken ct);

    Task MarkConversationReadAsync(string accessToken, Guid peerId, CancellationToken ct);

    Task<List<BasicProfileDto>> GetFavoritesAsync(string accessToken, CancellationToken ct);

    Task<AlbumDto> CreateAlbumAsync(string accessToken, string name, string visibility, CancellationToken ct);

    Task<List<AlbumDto>> ListAlbumsAsync(string accessToken, CancellationToken ct);

    Task UpdateAlbumAsync(string accessToken, string albumId, string? name, string? visibility, string? coverPhotoId, CancellationToken ct);

    Task DeleteAlbumAsync(string accessToken, string albumId, CancellationToken ct);

    Task<AlbumPhotoDto> AddAlbumPhotoAsync(string accessToken, string albumId, byte[] image, string contentType, CancellationToken ct);

    Task<List<AlbumPhotoDto>> ListAlbumPhotosAsync(string accessToken, string albumId, CancellationToken ct);

    Task RemoveAlbumPhotoAsync(string accessToken, string albumId, string photoId, CancellationToken ct);

    Task<string> AlbumPhotoViewUrlAsync(string accessToken, string albumId, string photoId, CancellationToken ct);

    Task<List<AlbumGranteeDto>> ListAlbumGrantsAsync(string accessToken, string albumId, CancellationToken ct);

    Task GrantAlbumAsync(string accessToken, string albumId, Guid granteeId, string source, CancellationToken ct);

    Task RevokeAlbumAsync(string accessToken, string albumId, string granteeId, CancellationToken ct);

    Task RequestAlbumAccessAsync(string accessToken, string albumId, CancellationToken ct);

    Task<List<AlbumRequestDto>> ListAlbumRequestsAsync(string accessToken, CancellationToken ct);

    Task ApproveAlbumRequestAsync(string accessToken, string requestId, CancellationToken ct);

    Task DenyAlbumRequestAsync(string accessToken, string requestId, CancellationToken ct);

    Task<List<PeerAlbumDto>> ListPeerAlbumsAsync(string accessToken, string userId, CancellationToken ct);
}

internal sealed class ApiClient : IApiClient, IDisposable
{
    private readonly HttpClient http;
    private readonly CancellationToken lifetime;

    public ApiClient(Configuration config, AppLifetime lifetime)
    {
        this.http = new HttpClient
        {
            BaseAddress = new Uri(config.ServerBaseUrl),
            Timeout = TimeSpan.FromSeconds(30),
        };
        this.lifetime = lifetime.Token;
    }

    // Test seam: inject a preconfigured HttpClient (e.g. over a stub handler) so request shaping can be
    // asserted without a live server. Production always uses the Configuration constructor above. The
    // lifetime token stays default (never cancelled), so request behavior is unchanged under test.
    internal ApiClient(HttpClient http) => this.http = http;

    // Test seam variant that also wires a lifetime token, so cancellation-on-unload can be asserted.
    internal ApiClient(HttpClient http, AppLifetime lifetime)
    {
        this.http = http;
        this.lifetime = lifetime.Token;
    }

    public async Task<LoginStartResponse> LoginStartAsync(CancellationToken ct)
    {
        var json = await this.PostAsync("/auth/login/start", null, ct);
        return LoginStartResponse.FromJson(json);
    }

    public async Task<LoginPollResponse> LoginPollAsync(string txnId, string pollSecret, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { txnId, pollSecret });
        var json = await this.PostAsync("/auth/login/poll", body, ct);
        return LoginPollResponse.FromJson(json);
    }

    public async Task<SessionTokens> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { refreshToken });
        var json = await this.PostAsync("/auth/refresh", body, ct);
        return JsonSerializer.Deserialize<SessionTokens>(json, Converter.Settings)
            ?? throw new ApiException("Empty refresh response.");
    }

    public async Task<VerifyStartResponse> VerifyStartAsync(string accessToken, CancellationToken ct)
    {
        var json = await this.PostAsync("/auth/verify/start", null, ct, accessToken);
        return VerifyStartResponse.FromJson(json);
    }

    public async Task<VerifyPollResponse> VerifyPollAsync(string txnId, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { txnId });
        var json = await this.PostAsync("/auth/verify/poll", body, ct);
        return VerifyPollResponse.FromJson(json);
    }

    public async Task<bool> GetVerifiedAsync(string accessToken, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, "/auth/me", null, accessToken, ct);
        if (status is < 200 or >= 300)
            return false;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True;
    }

    public async Task<(bool Discreet, bool OnlyVerifiedMessage)> GetSettingsAsync(string accessToken, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, "/auth/me", null, accessToken, ct);
        if (status is < 200 or >= 300)
            return (false, false);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        bool Flag(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        return (Flag("discreet"), Flag("onlyVerifiedMessage"));
    }

    public async Task UpdateSettingsAsync(string accessToken, bool discreet, bool onlyVerifiedMessage, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { discreet, onlyVerifiedMessage });
        var (status, body) = await this.SendAsync(HttpMethod.Post, "/api/settings", payload, accessToken, ct);
        Ensure(status, body, "/api/settings");
    }

    public async Task<(int Status, string? DeletionPendingUntil)> GetMeAsync(string accessToken, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, "/auth/me", null, accessToken, ct);
        if (status is < 200 or >= 300)
            return (status, null);
        using var doc = JsonDocument.Parse(body);
        var until = doc.RootElement.TryGetProperty("deletionPendingUntil", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
        return (status, until);
    }

    // Only send the fields that are set: the server's optional reasons/note reject a JSON null (which is
    // distinct from absent), so an empty body is {}.
    public async Task DeleteAccountAsync(string accessToken, IReadOnlyList<string>? reasons, string? note, CancellationToken ct)
    {
        var fields = new Dictionary<string, object>();
        if (reasons is { Count: > 0 })
            fields["reasons"] = reasons;
        if (!string.IsNullOrWhiteSpace(note))
            fields["note"] = note!;
        var payload = JsonSerializer.Serialize(fields);
        var (status, body) = await this.SendAsync(HttpMethod.Delete, "/api/account", payload, accessToken, ct);
        Ensure(status, body, "/api/account");
    }

    public async Task RestoreAccountAsync(string accessToken, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Post, "/api/account/restore", "{}", accessToken, ct);
        Ensure(status, body, "/api/account/restore");
    }

    public async Task DeleteNowAsync(string accessToken, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Post, "/api/account/delete-now", "{}", accessToken, ct);
        Ensure(status, body, "/api/account/delete-now");
    }

    public async Task PublishKeysAsync(string accessToken, PublicKeyBundle bundle, CancellationToken ct)
    {
        var payload = new
        {
            ed25519Pub = Convert.ToBase64String(bundle.Ed25519Pub),
            x25519Pub = Convert.ToBase64String(bundle.X25519Pub),
            x25519Sig = Convert.ToBase64String(bundle.X25519Sig),
            signedPreKey = new
            {
                keyId = bundle.SignedPreKey.KeyId,
                publicKey = Convert.ToBase64String(bundle.SignedPreKey.PublicKey),
                signature = bundle.SignedPreKey.Signature != null ? Convert.ToBase64String(bundle.SignedPreKey.Signature) : null,
            },
            oneTimePreKeys = bundle.OneTimePreKeys.Select(k => new
            {
                keyId = k.KeyId,
                publicKey = Convert.ToBase64String(k.PublicKey),
            }),
        };
        await this.PostAsync("/keys/publish", JsonSerializer.Serialize(payload), ct, accessToken);
    }

    public async Task PublishOneTimePreKeysAsync(string accessToken, IReadOnlyList<PreKeyPublic> keys, CancellationToken ct)
    {
        var payload = new
        {
            oneTimePreKeys = keys.Select(k => new { keyId = k.KeyId, publicKey = Convert.ToBase64String(k.PublicKey) }),
        };
        await this.PostAsync("/keys/onetime", JsonSerializer.Serialize(payload), ct, accessToken);
    }

    public async Task<WorldCatalogResponse> GetWorldsAsync(CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, "/api/worlds", null, null, ct);
        Ensure(status, body, "/api/worlds");
        return WorldCatalogResponse.FromJson(body);
    }

    // Public (unauthenticated): the current root-signed moderation seal key. Null on any non-200 so the
    // caller falls back to the embedded key.
    public async Task<ModerationKeyResponse?> GetModerationKeyAsync(CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, "/keys/moderation", null, null, ct);
        if (status != 200)
            return null;
        return ModerationKeyResponse.FromJson(body);
    }

    public async Task SaveProfileAsync(string accessToken, SaveProfileRequest profile, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(profile, Converter.Settings);
        var (status, body) = await this.SendAsync(HttpMethod.Post, "/api/profile", json, accessToken, ct);
        Ensure(status, body, "/api/profile");
    }

    public async Task<SaveProfileRequest?> GetMyProfileAsync(string accessToken, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, "/api/profile/me", null, accessToken, ct);
        if (status == 404)
            return null;
        Ensure(status, body, "/api/profile/me");
        return SaveProfileRequest.FromJson(body);
    }

    public async Task<DiscoverResult> DiscoverAsync(string accessToken, DiscoverQuery query, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(query, Converter.Settings);
        var (status, body) = await this.SendAsync(HttpMethod.Post, "/api/discover", json, accessToken, ct);
        Ensure(status, body, "/api/discover");
        return DiscoverResult.FromJson(body);
    }

    public async Task<ProfileDetailDto> GetProfileAsync(string accessToken, string userId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, "/api/profile/" + userId, null, accessToken, ct);
        Ensure(status, body, "/api/profile/:id");
        return ProfileDetailDto.FromJson(body);
    }

    public async Task<KeyBundleDto> GetKeyBundleAsync(string accessToken, string userId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, "/keys/bundle/" + userId, null, accessToken, ct);
        Ensure(status, body, "/keys/bundle/:id");
        return KeyBundleDto.FromJson(body);
    }

    public async Task<(string Ed25519Pub, string X25519Pub, string X25519Sig)> GetIdentityAsync(string accessToken, string userId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, "/keys/identity/" + userId, null, accessToken, ct);
        Ensure(status, body, "/keys/identity/:id");
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return (
            root.GetProperty("ed25519Pub").GetString() ?? string.Empty,
            root.GetProperty("x25519Pub").GetString() ?? string.Empty,
            root.GetProperty("x25519Sig").GetString() ?? string.Empty);
    }

    public async Task<PhotoDto> UploadPhotoAsync(string accessToken, byte[] image, string contentType, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new { imageBase64 = Convert.ToBase64String(image), contentType });
        var (status, body) = await this.SendAsync(HttpMethod.Post, "/api/photos", json, accessToken, ct);
        Ensure(status, body, "/api/photos");
        return PhotoUploadResponse.FromJson(body).Photo;
    }

    public async Task<List<PhotoDto>> ListPhotosAsync(string accessToken, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, "/api/photos", null, accessToken, ct);
        Ensure(status, body, "/api/photos");
        var result = new List<PhotoDto>();
        using var doc = JsonDocument.Parse(body);
        foreach (var element in doc.RootElement.GetProperty("photos").EnumerateArray())
            result.Add(PhotoDto.FromJson(element.GetRawText()));
        return result;
    }

    public async Task<string> PhotoViewUrlAsync(string accessToken, string photoId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, $"/api/photos/{photoId}/view-url", null, accessToken, ct);
        Ensure(status, body, "/api/photos/:id/view-url");
        return PhotoViewUrlResponse.FromJson(body).Url.ToString();
    }

    public async Task DeletePhotoAsync(string accessToken, string photoId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Delete, "/api/photos/" + photoId, null, accessToken, ct);
        Ensure(status, body, "/api/photos/:id");
    }

    public async Task SetMainPhotoAsync(string accessToken, string photoId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Post, "/api/photos/" + photoId + "/main", null, accessToken, ct);
        Ensure(status, body, "/api/photos/:id/main");
    }

    public async Task<string> UploadChatMediaAsync(string accessToken, byte[] blob, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new { blobBase64 = Convert.ToBase64String(blob) });
        var (status, body) = await this.SendAsync(HttpMethod.Post, "/api/chat-media", json, accessToken, ct);
        Ensure(status, body, "/api/chat-media");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("storageKey").GetString() ?? throw new ApiException("no storageKey");
    }

    public async Task<string> ChatMediaViewUrlAsync(string accessToken, string storageKey, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, $"/api/chat-media/{storageKey}/view-url", null, accessToken, ct);
        Ensure(status, body, "/api/chat-media/:key/view-url");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("url").GetString() ?? throw new ApiException("no url");
    }

    // Fetch raw bytes from an absolute URL (a signed storage URL); the chat-media blob is E2E ciphertext.
    public async Task<byte[]> DownloadBytesAsync(string url, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, this.lifetime);
        using var res = await this.http.GetAsync(url, linked.Token);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsByteArrayAsync(linked.Token);
    }

    public async Task BlockAsync(string accessToken, Guid targetId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Post, "/api/block", JsonSerializer.Serialize(new { targetId }), accessToken, ct);
        Ensure(status, body, "/api/block");
    }

    public async Task UnblockAsync(string accessToken, Guid targetId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Post, "/api/unblock", JsonSerializer.Serialize(new { targetId }), accessToken, ct);
        Ensure(status, body, "/api/unblock");
    }

    public async Task<List<User>> GetBlockedAsync(string accessToken, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, "/api/blocks", null, accessToken, ct);
        Ensure(status, body, "/api/blocks");
        return BlockedUsersResponse.FromJson(body).Users ?? new List<User>();
    }

    public async Task ReportAsync(string accessToken, ReportRequest request, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Post, "/api/report", JsonSerializer.Serialize(request, Converter.Settings), accessToken, ct);
        Ensure(status, body, "/api/report");
    }

    public async Task FavoriteAsync(string accessToken, Guid targetId, bool on, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Post, "/api/favorite", JsonSerializer.Serialize(new { targetId, on }), accessToken, ct);
        Ensure(status, body, "/api/favorite");
    }

    public async Task<List<ConversationSummaryDto>> GetConversationsAsync(string accessToken, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, "/api/conversations", null, accessToken, ct);
        Ensure(status, body, "/api/conversations");
        return ConversationsResponse.FromJson(body).Conversations ?? new List<ConversationSummaryDto>();
    }

    public async Task MarkConversationReadAsync(string accessToken, Guid peerId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Post, "/api/conversations/read", JsonSerializer.Serialize(new { peerId }), accessToken, ct);
        Ensure(status, body, "/api/conversations/read");
    }

    public async Task<List<BasicProfileDto>> GetFavoritesAsync(string accessToken, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, "/api/favorites", null, accessToken, ct);
        Ensure(status, body, "/api/favorites");
        return FavoritesResponse.FromJson(body).Profiles ?? new List<BasicProfileDto>();
    }

    // ---- albums -------------------------------------------------------------------------------

    public async Task<AlbumDto> CreateAlbumAsync(string accessToken, string name, string visibility, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Post, "/api/albums", JsonSerializer.Serialize(new { name, visibility }), accessToken, ct);
        Ensure(status, body, "/api/albums");
        return AlbumDto.FromJson(body);
    }

    public async Task<List<AlbumDto>> ListAlbumsAsync(string accessToken, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, "/api/albums", null, accessToken, ct);
        Ensure(status, body, "/api/albums");
        return AlbumsResponse.FromJson(body).Albums ?? new List<AlbumDto>();
    }

    public async Task UpdateAlbumAsync(string accessToken, string albumId, string? name, string? visibility, string? coverPhotoId, CancellationToken ct)
    {
        // Only send fields that were supplied: UpdateAlbumRequest's fields are optional and an explicit
        // null would fail the server's Zod (optional means absent, not null).
        var patch = new Dictionary<string, object>();
        if (name is not null) patch["name"] = name;
        if (visibility is not null) patch["visibility"] = visibility;
        if (coverPhotoId is not null) patch["coverPhotoId"] = coverPhotoId;
        var (status, body) = await this.SendAsync(HttpMethod.Patch, "/api/albums/" + albumId, JsonSerializer.Serialize(patch), accessToken, ct);
        Ensure(status, body, "/api/albums/:id");
    }

    public async Task DeleteAlbumAsync(string accessToken, string albumId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Delete, "/api/albums/" + albumId, null, accessToken, ct);
        Ensure(status, body, "/api/albums/:id");
    }

    public async Task<AlbumPhotoDto> AddAlbumPhotoAsync(string accessToken, string albumId, byte[] image, string contentType, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new { imageBase64 = Convert.ToBase64String(image), contentType });
        var (status, body) = await this.SendAsync(HttpMethod.Post, $"/api/albums/{albumId}/photos", json, accessToken, ct);
        Ensure(status, body, "/api/albums/:id/photos");
        return AlbumPhotoDto.FromJson(body);
    }

    public async Task<List<AlbumPhotoDto>> ListAlbumPhotosAsync(string accessToken, string albumId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, $"/api/albums/{albumId}/photos", null, accessToken, ct);
        Ensure(status, body, "/api/albums/:id/photos");
        var result = new List<AlbumPhotoDto>();
        using var doc = JsonDocument.Parse(body);   // bare array, like GET /api/photos-style lists
        foreach (var element in doc.RootElement.EnumerateArray())
            result.Add(AlbumPhotoDto.FromJson(element.GetRawText()));
        return result;
    }

    public async Task RemoveAlbumPhotoAsync(string accessToken, string albumId, string photoId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Delete, $"/api/albums/{albumId}/photos/{photoId}", null, accessToken, ct);
        Ensure(status, body, "/api/albums/:id/photos/:photoId");
    }

    public async Task<string> AlbumPhotoViewUrlAsync(string accessToken, string albumId, string photoId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, $"/api/albums/{albumId}/photos/{photoId}/view-url", null, accessToken, ct);
        Ensure(status, body, "/api/albums/:id/photos/:photoId/view-url");
        return PhotoViewUrlResponse.FromJson(body).Url.ToString();
    }

    public async Task<List<AlbumGranteeDto>> ListAlbumGrantsAsync(string accessToken, string albumId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, $"/api/albums/{albumId}/grants", null, accessToken, ct);
        Ensure(status, body, "/api/albums/:id/grants");
        return AlbumGrantsResponse.FromJson(body).Grantees ?? new List<AlbumGranteeDto>();
    }

    public async Task GrantAlbumAsync(string accessToken, string albumId, Guid granteeId, string source, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new { granteeId, source });
        var (status, body) = await this.SendAsync(HttpMethod.Post, $"/api/albums/{albumId}/grants", json, accessToken, ct);
        Ensure(status, body, "/api/albums/:id/grants");
    }

    public async Task RevokeAlbumAsync(string accessToken, string albumId, string granteeId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Delete, $"/api/albums/{albumId}/grants/{granteeId}", null, accessToken, ct);
        Ensure(status, body, "/api/albums/:id/grants/:granteeId");
    }

    public async Task RequestAlbumAccessAsync(string accessToken, string albumId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Post, "/api/album-requests", JsonSerializer.Serialize(new { albumId }), accessToken, ct);
        Ensure(status, body, "/api/album-requests");
    }

    public async Task<List<AlbumRequestDto>> ListAlbumRequestsAsync(string accessToken, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, "/api/album-requests", null, accessToken, ct);
        Ensure(status, body, "/api/album-requests");
        return AlbumRequestsResponse.FromJson(body).Requests ?? new List<AlbumRequestDto>();
    }

    public async Task ApproveAlbumRequestAsync(string accessToken, string requestId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Post, $"/api/album-requests/{requestId}/approve", null, accessToken, ct);
        Ensure(status, body, "/api/album-requests/:id/approve");
    }

    public async Task DenyAlbumRequestAsync(string accessToken, string requestId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Post, $"/api/album-requests/{requestId}/deny", null, accessToken, ct);
        Ensure(status, body, "/api/album-requests/:id/deny");
    }

    public async Task<List<PeerAlbumDto>> ListPeerAlbumsAsync(string accessToken, string userId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, $"/api/users/{userId}/albums", null, accessToken, ct);
        Ensure(status, body, "/api/users/:id/albums");
        return PeerAlbumsResponse.FromJson(body).Albums ?? new List<PeerAlbumDto>();
    }

    private static void Ensure(int status, string body, string path)
    {
        if (status is < 200 or >= 300)
            throw new ApiException($"{status} for {path}: {body}", status);
    }

    private async Task<string> PostAsync(string path, string? jsonBody, CancellationToken ct, string? bearer = null)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Post, path, jsonBody ?? "{}", bearer, ct);
        Ensure(status, body, path);
        return body;
    }

    private async Task<(int Status, string Body)> SendAsync(HttpMethod method, string path, string? jsonBody, string? bearer, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path);
        if (jsonBody != null)
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        if (bearer != null)
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);

        // Link the caller's token with the plugin-lifetime token so a request in flight at unload is
        // cancelled at once rather than running out its 30s timeout while rooting the load context.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, this.lifetime);
        using var res = await this.http.SendAsync(req, linked.Token);
        return ((int)res.StatusCode, await res.Content.ReadAsStringAsync(linked.Token));
    }

    public void Dispose() => this.http.Dispose();
}

internal sealed class ApiException : Exception
{
    public ApiException(string message, int status = 0) : base(message)
    {
        this.Status = status;
    }

    // HTTP status that produced this error (0 if it wasn't an HTTP status failure). Lets the auth layer
    // tell a genuine rejection (401/400) from a transient one (5xx) and avoid wiping a valid session.
    public int Status { get; }
}
