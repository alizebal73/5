using System.Collections.ObjectModel;
using System.Net.Http;
using GameNet.Desktop.Api;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Contracts.V1.Tariffs;

namespace GameNet.Desktop.Features.Sessions;

/// <summary>
/// Station-type-specific selection and refresh logic for the Session-to-cash capability.
/// Keeping this in a separate partial preserves the repository's source-size constraint and
/// lets the WPF view be rearranged without mixing station selection with financial commands.
/// </summary>
public sealed partial class SessionWorkflowViewModel
{
    private SessionStationOption? selectedStationChoice;
    private IReadOnlyList<TariffResponse> allActiveTariffs = [];
    private StationTypeContract tariffStationType = StationTypeContract.Pc;

    public ObservableCollection<SessionStationOption> StationChoices { get; } = [];
    public ObservableCollection<SessionStationTypeOption> StationTypeOptions { get; } = [];

    public SessionStationOption? SelectedStationChoice
    {
        get => selectedStationChoice;
        set
        {
            if (SetField(ref selectedStationChoice, value))
                SelectedStation = value?.Station;
        }
    }

    public StationTypeContract TariffStationType
    {
        get => tariffStationType;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            var tariffStationTypeBeforeChange = tariffStationType;
            if (!SetField(ref tariffStationType, value))
                return;

            // Keep the default form values consistent with the selected type, but preserve
            // any custom code/name already entered by the operator.
            if (string.Equals(TariffCode, DefaultTariffCode(tariffStationTypeBeforeChange), StringComparison.Ordinal))
                TariffCode = DefaultTariffCode(value);
            if (string.Equals(TariffName, DefaultTariffName(tariffStationTypeBeforeChange), StringComparison.Ordinal))
                TariffName = DefaultTariffName(value);

            RaiseCommands();
        }
    }

    private static string DefaultTariffCode(StationTypeContract type) => type switch
    {
        StationTypeContract.Pc => "PC-STANDARD",
        StationTypeContract.Ps5 => "PS5-STANDARD",
        StationTypeContract.Foosball => "FOOSBALL-STANDARD",
        _ => "STATION-STANDARD"
    };

    private static string DefaultTariffName(StationTypeContract type) => type switch
    {
        StationTypeContract.Pc => "PC Standard",
        StationTypeContract.Ps5 => "PS5 Standard",
        StationTypeContract.Foosball => "Foosball Standard",
        _ => "Standard station"
    };

    private void InitializeStationTypeOptions()
    {
        foreach (var stationType in new[]
        {
            StationTypeContract.Pc,
            StationTypeContract.Ps5,
            StationTypeContract.Foosball
        })
            StationTypeOptions.Add(new SessionStationTypeOption(stationType, StationTypeDisplayText(stationType)));
    }

    private void OnSelectedStationChanged()
    {
        var matchingChoice = StationChoices.FirstOrDefault(x => x.Station.Id == selectedStation?.Id);
        if (selectedStationChoice?.Station.Id != matchingChoice?.Station.Id)
        {
            selectedStationChoice = matchingChoice;
            OnPropertyChanged(nameof(SelectedStationChoice));
        }

        ApplyTariffFilter(selectedTariff?.Id);
        RaiseCommands();
    }

    private async Task RefreshStationAndTariffChoicesAsync()
    {
        if (CanReadStations)
        {
            StationResponse[] stations;
            try
            {
                stations = (await api.GetStationsAsync())
                    .OrderBy(x => x.Type)
                    .ThenBy(x => x.Code, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception ex) when (!CanOperateSessions &&
                (ex is GameNetApiException || ex is HttpRequestException || ex is TaskCanceledException))
            {
                // A station-code lookup enriches invoice rows; it must not block a cashier from loading invoices.
                stations = [];
                stationLabelsUnavailable = true;
            }

            stationLabels.Clear();
            foreach (var station in stations)
                stationLabels[station.Id] = $"{station.Code} — {StationTypeDisplayText(station.Type)}";

            if (CanOperateSessions)
            {
                var choices = stations
                    .Select(station => new SessionStationOption(station, StationTypeDisplayText(station.Type)))
                    .ToArray();
                Replace(StationChoices, choices, SelectedStation?.Id, x => x.Station.Id, out var currentChoice);
                SelectedStationChoice = currentChoice;
                SelectedStation = currentChoice?.Station;
            }
            else
            {
                // Station labels may be visible to a cashier without granting Session operations.
                StationChoices.Clear();
                SelectedStationChoice = null;
                SelectedStation = null;
            }
        }
        else
        {
            stationLabels.Clear();
            StationChoices.Clear();
            SelectedStationChoice = null;
            SelectedStation = null;
        }

        if (CanOperateSessions || CanManageTariffs)
        {
            allActiveTariffs = (await api.GetTariffsAsync()).ToArray();
            ApplyTariffFilter(SelectedTariff?.Id);
        }
        else
        {
            allActiveTariffs = [];
            Tariffs.Clear();
            SelectedTariff = null;
        }
    }

    private void ResetStationAndTariffChoices()
    {
        allActiveTariffs = [];
        StationChoices.Clear();
        SelectedStationChoice = null;
    }

    private static string StationTypeDisplayText(StationTypeContract type) => type switch
    {
        StationTypeContract.Pc => Text("رایانه (PC)", "PC workstation"),
        StationTypeContract.Ps5 => Text("کنسول PS5", "PS5 console"),
        StationTypeContract.Foosball => Text("فوتبال‌دستی", "Foosball table"),
        _ => Text("نوع نامشخص", "Unknown station type")
    };

    private void ApplyTariffFilter(Guid? preferredTariffId = null)
    {
        var compatibleTariffs = allActiveTariffs
            .Where(tariff => SelectedStation is null || tariff.StationType == SelectedStation.Type)
            .ToArray();

        var requestedId = preferredTariffId ?? selectedTariff?.Id;
        Replace(Tariffs, compatibleTariffs, requestedId, tariff => tariff.Id, out var currentTariff);
        SelectedTariff = currentTariff
            ?? Tariffs.FirstOrDefault(tariff => tariff.IsDefault)
            ?? Tariffs.FirstOrDefault();
    }
}
