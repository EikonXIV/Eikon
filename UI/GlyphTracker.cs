namespace Eikon.UI;

// Remembers which Hangul and CJK characters have actually been put on screen, so the font atlas bakes
// those instead of whole script blocks. Memory is fixed: one flag per BMP code unit plus at most Cap
// characters per script. Past the cap new characters are ignored, so no amount of incoming text (a
// hostile message included) can grow the atlas further.
internal sealed class GlyphTracker
{
    public const int Cap = 3000;

    private const char FirstTracked = 'ᄀ';

    private readonly bool[] seen = new bool[0x10000];
    private readonly object sync = new();
    private readonly List<char> hangul = new();
    private readonly List<char> cjk = new();
    private volatile int hangulCount;
    private volatile int cjkCount;

    public static GlyphTracker Shared { get; } = new();

    public int HangulCount => this.hangulCount;

    public int CjkCount => this.cjkCount;

    public static bool IsHangul(char c) =>
        c is (>= 'ᄀ' and <= 'ᇿ') or (>= '㄰' and <= '㆏') or (>= '가' and <= '힣');

    public static bool IsCjk(char c) =>
        c is (>= '　' and <= 'ヿ') or (>= '㐀' and <= '䶿') or (>= '一' and <= '鿿')
            or (>= '豈' and <= '﫿') or (>= '＀' and <= '￯');

    public void Observe(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        foreach (var c in text)
        {
            if (c < FirstTracked || this.seen[c])
                continue;
            this.Add(c);
        }
    }

    public char[] Hangul()
    {
        lock (this.sync)
            return this.hangul.ToArray();
    }

    public char[] Cjk()
    {
        lock (this.sync)
            return this.cjk.ToArray();
    }

    private void Add(char c)
    {
        lock (this.sync)
        {
            if (this.seen[c])
                return;
            this.seen[c] = true;

            if (IsHangul(c))
            {
                if (this.hangul.Count < Cap)
                {
                    this.hangul.Add(c);
                    this.hangulCount = this.hangul.Count;
                }
            }
            else if (IsCjk(c) && this.cjk.Count < Cap)
            {
                this.cjk.Add(c);
                this.cjkCount = this.cjk.Count;
            }
        }
    }
}
