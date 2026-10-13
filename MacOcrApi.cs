using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace STranslate.Plugin.Ocr.MacOcrHttp;

// Plain HTTP client for mac-ocr-http's native API. No STranslate or WPF types,
// so it can be exercised against a mock server on any OS (see tests/MockCheck).

public sealed record MacOcrOptions
{
    public string BaseUrl { get; init; } = "http://127.0.0.1:8741";
    public string Token { get; init; } = string.Empty;
    /// <summary>Vision language codes, e.g. zh-Hans, en-US. Empty: server default.</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];
    /// <summary>accurate or fast.</summary>
    public string Level { get; init; } = "accurate";
    /// <summary>hans, auto, or empty for the server default.</summary>
    public string Script { get; init; } = string.Empty;
    /// <summary>rows or document.</summary>
    public string Layout { get; init; } = "rows";
    /// <summary>true: fragments=drop (server default); false: fragments=keep.</summary>
    public bool DropFragments { get; init; } = true;
    public int TimeoutSeconds { get; init; } = 60;
    /// <summary>Ignore the system proxy. A LAN or loopback server should not go through one.</summary>
    public bool BypassProxy { get; init; } = true;
}

public sealed class MacOcrException(string message, HttpStatusCode? status = null, Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode? Status { get; } = status;
}

public sealed class OcrBox
{
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("width")] public double Width { get; set; }
    [JsonPropertyName("height")] public double Height { get; set; }
}

public sealed class OcrPoint
{
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
}

public sealed class OcrQuad
{
    [JsonPropertyName("topLeft")] public OcrPoint? TopLeft { get; set; }
    [JsonPropertyName("topRight")] public OcrPoint? TopRight { get; set; }
    [JsonPropertyName("bottomRight")] public OcrPoint? BottomRight { get; set; }
    [JsonPropertyName("bottomLeft")] public OcrPoint? BottomLeft { get; set; }
}

public sealed class OcrLineDto
{
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    [JsonPropertyName("confidence")] public double Confidence { get; set; }
    [JsonPropertyName("bbox")] public OcrBox? BBox { get; set; }
    [JsonPropertyName("quad")] public OcrQuad? Quad { get; set; }
}

public sealed class OcrRowDto
{
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    [JsonPropertyName("bbox")] public OcrBox? BBox { get; set; }
    [JsonPropertyName("lineIndices")] public List<int> LineIndices { get; set; } = [];
}

public sealed class OcrBlockDto
{
    [JsonPropertyName("bbox")] public OcrBox? BBox { get; set; }
    [JsonPropertyName("rowIndices")] public List<int> RowIndices { get; set; } = [];
}

public sealed class OcrResponseDto
{
    [JsonPropertyName("ok")] public bool? Ok { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("elapsedMs")] public int ElapsedMs { get; set; }
    [JsonPropertyName("lines")] public List<OcrLineDto> Lines { get; set; } = [];
    [JsonPropertyName("rows")] public List<OcrRowDto> Rows { get; set; } = [];
    [JsonPropertyName("blocks")] public List<OcrBlockDto> Blocks { get; set; } = [];
}

/// <summary>A row in reading order, with its box scaled to the caller's pixel grid.</summary>
public sealed record MappedLine(string Text, IReadOnlyList<(float X, float Y)> Points);

/// <summary>A block (column / paragraph) with its rows in reading order.</summary>
public sealed record MappedBlock(IReadOnlyList<(float X, float Y)> Points, IReadOnlyList<MappedLine> Lines);

