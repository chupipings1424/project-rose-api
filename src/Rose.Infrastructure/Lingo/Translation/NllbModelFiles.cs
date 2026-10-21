namespace Rose.Infrastructure.Lingo.Translation;

/// <summary>
/// Where the model lives on disk (setup-lingo.cmd downloads it into data/models/nllb-200-distilled-600M).
/// Two ONNX exports exist: one merged decoder, or a first-step decoder plus a "with past" decoder.
/// Quantized (int8, ~4x smaller) files are preferred over full-size ones when both are there.
/// </summary>
internal sealed record NllbModelFiles(string Directory, string? Encoder, string? MergedDecoder, string? FirstDecoder, string? PastDecoder, string? SentencePiece)
{
    public const string SentencePieceFile = "sentencepiece.bpe.model";

    public bool IsInstalled =>
        Encoder is not null && SentencePiece is not null && (MergedDecoder is not null || (FirstDecoder is not null && PastDecoder is not null));

    public static NllbModelFiles In(string directory) => new(
        directory,
        Find(directory, "encoder_model"),
        Find(directory, "decoder_model_merged"),
        Find(directory, "decoder_model"),
        Find(directory, "decoder_with_past_model"),
        Exists(Path.Combine(directory, SentencePieceFile)));

    private static string? Find(string directory, string name) =>
        Exists(Path.Combine(directory, $"{name}_quantized.onnx")) ?? Exists(Path.Combine(directory, $"{name}.onnx"));

    private static string? Exists(string path) => File.Exists(path) ? path : null;
}
