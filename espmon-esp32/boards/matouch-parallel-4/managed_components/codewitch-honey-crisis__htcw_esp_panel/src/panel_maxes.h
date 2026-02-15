#ifndef PANEL_MAXES_H
#define PANEL_MAXES_H
#include "panel_pins.h"

// ============================================================================
// SPI TRANSFER SIZE CONSOLIDATION
// ============================================================================
// Find maximum transfer size for each SPI host and cap at (32*1024-8)

#define SPI_MAX_TRANSFER_SIZE (32*1024-8)

// ------------- SPI_1 transfer size -------------
#if defined(SPI_1_HOST_USED)
    // Collect all transfer sizes for devices on SPI_1
    #define SPI_1_XFER_LCD 0
    #define SPI_1_XFER_TOUCH 0
    #define SPI_1_XFER_POWER 0
    #define SPI_1_XFER_SD 0
    #define SPI_1_XFER_EXPANDER 0
    
    #if defined(LCD_SPI_HOST) && (LCD_SPI_HOST == SPI_1) && defined(LCD_TRANSFER_SIZE)
        #undef SPI_1_XFER_LCD
        #define SPI_1_XFER_LCD LCD_TRANSFER_SIZE
    #endif
    
    #if defined(TOUCH_SPI_HOST) && (TOUCH_SPI_HOST == SPI_1) && defined(TOUCH_TRANSFER_SIZE)
        #undef SPI_1_XFER_TOUCH
        #define SPI_1_XFER_TOUCH TOUCH_TRANSFER_SIZE
    #endif
    
    #if defined(POWER_SPI_HOST) && (POWER_SPI_HOST == SPI_1) && defined(POWER_TRANSFER_SIZE)
        #undef SPI_1_XFER_POWER
        #define SPI_1_XFER_POWER POWER_TRANSFER_SIZE
    #endif
    
    #if defined(SD_SPI_HOST) && (SD_SPI_HOST == SPI_1) && defined(SD_TRANSFER_SIZE)
        #undef SPI_1_XFER_SD
        #define SPI_1_XFER_SD SD_TRANSFER_SIZE
    #endif
    
    #if defined(EXPANDER_SPI_HOST) && (EXPANDER_SPI_HOST == SPI_1) && defined(EXPANDER_TRANSFER_SIZE)
        #undef SPI_1_XFER_EXPANDER
        #define SPI_1_XFER_EXPANDER EXPANDER_TRANSFER_SIZE
    #endif
    
   // Find maximum using nested conditionals
    #if (SPI_1_XFER_LCD >= SPI_1_XFER_TOUCH) && (SPI_1_XFER_LCD >= SPI_1_XFER_POWER) && (SPI_1_XFER_LCD >= SPI_1_XFER_SD) && (SPI_1_XFER_LCD >= SPI_1_XFER_EXPANDER)
        #define SPI_1_TRANSFER_SIZE_RAW SPI_1_XFER_LCD
    #elif (SPI_1_XFER_TOUCH >= SPI_1_XFER_POWER) && (SPI_1_XFER_TOUCH >= SPI_1_XFER_SD) && (SPI_1_XFER_TOUCH >= SPI_1_XFER_EXPANDER)
        #define SPI_1_TRANSFER_SIZE_RAW SPI_1_XFER_TOUCH
    #elif (SPI_1_XFER_POWER >= SPI_1_XFER_SD) && (SPI_1_XFER_POWER >= SPI_1_XFER_EXPANDER)
        #define SPI_1_TRANSFER_SIZE_RAW SPI_1_XFER_POWER
    #elif (SPI_1_XFER_SD >= SPI_1_XFER_EXPANDER)
        #define SPI_1_TRANSFER_SIZE_RAW SPI_1_XFER_SD
    #else
        #define SPI_1_TRANSFER_SIZE_RAW SPI_1_XFER_EXPANDER
    #endif
    
    // Check if exceeds maximum and warn
    #if SPI_1_TRANSFER_SIZE_RAW > SPI_MAX_TRANSFER_SIZE
        #warning "SPI_1 transfer size exceeds maximum (32KB-8), capping at maximum"
        #define SPI_1_TRANSFER_SIZE SPI_MAX_TRANSFER_SIZE
    #else
        #define SPI_1_TRANSFER_SIZE SPI_1_TRANSFER_SIZE_RAW
    #endif
    
    // Cleanup temp defines
    #undef SPI_1_XFER_LCD
    #undef SPI_1_XFER_TOUCH
    #undef SPI_1_XFER_POWER
    #undef SPI_1_XFER_SD
    #undef SPI_1_XFER_EXPANDER
    #undef SPI_1_TRANSFER_SIZE_RAW
