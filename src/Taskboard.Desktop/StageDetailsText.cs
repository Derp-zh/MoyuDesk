using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace Taskboard.Desktop;

// The text engine accepts a width for each line. Only the final visible line
// yields to the jump button; preceding lines keep the full text rectangle.
internal sealed class StageDetailsText : Control
{
    internal const double BubbleWidth = 24;
    internal const double BubbleGap = 4;
    internal const double JumpGap = 4;

    internal string Text { get; }
    internal bool Ellipsize { get; }
    internal bool AdaptiveEllipsize { get; }
    internal bool UserExpanded { get; }
    internal bool InteractiveBubble { get; }
    internal double LineHeight => FontSize * FontFamily.LineSpacing;
    private readonly double maximumHeight;
    private readonly bool reserveJumpSpace;
    private FormattedText? formatted;
    private double cachedWidth = double.NaN;
    private double cachedDpi = double.NaN;
    internal double[] LineWidths { get; private set; } = [];
    internal int DisplayedLines { get; private set; }
    internal int TotalLines { get; private set; }
    internal bool IsTruncated { get; private set; }
    internal bool NeedsBubble { get; private set; }
    internal Rect BubbleBounds { get; private set; }

    internal static int ComputeAdaptiveVisibleLines(int totalLines)
    {
        if (totalLines <= 4) return totalLines;
        var visible = Math.Clamp((int)Math.Round(totalLines * 0.4, MidpointRounding.AwayFromZero), 3, 10);
        return Math.Min(visible, totalLines - 1);
    }

