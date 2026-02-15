#define PANEL_DEPENDENCIES
#include "panel.h"
#include <memory.h>
#include "esp_log.h"
#include "driver/gpio.h"
#include "esp_lcd_panel_io.h"
#include "esp_lcd_panel_ops.h"
#include "esp_lcd_panel_vendor.h"
#ifdef PANEL_USE_SPI
#include "driver/spi_master.h"
#endif
#ifdef PANEL_USE_I2C
#ifndef LEGACY_I2C
#include "driver/i2c_master.h"
#else
#include "driver/i2c.h"
#endif
#endif
#ifdef SD_BUS
#include "esp_vfs_fat.h"
#include "sdmmc_cmd.h"
#include "driver/sdmmc_host.h"
#endif
#if defined(LCD_MIPI_DSI_PHY_PWR_LDO_CHAN) && LCD_MIPI_DSI_PHY_PWR_LDO_CHAN!=-1
#include "esp_ldo_regulator.h"
#endif
#if LCD_BUS == PANEL_BUS_MIPI
#include "esp_dma_utils.h"
#include "esp_lcd_mipi_dsi.h"
#endif
#if LCD_BUS == PANEL_BUS_RGB
#include "esp_lcd_panel_rgb.h"
#endif
#ifdef LCD_BCKL_PWM_CHANNEL
#include "driver/ledc.h"
#endif
#ifdef EXPANDER
#include "esp_io_expander.h"
#endif
static const char* TAG = "panel";
static esp_lcd_panel_handle_t lcd_handle = NULL;
#if LCD_BUS != PANEL_BUS_RGB || defined(LCD_INIT)
static esp_lcd_panel_io_handle_t lcd_io_handle = NULL;
#endif
#if LCD_BUS == PANEL_BUS_MIPI
static esp_ldo_channel_handle_t ldo_mipi_phy = NULL;
static esp_lcd_dsi_bus_handle_t mipi_dsi_bus = NULL;
#endif
#if LCD_TRANSFER_SIZE > 0
static void* draw_buffer = NULL;
#if LCD_SYNC_TRANSFER == 0
static void* draw_buffer2 = NULL;
#endif
#endif
#if LCD_SYNC_TRANSFER > 0
static volatile int lcd_flushing = 0;
#endif
#if LCD_VSYNC > 0
static volatile bool vsync_count = 0;
#endif

#ifdef PANEL_USE_SPI
static bool spi_initialized = false;
static void spi_init() {
    if(spi_initialized) {
        return;
    }
    spi_bus_config_t spi_cfg;
    uint32_t spi_sz;
#ifdef SPI_1_HOST_USED
        memset(&spi_cfg,0,sizeof(spi_cfg));
#if SPI_1_TRANSFER_SIZE > 0
        spi_sz = SPI_1_TRANSFER_SIZE+8;
#else
        spi_sz = 32*1024;
#endif
        if(spi_sz>32*1024) {
            ESP_LOGW(TAG,"SPI transfer size is limited to 32KB, but a SPI host 1 device demands more.");
            spi_sz = 32*1024;
        }
        spi_cfg.max_transfer_sz = spi_sz;
        spi_cfg.data0_io_num = SPI_1_PIN_NUM_D00;
        spi_cfg.data1_io_num = SPI_1_PIN_NUM_D01;
        spi_cfg.data2_io_num = SPI_1_PIN_NUM_D02;
        spi_cfg.data3_io_num = SPI_1_PIN_NUM_D03;
        spi_cfg.data4_io_num = SPI_1_PIN_NUM_D04;
        spi_cfg.data5_io_num = SPI_1_PIN_NUM_D05;
        spi_cfg.data6_io_num = SPI_1_PIN_NUM_D06;
        spi_cfg.data7_io_num = SPI_1_PIN_NUM_D07;
        spi_cfg.sclk_io_num = SPI_1_PIN_NUM_CLK;
        ESP_ERROR_CHECK(spi_bus_initialize((spi_host_device_t)SPI_1,&spi_cfg,SPI_DMA_CH_AUTO));    
#endif
#ifdef SPI_2_HOST_USED
        memset(&spi_cfg,0,sizeof(spi_cfg));
#if SPI_2_TRANSFER_SIZE > 0
        spi_sz = SPI_2_TRANSFER_SIZE+8;
#else
        spi_sz = 32*1024;
#endif
        if(spi_sz>32*1024) {
            ESP_LOGW(TAG,"SPI transfer size is limited to 32KB, but a SPI host 2 device demands more.");
            spi_sz = 32*1024;
        }
        spi_cfg.max_transfer_sz = spi_sz;
        spi_cfg.data0_io_num = SPI_2_PIN_NUM_D00;
        spi_cfg.data1_io_num = SPI_2_PIN_NUM_D01;
        spi_cfg.data2_io_num = SPI_2_PIN_NUM_D02;
        spi_cfg.data3_io_num = SPI_2_PIN_NUM_D03;
        spi_cfg.data4_io_num = SPI_2_PIN_NUM_D04;
        spi_cfg.data5_io_num = SPI_2_PIN_NUM_D05;
        spi_cfg.data6_io_num = SPI_2_PIN_NUM_D06;
        spi_cfg.data7_io_num = SPI_2_PIN_NUM_D07;
        spi_cfg.sclk_io_num = SPI_2_PIN_NUM_CLK;
        ESP_ERROR_CHECK(spi_bus_initialize((spi_host_device_t)SPI_2,&spi_cfg,SPI_DMA_CH_AUTO));    
#endif

#ifdef SPI_3_HOST_USED
        memset(&spi_cfg,0,sizeof(spi_cfg));
#if SPI_3_TRANSFER_SIZE > 0
        spi_sz = SPI_3_TRANSFER_SIZE+8;
#else
        spi_sz = 32*1024;
#endif
        if(spi_sz>32*1024) {
            ESP_LOGW(TAG,"SPI transfer size is limited to 32KB, but a SPI host 3 device demands more.");
            spi_sz = 32*1024;
        }
        spi_cfg.max_transfer_sz = spi_sz;
        spi_cfg.data0_io_num = SPI_3_PIN_NUM_D00;
        spi_cfg.data1_io_num = SPI_3_PIN_NUM_D01;
        spi_cfg.data2_io_num = SPI_3_PIN_NUM_D02;
        spi_cfg.data3_io_num = SPI_3_PIN_NUM_D03;
        spi_cfg.data4_io_num = SPI_3_PIN_NUM_D04;
        spi_cfg.data5_io_num = SPI_3_PIN_NUM_D05;
        spi_cfg.data6_io_num = SPI_3_PIN_NUM_D06;
        spi_cfg.data7_io_num = SPI_3_PIN_NUM_D07;
        spi_cfg.sclk_io_num = SPI_3_PIN_NUM_CLK;
        ESP_ERROR_CHECK(spi_bus_initialize((spi_host_device_t)SPI_3,&spi_cfg,SPI_DMA_CH_AUTO));    
#endif
#ifdef SPI_4_HOST_USED
        memset(&spi_cfg,0,sizeof(spi_cfg));
#if SPI_4_TRANSFER_SIZE > 0
        spi_sz = SPI_4_TRANSFER_SIZE+8;
#else
        spi_sz = 32*1024;
#endif
        if(spi_sz>32*1024) {
            ESP_LOGW(TAG,"SPI transfer size is limited to 32KB, but a SPI host 4 device demands more.");
            spi_sz = 32*1024;
        }
        spi_cfg.max_transfer_sz = spi_sz;
        spi_cfg.data0_io_num = SPI_4_PIN_NUM_D00;
        spi_cfg.data1_io_num = SPI_4_PIN_NUM_D01;
        spi_cfg.data2_io_num = SPI_4_PIN_NUM_D02;
        spi_cfg.data3_io_num = SPI_4_PIN_NUM_D03;
        spi_cfg.data4_io_num = SPI_4_PIN_NUM_D04;
        spi_cfg.data5_io_num = SPI_4_PIN_NUM_D05;
        spi_cfg.data6_io_num = SPI_4_PIN_NUM_D06;
        spi_cfg.data7_io_num = SPI_4_PIN_NUM_D07;
        spi_cfg.sclk_io_num = SPI_4_PIN_NUM_CLK;
        ESP_ERROR_CHECK(spi_bus_initialize((spi_host_device_t)SPI_4,&spi_cfg,SPI_DMA_CH_AUTO));    
#endif
        spi_initialized = true;
}
#endif