#endif

// ------------- SPI_2 transfer size -------------
#if defined(SPI_2_HOST_USED)
    // Collect all transfer sizes for devices on SPI_2
    #define SPI_2_XFER_LCD 0
    #define SPI_2_XFER_TOUCH 0
    #define SPI_2_XFER_POWER 0
    #define SPI_2_XFER_SD 0
    #define SPI_2_XFER_EXPANDER 0

    #if defined(LCD_SPI_HOST) && (LCD_SPI_HOST == SPI_2) && defined(LCD_TRANSFER_SIZE)
        #undef SPI_2_XFER_LCD
        #define SPI_2_XFER_LCD LCD_TRANSFER_SIZE
    #endif
    
    #if defined(TOUCH_SPI_HOST) && (TOUCH_SPI_HOST == SPI_2) && defined(TOUCH_TRANSFER_SIZE)
        #undef SPI_2_XFER_TOUCH
        #define SPI_2_XFER_TOUCH TOUCH_TRANSFER_SIZE
    #endif
    
    #if defined(POWER_SPI_HOST) && (POWER_SPI_HOST == SPI_2) && defined(POWER_TRANSFER_SIZE)
        #undef SPI_2_XFER_POWER
        #define SPI_2_XFER_POWER POWER_TRANSFER_SIZE
    #endif
    
    #if defined(SD_SPI_HOST) && (SD_SPI_HOST == SPI_2) && defined(SD_TRANSFER_SIZE)
        #undef SPI_2_XFER_SD
        #define SPI_2_XFER_SD SD_TRANSFER_SIZE
    #endif

    #if defined(EXPANDER_SPI_HOST) && (EXPANDER_SPI_HOST == SPI_2) && defined(EXPANDER_TRANSFER_SIZE)
        #undef SPI_2_XFER_EXPANDER
        #define SPI_2_XFER_EXPANDER EXPANDER_TRANSFER_SIZE
    #endif
        
    // Find maximum using nested conditionals
    #if (SPI_2_XFER_LCD >= SPI_2_XFER_TOUCH) && (SPI_2_XFER_LCD >= SPI_2_XFER_POWER) && (SPI_2_XFER_LCD >= SPI_2_XFER_SD) && (SPI_2_XFER_LCD >= SPI_2_XFER_EXPANDER)
        #define SPI_2_TRANSFER_SIZE_RAW SPI_2_XFER_LCD
    #elif (SPI_2_XFER_TOUCH >= SPI_2_XFER_POWER) && (SPI_2_XFER_TOUCH >= SPI_2_XFER_SD) && (SPI_2_XFER_TOUCH >= SPI_2_XFER_EXPANDER)
        #define SPI_2_TRANSFER_SIZE_RAW SPI_2_XFER_TOUCH
    #elif (SPI_2_XFER_POWER >= SPI_2_XFER_SD) && (SPI_2_XFER_POWER >= SPI_2_XFER_EXPANDER)
        #define SPI_2_TRANSFER_SIZE_RAW SPI_2_XFER_POWER
    #elif (SPI_2_XFER_SD >= SPI_2_XFER_EXPANDER)
        #define SPI_2_TRANSFER_SIZE_RAW SPI_2_XFER_SD
    #else
        #define SPI_2_TRANSFER_SIZE_RAW SPI_2_XFER_EXPANDER
    #endif
    
    // Check if exceeds maximum and warn
    #if SPI_2_TRANSFER_SIZE_RAW > SPI_MAX_TRANSFER_SIZE
        #warning "SPI_2 transfer size exceeds maximum (32KB-8), capping at maximum"
        #define SPI_2_TRANSFER_SIZE SPI_MAX_TRANSFER_SIZE
    #else
        #define SPI_2_TRANSFER_SIZE SPI_2_TRANSFER_SIZE_RAW
    #endif
    
    // Cleanup temp defines
    #undef SPI_2_XFER_LCD
    #undef SPI_2_XFER_TOUCH
    #undef SPI_2_XFER_POWER
    #undef SPI_2_XFER_SD
    #undef SPI_2_XFER_EXPANDER
    #undef SPI_2_TRANSFER_SIZE_RAW
#endif

