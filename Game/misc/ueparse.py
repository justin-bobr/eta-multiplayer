import sys, struct, os

def read_names(data):
    # Parse UE name table by brute force: find the contiguous run of FName entries.
    # Each entry: int32 len, bytes (len includes trailing null) [ASCII] OR negative len => UTF16,
    # followed by 4 bytes (case-preserving hashes) in many versions.
    # We instead just collect ALL length-prefixed ASCII strings in the file as a name pool,
    # but for property-tag decoding we need the actual name index table. So parse header.
    # UE asset header is version-dependent; do a heuristic scan for the name table.
    names = []
    # Heuristic: scan whole file for length-prefixed ascii strings of printable chars.
    return names

def scan_strings(data, minlen=3):
    out = []
    i = 0
    n = len(data)
    while i < n - 4:
        ln = struct.unpack_from('<i', data, i)[0]
        if 0 < ln < 256 and i + 4 + ln <= n:
            s = data[i+4:i+4+ln]
            if s and s[-1] == 0 and all(32 <= c < 127 for c in s[:-1]):
                txt = s[:-1].decode('ascii', 'replace')
                if len(txt) >= minlen:
                    out.append((i, txt))
        i += 1
    return out

def parse_name_table(data):
    """Try to find the FName table: a long contiguous run of length-prefixed strings.
    Returns list of (offset, name) and a dict name->first index."""
    strs = scan_strings(data, minlen=1)
    # Find the longest contiguous run where each string's end+? aligns to next string start.
    # UE name entries: int32 len, bytes(len), then (depending on version) 0,2,or 4 hash bytes.
    best = []
    used = set()
    # Build index by offset
    by_off = {o: t for o, t in strs}
    offs = sorted(by_off)
    for start_idx in range(len(offs)):
        run = []
        o = offs[start_idx]
        while o in by_off:
            t = by_off[o]
            run.append((o, t))
            ln = len(t.encode('ascii','replace')) + 1
            nxt = o + 4 + ln
            # try gap 0,2,4 (hash bytes)
            found = None
            for gap in (4, 0, 2, 8):
                if nxt + gap in by_off:
                    found = nxt + gap
                    break
            if found is None:
                break
            o = found
        if len(run) > len(best):
            best = run
    return best

def main():
    path = sys.argv[1]
    with open(path, 'rb') as f:
        data = f.read()
    print(f"# FILE {path} ({len(data)} bytes)")
    table = parse_name_table(data)
    names = [t for _, t in table]
    print(f"# NAME TABLE: {len(names)} entries (heuristic)")
    name_to_idx = {}
    for i, nm in enumerate(names):
        name_to_idx.setdefault(nm, i)

    # Build offset->index for FName 8-byte keys using the table's index positions
    targets_float = ["FieldOfView","OrthoWidth","AimFieldOfView","HipFieldOfView","BlendTime",
                     "RelativeRotation","Intensity","SourceRadius","LightmassSettings"]
    # We decode by scanning for known property names followed by a known property type.
    types_of_interest = {"FloatProperty","DoubleProperty","StructProperty","BoolProperty",
                         "ByteProperty","IntProperty","NameProperty","ObjectProperty",
                         "TextProperty","StrProperty","EnumProperty","ArrayProperty"}
    type_idx = {t: name_to_idx[t] for t in types_of_interest if t in name_to_idx}

    def fname_keys(name):
        if name not in name_to_idx:
            return []
        idx = name_to_idx[name]
        return [struct.pack('<ii', idx, 0)]

    def read_prop_at(pos):
        # pos points at start of property tag: Name(8) Type(8) Size(4) ArrayIdx(4) ...
        name_i = struct.unpack_from('<i', data, pos)[0]
        type_i = struct.unpack_from('<i', data, pos+8)[0]
        if not (0 <= name_i < len(names)) or not (0 <= type_i < len(names)):
            return None
        pname = names[name_i]; ptype = names[type_i]
        if ptype not in types_of_interest:
            return None
        size = struct.unpack_from('<i', data, pos+16)[0]
        arr = struct.unpack_from('<i', data, pos+20)[0]
        p = pos + 24
        info = {"name": pname, "type": ptype, "size": size}
        if ptype == "StructProperty":
            sname_i = struct.unpack_from('<i', data, p)[0]
            sname = names[sname_i] if 0 <= sname_i < len(names) else "?"
            info["struct"] = sname
            p += 8 + 16  # struct name FName + guid
            p += 1       # has-guid flag
            # value
            if sname in ("Vector","Vector3f","Rotator"):
                if size >= 24:
                    info["val"] = struct.unpack_from('<ddd', data, p)
                elif size >= 12:
                    info["val"] = struct.unpack_from('<fff', data, p)
            elif sname in ("Vector2D",):
                info["val"] = struct.unpack_from('<dd', data, p) if size>=16 else struct.unpack_from('<ff', data, p)
            elif sname in ("Quat","Vector4"):
                info["val"] = struct.unpack_from('<dddd', data, p) if size>=32 else struct.unpack_from('<ffff', data, p)
            elif sname in ("Color","LinearColor"):
                if sname=="LinearColor": info["val"]=struct.unpack_from('<ffff', data, p)
                else: info["val"]=tuple(data[p:p+4])
        elif ptype in ("FloatProperty",):
            p += 1
            info["val"] = struct.unpack_from('<f', data, p)[0]
        elif ptype in ("DoubleProperty",):
            p += 1
            info["val"] = struct.unpack_from('<d', data, p)[0]
        elif ptype == "BoolProperty":
            info["val"] = data[p]  # bool stored in tag before guid flag
        elif ptype in ("IntProperty",):
            p += 1
            info["val"] = struct.unpack_from('<i', data, p)[0]
        elif ptype in ("NameProperty","EnumProperty","ByteProperty","ObjectProperty"):
            p += (8 if ptype in ("ByteProperty","EnumProperty") else 0)
            p += 1
            vi = struct.unpack_from('<i', data, p)[0]
            info["val"] = names[vi] if 0 <= vi < len(names) else vi
        return info

    found = []
    interesting = ["FieldOfView","RelativeLocation","RelativeRotation","RelativeScale3D",
                   "AimFieldOfView","HipFieldOfView","FieldOfViewAiming","CameraFieldOfView",
                   "Intensity","LightColor","Temperature","BlendTime","Mesh","bUsePawnControlRotation",
                   "TargetArmLength","SocketOffset","ProjectionMode","AspectRatio","PostProcessSettings",
                   "AttachParent","ComponentName","FOV","DefaultFieldOfView"]
    for nm in interesting:
        for key in fname_keys(nm):
            start = 0
            while True:
                pos = data.find(key, start)
                if pos < 0: break
                start = pos + 1
                r = read_prop_at(pos)
                if r and r["name"] == nm and "val" in r:
                    found.append((pos, r))

    seen = set()
    for pos, r in sorted(found):
        k = (r["name"], str(r.get("val")))
        if k in seen: continue
        seen.add(k)
        extra = f" struct={r.get('struct')}" if 'struct' in r else ""
        print(f"  @{pos:#x} {r['name']} : {r['type']}{extra} = {r.get('val')}  (size={r['size']})")

if __name__ == "__main__":
    main()
