#ifndef INTERFACE_BUFFERS_H
#define INTERFACE_BUFFERS_H
#include "interface.h"
#include "buffers.h"

#define INTERFACE_MAX_SIZE (164)
#define RESPONSE_VALUE_SIZE (8)
#define RESPONSE_VALUE_ENTRY_SIZE (16)
#define RESPONSE_DATA_SIZE (32)
#define RESPONSE_COLOR_SIZE (4)
#define RESPONSE_SCREEN_VALUE_ENTRY_SIZE (17)
#define RESPONSE_SCREEN_ENTRY_SIZE (55)
#define RESPONSE_SCREEN_HEADER_SIZE (2)
#define RESPONSE_SCREEN_SIZE (112)
#define RESPONSE_CLEAR_SIZE (0)
#define RESPONSE_IDENT_SIZE (0)
#define RESPONSE_REFRESH_SCREEN_SIZE (0)
#define REQUEST_DATA_SIZE (1)
#define REQUEST_NOP_SIZE (0)
#define REQUEST_SCREEN_SIZE (1)
#define REQUEST_SET_MODE_SIZE (1)
#define REQUEST_IDENT_SIZE (164)

#ifdef __cplusplus
extern "C" {
#endif

int response_value_read(response_value_t* s, buffers_read_callback_t on_read, void* on_read_state);
int response_value_write(const response_value_t* s, buffers_write_callback_t on_write, void* on_write_state);
size_t response_value_size(const response_value_t* s);

int response_value_entry_read(response_value_entry_t* s, buffers_read_callback_t on_read, void* on_read_state);
int response_value_entry_write(const response_value_entry_t* s, buffers_write_callback_t on_write, void* on_write_state);
size_t response_value_entry_size(const response_value_entry_t* s);

int response_data_read(response_data_t* s, buffers_read_callback_t on_read, void* on_read_state);
int response_data_write(const response_data_t* s, buffers_write_callback_t on_write, void* on_write_state);
size_t response_data_size(const response_data_t* s);

int response_color_read(response_color_t* s, buffers_read_callback_t on_read, void* on_read_state);
int response_color_write(const response_color_t* s, buffers_write_callback_t on_write, void* on_write_state);
size_t response_color_size(const response_color_t* s);

int response_screen_value_entry_read(response_screen_value_entry_t* s, buffers_read_callback_t on_read, void* on_read_state);
int response_screen_value_entry_write(const response_screen_value_entry_t* s, buffers_write_callback_t on_write, void* on_write_state);
size_t response_screen_value_entry_size(const response_screen_value_entry_t* s);

int response_screen_entry_read(response_screen_entry_t* s, buffers_read_callback_t on_read, void* on_read_state);
int response_screen_entry_write(const response_screen_entry_t* s, buffers_write_callback_t on_write, void* on_write_state);
size_t response_screen_entry_size(const response_screen_entry_t* s);

int response_screen_header_read(response_screen_header_t* s, buffers_read_callback_t on_read, void* on_read_state);
int response_screen_header_write(const response_screen_header_t* s, buffers_write_callback_t on_write, void* on_write_state);
size_t response_screen_header_size(const response_screen_header_t* s);

int response_screen_read(response_screen_t* s, buffers_read_callback_t on_read, void* on_read_state);
int response_screen_write(const response_screen_t* s, buffers_write_callback_t on_write, void* on_write_state);
size_t response_screen_size(const response_screen_t* s);

int response_clear_read(response_clear_t* s, buffers_read_callback_t on_read, void* on_read_state);
int response_clear_write(const response_clear_t* s, buffers_write_callback_t on_write, void* on_write_state);
size_t response_clear_size(const response_clear_t* s);

int response_ident_read(response_ident_t* s, buffers_read_callback_t on_read, void* on_read_state);
int response_ident_write(const response_ident_t* s, buffers_write_callback_t on_write, void* on_write_state);
size_t response_ident_size(const response_ident_t* s);

int response_refresh_screen_read(response_refresh_screen_t* s, buffers_read_callback_t on_read, void* on_read_state);
int response_refresh_screen_write(const response_refresh_screen_t* s, buffers_write_callback_t on_write, void* on_write_state);
size_t response_refresh_screen_size(const response_refresh_screen_t* s);

int request_data_read(request_data_t* s, buffers_read_callback_t on_read, void* on_read_state);
int request_data_write(const request_data_t* s, buffers_write_callback_t on_write, void* on_write_state);
size_t request_data_size(const request_data_t* s);

int request_nop_read(request_nop_t* s, buffers_read_callback_t on_read, void* on_read_state);
int request_nop_write(const request_nop_t* s, buffers_write_callback_t on_write, void* on_write_state);
size_t request_nop_size(const request_nop_t* s);

int request_screen_read(request_screen_t* s, buffers_read_callback_t on_read, void* on_read_state);
int request_screen_write(const request_screen_t* s, buffers_write_callback_t on_write, void* on_write_state);
size_t request_screen_size(const request_screen_t* s);

int request_set_mode_read(request_set_mode_t* s, buffers_read_callback_t on_read, void* on_read_state);
int request_set_mode_write(const request_set_mode_t* s, buffers_write_callback_t on_write, void* on_write_state);
size_t request_set_mode_size(const request_set_mode_t* s);

int request_ident_read(request_ident_t* s, buffers_read_callback_t on_read, void* on_read_state);
int request_ident_write(const request_ident_t* s, buffers_write_callback_t on_write, void* on_write_state);
size_t request_ident_size(const request_ident_t* s);

#ifdef __cplusplus
}
#endif
#endif /* INTERFACE_BUFFERS_H */
