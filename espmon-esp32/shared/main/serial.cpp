#if __has_include(<Arduino.h>)
#include <Arduino.h>
#include "serial_config.h"
#else
#include <driver/uart.h>
#include <driver/gpio.h>
#endif
#include <memory.h>
#include <stdio.h>
#include <stdint.h>
#include <esp_idf_version.h>
#include <esp_err.h>
#include <esp_log.h>
#include "serial.hpp"
#define SERIAL_QUEUE_SIZE 256
#define SERIAL_BUF_SIZE (2*SERIAL_QUEUE_SIZE)
const char* TAG = "Serial";
#ifdef TEST_NO_SERIAL
#include "esp_random.h"
static int8_t waiting = -1;
static int index_requested = -1;
static response_color_t to_resp_color(uint8_t* data) {
    response_color_t result;
    result.a = data[3];
    result.r = data[0];
    result.g = data[1];
    result.b = data[2];
    return result;
}
#endif
static const uint8_t* read_response_header(const uint8_t* data, response_screen_header_t* out) {
    out->index = (int8_t)data[0];
    out->flags = data[1];
    return data+2;
}
static const uint8_t* read_response_color(const uint8_t* data, response_color_t* out) {
    out->a = data[0];
    out->r = data[1];
    out->g = data[2];
    out->b = data[3];
    return data+4;
}
static const uint8_t* read_response_screen_value_entry(const uint8_t* data, response_screen_value_entry_t* out) {
    data = read_response_color(data,&out->color);
    memcpy(out->suffix,data,12);
    return data+12;
}
static const uint8_t* read_response_screen_entry(const uint8_t* data, response_screen_entry_t* out) {
    memcpy(out->label,data,16);
    data += 16;
    data = read_response_color(data,&out->color);
    data = read_response_screen_value_entry(data,&out->value1);
    data = read_response_screen_value_entry(data,&out->value2);
    return data;
}
static void read_response_screen(const uint8_t* data, response_screen_t* out) {
    data = read_response_header(data,&out->header);
    data = read_response_screen_entry(data,&out->top);
    read_response_screen_entry(data,&out->bottom);
}
static const uint8_t* read_response_value(const uint8_t* data, response_value_t* out) {
    memcpy(&out->value,data,4);
    data+=4;
    memcpy(&out->scaled,data,4);
    data+=4;
    return data;
}

