using System.Text;
using System.Text.Json;
using WowVmMonitor.Core.Configuration;
using WowVmMonitor.Infrastructure.Configuration;

namespace WowVmMonitor.Tests.Infrastructure;

public sealed class ConfigurationStoreTests
{
    [Fact]
    public void SavesAndLoadsVersionOneConfiguration()
    {
        using var fixture = new ConfigurationFixture();

        fixture.Store.Save(fixture.Configuration("vm-one"));
        var result = fixture.Store.Load();

        Assert.False(result.RequiresUserConfirmation);
        Assert.Equal("vm-one", result.Configuration.Machines[0].Id);
    }

    [Fact]
    public void CorruptPrimaryRestoresValidBackupWithWarning()
    {
        using var fixture = new ConfigurationFixture();
        fixture.WritePrimary("{broken");
        fixture.WriteBackup(fixture.ValidV1Json("vm-backup"));

        var result = fixture.Store.Load();

        Assert.False(result.RequiresUserConfirmation);
        Assert.Equal("vm-backup", result.Configuration.Machines[0].Id);
        Assert.Contains(result.Messages, message => message.Code == "configuration.recovered.backup");
        Assert.Single(Directory.GetFiles(fixture.Root, "config.corrupt-*.json"));
    }

    [Fact]
    public void CorruptPrimaryAndBackupCreateDefaultAndRequireConfirmation()
    {
        using var fixture = new ConfigurationFixture();
        fixture.WritePrimary("{broken-primary");
        fixture.WriteBackup("{broken-backup");

        var result = fixture.Store.Load();

        Assert.True(result.RequiresUserConfirmation);
        Assert.Empty(result.Configuration.Machines);
        Assert.Equal(2, Directory.GetFiles(fixture.Root, "*.corrupt-*.json").Length);
    }

    [Fact]
    public void ReplacementFailureLeavesPreviousConfigurationReadable()
    {
        using var fixture = new ConfigurationFixture();
        fixture.Store.Save(fixture.Configuration("old"));
        fixture.FileOperations.FailNextReplace = true;

        Assert.Throws<IOException>(() => fixture.Store.Save(fixture.Configuration("new")));

        Assert.Equal("old", fixture.Store.Load().Configuration.Machines[0].Id);
    }

    private sealed class ConfigurationFixture : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public ConfigurationFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "WowVmMonitor.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            FileOperations = new TestFileOperations();
            Store = new ConfigurationStore(Root, new ConfigurationMigrator(), FileOperations);
        }

        public string Root { get; }

        public TestFileOperations FileOperations { get; }

        public ConfigurationStore Store { get; }

        public MonitorConfiguration Configuration(string id) =>
            new(
                1,
                new MonitoringConfiguration(60, 10, 300, 600),
                [new MachineConfiguration(
                    id,
                    id,
                    @"\\server\wowlogs",
                    true,
                    MachineConfiguration.CredentialTargetFor(id))]);

        public string ValidV1Json(string id) => JsonSerializer.Serialize(Configuration(id), JsonOptions);

        public void WritePrimary(string content) =>
            File.WriteAllText(Path.Combine(Root, "config.json"), content, new UTF8Encoding(false));

        public void WriteBackup(string content) =>
            File.WriteAllText(Path.Combine(Root, "config.json.bak"), content, new UTF8Encoding(false));

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class TestFileOperations : IConfigurationFileOperations
    {
        public bool FailNextReplace { get; set; }

        public DateTimeOffset UtcNow => new(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);

        public bool Exists(string path) => File.Exists(path);

        public string ReadAllText(string path) => File.ReadAllText(path, Encoding.UTF8);

        public void WriteAllTextAndFlush(string path, string content) =>
            File.WriteAllText(path, content, new UTF8Encoding(false));

        public void Move(string source, string destination) => File.Move(source, destination);

        public void Replace(string source, string destination, string backup)
        {
            if (FailNextReplace)
            {
                FailNextReplace = false;
                throw new IOException("Injected replacement failure.");
            }

            File.Replace(source, destination, backup);
        }

        public void Delete(string path) => File.Delete(path);

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    }
}
