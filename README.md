# SpotifyNoSleep

**Keep your Windows PC and monitors awake automatically whenever Spotify is playing music — and let Windows sleep & lock normally the moment you hit pause.**

---

## What Problem Does It Solve?

When you listen to Spotify on a Windows PC without actively moving your mouse or typing (while reading, cooking, hosting friends, relaxing on the couch, or working on another device), Windows does not treat Spotify playback as "display/user activity":

- Your **monitors turn off** mid-playlist.
- Your **screen locks** and interrupts playback controls or lyrics/album art.
- Your **PC goes to sleep**, cutting off your music completely.

Manually changing Windows Power & Sleep settings back and forth (or leaving a permanent keep-awake utility running all day) is tedious and wastes power when you aren't listening.

**SpotifyNoSleep** solves this automatically: it runs quietly in your system tray whenever your PC is on, watches for active Spotify playback, prevents sleep, screen-off, and lock screen *only* while music is playing, and immediately restores normal Windows power behavior when Spotify stops.

---

## What It Can Do

- **Automatic Sleep, Screen-Off & Lock Prevention**: Holds official Windows kernel power requests (`PowerRequestSystemRequired` + `PowerRequestDisplayRequired` and `SetThreadExecutionState`) while Spotify plays, and cleanly releases them when playback stops.
- **Triple-Signal Spotify Detection**:
  1. **Windows Media Control (SMTC)**: Reads live `Playing` / `Paused` / `Stopped` status and current `Artist - Track` metadata for both the **Spotify Desktop** and **Microsoft Store** apps — even during quiet intros or muted volume.
  2. **Windows Core Audio (WASAPI)**: Monitors active audio sessions and live peak meters across *all* output devices (speakers, headphones, Bluetooth, HDMI monitors, USB DACs), so it still works even if Spotify's desktop overlay is disabled.
  3. **Window Title Fallback**: Handles transient audio device switches cleanly.
- **Track-Transition Grace Period**: Bridges brief gaps between songs or short pauses (default `10s`) without flickering, and releases immediately when Spotify is closed.
- **Always-On & Lightweight**: Starts automatically on Windows logon (no Administrator privileges required), enforces a single running instance, uses ~0% CPU, and stays out of your way in the system tray.
- **Optional Strict Lock Prevention**: Includes a one-click tray toggle (**Input Keepalive**) for work/managed PCs with Group Policy `Machine Inactivity Limit` locks that ignore display power requests (only triggers a harmless zero-delta nudge after 45 seconds of user idleness).
- **Zero External Dependencies**: Standalone native Windows `.exe` built on the .NET Framework 4.x already included in Windows 10 and Windows 11.

---

## How to Install & Run

### Option 1: One-Click Windows Installer (Recommended)

