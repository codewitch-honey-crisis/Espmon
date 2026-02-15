#ifndef PANEL_DEFS_H
#define PANEL_DEFS_H
#define SPI_1 0
#define SPI_2 1
#define SPI_3 2
#define SPI_4 3
#define I2C_1 0
#define I2C_2 1
#define MMC_1 0
#define MMC_2 1

#define LCD_COLOR_RGB 1
#define LCD_COLOR_BGR 2
#define LCD_COLOR_GSC 3
#define PANEL_BUS_SPI 1
#define PANEL_BUS_I2C 2
#define PANEL_BUS_I8080 3
#define PANEL_BUS_RGB 4
#define PANEL_BUS_MIPI 5
#define PANEL_BUS_MMC 6

#include "panels.h"
#if __has_include("custom_panel.h")
#include "custom_panel.h"
#endif

// BEGIN fill ins for defaults (don't mess!)

#define BUTTON_PIN(x) ((uint64_t)((((uint64_t)1)<<((uint64_t)x))))
#ifdef BUTTON_MASK
#ifndef BUTTON
#define BUTTON
#endif
#ifndef BUTTON_ON_LEVEL
#define BUTTON_ON_LEVEL 1
#endif
#ifndef BUTTON_OFF_LEVEL
#define BUTTON_OFF_LEVEL (!BUTTON_ON_LEVEL)
#endif
#endif

#ifndef LCD_BUS
#if defined(LCD_SPI_HOST)
#define LCD_BUS PANEL_BUS_SPI
#else
#if defined(LCD_HSYNC_FRONT_PORCH) && defined(LCD_PIN_NUM_D00)
#define LCD_BUS PANEL_BUS_RGB
#elif defined(LCD_HSYNC_FRONT_PORCH) 
#define LCD_BUS PANEL_BUS_MIPI
#elif defined(LCD_PIN_NUM_D00)
#define LCD_BUS PANEL_BUS_I8080
#elif defined(LCD_I2C_HOST)
#define LCD_BUS PANEL_BUS_I2C
#endif
#endif
#endif

