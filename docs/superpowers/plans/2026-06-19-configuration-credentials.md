# Configuration And Credentials Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist recoverable, versioned JSON configuration while keeping eight independent share credentials in Windows Credential Manager and reconnecting enabled UNC shares when the application starts.

**Architecture:** Add secret-free configuration models and validation in Core, then implement migration, atomic persistence, recovery, native credential storage, and Windows share connection adapters in Infrastructure. The App startup path loads configuration, refuses unsafe defaults that require confirmation, reconnects enabled shares independently, and never transports passwords through configuration, logging, or error result types.

**Tech Stack:** C# 12, .NET 8, `System.Text.Json`, Windows `CredWriteW`/`CredReadW`/`CredDeleteW`, `WNetAddConnection2W`, xUnit, PowerShell.

---

## File Map

- Create `src/WowVmMonitor.Core/Configuration/MonitorConfiguration.cs`: secret-free root v1 model.
- Create `src/WowVmMonitor.Core/Configuration/MonitoringConfiguration.cs`: global timing settings.
- Create `src/WowVmMonitor.Core/Configuration/MachineConfiguration.cs`: per-machine ID, display name, UNC path, enabled flag, and credential target.
- Create `src/WowVmMonitor.Core/Configuration/ConfigurationValidator.cs`: schema-independent domain validation.
- Create `src/WowVmMonitor.Core/Configuration/ConfigurationLoadResult.cs`: loaded config, warnings, and confirmation gate.
- Create `src/WowVmMonitor.Infrastructure/Configuration/ConfigurationMigrator.cs`: v0 to v1 migration.
- Create `src/WowVmMonitor.Infrastructure/Configuration/ConfigurationStore.cs`: JSON loading, atomic saving, backup, and corruption recovery.
- Create `src/WowVmMonitor.Infrastructure/Configuration/IConfigurationFileOperations.cs`: injectable atomic file operations.
- Create `src/WowVmMonitor.Infrastructure/Configuration/SystemConfigurationFileOperations.cs`: real file-system implementation.
- Create `src/WowVmMonitor.Infrastructure/Credentials/ShareCredential.cs`: short-lived disposable credential value.
- Create `src/WowVmMonitor.Infrastructure/Credentials/ICredentialNativeApi.cs`: testable native boundary.
- Create `src/WowVmMonitor.Infrastructure/Credentials/WindowsCredentialNativeApi.cs`: Credential Manager P/Invoke implementation.
- Create `src/WowVmMonitor.Infrastructure/Credentials/WindowsCredentialStore.cs`: validated per-machine credential operations.
- Create `src/WowVmMonitor.Infrastructure/Shares/IWindowsNetworkApi.cs`: testable Windows network boundary.
- Create `src/WowVmMonitor.Infrastructure/Shares/IShareConnectionCoordinator.cs`: App-facing startup abstraction.
- Create `src/WowVmMonitor.Infrastructure/Shares/WindowsNetworkApi.cs`: `WNetAddConnection2W` adapter.
- Create `src/WowVmMonitor.Infrastructure/Shares/ShareConnectionResult.cs`: secret-free machine result.
- Create `src/WowVmMonitor.Infrastructure/Shares/ShareConnectionCoordinator.cs`: independent connection attempts for enabled machines.
- Create `src/WowVmMonitor.App/StartupRunner.cs`: confirmation gate and startup connection flow.
- Modify `src/WowVmMonitor.App/Program.cs`: load configuration, honor confirmation gate, and reconnect enabled shares.
- Modify `tests/WowVmMonitor.Tests/WowVmMonitor.Tests.csproj`: reference the App project for startup tests.
- Create configuration, credential, share, startup, and security tests under `tests/WowVmMonitor.Tests`.
- Modify `README.md`: document storage locations, recovery, migration, and credential behavior.

### Task 1: Define And Validate Secret-Free Version 1 Configuration

