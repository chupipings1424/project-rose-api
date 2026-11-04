namespace Rose.Application.Abstractions;

/// <summary>
/// Who is making this request. The request-scoped services (payment tracker, budget, import)
/// read it; the shared singletons take the owner as a parameter instead, because the scheduler
/// also calls them - once for every user.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The signed-in user's id. Throws when nobody is signed in.</summary>
    Guid Id { get; }
}