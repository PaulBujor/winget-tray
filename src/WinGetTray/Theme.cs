// Windows 11 theme: palette resolved from the user's light/dark setting and accent colour,
// plus the DWM calls that give the window rounded corners and a dark caption bar.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinGetTray;

internal sealed record Palette(
    Color Window,
    Color Card,
    Color Stroke,
    Color Text,
    Color TextSecondary,
    Color Hover,
    Color Accent,
    Color AccentText);

internal static class Theme
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static bool IsDark { get; private set; }

    public static Palette Current { get; private set; } = Build();

    public static Palette Reload()
    {
        Current = Build();
        return Current;
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

    private static Color ReadAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr)
            {
                // stored as 0xAABBGGRR
                var c = Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
                var hsl = ToHsl(c);
                if (hsl.S > 0.12) return c;
            }
        }
        catch
        {
            // fall through to the Windows default blue
        }
        return Color.FromRgb(0x00, 0x67, 0xC0);
    }

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (Math.Abs(max - min) < 0.0001) return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (Math.Abs(max - r) < 0.0001) h = ((g - b) / d + (g < b ? 6 : 0)) / 6;
        else if (Math.Abs(max - g) < 0.0001) h = ((b - r) / d + 2) / 6;
        else h = ((r - g) / d + 4) / 6;
        return (h, s, l);
    }

    private static Palette Build()
    {
        IsDark = ReadDark();
        var accent = ReadAccent();

        return IsDark
            ? new Palette(
                Window: Color.FromRgb(0x20, 0x20, 0x20),
                Card: Color.FromRgb(0x2B, 0x2B, 0x2B),
                Stroke: Color.FromRgb(0x3A, 0x3A, 0x3A),
                Text: Color.FromRgb(0xF2, 0xF2, 0xF2),
                TextSecondary: Color.FromRgb(0x9D, 0x9D, 0x9D),
                Hover: Color.FromRgb(0x33, 0x33, 0x33),
                Accent: accent,
                AccentText: Colors.White)
            : new Palette(
                Window: Color.FromRgb(0xF3, 0xF3, 0xF3),
                Card: Color.FromRgb(0xFB, 0xFB, 0xFB),
                Stroke: Color.FromRgb(0xE0, 0xE0, 0xE0),
                Text: Color.FromRgb(0x1A, 0x1A, 0x1A),
                TextSecondary: Color.FromRgb(0x5D, 0x5D, 0x5D),
                Hover: Color.FromRgb(0xEC, 0xEC, 0xEC),
                Accent: accent,
                AccentText: Colors.White);
    }

    public static Brush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>Rounded corners + matching dark caption; ignored on Windows 10.</summary>
    public static void ApplyWindowHints(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
        var round = DwmCornerRound;
        DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref round, sizeof(int));
    }
}
