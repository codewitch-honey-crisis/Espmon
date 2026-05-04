#ifndef INTERFACE_H
#define INTERFACE_H
#include <stdint.h>
#include <stdbool.h>
#ifdef __cplusplus
extern "C" {
#endif
#define ESPMON_VERSION_MAJOR 4
#define ESPMON_VERSION_MINOR 0
// all packet structs are preceded on the wire by a 1 byte command
typedef enum {
    CMD_NONE,
    CMD_SCREEN,
    CMD_DATA,
    CMD_MODE,
    CMD_IDENT,
    CMD_NOP,
    CMD_CLEAR,
    CMD_REFRESH_SCREEN
} command_t;
typedef enum {
    INPUT_NONE = 0,
    INPUT_TOUCH = 1,
    INPUT_BUTTON = 2
} input_type_t;

typedef struct { // 8 byes
    float value;
    float scaled;
} response_value_t;

typedef struct { // 16 byes
    response_value_t value1;
    response_value_t value2;
} response_value_entry_t;

typedef struct { // 32 bytes
    response_value_entry_t top;
    response_value_entry_t bottom;
} response_data_t;

typedef struct { // 4 bytes on the wire
    uint8_t a;
    uint8_t r;
    uint8_t g;
    uint8_t b;
} response_color_t;

typedef struct { // 16 bytes on the wire
    response_color_t color;
    char suffix[12];
}  response_screen_value_entry_t;

typedef struct { // 52 bytes on the wire
    char label[16];
    response_color_t color;    
    response_screen_value_entry_t value1;
    response_screen_value_entry_t value2;
}  response_screen_entry_t;

typedef struct { // 2 bytes on the wire
    int8_t index;
    uint8_t flags; // bit 0 top 1 is gradient, bit 1 top 2 is gradient, bit 2 bottom 1 is gradient, bit 3 bottom 2 is gradient
} response_screen_header_t;

typedef struct { // 108 bytes on the wire
    response_screen_header_t header;
    response_screen_entry_t top;
    response_screen_entry_t bottom;
} response_screen_t;

typedef struct {

} response_clear_t;

typedef struct {

} response_ident_t;

typedef struct {

} response_refresh_screen_t;

typedef union {
    response_data_t data;
    response_screen_t screen;
    response_clear_t clear;
    response_ident_t ident;
    response_refresh_screen_t refresh_screen;
} response_t;

typedef struct {
    int8_t screen_index;
} request_data_t;

typedef struct {

} request_nop_t;

typedef struct {
    int8_t screen_index;
} request_screen_t;

typedef struct {
    int8_t mode;
} request_set_mode_t;

typedef struct { // 162 bytes on the wire
    uint16_t version_major;
    uint16_t version_minor;
    uint64_t build;
    int16_t id;
    uint8_t mac_address[6];
    char display_name[64];
    char slug[64];
    uint16_t horizontal_resolution;
    uint16_t vertical_resolution;
    bool is_monochrome;
    float dpi;
    float pixel_size; // in millimeters
    input_type_t input_type;
} request_ident_t;
#ifdef __cplusplus
}
#endif
#endif