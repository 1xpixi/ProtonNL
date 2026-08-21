using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ProtonNL.Hook;

internal static class WebServer
{
    private const int FirstPort = 27180;
    private const int LastPort = 27189;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static void Start()
    {
        Thread thread = new(Listen)
        {
            IsBackground = true,
            Name = "ProtonNL-http"
        };
        thread.Start();
    }

    private static void Listen()
    {
        HttpListener? listener = null;
        int port = FirstPort;
        for (; port <= LastPort; port++)
        {
            HttpListener candidate = new();
            candidate.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                candidate.Start();
                listener = candidate;
                break;
            }
            catch
            {
                candidate.Close();
            }
        }

        if (listener == null)
        {
            Logger.Write("http listener failed to bind 27180-27189");
            return;
        }

        string url = $"http://127.0.0.1:{port}/";
        Logger.Write("http listening " + url);
        OpenBrowser(url);

        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = listener.GetContext();
            }
            catch
            {
                break;
            }

            try
            {
                Handle(ctx);
            }
            catch (Exception ex)
            {
                Logger.Write("http error: " + ex.Message);
                try { ctx.Response.Abort(); } catch { /* ignore */ }
            }
        }
    }

    private static void Handle(HttpListenerContext ctx)
    {
        string path = ctx.Request.Url?.AbsolutePath ?? "/";
        string method = ctx.Request.HttpMethod.ToUpperInvariant();

        if (method == "GET" && (path == "/" || path == "/index.html"))
        {
            Write(ctx, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(ReadIndex()));
            return;
        }

        if (method == "GET" && path == "/api/list")
        {
            Write(ctx, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(ConnectionBridge.SnapshotJson()));
            return;
        }

        if (method == "GET" && path == "/api/status")
        {
            WriteJson(ctx, 200, ConnectionBridge.GetStatus());
            return;
        }

        if (method == "POST" && path == "/api/disconnect")
        {
            WriteJson(ctx, 200, new { op = "ok", message = ConnectionBridge.Disconnect() });
            return;
        }

        if (method == "POST" && path == "/api/protocol")
        {
            using StreamReader protoReader = new(ctx.Request.InputStream, ctx.Request.ContentEncoding);
            ProtocolBody? proto = JsonSerializer.Deserialize<ProtocolBody>(protoReader.ReadToEnd(), Json);
            if (proto == null || string.IsNullOrWhiteSpace(proto.Protocol))
            {
                WriteJson(ctx, 400, new { op = "error", message = "protocol required" });
                return;
            }

            WriteJson(ctx, 200, new { op = "ok", message = ConnectionBridge.SetProtocol(proto.Protocol) });
            return;
        }

        if (method == "POST" && path == "/api/connect")
        {
            using StreamReader reader = new(ctx.Request.InputStream, ctx.Request.ContentEncoding);
            string body = reader.ReadToEnd();
            ConnectBody? req = JsonSerializer.Deserialize<ConnectBody>(body, Json);
            if (req == null || string.IsNullOrWhiteSpace(req.Country))
            {
                WriteJson(ctx, 400, new { op = "error", message = "country required" });
                return;
            }

            string message = ConnectionBridge.Connect(req.Country, req.City, req.ServerId, req.Server);
            WriteJson(ctx, 200, new { op = "ok", message });
            return;
        }

        Write(ctx, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("not found"));
    }

    private static string ReadIndex()
    {
        string? dir = Path.GetDirectoryName(typeof(WebServer).Assembly.Location);
        string path = Path.Combine(dir ?? "", "wwwroot", "index.html");
        if (File.Exists(path))
            return File.ReadAllText(path);
        Logger.Write("wwwroot/index.html missing: " + path);
        return "<!doctype html><meta charset=utf-8><body style='background:#0c0c0d;color:#ececef;font:14px sans-serif'>ProtonNL UI file is missing.";
    }

    private static void WriteJson(HttpListenerContext ctx, int status, object payload)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        Write(ctx, status, "application/json; charset=utf-8", bytes);
    }

    private static void Write(HttpListenerContext ctx, int status, string type, byte[] bytes)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = type;
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.OutputStream.Close();
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            Logger.Write("opened " + url);
        }
        catch (Exception ex)
        {
            Logger.Write("failed to open browser: " + ex.Message);
        }
    }

    private sealed class ConnectBody
    {
        public string? Country { get; set; }
        public string? City { get; set; }
        public string? ServerId { get; set; }
        public string? Server { get; set; }
    }

    private sealed class ProtocolBody
    {
        public string? Protocol { get; set; }
    }
}
