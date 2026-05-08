enum ServiceCommand
{
    Initialize = 1,
    StartService,
    StopService
};
struct ErrorInfo {
    int Code; // 0 = success
    wchar_t Message[256];
};
struct InitializeRequest {
    wchar_t Path[1024];
};
struct InitializeResponse {
    ErrorInfo Error;
};

struct StartRequest {
};

struct StopRequest {
};

struct StartResponse {
    ErrorInfo Error;
};

struct StopResponse {
    ErrorInfo Error;
};
