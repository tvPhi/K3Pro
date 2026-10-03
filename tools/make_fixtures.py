"""Extracts feature report 0x06 transfers (SET/GET) from captures/*.pcapng into JSON fixtures for the golden tests.

  python tools/make_fixtures.py [captures_dir] [out_dir]

Default: captures/ -> tests/K3Pro.Protocol.Tests/Fixtures/. Capture files are only read.
"""
import json, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from extract import feature_events
import k3cap  # receiver 2.4G: SET output 0x13 + interrupt IN 0x13

REPORT_ID = 0x06
FEATURE = 3

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
cap_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(root, 'captures')
out_dir = sys.argv[2] if len(sys.argv) > 2 else os.path.join(root, 'tests', 'K3Pro.Protocol.Tests', 'Fixtures')
os.makedirs(out_dir, exist_ok=True)

for name in sorted(os.listdir(cap_dir)):
    if not name.endswith('.pcapng'):
        continue
    events = []
    for e in feature_events(os.path.join(cap_dir, name)):
        if e['rtype'] != FEATURE or e['rid'] != REPORT_ID:
            continue
        body = e['data'] if e['dir'] == 'SET' else (e['resp'] or b'')
        events.append(dict(frame=e['frame'], t=round(e['t'], 6), dir=e['dir'], iface=e['iface'],
                           wLength=e['wlen'], status=e['status'], length=len(body), hex=bytes(body).hex()))
    receiver = []
    try:
        revs, _ = k3cap.extract(os.path.join(cap_dir, name), ids=(0x13,), int_ids=(0x13,), quiet=True)
        receiver = [dict(frame=e.frame, dir='OUT' if e.kind == 'ctl' else 'IN', hex=e.payload.hex())
                    for e in revs if len(e.payload) == 20]
    except SystemExit:
        pass  # capture has no receiver
    out = os.path.join(out_dir, name.replace('.pcapng', '.json'))
    with open(out, 'w', encoding='utf-8') as f:
        json.dump(dict(source=f'captures/{name}', reportId=REPORT_ID, events=events, receiver=receiver), f, indent=1)
    print(f'{name}: {len(events)} event, {len(receiver)} receiver frames -> {os.path.relpath(out, root)}')
    for ev in events:
        print(f"   f{ev['frame']:<6} {ev['dir']} wLength={ev['wLength']} len={ev['length']} status={ev['status']} {ev['hex'][:16]}")