**Files:**
- Create: `src/WowVmMonitor.Core/Configuration/MonitorConfiguration.cs`
- Create: `src/WowVmMonitor.Core/Configuration/MonitoringConfiguration.cs`
- Create: `src/WowVmMonitor.Core/Configuration/MachineConfiguration.cs`
- Create: `src/WowVmMonitor.Core/Configuration/ConfigurationValidator.cs`
- Create: `tests/WowVmMonitor.Tests/Core/ConfigurationValidatorTests.cs`

- [ ] **Step 1: Write failing validation tests**

Create tests proving a valid eight-machine model passes and invalid count, duplicate IDs, invalid enabled UNC paths, forged credential targets, and invalid timing fail:

```csharp
using WowVmMonitor.Core.Configuration;

namespace WowVmMonitor.Tests.Core;

public sealed class ConfigurationValidatorTests
{
    [Fact]
    public void AcceptsEightValidMachines()
    {
        var configuration = CreateConfiguration(8);

        var errors = ConfigurationValidator.Validate(configuration);

        Assert.Empty(errors);
    }

    [Fact]
    public void RejectsSecretsAndInvalidMachineShapeByConstructionAndValidation()
    {
        var configuration = CreateConfiguration(9) with
        {
            Machines = CreateConfiguration(9).Machines
                .Select((machine, index) => index == 1
                    ? machine with { Id = "VM-01", SharePath = "C:\\logs", CredentialTarget = "wrong" }
                    : machine)
                .ToArray()
        };

        var errors = ConfigurationValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Code == "machines.maximum");
        Assert.Contains(errors, error => error.Code == "machines.id.duplicate");
        Assert.Contains(errors, error => error.Code == "machines.sharePath.unc");
        Assert.Contains(errors, error => error.Code == "machines.credentialTarget.invalid");
        Assert.DoesNotContain(typeof(MachineConfiguration).GetProperties(), property =>
            property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Username", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RejectsInvalidTimingOrder()
    {
        var configuration = CreateConfiguration(1) with
        {
            Monitoring = new MonitoringConfiguration(60, 10, 600, 300)
        };

        Assert.Contains(ConfigurationValidator.Validate(configuration),
            error => error.Code == "monitoring.thresholds.order");
    }

    private static MonitorConfiguration CreateConfiguration(int count) =>
        new(
            MonitorConfiguration.CurrentSchemaVersion,
            new MonitoringConfiguration(60, 10, 300, 600),
            Enumerable.Range(1, count)
                .Select(number => new MachineConfiguration(
                    $"vm-{number:D2}",
                    $"VM {number:D2}",
                    $@"\\192.168.1.{100 + number}\wowlogs",
                    true,
                    $"WowVmMonitor/share/vm-{number:D2}"))
                .ToArray());
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj --filter "FullyQualifiedName~ConfigurationValidatorTests"
```

Expected: compilation fails because the configuration types do not exist.

- [ ] **Step 3: Implement the models and validator**

Use immutable records:

```csharp
namespace WowVmMonitor.Core.Configuration;

public sealed record MonitorConfiguration(
    int SchemaVersion,
    MonitoringConfiguration Monitoring,
    IReadOnlyList<MachineConfiguration> Machines)
{
    public const int CurrentSchemaVersion = 1;

    public static MonitorConfiguration CreateDefault() =>
        new(CurrentSchemaVersion, new MonitoringConfiguration(60, 10, 300, 600), []);
}

public sealed record MonitoringConfiguration(
    int CheckIntervalSeconds,
    int CheckTimeoutSeconds,
    int WarningAfterSeconds,
    int AlertAfterSeconds);

public sealed record MachineConfiguration(
    string Id,
    string DisplayName,
    string SharePath,
    bool Enabled,
    string CredentialTarget)
{
    public static string CredentialTargetFor(string machineId) =>
        $"WowVmMonitor/share/{machineId}";
}

public sealed record ConfigurationValidationError(string Code, string Message);
```

