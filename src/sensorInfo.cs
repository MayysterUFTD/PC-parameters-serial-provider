namespace HardwareMonitorTray
{
    public class SensorInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public float? Value { get; set; }
        public string Hardware { get; set; } = "";
        public string Unit { get; set; } = "";
    }
}