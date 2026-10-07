using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using K3Pro.App.Services;
using K3Pro.Protocol;
using static K3Pro.Protocol.Lang;

namespace K3Pro.App.ViewModels;

public sealed record ColorPreset(string Name, Color Color)
{
    public IBrush Brush { get; } = new SolidColorBrush(Color);
}

/// <summary>An effect button. Not writable (Self-define) → dimmed, not selectable.</summary>
public partial class EffectItem(LightingEffect effect) : ObservableObject
{
    public LightingEffect Effect { get; } = effect;
    public string Label => Effect.Name;
    public bool IsEnabled => Effect.IsCaptured;

    public string Tooltip => Effect switch
    {
        { IsCaptured: true, Mode: { } m } => $"0x0A = 0x{m:X2} · capture {Effect.Source}" +
                                            (IsCurrent ? T("lighting.active_device") : ""),
        { Mode: not null } => T("lighting.self_define_needs_command_02"),
        _ => T("lighting.no_capture_yet"),
    };

    /// <summary>Selected for Apply.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The mode read from the device (0x84) matches this effect.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tooltip))]
    public partial bool IsCurrent { get; set; }

    public void RefreshTexts() => OnPropertyChanged(nameof(Tooltip));
}

/// <summary>
/// Effect: Apply = read 0x84 → 0x04 changing only 0x0A (+ that mode's brightness / speed if the user moved the slider) —
/// <see cref="WritePlanner.LightingEffect"/>.
/// Static color: Apply = read 0x84 → (0x04 mode 01 if needed) → 0x0A slot 7 (<see cref="WritePlanner.StaticColor"/>).
/// </summary>
public partial class LightingViewModel : ObservableObject
{
    private readonly IDeviceService _device;
    private readonly WriteCoordinator _writer;
    private readonly PacketLog _log;

    public LightingViewModel(IDeviceService device, WriteCoordinator writer, PacketLog log)
    {
        _device = device;
        _writer = writer;
        _log = log;
        var initial = CaptureBaseline.ColorTable().StaticColor;
        HexInput = initial.ToString();
        SelectedColor = Color.FromRgb(initial.R, initial.G, initial.B);
        Presets = BuildPresets();
    }

