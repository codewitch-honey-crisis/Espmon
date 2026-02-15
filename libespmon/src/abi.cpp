#include "abi.hpp"
#define MONOXBOLD_IMPLEMENTATION
#include <monoxbold.hpp>
#undef MONOXBOLD_IMPLEMENTATION
#include "espmon.hpp"
using namespace gfx;
template <size_t BitDepth>
using bgra_pixel = gfx::pixel<
    gfx::channel_traits<gfx::channel_name::B, (BitDepth / 4)>,
    gfx::channel_traits<gfx::channel_name::G,
                        ((BitDepth / 4) + (BitDepth % 4))>,
    gfx::channel_traits<gfx::channel_name::R, (BitDepth / 4)>,
    gfx::channel_traits<gfx::channel_name::A, (BitDepth / 4), 0,
                        (1 << (BitDepth / 4)) - 1, (1 << (BitDepth / 4)) - 1>>;
using espmon_t = espmon<bitmap<bgra_pixel<32>>>;
espmon_handle_t Create() {
    espmon_t* result = new espmon_t();
    result->initialize();
    result->dimensions({320, 240});
    return result;
}
void Destroy(espmon_handle_t handle) {
    return delete ((espmon_t*)handle);
}
void SetTransfer(espmon_handle_t handle, void* buffer, uint32_t bufferBytes) {
    espmon_t* ths = ((espmon_t*)handle);
    ths->set_transfer(uix::screen_update_mode::direct, (uint8_t*)buffer, bufferBytes);
}
void SetDimensions(espmon_handle_t handle, uint16_t width, uint16_t height) {
    espmon_t* ths = ((espmon_t*)handle);
    ths->dimensions(size16(width, height));
}
void SetGraph(espmon_handle_t handle,uint8_t isEnabled) {
    espmon_t* ths = ((espmon_t*)handle);
    ths->has_graph(!!isEnabled);
}
void SetMonochrome(espmon_handle_t handle,uint8_t isEnabled) {
    espmon_t* ths = ((espmon_t*)handle);
    ths->is_monochrome(!!isEnabled);
}
void Update(espmon_handle_t handle, uint8_t cmd, const response_t* response, espmon_rect_t* in_dirties_buffer, uint32_t* in_out_dirties_count) {
    espmon_t* ths = ((espmon_t*)handle);
    typedef struct {
        espmon_t* ths;
        espmon_rect_t* in_dirties_buffer;
        uint32_t* in_out_dirties_count;
        size_t dirt_next;
    } state_t;
    state_t st;
    st.ths = ths;
    st.in_dirties_buffer = in_dirties_buffer;
    st.in_out_dirties_count = in_out_dirties_count;
    st.dirt_next = 0;
    ths->set_flush_callback([](const uix::rect16& bounds, const void* bmp, void* state) {
        state_t& st = *(state_t*)state;
        if (st.in_dirties_buffer != nullptr && st.in_out_dirties_count != nullptr && *st.in_out_dirties_count) {
            if (st.dirt_next == 1) {
                espmon_rect_t& r = *(espmon_rect_t*)st.in_dirties_buffer;
                size16 dim =st.ths->dimensions();
                if (r.x == 0 && r.y == 0 && r.width ==dim.width && r.height == dim.height) {
                    st.ths->transfer_complete();
                    return;
                }
            }
            if (st.dirt_next >= *st.in_out_dirties_count) {
                st.dirt_next = 1;
                espmon_rect_t& r = *(espmon_rect_t*)st.in_dirties_buffer;
                *st.in_out_dirties_count = 1;
            } else {
                espmon_rect_t r;
                r.x = bounds.left();
                r.y = bounds.top();
                r.width = bounds.width();
                r.height = bounds.height();
                ((espmon_rect_t*)st.in_dirties_buffer)[st.dirt_next++] = r;
                *st.in_out_dirties_count = st.dirt_next;
            }   
        }
        st.ths->transfer_complete();
    },&st);
    ths->accept_packet((command_t)cmd,*response);
}
void HitTest(espmon_handle_t handle,int32_t x, int32_t y, int8_t* out_hit_index) {
    espmon_t* ths = ((espmon_t*)handle);
    espmon_hit result = ths->hit_test(spoint16(x,y));
    *out_hit_index = (int8_t)result;
}

void ClearData(espmon_handle_t handle) {
    espmon_t* ths = ((espmon_t*)handle);
    ths->clear_data();
}