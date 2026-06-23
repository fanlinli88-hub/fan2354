param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\release\$Version"
}

$fullOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
$root = [System.IO.Path]::GetPathRoot($fullOutput)
if ($fullOutput -eq $root -or $fullOutput -eq [System.IO.Path]::GetFullPath($repositoryRoot)) {
    throw "Unsafe output directory: $fullOutput"
}

if (Test-Path -LiteralPath $fullOutput) {
    Remove-Item -LiteralPath $fullOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $fullOutput -Force | Out-Null

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("WowVmMonitor-installer-" + [guid]::NewGuid().ToString("N"))
try {
    & (Join-Path $PSScriptRoot "Publish-SingleFile.ps1") `
        -Version $Version `
        -OutputDirectory $staging

    $application = Join-Path $staging "WowVmMonitor.exe"
    Copy-Item -LiteralPath $application -Destination (Join-Path $fullOutput "WowVmMonitor.exe")

    $compilerCandidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    $compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $compiler) {
        throw "Inno Setup compiler was not found."
    }

    $definition = Join-Path $PSScriptRoot "WowVmMonitor.iss"
    $signScript = Join-Path $PSScriptRoot "Sign-ReleaseFile.ps1"
    $signCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$signScript`" -Path `$f"
    $arguments = @(
        "/Qp",
        "/DMyAppVersion=$Version",
        "/DSourceExe=$application",
        "/O$fullOutput",
        "/Slocal=$signCommand",
        $definition
    )
    & $compiler $arguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    $setup = Join-Path $fullOutput "WowVmMonitor-Setup-$Version.exe"
    if (-not (Test-Path -LiteralPath $setup)) {
        throw "Installer output was not found: $setup"
    }

    foreach ($file in @((Join-Path $fullOutput "WowVmMonitor.exe"), $setup)) {
        $signature = Get-AuthenticodeSignature -LiteralPath $file
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            throw "Release signature is invalid for ${file}: $($signature.Status)"
        }
        $hash = Get-FileHash -LiteralPath $file -Algorithm SHA256
        Write-Host "$([System.IO.Path]::GetFileName($file)) SHA256 $($hash.Hash)"
    }
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}
