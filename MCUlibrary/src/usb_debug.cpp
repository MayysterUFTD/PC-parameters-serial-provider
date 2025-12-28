/**
 * @file usb_debug. cpp
 * @brief USB Debug Tool - pokazuje surowe dane przychodzące
 */

#include <Arduino.h>
#include <USB.h>
#include <USBCDC.h>

USBCDC USBSerial;

/* Bufor */
#define BUF_SIZE 2048
uint8_t buffer[BUF_SIZE];
size_t buf_pos = 0;

/* Stats */
uint32_t total_bytes = 0;
uint32_t packets_found = 0;
unsigned long last_rx_time = 0;

void setup()
{
    Serial.begin(115200);
    delay(1000);
    
    Serial.println();
    Serial.println("========================================");
    Serial.println("     USB RAW DATA DEBUGGER v1.0");
    Serial.println("========================================");
    Serial.println();
    Serial.println("Waiting for USB-OTG connection...");
    
    USBSerial.begin();
    USB.productName("HW Debug");
    USB.begin();
    
    Serial.println("USB-OTG ready. Connect PC app.");
    Serial.println();
}

void printHex(uint8_t* data, size_t len, size_t max_show = 64)
{
    size_t show = min(len, max_show);
    
    for (size_t i = 0; i < show; i++) {
        if (i > 0 && i % 16 == 0) Serial.println();
        Serial.printf("%02X ", data[i]);
    }
    
    if (len > max_show) {
        Serial.printf("... (+%d more)", len - max_show);
    }
    Serial.println();
}

void analyzePacket(uint8_t* data, size_t len)
{
    Serial.println();
    Serial.println("╔══════════════════════════════════════════════════════════════╗");
    Serial.printf( "║ PACKET ANALYSIS - %d bytes                                   \n", len);
    Serial.println("╠══════════════════════════════════════════════════════════════╣");
    
    /* Szukaj znacznika START (0xAA) */
    int start_count = 0;
    int end_count = 0;
    int first_start = -1;
    int first_end = -1;
    
    for (size_t i = 0; i < len; i++) {
        if (data[i] == 0xAA) {
            start_count++;
            if (first_start < 0) first_start = i;
        }
        if (data[i] == 0x55) {
            end_count++;
            if (first_end < 0 && first_start >= 0) first_end = i;
        }
    }
    
    Serial.printf("║ START bytes (0xAA): %d found\n", start_count);
    Serial.printf("║ END bytes (0x55):   %d found\n", end_count);
    Serial.println("║");
    
    if (first_start >= 0) {
        Serial. printf("║ First START at index:  %d\n", first_start);
        
        if (first_start + 2 < (int)len) {
            uint8_t version = data[first_start + 1];
            uint8_t count = data[first_start + 2];
            
            Serial.printf("║ Version byte: 0x%02X %s\n", version, 
                         version == 0x01 ? "(OK)" : "(WRONG!  Expected 0x01)");
            Serial. printf("║ Sensor count: %d\n", count);
            
            /* Oblicz oczekiwaną długość */
            size_t expected_len = 3 + (count * 5) + 3;  // header + data + crc + end
            Serial.printf("║ Expected packet length: %d bytes\n", expected_len);
            Serial.printf("║ Available from START: %d bytes\n", len - first_start);
            
            if (len - first_start >= expected_len) {
                Serial.println("║ ✓ Enough data for complete packet");
                
                /* Sprawdź END byte */
                uint8_t end_byte = data[first_start + expected_len - 1];
                Serial. printf("║ END byte at expected position: 0x%02X %s\n", 
                             end_byte, end_byte == 0x55 ? "(OK)" : "(WRONG!)");
                
                /* Pokaż pierwsze sensory */
                Serial.println("║");
                Serial.println("║ First 10 sensors:");
                size_t offset = first_start + 3;
                for (int i = 0; i < min((int)count, 10); i++) {
                    uint8_t id = data[offset];
                    
                    union { float f; uint8_t b[4]; } val;
                    val.b[0] = data[offset + 1];
                    val.b[1] = data[offset + 2];
                    val. b[2] = data[offset + 3];
                    val.b[3] = data[offset + 4];
                    
                    Serial.printf("║   [%2d] ID=0x%02X Value=%. 2f\n", i, id, val.f);
                    offset += 5;
                }
                
                if (count > 10) {
                    Serial.printf("║   ... and %d more sensors\n", count - 10);
                }
                
            } else {
                Serial.println("║ ✗ NOT enough data - packet truncated!");
                Serial.printf("║   Missing: %d bytes\n", expected_len - (len - first_start));
            }
        }
    } else {
        Serial.println("║ ✗ No START byte (0xAA) found!");
    }
    
    Serial.println("║");
    Serial.println("║ RAW DATA (first 128 bytes):");
    Serial.print("║ ");
    printHex(data, len, 128);
    
    Serial.println("╚══════════════════════════════════════════════════════════════╝");
    Serial.println();
}

void loop()
{
    /* Odbierz dane */
    while (USBSerial.available()) {
        if (buf_pos < BUF_SIZE) {
            buffer[buf_pos++] = USBSerial. read();
            total_bytes++;
            last_rx_time = millis();
        } else {
            USBSerial.read(); // Discard
            Serial.println("[WARN] Buffer overflow!");
        }
    }
    
    /* Jeśli minęło 100ms od ostatniego bajtu - analizuj */
    if (buf_pos > 0 && millis() - last_rx_time > 100) {
        packets_found++;
        
        Serial.println();
        Serial.println("════════════════════════════════════════════════════════════════");
        Serial.printf("RECEIVED CHUNK #%d:  %d bytes (total: %d bytes)\n", 
                     packets_found, buf_pos, total_bytes);
        Serial.println("════════════════════════════════════════════════════════════════");
        
        analyzePacket(buffer, buf_pos);
        
        /* Reset bufora */
        buf_pos = 0;
    }
    
    /* Status co 5 sekund */
    static unsigned long last_status = 0;
    if (millis() - last_status > 5000) {
        last_status = millis();
        Serial.printf("[STATUS] Total bytes: %d, Chunks: %d, Buffer: %d\n", 
                     total_bytes, packets_found, buf_pos);
    }
}