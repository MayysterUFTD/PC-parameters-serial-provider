# Hardware Monitor Tray

A Windows system tray application that monitors hardware sensors using LibreHardwareMonitor and sends data via COM port in binary/text/JSON format.

![.NET](https://img.shields.io/badge/.NET-10.0-purple)
![Platform](https://img.shields.io/badge/Platform-Windows-blue)
![Protocol](https://img.shields.io/badge/Protocol-v2.0-green)
![License](https://img.shields.io/badge/License-MIT-green)

## Features

- **System Tray Application** - Runs quietly in the background
- **Comprehensive Hardware Monitoring** - CPU, GPU, RAM, Motherboard, Storage, Network, PSU, Battery
- **COM Port Communication** - Sends data to any available serial port
- **Protocol v2** - 16-bit sensor IDs, up to 65000+ unique sensors
- **Multiple Protocols** - Binary (efficient), Text (debug), JSON (web)
- **Configurable Sensors** - Select which sensors to monitor
- **Search & Filter** - Quickly find sensors by name, hardware, or type
- **Adjustable Interval** - Set data transmission rate (50ms - 5s)
- **Auto-start Options** - Start monitoring on launch, start with Windows
- **Persistent Sensor Map** - Stable sensor IDs across restarts
- **Export to C Header** - Generate `.h` files for MCU projects

## Protocol v2.0

### Key Changes from v1

- **16-bit Sensor IDs** - 2 bytes instead of 1
- **Reserved Bytes Protection** - IDs cannot contain `0xAA` (START) or `0x55` (END)
- **6 bytes per sensor** - ID(2) + VALUE(4) instead of 5 bytes
- **Protocol version byte** - `0x02` in header

### Binary Packet Structure

```
┌───────┬─────────┬───────┬────────────────────────────┬───────┬───────┐
│ START │ VERSION │ COUNT │        SENSOR DATA         │ CRC16 │  END  │
│ 0xAA  │  0x02   │  1B   │      N × 6 bytes           │  2B   │ 0x55  │
└───────┴─────────┴───────┴────────────────────────────┴───────┴───────┘

Each Sensor (6 bytes):
┌──────────┬──────────┬─────────────────────────────────────┐
│ ID_HIGH  │ ID_LOW   │     VALUE (4 bytes, little-endian)  │
│   1B     │   1B     │              float32                │
└──────────┴──────────┴─────────────────────────────────────┘
```

### Example Packet (2 sensors)

```
AA 02 02 00 01 00 00 83 42 00 10 00 00 8C 42 [CRC] 55
│  │  │  │  │  │────────│  │  │  │────────│        │
│  │  │  │  │  CPU=65. 5°C  │  │  GPU=70.0°C        End
│  │  │  │  ID_LOW=0x01    │  ID_LOW=0x10
│  │  │  ID_HIGH=0x00      ID_HIGH=0x00
│  │  Count=2 sensors
│  Version=2 (16-bit IDs)
Start
```

### Sensor ID Ranges (16-bit)

| Category    | Range           | Examples                 |
| ----------- | --------------- | ------------------------ |
| CPU         | 0x0001 - 0x000F | Temp, Load, Clock, Power |
| GPU         | 0x0010 - 0x001F | Temp, Load, Clock, Fan   |
| RAM         | 0x0020 - 0x002F | Used, Available, Load    |
| Disk        | 0x0030 - 0x003F | Temp, Load, Read/Write   |
| Network     | 0x0040 - 0x004F | Upload, Download         |
| Motherboard | 0x0050 - 0x005F | Temp, Fans, Voltage      |
| Battery     | 0x0060 - 0x006F | Level, Voltage, Rate     |
| Custom      | 0x0080 - 0xFFFD | Dynamically assigned     |

### Reserved IDs (Invalid)

Sensor IDs **cannot** contain these bytes:

- `0xAA` (START byte)
- `0x55` (END byte)

Examples of **invalid** IDs: `0x00AA`, `0xAA00`, `0x0055`, `0x5500`, `0xAA55`, `0x55AA`

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

## Usage

### System Tray Menu

Right-click the tray icon to access:

| Option                | Description                      |
| --------------------- | -------------------------------- |
| **▶ Start/Stop**      | Toggle data transmission         |
| **🔄 Restart Serial** | Restart COM port connection      |
| **⚙ Settings**        | Open configuration window        |
| **📊 Statistics**     | View packet stats and sensor map |
| **📌 Pin to Taskbar** | Instructions to pin icon         |
| **❌ Exit**           | Close the application            |

### Settings Window

1. **COM Port** - Select target serial port
2. **Baud Rate** - Set communication speed (9600 - 921600)
3. **Send Interval** - Data transmission frequency in milliseconds
4. **Refresh Interval** - Sensor data refresh rate
5. **Protocol Mode** - Binary (recommended) / Text / JSON
6. **Tray Icon Style** - Status, Temperature, Load, Modern, Animated
7. **Auto-start monitoring** - Begin sending data when app starts
8. **Start with Windows** - Launch app on Windows startup
9. **Sensor Selection** - Choose which sensors to include

### Sensor Map Features

- **Persistent IDs** - Sensors keep the same ID across restarts
- **Export . h** - Generate C header file for MCU projects
- **Map View** - See all mapped sensors with their IDs
- **Cleanup** - Remove old sensors not seen in 30 days
- **Reset** - Clear entire map and reassign IDs

## MCU Library

The `MCUlibrary` folder contains Arduino/ESP32 compatible library:

```cpp
#include "HWMonitor.h"

HWMonitor monitor;

void setup() {
    Serial.begin(115200);
    Serial2.begin(115200);  // From PC
    monitor.begin();
}

void loop() {
    if (monitor.update(Serial2)) {
        float cpuTemp = monitor. getCpuTemp();
        float gpuTemp = monitor.getGpuTemp();

        Serial.printf("CPU:  %.1f°C, GPU: %.1f°C\n", cpuTemp, gpuTemp);
    }

    if (monitor.isStale(3000)) {
        Serial.println("No data from PC!");
    }
}
```

## Configuration File

Settings are stored in:

```
%AppData%\HardwareMonitorTray\config.json
```

Sensor ID map stored in:

```
%AppData%\HardwareMonitorTray\sensor_map.json
```

## Troubleshooting

### "Access Denied" or no sensors found

Run the application as Administrator.

### COM port not listed

1. Check if the device is connected
2. Click "Refresh" button in settings
3. Verify the device in Windows Device Manager

### No data being sent

1. Ensure at least one sensor is selected
2. Verify COM port settings match your device
3. Use "🔄 Restart Serial" from tray menu

### Sensor IDs changed

The sensor map persists IDs. If you reset the map or delete `sensor_map.json`, IDs will be reassigned.

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

- [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
- [System.IO.Ports](https://www.nuget.org/packages/System.IO.Ports)

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Changelog

### v2.0

- **Protocol v2** - 16-bit sensor IDs
- **Reserved byte protection** - IDs cannot contain 0xAA or 0x55
- **Restart Serial** - New tray menu option
- **Improved sensor map** - Auto-fix invalid IDs
- **Updated MCU library** - Support for 16-bit IDs

### v1.0

- Initial release
- 8-bit sensor IDs
- Binary/Text/JSON protocols
