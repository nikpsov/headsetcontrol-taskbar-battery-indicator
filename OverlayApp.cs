using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;

namespace HeadsetControlTaskbarBatteryIndicator
{
    public class App : Application
    {
        [STAThread]
        public static void Main()
        {
            try
            {
                var app = new App();
                app.Run(new OverlayWindow());
            }
            catch (Exception ex)
            {
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

        private DispatcherTimer _batteryTimer;
        private DispatcherTimer _positionTimer;
        private TextBlock _batteryText;
        private TextBlock _iconText;
        private StackPanel _stack;
        
        private Grid _batteryIconContainer;
        private Border _batteryIconBorder;
        private Border _batteryFill;
        private System.Windows.Shapes.Rectangle _batteryTerminal;
        private System.Windows.Shapes.Polygon _chargingBolt;
        private TextBlock _batteryCross;

        private WinEventDelegate _winEventProc;
        private IntPtr _hWinEventHook;
        private FlyoutWindow _flyout;
        
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private bool _hideWhenDisconnected = true;
        private bool _shouldHideOverlay = false;
        private bool _runOnStartup = false;
        private bool? _lastIsSystemLight = null;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        extern static bool DestroyIcon(IntPtr handle);
        
        private int _displayStyle = 0; // 0 = Percent, 1 = Icon

        private int _currentBattery = -1;
        private string _currentModel = "Loading...";
        private bool _isCharging = false;
        private bool _hasWarnedLowBattery = false;
        private int _ticksSinceLastUpdate = 0;

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
                    if (enable)
                    {
                        string path = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                        key.SetValue("HeadsetControlTaskbarBatteryIndicator", "\"" + path + "\"");
                    }
                    else
                    {
                        key.DeleteValue("HeadsetControlTaskbarBatteryIndicator", false);
                    }
                }
            }
            catch { }
        }

