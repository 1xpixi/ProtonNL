#include "ManagedHost.h"
#include "Log.h"

#include <Windows.h>

namespace {
    HMODULE gModule = nullptr;

    DWORD WINAPI MainThread(LPVOID)
    {
        Log::Init();

        if (GetEnvironmentVariableW(L"PROTONNL_CONSOLE", nullptr, 0) > 0)
        {
            AllocConsole();
            FILE* dummy = nullptr;
            freopen_s(&dummy, "CONOUT$", "w", stdout);
            freopen_s(&dummy, "CONOUT$", "w", stderr);
        }

        if (!LoadManagedHook(gModule))
            Log::Write(L"managed hook failed — see ProtonNL.log");
        else
            Log::Write(L"managed hook loaded");

        return 0;
    }
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        gModule = module;
        DisableThreadLibraryCalls(module);
        HANDLE thread = CreateThread(nullptr, 0, MainThread, nullptr, 0, nullptr);
        if (thread)
            CloseHandle(thread);
    }
    return TRUE;
}
