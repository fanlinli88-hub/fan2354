using WowVmMonitor.App.Mvvm;
using WowVmMonitor.App.Presentation;

namespace WowVmMonitor.App.Monitoring;

public sealed class MachineStatusViewModel : ObservableObject
{
    private string _displayName;
    private string _statusText;
    private string _sharePath;
    private string? _latestLogPath;
    private DateTimeOffset? _lastWriteTime;
    private TimeSpan? _logAge;
    private string? _errorCode;

    public MachineStatusViewModel(MachineStatusSnapshot snapshot)
    {
        Id = snapshot.MachineId;
        _displayName = snapshot.DisplayName;
        _statusText = snapshot.StatusText;
        _sharePath = snapshot.SharePath;
        _latestLogPath = snapshot.LatestLogPath;
        _lastWriteTime = snapshot.LastWriteTime;
        _logAge = snapshot.LogAge;
        _errorCode = snapshot.ErrorCode;
    }

    public string Id { get; }
    public string DisplayName { get => _displayName; private set => SetProperty(ref _displayName, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string SharePath { get => _sharePath; private set => SetProperty(ref _sharePath, value); }
    public string? LatestLogPath { get => _latestLogPath; private set => SetProperty(ref _latestLogPath, value); }
    public DateTimeOffset? LastWriteTime
    {
        get => _lastWriteTime;
        private set
        {
            if (SetProperty(ref _lastWriteTime, value))
            {
                OnPropertyChanged(nameof(LastWriteTimeText));
            }
        }
    }
    public string LastWriteTimeText => LocalTimeText.Format(LastWriteTime);
    public TimeSpan? LogAge { get => _logAge; private set => SetProperty(ref _logAge, value); }
    public string? ErrorCode { get => _errorCode; private set => SetProperty(ref _errorCode, value); }

    public void Update(MachineStatusSnapshot snapshot)
    {
        DisplayName = snapshot.DisplayName;
        StatusText = snapshot.StatusText;
        SharePath = snapshot.SharePath;
        LatestLogPath = snapshot.LatestLogPath;
        LastWriteTime = snapshot.LastWriteTime;
        LogAge = snapshot.LogAge;
        ErrorCode = snapshot.ErrorCode;
    }
}
