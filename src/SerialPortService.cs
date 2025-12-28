using System;
using System.Collections.Generic;
using System.IO.Ports;
using HardwareMonitorTray.Protocol;

namespace HardwareMonitorTray
{
    public class SerialPortService
    {
        private SerialPort _serialPort;

        public ProtocolMode Mode { get; set; } = ProtocolMode.Binary;

        // Statistics
        public int PacketsSent { get; private set; }
        public int PacketsErrors { get; private set; }

        public static string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }

        public void Connect(string portName, int baudRate)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
            }

            _serialPort = new SerialPort(portName, baudRate)
            {
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                Handshake = Handshake.None,
                WriteTimeout = 1000,
                ReadTimeout = 500,
                WriteBufferSize = 4096
            };

            _serialPort.Open();

            // Clear buffers on connect
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();

            // Reset statistics
            PacketsSent = 0;
            PacketsErrors = 0;
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

        /// <summary>
        /// Sends sensor data using the configured protocol
        /// </summary>
        public bool SendData(List<CompactSensorData> sensors)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
                return false;

            if (sensors == null || sensors.Count == 0)
                return false;

            try
            {
                switch (Mode)
                {
                    case ProtocolMode.Binary:
                        return SendBinaryPacket(sensors);

                    case ProtocolMode.Text:
                        return SendTextPacket(sensors);

                    default:
                        return false;
                }
            }
            catch (Exception)
            {
                PacketsErrors++;
                return false;
            }
        }

        private bool SendBinaryPacket(List<CompactSensorData> sensors)
        {
            var packet = SerialProtocol.CreateBinaryPacket(sensors);
            if (packet == null || packet.Length == 0)
            {
                PacketsErrors++;
                return false;
            }

            _serialPort.Write(packet, 0, packet.Length);
            PacketsSent++;
            return true;
        }

        private bool SendTextPacket(List<CompactSensorData> sensors)
        {
            var packet = SerialProtocol.CreateTextPacket(sensors);
            if (string.IsNullOrEmpty(packet))
            {
                PacketsErrors++;
                return false;
            }

            _serialPort.Write(packet);
            PacketsSent++;
            return true;
        }

        /// <summary>
        /// Legacy:  Sends raw string data (for JSON mode)
        /// </summary>
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