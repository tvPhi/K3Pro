using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using K3Pro.App.Localization;
using K3Pro.App.Services;
using K3Pro.App.ViewModels;
using K3Pro.Protocol;

namespace K3Pro.App.Tests;

public class ViewModelTests
{
    [AvaloniaFact]
    public void First_run_follows_os_language_until_the_user_picks_one()
    {
        using var h = new TestHarness();

        var vi = h.CreateViewModel(systemLanguage: UiLanguage.Vi);
        Assert.Equal(UiLanguage.Vi, vi.SelectedLanguage.Language);
        Assert.Equal("Reset về mặc định", Tr.I.ResetToDefault);
        Assert.Equal("Chọn một phím trên layout", vi.Keymap.SelectedTitle);

        var en = h.CreateViewModel(systemLanguage: UiLanguage.En); // English / other-language machine, nothing picked yet
        Assert.Equal(UiLanguage.En, en.SelectedLanguage.Language);
        Assert.Equal("Select a key on the layout", en.Keymap.SelectedTitle);
        Assert.False(File.Exists(h.SettingsStore.Path)); // nothing picked → no file written, next run still follows the OS

        en.SelectedLanguage = en.Languages.Single(l => l.Language == UiLanguage.Vi); // the user's pick wins over the OS
        Assert.Equal(UiLanguage.Vi, h.CreateViewModel(systemLanguage: UiLanguage.En).SelectedLanguage.Language);
    }

    [AvaloniaFact]
    public async Task Switching_to_english_updates_every_tab_immediately_and_persists()
    {
        using var h = new TestHarness();
        var vm = h.CreateViewModel();
        var km = vm.Keymap;
        km.SelectKeyCommand.Execute(km.Keys.Single(k => k.Index == 4));
        km.PickCommand.Execute(km.PickerTabs[KeyPickerCatalog.MediaTab].AllItems.Single(i => i.Label == "🔊 Volume +")); // not applied yet
        await vm.Device.ReadSettingsCommand.ExecuteAsync(null);
        var changed = new List<string?>();
        Tr.I.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SelectedLanguage = vm.Languages.Single(l => l.Language == UiLanguage.En);

        Assert.Equal(UiLanguage.En, Lang.Current);
        Assert.Contains(string.Empty, changed); // static XAML refreshes
        Assert.Equal("Reset to default", Tr.I.ResetToDefault);
        Assert.StartsWith("Connected by cable", vm.Session.ConnectionText);
        Assert.Equal("Key #4 — Num 1", km.SelectedTitle);
        Assert.StartsWith("Current: ", km.SelectedDetail);
        Assert.Equal("Apply (1 change)", km.ApplyText);
        Assert.Equal("⌨ Keyboard", km.PickerTabs[KeyPickerCatalog.KeyboardTab].Header);
        Assert.True(km.PickerTabs[KeyPickerCatalog.MediaTab].AllItems.Single(i => i.Label == "🔊 Volume +").IsSelected); // rebuilt picker still highlights the right button
        Assert.Equal(KeyPickerCatalog.MediaTab, km.CategoryIndex);
        Assert.Equal("Knob", km.Keys.Single(k => k.Index == 18).Label);
        Assert.True(km.HasPending); // switching language keeps changes not yet applied
        Assert.StartsWith("Device (offset 0x18):", vm.Device.DeviceSleepLine);
        Assert.Contains("2.4G mode", vm.Device.FieldsText);
        Assert.Equal("Red", vm.Lighting.Presets[0].Name);
        Assert.StartsWith("Packet log", vm.Log.Header);
        Assert.Contains("\"en\"", File.ReadAllText(h.SettingsStore.Path));
        Assert.Equal("Lock PC (Win + L)", km.PickerTabs[KeyPickerCatalog.CommandTab].AllItems.Single(i => i.Label == "🔒 Lock PC").Tooltip); // name comes from Protocol

        await km.ApplyCommand.ExecuteAsync(null);
        Assert.Equal("✅ Saved: Keymap page 0", h.Notifications.Last().Message);

        var next = h.CreateViewModel(); // next run
        Assert.Equal(UiLanguage.En, next.SelectedLanguage.Language);
        Assert.Equal("Select a key on the layout", next.Keymap.SelectedTitle);

        next.SelectedLanguage = next.Languages.Single(l => l.Language == UiLanguage.Vi);
        Assert.Equal("Núm", next.Keymap.Keys.Single(k => k.Index == 18).Label);
        Assert.Contains("\"vi\"", File.ReadAllText(h.SettingsStore.Path));
    }

