[🇷🇺 Русский (Russian)](README.ru.md) | [🇬🇧 English](README.md)

# HeadsetControl Taskbar Battery Indicator

[![Release](https://img.shields.io/github/v/release/nikpsov/headsetcontrol-taskbar-battery-indicator?label=релиз)](https://github.com/nikpsov/headsetcontrol-taskbar-battery-indicator/releases/latest) [![Tag](https://img.shields.io/github/v/tag/nikpsov/headsetcontrol-taskbar-battery-indicator?label=тег)](https://github.com/nikpsov/headsetcontrol-taskbar-battery-indicator/tags)

Легковесное приложение для Windows, которое отображает уровень заряда батареи различных беспроводных игровых гарнитур прямо на панели задач или в виде удобного оверлея.

<!-- Скриншоты -->
![Taskbar Indicator](docs/taskbar-indicator.png)
![Taskbar Percentage](docs/taskbar-percentage.png)
![Popup](docs/popup.png)

## Особенности
- **Индикатор на панели задач:** Отслеживайте уровень заряда вашей беспроводной гарнитуры прямо на панели задач или через удобный оверлей.
- **Поддержка множества устройств:** Поддерживает широкий спектр гарнитур, включая устройства от Logitech, SteelSeries, Corsair, HyperX и других.
- **Настраиваемость:** Возможность скрывать индикатор при отключении гарнитуры или менять стиль отображения (проценты или иконка).
- **Автозагрузка:** Может автоматически запускаться вместе с Windows.
- **Уведомления:** Показывает всплывающее уведомление (toast) при низком заряде батареи.

<!-- Скриншоты -->
![Settings Context Menu](docs/context-menu.png)

## Установка

1. Перейдите на страницу [Релизов (Releases)](https://github.com/nikpsov/headsetcontrol-taskbar-battery-indicator/releases/latest).
2. Скачайте последнюю версию файла `HeadsetBatteryIndicatorSetup.exe`.
3. Запустите установщик и следуйте инструкциям на экране.
4. После установки приложение появится в системном трее и на панели задач.

## Требования и совместимость

- **Поддерживаемые ОС:** Windows 10, Windows 11
- **Поддерживаемые устройства:** Это приложение работает на базе утилиты [HeadsetControl](https://github.com/Sapd/HeadsetControl). Полный список поддерживаемых гарнитур можно найти на [странице поддерживаемых устройств HeadsetControl](https://github.com/Sapd/HeadsetControl#supported-headsets). 
*(Примечание: поддержка реализована с помощью реверс-инжиниринга протокола USB HID, поэтому некоторые гарнитуры могут поддерживаться не полностью, или могут не работать, даже если указаны в списке).*

## Как это работает
Приложение работает за счет запроса уровня заряда батареи вашей гарнитуры с помощью встроенного движка `headsetcontrol` и бесшовного вывода полученного статуса в интерфейсе Windows.

## Благодарности и ссылки
В этом проекте используются или упоминаются следующие open-source проекты, которые обеспечивают его функциональность:

- **[HeadsetControl](https://github.com/Sapd/HeadsetControl)** - Основная библиотека и инструмент для получения состояния батареи с различных гарнитур.
- **[headset-battery-indicator](https://github.com/aarol/headset-battery-indicator)** - Референс логики работы с батареей гарнитуры.
- **[HeadsetControl-SystemTray](https://github.com/zampierilucas/HeadsetControl-SystemTray)** - Референс интеграции с системным треем Windows.
- **[TrafficMonitor](https://github.com/zhongyang219/TrafficMonitor)** - Референс для техник рендеринга оверлея и UI.
