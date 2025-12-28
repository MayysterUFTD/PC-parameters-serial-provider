/**
 * @file usb_stream_debug.cpp  
 * @brief Pokazuje każdy bajt na żywo + statystyki
 */

#include <Arduino.h>
#include <USB.h>
#include <USBCDC.h>

USBCDC USBSerial;

/* Analiza pakietu w locie */
enum State { IDLE, GOT_START, GOT_VER, GOT_COUNT, IN_DATA, IN_CRC1, IN_CRC2, GOT_END };
State state = IDLE;
uint8_t sensor_count = 0;
uint8_t current_sensor = 0;
uint8_t byte_in_sensor = 0;
size_t packet_size = 0;

/* Stats */
uint32_t total_bytes = 0;
uint32_t valid_packets = 0;
uint32_t invalid_packets = 0;
uint32_t max_sensor_count = 0;

void setup()
{
    Serial.begin(115200);
    delay(1000);
    
    Serial.println();
    Serial.println("╔════════════════════════════════════════╗");
    Serial.println("║   USB STREAM DEBUGGER - Live View      ║");
    Serial.println("╚════════════════════════════════════════╝");
    Serial.println();
    
    USBSerial.begin();
    USB.productName("Stream Debug");
    USB.begin();
    
    Serial.println("Ready. Start sending data...");
    Serial.println();
}

void processByte(uint8_t b)
{
    total_bytes++;
    
    switch (state) {
        case IDLE: 
            if (b == 0xAA) {
                state = GOT_START;
                packet_size = 1;
                Serial. print("\n[PKT] START ");
            }
            break;
            
        case GOT_START:
            packet_size++;
            if (b == 0x01) {
                state = GOT_VER;
                Serial.print("VER=01 ");
            } else {
                Serial.printf("BAD_VER=%02X ", b);
                state = IDLE;
                invalid_packets++;
            }
            break;
            
        case GOT_VER:
            packet_size++;
            sensor_count = b;
            current_sensor = 0;
            byte_in_sensor = 0;
            state = IN_DATA;
            Serial. printf("CNT=%d ", sensor_count);
            
            if (sensor_count > max_sensor_count) {
                max_sensor_count = sensor_count;
            }
            
            if (sensor_count == 0 || sensor_count > 255) {
                Serial.print("(INVALID COUNT!) ");
                state = IDLE;
                invalid_packets++;
            }
            break;
            
        case IN_DATA:
            packet_size++;
            byte_in_sensor++;
            
            if (byte_in_sensor == 5) {
                current_sensor++;
                byte_in_sensor = 0;
                
                /* Pokaż progress co 50 sensorów */
                if (current_sensor % 50 == 0) {
                    Serial.printf("[%d/%d] ", current_sensor, sensor_count);
                }
                
                if (current_sensor >= sensor_count) {
                    state = IN_CRC1;
                    Serial. printf("DATA_OK(%d) ", current_sensor);
                }
            }
            break;
            
        case IN_CRC1:
            packet_size++;
            state = IN_CRC2;
            break;
            
        case IN_CRC2:
            packet_size++;
            state = GOT_END;
            break;
            
        case GOT_END:
            packet_size++;
            if (b == 0x55) {
                Serial.printf("END=55 SIZE=%d ✓\n", packet_size);
                valid_packets++;
            } else {
                Serial.printf("BAD_END=%02X SIZE=%d ✗\n", b, packet_size);
                invalid_packets++;
            }
            state = IDLE;
            break;
    }
}

void printStats()
{
    Serial.println();
    Serial.println("┌────────────────────────────────────────┐");
    Serial.printf( "│ Total bytes:       %10d           │\n", total_bytes);
    Serial.printf( "│ Valid packets:     %10d           │\n", valid_packets);
    Serial.printf( "│ Invalid packets:   %10d           │\n", invalid_packets);
    Serial.printf( "│ Max sensor count: %10d           │\n", max_sensor_count);
    Serial.printf( "│ Current state:    %s                │\n", 
                  state == IDLE ?  "IDLE" :  
                  state == IN_DATA ? "IN_DATA" : "OTHER");
    Serial.println("└────────────────────────────────────────┘");
}

void loop()
{
    while (USBSerial.available()) {
        processByte(USBSerial.read());
    }
    
    /* Stats co 10 sekund */
    static unsigned long last_stats = 0;
    if (millis() - last_stats > 10000) {
        last_stats = millis();
        printStats();
    }
}