    [AvaloniaFact]
    public void Bluetooth_battery_shows_in_the_top_bar_while_connected()
    {
        using var h = new TestHarness();
        h.Device.State = ConnectionState.Disconnected with { Error = "x" };
        var vm = h.CreateViewModel(systemLanguage: UiLanguage.En);
        Assert.False(vm.Session.HasBattery);

        h.Device.RaiseBluetooth(new K3Pro.Protocol.Bluetooth.BluetoothStatus(true, 87, "EB968A1CC4A9"));
        Assert.Equal("🔋 87%", vm.Session.BatteryText);
        Assert.True(vm.Session.IsBluetoothOnly);
        Assert.StartsWith("Using Bluetooth (K3PRO 5.0)", vm.Session.ConnectionText);
        Assert.True(vm.Session.IsWriteBlocked); // keymap / lighting / sleep can't be changed over Bluetooth
        Assert.StartsWith("On Bluetooth", vm.Session.WriteBlockedText);

        h.Device.Raise(h.Device.State with { IsConnected = true, Error = null, Kind = ConnectionKind.Wired }); // cable plugged in as well
        Assert.False(vm.Session.IsBluetoothOnly);
        Assert.False(vm.Session.IsWriteBlocked);
        Assert.True(vm.Session.HasBattery);

        h.Device.RaiseBluetooth(K3Pro.Protocol.Bluetooth.BluetoothStatus.None); // switched to 2.4G / cable
        Assert.False(vm.Session.HasBattery);
    }

    [AvaloniaFact]
    public void Old_settings_file_with_dry_run_field_still_loads()
    {
        using var h = new TestHarness();
        File.WriteAllText(h.SettingsStore.Path, """{ "dryRun": false }""");

        Assert.Equal(UiLanguage.Vi, h.CreateViewModel().SelectedLanguage.Language);
    }

    [AvaloniaFact]
    public void Layout_matches_vendor_matrix_and_special_entries_are_locked()
    {
        using var h = new TestHarness();

        var keys = h.CreateViewModel().Keymap.Keys;

        Assert.Equal(22, keys.Count); // 20 keys + 2 knob rotation directions
        Assert.All(keys, k => Assert.True(k.IsEditable)); // Fn / knob / rotation unlocked after captures 17–17f
        Assert.Equal("Núm", keys.Single(k => k.Index == 18).Label);
        Assert.Equal("⟲ Vol−", keys.Single(k => k.Index == 11).Label);
        Assert.True(keys.Single(k => k.Index == 18).IsKnob);
        Assert.Equal("Num 1", keys.Single(k => k.Index == 4).Label);
    }

    [AvaloniaFact]
    public async Task Keymap_apply_live_executes_plan_and_persists_overrides()
    {
        using var h = new TestHarness();
        var km = h.CreateViewModel().Keymap;

        Remap(km, index: 4, code: 0x04);
        await km.ApplyCommand.ExecuteAsync(null);

        var plan = Assert.Single(h.Device.Executed);
        Assert.Equal(Command.WriteKeymap, Assert.Single(plan.Writes).Command);
        Assert.False(km.HasPending);
        Assert.StartsWith("✅ Đã lưu", h.Notifications.Last().Message);
        Assert.Equal(new Dictionary<int, KeymapEntry> { [4] = KeymapEntry.HidUsage(0x04) }, h.KeymapStore.LoadOverrides());

        var reloaded = h.CreateViewModel().Keymap; // next run: state loaded from file
        Assert.Equal("A", reloaded.Keys.Single(k => k.Index == 4).Label);
        Assert.True(reloaded.Keys.Single(k => k.Index == 4).IsRemapped);
    }

