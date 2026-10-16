using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using Npgsql;

namespace GameNet.Server.Infrastructure.Security;

internal static class ServerSecretProvisioning
{
    internal static bool TryHandleSpecialCommand(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var migrateFingerprintKey = arguments.Any(argument =>
            string.Equals(argument, "--migrate-idempotency-fingerprint-key", StringComparison.Ordinal));
        var provisionSecrets = arguments.Any(argument =>
            string.Equals(argument, "--provision-secrets", StringComparison.Ordinal));
        var provisionDisposableTestSecrets = arguments.Any(argument =>
            string.Equals(argument, "--provision-test-secrets-stdin", StringComparison.Ordinal));

        if (!migrateFingerprintKey && !provisionSecrets && !provisionDisposableTestSecrets)
            return false;
        if (arguments.Length != 1)
            throw new InvalidOperationException("A Server secret maintenance command must be the only command-line argument.");
        if (!OperatingSystem.IsWindows())
            throw new ServerSecretStoreException("Server secret maintenance commands require Windows.");

        if (migrateFingerprintKey)
            RunMigrateLegacyFingerprintKey();
        else if (provisionDisposableTestSecrets)
            RunDisposableTestProvisioningFromStandardInput();
        else
            RunInteractive();
        return true;
    }

    internal static void RunInteractive()
    {
        if (!OperatingSystem.IsWindows())
            throw new ServerSecretStoreException("Server secret-store provisioning requires Windows.");

        var operationId = Guid.NewGuid().ToString("N");
        ServerSecretProvisioningAuditLog.RecordStarted(operationId);
        var storeCreated = false;
        var completionAudited = false;

        try
        {
            Console.WriteLine("GameNet Server protected-secret provisioning");
            Console.WriteLine("Run only after the Windows service 'GameNet 5 Server' has been registered.");
            Console.WriteLine("The PostgreSQL connection string is entered without console echo.");

            var connection = ReadHiddenValue("PostgreSQL connection string");
            if (string.IsNullOrWhiteSpace(connection))
                throw new ServerSecretStoreException("The PostgreSQL connection string is required.");

            try
            {
                var parsed = new NpgsqlConnectionStringBuilder(connection);
                if (string.IsNullOrWhiteSpace(parsed.Host) ||
                    string.IsNullOrWhiteSpace(parsed.Database) ||
                    string.IsNullOrWhiteSpace(parsed.Username))
                    throw new ServerSecretStoreException("The PostgreSQL connection string is invalid.");
            }
            catch (ServerSecretStoreException)
            {
                throw;
            }
            catch
            {
                throw new ServerSecretStoreException("The PostgreSQL connection string is invalid.");
            }

            var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            var provisioningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            var fingerprintKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            DpapiServerSecretStore.Provision(connection, signingKey, provisioningKey, fingerprintKey);
            storeCreated = true;

            ServerSecretProvisioningAuditLog.RecordSucceeded(operationId);
            completionAudited = true;
        }
        catch (ServerSecretStoreException)
        {
            if (storeCreated && !completionAudited)
                throw new ServerSecretStoreException(
                    "The protected store was created, but its completion audit record could not be written. Do not rerun provisioning; inspect the restricted audit directory.");

            if (!ServerSecretProvisioningAuditLog.TryRecordFailed(operationId, ServerSecretProvisioningFailureCode.ProvisioningRejected))
                throw new ServerSecretStoreException(
                    "Provisioning did not complete and its failure audit record could not be written. Inspect the restricted audit directory before retrying.");
            throw;
        }
        catch
        {
            if (storeCreated && !completionAudited)
                throw new ServerSecretStoreException(
                    "The protected store was created, but its completion audit record could not be written. Do not rerun provisioning; inspect the restricted audit directory.");

            if (!ServerSecretProvisioningAuditLog.TryRecordFailed(operationId, ServerSecretProvisioningFailureCode.ProvisioningFailed))
                throw new ServerSecretStoreException(
                    "Provisioning failed and its failure audit record could not be written. Inspect the restricted audit directory before retrying.");

            throw new ServerSecretStoreException(
                "Server secret provisioning failed. Review the restricted audit records; no secret values were recorded.");
        }

        Console.WriteLine("Protected Server secret store created.");
        Console.WriteLine("Secret values were not printed; retain the store on the manager Server only.");
        Console.WriteLine("Provisioning audit operation: " + operationId);
    }

