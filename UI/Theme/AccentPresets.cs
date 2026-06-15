namespace Eikon.UI.Theme;

internal readonly record struct AccentPreset(string Name, uint Rgb);

// The twelve built-in accent colors from DESIGN.md section 4. Index 0 (Blue) is the default.
internal static class AccentPresets
{
    public static readonly IReadOnlyList<AccentPreset> All = new[]
    {
        new AccentPreset("Blue", 0x4D9EFF),
        new AccentPreset("Sky", 0x38BDF8),
        new AccentPreset("Indigo", 0x6366F1),
        new AccentPreset("Violet", 0x8B5CF6),
        new AccentPreset("Fuchsia", 0xD946EF),
        new AccentPreset("Pink", 0xEC4899),
        new AccentPreset("Rose", 0xF43F5E),
        new AccentPreset("Orange", 0xFB923C),
        new AccentPreset("Amber", 0xF59E0B),
        new AccentPreset("Emerald", 0x10B981),
        new AccentPreset("Teal", 0x14B8A6),
        new AccentPreset("Lime", 0x84CC16),
    };
}
