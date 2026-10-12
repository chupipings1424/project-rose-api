using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Rose.Api.Auth;
using Rose.Infrastructure.Reminders;

namespace Rose.Api.Reminders;

/// <summary>Gives the desktop popup this server's address and a one-day token for its callback.</summary>
internal sealed class AlarmCallback(IServer server, TokenService tokens) : IAlarmCallback
{
    public string? BaseUrl =>
        server.Features.Get<IServerAddressesFeature>()?.Addresses
            .Select(a => a.Replace("://+", "://127.0.0.1", StringComparison.Ordinal).Replace("://*", "://127.0.0.1", StringComparison.Ordinal))
            .OrderBy(a => a.StartsWith("https", StringComparison.Ordinal))
            .FirstOrDefault();

    public string CreateToken() => tokens.Create("desktop-alarm", TimeSpan.FromDays(1)).AccessToken;
}