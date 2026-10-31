using System.Collections;
using System.Globalization;
using System.Reflection;

namespace ProtonNL.Hook;

internal static class ConnectionBridge
{
    public static List<FreeRegion> GetFreeRegions()
    {
        object? loader = Runtime.ServersLoader;
        if (loader == null)
            return [];

        MethodInfo? method = FindInstanceMethod(loader, "GetFreeServers")
            ?? FindInstanceMethod(loader, "GetServers");
        if (method == null)
        {
            Logger.Write("no GetFreeServers/GetServers on " + loader.GetType().FullName);
            return [];
        }

        if (method.Invoke(loader, null) is not IEnumerable servers)
            return [];

        int seen = 0, kept = 0, free = 0, paid = 0, noPhysical = 0;
        Dictionary<string, FreeRegion> regions = new(StringComparer.OrdinalIgnoreCase);
        foreach (object server in servers)
        {
            seen++;
            if (IsFree(server))
                free++;
            else
                paid++;
            if (!HasPhysicalServers(server))
                noPhysical++;
            if (!IsListable(server))
                continue;
            kept++;

            string? country = GetString(server, "ExitCountry");
            if (string.IsNullOrWhiteSpace(country))
                continue;

            string city = GetString(server, "City") ?? "";
            if (!regions.TryGetValue(country, out FreeRegion? region))
            {
                region = new FreeRegion
                {
                    Code = country.ToUpperInvariant(),
                    Name = CountryName(country)
                };
                regions[country] = region;
            }

            if (string.IsNullOrWhiteSpace(city))
                city = "(no city)";

            region.ServerCount++;
            CityCount? existing = region.Cities.FirstOrDefault(c =>
                string.Equals(c.Name, city, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new CityCount { Name = city };
                region.Cities.Add(existing);
            }

            existing.ServerCount++;
            existing.Servers.Add(new ServerItem
            {
                Id = GetString(server, "Id") ?? "",
                Name = GetString(server, "Name") ?? GetString(server, "Id") ?? "server",
                Load = GetInt(server, "Load")
            });
        }

        Logger.Write($"list via {method.Name} seen={seen} kept={kept} free={free} paid={paid} noPhysical={noPhysical} plan={ReadPlanSummary()}");

        foreach (FreeRegion region in regions.Values)
        {
            region.Cities.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            foreach (CityCount cityNode in region.Cities)
                cityNode.Servers.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        return regions.Values
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string Connect(string country, string? city, string? serverId, string? serverName = null)
    {
        object? manager = Runtime.ConnectionManager;
        if (manager == null)
            return "ProtonVPN connection manager is not ready yet. Wait a second and retry.";

        country = country.Trim().ToUpperInvariant();
        city = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        serverId = string.IsNullOrWhiteSpace(serverId) ? null : serverId.Trim();
        serverName = string.IsNullOrWhiteSpace(serverName) ? null : serverName.Trim();

        city = NormalizeCity(city);
        Runtime.ForcedCountry = country;
        Runtime.ForcedCity = city;
        Runtime.ForcedServerId = serverId;
        Runtime.LastError = null;

        try
        {
            object? server = serverId == null ? null : FindServerById(serverId);
            object intent = BuildConnectionIntent(country, city, serverId, serverName, server)
                ?? GetFreeDefaultIntent()
                ?? throw new InvalidOperationException("ConnectionIntent not found");
            object trigger = GetTrigger("CountriesServer")
                ?? GetTrigger("CountriesCountry")
                ?? GetTrigger("NewConnection")
                ?? GetTrigger("ConnectionCard")
                ?? throw new InvalidOperationException("VpnTriggerDimension not found");

            MethodInfo connect = manager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "ConnectAsync" && m.GetParameters().Length == 2)
                ?? throw new InvalidOperationException("ConnectAsync not found");

            object? task = connect.Invoke(manager, [trigger, intent]);
            if (task is Task awaitable)
                awaitable.Wait(TimeSpan.FromSeconds(3));

            string? error = ReadCurrentError(manager);
            if (!string.IsNullOrWhiteSpace(error) && !string.Equals(error, "None", StringComparison.OrdinalIgnoreCase))
            {
                Runtime.LastError = error;
                Logger.Write($"connect failed country={country} city={city ?? "(any)"} server={serverName ?? serverId ?? "(any)"} error={error}");
                return $"Failed ({FriendlyError(error)}) on {serverName ?? city ?? CountryName(country)}.";
            }

            Logger.Write($"connect requested country={country} city={city ?? "(any)"} server={serverName ?? serverId ?? "(any)"} task={task != null}");
            if (serverId != null)
                return $"Connecting to {serverName ?? serverId}...";
            if (city != null)
                return $"Connecting to {CountryName(country)} / {city}...";
            return $"Connecting to {CountryName(country)}...";
        }
        catch (Exception ex)
        {
            Runtime.LastError = ex.GetBaseException().Message;
            Logger.Write("connect failed: " + ex);
            return "Connect failed: " + ex.GetBaseException().Message;
        }
    }

    private static string? ReadCurrentError(object manager)
    {
        object? error = Runtime.GetInstanceField(manager, "_currentError");
        string? name = error?.ToString();
        if (string.IsNullOrWhiteSpace(name) || name is "None" or "NoneKeepEnabledKillSwitch")
            return null;
        return name;
    }

    private static string FriendlyError(string error) => error switch
    {
        "NoServers" => "no servers for this protocol",
        "ServerUnreachable" => "server unreachable",
        "ServerOffline" => "server offline",
        "TlsError" => "TLS error",
        "TlsCertificateError" => "certificate error",
        "PingTimeoutError" => "timed out",
        "AdapterTimeoutError" => "adapter timed out",
        "Unknown" => "unknown error",
        _ => error
    };

    private static object? GetFreeDefaultIntent()
    {
        Type? type = FindType("ProtonVPN.Client.Logic.Connection.Contracts.Models.Intents.ConnectionIntent");
        return type?.GetProperty("FreeDefault", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
    }

    private static object? BuildConnectionIntent(
        string country, string? city, string? serverId, string? serverName, object? server)
    {
        object? location = BuildLocationIntent(country, city, serverId, serverName ?? (server == null ? null : GetString(server, "Name")));
        if (location == null)
            return null;

        object? feature = BuildFeatureIntent(server);
        Type? intentType = FindType("ProtonVPN.Client.Logic.Connection.Contracts.Models.Intents.ConnectionIntent");
        ConstructorInfo? ctor = intentType?.GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Length is 1 or 2);
        if (ctor == null)
            return null;

        object?[] args = ctor.GetParameters().Length == 1 ? [location] : [location, feature];
        object intent = ctor.Invoke(args);
        Logger.Write($"intent {intent} feature={(feature?.GetType().Name ?? "none")}");
        return intent;
    }

    private static object? BuildLocationIntent(string country, string? city, string? serverId, string? serverName)
    {
        if (!string.IsNullOrWhiteSpace(serverId))
        {
            Type? infoType = FindType("ProtonVPN.Client.Logic.Connection.Contracts.Models.ServerInfo");
            MethodInfo? fromInfo = infoType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "From" && m.GetParameters().Length == 2);
            object? info = fromInfo?.Invoke(null, [serverId, serverName ?? serverId]);
            Type? serverIntent = FindType(
                "ProtonVPN.Client.Logic.Connection.Contracts.Models.Intents.Locations.Servers.SingleServerLocationIntent");
            if (info != null && serverIntent != null)
            {
                foreach (MethodInfo method in serverIntent.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name != "From")
                        continue;
                    ParameterInfo[] args = method.GetParameters();
                    if (args.Length == 3 && args[2].ParameterType.Name == "ServerInfo")
                        return method.Invoke(null, [country, city, info]);
                    if (args.Length == 2 && args[1].ParameterType.Name == "ServerInfo")
                        return method.Invoke(null, [country, info]);
                }
            }
        }

        if (city != null)
        {
            Type? cityIntent = FindType(
                "ProtonVPN.Client.Logic.Connection.Contracts.Models.Intents.Locations.Cities.SingleCityLocationIntent");
            object? cityLocation = cityIntent?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m =>
                {
                    ParameterInfo[] args = m.GetParameters();
                    return m.Name == "From" && args.Length == 2 && args[0].ParameterType == typeof(string) && args[1].ParameterType == typeof(string);
                })
                ?.Invoke(null, [country, city]);
            if (cityLocation != null)
                return cityLocation;
        }

