param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "src\WowVmMonitor.Desktop\WowVmMonitor.Desktop.csproj"
$dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
$dotnet = if ($dotnetCommand) { $dotnetCommand.Source } else { "C:\Program Files\dotnet\dotnet.exe" }
if (-not (Test-Path -LiteralPath $dotnet)) {
    throw ".NET SDK was not found."
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

& $dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0" `
    --output $fullOutput
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$files = @(Get-ChildItem -LiteralPath $fullOutput -File)
if ($files.Count -ne 1 -or $files[0].Name -ne "WowVmMonitor.exe") {
    throw "Single-file publish produced unexpected files: $($files.Name -join ', ')"
}

& (Join-Path $PSScriptRoot "Sign-ReleaseFile.ps1") -Path $files[0].FullName
$hash = Get-FileHash -LiteralPath $files[0].FullName -Algorithm SHA256
Write-Host "Published $($files[0].FullName)"
Write-Host "SHA256 $($hash.Hash)"
