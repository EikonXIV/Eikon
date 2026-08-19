namespace Eikon.UI;

// Pure easing and timing helpers for the few places the UI moves. Kept free of ImGui so the
// transitions built on them are unit-testable; callers pass ImGui.GetTime() as `now`.
internal static class Motion
{
    public static float Clamp01(float t) => t < 0f ? 0f : t > 1f ? 1f : t;

    public static float Lerp(float a, float b, float t) => a + ((b - a) * Clamp01(t));

    public static float EaseOutCubic(float t)
    {
        t = Clamp01(t);
        var u = 1f - t;
        return 1f - (u * u * u);
    }

    public static float EaseInOutCubic(float t)
    {
        t = Clamp01(t);
        if (t < 0.5f)
            return 4f * t * t * t;
        var u = (-2f * t) + 2f;
        return 1f - (u * u * u / 2f);
    }

    // Progress of the sub-window [start, end] within a larger timeline value t, clamped to 0..1.
    public static float Segment(float t, float start, float end) =>
        end <= start ? (t >= end ? 1f : 0f) : Clamp01((t - start) / (end - start));
}

// A started-at marker: elapsed seconds since Start, or not running at all.
internal struct Timeline
{
    private double startedAt;

    public bool Running { get; private set; }

    public void Start(double now)
    {
        this.startedAt = now;
        this.Running = true;
    }

    public void Stop() => this.Running = false;

    public float Elapsed(double now) => this.Running ? (float)Math.Max(0.0, now - this.startedAt) : 0f;

    public float Progress(double now, float duration) =>
        duration <= 0f ? 1f : Motion.Clamp01(this.Elapsed(now) / duration);

    public bool Done(double now, float duration) => this.Running && this.Elapsed(now) >= duration;
}
