# capture.ps1 — Semi-automated USB capture for the K3 Pro (run PowerShell as Administrator)
# Usage:  powershell -ExecutionPolicy Bypass -File .\capture.ps1 [-Batch 1|2|3|4|5|6] [-Only 10] [-Iface USBPcap1] [-Seconds 0]
#   Default: press Enter to start recording, press Enter again when done to stop. -Seconds N = stop automatically after N seconds.
#   -Batch 1 : the 5 original captures (01–05), wired mode
#   -Batch 2 : keymap round 2 (06–18, 17b–17f knob / Fn), wired mode — each capture is ONE action, then Save
#   -Batch 3 : 2.4G mode (21–25) — repeats round 1 through receiver 3554:FA09
#   -Batch 4 : Sleep (31–34): min 30 s / max 20 Min, wired then 2.4G — needs ShowPower=1 in the vendor app's KB.ini
#   -Batch 6 : Light effect (41–64): one capture per effect + Respire brightness / speed. Wired or 2.4G both work,
#              keep the vendor app OPEN on the Light effect page for the whole batch.
#   -Batch 5 : battery % (35–37): the vendor app shows Battery — find the frame holding the battery %. Needs ShowPower=1. The % you enter goes to captures/battery-notes.txt
#   -Only    : run only the steps whose name starts with this string (e.g. -Only 10 to redo 10-num1-ctrl-c)
#   -Mode    : wired (default) | 24g — run batch 2 over 2.4G: file names get "-24g" (e.g. 06-24g-fn1-num1-A)
param(
    [ValidateSet(1, 2, 3, 4, 5, 6)]
    [int]$Batch = 2,
    [string]$Only = "",
    [ValidateSet("wired", "24g")]
    [string]$Mode = "wired",
    [string]$Iface = "",
    [int]$Seconds = 0,          # 0 = stop with Enter
    [string]$OutDir = (Join-Path $PSScriptRoot "captures")
)

$tshark = "C:\Program Files\Wireshark\tshark.exe"
if (-not (Test-Path $tshark)) { throw "tshark not found at: $tshark" }

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Warning "Not running as Administrator — USBPcap may not be able to record."
}

