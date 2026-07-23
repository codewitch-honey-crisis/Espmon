#pragma once
#include <interface.h>
#include <math.h>
#include <memory.h>
#include <stdint.h>
#include <string.h>

#include <gfx.hpp>
#include <monoxbold.hpp>
#include <uix.hpp>

enum struct espmon_hit {
    none = -1,
    top_label,
    top_value1,
    top_value1_bar,
    top_value2,
    top_value2_bar,
    bottom_label,
    bottom_value1,
    bottom_value1_bar,
    bottom_value2,
    bottom_value2_bar,
    graph
};

template <typename ControlSurfaceType>
class vvert_label : public uix::canvas_control<ControlSurfaceType> {
    using base_type = uix::canvas_control<ControlSurfaceType>;

   public:
    using type = vvert_label;
    using control_surface_type = ControlSurfaceType;

   private:
    gfx::canvas_text_info m_label_text;
    gfx::canvas_path m_label_text_path;
    gfx::rectf m_label_text_bounds;
    bool m_label_text_dirty;
    gfx::vector_pixel m_color;
    uix::uix_pixel m_background_color;
    void build_label_path_untransformed() {
        const float target_width = this->dimensions().height * .7f;
        float fsize = this->dimensions().width;
        if (m_label_text_path.initialized()) {
            m_label_text_path.clear();
        } else {
            m_label_text_path.initialize();
        }
        do {
            m_label_text_path.clear();
            m_label_text.font_size = fsize;
            m_label_text_path.text({0.f, 0.f}, m_label_text);
            m_label_text_bounds = m_label_text_path.bounds(false);
            --fsize;

        } while (fsize > 0.f && m_label_text_bounds.width() >= target_width);
    }

   public:
    vvert_label() : base_type(), m_label_text_dirty(true) {
        m_label_text.ttf_font = &monoxbold;
        m_label_text.text_sz("Label");
        m_label_text.encoding = &gfx::text_encoding::utf8;
        m_label_text.ttf_font_face = 0;
        m_color = gfx::vector_pixel(255, 255, 255, 255);
    }
    virtual ~vvert_label() {
    }
    gfx::text_handle text() const {
        return m_label_text.text;
    }
    void text(gfx::text_handle text, size_t text_byte_count) {
        m_label_text.text = text;
        m_label_text.text_byte_count = text_byte_count;
        m_label_text_dirty = true;
        this->invalidate();
    }
    void text(const char* sz) {
        m_label_text.text_sz(sz);
        m_label_text_dirty = true;
        this->invalidate();
    }
    uix::uix_pixel color() const {
        uix::uix_pixel result;
        convert(m_color, &result);
        return result;
    }
    void color(uix::uix_pixel value) {
        convert(value, &m_color);
        this->invalidate();
    }
    uix::uix_pixel background_color() const {
        return m_background_color;
    }
    void background_color(uix::uix_pixel value) {
        m_background_color = value;
        this->invalidate();
    }

   protected:
    virtual void on_before_paint() override {
        if (m_label_text_dirty) {
            build_label_path_untransformed();
            m_label_text_dirty = false;
        }
    }
    virtual void on_paint(control_surface_type& destination, const gfx::srect16& clip) {
        if (m_background_color.opacity() != 0) {
            gfx::draw::filled_rectangle(destination, destination.bounds(), m_background_color);
        }
        base_type::on_paint(destination, clip);
    }
    virtual void on_paint(gfx::canvas& destination, const gfx::srect16& clip) override {
        gfx::canvas_style si = destination.style();
        si.fill_paint_type = gfx::paint_type::solid;
        si.stroke_paint_type = gfx::paint_type::none;
        si.fill_color = m_color;
        destination.style(si);
        // save the current transform
        gfx::matrix old = destination.transform();
        gfx::matrix m = old.rotate(gfx::math::deg2rad(-90));

        m = m.translate(-m_label_text_bounds.width() - ((destination.dimensions().height - m_label_text_bounds.width()) * 0.5f), m_label_text_bounds.height());
        destination.transform(m);
        destination.path(m_label_text_path);
        destination.render();
        destination.clear_path();
        // restore the old transform
        destination.transform(old);
    }
};

template <typename ControlSurfaceType>
class graph : public uix::control<ControlSurfaceType> {
    using base_type = uix::control<ControlSurfaceType>;

   public:
    using type = graph;
    using control_surface_type = ControlSurfaceType;
    using buffer_type = data::circular_buffer<uint8_t, 100>;

   private:
    struct data_line {
        uix::uix_pixel color;
        buffer_type* buffer;
        data_line* next;
    };
    gfx::mask_draw_cache* m_draw_cache;
    gfx::spoint16* m_point_buffer;
    data_line* m_first;
    void clear_lines() {
        data_line* entry = m_first;
        while (entry != nullptr) {
            data_line* n = entry->next;
            delete entry;
            entry = n;
        }
        m_first = nullptr;
    }

