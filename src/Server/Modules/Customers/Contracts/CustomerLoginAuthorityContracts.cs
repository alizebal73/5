namespace GameNet.Server.Modules.Customers.Contracts;

public interface ICustomerLoginAuthority
{
    Task<bool> IsLoginUsableAsync(Guid customerId, string jti, CancellationToken cancellationToken);
}
