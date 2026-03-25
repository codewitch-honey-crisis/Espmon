#include "buffers.h"

/* =========================================================================
 * Internal helpers
 * ========================================================================= */

static int read_byte(buffers_read_callback_t cb, void* state, uint8_t* out, int* bytes_read) {
    int b = cb(state);
    if (b == -1) return BUFFERS_ERROR_EOF;
    if (b  <  0) return b;
    *out = (uint8_t)b;
    if (bytes_read) ++(*bytes_read);
    return 0;
}

static int write_byte(uint8_t v, buffers_write_callback_t cb, void* state) {
    int r = cb(v, state);
    if (r < 0) return r;
    return 1;
}

/* =========================================================================
 * uint8_t  (single byte - no byte order)
 * ========================================================================= */
int buffers_read_uint8_t(uint8_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    return read_byte(cb, state, result, bytes_read);
}
int buffers_write_uint8_t(uint8_t value, buffers_write_callback_t cb, void* state) {
    return write_byte(value, cb, state);
}

/* =========================================================================
 * int8_t  (single byte - no byte order)
 * ========================================================================= */
int buffers_read_int8_t(int8_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint8_t tmp; int r = read_byte(cb, state, &tmp, bytes_read); if (r < 0) return r;
    *result = (int8_t)tmp; return 0;
}
int buffers_write_int8_t(int8_t value, buffers_write_callback_t cb, void* state) {
    return write_byte((uint8_t)value, cb, state);
}

/* =========================================================================
 * uint16_t  - little-endian: low byte first
 * ========================================================================= */
int buffers_read_uint16_t_le(uint16_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint8_t lo, hi; int r;
    r = read_byte(cb, state, &lo, bytes_read); if (r < 0) return r;
    r = read_byte(cb, state, &hi, bytes_read); if (r < 0) return r;
    *result = (uint16_t)((hi << 8) | lo);
    return 0;
}
int buffers_write_uint16_t_le(uint16_t value, buffers_write_callback_t cb, void* state) {
    int r, total = 0;
    r = write_byte((uint8_t)(value      ), cb, state); if (r < 0) return r; total += r;
    r = write_byte((uint8_t)(value >> 8 ), cb, state); if (r < 0) return r; total += r;
    return total;
}

/* big-endian */
int buffers_read_uint16_t_be(uint16_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint8_t hi, lo; int r;
    r = read_byte(cb, state, &hi, bytes_read); if (r < 0) return r;
    r = read_byte(cb, state, &lo, bytes_read); if (r < 0) return r;
    *result = (uint16_t)((hi << 8) | lo);
    return 0;
}
int buffers_write_uint16_t_be(uint16_t value, buffers_write_callback_t cb, void* state) {
    int r, total = 0;
    r = write_byte((uint8_t)(value >> 8), cb, state); if (r < 0) return r; total += r;
    r = write_byte((uint8_t)(value     ), cb, state); if (r < 0) return r; total += r;
    return total;
}

/* =========================================================================
 * int16_t
 * ========================================================================= */
int buffers_read_int16_t_le(int16_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint16_t tmp; int r = buffers_read_uint16_t_le(&tmp, cb, state, bytes_read); if (r < 0) return r;
    *result = (int16_t)tmp; return 0;
}
int buffers_write_int16_t_le(int16_t value, buffers_write_callback_t cb, void* state) {
    return buffers_write_uint16_t_le((uint16_t)value, cb, state);
}
int buffers_read_int16_t_be(int16_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint16_t tmp; int r = buffers_read_uint16_t_be(&tmp, cb, state, bytes_read); if (r < 0) return r;
    *result = (int16_t)tmp; return 0;
}
int buffers_write_int16_t_be(int16_t value, buffers_write_callback_t cb, void* state) {
    return buffers_write_uint16_t_be((uint16_t)value, cb, state);
}

/* =========================================================================
 * uint32_t
 * ========================================================================= */
