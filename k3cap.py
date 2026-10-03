#!/usr/bin/env python3
"""
k3cap.py - Extract & diff HID reports (SET/GET feature 5/6, interrupt IN 0x03)
from USBPcap captures (.pcapng / .pcap). READS FILES ONLY, never touches the device.

No external libraries needed (no tshark/pyshark). Python 3.8+.

  python k3cap.py dump  cap1.pcapng [--brief] [--ids all] [--json cap1.json]
  python k3cap.py diff  cap2_A.pcapng cap3_B.pcapng
  python k3cap.py cmds  cap1.pcapng cap2_A.pcapng cap3_B.pcapng ...
  python k3cap.py find  "ff 00 00" cap4_red.pcapng cap5_blue.pcapng
  python k3cap.py cksum cap*.pcapng --rid 5

Offset convention: byte 0 = report ID (exactly as on the USB wire), byte 1 = data[0].
"""
from __future__ import annotations

import argparse
import difflib
import json
import os
import struct
import sys
from collections import Counter, OrderedDict
from dataclasses import dataclass, field
from typing import Optional

try:
    sys.stdout.reconfigure(encoding="utf-8")  # console Windows
except Exception:
    pass

LINKTYPE_USBPCAP = 249
STAGE_SETUP, STAGE_DATA, STAGE_STATUS, STAGE_COMPLETE = 0, 1, 2, 3
XFER_INT, XFER_CTRL = 1, 2

USBD_STATUS = {
    0x00000000: "OK",
    0xC0000004: "STALL",
    0xC0000005: "NORESP",
    0xC0000030: "HALTED",
    0xC0010000: "CANCEL",
}
HID_REQ = {0x01: "GET", 0x02: "GET_IDLE", 0x03: "GET_PROTO",
           0x09: "SET", 0x0A: "SET_IDLE", 0x0B: "SET_PROTO"}
RTYPE = {1: "IN", 2: "OUT", 3: "FEAT"}


# --------------------------------------------------------------------------- pcap/pcapng
def read_packets(path):
    """yield (frame_no, ts_seconds, linktype, bytes). frame_no matches the frame number in Wireshark."""
    with open(path, "rb") as f:
        buf = f.read()
    m = buf[:4]
    if m == b"\x0a\x0d\x0d\x0a":
        yield from _read_pcapng(buf)
    elif m in (b"\xd4\xc3\xb2\xa1", b"\x4d\x3c\xb2\xa1", b"\xa1\xb2\xc3\xd4", b"\xa1\xb2\x3c\x4d"):
        yield from _read_pcap(buf)
    else:
        raise ValueError(f"{path}: not a pcap/pcapng file")


def _read_pcap(buf):
    m = buf[:4]
    e = "<" if m in (b"\xd4\xc3\xb2\xa1", b"\x4d\x3c\xb2\xa1") else ">"
    nano = m in (b"\x4d\x3c\xb2\xa1", b"\xa1\xb2\x3c\x4d")
    lt = struct.unpack_from(e + "I", buf, 20)[0] & 0x0FFFFFFF
    off, frame = 24, 0
    while off + 16 <= len(buf):
        sec, frac, incl, _ = struct.unpack_from(e + "IIII", buf, off)
        off += 16
        frame += 1
        yield frame, sec + frac / (1e9 if nano else 1e6), lt, buf[off:off + incl]
        off += incl


def _if_tsresol(opts, e):
    i = 0
    while i + 4 <= len(opts):
        code, ln = struct.unpack_from(e + "HH", opts, i)
        if code == 0:
            break
        if code == 9 and ln >= 1:
            v = opts[i + 4]
            return float(2 ** (v & 0x7F)) if v & 0x80 else float(10 ** v)
        i += 4 + ((ln + 3) & ~3)
    return 1e6


