using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Taskboard.Desktop;

internal sealed class OrbMenuRenderer : ToolStripProfessionalRenderer
{
    internal static void Fit(ContextMenuStrip menu)
    {
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
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    { if (e.Item.Name != "BallEdgeAutoHide") base.OnRenderItemCheck(e); }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
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
        using var track = new SolidBrush(item.Checked ? Color.FromArgb(71, 137, 131) : Color.FromArgb(143, 150, 157));
        using var dot = new SolidBrush(Color.White); e.Graphics.FillPath(track, path);
        var inset = 2 * scale; var diameter = height - 2 * inset;
        e.Graphics.FillEllipse(dot, item.Checked ? x + width - diameter - inset : x + inset, y + inset, diameter, diameter);
        e.Graphics.SmoothingMode = previous;
    }
}
