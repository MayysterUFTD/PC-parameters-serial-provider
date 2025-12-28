/**
 * @file HWMonitor. cpp
 * @brief Hardware Monitor Protocol Parser Implementation
 */

#include "HWMonitor.h"

/*===========================================================================*/
/*  CONSTRUCTOR & INITIALIZATION                                             */
/*===========================================================================*/

HWMonitor::HWMonitor()
    : packetsOK(0), packetsError(0), sensorCount(0), lastUpdate(0), _state(HW_STATE_IDLE), _expectedCount(0), _currentSensor(0), _byteInSensor(0), _tempId(0), _crcLow(0), _crcHigh(0), _packetCallback(nullptr), _sensorCallback(nullptr)
{
}

void HWMonitor::begin()
{
    reset();
}

void HWMonitor::reset()
{
    for (uint8_t i = 0; i < HW_MAX_SENSORS; i++)
    {
        _sensors[i].id = SENSOR_UNKNOWN;
        _sensors[i].value = -999.0f;
        _sensors[i].valid = false;
        _sensors[i].timestamp = 0;
    }

    _state = HW_STATE_IDLE;
    sensorCount = 0;
    packetsOK = 0;
    packetsError = 0;
    lastUpdate = 0;
}

/*===========================================================================*/
/*  STREAM UPDATE                                                            */
/*===========================================================================*/

bool HWMonitor::update(Stream &stream)
{
    bool packetReceived = false;

    while (stream.available())
    {
        if (processByte(stream.read()))
        {
            packetReceived = true;
        }
    }

    return packetReceived;
}

/*===========================================================================*/
/*  BYTE-BY-BYTE PARSER                                                      */
/*===========================================================================*/

bool HWMonitor::processByte(uint8_t byte)
{
    switch (_state)
    {
    case HW_STATE_IDLE:
        if (byte == HW_PROTO_START)
        {
            _state = HW_STATE_VERSION;
        }
        break;

    case HW_STATE_VERSION:
        if (byte == HW_PROTO_VERSION)
        {
            _state = HW_STATE_COUNT;
        }
        else
        {
            _state = HW_STATE_IDLE;
            packetsError++;
        }
        break;

    case HW_STATE_COUNT:
        _expectedCount = byte;
        _currentSensor = 0;
        _byteInSensor = 0;

        if (byte > 0 && byte <= HW_MAX_SENSORS)
        {
            _state = HW_STATE_DATA;
        }
        else
        {
            _state = HW_STATE_IDLE;
            packetsError++;
        }
        break;

    case HW_STATE_DATA:
        if (_byteInSensor == 0)
        {
            _tempId = byte;
            _byteInSensor = 1;
        }
        else
        {
            _tempValue[_byteInSensor - 1] = byte;
            _byteInSensor++;

            if (_byteInSensor == 5)
            {
                _storeSensor();
                _currentSensor++;
                _byteInSensor = 0;

                if (_currentSensor >= _expectedCount)
                {
                    _state = HW_STATE_CRC_LOW;
                }
            }
        }
        break;

    case HW_STATE_CRC_LOW:
        _crcLow = byte;
        _state = HW_STATE_CRC_HIGH;
        break;

    case HW_STATE_CRC_HIGH:
        _crcHigh = byte;
        _state = HW_STATE_END;
        break;

    case HW_STATE_END:
        _state = HW_STATE_IDLE;

        if (byte == HW_PROTO_END)
        {
            return _finalizePacket();
        }
        else
        {
            packetsError++;
        }
        break;
    }

    return false;
}

void HWMonitor::_storeSensor()
{
    if (_currentSensor >= HW_MAX_SENSORS)
        return;

    // Convert bytes to float (little-endian)
    union
    {
        float f;
        uint8_t b[4];
    } converter;

    converter.b[0] = _tempValue[0];
    converter.b[1] = _tempValue[1];
    converter.b[2] = _tempValue[2];
    converter.b[3] = _tempValue[3];

    _sensors[_currentSensor].id = _tempId;
    _sensors[_currentSensor].value = converter.f;
    _sensors[_currentSensor].valid = true;
    _sensors[_currentSensor].timestamp = millis();

    // Call sensor callback if set
    if (_sensorCallback)
    {
        _sensorCallback(_tempId, converter.f);
    }
}

bool HWMonitor::_finalizePacket()
{
    sensorCount = _expectedCount;
    lastUpdate = millis();
    packetsOK++;

    // Call packet callback if set
    if (_packetCallback)
    {
        _packetCallback(sensorCount);
    }

    return true;
}

/*===========================================================================*/
/*  BUFFER PARSER                                                            */
/*===========================================================================*/

