using WowVmMonitor.App.History;

namespace WowVmMonitor.Tests.App;

public sealed class InMemoryIncidentHistoryTests
{
    [Fact]
    public async Task QueryFiltersByMachineAndEventType()
    {
        var history = new InMemoryIncidentHistory(capacity: 100);
        history.Append(new IncidentRecord(DateTimeOffset.UtcNow, "vm-01", "Alert", "alert"));
        history.Append(new IncidentRecord(DateTimeOffset.UtcNow, "vm-02", "Recovery", "recovery"));

        var records = await history.QueryAsync(
            new IncidentQuery("vm-01", "Alert"),
            CancellationToken.None);

        var record = Assert.Single(records);
        Assert.Equal("vm-01", record.MachineId);
    }

    [Fact]
    public async Task CapacityKeepsNewestRecords()
    {
        var history = new InMemoryIncidentHistory(capacity: 2);
        history.Append(new IncidentRecord(DateTimeOffset.UtcNow.AddMinutes(-2), "vm-01", "Alert", "one"));
        history.Append(new IncidentRecord(DateTimeOffset.UtcNow.AddMinutes(-1), "vm-01", "Alert", "two"));
        history.Append(new IncidentRecord(DateTimeOffset.UtcNow, "vm-01", "Recovery", "three"));

        var records = await history.QueryAsync(new IncidentQuery(null, null), CancellationToken.None);

        Assert.Equal(["three", "two"], records.Select(record => record.Message));
    }
}
