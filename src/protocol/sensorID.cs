namespace HardwareMonitorTray.Protocol
{
    public enum SensorId : byte
    {
        // CPU (0x01 - 0x0F)
        CpuTemp = 0x01,
        CpuLoad = 0x02,
        CpuClock = 0x03,
        CpuPower = 0x04,
        CpuTempCore = 0x05,
        CpuLoadCore = 0x06,
        CpuPowerCore = 0x07,
        CpuTempCcd = 0x08,
        CpuVoltage = 0x09,

        // GPU (0x10 - 0x1F)
        GpuTemp = 0x10,
        GpuLoad = 0x11,
        GpuClock = 0x12,
        GpuMemoryClock = 0x13,
        GpuPower = 0x14,
        GpuMemoryLoad = 0x15,
        GpuFan = 0x16,
        GpuMemoryTemp = 0x17,
        GpuHotspot = 0x18,
        GpuVideoLoad = 0x19,

        // RAM (0x20 - 0x2F)
        RamUsed = 0x20,
        RamAvailable = 0x21,
        RamLoad = 0x22,

        // Disk (0x30 - 0x3F)
        DiskTemp = 0x30,
        DiskLoad = 0x31,
        DiskRead = 0x32,
        DiskWrite = 0x33,

        // Network (0x40 - 0x4F)
        NetUpload = 0x40,
        NetDownload = 0x41,

        // Motherboard (0x50 - 0x5F)
        MbTemp = 0x50,
        MbFan1 = 0x51,
        MbFan2 = 0x52,
        MbFan3 = 0x53,
        MbFan4 = 0x54,
        MbVoltage = 0x55,

        // Battery (0x60 - 0x6F)
        BatteryLevel = 0x60,
        BatteryVoltage = 0x61,
        BatteryRate = 0x62,

        // Custom/Dynamic (0x80 - 0xFE)
        Custom0 = 0x80,
        Custom1 = 0x81,
        Custom2 = 0x82,
        Custom3 = 0x83,

        Unknown = 0xFF
    }
}