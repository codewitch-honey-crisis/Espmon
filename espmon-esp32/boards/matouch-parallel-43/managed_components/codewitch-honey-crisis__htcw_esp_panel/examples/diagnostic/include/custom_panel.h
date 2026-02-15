// you don't actually need a #ifdef guard on a custom panel for many projects, only for projects where you might support more than one device (either custom, or otherwise)
#ifdef C6DEVKITC1 // Works, but is a custom kit
#define LCD_I2C_HOST    I2C_1
#define LCD_I2C_ADDRESS 0x3C
#define LCD_I2C_PULLUP 1
#define LCD_CONTROL_PHASE_BYTES 1
#define LCD_DC_BIT_OFFSET 6
#define LCD_BIT_DEPTH 1
#define LCD_PIN_NUM_SCL 11
#define LCD_PIN_NUM_SDA 10
#define LCD_PIN_NUM_RST -1
#define LCD_INIT esp_lcd_new_panel_ssd1306
#define LCD_HRES 128
#define LCD_VRES 64
#define LCD_COLOR_SPACE LCD_COLOR_GSC
#define LCD_CLOCK_HZ (400 * 1000)
#define LCD_GAP_X 0
#define LCD_GAP_Y 0
#define LCD_MIRROR_X true
#define LCD_MIRROR_Y true
#define LCD_INVERT_COLOR false
#define LCD_SWAP_XY false
#define LCD_DIVISOR 1
#define LCD_Y_ALIGN 8
#define LCD_TRANSFER_PALETTE 1
#define LCD_VENDOR_CONFIG esp_lcd_panel_ssd1306_config_t vendor_config = {\
    .height = LCD_VRES,\
};
#define LCD_TRANSLATE static uint8_t ssd1306_buffer[LCD_TRANSFER_SIZE];\
    bitmap = ((uint8_t*)bitmap)+LCD_TRANSFER_PALETTE_SIZE; \
     int src_width = x2 - x1 + 1;\
     int dst_width = src_width;\
     int dst_height_pages = (y2 - y1 + 1) >> 3;  /* Height in pages (8-pixel groups) */\
     \
     for (int page = 0; page < dst_height_pages; page++) {\
        for (int x = 0; x < dst_width; x++) {\
            uint8_t dst_byte = 0;\
            for (int bit = 0; bit < 8; bit++) {\
                /* Calculate source bit position */\
                int src_y = page * 8 + bit;\
                int total_bit_offset = src_y * src_width + x;\
                int src_byte_index = total_bit_offset >> 3;\
                int src_bit = 7 - (total_bit_offset & 7);\
                \
                if (((uint8_t*)bitmap)[src_byte_index] & (1 << src_bit)) {\
                    dst_byte |= (1 << bit);\
                }\
            }\
            ssd1306_buffer[page * dst_width + x] = dst_byte;\
        }\
     }\
     bitmap = ssd1306_buffer;
#endif