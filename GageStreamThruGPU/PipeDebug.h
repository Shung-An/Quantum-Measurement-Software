// PipeDebug.h
#pragma once
#include <windows.h>
#include <string>
#include <cstdio>
#include <cstdarg>
#include <mutex>

inline std::string WinErrStr(DWORD err) {
    LPSTR msg = nullptr;
    DWORD n = FormatMessageA(
        FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
        nullptr, err, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
        (LPSTR)&msg, 0, nullptr);
    std::string s = n ? std::string(msg, n) : "Unknown error";
    if (msg) LocalFree(msg);
    return s;
}

class PipeLogger {
public:
    bool init(const char* path) {
        std::lock_guard<std::mutex> lk(mu_);
        if (f_) return true;
        f_ = std::fopen(path, "w");
        return f_ != nullptr;
    }
    void logf(const char* fmt, ...) {
        std::lock_guard<std::mutex> lk(mu_);
        if (!f_) return;
        ULONGLONG ms = GetTickCount64();
        DWORD tid = GetCurrentThreadId();
        std::fprintf(f_, "[%10llu ms][T%lu] ", ms, (unsigned long)tid);
        va_list ap; va_start(ap, fmt);
        std::vfprintf(f_, fmt, ap);
        va_end(ap);
        std::fprintf(f_, "\n");
        std::fflush(f_);
    }
private:
    std::mutex mu_;
    FILE* f_ = nullptr;
};

extern PipeLogger g_pipeLog;

struct ScopeTimer {
    const char* tag;
    ULONGLONG t0;
    ScopeTimer(const char* t) : tag(t), t0(GetTickCount64()) {}
    ~ScopeTimer() {
        ULONGLONG dt = GetTickCount64() - t0;
        g_pipeLog.logf("%s took %llu ms", tag, dt);
    }
};

// Wrapped blocking ReadFile/WriteFile with tracing
inline BOOL DebugRead(HANDLE h, void* buf, DWORD want, DWORD* got) {
    g_pipeLog.logf("ReadFile want=%lu", want);
    BOOL ok = ReadFile(h, buf, want, got, NULL);
    DWORD err = ok ? 0 : GetLastError();
    g_pipeLog.logf("ReadFile -> ok=%d got=%lu err=%lu (%s)", ok, got ? *got : 0, err, WinErrStr(err).c_str());
    return ok;
}

inline BOOL DebugWrite(HANDLE h, const void* buf, DWORD want, DWORD* sent) {
    g_pipeLog.logf("WriteFile want=%lu", want);
    BOOL ok = WriteFile(h, buf, want, sent, NULL);
    DWORD err = ok ? 0 : GetLastError();
    g_pipeLog.logf("WriteFile -> ok=%d sent=%lu err=%lu (%s)", ok, sent ? *sent : 0, err, WinErrStr(err).c_str());
    return ok;
}

// Non-blocking peek for pending 16-bit request
inline bool DebugHasShortRequest(HANDLE h, short& outReq) {
    DWORD avail = 0;
    BOOL pk = PeekNamedPipe(h, nullptr, 0, nullptr, &avail, nullptr);
    g_pipeLog.logf("PeekNamedPipe -> %d avail=%lu err=%lu (%s)", pk, avail, pk ? 0 : GetLastError(), WinErrStr(pk ? 0 : GetLastError()).c_str());
    if (!pk || avail < sizeof(short)) return false;
    DWORD br = 0;
    BOOL ok = ReadFile(h, &outReq, sizeof(outReq), &br, NULL);
    g_pipeLog.logf("ReadFile(req) -> ok=%d br=%lu val=%d err=%lu (%s)", ok, br, (int)outReq, ok ? 0 : GetLastError(), WinErrStr(ok ? 0 : GetLastError()).c_str());
    return ok && br == sizeof(short);
}
