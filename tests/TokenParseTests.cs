using System.Text;
using Eikon.Net;
using Xunit;

namespace Eikon.Tests;

// SessionStore.ParseSub extracts the signed-in member's id from a JWT's `sub` claim. It is defensive:
// any malformed token yields null rather than throwing.
public class TokenParseTests
{
    // A minimal unsigned JWT: base64url(header).base64url(payload).sig
    private static string Jwt(string payloadJson)
    {
        static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = B64Url(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        return $"{header}.{B64Url(Encoding.UTF8.GetBytes(payloadJson))}.sig";
    }

    [Fact]
    public void ParseSub_reads_the_sub_guid()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, SessionStore.ParseSub(Jwt($"{{\"sub\":\"{id}\"}}")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]   // fewer than two segments
    [InlineData("a.b")]         // payload is not valid base64/json
    public void ParseSub_returns_null_for_unusable_tokens(string? token)
        => Assert.Null(SessionStore.ParseSub(token));

    [Fact]
    public void ParseSub_returns_null_when_sub_is_missing_or_not_a_guid()
    {
        Assert.Null(SessionStore.ParseSub(Jwt("{\"foo\":\"bar\"}")));
        Assert.Null(SessionStore.ParseSub(Jwt("{\"sub\":\"not-a-guid\"}")));
    }
}