def _read_pcapng(buf):
    off, frame, e = 0, 0, "<"
    ifaces, last_ts = [], 0.0
    while off + 12 <= len(buf):
        if buf[off:off + 4] == b"\x0a\x0d\x0d\x0a":  # SHB: determines endianness
            e = "<" if buf[off + 8:off + 12] == b"\x4d\x3c\x2b\x1a" else ">"
            ifaces = []
        btype, blen = struct.unpack_from(e + "II", buf, off)
        if blen < 12 or off + blen > len(buf):
            break
        body = buf[off + 8:off + blen - 4]
        if btype == 1:  # IDB
            ifaces.append((struct.unpack_from(e + "H", body, 0)[0], _if_tsresol(body[8:], e)))
        elif btype == 6:  # EPB
            iid, th, tl, cap, _ = struct.unpack_from(e + "IIIII", body, 0)
            lt, res = ifaces[iid]
            last_ts = ((th << 32) | tl) / res
            frame += 1
            yield frame, last_ts, lt, body[20:20 + cap]
        elif btype == 3:  # SPB
            orig = struct.unpack_from(e + "I", body, 0)[0]
            frame += 1
            yield frame, last_ts, ifaces[0][0], body[4:4 + orig]
        elif btype == 2:  # PB (obsolete)
            iid, _, th, tl, cap, _ = struct.unpack_from(e + "HHIIII", body, 0)
            lt, res = ifaces[iid]
            last_ts = ((th << 32) | tl) / res
            frame += 1
            yield frame, last_ts, lt, body[20:20 + cap]
        off += blen


# --------------------------------------------------------------------------- USBPcap
@dataclass
class UPkt:
    irp: int
    status: int
    completion: bool  # info bit0: 1 = PDO->FDO (from the device / completion)
    bus: int
    dev: int
    ep: int
    xfer: int
    stage: Optional[int]
    data: bytes


def parse_usbpcap(pkt) -> Optional[UPkt]:
    # USBPCAP_BUFFER_PACKET_HEADER (packed, 27 bytes) + 1 stage byte for control transfers
    if len(pkt) < 27:
        return None
    hlen, irp, status, _func, info, bus, dev, ep, xfer, dlen = struct.unpack_from("<HQIHBHHBBI", pkt, 0)
    stage = pkt[27] if (xfer == XFER_CTRL and hlen >= 28 and len(pkt) >= 28) else None
    return UPkt(irp, status, bool(info & 1), bus, dev, ep, xfer, stage, bytes(pkt[hlen:hlen + dlen]))


@dataclass
class Ev:
    kind: str           # "ctl" | "int"
    frame: int          # Wireshark frame (submit for ctl, completion for int)
    ts: float
    bus: int
    dev: int
    op: str             # SET/GET/... or "INT"
    rtype: int          # 1 IN, 2 OUT, 3 FEAT (0 for int)
    rid: int
    payload: bytes      # data on the wire (byte 0 = report ID if the device uses report IDs)
    status: Optional[int] = None
    wIndex: int = 0
    wLength: int = 0
    ep: int = 0
    end_frame: Optional[int] = None
    idx: int = 0        # sequence number after filtering (1-based)

    @property
    def tag(self):
        if self.kind == "int":
            return f"INT  ep{self.ep & 0x7F:x} {self.rid:02X}"
        return f"{self.op:<4} {RTYPE.get(self.rtype, '?'):<4} {self.rid:02X}"

    @property
    def status_str(self):
        if self.status is None:
            return "-" if self.kind == "int" else "?"
        return USBD_STATUS.get(self.status, f"{self.status:08X}")


class _Txn:
    def __init__(self, frame, ts, p: UPkt):
        self.frame, self.ts, self.bus, self.dev = frame, ts, p.bus, p.dev
        setup = p.data[:8].ljust(8, b"\0")
        self.bmRT, self.bReq, self.wValue, self.wIndex, self.wLength = struct.unpack("<BBHHH", setup)
        self.out = p.data[8:]          # some USBPcap versions append OUT data right after the setup packet
        self.inp = b""
        self.status = None
        self.end_frame = None

    def to_ev(self) -> Optional[Ev]:
        is_in = bool(self.bmRT & 0x80)
        if (self.bmRT & 0x7F) == 0x21:  # class, interface (HID)
            op = HID_REQ.get(self.bReq, f"R{self.bReq:02X}")
        else:
            op = f"C{self.bmRT:02X}{self.bReq:02X}"
        return Ev("ctl", self.frame, self.ts, self.bus, self.dev, op,
                  self.wValue >> 8, self.wValue & 0xFF,
                  self.inp if is_in else self.out, self.status,
                  self.wIndex, self.wLength, end_frame=self.end_frame)


