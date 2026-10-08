namespace GameNet.Server.Modules.Customers.Application;

public interface IPinHasher
{
    string Hash(string pin);
    bool Verify(string pin, string encodedHash);
}
