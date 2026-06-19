Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repositoryRoot "src\WowVmMonitor.Core\bin\Release\net8.0\WowVmMonitor.Core.dll"
if (-not (Test-Path -LiteralPath $source)) {
    throw "Build output was not found: $source"
}

$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("WowVmMonitor-signing-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
try {
    $copy = Join-Path $temporaryDirectory "WowVmMonitor.Core.dll"
    Copy-Item -LiteralPath $source -Destination $copy
    & (Join-Path $PSScriptRoot "Sign-ReleaseFile.ps1") -Path $copy

    $signature = Get-AuthenticodeSignature -LiteralPath $copy
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Expected a valid Authenticode signature, got $($signature.Status)."
    }
}
finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Release signing check passed."
