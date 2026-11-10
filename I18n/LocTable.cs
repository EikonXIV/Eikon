using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Eikon.I18n;

internal sealed class LocTable
{
    public static readonly LocTable Empty = new(new Dictionary<string, string>());

    private const string AccentedUpper = "ÀBÇĎÉFĜĤÎĴĶĻMÑÖPQŘŠŤÛVŴXÝŽ";
    private const string AccentedLower = "àbçďéfĝĥîĵķļmñöpqřšťûvŵxýž";

    public LocTable(IReadOnlyDictionary<string, string> strings) => this.Strings = strings;

    public IReadOnlyDictionary<string, string> Strings { get; }

    public static LocTable Parse(string json) =>
        new(JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>());

    public static LocTable? Load(string localeId)
    {
        using var stream = typeof(LocTable).Assembly.GetManifestResourceStream($"Eikon.Loc.{localeId}.json");
        if (stream == null)
            return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Parse(reader.ReadToEnd());
    }

    // Date and time patterns ("h:mm tt") must survive the pseudo-locale untouched.
    private static readonly Regex FormatPattern = new("^[dMyhHmst:,.|/ -]+$", RegexOptions.Compiled);

    public static LocTable PseudoFrom(LocTable source) =>
        new(source.Strings.ToDictionary(p => p.Key, p => FormatPattern.IsMatch(p.Value) ? p.Value : Pseudolocalize(p.Value)));

    public static string Pseudolocalize(string text)
    {
        var sb = new StringBuilder("[");
        var inPlaceholder = false;
        var letters = 0;
        foreach (var c in text)
        {
            if (c == '{')
                inPlaceholder = true;

            if (!inPlaceholder && c is >= 'A' and <= 'Z')
            {
                sb.Append(AccentedUpper[c - 'A']);
                letters++;
            }
            else if (!inPlaceholder && c is >= 'a' and <= 'z')
            {
                sb.Append(AccentedLower[c - 'a']);
                letters++;
            }
            else
            {
                sb.Append(c);
            }

            if (c == '}')
                inPlaceholder = false;
        }

        var pad = (int)MathF.Ceiling(letters * 0.35f);
        if (pad > 0)
            sb.Append(' ').Append('·', pad);
        return sb.Append(']').ToString();
    }
}
