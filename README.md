# JiggleSharp

A cross-platform mouse jiggler that prevents your system from locking or marking you as away. JiggleSharp runs quietly in the system tray and, after a configurable idle period, performs a natural-looking mouse movement using the **WindMouse** algorithm — a physics-based cursor trajectory that mimics real human motion.

## Features

- **Human-like movement** — WindMouse applies gravity and stochastic wind forces to produce curved, varied paths rather than mechanical straight-line jumps.
- **Idle-aware** — only acts after the user has been idle for a configurable duration (default: 5 minutes). Resumes silently once real activity is detected.
- **Cross-platform** — native implementations for Linux (Wayland), macOS, and Windows.
- **System tray UI** — lightweight background app with a customisable tray icon (emoji + colour).
- **Fully configurable** — every WindMouse parameter is tunable at runtime via the Settings window without a restart.
- **Startup integration** — optional launch on system boot and/or auto-start the engine when the application opens.
- **Command-line control** — start, stop, or toggle jiggling in the running tray instance from a shell, script, or hotkey binding.

## Platform Support

| Platform | Idle Detection | Input Injection |
|----------|---------------|-----------------|
| Linux (Wayland + GNOME) | Mutter D-Bus (`org.gnome.Mutter.IdleMonitor`) | XDG Desktop Portal (`org.freedesktop.portal.RemoteDesktop`) |
| Linux (Wayland + KDE) | KWin idle protocol | XDG Desktop Portal (`org.freedesktop.portal.RemoteDesktop`) |
| macOS | Native APIs | Native APIs |
| Windows | Native APIs | Native APIs |

> **Note:** Linux X11 sessions are not currently supported for idle detection.

## Prerequisites

### Linux