Implement `ConfigurationValidator.Validate` with exact checks used by the tests: supported schema version, positive timing, warning less than alert, maximum eight machines, non-empty unique IDs/names, enabled UNC paths beginning with `\\`, and `CredentialTarget == MachineConfiguration.CredentialTargetFor(Id)` using ordinal comparison.

- [ ] **Step 4: Run focused tests and commit**

Expected: all validator tests pass.

```powershell
& 'C:\Program Files\Git\cmd\git.exe' add src/WowVmMonitor.Core/Configuration tests/WowVmMonitor.Tests/Core/ConfigurationValidatorTests.cs
& 'C:\Program Files\Git\cmd\git.exe' commit -m "feat: define validated monitor configuration"
```

### Task 2: Implement Versioned JSON And V0 Migration

**Files:**
- Create: `src/WowVmMonitor.Infrastructure/Configuration/ConfigurationMigrator.cs`
- Create: `tests/WowVmMonitor.Tests/Infrastructure/ConfigurationMigratorTests.cs`

- [ ] **Step 1: Write failing migration tests**

Test a v0 document without `schemaVersion`, a current v1 document, and a future version:

```csharp
[Fact]
public void MigratesUnversionedConfigurationToVersionOne()
{
    const string json = """
        {
          "checkIntervalSeconds": 60,
          "machines": [
            { "name": "VM One", "sharePath": "\\\\server\\wowlogs", "enabled": true }
          ]
        }
        """;

    var result = new ConfigurationMigrator().DeserializeAndMigrate(json);

    Assert.True(result.WasMigrated);
    Assert.Equal(1, result.Configuration.SchemaVersion);
    Assert.Equal("vm-one", result.Configuration.Machines[0].Id);
    Assert.Equal("WowVmMonitor/share/vm-one", result.Configuration.Machines[0].CredentialTarget);
}

[Fact]
public void RejectsUnknownFutureVersion()
{
    const string json = """{ "schemaVersion": 99, "monitoring": {}, "machines": [] }""";

    var exception = Assert.Throws<ConfigurationFormatException>(
        () => new ConfigurationMigrator().DeserializeAndMigrate(json));

    Assert.Equal("configuration.version.unsupported", exception.Code);
}
```

- [ ] **Step 2: Run and verify RED**

Run the `ConfigurationMigratorTests` filter. Expected: compilation fails because the migrator does not exist.

- [ ] **Step 3: Implement explicit migration**

Create these result and exception types in `ConfigurationMigrator.cs`:

```csharp
public sealed record ConfigurationMigrationResult(
    MonitorConfiguration Configuration,
    bool WasMigrated,
    int SourceVersion);

public sealed class ConfigurationFormatException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
}
```

Use `JsonDocument` to detect the root version. Missing version dispatches only to `MigrateV0`; version 1 deserializes with `JsonSerializerOptions { PropertyNameCaseInsensitive = true }`; any other version throws `configuration.version.unsupported`. `MigrateV0` slugifies the old name to lowercase ASCII letters, digits, and hyphens; if no ASCII characters remain it assigns sequential IDs beginning with `vm-01`. Resolve collisions with numeric suffixes, apply 10/300/600 defaults, derive credential targets, then validate the v1 result.

