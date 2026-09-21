namespace Sequence.Domain;

public enum Suit
{
    Clubs = 0,
    Diamonds = 1,
    Hearts = 2,
    Spades = 3,
}

public enum Rank
{
    Ace = 1,
    Two = 2,
    Three = 3,
    Four = 4,
    Five = 5,
    Six = 6,
    Seven = 7,
    Eight = 8,
    Nine = 9,
    Ten = 10,
    Jack = 11,
    Queen = 12,
    King = 13,
}

/// <summary>
/// A single playing card, identified by rank and suit.
/// Value semantics: equality and hashing are derived from both fields.
/// </summary>
public readonly record struct Card(Rank Rank, Suit Suit)
{
    public bool IsJack => Rank == Rank.Jack;

    /// <summary>Jack of Hearts or Jack of Spades: removes an opponent chip.</summary>
    public bool IsOneEyedJack => IsJack && Suit is Suit.Hearts or Suit.Spades;

    /// <summary>Jack of Clubs or Jack of Diamonds: places a chip on any empty non-corner space.</summary>
    public bool IsTwoEyedJack => IsJack && Suit is Suit.Clubs or Suit.Diamonds;

    /// <summary>Short display code, e.g. "7H", "10S", "JC".</summary>
    public string Code => $"{RankCode}{SuitCode}";

    private string RankCode => Rank switch
    {
        Rank.Ace => "A",
        Rank.Ten => "10",
        Rank.Jack => "J",
        Rank.Queen => "Q",
        Rank.King => "K",
        var rank => ((int)rank).ToString(),
    };

    private char SuitCode => Suit switch
    {
        Suit.Clubs => 'C',
        Suit.Diamonds => 'D',
        Suit.Hearts => 'H',
        Suit.Spades => 'S',
        _ => '?',
    };

    public override string ToString() => Code;

    public static Card Parse(string text)
    {
        if (!TryParse(text, out var card))
        {
            throw new FormatException($"'{text}' is not a valid card code.");
        }

        return card;
    }

    public static bool TryParse(string? text, out Card card)
    {
        card = default;

        if (string.IsNullOrEmpty(text) || text.Length < 2)
        {
            return false;
        }

        Suit suit = text[^1] switch
        {
            'C' => Suit.Clubs,
            'D' => Suit.Diamonds,
            'H' => Suit.Hearts,
            'S' => Suit.Spades,
            _ => (Suit)(-1),
        };

        if (suit == (Suit)(-1))
        {
            return false;
        }

        string rankPart = text[..^1];
        Rank? rank = rankPart switch
        {
            "A" => Rank.Ace,
            "J" => Rank.Jack,
            "Q" => Rank.Queen,
            "K" => Rank.King,
            "10" => Rank.Ten,
            _ when int.TryParse(rankPart, out int number) && number is >= 2 and <= 9 => (Rank)number,
            _ => null,
        };

        if (rank is null)
        {
            return false;
        }

        card = new Card(rank.Value, suit);
        return true;
    }
}