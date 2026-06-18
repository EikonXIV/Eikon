using System.Collections.Generic;
using Dalamud.Configuration;

namespace Eikon.Config;

// Persisted plugin configuration. Stored by Dalamud per character-independent plugin config.
internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Index into AccentPresets.All. 0 is Blue, the default.
    public int AccentPresetIndex { get; set; }

    // Eikon API server. Defaults to production for every build. To run against a local docker-compose
    // server during development, set this to "http://127.0.0.1:8080" in the saved config; it persists
    // and is not reset (the one-time loopback migration in Plugin.cs only runs once, at Version 1 -> 2).
    public string ServerBaseUrl { get; set; } = "https://api.eikon.chat";

    // Persisted session, DPAPI-sealed at rest. The refresh token restores the session across launches;
    // the access token + its expiry let a relaunch or plugin update inside the token's lifetime restore
    // instantly without rotating the one-time refresh token (rotating it on every launch is what made an
    // update force a re-login). NeedsOnboarding is kept so a returning member routes correctly offline.
    public string? RefreshToken { get; set; }
    public string? AccessToken { get; set; }
    public long AccessExpiresAtUnix { get; set; }
    public bool NeedsOnboarding { get; set; }

    // Notifications. A coalesced toast pops up for new messages; these control whether it shows, whether
    // it plays a sound, and where it appears. NotificationCorner is vertical*3 + horizontal where
    // vertical is 0=top/1=bottom and horizontal is 0=left/1=center/2=right (default 2 = top-right).
    public bool NotificationsEnabled { get; set; } = true;
    public bool NotificationSoundEnabled { get; set; } = true;
    public int NotificationCorner { get; set; } = 2;

    // Notification chime loudness, 0-100. Scales the bundled WAV at playback time.
    public int NotificationVolume { get; set; } = 70;

    // Peers (user id strings) muted from the chat overflow menu: no toast, no sound.
    public List<string> MutedConversations { get; set; } = new();

    // Per-conversation chat wallpaper: peer user id string -> absolute path of a local image the
    // viewer picked from the chat overflow menu. Local and cosmetic only - never uploaded, never sent
    // to the peer or the server. A missing or unreadable path silently falls back to the default
    // background. Purely additive and optional, so old and new plugin builds read each other's config.
    public Dictionary<string, string> ChatBackgrounds { get; set; } = new();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
