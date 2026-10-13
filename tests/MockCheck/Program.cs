// Functional check of MacOcrApi.cs against a mock mac-ocr-http (synthetic data only).
// Run: dotnet run --project tests/MockCheck
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using STranslate.Plugin.Ocr.MacOcrHttp;

const string Token = "test-token-0123456789abcdef";
var failures = 0;
void Check(bool ok, string name, string info = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(info.Length > 0 ? " | " + info : "")}");
    if (!ok) failures++;
}

int FreePort() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }

var port = FreePort();
var listener = new HttpListener();
listener.Prefixes.Add($"http://127.0.0.1:{port}/");
listener.Start();
string? lastBody = null;
var server = Task.Run(async () =>
{
    while (listener.IsListening)
    {
        HttpListenerContext ctx;
        try { ctx = await listener.GetContextAsync(); } catch { break; }
        var req = ctx.Request; var res = ctx.Response;
        string body = new StreamReader(req.InputStream).ReadToEnd();
        int status = 200; string json;
        var authed = req.Headers["Authorization"] == $"Bearer {Token}";
        switch (req.Url!.AbsolutePath)
        {
            case "/health": json = "{\"ok\":true}"; break;
            case "/languages" when !authed: status = 401; json = "{\"ok\":false,\"error\":\"unauthorized\"}"; break;
            case "/languages": json = "{\"ok\":true,\"defaults\":[\"zh-Hans\",\"en-US\"],\"accurate\":[\"zh-Hans\",\"en-US\",\"ja-JP\"],\"fast\":[\"en-US\"]}"; break;
            case "/ocr" when !authed: status = 401; json = "{\"ok\":false,\"error\":\"unauthorized\"}"; break;
            case "/ocr":
                lastBody = body;
                using (var doc = JsonDocument.Parse(body))
                {
                    var img = Convert.FromBase64String(doc.RootElement.GetProperty("image").GetString()!);
                    if (img.Length > 1000) { status = 413; json = "{\"ok\":false,\"error\":\"image too large\",\"maxPixels\":100000000,\"width\":12000,\"height\":9000}"; break; }
                    if (img[0] == 0xFF) { await Task.Delay(4000); }
                }
                json = """
                {"text":"Hello world\nSecond line\nRight column","width":1000,"height":500,"elapsedMs":12,
                 "lines":[
                  {"text":"Hello world","confidence":1,"bbox":{"x":10,"y":20,"width":200,"height":30}},
                  {"text":"Second line","confidence":1,"bbox":{"x":10,"y":60,"width":180,"height":30}},
                  {"text":"Right column","confidence":0.5,"bbox":{"x":600,"y":20,"width":220,"height":30}}],
                 "rows":[
                  {"text":"Hello world","bbox":{"x":10,"y":20,"width":200,"height":30},"confidence":1,"lineIndices":[0]},
                  {"text":"Second line","bbox":{"x":10,"y":60,"width":180,"height":30},"confidence":1,"lineIndices":[1]},
                  {"text":"Right column","bbox":{"x":600,"y":20,"width":220,"height":30},"confidence":0.5,"lineIndices":[2]}],
                 "blocks":[
                  {"bbox":{"x":10,"y":20,"width":200,"height":70},"rowIndices":[0,1]},
                  {"bbox":{"x":600,"y":20,"width":220,"height":30},"rowIndices":[2]}]}
                """;
                break;
            default: status = 404; json = "{\"ok\":false,\"error\":\"not found\"}"; break;
        }
        var bytes = Encoding.UTF8.GetBytes(json);
        res.StatusCode = status; res.ContentType = "application/json";
        try { await res.OutputStream.WriteAsync(bytes); res.Close(); } catch { }
    }
});

var baseUrl = $"http://127.0.0.1:{port}";
MacOcrOptions Opt(string token = Token, string url = "", int timeout = 60) => new() { BaseUrl = url.Length > 0 ? url : baseUrl, Token = token, TimeoutSeconds = timeout };
byte[] small = [1, 2, 3, 4];

// URL normalisation
Check(MacOcrClient.NormalizeBaseUrl("127.0.0.1:8741/") == "http://127.0.0.1:8741", "normalize adds scheme, trims slash");
Check(MacOcrClient.NormalizeBaseUrl("http://h:8741/ocr") == "http://h:8741", "normalize strips /ocr");
Check(MacOcrClient.NormalizeBaseUrl("http://h:8741/v1") == "http://h:8741", "normalize strips /v1");
Check(MacOcrClient.NormalizeBaseUrl("") == "http://127.0.0.1:8741", "empty -> default");

