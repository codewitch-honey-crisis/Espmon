#include "serial.h"

#include <driver/gpio.h>
#include <driver/uart.h>
#include <esp_err.h>
#include <esp_idf_version.h>
#include <esp_log.h>
#include <memory.h>

static bool initialized = false;
static const char* TAG = "Serial";
bool serial_init(size_t max_payload) {
    if(initialized) return true;
    esp_log_level_set(TAG, ESP_LOG_INFO);
    /* Configure parameters of an UART driver,
     * communication pins and install the driver */
    uart_config_t uart_config;
    memset(&uart_config, 0, sizeof(uart_config));
    uart_config.baud_rate = 115200;
    uart_config.data_bits = UART_DATA_8_BITS;
    uart_config.parity = UART_PARITY_DISABLE;
    uart_config.stop_bits = UART_STOP_BITS_1;
    uart_config.flow_ctrl = UART_HW_FLOWCTRL_DISABLE;
    // Install UART driver, and get the queue.
    if (ESP_OK != uart_driver_install(UART_NUM_0, max_payload * 2, 0, 20, NULL, 0)) {
        ESP_LOGE(TAG, "Unable to install uart driver");
        goto error;
    }
    uart_param_config(UART_NUM_0, &uart_config);
    // Set UART pins (using UART0 default pins ie no changes.)
    uart_set_pin(UART_NUM_0, UART_PIN_NO_CHANGE, UART_PIN_NO_CHANGE, UART_PIN_NO_CHANGE, UART_PIN_NO_CHANGE);
    initialized = true;
    return true;
error:
    return false;
}
int serial_getc() {
    uint8_t tmp;
    if(1==uart_read_bytes(UART_NUM_0,&tmp,1,0)) {
        return tmp;
    }
    return -1;
}
void serial_putc(int value) {
    uint8_t tmp = value;
    uart_write_bytes(UART_NUM_0,&tmp,1);
}
