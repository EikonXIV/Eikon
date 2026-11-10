using System.Globalization;

namespace Eikon.I18n;

internal enum Script
{
    Latin,
    Japanese,
    Korean,
    SimplifiedChinese,
}

internal enum PluralRule
{
    OneOther,
    ZeroAndOneAreOne,
    OtherOnly,
}

internal sealed record Locale(string Id, string NativeName, string CultureName, Script Script, PluralRule Plural)
{
    public CultureInfo Culture { get; } = ResolveCulture(CultureName);

    public bool IsOne(long count) => this.Plural switch
    {
        PluralRule.OneOther => count == 1,
        PluralRule.ZeroAndOneAreOne => count is 0 or 1,
        _ => false,
    };

    private static CultureInfo ResolveCulture(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}

internal static class Locales
{
    public static readonly Locale English = new("en", "English", "en-US", Script.Latin, PluralRule.OneOther);

    public static readonly Locale Pseudo = new("qps", "Pseudo", "en-US", Script.Latin, PluralRule.OneOther);

    public static readonly IReadOnlyList<Locale> All = new[]
    {
        English,
        new Locale("ja", "日本語", "ja-JP", Script.Japanese, PluralRule.OtherOnly),
        new Locale("ko", "한국어", "ko-KR", Script.Korean, PluralRule.OtherOnly),
        new Locale("zh-Hans", "简体中文", "zh-CN", Script.SimplifiedChinese, PluralRule.OtherOnly),
        new Locale("fr", "Français", "fr-FR", Script.Latin, PluralRule.ZeroAndOneAreOne),
        new Locale("de", "Deutsch", "de-DE", Script.Latin, PluralRule.OneOther),
        new Locale("es-419", "Español", "es-419", Script.Latin, PluralRule.OneOther),
    };

    public static Locale? ById(string? id) =>
        id == Pseudo.Id ? Pseudo : All.FirstOrDefault(l => l.Id == id);

    public static Locale Resolve(string? setting) => ById(setting) ?? English;
}
