using GameNet.Server.Modules.Identity.Domain;

namespace GameNet.Server.UnitTests.Modules.Identity;

public sealed class IdentityDomainTests
{
    [Fact]
    public void Failed_logins_lock_account_after_five_attempts()
    {
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var user = OperatorUser.Create(
            Guid.NewGuid(),
            "admin",
            "Admin",
            "hash",
            now);

        for (var i = 0; i < 5; i++)
            user.RecordFailedLogin(now);

        Assert.True(user.IsLocked(now));
        Assert.True(user.IsLocked(now.AddMinutes(9)));
        Assert.False(user.IsLocked(now.AddMinutes(11)));
    }

    [Fact]
    public void Successful_login_resets_lockout_counters()
    {
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var user = OperatorUser.Create(
            Guid.NewGuid(),
            "admin",
            "Admin",
            "hash",
            now);

        user.RecordFailedLogin(now);
        user.RecordSuccessfulLogin(now.AddMinutes(1));

        Assert.False(user.IsLocked(now.AddMinutes(1)));
    }
}