    [AvaloniaFact]
    public async Task Keymap_reset_forces_baseline_page_and_clears_state()
    {
        using var h = new TestHarness();
        h.KeymapStore.Save(new Dictionary<int, byte> { [4] = 0x04 });
        var km = h.CreateViewModel().Keymap;

        await km.ResetToDefaultCommand.ExecuteAsync(null);

        Assert.Equal(PacketBuilder.WriteKeymap(CaptureBaseline.KeymapPage(0)), Assert.Single(Assert.Single(h.Device.Executed).Writes).Packet);
        Assert.Empty(h.KeymapStore.LoadOverrides());
        Assert.Equal("Num 1", km.Keys.Single(k => k.Index == 4).Label);
    }

    [AvaloniaFact]
    public async Task Knob_press_remap_then_revert_writes_vendor_default_back()
    {
        using var h = new TestHarness();
        var km = h.CreateViewModel().Keymap;

        Remap(km, index: 18, code: 0x06);
        await km.ApplyCommand.ExecuteAsync(null);
        Assert.Equal("C", km.Keys.Single(k => k.Index == 18).Label);

        km.SelectKeyCommand.Execute(km.Keys.Single(k => k.Index == 18));
        km.RevertSelectedToDefaultCommand.Execute(null);
        await km.ApplyCommand.ExecuteAsync(null);

        var last = h.Device.Executed[^1].Writes.Single().Packet;
        Assert.Equal(PacketBuilder.WriteKeymap(CaptureBaseline.KeymapPage(0)), last); // = capture 17d
        Assert.Equal("Núm", km.Keys.Single(k => k.Index == 18).Label);
        Assert.Empty(h.KeymapStore.LoadOverrides());
    }

    [AvaloniaFact]
    public void Locked_key_cannot_be_remapped()
    {
        using var h = new TestHarness();
        var layout = new LayoutConfig(60, 6, [new KeyLayout(12, "trống", 0, 0)]); // index 12: no physical key, not captured yet
        var km = new ViewModels.KeymapViewModel(layout, null, h.KeymapStore,
            new WriteCoordinator(h.Device, h.Log, (_, _) => { }), h.Log);

        km.SelectKeyCommand.Execute(km.Keys.Single());
        km.SelectedUsage = HidUsages.Keyboard.First(u => u.Code == 0x04);
        km.IsCapturing = true;

        Assert.False(km.CanEditSelected);
        Assert.False(km.HandleCapturedKey(0x04));
        Assert.False(km.HasPending);
    }

    [AvaloniaFact]
    public void Capture_key_maps_physical_key_to_hid_usage()
    {
        using var h = new TestHarness();
        var km = h.CreateViewModel().Keymap;
        km.SelectKeyCommand.Execute(km.Keys.Single(k => k.Index == 0));
        km.ToggleCaptureCommand.Execute(null);

        Assert.True(km.HandleCapturedKey(PhysicalKeyMap.ToHidUsage(PhysicalKey.F5)!.Value));

        Assert.False(km.IsCapturing);
        Assert.Equal("F5", km.Keys.Single(k => k.Index == 0).Label);
        Assert.Equal((byte)0x3E, km.SelectedUsage?.Code);
    }

    [Fact]
    public void Every_enabled_picker_button_is_a_captured_entry()
    {
        var items = KeyPickerCatalog.Build().SelectMany(t => t.AllItems).ToList();

        Assert.All(items.Where(i => i.IsEnabled), i => Assert.True(KeymapActions.IsAllowed(i.Entry!.Value), $"{i.Label} {i.Entry}"));
        Assert.Equal(items.Count(i => i.IsEnabled), items.Where(i => i.IsEnabled).Select(i => i.Entry).Distinct().Count()); // no duplicate buttons
        Assert.Contains(items, i => !i.IsEnabled); // vendor app buttons not captured yet are still shown, but locked
    }

