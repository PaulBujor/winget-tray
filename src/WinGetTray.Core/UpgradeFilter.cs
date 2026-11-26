// Pure helpers around the parsed upgrade list, kept out of the UI so they can be unit tested.
namespace WinGetTray;

public static class UpgradeFilter
{
    /// <summary>
    /// Case-insensitive match on package name or id. Empty/whitespace query returns everything.
    /// </summary>
    public static List<Upgrade> Apply(IEnumerable<Upgrade> upgrades, string? query)
    {
        var list = upgrades as IList<Upgrade> ?? upgrades.ToList();
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0)
        {
            return list.ToList();
        }

        return list.Where(u =>
                u.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                u.Id.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
