Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$definition = Join-Path $PSScriptRoot "WowVmMonitor.iss"
if (-not (Test-Path -LiteralPath $definition)) {
    throw "Installer definition was not found: $definition"
}

$content = Get-Content -LiteralPath $definition -Raw
$requiredPatterns = @(
    'AppId=\{\{[0-9A-Fa-f-]{36}\}',
    'PrivilegesRequired=lowest',
    'DefaultDirName=\{localappdata\}\\Programs\\WowVmMonitor',
    'ArchitecturesAllowed=x64compatible',
    'ArchitecturesInstallIn64BitMode=x64compatible',
    'CloseApplications=yes',
    'RestartApplications=no',
    'SignedUninstaller=yes',
    'SignTool=local',
    'Name: "desktopicon"',
    '\{autoprograms\}',
    'Languages\\ChineseSimplified\.isl'
)

foreach ($pattern in $requiredPatterns) {
    if ($content -notmatch $pattern) {
        throw "Installer definition is missing required pattern: $pattern"
    }
}

if ($content -match '\{localappdata\}\\WowVmMonitor.*uninsdelete') {
    throw "Installer must not delete the user configuration directory."
}

Write-Host "Installer metadata check passed."
