using System;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Taskboard.Desktop;

// Materialize before copying: an OLE proxy to the previous clipboard can become
// invalid after its owner is replaced, especially for multiple formats.
internal sealed class ConversationClipboard : IDisposable
{
    private readonly DataObject snapshot = new();
    private readonly string[] formats;
    private bool publicationMayBeDelayed;
    internal uint Sequence { get; }
    internal ConversationClipboard()
    {
        Sequence = GetClipboardSequenceNumber();
        var source = Clipboard.GetDataObject(); formats = source?.GetFormats(false) ?? [];
        foreach (var format in formats)
        {
            var value = source!.GetData(format, false) ?? throw new InvalidOperationException("无法完整缓存剪贴板格式");
            object copy = value switch
            {
                string text => text,
                byte[] bytes => bytes.ToArray(),
                string[] files => files.ToArray(),
                StringCollection files => CopyFiles(files),
                MemoryStream stream => new MemoryStream(stream.ToArray()),
                Image image => image.Clone(),
                int or uint or long or ulong or short or ushort or byte or sbyte or bool or char or double or float or decimal => value,
                _ => throw new InvalidOperationException("当前剪贴板格式不能完整缓存")
            };
            snapshot.SetData(format, false, copy);
        }
        if (Sequence != GetClipboardSequenceNumber()) throw new InvalidOperationException("剪贴板正在变化");
    }
    private static StringCollection CopyFiles(StringCollection source)
    { var result = new StringCollection(); result.AddRange(source.Cast<string>().ToArray()); return result; }
    internal bool Restore(uint expectedSequence)
    {
        if (GetClipboardSequenceNumber() != expectedSequence) return false;
        try
        {
            if (formats.Length == 0) Clipboard.Clear();
            else
            {
                publicationMayBeDelayed = true;
                Clipboard.SetDataObject(snapshot, true, 3, 20);
                publicationMayBeDelayed = false;
            }
        }
        catch (ExternalException)
        {
            // OLE may report failure after publishing the data. Verify every
            // cached format instead of overwriting a later copy or blindly retrying.
            return VerifyRestored();
        }
        return VerifyRestored();
    }
    private bool VerifyRestored()
    {
        // OLE can materialize delayed formats on the first read and advance
        // its sequence. Retry verification only; never publish over a new copy.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { if (MatchesCurrent()) return true; }
            catch (ExternalException) { }
            if (attempt < 2) System.Threading.Thread.Sleep(20);
        }
        return false;
    }
    internal bool MatchesCurrent()
    {
        var sequence = GetClipboardSequenceNumber();
        var restored = Clipboard.GetDataObject();
        if (formats.Length == 0) return (restored?.GetFormats(false).Length ?? 0) == 0 && sequence == GetClipboardSequenceNumber();
        if (formats.Any(format => restored?.GetDataPresent(format, false) != true)) return false;
        foreach (var format in formats)
            if (!Same(snapshot.GetData(format, false), restored!.GetData(format, false))) return false;
        return sequence == GetClipboardSequenceNumber();
    }
    private static bool Same(object? first, object? second) => (first, second) switch
    {
        (byte[] a, byte[] b) => a.SequenceEqual(b),
        (string[] a, string[] b) => a.SequenceEqual(b),
        (StringCollection a, StringCollection b) => a.Cast<string>().SequenceEqual(b.Cast<string>()),
        (StringCollection a, string[] b) => a.Cast<string>().SequenceEqual(b),
        (string[] a, StringCollection b) => a.SequenceEqual(b.Cast<string>()),
        (MemoryStream a, MemoryStream b) => a.ToArray().SequenceEqual(b.ToArray()),
        (Image a, Image b) => ImageBytes(a).SequenceEqual(ImageBytes(b)),
        _ => Equals(first, second)
    };
    private static byte[] ImageBytes(Image image)
    { using var bytes = new MemoryStream(); image.Save(bytes, System.Drawing.Imaging.ImageFormat.Png); return bytes.ToArray(); }
    public void Dispose()
    {
        // If OleFlushClipboard failed, the clipboard may still be serving this
        // DataObject lazily. Its images/streams must stay usable until it is replaced.
        if (publicationMayBeDelayed) return;
        foreach (var format in formats)
            if (snapshot.GetData(format, false) is IDisposable value) value.Dispose();
    }
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}