    [AvaloniaFact]
    public void Picker_buttons_assign_and_highlight_current_entry()
    {
        using var h = new TestHarness();
        var km = h.CreateViewModel().Keymap;
        PickerItem Item(string label) => km.PickerTabs.SelectMany(t => t.AllItems).Single(i => i.Label == label);
        var num1 = km.Keys.Single(k => k.Index == 4);
        km.SelectKeyCommand.Execute(num1);
        Assert.True(Item("Num1").IsSelected);
        Assert.Equal(KeyPickerCatalog.KeyboardTab, km.CategoryIndex);

        km.PickCommand.Execute(Item("🔊 Volume +"));

        Assert.Equal(new KeymapEntry(0x02, 0x00, 0x00, 0xE9), num1.Pending);
        Assert.True(Item("🔊 Volume +").IsSelected);
        Assert.False(Item("Num1").IsSelected);
        Assert.Equal(KeyPickerCatalog.MediaTab, km.CategoryIndex);
        Assert.Equal("Vol+", km.SelectedLabel);

        km.PickCommand.Execute(Item("🔉 Volume −")); // locked — not captured yet
        Assert.Equal(new KeymapEntry(0x02, 0x00, 0x00, 0xE9), num1.Pending);

        km.PickCommand.Execute(Item("LCtrl"));
        Assert.Equal(new KeymapEntry(0x00, 0x01, 0x00, 0x00), num1.Pending);
        Assert.Equal(KeyPickerCatalog.KeyboardTab, km.CategoryIndex);

        km.PickCommand.Execute(Item("Num1")); // back to the written value → no longer pending
        Assert.False(km.HasPending);
    }

    [AvaloniaFact]
    public void Capture_with_modifiers_builds_combo_or_lone_modifier()
    {
        using var h = new TestHarness();
        var km = h.CreateViewModel().Keymap;
        var key = km.Keys.Single(k => k.Index == 0);
        km.SelectKeyCommand.Execute(key);

        km.ToggleCaptureCommand.Execute(null);
        Assert.True(km.HandleCapturedKey(0x06, KeymapActions.ModCtrl));
        Assert.Equal("Ctrl+C", key.Label);
        Assert.Equal(KeyPickerCatalog.ComboTab, km.CategoryIndex);
        Assert.True(km.ComboCtrl);

        km.ToggleCaptureCommand.Execute(null);
        Assert.True(km.HandleCapturedModifier(KeymapActions.ModShift));
        Assert.Equal(new KeymapEntry(0x00, 0x02, 0x00, 0x00), key.Pending);

        km.ToggleCaptureCommand.Execute(null);
        Assert.True(km.HandleCapturedModifier(0x10)); // RCtrl — not captured yet, no change
        Assert.Equal(new KeymapEntry(0x00, 0x02, 0x00, 0x00), key.Pending);
        Assert.False(km.IsCapturing);
    }

    [AvaloniaFact]
    public void Search_filters_picker_buttons_without_switching_tab()
    {
        using var h = new TestHarness();
        var km = h.CreateViewModel().Keymap;
        km.SelectKeyCommand.Execute(km.Keys.Single(k => k.Index == 4));
        km.PickCommand.Execute(km.PickerTabs[KeyPickerCatalog.MediaTab].AllItems.First(i => i.IsEnabled));
        km.CategoryIndex = KeyPickerCatalog.KeyboardTab; // the user reopens the Keyboard tab to search
        var keyboard = km.PickerTabs[KeyPickerCatalog.KeyboardTab];

        km.SearchText = "f5";

        Assert.Equal(KeyPickerCatalog.KeyboardTab, km.CategoryIndex);
        Assert.Equal(["F5"], keyboard.AllItems.Where(i => i.IsVisible).Select(i => i.Label));
        Assert.Equal(["Chức năng"], keyboard.Sections.Where(s => s.IsVisible).Select(s => s.Title));

        km.SearchText = "0x2a";
        Assert.Equal(["Back"], keyboard.AllItems.Where(i => i.IsVisible).Select(i => i.Label));

        km.SearchText = "";
        Assert.All(keyboard.AllItems, i => Assert.True(i.IsVisible));
        Assert.True(km.HasPending); // filtering keeps the selection
    }

    [AvaloniaFact]
    public void Search_filters_by_name_or_hex()
    {
        using var h = new TestHarness();
        var km = h.CreateViewModel().Keymap;

        km.SearchText = "0x2a";
        Assert.Equal("Backspace", Assert.Single(km.FilteredUsages).Name);

        km.SearchText = "num 1";
        Assert.Contains(km.FilteredUsages, u => u.Code == 0x59);
    }

