using GameNet.Server.Modules.Stations.Domain;
using Xunit;
namespace GameNet.Server.UnitTests.Modules.Stations;
public sealed class StationTests
{
    [Fact]
    public void Code_is_normalized_and_rejects_unsafe_characters()
    {
        var s = Station.Create(Guid.NewGuid(), " pc-01 ", "Front row", StationType.Pc);
        Assert.Equal("PC-01", s.Code);
        Assert.Throws<ArgumentException>(() => Station.Create(Guid.NewGuid(), "pc 01", "Front row", StationType.Pc));
    }
    [Fact]
    public void Rename_and_binding_increment_version()
    {
        var s = Station.Create(Guid.NewGuid(), "PC-01", "Front row", StationType.Pc);
        s.Rename("Back row");
        Assert.Equal(2, s.Version);
        s.BindAgent("device-001");
        Assert.Equal(3, s.Version);
        Assert.Equal("device-001", s.AgentDeviceId);
    }
    [Fact]
    public void Administrative_status_cannot_forge_recovery_state()
    {
        var s = Station.Create(Guid.NewGuid(), "PC-01", "Front row", StationType.Pc);
        Assert.Throws<InvalidOperationException>(() => s.SetAdministrativeStatus(StationStatus.RecoveryRequired));
        s.SetAdministrativeStatus(StationStatus.Maintenance);
        Assert.Equal(StationStatus.Maintenance, s.Status);
    }
}
