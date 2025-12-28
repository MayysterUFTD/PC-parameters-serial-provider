/**
 * @file buffer_test. cpp
 * @brief Test rozmiaru bufora USB CDC
 */

#include <Arduino.h>
#include <USB.h>
#include <USBCDC.h>

USBCDC USBSerial;

void setup()
{
    Serial.begin(115200);
    delay(1000);
    
    Serial.println("USB Buffer Size Test");
    Serial.println("====================");
    
    /* Zwiększ bufor RX */
    USBSerial.setRxBufferSize(4096);  // Domyślnie może być 256! 
    
    USBSerial.begin();
    USB.begin();
    
    Serial.println("Ready. Send large packet...");
}

void loop()
{
    static uint8_t buf[4096];
    static size_t total = 0;
    
    int avail = USBSerial.available();
    if (avail > 0) {
        size_t chunk = USBSerial.readBytes(buf, min(avail, (int)sizeof(buf)));
        total += chunk;
        
        Serial.printf("[RX] Chunk:  %d bytes, Total: %d bytes, Available after: %d\n", 
                     chunk, total, USBSerial.available());
        
        /* Analizuj pierwszy i ostatni bajt */
        if (chunk > 0) {
            Serial.printf("     First: 0x%02X, Last: 0x%02X\n", buf[0], buf[chunk-1]);
            
            /* Sprawdź czy to kompletny pakiet */
            if (buf[0] == 0xAA && chunk >= 3) {
                uint8_t count = buf[2];
                size_t expected = 3 + (count * 5) + 3;
                Serial.printf("     Sensor count: %d, Expected size: %d\n", count, expected);
                
                if (chunk >= expected) {
                    Serial.printf("     END byte: 0x%02X %s\n", 
                                 buf[expected-1],
                                 buf[expected-1] == 0x55 ? "✓" : "✗");
                }
            }
        }
        
        /* Reset po 500ms ciszy */
        static unsigned long last_rx = 0;
        last_rx = millis();
    }
    
    delay(1);
}