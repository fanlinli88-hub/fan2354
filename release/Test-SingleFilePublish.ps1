Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("WowVmMonitor-publish-" + [guid]::NewGuid().ToString("N"))
try {
    & (Join-Path $PSScriptRoot "Publish-SingleFile.ps1") `
        -Version "1.0.0" `
        -OutputDirectory $temporaryDirectory

    $files = @(Get-ChildItem -LiteralPath $temporaryDirectory -File)
    if ($files.Count -ne 1 -or $files[0].Name -ne "WowVmMonitor.exe") {
        throw "Expected only WowVmMonitor.exe, found: $($files.Name -join ', ')"
    }

    $version = $files[0].VersionInfo.FileVersion
    if ($version -ne "1.0.0.0") {
        throw "Expected file version 1.0.0.0, got $version."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $files[0].FullName
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Expected a valid signature, got $($signature.Status)."
    }
}
finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Single-file publish check passed."
