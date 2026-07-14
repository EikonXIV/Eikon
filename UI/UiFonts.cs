using System.Threading.Tasks;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;

namespace Eikon.UI;

// The Eikon type scale. Each size is rasterized from the Dalamud asset font at its real on-screen pixel
// size (design px x the game HUD scale x buildScale) so text stays crisp. A Text size change re-rasterizes
// in the background (Rebuild); until it lands, the draw-list text is scaled to the new size in Ui, so the
// change reads instantly and then sharpens.
internal sealed class UiFonts : IDisposable
{
    private const float CaptionPx = 15f;
    private const float BodyPx = 18f;
    private const float TitlePx = 22f;
    private const float IconPx = 17f;

    private readonly IFontAtlas atlas;
    private float buildScale;   // factor the build delegates rasterize at (the rebuild target)

    public UiFonts(IDalamudPluginInterface pluginInterface)
    {
        this.atlas = pluginInterface.UiBuilder.FontAtlas;
        this.buildScale = Ui.Scale;
        Ui.FontBakedScale = Ui.Scale;   // first build rasterizes at the startup Text size
        this.Caption = this.BuildText(CaptionPx);
        this.Body = this.BuildText(BodyPx);
        this.Title = this.BuildText(TitlePx);
        this.Icon = this.BuildIcon(IconPx);
    }

    public IFontHandle Caption { get; }

    public IFontHandle Body { get; }

    public IFontHandle Title { get; }

    // Our own scaled FontAwesome handle rather than Dalamud's fixed-size shared one, so icons grow with the
    // text when the member enlarges it.
    public IFontHandle Icon { get; }

    // Re-rasterize every handle at targetScale in the background. The build delegates read buildScale, so
    // the rebuild bakes at the new size; Ui.FontBakedScale flips to match only once the build finishes, so
    // draw-list text (scaled by Scale/FontBakedScale) reads at the new size immediately and sharpens then.
    public void Rebuild(float targetScale)
    {
        this.buildScale = targetScale;
        this.atlas.BuildFontsAsync().ContinueWith(
            t => { if (t.Status == TaskStatus.RanToCompletion) Ui.FontBakedScale = targetScale; },
            TaskScheduler.Default);
    }

    private float ScaledPx(float designPx) => designPx * ImGuiHelpers.GlobalScale * this.buildScale;

    private IFontHandle BuildText(float designPx) =>
        this.atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
            tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansCjkMedium, new SafeFontConfig { SizePx = this.ScaledPx(designPx) })));

    private IFontHandle BuildIcon(float designPx) =>
        this.atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
            tk.AddFontAwesomeIconFont(new SafeFontConfig { SizePx = this.ScaledPx(designPx) })));

    public void Dispose()
    {
        this.Caption.Dispose();
        this.Body.Dispose();
        this.Title.Dispose();
        this.Icon.Dispose();
    }
}
