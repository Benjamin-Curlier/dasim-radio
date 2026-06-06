using Dasim.Radio.Client.Ptt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpHook.Data;

namespace Dasim.Radio.Client.App;

/// <summary>
/// Builds the client's push-to-talk source: the on-screen button always, plus an OS/session-appropriate
/// global hotkey when configured (the #12 decision — SharpHook on Windows/X11, evdev on Wayland). When a
/// global provider is available the two are combined so either input transmits.
/// </summary>
internal static class PushToTalkComposition
{
    public static IPushToTalkHotkey Create(IServiceProvider services, IConfiguration configuration)
    {
        var onScreen = services.GetRequiredService<ManualPushToTalk>();
        IPushToTalkHotkey? global = CreateGlobal(services, configuration);
        return global is null ? onScreen : new CompositePushToTalk(onScreen, global);
    }

    private static IPushToTalkHotkey? CreateGlobal(IServiceProvider services, IConfiguration configuration)
    {
        ILoggerFactory loggers = services.GetRequiredService<ILoggerFactory>();
        SessionType session = SessionTypeDetector.Detect();

        // Windows / X11 — SharpHook global hook, if a PTT key is configured.
        if (OperatingSystem.IsWindows() || session == SessionType.X11)
        {
            return Enum.TryParse(configuration["Client:Ptt:Key"], ignoreCase: true, out KeyCode key)
                ? new SharpHookPushToTalk(key, loggers.CreateLogger<SharpHookPushToTalk>())
                : null;
        }

        // Wayland — evdev (SharpHook can't capture unfocused there), if a device + key code are configured.
        if (session == SessionType.Wayland)
        {
            string? device = configuration["Client:Ptt:EvdevDevice"];
            return !string.IsNullOrWhiteSpace(device)
                   && ushort.TryParse(configuration["Client:Ptt:EvdevKeyCode"], out ushort code)
                ? new EvdevPushToTalk(device, code, loggers.CreateLogger<EvdevPushToTalk>())
                : null;
        }

        return null; // headless / unknown session — on-screen PTT only
    }
}
