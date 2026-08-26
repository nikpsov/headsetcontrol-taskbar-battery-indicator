using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HeadsetControlTaskbarBatteryIndicator
{
    public class App : Application
    {
        [STAThread]
        public static void Main()
        {
            try
            {
                HeadsetService.Log("Application Main() started");
                var app = new App();
                app.Run(new OverlayWindow());
            }
            catch (Exception ex)
            {
                HeadsetService.Log("Fatal Application Crash: " + ex);
                System.IO.File.WriteAllText("crash.log", ex.ToString());
            }
        }
    }


    public class OverlayWindow : Window
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        const uint WINEVENT_OUTOFCONTEXT = 0;

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
        }
        const int GWLP_HWNDPARENT = -8;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private DispatcherTimer _positionTimer;
        private TextBlock _batteryText;
        private TextBlock _percentChargingBolt;
        private Grid _iconGrid;
        private TextBlock _iconText;
        private TextBlock _iconChargingBadge;
        private StackPanel _stack;
        private Border _containerBorder;

        private Grid _batteryIconContainer;
        private Border _batteryIconBorder;
        private Border _batteryFill;
        private Border _batteryTerminal;
        private Polygon _chargingBolt;
        private TextBlock _batteryCross;

        private WinEventDelegate _winEventProc;
        private IntPtr _hWinEventHook;
        private FlyoutWindow _flyout;

        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private bool _hideWhenDisconnected = true;
        private bool _shouldHideOverlay = false;
        private bool _runOnStartup = false;
        private bool? _lastIsSystemLight = null;

        private int _displayStyle = 0; // 0 = Percent, 1 = Icon

        private HeadsetState _latestState = new HeadsetState();
        private bool _hasWarnedLowBattery = false;

        public bool IsDarkTheme
        {
            get
            {
                try
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        if (key != null)
                        {
                            object val = key.GetValue("SystemUsesLightTheme");
                            return val == null || (int)val == 0;
                        }
                    }
                }
                catch { }
                return true;
            }
        }

        public OverlayWindow()
        {
            LoadSettings();

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            Topmost = true;
            ShowInTaskbar = false;

            Width = 90;
            Height = 36;

            _stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = Brushes.Transparent,
                Margin = new Thickness(6, 0, 6, 0)
            };

            // Headset icon with layered charging indicator
            _iconGrid = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 4, 0)
            };

            _iconText = new TextBlock
            {
                Text = "\uE7F6",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 16,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _iconChargingBadge = new TextBlock
            {
                Text = "\uE945",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 215, 96)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -3, -2),
                Visibility = Visibility.Collapsed
            };

            _iconGrid.Children.Add(_iconText);
            _iconGrid.Children.Add(_iconChargingBadge);

            _batteryText = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                FontSize = 12.5,
                FontWeight = FontWeights.Normal,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Text = "--%",
                Margin = new Thickness(0, 0, 2, 0)
            };

            _percentChargingBolt = new TextBlock
            {
                Text = "\uE945",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 215, 96)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1, 0, 2, 0),
                Visibility = Visibility.Collapsed
            };

            _batteryIconContainer = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 4, 0),
                Visibility = Visibility.Collapsed
            };

            var outerStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            _batteryIconBorder = new Border
            {
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(3),
                Width = 20,
                Height = 11,
                Padding = new Thickness(1)
            };

            _batteryFill = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _batteryIconBorder.Child = _batteryFill;

            _batteryTerminal = new Border
            {
                Background = Brushes.White,
                Width = 1.5,
                Height = 4,
                CornerRadius = new CornerRadius(0, 0.5, 0.5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1, 0, 0, 0)
            };

            outerStack.Children.Add(_batteryIconBorder);
            outerStack.Children.Add(_batteryTerminal);

            _chargingBolt = new Polygon
            {
                Fill = Brushes.Black,
                Points = new PointCollection
                {
                    new Point(3, 0),
                    new Point(0, 5),
                    new Point(3, 5),
                    new Point(2, 9),
                    new Point(6, 4),
                    new Point(3, 4)
                },
                Stretch = Stretch.Fill,
                Width = 5,
                Height = 8,
                Margin = new Thickness(0, 0, 1, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            _batteryCross = new TextBlock
            {
                Text = "\u2715",
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 1, 0),
                Visibility = Visibility.Collapsed
            };

            _batteryIconContainer.Children.Add(outerStack);
            _batteryIconContainer.Children.Add(_chargingBolt);
            _batteryIconContainer.Children.Add(_batteryCross);

            _stack.Children.Add(_iconGrid);
            _stack.Children.Add(_batteryText);
            _stack.Children.Add(_percentChargingBolt);
            _stack.Children.Add(_batteryIconContainer);

            _containerBorder = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)),
                Cursor = Cursors.Hand,
                Margin = new Thickness(2, 0, 2, 0),
                Child = _stack
            };

            Content = _containerBorder;

            _containerBorder.MouseEnter += (s, e) =>
            {
                bool isLight = _lastIsSystemLight.GetValueOrDefault(false);
                _containerBorder.Background = isLight ?
                    new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)) :
                    new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
            };
            _containerBorder.MouseLeave += (s, e) =>
            {
                _containerBorder.Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255));
            };
            this.PreviewMouseLeftButtonDown += (s, e) =>
            {
                bool isLight = _lastIsSystemLight.GetValueOrDefault(false);
                _containerBorder.Background = isLight ?
                    new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)) :
                    new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
            };
            this.PreviewMouseLeftButtonUp += (s, e) =>
            {
                bool isLight = _lastIsSystemLight.GetValueOrDefault(false);
                _containerBorder.Background = isLight ?
                    new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)) :
                    new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
            };
            this.MouseLeftButtonUp += (s, e) => ShowFlyout();
            this.MouseRightButtonUp += (s, e) => ShowContextMenu();

            InitNotifyIcon();

            // Subscribe to in-process HeadsetService
            HeadsetService.Instance.StateChanged += OnHeadsetStateChanged;
            HeadsetService.Instance.Start(15000);

            this.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
                if (taskbar != IntPtr.Zero)
                {
                    SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, taskbar);
                }

                SetupTaskbarHook();
                UpdatePosition();

                _positionTimer = new DispatcherTimer();
                _positionTimer.Interval = TimeSpan.FromMilliseconds(500);
                _positionTimer.Tick += (ts, te) => UpdatePosition();
                _positionTimer.Start();
            };

            this.Closed += (s, e) =>
            {
                HeadsetService.Instance.Stop();
                if (_positionTimer != null) _positionTimer.Stop();
                if (_hWinEventHook != IntPtr.Zero)
                    UnhookWinEvent(_hWinEventHook);
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
            };
        }

        private void OnHeadsetStateChanged(HeadsetState state)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                _latestState = state;
                ApplyHeadsetState(state);
            }));
        }

        private void ApplyHeadsetState(HeadsetState state)
        {
            try
            {
                if (state.IsConnected && state.BatteryLevel >= 0)
                {
                    _shouldHideOverlay = false;

                    if (_displayStyle == 0)
                    {
                        _batteryText.Visibility = Visibility.Visible;
                        _batteryIconContainer.Visibility = Visibility.Collapsed;
                        _batteryText.Text = state.BatteryLevel + "%";
                        _percentChargingBolt.Visibility = state.IsCharging ? Visibility.Visible : Visibility.Collapsed;
                    }
                    else
                    {
                        _batteryText.Visibility = Visibility.Collapsed;
                        _percentChargingBolt.Visibility = Visibility.Collapsed;
                        _batteryIconContainer.Visibility = Visibility.Visible;

                        double maxFillWidth = _batteryIconBorder.Width - _batteryIconBorder.BorderThickness.Left * 2 - _batteryIconBorder.Padding.Left * 2;
                        double fillWidth = maxFillWidth * (state.BatteryLevel / 100.0);
                        if (fillWidth < 0) fillWidth = 0;
                        if (fillWidth > maxFillWidth) fillWidth = maxFillWidth;
                        _batteryFill.Width = fillWidth;

                        _batteryCross.Visibility = Visibility.Collapsed;

                        if (state.IsCharging)
                        {
                            _chargingBolt.Visibility = Visibility.Visible;
                            _batteryFill.Background = new SolidColorBrush(Color.FromRgb(30, 215, 96)); // Green
                        }
                        else
                        {
                            _chargingBolt.Visibility = Visibility.Collapsed;
                            if (state.BatteryLevel <= 20)
                                _batteryFill.Background = new SolidColorBrush(Color.FromRgb(225, 40, 40)); // Red
                            else if (state.BatteryLevel <= 40)
                                _batteryFill.Background = new SolidColorBrush(Color.FromRgb(245, 166, 35)); // Yellow/Orange
                            else
                                _batteryFill.Background = new SolidColorBrush(Color.FromRgb(30, 215, 96)); // Green
                        }
                    }

                    _iconChargingBadge.Visibility = state.IsCharging ? Visibility.Visible : Visibility.Collapsed;

                    this.Visibility = Visibility.Visible;

                    if (_notifyIcon != null)
                    {
                        string tip = string.Format("{0}: {1}%{2}", state.DeviceName, state.BatteryLevel, state.IsCharging ? " (Charging ⚡)" : "");
                        if (tip.Length > 63) tip = tip.Substring(0, 63);
                        _notifyIcon.Text = tip;
                    }

                    if (state.BatteryLevel <= 20 && !_hasWarnedLowBattery && !state.IsCharging)
                    {
                        ShowLowBatteryToast();
                        _hasWarnedLowBattery = true;
                    }
                    else if (state.BatteryLevel > 20 || state.IsCharging)
                    {
                        _hasWarnedLowBattery = false;
                    }
                }
                else
                {
                    _shouldHideOverlay = true;

                    if (_displayStyle == 0)
                    {
                        _batteryText.Visibility = Visibility.Visible;
                        _batteryIconContainer.Visibility = Visibility.Collapsed;
                        _batteryText.Text = "--%";
                        _percentChargingBolt.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        _batteryText.Visibility = Visibility.Collapsed;
                        _percentChargingBolt.Visibility = Visibility.Collapsed;
                        _batteryIconContainer.Visibility = Visibility.Visible;
                        _batteryFill.Width = 0;
                        _chargingBolt.Visibility = Visibility.Collapsed;
                        _batteryCross.Visibility = Visibility.Visible;
                    }

                    _iconChargingBadge.Visibility = Visibility.Collapsed;

                    if (_notifyIcon != null)
                    {
                        string tip = string.Format("{0}: Disconnected", state.DeviceName);
                        if (tip.Length > 63) tip = tip.Substring(0, 63);
                        _notifyIcon.Text = tip;
                    }

                    this.Visibility = _hideWhenDisconnected ? Visibility.Hidden : Visibility.Visible;
                }

                if (_flyout != null && _flyout.IsLoaded)
                {
                    _flyout.UpdateData(state);
                }
            }
            catch { }
        }

        private void ShowFlyout()
        {
            if (_flyout == null || !_flyout.IsLoaded)
            {
                _flyout = new FlyoutWindow(this, _latestState);
                _flyout.Show();
            }
            else
            {
                _flyout.Close();
            }
        }

        private static Style CreateContextMenuStyle(bool isDark)
        {
            var style = new Style(typeof(ContextMenu));
            var template = new ControlTemplate(typeof(ContextMenu));

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, isDark ? new SolidColorBrush(Color.FromArgb(242, 32, 32, 32)) : new SolidColorBrush(Color.FromArgb(245, 252, 252, 252)));
            border.SetValue(Border.BorderBrushProperty, isDark ? new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.PaddingProperty, new Thickness(4));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var itemsPresenter = new FrameworkElementFactory(typeof(ItemsPresenter));
            border.AppendChild(itemsPresenter);

            template.VisualTree = border;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            var dropShadow = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 3,
                Direction = 270,
                Color = Colors.Black,
                Opacity = isDark ? 0.45 : 0.15
            };
            style.Setters.Add(new Setter(UIElement.EffectProperty, dropShadow));

            return style;
        }

        private static Style CreateMenuItemStyle(bool isDark)
        {
            var style = new Style(typeof(MenuItem));
            var template = new ControlTemplate(typeof(MenuItem));

            var border = new FrameworkElementFactory(typeof(Border), "Bd");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.PaddingProperty, new Thickness(8, 6, 12, 6));
            border.SetValue(Border.MarginProperty, new Thickness(0, 1, 0, 1));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var grid = new FrameworkElementFactory(typeof(Grid));

            var col0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col0.SetValue(ColumnDefinition.WidthProperty, new GridLength(20));
            grid.AppendChild(col0);

            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            grid.AppendChild(col1);

            // Checkmark glyph
            var checkGlyph = new FrameworkElementFactory(typeof(TextBlock), "CheckGlyph");
            checkGlyph.SetValue(Grid.ColumnProperty, 0);
            checkGlyph.SetValue(TextBlock.TextProperty, "\uE73E");
            checkGlyph.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"));
            checkGlyph.SetValue(TextBlock.FontSizeProperty, 11.0);
            checkGlyph.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            checkGlyph.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            checkGlyph.SetValue(TextBlock.VisibilityProperty, Visibility.Collapsed);
            checkGlyph.SetValue(TextBlock.ForegroundProperty, isDark ? new SolidColorBrush(Color.FromRgb(96, 205, 255)) : new SolidColorBrush(Color.FromRgb(0, 95, 184)));
            grid.AppendChild(checkGlyph);

            // Header text
            var cp = new FrameworkElementFactory(typeof(ContentPresenter), "HeaderHost");
            cp.SetValue(Grid.ColumnProperty, 1);
            cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            grid.AppendChild(cp);

            border.AppendChild(grid);
            template.VisualTree = border;

            // Trigger for Checked state
            var checkedTrigger = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckGlyph"));
            template.Triggers.Add(checkedTrigger);

            // Trigger for Highlighted / Hover state
            var highlightTrigger = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            highlightTrigger.Setters.Add(new Setter(
                Border.BackgroundProperty,
                isDark ? new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(16, 0, 0, 0)),
                "Bd"));
            template.Triggers.Add(highlightTrigger);

            // Trigger for Pressed state
            var pressedTrigger = new Trigger { Property = MenuItem.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(
                Border.BackgroundProperty,
                isDark ? new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(26, 0, 0, 0)),
                "Bd"));
            template.Triggers.Add(pressedTrigger);

            // Trigger for Disabled state
            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(
                Control.ForegroundProperty,
                isDark ? new SolidColorBrush(Color.FromRgb(128, 128, 128)) : new SolidColorBrush(Color.FromRgb(160, 160, 160))));
            template.Triggers.Add(disabledTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.ForegroundProperty, isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(26, 26, 26))));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif")));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));

            return style;
        }

        private static Separator CreateStyledSeparator(bool isDark)
        {
            return new Separator
            {
                Margin = new Thickness(4, 3, 4, 3),
                Height = 1,
                Background = isDark ? new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)),
                BorderThickness = new Thickness(0)
            };
        }

        private void ShowContextMenu(bool fromTray = false)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetForegroundWindow(hwnd);
            }

            bool isDark = IsDarkTheme;
            var itemStyle = CreateMenuItemStyle(isDark);

            var menu = new ContextMenu
            {
                Style = CreateContextMenuStyle(isDark)
            };

            if (fromTray)
            {
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.PlacementTarget = this;

                var openPanelItem = CreateStyledMenuItem("Open Panel", itemStyle);
                openPanelItem.Click += (s, e) => ShowFlyout();
                menu.Items.Add(openPanelItem);

                menu.Items.Add(CreateStyledSeparator(isDark));
            }
            else
            {
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                menu.PlacementTarget = this;
                menu.VerticalOffset = -6;
            }

            var styleItem = CreateStyledMenuItem("Display Style: " + (_displayStyle == 0 ? "Percentage (85%)" : "Battery Icon"), itemStyle);
            styleItem.Click += (s, e) =>
            {
                _displayStyle = _displayStyle == 0 ? 1 : 0;
                SaveSettings();
                ApplyHeadsetState(_latestState);
                UpdateTheme();
            };
            menu.Items.Add(styleItem);

            var hideItem = CreateStyledMenuItem("Hide when disconnected", itemStyle, true, _hideWhenDisconnected);
            hideItem.Click += (s, e) =>
            {
                _hideWhenDisconnected = hideItem.IsChecked;
                SaveSettings();
                ApplyHeadsetState(_latestState);
            };
            menu.Items.Add(hideItem);

            var startupItem = CreateStyledMenuItem("Run on startup", itemStyle, true, _runOnStartup);
            startupItem.Click += (s, e) =>
            {
                _runOnStartup = startupItem.IsChecked;
                SetRunOnStartup(_runOnStartup);
            };
            menu.Items.Add(startupItem);

            menu.Items.Add(CreateStyledSeparator(isDark));

            var refreshItem = CreateStyledMenuItem("Refresh Device Info", itemStyle);
            refreshItem.Click += (s, e) => HeadsetService.Instance.ForcePoll();
            menu.Items.Add(refreshItem);

            var exitItem = CreateStyledMenuItem("Exit", itemStyle);
            exitItem.Click += (s, e) => Application.Current.Shutdown();
            menu.Items.Add(exitItem);

            menu.IsOpen = true;
        }

        private MenuItem CreateStyledMenuItem(string text, Style style, bool isCheckable = false, bool isChecked = false)
        {
            var item = new MenuItem
            {
                Header = text,
                Style = style,
                IsCheckable = isCheckable,
                IsChecked = isChecked
            };
            return item;
        }

        private void SetupTaskbarHook()
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero)
            {
                uint processId;
                uint threadId = GetWindowThreadProcessId(taskbar, out processId);
                if (threadId != 0)
                {
                    _winEventProc = new WinEventDelegate(WinEventCallback);
                    _hWinEventHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _winEventProc, processId, threadId, WINEVENT_OUTOFCONTEXT);
                }
            }
        }

        private void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            UpdatePosition();
        }

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private bool IsForegroundFullscreen()
        {
            IntPtr fgWnd = GetForegroundWindow();
            if (fgWnd == IntPtr.Zero) return false;

            IntPtr desktop = FindWindow("Progman", null);
            IntPtr shell = FindWindow("WorkerW", null);
            if (fgWnd == desktop || fgWnd == shell) return false;

            RECT appBounds;
            GetWindowRect(fgWnd, out appBounds);

            IntPtr hMonitor = MonitorFromWindow(fgWnd, MONITOR_DEFAULTTONEAREST);
            if (hMonitor == IntPtr.Zero) return false;

            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(mi);
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                return appBounds.Bottom == mi.rcMonitor.Bottom &&
                       appBounds.Top == mi.rcMonitor.Top &&
                       appBounds.Left == mi.rcMonitor.Left &&
                       appBounds.Right == mi.rcMonitor.Right;
            }
            return false;
        }

        private int _lastX = -1;
        private int _lastY = -1;

        public void UpdatePosition()
        {
            UpdateTheme();

            if (IsForegroundFullscreen() || (_shouldHideOverlay && _hideWhenDisconnected))
            {
                if (this.Visibility != Visibility.Hidden)
                    this.Visibility = Visibility.Hidden;
                return;
            }
            else
            {
                if (this.Visibility != Visibility.Visible)
                    this.Visibility = Visibility.Visible;
            }

            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero)
            {
                IntPtr trayNotify = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
                if (trayNotify != IntPtr.Zero)
                {
                    RECT rect;
                    if (GetWindowRect(trayNotify, out rect))
                    {
                        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

                        var source = System.Windows.PresentationSource.FromVisual(this);
                        double dpiX = source != null ? source.CompositionTarget.TransformToDevice.M11 : 1.0;
                        double dpiY = source != null ? source.CompositionTarget.TransformToDevice.M22 : 1.0;

                        int physicalWidth = (int)(this.Width * dpiX);
                        int physicalHeight = (int)(this.Height * dpiY);

                        int x = rect.Left - physicalWidth;
                        int taskbarHeight = rect.Bottom - rect.Top;
                        int y = rect.Top + (taskbarHeight - physicalHeight) / 2;

                        if (x != _lastX || y != _lastY)
                        {
                            _lastX = x;
                            _lastY = y;
                            SetWindowPos(hwnd, HWND_TOPMOST, x, y, physicalWidth, physicalHeight, 0x0010); // NOACTIVATE
                        }
                        if (this.Visibility == Visibility.Visible)
                        {
                            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002);
                        }
                    }
                }
            }
        }

        private void UpdateTheme()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("SystemUsesLightTheme");
                        bool isLight = val != null && (int)val == 1;

                        if (_lastIsSystemLight != isLight)
                        {
                            _lastIsSystemLight = isLight;
                            var brush = isLight ? Brushes.Black : Brushes.White;

                            _batteryText.Foreground = brush;
                            _iconText.Foreground = brush;

                            if (_batteryIconBorder != null)
                            {
                                _batteryIconBorder.BorderBrush = brush;
                                _batteryTerminal.Background = brush;
                                _batteryCross.Foreground = brush;
                                _chargingBolt.Fill = isLight ? Brushes.White : Brushes.Black;

                                // Re-apply fill colors - always use colored fills in Win11 style
                                if (_latestState.IsConnected && _latestState.BatteryLevel >= 0)
                                    ApplyHeadsetState(_latestState);
                            }

                            UpdateTrayIconTheme(isLight);
                        }
                    }
                }
            }
            catch { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        extern static bool DestroyIcon(IntPtr handle);

        private void UpdateTrayIconTheme(bool isLight)
        {
            try
            {
                using (var bmp = new System.Drawing.Bitmap(16, 16))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    var pen = new System.Drawing.Pen(isLight ? System.Drawing.Color.Black : System.Drawing.Color.White, 1.5f);
                    g.DrawArc(pen, 2, 2, 11, 11, 180, 180);
                    g.FillRectangle(new System.Drawing.SolidBrush(isLight ? System.Drawing.Color.Black : System.Drawing.Color.White), 1, 7, 3, 6);
                    g.FillRectangle(new System.Drawing.SolidBrush(isLight ? System.Drawing.Color.Black : System.Drawing.Color.White), 11, 7, 3, 6);

                    IntPtr hIcon = bmp.GetHicon();
                    _notifyIcon.Icon = System.Drawing.Icon.FromHandle(hIcon);
                }
            }
            catch { }
        }

        private void InitNotifyIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Text = "Headset Battery Indicator";
            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenuStrip = null;

            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    this.Dispatcher.Invoke(() => ShowFlyout());
                }
                else if (e.Button == System.Windows.Forms.MouseButtons.Right)
                {
                    this.Dispatcher.Invoke(() => ShowContextMenu(true));
                }
            };

            UpdateTrayIconTheme(!IsDarkTheme);
        }

        private void ShowLowBatteryToast()
        {
            var notif = new NotificationWindow("Low Headset Battery", string.Format("{0}% remaining on {1}.", _latestState.BatteryLevel, _latestState.DeviceName));
            notif.Show();
        }

        private void LoadSettings()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\HeadsetControlTaskbarBatteryIndicator"))
                {
                    object val = key.GetValue("HideWhenDisconnected");
                    if (val != null)
                        _hideWhenDisconnected = (int)val == 1;

                    object styleVal = key.GetValue("DisplayStyle");
                    if (styleVal != null)
                        _displayStyle = (int)styleVal;
                }
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("HeadsetControlTaskbarBatteryIndicator");
                        _runOnStartup = val != null;
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\HeadsetControlTaskbarBatteryIndicator"))
                {
                    key.SetValue("HideWhenDisconnected", _hideWhenDisconnected ? 1 : 0);
                    key.SetValue("DisplayStyle", _displayStyle);
                }
            }
            catch { }
        }

        private void SetRunOnStartup(bool enable)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                            key.SetValue("HeadsetControlTaskbarBatteryIndicator", "\"" + appPath + "\"");
                        }
                        else
                        {
                            key.DeleteValue("HeadsetControlTaskbarBatteryIndicator", false);
                        }
                    }
                }
            }
            catch { }
        }
    }

    public class FlyoutWindow : Window
    {
        private TextBlock _titleText;
        private TextBlock _batteryPercentText;
        private Border _chargingBadge;
        private Border _chargingPillBorder;
        private TextBlock _chargingPillText;
        private Grid _batteryProgressBar;
        private Border _batteryProgressFill;
        private TextBlock _timeText;
        private TextBlock _voltageText;

        private Grid _mainView;
        private StackPanel _settingsView;
        private TextBlock _statusFeedbackText;

        public FlyoutWindow(OverlayWindow owner, HeadsetState state)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            Topmost = true;
            ShowInTaskbar = false;
            Width = 300;
            SizeToContent = SizeToContent.Height;

            // Position above taskbar
            this.Left = owner.Left + owner.Width / 2 - this.Width / 2;
            this.Top = owner.Top - 120;

            bool isDark = true;
            OverlayWindow ow = owner as OverlayWindow;
            if (ow != null)
            {
                isDark = ow.IsDarkTheme;
            }

            var border = new Border
            {
                Background = new SolidColorBrush(isDark ? Color.FromArgb(245, 28, 28, 28) : Color.FromArgb(245, 245, 245, 245)),
                BorderBrush = new SolidColorBrush(isDark ? Color.FromArgb(120, 80, 80, 80) : Color.FromArgb(120, 200, 200, 200)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(4)
            };

            var rootGrid = new Grid();

            // -------------------------------------------------------------
            // 1. MAIN VIEW (Headset icon + Info + Gear icon on bottom-right)
            // -------------------------------------------------------------
            _mainView = new Grid();
            _mainView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            _mainView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _mainView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

            // Icon container with layered charging badge
            var iconContainer = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var icon = new TextBlock
            {
                Text = "\uE7F6",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 38,
                Foreground = isDark ? Brushes.White : Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            iconContainer.Children.Add(icon);

            _chargingBadge = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromRgb(30, 215, 96)),
                BorderBrush = new SolidColorBrush(isDark ? Color.FromRgb(28, 28, 28) : Color.FromRgb(245, 245, 245)),
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 2, 2),
                Visibility = Visibility.Collapsed
            };
            var badgeIcon = new TextBlock
            {
                Text = "\uE945",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _chargingBadge.Child = badgeIcon;
            iconContainer.Children.Add(_chargingBadge);

            Grid.SetColumn(iconContainer, 0);
            _mainView.Children.Add(iconContainer);

            var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) };

            _titleText = new TextBlock
            {
                Text = state.DeviceName,
                Foreground = isDark ? Brushes.White : Brushes.Black,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 2)
            };

            var batteryRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 4)
            };

            _batteryPercentText = new TextBlock
            {
                Foreground = isDark ? Brushes.White : Brushes.Black,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Text = "--%"
            };
            batteryRow.Children.Add(_batteryPercentText);

            _chargingPillBorder = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(isDark ? Color.FromArgb(40, 30, 215, 96) : Color.FromArgb(30, 16, 124, 65)),
                BorderBrush = new SolidColorBrush(isDark ? Color.FromArgb(120, 30, 215, 96) : Color.FromArgb(100, 16, 124, 65)),
                BorderThickness = new Thickness(1),
                Visibility = Visibility.Collapsed
            };
            _chargingPillText = new TextBlock
            {
                Text = "⚡ Charging",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(isDark ? Color.FromRgb(50, 230, 110) : Color.FromRgb(16, 124, 65))
            };
            _chargingPillBorder.Child = _chargingPillText;
            batteryRow.Children.Add(_chargingPillBorder);

            // Battery Level Progress Bar
            _batteryProgressBar = new Grid
            {
                Height = 5,
                Margin = new Thickness(0, 0, 0, 4),
                Visibility = Visibility.Collapsed
            };
            var barBg = new Border
            {
                CornerRadius = new CornerRadius(2.5),
                Background = isDark ? new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0))
            };
            _batteryProgressFill = new Border
            {
                CornerRadius = new CornerRadius(2.5),
                Background = new SolidColorBrush(Color.FromRgb(30, 215, 96)),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0
            };
            _batteryProgressBar.Children.Add(barBg);
            _batteryProgressBar.Children.Add(_batteryProgressFill);

            _timeText = new TextBlock
            {
                Foreground = isDark ? new SolidColorBrush(Color.FromRgb(165, 165, 165)) : Brushes.Gray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 2)
            };

            _voltageText = new TextBlock
            {
                Foreground = isDark ? new SolidColorBrush(Color.FromRgb(140, 140, 140)) : Brushes.Gray,
                FontSize = 10,
                Visibility = Visibility.Collapsed
            };

            infoPanel.Children.Add(_titleText);
            infoPanel.Children.Add(batteryRow);
            infoPanel.Children.Add(_batteryProgressBar);
            infoPanel.Children.Add(_timeText);
            infoPanel.Children.Add(_voltageText);

            Grid.SetColumn(infoPanel, 1);
            _mainView.Children.Add(infoPanel);

            // Gear icon at bottom right of the main card
            var gearBtn = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.Hand,
                ToolTip = "Sleep Timer Settings",
                Background = Brushes.Transparent
            };
            var gearIcon = new TextBlock
            {
                Text = "\uE713",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 13,
                Foreground = isDark ? new SolidColorBrush(Color.FromRgb(150, 150, 150)) : Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            gearBtn.Child = gearIcon;

            gearBtn.MouseEnter += (s, e) =>
            {
                gearBtn.Background = isDark ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
                gearIcon.Foreground = isDark ? Brushes.White : Brushes.Black;
            };
            gearBtn.MouseLeave += (s, e) =>
            {
                gearBtn.Background = Brushes.Transparent;
                gearIcon.Foreground = isDark ? new SolidColorBrush(Color.FromRgb(150, 150, 150)) : Brushes.Gray;
            };

            gearBtn.MouseLeftButtonUp += (s, e) =>
            {
                _mainView.Visibility = Visibility.Collapsed;
                _settingsView.Visibility = Visibility.Visible;
                this.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    this.Top = owner.Top - this.ActualHeight - 8;
                }));
            };

            Grid.SetColumn(gearBtn, 2);
            _mainView.Children.Add(gearBtn);

            rootGrid.Children.Add(_mainView);

            // -------------------------------------------------------------
            // 2. SETTINGS VIEW (Back button + Auto-sleep Timer options)
            // -------------------------------------------------------------
            _settingsView = new StackPanel { Visibility = Visibility.Collapsed };

            var settingsHeader = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            settingsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            settingsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var backBtn = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand,
                ToolTip = "Back",
                Background = Brushes.Transparent
            };
            var backIcon = new TextBlock
            {
                Text = "\uE72B",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = isDark ? Brushes.White : Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            backBtn.Child = backIcon;

            backBtn.MouseEnter += (s, e) =>
            {
                backBtn.Background = isDark ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
            };
            backBtn.MouseLeave += (s, e) =>
            {
                backBtn.Background = Brushes.Transparent;
            };

            backBtn.MouseLeftButtonUp += (s, e) =>
            {
                _settingsView.Visibility = Visibility.Collapsed;
                _statusFeedbackText.Visibility = Visibility.Collapsed;
                _mainView.Visibility = Visibility.Visible;
                this.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    this.Top = owner.Top - this.ActualHeight - 8;
                }));
            };

            Grid.SetColumn(backBtn, 0);
            settingsHeader.Children.Add(backBtn);

            var settingsTitle = new TextBlock
            {
                Text = "Inactive Sleep Timer",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = isDark ? Brushes.White : Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(settingsTitle, 1);
            settingsHeader.Children.Add(settingsTitle);

            _settingsView.Children.Add(settingsHeader);

            var sleepButtons = new System.Windows.Controls.Primitives.UniformGrid { Columns = 5, Margin = new Thickness(0, 2, 0, 0) };
            int[] timeouts = new int[] { 0, 5, 15, 30, 60 };
            string[] timeoutLabels = new string[] { "Off", "5m", "15m", "30m", "1h" };

            for (int i = 0; i < timeouts.Length; i++)
            {
                byte mins = (byte)timeouts[i];
                string label = timeoutLabels[i];

                var btnBorder = new Border
                {
                    Height = 26,
                    Margin = new Thickness(2, 0, 2, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = isDark ? new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)),
                    BorderBrush = isDark ? new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };

                var btnText = new TextBlock
                {
                    Text = label,
                    FontSize = 11,
                    FontWeight = FontWeights.Medium,
                    Foreground = isDark ? Brushes.White : Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                btnBorder.Child = btnText;

                btnBorder.MouseEnter += (s, e) =>
                {
                    btnBorder.Background = isDark ? new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
                    btnBorder.BorderBrush = isDark ? new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(60, 0, 0, 0));
                };
                btnBorder.MouseLeave += (s, e) =>
                {
                    btnBorder.Background = isDark ? new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0));
                    btnBorder.BorderBrush = isDark ? new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
                };

                btnBorder.MouseLeftButtonUp += (s, e) =>
                {
                    bool ok = HeadsetService.Instance.SetInactiveTime(mins);
                    if (ok)
                    {
                        _statusFeedbackText.Text = string.Format("✓ Sleep timer set to {0}", mins == 0 ? "Off" : label);
                        _statusFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(40, 190, 90));
                    }
                    else
                    {
                        _statusFeedbackText.Text = "✕ Failed to set sleep timer";
                        _statusFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(230, 60, 60));
                    }
                    _statusFeedbackText.Visibility = Visibility.Visible;
                };

                sleepButtons.Children.Add(btnBorder);
            }
            _settingsView.Children.Add(sleepButtons);

            _statusFeedbackText = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            _settingsView.Children.Add(_statusFeedbackText);

            rootGrid.Children.Add(_settingsView);

            border.Child = rootGrid;
            Content = border;

            UpdateData(state);

            this.Loaded += (s, e) =>
            {
                this.Top = owner.Top - this.ActualHeight - 8;
            };

            this.Deactivated += (s, e) => this.Close();
        }

        private static double GetModelMaxBatteryHours(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return 50.0;
            string lower = deviceName.ToLowerInvariant();
            if (lower.Contains("pro x 2") || lower.Contains("0x0af7")) return 50.0;
            if (lower.Contains("alpha wireless")) return 300.0;
            if (lower.Contains("cloud 3") || lower.Contains("cloud iii")) return 120.0;
            if (lower.Contains("cloud 2") || lower.Contains("cloud ii") || lower.Contains("flight")) return 30.0;
            if (lower.Contains("nova 7")) return 38.0;
            if (lower.Contains("nova 5")) return 60.0;
            if (lower.Contains("nova pro")) return 22.0;
            if (lower.Contains("nova 3")) return 36.0;
            if (lower.Contains("arctis 7+") || lower.Contains("arctis 7 plus")) return 30.0;
            if (lower.Contains("arctis 7")) return 24.0;
            if (lower.Contains("arctis 9")) return 20.0;
            if (lower.Contains("arctis 1")) return 20.0;
            if (lower.Contains("g733")) return 29.0;
            if (lower.Contains("g535")) return 33.0;
            if (lower.Contains("g533") || lower.Contains("g933")) return 15.0;
            if (lower.Contains("g935")) return 18.0;
            if (lower.Contains("virtuoso")) return 20.0;
            if (lower.Contains("void")) return 16.0;
            if (lower.Contains("maxwell")) return 80.0;
            return 50.0;
        }

        public void UpdateData(HeadsetState state)
        {
            _titleText.Text = state.DeviceName;

            if (!state.IsConnected || state.BatteryLevel < 0)
            {
                _batteryPercentText.Text = "--";
                _chargingBadge.Visibility = Visibility.Collapsed;
                _chargingPillBorder.Visibility = Visibility.Collapsed;
                _batteryProgressBar.Visibility = Visibility.Collapsed;
                _timeText.Text = "Headset is disconnected or sleeping\nTurn on headset or plug in USB receiver";
                _voltageText.Visibility = Visibility.Collapsed;
                return;
            }

            _batteryPercentText.Text = state.BatteryLevel + "%";
            _batteryProgressBar.Visibility = Visibility.Visible;

            double maxBarWidth = 190.0;
            double fillWidth = maxBarWidth * (state.BatteryLevel / 100.0);
            if (fillWidth < 0) fillWidth = 0;
            if (fillWidth > maxBarWidth) fillWidth = maxBarWidth;
            _batteryProgressFill.Width = fillWidth;

            if (state.IsCharging)
            {
                _chargingBadge.Visibility = Visibility.Visible;
                _chargingPillBorder.Visibility = Visibility.Visible;
                _chargingPillText.Text = "⚡ Charging";
                _batteryProgressFill.Background = new SolidColorBrush(Color.FromRgb(30, 215, 96));

                if (state.TimeToFullMin > 0)
                    _timeText.Text = string.Format("Time to full: ~{0}h {1}m", state.TimeToFullMin / 60, state.TimeToFullMin % 60);
                else
                    _timeText.Text = "⚡ Charging via USB...";
            }
            else
            {
                _chargingBadge.Visibility = Visibility.Collapsed;
                _chargingPillBorder.Visibility = Visibility.Collapsed;

                if (state.BatteryLevel <= 20)
                    _batteryProgressFill.Background = new SolidColorBrush(Color.FromRgb(225, 40, 40));
                else if (state.BatteryLevel <= 40)
                    _batteryProgressFill.Background = new SolidColorBrush(Color.FromRgb(245, 166, 35));
                else
                    _batteryProgressFill.Background = new SolidColorBrush(Color.FromRgb(30, 215, 96));

                if (state.TimeToEmptyMin > 0)
                {
                    _timeText.Text = string.Format("Approx. {0}h {1}m remaining", state.TimeToEmptyMin / 60, state.TimeToEmptyMin % 60);
                }
                else
                {
                    double maxHours = GetModelMaxBatteryHours(state.DeviceName);
                    double estimatedHours = maxHours * (state.BatteryLevel / 100.0);
                    int hours = (int)estimatedHours;
                    int minutes = (int)((estimatedHours - hours) * 60);
                    _timeText.Text = string.Format("Approx. {0}h {1}m remaining", hours, minutes);
                }
            }

            if (state.VoltageMv > 0)
            {
                _voltageText.Text = string.Format("Battery Voltage: {0} mV", state.VoltageMv);
                _voltageText.Visibility = Visibility.Visible;
            }
            else
            {
                _voltageText.Visibility = Visibility.Collapsed;
            }
        }
    }

    public class NotificationWindow : Window
    {
        public NotificationWindow(string title, string message)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            Topmost = true;
            ShowInTaskbar = false;
            Width = 320;
            Height = 90;

            this.Left = SystemParameters.WorkArea.Right - this.Width - 10;
            this.Top = SystemParameters.WorkArea.Bottom - this.Height - 10;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(240, 30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(100, 100, 100, 100)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleText = new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var msgText = new TextBlock
            {
                Text = message,
                Foreground = Brushes.LightGray,
                FontSize = 14
            };

            stack.Children.Add(titleText);
            stack.Children.Add(msgText);

            border.Child = stack;
            Content = border;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                this.Close();
            };
            timer.Start();

            this.MouseLeftButtonUp += (s, e) => this.Close();
        }
    }
}