int buffers_read_uint32_t_le(uint32_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint8_t b0, b1, b2, b3; int r;
    r = read_byte(cb, state, &b0, bytes_read); if (r < 0) return r;
    r = read_byte(cb, state, &b1, bytes_read); if (r < 0) return r;
    r = read_byte(cb, state, &b2, bytes_read); if (r < 0) return r;
    r = read_byte(cb, state, &b3, bytes_read); if (r < 0) return r;
    *result = ((uint32_t)b3 << 24) | ((uint32_t)b2 << 16) |
              ((uint32_t)b1 <<  8) |  (uint32_t)b0;
    return 0;
}
int buffers_write_uint32_t_le(uint32_t value, buffers_write_callback_t cb, void* state) {
    int r, total = 0;
    r = write_byte((uint8_t)(value      ), cb, state); if (r < 0) return r; total += r;
    r = write_byte((uint8_t)(value >>  8), cb, state); if (r < 0) return r; total += r;
    r = write_byte((uint8_t)(value >> 16), cb, state); if (r < 0) return r; total += r;
    r = write_byte((uint8_t)(value >> 24), cb, state); if (r < 0) return r; total += r;
    return total;
}
int buffers_read_uint32_t_be(uint32_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint8_t b0, b1, b2, b3; int r;
    r = read_byte(cb, state, &b0, bytes_read); if (r < 0) return r;
    r = read_byte(cb, state, &b1, bytes_read); if (r < 0) return r;
    r = read_byte(cb, state, &b2, bytes_read); if (r < 0) return r;
    r = read_byte(cb, state, &b3, bytes_read); if (r < 0) return r;
    *result = ((uint32_t)b0 << 24) | ((uint32_t)b1 << 16) |
              ((uint32_t)b2 <<  8) |  (uint32_t)b3;
    return 0;
}
int buffers_write_uint32_t_be(uint32_t value, buffers_write_callback_t cb, void* state) {
    int r, total = 0;
    r = write_byte((uint8_t)(value >> 24), cb, state); if (r < 0) return r; total += r;
    r = write_byte((uint8_t)(value >> 16), cb, state); if (r < 0) return r; total += r;
    r = write_byte((uint8_t)(value >>  8), cb, state); if (r < 0) return r; total += r;
    r = write_byte((uint8_t)(value      ), cb, state); if (r < 0) return r; total += r;
    return total;
}

/* =========================================================================
 * int32_t
 * ========================================================================= */
int buffers_read_int32_t_le(int32_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint32_t tmp; int r = buffers_read_uint32_t_le(&tmp, cb, state, bytes_read); if (r < 0) return r;
    *result = (int32_t)tmp; return 0;
}
int buffers_write_int32_t_le(int32_t value, buffers_write_callback_t cb, void* state) {
    return buffers_write_uint32_t_le((uint32_t)value, cb, state);
}
int buffers_read_int32_t_be(int32_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint32_t tmp; int r = buffers_read_uint32_t_be(&tmp, cb, state, bytes_read); if (r < 0) return r;
    *result = (int32_t)tmp; return 0;
}
int buffers_write_int32_t_be(int32_t value, buffers_write_callback_t cb, void* state) {
    return buffers_write_uint32_t_be((uint32_t)value, cb, state);
}

/* =========================================================================
 * uint64_t
 * ========================================================================= */
