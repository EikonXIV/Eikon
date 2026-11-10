using System.Globalization;

namespace Eikon.I18n;

internal static class Loc
{
    private static readonly LocTable Source = LocTable.Load(Locales.English.Id) ?? LocTable.Empty;

    private static LocBook book = new(Locales.English, Source, Source);

#if DEBUG
    public static bool Preview { get; set; } = true;
#else
    public static bool Preview { get; set; }
#endif

    public static event Action? Changed;

    public static Locale Current => book.Locale;

    public static CultureInfo Culture => book.Locale.Culture;

    public static IEnumerable<Locale> Offered => Preview ? Locales.All.Append(Locales.Pseudo) : Locales.All;

    public static void Apply(string? setting)
    {
        var locale = Locales.Resolve(setting);
        var active = locale.Id == Locales.Pseudo.Id
            ? LocTable.PseudoFrom(Source)
            : LocTable.Load(locale.Id) ?? LocTable.Empty;
        var changed = locale.Id != book.Locale.Id;
        book = new LocBook(locale, Source, active);
        if (changed)
            Changed?.Invoke();
    }

    public static string T(string key) => book.T(key);

    public static string T(string key, params object?[] args) => book.T(key, args);

    public static string N(string key, long count) => book.N(key, count);

    public static (string Lead, string Tail) Tones(string key) => book.Tones(key);

    public static string Upper(string text) => book.Upper(text);

    public static IEnumerable<char> Glyphs() => book.Glyphs();
}
