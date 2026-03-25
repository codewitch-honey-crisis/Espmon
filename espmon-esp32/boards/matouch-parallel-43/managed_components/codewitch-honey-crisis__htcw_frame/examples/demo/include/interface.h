// generate with:
// C code
// python .\buffers_gen_c.py --buffers interface.h
// C# code
// python .\buffers_gen_cs.py --buffers interface.h
#ifndef INTERFACE_H
#define INTERFACE_H
#include <stdint.h>
#include <stdbool.h>

typedef enum {
    CMD_NONE,
    CMD_RNG,
    CMD_GPIO_MODE,
    CMD_GPIO_GET,
    CMD_GPIO_SET,
    CMD_RNG_RESPONSE,
    CMD_GPIO_GET_RESPONSE
} st_message_command_t;

typedef enum {
    MODE_INPUT,
    MODE_INPUT_PULLUP,
    MODE_INPUT_PULLDOWN,
    MODE_OUTPUT,
    MODE_OUTPUT_OPEN_DRAIN
} st_gpio_mode_t;

typedef struct {
} st_rng_message_t;

typedef struct {
    uint64_t mask;
} st_gpio_get_message_t;

typedef struct {
    uint64_t mask;
    uint64_t values;
} st_gpio_set_message_t;

typedef struct {
    uint8_t gpio;
    st_gpio_mode_t mode;
} st_gpio_mode_message_t;

typedef struct {
    uint32_t value;
} st_rng_response_message_t;

typedef struct {
    uint64_t values;
} st_gpio_get_response_message_t;

#endif // INTERFACE_H