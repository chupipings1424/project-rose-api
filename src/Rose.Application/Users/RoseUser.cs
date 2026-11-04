namespace Rose.Application.Users;

/// <summary>A ROSE account. <see cref="Id"/> is the Supabase user id (the token's <c>sub</c>) and owns all their data.</summary>
public sealed record RoseUser(Guid Id, string Email, bool MustChangePassword, DateTimeOffset CreatedAt);

/// <summary>An account as the admin page shows it.</summary>
public sealed record UserView(Guid Id, string Email, bool IsAdmin, bool MustChangePassword, DateTimeOffset CreatedAt);

/// <summary>Who is signed in, as far as ROSE is concerned.</summary>
public sealed record Account(RoseUser User, bool IsAdmin);

/// <summary>Add an account with a temporary password (they must change it when they first sign in).</summary>
public sealed record CreateUserRequest(string? Email, string? TemporaryPassword);

/// <summary>Give an account a new temporary password.</summary>
public sealed record ResetPasswordRequest(string? TemporaryPassword);

/// <summary>Choose your own new password.</summary>
public sealed record ChangePasswordRequest(string? NewPassword);