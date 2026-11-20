[CmdletBinding()]
param(
    [switch]$RemoveData
)

$ErrorActionPreference = "Stop"

$installDir = Join-Path $env:LOCALAPPDATA "SpotifyNoSleep"
$dataDir = Join-Path $env:APPDATA "SpotifyNoSleep"
$regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

$existingProcs = Get-CimInstance Win32_Process -Filter "Name='pythonw.exe' OR Name='python.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine -like "*spotify_no_sleep.py*" }
foreach ($proc in $existingProcs) {
    Write-Host "Stopping SpotifyNoSleep (PID $($proc.ProcessId))..." -ForegroundColor Yellow
    Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
}
if ($existingProcs) {
    Start-Sleep -Milliseconds 400
}

$existingReg = Get-ItemProperty -Path $regPath -Name "SpotifyNoSleep" -ErrorAction SilentlyContinue
if ($existingReg) {
    Remove-ItemProperty -Path $regPath -Name "SpotifyNoSleep" -Force
    Write-Host "Removed Windows startup entry." -ForegroundColor Green
}

if (Test-Path $installDir) {
    Remove-Item -Path $installDir -Recurse -Force
    Write-Host "Removed installed app: $installDir" -ForegroundColor Green
}

if ($RemoveData -and (Test-Path $dataDir)) {
    Remove-Item -Path $dataDir -Recurse -Force
    Write-Host "Removed configuration and logs: $dataDir" -ForegroundColor Green
}

Write-Host "SpotifyNoSleep uninstalled." -ForegroundColor Cyan
