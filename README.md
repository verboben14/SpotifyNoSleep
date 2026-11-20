# SpotifyNoSleep

A lightweight, zero-dependency Windows system tray application that automatically prevents your PC from going to sleep or locking the screen while Spotify is playing music — and restores normal Windows power & lock behavior the moment playback stops.

## Features

- **Always-On Background Monitor**: Runs silently in the Windows system tray via signed `pythonw.exe` (no console window, 100% compatible with Windows 11 Smart App Control), enforces a single running instance via a Windows mutex, auto-recovers if Explorer restarts, and launches automatically whenever Windows starts.
- **Triple-Signal Spotify Detection** (native Windows WinRT + COM + Win32 APIs, zero third-party packages):
  1. **Windows Media Control (SMTC)** via `combase.dll`: Reads live playback status (`Playing` / `Paused` / `Stopped`) and current track metadata for both Spotify Desktop and Spotify Microsoft Store apps — even during quiet intros or muted volume.
  2. **Windows Core Audio (WASAPI)** via `ole32.dll`: Inspects active audio sessions and live peak meters across *all* active audio output devices (speakers, headphones, Bluetooth, HDMI, USB DACs), so detection still works even if Spotify's desktop overlay is disabled.
  3. **Spotify Window Title Fallback** via `user32.dll`: Handles transient audio device switches seamlessly.
- **Reliable Sleep & Lock Screen Prevention**:
  - Acquires Windows kernel power requests (`PowerCreateRequest` / `PowerSetRequest` for `PowerRequestSystemRequired` + `PowerRequestDisplayRequired`) alongside `SetThreadExecutionState`.
  - Includes an optional **Strict Lock Prevention (Input Keepalive)** mode for managed/work PCs with Group Policy `Machine Inactivity Limit` locks (only sends a harmless zero-delta input nudge when idle >= 45 seconds).
- **Track-Transition Grace Period**: Bridges brief track changes or short pauses (default `10s`) without state flapping, and releases immediately when Spotify is closed.
- **Zero External Dependencies**: Uses only the Python 3.10+ standard library and built-in Windows system DLLs — no `pip install` or unsigned `.exe` required.

## Quick Start

### Install, Enable Auto-Start, and Launch

From PowerShell in the repository root:

```powershell
.\install.ps1
```

This will:
1. Run the automated unit & Windows native API test suite.
2. Install the app to `%LocalAppData%\SpotifyNoSleep`.
3. Register automatic startup on Windows logon (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SpotifyNoSleep`, no Administrator privileges required).
4. Start **SpotifyNoSleep** silently in your system tray right away.

### Run Directly (Without Installing)

```powershell
# Launch silently in the system tray:
pythonw .\spotify_no_sleep.pyw

# Or run live diagnostics in the terminal:
python .\spotify_no_sleep.py --diagnose
```

### Run Unit Tests

```powershell
python -m unittest discover -s tests -v
```

### Uninstall

```powershell
.\uninstall.ps1
# Or also remove config & logs in %AppData%\SpotifyNoSleep:
.\uninstall.ps1 -RemoveData
```

## System Tray Controls

Right-click the **SpotifyNoSleep** icon in the taskbar notification area:

- **Green icon with yellow dot**: Spotify is playing — sleep & lock screen prevention is **ACTIVE**.
- **Grey icon**: Monitoring in the background — Spotify is idle/closed, normal Windows power settings apply.
- **Amber pause icon**: Monitoring manually paused (double-click the tray icon to toggle pause/resume).

Menu options:
- **Pause / Resume Monitoring**
- **Prevent Lock Screen & Display Off** (enabled by default; uncheck to prevent sleep only while allowing the display to turn off)
- **Strict Lock Prevention (Input Keepalive)** (enable if your PC enforces a Group Policy / Intune inactivity lock)
- **Start Automatically with Windows**
- **Show Tray Notifications**
- **Open Log File**
- **Exit**

## Verify Live Wake Locks

Inspect live Spotify detection and startup state:

```powershell
python .\spotify_no_sleep.py --diagnose
```

Verify active Windows kernel power requests while Spotify is playing (from an elevated terminal):

```powershell
powercfg /requests
```

While Spotify plays, Windows will list `pythonw.exe` under both `DISPLAY:` and `SYSTEM:` with reason `"Spotify is actively playing music"`, and clear both entries automatically after playback stops.

## Configuration

Settings are persisted at `%AppData%\SpotifyNoSleep\config.json`:

| Key | Default | Description |
|---|---|---|
| `prevent_lock_screen` | `true` | Keeps display awake and prevents standard Windows lock screen & screensaver. |
| `strict_lock_prevention` | `false` | Sends an idle-gated zero-delta input nudge when idle >= 45s for GPO/Intune inactivity locks. |
| `poll_interval_ms` | `2000` | Playback check interval in milliseconds (`500`–`30000`). |
| `grace_period_seconds` | `10` | Seconds to stay awake during brief pauses/track changes (`0`–`300`). |
| `show_notifications` | `false` | Show tray balloon notifications when awake mode engages/releases. |
| `enable_logging` | `true` | Write rotating logs to `%AppData%\SpotifyNoSleep\spotifynosleep.log`. |

## License

MIT — see [LICENSE](LICENSE).