        private void UpdateContextMenu()
        {
            var contextMenu = new ContextMenu();
            
            var hideItem = new MenuItem { Header = "Hide when disconnected", IsChecked = _hideWhenDisconnected };
            hideItem.Click += (s, e) => {
                _hideWhenDisconnected = !_hideWhenDisconnected;
                SaveSettings();
                UpdateContextMenu();
                UpdateTrayMenu();
                UpdateBattery();
            };

            var stylePercentItem = new MenuItem { Header = "Show percentage", IsChecked = _displayStyle == 0 };
            stylePercentItem.Click += (s, e) => {
                _displayStyle = 0;
                SaveSettings();
                UpdateContextMenu();
                UpdateTrayMenu();
                UpdateBattery();
            };

            var styleIconItem = new MenuItem { Header = "Show as icon", IsChecked = _displayStyle == 1 };
            styleIconItem.Click += (s, e) => {
                _displayStyle = 1;
                SaveSettings();
                UpdateContextMenu();
                UpdateTrayMenu();
                UpdateBattery();
            };

            var autorunItem = new MenuItem { Header = "Run at Windows startup", IsChecked = _runOnStartup };
            autorunItem.Click += (s, e) => {
                _runOnStartup = !_runOnStartup;
                SetRunOnStartup(_runOnStartup);
                UpdateContextMenu();
                UpdateTrayMenu();
            };

            var exitItem = new MenuItem { Header = "Exit" };
            exitItem.Click += (s, e) => Application.Current.Shutdown();

            contextMenu.Items.Add(hideItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(stylePercentItem);
            contextMenu.Items.Add(styleIconItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(autorunItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(exitItem);

            _stack.ContextMenu = contextMenu;
        }

        private void UpdateTrayIconTheme(bool isSystemLight)
        {
            if (_notifyIcon == null) return;
            if (_lastIsSystemLight.HasValue && _lastIsSystemLight.Value == isSystemLight) return;
            
            _lastIsSystemLight = isSystemLight;
            
            try
            {
                using (var bitmap = new System.Drawing.Bitmap(32, 32))
                {
                    using (var g = System.Drawing.Graphics.FromImage(bitmap))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        // Avoid GDI+ dark fringe when drawing white text on transparent background
                        var clearColor = isSystemLight ? System.Drawing.Color.FromArgb(0, 0, 0, 0) : System.Drawing.Color.FromArgb(0, 255, 255, 255);
                        g.Clear(clearColor);
                        using (var font = new System.Drawing.Font("Segoe Fluent Icons", 26, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel))
                        {
                            var fontToUse = font.Name == "Segoe Fluent Icons" ? font : new System.Drawing.Font("Segoe MDL2 Assets", 26, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
                            var brush = isSystemLight ? System.Drawing.Brushes.Black : System.Drawing.Brushes.White;
                            
                            var format = new System.Drawing.StringFormat 
                            { 
                                Alignment = System.Drawing.StringAlignment.Center, 
                                LineAlignment = System.Drawing.StringAlignment.Center 
                            };
                            g.DrawString("\uE7F6", fontToUse, brush, new System.Drawing.RectangleF(0, 0, 32, 32), format);
                            if (fontToUse != font) fontToUse.Dispose();
                        }
                    }
                    IntPtr hIcon = bitmap.GetHicon();
                    var oldIcon = _notifyIcon.Icon;
                    _notifyIcon.Icon = System.Drawing.Icon.FromHandle(hIcon);
                    
                    if (oldIcon != null)
                    {
                        DestroyIcon(oldIcon.Handle);
                        oldIcon.Dispose();
                    }
                }
            }
            catch
            {
                try { _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); }
                catch { _notifyIcon.Icon = System.Drawing.SystemIcons.Application; }
            }
        }

        private void SetupTrayIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            UpdateTrayIconTheme(true); // default to something, updated on next frame
            _notifyIcon.Text = "HeadsetControl Taskbar Battery Indicator";
            _notifyIcon.Visible = true;
            _notifyIcon.MouseUp += (s, e) => {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                    ShowFlyout();
            };
            
            UpdateTrayMenu();
        }

        private void UpdateTrayMenu()
        {
            var menu = new System.Windows.Forms.ContextMenu();
            
            var hideItem = new System.Windows.Forms.MenuItem("Hide when disconnected");
            hideItem.Checked = _hideWhenDisconnected;
            hideItem.Click += (s, e) => {
                _hideWhenDisconnected = !_hideWhenDisconnected;
                SaveSettings();
                UpdateContextMenu();
                UpdateTrayMenu();
                UpdateBattery();
            };

            var stylePercentItem = new System.Windows.Forms.MenuItem("Show percentage");
            stylePercentItem.Checked = _displayStyle == 0;
            stylePercentItem.Click += (s, e) => {
                _displayStyle = 0;
                SaveSettings();
                UpdateContextMenu();
                UpdateTrayMenu();
                UpdateBattery();
            };

            var styleIconItem = new System.Windows.Forms.MenuItem("Show as icon");
            styleIconItem.Checked = _displayStyle == 1;
            styleIconItem.Click += (s, e) => {
                _displayStyle = 1;
                SaveSettings();
                UpdateContextMenu();
                UpdateTrayMenu();
                UpdateBattery();
            };

            var autorunItem = new System.Windows.Forms.MenuItem("Run at Windows startup");
            autorunItem.Checked = _runOnStartup;
            autorunItem.Click += (s, e) => {
                _runOnStartup = !_runOnStartup;
                SetRunOnStartup(_runOnStartup);
                UpdateContextMenu();
                UpdateTrayMenu();
            };

            var exitItem = new System.Windows.Forms.MenuItem("Exit");
            exitItem.Click += (s, e) => Application.Current.Shutdown();

            menu.MenuItems.Add(hideItem);
            menu.MenuItems.Add("-");
            menu.MenuItems.Add(stylePercentItem);
            menu.MenuItems.Add(styleIconItem);
            menu.MenuItems.Add("-");
            menu.MenuItems.Add(autorunItem);
            menu.MenuItems.Add("-");
            menu.MenuItems.Add(exitItem);

            _notifyIcon.ContextMenu = menu;
        }
        public bool IsDarkTheme 
        { 
            get { return _iconText != null && _iconText.Foreground.ToString() == "#FFFFFFFF"; } 
        }

        public OverlayWindow()
        {
            LoadSettings();
            
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            Topmost = true;
            ShowInTaskbar = false;
            Width = 85;
            Height = 36; // Windows 11 taskbar icons are 36px tall (centered in 48px taskbar)

            _stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = Brushes.Transparent,
                Margin = new Thickness(8, 0, 8, 0) // Padding inside the hover box
            };
            
            UpdateContextMenu();
            SetupTrayIcon();

            _iconText = new TextBlock
            {
                Text = "\uE7F6",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 16,
                Foreground = Brushes.Black,
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            _batteryText = new TextBlock
            {
                Text = "--%",
                Foreground = Brushes.Black,
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };

            _batteryIconContainer = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0), Visibility = Visibility.Collapsed };
            
