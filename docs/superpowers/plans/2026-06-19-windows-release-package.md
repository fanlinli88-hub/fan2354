# Windows Release Package Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a signed self-contained x64 single-file application and a signed per-user Inno Setup installer with uninstall and in-place upgrade support.

**Architecture:** A PowerShell release pipeline runs tests, publishes the WPF project as one self-contained EXE, signs it with the existing local certificate, and invokes Inno Setup. Inno Setup uses a fixed AppId and its SignTool integration so both setup and generated uninstaller satisfy Windows Code Integrity; user configuration and credentials remain outside the installation directory.

**Tech Stack:** .NET 8, WPF, PowerShell 7/Windows PowerShell, Authenticode, Inno Setup 6, xUnit

---

### Task 1: Reusable Release Signing

**Files:**
- Create: `release/Sign-ReleaseFile.ps1`
- Modify: `tools/Sign-LocalBuild.ps1`
- Create: `release/Test-ReleaseSigning.ps1`

- [ ] **Step 1: Write the failing signing check**

Create `release/Test-ReleaseSigning.ps1` so it copies an unsigned WowVmMonitor DLL to a temporary directory, calls `Sign-ReleaseFile.ps1`, and asserts:

```powershell
$signature = Get-AuthenticodeSignature -LiteralPath $copy
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Expected a valid Authenticode signature, got $($signature.Status)."
}
```

- [ ] **Step 2: Run the check and verify RED**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\release\Test-ReleaseSigning.ps1
```

Expected: FAIL because `Sign-ReleaseFile.ps1` does not exist.

- [ ] **Step 3: Implement one-file Authenticode signing**

`Sign-ReleaseFile.ps1` accepts a mandatory literal file path, finds the newest valid `CN=WowVmMonitor Local Development` code-signing certificate, signs SHA-256, and throws unless both `Set-AuthenticodeSignature` and a fresh `Get-AuthenticodeSignature` return `Valid`.

Update `tools/Sign-LocalBuild.ps1` to call the helper for every `WowVmMonitor.*.dll` or `.exe` under both `net8.0` and `net8.0-windows` output paths. Use this target framework pattern:

```powershell
$outputPattern = "\\bin\\$([regex]::Escape($Configuration))\\net8\.0(?:-windows)?\\"
```

- [ ] **Step 4: Run signing checks**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\release\Test-ReleaseSigning.ps1
powershell -ExecutionPolicy Bypass -File .\tools\Sign-LocalBuild.ps1 -Configuration Release
```

Expected: PASS; Desktop EXE and DLL signatures report `Valid`.

- [ ] **Step 5: Commit**

```powershell
git add release/Sign-ReleaseFile.ps1 release/Test-ReleaseSigning.ps1 tools/Sign-LocalBuild.ps1
git commit -m "build: sign Windows desktop release outputs"
```

### Task 2: Self-Contained Single-File Publish

**Files:**
- Create: `release/Publish-SingleFile.ps1`
- Create: `release/Test-SingleFilePublish.ps1`
- Modify: `src/WowVmMonitor.Desktop/WowVmMonitor.Desktop.csproj`
- Modify: `.gitignore`

- [ ] **Step 1: Write the failing publish contract check**

Create `Test-SingleFilePublish.ps1` to invoke `Publish-SingleFile.ps1 -Version 1.0.0 -OutputDirectory <temp>`, then require exactly one file named `WowVmMonitor.exe`, no DLL/PDB/runtimeconfig files, version `1.0.0.0`, and a `Valid` signature.

- [ ] **Step 2: Run the check and verify RED**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\release\Test-SingleFilePublish.ps1
```

Expected: FAIL because the publish script does not exist.

- [ ] **Step 3: Add stable product metadata**

Set these defaults in the Desktop project while allowing command-line overrides:

```xml
<Version Condition="'$(Version)' == ''">1.0.0</Version>
<AssemblyVersion Condition="'$(AssemblyVersion)' == ''">1.0.0.0</AssemblyVersion>
<FileVersion Condition="'$(FileVersion)' == ''">1.0.0.0</FileVersion>
<Product>WowVmMonitor</Product>
<Company>WowVmMonitor</Company>
```

- [ ] **Step 4: Implement deterministic publish**

Validate `Version` against `^\d+\.\d+\.\d+$`, clear only the requested output directory, and run:

```powershell
dotnet publish .\src\WowVmMonitor.Desktop\WowVmMonitor.Desktop.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false `
  -p:Version=$Version -p:AssemblyVersion="$Version.0" -p:FileVersion="$Version.0" `
  -o $OutputDirectory
```

Reject any output other than `WowVmMonitor.exe`, sign it with `Sign-ReleaseFile.ps1`, then print its SHA-256.

- [ ] **Step 5: Ignore generated release artifacts and verify**

Add `artifacts/release/` to `.gitignore`, run the publish contract check, and launch the signed EXE for five seconds. Confirm there are no new Code Integrity 3033/3077 events for the published path, then stop only the launched process.

- [ ] **Step 6: Commit**

```powershell
git add .gitignore release src/WowVmMonitor.Desktop/WowVmMonitor.Desktop.csproj
git commit -m "build: publish signed single-file desktop app"
```

### Task 3: Inno Setup Installer And Uninstaller

**Files:**
- Create: `release/WowVmMonitor.iss`
- Create: `release/Build-Installer.ps1`
- Create: `release/Test-InstallerMetadata.ps1`

- [ ] **Step 1: Install the approved installer compiler**

Run:

```powershell
winget install --id JRSoftware.InnoSetup --exact --source winget `
  --accept-package-agreements --accept-source-agreements --silent
```

