using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using K3Pro.App.Services;
using K3Pro.Protocol;
using static K3Pro.Protocol.Lang;

namespace K3Pro.App.ViewModels;

public partial class KeyViewModel : ObservableObject
{
    public KeyViewModel(KeyLayout layout, double unit, double gap, byte page, KeymapEntry baseline)
    {
        Index = layout.Index;
        IsKnob = layout.IsKnob;
        Baseline = baseline;
        IsEditable = KeymapRules.IsEditable(page, layout.Index);
        _layout = layout;
        Applied = baseline;
        Left = layout.X * unit;
        Top = layout.Y * unit;
        Width = layout.W * unit - gap;
        Height = layout.H * unit - gap;
    }

    private readonly KeyLayout _layout;

    public int Index { get; }
    public bool IsKnob { get; }
    public KeymapEntry Baseline { get; }
    public bool IsEditable { get; }

    /// <summary>layout.json label (labelEn in English), otherwise the HID name of the default entry.</summary>
    public string DefaultLabel => (IsEnglish ? _layout.LabelEn ?? _layout.Label : _layout.Label) ??
                                  (Baseline.IsHidUsage ? HidUsages.Name(Baseline.Code) : Baseline.IsEmpty ? T("(trống)", "(empty)") : $"❓ {Baseline}");
    public double Left { get; }
    public double Top { get; }
    public double Width { get; }
    public double Height { get; }

    /// <summary>Entry written to the device (per keymap-state.json): HID usage override or the capture baseline.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Effective), nameof(EffectiveCode), nameof(Label), nameof(IsPending), nameof(IsRemapped), nameof(LabelFontSize), nameof(Tooltip))]
    public partial KeymapEntry Applied { get; set; }

    /// <summary>Change not applied yet (HID usage, or <see cref="Baseline"/> = revert to default).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Effective), nameof(EffectiveCode), nameof(Label), nameof(IsPending), nameof(IsRemapped), nameof(LabelFontSize), nameof(Tooltip))]
    public partial KeymapEntry? Pending { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public KeymapEntry Effective => Pending ?? Applied;

    public byte? EffectiveCode => Effective.IsHidUsage ? Effective.Code : null;

    public bool IsPending => Pending is { } p && p != Applied;

    public bool IsRemapped => Effective != Baseline;

    /// <summary>Key / combo / media / mouse / command → short label; special default entries (Fn, knob, rotation) → layout label.</summary>
    public string Label => Effective == Baseline && !Baseline.IsHidUsage ? DefaultLabel : KeymapActions.ShortLabel(Effective) ?? DefaultLabel;

    public string SubLabel => IsEditable ? $"#{Index} · {DefaultLabel}" : T($"#{Index} · khóa", $"#{Index} · locked");

    // Long single words ("Backspace") must fit a 1-unit key without breaking mid-word.
    public double LabelFontSize => Label.Length switch { <= 5 => 14, <= 8 => 12, _ when !Label.Contains(' ') => 9.5, _ => 10.5 };

    public string Tooltip => IsEditable
        ? T($"Index {Index} · mặc định {DefaultLabel} ({Baseline}) · hiện tại {Label}", $"Index {Index} · default {DefaultLabel} ({Baseline}) · current {Label}") +
          (IsPending ? T(" (chưa Apply)", " (not applied)") : "")
        : Baseline.IsEmpty
            ? T($"Index {Index} · trống trong capture — khóa cho tới khi có capture app hãng ghi vào entry này",
                $"Index {Index} · empty in the capture — locked until there is a capture of the vendor app writing this entry")
            : T($"Index {Index} · entry đặc biệt ❓ {Baseline} — khóa", $"Index {Index} · special entry ❓ {Baseline} — locked");

    /// <summary>Language switch: re-reads every label.</summary>
    public void RefreshTexts() => OnPropertyChanged(string.Empty);
}

/// <summary>
/// Remaps the base layer (page 0). No keymap read command yet → state = capture baseline + overrides in keymap-state.json.
/// Packet building / entry validation is done entirely by <see cref="KeymapRules"/> and <see cref="WritePlanner"/>.
/// </summary>
public partial class KeymapViewModel : ObservableObject
{
    private const byte Page = 0;

    private readonly KeymapStateStore _store;
    private readonly WriteCoordinator _writer;
    private readonly PacketLog _log;
    private Dictionary<int, KeymapEntry> _applied = [];
    private bool _syncingUsage;