    /// <summary>The vendor app's Light effect list (<see cref="LightingModes.Effects"/>).</summary>
    public IReadOnlyList<EffectItem> Effects { get; } = LightingModes.Effects.Select(e => new EffectItem(e)).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyEffect), nameof(ShowBrightness), nameof(ShowSpeed), nameof(BrightnessText), nameof(SpeedText))]
    public partial EffectItem? SelectedEffect { get; private set; }

    public bool CanApplyEffect => SelectedEffect is not null && !IsBusy;

    // Latest block read: source of the selected effect's current brightness / speed.
    private Settings? _settings;
    private bool _loadingParams, _brightnessTouched, _speedTouched;

    public int LevelMax => LightingModes.MaxLevel;

    public bool ShowBrightness => SelectedEffect?.Effect.HasBrightness == true;

    public bool ShowSpeed => SelectedEffect?.Effect.HasSpeed == true;

    /// <summary>Brightness 0..4 (like the vendor app). Only written if the user moves the slider.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BrightnessText))]
    public partial int EffectBrightness { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpeedText))]
    public partial int EffectSpeed { get; set; }

    public string BrightnessText => T("lighting.brightness") + $" {EffectBrightness}/{LevelMax}" + DeviceLevelNote(s => s.Brightness, EffectBrightness);

    public string SpeedText => T("lighting.speed") + $" {EffectSpeed}/{LevelMax}" + DeviceLevelNote(s => s.Speed, EffectSpeed);

    /// <summary>Device value outside 0..4 (e.g. 07 set by the knob ❓) → show a note; not overwritten unless the slider is moved.</summary>
    private string DeviceLevelNote(Func<Settings, Func<byte, byte>> read, int shown) =>
        _settings is { } s && SelectedEffect?.Effect.Mode is { } m && read(s)(m) is var raw && raw != shown
            ? T("lighting.device", raw)
            : "";

    partial void OnEffectBrightnessChanged(int value)
    {
        if (!_loadingParams) _brightnessTouched = true;
    }

    partial void OnEffectSpeedChanged(int value)
    {
        if (!_loadingParams) _speedTouched = true;
    }

    /// <summary>Loads the selected effect's parameters from the latest block read (creates no change).</summary>
    private void LoadEffectParams()
    {
        _loadingParams = true;
        if (_settings is { } s && SelectedEffect?.Effect.Mode is { } m)
        {
            EffectBrightness = Math.Min((int)s.Brightness(m), LevelMax);
            EffectSpeed = Math.Min((int)s.Speed(m), LevelMax);
        }
        _brightnessTouched = _speedTouched = false;
        _loadingParams = false;
        OnPropertyChanged(nameof(BrightnessText));
        OnPropertyChanged(nameof(SpeedText));
    }

    public int CapturedEffectCount => Effects.Count(e => e.IsEnabled);

    public string EffectsSummary => T("lighting.effects_available", CapturedEffectCount, Effects.Count);

    /// <summary>Default palette from the capture (slots 8–14). Rebuilt on language switch (color names).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ColorPreset> Presets { get; private set; }

    private static IReadOnlyList<ColorPreset> BuildPresets() =>
    [
        new(T("lighting.red"), Color.FromRgb(0xFF, 0x00, 0x00)),
        new(T("lighting.blue"), Color.FromRgb(0x00, 0x00, 0xFF)),
        new(T("lighting.green"), Color.FromRgb(0x00, 0xFF, 0x00)),
        new(T("lighting.yellow"), Color.FromRgb(0xFF, 0xFF, 0x00)),
        new(T("lighting.magenta"), Color.FromRgb(0xFF, 0x00, 0xFF)),
        new("Cyan", Color.FromRgb(0x00, 0xFF, 0xFF)),
        new(T("lighting.white"), Color.FromRgb(0xFF, 0xFF, 0xFF)),
    ];

    // Latest mode read (null = not read yet); display text follows the current language.
    private byte? _mode;
    private bool _modeReadFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HexText), nameof(SelectedBrush))]
    public partial Color SelectedColor { get; set; }

    /// <summary>RRGGBB input box; updates <see cref="SelectedColor"/> when valid.</summary>
    [ObservableProperty]
    public partial string HexInput { get; set; }

    public string ModeText => _modeReadFailed ? T("lighting.read_failed")
        : _mode is { } m ? $"0x{m:X2} ({LightingModes.Describe(m)})" : T("device.not_read");

    public string ModeLine => T("lighting.current_mode_0x84_offset_0x0a", ModeText);

    /// <summary>Language switch: rebuilds the color names + mode text.</summary>
    public void RefreshLanguage()
    {
        Presets = BuildPresets();
        foreach (var e in Effects) e.RefreshTexts();
        OnPropertyChanged(string.Empty);
    }

    private void SetMode(byte? mode, bool failed)
    {
        _mode = mode;
        _modeReadFailed = failed;
        foreach (var e in Effects) e.IsCurrent = mode is { } m && e.Effect.Mode == m;
        OnPropertyChanged(nameof(ModeText));
        OnPropertyChanged(nameof(ModeLine));
    }

    /// <summary>Selects an effect (not written yet) and loads its current brightness / speed. Non-writable effects are ignored.</summary>
    [RelayCommand]
    private async Task PickEffectAsync(EffectItem item)
    {
        if (!item.IsEnabled) return;
        foreach (var e in Effects) e.IsSelected = ReferenceEquals(e, item);
        SelectedEffect = item;
        if (_settings is null) await ReadSettingsAsync();
        LoadEffectParams();
    }

    [RelayCommand]
    private async Task ApplyEffectAsync()
    {
        if (SelectedEffect?.Effect.Mode is not { } mode) return;
        IsBusy = true;
        try
        {
            if (await ReadSettingsAsync() is not { } current) return;
            WritePlan plan;
            try
            {
                plan = WritePlanner.LightingEffect(current, mode,
                    _brightnessTouched && ShowBrightness ? (byte)EffectBrightness : null,
                    _speedTouched && ShowSpeed ? (byte)EffectSpeed : null);
            }
            catch (UnsafeCommandException ex)
            {
                _log.Error(ex.Message);
                return;
            }
            if (await _writer.ApplyAsync(plan) == ApplyResult.Applied)
            {
                await ReadSettingsAsync();
                LoadEffectParams();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyEffect))]
    public partial bool IsBusy { get; set; }

    public string HexText => $"{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";

    public IBrush SelectedBrush => new SolidColorBrush(Color.FromRgb(SelectedColor.R, SelectedColor.G, SelectedColor.B));

    partial void OnSelectedColorChanged(Color value)
    {
        if (!string.Equals(HexInput, HexText, StringComparison.OrdinalIgnoreCase)) HexInput = HexText;
    }

    partial void OnHexInputChanged(string value)
    {
        if (value.Length != 6) return;
        try
        {
            var rgb = Rgb.Parse(value);
            if (rgb.ToString() != HexText) SelectedColor = Color.FromRgb(rgb.R, rgb.G, rgb.B);
        }
        catch (FormatException)
        {
            // still typing — keep the old color
        }
    }

    [RelayCommand]
    private void PickPreset(ColorPreset preset) => SelectedColor = preset.Color;

    [RelayCommand]
    private async Task RefreshModeAsync() => await ReadSettingsAsync();

    [RelayCommand]
    private async Task ApplyAsync()
    {
        IsBusy = true;
        try
        {
            if (await ReadSettingsAsync() is not { } current) return;
            var plan = WritePlanner.StaticColor(current, new Rgb(SelectedColor.R, SelectedColor.G, SelectedColor.B));
            if (await _writer.ApplyAsync(plan) == ApplyResult.Applied) await ReadSettingsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Shows a settings block read from the device (automatic read on connect): the active effect gets the blue outline and —
    /// if the user hasn't picked one — is selected, so its brightness / speed appear on the sliders. Nothing is written.
    /// </summary>
    public void ShowSettings(Settings s)
    {
        _settings = s;
        SetMode(s.LightingMode, false);
        if (SelectedEffect is null && Effects.FirstOrDefault(e => e.IsEnabled && e.Effect.Mode == s.LightingMode) is { } active)
        {
            foreach (var e in Effects) e.IsSelected = ReferenceEquals(e, active);
            SelectedEffect = active;
        }
        LoadEffectParams();
    }

    private async Task<Settings?> ReadSettingsAsync()
    {
        try
        {
            var s = await _device.ReadSettingsAsync();
            _settings = s;
            SetMode(s.LightingMode, false);
            return s;
        }
        catch (Exception ex)
        {
            SetMode(null, true);
            _log.Error(T("device.reading_0x84_failed", ex.Message));
            return null;
        }
    }
}
