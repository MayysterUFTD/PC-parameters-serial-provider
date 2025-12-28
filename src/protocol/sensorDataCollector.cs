using System;
using System.Collections.Generic;
using System.Linq;

namespace HardwareMonitorTray.Protocol
{
    public class SensorDataCollector
    {
        private readonly HardwareMonitorService _monitorService;

        public SensorDataCollector(HardwareMonitorService monitorService)
        {
            _monitorService = monitorService;
        }

        public List<CompactSensorData> CollectData(List<string> selectedSensorIds)
        {
            var result = new List<CompactSensorData>();
            var allSensors = _monitorService.GetSensors(); // Zmienione z GetAllSensors()
            var addedIds = new HashSet<SensorId>();

            foreach (var sensorId in selectedSensorIds)
            {
                var sensor = allSensors.FirstOrDefault(s => s.Id == sensorId);
                if (sensor == null || !sensor.Value.HasValue)
                    continue;

                float value = sensor.Value.Value;
                if (!IsValidFloat(value))
                    continue;

                var compactId = MapToCompactId(sensor);
                if (compactId == SensorId.Unknown)
                    continue;

                if (addedIds.Contains(compactId))
                    continue;

                var compactData = new CompactSensorData
                {
                    Id = compactId,
                    Value = (float)Math.Round(value, 1)
                };

                if (compactData.IsValid())
                {
                    result.Add(compactData);
                    addedIds.Add(compactId);
                }
            }

            return result;
        }

        private bool IsValidFloat(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value) &&
                   value > -1000 &&
                   value < 100000;
        }

        private SensorId MapToCompactId(SensorInfo sensor)
        {
            var hardware = sensor.Hardware.ToLowerInvariant();
            var name = sensor.Name.ToLowerInvariant();
            var type = sensor.Type.ToLowerInvariant();

            // CPU
            if (IsCpu(hardware))
            {
                if (type == "temperature")
                {
                    if (name.Contains("package") || name.Contains("tctl") ||
                        name.Contains("tdie") || name.Contains("core"))
                        return SensorId.CpuTemp;
                }
                else if (type == "load")
                {
                    if (name.Contains("total") || name == "cpu total")
                        return SensorId.CpuLoad;
                }
                else if (type == "clock")
                {
                    if (name.Contains("core") && !name.Contains("bus"))
                        return SensorId.CpuClock;
                }
                else if (type == "power")
                {
                    if (name.Contains("package") || name.Contains("cpu"))
                        return SensorId.CpuPower;
                }
                else if (type == "voltage")
                {
                    if (name.Contains("core") || name.Contains("vcore"))
                        return SensorId.CpuVoltage;
                }
            }
            // GPU
            else if (IsGpu(hardware))
            {
                if (type == "temperature")
                {
                    if (name.Contains("hotspot") || name.Contains("hot spot"))
                        return SensorId.GpuHotspot;
                    if (name.Contains("memory") || name.Contains("vram"))
                        return SensorId.GpuMemoryTemp;
                    if (name.Contains("core") || name.Contains("gpu") || name == "temperature")
                        return SensorId.GpuTemp;
                }
                else if (type == "load")
                {
                    if (name.Contains("memory") || name.Contains("vram") || name.Contains("fb"))
                        return SensorId.GpuMemoryLoad;
                    if (name.Contains("core") || name == "gpu" || name == "gpu core")
                        return SensorId.GpuLoad;
                }
                else if (type == "clock")
                {
                    if (name.Contains("memory"))
                        return SensorId.GpuMemoryClock;
                    if (name.Contains("core") || name == "gpu")
                        return SensorId.GpuClock;
                }
                else if (type == "power")
                {
                    if (name.Contains("gpu") || name.Contains("total") || name.Contains("package"))
                        return SensorId.GpuPower;
                }
                else if (type == "fan")
                {
                    return SensorId.GpuFan;
                }
            }
            // RAM
            else if (IsMemory(hardware))
            {
                if (type == "load" && name.Contains("memory"))
                    return SensorId.RamLoad;
                if (type == "data")
                {
                    if (name.Contains("used"))
                        return SensorId.RamUsed;
                    if (name.Contains("available"))
                        return SensorId.RamAvailable;
                }
            }
            // Storage
            else if (IsStorage(hardware))
            {
                if (type == "temperature")
                    return SensorId.DiskTemp;
                if (type == "load" && (name.Contains("used") || name.Contains("total")))
                    return SensorId.DiskLoad;
                if (type == "throughput")
                {
                    if (name.Contains("read"))
                        return SensorId.DiskRead;
                    if (name.Contains("write"))
                        return SensorId.DiskWrite;
                }
            }
            // Network
            else if (IsNetwork(hardware))
            {
                if (type == "throughput")
                {
                    if (name.Contains("upload") || name.Contains("sent"))
                        return SensorId.NetUpload;
                    if (name.Contains("download") || name.Contains("received"))
                        return SensorId.NetDownload;
                }
            }
            // Motherboard
            else if (IsMotherboard(hardware))
            {
                if (type == "temperature")
                    return SensorId.MbTemp;
                if (type == "fan")
                {
                    if (name.Contains("1") || name.Contains("cpu"))
                        return SensorId.MbFan1;
                    if (name.Contains("2") || name.Contains("sys"))
                        return SensorId.MbFan2;
                    if (name.Contains("3") || name.Contains("cha"))
                        return SensorId.MbFan3;
                }
            }

            return SensorId.Unknown;
        }

        private bool IsCpu(string hw) => hw.Contains("cpu") || hw.Contains("ryzen") || hw.Contains("intel") || hw.Contains("core i") || hw.Contains("processor");
        private bool IsGpu(string hw) => hw.Contains("gpu") || hw.Contains("nvidia") || hw.Contains("radeon") || hw.Contains("geforce") || hw.Contains("rtx") || hw.Contains("gtx") || hw.Contains("amd radeon");
        private bool IsMemory(string hw) => hw.Contains("memory") || hw.Contains("ram");
        private bool IsStorage(string hw) => hw.Contains("ssd") || hw.Contains("hdd") || hw.Contains("nvme") || hw.Contains("disk") || hw.Contains("drive");
        private bool IsNetwork(string hw) => hw.Contains("network") || hw.Contains("ethernet") || hw.Contains("wifi") || hw.Contains("wireless");
        private bool IsMotherboard(string hw) => hw.Contains("motherboard") || hw.Contains("mainboard") || hw.Contains("nuvoton") || hw.Contains("ite");
    }
}