using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Rose.Application.Abstractions;
using Rose.Domain.Common;
using Rose.Domain.Lingo;

namespace Rose.Infrastructure.Lingo.Translation;

/// <summary>
/// Offline translation with Meta's NLLB-200 (distilled, 600M parameters) running on this PC through
/// ONNX Runtime. Nothing is sent anywhere. The model is loaded on the first translation (a few
/// seconds), shared by every request one at a time, and unloaded again after a quiet spell.
/// </summary>
internal sealed partial class NllbTranslator : ITranslator, IDisposable
{
    public const string Engine = "NLLB-200 distilled 600M (offline)";
    public const string NotInstalled =
        "The offline translator is not installed yet. Run setup-lingo.cmd once (it needs internet), then restart ROSE.";

    private readonly NllbModelFiles _files;
    private readonly LingoOptions _options;
    private readonly ILogger<NllbTranslator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ITimer _idle;
    private readonly Func<NllbModelFiles, ITranslationModel> _load;
    private (ITranslationModel Model, NllbVocabulary Vocabulary)? _loaded;

    public NllbTranslator(NllbModelFiles files, LingoOptions options, TimeProvider clock, ILogger<NllbTranslator> logger)
        : this(files, options, clock, logger, f => OnnxTranslationModel.Load(f, options.Threads))
    {
    }

    internal NllbTranslator(NllbModelFiles files, LingoOptions options, TimeProvider clock, ILogger<NllbTranslator> logger, Func<NllbModelFiles, ITranslationModel> load)
    {
        _files = files;
        _options = options;
        _logger = logger;
        _load = load;
        _idle = clock.CreateTimer(_ => Unload(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public bool IsLoaded => _loaded is not null;

    public TranslatorStatus Status()
    {
        var installed = NllbModelFiles.In(_files.Directory).IsInstalled;
        var message = !installed ? NotInstalled
            : IsLoaded ? "Ready."
            : "Installed. The first translation takes a few seconds while the model loads.";
        return new TranslatorStatus(installed, IsLoaded, Engine, message);
    }

    public async Task<string> TranslateAsync(string text, string from, string to, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Model work is CPU-bound for seconds; Task.Run keeps it off the request's thread.
            return await Task.Run(() => Translate(text, from, to, ct), ct);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or RoseException))
        {
            _logger.LogError(ex, "Offline translation failed");
            throw new UpstreamException("The offline translator failed. Try again, or run setup-lingo.cmd to repair the model files.");
        }
        finally
        {
            _idle.Change(_options.UnloadAfterIdle, Timeout.InfiniteTimeSpan);
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _idle.Dispose();
        _loaded?.Model.Dispose();
        _loaded = null;
        _gate.Dispose();
    }

    /// <summary>Splits on line breaks and sentence ends; the separators are kept (Regex.Split keeps captured groups).</summary>
    internal static IEnumerable<(string Text, bool IsSeparator)> Sentences(string text) =>
        SentenceBreak().Split(text)
            .Where(part => part.Length > 0)
            .Select(part => (part, string.IsNullOrWhiteSpace(part)));

    private string Translate(string text, string from, string to, CancellationToken ct)
    {
        var (model, vocabulary) = _loaded ??= Load();
        var output = new StringBuilder();
        var spaceless = to is Languages.Japanese || Languages.IsChinese(to);

        foreach (var (part, isSeparator) in Sentences(text))
        {
            ct.ThrowIfCancellationRequested();
            if (isSeparator)
            {
                // Japanese and Chinese do not put spaces between sentences; line breaks stay.
                output.Append(spaceless && !part.Contains('\n', StringComparison.Ordinal) ? "" : part);
                continue;
            }
            var source = vocabulary.EncodeSource(part.Normalize(NormalizationForm.FormKC), from);
            var limit = Math.Min(_options.MaxTokens, (source.Count * 3) + 10);
            var ids = GreedyDecoder.Generate(model, source, vocabulary.DecoderPrefix(to), NllbVocabulary.Eos, limit, ct);
            output.Append(vocabulary.Decode(ids).Trim());
        }
        return output.ToString().Trim();
    }

    private (ITranslationModel, NllbVocabulary) Load()
    {
        var files = NllbModelFiles.In(_files.Directory);
        if (!files.IsInstalled) throw new UnavailableException(NotInstalled);

        var watch = Stopwatch.StartNew();
        var vocabulary = NllbVocabulary.Load(files.SentencePiece!);
        var model = _load(files);
        _logger.LogInformation("Loaded the offline translator from {Directory} in {Seconds:0.0}s", files.Directory, watch.Elapsed.TotalSeconds);
        return (model, vocabulary);
    }

    private void Unload()
    {
        // Busy translating? Then it is not idle; the timer is re-armed when that translation ends.
        try
        {
            if (!_gate.Wait(0)) return;
        }
        catch (ObjectDisposedException)
        {
            return; // ROSE is shutting down.
        }
        try
        {
            if (_loaded is not { } loaded) return;
            loaded.Model.Dispose();
            _loaded = null;
            _logger.LogInformation("Unloaded the offline translator after {Idle} without use", _options.UnloadAfterIdle);
        }
        finally
        {
            _gate.Release();
        }
    }

    // A line break, or the space after . ! ? (or right after the full-width ??? which need no space).
    [GeneratedRegex(@"(\r?\n+|(?<=[.!?\u2026])\s+|(?<=[\u3002\uFF01\uFF1F])\s*)")]
    private static partial Regex SentenceBreak();
}
