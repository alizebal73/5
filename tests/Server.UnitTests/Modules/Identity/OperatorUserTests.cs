using GameNet.Server.Modules.Identity.Domain;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Identity;

public sealed class OperatorUserTests
{
    [Fact]
    public void Username_is_normalized_and_failed_logins_lock_after_five_attempts()
    {
        var now = DateTimeOffset.Parse("2026-10-09T00:00:00Z");
        var user = OperatorUser.Create(Guid.NewGuid(), "  OPERATOR  ", "Test Operator", "safe-hash", now);
        Assert.Equal("operator", user.Username);
        for (var i = 0; i < 5; i++) user.RecordFailedLogin(now);
        Assert.True(user.IsLocked(now));
        Assert.False(user.IsLocked(now.AddMinutes(11)));
    }
    [Fact]
    public void Changing_password_resets_failed_login_lockout_without_changing_identity()
    {
        var now = DateTimeOffset.Parse("2026-10-09T00:00:00Z");
        var user = OperatorUser.Create(Guid.NewGuid(), "operator", "Test Operator", "old-hash", now);
        for (var i = 0; i < 5; i++) user.RecordFailedLogin(now);
        Assert.True(user.IsLocked(now));

        user.ChangePassword("new-hash");

        Assert.Equal("new-hash", user.PasswordHash);
        Assert.Equal("operator", user.Username);
        Assert.Equal(0, user.FailedLoginCount);
        Assert.False(user.IsLocked(now));
    }

    [Fact]
    public void Successful_login_clears_lockout_and_updates_last_login()
    {
        var now = DateTimeOffset.Parse("2026-10-09T00:00:00Z");
        var user = OperatorUser.Create(Guid.NewGuid(), "operator", "Test Operator", "safe-hash", now);
        for (var i = 0; i < 5; i++) user.RecordFailedLogin(now);
        var successTime = now.AddMinutes(2);
        user.RecordSuccessfulLogin(successTime);
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockoutUntilUtc);
        Assert.Equal(successTime, user.LastLoginAtUtc);
    }
    [Theory]
    [InlineData("")]
    [InlineData("has whitespace")]
    public void Invalid_username_is_rejected(string username) =>
        Assert.Throws<ArgumentException>(() => OperatorUser.Create(Guid.NewGuid(), username, "Name", "safe-hash", DateTimeOffset.UtcNow));
}
