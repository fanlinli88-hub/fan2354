param(
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$subject = "CN=WowVmMonitor Local Development"
$certificate = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object Subject -eq $subject |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $certificate) {
    throw "Local development signing certificate was not found: $subject"
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputPattern = "\\bin\\$([regex]::Escape($Configuration))\\net8\.0\\"
$files = Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File |
    Where-Object {
        $_.FullName -match $outputPattern -and
        $_.Name -match '^WowVmMonitor\..*\.(dll|exe)$'
    }

if (-not $files) {
    throw "No WowVmMonitor build outputs were found for configuration $Configuration."
}

foreach ($file in $files) {
    $signature = Set-AuthenticodeSignature -FilePath $file.FullName -Certificate $certificate -HashAlgorithm SHA256
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Failed to sign $($file.FullName): $($signature.StatusMessage)"
    }
}

Write-Host "Signed $($files.Count) local build outputs with $subject."
