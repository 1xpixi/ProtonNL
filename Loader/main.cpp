#include <Windows.h>
#include <TlHelp32.h>
#include <Shlwapi.h>

#include <iostream>
#include <string>

namespace {

void PrintUsage(const wchar_t* argv0)
{
    std::wcout
        << L"ProtonNL loader\n"
        << L"Injects ProtonNL.Internal.dll into ProtonVPN.Client.exe.\n"
        << L"Removes the Change Server cooldown and opens a free-region picker.\n\n"
        << L"Usage:\n"
        << L"  " << argv0 << L" [--launch] [--wait ms] [--dll path]\n\n"
        << L"  (default)  inject into a running ProtonVPN.Client.exe\n"
        << L"  --launch   start the client with DOTNET_STARTUP_HOOKS if it is not running\n"
        << L"  --wait ms  retry finding the process for this many milliseconds\n"
        << L"  --dll path override Internal.dll path (default: next to this exe)\n\n"
        << L"Log: %TEMP%\\ProtonNL.log\n";
}

std::wstring ModuleDirectory(HMODULE module = nullptr)
{
    wchar_t path[MAX_PATH]{};
    GetModuleFileNameW(module, path, MAX_PATH);
    PathRemoveFileSpecW(path);
    return path;
}

std::wstring Join(const std::wstring& dir, const std::wstring& file)
{
    wchar_t combined[MAX_PATH]{};
    PathCombineW(combined, dir.c_str(), file.c_str());
    return combined;
}

DWORD FindPid(const wchar_t* exeName)
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE)
        return 0;

    PROCESSENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    DWORD pid = 0;
    if (Process32FirstW(snap, &entry))
    {
        do
        {
            if (_wcsicmp(entry.szExeFile, exeName) == 0)
            {
                pid = entry.th32ProcessID;
                break;
            }
        } while (Process32NextW(snap, &entry));
    }
    CloseHandle(snap);
    return pid;
}

DWORD WaitForPid(const wchar_t* exeName, DWORD timeoutMs)
{
    const DWORD start = GetTickCount();
    do
    {
        if (DWORD pid = FindPid(exeName))
            return pid;
        Sleep(200);
    } while (GetTickCount() - start < timeoutMs);
    return FindPid(exeName);
}

bool EnableDebugPrivilege()
{
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &token))
        return false;

    LUID luid{};
    if (!LookupPrivilegeValueW(nullptr, SE_DEBUG_NAME, &luid))
    {
        CloseHandle(token);
        return false;
    }

    TOKEN_PRIVILEGES tp{};
    tp.PrivilegeCount = 1;
    tp.Privileges[0].Luid = luid;
    tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
    const bool ok = AdjustTokenPrivileges(token, FALSE, &tp, sizeof(tp), nullptr, nullptr) != FALSE;
    CloseHandle(token);
    return ok;
}

bool Inject(DWORD pid, const std::wstring& dllPath)
{
    HANDLE process = OpenProcess(
        PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
        PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
        FALSE,
        pid);
    if (!process)
    {
        std::wcerr << L"OpenProcess failed: " << GetLastError() << L"\n";
        return false;
    }

    const size_t bytes = (dllPath.size() + 1) * sizeof(wchar_t);
    void* remote = VirtualAllocEx(process, nullptr, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remote)
    {
        std::wcerr << L"VirtualAllocEx failed: " << GetLastError() << L"\n";
        CloseHandle(process);
        return false;
    }

    if (!WriteProcessMemory(process, remote, dllPath.c_str(), bytes, nullptr))
    {
        std::wcerr << L"WriteProcessMemory failed: " << GetLastError() << L"\n";
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        CloseHandle(process);
        return false;
    }

    auto loadLibraryW = reinterpret_cast<LPTHREAD_START_ROUTINE>(
        GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "LoadLibraryW"));
    HANDLE thread = CreateRemoteThread(process, nullptr, 0, loadLibraryW, remote, 0, nullptr);
    if (!thread)
    {
        std::wcerr << L"CreateRemoteThread failed: " << GetLastError() << L"\n";
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        CloseHandle(process);
        return false;
    }

    WaitForSingleObject(thread, 15000);

    DWORD exitCode = 0;
    GetExitCodeThread(thread, &exitCode);
    CloseHandle(thread);
    VirtualFreeEx(process, remote, 0, MEM_RELEASE);
    CloseHandle(process);

    if (!exitCode)
    {
        std::wcerr << L"remote LoadLibraryW returned null (DLL missing a dependency?)\n";
        return false;
    }

    std::wcout << L"[+] injected, remote module 0x" << std::hex << exitCode << std::dec << L"\n";
    return true;
}

