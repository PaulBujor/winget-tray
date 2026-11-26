using System.Text.Json;
using WinGetTray;

namespace WinGetTray.Core.Tests;

public class WingetParseTests
{
    private static string Fixture()
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "winget-upgrade.txt"));

    [Fact]
    public void Parses_every_row_from_real_winget_output()
    {
        var upgrades = Winget.Parse(Fixture());

        // the fixture's own footer says "31 upgrades available."
        Assert.Equal(31, upgrades.Count);
    }

    [Fact]
    public void Splits_columns_correctly()
    {
        var git = Winget.Parse(Fixture()).Single(u => u.Id == "Git.Git");

        Assert.Equal("Git", git.Name);
        Assert.Equal("2.55.0.3", git.Current);
        Assert.Equal("2.56.1", git.Available);
        Assert.Equal("winget", git.Source);
    }

    [Fact]
    public void Handles_names_with_spaces_and_brackets()
    {
        var chrome = Winget.Parse(Fixture()).Single(u => u.Id == "Google.Chrome.EXE");
        Assert.Equal("Google Chrome", chrome.Name);

        var runtime = Winget.Parse(Fixture()).Single(u => u.Id == "Microsoft.VCRedist.2015+.x64");
        Assert.Contains("Microsoft Visual C++ v14 Redistributable", runtime.Name);
    }

    [Fact]
    public void Keeps_rows_whose_current_version_is_unknown()
    {
        // winget prints "< 175.2.0.13423" in the version column for these
        var ubisoft = Winget.Parse(Fixture()).Single(u => u.Id == "Ubisoft.Connect");
        Assert.Equal("175.2.0.13423", ubisoft.Available);
        Assert.Contains("175.2.0.13423", ubisoft.Current);
    }

    [Fact]
    public void Detail_formats_the_arrow_string()
    {
        var upgrade = new Upgrade("Git", "Git.Git", "2.55.0.3", "2.56.1", "winget");
        Assert.Equal("2.55.0.3  →  2.56.1", upgrade.Detail);
    }

    [Fact]
    public void Ignores_the_footer_and_returns_nothing_when_no_upgrades()
    {
        const string upToDate =
            "Name                     Id                     Version      Available    Source\n" +
            "-------------------------------------------------------------------------------\n" +
            "No installed package found matching input criteria.\n";

        Assert.Empty(Winget.Parse(upToDate));
    }

    [Theory]
    [InlineData("")]
    [InlineData("random text with no table")]
    [InlineData("Name   Id\n----   --\n")]
    public void Returns_empty_for_input_without_a_header(string input)
    {
        Assert.Empty(Winget.Parse(input));
    }

    [Fact]
    public void Trim_shortens_long_text_with_an_ellipsis()
    {
        var trimmed = Winget.Trim(new string('x', 50), 10);

        Assert.Equal(11, trimmed.Length);
        Assert.EndsWith("…", trimmed);
        Assert.Equal("short", Winget.Trim("short", 10));
    }
}

public class UpgradeFilterTests
{
    private static List<Upgrade> Sample() => new()
    {
        new Upgrade("Git", "Git.Git", "2.55.0.3", "2.56.1", "winget"),
        new Upgrade("GitHub CLI", "GitHub.cli", "2.97.0", "2.106.0", "winget"),
        new Upgrade("Docker Desktop", "Docker.DockerDesktop", "4.84.0", "4.100.1", "winget"),
        new Upgrade("Zed", "ZedIndustries.Zed", "1.29.3", "1.30.2", "winget"),
    };

    [Fact]
    public void Empty_query_returns_everything()
    {
        Assert.Equal(4, UpgradeFilter.Apply(Sample(), "").Count);
        Assert.Equal(4, UpgradeFilter.Apply(Sample(), "   ").Count);
        Assert.Equal(4, UpgradeFilter.Apply(Sample(), null).Count);
    }

    [Fact]
    public void Matches_package_name_case_insensitively()
    {
        var hits = UpgradeFilter.Apply(Sample(), "GIT");

        Assert.Equal(new[] { "Git.Git", "GitHub.cli" }, hits.Select(h => h.Id));
    }

    [Fact]
    public void Matches_package_id()
    {
        var hits = UpgradeFilter.Apply(Sample(), "DockerDesktop");

        Assert.Single(hits);
        Assert.Equal("Docker.DockerDesktop", hits[0].Id);
    }

    [Fact]
    public void Trims_the_query_before_matching()
    {
        Assert.Equal(2, UpgradeFilter.Apply(Sample(), "  git  ").Count);
    }

    [Fact]
    public void No_match_returns_an_empty_list()
    {
        Assert.Empty(UpgradeFilter.Apply(Sample(), "nothing-here"));
    }
}

public class SettingsTests
{
    [Fact]
    public void Defaults_are_sane()
    {
        var settings = new Settings();

        Assert.Equal(12, settings.IntervalHours);
        Assert.False(settings.AutoInstall);
        Assert.Equal(string.Empty, settings.LastError);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var original = new Settings { IntervalHours = 3, AutoInstall = true, LastCount = 7 };
        var json = JsonSerializer.Serialize(original);

        var back = JsonSerializer.Deserialize<Settings>(json)!;

        Assert.Equal(3, back.IntervalHours);
        Assert.True(back.AutoInstall);
        Assert.Equal(7, back.LastCount);
    }
}