    [AvaloniaFact]
    public async Task Effect_apply_changes_only_mode_byte_and_locked_effects_cannot_be_picked()
    {
        using var h = new TestHarness();
        h.Device.Settings = CaptureBaseline.ReferenceSettings().WithLightingMode(LightingModes.Rainbow);
        var vm = h.CreateViewModel().Lighting;
        var fixedOn = vm.Effects.Single(e => e.Label == "Fixed_on");
        var respire = vm.Effects.Single(e => e.Label == "Respire");
        var selfDefine = vm.Effects.Single(e => e.Label == "Self-define");

        Assert.Equal(17, vm.CapturedEffectCount);
        await vm.PickEffectCommand.ExecuteAsync(selfDefine); // needs command 02 — not supported yet
        Assert.Null(vm.SelectedEffect);
        Assert.False(vm.CanApplyEffect);

        await vm.PickEffectCommand.ExecuteAsync(fixedOn);
        Assert.True(fixedOn.IsSelected);
        Assert.True(vm.ShowBrightness);
        Assert.False(vm.ShowSpeed); // Fixed_on: KB.ini LedOpt1 speed = 0
        await vm.ApplyEffectCommand.ExecuteAsync(null);

        var write = Assert.Single(Assert.Single(h.Device.Executed).Writes);
        Assert.Equal(Command.WriteSettings, write.Command);
        Assert.Equal([(K3ProConstants.HeaderLength + Settings.LightingModeOffset, K3ProConstants.HeaderLength + Settings.LightingModeOffset + 1)], write.Changes);
        Assert.Equal(LightingModes.Static, write.Packet[K3ProConstants.HeaderLength + Settings.LightingModeOffset]);
        Assert.False(fixedOn.IsCurrent); // fake device still returns mode 03

        h.Device.Settings = CaptureBaseline.ReferenceSettings(); // static
        await vm.RefreshModeCommand.ExecuteAsync(null);
        Assert.True(fixedOn.IsCurrent);
        Assert.False(respire.IsCurrent);
    }

    [AvaloniaFact]
    public async Task Effect_brightness_and_speed_are_written_only_when_moved()
    {
        using var h = new TestHarness();
        var vm = h.CreateViewModel().Lighting;
        var respire = vm.Effects.Single(e => e.Label == "Respire");
        var reference = CaptureBaseline.ReferenceSettings();

        await vm.PickEffectCommand.ExecuteAsync(respire);
        Assert.Equal(reference.Brightness(LightingModes.Respire), vm.EffectBrightness); // loaded from the read block
        Assert.Equal(reference.Speed(LightingModes.Respire), vm.EffectSpeed);

        await vm.ApplyEffectCommand.ExecuteAsync(null); // sliders untouched → only the mode changes
        var only = Assert.Single(Assert.Single(h.Device.Executed).Writes);
        int mode = K3ProConstants.HeaderLength + Settings.LightingModeOffset;
        Assert.Equal([(mode, mode + 1)], only.Changes);

        vm.EffectBrightness = 0;
        vm.EffectSpeed = 1;
        await vm.ApplyEffectCommand.ExecuteAsync(null);
        var write = Assert.Single(h.Device.Executed[1].Writes);
        int b = K3ProConstants.HeaderLength + Settings.BrightnessOffset(LightingModes.Respire);
        Assert.Equal(0, write.Packet[b]);
        Assert.Equal((byte)(0x10 | (reference.Data[Settings.SpeedOffset(LightingModes.Respire)] & 0x0F)), write.Packet[b + 1]);
        Assert.All(write.Changes, r => Assert.True(r.Start >= mode && r.End <= b + 2 && (r.Start == mode || r.Start >= b)));
    }