#ifdef PANEL_USE_I2C
static bool i2c_initialized = false;
static void i2c_init() {
    if(i2c_initialized) {
        return;
    }
#ifndef LEGACY_I2C
    i2c_master_bus_config_t i2c_cfg;
    i2c_master_bus_handle_t i2c_bus_handle;
#else
    i2c_config_t i2c_cfg;
#endif
#ifdef I2C_1_HOST_USED
        memset(&i2c_cfg,0,sizeof(i2c_cfg));
#ifndef LEGACY_I2C
        i2c_cfg.clk_source = I2C_CLK_SRC_DEFAULT;
        i2c_cfg.glitch_ignore_cnt = 7;
        i2c_cfg.i2c_port = (i2c_port_num_t)I2C_1;
        i2c_cfg.sda_io_num = I2C_1_PIN_NUM_SDA;
        i2c_cfg.scl_io_num = I2C_1_PIN_NUM_SCL;
#if defined(I2C_1_PULLUP)
        i2c_cfg.flags.enable_internal_pullup = I2C_1_PULLUP;
#else
        i2c_cfg.flags.enable_internal_pullup = 0;
#endif
        ESP_ERROR_CHECK(i2c_new_master_bus(&i2c_cfg,&i2c_bus_handle));
#else // legacy code
#ifdef I2C_1_CLOCK_HZ
        i2c_cfg.master.clk_speed = I2C_1_CLOCK_HZ;
#else
        i2c_cfg.master.clk_speed = 100 * 1000;
#endif
        i2c_cfg.mode = I2C_MODE_MASTER;
        i2c_cfg.sda_io_num = I2C_1_PIN_NUM_SDA;
#if defined(I2C_1_PULLUP)
        i2c_cfg.sda_pullup_en = I2C_1_PULLUP;
#else
        i2c_cfg.sda_pullup_en = 0;
#endif
        i2c_cfg.scl_io_num = I2C_1_PIN_NUM_SCL;
#if defined(I2C_1_PULLUP)
        i2c_cfg.scl_pullup_en = I2C_1_PULLUP;
#else
        i2c_cfg.scl_pullup_en = 0;
#endif
        ESP_ERROR_CHECK(i2c_driver_install((i2c_port_t)I2C_1,I2C_MODE_MASTER,0,0,0));
        ESP_ERROR_CHECK(i2c_param_config((i2c_port_t)I2C_1,&i2c_cfg));
#endif 
#endif // I2C_1_HOST_USED
#ifdef I2C_2_HOST_USED
        memset(&i2c_cfg,0,sizeof(i2c_cfg));
#ifndef LEGACY_I2C
        i2c_cfg.clk_source = I2C_CLK_SRC_DEFAULT;
        i2c_cfg.glitch_ignore_cnt = 7;
        i2c_cfg.i2c_port = (i2c_port_num_t)I2C_2;
        i2c_cfg.sda_io_num = I2C_2_PIN_NUM_SDA;
        i2c_cfg.scl_io_num = I2C_2_PIN_NUM_SCL;
#if defined(I2C_2_PULLUP)
        i2c_cfg.flags.enable_internal_pullup = I2C_2_PULLUP;
#else
        i2c_cfg.flags.enable_internal_pullup = 0;
#endif
        ESP_ERROR_CHECK(i2c_new_master_bus(&i2c_cfg,&i2c_bus_handle));
#else // legacy code
#ifdef I2C_2_CLOCK_HZ
        i2c_cfg.master.clk_speed = I2C_2_CLOCK_HZ;
#else
        i2c_cfg.master.clk_speed = 100 * 1000;
#endif
        i2c_cfg.mode = I2C_MODE_MASTER;
        i2c_cfg.sda_io_num = I2C_2_PIN_NUM_SDA;
#if defined(I2C_2_PULLUP)
        i2c_cfg.sda_pullup_en = I2C_2_PULLUP;
#else
        i2c_cfg.sda_pullup_en = 0;
#endif
        i2c_cfg.scl_io_num = I2C_2_PIN_NUM_SCL;
#if defined(I2C_2_PULLUP)
        i2c_cfg.scl_pullup_en = I2C_2_PULLUP;
#else
        i2c_cfg.scl_pullup_en = 0;
#endif
        ESP_ERROR_CHECK(i2c_driver_install((i2c_port_t)I2C_2,I2C_MODE_MASTER,0,0,0));
        ESP_ERROR_CHECK(i2c_param_config((i2c_port_t)I2C_2,&i2c_cfg));
#endif 
#endif // I2C_2_HOST_USED
        i2c_initialized=true;
    }
#endif 
#ifdef LCD_BUS
size_t panel_lcd_vsync_flush_count(void) { 
#if LCD_VSYNC > 0
    return vsync_count; 
#else
    return 0;
#endif
}

