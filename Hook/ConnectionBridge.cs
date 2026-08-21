using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProtonNL.Hook;

internal static class ConnectionBridge
{
    public static List<FreeRegion> GetFreeRegions()
    {
        object? loader = Runtime.ServersLoader;
        if (loader == null)
            return [];

        MethodInfo? method = loader.GetType().GetMethod("GetFreeServers", Type.EmptyTypes)
            ?? loader.GetType().GetMethod("GetServers", Type.EmptyTypes);
        if (method == null)
            return [];

        if (method.Invoke(loader, null) is not IEnumerable servers)
            return [];

        Dictionary<string, FreeRegion> regions = new(StringComparer.OrdinalIgnoreCase);
        foreach (object server in servers)
        {
            if (!IsFree(server) || IsUnderMaintenance(server))
                continue;

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

        Runtime.ForcedCountry = country;
        Runtime.ForcedCity = city;
        Runtime.ForcedServerId = serverId;

        try
        {
            object intent = GetFreeDefaultIntent()
                ?? throw new InvalidOperationException("ConnectionIntent.FreeDefault not found");
            object trigger = GetTrigger("CountriesServer")
                ?? GetTrigger("CountriesCountry")
                ?? GetTrigger("NewConnection")
                ?? GetTrigger("ConnectionCard")
                ?? throw new InvalidOperationException("VpnTriggerDimension not found");

            MethodInfo connect = manager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "ConnectAsync" && m.GetParameters().Length == 2)
                ?? throw new InvalidOperationException("ConnectAsync not found");

            object? task = connect.Invoke(manager, [trigger, intent]);
            Logger.Write($"connect requested country={country} city={city ?? "(any)"} server={serverId ?? "(any)"} task={task != null}");
            if (serverId != null)
                return $"Connecting to {serverName ?? serverId}...";
            if (city != null)
                return $"Connecting to {CountryName(country)} / {city}...";
            return $"Connecting to {CountryName(country)} free servers...";
        }
        catch (Exception ex)
        {
            Logger.Write("connect failed: " + ex);
            return "Connect failed: " + ex.GetBaseException().Message;
        }
    }

    public static string SnapshotJson()
    {
        return JsonSerializer.Serialize(new ListResponse
        {
            ForcedCountry = Runtime.ForcedCountry,
            ForcedCity = Runtime.ForcedCity,
            ForcedServerId = Runtime.ForcedServerId,
            Ready = Runtime.ConnectionManager != null && Runtime.ServersLoader != null,
            Regions = GetFreeRegions()
        }, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static object? GetFreeDefaultIntent()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType("ProtonVPN.Client.Logic.Connection.Contracts.Models.Intents.ConnectionIntent");
            PropertyInfo? freeDefault = type?.GetProperty("FreeDefault", BindingFlags.Public | BindingFlags.Static);
            if (freeDefault != null)
                return freeDefault.GetValue(null);
        }

        return null;
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

internal sealed class ListResponse
{
    public string? ForcedCountry { get; set; }
    public string? ForcedCity { get; set; }
    public string? ForcedServerId { get; set; }
    public bool Ready { get; set; }
    public List<FreeRegion> Regions { get; set; } = [];
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
