using GameNet.Desktop.Features.Stations;
using GameNet.Shared.Contracts.V1.Stations;
using Xunit;

namespace GameNet.Desktop.Tests;

public sealed class StationBoardFilterTests
{
    private static StationBoardItem PcStation() => new(new StationResponse(
        Guid.Parse("c31e4c7e-806f-4d42-9f33-9940ac4ab351"),
        "PC-01", "Front PC", StationTypeContract.Pc, StationStatusContract.Available, 3,
        "agent-front-01", true, DateTimeOffset.Parse("2026-10-09T10:00:00Z"), "1.2.3", "Ready"));

    private static StationBoardItem Ps5Station() => new(new StationResponse(
        Guid.Parse("c638fe1b-0de3-4a40-9e8d-873c7afc4c2d"),
        "PS-02", "PlayStation Area", StationTypeContract.Ps5, StationStatusContract.Maintenance, 1,
        null, false, null, null, null));

    [Theory]
    [InlineData("pc-01")]
    [InlineData("front pc")]
    [InlineData("AGENT-FRONT-01")]
    [InlineData("available")]
    [InlineData("online")]
    public void Search_matches_code_name_device_and_status_without_case_sensitivity(string query)
    {
        Assert.True(StationBoardFilter.Matches(PcStation(), query, "All", "All"));
    }

    [Fact]
    public void Type_and_status_filters_are_combined_with_and_semantics()
    {
        var pc = PcStation();
        var ps5 = Ps5Station();

        Assert.True(StationBoardFilter.Matches(pc, "", "PC", "Available"));
        Assert.False(StationBoardFilter.Matches(pc, "", "PS5", "Available"));
        Assert.False(StationBoardFilter.Matches(ps5, "", "PS5", "Available"));
        Assert.True(StationBoardFilter.Matches(ps5, "", "PS5", "Maintenance"));
        Assert.True(StationBoardFilter.Matches(ps5, "", "All", "Maintenance"));
        Assert.True(StationBoardFilter.Matches(ps5, "", "All", "All"));
    }

    [Fact]
    public void Search_and_filters_must_both_match()
    {
        var pc = PcStation();

        Assert.True(StationBoardFilter.Matches(pc, "front", "PC", "Available"));
        Assert.False(StationBoardFilter.Matches(pc, "playstation", "PC", "Available"));
        Assert.False(StationBoardFilter.Matches(pc, "front", "PC", "Maintenance"));
    }

    [Fact]
    public void Empty_search_does_not_treat_non_pc_stations_as_agent_online()
    {
        var ps5 = Ps5Station();
        Assert.True(StationBoardFilter.Matches(ps5, null, "PS5", "Maintenance"));
        Assert.Equal(StationTypeContract.Ps5, ps5.Station.Type);
        Assert.Null(ps5.Station.AgentDeviceId);
        Assert.False(ps5.Station.AgentOnline);
    }
}
