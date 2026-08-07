using System;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Open a private event straight from its code, without a board listing (mockups 06/07). A miss is a
// generic "no event matches that code"; a hit caches the event un-gated and opens its full detail.
internal sealed class EventCodeLookupScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly EventService events;
    private readonly Selection selection;

    private string codeInput = string.Empty;
    private string? error;
    private bool searching;
    private bool forceFocus;   // harness only: hold keyboard focus so the caret shows in a screenshot

    public EventCodeLookupScreen(ScreenRouter router, Kit kit, UiFonts fonts, EventService events, Selection selection)
    {
        this.router = router;
        this.kit = kit;
        this.fonts = fonts;
        this.events = events;
        this.selection = selection;
    }

    public Screen Id => Screen.EventCodeLookup;

    public bool Chrome => false;

    // Harness seam (Vitrine): prefill the code and a lookup error for a screenshot of the ready state.
    internal void SetForTest(string code, string? error)
    {
        this.codeInput = code;
        this.error = error;
        this.forceFocus = true;
    }

    public void Draw()
    {
        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(20f);
        var headerH = Ui.Px(46f);
        this.DrawHeader(avail.X, pad, headerH);

        ImGui.SetCursorPos(new Vector2(0f, headerH));
        using var body = ImRaii.Child("event_lookup_body", new Vector2(avail.X, avail.Y - headerH), false, ImGuiWindowFlags.AlwaysVerticalScrollbar);
        if (!body.Success)
            return;

        var dl = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;
        var w = width - (pad * 2f);

        // Intro: eyebrow, two-tone serif title, description, then a hairline.
        ImGui.Dummy(new Vector2(0f, Ui.Px(28f)));
        var kp = ImGui.GetCursorScreenPos();
        var keyIcon = FontAwesomeIcon.Key.ToIconString();
        var kis = Ui.Measure(this.fonts.Icon, keyIcon);
        Ui.TextAt(dl, this.fonts.Icon, new Vector2(kp.X + pad, kp.Y), Palette.TextMuted.U32(), keyIcon);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(kp.X + pad + kis.X + Ui.Px(8f), kp.Y + Ui.Px(2f)), Palette.TextMuted.U32(), "PRIVATE LISTING");
        ImGui.Dummy(new Vector2(0f, Ui.Px(26f)));

        var tp = ImGui.GetCursorScreenPos();
        var lead = "Have a code? ";
        var lSz = Ui.Measure(this.fonts.SerifTitle, lead);
        Ui.TextAt(dl, this.fonts.SerifTitle, new Vector2(tp.X + pad, tp.Y), Palette.TextPrimary.U32(), lead);
        Ui.TextAt(dl, this.fonts.SerifItalicTitle, new Vector2(tp.X + pad + lSz.X, tp.Y), Palette.TextSecondary.U32(), "Open the door.");
        ImGui.Dummy(new Vector2(0f, lSz.Y + Ui.Px(10f)));

        var desc = "Private events never appear on the board. Paste the code a host gave you to see the full listing.";
        var descPos = ImGui.GetCursorScreenPos();
        Ui.TextWrappedAt(dl, this.fonts.Caption, new Vector2(descPos.X + pad, descPos.Y), Palette.TextMuted.U32(), desc, w);
        ImGui.Dummy(new Vector2(0f, Ui.MeasureWrapped(this.fonts.Caption, desc, w).Y + Ui.Px(24f)));

        var hp = ImGui.GetCursorScreenPos();
        dl.AddLine(new Vector2(hp.X, hp.Y), new Vector2(hp.X + width, hp.Y), Palette.Border.U32(), 1f);
        ImGui.Dummy(new Vector2(0f, Ui.Px(30f)));

        // Full-width code field: a dark box with the code (or placeholder) centered and letter-spaced.
        var fieldH = Ui.Px(54f);
        var fp = ImGui.GetCursorScreenPos();
        this.CodeField(fp.X + pad, fp.Y, w, fieldH);
        ImGui.SetCursorScreenPos(new Vector2(fp.X, fp.Y + fieldH));   // neutralize the input's own advance

        // Gap under the field, then the error (left-aligned) when a lookup missed.
        ImGui.Dummy(new Vector2(0f, this.error is null ? Ui.Px(38f) : Ui.Px(14f)));
        if (this.error is { } err)
        {
            var ep = ImGui.GetCursorScreenPos();
            Ui.TextAt(dl, this.fonts.Caption, new Vector2(ep.X + pad, ep.Y), Palette.Danger.U32(), err);
            ImGui.Dummy(new Vector2(0f, Ui.Measure(this.fonts.Caption, err).Y + Ui.Px(16f)));
        }

        // Full-width action; muted until a full code is entered, cream once it is ready.
        var btnH = Ui.Px(54f);
        var bp = ImGui.GetCursorScreenPos();
        var ready = this.codeInput.Length >= 6 && !this.searching;
        if (this.FindButton(bp.X + pad, bp.Y, w, btnH, ready))
            this.Find();
        ImGui.SetCursorScreenPos(new Vector2(bp.X, bp.Y + btnH));
    }

    // Full-width code entry. A transparent InputText over the box captures typing (its native text and
    // caret hidden); the code, or an "ABC123" placeholder, is drawn centered and letter-spaced on top.
    // While the field owns the keyboard it gets a gold ring and a blinking caret so it reads as editable.
    private void CodeField(float x, float y, float w, float h)
    {
        var dl = ImGui.GetWindowDrawList();
        var min = new Vector2(x, y);
        var max = new Vector2(x + w, y + h);
        dl.AddRectFilled(min, max, Palette.Surface2.U32());

        var code = this.codeInput;
        var padY = (h - Ui.Measure(this.fonts.Body, "A").Y) * 0.5f;
        bool focused;
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.FrameBgHovered, Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.FrameBgActive, Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.Text, Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.TextSelectedBg, Vector4.Zero))
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 0f))
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 0f))
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(Ui.Px(12f), padY)))
        using (this.fonts.Body.Push())
        {
            ImGui.SetCursorScreenPos(min);
            ImGui.SetNextItemWidth(w);
            if (this.forceFocus)
                ImGui.SetKeyboardFocusHere();
            ImGui.InputTextWithHint("##el_code", string.Empty, ref code, 6, ImGuiInputTextFlags.CharsUppercase);
            focused = ImGui.IsItemActive();
        }

        var upper = code.ToUpperInvariant();
        if (upper.Length > 6)
            upper = upper[..6];
        if (upper != this.codeInput)
            this.error = null;
        this.codeInput = upper;

        var hasText = this.codeInput.Length > 0;
        var font = this.fonts.Count;
        var tracking = Ui.Px(10f);
        var centerX = x + (w * 0.5f);
        var glyphH = Ui.Measure(font, "0").Y;
        var midY = y + ((h - glyphH) * 0.5f);

        if (hasText)
            DrawTracked(dl, font, this.codeInput, centerX, midY, tracking, Palette.TextPrimary.U32());
        else if (!focused)
            DrawTracked(dl, font, "ABC123", centerX, midY, tracking, Palette.TextMuted.U32());

        if (focused)
        {
            dl.AddRect(min, max, Palette.Signal.U32(), 0f, ImDrawFlags.None, Ui.Px(1.5f));
            if (ImGui.GetTime() % 1.0 < 0.55)
            {
                var caretX = hasText ? centerX + (TrackedWidth(font, this.codeInput, tracking) * 0.5f) + Ui.Px(4f) : centerX;
                dl.AddRectFilled(new Vector2(caretX, midY), new Vector2(caretX + Ui.Px(2f), midY + glyphH), Palette.Signal.U32());
            }
        }
    }

    private bool FindButton(float x, float y, float w, float h, bool ready)
    {
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        var clicked = ImGui.InvisibleButton("##el_find", new Vector2(w, h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();

        var fill = ready
            ? (hovered ? Palette.WithAlpha(Palette.TextPrimary, 0.88f) : Palette.TextPrimary)
            : Palette.WithAlpha(Palette.TextPrimary, 0.22f);
        dl.AddRectFilled(new Vector2(x, y), new Vector2(x + w, y + h), fill.U32());

        var label = this.searching ? "SEARCHING" : "FIND EVENT";
        var color = (ready ? Palette.Paper : Palette.TextSecondary).U32();
        var midY = y + ((h - Ui.Measure(this.fonts.Eyebrow, label).Y) * 0.5f);
        DrawTracked(dl, this.fonts.Eyebrow, label, x + (w * 0.5f), midY, Ui.Px(2f), color);
        return clicked && ready;
    }

    // Total width of text drawn with a fixed gap between characters.
    private static float TrackedWidth(IFontHandle font, string text, float tracking)
    {
        var total = 0f;
        for (var i = 0; i < text.Length; i++)
            total += Ui.Measure(font, text[i].ToString()).X + (i < text.Length - 1 ? tracking : 0f);
        return total;
    }

    // Draw text centered on centerX with a fixed gap between characters (the reference's spaced-out code).
    private static void DrawTracked(ImDrawListPtr dl, IFontHandle font, string text, float centerX, float y, float tracking, uint color)
    {
        var x = centerX - (TrackedWidth(font, text, tracking) * 0.5f);
        foreach (var ch in text)
        {
            var s = ch.ToString();
            Ui.TextAt(dl, font, new Vector2(x, y), color, s);
            x += Ui.Measure(font, s).X + tracking;
        }
    }

    private void DrawHeader(float width, float pad, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var midY = origin.Y + (height * 0.5f);

        var back = FontAwesomeIcon.ChevronLeft.ToIconString();
        var bs = Ui.Measure(this.fonts.Icon, back);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (bs.Y * 0.5f)));
        if (ImGui.InvisibleButton("##el_back", bs))
        {
            this.codeInput = string.Empty;
            this.error = null;
            this.searching = false;
            this.router.Navigate(Screen.Grid);
        }
        Ui.TextAt(dl, this.fonts.Icon, ImGui.GetItemRectMin(), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32(), back);

        var title = "ENTER A CODE";
        var ts = Ui.Measure(this.fonts.Eyebrow, title);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(origin.X + ((width - ts.X) * 0.5f), midY - (ts.Y * 0.5f)), Palette.TextSecondary.U32(), title);

        dl.AddLine(new Vector2(origin.X, origin.Y + height), new Vector2(origin.X + width, origin.Y + height), Palette.Border.U32(), 1f);
    }

    private async void Find()
    {
        this.searching = true;
        try
        {
            var e = await this.events.LookupAsync(this.codeInput);
            if (e != null)
            {
                this.selection.EventId = e.Id;
                this.selection.EventReturn = Screen.Grid;
                this.codeInput = string.Empty;
                this.error = null;
                this.router.Navigate(Screen.EventDetail);
            }
            else
            {
                this.error = "No event matches that code.";
            }
        }
        finally
        {
            this.searching = false;
        }
    }
}
