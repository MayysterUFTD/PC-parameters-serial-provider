# Hardware Monitor Tray

A Windows system tray application that monitors hardware sensors using LibreHardwareMonitor and sends data via COM port in JSON format.

![. NET](https://img.shields.io/badge/. NET-10. 0-purple)
![Platform](https://img.shields.io/badge/Platform-Windows-blue)
![License](https://img.shields.io/badge/License-MIT-green)

## Features

- 🖥️ **System Tray Application** - Runs quietly in the background
- 📊 **Comprehensive Hardware Monitoring** - CPU, GPU, RAM, Motherboard, Storage, Network, PSU, Battery
- 🔌 **COM Port Communication** - Sends JSON data to any available serial port
- ⚙️ **Configurable Sensors** - Select which sensors to monitor
- 🔍 **Search & Filter** - Quickly find sensors by name, hardware, or type
- ⏱️ **Adjustable Interval** - Set data transmission rate (100ms - 60s)
- 🚀 **Auto-start Options** - Start monitoring on launch, start with Windows
- 💾 **Persistent Configuration** - Settings saved automatically

## Requirements

- Windows 10/11
- .NET 10.0 Runtime
- Administrator privileges (required for hardware sensor access)

## Installation

### From Source

```bash
git clone https://github.com/yourusername/HardwareMonitorTray.git
cd HardwareMonitorTray
dotnet restore
dotnet build -c Release
```

### Run

```bash
# Must run as Administrator
dotnet run
```

Or navigate to `bin/Release/net10.0-windows/` and run `HardwareMonitorTray.exe` as Administrator.

## Usage

### System Tray Menu

Right-click the tray icon to access: 

| Option | Description |
|--------|-------------|
| **Start/Stop** | Toggle data transmission |
| **Settings** | Open configuration window |
| **Exit** | Close the application |

### Settings Window

1. **COM Port** - Select target serial port
2. **Baud Rate** - Set communication speed (9600 - 921600)
3. **Send Interval** - Data transmission frequency in milliseconds
4. **Auto-start monitoring** - Begin sending data when app starts
5. **Start with Windows** - Launch app on Windows startup
6. **Sensor Selection** - Choose which sensors to include

### Sensor Search

Use the search box to filter sensors: 
- Search by sensor name:  `CPU Temperature`
- Search by hardware: `RTX 3080`
- Search by type: `Temperature`, `Load`, `Clock`

## JSON Output Format

```json
{
  "timestamp": "2025-12-21T14:30:00.000Z",
  "sensors": [
    {
      "id": "/amdcpu/0/temperature/0",
      "name": "CPU Package",
      "hardware":  "AMD Ryzen 7 5800X",
      "type": "Temperature",
      "value": 45.5,
      "unit": "°C"
    },
    {
      "id": "/nvidiagpu/0/load/0",
      "name": "GPU Core",
      "hardware": "NVIDIA GeForce RTX 3080",
      "type": "Load",
      "value": 67.0,
      "unit": "%"
    }
  ]
}
```

## Supported Sensor Types

| Type | Unit | Description |
|------|------|-------------|
| Temperature | °C | Component temperatures |
| Load | % | Usage percentage |
| Clock | MHz | Clock speeds |
| Voltage | V | Voltage readings |
| Current | A | Current readings |
| Power | W | Power consumption |
| Fan | RPM | Fan speeds |
| Flow | L/h | Liquid flow rate |
| Level | % | Fill levels |
| Data | GB | Data amounts |
| SmallData | MB | Small data amounts |
| Throughput | B/s | Transfer speeds |
| Energy | Wh | Energy consumption |
| Noise | dBA | Noise levels |

## Configuration File

Settings are stored in: 
```
%AppData%\HardwareMonitorTray\config.json
```

Example configuration:
```json
{
  "ComPort": "COM3",
  "BaudRate": 115200,
  "SendIntervalMs": 1000,
  "AutoStart": true,
  "StartWithWindows": false,
  "SelectedSensors": [
    "/amdcpu/0/temperature/0",
    "/nvidiagpu/0/load/0"
  ]
}
```

## Use Cases

- **Arduino/ESP32 Displays** - Send PC stats to external LCD/OLED displays
- **Home Automation** - Integrate PC monitoring with smart home systems
- **Custom Monitoring** - Build your own hardware monitoring dashboard
- **Logging Systems** - Feed data to external logging solutions

## Troubleshooting

### "Access Denied" or no sensors found
Run the application as Administrator.  LibreHardwareMonitor requires elevated privileges.

### COM port not listed
1. Check if the device is connected
2. Click "Refresh" button in settings
3. Verify the device in Windows Device Manager

### No data being sent
1. Ensure at least one sensor is selected
2. Verify COM port settings match your device
3. Check that monitoring is started (shield icon in tray)

## Building

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release

# Publish self-contained
dotnet publish -c Release -r win-x64 --self-contained true
```

## Dependencies

- [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) - Hardware monitoring library
- [System.IO.Ports](https://www.nuget.org/packages/System.IO. Ports) - Serial port communication

## License

MIT License - see [LICENSE](LICENSE) file for details. 

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## Acknowledgments

- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) - Open source hardware monitoring library
- [OpenHardwareMonitor](https://openhardwaremonitor.org/) - Original project that LibreHardwareMonitor is based on