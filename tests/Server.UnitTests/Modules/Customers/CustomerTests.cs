using GameNet.Server.Modules.Customers.Domain;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Customers;

public sealed class CustomerTests
{
    [Fact]
    public void Walk_in_customer_has_unique_stable_code_without_invented_profile_data()
    {
        var id = Guid.NewGuid();
        var created = new DateTimeOffset(2026, 10, 14, 15, 30, 0, TimeSpan.FromHours(3.5));

        var customer = Customer.CreateWalkIn(id, created);

        Assert.Equal(id, customer.Id);
        Assert.Equal(CustomerKind.WalkIn, customer.Kind);
        Assert.Null(customer.DisplayName);
        Assert.StartsWith("W-", customer.Code);
        Assert.Equal(34, customer.Code.Length);
        Assert.True(customer.IsActive);
        Assert.Equal(created.ToUniversalTime(), customer.CreatedAtUtc);
    }

    [Fact]
    public void Registered_customer_requires_name_and_normalizes_code_and_name()
    {
        var customer = Customer.CreateRegistered(
            Guid.NewGuid(), " c-001 ", "  Test Customer  ", DateTimeOffset.UtcNow);

        Assert.Equal("C-001", customer.Code);
        Assert.Equal("Test Customer", customer.DisplayName);
        Assert.Equal(CustomerKind.Registered, customer.Kind);
        Assert.Throws<ArgumentException>(() =>
            Customer.CreateRegistered(Guid.NewGuid(), "C-002", " ", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Walk_in_profile_cannot_be_renamed_and_deactivation_is_idempotent()
    {
        var customer = Customer.CreateWalkIn(Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => customer.Rename("Not a walk-in"));

        customer.Deactivate();
        var version = customer.Version;
        customer.Deactivate();

        Assert.False(customer.IsActive);
        Assert.Equal(version, customer.Version);
    }
}
