using GameNet.Server.Infrastructure.Security;
using GameNet.Server.Modules.Customers.Application;
using GameNet.Server.Modules.Customers.Api;
using GameNet.Server.Modules.Sessions.Api;
using GameNet.Server.Modules.Sessions.Application;
using GameNet.Server.Modules.Stations.Contracts;
using GameNet.Shared.Contracts.V1.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Customers;

/// <summary>
/// Metadata-only contract tests: customer entry must use the authenticated Agent path,
/// while customer tokens must not inherit Operator access to the dashboard Session API.
/// </summary>
public sealed class CustomerEntryEndpointContractTests
{
    [Fact]
    public void Customer_entry_and_session_routes_keep_their_actor_boundaries()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<IStationSessionAccess>(_ =>
            throw new InvalidOperationException("Metadata-only endpoint contract test must not invoke Station service."));
        builder.Services.AddScoped<ICustomerAuthenticationService>(_ =>
            throw new InvalidOperationException("Metadata-only endpoint contract test must not invoke Customer auth service."));
        builder.Services.AddScoped<ISessionService>(_ =>
            throw new InvalidOperationException("Metadata-only endpoint contract test must not invoke Session service."));

        using var app = builder.Build();
        app.MapCustomerAuthenticationEndpoints();
        app.MapAgentCustomerSessionEndpoints();
        app.MapSessionEndpoints();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        var directEntry = Assert.Single(routes, endpoint =>
            endpoint.RoutePattern.RawText == "/api/v1/agent/customer-session/start");
        AssertRoutePolicies(directEntry, "AgentTransport");
        Assert.DoesNotContain(
            directEntry.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            policy => string.Equals(policy.Policy, "Operator", StringComparison.Ordinal));

        var customerLogin = Assert.Single(routes, endpoint =>
            endpoint.RoutePattern.RawText == "/api/v1/customer-auth/login");
        Assert.NotNull(customerLogin.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.Equal("customer-password-login",
            customerLogin.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);

        var customerProfile = Assert.Single(routes, endpoint =>
            endpoint.RoutePattern.RawText == "/api/v1/customer-auth/me");
        AssertRoutePolicies(customerProfile, "Customer");

        var customerLogout = Assert.Single(routes, endpoint =>
            endpoint.RoutePattern.RawText == "/api/v1/customer-auth/logout");
        AssertRoutePolicies(customerLogout, "Customer");

        var operatorSessionStart = Assert.Single(routes, endpoint =>
            endpoint.RoutePattern.RawText == "/api/v1/sessions");
        AssertRoutePolicies(operatorSessionStart, "Operator", Permissions.SessionsOperate);

        // Both requirements are present: actor_type=Customer alone cannot satisfy the
        // Operator policy even when the caller knows a Session or Station identifier.
        Assert.Contains(
            operatorSessionStart.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            policy => string.Equals(policy.Policy, Permissions.SessionsOperate, StringComparison.Ordinal));
    }

    private static void AssertRoutePolicies(RouteEndpoint endpoint, params string[] expectedPolicies)
    {
        var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        foreach (var expected in expectedPolicies)
            Assert.Contains(policies, policy => string.Equals(policy.Policy, expected, StringComparison.Ordinal));
    }
}
