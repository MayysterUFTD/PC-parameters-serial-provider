/**
 * @file main.c
 * @brief ESP-IDF Hardware Monitor Example
 */

#include <stdio.h>
#include "freertos/FreeRTOS. h"
#include "freertos/task.h"
#include "driver/uart.h"
#include "driver/gpio.h"
#include "esp_log.h"
#include "hw_monitor.h"

static const char* TAG = "HW_MON";

#define UART_PORT      UART_NUM_1
#define UART_TX_PIN    GPIO_NUM_17
#define UART_RX_PIN    GPIO_NUM_16
#define UART_BAUD      115200
#define BUF_SIZE       1024

static void uart_init(void)
{
    uart_config_t cfg = {
        . baud_rate = UART_BAUD,
        . data_bits = UART_DATA_8_BITS,
        .parity = UART_PARITY_DISABLE,
        .stop_bits = UART_STOP_BITS_1,
        .flow_ctrl = UART_HW_FLOWCTRL_DISABLE,
        .source_clk = UART_SCLK_DEFAULT,
    };
    
    uart_driver_install(UART_PORT, BUF_SIZE * 2, 0, 0, NULL, 0);
    uart_param_config(UART_PORT, &cfg);
    uart_set_pin(UART_PORT, UART_TX_PIN, UART_RX_PIN, -1, -1);
    
    ESP_LOGI(TAG, "UART initialized:  TX=%d, RX=%d, %d baud", 
             UART_TX_PIN, UART_RX_PIN, UART_BAUD);
}

static void uart_task(void* arg)
{
    uint8_t buf[BUF_SIZE];
    
    while (1) {
        int len = uart_read_bytes(UART_PORT, buf, BUF_SIZE, pdMS_TO_TICKS(100));
        
        if (len > 0) {
            /* Option 1: Parse complete buffer */
            if (hw_monitor_parse(buf, len)) {
                ESP_LOGI(TAG, "Packet OK (%d sensors)", hw_monitor.sensor_count);
            }
            
            /* Option 2: Process byte-by-byte (for streaming)
            for (int i = 0; i < len; i++) {
                if (hw_monitor_process_byte(buf[i])) {
                    ESP_LOGI(TAG, "Packet OK");
                }
            }
            */
        }
        
        /* Timeout check */
        if (hw_monitor_age_ms() > 5000) {
            hw_monitor_invalidate_all();
        }
    }
}

static void display_task(void* arg)
{
    while (1) {
        float cpu_temp = hw_get_cpu_temp();
        float cpu_load = hw_get_cpu_load();
        float gpu_temp = hw_get_gpu_temp();
        float gpu_load = hw_get_gpu_load();
        float ram_load = hw_get_ram_load();
        
        if (cpu_temp > -900) {
            printf("\n");
            printf("╔═══════════════════════════════╗\n");
            printf("║     PC HARDWARE MONITOR       ║\n");
            printf("╠═══════════════════════════════╣\n");
            printf("║ CPU:  %5.1f°C  Load: %5.1f%%   ║\n", cpu_temp, cpu_load);
            printf("║ GPU: %5.1f°C  Load: %5.1f%%   ║\n", gpu_temp, gpu_load);
            printf("║ RAM: %5.1f%%                  ║\n", ram_load);
            printf("╠═══════════════════════════════╣\n");
            printf("║ Packets:  OK=%lu ERR=%lu       ║\n", 
                   hw_monitor.packets_ok, hw_monitor. packets_err);
            printf("╚═══════════════════════════════╝\n");
        } else {
            printf("Waiting for data from PC...\n");
        }
        
        vTaskDelay(pdMS_TO_TICKS(1000));
    }
}

void app_main(void)
{
    ESP_LOGI(TAG, "Hardware Monitor starting...");
    
    hw_monitor_init();
    uart_init();
    
    xTaskCreate(uart_task, "uart", 4096, NULL, 10, NULL);
    xTaskCreate(display_task, "display", 4096, NULL, 5, NULL);
    
    ESP_LOGI(TAG, "Ready!");
}