    internal static NpgsqlConnectionStringBuilder ValidateDisposableTestConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Length > 4096)
            throw new ServerSecretStoreException("The disposable test database connection string is missing or too long.");

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch
        {
            throw new ServerSecretStoreException("The disposable test database connection string is invalid.");
        }

        var host = builder.Host.Trim();
        if (!host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
            !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            throw new ServerSecretStoreException("Test secret provisioning accepts loopback PostgreSQL only.");

        if (!System.Text.RegularExpressions.Regex.IsMatch(
                builder.Database,
                @"^gamenet_test_[A-Za-z0-9_]{1,48}$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new ServerSecretStoreException("The test database name must begin with gamenet_test_.");

        if (!System.Text.RegularExpressions.Regex.IsMatch(
                builder.Username,
                @"^gamenet_test_app_[A-Za-z0-9_]{1,40}$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new ServerSecretStoreException("The test database login must be a dedicated gamenet_test_app_ role.");

        if (string.IsNullOrWhiteSpace(builder.Password))
            throw new ServerSecretStoreException("The disposable test database login must have a password.");

        if (builder.Pooling)
            throw new ServerSecretStoreException("Disposable test secret provisioning requires connection pooling to be disabled.");

        return builder;
    }

    private static void RunDisposableTestProvisioningFromStandardInput()
    {
        if (!Console.IsInputRedirected)
            throw new ServerSecretStoreException("Disposable test secret provisioning accepts its connection string only through redirected standard input.");

        var connection = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(connection))
            throw new ServerSecretStoreException("No disposable test database connection string was supplied through standard input.");

        var operationId = Guid.NewGuid().ToString("N");
        ServerSecretProvisioningAuditLog.RecordStarted(operationId);
        var storeCreated = false;
        var completionAudited = false;

        try
        {
            var builder = ValidateDisposableTestConnectionString(connection);
            using (var db = new NpgsqlConnection(builder.ConnectionString))
            {
                db.Open();
                using var command = db.CreateCommand();
                command.CommandText = """
                    SELECT current_database(), current_user,
                           role.rolsuper, role.rolcreatedb, role.rolcreaterole, role.rolreplication,
                           (SELECT COUNT(*)
                            FROM pg_catalog.pg_class AS relation
                            JOIN pg_catalog.pg_namespace AS schema ON schema.oid = relation.relnamespace
                            WHERE schema.nspname !~ '^pg_'
                              AND schema.nspname <> 'information_schema'
                              AND relation.relkind IN ('r', 'p', 'v', 'm', 'S', 'f'))
                    FROM pg_catalog.pg_roles AS role
                    WHERE role.rolname = current_user;
                    """;
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                    throw new ServerSecretStoreException("The test database login could not be verified.");

                var databaseName = reader.GetString(0);
                var loginName = reader.GetString(1);
                var isSuperUser = reader.GetBoolean(2);
                var canCreateDatabase = reader.GetBoolean(3);
                var canCreateRoles = reader.GetBoolean(4);
                var canReplicate = reader.GetBoolean(5);
                var applicationObjectCount = reader.GetInt64(6);

                if (!databaseName.Equals(builder.Database, StringComparison.Ordinal) ||
                    !loginName.Equals(builder.Username, StringComparison.Ordinal) ||
                    isSuperUser || canCreateDatabase || canCreateRoles || canReplicate)
                    throw new ServerSecretStoreException("The disposable test database must use its dedicated non-superuser application role.");

                if (applicationObjectCount != 0)
                    throw new ServerSecretStoreException("Refusing test secret provisioning because the named test database is not empty.");
            }

            var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            var provisioningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            var fingerprintKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            DpapiServerSecretStore.Provision(builder.ConnectionString, signingKey, provisioningKey, fingerprintKey);
            storeCreated = true;

            ServerSecretProvisioningAuditLog.RecordSucceeded(operationId);
            completionAudited = true;
        }
        catch (ServerSecretStoreException)
        {
            if (storeCreated && !completionAudited)
                throw new ServerSecretStoreException(
                    "The protected store was created, but its completion audit record could not be written. Do not rerun provisioning; inspect the restricted audit directory.");

            if (!ServerSecretProvisioningAuditLog.TryRecordFailed(operationId, ServerSecretProvisioningFailureCode.ProvisioningRejected))
                throw new ServerSecretStoreException(
                    "Test secret provisioning did not complete and its failure audit record could not be written. Inspect the restricted audit directory before retrying.");
            throw;
        }
        catch
        {
            if (storeCreated && !completionAudited)
                throw new ServerSecretStoreException(
                    "The protected store was created, but its completion audit record could not be written. Do not rerun provisioning; inspect the restricted audit directory.");

            if (!ServerSecretProvisioningAuditLog.TryRecordFailed(operationId, ServerSecretProvisioningFailureCode.ProvisioningFailed))
                throw new ServerSecretStoreException(
                    "Test secret provisioning failed and its failure audit record could not be written. Inspect the restricted audit directory before retrying.");

            throw new ServerSecretStoreException(
                "Disposable test secret provisioning failed. Review the restricted audit records; no secret values were recorded.");
        }
        finally
        {
            // Keep the connection string out of diagnostic output. The caller's process owns
            // the short-lived input buffer and must never persist it in configuration or logs.
            connection = null;
        }

        Console.WriteLine("Disposable local PostgreSQL test database validated; protected Server secrets were created.");
        Console.WriteLine("Only the dedicated test database is eligible for this provisioning path.");
        Console.WriteLine("Secret values and the connection string were not printed.");
        Console.WriteLine("Provisioning audit operation: " + operationId);
    }

    internal static void RunMigrateLegacyFingerprintKey()
    {
        if (!OperatingSystem.IsWindows())
            throw new ServerSecretStoreException("Fingerprint-key migration requires Windows.");

        var operationId = Guid.NewGuid().ToString("N");
        ServerSecretProvisioningAuditLog.RecordMigrationStarted(operationId);
        string? backupPath = null;
        var completionAudited = false;

        try
        {
            backupPath = DpapiServerSecretStore.MigrateLegacyFingerprintKey();
            ServerSecretProvisioningAuditLog.RecordMigrationSucceeded(operationId);
            completionAudited = true;
        }
        catch (ServerSecretStoreException)
        {
            if (backupPath is not null && !completionAudited)
                throw new ServerSecretStoreException(
                    "The protected secret-store migration completed, but its success audit record could not be written. Do not rerun it; inspect the restricted audit directory and backup.");

            if (!ServerSecretProvisioningAuditLog.TryRecordMigrationFailed(
                    operationId,
                    ServerSecretProvisioningFailureCode.MigrationRejected))
                throw new ServerSecretStoreException(
                    "Fingerprint-key migration did not complete and its failure audit record could not be written. Inspect the restricted audit directory before retrying.");
            throw;
        }
        catch
        {
            if (backupPath is not null && !completionAudited)
                throw new ServerSecretStoreException(
                    "The protected secret-store migration completed, but its success audit record could not be written. Do not rerun it; inspect the restricted audit directory and backup.");

            if (!ServerSecretProvisioningAuditLog.TryRecordMigrationFailed(
                    operationId,
                    ServerSecretProvisioningFailureCode.MigrationFailed))
                throw new ServerSecretStoreException(
                    "Fingerprint-key migration failed and its failure audit record could not be written. Inspect the restricted audit directory before retrying.");
            throw new ServerSecretStoreException(
                "Fingerprint-key migration failed. Review the restricted audit records; no secret values were recorded.");
        }

        Console.WriteLine(backupPath is null
            ? "The protected Server secret store already has a dedicated fingerprint key; no change was made."
            : "Legacy fingerprint compatibility was preserved and the dedicated fingerprint-key field was added.");
        if (backupPath is not null)
            Console.WriteLine("An encrypted, ACL-restricted pre-migration backup was retained at: " + backupPath);
        Console.WriteLine("Migration audit operation: " + operationId);
    }

    private static string ReadHiddenValue(string prompt)
    {
        Console.Write(prompt + ": ");
        using var secure = new SecureString();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (secure.Length > 0)
                {
                    secure.RemoveAt(secure.Length - 1);
                    Console.Write("\b \b");
                }
                continue;
            }

            if (!char.IsControl(key.KeyChar))
                secure.AppendChar(key.KeyChar);
        }

        if (secure.Length == 0)
            return string.Empty;

        secure.MakeReadOnly();
        var pointer = Marshal.SecureStringToGlobalAllocUnicode(secure);
        try
        {
            return Marshal.PtrToStringUni(pointer) ?? string.Empty;
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(pointer);
        }
    }
}
