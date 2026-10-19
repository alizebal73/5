using GameNet.CustomerClient.Features;
using GameNet.Shared.Contracts.V1.Customers;
using Xunit;

namespace GameNet.Desktop.Tests;

public sealed class CustomerClientViewModelTests
{
    [Fact]
    public void Definitive_wallet_rejection_gets_a_new_idempotency_key_on_next_attempt()
    {
        var gateway = new FakeCustomerEntryGateway(
            Rejected("wallet.insufficient_balance"),
            Succeeded());
        using var viewModel = new CustomerClientViewModel(gateway)
        {
            Username = "player01",
            Password = "correct horse battery staple"
        };

        viewModel.LoginCommand.Execute(null);

        Assert.Single(gateway.Requests);
        Assert.False(viewModel.IsSessionStarted);
        var firstKey = gateway.Requests[0].IdempotencyKey;

        // Model the operator topping up the Wallet after the definitive rejection.
        viewModel.LoginCommand.Execute(null);

        Assert.Equal(2, gateway.Requests.Count);
        Assert.NotEqual(firstKey, gateway.Requests[1].IdempotencyKey);
        Assert.True(viewModel.IsSessionStarted);
    }

    [Fact]
    public void Ambiguous_result_reuses_the_same_idempotency_key_on_retry()
    {
        var gateway = new FakeCustomerEntryGateway(
            Rejected("client.result_unknown"),
            Succeeded());
        using var viewModel = new CustomerClientViewModel(gateway)
        {
            Username = "player01",
            Password = "correct horse battery staple"
        };

        viewModel.LoginCommand.Execute(null);

        Assert.Single(gateway.Requests);
        Assert.False(viewModel.IsSessionStarted);
        var firstKey = gateway.Requests[0].IdempotencyKey;

        // The Server might have committed before the reply was lost; a retry must replay it.
        viewModel.LoginCommand.Execute(null);

        Assert.Equal(2, gateway.Requests.Count);
        Assert.Equal(firstKey, gateway.Requests[1].IdempotencyKey);
        Assert.True(viewModel.IsSessionStarted);
    }

    private static CustomerClientEntryResponse Rejected(string code) =>
        new(false, null, null, null, null, null, code, "Rejected for test.");

    private static CustomerClientEntryResponse Succeeded() =>
        new(true, Guid.NewGuid(), "Test player", Guid.NewGuid(), 105_000,
            TimeSpan.TicksPerHour, null, null);

    private sealed class FakeCustomerEntryGateway(params CustomerClientEntryResponse[] responses)
        : ICustomerEntryGateway
    {
        private readonly Queue<CustomerClientEntryResponse> remaining = new(responses);

        public List<CustomerClientEntryRequest> Requests { get; } = [];

        public Task<CustomerClientEntryResponse> StartAsync(
            CustomerClientEntryRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (remaining.Count == 0)
                throw new InvalidOperationException("No customer-entry test response is configured.");
            return Task.FromResult(remaining.Dequeue());
        }
    }
}
