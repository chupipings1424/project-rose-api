namespace Rose.Domain.Lingo;

public sealed record StarterWord(string Term, string Reading, string Meaning, string Notes = "");

/// <summary>A few everyday words per language, so the review and the quiz work from day one.</summary>
public static class StarterDecks
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<StarterWord>> All =
        new Dictionary<string, IReadOnlyList<StarterWord>>
        {
            [Languages.Japanese] =
            [
                new("こんにちは", "", "Hello"),
                new("ありがとう", "", "Thank you"),
                new("おはよう", "", "Good morning"),
                new("こんばんは", "", "Good evening"),
                new("さようなら", "", "Goodbye"),
                new("はい", "", "Yes"),
                new("いいえ", "", "No"),
                new("すみません", "", "Excuse me / Sorry"),
                new("おいしい", "", "Delicious"),
                new("がんばって", "", "Do your best!"),
                new("水", "mizu", "Water", "みず"),
                new("猫", "neko", "Cat", "ねこ"),
                new("犬", "inu", "Dog", "いぬ"),
                new("友達", "tomodachi", "Friend", "ともだち"),
                new("大丈夫", "daijoubu", "It's okay / All right", "だいじょうぶ"),
            ],
            [Languages.Korean] =
            [
                new("안녕하세요", "", "Hello"),
                new("감사합니다", "", "Thank you"),
                new("네", "", "Yes"),
                new("아니요", "", "No"),
                new("죄송합니다", "", "I'm sorry"),
                new("사랑해요", "", "I love you"),
                new("괜찮아요", "", "It's okay"),
                new("맛있어요", "", "It's delicious"),
                new("물", "", "Water"),
                new("친구", "", "Friend"),
                new("고양이", "", "Cat"),
                new("강아지", "", "Puppy"),
                new("화이팅", "", "Fighting! (You can do it)"),
            ],
            [Languages.German] =
            [
                new("Hallo", "", "Hello"),
                new("Danke", "", "Thank you"),
                new("Bitte", "", "Please / You're welcome"),
                new("Ja", "", "Yes"),
                new("Nein", "", "No"),
                new("Guten Morgen", "", "Good morning"),
                new("Tschüss", "", "Bye"),
                new("Entschuldigung", "", "Excuse me / Sorry"),
                new("Wasser", "", "Water"),
                new("der Freund", "", "Friend"),
                new("die Katze", "", "Cat"),
                new("der Hund", "", "Dog"),
                new("lecker", "", "Delicious"),
                new("Ich liebe dich", "", "I love you"),
            ],
        };
}