            var outerStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            
            _batteryIconBorder = new Border 
            { 
                BorderThickness = new Thickness(1.5), 
                CornerRadius = new CornerRadius(3), 
                Width = 20, 
                Height = 11, 
                Padding = new Thickness(1) 
            };
            
            _batteryFill = new Border 
            { 
                HorizontalAlignment = HorizontalAlignment.Left, 
                CornerRadius = new CornerRadius(1.5) 
            };
            _batteryIconBorder.Child = _batteryFill;
            
            _batteryTerminal = new System.Windows.Shapes.Rectangle 
            { 
                Width = 1.5, 
                Height = 4, 
                RadiusX = 0.5, 
                RadiusY = 0.5,
                Margin = new Thickness(1, 0, 0, 0)
            };
            
            outerStack.Children.Add(_batteryIconBorder);
            outerStack.Children.Add(_batteryTerminal);
            
            _chargingBolt = new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection(new[] { new Point(3,0), new Point(0,5), new Point(3,5), new Point(2,9), new Point(6,4), new Point(3,4) }),
                Stretch = Stretch.Fill,
                Width = 5,
                Height = 8,
                Margin = new Thickness(0,0,1,0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            _batteryCross = new TextBlock
            {
                Text = "✕",
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0,0,1,0),
                Visibility = Visibility.Collapsed
            };

            _batteryIconContainer.Children.Add(outerStack);
            _batteryIconContainer.Children.Add(_chargingBolt);
            _batteryIconContainer.Children.Add(_batteryCross);

            _stack.Children.Add(_iconText);
            _stack.Children.Add(_batteryText);
            _stack.Children.Add(_batteryIconContainer);
            
