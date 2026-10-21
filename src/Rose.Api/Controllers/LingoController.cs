using Microsoft.AspNetCore.Mvc;
using Rose.Application.Lingo;
using Rose.Application.Tracker;
using Rose.Domain.Lingo;

namespace Rose.Api.Controllers;

/// <summary>ROSE Lingo: translate, flashcards with spaced repetition, the daily quiz and kana drills.</summary>
[ApiController]
[Route("api/lingo")]
[Produces("application/json")]
[Tags("ROSE Lingo")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class LingoController(LingoService lingo) : ControllerBase
{
    /// <summary>The languages you can learn: ja, ko, de and en.</summary>
    [HttpGet("languages")]
    public IReadOnlyList<LanguageInfo> Languages() => LingoService.AllLanguages();

    /// <summary>Every hiragana with its katakana and romaji, for the kana drills.</summary>
    [HttpGet("kana")]
    public IReadOnlyList<KanaEntry> KanaChart() => LingoService.KanaChart();

    /// <summary>Translate up to 500 characters with MyMemory (online; do not send anything private).</summary>
    /// <response code="502">The translator is offline or today's free limit is used up.</response>
    [HttpPost("translate")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public Task<TranslationResult> Translate(TranslateRequest request, CancellationToken ct) =>
        lingo.TranslateAsync(request, ct);

    /// <summary>Your cards, newest first. Filter with <c>?language=ja</c>.</summary>
    [HttpGet("cards")]
    public Task<IReadOnlyList<CardDto>> Cards(string? language, CancellationToken ct) => lingo.CardsAsync(language, ct);

    /// <summary>Cards to review now in one language, most overdue first.</summary>
    [HttpGet("cards/due")]
    public Task<IReadOnlyList<CardDto>> Due([FromQuery] string? language, CancellationToken ct) => lingo.DueAsync(language, ct);

    /// <summary>Add a card. Japanese kana and Korean get their reading filled in automatically.</summary>
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

    /// <summary>Add the built-in everyday words for ja, ko or de. Words you already have are skipped.</summary>
    [HttpPost("starter/{language}")]
    public Task<StarterImportResult> ImportStarter(string language, CancellationToken ct) => lingo.ImportStarterAsync(language, ct);

    /// <summary>Today's five questions for one language (the same all day). Needs at least 4 cards.</summary>
    [HttpGet("quiz")]
    public Task<QuizDto> Quiz([FromQuery] string? language, CancellationToken ct) => lingo.QuizAsync(language, ct);

    /// <summary>Save today's score. Wrong answers go back into today's review.</summary>
    /// <response code="409">Today's quiz for that language is already done.</response>
    [HttpPost("quiz")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<QuizResultDto> Submit(QuizSubmitRequest request, CancellationToken ct) => lingo.SubmitQuizAsync(request, ct);

    /// <summary>Card counts, the quiz streak and whether today's quiz is done. Optional <c>?language=</c>.</summary>
    [HttpGet("stats")]
    public Task<LingoStats> Stats(string? language, CancellationToken ct) => lingo.StatsAsync(language, ct);
}
