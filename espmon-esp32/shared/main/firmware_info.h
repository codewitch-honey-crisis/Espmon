#ifndef FIRMWARE_INFO_H
#define FIRMWARE_INFO_H
#include "interface.h"
#include "panel.h"
#include "board_config.h"
#ifdef TOUCH_BUS
#define BOARD_INPUT INPUT_TOUCH
#else
#ifdef BUTTON
#define BOARD_INPUT INPUT_BUTTON
#endif
#endif
#define FIRMWARE_INFO() (request_ident_t){ \
  ESPMON_VERSION_MAJOR, ESPMON_VERSION_MINOR, \
  BUILD_TIMESTAMP_UTC, \
  FIRMWARE_BOARD_ID, \
  {mac_address[0], mac_address[1], mac_address[2], \
   mac_address[3], mac_address[4], mac_address[5]}, \
  FIRMWARE_DISPLAY_NAME, \
  FIRMWARE_SLUG, \
  LCD_WIDTH, LCD_HEIGHT, (LCD_BIT_DEPTH==1), \
  dpi, \
  pixel_size, \
  BOARD_INPUT \
}
#endif
