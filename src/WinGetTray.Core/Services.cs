// Shared plumbing: settings, logging, winget invocation and parsing of its fixed-width upgrade table.
// No UI types in here so both the WPF window and the tray can use it.
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinGetTray;

public sealed record Upgrade(string Name, string Id, string Current, string Available, string Source)
{
    /// <summary>"1.2.3 → 1.4.0" for display.</summary>
    public string Detail => $"{Current}  →  {Available}";
}

internal static class Paths
{
    public static string Dir
    {
        get
        {
            var d = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGetTray");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static string LogFile => Path.Combine(Dir, "winget-tray.log");
    public static string SettingsFile => Path.Combine(Dir, "settings.json");
    public static string ExePath => Environment.ProcessPath ?? AppContext.BaseDirectory + "WinGetTray.exe";
}

internal sealed class Settings
{
    public int IntervalHours { get; set; } = 12;
    public bool AutoInstall { get; set; }
    public DateTimeOffset? LastCheck { get; set; }
    public int LastCount { get; set; }
    public string LastError { get; set; } = "";
    public string WingetPath { get; set; } = "";

    public static Settings Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
            {
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Paths.SettingsFile)) ?? new Settings();
            }
        }
        catch (Exception e)
        {
            Log.Write($"settings load failed: {e.Message}");
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(Paths.SettingsFile,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Log.Write($"settings save failed: {e.Message}");
        }
    }
}

internal static class Log
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var fi = new FileInfo(Paths.LogFile);
                if (fi.Exists && fi.Length > 1_000_000)
                {
                    var old = Paths.LogFile + ".old";
                    File.Delete(old);
                    File.Move(Paths.LogFile, old);
                }
                File.AppendAllText(Paths.LogFile,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // logging must never take the app down
        }
    }
}

internal static class Con
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    public static void Attach()
    {
        if (!AttachConsole(-1))
        {
            AllocConsole();
        }
    }

    public static void Say(string line) => Console.WriteLine(line);
}

internal static class Winget
{
    private static readonly Regex IdPattern = new(@"^[A-Za-z0-9][A-Za-z0-9._+-]*$", RegexOptions.Compiled);

    private const string CheckArgs =
        "upgrade --include-unknown --accept-source-agreements --disable-interactivity";

    private const string UpdateAllArgs =
        "upgrade --all --include-unknown --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";

    public static string ResolveExe(Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.WingetPath) && File.Exists(settings.WingetPath))
        {
            return settings.WingetPath;
        }

        var alias = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\WindowsApps\winget.exe");
        if (File.Exists(alias))
        {
            return alias;
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';'))
        {
            var candidate = Path.Combine(dir.Trim(), "winget.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "winget.exe";
    }

    private static (string StdOut, string StdErr, int ExitCode) Run(string exe, string arguments, int timeoutMs)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var psi = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetTempPath(),
        };

        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!p.Start())
        {
            return (string.Empty, "failed to start winget", -1);
        }

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            return (stdout.ToString(), "timeout", -2);
        }

        p.WaitForExit();
        return (stdout.ToString(), stderr.ToString(), p.ExitCode);
    }

    /// <summary>Slices a fixed-width row using the header column offsets.</summary>
    private static string Cell(string line, int start, int end)
    {
        if (start < 0 || start >= line.Length) return string.Empty;
        var stop = end > 0 && end <= line.Length ? end : line.Length;
        return (stop > start ? line[start..stop] : string.Empty).Trim();
    }

    public static List<Upgrade> Parse(string output)
    {
        var results = new List<Upgrade>();
        var lines = output.Replace("\r\n", "\n").Split('\n');

        var header = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i];
            if (t.Contains(" Id", StringComparison.Ordinal) &&
                t.TrimEnd().EndsWith("Source", StringComparison.Ordinal))
            {
                header = i;
                break;
            }
        }

        if (header < 0)
        {
            return results;
        }

        var h = lines[header];
        var nameCol = h.IndexOf("Name", StringComparison.Ordinal);
        var idCol = h.IndexOf(" Id", StringComparison.Ordinal);
        var verCol = h.IndexOf("Version", StringComparison.Ordinal);
        var availCol = h.IndexOf("Available", StringComparison.Ordinal);
        var srcCol = h.IndexOf("Source", StringComparison.Ordinal);
        if (nameCol < 0 || idCol < 0 || verCol < 0 || availCol < 0 || srcCol < 0)
        {
            return results;
        }

        for (var i = header + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length < srcCol + 1) continue;
            if (line.TrimStart().StartsWith("---", StringComparison.Ordinal)) continue;
            if (line.TrimStart().StartsWith("Name", StringComparison.Ordinal)) continue;

            var id = Cell(line, idCol, verCol);
            var source = Cell(line, srcCol, 0);
            if (!IdPattern.IsMatch(id)) continue;
            if (source is not ("winget" or "msstore")) continue;

            results.Add(new Upgrade(
                Cell(line, nameCol, idCol),
                id,
                Cell(line, verCol, availCol),
                Cell(line, availCol, srcCol),
                source));
        }

        return results;
    }

    public static (List<Upgrade> Upgrades, string Error) Check(Settings settings)
    {
        var exe = ResolveExe(settings);
        var (stdout, stderr, code) = Run(exe, CheckArgs, 300_000);

        // A 0-byte result usually means the WindowsApps execution alias failed under redirected IO.
        if (stdout.Length == 0 && code != 0)
        {
            Log.Write($"check via '{exe}' returned nothing (exit {code}); retrying via the App Installer package path");
            var real = ResolveRealExe();
            if (real is not null)
            {
                settings.WingetPath = real;
                settings.Save();
                (stdout, stderr, code) = Run(real, CheckArgs, 300_000);
            }
        }

        var ups = Parse(stdout);
        if (ups.Count == 0 && code is not (0 or 1))
        {
            var err = Trim((stderr + stdout).Trim(), 400);
            Log.Write($"check exit {code}: {err}");
            return (ups, err);
        }

        return (ups, string.Empty);
    }

    private static string? ResolveRealExe()
    {
        const string ps = "(Get-AppxPackage -Name Microsoft.DesktopAppInstaller).InstallLocation";
        var (stdout, _, code) = Run("powershell.exe", $"-NoProfile -NonInteractive -Command \"{ps}\"", 60_000);
        if (code != 0) return null;
        var dir = stdout.Trim().Split('\n').LastOrDefault()?.Trim();
        if (string.IsNullOrEmpty(dir)) return null;
        var exe = Path.Combine(dir, "winget.exe");
        return File.Exists(exe) ? exe : null;
    }

    public static (bool Ok, string Output) UpdateAll(Settings settings)
    {
        var exe = ResolveExe(settings);
        var (stdout, stderr, code) = Run(exe, UpdateAllArgs, 3_600_000);
        var combined = (stdout + Environment.NewLine + stderr).Trim();
        Log.Write($"update-all exit {code}");
        Log.Write(Trim(combined, 4000));
        return (code == 0, combined);
    }

    public static (bool Ok, string Output) UpdateOne(Settings settings, string id)
    {
        var exe = ResolveExe(settings);
        var args =
            $"upgrade --id \"{id}\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";
        var (stdout, stderr, code) = Run(exe, args, 1_800_000);
        var combined = (stdout + Environment.NewLine + stderr).Trim();
        Log.Write($"update {id} exit {code}");
        Log.Write(Trim(combined, 2000));
        return (code == 0, combined);
    }

    public static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
