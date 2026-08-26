[🇷🇺 Русский (Russian)](README.ru.md) | [🇬🇧 English](README.md)

# HeadsetControl Taskbar Battery Indicator

[![Release](https://img.shields.io/github/v/release/nikpsov/headsetcontrol-taskbar-battery-indicator?label=release)](https://github.com/nikpsov/headsetcontrol-taskbar-battery-indicator/releases/latest) [![Tag](https://img.shields.io/github/v/tag/nikpsov/headsetcontrol-taskbar-battery-indicator?label=tag)](https://github.com/nikpsov/headsetcontrol-taskbar-battery-indicator/tags)

A lightweight and fast Windows application that displays the battery level of various wireless gaming headsets directly on the taskbar with a modern interactive overlay and flyout panel.

<!-- Screenshots -->
![Taskbar Indicator](docs/taskbar-indicator.png)
![Taskbar Percentage](docs/taskbar-percentage.png)
![Popup](docs/popup.png)

## Features
- **Direct In-Process Library Integration:** Direct communication with the `HeadsetControl` library via C-API without spawning external CLI processes.
- **Taskbar Battery Indicator:** Monitor your headset's battery level directly on the Windows taskbar (percentage or battery bar icon).
- **Interactive Flyout Panel:**
  - Displays current battery percentage, visual charging pill/badge, battery level progress bar, and device name.
  - Estimated remaining battery runtime and time to full charge.
  - Accurate battery voltage (mV) when supported by the headset hardware.
  - **Inactive Sleep Timer:** Configure auto-sleep timeouts directly from the flyout (*Off, 5m, 15m, 30m, 1h*).
- **Wide Device Support:** Supports Logitech (including Centurion protocol / G PRO X 2), SteelSeries, Corsair, HyperX, Razer, Astro, and more.
- **Live Charging Detection:** Visual lightning bolt charging badge on the taskbar icon, animated green charging bar, and dedicated charging status indicators.
- **Customizable & Theme-Aware:** Automatic hiding when disconnected, Windows 11 Fluent dark & light theme styling for overlay and context menu, style switcher (percentage or battery icon).
- **Auto-start & Notifications:** Low battery toast warnings (<= 20%) and Windows startup support.
- **Safe Logging & Rotation:** File logging is disabled by default in release builds (enabled via `--debug` or `HeadsetControlTaskbarBatteryIndicatorDebug.exe`) with automatic 1MB log rotation.

<!-- Screenshots -->
![Settings Context Menu](docs/context-menu.png)

## Installation

1. Navigate to the [Releases](https://github.com/nikpsov/headsetcontrol-taskbar-battery-indicator/releases/latest) page.
2. Download `HeadsetControlTaskbarBatteryIndicatorSetup.exe` (or the portable `HeadsetControlTaskbarBatteryIndicatorPortable.zip`).
3. Run the installer and follow the on-screen steps.
4. The indicator will appear on your taskbar and system tray.

## Building from Source

### Prerequisites:
- Windows 10/11
- C++20 compiler (`Clang` or `MSVC`)
- .NET Framework 4.8 / .NET 8 SDK

### Build:
Run the build script:
```cmd
build.bat
```
This compiles the native `headsetcontrol.dll` from vendored submodules and builds `HeadsetControlTaskbarBatteryIndicator.exe`.

## Acknowledgements & Credits

This project builds upon great open-source work in the gaming headset and Windows utility ecosystem:

- **[HeadsetControl](https://github.com/Sapd/HeadsetControl)** (by @Sapd)  
  *What we use:* The core C++20 headset protocol engine and device registry. Vendored as a Git submodule (`vendor/headsetcontrol`) and compiled into an in-process native library (`headsetcontrol.dll`) via its C-API (`headsetcontrol_c.h`).
- **[hidapi](https://github.com/libusb/hidapi)** (by libusb / Alan Ott)  
  *What we use:* The low-level USB HID communication library on Windows (using Win32 SetupAPI), statically compiled into `headsetcontrol.dll`.
- **[headset-battery-indicator](https://github.com/aarol/headset-battery-indicator)** (by @aarol)  
  *What we borrowed:* The original concept of a lightweight Windows taskbar/tray battery indicator and the initial baseline for C# P/Invoke bindings to the `HeadsetControl` C-API.
- **[DeskBox](https://github.com/Tianyu199509/DeskBox)** (by @Tianyu199509)  
  *What we borrowed:* Advanced Windows desktop overlay optimization techniques, including DWM window cloaking & transition suppression (`DWMWA_CLOAK`, `DWMWA_TRANSITIONS_FORCEDISABLED`) for flicker-free window rendering, multi-monitor work-area clamping algorithm for the flyout popup, flyout instance caching / zero-latency warmup, and high-refresh-rate compositor clock boost (`DCompositionBoostCompositorClock` & `timeBeginPeriod`).
