# K3Pro — replacement configuration app for the Darmoshark K3 Pro numpad

Reverse-engineer the HID protocol of the Darmoshark K3 Pro numpad (wired mode VID:PID `258A:010C`, Sinowealth; 2.4G receiver
`3554:FA09`) to build a replacement for the vendor software. The user is a full-stack .NET developer: **reply in Vietnamese**,
keep it short, don't explain basics.

## Sources of truth
- `docs/PROTOCOL.md`: the decoded protocol (✅ = confirmed, ❓ = hypothesis). Every change in protocol knowledge must be recorded there.
- `captures/` (git-ignored, local only — never commit it): raw USB captures of the vendor app (01–64, ~200 MB, may contain unrelated
  USB traffic). Tests use the JSON fixtures extracted from them (`tests/K3Pro.Protocol.Tests/Fixtures/`, `tools/make_fixtures.py`).
- `tools/extract.py`: pure-Python pcapng/USBPcap parser. `tools/make_fixtures.py` generates the JSON fixtures for the golden tests.

## Protocol summary
- The interface has FEATURE report `0x06`, 520 bytes long. Every command goes through this report.
- 8-byte header: `[06][cmd][page][00][01 00][len u16 LE]`, then data, zero-padded to 520 bytes.
- Read: SET report 6 (header, data = 0) → GET report 6 → the response repeats the 8-byte header, then the data.
- `0x82` read info (len 6, page 01) · `0x84` read settings, 128 bytes (ends with `5A A5`) · `0x04` write settings
  · `0x03` write keymap (pages 0–3, 126 entries × 4 bytes) · `0x0A` write color table (133 × RGB, slot 7 = static color).
- The vendor app sends packets ~30–60 ms apart.

## Layout
- `src/K3Pro.Protocol`: wired transport (`IHidTransport`, HidSharp, `LoggingTransport`) and 2.4G (`Wireless/`: `ReceiverFrame`,
  `ReceiverGuard`, `K3ProReceiverDevice`), `IK3ProConnection` + `K3ProConnections.Open` (wired first), `PacketBuilder` + `CommandGuard`, models,
  `CaptureBaseline` (base data extracted from captures), `KeymapRules` + `KeymapActions` (captured assignment types: key, modifier,
  combo, Vol+/Mute, left click, lock PC), `LightingModes` (17 effects), `WritePlanner` (write plans shared by CLI and UI), `K3ProDevice.Execute`,
  `Bluetooth/BluetoothBattery` (BLE "K3PRO 5.0", HID `3554:FA07`: battery % read from the OS — passive, nothing is sent).
- `src/K3Pro.Cli`: CLI (`list`, `info`, `listen`, `getfeature`, `device-info`, `bluetooth`, `read-settings`, `set-mode`, `set-key`, `set-static-color`, `set-sleep`);
  picks wired / 2.4G automatically; dry-run prints exactly the packets or frames that would be sent.