void panel_lcd_flush(uint16_t x1, uint16_t y1, uint16_t x2, uint16_t y2, void *bitmap)
{
    if(lcd_handle==NULL) {
        ESP_LOGE(TAG,"panel_lcd_flush() was invoked but panel_lcd_init() was never called.");
        return;
    }
#if LCD_SYNC_TRANSFER > 0
    lcd_flushing = 1;
#endif
#ifdef LCD_TRANSLATE
    LCD_TRANSLATE;
#endif    
    ESP_ERROR_CHECK(esp_lcd_panel_draw_bitmap(lcd_handle, x1, y1, x2 + 1, y2 + 1, bitmap));
#if LCD_SYNC_TRANSFER > 0
    while(lcd_flushing) portYIELD();
#endif

#if LCD_VSYNC > 0
    vsync_count = vsync_count + 1;
#endif
}
#if  LCD_BUS == PANEL_BUS_RGB
// LCD Panel API calls this
static IRAM_ATTR bool on_flush_complete(esp_lcd_panel_handle_t panel, const esp_lcd_rgb_panel_event_data_t *edata, void *user_ctx) {
    // let the display know the flush has finished
#if LCD_SYNC_TRANSFER == 0
    panel_lcd_flush_complete();
#else
    lcd_flushing = 0;
#endif
    return true;
}
#if LCD_VSYNC > 0
// LCD Panel API calls this
static IRAM_ATTR bool on_vsync(esp_lcd_panel_handle_t panel, const esp_lcd_rgb_panel_event_data_t *edata, void *user_ctx) {
    vsync_count = 0;
    return true;
}
#endif
#endif
#if (LCD_BUS==PANEL_BUS_SPI || LCD_BUS==PANEL_BUS_I2C || LCD_BUS==PANEL_BUS_I8080)
static IRAM_ATTR bool on_flush_complete(esp_lcd_panel_io_handle_t lcd_io, esp_lcd_panel_io_event_data_t* edata, void* user_ctx) {
    // let the display know the flush has finished
#if LCD_SYNC_TRANSFER == 0
    panel_lcd_flush_complete();
#else
    lcd_flushing = 0;
#endif
    return true;
}
#endif
#if LCD_BUS == PANEL_BUS_MIPI
static IRAM_ATTR bool on_flush_complete(esp_lcd_panel_handle_t panel, esp_lcd_dpi_panel_event_data_t *edata, void *user_ctx) {
    // let the display know the flush has finished
#if LCD_SYNC_TRANSFER == 0
    panel_lcd_flush_complete();
#else
    lcd_flushing = 0;
#endif
    return true;
}
#endif
void panel_lcd_init(void) {
    if(lcd_handle!=NULL) {
        ESP_LOGW(TAG,"lcd_init() was already called");
        return; // already initialized
    }
#ifdef LCD_PIN_NUM_BCKL
#if LCD_PIN_NUM_BCKL >= 0
#ifdef LCD_BCKL_PWM_CHANNEL
    ledc_timer_config_t ledc_timer;
    memset(&ledc_timer,0,sizeof(ledc_timer)); 
    ledc_timer.speed_mode       = LEDC_LOW_SPEED_MODE;
    ledc_timer.duty_resolution  = LEDC_TIMER_8_BIT;
    ledc_timer.timer_num        = LEDC_TIMER_0;
    ledc_timer.freq_hz          = 5000;
    ledc_timer.clk_cfg          = LEDC_AUTO_CLK;
    ESP_ERROR_CHECK(ledc_timer_config(&ledc_timer));
    ledc_channel_config_t ledc_cfg;
    memset(&ledc_cfg,0,sizeof(ledc_cfg));
    ledc_cfg.channel = LCD_BCKL_PWM_CHANNEL;
    ledc_cfg.duty = (!LCD_BCKL_ON_LEVEL)*255;
    ledc_cfg.hpoint = 0;
    ledc_cfg.speed_mode = LEDC_LOW_SPEED_MODE;
    ledc_cfg.gpio_num = LCD_PIN_NUM_BCKL;
    ledc_cfg.timer_sel = LEDC_TIMER_0;
    ledc_cfg.flags.output_invert= 0;
    ledc_cfg.intr_type = LEDC_INTR_DISABLE;
    ESP_ERROR_CHECK(ledc_channel_config(&ledc_cfg));
#else
    gpio_config_t bk_gpio_config;
    memset(&bk_gpio_config,0,sizeof(gpio_config_t));
    bk_gpio_config.mode = GPIO_MODE_OUTPUT;
    bk_gpio_config.pin_bit_mask = 1ULL << LCD_PIN_NUM_BCKL;
    ESP_ERROR_CHECK(gpio_config(&bk_gpio_config));
    gpio_set_level((gpio_num_t)LCD_PIN_NUM_BCKL, LCD_BCKL_OFF_LEVEL);
#endif
#endif
#endif
    // Turn on the power for MIPI DSI PHY, so it can go from "No Power" state to "Shutdown" state
#if defined(LCD_MIPI_DSI_PHY_PWR_LDO_CHAN) && LCD_MIPI_DSI_PHY_PWR_LDO_CHAN!=-1
    esp_ldo_channel_config_t ldo_mipi_phy_config = {
        .chan_id = LCD_MIPI_DSI_PHY_PWR_LDO_CHAN,
        .voltage_mv = LCD_MIPI_DSI_PHY_PWR_LDO_VOLTAGE_MV,
    };
    ESP_ERROR_CHECK(esp_ldo_acquire_channel(&ldo_mipi_phy_config, &ldo_mipi_phy));
#endif
#if LCD_BUS == PANEL_BUS_I8080
    gpio_set_direction((gpio_num_t)LCD_PIN_NUM_RD, GPIO_MODE_OUTPUT);
    gpio_set_level((gpio_num_t)LCD_PIN_NUM_RD, 1);
#ifdef LCD_RESET
    LCD_RESET;
#endif
    esp_lcd_i80_bus_handle_t i80_bus = NULL;
    esp_lcd_i80_bus_config_t i80_cfg;
    memset(&i80_cfg, 0, sizeof(i80_cfg));
    i80_cfg.clk_src = LCD_CLK_SRC_DEFAULT;
    i80_cfg.dc_gpio_num = LCD_PIN_NUM_DC;
    i80_cfg.wr_gpio_num = LCD_PIN_NUM_WR;
#ifndef LCD_PIN_NUM_D15    
    i80_cfg.data_gpio_nums[0] = LCD_PIN_NUM_D00;
    i80_cfg.data_gpio_nums[1] = LCD_PIN_NUM_D01;
    i80_cfg.data_gpio_nums[2] = LCD_PIN_NUM_D02;
    i80_cfg.data_gpio_nums[3] = LCD_PIN_NUM_D03;
    i80_cfg.data_gpio_nums[4] = LCD_PIN_NUM_D04;
    i80_cfg.data_gpio_nums[5] = LCD_PIN_NUM_D05;
    i80_cfg.data_gpio_nums[6] = LCD_PIN_NUM_D06;
    i80_cfg.data_gpio_nums[7] = LCD_PIN_NUM_D07;
    bus_config.bus_width = 8;
#else
    i80_cfg.data_gpio_nums[0] = LCD_PIN_NUM_D00;
    i80_cfg.data_gpio_nums[1] = LCD_PIN_NUM_D01;
    i80_cfg.data_gpio_nums[2] = LCD_PIN_NUM_D02;
    i80_cfg.data_gpio_nums[3] = LCD_PIN_NUM_D03;
    i80_cfg.data_gpio_nums[4] = LCD_PIN_NUM_D04;
    i80_cfg.data_gpio_nums[5] = LCD_PIN_NUM_D05;
    i80_cfg.data_gpio_nums[6] = LCD_PIN_NUM_D06;
    i80_cfg.data_gpio_nums[7] = LCD_PIN_NUM_D07;
    i80_cfg.data_gpio_nums[8] = LCD_PIN_NUM_D08;
    i80_cfg.data_gpio_nums[9] = LCD_PIN_NUM_D09;
    i80_cfg.data_gpio_nums[10] = LCD_PIN_NUM_D10;
    i80_cfg.data_gpio_nums[11] = LCD_PIN_NUM_D11;
    i80_cfg.data_gpio_nums[12] = LCD_PIN_NUM_D12;
    i80_cfg.data_gpio_nums[13] = LCD_PIN_NUM_D13;
    i80_cfg.data_gpio_nums[14] = LCD_PIN_NUM_D14;
    i80_cfg.data_gpio_nums[15] = LCD_PIN_NUM_D15;
    i80_cfg.bus_width = 16;
#endif  // LCD_PIN_NUM_D15
    i80_cfg.dma_burst_size = 64;
    i80_cfg.max_transfer_bytes = LCD_TRANSFER_SIZE+8;
    ESP_ERROR_CHECK(esp_lcd_new_i80_bus(&i80_cfg, &i80_bus));
    esp_lcd_panel_io_i80_config_t i80_io_cfg;    
    memset(&i80_io_cfg, 0, sizeof(i80_io_cfg));
    i80_io_cfg.cs_gpio_num = LCD_PIN_NUM_CS;
    i80_io_cfg.pclk_hz = LCD_CLOCK_HZ;
    i80_io_cfg.trans_queue_depth = 20;
#ifdef LCD_DC_ON_LEVEL
    i80_io_cfg.dc_levels.dc_cmd_level = !LCD_DC_ON_LEVEL;
#else
    i80_io_cfg.dc_levels.dc_cmd_level = 0;
#endif
    i80_io_cfg.dc_levels.dc_dummy_level = 0;
#ifdef LCD_DC_ON_LEVEL
    i80_io_cfg.dc_levels.dc_data_level = LCD_DC_ON_LEVEL;
    i80_io_cfg.dc_levels.dc_idle_level = LCD_DC_ON_LEVEL;
#else
    i80_io_cfg.dc_levels.dc_data_level = 1;
    i80_io_cfg.dc_levels.dc_idle_level = 1;
#endif
    i80_io_cfg.lcd_cmd_bits = LCD_CMD_BITS;
    i80_io_cfg.lcd_param_bits = LCD_PARAM_BITS;
    i80_io_cfg.on_color_trans_done = on_flush_complete;
    i80_io_cfg.user_ctx = NULL;
#ifdef LCD_SWAP_COLOR_BYTES
    i80_io_cfg.flags.swap_color_bytes = LCD_SWAP_COLOR_BYTES;
#else
    io_config.flags.swap_color_bytes = false;
#endif  // LCD_SWAP_COLOR_BYTES
#ifdef LCD_CS_ON_LEVEL
    i80_io_cfg.flags.cs_active_high = !LCD_CS_ON_LEVEL;
#else
    i80_io_cfg.flags.cs_active_high = 0;
#endif
#ifdef LCD_REVERSE_COLOR_BITS
    i80_io_cfg.flags.reverse_color_bits = LCD_REVERSE_COLOR_BITS;
#endif

    ESP_ERROR_CHECK(esp_lcd_new_panel_io_i80(i80_bus, &i80_io_cfg, &lcd_io_handle));
#endif
#if LCD_BUS == PANEL_BUS_RGB
#ifdef LCD_RESET
    LCD_RESET;
#endif
    esp_lcd_rgb_panel_config_t rgb_panel_cfg;
    memset(&rgb_panel_cfg,0,sizeof(esp_lcd_rgb_panel_config_t));
    rgb_panel_cfg.data_width = LCD_DATA_WIDTH;
    rgb_panel_cfg.clk_src = LCD_CLK_SRC_DEFAULT;
#ifdef LCD_PIN_NUM_DISP
    rgb_panel_cfg.disp_gpio_num = LCD_PIN_NUM_DISP;
#else
    rgb_panel_cfg.disp_gpio_num = -1;
#endif
    rgb_panel_cfg.pclk_gpio_num = LCD_PIN_NUM_CLK;

    rgb_panel_cfg.vsync_gpio_num = LCD_PIN_NUM_VSYNC;
    rgb_panel_cfg.hsync_gpio_num = LCD_PIN_NUM_HSYNC;
#ifdef LCD_PIN_NUM_DE
    rgb_panel_cfg.de_gpio_num = LCD_PIN_NUM_DE;
#else
    rgb_panel_cfg.de_gpio_num = -1;
#endif
#if LCD_DATA_WIDTH == 8
    rgb_panel_cfg.data_gpio_nums[8]=LCD_PIN_NUM_D08;
    rgb_panel_cfg.data_gpio_nums[9]=LCD_PIN_NUM_D09;
    rgb_panel_cfg.data_gpio_nums[10]=LCD_PIN_NUM_D10;
    rgb_panel_cfg.data_gpio_nums[11]=LCD_PIN_NUM_D11;
    rgb_panel_cfg.data_gpio_nums[12]=LCD_PIN_NUM_D12;
    rgb_panel_cfg.data_gpio_nums[13]=LCD_PIN_NUM_D13;
    rgb_panel_cfg.data_gpio_nums[14]=LCD_PIN_NUM_D14;
    rgb_panel_cfg.data_gpio_nums[15]=LCD_PIN_NUM_D15;
#elif LCD_DATA_WIDTH==16 && (!defined(LCD_SWAP_COLOR_BYTES) || LCD_SWAP_COLOR_BYTES) == 0
#if LCD_COLOR_SPACE != LCD_COLOR_BGR
    rgb_panel_cfg.data_gpio_nums[0]=LCD_PIN_NUM_D00;
    rgb_panel_cfg.data_gpio_nums[1]=LCD_PIN_NUM_D01;
    rgb_panel_cfg.data_gpio_nums[2]=LCD_PIN_NUM_D02;
    rgb_panel_cfg.data_gpio_nums[3]=LCD_PIN_NUM_D03;
    rgb_panel_cfg.data_gpio_nums[4]=LCD_PIN_NUM_D04;
    rgb_panel_cfg.data_gpio_nums[5]=LCD_PIN_NUM_D05;
    rgb_panel_cfg.data_gpio_nums[6]=LCD_PIN_NUM_D06;
    rgb_panel_cfg.data_gpio_nums[7]=LCD_PIN_NUM_D07;
    rgb_panel_cfg.data_gpio_nums[8]=LCD_PIN_NUM_D08;
    rgb_panel_cfg.data_gpio_nums[9]=LCD_PIN_NUM_D09;
    rgb_panel_cfg.data_gpio_nums[10]=LCD_PIN_NUM_D10;
    rgb_panel_cfg.data_gpio_nums[11]=LCD_PIN_NUM_D11;
    rgb_panel_cfg.data_gpio_nums[12]=LCD_PIN_NUM_D12;
    rgb_panel_cfg.data_gpio_nums[13]=LCD_PIN_NUM_D13;
    rgb_panel_cfg.data_gpio_nums[14]=LCD_PIN_NUM_D14;
    rgb_panel_cfg.data_gpio_nums[15]=LCD_PIN_NUM_D15;
#else
    rgb_panel_cfg.data_gpio_nums[0]=LCD_PIN_NUM_D11;
    rgb_panel_cfg.data_gpio_nums[1]=LCD_PIN_NUM_D12;
    rgb_panel_cfg.data_gpio_nums[2]=LCD_PIN_NUM_D13;
    rgb_panel_cfg.data_gpio_nums[3]=LCD_PIN_NUM_D14;
    rgb_panel_cfg.data_gpio_nums[4]=LCD_PIN_NUM_D15;
    rgb_panel_cfg.data_gpio_nums[5]=LCD_PIN_NUM_D05;
    rgb_panel_cfg.data_gpio_nums[6]=LCD_PIN_NUM_D06;
    rgb_panel_cfg.data_gpio_nums[7]=LCD_PIN_NUM_D07;
    rgb_panel_cfg.data_gpio_nums[8]=LCD_PIN_NUM_D08;
    rgb_panel_cfg.data_gpio_nums[9]=LCD_PIN_NUM_D09;
    rgb_panel_cfg.data_gpio_nums[10]=LCD_PIN_NUM_D10;
    rgb_panel_cfg.data_gpio_nums[11]=LCD_PIN_NUM_D00;
    rgb_panel_cfg.data_gpio_nums[12]=LCD_PIN_NUM_D01;
    rgb_panel_cfg.data_gpio_nums[13]=LCD_PIN_NUM_D02;
    rgb_panel_cfg.data_gpio_nums[14]=LCD_PIN_NUM_D03;
    rgb_panel_cfg.data_gpio_nums[15]=LCD_PIN_NUM_D04;
#endif
#else
#if LCD_COLOR_SPACE != LCD_COLOR_BGR
    rgb_panel_cfg.data_gpio_nums[8]=LCD_PIN_NUM_D00;
    rgb_panel_cfg.data_gpio_nums[9]=LCD_PIN_NUM_D01;
    rgb_panel_cfg.data_gpio_nums[10]=LCD_PIN_NUM_D02;
    rgb_panel_cfg.data_gpio_nums[11]=LCD_PIN_NUM_D03;
    rgb_panel_cfg.data_gpio_nums[12]=LCD_PIN_NUM_D04;
    rgb_panel_cfg.data_gpio_nums[13]=LCD_PIN_NUM_D05;
    rgb_panel_cfg.data_gpio_nums[14]=LCD_PIN_NUM_D06;
    rgb_panel_cfg.data_gpio_nums[15]=LCD_PIN_NUM_D07;
    rgb_panel_cfg.data_gpio_nums[0]=LCD_PIN_NUM_D08;
    rgb_panel_cfg.data_gpio_nums[1]=LCD_PIN_NUM_D09;
    rgb_panel_cfg.data_gpio_nums[2]=LCD_PIN_NUM_D10;
    rgb_panel_cfg.data_gpio_nums[3]=LCD_PIN_NUM_D11;
    rgb_panel_cfg.data_gpio_nums[4]=LCD_PIN_NUM_D12;
    rgb_panel_cfg.data_gpio_nums[5]=LCD_PIN_NUM_D13;
    rgb_panel_cfg.data_gpio_nums[6]=LCD_PIN_NUM_D14;
    rgb_panel_cfg.data_gpio_nums[7]=LCD_PIN_NUM_D15;
#else
    rgb_panel_cfg.data_gpio_nums[8]=LCD_PIN_NUM_D11;
    rgb_panel_cfg.data_gpio_nums[9]=LCD_PIN_NUM_D12;
    rgb_panel_cfg.data_gpio_nums[10]=LCD_PIN_NUM_D13;
    rgb_panel_cfg.data_gpio_nums[11]=LCD_PIN_NUM_D14;
    rgb_panel_cfg.data_gpio_nums[12]=LCD_PIN_NUM_D15;
    rgb_panel_cfg.data_gpio_nums[13]=LCD_PIN_NUM_D05;
    rgb_panel_cfg.data_gpio_nums[14]=LCD_PIN_NUM_D06;
    rgb_panel_cfg.data_gpio_nums[15]=LCD_PIN_NUM_D07;
    rgb_panel_cfg.data_gpio_nums[0]=LCD_PIN_NUM_D08;
    rgb_panel_cfg.data_gpio_nums[1]=LCD_PIN_NUM_D09;
    rgb_panel_cfg.data_gpio_nums[2]=LCD_PIN_NUM_D10;
    rgb_panel_cfg.data_gpio_nums[3]=LCD_PIN_NUM_D00;
    rgb_panel_cfg.data_gpio_nums[4]=LCD_PIN_NUM_D01;
    rgb_panel_cfg.data_gpio_nums[5]=LCD_PIN_NUM_D02;
    rgb_panel_cfg.data_gpio_nums[6]=LCD_PIN_NUM_D03;
    rgb_panel_cfg.data_gpio_nums[7]=LCD_PIN_NUM_D04;
#endif

#endif
    memset(&rgb_panel_cfg.timings,0,sizeof(esp_lcd_rgb_timing_t));
    rgb_panel_cfg.timings.pclk_hz = LCD_CLOCK_HZ;
    rgb_panel_cfg.timings.h_res = LCD_HRES;
    rgb_panel_cfg.timings.v_res = LCD_VRES;
    rgb_panel_cfg.timings.hsync_back_porch = LCD_HSYNC_BACK_PORCH;
    rgb_panel_cfg.timings.hsync_front_porch = LCD_HSYNC_FRONT_PORCH;
    rgb_panel_cfg.timings.hsync_pulse_width = LCD_HSYNC_PULSE_WIDTH;
    rgb_panel_cfg.timings.vsync_back_porch = LCD_VSYNC_BACK_PORCH;
    rgb_panel_cfg.timings.vsync_front_porch = LCD_VSYNC_FRONT_PORCH;
    rgb_panel_cfg.timings.vsync_pulse_width = LCD_VSYNC_PULSE_WIDTH;
#ifdef LCD_CLK_ACTIVE_NEG
    rgb_panel_cfg.timings.flags.pclk_active_neg = LCD_CLK_ACTIVE_NEG;
#endif
    rgb_panel_cfg.timings.flags.hsync_idle_low = 0;
#ifdef LCD_CLK_ON_LEVEL
    rgb_panel_cfg.timings.flags.pclk_idle_high = !LCD_CLK_ON_LEVEL;
#else
    rgb_panel_cfg.timings.flags.pclk_idle_high = 0;
#endif
#ifdef LCD_DE_ON_LEVEL
    rgb_panel_cfg.timings.flags.de_idle_high = !LCD_DE_ON_LEVEL;
#else
    rgb_panel_cfg.timings.flags.de_idle_high = 0;
#endif
#ifdef LCD_VSYNC_ON_LEVEL
    rgb_panel_cfg.timings.flags.vsync_idle_low = LCD_VSYNC_ON_LEVEL;
#else
    rgb_panel_cfg.timings.flags.vsync_idle_low = 0;
#endif
    rgb_panel_cfg.flags.bb_invalidate_cache = 1;
#ifdef LCD_DISP_ON_LEVEL
    rgb_panel_cfg.flags.disp_active_low = LCD_DISP_ON_LEVEL;
#endif
    rgb_panel_cfg.flags.double_fb = 0;
    rgb_panel_cfg.flags.no_fb = 0;
    rgb_panel_cfg.flags.refresh_on_demand = 0;
    rgb_panel_cfg.flags.fb_in_psram = 1; // allocate frame buffer in PSRAM
    rgb_panel_cfg.dma_burst_size = 64;
#ifdef LCD_FRAMEBUFFER_COUNT
    rgb_panel_cfg.num_fbs = LCD_FRAMEBUFFER_COUNT;
#else
    rgb_panel_cfg.num_fbs = 1;
#endif
    rgb_panel_cfg.bounce_buffer_size_px = LCD_HRES*(LCD_VRES/LCD_DIVISOR);
#endif
#if LCD_BUS == PANEL_BUS_SPI
    spi_init();
#ifdef LCD_RESET
    LCD_RESET;
#endif
    esp_lcd_panel_io_spi_config_t lcd_spi_cfg;
    memset(&lcd_spi_cfg,0,sizeof(lcd_spi_cfg));
    lcd_spi_cfg.cs_gpio_num = LCD_PIN_NUM_CS;
    lcd_spi_cfg.dc_gpio_num = LCD_PIN_NUM_DC;
    lcd_spi_cfg.lcd_cmd_bits = LCD_CMD_BITS;
    lcd_spi_cfg.lcd_param_bits = LCD_PARAM_BITS;        
#ifdef LCD_CLOCK_HZ
    lcd_spi_cfg.pclk_hz = LCD_CLOCK_HZ;
#else
    lcd_spi_cfg.pclk_hz = 20 * 1000 * 1000;
#endif
    lcd_spi_cfg.trans_queue_depth = 10;
    lcd_spi_cfg.on_color_trans_done = on_flush_complete;
#ifdef LCD_SPI_MODE
    lcd_spi_cfg.spi_mode = LCD_SPI_MODE;
#else
    lcd_spi_cfg.spi_mode = 0;
#endif
    ESP_ERROR_CHECK(esp_lcd_new_panel_io_spi((esp_lcd_spi_bus_handle_t)LCD_SPI_HOST, &lcd_spi_cfg, &lcd_io_handle));
#endif
#if LCD_BUS == PANEL_BUS_I2C
    i2c_init();
#ifdef LCD_RESET
    LCD_RESET;
#endif
    esp_lcd_panel_io_i2c_config_t lcd_i2c_cfg;
    memset(&lcd_i2c_cfg,0,sizeof(lcd_i2c_cfg));
    lcd_i2c_cfg.control_phase_bytes = LCD_CONTROL_PHASE_BYTES;
    lcd_i2c_cfg.dc_bit_offset = LCD_DC_BIT_OFFSET;
    lcd_i2c_cfg.dev_addr = LCD_I2C_ADDRESS;
#if LCD_DISABLE_CONTROL_PHASE > 0
    lcd_i2c_cfg.flags.disable_control_phase = true;
#endif
    lcd_i2c_cfg.lcd_cmd_bits = LCD_CMD_BITS;
    lcd_i2c_cfg.lcd_param_bits = LCD_PARAM_BITS;
    lcd_i2c_cfg.on_color_trans_done = on_flush_complete;
#ifndef LEGACY_I2C
#ifdef LCD_CLOCK_HZ
    lcd_i2c_cfg.scl_speed_hz = LCD_CLOCK_HZ;
#else
    lcd_i2c_cfg.scl_speed_hz = 100 * 1000;
#endif
#else
    lcd_i2c_cfg.scl_speed_hz = 0;
#endif
#ifndef LEGACY_I2C
    i2c_master_bus_handle_t i2c_bus_handle;
    ESP_ERROR_CHECK(i2c_master_get_bus_handle((i2c_port_num_t)LCD_I2C_HOST,&i2c_bus_handle));
    ESP_ERROR_CHECK(esp_lcd_new_panel_io_i2c_v2((i2c_master_bus_handle_t)i2c_bus_handle, &lcd_i2c_cfg, &lcd_io_handle));
#else
    ESP_ERROR_CHECK(esp_lcd_new_panel_io_i2c_v1((uint32_t)LCD_I2C_HOST, &lcd_i2c_cfg, &lcd_io_handle));
#endif
#endif
#if LCD_BUS == PANEL_BUS_MIPI
    esp_lcd_dsi_bus_config_t dsi_bus_config = {                                                    \
        .bus_id = LCD_MIPI_BUS_ID,                                     
        .num_data_lanes = LCD_MIPI_LANES,                             
        .phy_clk_src = LCD_MIPI_CLK_SRC,     
        .lane_bit_rate_mbps = LCD_MIPI_LANE_MBPS,                      
    };
    ESP_ERROR_CHECK(esp_lcd_new_dsi_bus(&dsi_bus_config, &mipi_dsi_bus));
    
    esp_lcd_dbi_io_config_t dbi_config = {                                 
        .virtual_channel = LCD_MIPI_CHANNEL,         
        .lcd_cmd_bits = LCD_CMD_BITS,            
        .lcd_param_bits = LCD_PARAM_BITS,          
    };
    ESP_ERROR_CHECK(esp_lcd_new_panel_io_dbi(mipi_dsi_bus, &dbi_config, &lcd_io_handle));

    esp_lcd_dpi_panel_config_t dpi_config=// = ST7703_720_720_PANEL_60HZ_DPI_CONFIG(MIPI_DPI_PX_FORMAT);
    {   
        .virtual_channel = LCD_MIPI_CHANNEL,                                                                             
        .dpi_clk_src = MIPI_DSI_DPI_CLK_SRC_DEFAULT,     
        .dpi_clock_freq_mhz = LCD_CLOCK_HZ/(1000 * 1000),                        
        .pixel_format = MIPI_DPI_PX_FORMAT,        
#ifdef LCD_FRAMEBUFFER_COUNT
        .num_fbs = LCD_FRAMEBUFFER_COUNT,    
#else 
        .num_fbs = 1,                                    
#endif
        .video_timing = {                                
            .h_size = LCD_HRES,                               
            .v_size = LCD_VRES,                               
            .hsync_pulse_width = LCD_HSYNC_PULSE_WIDTH,                     
            .hsync_back_porch = LCD_HSYNC_BACK_PORCH,                     
            .hsync_front_porch = LCD_HSYNC_FRONT_PORCH,                    
            .vsync_pulse_width = LCD_VSYNC_PULSE_WIDTH,                     
            .vsync_back_porch = LCD_VSYNC_BACK_PORCH,                      
            .vsync_front_porch = LCD_VSYNC_FRONT_PORCH,                     
        },           
#if defined(LCD_MIPI_DMA2D)
        .flags.use_dma2d = LCD_MIPI_DMA2D,
#endif
    };

#endif
#if (LCD_BUS != PANEL_BUS_RGB) || defined(LCD_INIT)
    esp_lcd_panel_dev_config_t panel_config;
    memset(&panel_config,0,sizeof(panel_config));
#ifdef LCD_VENDOR_CONFIG
    LCD_VENDOR_CONFIG;
#endif
#ifdef LCD_PIN_NUM_RST
    panel_config.reset_gpio_num = LCD_PIN_NUM_RST;
#else
    panel_config.reset_gpio_num = -1;
#endif
#if defined(LCD_RST_ON_LEVEL)
    panel_config.flags.reset_active_high = LCD_RST_ON_LEVEL;
#endif
    
#if LCD_COLOR_SPACE == LCD_COLOR_RGB
    panel_config.rgb_ele_order = LCD_RGB_ELEMENT_ORDER_RGB;
#elif LCD_COLOR_SPACE == LCD_COLOR_BGR
    panel_config.rgb_ele_order = LCD_RGB_ELEMENT_ORDER_BGR;
#elif LCD_COLOR_SPACE == LCD_COLOR_GSC
    // seems to work
    panel_config.rgb_ele_order = LCD_RGB_ELEMENT_ORDER_RGB;
#endif
#if defined(LCD_DATA_ENDIAN_LITTLE) && LCD_DATA_ENDIAN_LITTLE > 0
    panel_config.data_endian = LCD_RGB_DATA_ENDIAN_LITTLE;
#else
    panel_config.data_endian = LCD_RGB_DATA_ENDIAN_BIG;
#endif
    panel_config.bits_per_pixel = LCD_BIT_DEPTH;
#if LCD_BUS == PANEL_BUS_RGB && defined(LCD_IO_INIT)
    LCD_IO_INIT;
#endif
#ifdef LCD_VENDOR_CONFIG
    panel_config.vendor_config = &vendor_config;
#else
    panel_config.vendor_config = NULL;
#endif
    ESP_ERROR_CHECK(LCD_INIT(lcd_io_handle, &panel_config, &lcd_handle));
#else
    ESP_ERROR_CHECK(esp_lcd_new_rgb_panel(&rgb_panel_cfg, &lcd_handle));   
#endif
    ESP_ERROR_CHECK(esp_lcd_panel_reset(lcd_handle));
    ESP_ERROR_CHECK(esp_lcd_panel_init(lcd_handle));
#if LCD_BUS == PANEL_BUS_RGB
    esp_lcd_rgb_panel_event_callbacks_t rgb_cbs;
    memset(&rgb_cbs,0,sizeof(rgb_cbs));
    rgb_cbs.on_color_trans_done = on_flush_complete;
    rgb_cbs.on_vsync = on_vsync;
    ESP_ERROR_CHECK(esp_lcd_rgb_panel_register_event_callbacks(lcd_handle,&rgb_cbs,NULL));
#endif
#if LCD_BUS == PANEL_BUS_MIPI 
    esp_lcd_dpi_panel_event_callbacks_t mipi_cbs = {
        .on_color_trans_done = on_flush_complete,
    };
    ESP_ERROR_CHECK(esp_lcd_dpi_panel_register_event_callbacks(lcd_handle, &mipi_cbs, NULL));
#endif

    int gap_x = 0, gap_y = 0;
#ifdef LCD_GAP_X
    gap_x = LCD_GAP_X;
#endif
#ifdef LCD_GAP_Y
    gap_y = LCD_GAP_Y;
#endif
    esp_lcd_panel_set_gap(lcd_handle,gap_x,gap_y);
    
#ifdef LCD_SWAP_XY
#if LCD_SWAP_XY
    ESP_ERROR_CHECK(esp_lcd_panel_swap_xy(lcd_handle,true));
#endif
#endif
    bool mirror_x = false, mirror_y = false;
#ifdef LCD_MIRROR_X
#if LCD_MIRROR_X
    mirror_x = LCD_MIRROR_X;
#endif
#endif
#ifdef LCD_MIRROR_Y
#if LCD_MIRROR_Y
    mirror_y = LCD_MIRROR_Y;
#endif
#endif
    esp_lcd_panel_mirror(lcd_handle,mirror_x,mirror_y);
    
#ifdef LCD_INVERT_COLOR
    ESP_ERROR_CHECK(esp_lcd_panel_invert_color(lcd_handle,LCD_INVERT_COLOR));
#endif
    esp_lcd_panel_disp_on_off(lcd_handle, true);
#ifdef LCD_PIN_NUM_BCKL
#if LCD_PIN_NUM_BCKL >= 0
#ifdef LCD_BCKL_PWM_CHANNEL
    ledc_set_duty(LEDC_LOW_SPEED_MODE,(ledc_channel_t)LCD_BCKL_PWM_CHANNEL,LCD_BCKL_ON_LEVEL*255);
    ledc_update_duty(LEDC_LOW_SPEED_MODE,(ledc_channel_t)LCD_BCKL_PWM_CHANNEL);
#else
    gpio_set_level((gpio_num_t)LCD_PIN_NUM_BCKL, LCD_BCKL_ON_LEVEL);
#endif
#endif
#endif
#if LCD_TRANSFER_SIZE > 0
    uint32_t heap_caps = MALLOC_CAP_8BIT;
#ifdef LCD_TRANSFER_IN_SPIRAM
    heap_caps |= MALLOC_CAP_SPIRAM;
#else
    heap_caps |= MALLOC_CAP_INTERNAL | MALLOC_CAP_DMA;
#endif
    // it's recommended to allocate the draw buffer from internal memory, for better performance
    draw_buffer = heap_caps_malloc(LCD_TRANSFER_SIZE, heap_caps);
    if(draw_buffer==NULL) {
        ESP_ERROR_CHECK(ESP_ERR_NO_MEM);
    }
#if LCD_SYNC_TRANSFER == 0
    draw_buffer2 = heap_caps_malloc(LCD_TRANSFER_SIZE, heap_caps);
    if(draw_buffer2==NULL) {
        ESP_ERROR_CHECK(ESP_ERR_NO_MEM);
    }
#endif
#endif
}
#if LCD_TRANSFER_SIZE > 0
void* panel_lcd_transfer_buffer(void) { return draw_buffer; }
#if LCD_SYNC_TRANSFER == 0
void* panel_lcd_transfer_buffer2(void) { return draw_buffer2; }
#endif
#endif
#ifdef LCD_PIN_NUM_BCKL
#if LCD_PIN_NUM_BCKL > -1
#ifdef LCD_BCKL_PWM_CHANNEL
void panel_lcd_backlight(uint8_t level) {
#if LCD_BCKL_ON_LEVEL == 0
    ledc_set_duty(LEDC_LOW_SPEED_MODE,(ledc_channel_t)LCD_BCKL_PWM_CHANNEL,255-level);
#else
    ledc_set_duty(LEDC_LOW_SPEED_MODE,(ledc_channel_t)LCD_BCKL_PWM_CHANNEL,level);
#endif
    ledc_update_duty(LEDC_LOW_SPEED_MODE,(ledc_channel_t)LCD_BCKL_PWM_CHANNEL);
}
#else
void panel_lcd_backlight(bool on) {
    gpio_set_level((gpio_num_t)LCD_PIN_NUM_BCKL,on?LCD_BCKL_ON_LEVEL:!LCD_BCKL_ON_LEVEL);
}
#endif
#endif
#endif
#endif
#ifdef TOUCH_BUS
static esp_lcd_panel_io_handle_t touch_io_handle = NULL;
static esp_lcd_touch_handle_t touch_handle = NULL;
#ifdef LCD_TOUCH_PIN_NUM_INT
static void on_touch(esp_lcd_touch_handle_t tp) {
    // TODO: do something here
}
#endif
void panel_touch_init(void) {
    if(touch_handle!=NULL) {
        ESP_LOGW(TAG,"panel_touch_init() was already called");
        return; // already initialized
    }
#if TOUCH_BUS == PANEL_BUS_SPI
    spi_init();
#endif    
#if TOUCH_BUS == PANEL_BUS_I2C
    i2c_init();
#endif
#ifdef TOUCH_RESET
    TOUCH_RESET;
#endif
    esp_lcd_touch_config_t touch_cfg;
    memset(&touch_cfg,0,sizeof(touch_cfg));
    #ifdef TOUCH_DRIVER_DATA
    TOUCH_DRIVER_DATA;
    touch_cfg.driver_data = &driver_data;
#endif
#ifdef TOUCH_PIN_NUM_INT
    touch_cfg.int_gpio_num = (gpio_num_t)TOUCH_PIN_NUM_INT;
#else
    touch_cfg.int_gpio_num = (gpio_num_t)-1;
#endif
#ifdef TOUCH_INT_ON_LEVEL
    touch_cfg.levels.reset = TOUCH_INT_ON_LEVEL;
#else 
    touch_cfg.levels.reset = 1;
#endif
#ifdef TOUCH_PIN_NUM_RST
    touch_cfg.rst_gpio_num = (gpio_num_t)TOUCH_PIN_NUM_RST;
#else
    touch_cfg.rst_gpio_num = (gpio_num_t)-1;
#endif
#ifdef TOUCH_RST_ON_LEVEL
    touch_cfg.levels.reset = TOUCH_RST_ON_LEVEL;
#else
    touch_cfg.levels.reset = 1;
#endif

    touch_cfg.x_max = TOUCH_HRES;
    touch_cfg.y_max = TOUCH_VRES;
#ifdef TOUCH_SWAP_XY
#if TOUCH_SWAP_XY
    touch_cfg.flags.swap_xy = TOUCH_SWAP_XY;
#endif
#endif
#ifdef TOUCH_MIRROR_X
#if TOUCH_MIRROR_X
    touch_cfg.flags.mirror_x = TOUCH_MIRROR_X;
#endif
#endif
#ifdef TOUCH_MIRROR_Y
#if TOUCH_MIRROR_Y
    touch_cfg.flags.mirror_y = TOUCH_MIRROR_X;
#endif
#endif
#if TOUCH_BUS == PANEL_BUS_I2C
    esp_lcd_panel_io_i2c_config_t touch_i2c_cfg;
    touch_i2c_cfg.control_phase_bytes = TOUCH_CONTROL_PHASE_BYTES;
    touch_i2c_cfg.dc_bit_offset = TOUCH_DC_BIT_OFFSET;
    touch_i2c_cfg.dev_addr = TOUCH_I2C_ADDRESS;
#ifdef TOUCH_DISABLE_CONTROL_PHASE
    touch_i2c_cfg.flags.disable_control_phase = true;
#endif
    touch_i2c_cfg.lcd_cmd_bits = TOUCH_CMD_BITS;
    touch_i2c_cfg.lcd_param_bits = TOUCH_PARAM_BITS;
#ifndef LEGACY_I2C
    touch_i2c_cfg.scl_speed_hz = TOUCH_CLOCK_HZ;
#else
    touch_i2c_cfg.scl_speed_hz = 0;
#endif
#ifndef LEGACY_I2C
    i2c_master_bus_handle_t i2c_bus_handle;
    ESP_ERROR_CHECK(i2c_master_get_bus_handle((i2c_port_num_t)TOUCH_I2C_HOST,&i2c_bus_handle));
    ESP_ERROR_CHECK(esp_lcd_new_panel_io_i2c((i2c_master_bus_handle_t)i2c_bus_handle, &touch_i2c_cfg, &touch_io_handle));
#else
    ESP_ERROR_CHECK(esp_lcd_new_panel_io_i2c_v1((uint32_t)TOUCH_I2C_HOST, &touch_i2c_cfg, &touch_io_handle));
#endif
#endif
    ESP_ERROR_CHECK(TOUCH_INIT(touch_io_handle,&touch_cfg,&touch_handle));
}
void panel_touch_read_raw(size_t* in_out_count,uint16_t* out_x,uint16_t* out_y,uint16_t* out_strength) {
    uint8_t count=*in_out_count;
    if(!esp_lcd_touch_get_coordinates(touch_handle,out_x,out_y,out_strength,&count,count)) {
        *in_out_count = 0;
        return;
    }
    *in_out_count = count;
}
void panel_touch_read(size_t* in_out_count,uint16_t* out_x,uint16_t* out_y,uint16_t* out_strength) {
    panel_touch_read_raw(in_out_count,out_x,out_y,out_strength);
#if defined(LCD_BUS) && (TOUCH_WIDTH!=LCD_WIDTH||TOUCH_HEIGHT!=LCD_HEIGHT || TOUCH_LEFT_OVERHANG!=0 ||TOUCH_RIGHT_OVERHANG!=0||TOUCH_TOP_OVERHANG!=0||TOUCH_BOTTOM_OVERHANG!=0)
    for(size_t i = 0;i<*in_out_count;++i) {
        // the panel may have a different res than the screen
        out_x[i]=(out_x[i]*(TOUCH_WIDTH-TOUCH_LEFT_OVERHANG-TOUCH_RIGHT_OVERHANG))/(LCD_WIDTH);
        out_y[i]=(out_y[i]*(TOUCH_HEIGHT-TOUCH_TOP_OVERHANG-TOUCH_BOTTOM_OVERHANG))/LCD_HEIGHT;
    }
#endif
}
void panel_touch_update(void) {
#if defined(TOUCH_UPDATE_LIMIT_MS) && TOUCH_UPDATE_LIMIT_TS > 0
    static TickType_t limit_ts = 0;
    TickType_t ticks = xTaskGetTickCount();
    if(ticks>limit_ts+pdMS_TO_TICKS(TOUCH_UPDATE_LIMIT_MS)) {
        limit_ts = ticks;
#endif
    esp_lcd_touch_read_data(touch_handle);
#if defined(TOUCH_UPDATE_LIMIT_MS) && TOUCH_UPDATE_LIMIT_TS > 0
    }
#endif
}
#endif

