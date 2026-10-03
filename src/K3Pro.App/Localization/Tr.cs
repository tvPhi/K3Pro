using CommunityToolkit.Mvvm.ComponentModel;
using K3Pro.Protocol;
using static K3Pro.Protocol.Lang;

namespace K3Pro.App.Localization;

public sealed record LanguageOption(UiLanguage Language, string Name)
{
    public static IReadOnlyList<LanguageOption> All { get; } = [new(UiLanguage.Vi, "Tiếng Việt"), new(UiLanguage.En, "English")];

    public static LanguageOption Of(UiLanguage language) => All.First(o => o.Language == language);
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
    public string LanguageTip => T("Ngôn ngữ", "Language");

    // Keymap
    public string KeymapWarn => T(
        "⚠ Chưa có lệnh đọc keymap từ thiết bị. App giữ trạng thái trong keymap-state.json (khởi tạo từ keymap mặc định trích trong capture). " +
        "Nếu đổi phím bằng app hãng, trạng thái hiển thị ở đây sẽ LỆCH với thiết bị — bấm \"Reset về mặc định\" để đồng bộ lại.",
        "⚠ There is no command to read the keymap from the device. The app keeps its state in keymap-state.json (initialised from the default keymap extracted from the capture). " +
        "If you remap keys with the vendor app, what is shown here will DIFFER from the device — press \"Reset to default\" to resync.");
    public string BaseLayer => T("Layer gốc (page 0)", "Base layer (page 0)");
    public string LegendRemapped => T("■ đã remap", "■ remapped");
    public string LegendPending => T("■ chưa Apply", "■ not applied");
    public string LegendLocked => T("mờ = khóa ❓", "dimmed = locked ❓");
    public string CaptureKey => T("⌨ Bắt phím", "⌨ Capture key");
    public string CaptureKeyTip => T(
        "Bấm rồi nhấn phím / tổ hợp trên bàn phím thật (giữ Ctrl rồi nhấn C → Ctrl+C; nhấn-nhả riêng Ctrl → LCtrl)",
        "Click, then press a key or combo on your real keyboard (hold Ctrl and press C → Ctrl+C; tap Ctrl alone → LCtrl)");
    public string KeyDefault => T("Về mặc định", "Default");
    public string CaptureHint => T(
        "Đang chờ phím… nhấn một phím, hoặc giữ Ctrl / Shift / Alt rồi nhấn phím (→ tổ hợp). Nhấn-nhả riêng Ctrl / Shift / Alt → gán modifier. Tổ hợp có Win chọn ở tab Tổ hợp phím.",
        "Waiting for a key… press a key, or hold Ctrl / Shift / Alt and press a key (→ combo). Tap Ctrl / Shift / Alt alone → assign that modifier. For combos with Win use the Key combo tab.");
    public string SearchPlaceholder => T("🔍 Lọc phím: tên hoặc mã hex (vd F5, Num, 0x2A)", "🔍 Filter keys: name or hex code (e.g. F5, Num, 0x2A)");
    public string ComboTab => T("✚ Tổ hợp phím", "✚ Key combo");
    public string ComboStep1 => T("1. Chọn modifier (một hoặc nhiều)", "1. Pick modifiers (one or more)");
    public string ComboStep2 => T("2. Chọn phím", "2. Pick a key");
    public string ComboKeyPlaceholder => T("Chọn phím…", "Pick a key…");
    public string ComboTip => T(
        "Mẹo: bấm ⌨ Bắt phím rồi giữ Ctrl + nhấn C trên bàn phím thật. Mới hỗ trợ modifier bên trái (capture 10, 11).",
        "Tip: click ⌨ Capture key, then hold Ctrl and press C on your real keyboard. Only left-side modifiers are supported so far (captures 10, 11).");
    public string ResetToDefault => T("Reset về mặc định", "Reset to default");
    public string DiscardChanges => T("Hủy thay đổi", "Discard changes");