   public:
    graph() : base_type(), m_draw_cache(nullptr), m_point_buffer(nullptr), m_first(nullptr) {
    }
    virtual ~graph() {
        clear_lines();
    }
    void remove_lines() {
        clear_lines();
        this->invalidate();
    }
    bool has_lines() const {
        return m_first != nullptr;
    }
    size_t add_line(uix::uix_pixel color, buffer_type* buffer) {
        data_line* n;
        if (m_first == nullptr) {
            n = new data_line();
            if (n == nullptr) {
                return 0;  // out of memory
            }
            n->color = color;
            n->buffer = buffer;
            n->next = nullptr;
            m_first = n;
            return 1;
        }
        size_t result = 0;
        data_line* entry = m_first;
        while (entry != nullptr) {
            n = entry->next;
            if (n == nullptr) {
                n = new data_line();
                if (n == nullptr) {
                    return 0;  // out of memory
                }
                n->color = color;
                n->buffer = buffer;
                n->next = nullptr;
                entry->next = n;
                break;
            }
            entry = n;
            ++result;
        }
        this->invalidate();
        return result + 1;
    }
    uix::uix_pixel get_line(size_t index) const {
        if (m_first == nullptr) {
            return uix::uix_pixel(0, true);
        }
        data_line* entry = m_first;
        while (entry != nullptr && index-- > 0) {
            entry = entry->next;
            if (entry == nullptr) {
                return uix::uix_pixel(0, true);
            }
        }
        return entry->color;
    }
    bool set_line(size_t index, uix::uix_pixel color) {
        if (m_first == nullptr) {
            return false;
        }
        data_line* entry = m_first;
        while (entry != nullptr && index-- > 0) {
            entry = entry->next;
            if (entry == nullptr) {
                return false;
            }
        }
        entry->color = color;
        this->invalidate();
        return true;
    }
    bool add_data(size_t line_index, float value) {
        uint8_t v = gfx::math::clamp(0.f, value, 1.f) * 255;
        size_t i = 0;
        for (data_line* entry = m_first; entry != nullptr; entry = entry->next) {
            if (i == line_index) {
                if (entry->buffer->size() == entry->buffer->capacity) {
                    uint8_t tmp;
                    entry->buffer->get(&tmp);
                }
                entry->buffer->put(v);
                this->invalidate();
                return true;
            }
            ++i;
        }
        return false;
    }
    void clear_data() {
        for (data_line* entry = m_first; entry != nullptr; entry = entry->next) {
            entry->buffer->clear();
        }
        this->invalidate();
    }
    const buffer_type* buffer(size_t index) const {
        size_t i = 0;
        for (data_line* entry = m_first; entry != nullptr; entry = entry->next) {
            if (i == index) {
                return entry->buffer;
            }
            ++i;
        }
        return nullptr;
    }
    gfx::mask_draw_cache* draw_cache() {
        return m_draw_cache;
    }
    void draw_cache(gfx::mask_draw_cache* value) {
        m_draw_cache = value;
    }
    gfx::spoint16* point_buffer() {
        return m_point_buffer;
    }
    void point_buffer(gfx::spoint16* value) {
        m_point_buffer = value;
    }

   private:  // draw helpers
    // round-to-nearest of span*k/10 (span, k >= 0)
    static int grid_offset(int span, int k) {
        return (span * k + 5) / 10;
    }
    // floor of j*(aw-1)/(cap-1): x offset from ax1 (non-negative)
    static int sample_x_floor(int j, int aw, int cap) {
        if (cap <= 1) return 0;
        return (j * (aw - 1)) / (cap - 1);
    }
    // ceil of j*(aw-1)/(cap-1)
    static int sample_x_ceil(int j, int aw, int cap) {
        if (cap <= 1) return 0;
        return (j * (aw - 1) + (cap - 2)) / (cap - 1);
    }
    // floor of (255-v)*(ah-1)/255: y offset from ay1 (non-negative)
    static int value_y_floor(uint8_t v, int ah) {
        return ((255 - (int)v) * (ah - 1)) / 255;
    }
    // ceil of (255-v)*(ah-1)/255
    static int value_y_ceil(uint8_t v, int ah) {
        return ((255 - (int)v) * (ah - 1) + 254) / 255;
    }
    // round-to-nearest of (255-v)*(ah-1)/255
    static int value_y_round(uint8_t v, int ah) {
        return ((255 - (int)v) * (ah - 1) + 127) / 255;
    }

