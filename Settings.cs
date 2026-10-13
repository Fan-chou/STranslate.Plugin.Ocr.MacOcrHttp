namespace STranslate.Plugin.Ocr.MacOcrHttp;

public class Settings
{
    /// <summary>mac-ocr-http base URL, e.g. http://127.0.0.1:8741 or a LAN address of the Mac.</summary>
    public string Url { get; set; } = "http://127.0.0.1:8741";

    /// <summary>Bearer token (the server's token file). Stored like other STranslate plugin API keys.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Comma-separated Vision languages, e.g. "zh-Hans,en-US". Empty: follow STranslate's OCR language, else the server default.</summary>
    public string Languages { get; set; } = string.Empty;

    /// <summary>accurate or fast.</summary>
    public string Level { get; set; } = "accurate";

    /// <summary>"" (server default), hans, or auto.</summary>
    public string Script { get; set; } = string.Empty;

    /// <summary>rows or document.</summary>
    public string Layout { get; set; } = "rows";

    /// <summary>true: drop icon / edge-cut / watermark fragments (server default).</summary>
    public bool DropFragments { get; set; } = true;

    /// <summary>Return line boxes so STranslate can highlight and translate regions.</summary>
    public bool UseBoxPoints { get; set; } = true;

    /// <summary>Ignore the system proxy for this server.</summary>
    public bool BypassProxy { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 60;
}
