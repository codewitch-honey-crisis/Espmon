#include <stdio.h>
#include <memory.h>
#include "panel.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

#define CLEAR_DELAY 1000

#if defined(LCD_BUS) && LCD_BIT_DEPTH == 16 && LCD_COLOR_SPACE != LCD_COLOR_GSC
#define ICONS_RGB_IMPLEMENTATION
#include "icons_rgb.h"
#define RGB_OR_BGR_16
#define PX_TYPE uint16_t
#define COLOR_RGB(r,g,b) ((((uint16_t)r)<<11)|(((uint16_t)g)<<5)|(((uint16_t)b)<<0))
#define COLOR_BLACK COLOR_RGB(0,0,0)
#define COLOR_WHITE COLOR_RGB(31,63,31)
#define COLOR_RED COLOR_RGB(31,0,0)
#define COLOR_GREEN COLOR_RGB(0,63,0)
#define COLOR_BLUE COLOR_RGB(0,0,31)
#define COLOR_ORANGE COLOR_RGB(31,31,0)
#define COLOR_YELLOW COLOR_RGB(31,63,0)
#define COLOR_CYAN COLOR_RGB(0,63,31)
#elif defined(LCD_BUS) && LCD_COLOR_SPACE == LCD_COLOR_GSC
#define ICONS_GSC_IMPLEMENTATION
#include "icons_gsc.h"
#define GSC
#define PX_TYPE uint8_t
#define COLOR_GSC(x) ((uint8_t)((uint8_t)x)>>(8-LCD_BIT_DEPTH))
#define COLOR_BLACK COLOR_GSC(0)
#define COLOR_GRAY COLOR_GSC(127)
#define COLOR_WHITE COLOR_GSC(255)
#endif

#ifdef LCD_BUS
volatile int flushing = 0;
void panel_lcd_flush_complete(void) {
    flushing = 0;
}
#endif
#ifdef COLOR_BLACK
typedef struct {
    PX_TYPE color;
    const uint8_t* icon;
} col_entry_t;
static const col_entry_t colors[] = {
#ifdef RGB_OR_BGR_16
    {COLOR_RED,icon_red},{COLOR_GREEN,icon_green},{COLOR_BLUE,icon_blue},{COLOR_ORANGE,icon_orange}, {COLOR_CYAN,icon_cyan}
#endif
#ifdef GSC
    {COLOR_BLACK,icon_black},{COLOR_GRAY,icon_gray},{COLOR_WHITE,icon_white}
#endif
};
static const size_t colors_size = sizeof(colors)/sizeof(colors[0]);

static void draw_icon(size_t index) {
    memset(panel_lcd_transfer_buffer(),0,LCD_TRANSFER_SIZE);
    const uint8_t *p = colors[index].icon;
    PX_TYPE* t = (PX_TYPE*)panel_lcd_transfer_buffer();
#if defined(GSC) && LCD_BIT_DEPTH == 1
    uint8_t accum = 0;
    int bits = 0;
    bool chroma;
    static const size_t icon_bytes = 128*32/2;
    for(int i = 0;i<icon_bytes;++i) {
        const uint8_t data = *p++;
        const uint8_t low = data & 0x0F;
        const uint8_t high = (data & 0xF0)>>4;
        accum<<=1;
        chroma = (high>7);
        accum|= chroma;
        ++bits;
        if(bits==8) {
            bits=0;
            *t++=accum;
            accum = 0;
        }
        accum<<=1;
        chroma = (low>7);
        accum|= chroma;
        ++bits;
        if(bits==8) {
            bits=0;
            *t++=accum;
            accum = 0;
        }
    }
#endif
#if LCD_BIT_DEPTH >= 4
    for(int y = 0;y<32;++y) {
        for(int x = 0; x<128;x+=2) {
            const uint8_t data = *p++;
#ifdef RGB_OR_BGR_16
            const uint8_t low = data & 0x0F;
            const uint8_t high = (data & 0xF0)>>4;
            uint16_t col1rb = (high*31)/15;
            uint16_t col1g = (high*63)/15;
            uint16_t col2rb = (low*31)/15;
            uint16_t col2g = (low*63)/15;
#ifndef LCD_DATA_ENDIAN_LITTLE
            uint16_t col1raw = (col1rb<<11)|(col1g<<5)|col1rb;
            uint16_t col2raw = (col2rb<<11)|(col2g<<5)|col2rb;
            uint16_t col1 = (col1raw>>8)|((col1raw&0xFF)<<8);
            uint16_t col2 = (col2raw>>8)|((col2raw&0xFF)<<8);
#else
            uint16_t col1 = (col1rb<<11)|(col1g<<6)|col1rb;
            uint16_t col2 = (col2rb<<11)|(col2g<<6)|col2rb;
#endif
            *t++=col1;
            *t++=col2;
#endif
#ifdef GSC
#if LCD_BIT_DEPTH == 4
            const uint8_t low = data & 0x0F;
            const uint8_t high = (data & 0xF0)>>4;
            *t++=(low | (high<<4));
#endif
#if LCD_BIT_DEPTH == 8
            const uint8_t low = data & 0x0F;
            const uint8_t high = (data & 0xF0)>>4;
            *t++=high*16;
            *t++=low*16;
#endif
#endif

        }
    }
#endif
}

