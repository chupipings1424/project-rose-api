namespace Rose.Infrastructure.Lingo.Translation;

/// <summary>A loaded sequence-to-sequence model: read the source once, then write the output token by token.</summary>
internal interface ITranslationModel : IDisposable
{
    /// <summary>Runs the encoder over the source ids.</summary>
    IDecoderRun Encode(IReadOnlyList<long> sourceIds);
}

/// <summary>One translation in progress. It remembers what it has written (the key/value cache).</summary>
internal interface IDecoderRun : IDisposable
{
    /// <summary>Feeds the next token(s) and returns the most likely token after them.</summary>
    long Next(IReadOnlyList<long> tokens);
}

/// <summary>
/// Greedy search: always take the single most likely next token. Beam search (keeping the best
/// few candidates) is a little better but several times slower; greedy is plenty for short phrases.
/// </summary>
internal static class GreedyDecoder
{
    public static List<long> Generate(ITranslationModel model, IReadOnlyList<long> sourceIds, IReadOnlyList<long> prefix, long eos, int maxTokens, CancellationToken ct)
    {
        using var run = model.Encode(sourceIds);
        var output = new List<long>();
        var next = run.Next(prefix);
        while (next != eos && output.Count < maxTokens)
        {
            ct.ThrowIfCancellationRequested();
            output.Add(next);
            next = run.Next([next]);
        }
        return output;
    }
}
