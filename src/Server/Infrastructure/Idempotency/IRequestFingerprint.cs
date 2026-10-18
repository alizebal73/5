namespace GameNet.Server.Infrastructure.Idempotency;

public interface IRequestFingerprint
{
    string Compute(object payload);
}
