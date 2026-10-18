namespace GameNet.Shared.Primitives;

public readonly record struct Money
{
    private Money(long amount, string currency)
    {
        Amount = amount;
        Currency = NormalizeCurrency(currency);
    }

    public long Amount { get; }
    public string Currency { get; }

    public static Money Zero(string currency) => new(0, currency);
    public static Money From(long amount, string currency) => new(amount, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new(checked(Amount + other.Amount), Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new(checked(Amount - other.Amount), Currency);
    }

    public Money Negate() => new(checked(-Amount), Currency);

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
            throw new InvalidOperationException("MONEY_CURRENCY_MISMATCH");
    }

    private static string NormalizeCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Currency is required.", nameof(currency));
        return currency.Trim().ToUpperInvariant();
    }
}
