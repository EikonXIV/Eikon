using System;
using Dalamud.Interface;
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

        var desc = "Private events never appear on the board. Enter the code a host gave you to see the full listing.";
        var descPos = ImGui.GetCursorScreenPos();
        Ui.TextWrappedAt(dl, this.fonts.Caption, new Vector2(descPos.X + pad, descPos.Y), Palette.TextMuted.U32(), desc, w);
        ImGui.Dummy(new Vector2(0f, Ui.MeasureWrapped(this.fonts.Caption, desc, w).Y + Ui.Px(24f)));

        var hp = ImGui.GetCursorScreenPos();
        dl.AddLine(new Vector2(hp.X, hp.Y), new Vector2(hp.X + width, hp.Y), Palette.Border.U32(), 1f);
        ImGui.Dummy(new Vector2(0f, Ui.Px(30f)));

        // Code entry, centered.
        var fieldW = Math.Min(w, Ui.Px(240f));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((width - fieldW) * 0.5f));
        var code = this.codeInput;
        this.kit.TextField("##el_code", ref code, "ABC123", fieldW);
        var upper = code.ToUpperInvariant();
        if (upper.Length > 6)
            upper = upper[..6];
        if (upper != this.codeInput)
            this.error = null;
        this.codeInput = upper;
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));

        if (this.error is { } err)
            Ui.CenteredText(width, this.fonts.Caption, Palette.Danger, err);
        ImGui.Dummy(new Vector2(0f, Ui.Px(10f)));

        var btnW = Math.Min(w, Ui.Px(240f));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((width - btnW) * 0.5f));
        var ready = this.codeInput.Length >= 6 && !this.searching;
        if (this.kit.PrimaryButton("##el_find", this.searching ? "Searching" : "Find event", btnW) && ready)
            this.Find();
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
            this.router.Navigate(Screen.Grid);
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
