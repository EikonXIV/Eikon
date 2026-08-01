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

// The Events board, rendered as the "Events" tab of the Grid (Discover) screen (GridScreen owns the
// People/Events toggle and calls Draw here). Masthead + Browse/Hosting/Saved segmented + World/DC/Region
// scope tabs + kind chips + day-grouped cards with full-bleed banners and infinite scroll. Opening a
// card navigates to the event detail; the key opens the code lookup, the plus opens the create wizard.
internal sealed class EventsBoardView
{
    private readonly ScreenRouter router;
    private readonly Kit kit;
    private readonly UiFonts fonts;
    private readonly EventService events;
    private readonly EventCatalog catalog;
    private readonly WorldCatalog worlds;
    private readonly Selection selection;

    // Kind-chip strip horizontal scroll: the ">" chevron nudges (0 = none, 1 = step right, 2 = loop to
    // start); ScrollX/MaxX are captured inside the child so the chevron (drawn outside it) knows the state.
    private int kindNudge;
    private float kindScrollX;
    private float kindScrollMaxX;

    // Kind filter chips in display order, with the short chip label and the board/detail label.
    private static readonly (EventKindElement Kind, string Chip, string Label)[] KindRow =
    {
        (EventKindElement.Gathering, "Gathering", "Gathering"),
        (EventKindElement.Club, "Club", "Club night"),
        (EventKindElement.Performance, "Performance", "Performance"),
        (EventKindElement.Raid, "Raid", "Raid"),
        (EventKindElement.Roleplay, "Roleplay", "Roleplay"),
        (EventKindElement.Market, "Market", "Market"),
    };

    public EventsBoardView(ScreenRouter router, Kit kit, UiFonts fonts, EventService events, EventCatalog catalog, WorldCatalog worlds, Selection selection)
    {
        this.router = router;
        this.kit = kit;
        this.fonts = fonts;
        this.events = events;
        this.catalog = catalog;
        this.worlds = worlds;
        this.selection = selection;
    }

    public void EnsureInitial()
    {
        this.events.EnsureInitial();
        this.catalog.EnsureLoaded();
        this.worlds.EnsureLoaded();
    }

    public void Draw(float pad)
    {
        this.EnsureInitial();
        var width = ImGui.GetContentRegionAvail().X;

        this.DrawMasthead(width, pad);
        this.DrawScopeRow(width, pad);
        this.DrawKindChips(width, pad);

        var bodyAvail = ImGui.GetContentRegionAvail();
        using var noPad = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);   // banners are full-bleed
        using var scroll = ImRaii.Child("events_scroll", bodyAvail, false, ImGuiWindowFlags.AlwaysVerticalScrollbar);
        if (!scroll.Success)
            return;
        var contentWidth = ImGui.GetContentRegionAvail().X;

        if (this.events.Reloading && this.events.Events.Count == 0)
        {
            this.DrawStatus(contentWidth, "Finding events…");
            return;
        }

        if (this.events.Events.Count == 0)
        {
            this.DrawEmpty(contentWidth, pad);
            return;
        }

        this.DrawCards(contentWidth, pad);

