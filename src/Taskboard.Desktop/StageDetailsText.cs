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
    internal string Text { get; }
    internal bool Ellipsize { get; }
    internal double LineHeight => FontSize * FontFamily.LineSpacing;
    private readonly double maximumHeight;
    private readonly bool reserveJumpSpace;
    private FormattedText? formatted;
    private double cachedWidth = double.NaN;
    private double cachedDpi = double.NaN;
    internal double[] LineWidths { get; private set; } = [];
    internal int DisplayedLines { get; private set; }
    internal StageDetailsText(string text, double fontSize, Brush foreground, bool ellipsize, double maximumHeight, bool reserveJumpSpace = true)
    {
        Text = text; FontSize = fontSize; Foreground = foreground;
        Ellipsize = ellipsize; this.maximumHeight = maximumHeight; this.reserveJumpSpace = reserveJumpSpace;
        Tag = "TaskStageDetails"; Focusable = false; IsHitTestVisible = false;
        System.Windows.Automation.AutomationProperties.SetName(this, text);
    }
    private FormattedText Format(string value, double width, double pixelsPerDip) => new(value, CultureInfo.CurrentUICulture, FlowDirection,
        new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground, pixelsPerDip)
        { MaxTextWidth = width, LineHeight = LineHeight, Trimming = TextTrimming.None };
    private string EllipsisCandidateText(double width, int limit)
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
        return Text[..cut].TrimEnd() + "…";
    }
    private void LayoutText(double width)
    {
        width = Math.Max(.1, double.IsFinite(width) ? width : 400);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (formatted is not null && Math.Abs(cachedWidth - width) < .05 && Math.Abs(cachedDpi - dpi) < .001) return;
        cachedWidth = width; cachedDpi = dpi;

        var limit = Ellipsize ? Math.Max(2, (int)Math.Floor(maximumHeight / LineHeight)) : int.MaxValue;
        var source = Ellipsize ? EllipsisCandidateText(width, limit) : Text;
        var candidate = Format(source, width, dpi);
        var lines = Math.Min(limit, Math.Max(2, (int)Math.Ceiling(candidate.Height / LineHeight - .001)));
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
