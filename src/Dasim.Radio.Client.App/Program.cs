using Avalonia;
using Dasim.Radio.Audio;
using Dasim.Radio.Audio.Concentus;
using Dasim.Radio.Client.App.ViewModels;
using Dasim.Radio.Client.Audio.OwnAudio;
using Dasim.Radio.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dasim.Radio.Client.App;

internal static class Program
{
    /// <summary>The composition root, available to <see cref="App"/> once <see cref="Main"/> has built it.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    [STAThread]
    public static void Main(string[] args)
    {
        Services = BuildServices();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Referenced by the Avalonia design-time tooling.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();

    private static IServiceProvider BuildServices()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSimpleConsole());
        services.AddSingleton(configuration);

        string natsUrl = configuration["Nats:Url"] ?? "nats://srv_brk:4222";
        services.AddDasimRadioMessaging(natsUrl);
        services.AddConcentusOpusCodec();

        // Device I/O — defaults unless a device id is configured. (Device re-selection at runtime is a follow-up.)
        services.AddSingleton<IAudioCaptureDevice>(sp => new OwnAudioCaptureDevice(
            configuration["Client:CaptureDeviceId"], sp.GetRequiredService<ILogger<OwnAudioCaptureDevice>>()));
        services.AddSingleton<IAudioPlaybackDevice>(sp => new OwnAudioPlaybackDevice(
            configuration["Client:PlaybackDeviceId"], sp.GetRequiredService<ILogger<OwnAudioPlaybackDevice>>()));
        services.AddSingleton<IAudioDeviceEnumerator, OwnAudioDeviceEnumerator>();

        // Push-to-talk: on-screen button + (configured) global hotkey, registered before AddDasimRadioClient
        // so it wins over that method's ManualPushToTalk default.
        services.AddSingleton<ManualPushToTalk>();
        services.AddSingleton<IPushToTalkHotkey>(sp => PushToTalkComposition.Create(sp, configuration));

        services.AddDasimRadioClient(configuration);
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
