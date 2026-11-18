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
    private readonly CancellationToken cancellationToken;
    private readonly Dictionary<string, JsonNode?> inputSchemas = new(StringComparer.Ordinal);
    private int nextRequestId;

    public HashSet<string> Tools { get; } = new(StringComparer.Ordinal);

    public static string DataRoot => Environment.GetEnvironmentVariable("CODEX_HOME")
        ?? Environment.GetEnvironmentVariable("CODEX_HOME", EnvironmentVariableTarget.User)
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    internal NativeBridge(Stream stream, string callerThreadId, CancellationToken cancellationToken = default)
    {
        this.stream = stream;
        this.callerThreadId = callerThreadId;
        this.cancellationToken = cancellationToken;
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
                var bridge = new NativeBridge(stream, caller, cancellationToken);
                JsonObject catalog = await bridge.RequestAsync("tools/list", new JsonObject { ["threadStartKind"] = "all" });
                bridge.ReadToolCatalog(catalog);
                if (!bridge.Tools.Contains("list_threads") || !bridge.Tools.Contains("list_projects"))
                    throw new InvalidDataException(Localization.Text("The Codex tool catalog is missing list access."));
                return bridge;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or TimeoutException or SocketException
                or OperationCanceledException or InvalidOperationException)
            {
                stream?.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                lastError = error;
            }
        }
        throw new IOException(Localization.Text("Codex is not connected yet. Open Codex and click Refresh."), lastError);
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
        RequireArguments(tool, arguments);

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
        string detail = response["contentItems"] is JsonArray content
            ? string.Join("\n", content.OfType<JsonObject>().Where(item => TextValue(item["type"]) == "inputText")
                .Select(item => TextValue(item["text"])).Where(value => value != null))
            : "";
        if (response["success"] is not JsonValue success || !success.TryGetValue(out bool succeeded))
            throw InvalidResponse();
        if (!succeeded)
            throw new InvalidOperationException(detail.Length > 0 ? detail : Localization.Text("客户端操作失败。"));
        return ParseObject(detail);
    }

    internal void ReadToolCatalog(JsonObject catalog)
    {
        if (catalog["tools"] is not JsonArray tools) throw InvalidResponse();
        foreach (JsonObject tool in tools.OfType<JsonObject>())
        {
            if (TextValue(tool["namespace"]) != "codex_app" || TextValue(tool["name"]) is not string name) continue;
            if (!Tools.Add(name)) throw InvalidResponse();
            if (tool.ContainsKey("inputSchema")) inputSchemas.Add(name, tool["inputSchema"]);
            else if (tool.ContainsKey("input_schema")) inputSchemas.Add(name, tool["input_schema"]);
        }
    }

    public void RequireTools(params string[] required)
    {
        string[] missing = required.Distinct(StringComparer.Ordinal).Where(tool => !Tools.Contains(tool)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(Localization.Text("当前客户端尚未提供这项排序操作。") + "\n" + string.Join(", ", missing));
    }

    public void RequireArguments(string tool, JsonObject arguments)
    {
        RequireTools(tool);
        // Earlier catalogs advertised only operation names. If a catalog supplies a schema,
        // check the fields used by this adapter before submitting the operation.
        if (inputSchemas.TryGetValue(tool, out JsonNode? schema) && !MatchesSchema(arguments, schema))
            throw new InvalidOperationException(Localization.Text("当前客户端尚未提供这项排序操作。") + "\n" + tool + " (input schema)");
    }

    private static bool MatchesSchema(JsonNode? value, JsonNode? schema)
    {
        if (schema is JsonValue literal && literal.TryGetValue(out bool allowed)) return allowed;
        if (schema is not JsonObject rules || rules.ContainsKey("$ref") || rules.ContainsKey("not")
            || rules.ContainsKey("if") || rules.ContainsKey("dependentSchemas")) return false;
        if (rules["required"] is JsonArray required
            && (value is not JsonObject record || required.Any(key => TextValue(key) is not string name || !record.ContainsKey(name)))) return false;
        if (rules.TryGetPropertyValue("const", out JsonNode? constant) && !JsonNode.DeepEquals(value, constant)) return false;
        if (rules["enum"] is JsonArray choices && !choices.Any(choice => JsonNode.DeepEquals(value, choice))) return false;
        if (rules["anyOf"] is JsonArray any && !any.Any(rule => MatchesSchema(value, rule))) return false;
        if (rules["oneOf"] is JsonArray one && one.Count(rule => MatchesSchema(value, rule)) != 1) return false;
        if (rules["allOf"] is JsonArray all && !all.All(rule => MatchesSchema(value, rule))) return false;
        if (rules["type"] is JsonArray types && !types.Any(type => MatchesType(value, TextValue(type)))) return false;
        if (TextValue(rules["type"]) is string typeName && !MatchesType(value, typeName)) return false;
        if (value is JsonObject fields)
        {
            var properties = rules["properties"] as JsonObject;
            return fields.All(field => properties?.TryGetPropertyValue(field.Key, out JsonNode? fieldRules) == true
                ? MatchesSchema(field.Value, fieldRules)
                : rules["additionalProperties"] == null || MatchesSchema(field.Value, rules["additionalProperties"]));
        }
        return value is not JsonArray items || rules["items"] == null || items.All(item => MatchesSchema(item, rules["items"]));
    }

    private static bool MatchesType(JsonNode? value, string? type) => type switch
    {
        "object" => value is JsonObject,
        "array" => value is JsonArray,
        "string" => value?.GetValueKind() == JsonValueKind.String,
        "boolean" => value?.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
        "number" => value?.GetValueKind() == JsonValueKind.Number,
        "integer" => value is JsonValue number && number.TryGetValue(out long _),
        "null" => value == null,
        _ => false
    };

    private static string? TextValue(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static InvalidDataException InvalidResponse() => new(Localization.Text("客户端返回了无法读取的排序回执。"));

    private static JsonObject ParseObject(string text)
    {
        try { return JsonNode.Parse(text) as JsonObject ?? throw InvalidResponse(); }
        catch (JsonException error) { throw new InvalidDataException(Localization.Text("客户端返回了无法读取的排序回执。"), error); }
    }

    private async Task<JsonObject> RequestAsync(string method, JsonObject parameters)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        int requestId = ++nextRequestId;
        byte[] payload = Encoding.UTF8.GetBytes(new JsonObject
        {
            ["id"] = requestId, ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters
        }.ToJsonString());
        if (payload.Length > MaximumFrameBytes) throw new InvalidDataException(Localization.Text("请求超出本地接口支持的大小。"));
        try
        {
            await stream.WriteAsync(BitConverter.GetBytes(payload.Length), timeout.Token);
            await stream.WriteAsync(payload, timeout.Token);
            byte[] header = new byte[4];
            await stream.ReadExactlyAsync(header, timeout.Token);
            int length = BitConverter.ToInt32(header);
            if (length < 1 || length > MaximumFrameBytes) throw new InvalidDataException(Localization.Text("客户端返回了异常长度。"));
            byte[] body = new byte[length];
            await stream.ReadExactlyAsync(body, timeout.Token);
            JsonObject message = ParseObject(Encoding.UTF8.GetString(body));
            if (message["id"] is not JsonValue id || !id.TryGetValue(out int number) || number != requestId)
                throw new InvalidDataException(Localization.Text("客户端回执序号异常。"));
            if (message["error"] is JsonObject error)
                throw new InvalidOperationException(TextValue(error["message"]) ?? Localization.Text("客户端操作失败。"));
            return message["result"] as JsonObject ?? throw new InvalidDataException(Localization.Text("客户端回执缺少结果。"));
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException(Localization.Text("连接超时，请刷新后重试。"), error);
        }
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
                    string? id = JsonNode.Parse(line) is JsonObject record ? TextValue(record["id"]) : null;
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
