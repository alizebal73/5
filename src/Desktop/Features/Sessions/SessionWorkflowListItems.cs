using GameNet.Shared.Contracts.V1.Sessions;

namespace GameNet.Desktop.Features.Sessions;

/// <summary>
/// Presentation-only rows for operator lists. These keep the server DTO as the source of
/// truth while adding the station identifier and context needed to choose the correct item.
/// </summary>
public sealed record ActiveSessionListItem(
    SessionResponse Session,
    string StationLabel,
    string StartedAtDisplayText)
{
    public Guid Id => Session.Id;
    public string TariffNameSnapshot => Session.TariffNameSnapshot;
}

/// <summary>
/// Presentation-only invoice row. Amount and elapsed duration are server-produced values;
/// this record only formats them for display and associates the station label.
/// </summary>
public sealed record PendingInvoiceListItem(
    InvoicePreviewResponse Invoice,
    string StationLabel,
    string DurationDisplayText,
    string AmountDisplayText)
{
    public Guid Id => Invoice.InvoiceId;
    public string TariffNameSnapshot => Invoice.TariffNameSnapshot;
}
