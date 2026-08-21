using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace ProtonNL.Gui;

internal sealed class HookClient : IDisposable
{
    private const string PipeName = "ProtonNL";
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public bool Connected => _pipe is { IsConnected: true };

    public async Task ConnectAsync(CancellationToken token)
    {
        DisposePipe();
        NamedPipeClientStream pipe = new(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(2500);
        await pipe.ConnectAsync(timeout.Token);
        _pipe = pipe;
        _reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
        _writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
    }

    public async Task<ListResponse> ListAsync(CancellationToken token)
    {
        string json = await RequestAsync("""{"op":"list"}""", token);
        return JsonSerializer.Deserialize<ListResponse>(json, JsonOptions) ?? new ListResponse();
    }

    public async Task<string> ConnectRegionAsync(RegionRow row, CancellationToken token)
    {
        var payload = new Dictionary<string, string?>
        {
            ["op"] = "connect",
            ["country"] = row.Code
        };
        if (!string.IsNullOrWhiteSpace(row.City))
            payload["city"] = row.City;
        if (!string.IsNullOrWhiteSpace(row.ServerId))
        {
            payload["serverId"] = row.ServerId;
            payload["server"] = row.ServerName ?? row.Title;
        }

        string json = await RequestAsync(JsonSerializer.Serialize(payload), token);
        ConnectResponse? response = JsonSerializer.Deserialize<ConnectResponse>(json, JsonOptions);
        return response?.Message ?? json;
    }

    private async Task<string> RequestAsync(string line, CancellationToken token)
    {
        if (_writer == null || _reader == null || _pipe is not { IsConnected: true })
            throw new InvalidOperationException("Not connected to ProtonNL hook. Inject first.");

        await _writer.WriteLineAsync(line.AsMemory(), token);
        string? reply = await _reader.ReadLineAsync(token);
        if (reply == null)
            throw new IOException("Hook closed the pipe.");
        return reply;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private void DisposePipe()
    {
        try { _writer?.Dispose(); } catch { /* ignore */ }
        try { _reader?.Dispose(); } catch { /* ignore */ }
        try { _pipe?.Dispose(); } catch { /* ignore */ }
        _writer = null;
        _reader = null;
        _pipe = null;
    }

    public void Dispose() => DisposePipe();
}
