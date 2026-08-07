using System.Security.Cryptography;
using Eikon.Contracts;

namespace Eikon.Net;

// Pure event formatting and logic helpers shared across the event screens. Kept free of Dalamud so they
// can be unit-tested (Dalamud won't load under dotnet test), and in one place so the board, detail, and
// chat card read the same.
internal static class EventFormat
{
    // Ambiguity-free alphabet for entry codes (no I, O, 0, 1), matching the web.
    internal const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    internal static string KindLabel(EventKindElement kind) => kind switch
    {
        EventKindElement.Club => "Club night",
        EventKindElement.Gathering => "Gathering",
        EventKindElement.Performance => "Performance",
        EventKindElement.Raid => "Raid",
        EventKindElement.Roleplay => "Roleplay",
        _ => "Market",
    };

    internal static string Duration(long mins)
    {
        if (mins < 60)
            return $"{mins}m";
        var h = mins / 60;
        var m = mins % 60;
        return m > 0 ? $"{h}h {m}m" : $"{h}h";
    }

    // Today / Tomorrow / weekday, relative to now (passed in so tests are deterministic).
    internal static string DayLabel(DateTimeOffset local, DateTimeOffset now)
    {
        var diff = (local.Date - now.Date).Days;
        return diff switch { 0 => "Today", 1 => "Tomorrow", _ => local.ToString("dddd") };
    }

    internal static string DayLabel(DateTimeOffset local) => DayLabel(local, DateTimeOffset.Now);

    // An event is over once its start plus its duration is in the past (matches the server's board cutoff).
    internal static bool IsEnded(DateTimeOffset startsAt, long durationMins, bool cancelled, DateTimeOffset now)
        => !cancelled && startsAt.AddMinutes(durationMins) < now;

    internal static bool IsEnded(DateTimeOffset startsAt, long durationMins, bool cancelled)
        => IsEnded(startsAt, durationMins, cancelled, DateTimeOffset.Now);

    // Six characters over the ambiguity-free alphabet, drawn from a cryptographic RNG.
    internal static string GenerateCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(6);
        var s = new char[6];
        for (var i = 0; i < 6; i++)
            s[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
        return new string(s);
    }
}
