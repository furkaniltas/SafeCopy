using System.Windows;
using SafeCopy.App.Extensions;
using SafeCopy.App.Services;
using SafeCopy.App.ViewModels;
using SafeCopy.Detectors;
using SafeCopy.Infrastructure.Batch;
using SafeCopy.Ocr;
using SafeCopy.Renderer;
using Microsoft.Extensions.DependencyInjection;

namespace SafeCopy.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        services.AddInfrastructure();
        services.AddOcr();
        services.AddDocumentEngine();
        services.AddDetectors();
        services.AddRenderer();
        services.AddBatch();

        // App services
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
