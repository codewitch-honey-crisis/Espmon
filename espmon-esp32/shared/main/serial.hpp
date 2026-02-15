#pragma once
#include <stdint.h>
#include <interface.h>

bool serial_init();
void serial_write(const request_ident_t& req);
void serial_write(int8_t cmd,uint8_t screen_index);
void serial_write_identity();
int8_t serial_read_packet(response_t* out_resp);
