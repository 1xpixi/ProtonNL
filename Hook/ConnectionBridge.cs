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
            Regions = GetFreeRegions(),
            Status = GetStatus()
        }, JsonOptions);
    }

    public static StatusInfo GetStatus()
    {
        StatusInfo info = new()
        {
            Ready = Runtime.ConnectionManager != null,
            Protocols = ProtocolOptions(),
            Protocol = ReadProtocolName(Runtime.Settings)
        };

        object? manager = Runtime.ConnectionManager;
        if (manager == null)
            return info;

        info.State = ReadState(manager);
        info.Protected = info.State == "connected";
        info.Blocked = manager.GetType().GetProperty("IsNetworkBlocked")?.GetValue(manager) is true;
        ReadTraffic(info);

        object? details = manager.GetType().GetProperty("CurrentConnectionDetails")?.GetValue(manager);
        if (details == null)
            return info;

        object? server = details.GetType().GetProperty("Server")?.GetValue(details);
        info.ServerId = GetString(details, "ServerId") ?? GetString(server ?? details, "Id");
        info.Server = GetString(details, "ServerName") ?? GetString(server ?? details, "Name");
        info.City = GetString(details, "City");
        info.CountryCode = GetString(details, "ExitCountryCode") ?? GetString(server ?? details, "ExitCountry");
        info.Country = string.IsNullOrWhiteSpace(info.CountryCode) ? null : CountryName(info.CountryCode);
        info.Load = GetInt(server ?? details, "Load");
        info.ActiveProtocol = details.GetType().GetProperty("Protocol")?.GetValue(details)?.ToString();

        object? established = details.GetType().GetProperty("EstablishedConnectionTimeUtc")?.GetValue(details);
        if (established is DateTime utc && utc != default)
            info.ConnectedAt = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("o");

        info.Ip = ReadIp(details, server);
        return info;
    }

    private static void ReadTraffic(StatusInfo info)
    {
        object? traffic = Runtime.TrafficManager;
        if (traffic == null)
            return;

        object? volume = traffic.GetType().GetMethod("GetVolume", Type.EmptyTypes)?.Invoke(traffic, null);
        if (volume == null)
            return;

        info.BytesDown = GetULong(volume, "BytesDownloaded");
        info.BytesUp = GetULong(volume, "BytesUploaded");
    }

    private static string? ReadIp(object details, object? server)
    {
        object? ip = details.GetType().GetProperty("ServerIpAddress")?.GetValue(details);
        string? v4 = ip?.GetType().GetProperty("Ipv4Address")?.GetValue(ip) as string;
        if (!string.IsNullOrWhiteSpace(v4))
            return v4;

        string? exit = server == null ? null : GetString(server, "ExitIp");
        if (!string.IsNullOrWhiteSpace(exit))
            return exit;

        return details.GetType().GetProperty("EntryIpAddress")?.GetValue(details) as string;
    }

    private static ulong GetULong(object instance, string property)
    {
        object? value = instance.GetType().GetProperty(property)?.GetValue(instance);
        return value is IConvertible convertible ? convertible.ToUInt64(null) : 0;
    }

    public static string Disconnect()
    {
        object? manager = Runtime.ConnectionManager;
        if (manager == null)
            return "ProtonVPN is not ready yet.";

        MethodInfo? disconnect = manager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m => m.Name == "DisconnectAsync" && m.GetParameters().Length == 1);
        if (disconnect == null)
            return "Disconnect is not available.";

        object trigger = GetTrigger("Exit") ?? GetTrigger("Disconnect") ?? GetTrigger("ConnectionCard")
            ?? throw new InvalidOperationException("disconnect trigger not found");
        disconnect.Invoke(manager, [trigger]);
        Logger.Write("disconnect requested");
        return "Disconnecting…";
    }

    public static string SetProtocol(string protocol)
    {
        object? settings = Runtime.Settings;
        if (settings == null)
            return "Settings are not ready yet.";

        PropertyInfo? prop = settings.GetType().GetProperty("VpnProtocol");
        if (prop == null)
            return "Protocol setting not found.";

        Type enumType = prop.PropertyType;
        object parsed;
        try
        {
            parsed = Enum.Parse(enumType, protocol, ignoreCase: true);
        }
        catch
        {
            return "Unknown protocol.";
        }

        prop.SetValue(settings, parsed);
        Logger.Write("protocol set to " + parsed);

        object? manager = Runtime.ConnectionManager;
        bool connected = manager?.GetType().GetProperty("IsConnected")?.GetValue(manager) is true;
        if (connected)
        {
            MethodInfo? reconnect = manager!.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "ReconnectAsync" && m.GetParameters().Length == 1);
            object? trigger = GetTrigger("NewConnection") ?? GetTrigger("ConnectionCard");
            if (reconnect != null && trigger != null)
                reconnect.Invoke(manager, [trigger]);
            return $"Switching to {LabelForProtocol(parsed.ToString()!)}…";
        }

        return $"Protocol set to {LabelForProtocol(parsed.ToString()!)}.";
    }

    private static string ReadState(object manager)
    {
        if (manager.GetType().GetProperty("IsConnected")?.GetValue(manager) is true)
            return "connected";
        if (manager.GetType().GetProperty("IsConnecting")?.GetValue(manager) is true)
            return "connecting";
        return "disconnected";
    }

    private static string? ReadProtocolName(object? settings)
    {
        return settings?.GetType().GetProperty("VpnProtocol")?.GetValue(settings)?.ToString();
    }

    private static List<ProtocolOption> ProtocolOptions()
    {
        string[] names =
        [
            "Smart", "WireGuardUdp", "WireGuardTcp", "WireGuardTls",
            "OpenVpnUdp", "OpenVpnTcp", "ProTunUdp", "ProTunTcp", "ProTunTls"
        ];
        return names.Select(n => new ProtocolOption { Id = n, Label = LabelForProtocol(n) }).ToList();
    }

    private static string LabelForProtocol(string id) => id switch
    {
        "Smart" => "Smart",
        "WireGuardUdp" => "WireGuard UDP",
        "WireGuardTcp" => "WireGuard TCP",
        "WireGuardTls" => "WireGuard TLS",
        "OpenVpnUdp" => "OpenVPN UDP",
        "OpenVpnTcp" => "OpenVPN TCP",
        "ProTunUdp" => "Proton UDP",
        "ProTunTcp" => "Proton TCP",
        "ProTunTls" => "Proton TLS",
        _ => id
    };

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
    public StatusInfo? Status { get; set; }
}

internal sealed class StatusInfo
{
    public bool Ready { get; set; }
    public string State { get; set; } = "disconnected";
    public string? Server { get; set; }
    public string? ServerId { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? CountryCode { get; set; }
    public int Load { get; set; }
    public string? Protocol { get; set; }
    public string? ActiveProtocol { get; set; }
    public string? ConnectedAt { get; set; }
    public string? Ip { get; set; }
    public ulong BytesDown { get; set; }
    public ulong BytesUp { get; set; }
    public bool Protected { get; set; }
    public bool Blocked { get; set; }
    public List<ProtocolOption> Protocols { get; set; } = [];
}

internal sealed class ProtocolOption
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
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