// ------------- SPI_3 transfer size -------------
#if defined(SPI_3_HOST_USED)
    // Collect all transfer sizes for devices on SPI_3
    #define SPI_3_XFER_LCD 0
    #define SPI_3_XFER_TOUCH 0
    #define SPI_3_XFER_POWER 0
    #define SPI_3_XFER_SD 0
    #define SPI_3_XFER_EXPANDER 0
    
    #if defined(LCD_SPI_HOST) && (LCD_SPI_HOST == SPI_3) && defined(LCD_TRANSFER_SIZE)
        #undef SPI_3_XFER_LCD
        #define SPI_3_XFER_LCD LCD_TRANSFER_SIZE
    #endif
    
    #if defined(TOUCH_SPI_HOST) && (TOUCH_SPI_HOST == SPI_3) && defined(TOUCH_TRANSFER_SIZE)
        #undef SPI_3_XFER_TOUCH
        #define SPI_3_XFER_TOUCH TOUCH_TRANSFER_SIZE
    #endif
    
    #if defined(POWER_SPI_HOST) && (POWER_SPI_HOST == SPI_3) && defined(POWER_TRANSFER_SIZE)
        #undef SPI_3_XFER_POWER
        #define SPI_3_XFER_POWER POWER_TRANSFER_SIZE
    #endif
    
    #if defined(SD_SPI_HOST) && (SD_SPI_HOST == SPI_3) && defined(SD_TRANSFER_SIZE)
        #undef SPI_3_XFER_SD
        #define SPI_3_XFER_SD SD_TRANSFER_SIZE
    #endif
    
    #if defined(EXPANDER_SPI_HOST) && (EXPANDER_SPI_HOST == SPI_3) && defined(EXPANDER_TRANSFER_SIZE)
        #undef SPI_3_XFER_EXPANDER
        #define SPI_3_XFER_EXPANDER EXPANDER_TRANSFER_SIZE
    #endif
    
    // Find maximum using nested conditionals
    #if (SPI_3_XFER_LCD >= SPI_3_XFER_TOUCH) && (SPI_3_XFER_LCD >= SPI_3_XFER_POWER) && (SPI_3_XFER_LCD >= SPI_3_XFER_SD) && (SPI_3_XFER_LCD >= SPI_3_XFER_EXPANDER)
        #define SPI_3_TRANSFER_SIZE_RAW SPI_3_XFER_LCD
    #elif (SPI_3_XFER_TOUCH >= SPI_3_XFER_POWER) && (SPI_3_XFER_TOUCH >= SPI_3_XFER_SD) && (SPI_3_XFER_TOUCH >= SPI_3_XFER_EXPANDER)
        #define SPI_3_TRANSFER_SIZE_RAW SPI_3_XFER_TOUCH
    #elif (SPI_3_XFER_POWER >= SPI_3_XFER_SD) && (SPI_3_XFER_POWER >= SPI_3_XFER_EXPANDER)
        #define SPI_3_TRANSFER_SIZE_RAW SPI_3_XFER_POWER
    #elif (SPI_3_XFER_SD >= SPI_3_XFER_EXPANDER)
        #define SPI_3_TRANSFER_SIZE_RAW SPI_3_XFER_SD
    #else
        #define SPI_3_TRANSFER_SIZE_RAW SPI_3_XFER_EXPANDER
    #endif
    
    // Check if exceeds maximum and warn
    #if SPI_3_TRANSFER_SIZE_RAW > SPI_MAX_TRANSFER_SIZE
        #warning "SPI_3 transfer size exceeds maximum (32KB-8), capping at maximum"
        #define SPI_3_TRANSFER_SIZE SPI_MAX_TRANSFER_SIZE
    #else
        #define SPI_3_TRANSFER_SIZE SPI_3_TRANSFER_SIZE_RAW
    #endif
    
    // Cleanup temp defines
    #undef SPI_3_XFER_LCD
    #undef SPI_3_XFER_TOUCH
    #undef SPI_3_XFER_POWER
    #undef SPI_3_XFER_SD
    #undef SPI_3_XFER_EXPANDER
    #undef SPI_3_TRANSFER_SIZE_RAW
#endif

