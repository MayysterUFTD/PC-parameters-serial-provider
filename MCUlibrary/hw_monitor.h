/**
 * @file hw_monitor.h
 * @brief Hardware Monitor Parser Library for ESP-IDF
 * @version 1.0
 * 
 * Usage:
 *   1. Call hw_monitor_init() once at startup
 *   2. Feed UART data to hw_monitor_process_byte() or hw_monitor_parse()
 *   3. Read values with hw_monitor_get_xxx() functions
 */

#ifndef HW_MONITOR_H
#define HW_MONITOR_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint. h>
#include <stdbool.h>
#include <stddef.h>

/*===========================================================================*/
/*  PROTOCOL CONSTANTS                                                       */
/*===========================================================================*/

#define HW_PROTO_START      0xAA
#define HW_PROTO_END        0x55
#define HW_PROTO_VERSION    0x01
#define HW_MAX_SENSORS      64
#define HW_RX_BUFFER_SIZE   512

/*===========================================================================*/
/*  PREDEFINED SENSOR IDs                                                    */
/*===========================================================================*/

/* CPU Sensors */
#define SENSOR_CPU_TEMP_PKG     0x01
#define SENSOR_CPU_LOAD_TOTAL   0x02
#define SENSOR_CPU_CLOCK        0x03
#define SENSOR_CPU_POWER_PKG    0x04
#define SENSOR_CPU_TEMP_CORE    0x05
#define SENSOR_CPU_LOAD_CORE    0x06
#define SENSOR_CPU_POWER_CORE   0x07
#define SENSOR_CPU_TEMP_CCD     0x08
#define SENSOR_CPU_VOLTAGE      0x09

/* GPU Sensors */
#define SENSOR_GPU_TEMP_CORE    0x10
#define SENSOR_GPU_LOAD_CORE    0x11
#define SENSOR_GPU_CLOCK_CORE   0x12
#define SENSOR_GPU_CLOCK_MEM    0x13
#define SENSOR_GPU_POWER        0x14
#define SENSOR_GPU_LOAD_MEM     0x15
#define SENSOR_GPU_FAN          0x16
#define SENSOR_GPU_TEMP_MEM     0x17
#define SENSOR_GPU_TEMP_HOTSPOT 0x18
#define SENSOR_GPU_LOAD_VIDEO   0x19

/* RAM Sensors */
#define SENSOR_RAM_USED         0x20
#define SENSOR_RAM_AVAIL        0x21
#define SENSOR_RAM_LOAD         0x22

/* Disk Sensors */
#define SENSOR_DISK_TEMP        0x30
#define SENSOR_DISK_LOAD        0x31
#define SENSOR_DISK_READ        0x32
#define SENSOR_DISK_WRITE       0x33

/* Network Sensors */
#define SENSOR_NET_UP           0x40
#define SENSOR_NET_DOWN         0x41

/*===========================================================================*/
/*  DATA TYPES                                                               */
/*===========================================================================*/

/**
 * @brief Single sensor data
 */
typedef struct {
    uint8_t id;           /**< Sensor ID */
    float   value;        /**< Current value */
    bool    valid;        /**< Data validity */
    int64_t timestamp_ms; /**< Last update timestamp */
} hw_sensor_data_t;

/**
 * @brief Parser state machine
 */
typedef enum {
    HW_STATE_IDLE,
    HW_STATE_VERSION,
    HW_STATE_COUNT,
    HW_STATE_DATA,
    HW_STATE_CRC_LOW,
    HW_STATE_CRC_HIGH,
    HW_STATE_END
} hw_parser_state_t;

/**
 * @brief Monitor context
 */
typedef struct {
    hw_sensor_data_t sensors[HW_MAX_SENSORS];
    uint8_t          sensor_count;
    uint8_t          rx_buffer[HW_RX_BUFFER_SIZE];
    size_t           rx_pos;
    hw_parser_state_t state;
    uint8_t          expected_count;
    uint8_t          current_sensor;
    uint8_t          byte_in_sensor;
    uint32_t         packets_ok;
    uint32_t         packets_err;
    int64_t          last_update_ms;
} hw_monitor_t;

/*===========================================================================*/
/*  GLOBAL INSTANCE                                                          */
/*===========================================================================*/

extern hw_monitor_t hw_monitor;

/*===========================================================================*/
/*  INITIALIZATION                                                           */
/*===========================================================================*/

/**
 * @brief Initialize hardware monitor
 */
void hw_monitor_init(void);

/*===========================================================================*/
/*  PARSING                                                                  */
/*===========================================================================*/

/**
 * @brief Process single byte (call from UART ISR or task)
 * @param byte Received byte
 * @return true if complete packet was parsed
 */
bool hw_monitor_process_byte(uint8_t byte);

/**
 * @brief Parse complete buffer
 * @param data Buffer pointer
 * @param len Buffer length
 * @return true if valid packet found and parsed
 */
bool hw_monitor_parse(const uint8_t* data, size_t len);

/*===========================================================================*/
/*  DATA ACCESS                                                              */
/*===========================================================================*/

/**
 * @brief Get sensor value by ID
 * @param id Sensor ID
 * @return Value or -999.0f if not found/invalid
 */
float hw_monitor_get(uint8_t id);

/**
 * @brief Check if sensor data is valid
 * @param id Sensor ID
 * @return true if valid
 */
bool hw_monitor_valid(uint8_t id);

/**
 * @brief Get sensor pointer by ID
 * @param id Sensor ID
 * @return Pointer to sensor data or NULL
 */
hw_sensor_data_t* hw_monitor_find(uint8_t id);

/**
 * @brief Invalidate all sensors (call on timeout)
 */
void hw_monitor_invalidate_all(void);

/**
 * @brief Get time since last update
 * @return Milliseconds since last valid packet
 */
int64_t hw_monitor_age_ms(void);

/*===========================================================================*/
/*  CONVENIENCE GETTERS                                                      */
/*===========================================================================*/

/* CPU */
static inline float hw_get_cpu_temp(void)       { return hw_monitor_get(SENSOR_CPU_TEMP_PKG); }
static inline float hw_get_cpu_load(void)       { return hw_monitor_get(SENSOR_CPU_LOAD_TOTAL); }
static inline float hw_get_cpu_clock(void)      { return hw_monitor_get(SENSOR_CPU_CLOCK); }
static inline float hw_get_cpu_power(void)      { return hw_monitor_get(SENSOR_CPU_POWER_PKG); }

/* GPU */
static inline float hw_get_gpu_temp(void)       { return hw_monitor_get(SENSOR_GPU_TEMP_CORE); }
static inline float hw_get_gpu_load(void)       { return hw_monitor_get(SENSOR_GPU_LOAD_CORE); }
static inline float hw_get_gpu_clock(void)      { return hw_monitor_get(SENSOR_GPU_CLOCK_CORE); }
static inline float hw_get_gpu_power(void)      { return hw_monitor_get(SENSOR_GPU_POWER); }
static inline float hw_get_gpu_fan(void)        { return hw_monitor_get(SENSOR_GPU_FAN); }
static inline float hw_get_gpu_hotspot(void)    { return hw_monitor_get(SENSOR_GPU_TEMP_HOTSPOT); }

/* RAM */
static inline float hw_get_ram_used(void)       { return hw_monitor_get(SENSOR_RAM_USED); }
static inline float hw_get_ram_load(void)       { return hw_monitor_get(SENSOR_RAM_LOAD); }

/* Disk */
static inline float hw_get_disk_temp(void)      { return hw_monitor_get(SENSOR_DISK_TEMP); }

#ifdef __cplusplus
}
#endif

#endif /* HW_MONITOR_H */