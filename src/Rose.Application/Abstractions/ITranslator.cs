namespace Rose.Application.Abstractions;

/// <summary>
/// Machine translation. Throws <see cref="Rose.Domain.Common.UpstreamException"/> when the engine fails
/// and <see cref="Rose.Domain.Common.UnavailableException"/> when it is not installed.
/// </summary>
public interface ITranslator
{
    Task<string> TranslateAsync(string text, string from, string to, CancellationToken ct);

    /// <summary>Is the engine installed and loaded? Cheap: never loads the model.</summary>
    TranslatorStatus Status();
}

/// <param name="Installed">The model files are on this PC.</param>
/// <param name="Loaded">The model is in memory now (the first translation after a while loads it).</param>
/// <param name="Engine">What does the translating, e.g. "NLLB-200 (offline)".</param>
/// <param name="Message">A sentence for the user: what to do if it is not installed.</param>
public sealed record TranslatorStatus(bool Installed, bool Loaded, string Engine, string Message);
