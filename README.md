# HeadsetControl Taskbar Battery Indicator

A lightweight Windows application that displays a taskbar battery indicator (or overlay) for various wireless gaming headsets.

## Features
- **Taskbar Battery Indicator:** Keeps track of your wireless headset's battery level directly from your taskbar or through a convenient overlay.
- **Multi-Device Support:** Supports a wide range of devices including Logitech, SteelSeries, Corsair, HyperX, and more.
- **Customizable:** Settings to hide when disconnected or change display styles.

## How it works
This application works by querying your wireless headset's battery level using the underlying `headsetcontrol` backend and rendering the status seamlessly on your Windows interface.

## Acknowledgements and References
This project utilizes or references the following open-source projects to provide its functionality:

- **[HeadsetControl](https://github.com/Sapd/HeadsetControl)** - The core library and tool for retrieving battery status from various headsets.
- **[headset-battery-indicator](https://github.com/aarol/headset-battery-indicator)** - Reference for headset battery logic.
- **[HeadsetControl-SystemTray](https://github.com/zampierilucas/HeadsetControl-SystemTray)** - Reference for Windows system tray integration.
- **[TrafficMonitor](https://github.com/zhongyang219/TrafficMonitor)** - Reference for overlay rendering techniques and UI.
