namespace WowVmMonitor.Infrastructure.Configuration;

public interface IConfigurationFileOperations
{
    DateTimeOffset UtcNow { get; }

    bool Exists(string path);

    string ReadAllText(string path);

    void WriteAllTextAndFlush(string path, string content);

    void Move(string source, string destination);

    void Replace(string source, string destination, string backup);

    void Delete(string path);

    void CreateDirectory(string path);
}
