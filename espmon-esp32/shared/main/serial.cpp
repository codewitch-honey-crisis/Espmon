#include "serial.hpp"

#include <driver/gpio.h>
#include <driver/uart.h>
#include <esp_err.h>
#include <esp_idf_version.h>
#include <esp_log.h>
#include <memory.h>

#define FRAME_HEADER_LENGTH (8 + 4 + 4)
#define FRAME_TOTAL (FRAME_HEADER_LENGTH + max_payload_size)
#define SERIAL_QUEUE_SIZE (2 * FRAME_TOTAL)
#define SERIAL_BUF_SIZE (2 * SERIAL_QUEUE_SIZE)
static const char* TAG = "Serial";
static uint8_t* frame_rx_data=nullptr;
static size_t max_payload_size = 0;
static uint8_t frame_start;
static size_t frame_start_count=0;
static bool initialized = false;

static uint8_t rx_frame_next = 0;
static uint32_t crc32(const uint8_t* data, size_t length, uint32_t seed = UINT32_MAX / 3) {
    uint32_t result = seed;
    while (length--) {
        result ^= *data++;
    }
    return result;
}

static void write_frame_marker(uint8_t cmd, uint8_t* ptr) {
    cmd +=128;
    ptr[0]=cmd;
    ptr[1]=cmd;
    ptr[2]=cmd;
    ptr[3]=cmd;
    ptr[4]=cmd;
    ptr[5]=cmd;
    ptr[6]=cmd;
    ptr[7]=cmd;
}
static void write_frame_length(size_t length, uint8_t* ptr) {
    uint32_t len = (uint32_t)length;
    *(uint32_t*)(ptr+8)=len;
}
static void write_frame_crc(uint32_t crc,uint8_t* ptr) {
    *(uint32_t*)(ptr+12)=crc;
}
static void write_frame_header(uint8_t cmd, void* frame, size_t length, uint8_t* ptr) {
    write_frame_marker(cmd,ptr);
    write_frame_length(length,ptr);
    write_frame_crc(crc32((const uint8_t*)frame,length),ptr);
}

static int8_t cmd_from_frame(const uint8_t* frame) {
    return (frame[0]==frame[1]&&frame[0]==frame[2]&&frame[0]==frame[3]&&
    frame[0]==frame[4]&&frame[0]==frame[5]&&frame[0]==frame[6]&&frame[0]==frame[7])?(int8_t)(frame[0]-128):0;
}
static size_t length_from_frame(const uint8_t* frame) {
    uint32_t* result = (uint32_t*)(frame+8);
    return (size_t)*result;
}
static size_t crc_from_frame(const uint8_t* frame) {
    uint32_t* result = (uint32_t*)(frame+12);
    return (size_t)*result;
}
static bool read_frame_marker() {
    // puts("CHECK DATA");
    int length = 0;
    if(ESP_OK!=uart_get_buffered_data_len(UART_NUM_0, (size_t*)&length)) {
        return false;
    }
    if(length==0) {
        return false;
    }
    uint8_t b;
    int bytesRead = uart_read_bytes(UART_NUM_0,&b,1,pdMS_TO_TICKS(50));
    if(bytesRead<1) {
        //ESP_LOGE(TAG, "Serial read error reading frame");
        return false;
    }
    if(frame_start_count == 0) {
        if(b<128) {
            return false;
        }
        frame_start = b;
        ++frame_start_count;
        frame_rx_data[0]=b;
        return false;
    } 
    if(frame_start_count<8) {
        if(frame_start!=b) {
            frame_start_count = 0;
            frame_start=b;
            return false;
        }
        frame_rx_data[frame_start_count]=frame_start;
        ++frame_start_count;
        if(frame_start_count==8) {
            frame_start_count = 0;
            return true;
        }
        return false;
    }
    frame_start_count = 0;
    return false;
}
static bool read_frame_header() {
    if(!read_frame_marker()) {
        return false;
    }
    uint8_t* p = frame_rx_data+8;
   
    int bytes_read = uart_read_bytes(UART_NUM_0, p, FRAME_HEADER_LENGTH-8, pdMS_TO_TICKS(1000));
    if (bytes_read < (FRAME_HEADER_LENGTH-8)) {
        ESP_LOGE(TAG, "Serial read error reading frame");
        return false;
    }
   
    return true;
}
static bool read_frame() {
    if(!read_frame_header()) {
        return false;
    }
    if(cmd_from_frame(frame_rx_data)==0) return false;
    size_t len = length_from_frame(frame_rx_data);
    uint32_t crc = crc_from_frame(frame_rx_data);
    uint8_t* p = frame_rx_data+FRAME_HEADER_LENGTH;
    if(len>max_payload_size) {
        ESP_LOGE(TAG,"Serial corruption reading frame");
        return false;
    }
    if(len>0) {
        int bytes_read = uart_read_bytes(UART_NUM_0, p, len, pdMS_TO_TICKS(1000));
        if (bytes_read < 0) {
            ESP_LOGE(TAG, "Serial read error reading frame");
            return false;
        }
    }
    if(crc!=crc32(frame_rx_data+FRAME_HEADER_LENGTH,len)) {
        ESP_LOGE(TAG, "CRC32 check failed for frame");
        return false;
    }
    return true;
}

