namespace Eikon.Config;

// Pure server-URL migration logic, kept off the Dalamud-coupled Plugin type so it can be unit-tested
// without the Dalamud runtime: an absolute loopback URL (a persisted dev default) resets to production;
// a self-hoster's non-loopback URL is left untouched.
internal static class ServerUrl
{
    public const string Production = "https://api.eikon.chat";

    public static string ResetLoopbackIfNeeded(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.IsLoopback ? Production : url;
}