    [AvaloniaFact]
    public async Task Lighting_apply_from_non_static_plans_settings_then_color_table()
    {
        using var h = new TestHarness();
        h.Device.Settings = CaptureBaseline.ReferenceSettings().WithLightingMode(LightingModes.Rainbow);
        var vm = h.CreateViewModel().Lighting;
        vm.SelectedColor = Color.FromRgb(0xFF, 0x00, 0x00);

        await vm.ApplyCommand.ExecuteAsync(null);

        var plan = Assert.Single(h.Device.Executed);
        Assert.Equal([Command.WriteSettings, Command.WriteColorTable], plan.Writes.Select(w => w.Command));
        Assert.Equal(CaptureBaseline.ReferenceSettings().Data.ToArray(),
            plan.Writes[0].Packet.AsSpan(K3ProConstants.HeaderLength, Settings.Length).ToArray());
    }

    [AvaloniaFact]
    public void Corrupt_keymap_state_falls_back_to_default_without_overwriting()
    {
        using var h = new TestHarness();
        File.WriteAllText(h.KeymapStore.Path, """{ "overrides": { "12": 4 } }""");

        var km = h.CreateViewModel().Keymap;

        Assert.NotNull(km.StateError);
        Assert.Equal("Num 1", km.Keys.Single(k => k.Index == 4).Label);
        Assert.Contains("\"12\"", File.ReadAllText(h.KeymapStore.Path));
    }

    [AvaloniaFact]
    public void Lighting_hex_input_and_color_stay_in_sync()
    {
        using var h = new TestHarness();
        var vm = h.CreateViewModel().Lighting;

        vm.HexInput = "12ab3c";
        Assert.Equal(Color.FromRgb(0x12, 0xAB, 0x3C), vm.SelectedColor);

        vm.HexInput = "zz";
        Assert.Equal(Color.FromRgb(0x12, 0xAB, 0x3C), vm.SelectedColor);

        vm.SelectedColor = Color.FromRgb(0x00, 0xFF, 0x00);
        Assert.Equal("00FF00", vm.HexInput);
    }

    [AvaloniaFact]
    public async Task Sleep_apply_live_writes_only_offset_0x18()
    {
        using var h = new TestHarness();
        var vm = h.CreateViewModel().Device;
        vm.SleepUnits = 40;

        await vm.ApplySleepCommand.ExecuteAsync(null);

        var write = Assert.Single(Assert.Single(h.Device.Executed).Writes);
        Assert.Equal([(K3ProConstants.HeaderLength + Settings.SleepOffset, K3ProConstants.HeaderLength + Settings.SleepOffset + 1)], write.Changes);
        Assert.Equal(0x28, write.Packet[K3ProConstants.HeaderLength + Settings.SleepOffset]);
    }

    private static ConnectionState Wireless(FakeDevice d) => d.State with { Kind = ConnectionKind.Wireless, DevicePath = "receiver" };

    [AvaloniaFact]
    public async Task Over_2_4G_keymap_apply_goes_through_receiver()
    {
        using var h = new TestHarness();
        var vm = h.CreateViewModel();
        h.Device.Raise(Wireless(h.Device));

        Remap(vm.Keymap, index: 4, code: 0x04);
        await vm.Keymap.ApplyCommand.ExecuteAsync(null);

        Assert.Equal([ConnectionKind.Wireless], h.Device.ExecutedKinds);
        Assert.False(vm.Keymap.HasPending);
        Assert.False(h.Notifications.Last().IsError);
        Assert.True(vm.Session.IsWireless);
    }

    [AvaloniaFact]
    public async Task Over_2_4G_static_color_and_sleep_go_through_receiver()
    {
        using var h = new TestHarness();
        var vm = h.CreateViewModel();
        h.Device.Raise(Wireless(h.Device));

        await vm.Lighting.ApplyCommand.ExecuteAsync(null);
        vm.Device.SleepUnits = 40;
        await vm.Device.ApplySleepCommand.ExecuteAsync(null);

        Assert.Equal([ConnectionKind.Wireless, ConnectionKind.Wireless], h.Device.ExecutedKinds);
        Assert.Equal(Command.WriteColorTable, h.Device.Executed[0].Writes[^1].Command);
        Assert.Equal(Command.WriteSettings, Assert.Single(h.Device.Executed[1].Writes).Command);
    }

