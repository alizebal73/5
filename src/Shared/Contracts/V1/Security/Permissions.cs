namespace GameNet.Shared.Contracts.V1.Security;

public static class Permissions
{
    public const string StationsRead = "stations.read";
    public const string StationsOperate = "stations.operate";
    public const string CustomersRead = "customers.read";
    public const string CustomersWrite = "customers.write";
    public const string SessionsOperate = "sessions.operate";
    public const string BillingWrite = "billing.write";
    public const string WalletWrite = "wallet.write";
    public const string InventoryWrite = "inventory.write";
    public const string ReportsRead = "reports.read";
    public const string SettingsWrite = "settings.write";
    public const string BackupOperate = "backup.operate";
}
