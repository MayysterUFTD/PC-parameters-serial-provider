/**
 * @file main.cpp
 * @brief Example usage of HWMonitor library
 */

#include <Arduino.h>
#include "HWMonitor.h"

// Dla ESP32-S3 z dwoma USB
#if defined(ARDUINO_USB_CDC_ON_BOOT) && ARDUINO_USB_CDC_ON_BOOT
    #include <USB. h>
    #include <USBCDC. h>
    USBCDC USBSerial;
    #define DATA_SERIAL USBSerial
    #define DEBUG_SERIAL Serial
#else
    // Dla innych płytek - jeden Serial
    #define DATA_SERIAL Serial
    #define DEBUG_SERIAL Serial
#endif

HWMonitor monitor;

// Callback wywoływany po odebraniu pakietu
void onPacketReceived(uint8_t sensorCount)
{
    DEBUG_SERIAL.printf("[HWMonitor] Packet received: %d sensors\n", sensorCount);
}

// Callback wywoływany dla każdego sensora (opcjonalnie)
void onSensorUpdate(uint8_t id, float value)
{
    // DEBUG_SERIAL.printf("  Sensor 0x%02X = %.1f\n", id, value);
}

void setup()
{
    // Debug output
    DEBUG_SERIAL.begin(115200);
    delay(2000);
    
    DEBUG_SERIAL.println();
    DEBUG_SERIAL.println("╔═══════════════════════════════════════╗");
    DEBUG_SERIAL.println("║     PC Hardware Monitor Receiver      ║");
    DEBUG_SERIAL. println("║         HWMonitor Library v2.0        ║");
    DEBUG_SERIAL. println("╚═══════════════════════════════════════╝");
    DEBUG_SERIAL.println();

#if defined(ARDUINO_USB_CDC_ON_BOOT) && ARDUINO_USB_CDC_ON_BOOT
    // ESP32-S3 z USB-OTG
    USBSerial.setRxBufferSize(4096);
    USBSerial.begin();
    USB.begin();
    DEBUG_SERIAL.println("[USB] USB-OTG CDC initialized");
#endif

    // Initialize monitor
    monitor.begin();
    monitor.onPacket(onPacketReceived);
    // monitor.onSensor(onSensorUpdate);  // Uncomment for per-sensor callbacks
    
    DEBUG_SERIAL.println("[OK] Ready!  Waiting for data...");
    DEBUG_SERIAL. println();
}

void displayStats()
{
    static uint32_t lastDisplay = 0;
    
    if (millis() - lastDisplay < 1000) return;
    lastDisplay = millis();
    
    // Check for timeout
    if (monitor.isStale(5000)) {
        monitor.invalidateAll();
    }
    
    float cpuTemp = monitor.getCpuTemp();
    float cpuLoad = monitor. getCpuLoad();
    float gpuTemp = monitor. getGpuTemp();
    float gpuLoad = monitor. getGpuLoad();
    float ramLoad = monitor.getRamLoad();
    
    DEBUG_SERIAL.println("┌─────────────────────────────────────┐");
    DEBUG_SERIAL.println("│        PC HARDWARE STATUS           │");
    DEBUG_SERIAL. println("├─────────────────────────────────────┤");
    
    if (cpuTemp > -900) {
        DEBUG_SERIAL.printf("│ CPU:   %5.1f°C   Load:  %5.1f%%        │\n", cpuTemp, cpuLoad);
        DEBUG_SERIAL. printf("│ GPU:  %5.1f°C   Load: %5.1f%%        │\n", gpuTemp, gpuLoad);
        DEBUG_SERIAL. printf("│ RAM:  %5.1f%%                       │\n", ramLoad);
        DEBUG_SERIAL. println("├─────────────────────────────────────┤");
        DEBUG_SERIAL.printf("│ Sensors:  %3d  Age: %4lums           │\n", 
                           monitor. sensorCount, monitor.getAge());
        DEBUG_SERIAL. printf("│ Packets: OK=%lu ERR=%lu             │\n",
                           monitor. packetsOK, monitor.packetsError);
    } else {
        DEBUG_SERIAL.println("│ Waiting for data from PC...          │");
        DEBUG_SERIAL. println("│                                     │");
        DEBUG_SERIAL. println("│ Connect USB to PC and start         │");
        DEBUG_SERIAL.println("│ Hardware Monitor application        │");
    }
    
    DEBUG_SERIAL. println("└─────────────────────────────────────┘");
    DEBUG_SERIAL.println();
}

void listAllSensors()
{
    static uint32_t lastList = 0;
    
    if (millis() - lastList < 10000) return;
    lastList = millis();
    
    if (monitor.sensorCount == 0) return;
    
    DEBUG_SERIAL. println("\n=== ALL SENSORS ===");
    
    for (uint8_t i = 0; i < monitor.sensorCount; i++) {
        const HWSensor* sensor = monitor.getSensorByIndex(i);
        if (sensor && sensor->valid) {
            DEBUG_SERIAL.printf("[0x%02X] %-20s = %8.1f %s\n",
                               sensor->id,
                               hwGetSensorName(sensor->id),
                               sensor->value,
                               hwGetSensorUnit(sensor->id));
        }
    }
    
    DEBUG_SERIAL.println("===================\n");
}

void loop()
{
    // Update from data serial
    if (monitor.update(DATA_SERIAL)) {
        // New packet received
        DEBUG_SERIAL.printf("[OK] Received %d sensors!\n", monitor.sensorCount);
    }
    
    displayStats();
    listAllSensors();
    
    delay(10);
}