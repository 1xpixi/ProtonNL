#pragma once

#include <Windows.h>
#include <string>

bool LoadManagedHook(HMODULE thisModule);
std::wstring GetModuleDirectory(HMODULE module);
std::wstring JoinPath(const std::wstring& dir, const std::wstring& file);
