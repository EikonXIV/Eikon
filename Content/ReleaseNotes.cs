namespace Eikon.Content;

// One shipped release's member-facing notes. Only the non-empty groups are shown.
internal sealed record Release(Version Version, string Date, string[] New, string[] Improved, string[] Fixed);

// The curated changelog the "What's new" screen reads. Bundled in the build so a client always shows its
// own version's notes, works offline, and needs no server. When you cut a release, add a Release at the
// TOP (keep the list newest-first) and bump the plugin <Version> in Eikon.csproj to match. Keep each line
// short, sentence case, and member-facing - what changed for them, not the commit.
internal static class ReleaseNotes
{
    public static readonly IReadOnlyList<Release> All =
    [
        new Release(
            new Version(1, 2, 0),
            "July 2026",
            New:
            [
                "Private albums - collect photos into sets and share them with people you trust.",
                "Delete your account any time, with a 30-day window to change your mind.",
            ],
            Improved:
            [
                "Pride flag themes recolor the whole app.",
                "Message timestamps in every conversation.",
            ],
            Fixed:
            [
                "Blurred photos no longer reveal when a chat reopens.",
            ]),
    ];

    // Releases newer than what the member last acknowledged, newest-first. A null version (fresh install,
    // or the Settings entry) returns the full bundled history.
    public static IEnumerable<Release> Since(Version? seen) =>
        All.Where(r => seen is null || r.Version > seen);
}

// The running plugin version, normalized to Major.Minor.Build for comparison and display. Sourced from
// the assembly version, which MSBuild derives from <Version> in Eikon.csproj.
internal static class PluginVersion
{
    public static Version Current { get; } = Normalize(typeof(Plugin).Assembly.GetName().Version);

    public static string Display => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    // Parse a stored "Major.Minor.Build"; anything missing or unparseable sorts as the oldest version.
    public static Version Parse(string? stored) =>
        Version.TryParse(stored, out var v) ? Normalize(v) : new Version(0, 0, 0);

    private static Version Normalize(Version? v) =>
        v is null ? new Version(0, 0, 0) : new Version(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
}
