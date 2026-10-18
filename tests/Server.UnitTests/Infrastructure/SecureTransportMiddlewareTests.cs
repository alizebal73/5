using System.Net;
using GameNet.Server.Infrastructure.Security.Transport;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GameNet.Server.UnitTests.Infrastructure;

public sealed class SecureTransportMiddlewareTests
{
    [Fact]
    public async Task Remote_http_authentication_is_rejected_before_endpoint_execution()
    {
        var context = CreateContext("/api/v1/auth/login", IPAddress.Parse("192.0.2.10"));
        var invoked = false;
        var middleware = new SecureTransportMiddleware(_ => { invoked = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(context);
        Assert.False(invoked);
        Assert.Equal(StatusCodes.Status426UpgradeRequired, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);
    }

    [Fact]
    public async Task Loopback_http_is_allowed_for_local_smoke_and_development()
    {
        var context = CreateContext("/api/v1/auth/login", IPAddress.Loopback);
        var invoked = false;
        var middleware = new SecureTransportMiddleware(_ => { invoked = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(context);
        Assert.True(invoked);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Non_sensitive_health_route_is_not_blocked_by_transport_guard()
    {
        var context = CreateContext("/health", IPAddress.Parse("192.0.2.10"));
        var invoked = false;
        var middleware = new SecureTransportMiddleware(_ => { invoked = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(context);
        Assert.True(invoked);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    private static DefaultHttpContext CreateContext(string path, IPAddress remoteIp)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = remoteIp;
        context.Response.Body = new MemoryStream();
        return context;
    }
}
