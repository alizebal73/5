using GameNet.Server.Modules.Stations.Domain;

namespace GameNet.Server.UnitTests.Modules.Stations;

public sealed class StationDomainTests
{
    [Fact]
    public void New_station_starts_available()
    {
        var station = Station.Create(Guid.NewGuid(), "PC01", "PC 01", StationType.Pc);
        Assert.Equal(StationStatus.Available, station.Status);
    }

    [Fact]
    public void Maintenance_station_cannot_be_enabled()
    {
        var station = Station.Create(Guid.NewGuid(), "PC01", "PC 01", StationType.Pc);
        station.MarkMaintenance();

        var ex = Assert.Throws<InvalidOperationException>(() => station.Enable());
        Assert.Equal("STATION_IN_MAINTENANCE", ex.Message);
    }

    [Fact]
    public void Code_and_name_are_normalized()
    {
        var station = Station.Create(Guid.NewGuid(), " PC01 ", " PC 01 ", StationType.Pc);
        Assert.Equal("PC01", station.Code);
        Assert.Equal("PC 01", station.Name);
    }
}
