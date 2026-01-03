namespace HardwareMonitorTray.Protocol
{
    public enum SensorId : ushort  // Zmiana z byte na ushort (2 bajty)
    {
        // CPU (0x0001 - 0x00FF)
        CpuTemp = 0x0001,
        CpuLoad = 0x0002,
        CpuClock = 0x0003,
        CpuPower = 0x0004,
        CpuTempCore = 0x0005,
        CpuLoadCore = 0x0006,
        CpuPowerCore = 0x0007,
        CpuTempCcd = 0x0008,
        CpuVoltage = 0x0009,

        // GPU (0x0010 - 0x001F)
        GpuTemp = 0x0010,
        GpuLoad = 0x0011,
        GpuClock = 0x0012,
        GpuMemoryClock = 0x0013,
        GpuPower = 0x0014,
        GpuMemoryLoad = 0x0015,
        GpuFan = 0x0016,
        GpuMemoryTemp = 0x0017,
        GpuHotspot = 0x0018,
        GpuVideoLoad = 0x0019,

        // RAM (0x0020 - 0x002F)
        RamUsed = 0x0020,
        RamAvailable = 0x0021,
        RamLoad = 0x0022,

        // Disk (0x0030 - 0x003F)
        DiskTemp = 0x0030,
        DiskLoad = 0x0031,
        DiskRead = 0x0032,
        DiskWrite = 0x0033,

        // Network (0x0040 - 0x004F)
        NetUpload = 0x0040,
        NetDownload = 0x0041,

        // Motherboard (0x0050 - 0x005F)
        MbTemp = 0x0050,
        MbFan1 = 0x0051,
        MbFan2 = 0x0052,
        MbFan3 = 0x0053,
        MbFan4 = 0x0054,
        MbVoltage = 0x0055,

        // Battery (0x0060 - 0x006F)
        BatteryLevel = 0x0060,
        BatteryVoltage = 0x0061,
        BatteryRate = 0x0062,

        // Custom/Dynamic (0x0080 - 0xFFFD)
        Custom0 = 0x0080,
        Custom1 = 0x0081,
        Custom2 = 0x0082,
        Custom3 = 0x0083,

        // Reserved - nie używać! 
        Reserved_Start = 0x00AA,  // START_BYTE pattern
        Reserved_End = 0x0055,    // END_BYTE pattern

        Unknown = 0xFFFF
    }
}