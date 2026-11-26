// Tray icon, its context menu, the check schedule and the elevated update launch.
// WinForms is used only for NotifyIcon/ContextMenuStrip; everything user-facing is WPF.
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace WinGetTray;

internal static class Icons
{
    private static readonly Dictionary<(Color, string), Icon> Cache = new();

    public static Icon Get(Color color, string glyph)
    {
        if (Cache.TryGetValue((color, glyph), out var cached)) return cached;

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            using (var fill = new SolidBrush(color))
            {
                g.FillEllipse(fill, 1, 1, 30, 30);
            }
            using (var pen = new Pen(Color.FromArgb(70, 0, 0, 0), 1.5f))
            {
                g.DrawEllipse(pen, 1, 1, 30, 30);
            }
            using var font = new Font("Segoe UI", 19f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(glyph, font, Brushes.White, new RectangleF(0, 0, 32, 33), format);
        }

        var icon = Icon.FromHandle(bmp.GetHicon());
        Cache[(color, glyph)] = icon;
        return icon;
    }
}

/// <summary>Flat menu colours so the tray menu matches the light/dark shell.</summary>
internal sealed class MenuColors : WinForms.ProfessionalColorTable
{
    private readonly Color _card;
    private readonly Color _stroke;
    private readonly Color _hover;

    public MenuColors(Color card, Color stroke, Color hover)
    {
        _card = card;
        _stroke = stroke;
        _hover = hover;
    }

    public override Color ToolStripDropDownBackground => _card;
    public override Color ImageMarginGradientBegin => _card;
    public override Color ImageMarginGradientMiddle => _card;
    public override Color ImageMarginGradientEnd => _card;
    public override Color MenuBorder => _stroke;
    public override Color MenuItemBorder => _stroke;
    public override Color MenuItemSelected => _hover;
    public override Color MenuItemSelectedGradientBegin => _hover;
    public override Color MenuItemSelectedGradientEnd => _hover;
    public override Color MenuItemPressedGradientBegin => _hover;
    public override Color MenuItemPressedGradientEnd => _hover;
    public override Color SeparatorDark => _stroke;
    public override Color SeparatorLight => _stroke;
}

internal sealed class TrayHost : IDisposable
{
    private static readonly Color Grey = Color.FromArgb(0x6B, 0x72, 0x80);
    private static readonly Color Green = Color.FromArgb(0x16, 0xA3, 0x4A);
    private static readonly Color Amber = Color.FromArgb(0xD9, 0x77, 0x06);
    private static readonly Color Blue = Color.FromArgb(0x25, 0x63, 0xEB);
    private static readonly Color Red = Color.FromArgb(0xDC, 0x26, 0x26);

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "WinGetTray";

    private readonly WinForms.NotifyIcon _icon = new();
    private readonly WinForms.ContextMenuStrip _menu = new();
    private readonly DispatcherTimer _timer = new();
    private readonly Settings _settings = Settings.Load();

    private UpdatesWindow? _window;
    private List<Upgrade> _upgrades = new();
    private bool _busy;
    private string _status = "Starting…";

    public void Start()
    {
        Log.Write($"tray start v1.1 (autoInstall={_settings.AutoInstall}, intervalHours={_settings.IntervalHours})");

        var theme = Theme.Tray;
        _menu.Renderer = new WinForms.ToolStripProfessionalRenderer(
            new MenuColors(Dto(theme.Card), Dto(theme.Stroke), Dto(theme.Hover)));
        _menu.ShowImageMargin = false;

        _icon.Icon = Icons.Get(Grey, "…");
        _icon.Text = "WinGet updates";
        _icon.Visible = true;
        _icon.ContextMenuStrip = _menu;
        _icon.DoubleClick += (_, _) => ShowUpdates();

        _timer.Tick += (_, _) => { _timer.Stop(); _ = CheckAsync(); };
        RebuildMenu();

        if (Environment.GetCommandLineArgs().Skip(1).Any(a => a.Equals("--show", StringComparison.OrdinalIgnoreCase)))
        {
            _ = Dispatcher.CurrentDispatcher.InvokeAsync(ShowUpdates, DispatcherPriority.Background);
        }

        // first check shortly after login, then every IntervalHours
        var kick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        kick.Tick += (_, _) => { kick.Stop(); _ = CheckAsync(); };
        kick.Start();
    }

    private static Color Dto(System.Windows.Media.Color c) => Color.FromArgb(c.R, c.G, c.B);

    private void RebuildMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add(new WinForms.ToolStripMenuItem(_status) { Enabled = false });
        var show = new WinForms.ToolStripMenuItem(
            _upgrades.Count > 0 ? $"Show updates ({_upgrades.Count})" : "Show updates")
        {
            Enabled = !_busy,
        };
        show.Click += (_, _) => ShowUpdates();
        _menu.Items.Add(show);

        var check = new WinForms.ToolStripMenuItem("Check for updates now") { Enabled = !_busy };
        check.Click += (_, _) => _ = CheckAsync();
        _menu.Items.Add(check);

        _menu.Items.Add(new WinForms.ToolStripSeparator());

        var auto = new WinForms.ToolStripMenuItem("Install updates automatically") { Checked = _settings.AutoInstall };
        auto.Click += (_, _) =>
        {
            _settings.AutoInstall = !_settings.AutoInstall;
            _settings.Save();
            RebuildMenu();
            Log.Write($"autoInstall={_settings.AutoInstall}");
        };
        _menu.Items.Add(auto);

        var startup = new WinForms.ToolStripMenuItem("Start with Windows") { Checked = IsAutostartEnabled() };
        startup.Click += (_, _) =>
        {
            SetAutostart(!IsAutostartEnabled());
            RebuildMenu();
        };
        _menu.Items.Add(startup);

