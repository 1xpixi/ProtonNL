using System.Reflection;
using HarmonyLib;

namespace ProtonNL.Hook;

public static class CooldownPatch
{
    public static bool CanChangeServerPrefix(object __instance, ref bool __result)
    {
        Runtime.CaptureFromModerator(__instance);
        __result = true;
        return false;
    }

    public static bool IsAttemptsLimitReachedPrefix(ref bool __result)
    {
        __result = false;
        return false;
    }

    public static bool GetRemainingDelayUntilNextAttemptPrefix(ref TimeSpan __result)
    {
        __result = TimeSpan.Zero;
        return false;
    }

    public static bool GetDelayUntilNextAttemptPrefix(ref TimeSpan __result)
    {
        __result = TimeSpan.Zero;
        return false;
    }

    public static bool RegisterChangeServerAttemptPrefix(object __instance)
    {
        Runtime.CaptureFromModerator(__instance);
        return false;
    }

    public static void Apply(Harmony harmony, Assembly connection)
    {
        Type? moderator = connection.GetType("ProtonVPN.Client.Logic.Connection.ChangeServerModerator");
        if (moderator == null)
        {
            Logger.Write("ChangeServerModerator type not found");
            return;
        }

        PatchPrefix(harmony, moderator, "CanChangeServer", nameof(CanChangeServerPrefix));
        PatchPrefix(harmony, moderator, "IsAttemptsLimitReached", nameof(IsAttemptsLimitReachedPrefix));
        PatchPrefix(harmony, moderator, "GetRemainingDelayUntilNextAttempt", nameof(GetRemainingDelayUntilNextAttemptPrefix));
        PatchPrefix(harmony, moderator, "GetDelayUntilNextAttempt", nameof(GetDelayUntilNextAttemptPrefix));
        PatchPrefix(harmony, moderator, "RegisterChangeServerAttempt", nameof(RegisterChangeServerAttemptPrefix));
    }

    private static void PatchPrefix(Harmony harmony, Type type, string methodName, string patchName)
    {
        MethodInfo? method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == 0);
        if (method == null)
        {
            Logger.Write($"{type.Name}.{methodName} not found");
            return;
        }

        harmony.Patch(method, prefix: new HarmonyMethod(typeof(CooldownPatch), patchName));
        Logger.Write($"patched {type.Name}.{methodName} (no cooldown)");
    }
}