1. Download **`SpotifyNoSleep-Setup.exe`** from the [Latest Release](https://github.com/verboben14/SpotifyNoSleep/releases/latest) (or directly from [`release/SpotifyNoSleep-Setup.exe`](release/SpotifyNoSleep-Setup.exe)).
2. Double-click **`SpotifyNoSleep-Setup.exe`** and click **Install**.
   - Installs to `%LocalAppData%\SpotifyNoSleep` (no admin password needed).
   - Enables **Start automatically with Windows**.
   - Adds a **Start Menu** shortcut and a standard **Windows Settings → Apps** uninstall entry.
   - Launches **SpotifyNoSleep** immediately in your system tray.

> **Windows SmartScreen / Smart App Control Note:** Because this is a free, open-source app without a paid code-signing certificate:
> - If **Windows SmartScreen** shows *"Windows protected your PC"*, click **More info** → **Run anyway**.
> - If Windows 11 **Smart App Control** is enabled in enforcement mode, it blocks unsigned executables; you can either inspect the source and build locally or check your Windows Security → App & browser control settings.

### Option 2: Portable Mode (No Installation)

1. Download **`SpotifyNoSleep.exe`** from [Releases](https://github.com/verboben14/SpotifyNoSleep/releases/latest) (or [`release/SpotifyNoSleep.exe`](release/SpotifyNoSleep.exe)).
2. Double-click **`SpotifyNoSleep.exe`** to run immediately in your system tray.
3. Optional: Right-click the tray icon and check **Start Automatically with Windows** whenever you want it to launch at logon.

### Option 3: Build & Install From Source (PowerShell)

No Visual Studio or SDK download is required — it compiles using Windows' built-in .NET Framework C# compiler (`csc.exe`):

```powershell
git clone https://github.com/verboben14/SpotifyNoSleep.git
cd SpotifyNoSleep

# Build SpotifyNoSleep.exe + SpotifyNoSleep-Setup.exe and run all unit tests:
.\build.ps1

# Or build, install, enable auto-start, and launch immediately:
.\install.ps1
```

---

## System Tray Icon & Controls

Look in the Windows taskbar notification area (on Windows 11, check the **`^` Show hidden icons** overflow menu near the clock — you can drag the icon onto the main taskbar to pin it).

| Icon | Meaning |
|---|---|
| **Green circle + yellow dot** | Spotify is playing — PC sleep, screen-off, and lock screen are **prevented**. |
| **Grey circle** | Monitoring in the background — Spotify is idle/closed, normal Windows power settings apply. |
| **Amber pause circle** | Monitoring is manually paused (double-click the tray icon to toggle pause/resume). |

**Right-click menu options:**
- **Status & Now Playing** (live state + current song title)
- **Pause / Resume Monitoring**
- **Prevent Lock Screen & Display Off** *(enabled by default; uncheck to prevent sleep only while allowing monitors to turn off)*
- **Strict Lock Prevention (Input Keepalive)** *(enable if your PC uses a corporate/GPO inactivity lock)*
- **Start Automatically with Windows**
- **Show Tray Notifications**
- **Open Log File**
- **Exit**

---

## Verifying It Works

Run a live diagnostic snapshot from PowerShell to inspect SMTC, WASAPI, kernel power requests, and the awake decision in real time:

```powershell
& "$env:LOCALAPPDATA\SpotifyNoSleep\SpotifyNoSleep.exe" --diagnose
```

You can also confirm Windows' active kernel power requests while Spotify is playing (from an elevated PowerShell/Terminal):

```powershell
powercfg /requests
```

While a track is playing, Windows will list `SpotifyNoSleep.exe` under both `DISPLAY:` and `SYSTEM:` with reason `"Spotify is actively playing music"`, and clear both entries automatically after playback pauses.

---

## Configuration

Settings are saved automatically at `%AppData%\SpotifyNoSleep\config.json`:

| Setting | Default | Description |
|---|---|---|
| `PreventLockScreen` | `true` | Keeps monitors awake and prevents standard Windows lock screen & screensaver. |
| `StrictLockPrevention` | `false` | Sends a harmless zero-delta mouse input nudge only after 45s of user idleness (for GPO/Intune inactivity locks). |
| `PollIntervalMs` | `2000` | Playback check interval in milliseconds (`500`–`30000`). |
| `GracePeriodSeconds` | `10` | Seconds to stay awake during brief pauses or track transitions (`0`–`300`). |
| `ShowNotifications` | `false` | Show tray balloon alerts when awake mode turns on or off. |
| `EnableLogging` | `true` | Write rotating logs to `%AppData%\SpotifyNoSleep\spotifynosleep.log`. |

---

## Uninstalling

You can uninstall cleanly in any of three ways:
1. **Windows Settings → Apps → Installed Apps** → search **SpotifyNoSleep** → **Uninstall**.
2. Open **Start Menu → SpotifyNoSleep → Uninstall SpotifyNoSleep** (or run **`SpotifyNoSleep-Setup.exe`** and click **Uninstall**).
3. From the repository folder in PowerShell:
   ```powershell
   .\uninstall.ps1 -RemoveData
   ```

---

## Repository Structure

```text
SpotifyNoSleep/
├── release/
│   ├── SpotifyNoSleep-Setup.exe   # Ready-to-run one-click Windows installer
│   └── SpotifyNoSleep.exe         # Ready-to-run standalone portable app
├── src/                           # C# source code for SpotifyNoSleep.exe
│   ├── Program.cs                 # Entry point, single-instance mutex, CLI flags
│   ├── SmtcDetector.cs            # Windows Media Control (WinRT SMTC) detector
│   ├── WasapiDetector.cs          # Windows Core Audio (WASAPI) COM detector
│   ├── WindowTitleDetector.cs     # Spotify window title fallback detector
│   ├── PlaybackDecisionEngine.cs  # Combined detection logic + grace period
│   ├── PowerManager.cs            # Kernel PowerCreateRequest + SetThreadExecutionState + input keepalive
│   ├── TrayApplicationContext.cs  # WinForms system tray UI & lifecycle
│   ├── TrayIconFactory.cs         # Dynamic state-aware tray icons
│   ├── StartupManager.cs          # Windows logon auto-start registry helper
│   ├── AppConfig.cs               # Persistent JSON config
│   └── Logger.cs                  # Rotating log file writer
├── installer/
│   └── InstallerProgram.cs        # Self-contained Windows GUI/CLI installer & uninstaller
├── tools/
│   └── generate_icon.ps1          # Multi-resolution .ico icon generator
├── tests/
│   └── SpotifyNoSleep.Tests.cs    # Automated unit & native Windows API tests
├── build.ps1                      # Zero-dependency build + test script
├── install.ps1                    # PowerShell install & launch script
└── uninstall.ps1                  # PowerShell uninstall script
```

---

## License

MIT — see [LICENSE](LICENSE).
