[CmdletBinding()]
param(
    [switch]$NoLaunch,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$installDir = Join-Path $env:LOCALAPPDATA "SpotifyNoSleep"
$entryScript = Join-Path $installDir "spotify_no_sleep.pyw"

$pythonCmd = Get-Command python -ErrorAction SilentlyContinue
if (-not $pythonCmd) {
    throw "Python 3 was not found in PATH. Please install Python 3.10+ from python.org."
}
$pythonExe = $pythonCmd.Source
$pythonwExe = Join-Path (Split-Path -Parent $pythonExe) "pythonw.exe"
if (-not (Test-Path $pythonwExe)) {
    $pythonwExe = $pythonExe
}

if (-not $SkipTests) {
    Write-Host "Running SpotifyNoSleep test suite..." -ForegroundColor Cyan
    & $pythonExe -m unittest discover -s (Join-Path $repoRoot "tests") -v
    if ($LASTEXITCODE -ne 0) {
        throw "Unit tests failed with exit code $LASTEXITCODE"
    }
}

# Stop any existing SpotifyNoSleep background instance before replacing files.
$existingProcs = Get-CimInstance Win32_Process -Filter "Name='pythonw.exe' OR Name='python.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine -like "*spotify_no_sleep.py*" }
foreach ($proc in $existingProcs) {
    Write-Host "Stopping existing SpotifyNoSleep process (PID $($proc.ProcessId))..." -ForegroundColor Yellow
    Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
}
if ($existingProcs) {
    Start-Sleep -Milliseconds 400
}

if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

Copy-Item -Path (Join-Path $repoRoot "spotify_no_sleep.py") -Destination $installDir -Force
Copy-Item -Path (Join-Path $repoRoot "spotify_no_sleep.pyw") -Destination $installDir -Force
$pkgDest = Join-Path $installDir "spotify_nosleep"
if (Test-Path $pkgDest) {
    Remove-Item -Path $pkgDest -Recurse -Force
}
Copy-Item -Path (Join-Path $repoRoot "spotify_nosleep") -Destination $pkgDest -Recurse -Force
Write-Host "Installed to: $installDir" -ForegroundColor Green

# Register automatic startup on Windows logon (current user, no admin privileges required).
$regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$startupCommand = "`"$pythonwExe`" `"$entryScript`""
Set-ItemProperty -Path $regPath -Name "SpotifyNoSleep" -Value $startupCommand
Write-Host "Enabled automatic Windows startup: $startupCommand" -ForegroundColor Green

if (-not $NoLaunch) {
    Start-Process -FilePath $pythonwExe -ArgumentList "`"$entryScript`"" -WorkingDirectory $installDir
    Write-Host "SpotifyNoSleep is now running silently in your system tray." -ForegroundColor Cyan
}
