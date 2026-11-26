// Windows shell bits the WinForms tray menu still needs. The window takes its colours
// from WPF-UI (Fluent tokens + Mica), so this only covers the context menu.
using System.Windows.Media;
using Microsoft.Win32;

namespace WinGetTray;

/// <summary>Colours for the WinForms tray menu, which cannot use Fluent resources.</summary>
internal sealed record TrayPalette(Color Card, Color Stroke, Color Hover, Color Text);

internal static class Theme
{
    public static bool IsDark { get; private set; } = ReadDark();

    public static TrayPalette Tray { get; } = Build();

    public static TrayPalette Reload()
    {
        IsDark = ReadDark();
        return Build();
    }

    private static bool ReadDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return true;
        }
    }

    private static TrayPalette Build() => IsDark
        ? new TrayPalette(
            Card: Color.FromRgb(0x2B, 0x2B, 0x2B),
            Stroke: Color.FromRgb(0x3A, 0x3A, 0x3A),
            Hover: Color.FromRgb(0x33, 0x33, 0x33),
            Text: Color.FromRgb(0xF2, 0xF2, 0xF2))
        : new TrayPalette(
            Card: Color.FromRgb(0xFB, 0xFB, 0xFB),
            Stroke: Color.FromRgb(0xE0, 0xE0, 0xE0),
            Hover: Color.FromRgb(0xEC, 0xEC, 0xEC),
            Text: Color.FromRgb(0x1A, 0x1A, 0x1A));
}