// ------------- SPI_4 transfer size -------------
#if defined(SPI_4_HOST_USED)
    // Collect all transfer sizes for devices on SPI_4
    #define SPI_4_XFER_LCD 0
    #define SPI_4_XFER_TOUCH 0
    #define SPI_4_XFER_POWER 0
    #define SPI_4_XFER_SD 0
    #define SPI_4_XFER_EXPANDER 0

    #if defined(LCD_SPI_HOST) && (LCD_SPI_HOST == SPI_4) && defined(LCD_TRANSFER_SIZE)
        #undef SPI_4_XFER_LCD
        #define SPI_4_XFER_LCD LCD_TRANSFER_SIZE
    #endif
    
    #if defined(TOUCH_SPI_HOST) && (TOUCH_SPI_HOST == SPI_4) && defined(TOUCH_TRANSFER_SIZE)
        #undef SPI_4_XFER_TOUCH
        #define SPI_4_XFER_TOUCH TOUCH_TRANSFER_SIZE
    #endif
    
    #if defined(POWER_SPI_HOST) && (POWER_SPI_HOST == SPI_4) && defined(POWER_TRANSFER_SIZE)
        #undef SPI_4_XFER_POWER
        #define SPI_4_XFER_POWER POWER_TRANSFER_SIZE
    #endif
    
    #if defined(SD_SPI_HOST) && (SD_SPI_HOST == SPI_4) && defined(SD_TRANSFER_SIZE)
        #undef SPI_4_XFER_SD
        #define SPI_4_XFER_SD SD_TRANSFER_SIZE
    #endif
    
    #if defined(EXPANDER_SPI_HOST) && (EXPANDER_SPI_HOST == SPI_4) && defined(EXPANDER_TRANSFER_SIZE)
        #undef SPI_4_XFER_EXPANDER
        #define SPI_4_XFER_EXPANDER EXPANDER_TRANSFER_SIZE
    #endif
    
    // Find maximum using nested conditionals
    #if (SPI_4_XFER_LCD >= SPI_4_XFER_TOUCH) && (SPI_4_XFER_LCD >= SPI_4_XFER_POWER) && (SPI_4_XFER_LCD >= SPI_4_XFER_SD) && (SPI_4_XFER_LCD >= SPI_4_XFER_EXPANDER)
        #define SPI_4_TRANSFER_SIZE_RAW SPI_4_XFER_LCD
    #elif (SPI_4_XFER_TOUCH >= SPI_4_XFER_POWER) && (SPI_4_XFER_TOUCH >= SPI_4_XFER_SD) && (SPI_4_XFER_TOUCH >= SPI_4_XFER_EXPANDER)
        #define SPI_4_TRANSFER_SIZE_RAW SPI_4_XFER_TOUCH
    #elif (SPI_4_XFER_POWER >= SPI_4_XFER_SD) && (SPI_4_XFER_POWER >= SPI_4_XFER_EXPANDER)
        #define SPI_4_TRANSFER_SIZE_RAW SPI_4_XFER_POWER
    #elif (SPI_4_XFER_SD >= SPI_4_XFER_EXPANDER)
        #define SPI_4_TRANSFER_SIZE_RAW SPI_4_XFER_SD
    #else
        #define SPI_4_TRANSFER_SIZE_RAW SPI_4_XFER_EXPANDER
    #endif
    
    // Check if exceeds maximum and warn
    #if SPI_4_TRANSFER_SIZE_RAW > SPI_MAX_TRANSFER_SIZE
        #warning "SPI_4 transfer size exceeds maximum (32KB-8), capping at maximum"
        #define SPI_4_TRANSFER_SIZE SPI_MAX_TRANSFER_SIZE
    #else
        #define SPI_4_TRANSFER_SIZE SPI_4_TRANSFER_SIZE_RAW
    #endif
    
    // Cleanup temp defines
    #undef SPI_4_XFER_LCD
    #undef SPI_4_XFER_TOUCH
    #undef SPI_4_XFER_POWER
    #undef SPI_4_XFER_SD
    #undef SPI_4_XFER_EXPANDER
    #undef SPI_4_TRANSFER_SIZE_RAW
