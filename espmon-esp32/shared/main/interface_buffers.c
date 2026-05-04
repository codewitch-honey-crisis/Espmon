#include "buffers.h"
#include "interface_buffers.h"

static int read_input_type_t(input_type_t* e, buffers_read_callback_t on_read, void* on_read_state, int* bytes_read) {
    uint8_t tmp;
    int res = buffers_read_uint8_t(&tmp, on_read, on_read_state, bytes_read);
    if(res < 0) { return res; }
    *e = (input_type_t)tmp;
    return 0;
}

static int write_input_type_t(input_type_t e, buffers_write_callback_t on_write, void* on_write_state) {
    uint8_t tmp = (uint8_t)e;
    return buffers_write_uint8_t(tmp, on_write, on_write_state);
}

int response_value_read(response_value_t* s, buffers_read_callback_t on_read, void* on_read_state) {
    int res;
    int bytes_read = 0;
    res = buffers_read_float_le(&s->value, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    res = buffers_read_float_le(&s->scaled, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    return bytes_read;
}

int response_value_write(const response_value_t* s, buffers_write_callback_t on_write, void* on_write_state) {
    int res;
    int total = 0;
    res = buffers_write_float_le(s->value, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = buffers_write_float_le(s->scaled, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    return total;
}

size_t response_value_size(const response_value_t* s) {
    size_t size = 0;
    size += 4;
    size += 4;
    return size;
}

int response_value_entry_read(response_value_entry_t* s, buffers_read_callback_t on_read, void* on_read_state) {
    int res;
    int bytes_read = 0;
    res = response_value_read(&s->value1, on_read, on_read_state);
    if(res < 0) { return res; }
    bytes_read += res;
    res = response_value_read(&s->value2, on_read, on_read_state);
    if(res < 0) { return res; }
    bytes_read += res;
    return bytes_read;
}

int response_value_entry_write(const response_value_entry_t* s, buffers_write_callback_t on_write, void* on_write_state) {
    int res;
    int total = 0;
    res = response_value_write(&s->value1, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = response_value_write(&s->value2, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    return total;
}

size_t response_value_entry_size(const response_value_entry_t* s) {
    size_t size = 0;
    size += response_value_size(&s->value1);
    size += response_value_size(&s->value2);
    return size;
}

int response_data_read(response_data_t* s, buffers_read_callback_t on_read, void* on_read_state) {
    int res;
    int bytes_read = 0;
    res = response_value_entry_read(&s->top, on_read, on_read_state);
    if(res < 0) { return res; }
    bytes_read += res;
    res = response_value_entry_read(&s->bottom, on_read, on_read_state);
    if(res < 0) { return res; }
    bytes_read += res;
    return bytes_read;
}

int response_data_write(const response_data_t* s, buffers_write_callback_t on_write, void* on_write_state) {
    int res;
    int total = 0;
    res = response_value_entry_write(&s->top, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = response_value_entry_write(&s->bottom, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    return total;
}

size_t response_data_size(const response_data_t* s) {
    size_t size = 0;
    size += response_value_entry_size(&s->top);
    size += response_value_entry_size(&s->bottom);
    return size;
}

int response_color_read(response_color_t* s, buffers_read_callback_t on_read, void* on_read_state) {
    int res;
    int bytes_read = 0;
    res = buffers_read_uint8_t(&s->a, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    res = buffers_read_uint8_t(&s->r, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    res = buffers_read_uint8_t(&s->g, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    res = buffers_read_uint8_t(&s->b, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    return bytes_read;
}

int response_color_write(const response_color_t* s, buffers_write_callback_t on_write, void* on_write_state) {
    int res;
    int total = 0;
    res = buffers_write_uint8_t(s->a, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = buffers_write_uint8_t(s->r, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = buffers_write_uint8_t(s->g, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = buffers_write_uint8_t(s->b, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    return total;
}

size_t response_color_size(const response_color_t* s) {
    size_t size = 0;
    size += 1;
    size += 1;
    size += 1;
    size += 1;
    return size;
}

int response_screen_value_entry_read(response_screen_value_entry_t* s, buffers_read_callback_t on_read, void* on_read_state) {
    int res;
    int bytes_read = 0;
    res = response_color_read(&s->color, on_read, on_read_state);
    if(res < 0) { return res; }
    bytes_read += res;
    {
        uint8_t _len_suffix;
        res = buffers_read_uint8_t(&_len_suffix, on_read, on_read_state, &bytes_read);
        if(res < 0) { return res; }
        if(_len_suffix > 12) { return BUFFERS_ERROR_EOF; }
        for(int i = 0; i < (int)_len_suffix; ++i) {
            res = buffers_read_char(&s->suffix[i], on_read, on_read_state, &bytes_read);
            if(res < 0) { return res; }
        }
        if(_len_suffix < 12) {
            s->suffix[_len_suffix] = '\0';
        }
    }
    return bytes_read;
}

int response_screen_value_entry_write(const response_screen_value_entry_t* s, buffers_write_callback_t on_write, void* on_write_state) {
    int res;
    int total = 0;
    res = response_color_write(&s->color, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    {
        uint8_t _len_suffix = 0;
        for(int i = 0; i < 12; ++i) {
            if(s->suffix[i] == '\0') break;
            _len_suffix++;
        }
        res = buffers_write_uint8_t(_len_suffix, on_write, on_write_state);
        if(res < 0) { return res; }
        total += res;
        for(int i = 0; i < (int)_len_suffix; ++i) {
            res = buffers_write_char(s->suffix[i], on_write, on_write_state);
            if(res < 0) { return res; }
            total += res;
        }
    }
    return total;
}

size_t response_screen_value_entry_size(const response_screen_value_entry_t* s) {
    size_t size = 0;
    size += response_color_size(&s->color);
    {
        uint8_t _len = 0;
        for(int i = 0; i < 12; ++i) {
            if(s->suffix[i] == '\0') break;
            _len++;
        }
        size += 1 + (size_t)_len * 1;
    }
    return size;
}

int response_screen_entry_read(response_screen_entry_t* s, buffers_read_callback_t on_read, void* on_read_state) {
    int res;
    int bytes_read = 0;
    {
        uint8_t _len_label;
        res = buffers_read_uint8_t(&_len_label, on_read, on_read_state, &bytes_read);
        if(res < 0) { return res; }
        if(_len_label > 16) { return BUFFERS_ERROR_EOF; }
        for(int i = 0; i < (int)_len_label; ++i) {
            res = buffers_read_char(&s->label[i], on_read, on_read_state, &bytes_read);
            if(res < 0) { return res; }
        }
        if(_len_label < 16) {
            s->label[_len_label] = '\0';
        }
    }
    res = response_color_read(&s->color, on_read, on_read_state);
    if(res < 0) { return res; }
    bytes_read += res;
    res = response_screen_value_entry_read(&s->value1, on_read, on_read_state);
    if(res < 0) { return res; }
    bytes_read += res;
    res = response_screen_value_entry_read(&s->value2, on_read, on_read_state);
    if(res < 0) { return res; }
    bytes_read += res;
    return bytes_read;
}

int response_screen_entry_write(const response_screen_entry_t* s, buffers_write_callback_t on_write, void* on_write_state) {
    int res;
    int total = 0;
    {
        uint8_t _len_label = 0;
        for(int i = 0; i < 16; ++i) {
            if(s->label[i] == '\0') break;
            _len_label++;
        }
        res = buffers_write_uint8_t(_len_label, on_write, on_write_state);
        if(res < 0) { return res; }
        total += res;
        for(int i = 0; i < (int)_len_label; ++i) {
            res = buffers_write_char(s->label[i], on_write, on_write_state);
            if(res < 0) { return res; }
            total += res;
        }
    }
    res = response_color_write(&s->color, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = response_screen_value_entry_write(&s->value1, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = response_screen_value_entry_write(&s->value2, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    return total;
}

size_t response_screen_entry_size(const response_screen_entry_t* s) {
    size_t size = 0;
    {
        uint8_t _len = 0;
        for(int i = 0; i < 16; ++i) {
            if(s->label[i] == '\0') break;
            _len++;
        }
        size += 1 + (size_t)_len * 1;
    }
    size += response_color_size(&s->color);
    size += response_screen_value_entry_size(&s->value1);
    size += response_screen_value_entry_size(&s->value2);
    return size;
}

int response_screen_header_read(response_screen_header_t* s, buffers_read_callback_t on_read, void* on_read_state) {
    int res;
    int bytes_read = 0;
    res = buffers_read_int8_t(&s->index, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    res = buffers_read_uint8_t(&s->flags, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    return bytes_read;
}

int response_screen_header_write(const response_screen_header_t* s, buffers_write_callback_t on_write, void* on_write_state) {
    int res;
    int total = 0;
    res = buffers_write_int8_t(s->index, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = buffers_write_uint8_t(s->flags, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    return total;
}

size_t response_screen_header_size(const response_screen_header_t* s) {
    size_t size = 0;
    size += 1;
    size += 1;
    return size;
}

int response_screen_read(response_screen_t* s, buffers_read_callback_t on_read, void* on_read_state) {
    int res;
    int bytes_read = 0;
    res = response_screen_header_read(&s->header, on_read, on_read_state);
    if(res < 0) { return res; }
    bytes_read += res;
    res = response_screen_entry_read(&s->top, on_read, on_read_state);
    if(res < 0) { return res; }
    bytes_read += res;
    res = response_screen_entry_read(&s->bottom, on_read, on_read_state);
    if(res < 0) { return res; }
    bytes_read += res;
    return bytes_read;
}

int response_screen_write(const response_screen_t* s, buffers_write_callback_t on_write, void* on_write_state) {
    int res;
    int total = 0;
    res = response_screen_header_write(&s->header, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = response_screen_entry_write(&s->top, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = response_screen_entry_write(&s->bottom, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    return total;
}

size_t response_screen_size(const response_screen_t* s) {
    size_t size = 0;
    size += response_screen_header_size(&s->header);
    size += response_screen_entry_size(&s->top);
    size += response_screen_entry_size(&s->bottom);
    return size;
}

int response_clear_read(response_clear_t* s, buffers_read_callback_t on_read, void* on_read_state) {
    (void)s; (void)on_read; (void)on_read_state;
    return 0;
}

int response_clear_write(const response_clear_t* s, buffers_write_callback_t on_write, void* on_write_state) {
    (void)s; (void)on_write; (void)on_write_state;
    return 0;
}

size_t response_clear_size(const response_clear_t* s) {
    (void)s;
    return 0;
}

int response_ident_read(response_ident_t* s, buffers_read_callback_t on_read, void* on_read_state) {
    (void)s; (void)on_read; (void)on_read_state;
    return 0;
}

int response_ident_write(const response_ident_t* s, buffers_write_callback_t on_write, void* on_write_state) {
    (void)s; (void)on_write; (void)on_write_state;
    return 0;
}

size_t response_ident_size(const response_ident_t* s) {
    (void)s;
    return 0;
}

int response_refresh_screen_read(response_refresh_screen_t* s, buffers_read_callback_t on_read, void* on_read_state) {
    (void)s; (void)on_read; (void)on_read_state;
    return 0;
}

int response_refresh_screen_write(const response_refresh_screen_t* s, buffers_write_callback_t on_write, void* on_write_state) {
    (void)s; (void)on_write; (void)on_write_state;
    return 0;
}

size_t response_refresh_screen_size(const response_refresh_screen_t* s) {
    (void)s;
    return 0;
}

int request_data_read(request_data_t* s, buffers_read_callback_t on_read, void* on_read_state) {
    int res;
    int bytes_read = 0;
    res = buffers_read_int8_t(&s->screen_index, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    return bytes_read;
}

int request_data_write(const request_data_t* s, buffers_write_callback_t on_write, void* on_write_state) {
    int res;
    int total = 0;
    res = buffers_write_int8_t(s->screen_index, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    return total;
}

size_t request_data_size(const request_data_t* s) {
    size_t size = 0;
    size += 1;
    return size;
}

int request_nop_read(request_nop_t* s, buffers_read_callback_t on_read, void* on_read_state) {
    (void)s; (void)on_read; (void)on_read_state;
    return 0;
}

int request_nop_write(const request_nop_t* s, buffers_write_callback_t on_write, void* on_write_state) {
    (void)s; (void)on_write; (void)on_write_state;
    return 0;
}

size_t request_nop_size(const request_nop_t* s) {
    (void)s;
    return 0;
}

int request_screen_read(request_screen_t* s, buffers_read_callback_t on_read, void* on_read_state) {
    int res;
    int bytes_read = 0;
    res = buffers_read_int8_t(&s->screen_index, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    return bytes_read;
}

int request_screen_write(const request_screen_t* s, buffers_write_callback_t on_write, void* on_write_state) {
    int res;
    int total = 0;
    res = buffers_write_int8_t(s->screen_index, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    return total;
}

size_t request_screen_size(const request_screen_t* s) {
    size_t size = 0;
    size += 1;
    return size;
}

int request_set_mode_read(request_set_mode_t* s, buffers_read_callback_t on_read, void* on_read_state) {
    int res;
    int bytes_read = 0;
    res = buffers_read_int8_t(&s->mode, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    return bytes_read;
}

int request_set_mode_write(const request_set_mode_t* s, buffers_write_callback_t on_write, void* on_write_state) {
    int res;
    int total = 0;
    res = buffers_write_int8_t(s->mode, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    return total;
}

size_t request_set_mode_size(const request_set_mode_t* s) {
    size_t size = 0;
    size += 1;
    return size;
}

int request_ident_read(request_ident_t* s, buffers_read_callback_t on_read, void* on_read_state) {
    int res;
    int bytes_read = 0;
    res = buffers_read_uint16_t_le(&s->version_major, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    res = buffers_read_uint16_t_le(&s->version_minor, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    res = buffers_read_uint64_t_le(&s->build, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    res = buffers_read_int16_t_le(&s->id, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    for(int i = 0; i < 6; ++i) {
        res = buffers_read_uint8_t(&s->mac_address[i], on_read, on_read_state, &bytes_read);
        if(res < 0) { return res; }
    }
    {
        uint8_t _len_display_name;
        res = buffers_read_uint8_t(&_len_display_name, on_read, on_read_state, &bytes_read);
        if(res < 0) { return res; }
        if(_len_display_name > 64) { return BUFFERS_ERROR_EOF; }
        for(int i = 0; i < (int)_len_display_name; ++i) {
            res = buffers_read_char(&s->display_name[i], on_read, on_read_state, &bytes_read);
            if(res < 0) { return res; }
        }
        if(_len_display_name < 64) {
            s->display_name[_len_display_name] = '\0';
        }
    }
    {
        uint8_t _len_slug;
        res = buffers_read_uint8_t(&_len_slug, on_read, on_read_state, &bytes_read);
        if(res < 0) { return res; }
        if(_len_slug > 64) { return BUFFERS_ERROR_EOF; }
        for(int i = 0; i < (int)_len_slug; ++i) {
            res = buffers_read_char(&s->slug[i], on_read, on_read_state, &bytes_read);
            if(res < 0) { return res; }
        }
        if(_len_slug < 64) {
            s->slug[_len_slug] = '\0';
        }
    }
    res = buffers_read_uint16_t_le(&s->horizontal_resolution, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    res = buffers_read_uint16_t_le(&s->vertical_resolution, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    res = buffers_read_bool(&s->is_monochrome, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    res = buffers_read_float_le(&s->dpi, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    res = buffers_read_float_le(&s->pixel_size, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    res = read_input_type_t(&s->input_type, on_read, on_read_state, &bytes_read);
    if(res < 0) { return res; }
    return bytes_read;
}

int request_ident_write(const request_ident_t* s, buffers_write_callback_t on_write, void* on_write_state) {
    int res;
    int total = 0;
    res = buffers_write_uint16_t_le(s->version_major, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = buffers_write_uint16_t_le(s->version_minor, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = buffers_write_uint64_t_le(s->build, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = buffers_write_int16_t_le(s->id, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    for(int i = 0; i < 6; ++i) {
        res = buffers_write_uint8_t(s->mac_address[i], on_write, on_write_state);
        if(res < 0) { return res; }
        total += res;
    }
    {
        uint8_t _len_display_name = 0;
        for(int i = 0; i < 64; ++i) {
            if(s->display_name[i] == '\0') break;
            _len_display_name++;
        }
        res = buffers_write_uint8_t(_len_display_name, on_write, on_write_state);
        if(res < 0) { return res; }
        total += res;
        for(int i = 0; i < (int)_len_display_name; ++i) {
            res = buffers_write_char(s->display_name[i], on_write, on_write_state);
            if(res < 0) { return res; }
            total += res;
        }
    }
    {
        uint8_t _len_slug = 0;
        for(int i = 0; i < 64; ++i) {
            if(s->slug[i] == '\0') break;
            _len_slug++;
        }
        res = buffers_write_uint8_t(_len_slug, on_write, on_write_state);
        if(res < 0) { return res; }
        total += res;
        for(int i = 0; i < (int)_len_slug; ++i) {
            res = buffers_write_char(s->slug[i], on_write, on_write_state);
            if(res < 0) { return res; }
            total += res;
        }
    }
    res = buffers_write_uint16_t_le(s->horizontal_resolution, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = buffers_write_uint16_t_le(s->vertical_resolution, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = buffers_write_bool(s->is_monochrome, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = buffers_write_float_le(s->dpi, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = buffers_write_float_le(s->pixel_size, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    res = write_input_type_t(s->input_type, on_write, on_write_state);
    if(res < 0) { return res; }
    total += res;
    return total;
}

size_t request_ident_size(const request_ident_t* s) {
    size_t size = 0;
    size += 2;
    size += 2;
    size += 8;
    size += 2;
    size += (size_t)6 * 1;
    {
        uint8_t _len = 0;
        for(int i = 0; i < 64; ++i) {
            if(s->display_name[i] == '\0') break;
            _len++;
        }
        size += 1 + (size_t)_len * 1;
    }
    {
        uint8_t _len = 0;
        for(int i = 0; i < 64; ++i) {
            if(s->slug[i] == '\0') break;
            _len++;
        }
        size += 1 + (size_t)_len * 1;
    }
    size += 2;
    size += 2;
    size += 1;
    size += 4;
    size += 4;
    size += 1;
    return size;
}
