#include <stdio.h>
#include <stdbool.h>
#include <math.h>
#include <time.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "esp_system.h"
#include "esp_mac.h"
#include "esp_log.h"
#include "gfx.hpp"
#include "uix.hpp"
#include "panel.h"
#include "serial.h"
#include "frame.h"
#include "interface_buffers.h"
#define MONOXBOLD_IMPLEMENTATION
#include <monoxbold.hpp>
#undef MONOXBOLD_IMPLEMENTATION
#include <espmon.hpp>

#include <firmware_info.h>

using namespace gfx;
using namespace uix;
static const char* TAG = "app";
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
static uint8_t mac_address[6] = {0};
static float dpi = 0.0f;
static float pixel_size = 0.0f;
static frame_handle_t frame_handle = nullptr;
#ifdef HAS_SERIAL2
static frame_handle_t frame_handle2 = nullptr;
#endif
static int8_t screen_index = -1;
int serial_read(void* state) {
    return serial_getc();
}
int serial_write(uint8_t value, void* state) {
    serial_putc(value);
    return 1;
}
#ifdef HAS_SERIAL2
int serial2_read(void* state) {
    return serial2_getc();
}
int serial2_write(uint8_t value, void* state) {
    serial2_putc(value);
    return 1;
}
#endif

static uint8_t write_buffer[INTERFACE_MAX_SIZE];
typedef struct {
    uint8_t* ptr;
    size_t remaining;
} buffer_cursor_t;
static int on_read_buffer(void* state) {
    buffer_cursor_t* cur = (buffer_cursor_t*)state;
    if(cur->remaining==0) {
        return BUFFERS_EOF;
    }
    uint8_t result = *cur->ptr++;
    --cur->remaining;
    return result;
}
int on_write_buffer(uint8_t value, void* state) {
    buffer_cursor_t* cur = (buffer_cursor_t*)state;
    if(cur->remaining==0) {
        return BUFFERS_ERROR_EOF;
    }
    *cur->ptr++=value;
    --cur->remaining;
    return 1;
}
static void write_screen_req(frame_handle_t frame_handle,int index) {
    request_screen_t req;
    req.screen_index = index;
    buffer_cursor_t cur = {write_buffer,sizeof(write_buffer)};
    int res = request_screen_write(&req,on_write_buffer,&cur);
    if(-1<res) {
        frame_put(frame_handle,CMD_SCREEN,write_buffer,res);
    }
}
static void write_data_req(frame_handle_t frame_handle,int index) {
    request_data_t req;
    req.screen_index = index;
    buffer_cursor_t cur = {write_buffer,sizeof(write_buffer)};
    int res  = request_data_write(&req,on_write_buffer,&cur);
    if(-1<res) {
        frame_put(frame_handle,CMD_DATA,write_buffer,res);
    }
}
static void write_nop(frame_handle_t frame_handle) {
    request_nop_t req;
    buffer_cursor_t cur = {write_buffer,sizeof(write_buffer)};
    int res = request_nop_write(&req,on_write_buffer,&cur);
    if(-1<res) {
        frame_put(frame_handle, CMD_NOP,write_buffer,res);
    }
}

#ifdef HAS_INPUT

static void update_input(frame_handle_t frame_handle) {
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
                write_screen_req(frame_handle,screen_index);
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
                write_screen_req(frame_handle, screen_index);
            }
        }
        pressed = 0;
    }
