using System;
using System.Collections.Generic;
using System.Linq;
using HardwareMonitorTray;
using HardwareMonitorTray.Protocol;

namespace HardwareMonitorTray.Protocol
{
    public class SensorDataCollector
    {
        private readonly HardwareMonitorService _monitorService;
        private readonly Dictionary<string, byte> _sensorIdCache = new();
        private readonly HashSet<byte> _usedIds = new();
        private byte _nextId = 0x01;

        public SensorDataCollector(HardwareMonitorService monitorService)
        {
            _monitorService = monitorService;
        }

        public List<CompactSensorData> CollectData(List<string> selectedSensorIds)
        {
            var result = new List<CompactSensorData>();
            var allSensors = _monitorService.GetSensors();

            int skipped = 0;

            foreach (var sensorId in selectedSensorIds)
            {
                if (result.Count >= 250) break;

                var sensor = allSensors.FirstOrDefault(s => s.Id == sensorId);
                if (sensor == null || !sensor.Value.HasValue)
                {
                    skipped++;
                    continue;
                }

                float value = sensor.Value.Value;
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    skipped++;
                    continue;
                }

                // Pobierz unikalne ID dla tego konkretnego sensora
                byte compactId = GetUniqueId(sensorId, sensor);

                result.Add(new CompactSensorData
                {
                    Id = (SensorId)compactId,
                    Value = (float)Math.Round(value, 1)
                });
            }

            System.Diagnostics.Debug.WriteLine($"[Collector] Sent:  {result.Count}, Skipped: {skipped}");

            return result;
        }

        private byte GetUniqueId(string fullSensorId, SensorInfo sensor)
        {
            // Sprawdź cache
            if (_sensorIdCache.TryGetValue(fullSensorId, out var cached))
                return cached;

            // Pobierz bazowe ID dla typu sensora
            byte baseId = GetBaseId(sensor);

            // Znajdź wolne ID zaczynając od bazowego
            byte id = baseId;
            int attempts = 0;

            while (_usedIds.Contains(id) && attempts < 254)
            {
                id++;
                if (id == 0xFF) id = 0x01;  // Skip Unknown, wrap around
                attempts++;
            }

            // Zapisz
            _usedIds.Add(id);
            _sensorIdCache[fullSensorId] = id;

            return id;
        }

        private byte GetBaseId(SensorInfo sensor)
        {
            var hw = sensor.Hardware.ToLower();
            var nm = sensor.Name.ToLower();
            var tp = sensor.Type.ToLower();

            // CPU (0x01-0x0F)
            if (IsCpu(hw))
            {
                if (tp == "temperature") return 0x01;
                if (tp == "load") return 0x02;
                if (tp == "clock") return 0x03;
                if (tp == "power") return 0x04;
                if (tp == "voltage") return 0x09;
                return 0x0A;
            }

            // GPU (0x10-0x1F)
            if (IsGpu(hw))
            {
                if (tp == "temperature") return 0x10;
                if (tp == "load") return 0x11;
                if (tp == "clock") return 0x12;
                if (tp == "power") return 0x14;
                if (tp == "fan") return 0x16;
                return 0x1A;
            }

            // RAM (0x20-0x2F)
            if (IsMemory(hw))
            {
                if (tp == "data") return 0x20;
                if (tp == "load") return 0x22;
                return 0x23;
            }

            // Disk (0x30-0x3F)
            if (IsStorage(hw))
            {
                if (tp == "temperature") return 0x30;
                if (tp == "load") return 0x31;
                if (tp == "throughput") return 0x32;
                return 0x34;
            }

            // Network (0x40-0x4F)
            if (IsNetwork(hw))
            {
                if (tp == "throughput") return 0x40;
                return 0x42;
            }

            // Motherboard (0x50-0x5F)
            if (IsMotherboard(hw))
            {
                if (tp == "temperature") return 0x50;
                if (tp == "fan") return 0x51;
                if (tp == "voltage") return 0x55;
                return 0x58;
            }

            // Battery (0x60-0x6F)
            if (IsBattery(hw))
            {
                return 0x60;
            }

            // Custom/Other (0x70-0xFE)
            return 0x70;
        }

        private bool IsCpu(string hw) =>
            hw.Contains("cpu") || hw.Contains("ryzen") || hw.Contains("intel") || hw.Contains("processor");

        private bool IsGpu(string hw) =>
            hw.Contains("gpu") || hw.Contains("nvidia") || hw.Contains("radeon") ||
            hw.Contains("geforce") || hw.Contains("rtx") || hw.Contains("gtx");

        private bool IsMemory(string hw) =>
            hw.Contains("memory") && !hw.Contains("gpu");

        private bool IsStorage(string hw) =>
            hw.Contains("ssd") || hw.Contains("hdd") || hw.Contains("nvme") || hw.Contains("disk");

        private bool IsNetwork(string hw) =>
            hw.Contains("network") || hw.Contains("ethernet") || hw.Contains("wifi");

        private bool IsMotherboard(string hw) =>
            hw.Contains("motherboard") || hw.Contains("mainboard") || hw.Contains("superio") || hw.Contains("lpc");

        private bool IsBattery(string hw) =>
            hw.Contains("battery");

        public void ResetCache()
        {
            _sensorIdCache.Clear();
            _usedIds.Clear();
        }
    }
}