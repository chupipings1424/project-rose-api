namespace Rose.Application.Abstractions;

/// <summary>Machine translation. Throws <see cref="Rose.Domain.Common.UpstreamException"/> when the service fails.</summary>
public interface ITranslator
{
    Task<string> TranslateAsync(string text, string from, string to, CancellationToken ct);
}
