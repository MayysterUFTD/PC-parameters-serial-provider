/**
 * @file HWMonitor.h
 * @brief Hardware Monitor Protocol Parser Library
 * @version 2.0
 * @author Auto-generated
 * 
 * Universal library for parsing PC hardware sensor data. 
 * Compatible with:  Arduino, ESP32, ESP8266, STM32, Teensy, etc. 
 * 
 * Usage:
 *   1. Create instance: HWMonitor monitor;
 *   2. In setup(): monitor.begin();
 *   3. In loop(): monitor.update(Serial);  // or any Stream
 *   4. Read values: float temp = monitor.get(SENSOR_CPU_TEMP);
 */

#ifndef HW_MONITOR_H
#define HW_MONITOR_H

#include <Arduino. h>

/*===========================================================================*/
/*  CONFIGURATION                                                            */
/*===========================================================================*/

#ifndef HW_MAX_SENSORS
#define HW_MAX_SENSORS 250
#endif

#ifndef HW_RX_BUFFER_SIZE
#define HW_RX_BUFFER_SIZE 2048
#endif

#ifndef HW_TIMEOUT_MS
#define HW_TIMEOUT_MS 5000
#endif

/*===========================================================================*/
/*  PROTOCOL CONSTANTS                                                       */
/*===========================================================================*/

#define HW_PROTO_START      0xAA
#define HW_PROTO_END        0x55
#define HW_PROTO_VERSION    0x01

/*===========================================================================*/
/*  SENSOR IDs                                                               */
/*===========================================================================*/

// CPU Sensors (0x01 - 0x0F)
#define SENSOR_CPU_TEMP         0x01
#define SENSOR_CPU_LOAD         0x02
#define SENSOR_CPU_CLOCK        0x03
#define SENSOR_CPU_POWER        0x04
#define SENSOR_CPU_TEMP_CORE    0x05
#define SENSOR_CPU_LOAD_CORE    0x06
#define SENSOR_CPU_POWER_CORE   0x07
#define SENSOR_CPU_TEMP_CCD     0x08
#define SENSOR_CPU_VOLTAGE      0x09

// GPU Sensors (0x10 - 0x1F)
#define SENSOR_GPU_TEMP         0x10
#define SENSOR_GPU_LOAD         0x11
#define SENSOR_GPU_CLOCK        0x12
#define SENSOR_GPU_CLOCK_MEM    0x13
#define SENSOR_GPU_POWER        0x14
#define SENSOR_GPU_LOAD_MEM     0x15
#define SENSOR_GPU_FAN          0x16
#define SENSOR_GPU_TEMP_MEM     0x17
#define SENSOR_GPU_HOTSPOT      0x18
#define SENSOR_GPU_LOAD_VIDEO   0x19

// RAM Sensors (0x20 - 0x2F)
#define SENSOR_RAM_USED         0x20
#define SENSOR_RAM_AVAILABLE    0x21
#define SENSOR_RAM_LOAD         0x22

// Disk Sensors (0x30 - 0x3F)
#define SENSOR_DISK_TEMP        0x30
#define SENSOR_DISK_LOAD        0x31
#define SENSOR_DISK_READ        0x32
#define SENSOR_DISK_WRITE       0x33

// Network Sensors (0x40 - 0x4F)
#define SENSOR_NET_UP           0x40
#define SENSOR_NET_DOWN         0x41

// Motherboard Sensors (0x50 - 0x5F)
#define SENSOR_MB_TEMP          0x50
#define SENSOR_MB_FAN1          0x51
#define SENSOR_MB_FAN2          0x52
#define SENSOR_MB_FAN3          0x53
#define SENSOR_MB_VOLTAGE       0x55

// Battery Sensors (0x60 - 0x6F)
#define SENSOR_BATTERY_LEVEL    0x60
#define SENSOR_BATTERY_VOLTAGE  0x61
#define SENSOR_BATTERY_RATE     0x62

// Invalid/Unknown
#define SENSOR_UNKNOWN          0xFF

/*===========================================================================*/
/*  DATA STRUCTURES                                                          */
/*===========================================================================*/

/**
 * @brief Single sensor data
 */
struct HWSensor {
    uint8_t id;
    float value;
    bool valid;
    uint32_t timestamp;
};

/**
 * @brief Parser state machine states
 */
enum HWParserState {
    HW_STATE_IDLE,
    HW_STATE_VERSION,
    HW_STATE_COUNT,
    HW_STATE_DATA,
    HW_STATE_CRC_LOW,
    HW_STATE_CRC_HIGH,
    HW_STATE_END
};

/**
 * @brief Callback function type for new packet
 */
typedef void (*HWPacketCallback)(uint8_t sensorCount);

/**
 * @brief Callback function type for sensor update
 */
typedef void (*HWSensorCallback)(uint8_t id, float value);

/*===========================================================================*/
/*  MAIN CLASS                                                               */
/*===========================================================================*/

class HWMonitor {
public:
    /**
     * @brief Constructor
     */
    HWMonitor();
    
    /**
     * @brief Initialize the monitor
     */
    void begin();
    
    /**
     * @brief Reset all data
     */
    void reset();
    
