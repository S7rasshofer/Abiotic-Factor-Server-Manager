using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace AbioticServerManager.App.Services;

/// <summary>
/// Resilient wrapper around <see cref="Clipboard.SetText"/>. The raw WPF
/// clipboard call throws <see cref="COMException"/> (HRESULT 0x800401D0,
/// "OpenClipboard failed") when another process is holding the clipboard at
/// the moment of the call - a routine occurrence on Windows when a browser,
/// IM client, screenshot tool, or clipboard manager touches the clipboard
/// concurrently. The retry loop here masks that transient race and falls
/// back to <see cref="Clipboard.SetDataObject(object, bool)"/> with the
/// <c>copy</c> flag set so the value survives the originating process
/// exiting.
/// </summary>
public static class ClipboardHelper
{
    /// <summary>
    /// Copies <paramref name="text"/> to the system clipboard. Returns
    /// <c>true</c> on success, <c>false</c> after all retries have failed.
    /// Empty / null text is a no-op that returns <c>false</c> - the WPF
    /// clipboard rejects empty strings with <see cref="ArgumentException"/>.
    /// </summary>
    public static bool TryCopy(string? text, int maxAttempts = 3)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                // SetDataObject with copy=true is the only call that
                // guarantees the data survives this process exiting.
                // SetText is a convenience wrapper that internally uses
                // copy=false on older runtimes.
                Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (COMException) when (attempt < maxAttempts)
            {
                // Another process holds the clipboard; back off briefly and
                // retry. 50-100 ms is the well-known sweet spot for this
                // race - long enough for the other process to release,
                // short enough not to feel like a hang.
                Thread.Sleep(50 * attempt);
            }
            catch (COMException)
            {
                return false; // last attempt failed
            }
            catch (Exception)
            {
                // ArgumentException (null/empty already guarded), or
                // unforeseen runtime issues. Surface as failure but don't
                // crash the UI thread.
                return false;
            }
        }

        return false;
    }
}