            // Round borders for hover effect
            var border = new Border 
            { 
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)), // Make clickable
                Cursor = Cursors.Hand,
                Margin = new Thickness(2, 0, 2, 0), // Small horizontal margin
                Child = _stack
            };
            
            border.MouseEnter += (s, e) => {
                // Determine hover color based on dark/light mode
                bool isDark = _iconText.Foreground.ToString() == "#FFFFFFFF";
                if (isDark)
                    border.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)); // Faint white for dark mode
                else
                    border.Background = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)); // Translucent white for light mode
            };
            
            border.MouseLeave += (s, e) => {
                border.Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255));
            };

            border.MouseLeftButtonUp += (s, e) => ShowFlyout();

            Content = border;

            // Timer for battery
            _batteryTimer = new DispatcherTimer();
            _batteryTimer.Interval = TimeSpan.FromSeconds(5);
            _batteryTimer.Tick += (s, e) => 
            {
                if (IsForegroundFullscreen())
                {
                    _ticksSinceLastUpdate++;
                    if (_ticksSinceLastUpdate >= 12) // 12 * 5 = 60 seconds in games
                    {
                        _ticksSinceLastUpdate = 0;
                        UpdateBattery();
                    }
                }
                else
                {
                    _ticksSinceLastUpdate = 0;
                    UpdateBattery();
                }
            };
            _batteryTimer.Start();
            UpdateBattery();

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

        private void ShowFlyout()
        {
            if (_flyout == null || !_flyout.IsLoaded)
            {
                _flyout = new FlyoutWindow(_currentBattery, _currentModel, _isCharging, this);
                _flyout.Show();
            }
            else
            {
                _flyout.Close();
            }
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
                            // Enforce Z-order
                            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002); // NOACTIVATE | NOSIZE | NOMOVE
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

                        var brush = isLight ? Brushes.Black : Brushes.White;
                        if (_batteryText.Foreground.ToString() != brush.ToString())
                        {
                            _batteryText.Foreground = brush;
                            _iconText.Foreground = brush;
                            
                            if (_batteryIconBorder != null)
                            {
                                _batteryIconBorder.BorderBrush = brush;
                                _batteryTerminal.Fill = brush;
                                _batteryCross.Foreground = brush;
                                _chargingBolt.Fill = isLight ? Brushes.White : Brushes.Black;
                                
                                if (!_isCharging && _currentBattery > 20)
                                    _batteryFill.Background = brush;
                            }
                        }

                        UpdateTrayIconTheme(isLight);
                    }
                }
            }
            catch { }
        }

        private void UpdateBattery()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var process = new System.Diagnostics.Process();
                    process.StartInfo.FileName = "headsetcontrol.exe";
                    process.StartInfo.Arguments = "-b -o env";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();

                    string outputText = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    
                    int exitCode = process.ExitCode;

                    this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            string output = outputText;
                            int percent = -1;
                            if (exitCode == 0 || output != "") 
                            {
                                _isCharging = false; // Reset before parsing
                                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var line in lines)
                                {
                                    if (line.Contains("_BATTERY_LEVEL="))
                                    {
                                        int parsed;
                                        int index = line.IndexOf('=') + 1;
                                        if (int.TryParse(line.Substring(index).Trim('\"', '\''), out parsed)) percent = parsed;
                                    }
                                    else if (line.Contains("_BATTERY_STATUS="))
                                    {
                                        int index = line.IndexOf('=') + 1;
                                        string status = line.Substring(index).Trim('\"', '\'');
                                        _isCharging = (status == "BATTERY_CHARGING");
                                    }
                                    else if (line.StartsWith("DEVICE_0=") || line.StartsWith("DEVICE_1=") || line.StartsWith("DEVICE_2="))
                                    {
                                        int index = line.IndexOf('=') + 1;
                                        _currentModel = line.Substring(index).Trim('\"', '\'');
                                    }
                                }
                                // Fallback to old simple percent if env failed
                                if (percent == -1)
                                {
                                    int parsed;
                                    if (int.TryParse(output, out parsed)) percent = parsed;
                                }
                            }

                            if (percent != -1)
                            {
                                _shouldHideOverlay = false;
                                _currentBattery = percent;
                                
                                if (_displayStyle == 0)
                                {
                                    _batteryText.Visibility = Visibility.Visible;
                                    _batteryIconContainer.Visibility = Visibility.Collapsed;
                                    
                                    _batteryText.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
                                    _batteryText.FontSize = 12.5;
                                    _batteryText.FontWeight = FontWeights.Normal;
                                    _batteryText.Text = percent + "%" + (_isCharging ? " ⚡" : "");
                                    _batteryText.Margin = new Thickness(0, 0, 4, 0);
                                }
                                else
                                {
                                    _batteryText.Visibility = Visibility.Collapsed;
                                    _batteryIconContainer.Visibility = Visibility.Visible;
                                    
                                    double maxFillWidth = _batteryIconBorder.Width - _batteryIconBorder.BorderThickness.Left * 2 - _batteryIconBorder.Padding.Left * 2;
                                    double fillWidth = maxFillWidth * (percent / 100.0);
                                    if (fillWidth < 0) fillWidth = 0;
                                    if (fillWidth > maxFillWidth) fillWidth = maxFillWidth;
                                    _batteryFill.Width = fillWidth;
                                    
                                    _batteryCross.Visibility = Visibility.Collapsed;
                                    
                                    if (_isCharging)
                                    {
                                        _chargingBolt.Visibility = Visibility.Visible;
                                        _batteryFill.Background = new SolidColorBrush(Color.FromRgb(30, 215, 96)); // Green
                                    }
                                    else
                                    {
                                        _chargingBolt.Visibility = Visibility.Collapsed;
                                        bool isDark = _iconText.Foreground.ToString() == "#FFFFFFFF";
                                        var brush = isDark ? Brushes.White : Brushes.Black;
                                        if (percent <= 20)
                                            _batteryFill.Background = new SolidColorBrush(Color.FromRgb(225, 40, 40)); // Red
                                        else
                                            _batteryFill.Background = brush;
                                    }
                                }
                                
                                this.Visibility = Visibility.Visible;

                                if (_notifyIcon != null)
                                {
                                    string tip = string.Format("{0}: {1}%{2}", _currentModel, percent, _isCharging ? " (Charging)" : "");
                                    if (tip.Length > 63) tip = tip.Substring(0, 63);
                                    _notifyIcon.Text = tip;
                                }

                                if (percent <= 20 && !_hasWarnedLowBattery && !_isCharging)
                                {
                                    ShowLowBatteryToast();
                                    _hasWarnedLowBattery = true;
                                }
                                else if (percent > 20 || _isCharging)
                                {
                                    _hasWarnedLowBattery = false;
                                }
                            }
                            else
                            {
                                _shouldHideOverlay = true;
                                _currentBattery = -1;
                                
                                if (_displayStyle == 0)
                                {
                                    _batteryText.Visibility = Visibility.Visible;
                                    _batteryIconContainer.Visibility = Visibility.Collapsed;
                                    
                                    _batteryText.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
                                    _batteryText.FontSize = 12.5;
                                    _batteryText.FontWeight = FontWeights.Normal;
                                    _batteryText.Text = "--%";
                                    _batteryText.Margin = new Thickness(0, 0, 4, 0);
                                }
                                else
                                {
                                    _batteryText.Visibility = Visibility.Collapsed;
                                    _batteryIconContainer.Visibility = Visibility.Visible;
                                    
                                    _batteryFill.Width = 0;
                                    _chargingBolt.Visibility = Visibility.Collapsed;
                                    _batteryCross.Visibility = Visibility.Visible;
                                }
                                    
                                if (_notifyIcon != null)
                                {
                                    string tip = string.Format("{0}: Disconnected", _currentModel);
                                    if (tip.Length > 63) tip = tip.Substring(0, 63);
                                    _notifyIcon.Text = tip;
                                }

                                if (_hideWhenDisconnected)
                                {
                                    this.Visibility = Visibility.Hidden;
                                }
                                else
                                {
                                    this.Visibility = Visibility.Visible;
                                }
                            }
                        }
                        catch (Exception exInner)
                        {
                            System.IO.File.WriteAllText("crash_battery_inner.log", exInner.ToString());
                            // this.Visibility = Visibility.Hidden;
                        }
                    }));
                }
                catch (Exception ex)
                {
                    this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        System.IO.File.WriteAllText("crash_battery_outer.log", ex.ToString());
                        // this.Visibility = Visibility.Hidden;
                    }));
                }
            });
        }

        private void ShowLowBatteryToast()
        {
            var notif = new NotificationWindow("Low Battery", string.Format("{0}% remaining.", _currentBattery));
            notif.Show();
        }
    }

    public class FlyoutWindow : Window
    {
        public FlyoutWindow(int batteryPercent, string modelName, bool isCharging, Window owner)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            Topmost = true;
            ShowInTaskbar = false;
            Width = 340;
            Height = 150;
            
            // Calculate position
            var source = System.Windows.PresentationSource.FromVisual(owner);
            double dpiX = source != null ? source.CompositionTarget.TransformToDevice.M11 : 1.0;
            double dpiY = source != null ? source.CompositionTarget.TransformToDevice.M22 : 1.0;

            this.Left = owner.Left + owner.Width / 2 - this.Width / 2;
            this.Top = owner.Top - this.Height - 20; // Show above the taskbar with more padding

            bool isDark = true;
            OverlayWindow ow = owner as OverlayWindow;
            if (ow != null)
            {
                isDark = ow.IsDarkTheme;
            }

            var border = new Border
            {
                Background = new SolidColorBrush(isDark ? Color.FromArgb(240, 30, 30, 30) : Color.FromArgb(240, 243, 243, 243)),
                BorderBrush = new SolidColorBrush(isDark ? Color.FromArgb(100, 100, 100, 100) : Color.FromArgb(100, 200, 200, 200)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Icon (huge headset icon)
            var icon = new TextBlock
            {
                Text = "\uE7F6",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 56,
                Foreground = isDark ? Brushes.White : Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            
            var titleText = new TextBlock
            {
                Text = modelName,
                Foreground = isDark ? Brushes.White : Brushes.Black,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            
            var batteryText = new TextBlock
            {
                Text = batteryPercent == -1 ? "Disconnected" : string.Format("Battery: {0}%", batteryPercent),
                Foreground = isDark ? Brushes.LightGray : Brushes.DarkGray,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 4)
            };

            // Estimate time based on max 15 hours for 100%
            string timeString = "";
            if (batteryPercent == -1)
            {
                timeString = "Headset is disconnected or sleeping";
            }
            else if (isCharging)
            {
                timeString = "Charging...";
            }
            else
            {
                double estimatedHours = 15.0 * (batteryPercent / 100.0);
                int hours = (int)estimatedHours;
                int minutes = (int)((estimatedHours - hours) * 60);
                timeString = string.Format("Approx. {0}h {1}m", hours, minutes);
            }

            var timeText = new TextBlock
            {
                Text = timeString,
                Foreground = isDark ? Brushes.LightGray : Brushes.DarkGray,
                FontSize = 12
            };

            infoPanel.Children.Add(titleText);
            infoPanel.Children.Add(batteryText);
            infoPanel.Children.Add(timeText);

            Grid.SetColumn(infoPanel, 1);
            grid.Children.Add(infoPanel);

            border.Child = grid;
            Content = border;

            // Close when clicking outside (deactivated)
            this.Deactivated += (s, e) => this.Close();
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
