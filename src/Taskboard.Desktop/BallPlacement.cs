using System;
using System.Windows;

namespace Taskboard.Desktop;

internal static class BallPlacement
{
    internal static bool ExpandLeft(Rect orb, Rect area, bool? previous = null)
    {
        var left = orb.Left - area.Left; var right = area.Right - orb.Right;
        if (left < 312 && right >= 312) return false;
        if (right < 312 && left >= 312) return true;
        var delta = orb.Left + orb.Width / 2 - (area.Left + area.Width / 2);
        return previous is bool known && Math.Abs(delta) < 32 ? known : delta >= 0;
    }
    internal static bool NearestLeft(Rect orb, Rect area, bool? previous = null)
    {
        var delta = orb.Left + orb.Width / 2 - (area.Left + area.Width / 2);
        return previous is bool known && Math.Abs(delta) < 24 ? known : delta < 0;
    }
    internal static Rect Vertical(Rect anchor, Size desired, Rect area, double gap = 8)
    {
        var width = Math.Max(1, Math.Min(desired.Width, area.Width));
        var up = Math.Max(0, anchor.Top - area.Top - gap); var down = Math.Max(0, area.Bottom - anchor.Bottom - gap);
        var below = anchor.Top + anchor.Height / 2 <= area.Top + area.Height / 2;
        if ((below ? down : up) < Math.Min(120, desired.Height)) below = down >= up;
        var height = Math.Max(1, Math.Min(desired.Height, below ? down : up));
        var top = below ? anchor.Bottom + gap : anchor.Top - gap - height;
        return new Rect(Math.Clamp(anchor.Left, area.Left, Math.Max(area.Left, area.Right - width)),
            Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - height)), width, height);
    }
    internal static Rect Menu(Rect orb, Size desired, Rect area, bool expandLeft, double gap = 8)
    {
        var width = Math.Min(desired.Width, area.Width); var height = Math.Min(desired.Height, area.Height);
        if (expandLeft && orb.Left - gap - width < area.Left && orb.Right + gap + width <= area.Right) expandLeft = false;
        else if (!expandLeft && orb.Right + gap + width > area.Right && orb.Left - gap - width >= area.Left) expandLeft = true;
        var left = expandLeft ? orb.Left - gap - width : orb.Right + gap;
        var top = orb.Top + orb.Height / 2 < area.Top + area.Height / 2 ? orb.Top : orb.Bottom - height;
        return new Rect(Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - width)),
            Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - height)), width, height);
    }
}
