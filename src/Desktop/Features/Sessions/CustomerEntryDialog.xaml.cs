using System.Globalization;
using System.Windows;

namespace GameNet.Desktop.Features.Sessions;

public partial class CustomerEntryDialog : Window
{
    public CustomerEntryDialog(string stationCode)
    {
        InitializeComponent();
        var prompt = (string)FindResource("Customer.EntryPrompt");
        StationPrompt.Text = string.Format(CultureInfo.CurrentCulture, prompt, stationCode);
        Loaded += (_, _) => CustomerCodeInput.Focus();
    }

    public string CustomerCode => CustomerCodeInput.Text.Trim();

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CustomerCodeInput.Text))
        {
            ValidationMessage.Text = (string)FindResource("Customer.EntryIdRequired");
            CustomerCodeInput.Focus();
            return;
        }

        DialogResult = true;
    }
}
