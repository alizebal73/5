using System.Text.Json;
using GameNet.Server.Modules.Wallet.Api;
using GameNet.Server.Modules.Wallet.Application;
using GameNet.Server.Modules.Wallet.Infrastructure;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Wallet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Wallet;

public sealed class WalletEndpointContractTests
{
    [Fact]
    public void Wallet_read_and_mutation_routes_require_operator_authentication_and_wallet_permission()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        builder.Services.AddAuthorization();
        // Minimal API parameter-source inference recognizes IWalletService as an injected service.
        builder.Services.AddScoped<IWalletService, WalletService>();

        using var app = builder.Build();
        app.MapWalletEndpoints();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        var balance = Assert.Single(routes, endpoint =>
            endpoint.RoutePattern.RawText == "/api/v1/wallet/customers/{customerId:guid}/balance");
        var topUp = Assert.Single(routes, endpoint =>
            endpoint.RoutePattern.RawText == "/api/v1/wallet/customers/{customerId:guid}/cash-top-up");

        AssertRoutePolicy(balance, "GET");
        AssertRoutePolicy(topUp, "POST");
    }

    [Fact]
    public void Wallet_v1_json_contract_uses_stable_camel_case_integer_toman_fields()
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var request = new WalletCashTopUpRequest(105_000, 110_000, 7);
        using var requestDocument = JsonDocument.Parse(JsonSerializer.Serialize(request, jsonOptions));

        Assert.Equal(105_000L, requestDocument.RootElement.GetProperty("amountToCreditToman").GetInt64());
        Assert.Equal(110_000L, requestDocument.RootElement.GetProperty("amountReceivedToman").GetInt64());
        Assert.Equal(7, requestDocument.RootElement.GetProperty("expectedCustomerVersion").GetInt32());

        var response = new WalletCashTopUpResponse(
            Guid.Parse("fae7eec6-8dba-4ab9-b76f-8431128e4f44"),
            Guid.Parse("f1a2d0d8-2c95-47d9-9a84-f615f582039f"),
            Guid.Parse("77b529cc-3f14-4510-865b-7d1d31bce6f1"),
            105_000, 110_000, 5_000, 305_000, 8,
            DateTimeOffset.Parse("2026-10-19T09:00:00Z"));
        using var responseDocument = JsonDocument.Parse(JsonSerializer.Serialize(response, jsonOptions));

        Assert.Equal(105_000L, responseDocument.RootElement.GetProperty("amountCreditedToman").GetInt64());
        Assert.Equal(110_000L, responseDocument.RootElement.GetProperty("amountReceivedToman").GetInt64());
        Assert.Equal(5_000L, responseDocument.RootElement.GetProperty("changeGivenToman").GetInt64());
        Assert.Equal(305_000L, responseDocument.RootElement.GetProperty("balanceAfterToman").GetInt64());
        Assert.Equal(8, responseDocument.RootElement.GetProperty("walletVersion").GetInt32());
    }

    private static void AssertRoutePolicy(RouteEndpoint endpoint, string expectedMethod)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
        Assert.NotNull(methods);
        Assert.Contains(expectedMethod, methods!.HttpMethods);

        var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        Assert.Contains(policies, policy => string.Equals(policy.Policy, "Operator", StringComparison.Ordinal));
        Assert.Contains(policies, policy => string.Equals(
            policy.Policy, Permissions.WalletWrite, StringComparison.Ordinal));
    }
}
