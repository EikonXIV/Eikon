using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures.TextureWraps;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.Services;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Create / edit an event: a four-step wizard (Basics, Timing, Place, Access) with a segment progress
// bar (mockups 13-19). Matches the create.tsx flow: banner preset + upload, kind chips, date/time/tz,
// duration steppers, venue variants, scope/rating/visibility, entry code, and capacity. On publish it
// builds a CreateEventRequest (or UpdateEventRequest in edit mode) and opens the new event's detail.
internal sealed class EventCreateScreen : IScreen, IDisposable
{
    private static readonly string[] Steps = { "BASICS", "TIMING", "PLACE", "ACCESS" };
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    // Section heights: control content plus the gap to the next section. A labelled field is a 22px label
    // over a 44px box; a stepper box is 64px. Both sit above a 24px gap.
    private static float FieldBlock => Ui.Px(22f) + Ui.Px(44f) + Ui.Px(24f);
    private static float StepperBlock => Ui.Px(64f) + Ui.Px(24f);

    // Reset the layout cursor to a section's start and reserve exactly its height, so the absolutely
    // positioned controls (which advance the cursor via InvisibleButton) never double-count spacing.
    private static void Block(Vector2 start, float w, float h)
    {
        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(w, h));
    }

    // Host-selectable timezones (short, label, IANA), west to east, covering every populated offset.
    private static readonly (string Short, string Label, string Iana)[] Timezones =
    {
        ("SST", "Samoa — Pago Pago", "Pacific/Pago_Pago"),
        ("HST", "Hawaii — Honolulu", "Pacific/Honolulu"),
        ("AKT", "Alaska — Anchorage", "America/Anchorage"),
        ("PT", "Pacific — Los Angeles", "America/Los_Angeles"),
        ("MT", "Mountain — Denver", "America/Denver"),
        ("CT", "Central — Chicago", "America/Chicago"),
        ("ET", "Eastern — New York", "America/New_York"),
        ("AT", "Atlantic — Halifax", "America/Halifax"),
        ("BRT", "Brazil — São Paulo", "America/Sao_Paulo"),
        ("AZOT", "Azores", "Atlantic/Azores"),
        ("GMT", "UK & Ireland — London", "Europe/London"),
        ("CET", "Central Europe — Paris", "Europe/Paris"),
        ("EET", "Eastern Europe — Athens", "Europe/Athens"),
        ("MSK", "Moscow & Istanbul", "Europe/Moscow"),
        ("IRST", "Iran — Tehran", "Asia/Tehran"),
        ("GST", "Gulf — Dubai", "Asia/Dubai"),
        ("PKT", "Pakistan — Karachi", "Asia/Karachi"),
        ("IST", "India — Kolkata", "Asia/Kolkata"),
        ("BDT", "Bangladesh — Dhaka", "Asia/Dhaka"),
        ("ICT", "SE Asia — Bangkok", "Asia/Bangkok"),
        ("SGT", "Singapore & China", "Asia/Singapore"),
        ("JST", "Japan & Korea — Tokyo", "Asia/Tokyo"),
        ("ACST", "Central Australia — Adelaide", "Australia/Adelaide"),
        ("AEST", "Eastern Australia — Sydney", "Australia/Sydney"),
        ("SBT", "Solomon Islands — Honiara", "Pacific/Guadalcanal"),
        ("NZST", "New Zealand — Auckland", "Pacific/Auckland"),
        ("TOT", "Tonga — Nuku'alofa", "Pacific/Tongatapu"),
    };

    private static readonly int DefaultTz = Math.Max(0, Array.FindIndex(Timezones, t => t.Iana == "America/New_York"));

    private static readonly (EventKindElement Kind, string Label)[] Kinds =
    {
        (EventKindElement.Gathering, "Gathering"), (EventKindElement.Club, "Club"),
        (EventKindElement.Performance, "Performance"), (EventKindElement.Raid, "Raid"),
        (EventKindElement.Roleplay, "Roleplay"), (EventKindElement.Market, "Market"),
    };

    private static readonly (HousingDistrictEnum Value, string Label)[] DistrictOptions = EventCatalog.Districts
        .Select(d => (d.Value, d.Label)).ToArray();

    private static readonly string[] WardNums = Enumerable.Range(1, 30).Select(n => n.ToString()).ToArray();
    private static readonly string[] PlotNums = Enumerable.Range(1, 60).Select(n => n.ToString()).ToArray();
    private static readonly string[] RoomNums = Enumerable.Range(0, 91).Select(n => n.ToString()).ToArray();

    private readonly ScreenRouter router;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly EventService events;
    private readonly EventCatalog catalog;
    private readonly WorldCatalog worlds;
    private readonly ProfileService profile;
    private readonly Media media;
    private readonly Selection selection;

    private int step;
    private bool prefilled;
    private Guid? editId;

    // Where the last-tapped field sits, so its popup opens anchored just below it (not at a default spot).
    private Vector2 popupAnchor;
    private float popupWidth;
    private float windowLeft;
    private float windowRight;
    private float popupBottomLimit;

    // Basics
    private string title = string.Empty;
    private EventKindElement kind = EventKindElement.Gathering;
    private string description = string.Empty;
    private string tagsText = string.Empty;
    private string bannerPreset = "lounge";
    private byte[]? uploadBytes;
    private IDalamudTextureWrap? uploadPreview;

    // Timing
    private DateTime date = DateTime.Today;
    private DateTime calView = DateTime.Today;
    private int hour = 20;
    private int minute = 0;
    private int tz = DefaultTz;
    private int durHours = 2;
    private int durMins;
    private EventRecurrenceEnum recurrence = EventRecurrenceEnum.None;

    // Place
    private EventVenueEnum venue = EventVenueEnum.Housing;
    private int venueWorldId;
    private int district;
    private int ward = 1;
    private int plot = 1;
    private int room;
    private int zoneIdx;
    private int aetheryteIdx;
    private string discordUrl = string.Empty;
    private string discordNote = string.Empty;

    // Access
    private EventScopeEnum scope = EventScopeEnum.World;
    private EventRatingEnum rating = EventRatingEnum.Sfw;
    private Visibility visibility = Visibility.Public;
    private string code = string.Empty;
    private int capacity;
    private DateTime codeCopiedAt;

    public EventCreateScreen(ScreenRouter router, Kit kit, UiFonts fonts, EventService events, EventCatalog catalog, WorldCatalog worlds, ProfileService profile, Media media, Selection selection)
    {
        this.router = router;
        this.kit = kit;
        this.fonts = fonts;
        this.events = events;
        this.catalog = catalog;
        this.worlds = worlds;
        this.profile = profile;
        this.media = media;
        this.selection = selection;
        this.code = GenerateCode();
    }

    public Screen Id => Screen.EventCreate;

    public bool Chrome => false;

    // Harness seam (Vitrine): jump to a step for a screenshot; a placeholder title keeps step 0 valid.
    internal void SetStep(int s)
    {
        if (this.title.Trim().Length <= 2)
            this.title = "Moonlit Terrace Social";
        this.step = Math.Clamp(s, 0, Steps.Length - 1);
    }

    // Harness seam (Vitrine): open a picker popup on the next frame so a screenshot can capture it.
    internal void OpenPopupForTest(string popupId) => this.pendingPopup = popupId;

    // Harness seam (Vitrine): flip to a private event so a screenshot can show the entry-code row.
    internal void SetPrivateForTest() => this.visibility = Visibility.Private;

    // Harness seam (Vitrine): show the publish-error footer for a screenshot.
    internal void SetErrorForTest() => this.submitError = "Couldn't publish. Check your connection and try again.";

    private string? pendingPopup;
    private bool publishing;
    private string? submitError;

    public void Draw()
    {
        this.catalog.EnsureLoaded();
        this.worlds.EnsureLoaded();
        this.profile.EnsureLoaded();
        this.EnsurePrefill();

        var avail = ImGui.GetContentRegionAvail();
        var wpos = ImGui.GetWindowPos();
        this.windowLeft = wpos.X;
        this.windowRight = wpos.X + avail.X;
        var pad = Ui.Px(20f);
        var headerH = Ui.Px(46f);
        var progressH = Ui.Px(48f);
        var footerH = Ui.Px(64f) + (this.submitError != null ? Ui.Px(26f) : 0f);
        this.popupBottomLimit = wpos.Y + ImGui.GetWindowSize().Y - footerH - Ui.Px(8f);
        this.DrawHeader(avail.X, pad, headerH);
        this.DrawProgress(avail.X, pad, headerH, progressH);

        var bodyTop = headerH + progressH;
        ImGui.SetCursorPos(new Vector2(0f, bodyTop));
        using (var body = ImRaii.Child("event_create_body", new Vector2(avail.X, avail.Y - bodyTop - footerH), false, ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            if (body.Success)
            {
                var width = ImGui.GetContentRegionAvail().X;
                // Zero item spacing so every section's height is exactly the Block() reservation below;
                // the absolutely-positioned controls draw themselves, layout only advances the cursor.
                using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
                {
                    switch (this.step)
                    {
                        case 0: this.DrawBasics(width, pad); break;
                        case 1: this.DrawTiming(width, pad); break;
                        case 2: this.DrawPlace(width, pad); break;
                        default: this.DrawAccess(width, pad); break;
                    }
                }

                // Popups must open and begin in the same window/ID scope as the fields that open them
                // (the fields live inside this child), so draw them here rather than after the child.
                this.DrawPopups(pad);
            }
        }

        this.DrawFooter(avail.X, pad, footerH);
    }

    private void EnsurePrefill()
    {
        if (this.prefilled)
            return;
        this.prefilled = true;
        if (this.selection.EventId is not { } id)
            return;
        var e = this.events.Detail(id);
        if (e == null)
        {
            this.prefilled = false;   // wait for the detail to load
            return;
        }

        this.editId = id;
        this.title = e.Title;
        this.kind = e.Kind;
        this.description = e.Description;
        this.tagsText = string.Join(", ", e.Tags ?? new List<string>());
        this.bannerPreset = e.BannerPreset ?? "lounge";
        var local = e.StartsAt.ToLocalTime();
        this.date = local.Date;
        this.hour = int.TryParse(e.HostClock.Split(':').FirstOrDefault(), out var h) ? h : local.Hour;
        this.minute = int.TryParse(e.HostClock.Split(':').LastOrDefault(), out var m) ? m : local.Minute;
        this.tz = Math.Max(0, Array.FindIndex(Timezones, t => t.Short == e.HostTzLabel));
        this.durHours = (int)(e.DurationMins / 60);
        this.durMins = (int)(e.DurationMins % 60);
        this.venue = e.Venue.Type;
        this.venueWorldId = (int)(e.Venue.WorldId ?? 0);
        this.district = Math.Max(0, Array.FindIndex(DistrictOptions, d => d.Value == e.Venue.District));
        this.ward = (int)(e.Venue.Ward ?? 1);
        this.plot = (int)(e.Venue.Plot ?? 1);
        this.room = (int)(e.Venue.Room ?? 0);
        this.zoneIdx = e.Venue.ZoneId is { } z ? Math.Max(0, this.catalog.Zones.ToList().FindIndex(zn => zn.Id == (int)z)) : 0;
        this.discordUrl = e.Venue.DiscordUrl ?? string.Empty;
        this.discordNote = e.Venue.DiscordNote ?? string.Empty;
        this.scope = e.Scope;
        this.rating = e.Rating;
        this.visibility = e.Visibility;
        this.code = e.EntryCode ?? this.code;
        this.capacity = (int)(e.Capacity ?? 0);
    }

    // ---- chrome ------------------------------------------------------------------------------

    private void DrawHeader(float width, float pad, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var midY = origin.Y + (height * 0.5f);

        var title = this.editId != null ? "EDIT EVENT" : "NEW EVENT";
        var ts = Ui.Measure(this.fonts.Eyebrow, title);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(origin.X + ((width - ts.X) * 0.5f), midY - (ts.Y * 0.5f)), Palette.TextSecondary.U32(), title);

        var x = FontAwesomeIcon.Times.ToIconString();
        var xs = Ui.Measure(this.fonts.Icon, x);
        ImGui.SetCursorScreenPos(new Vector2((origin.X + width - pad) - xs.X, midY - (xs.Y * 0.5f)));
        if (ImGui.InvisibleButton("##ec_close", xs))
            this.router.Navigate(this.selection.EventReturn);
        Ui.TextAt(dl, this.fonts.Icon, ImGui.GetItemRectMin(), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32(), x);

        dl.AddLine(new Vector2(origin.X, origin.Y + height), new Vector2(origin.X + width, origin.Y + height), Palette.Border.U32(), 1f);
    }

    private void DrawProgress(float width, float pad, float top, float height)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = new Vector2(ImGui.GetWindowPos().X, ImGui.GetWindowPos().Y + top);
        var left = origin.X + pad;
        var segW = (width - (pad * 2f)) / Steps.Length;
        var y = origin.Y + Ui.Px(12f);

        for (var i = 0; i < Steps.Length; i++)
        {
            var sx = left + (segW * i);
            var filled = i <= this.step;
            dl.AddLine(new Vector2(sx, y), new Vector2(sx + segW - Ui.Px(4f), y), (filled ? Palette.TextPrimary : Palette.Border).U32(), 1f);
            var col = (i == this.step ? Palette.TextPrimary : Palette.TextSecondary).U32();
            Ui.TextAt(dl, this.fonts.Mono, new Vector2(sx, y + Ui.Px(8f)), col, Steps[i]);
        }

        dl.AddLine(new Vector2(origin.X, origin.Y + height), new Vector2(origin.X + width, origin.Y + height), Palette.Border.U32(), 1f);
    }

    private void DrawFooter(float width, float pad, float height)
    {
        var dl = ImGui.GetWindowDrawList();
        var wp = ImGui.GetWindowPos();
        var wsz = ImGui.GetWindowSize();
        var y = wp.Y + wsz.Y - height;
        dl.AddLine(new Vector2(wp.X, y), new Vector2(wp.X + width, y), Palette.Border.U32(), 1f);

        var errH = 0f;
        if (this.submitError is { } err)
        {
            errH = Ui.Px(26f);
            Ui.TextAt(dl, this.fonts.Caption, new Vector2(wp.X + pad, y + Ui.Px(7f)), Palette.Danger.U32(), err);
        }

        var btnH = Ui.Px(44f);
        var by = y + errH + ((Ui.Px(64f) - btnH) * 0.5f);
        var backW = Ui.Px(96f);
        var backLabel = this.step == 0 ? "CANCEL" : "BACK";
        if (this.OutlineButton(dl, "##ec_back", backLabel, wp.X + pad, by, backW, btnH))
        {
            this.submitError = null;
            if (this.step == 0)
                this.router.Navigate(this.selection.EventReturn);
            else
                this.step--;
        }

        var nextX = wp.X + pad + backW + Ui.Px(10f);
        var nextW = (wp.X + width - pad) - nextX;
        var last = this.step == Steps.Length - 1;
        var nextLabel = last
            ? (this.publishing ? (this.editId != null ? "SAVING" : "PUBLISHING") : this.editId != null ? "SAVE CHANGES" : "PUBLISH EVENT")
            : "CONTINUE";
        var enabled = this.StepValid() && !this.publishing;
        if (this.FilledButton(dl, "##ec_next", nextLabel, nextX, by, nextW, btnH, enabled) && enabled)
        {
            if (last)
                this.Submit();
            else
                this.step++;
        }
    }

    // ---- step 0: basics ----------------------------------------------------------------------

    private void DrawBasics(float width, float pad)
    {
        var dl = ImGui.GetWindowDrawList();
        var x = pad;
        var w = width - (pad * 2f);
        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));

        // Banner label row.
        var lp = ImGui.GetCursorScreenPos();
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(lp.X + x, lp.Y), Palette.TextSecondary.U32(), "BANNER");
        var hint = "3:1 · 1536×512";
        Ui.TextAt(dl, this.fonts.Mono, new Vector2(lp.X + x + w - Ui.Measure(this.fonts.Mono, hint).X, lp.Y + Ui.Px(1f)), Palette.TextMuted.U32(), hint);
        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));

        // Banner preview + filmstrip.
        this.DrawBannerPicker(dl, x, w);

        this.Field(dl, "Title", x, w, () => { var t = this.title; this.kit.TextField("##ec_title", ref t, "The Velvet Hour", w); this.title = t; });

        // Kind chips.
        this.Label(dl, "KIND", x);
        this.DrawKindChips(dl, x, w);

        this.Field(dl, "Description", x, w, () =>
        {
            var d = this.description;
            this.kit.TextField("##ec_desc", ref d, "What happens, who it's for, house rules.", w);
            this.description = d;
        });

        this.Field(dl, "Tags (comma separated)", x, w, () =>
        {
            var t = this.tagsText;
            this.kit.TextField("##ec_tags", ref t, "DJ, Lounge, 18+", w);
            this.tagsText = t;
        });

        ImGui.Dummy(new Vector2(0f, Ui.Px(20f)));
    }

    private void DrawBannerPicker(ImDrawListPtr dl, float x, float w)
    {
        var pos = ImGui.GetCursorScreenPos();
        var previewH = w / 3f;
        var tex = this.uploadPreview ?? (this.uploadBytes == null ? this.events.PresetBanner(this.bannerPreset) : null);
        dl.AddRectFilled(new Vector2(pos.X + x, pos.Y), new Vector2(pos.X + x + w, pos.Y + previewH), Palette.Surface2.U32());
        if (tex != null)
        {
            var (uvMin, uvMax) = Ui.CoverUv(tex.Width, tex.Height, w / previewH);
            dl.AddImage(tex.Handle, new Vector2(pos.X + x, pos.Y), new Vector2(pos.X + x + w, pos.Y + previewH), uvMin, uvMax);
        }

        dl.AddRect(new Vector2(pos.X + x, pos.Y), new Vector2(pos.X + x + w, pos.Y + previewH), Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);

        // Filmstrip: 3 presets + upload tile.
        var gap = Ui.Px(8f);
        var cellW = (w - (gap * 3f)) / 4f;
        var cellH = cellW / 3f * 2f;   // slightly taller thumbs read better
        var stripY = pos.Y + previewH + Ui.Px(10f);
        string[] presetIds = { "lounge", "rooftops", "lakeside" };
        for (var i = 0; i < 3; i++)
        {
            var cx = pos.X + x + (i * (cellW + gap));
            ImGui.SetCursorScreenPos(new Vector2(cx, stripY));
            if (ImGui.InvisibleButton($"##ec_preset_{i}", new Vector2(cellW, cellH)))
            {
                this.bannerPreset = presetIds[i];
                this.uploadBytes = null;
                this.uploadPreview?.Dispose();
                this.uploadPreview = null;
            }

            var pt = this.events.PresetBanner(presetIds[i]);
            var min = new Vector2(cx, stripY);
            var max = min + new Vector2(cellW, cellH);
            dl.AddRectFilled(min, max, Palette.Surface2.U32());
            if (pt != null)
            {
                var (uvMin, uvMax) = Ui.CoverUv(pt.Width, pt.Height, cellW / cellH);
                dl.AddImage(pt.Handle, min, max, uvMin, uvMax);
            }

            var selected = this.uploadBytes == null && this.bannerPreset == presetIds[i];
            dl.AddRect(min, max, (selected ? Palette.TextPrimary : Palette.Border).U32(), 0f, ImDrawFlags.None, selected ? Ui.Px(2f) : 1f);
        }

        var ux = pos.X + x + (3 * (cellW + gap));
        ImGui.SetCursorScreenPos(new Vector2(ux, stripY));
        if (ImGui.InvisibleButton("##ec_upload", new Vector2(cellW, cellH)))
            this.PickBanner();
        var umin = new Vector2(ux, stripY);
        var umax = umin + new Vector2(cellW, cellH);
        var uploadSel = this.uploadBytes != null;
        dl.AddRect(umin, umax, (uploadSel ? Palette.TextPrimary : Palette.Border).U32(), 0f, ImDrawFlags.None, uploadSel ? Ui.Px(2f) : 1f);
        var up = FontAwesomeIcon.Image.ToIconString();
        var us = Ui.Measure(this.fonts.Icon, up);
        Ui.TextAt(dl, this.fonts.Icon, (umin + umax) * 0.5f - (us * 0.5f), Palette.TextMuted.U32(), up);

        var note = "Wide crops read best. The banner is trimmed to 3:1 on the board and in chat shares.";
        var noteY = stripY + cellH + Ui.Px(12f);
        Ui.TextWrappedAt(dl, this.fonts.Caption, new Vector2(pos.X + x, noteY), Palette.TextMuted.U32(), note, w);
        var total = (noteY + Ui.MeasureWrapped(this.fonts.Caption, note, w).Y + Ui.Px(12f)) - pos.Y;
        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(w, total));
    }

    private void DrawKindChips(ImDrawListPtr dl, float x, float w)
    {
        var pos = ImGui.GetCursorScreenPos();
        var chipH = Ui.Px(30f);
        var cx = pos.X + x;
        var cy = pos.Y;
        var rows = 1;
        foreach (var (k, label) in Kinds)
        {
            var ts = Ui.Measure(this.fonts.Label, label);
            var cw = ts.X + (Ui.Px(14f) * 2f);
            if (cx + cw > pos.X + x + w)
            {
                cx = pos.X + x;
                cy += chipH + Ui.Px(8f);
                rows++;
            }

            ImGui.SetCursorScreenPos(new Vector2(cx, cy));
            if (ImGui.InvisibleButton("##ec_kind_" + label, new Vector2(cw, chipH)))
                this.kind = k;
            var active = this.kind == k;
            var min = new Vector2(cx, cy);
            var max = min + new Vector2(cw, chipH);
            var r = chipH * 0.5f;
            if (active)
                dl.AddRectFilled(min, max, Palette.TextPrimary.U32(), r);
            else
                dl.AddRect(min, max, Palette.Border.U32(), r, ImDrawFlags.None, 1f);
            Ui.TextAt(dl, this.fonts.Label, new Vector2(cx + Ui.Px(14f), cy + ((chipH - ts.Y) * 0.5f)), (active ? Palette.Paper : Palette.TextSecondary).U32(), label);
            cx += cw + Ui.Px(8f);
        }

        var chipsH = (rows * chipH) + ((rows - 1) * Ui.Px(8f));
        Block(pos, w, chipsH + Ui.Px(20f));
    }

    // ---- step 1: timing ----------------------------------------------------------------------

    private void DrawTiming(float width, float pad)
    {
        var dl = ImGui.GetWindowDrawList();
        var x = pad;
        var w = width - (pad * 2f);
        var half = (w - Ui.Px(12f)) * 0.5f;
        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));

        // Date + Start fields, side by side.
        var rp = ImGui.GetCursorScreenPos();
        if (this.FieldBox(dl, "date", "Date", rp.X + x, rp.Y, half, this.date.ToString("dd MMM yyyy"), FontAwesomeIcon.CalendarAlt))
        {
            this.calView = new DateTime(this.date.Year, this.date.Month, 1);
            ImGui.OpenPopup("##ec_datepop");
        }
        if (this.FieldBox(dl, "start", "Start", rp.X + x + half + Ui.Px(12f), rp.Y, half, To12H(this.hour, this.minute), FontAwesomeIcon.Clock))
            ImGui.OpenPopup("##ec_timepop");
        Block(rp, w, FieldBlock);

        // Harness (Vitrine): open a picker anchored to its real field, so a screenshot matches a click.
        if (this.pendingPopup is { } pp)
        {
            if (pp == "##ec_tzpop")
            {
                this.popupAnchor = new Vector2(rp.X + x, rp.Y + FieldBlock + Ui.Px(70f));
                this.popupWidth = w;
            }
            else
            {
                var isTime = pp == "##ec_timepop";
                this.popupAnchor = new Vector2(rp.X + x + (isTime ? half + Ui.Px(12f) : 0f), rp.Y + Ui.Px(70f));
                this.popupWidth = half;
            }

            this.calView = new DateTime(this.date.Year, this.date.Month, 1);
            ImGui.OpenPopup(pp);
            this.pendingPopup = null;
        }

        // Timezone.
        var tp = ImGui.GetCursorScreenPos();
        if (this.FieldBox(dl, "tz", "Timezone", tp.X + x, tp.Y, w, $"{Timezones[this.tz].Short} — {Timezones[this.tz].Label.Split('—').Last().Trim()}", FontAwesomeIcon.ChevronDown))
            ImGui.OpenPopup("##ec_tzpop");
        Block(tp, w, FieldBlock);

        // Hours / Minutes steppers.
        var sp = ImGui.GetCursorScreenPos();
        this.durHours = this.StepperBox(dl, "HOURS", sp.X + x, sp.Y, half, this.durHours, 0, 12);
        this.durMins = this.StepperBox(dl, "MINUTES", sp.X + x + half + Ui.Px(12f), sp.Y, half, this.durMins, 0, 45, step: 15);
        Block(sp, w, StepperBlock);

        // Repeats (only Once enabled for v1).
        this.Label(dl, "REPEATS", x);
        var qp = ImGui.GetCursorScreenPos();
        var opts = new[] { "ONCE", "WEEKLY", "2WK", "MONTHLY" };
        var enabled = new[] { true, false, false, false };
        var sel = this.Segmented(dl, "##ec_repeats", opts, 0, qp.X + x, qp.Y, w, Ui.Px(42f), enabled);
        if (sel == 0)
            this.recurrence = EventRecurrenceEnum.None;
        Block(qp, w, Ui.Px(42f) + Ui.Px(18f));
    }

    // ---- step 2: place -----------------------------------------------------------------------

    private void DrawPlace(float width, float pad)
    {
        var dl = ImGui.GetWindowDrawList();
        var x = pad;
        var w = width - (pad * 2f);
        var third = (w - (Ui.Px(10f) * 2f)) / 3f;
        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));

        // Harness (Vitrine): open a district/zone/landmark picker for a screenshot (world and the
        // ward/plot/room number fields anchor themselves below).
        if (this.pendingPopup is { } pp && pp != "##ec_worldpop"
            && pp != "##ec_wardpop" && pp != "##ec_plotpop" && pp != "##ec_roompop")
        {
            var wp = ImGui.GetWindowPos();
            this.popupAnchor = new Vector2(wp.X + pad, wp.Y + Ui.Px(200f));
            this.popupWidth = w;
            ImGui.OpenPopup(pp);
            this.pendingPopup = null;
        }

        this.Label(dl, "VENUE", x);
        var vp = ImGui.GetCursorScreenPos();
        var venueIdx = this.venue == EventVenueEnum.Housing ? 0 : this.venue == EventVenueEnum.OpenWorld ? 1 : 2;
        var vsel = this.Segmented(dl, "##ec_venue", new[] { "HOUSE", "OPEN", "DISCORD" }, venueIdx, vp.X + x, vp.Y, w, Ui.Px(42f), null);
        this.venue = vsel == 0 ? EventVenueEnum.Housing : vsel == 1 ? EventVenueEnum.OpenWorld : EventVenueEnum.Discord;
        Block(vp, w, Ui.Px(42f) + Ui.Px(24f));

        if (this.venue != EventVenueEnum.Discord)
        {
            if (this.venueWorldId == 0 && this.profile.Mine?.WorldId is { } wid)
                this.venueWorldId = (int)wid;
            var wfp = ImGui.GetCursorScreenPos();
            this.Field(dl, "World", x, w, () =>
            {
                var wp = ImGui.GetCursorScreenPos();
                if (this.FieldBox(dl, "world", string.Empty, wp.X, wp.Y, w, this.WorldLabel(), FontAwesomeIcon.ChevronDown, labelAbove: false))
                    ImGui.OpenPopup("##ec_worldpop");
                Block(wp, w, Ui.Px(44f));
            });
            if (this.pendingPopup == "##ec_worldpop")
            {
                this.popupAnchor = new Vector2(wfp.X + x, wfp.Y + Ui.Px(70f));
                this.popupWidth = w;
                ImGui.OpenPopup("##ec_worldpop");
                this.pendingPopup = null;
            }
        }

        if (this.venue == EventVenueEnum.Housing)
        {
            this.Field(dl, "District", x, w, () =>
            {
                var dp = ImGui.GetCursorScreenPos();
                if (this.FieldBox(dl, "district", string.Empty, dp.X, dp.Y, w, DistrictOptions[this.district].Label, FontAwesomeIcon.ChevronDown, labelAbove: false))
                    ImGui.OpenPopup("##ec_districtpop");
                Block(dp, w, Ui.Px(44f));
            });

            var gp = ImGui.GetCursorScreenPos();
            this.NumberField(dl, "ward", "WARD", gp.X + x, gp.Y, third, this.ward, "##ec_wardpop");
            this.NumberField(dl, "plot", "PLOT", gp.X + x + third + Ui.Px(10f), gp.Y, third, this.plot, "##ec_plotpop");
            this.NumberField(dl, "room", "ROOM", gp.X + x + (2f * (third + Ui.Px(10f))), gp.Y, third, this.room, "##ec_roompop");
            Block(gp, w, Ui.Px(22f) + Ui.Px(44f) + Ui.Px(16f));
            if (this.pendingPopup is "##ec_wardpop" or "##ec_plotpop" or "##ec_roompop")
            {
                var col = this.pendingPopup == "##ec_wardpop" ? 0 : this.pendingPopup == "##ec_plotpop" ? 1 : 2;
                this.popupAnchor = new Vector2(gp.X + x + (col * (third + Ui.Px(10f))), gp.Y + Ui.Px(70f));
                this.popupWidth = third;
                ImGui.OpenPopup(this.pendingPopup);
                this.pendingPopup = null;
            }

            var hp = ImGui.GetCursorScreenPos();
            Ui.TextWrappedAt(dl, this.fonts.Caption, new Vector2(hp.X + x, hp.Y), Palette.TextMuted.U32(), "Room 0 means the yard or main hall, no apartment number shown.", w);
            ImGui.Dummy(new Vector2(0f, Ui.Px(28f)));
        }
        else if (this.venue == EventVenueEnum.OpenWorld)
        {
            this.Field(dl, "Zone", x, w, () =>
            {
                var zp = ImGui.GetCursorScreenPos();
                var zoneName = this.catalog.Zones.Count > this.zoneIdx && this.zoneIdx >= 0 ? this.catalog.Zones[this.zoneIdx].Name : "Pick a zone";
                if (this.FieldBox(dl, "zone", string.Empty, zp.X, zp.Y, w, zoneName, FontAwesomeIcon.ChevronDown, labelAbove: false))
                    ImGui.OpenPopup("##ec_zonepop");
                Block(zp, w, Ui.Px(44f));
            });

            this.Field(dl, "Landmark (optional)", x, w, () =>
            {
                var ap = ImGui.GetCursorScreenPos();
                var aList = this.AetherytesForZone();
                var aName = aList.Count > this.aetheryteIdx && this.aetheryteIdx >= 0 ? aList[this.aetheryteIdx].Name : "None";
                if (this.FieldBox(dl, "aeth", string.Empty, ap.X, ap.Y, w, aName, FontAwesomeIcon.ChevronDown, labelAbove: false))
                    ImGui.OpenPopup("##ec_aethpop");
                Block(ap, w, Ui.Px(44f));
            });
        }
        else
        {
            this.Field(dl, "Invite link", x, w, () => { var u = this.discordUrl; this.kit.TextField("##ec_durl", ref u, "https://discord.gg/…", w); this.discordUrl = u; });
            this.Field(dl, "Channel note (optional)", x, w, () => { var n = this.discordNote; this.kit.TextField("##ec_dnote", ref n, "Prog voice", w); this.discordNote = n; });
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(20f)));
    }

    // ---- step 3: access ----------------------------------------------------------------------

    private void DrawAccess(float width, float pad)
    {
        var dl = ImGui.GetWindowDrawList();
        var x = pad;
        var w = width - (pad * 2f);
        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));

        // Scope is no longer host-chosen: an event is anchored on its venue world and the board's own
        // World/DC/Region tabs filter by proximity (like people discovery), so there is no picker here.
        this.Label(dl, "RATING", x);
        var rp = ImGui.GetCursorScreenPos();
        var rsel = this.Segmented(dl, "##ec_rating", new[] { "SFW", "AFTER DARK 18+" }, this.rating == EventRatingEnum.Sfw ? 0 : 1, rp.X + x, rp.Y, w, Ui.Px(42f), null);
        this.rating = rsel == 0 ? EventRatingEnum.Sfw : EventRatingEnum.Ad;
        Block(rp, w, Ui.Px(42f));
        if (this.rating == EventRatingEnum.Ad)
        {
            var note = "After dark events are hidden from anyone with 18+ content switched off.";
            var hp = ImGui.GetCursorScreenPos();
            Ui.TextWrappedAt(dl, this.fonts.Caption, new Vector2(hp.X + x, hp.Y + Ui.Px(8f)), Palette.TextMuted.U32(), note, w);
            ImGui.Dummy(new Vector2(0f, Ui.Px(8f) + Ui.MeasureWrapped(this.fonts.Caption, note, w).Y + Ui.Px(20f)));
        }
        else
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(24f)));
        }

        this.Label(dl, "VISIBILITY", x);
        var pp = ImGui.GetCursorScreenPos();
        var vsel = this.Segmented(dl, "##ec_vis", new[] { "PUBLIC", "PRIVATE" }, this.visibility == Visibility.Public ? 0 : 1, pp.X + x, pp.Y, w, Ui.Px(42f), null);
        this.visibility = vsel == 0 ? Visibility.Public : Visibility.Private;
        Block(pp, w, Ui.Px(42f) + Ui.Px(24f));

        if (this.visibility == Visibility.Private)
        {
            this.Label(dl, "ENTRY CODE", x);
            var cp = ImGui.GetCursorScreenPos();
            var boxH = Ui.Px(46f);
            var bx = cp.X + x;
            dl.AddRect(new Vector2(bx, cp.Y), new Vector2(bx + w, cp.Y + boxH), Palette.BorderStrong.U32(), 0f, ImDrawFlags.None, 1f);
            Ui.TextAt(dl, this.fonts.EventTitle, new Vector2(bx + Ui.Px(14f), cp.Y + ((boxH - Ui.Measure(this.fonts.EventTitle, this.code).Y) * 0.5f)), Palette.TextPrimary.U32(), this.code);

            // NEW regenerates; COPY puts the code on the clipboard and confirms briefly.
            var newSz = Ui.Measure(this.fonts.Eyebrow, "NEW");
            var newX = bx + w - newSz.X - Ui.Px(14f);
            var actY = cp.Y + ((boxH - newSz.Y) * 0.5f);
            ImGui.SetCursorScreenPos(new Vector2(newX, actY));
            if (ImGui.InvisibleButton("##ec_newcode", newSz))
                this.code = GenerateCode();
            Ui.TextAt(dl, this.fonts.Eyebrow, ImGui.GetItemRectMin(), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32(), "NEW");

            var copied = (DateTime.UtcNow - this.codeCopiedAt).TotalSeconds < 1.5;
            var copyLabel = copied ? "COPIED" : "COPY";
            var copySz = Ui.Measure(this.fonts.Eyebrow, copyLabel);
            ImGui.SetCursorScreenPos(new Vector2(newX - Ui.Px(16f) - copySz.X, actY));
            if (ImGui.InvisibleButton("##ec_copycode", copySz))
            {
                ImGui.SetClipboardText(this.code);
                this.codeCopiedAt = DateTime.UtcNow;
            }

            Ui.TextAt(dl, this.fonts.Eyebrow, ImGui.GetItemRectMin(), (copied ? Palette.Signal : ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32(), copyLabel);
            Block(cp, w, boxH + Ui.Px(24f));
        }

        this.DrawCapacity(dl, x, w);
    }

    // CAPACITY: FFXIV-native sizes as one-tap chips, plus a slider for any exact value in between.
    private void DrawCapacity(ImDrawListPtr dl, float x, float w)
    {
        var lp = ImGui.GetCursorScreenPos();
        var readout = this.capacity > 0 ? this.capacity.ToString() : "No cap";
        var rSz = Ui.Measure(this.fonts.SerifName, readout);
        var eSz = Ui.Measure(this.fonts.Eyebrow, "CAPACITY");
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(lp.X + x, lp.Y + ((rSz.Y - eSz.Y) * 0.5f)), Palette.TextSecondary.U32(), "CAPACITY");
        Ui.TextAt(dl, this.fonts.SerifName, new Vector2(lp.X + x + w - rSz.X, lp.Y), Palette.TextPrimary.U32(), readout);
        ImGui.Dummy(new Vector2(0f, rSz.Y + Ui.Px(12f)));

        var presets = new[] { 0, 8, 16, 24, 32, 48, 64, 100, 200 };
        var cp = ImGui.GetCursorScreenPos();
        var chipH = Ui.Px(30f);
        var cx = cp.X + x;
        var cy = cp.Y;
        var rows = 1;
        foreach (var val in presets)
        {
            var label = val == 0 ? "No cap" : val.ToString();
            var ts = Ui.Measure(this.fonts.Label, label);
            var cw = ts.X + (Ui.Px(14f) * 2f);
            if (cx + cw > cp.X + x + w)
            {
                cx = cp.X + x;
                cy += chipH + Ui.Px(8f);
                rows++;
            }

            ImGui.SetCursorScreenPos(new Vector2(cx, cy));
            if (ImGui.InvisibleButton($"##ec_cap_{val}", new Vector2(cw, chipH)))
                this.capacity = val;
            var active = this.capacity == val;
            var min = new Vector2(cx, cy);
            if (active)
                dl.AddRectFilled(min, min + new Vector2(cw, chipH), Palette.TextPrimary.U32(), chipH * 0.5f);
            else
                dl.AddRect(min, min + new Vector2(cw, chipH), Palette.Border.U32(), chipH * 0.5f, ImDrawFlags.None, 1f);
            Ui.TextAt(dl, this.fonts.Label, new Vector2(cx + Ui.Px(14f), cy + ((chipH - ts.Y) * 0.5f)), (active ? Palette.Paper : Palette.TextSecondary).U32(), label);
            cx += cw + Ui.Px(8f);
        }

        Block(cp, w, (rows * chipH) + ((rows - 1) * Ui.Px(8f)) + Ui.Px(18f));

        var sp = ImGui.GetCursorScreenPos();
        this.capacity = this.Slider(dl, "##ec_capslider", sp.X + x, sp.Y, w, this.capacity, 0, 200);
        Block(sp, w, Ui.Px(22f));
    }

    // A draw-list slider: track, filled portion, and a round handle you drag or click to set a value.
    private int Slider(ImDrawListPtr dl, string id, float x, float y, float w, int value, int min, int max)
    {
        var h = Ui.Px(22f);
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        ImGui.InvisibleButton(id, new Vector2(w, h));
        if (ImGui.IsItemActive() && w > 0f)
        {
            var t = Math.Clamp((ImGui.GetMousePos().X - x) / w, 0f, 1f);
            value = min + (int)MathF.Round(t * (max - min));
        }

        var trackY = y + (h * 0.5f);
        var frac = max > min ? (float)(value - min) / (max - min) : 0f;
        var hx = x + (frac * w);
        dl.AddLine(new Vector2(x, trackY), new Vector2(x + w, trackY), Palette.BorderStrong.U32(), Ui.Px(2f));
        dl.AddLine(new Vector2(x, trackY), new Vector2(hx, trackY), Palette.Signal.U32(), Ui.Px(2f));
        dl.AddCircleFilled(new Vector2(hx, trackY), Ui.Px(7f), Palette.TextPrimary.U32());
        return value;
    }

    // ---- shared field/stepper/segmented primitives -------------------------------------------

    private void Label(ImDrawListPtr dl, string label, float x)
    {
        var pos = ImGui.GetCursorScreenPos();
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(pos.X + x, pos.Y + Ui.Px(4f)), Palette.TextSecondary.U32(), label);
        ImGui.Dummy(new Vector2(0f, Ui.Px(24f)));
    }

    // A labelled control: draws the sentence-case label, then runs `content` (an ImGui input) at the pad.
    private void Field(ImDrawListPtr dl, string label, float x, float w, Action content)
    {
        var pos = ImGui.GetCursorScreenPos();
        Ui.TextAt(dl, this.fonts.Caption, new Vector2(pos.X + x, pos.Y), Palette.TextSecondary.U32(), label);
        ImGui.SetCursorScreenPos(new Vector2(pos.X + x, pos.Y + Ui.Px(22f)));
        content();
        ImGui.Dummy(new Vector2(0f, Ui.Px(18f)));
    }

    // A tappable bordered value box (date/time/timezone/dropdown), with an optional label above.
    private bool FieldBox(ImDrawListPtr dl, string id, string label, float x, float y, float w, string value, FontAwesomeIcon icon, bool labelAbove = true)
    {
        var top = y;
        if (labelAbove && label.Length > 0)
        {
            Ui.TextAt(dl, this.fonts.Caption, new Vector2(x, y), Palette.TextSecondary.U32(), label);
            top = y + Ui.Px(22f);
        }

        var h = Ui.Px(44f);
        ImGui.SetCursorScreenPos(new Vector2(x, top));
        var clicked = ImGui.InvisibleButton("##fb_" + id, new Vector2(w, h));
        if (clicked)
        {
            this.popupAnchor = new Vector2(x, top + h + Ui.Px(4f));
            this.popupWidth = w;
        }

        var hovered = ImGui.IsItemHovered();
        var min = new Vector2(x, top);
        var max = min + new Vector2(w, h);
        dl.AddRectFilled(min, max, Palette.Surface2.U32());
        dl.AddRect(min, max, (hovered ? Palette.BorderStrong : Palette.Border).U32(), 0f, ImDrawFlags.None, 1f);
        Ui.TextAt(dl, this.fonts.Label, new Vector2(x + Ui.Px(12f), top + ((h - Ui.Measure(this.fonts.Label, value).Y) * 0.5f)), Palette.TextPrimary.U32(), value);
        var g = icon.ToIconString();
        var gs = Ui.Measure(this.fonts.Icon, g);
        Ui.TextAt(dl, this.fonts.Icon, new Vector2(x + w - gs.X - Ui.Px(12f), top + ((h - gs.Y) * 0.5f)), Palette.TextMuted.U32(), g);
        return clicked;
    }

    // A small labelled numeric dropdown: an eyebrow label over a tappable box that opens a number list.
    private void NumberField(ImDrawListPtr dl, string id, string label, float x, float y, float w, int value, string popupId)
    {
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(x, y), Palette.TextSecondary.U32(), label);
        if (this.FieldBox(dl, id, string.Empty, x, y + Ui.Px(22f), w, value.ToString(), FontAwesomeIcon.ChevronDown, labelAbove: false))
            ImGui.OpenPopup(popupId);
    }

    // A labelled numeric stepper box (minus / serif value / plus).
    private int StepperBox(ImDrawListPtr dl, string label, float x, float y, float w, int value, int min, int max, int step = 1, bool labelInside = true)
    {
        var h = labelInside && label.Length > 0 ? Ui.Px(64f) : Ui.Px(46f);
        var boxMin = new Vector2(x, y);
        var boxMax = boxMin + new Vector2(w, h);
        dl.AddRect(boxMin, boxMax, Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);
        var rowY = y;
        if (labelInside && label.Length > 0)
        {
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(x + Ui.Px(12f), y + Ui.Px(10f)), Palette.TextSecondary.U32(), label);
            rowY = y + Ui.Px(24f);
        }

        var mid = rowY + ((h - (rowY - y)) * 0.5f);
        var minus = FontAwesomeIcon.Minus.ToIconString();
        var plus = FontAwesomeIcon.Plus.ToIconString();
        var ms = Ui.Measure(this.fonts.Icon, minus);
        ImGui.SetCursorScreenPos(new Vector2(x + Ui.Px(12f), mid - (ms.Y * 0.5f)));
        if (ImGui.InvisibleButton($"##sb_minus_{label}{x}", ms) && value > min)
            value = Math.Max(min, value - step);
        Ui.TextAt(dl, this.fonts.Icon, ImGui.GetItemRectMin(), (value > min ? Palette.TextSecondary : Palette.TextMuted).U32(), minus);

        var ps = Ui.Measure(this.fonts.Icon, plus);
        ImGui.SetCursorScreenPos(new Vector2(x + w - Ui.Px(12f) - ps.X, mid - (ps.Y * 0.5f)));
        if (ImGui.InvisibleButton($"##sb_plus_{label}{x}", ps) && value < max)
            value = Math.Min(max, value + step);
        Ui.TextAt(dl, this.fonts.Icon, ImGui.GetItemRectMin(), (value < max ? Palette.TextSecondary : Palette.TextMuted).U32(), plus);

        var num = value.ToString();
        var nsz = Ui.Measure(this.fonts.SerifName, num);
        Ui.TextAt(dl, this.fonts.SerifName, new Vector2(x + ((w - nsz.X) * 0.5f), mid - (nsz.Y * 0.5f)), Palette.TextPrimary.U32(), num);
        return value;
    }

    // Cream-active square segmented control, with an optional per-cell enabled mask (disabled = muted).
    private int Segmented(ImDrawListPtr dl, string id, string[] options, int selected, float x, float y, float w, float h, bool[]? enabled)
    {
        var cellW = w / options.Length;
        dl.AddRect(new Vector2(x, y), new Vector2(x + w, y + h), Palette.BorderStrong.U32(), 0f, ImDrawFlags.None, 1f);
        var result = selected;
        for (var i = 0; i < options.Length; i++)
        {
            var cx = x + (cellW * i);
            var on = enabled == null || enabled[i];
            ImGui.SetCursorScreenPos(new Vector2(cx, y));
            if (ImGui.InvisibleButton($"{id}_{i}", new Vector2(cellW, h)) && on)
                result = i;
            var active = i == selected;
            if (active)
                dl.AddRectFilled(new Vector2(cx + Ui.Px(2f), y + Ui.Px(2f)), new Vector2(cx + cellW - Ui.Px(2f), y + h - Ui.Px(2f)), Palette.TextPrimary.U32(), 0f);
            else if (i > 0)
                dl.AddLine(new Vector2(cx, y + Ui.Px(9f)), new Vector2(cx, y + h - Ui.Px(9f)), Palette.Border.U32(), 1f);
            var col = active ? Palette.Paper : (on ? Palette.TextSecondary : Palette.TextMuted);
            var ts = Ui.Measure(this.fonts.Eyebrow, options[i]);
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(cx + ((cellW - ts.X) * 0.5f), y + ((h - ts.Y) * 0.5f)), col.U32(), options[i]);
        }

        return result;
    }

    private bool OutlineButton(ImDrawListPtr dl, string id, string label, float x, float y, float w, float h)
    {
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
        var hovered = ImGui.IsItemHovered();
        dl.AddRect(new Vector2(x, y), new Vector2(x + w, y + h), (hovered ? Palette.BorderStrong : Palette.Border).U32(), 0f, ImDrawFlags.None, 1f);
        var ts = Ui.Measure(this.fonts.Eyebrow, label);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(x + ((w - ts.X) * 0.5f), y + ((h - ts.Y) * 0.5f)), (hovered ? Palette.TextPrimary : Palette.TextSecondary).U32(), label);
        return clicked;
    }

    private bool FilledButton(ImDrawListPtr dl, string id, string label, float x, float y, float w, float h, bool enabled)
    {
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
        dl.AddRectFilled(new Vector2(x, y), new Vector2(x + w, y + h), (enabled ? Palette.TextPrimary : Palette.WithAlpha(Palette.TextPrimary, 0.3f)).U32());
        var ts = Ui.Measure(this.fonts.Eyebrow, label);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(x + ((w - ts.X) * 0.5f), y + ((h - ts.Y) * 0.5f)), Palette.Paper.U32(), label);
        return clicked;
    }

    // A tappable cell (calendar day / time value): cream fill when selected, hairline when idle.
    private bool Cell(ImDrawListPtr dl, string id, string text, float x, float y, float w, float h, bool selected, IFontHandle? font, bool enabled = true, bool border = true)
    {
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        var clicked = enabled && ImGui.InvisibleButton(id, new Vector2(w, h));
        var hovered = enabled && ImGui.IsItemHovered();
        var min = new Vector2(x, y);
        var max = min + new Vector2(w, h);
        if (selected)
            dl.AddRectFilled(min, max, Palette.TextPrimary.U32());
        else if (hovered)
            dl.AddRectFilled(min, max, Palette.WithAlpha(Palette.Overlay, 0.05f).U32());
        if (border && !selected)
            dl.AddRect(min, max, (hovered ? Palette.BorderStrong : Palette.Border).U32(), 0f, ImDrawFlags.None, 1f);
        var col = selected ? Palette.Paper : (enabled ? Palette.TextPrimary : Palette.TextMuted);
        var ts = Ui.Measure(font, text);
        Ui.TextAt(dl, font, new Vector2(x + ((w - ts.X) * 0.5f), y + ((h - ts.Y) * 0.5f)), col.U32(), text);
        return clicked;
    }

    // A bordered square icon button (calendar month prev / next).
    private bool NavButton(ImDrawListPtr dl, string id, FontAwesomeIcon icon, float x, float y, float w, float h)
    {
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
        var hovered = ImGui.IsItemHovered();
        dl.AddRect(new Vector2(x, y), new Vector2(x + w, y + h), (hovered ? Palette.BorderStrong : Palette.Border).U32(), 0f, ImDrawFlags.None, 1f);
        var g = icon.ToIconString();
        var gs = Ui.Measure(this.fonts.Icon, g);
        Ui.TextAt(dl, this.fonts.Icon, new Vector2(x + ((w - gs.X) * 0.5f), y + ((h - gs.Y) * 0.5f)), (hovered ? Palette.TextPrimary : Palette.TextSecondary).U32(), g);
        return clicked;
    }

    // ---- popups (date / time / timezone / district / zone / aetheryte) ------------------------

    private void DrawPopups(float pad)
    {
        this.ListPopup("##ec_tzpop", Timezones.Select(t => $"{t.Short}  {t.Label}").ToArray(), this.tz, i => this.tz = i);
        this.ListPopup("##ec_districtpop", DistrictOptions.Select(d => d.Label).ToArray(), this.district, i => this.district = i);
        this.ListPopup("##ec_wardpop", WardNums, this.ward - 1, i => this.ward = i + 1);
        this.ListPopup("##ec_plotpop", PlotNums, this.plot - 1, i => this.plot = i + 1);
        this.ListPopup("##ec_roompop", RoomNums, this.room, i => this.room = i);
        this.ListPopup("##ec_zonepop", this.catalog.Zones.Select(z => z.Name).ToArray(), this.zoneIdx, i => { this.zoneIdx = i; this.aetheryteIdx = 0; });
        var aeth = this.AetherytesForZone();
        this.ListPopup("##ec_aethpop", aeth.Select(a => a.Name).Prepend("None").ToArray(), this.aetheryteIdx + 1, i => this.aetheryteIdx = i - 1);
        this.WorldPopup();
        this.DatePopup();
        this.TimePopup();
    }

    // World picker: all worlds grouped by region then data center, host's world pre-selected.
    private void WorldPopup()
    {
        var winW = Math.Max(Ui.Px(200f), this.popupWidth);   // match the field's own width
        ImGui.SetNextWindowPos(this.PopupPos(winW), ImGuiCond.Appearing);
        ImGui.SetNextWindowSizeConstraints(new Vector2(winW, 0f), new Vector2(winW, this.PopupMaxHeight()));
        using (this.MenuStyle())
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
        {
            if (!ImGui.BeginPopup("##ec_worldpop"))
                return;
            var rowW = ImGui.GetContentRegionAvail().X;
            var dl = ImGui.GetWindowDrawList();
            foreach (var region in new[] { RegionEnum.Na, RegionEnum.Eu, RegionEnum.Jp, RegionEnum.Oce })
                foreach (var dc in this.worlds.DataCenters.Where(d => d.Region == region.ToString()))
                {
                    var hp = ImGui.GetCursorScreenPos();
                    var headerH = Ui.Px(24f);
                    var header = $"{RegionLabel(region)} · {dc.Name.ToUpperInvariant()}";
                    Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(hp.X + Ui.Px(10f), hp.Y + ((headerH - Ui.Measure(this.fonts.Eyebrow, header).Y) * 0.5f)), Palette.TextMuted.U32(), header);
                    ImGui.Dummy(new Vector2(rowW, headerH));
                    foreach (var wld in dc.Worlds)
                        if (this.MenuItem(rowW, wld.Name, (int)wld.Id == this.venueWorldId))
                        {
                            this.venueWorldId = (int)wld.Id;
                            ImGui.CloseCurrentPopup();
                        }
                }

            ImGui.EndPopup();
        }
    }

    // Position a popup just below the field that opened it. Left-align under the field when the popup
    // fits; otherwise hang it from the field's right edge (a right-aligned dropdown). Clamp to screen.
    // Cap a scrolling popup so it fits between its anchor and the footer, never spilling over the buttons.
    private float PopupMaxHeight() =>
        Math.Clamp(this.popupBottomLimit - this.popupAnchor.Y, Ui.Px(140f), Ui.Px(360f));

    private Vector2 PopupPos(float popupW)
    {
        var minX = this.windowLeft + Ui.Px(8f);
        var maxX = Math.Max(minX, this.windowRight - popupW - Ui.Px(8f));
        var fieldLeft = this.popupAnchor.X;
        var px = fieldLeft + popupW <= this.windowRight - Ui.Px(8f) ? fieldLeft : (fieldLeft + this.popupWidth) - popupW;
        return new Vector2(Math.Clamp(px, minX, maxX), this.popupAnchor.Y);
    }

    private void ListPopup(string id, string[] items, int selected, Action<int> onPick)
    {
        var winW = Math.Max(Ui.Px(80f), this.popupWidth);   // match the field's own width (narrow for numbers)
        ImGui.SetNextWindowPos(this.PopupPos(winW), ImGuiCond.Appearing);
        // Fix the width and cap the height to the room above the footer so a long list scrolls without
        // spilling over the buttons, instead of nesting a child that collapses in an auto-resize popup.
        ImGui.SetNextWindowSizeConstraints(new Vector2(winW, 0f), new Vector2(winW, this.PopupMaxHeight()));
        using (this.MenuStyle())
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
        {
            if (!ImGui.BeginPopup(id))
                return;
            var rowW = ImGui.GetContentRegionAvail().X;
            for (var i = 0; i < items.Length; i++)
            {
                if (this.MenuItem(rowW, items[i], i == selected))
                {
                    onPick(i);
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.EndPopup();
        }
    }

    // Month calendar: prev/next roll the year at the boundaries, past days are disabled, the selected day
    // is a cream fill and today gets a gold ring. A Today chip jumps back and selects the current day.
    private void DatePopup()
    {
        var popupW = Ui.Px(300f);
        ImGui.SetNextWindowPos(this.PopupPos(popupW), ImGuiCond.Appearing);
        using (this.MenuStyle())
        {
            if (!ImGui.BeginPopup("##ec_datepop"))
                return;
            var dl = ImGui.GetWindowDrawList();
            var inner = popupW - (Ui.Px(8f) * 2f);
            var p = ImGui.GetCursorScreenPos();

            var headerH = Ui.Px(34f);
            var navW = Ui.Px(30f);
            if (this.NavButton(dl, "##cal_prev", FontAwesomeIcon.ChevronLeft, p.X, p.Y, navW, headerH))
                this.calView = this.calView.AddMonths(-1);
            if (this.NavButton(dl, "##cal_next", FontAwesomeIcon.ChevronRight, p.X + inner - navW, p.Y, navW, headerH))
                this.calView = this.calView.AddMonths(1);
            var monthStr = this.calView.ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture);
            var yearStr = " " + this.calView.Year;
            var mSz = Ui.Measure(this.fonts.SerifName, monthStr);
            var ySz = Ui.Measure(this.fonts.SerifName, yearStr);
            var hx = p.X + ((inner - (mSz.X + ySz.X)) * 0.5f);
            var hy = p.Y + ((headerH - mSz.Y) * 0.5f);
            Ui.TextAt(dl, this.fonts.SerifName, new Vector2(hx, hy), Palette.TextPrimary.U32(), monthStr);
            Ui.TextAt(dl, this.fonts.SerifName, new Vector2(hx + mSz.X, hy), Palette.TextSecondary.U32(), yearStr);

            var cellW = inner / 7f;
            var wdY = p.Y + headerH;
            var wdH = Ui.Px(22f);
            var wd = new[] { "M", "T", "W", "T", "F", "S", "S" };
            for (var i = 0; i < 7; i++)
            {
                var ts = Ui.Measure(this.fonts.Eyebrow, wd[i]);
                Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(p.X + (i * cellW) + ((cellW - ts.X) * 0.5f), wdY + ((wdH - ts.Y) * 0.5f)), Palette.TextMuted.U32(), wd[i]);
            }

            var gridTop = wdY + wdH;
            var cellH = Ui.Px(34f);
            var first = new DateTime(this.calView.Year, this.calView.Month, 1);
            var lead = ((int)first.DayOfWeek + 6) % 7;   // Monday-start offset
            var days = DateTime.DaysInMonth(this.calView.Year, this.calView.Month);
            var rows = (int)Math.Ceiling((lead + days) / 7f);
            var today = DateTime.Today;
            for (var d = 1; d <= days; d++)
            {
                var idx = lead + (d - 1);
                var cx = p.X + ((idx % 7) * cellW);
                var cy = gridTop + ((idx / 7) * cellH);
                var cellDate = new DateTime(this.calView.Year, this.calView.Month, d);
                var selected = cellDate == this.date.Date;
                var past = cellDate < today;
                if (this.Cell(dl, $"##cal_{d}", d.ToString(), cx, cy, cellW, cellH, selected, this.fonts.SerifName, enabled: !past, border: false))
                {
                    this.date = cellDate;
                    ImGui.CloseCurrentPopup();
                }

                if (cellDate == today && !selected)
                    dl.AddRect(new Vector2(cx + Ui.Px(3f), cy + Ui.Px(3f)), new Vector2(cx + cellW - Ui.Px(3f), cy + cellH - Ui.Px(3f)), Palette.Signal.U32(), 0f, ImDrawFlags.None, 1f);
            }

            var footY = gridTop + (rows * cellH) + Ui.Px(10f);
            var chipH = Ui.Px(26f);
            if (this.OutlineButton(dl, "##cal_today", "TODAY", p.X, footY, Ui.Px(76f), chipH))
            {
                this.date = today;
                this.calView = new DateTime(today.Year, today.Month, 1);
                ImGui.CloseCurrentPopup();
            }

            Block(p, inner, (footY - p.Y) + chipH);
            ImGui.EndPopup();
        }
    }

    // Time as a tap grid: one tap each for hour, minute (15s), and AM/PM. The value is kept in 24h.
    private void TimePopup()
    {
        var popupW = Ui.Px(300f);
        ImGui.SetNextWindowPos(this.PopupPos(popupW), ImGuiCond.Appearing);
        using (this.MenuStyle())
        {
            if (!ImGui.BeginPopup("##ec_timepop"))
                return;
            var dl = ImGui.GetWindowDrawList();
            var inner = popupW - (Ui.Px(8f) * 2f);
            var gap = Ui.Px(4f);
            var p = ImGui.GetCursorScreenPos();
            var h12 = this.hour % 12 == 0 ? 12 : this.hour % 12;
            var isPm = this.hour >= 12;
            var snapMin = (this.minute / 15) * 15;
            var cellH = Ui.Px(38f);
            var quarter = (inner - (gap * 3f)) / 4f;

            var y = p.Y;
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(p.X, y), Palette.TextSecondary.U32(), "HOUR");
            y += Ui.Px(20f);
            for (var i = 0; i < 12; i++)
            {
                var hh = i + 1;
                var cx = p.X + ((i % 4) * (quarter + gap));
                var cy = y + ((i / 4) * (cellH + gap));
                if (this.Cell(dl, $"##th_{hh}", hh.ToString(), cx, cy, quarter, cellH, hh == h12, this.fonts.SerifName))
                    this.hour = (hh % 12) + (isPm ? 12 : 0);
            }

            y += (3 * (cellH + gap)) + Ui.Px(12f);
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(p.X, y), Palette.TextSecondary.U32(), "MINUTE");
            y += Ui.Px(20f);
            var mins = new[] { 0, 15, 30, 45 };
            for (var i = 0; i < 4; i++)
            {
                var cx = p.X + (i * (quarter + gap));
                if (this.Cell(dl, $"##tm_{mins[i]}", mins[i].ToString("00"), cx, y, quarter, cellH, mins[i] == snapMin, this.fonts.SerifName))
                    this.minute = mins[i];
            }

            y += cellH + Ui.Px(12f);
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(p.X, y), Palette.TextSecondary.U32(), "AM / PM");
            y += Ui.Px(20f);
            var apW = (inner - gap) * 0.5f;
            if (this.Cell(dl, "##t_am", "AM", p.X, y, apW, cellH, !isPm, this.fonts.SerifName))
                this.hour = h12 % 12;
            if (this.Cell(dl, "##t_pm", "PM", p.X + apW + gap, y, apW, cellH, isPm, this.fonts.SerifName))
                this.hour = (h12 % 12) + 12;
            y += cellH;

            Block(p, inner, y - p.Y);
            ImGui.EndPopup();
        }
    }

    private bool MenuItem(float width, string label, bool selected)
    {
        var height = Ui.Px(32f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##mi_" + label, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        if (hovered || selected)
            dl.AddRectFilled(pos, pos + new Vector2(width, height), Palette.WithAlpha(Palette.Overlay, 0.05f).U32());
        Ui.TextAt(dl, this.fonts.Label, new Vector2(pos.X + Ui.Px(10f), pos.Y + ((height - Ui.Measure(this.fonts.Label, label).Y) * 0.5f)), (selected ? Palette.TextPrimary : Palette.TextSecondary).U32(), label);
        return clicked;
    }

    // ---- submit + helpers ---------------------------------------------------------------------

    private bool StepValid() => this.step switch
    {
        0 => this.title.Trim().Length > 2,
        1 => (this.durHours * 60) + this.durMins >= 15,
        2 => this.venue switch
        {
            EventVenueEnum.Housing => DistrictOptions.Length > 0,
            EventVenueEnum.OpenWorld => this.catalog.Zones.Count > 0,
            _ => this.discordUrl.Trim().Length > 4,
        },
        _ => true,
    };

    private void Submit()
    {
        if (this.publishing)
            return;
        var venue = new EventVenueDto { Type = this.venue };
        if (this.venue != EventVenueEnum.Discord && this.venueWorldId > 0)
            venue.WorldId = this.venueWorldId;
        switch (this.venue)
        {
            case EventVenueEnum.Housing:
                venue.District = DistrictOptions[this.district].Value;
                venue.Ward = this.ward; venue.Plot = this.plot; venue.Room = this.room;
                break;
            case EventVenueEnum.OpenWorld:
                if (this.catalog.Zones.Count > this.zoneIdx)
                    venue.ZoneId = this.catalog.Zones[this.zoneIdx].Id;
                var aeth = this.AetherytesForZone();
                if (this.aetheryteIdx >= 0 && aeth.Count > this.aetheryteIdx)
                    venue.AetheryteId = aeth[this.aetheryteIdx].Id;
                break;
            default:
                venue.DiscordUrl = this.discordUrl.Trim();
                if (this.discordNote.Trim().Length > 0)
                    venue.DiscordNote = this.discordNote.Trim();
                break;
        }

        var (startsAt, iana, label) = this.ResolveStart();
        var tags = this.tagsText.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

        var req = new CreateEventRequest
        {
            Title = this.title.Trim(), Kind = this.kind, Scope = this.scope, Rating = this.rating, Visibility = this.visibility,
            StartsAt = startsAt, HostClock = $"{this.hour:00}:{this.minute:00}", HostTz = iana, HostTzLabel = label,
            // startsAt above is a DateTimeOffset from ResolveStart.
            DurationMins = (this.durHours * 60) + this.durMins, Recurrence = EventRecurrenceEnum.None, Venue = venue,
            Description = this.description.Trim(), Tags = tags,
            Capacity = this.capacity > 0 ? this.capacity : null,
        };
        if (this.uploadBytes != null)
        {
            req.BannerBase64 = Convert.ToBase64String(this.uploadBytes);
            req.BannerContentType = ContentType.ImageJpeg;
        }
        else
        {
            req.BannerPreset = this.bannerPreset;
        }

        if (this.visibility == Visibility.Private)
            req.EntryCode = this.code;

        this.publishing = true;
        this.submitError = null;
        _ = this.Publish(req);
    }

    private async System.Threading.Tasks.Task Publish(CreateEventRequest req)
    {
        try
        {
            var created = await this.events.CreateAsync(req);
            if (created != null)
            {
                this.selection.EventId = created.Id;
                this.selection.EventReturn = Screen.Grid;
                this.router.Navigate(Screen.EventDetail);
            }
            else
            {
                this.submitError = "Couldn't publish. Check your connection and try again.";
            }
        }
        finally
        {
            this.publishing = false;
        }
    }

    private (DateTimeOffset StartsAt, string Iana, string Label) ResolveStart()
    {
        var (label, _, iana) = Timezones[this.tz];
        try
        {
            var tzInfo = TimeZoneInfo.FindSystemTimeZoneById(iana);
            var wall = new DateTime(this.date.Year, this.date.Month, this.date.Day, this.hour, this.minute, 0, DateTimeKind.Unspecified);
            var utc = TimeZoneInfo.ConvertTimeToUtc(wall, tzInfo);
            return (new DateTimeOffset(utc, TimeSpan.Zero), iana, label);
        }
        catch (Exception)
        {
            var wall = new DateTime(this.date.Year, this.date.Month, this.date.Day, this.hour, this.minute, 0, DateTimeKind.Utc);
            return (new DateTimeOffset(wall, TimeSpan.Zero), iana, label);
        }
    }

    private void PickBanner() =>
        this.media.PickImage(path =>
        {
            try
            {
                var bytes = ImageCrop.ToJpeg(path, 3f, 1f, 0.5f, 0.5f);
                this.uploadBytes = bytes;
                this.uploadPreview?.Dispose();
                this.uploadPreview = Plugin.TextureProvider.CreateFromImageAsync(bytes).GetAwaiter().GetResult();
            }
            catch
            {
                // ignore a bad image; the preset stays selected
            }
        });

    private string WorldLabel()
    {
        foreach (var dc in this.worlds.DataCenters)
            foreach (var w in dc.Worlds)
                if ((int)w.Id == this.venueWorldId)
                    return $"{w.Name} · {dc.Name}";
        return "Pick a world";
    }

    private static string RegionLabel(RegionEnum region) => region switch
    {
        RegionEnum.Na => "NA",
        RegionEnum.Eu => "EU",
        RegionEnum.Jp => "JP",
        _ => "OCE",
    };

    private List<EventCatalog.Aetheryte> AetherytesForZone()
    {
        if (this.catalog.Zones.Count <= this.zoneIdx || this.zoneIdx < 0)
            return new List<EventCatalog.Aetheryte>();
        return this.catalog.AetherytesInZone(this.catalog.Zones[this.zoneIdx].Id).ToList();
    }

    private static string To12H(int hour, int minute)
    {
        var ampm = hour >= 12 ? "PM" : "AM";
        var h12 = hour % 12;
        if (h12 == 0)
            h12 = 12;
        return $"{h12:00}:{minute:00} {ampm}";
    }

    private static string GenerateCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(6);
        var s = new char[6];
        for (var i = 0; i < 6; i++)
            s[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
        return new string(s);
    }

    private IDisposable MenuStyle() => new Composite(new List<IDisposable>
    {
        ImRaii.PushColor(ImGuiCol.PopupBg, Palette.Surface1),
        ImRaii.PushColor(ImGuiCol.Border, Palette.Border),
        ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(Ui.Px(8f), Ui.Px(8f))),
        ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 0f),
        ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f),
    });

    public void Dispose() => this.uploadPreview?.Dispose();

    private sealed class Composite : IDisposable
    {
        private readonly List<IDisposable> items;

        public Composite(List<IDisposable> items) => this.items = items;

        public void Dispose()
        {
            for (var i = this.items.Count - 1; i >= 0; i--)
                this.items[i].Dispose();
        }
    }
}
