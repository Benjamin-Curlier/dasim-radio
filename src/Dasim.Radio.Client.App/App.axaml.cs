using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Dasim.Radio.Client.App.ViewModels;
using Dasim.Radio.Client.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dasim.Radio.Client.App;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = Program.Services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            var engine = Program.Services.GetRequiredService<RadioClientEngine>();
            var logger = Program.Services.GetRequiredService<ILogger<App>>();

            // The engine's Start touches the native audio devices and the global hook, which block — run
            // it off the UI thread so the window appears immediately.
            _ = Task.Run(async () =>
            {
                try
                {
                    await engine.StartAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Radio client engine failed to start.");
                }
            });

            desktop.ShutdownRequested += (_, _) =>
            {
                try
                {
                    engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Radio client engine shutdown failed.");
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
