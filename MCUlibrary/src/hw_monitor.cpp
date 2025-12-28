#include "hw_monitor.h"

HWMonitor hw_monitor;

void hw_monitor_init()
{
    memset(&hw_monitor, 0, sizeof(hw_monitor));
    for (int i = 0; i < HW_MAX_SENSORS; i++) {
        hw_monitor.sensors[i].id = 0xFF;
        hw_monitor.sensors[i].value = -999.0f;
        hw_monitor.sensors[i].valid = false;
    }
}

bool hw_monitor_parse(const uint8_t* data, size_t len)
{
    if (! data || len < 6) return false;
    if (data[0] != HW_PROTO_START) return false;
    if (data[1] != HW_PROTO_VERSION) return false;
    
    uint8_t count = data[2];
    size_t expected = 3 + (count * 5) + 3;
    
    if (len < expected) return false;
    if (data[expected - 1] != HW_PROTO_END) {
        hw_monitor.packets_err++;
        return false;
    }
    
    size_t offset = 3;
    for (uint8_t i = 0; i < count && i < HW_MAX_SENSORS; i++) {
        hw_monitor.sensors[i].id = data[offset];
        
        union { float f; uint8_t b[4]; } conv;
        conv. b[0] = data[offset + 1];
        conv.b[1] = data[offset + 2];
        conv.b[2] = data[offset + 3];
        conv.b[3] = data[offset + 4];
        
        hw_monitor.sensors[i].value = conv.f;
        hw_monitor.sensors[i].valid = true;
        
        offset += 5;
    }
    
    hw_monitor.sensor_count = count;
    hw_monitor.packets_ok++;
    hw_monitor.last_update = millis();
    
    return true;
}

float hw_monitor_get(uint8_t id)
{
    for (int i = 0; i < hw_monitor.sensor_count; i++) {
        if (hw_monitor.sensors[i].id == id && hw_monitor.sensors[i].valid) {
            return hw_monitor.sensors[i].value;
        }
    }
    return -999.0f;
}