int buffers_read_uint64_t_le(uint64_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint32_t lo, hi; int r;
    r = buffers_read_uint32_t_le(&lo, cb, state, bytes_read); if (r < 0) return r;
    r = buffers_read_uint32_t_le(&hi, cb, state, bytes_read); if (r < 0) return r;
    *result = ((uint64_t)hi << 32) | lo;
    return 0;
}
int buffers_write_uint64_t_le(uint64_t value, buffers_write_callback_t cb, void* state) {
    int r, total = 0;
    r = buffers_write_uint32_t_le((uint32_t)(value      ), cb, state); if (r < 0) return r; total += r;
    r = buffers_write_uint32_t_le((uint32_t)(value >> 32), cb, state); if (r < 0) return r; total += r;
    return total;
}
int buffers_read_uint64_t_be(uint64_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint32_t hi, lo; int r;
    r = buffers_read_uint32_t_be(&hi, cb, state, bytes_read); if (r < 0) return r;
    r = buffers_read_uint32_t_be(&lo, cb, state, bytes_read); if (r < 0) return r;
    *result = ((uint64_t)hi << 32) | lo;
    return 0;
}
int buffers_write_uint64_t_be(uint64_t value, buffers_write_callback_t cb, void* state) {
    int r, total = 0;
    r = buffers_write_uint32_t_be((uint32_t)(value >> 32), cb, state); if (r < 0) return r; total += r;
    r = buffers_write_uint32_t_be((uint32_t)(value      ), cb, state); if (r < 0) return r; total += r;
    return total;
}

/* =========================================================================
 * int64_t
 * ========================================================================= */
int buffers_read_int64_t_le(int64_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint64_t tmp; int r = buffers_read_uint64_t_le(&tmp, cb, state, bytes_read); if (r < 0) return r;
    *result = (int64_t)tmp; return 0;
}
int buffers_write_int64_t_le(int64_t value, buffers_write_callback_t cb, void* state) {
    return buffers_write_uint64_t_le((uint64_t)value, cb, state);
}
int buffers_read_int64_t_be(int64_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint64_t tmp; int r = buffers_read_uint64_t_be(&tmp, cb, state, bytes_read); if (r < 0) return r;
    *result = (int64_t)tmp; return 0;
}
int buffers_write_int64_t_be(int64_t value, buffers_write_callback_t cb, void* state) {
    return buffers_write_uint64_t_be((uint64_t)value, cb, state);
}

/* =========================================================================
 * float  (IEEE 754, reinterpreted as uint32_t)
 * ========================================================================= */
int buffers_read_float_le(float* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint32_t tmp; int r = buffers_read_uint32_t_le(&tmp, cb, state, bytes_read); if (r < 0) return r;
    memcpy(result, &tmp, sizeof(float)); return 0;
}
int buffers_write_float_le(float value, buffers_write_callback_t cb, void* state) {
    uint32_t tmp; memcpy(&tmp, &value, sizeof(float));
    return buffers_write_uint32_t_le(tmp, cb, state);
}
int buffers_read_float_be(float* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint32_t tmp; int r = buffers_read_uint32_t_be(&tmp, cb, state, bytes_read); if (r < 0) return r;
    memcpy(result, &tmp, sizeof(float)); return 0;
}
int buffers_write_float_be(float value, buffers_write_callback_t cb, void* state) {
    uint32_t tmp; memcpy(&tmp, &value, sizeof(float));
    return buffers_write_uint32_t_be(tmp, cb, state);
}

/* =========================================================================
 * double  (IEEE 754, reinterpreted as uint64_t)
 * ========================================================================= */
int buffers_read_double_le(double* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint64_t tmp; int r = buffers_read_uint64_t_le(&tmp, cb, state, bytes_read); if (r < 0) return r;
    memcpy(result, &tmp, sizeof(double)); return 0;
}
int buffers_write_double_le(double value, buffers_write_callback_t cb, void* state) {
    uint64_t tmp; memcpy(&tmp, &value, sizeof(double));
    return buffers_write_uint64_t_le(tmp, cb, state);
}
int buffers_read_double_be(double* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint64_t tmp; int r = buffers_read_uint64_t_be(&tmp, cb, state, bytes_read); if (r < 0) return r;
    memcpy(result, &tmp, sizeof(double)); return 0;
}
int buffers_write_double_be(double value, buffers_write_callback_t cb, void* state) {
    uint64_t tmp; memcpy(&tmp, &value, sizeof(double));
    return buffers_write_uint64_t_be(tmp, cb, state);
}

/* =========================================================================
 * Alias functions (delegate to the wire-type counterpart)
 * ========================================================================= */

