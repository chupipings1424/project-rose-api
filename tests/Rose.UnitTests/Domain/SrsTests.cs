using Rose.Domain.Common;
using Rose.Domain.Lingo;

namespace Rose.UnitTests.Domain;

public sealed class SrsTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);

    [Fact]
    public void Good_answers_space_reviews_further_apart()
    {
        var state = SrsState.New(Today);
        var gaps = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            state = Srs.Next(state, ReviewGrade.Good, Today);
            gaps.Add(state.IntervalDays);
        }

        Assert.Equal([1, 6, 15, 38], gaps);
        Assert.Equal(Today.AddDays(38), state.DueOn);
    }

    [Fact]
    public void Again_resets_the_card_to_today_and_lowers_the_ease()
    {
        var learned = Srs.Next(Srs.Next(SrsState.New(Today), ReviewGrade.Good, Today), ReviewGrade.Good, Today);

        var forgot = Srs.Next(learned, ReviewGrade.Again, Today);

        Assert.Equal(0, forgot.Repetitions);
        Assert.Equal(Today, forgot.DueOn);
        Assert.Equal(1, forgot.Lapses);
        Assert.Equal(2.3, forgot.Ease);
    }

    [Fact]
    public void Ease_never_drops_below_the_minimum()
    {
        var state = SrsState.New(Today);
        for (var i = 0; i < 20; i++) state = Srs.Next(state, ReviewGrade.Again, Today);

        Assert.Equal(Srs.MinEase, state.Ease);
    }

    [Fact]
    public void Easy_first_answer_skips_ahead()
    {
        Assert.Equal(4, Srs.Next(SrsState.New(Today), ReviewGrade.Easy, Today).IntervalDays);
    }

    [Fact]
    public void A_card_validates_its_text_and_fills_in_the_reading()
    {
        var card = new Card("KO", " 감사합니다 ", "", "Thank you", "", DateTimeOffset.UnixEpoch, Today);
        Assert.Equal("ko", card.Language);
        Assert.Equal("gamsahamnida", card.Reading);
        Assert.True(card.IsDue(Today));

        card.Review(ReviewGrade.Good, Today);
        Assert.False(card.IsDue(Today));
        Assert.Equal(Today, card.LastReviewedOn);

        Assert.Throws<ValidationException>(() => new Card("ja", " ", "", "x", "", DateTimeOffset.UnixEpoch, Today));
        Assert.Throws<ValidationException>(() => new Card("fr", "Bonjour", "", "Hello", "", DateTimeOffset.UnixEpoch, Today));
        Assert.Throws<ValidationException>(() => card.Review((ReviewGrade)9, Today));
    }
}
