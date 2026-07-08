using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;

namespace Eikon.UI;

// The Eikon type scale, ported to the warm-editorial families (Lovable prototype): Instrument Serif
// for the wordmark, titles and names (with an italic cut for the two-tone treatment), Inter Tight for
// UI and body, JetBrains Mono for eyebrows, counters and meta. Each handle is built at its real
// on-screen pixel size so text stays crisp. Fonts are bundled as embedded resources (see the csproj).
// NOTE: CJK is not yet merged in, so non-Latin names render as tofu for now — merge Noto Sans CJK into
// the sans/serif families as a follow-up.
internal sealed class UiFonts : IDisposable
{
    private const string SerifFile = "Eikon.Fonts.InstrumentSerif-Regular.ttf";
    private const string SerifItalicFile = "Eikon.Fonts.InstrumentSerif-Italic.ttf";
    private const string SansFile = "Eikon.Fonts.InterTight.ttf";
    private const string MonoFile = "Eikon.Fonts.JetBrainsMono.ttf";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly List<IFontHandle> owned = new();

    public UiFonts(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
        var atlas = pluginInterface.UiBuilder.FontAtlas;

        this.Title = this.Make(atlas, SerifFile, 22f);
        this.SerifTitle = this.Make(atlas, SerifFile, 28f);
        this.SerifItalicTitle = this.Make(atlas, SerifItalicFile, 28f);
        this.Body = this.Make(atlas, SansFile, 18f);
        this.Caption = this.Make(atlas, SansFile, 15f);
        this.Label = this.Make(atlas, SansFile, 13f);
        this.LabelSmall = this.Make(atlas, SansFile, 11f);
        this.Eyebrow = this.Make(atlas, MonoFile, 11f);
        this.Count = this.Make(atlas, MonoFile, 15f);
    }

    public IFontHandle Title { get; }             // Instrument Serif 22 — wordmark, legacy headers
    public IFontHandle SerifTitle { get; }        // Instrument Serif 28 — screen titles
    public IFontHandle SerifItalicTitle { get; }  // Instrument Serif Italic 28 — two-tone titles
    public IFontHandle Body { get; }              // Inter Tight 18 — body, tile names
    public IFontHandle Caption { get; }           // Inter Tight 15 — small content
    public IFontHandle Label { get; }             // Inter Tight 13 — nav, values
    public IFontHandle LabelSmall { get; }        // Inter Tight 11 — chips
    public IFontHandle Eyebrow { get; }           // JetBrains Mono 11 — eyebrows, tags, tabs, meta
    public IFontHandle Count { get; }             // JetBrains Mono 15 — counters

    // The shared FontAwesome icon font. Owned by Dalamud, so it is not disposed here.
    public IFontHandle Icon => this.pluginInterface.UiBuilder.IconFontHandle;

    private IFontHandle Make(IFontAtlas atlas, string resource, float designPx)
    {
        var handle = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
            tk.AddFontFromStream(
                typeof(UiFonts).Assembly.GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException($"Missing embedded font resource: {resource}"),
                new SafeFontConfig { SizePx = designPx * ImGuiHelpers.GlobalScale },
                false,
                resource)));
        this.owned.Add(handle);
        return handle;
    }

    public void Dispose()
    {
        foreach (var handle in this.owned)
            handle.Dispose();
    }
}
