using WowVmMonitor.App.Presentation;

namespace WowVmMonitor.App.History;

public sealed record IncidentRecordViewModel(IncidentRecord Record)
{
    public string TimestampText => LocalTimeText.Format(Record.Timestamp);
    public string MachineId => Record.MachineId;
    public string EventType => Record.EventType;
    public string Message => Record.Message;
}