   protected:
    void on_paint(control_surface_type& destination, const gfx::srect16& clip) {
        using pixel_type = typename control_surface_type::pixel_type;
        gfx::srect16 b = (gfx::srect16)destination.bounds();
        auto px = gfx::color<pixel_type>::gray;

        gfx::draw::rectangle(destination, b, px);

        const gfx::srect16 area(b.x1 + 1, b.y1 + 1, b.x2 - 1, b.y2 - 1);
        const int ax1 = area.x1, ay1 = area.y1, ax2 = area.x2, ay2 = area.y2;
        const int aw = ax2 - ax1 + 1;
        const int ah = ay2 - ay1 + 1;
        if (aw <= 0 || ah <= 0) return;

        for (int k = 0; k <= 10; ++k) {
            int x = ax1 + grid_offset(aw - 1, k);
            gfx::draw::line(destination, gfx::srect16(x, ay1, x, ay2), px);
        }
        for (int k = 0; k <= 10; ++k) {
            int y = ay1 + grid_offset(ah - 1, k);
            gfx::draw::line(destination, gfx::srect16(ax1, y, ax2, y), px);
        }

        // Fixed step so a full buffer spans ax1..ax2 with the last sample on ax2.
        const int cap = (int)buffer_type::capacity;

        for (data_line* entry = m_first; entry != nullptr; entry = entry->next) {
            const size_t n = entry->buffer->size();
            if (!n) continue;

            // peek(0) = oldest at ax1; index grows rightward. When size == capacity
            // the newest (peek(n-1)) lands on ax2. x offset for sample j is
            // j*(aw-1)/(cap-1); y offset for value v is (255-v)*(ah-1)/255.
            // blocky lines
            if (m_point_buffer == nullptr) {
                if (n == 1) {
                    int yi = ay1 + value_y_round(*entry->buffer->peek(0), ah);
                    gfx::draw::filled_rectangle(destination,
                                                gfx::srect16(ax1, yi, ax1, yi), entry->color);
                    continue;
                }

                for (size_t j = 1; j < n; ++j) {
                    uint8_t pv = *entry->buffer->peek(j - 1);
                    uint8_t cv = *entry->buffer->peek(j);

                    // Main rect: floor left corner, ceil right (point -> next), so a
                    // small diagonal is still a whole rect. Clamp into the area.
                    int mx0 = ax1 + sample_x_floor((int)(j - 1), aw, cap);
                    if (mx0 < ax1) mx0 = ax1;
                    int my0 = ay1 + value_y_floor(pv, ah);
                    int mx1 = ax1 + sample_x_ceil((int)j, aw, cap);
                    if (mx1 > ax2) mx1 = ax2;
                    int my1 = ay1 + value_y_ceil(cv, ah);
                    gfx::draw::filled_rectangle(destination,
                                                gfx::srect16(mx0, my0, mx1, my1), entry->color);

                    // (1,1) offset copy so a flat run is 2px; clamped off the border.
                    int ox0 = mx0 + 1;
                    if (ox0 > ax2) ox0 = ax2;
                    int oy0 = my0 + 1;
                    if (oy0 > ay2) oy0 = ay2;
                    int ox1 = mx1 + 1;
                    if (ox1 > ax2) ox1 = ax2;
                    int oy1 = my1 + 1;
                    if (oy1 > ay2) oy1 = ay2;
                    gfx::draw::filled_rectangle(destination,
                                                gfx::srect16(ox0, oy0, ox1, oy1), entry->color);
                }
            } else {
                // smooth lines
                if (n < 2) continue;

                const int bx2 = (int)destination.bounds().x2;
                const int by2 = (int)destination.bounds().y2;

                uint8_t pv = *entry->buffer->peek(0);
                int mx0 = ax1 + sample_x_floor(0, aw, cap);
                if (mx0 < ax1) mx0 = ax1;
                int my0 = ay1 + value_y_floor(pv, ah);
                m_point_buffer[0] = gfx::spoint16(gfx::math::clamp(mx0, 0, bx2),
                                                  gfx::math::clamp(my0, 0, by2));
                size_t k = 1;  // number of points kept

                for (size_t j = 1; j < n; ++j) {
                    uint8_t cv = *entry->buffer->peek(j);
                    int mx1 = ax1 + sample_x_ceil((int)j, aw, cap);
                    if (mx1 > ax2) mx1 = ax2;
                    int my1 = ay1 + value_y_ceil(cv, ah);
                    const gfx::spoint16 p(gfx::math::clamp(mx1, 0, bx2),
                                          gfx::math::clamp(my1, 0, by2));

                    // exact duplicate (can happen when aw < cap): drop it
                    if (p.x == m_point_buffer[k - 1].x && p.y == m_point_buffer[k - 1].y) continue;

                    // interior of a horizontal run: extend the run rather than adding a vertex
                    if (k >= 2 && m_point_buffer[k - 1].y == p.y && m_point_buffer[k - 2].y == p.y) {
                        m_point_buffer[k - 1] = p;
                    } else {
                        m_point_buffer[k++] = p;
                    }
                }

                if (k < 2) continue;
                gfx::spath16 path(k, m_point_buffer);
                gfx::draw::aa_polyline(destination, path, entry->color,
                                       this->dimensions().height / 30,
                                       gfx::line_cap::round, gfx::line_join::round,
                                       4, m_draw_cache);
            }
        }
    }
};

template <typename ControlSurfaceType>
class bar : public uix::control<ControlSurfaceType> {
    using base_type = uix::control<ControlSurfaceType>;
    using uix_color_t = gfx::color<uix::uix_pixel>;

   public:
    using type = bar;
    using control_surface_type = ControlSurfaceType;

   private:
    uix::uix_pixel m_color;
    uix::uix_pixel m_back_color;
    bool m_is_gradient;
    float m_value;
    const data::circular_buffer<uint8_t, 100>* m_buffer;
    bool m_dark_mode;

   public:
    bar() : base_type(), m_is_gradient(false), m_value(0), m_buffer(nullptr), m_dark_mode(true) {
        static constexpr const gfx::rgb_pixel<24> px(0, 255, 0);
        static constexpr const gfx::rgb_pixel<24> black(0, 0, 0);
        convert(px, &m_color);
        uix::uix_pixel px2;
        convert(black, &px2);
        m_back_color = m_color.blend(px2, .125f);
    }

    virtual ~bar() {
    }
    float value() const {
        return m_value;
    }
    void value(float value) {
        value = gfx::math::clamp(0.f, value, 1.f);
        if (value != m_value) {
            m_value = value;
            this->invalidate();
        }
    }

    void clear() {
        if (m_buffer != nullptr) {
            this->invalidate();
        }
    }

    bool is_gradient() const {
        return m_is_gradient;
    }
    void is_gradient(bool value) {
        if (value != m_is_gradient) {
            m_is_gradient = value;
            this->invalidate();
        }
    }
    bool is_dark_mode() const {
        return m_dark_mode;
    }
    void is_dark_mode(bool value) {
        if (m_dark_mode != value) {
            m_dark_mode = value;
            this->invalidate();
        }
    }
    const data::circular_buffer<uint8_t, 100>* graph_buffer() const {
        return m_buffer;
    }
    void graph_buffer(const data::circular_buffer<uint8_t, 100>* value) {
        if (m_buffer != value) {
            m_buffer = value;
            this->invalidate();
        }
    }
    uix::uix_pixel color() const {
        return m_color;
    }
    void color(uix::uix_pixel value) {
        if (value != m_color) {
            m_color = value;
            this->invalidate();
        }
    }
    uix::uix_pixel back_color() const {
        return m_back_color;
    }
    void back_color(uix::uix_pixel value) {
        if (m_back_color != value) {
            m_back_color = value;
            this->invalidate();
        }
    }