Verify `ISCC.exe` exists under an Inno Setup installation directory.

- [ ] **Step 2: Write the failing installer metadata check**

The check requires a fixed AppId, `PrivilegesRequired=lowest`, `{localappdata}\Programs\WowVmMonitor`, x64 architecture, `CloseApplications=yes`, `SignedUninstaller=yes`, a desktop-icon task, Start Menu shortcut, and no deletion of `%LocalAppData%\WowVmMonitor`.

- [ ] **Step 3: Run the metadata check and verify RED**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\release\Test-InstallerMetadata.ps1
```

Expected: FAIL because `WowVmMonitor.iss` does not exist.

- [ ] **Step 4: Implement the Inno Setup definition**

Use a stable GUID AppId, preprocessor inputs `MyAppVersion`, `SourceExe`, and `OutputDir`, and these core settings:

```ini
[Setup]
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\WowVmMonitor
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
SignedUninstaller=yes
SignTool=local
UsePreviousAppDir=yes
```

Include only the signed `WowVmMonitor.exe`. Add optional desktop and required Start Menu shortcuts, post-install launch, and Chinese wizard language. Do not add any uninstall deletion entry for the configuration directory.

- [ ] **Step 5: Implement installer build and Inno SignTool wiring**

`Build-Installer.ps1` calls `Publish-SingleFile.ps1`, locates `ISCC.exe`, and supplies an Inno `/Slocal=` command that invokes `release/Sign-ReleaseFile.ps1` for Inno's `$f` placeholder. Build `WowVmMonitor-Setup-<version>.exe`, verify its version/hash/signature, and reject extra setup outputs.

- [ ] **Step 6: Run metadata and installer builds**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\release\Test-InstallerMetadata.ps1
powershell -ExecutionPolicy Bypass -File .\release\Build-Installer.ps1 -Version 1.0.0
```

Expected: setup EXE exists and reports a `Valid` signature.

- [ ] **Step 7: Commit**

```powershell
git add release
git commit -m "build: add signed per-user Windows installer"
```

### Task 4: Full Release Pipeline And Upgrade Verification

**Files:**
- Create: `release/Build-Release.ps1`
- Create: `release/Test-InstallUpgrade.ps1`
- Modify: `README.md`

- [ ] **Step 1: Write the failing end-to-end release check**

`Test-InstallUpgrade.ps1` creates a unique sentinel under `%LocalAppData%\WowVmMonitor`, silently installs `1.0.0`, verifies the installed EXE and uninstall registration, silently upgrades with a `1.0.1` test installer, verifies the installed file version changed, silently uninstalls, then verifies application files are removed while the sentinel remains. The `finally` block deletes only its own sentinel.

- [ ] **Step 2: Implement the release orchestrator**

`Build-Release.ps1 -Version <semver>` must:

1. Run Release build and both test projects.
2. Call `Build-Installer.ps1`.
3. Copy the signed single-file EXE and signed setup EXE into `artifacts/release/<version>`.
4. Emit a `SHA256SUMS.txt` containing both hashes.
5. Print file paths, sizes, product versions, and signature states.

- [ ] **Step 3: Run end-to-end install, upgrade, and uninstall verification**

Build `1.0.0` and `1.0.1`, then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\release\Test-InstallUpgrade.ps1 `
  -VersionOneInstaller .\artifacts\release\1.0.0\WowVmMonitor-Setup-1.0.0.exe `
  -VersionTwoInstaller .\artifacts\release\1.0.1\WowVmMonitor-Setup-1.0.1.exe
```

Expected: install, upgrade, and uninstall all succeed; configuration sentinel remains.

- [ ] **Step 4: Build the final 1.0.0 package**

After upgrade testing, rerun:

```powershell
powershell -ExecutionPolicy Bypass -File .\release\Build-Release.ps1 -Version 1.0.0
```

Verify the final directory contains exactly `WowVmMonitor.exe`, `WowVmMonitor-Setup-1.0.0.exe`, and `SHA256SUMS.txt`.

- [ ] **Step 5: Update documentation**

Document the release command, artifact locations, direct EXE usage, install/uninstall behavior, upgrade procedure, preserved data, and the limitation that the local certificate is trusted only on this computer.

- [ ] **Step 6: Final verification and commit**

Run:

```powershell
dotnet build .\WowVmMonitor.sln -c Release --no-restore
dotnet test .\tests\WowVmMonitor.Tests\WowVmMonitor.Tests.csproj -c Release --no-build
dotnet test .\tests\WowVmMonitor.Desktop.Tests\WowVmMonitor.Desktop.Tests.csproj -c Release --no-build
git diff --check
```

Expected: zero warnings, zero errors, all tests pass, and diff check has no output.

```powershell
git add release README.md .gitignore src/WowVmMonitor.Desktop/WowVmMonitor.Desktop.csproj
git commit -m "build: automate Windows release packaging"
```
