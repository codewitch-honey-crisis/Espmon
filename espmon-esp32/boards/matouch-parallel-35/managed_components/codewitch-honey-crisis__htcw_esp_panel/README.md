# htcw_esp_panel

Provides out of the box support for several devkits, including display, input, and power management

Supports up to 16 different devices (some untested) out of the box

You can also easily add your own

This hides the complications of using the ESP LCD Panel API, and ESP LCD Touch API, plus any power management necessities required for the board to work properly.

It provides support for GPIO buttons, touch panels, power management (minimal) and displays

The display API is designed with LVGL or htcw_uix partial updates in mind

As far as display interconnects it supports I2C, SPI, I8080, RGB and MIPI

For touch it supports I2C or SPI

Buttons are presently GPIO

SD supports SPI or MMC

If you need more display or touch controller support, you can do what i did, download the ESP LCD Panel API component or related touch component and create a platformIO library out of it. You can then use it in your project. Of course, if you're just using idf.py you can use the component directly.

Documentation is presently somewhat lacking, but it is fairly easy to copy from an existing device and use its configuration as a template.

PlatformIO lib is `codewitch-honey-crisis/htcw_esp_panel`

Use the example code therein to get started.

You can also use the [Panel Generator Tool](panel_gen.html) to produce a `custom_panel.h` for your project. It runs locally in a browser.

## Configuration

To support a custom panel in you project, include custom_panel.h in your project somewhere in the compilers include folders.

For PlatformIO you can put it in `/include` and then add `build_flags= -Iinclude` to your platformio.ini entry.

I recommend you start with an entry from `src/panels.h` copy that into your `custom_panel.h` and then modify it from there.


### LCD SPI, I2C RGB, MIPI and I8080 Display bus detection

Depending on the values you set, the `LCD_BUS` will be set to `PANEL_BUS_I2C`, `PANEL_BUS_SPI`, `PANEL_BUS_I8080` `PANEL_BUS_RGB`, or `PANEL_BUS_MIPI`

For example, if you define `LCD_PIN_NUM_D00` you'll either end up with I8080 or RGB, depending on whether `LCD_HSYNC_FRONT_PORCH` is defined.

If you define `LCD_HSYNC_FRONT_PORCH` but not `LCD_PIN_NUM_D00` you'll end up with MIPI.

If you define `LCD_SPI_HOST` the bus ends up as SPI, otherwise I2C for `LCD_I2C_HOST`

These internal determinations may change in the future if more bus types are added, the above is just to clarify some of the automagic happening. 

### LCD_SPI_HOST
The SPI host for the display connected via SPI

Example setting: `SPI_2`

Requires `LCD_PIN_NUM_MOSI`,`LCD_PIN_NUM_CLK`,`LCD_PIN_NUM_DC`

Can also have `LCD_PIN_NUM_CS`,`LCD_PIN_NUM_RST`, `LCD_PIN_NUM_BCKL`, `LCD_BCKL_ON_LEVEL`, often `LCD_CLOCK_HZ` and sometimes `LCD_PIN_NUM_MISO`

Note that if you're sharing the SPI bus with something that needs input like a touch device or SD reader, its MISO pin if defined will be attached to the same bus

### LCD_I2C_HOST
The I2C host for the display connected via I2C

Example setting: `I2C_1`

Requires `LCD_PIN_NUM_SDA`, and `LCD_PIN_NUM_SCL`, `LCD_I2C_ADDRESS`

Can also have `LCD_I2C_PICKUP`, `LCD_CLOCK_HZ`, `LCD_DC_BIT_OFFSET`, `LCD_CMD_BITS`, `LCD_PARAM_BITS`, and `LCD_DISABLE_CONTROL_PHASE`

Notes: Some devices do not like the newer ESP32 I2C facilities. If you run into trouble, try defining `LEGACY_I2C` - note that this impacts all the code in the system.

If you have multiple devices connected to the same I2C bus, and `LEGACY_I2C` is defined (or you want your config to be able to work with it), it will use the lowest frequency setting supported by any device on that bus, because the old I2C does not have per device speed settings.

### LCD_INIT
The LCD panel call to instantiate the panel 

Example setting: `esp_lcd_new_panel_st7789`

Note: The ST7789 is built in, but if you need to use an external LCD driver component, you can add it to the project `./components` folder or to the `./platformio.ini` as a `lib_deps` entry, and add the `#include` for the component here. All includes must be wrapped in an `#ifdef PANEL_DEPENDENCIES` guard to ensure that headers don't get includeed when simply looking for configuration settings during for example, the LVGL build process when `lv_conf.h`, including `#include "panel_defs.h` is consumed by the compiler. (You don't have to use `lv_conf.h` this way, but I allow for it)

### LCD_HRES, LCD_VRES

The native horizontal and vertical resolutions. This is pre-swapped or adjusted - just what the controller natively registers as.

Example setting: `320` 

