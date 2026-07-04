using System.Diagnostics;
using System.Threading;
using Dalamud.Plugin.Services;
using Eikon.Contracts;
using Eikon.Crypto;
using Eikon.Navigation;

namespace Eikon.Net;

internal enum AuthPhase
{
    LoggedOut,
    Authorizing,
    LoggedIn,
    Failed,
}

internal enum VerifyPhase
{
    Idle,
    Authorizing,
    Done,
    Failed,
}

// Drives the browser authorization-code login (ARCHITECTURE 4.2): start a transaction, open the
// authorize URL in the browser, then poll until the server hands back a session. On startup it tries
// to restore a session from the stored refresh token. UI reads Phase/Message each frame.
internal sealed class AuthService : IDisposable, ITokenProvider
{
    private readonly IApiClient api;
    private readonly SessionStore store;
    private readonly IPluginLog log;
    private readonly Eikon.Crypto.KeyVault vault;
    private readonly ScreenRouter router;
    private readonly SemaphoreSlim refreshLock = new(1, 1);   // single-flight the token refresh
    private CancellationTokenSource? cts;
    private CancellationTokenSource? verifyCts;

    public AuthService(IApiClient api, SessionStore store, IPluginLog log, Eikon.Crypto.KeyVault vault, ScreenRouter router)
    {
        this.api = api;
        this.store = store;
        this.log = log;
        this.vault = vault;
        this.router = router;

        this.store.Load();
        // Silent DPAPI auto-unlock on launch; the passphrase is only needed at setup or after a
        // logout/reset (which drops the auto-unlock material).
        if (this.vault.HasIdentity)
            this.vault.TryAutoUnlock();
        if (this.store.HasSession)
            this.TryRestore();
    }

    public AuthPhase Phase { get; private set; } = AuthPhase.LoggedOut;

    public string Message { get; private set; } = string.Empty;

    // The current authorize URL, exposed so the UI can offer a manual "copy link" fallback if the
    // browser could not be launched automatically. Cleared when not authorizing.
    public string? AuthorizeUrl { get; private set; }

    public VerifyPhase VerifyState { get; private set; } = VerifyPhase.Idle;

    public string VerifyMessage { get; private set; } = string.Empty;

    public bool IsVerified { get; private set; }

    // Set after auth when the account is soft-deleted but still inside the grace window; drives the
    // restore prompt (Screen.RestoreAccount). Null when the account is active.
    public string? DeletionPendingUntil { get; private set; }

    public void StartLogin()
    {
        if (this.Phase == AuthPhase.Authorizing)
            return;

        this.cts?.Cancel();
        this.cts?.Dispose();
        this.cts = new CancellationTokenSource();
        this.Phase = AuthPhase.Authorizing;
        this.AuthorizeUrl = null;
        this.Message = "Connecting...";
        _ = Task.Run(() => this.RunLoginAsync(this.cts.Token));
    }

    public void Cancel()
    {
        this.cts?.Cancel();
        if (this.Phase == AuthPhase.Authorizing)
        {
            this.Phase = AuthPhase.LoggedOut;
            this.Message = string.Empty;
            this.AuthorizeUrl = null;
        }
    }

    public void SignOut()
    {
        this.cts?.Cancel();
        this.verifyCts?.Cancel();
        this.store.Clear();
        // Require the passphrase again on the next sign-in.
        this.vault.ForgetAutoUnlock();
        this.vault.Lock();
        this.Phase = AuthPhase.LoggedOut;
        this.Message = string.Empty;
        this.VerifyState = VerifyPhase.Idle;
        this.VerifyMessage = string.Empty;
        this.IsVerified = false;
        this.DeletionPendingUntil = null;
    }

    // Restore a soft-deleted account from the sign-in restore prompt. On success clears the pending
    // state and continues to the normal app landing. Returns false on failure so the screen can keep
    // the prompt up with an error.
    public async Task<bool> RestoreAsync(CancellationToken ct)
    {
        try
        {
            var access = await this.GetAccessTokenAsync(ct);
            if (string.IsNullOrEmpty(access))
                return false;
            await this.api.RestoreAccountAsync(access, ct);
            this.DeletionPendingUntil = null;
            this.RouteToApp();
            return true;
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Account restore failed.");
            return false;
        }
    }

    // "Delete now instead" on the restore prompt: hard-delete immediately, then sign out to the gate.
    public async Task<bool> ConfirmDeleteNowAsync(CancellationToken ct)
    {
        try
        {
            var access = await this.GetAccessTokenAsync(ct);
            if (string.IsNullOrEmpty(access))
                return false;
            await this.api.DeleteNowAsync(access, ct);
            this.SignOut();
            this.router.Navigate(Screen.AgeGuidelines);
            return true;
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Immediate account deletion failed.");
            return false;
        }
    }

