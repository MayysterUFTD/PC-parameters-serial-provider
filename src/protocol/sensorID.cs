namespace HardwareMonitorTray.Protocol
{
    /// <summary>
    /// Compact sensor identifiers for binary protocol
    /// </summary>
    public enum SensorId : byte
    {
        // CPU Sensors (0x01 - 0x0F)
        CpuTemp = 0x01,
        CpuLoad = 0x02,
        CpuClock = 0x03,
        CpuPower = 0x04,
        CpuVoltage = 0x05,

        // GPU Sensors (0x10 - 0x1F)
        GpuTemp = 0x10,
        GpuLoad = 0x11,
        GpuClock = 0x12,
        GpuMemoryClock = 0x13,
        GpuPower = 0x14,
        GpuMemoryLoad = 0x15,
        GpuFan = 0x16,
        GpuMemoryTemp = 0x17,
        GpuHotspot = 0x18,

        // RAM Sensors (0x20 - 0x2F)
        RamUsed = 0x20,
        RamAvailable = 0x21,
        RamLoad = 0x22,

        // Storage Sensors (0x30 - 0x3F)
        DiskTemp = 0x30,
        DiskLoad = 0x31,
        DiskRead = 0x32,
        DiskWrite = 0x33,

        // Network Sensors (0x40 - 0x4F)
        NetUpload = 0x40,
        NetDownload = 0x41,

        // Motherboard Sensors (0x50 - 0x5F)
        MbTemp = 0x50,
        MbFan1 = 0x51,
        MbFan2 = 0x52,
        MbFan3 = 0x53,

        // Unknown/Invalid
        Unknown = 0xFF
    }
}