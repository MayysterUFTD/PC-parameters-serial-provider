using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using HardwareMonitorTray.Protocol;

namespace HardwareMonitorTray
{
    public class SerialPortService
    {
        private SerialPort _serialPort;

        public ProtocolMode Mode { get; set; } = ProtocolMode.Binary;

        public int PacketsSent { get; private set; }
        public int PacketsErrors { get; private set; }

        public static string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }

        public void Connect(string portName, int baudRate)
        {
            Disconnect();

            _serialPort = new SerialPort(portName, baudRate)
            {
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                Handshake = Handshake.None,
                WriteTimeout = 2000,
                ReadTimeout = 500,
                WriteBufferSize = 8192,
                ReadBufferSize = 4096
            };

            _serialPort.Open();
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();

            PacketsSent = 0;
            PacketsErrors = 0;

            System.Diagnostics.Debug.WriteLine($"[Serial] Connected to {portName} @ {baudRate} (Protocol v2)");
        }

        public void Disconnect()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                try
                {
                    _serialPort.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();
                    _serialPort.Close();
                }
                catch { }
            }
        }

        public void SendData(List<CompactSensorData> sensors)
        {
            if (_serialPort == null || !_serialPort.IsOpen || sensors.Count == 0)
                return;

            try
            {
                byte[] packet;

                switch (Mode)
                {
                    case ProtocolMode.Binary:
                        packet = BuildBinaryPacketV2(sensors);
                        break;
                    case ProtocolMode.Text:
                        var text = BuildTextPacketV2(sensors);
                        _serialPort.Write(text);
                        PacketsSent++;
                        return;
                    case ProtocolMode.Json:
                        return;
                    default:
                        packet = BuildBinaryPacketV2(sensors);
                        break;
                }

                if (packet == null || packet.Length == 0)
                {
                    PacketsErrors++;
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[Serial] Sending {packet.Length} bytes ({sensors.Count} sensors, Protocol v2)");

                _serialPort.Write(packet, 0, packet.Length);
                PacketsSent++;
            }
            catch (Exception ex)
            {
                PacketsErrors++;
                System.Diagnostics.Debug.WriteLine($"[Serial] Error:  {ex.Message}");
            }
        }

        /// <summary>
        /// Buduje pakiet binarny Protocol v2 - 2-bajtowe ID sensorów
        /// Struktura: [START 0xAA][VER 0x02][COUNT][ID_HI][ID_LO][FLOAT x4].. .[CRC16][END 0x55]
        /// </summary>
        private byte[] BuildBinaryPacketV2(List<CompactSensorData> sensors)
        {
            int count = Math.Min(sensors.Count, 250);

            // Packet:  START(1) + VER(1) + COUNT(1) + DATA(count*6) + CRC(2) + END(1)
            // 6 bytes per sensor:  2 bytes ID + 4 bytes float
            int packetSize = 3 + (count * 6) + 3;
            byte[] packet = new byte[packetSize];

            int idx = 0;

            // Header
            packet[idx++] = 0xAA;  // START
            packet[idx++] = 0x02;  // VERSION 2 - 16-bit IDs
            packet[idx++] = (byte)count;

            // Sensor data - 6 bytes each
            for (int i = 0; i < count; i++)
            {
                var sensor = sensors[i];
                ushort id = (ushort)sensor.Id;

                // 16-bit ID (big-endian - high byte first)
                packet[idx++] = (byte)(id >> 8);    // ID high byte
                packet[idx++] = (byte)(id & 0xFF);  // ID low byte

                // Float value (little-endian)
                byte[] valueBytes = BitConverter.GetBytes(sensor.Value);
                packet[idx++] = valueBytes[0];
                packet[idx++] = valueBytes[1];
                packet[idx++] = valueBytes[2];
                packet[idx++] = valueBytes[3];
            }

            // CRC16 (from VER to last data byte)
            ushort crc = CalculateCRC16(packet, 1, 2 + count * 6);
            packet[idx++] = (byte)(crc & 0xFF);
            packet[idx++] = (byte)(crc >> 8);

            // END
            packet[idx++] = 0x55;

            return packet;
        }

        /// <summary>
        /// Buduje pakiet tekstowy Protocol v2 - 4-cyfrowe ID hex
        /// </summary>
        private string BuildTextPacketV2(List<CompactSensorData> sensors)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("$S");  // Short start marker

            foreach (var sensor in sensors.Take(250))
            {
                ushort id = (ushort)sensor.Id;
                sb.AppendLine($"{id:X4}:{sensor.Value:F1}");  // 4-digit hex ID
            }

            // Simple XOR checksum
            byte checksum = 0;
            foreach (char c in sb.ToString())
            {
                checksum ^= (byte)c;
            }

            sb.AppendLine($"$E:{checksum: X2}");
            return sb.ToString();
        }

        private ushort CalculateCRC16(byte[] data, int offset, int length)
        {
            ushort crc = 0xFFFF;

            for (int i = offset; i < offset + length && i < data.Length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    else
                        crc = (ushort)(crc >> 1);
                }
            }

            return crc;
        }

        public void SendRawData(string data)
        {
            if (_serialPort != null && _serialPort.IsOpen && !string.IsNullOrEmpty(data))
            {
                _serialPort.WriteLine(data);
                PacketsSent++;
            }
        }

        public bool IsConnected => _serialPort?.IsOpen ?? false;

        public float SuccessRate => PacketsSent + PacketsErrors > 0
            ? 100f * PacketsSent / (PacketsSent + PacketsErrors)
            : 0f;
    }
}