def extract_all(path):
    """All control transfers + interrupt IN on every device, in frame order."""
    evs, pending = [], {}

    def flush(t):
        ev = t.to_ev()
        if ev:
            evs.append(ev)

    for frame, ts, lt, pkt in read_packets(path):
        if lt != LINKTYPE_USBPCAP:
            continue
        p = parse_usbpcap(pkt)
        if p is None:
            continue
        if p.xfer == XFER_CTRL:
            if not p.completion:
                if p.stage == STAGE_SETUP:
                    if p.irp in pending:
                        flush(pending.pop(p.irp))
                    pending[p.irp] = _Txn(frame, ts, p)
                elif p.stage == STAGE_DATA and p.irp in pending:
                    t = pending[p.irp]
                    if not t.out:
                        t.out = p.data
            else:
                t = pending.get(p.irp)
                if t is None:
                    continue
                if p.status:
                    t.status = p.status
                elif t.status is None:
                    t.status = 0
                if p.data and (t.bmRT & 0x80) and p.stage != STAGE_SETUP:
                    t.inp += p.data
                if p.stage in (STAGE_STATUS, STAGE_COMPLETE, None):
                    t.end_frame = frame
                    flush(pending.pop(p.irp))
        elif p.xfer == XFER_INT and p.completion and (p.ep & 0x80) and p.data:
            evs.append(Ev("int", frame, ts, p.bus, p.dev, "INT", 0, p.data[0], p.data,
                          status=None, ep=p.ep))
    for t in pending.values():
        flush(t)
    evs.sort(key=lambda e: e.frame)
    return evs


def extract(path, ids=(5, 6), int_ids=(3,), device=None, quiet=False):
    """Filter: only HID class requests with a report ID in `ids` (None = all) + interrupt IN whose first byte is in int_ids."""
    allev = extract_all(path)

    def want_ctl(e):
        return e.kind == "ctl" and e.op in HID_REQ.values() and (ids is None or e.rid in ids)

    if device is None:
        cnt = Counter((e.bus, e.dev) for e in allev if want_ctl(e))
        if not cnt:
            raise SystemExit(f"{path}: no matching HID reports found. Try --ids all or check the capture.")
        device = cnt.most_common(1)[0][0]
        if len(cnt) > 1 and not quiet:
            print(f"! {os.path.basename(path)}: several devices have matching reports {dict(cnt)}; "
                  f"picking bus {device[0]} dev {device[1]} (use --device BUS:DEV to choose)", file=sys.stderr)
    out = []
    for e in allev:
        if (e.bus, e.dev) != tuple(device):
            continue
        if want_ctl(e) or (e.kind == "int" and int_ids is not None and e.rid in int_ids):
            out.append(e)
    for i, e in enumerate(out, 1):
        e.idx = i
    return out, device


# --------------------------------------------------------------------------- display
def hx(b, sep=" "):
    return sep.join(f"{x:02x}" for x in b)


def hexdump(b, indent="        ", width=16):
    lines, prev, star = [], None, False
    for off in range(0, len(b), width):
        chunk = b[off:off + width]
        if chunk == prev and off + width < len(b):
            if not star:
                lines.append(indent + "*")
                star = True
            continue
        star, prev = False, chunk
        asc = "".join(chr(x) if 32 <= x < 127 else "." for x in chunk)
        lines.append(f"{indent}{off:04x}  {hx(chunk):<{width * 3}} {asc}")
    return "\n".join(lines)


def print_dump(path, evs, device, cmd_id, brief=False, inline_max=32):
    print(f"=== {os.path.basename(path)}  bus {device[0]} dev {device[1]}  ({len(evs)} events)")
    print(f"{'#':>4} {'frame':>7} {'t(ms)':>9} {'d(ms)':>8}  {'op':<13} {'if':>2} {'len':>4} {'st':<6} data")
    t0 = evs[0].ts if evs else 0
    prev_ts, seen = t0, {}
    for e in evs:
        if e.kind == "ctl" and e.op == "SET" and e.rid == cmd_id:
            print()  # every command on the command channel = a new group
        p = e.payload
        head = f"{e.idx:>4} {e.frame:>7} {(e.ts - t0) * 1e3:>9.2f} {(e.ts - prev_ts) * 1e3:>8.2f}  " \
               f"{e.tag:<13} {e.wIndex if e.kind == 'ctl' else '':>2} {len(p):>4} {e.status_str:<6} "
        prev_ts = e.ts
        if len(p) <= inline_max:
            print(head + hx(p))
            continue
        dup = seen.get(p)
        nz = sum(1 for x in p if x)
        print(head + hx(p[:16]) + f" ... ({nz} non-zero bytes)" + (f"  == same as #{dup}" if dup else ""))
        if not brief and not dup:
            print(hexdump(p))
        seen.setdefault(p, e.idx)


