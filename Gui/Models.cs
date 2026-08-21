namespace ProtonNL.Gui;

internal sealed class ListResponse
{
    public string? ForcedCountry { get; set; }
    public string? ForcedCity { get; set; }
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
}

internal sealed class ConnectResponse
{
    public string? Op { get; set; }
    public string? Message { get; set; }
}

internal sealed class RegionRow
{
    public required string Code { get; init; }
    public string? City { get; init; }
    public required string Title { get; init; }
    public int ServerCount { get; init; }
}
