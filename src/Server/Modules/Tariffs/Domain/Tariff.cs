using GameNet.Shared.Contracts.V1.Stations;

namespace GameNet.Server.Modules.Tariffs.Domain;

public sealed class Tariff
{
    private Tariff() { }

    private Tariff(
        Guid id,
        string code,
        string name,
        StationTypeContract stationType,
        long hourlyRateToman,
        bool isDefault,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Code = NormalizeCode(code);
        Name = NormalizeName(name);
        StationType = stationType;
        HourlyRateToman = hourlyRateToman;
        IsDefault = isDefault;
        IsActive = true;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        Version = 1;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public StationTypeContract StationType { get; private set; }
    public long HourlyRateToman { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public int Version { get; private set; }

    public static Tariff CreateFixedHourly(
        Guid id,
        string code,
        string name,
        StationTypeContract stationType,
        long hourlyRateToman,
        bool isDefault,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Tariff id is required.", nameof(id));
        if (!Enum.IsDefined(stationType)) throw new ArgumentOutOfRangeException(nameof(stationType));
        if (hourlyRateToman <= 0) throw new ArgumentOutOfRangeException(nameof(hourlyRateToman), "Hourly rate must be positive.");
        return new Tariff(id, code, name, stationType, hourlyRateToman, isDefault, createdAtUtc);
    }

    public void ChangeHourlyRate(long hourlyRateToman)
    {
        if (hourlyRateToman <= 0) throw new ArgumentOutOfRangeException(nameof(hourlyRateToman), "Hourly rate must be positive.");
        if (HourlyRateToman == hourlyRateToman) return;
        HourlyRateToman = hourlyRateToman;
        Version = checked(Version + 1);
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        IsDefault = false;
        Version = checked(Version + 1);
    }

    public void SetDefault(bool isDefault)
    {
        if (!IsActive && isDefault)
            throw new InvalidOperationException("INACTIVE_TARIFF_CANNOT_BE_DEFAULT");
        if (IsDefault == isDefault) return;
        IsDefault = isDefault;
        Version = checked(Version + 1);
    }

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Tariff code is required.", nameof(code));
        var value = code.Trim().ToUpperInvariant();
        if (value.Length is < 2 or > 32 ||
            value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) ||
            !char.IsAsciiLetterOrDigit(value[0]) || !char.IsAsciiLetterOrDigit(value[^1]))
            throw new ArgumentException("Tariff code must be 2-32 ASCII letters, digits, dashes or underscores.", nameof(code));
        return value;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Tariff name is required.", nameof(name));
        var value = name.Trim();
        if (value.Length > 120 || value.Any(char.IsControl))
            throw new ArgumentException("Tariff name must be at most 120 characters.", nameof(name));
        return value;
    }
}
