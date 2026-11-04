namespace Rose.Application.Abstractions;

/// <summary>A service that keeps users' data in memory. When an account is removed, its cache is dropped.</summary>
public interface IUserScopedCache
{
    void Forget(Guid owner);
}