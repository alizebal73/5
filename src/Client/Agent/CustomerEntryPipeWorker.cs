using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using GameNet.Agent.Transport;
using GameNet.Shared.Contracts.V1.Customers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameNet.Agent;

/// <summary>
/// Local UI-to-service bridge. The pipe is ACL-restricted to authenticated local users,
/// and the Agent forwards credentials only over the configured Server HTTPS endpoint.
/// </summary>
public sealed class CustomerEntryPipeWorker(
    IAgentCustomerSessionGateway gateway,
    ILogger<CustomerEntryPipeWorker> logger) : BackgroundService
{
    public const string PipeName = "GameNet5.Agent.CustomerEntry.v1";
    private const int MaximumFrameBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(stoppingToken);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(20));

                var frame = await ReadFrameAsync(pipe, requestTimeout.Token);
                var request = JsonSerializer.Deserialize<CustomerClientEntryRequest>(frame, JsonOptions);
                CustomerClientEntryResponse result;
                if (request is null)
                    result = Rejected("client.request_invalid", "The request was empty.");
                else
                {
                    try
                    {
                        result = await gateway.StartAsync(request, requestTimeout.Token);
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        result = Rejected("client.result_unknown",
                            "The Server response timed out. Retry the same entry request to reconcile its result.");
                    }
                    catch (Exception)
                    {
                        result = Rejected("client.server_unavailable",
                            "Could not verify the Session with the Server. Check connection and retry.");
                    }
                }

                await WriteFrameAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(result, JsonOptions), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "Customer-entry pipe request failed; the pipe will be recreated.");
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogError(exception, "Could not create the ACL-restricted customer-entry pipe.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Customer-entry bridge failed; it will retry.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        var serviceSid = WindowsIdentity.GetCurrent().User;
        if (serviceSid is not null)
            security.AddAccessRule(new PipeAccessRule(serviceSid, PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 4096,
            outBufferSize: 4096,
            security);
    }

    private static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > MaximumFrameBytes)
            throw new InvalidDataException("Customer-entry message length is invalid.");

        var frame = new byte[length];
        await stream.ReadExactlyAsync(frame, cancellationToken);
        return frame;
    }

    private static async Task WriteFrameAsync(Stream stream, byte[] frame, CancellationToken cancellationToken)
    {
        if (frame.Length is < 1 or > MaximumFrameBytes)
            throw new InvalidDataException("Customer-entry response length is invalid.");

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, frame.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(frame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static CustomerClientEntryResponse Rejected(string code, string message) =>
        new(false, null, null, null, null, null, null, null, code, message);
}
