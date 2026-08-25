using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace HeadsetControlTaskbarBatteryIndicator
{
    public class HeadsetState
    {
        public bool IsConnected { get; set; }
        public string DeviceName { get; set; }
        public int BatteryLevel { get; set; }
        public bool IsCharging { get; set; }
        public int VoltageMv { get; set; }
        public int TimeToFullMin { get; set; }
        public int TimeToEmptyMin { get; set; }

        public bool SupportsInactiveTime { get; set; }

        public HeadsetState()
        {
            DeviceName = "No Headset";
            BatteryLevel = -1;
            VoltageMv = -1;
            TimeToFullMin = -1;
            TimeToEmptyMin = -1;
        }
    }

    public class HeadsetService : IDisposable
    {
        private static HeadsetService _instance;
        private static readonly object _instanceLock = new object();

        public static HeadsetService Instance
        {
            get
            {
                lock (_instanceLock)
                {
                    if (_instance == null)
                        _instance = new HeadsetService();
                    return _instance;
                }
            }
        }

        public event Action<HeadsetState> StateChanged;

        private readonly object _lock = new object();
        private Timer _pollTimer;
        private bool _isPolling;
        private HeadsetState _currentState = new HeadsetState();
        private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");

        public static void Log(string message)
        {
            string line = string.Format("[{0:HH:mm:ss.fff}] {1}", DateTime.Now, message);
            try
            {
                Console.WriteLine(line);
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
            catch { }
        }

        public HeadsetState CurrentState
        {
            get
            {
                lock (_lock)
                {
                    return _currentState;
                }
            }
        }

        public HeadsetService()
        {
            _pollTimer = new Timer(OnPollTimer, null, Timeout.Infinite, Timeout.Infinite);
            try
            {
                string ver = HeadsetControlNative.Utf8PtrToString(HeadsetControlNative.hsc_version());
                Log("================================================================================");
                Log(string.Format("HeadsetControl Native Library loaded successfully (Version: {0})", ver));
                Log("================================================================================");
            }
            catch (Exception ex)
            {
                Log(string.Format("Error initializing HeadsetControl native library: {0}", ex));
            }
        }

        public void Start(int intervalMs)
        {
            Log(string.Format("Starting periodic device monitoring (Interval: {0} ms)", intervalMs));
            _pollTimer.Change(0, intervalMs);
        }

        public void Stop()
        {
            Log("Stopping device monitoring");
            _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public void ForcePoll()
        {
            Log("Manual device poll triggered");
            ThreadPool.QueueUserWorkItem(state => PollDevices());
        }

        private void OnPollTimer(object state)
        {
            PollDevices();
        }

        private void PollDevices()
        {
            if (_isPolling) return;
            _isPolling = true;

            try
            {
                HeadsetState newState = new HeadsetState();
                IntPtr headsetsPtr;
                int count = HeadsetControlNative.hsc_discover(out headsetsPtr);

                if (count > 0 && headsetsPtr != IntPtr.Zero)
                {
                    IntPtr[] handles = new IntPtr[count];
                    Marshal.Copy(headsetsPtr, handles, 0, count);

                    for (int i = 0; i < count; i++)
                    {
                        IntPtr activeHandle = handles[i];
                        string productName = HeadsetControlNative.Utf8PtrToString(HeadsetControlNative.hsc_get_product_name(activeHandle));
                        string internalName = HeadsetControlNative.Utf8PtrToString(HeadsetControlNative.hsc_get_name(activeHandle));
                        string vendorName = HeadsetControlNative.Utf8PtrToString(HeadsetControlNative.hsc_get_vendor_name(activeHandle));
                        ushort vid = HeadsetControlNative.hsc_get_vendor_id(activeHandle);
                        ushort pid = HeadsetControlNative.hsc_get_product_id(activeHandle);

                        if (string.IsNullOrEmpty(productName)) productName = internalName;
                        if (string.IsNullOrEmpty(productName)) productName = "Wireless Headset";

                        Log("--------------------------------------------------------------------------------");
                        Log(string.Format("🎮 DISCOVERED HEADSET [{0}/{1}]: {2}", i + 1, count, productName));
                        Log(string.Format("   - Vendor:       {0} (VID: 0x{1:X4})", vendorName, vid));
                        Log(string.Format("   - Product ID:   0x{0:X4}", pid));

                        // Primary device for UI state
                        if (i == 0)
                        {
                            newState.DeviceName = productName;
                            newState.IsConnected = false;
                        }

                        // Battery Info
                        if (HeadsetControlNative.hsc_supports(activeHandle, HeadsetControlNative.Capability.BatteryStatus))
                        {
                            HeadsetControlNative.HscBattery batt = new HeadsetControlNative.HscBattery();
                            batt.LevelPercent = -1;
                            batt.Status = HeadsetControlNative.BatteryStatus.Unavailable;

                            int res = HeadsetControlNative.hsc_get_battery(activeHandle, ref batt);

                            bool isOnline = (res == 0 && batt.Status != HeadsetControlNative.BatteryStatus.Unavailable && batt.LevelPercent >= 0);

                            string statusText = batt.Status == HeadsetControlNative.BatteryStatus.Charging ? "BATTERY_CHARGING (Charging ⚡)" :
                                               batt.Status == HeadsetControlNative.BatteryStatus.Available ? "BATTERY_AVAILABLE (Discharging)" :
                                               batt.Status == HeadsetControlNative.BatteryStatus.Unavailable ? "BATTERY_UNAVAILABLE (Offline / Standby)" :
                                               batt.Status.ToString();

                            if (isOnline)
                            {
                                Log("🔋 BATTERY STATUS:");
                                Log(string.Format("   - Level:         {0}%", batt.LevelPercent));
                                Log(string.Format("   - State:         {0} (Query result code: {1})", statusText, res));
                                if (batt.VoltageMv > 0)
                                    Log(string.Format("   - Voltage:       {0} mV", batt.VoltageMv));
                                if (batt.TimeToFullMin > 0)
                                    Log(string.Format("   - Time to Full:  ~{0}h {1}m", batt.TimeToFullMin / 60, batt.TimeToFullMin % 60));
                                if (batt.TimeToEmptyMin > 0)
                                    Log(string.Format("   - Time to Empty: ~{0}h {1}m", batt.TimeToEmptyMin / 60, batt.TimeToEmptyMin % 60));

                                if (i == 0)
                                {
                                    newState.IsConnected = true;
                                    newState.BatteryLevel = batt.LevelPercent;
                                    newState.IsCharging = (batt.Status == HeadsetControlNative.BatteryStatus.Charging);
                                    newState.VoltageMv = batt.VoltageMv;
                                    newState.TimeToFullMin = batt.TimeToFullMin;
                                    newState.TimeToEmptyMin = batt.TimeToEmptyMin;
                                }
                            }
                            else
                            {
                                Log(string.Format("🔋 BATTERY STATUS: Headset Offline / Sleeping (Query result code: {0}, State: {1})", res, statusText));
                                if (i == 0)
                                {
                                    newState.IsConnected = false;
                                    newState.BatteryLevel = -1;
                                }
                            }
                        }

                        // Inactive Sleep Time Info
                        if (HeadsetControlNative.hsc_supports(activeHandle, HeadsetControlNative.Capability.InactiveTime))
                        {
                            if (i == 0) newState.SupportsInactiveTime = true;
                        }

                        Log("--------------------------------------------------------------------------------");
                    }

                    HeadsetControlNative.hsc_free_headsets(headsetsPtr, count);
                }
                else
                {
                    Log("[INFO] No supported headset discovered by HeadsetControl registry. Dumping all raw connected USB HID devices:");
                    StringBuilder sb = new StringBuilder(8192);
                    HeadsetControlNative.hsc_dump_hid_devices(sb, 8192);
                    string rawDevs = sb.ToString();
                    if (!string.IsNullOrEmpty(rawDevs))
                    {
                        Log(rawDevs.TrimEnd());
                    }
                    else
                    {
                        Log("  (No HID devices detected on USB bus)");
                    }
                }

                lock (_lock)
                {
                    _currentState = newState;
                }

                if (StateChanged != null)
                {
                    StateChanged(newState);
                }
            }
            catch (Exception ex)
            {
                Log(string.Format("Exception during PollDevices: {0}", ex));
            }
            finally
            {
                _isPolling = false;
            }
        }

        public bool SetInactiveTime(byte minutes)
        {
            Log(string.Format("Command: SetInactiveTime({0} min)", minutes));
            bool success = ExecuteWithActiveHeadset(delegate(IntPtr handle)
            {
                if (HeadsetControlNative.hsc_supports(handle, HeadsetControlNative.Capability.InactiveTime))
                {
                    int res = HeadsetControlNative.hsc_set_inactive_time(handle, minutes, IntPtr.Zero);
                    Log(string.Format("  -> Inactive Time successfully written to headset: {0} min (Result: {1})", minutes, res));
                    return res == 0;
                }
                return false;
            });
            return success;
        }

        private bool ExecuteWithActiveHeadset(Func<IntPtr, bool> action)
        {
            try
            {
                IntPtr headsetsPtr;
                int count = HeadsetControlNative.hsc_discover(out headsetsPtr);
                if (count > 0 && headsetsPtr != IntPtr.Zero)
                {
                    IntPtr[] handles = new IntPtr[count];
                    Marshal.Copy(headsetsPtr, handles, 0, count);

                    bool result = action(handles[0]);
                    HeadsetControlNative.hsc_free_headsets(headsetsPtr, count);
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log(string.Format("Exception in ExecuteWithActiveHeadset: {0}", ex));
            }
            return false;
        }

        public void Dispose()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Dispose();
                _pollTimer = null;
            }
        }
    }
}
