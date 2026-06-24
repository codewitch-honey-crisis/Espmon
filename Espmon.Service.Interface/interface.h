#pragma once
#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>

enum ServiceCommand
{
    AppStart = 1,
    AppEnd = 2
};

struct ServiceAppStartRequest {

};
struct ServiceDeviceEntry {
    wchar_t SerialNumber[64];
    int ScreenIndex;
};
struct ServiceAppStartResponse {
    size_t Count;
    ServiceDeviceEntry Entries[1024];
};


struct ServiceAppStopRequest {

};
struct ServiceAppStopResponse {

};