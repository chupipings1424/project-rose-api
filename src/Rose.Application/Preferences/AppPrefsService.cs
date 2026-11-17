using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Domain.Preferences;

namespace Rose.Application.Preferences;

/// <summary>Replace the list of hidden apps. Ids are the React app's project ids, e.g. <c>payment-tracker</c>.</summary>
public sealed record AppPrefsRequest(IReadOnlyList<string?>? Hidden);

/// <summary>The apps this person unticked in "My apps". Empty = everything is shown.</summary>
public sealed record AppPrefsView(IReadOnlyList<string> Hidden, DateTimeOffset? UpdatedAt);

/// <summary>"My apps": which projects each person hides from their hub, saved so every device agrees.</summary>
public sealed class AppPrefsService(IRepository<AppPrefs> repository, TimeProvider clock) : IDisposable, IUserScopedCache
{
    private readonly RepositoryCache<AppPrefs> cache = new(repository);

    public async Task<AppPrefsView> GetAsync(Guid owner, CancellationToken ct = default)
    {
        using var session = await cache.OpenAsync(owner, ct);
        return View(session.Items.FirstOrDefault());
    }

    public async Task<AppPrefsView> SetAsync(Guid owner, AppPrefsRequest request, CancellationToken ct = default)
    {
        var hidden = AppPrefsRules.Clean(request.Hidden);
        using var session = await cache.OpenAsync(owner, ct);
        var prefs = session.Items.FirstOrDefault();
        if (prefs is null)
        {
            prefs = new AppPrefs { Id = owner.ToString(), UpdatedAt = clock.GetUtcNow() };
            session.Items.Add(prefs);
        }
        prefs.Hidden = hidden;
        prefs.UpdatedAt = clock.GetUtcNow();
        await session.SaveAsync(prefs, ct);
        return View(prefs);
    }

    public void Forget(Guid owner) => cache.Forget(owner);

    public void Dispose() => cache.Dispose();

    private static AppPrefsView View(AppPrefs? prefs) => new(prefs?.Hidden ?? [], prefs?.UpdatedAt);
}