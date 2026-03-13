# JiggleSharp

A cross-platform mouse jiggler that prevents your system from locking or marking you as away. JiggleSharp runs quietly in the system tray and, after a configurable idle period, performs a natural-looking mouse movement using the **WindMouse** algorithm — a physics-based cursor trajectory that mimics real human motion.

## Features

- **Human-like movement** — WindMouse applies gravity and stochastic wind forces to produce curved, varied paths rather than mechanical straight-line jumps.
- **Idle-aware** — only acts after the user has been idle for a configurable duration (default: 5 minutes). Resumes silently once real activity is detected.
- **Cross-platform** — native implementations for Linux (Wayland), macOS, and Windows.
- **System tray UI** — lightweight background app with a customisable tray icon (emoji + colour).
- **Fully configurable** — every WindMouse parameter is tunable at runtime via the Settings window without a restart.
- **Startup integration** — optional launch on system boot and/or auto-start the engine when the application opens.

## Platform Support

| Platform | Idle Detection | Input Injection |
|----------|---------------|-----------------|
| Linux (Wayland + GNOME) | Mutter D-Bus (`org.gnome.Mutter.IdleMonitor`) | `ydotoold` |
| Linux (Wayland + KDE) | KWin idle protocol | `ydotoold` |
| macOS | Native APIs | Native APIs |
| Windows | Native APIs | Native APIs |

> **Note:** Linux X11 sessions are not currently supported for idle detection.

## Prerequisites

### Linux

- A **Wayland** session (GNOME or KDE Plasma).
- [`ydotool`](https://github.com/ReimuNotMoe/ydotool) installed, with `ydotoold` running as a system-wide systemd service. The service definition must include a `--socket-path` argument — JiggleSharp reads this from the unit at startup to locate the socket.

Some distributions (e.g. Fedora) do not ship a service unit for `ydotoold`. Create one manually:

**`/etc/systemd/system/ydotoold.service`**
```ini
[Unit]
Description=ydotool daemon
Documentation=man:ydotoold(8)

[Service]
ExecStart=/usr/bin/ydotoold --socket-path=/tmp/.ydotool_socket
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

Then enable and start it:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now ydotoold.service

# Verify the service is running
sudo systemctl status ydotoold.service
```

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
│   ├── PlatformServicesFactory.cs  # Selects the correct platform backend at runtime
│   └── ApplicationConfiguration.cs # Persisted user settings
│
├── JiggleSharp.Core/       # Platform-agnostic engine and interfaces
│   ├── Engine/
│   │   ├── JiggleEngine.cs     # Core jiggle loop + WindMouse implementation
│   │   ├── JiggleOptions.cs    # All tunable engine parameters
│   │   └── IPlatformServices.cs
│   ├── Idle/               # IIdleTimeProvider interface + event args
│   └── Input/              # IInputInjector interface
│
├── JiggleSharp.Linux/      # Linux platform implementation
│   ├── Idle/               # Mutter (GNOME) and KWin (KDE) idle providers via D-Bus
│   ├── Input/              # ydotool-based mouse injection
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