- A **Wayland** session (GNOME or KDE Plasma).
- [`xdg-desktop-portal`](https://github.com/flatpak/xdg-desktop-portal) with a RemoteDesktop-capable backend (e.g. `xdg-desktop-portal-gnome` or `xdg-desktop-portal-kde`) installed and running — these ship by default on GNOME and KDE Plasma, so most desktop installs need no extra setup.

JiggleSharp talks to the portal's `org.freedesktop.portal.RemoteDesktop` interface directly over D-Bus — no separate daemon or process needs to be installed or started. The first time JiggleSharp moves the mouse, the desktop will show a one-time consent dialog asking to grant remote control access; this must be approved for mouse movement to work.

### macOS / Windows

No additional dependencies required.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone <repo-url>
cd JiggleSharp
dotnet build
```

### Run

```bash
dotnet run --project JiggleSharp.App
```

### Publish (self-contained)

```bash
# Linux
dotnet publish JiggleSharp.App -r linux-x64 -c Release --self-contained

# macOS (produces a .app bundle)
dotnet publish JiggleSharp.App -r osx-arm64 -c Release --self-contained

# Windows
dotnet publish JiggleSharp.App -r win-x64 -c Release --self-contained
```

## Solution Structure

```
JiggleSharp/
├── JiggleSharp.App/        # Avalonia UI shell — tray icon, settings window, DI host
│   ├── ViewModels/         # MVVM view models (CommunityToolkit.Mvvm)
│   ├── Cli/                # Command-line parsing, help text, console attachment
│   ├── PlatformServicesFactory.cs  # Selects the correct platform backend at runtime
│   └── ApplicationConfiguration.cs # Persisted user settings
│
├── JiggleSharp.Core/       # Platform-agnostic engine and interfaces
│   ├── Engine/
│   │   ├── JiggleEngine.cs     # Core jiggle loop + WindMouse implementation
│   │   ├── JiggleOptions.cs    # All tunable engine parameters
│   │   └── IPlatformServices.cs
│   ├── Idle/               # IIdleTimeProvider interface + event args
│   ├── Input/              # IInputInjector interface
│   └── Ipc/                # Named-pipe server/client for command-line control
│
├── JiggleSharp.Linux/      # Linux platform implementation
│   ├── Idle/               # Mutter (GNOME) and KWin (KDE) idle providers via D-Bus
│   ├── Input/              # XDG Desktop Portal (RemoteDesktop) mouse injection via D-Bus
│   └── System/             # Autostart / systemd integration
│
├── JiggleSharp.Mac/        # macOS platform implementation
└── JiggleSharp.Windows/    # Windows platform implementation
```

## Configuration

Settings are accessible from the tray icon context menu. All changes take effect immediately without a restart.

| Setting | Description | Default |
|---------|-------------|---------|
| **Idle Timeout** | Seconds of inactivity before a jiggle is triggered | `300` (5 min) |
| **Mouse Speed** | Speed divisor range for the force vector (higher = slower) | `5 – 15` |
| **Gravity** | Pull strength toward the target (higher = straighter path) | `5 – 10` |
| **Wind** | Random perturbation magnitude (higher = more erratic path) | `1 – 5` |
| **Target Radius** | Pixel distance at which the move is considered complete | `2 – 5 px` |
| **Velocity Max Step** | Per-step velocity cap to prevent large jumps | `5 – 15` |
| **Movement Delay** | Delay between path points in smooth mode | `2000 – 3500 µs` |
| **Path Points Maximum** | Hard cap on generated path length | `1000` |
| **Tray Icon** | Emoji displayed in the system tray | `🖱️` |
| **Start engine on app launch** | Auto-start jiggling when JiggleSharp opens | `true` |
| **Start on system startup** | Launch JiggleSharp automatically on login | `false` |

## Command-Line Arguments

JiggleSharp can be driven from a shell, a script, or a desktop hotkey binding. A
newly launched process detects the instance already running in the tray and
forwards the command to it over a local named pipe, so the tray instance's engine
is what actually starts or stops — a second process cannot affect it by changing
its own in-memory state.

| Flag | With an instance running | With nothing running |
|------|--------------------------|----------------------|
| `--start` | Starts jiggling in the running instance. | Launches JiggleSharp with jiggling active, regardless of the **Start engine on app launch** setting. |
| `--stop` | Stops jiggling in the running instance. The app stays in the tray. | Prints `JiggleSharp is not running.` and exits. Does **not** launch the app. |
| `--toggle` | Inverts the running instance's jiggling state. | Same as `--start`. |
| `--status` | Reports whether jiggling is currently active. | Prints `JiggleSharp is not running.` and exits. Does **not** launch the app. |
| `-h`, `--help` | Prints usage and exits. | Prints usage and exits. |

Only one action flag may be given per invocation. Running JiggleSharp with no
flags launches the tray application; if an instance is already running, the new
process reports that and exits instead of adding a second tray icon.

### Exit codes

| Code | Meaning |
|------|---------|
| `0` | Success — including `--stop` and `--status` when no instance is running. |
| `1` | An instance was reached but reported an error. |
| `2` | The command line could not be parsed. |

### Examples

```bash
# Start jiggling, launching JiggleSharp first if it isn't already running
JiggleSharp --start

# Pause jiggling without quitting the tray app
JiggleSharp --stop

# Bind to a hotkey or call from a script
JiggleSharp --toggle

# Check the current state
JiggleSharp --status
# -> JiggleSharp is running and jiggling.
```

During development, pass the flags after `--` so the SDK forwards them to the app
rather than consuming them itself:

```bash
dotnet run --project JiggleSharp -- --status
```

### Platform notes

**macOS.** A published build is an `.app` bundle, whose executable lives inside
it. Either invoke that executable directly or use `open --args`:

```bash
/Applications/JiggleSharp.app/Contents/MacOS/JiggleSharp --stop

# or, equivalently
open -a JiggleSharp --args --stop
```

Note that `open` does not relay the command's output back to your terminal, so
prefer the direct path when you want to read `--status`.

**Windows.** JiggleSharp is a GUI-subsystem executable, so it is not given a
console of its own; it attaches to the console of the invoking shell in order to
print its output. One cosmetic consequence is unavoidable for such an
executable: the shell returns the prompt as soon as the process starts, so the
output prints underneath the new prompt. When sequencing JiggleSharp inside a
script, wait for it explicitly:

```powershell
Start-Process -Wait -NoNewWindow JiggleSharp.exe -ArgumentList "--stop"
```

**Linux.** The pipe is a Unix domain socket under `$TMPDIR` (normally `/tmp`).
Because that directory is shared between users, the socket name includes your
user name, so several users can run JiggleSharp on the same machine without
interfering with one another. Access is restricted to the owning user.

## Acknowledgements

The WindMouse implementation is based on [wayland-jiggler](https://github.com/emilszymecki/wayland-jiggler) by Emil Szymecki.

## How It Works

1. The selected platform's `IIdleTimeProvider` emits an `IdleTimeChanged` event on a regular interval.
2. `JiggleEngine` checks whether the reported idle time exceeds the configured timeout **and** that the engine itself has not acted within the same window (prevents retriggering immediately after its own movement resets the compositor's idle clock).
3. When both conditions are met, a random target offset (±400 px) is chosen and a WindMouse path is generated toward it.
4. Each path point is dispatched to `IInputInjector` with a per-point microsecond delay for smooth, human-like playback.

## Tech Stack

- [.NET 10](https://dotnet.microsoft.com/)
- [Avalonia UI 11.3](https://avaloniaui.net/) — cross-platform desktop UI
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM source generators
- [Semi.Avalonia](https://github.com/irihitech/Semi.Avalonia) + [Ursa](https://github.com/irihitech/Ursa.Avalonia) — UI theme
- [Serilog](https://serilog.net/) — structured logging
- [Microsoft.Extensions.Hosting](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host) — DI / hosted services
- [Tmds.DBus](https://github.com/tmds/Tmds.DBus) — D-Bus communication on Linux

## License

See [LICENSE](LICENSE) for details.
