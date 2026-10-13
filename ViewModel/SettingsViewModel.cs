using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace STranslate.Plugin.Ocr.MacOcrHttp.ViewModel;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IPluginContext _context;
    private readonly Settings _settings;

    public IReadOnlyList<string> Levels { get; } = ["accurate", "fast"];
    public IReadOnlyList<string> Granularities { get; } = ["row", "line", "block"];
    public IReadOnlyList<string> Scripts { get; } = ["", "hans", "auto"];
    public IReadOnlyList<string> Layouts { get; } = ["rows", "document"];

    public SettingsViewModel(IPluginContext context, Settings settings)
    {
        _context = context;
        _settings = settings;

        Url = _settings.Url;
        Token = _settings.Token;
        Languages = _settings.Languages;
        Level = _settings.Level;
        Script = _settings.Script;
        Layout = _settings.Layout;
        DropFragments = _settings.DropFragments;
        UseBoxPoints = _settings.UseBoxPoints;
        Granularity = _settings.Granularity;
        SplitGaps = _settings.SplitGaps;
        BypassProxy = _settings.BypassProxy;
        TimeoutSeconds = _settings.TimeoutSeconds;

        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Url): _settings.Url = Url.Trim(); break;
            case nameof(Token): _settings.Token = Token.Trim(); break;
            case nameof(Languages): _settings.Languages = Languages.Trim(); break;
            case nameof(Level): _settings.Level = Level ?? "accurate"; break;
            case nameof(Script): _settings.Script = Script ?? string.Empty; break;
            case nameof(Layout): _settings.Layout = Layout ?? "rows"; break;
            case nameof(DropFragments): _settings.DropFragments = DropFragments; break;
            case nameof(UseBoxPoints): _settings.UseBoxPoints = UseBoxPoints; break;
            case nameof(Granularity): _settings.Granularity = Granularity ?? "row"; break;
            case nameof(SplitGaps): _settings.SplitGaps = SplitGaps; break;
            case nameof(BypassProxy): _settings.BypassProxy = BypassProxy; break;
            case nameof(TimeoutSeconds): _settings.TimeoutSeconds = Math.Clamp(TimeoutSeconds, 5, 600); break;
            default: return;
        }
        _context.SaveSettingStorage<Settings>();
    }

    [ObservableProperty] public partial string ValidateResult { get; set; } = string.Empty;
    [ObservableProperty] public partial string Url { get; set; }
    [ObservableProperty] public partial string Token { get; set; }
    [ObservableProperty] public partial string Languages { get; set; }
    [ObservableProperty] public partial string? Level { get; set; }
    [ObservableProperty] public partial string? Script { get; set; }
    [ObservableProperty] public partial string? Layout { get; set; }
    [ObservableProperty] public partial bool DropFragments { get; set; }
    [ObservableProperty] public partial bool UseBoxPoints { get; set; }
    [ObservableProperty] public partial string? Granularity { get; set; }
    [ObservableProperty] public partial bool SplitGaps { get; set; }
    [ObservableProperty] public partial bool BypassProxy { get; set; }
    [ObservableProperty] public partial int TimeoutSeconds { get; set; }

    [RelayCommand]
    public async Task ValidateAsync()
    {
        ValidateResult = "…";
        try
        {
            using var client = new MacOcrClient(Main.OptionsFrom(_settings));
            var summary = await client.ValidateAsync(CancellationToken.None);
            ValidateResult = $"{_context.GetTranslation("ValidationSuccess")} · {summary}";
        }
        catch (Exception ex)
        {
            ValidateResult = $"{_context.GetTranslation("ValidationFailure")}: {ex.Message}";
            _context.Logger.LogError(ex, "mac-ocr-http validation failed");
        }
    }

    public void Dispose() => PropertyChanged -= OnPropertyChanged;
}
