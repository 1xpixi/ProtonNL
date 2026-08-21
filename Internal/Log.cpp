#include "Log.h"

#include <Windows.h>
#include <fstream>
#include <mutex>

namespace {
    std::mutex gMutex;
    std::wstring gPath;

    std::wstring DefaultPath()
    {
        wchar_t temp[MAX_PATH]{};
        GetTempPathW(MAX_PATH, temp);
        return std::wstring(temp) + L"ProtonNL.log";
    }
}

namespace Log {

void Init()
{
    gPath = DefaultPath();
    Write(L"internal attached");
}

void Write(const std::wstring& message)
{
    std::lock_guard lock(gMutex);
    if (gPath.empty())
        gPath = DefaultPath();

    SYSTEMTIME st{};
    GetLocalTime(&st);

    wchar_t line[1024]{};
    swprintf_s(
        line,
        L"%04u-%02u-%02u %02u:%02u:%02u.%03u  %s\n",
        st.wYear, st.wMonth, st.wDay,
        st.wHour, st.wMinute, st.wSecond, st.wMilliseconds,
        message.c_str());

    std::wofstream out(gPath, std::ios::app);
    if (out)
        out << line;

    OutputDebugStringW(line);
}

}
