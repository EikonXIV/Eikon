using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures.TextureWraps;

namespace Eikon.Services;

// Image loading and the file picker. Textures are resolved through Dalamud's shared cache so we do
// not own their lifetime. The host draws the dialog manager each frame (see EikonBootstrap).
internal sealed class Media
{
    private readonly FileDialogManager dialogs = new();

    public void PickImage(Action<string> onPicked)
    {
        this.dialogs.OpenFileDialog("Choose a photo", "Images{.png,.jpg,.jpeg,.bmp,.webp}", (ok, path) =>
        {
            if (ok && !string.IsNullOrEmpty(path))
                onPicked(path);
        });
    }

    public IDalamudTextureWrap? Load(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var wrap = Plugin.TextureProvider.GetFromFile(path).GetWrapOrDefault();
        return wrap is { Width: > 0 } ? wrap : null;
    }

    public void Draw() => this.dialogs.Draw();
}
