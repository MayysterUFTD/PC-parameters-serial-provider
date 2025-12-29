using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace HardwareMonitorTray
{
    public class HardwareMonitorService : IDisposable
    {
        private readonly Computer _computer;
        private readonly Timer _updateTimer;
        private readonly object _lock = new object();
        private List<SensorInfo> _sensors = new();
        private int _refreshIntervalMs = 500;  // Domyślnie 500ms

        public event Action<List<SensorInfo>> OnDataReady;
        public bool HasData => _sensors.Count > 0;

        public int RefreshIntervalMs
        {
            get => _refreshIntervalMs;
            set
            {
                _refreshIntervalMs = Math.Clamp(value, 50, 5000);
                _updateTimer?.Change(0, _refreshIntervalMs);
                System.Diagnostics.Debug.WriteLine($"[HWMonitor] Refresh interval:  {_refreshIntervalMs}ms");
            }
        }

        public HardwareMonitorService()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsNetworkEnabled = true,
                IsBatteryEnabled = true,
                IsControllerEnabled = true
            };

            _computer.Open();

            // Timer odświeżający dane
            _updateTimer = new Timer(UpdateSensors, null, 0, _refreshIntervalMs);
        }

        private void UpdateSensors(object state)
        {
            try
            {
                var sensors = new List<SensorInfo>();

                lock (_lock)
                {
                    foreach (var hardware in _computer.Hardware)
                    {
                        hardware.Update();
                        CollectSensors(hardware, sensors);

                        foreach (var subHardware in hardware.SubHardware)
                        {
                            subHardware.Update();
                            CollectSensors(subHardware, sensors);
                        }
                    }

                    _sensors = sensors;
                }

                OnDataReady?.Invoke(sensors);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HWMonitor] Update error: {ex.Message}");
            }
        }

        private void CollectSensors(IHardware hardware, List<SensorInfo> sensors)
        {
            foreach (var sensor in hardware.Sensors)
            {
                sensors.Add(new SensorInfo
                {
                    Id = sensor.Identifier.ToString(),
                    Name = sensor.Name,
                    Type = sensor.SensorType.ToString(),
                    Value = sensor.Value,
                    Hardware = hardware.Name,
                    Unit = GetUnit(sensor.SensorType)
                });
            }
        }

        private string GetUnit(SensorType type)
        {
            return type switch
            {
                SensorType.Temperature => "°C",
                SensorType.Load => "%",
                SensorType.Clock => "MHz",
                SensorType.Power => "W",
                SensorType.Voltage => "V",
                SensorType.Fan => "RPM",
                SensorType.Flow => "L/h",
                SensorType.Data => "GB",
                SensorType.SmallData => "MB",
                SensorType.Throughput => "MB/s",
                SensorType.Level => "%",
                _ => ""
            };
        }

        public List<SensorInfo> GetSensors()
        {
            lock (_lock)
            {
                return new List<SensorInfo>(_sensors);
            }
        }

        public Dictionary<string, object> GetSelectedData(List<string> selectedIds)
        {
            lock (_lock)
            {
                var result = new Dictionary<string, object>
                {
                    ["timestamp"] = DateTime.UtcNow.ToString("o"),
                    ["sensors"] = _sensors
                        .Where(s => selectedIds.Contains(s.Id))
                        .Select(s => new
                        {
                            id = s.Id,
                            name = s.Name,
                            value = s.Value,
                            unit = s.Unit,
                            type = s.Type
                        })
                        .ToList()
                };
                return result;
            }
        }

        public void Dispose()
        {
            _updateTimer?.Dispose();
            _computer?.Close();
        }
    }
}