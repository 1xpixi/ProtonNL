namespace ProtonNL.Hook;

public static class FreeServerNlPatch
{
    public static void IsSupportedPostfix(object __instance, object server, ref bool __result)
    {
        try
        {
            if (!__result || server == null)
                return;

            object? strategy = __instance.GetType().GetProperty("Strategy")?.GetValue(__instance);
            if (string.Equals(strategy?.ToString(), "Random", StringComparison.Ordinal))
                return;

            if (!string.IsNullOrWhiteSpace(Runtime.ForcedServerId))
            {
                string? id = server.GetType().GetProperty("Id")?.GetValue(server) as string;
                if (!string.Equals(id, Runtime.ForcedServerId, StringComparison.Ordinal))
                    __result = false;
                return;
            }

            if (string.IsNullOrWhiteSpace(Runtime.ForcedCountry))
                return;

            string? exitCountry = server.GetType().GetProperty("ExitCountry")?.GetValue(server) as string;
            if (!string.Equals(exitCountry, Runtime.ForcedCountry, StringComparison.OrdinalIgnoreCase))
            {
                __result = false;
                return;
            }

            if (string.IsNullOrWhiteSpace(Runtime.ForcedCity))
                return;

            string? city = server.GetType().GetProperty("City")?.GetValue(server) as string;
            if (!string.Equals(city, Runtime.ForcedCity, StringComparison.OrdinalIgnoreCase))
                __result = false;
        }
        catch (Exception ex)
        {
            Logger.Write("postfix failed: " + ex.Message);
        }
    }
}
