using System.Security.Claims;
using Rose.Application.Abstractions;
using Rose.Application.Users;

namespace Rose.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in Supabase user's id (the token's <c>sub</c>), which owns their data.</summary>
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub"), out var id) && id != Guid.Empty
            ? id
            : throw new InvalidOperationException("The token has no user id (sub).");
}

/// <summary><see cref="ICurrentUser"/> for the request being handled.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    public Guid Id => (http.HttpContext?.User ?? throw new InvalidOperationException("No request in progress.")).UserId();
}

/// <summary>Lets someone who still has a temporary password call this endpoint (to see their session and change it).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowPendingPasswordChangeAttribute : Attribute;

/// <summary>Where the authorization check leaves the signed-in account for the rest of the request.</summary>
internal static class AccountItem
{
    private const string Key = "rose.account";

    public static void Set(HttpContext context, Account account) => context.Items[Key] = account;

    public static Account? Get(HttpContext context) => context.Items[Key] as Account;
}