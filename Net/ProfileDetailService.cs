using System.Threading;
using Eikon.Contracts;

namespace Eikon.Net;

// Fetches a member's profile detail by id (from the current Selection) and caches the last result. A
// failed fetch is retried with a short backoff before giving up, so a transient hiccup (a 429, a brief
// 5xx, a dropped connection) resolves on its own instead of leaving the screen stuck on "Loading...".
internal sealed class ProfileDetailService
{
    // Backoff between attempts; its length is the retry count. A transient failure walks the schedule and
    // only surfaces as Failed once it is spent. A terminal error (a 4xx that isn't 429) skips it entirely.
    private static readonly int[] DefaultRetryDelaysMs = { 1000, 3000, 6000 };

    private readonly IApiClient api;
    private readonly ITokenProvider auth;
    private readonly ILog log;
    private readonly int[] retryDelaysMs;
    private CancellationTokenSource? loadCts;
    private Task loadTask = Task.CompletedTask;
    private Guid loadedFor;

    public ProfileDetailService(IApiClient api, ITokenProvider auth, ILog log)
        : this(api, auth, log, DefaultRetryDelaysMs)
    {
    }

    // Test seam: a zero-length or all-zero backoff keeps the retry-path tests fast.
    internal ProfileDetailService(IApiClient api, ITokenProvider auth, ILog log, int[] retryDelaysMs)
    {
        this.api = api;
        this.auth = auth;
        this.log = log;
        this.retryDelaysMs = retryDelaysMs;
    }

    public bool Loading { get; private set; }

    // Every attempt failed (retries included). The screen shows its error text off this rather than a null
    // Current, since Current is also null while a load is still in flight.
    public bool Failed { get; private set; }

    public ProfileDetailDto? Current { get; private set; }

    // Test seam: the most recent background load, so a test can await settling deterministically instead
    // of polling Loading (which would busy-wait and starve the load's continuations on a small thread pool).
    internal Task LoadTask => this.loadTask;

    public void Ensure(Guid userId)
    {
        if (this.loadedFor == userId)
            return;
        this.loadedFor = userId;
        this.Current = null;
        this.Load(userId);
    }

    // Force the next Ensure to refetch (e.g. previewing your own profile after editing it).
    public void Invalidate() => this.loadedFor = Guid.Empty;

    private void Load(Guid userId)
    {
        this.loadCts?.Cancel();
        var ct = (this.loadCts = new CancellationTokenSource()).Token;
        this.Loading = true;
        this.Failed = false;
        this.loadTask = Task.Run(async () =>
        {
            try
            {
                for (var attempt = 0; ; attempt++)
                {
                    var retryable = true;
                    try
                    {
                        var token = await this.auth.GetAccessTokenAsync(ct);
                        if (!string.IsNullOrEmpty(token))
                        {
                            var dto = await this.api.GetProfileAsync(token, userId.ToString(), ct);
                            if (this.loadedFor == userId)
                                this.Current = dto;
                            return;
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;   // superseded by a newer selection; that load owns the state now
                    }
                    catch (Exception ex)
                    {
                        this.log.Warning(ex, "Load profile detail failed.");
                        retryable = IsRetryable(ex);
                    }

                    if (!retryable || attempt >= this.retryDelaysMs.Length)
                    {
                        if (this.loadedFor == userId)
                            this.Failed = true;
                        return;
                    }
                    try
                    {
                        await Task.Delay(this.retryDelaysMs[attempt], ct);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
            finally
            {
                if (this.loadedFor == userId)
                    this.Loading = false;
            }
        });
    }

    // A transient failure is worth another attempt; a terminal one (blocked/deleted/gone, i.e. a 4xx that
    // isn't 429 backpressure) is not. Non-HTTP failures (network, timeout) carry status 0 and are transient.
    private static bool IsRetryable(Exception ex)
        => ex is not ApiException apiEx || apiEx.Status is 0 or 429 or >= 500;
}
