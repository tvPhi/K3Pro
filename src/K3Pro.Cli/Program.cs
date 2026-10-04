using System.Text;
using K3Pro.Cli;
using K3Pro.Protocol;

Console.OutputEncoding = Encoding.UTF8;

try
{
    var a = new CliArgs(args);
    return a.Command switch
    {
        "list" => Run(() => LegacyCommands.List(vendorOnly: a.Has("--vendor")), a, "--vendor"),
        "info" => Run(() => LegacyCommands.Info(HexArg(a, 0, "vid"), HexArg(a, 1, "pid")), a),
        "listen" => Run(() => LegacyCommands.Listen(HexArg(a, 0, "vid"), HexArg(a, 1, "pid")), a),
        "getfeature" => Run(() => LegacyCommands.GetFeature(HexArg(a, 0, "vid"), HexArg(a, 1, "pid"), HexArg(a, 2, "id")), a),
        "device-info" => Run(DeviceCommands.DeviceInfoCmd, a),
        "bluetooth" => Run(DeviceCommands.BluetoothCmd, a),
        "read-settings" => Run(DeviceCommands.ReadSettingsCmd, a),
        "set-mode" => Run(() => DeviceCommands.SetMode(a), a, "--send"),
        "set-key" => Run(() => DeviceCommands.SetKey(a), a, "--send"),
        "set-static-color" => Run(() => DeviceCommands.SetStaticColor(a), a, "--send"),
        "set-sleep" => Run(() => DeviceCommands.SetSleep(a), a, "--send"),
        _ => Help(),
    };
}
catch (UsageException ex)
{
    Console.Error.WriteLine($"Lỗi tham số: {ex.Message}\n");
    Help();
    return 2;
}
catch (Exception ex) when (ex is IOException or TimeoutException or InvalidDataException or UnsafeCommandException or FormatException
                               or InvalidOperationException or ArgumentException)
{
    Console.Error.WriteLine($"Lỗi: {ex.Message}");
    return 1;
}

static int Run(Func<int> command, CliArgs a, params string[] allowedFlags)
{
    a.EnsureOnly(allowedFlags);
    return command();
}

static int HexArg(CliArgs a, int i, string name) => CliArgs.ParseHex(a.Arg(i, name), name);

static int Help()
{
    Console.WriteLine("""
        k3pro — Darmoshark K3 Pro (cắm dây 258A:010C, hoặc 2.4G qua receiver 3554:FA09)

        Khám phá (chỉ đọc, như K3ProTool cũ):
          list [--vendor]                      Liệt kê HID device
          info <vid> <pid>                     Report descriptor + danh sách report
          listen <vid> <pid>                   In input report nhận được
          getfeature <vid> <pid> <id>          GET_REPORT một feature report

        Đọc (chạy thẳng trên thiết bị):
          device-info                          0x82
          bluetooth                            Chế độ Bluetooth: % pin hệ điều hành đọc từ BLE Battery Service (không gửi gì)
          read-settings                        0x84, in hex + field đã biết

        Ghi (mặc định DRY-RUN: in đủ hex gói, không gửi; --send để gửi thật):
          set-mode <n> [--send]                0x84 → sửa offset 0x0A → 0x04. n: 0 OFF, 1 Fixed_on, 2 Respire, 3 Rainbow …
                                               (17 hiệu ứng, bảng trong docs/PROTOCOL.md; Self-define 0x15 không hỗ trợ)
          set-key <index> <hidcode> [--page 0] [--send]
                                               0x03. index thập phân 0–23, hidcode hex (04 = A)
          set-static-color <RRGGBB> [--send]   0x84 → (0x04 mode 01 nếu cần) → 0x0A slot 7
          set-sleep <thời gian> [--send]       0x84 → sửa offset 0x18 → 0x04. Nấc app hãng: 30s 1m 1.5m 2m 3m 4m 5m 10m 15m 20m
                                               Sleep ở chế độ 2.4G; block dùng chung nên ghi qua dây là đủ

        Số: <vid> <pid> <id> <hidcode> luôn hex; <n> <index> --page thập phân hoặc 0x...
        """);
    return 2;
}
