namespace Rose.Application.Users;

/// <summary>
/// The admins: the emails in the server's settings (Access:AdminEmails). They can always sign
/// in and manage accounts; everybody else needs an account an admin created.
/// </summary>
public sealed class AccessPolicy(IEnumerable<string> adminEmails)
{
    private readonly HashSet<string> admins = adminEmails
        .Select(email => email.Trim())
        .Where(email => email.Length > 0)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool IsAdmin(string? email) => email is not null && admins.Contains(email.Trim());
}