#ifdef LCD_BUS
#ifndef LCD_DISABLE_CONTROL_PHASE
#define LCD_DISABLE_CONTROL_PHASE 0
#endif 
#ifndef LCD_WIDTH
#ifdef LCD_SWAP_XY
#if LCD_SWAP_XY
#define LCD_WIDTH LCD_VRES
#define LCD_HEIGHT LCD_HRES
#else
#define LCD_WIDTH LCD_HRES
#define LCD_HEIGHT LCD_VRES
#endif
#else
#define LCD_WIDTH LCD_HRES
#define LCD_HEIGHT LCD_VRES
#endif
#endif
#ifndef LCD_BIT_DEPTH
#define LCD_BIT_DEPTH 16
#endif
#ifndef LCD_COLOR_SPACE
#if LCD_BIT_DEPTH <= 8
#define LCD_COLOR_SPACE LCD_COLOR_GSC
#else
#define LCD_COLOR_SPACE LCD_COLOR_RGB
#endif
#endif
#ifndef LCD_X_ALIGN
#define LCD_X_ALIGN 1
#endif
#ifndef LCD_Y_ALIGN
#define LCD_Y_ALIGN 1
#endif
#ifndef LCD_DC_BIT_OFFSET
#define LCD_DC_BIT_OFFSET 0
#endif
#ifndef LCD_CONTROL_PHASE_BYTES
#define LCD_CONTROL_PHASE_BYTES 1
#endif
#ifndef LCD_CMD_BITS
#define LCD_CMD_BITS 8
#endif
#ifndef LCD_PARAM_BITS
#define LCD_PARAM_BITS 8
#endif
#ifndef LCD_DIVISOR
#define LCD_DIVISOR 10
#endif
#ifdef LCD_PIN_NUM_BCKL
#if LCD_PIN_NUM_BCKL > -1
#ifndef LCD_BCKL_ON_LEVEL
#define LCD_BCKL_ON_LEVEL 1
#endif
#ifndef LCD_BCKL_OFF_LEVEL
#define LCD_BCKL_OFF_LEVEL !LCD_BCKL_ON_LEVEL
#endif
#endif
#endif
#ifdef LCD_PIN_NUM_BCKL
#if LCD_PIN_NUM_BCKL > -1
#define LCD_BCKL
#ifdef LCD_BCKL_PWM_CHANNEL
#define LCD_BCKL_PWM
#endif
#endif
#endif
#ifndef LCD_FULLSCREEN_TRANSFER
#define LCD_FULLSCREEN_TRANSFER 0
#endif
#ifndef LCD_TRANSFER_PALETTE_SIZE
#if defined(LCD_TRANSFER_PALETTE) && LCD_TRANSFER_PALETTE>0 && ((LCD_COLOR_DEPTH==LCD_COLOR_GSC && LCD_BIT_DEPTH<8)||LCD_BIT_DEPTH<16)
#define LCD_TRANSFER_PALETTE_SIZE ((1<<LCD_BIT_DEPTH)*4)
#else 
#define LCD_TRANSFER_PALETTE_SIZE 0
#endif
#else
#define LCD_TRANSFER_PALETTE_SIZE 0
#endif
#ifndef LCD_TRANSFER_SIZE
#define LCD_TRANSFER_SIZE ((((((LCD_WIDTH*(LCD_HEIGHT+(0!=(LCD_HEIGHT%LCD_DIVISOR)))*LCD_BIT_DEPTH+(LCD_DIVISOR-1))/LCD_DIVISOR))+7)/8)+LCD_TRANSFER_PALETTE_SIZE)
#endif
#ifndef LCD_SYNC_TRANSFER
#define LCD_SYNC_TRANSFER 0
#endif
#ifndef LCD_VSYNC
#ifdef LCD_PIN_NUM_VSYNC
#define LCD_VSYNC 1
#else
#define LCD_VSYNC 0
#endif
#endif
#ifndef LCD_DATA_WIDTH
#if LCD_BUS == PANEL_BUS_RGB
#define LCD_DATA_WIDTH LCD_BIT_DEPTH
#else
#define LCD_DATA_WIDTH 16
#endif
#endif 
#endif
#ifndef TOUCH_BUS
#if defined(TOUCH_SPI_HOST)
#define TOUCH_BUS PANEL_BUS_SPI
#elif defined(TOUCH_I2C_HOST) 
#define TOUCH_BUS PANEL_BUS_I2C
#endif
#endif
#ifdef TOUCH_BUS
#ifndef TOUCH_DISABLE_CONTROL_PHASE
#define TOUCH_DISABLE_CONTROL_PHASE 0
#endif 
#ifndef TOUCH_DC_BIT_OFFSET
#define TOUCH_DC_BIT_OFFSET 0
#endif
#ifndef TOUCH_CONTROL_PHASE_BYTES
#define TOUCH_CONTROL_PHASE_BYTES 1
#endif
#ifndef TOUCH_CMD_BITS
#define TOUCH_CMD_BITS 8
#endif
#ifndef TOUCH_PARAM_BITS
#define TOUCH_PARAM_BITS 8
#endif
#ifndef TOUCH_HRES
#define TOUCH_HRES LCD_HRES
#endif
#ifndef TOUCH_VRES
#define TOUCH_VRES LCD_VRES
#endif
#ifndef TOUCH_MIRROR_X
#ifdef LCD_MIRROR_X
#define TOUCH_MIRROR_X LCD_MIRROR_X
#endif
#endif
#ifndef TOUCH_MIRROR_Y
#ifdef LCD_MIRROR_Y
#define TOUCH_MIRROR_Y LCD_MIRROR_Y
#endif
#endif
#ifndef TOUCH_SWAP_XY
#ifdef LCD_SWAP_XY
#define TOUCH_SWAP_XY LCD_SWAP_XY
#endif
#endif
#ifndef TOUCH_TOP_OVERHANG
#define TOUCH_TOP_OVERHANG 0
#endif
#ifndef TOUCH_BOTTOM_OVERHANG
#define TOUCH_BOTTOM_OVERHANG 0
#endif

#ifndef TOUCH_LEFT_OVERHANG
#define TOUCH_LEFT_OVERHANG 0
#endif
#ifndef TOUCH_RIGHT_OVERHANG
#define TOUCH_RIGHT_OVERHANG 0
#endif
#ifndef TOUCH_WIDTH
#ifdef TOUCH_SWAP_XY
#if TOUCH_SWAP_XY
#define TOUCH_WIDTH TOUCH_VRES
#define TOUCH_HEIGHT TOUCH_HRES
#else
#define TOUCH_WIDTH TOUCH_HRES
#define TOUCH_HEIGHT TOUCH_VRES
#endif
#else
#define TOUCH_WIDTH TOUCH_HRES
#define TOUCH_HEIGHT TOUCH_VRES
#endif
#endif
#endif