    public KeymapViewModel(LayoutConfig? layout, string? layoutError, KeymapStateStore store, WriteCoordinator writer, PacketLog log)
    {
        _store = store;
        _writer = writer;
        _log = log;
        SearchText = "";
        LayoutError = layoutError;

        var baseline = CaptureBaseline.KeymapPage(Page);
        if (layout is not null)
        {
            foreach (var k in layout.Keys)
                Keys.Add(new KeyViewModel(k, layout.Unit, layout.Gap, Page, baseline[k.Index]));
            CanvasWidth = Keys.Select(k => k.Left + k.Width).DefaultIfEmpty(0).Max();
            CanvasHeight = Keys.Select(k => k.Top + k.Height).DefaultIfEmpty(0).Max();
        }

        try
        {
            _applied = new(store.LoadOverrides());
        }
        catch (InvalidDataException ex)
        {
            StateError = T($"Không đọc được trạng thái keymap ({ex.Message}). Đang hiển thị keymap mặc định; file cũ chưa bị ghi đè.",
                $"Could not read the keymap state ({ex.Message}). Showing the default keymap; the old file has not been overwritten.");
            _log.Error(StateError);
        }
        SyncAppliedToKeys();
        RefreshUsages();
    }

    public ObservableCollection<KeyViewModel> Keys { get; } = [];
    public ObservableCollection<HidUsage> FilteredUsages { get; } = [];

    /// <summary>Picker laid out like the vendor app: Keyboard / Mouse / Media / Macro / Commands (+ a separate Key combo tab).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<PickerTab> PickerTabs { get; private set; } = KeyPickerCatalog.Build();

    /// <summary>Allowed keys (for the combo's main-key selector).</summary>
    public IReadOnlyList<HidUsage> AllowedUsages { get; } = HidUsages.Keyboard.Where(u => KeymapRules.IsAllowedHidUsage(u.Code)).ToList();

    // Assignment groups beyond plain keys — only entries seen in the capture (KeymapActions)
    public IReadOnlyList<KeymapAction> ModifierActions => KeymapActions.Modifiers;
    public IReadOnlyList<KeymapAction> MediaActions => KeymapActions.Media;
    public IReadOnlyList<KeymapAction> MouseActions => KeymapActions.Mouse;
    public IReadOnlyList<KeymapAction> CommandActions => KeymapActions.Commands;

    /// <summary>Open tab — see the constants in <see cref="KeyPickerCatalog"/> (0 Keyboard … 5 Key combo).</summary>
    [ObservableProperty]
    public partial int CategoryIndex { get; set; }

    [ObservableProperty]
    public partial KeymapAction? SelectedAction { get; set; }

    [ObservableProperty]
    public partial bool ComboCtrl { get; set; }

    [ObservableProperty]
    public partial bool ComboShift { get; set; }

    [ObservableProperty]
    public partial bool ComboAlt { get; set; }

    [ObservableProperty]
    public partial bool ComboWin { get; set; }

    [ObservableProperty]
    public partial HidUsage? ComboKey { get; set; }

    public string ComboHint => ComboMask == 0
        ? T("Chọn ít nhất một modifier, rồi chọn phím.", "Pick at least one modifier, then a key.")
        : ComboKey is { } k ? $"{KeymapActions.ModifierText(ComboMask)} + {k.Name}" : $"{KeymapActions.ModifierText(ComboMask)} + … {T("(chọn phím)", "(pick a key)")}";

