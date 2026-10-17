using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using GameNet.Desktop.Features.Sessions;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Contracts.V1.Security;
using Xunit;

namespace GameNet.Desktop.Tests;

public sealed partial class SessionWorkflowViewModelTests
{
    [Fact]
    public async Task Wallet_cash_top_up_credits_only_requested_amount_and_displays_server_change()
    {
        var oldCulture = CultureInfo.CurrentCulture;
        var oldUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var customer = new CustomerResponse(
                Guid.NewGuid(), "C-WALLET-01", "Wallet Test", "Player", null,
                CustomerKindContract.Registered, CustomerStatusContract.Active, DateTimeOffset.UtcNow, 1);
            var client = new FakeGameNetServerClient([], []);
            client.Customers.Add(customer);
            var viewModel = new SessionWorkflowViewModel(
                client, permission => permission is Permissions.WalletWrite or Permissions.CustomersRead);

            viewModel.WalletCustomerSearchText = customer.Code;
            viewModel.SearchWalletCustomersCommand.Execute(null);
            await WaitUntilAsync(() => viewModel.WalletCustomers.Count == 1);

            Assert.Equal(customer.Id, viewModel.SelectedWalletCustomer?.Id);
            viewModel.LoadWalletBalanceCommand.Execute(null);
            await WaitUntilAsync(() => viewModel.CurrentWalletBalance is not null);
            Assert.Equal(0L, viewModel.CurrentWalletBalance!.PurchasedBalanceToman);

            viewModel.WalletAmountToCreditText = "105000";
            viewModel.WalletAmountReceivedText = "104999";
            viewModel.ConfirmWalletTopUp = true;
            Assert.False(viewModel.WalletCashTopUpCommand.CanExecute(null));

            viewModel.WalletAmountReceivedText = "110000";
            Assert.True(viewModel.WalletCashTopUpCommand.CanExecute(null));
            var success = WaitForStatusAsync(viewModel,
                message => message.Contains("Top-up recorded", StringComparison.Ordinal));
            viewModel.WalletCashTopUpCommand.Execute(null);
            await success;

            Assert.Equal(1, client.WalletTopUpWriteCount);
            Assert.Equal(customer.Id, client.LastWalletTopUpCustomerId);
            Assert.Equal(105_000L, client.LastWalletTopUpRequest!.AmountToCreditToman);
            Assert.Equal(110_000L, client.LastWalletTopUpRequest.AmountReceivedToman);
            Assert.False(string.IsNullOrWhiteSpace(client.LastWalletTopUpIdempotencyKey));
            Assert.Equal(105_000L, viewModel.CurrentWalletBalance!.PurchasedBalanceToman);
            Assert.Contains("5,000 change", viewModel.WalletTopUpOutcomeText, StringComparison.Ordinal);
            Assert.Contains("105,000 Toman", viewModel.WalletTopUpOutcomeText, StringComparison.Ordinal);
            Assert.Equal(string.Empty, viewModel.WalletAmountToCreditText);
            Assert.Equal(string.Empty, viewModel.WalletAmountReceivedText);
            Assert.False(viewModel.ConfirmWalletTopUp);
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUiCulture;
        }
    }

    [Fact]
    public async Task Wallet_picker_excludes_walk_in_and_blocked_customers()
    {
        var activeRegistered = new CustomerResponse(
            Guid.NewGuid(), "C-WALLET-02", "Registered", null, null,
            CustomerKindContract.Registered, CustomerStatusContract.Active, DateTimeOffset.UtcNow, 1);
        var walkIn = new CustomerResponse(
            Guid.NewGuid(), "W-WALLET-01", null, null, null,
            CustomerKindContract.WalkIn, CustomerStatusContract.Active, DateTimeOffset.UtcNow, 1);
        var blocked = new CustomerResponse(
            Guid.NewGuid(), "C-WALLET-03", "Blocked", null, null,
            CustomerKindContract.Registered, CustomerStatusContract.Blocked, DateTimeOffset.UtcNow, 1);
        var client = new FakeGameNetServerClient([], []);
        client.Customers.AddRange([activeRegistered, walkIn, blocked]);
        var viewModel = new SessionWorkflowViewModel(
            client, permission => permission is Permissions.WalletWrite or Permissions.CustomersRead);

        viewModel.SearchWalletCustomersCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.WalletCustomers.Count == 1);

        Assert.Equal(activeRegistered.Id, Assert.Single(viewModel.WalletCustomers).Id);
        viewModel.SelectedWalletCustomer = walkIn;
        Assert.False(viewModel.LoadWalletBalanceCommand.CanExecute(null));
        Assert.False(viewModel.WalletCashTopUpCommand.CanExecute(null));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Timed out waiting for the Wallet ViewModel state to update.");
            await Task.Delay(10);
        }
    }
}
