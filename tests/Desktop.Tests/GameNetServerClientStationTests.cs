using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameNet.Desktop.Api;
using GameNet.Shared.Contracts.V1.Identity;
using GameNet.Shared.Contracts.V1.Stations;
using Xunit;

namespace GameNet.Desktop.Tests;

public sealed class GameNetServerClientStationTests
{
    [Fact]
    public async Task Station_requests_use_bearer_and_mutation_idempotency_key()
    {
        var handler = new StationApiHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5096/") };
        var client = new GameNetServerClient(http);
        await client.LoginAsync(new LoginRequest("operator", "password-example"));
        var rows = await client.GetStationsAsync();
        Assert.Single(rows);
        Assert.Equal("PC-01", rows[0].Code);
        var created = await client.CreateStationAsync(new CreateStationRequest("PC-02", "Second PC", StationTypeContract.Pc), "stable-create-key");
        Assert.Equal("PC-02", created.Code);
        var request = handler.Requests.Single(x => x.Path == "/api/v1/stations" && x.Method == "POST");
        Assert.Equal("Bearer test-token", request.Authorization);
        Assert.Equal(new[] { "stable-create-key" }, request.IdempotencyKeys);
        Assert.Equal(new[] { "v1" }, request.ContractVersions);
    }

    [Fact]
    public async Task Station_requests_are_blocked_before_sending_bearer_to_remote_http()
    {
        var handler = new StationApiHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://192.0.2.20:5080/") };
        var client = new GameNetServerClient(http);
        var exception = await Assert.ThrowsAsync<GameNetApiException>(() => client.GetStationsAsync());
        Assert.Equal("security.https_required", exception.Code);
        Assert.Equal(426, exception.StatusCode);
        Assert.Empty(handler.Requests);
    }

    private sealed record CapturedRequest(string Method, string Path, string? Authorization, string[] IdempotencyKeys, string[] ContractVersions);
    private sealed class StationApiHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(new CapturedRequest(request.Method.Method, path, request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.ToArray() : Array.Empty<string>(),
                request.Headers.TryGetValues("X-GameNet-Contract", out var versions) ? versions.ToArray() : Array.Empty<string>()));
            var json = path switch
            {
                "/api/v1/auth/login" => """{"data":{"accessToken":"test-token","tokenType":"Bearer","expiresAtUtc":"2030-01-01T00:00:00Z","userId":"5e7335e1-7425-4ff0-9e28-348b0e0bd306","username":"operator","displayName":"Operator","permissions":["stations.read","stations.operate"]},"correlationId":"test"}""",
                "/api/v1/stations" when request.Method == HttpMethod.Get => """{"data":[{"id":"c31e4c7e-806f-4d42-9f33-9940ac4ab351","code":"PC-01","name":"Front PC","type":1,"status":2,"version":1,"agentDeviceId":null,"agentOnline":false,"lastHeartbeatAtUtc":null,"agentVersion":null,"agentReportedState":null}],"correlationId":"test"}""",
                "/api/v1/stations" when request.Method == HttpMethod.Post => """{"data":{"id":"c638fe1b-0de3-4a40-9e8d-873c7afc4c2d","code":"PC-02","name":"Second PC","type":1,"status":2,"version":1,"agentDeviceId":null,"agentOnline":false,"lastHeartbeatAtUtc":null,"agentVersion":null,"agentReportedState":null},"correlationId":"test"}""",
                _ => """{"data":{},"correlationId":"test"}"""
            };
            return Task.FromResult(new HttpResponseMessage(path == "/api/v1/stations" && request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
