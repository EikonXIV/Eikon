namespace Eikon.UI;

internal static class GlyphSubsets
{
    public static ushort[] ToRanges(IEnumerable<char> chars)
    {
        var sorted = chars.Distinct().OrderBy(c => c).ToArray();
        var ranges = new List<ushort>();
        for (var i = 0; i < sorted.Length; i++)
        {
            var start = sorted[i];
            while (i + 1 < sorted.Length && sorted[i + 1] == sorted[i] + 1)
                i++;
            ranges.Add(start);
            ranges.Add(sorted[i]);
        }

        ranges.Add(0);
        return ranges.ToArray();
    }

    public static IEnumerable<char> Expand(ushort[] ranges)
    {
        for (var i = 0; i + 1 < ranges.Length && ranges[i] != 0; i += 2)
        {
            for (int c = ranges[i]; c <= ranges[i + 1]; c++)
                yield return (char)c;
        }
    }

    public static ushort[] Without(ushort[] ranges, IEnumerable<char> excluded)
    {
        var skip = new HashSet<char>(excluded);
        return ToRanges(Expand(ranges).Where(c => !skip.Contains(c)));
    }
}
