using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using K3Pro.App.ViewModels;
using K3Pro.App.Views;
using K3Pro.Protocol;

namespace K3Pro.App.Tests;

/// <summary>Renders the real window (Skia, headless) with fake services → PNGs in bin/.../screenshots for UI review.</summary>
public class ScreenshotTests
{
    private static readonly string OutDir = Path.Combine(AppContext.BaseDirectory, "screenshots");

    [AvaloniaFact]
    public async Task Render_main_window_tabs()
    {
        using var h = new TestHarness();
        var vm = h.CreateViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1180, Height = 880 };
        window.Show();

        // Keymap: one key already remapped (state), one key pending Apply and currently selected.
        h.KeymapStore.Save(new Dictionary<int, byte> { [0] = 0x29 });
        vm = h.CreateViewModel();
        window.DataContext = vm;
        PickerItem Item(string label) => vm.Keymap.PickerTabs.SelectMany(t => t.AllItems).Single(i => i.Label == label);
        vm.Keymap.SelectKeyCommand.Execute(vm.Keymap.Keys.Single(k => k.Index == 4));
        vm.Keymap.PickCommand.Execute(Item("A"));
        Save(window, "1-keymap.png");

        vm.Keymap.SelectKeyCommand.Execute(vm.Keymap.Keys.Single(k => k.Index == 10));
        vm.Keymap.ComboCtrl = true;
        vm.Keymap.ComboKey = HidUsages.Keyboard.Single(u => u.Code == 0x06);
        vm.Keymap.SelectKeyCommand.Execute(vm.Keymap.Keys.Single(k => k.Index == 16));
        vm.Keymap.PickCommand.Execute(Item("🔊 Volume +"));
        Save(window, "1c-keymap-media.png");
        vm.Keymap.SelectKeyCommand.Execute(vm.Keymap.Keys.Single(k => k.Index == 22));
        vm.Keymap.PickCommand.Execute(Item("🔒 Khóa máy"));
        Save(window, "1e-keymap-command.png");
        vm.Keymap.SelectKeyCommand.Execute(vm.Keymap.Keys.Single(k => k.Index == 4));
        vm.Keymap.CategoryIndex = KeyPickerCatalog.MouseTab;
        Save(window, "1f-keymap-mouse-tab.png");
        vm.Keymap.SelectKeyCommand.Execute(vm.Keymap.Keys.Single(k => k.Index == 10));
        Save(window, "1d-keymap-combo.png");

        await vm.Keymap.ApplyCommand.ExecuteAsync(null); // fake device: records the write + "saved" notification
        Assert.Single(h.Device.Executed);
        Save(window, "1b-keymap-after-apply.png");

        vm.SelectedTab = 1;
        await vm.Lighting.RefreshModeCommand.ExecuteAsync(null);
        await vm.Lighting.PickEffectCommand.ExecuteAsync(vm.Lighting.Effects[1]); // Respire: has brightness + speed
        Save(window, "2-lighting.png");

        vm.SelectedTab = 2;
        await vm.Device.ReadSettingsCommand.ExecuteAsync(null);
        vm.Notify("✅ Đã lưu: Sleep 10 Min → 20 Min", false);
        Save(window, "3-device.png");

        // English: switch on the open window (view is not rebuilt)
        vm.SelectedLanguage = vm.Languages.Single(l => l.Language == UiLanguage.En);
        vm.Notification = null;
        Save(window, "4c-english-device.png");
        vm.SelectedTab = 0;
        vm.Keymap.SelectKeyCommand.Execute(vm.Keymap.Keys.Single(k => k.Index == 16));
        Save(window, "4-english-keymap.png");
        vm.Keymap.CategoryIndex = KeyPickerCatalog.ComboTab;
        Save(window, "4b-english-keymap-combo.png");
        vm.SelectedTab = 1;
        Save(window, "4d-english-lighting.png");

        // Bluetooth only: battery pill + "settings over cable / 2.4G only"
        h.Device.Raise(K3Pro.App.Services.ConnectionState.Disconnected with { Error = "x" });
        h.Device.RaiseBluetooth(new K3Pro.Protocol.Bluetooth.BluetoothStatus(true, 100, "EB968A1CC4A9"));
        vm.SelectedTab = 2;
        Save(window, "5-bluetooth.png");
        vm.SelectedTab = 0;
        Save(window, "5b-bluetooth-keymap-locked.png");

        Assert.True(File.Exists(Path.Combine(OutDir, "3-device.png")));
    }

    /// <summary>Clean English screenshots for README (copied to docs/images): starts in English, a few keys already remapped.</summary>
    [AvaloniaFact]
    public async Task Render_readme_screenshots()
    {
        using var h = new TestHarness();
        h.KeymapStore.Save(new Dictionary<int, KeymapEntry>
        {
            [10] = KeymapActions.Combo(KeymapActions.ModCtrl, 0x06),   // Ctrl+C
            [16] = new KeymapEntry(0x02, 0x00, 0x00, 0xE9),           // Volume +
            [22] = new KeymapEntry(0x00, 0x08, 0x0F, 0x00),           // Lock PC
        });
        var vm = h.CreateViewModel(systemLanguage: UiLanguage.En);
        var window = new MainWindow { DataContext = vm, Width = 1180, Height = 880 };
        window.Show();

        vm.Keymap.SelectKeyCommand.Execute(vm.Keymap.Keys.Single(k => k.Index == 4));
        vm.Keymap.PickCommand.Execute(vm.Keymap.PickerTabs.SelectMany(t => t.AllItems).Single(i => i.Label == "A"));
        Save(window, "readme-keymap.png");
        vm.Keymap.SelectKeyCommand.Execute(vm.Keymap.Keys.Single(k => k.Index == 10));
        Save(window, "readme-keymap-combo.png");

        vm.SelectedTab = 1;
        await vm.Lighting.RefreshModeCommand.ExecuteAsync(null);
        await vm.Lighting.PickEffectCommand.ExecuteAsync(vm.Lighting.Effects.Single(e => e.Label == "Respire"));
        Save(window, "readme-lighting.png");

        vm.SelectedTab = 2;
        await vm.Device.ReadSettingsCommand.ExecuteAsync(null);
        Save(window, "readme-device.png");

        Assert.Equal(UiLanguage.En, Lang.Current);
    }

    private static void Save(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory(OutDir);
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Could not capture a frame.");
#pragma warning disable CS0618 // new BitmapEncoderOptions overload in Avalonia 12; default PNG is enough for UI review images
        frame.Save(Path.Combine(OutDir, name));
#pragma warning restore CS0618
    }
}
