# Reads 0x84 via the CLI (READ ONLY), saves a snapshot to captures/settings-snaps.json and diffs it with the previous one.  python tools/settings_snap.py "<label>"
import subprocess, sys, re, json, os
d = os.path.dirname(os.path.abspath(__file__))
hist = os.path.join(os.path.dirname(d), 'captures', 'settings-snaps.json')
out = subprocess.run(['dotnet', 'run', '--project', os.path.join(os.path.dirname(d), 'src', 'K3Pro.Cli'), '--no-build', '--', 'read-settings'],
                     capture_output=True, text=True, encoding='utf-8').stdout
rows = [l for l in out.splitlines() if re.match(r'^\s{2}00[0-7]0  ', l)]
data = bytes.fromhex(''.join(l.strip()[6:] for l in rows).replace(' ', ''))
assert len(data) == 128 and data[-2:] == b'\x5a\xa5', out
snaps = json.load(open(hist)) if os.path.exists(hist) else []
label = sys.argv[1] if len(sys.argv) > 1 else f'#{len(snaps)}'
if snaps:
    prev = bytes.fromhex(snaps[-1]['hex'])
    diff = [(i, prev[i], data[i]) for i in range(128) if prev[i] != data[i]]
    print(f'[{label}] vs [{snaps[-1]["label"]}]: ' + (', '.join(f'0x{i:02X}: {a:02X}→{b:02X}' for i, a, b in diff) or 'unchanged'))
else:
    print(f'[{label}] baseline: 0x0A = {data[0x0A]:02X}')
snaps.append({'label': label, 'hex': data.hex()})
json.dump(snaps, open(hist, 'w'), indent=1)
