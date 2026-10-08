using GameNet.Shared.Contracts.V1.Api;
namespace GameNet.ContractTests;
public sealed class ApiContractTests
{
    [Fact] public void V1_contract_is_explicit()
    {
        Assert.Equal("v1", ContractVersions.V1);
        Assert.Equal("X-GameNet-Contract", ApiHeaders.ContractVersion);
    }
}
