#include "ManagedHost.h"
#include "Log.h"

#include "coreclr_delegates.h"
#include "hostfxr.h"

#include <Shlwapi.h>

#include <string>

namespace {

constexpr int kSuccess = 0;
constexpr int kHostAlreadyInitialized = 1;
constexpr int kDifferentRuntimeProperties = 2;

using component_entry_t = int(CORECLR_DELEGATE_CALLTYPE*)(void* arg, int32_t argSize);

std::wstring WideError(int rc)
{
    wchar_t buf[64]{};
    swprintf_s(buf, L"0x%08X (%d)", static_cast<unsigned>(rc), rc);
    return buf;
}

HMODULE WaitForModule(const wchar_t* name, DWORD timeoutMs)
{
    const DWORD start = GetTickCount();
    while (GetTickCount() - start < timeoutMs)
    {
        if (HMODULE mod = GetModuleHandleW(name))
            return mod;
        Sleep(50);
    }
    return GetModuleHandleW(name);
}

std::wstring GetProcessDirectory()
{
    wchar_t path[MAX_PATH]{};
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    PathRemoveFileSpecW(path);
    return path;
}

bool InitHostContext(
    HMODULE hostfxr,
    const std::wstring& runtimeConfig,
    const std::wstring& hostPath,
    const std::wstring& dotnetRoot,
    hostfxr_handle* outHandle)
{
    auto init = reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(
        GetProcAddress(hostfxr, "hostfxr_initialize_for_runtime_config"));
    if (!init)
    {
        Log::Write(L"hostfxr_initialize_for_runtime_config export missing");
        return false;
    }

    hostfxr_initialize_parameters params{};
    params.size = sizeof(params);
    params.host_path = hostPath.c_str();
    params.dotnet_root = dotnetRoot.c_str();

    hostfxr_handle handle = nullptr;
    const int rc = init(runtimeConfig.c_str(), &params, &handle);
    if (rc != kSuccess && rc != kHostAlreadyInitialized && rc != kDifferentRuntimeProperties)
    {
        Log::Write(L"hostfxr_initialize_for_runtime_config failed: " + WideError(rc));
        return false;
    }

    if (!handle)
    {
        Log::Write(L"hostfxr returned a null context");
        return false;
    }

    *outHandle = handle;
    Log::Write(L"hostfxr context ready, status " + WideError(rc));
    return true;
}

}

std::wstring GetModuleDirectory(HMODULE module)
{
    wchar_t path[MAX_PATH]{};
    GetModuleFileNameW(module, path, MAX_PATH);
    PathRemoveFileSpecW(path);
    return path;
}

std::wstring JoinPath(const std::wstring& dir, const std::wstring& file)
{
    wchar_t combined[MAX_PATH]{};
    PathCombineW(combined, dir.c_str(), file.c_str());
    return combined;
}

bool LoadManagedHook(HMODULE thisModule)
{
    Log::Write(L"waiting for CoreCLR / hostfxr");

    HMODULE hostfxr = WaitForModule(L"hostfxr.dll", 30000);
    HMODULE coreclr = WaitForModule(L"coreclr.dll", 30000);
    if (!hostfxr || !coreclr)
    {
        Log::Write(L"runtime modules not loaded (hostfxr/coreclr)");
        return false;
    }

    // Give the WinUI client a moment to finish startup before we load extra assemblies.
    Sleep(1500);

    const std::wstring payloadDir = GetModuleDirectory(thisModule);
    const std::wstring hookDll = JoinPath(payloadDir, L"ProtonNL.Hook.dll");
    const std::wstring hookConfig = JoinPath(payloadDir, L"ProtonNL.Hook.runtimeconfig.json");
    const std::wstring appDir = GetProcessDirectory();

    wchar_t hostPath[MAX_PATH]{};
    GetModuleFileNameW(nullptr, hostPath, MAX_PATH);

    if (GetFileAttributesW(hookDll.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        Log::Write(L"missing ProtonNL.Hook.dll next to Internal.dll");
        return false;
    }
    if (GetFileAttributesW(hookConfig.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        Log::Write(L"missing ProtonNL.Hook.runtimeconfig.json next to Internal.dll");
        return false;
    }

    hostfxr_handle context = nullptr;
    if (!InitHostContext(hostfxr, hookConfig, hostPath, appDir, &context))
        return false;

    auto getDelegate = reinterpret_cast<hostfxr_get_runtime_delegate_fn>(
        GetProcAddress(hostfxr, "hostfxr_get_runtime_delegate"));
    auto close = reinterpret_cast<hostfxr_close_fn>(
        GetProcAddress(hostfxr, "hostfxr_close"));

    if (!getDelegate)
    {
        Log::Write(L"hostfxr_get_runtime_delegate export missing");
        if (close)
            close(context);
        return false;
    }

    void* loadFn = nullptr;
    int rc = getDelegate(context, hdt_load_assembly_and_get_function_pointer, &loadFn);
    if (rc != kSuccess || !loadFn)
    {
        Log::Write(L"get load_assembly_and_get_function_pointer failed: " + WideError(rc));
        if (close)
            close(context);
        return false;
    }

    auto loadAndGet = reinterpret_cast<load_assembly_and_get_function_pointer_fn>(loadFn);
    void* entry = nullptr;
    rc = loadAndGet(
        hookDll.c_str(),
        L"ProtonNL.Hook.Entry, ProtonNL.Hook",
        L"Initialize",
        nullptr,
        nullptr,
        &entry);

    if (rc != kSuccess || !entry)
    {
        Log::Write(L"failed to load ProtonNL.Hook.Entry.Initialize: " + WideError(rc));
        if (close)
            close(context);
        return false;
    }

    Log::Write(L"calling ProtonNL.Hook.Entry.Initialize");
    const int hookRc = reinterpret_cast<component_entry_t>(entry)(nullptr, 0);
    Log::Write(L"hook Initialize returned " + std::to_wstring(hookRc));

    if (close)
        close(context);

    return hookRc == 0;
}