### LCD_COLOR_SPACE 

The color space the LCD uses.

Example setting: `LCD_COLOR_RGB` 

Can be `LCD_COLOR_RGB` for RGB, `LCD_COLOR_BGR` for BGR or `LCD_COLOR_GSC` for grayscale or monochrome

### LCD_GAP_X, LCD_GAP_Y

The offset in pixels from the top or left of the screen.

Example setting: `40`

### LCD_MIRROR_X, LCD_MIRROR_Y

Can be `false` or `true`. Defaults to `false`. Indicates whether to mirror the X or Y axes.

### LCD_SWAP_XY 

Can be `false` or `true`. Defaults to `false`. Indicates whether or not the X and Y axes are flipped.

### LCD_INVERT_COLOR 

Can be `false` or `true`. Defaults to `false`. Indicates whether the colors are inverted (common for IPS panels)

### LCD_BIT_DEPTH

Indicates the color depth of the frame buffer

### LCD_TRANSFER_SIZE

Can be any number of bytes. `0` disables provisioning of transfer buffers. The default is computed based on a fractional part of the display resolution, as indicated by `LCD_DIVISOR`

Note that LVGL includes a palette in the transfer buffer for `LCD_BIT_DEPTH < 8`. Any screens at that bit depth must account for this by setting `LCD_TRANSFER_SIZE` explicitly and including the space for the palette. I may make some facilities to help with this in the future, but for example, to hold the entire SSD1306 frame buffer + palette (2 32-bit color entries), you'd set `LCD_TRANSFER_SIZE` to `(LCD_WIDTH*LCD_HEIGHT*LCD_BIT_DEPTH+7)/8+8` where the `+8` makes room for the palette data.

### LCD_TRANSFER_IN_SPIRAM

Indicates that the LCD transfer buffer(s) should be created in attached PSRAM instead of intrinsic SRAM. This is not supported on regular ESP32s, but it is on S3s and P4s, and is often desirable with larger resolution displays.

### LCD_DIVISOR

Defaults to `10`. Indicates the fractional part of the display to use for computing `LCD_TRANSFER_SIZE`. Typically 10 is fine, but for something like an SSD1306 it's so small, that it doesn't make sense to subdivide it further, so `1` is a good value for it there. Similarly for the TTGO its display is smaller but not quite as small as the SSD1306, so making it `2` here strikes a good balance between memory consumption and performance. Generally lower is faster but requires more memory. However "faster" here quickly runs into diminishing returns after some magic point to be determined by you, dear user.

### LCD_DATA_ENDIAN_LITTLE

This indicates that the display operates on little endian data, instead of the more common and default big endian data ordering

### LCD_VENDOR_CONFIG

Some panels have a custom structure you must fill in with extra details about the display. You can define this macro to use it. See the existing panels.h for examples

#### LCD_TRANSLATE

Some panels have a funny memory buffer, and the flushing doesn't consume regular bitmaps. Defining code in this macro will allow insert it into the flushing pipeline to do pre-flush translation. See the existing definitions in panels.h for examples. For LVGL, which includes a palette for `LCD_BIT_DEPTH < 8`, you'll need to advance the `buffer` pointer by the number of bytes in the palette (ex: 8 for 2-color monochrome), like 
```c
#define LCD_TRANSLATE bitmap = ((uint8_t*)bitmap) + 8;
```

### TOUCH_SPI_HOST
The SPI host for the touch panel connected via SPI

Example setting: `SPI_3`

Requires `TOUCH_PIN_NUM_MISO`,`TOUCH_PIN_NUM_CLK`

Can also have `LCD_PIN_NUM_CS`,`LCD_PIN_NUM_RST`, `LCD_PIN_NUM_INT`, `LCD_INT_ON_LEVEL`, `TOUCH_RST_ON_LEVEL`, often `TOUCH_CLOCK_HZ` and sometimes `TOUCH_PIN_NUM_MOSI`

Note that if you're sharing the SPI bus with something that needs output like an SD reader, you should provide a MOSI pin definition here.

### TOUCH_I2C_HOST
The I2C host for the touch panel connected via I2C

Example setting: `I2C_1`

Requires `TOUCH_PIN_NUM_SDA`, and `TOUCH_PIN_NUM_SCL`, `TOUCH_I2C_ADDRESS`

Can also have `TOUCH_I2C_PICKUP`, `TOUCH_CLOCK_HZ`, `TOUCH_DC_BIT_OFFSET`, `TOUCH_CMD_BITS`, `TOUCH_PARAM_BITS`, and `TOUCH_DISABLE_CONTROL_PHASE`

Note: Some devices do not like the newer ESP32 I2C facilities. If you run into trouble, try defining `LEGACY_I2C` - note that this impacts all the code in the system.

### TOUCH_INIT
The touch panel call to instantiate the panel 

Example setting: `esp_lcd_touch_new_i2c_ft6x36`

