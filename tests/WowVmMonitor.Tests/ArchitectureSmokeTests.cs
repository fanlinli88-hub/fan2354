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

    [Fact]
    public void CoreDoesNotReferenceOtherApplicationProjects()
    {
        var projectReferences = typeof(CoreAssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Where(assembly => assembly.Name?.StartsWith("WowVmMonitor.", StringComparison.Ordinal) == true)
            .Select(assembly => assembly.Name)
            .ToArray();

        Assert.Empty(projectReferences);
    }
}
