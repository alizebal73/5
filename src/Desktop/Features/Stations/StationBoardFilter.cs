using GameNet.Shared.Contracts.V1.Stations;

namespace GameNet.Desktop.Features.Stations;

/// <summary>
/// Pure presentation filtering for the Station Board. It never changes server state or station authority.
/// </summary>
public static class StationBoardFilter
{
    public static bool Matches(StationBoardItem station, string? searchText, string? typeFilter, string? statusFilter)
    {
        ArgumentNullException.ThrowIfNull(station);

        if (!IsAll(typeFilter) && !string.Equals(station.TypeText, typeFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!IsAll(statusFilter) && !string.Equals(station.StatusFilterKey, statusFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        var query = searchText?.Trim();
        if (string.IsNullOrEmpty(query)) return true;

        return Contains(station.Code, query)
            || Contains(station.Name, query)
            || Contains(station.TypeText, query)
            || Contains(station.StatusText, query)
            || Contains(station.RuntimeText, query)
            || Contains(station.AgentText, query)
            || Contains(station.AgentVersionText, query)
            || Contains(station.AgentStateText, query);
    }

    private static bool IsAll(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, "All", StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);
}
