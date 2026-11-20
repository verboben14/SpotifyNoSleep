[CmdletBinding()]
param(
    [switch]$RemoveData
)

$ErrorActionPreference = "Stop"

$installDir = Join-Path $env:LOCALAPPDATA "SpotifyNoSleep"
$dataDir = Join-Path $env:APPDATA "SpotifyNoSleep"
$regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

$running = Get-Process -Name "SpotifyNoSleep" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping SpotifyNoSleep..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 400
}

$existingReg = Get-ItemProperty -Path $regPath -Name "SpotifyNoSleep" -ErrorAction SilentlyContinue
if ($existingReg) {
    Remove-ItemProperty -Path $regPath -Name "SpotifyNoSleep" -Force
    Write-Host "Removed Windows startup entry." -ForegroundColor Green
}

$existingTask = Get-ScheduledTask -TaskName "SpotifyNoSleep" -ErrorAction SilentlyContinue
if ($existingTask) {
    Unregister-ScheduledTask -TaskName "SpotifyNoSleep" -Confirm:$false
    Write-Host "Removed SpotifyNoSleep scheduled task." -ForegroundColor Green
}

if (Test-Path $installDir) {
    Remove-Item -Path $installDir -Recurse -Force
    Write-Host "Removed installed binaries: $installDir" -ForegroundColor Green
}

if ($RemoveData -and (Test-Path $dataDir)) {
    Remove-Item -Path $dataDir -Recurse -Force
    Write-Host "Removed configuration and logs: $dataDir" -ForegroundColor Green
}

Write-Host "SpotifyNoSleep uninstalled." -ForegroundColor Cyan