        if (this.events.HasMore)
        {
            if (this.events.Loading)
                this.DrawStatus(contentWidth, "Loading more…");
            else if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - Ui.Px(240f))
                this.events.LoadMore();
        }
    }

    private void DrawMasthead(float width, float pad)
    {
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var left = origin.X + pad;
        var right = (origin.X + width) - pad;
        var y = origin.Y + Ui.Px(14f);

        var eyebrowH = Ui.Measure(this.fonts.Eyebrow, "X").Y;
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(left, y), Palette.TextSecondary.U32(), "HAPPENING");
        var listedW = Ui.Measure(this.fonts.Eyebrow, "LISTED").X;
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(right - listedW, y), Palette.TextSecondary.U32(), "LISTED");
        y += eyebrowH + Ui.Px(4f);

        var titleH = Ui.Measure(this.fonts.SerifTitle, "Events").Y;
        var leadW = Ui.Measure(this.fonts.SerifTitle, "Events ").X;
        Ui.TextAt(dl, this.fonts.SerifTitle, new Vector2(left, y), Palette.TextPrimary.U32(), "Events ");
        Ui.TextAt(dl, this.fonts.SerifItalicTitle, new Vector2(left + leadW, y), Palette.TextSecondary.U32(), "on the board");
        var count = this.events.Events.Count.ToString("N0");
        var countSize = Ui.Measure(this.fonts.Count, count);
        Ui.TextAt(dl, this.fonts.Count, new Vector2(right - countSize.X, (y + titleH) - countSize.Y - Ui.Px(1f)), Palette.TextPrimary.U32(), count);
        y += titleH + Ui.Px(16f);

        // Browse / Hosting / Saved segmented: a bordered SQUARE control (editorial radius is 0), the
        // active cell filled cream (paper text), inactive cells transparent with the mono label + count.
        var segH = Ui.Px(42f);
        var segX = left;
        var segW = right - left;
        var cellW = segW / 3f;
        var cells = new[]
        {
            ("BROWSE", (int?)null),
            ("HOSTING", this.events.HostingCount),
            ("SAVED", this.events.SavedCount),
        };
        dl.AddRect(new Vector2(segX, y), new Vector2(segX + segW, y + segH), Palette.BorderStrong.U32(), 0f, ImDrawFlags.None, 1f);
        for (var b = 1; b < 3; b++)
            if (this.events.TabIndex != b - 1 && this.events.TabIndex != b)
                dl.AddLine(new Vector2(segX + (cellW * b), y + Ui.Px(9f)), new Vector2(segX + (cellW * b), y + segH - Ui.Px(9f)), Palette.Border.U32(), 1f);
        for (var i = 0; i < 3; i++)
        {
            var cx0 = segX + (cellW * i);
            var active = this.events.TabIndex == i;
            ImGui.SetCursorScreenPos(new Vector2(cx0, y));
            if (ImGui.InvisibleButton($"##etab_{i}", new Vector2(cellW, segH)) && !active)
                this.events.SetTab(EventService.TabOrder[i]);
            if (active)
                dl.AddRectFilled(new Vector2(cx0 + Ui.Px(2f), y + Ui.Px(2f)), new Vector2(cx0 + cellW - Ui.Px(2f), y + segH - Ui.Px(2f)), Palette.TextPrimary.U32(), 0f);
            var label = cells[i].Item2 is { } n ? $"{cells[i].Item1}  {n:00}" : cells[i].Item1;
            var ts = Ui.Measure(this.fonts.Eyebrow, label);
            var col = (active ? Palette.Paper : Palette.TextSecondary).U32();
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(cx0 + ((cellW - ts.X) * 0.5f), y + ((segH - ts.Y) * 0.5f)), col, label);
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, y + segH + Ui.Px(14f)));
    }

    private void DrawScopeRow(float width, float pad)
    {
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var left = origin.X + pad;
        var right = (origin.X + width) - pad;
        var tabH = Ui.Measure(this.fonts.Eyebrow, "WORLD").Y;
        var y = origin.Y;

        var tabX = left;
        foreach (var scope in EventService.ScopeOrder)
        {
            var label = scope.ToString().ToUpperInvariant();
            var size = Ui.Measure(this.fonts.Eyebrow, label);
            ImGui.SetCursorScreenPos(new Vector2(tabX, y));
            if (ImGui.InvisibleButton($"##escope_{scope}", new Vector2(size.X, tabH)))
                this.events.SetScope(scope);
            var active = this.events.Scope == scope;
            var col = (active || ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32();
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(tabX, y), col, label);
            if (active)
                dl.AddLine(new Vector2(tabX, y + tabH + Ui.Px(3f)), new Vector2(tabX + size.X, y + tabH + Ui.Px(3f)), Palette.TextPrimary.U32(), 1f);
            tabX += size.X + Ui.Px(18f);
        }

        // Right group: key (code lookup) | + HOST (create).
        var plus = FontAwesomeIcon.Plus.ToIconString();
        var plusW = Ui.Measure(this.fonts.Icon, plus).X;
        var hostW = Ui.Measure(this.fonts.Eyebrow, "HOST").X;
        var hostGroupW = plusW + Ui.Px(6f) + hostW;
        var hx = right - hostGroupW;
        ImGui.SetCursorScreenPos(new Vector2(hx, y - Ui.Px(3f)));
        var hostClicked = ImGui.InvisibleButton("##ehost", new Vector2(hostGroupW, tabH + Ui.Px(6f)));
        var hostCol = (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32();
        Ui.TextAt(dl, this.fonts.Icon, new Vector2(hx, y), hostCol, plus);
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(hx + plusW + Ui.Px(6f), y), hostCol, "HOST");
        if (hostClicked)
            this.OpenCreate();

        var divX = hx - Ui.Px(12f);
        dl.AddLine(new Vector2(divX, y), new Vector2(divX, y + tabH), Palette.Border.U32(), 1f);

        var key = FontAwesomeIcon.Key.ToIconString();
        var keyW = Ui.Measure(this.fonts.Icon, key).X;
        var kx = divX - Ui.Px(12f) - keyW;
        ImGui.SetCursorScreenPos(new Vector2(kx - Ui.Px(4f), y - Ui.Px(3f)));
        if (ImGui.InvisibleButton("##ekey", new Vector2(keyW + Ui.Px(8f), tabH + Ui.Px(6f))))
            this.router.Navigate(Screen.EventCodeLookup);
        Ui.TextAt(dl, this.fonts.Icon, new Vector2(kx, y), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32(), key);

        var hairY = y + tabH + Ui.Px(12f);
        dl.AddLine(new Vector2(origin.X, hairY), new Vector2(origin.X + width, hairY), Palette.Border.U32(), 1f);
        ImGui.SetCursorScreenPos(new Vector2(origin.X, hairY));
    }

    private void DrawKindChips(float width, float pad)
    {
        // Only the browse tab filters by kind; hosting/saved show everything.
        if (this.events.Tab != Tab.Browse)
        {
            ImGui.Dummy(new Vector2(0f, Ui.Px(6f)));
            return;
        }

        var chipH = Ui.Px(30f);
        var overflow = this.kindScrollMaxX > 1f;             // there are chips off the right edge
        var chevW = overflow ? Ui.Px(30f) : 0f;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + pad);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Ui.Px(12f));
        var childOrigin = ImGui.GetCursorScreenPos();
        var childW = width - (pad * 2f) - chevW;

        // Mouse wheel scrolls the strip (no vertical scrollbar => wheel scrolls X); the ">" chevron below
        // nudges it too, so every kind is reachable on the narrow window.
        using (ImRaii.PushStyle(ImGuiStyleVar.ScrollbarSize, 0f))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero))
        using (var child = ImRaii.Child("evkinds", new Vector2(childW, chipH), false, ImGuiWindowFlags.HorizontalScrollbar))
        {
            if (child.Success)
            {
                if (this.kindNudge == 1)
                    ImGui.SetScrollX(Math.Clamp(ImGui.GetScrollX() + Ui.Px(150f), 0f, ImGui.GetScrollMaxX()));
                else if (this.kindNudge == 2)
                    ImGui.SetScrollX(0f);
                this.kindNudge = 0;

                this.Chip("All", this.events.Kinds.Count == 0, chipH, () => this.events.ClearKinds());
                foreach (var k in KindRow)
                {
                    ImGui.SameLine(0f, Ui.Px(8f));
                    var kind = k.Kind;
                    this.Chip(k.Chip, this.events.Kinds.Contains(kind), chipH, () => this.events.ToggleKind(kind));
                }

                this.kindScrollX = ImGui.GetScrollX();
                this.kindScrollMaxX = ImGui.GetScrollMaxX();
            }
        }

        if (overflow)
        {
            var dl = ImGui.GetWindowDrawList();
            var atEnd = this.kindScrollX >= this.kindScrollMaxX - 1f;
            var chevPos = new Vector2(childOrigin.X + childW, childOrigin.Y);
            ImGui.SetCursorScreenPos(chevPos);
            if (ImGui.InvisibleButton("##kindchev", new Vector2(chevW, chipH)))
                this.kindNudge = atEnd ? 2 : 1;
            var glyph = (atEnd ? FontAwesomeIcon.ChevronLeft : FontAwesomeIcon.ChevronRight).ToIconString();
            var gs = Ui.Measure(this.fonts.Icon, glyph);
            var center = chevPos + new Vector2(chevW * 0.5f, chipH * 0.5f);
            dl.AddCircleFilled(center, chipH * 0.46f, Palette.Surface2.U32(), 20);
            dl.AddCircle(center, chipH * 0.46f, Palette.Border.U32(), 20, 1f);
            Ui.TextAt(dl, this.fonts.Icon, center - (gs * 0.5f), (ImGui.IsItemHovered() ? Palette.TextPrimary : Palette.TextSecondary).U32(), glyph);
        }

        // Restore the layout cursor to just below the strip, full width, for the scroll region.
        ImGui.SetCursorScreenPos(new Vector2(childOrigin.X - pad, childOrigin.Y + chipH + Ui.Px(10f)));
    }

    // A pill chip (rounded-full): active = cream fill + paper text; inactive = hairline outline.
    private void Chip(string label, bool active, float h, Action onClick)
    {
        var ts = Ui.Measure(this.fonts.Label, label);
        var padX = Ui.Px(13f);
        if (ImGui.InvisibleButton("##kc_" + label, new Vector2(ts.X + (padX * 2f), h)))
            onClick();
        var hovered = ImGui.IsItemHovered();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        var r = h * 0.5f;
        if (active)
            dl.AddRectFilled(min, max, Palette.TextPrimary.U32(), r);
        else
            dl.AddRect(min, max, (hovered ? Palette.BorderStrong : Palette.Border).U32(), r, ImDrawFlags.None, 1f);
        var col = active ? Palette.Paper.U32() : (hovered ? Palette.TextPrimary : Palette.TextSecondary).U32();
        Ui.TextAt(dl, this.fonts.Label, new Vector2(min.X + padX, min.Y + ((h - ts.Y) * 0.5f)), col, label);
    }

    private void DrawCards(float width, float pad)
    {
        var dl = ImGui.GetWindowDrawList();
        string? currentDay = null;
        ImGui.Dummy(new Vector2(0f, Ui.Px(4f)));
        foreach (var e in this.events.Events)
        {
            var local = e.StartsAt.ToLocalTime();
            var dayKey = local.Date.ToString("yyyy-MM-dd");
            if (dayKey != currentDay)
            {
                currentDay = dayKey;
                this.DrawDayHeader(dl, width, pad, DayLabel(local), local.ToString("MMM dd").ToUpperInvariant());
            }

            this.DrawCard(dl, e, width, pad);
        }

        ImGui.Dummy(new Vector2(0f, Ui.Px(16f)));
    }

    private void DrawDayHeader(ImDrawListPtr dl, float width, float pad, string day, string stamp)
    {
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Ui.Px(8f));
        var pos = ImGui.GetCursorScreenPos();
        var h = Ui.Measure(this.fonts.Eyebrow, day).Y;
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(pos.X + pad, pos.Y), Palette.TextSecondary.U32(), day.ToUpperInvariant());
        var stampW = Ui.Measure(this.fonts.Eyebrow, stamp).X;
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2((pos.X + width) - pad - stampW, pos.Y), Palette.TextSecondary.U32(), stamp);
        ImGui.Dummy(new Vector2(width, h + Ui.Px(10f)));
    }

    private void DrawCard(ImDrawListPtr dl, EventCardDto e, float width, float pad)
    {
        var bannerH = width / 3f;                       // full-bleed 3:1
        var infoH = Ui.Px(92f);
        var cardH = bannerH + infoH;
        var pos = ImGui.GetCursorScreenPos();

        if (ImGui.InvisibleButton($"##ev_{e.Id}", new Vector2(width, cardH)))
        {
            this.selection.EventId = e.Id;
            this.selection.EventName = e.Title;
            this.selection.EventReturn = Screen.Grid;
            this.router.Navigate(Screen.EventDetail);
        }

        // Full-bleed banner (preset art or an uploaded texture).
        dl.AddRectFilled(pos, pos + new Vector2(width, bannerH), Palette.Surface2.U32());
        var texture = this.events.BannerFor(e);
        if (texture != null)
        {
            var (uvMin, uvMax) = Ui.CoverUv(texture.Width, texture.Height, width / bannerH);
            dl.AddImage(texture.Handle, pos, pos + new Vector2(width, bannerH), uvMin, uvMax);
        }
        else
        {
            var glyph = FontAwesomeIcon.Image.ToIconString();
            var gs = Ui.Measure(this.fonts.Icon, glyph);
            Ui.TextAt(dl, this.fonts.Icon, new Vector2(pos.X + ((width - gs.X) * 0.5f), pos.Y + ((bannerH - gs.Y) * 0.5f)), Palette.TextMuted.U32(), glyph);
        }

        // Info row (inset by pad).
        var iy = pos.Y + bannerH + Ui.Px(12f);
        var timeX = pos.X + pad;
        Ui.TextAt(dl, this.fonts.Count, new Vector2(timeX, iy), Palette.TextPrimary.U32(), e.HostClock);
        var bigH = Ui.Measure(this.fonts.Count, e.HostClock).Y;
        Ui.TextAt(dl, this.fonts.Mono, new Vector2(timeX, iy + bigH + Ui.Px(3f)), Palette.TextSecondary.U32(), e.HostTzLabel.ToUpperInvariant());
        Ui.TextAt(dl, this.fonts.Mono, new Vector2(timeX, iy + bigH + Ui.Px(17f)), Palette.TextMuted.U32(), Duration(e.DurationMins));

        var textX = pos.X + pad + Ui.Px(62f);
        var textRight = (pos.X + width) - pad;
        var textW = textRight - textX;

        var eyebrow = KindLabel(e.Kind).ToUpperInvariant();
        Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(textX, iy), Palette.TextSecondary.U32(), eyebrow);
        var markX = textX + Ui.Measure(this.fonts.Eyebrow, eyebrow).X + Ui.Px(8f);
        if (e.Rating == EventRatingEnum.Ad)
        {
            Ui.TextAt(dl, this.fonts.Eyebrow, new Vector2(markX, iy), Palette.Danger.U32(), "18+");
            markX += Ui.Measure(this.fonts.Eyebrow, "18+").X + Ui.Px(6f);
        }
        if (e.Visibility == Visibility.Private)
        {
            var lk = FontAwesomeIcon.Lock.ToIconString();
            Ui.TextAt(dl, this.fonts.Icon, new Vector2(markX, iy), Palette.TextMuted.U32(), lk);
            markX += Ui.Measure(this.fonts.Icon, lk).X + Ui.Px(6f);
        }
        if (e.SavedByMe)
        {
            var bm = FontAwesomeIcon.Bookmark.ToIconString();
            Ui.TextAt(dl, this.fonts.Icon, new Vector2(markX, iy), Palette.Signal.U32(), bm);
        }

        var (first, tail) = SplitTitle(e.Title);
        var titleY = iy + Ui.Px(16f);
        Ui.TextAt(dl, this.fonts.SerifName, new Vector2(textX, titleY), Palette.TextPrimary.U32(), first);
        if (tail.Length > 0)
        {
            var fw = Ui.Measure(this.fonts.SerifName, first).X;
            Ui.TextAt(dl, this.fonts.SerifItalicTitle, new Vector2(textX + fw, titleY), Palette.TextSecondary.U32(), tail);
        }

        var loc = this.LocationLine(e);
        var locY = titleY + Ui.Measure(this.fonts.SerifName, first).Y + Ui.Px(4f);
        Ui.TextAt(dl, this.fonts.LabelSmall, new Vector2(textX, locY), Palette.TextSecondary.U32(), this.Fit(loc, textW, this.fonts.LabelSmall));

        var footY = locY + Ui.Measure(this.fonts.LabelSmall, "X").Y + Ui.Px(6f);
        var people = FontAwesomeIcon.User.ToIconString();
        Ui.TextAt(dl, this.fonts.Icon, new Vector2(textX, footY), Palette.TextMuted.U32(), people);
        var attend = e.Capacity is { } cap ? $"{e.Attending}/{cap}" : e.Attending.ToString();
        var attendX = textX + Ui.Measure(this.fonts.Icon, people).X + Ui.Px(7f);
        Ui.TextAt(dl, this.fonts.LabelSmall, new Vector2(attendX, footY), Palette.TextSecondary.U32(), attend);
        var hostX = attendX + Ui.Measure(this.fonts.LabelSmall, attend).X + Ui.Px(14f);
        Ui.TextAt(dl, this.fonts.LabelSmall, new Vector2(hostX, footY), Palette.TextMuted.U32(), e.HostName);

        ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + cardH));
        ImGui.Dummy(new Vector2(0f, Ui.Px(20f)));
    }

    private void DrawStatus(float width, string text)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(40f)));
        Ui.CenteredText(width, this.fonts.Caption, Palette.TextMuted, text);
    }

    private void DrawEmpty(float width, float pad)
    {
        ImGui.Dummy(new Vector2(0f, Ui.Px(44f)));
        var (headline, body) = this.events.Tab switch
        {
            Tab.Hosting => ("Nothing hosted yet", "Put an event on the board and it shows here."),
            Tab.Saved => ("No saved events", "Save an event and it shows here for quick access."),
            _ => ("Nothing on right now", "Widen the scope, or put something on the board yourself."),
        };
        this.kit.EmptyState(FontAwesomeIcon.CalendarAlt.ToIconString(), headline, body, width);
        var buttonWidth = Ui.Px(180f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((width - buttonWidth) * 0.5f));
        if (this.kit.PrimaryButton("##empty_host", "Create an event", buttonWidth))
            this.OpenCreate();
    }

    private void OpenCreate()
    {
        this.selection.EventId = null;
        this.selection.EventReturn = Screen.Grid;
        this.router.Navigate(Screen.EventCreate);
    }

    // ---- resolution + formatting helpers ------------------------------------------------------

    private string LocationLine(EventCardDto e)
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
                return string.IsNullOrEmpty(v.DiscordNote) ? "Discord voice" : $"Discord · {v.DiscordNote}";
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

    private static string KindLabel(EventKindElement kind)
    {
        foreach (var k in KindRow)
            if (k.Kind == kind)
                return k.Label;
        return kind.ToString();
    }

    private static (string First, string Tail) SplitTitle(string title)
    {
        var space = title.IndexOf(' ');
        return space > 0 ? (title[..(space + 1)], title[(space + 1)..]) : (title, string.Empty);
    }

    private static string Duration(long mins)
    {
        if (mins < 60)
            return $"{mins}M";
        var h = mins / 60;
        var m = mins % 60;
        return m > 0 ? $"{h}H {m}M" : $"{h}H";
    }

    private static string DayLabel(DateTimeOffset local)
    {
        var today = DateTimeOffset.Now.Date;
        var diff = (local.Date - today).Days;
        return diff switch
        {
            0 => "Today",
            1 => "Tomorrow",
            _ => local.ToString("dddd"),
        };
    }

    private string Fit(string text, float maxWidth, IFontHandle font)
    {
        if (Ui.Measure(font, text).X <= maxWidth)
            return text;
        var s = text;
        while (s.Length > 1 && Ui.Measure(font, s + "…").X > maxWidth)
            s = s[..^1];
        return s + "…";
    }
}
