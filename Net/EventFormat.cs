using System.Security.Cryptography;
using Eikon.Contracts;

namespace Eikon.Net;

// Pure event formatting and logic helpers shared across the event screens. Kept free of Dalamud so they
// can be unit-tested (Dalamud won't load under dotnet test), and in one place so the board, detail, and
// chat card read the same.
internal static class EventFormat
{
    public static bool Uses24HourClock => !Loc.T("eventFormat.time").Contains('t');

    // Ambiguity-free alphabet for entry codes (no I, O, 0, 1), matching the web.
    internal const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    internal static string KindLabel(EventKindElement kind) => kind switch
    {
        EventKindElement.Club => Loc.T("eventFormat.kind.club"),
        EventKindElement.Gathering => Loc.T("eventFormat.kind.gathering"),
        EventKindElement.Performance => Loc.T("eventFormat.kind.performance"),
        EventKindElement.Raid => Loc.T("eventFormat.kind.raid"),
        EventKindElement.Roleplay => Loc.T("eventFormat.kind.roleplay"),
        _ => Loc.T("eventFormat.kind.market"),
    };

    internal static string Duration(long mins)
    {
        if (mins < 60)
            return Loc.T("eventFormat.duration.minutes", mins);
        var h = mins / 60;
        var m = mins % 60;
        return m > 0 ? Loc.T("eventFormat.duration.hoursMinutes", h, m) : Loc.T("eventFormat.duration.hours", h);
    }

    // A 24-hour hour/minute rendered as a 12-hour clock with AM/PM (20:00 -> "8:00 PM").
    internal static string Clock12(int hour, int minute)
    {
        var time = new DateTime(2000, 1, 1, Math.Clamp(hour, 0, 23), Math.Clamp(minute, 0, 59), 0);
        return time.ToString(Loc.T("eventFormat.time"), Loc.Culture);
    }

    // A stored "HH:mm" host clock rendered in 12-hour; returns the input unchanged if it doesn't parse.
    internal static string Clock12(string clock24)
    {
        var parts = clock24.Split(':');
        return parts.Length == 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m)
            ? Clock12(h, m)
            : clock24;
    }

    internal static string Clock12(DateTimeOffset time) => Clock12(time.Hour, time.Minute);

    // Today / Tomorrow / weekday, relative to now (passed in so tests are deterministic).
    internal static string DayLabel(DateTimeOffset local, DateTimeOffset now)
    {
        var diff = (local.Date - now.Date).Days;
        return diff switch
        {
            0 => Loc.T("eventFormat.day.today"),
            1 => Loc.T("eventFormat.day.tomorrow"),
            _ => local.ToString(Loc.T("eventFormat.day.weekday"), Loc.Culture),
        };
    }

    internal static string DayLabel(DateTimeOffset local) => DayLabel(local, DateTimeOffset.Now);

    internal static string MonthDay(DateTimeOffset local) => local.ToString(Loc.T("eventFormat.monthDay"), Loc.Culture);

    internal static string HousingLine(string district, long? ward, long? plot, long? room, string world) =>
        (room is > 0
            ? Loc.T("eventFormat.location.housingRoom", district, ward, plot, room, world)
            : Loc.T("eventFormat.location.housing", district, ward, plot, world)).Trim(' ', '·');

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
