namespace Eikon.UI;

// The member's Text size preference: a percentage from a fixed set of steps, applied as a whole-UI zoom
// factor. Discrete steps so the Appearance slider snaps and each change is one deliberate font rebuild.
// Kept as pure logic (no Dalamud) so the steps, clamp, and mapping are unit-tested; the actual font
// rebuild is in UiFonts and the layout scaling is folded into Ui.Px.
internal static class TextScale
{
    // Small to large, in percent. 100 is the default. Widened past 100 for a real accessibility bump.
    public static readonly int[] Steps = { 85, 95, 100, 115, 130, 150 };

    public const int Default = 100;

    public static int Min => Steps[0];

    public static int Max => Steps[^1];

    // A persisted value can be out of range or off-grid if an older or newer build wrote it; clamp on the
    // way in for the factor, and snap to the nearest step for the slider position.
    public static int Clamp(int percent) => Math.Clamp(percent, Min, Max);

    public static float ToFactor(int percent) => Clamp(percent) / 100f;

    public static int NearestStepIndex(int percent)
    {
        var best = 0;
        for (var i = 1; i < Steps.Length; i++)
        {
            if (Math.Abs(Steps[i] - percent) < Math.Abs(Steps[best] - percent))
                best = i;
        }

        return best;
    }
}
