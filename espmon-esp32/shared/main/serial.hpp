#pragma once
#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
bool serial_init(size_t max_payload);
bool serial_update();
bool serial_try_get_frame(uint8_t* out_cmd,void** out_ptr, size_t* out_length);
bool serial_put_frame(uint8_t cmd, void* frame, size_t frame_length);
bool serial_discard_frame();