- [ ] **Step 4: Run migration and validator tests, then commit**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj --filter "FullyQualifiedName~Configuration"
& 'C:\Program Files\Git\cmd\git.exe' add src/WowVmMonitor.Infrastructure/Configuration/ConfigurationMigrator.cs tests/WowVmMonitor.Tests/Infrastructure/ConfigurationMigratorTests.cs
& 'C:\Program Files\Git\cmd\git.exe' commit -m "feat: migrate versioned json configuration"
```

### Task 3: Add Atomic Save, Backup Recovery, And Confirmation Gate

**Files:**
- Create: `src/WowVmMonitor.Core/Configuration/ConfigurationLoadResult.cs`
- Create: `src/WowVmMonitor.Infrastructure/Configuration/IConfigurationFileOperations.cs`
- Create: `src/WowVmMonitor.Infrastructure/Configuration/SystemConfigurationFileOperations.cs`
- Create: `src/WowVmMonitor.Infrastructure/Configuration/ConfigurationStore.cs`
- Create: `tests/WowVmMonitor.Tests/Infrastructure/ConfigurationStoreTests.cs`

- [ ] **Step 1: Write failing recovery tests**

Using a unique temporary directory per test, cover valid round-trip, corrupt-primary recovery, both-files-corrupt confirmation, and injected replacement failure:

```csharp
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
```

- [ ] **Step 2: Run and verify RED**

Run the `ConfigurationStoreTests` filter. Expected: compilation fails because the store and load result do not exist.

- [ ] **Step 3: Implement the load result and file boundary**

```csharp
namespace WowVmMonitor.Core.Configuration;

public sealed record ConfigurationMessage(string Code, string Message);

public sealed record ConfigurationLoadResult(
    MonitorConfiguration Configuration,
    bool RequiresUserConfirmation,
    IReadOnlyList<ConfigurationMessage> Messages);
```

`IConfigurationFileOperations` must expose `Exists`, `ReadAllText`, `WriteAllTextAndFlush`, `Move`, `Replace`, `Delete`, `CreateDirectory`, and `UtcNow`. The production implementation writes UTF-8 without BOM, calls `FileStream.Flush(flushToDisk: true)`, uses `File.Replace(temp, primary, backup)` for replacement, and `File.Move(temp, primary)` for the first save.

- [ ] **Step 4: Implement store behavior**

`ConfigurationStore` accepts a configuration directory, migrator, and file-operations dependency. `Save` serializes with indented camel-case JSON, writes `config.json.tmp`, reads and migrates it, validates the round trip, then replaces or moves. A `finally` block deletes a leftover temp file.

`Load` tries primary, moves an invalid file to `config.corrupt-yyyyMMddTHHmmssfffZ.json`, then tries backup. A valid backup is saved as the primary and returns `configuration.recovered.backup`. If both fail, it saves `MonitorConfiguration.CreateDefault()` and returns `RequiresUserConfirmation = true` with `configuration.default.confirmationRequired`. A successful v0 migration saves the v1 model and returns `configuration.migrated.v0-v1`.

- [ ] **Step 5: Run store tests and commit**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj --filter "FullyQualifiedName~ConfigurationStoreTests"
& 'C:\Program Files\Git\cmd\git.exe' add src/WowVmMonitor.Core/Configuration/ConfigurationLoadResult.cs src/WowVmMonitor.Infrastructure/Configuration tests/WowVmMonitor.Tests/Infrastructure/ConfigurationStoreTests.cs
& 'C:\Program Files\Git\cmd\git.exe' commit -m "feat: recover and atomically save configuration"
```

### Task 4: Store Independent Credentials In Windows Credential Manager

**Files:**
- Create: `src/WowVmMonitor.Infrastructure/Credentials/ShareCredential.cs`
- Create: `src/WowVmMonitor.Infrastructure/Credentials/ICredentialNativeApi.cs`
- Create: `src/WowVmMonitor.Infrastructure/Credentials/WindowsCredentialNativeApi.cs`
- Create: `src/WowVmMonitor.Infrastructure/Credentials/WindowsCredentialStore.cs`
- Create: `tests/WowVmMonitor.Tests/Infrastructure/WindowsCredentialStoreTests.cs`

- [ ] **Step 1: Write failing store tests**

Use an in-memory native adapter for unit tests and a unique real target for one Windows integration test:

```csharp
[Fact]
public void EightTargetsRemainIndependent()
{
    var native = new InMemoryCredentialNativeApi();
    var store = new WindowsCredentialStore(native);

    for (var number = 1; number <= 8; number++)
    {
        store.Save($"vm-{number:D2}", $"user-{number}", $"secret-{number}".ToCharArray());
    }

    using var changed = store.Read("vm-01");
    store.Delete("vm-02");

    Assert.Equal("user-1", changed!.Username);
    Assert.True(changed.Password.Span.SequenceEqual("secret-1".AsSpan()));
    Assert.Null(store.Read("vm-02"));
    using var eighth = store.Read("vm-08");
    Assert.NotNull(eighth);
}

[Fact]
public void NewStoreInstanceReadsCredentialPersistedByWindows()
{
    if (!OperatingSystem.IsWindows()) return;
    var machineId = $"WowVmMonitor.Tests-{Guid.NewGuid():N}";
    var first = new WindowsCredentialStore(new WindowsCredentialNativeApi(), "WowVmMonitor.Tests/share/");
    var second = new WindowsCredentialStore(new WindowsCredentialNativeApi(), "WowVmMonitor.Tests/share/");
    try
    {
        first.Save(machineId, "restart-user", "restart-secret".ToCharArray());
        using var loaded = second.Read(machineId);
        Assert.Equal("restart-user", loaded!.Username);
        Assert.True(loaded.Password.Span.SequenceEqual("restart-secret".AsSpan()));
    }
    finally
    {
        first.Delete(machineId);
    }
}
```

- [ ] **Step 2: Run and verify RED**

Run the `WindowsCredentialStoreTests` filter. Expected: compilation fails because credential types do not exist.

- [ ] **Step 3: Implement disposable secret ownership**

`ShareCredential` owns a private `char[]`, exposes `Username` and `ReadOnlyMemory<char> Password`, returns a redacted `ToString()`, and clears the owned array with `Array.Clear` in `Dispose`. Unit tests compare spans with `SequenceEqual` so a failure never prints the secret, and assert disposal clears the array through the native adapter boundary.

- [ ] **Step 4: Implement native Credential Manager adapter**

`WindowsCredentialNativeApi` uses Unicode P/Invoke definitions for `CredWriteW`, `CredReadW`, `CredDeleteW`, and `CredFree`. Save password bytes as UTF-16 in `CredentialBlob`, use `CRED_TYPE_GENERIC`, and set `Persist = CRED_PERSIST_LOCAL_MACHINE`, which persists for the current user across restart. Always free unmanaged allocations in `finally`; translate `ERROR_NOT_FOUND` to a missing result and other failures to `CredentialStoreException` containing only operation, target, and Win32 code.

- [ ] **Step 5: Implement validated machine store and run tests**

`WindowsCredentialStore.Save/Read/Delete` derives the target from a configurable prefix plus machine ID. It rejects blank IDs, usernames, empty passwords, IDs containing path separators, and passwords exceeding Credential Manager limits. It never accepts a caller-provided arbitrary target.

