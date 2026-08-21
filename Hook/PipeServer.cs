using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace ProtonNL.Hook;

internal static class PipeServer
{
    public const string PipeName = "ProtonNL";

    public static void Start()
    {
        Thread thread = new(Listen)
        {
            IsBackground = true,
            Name = "ProtonNL-pipe"
        };
        thread.Start();
    }

    private static void Listen()
    {
        while (true)
        {
            try
            {
                using NamedPipeServerStream pipe = new(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.None);

                Logger.Write("pipe waiting for GUI");
                pipe.WaitForConnection();
                Logger.Write("GUI connected");
                HandleClient(pipe);
            }
            catch (Exception ex)
            {
                Logger.Write("pipe error: " + ex.Message);
                Thread.Sleep(500);
            }
        }
    }

    private static void HandleClient(NamedPipeServerStream pipe)
    {
        using StreamReader reader = new(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, 1024, leaveOpen: true);
        using StreamWriter writer = new(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
        {
            AutoFlush = true
        };

        while (pipe.IsConnected)
        {
            string? line = reader.ReadLine();
            if (line == null)
                break;

            line = line.Trim();
            if (line.Length == 0)
                continue;

            try
            {
                writer.WriteLine(HandleLine(line));
            }
            catch (Exception ex)
            {
                Logger.Write("pipe request failed: " + ex.Message);
                writer.WriteLine(JsonSerializer.Serialize(new { op = "error", message = ex.GetBaseException().Message }));
            }
        }
    }

    private static string HandleLine(string line)
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;
        string op = root.TryGetProperty("op", out JsonElement opEl) ? opEl.GetString() ?? "" : "";

        switch (op.ToLowerInvariant())
        {
            case "list":
                return ConnectionBridge.SnapshotJson();
            case "connect":
            {
                string country = root.TryGetProperty("country", out JsonElement countryEl)
                    ? countryEl.GetString() ?? ""
                    : "";
                string? city = root.TryGetProperty("city", out JsonElement cityEl)
                    ? cityEl.GetString()
                    : null;
                string? serverId = root.TryGetProperty("serverId", out JsonElement serverEl)
                    ? serverEl.GetString()
                    : null;
                string? serverName = root.TryGetProperty("server", out JsonElement nameEl)
                    ? nameEl.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(country))
                    return JsonSerializer.Serialize(new { op = "error", message = "country required" });

                string message = ConnectionBridge.Connect(country, city, serverId, serverName);
                return JsonSerializer.Serialize(new { op = "ok", message });
            }
            default:
                return JsonSerializer.Serialize(new { op = "error", message = "unknown op" });
        }
    }
}
