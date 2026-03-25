#ifdef ESP_IDF
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "freertos/task.h"
#include "driver/uart.h"
#include "esp_idf_version.h"
#include "esp_random.h"
#include "esp_system.h"
#include "esp_mac.h"
#include "driver/gpio.h"
#include <memory.h>
#include <stdio.h>
#include "frame.h"
#include "interface_buffers.h"


int serial_getch(void) {
    uint8_t tmp;
    if(1==uart_read_bytes(UART_NUM_0,&tmp,1,pdMS_TO_TICKS(100))) {
        return tmp;
    }
    return -1;
}
bool serial_putch(uint8_t value) {
    if(1==uart_write_bytes(UART_NUM_0,&value,1)) {
        //uart_wait_tx_done(UART_NUM_0,pdMS_TO_TICKS(100));
        return true;
    }
    return false;
}

bool serial_init(size_t queue_size) {
    uart_config_t uart_config;
    memset(&uart_config, 0, sizeof(uart_config));
    uart_config.baud_rate = 115200;
    uart_config.data_bits = UART_DATA_8_BITS;
    uart_config.parity = UART_PARITY_DISABLE;
    uart_config.stop_bits = UART_STOP_BITS_1;
    uart_config.flow_ctrl = UART_HW_FLOWCTRL_DISABLE;
    // Install UART driver, and get the queue.
    if (ESP_OK != uart_driver_install(UART_NUM_0, queue_size, 0, 20, NULL, 0)) {
        goto error;
    }
    uart_param_config(UART_NUM_0, &uart_config);
    // Set UART pins (using UART0 default pins ie no changes.)
    uart_set_pin(UART_NUM_0, UART_PIN_NO_CHANGE, UART_PIN_NO_CHANGE, UART_PIN_NO_CHANGE, UART_PIN_NO_CHANGE);
    return true;
error:
    return false;
}

