using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Rose.Application.Abstractions;
using Rose.Domain.Common;

namespace Rose.Infrastructure.Accounts;

/// <summary>The Supabase settings the server needs to manage logins (the "Supabase" section).</summary>
public sealed class SupabaseAdminOptions
{
    public const string Section = "Supabase";

    /// <summary>https://&lt;project-ref&gt;.supabase.co</summary>
    public string? Url { get; set; }

    /// <summary>
    /// The project's secret key (Supabase: Project Settings &gt; API Keys &gt; Secret keys).
    /// Server only - it bypasses every rule, so never put it in the web app.
    /// </summary>
    public string? SecretKey { get; set; }
}

/// <summary>Creates, changes and deletes logins through Supabase Auth's admin API.</summary>
internal sealed class SupabaseAccountProvider(IHttpClientFactory http, IOptions<SupabaseAdminOptions> options) : IAccountProvider
{
    public const string ClientName = "supabase-admin";

    private readonly SupabaseAdminOptions settings = options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(settings.SecretKey) && Uri.TryCreate(settings.Url, UriKind.Absolute, out _);

    public async Task<Guid> CreateAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post, "auth/v1/admin/users", new { email, password, email_confirm = true }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, cancellationToken);
            if (error.Code is "email_exists" or "user_already_exists" || response.StatusCode == HttpStatusCode.Conflict)
                throw new ConflictException("That email already has a login. Remove it in Supabase (Authentication > Users) or use another email.");
            throw Fail(response, error);
        }
        var user = await response.Content.ReadFromJsonAsync<SupabaseUser>(cancellationToken);
        return user?.Id ?? throw new InvalidOperationException("Supabase did not return the new user's id.");
    }

    public async Task SetPasswordAsync(Guid id, string password, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Put, $"auth/v1/admin/users/{id}", new { password }, cancellationToken);
        if (response.IsSuccessStatusCode) return;
        var error = await ReadErrorAsync(response, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new NotFoundException("That login no longer exists in Supabase.");
        throw Fail(response, error);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"auth/v1/admin/users/{id}", null, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound) return;
        throw Fail(response, await ReadErrorAsync(response, cancellationToken));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!IsConfigured) throw new UnavailableException("Managing accounts needs the Supabase secret key on the server (Supabase:SecretKey).");
        var baseUrl = new Uri(settings.Url!.TrimEnd('/') + "/");
        using var request = new HttpRequestMessage(method, new Uri(baseUrl, path));
        request.Headers.Add("apikey", settings.SecretKey);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.SecretKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await http.CreateClient(ClientName).SendAsync(request, ct);
    }

    private static async Task<SupabaseError> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<SupabaseError>(ct) ?? new SupabaseError(null, null, null);
        }
        catch (System.Text.Json.JsonException)
        {
            return new SupabaseError(null, null, null);
        }
    }

    /// <summary>Bad input (e.g. a password Supabase finds too weak) is the user's to fix; anything else is a server problem.</summary>
    private static Exception Fail(HttpResponseMessage response, SupabaseError error)
    {
        var message = error.Message ?? error.Description ?? $"Supabase answered {(int)response.StatusCode}.";
        return (int)response.StatusCode is 400 or 422
            ? new ValidationException(message)
            : new HttpRequestException($"Supabase admin API failed ({(int)response.StatusCode}): {message}", null, response.StatusCode);
    }

    private sealed record SupabaseUser([property: JsonPropertyName("id")] Guid Id);

    private sealed record SupabaseError(
        [property: JsonPropertyName("error_code")] string? Code,
        [property: JsonPropertyName("msg")] string? Message,
        [property: JsonPropertyName("error_description")] string? Description);
}