    // Start optional XIVAuth character verification (badge only). Requires a signed-in session.
    public void StartVerify()
    {
        if (this.Phase != AuthPhase.LoggedIn)
        {
            this.VerifyState = VerifyPhase.Failed;
            this.VerifyMessage = "Sign in first.";
            return;
        }

        if (this.VerifyState == VerifyPhase.Authorizing)
            return;

        this.verifyCts?.Cancel();
        this.verifyCts?.Dispose();
        this.verifyCts = new CancellationTokenSource();
        this.VerifyState = VerifyPhase.Authorizing;
        this.AuthorizeUrl = null;
        this.VerifyMessage = "Connecting...";
        _ = Task.Run(() => this.RunVerifyAsync(this.verifyCts.Token));
    }

    public void CancelVerify()
    {
        this.verifyCts?.Cancel();
        if (this.VerifyState == VerifyPhase.Authorizing)
        {
            this.VerifyState = VerifyPhase.Idle;
            this.VerifyMessage = string.Empty;
        }
    }

    // Publish the local public key bundle for X3DH (fire and forget; logged on failure).
    public void PublishKeys(PublicKeyBundle bundle)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var access = await this.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(access))
                    return;
                await this.api.PublishKeysAsync(access, bundle, CancellationToken.None);
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Publishing key bundle failed.");
            }
        });
    }

    // Periodic key maintenance: rotate the signed prekey when it ages out, and top up one-time prekeys
    // when the pool runs low (so an attacker can't pin a member to the weaker no-OPK handshake by
    // draining it). Fire-and-forget; requires the vault unlocked.
    public void MaintainKeys()
    {
        if (!this.vault.IsUnlocked)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                var access = await this.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(access))
                    return;

                if (this.vault.SignedPreKeyOlderThan(TimeSpan.FromDays(30)))
                    await this.api.PublishKeysAsync(access, this.vault.RotateSignedPreKey(), CancellationToken.None);

                if (this.vault.AvailableOneTimePreKeys < 10)
                    await this.api.PublishOneTimePreKeysAsync(access, this.vault.GenerateOneTimePreKeys(40), CancellationToken.None);
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Key maintenance failed.");
            }
        });
    }

    // Return a non-expired access token, refreshing it from the stored refresh token if needed. The
    // refresh is single-flighted: concurrent callers (the relay, key maintenance, the startup restore)
    // share one rotation instead of each spending the one-time refresh token, which the server would
    // treat as reuse and revoke, forcing a re-login.
    public async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        if (this.IsAccessFresh())
            return this.store.AccessToken;

        await this.refreshLock.WaitAsync(ct);
        try
        {
            if (this.IsAccessFresh())
                return this.store.AccessToken;   // another caller refreshed while we waited

            var refresh = this.store.RefreshToken;
            if (string.IsNullOrEmpty(refresh))
                return null;

            var tokens = await this.api.RefreshAsync(refresh, ct);
            this.store.Set(tokens);
            return this.store.AccessToken;
        }
        finally
        {
            this.refreshLock.Release();
        }
    }

    private bool IsAccessFresh() =>
        !string.IsNullOrEmpty(this.store.AccessToken) && this.store.AccessExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30);

    private async Task RunLoginAsync(CancellationToken ct)
    {
        try
        {
            var start = await this.api.LoginStartAsync(ct);
            var url = start.AuthorizeUrl.ToString();
            this.AuthorizeUrl = url;
            this.Message = OpenBrowser(url, this.log)
                ? "Waiting for Discord..."
                : "Couldn't open your browser. Copy the sign-in link below.";

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                var poll = await this.api.LoginPollAsync(start.TxnId, start.PollSecret, ct);
                if (poll.Status == Status.Complete && poll.Tokens != null)
                {
                    this.store.Set(poll.Tokens);
                    this.AuthorizeUrl = null;
                    this.Phase = AuthPhase.LoggedIn;
                    this.Message = "Signed in";
                    this.RouteAfterAuth();
                    return;
                }

                if (poll.Status == Status.Expired)
                {
                    this.Phase = AuthPhase.Failed;
                    this.Message = "Sign-in expired. Try again.";
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled; state already reset by Cancel/StartLogin.
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Discord login failed");
            this.Phase = AuthPhase.Failed;
            this.Message = "Couldn't reach the server.";
        }
    }

    private async Task RunVerifyAsync(CancellationToken ct)
    {
        try
        {
            var access = await this.GetAccessTokenAsync(ct);
            if (string.IsNullOrEmpty(access))
            {
                this.VerifyState = VerifyPhase.Failed;
                this.VerifyMessage = "Sign in first.";
                return;
            }

            var start = await this.api.VerifyStartAsync(access, ct);
            var url = start.AuthorizeUrl.ToString();
            this.AuthorizeUrl = url;
            this.VerifyMessage = OpenBrowser(url, this.log)
                ? "Waiting for XIVAuth..."
                : "Couldn't open your browser. Copy the link below.";

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                var poll = await this.api.VerifyPollAsync(start.TxnId, ct);
                if (poll.Status == Status.Complete)
                {
                    this.IsVerified = poll.Verified ?? false;
                    this.VerifyState = VerifyPhase.Done;
                    this.VerifyMessage = this.IsVerified ? "Character verified" : "No verified character found.";
                    return;
                }

                if (poll.Status == Status.Expired)
                {
                    this.VerifyState = VerifyPhase.Failed;
                    this.VerifyMessage = "Verification expired. Try again.";
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled.
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "XIVAuth verification failed");
            this.VerifyState = VerifyPhase.Failed;
            this.VerifyMessage = "Couldn't reach the server.";
        }
    }

    private void TryRestore()
    {
        if (string.IsNullOrEmpty(this.store.RefreshToken))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                // A persisted, still-valid access token restores instantly with no network call and no
                // rotation; otherwise this rotates once through the single-flight guard above.
                var access = await this.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(access))
                    return;
                this.Phase = AuthPhase.LoggedIn;
                this.RouteAfterAuth();
            }
            catch (ApiException ex) when (ex.Status is 400 or 401)
            {
                // The refresh token is genuinely rejected (revoked, expired, or reused): only now sign
                // out. A transient failure (server unreachable, mid-deploy) falls through to the catch
                // below and keeps the token, so the next request retries instead of forcing a re-login.
                this.log.Warning(ex, "Session restore rejected; clearing stored token.");
                this.store.Clear();
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Session restore failed transiently; keeping token.");
            }
        });
    }

    // Decide where an authenticated member lands. New members finish onboarding (which sets up the
    // vault and passphrase). Returning members skip straight in: to the grid if the vault auto-
    // unlocked, or to the passphrase unlock screen if it is still locked (after a logout or reset,
    // or on a new machine).
    // After authentication, first check whether this account is pending deletion (recoverable inside
    // the grace window); if so, land on the restore prompt instead of the app. Otherwise route normally.
    // A failed check falls through to the app so a transient error doesn't strand a healthy account.
    private void RouteAfterAuth()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var access = await this.GetAccessTokenAsync(CancellationToken.None);
                if (!string.IsNullOrEmpty(access))
                {
                    var (status, pendingUntil) = await this.api.GetMeAsync(access, CancellationToken.None);
                    if (!string.IsNullOrEmpty(pendingUntil))
                    {
                        this.DeletionPendingUntil = pendingUntil;
                        this.router.Navigate(Screen.RestoreAccount);
                        return;
                    }
                    // Authenticated but the account is not usable (deleted past its grace window, or
                    // suspended): sign out to the gate rather than drop into an app that 401s on every
                    // call. Transient errors (5xx, network) fall through and let the app retry.
                    if (status is 401 or 403)
                    {
                        this.SignOut();
                        this.router.Navigate(Screen.AgeGuidelines);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Deletion-state check failed; continuing into the app.");
            }

            this.DeletionPendingUntil = null;
            this.RouteToApp();
        });
    }

    // The normal post-auth landing. New members finish onboarding (which sets up the vault and
    // passphrase). Returning members skip in: to the grid if the vault auto-unlocked, or to the
    // passphrase unlock screen if it is still locked (after a logout or reset, or on a new machine).
    private void RouteToApp()
    {
        // Re-read the verified badge from the server so it survives a reload (it isn't kept locally);
        // otherwise a returning member looks unverified and gets re-prompted to verify needlessly.
        this.RefreshVerifiedStatus();
        if (this.store.NeedsOnboarding)
            return;
        if (this.vault.IsUnlocked)
            this.MaintainKeys();
        this.router.Navigate(this.vault.IsUnlocked ? Screen.Grid : Screen.Unlock);
    }

    // Fetch the account's verified flag from /auth/me (fire and forget). Keeps the badge accurate
    // across launches without making the member re-run XIVAuth verification.
    private void RefreshVerifiedStatus()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var access = await this.GetAccessTokenAsync(CancellationToken.None);
                if (string.IsNullOrEmpty(access))
                    return;
                this.IsVerified = await this.api.GetVerifiedAsync(access, CancellationToken.None);
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Fetching verified status failed.");
            }
        });
    }

    // Launch the system browser at the authorize URL. Returns false (instead of throwing) when the
    // shell can't start a browser, so the caller can fall back to a copyable link rather than leaving
    // the user stuck on a "browser opening" message that never resolves.
    private static bool OpenBrowser(string url, IPluginLog log)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Could not launch the browser for {Url}", url);
            return false;
        }
    }

    public void Dispose()
    {
        this.cts?.Cancel();
        this.cts?.Dispose();
        this.verifyCts?.Cancel();
        this.verifyCts?.Dispose();
        this.refreshLock.Dispose();
    }
}
