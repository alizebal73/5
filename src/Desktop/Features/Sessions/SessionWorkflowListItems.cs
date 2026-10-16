using System.Globalization;
using GameNet.Shared.Contracts.V1.Billing;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Stations;

namespace GameNet.Desktop.Features.Sessions;

/// <summary>
/// Presentation-only station choice. The Server response remains authoritative; this type label
/// only helps operators distinguish station types in the selector.
/// </summary>
public sealed record SessionStationOption(StationResponse Station, string TypeDisplayText)
{
    public Guid Id => Station.Id;
    public string Code => Station.Code;
}

/// <summary>
/// Localized option shown when configuring a tariff. The enum, not the label, is sent to Server.
/// </summary>
public sealed record SessionStationTypeOption(StationTypeContract Type, string DisplayText);

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
    public string StatusDisplayText =>
        Session.Status == SessionStatusContract.Paused
            ? (CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? "متوقف" : "Paused")
            : (CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? "در حال اجرا" : "Running");
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


/// <summary>
/// Read-only Server-derived proof of a recent completed checkout. This is intentionally
/// independent from ViewModel memory so the operator can reconcile after Desktop restart.
/// </summary>
public sealed record CashCheckoutOutcomeListItem(
    CashCheckoutOutcomeResponse Outcome,
    string StationLabel,
    string InvoiceIdentityText,
    string AmountDisplayText,
    string CashDetailsDisplayText,
    string PaidAtDisplayText)
{
    public Guid Id => Outcome.InvoiceId;
}