   protected:
    virtual void on_paint(control_surface_type& destination, const gfx::srect16& clip) {
        typename control_surface_type::pixel_type scr_bg;
        destination.point({0, 0}, &scr_bg);
        uint16_t x_end = roundf(m_value * destination.dimensions().width - 1);
        uint16_t y_end = destination.dimensions().height - 1;
        if (m_is_gradient) {
            y_end = destination.dimensions().height * .6666;
            // two reference points for the ends of the graph
            gfx::hsva_pixel<32> px = gfx::color<gfx::hsva_pixel<32>>::red;
            gfx::hsva_pixel<32> px2 = gfx::color<gfx::hsva_pixel<32>>::green;
            auto h1 = px.channel<gfx::channel_name::H>();
            auto h2 = px2.channel<gfx::channel_name::H>();
            h2 -= 24;
            // the actual range we're drawing
            auto range = abs(h2 - h1) + 1;
            // the width of each gradient segment
            int w = (int)ceilf(destination.dimensions().width /
                               (float)range) +
                    1;
            // the step of each segment - default 1
            int s = 1;
            // if the gradient is larger than the control
            if (destination.dimensions().width < range) {
                // change the segment to width 1
                w = 1;
                // and make its step larger
                s = range / (float)destination.dimensions().width;
            }
            int x = 0;
            // c is the current color offset
            // it increases by s (step)
            int c = 0;
            // for each color in the range
            for (auto j = 0; j < range; ++j) {
                // adjust the H value (inverted and offset)
                px.channel<gfx::channel_name::H>(range - c - 1 + h1);
                // if we're drawing the filled part
                // it's fully opaque
                // otherwise it's semi-transparent
                int sw = w;
                int diff = 0;
                if (m_value == 0 || x > x_end) {
                    px.channel<gfx::channel_name::A>(95);
                    if ((x - w) <= x_end) {
                        sw = x_end - x + 1;
                        diff = w - sw;
                    }
                } else {
                    px.channel<gfx::channel_name::A>(255);
                }
                // create the rect for our segment
                gfx::srect16 r(x, y_end + 1, x + sw, destination.dimensions().height - 1);

                // black out the area underneath so alpha blending
                // works correctly
                gfx::draw::filled_rectangle(destination,
                                            r,
                                            scr_bg);
                // draw the segment
                gfx::draw::filled_rectangle(destination,
                                            r,
                                            px);
                if (diff > 0) {
                    r = gfx::srect16(x + sw, y_end + 1, x + w, destination.dimensions().height - 1);
                    gfx::draw::filled_rectangle(destination,
                                                r,
                                                scr_bg);
                    // draw the segment
                    gfx::draw::filled_rectangle(destination,
                                                r,
                                                px);
                }
                // increment
                x += w;
                c += s;
            }
        }
        if (m_value > 0) {
            gfx::draw::filled_rectangle(destination, gfx::srect16(0, 0, x_end, y_end), m_color);
            gfx::draw::filled_rectangle(destination, gfx::srect16(x_end + 1, 0, destination.dimensions().width - 1, y_end), m_back_color);
        } else {
            gfx::draw::filled_rectangle(destination, gfx::srect16(0, 0, destination.dimensions().width - 1, y_end), m_back_color);
        }
        if (m_buffer != nullptr) {
            if (m_buffer->size() > 0) {
                float x_step = (destination.dimensions().width - 1) / (float)(data::circular_buffer<uint8_t, 100>::capacity - 1);
                float x = x_step;
                uint8_t initial = 255 - *m_buffer->peek(0);
                gfx::point16 opt(0, initial * (y_end) / 255);
                size_t i = 1;
                auto px = (m_value == 0) ? m_color : m_dark_mode ? uix_color_t::black
                                                                 : uix_color_t::white;
                while (i < m_buffer->size()) {
                    uint8_t v = 255 - *m_buffer->peek(i);
                    gfx::point16 pt(x, v * (y_end) / 255);
                    gfx::draw::filled_rectangle(destination, gfx::rect16(opt, pt), px);
                    if (x > x_end) {
                        px = m_color;
                    }
                    opt = pt;
                    x += x_step;
                    ++i;
                }
            }
        }
    }
};

template <typename BitmapType, uint8_t HorizontalAlignment = 1, uint8_t VerticalAlignment = 1>
class espmon {
    using pixel_t = typename BitmapType::pixel_type;
    using palette_t = typename BitmapType::palette_type;
    using screen_t = uix::screen_ex<BitmapType, HorizontalAlignment, VerticalAlignment>;
    using control_surface_t = typename screen_t::control_surface_type;
    using color_t = gfx::color<typename BitmapType::pixel_type>;
    using uix_color_t = gfx::color<uix::uix_pixel>;
    using vcolor_t = gfx::color<gfx::vector_pixel>;
    using bar_t = bar<typename screen_t::control_surface_type>;
    using vert_label_t = vvert_label<typename screen_t::control_surface_type>;
    using label_t = uix::vlabel<typename screen_t::control_surface_type>;
    using graph_t = graph<typename screen_t::control_surface_type>;
    using graph_buffer_t = typename graph_t::buffer_type;
    static constexpr const size_t hit_boxes_size = ((size_t)espmon_hit::graph) + 1;
    gfx::srect16 m_hit_boxes[hit_boxes_size];
    uix::display m_display;
    gfx::const_buffer_stream m_font;
    bool m_has_graph;
    bool m_is_monochrome;
    bool m_is_screen_populated;
    bool m_is_connected;
    gfx::size16 m_display_size;
    screen_t m_screen;
    screen_t m_disconnected_screen;
    typedef struct {
        char value_buffer[16];
        char suffix_buffer[12];
        bar_t bar;
        size_t index;
        label_t label;
        vert_label_t vsuffix;
    } value_entry_t;
    typedef struct {
        char text[12];
        vert_label_t label;
        label_t hlabel;
        value_entry_t value1;
        value_entry_t value2;
    } screen_entry_t;
    screen_entry_t m_top;
    screen_entry_t m_bottom;
    label_t m_disconnected_label;
    int8_t m_screen_index = -1;
    graph_buffer_t m_buffers[4];
    gfx::spoint16 m_points[graph_buffer_t::capacity];
    gfx::mask_draw_cache m_draw_cache;
    graph_t m_graph;

