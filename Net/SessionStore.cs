using System.Text;
using System.Text.Json;
using Eikon.Config;
using Eikon.Contracts;
using Eikon.Crypto;

namespace Eikon.Net;

// Holds the current session. The access token lives in memory; the refresh token is persisted,
// DPAPI-sealed at rest, so the session survives a restart but the token cannot be read off-machine.
internal sealed class SessionStore
{
    private readonly Configuration config;

    public SessionStore(Configuration config)
    {
        this.config = config;
    }

    public string? AccessToken { get; private set; }

    public DateTimeOffset AccessExpiresAt { get; private set; }

    public string? RefreshToken { get; private set; }

    public bool NeedsOnboarding { get; private set; }

    public bool HasSession => !string.IsNullOrEmpty(this.RefreshToken);

    // The signed-in member's id, read from the access token's `sub` claim (null if signed out).
    public Guid? UserId => ParseSub(this.AccessToken);

    private static Guid? ParseSub(string? accessToken)
    {
        if (string.IsNullOrEmpty(accessToken))
            return null;
        try
        {
            var parts = accessToken.Split('.');
            if (parts.Length < 2)
                return null;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = (payload.Length % 4) switch { 2 => payload + "==", 3 => payload + "=", _ => payload };
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return doc.RootElement.TryGetProperty("sub", out var sub) && sub.GetString() is { } s && Guid.TryParse(s, out var g)
                ? g
                : null;
        }
        catch
        {
            return null;
        }
    }

    public void Load()
    {
        if (string.IsNullOrEmpty(this.config.RefreshToken))
        {
            this.RefreshToken = null;
            return;
        }

        try
        {
            this.RefreshToken = Unseal(this.config.RefreshToken);
        }
        catch
        {
            this.RefreshToken = null;
            this.ClearPersisted();
            return;
        }

        // Restore the access token too when it's present and not already expired, so a relaunch inside
        // its lifetime needs no refresh at all (and so cannot race the one-time refresh-token rotation).
        if (!string.IsNullOrEmpty(this.config.AccessToken) && this.config.AccessExpiresAtUnix > 0)
        {
            try
            {
                this.AccessToken = Unseal(this.config.AccessToken);
                this.AccessExpiresAt = DateTimeOffset.FromUnixTimeSeconds(this.config.AccessExpiresAtUnix);
                this.NeedsOnboarding = this.config.NeedsOnboarding;
            }
            catch
            {
                this.AccessToken = null;
                this.AccessExpiresAt = default;
            }
        }
    }

    public void Set(SessionTokens tokens)
    {
        this.AccessToken = tokens.AccessToken;
        this.AccessExpiresAt = tokens.AccessExpiresAt;
        this.RefreshToken = tokens.RefreshToken;
        this.NeedsOnboarding = tokens.NeedsOnboarding;
        this.config.RefreshToken = Seal(tokens.RefreshToken);
        this.config.AccessToken = Seal(tokens.AccessToken);
        this.config.AccessExpiresAtUnix = tokens.AccessExpiresAt.ToUnixTimeSeconds();
        this.config.NeedsOnboarding = tokens.NeedsOnboarding;
        this.config.Save();
    }

    public void Clear()
    {
        this.AccessToken = null;
        this.RefreshToken = null;
        this.AccessExpiresAt = default;
        this.NeedsOnboarding = false;
        this.ClearPersisted();
    }

    private void ClearPersisted()
    {
        this.config.RefreshToken = null;
        this.config.AccessToken = null;
        this.config.AccessExpiresAtUnix = 0;
        this.config.NeedsOnboarding = false;
        this.config.Save();
    }

    private static string Seal(string value) => Convert.ToBase64String(Dpapi.Protect(Encoding.UTF8.GetBytes(value)));

    private static string Unseal(string blob) => Encoding.UTF8.GetString(Dpapi.Unprotect(Convert.FromBase64String(blob)));
}
