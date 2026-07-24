using Dalamud.Interface;
using Eikon.UI.Theme;

namespace Eikon.UI;

// Reusable themed widgets that match the SCREENS conventions. Each is drawn with the draw list so
// we keep pixel control, every color comes from the theme so the kit recolors with the accent, and
// text uses the shared type scale. Widgets that can repeat on a screen take an explicit id.
internal sealed class Kit
{
    private readonly ThemeService theme;
    private readonly UiFonts fonts;

    // Which stepper (if any) currently has its centre field in edit, plus the in-progress text so an
    // immediate-mode resync can't clobber a partially typed number.
    private string? stepperEditId;
    private string stepperEditBuffer = string.Empty;

    public Kit(ThemeService theme, UiFonts fonts)
    {
        this.theme = theme;
        this.fonts = fonts;
    }

    // An editorial tag chip: hard-square, hairline outline with secondary text; selected chips fill
    // with ink, flip the text to paper, and gain a small drawn check. Returns true on click.
    // showCheck draws a tick beside the label when selected. Onboarding opts out for a fill-only chip.
    public bool Chip(string id, string label, bool selected, bool showCheck = true)
    {
        var padX = Ui.Px(12f);
        var height = Ui.Px(32f);
        var check = selected && showCheck ? Ui.Px(14f) : 0f;
        var textSize = Ui.Measure(this.fonts.LabelSmall, label);
        var size = new Vector2(check + textSize.X + (padX * 2f), height);
        var padY = (height - textSize.Y) * 0.5f;

        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();

        if (selected)
        {
            drawList.AddRectFilled(pos, pos + size, Palette.TextPrimary.U32(), 0f);
            if (showCheck)
            {
                var tick = Palette.Paper.U32();
                var cx = pos.X + padX;
                var cy = pos.Y + (height * 0.5f);
                drawList.AddLine(new Vector2(cx, cy), new Vector2(cx + Ui.Px(3f), cy + Ui.Px(3.5f)), tick, Ui.Px(1.5f));
                drawList.AddLine(new Vector2(cx + Ui.Px(3f), cy + Ui.Px(3.5f)), new Vector2(cx + Ui.Px(9f), cy - Ui.Px(4f)), tick, Ui.Px(1.5f));
            }

            Ui.TextAt(drawList, this.fonts.LabelSmall, pos + new Vector2(padX + check, padY), Palette.Paper.U32(), label);
        }
        else
        {
            var border = hovered ? Palette.BorderStrong : Palette.Border;
            drawList.AddRect(pos, pos + size, border.U32(), 0f, ImDrawFlags.None, 1f);
            Ui.TextAt(drawList, this.fonts.LabelSmall, pos + new Vector2(padX, padY), (hovered ? Palette.TextPrimary : Palette.TextSecondary).U32(), label);
        }

        return clicked;
    }

