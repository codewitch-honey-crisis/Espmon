// If the CMD_SCREEN response for an input-driven advance never arrives, don't
// lock out navigation forever — clear the input gate after this.
#define SCREEN_CHANGE_TIMEOUT_MS 1000
#define INPUT_STABLE_MS 30   // contact must hold this long to be trusted
#include <stdio.h>
#include <stdbool.h>
#include <string.h>
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
#include "frame_arq.h"
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

// How long to wait for an ACK before retransmitting an outstanding frame.
// Per the 1-with-3 model this never gives up on its own; a truly dead link is
// cleared by the disconnect timeout below, which resets the ARQ state.
#define ACK_TIMEOUT_MS 300

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
static bool screen_change_pending = false;
static TickType_t screen_change_ts = 0;
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

// One serial port: the ARQ state + its two caller-supplied buffers (read + retain,
// required by the zero-alloc _za create), plus a single staged outbound frame.
// Stop-and-wait allows only one frame in flight, so an outbound request that
// can't go out yet (a prior frame is unacked) is held in stage_buf and flushed
// once the link is free. The device only sends small request frames, so stage_buf
// can be shrunk below INTERFACE_MAX_SIZE if RAM is tight and you know your largest
// request size.
typedef struct {
    frame_arq_t        state;
    uint8_t            read_buf[INTERFACE_MAX_SIZE + FRAME_ARQ_HEADER_LENGTH];
    uint8_t            retain_buf[INTERFACE_MAX_SIZE + FRAME_ARQ_HEADER_LENGTH];
    frame_arq_handle_t handle;
    uint8_t            stage_buf[INTERFACE_MAX_SIZE];
    size_t             stage_len;
    uint8_t            stage_cmd;
    bool               pending;
    bool               priority;    // important send (screen request / ident) that must not drop
    TickType_t         last_send;   // when the outstanding frame was last (re)transmitted
} port_t;

static port_t port1;
#ifdef HAS_SERIAL2
static port_t port2;
#endif
// The first handle to receive a frame becomes the sender for good (until reboot).
// Before that, port1 is the default target for connection-soliciting sends.
static port_t* active = &port1;
static bool active_pinned = false;

// Stage an outbound frame. A routine send won't clobber a pending important one;
// a newer important send replaces an older one (rare, and periodic sends self-heal).
static void stage(port_t* pt, uint8_t cmd, const uint8_t* data, size_t len, bool priority) {
    if(pt->pending && pt->priority && !priority) {
        return;
    }
    if(len > sizeof(pt->stage_buf)) {
        return;
    }
    memcpy(pt->stage_buf, data, len);
    pt->stage_cmd = cmd;
    pt->stage_len = len;
    pt->priority = priority;
    pt->pending = true;
}
// Flush a staged frame once the link is free (no unacked frame outstanding).
static void pump_send(port_t* pt) {
    if(pt->pending && !frame_arq_awaiting_ack(pt->handle)) {
        int r = frame_arq_put(pt->handle, pt->stage_cmd, pt->stage_buf, pt->stage_len);
        if(r == FRAME_ARQ_SUCCESS) {
            pt->pending = false;
            pt->last_send = xTaskGetTickCount();
        } else if(r == FRAME_ARQ_ERROR_BUSY) {
            // still awaiting; leave staged and retry next loop
        } else {
            pt->pending = false; // unexpected (e.g. oversized); drop rather than spin
        }
    }
}
// Caller-owned retransmit timer. Never gives up / never advances the sequence.
static void service_retransmit(port_t* pt) {
    if(frame_arq_awaiting_ack(pt->handle) &&
       (uint32_t)(xTaskGetTickCount() - pt->last_send) >= pdMS_TO_TICKS(ACK_TIMEOUT_MS)) {
        frame_arq_resend(pt->handle);
        pt->last_send = xTaskGetTickCount();
    }
}

static void write_screen_req(port_t* pt,int index) {
    request_screen_t req;
    req.screen_index = index;
    buffer_cursor_t cur = {write_buffer,sizeof(write_buffer)};
    int res = request_screen_write(&req,on_write_buffer,&cur);
    if(-1<res) {
        stage(pt,CMD_SCREEN,write_buffer,res,true);
    }
}
static void write_data_req(port_t* pt,int index) {
    request_data_t req;
    req.screen_index = index;
    buffer_cursor_t cur = {write_buffer,sizeof(write_buffer)};
    int res  = request_data_write(&req,on_write_buffer,&cur);
    if(-1<res) {
        stage(pt,CMD_DATA,write_buffer,res,false);
    }
}
static void write_nop(port_t* pt) {
    request_nop_t req;
    buffer_cursor_t cur = {write_buffer,sizeof(write_buffer)};
    int res = request_nop_write(&req,on_write_buffer,&cur);
    if(-1<res) {
        stage(pt,CMD_NOP,write_buffer,res,false);
    }
}