    void refresh_display(bool full = true) {
        while (m_display.dirty()) {
            m_display.update();
            if (!full) {
                break;
            }
        }
    }
    void init_disconnected_screen() {
        m_disconnected_label.bounds(gfx::srect16(0, 0, m_disconnected_screen.dimensions().width / 2, m_disconnected_screen.dimensions().width / 8).center(m_disconnected_screen.bounds()));
        uix::uix_pixel bg = uix_color_t::black;
        m_disconnected_label.font(m_top.value1.label.font());
        m_disconnected_label.color(uix_color_t::white);
        m_disconnected_label.background_color(bg);
        m_disconnected_label.text("[ disconnected ]");
        m_disconnected_label.text_justify(uix::uix_justify::center);
        m_disconnected_screen.register_control(m_disconnected_label);
    }
    void init_screen() {
        m_screen.unregister_controls();
        int section_height_divisor = m_has_graph ? 4 : 2;
        m_screen.background_color(color_t::black);
        if (m_has_graph) {
            uix::srect16 b = m_screen.bounds();
            b.y1 = m_screen.dimensions().height / 2 + 1;
            m_graph.bounds(b);
            m_graph.point_buffer(m_points);
            m_graph.draw_cache(&m_draw_cache);
            m_hit_boxes[(size_t)espmon_hit::graph] = m_graph.bounds();
            if (!m_graph.has_lines()) {
                m_graph.add_line(uix_color_t::green, &m_buffers[0]);
                m_graph.add_line(uix_color_t::orange, &m_buffers[1]);
                m_graph.add_line(uix_color_t::white, &m_buffers[2]);
                m_graph.add_line(uix_color_t::purple, &m_buffers[3]);
            }
        }
        m_top.label.bounds(gfx::srect16(0, 0, (m_screen.dimensions().width) / 10 - 1, m_screen.dimensions().height / section_height_divisor).inflate(-2, -4));
        m_hit_boxes[(size_t)espmon_hit::top_label] = m_top.label.bounds();
        m_top.label.text("---");
        m_top.label.background_color(uix_color_t::black);
        m_top.label.color(uix_color_t::white);
        m_screen.register_control(m_top.label);
        gfx::srect16 b = m_top.label.bounds();
        int16_t sh = m_screen.dimensions().height / section_height_divisor;
        m_top.hlabel.bounds(gfx::srect16(0, m_top.label.bounds().y1, b.x2, sh).inflate(-2, -4));
        m_top.hlabel.text("---");
        m_top.hlabel.background_color(uix_color_t::black);
        m_top.hlabel.color(uix_color_t::white);
        m_top.hlabel.text_justify(uix::uix_justify::center);
        m_screen.register_control(m_top.hlabel);
        m_top.hlabel.visible(sh <= m_top.label.dimensions().width ? true : false);
        m_top.label.visible(!m_top.hlabel.visible());
        gfx::srect16 vb(b.x2 + 2, b.y1, b.x2 + 1 + (m_screen.dimensions().width / 5), b.height() / 2 + b.y1);
        m_top.value1.label.bounds(vb);
        m_hit_boxes[(size_t)espmon_hit::top_value1] = m_top.value1.label.bounds();
        m_top.value1.label.font(monoxbold);
        m_top.value1.label.color(uix_color_t::white);
        m_top.value1.label.text_justify(uix::uix_justify::center_right);
        strcpy(m_top.value1.value_buffer, "---");
        m_top.value1.label.text(m_top.value1.value_buffer);
        m_screen.register_control(m_top.value1.label);

        m_top.value1.vsuffix.bounds(gfx::srect16(vb.x2 - vb.height() / 3 + 2, vb.y1, vb.x2, vb.y1 + m_top.value1.label.dimensions().height - 1));
        m_top.value1.vsuffix.text("---");
        m_top.value1.vsuffix.color(m_top.value1.label.color());
        m_top.value1.vsuffix.background_color(uix_color_t::black);
        m_top.value1.vsuffix.visible(false);
        m_screen.register_control(m_top.value1.vsuffix);

        b = m_top.value1.label.bounds();
        vb = b.offset(0, b.height() + 1);
        m_top.value2.label.bounds(vb);
        m_hit_boxes[(size_t)espmon_hit::top_value2] = m_top.value2.label.bounds();
        m_top.value2.label.font(m_top.value1.label.font());
        m_top.value2.label.color(uix_color_t::white);
        m_top.value2.label.text_justify(uix::uix_justify::center_right);
        strcpy(m_top.value2.value_buffer, "---");
        m_top.value2.label.text(m_top.value2.value_buffer);
        m_screen.register_control(m_top.value2.label);

        m_top.value2.vsuffix.bounds(gfx::srect16(vb.x2 - vb.height() / 3 + 2, vb.y1, vb.x2, vb.y1 + m_top.value1.label.dimensions().height - 1));
        m_top.value2.vsuffix.text("---");
        m_top.value2.vsuffix.color(m_top.value2.label.color());
        m_top.value2.vsuffix.background_color(uix_color_t::black);
        m_top.value2.vsuffix.visible(false);
        m_screen.register_control(m_top.value2.vsuffix);

        b = m_top.value1.label.bounds();
        b.x1 = b.x2 + 4;
        b.x2 = m_screen.dimensions().width - 1;
        b.y2 -= 2;
        m_top.value1.bar.bounds(b);
        m_hit_boxes[(size_t)espmon_hit::top_value1_bar] = m_top.value1.bar.bounds();
        m_top.value1.bar.back_color(uix_color_t::black);
        if (m_has_graph) {
            m_top.value1.bar.graph_buffer(&m_buffers[0]);
        }
        m_top.value1.bar.color(m_has_graph ? m_graph.get_line(0) : uix_color_t::green);
        m_screen.register_control(m_top.value1.bar);

        b = m_top.value2.label.bounds();
        b.x1 = b.x2 + 4;
        b.x2 = m_screen.dimensions().width - 1;
        b.y2 -= 2;
        m_top.value2.bar.bounds(b);
        m_hit_boxes[(size_t)espmon_hit::top_value2_bar] = m_top.value2.bar.bounds();
        auto px = uix_color_t::white;
        if (m_has_graph) {
            m_top.value2.bar.graph_buffer(&m_buffers[1]);
        }
        m_top.value2.bar.color(m_has_graph ? m_graph.get_line(1) : uix_color_t::orange);
        m_top.value2.bar.is_gradient(true);
        m_top.value2.bar.back_color(uix_color_t::black);
        m_screen.register_control(m_top.value2.bar);

        m_bottom.label.bounds(m_top.label.bounds().offset(0, m_screen.dimensions().height / section_height_divisor + 3));
        m_hit_boxes[(size_t)espmon_hit::bottom_label] = m_bottom.label.bounds();
        m_bottom.label.color(uix_color_t::white);
        m_bottom.label.background_color(uix_color_t::black);
        m_bottom.label.text("---");
        m_screen.register_control(m_bottom.label);
        m_bottom.value1.label.bounds(m_top.value1.label.bounds().offset(0, m_screen.dimensions().height / section_height_divisor));
        m_hit_boxes[(size_t)espmon_hit::bottom_value1] = m_bottom.value1.label.bounds();
        b = m_top.value2.label.bounds();
        sh = m_screen.dimensions().height / section_height_divisor;
        m_bottom.hlabel.bounds(gfx::srect16(0, m_bottom.label.bounds().y1, b.x2, sh).inflate(-2, -4));
        m_bottom.hlabel.text("---");
        m_bottom.hlabel.background_color(uix_color_t::black);
        m_bottom.hlabel.color(uix_color_t::white);
        m_bottom.hlabel.text_justify(uix::uix_justify::center);
        m_screen.register_control(m_bottom.hlabel);
        m_bottom.hlabel.visible(sh <= m_bottom.label.dimensions().width ? true : false);
        m_bottom.label.visible(!m_bottom.hlabel.visible());

        vb = b.offset(0, b.height() + 1);
        m_bottom.value1.label.font(m_top.value1.label.font());
        m_bottom.value1.label.color(uix_color_t::white);
        m_bottom.value1.label.text_justify(uix::uix_justify::center_right);
        strcpy(m_bottom.value1.value_buffer, "---");
        m_bottom.value1.label.text(m_bottom.value1.value_buffer);
        m_bottom.value1.vsuffix.bounds(gfx::srect16(vb.x2 - vb.height() / 3 + 2, vb.y1, vb.x2, vb.y1 + m_top.value1.label.dimensions().height - 1));
        m_bottom.value1.vsuffix.text("---");
        m_bottom.value1.vsuffix.color(m_bottom.value1.label.color());
        m_bottom.value1.vsuffix.background_color(uix_color_t::black);
        m_bottom.value1.vsuffix.visible(false);
        m_screen.register_control(m_bottom.value1.vsuffix);

        m_screen.register_control(m_bottom.value1.label);
        b = m_bottom.value1.label.bounds();
        vb = b.offset(0, b.height() + 1);
        m_bottom.value2.label.bounds(vb);
        m_hit_boxes[(size_t)espmon_hit::bottom_value2] = m_bottom.value2.label.bounds();
        m_bottom.value2.label.color(uix_color_t::white);
        m_bottom.value2.label.text_justify(uix::uix_justify::center_right);
        m_bottom.value2.label.font(m_bottom.value1.label.font());
        strcpy(m_bottom.value2.value_buffer, "---");
        m_bottom.value2.label.text(m_bottom.value2.value_buffer);
        m_screen.register_control(m_bottom.value2.label);
        m_bottom.value2.vsuffix.bounds(gfx::srect16(vb.x2 - vb.height() / 3 + 2, vb.y1, vb.x2, vb.y1 + m_top.value1.label.dimensions().height - 1));
        m_bottom.value2.vsuffix.text("---");
        m_bottom.value2.vsuffix.color(m_bottom.value2.label.color());
        m_bottom.value2.vsuffix.background_color(uix_color_t::black);
        m_bottom.value2.vsuffix.visible(false);
        m_screen.register_control(m_bottom.value2.vsuffix);

        b = m_bottom.value1.label.bounds();
        b.x1 = b.x2 + 4;
        b.x2 = m_screen.dimensions().width - 1;
        b.y2 -= 2;
        m_bottom.value1.bar.bounds(b);
        m_hit_boxes[(size_t)espmon_hit::bottom_value1_bar] = m_bottom.value1.bar.bounds();
        if (m_has_graph) {
            m_bottom.value1.bar.graph_buffer(&m_buffers[2]);
        }
        m_bottom.value1.bar.color(m_has_graph ? m_graph.get_line(2) : uix_color_t::white);
        m_bottom.value1.bar.back_color(uix_color_t::black);
        m_screen.register_control(m_bottom.value1.bar);

        b = m_bottom.value2.label.bounds();
        b.x1 = b.x2 + 4;
        b.x2 = m_screen.dimensions().width - 1;
        b.y2 -= 2;
        m_bottom.value2.bar.bounds(b);
        m_hit_boxes[(size_t)espmon_hit::bottom_value2_bar] = m_bottom.value2.bar.bounds();
        px = uix_color_t::white;
        m_bottom.value2.bar.back_color(uix_color_t::black);
        if (m_has_graph) {
            m_bottom.value2.bar.graph_buffer(&m_buffers[3]);
        }
        m_bottom.value2.bar.color(m_has_graph ? m_graph.get_line(3) : uix_color_t::purple);
        m_bottom.value2.bar.is_gradient(true);
        m_screen.register_control(m_bottom.value2.bar);

        if (m_has_graph) {
            m_screen.register_control(m_graph);
        }
        m_display.active_screen(m_screen);
        m_is_screen_populated = false;
    }

