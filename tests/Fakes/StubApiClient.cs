using System.Threading;
using Eikon.Contracts;
using Eikon.Crypto;
using Eikon.Net;

namespace Eikon.Tests.Fakes;

// Minimal IApiClient for the crypto round-trip: serves each peer's published key bundle and identity
// from that peer's local KeyVault (as the real server would from what the peer published). Every other
// endpoint throws, since the round-trip never calls it. The peer directory is mutable so a test can
// simulate a server that later serves a different identity for the same user id (a MITM / key swap).
internal class StubApiClient : IApiClient
{
    private readonly Dictionary<Guid, KeyVault> peers = new();

    // Register (or replace) the vault a given user id resolves to.
    public void Set(Guid userId, KeyVault vault) => this.peers[userId] = vault;

    public Task<KeyBundleDto> GetKeyBundleAsync(string accessToken, string userId, CancellationToken ct)
    {
        var id = Guid.Parse(userId);
        var b = this.peers[id].PublicBundle();
        var otk = b.OneTimePreKeys.Count > 0 ? b.OneTimePreKeys[0] : null;
        return Task.FromResult(new KeyBundleDto
        {
            UserId = id,
            Ed25519Pub = Convert.ToBase64String(b.Ed25519Pub),
            X25519Pub = Convert.ToBase64String(b.X25519Pub),
            X25519Sig = Convert.ToBase64String(b.X25519Sig),
            SignedPreKey = new PreKeyDto
            {
                KeyId = b.SignedPreKey.KeyId,
                PublicKey = Convert.ToBase64String(b.SignedPreKey.PublicKey),
                Signature = Convert.ToBase64String(b.SignedPreKey.Signature!),   // PublicBundle always signs the SPK
            },
            OneTimePreKey = otk is null ? null! : new PreKeyDto
            {
                KeyId = otk.KeyId,
                PublicKey = Convert.ToBase64String(otk.PublicKey),
            },
        });
    }

    public Task<(string Ed25519Pub, string X25519Pub, string X25519Sig)> GetIdentityAsync(string accessToken, string userId, CancellationToken ct)
    {
        var b = this.peers[Guid.Parse(userId)].PublicBundle();
        return Task.FromResult((Convert.ToBase64String(b.Ed25519Pub), Convert.ToBase64String(b.X25519Pub), Convert.ToBase64String(b.X25519Sig)));
    }