#ifndef POWER_BUS
#if defined(POWER_SPI_HOST)
#define POWER_BUS PANEL_BUS_SPI
#elif defined(POWER_I2C_HOST) 
#define POWER_BUS PANEL_BUS_I2C
#endif
#endif
#if defined(POWER_BUS) || defined(POWER_INIT)
#define POWER
#endif
#if defined(POWER_BUS) && (POWER_BUS == PANEL_BUS_SPI)
#ifndef POWER_TRANSFER_SIZE
#define POWER_TRANSFER_SIZE 1024
#endif
#endif
#if defined(POWER_BUS) && (POWER_BUS == PANEL_BUS_SPI)
#ifndef POWER_TRANSFER_SIZE
#define POWER_TRANSFER_SIZE 1024
#endif
#endif

#ifndef EXPANDER_BUS
#if defined(EXPANDER_SPI_HOST)
#define EXPANDER_BUS PANEL_BUS_SPI
#elif defined(EXPANDER_I2C_HOST) 
#define EXPANDER_BUS PANEL_BUS_I2C
#endif
#endif
#if defined(EXPANDER_BUS)
#define EXPANDER
#endif
#if defined(EXAPNDER_BUS) && (EXPANDER_BUS == PANEL_BUS_SPI)
#ifndef EXPANDER_TRANSFER_SIZE
#define EXPANDER_TRANSFER_SIZE 1024
#endif
#endif

#if LCD_BUS == PANEL_BUS_MIPI
#if LCD_BIT_DEPTH == 24
#define MIPI_DPI_PX_FORMAT (LCD_COLOR_PIXEL_FORMAT_RGB888)
#elif LCD_BIT_DEPTH == 18
// not supported
#define MIPI_DPI_PX_FORMAT (LCD_COLOR_PIXEL_FORMAT_RGB666)
#elif LCD_BIT_DEPTH == 16
#define MIPI_DPI_PX_FORMAT (LCD_COLOR_PIXEL_FORMAT_RGB565)
#endif
#ifndef LCD_MIPI_BUS_ID
#define LCD_MIPI_BUS_ID 0
#endif
#ifndef LCD_MIPI_LANES
#define LCD_MIPI_LANES 2
#endif
#ifndef LCD_MIPI_LANE_MBPS
#define LCD_MIPI_LANE_MBPS 1000
#endif
#ifndef LCD_MIPI_CLK_SRC
#define LCD_MIPI_CLK_SRC MIPI_DSI_PHY_CLK_SRC_DEFAULT
#endif
#ifndef LCD_MIPI_CHANNEL
#define LCD_MIPI_CHANNEL 0
#endif
#endif

// END fill ins for defauls (don't mess!)

// ============================================================================
// SPI PIN ALIAS MAPPING
// ============================================================================

// ------------- LCD SPI pin aliases -------------
#ifdef LCD_SPI_HOST
#ifdef LCD_PIN_NUM_MOSI
#ifdef LCD_PIN_NUM_D00
#error "LCD_PIN_NUM_D00 and LCD_PIN_NUM_MOSI are both defined"
#else 
#define LCD_PIN_NUM_D00 LCD_PIN_NUM_MOSI
#endif
#endif
#ifdef LCD_PIN_NUM_MISO
#ifdef LCD_PIN_NUM_D01
#error "LCD_PIN_NUM_D01 and LCD_PIN_NUM_MISO are both defined"
#else 
#define LCD_PIN_NUM_D01 LCD_PIN_NUM_MISO
#endif
#endif
#ifdef LCD_PIN_NUM_QUADWP
#ifdef LCD_PIN_NUM_D02
#error "LCD_PIN_NUM_D02 and LCD_PIN_NUM_QUADWP are both defined"
#else
#define LCD_PIN_NUM_D02 LCD_PIN_NUM_QUADWP
#endif
#endif
#ifdef LCD_PIN_NUM_QUADHD
#ifdef LCD_PIN_NUM_D03
#error "LCD_PIN_NUM_D03 and LCD_PIN_NUM_QUADHD are both defined"
#else
#define LCD_PIN_NUM_D03 LCD_PIN_NUM_QUADHD
#endif
#endif
#endif

