using Eikon.UI.Theme;

namespace Eikon.UI;

// Reusable themed widgets that match the SCREENS conventions. Each is drawn with the draw list so
// we keep pixel control, every color comes from the theme so the kit recolors with the accent, and
// text uses the shared type scale. Widgets that can repeat on a screen take an explicit id.
internal sealed class Kit
{
    private readonly ThemeService theme;
    private readonly UiFonts fonts;

    public Kit(ThemeService theme, UiFonts fonts)
    {
        this.theme = theme;
        this.fonts = fonts;
    }

    // A rounded pill. Selected pills use the accent tint with accent text; unselected pills use a
    // hairline outline with secondary text. Returns true on click.
    public bool Chip(string id, string label, bool selected)
    {
        var padX = Ui.Px(11f);
        var padY = Ui.Px(6f);
        var rounding = Ui.Px(8f);
        var textSize = Ui.Measure(this.fonts.Caption, label);
        var size = new Vector2(textSize.X + (padX * 2f), textSize.Y + (padY * 2f));

        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();

        if (selected)
        {
            drawList.AddRectFilled(pos, pos + size, this.theme.AccentTint.U32(), rounding);
            Ui.TextAt(drawList, this.fonts.Caption, pos + new Vector2(padX, padY), this.theme.AccentText.U32(), label);
        }
        else
        {
            var border = hovered ? Palette.TextMuted : Palette.Border;
            drawList.AddRect(pos, pos + size, border.U32(), rounding, ImDrawFlags.None, 1f);
            Ui.TextAt(drawList, this.fonts.Caption, pos + new Vector2(padX, padY), Palette.TextSecondary.U32(), label);
        }

        return clicked;
    }

