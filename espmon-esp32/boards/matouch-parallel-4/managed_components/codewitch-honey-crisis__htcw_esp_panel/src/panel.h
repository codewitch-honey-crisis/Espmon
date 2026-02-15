#ifndef PANEL_H
#define PANEL_H
#include <driver/spi_master.h>
#ifdef LEGACY_I2C
    #include <driver/i2c.h>
#else
    #include <driver/i2c_master.h>
#endif
#include "panel_maxes.h"
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#ifdef SD_BUS
#include <esp_vfs_fat.h>
#include <sdmmc_cmd.h>
#include <driver/sdmmc_host.h>
#endif

#ifdef __cplusplus
extern "C" {
#endif

#ifdef LCD_BUS
/// @brief Initializes the display
void panel_lcd_init(void);
/// @brief Sends a bitmap to the display
/// @param x1 The x1 coord
/// @param y1 The y1 coord
/// @param x2 The x2 coord
/// @param y2 The y2 coord
/// @param bitmap The bitmap data
void panel_lcd_flush(uint16_t x1, uint16_t y1, uint16_t x2, uint16_t y2, void* bitmap);
#if LCD_TRANSFER_SIZE > 0
/// @brief Returns the transfer buffer 
/// @return A pointer to the transfer buffer, of size LCD_TRANSFER_SIZE
void* panel_lcd_transfer_buffer(void);
#if LCD_SYNC_TRANSFER == 0
/// @brief Returns the secondary transfer buffer 
/// @return A pointer to the secondary transfer buffer, of size LCD_TRANSFER_SIZE
void* panel_lcd_transfer_buffer2(void);
#endif
#ifdef LCD_PIN_NUM_BCKL
#if LCD_PIN_NUM_BCKL > -1
#ifdef LCD_BCKL_PWM_CHANNEL
void panel_lcd_backlight(uint8_t level);
#else
void panel_lcd_backlight(bool on);
#endif
#endif
#endif
#endif
#if LCD_SYNC_TRANSFER == 0
/// @brief Called when a transfer is complete. To be implemented by the panel_lcd_init() caller
void panel_lcd_flush_complete(void);
#endif
/// @brief Indicates how many flushes have occurred since the vblanking period was active
/// @return The number of draws that have occurred, or 0 if not supported
size_t panel_lcd_vsync_flush_count(void);
#endif
#ifdef TOUCH_BUS
/// @brief Initializes the touch panel
void panel_touch_init(void);
/// @brief Updated the touch panel data
void panel_touch_update(void);
/// @brief Reads the touch scaled to the LCD (if present, otherwise, same as touch_read_raw)
/// @param in_out_count The touch count
/// @param out_x The x array
/// @param out_y The y array
/// @param out_strength The strength array
void panel_touch_read(size_t* in_out_count,uint16_t* out_x,uint16_t* out_y, uint16_t* out_strength);
/// @brief Reads the touch unscaled
/// @param in_out_count The touch count
/// @param out_x The x array
/// @param out_y The y array
/// @param out_strength The strength array
void panel_touch_read_raw(size_t* in_out_count,uint16_t* out_x,uint16_t* out_y, uint16_t* out_strength);
#endif
#ifdef BUTTON
/// @brief Initializes the buttons
void panel_button_init(void);
/// @brief Indicates which buttons are pressed
/// @param pin The pin to read
/// @return True if pressed, otherwise false
bool panel_button_read(uint8_t pin);
/// @brief Reads all the buttons and returns a mask
/// @return A mask indicating the pins of the pressed buttons
uint64_t panel_button_read_all(void);
#endif
#ifdef POWER
/// @brief Initializes the power subsystem
void panel_power_init(void);
#endif
#ifdef SD_BUS
/// @brief Returns the handle for the mounted SD card
/// @return The handle, or NULL of no SD is mounted
sdmmc_card_t* panel_sd_handle();
/// @brief Unmounts any mounted SD card
void panel_sd_end();
/// @brief Mounts the SD card
/// @param format_on_fail True to format if the mount fails, otherwise false
/// @param max_files The max number of open files, or 0 for the default
/// @param alloc_unit_size The allocation unit size, or 0 for the default
/// @return True if successfully mounted, otherwise false
bool panel_sd_init(bool format_on_fail, size_t max_files, size_t alloc_unit_size);
#endif
#ifdef EXPANDER
/// @brief Initializes the I/O expander
void panel_expander_init(void);
/// @brief Returns the handle for the I/O expander 
/// @return The I/O expander handle
void* panel_expander_handle(void);
#endif
#ifdef __cplusplus
}
#endif
#endif // PANEL_H