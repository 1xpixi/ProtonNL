# ProtonNL

**NL stands for NO LIES.**

Unofficial ProtonVPN Windows helper. Injects into `ProtonVPN.Client.exe`, removes the free-plan Change Server cooldown, and opens a picker so you can connect to any **free** region instead of rolling the dice.

Not affiliated with Proton AG.

## Features

- No Change Server cooldown
- Small window listing every free country / city
- Click a region (or a city) to connect
- ProtonVPN's own Change Server button stays random

Free-tier servers only. Paid Plus locations are not used.

## Run

1. Install [ProtonVPN for Windows](https://protonvpn.com/download)
2. Install the [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0) if the GUI does not start
3. Start ProtonVPN and log in
4. Run `ProtonNL.Loader.exe` from the build output folder

The picker should open. If it does not, run `ProtonNL.Gui.exe` from the same folder.

Log: `%TEMP%\ProtonNL.log`

## Build

Needs CMake, Visual Studio 2022 (x64), and the .NET 8 SDK.

```bat
cmake -S . -B build -G "Visual Studio 17 2022" -A x64
cmake --build build --config Release
```

Output lands in `build/bin/Release/`.

## Ship

If you are sending a zip, these ten files have to stay together:

```text
ProtonNL.Loader.exe
ProtonNL.Internal.dll
ProtonNL.Hook.dll
ProtonNL.Hook.deps.json
ProtonNL.Hook.runtimeconfig.json
0Harmony.dll
ProtonNL.Gui.exe
ProtonNL.Gui.dll
ProtonNL.Gui.deps.json
ProtonNL.Gui.runtimeconfig.json
```

## Layout

| Piece | What it is |
|---|---|
| `Loader/` | LoadLibrary injector targeting `ProtonVPN.Client.exe` |
| `Internal/` | Native bootstrap; attaches to the running CoreCLR via hostfxr |
| `Hook/` | Harmony patches + named pipe |
| `Gui/` | WinForms free-region picker |

## Notes

- Same Windows user as ProtonVPN. Admin is only needed if injection is denied.
- Patches live in memory. Restart ProtonVPN and they are gone until you load again.
- This is unofficial and likely against ProtonVPN's terms. Use it on your own account.