#endif
#ifdef I2C_1_HOST_USED
    // Collect all clock speeds for devices on I2C_1_HOST (for legacy i2c support)
    #define I2C_1_SPEED_LCD (800*1000)
    #define I2C_1_SPEED_TOUCH (800*1000)
    #define I2C_1_SPEED_POWER (800*1000)
    #define I2C_1_SPEED_EXPANDER (800*1000)

    #if defined(LCD_I2C_HOST) && (LCD_I2C_HOST == I2C_1) && defined(LCD_CLOCK_HZ)
        #undef I2C_1_SPEED_LCD
        #define I2C_1_SPEED_LCD LCD_CLOCK_HZ
    #endif
    
    #if defined(TOUCH_I2C_HOST) && (TOUCH_I2C_HOST == I2C_1) && defined(TOUCH_CLOCK_HZ)
        #undef I2C_1_SPEED_TOUCH
        #define I2C_1_SPEED_TOUCH TOUCH_CLOCK_HZ
    #endif
    
    #if defined(POWER_I2C_HOST) && (POWER_I2C_HOST == I2C_1) && defined(POWER_CLOCK_HZ)
        #undef I2C_1_SPEED_POWER
        #define I2C_1_SPEED_POWER POWER_CLOCK_HZ
    #endif
    
    #if defined(EXPANDER_I2C_HOST) && (EXPANDER_I2C_HOST == I2C_1) && defined(EXPANDER_CLOCK_HZ)
        #undef I2C_1_SPEED_EXPANDER
        #define I2C_1_SPEED_EXPANDER EXPANDER_CLOCK_HZ
    #endif
    
   // Find minimum using nested conditionals
    #if (I2C_1_SPEED_LCD <= I2C_1_SPEED_TOUCH) && (I2C_1_SPEED_LCD <= I2C_1_SPEED_POWER) && (I2C_1_SPEED_LCD <= I2C_1_SPEED_EXPANDER)
        #define I2C_1_CLOCK_HZ I2C_1_SPEED_LCD
    #elif (I2C_1_SPEED_TOUCH <= I2C_1_SPEED_POWER) && (I2C_1_SPEED_TOUCH <= I2C_1_SPEED_EXPANDER)
        #define I2C_1_CLOCK_HZ I2C_1_SPEED_TOUCH
    #elif (I2C_1_SPEED_POWER <= I2C_1_SPEED_EXPANDER)
        #define I2C_1_CLOCK_HZ I2C_1_SPEED_POWER
    #else
        #define I2C_1_CLOCK_HZ I2C_1_SPEED_EXPANDER
    #endif

    // Cleanup temp defines
    #undef I2C_1_SPEED_LCD
    #undef I2C_1_SPEED_TOUCH
    #undef I2C_1_SPEED_POWER
    #undef I2C_1_SPEED_EXPANDER
#endif

#ifdef I2C_2_HOST_USED
    // Collect all clock speeds for devices on I2C_2_HOST (for legacy i2c support)
    #define I2C_2_SPEED_LCD (800*1000)
    #define I2C_2_SPEED_TOUCH (800*1000)
    #define I2C_2_SPEED_POWER (800*1000)
    #define I2C_2_SPEED_EXPANDER (800*1000)
    
    #if defined(LCD_I2C_HOST) && (LCD_I2C_HOST == I2C_2) && defined(LCD_CLOCK_HZ)
        #undef I2C_2_SPEED_LCD
        #define I2C_2_SPEED_LCD LCD_CLOCK_HZ
    #endif
    
    #if defined(TOUCH_I2C_HOST) && (TOUCH_I2C_HOST == I2C_2) && defined(TOUCH_CLOCK_HZ)
        #undef I2C_2_SPEED_TOUCH
        #define I2C_2_SPEED_TOUCH TOUCH_CLOCK_HZ
    #endif
    
    #if defined(POWER_I2C_HOST) && (POWER_I2C_HOST == I2C_2) && defined(POWER_CLOCK_HZ)
        #undef I2C_2_SPEED_POWER
        #define I2C_2_SPEED_POWER POWER_CLOCK_HZ
    #endif
    
    // Find minimum using nested conditionals
    #if (I2C_2_SPEED_LCD <= I2C_2_SPEED_TOUCH) && (I2C_2_SPEED_LCD <= I2C_2_SPEED_POWER) && (I2C_2_SPEED_LCD <= I2C_2_SPEED_EXPANDER)
        #define I2C_2_CLOCK_HZ I2C_2_SPEED_LCD
    #elif (I2C_2_SPEED_TOUCH <= I2C_2_SPEED_POWER) && (I2C_2_SPEED_TOUCH <= I2C_2_SPEED_EXPANDER)
        #define I2C_2_CLOCK_HZ I2C_2_SPEED_TOUCH
    #elif (I2C_2_SPEED_POWER <= I2C_2_SPEED_EXPANDER)
        #define I2C_2_CLOCK_HZ I2C_2_SPEED_POWER
    #else
        #define I2C_2_CLOCK_HZ I2C_2_SPEED_EXPANDER
    #endif
    
    // Cleanup temp defines
    #undef I2C_2_SPEED_LCD
    #undef I2C_2_SPEED_TOUCH
    #undef I2C_2_SPEED_POWER
    #undef I2C_2_SPEED_EXPANDER
#endif
#endif // PANEL_MAXES_H