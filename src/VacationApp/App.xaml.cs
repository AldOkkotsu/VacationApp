using VacationApp.Core;
using VacationApp.Pages;

namespace VacationApp;

public partial class App : Application
{
    private readonly VacationService _service;

    public App(VacationService service)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Light;
        _service = service;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var navigation = new NavigationPage(new LoginPage(_service))
        {
            BarBackgroundColor = Color.FromArgb("#122D3D"),
            BarTextColor = Colors.White
        };
        return new Window(navigation) { Title = "VacationApp" };
    }
}