    static uix::uix_pixel to_color(const response_color_t& col) {
        return uix::uix_pixel(col.r, col.g, col.b, col.a);
    }
    static void format_float(float expr, char* buffer, size_t size) {
        memset(buffer, 0, size);
        if (isnan(expr)) {
            if (size > 4) size = 4;
            for (int i = 0; i < size - 1; ++i) {
                buffer[i] = '-';
            }
            // buffer[size]=0;
            return;
        }
        snprintf(buffer, size - 1, "%0.2f", expr);
        size_t len = strlen(buffer);
        buffer[len] = 0;
        for (size_t i = len - 1; i > 0; --i) {
            char ch = buffer[i];
            if (ch == '0' || ch == '.') {
                buffer[i] = '\0';
                if (ch == '.') {
                    break;
                }
            } else if (ch != '\0') {
                break;
            }
        }
    }
    size_t utf8_len(const char* text) {
        int32_t cp;
        const uint8_t* data = (const uint8_t*)text;
        size_t result = 0;
        while (*data) {
            size_t l = 3;  // max num chars per codepoint
            if (::gfx::gfx_result::success != gfx::text_encoding::utf8.to_utf32((::gfx::text_handle)data, &cp, &l)) {
                return result;
            }
            data += l;
            ++result;
        }
        return result;
    }
    void set_screen_value_entry(value_entry_t& entry, size_t index, bool vert, const response_screen_value_entry_t& rentry) {
        entry.index = index;
        entry.bar.value(0);
        entry.label.text("---");
        strncpy(entry.suffix_buffer, rentry.suffix, sizeof(entry.suffix_buffer) - 1);
        if (vert) {
            gfx::srect16 b = entry.label.bounds();
            gfx::srect16 bv = entry.vsuffix.bounds();
            entry.label.bounds(gfx::srect16(b.x1, b.y1, bv.x1 - 2, b.y2));
            entry.vsuffix.visible(true);
            entry.vsuffix.text(entry.suffix_buffer);
        } else {
            gfx::srect16 b = entry.label.bounds();
            gfx::srect16 bv = entry.vsuffix.bounds();
            entry.label.bounds(gfx::srect16(b.x1, b.y1, bv.x2 - 2, b.y2));
            entry.vsuffix.text("");
            entry.vsuffix.visible(false);
        }
        auto col = to_color(rentry.color);
        entry.bar.color(col);
        if (m_has_graph) {
            m_graph.set_line(entry.index, col);
        }
        if (!m_is_monochrome) {
            entry.bar.back_color(col.blend(uix_color_t::black, .25));
        } else {
            entry.bar.back_color(uix_color_t::black);
        }
    }
    void set_gradients(const response_screen_t& rscr) {
        m_top.value1.bar.is_gradient((rscr.header.flags & (1 << 0)));
        m_top.value2.bar.is_gradient((rscr.header.flags & (1 << 1)));
        m_bottom.value1.bar.is_gradient((rscr.header.flags & (1 << 2)));
        m_bottom.value2.bar.is_gradient((rscr.header.flags & (1 << 3)));
    }
    void set_screen_entry(screen_entry_t& entry, const response_screen_entry_t& rentry) {
        if (0 != strcmp(entry.text, rentry.label)) {
            strncpy(entry.text, rentry.label, sizeof(entry.text) - 1);
            entry.label.text(entry.text);
            entry.hlabel.text(entry.text);
        }
        uix::uix_pixel col = to_color(rentry.color);
        if (col != entry.label.color()) {
            entry.label.color(col);
            entry.hlabel.color(col);
        }
    }

