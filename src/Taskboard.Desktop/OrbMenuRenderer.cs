using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Taskboard.Desktop;

internal sealed class OrbMenuRenderer : ToolStripProfessionalRenderer
{
    internal OrbMenuRenderer() : base(new ThemeColorTable())
    {
        RoundedEdges = false;
    }

    internal static void ApplyTheme(ToolStrip menu)
    {
        var p = ThemePalette.Current;
        menu.BackColor = p.MenuBackDrawingColor;
        menu.ForeColor = p.MenuTextDrawingColor;
        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = p.MenuBackDrawingColor;
            item.ForeColor = item.Enabled ? p.MenuTextDrawingColor : p.MenuDisabledDrawingColor;
            if (item is ToolStripDropDownItem { HasDropDownItems: true } drop)
            {
                ApplyTheme(drop.DropDown);
            }
        }
        menu.Invalidate();
    }

    internal static void Fit(ContextMenuStrip menu)
    {
        ApplyTheme(menu);
        var scale = Math.Max(menu.DeviceDpi, Native.GetDpiForWindow(menu.Handle)) / 96.0;
        var width = menu.Items.OfType<ToolStripMenuItem>().Where(x => x.Available).Select(item =>
        {
            var text = TextRenderer.MeasureText(item.Text, item.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
            var trailing = item.Name == "BallEdgeAutoHide" ? 26 + 7 + 10 : item.HasDropDownItems ? 14 + 10 : 10;
            return text + (8 + trailing) * scale + 3;
        }).DefaultIfEmpty(0).Max();
        // ToolStripDropDownMenu can enlarge its HWND for MinimumSize while its
        // rows retain their smaller preferred width, leaving a second right
        // gutter. Size the root and its rows together; child menus stay native.
        menu.SuspendLayout();
        try
        {
            foreach (var item in menu.Items.OfType<ToolStripMenuItem>()) item.AutoSize = true;
            menu.AutoSize = true;
            menu.MinimumSize = new Size((int)Math.Ceiling(width), 0);
        }
        finally { menu.ResumeLayout(true); }
        // Let the native uniform-row layout determine heights after font/DPI
        // changes. Individual preferred heights omit its normal row spacing.
        menu.PerformLayout();
        var preferred = menu.GetPreferredSize(Size.Empty);
        var nativeHeights = menu.Items.OfType<ToolStripMenuItem>().ToDictionary(item => item, item => item.Height);
        menu.SuspendLayout();
        try
        {
            menu.AutoSize = false;
            menu.Size = new Size((int)Math.Ceiling(width), preferred.Height);
            // DropDownMenu rows start at x=0; its left padding belongs to the
            // text layout and is not an extra item inset.
            var rowWidth = Math.Max(1, menu.ClientSize.Width - menu.Padding.Right);
            foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
            {
                item.AutoSize = false; item.Size = new Size(rowWidth, nativeHeights[item]);
            }
        }
        finally { menu.ResumeLayout(true); }
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var bg = new SolidBrush(ThemePalette.Current.MenuBackDrawingColor);
        e.Graphics.FillRectangle(bg, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var bounds = new Rectangle(0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        using var pen = new Pen(ThemePalette.Current.MenuBorderDrawingColor);
        e.Graphics.DrawRectangle(pen, bounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var rect = new Rectangle(3, 1, Math.Max(1, e.Item.Width - 6), Math.Max(1, e.Item.Height - 2));
        var color = e.Item.Selected && e.Item.Enabled
            ? ThemePalette.Current.MenuHighlightDrawingColor
            : ThemePalette.Current.MenuBackDrawingColor;
        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, rect);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.Height / 2;
        using var pen = new Pen(ThemePalette.Current.MenuBorderDrawingColor);
        e.Graphics.DrawLine(pen, 8, y, Math.Max(8, e.Item.Width - 8), y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled == false
            ? ThemePalette.Current.MenuDisabledDrawingColor
            : ThemePalette.Current.MenuTextDrawingColor;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    { if (e.Item.Name != "BallEdgeAutoHide") base.OnRenderItemCheck(e); }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var p = ThemePalette.Current;
        e.TextColor = e.Item.Enabled ? p.MenuTextDrawingColor : p.MenuDisabledDrawingColor;
        if (e.Item is not ToolStripMenuItem { Name: "BallEdgeAutoHide" } item) { base.OnRenderItemText(e); return; }
        var scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        var width = 26 * scale; var height = 14 * scale;
        var x = Math.Min(item.Width, e.ToolStrip?.ClientSize.Width ?? item.Width) - width - 10 * scale; var y = (item.Height - height) / 2;
        e.TextRectangle = new Rectangle(e.TextRectangle.Left, e.TextRectangle.Top,
            Math.Max(1, (int)(x - 7 * scale - e.TextRectangle.Left)), e.TextRectangle.Height);
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, e.TextColor,
            e.TextFormat | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        var previous = e.Graphics.SmoothingMode; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = new GraphicsPath(); path.AddArc(x, y, height, height, 90, 180); path.AddArc(x + width - height, y, height, height, 270, 180); path.CloseFigure();
        using var track = new SolidBrush(item.Checked ? p.SwitchTrackOnDrawingColor : p.SwitchTrackOffDrawingColor);
        using var dot = new SolidBrush(p.SwitchDotDrawingColor); e.Graphics.FillPath(track, path);
        var inset = 2 * scale; var diameter = height - 2 * inset;
        e.Graphics.FillEllipse(dot, item.Checked ? x + width - diameter - inset : x + inset, y + inset, diameter, diameter);
        e.Graphics.SmoothingMode = previous;
    }

    private sealed class ThemeColorTable : ProfessionalColorTable
    {
        internal ThemeColorTable() { UseSystemColors = false; }
        public override Color ToolStripDropDownBackground => ThemePalette.Current.MenuBackDrawingColor;
        public override Color MenuBorder => ThemePalette.Current.MenuBorderDrawingColor;
        public override Color MenuItemBorder => ThemePalette.Current.MenuHighlightDrawingColor;
        public override Color MenuItemSelected => ThemePalette.Current.MenuHighlightDrawingColor;
        public override Color MenuItemSelectedGradientBegin => ThemePalette.Current.MenuHighlightDrawingColor;
        public override Color MenuItemSelectedGradientEnd => ThemePalette.Current.MenuHighlightDrawingColor;
        public override Color ImageMarginGradientBegin => ThemePalette.Current.MenuBackDrawingColor;
        public override Color ImageMarginGradientMiddle => ThemePalette.Current.MenuBackDrawingColor;
        public override Color ImageMarginGradientEnd => ThemePalette.Current.MenuBackDrawingColor;
        public override Color SeparatorDark => ThemePalette.Current.MenuBorderDrawingColor;
        public override Color SeparatorLight => ThemePalette.Current.MenuBackDrawingColor;
    }
}
