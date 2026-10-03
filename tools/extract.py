import struct, sys, collections

def read_pcapng(path):
    data = open(path,'rb').read()
    off = 0; endian = '<'; linktypes = []
    while off + 12 <= len(data):
        btype = struct.unpack_from(endian+'I', data, off)[0]
        if btype == 0x0A0D0D0A:
            bom = data[off+8:off+12]
            endian = '<' if bom == b'\x4d\x3c\x2b\x1a' else '>'
        blen = struct.unpack_from(endian+'I', data, off+4)[0]
        if blen < 12 or off + blen > len(data):
            break  # truncated last block (capture stopped by hand) — skip it
        body = data[off+8:off+blen-4]
        if btype == 1:
            linktypes.append(struct.unpack_from(endian+'H', body, 0)[0])
        elif btype == 6:
            iid, th, tl, caplen, origlen = struct.unpack_from(endian+'IIIII', body, 0)
            ts = ((th<<32)|tl)/1e6
            yield iid, ts, body[20:20+caplen]
        off += blen

def parse_usbpcap(pkt):
    hlen, irp, status, func, info, bus, dev, ep, xfer, dlen = struct.unpack_from('<HQIHBHHBBI', pkt, 0)
    stage = pkt[27] if xfer == 2 and hlen >= 28 else None
    return dict(irp=irp, status=status, info=info, bus=bus, dev=dev, ep=ep, xfer=xfer,
                stage=stage, payload=pkt[hlen:hlen+dlen])

def feature_events(path):
    pending = {}
    events = []
    t0 = None
    for n,(iid, ts, pkt) in enumerate(read_pcapng(path), 1):
        if t0 is None: t0 = ts
        p = parse_usbpcap(pkt)
        if p['xfer'] != 2: continue
        if p['stage'] == 0 and len(p['payload']) >= 8:
            bm, br, wv, wi, wl = struct.unpack_from('<BBHHH', p['payload'], 0)
            if bm in (0x21, 0xA1) and br in (0x01, 0x09):
                rtype, rid = wv >> 8, wv & 0xFF
                ev = dict(frame=n, t=ts-t0, dev=p['dev'], dir='SET' if br==9 else 'GET',
                          rtype=rtype, rid=rid, iface=wi, wlen=wl, data=p['payload'][8:], resp=None, status=None)
                events.append(ev); pending[p['irp']] = ev
        elif p['stage'] == 3 and p['irp'] in pending:
            ev = pending.pop(p['irp'])
            ev['status'] = p['status']
            if ev['dir']=='GET': ev['resp'] = p['payload']
    return events

def trim(b):
    b = bytes(b); i = len(b)
    while i > 0 and b[i-1] == 0: i -= 1
    return b[:max(i,1)].hex(' ') + (f'  …(+{len(b)-i} zero)' if len(b)-i>0 else '')

if __name__ == '__main__':
    for path in sys.argv[1:]:
        evs = feature_events(path)
        print(f'\n##### {path.split("/")[-1]}  ({len(evs)} feature/report transfers)')
        for e in evs:
            body = e['data'] if e['dir']=='SET' else (e['resp'] or b'')
            st = '' if not e['status'] else f' status=0x{e["status"]:08X}'
            print(f"{e['frame']:>6} {e['t']:7.3f}s dev={e['dev']:<3} if={e['iface']} {e['dir']} type={e['rtype']} id=0x{e['rid']:02X} len={e['wlen']}{st}\n        {trim(body)}")
