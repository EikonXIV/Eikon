using System;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures.TextureWraps;
using Eikon.Contracts;
using Eikon.Navigation;
using Eikon.Net;
using Eikon.UI;
using Eikon.UI.Theme;

namespace Eikon.Screens;

// Event detail (mockups 08/09). Full-bleed banner, kind + serif title + host line, the Doors/Attending
// block, labelled meta rows (Runs, Your time, location, Scope, and the owner-only Entry code), the
// description with tag pills, and the Save/Share/Copy-location footer with "spots left". A private event
// the viewer hasn't unlocked shows an inline code gate first. The overflow menu differs by role.
internal sealed class EventDetailScreen : IScreen
{
    private readonly ScreenRouter router;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly EventService events;
    private readonly EventCatalog catalog;
    private readonly WorldCatalog worlds;
    private readonly Selection selection;
    private readonly ModerationFlow moderation;

    private string codeInput = string.Empty;
    private int codeTries;
    private DateTime lockoutUntil;
    private bool confirmDelete;

    public EventDetailScreen(ScreenRouter router, Kit kit, UiFonts fonts, EventService events, EventCatalog catalog, WorldCatalog worlds, Selection selection, ModerationFlow moderation)
    {
        this.router = router;
        this.kit = kit;
        this.fonts = fonts;
        this.events = events;
        this.catalog = catalog;
        this.worlds = worlds;
        this.selection = selection;
        this.moderation = moderation;
    }

    public Screen Id => Screen.EventDetail;

    public bool Chrome => false;

    public void Draw()
    {
        if (this.selection.EventId is not { } eventId)
        {
            this.router.Navigate(this.selection.EventReturn);
            return;
        }

        this.catalog.EnsureLoaded();
        this.worlds.EnsureLoaded();
        var detail = this.events.Detail(eventId);

        var avail = ImGui.GetContentRegionAvail();
        var pad = Ui.Px(20f);
        var headerHeight = Ui.Px(52f);
        this.DrawHeader(avail.X, pad, headerHeight, detail);

        ImGui.SetCursorPos(new Vector2(0f, headerHeight));
        using var noPad = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var body = ImRaii.Child("event_detail_body", new Vector2(avail.X, avail.Y - headerHeight), false, ImGuiWindowFlags.AlwaysVerticalScrollbar);
        if (!body.Success)
            return;
        var width = ImGui.GetContentRegionAvail().X;

        if (detail != null)
            this.DrawDetail(detail, width, pad, eventId);
        else if (this.events.IsGated(eventId))
            this.DrawCodeGate(width, pad, eventId);
        else
            this.DrawStatus(width, "Loading…");

        this.DrawMenu(detail, eventId);
        this.moderation.Draw();
        this.DrawDeleteDialog(eventId);
    }

