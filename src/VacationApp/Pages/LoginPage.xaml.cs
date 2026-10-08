using VacationApp.Core;
using VacationApp.ViewModels;

namespace VacationApp.Pages;

public partial class LoginPage : ContentPage
{
    private readonly VacationService _service;
    private bool _isBusy;

    public LoginPage(VacationService service)
    {
        InitializeComponent();
        _service = service;
        var accounts = service.GetDemoAccounts()
            .Select(account => new AccountOption(account,
                $"{account.FullName} · {DashboardViewModel.RoleLabel(account.Role)}"))
            .ToList();
        AccountPicker.ItemsSource = accounts;
        AccountPicker.SelectedIndexChanged += (_, _) =>
            AccountEmailLabel.Text = (AccountPicker.SelectedItem as AccountOption)?.Account.Email;
        AccountPicker.SelectedIndex = 0;
        PasswordEntry.Text = VacationService.DemoPassword;
        DemoPasswordLabel.Text = $"Contraseña demo: {VacationService.DemoPassword}";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _service.SignOut();
        ErrorLabel.IsVisible = false;
    }

    private async void OnSignInClicked(object? sender, EventArgs e)
    {
        if (_isBusy) return;
        ErrorLabel.IsVisible = false;
        if (AccountPicker.SelectedItem is not AccountOption option)
        {
            ShowError("Selecciona una cuenta para continuar.");
            return;
        }

        _isBusy = true;
        SignInButton.IsEnabled = false;
        AccountPicker.IsEnabled = false;
        PasswordEntry.IsEnabled = false;
        SignInActivity.IsVisible = SignInActivity.IsRunning = true;
        try
        {
            var account = _service.SignIn(option.Account.Email, PasswordEntry.Text ?? string.Empty);
            if (account is null)
            {
                ShowError("La contraseña no es correcta. Revisa la contraseña demo indicada arriba.");
                return;
            }

            await Navigation.PushAsync(new DashboardPage(_service, account));
        }
        catch (Exception ex) when (ex is VacationValidationException or IOException or UnauthorizedAccessException)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _isBusy = false;
            SignInButton.IsEnabled = AccountPicker.IsEnabled = PasswordEntry.IsEnabled = true;
            SignInActivity.IsVisible = SignInActivity.IsRunning = false;
        }
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
        SemanticScreenReader.Announce(message);
    }

    private sealed record AccountOption(UserProfile Account, string Label);
}
