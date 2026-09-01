namespace SshTunneler.Models;

public sealed class UiSettings : ObservableObject
{
    private double _opacity = 0.88;
    private bool _topmost = true;
    private bool _autoStartWithWindows;
    private bool _autoReconnect = true;
    private bool _startMinimized;
    private int _localPortRangeStart = 20000;
    private int _localPortRangeEnd = 20999;
    private string? _browserPath;

    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 380;
    public double Height { get; set; } = 280;

    /// <summary>Window opacity, 0.35..1.0.</summary>
    public double Opacity { get => _opacity; set => SetField(ref _opacity, value); }

    public bool Topmost { get => _topmost; set => SetField(ref _topmost, value); }

    public bool AutoStartWithWindows { get => _autoStartWithWindows; set => SetField(ref _autoStartWithWindows, value); }

    public bool AutoReconnect { get => _autoReconnect; set => SetField(ref _autoReconnect, value); }

    public bool StartMinimized { get => _startMinimized; set => SetField(ref _startMinimized, value); }

    public int LocalPortRangeStart { get => _localPortRangeStart; set => SetField(ref _localPortRangeStart, value); }

    public int LocalPortRangeEnd { get => _localPortRangeEnd; set => SetField(ref _localPortRangeEnd, value); }

    /// <summary>Empty means the system default browser.</summary>
    public string? BrowserPath { get => _browserPath; set => SetField(ref _browserPath, value); }
}