// Validate
using (var c = new MacOcrClient(Opt())) { var s = await c.ValidateAsync(default); Check(s.Contains("3 languages"), "validate ok", s); }
using (var c = new MacOcrClient(Opt(token: "wrong")))
{
    try { await c.ValidateAsync(default); Check(false, "validate wrong token -> 401"); }
    catch (MacOcrException e) { Check(e.Status == HttpStatusCode.Unauthorized && e.Message.Contains("401"), "validate wrong token -> 401", e.Message); }
}
using (var c = new MacOcrClient(Opt(token: "Bearer " + Token))) { var s = await c.ValidateAsync(default); Check(s.StartsWith("OK"), "token with 'Bearer ' prefix accepted"); }

// Recognize + body
using (var c = new MacOcrClient(Opt() with { Languages = ["zh-Hans", "en-US"], Script = "hans", DropFragments = false }))
{
    var dto = await c.RecognizeAsync(small, null, default);
    Check(dto.Rows.Count == 3 && dto.Blocks.Count == 2 && dto.Width == 1000, "recognize parses rows/blocks", $"rows={dto.Rows.Count} blocks={dto.Blocks.Count}");
    using var doc = JsonDocument.Parse(lastBody!);
    var r = doc.RootElement;
    Check(r.GetProperty("image").GetString() == Convert.ToBase64String(small), "body image base64");
    Check(r.GetProperty("languages").GetArrayLength() == 2 && r.GetProperty("script").GetString() == "hans", "body languages + script");
    Check(r.GetProperty("fragments").GetString() == "keep" && r.GetProperty("level").GetString() == "accurate" && r.GetProperty("layout").GetString() == "rows", "body fragments/level/layout");

    var blocks = MacOcrClient.Map(dto, 2000, 1000);
    Check(blocks.Count == 2 && blocks[0].Lines.Count == 2 && blocks[1].Lines[0].Text == "Right column", "map blocks -> rows in reading order");
    var p = blocks[0].Lines[0].Points;
    Check(p.Count == 4 && p[0] == (20f, 40f) && p[2] == (420f, 100f), "boxes scaled to request pixel grid", string.Join(" ", p));
    var same = MacOcrClient.Map(dto, 0, 0);
    Check(same[0].Lines[0].Points[0] == (10f, 20f), "unknown target size keeps server grid");
}
using (var c = new MacOcrClient(Opt()))
{
    await c.RecognizeAsync(small, ["ja-JP"], default);
    using var doc = JsonDocument.Parse(lastBody!);
    Check(doc.RootElement.GetProperty("languages")[0].GetString() == "ja-JP" && !doc.RootElement.TryGetProperty("script", out _), "per-request languages; no script when unset");
}
// Map fallback when server sends no blocks
var noBlocks = new OcrResponseDto { Width = 10, Height = 10, Rows = [new OcrRowDto { Text = "a", BBox = new OcrBox { X = 1, Y = 1, Width = 2, Height = 2 } }] };
Check(MacOcrClient.Map(noBlocks, 10, 10).Count == 1, "rows without blocks still mapped");

// Errors
using (var c = new MacOcrClient(Opt(token: "")))
{
    try { await c.RecognizeAsync(small, null, default); Check(false, "no token -> 401"); }
    catch (MacOcrException e) { Check(e.Status == HttpStatusCode.Unauthorized, "no token -> 401", e.Message); }
}
using (var c = new MacOcrClient(Opt()))
{
    try { await c.RecognizeAsync(new byte[2000], null, default); Check(false, "big image -> 413"); }
    catch (MacOcrException e) { Check(e.Status == HttpStatusCode.RequestEntityTooLarge && e.Message.Contains("12000x9000"), "big image -> 413 with size", e.Message); }
}
var dead = FreePort();
using (var c = new MacOcrClient(Opt(url: $"http://127.0.0.1:{dead}")))
{
    try { await c.ValidateAsync(default); Check(false, "connection refused"); }
    catch (MacOcrException e) { Check(e.Message.Contains("Connection refused") && e.Message.Contains(dead.ToString()), "connection refused message", e.Message); }
}
using (var c = new MacOcrClient(Opt(timeout: 5) with { TimeoutSeconds = 5 }))
{
    // A body starting with 0xFF makes the mock wait 4s: within 5s, so it must succeed.
    var dto = await c.RecognizeAsync([0xFF, 1], null, default);
    Check(dto.Rows.Count == 3, "slow response within timeout");
}
using (var c = new MacOcrClient(Opt(url: baseUrl + "/nope/")))
{
    try { await c.ValidateAsync(default); Check(false, "wrong path -> 404"); }
    catch (MacOcrException e) { Check(e.Message.Contains("404"), "wrong base path -> clear /health error", e.Message); }
}
using (var cts = new CancellationTokenSource(200))
using (var c = new MacOcrClient(Opt()))
{
    try { await c.RecognizeAsync([0xFF, 2], null, cts.Token); Check(false, "cancellation"); }
    catch (OperationCanceledException) { Check(true, "caller cancellation propagates"); }
}

