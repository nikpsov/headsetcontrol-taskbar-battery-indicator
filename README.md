[🇷🇺 Русский (Russian)](README.ru.md) | [🇬🇧 English](README.md)

# HeadsetControl Taskbar Battery Indicator

[![Release](https://img.shields.io/github/v/release/nikpsov/headsetcontrol-taskbar-battery-indicator?label=release)](https://github.com/nikpsov/headsetcontrol-taskbar-battery-indicator/releases/latest) [![Tag](https://img.shields.io/github/v/tag/nikpsov/headsetcontrol-taskbar-battery-indicator?label=tag)](https://github.com/nikpsov/headsetcontrol-taskbar-battery-indicator/tags)

A lightweight Windows application that displays a taskbar battery indicator (or overlay) for various wireless gaming headsets.

<!-- Screenshots -->
![Taskbar Indicator](docs/taskbar-indicator.png)
![Taskbar Percentage](docs/taskbar-percentage.png)
![Popup](docs/popup.png)

## Features
- **Taskbar Battery Indicator:** Keeps track of your wireless headset's battery level directly from your taskbar or through a convenient overlay.
- **Multi-Device Support:** Supports a wide range of devices including Logitech, SteelSeries, Corsair, HyperX, and more.
- **Customizable:** Settings to hide when disconnected or change display styles (percentage or icon).
- **Auto-Startup:** Can automatically run when Windows starts.
- **Low Battery Notifications:** Pops up a toast notification when battery is low.

<!-- Screenshots -->
![Settings Context Menu](docs/context-menu.png)

## Installation

1. Go to the [Releases page](https://github.com/nikpsov/headsetcontrol-taskbar-battery-indicator/releases/latest).
2. Download the latest `HeadsetBatteryIndicatorSetup.exe`.
3. Run the installer and follow the instructions.
4. The application will appear in your system tray and taskbar.

## Requirements & Compatibility

- **Supported OS:** Windows 10, Windows 11
- **Supported Devices:** This app relies on the [HeadsetControl](https://github.com/Sapd/HeadsetControl) backend. For a full list of supported headsets, please check the [HeadsetControl Supported Devices list](https://github.com/Sapd/HeadsetControl#supported-headsets). 
*(Note: Support is based on reverse-engineering the USB HID protocol, so some headsets might not be fully supported yet).*

## How it works
This application works by querying your wireless headset's battery level using the underlying `headsetcontrol` backend and rendering the status seamlessly on your Windows interface.

## Acknowledgements and References
This project utilizes or references the following open-source projects to provide its functionality:

- **[HeadsetControl](https://github.com/Sapd/HeadsetControl)** - The core library and tool for retrieving battery status from various headsets.
- **[headset-battery-indicator](https://github.com/aarol/headset-battery-indicator)** - Reference for headset battery logic.
- **[HeadsetControl-SystemTray](https://github.com/zampierilucas/HeadsetControl-SystemTray)** - Reference for Windows system tray integration.
- **[TrafficMonitor](https://github.com/zhongyang219/TrafficMonitor)** - Reference for overlay rendering techniques and UI.
