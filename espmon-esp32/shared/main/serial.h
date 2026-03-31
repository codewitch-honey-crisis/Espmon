#ifndef SERIAL_H
#define SERIAL_H
#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#ifdef __cplusplus
extern "C" {
#endif
bool serial_init(size_t max_payload_size);
int serial_getc(void);
void serial_putc(int value);
#ifdef CONFIG_SOC_USB_SERIAL_JTAG_SUPPORTED
#define HAS_SERIAL2
bool serial2_init(size_t max_payload_size);
int serial2_getc(void);
void serial2_putc(int value);
#endif
#ifdef __cplusplus
}
#endif
#endif // SERIAL_H