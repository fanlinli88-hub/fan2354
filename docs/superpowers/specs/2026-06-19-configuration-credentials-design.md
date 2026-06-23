# Configuration And Credentials Design

## Goal

Persist versioned WowVmMonitor configuration as JSON while storing each virtual machine's independent share username and password in Windows Credential Manager. After Windows restarts, manually starting WowVmMonitor must reload credentials and reconnect enabled UNC shares without requiring passwords to be entered again.

## Scope

This increment implements configuration persistence, schema migration, corruption recovery, Windows credential storage, and authenticated UNC connection setup. It does not add Windows startup registration, a configuration UI, notifications, or history persistence.

## Architecture

The feature has three independent boundaries:

- `ConfigurationStore` reads, validates, migrates, backs up, restores, and atomically writes JSON configuration.
- `WindowsCredentialStore` stores one Generic Credential per machine through the native Windows Credential Manager API.
- `WindowsShareConnector` reads a machine credential and establishes its UNC session through the native Windows networking API.

The application layer coordinates these boundaries. Configuration types never contain a username or password. Credential types are passed only to the credential and share-connection boundaries.

## Configuration Schema

Version 1 uses a root schema version, global monitoring settings, and no more than eight machine records:

```json
{
  "schemaVersion": 1,
  "monitoring": {
    "checkIntervalSeconds": 60,
    "checkTimeoutSeconds": 10,
    "warningAfterSeconds": 300,
    "alertAfterSeconds": 600
  },
  "machines": [
    {
      "id": "vm-01",
      "displayName": "VM 01",
      "sharePath": "\\\\192.168.1.101\\wowlogs",
      "enabled": true,
      "credentialTarget": "WowVmMonitor/share/vm-01"
    }
  ]
}
```

Validation rules are:

- `schemaVersion` must be a supported version.
- There may be no more than eight machines.
- Machine IDs and display names must be non-empty and unique, case-insensitively.
- Enabled machines must have a valid UNC path and the exact credential target derived from their ID.
- The check interval and timeout must be positive.
- Warning and alert thresholds must remain ordered and positive.
- Unknown JSON properties are ignored for forward-compatible minor additions, but an unknown future schema version is rejected.

The configuration location is `%LocalAppData%\WowVmMonitor\config.json`. The store creates the directory when first saving.

## Schema Migration

JSON without `schemaVersion` is version 0. Version 0 is migrated to version 1 by normalizing the old monitoring settings, assigning a stable machine ID when one is absent, and deriving `credentialTarget` from that ID.

Migration is sequential. Future migrations must implement one explicit step at a time, such as v1 to v2 and then v2 to v3. The store never skips steps, guesses future fields, or silently downgrades a newer configuration.

Before writing a migrated configuration, the original input is preserved as the backup. The migrated model is validated before it becomes the active configuration.

## Atomic Save And Recovery

Saving follows this sequence:

1. Serialize to a temporary file in the configuration directory.
2. Flush file contents to disk.
3. Read the temporary file back with the production deserializer.
4. Validate the round-tripped model.
5. Atomically replace `config.json`, preserving the previous valid file as `config.json.bak`.
6. For the first save, atomically move the validated temporary file into place.
7. Remove a leftover temporary file when an operation fails.

Loading follows this sequence:

1. Load, migrate if necessary, and validate `config.json`.
2. If the primary file is invalid, preserve it as `config.corrupt-<UTC timestamp>.json`.
3. Load, migrate if necessary, and validate `config.json.bak`.
4. If the backup is valid, restore it as the primary file and return a visible recovery warning.
5. If the backup is also invalid or absent, preserve it with a corrupt timestamp, write a default configuration, and return `RequiresUserConfirmation = true`.

Monitoring may not start while `RequiresUserConfirmation` is true. This prevents a silently generated default path from producing false status reports. Parse, migration, validation, recovery, and file-system failures return a structured load result with a user-facing message and a non-secret diagnostic code.

## Credential Storage

Each machine uses a Windows Generic Credential with this deterministic target:

```text
WowVmMonitor/share/{machineId}
```

The credential contains both the share username and password. Neither value appears in JSON. Credential operations use native `CredWriteW`, `CredReadW`, `CredDeleteW`, and `CredFree` calls with user-level persistence suitable for surviving a Windows restart.

The store API supports save, read, and delete operations. Updating one target replaces only that target. Deleting one machine's credential cannot affect another machine. Native buffers are copied only for the duration required to establish the connection and are released immediately afterward.

## Share Connection

Before starting an enabled machine monitor, the application:

1. Loads the machine configuration.
2. Derives and validates its credential target.
3. Reads the username and password from Windows Credential Manager.
4. Calls `WNetAddConnection2W` for that machine's UNC path.
5. Releases the in-memory credential values.
6. Starts that machine monitor only when its connection succeeds or Windows reports that the matching connection already exists.

Connections are attempted independently. Missing credentials, invalid credentials, an unavailable VM, or a networking failure returns a machine-scoped result and does not block the other seven machines.

After Windows restarts, the user starts WowVmMonitor manually. The same startup flow reloads persisted credentials and reconnects all enabled shares. This feature does not register WowVmMonitor for automatic Windows startup.

## Secret Handling

Passwords are prohibited from:

- JSON configuration and backups;
- corrupt configuration copies;
- log events and exception messages;
- monitoring results and history records;
- notification bodies;
- object `ToString()` output;
- test failure messages and snapshots.

Credential and share connector errors expose the machine ID, UNC path, operation name, and Win32 error code only. Native error messages are generated from the error code and never concatenate credentials. Usernames are treated as credential data and omitted from routine diagnostics as well.

Credential values use the narrowest possible lifetime. Managed buffers are cleared in a `finally` block where practical, and native allocations are always released through a safe ownership wrapper.

## Test Strategy

Configuration tests cover:

- version 1 save and load;
- first save and replacement save;
- failure before atomic replacement leaves the previous primary readable;
- a corrupt primary restores a valid backup and returns a warning;
- corrupt primary and backup files are preserved and produce a confirmation-required default;
- version 0 migrates to version 1 and preserves the original;
- an unknown future version is rejected;
- invalid duplicate IDs, invalid UNC paths, and more than eight machines are rejected.

Credential tests cover:

- eight independent credential targets;
- save, read, update, and delete behavior;
- changing or deleting one target does not affect the other seven;
- a fresh `WindowsCredentialStore` instance reads an existing test credential, simulating application restart;
- test targets use a unique `WowVmMonitor.Tests/` prefix and are deleted in `finally` blocks.

Share connector tests use an injectable native adapter and cover:

- the correct target and UNC path are used;
- an already-established matching connection is successful;
- one authentication failure does not stop seven successful connections;
- credentials are released after success, failure, and cancellation;
- structured errors contain no username or password.

Security regression tests serialize configuration, backups, structured results, exceptions, and captured logs, then assert that unique test usernames and passwords are absent.

## Acceptance Criteria

- Configuration is stored as versioned JSON under the current user's local application data.
- A corrupt primary automatically restores a valid backup with a clear warning.
- If both primary and backup are invalid, both are preserved, a default is generated, and monitoring remains disabled until user confirmation.
- Version 0 configuration upgrades to version 1 without losing supported settings.
- Each of eight machines has an independent Windows Credential Manager target.
- No username or password enters JSON, logs, error reports, monitoring results, or notifications.
- After Windows restarts, manually starting WowVmMonitor reconnects enabled shares without password re-entry.
- One credential or share failure does not prevent the other seven machines from connecting and monitoring.
- Release build completes with zero warnings and zero errors, and all automated tests pass.