def ev_json(e):
    return {"idx": e.idx, "frame": e.frame, "end_frame": e.end_frame, "ts": e.ts, "kind": e.kind,
            "op": e.op, "rtype": RTYPE.get(e.rtype), "rid": e.rid, "wIndex": e.wIndex,
            "wLength": e.wLength, "len": len(e.payload), "status": e.status_str, "hex": e.payload.hex()}


# --------------------------------------------------------------------------- diff
def byte_ranges(a: bytes, b: bytes, gap=2):
    """[(start, end_exclusive)] of differing ranges, merged when <= gap bytes apart."""
    n = min(len(a), len(b))
    diffs = [i for i in range(n) if a[i] != b[i]]
    rng = []
    for i in diffs:
        if rng and i - rng[-1][1] <= gap:
            rng[-1][1] = i + 1
        else:
            rng.append([i, i + 1])
    if len(a) != len(b):
        rng.append([n, max(len(a), len(b))])
    return [tuple(r) for r in rng]


def print_byte_diff(ea, eb, max_ranges=40, max_show=24):
    a, b = ea.payload, eb.payload
    rs = byte_ranges(a, b)
    print(f"    A#{ea.idx}(f{ea.frame}) vs B#{eb.idx}(f{eb.frame})  {ea.tag}  len {len(a)}/{len(b)}  "
          f"{len(rs)} differing range(s)")
    for s, t in rs[:max_ranges]:
        sa, sb = a[s:t], b[s:t]
        cut = "..." if t - s > max_show else ""
        print(f"      0x{s:04x}..0x{t - 1:04x} ({t - s:>3}B): {hx(sa[:max_show])}{cut}")
        print(f"      {'':>18}  -> {hx(sb[:max_show])}{cut}")
    if len(rs) > max_ranges:
        print(f"      ... {len(rs) - max_ranges} more ranges")


def diff_key(e, cmd_id):
    if e.kind == "ctl" and e.op == "SET" and e.rid == cmd_id:
        return ("cmd", e.payload)       # command: match on content too -> anchor for alignment
    return (e.kind, e.op, e.rid, len(e.payload))


def shape(e):
    return (e.kind, e.op, e.rid, len(e.payload))


def line(e):
    p = e.payload
    s = hx(p) if len(p) <= 24 else hx(p[:16]) + f" ... ({len(p)}B)"
    return f"#{e.idx:<4} f{e.frame:<7} {e.tag:<13} {e.status_str:<6} {s}"


def cmd_diff(args):
    ea, _ = extract(args.a, args.ids, args.int_ids, args.device)
    eb, _ = extract(args.b, args.ids, args.int_ids, args.device)
    ka = [diff_key(e, args.cmd_id) for e in ea]
    kb = [diff_key(e, args.cmd_id) for e in eb]
    print(f"=== DIFF  A={os.path.basename(args.a)} ({len(ea)})   B={os.path.basename(args.b)} ({len(eb)})")
    sm = difflib.SequenceMatcher(None, ka, kb, autojunk=False)
    for tag, i1, i2, j1, j2 in sm.get_opcodes():
        if tag == "equal":
            changed = [(x, y) for x, y in zip(ea[i1:i2], eb[j1:j2]) if x.payload != y.payload]
            print(f"\n[same structure] A#{ea[i1].idx}..#{ea[i2 - 1].idx} = B#{eb[j1].idx}..#{eb[j2 - 1].idx}"
                  f"  ({i2 - i1} events, {len(changed)} with different payload)")
            if args.verbose:
                for x in ea[i1:i2]:
                    print("    " + line(x))
            for x, y in changed:
                print_byte_diff(x, y)
            continue
        print(f"\n[{ {'replace': 'replaced', 'delete': 'only in A', 'insert': 'only in B'}[tag] }]")
        sa, sb = list(ea[i1:i2]), list(eb[j1:j2])
        for x in sa:
            print("  A " + line(x))
        for y in sb:
            print("  B " + line(y))
        if tag == "replace":  # pair up by position when shapes match -> byte-by-byte diff
            for x, y in zip(sa, sb):
                if shape(x) == shape(y):
                    print_byte_diff(x, y)


