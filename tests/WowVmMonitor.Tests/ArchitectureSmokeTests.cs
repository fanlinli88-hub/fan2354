using WowVmMonitor.Core;
using WowVmMonitor.Infrastructure;

namespace WowVmMonitor.Tests;

public sealed class ArchitectureSmokeTests
{
    [Fact]
    public void TestProjectCanLoadCoreAndInfrastructureAssemblies()
    {
        Assert.Equal("WowVmMonitor.Core", typeof(CoreAssemblyMarker).Assembly.GetName().Name);
        Assert.Equal("WowVmMonitor.Infrastructure", typeof(InfrastructureAssemblyMarker).Assembly.GetName().Name);
    }
}
