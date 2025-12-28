using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace HardwareMonitorTray
{
    public class SensorInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Hardware { get; set; }
        public string Type { get; set; }
        public float? Value { get; set; }
        public string Unit { get; set; }

        /// <summary>
        /// Checks if sensor matches the search query
        /// </summary>
        public bool MatchesSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            var searchTerms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var searchableText = $"{Name} {Hardware} {Type}".ToLowerInvariant();

            return searchTerms.All(term => searchableText.Contains(term));
        }
    }

    public class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer)
        {
            computer.Traverse(this);
        }

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
            {
                subHardware.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    public class HardwareMonitorService : IDisposable
    {
        private Computer _computer;
        private UpdateVisitor _updateVisitor;

        public HardwareMonitorService()
        {
            _computer = new Computer()
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsNetworkEnabled = true,
                IsControllerEnabled = true,
                IsPsuEnabled = true,
                IsBatteryEnabled = true
            };
            _computer.Open();
            _updateVisitor = new UpdateVisitor();
        }

        public List<SensorInfo> GetAllSensors()
        {
            var sensors = new List<SensorInfo>();
            _computer.Accept(_updateVisitor);

            foreach (var hardware in _computer.Hardware)
            {
                CollectSensors(hardware, sensors);
            }

            return sensors;
        }

        /// <summary>
        /// Gets all sensors filtered by search query
        /// </summary>
        public List<SensorInfo> SearchSensors(string query)
        {
            return GetAllSensors()
                .Where(s => s.MatchesSearch(query))
                .OrderBy(s => s.Hardware)
                .ThenBy(s => s.Type)
                .ThenBy(s => s.Name)
                .ToList();
        }

        /// <summary>
        /// Gets sensors filtered by type
        /// </summary>
        public List<SensorInfo> GetSensorsByType(string sensorType)
        {
            return GetAllSensors()
                .Where(s => s.Type.Equals(sensorType, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Hardware)
                .ThenBy(s => s.Name)
                .ToList();
        }

        /// <summary>
        /// Gets sensors filtered by hardware name
        /// </summary>
        public List<SensorInfo> GetSensorsByHardware(string hardwareName)
        {
            return GetAllSensors()
                .Where(s => s.Hardware.Contains(hardwareName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Type)
                .ThenBy(s => s.Name)
                .ToList();
        }

        /// <summary>
        /// Gets list of all available sensor types
        /// </summary>
        public List<string> GetAvailableSensorTypes()
        {
            return GetAllSensors()
                .Select(s => s.Type)
                .Distinct()
                .OrderBy(t => t)
                .ToList();
        }

        /// <summary>
        /// Gets list of all hardware names
        /// </summary>
        public List<string> GetAvailableHardware()
        {
            return GetAllSensors()
                .Select(s => s.Hardware)
                .Distinct()
                .OrderBy(h => h)
                .ToList();
        }

        private void CollectSensors(IHardware hardware, List<SensorInfo> sensors)
        {
            foreach (var sensor in hardware.Sensors)
            {
                sensors.Add(new SensorInfo
                {
                    Id = sensor.Identifier.ToString(),
                    Name = sensor.Name,
                    Hardware = hardware.Name,
                    Type = sensor.SensorType.ToString(),
                    Value = sensor.Value,
                    Unit = GetUnit(sensor.SensorType)
                });
            }

            foreach (var subHardware in hardware.SubHardware)
            {
                CollectSensors(subHardware, sensors);
            }
        }

        public Dictionary<string, object> GetSelectedSensorData(List<string> selectedSensorIds)
        {
            _computer.Accept(_updateVisitor);
            var result = new Dictionary<string, object>();
            result["timestamp"] = DateTime.Now.ToString("o");

            var sensorsData = new List<object>();
            var allSensors = GetAllSensors();

            foreach (var sensorId in selectedSensorIds)
            {
                var sensor = allSensors.FirstOrDefault(s => s.Id == sensorId);
                if (sensor != null && sensor.Value.HasValue && IsValidValue(sensor.Value.Value))
                {
                    sensorsData.Add(new
                    {
                        id = sensor.Id,
                        name = sensor.Name,
                        hardware = sensor.Hardware,
                        type = sensor.Type,
                        value = Math.Round(sensor.Value.Value, 2),
                        unit = sensor.Unit
                    });
                }
            }

            result["sensors"] = sensorsData;
            return result;
        }

        private bool IsValidValue(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private string GetUnit(SensorType sensorType)
        {
            return sensorType switch
            {
                SensorType.Voltage => "V",
                SensorType.Current => "A",
                SensorType.Clock => "MHz",
                SensorType.Temperature => "°C",
                SensorType.Load => "%",
                SensorType.Fan => "RPM",
                SensorType.Flow => "L/h",
                SensorType.Control => "%",
                SensorType.Level => "%",
                SensorType.Factor => "",
                SensorType.Power => "W",
                SensorType.Data => "GB",
                SensorType.SmallData => "MB",
                SensorType.Throughput => "B/s",
                SensorType.Energy => "Wh",
                SensorType.Noise => "dBA",
                _ => ""
            };
        }

        public void Dispose()
        {
            _computer?.Close();
        }
    }
}