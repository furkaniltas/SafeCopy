using System.Windows;
using EksimSafeCopy.App.Extensions;
using EksimSafeCopy.App.Services;
using EksimSafeCopy.App.ViewModels;
using EksimSafeCopy.Detectors;
using EksimSafeCopy.Ocr;
using EksimSafeCopy.Renderer;
using Microsoft.Extensions.DependencyInjection;

namespace EksimSafeCopy.App;

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
