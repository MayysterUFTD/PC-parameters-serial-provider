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
                WriteBufferSize = 8192,  // Zwiększony bufor
                ReadBufferSize = 4096
            };

            _serialPort.Open();
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();

            PacketsSent = 0;
            PacketsErrors = 0;

            System.Diagnostics.Debug.WriteLine($"[Serial] Connected to {portName} @ {baudRate}");
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
            // NAPRAWIONE: _serialPort zamiast _port
            if (_serialPort == null || !_serialPort.IsOpen || sensors.Count == 0)
                return;

            try
            {
                byte[] packet;

                switch (Mode)
                {
                    case ProtocolMode.Binary:
                        packet = BuildBinaryPacket(sensors);
                        break;
                    case ProtocolMode.Text:
                        var text = BuildTextPacket(sensors);
                        _serialPort.Write(text);
                        PacketsSent++;
                        return;
                    case ProtocolMode.Json:
                        // JSON handled separately via SendRawData
                        return;
                    default:
                        packet = BuildBinaryPacket(sensors);
                        break;
                }

                if (packet == null || packet.Length == 0)
                {
                    PacketsErrors++;
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[Serial] Sending {packet.Length} bytes ({sensors.Count} sensors), COUNT byte: {packet[2]}");

                _serialPort.Write(packet, 0, packet.Length);
                PacketsSent++;
            }
            catch (Exception ex)
            {
                PacketsErrors++;
                System.Diagnostics.Debug.WriteLine($"[Serial] Error:  {ex.Message}");
            }
        }

        private byte[] BuildBinaryPacket(List<CompactSensorData> sensors)
        {
            // Max 250 sensors (COUNT is 1 byte, leave margin)
            int count = Math.Min(sensors.Count, 250);

            // Packet:  START(1) + VER(1) + COUNT(1) + DATA(count*5) + CRC(2) + END(1)
            int packetSize = 3 + (count * 5) + 3;
            byte[] packet = new byte[packetSize];

            int idx = 0;

            // Header
            packet[idx++] = 0xAA;  // START
            packet[idx++] = 0x01;  // VERSION
            packet[idx++] = (byte)count;  // COUNT

            // Sensor data
            for (int i = 0; i < count; i++)
            {
                var sensor = sensors[i];
                packet[idx++] = (byte)sensor.Id;

                // Float to bytes (little-endian)
                byte[] valueBytes = BitConverter.GetBytes(sensor.Value);
                packet[idx++] = valueBytes[0];
                packet[idx++] = valueBytes[1];
                packet[idx++] = valueBytes[2];
                packet[idx++] = valueBytes[3];
            }

            // CRC16 (placeholder - można zaimplementować prawdziwe CRC)
            ushort crc = CalculateCRC16(packet, 1, 2 + count * 5);  // From VER to last data byte
            packet[idx++] = (byte)(crc & 0xFF);
            packet[idx++] = (byte)(crc >> 8);

            // END
            packet[idx++] = 0x55;

            return packet;
        }

        private string BuildTextPacket(List<CompactSensorData> sensors)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("$START");

            foreach (var sensor in sensors.Take(250))
            {
                sb.AppendLine($"{(byte)sensor.Id:X2}:{sensor.Value:F1}");
            }

            sb.AppendLine("$END");
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