using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Rose.Infrastructure.Lingo.Translation;

/// <summary>
/// Runs an encoder-decoder transformer exported to ONNX (the Hugging Face Optimum layout) with
/// ONNX Runtime on the CPU. The decoder keeps a key/value cache ("past_key_values"), so each new
/// token only costs one small step instead of re-reading everything written so far.
/// </summary>
internal sealed class OnnxTranslationModel : ITranslationModel
{
    private const string PastPrefix = "past_key_values.";
    private const string PresentPrefix = "present.";

    private readonly InferenceSession _encoder;
    private readonly InferenceSession _firstDecoder;
    private readonly InferenceSession _pastDecoder;
    private readonly bool _merged;
    private readonly RunOptions _runOptions = new();

    private OnnxTranslationModel(InferenceSession encoder, InferenceSession firstDecoder, InferenceSession pastDecoder, bool merged)
    {
        _encoder = encoder;
        _firstDecoder = firstDecoder;
        _pastDecoder = pastDecoder;
        _merged = merged;
    }

    public static OnnxTranslationModel Load(NllbModelFiles files, int threads)
    {
        using var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            IntraOpNumThreads = threads,
            InterOpNumThreads = 1,
            // The arena keeps freed blocks for reuse and memory patterns pre-plan buffers per input
            // shape. Every sentence has a different length, so both mostly just hold on to memory.
            EnableCpuMemArena = false,
            EnableMemoryPattern = false,
        };
        var sessions = new List<InferenceSession>();
        try
        {
            InferenceSession Open(string path)
            {
                var session = new InferenceSession(path, options);
                sessions.Add(session);
                return session;
            }

            var encoder = Open(files.Encoder!);
            if (files.MergedDecoder is not null)
            {
                var merged = Open(files.MergedDecoder);
                return new OnnxTranslationModel(encoder, merged, merged, merged: true);
            }
            return new OnnxTranslationModel(encoder, Open(files.FirstDecoder!), Open(files.PastDecoder!), merged: false);
        }
        catch
        {
            sessions.ForEach(s => s.Dispose());
            throw;
        }
    }

    public IDecoderRun Encode(IReadOnlyList<long> sourceIds)
    {
        var shape = new long[] { 1, sourceIds.Count };
        var mask = OrtValue.CreateTensorValueFromMemory(Enumerable.Repeat(1L, sourceIds.Count).ToArray(), shape);
        try
        {
            using var ids = OrtValue.CreateTensorValueFromMemory(sourceIds.ToArray(), shape);
            var inputs = new Dictionary<string, OrtValue>(StringComparer.Ordinal);
            foreach (var name in _encoder.InputNames)
            {
                inputs[name] = name switch
                {
                    "input_ids" => ids,
                    "attention_mask" => mask,
                    _ => throw new InvalidOperationException($"The encoder wants an input ROSE does not know: {name}."),
                };
            }

            var outputs = _encoder.Run(_runOptions, inputs, [_encoder.OutputNames[0]]);
            return new DecoderRun(this, hidden: outputs[0], mask);
        }
        catch
        {
            mask.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _encoder.Dispose();
        _firstDecoder.Dispose();
        if (!_merged) _pastDecoder.Dispose();
        _runOptions.Dispose();
    }

    private sealed class DecoderRun(OnnxTranslationModel model, OrtValue hidden, OrtValue mask) : IDecoderRun
    {
        // "0.decoder.key" -> the cached keys of layer 0's self-attention, and so on.
        private readonly Dictionary<string, OrtValue> _cache = new(StringComparer.Ordinal);
        private long _length;

        public long Next(IReadOnlyList<long> tokens)
        {
            var first = _length == 0;
            var session = first ? model._firstDecoder : model._pastDecoder;
            var owned = new List<OrtValue>();
            try
            {
                OrtValue Own(OrtValue value)
                {
                    owned.Add(value);
                    return value;
                }

                var inputs = new Dictionary<string, OrtValue>(StringComparer.Ordinal);
                foreach (var (name, meta) in session.InputMetadata)
                {
                    inputs[name] = name switch
                    {
                        "input_ids" => Own(OrtValue.CreateTensorValueFromMemory(tokens.ToArray(), [1, tokens.Count])),
                        "encoder_hidden_states" => hidden,
                        "encoder_attention_mask" => mask,
                        "attention_mask" => Own(OrtValue.CreateTensorValueFromMemory(
                            Enumerable.Repeat(1L, (int)_length + tokens.Count).ToArray(), [1, _length + tokens.Count])),
                        "use_cache_branch" => Own(OrtValue.CreateTensorValueFromMemory(new[] { !first }, [1])),
                        _ when name.StartsWith(PastPrefix, StringComparison.Ordinal) =>
                            _cache.TryGetValue(name[PastPrefix.Length..], out var cached) ? cached : Own(EmptyCache(name, meta)),
                        _ => throw new InvalidOperationException($"The decoder wants an input ROSE does not know: {name}."),
                    };
                }

                var names = session.OutputNames;
                var outputs = session.Run(model._runOptions, inputs, names);
                long next = -1;
                for (var i = 0; i < names.Count; i++)
                {
                    var value = outputs[i];
                    if (names[i] == "logits")
                    {
                        next = ArgMaxOfLast(value);
                        value.Dispose();
                    }
                    else if (names[i].StartsWith(PresentPrefix, StringComparison.Ordinal))
                    {
                        Keep(names[i][PresentPrefix.Length..], value);
                    }
                    else
                    {
                        value.Dispose();
                    }
                }

                _length += tokens.Count;
                return next >= 0 ? next : throw new InvalidOperationException("The decoder has no \"logits\" output.");
            }
            finally
            {
                owned.ForEach(v => v.Dispose());
            }
        }

        public void Dispose()
        {
            foreach (var value in _cache.Values) value.Dispose();
            _cache.Clear();
            hidden.Dispose();
            mask.Dispose();
        }

        /// <summary>
        /// The decoder's own keys grow by one token each step, so they are replaced. The encoder's
        /// keys never change: the first step computes them and later steps reuse them.
        /// </summary>
        private void Keep(string key, OrtValue value)
        {
            var isEncoder = key.Contains(".encoder.", StringComparison.Ordinal);
            if (isEncoder && _cache.ContainsKey(key))
            {
                value.Dispose();
                return;
            }
            if (_cache.Remove(key, out var old)) old.Dispose();
            _cache[key] = value;
        }

        /// <summary>The merged decoder still wants a cache on its first step: an empty one, [1, heads, 0, head size].</summary>
        private static OrtValue EmptyCache(string name, NodeMetadata meta)
        {
            var dims = meta.Dimensions;
            if (dims.Length != 4 || dims[1] <= 0 || dims[3] <= 0)
                throw new InvalidOperationException($"Cannot tell the attention size of {name}.");
            return OrtValue.CreateAllocatedTensorValue(OrtAllocator.DefaultInstance, TensorElementType.Float, [1, dims[1], 0, dims[3]]);
        }

        /// <summary>logits are [1, tokens, vocabulary]: the next token is the best score in the last row.</summary>
        private static long ArgMaxOfLast(OrtValue logits)
        {
            var shape = logits.GetTensorTypeAndShape().Shape;
            var vocabulary = (int)shape[^1];
            var scores = logits.GetTensorDataAsSpan<float>();
            var last = scores[^vocabulary..];
            var best = 0;
            for (var i = 1; i < last.Length; i++)
            {
                if (last[i] > last[best]) best = i;
            }
            return best;
        }
    }
}
