using System.Windows;
using GameNet.Shared.Contracts.V1.Stations;

namespace GameNet.Desktop.Features.Sessions;

/// <summary>WPF adapter for the Customer ID prompt; it contains no Session or Wallet rules.</summary>
public sealed class CustomerEntryPrompt : ICustomerEntryPrompt
{
    public string? PromptForCustomerCode(StationResponse station)
    {
        ArgumentNullException.ThrowIfNull(station);
        var dialog = new CustomerEntryDialog(station.Code);
        if (Application.Current?.MainWindow is Window owner)
            dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog.CustomerCode : null;
    }
}
