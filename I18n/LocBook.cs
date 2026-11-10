namespace Eikon.I18n;

internal sealed class LocBook
{
    public const char ToneBreak = '|';

    private readonly LocTable source;
    private readonly LocTable active;

    public LocBook(Locale locale, LocTable source, LocTable active)
    {
        this.Locale = locale;
        this.source = source;
        this.active = active;
    }

    public Locale Locale { get; }

    public string T(string key) =>
        this.active.Strings.TryGetValue(key, out var hit) ? hit
        : this.source.Strings.TryGetValue(key, out var fallback) ? fallback
        : key;

    public string T(string key, params object?[] args) => this.Format(this.T(key), args);

    public string N(string key, long count) =>
        this.Format(this.T(key + (this.Locale.IsOne(count) ? ".one" : ".other")), new object?[] { count });

    public (string Lead, string Tail) Tones(string key)
    {
        var text = this.T(key);
        var at = text.IndexOf(ToneBreak);
        return at < 0 ? (text, string.Empty) : (text[..at], text[(at + 1)..]);
    }

    public string Upper(string text) => text.ToUpper(this.Locale.Culture);

    public IEnumerable<char> Glyphs() =>
        this.active.Strings.Values.SelectMany(v => v).Where(c => c > 0x2FFF).Distinct();

    private string Format(string template, object?[] args)
    {
        try
        {
            return string.Format(this.Locale.Culture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