#endif
}
#endif
static void process_frame(frame_handle_t frame_handle, uint8_t cmd, void* p, size_t len) {
    buffer_cursor_t cur = {(uint8_t*)p,len};
    response_t resp;
    int res;
    switch(cmd) {
        case CMD_NOP:
            puts("GOT NOP");
            break;
        case CMD_SCREEN:
            puts("GOT CMD SCREEN");
            if(-1<response_screen_read(&resp.screen,on_read_buffer,&cur)) {
                screen_index = resp.screen.header.index;
                app.accept_packet((command_t)cmd,resp,false);
            } else {
                puts("CMD_SCREEN READ ERROR");
            }
            break;
        case CMD_DATA:
            if(-1<response_data_read(&resp.data,on_read_buffer,&cur)) {
                app.accept_packet((command_t)cmd,resp,false);
            } else {
                puts("CMD_DATA READ ERROR");
            }
            break;
        case CMD_CLEAR:
            if(-1<response_clear_read(&resp.clear,on_read_buffer,&cur)) {
                app.accept_packet((command_t)cmd,resp,false);
            } else {
                puts("CMD_CLEAR READ ERROR");
            }
            break;
        case CMD_IDENT:
            if(-1<response_ident_read(&resp.ident,on_read_buffer,&cur)) {
                request_ident_t ident = FIRMWARE_INFO();
                buffer_cursor_t write_cur = {(uint8_t*)write_buffer,sizeof(write_buffer)};
                res = request_ident_write(&ident,on_write_buffer,&write_cur);
                if(-1<res) {
                    frame_put(frame_handle,CMD_IDENT,write_buffer,res);
                }
            } else {
                puts("CMD_IDENT READ ERROR");
            }
            break;
        case CMD_REFRESH_SCREEN:
            if(-1<response_refresh_screen_read(&resp.refresh_screen,on_read_buffer,&cur)) {
                screen_index = -1;
            } else {
                puts("CMD_REFRESH_SCREEN READ ERROR");
            }
            break;
        default:
            puts("GOT UNKNOWN CMD");
            break;
        
    }
}
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
    if(!serial_init(INTERFACE_MAX_SIZE)) {
        ESP_LOGE(TAG,"Serial could not be initialized");
        while(1) vTaskDelay(5);
    }
    frame_handle = frame_create(INTERFACE_MAX_SIZE,serial_read,nullptr,serial_write,nullptr);
    if(frame_handle==nullptr) {
        ESP_LOGE(TAG,"Frame handler could not be initialized");
        while(1) vTaskDelay(5);
    }
#ifdef HAS_SERIAL2
    if(!serial2_init(INTERFACE_MAX_SIZE)) {
        ESP_LOGE(TAG,"Serial2 could not be initialized");
        while(1) vTaskDelay(5);
    }
    frame_handle2 = frame_create(INTERFACE_MAX_SIZE,serial2_read,nullptr,serial2_write,nullptr);
    if(frame_handle2==nullptr) {
        ESP_LOGE(TAG,"Frame handler 2 could not be initialized");
        while(1) vTaskDelay(5);
    }
#endif
    app.dimensions({LCD_WIDTH,LCD_HEIGHT});
    app.set_flush_callback(espmon_flush);
    app.set_transfer(LCD_FULLSCREEN_TRANSFER?screen_update_mode::direct:screen_update_mode::partial,LCD_BUFFER1,LCD_TRANSFER_SIZE,LCD_BUFFER2);
    app.has_graph(LCD_HEIGHT>64);
    app.is_monochrome(LCD_BIT_DEPTH==1);
    app.initialize();
    TickType_t send_ts = 0;
    bool connected = false;
    
    void* p;
    size_t len;
    uint8_t cmd;
    frame_handle_t fh=frame_handle;
    while(1) {
        if(disconnect_ts>0 && xTaskGetTickCount()>=disconnect_ts+pdMS_TO_TICKS(5000)) {
            app.disconnect();
            connected = false;
            disconnect_ts = 0;
            screen_index=-1;
        }
        int res = frame_get(frame_handle,&p,&len);
        if(res>0) {
            cmd = res;
            connected = true;
            disconnect_ts = xTaskGetTickCount();
            process_frame(frame_handle,cmd,p,len);
        }
#ifdef HAS_SERIAL2
        res = frame_get(frame_handle2,&p,&len);
        if(res>0) {
            fh=frame_handle2;
            cmd = res;
            connected = true;
            disconnect_ts = xTaskGetTickCount();
            process_frame(frame_handle2,cmd,p,len);
        }
#endif
        app.refresh(false);
        
        if(xTaskGetTickCount()>send_ts+pdMS_TO_TICKS(100)) {
            send_ts = xTaskGetTickCount();
            if(screen_index==-1) {
                write_screen_req(fh,0);
            } else {
                if(res!=CMD_REFRESH_SCREEN) {
                    if(connected) {
                        write_data_req(fh,screen_index);
                    } else {
                        write_nop(fh);
                    }
                } else {
                    write_screen_req(fh,screen_index);
                }
            }
            vTaskDelay(5);
        }
#ifdef HAS_INPUT
        update_input(fh);
#endif
        
    }
}