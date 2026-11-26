// Entry point: CLI modes for tooling/verification, otherwise the tray + window.
using System.Text.Json;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace WinGetTray;

public partial class App : Application
{
    private TrayHost? _tray;
    private Mutex? _mutex;
    private EventWaitHandle? _showSignal;
    private CancellationTokenSource? _listen;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = e.Args;
        var isCli = args.Length > 0 &&
                    args[0].StartsWith("--", StringComparison.Ordinal) &&
                    !args[0].Equals("--show", StringComparison.OrdinalIgnoreCase);
        if (isCli)
        {
            Shutdown(Cli.Run(args));
            return;
        }

        _mutex = new Mutex(initiallyOwned: true, @"Local\WinGetTray.SingleInstance", out var isNew);
        if (!isNew)
        {
            // already running: signal it to show the window, then leave
            try
            {
                using var show = EventWaitHandle.OpenExisting(@"Local\WinGetTray.Show");
                show.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // no live instance listening; nothing to do
            }
            Shutdown(0);
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Write($"unhandled: {ex.ExceptionObject}");
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Write($"dispatcher: {ex.Exception}");
            ex.Handled = true;
        };

        _tray = new TrayHost();
        _tray.Start();

        // listen for "--show" from a second instance (Start Menu shortcut)
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WinGetTray.Show");
        _listen = new CancellationTokenSource();
        var token = _listen.Token;
        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                if (_showSignal.WaitOne(500) && !token.IsCancellationRequested)
                {
                    Dispatcher.Invoke(() => _tray?.ShowUpdates());
                }
            }
        }, token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _listen?.Cancel(); } catch { }
        _showSignal?.Dispose();
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}

internal static class Cli
{
    public static int Run(string[] args)
    {
        var arg = args[0].ToLowerInvariant();
        var settings = Settings.Load();

        switch (arg)
        {
            case "--check":
            {
                Con.Attach();
                var (ups, error) = Winget.Check(settings);
                if (args.Contains("--json"))
                {
                    Con.Say(JsonSerializer.Serialize(new { count = ups.Count, error, upgrades = ups },
                        new JsonSerializerOptions { WriteIndented = true }));
                }
                else if (ups.Count == 0)
                {
                    Con.Say(error.Length > 0 ? $"Check failed: {error}" : "Up to date.");
                }
                else
                {
                    Con.Say($"{ups.Count} update{(ups.Count == 1 ? "" : "s")} available:");
                    foreach (var u in ups)
                    {
                        Con.Say($"  {u.Name} [{u.Id}] {u.Current} -> {u.Available} ({u.Source})");
                    }
                }
                return ups.Count > 0 ? 10 : 0;
            }

            case "--update-all":
            {
                Con.Attach();
                var (ok, output) = Winget.UpdateAll(settings);
                Con.Say(output);
                return ok ? 0 : 1;
            }

            case "--update-one":
            case "--update-ids":
            {
                Con.Attach();
                var ids = (args.Length > 1 ? args[1] : string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (ids.Length == 0)
                {
                    Con.Say("--update-ids needs one or more package ids");
                    return 2;
                }

                var allOk = true;
                foreach (var id in ids)
                {
                    var (ok, output) = Winget.UpdateOne(settings, id);
                    Con.Say($"[{id}] {(ok ? "ok" : "failed")}");
                    if (!ok)
                    {
                        allOk = false;
                        Con.Say(Winget.Trim(output, 2000));
                    }
                }
                return allOk ? 0 : 1;
            }

            case "--version":
            {
                Con.Attach();
                Con.Say("WinGetTray 1.1.0");
                return 0;
            }

            default:
            {
                Con.Attach();
                Con.Say("usage: WinGetTray [--check [--json]] [--update-all] [--update-ids id1,id2] [--show]");
                return 2;
            }
        }
    }
}