# --------------------------------------------------------------------------- cmds
def exchanges(evs, cmd_id):
    """Groups: each SET <cmd_id> + the events that follow it until the next command."""
    groups, cur = [], None
    for e in evs:
        if e.kind == "ctl" and e.op == "SET" and e.rid == cmd_id:
            cur = (e, [])
            groups.append(cur)
        elif cur is not None:
            cur[1].append(e)
    return groups


def follow_sig(followers):
    if not followers:
        return "-"
    parts = []
    for f in followers:
        s = f"{'INT' if f.kind == 'int' else f.op}{f.rid:02X}" + ("" if f.status in (None, 0) else f"!{f.status_str}")
        if parts and parts[-1][0] == s:
            parts[-1][1] += 1
        else:
            parts.append([s, 1])
    return " ".join(s if n == 1 else f"{s}x{n}" for s, n in parts)


def cmd_cmds(args):
    files = args.files
    table = OrderedDict()
    for i, f in enumerate(files):
        evs, _ = extract(f, args.ids, args.int_ids, args.device, quiet=True)
        for c, fol in exchanges(evs, args.cmd_id):
            row = table.setdefault(c.payload, {"n": [0] * len(files), "follow": Counter(), "first": []})
            row["n"][i] += 1
            row["follow"][follow_sig(fol)] += 1
            if row["n"][i] == 1:
                row["first"].append(f"{i}:#{c.idx}")
    print("Files:")
    for i, f in enumerate(files):
        print(f"  [{i}] {os.path.basename(f)}")
    cols = " ".join(f"[{i}]" for i in range(len(files)))
    print(f"\n{'command (SET %02X, offset 0 = report ID)' % args.cmd_id:<40} {cols}   followed by")
    for payload, row in table.items():
        counts = " ".join(f"{n:>3}" for n in row["n"])
        only = "" if all(row["n"]) else "  <- only in " + ",".join(str(i) for i, n in enumerate(row["n"]) if n)
        fol = "; ".join(f"{s}" + (f" (x{n})" if n > 1 else "") for s, n in row["follow"].most_common(3))
        print(f"{hx(payload):<40} {counts}   {fol}{only}")


# --------------------------------------------------------------------------- find
def cmd_find(args):
    pat = bytes.fromhex(args.pattern.replace(" ", "").replace(":", ""))
    for f in args.files:
        evs, _ = extract(f, args.ids, args.int_ids, args.device, quiet=True)
        for e in evs:
            p, start, offs = e.payload, 0, []
            while True:
                k = p.find(pat, start)
                if k < 0:
                    break
                offs.append(k)
                start = k + 1
            if offs:
                print(f"{os.path.basename(f)}  {line(e)}\n    offset: " + ", ".join(f"0x{o:04x}" for o in offs[:20]))


# --------------------------------------------------------------------------- checksum probe
CKSUMS = {
    "sum8":     (1, lambda d: sum(d) & 0xFF),
    "neg8":     (1, lambda d: (-sum(d)) & 0xFF),
    "not8":     (1, lambda d: (~sum(d)) & 0xFF),
    "xor8":     (1, lambda d: _xor(d)),
    "0x55-sum": (1, lambda d: (0x55 - sum(d)) & 0xFF),
    "sum16le":  (2, lambda d: (sum(d) & 0xFFFF).to_bytes(2, "little")),
    "sum16be":  (2, lambda d: (sum(d) & 0xFFFF).to_bytes(2, "big")),
}


def _xor(d):
    x = 0
    for b in d:
        x ^= b
    return x


