using Xunit;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Shared.Contracts.V1.Api;
using Microsoft.AspNetCore.Http;

namespace GameNet.Server.UnitTests;

public sealed class ContractVersionMiddlewareTests
{
    [Fact]
    public async Task Missing_contract_header_is_rejected()
    {
        var nextCalled = false;
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/customers";

        var middleware = new ContractVersionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task V1_contract_header_allows_request()
    {
        var nextCalled = false;
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/customers";
        context.Request.Headers[ApiHeaders.ContractVersion] = ContractVersions.V1;

        var middleware = new ContractVersionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }
}
