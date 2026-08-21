# ProtonNL

**NL stands for NO LIES.**

Unofficial ProtonVPN helper for Windows. Kills the Change Server cooldown and lets you pick any free region.

Not affiliated with Proton AG.

## Run

Grab a [release](https://github.com/1xpixi/ProtonNL/releases), start ProtonVPN, then run `ProtonNL.Loader.exe`.

If the picker does not open, run `ProtonNL.Gui.exe` from the same folder. You need the [.NET Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64).

## Build

CMake, VS 2022 (x64), .NET 8 SDK:

```bat
cmake -S . -B build -G "Visual Studio 17 2022" -A x64
cmake --build build --config Release
```
