using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using K3Pro.App.Services;
using K3Pro.App.ViewModels;
using K3Pro.Protocol;

namespace K3Pro.App.Views;

public partial class KeymapView : UserControl
{
    private TopLevel? _topLevel;

    // Modifier held during "Capture key": released without pressing any other key → assign that modifier by itself.
    private byte? _loneModifier;

    public KeymapView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        _topLevel?.AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _topLevel?.RemoveHandler(KeyDownEvent, OnPreviewKeyDown);
        _topLevel?.RemoveHandler(KeyUpEvent, OnPreviewKeyUp);
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    // "Capture key": swallow the next key (tunnel, before any control) and map it to a HID usage by physical position.
    // Ctrl / Shift / Alt / Win held → combo (left-side bits 01 02 04 08, as the vendor app writes them).
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not KeymapViewModel { IsCapturing: true } vm) return;
        e.Handled = true;
        if (PhysicalKeyMap.ToHidUsage(e.PhysicalKey) is not { } code) return;
        if (code is >= 0xE0 and <= 0xE7)
        {
            _loneModifier ??= (byte)(1 << (code - 0xE0));
            return;
        }
        _loneModifier = null;
        vm.HandleCapturedKey(code, ModifierMask(e.KeyModifiers));
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (DataContext is not KeymapViewModel { IsCapturing: true } vm) return;
        e.Handled = true;
        if (_loneModifier is { } mod && PhysicalKeyMap.ToHidUsage(e.PhysicalKey) is >= 0xE0 and <= 0xE7)
        {
            _loneModifier = null;
            vm.HandleCapturedModifier(mod);
        }
    }

    private static byte ModifierMask(KeyModifiers m) => (byte)(
        (m.HasFlag(KeyModifiers.Control) ? KeymapActions.ModCtrl : 0) |
        (m.HasFlag(KeyModifiers.Shift) ? KeymapActions.ModShift : 0) |
        (m.HasFlag(KeyModifiers.Alt) ? KeymapActions.ModAlt : 0) |
        (m.HasFlag(KeyModifiers.Meta) ? KeymapActions.ModWin : 0));
}
