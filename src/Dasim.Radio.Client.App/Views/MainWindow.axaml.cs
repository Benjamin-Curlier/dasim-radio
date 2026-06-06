using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Dasim.Radio.Client.App.ViewModels;

namespace Dasim.Radio.Client.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Hold-to-talk: press the floor on pointer-down, release on pointer-up (Button.Click is release-only).
        var ptt = this.FindControl<Button>("PttButton");
        if (ptt is not null)
        {
            ptt.AddHandler(PointerPressedEvent, OnPttPressed, RoutingStrategies.Tunnel);
            ptt.AddHandler(PointerReleasedEvent, OnPttReleased, RoutingStrategies.Tunnel);
        }
    }

    private void OnPttPressed(object? sender, PointerPressedEventArgs e) =>
        (DataContext as MainViewModel)?.PushToTalkDown();

    private void OnPttReleased(object? sender, PointerReleasedEventArgs e) =>
        (DataContext as MainViewModel)?.PushToTalkUp();
}
