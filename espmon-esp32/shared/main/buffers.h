#ifndef HTCW_BUFFERS_H
#define HTCW_BUFFERS_H
#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include <wchar.h>
#include <string.h>  /* memcpy */
#ifdef __cplusplus
extern "C" {
#endif

enum {
    BUFFERS_ERROR_EOF = -2,
    BUFFERS_EOF       = -1,
    BUFFERS_SUCCESS   =  0
};

typedef int  (*buffers_read_callback_t )(void* state);
typedef int  (*buffers_write_callback_t)(uint8_t value, void* state);

/* -------------------------------------------------------------------------
 * Read/write functions - single-byte (no byte order)
 * ------------------------------------------------------------------------- */
int buffers_read_uint8_t (uint8_t*  result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_int8_t  (int8_t*   result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_write_uint8_t(uint8_t   value,  buffers_write_callback_t cb, void* state);
int buffers_write_int8_t (int8_t    value,  buffers_write_callback_t cb, void* state);
/* single-byte aliases */
int buffers_read_char         (char*          result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_unsigned_char(unsigned char* result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_bool         (bool*          result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_write_char         (char          value, buffers_write_callback_t cb, void* state);
int buffers_write_unsigned_char(unsigned char value, buffers_write_callback_t cb, void* state);
int buffers_write_bool         (bool          value, buffers_write_callback_t cb, void* state);

/* -------------------------------------------------------------------------
 * Read functions - little-endian (_le variants)
 * ------------------------------------------------------------------------- */
int buffers_read_uint16_t_le(uint16_t* result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_uint32_t_le(uint32_t* result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_uint64_t_le(uint64_t* result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_int16_t_le (int16_t*  result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_int32_t_le (int32_t*  result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_int64_t_le (int64_t*  result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_float_le   (float*    result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_double_le  (double*   result, buffers_read_callback_t cb, void* state, int* bytes_read);
/* aliases */
int buffers_read_short_le         (short*              result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_unsigned_short_le(unsigned short*     result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_int_le           (int*                result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_unsigned_int_le  (unsigned int*       result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_long_le          (long*               result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_unsigned_long_le (unsigned long*      result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_long_long_le         (long long*          result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_unsigned_long_long_le(unsigned long long* result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_wchar_t_le       (wchar_t*            result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_size_t_le        (size_t*             result, buffers_read_callback_t cb, void* state, int* bytes_read);

/* -------------------------------------------------------------------------
 * Read functions - big-endian (_be variants)
 * ------------------------------------------------------------------------- */
int buffers_read_uint16_t_be(uint16_t* result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_uint32_t_be(uint32_t* result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_uint64_t_be(uint64_t* result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_int16_t_be (int16_t*  result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_int32_t_be (int32_t*  result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_int64_t_be (int64_t*  result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_float_be   (float*    result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_double_be  (double*   result, buffers_read_callback_t cb, void* state, int* bytes_read);

/* -------------------------------------------------------------------------
 * Write functions - little-endian (_le variants)
 * ------------------------------------------------------------------------- */
int buffers_write_uint16_t_le(uint16_t value, buffers_write_callback_t cb, void* state);
int buffers_write_uint32_t_le(uint32_t value, buffers_write_callback_t cb, void* state);
int buffers_write_uint64_t_le(uint64_t value, buffers_write_callback_t cb, void* state);
int buffers_write_int16_t_le (int16_t  value, buffers_write_callback_t cb, void* state);
int buffers_write_int32_t_le (int32_t  value, buffers_write_callback_t cb, void* state);
int buffers_write_int64_t_le (int64_t  value, buffers_write_callback_t cb, void* state);
int buffers_write_float_le   (float    value, buffers_write_callback_t cb, void* state);
int buffers_write_double_le  (double   value, buffers_write_callback_t cb, void* state);
/* aliases */
int buffers_write_short_le         (short              value, buffers_write_callback_t cb, void* state);
int buffers_write_unsigned_short_le(unsigned short     value, buffers_write_callback_t cb, void* state);
int buffers_write_int_le           (int                value, buffers_write_callback_t cb, void* state);
int buffers_write_unsigned_int_le  (unsigned int       value, buffers_write_callback_t cb, void* state);
int buffers_write_long_le          (long               value, buffers_write_callback_t cb, void* state);
int buffers_write_unsigned_long_le (unsigned long      value, buffers_write_callback_t cb, void* state);
int buffers_write_long_long_le         (long long          value, buffers_write_callback_t cb, void* state);
int buffers_write_unsigned_long_long_le(unsigned long long value, buffers_write_callback_t cb, void* state);
int buffers_write_wchar_t_le       (wchar_t            value, buffers_write_callback_t cb, void* state);
int buffers_write_size_t_le        (size_t             value, buffers_write_callback_t cb, void* state);

/* -------------------------------------------------------------------------
 * Write functions - big-endian
 * ------------------------------------------------------------------------- */
int buffers_write_uint16_t_be(uint16_t value, buffers_write_callback_t cb, void* state);
int buffers_write_uint32_t_be(uint32_t value, buffers_write_callback_t cb, void* state);
int buffers_write_uint64_t_be(uint64_t value, buffers_write_callback_t cb, void* state);
int buffers_write_int16_t_be (int16_t  value, buffers_write_callback_t cb, void* state);
int buffers_write_int32_t_be (int32_t  value, buffers_write_callback_t cb, void* state);
int buffers_write_int64_t_be (int64_t  value, buffers_write_callback_t cb, void* state);
int buffers_write_float_be   (float    value, buffers_write_callback_t cb, void* state);
int buffers_write_double_be  (double   value, buffers_write_callback_t cb, void* state);

#ifdef __cplusplus
}
#endif
#endif /* HTCW_BUFFERS_H */