static void poll_input() {
#ifdef TOUCH_BUS
        panel_touch_update();
        size_t count = 5;
        uint16_t x[5],y[5],s[5];
        panel_touch_read(&count,x,y,s);
        if(count) {
            printf("touch: (%d, %d)\n",x[0],y[0]);
        }
#endif
#ifdef BUTTON
        uint64_t button_mask = panel_button_read_all();
        if(button_mask>0) {
            printf("Pressed mask: 0x%llx (%lld)\n",button_mask,button_mask);
        }
#endif    
}
#endif
void app_main(void)
{
#ifdef POWER
    panel_power_init();
#endif
#ifdef LCD_BUS
    panel_lcd_init();
#endif
#ifdef TOUCH_BUS
    panel_touch_init();
#endif
#ifdef BUTTON
    panel_button_init();
#endif
#ifdef SD_BUS
    panel_sd_init(false,0,0);
#endif
#ifdef COLOR_BLACK // we have a suported color model
    TickType_t screen_ts = 0;
    TickType_t wdt_ts = xTaskGetTickCount();
    int iter = 0;
    
#endif
    static const uint16_t xoffs = (LCD_WIDTH-128)/2;
    static const uint16_t yoffs = (LCD_HEIGHT-32)/2;
    while(1) {
        TickType_t ts = xTaskGetTickCount();
        // feed the watchdog timer to prevent a reboot
        if(ts>wdt_ts+pdMS_TO_TICKS(200))  {
            wdt_ts = ts;
            vTaskDelay(5);
        }
#ifdef COLOR_BLACK
        if(!panel_lcd_vsync_flush_count()) {
            if(ts>=screen_ts+pdMS_TO_TICKS(CLEAR_DELAY)) {
                screen_ts = ts;
                // draw the screen
                const size_t index= (iter++)%colors_size;
                PX_TYPE color = colors[index].color;
                while(flushing) portYIELD(); 
                PX_TYPE* buf = (PX_TYPE*)panel_lcd_transfer_buffer();
#ifdef RGB_OR_BGR_16
#ifdef LITTLE_ENDIAN   
                const PX_TYPE px = color;
#else
                const PX_TYPE px = (color>>8)|((color&0xFF)<<8);
#endif
                color = px;
                for(int i = 0;i<LCD_TRANSFER_SIZE/sizeof(uint16_t);++i) {
                    *buf++=color;
                }
#endif
#if defined(GSC)
                uint8_t c = 0;
                int bits = 0;
                while(bits<8) {
                    c<<=LCD_BIT_DEPTH;
                    c |= color;
                    bits += LCD_BIT_DEPTH;
                }
                for(int i = 0;i<LCD_TRANSFER_SIZE;++i) {
                    *buf++=c;
                }
#endif
                int y = 0;
                while(y<LCD_HEIGHT) {
                    int yend = y+(LCD_HEIGHT/LCD_DIVISOR)-1;
                    if(yend>=LCD_HEIGHT) {
                        yend = LCD_HEIGHT-1;
                    }
                    while(flushing) portYIELD(); 
                    flushing = 1;
                    panel_lcd_flush(0,y,LCD_WIDTH-1,yend,panel_lcd_transfer_buffer());
                    y= yend+1;
                    while(panel_lcd_vsync_flush_count()) { poll_input(); portYIELD(); }
                }
                while(flushing) portYIELD(); 
                flushing = 1;
                draw_icon(index);
                panel_lcd_flush(xoffs,yoffs,xoffs+127,yoffs+31,panel_lcd_transfer_buffer());
            }
        }
#endif
        poll_input();
    }
}