using Xunit;
using GameNet.Shared.Primitives;
namespace GameNet.Shared.Tests;
public sealed class MoneyTests
{
    [Fact] public void Add_same_currency() => Assert.Equal(150, Money.From(100,"TOM").Add(Money.From(50,"TOM")).Amount);
    [Fact] public void Reject_currency_mismatch() => Assert.Throws<InvalidOperationException>(() => Money.From(100,"TOM").Add(Money.From(50,"USD")));
}
