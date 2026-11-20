using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Taskboard.Desktop;

internal sealed partial class SettingsSurface
{
    private static readonly Dictionary<string, Int32Rect> HelpPhotoRegions = new(StringComparer.Ordinal)
    {
        ["focus-off"] = new(0, 0, 2190, 535),
        ["focus-on"] = new(0, 0, 2190, 535),
        ["details-off"] = new(0, 0, 525, 305),
        ["details-on"] = new(0, 0, 525, 225),
        ["inactive-off"] = new(0, 0, 602, 345),
        ["inactive-on"] = new(0, 0, 602, 245),
        ["binding-off"] = new(22, 142, 541, 207),
        ["binding-on"] = new(22, 142, 955, 308),
        ["attention-off"] = new(22, 142, 1102, 210),
        ["attention-on"] = new(22, 142, 1102, 210),
        ["edge-off"] = new(439, 168, 116, 111),
        ["edge-on"] = new(439, 168, 116, 111),
        ["quick-off"] = new(592, 142, 760, 412),
        ["quick-on"] = new(22, 142, 541, 213),
    };
    private static readonly ConcurrentDictionary<string, BitmapSource> HelpPhotos = new(StringComparer.Ordinal);
    internal static ImageSource HelpImage(string kind, bool enabled = false)
    {
        var key = kind + (enabled ? "-on" : "-off");
        if (!HelpPhotoRegions.ContainsKey(key)) throw new ArgumentException("未知帮助主题", nameof(kind));
        return HelpPhotos.GetOrAdd(key, name =>
        {
            var original = new BitmapImage(); original.BeginInit(); original.CacheOption = BitmapCacheOption.OnLoad;
            original.UriSource = new Uri("pack://application:,,,/Taskboard;component/Assets/HelpScreenshots/" + name + ".png");
            original.EndInit(); original.Freeze();
            var cropped = new CroppedBitmap(original, HelpPhotoRegions[name]); cropped.Freeze(); return cropped;
        });
    }
}