bool serial_init(size_t max_payload) {
    if(initialized) return true;
    esp_log_level_set(TAG, ESP_LOG_INFO);
    max_payload_size = max_payload?max_payload:8192;
    frame_rx_data = (uint8_t*)malloc(FRAME_TOTAL);
    if(frame_rx_data==nullptr) {
        ESP_LOGE(TAG, "Unable to allocate serial frame buffer");
        goto error;
    }
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
    if (ESP_OK != uart_driver_install(UART_NUM_0, SERIAL_BUF_SIZE * 2, 0, 20, nullptr, 0)) {
        ESP_LOGE(TAG, "Unable to install uart driver");
        goto error;
    }
    uart_param_config(UART_NUM_0, &uart_config);
    // Set UART pins (using UART0 default pins ie no changes.)
    uart_set_pin(UART_NUM_0, UART_PIN_NO_CHANGE, UART_PIN_NO_CHANGE, UART_PIN_NO_CHANGE, UART_PIN_NO_CHANGE);
    initialized = true;
    return true;
error:
    if(frame_rx_data!=nullptr) {
        free(frame_rx_data);
        frame_rx_data = nullptr;
    }
    return false;
}
bool serial_try_get_frame(uint8_t* out_cmd,void** out_ptr, size_t* out_length) {
    if(!initialized || rx_frame_next==0 || !out_cmd || !out_ptr || !out_length) {
        return false;
    }
    *out_cmd = cmd_from_frame(frame_rx_data);
    *out_ptr = frame_rx_data + FRAME_HEADER_LENGTH;
    *out_length = length_from_frame(frame_rx_data);
    rx_frame_next = 0;
    return true;
}
static bool try_write(const uint8_t* data, size_t length) {
    int tries=10;
    size_t total = 0;
    while(total<length && --tries) {
        int written = uart_write_bytes(UART_NUM_0,data, length-total);
        if(written<0) {
            ESP_LOGE(TAG,"Write error");
            return false;
        } 
        if(ESP_OK!=uart_wait_tx_done(UART_NUM_0,pdMS_TO_TICKS(5000))) {
            ESP_LOGE(TAG,"Timout error during write");
            return false;
        }
        total += written;
        data+=written;
    }
    if(tries<=0) {
        ESP_LOGE(TAG,"Retry count exceeded error during write");
        return false;
    }
    return true;
}
bool serial_put_frame(uint8_t cmd, void* frame, size_t frame_length) {
    if(!initialized || cmd==0 || cmd>127) {
        return false;
    }
    uint8_t frame_header[FRAME_HEADER_LENGTH];
    write_frame_header(cmd, frame, frame_length, frame_header);
    if(!try_write(frame_header,FRAME_HEADER_LENGTH)) { return false; }
    if(frame_length==0) return true;
    return try_write((const uint8_t*)frame,frame_length);
}
bool serial_discard_frame() {
    if(!initialized || rx_frame_next==0) {
        return false;
    }
    rx_frame_next = 0;
    return true;
}

bool serial_update() {
    if(!initialized) {
        return false;
    }
    if(rx_frame_next!=0) {
        return true; // already have a frame waiting so we don't want to process more
    }
    if(read_frame()) {
        rx_frame_next = cmd_from_frame(frame_rx_data);
    } else {
        rx_frame_next = 0;
    }
    return true;
}