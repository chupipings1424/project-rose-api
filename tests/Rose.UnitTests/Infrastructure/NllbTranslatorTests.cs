using Microsoft.Extensions.Logging.Abstractions;
using Rose.Domain.Common;
using Rose.Domain.Lingo;
using Rose.Infrastructure.Lingo;
using Rose.Infrastructure.Lingo.Translation;

namespace Rose.UnitTests.Infrastructure;

/// <summary>
/// Runs the real ONNX Runtime code path on the "echo" fixtures (see Fixtures/Nllb/make_fixtures.py):
/// a correct caller gets the source text back; a wrong cache or flag gives garbage.
/// </summary>
public sealed class NllbTranslatorTests
{
    private static readonly LingoOptions Options = new();

    private static NllbTranslator Real(string layout, TimeProvider? clock = null) => new(
        NllbModelFiles.In(Path.Combine(NllbVocabularyTests.Fixtures, layout)), Options, clock ?? TimeProvider.System, NullLogger<NllbTranslator>.Instance);

    [Theory]
    [InlineData("merged")]
    [InlineData("split")]
    public async Task Both_onnx_layouts_run_the_encoder_and_the_cached_decoder(string layout)
    {
        using var translator = Real(layout);

        Assert.Equal("Hello, how are you today?", await translator.TranslateAsync("Hello, how are you today?", Languages.English, Languages.Tagalog, CancellationToken.None));
        Assert.True(translator.IsLoaded);
    }

    [Fact]
    public async Task Each_sentence_and_line_is_translated_on_its_own()
    {
        using var translator = Real("merged");

        var text = "Thank you very much. Where is the water?\nI love you.";

        Assert.Equal(text, await translator.TranslateAsync(text, Languages.English, Languages.German, CancellationToken.None));
        Assert.Equal(
            "Thank you very much.Where is the water?\nI love you.",
            await translator.TranslateAsync(text, Languages.English, Languages.Japanese, CancellationToken.None));
    }

    [Fact]
    public void Sentences_keep_their_separators()
    {
        var parts = NllbTranslator.Sentences("Hi. How are you?\r\nOkay\u3002\u306F\u3044").ToList();

        Assert.Equal(["Hi.", " ", "How are you?", "\r\n", "Okay\u3002", "\u306F\u3044"], parts.Select(p => p.Text));
        Assert.Equal([false, true, false, true, false, false], parts.Select(p => p.IsSeparator));
    }

    [Fact]
    public void The_status_says_what_is_installed()
    {
        using var installed = Real("split");
        using var missing = new NllbTranslator(
            NllbModelFiles.In(Path.Combine(Path.GetTempPath(), "rose-no-model-" + Guid.NewGuid())), Options, TimeProvider.System, NullLogger<NllbTranslator>.Instance);

        Assert.Equal((true, false), (installed.Status().Installed, installed.Status().Loaded));
        Assert.Equal((false, NllbTranslator.NotInstalled), (missing.Status().Installed, missing.Status().Message));
    }

    [Fact]
    public async Task Without_the_model_translating_explains_how_to_install_it()
    {
        using var missing = new NllbTranslator(
            NllbModelFiles.In(Path.Combine(Path.GetTempPath(), "rose-no-model-" + Guid.NewGuid())), Options, TimeProvider.System, NullLogger<NllbTranslator>.Instance);

        var error = await Assert.ThrowsAsync<UnavailableException>(() => missing.TranslateAsync("Hi", "en", "tl", CancellationToken.None));
        Assert.Contains("setup-lingo.cmd", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_model_is_unloaded_when_idle_and_reloaded_on_the_next_translation()
    {
        var clock = new ManualTimers();
        var loads = new List<FakeModel>();
        using var translator = WithFake(clock, () =>
        {
            var model = new FakeModel();
            loads.Add(model);
            return model;
        });

        await translator.TranslateAsync("Hello", "en", "tl", CancellationToken.None);
        Assert.Equal(Options.UnloadAfterIdle, clock.LastDue);
        clock.Fire();

        Assert.False(translator.IsLoaded);
        Assert.True(loads[0].Disposed);

        await translator.TranslateAsync("Hello", "en", "tl", CancellationToken.None);
        Assert.Equal(2, loads.Count);
    }

    [Fact]
    public async Task A_broken_model_is_a_friendly_upstream_error()
    {
        using var translator = WithFake(new ManualTimers(), () => throw new InvalidDataException("corrupt"));

        var error = await Assert.ThrowsAsync<UpstreamException>(() => translator.TranslateAsync("Hello", "en", "tl", CancellationToken.None));
        Assert.Contains("setup-lingo.cmd", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Greedy_search_feeds_the_prefix_then_each_pick_and_stops_at_the_end_or_the_limit()
    {
        var model = new FakeModel { Script = [7, 8, 9, NllbVocabulary.Eos] };

        Assert.Equal([7L, 8, 9], GreedyDecoder.Generate(model, [1, 2], [NllbVocabulary.Eos, 50], NllbVocabulary.Eos, 10, CancellationToken.None));
        Assert.Equal([[NllbVocabulary.Eos, 50], [7L], [8L], [9L]], model.Fed);
        Assert.Equal([7L, 8], GreedyDecoder.Generate(new FakeModel { Script = [7, 8, 9, 10] }, [1], [2], NllbVocabulary.Eos, 2, CancellationToken.None));
    }

    /// <summary>The real tokenizer from the fixtures with a stand-in model.</summary>
    private static NllbTranslator WithFake(TimeProvider clock, Func<ITranslationModel> load) => new(
        NllbModelFiles.In(Path.Combine(NllbVocabularyTests.Fixtures, "merged")), Options, clock, NullLogger<NllbTranslator>.Instance, _ => load());

    private sealed class FakeModel : ITranslationModel
    {
        private int _step;

        public List<long> Script { get; init; } = [NllbVocabulary.Eos];
        public List<long[]> Fed { get; } = [];
        public bool Disposed { get; private set; }

        public IDecoderRun Encode(IReadOnlyList<long> sourceIds) => new Run(this);

        public void Dispose() => Disposed = true;

        private sealed class Run(FakeModel model) : IDecoderRun
        {
            public long Next(IReadOnlyList<long> tokens)
            {
                model.Fed.Add([.. tokens]);
                return model.Script[Math.Min(model._step++, model.Script.Count - 1)];
            }

            public void Dispose()
            {
            }
        }
    }

    /// <summary>Keeps the timer's callback so the test can fire it.</summary>
    private sealed class ManualTimers : TimeProvider
    {
        private TimerCallback? _callback;
        public TimeSpan LastDue { get; private set; }

        public void Fire() => _callback!(null);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            return new Timer(this);
        }

        private sealed class Timer(ManualTimers owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                owner.LastDue = dueTime;
                return true;
            }

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
