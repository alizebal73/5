using GameNet.Server.Modules.Identity.Domain;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Identity;

public sealed class RoleTests
{
    [Fact]
    public void Owner_role_code_is_allowed_for_bootstrap_system_role()
    {
        var role = Role.Create(Guid.NewGuid(), "owner", "Owner");
        Assert.Equal("owner", role.Code);
    }

    [Theory]
    [InlineData("has whitespace")]
    [InlineData("1starts-with-digit")]
    [InlineData("bad/slash")]
    public void Unsafe_role_codes_are_rejected(string code) =>
        Assert.Throws<ArgumentException>(() => Role.Create(Guid.NewGuid(), code, "Test"));

    [Fact]
    public void Role_code_is_normalized_to_lowercase()
    {
        var role = Role.Create(Guid.NewGuid(), "Operator.Basic", "Operator Basic");
        Assert.Equal("operator.basic", role.Code);
    }
}