bool HWMonitor::parse(const uint8_t *data, size_t len)
{
    if (!data || len < 6)
        return false;

    // Find start byte
    size_t startIdx = 0;
    while (startIdx < len && data[startIdx] != HW_PROTO_START)
    {
        startIdx++;
    }

    if (startIdx >= len)
        return false;

    const uint8_t *pkt = data + startIdx;
    size_t remaining = len - startIdx;

    // Check header
    if (remaining < 3)
        return false;
    if (pkt[1] != HW_PROTO_VERSION)
    {
        packetsError++;
        return false;
    }

    uint8_t count = pkt[2];
    size_t expectedLen = 3 + (count * 5) + 3;

    if (remaining < expectedLen)
    {
        packetsError++;
        return false;
    }

    if (pkt[expectedLen - 1] != HW_PROTO_END)
    {
        packetsError++;
        return false;
    }

    // Parse sensor data
    size_t offset = 3;
    for (uint8_t i = 0; i < count && i < HW_MAX_SENSORS; i++)
    {
        _sensors[i].id = pkt[offset];

        union
        {
            float f;
            uint8_t b[4];
        } converter;

        converter.b[0] = pkt[offset + 1];
        converter.b[1] = pkt[offset + 2];
        converter.b[2] = pkt[offset + 3];
        converter.b[3] = pkt[offset + 4];

        _sensors[i].value = converter.f;
        _sensors[i].valid = true;
        _sensors[i].timestamp = millis();

        if (_sensorCallback)
        {
            _sensorCallback(_sensors[i].id, converter.f);
        }

        offset += 5;
    }

    sensorCount = count;
    lastUpdate = millis();
    packetsOK++;

    if (_packetCallback)
    {
        _packetCallback(sensorCount);
    }

    return true;
}

/*===========================================================================*/
/*  DATA ACCESS                                                              */
/*===========================================================================*/

float HWMonitor::get(uint8_t id, float defaultValue) const
{
    for (uint8_t i = 0; i < sensorCount; i++)
    {
        if (_sensors[i].id == id && _sensors[i].valid)
        {
            return _sensors[i].value;
        }
    }
    return defaultValue;
}

bool HWMonitor::isValid(uint8_t id) const
{
    for (uint8_t i = 0; i < sensorCount; i++)
    {
        if (_sensors[i].id == id)
        {
            return _sensors[i].valid;
        }
    }
    return false;
}

const HWSensor *HWMonitor::getSensorByIndex(uint8_t index) const
{
    if (index < sensorCount)
    {
        return &_sensors[index];
    }
    return nullptr;
}

const HWSensor *HWMonitor::findSensor(uint8_t id) const
{
    for (uint8_t i = 0; i < sensorCount; i++)
    {
        if (_sensors[i].id == id)
        {
            return &_sensors[i];
        }
    }
    return nullptr;
}

void HWMonitor::invalidateAll()
{
    for (uint8_t i = 0; i < HW_MAX_SENSORS; i++)
    {
        _sensors[i].valid = false;
    }
}

bool HWMonitor::isStale(uint32_t timeoutMs) const
{
    if (lastUpdate == 0)
        return true;
    return (millis() - lastUpdate) > timeoutMs;
}

uint32_t HWMonitor::getAge() const
{
    if (lastUpdate == 0)
        return UINT32_MAX;
    return millis() - lastUpdate;
}

void HWMonitor::onPacket(HWPacketCallback callback)
{
    _packetCallback = callback;
}

void HWMonitor::onSensor(HWSensorCallback callback)
{
    _sensorCallback = callback;
}

/*===========================================================================*/
/*  CRC CALCULATION                                                          */
/*===========================================================================*/

uint16_t HWMonitor::_calculateCRC(const uint8_t *data, size_t len) const
{
    uint16_t crc = 0xFFFF;

    for (size_t i = 0; i < len; i++)
    {
        crc ^= data[i];
        for (uint8_t j = 0; j < 8; j++)
        {
            if (crc & 0x0001)
            {
                crc = (crc >> 1) ^ 0xA001;
            }
            else
            {
                crc = crc >> 1;
            }
        }
    }

    return crc;
}

/*===========================================================================*/
/*  UTILITY FUNCTIONS                                                        */
/*===========================================================================*/