    public void SectionLabel(string text)
    {
        using (this.fonts.Caption.Push())
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextMuted))
            ImGui.TextUnformatted(text);
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

    public bool PrimaryButton(string id, string label, float width = 0f)
        => this.FilledButton(id, label, width, this.theme.AccentDeep, this.theme.Accent, this.theme.OnAccent);

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
            (hovered ? Palette.TextMuted : Palette.Border).U32(), Ui.Px(10f), ImDrawFlags.None, 1f);
        var textSize = Ui.Measure(this.fonts.Body, label);
        Ui.TextAt(drawList, this.fonts.Body,
            pos + new Vector2((w - textSize.X) * 0.5f, (height - textSize.Y) * 0.5f),
            Palette.TextSecondary.U32(), label);
        return clicked;
    }

    // Pill toggle. Returns the next value, flipped if it was clicked this frame.
    public bool Toggle(string id, bool value)
    {
        var width = Ui.Px(38f);
        var height = Ui.Px(22f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(width, height));
        var drawList = ImGui.GetWindowDrawList();

        var track = value ? this.theme.AccentDeep : Palette.Rgb(0x2A3346);
        drawList.AddRectFilled(pos, pos + new Vector2(width, height), track.U32(), height * 0.5f);

        var knobRadius = (height * 0.5f) - Ui.Px(2f);
        var knobX = value
            ? pos.X + width - knobRadius - Ui.Px(2f)
            : pos.X + knobRadius + Ui.Px(2f);
        var knobColor = value ? Palette.White : Palette.Rgb(0x8A94A4);
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
            drawList.AddRectFilled(pos, max, this.theme.AccentDeep.U32(), Ui.Px(5f));
            var tick = this.theme.OnAccent.U32();
            var a = pos + new Vector2(size * 0.22f, size * 0.52f);
            var b = pos + new Vector2(size * 0.42f, size * 0.72f);
            var c = pos + new Vector2(size * 0.78f, size * 0.30f);
            drawList.AddLine(a, b, tick, Ui.Px(2f));
            drawList.AddLine(b, c, tick, Ui.Px(2f));
        }
        else
        {
            drawList.AddRect(pos, max, Palette.Border.U32(), Ui.Px(5f), ImDrawFlags.None, 1f);
        }

        return clicked ? !value : value;
    }

    // Segmented single select (for example World and DC). Returns the selected index.
    public int Segmented(string id, IReadOnlyList<string> options, int selected, float width = 0f)
    {
        var height = Ui.Px(34f);
        var w = width <= 0f ? ImGui.GetContentRegionAvail().X : width;
        var rounding = Ui.Px(8f);
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
            var color = i < current ? this.theme.Accent : Palette.Rgb(0x2A3346);
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
    public int ChipFlow(string idPrefix, IReadOnlyList<string> labels, Func<int, bool> selected, float maxWidth)
    {
        var clicked = -1;
        var spacing = Ui.Px(6f);
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(spacing, spacing)))
        {
            var lineWidth = 0f;
            for (var i = 0; i < labels.Count; i++)
            {
                var chipWidth = Ui.Measure(this.fonts.Caption, labels[i]).X + (Ui.Px(11f) * 2f);
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

                if (this.Chip($"{idPrefix}{i}", labels[i], selected(i)))
                    clicked = i;
            }
        }

        return clicked;
    }

    // Numeric stepper with minus and plus controls. Returns the next value, clamped to the range.
    public int Stepper(string id, int value, int min, int max)
    {
        var box = Ui.Px(32f);
        var gap = Ui.Px(16f);
        var numberWidth = Ui.Px(46f);
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var result = value;

        ImGui.SetCursorScreenPos(pos);
        if (ImGui.InvisibleButton(id + "_minus", new Vector2(box, box)))
            result = Math.Max(min, value - 1);
        this.StepperButton(drawList, pos, box, "-", ImGui.IsItemHovered());

        var numberX = pos.X + box + gap;
        var number = value.ToString();
        var numberSize = Ui.Measure(this.fonts.Title, number);
        Ui.TextAt(drawList, this.fonts.Title,
            new Vector2(numberX + ((numberWidth - numberSize.X) * 0.5f), pos.Y + ((box - numberSize.Y) * 0.5f)),
            Palette.TextPrimary.U32(), number);

        var plusPos = new Vector2(numberX + numberWidth + gap, pos.Y);
        ImGui.SetCursorScreenPos(plusPos);
        if (ImGui.InvisibleButton(id + "_plus", new Vector2(box, box)))
            result = Math.Min(max, value + 1);
        this.StepperButton(drawList, plusPos, box, "+", ImGui.IsItemHovered());

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2((box * 2f) + (gap * 2f) + numberWidth, box));
        return result;
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
        drawList.AddRectFilled(trackMin, new Vector2(knobX, trackMax.Y), this.theme.Accent.U32(), track * 0.5f);
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
        drawList.AddRectFilled(pos, pos + new Vector2(w * Math.Clamp(fraction, 0f, 1f), height), this.theme.Accent.U32(), height * 0.5f);
        ImGui.Dummy(new Vector2(w, height));
    }

    public void TextField(string id, ref string value, string hint, float width)
    {
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Palette.Surface2))
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, Ui.Px(10f)))
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

        bool submitted;
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Palette.Surface2))
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, Ui.Px(10f)))
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(Ui.Px(12f), Ui.Px(10f))))
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
                Ui.TextAt(ImGui.GetWindowDrawList(), this.fonts.Body, new Vector2(min.X + Ui.Px(12f), min.Y + Ui.Px(10f)), Palette.TextMuted.U32(), hint);
            }
        }

        // With Shift held the multiline returns true on the line-break edit too, so only treat a
        // submit (Shift up, EnterReturnsTrue) as a send.
        return submitted && !shift;
    }

    public void PasswordField(string id, ref string value, float width)
    {
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Palette.Surface2))
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, Ui.Px(10f)))
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(Ui.Px(12f), Ui.Px(10f))))
        using (this.fonts.Body.Push())
        {
            ImGui.SetNextItemWidth(width);
            // The managed ref-string overload does not take flags, so this is not masked yet.
            // Masking moves in with the real key handling in phase C.
            ImGui.InputTextWithHint(id, string.Empty, ref value);
        }
    }

    // Masked passphrase field with an optional reveal. Unlike PasswordField this uses the flagged
    // overload so the characters are dotted out unless `reveal` is set. Returns true when Enter is
    // pressed, so the caller can submit on Enter.
    public bool MaskedField(string id, ref string value, float width, bool reveal)
    {
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Palette.Surface2))
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextPrimary))
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, Ui.Px(10f)))
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

    private void StepperButton(ImDrawListPtr drawList, Vector2 pos, float size, string label, bool hovered)
    {
        drawList.AddRect(pos, pos + new Vector2(size, size),
            (hovered ? Palette.TextMuted : Palette.Border).U32(), size * 0.5f, ImDrawFlags.None, 1f);
        var textSize = Ui.Measure(this.fonts.Body, label);
        Ui.TextAt(drawList, this.fonts.Body,
            pos + new Vector2((size - textSize.X) * 0.5f, (size - textSize.Y) * 0.5f),
            Palette.TextSecondary.U32(), label);
    }

    private bool FilledButton(string id, string label, float width, Vector4 fill, Vector4 hoverFill, Vector4 textColor)
    {
        var height = Ui.Px(38f);
        var w = width <= 0f ? ImGui.GetContentRegionAvail().X : width;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, height));
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(pos, pos + new Vector2(w, height), (hovered ? hoverFill : fill).U32(), Ui.Px(10f));
        var textSize = Ui.Measure(this.fonts.Body, label);
        Ui.TextAt(drawList, this.fonts.Body,
            pos + new Vector2((w - textSize.X) * 0.5f, (height - textSize.Y) * 0.5f),
            textColor.U32(), label);
        return clicked;
    }
}
