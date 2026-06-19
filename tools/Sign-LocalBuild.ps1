param(
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputPattern = "\\bin\\$([regex]::Escape($Configuration))\\net8\.0(?:-windows)?\\"
$files = Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File |
    Where-Object {
        $_.FullName -match $outputPattern -and
        $_.Name -match '^WowVmMonitor(?:\..*)?\.(dll|exe)$'
    }

if (-not $files) {
    throw "No WowVmMonitor build outputs were found for configuration $Configuration."
}

foreach ($file in $files) {
    & (Join-Path $repositoryRoot "release\Sign-ReleaseFile.ps1") -Path $file.FullName
}

Write-Host "Signed $($files.Count) local build outputs."
