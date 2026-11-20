[CmdletBinding()]
param(
    [switch]$NoLaunch,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$distExe = Join-Path $repoRoot "dist\SpotifyNoSleep.exe"
$installDir = Join-Path $env:LOCALAPPDATA "SpotifyNoSleep"
$installedExe = Join-Path $installDir "SpotifyNoSleep.exe"

if (-not $SkipBuild) {
    & (Join-Path $repoRoot "build.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed."
    }
}

if (-not (Test-Path $distExe)) {
    throw "Built executable not found at $distExe. Run build.ps1 first."
}

# Stop any existing SpotifyNoSleep.exe instance
$running = Get-Process -Name "SpotifyNoSleep" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping existing SpotifyNoSleep process..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 400
}

# Also stop any legacy pythonw.exe instance from an earlier install
$legacyProcs = Get-CimInstance Win32_Process -Filter "Name='pythonw.exe' OR Name='python.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine -like "*spotify_no_sleep.py*" }
foreach ($proc in $legacyProcs) {
    Write-Host "Stopping legacy Python SpotifyNoSleep process (PID $($proc.ProcessId))..." -ForegroundColor Yellow
    Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
}
if ($legacyProcs) {
    Start-Sleep -Milliseconds 400
}

if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

# Clean up any legacy Python files if upgrading in-place
foreach ($legacyItem in @("spotify_no_sleep.py", "spotify_no_sleep.pyw", "spotify_nosleep")) {
    $legacyPath = Join-Path $installDir $legacyItem
    if (Test-Path $legacyPath) {
        Remove-Item -Path $legacyPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Copy-Item -Path $distExe -Destination $installedExe -Force
Write-Host "Installed to: $installedExe" -ForegroundColor Green

# Register automatic startup on Windows logon (current user, no admin privileges required).
$regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Set-ItemProperty -Path $regPath -Name "SpotifyNoSleep" -Value "`"$installedExe`""
Write-Host "Enabled automatic Windows startup: `"$installedExe`"" -ForegroundColor Green

if (-not $NoLaunch) {
    try {
        $action = New-ScheduledTaskAction -Execute $installedExe
        $trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Seconds 0)
        Register-ScheduledTask -TaskName "SpotifyNoSleep" -Action $action -Trigger $trigger -Settings $settings -Force | Out-Null
        Start-ScheduledTask -TaskName "SpotifyNoSleep"
    }
    catch {
        Start-Process -FilePath $installedExe
    }
    Write-Host "SpotifyNoSleep.exe is now running silently in your system tray." -ForegroundColor Cyan
}
