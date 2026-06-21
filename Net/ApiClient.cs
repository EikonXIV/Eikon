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

    Task<Tokens> RefreshAsync(string refreshToken, CancellationToken ct);

    Task<VerifyStartResponse> VerifyStartAsync(string accessToken, CancellationToken ct);

    Task<VerifyPollResponse> VerifyPollAsync(string txnId, CancellationToken ct);

    Task<bool> GetVerifiedAsync(string accessToken, CancellationToken ct);

    Task<(bool Discreet, bool OnlyVerifiedMessage)> GetSettingsAsync(string accessToken, CancellationToken ct);

    Task UpdateSettingsAsync(string accessToken, bool discreet, bool onlyVerifiedMessage, CancellationToken ct);

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

    Task<Photo> UploadPhotoAsync(string accessToken, byte[] image, string contentType, CancellationToken ct);

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

    Task<List<Conversation>> GetConversationsAsync(string accessToken, CancellationToken ct);

    Task MarkConversationReadAsync(string accessToken, Guid peerId, CancellationToken ct);

    Task<List<FavoritesResponseProfile>> GetFavoritesAsync(string accessToken, CancellationToken ct);
}

internal sealed class ApiClient : IApiClient, IDisposable
{
    private readonly HttpClient http;

    public ApiClient(Configuration config)
    {
        this.http = new HttpClient
        {
            BaseAddress = new Uri(config.ServerBaseUrl),
            Timeout = TimeSpan.FromSeconds(30),
        };
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

    public async Task<Tokens> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { refreshToken });
        var json = await this.PostAsync("/auth/refresh", body, ct);
        return JsonSerializer.Deserialize<Tokens>(json, Converter.Settings)
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

    public async Task<Photo> UploadPhotoAsync(string accessToken, byte[] image, string contentType, CancellationToken ct)
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
        using var res = await this.http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsByteArrayAsync(ct);
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

    public async Task<List<Conversation>> GetConversationsAsync(string accessToken, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, "/api/conversations", null, accessToken, ct);
        Ensure(status, body, "/api/conversations");
        return ConversationsResponse.FromJson(body).Conversations ?? new List<Conversation>();
    }

    public async Task MarkConversationReadAsync(string accessToken, Guid peerId, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Post, "/api/conversations/read", JsonSerializer.Serialize(new { peerId }), accessToken, ct);
        Ensure(status, body, "/api/conversations/read");
    }

    public async Task<List<FavoritesResponseProfile>> GetFavoritesAsync(string accessToken, CancellationToken ct)
    {
        var (status, body) = await this.SendAsync(HttpMethod.Get, "/api/favorites", null, accessToken, ct);
        Ensure(status, body, "/api/favorites");
        return FavoritesResponse.FromJson(body).Profiles ?? new List<FavoritesResponseProfile>();
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

        using var res = await this.http.SendAsync(req, ct);
        return ((int)res.StatusCode, await res.Content.ReadAsStringAsync(ct));
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