listener.Stop();
// --- Segments: one box per row, never per block (synthetic layout: title, subtitle, tab bar) ---
OcrLineDto L(string text, double x, double y, double w, double h) => new()
{
    Text = text, Confidence = 1, BBox = new OcrBox { X = x, Y = y, Width = w, Height = h },
    Quad = new OcrQuad
    {
        TopLeft = new OcrPoint { X = x, Y = y }, TopRight = new OcrPoint { X = x + w, Y = y },
        BottomRight = new OcrPoint { X = x + w, Y = y + h }, BottomLeft = new OcrPoint { X = x, Y = y + h },
    },
};
OcrBox B(double x, double y, double w, double h) => new() { X = x, Y = y, Width = w, Height = h };
var page = new OcrResponseDto
{
    Width = 1000, Height = 400,
    Lines =
    [
        L("Hello World", 300, 40, 300, 60),      // 0 title
        L("示例标题", 330, 120, 220, 40),          // 1 subtitle
        L("首页", 100, 250, 80, 40),               // 2 tab
        L("设置中心", 260, 250, 160, 40),          // 3 tab (gap 80 = 2x height)
        L("帮助", 500, 250, 80, 40),               // 4 tab
        L("Long sentence one", 100, 330, 400, 40), // 5 same row, small gap (10px)
        L("continues here", 510, 330, 300, 40),    // 6
    ],
    Rows =
    [
        new OcrRowDto { Text = "Hello World", BBox = B(300, 40, 300, 60), LineIndices = [0] },
        new OcrRowDto { Text = "示例标题", BBox = B(330, 120, 220, 40), LineIndices = [1] },
        new OcrRowDto { Text = "首页 设置中心 帮助", BBox = B(100, 250, 480, 40), LineIndices = [2, 3, 4] },
        new OcrRowDto { Text = "Long sentence one continues here", BBox = B(100, 330, 710, 40), LineIndices = [5, 6] },
    ],
    Blocks =
    [
        new OcrBlockDto { BBox = B(300, 40, 300, 120), RowIndices = [0, 1] },
        new OcrBlockDto { BBox = B(100, 250, 710, 120), RowIndices = [2, 3] },
    ],
};
var seg = MacOcrClient.Segments(page, 0, 0);
var texts = seg.Select(x => x.Text).ToList();
Check(texts.Take(2).SequenceEqual(["Hello World", "示例标题"]), "title and subtitle are separate boxes", string.Join(" | ", texts));
Check(texts.Skip(2).Take(3).SequenceEqual(["首页", "设置中心", "帮助"]), "tab row split at wide gaps", string.Join(" | ", texts));
Check(texts.Count == 6 && texts[5] == "Long sentence one continues here", "small gap keeps one row", string.Join(" | ", texts));
Check(seg.All(x => x.Points.Count == 4), "every segment has 4 points");
Check(seg[0].Points[0] == (300f, 40f) && seg[0].Points[2] == (600f, 100f), "single-line row uses its quad", string.Join(",", seg[0].Points));
Check(seg[2].Points[0] == (100f, 250f) && seg[2].Points[2] == (180f, 290f), "split tab box = its own line", string.Join(",", seg[2].Points));
Check(seg[5].Points[0] == (100f, 330f) && seg[5].Points[2] == (810f, 370f), "merged row box = union of its lines", string.Join(",", seg[5].Points));
var noSplit = MacOcrClient.Segments(page, 0, 0, "row", splitGaps: false).Select(x => x.Text).ToList();
Check(noSplit.Count == 4 && noSplit[2] == "首页 设置中心 帮助", "splitGaps off keeps the row", string.Join(" | ", noSplit));
var perLine = MacOcrClient.Segments(page, 0, 0, "line");
Check(perLine.Count == 7 && perLine[5].Text == "Long sentence one" && perLine[6].Text == "continues here", "line granularity = one per Vision line", string.Join(" | ", perLine.Select(x => x.Text)));
var perBlock = MacOcrClient.Segments(page, 0, 0, "block");
Check(perBlock.Count == 2 && perBlock[0].Text == "Hello World\n示例标题", "block granularity joins rows with newlines", string.Join(" | ", perBlock.Select(x => x.Text.Replace("\n", "⏎"))));
var scaled = MacOcrClient.Segments(page, 500, 200);
Check(scaled[0].Points[0] == (150f, 20f), "segments scaled to request grid", string.Join(",", scaled[0].Points));
var linesOnly = new OcrResponseDto { Width = 100, Height = 100, Lines = [L("a", 0, 0, 10, 10), L("b", 0, 20, 10, 10)] };
Check(MacOcrClient.Segments(linesOnly, 0, 0).Select(x => x.Text).SequenceEqual(["a", "b"]), "no rows -> per line fallback");
var rowNoLines = new OcrResponseDto { Width = 100, Height = 100, Rows = [new OcrRowDto { Text = "r", BBox = B(1, 2, 3, 4) }] };
Check(MacOcrClient.Segments(rowNoLines, 0, 0).Single().Points.Count == 4, "row without lineIndices uses row bbox");

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
