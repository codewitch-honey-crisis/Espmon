#include <stdio.h>
#include <stdbool.h>
#include <math.h>
#include <time.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "esp_system.h"
#include "esp_mac.h"
#include "gfx.hpp"
#include "uix.hpp"
#include "panel.h"
#include "serial.hpp"
#define MONOXBOLD_IMPLEMENTATION
#include <monoxbold.hpp>
#undef MONOXBOLD_IMPLEMENTATION
#include <espmon.hpp>

#include <firmware_info.h>

using namespace gfx;
using namespace uix;

#if LCD_COLOR_SPACE== LCD_COLOR_GSC
#define PIXEL gsc_pixel<LCD_BIT_DEPTH>
#else
#define PIXEL rgb_pixel<LCD_BIT_DEPTH>
#endif
#define LCD_BUFFER1 ((uint8_t*)panel_lcd_transfer_buffer())
#if LCD_SYNC_TRANSFER == 0
#define LCD_BUFFER2 ((uint8_t*)panel_lcd_transfer_buffer2())
#else
#define LCD_BUFFER2 nullptr
#endif
#define INCHES_TO_MM(x) ((x)*25.4f)

#if defined(TOUCH_BUS) || defined(BUTTON)
#define HAS_INPUT
static TickType_t pressed = 0;
#endif

using uix_color_t = color<uix_pixel>;
static espmon<bitmap<PIXEL>,LCD_X_ALIGN,LCD_Y_ALIGN> app;
static TickType_t disconnect_ts = xTaskGetTickCount();
static int8_t screen_index = -1;
#ifdef HAS_INPUT
static void update_input() {
#ifdef TOUCH_BUS
    panel_touch_update();
    uint16_t x,y,s;
    size_t count = 1;
    panel_touch_read_raw(&count,&x,&y,&s);
    if(count>0) {
        if(pressed==0) {
            pressed = xTaskGetTickCount();
        }
    } else {
        if(pressed>0) {
            if(app.connected()) {
                ++screen_index;
                serial_write(0,screen_index);
            }
        }
        pressed = 0;
    }
#endif
#ifdef BUTTON
    if(panel_button_read_all()) {
        if(pressed==0) {
            pressed = xTaskGetTickCount();
        }
    } else {
        if(pressed>0) {
            if(app.connected()) {
                ++screen_index;
                serial_write(0,screen_index);
            }
        }
        pressed = 0;
    }
#endif
}
#endif

extern "C" void panel_lcd_flush_complete() {
    app.transfer_complete();
}
static void espmon_flush(const rect16& bounds, const void* bmp, void* state) {
    panel_lcd_flush(bounds.x1,bounds.y1,bounds.x2,bounds.y2,(void*)bmp);;
#if LCD_SYNC_TRANSFER == 1
    app.transfer_complete();
#endif
}
static void get_pixel_metrics(uint16_t res_x, uint16_t res_y, float diagonal_mm, 
                             float *out_pixel_size, float *out_ppi) {
    float pixel_size_mm;
    
    pixel_size_mm = diagonal_mm / sqrtf((float)(res_x * res_x + res_y * res_y));
    
    if (out_pixel_size != NULL) {
        *out_pixel_size = pixel_size_mm;
    }

    if (out_ppi != NULL) {
        *out_ppi = 25.4f/pixel_size_mm;
    }
}

extern "C" void app_main() {
    uint8_t mac_address[6] = {0};
    float dpi = 0.0f;
    float pixel_size = 0.0f;
#ifdef POWER
    panel_power_init();
#endif
#ifdef BUTTON
    panel_button_init();
#endif
#ifdef EXPANDER_BUS
    panel_expander_init();
#endif
    panel_lcd_init();
#ifdef TOUCH_BUS
    panel_touch_init();
#endif

    esp_base_mac_addr_get(mac_address);
    get_pixel_metrics(LCD_WIDTH,LCD_HEIGHT,INCHES_TO_MM(LCD_INCHES),&pixel_size,&dpi);
    serial_init();
    app.dimensions({LCD_WIDTH,LCD_HEIGHT});
    app.set_flush_callback(espmon_flush);
    app.set_transfer(LCD_FULLSCREEN_TRANSFER?screen_update_mode::direct:screen_update_mode::partial,LCD_BUFFER1,LCD_TRANSFER_SIZE,LCD_BUFFER2);
    app.has_graph(LCD_HEIGHT>64);
    app.is_monochrome(LCD_BIT_DEPTH==1);
    app.initialize();
    serial_write_identity();
    TickType_t send_ts = 0;
    response_t resp;
    bool connected = false;
    bool updated = false;
    while(1) {
        updated = false;
        if(disconnect_ts>0 && xTaskGetTickCount()>=disconnect_ts+pdMS_TO_TICKS(5000)) {
            app.disconnect();
            connected = false;
            disconnect_ts = 0;
            updated = true;
        }
        int8_t cmd = serial_read_packet(&resp);
        while(cmd!=-1) {
            updated = true;
            connected = true;
            if(disconnect_ts==0) {
                screen_index=-1;
            }
            disconnect_ts = xTaskGetTickCount();
            if(cmd==0) {
                screen_index = resp.screen.header.index;
            }
            if(cmd==0||cmd==1||cmd==5) {
                app.accept_packet((command_t)cmd,resp,false);
            }
            if(cmd==3) {
                serial_write(FIRMWARE_INFO());
            }
            cmd = serial_read_packet(&resp);
        }
        if(updated) {
            app.refresh();
        }
        if(xTaskGetTickCount()>send_ts+pdMS_TO_TICKS(100)) {
            send_ts = xTaskGetTickCount();
            if(screen_index==-1) {
                serial_write(CMD_SCREEN,0);
            } else {
                serial_write(connected?CMD_DATA:CMD_NOP,screen_index);
            }
        }
#ifdef HAS_INPUT
        update_input();
#endif
        vTaskDelay(5);
    }
}