    // ---- Not exercised by the crypto round-trip. ----
    public Task<LoginStartResponse> LoginStartAsync(CancellationToken ct) => throw new NotImplementedException();
    public Task<LoginPollResponse> LoginPollAsync(string txnId, string pollSecret, CancellationToken ct) => throw new NotImplementedException();
    public Task<SessionTokens> RefreshAsync(string refreshToken, CancellationToken ct) => throw new NotImplementedException();
    public Task<VerifyStartResponse> VerifyStartAsync(string accessToken, CancellationToken ct) => throw new NotImplementedException();
    public Task<VerifyPollResponse> VerifyPollAsync(string txnId, CancellationToken ct) => throw new NotImplementedException();
    public Task<bool> GetVerifiedAsync(string accessToken, CancellationToken ct) => throw new NotImplementedException();
    public Task<(bool Discreet, bool OnlyVerifiedMessage)> GetSettingsAsync(string accessToken, CancellationToken ct) => throw new NotImplementedException();
    public Task UpdateSettingsAsync(string accessToken, bool discreet, bool onlyVerifiedMessage, CancellationToken ct) => throw new NotImplementedException();
    public Task<(int Status, string? DeletionPendingUntil)> GetMeAsync(string accessToken, CancellationToken ct) => throw new NotImplementedException();
    public Task DeleteAccountAsync(string accessToken, IReadOnlyList<string>? reasons, string? note, CancellationToken ct) => throw new NotImplementedException();
    public Task RestoreAccountAsync(string accessToken, CancellationToken ct) => throw new NotImplementedException();
    public Task DeleteNowAsync(string accessToken, CancellationToken ct) => throw new NotImplementedException();
    public Task PublishKeysAsync(string accessToken, PublicKeyBundle bundle, CancellationToken ct) => throw new NotImplementedException();
    public Task<WorldCatalogResponse> GetWorldsAsync(CancellationToken ct) => throw new NotImplementedException();
    public Task<ModerationKeyResponse?> GetModerationKeyAsync(CancellationToken ct) => throw new NotImplementedException();
    public Task SaveProfileAsync(string accessToken, SaveProfileRequest profile, CancellationToken ct) => throw new NotImplementedException();
    public Task<SaveProfileRequest?> GetMyProfileAsync(string accessToken, CancellationToken ct) => throw new NotImplementedException();
    public Task<DiscoverResult> DiscoverAsync(string accessToken, DiscoverQuery query, CancellationToken ct) => throw new NotImplementedException();
    public virtual Task<ProfileDetailDto> GetProfileAsync(string accessToken, string userId, CancellationToken ct) => throw new NotImplementedException();
    public Task PublishOneTimePreKeysAsync(string accessToken, IReadOnlyList<PreKeyPublic> keys, CancellationToken ct) => throw new NotImplementedException();
    public Task<PhotoDto> UploadPhotoAsync(string accessToken, byte[] image, string contentType, CancellationToken ct) => throw new NotImplementedException();
    public Task<List<PhotoDto>> ListPhotosAsync(string accessToken, CancellationToken ct) => throw new NotImplementedException();
    public Task<string> PhotoViewUrlAsync(string accessToken, string photoId, CancellationToken ct) => throw new NotImplementedException();
    public Task DeletePhotoAsync(string accessToken, string photoId, CancellationToken ct) => throw new NotImplementedException();
    public Task SetMainPhotoAsync(string accessToken, string photoId, CancellationToken ct) => throw new NotImplementedException();
    public Task<string> UploadChatMediaAsync(string accessToken, byte[] blob, CancellationToken ct) => throw new NotImplementedException();
    public Task<string> ChatMediaViewUrlAsync(string accessToken, string storageKey, CancellationToken ct) => throw new NotImplementedException();
    public Task<byte[]> DownloadBytesAsync(string url, CancellationToken ct) => throw new NotImplementedException();
    public Task BlockAsync(string accessToken, Guid targetId, CancellationToken ct) => throw new NotImplementedException();
    public Task UnblockAsync(string accessToken, Guid targetId, CancellationToken ct) => throw new NotImplementedException();
    public Task<List<User>> GetBlockedAsync(string accessToken, CancellationToken ct) => throw new NotImplementedException();
    public Task ReportAsync(string accessToken, ReportRequest request, CancellationToken ct) => throw new NotImplementedException();
    public Task FavoriteAsync(string accessToken, Guid targetId, bool on, CancellationToken ct) => throw new NotImplementedException();
    public Task<List<ConversationSummaryDto>> GetConversationsAsync(string accessToken, CancellationToken ct) => throw new NotImplementedException();
    public Task MarkConversationReadAsync(string accessToken, Guid peerId, CancellationToken ct) => throw new NotImplementedException();
    public Task<List<BasicProfileDto>> GetFavoritesAsync(string accessToken, CancellationToken ct) => throw new NotImplementedException();
    public Task<AlbumDto> CreateAlbumAsync(string accessToken, string name, string visibility, CancellationToken ct) => throw new NotImplementedException();
    public Task<List<AlbumDto>> ListAlbumsAsync(string accessToken, CancellationToken ct) => throw new NotImplementedException();
    public Task UpdateAlbumAsync(string accessToken, string albumId, string? name, string? visibility, string? coverPhotoId, CancellationToken ct) => throw new NotImplementedException();
    public Task DeleteAlbumAsync(string accessToken, string albumId, CancellationToken ct) => throw new NotImplementedException();
    public Task<AlbumPhotoDto> AddAlbumPhotoAsync(string accessToken, string albumId, byte[] image, string contentType, CancellationToken ct) => throw new NotImplementedException();
    public Task<List<AlbumPhotoDto>> ListAlbumPhotosAsync(string accessToken, string albumId, CancellationToken ct) => throw new NotImplementedException();
    public Task RemoveAlbumPhotoAsync(string accessToken, string albumId, string photoId, CancellationToken ct) => throw new NotImplementedException();
    public Task<string> AlbumPhotoViewUrlAsync(string accessToken, string albumId, string photoId, CancellationToken ct) => throw new NotImplementedException();
    public Task<List<AlbumGranteeDto>> ListAlbumGrantsAsync(string accessToken, string albumId, CancellationToken ct) => throw new NotImplementedException();
    public Task GrantAlbumAsync(string accessToken, string albumId, Guid granteeId, string source, CancellationToken ct) => throw new NotImplementedException();
    public Task RevokeAlbumAsync(string accessToken, string albumId, string granteeId, CancellationToken ct) => throw new NotImplementedException();
    public Task RequestAlbumAccessAsync(string accessToken, string albumId, CancellationToken ct) => throw new NotImplementedException();
    public Task<List<AlbumRequestDto>> ListAlbumRequestsAsync(string accessToken, CancellationToken ct) => throw new NotImplementedException();
    public Task ApproveAlbumRequestAsync(string accessToken, string requestId, CancellationToken ct) => throw new NotImplementedException();
    public Task DenyAlbumRequestAsync(string accessToken, string requestId, CancellationToken ct) => throw new NotImplementedException();
    public Task<List<PeerAlbumDto>> ListPeerAlbumsAsync(string accessToken, string userId, CancellationToken ct) => throw new NotImplementedException();
}