static void loop();
static void loop_task(void* arg) {
    TickType_t wdt_ts = xTaskGetTickCount();
    while(1) {
        TickType_t ticks = xTaskGetTickCount();
        if(ticks>wdt_ts+pdMS_TO_TICKS(200)) {
            wdt_ts = ticks;
            vTaskDelay(5);
        }
        loop();
    }
}
static frame_handle_t frame_handle = NULL;
static int serial_read(void* state) {
    (void)state;
    return serial_getch();
}
static int serial_write(uint8_t value, void* state) {
    (void)state;
    if(!serial_putch(value)) {
        return FRAME_ERROR_OVERFLOW;
    }
    return 0;
}
void app_main() {
    serial_init(8192);
    frame_handle = frame_create(INTERFACE_MAX_SIZE,serial_read,NULL,serial_write,NULL);
    TaskHandle_t loop_handle;
    xTaskCreate(loop_task,"loop_task",8192,NULL,1,&loop_handle);
}
typedef struct {
    uint8_t* ptr;
    size_t remaining;
} buffer_write_cursor_t;
typedef struct {
    const uint8_t* ptr;
    size_t remaining;
} buffer_read_cursor_t;
int on_write_buffer(uint8_t value, void* state) {
    buffer_write_cursor_t* cur = (buffer_write_cursor_t*)state;
    if(cur->remaining==0) {
        return BUFFERS_ERROR_EOF;
    }
    *cur->ptr++=value;
    --cur->remaining;
    return 1;
}
int on_read_buffer(void* state) {
    buffer_read_cursor_t* cur = (buffer_read_cursor_t*)state;
    if(cur->remaining==0) {
        return BUFFERS_EOF;
    }
    uint8_t result = *cur->ptr++;
    --cur->remaining;
    return result;
}
static void loop() {
    static uint8_t msg_buffer[INTERFACE_MAX_SIZE];
    int cmd;
    void* ptr;
    size_t length;
    cmd = frame_get(frame_handle,&ptr,&length);
    if(cmd>0) {
        buffer_read_cursor_t read_cur = {(const uint8_t*)ptr,length};
        // the following is only used when we need to respond
        buffer_write_cursor_t write_cur = {msg_buffer,INTERFACE_MAX_SIZE};
        switch((st_message_command_t)cmd) {
            case CMD_RNG: {
                st_rng_message_t msg;
                if(-1<st_rng_message_read(&msg,on_read_buffer,&read_cur)) {
                    puts("RNG generation requested");
                    st_rng_response_message_t resp;
                    resp.value = esp_random();
                    int count = st_rng_response_message_write(&resp,on_write_buffer,&write_cur);
                    frame_put(frame_handle, CMD_RNG_RESPONSE,msg_buffer,count);
                }
            }
            break;
            case CMD_GPIO_GET: {
                st_gpio_get_message_t msg;
                uint64_t result = 0;
                if(-1<st_gpio_get_message_read(&msg,on_read_buffer,&read_cur)) {
                    for(int i = 0; i<64;++i) {
                        if(0!=(msg.mask & (((uint64_t)1)<<i))) {
                            printf("GPIO get request for %d\n",(int)i);
                            if(gpio_get_level((gpio_num_t)i)) {
                                result |= (((uint64_t)1)<<i);
                            }
                        }
                    }
                    st_gpio_get_response_message_t resp;
                    resp.values = result;
                    int count = st_gpio_get_response_message_write(&resp,on_write_buffer,&write_cur);
                    frame_put(frame_handle, CMD_GPIO_GET_RESPONSE,msg_buffer,count);
                }
            }
            break;
            case CMD_GPIO_SET: {
                st_gpio_set_message_t msg;
                if(-1<st_gpio_set_message_read(&msg,on_read_buffer,&read_cur)) {
                    for(int i = 0; i<64;++i) {
                        if(0!=(msg.mask & (((uint64_t)1)<<i))) {
                            printf("GPIO set level request for %d ",(int)i);
                            if(0!=(msg.values&(((uint64_t)1)<<i))) {
                                puts("to on");
                                gpio_set_level((gpio_num_t)i,1);
                            } else {
                                puts("to off");
                                gpio_set_level((gpio_num_t)i,0);
                            }
                        }
                    }
                }
            }
            break;
            case CMD_GPIO_MODE: {
                st_gpio_mode_message_t msg;
                if(-1<st_gpio_mode_message_read(&msg,on_read_buffer,&read_cur)) {
                    printf("GPIO set mode for %d\n",(int)msg.gpio);
                    switch(msg.mode) {
                        case MODE_INPUT:
                            gpio_set_direction((gpio_num_t)msg.gpio,GPIO_MODE_INPUT);
                            gpio_set_pull_mode((gpio_num_t)msg.gpio,GPIO_FLOATING);
                            break;
                        case MODE_INPUT_PULLUP:
                            gpio_set_direction((gpio_num_t)msg.gpio,GPIO_MODE_INPUT);
                            gpio_set_pull_mode((gpio_num_t)msg.gpio,GPIO_PULLUP_ONLY);
                            break;
                        case MODE_INPUT_PULLDOWN:
                            gpio_set_direction((gpio_num_t)msg.gpio,GPIO_MODE_INPUT);
                            gpio_set_pull_mode((gpio_num_t)msg.gpio,GPIO_PULLDOWN_ONLY);
                            break;
                        case MODE_OUTPUT:
                            gpio_set_direction((gpio_num_t)msg.gpio,GPIO_MODE_OUTPUT);
                            break;
                        case MODE_OUTPUT_OPEN_DRAIN:
                            gpio_set_direction((gpio_num_t)msg.gpio,GPIO_MODE_OUTPUT_OD);
                            break;
                    }
                    
                }
            }
            break;
            default: {
                printf("Unknown command received %d\n",(int)cmd);
            }
            break;
        }
    }
}
#endif // ESP_IDF