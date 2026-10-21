using Microsoft.AspNetCore.Mvc;
using Rose.Application.Abstractions;
using Rose.Application.Lingo;
using Rose.Application.Tracker;
using Rose.Domain.Lingo;

namespace Rose.Api.Controllers;

/// <summary>ROSE Lingo: offline translation, flashcards with spaced repetition, practice quizzes and alphabet drills.</summary>
[ApiController]
[Route("api/lingo")]
[Produces("application/json")]
[Tags("ROSE Lingo")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class LingoController(LingoService lingo) : ControllerBase
{
    /// <summary>The languages: tl, en, ja, ko, zh-Hans, zh-Hant and de.</summary>
    [HttpGet("languages")]
    public IReadOnlyList<LanguageInfo> Languages() => LingoService.AllLanguages();

    /// <summary>Every hiragana with its katakana and romaji, for the kana drills.</summary>
    [HttpGet("kana")]
    public IReadOnlyList<KanaEntry> KanaChart() => LingoService.KanaChart();

    /// <summary>Every Hangul letter with its romanization, name and an example block, for the Hangul drills.</summary>
    [HttpGet("hangul")]
    public IReadOnlyList<HangulEntry> HangulChart() => LingoService.HangulChart();

    /// <summary>Is the offline translation model installed (run setup-lingo.cmd) and loaded?</summary>
    [HttpGet("translator")]
    public TranslatorStatus Translator() => lingo.TranslatorStatus();

    /// <summary>Translate up to 500 characters on this PC with NLLB-200. Nothing leaves your computer.</summary>
    /// <response code="502">The model failed to translate.</response>
    /// <response code="503">The model is not installed yet: run setup-lingo.cmd.</response>
    [HttpPost("translate")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<TranslationResult> Translate(TranslateRequest request, CancellationToken ct) =>
        lingo.TranslateAsync(request, ct);

    /// <summary>Your cards, newest first. Filter with <c>?language=ja</c>.</summary>
    [HttpGet("cards")]
    public Task<IReadOnlyList<CardDto>> Cards(string? language, CancellationToken ct) => lingo.CardsAsync(language, ct);

    /// <summary>Cards to review now in one language, most overdue first.</summary>
    [HttpGet("cards/due")]
    public Task<IReadOnlyList<CardDto>> Due([FromQuery] string? language, CancellationToken ct) => lingo.DueAsync(language, ct);

    /// <summary>Add a card. Japanese kana, Korean and Chinese get their reading filled in automatically.</summary>
    /// <response code="409">You already have a card for that word.</response>
    [HttpPost("cards")]
    [ProducesResponseType<CardDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CardRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await lingo.CreateCardAsync(request, ct));

    /// <summary>Edit a card's text. Its review schedule is kept.</summary>
    [HttpPatch("cards/{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<CardDto> Update(string id, CardRequest request, CancellationToken ct) => lingo.UpdateCardAsync(id, request, ct);

    /// <summary>Delete a card.</summary>
    [HttpDelete("cards/{id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<OkResponse> Delete(string id, CancellationToken ct)
    {
        await lingo.DeleteCardAsync(id, ct);
        return new OkResponse();
    }

    /// <summary>How well you remembered it (again, hard, good, easy) decides when you see it next.</summary>
    [HttpPost("cards/{id}/review")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<CardDto> Review(string id, ReviewRequest request, CancellationToken ct) => lingo.ReviewAsync(id, request, ct);

    /// <summary>Add the built-in everyday words for any language but English. Words you already have are skipped.</summary>
    [HttpPost("starter/{language}")]
    public Task<StarterImportResult> ImportStarter(string language, CancellationToken ct) => lingo.ImportStarterAsync(language, ct);

    /// <summary>
    /// A new quiz: <c>count</c> 5, 10 or 20 random cards ("Take a quiz"), or the <c>card_ids</c> you
    /// picked ("Make a quiz"). Needs at least 4 cards in the language for the multiple choices.
    /// </summary>
    [HttpPost("quiz")]
    public Task<QuizDto> Quiz(QuizRequest request, CancellationToken ct) => lingo.QuizAsync(request, ct);

    /// <summary>Save a finished quiz. Wrong answers go back into today's review.</summary>
    [HttpPost("quiz/results")]
    public Task<QuizResultDto> Submit(QuizSubmitRequest request, CancellationToken ct) => lingo.SubmitQuizAsync(request, ct);

    /// <summary>Your last 30 quiz scores in one language, newest first.</summary>
    [HttpGet("quiz/results")]
    public Task<IReadOnlyList<QuizResultDto>> History([FromQuery] string? language, CancellationToken ct) =>
        lingo.QuizHistoryAsync(language, ct);

    /// <summary>Card counts: total, due now and learned. Optional <c>?language=</c>.</summary>
    [HttpGet("stats")]
    public Task<LingoStats> Stats(string? language, CancellationToken ct) => lingo.StatsAsync(language, ct);
}