        Type? countryIntent = FindType(
            "ProtonVPN.Client.Logic.Connection.Contracts.Models.Intents.Locations.Countries.SingleCountryLocationIntent");
        return countryIntent?.GetMethod("From", BindingFlags.Public | BindingFlags.Static, [typeof(string)])
            ?.Invoke(null, [country]);
    }

    private static object? BuildFeatureIntent(object? server)
    {
        if (server == null)
            return null;

        object? features = server.GetType().GetProperty("Features")?.GetValue(server);
        ulong flags = features is IConvertible convertible ? convertible.ToUInt64(null) : 0;
        const ulong secureCore = 1;
        const ulong tor = 2;
        if ((flags & secureCore) != 0)
        {
            Type? type = FindType(
                "ProtonVPN.Client.Logic.Connection.Contracts.Models.Intents.Features.SecureCoreFeatureIntent");
            string? entry = GetString(server, "EntryCountry");
            ConstructorInfo? ctor = type?.GetConstructor([typeof(string)]);
            return ctor != null ? ctor.Invoke([entry]) : type?.GetConstructor(Type.EmptyTypes)?.Invoke(null);
        }

        if ((flags & tor) != 0)
        {
            Type? type = FindType(
                "ProtonVPN.Client.Logic.Connection.Contracts.Models.Intents.Features.TorFeatureIntent");
            return type?.GetConstructor(Type.EmptyTypes)?.Invoke(null);
        }

        return null;
    }

    private static object? FindServerById(string serverId)
    {
        object? loader = Runtime.ServersLoader;
        MethodInfo? method = loader?.GetType().GetMethod("GetById", [typeof(string)])
            ?? loader?.GetType().GetMethod("GetServers", Type.EmptyTypes);
        if (loader == null || method == null)
            return null;

        if (method.Name == "GetById")
            return method.Invoke(loader, [serverId]);

        if (method.Invoke(loader, null) is not IEnumerable servers)
            return null;

        foreach (object server in servers)
        {
            if (string.Equals(GetString(server, "Id"), serverId, StringComparison.Ordinal))
                return server;
        }

        return null;
    }

    private static Type? FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(fullName);
            if (type != null)
                return type;
        }

        return null;
    }

    private static MethodInfo? FindInstanceMethod(object instance, string name)
    {
        for (Type? type = instance.GetType(); type != null; type = type.BaseType)
        {
            MethodInfo? method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == 0 && !m.IsGenericMethod);
            if (method != null)
                return method;
        }

        foreach (Type iface in instance.GetType().GetInterfaces())
        {
            MethodInfo? method = iface.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == 0);
            if (method != null)
                return method;
        }

        return null;
    }

    private static string ReadPlanSummary()
    {
        object? settings = Runtime.Settings;
        object? plan = settings?.GetType().GetProperty("VpnPlan")?.GetValue(settings);
        if (plan == null)
            return "unknown";

        string name = plan.GetType().GetProperty("Title")?.GetValue(plan)?.ToString()
            ?? plan.GetType().GetProperty("Name")?.GetValue(plan)?.ToString()
            ?? plan.ToString()
            ?? "plan";
        string? maxTier = plan.GetType().GetProperty("MaxTier")?.GetValue(plan)?.ToString();
        string paid = plan.GetType().GetProperty("IsPaid")?.GetValue(plan) is true ? "paid" : "free";
        return $"{name}/{paid}/tier={maxTier}";
    }

    private static string? NormalizeCity(string? city)
    {
        if (string.IsNullOrWhiteSpace(city) || string.Equals(city, "(no city)", StringComparison.OrdinalIgnoreCase))
            return null;
        return city.Trim();
    }

    private static object? GetTrigger(string name)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType("ProtonVPN.StatisticalEvents.Contracts.Dimensions.VpnTriggerDimension");
            if (type is { IsEnum: true })
            {
                try
                {
                    return Enum.Parse(type, name, ignoreCase: true);
                }
                catch
                {
                    return null;
                }
            }
        }

        return null;
    }

    private static bool IsListable(object server)
    {
        if (!IsFree(server) || IsUnderMaintenance(server) || IsB2B(server))
            return false;

        string? tier = GetTierName(server);
        return !string.Equals(tier, "Internal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsB2B(object server)
    {
        MethodInfo? method = server.GetType().GetMethod("IsB2B", Type.EmptyTypes);
        return method?.Invoke(server, null) is true;
    }

    private static bool HasPhysicalServers(object server)
    {
        object? physical = server.GetType().GetProperty("Servers")?.GetValue(server);
        if (physical is not IEnumerable items)
            return false;

        IEnumerator enumerator = items.GetEnumerator();
        try
        {
            return enumerator.MoveNext();
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }

    private static string? GetTierName(object server)
    {
        return server.GetType().GetProperty("Tier")?.GetValue(server)?.ToString();
    }

    private static bool IsFree(object server)
    {
        MethodInfo? isFree = server.GetType().GetMethod("IsFree", Type.EmptyTypes);
        if (isFree?.Invoke(server, null) is bool free)
            return free;

        object? tier = server.GetType().GetProperty("Tier")?.GetValue(server);
        return tier != null && Convert.ToInt32(tier) == 0;
    }

    private static bool IsUnderMaintenance(object server)
    {
        MethodInfo? method = server.GetType().GetMethod("IsUnderMaintenance", Type.EmptyTypes);
        return method?.Invoke(server, null) is true;
    }

    private static string? GetString(object instance, string property)
    {
        return instance.GetType().GetProperty(property)?.GetValue(instance) as string;
    }

    private static int GetInt(object instance, string property)
    {
        object? value = instance.GetType().GetProperty(property)?.GetValue(instance);
        return value is IConvertible convertible ? convertible.ToInt32(null) : 0;
    }

    public static string CountryName(string code)
    {
        try
        {
            return new RegionInfo(code).EnglishName;
        }
        catch
        {
            return code.ToUpperInvariant();
        }
    }
}

internal sealed class FreeRegion
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int ServerCount { get; set; }
    public List<CityCount> Cities { get; set; } = [];
}

internal sealed class CityCount
{
    public string Name { get; set; } = "";
    public int ServerCount { get; set; }
    public List<ServerItem> Servers { get; set; } = [];
}

internal sealed class ServerItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Load { get; set; }
}
