using System.Collections.ObjectModel;
using WowVmMonitor.App.Mvvm;
using WowVmMonitor.App.Ui;

namespace WowVmMonitor.App.History;

public sealed class IncidentHistoryViewModel : ObservableObject
{
    private readonly IIncidentHistoryReader _reader;
    private readonly IUiDispatcher _dispatcher;
    private string? _machineFilter;
    private string? _eventTypeFilter;

    public IncidentHistoryViewModel(IIncidentHistoryReader reader, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _reader = reader;
        _dispatcher = dispatcher;
        RefreshCommand = new AsyncCommand(RefreshAsync);
    }

    public ObservableCollection<IncidentRecordViewModel> Records { get; } = [];
    public AsyncCommand RefreshCommand { get; }
    public string Notice => "History is kept in memory until WowVmMonitor exits.";
    public string? MachineFilter { get => _machineFilter; set => SetProperty(ref _machineFilter, value); }
    public string? EventTypeFilter { get => _eventTypeFilter; set => SetProperty(ref _eventTypeFilter, value); }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var records = await _reader
            .QueryAsync(new IncidentQuery(MachineFilter, EventTypeFilter), cancellationToken)
            .ConfigureAwait(false);
        await _dispatcher.InvokeAsync(() =>
        {
            Records.Clear();
            foreach (var record in records)
            {
                Records.Add(new IncidentRecordViewModel(record));
            }
        }, cancellationToken).ConfigureAwait(false);
    }
}
