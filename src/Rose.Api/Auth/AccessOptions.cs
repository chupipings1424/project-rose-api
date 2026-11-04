namespace Rose.Api.Auth;

/// <summary>The "Access" section: who runs this ROSE.</summary>
public sealed class AccessOptions
{
    public const string Section = "Access";

    /// <summary>
    /// The admins (case-insensitive). They can always sign in and manage the other accounts.
    /// Their first sign-in also takes over any data saved before ROSE had accounts.
    /// </summary>
    public List<string> AdminEmails { get; set; } = [];
}