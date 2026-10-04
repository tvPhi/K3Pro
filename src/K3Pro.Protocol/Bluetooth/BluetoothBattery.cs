using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using HidSharp;

namespace K3Pro.Protocol.Bluetooth;

/// <summary>What the OS knows about the numpad's Bluetooth LE link. <see cref="BatteryPercent"/> null = unknown.</summary>
public sealed record BluetoothStatus(bool Connected, byte? BatteryPercent, string? Address)
{
    public static readonly BluetoothStatus None = new(false, null, null);
}

/// <summary>
/// Bluetooth mode ("K3PRO 5.0", BLE HID-over-GATT, HID <c>3554:FA07</c>): the battery level comes from the standard BLE Battery
/// Service, which the OS reads by itself. This class only queries the OS — <b>nothing is sent to the numpad</b>.
/// Windows: device property <c>DEVPKEY_Bluetooth_Battery</c> on the <c>BTHLE\DEV_&lt;address&gt;</c> node (docs/PROTOCOL.md, Bluetooth).
/// Other platforms: not implemented yet → <see cref="BluetoothStatus.None"/>.
/// </summary>
public static partial class BluetoothBattery
{
    /// <summary>HID VID / PID of the numpad over Bluetooth (VID source 02 = USB-IF; same vendor ID as the 2.4G receiver).</summary>
    public const int VendorId = 0x3554;
    public const int ProductId = 0xFA07;

    /// <summary>BLE device address (12 hex digits) from a Windows HID-over-GATT device path, e.g.
    /// <c>…_dev_vid&amp;023554_pid&amp;fa07_rev&amp;6701_eb968a1cc4a9&amp;col02#…</c> → <c>EB968A1CC4A9</c>.</summary>
    public static string? AddressFromHidPath(string devicePath) =>
        AddressRegex().Match(devicePath) is { Success: true } m ? m.Groups[1].Value.ToUpperInvariant() : null;

    [GeneratedRegex(@"_rev&[0-9a-f]{4}_([0-9a-f]{12})(?=&col|#|\\|$)", RegexOptions.IgnoreCase)]
    private static partial Regex AddressRegex();

    /// <summary>Current status; never throws (OS query failures → <see cref="BluetoothStatus.None"/>).</summary>
    public static BluetoothStatus Read()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return BluetoothStatus.None;
            // The BLE HID collections only exist while the numpad is paired with this PC.
            var address = DeviceList.Local.GetHidDevices(VendorId, ProductId)
                .Select(d => AddressFromHidPath(d.DevicePath))
                .FirstOrDefault(a => a is not null);
            return address is null ? BluetoothStatus.None : ReadWindows(address);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException or InvalidOperationException)
        {
            return BluetoothStatus.None;
        }
    }

    [SupportedOSPlatform("windows")]
    private static BluetoothStatus ReadWindows(string address)
    {
        var instanceId = Win32.FindDeviceInstance("BTHLE", $@"BTHLE\DEV_{address}\");
        if (instanceId is null || Win32.LocateDevNode(instanceId) is not { } devInst) return new(false, null, address);
        // {83DA6326-97A6-4088-9453-A1923F573B29} 15: true while the BLE link is up ❓ (observed: true when connected).
        bool connected = Win32.GetBool(devInst, Win32.IsConnectedKey) ?? true;
        byte? battery = connected ? Win32.GetByte(devInst, Win32.BatteryKey) is { } b && b <= 100 ? b : null : null;
        return new(connected, battery, address);
    }

    [SupportedOSPlatform("windows")]
    private static class Win32
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct DevPropKey(Guid fmtid, uint pid)
        {
            public Guid FmtId = fmtid;
            public uint Pid = pid;
        }

        public static readonly DevPropKey BatteryKey = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);
        public static readonly DevPropKey IsConnectedKey = new(new Guid("83DA6326-97A6-4088-9453-A1923F573B29"), 15);

        private const int CrSuccess = 0;
        private const uint FilterEnumerator = 0x1, FilterPresent = 0x100;
        private const uint PropTypeByte = 0x3, PropTypeBoolean = 0x11;

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_Device_ID_ListW(string filter, char[] buffer, uint length, uint flags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_DevNode_PropertyW(uint devInst, ref DevPropKey key, out uint type, byte[]? buffer, ref uint size, uint flags);

        /// <summary>First present device instance of <paramref name="enumerator"/> whose ID starts with <paramref name="prefix"/>.</summary>
        public static string? FindDeviceInstance(string enumerator, string prefix)
        {
            const uint flags = FilterEnumerator | FilterPresent;
            if (CM_Get_Device_ID_List_SizeW(out var length, enumerator, flags) != CrSuccess || length == 0) return null;
            var buffer = new char[length];
            if (CM_Get_Device_ID_ListW(enumerator, buffer, length, flags) != CrSuccess) return null;
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(id => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        public static uint? LocateDevNode(string instanceId) =>
            CM_Locate_DevNodeW(out var devInst, instanceId, 0) == CrSuccess ? devInst : null;

        public static byte? GetByte(uint devInst, DevPropKey key) =>
            Get(devInst, key, PropTypeByte) is [var b] ? b : null;

        public static bool? GetBool(uint devInst, DevPropKey key) =>
            Get(devInst, key, PropTypeBoolean) is [var b] ? b != 0 : null;

        private static byte[]? Get(uint devInst, DevPropKey key, uint expectedType)
        {
            uint size = 0;
            CM_Get_DevNode_PropertyW(devInst, ref key, out _, null, ref size, 0);
            if (size == 0) return null;
            var buffer = new byte[size];
            return CM_Get_DevNode_PropertyW(devInst, ref key, out var type, buffer, ref size, 0) == CrSuccess && type == expectedType
                ? buffer
                : null;
        }
    }
}