int buffers_read_char(char* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_int8_t((int8_t*)r, cb, s, bytes_read); }
int buffers_write_char(char v, buffers_write_callback_t cb, void* s) {
    return buffers_write_int8_t((int8_t)v, cb, s); }

int buffers_read_unsigned_char(unsigned char* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_uint8_t((uint8_t*)r, cb, s, bytes_read); }
int buffers_write_unsigned_char(unsigned char v, buffers_write_callback_t cb, void* s) {
    return buffers_write_uint8_t((uint8_t)v, cb, s); }

int buffers_read_short_le(short* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_int16_t_le((int16_t*)r, cb, s, bytes_read); }
int buffers_write_short_le(short v, buffers_write_callback_t cb, void* s) {
    return buffers_write_int16_t_le((int16_t)v, cb, s); }

int buffers_read_unsigned_short_le(unsigned short* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_uint16_t_le((uint16_t*)r, cb, s, bytes_read); }
int buffers_write_unsigned_short_le(unsigned short v, buffers_write_callback_t cb, void* s) {
    return buffers_write_uint16_t_le((uint16_t)v, cb, s); }

int buffers_read_int_le(int* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_int32_t_le((int32_t*)r, cb, s, bytes_read); }
int buffers_write_int_le(int v, buffers_write_callback_t cb, void* s) {
    return buffers_write_int32_t_le((int32_t)v, cb, s); }

int buffers_read_unsigned_int_le(unsigned int* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_uint32_t_le((uint32_t*)r, cb, s, bytes_read); }
int buffers_write_unsigned_int_le(unsigned int v, buffers_write_callback_t cb, void* s) {
    return buffers_write_uint32_t_le((uint32_t)v, cb, s); }

int buffers_read_long_le(long* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_int32_t_le((int32_t*)r, cb, s, bytes_read); }
int buffers_write_long_le(long v, buffers_write_callback_t cb, void* s) {
    return buffers_write_int32_t_le((int32_t)v, cb, s); }

int buffers_read_unsigned_long_le(unsigned long* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_uint32_t_le((uint32_t*)r, cb, s, bytes_read); }
int buffers_write_unsigned_long_le(unsigned long v, buffers_write_callback_t cb, void* s) {
    return buffers_write_uint32_t_le((uint32_t)v, cb, s); }

int buffers_read_long_long_le(long long* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_int64_t_le((int64_t*)r, cb, s, bytes_read); }
int buffers_write_long_long_le(long long v, buffers_write_callback_t cb, void* s) {
    return buffers_write_int64_t_le((int64_t)v, cb, s); }

int buffers_read_unsigned_long_long_le(unsigned long long* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_uint64_t_le((uint64_t*)r, cb, s, bytes_read); }
int buffers_write_unsigned_long_long_le(unsigned long long v, buffers_write_callback_t cb, void* s) {
    return buffers_write_uint64_t_le((uint64_t)v, cb, s); }

int buffers_read_bool(bool* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    uint8_t tmp; int res = buffers_read_uint8_t(&tmp, cb, s, bytes_read); if (res < 0) return res;
    *r = tmp ? 1 : 0; return 0; }
int buffers_write_bool(bool v, buffers_write_callback_t cb, void* s) {
    return buffers_write_uint8_t(v ? 1 : 0, cb, s); }

int buffers_read_wchar_t_le(wchar_t* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_int16_t_le((int16_t*)r, cb, s, bytes_read); }
int buffers_write_wchar_t_le(wchar_t v, buffers_write_callback_t cb, void* s) {
    return buffers_write_int16_t_le((int16_t)v, cb, s); }

int buffers_read_size_t_le(size_t* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    uint32_t tmp; int res = buffers_read_uint32_t_le(&tmp, cb, s, bytes_read); if (res < 0) return res;
    *r = (size_t)tmp; return 0; }
int buffers_write_size_t_le(size_t v, buffers_write_callback_t cb, void* s) {
    return buffers_write_uint32_t_le((uint32_t)v, cb, s); }
