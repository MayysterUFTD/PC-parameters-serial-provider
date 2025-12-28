/**
 * Alternatywa: USB do debug, UART na pinach do danych
 * Wymaga konwertera USB-UART (np. CP2102, CH340)
 */

#include "driver/uart.h"

#define DATA_UART       UART_NUM_1
#define DATA_TX_PIN     GPIO_NUM_17
#define DATA_RX_PIN     GPIO_NUM_18
#define DATA_BAUD       115200

static void uart_data_init(void)
{
    uart_config_t cfg = {
        . baud_rate = DATA_BAUD,
        .data_bits = UART_DATA_8_BITS,
        . parity = UART_PARITY_DISABLE,
        .stop_bits = UART_STOP_BITS_1,
        .flow_ctrl = UART_HW_FLOWCTRL_DISABLE,
    };
    
    uart_driver_install(DATA_UART, 1024, 0, 0, NULL, 0);
    uart_param_config(DATA_UART, &cfg);
    uart_set_pin(DATA_UART, DATA_TX_PIN, DATA_RX_PIN, -1, -1);
}

static void uart_rx_task(void* arg)
{
    uint8_t buf[512];
    
    while (1) {
        int len = uart_read_bytes(DATA_UART, buf, sizeof(buf), pdMS_TO_TICKS(100));
        
        if (len > 0) {
            if (hw_monitor_parse(buf, len)) {
                printf("Data received: %d sensors\n", hw_monitor.sensor_count);
            }
        }
    }
}