    // Lighting
    public string EffectsTitle => T("Hiệu ứng đèn (0x84 offset 0x0A)", "Light effect (0x84 offset 0x0A)");
    public string EffectsNote => T(
        "Viền xanh = đang dùng trên thiết bị. Apply hiệu ứng: đọc 0x84 → đổi 0x0A (+ độ sáng / tốc độ 0–4 của hiệu ứng đó nếu bạn kéo thanh trượt) → ghi 0x04 — đúng từng byte như app hãng (capture batch 6). Màu giữ nguyên. Self-define (màu từng phím) chưa hỗ trợ.",
        "Blue outline = active on the device. Apply effect: read 0x84 → change 0x0A (+ that effect's brightness / speed 0–4 if you moved the slider) → write 0x04 — byte-for-byte like the vendor app (capture batch 6). Colors stay unchanged. Self-define (per-key colors) is not supported.");
    public string ApplyEffect => T("Apply hiệu ứng", "Apply effect");
    public string BrightnessTip => T("Độ sáng (V của HSV)", "Brightness (V of HSV)");
    public string StaticColorTitle => T("Màu static (bảng màu 0x0A, slot 7)", "Static color (color table 0x0A, slot 7)");
    public string RgbOrderNote => T("R, G, B — đúng thứ tự byte trên dây", "R, G, B — same byte order as on the wire");
    public string DefaultPalette => T("Palette mặc định trong capture", "Default palette from the capture");
    public string Read0x84 => T("Đọc 0x84", "Read 0x84");
    public string LightingWarn => T(
        "Apply: đọc 0x84 → nếu chưa ở static thì ghi 0x04 (mode = 01, read-modify-write, chỉ đổi offset 0x0A) → ghi 0x0A. " +
        "Các slot màu khác giữ nguyên như capture. Chưa có lệnh đọc bảng màu nên không hiển thị được màu đang có trên thiết bị.",
        "Apply: read 0x84 → if not in static mode, write 0x04 (mode = 01, read-modify-write, only offset 0x0A changes) → write 0x0A. " +
        "Other color slots stay as in the capture. There is no command to read the color table, so the color currently on the device cannot be shown.");

    // Device
    public string Connection => T("Kết nối", "Connection");
    public string ConnectionNote => T(
        "Ưu tiên cắm dây (258A:010C); không có thì qua receiver 2.4G (3554:FA09). Đổi chế độ / numpad vừa thức dậy: bấm Quét lại. Qua 2.4G ghi được layer gốc, màu static, sleep; tắt app hãng trước.",
        "Wired (258A:010C) is preferred, otherwise the 2.4G receiver (3554:FA09). After switching mode / waking the numpad: press Rescan. Over 2.4G you can write the base layer, static color and sleep; close the vendor app first.");
    public string Rescan => T("Quét lại", "Rescan");
    public string SleepTitle => T("Sleep (chế độ 2.4G)", "Sleep (2.4G mode)");
    public string SleepNote => T(
        "Đơn vị 30 s, đúng 10 nấc của app hãng (30 s … 20 Min). Block settings dùng chung cho 2 chế độ: chỉnh qua dây có tác dụng ở 2.4G. Công tắc OFF chưa có capture nên chưa hỗ trợ.",
        "Unit 30 s, the vendor app's 10 steps (30 s … 20 Min). Both modes share the settings block: changing it over the cable also applies to 2.4G. The OFF switch has no capture yet, so it is not supported.");
    public string AppData => T("Dữ liệu app", "App data");
    public string AppDataNote => T(
        "Apply lưu thẳng xuống numpad và báo kết quả; hex mọi gói gửi / nhận nằm trong log.",
        "Apply writes straight to the numpad and reports the result; the hex of every packet sent / received is in the log.");
    public string Files => T("File:", "Files:");
    public string OpenDataFolder => T("Mở thư mục dữ liệu", "Open data folder");
    public string SettingsBlockTitle => T("Block settings 128 byte (0x84, chỉ đọc)", "Settings block, 128 bytes (0x84, read-only)");

    // Log
    public string Clear => T("Xóa", "Clear");
}