const char *hwGetSensorName(uint8_t id)
{
    switch (id)
    {
    // CPU
    case SENSOR_CPU_TEMP:
        return "CPU Temperature";
    case SENSOR_CPU_LOAD:
        return "CPU Load";
    case SENSOR_CPU_CLOCK:
        return "CPU Clock";
    case SENSOR_CPU_POWER:
        return "CPU Power";
    case SENSOR_CPU_TEMP_CORE:
        return "CPU Core Temp";
    case SENSOR_CPU_LOAD_CORE:
        return "CPU Core Load";
    case SENSOR_CPU_POWER_CORE:
        return "CPU Core Power";
    case SENSOR_CPU_TEMP_CCD:
        return "CPU CCD Temp";
    case SENSOR_CPU_VOLTAGE:
        return "CPU Voltage";

    // GPU
    case SENSOR_GPU_TEMP:
        return "GPU Temperature";
    case SENSOR_GPU_LOAD:
        return "GPU Load";
    case SENSOR_GPU_CLOCK:
        return "GPU Clock";
    case SENSOR_GPU_CLOCK_MEM:
        return "GPU Memory Clock";
    case SENSOR_GPU_POWER:
        return "GPU Power";
    case SENSOR_GPU_LOAD_MEM:
        return "GPU Memory Load";
    case SENSOR_GPU_FAN:
        return "GPU Fan";
    case SENSOR_GPU_TEMP_MEM:
        return "GPU Memory Temp";
    case SENSOR_GPU_HOTSPOT:
        return "GPU Hotspot";
    case SENSOR_GPU_LOAD_VIDEO:
        return "GPU Video Load";

    // RAM
    case SENSOR_RAM_USED:
        return "RAM Used";
    case SENSOR_RAM_AVAILABLE:
        return "RAM Available";
    case SENSOR_RAM_LOAD:
        return "RAM Load";

    // Disk
    case SENSOR_DISK_TEMP:
        return "Disk Temperature";
    case SENSOR_DISK_LOAD:
        return "Disk Load";
    case SENSOR_DISK_READ:
        return "Disk Read";
    case SENSOR_DISK_WRITE:
        return "Disk Write";

    // Network
    case SENSOR_NET_UP:
        return "Network Upload";
    case SENSOR_NET_DOWN:
        return "Network Download";

    // Motherboard
    case SENSOR_MB_TEMP:
        return "Motherboard Temp";
    case SENSOR_MB_FAN1:
        return "System Fan 1";
    case SENSOR_MB_FAN2:
        return "System Fan 2";
    case SENSOR_MB_FAN3:
        return "System Fan 3";
    case SENSOR_MB_VOLTAGE:
        return "System Voltage";

    // Battery
    case SENSOR_BATTERY_LEVEL:
        return "Battery Level";
    case SENSOR_BATTERY_VOLTAGE:
        return "Battery Voltage";
    case SENSOR_BATTERY_RATE:
        return "Battery Rate";

    default:
        return "Unknown";
    }
}

const char *hwGetSensorUnit(uint8_t id)
{
    switch (id)
    {
    // Temperatures
    case SENSOR_CPU_TEMP:
    case SENSOR_CPU_TEMP_CORE:
    case SENSOR_CPU_TEMP_CCD:
    case SENSOR_GPU_TEMP:
    case SENSOR_GPU_TEMP_MEM:
    case SENSOR_GPU_HOTSPOT:
    case SENSOR_DISK_TEMP:
    case SENSOR_MB_TEMP:
        return "°C";

    // Loads/Percentages
    case SENSOR_CPU_LOAD:
    case SENSOR_CPU_LOAD_CORE:
    case SENSOR_GPU_LOAD:
    case SENSOR_GPU_LOAD_MEM:
    case SENSOR_GPU_LOAD_VIDEO:
    case SENSOR_RAM_LOAD:
    case SENSOR_DISK_LOAD:
    case SENSOR_BATTERY_LEVEL:
        return "%";

    // Clocks
    case SENSOR_CPU_CLOCK:
    case SENSOR_GPU_CLOCK:
    case SENSOR_GPU_CLOCK_MEM:
        return "MHz";

    // Power
    case SENSOR_CPU_POWER:
    case SENSOR_CPU_POWER_CORE:
    case SENSOR_GPU_POWER:
    case SENSOR_BATTERY_RATE:
        return "W";

    // Voltage
    case SENSOR_CPU_VOLTAGE:
    case SENSOR_MB_VOLTAGE:
    case SENSOR_BATTERY_VOLTAGE:
        return "V";

    // Fan
    case SENSOR_GPU_FAN:
    case SENSOR_MB_FAN1:
    case SENSOR_MB_FAN2:
    case SENSOR_MB_FAN3:
        return "RPM";

    // Data sizes
    case SENSOR_RAM_USED:
    case SENSOR_RAM_AVAILABLE:
        return "GB";

    // Throughput
    case SENSOR_DISK_READ:
    case SENSOR_DISK_WRITE:
    case SENSOR_NET_UP:
    case SENSOR_NET_DOWN:
        return "MB/s";

    default:
        return "";
    }
}

const char *hwGetSensorCategory(uint8_t id)
{
    if (id >= 0x01 && id <= 0x0F)
        return "CPU";
    if (id >= 0x10 && id <= 0x1F)
        return "GPU";
    if (id >= 0x20 && id <= 0x2F)
        return "RAM";
    if (id >= 0x30 && id <= 0x3F)
        return "Disk";
    if (id >= 0x40 && id <= 0x4F)
        return "Network";
    if (id >= 0x50 && id <= 0x5F)
        return "Motherboard";
    if (id >= 0x60 && id <= 0x6F)
        return "Battery";
    if (id >= 0x80 && id <= 0xFE)
        return "Custom";
    return "Unknown";
}