    // Editorial section eyebrow: mono, tracked caps, muted. Callers pass mixed case; it uppercases.
    public void SectionLabel(string text)
    {
        using (this.fonts.Eyebrow.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSecondary))
            ImGui.TextUnformatted(text.ToUpperInvariant());
    }

    // A centered, square-bordered icon badge (age gate shield, onboarding welcome mark). Draws at the
    // current cursor, centered within `fullWidth`, and advances the cursor past the badge.
    public void CenteredFramedIcon(float fullWidth, string glyph, float box)
    {
        var pos = ImGui.GetCursorScreenPos();
        var min = new Vector2(pos.X + ((fullWidth - box) * 0.5f), pos.Y);
        var max = min + new Vector2(box, box);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRect(min, max, Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);
        var gs = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(drawList, this.fonts.Icon, min + ((new Vector2(box, box) - gs) * 0.5f), this.theme.Accent.U32(), glyph);
        ImGui.Dummy(new Vector2(fullWidth, box));
    }

    // A square header icon button matching the main window's title-bar chrome: a faint hover fill and a
    // muted glyph that brightens on hover. `glyph` is a FontAwesome icon string. Positioned in screen
    // space at topLeft, so a screen drawing its own header (minimize, overflow) can place it precisely.
    public bool HeaderIconButton(ImDrawListPtr drawList, string id, string glyph, Vector2 topLeft, float size)
    {
        ImGui.SetCursorScreenPos(topLeft);
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();
        var min = ImGui.GetItemRectMin();
        if (hovered)
            drawList.AddRectFilled(min, min + new Vector2(size, size), Palette.WithAlpha(Palette.White, 0.06f).U32(), Ui.Px(8f));
        var gs = Ui.Measure(this.fonts.Icon, glyph);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(min.X + ((size - gs.X) * 0.5f), min.Y + ((size - gs.Y) * 0.5f)), (hovered ? Palette.TextSecondary : Palette.TextMuted).U32(), glyph);
        return clicked;
    }

    // Editorial primary CTA: a cream (ink) fill with paper-dark text, matching the drawn CTAs on the
    // profile and filter screens. Dims slightly on hover.
    public bool PrimaryButton(string id, string label, float width = 0f)
        => this.FilledButton(id, label, width, Palette.TextPrimary, Palette.WithAlpha(Palette.TextPrimary, 0.88f), Palette.Paper);

    public bool DangerButton(string id, string label, float width = 0f)
        => this.FilledButton(id, label, width, Palette.DangerFill, Palette.Danger, Palette.White);

    public bool SecondaryButton(string id, string label, float width = 0f)
    {
        var height = Ui.Px(38f);
        var w = width <= 0f ? ImGui.GetContentRegionAvail().X : width;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, height));
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRect(pos, pos + new Vector2(w, height),
            (hovered ? Palette.TextMuted : Palette.Border).U32(), 0f, ImDrawFlags.None, 1f);
        var textSize = Ui.Measure(this.fonts.Body, label);
        Ui.TextAt(drawList, this.fonts.Body,
            pos + new Vector2((w - textSize.X) * 0.5f, (height - textSize.Y) * 0.5f),
            Palette.TextSecondary.U32(), label);
        return clicked;
    }

    // Pill toggle (the switch itself stays round, like the presence dots). On: ink track, paper knob.
    // Off: surface track with a strong hairline, muted knob. Returns the next value, flipped on click.
    public bool Toggle(string id, bool value)
    {
        var width = Ui.Px(36f);
        var height = Ui.Px(20f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(width, height));
        var drawList = ImGui.GetWindowDrawList();

        if (value)
        {
            drawList.AddRectFilled(pos, pos + new Vector2(width, height), Palette.TextPrimary.U32(), height * 0.5f);
        }
        else
        {
            drawList.AddRectFilled(pos, pos + new Vector2(width, height), Palette.Surface2.U32(), height * 0.5f);
            drawList.AddRect(pos, pos + new Vector2(width, height), Palette.BorderStrong.U32(), height * 0.5f, ImDrawFlags.None, 1f);
        }

        var knobRadius = (height * 0.5f) - Ui.Px(2f);
        var knobX = value
            ? pos.X + width - knobRadius - Ui.Px(2f)
            : pos.X + knobRadius + Ui.Px(2f);
        var knobColor = value ? Palette.Paper : Palette.TextMuted;
        drawList.AddCircleFilled(new Vector2(knobX, pos.Y + (height * 0.5f)), knobRadius, knobColor.U32(), 16);

        return clicked ? !value : value;
    }

    // Square checkbox with a drawn tick. Returns the next value, flipped if clicked this frame.
    public bool Checkbox(string id, bool value)
    {
        var size = Ui.Px(18f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var drawList = ImGui.GetWindowDrawList();
        var max = pos + new Vector2(size, size);

        if (value)
        {
            drawList.AddRectFilled(pos, max, Palette.TextPrimary.U32());
            var tick = Palette.Paper.U32();
            var a = pos + new Vector2(size * 0.22f, size * 0.52f);
            var b = pos + new Vector2(size * 0.42f, size * 0.72f);
            var c = pos + new Vector2(size * 0.78f, size * 0.30f);
            drawList.AddLine(a, b, tick, Ui.Px(2f));
            drawList.AddLine(b, c, tick, Ui.Px(2f));
        }
        else
        {
            drawList.AddRect(pos, max, Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);
        }

        return clicked ? !value : value;
    }

    // Segmented single select (for example World and DC). Returns the selected index.
    public int Segmented(string id, IReadOnlyList<string> options, int selected, float width = 0f)
    {
        var height = Ui.Px(34f);
        var w = width <= 0f ? ImGui.GetContentRegionAvail().X : width;
        var rounding = 0f;
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRect(pos, pos + new Vector2(w, height), Palette.Border.U32(), rounding, ImDrawFlags.None, 1f);

        var cell = w / options.Count;
        var result = selected;
        for (var i = 0; i < options.Count; i++)
        {
            var cellX = pos.X + (i * cell);
            ImGui.SetCursorScreenPos(new Vector2(cellX, pos.Y));
            if (ImGui.InvisibleButton($"{id}_{i}", new Vector2(cell, height)))
                result = i;

            var isSelected = i == selected;
            if (isSelected)
                drawList.AddRectFilled(
                    new Vector2(cellX, pos.Y), new Vector2(cellX + cell, pos.Y + height),
                    this.theme.AccentDeep.U32(), rounding);

            var textSize = Ui.Measure(this.fonts.Caption, options[i]);
            var color = isSelected ? this.theme.OnAccent : Palette.TextSecondary;
            Ui.TextAt(drawList, this.fonts.Caption,
                new Vector2(cellX + ((cell - textSize.X) * 0.5f), pos.Y + ((height - textSize.Y) * 0.5f)),
                color.U32(), options[i]);
        }

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(w, height));
        return result;
    }

    // The onboarding step indicator: a row of segments, the first `current` of which are filled.
    public void ProgressSegments(int current, int total, float width = 0f)
    {
        var height = Ui.Px(3f);
        var gap = Ui.Px(3f);
        var w = width <= 0f ? ImGui.GetContentRegionAvail().X : width;
        var segment = (w - (gap * (total - 1))) / total;
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        for (var i = 0; i < total; i++)
        {
            var x = pos.X + (i * (segment + gap));
            var color = i < current ? this.theme.Secondary.Base : Palette.Surface2;
            drawList.AddRectFilled(new Vector2(x, pos.Y), new Vector2(x + segment, pos.Y + height), color.U32(), height * 0.5f);
        }

        ImGui.Dummy(new Vector2(w, height));
    }

    // Placeholder portrait tile (surface fill with a centered initial) until real photos load.
    public void PhotoTile(string initial, Vector2 size, float rounding)
    {
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, pos + size, Palette.Surface2.U32(), rounding);
        var textSize = Ui.Measure(this.fonts.Title, initial);
        Ui.TextAt(drawList, this.fonts.Title,
            pos + new Vector2((size.X - textSize.X) * 0.5f, (size.Y - textSize.Y) * 0.5f),
            Palette.TextMuted.U32(), initial);
        ImGui.Dummy(size);
    }

    // Wrapping chip row for single or multi select. `selected` reports whether index i is on;
    // returns the clicked index, or -1 if nothing was clicked this frame.
    public int ChipFlow(string idPrefix, IReadOnlyList<string> labels, Func<int, bool> selected, float maxWidth, bool showCheck = true)
    {
        var clicked = -1;
        var spacing = Ui.Px(6f);
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(spacing, spacing)))
        {
            var lineWidth = 0f;
            for (var i = 0; i < labels.Count; i++)
            {
                // Mirrors Chip's geometry: LabelSmall text, 12px side padding, 14px check when selected.
                var chipWidth = Ui.Measure(this.fonts.LabelSmall, labels[i]).X + (Ui.Px(12f) * 2f) + (selected(i) && showCheck ? Ui.Px(14f) : 0f);
                if (i > 0)
                {
                    if (lineWidth + spacing + chipWidth <= maxWidth)
                    {
                        ImGui.SameLine(0f, spacing);
                        lineWidth += spacing + chipWidth;
                    }
                    else
                    {
                        lineWidth = chipWidth;
                    }
                }
                else
                {
                    lineWidth = chipWidth;
                }

                if (this.Chip($"{idPrefix}{i}", labels[i], selected(i), showCheck))
                    clicked = i;
            }
        }

        return clicked;
    }

    // Boxed numeric stepper matching the filter's age cell: a hairline box with minus at the left edge,
    // a typeable serif value centered, and plus at the right edge. Type a number directly (faster than
    // clicking a long way) or step with the edge controls, which dim at the range limits.
    public int Stepper(string id, int value, int min, int max, float width = 0f)
    {
        var w = width <= 0f ? Ui.Px(160f) : width;
        var height = Ui.Px(44f);
        var box = Ui.Px(40f);
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRect(pos, pos + new Vector2(w, height), Palette.Border.U32(), 0f, ImDrawFlags.None, 1f);

        // Editable centre first, so a blur caused by clicking an edge control commits before the step
        // applies to the freshly committed value.
        var result = this.SerifNumberField(id, value, min, max, pos, w, height);

        if (this.StepGlyph(id + "_minus", pos, box, height, FontAwesomeIcon.Minus, result > min))
            result = Math.Max(min, result - 1);
        if (this.StepGlyph(id + "_plus", new Vector2((pos.X + w) - box, pos.Y), box, height, FontAwesomeIcon.Plus, result < max))
            result = Math.Min(max, result + 1);

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(w, height));
        return result;
    }

    // A borderless, centred, typeable serif number filling [pos, pos+(width,height)]. Type to edit; the
    // value commits (clamped) on blur/enter. Shares the stepper edit state so only one is active at once.
    // The buffer holds the in-progress text so an immediate-mode resync can't clobber partial input.
    public int SerifNumberField(string id, int value, int min, int max, Vector2 pos, float width, float height)
    {
        var result = value;
        var editing = this.stepperEditId == id;
        var digits = max.ToString().Length;
        var text = editing ? this.stepperEditBuffer : value.ToString();
        var fieldWidth = Ui.Measure(this.fonts.SerifName, new string('0', digits)).X + Ui.Px(14f);
        var framePadY = (height - Ui.Measure(this.fonts.SerifName, "0").Y) * 0.5f;

        ImGui.SetCursorScreenPos(new Vector2(pos.X + ((width - fieldWidth) * 0.5f), pos.Y));
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.FrameBgHovered, Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.FrameBgActive, Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(Ui.Px(7f), framePadY)))
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 0f))
        using (this.fonts.SerifName.Push())
        {
            ImGui.SetNextItemWidth(fieldWidth);
            var numFlags = ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.AutoSelectAll;
            ImGui.InputTextWithHint(id + "_num", string.Empty, ref text, 8, numFlags);
            if (ImGui.IsItemActivated())
            {
                this.stepperEditId = id;
                this.stepperEditBuffer = value.ToString();
            }
            else if (editing && ImGui.IsItemActive())
            {
                this.stepperEditBuffer = text;
            }

            if (editing && ImGui.IsItemDeactivated())
            {
                if (int.TryParse(this.stepperEditBuffer, out var typed))
                    result = Math.Clamp(typed, min, max);
                this.stepperEditId = null;
            }
        }

        return result;
    }

    private bool StepGlyph(string id, Vector2 pos, float box, float height, FontAwesomeIcon icon, bool enabled)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, new Vector2(box, height)) && enabled;
        var hovered = ImGui.IsItemHovered();
        var glyph = icon.ToIconString();
        var glyphSize = Ui.Measure(this.fonts.Icon, glyph);
        var color = !enabled ? Palette.WithAlpha(Palette.TextMuted, 0.3f) : (hovered ? Palette.TextPrimary : Palette.TextMuted);
        Ui.TextAt(ImGui.GetWindowDrawList(), this.fonts.Icon, pos + ((new Vector2(box, height) - glyphSize) * 0.5f), color.U32(), glyph);
        return clicked;
    }

    // Horizontal slider. Click or drag anywhere along the track to set the value; returns the next
    // value clamped to [min, max]. The filled portion uses the accent so it recolors with the theme.
    public int Slider(string id, int value, int min, int max, float width = 0f)
    {
        var w = width <= 0f ? ImGui.GetContentRegionAvail().X : width;
        var rowHeight = Ui.Px(22f);
        var track = Ui.Px(4f);
        var knobRadius = Ui.Px(8f);
        var pad = knobRadius;   // keep the knob fully inside the row at either end

        var pos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, new Vector2(w, rowHeight));
        var active = ImGui.IsItemActive();
        var drawList = ImGui.GetWindowDrawList();

        var trackMin = new Vector2(pos.X + pad, pos.Y + ((rowHeight - track) * 0.5f));
        var trackMax = new Vector2(pos.X + w - pad, trackMin.Y + track);
        var span = trackMax.X - trackMin.X;

        var result = value;
        if (active && span > 0f)
        {
            var t = Math.Clamp((ImGui.GetIO().MousePos.X - trackMin.X) / span, 0f, 1f);
            result = min + (int)MathF.Round(t * (max - min));
        }

        result = Math.Clamp(result, min, max);
        var frac = max > min ? (float)(result - min) / (max - min) : 0f;
        var knobX = trackMin.X + (span * frac);
        var knobY = pos.Y + (rowHeight * 0.5f);

        drawList.AddRectFilled(trackMin, trackMax, Palette.Surface2.U32(), track * 0.5f);
        drawList.AddRectFilled(trackMin, new Vector2(knobX, trackMax.Y), this.theme.Secondary.Base.U32(), track * 0.5f);
        drawList.AddCircleFilled(new Vector2(knobX, knobY), knobRadius, Palette.White.U32(), 16);
        drawList.AddCircle(new Vector2(knobX, knobY), knobRadius, Palette.Border.U32(), 16, 1f);

        return result;
    }

    // A thin filled meter, for example passphrase strength. Fraction is clamped to 0..1.
    public void Meter(float fraction)
    {
        var height = Ui.Px(4f);
        var w = ImGui.GetContentRegionAvail().X;
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, pos + new Vector2(w, height), Palette.Surface2.U32(), height * 0.5f);
        drawList.AddRectFilled(pos, pos + new Vector2(w * Math.Clamp(fraction, 0f, 1f), height), this.theme.Secondary.Base.U32(), height * 0.5f);
        ImGui.Dummy(new Vector2(w, height));
    }

    public void TextField(string id, ref string value, string hint, float width)
    {
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Palette.Surface2))
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 0f))
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(Ui.Px(12f), Ui.Px(10f))))
        using (this.fonts.Body.Push())
        {
            ImGui.SetNextItemWidth(width);
            ImGui.InputTextWithHint(id, hint, ref value);
        }
    }

    // Chat composer. Multiline so it can hold line breaks. The Enter/Shift split keys off the Shift
    // modifier (which stays readable even while the field owns the keyboard): while Shift is held the
    // field is a plain multiline, so Enter inserts a native line break and keeps focus; otherwise Enter
    // submits (EnterReturnsTrue) and Ctrl+Enter is a line break. Multiline has no hint overload, so the
    // placeholder is drawn when empty. Pass refocus=true to re-focus the field (e.g. just after a send).
    // Returns true when the user pressed Enter to send.
    public bool ComposerField(string id, ref string value, string hint, float width, float height, bool refocus)
    {
        var shift = ImGui.GetIO().KeyShift;
        var empty = value.Length == 0;

        // Transparent frame: the chat composer draws a single surface bar behind the field, the attach
        // icon and the send square, so the input itself must not paint its own background in any state.
        var clear = new Vector4(0f, 0f, 0f, 0f);
        bool submitted;
        using (ImRaii.PushColor(ImGuiCol.FrameBg, clear))
        using (ImRaii.PushColor(ImGuiCol.FrameBgHovered, clear))
        using (ImRaii.PushColor(ImGuiCol.FrameBgActive, clear))
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 0f))
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(Ui.Px(4f), Ui.Px(10f))))
        using (this.fonts.Body.Push())
        {
            if (refocus)
                ImGui.SetKeyboardFocusHere();   // focus the field we are about to draw
            var flags = shift
                ? ImGuiInputTextFlags.None
                : ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CtrlEnterForNewLine;
            submitted = ImGui.InputTextMultiline(id, ref value, 2000, new Vector2(width, height), flags);
            if (empty)
            {
                var min = ImGui.GetItemRectMin();
                Ui.TextAt(ImGui.GetWindowDrawList(), this.fonts.Body, new Vector2(min.X + Ui.Px(4f), min.Y + Ui.Px(10f)), Palette.TextMuted.U32(), hint);
            }
        }

        // With Shift held the multiline returns true on the line-break edit too, so only treat a
        // submit (Shift up, EnterReturnsTrue) as a send.
        return submitted && !shift;
    }

    public void PasswordField(string id, ref string value, float width, string hint = "")
    {
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Palette.Surface2))
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 0f))
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(Ui.Px(12f), Ui.Px(10f))))
        using (this.fonts.Body.Push())
        {
            ImGui.SetNextItemWidth(width);
            // The managed ref-string overload does not take flags, so this is not masked yet.
            // Masking moves in with the real key handling in phase C.
            ImGui.InputTextWithHint(id, hint, ref value);
        }
    }

    // Masked passphrase field with an optional reveal. Unlike PasswordField this uses the flagged
    // overload so the characters are dotted out unless `reveal` is set. Returns true when Enter is
    // pressed, so the caller can submit on Enter.
    public bool MaskedField(string id, ref string value, float width, bool reveal)
    {
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Palette.Surface2))
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 0f))
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(Ui.Px(12f), Ui.Px(10f))))
        using (this.fonts.Body.Push())
        {
            ImGui.SetNextItemWidth(width);
            var flags = ImGuiInputTextFlags.EnterReturnsTrue;
            if (!reveal)
                flags |= ImGuiInputTextFlags.Password;
            return ImGui.InputTextWithHint(id, string.Empty, ref value, 256, flags);
        }
    }

    // Centered empty state: an icon in a soft circle, a headline, and a one-line body. The caller
    // draws the CTA below, centered against the same container width.
    public void EmptyState(string iconGlyph, string headline, string body, float containerWidth)
    {
        var localStart = ImGui.GetCursorPos();
        var start = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var circle = Ui.Px(72f);
        var centerX = start.X + (containerWidth * 0.5f);

        var circleCenter = new Vector2(centerX, start.Y + (circle * 0.5f));
        drawList.AddCircleFilled(circleCenter, circle * 0.5f, this.theme.AccentTint.U32(), 32);
        var glyphSize = Ui.Measure(this.fonts.Icon, iconGlyph);
        Ui.TextAt(drawList, this.fonts.Icon, new Vector2(centerX - (glyphSize.X * 0.5f), circleCenter.Y - (glyphSize.Y * 0.5f)), this.theme.AccentText.U32(), iconGlyph);

        var headlineY = start.Y + circle + Ui.Px(16f);
        var headlineSize = Ui.Measure(this.fonts.Title, headline);
        Ui.TextAt(drawList, this.fonts.Title, new Vector2(centerX - (headlineSize.X * 0.5f), headlineY), Palette.TextPrimary.U32(), headline);

        var bodyY = headlineY + headlineSize.Y + Ui.Px(6f);
        var bodySize = Ui.Measure(this.fonts.Caption, body);
        Ui.TextAt(drawList, this.fonts.Caption, new Vector2(centerX - (bodySize.X * 0.5f), bodyY), Palette.TextSecondary.U32(), body);

        var bottom = bodyY + bodySize.Y + Ui.Px(16f);
        ImGui.SetCursorPos(new Vector2(localStart.X, localStart.Y + (bottom - start.Y)));
    }

    private bool FilledButton(string id, string label, float width, Vector4 fill, Vector4 hoverFill, Vector4 textColor)
    {
        var height = Ui.Px(38f);
        var w = width <= 0f ? ImGui.GetContentRegionAvail().X : width;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, height));
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(pos, pos + new Vector2(w, height), (hovered ? hoverFill : fill).U32());
        var textSize = Ui.Measure(this.fonts.Body, label);
        Ui.TextAt(drawList, this.fonts.Body,
            pos + new Vector2((w - textSize.X) * 0.5f, (height - textSize.Y) * 0.5f),
            textColor.U32(), label);
        return clicked;
    }
}
