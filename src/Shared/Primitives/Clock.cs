using System.Numerics;
using System.Runtime.InteropServices;

namespace GameNet.Shared.Primitives;

public sealed record GameClockSnapshot(
    DateTimeOffset UtcNow,
    long Timestamp,
    long TimestampFrequency,
    long SystemUptimeMilliseconds,
    Guid BootIdentifier);

public interface IGameClock
{
    DateTimeOffset UtcNow { get; }
    GameClockSnapshot Capture();
}

public sealed class SystemGameClock(TimeProvider provider) : IGameClock
{
    public DateTimeOffset UtcNow => provider.GetUtcNow();

    public GameClockSnapshot Capture() => new(
        provider.GetUtcNow(),
        provider.GetTimestamp(),
        provider.TimestampFrequency,
        Environment.TickCount64,
        WindowsBootIdentifier.Read());
}

internal static partial class WindowsBootIdentifier
{
    private const int SystemBootEnvironmentInformation = 90;

    [StructLayout(LayoutKind.Sequential, Size = 32)]
    private struct BootEnvironmentInformation
    {
        public Guid BootIdentifier;
    }

    #pragma warning disable SYSLIB1054 // Keep this narrow native call free of generated unsafe-code requirements.
    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        uint systemInformationLength,
        out uint returnLength);
#pragma warning restore SYSLIB1054

    public static Guid Read()
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("SESSION_BOOT_ID_UNAVAILABLE");

        var size = Marshal.SizeOf<BootEnvironmentInformation>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var status = NtQuerySystemInformation(
                SystemBootEnvironmentInformation, buffer, (uint)size, out _);
            if (status < 0)
                throw new InvalidOperationException("SESSION_BOOT_ID_UNAVAILABLE");

            var info = Marshal.PtrToStructure<BootEnvironmentInformation>(buffer);
            if (info.BootIdentifier == Guid.Empty)
                throw new InvalidOperationException("SESSION_BOOT_ID_UNAVAILABLE");
            return info.BootIdentifier;
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException("SESSION_BOOT_ID_UNAVAILABLE", ex);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

/// <summary>
/// Calculates elapsed duration from a monotonic clock and verifies that the OS boot did
/// not change. UTC is not an input. If continuity cannot be proven, callers must fail closed.
/// </summary>
public static class MonotonicElapsedTime
{
    private static readonly BigInteger ContinuityToleranceTicks =
        TimeSpan.FromSeconds(2).Ticks;

    public static bool TryCalculateElapsedTicks(
        long startedTimestamp,
        long startedTimestampFrequency,
        long startedSystemUptimeMilliseconds,
        Guid startedBootIdentifier,
        GameClockSnapshot ending,
        out long elapsedTicks)
    {
        elapsedTicks = 0;
        if (startedTimestamp < 0 ||
            ending.Timestamp < 0 ||
            startedTimestampFrequency <= 0 ||
            startedTimestampFrequency != ending.TimestampFrequency ||
            ending.Timestamp <= startedTimestamp ||
            startedSystemUptimeMilliseconds < 0 ||
            ending.SystemUptimeMilliseconds < startedSystemUptimeMilliseconds ||
            startedBootIdentifier == Guid.Empty ||
            ending.BootIdentifier == Guid.Empty ||
            startedBootIdentifier != ending.BootIdentifier)
            return false;

        var timestampDelta = new BigInteger(ending.Timestamp) - startedTimestamp;
        var calculatedTicks = timestampDelta * TimeSpan.TicksPerSecond / startedTimestampFrequency;
        if (calculatedTicks <= BigInteger.Zero || calculatedTicks > long.MaxValue)
            return false;

        var uptimeDeltaMilliseconds = ending.SystemUptimeMilliseconds - startedSystemUptimeMilliseconds;
        var uptimeTicks = new BigInteger(uptimeDeltaMilliseconds) * TimeSpan.TicksPerMillisecond;
        var tolerance = BigInteger.Max(ContinuityToleranceTicks, calculatedTicks / 1000);
        if (BigInteger.Abs(calculatedTicks - uptimeTicks) > tolerance)
            return false;

        elapsedTicks = (long)calculatedTicks;
        return true;
    }
}
