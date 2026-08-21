using System.Reflection;
using HarmonyLib;

namespace ProtonNL.Hook;

public static class Entry
{
    public static int Initialize(IntPtr arg, int argSize)
    {
        try
        {
            Logger.Write("Initialize");
            Task.Run(WaitAndPatch);
            return 0;
        }
        catch (Exception ex)
        {
            Logger.Write("Initialize failed: " + ex);
            return 1;
        }
    }

    private static void WaitAndPatch()
    {
        Assembly? contracts = null;
        Assembly? connection = null;

        for (int i = 0; i < 120; i++)
        {
            contracts ??= FindAssembly("ProtonVPN.Client.Logic.Connection.Contracts");
            connection ??= FindAssembly("ProtonVPN.Client.Logic.Connection");
            if (contracts != null && connection != null)
            {
                ApplyPatches(contracts, connection);
                return;
            }

            Thread.Sleep(500);
        }

        Logger.Write("timed out waiting for ProtonVPN connection assemblies "
            + $"(contracts={contracts != null}, connection={connection != null})");

        if (contracts != null || connection != null)
            ApplyPatches(contracts, connection);
    }

    private static Assembly? FindAssembly(string name)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                return assembly;
        }

        return null;
    }

    private static void ApplyPatches(Assembly? contracts, Assembly? connection)
    {
        Harmony harmony = new("protonnl.changeserver.nl");

        if (contracts != null)
            PatchCountry(harmony, contracts);
        else
            Logger.Write("skipped country patch, contracts assembly missing");

        if (connection != null)
        {
            CooldownPatch.Apply(harmony, connection);
            PatchConnectionManager(harmony, connection);
        }
        else
            Logger.Write("skipped cooldown patch, connection assembly missing");

        PipeServer.Start();
        GuiLauncher.Start();
    }

    private static void PatchConnectionManager(Harmony harmony, Assembly connection)
    {
        Type? manager = connection.GetType("ProtonVPN.Client.Logic.Connection.ConnectionManager");
        if (manager == null)
        {
            Logger.Write("ConnectionManager type not found");
            return;
        }

        foreach (ConstructorInfo ctor in manager.GetConstructors())
        {
            harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(Entry), nameof(ConnectionManagerCtorPostfix)));
        }

        MethodInfo? connect = manager.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m => m.Name == "ConnectAsync" && m.GetParameters().Length == 2);
        if (connect != null)
        {
            harmony.Patch(connect, prefix: new HarmonyMethod(typeof(Entry), nameof(ConnectAsyncPrefix)));
            Logger.Write("patched ConnectionManager.ConnectAsync capture");
        }
    }

    public static void ConnectionManagerCtorPostfix(object __instance)
    {
        Runtime.CaptureManager(__instance);
    }

    public static void ConnectAsyncPrefix(object __instance)
    {
        Runtime.CaptureManager(__instance);
    }

    private static void PatchCountry(Harmony harmony, Assembly contracts)
    {
        Type? intentType = contracts.GetType(
            "ProtonVPN.Client.Logic.Connection.Contracts.Models.Intents.Locations.FreeServers.FreeServerLocationIntent");
        if (intentType == null)
        {
            Logger.Write("FreeServerLocationIntent type not found");
            return;
        }

        MethodInfo? isSupported = intentType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "IsSupported" && m.GetParameters().Length == 1);

        if (isSupported == null)
        {
            Logger.Write("IsSupported method not found");
            return;
        }

        harmony.Patch(
            isSupported,
            postfix: new HarmonyMethod(typeof(FreeServerNlPatch), nameof(FreeServerNlPatch.IsSupportedPostfix)));

        Logger.Write($"patched {isSupported.DeclaringType?.FullName}.{isSupported.Name} (GUI country filter)");
    }
}
