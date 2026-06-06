using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Dasim.Radio.Client.App.ViewModels;

/// <summary>Minimal INotifyPropertyChanged base so the view models need no MVVM-framework dependency.</summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
