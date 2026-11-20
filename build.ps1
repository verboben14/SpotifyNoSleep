[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$SkipReleaseCopy
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $repoRoot "src"
$installerDir = Join-Path $repoRoot "installer"
$testsDir = Join-Path $repoRoot "tests"
$distDir = Join-Path $repoRoot "dist"
$releaseDir = Join-Path $repoRoot "release"
$assetsDir = Join-Path $repoRoot "assets"
$iconPath = Join-Path $assetsDir "SpotifyNoSleep.ico"

$frameworkDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path (Join-Path $frameworkDir "csc.exe"))) {
    $frameworkDir = "C:\Windows\Microsoft.NET\Framework\v4.0.30319"
}

$csc = Join-Path $frameworkDir "csc.exe"
if (-not (Test-Path $csc)) {
    throw "Could not find .NET Framework csc.exe at $csc"
}

$winMetadataDir = "C:\Windows\System32\WinMetadata"
$mediaWinmd = Join-Path $winMetadataDir "Windows.Media.winmd"
$foundationWinmd = Join-Path $winMetadataDir "Windows.Foundation.winmd"
$sysRuntime = Join-Path $frameworkDir "System.Runtime.dll"

foreach ($required in @($mediaWinmd, $foundationWinmd, $sysRuntime)) {
    if (-not (Test-Path $required)) {
        throw "Required Windows runtime assembly not found: $required"
    }
}

if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

# Generate multi-resolution Windows application icon (.ico)
Write-Host "Generating application icon..." -ForegroundColor Cyan
& (Join-Path $repoRoot "tools\generate_icon.ps1") -OutputPath $iconPath

$appExe = Join-Path $distDir "SpotifyNoSleep.exe"
$setupExe = Join-Path $distDir "SpotifyNoSleep-Setup.exe"
$sourceFiles = Get-ChildItem -Path $srcDir -Filter "*.cs" | ForEach-Object { $_.FullName }

Write-Host "Building SpotifyNoSleep.exe..." -ForegroundColor Cyan

$appArgs = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/platform:anycpu",
    "/win32icon:$iconPath",
    "/out:$appExe",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    "/r:System.Runtime.Serialization.dll",
    "/r:$sysRuntime",
    "/r:$foundationWinmd",
    "/r:$mediaWinmd"
) + $sourceFiles

& $csc @appArgs
if ($LASTEXITCODE -ne 0) {
    throw "Compilation of SpotifyNoSleep.exe failed with exit code $LASTEXITCODE"
}
Write-Host "Built: $appExe" -ForegroundColor Green

Write-Host "Building self-contained installer SpotifyNoSleep-Setup.exe..." -ForegroundColor Cyan

$installerArgs = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/platform:anycpu",
    "/win32icon:$iconPath",
    "/resource:$appExe,SpotifyNoSleep.exe",
    "/out:$setupExe",
    "/r:System.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    (Join-Path $installerDir "InstallerProgram.cs")
)

& $csc @installerArgs
if ($LASTEXITCODE -ne 0) {
    throw "Compilation of SpotifyNoSleep-Setup.exe failed with exit code $LASTEXITCODE"
}
Write-Host "Built: $setupExe" -ForegroundColor Green

if (-not $SkipReleaseCopy) {
    if (-not (Test-Path $releaseDir)) {
        New-Item -ItemType Directory -Path $releaseDir | Out-Null
    }
    Copy-Item -Path $appExe -Destination (Join-Path $releaseDir "SpotifyNoSleep.exe") -Force
    Copy-Item -Path $setupExe -Destination (Join-Path $releaseDir "SpotifyNoSleep-Setup.exe") -Force
    Write-Host "Updated runnable binaries in: $releaseDir" -ForegroundColor Green
}

if (-not $SkipTests) {
    $testExe = Join-Path $distDir "SpotifyNoSleep.Tests.exe"
    $testSources = @(
        (Join-Path $srcDir "PlaybackModels.cs"),
        (Join-Path $srcDir "PlaybackDecisionEngine.cs"),
        (Join-Path $srcDir "AppConfig.cs"),
        (Join-Path $srcDir "Logger.cs"),
        (Join-Path $srcDir "PowerManager.cs"),
        (Join-Path $srcDir "TrayIconFactory.cs"),
        (Join-Path $srcDir "SmtcDetector.cs"),
        (Join-Path $srcDir "WasapiDetector.cs"),
        (Join-Path $srcDir "WindowTitleDetector.cs"),
        (Join-Path $testsDir "SpotifyNoSleep.Tests.cs")
    )

    Write-Host "Building and running unit tests..." -ForegroundColor Cyan

    $testArgs = @(
        "/nologo",
        "/target:exe",
        "/platform:anycpu",
        "/out:$testExe",
        "/r:System.dll",
        "/r:System.Core.dll",
        "/r:System.Drawing.dll",
        "/r:System.Runtime.Serialization.dll",
        "/r:$sysRuntime",
        "/r:$foundationWinmd",
        "/r:$mediaWinmd"
    ) + $testSources

    & $csc @testArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Compilation of unit tests failed with exit code $LASTEXITCODE"
    }

    & $testExe
    if ($LASTEXITCODE -ne 0) {
        throw "Unit tests failed with exit code $LASTEXITCODE"
    }
}
