#ifndef HTCW_FRAME_H
#define HTCW_FRAME_H
#include <stddef.h>
#include <stdint.h>
#include <stdbool.h>
#ifdef __cplusplus
extern "C" {
#endif
enum {
    /// @brief An invalid argument was passed
    FRAME_ERROR_ARG = -5,
    /// @brief There was not enough data to complete the request (deprecated)
    FRAME_ERROR_UNDERFLOW = -4,
    /// @brief The frame received length is too large
    FRAME_ERROR_OVERFLOW = -3,
    /// @brief The frame CRC did not match
    FRAME_ERROR_CRC = -2,
    /// @brief The end of a stream was found
    FRAME_EOF = -1,
    /// @brief The operation completed successfully
    FRAME_SUCCESS   =  0
};
/// @brief The callback used to read a byte from the transport
typedef int(*frame_read_callback_t)(void* state);
/// @brief The callback used to write a byte to the transport
typedef int(*frame_write_callback_t)(uint8_t value, void* state);
/// @brief A handle to a frame controller
typedef void* frame_handle_t;
/// @brief Creates a frame controller
/// @param max_payload_size The maximum size of a payload for a frame. This should be at least the size of the largest message to be received
/// @param on_read_callback The read callback used to read a byte from the transport
/// @param on_read_callback_state User defined state to pass to the read callback
/// @param on_write_callback The write callback used to write a byte to the transport
/// @param on_write_callback_state User defined state to pass to the write callback
/// @return A frame handle, or NULL on error (out of memory or invalid arg)
frame_handle_t frame_create(size_t max_payload_size,
    frame_read_callback_t on_read_callback, void* on_read_callback_state,
    frame_write_callback_t on_write_callback, void* on_write_callback_state
);
/// @brief Releases the resources used by a frame controller
/// @param handle The handle to destroy
void frame_destroy(frame_handle_t handle);
/// @brief Attempts to retrieve the next waiting frame in the buffer.
/// @param handle The handle to the frame controller
/// @param out_data A pointer to the payload data recieved. The frame controller handles the lifetime.
/// @param out_size The size of the payload data raceived.
/// @return 0 = no frame waiting. < 0 = error. > 0 = frame marker cmd byte
/// @remarks This method should be called repeatedly in a loop as doing so feeds bytes in from the transport as they become available, and this pops once a complete frame is received
int frame_get(frame_handle_t handle, void** out_data, size_t* out_size);
/// @brief Unconditionally discards the next waiting frame
/// @param handle The handle to the frame controller
/// @return 0 on success. < 0 on error (frame handle invalid)
int frame_discard(frame_handle_t handle);
/// @brief Writes a frame to the transport
/// @param handle The handle to the frame controller
/// @param cmd The cmd marker byte to write (1-127)
/// @param payload The payload to send
/// @param size The size of the payload
/// @return >= 0 success. Otherwise, error.
int frame_put(frame_handle_t handle, uint8_t cmd, const void* payload, size_t size);
#ifdef __cplusplus
}
#endif
#endif // HTCW_FRAME_H