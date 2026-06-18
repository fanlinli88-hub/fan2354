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

$testProjects = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot "tests") -Recurse -Filter "*.csproj"
foreach ($project in $testProjects) {
    $resultName = "$($project.BaseName).trx"
    & $dotnet test $project.FullName --configuration $Configuration --no-build --logger "trx;LogFileName=$resultName" --results-directory $resultsDirectory
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$executed = 0
$passed = 0
$failed = 0
foreach ($resultFile in Get-ChildItem -LiteralPath $resultsDirectory -Filter "*.trx") {
    [xml]$testResults = Get-Content -LiteralPath $resultFile.FullName -Raw
    $counters = $testResults.TestRun.ResultSummary.Counters
    $executed += [int]$counters.executed
    $passed += [int]$counters.passed
    $failed += [int]$counters.failed
}

if ($executed -lt 1) {
    throw "The test runner did not execute any tests. Check Windows application-control events."
}

if ($failed -gt 0) {
    throw "$failed automated test(s) failed."
}

Write-Host "Verified $passed passing automated test(s)."
