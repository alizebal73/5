namespace GameNet.Server.Modules.Identity.Domain;

public sealed class Role
{
    private Role() { }
    private Role(Guid id, string code, string name)
    {
        Id = id;
        Code = Normalize(code, 64, nameof(code)).ToLowerInvariant();
        if (!char.IsAsciiLetter(Code[0]) || Code.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '.' && ch != '-' && ch != '_'))
            throw new ArgumentException("Role code must start with a letter and contain only letters, digits, dots, hyphens or underscores.", nameof(code));
        Name = Normalize(name, 120, nameof(name));
    }
    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public static Role Create(Guid id, string code, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException("Role id is required.", nameof(id));
        return new Role(id, code, name);
    }
    private static string Normalize(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentException("Value is too long.", parameterName);
        return normalized;
    }
}
