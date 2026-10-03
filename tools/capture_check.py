"""Counts WRITE packets in a capture (reads the file only) — used by capture.ps1 to tell whether the last step was saved in time.

  python tools/capture_check.py captures/10-num1-ctrl-c.pcapng

Prints a one-line summary; exit code 0 if there are write packets, 2 if none, 1 if the file can't be read.
Wired: SET feature 0x06 command 03 (keymap) / 04 (settings) / 0A (colors).
2.4G:  SET output 0x13 command 01 (keymap) / 04 (settings) / 09 (colors).
"""
import collections, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from extract import feature_events
import k3cap

WIRED_WRITES = {0x03: 'keymap', 0x04: 'settings', 0x0A: 'colors'}
WIRELESS_WRITES = {0x01: 'keymap', 0x04: 'settings', 0x09: 'colors'}


def main(path):
    wired = collections.Counter()
    for e in feature_events(path):
        if e['dir'] == 'SET' and e['rtype'] == 3 and e['rid'] == 0x06 and len(e['data']) > 1 and e['data'][1] in WIRED_WRITES:
            wired[WIRED_WRITES[e['data'][1]]] += 1

    wireless = collections.Counter()
    try:
        evs, _ = k3cap.extract(path, ids=(0x13,), int_ids=(), quiet=True)
        for e in evs:
            p = e.payload
            if e.kind == 'ctl' and e.op == 'SET' and len(p) == 20 and p[1] in WIRELESS_WRITES:
                page = p[4] >> 4
                key = WIRELESS_WRITES[p[1]] + (f' page {page}' if p[1] == 0x01 and page else '')
                wireless[key] += 1
    except SystemExit:
        pass  # no receiver in the capture

    parts = [f'wired: {", ".join(f"{k} ×{v}" for k, v in wired.items())}' if wired else None,
             f'2.4G: {", ".join(f"{k} ×{v} frames" for k, v in wireless.items())}' if wireless else None]
    parts = [p for p in parts if p]
    print('Write packets — ' + ('; '.join(parts) if parts else 'NONE'))
    return 0 if parts else 2


if __name__ == '__main__':
    try:
        sys.exit(main(sys.argv[1]))
    except Exception as ex:  # corrupt / unreadable file
        print(f'Cannot read capture: {ex}')
        sys.exit(1)
