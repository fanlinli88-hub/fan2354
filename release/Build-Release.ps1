param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $repositoryRoot "build-and-test.ps1") -Configuration Release
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$outputDirectory = Join-Path $repositoryRoot "artifacts\release\$Version"
& (Join-Path $PSScriptRoot "Build-Installer.ps1") `
    -Version $Version `
    -OutputDirectory $outputDirectory
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$application = Join-Path $outputDirectory "WowVmMonitor.exe"
$installer = Join-Path $outputDirectory "WowVmMonitor-Setup-$Version.exe"
$expected = @($application, $installer)
foreach ($file in $expected) {
    if (-not (Test-Path -LiteralPath $file)) {
        throw "Release file was not found: $file"
    }
}

$checksumPath = Join-Path $outputDirectory "SHA256SUMS.txt"
$checksumLines = foreach ($file in $expected) {
    $hash = Get-FileHash -LiteralPath $file -Algorithm SHA256
    "$($hash.Hash)  $([System.IO.Path]::GetFileName($file))"
}
Set-Content -LiteralPath $checksumPath -Value $checksumLines -Encoding ascii

$files = @(Get-ChildItem -LiteralPath $outputDirectory -File)
if ($files.Count -ne 3) {
    throw "Expected three release files, found: $($files.Name -join ', ')"
}

foreach ($file in @($application, $installer)) {
    $item = Get-Item -LiteralPath $file
    $signature = Get-AuthenticodeSignature -LiteralPath $file
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Release signature is invalid for ${file}: $($signature.Status)"
    }
    Write-Host "$($item.Name): $($item.Length) bytes, version $($item.VersionInfo.FileVersion), signature $($signature.Status)"
}

$installerVersion = (Get-Item -LiteralPath $installer).VersionInfo.FileVersion.Trim()
if ($installerVersion -ne "$Version.0") {
    throw "Installer file version does not match release version $Version."
}

Write-Host "Release package created: $outputDirectory"