Run unit and real Windows integration tests, then commit:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj --filter "FullyQualifiedName~WindowsCredentialStoreTests"
& 'C:\Program Files\Git\cmd\git.exe' add src/WowVmMonitor.Infrastructure/Credentials tests/WowVmMonitor.Tests/Infrastructure/WindowsCredentialStoreTests.cs
& 'C:\Program Files\Git\cmd\git.exe' commit -m "feat: persist per-vm windows credentials"
```

### Task 5: Reconnect Enabled Shares Independently

**Files:**
- Create: `src/WowVmMonitor.Infrastructure/Shares/IWindowsNetworkApi.cs`
- Create: `src/WowVmMonitor.Infrastructure/Shares/IShareConnectionCoordinator.cs`
- Create: `src/WowVmMonitor.Infrastructure/Shares/WindowsNetworkApi.cs`
- Create: `src/WowVmMonitor.Infrastructure/Shares/ShareConnectionResult.cs`
- Create: `src/WowVmMonitor.Infrastructure/Shares/ShareConnectionCoordinator.cs`
- Create: `tests/WowVmMonitor.Tests/Infrastructure/ShareConnectionCoordinatorTests.cs`

- [ ] **Step 1: Write failing isolation test**

```csharp
[Fact]
public async Task OneAuthenticationFailureDoesNotStopOtherSevenConnections()
{
    var credentials = new StubCredentialStore();
    var network = new StubWindowsNetworkApi(failingMachineId: "vm-01", errorCode: 1326);
    var coordinator = new ShareConnectionCoordinator(credentials, network);
    var configuration = CreateEightMachineConfiguration();

    var results = await coordinator.ConnectEnabledAsync(configuration, CancellationToken.None);

    Assert.Equal(8, results.Count);
    Assert.False(results.Single(result => result.MachineId == "vm-01").Succeeded);
    Assert.All(results.Where(result => result.MachineId != "vm-01"), result => Assert.True(result.Succeeded));
    Assert.DoesNotContain("test-password", JsonSerializer.Serialize(results));
}
```

- [ ] **Step 2: Run and verify RED**

Run the `ShareConnectionCoordinatorTests` filter. Expected: compilation fails because the share types do not exist.

- [ ] **Step 3: Implement network adapter and result**

```csharp
public sealed record ShareConnectionResult(
    string MachineId,
    string SharePath,
    bool Succeeded,
    string Code,
    int? Win32ErrorCode);
```

`IWindowsNetworkApi.Connect` accepts a UNC path, username, and `ReadOnlySpan<char>` password. `IShareConnectionCoordinator.ConnectEnabledAsync` accepts `MonitorConfiguration` and cancellation and returns `IReadOnlyList<ShareConnectionResult>`. `WindowsNetworkApi` creates a `NETRESOURCE` with `RESOURCETYPE_DISK` and calls `WNetAddConnection2W`. Return success for code 0 and an existing matching connection. Never format the username or password into an exception.

- [ ] **Step 4: Implement concurrent coordinator**

For each enabled machine, start an independent task that reads its credential, calls the network API, disposes the credential in `finally`, and returns a machine-scoped result. Use `Task.WhenAll`; catch failures inside each task so one failure cannot fault the aggregate. Missing credentials return `credential.missing`; Win32 failures return `share.connect.failed` with numeric code only.

- [ ] **Step 5: Run share tests and commit**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj --filter "FullyQualifiedName~ShareConnectionCoordinatorTests"
& 'C:\Program Files\Git\cmd\git.exe' add src/WowVmMonitor.Infrastructure/Shares tests/WowVmMonitor.Tests/Infrastructure/ShareConnectionCoordinatorTests.cs
& 'C:\Program Files\Git\cmd\git.exe' commit -m "feat: reconnect configured windows shares"
```

### Task 6: Wire Startup And Add Secret Regression Coverage

**Files:**
- Create: `src/WowVmMonitor.App/StartupRunner.cs`
- Modify: `src/WowVmMonitor.App/Program.cs`
- Modify: `tests/WowVmMonitor.Tests/WowVmMonitor.Tests.csproj`
- Create: `tests/WowVmMonitor.Tests/Security/SecretLeakageTests.cs`
- Create: `tests/WowVmMonitor.Tests/App/StartupConfigurationTests.cs`
- Modify: `README.md`

- [ ] **Step 1: Write failing startup and leakage tests**

Test that confirmation-required configuration prevents connector calls, valid configuration reconnects enabled machines, and serialized public objects contain neither a unique username nor password:

```csharp
[Fact]
public async Task ConfirmationRequiredConfigurationDoesNotConnectShares()
{
    var connector = new RecordingShareConnector();
    var result = new ConfigurationLoadResult(
        MonitorConfiguration.CreateDefault(),
        true,
        [new ConfigurationMessage("configuration.default.confirmationRequired", "Review configuration.")]);

    var exitCode = await StartupRunner.RunAsync(result, connector, TextWriter.Null, CancellationToken.None);

    Assert.Equal(2, exitCode);
    Assert.Equal(0, connector.CallCount);
}

[Fact]
public void PublicConfigurationAndErrorsNeverContainCredentialValues()
{
    const string username = "unique-user-security-test";
    const string password = "unique-password-security-test";
    var configurationJson = JsonSerializer.Serialize(CreateConfiguration());
    var errorJson = JsonSerializer.Serialize(new ShareConnectionResult("vm-01", @"\\server\share", false, "share.connect.failed", 1326));

    Assert.DoesNotContain(username, configurationJson + errorJson, StringComparison.Ordinal);
    Assert.DoesNotContain(password, configurationJson + errorJson, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run and verify RED**

Run the `StartupConfigurationTests|SecretLeakageTests` filters. Expected: compilation fails because `StartupRunner` does not exist.

- [ ] **Step 3: Implement startup runner and Program wiring**

Create a small `StartupRunner` in the App project that accepts `ConfigurationLoadResult`, an `IShareConnectionCoordinator`, a `TextWriter`, and cancellation. It writes only structured user messages, returns exit code 2 without connecting when confirmation is required, otherwise connects enabled shares and returns 0 when every enabled share succeeds or 1 when any fail.

`Program.cs` resolves `%LocalAppData%\WowVmMonitor`, loads configuration, creates native credential and network adapters, calls the runner, and sets `Environment.ExitCode`. It never prints exception objects from credential operations; it prints the secret-free result code and Win32 number.

Add this reference to `tests/WowVmMonitor.Tests/WowVmMonitor.Tests.csproj`:

```xml
<ProjectReference Include="..\..\src\WowVmMonitor.App\WowVmMonitor.App.csproj" />
```

- [ ] **Step 4: Update README**

Document:

```markdown
## Configuration and credentials

- Configuration: `%LocalAppData%\WowVmMonitor\config.json`
- Backup: `%LocalAppData%\WowVmMonitor\config.json.bak`
- Credentials: Windows Credential Manager targets named `WowVmMonitor/share/<machine-id>`
- Passwords and usernames are never stored in JSON or emitted in diagnostics.
- A valid backup is restored automatically. If both files are invalid, monitoring remains disabled until the generated default is reviewed.
- Starting WowVmMonitor after a Windows restart reloads saved credentials and reconnects enabled shares; the app does not register itself for automatic startup.
```

- [ ] **Step 5: Run complete verification**

```powershell
powershell -ExecutionPolicy Bypass -File .\build-and-test.ps1
```

Expected: Release build succeeds with zero warnings and zero errors, all tests pass, and all test Credential Manager targets are deleted.

- [ ] **Step 6: Commit startup and documentation**

```powershell
& 'C:\Program Files\Git\cmd\git.exe' add src/WowVmMonitor.App tests/WowVmMonitor.Tests/WowVmMonitor.Tests.csproj tests/WowVmMonitor.Tests/App tests/WowVmMonitor.Tests/Security README.md
& 'C:\Program Files\Git\cmd\git.exe' commit -m "feat: load credentials and reconnect shares at startup"
```

## Final Verification Checklist

- [ ] JSON contains no username or password properties or values.
- [ ] Main and backup corruption behavior is explicit and tested.
- [ ] Version 0 upgrades to version 1; future versions are rejected.
- [ ] Atomic replacement failure preserves the last valid primary.
- [ ] Eight credential targets remain independent.
- [ ] A new store instance reads credentials saved before it was constructed.
- [ ] One authentication failure does not stop seven other connections.
- [ ] Confirmation-required defaults cannot start monitoring or connect shares.
- [ ] Native buffers and test credentials are cleaned up in `finally` blocks.
- [ ] Release build reports zero warnings and zero errors.
- [ ] All automated tests pass with zero failures.
