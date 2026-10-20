using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Modules.Customers.Infrastructure;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Customers;

public sealed class CustomerPasswordHasherTests
{
    [Fact]
    public void Hash_is_salted_and_verifies_only_the_same_password()
    {
        var hasher = new CustomerPasswordHasher();
        var first = hasher.HashPassword("good customer password 123");
        var second = hasher.HashPassword("good customer password 123");
        Assert.NotEqual(first, second);
        Assert.True(hasher.VerifyPassword("good customer password 123", first));
        Assert.False(hasher.VerifyPassword("wrong customer password 123", first));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("contains\nnewline")]
    public void Rejects_invalid_passwords(string password)
    {
        var hasher = new CustomerPasswordHasher();
        Assert.False(hasher.IsValidPassword(password));
        Assert.Throws<ArgumentException>(() => hasher.HashPassword(password));
    }

    [Fact]
    public void Failed_password_attempts_lock_customer_after_five_tries()
    {
        var customer = Customer.CreateRegistered(Guid.NewGuid(), "CUS-001", "Test Customer", DateTimeOffset.UtcNow);
        customer.SetCredentials("player-001", new CustomerPasswordHasher().HashPassword("a valid customer password"));
        Assert.Equal("PLAYER-001", customer.LoginUsername);
        Assert.Equal("CUS-001", customer.Code);
        var now = new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 4; i++) customer.RecordFailedPasswordAttempt(now);
        Assert.False(customer.IsPasswordLoginLocked(now));
        customer.RecordFailedPasswordAttempt(now);
        Assert.True(customer.IsPasswordLoginLocked(now));
        Assert.False(customer.IsPasswordLoginLocked(now.AddMinutes(11)));
        customer.RecordSuccessfulPasswordLogin();
        Assert.Null(customer.PasswordLockedUntilUtc);
        Assert.Equal(0, customer.PasswordFailedAttempts);
    }

    [Fact]
    public void Walk_in_customer_cannot_have_customer_password()
    {
        var customer = Customer.CreateWalkIn(Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => customer.SetCredentials("walkin-user", new CustomerPasswordHasher().HashPassword("a valid customer password")));
    }
}