def cmd_cksum(args):
    payloads = []
    for f in args.files:
        evs, _ = extract(f, args.ids, args.int_ids, args.device, quiet=True)
        payloads += [e.payload for e in evs if e.kind == "ctl" and e.rid == args.rid and e.op == "SET"]
        if args.get:
            payloads += [e.payload for e in evs if e.kind == "ctl" and e.rid == args.rid and e.op == "GET" and e.payload]
    payloads = list(dict.fromkeys(payloads))  # unique, order kept
    print(f"{len(payloads)} unique payloads of report {args.rid:02X}. Trying checksums at the END of the payload "
          f"(computed from byte 0 or 1). RESULTS ARE ONLY HYPOTHESES.")
    if len(payloads) < 3:
        print("Too few samples to conclude anything.")
        return
    found = False
    for name, (w, fn) in CKSUMS.items():
        for start in (0, 1):
            ok = tot = 0
            for p in payloads:
                if len(p) < start + w + 1:
                    continue
                body, tail = p[start:-w], p[-w:]
                if not any(body) and not any(tail):
                    continue  # all zeros tells us nothing
                tot += 1
                v = fn(body)
                ok += (bytes([v]) if isinstance(v, int) else v) == tail
            if tot and ok / tot >= args.min_ratio and ok >= 3:
                found = True
                print(f"  {name:<9} from byte {start}: matches {ok}/{tot}" + ("" if ok == tot else "  (partial: maybe only one group of commands has a checksum)"))
    if not found:
        print("  No simple algorithm matches at the end of the payload. A checksum (if any) may sit at another "
              "fixed offset -> check the diff: which bytes change without matching the change you made.")


# --------------------------------------------------------------------------- main
def parse_ids(s):
    if s is None or s.lower() == "all":
        return None
    if s.lower() == "none":
        return ()
    return tuple(int(x, 0) for x in s.split(","))


def expand_files(items):
    """Windows cmd/PowerShell don't expand wildcards -> do it here. Directories are accepted too."""
    import glob
    out = []
    for it in items:
        if os.path.isdir(it):
            out += sorted(glob.glob(os.path.join(it, "*.pcapng")) + glob.glob(os.path.join(it, "*.pcap")))
        elif any(c in it for c in "*?["):
            out += sorted(glob.glob(it))
        else:
            out.append(it)
    if not out:
        raise SystemExit(f"No files found: {items}")
    return out


def parse_dev(s):
    if not s:
        return None
    b, d = s.split(":")
    return (int(b), int(d))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    common = argparse.ArgumentParser(add_help=False)
    common.add_argument("--ids", default="5,6", help="control report IDs to keep: '5,6' (default) | 'all'")
    common.add_argument("--int-ids", default="3", help="interrupt IN to keep, by first byte: '3' | 'all' | 'none'")
    common.add_argument("--device", help="USBPcap BUS:DEV (auto-detected by default)")
    common.add_argument("--cmd-id", type=lambda x: int(x, 0), default=5, help="report ID of the command channel (default 5)")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("dump", parents=[common], help="list in chronological order")
    p.add_argument("file")
    p.add_argument("--brief", action="store_true", help="don't print hexdumps of long payloads")
    p.add_argument("--json", help="export JSON")

    p = sub.add_parser("diff", parents=[common], help="align 2 captures and diff them byte by byte")
    p.add_argument("a")
    p.add_argument("b")
    p.add_argument("-v", "--verbose", action="store_true")

    p = sub.add_parser("cmds", parents=[common], help="table of unique report 5 commands across captures")
    p.add_argument("files", nargs="+")

    p = sub.add_parser("find", parents=[common], help="search for a byte sequence in every payload")
    p.add_argument("pattern")
    p.add_argument("files", nargs="+")

    p = sub.add_parser("cksum", parents=[common], help="probe simple checksums at the end of the payload (hypothesis)")
    p.add_argument("files", nargs="+")
    p.add_argument("--rid", type=lambda x: int(x, 0), default=5)
    p.add_argument("--get", action="store_true", help="include GET payloads too")
    p.add_argument("--min-ratio", type=float, default=0.5, help="minimum match ratio to print (default 0.5)")

    args = ap.parse_args()
    args.ids = parse_ids(args.ids)
    args.int_ids = parse_ids(args.int_ids) if args.int_ids.lower() != "all" else tuple(range(256))
    args.device = parse_dev(args.device)
    if hasattr(args, "files"):
        args.files = expand_files(args.files)

    if args.cmd == "dump":
        evs, dev = extract(args.file, args.ids, args.int_ids, args.device)
        print_dump(args.file, evs, dev, args.cmd_id, args.brief)
        if args.json:
            with open(args.json, "w", encoding="utf-8") as f:
                json.dump([ev_json(e) for e in evs], f, indent=1)
            print(f"\n-> {args.json}")
    elif args.cmd == "diff":
        cmd_diff(args)
    elif args.cmd == "cmds":
        cmd_cmds(args)
    elif args.cmd == "find":
        cmd_find(args)
    elif args.cmd == "cksum":
        cmd_cksum(args)


if __name__ == "__main__":
    main()