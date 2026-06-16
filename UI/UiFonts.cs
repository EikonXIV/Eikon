using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;

namespace Eikon.UI;

// The Eikon type scale. Each size is built from the Dalamud default asset font at its real
// on-screen pixel size so text stays crisp rather than being stretched from one base size.
internal sealed class UiFonts : IDisposable
{
    private const float CaptionPx = 15f;
    private const float BodyPx = 18f;
    private const float TitlePx = 22f;

    private readonly IDalamudPluginInterface pluginInterface;

    public UiFonts(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
        var atlas = pluginInterface.UiBuilder.FontAtlas;
        this.Caption = Build(atlas, CaptionPx);
        this.Body = Build(atlas, BodyPx);
        this.Title = Build(atlas, TitlePx);
    }

    public IFontHandle Caption { get; }

    public IFontHandle Body { get; }

    public IFontHandle Title { get; }

    // The shared FontAwesome icon font. Owned by Dalamud, so it is not disposed here.
    public IFontHandle Icon => this.pluginInterface.UiBuilder.IconFontHandle;

    private static IFontHandle Build(IFontAtlas atlas, float designPx) =>
        atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
            tk.AddDalamudAssetFont(
                Dalamud.DalamudAsset.NotoSansCjkMedium,
                new SafeFontConfig { SizePx = designPx * ImGuiHelpers.GlobalScale })));

    public void Dispose()
    {
        this.Caption.Dispose();
        this.Body.Dispose();
        this.Title.Dispose();
    }
}
