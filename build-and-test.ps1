param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
$dotnet = if ($dotnetCommand) { $dotnetCommand.Source } else { "C:\Program Files\dotnet\dotnet.exe" }

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw ".NET SDK was not found."
}

$solution = Join-Path $PSScriptRoot "WowVmMonitor.sln"
& $dotnet build $solution --configuration $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot "tools\Sign-LocalBuild.ps1") -Configuration $Configuration

$resultsDirectory = Join-Path $PSScriptRoot "artifacts\TestResults"
Remove-Item -LiteralPath $resultsDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null

& $dotnet test $solution --configuration $Configuration --no-build --logger "trx;LogFileName=tests.trx" --results-directory $resultsDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

[xml]$testResults = Get-Content -LiteralPath (Join-Path $resultsDirectory "tests.trx") -Raw
$counters = $testResults.TestRun.ResultSummary.Counters
if ([int]$counters.executed -lt 1) {
    throw "The test runner did not execute any tests. Check Windows application-control events."
}

if ([int]$counters.failed -gt 0) {
    throw "$($counters.failed) automated test(s) failed."
}

Write-Host "Verified $($counters.passed) passing automated test(s)."
