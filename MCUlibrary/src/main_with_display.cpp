/**
 * @file main_with_display.cpp
 * @brief Wersja z wyświetlaczem TFT (np. ST7789, ILI9341)
 */

#include <Arduino. h>
#include <USB.h>
#include <USBCDC. h>
#include <TFT_eSPI.h>
#include "hw_monitor.h"

USBCDC USBSerial;
TFT_eSPI tft;

/* Kolory */
#define BG_COLOR      TFT_BLACK
#define TITLE_COLOR   TFT_CYAN
#define LABEL_COLOR   TFT_WHITE
#define VALUE_COLOR   TFT_GREEN
#define WARN_COLOR    TFT_YELLOW
#define CRIT_COLOR    TFT_RED

void setup()
{
    Serial.begin(115200);
    
    /* TFT */
    tft. init();
    tft.setRotation(1);
    tft.fillScreen(BG_COLOR);
    tft.setTextColor(TITLE_COLOR, BG_COLOR);
    tft.setTextSize(2);
    tft.drawString("PC MONITOR", 10, 10);
    
    /* USB */
    USBSerial.begin();
    USB.begin();
    
    hw_monitor_init();
    
    Serial.println("Ready!");
}

uint32_t getColorForTemp(float temp)
{
    if (temp >= 85) return CRIT_COLOR;
    if (temp >= 70) return WARN_COLOR;
    return VALUE_COLOR;
}

uint32_t getColorForLoad(float load)
{
    if (load >= 90) return CRIT_COLOR;
    if (load >= 70) return WARN_COLOR;
    return VALUE_COLOR;
}

void drawBar(int x, int y, int w, int h, float value, uint32_t color)
{
    int fill = (int)(value * w / 100.0f);
    if (fill > w) fill = w;
    if (fill < 0) fill = 0;
    
    tft.fillRect(x, y, w, h, TFT_DARKGREY);
    tft.fillRect(x, y, fill, h, color);
    tft.drawRect(x, y, w, h, TFT_WHITE);
}

void updateDisplay()
{
    static unsigned long lastUpdate = 0;
    if (millis() - lastUpdate < 250) return;
    lastUpdate = millis();
    
    float cpu_temp = hw_get_cpu_temp();
    float cpu_load = hw_get_cpu_load();
    float gpu_temp = hw_get_gpu_temp();
    float gpu_load = hw_get_gpu_load();
    float ram_load = hw_get_ram_load();
    
    int y = 50;
    
    /* CPU */
    tft.setTextColor(LABEL_COLOR, BG_COLOR);
    tft.drawString("CPU:", 10, y);
    
    if (cpu_temp > -900) {
        tft.setTextColor(getColorForTemp(cpu_temp), BG_COLOR);
        tft.drawString(String(cpu_temp, 1) + "C  ", 70, y);
        
        drawBar(150, y, 100, 16, cpu_load, getColorForLoad(cpu_load));
        
        tft.setTextColor(VALUE_COLOR, BG_COLOR);
        tft.drawString(String(cpu_load, 0) + "% ", 260, y);
    } else {
        tft.setTextColor(TFT_DARKGREY, BG_COLOR);
        tft.drawString("--.-C    ", 70, y);
    }
    
    y += 30;
    
    /* GPU */
    tft.setTextColor(LABEL_COLOR, BG_COLOR);
    tft. drawString("GPU:", 10, y);
    
    if (gpu_temp > -900) {
        tft.setTextColor(getColorForTemp(gpu_temp), BG_COLOR);
        tft.drawString(String(gpu_temp, 1) + "C  ", 70, y);
        
        drawBar(150, y, 100, 16, gpu_load, getColorForLoad(gpu_load));
        
        tft.setTextColor(VALUE_COLOR, BG_COLOR);
        tft.drawString(String(gpu_load, 0) + "% ", 260, y);
    }
    
    y += 30;
    
    /* RAM */
    tft.setTextColor(LABEL_COLOR, BG_COLOR);
    tft. drawString("RAM:", 10, y);
    
    if (ram_load > -900) {
        drawBar(70, y, 150, 16, ram_load, getColorForLoad(ram_load));
        
        tft.setTextColor(VALUE_COLOR, BG_COLOR);
        tft.drawString(String(ram_load, 0) + "% ", 230, y);
    }
    
    y += 40;
    
    /* Stats */
    tft.setTextSize(1);
    tft.setTextColor(TFT_DARKGREY, BG_COLOR);
    tft.drawString("Packets:  " + String(hw_monitor.packets_ok) + 
                   " OK / " + String(hw_monitor.packets_err) + " ERR  ", 10, y);
    tft.setTextSize(2);
}

void loop()
{
    /* Process USB data */
    while (USBSerial.available()) {
        if (hw_monitor_process_byte(USBSerial.read())) {
            Serial.printf("Packet OK:  %d sensors\n", hw_monitor.sensor_count);
        }
    }
    
    /* Timeout */
    if (millis() - hw_monitor.last_update > 10000) {
        hw_monitor_invalidate_all();
    }
    
    updateDisplay();
    delay(10);
}