if (-not $Iface) {
    Write-Host "USBPcap interfaces:"
    & $tshark -D | Select-String "USBPcap"
    $Iface = Read-Host "Enter the interface the numpad is on (e.g. USBPcap1)"
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$batches = @{
    1 = @(
        @{ Name = "01-open-close"; Task = "Open the Darmoshark app, wait until it has loaded, do NOTHING, then close the app." },
        @{ Name = "02-num1-to-A";  Task = "Open the app, remap Num1 -> A, click Apply/Save, then close the app." },
        @{ Name = "03-num1-to-B";  Task = "Open the app, remap Num1 -> B, click Apply/Save, then close the app." },
        @{ Name = "04-rgb-red";    Task = "Open the app, set RGB to static red, click Apply/Save, then close the app." },
        @{ Name = "05-rgb-blue";   Task = "Open the app, set RGB to static blue, click Apply/Save, then close the app." }
    )
    2 = @(
        @{ Name = "06-fn1-num1-A";       Task = "Tab FN1: Num1 -> A (Keyboard). Save." },
        @{ Name = "07-fn2-num1-A";       Task = "Tab FN2: Num1 -> A (Keyboard). Save." },
        @{ Name = "08-tap-num1-A";       Task = "Tab Tap: Num1 -> A (Keyboard). Save." },
        @{ Name = "09-num1-lctrl";       Task = "Tab Default: Num1 -> LCtrl (Keyboard > Modify). Save." },
        @{ Name = "10-num1-ctrl-c";      Task = "Tab Default: Num1 -> Key combination Ctrl + C. Save." },
        @{ Name = "11-num1-shift-alt-a"; Task = "Tab Default: Num1 -> Key combination Shift + Alt + A. Save." },
        @{ Name = "12-num1-volup";       Task = "Tab Default: Num1 -> Multimedia Vol+. Save." },
        @{ Name = "13-num1-mouse-left";  Task = "Tab Default: Num1 -> Mouse left button. Save." },
        @{ Name = "14-num1-fn2";         Task = "Tab Default: Num1 -> FN2 (Keyboard). Save." },
        @{ Name = "15-num1-cmd-lock";    Task = "Tab Default: Num1 -> Commands lock PC (padlock icon). Save." },
        @{ Name = "16-num1-macro";       Task = "Create macro 'A, B' (Macro > Click to create), assign it to Num1 (Default). Save." },
        @{ Name = "16a-macro-create";    Task = "ONLY create macro 'A, B': Macro > Click to create > name it AB > Record, type A then B > Stop > OK. If there is a save-macro button, click it. Do NOT assign it to a key." },
        @{ Name = "16b-macro-assign";    Task = "Assign macro AB (created in 16a) to Num1: Default > key 1 > Macro tab > pick AB, Cycle times 1 > Save 💾." },
        @{ Name = "17-knob-left-A";      Task = "Tab Default: select the knob's ROTATE LEFT (area left of the knob), assign -> A (Keyboard). Save." },
        @{ Name = "17b-knob-right-B";    Task = "Tab Default: select the knob's ROTATE RIGHT (area right of the knob), assign -> B (Keyboard). Save." },
        @{ Name = "17c-knob-press-C";    Task = "Tab Default: select knob PRESS (center of the knob), assign -> C (Keyboard). Save." },
        @{ Name = "17d-knob-reset";      Task = "Tab Default: reset the knob's 3 areas to default (the KEY's 'Reset Key' / ⟳ button — NOT the device Reset in Global). Save. Turn the knob: does it still change the volume?" },
        @{ Name = "17e-fn-to-D";         Task = "(Optional) Tab Default: Fn key (right custom key) -> D. Save. Then do 17f right away." },
        @{ Name = "17f-fn-reset";        Task = "(Optional) Tab Default: reset the Fn key to default (the key's Reset Key, pick FN again). Save. Check that Fn + key still works." },
        @{ Name = "18-leftkey-A";        Task = "Tab Default: top-left custom key -> A. Save." }
    )
    3 = @(
        @{ Name = "21-24g-open-close";   Task = "[2.4G] Open the app, wait until it has loaded, do NOTHING, then close the app." },
        @{ Name = "22-24g-num1-to-A";    Task = "[2.4G] Open the app, remap Num1 -> A (Default), Save, close the app." },
        @{ Name = "23-24g-num1-to-B";    Task = "[2.4G] Open the app, remap Num1 -> B (Default), Save, close the app." },
        @{ Name = "24-24g-rgb-red";      Task = "[2.4G] Open the app, set RGB to static red, Save, close the app." },
        @{ Name = "25-24g-rgb-blue";     Task = "[2.4G] Open the app, set RGB to static blue, Save, close the app." }
    )
    4 = @(
        @{ Name = "31-wired-sleep-30s";   Task = "[Wired] Global > Sleep: ON, drag to MIN (30 s). Save. Close the app. Do NOT click Reset." },
        @{ Name = "32-wired-sleep-20min"; Task = "[Wired] Global > Sleep: ON, drag to MAX (20 Min). Save. Close the app." },
        @{ Name = "33-24g-sleep-30s";     Task = "[2.4G] Global > Sleep: ON, drag to MIN (30 s). Save. Close the app." },
        @{ Name = "34-24g-sleep-20min";   Task = "[2.4G] Global > Sleep: ON, drag to MAX (20 Min). Save. Close the app." }
    )
    5 = @(
        @{ Name = "35-24g-battery";   Task = "[2.4G] Open the vendor app, open the place that shows Battery % (Device Info), wait ~10 seconds for the % to appear, REMEMBER the %, close the app. Do NOT Save anything." },
        @{ Name = "36-24g-battery-2"; Task = "(Not needed: the vendor app always shows a fixed 90%, see docs/PROTOCOL.md) [2.4G] repeat 35 exactly, remember the %." },
        @{ Name = "37-wired-battery"; Task = "(Optional) [Wired] Open the vendor app, check whether Device Info shows Battery %, wait ~10 seconds, remember the % (or 'not shown'), close the app." }
    )
    6 = @(
        @{ Name = "42-led-respire"; Task = "Light effect > Respire. Save if there is a button (don't change color / brightness / speed)." },
        @{ Name = "43-led-rainbow"; Task = "Light effect > Rainbow. Save if there is a button (don't change color / brightness / speed)." },
        @{ Name = "44-led-flash-away"; Task = "Light effect > Flash_away. Save if there is a button (don't change color / brightness / speed)." },
        @{ Name = "45-led-raindrops"; Task = "Light effect > Raindrops. Save if there is a button (don't change color / brightness / speed)." },
        @{ Name = "46-led-rainbow-wheel"; Task = "Light effect > Rainbow_wheel. Save if there is a button (don't change color / brightness / speed)." },
        @{ Name = "47-led-ripples-shining"; Task = "Light effect > Ripples_shining. Save if there is a button (don't change color / brightness / speed)." },
        @{ Name = "48-led-stars-twinkle"; Task = "Light effect > Stars_twinkle. Save if there is a button (don't change color / brightness / speed)." },
        @{ Name = "49-led-shadow-disappear"; Task = "Light effect > Shadow_disappear. Save if there is a button (don't change color / brightness / speed)." },
        @{ Name = "50-led-retro-snake"; Task = "Light effect > Retro_snake. Save if there is a button (don't change color / brightness / speed)." },
        @{ Name = "51-led-neon-stream"; Task = "Light effect > Neon_stream. Save if there is a button (don't change color / brightness / speed)." },
        @{ Name = "52-led-reaction"; Task = "Light effect > Reaction. Save if there is a button (don't change color / brightness / speed)." },
        @{ Name = "53-led-sine-wave"; Task = "Light effect > Sine_wave. Save if there is a button (don't change color / brightness / speed)." },
        @{ Name = "54-led-rotating-windmill"; Task = "Light effect > Rotating windmill. Save if there is a button (don't change color / brightness / speed)." },
        @{ Name = "55-led-colorful-waterfall"; Task = "Light effect > Colorful waterfall. Save if there is a button (don't change color / brightness / speed)." },
        @{ Name = "56-led-blossoming"; Task = "Light effect > Blossoming. Save if there is a button (don't change color / brightness / speed)." },
        @{ Name = "57-led-self-define";      Task = "(Optional) Light effect > Self-define. Do NOT paint any key. Save if there is a button." },
        @{ Name = "58-led-off";              Task = "Light effect > OFF. Save if there is a button." },
        @{ Name = "61-led-respire-bright-min"; Task = "Light effect > Respire, drag BRIGHTNESS to MIN. Save if there is a button." },
        @{ Name = "62-led-respire-bright-max"; Task = "Still Respire, drag BRIGHTNESS to MAX. Save if there is a button." },
        @{ Name = "63-led-respire-speed-min";  Task = "Still Respire, drag SPEED to MIN. Save if there is a button." },
        @{ Name = "64-led-respire-speed-max";  Task = "Still Respire, drag SPEED to MAX. Save if there is a button." },
        @{ Name = "41-led-fixed-on";         Task = "Light effect > Fixed_on (keeps the current color) — puts the LEDs back to static. Save if there is a button." }
    )
}

$steps = $batches[$Batch] | Where-Object { -not $Only -or $_.Name.StartsWith($Only) }
if ($Mode -eq "24g" -and $Batch -eq 2) {
    # 06-fn1-num1-A -> 06-24g-fn1-num1-A
    $steps = $steps | ForEach-Object { @{ Name = ($_.Name -replace '^(\w+?)-', '$1-24g-'); Task = "[2.4G] " + $_.Task } }
}
if (-not $steps) { throw "No step matches -Only '$Only' in batch $Batch." }

# Report 5/6: wired numpad; 0x13: 2.4G receiver (output report + interrupt IN responses)
$filter = "usbhid.setup.ReportID == 5 || usbhid.setup.ReportID == 6 || usbhid.setup.ReportID == 0x13 || (usb.idVendor == 0x3554)"

Write-Host "Batch $Batch — $(@($steps).Count) steps, $(if ($Seconds -gt 0) { "each step records $Seconds seconds" } else { "each step: Enter to start, Enter when done" })." -ForegroundColor Cyan
if ($Batch -eq 6) {
    Write-Host "Open the vendor app, go to the Light effect page and keep it OPEN for the whole batch. Each step: Enter -> pick exactly ONE effect (Save if available) -> Enter." -ForegroundColor Cyan
    Write-Host "Wired or 2.4G both work (2.4G: press a key to wake the numpad before each step)." -ForegroundColor Yellow
}
elseif ($Batch -ge 2) {
    Write-Host "Each step: open the app -> do EXACTLY one action -> Save -> close the app." -ForegroundColor Cyan
    Write-Host "Do NOT click the ⟳ (reset) button while capturing." -ForegroundColor Yellow
}
if ($Batch -in 3, 4, 5 -or $Mode -eq "24g") {
    Write-Host "2.4G: pick the USBPcap interface the RECEIVER is on (may differ from the wired one)." -ForegroundColor Yellow
    Write-Host "Before each step press a key on the numpad to wake it (in 2.4G it falls asleep very quickly)." -ForegroundColor Yellow
}

$python = (Get-Command python -ErrorAction SilentlyContinue).Source
$checker = Join-Path $PSScriptRoot "tools\capture_check.py"

foreach ($s in $steps) {
    $file = Join-Path $OutDir "$($s.Name).pcapng"
    $expectWrite = $s.Name -notmatch 'open-close|battery'   # steps that only open the app / check the battery: no write packets is expected
    $attempt = 0
    while ($true) {
        $attempt++
        Write-Host "`n=== $($s.Name)$(if ($attempt -gt 1) { " (attempt $attempt)" }) ===" -ForegroundColor Cyan
        Write-Host "To do: $($s.Task)"
        if (Test-Path $file) { Write-Warning "$file already exists — it will be overwritten." }
        if ($s.Name -match '24g') { Write-Host "The numpad must be in 2.4G mode, cable unplugged (if it is plugged in just to charge, make a note of it)." -ForegroundColor Yellow }
        elseif ($Batch -in 4, 5) { Write-Host "The numpad must be in WIRED mode." -ForegroundColor Yellow }
        if ($Batch -eq 6) { Write-Host "Keep the vendor app OPEN on the Light effect page. Press Enter to start recording..." }
        else { Write-Host "The vendor app must be CLOSED. Press Enter to start recording..." }
        [void](Read-Host)

        if ($Seconds -gt 0) {
            Write-Host "Recording for $Seconds seconds... do the action now. Don't type anything sensitive on other keyboards." -ForegroundColor Yellow
            & $tshark -i $Iface -a "duration:$Seconds" -w $file -q
        }
        else {
            # Record until the user presses Enter (safety net: stops on its own after 10 minutes)
            $proc = Start-Process -FilePath $tshark -ArgumentList @("-i", $Iface, "-a", "duration:600", "-w", "`"$file`"", "-q") -PassThru -NoNewWindow
            Start-Sleep -Milliseconds 800
            Write-Host "Recording... do the action (open app, Save, close app). Don't type anything sensitive on other keyboards." -ForegroundColor Yellow
            [void](Read-Host "When DONE, press Enter to stop recording")
            Start-Sleep -Milliseconds 1500   # give the last packets time to reach the file
            if (-not $proc.HasExited) { & taskkill /PID $proc.Id /T /F 2>$null | Out-Null }
            $proc.WaitForExit()
        }

        $count = (& $tshark -r $file -Y $filter 2>$null | Measure-Object).Count   # a file stopped by hand may be truncated at the end — that is normal
        Write-Host "Saved: $file  ($count report 5/6/0x13 packets)" -ForegroundColor Green

        # Check for WRITE packets (not saved in time → none) — tools/capture_check.py, reads the file only
        $hasWrite = $null
        if ($python -and (Test-Path $checker)) {
            $env:PYTHONIOENCODING = "utf-8"
            & $python $checker $file
            $hasWrite = ($LASTEXITCODE -eq 0)
        }
        elseif ($count -eq 0) { $hasWrite = $false }

        if ($expectWrite -and $hasWrite -eq $false) {
            Write-Warning "No write packets found — recording may have stopped before Save finished (or the vendor app does not send this action)."
            $ans = Read-Host "Enter = redo this step  ·  s = skip"
            if ($ans -ne 's') { continue }
            break
        }
        $ans = Read-Host "Enter = next step  ·  r = redo this step"
        if ($ans -eq 'r') { continue }
        if ($s.Name -match 'battery') {
            # The % the vendor app shows — needed to locate the battery byte in the capture
            $pct = Read-Host "What battery % does the vendor app show? (e.g. 90, or 'not shown')"
            Add-Content -Path (Join-Path $OutDir "battery-notes.txt") -Value "$($s.Name)`t$pct`t$(Get-Date -Format s)" -Encoding utf8
        }
        break
    }
}

Write-Host "`nDone. The files are in: $OutDir"
if ($Batch -eq 6) { Write-Host "The last step (41) put the LEDs back to Fixed_on. To restore your color: K3Pro.App > Lighting > Apply." }
if ($Batch -eq 2) { Write-Host "Remember to set Num1 back to 1 (vendor app or K3Pro.App > Reset to default) for normal use." }
