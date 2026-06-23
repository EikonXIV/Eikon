using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Eikon.Services;

// Bakes the profile-photo crop (target aspect + zoom + vertical pan) into JPEG bytes for upload.
// The crop math mirrors the editor preview (CoverUv) so what the member frames is what gets stored,
// instead of uploading the original uncropped image.
internal static class ImageCrop
{
    // Longest stored edge. Profile tiles are small, so this keeps uploads sharp but well under the
    // server's size cap.
    private const int MaxHeight = 1280;

    // Drop EXIF/GPS/XMP/IPTC before encoding so an uploaded photo can't carry the member's location,
    // device, or capture info. ImageSharp preserves these on re-encode unless they're cleared.
    private static void StripMetadata(Image image)
    {
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IptcProfile = null;
    }

    public static byte[] ToJpeg(string path, float targetAspect, float zoom, float centerX, float centerY)
    {
        using var image = Image.Load(path);
        var w = image.Width;
        var h = image.Height;

        // Same cover-crop as the preview: fill the target aspect, shrink by zoom, position by center.
        float cropW, cropH;
        if ((float)w / h > targetAspect)
        {
            cropH = h;
            cropW = h * targetAspect;
        }
        else
        {
            cropW = w;
            cropH = w / targetAspect;
        }

        cropW /= zoom;
        cropH /= zoom;

        var rectW = Math.Clamp((int)MathF.Round(cropW), 1, w);
        var rectH = Math.Clamp((int)MathF.Round(cropH), 1, h);
        var x0 = Math.Clamp((int)MathF.Round((centerX * w) - (cropW * 0.5f)), 0, w - rectW);
        var y0 = Math.Clamp((int)MathF.Round((centerY * h) - (cropH * 0.5f)), 0, h - rectH);

        image.Mutate(ctx =>
        {
            ctx.Crop(new Rectangle(x0, y0, rectW, rectH));
            if (rectH > MaxHeight)
                ctx.Resize((int)MathF.Round(MaxHeight * targetAspect), MaxHeight);
        });

        StripMetadata(image);
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, new JpegEncoder { Quality = 88 });
        return ms.ToArray();
    }

    // Downscale to fit within maxEdge (preserving aspect; never upscales), drop metadata, and
    // JPEG-encode. Used for chat images so the uploaded blob stays small and carries no EXIF/GPS.
    public static byte[] ResizeJpeg(string path, int maxEdge)
    {
        using var image = Image.Load(path);
        var longest = Math.Max(image.Width, image.Height);
        if (longest > maxEdge)
        {
            var scale = (float)maxEdge / longest;
            image.Mutate(ctx => ctx.Resize((int)MathF.Round(image.Width * scale), (int)MathF.Round(image.Height * scale)));
        }

        StripMetadata(image);
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, new JpegEncoder { Quality = 82 });
        return ms.ToArray();
    }
}
