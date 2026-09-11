using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WPRulesReviewer.App.Services;

/// <summary>
/// Opts the main window into the Windows 11 Mica backdrop. Everything here is best effort: on
/// Windows 10 (or when the call fails) the window simply keeps its solid background.
/// </summary>
public static class WindowBackdrop
{
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int BackdropMica = 2;

    /// <summary>Mica needs build 22000 or later; earlier builds ignore or misrender the attribute.</summary>
    private const int MinimumMicaBuild = 22000;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static bool TryApplyMica(Window window, bool dark)
    {
        if (!IsSupported) return false;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;

        try
        {
            var useDark = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));

            var backdrop = BackdropMica;
            return DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static bool IsSupported =>
        Environment.OSVersion.Platform == PlatformID.Win32NT &&
        Environment.OSVersion.Version.Build >= MinimumMicaBuild;
}