public sealed class MacOcrClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly MacOcrOptions _options;

    public MacOcrClient(MacOcrOptions options, HttpMessageHandler? handler = null)
    {
        _options = options;
        handler ??= new SocketsHttpHandler
        {
            UseProxy = !options.BypassProxy,
            ConnectTimeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 10)),
        };
        _http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 600)),
        };
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Base URL without a trailing slash; an accidental /ocr, /health or /v1 suffix is removed.</summary>
    public static string NormalizeBaseUrl(string? url)
    {
        var value = (url ?? string.Empty).Trim();
        if (value.Length == 0) value = "http://127.0.0.1:8741";
        if (!value.Contains("://", StringComparison.Ordinal)) value = "http://" + value;
        value = value.TrimEnd('/');
        foreach (var suffix in new[] { "/v1/chat/completions", "/v1", "/ocr", "/health" })
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^suffix.Length].TrimEnd('/');
                break;
            }
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new MacOcrException($"服务地址无效 / Invalid base URL: {url}");
        return value;
    }

    /// <summary>Request body for POST /ocr (JSON with base64 image).</summary>
    public static Dictionary<string, object> BuildBody(byte[] image, MacOcrOptions options, IReadOnlyList<string>? languages)
    {
        var body = new Dictionary<string, object>
        {
            ["image"] = Convert.ToBase64String(image),
            ["level"] = string.IsNullOrWhiteSpace(options.Level) ? "accurate" : options.Level.Trim(),
            ["layout"] = string.IsNullOrWhiteSpace(options.Layout) ? "rows" : options.Layout.Trim(),
            ["fragments"] = options.DropFragments ? "drop" : "keep",
        };
        var langs = (languages is { Count: > 0 } ? languages : options.Languages)
            .Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();
        if (langs.Count > 0) body["languages"] = langs;
        if (!string.IsNullOrWhiteSpace(options.Script)) body["script"] = options.Script.Trim();
        return body;
    }

    public async Task<OcrResponseDto> RecognizeAsync(byte[] image, IReadOnlyList<string>? languages, CancellationToken ct)
    {
        if (image is not { Length: > 0 }) throw new MacOcrException("图片为空 / Empty image");
        var url = NormalizeBaseUrl(_options.BaseUrl) + "/ocr";
        var json = JsonSerializer.Serialize(BuildBody(image, _options, languages));
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        AddAuth(request);
        var (status, text) = await SendAsync(request, ct).ConfigureAwait(false);
        if (status != HttpStatusCode.OK) throw ErrorFor(status, text);
        try
        {
            return JsonSerializer.Deserialize<OcrResponseDto>(text)
                ?? throw new MacOcrException("服务返回为空 / Empty response");
        }
        catch (JsonException ex)
        {
            throw new MacOcrException($"无法解析服务返回 / Could not parse the response: {Shorten(text)}", status, ex);
        }
    }

    /// <summary>GET /health (no auth) then GET /languages (auth). Returns a short summary.</summary>
    public async Task<string> ValidateAsync(CancellationToken ct)
    {
        var baseUrl = NormalizeBaseUrl(_options.BaseUrl);
        using (var health = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/health"))
        {
            var (status, text) = await SendAsync(health, ct).ConfigureAwait(false);
            if (status != HttpStatusCode.OK || !text.Contains("\"ok\"", StringComparison.Ordinal))
                throw new MacOcrException($"/health 返回 {(int)status}，这不像 mac-ocr-http / /health returned {(int)status}; is this mac-ocr-http? {Shorten(text)}", status);
        }
        using var languages = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/languages");
        AddAuth(languages);
        var (code, body) = await SendAsync(languages, ct).ConfigureAwait(false);
        if (code != HttpStatusCode.OK) throw ErrorFor(code, body);
        var count = 0;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("accurate", out var acc) && acc.ValueKind == JsonValueKind.Array)
                count = acc.GetArrayLength();
        }
        catch (JsonException) { }
        return count > 0 ? $"OK · {count} languages" : "OK";
    }

    private void AddAuth(HttpRequestMessage request)
    {
        var token = (_options.Token ?? string.Empty).Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token[7..].Trim();
        if (token.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<(HttpStatusCode, string)> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (response.StatusCode, text);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            throw new MacOcrException($"请求超时（{_options.TimeoutSeconds}s）/ Request timed out", null, ex);
        }
        catch (HttpRequestException ex)
        {
            var target = request.RequestUri?.GetLeftPart(UriPartial.Authority) ?? _options.BaseUrl;
            var socket = FindSocketError(ex);
            var message = socket switch
            {
                SocketError.ConnectionRefused => $"连接被拒绝：{target} 上没有服务在监听，请确认 mac-ocr-http 已启动且监听该地址 / Connection refused: nothing is listening at {target}",
                SocketError.HostUnreachable or SocketError.NetworkUnreachable => $"无法到达 {target}，请检查网络或 IP / Host unreachable: {target}",
                SocketError.TimedOut => $"连接 {target} 超时，请检查 IP、端口和防火墙 / Connection to {target} timed out",
                SocketError.HostNotFound => $"无法解析主机名 / Host not found: {target}",
                _ => $"无法连接 {target} / Could not connect to {target}: {ex.Message}",
            };
            throw new MacOcrException(message, null, ex);
        }
    }

    private static SocketError? FindSocketError(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
            if (e is SocketException se) return se.SocketErrorCode;
        return null;
    }

    public static MacOcrException ErrorFor(HttpStatusCode status, string body)
    {
        var detail = ServerError(body);
        var message = (int)status switch
        {
            401 => "认证失败（401）：Token 缺失或错误，请填写服务端 token 文件中的值 / Unauthorized (401): missing or wrong token",
            413 => $"图片过大（413）：{detail ?? "超过服务端限制"} / Image too large (413)",
            415 => "不支持的图片格式（415）/ Unsupported image type (415)",
            400 => $"请求参数错误（400）：{detail} / Bad request (400)",
            408 => "服务端处理超时（408）/ Server timeout (408)",
            503 => "服务繁忙（503），请稍后重试 / Server busy (503), retry shortly",
            500 => $"服务端识别失败（500）：{detail} / OCR failed on the server (500)",
            404 => "接口不存在（404），请检查服务地址 / Not found (404): check the base URL",
            _ => $"服务返回 {(int)status}：{detail ?? Shorten(body)}",
        };
        return new MacOcrException(message, status);
    }

    private static string? ServerError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("error", out var e))
            {
                if (e.ValueKind == JsonValueKind.String)
                {
                    var text = e.GetString();
                    if (root.TryGetProperty("width", out var w) && root.TryGetProperty("height", out var h))
                        text += $" ({w}x{h})";
                    return text;
                }
                if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m)) return m.GetString();
            }
        }
        catch (JsonException) { }
        return null;
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..200] + "…";

    /// <summary>
    /// Blocks → rows → boxes, in reading order. Boxes are scaled from the server's
    /// pixel grid (response width/height) to the caller's (targetWidth/targetHeight).
    /// </summary>
    /// <summary>
    /// Flat reading-order segments, one box each. <paramref name="granularity"/>: "row" (default), "line" or "block".
    /// In row mode a row made of several Vision lines is split where the horizontal gap between
    /// neighbouring lines is at least <paramref name="gapFactor"/> × the median line height
    /// (e.g. three tabs on one bar). A single-line row is never split.
    /// </summary>
    public static List<MappedLine> Segments(OcrResponseDto dto, int targetWidth, int targetHeight,
        string? granularity = "row", bool splitGaps = true, double gapFactor = 1.0)
    {
        var sx = dto.Width > 0 && targetWidth > 0 ? (double)targetWidth / dto.Width : 1.0;
        var sy = dto.Height > 0 && targetHeight > 0 ? (double)targetHeight / dto.Height : 1.0;
        var mode = (granularity ?? "row").Trim().ToLowerInvariant();

        IReadOnlyList<(float, float)> Rect(OcrBox b) =>
        [
            ((float)(b.X * sx), (float)(b.Y * sy)),
            ((float)((b.X + b.Width) * sx), (float)(b.Y * sy)),
            ((float)((b.X + b.Width) * sx), (float)((b.Y + b.Height) * sy)),
            ((float)(b.X * sx), (float)((b.Y + b.Height) * sy)),
        ];
        IReadOnlyList<(float, float)> LinePoints(OcrLineDto l)
        {
            var q = l.Quad;
            if (q?.TopLeft != null && q.TopRight != null && q.BottomRight != null && q.BottomLeft != null)
                return
                [
                    ((float)(q.TopLeft.X * sx), (float)(q.TopLeft.Y * sy)),
                    ((float)(q.TopRight.X * sx), (float)(q.TopRight.Y * sy)),
                    ((float)(q.BottomRight.X * sx), (float)(q.BottomRight.Y * sy)),
                    ((float)(q.BottomLeft.X * sx), (float)(q.BottomLeft.Y * sy)),
                ];
            return l.BBox != null ? Rect(l.BBox) : [];
        }
        IReadOnlyList<(float, float)> Union(IEnumerable<OcrLineDto> ls)
        {
            var pts = ls.SelectMany(LinePoints).ToList();
            if (pts.Count == 0) return [];
            float x0 = pts.Min(p => p.Item1), y0 = pts.Min(p => p.Item2), x1 = pts.Max(p => p.Item1), y1 = pts.Max(p => p.Item2);
            return [(x0, y0), (x1, y0), (x1, y1), (x0, y1)];
        }
        List<OcrLineDto> Members(OcrRowDto r) =>
            [.. r.LineIndices.Where(i => i >= 0 && i < dto.Lines.Count).Select(i => dto.Lines[i])
                .Where(l => !string.IsNullOrWhiteSpace(l.Text))];

        var result = new List<MappedLine>();
        var rows = dto.Rows.Where(r => !string.IsNullOrWhiteSpace(r.Text)).ToList();
        var lines = dto.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).ToList();

        if (mode == "block" && dto.Blocks.Count > 0 && rows.Count > 0)
        {
            foreach (var b in Map(dto, targetWidth, targetHeight))
                result.Add(new MappedLine(string.Join("\n", b.Lines.Select(l => l.Text)), b.Points.Count > 0 ? b.Points : b.Lines[0].Points));
            return result;
        }
        if (mode == "line" || rows.Count == 0)
        {
            // Lines in reading order: by row order when rows exist, otherwise as sent.
            var ordered = rows.Count > 0
                ? rows.SelectMany(r => Members(r).OrderBy(l => l.BBox?.X ?? 0)).Distinct().ToList()
                : lines;
            if (ordered.Count == 0) ordered = lines;
            foreach (var l in ordered) result.Add(new MappedLine(l.Text, LinePoints(l)));
            return result;
        }

        var heights = lines.Where(l => l.BBox != null).Select(l => l.BBox!.Height).OrderBy(h => h).ToList();
        var medianH = heights.Count > 0 ? heights[heights.Count / 2] : 0;
        foreach (var row in rows)
        {
            var members = Members(row).OrderBy(l => l.BBox?.X ?? 0).ToList();
            if (members.Count == 0)
            {
                if (row.BBox != null) result.Add(new MappedLine(row.Text, Rect(row.BBox)));
                continue;
            }
            if (members.Count == 1)
            {
                result.Add(new MappedLine(row.Text, LinePoints(members[0])));
                continue;
            }
            var groups = new List<List<OcrLineDto>> { new() { members[0] } };
            for (var i = 1; i < members.Count; i++)
            {
                var prev = groups[^1][^1].BBox; var cur = members[i].BBox;
                var h = Math.Max(medianH, Math.Max(prev?.Height ?? 0, cur?.Height ?? 0) * 0.5);
                var gap = prev != null && cur != null ? cur.X - (prev.X + prev.Width) : 0;
                if (splitGaps && h > 0 && gap >= gapFactor * h) groups.Add([members[i]]);
                else groups[^1].Add(members[i]);
            }
            if (groups.Count == 1)
                result.Add(new MappedLine(row.Text, Union(members)));
            else
                foreach (var g in groups)
                    result.Add(new MappedLine(string.Join(" ", g.Select(l => l.Text.Trim())), Union(g)));
        }
        return result;
    }

    public static List<MappedBlock> Map(OcrResponseDto dto, int targetWidth, int targetHeight)
    {
        var sx = dto.Width > 0 && targetWidth > 0 ? (double)targetWidth / dto.Width : 1.0;
        var sy = dto.Height > 0 && targetHeight > 0 ? (double)targetHeight / dto.Height : 1.0;

        MappedLine Row(OcrRowDto row) => new(row.Text, Rect(row.BBox));
        IReadOnlyList<(float, float)> Rect(OcrBox? b) => b == null
            ? []
            : [
                ((float)(b.X * sx), (float)(b.Y * sy)),
                ((float)((b.X + b.Width) * sx), (float)(b.Y * sy)),
                ((float)((b.X + b.Width) * sx), (float)((b.Y + b.Height) * sy)),
                ((float)(b.X * sx), (float)((b.Y + b.Height) * sy)),
            ];

        var rows = dto.Rows.Where(r => !string.IsNullOrWhiteSpace(r.Text)).ToList();
        var blocks = new List<MappedBlock>();
        var used = new HashSet<int>();
        foreach (var block in dto.Blocks)
        {
            var lines = new List<MappedLine>();
            foreach (var index in block.RowIndices)
            {
                if (index < 0 || index >= dto.Rows.Count || !used.Add(index)) continue;
                if (string.IsNullOrWhiteSpace(dto.Rows[index].Text)) continue;
                lines.Add(Row(dto.Rows[index]));
            }
            if (lines.Count > 0) blocks.Add(new MappedBlock(Rect(block.BBox), lines));
        }
        // Rows not in any block (older servers send no blocks): one block per row.
        for (var i = 0; i < dto.Rows.Count; i++)
        {
            if (used.Contains(i) || string.IsNullOrWhiteSpace(dto.Rows[i].Text)) continue;
            blocks.Add(new MappedBlock(Rect(dto.Rows[i].BBox), [Row(dto.Rows[i])]));
        }
        // No rows at all: fall back to lines.
        if (rows.Count == 0)
            foreach (var line in dto.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)))
                blocks.Add(new MappedBlock(Rect(line.BBox), [new MappedLine(line.Text, Rect(line.BBox))]));
        return blocks;
    }
}