        var log = new WinForms.ToolStripMenuItem("Open log");
        log.Click += (_, _) => OpenLog();
        _menu.Items.Add(log);

        _menu.Items.Add(new WinForms.ToolStripSeparator());

        var exit = new WinForms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitApp();
        _menu.Items.Add(exit);

        var p = Theme.Tray;
        _menu.ForeColor = Color.FromArgb(p.Text.R, p.Text.G, p.Text.B);
        _menu.BackColor = Color.FromArgb(p.Card.R, p.Card.G, p.Card.B);
    }

    private void SetState(Color color, string glyph, string status, string? tip)
    {
        _status = status;
        _icon.Icon = Icons.Get(color, glyph);
        _icon.Text = tip ?? "WinGet updates";
        RebuildMenu();
    }

    public void ShowUpdates()
    {
        if (_window is null)
        {
            _window = new UpdatesWindow(BeginUpdate, () => _ = CheckAsync());
            _window.Closed += (_, _) => _window = null;
        }

        _window.SetData(_upgrades);
        _window.SetBusy(_busy);
        if (!_window.IsVisible)
        {
            _window.Show();
        }
        _window.Activate();
    }

    private async Task CheckAsync()
    {
        if (_busy) return;
        _busy = true;
        SetState(Blue, "…", "Checking for updates…", "WinGet: checking…");
        _window?.SetBusy(true, "Checking…");

        try
        {
            var (ups, error) = await Task.Run(() => Winget.Check(_settings));
            _upgrades = ups;
            _settings.LastCheck = DateTimeOffset.Now;
            _settings.LastCount = ups.Count;
            _settings.LastError = error;
            _settings.Save();

            if (ups.Count == 0)
            {
                SetState(error.Length > 0 ? Red : Green, error.Length > 0 ? "!" : "✓",
                    error.Length > 0 ? "Check failed — see log" : "Up to date",
                    error.Length > 0 ? "WinGet: error" : "WinGet: up to date");
            }
            else
            {
                var preview = string.Join(", ", ups.Take(3).Select(u => u.Name));
                if (ups.Count > 3) preview += ", …";
                SetState(Amber, "↑", $"{ups.Count} update{(ups.Count == 1 ? "" : "s")} available",
                    $"WinGet: {ups.Count} update{(ups.Count == 1 ? "" : "s")}");
                _icon.ShowBalloonTip(8000,
                    $"WinGet: {ups.Count} update{(ups.Count == 1 ? "" : "s")} available",
                    preview, WinForms.ToolTipIcon.Info);
            }
        }
        catch (Exception e)
        {
            Log.Write($"check failed: {e}");
            SetState(Red, "!", "Check failed — see log", "WinGet: error");
        }
        finally
        {
            _busy = false;
            _window?.SetData(_upgrades);
            _window?.SetBusy(false);

            _timer.Interval = TimeSpan.FromHours(Math.Max(1, _settings.IntervalHours));
            _timer.Start();

            if (_settings.AutoInstall && _upgrades.Count > 0)
            {
                BeginUpdate(_upgrades);
            }
        }
    }

    private void BeginUpdate(IReadOnlyList<Upgrade> picks)
    {
        if (_busy || picks.Count == 0) return;

        var summary = string.Join(Environment.NewLine, picks.Take(8).Select(u => $"   {u.Name}   {u.Current} → {u.Available}"));
        if (picks.Count > 8) summary += $"{Environment.NewLine}   …and {picks.Count - 8} more";

        var owner = _window is { IsVisible: true } ? _window : null;
        var text = $"Install {picks.Count} update{(picks.Count == 1 ? "" : "s")}?{Environment.NewLine}{Environment.NewLine}{summary}" +
                   $"{Environment.NewLine}{Environment.NewLine}Windows will ask for administrator approval.";
        var answer = owner is null
            ? MessageBox.Show(text, "WinGet updates", MessageBoxButton.OKCancel, MessageBoxImage.Question)
            : MessageBox.Show(owner, text, "WinGet updates", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        RunElevated("--update-ids \"" + string.Join(",", picks.Select(u => u.Id)) + "\"");
    }

    private void RunElevated(string arguments)
    {
        try
        {
            _busy = true;
            SetState(Blue, "↑", "Installing updates…", "WinGet: installing…");
            _window?.SetBusy(true, "Installing…");

            var psi = new ProcessStartInfo(Paths.ExePath, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            var p = Process.Start(psi);
            if (p is null)
            {
                _busy = false;
                return;
            }
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) => _ = CheckAsync();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Log.Write("elevation declined");
            _busy = false;
            SetState(Amber, "↑", $"{_upgrades.Count} updates available", $"WinGet: {_upgrades.Count} updates");
            _window?.SetBusy(false, "Cancelled");
        }
    }

    private void ExitApp()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _timer.Stop();
        _window?.CloseQuietly();
        Log.Write("tray exit");
        Application.Current.Shutdown();
    }

    private static void OpenLog()
    {
        try
        {
            if (!File.Exists(Paths.LogFile)) File.WriteAllText(Paths.LogFile, string.Empty);
            Process.Start(new ProcessStartInfo(Paths.LogFile) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Write($"open log failed: {e.Message}");
        }
    }

    private static bool IsAutostartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string s && s.Length > 0;
    }

    private static void SetAutostart(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;
        if (enable)
        {
            key.SetValue(RunValue, $"\"{Paths.ExePath}\"");
            Log.Write("autostart enabled");
        }
        else
        {
            key.DeleteValue(RunValue, throwOnMissingValue: false);
            Log.Write("autostart disabled");
        }
    }

    public void Dispose()
    {
        _icon.Dispose();
        _menu.Dispose();
    }
}
