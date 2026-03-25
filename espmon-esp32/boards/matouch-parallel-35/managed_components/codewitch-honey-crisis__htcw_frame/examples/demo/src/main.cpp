// THIS IS THE ARDUINO source file
// the ESP-IDF source file is esp-idf/main.c
#ifndef ESP_IDF
#include <Arduino.h>
#include "frame.h"
#include "interface_buffers.h"

static frame_handle_t frame_handle = NULL;
static int serial_read(void* state) {
    (void)state;
    return Serial.read();
}
static int serial_write(uint8_t value, void* state) {
    (void)state;
    Serial.write((uint8_t)value);
    return 0;
}
void setup() {
    Serial.begin(115200);
    frame_handle = frame_create(INTERFACE_MAX_SIZE,serial_read,NULL,serial_write,NULL);
}
typedef struct {
    uint8_t* ptr;
    size_t remaining;
} buffer_write_cursor_t;
typedef struct {
    const uint8_t* ptr;
    size_t remaining;
} buffer_read_cursor_t;
int on_write_buffer(uint8_t value, void* state) {
    buffer_write_cursor_t* cur = (buffer_write_cursor_t*)state;
    if(cur->remaining==0) {
        return BUFFERS_ERROR_EOF;
    }
    *cur->ptr++=value;
    --cur->remaining;
    return 1;
}
int on_read_buffer(void* state) {
    buffer_read_cursor_t* cur = (buffer_read_cursor_t*)state;
    if(cur->remaining==0) {
        return BUFFERS_EOF;
    }
    uint8_t result = *cur->ptr++;
    --cur->remaining;
    return result;
}
void loop() {
    static uint8_t msg_buffer[INTERFACE_MAX_SIZE];
    int cmd;
    void* ptr;
    size_t length;
    cmd = frame_get(frame_handle,&ptr,&length);
    if(cmd>0) {
        buffer_read_cursor_t read_cur = {(const uint8_t*)ptr,length};
        // the following is only used when we need to respond
        buffer_write_cursor_t write_cur = {msg_buffer,INTERFACE_MAX_SIZE};
        switch((st_message_command_t)cmd) {
            case CMD_RNG: {
                st_rng_message_t msg;
                if(-1<st_rng_message_read(&msg,on_read_buffer,&read_cur)) {
                    Serial.println("RNG generation requested");
                    st_rng_response_message_t resp;
                    randomSeed(millis());
                    resp.value = random();
                    int count = st_rng_response_message_write(&resp,on_write_buffer,&write_cur);
                    frame_put(frame_handle, CMD_RNG_RESPONSE,msg_buffer,count);
                }
            }
            break;
            case CMD_GPIO_GET: {
                st_gpio_get_message_t msg;
                uint64_t result = 0;
                if(-1<st_gpio_get_message_read(&msg,on_read_buffer,&read_cur)) {
                    for(int i = 0; i<64;++i) {
                        if(0!=(msg.mask & (((uint64_t)1)<<i))) {
                            Serial.print("GPIO get request for ");
                            Serial.println((int)i);
                            if(digitalRead(i)==HIGH) {
                                result |= (((uint64_t)1)<<i);
                            }
                        }
                    }
                    st_gpio_get_response_message_t resp;
                    resp.values = result;
                    int count = st_gpio_get_response_message_write(&resp,on_write_buffer,&write_cur);
                    frame_put(frame_handle, CMD_GPIO_GET_RESPONSE,msg_buffer,count);
                }
            }
            break;
            case CMD_GPIO_SET: {
                st_gpio_set_message_t msg;
                if(-1<st_gpio_set_message_read(&msg,on_read_buffer,&read_cur)) {
                    for(int i = 0; i<64;++i) {
                        uint64_t mask_cmp = (((uint64_t)1)<<i); 
                        if(0!=(msg.mask & mask_cmp)) {
                            Serial.print("GPIO set level request for ");
                            Serial.print((int)i);
                            if(0!=(msg.values & mask_cmp)) {
                                Serial.println(" to on");
                                digitalWrite(i,HIGH);
                            } else {
                                Serial.println(" to off");
                                digitalWrite(i,LOW);
                            }
                        }
                    }
                }
            }
            break;
            case CMD_GPIO_MODE: {
                st_gpio_mode_message_t msg;
                if(-1<st_gpio_mode_message_read(&msg,on_read_buffer,&read_cur)) {
                    Serial.print("GPIO set mode for ");
                    Serial.println((int)msg.gpio);
                    switch(msg.mode) {
                        case MODE_INPUT:
                            pinMode(msg.gpio,INPUT);
                            break;
                        case MODE_INPUT_PULLUP:
                            pinMode(msg.gpio,INPUT_PULLUP);    
                        break;
                        case MODE_INPUT_PULLDOWN:
                            pinMode(msg.gpio,INPUT_PULLDOWN);
                            break;
                        case MODE_OUTPUT:
                            pinMode(msg.gpio,OUTPUT);
                            break;
                        case MODE_OUTPUT_OPEN_DRAIN:
                            pinMode(msg.gpio,OUTPUT_OPEN_DRAIN);
                            break;
                    }
                    
                }
            }
            break;
            
            default: {
                Serial.print("Unknown command received ");
                Serial.println((int)cmd);
            }
            break;
        }
    }
}
#endif // !ESP_IDF