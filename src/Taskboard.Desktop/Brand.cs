using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Taskboard.Desktop;

internal static class Brand
{
    internal const string Name = "摸鱼台";
    private static BitmapSource? appIconImage;

    internal static ImageSource? AppIconImage()
    {
        if (appIconImage is not null) return appIconImage;
        try
        {
            if (Application.Current is null) return null;
            var resource = Application.GetResourceStream(new Uri("/Taskboard;component/Assets/orb.png", UriKind.Relative));
            if (resource?.Stream is null) return null;
            using var stream = resource.Stream;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return appIconImage = bitmap;
        }
        catch
        {
            return null;
        }
    }

    internal static FrameworkElement OrbGlyph()
    {
        if (AppIconImage() is { } source)
        {
            var image = new Image
            {
                Tag = "BrandOrbIcon",
                Source = source,
                Width = 50,
                Height = 50,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                SnapsToDevicePixels = false
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }
        return FallbackOrbGlyph();
    }

    internal static Viewbox FallbackOrbGlyph()
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse("M 12,1 L 23,12 12,23 1,12 Z M 12,6 L 18,12 12,18 6,12 Z"),
            Stroke = Ui.Accent,
            StrokeThickness = 1.7,
            StrokeLineJoin = PenLineJoin.Round
        });
        return new Viewbox
        {
            Tag = "BrandOrbFallback",
            Child = canvas,
            Width = 19,
            Height = 19,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false
        };
    }

    internal static System.Drawing.Icon TrayIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("/Taskboard;component/Assets/orb.ico", UriKind.Relative))!.Stream;
        return new System.Drawing.Icon(stream);
    }
}
