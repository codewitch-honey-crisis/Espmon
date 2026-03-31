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
int serial_getc(void) {
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
#ifdef CONFIG_SOC_USB_SERIAL_JTAG_SUPPORTED
#include "driver/usb_serial_jtag.h"

bool serial2_init(size_t queue_size) {
    usb_serial_jtag_driver_config_t usb_config;
    memset(&usb_config,0,sizeof(usb_config));
    usb_config.rx_buffer_size = queue_size;
    usb_config.tx_buffer_size = queue_size;
    if(ESP_OK!=usb_serial_jtag_driver_install(&usb_config)) {
        return false;
    }
    return true;
}
int serial2_getc(void) {
    uint8_t tmp;
    if(1==usb_serial_jtag_read_bytes(&tmp,1,0)) {
        return tmp;
    }
    return -1;
}
bool serial2_putc(int value) {
    uint8_t tmp=value;
    if(1==usb_serial_jtag_write_bytes(&tmp,1,pdMS_TO_TICKS(100))) {
        //uart_wait_tx_done(UART_NUM_0,pdMS_TO_TICKS(100));
        return true;
    }
    return false;
}
#endif