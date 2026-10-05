using CommunityToolkit.Mvvm.ComponentModel;
using K3Pro.Protocol;
using static K3Pro.Protocol.Lang;

namespace K3Pro.App.Localization;

/// <summary>A language in the 🌐 picker: code ("en", "vi", …) + native name from its JSON file ("language.name").</summary>
public sealed record LanguageOption(string Language, string Name)
{
    /// <summary>Every available translation (built-in + lang/ folders), English first.</summary>
    public static IReadOnlyList<LanguageOption> All => Lang.Available.Select(l => new LanguageOption(l.Code, l.Name)).ToList();

    public static LanguageOption Of(string language) =>
        All.FirstOrDefault(o => o.Language == language) ?? All.First(o => o.Language == UiLanguage.En);
}

/// <summary>
/// Static XAML strings: <c>{Binding X, Source={x:Static l:Tr.I}}</c>. Changing <see cref="Lang.Current"/> → all bindings refresh at once.
/// Data-derived strings live in the ViewModels (which also use <see cref="Lang.T"/>).
/// </summary>
public sealed class Tr : ObservableObject
{
    public static Tr I { get; } = new();

    private Tr() => Lang.Changed += () => OnPropertyChanged(string.Empty);

    // Top bar
    public string LanguageTip => T("ui.language_tip");

    // Keymap
    public string KeymapWarn => T("ui.keymap_warn");
    public string BaseLayer => T("ui.base_layer");
    public string LegendRemapped => T("ui.legend_remapped");
    public string LegendPending => T("ui.legend_pending");
    public string LegendLocked => T("ui.legend_locked");
    public string CaptureKey => T("ui.capture_key");
    public string CaptureKeyTip => T("ui.capture_key_tip");
    public string KeyDefault => T("ui.key_default");
    public string CaptureHint => T("ui.capture_hint");
    public string SearchPlaceholder => T("ui.search_placeholder");
    public string ComboTab => T("ui.combo_tab");
    public string ComboStep1 => T("ui.combo_step1");
    public string ComboStep2 => T("ui.combo_step2");
    public string ComboKeyPlaceholder => T("ui.combo_key_placeholder");
    public string ComboTip => T("ui.combo_tip");
    public string ResetToDefault => T("ui.reset_to_default");
    public string DiscardChanges => T("ui.discard_changes");

    // Lighting
    public string EffectsTitle => T("ui.effects_title");
    public string EffectsNote => T("ui.effects_note");
    public string ApplyEffect => T("ui.apply_effect");
    public string BrightnessTip => T("ui.brightness_tip");
    public string StaticColorTitle => T("ui.static_color_title");
    public string RgbOrderNote => T("ui.rgb_order_note");
    public string DefaultPalette => T("ui.default_palette");
    public string Read0x84 => T("ui.read0x84");
    public string LightingWarn => T("ui.lighting_warn");

    // Device
    public string Connection => T("ui.connection");
    public string ConnectionNote => T("ui.connection_note");
    public string Rescan => T("ui.rescan");
    public string SleepTitle => T("ui.sleep_title");
    public string SleepNote => T("ui.sleep_note");
    public string AppData => T("ui.app_data");
    public string AppDataNote => T("ui.app_data_note");
    public string Files => T("ui.files");
    public string AppVersionTitle => T("ui.app_version_title");
    public string CheckForUpdates => T("ui.check_for_updates");
    public string AutoCheckUpdates => T("ui.auto_check_updates");
    public string UpdatesNote => T("ui.updates_note");
    public string OpenDataFolder => T("ui.open_data_folder");
    public string SettingsBlockTitle => T("ui.settings_block_title");

    // Log
    public string Clear => T("ui.clear");
}
