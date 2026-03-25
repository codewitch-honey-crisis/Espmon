// htcw_buffers protocol definition
enum ElevatedCommand
{
    InstallService = 1,
    UninstallService,
    InstalledCheck,
    StartService,
    StopService,
    StartedCheck,
    Shutdown
};
struct InstalledCheck {
    // returns Success if installed, otherwise not
};
struct StartedCheck {
    // returns Success if started, otherwise not
};
struct InstallRequest {
    wchar_t FromAppPath[256];
};
struct RequestResponse {
    bool Succeeded;
    uint32_t ErrorCode;
    wchar_t Message[256];

};
struct UninstallRequest {
};
struct StartRequest {

};

struct StopRequest {

};
struct ShutdownRequest {

};