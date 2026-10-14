using GameNet.Server.Modules.Tariffs.Domain;
using GameNet.Shared.Contracts.V1.Stations;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Tariffs;

public sealed class TariffTests
{
    [Fact]
    public void Fixed_hourly_tariff_normalizes_code_and_preserves_integer_toman_rate()
    {
        var tariff = Tariff.CreateFixedHourly(
            Guid.NewGuid(), " pc-standard ", " PC Standard",
            StationTypeContract.Pc, 60_000L, true, DateTimeOffset.UtcNow);

        Assert.Equal("PC-STANDARD", tariff.Code);
        Assert.Equal(60_000L, tariff.HourlyRateToman);
        Assert.Equal(StationTypeContract.Pc, tariff.StationType);
        Assert.True(tariff.IsDefault);
        Assert.True(tariff.IsActive);
    }

    [Fact]
    public void Invalid_or_zero_hourly_rate_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Tariff.CreateFixedHourly(
            Guid.NewGuid(), "PC-1", "PC", StationTypeContract.Pc, 0L, true, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Rate_change_increments_version_and_deactivation_removes_default()
    {
        var tariff = Tariff.CreateFixedHourly(
            Guid.NewGuid(), "PC-1", "PC", StationTypeContract.Pc, 60_000L, true, DateTimeOffset.UtcNow);
        var version = tariff.Version;

        tariff.ChangeHourlyRate(72_000L);
        Assert.Equal(72_000L, tariff.HourlyRateToman);
        Assert.Equal(version + 1, tariff.Version);

        tariff.Deactivate();
        Assert.False(tariff.IsActive);
        Assert.False(tariff.IsDefault);
        Assert.Throws<InvalidOperationException>(() => tariff.SetDefault(true));
    }
}