#ifdef BUTTON
void panel_button_init(void) {
    gpio_config_t cfg;
    memset(&cfg,0,sizeof(cfg));
    cfg.mode = GPIO_MODE_INPUT;
    cfg.pin_bit_mask = BUTTON_MASK;
#if BUTTON_ON_LEVEL == 0
    cfg.pull_down_en = 0;
    cfg.pull_up_en = 1;
#else
    cfg.pull_down_en = 1;
    cfg.pull_up_en = 0;
#endif
    ESP_ERROR_CHECK(gpio_config(&cfg));
}
bool panel_button_read(uint8_t pin) {
#if BUTTON_ON_LEVEL == 1
    return gpio_get_level((gpio_num_t)pin);
#else
    return !gpio_get_level((gpio_num_t)pin);
#endif
}
uint64_t panel_button_read_all(void) {
    uint64_t result = 0;
    for(int i = 0;i<GPIO_NUM_MAX;++i) {
        uint64_t mask = ((uint64_t)1)<<i;
        if(0!=(mask & (BUTTON_MASK))) {
            if(panel_button_read(i)) {
                result |= mask;
            }
        }
    }
    return result;
}
#endif
#ifdef POWER
void panel_power_init(void) {
#if defined(POWER_BUS) && (POWER_BUS == PANEL_BUS_SPI)
    spi_init();
#endif
#if defined(POWER_BUS) && (POWER_BUS == PANEL_BUS_I2C)
    i2c_init();
#endif
#ifdef POWER_INIT
    POWER_INIT;
#endif
}
#endif
#ifdef SD_BUS
static sdmmc_card_t* sd_card_handle = NULL;
sdmmc_card_t* panel_sd_handle() {
    return sd_card_handle;
}
void panel_sd_end() {
    if(sd_card_handle==NULL) {
        return;
    }
    ESP_ERROR_CHECK(esp_vfs_fat_sdcard_unmount(SD_MOUNT_POINT,sd_card_handle));
    sd_card_handle = NULL;
}
bool panel_sd_init(bool format_on_fail, size_t max_files, size_t alloc_unit_size) {
    if(sd_card_handle!=NULL) {
        panel_sd_end();
    }
    if(max_files==0) {
        max_files = 5;
    }
    esp_vfs_fat_sdmmc_mount_config_t mount_config = {
        .format_if_mount_failed = format_on_fail,
        .max_files = (int)max_files,
        .allocation_unit_size = alloc_unit_size
    };
#if SD_BUS == PANEL_BUS_SPI
    spi_init();
    sdmmc_host_t host = SDSPI_HOST_DEFAULT();
    host.slot = SD_SPI_HOST;
#ifdef SD_CLOCK_HZ
    host.max_freq_khz = ((SD_CLOCK_HZ)/1000);
#endif
    sdspi_device_config_t slot_config;
    memset(&slot_config, 0, sizeof(slot_config));
    slot_config.host_id = (spi_host_device_t)SD_SPI_HOST;
#ifdef SD_PIN_NUM_CS
    slot_config.gpio_cs = (gpio_num_t)SD_PIN_NUM_CS;
#else
    slot_config.gpio_cs = (gpio_num_t)-1;
    
#endif
#ifdef SD_PIN_NUM_WP
    slot_config.gpio_wp = (gpio_num_t)SD_PIN_NUM_WP;
#else
    slot_config.gpio_wp = GPIO_NUM_NC;
#endif
#if defined(SD_WP_ON_LEVEL) && SD_WP_ON_LEVEL == 0 
    slot_config.gpio_wp_polarity = 1;
#endif
#ifdef SD_PIN_NUM_CD
    slot_config.gpio_cd = (gpio_num_t)SD_PIN_NUM_CD;
#else
    slot_config.gpio_cd = GPIO_NUM_NC;
#endif
#ifdef SD_PIN_NUM_INT
    slot_config.gpio_int = (gpio_num_t)SD_PIN_NUM_INT;
#else
    slot_config.gpio_int = GPIO_NUM_NC;
#endif
 if (ESP_OK != esp_vfs_fat_sdspi_mount(SD_MOUNT_POINT, &host, &slot_config,
                                          &mount_config, &sd_card_handle)) {
        return false;
}
return true;

    
#endif

#if SD_BUS == PANEL_BUS_MMC
    sdmmc_host_t host = SDMMC_HOST_DEFAULT();
    host.flags = SDMMC_HOST_FLAG_1BIT; //use 1-line SD/MMC mode
#ifdef SD_CLOCK_HZ
    host.max_freq_khz = ((SD_CLOCK_HZ)/1000);
#endif
#ifdef SD_MMC_HOST
    host.slot = SD_MMC_HOST;
#endif
    sdmmc_slot_config_t slot_config = SDMMC_SLOT_CONFIG_DEFAULT();
    slot_config.clk = SD_PIN_NUM_CLK;
    slot_config.cmd = SD_PIN_NUM_CMD;
#ifdef SD_PIN_NUM_WP
    slot_config.wp = SD_PIN_NUM_WP;
#else
    slot_config.wp = -1;
#endif
#ifdef SD_PIN_NUM_CD
    slot_config.cd = SD_PIN_NUM_CD;
#else
    slot_config.cd = -1;
#endif
    slot_config.d0 = SD_PIN_NUM_D00;
    slot_config.width = 1;
#ifdef SD_PIN_NUM_D07
    slot_config.d1 = SD_PIN_NUM_D01;
    slot_config.d2 = SD_PIN_NUM_D02;
    slot_config.d3 = SD_PIN_NUM_D03;
    slot_config.d4 = SD_PIN_NUM_D04;
    slot_config.d5 = SD_PIN_NUM_D05;
    slot_config.d6 = SD_PIN_NUM_D06;
    slot_config.d7 = SD_PIN_NUM_D07;
    slot_config.width = 8;
    host.flags = SDMMC_HOST_FLAG_8BIT; //use 4-line SD/MMC mode
#elif defined(SD_PIN_NUM_D03)
    slot_config.d1 = SD_PIN_NUM_D01;
    slot_config.d2 = SD_PIN_NUM_D02;
    slot_config.d3 = SD_PIN_NUM_D03;
    slot_config.width = 4;
    host.flags = SDMMC_HOST_FLAG_4BIT; //use 4-line SD/MMC mode
#endif
#if defined(SD_NO_DDR) 
    host.flags &= ~SDMMC_HOST_FLAG_DDR;
#endif
    // assuming the board is built correctly, we don't need this:
#if defined(SD_PULLUP) && SD_PULLUP
    slot_config.flags |= SDMMC_SLOT_FLAG_INTERNAL_PULLUP;
#endif
    esp_err_t ret = esp_vfs_fat_sdmmc_mount(SD_MOUNT_POINT, &host, &slot_config, &mount_config, &sd_card_handle);
    if(ret!=ESP_OK) {
        return false;
    }
    return true;
#endif
}
#endif

#ifdef EXPANDER
static esp_io_expander_handle_t expander_handle = NULL;
void panel_expander_init(void) {
    if(expander_handle!=NULL) {
        ESP_LOGW(TAG,"panel_expander_init() was already called");
        return; // already initialized
    }
#if defined(EXPANDER_BUS) && (EXPANDER_BUS == EXPANDER_BUS_SPI)
    spi_init();
#endif
#if defined(POWER_BUS) && (EXPANDER_BUS == EXPANDER_BUS_I2C)
    i2c_init();
#endif
    ESP_ERROR_CHECK(EXPANDER_INIT((i2c_port_num_t)EXPANDER_I2C_HOST,EXPANDER_I2C_ADDRESS,&expander_handle));
}
void panel_expander_handle(void) {
    return expander_handle;
}
#endif