static const uint8_t* read_response_value_entry(const uint8_t* data, response_value_entry_t* out) {
    data = read_response_value(data,&out->value1);
    data = read_response_value(data,&out->value2);
    return data;
}
static void read_response_data(const uint8_t* data, response_data_t* out) {
    data = read_response_value_entry(data,&out->top);
    read_response_value_entry(data,&out->bottom);
}
int8_t serial_read_packet(response_t* out_resp) {
#ifndef TEST_NO_SERIAL
    uint8_t tmp;
    uint8_t data[sizeof(response_t)];
    if(1==uart_read_bytes(UART_NUM_0,&tmp,1,0)) {
        if(tmp==0) {
            if(0<uart_read_bytes(UART_NUM_0,data,108,portMAX_DELAY)) {
                read_response_screen(data,&out_resp->screen);
                return tmp;
            }
        }
        if(tmp==1) {
            if(0<uart_read_bytes(UART_NUM_0,data,32,portMAX_DELAY)) {
                read_response_data(data,&out_resp->data);
                return tmp;
            }
        }
        if(tmp==3) {
            return 3;
        }
        if(tmp==4) {
            return 4;
        }
        if(tmp==5) {
            return 5;
        }
    }
    return -1;
#else
    if(waiting==CMD_SCREEN) {
        response_screen_t* pr = (response_screen_t*)out_resp;
        pr->header.flags = (1<<1)|(1<<3);
        pr->header.index = 0;
        pr->top.color = to_resp_color((uint8_t[]){ (uint8_t)(0.67843137254902*0xFF), (uint8_t)(0.847058823529412*0xFF), (uint8_t)(0.901960784313726*0xFF),0xFF});
        strcpy(pr->top.label,"CPU");
        pr->top.value1.color=to_resp_color((uint8_t[]){0x00,0xFF,0x00,0xFF});
        strcpy(pr->top.value1.suffix, "GHz");
        pr->top.value2.color=to_resp_color((uint8_t[]){0xFF,0x7F,0x00,0xFF});
        strcpy(pr->top.value2.suffix, "\xC2\xB0");
        pr->bottom.color = to_resp_color((uint8_t[]){0xFF, (uint8_t)(0.627450980392157*0xFF), (uint8_t)(0.47843137254902*0xFF),0xFF});
        strcpy(pr->bottom.label,"GPU");
        pr->bottom.value1.color=to_resp_color((uint8_t[]){0xFF,0xFF,0xFF,0xFF});
        strcpy(pr->bottom.value1.suffix, "%");
        pr->bottom.value2.color=to_resp_color((uint8_t[]){0xFF,0x00,0xFF,0xFF});
        strcpy(pr->bottom.value2.suffix, "\xC2\xB0");
    }
    if(waiting==CMD_DATA) {
        response_data_t* pr = (response_data_t*)out_resp;
        pr->top.value1.value = 50;
        pr->top.value2.value = 30;
        pr->bottom.value1.value = 15;
        pr->bottom.value2.value = 35;
        int variance = (esp_random()%30)-15;
        pr->top.value1.value += variance;
        pr->top.value1.scaled = (pr->top.value1.value/100.0f);
        variance = (esp_random()%30)-15;
        pr->top.value2.value += variance;
        pr->top.value2.scaled = (pr->top.value2.value/100.0f);
        variance = (esp_random()%30)-15;
        pr->bottom.value1.value += variance;
        pr->bottom.value1.scaled = (pr->bottom.value1.value/100.0f);
        variance = (esp_random()%30)-15;
        pr->bottom.value2.value += variance;
        pr->bottom.value2.scaled = (pr->bottom.value2.value/85.0f);
    }
    int8_t ret = waiting;
    waiting = -1;
    return ret;
#endif
}
static void build_array(const request_ident_t& req,uint8_t* data) {
    memcpy(&data[0],&req.version_major,2);
    memcpy(&data[2],&req.version_minor,2);
    memcpy(&data[4],&req.build,8);
    memcpy(&data[12],&req.id,2);
    memcpy(&data[14],req.mac_address,6);
    memcpy(&data[20],req.display_name,64);
    memcpy(&data[84],req.slug,64);
    memcpy(&data[148],&req.horizontal_resolution,2);
    memcpy(&data[150],&req.vertical_resolution,2);
    data[152]=req.is_monochrome;
    memcpy(&data[153],&req.dpi,4);
    memcpy(&data[157],&req.pixel_size,4);
    data[161]=(uint8_t)req.input_type;
} 
void serial_write_identity() {
    char tmp[128];
    sprintf(tmp,"### Espmon firmware build: %lld\n",(long long)BUILD_TIMESTAMP_UTC);
    size_t len = strlen(tmp);
    if(0>uart_write_bytes(UART_NUM_0,tmp,len)) {
        int i=1000;
        while(i-->0) {
            vTaskDelay(5);
            if(-1<uart_write_bytes(UART_NUM_0,tmp,len)) {
                break;
            }
        }
        if(i==0) { return; }
    }
    uart_wait_tx_done(UART_NUM_0,portMAX_DELAY);
}
void serial_write(const request_ident_t& req) {
    uint8_t tmp[165];
    build_array(req,tmp+3);
    tmp[0]=3;
    tmp[1]=3;
    tmp[2]=3;
    if(0>uart_write_bytes(UART_NUM_0,tmp,165)) {
        int i=1000;
        while(i-->0) {
            vTaskDelay(5);
            if(-1<uart_write_bytes(UART_NUM_0,tmp,165)) {
                break;
            }
        }
        if(i==0) { return; }
    }
    
    uart_wait_tx_done(UART_NUM_0,portMAX_DELAY);
}
void serial_write(int8_t cmd, uint8_t screen_index) {
#ifndef TEST_NO_SERIAL
    uint8_t ba[] = {(uint8_t)cmd,(uint8_t)cmd,(uint8_t)cmd,screen_index};
    if(0>uart_write_bytes(UART_NUM_0,ba,sizeof(ba))) {
        int i=1000;
        while(i-->0) {
            vTaskDelay(5);
            if(-1<uart_write_bytes(UART_NUM_0,ba,sizeof(ba))) {
                break;
            }
        }
        if(i==0) { return; }
    }
    
    uart_wait_tx_done(UART_NUM_0,portMAX_DELAY);
#else
    waiting = cmd;
    index_requested = screen_index;
#endif
}
bool serial_init() {
#ifndef TEST_NO_SERIAL
    esp_log_level_set(TAG, ESP_LOG_INFO);
    /* Configure parameters of an UART driver,
     * communication pins and install the driver */
    uart_config_t uart_config;
    memset(&uart_config,0,sizeof(uart_config));
    uart_config.baud_rate = 115200;
    uart_config.data_bits = UART_DATA_8_BITS;
    uart_config.parity = UART_PARITY_DISABLE;
    uart_config.stop_bits = UART_STOP_BITS_1;
    uart_config.flow_ctrl = UART_HW_FLOWCTRL_DISABLE;
    //Install UART driver, and get the queue.
    if(ESP_OK!=uart_driver_install(UART_NUM_0, SERIAL_BUF_SIZE * 2, 0, 20, nullptr, 0)) {
        ESP_LOGE(TAG,"Unable to install uart driver");
        goto error;
    }
    uart_param_config(UART_NUM_0, &uart_config);
    //Set UART pins (using UART0 default pins ie no changes.)
    uart_set_pin(UART_NUM_0, UART_PIN_NO_CHANGE, UART_PIN_NO_CHANGE, UART_PIN_NO_CHANGE, UART_PIN_NO_CHANGE);
    //Create a task to handler UART event from ISR
#else
    waiting = 0;
#endif
    return true;
#ifndef TEST_NO_SERIAL
error:
    return false;
#endif
}