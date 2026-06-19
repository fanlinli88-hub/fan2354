param(
    [Parameter(Mandatory = $true)]
    [string]$VersionOneInstaller,

    [Parameter(Mandatory = $true)]
    [string]$VersionTwoInstaller
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$firstInstaller = (Resolve-Path -LiteralPath $VersionOneInstaller).Path
$secondInstaller = (Resolve-Path -LiteralPath $VersionTwoInstaller).Path
$installDirectory = Join-Path $env:LOCALAPPDATA "Programs\WowVmMonitor"
$installedApplication = Join-Path $installDirectory "WowVmMonitor.exe"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{C7AF91D3-2598-4BB7-BEC0-3712BE565857}_is1"
if (Test-Path -LiteralPath $uninstallKey) {
    throw "An existing WowVmMonitor installation was found. Uninstall it before running the upgrade test."
}

$configurationDirectory = Join-Path $env:LOCALAPPDATA "WowVmMonitor"
New-Item -ItemType Directory -Path $configurationDirectory -Force | Out-Null
$sentinel = Join-Path $configurationDirectory ("release-sentinel-" + [guid]::NewGuid().ToString("N") + ".txt")
Set-Content -LiteralPath $sentinel -Value "preserve" -NoNewline

function Invoke-Installer([string]$Path) {
    $process = Start-Process -FilePath $Path -ArgumentList @(
        "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/TASKS=!desktopicon"
    ) -PassThru -Wait
    if ($process.ExitCode -ne 0) {
        throw "Installer failed with exit code $($process.ExitCode): $Path"
    }
}

try {
    Invoke-Installer $firstInstaller
    if (-not (Test-Path -LiteralPath $installedApplication)) {
        throw "Installed application was not found."
    }
    if ((Get-Item -LiteralPath $installedApplication).VersionInfo.FileVersion -ne "1.0.0.0") {
        throw "Version 1.0.0 was not installed."
    }
    if (-not (Test-Path -LiteralPath $uninstallKey)) {
        throw "Uninstall registration was not created."
    }

    Invoke-Installer $secondInstaller
    if ((Get-Item -LiteralPath $installedApplication).VersionInfo.FileVersion -ne "1.0.1.0") {
        throw "Version 1.0.1 did not replace version 1.0.0."
    }
    if (-not (Test-Path -LiteralPath $sentinel)) {
        throw "User configuration was removed during upgrade."
    }

    $uninstall = Get-ItemProperty -LiteralPath $uninstallKey
    $uninstaller = $uninstall.UninstallString.Trim('"')
    $signature = Get-AuthenticodeSignature -LiteralPath $uninstaller
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Uninstaller signature is invalid: $($signature.Status)"
    }

    $process = Start-Process -FilePath $uninstaller -ArgumentList @(
        "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART"
    ) -PassThru -Wait
    if ($process.ExitCode -ne 0) {
        throw "Uninstaller failed with exit code $($process.ExitCode)."
    }
    if (Test-Path -LiteralPath $installedApplication) {
        throw "Application remained after uninstall."
    }
    if (-not (Test-Path -LiteralPath $sentinel)) {
        throw "User configuration was removed during uninstall."
    }
}
finally {
    if (Test-Path -LiteralPath $uninstallKey) {
        try {
            $cleanupUninstall = (Get-ItemProperty -LiteralPath $uninstallKey).UninstallString.Trim('"')
            Start-Process -FilePath $cleanupUninstall -ArgumentList @(
                "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART"
            ) -Wait
        }
        catch {
            Write-Warning "Test installation cleanup failed: $($_.Exception.Message)"
        }
    }
    Remove-Item -LiteralPath $sentinel -Force -ErrorAction SilentlyContinue
}

Write-Host "Install, upgrade, and uninstall check passed."
