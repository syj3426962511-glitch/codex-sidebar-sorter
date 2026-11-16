using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SidebarSorter;

internal sealed class NativeBridge : IDisposable
{
    private const int MaximumFrameBytes = 8 * 1024 * 1024;
    private readonly Stream stream;
    private readonly string callerThreadId;
    private int nextRequestId;

    public HashSet<string> Tools { get; } = new(StringComparer.Ordinal);

    public static string DataRoot => Environment.GetEnvironmentVariable("CODEX_HOME")
        ?? Environment.GetEnvironmentVariable("CODEX_HOME", EnvironmentVariableTarget.User)
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    internal NativeBridge(Stream stream, string callerThreadId)
    {
        this.stream = stream;
        this.callerThreadId = callerThreadId;
    }

    public static async Task<NativeBridge> ConnectAsync(CancellationToken cancellationToken = default)
    {
        string caller = FindCallerThread();
        string[] candidates = FindEndpoints();
        Exception? lastError = null;
        foreach (string candidate in candidates)
        {
            Stream? stream = null;
            try
            {
                using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectionTimeout.CancelAfter(800);
                if (OperatingSystem.IsWindows())
                {
                    var pipe = new NamedPipeClientStream(".", Path.GetFileName(candidate), PipeDirection.InOut, PipeOptions.Asynchronous);
                    stream = pipe;
                    await pipe.ConnectAsync(connectionTimeout.Token);
                }
                else
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(new UnixDomainSocketEndPoint(candidate), connectionTimeout.Token);
                        stream = new NetworkStream(socket, ownsSocket: true);
                    }
                    catch { socket.Dispose(); throw; }
                }
                var bridge = new NativeBridge(stream, caller);
                JsonObject catalog = await bridge.RequestAsync("tools/list", new JsonObject { ["threadStartKind"] = "all" });
                foreach (JsonNode? tool in catalog["tools"]!.AsArray())
                {
                    if (tool?["namespace"]?.GetValue<string>() == "codex_app")
                        bridge.Tools.Add(tool["name"]!.GetValue<string>());
                }
                if (!bridge.Tools.Contains("list_threads") || !bridge.Tools.Contains("list_projects"))
                    throw new InvalidDataException(Localization.Text("客户端返回的工具目录缺少列表读取能力。"));
                return bridge;
            }
            catch (Exception error) when (error is IOException or TimeoutException or SocketException or OperationCanceledException)
            {
                stream?.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                lastError = error;
            }
        }
        throw new IOException(Localization.Text("客户端连接尚未就绪。打开客户端后点击刷新。"), lastError);
    }

    internal static string[] FindEndpoints()
    {
        if (OperatingSystem.IsWindows()) return Directory.GetFiles(@"\\.\pipe\", "codex-app-tools-*");
        string directory = OperatingSystem.IsMacOS() ? "/tmp/codex-app-tools" : "/tmp/codex-app-tools-" + geteuid();
        return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.sock") : [];
    }

    [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint geteuid();

    public async Task<JsonObject> CallAsync(string tool, JsonObject arguments)
    {
        RequireTools(tool);

        JsonObject response = await RequestAsync("tools/call", new JsonObject
        {
            ["arguments"] = arguments,
            ["callerSource"] = "codex",
            ["callId"] = "mcp-call-" + Guid.NewGuid(),
            ["namespace"] = "codex_app",
            ["threadId"] = callerThreadId,
            ["tool"] = tool,
            ["turnId"] = "mcp-turn-" + Guid.NewGuid(),
            ["hostId"] = "local"
        });
        string text = string.Join("\n", response["contentItems"]!.AsArray()
            .Where(item => item?["type"]?.GetValue<string>() == "inputText")
            .Select(item => item!["text"]!.GetValue<string>()));
        if (response["success"]?.GetValue<bool>() != true)
            throw new InvalidOperationException(text);
        return JsonNode.Parse(text) as JsonObject ?? throw new InvalidDataException(Localization.Text("客户端返回了无法读取的排序回执。"));
    }

    public void RequireTools(params string[] required)
    {
        string[] missing = required.Distinct(StringComparer.Ordinal).Where(tool => !Tools.Contains(tool)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(Localization.Text("当前客户端尚未提供这项排序操作。") + "\n" + string.Join(", ", missing));
    }

    private async Task<JsonObject> RequestAsync(string method, JsonObject parameters)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        int requestId = ++nextRequestId;
        byte[] payload = Encoding.UTF8.GetBytes(new JsonObject
        {
            ["id"] = requestId, ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters
        }.ToJsonString());
        if (payload.Length > MaximumFrameBytes) throw new InvalidDataException(Localization.Text("请求超出本地接口支持的大小。"));
        await stream.WriteAsync(BitConverter.GetBytes(payload.Length), timeout.Token);
        await stream.WriteAsync(payload, timeout.Token);
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, timeout.Token);
        int length = BitConverter.ToInt32(header);
        if (length < 1 || length > MaximumFrameBytes) throw new InvalidDataException(Localization.Text("客户端返回了异常长度。"));
        byte[] body = new byte[length];
        await stream.ReadExactlyAsync(body, timeout.Token);
        JsonObject message = JsonNode.Parse(body)!.AsObject();
        if (message["id"]?.GetValue<int>() != requestId) throw new InvalidDataException(Localization.Text("客户端回执序号异常。"));
        if (message["error"] is JsonObject error)
            throw new InvalidOperationException(error["message"]?.GetValue<string>() ?? Localization.Text("客户端操作失败。"));
        return message["result"]?.AsObject() ?? throw new InvalidDataException(Localization.Text("客户端回执缺少结果。"));
    }

    private static string FindCallerThread()
    {
        string index = Path.Combine(DataRoot, "session_index.jsonl");
        if (File.Exists(index))
        {
            using var input = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int length = (int)Math.Min(input.Length, 128 * 1024);
            input.Seek(-length, SeekOrigin.End);
            byte[] buffer = new byte[length];
            input.ReadExactly(buffer);
            foreach (string line in Encoding.UTF8.GetString(buffer).Split('\n').Reverse())
            {
                try
                {
                    string? id = JsonNode.Parse(line)?["id"]?.GetValue<string>();
                    if (Guid.TryParse(id, out _)) return id!;
                }
                catch (JsonException) { }
            }
        }
        string? inherited = Environment.GetEnvironmentVariable("CODEX_THREAD_ID");
        if (Guid.TryParse(inherited, out _)) return inherited!;
        throw new IOException(Localization.Text("请先在客户端建立一条本机聊天，随后点击刷新。"));
    }

    public void Dispose() => stream.Dispose();
}