    internal StageDetailsText(string text, double fontSize, Brush foreground, bool ellipsize, double maximumHeight,
        bool reserveJumpSpace = true, bool adaptiveEllipsize = false, bool userExpanded = false, bool interactiveBubble = false)
    {
        Text = text; FontSize = fontSize; Foreground = foreground;
        Ellipsize = ellipsize; this.maximumHeight = maximumHeight; this.reserveJumpSpace = reserveJumpSpace;
        AdaptiveEllipsize = adaptiveEllipsize; UserExpanded = userExpanded; InteractiveBubble = interactiveBubble;
        Tag = "TaskStageDetails"; Focusable = false; IsHitTestVisible = false;
        System.Windows.Automation.AutomationProperties.SetName(this, text);
    }
    private FormattedText Format(string value, double width, double pixelsPerDip) => new(value, CultureInfo.CurrentUICulture, FlowDirection,
        new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground, pixelsPerDip)
        { MaxTextWidth = width, LineHeight = LineHeight, Trimming = TextTrimming.None };
    private string EllipsisCandidateText(double width, int limit, bool appendEllipsis = true)
    {
        if (string.IsNullOrEmpty(Text)) return Text;
        var maxLines = limit >= int.MaxValue - 2 ? int.MaxValue : limit + 1;
        var minCharWidth = Math.Max(2.0, FontSize * 0.22);
        var maxChars = limit >= int.MaxValue - 2
            ? int.MaxValue
            : Math.Max(512, (int)Math.Ceiling(width / minCharWidth) * (limit + 2));
        if (Text.Length <= maxChars && maxLines == int.MaxValue) return Text;

        var scanLimit = Math.Min(Text.Length, maxChars);
        var linesSeen = 0;
        var cut = Text.Length;
        for (var i = 0; i < scanLimit; i++)
        {
            var ch = Text[i];
            if (ch == '\r')
            {
                linesSeen++;
                if (linesSeen >= maxLines) { cut = i; break; }
                if (i + 1 < scanLimit && Text[i + 1] == '\n') i++;
            }
            else if (ch == '\n')
            {
                linesSeen++;
                if (linesSeen >= maxLines) { cut = i; break; }
            }
        }
        if (cut == Text.Length && Text.Length > maxChars) cut = maxChars;
        if (cut >= Text.Length) return Text;
        while (cut > 0 && (char.IsLowSurrogate(Text[cut]) ||
            CharUnicodeInfo.GetUnicodeCategory(Text[cut]) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))
            cut--;
        return appendEllipsis ? Text[..cut].TrimEnd() + "…" : Text[..cut];
    }
    private void LayoutText(double width)
    {
        width = Math.Max(.1, double.IsFinite(width) ? width : 400);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (formatted is not null && Math.Abs(cachedWidth - width) < .05 && Math.Abs(cachedDpi - dpi) < .001) return;
        cachedWidth = width; cachedDpi = dpi;

        if (AdaptiveEllipsize && Ellipsize)
        {
            LayoutAdaptive(width, dpi);
            return;
        }

        IsTruncated = false;
        NeedsBubble = false;
        BubbleBounds = Rect.Empty;
        var limit = Ellipsize ? Math.Max(2, (int)Math.Floor(maximumHeight / LineHeight)) : int.MaxValue;
        var source = Ellipsize ? EllipsisCandidateText(width, limit) : Text;
        var candidate = Format(source, width, dpi);
        var measuredLines = Math.Max(1, (int)Math.Ceiling(candidate.Height / LineHeight - .001));
        TotalLines = measuredLines;
        var lines = Math.Min(limit, Math.Max(2, measuredLines));
        if (!reserveJumpSpace)
        {
            if (Ellipsize) { candidate.MaxLineCount = lines; candidate.Trimming = TextTrimming.CharacterEllipsis; }
            var widths = new double[lines];
            Array.Fill(widths, width);
            formatted = candidate; DisplayedLines = lines; LineWidths = widths; return;
        }
        // An untrimmed final line may spill. Move the button to the new last
        // line and recompute, leaving all prior lines at their original width.
        var firstPass = true;
        while (true)
        {
            if (!firstPass) candidate = Format(source, width, dpi);
            firstPass = false;
            var widths = new double[lines + 1];
            Array.Fill(widths, width);
            widths[lines - 1] = Math.Max(.1, width - LineHeight - 3);
            candidate.SetMaxTextWidths(widths);
            var needed = Math.Max(2, (int)Math.Ceiling(candidate.Height / LineHeight - .001));
            if (needed > lines && lines < limit) { lines = Math.Min(limit, needed); continue; }
            if (Ellipsize && needed > lines && source.IndexOfAny(['\r', '\n']) >= 0)
                candidate = TrimParagraphs(source, candidate, width, dpi, widths, lines);
            if (Ellipsize) { candidate.MaxLineCount = lines; candidate.Trimming = TextTrimming.CharacterEllipsis; }
            formatted = candidate; DisplayedLines = lines; LineWidths = widths[..lines]; return;
        }
    }
    private void LayoutAdaptive(double width, double dpi)
    {
        var jumpWidth = reserveJumpSpace ? LineHeight + 3 : 0;
        var bubbleHeight = Math.Max(16, Math.Round(LineHeight - 2));
        var bubbleReserve = InteractiveBubble ? BubbleWidth + BubbleGap + (reserveJumpSpace ? JumpGap : 0) : 0;
        var maxBubbleRight = Math.Max(0, width - jumpWidth - (reserveJumpSpace ? JumpGap : 0));

        // For collapsed mode, cap measurement at 28 lines: >= 25 lines always maps to 10 visible lines.
        var measureSource = UserExpanded ? Text : EllipsisCandidateText(width, 28, appendEllipsis: false);
        var probe = Format(measureSource, width, dpi);
        var measuredLines = string.IsNullOrEmpty(measureSource) ? 1 : Math.Max(1, (int)Math.Ceiling(probe.Height / LineHeight - .001));
        if (!UserExpanded && measureSource.Length < Text.Length) measuredLines = Math.Max(measuredLines, 26);
        TotalLines = measuredLines;

        if (TotalLines <= 4)
        {
            IsTruncated = false;
            NeedsBubble = false;
            BubbleBounds = Rect.Empty;
            LayoutFullWithoutBubble(Text, width, dpi, jumpWidth);
            return;
        }

        if (UserExpanded)
        {
            IsTruncated = false;
            NeedsBubble = InteractiveBubble;
            var reserveLast = jumpWidth + bubbleReserve;
            var lines = Math.Max(2, TotalLines);
            var candidate = Format(Text, width, dpi);
            double[] widths;
            while (true)
            {
                widths = new double[lines + 1];
                Array.Fill(widths, width);
                if (reserveLast > 0) widths[lines - 1] = Math.Max(.1, width - reserveLast);
                candidate.SetMaxTextWidths(widths);
                var needed = Math.Max(2, (int)Math.Ceiling(candidate.Height / LineHeight - .001));
                if (needed > lines) { lines = needed; candidate = Format(Text, width, dpi); continue; }
                break;
            }
            if (NeedsBubble)
            {
                var (bounds, extraLine) = ComputeBubbleBounds(candidate, Text, width, maxBubbleRight, bubbleHeight, lines, allowExtendLine: true);
                if (extraLine)
                {
                    lines++;
                    Array.Resize(ref widths, lines);
                    widths[lines - 1] = Math.Max(.1, width - reserveLast);
                }
                BubbleBounds = bounds;
            }
            else BubbleBounds = Rect.Empty;
            formatted = candidate;
            DisplayedLines = lines;
            LineWidths = widths[..lines];
            return;
        }

        // Collapsed adaptive mode: TotalLines > 4 && !UserExpanded
        var visibleLimit = ComputeAdaptiveVisibleLines(TotalLines);
        IsTruncated = true;
        NeedsBubble = InteractiveBubble;
        var source = EllipsisCandidateText(width, visibleLimit, appendEllipsis: false);
        var reserveTail = jumpWidth + bubbleReserve;
        var collapsedWidths = new double[visibleLimit + 1];
        Array.Fill(collapsedWidths, width);
        if (reserveTail > 0) collapsedWidths[visibleLimit - 1] = Math.Max(.1, width - reserveTail);

        if (!InteractiveBubble)
        {
            var raw = Format(source, width, dpi);
            raw.SetMaxTextWidths(collapsedWidths);
            var needed = Math.Max(2, (int)Math.Ceiling(raw.Height / LineHeight - .001));
            if (needed > visibleLimit && source.IndexOfAny(['\r', '\n']) >= 0)
                raw = TrimParagraphs(source, raw, width, dpi, collapsedWidths, visibleLimit);
            raw.MaxLineCount = visibleLimit;
            raw.Trimming = TextTrimming.CharacterEllipsis;
            formatted = raw;
            DisplayedLines = visibleLimit;
            LineWidths = collapsedWidths[..visibleLimit];
            BubbleBounds = Rect.Empty;
            return;
        }

        var full = Format(source, width, dpi);
        full.SetMaxTextWidths(collapsedWidths);
        var (trimmedText, trimmedFormatted) = TrimToLinesWithoutEllipsis(source, full, width, dpi, collapsedWidths, visibleLimit);
        var (bubbleRect, _) = ComputeBubbleBounds(trimmedFormatted, trimmedText, width, maxBubbleRight, bubbleHeight, visibleLimit, allowExtendLine: false);
        BubbleBounds = bubbleRect;
        formatted = trimmedFormatted;
        DisplayedLines = visibleLimit;
        LineWidths = collapsedWidths[..visibleLimit];
    }
    private void LayoutFullWithoutBubble(string source, double width, double dpi, double jumpWidth)
    {
        var candidate = Format(source, width, dpi);
        var lines = Math.Max(2, (int)Math.Ceiling(candidate.Height / LineHeight - .001));
        if (jumpWidth <= 0)
        {
            var widths = new double[lines];
            Array.Fill(widths, width);
            formatted = candidate; DisplayedLines = lines; LineWidths = widths; return;
        }
        var firstPass = true;
        while (true)
        {
            if (!firstPass) candidate = Format(source, width, dpi);
            firstPass = false;
            var widths = new double[lines + 1];
            Array.Fill(widths, width);
            widths[lines - 1] = Math.Max(.1, width - jumpWidth);
            candidate.SetMaxTextWidths(widths);
            var needed = Math.Max(2, (int)Math.Ceiling(candidate.Height / LineHeight - .001));
            if (needed > lines) { lines = needed; continue; }
            formatted = candidate; DisplayedLines = lines; LineWidths = widths[..lines]; return;
        }
    }
    private (string Trimmed, FormattedText Result) TrimToLinesWithoutEllipsis(string source, FormattedText full, double width, double pixelsPerDip, double[] widths, int lines)
    {
        var limit = lines * LineHeight + .01;
        int low = 0, high = source.Length;
        while (low < high)
        {
            var middle = low + (high - low + 1) / 2;
            var highlight = full.BuildHighlightGeometry(new Point(), 0, middle);
            if (highlight is null || highlight.Bounds.Bottom <= limit) low = middle;
            else high = middle - 1;
        }
        var boundaries = StringInfo.ParseCombiningCharacters(source);
        var index = Array.BinarySearch(boundaries, low);
        if (index < 0) index = ~index - 1;
        var cut = low == source.Length ? low : boundaries[Math.Max(0, index)];
        while (true)
        {
            var slice = source[..cut].TrimEnd();
            var result = Format(slice, width, pixelsPerDip);
            result.SetMaxTextWidths(widths);
            if (result.Height <= limit || cut == 0) return (slice, result);
            index = Array.BinarySearch(boundaries, cut);
            if (index < 0) index = ~index;
            index--;
            cut = index >= 0 ? boundaries[index] : 0;
        }
    }
    private (Rect Bounds, bool ExtraLine) ComputeBubbleBounds(FormattedText target, string content, double width, double maxBubbleRight, double bubbleHeight, int lines, bool allowExtendLine)
    {
        var centerOffset = Math.Max(0, (LineHeight - bubbleHeight) / 2);
        var lastVisible = content.Length - 1;
        while (lastVisible >= 0 && char.IsWhiteSpace(content[lastVisible])) lastVisible--;
        if (lastVisible < 0)
            return (new Rect(0, centerOffset, BubbleWidth, bubbleHeight), false);

        var geom = target.BuildHighlightGeometry(new Point(), lastVisible, 1);
        if (geom is null || geom.IsEmpty())
            return (new Rect(0, Math.Max(0, (lines - 1) * LineHeight) + centerOffset, BubbleWidth, bubbleHeight), false);

        var lineIndex = Math.Max(0, (int)Math.Floor((geom.Bounds.Top + LineHeight * 0.25) / LineHeight));
        var lineTop = lineIndex * LineHeight;
        var x = geom.Bounds.Right + BubbleGap;
        var lineMaxRight = lineIndex == lines - 1 ? maxBubbleRight : width;
        if (x + BubbleWidth <= lineMaxRight + 0.5)
            return (new Rect(Math.Max(0, x), lineTop + centerOffset, BubbleWidth, bubbleHeight), false);

        var nextLineIndex = lineIndex + 1;
        if (nextLineIndex < lines)
            return (new Rect(0, nextLineIndex * LineHeight + centerOffset, BubbleWidth, bubbleHeight), false);

        if (allowExtendLine)
            return (new Rect(0, lines * LineHeight + centerOffset, BubbleWidth, bubbleHeight), true);

        var clampedX = Math.Max(0, maxBubbleRight - BubbleWidth);
        return (new Rect(clampedX, Math.Max(0, (lines - 1) * LineHeight) + centerOffset, BubbleWidth, bubbleHeight), false);
    }
    private FormattedText TrimParagraphs(string source, FormattedText full, double width, double pixelsPerDip, double[] widths, int lines)
    {
        // WPF may cut at a hard paragraph boundary without adding its normal
        // trailing ellipsis. Find the visible prefix using the same text layout,
        // then append a real marker, preserving whole Unicode graphemes.
        var limit = lines * LineHeight + .01;
        int low = 0, high = source.Length;
        while (low < high)
        {
            var middle = low + (high - low + 1) / 2;
            var highlight = full.BuildHighlightGeometry(new Point(), 0, middle);
            if (highlight is null || highlight.Bounds.Bottom <= limit) low = middle;
            else high = middle - 1;
        }
        var boundaries = StringInfo.ParseCombiningCharacters(source);
        var index = Array.BinarySearch(boundaries, low);
        if (index < 0) index = ~index - 1;
        var cut = low == source.Length ? low : boundaries[Math.Max(0, index)];
        while (true)
        {
            var result = Format(source[..cut].TrimEnd() + "…", width, pixelsPerDip); result.SetMaxTextWidths(widths);
            if (result.Height <= limit || cut == 0) return result;
            index = Array.BinarySearch(boundaries, cut); if (index < 0) index = ~index; index--;
            cut = index >= 0 ? boundaries[index] : 0;
        }
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        LayoutText(availableSize.Width);
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : formatted!.WidthIncludingTrailingWhitespace,
            DisplayedLines * LineHeight);
    }
    protected override Size ArrangeOverride(Size finalSize)
    { LayoutText(finalSize.Width); return finalSize; }
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (formatted is not null) drawingContext.DrawText(formatted, new Point());
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new DetailsPeer(this);
    private sealed class DetailsPeer(StageDetailsText owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(StageDetailsText);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
    }
}
