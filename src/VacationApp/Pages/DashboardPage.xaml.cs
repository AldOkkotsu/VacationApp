using VacationApp.Core;
using VacationApp.ViewModels;

namespace VacationApp.Pages;

public partial class DashboardPage : ContentPage
{
    private readonly VacationService _service;
    private readonly DashboardViewModel _viewModel;
    private bool _leaving;

    public DashboardPage(VacationService service, UserProfile actor)
    {
        InitializeComponent();
        _service = service;
        _viewModel = new DashboardViewModel(service, actor);
        BindingContext = _viewModel;
    }

    private async void OnSubmitClicked(object? sender, EventArgs e) => await RunActionAsync(async () =>
    {
        if (_viewModel.StartDate is not DateTime start || _viewModel.EndDate is not DateTime end)
            throw new VacationValidationException("Selecciona el primer y el último día de vacaciones.");
        _service.SubmitRequest(_viewModel.Actor, DateOnly.FromDateTime(start), DateOnly.FromDateTime(end), _viewModel.Reason);
        _viewModel.Reason = string.Empty;
        _viewModel.Feedback = "Solicitud enviada. Tu líder recibirá la solicitud para revisarla.";
        await Task.CompletedTask;
    });

    private async void OnApproveClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: RequestCardViewModel card }) return;
        await RunActionAsync(async () =>
        {
            var confirmed = await DisplayAlertAsync("Aprobar solicitud",
                $"{card.EmployeeName}\n{card.Dates}\n{card.Days}\n\n¿Deseas aprobar esta solicitud?", "Aprobar", "Volver");
            if (!confirmed) return;
            _service.Approve(_viewModel.Actor, card.Request.Id);
            _viewModel.Feedback = _viewModel.Actor.Role == UserRole.Leader
                ? "Solicitud aprobada y enviada a Recursos Humanos."
                : "Vacaciones aprobadas. Nómina puede registrar la solicitud.";
        });
    }

    private async void OnRejectClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: RequestCardViewModel card }) return;
        await RunActionAsync(async () =>
        {
            var comment = await DisplayPromptAsync("Rechazar solicitud",
                $"Escribe el motivo del rechazo para {card.EmployeeName}. El empleado podrá leerlo.",
                "Rechazar", "Volver", "Motivo obligatorio", 500, Keyboard.Text);
            if (comment is null) return;
            _service.Reject(_viewModel.Actor, card.Request.Id, comment);
            _viewModel.Feedback = "Solicitud rechazada. Los días reservados vuelven a estar disponibles.";
        });
    }

    private async void OnRegisterPayrollClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: RequestCardViewModel card }) return;
        await RunActionAsync(async () =>
        {
            var confirmed = await DisplayAlertAsync("Registrar en nómina",
                $"{card.EmployeeName}\n{card.Dates}\n{card.Days}\n\n¿Confirmas el registro de estas vacaciones?", "Registrar", "Volver");
            if (!confirmed) return;
            _service.RegisterPayroll(_viewModel.Actor, card.Request.Id);
            _viewModel.Feedback = "Solicitud registrada en nómina. El flujo de aprobación ha finalizado.";
        });
    }

    private async void OnHistoryClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: RequestCardViewModel card }) return;
        await RunActionAsync(async () =>
            await DisplayAlertAsync("Seguimiento de la solicitud", card.History, "Cerrar"));
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) =>
        await RunActionAsync(() => Task.CompletedTask);

    private async Task RunActionAsync(Func<Task> action)
    {
        if (_viewModel.IsBusy || _leaving) return;
        _viewModel.IsBusy = true;
        _viewModel.Feedback = string.Empty;
        try
        {
            await action();
            _viewModel.Reload();
            if (_viewModel.HasFeedback) SemanticScreenReader.Announce(_viewModel.Feedback);
        }
        catch (VacationValidationException ex)
        {
            await DisplayAlertAsync("Revisa la solicitud", ex.Message, "Entendido");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await DisplayAlertAsync("No se pudo guardar", "No se pudo leer o guardar la información local. Inténtalo de nuevo.", "Entendido");
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e) => await LogoutAsync();

    private async Task LogoutAsync()
    {
        if (_viewModel.IsBusy || _leaving) return;
        _leaving = true;
        _service.SignOut();
        await Navigation.PopToRootAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        Dispatcher.Dispatch(async () => await LogoutAsync());
        return true;
    }
}
