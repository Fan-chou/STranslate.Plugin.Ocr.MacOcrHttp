using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using STranslate.Plugin.Ocr.MacOcrHttp.View;
using STranslate.Plugin.Ocr.MacOcrHttp.ViewModel;
using System.Windows.Controls;

namespace STranslate.Plugin.Ocr.MacOcrHttp;

public partial class Main : ObservableObject, IOcrPlugin
{
    private Control? _settingUi;
    private SettingsViewModel? _viewModel;
    private Settings Settings { get; set; } = null!;
    private IPluginContext Context { get; set; } = null!;

    public IEnumerable<LangEnum> SupportedLanguages => LanguageMap.Supported;

    public Control GetSettingUI()
    {
        _viewModel ??= new SettingsViewModel(Context, Settings);
        _settingUi ??= new SettingsView { DataContext = _viewModel };
        return _settingUi;
    }

    public void Init(IPluginContext context)
    {
        Context = context;
        Settings = context.LoadSettingStorage<Settings>();
    }

    public void Dispose() => _viewModel?.Dispose();

    public bool SupportBoxPoints() => Settings.UseBoxPoints;

    internal static MacOcrOptions OptionsFrom(Settings s) => new()
    {
        BaseUrl = s.Url,
        Token = s.Token,
        Languages = LanguageMap.Parse(s.Languages),
        Level = s.Level,
        Script = s.Script,
        Layout = s.Layout,
        DropFragments = s.DropFragments,
        TimeoutSeconds = s.TimeoutSeconds <= 0 ? 60 : s.TimeoutSeconds,
        BypassProxy = s.BypassProxy,
    };

    public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        var options = OptionsFrom(Settings);
        // Explicit languages in settings win; otherwise follow STranslate's OCR language.
        var languages = options.Languages.Count > 0 ? options.Languages : LanguageMap.ForVision(request.Language);
        try
        {
            options = options with { Script = ScriptDefaults.ScriptFor(options.Script, languages) };
            using var client = new MacOcrClient(options);
            var dto = await client.RecognizeAsync(request.ImageData, languages, cancellationToken);
            var segments = MacOcrClient.Segments(dto, request.PixelWidth, request.PixelHeight, Settings.Granularity, Settings.SplitGaps);
            if (segments.Count == 0) return new OcrResult().Fail("未检测到文字 / No text found");

            var result = new OcrResult();
            foreach (var segment in segments)
            {
                // One region → one paragraph → one line per segment, so STranslate draws a box per row
                // and never concatenates two rows without a separator.
                if (Settings.UseBoxPoints && segment.Points.Count == 4)
                {
                    var points = Points(segment.Points);
                    var paragraph = new OcrParagraph { BoxPoints = points };
                    paragraph.Lines.Add(new OcrContent { Text = segment.Text, BoxPoints = points });
                    var region = new OcrRegion { BoxPoints = points };
                    region.Paragraphs.Add(paragraph);
                    result.Regions.Add(region);
                }
                result.OcrContents.Add(new OcrContent { Text = segment.Text, BoxPoints = Settings.UseBoxPoints ? Points(segment.Points) : [] });
            }
            return result;
        }
        catch (MacOcrException ex)
        {
            Context.Logger.LogWarning("mac-ocr-http: {Message}", ex.Message);
            return new OcrResult().Fail(ex.Message);
        }
    }

    private static List<BoxPoint> Points(IReadOnlyList<(float X, float Y)> points) =>
        [.. points.Select(p => new BoxPoint(p.X, p.Y))];
}
