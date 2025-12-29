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
            var allSensors = _monitorService.GetSensors();
            var mapper = SensorIdMapper.Instance;

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

                // Użyj centralnej mapy ID
                byte compactId = mapper.GetOrAssignId(sensorId, sensor);

                result.Add(new CompactSensorData
                {
                    Id = (SensorId)compactId,
                    Value = (float)Math.Round(value, 1)
                });
            }

            System.Diagnostics.Debug.WriteLine($"[Collector] Sent:  {result.Count}, Skipped: {skipped}, Mapped: {mapper.Count}");

            return result;
        }
    }
}