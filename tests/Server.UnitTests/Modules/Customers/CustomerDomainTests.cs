using GameNet.Server.Modules.Customers.Domain;

namespace GameNet.Server.UnitTests.Modules.Customers;

public sealed class CustomerDomainTests
{
    [Fact]
    public void Customer_code_is_normalized()
    {
        var customer = Customer.Create(
            Guid.NewGuid(),
            " c001 ",
            "Ali",
            null,
            null,
            new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal("C001", customer.Code);
    }

    [Fact]
    public void Profile_update_increments_version()
    {
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var customer = Customer.Create(
            Guid.NewGuid(),
            "C001",
            "Ali",
            null,
            null,
            now);

        customer.UpdateProfile("Ali Reza", "09120000000", now.AddMinutes(1));

        Assert.Equal(2, customer.Version);
        Assert.Equal("Ali Reza", customer.DisplayName);
    }
}
