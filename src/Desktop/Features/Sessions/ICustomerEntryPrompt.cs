using GameNet.Shared.Contracts.V1.Stations;

namespace GameNet.Desktop.Features.Sessions;

/// <summary>Presentation-only prompt for the operator's Customer ID input.</summary>
public interface ICustomerEntryPrompt
{
    string? PromptForCustomerCode(StationResponse station);
}
