using System.Text;

namespace WowVmMonitor.Infrastructure.Configuration;

public sealed class SystemConfigurationFileOperations : IConfigurationFileOperations
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public bool Exists(string path) => File.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path, Encoding.UTF8);

    public void WriteAllTextAndFlush(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        writer.Write(content);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    public void Move(string source, string destination) => File.Move(source, destination);

    public void Replace(string source, string destination, string backup) =>
        File.Replace(source, destination, backup);

    public void Delete(string path) => File.Delete(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
}