    [AvaloniaFact]
    public async Task Over_2_4G_sleep_plan_encodes_to_10_receiver_frames()
    {
        using var h = new TestHarness();
        var vm = h.CreateViewModel();
        h.Device.Raise(Wireless(h.Device));

        vm.Device.SleepUnits = 40;
        await vm.Device.ApplySleepCommand.ExecuteAsync(null);

        var write = Assert.Single(Assert.Single(h.Device.Executed).Writes);
        var frames = WireEncoding.Encode(ConnectionKind.Wireless, write);
        Assert.Equal(10, frames.Count);
        Assert.All(frames, f => Assert.Equal(20, f.Length));
    }

    [AvaloniaFact]
    public async Task Media_combo_mouse_and_command_actions_apply_and_persist()
    {
        using var h = new TestHarness();
        var km = h.CreateViewModel().Keymap;

        km.SelectKeyCommand.Execute(km.Keys.Single(k => k.Index == 4));
        km.SelectedAction = km.MediaActions.Single(a => a.Short == "Vol+");
        km.SelectKeyCommand.Execute(km.Keys.Single(k => k.Index == 10));
        km.ComboCtrl = true;
        km.ComboKey = HidUsages.Keyboard.Single(u => u.Code == 0x06);
        km.SelectKeyCommand.Execute(km.Keys.Single(k => k.Index == 16));
        km.SelectedAction = km.MouseActions.Single();
        km.SelectKeyCommand.Execute(km.Keys.Single(k => k.Index == 0));
        km.SelectedAction = km.CommandActions.Single();
        Assert.Equal(4, km.PendingCount);

        await km.ApplyCommand.ExecuteAsync(null);

        var packet = Assert.Single(Assert.Single(h.Device.Executed).Writes).Packet;
        KeymapEntry At(int i) => KeymapEntry.Read(packet.AsSpan(K3ProConstants.HeaderLength + i * KeymapEntry.Size));
        Assert.Equal(new KeymapEntry(0x02, 0x00, 0x00, 0xE9), At(4));
        Assert.Equal(new KeymapEntry(0x00, 0x01, 0x00, 0x06), At(10));
        Assert.Equal(new KeymapEntry(0x01, 0x01, 0x01, 0x00), At(16));
        Assert.Equal(new KeymapEntry(0x00, 0x08, 0x0F, 0x00), At(0));

        var reloaded = h.CreateViewModel().Keymap; // next run
        Assert.Equal("Vol+", reloaded.Keys.Single(k => k.Index == 4).Label);
        Assert.Equal("Ctrl+C", reloaded.Keys.Single(k => k.Index == 10).Label);
        reloaded.SelectKeyCommand.Execute(reloaded.Keys.Single(k => k.Index == 10));
        Assert.Equal(KeyPickerCatalog.ComboTab, reloaded.CategoryIndex); // opens the Combo tab
        Assert.True(reloaded.ComboCtrl);
        Assert.False(reloaded.HasPending);          // syncing the selection creates no change
    }

    [AvaloniaFact]
    public void Old_state_format_with_hid_bytes_still_loads()
    {
        using var h = new TestHarness();
        File.WriteAllText(h.KeymapStore.Path, """{ "page": 0, "overrides": { "4": 4 } }""");

        var km = h.CreateViewModel().Keymap;

        Assert.Null(km.StateError);
        Assert.Equal("A", km.Keys.Single(k => k.Index == 4).Label);
    }

    [Fact]
    public void Physical_key_map_names_exist_in_avalonia()
    {
        var names = Enum.GetNames<PhysicalKey>().ToHashSet();
        Assert.All(PhysicalKeyMap.ByName.Keys, n => Assert.Contains(n, names));
        Assert.Equal((byte)0x59, PhysicalKeyMap.ToHidUsage(PhysicalKey.NumPad1));
        Assert.Equal((byte)0x04, PhysicalKeyMap.ToHidUsage(PhysicalKey.A));
    }

    private static void Remap(ViewModels.KeymapViewModel km, int index, byte code)
    {
        km.SelectKeyCommand.Execute(km.Keys.Single(k => k.Index == index));
        km.SelectedUsage = HidUsages.Keyboard.Single(u => u.Code == code);
    }
}
