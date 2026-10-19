using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Runtime;
using GameNet.CustomerClient.Features;

namespace GameNet.CustomerClient;

public sealed class CustomerEntryPipeClient : ICustomerEntryGateway
{
    private const int MaximumFrameBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CustomerClientEntryResponse> StartAsync(
        CustomerClientEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                CustomerEntryPipeProtocol.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(7000, cancellationToken);
            await WriteFrameAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions), cancellationToken);
            var frame = await ReadFrameAsync(pipe, cancellationToken);
            var response = JsonSerializer.Deserialize<CustomerClientEntryResponse>(frame, JsonOptions);
            return response ?? Rejected("client.invalid_response", "پاسخ معتبری از سرویس رایانه دریافت نشد.");
        }
        catch (TimeoutException)
        {
            return Rejected("client.agent_unavailable", "سرویس GameNet روی این رایانه در دسترس نیست.");
        }
        catch (IOException)
        {
            return Rejected("client.result_unknown", "نتیجهٔ ورود نامشخص است؛ با همان درخواست دوباره تلاش کنید.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Rejected("client.result_unknown", "نتیجهٔ ورود نامشخص است؛ با همان درخواست دوباره تلاش کنید.");
        }
    }

    private static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > MaximumFrameBytes)
            throw new InvalidDataException("Customer-entry response length is invalid.");

        var frame = new byte[length];
        await stream.ReadExactlyAsync(frame, cancellationToken);
        return frame;
    }

    private static async Task WriteFrameAsync(Stream stream, byte[] frame, CancellationToken cancellationToken)
    {
        if (frame.Length is < 1 or > MaximumFrameBytes)
            throw new InvalidDataException("Customer-entry request length is invalid.");

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, frame.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(frame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static CustomerClientEntryResponse Rejected(string code, string message) =>
        new(false, null, null, null, null, null, code, message);
}
