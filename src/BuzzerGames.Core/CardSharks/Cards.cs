namespace BuzzerGames.Core.CardSharks;

/// <summary>A playing card. Rank is 2 to 14 (jack 11, queen 12, king 13, ace 14: aces are high). Suit is S, H, D or C.</summary>
public readonly record struct Card(int Rank, char Suit)
{
    public string Label => Rank switch { 11 => "J", 12 => "Q", 13 => "K", 14 => "A", _ => Rank.ToString() };

    public string SuitSymbol => Suit switch { 'S' => "♠", 'H' => "♥", 'D' => "♦", _ => "♣" };

    public bool IsRed => Suit is 'H' or 'D';

    public override string ToString() => Label + SuitSymbol;

    /// <summary>A fresh, ordered 52-card deck.</summary>
    public static List<Card> FullDeck() =>
        [.. "SHDC".SelectMany(s => Enumerable.Range(2, 13).Select(r => new Card(r, s)))];
}
