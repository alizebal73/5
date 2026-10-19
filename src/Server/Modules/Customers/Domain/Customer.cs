namespace GameNet.Server.Modules.Customers.Domain;

public sealed class Customer
{
    private Customer() { }
    private Customer(Guid id, string code, string? displayName, CustomerKind kind, DateTimeOffset createdAtUtc,
        string? nickname, string? phoneNumber)
    {
        Id=id; Code=NormalizeCode(code); DisplayName=NormalizeDisplayName(displayName,kind);
        Nickname=NormalizeNickname(nickname); PhoneNumber=NormalizePhoneNumber(phoneNumber);
        Kind=kind; IsActive=true; IsBlocked=false; CreatedAtUtc=createdAtUtc.ToUniversalTime(); Version=1;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string? DisplayName { get; private set; }
    public string? Nickname { get; private set; }
    public string? PhoneNumber { get; private set; }
    public CustomerKind Kind { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsBlocked { get; private set; }
    public CustomerStatus Status => IsBlocked ? CustomerStatus.Blocked : IsActive ? CustomerStatus.Active : CustomerStatus.Inactive;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string? PasswordHash { get; private set; }
    public int PasswordFailedAttempts { get; private set; }
    public DateTimeOffset? PasswordLockedUntilUtc { get; private set; }
    public int Version { get; private set; }

    public static Customer CreateWalkIn(Guid id, DateTimeOffset createdAtUtc)
    {
        if(id==Guid.Empty) throw new ArgumentException("Customer id is required.",nameof(id));
        return new Customer(id,$"W-{id:N}".ToUpperInvariant(),null,CustomerKind.WalkIn,createdAtUtc,null,null);
    }

    public static Customer CreateRegistered(Guid id,string code,string displayName,DateTimeOffset createdAtUtc,
        string? nickname=null,string? phoneNumber=null)
    {
        if(id==Guid.Empty) throw new ArgumentException("Customer id is required.",nameof(id));
        return new Customer(id,code,displayName,CustomerKind.Registered,createdAtUtc,nickname,phoneNumber);
    }

    public void Rename(string displayName) => UpdateProfile(displayName,Nickname,PhoneNumber);

    public void UpdateProfile(string displayName,string? nickname,string? phoneNumber)
    {
        if(Kind!=CustomerKind.Registered) throw new InvalidOperationException("CUSTOMER_WALKIN_PROFILE_IS_NOT_EDITABLE");
        var name=NormalizeDisplayName(displayName,Kind);
        var nick=NormalizeNickname(nickname);
        var phone=NormalizePhoneNumber(phoneNumber);
        if(DisplayName==name && Nickname==nick && PhoneNumber==phone) return;
        DisplayName=name; Nickname=nick; PhoneNumber=phone; Version=checked(Version+1);
    }

    public void SetStatus(CustomerStatus status)
    {
        if(Kind!=CustomerKind.Registered) throw new InvalidOperationException("CUSTOMER_WALKIN_STATUS_IS_NOT_EDITABLE");
        if(!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        var (active,blocked)=status switch {
            CustomerStatus.Active=>(true,false),
            CustomerStatus.Inactive=>(false,false),
            CustomerStatus.Blocked=>(true,true),
            _=>throw new ArgumentOutOfRangeException(nameof(status))
        };
        if(IsActive==active && IsBlocked==blocked) return;
        IsActive=active; IsBlocked=blocked; Version=checked(Version+1);
    }

    public void Deactivate()
    {
        if (!IsActive && !IsBlocked) return;
        IsActive = false;
        IsBlocked = false;
        Version = checked(Version + 1);
    }

    public void SetPasswordHash(string encodedHash)
    {
        if (Kind != CustomerKind.Registered)
            throw new InvalidOperationException("CUSTOMER_WALKIN_PASSWORD_IS_NOT_CONFIGURABLE");
        if (string.IsNullOrWhiteSpace(encodedHash) || encodedHash.Length > 256)
            throw new ArgumentException("Encoded customer password hash is invalid.", nameof(encodedHash));
        PasswordHash = encodedHash;
        PasswordFailedAttempts = 0;
        PasswordLockedUntilUtc = null;
        Version = checked(Version + 1);
    }

    public bool IsPasswordLoginLocked(DateTimeOffset nowUtc) =>
        PasswordLockedUntilUtc is DateTimeOffset lockedUntil && lockedUntil > nowUtc.ToUniversalTime();

    public void RecordFailedPasswordAttempt(DateTimeOffset nowUtc)
    {
        nowUtc = nowUtc.ToUniversalTime();
        if (IsPasswordLoginLocked(nowUtc)) return;
        PasswordFailedAttempts = checked(PasswordFailedAttempts + 1);
        if (PasswordFailedAttempts >= 5)
        {
            PasswordFailedAttempts = 0;
            PasswordLockedUntilUtc = nowUtc.AddMinutes(10);
        }
    }

    public void RecordSuccessfulPasswordLogin()
    {
        if (PasswordFailedAttempts == 0 && PasswordLockedUntilUtc is null) return;
        PasswordFailedAttempts = 0;
        PasswordLockedUntilUtc = null;
    }

    private static string NormalizeCode(string code)
    {
        if(string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Customer code is required.",nameof(code));
        var value=code.Trim().ToUpperInvariant();
        if(value.Length is <2 or >40 || value.Any(ch=>!(char.IsAsciiLetterOrDigit(ch)||ch is '-' or '_')) ||
            !char.IsAsciiLetterOrDigit(value[0]) || !char.IsAsciiLetterOrDigit(value[^1]))
            throw new ArgumentException("Customer code must be 2-40 ASCII letters, digits, dashes or underscores.",nameof(code));
        return value;
    }

    private static string? NormalizeDisplayName(string? value,CustomerKind kind)
    {
        if(kind==CustomerKind.WalkIn && string.IsNullOrWhiteSpace(value)) return null;
        if(string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Registered customer name is required.",nameof(value));
        value=value.Trim();
        if(value.Length>120 || value.Any(char.IsControl)) throw new ArgumentException("Customer name must be at most 120 characters without control characters.",nameof(value));
        return value;
    }

    private static string? NormalizeNickname(string? value)
    {
        if(string.IsNullOrWhiteSpace(value)) return null;
        value=value.Trim();
        if(value.Length>60 || value.Any(char.IsControl)) throw new ArgumentException("Customer nickname must be at most 60 characters without control characters.",nameof(value));
        return value;
    }

    private static string? NormalizePhoneNumber(string? phoneNumber)
    {
        if(string.IsNullOrWhiteSpace(phoneNumber)) return null;
        var chars=phoneNumber.Trim().Select(ch=>ch switch {
            >= '۰' and <= '۹'=>(char)('0'+ch-'۰'),
            >= '٠' and <= '٩'=>(char)('0'+ch-'٠'),
            _=>ch
        }).ToArray();
        var value=new string(chars);
        var digits=value.Count(char.IsAsciiDigit);
        if(value.Length>32 || value.Any(char.IsControl) || digits is <7 or >20 ||
            value.Any(ch=>!(char.IsAsciiDigit(ch)||char.IsWhiteSpace(ch)||ch is '+' or '(' or ')' or '-')) ||
            value.Count(ch=>ch=='+')>1 || (value.Contains('+') && value[0]!='+'))
            throw new ArgumentException("Customer phone number must contain 7-20 digits and only common phone formatting characters.",nameof(phoneNumber));
        return value;
    }
}
