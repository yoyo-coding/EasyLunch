param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateSet('x64', 'x86', 'ARM64')]
    [string]$Platform = 'x64',

    [switch]$Build
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$rid = switch ($Platform) {
    'ARM64' { 'win-arm64' }
    'x64' { 'win-x64' }
    'x86' { 'win-x86' }
}

if ($Build) {
    dotnet build (Join-Path $repoRoot 'StartPage.slnx') -p:Platform=$Platform -p:Configuration=$Configuration
}

$outputDir = Join-Path $repoRoot "StartPage\bin\$Platform\$Configuration\net8.0-windows10.0.19041.0\$rid"
$manifest = Join-Path $outputDir 'AppxManifest.xml'

if (-not (Test-Path $manifest)) {
    throw "Cannot find $manifest. Run with -Build first, or build the project in Visual Studio."
}

Add-AppxPackage -Register $manifest -ForceApplicationShutdown | Out-Null

$package = Get-AppxPackage | Where-Object { $_.InstallLocation -ieq $outputDir } | Select-Object -First 1
if (-not $package) {
    throw 'StartPage package registration failed.'
}

$aumid = "$($package.PackageFamilyName)!App"
Start-Process explorer.exe "shell:AppsFolder\$aumid"
Write-Host "Started StartPage via $aumid"
