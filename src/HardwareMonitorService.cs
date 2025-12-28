using System;
using System.Collections.Generic;
using System.Threading;
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
    }

    public class HardwareMonitorService : IDisposable
    {
        private Thread _workerThread;
        private volatile bool _running = true;
        private List<SensorInfo> _cache = new List<SensorInfo>();
        private readonly object _lock = new object();

        public event Action<List<SensorInfo>> OnDataReady;
        public bool HasData { get; private set; } = false;

        public HardwareMonitorService()
        {
            _workerThread = new Thread(Worker)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "HWMonitor"
            };
            _workerThread.Start();
        }

        private void Worker()
        {
            Computer computer = null;
            try
            {
                computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = true,
                    IsMotherboardEnabled = true,
                    IsStorageEnabled = true,
                    IsNetworkEnabled = true,
                    IsPsuEnabled = true,
                    IsBatteryEnabled = true
                };
                computer.Open();
                var visitor = new UpdateVisitor();

                while (_running)
                {
                    try
                    {
                        computer.Accept(visitor);
                        var list = new List<SensorInfo>();

                        foreach (var hw in computer.Hardware)
                            Collect(hw, list);

                        lock (_lock)
                        {
                            _cache = list;
                            HasData = true;
                        }

                        // Fire event (on background thread)
                        OnDataReady?.Invoke(list);
                    }
                    catch { }

                    // Sleep in small chunks so we can exit quickly
                    for (int i = 0; i < 20 && _running; i++)
                        Thread.Sleep(100);
                }
            }
            catch { }
            finally
            {
                try { computer?.Close(); } catch { }
            }
        }

        private void Collect(IHardware hw, List<SensorInfo> list)
        {
            foreach (var s in hw.Sensors)
            {
                list.Add(new SensorInfo
                {
                    Id = s.Identifier.ToString(),
                    Name = s.Name,
                    Hardware = hw.Name,
                    Type = s.SensorType.ToString(),
                    Value = s.Value,
                    Unit = Unit(s.SensorType)
                });
            }
            foreach (var sub in hw.SubHardware)
                Collect(sub, list);
        }

        public List<SensorInfo> GetSensors()
        {
            lock (_lock) { return new List<SensorInfo>(_cache); }
        }

        public Dictionary<string, object> GetSelectedData(List<string> ids)
        {
            var result = new Dictionary<string, object>();
            result["timestamp"] = DateTime.UtcNow.ToString("o");
            var data = new List<object>();

            lock (_lock)
            {
                foreach (var id in ids)
                {
                    var s = _cache.Find(x => x.Id == id);
                    if (s?.Value != null && !float.IsNaN(s.Value.Value))
                    {
                        data.Add(new { id = s.Id, name = s.Name, hardware = s.Hardware, type = s.Type, value = Math.Round(s.Value.Value, 2), unit = s.Unit });
                    }
                }
            }
            result["sensors"] = data;
            return result;
        }

        private string Unit(SensorType t) => t switch
        {
            SensorType.Voltage => "V",
            SensorType.Clock => "MHz",
            SensorType.Temperature => "°C",
            SensorType.Load => "%",
            SensorType.Fan => "RPM",
            SensorType.Power => "W",
            SensorType.Data => "GB",
            SensorType.Throughput => "B/s",
            _ => ""
        };

        public void Dispose()
        {
            _running = false;
            _workerThread?.Join(1000);
        }

        private class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer c) => c.Traverse(this);
            public void VisitHardware(IHardware h) { h.Update(); foreach (var s in h.SubHardware) s.Accept(this); }
            public void VisitSensor(ISensor s) { }
            public void VisitParameter(IParameter p) { }
        }
    }
}