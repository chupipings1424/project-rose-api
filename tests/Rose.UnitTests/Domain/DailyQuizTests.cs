using Rose.Domain.Lingo;

namespace Rose.UnitTests.Domain;

public sealed class DailyQuizTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);

    private static List<Card> Deck(int count) =>
        StarterDecks.All[Languages.German].Take(count)
            .Select(w => new Card(Languages.German, w.Term, w.Reading, w.Meaning, w.Notes, DateTimeOffset.UnixEpoch, Today))
            .ToList();

    [Fact]
    public void Five_questions_whose_answer_is_the_right_card()
    {
        var cards = Deck(10);

        var quiz = DailyQuiz.Build(cards, Languages.German, Today);

        Assert.Equal(DailyQuiz.Size, quiz.Count);
        Assert.Equal(5, quiz.Select(q => q.CardId).Distinct().Count());
        foreach (var question in quiz)
        {
            var card = cards.Single(c => c.Id == question.CardId);
            var expected = question.Kind == QuizKind.Term ? card.Term : card.Meaning;
            Assert.Equal(expected, question.Choices[question.Answer]);
            Assert.Equal(4, question.Choices.Count);
            Assert.Equal(question.Choices.Count, question.Choices.Distinct().Count());
        }
    }

    [Fact]
    public void The_same_day_gives_the_same_quiz()
    {
        var cards = Deck(10);

        var first = DailyQuiz.Build(cards, Languages.German, Today);
        var again = DailyQuiz.Build(cards, Languages.German, Today);

        Assert.Equal(first.Select(q => (q.CardId, q.Kind, string.Join('|', q.Choices))), again.Select(q => (q.CardId, q.Kind, string.Join('|', q.Choices))));
    }

    [Fact]
    public void Too_few_cards_means_no_quiz()
    {
        Assert.Empty(DailyQuiz.Build(Deck(3), Languages.German, Today));
        Assert.Equal(4, DailyQuiz.Build(Deck(4), Languages.German, Today).Count);
    }

    [Fact]
    public void The_seed_is_stable_across_runs() =>
        Assert.Equal(-1819880700, DailyQuiz.StableSeed("2026-10-21|ja"));

    [Fact]
    public void Streak_counts_consecutive_days_up_to_today_or_yesterday()
    {
        DateOnly[] days = [Today, Today.AddDays(-1), Today.AddDays(-2), Today.AddDays(-4)];
        Assert.Equal(3, Streak.Count(days, Today));
        Assert.Equal(3, Streak.Count(days, Today.AddDays(1)));
        Assert.Equal(5, Streak.Count(days.Append(Today.AddDays(-3)), Today));
        Assert.Equal(2, Streak.Count(days.Skip(1), Today));
        Assert.Equal(0, Streak.Count(days, Today.AddDays(3)));
    }
}
