using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using Npgsql;

namespace GameNet.Server.Infrastructure.Security;

internal static class ServerSecretProvisioning
{
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
            DpapiServerSecretStore.Provision(connection, signingKey, provisioningKey);
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
