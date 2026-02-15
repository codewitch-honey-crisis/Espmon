#pragma once
#include <stdint.h>
#include <interface.h>
#define EXPORT __declspec(dllexport)
extern "C" {
    /// @brief A handle to the instace
    typedef void* espmon_handle_t;
    typedef struct {
        int32_t x,y,width,height;
    } espmon_rect_t;
    typedef enum {
        HIT_NONE = -1,
        HIT_TOP_LABEL,
        HIT_TOP_VALUE1,
        HIT_TOP_VALUE1_BAR,
        HIT_TOP_VALUE2,
        HIT_TOP_VALUE2_BAR,
        HIT_BOTTOM_LABEL,
        HIT_BOTTOM_VALUE1,
        HIT_BOTTOM_VALUE1_BAR,
        HIT_BOTTOM_VALUE2,
        HIT_BOTTOM_VALUE2_BAR,
        HIT_GRAPH
    } espmon_hit_t;

    /// @brief Creates a new instance
    /// @return On sucess, a handle to an instance. Null if ouf ot memory
    EXPORT espmon_handle_t Create();
    /// @brief Destroys a previous instance
    /// @param handle A handle to an instance
    EXPORT void Destroy(espmon_handle_t handle);
    /// @brief Sets the size of the render area (should match the bitmap in settransfer)
    /// @param handle The instance
    /// @param width The width in pixels
    /// @param height The height in pixels
    EXPORT void SetDimensions(espmon_handle_t handle,uint16_t width,uint16_t height);
    /// @brief Enables/Disables the graph portion of the display
    /// @param handle THe instance
    /// @param isEnabled 0 to disable, non-zero to enable
    EXPORT void SetGraph(espmon_handle_t handle,uint8_t isEnabled);
    /// @brief Enables monochrome mode
    /// @param handle The instance
    /// @param isEnabled 0 to disable, non-zero to enable
    EXPORT void SetMonochrome(espmon_handle_t handle,uint8_t isEnabled);
    /// @brief Sets the bitmap buffer to render to
    /// @param handle A handle to the instance
    /// @param buffer A buffer to set
    /// @param bufferBytes The size of the buffer in bytes
    EXPORT void SetTransfer(espmon_handle_t handle,void* buffer,uint32_t bufferBytes);
    /// @brief Updates the instance with new data, and updates the bitmap accordingly
    /// @param handle The instance
    /// @param cmd A command to send
    /// @param response The response structure to fill
    /// @param in_dirties_buffer An array of espmon_rect_ts, on out will be filled with the dirty areas that were updated (in pixels). 
    /// @param in_out_dirties_count On input, indicates the size of the dirty rect array. On output contains the count of dirty rectangles filled
    EXPORT void Update(espmon_handle_t handle,uint8_t cmd, const response_t* response, espmon_rect_t* in_dirties_buffer, uint32_t* in_out_dirties_count);
    /// @brief Indicates the part of the screen where the point lands
    /// @param handle The handle to the instance
    /// @param x The x in local coordinates (relative to the control itself, with top being 0,0)
    /// @param y The y in local coordinates
    /// @param out_hit_index The hit index our -1 if not on a meaningful portion of the display
    EXPORT void HitTest(espmon_handle_t handle,int32_t x, int32_t y, int8_t* out_hit_index);
    /// @brief Clears the history data from the graphs
    /// @param handle The handle to the instance
    EXPORT void ClearData(espmon_handle_t handle);
}