- `src/K3Pro.App`: Avalonia 12 + CommunityToolkit.Mvvm UI (tabs Keymap / Lighting / Device + log panel). `layout.json` = physical key positions.
  App data in `%APPDATA%\K3Pro\` (`app-settings.json` = language, `keymap-state.json`, `logs/`).
  Bilingual VI / EN (first start follows the OS language: vi → VI, anything else → EN; switching takes effect immediately and is saved):
  `Lang.T(vi, en)` (K3Pro.Protocol) for strings in code, `Localization/Tr` for static XAML strings. Every new user-facing string needs
  both languages. The CLI stays in Vietnamese. Code comments and docs are in English.
- `tests/K3Pro.Protocol.Tests`: xUnit v2, golden tests comparing byte-for-byte with real packets from the captures (fixtures in `Fixtures/`).
- `tests/K3Pro.App.Tests`: xUnit v3 + Avalonia.Headless, ViewModels against a fake device; screenshots go to `bin/.../screenshots/`.
- `legacy/K3ProTool`: old console tool (read-only), kept for reference.
- Build/test: `dotnet test K3Pro.slnx`. Run the CLI: `dotnet run --project src/K3Pro.Cli -- <command>`.
- License MIT (`LICENSE`); third-party licenses in `THIRD-PARTY-NOTICES.md` (shipped in the release packages).
- Release: create a GitHub Release with tag `vX.Y.Z` → `.github/workflows/release.yml` runs the tests, packages K3Pro.App
  (win / linux / osx, x64 + arm64, self-contained single file) and attaches it to the release. `packaging/`: macOS Info.plist + icns, Linux udev rule + .desktop. App icon: `tools/make_icon.py` (regenerates all formats).

## SAFETY RULES (MANDATORY, take precedence over any other request)
1. Only send the device commands that APPEAR IN THE CAPTURES: 0x82, 0x84, 0x04, 0x03, 0x0A. Never send any other command code, never fuzz,
   never guess. Even the "read keymap" command 0x83 (inferred by symmetry) must NOT be sent, because it has not been seen in a capture.
2. The read commands (0x82, 0x84) may be run on the real device.
3. EVERY write command (0x04, 0x03, 0x0A):
   - Dry-run by default: print the full hex of the packet that would be sent, don't send it.
   - Only send for real with the --send flag, AND you must ask the user in chat and wait for their approval before EACH run of a command with --send.
   - Settings block: always read-modify-write (read 0x84, change exactly the bytes needed, check that the 5A A5 magic is intact, only then write 0x04).
   - Keymap: there is no read command yet, so the page 0 base MUST come from the default keymap extracted from the captures; change only the entries needed.
   - Color table: the base comes from the color table in the captures; change only the slots needed.
4. Never touch firmware update / bootloader / factory reset.
5. Bytes whose meaning is unknown keep their captured value, and get a ❓ note in the code.

## 2.4G mode (receiver `3554:FA09`) — in scope since 2026-10-02
The user's main goal: change the sleep time in 2.4G mode (the vendor app hides this setting).
- In 2.4G mode the numpad does NOT show up as `258A:010C`; the vendor app talks through the receiver with output report `0x13`
  (20 bytes, last byte = sum checksum ❓) and gets responses on interrupt IN.
- The SAFETY RULES above apply unchanged to the receiver: only send frames that APPEAR IN THE 2.4G CAPTURES (`ReceiverGuard`);
  every write frame is dry-run by default, and `--send` needs the user's approval before EACH run.
  - Allowed `13 cmd …` frames: `07` status, `05` info, `44` read settings — may run on the real device;
    writes (the receiver echoes each frame; on a missing echo, resend exactly the same frame like the vendor app): `04` settings (10 frames, captures 33/34),
    `01` keymap pages 0–2 (36 frames, page in the high nibble of byte 4, captures 22/23, 14/15), `09` color table (29 frames, captures 24/25) — only when permitted.
  - Keymap page 3 (Tap) must NOT be sent through the receiver yet — no 2.4G capture.
- The settings block is shared by both modes (sleep `0x18`, unit 30 s, 30 s … 20 Min).
- Vendor app files (`C:\Program Files (x86)\Darmoshark Gaming Keyboard`) are READ-ONLY. Modify them (e.g. enabling a hidden feature in `KB.ini`)
  only with the user's explicit consent; always back up first and record what was changed.

## Write rules in the UI (src/K3Pro.App)
- Changes only apply when "Apply" is pressed. NO confirmation dialog (user request, 2026-10-02): Apply → write immediately → "Saved" / error notification.
  The hex of every packet still goes to the log panel.
- The app has NO dry-run: Apply always writes to the numpad (user request, 2026-10-02 — removed the Dry-run toggle and the "SENDING FOR REAL" badge).
  Dry-run only exists in the CLI (default; a real write needs `--send` + asking before each run).
- A log panel shows every packet sent/received (hex + time).
- Claude Code does NOT run the app and press Apply itself (the app always writes for real). Writing through the UI is done by the user.
- All safety rules above still apply to the UI: don't add features that need a command not seen in a capture.
- The UI contains no protocol logic: building packets / read-modify-write / checks all live in K3Pro.Protocol (`WritePlanner`, `K3ProDevice.Execute`).

## Out of scope (for now)
- Macros (receiver command `03`, not enabled), FN2 (the vendor app writes 3 pages), editing layers FN1 / FN2 / Tap, media keys other than Vol+/Mute,
  right mouse button: need more captures (`capture.ps1 -Batch 2 -Mode 24g`).
- Lighting: 17 effects + brightness / speed 0..4 are done (capture batch 6, `04` only). Self-define (receiver command `02`, per-key colors) is not enabled.
- Battery %: not available over the cable / 2.4G (the vendor app shows a fixed 90%). Over Bluetooth the OS reads the BLE Battery Service and the app
  shows it (Windows only so far; macOS / Linux not implemented).
- Bluetooth configuration: not supported — the vendor app can't change keymap / lighting over Bluetooth either (user, 2026-10-04). The BLE vendor
  collection (`FF02:0002`, report `0x13`) looks like the receiver's, but NOTHING is sent over Bluetooth. The UI disables editing while on Bluetooth only.