#ifdef HAS_INPUT
// Feed the raw "is a contact present right now" signal each poll. Returns true
// exactly once, on a *confirmed* release: a press that stayed stable-down for
// INPUT_STABLE_MS followed by a release stable-up for INPUT_STABLE_MS. Glitches
// shorter than that — an FT6x36 spurious blip, or a count==0 dropout mid-hold —
// never commit, so they produce no edge and no advance.
static bool input_released(bool raw_down) {
    static bool       stable_down = false;   // debounced/confirmed state
    static bool       raw_last    = false;   // last raw sample
    static TickType_t raw_change_ts = 0;     // when the raw sample last changed
    TickType_t now = xTaskGetTickCount();
    if(raw_down != raw_last) {
        raw_last = raw_down;
        raw_change_ts = now;
    }
    bool released = false;
    if((uint32_t)(now - raw_change_ts) >= pdMS_TO_TICKS(INPUT_STABLE_MS)) {
        if(raw_down != stable_down) {        // raw held long enough; commit it
            if(!raw_down) {
                released = true;             // confirmed down -> up
            }
            stable_down = raw_down;
        }
    }
    return released;
}

static void update_input(port_t* pt) {
    bool raw_down = false;
#ifdef TOUCH_BUS
    panel_touch_update();
    uint16_t x,y,s;
    size_t count = 1;
    panel_touch_read_raw(&count,&x,&y,&s);
    if(count>0) raw_down = true;
#endif
#ifdef BUTTON
    if(panel_button_read_all()) raw_down = true;
#endif
    if(input_released(raw_down)) {
        if(app.connected() && !screen_change_pending) {
            ++screen_index;
            screen_change_pending = true;
            screen_change_ts = xTaskGetTickCount();
            write_screen_req(pt,screen_index);
        }
    }
}
#endif
static void process_frame(port_t* pt, uint8_t cmd, void* p, size_t len) {
    buffer_cursor_t cur = {(uint8_t*)p,len};
    response_t resp;
    int res;
    switch(cmd) {
        case CMD_NOP:
            break;
        case CMD_SCREEN:
            if(-1<response_screen_read(&resp.screen,on_read_buffer,&cur)) {
                screen_index = resp.screen.header.index;
                screen_change_pending = false;
                app.accept_packet((command_t)cmd,resp,false);
            } else {
                ESP_LOGE(TAG, "CMD_SCREEN READ ERROR");
            }
            break;
        case CMD_DATA:
            if(-1<response_data_read(&resp.data,on_read_buffer,&cur)) {
                app.accept_packet((command_t)cmd,resp,false);
            } else {
                ESP_LOGE(TAG, "CMD_DATA READ ERROR");
            }
            break;
        case CMD_CLEAR:
            if(-1<response_clear_read(&resp.clear,on_read_buffer,&cur)) {
                app.accept_packet((command_t)cmd,resp,false);
            } else {
                ESP_LOGE(TAG, "CMD_CLEAR READ ERROR");
            }
            break;
        case CMD_IDENT:
            if(-1<response_ident_read(&resp.ident,on_read_buffer,&cur)) {
                request_ident_t ident = FIRMWARE_INFO();
                buffer_cursor_t write_cur = {(uint8_t*)write_buffer,sizeof(write_buffer)};
                res = request_ident_write(&ident,on_write_buffer,&write_cur);
                if(-1<res) {
                    // respond on the port the request arrived on; important, so priority
                    stage(pt,CMD_IDENT,write_buffer,res,true);
                }
            } else {
                ESP_LOGE(TAG, "CMD_IDENT READ ERROR");
            }
            break;
        case CMD_REFRESH_SCREEN:
            if(-1<response_refresh_screen_read(&resp.refresh_screen,on_read_buffer,&cur)) {
                screen_index = -1;
                screen_change_pending = false;
            } else {
                ESP_LOGE(TAG, "CMD_REFRESH_SCREEN READ ERROR");
            }
            break;
        default:
            ESP_LOGE(TAG, "UNKNOWN CMD");
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
    esp_read_mac(mac_address, ESP_MAC_EFUSE_FACTORY);
    get_pixel_metrics(LCD_WIDTH,LCD_HEIGHT,INCHES_TO_MM(LCD_INCHES),&pixel_size,&dpi);
    if(!serial_init(INTERFACE_MAX_SIZE)) {
        ESP_LOGE(TAG,"Serial could not be initialized");
        while(1) vTaskDelay(5);
    }
    port1.handle = frame_arq_create_za(INTERFACE_MAX_SIZE,&port1.state,port1.read_buf,port1.retain_buf,serial_read,nullptr,serial_write,nullptr);
    if(port1.handle==nullptr) {
        ESP_LOGE(TAG,"Frame handler could not be initialized");
        while(1) vTaskDelay(5);
    }
    port1.pending = false;
    port1.priority = false;
    port1.last_send = 0;
#ifdef HAS_SERIAL2
    if(!serial2_init(INTERFACE_MAX_SIZE)) {
        ESP_LOGE(TAG,"Serial2 could not be initialized");
        while(1) vTaskDelay(5);
    }
    port2.handle = frame_arq_create_za(INTERFACE_MAX_SIZE,&port2.state,port2.read_buf,port2.retain_buf,serial2_read,nullptr,serial2_write,nullptr);
    if(port2.handle==nullptr) {
        ESP_LOGE(TAG,"Frame handler 2 could not be initialized");
        while(1) vTaskDelay(5);
    }
    port2.pending = false;
    port2.priority = false;
    port2.last_send = 0;
#endif
    active = &port1;
    active_pinned = false;
    app.dimensions({LCD_WIDTH,LCD_HEIGHT});
    app.set_flush_callback(espmon_flush);
    app.set_transfer(LCD_FULLSCREEN_TRANSFER?screen_update_mode::direct:screen_update_mode::partial,LCD_BUFFER1,LCD_TRANSFER_SIZE,LCD_BUFFER2);
    app.has_graph(LCD_HEIGHT>64);
    app.is_monochrome(LCD_BIT_DEPTH==1);
    app.initialize();
    TickType_t send_ts = 0;
    bool connected = false;
    ESP_LOGE(TAG,"Booted");
    void* p;
    size_t len;
    while(1) {
        if(disconnect_ts>0 && xTaskGetTickCount()>=disconnect_ts+pdMS_TO_TICKS(5000)) {
            app.disconnect();
            connected = false;
            disconnect_ts = 0;
            screen_index=-1;
            screen_change_pending = false;
            // Link is dead. Reset ARQ state so a reconnect that does NOT reboot the
            // MCU (native USB CDC) still realigns sequence numbers. Only fires after
            // 5s of no frames, so it can't disturb a live link. The active handle
            // stays pinned to whichever port connected first (until reboot).
            frame_arq_reset(port1.handle);
            port1.pending = false;
#ifdef HAS_SERIAL2
            frame_arq_reset(port2.handle);
            port2.pending = false;
#endif
        }
        int res = frame_arq_get(port1.handle,&p,&len);
        if(res==FRAME_ARQ_RESEND_NEEDED) {
            frame_arq_resend(port1.handle);
            port1.last_send = xTaskGetTickCount();
        } else if(res>0) {
            if(!active_pinned) { active = &port1; active_pinned = true; }
            connected = true;
            disconnect_ts = xTaskGetTickCount();
            process_frame(&port1,(uint8_t)res,p,len);
        }
#ifdef HAS_SERIAL2
        res = frame_arq_get(port2.handle,&p,&len);
        if(res==FRAME_ARQ_RESEND_NEEDED) {
            frame_arq_resend(port2.handle);
            port2.last_send = xTaskGetTickCount();
        } else if(res>0) {
            if(!active_pinned) { active = &port2; active_pinned = true; }
            connected = true;
            disconnect_ts = xTaskGetTickCount();
            process_frame(&port2,(uint8_t)res,p,len);
        }
#endif
        app.refresh(true);
        
        if(xTaskGetTickCount()>send_ts+pdMS_TO_TICKS(100)) {
            send_ts = xTaskGetTickCount();
            if(screen_index==-1) {
                write_screen_req(active,0);
            } else {
                if(res!=CMD_REFRESH_SCREEN) {
                    if(connected) {
                        write_data_req(active,screen_index);
                    } else {
                        write_nop(active);
                    }
                } else {
                    write_screen_req(active,screen_index);
                }
            }
            vTaskDelay(5);
        }

        // Flush staged sends and service retransmits on both ports, so an ident
        // reply left pending on the non-active port still gets delivered.
        pump_send(&port1);
        service_retransmit(&port1);
#ifdef HAS_SERIAL2
        pump_send(&port2);
        service_retransmit(&port2);
#endif
        if(screen_change_pending &&
           (uint32_t)(xTaskGetTickCount() - screen_change_ts) >= pdMS_TO_TICKS(SCREEN_CHANGE_TIMEOUT_MS)) {
            screen_change_pending = false;
        }
#ifdef HAS_INPUT
        update_input(active);
#endif
        
    }
}