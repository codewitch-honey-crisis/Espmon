# frame

Provides transport framing and error checking for dirty comms lines.

This library accepts simple read and write callbacks to read or write bytes to and from a transport.

It frames the data using identical 8 command bytes **, followed by a 4 byte unsigned little endian length for the length of the payload portion and the same int type for the CRC value that's computed for the payload, followed by the payload itself.

** a value of 1 through 127, inclusive can be indicated for the command byte, but over the wire, 128 is added to the value of the command as sent over the wire so 1 becomes 129 in order to prevent otherwise likely collisions with ASCII strings. 0 is reserved.

Setting it up is simple, and involves giving `frame_create()` a couple of read/write callbacks and a max payload size.

Here's an example of doing so w/ Arduino
```cpp
#include <Arduino.h>
#include "frame.h"
#define PAYLOAD_MAX_SIZE (1024)
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
    frame_handle = frame_create(PAYLOAD_MAX_SIZE,serial_read,NULL,serial_write,NULL);
}
```
Once it's configured and bound to a transport, like the serial port above you can start reading and writing frames using `frame_get()` and `frame_put()`

The getter returns the command value (1-127) or 0 if no frame is waiting yet.
```cpp
void* ptr;
size_t length;
int cmd = frame_get(frame_handle, &ptr, &length);
// when cmd > 0 ptr contains the pointer to the payload, while length contains its size in bytes
// if cmd < 0 that indicates an error. If cmd is zero, there's no frame waiting yet.
```
Writing a frame is straightforward, but unlike reading one, you provide the buffer:
```cpp
// typdef struct { ... } data_t; data_t my_data;
int res = frame_put(frame_handle, my_cmd, &my_data, sizeof(my_data));
// again < 0 indicates an error. Otherwise the write was successful
```
There may be a case where you want to unconditionally discard a frame without reading it:
```cpp
frame_discard(frame_handle); // may never need this
```

If you no longer need the frame handle you can free its resources using `frame_destroy()`:
```cpp
frame_destroy(frame_handle);
```
```
; PlatformIO INI entry
[env:node32s]
platform = espressif32
board = node32s
framework = arduino
lib_deps = 
	codewitch-honey-crisis/htcw_frame
```

## About the Demo

The PlatformIO repo portion contains a Demo project under its examples tree which demonstrates using this library in tandem with [htcw_buffers](https://github.com/codewitch-honey-crisis/htcw_buffers) to create a complete and framed serial wire protocol for communicating betweeen the SerialFrameDemo C# app running on a Windows PC and a connected ESP32.