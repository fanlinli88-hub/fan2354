param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resolvedPath = (Resolve-Path -LiteralPath $Path).Path
$subject = "CN=WowVmMonitor Local Development"
$certificate = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object Subject -eq $subject |
    Where-Object NotAfter -gt (Get-Date) |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $certificate) {
    throw "Local development signing certificate was not found: $subject"
}

$signature = Set-AuthenticodeSignature `
    -LiteralPath $resolvedPath `
    -Certificate $certificate `
    -HashAlgorithm SHA256
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Failed to sign ${resolvedPath}: $($signature.StatusMessage)"
}

$verification = Get-AuthenticodeSignature -LiteralPath $resolvedPath
if ($verification.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Signature verification failed for ${resolvedPath}: $($verification.StatusMessage)"
}

Write-Host "Signed $resolvedPath with $subject."