std::wstring FindClientExe()
{
    const wchar_t* candidates[] = {
        L"C:\\Program Files\\Proton\\VPN\\v5.1.7\\ProtonVPN.Client.exe",
        L"C:\\Program Files\\Proton\\VPN\\ProtonVPN.Client.exe",
    };
    for (const wchar_t* path : candidates)
    {
        if (GetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES)
            return path;
    }

    WIN32_FIND_DATAW dirFd{};
    HANDLE dirFind = FindFirstFileW(L"C:\\Program Files\\Proton\\VPN\\v*", &dirFd);
    if (dirFind == INVALID_HANDLE_VALUE)
        return {};

    std::wstring found;
    do
    {
        if (!(dirFd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY))
            continue;
        if (dirFd.cFileName[0] == L'.')
            continue;

        std::wstring path = std::wstring(L"C:\\Program Files\\Proton\\VPN\\") + dirFd.cFileName + L"\\ProtonVPN.Client.exe";
        if (GetFileAttributesW(path.c_str()) != INVALID_FILE_ATTRIBUTES)
            found = path;
    } while (FindNextFileW(dirFind, &dirFd));
    FindClose(dirFind);
    return found;
}

bool LaunchWithStartupHook(const std::wstring& hookDll)
{
    const std::wstring client = FindClientExe();
    if (client.empty())
    {
        std::wcerr << L"ProtonVPN.Client.exe not found under Program Files\\Proton\\VPN\n";
        return false;
    }

    std::wstring env = L"DOTNET_STARTUP_HOOKS=" + hookDll;
    SetEnvironmentVariableW(L"DOTNET_STARTUP_HOOKS", hookDll.c_str());

    STARTUPINFOW si{};
    si.cb = sizeof(si);
    PROCESS_INFORMATION pi{};
    std::wstring cmd = L"\"" + client + L"\"";
    if (!CreateProcessW(client.c_str(), cmd.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &si, &pi))
    {
        std::wcerr << L"CreateProcessW failed: " << GetLastError() << L"\n";
        return false;
    }
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    std::wcout << L"[+] launched " << client << L" with DOTNET_STARTUP_HOOKS\n";
    return true;
}

}

int wmain(int argc, wchar_t** argv)
{
    bool launch = false;
    DWORD waitMs = 0;
    std::wstring dllPath;

    for (int i = 1; i < argc; ++i)
    {
        const std::wstring arg = argv[i];
        if (arg == L"-h" || arg == L"--help")
        {
            PrintUsage(argv[0]);
            return 0;
        }
        if (arg == L"--launch")
        {
            launch = true;
            continue;
        }
        if (arg == L"--wait" && i + 1 < argc)
        {
            waitMs = static_cast<DWORD>(_wtoi(argv[++i]));
            continue;
        }
        if (arg == L"--dll" && i + 1 < argc)
        {
            dllPath = argv[++i];
            continue;
        }
        std::wcerr << L"unknown argument: " << arg << L"\n";
        PrintUsage(argv[0]);
        return 1;
    }

    const std::wstring dir = ModuleDirectory();
    if (dllPath.empty())
        dllPath = Join(dir, L"ProtonNL.Internal.dll");

    wchar_t fullDll[MAX_PATH]{};
    if (!GetFullPathNameW(dllPath.c_str(), MAX_PATH, fullDll, nullptr))
    {
        std::wcerr << L"GetFullPathNameW failed\n";
        return 1;
    }
    dllPath = fullDll;

    if (GetFileAttributesW(dllPath.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        std::wcerr << L"Internal.dll not found: " << dllPath << L"\n";
        return 1;
    }

    const std::wstring hookDll = Join(dir, L"ProtonNL.Hook.dll");
    if (GetFileAttributesW(hookDll.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        std::wcerr << L"ProtonNL.Hook.dll not found next to the loader: " << hookDll << L"\n";
        return 1;
    }

    EnableDebugPrivilege();

    DWORD pid = FindPid(L"ProtonVPN.Client.exe");
    if (!pid && waitMs)
        pid = WaitForPid(L"ProtonVPN.Client.exe", waitMs);

    if (!pid)
    {
        if (launch)
            return LaunchWithStartupHook(hookDll) ? 0 : 2;

        std::wcerr << L"ProtonVPN.Client.exe is not running. Start the app, or pass --launch.\n";
        return 2;
    }

    std::wcout << L"[*] pid : " << pid << L"\n";
    std::wcout << L"[*] dll : " << dllPath << L"\n";
    return Inject(pid, dllPath) ? 0 : 3;
}
