using System.Net;
using System.Text;
using System.Text.Json;
using Eikon.Net;
using Xunit;

namespace Eikon.Tests;

// Contract tests for the delete-account ApiClient methods: correct HTTP method/path, bearer header,
// and request-body shaping. The body shaping matters because the server's optional fields reject a
// JSON null (distinct from absent), so empty reasons/note must be omitted, not sent as null.
public class ApiClientTests
{
    // Captures the outgoing request and returns a canned response.
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode status;
        private readonly string responseBody;

        public StubHandler(HttpStatusCode status = HttpStatusCode.OK, string responseBody = "{}")
        {
            this.status = status;
            this.responseBody = responseBody;
        }

        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Request = request;
            this.Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(this.status)
            {
                Content = new StringContent(this.responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (ApiClient Api, StubHandler Handler) Make(HttpStatusCode status = HttpStatusCode.OK, string body = "{}")
    {
        var handler = new StubHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local") };
        return (new ApiClient(http), handler);
    }

    [Fact]
    public async Task DeleteAccount_omits_empty_reasons_and_note()
    {
        var (api, handler) = Make();
        await api.DeleteAccountAsync("tok", null, null, CancellationToken.None);
        Assert.Equal(HttpMethod.Delete, handler.Request!.Method);
        Assert.Equal("/api/account", handler.Request!.RequestUri!.AbsolutePath);
        Assert.Equal("{}", handler.Body);
    }

    [Fact]
    public async Task DeleteAccount_omits_whitespace_only_note()
    {
        var (api, handler) = Make();
        await api.DeleteAccountAsync("tok", System.Array.Empty<string>(), "   ", CancellationToken.None);
        Assert.Equal("{}", handler.Body);
    }

    [Fact]
    public async Task DeleteAccount_includes_reasons_and_note_when_present()
    {
        var (api, handler) = Make();
        await api.DeleteAccountAsync("tok", new[] { "Met someone", "Taking a break" }, "note here", CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Body!);
        var reasons = doc.RootElement.GetProperty("reasons").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "Met someone", "Taking a break" }, reasons);
        Assert.Equal("note here", doc.RootElement.GetProperty("note").GetString());
    }

    [Fact]
    public async Task DeleteAccount_sends_bearer_token()
    {
        var (api, handler) = Make();
        await api.DeleteAccountAsync("tok123", null, null, CancellationToken.None);
        Assert.Equal("Bearer tok123", handler.Request!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task DeleteAccount_throws_on_error_status()
    {
        var (api, _) = Make(HttpStatusCode.InternalServerError, "boom");
        await Assert.ThrowsAsync<ApiException>(() => api.DeleteAccountAsync("tok", null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Restore_posts_to_restore_endpoint()
    {
        var (api, handler) = Make();
        await api.RestoreAccountAsync("tok", CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("/api/account/restore", handler.Request!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task DeleteNow_posts_to_delete_now_endpoint()
    {
        var (api, handler) = Make();
        await api.DeleteNowAsync("tok", CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("/api/account/delete-now", handler.Request!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetMe_parses_pending_until()
    {
        var (api, _) = Make(HttpStatusCode.OK, "{\"deletionPendingUntil\":\"2026-08-01T00:00:00.000Z\"}");
        var (status, until) = await api.GetMeAsync("tok", CancellationToken.None);
        Assert.Equal(200, status);
        Assert.Equal("2026-08-01T00:00:00.000Z", until);
    }

    [Fact]
    public async Task GetMe_null_pending_for_active_account()
    {
        var (api, _) = Make(HttpStatusCode.OK, "{\"userId\":\"x\",\"deletionPendingUntil\":null}");
        var (status, until) = await api.GetMeAsync("tok", CancellationToken.None);
        Assert.Equal(200, status);
        Assert.Null(until);
    }

    [Fact]
    public async Task GetMe_surfaces_unauthorized_status()
    {
        var (api, _) = Make(HttpStatusCode.Unauthorized, "{\"error\":\"inactive_account\"}");
        var (status, until) = await api.GetMeAsync("tok", CancellationToken.None);
        Assert.Equal(401, status);
        Assert.Null(until);
    }

    // ---- request-shaping for more endpoints (same StubHandler seam) --------------------------------

    [Fact]
    public async Task Favorite_posts_target_and_flag()
    {
        var (api, handler) = Make();
        var target = Guid.NewGuid();
        await api.FavoriteAsync("tok", target, true, CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("/api/favorite", handler.Request!.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(handler.Body!);
        Assert.Equal(target, doc.RootElement.GetProperty("targetId").GetGuid());
        Assert.True(doc.RootElement.GetProperty("on").GetBoolean());
    }

    [Fact]
    public async Task Block_and_Unblock_post_the_target_to_their_endpoints()
    {
        var target = Guid.NewGuid();

        var (block, hb) = Make();
        await block.BlockAsync("tok", target, CancellationToken.None);
        Assert.Equal(HttpMethod.Post, hb.Request!.Method);
        Assert.Equal("/api/block", hb.Request!.RequestUri!.AbsolutePath);
        Assert.Equal(target, JsonDocument.Parse(hb.Body!).RootElement.GetProperty("targetId").GetGuid());

        var (unblock, hu) = Make();
        await unblock.UnblockAsync("tok", target, CancellationToken.None);
        Assert.Equal("/api/unblock", hu.Request!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GrantAlbum_posts_grantee_and_source_to_the_album_path()
    {
        var (api, handler) = Make();
        var grantee = Guid.NewGuid();
        await api.GrantAlbumAsync("tok", "alb-1", grantee, "request", CancellationToken.None);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("/api/albums/alb-1/grants", handler.Request!.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(handler.Body!);
        Assert.Equal(grantee, doc.RootElement.GetProperty("granteeId").GetGuid());
        Assert.Equal("request", doc.RootElement.GetProperty("source").GetString());
    }

    [Fact]
    public async Task UpdateAlbum_sends_only_the_supplied_fields()
    {
        var (api, handler) = Make();
        await api.UpdateAlbumAsync("tok", "alb-1", "New name", null, null, CancellationToken.None);
        Assert.Equal(HttpMethod.Patch, handler.Request!.Method);
        Assert.Equal("/api/albums/alb-1", handler.Request!.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(handler.Body!);
        Assert.Equal("New name", doc.RootElement.GetProperty("name").GetString());
        Assert.False(doc.RootElement.TryGetProperty("visibility", out _));
        Assert.False(doc.RootElement.TryGetProperty("coverPhotoId", out _));
    }

    [Fact]
    public async Task UpdateAlbum_includes_all_supplied_fields()
    {
        var (api, handler) = Make();
        await api.UpdateAlbumAsync("tok", "alb-1", "n", "public", "cover-9", CancellationToken.None);
        using var doc = JsonDocument.Parse(handler.Body!);
        Assert.Equal("n", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("public", doc.RootElement.GetProperty("visibility").GetString());
        Assert.Equal("cover-9", doc.RootElement.GetProperty("coverPhotoId").GetString());
    }
}
