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
            new Version(2, 4, 0),
            "August 2026",
            New:
            [
                "Data center travel. Browse other data centers alongside your home one, in any region, without changing your home world. Open the diamond beside World / DC / Region on the grid, or Settings > Connection, and pick where to travel. DC shows your home plus every data center you picked; Region widens to all of their regions.",
                "Crossing the aether. Travelling plays a short transition over the grid while the new roster loads. Click it to skip.",
            ],
            Improved:
            [
                "Members you reach through travel show their data center next to their world on the grid.",
            ],
            Fixed: []),
        new Release(
            new Version(2, 3, 2),
            "August 2026",
            New: [],
            Improved:
            [
                "Profile limits are shown as you edit. Race and tribe say how many you can pick and grey out once you're there, the display name stops at 20 characters, and anything still missing is called out right under the field, with Save waiting until it's sorted.",
                "After dark isn't available for Lalafell profiles, and the editor now says so instead of quietly failing to save.",
            ],
            Fixed:
            [
                "Profiles that never saved. Going past a limit (more than four races, say) made a save fail with no warning, so you didn't show up on the grid, your own preview was empty, and edits seemed to vanish. Setting up an account now waits for the profile to save and lets you try again if it doesn't, and the editor tells you when a change didn't go through.",
                "The profile header no longer borrows the previously viewed member's world when a profile fails to load.",
            ]),
        new Release(
            new Version(2, 3, 1),
            "August 2026",
            New: [],
            Improved: [],
            Fixed:
            [
                "Notifications for messages that never turned up. A message Eikon couldn't finish receiving would announce itself again on every login, with nothing to open when you got there.",
                "Photos in chat that wouldn't load now say so in the message, instead of the whole message going missing.",
            ]),
        new Release(
            new Version(2, 3, 0),
            "August 2026",
            New:
            [
                "Events. Host a gathering or find one to go to on the new Events board in Discover, filtered by World, Data Center or Region. Each event has its own page with the doors time in your timezone, the place, and how many are going.",
                "Private events. Keep an event off the board and share a code with the people you want there, so only they can open it.",
                "Share to chat. Send an event straight into a conversation, and open it from the message.",
            ],
            Improved: [],
            Fixed: []),
        new Release(
            new Version(2, 2, 0),
            "July 2026",
            New:
            [
                "Choose your font. A new Font setting lets you pick from six typefaces, from a refined serif to a high-legibility face made for easy reading, and the whole app updates the moment you pick.",
            ],
            Improved:
            [
                "A more readable default. Names and text across the app are clearer and less squeezed than before, so it's easier on the eyes at a glance.",
            ],
            Fixed: []),
        new Release(
            new Version(2, 1, 1),
            "July 2026",
            New: [],
            Improved: [],
            Fixed:
            [
                "Every screen shows a scrollbar when there's more to see. On smaller windows some screens didn't always show one, so it wasn't obvious there was more to scroll.",
            ]),
        new Release(
            new Version(2, 1, 0),
            "July 2026",
            New:
            [
                "Side is an option under After dark now, alongside top, vers and bottom.",
            ],
            Improved:
            [
                "Vers is spelled the way people actually write it, in your profile and in the filters.",
            ],
            Fixed: []),
        new Release(
            new Version(2, 0, 1),
            "July 2026",
            New: [],
            Improved: [],
            Fixed:
            [
                "You can resize the window again. Drag any edge or the corner to scale the app up on a big monitor or down on a small one, and it keeps its shape as it goes.",
            ]),
        new Release(
            new Version(2, 0, 0),
            "July 2026",
            New:
            [
                "Seventy-three themes, grouped into sets you can open one at a time: pastels, metallics, neons, earth, jewel tones, colour blends and more pride flags. Paper light is a full daytime theme.",
                "Delete a thread. Messages has a Deleted tab now, and a thread's menu can delete it, put it back, or clear it for good.",
                "Favorites is a filter on the grid, so starred people show up in the same layout as everyone else.",
                "Show your passphrase while you type it, with the eye in the field.",
                "Type an age straight in instead of clicking the arrows.",
            ],
            Improved:
            [
                "A new look right through the app: warm near-black, cream text and a single gold accent, with square edges and editorial type.",
                "The minimised launcher is a solid plate carrying the Eikon mark, and it takes the colour of whichever theme you pick.",
                "Albums have been rebuilt. The list shows each cover, count and who can see it, and an album is renamed in place.",
                "Settings has been rebuilt around the same look, with the theme picker on its own screen.",
            ],
            Fixed:
            [
                "Your theme carries over when you update. A theme picked in an older version maps onto the new set instead of resetting.",
                "Your albums are reachable again from Your profile.",
                "Opening an album no longer flashes 0 photos before the real count arrives.",
                "Fixed a crash that could happen while photos were still loading.",
            ]),
        new Release(
            new Version(1, 7, 0),
            "July 2026",
            New:
            [
                "Set your text size in Settings. A slider scales the whole app, from a little smaller to much larger, so it's easier to read.",
                "Refresh the grid with the new button to pull in whoever just came online, without leaving the screen.",
            ],
            Improved: [],
            Fixed: []),
        new Release(
            new Version(1, 6, 2),
            "July 2026",
            New: [],
            Improved: [],
            Fixed:
            [
                "Opening someone's profile no longer gets stuck on 'Loading' after a brief connection hiccup. It retries on its own, and only shows a message if it really can't reach the profile.",
            ]),
        new Release(
            new Version(1, 6, 1),
            "July 2026",
            New: [],
            Improved: [],
            Fixed:
            [
                "Albums shared with you in a chat now open right away, instead of showing an empty folder or nothing when opened from a profile.",
                "A 'New message' notice no longer keeps coming back with nothing behind it, which could happen after the other person reinstalled Eikon.",
            ]),
        new Release(
            new Version(1, 6, 0),
            "July 2026",
            New:
            [
                "Scroll the grid to load more people. It no longer stops after the first screen, so you can browse everyone in your World, Data Center, or Region.",
            ],
            Improved:
            [
                "Online members show first now, and the order refreshes each day so you're not always seeing the same profiles.",
            ],
            Fixed: []),
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
