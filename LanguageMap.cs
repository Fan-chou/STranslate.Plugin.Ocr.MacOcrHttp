namespace STranslate.Plugin.Ocr.MacOcrHttp;

/// <summary>STranslate language → Apple Vision recognition languages.</summary>
public static class LanguageMap
{
    public static IReadOnlyList<string> ForVision(LangEnum lang) => lang switch
    {
        LangEnum.ChineseSimplified => ["zh-Hans", "en-US"],
        LangEnum.ChineseTraditional or LangEnum.Cantonese => ["zh-Hant", "en-US"],
        LangEnum.English => ["en-US"],
        LangEnum.Japanese => ["ja-JP", "en-US"],
        LangEnum.Korean => ["ko-KR", "en-US"],
        LangEnum.French => ["fr-FR"],
        LangEnum.German => ["de-DE"],
        LangEnum.Spanish => ["es-ES"],
        LangEnum.Italian => ["it-IT"],
        LangEnum.PortuguesePortugal or LangEnum.PortugueseBrazil => ["pt-BR"],
        LangEnum.Russian => ["ru-RU"],
        LangEnum.Ukrainian => ["uk-UA"],
        LangEnum.Vietnamese => ["vi-VT"],
        LangEnum.Thai => ["th-TH"],
        LangEnum.Arabic => ["ar-SA"],
        _ => [],
    };

    public static IEnumerable<LangEnum> Supported =>
    [
        LangEnum.Auto, LangEnum.ChineseSimplified, LangEnum.ChineseTraditional, LangEnum.Cantonese,
        LangEnum.English, LangEnum.Japanese, LangEnum.Korean, LangEnum.French, LangEnum.German,
        LangEnum.Spanish, LangEnum.Italian, LangEnum.PortuguesePortugal, LangEnum.PortugueseBrazil,
        LangEnum.Russian, LangEnum.Ukrainian, LangEnum.Vietnamese, LangEnum.Thai, LangEnum.Arabic,
    ];

    public static IReadOnlyList<string> Parse(string? csv) =>
        (csv ?? string.Empty).Split([',', ';', ' ', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
