# ProtonNL

**NL stands for NO LIES.**

Unofficial ProtonVPN helper for Windows. Kills the Change Server cooldown and lets you pick any free region.

Not affiliated with Proton AG.

## Run

Grab a [release](https://github.com/1xpixi/ProtonNL/releases), start ProtonVPN, then run `ProtonNL.Loader.exe`.

A floating **Free servers** button appears in the bottom-right corner of ProtonVPN. Click it to expand the compact picker with an animated transition. Browse country → city → server, or search by name. Server rows show load and highlight your selection; **Quick connect** chooses the lowest-load server, while **Connect** uses your selected location or server. Double-click a server to connect immediately.

Use the down-arrow button or **Escape** to collapse the picker. **Enter** reopens it when the launcher has keyboard focus. Search, expanded groups, and selection are preserved when collapsed. The card resizes to fit the ProtonVPN window.

## Build

CMake, VS 2022 (x64), .NET 8 SDK:

```bat
cmake -S . -B build -G "Visual Studio 17 2022" -A x64
cmake --build build --config Release
```