Note: You can add any necessary touch component to the project `./components` folder or to the `./platformio.ini` as a `lib_deps` entry, and add the `#include` for the component here. 

Again, wrap any includes in a `#ifdef PANEL_DEPENDENCIES` guard.

### TOUCH_HRES, TOUCH_VRES

The native horizontal and vertical resolutions. This is pre-swapped or adjusted - just what the controller natively registers as. If not specified these will be the same as the LCD, if defined.

Example setting: `320` 

### TOUCH_LEFT_OVERHANG,TOUCH_RIGHT_OVERHANG,TOUCH_TOP_OVERHANG,TOUCH_BOTTOM_OVERHANG

These arguments are for indicating the amount that the touch panel hangs over or past the display boundary.

For example, the M5 Stack Core2's touch panel is 40 pixels taller than the display, extending off the bottom so it has a `TOUCH_BOTTOM_OVERHANG` of `40`

These default to `0`

Example setting: `40`

### SD

For SD just define the pins, and the `SD_SPI_HOST` if it's SPI. Otherwise it uses SDMMC. `SD_PIN_NUM_WP` and `SD_PIN_NUM_CD` are for write protection and line change detection, respectively

## Using the API

The LCD API is only available when an LCD has been configured. You can determine this by checking if `LCD_BUS` is defined.

```c
void panel_lcd_init(void);
```
This initializes the display and transfer buffers. Call this before using any other `panel_lcd` functions.

```c
void panel_lcd_flush(uint16_t x1, uint16_t y1, uint16_t x2, uint16_t y2, void* bitmap);
```
This function flushes a bitmap to the display at the specified coordinates. The bitmap must be the size of the logical rectangle indicated by the coordinates, otherwise the behavior is undefined.

```c
void* panel_lcd_transfer_buffer(void);
void* panel_lcd_transfer_buffer2(void);
```
These functions return the transfer buffers that can be used by libraries like LVGL to create bitmaps to send to the display. `LCD_TRANSFER_SIZE` indicates the size of each buffer. Set it to zero to disable this feature.

```c
void panel_lcd_flush_complete(void);
```
This function must be implemented by you. It notifies you when the last call to `panel_lcd_flush()` is completed.

```c
size_t panel_lcd_vsync_flush_count(void);
```
With supported displays, this will return the number of calls to `panel_lcd_flush_complete()` since the last display vertical sync. On displays where it's not supported, it will always be zero.


The touch API is only available when a touch panel has been configured. You can check this by seeing if `TOUCH_BUS` is defined.

```c
void panel_touch_init(void);
```
This function initializes the touch panel. Call this before calling any other touch functions.

```c
void panel_touch_update(void);
```
This function fetches the latest touch information from the touch panel. Call this before calling `panel_touch_read()` or `panel_touch_read_raw()`.

```c
void panel_touch_read(size_t* in_out_count,uint16_t* out_x,uint16_t* out_y, uint16_t* out_strength);
```
This function reads the panel data, doing any necessary scaling to match the size of the display, plus overhang. The first argument takes the maximum count of buttons to check, and on return contains the actual number of buttons to check. The rest of the arguments are arrays of size `in_out_count` containing the X, Y and strength values for each touch point.

```c
void panel_touch_read_raw(size_t* in_out_count,uint16_t* out_x,uint16_t* out_y, uint16_t* out_strength);
```
This function reads the touch panel without doing any rescaling.


The button API is only available if buttons have been configured. You can check if `BUTTON` is defined to determine this.
```c
void panel_button_init(void);
```

Call this function to initialize the buttons, before using any other button functions.

```c
bool panel_button_read(uint8_t pin);
```
This reads the state of the button at the given pin.

```c
uint64_t panel_button_read_all(void);
```
This function reads all of the buttons and returns a mask of the pressed buttons as a series of bits - 1 bit set for each pin of a pressed button, at the bit position of the GPIO pin for the button.

The power API is only available if a power setup has been configured. You can determine this by checking if `POWER` is defined.

```c
void panel_power_init(void);
```
This function initializes the power management subsystem. It should be called before other initialization functions in case it needs to turn peripherals on.

The SD API is only available if an SD reader/writer has been configured. You can determine this by checking if `SD_BUS` is defined.

```c
bool panel_sd_init(bool format_on_fail, size_t max_files, size_t alloc_unit_size);
```
Use this function to attempt to mount an SD card. You can indicate whether or not to attempt to format the card if the mount failed, the maximum open file count or zero to use the default of 5, and the allocation unit size, which can be 0 in order to use the default value of 512.

Once mounted, file operations are supported through the C/++ runtimes and POSIX system calls off of the path indicated by `SD_MOUNT_POINT` which defaults to `/sdcard`.

```c
void panel_sd_end();
```
Unmounts a mounted SD card.

```c
sdmmc_card_t* panel_sd_handle();
```
Gets the handle to the SD card which can be used for things like querying the card storage capacity.
