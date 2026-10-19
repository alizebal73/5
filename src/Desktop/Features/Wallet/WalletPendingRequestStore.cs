using System.IO;
using System.Text;
using System.Text.Json;

namespace GameNet.Desktop.Features.Wallet;

public sealed record PendingWalletTopUp(
    Guid OperatorId,
    Guid CustomerId,
    string CustomerCode,
    int CustomerVersion,
    long AmountToCreditToman,
    long AmountReceivedToman,
    string IdempotencyKey);

public interface IWalletPendingRequestStore
{
    PendingWalletTopUp? Load();
    void Save(PendingWalletTopUp request);
    void Clear();
}

/// <summary>
/// Persists only the unresolved Wallet command identity and its exact payload, never credentials.
/// The file is scoped to the current Windows user. It is written before sending cash commands
/// so a Desktop restart cannot silently discard the idempotency key for an ambiguous result.
/// </summary>
public sealed class FileWalletPendingRequestStore : IWalletPendingRequestStore
{
    private readonly string rootDirectory;
    private readonly string filePath;

    public FileWalletPendingRequestStore(string? rootDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                throw new InvalidOperationException("LOCAL_APPLICATION_DATA_NOT_AVAILABLE");
            rootDirectory = Path.Combine(localAppData, "GameNet Manager", "WalletPendingRequests");
        }

        this.rootDirectory = Path.GetFullPath(rootDirectory);
        filePath = Path.Combine(this.rootDirectory, "pending-wallet-top-up.json");
    }

    public PendingWalletTopUp? Load()
    {
        if (!File.Exists(filePath)) return null;

        PendingWalletTopUp? request;
        try
        {
            request = JsonSerializer.Deserialize<PendingWalletTopUp>(File.ReadAllText(filePath, Encoding.UTF8));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("WALLET_PENDING_REQUEST_JSON_INVALID", ex);
        }

        if (request is null)
            throw new InvalidDataException("WALLET_PENDING_REQUEST_EMPTY");
        Validate(request);
        return request;
    }

    public void Save(PendingWalletTopUp request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        Directory.CreateDirectory(rootDirectory);
        var temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(request), new UTF8Encoding(false));
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public void Clear()
    {
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    private static void Validate(PendingWalletTopUp request)
    {
        if (request.OperatorId == Guid.Empty || request.CustomerId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.CustomerCode) || request.CustomerCode.Length > 40 ||
            request.CustomerVersion < 1 || request.AmountToCreditToman <= 0 ||
            request.AmountReceivedToman < request.AmountToCreditToman ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200)
            throw new InvalidDataException("WALLET_PENDING_REQUEST_INVALID");
    }
}
