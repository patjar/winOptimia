using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EPFOptimizerPro.Models;

public sealed class TaskProgressInfo : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _icon = "•";
    private string _status = "En attente";
    private string _message = string.Empty;
    private string _statusColor = "#94A3B8";
    private int _progress;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Icon
    {
        get => _icon;
        set => SetField(ref _icon, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string Message
    {
        get => _message;
        set => SetField(ref _message, value);
    }

    public string StatusColor
    {
        get => _statusColor;
        set => SetField(ref _statusColor, value);
    }

    public int Progress
    {
        get => _progress;
        set => SetField(ref _progress, Math.Clamp(value, 0, 100));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