   public:
    espmon() {
        m_has_graph = true;
        m_is_monochrome = false;
        m_is_screen_populated = false;
        m_screen_index = -1;
    }
    // application defined use
    void* user_ctx;
    bool is_screen_populated() const {
        return m_is_screen_populated;
    }
    int8_t screen_index() const {
        return m_screen_index;
    }
    bool has_graph() const {
        return m_has_graph;
    }
    void has_graph(bool value) {
        if (m_has_graph != value) {
            m_has_graph = value;
            init_screen();
            m_is_screen_populated = false;
            m_screen.validate_all();
            m_screen.invalidate();
        }
    }
    bool is_monochrome() const {
        return m_is_monochrome;
    }
    void is_monochrome(bool value) {
        if (m_is_monochrome != value) {
            m_is_monochrome = value;
            m_is_screen_populated = false;
            m_screen.validate_all();
            m_screen.invalidate();
        }
    }
    espmon_hit hit_test(gfx::spoint16 pt) {
        for (size_t i = 0; i < hit_boxes_size - (!has_graph()); ++i) {
            if (m_hit_boxes[i].intersects(pt)) {
                return (espmon_hit)i;
            }
        }
        return espmon_hit::none;
    }
    void dimensions(gfx::size16 dimensions) {
        if (dimensions != m_display_size) {
            m_draw_cache.release();
            m_screen.unregister_controls();
            m_screen.dimensions((gfx::ssize16)dimensions);
            m_draw_cache.ensure((size_t)dimensions.width);
            m_display_size = dimensions;
            init_screen();
            m_is_screen_populated = false;
            m_screen.update_strategy(uix::screen_update_strategy::balanced);
            m_screen.validate_all();
            m_screen.invalidate();

            m_disconnected_screen.unregister_controls();
            m_disconnected_screen.dimensions((gfx::ssize16)dimensions);
            init_disconnected_screen();
            m_disconnected_screen.validate_all();
            m_disconnected_screen.invalidate();
        }
    }
    uix::size16 dimensions() const {
        return m_display_size;
    }
    void transfer_complete() { m_display.flush_complete(); }
    void disconnect() {
        m_is_connected = false;
        m_display.active_screen(m_disconnected_screen);
    }
    bool connected() const {
        return m_is_connected;
    }
    void refresh(bool full = true) {
        refresh_display(full);
    }
    bool is_dirty() const {
        return m_display.dirty();
    }
    void clear_data() {
        m_graph.clear_data();
        m_top.value1.bar.clear();
        m_top.value2.bar.clear();
        m_bottom.value1.bar.clear();
        m_bottom.value2.bar.clear();
    }
    void accept_packet(command_t cmd, const response_t& resp, bool refresh = true) {
        float v;
        if (!m_is_connected) {
            m_display.active_screen(m_screen);
            m_is_connected = true;
            if (refresh) {
                refresh_display();
            }
            m_is_screen_populated = false;
        }
        if (cmd == CMD_SCREEN) {  // new screen
            const response_screen_t& scr = resp.screen;

            m_screen_index = scr.header.index;
            uint8_t flags = scr.header.flags;
            if (m_is_monochrome) {
                flags &= 0xF0;  // turn off gradients for monochrome displays
            }

            static const size_t vlen = 2;
            bool is_vert;
            size_t vert[] = {
                utf8_len(scr.top.value1.suffix),
                utf8_len(scr.top.value2.suffix),
                utf8_len(scr.bottom.value1.suffix),
                utf8_len(scr.bottom.value2.suffix)};
            is_vert = (vert[0] > vlen) || (vert[1] > vlen) || (vert[2] > vlen) || (vert[3] > vlen);
            m_screen_index = scr.header.index;

            set_screen_entry(m_top, scr.top);
            set_screen_value_entry(m_top.value1, 0, is_vert && (vert[0] > 1), scr.top.value1);
            set_screen_value_entry(m_top.value2, 1, is_vert && (vert[1] > 1), scr.top.value2);

            set_screen_entry(m_bottom, scr.bottom);
            set_screen_value_entry(m_bottom.value1, 2, is_vert && (vert[2] > 1), scr.bottom.value1);
            set_screen_value_entry(m_bottom.value2, 3, is_vert && (vert[3] > 1), scr.bottom.value2);

            set_gradients(scr);
            if (!m_is_screen_populated) {
                clear_data();
            }
            m_is_screen_populated = true;

            m_graph.set_line(0, to_color(scr.top.value1.color));
            m_graph.set_line(1, to_color(scr.top.value2.color));
            m_graph.set_line(2, to_color(scr.bottom.value1.color));
            m_graph.set_line(3, to_color(scr.bottom.value2.color));
            m_screen.validate_all();
            m_screen.invalidate();
            if (refresh) {
                refresh_display();
            }
        }
        if (cmd == CMD_DATA) {  // screen data
            const response_data_t& data = resp.data;
            v = data.top.value1.scaled;
            if (isnan(v)) {
                v = 0.f;
            }
            if (m_has_graph) {
                m_graph.add_data(0, v);
            }
            format_float(data.top.value1.value, m_top.value1.value_buffer, sizeof(m_top.value1.value_buffer));
            if (!m_top.value1.vsuffix.visible()) {
                size_t len = strlen(m_top.value1.value_buffer);
                strncpy(m_top.value1.value_buffer + len, m_top.value1.suffix_buffer, sizeof(m_top.value1.value_buffer) - len);
            }
            m_top.value1.label.text(m_top.value1.value_buffer);
            m_top.value1.bar.value(v);
            m_top.value1.bar.invalidate();
            v = data.top.value2.scaled;
            if (isnan(v)) {
                v = 0.f;
            }
            if (m_has_graph) {
                m_graph.add_data(1, v);
            }
            format_float(data.top.value2.value, m_top.value2.value_buffer, sizeof(m_top.value2.value_buffer));
            if (!m_top.value2.vsuffix.visible()) {
                size_t len = strlen(m_top.value2.value_buffer);
                strncpy(m_top.value2.value_buffer + len, m_top.value2.suffix_buffer, sizeof(m_top.value2.value_buffer) - len);
            }
            m_top.value2.label.text(m_top.value2.value_buffer);
            m_top.value2.bar.value(v);
            m_top.value2.bar.invalidate();
            v = data.bottom.value1.scaled;
            if (isnan(v)) {
                v = 0.f;
            }
            if (m_has_graph) {
                m_graph.add_data(2, v);
            }
            format_float(data.bottom.value1.value, m_bottom.value1.value_buffer, sizeof(m_bottom.value1.value_buffer));
            if (!m_bottom.value1.vsuffix.visible()) {
                size_t len = strlen(m_bottom.value1.value_buffer);
                strncpy(m_bottom.value1.value_buffer + len, m_bottom.value1.suffix_buffer, sizeof(m_bottom.value1.value_buffer) - len);
            }
            m_bottom.value1.label.text(m_bottom.value1.value_buffer);
            m_bottom.value1.bar.value(v);
            m_bottom.value1.bar.invalidate();
            v = data.bottom.value2.scaled;
            if (isnan(v)) {
                v = 0.f;
            }
            if (m_has_graph) {
                m_graph.add_data(3, v);
            }
            format_float(data.bottom.value2.value, m_bottom.value2.value_buffer, sizeof(m_bottom.value2.value_buffer));
            if (!m_bottom.value2.vsuffix.visible()) {
                size_t len = strlen(m_bottom.value2.value_buffer);
                strncpy(m_bottom.value2.value_buffer + len, m_bottom.value2.suffix_buffer, sizeof(m_bottom.value2.value_buffer) - len);
            }
            m_bottom.value2.label.text(m_bottom.value2.value_buffer);
            m_bottom.value2.bar.value(v);
            m_bottom.value2.bar.invalidate();
            if (refresh) {
                refresh_display();
            }
        }
        if (cmd == CMD_CLEAR) {
            clear_data();
            if (refresh) {
                refresh_display();
            }
        }
    }
    void set_flush_callback(uix::screen_base::on_flush_callback_type flush_callback, void* flush_state = nullptr) {
        m_display.on_flush_callback(flush_callback, flush_state);
        m_display.active_screen(m_screen);
    }
    void set_transfer(uix::screen_update_mode update_mode, uint8_t* buffer1, size_t buffer_size, uint8_t* buffer2 = nullptr) {
        m_display.update_mode(update_mode);
        m_display.buffer_size(buffer_size);
        m_display.buffer1(buffer1);
        m_display.buffer2(buffer2);
        if (m_is_connected) {
            m_display.active_screen(m_screen);
        } else {
            m_display.active_screen(m_disconnected_screen);
        }
    }
    void initialize() {
        init_screen();
        init_disconnected_screen();
        m_is_connected = false;
        m_display.active_screen(m_disconnected_screen);
    }
};