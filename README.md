# SpotifyNoSleep

A lightweight, zero-dependency native Windows system tray application (`SpotifyNoSleep.exe`) that automatically prevents your PC from going to sleep or locking the screen while Spotify is playing music — and restores normal Windows sleep and lock-screen behavior the moment playback stops.

## Features

- **Standalone Native Executable**: Compiles into a single tiny `SpotifyNoSleep.exe` using Windows' built-in .NET Framework 4.x C# compiler (`csc.exe`) — zero external runtimes, zero NuGet packages, and clean identity in Task Manager, Windows Startup Apps, and `powercfg /requests`.
- **Always-On Background Monitor**: Runs silently in the Windows system tray, enforces a single running instance via a Windows mutex, and starts automatically whenever Windows boots.
- **Triple-Signal Spotify Detection**:
  1. **Windows Media Control (SMTC)** (`Windows.Media.Control`): Reads live playback status (`Playing` / `Paused` / `Stopped`) and current track metadata for both Spotify Desktop and Spotify Microsoft Store apps — even during quiet intros or muted volume.
  2. **Windows Core Audio (WASAPI)**: Inspects active audio sessions and peak audio levels across *all* active audio output devices (speakers, headphones, Bluetooth, HDMI, USB DACs), so detection still works even if Spotify's desktop media overlay is disabled.
  3. **Spotify Window Title Fallback**: Handles transient audio device switches cleanly.
- **Reliable Sleep & Lock Screen Prevention**:
  - Acquires Windows kernel power requests (`PowerCreateRequest` / `PowerSetRequest` for `PowerRequestSystemRequired` + `PowerRequestDisplayRequired`) alongside `SetThreadExecutionState`.
  - Includes an optional **Strict Lock Prevention (Input Keepalive)** mode for managed/work PCs with Group Policy `Machine Inactivity Limit` locks (sends a harmless zero-delta input nudge only when idle >= 45 seconds).
- **Track-Transition Grace Period**: Keeps the PC awake for a short grace period (default `10s`) during track changes or brief pauses, and releases immediately when Spotify is closed.

## Quick Start

### Build, Test, Install, and Auto-Start

From PowerShell in the repository root:

```powershell
.\install.ps1
```

This will:
1. Compile `dist\SpotifyNoSleep.exe` and run all unit & native API tests.
2. Copy `SpotifyNoSleep.exe` to `%LocalAppData%\SpotifyNoSleep\SpotifyNoSleep.exe`.
3. Register automatic startup on Windows logon (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SpotifyNoSleep`, no Administrator privileges required).
4. Launch `SpotifyNoSleep.exe` silently in your system tray right away.

### Build & Run Tests Only

```powershell
.\build.ps1
```

### Uninstall

```powershell
.\uninstall.ps1
# Or also delete config & logs in %AppData%\SpotifyNoSleep:
.\uninstall.ps1 -RemoveData
```

## System Tray Controls

Right-click the **SpotifyNoSleep** icon in the taskbar notification area:

- **Green icon with yellow dot**: Spotify is playing — sleep & lock screen prevention is **ACTIVE**.
- **Grey icon**: Monitoring in the background — Spotify is idle/closed, normal Windows power settings apply.
- **Amber pause icon**: Monitoring manually paused (double-click the tray icon to quickly toggle pause/resume).

Menu options:
- **Pause / Resume Monitoring**
- **Prevent Lock Screen & Display Off** (enabled by default; uncheck to prevent sleep only while allowing the display to turn off)
- **Strict Lock Prevention (Input Keepalive)** (enable if your PC enforces a Group Policy / Intune inactivity lock)
- **Start Automatically with Windows**
- **Show Tray Notifications**
- **Open Log File**
- **Exit**

## CLI & Verification

Inspect live Spotify detection, kernel power request availability, and startup status:

```powershell
.\dist\SpotifyNoSleep.exe --diagnose
```

Verify active Windows kernel power requests while Spotify is playing (from an elevated terminal):

```powershell
powercfg /requests
```

While Spotify plays, Windows lists `SpotifyNoSleep.exe` under both `DISPLAY:` and `SYSTEM:` with reason `"Spotify is actively playing music"`, and clears both entries automatically after playback stops.

## Configuration

Settings are stored at `%AppData%\SpotifyNoSleep\config.json`:

| Setting | Default | Description |
|---|---|---|
| `PreventLockScreen` | `true` | Keeps display awake and prevents standard Windows lock screen & screensaver. |
| `StrictLockPrevention` | `false` | Sends an idle-gated zero-delta input nudge when idle >= 45s for GPO/Intune inactivity locks. |
| `PollIntervalMs` | `2000` | Playback check interval in milliseconds (`500`–`30000`). |
| `GracePeriodSeconds` | `10` | Seconds to stay awake during brief pauses/track changes (`0`–`300`). |
| `ShowNotifications` | `false` | Show tray balloon notifications when awake mode engages/releases. |
| `EnableLogging` | `true` | Write rotating logs to `%AppData%\SpotifyNoSleep\spotifynosleep.log`. |

## License

MIT — see [LICENSE](LICENSE).
