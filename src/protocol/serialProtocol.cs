using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HardwareMonitorTray.Protocol
{
    /// <summary>
    /// Protocol mode for serial communication
    /// </summary>
    public enum ProtocolMode
    {
        Binary,     // Most efficient - 5 bytes per sensor
        Text,       // Human readable - for debugging
        Json        // Legacy - full JSON format
    }

    /// <summary>
    /// Compact sensor data structure
    /// </summary>
    public class CompactSensorData
    {
        public SensorId Id { get; set; }
        public float Value { get; set; }

        /// <summary>
        /// Validates sensor value is within expected range
        /// </summary>
        public bool IsValid()
        {
            if (float.IsNaN(Value) || float.IsInfinity(Value))
                return false;

            return Id switch
            {
                // Temperature sensors:  -40°C to 150°C
                SensorId.CpuTemp or SensorId.GpuTemp or SensorId.GpuMemoryTemp or
                SensorId.GpuHotspot or SensorId.DiskTemp or SensorId.MbTemp
                    => Value >= -40 && Value <= 150,

                // Load sensors: 0% to 100%
                SensorId.CpuLoad or SensorId.GpuLoad or SensorId.GpuMemoryLoad or
                SensorId.RamLoad or SensorId.DiskLoad
                    => Value >= 0 && Value <= 100,

                // Clock sensors: 0 to 10000 MHz
                SensorId.CpuClock or SensorId.GpuClock or SensorId.GpuMemoryClock
                    => Value >= 0 && Value <= 10000,

                // Power sensors: 0 to 1000W
                SensorId.CpuPower or SensorId.GpuPower
                    => Value >= 0 && Value <= 1000,

                // Voltage sensors: 0 to 15V
                SensorId.CpuVoltage
                    => Value >= 0 && Value <= 15,

                // Fan sensors: 0 to 20000 RPM
                SensorId.GpuFan or SensorId.MbFan1 or SensorId.MbFan2 or SensorId.MbFan3
                    => Value >= 0 && Value <= 20000,

                // RAM:  0 to 1024 GB
                SensorId.RamUsed or SensorId.RamAvailable
                    => Value >= 0 && Value <= 1024,

                // Network/Disk throughput: >= 0
                SensorId.NetUpload or SensorId.NetDownload or
                SensorId.DiskRead or SensorId.DiskWrite
                    => Value >= 0,

                _ => false
            };
        }
    }

    /// <summary>
    /// Serial protocol for hardware monitor data transmission
    /// </summary>
    public static class SerialProtocol
    {
        public const byte START_BYTE = 0xAA;
        public const byte END_BYTE = 0x55;
        public const byte PROTOCOL_VERSION = 0x01;

        public const int MAX_SENSORS = 32;
        public const int HEADER_SIZE = 3;   // START + VERSION + LENGTH
        public const int FOOTER_SIZE = 3;   // CRC16 + END
        public const int SENSOR_SIZE = 5;   // ID(1) + VALUE(4)

        /// <summary>
        /// Creates a binary packet from sensor data
        /// Packet structure:  [START][VERSION][LENGTH][SENSOR_DATA... ][CRC16][END]
        /// </summary>
        public static byte[] CreateBinaryPacket(List<CompactSensorData> sensors)
        {
            if (sensors == null || sensors.Count == 0)
                return null;

            var validSensors = sensors.FindAll(s => s.IsValid());
            if (validSensors.Count == 0)
                return null;

            if (validSensors.Count > MAX_SENSORS)
                validSensors = validSensors.GetRange(0, MAX_SENSORS);

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // Header
            writer.Write(START_BYTE);
            writer.Write(PROTOCOL_VERSION);
            writer.Write((byte)validSensors.Count);

            // Sensor data
            foreach (var sensor in validSensors)
            {
                writer.Write((byte)sensor.Id);
                writer.Write(sensor.Value);
            }

            // Calculate CRC16 over data (skip START byte)
            var data = ms.ToArray();
            ushort crc = CalculateCRC16(data, 1, data.Length - 1);
            writer.Write(crc);

            // Footer
            writer.Write(END_BYTE);

            return ms.ToArray();
        }

        /// <summary>
        /// Creates a compact text packet for debugging
        /// Format: $S\nID: VALUE\n.. .\n$E: CHECKSUM\n
        /// </summary>
        public static string CreateTextPacket(List<CompactSensorData> sensors)
        {
            if (sensors == null || sensors.Count == 0)
                return null;

            var sb = new StringBuilder();
            sb.AppendLine("$S");

            foreach (var sensor in sensors)
            {
                if (!sensor.IsValid()) continue;
                sb.AppendFormat("{0:X2}:{1:F1}\n", (byte)sensor.Id, sensor.Value);
            }

            // Simple XOR checksum
            byte checksum = 0;
            foreach (char c in sb.ToString())
            {
                checksum ^= (byte)c;
            }

            sb.AppendFormat("$E:{0:X2}\n", checksum);
            return sb.ToString();
        }

        /// <summary>
        /// CRC-16-CCITT calculation
        /// </summary>
        public static ushort CalculateCRC16(byte[] data, int offset, int length)
        {
            ushort crc = 0xFFFF;

            for (int i = offset; i < offset + length && i < data.Length; i++)
            {
                crc ^= (ushort)(data[i] << 8);

                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x8000) != 0)
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    else
                        crc <<= 1;
                }
            }

            return crc;
        }

        /// <summary>
        /// Gets sensor name for display
        /// </summary>
        public static string GetSensorName(SensorId id)
        {
            return id switch
            {
                SensorId.CpuTemp => "CPU Temp",
                SensorId.CpuLoad => "CPU Load",
                SensorId.CpuClock => "CPU Clock",
                SensorId.CpuPower => "CPU Power",
                SensorId.CpuVoltage => "CPU Voltage",
                SensorId.GpuTemp => "GPU Temp",
                SensorId.GpuLoad => "GPU Load",
                SensorId.GpuClock => "GPU Clock",
                SensorId.GpuMemoryClock => "GPU Mem Clock",
                SensorId.GpuPower => "GPU Power",
                SensorId.GpuMemoryLoad => "GPU Memory",
                SensorId.GpuFan => "GPU Fan",
                SensorId.GpuMemoryTemp => "GPU Mem Temp",
                SensorId.GpuHotspot => "GPU Hotspot",
                SensorId.RamUsed => "RAM Used",
                SensorId.RamAvailable => "RAM Available",
                SensorId.RamLoad => "RAM Load",
                SensorId.DiskTemp => "Disk Temp",
                SensorId.DiskLoad => "Disk Load",
                SensorId.DiskRead => "Disk Read",
                SensorId.DiskWrite => "Disk Write",
                SensorId.NetUpload => "Net Upload",
                SensorId.NetDownload => "Net Download",
                SensorId.MbTemp => "MB Temp",
                SensorId.MbFan1 => "MB Fan 1",
                SensorId.MbFan2 => "MB Fan 2",
                SensorId.MbFan3 => "MB Fan 3",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Gets sensor unit
        /// </summary>
        public static string GetSensorUnit(SensorId id)
        {
            return id switch
            {
                SensorId.CpuTemp or SensorId.GpuTemp or SensorId.GpuMemoryTemp or
                SensorId.GpuHotspot or SensorId.DiskTemp or SensorId.MbTemp => "°C",

                SensorId.CpuLoad or SensorId.GpuLoad or SensorId.GpuMemoryLoad or
                SensorId.RamLoad or SensorId.DiskLoad => "%",

                SensorId.CpuClock or SensorId.GpuClock or SensorId.GpuMemoryClock => "MHz",

                SensorId.CpuPower or SensorId.GpuPower => "W",

                SensorId.CpuVoltage => "V",

                SensorId.GpuFan or SensorId.MbFan1 or SensorId.MbFan2 or SensorId.MbFan3 => "RPM",

                SensorId.RamUsed or SensorId.RamAvailable => "GB",

                SensorId.NetUpload or SensorId.NetDownload or
                SensorId.DiskRead or SensorId.DiskWrite => "KB/s",

                _ => ""
            };
        }
    }
}