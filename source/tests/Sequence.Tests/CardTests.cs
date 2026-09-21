using Sequence.Domain;

namespace Sequence.Tests;

public class CardTests
{
    [Theory]
    [InlineData("7H")]
    [InlineData("10S")]
    [InlineData("JC")]
    [InlineData("AS")]
    [InlineData("QD")]
    public void Parse_And_Format_RoundTrip(string code)
    {
        Card card = Card.Parse(code);
        Assert.Equal(code, card.Code);
        Card parsed = Card.Parse(card.Code);
        Assert.Equal(card, parsed);
    }

    [Theory]
    [InlineData("JH", true, false)]
    [InlineData("JS", true, false)]
    [InlineData("JC", false, true)]
    [InlineData("JD", false, true)]
    [InlineData("AS", false, false)]
    public void Jack_Flags(string code, bool oneEyed, bool twoEyed)
    {
        Card card = Card.Parse(code);
        Assert.Equal(card.IsJack, oneEyed || twoEyed);
        Assert.Equal(oneEyed, card.IsOneEyedJack);
        Assert.Equal(twoEyed, card.IsTwoEyedJack);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1H")]
    [InlineData("JX")]
    [InlineData("H")]
    public void TryParse_Rejects_Invalid(string code)
    {
        Assert.False(Card.TryParse(code, out _));
    }
}