// ------------- TOUCH SPI pin aliases -------------
#ifdef TOUCH_SPI_HOST
#ifdef TOUCH_PIN_NUM_MOSI
#ifdef TOUCH_PIN_NUM_D00
#error "TOUCH_PIN_NUM_D00 and TOUCH_PIN_NUM_MOSI are both defined"
#else 
#define TOUCH_PIN_NUM_D00 TOUCH_PIN_NUM_MOSI
#endif
#endif
#ifdef TOUCH_PIN_NUM_MISO
#ifdef TOUCH_PIN_NUM_D01
#error "TOUCH_PIN_NUM_D01 and TOUCH_PIN_NUM_MISO are both defined"
#else 
#define TOUCH_PIN_NUM_D01 TOUCH_PIN_NUM_MISO
#endif
#endif
#ifdef TOUCH_PIN_NUM_QUADWP
#ifdef TOUCH_PIN_NUM_D02
#error "TOUCH_PIN_NUM_D02 and TOUCH_PIN_NUM_QUADWP are both defined"
#else
#define TOUCH_PIN_NUM_D02 TOUCH_PIN_NUM_QUADWP
#endif
#endif
#ifdef TOUCH_PIN_NUM_QUADHD
#ifdef TOUCH_PIN_NUM_D03
#error "TOUCH_PIN_NUM_D03 and TOUCH_PIN_NUM_QUADHD are both defined"
#else
#define TOUCH_PIN_NUM_D03 TOUCH_PIN_NUM_QUADHD
#endif
#endif
#endif

// ------------- POWER SPI pin aliases -------------
#ifdef POWER_SPI_HOST
#ifdef POWER_PIN_NUM_MOSI
#ifdef POWER_PIN_NUM_D00
#error "POWER_PIN_NUM_D00 and POWER_PIN_NUM_MOSI are both defined"
#else 
#define POWER_PIN_NUM_D00 POWER_PIN_NUM_MOSI
#endif
#endif
#ifdef POWER_PIN_NUM_MISO
#ifdef POWER_PIN_NUM_D01
#error "POWER_PIN_NUM_D01 and POWER_PIN_NUM_MISO are both defined"
#else 
#define POWER_PIN_NUM_D01 POWER_PIN_NUM_MISO
#endif
#endif
#ifdef POWER_PIN_NUM_QUADWP
#ifdef POWER_PIN_NUM_D02
#error "POWER_PIN_NUM_D02 and POWER_PIN_NUM_QUADWP are both defined"
#else
#define POWER_PIN_NUM_D02 POWER_PIN_NUM_QUADWP
#endif
#endif
#ifdef POWER_PIN_NUM_QUADHD
#ifdef POWER_PIN_NUM_D03
#error "POWER_PIN_NUM_D03 and POWER_PIN_NUM_QUADHD are both defined"
#else
#define POWER_PIN_NUM_D03 POWER_PIN_NUM_QUADHD
#endif
#endif
#endif

// ------------- SD SPI pin aliases -------------
#ifdef SD_SPI_HOST
#ifdef SD_PIN_NUM_MOSI
#ifdef SD_PIN_NUM_D00
#error "SD_PIN_NUM_D00 and SD_PIN_NUM_MOSI are both defined"
#else 
#define SD_PIN_NUM_D00 SD_PIN_NUM_MOSI
#endif
#endif
#ifdef SD_PIN_NUM_MISO
#ifdef SD_PIN_NUM_D01
#error "SD_PIN_NUM_D01 and SD_PIN_NUM_MISO are both defined"
#else 
#define SD_PIN_NUM_D01 SD_PIN_NUM_MISO
#endif
#endif
#ifdef SD_PIN_NUM_QUADWP
#ifdef SD_PIN_NUM_D02
#error "SD_PIN_NUM_D02 and SD_PIN_NUM_QUADWP are both defined"
#else
#define SD_PIN_NUM_D02 SD_PIN_NUM_QUADWP
#endif
#endif
#ifdef SD_PIN_NUM_QUADHD
#ifdef SD_PIN_NUM_D03
#error "SD_PIN_NUM_D03 and SD_PIN_NUM_QUADHD are both defined"
#else
#define SD_PIN_NUM_D03 SD_PIN_NUM_QUADHD
#endif
#endif
#endif

#if defined(SD_SPI_HOST) || defined(SD_PIN_NUM_D00)
#ifdef SD_SPI_HOST
#define SD_BUS PANEL_BUS_SPI
#else
#define SD_BUS PANEL_BUS_MMC
#ifndef SD_MMC_HOST
#define SD_MMC_HOST MMC_1
#endif
#endif
#ifndef SD_MOUNT_POINT
#define SD_MOUNT_POINT "/sdcard"
#endif

#endif


#endif // PANEL_DEFS_H