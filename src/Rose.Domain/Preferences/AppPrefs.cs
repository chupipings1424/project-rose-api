using System.Text.RegularExpressions;
using Rose.Domain.Common;

namespace Rose.Domain.Preferences;

/// <summary>
/// One person's choices for the projects hub: the ids of the projects they unticked in "My apps".
/// Hidden, not removed - their data stays and comes back when the project is ticked again.
/// </summary>
public sealed class AppPrefs
{
    /// <summary>The owner's id as text: there is exactly one per person.</summary>
    public required string Id { get; init; }
    public List<string> Hidden { get; set; } = [];
    public required DateTimeOffset UpdatedAt { get; set; }
}

public static partial class AppPrefsRules
{
    public const int MaxIds = 50;

    /// <summary>Trimmed, lower-case, unique project ids such as <c>payment-tracker</c>. Anything else is a 400.</summary>
    public static List<string> Clean(IEnumerable<string?>? ids)
    {
        var list = (ids ?? []).Select(id => (id ?? "").Trim().ToLowerInvariant()).Distinct().ToList();
        if (list.Count > MaxIds) throw new ValidationException($"At most {MaxIds} apps can be hidden.");
        var bad = list.FirstOrDefault(id => !AppId().IsMatch(id));
        if (bad is not null) throw new ValidationException($"'{bad}' is not an app id.");
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex AppId();
}