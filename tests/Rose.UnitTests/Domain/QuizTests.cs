using Rose.Domain.Lingo;

namespace Rose.UnitTests.Domain;

public sealed class QuizTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);

    private static List<Card> Deck(int count) =>
        StarterDecks.All[Languages.German].Take(count)
            .Select(w => new Card(Languages.German, w.Term, w.Reading, w.Meaning, w.Notes, DateTimeOffset.UnixEpoch, Today))
            .ToList();

    [Fact]
    public void Each_question_has_four_distinct_choices_and_the_right_answer()
    {
        var deck = Deck(10);

        var quiz = Quiz.Build(deck, Quiz.PickRandom(deck, 5, new Random(1)), new Random(2));

        Assert.Equal(5, quiz.Count);
        Assert.Equal(5, quiz.Select(q => q.CardId).Distinct().Count());
        Assert.Equal([1, 2, 3, 4, 5], quiz.Select(q => q.Number));
        foreach (var question in quiz)
        {
            var card = deck.Single(c => c.Id == question.CardId);
            var expected = question.Kind == QuizKind.Term ? card.Term : card.Meaning;
            Assert.Equal(expected, question.Choices[question.Answer]);
            Assert.Equal(4, question.Choices.Count);
            Assert.Equal(question.Choices.Count, question.Choices.Distinct().Count());
        }
    }

    [Fact]
    public void A_made_quiz_asks_only_the_chosen_cards_but_borrows_wrong_answers_from_the_deck()
    {
        var deck = Deck(10);
        var chosen = deck.Take(2).ToList();

        var quiz = Quiz.Build(deck, chosen, new Random(3));

        Assert.Equal(chosen.Select(c => c.Id).Order(), quiz.Select(q => q.CardId).Order());
        Assert.All(quiz, q => Assert.Equal(4, q.Choices.Count));
    }

    [Fact]
    public void A_fixed_seed_gives_the_same_quiz()
    {
        var deck = Deck(10);
        static string Shape(IReadOnlyList<QuizQuestion> quiz) =>
            string.Join(';', quiz.Select(q => $"{q.CardId}|{q.Kind}|{string.Join(',', q.Choices)}"));

        Assert.Equal(Shape(Quiz.Build(deck, deck, new Random(7))), Shape(Quiz.Build(deck, deck, new Random(7))));
    }

    [Fact]
    public void Too_few_cards_means_no_quiz()
    {
        Assert.Empty(Quiz.Build(Deck(3), Deck(3), new Random(1)));
        Assert.Equal(4, Quiz.Build(Deck(4), Deck(4), new Random(1)).Count);
    }

    [Fact]
    public void Picking_more_than_the_deck_takes_the_whole_deck() =>
        Assert.Equal(6, Quiz.PickRandom(Deck(6), 20, new Random(1)).Count);

    [Fact]
    public void A_quiz_never_has_more_than_the_maximum_questions()
    {
        var deck = Enumerable.Range(1, Quiz.MaxQuestions + 5)
            .Select(i => new Card(Languages.German, $"Wort {i}", "", $"Word {i}", "", DateTimeOffset.UnixEpoch, Today))
            .ToList();

        Assert.Equal(Quiz.MaxQuestions, Quiz.Build(deck, deck, new Random(1)).Count);
    }

    [Fact]
    public void A_result_keeps_its_mode_and_clamps_the_score()
    {
        var result = new QuizResult(Today, "DE", QuizMode.Custom, 9, 5, DateTimeOffset.UnixEpoch);

        Assert.Equal((Languages.German, QuizMode.Custom, 5, 5), (result.Language, result.Mode, result.Score, result.Total));
    }
}