    private byte ComboMask => (byte)((ComboCtrl ? KeymapActions.ModCtrl : 0) | (ComboShift ? KeymapActions.ModShift : 0) |
                                     (ComboAlt ? KeymapActions.ModAlt : 0) | (ComboWin ? KeymapActions.ModWin : 0));
    public double CanvasWidth { get; }
    public double CanvasHeight { get; }
    public string? LayoutError { get; }
    public string StatePath => _store.Path;
    public string StatePathText => T($"Trạng thái: {_store.Path}", $"State: {_store.Path}");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTitle), nameof(SelectedDetail), nameof(CanEditSelected), nameof(SelectedLabel), nameof(SelectedIsPending))]
    public partial KeyViewModel? SelectedKey { get; set; }

    [ObservableProperty]
    public partial HidUsage? SelectedUsage { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial bool IsCapturing { get; set; }

    [ObservableProperty]
    public partial string? StateError { get; set; }

    public int PendingCount => Keys.Count(k => k.IsPending);
    public bool HasPending => PendingCount > 0;
    public string ApplyText => HasPending ? T($"Apply ({PendingCount} thay đổi)", $"Apply ({PendingCount} change{(PendingCount == 1 ? "" : "s")})") : "Apply";
    public bool CanEditSelected => SelectedKey is { IsEditable: true };

    public string SelectedTitle => SelectedKey is { } k
        ? T($"Phím #{k.Index} — {k.DefaultLabel}", $"Key #{k.Index} — {k.DefaultLabel}")
        : T("Chọn một phím trên layout", "Select a key on the layout");

    /// <summary>Large label of the current function (including changes not applied yet).</summary>
    public string SelectedLabel => SelectedKey?.Label ?? "—";

    public bool SelectedIsPending => SelectedKey?.IsPending == true;

    public string SelectedDetail => SelectedKey switch
    {
        null => T("Bấm vào phím để đổi. Thay đổi chỉ gửi khi bấm Apply.", "Click a key to change it. Changes are only sent when you press Apply."),
        { IsEditable: false, Baseline.IsEmpty: true } => T("Entry trống trong capture — khóa cho tới khi có capture.", "Empty entry in the capture — locked until there is a capture."),
        { IsEditable: false } k => T($"Entry đặc biệt ❓ ({k.Baseline}) chưa giải mã — khóa, không cho sửa.", $"Special entry ❓ ({k.Baseline}) not decoded yet — locked."),
        var k => T($"Hiện tại: {Describe(k, k.Applied)} · Mặc định: {Describe(k, k.Baseline)}", $"Current: {Describe(k, k.Applied)} · Default: {Describe(k, k.Baseline)}") +
                 (k.IsPending ? T($" · Sẽ đổi thành: {Describe(k, k.Pending!.Value)}", $" · Will change to: {Describe(k, k.Pending!.Value)}") : "") +
                 (KeymapRules.CapturedSpecialEntries.TryGetValue(k.Index, out var note)
                     ? T($"\nℹ {note}. Gán phím khác sẽ thay chức năng mặc định; \"Về mặc định\" ghi lại đúng giá trị app hãng.",
                         $"\nℹ {note}. Assigning something else replaces the default function; \"Default\" writes back the exact vendor value.")
                     : ""),
    };

    [RelayCommand]
    private void SelectKey(KeyViewModel key) => SelectedKey = key;

    /// <summary>A picker button was clicked. Locked buttons (not captured yet) do nothing.</summary>
    [RelayCommand]
    private void Pick(PickerItem item)
    {
        if (item.Entry is not { } entry || SelectedKey is not { IsEditable: true } k) return;
        SetPending(k, entry);
        SyncSelectedUsage();
    }

    [RelayCommand]
    private void ToggleCapture() => IsCapturing = !IsCapturing && CanEditSelected;

    [RelayCommand]
    private void RevertSelectedToDefault()
    {
        if (SelectedKey is { IsEditable: true } k) SetPending(k, k.Baseline);
    }

    [RelayCommand]
    private void DiscardPending()
    {
        foreach (var k in Keys) k.Pending = null;
        NotifyPending();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var target = MergedOverrides();
        WritePlan plan;
        try
        {
            plan = WritePlanner.Keymap(KeymapRules.Apply(Page, _applied), KeymapRules.Apply(Page, target), $"{_store.Path} {T("(trạng thái app)", "(app state)")}");
        }
        catch (UnsafeCommandException ex)
        {
            _log.Error(ex.Message);
            return;
        }

        switch (await _writer.ApplyAsync(plan))
        {
            case ApplyResult.Applied: Commit(target); break;
            case ApplyResult.Nothing: DiscardPending(); break;
        }
    }

    [RelayCommand]
    private async Task ResetToDefaultAsync()
    {
        var plan = WritePlanner.Keymap(KeymapRules.Apply(Page, _applied), CaptureBaseline.KeymapPage(Page),
            $"{_store.Path} {T("(trạng thái app)", "(app state)")}", force: true);
        if (await _writer.ApplyAsync(plan) == ApplyResult.Applied) Commit([]);
    }

    /// <summary>Called from the view during "Capture key". <paramref name="modifiers"/> ≠ 0 → combo. true = the key was consumed.</summary>
    public bool HandleCapturedKey(byte hidUsage, byte modifiers = 0)
    {
        if (!IsCapturing || SelectedKey is not { IsEditable: true } k) return false;
        IsCapturing = false;
        if (!KeymapRules.IsAllowedHidUsage(hidUsage))
        {
            _log.Warning(T($"{HidUsages.Name(hidUsage)} (0x{hidUsage:X2}) chưa được hỗ trợ — chưa có capture.",
                $"{HidUsages.Name(hidUsage)} (0x{hidUsage:X2}) is not supported yet — no capture."));
            return true;
        }
        SearchText = "";
        SetPending(k, modifiers == 0 ? KeymapEntry.HidUsage(hidUsage) : KeymapActions.Combo(modifiers, hidUsage));
        SyncSelectedUsage();
        return true;
    }

    /// <summary>During "Capture key", pressing and releasing a lone modifier → assign that modifier by itself (e.g. LCtrl).</summary>
    public bool HandleCapturedModifier(byte modifier)
    {
        if (!IsCapturing || SelectedKey is not { IsEditable: true } k) return false;
        IsCapturing = false;
        if (KeymapActions.Modifiers.FirstOrDefault(m => m.Entry.P1 == modifier) is not { } action)
        {
            _log.Warning(T($"Modifier 0x{modifier:X2} (phím bên phải) chưa có capture — mới hỗ trợ LCtrl / LShift / LAlt / LWin.",
                $"Modifier 0x{modifier:X2} (right-side key) has no capture yet — only LCtrl / LShift / LAlt / LWin are supported."));
            return true;
        }
        SetPending(k, action.Entry);
        SyncSelectedUsage();
        return true;
    }

    partial void OnSelectedKeyChanged(KeyViewModel? oldValue, KeyViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        IsCapturing = false;
        SearchText = "";
        SyncSelectedUsage();
    }

    partial void OnSelectedUsageChanged(HidUsage? value)
    {
        if (_syncingUsage || value is null || SelectedKey is not { IsEditable: true } k) return;
        SetPending(k, KeymapEntry.HidUsage(value.Code));
        SyncSelectedUsage();
    }

    partial void OnSelectedActionChanged(KeymapAction? value)
    {
        if (_syncingUsage || value is null || SelectedKey is not { IsEditable: true } k) return;
        SetPending(k, value.Entry);
        SyncSelectedUsage(); // sync the other tabs to the new entry (e.g. un-highlight the old key in the Keyboard tab)
    }

    partial void OnComboCtrlChanged(bool value) => ApplyCombo();
    partial void OnComboShiftChanged(bool value) => ApplyCombo();
    partial void OnComboAltChanged(bool value) => ApplyCombo();
    partial void OnComboWinChanged(bool value) => ApplyCombo();
    partial void OnComboKeyChanged(HidUsage? value) => ApplyCombo();

    private void ApplyCombo()
    {
        OnPropertyChanged(nameof(ComboHint));
        if (_syncingUsage || SelectedKey is not { IsEditable: true } k || ComboMask == 0 || ComboKey is not { } key) return;
        SetPending(k, KeymapActions.Combo(ComboMask, key.Code));
        SyncSelectedUsage();
    }

    partial void OnSearchTextChanged(string value) => RefreshUsages();

    private void SetPending(KeyViewModel key, KeymapEntry entry)
    {
        key.Pending = entry == key.Applied ? null : entry;
        NotifyPending();
    }

    private Dictionary<int, KeymapEntry> MergedOverrides()
    {
        var merged = new Dictionary<int, KeymapEntry>(_applied);
        foreach (var k in Keys.Where(k => k.IsPending))
        {
            var p = k.Pending!.Value;
            if (p == k.Baseline) merged.Remove(k.Index);
            else merged[k.Index] = p; // KeymapRules re-validates on Apply
        }
        return merged;
    }

    private void Commit(Dictionary<int, KeymapEntry> overrides)
    {
        _applied = overrides;
        try
        {
            _store.Save(_applied);
            StateError = null;
        }
        catch (IOException ex)
        {
            StateError = T($"Đã ghi thiết bị nhưng KHÔNG lưu được {_store.Path}: {ex.Message}",
                $"Written to the device but could NOT save {_store.Path}: {ex.Message}");
            _log.Error(StateError);
        }
        foreach (var k in Keys) k.Pending = null;
        SyncAppliedToKeys();
        NotifyPending();
    }

    private void SyncAppliedToKeys()
    {
        foreach (var k in Keys)
            k.Applied = _applied.TryGetValue(k.Index, out var e) ? e : k.Baseline;
    }

    /// <summary>Shows the matching group + selection for the selected key's current entry (creates no change).</summary>
    private void SyncSelectedUsage()
    {
        _syncingUsage = true;
        var e = SelectedKey?.Effective;
        var kind = e is { } entry ? KeymapActions.Classify(entry) : null;
        SelectedUsage = kind == KeymapActionKind.Key ? HidUsages.Keyboard.FirstOrDefault(u => u.Code == e!.Value.Code) : null;
        SelectedAction = kind is KeymapActionKind.Modifier or KeymapActionKind.Media or KeymapActionKind.Mouse or KeymapActionKind.Command
            ? KeymapActions.FixedActions.First(a => a.Entry == e)
            : null;
        bool combo = kind == KeymapActionKind.Combo;
        ComboCtrl = combo && (e!.Value.P1 & KeymapActions.ModCtrl) != 0;
        ComboShift = combo && (e!.Value.P1 & KeymapActions.ModShift) != 0;
        ComboAlt = combo && (e!.Value.P1 & KeymapActions.ModAlt) != 0;
        ComboWin = combo && (e!.Value.P1 & KeymapActions.ModWin) != 0;
        ComboKey = combo ? HidUsages.Keyboard.FirstOrDefault(u => u.Code == e!.Value.Code) : null;
        if (SelectedKey is not null)
            CategoryIndex = kind switch
            {
                KeymapActionKind.Combo => KeyPickerCatalog.ComboTab,
                KeymapActionKind.Media => KeyPickerCatalog.MediaTab,
                KeymapActionKind.Mouse => KeyPickerCatalog.MouseTab,
                KeymapActionKind.Command => KeyPickerCatalog.CommandTab,
                _ => KeyPickerCatalog.KeyboardTab, // plain key, modifier, special default entry
            };
        foreach (var item in PickerTabs.SelectMany(t => t.AllItems))
            item.IsSelected = e is { } cur && item.Entry == cur;
        _syncingUsage = false;
        OnPropertyChanged(nameof(ComboHint));
    }

    private void RefreshUsages()
    {
        var q = SearchText.Trim();
        var hex = q.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? q[2..] : q;
        FilteredUsages.Clear();
        var comboKey = ComboKey;
        foreach (var u in HidUsages.Keyboard.Where(u => KeymapRules.IsAllowedHidUsage(u.Code)))
        {
            if (q.Length == 0 || u.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                u.Code.ToString("X2").Equals(hex, StringComparison.OrdinalIgnoreCase))
                FilteredUsages.Add(u);
        }
        foreach (var section in PickerTabs[KeyPickerCatalog.KeyboardTab].Sections)
        {
            foreach (var item in section.Items)
                item.IsVisible = q.Length == 0 || item.Label.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                 (item.Entry is { IsHidUsage: true } e && (HidUsages.Name(e.Code).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                                                           e.Code.ToString("X2").Equals(hex, StringComparison.OrdinalIgnoreCase)));
            section.IsVisible = section.Items.Any(i => i.IsVisible);
        }
        // Re-filtering the list must not change the selection / switch tabs
        _syncingUsage = true;
        ComboKey = comboKey;
        if (SelectedKey?.Effective is { IsHidUsage: true } cur) SelectedUsage = HidUsages.Keyboard.FirstOrDefault(u => u.Code == cur.Code);
        _syncingUsage = false;
    }

    /// <summary>Language switch: rebuilds the picker (new labels), keeps the selection / filter / open tab.</summary>
    public void RefreshLanguage()
    {
        var tab = CategoryIndex;
        PickerTabs = KeyPickerCatalog.Build();
        foreach (var k in Keys) k.RefreshTexts();
        RefreshUsages();
        SyncSelectedUsage();
        CategoryIndex = tab;
        OnPropertyChanged(string.Empty);
    }

    private void NotifyPending()
    {
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(ApplyText));
        OnPropertyChanged(nameof(SelectedDetail));
        OnPropertyChanged(nameof(SelectedLabel));
        OnPropertyChanged(nameof(SelectedIsPending));
    }

    private static string Describe(KeyViewModel k, KeymapEntry e) =>
        e == k.Baseline && !e.IsHidUsage ? $"{k.DefaultLabel} ({e})" : KeymapActions.Describe(e);
}