    /**
     * @brief Update from a Stream (Serial, etc.)
     * @param stream Reference to input stream
     * @return true if a complete packet was parsed
     */
    bool update(Stream& stream);
    
    /**
     * @brief Process a single byte
     * @param byte Incoming byte
     * @return true if a complete packet was parsed
     */
    bool processByte(uint8_t byte);
    
    /**
     * @brief Parse a complete buffer
     * @param data Pointer to data
     * @param len Length of data
     * @return true if valid packet was parsed
     */
    bool parse(const uint8_t* data, size_t len);
    
    /**
     * @brief Get sensor value by ID
     * @param id Sensor ID
     * @param defaultValue Value to return if sensor not found
     * @return Sensor value or defaultValue
     */
    float get(uint8_t id, float defaultValue = -999.0f) const;
    
    /**
     * @brief Check if sensor has valid data
     * @param id Sensor ID
     * @return true if sensor data is valid
     */
    bool isValid(uint8_t id) const;
    
    /**
     * @brief Get sensor by index
     * @param index Sensor index (0 to sensorCount-1)
     * @return Pointer to sensor or nullptr
     */
    const HWSensor* getSensorByIndex(uint8_t index) const;
    
    /**
     * @brief Find sensor by ID
     * @param id Sensor ID
     * @return Pointer to sensor or nullptr
     */
    const HWSensor* findSensor(uint8_t id) const;
    
    /**
     * @brief Invalidate all sensors (call on timeout)
     */
    void invalidateAll();
    
    /**
     * @brief Check if data is stale
     * @param timeoutMs Timeout in milliseconds
     * @return true if last update was more than timeoutMs ago
     */
    bool isStale(uint32_t timeoutMs = HW_TIMEOUT_MS) const;
    
    /**
     * @brief Get time since last update
     * @return Milliseconds since last valid packet
     */
    uint32_t getAge() const;
    
    /**
     * @brief Set callback for new packet
     * @param callback Function to call when packet is received
     */
    void onPacket(HWPacketCallback callback);
    
    /**
     * @brief Set callback for sensor update
     * @param callback Function to call for each sensor
     */
    void onSensor(HWSensorCallback callback);
    
    // Convenience getters
    inline float getCpuTemp() const     { return get(SENSOR_CPU_TEMP); }
    inline float getCpuLoad() const     { return get(SENSOR_CPU_LOAD); }
    inline float getCpuClock() const    { return get(SENSOR_CPU_CLOCK); }
    inline float getCpuPower() const    { return get(SENSOR_CPU_POWER); }
    
    inline float getGpuTemp() const     { return get(SENSOR_GPU_TEMP); }
    inline float getGpuLoad() const     { return get(SENSOR_GPU_LOAD); }
    inline float getGpuClock() const    { return get(SENSOR_GPU_CLOCK); }
    inline float getGpuPower() const    { return get(SENSOR_GPU_POWER); }
    inline float getGpuFan() const      { return get(SENSOR_GPU_FAN); }
    inline float getGpuHotspot() const  { return get(SENSOR_GPU_HOTSPOT); }
    inline float getGpuMemLoad() const  { return get(SENSOR_GPU_LOAD_MEM); }
    
    inline float getRamUsed() const     { return get(SENSOR_RAM_USED); }
    inline float getRamLoad() const     { return get(SENSOR_RAM_LOAD); }
    
    inline float getDiskTemp() const    { return get(SENSOR_DISK_TEMP); }
    inline float getDiskLoad() const    { return get(SENSOR_DISK_LOAD); }
    
    inline float getNetUp() const       { return get(SENSOR_NET_UP); }
    inline float getNetDown() const     { return get(SENSOR_NET_DOWN); }
    
    // Statistics
    uint32_t packetsOK;
    uint32_t packetsError;
    uint8_t sensorCount;
    uint32_t lastUpdate;

private:
    HWSensor _sensors[HW_MAX_SENSORS];
    HWParserState _state;
    uint8_t _expectedCount;
    uint8_t _currentSensor;
    uint8_t _byteInSensor;
    uint8_t _tempId;
    uint8_t _tempValue[4];
    uint8_t _crcLow;
    uint8_t _crcHigh;
    
    HWPacketCallback _packetCallback;
    HWSensorCallback _sensorCallback;
    
    void _storeSensor();
    bool _finalizePacket();
    uint16_t _calculateCRC(const uint8_t* data, size_t len) const;
};

/*===========================================================================*/
/*  UTILITY FUNCTIONS                                                        */
/*===========================================================================*/

/**
 * @brief Get sensor name string
 * @param id Sensor ID
 * @return Human-readable sensor name
 */
const char* hwGetSensorName(uint8_t id);

/**
 * @brief Get sensor unit string
 * @param id Sensor ID
 * @return Unit string (°C, %, MHz, etc.)
 */
const char* hwGetSensorUnit(uint8_t id);

/**
 * @brief Get sensor category
 * @param id Sensor ID
 * @return Category string (CPU, GPU, RAM, etc.)
 */
const char* hwGetSensorCategory(uint8_t id);

#endif // HW_MONITOR_H