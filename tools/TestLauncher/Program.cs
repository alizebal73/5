using System.Security.Cryptography;

namespace GameNet.TestLauncher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(argument => string.Equals(argument, "--self-test", StringComparison.Ordinal)))
        {
            try
            {
                SettingsStore.RunSelfTest();
                Console.WriteLine("TEST_LAUNCHER_DPAPI_SELF_TEST=PASS");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"TEST_LAUNCHER_DPAPI_SELF_TEST=FAIL: {exception.GetType().Name}");
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}

internal sealed class LauncherSettings
{
    public string PostgreSqlPort { get; set; } = "5432";
    public string PostgreSqlAdminUser { get; set; } = "postgres";
    public string OwnerUsername { get; set; } = "slice1-owner";
    public string OwnerDisplayName { get; set; } = "Slice 1 Test Owner";
    public string OwnerPassword { get; set; } = string.Empty;
    public string HourlyRateToman { get; set; } = "1000";
}

internal static class SettingsStore
{
    private static readonly byte[] Entropy = "GameNet5.TestLauncher.UserSettings.v1"u8.ToArray();

    private static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameNet5", "TestLauncher");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.dpapi");

    public static LauncherSettings? Load()
    {
        if (!File.Exists(SettingsPath))
            return null;

        var protectedBytes = File.ReadAllBytes(SettingsPath);
        var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<LauncherSettings>(clearBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    public static void Save(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(SettingsDirectory);
        var clearBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(settings);
        try
        {
            var protectedBytes = ProtectedData.Protect(clearBytes, Entropy, DataProtectionScope.CurrentUser);
            var temporaryPath = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, protectedBytes);
                File.Move(temporaryPath, SettingsPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
                CryptographicOperations.ZeroMemory(protectedBytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    public static void Clear()
    {
        if (File.Exists(SettingsPath))
            File.Delete(SettingsPath);
    }

    public static void RunSelfTest()
    {
        var testValue = new LauncherSettings
        {
            PostgreSqlPort = "5432",
            PostgreSqlAdminUser = "self-test",
            OwnerUsername = "self-test-owner",
            OwnerDisplayName = "Self Test",
            OwnerPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
            HourlyRateToman = "1000"
        };
        var clear = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(testValue);
        var protectedBytes = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
        var roundTrip = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var restored = System.Text.Json.JsonSerializer.Deserialize<LauncherSettings>(roundTrip);
            if (restored?.OwnerUsername != testValue.OwnerUsername ||
                restored.OwnerPassword != testValue.OwnerPassword)
                throw new CryptographicException("DPAPI settings round-trip did not match.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(protectedBytes);
            CryptographicOperations.ZeroMemory(roundTrip);
        }
    }
}