    private void DrawHeader(float fullWidth, float pad, float height, EventDto? detail)
    {
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var midY = origin.Y + (height * 0.5f);

        var back = FontAwesomeIcon.ChevronLeft.ToIconString();
        var backSize = Ui.Measure(this.fonts.Icon, back);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, midY - (backSize.Y * 0.5f)));
        if (ImGui.InvisibleButton("##ed_back", backSize))
            this.router.Navigate(this.selection.EventReturn);
        Ui.TextAt(dl, this.fonts.Icon, ImGui.GetItemRectMin(), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32(), back);

        var title = detail is { Hosting: true } ? "YOUR EVENT" : "EVENT";
        var titleSize = Ui.Measure(this.fonts.Eyebrow, title);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(origin.X + ((fullWidth - titleSize.X) * 0.5f), midY - (titleSize.Y * 0.5f)), Palette.TextSecondary.U32(), title);

        // Overflow only when the event is open to the viewer (an accessible detail exists).
        if (detail != null)
        {
            var dots = FontAwesomeIcon.EllipsisV.ToIconString();
            var ds = Ui.Measure(this.fonts.Icon, dots);
            ImGui.SetCursorScreenPos(new Vector2((origin.X + fullWidth - pad) - ds.X, midY - (ds.Y * 0.5f)));
            if (ImGui.InvisibleButton("##ed_more", ds))
                ImGui.OpenPopup("##ed_menu");
            Ui.TextAt(dl, this.fonts.Icon, ImGui.GetItemRectMin(), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32(), dots);
        }

        dl.AddLine(new Vector2(origin.X, origin.Y + height), new Vector2(origin.X + fullWidth, origin.Y + height), Palette.Border.U32(), 1f);
    }

    private void DrawDetail(EventDto e, float width, float pad, Guid eventId)
    {
        var dl = ImGui.GetWindowDrawList();
        var left = pad;
        var right = width - pad;
        var contentW = right - left;

        // Full-bleed banner.
        var bannerH = width / 3f;
        var bannerPos = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(bannerPos, bannerPos + new Vector2(width, bannerH), Palette.Surface2.U32());
        var banner = this.events.BannerFor(e.Id, e.BannerUploaded, e.BannerPreset);
        if (banner != null)
        {
            var (uvMin, uvMax) = Ui.CoverUv(banner.Width, banner.Height, width / bannerH);
            dl.AddImage(banner.Handle, bannerPos, bannerPos + new Vector2(width, bannerH), uvMin, uvMax);
        }

        // Hairline under the banner, separating it from the content.
        dl.AddLine(new Vector2(bannerPos.X, bannerPos.Y + bannerH), new Vector2(bannerPos.X + width, bannerPos.Y + bannerH), Palette.Border.U32(), 1f);
        ImGui.Dummy(new Vector2(width, bannerH));

        // Masthead: kind eyebrow + badges, serif title, host line.
        var mp = ImGui.GetCursorScreenPos();
        var y = mp.Y + Ui.Px(18f);
        var eyebrow = KindLabel(e.Kind).ToUpperInvariant();
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(mp.X + left, y), Palette.TextSecondary.U32(), eyebrow);
        var bx = mp.X + left + Ui.Measure(this.fonts.Eyebrow, eyebrow).X + Ui.Px(10f);
        if (e.Rating == EventRatingEnum.Ad)
        {
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(bx, y), Palette.Danger.U32(), "18+");
            bx += Ui.Measure(this.fonts.Eyebrow, "18+").X + Ui.Px(8f);
        }
        if (e.Visibility == Visibility.Private)
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(bx, y), Palette.TextSecondary.U32(), "PRIVATE");
        if (e.Cancelled)
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(bx, y), Palette.Danger.U32(), "CANCELLED");

        y += Ui.Measure(this.fonts.Eyebrow, eyebrow).Y + Ui.Px(10f);
        Ui.TextWrappedAt(dl, this.fonts.SerifTitle, new Vector2(mp.X + left, y), Palette.TextPrimary.U32(), e.Title, contentW);
        y += Ui.MeasureWrapped(this.fonts.SerifTitle, e.Title, contentW).Y + Ui.Px(8f);

        var hosted = "Hosted by ";
        Ui.TextAt(dl, this.fonts.EventMeta, new Vector2(mp.X + left, y), Palette.TextSecondary.U32(), hosted);
        var hx = mp.X + left + Ui.Measure(this.fonts.EventMeta, hosted).X;
        Ui.TextAt(dl, this.fonts.EventMeta, new Vector2(hx, y), Palette.TextPrimary.U32(), e.HostName);
        hx += Ui.Measure(this.fonts.EventMeta, e.HostName).X;
        Ui.TextAt(dl, this.fonts.EventMeta, new Vector2(hx, y), Palette.TextMuted.U32(), $" · {e.HostHandle}");
        var mastheadH = (y + Ui.Measure(this.fonts.EventMeta, hosted).Y + Ui.Px(18f)) - mp.Y;
        ImGui.Dummy(new Vector2(width, mastheadH));

        // Doors / Attending block.
        this.DrawDoors(dl, e, width, left, right);

        // Meta rows.
        this.MetaRow(dl, width, left, right, "RUNS", Duration(e.DurationMins));
        this.YourTimeRow(dl, width, left, right, e);
        this.MetaRow(dl, width, left, right, VenueLabel(e.Venue.Type), this.LocationLine(e));
        if (e.Hosting && e.Visibility == Visibility.Private && !string.IsNullOrEmpty(e.EntryCode))
            this.MetaRow(dl, width, left, right, "ENTRY CODE", e.EntryCode, mono: true);

        // Details + tags.
        this.DrawDetails(dl, e, width, left, right, contentW);

        // Footer actions.
        this.DrawFooter(e, width, left, right, contentW, eventId);
    }

    private void DrawDoors(ImDrawListPtr dl, EventDto e, float width, float left, float right)
    {
        var pos = ImGui.GetCursorScreenPos();
        var y = pos.Y + Ui.Px(14f);
        var lx = pos.X + left;
        var rx = pos.X + right;

        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(lx, y), Palette.TextSecondary.U32(), "DOORS");
        var attend = "ATTENDING";
        var attW = Ui.Measure(this.fonts.Eyebrow, attend).X;
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(rx - attW, y), Palette.TextSecondary.U32(), attend);

        var ry = y + Ui.Measure(this.fonts.Eyebrow, "X").Y + Ui.Px(6f);
        Ui.TextAt(dl, this.fonts.EventTitle, new Vector2(lx, ry), Palette.TextPrimary.U32(), e.HostClock);
        var clockW = Ui.Measure(this.fonts.EventTitle, e.HostClock).X;
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(lx + clockW + Ui.Px(6f), ry + Ui.Px(6f)), Palette.TextSecondary.U32(), e.HostTzLabel.ToUpperInvariant());

        // Attending count, right-aligned: count in ink, /capacity muted.
        var count = e.Attending.ToString();
        var cap = e.Capacity is { } c ? $"/{c}" : string.Empty;
        var capW = Ui.Measure(this.fonts.EventMeta, cap).X;
        var countW = Ui.Measure(this.fonts.EventTitle, count).X;
        Ui.TextAt(dl, this.fonts.EventTitle, new Vector2(rx - capW - countW, ry), Palette.TextPrimary.U32(), count);
        if (cap.Length > 0)
            Ui.TextAt(dl, this.fonts.EventMeta, new Vector2(rx - capW, ry + Ui.Px(6f)), Palette.TextMuted.U32(), cap);

        var dayY = ry + Ui.Measure(this.fonts.EventTitle, e.HostClock).Y + Ui.Px(2f);
        var stamp = $"{DayLabel(e.StartsAt.ToLocalTime())} · {e.StartsAt.ToLocalTime():MMM dd}".ToUpperInvariant();
        Ui.TextAt(dl, this.fonts.Mono, new Vector2(lx, dayY), Palette.TextMuted.U32(), stamp);

        var h = (dayY + Ui.Measure(this.fonts.Mono, stamp).Y + Ui.Px(16f)) - pos.Y;
        dl.AddLine(new Vector2(pos.X, pos.Y + h), new Vector2(pos.X + width, pos.Y + h), Palette.Border.U32(), 1f);
        ImGui.Dummy(new Vector2(width, h));
    }

    private void MetaRow(ImDrawListPtr dl, float width, float left, float right, string label, string value, bool mono = false)
    {
        var pos = ImGui.GetCursorScreenPos();
        var rowH = Ui.Px(50f);
        var cy = pos.Y + (rowH * 0.5f);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(pos.X + left, cy - (Ui.Measure(this.fonts.Eyebrow, label).Y * 0.5f)), Palette.TextSecondary.U32(), label);
        var valueFont = mono ? this.fonts.Eyebrow : this.fonts.EventMeta;
        var vs = Ui.Measure(valueFont, value);
        Ui.TextAt(dl, valueFont, new Vector2(pos.X + right - vs.X, cy - (vs.Y * 0.5f)), Palette.TextPrimary.U32(), value);
        dl.AddLine(new Vector2(pos.X, pos.Y + rowH), new Vector2(pos.X + width, pos.Y + rowH), Palette.Border.U32(), 1f);
        ImGui.Dummy(new Vector2(width, rowH));
    }

    private void YourTimeRow(ImDrawListPtr dl, float width, float left, float right, EventDto e)
    {
        var pos = ImGui.GetCursorScreenPos();
        var rowH = Ui.Px(50f);
        var cy = pos.Y + (rowH * 0.5f);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(pos.X + left, cy - (Ui.Measure(this.fonts.Eyebrow, "YOUR TIME").Y * 0.5f)), Palette.TextSecondary.U32(), "YOUR TIME");
        var local = e.StartsAt.ToLocalTime().ToString("HH:mm");
        var suffix = " local";
        var localW = Ui.Measure(this.fonts.EventMeta, local).X;
        var suffixW = Ui.Measure(this.fonts.EventMeta, suffix).X;
        var vy = cy - (Ui.Measure(this.fonts.EventMeta, local).Y * 0.5f);
        Ui.TextAt(dl, this.fonts.EventMeta, new Vector2(pos.X + right - suffixW - localW, vy), Palette.TextPrimary.U32(), local);
        Ui.TextAt(dl, this.fonts.EventMeta, new Vector2(pos.X + right - suffixW, vy), Palette.TextMuted.U32(), suffix);
        dl.AddLine(new Vector2(pos.X, pos.Y + rowH), new Vector2(pos.X + width, pos.Y + rowH), Palette.Border.U32(), 1f);
        ImGui.Dummy(new Vector2(width, rowH));
    }

    private void DrawDetails(ImDrawListPtr dl, EventDto e, float width, float left, float right, float contentW)
    {
        var pos = ImGui.GetCursorScreenPos();
        var y = pos.Y + Ui.Px(16f);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(pos.X + left, y), Palette.TextSecondary.U32(), "DETAILS");
        y += Ui.Measure(this.fonts.Eyebrow, "X").Y + Ui.Px(10f);

        var desc = string.IsNullOrWhiteSpace(e.Description) ? "No details yet." : e.Description;
        Ui.TextWrappedAt(dl, this.fonts.EventMeta, new Vector2(pos.X + left, y), Palette.TextPrimary.U32(), desc, contentW);
        y += Ui.MeasureWrapped(this.fonts.EventMeta, desc, contentW).Y + Ui.Px(14f);

        // Tag pills.
        if (e.Tags is { Count: > 0 })
        {
            var chipH = Ui.Px(28f);
            var x = pos.X + left;
            foreach (var tag in e.Tags)
            {
                var ts = Ui.Measure(this.fonts.Label, tag);
                var w = ts.X + (Ui.Px(13f) * 2f);
                if (x + w > pos.X + right)
                {
                    x = pos.X + left;
                    y += chipH + Ui.Px(8f);
                }

                var min = new Vector2(x, y);
                dl.AddRect(min, min + new Vector2(w, chipH), Palette.Border.U32(), chipH * 0.5f, ImDrawFlags.None, 1f);
                Ui.TextAt(dl, this.fonts.Label, new Vector2(x + Ui.Px(13f), y + ((chipH - ts.Y) * 0.5f)), Palette.TextSecondary.U32(), tag);
                x += w + Ui.Px(8f);
            }

            y += chipH;
        }

        var h = (y + Ui.Px(16f)) - pos.Y;
        dl.AddLine(new Vector2(pos.X, pos.Y + h), new Vector2(pos.X + width, pos.Y + h), Palette.Border.U32(), 1f);
        ImGui.Dummy(new Vector2(width, h));
    }

    private void DrawFooter(EventDto e, float width, float left, float right, float contentW, Guid eventId)
    {
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var y = pos.Y + Ui.Px(16f);
        var btnH = Ui.Px(44f);

        // Save event (cream fill when saved, else outlined).
        if (this.FooterButton(dl, "##ed_save", FontAwesomeIcon.Bookmark, e.SavedByMe ? "SAVED" : "SAVE EVENT", pos.X + left, y, contentW, btnH, e.SavedByMe))
            this.events.Save(eventId, !e.SavedByMe);
        y += btnH + Ui.Px(10f);

        var half = (contentW - Ui.Px(10f)) * 0.5f;
        if (this.FooterButton(dl, "##ed_share", FontAwesomeIcon.Share, "SHARE", pos.X + left, y, half, btnH, false))
            this.ShareToChat(e);
        if (this.FooterButton(dl, "##ed_loc", FontAwesomeIcon.Copy, "LOCATION", pos.X + left + half + Ui.Px(10f), y, half, btnH, false))
            ImGui.SetClipboardText(this.LocationLine(e));
        y += btnH + Ui.Px(12f);

        // Spots left / no cap.
        var spots = e.Capacity is { } cap ? $"{Math.Max(0, cap - e.Attending)} spots left" : "No cap, walk-ups welcome";
        var people = FontAwesomeIcon.User.ToIconString();
        Ui.TextAt(dl, this.fonts.Icon, new Vector2(pos.X + left, y), Palette.TextMuted.U32(), people);
        Ui.TextAt(dl, this.fonts.EventMeta, new Vector2(pos.X + left + Ui.Measure(this.fonts.Icon, people).X + Ui.Px(8f), y), Palette.TextMuted.U32(), spots);

        var h = (y + Ui.Measure(this.fonts.EventMeta, spots).Y + Ui.Px(20f)) - pos.Y;
        ImGui.Dummy(new Vector2(width, h));
    }

    private bool FooterButton(ImDrawListPtr dl, string id, FontAwesomeIcon icon, string label, float x, float y, float w, float h, bool filled)
    {
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
        var hovered = ImGui.IsItemHovered();
        var min = new Vector2(x, y);
        var max = min + new Vector2(w, h);
        if (filled)
            dl.AddRectFilled(min, max, Palette.TextPrimary.U32());
        else
            dl.AddRect(min, max, (hovered ? Palette.BorderStrong : Palette.Border).U32(), 0f, ImDrawFlags.None, 1f);
        var col = (filled ? Palette.Paper : (hovered ? Palette.TextPrimary : Palette.TextSecondary)).U32();
        var glyph = icon.ToIconString();
        var gs = Ui.Measure(this.fonts.Icon, glyph);
        var ls = Ui.Measure(this.fonts.Eyebrow, label);
        var totalW = gs.X + Ui.Px(8f) + ls.X;
        var gx = x + ((w - totalW) * 0.5f);
        Ui.TextAt(dl, this.fonts.Icon, new Vector2(gx, y + ((h - gs.Y) * 0.5f)), col, glyph);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(gx + gs.X + Ui.Px(8f), y + ((h - ls.Y) * 0.5f)), col, label);
        return clicked;
    }

    // ---- code gate (private event, viewer without access) -------------------------------------

    private void DrawCodeGate(float width, float pad, Guid eventId)
    {
        var dl = ImGui.GetWindowDrawList();
        var locked = DateTime.UtcNow < this.lockoutUntil;
        ImGui.Dummy(new Vector2(0f, Ui.Px(56f)));

        this.kit.EmptyState(FontAwesomeIcon.Lock.ToIconString(), "By invitation only", "Enter the code the host gave you. Nothing is shown until it matches.", width);

        var fieldW = Ui.Px(220f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((width - fieldW) * 0.5f));
        var code = this.codeInput;
        this.kit.TextField("##ed_code", ref code, "ABC123", fieldW);
        this.codeInput = code.ToUpperInvariant();
        if (this.codeInput.Length > 8)
            this.codeInput = this.codeInput[..8];
        ImGui.Dummy(new Vector2(0f, Ui.Px(8f)));

        if (this.codeTries > 0 && !locked)
            Ui.CenteredText(width, this.fonts.Caption, Palette.Danger, "No event matches that code.");
        if (locked)
            Ui.CenteredText(width, this.fonts.Caption, Palette.TextMuted, $"Too many attempts. Wait {Math.Ceiling((this.lockoutUntil - DateTime.UtcNow).TotalSeconds)}s.");

        var btnW = Ui.Px(160f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((width - btnW) * 0.5f));
        if (this.kit.PrimaryButton("##ed_unlock", "Unlock", btnW) && !locked && this.codeInput.Length >= 4)
            this.TryUnlock();
    }

    private async void TryUnlock()
    {
        var result = await this.events.LookupAsync(this.codeInput);
        if (result != null)
        {
            this.selection.EventId = result.Id;
            this.codeInput = string.Empty;
            this.codeTries = 0;
        }
        else if (++this.codeTries >= 3)
        {
            this.lockoutUntil = DateTime.UtcNow.AddSeconds(30);
            this.codeTries = 0;
        }
    }

    // ---- overflow menu + delete confirm -------------------------------------------------------

    private void DrawMenu(EventDto? e, Guid eventId)
    {
        using (this.MenuStyle())
        {
            if (!ImGui.BeginPopup("##ed_menu"))
                return;

            if (e is { Hosting: true })
            {
                if (this.MenuRow(FontAwesomeIcon.Pen, "Edit event", false))
                {
                    this.selection.EventId = eventId;
                    this.selection.EventReturn = Screen.EventDetail;
                    this.router.Navigate(Screen.EventCreate);
                    ImGui.CloseCurrentPopup();
                }

                if (e.Visibility == Visibility.Private && this.MenuRow(FontAwesomeIcon.Sync, "Regenerate code", false))
                {
                    _ = this.events.RegenerateCodeAsync(eventId);
                    ImGui.CloseCurrentPopup();
                }

                if (this.MenuRow(FontAwesomeIcon.Share, "Share to chat", false))
                {
                    this.ShareToChat(e);
                    ImGui.CloseCurrentPopup();
                }

                if (e.Cancelled)
                {
                    if (this.MenuRow(FontAwesomeIcon.Undo, "Restore event", true))
                    {
                        this.events.Restore(eventId);
                        ImGui.CloseCurrentPopup();
                    }
                }
                else if (this.MenuRow(FontAwesomeIcon.Ban, "Cancel event", true))
                {
                    this.events.Cancel(eventId);
                    ImGui.CloseCurrentPopup();
                }

                if (this.MenuRow(FontAwesomeIcon.TrashAlt, "Delete event", true))
                {
                    this.confirmDelete = true;
                    ImGui.CloseCurrentPopup();
                }
            }
            else
            {
                if (e != null && this.MenuRow(FontAwesomeIcon.Share, "Share to chat", false))
                {
                    this.ShareToChat(e);
                    ImGui.CloseCurrentPopup();
                }

                if (e != null && this.MenuRow(FontAwesomeIcon.Bookmark, e.SavedByMe ? "Remove from saved" : "Save event", false))
                {
                    this.events.Save(eventId, e is not { SavedByMe: true });
                    ImGui.CloseCurrentPopup();
                }

                if (e != null && this.MenuRow(FontAwesomeIcon.Flag, "Report event", true))
                {
                    this.moderation.OpenReportEvent(e.HostId, eventId, e.Title);
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.EndPopup();
        }
    }

    private void DrawDeleteDialog(Guid eventId)
    {
        if (this.confirmDelete)
        {
            ImGui.OpenPopup("##ed_delete");
            this.confirmDelete = false;
        }

        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + (ImGui.GetWindowSize() * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(Ui.Px(280f), 0f));
        using (this.DialogStyle())
        {
            var open = true;
            if (!ImGui.BeginPopupModal("##ed_delete", ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize))
                return;

            Ui.CenteredText(Ui.Px(240f), this.fonts.SerifName, Palette.TextPrimary, "Delete this event?");
            ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
            Ui.CenteredText(Ui.Px(240f), this.fonts.Caption, Palette.TextMuted, "This can't be undone.");
            ImGui.Dummy(new Vector2(0f, Ui.Px(14f)));

            var half = (Ui.Px(240f) - Ui.Px(10f)) * 0.5f;
            if (this.kit.SecondaryButton("##ed_del_cancel", "Cancel", half))
                ImGui.CloseCurrentPopup();
            ImGui.SameLine(0f, Ui.Px(10f));
            if (this.kit.DangerButton("##ed_del_ok", "Delete", half))
            {
                this.events.Delete(eventId);
                ImGui.CloseCurrentPopup();
                this.router.Navigate(this.selection.EventReturn);
            }

            ImGui.EndPopup();
        }
    }

    private void ShareToChat(EventDto e)
    {
        this.selection.PendingShareEvent = new EventShare(
            e.Id, e.Kind, e.Title, e.BannerPreset ?? EventService.FallbackPreset(e.Id),
            e.StartsAt, e.HostClock, e.HostTzLabel, this.LocationLine(e), e.Attending, e.Capacity);
        this.router.Navigate(Screen.Messages);
    }

    private void DrawStatus(float width, string text)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(60f)));
        Ui.CenteredText(width, this.fonts.Caption, Palette.TextMuted, text);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private string LocationLine(EventDto e)
    {
        var v = e.Venue;
        switch (v.Type)
        {
            case EventVenueEnum.Housing:
            {
                var room = v.Room is > 0 ? $" · Room {v.Room}" : string.Empty;
                var world = v.WorldId is { } w ? this.WorldOnly((int)w) : string.Empty;
                return $"{EventCatalog.DistrictLabel(v.District ?? HousingDistrictEnum.Mist)} W{v.Ward} P{v.Plot}{room} · {world}".Trim(' ', '·');
            }

            case EventVenueEnum.OpenWorld:
            {
                var zone = v.ZoneId is { } z ? this.catalog.ZoneName((int)z) : string.Empty;
                var land = v.AetheryteId is { } a ? this.catalog.AetheryteName((int)a) : string.Empty;
                var world = v.WorldId is { } w ? this.WorldOnly((int)w) : string.Empty;
                var landPart = land.Length > 0 ? $" · {land}" : string.Empty;
                return $"{zone}{landPart} · {world}".Trim(' ', '·');
            }

            default:
                return string.IsNullOrEmpty(v.DiscordUrl) ? "Discord" : v.DiscordUrl;
        }
    }

    private string WorldOnly(int worldId)
    {
        foreach (var dc in this.worlds.DataCenters)
            foreach (var w in dc.Worlds)
                if (w.Id == worldId)
                    return w.Name;
        return string.Empty;
    }

    private static string VenueLabel(EventVenueEnum venue) => venue switch
    {
        EventVenueEnum.Housing => "HOUSING",
        EventVenueEnum.OpenWorld => "OPEN WORLD",
        _ => "DISCORD",
    };

    private static string KindLabel(EventKindElement kind) => kind switch
    {
        EventKindElement.Club => "Club night",
        EventKindElement.Gathering => "Gathering",
        EventKindElement.Performance => "Performance",
        EventKindElement.Raid => "Raid",
        EventKindElement.Roleplay => "Roleplay",
        _ => "Market",
    };

    private static string Duration(long mins)
    {
        if (mins < 60)
            return $"{mins}m";
        var h = mins / 60;
        var m = mins % 60;
        return m > 0 ? $"{h}h {m}m" : $"{h}h";
    }

    private static string DayLabel(DateTimeOffset local)
    {
        var diff = (local.Date - DateTimeOffset.Now.Date).Days;
        return diff switch { 0 => "Today", 1 => "Tomorrow", _ => local.ToString("dddd") };
    }

    private IDisposable MenuStyle() => new Composite(new List<IDisposable>
    {
        ImRaii.PushColor(ImGuiCol.PopupBg, Palette.Surface1),
        ImRaii.PushColor(ImGuiCol.Border, Palette.Border),
        ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(Ui.Px(6f), Ui.Px(6f))),
        ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 0f),
        ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f),
    });

    private IDisposable DialogStyle() => new Composite(new List<IDisposable>
    {
        ImRaii.PushColor(ImGuiCol.PopupBg, Palette.Surface1),
        ImRaii.PushColor(ImGuiCol.Border, Palette.Border),
        ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(Ui.Px(18f), Ui.Px(18f))),
        ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 0f),
        ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f),
    });

    private bool MenuRow(FontAwesomeIcon icon, string label, bool danger)
    {
        var width = Ui.Px(198f);
        var height = Ui.Px(36f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##ed_menu_" + label, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        if (hovered)
            dl.AddRectFilled(pos, pos + new Vector2(width, height), Palette.WithAlpha(Palette.Overlay, 0.05f).U32());
        var color = danger ? Palette.Danger : Palette.TextSecondary;
        var glyph = icon.ToIconString();
        var gs = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(dl, this.fonts.Icon, new Vector2(pos.X + Ui.Px(10f), pos.Y + ((height - gs.Y) * 0.5f)), color.U32(), glyph);
        var ls = Ui.Measure(this.fonts.Body, label);
        Ui.TextAt(dl, this.fonts.Body, new Vector2(pos.X + Ui.Px(36f), pos.Y + ((height - ls.Y) * 0.5f)), (danger ? color : Palette.TextPrimary).U32(), label);
        return clicked;
    }

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
