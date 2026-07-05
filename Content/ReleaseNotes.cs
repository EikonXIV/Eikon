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
            new Version(1, 5, 1),
            "July 2026",
            New: [],
            Improved: [],
            Fixed:
            [
                "Eikon now unloads cleanly when you disable or update it, fixing the occasional 'Failed to unload plugin' error that asked you to restart the game.",
            ]),
        new Release(
            new Version(1, 5, 0),
            "July 2026",
            New:
            [
                "Pride flag themes: Pride, MLM, Bisexual, Trans, Ace, and Non-binary. Each one recolors the app and shows its flag as a stripe under the header.",
            ],
            Improved:
            [
                "Your theme now lives in a dedicated Appearance screen in Settings, with the classic colors and the new flags together.",
            ],
            Fixed: []),
        new Release(
            new Version(1, 4, 0),
            "July 2026",
            New:
            [
                "Fully close Eikon with the new title-bar X, or by right-clicking the orb. Reopen any time with /eikon.",
            ],
            Improved: [],
            Fixed: []),
        new Release(
            new Version(1, 3, 1),
            "July 2026",
            New: [],
            Improved: [],
            Fixed:
            [
                "Albums shared with you now unlock right away, instead of sometimes staying stuck on 'Requested'.",
            ]),
        new Release(
            new Version(1, 3, 0),
            "July 2026",
            New:
            [
                "A What's new screen. See what changed after each update, and browse past releases any time from Settings.",
            ],
            Improved: [],
            Fixed: []),
        new Release(
            new Version(1, 2, 0),
            "July 2026",
            New:
            [
                "Delete your account from Settings. It's recoverable: sign back in within 30 days to restore everything, or confirm removal right away.",
            ],
            Improved: [],
            Fixed: []),
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
