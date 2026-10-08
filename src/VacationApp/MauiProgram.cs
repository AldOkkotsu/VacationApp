using Microsoft.Extensions.Logging;
using VacationApp.Core;

namespace VacationApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<IVacationRepository>(_ =>
            new JsonVacationRepository(Path.Combine(FileSystem.AppDataDirectory, "vacation-data.json")));
        builder.Services.AddSingleton<VacationService>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
