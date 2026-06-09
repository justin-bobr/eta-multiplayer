import struct, sys

PATH = sys.argv[1]
b = open(PATH, 'rb').read()
N = len(b)

def fstr(o):
    n = struct.unpack_from('<i', b, o)[0]; o += 4
    if n == 0: return '', o
    if n < 0:
        raw = b[o:o-n*2]; o += -n*2
        return raw.decode('utf-16-le', 'replace').rstrip('\x00'), o
    raw = b[o:o+n]; o += n
    return raw.decode('latin-1', 'replace').rstrip('\x00'), o

def entry_at(p):
    if p + 4 > N: return None
    n = struct.unpack_from('<i', b, p)[0]
    if n < 2 or n > 200: return None
    s = p + 4
    if s + n + 4 > N: return None
    if b[s + n - 1] != 0: return None
    body = b[s:s + n - 1]
    if not body or not all(32 <= c < 127 for c in body): return None
    return p + 4 + n + 4

nexts = {}; p = 0
while p < N - 8:
    nx = entry_at(p)
    if nx is not None:
        nexts[p] = nx; p = nx if nx > p else p + 1
    else:
        p += 1

best_start, best_len = None, 0
for start in nexts:
    cur = start; cnt = 0
    while cur in nexts:
        cnt += 1; cur = nexts[cur]
    if cnt > best_len:
        best_len, best_start = cnt, start

names = []; o = best_start
while o in nexts:
    s, _ = fstr(o); names.append(s); o = nexts[o]
nameCount = len(names)
def nm(i): return names[i] if 0 <= i < nameCount else None
print(f'# {PATH.split(chr(92))[-1]}: names={nameCount}')

PROP_TYPES = {'FloatProperty','DoubleProperty','IntProperty','BoolProperty','StructProperty',
              'ByteProperty','EnumProperty','NameProperty','ObjectProperty'}

def decode_value(tname, size, p):
    try:
        if tname == 'FloatProperty': return round(struct.unpack_from('<f', b, p+1)[0], 6)
        if tname == 'DoubleProperty': return round(struct.unpack_from('<d', b, p+1)[0], 6)
        if tname == 'IntProperty': return struct.unpack_from('<i', b, p+1)[0]
        if tname == 'BoolProperty': return bool(b[p])
        if tname in ('ByteProperty','EnumProperty'):
            return nm(struct.unpack_from('<i', b, p+8+1)[0])
        if tname == 'StructProperty':
            sname = nm(struct.unpack_from('<i', b, p)[0]); q = p+8+16+1
            if sname == 'LinearColor':
                r,g,bl,a = struct.unpack_from('<4f', b, q); return ('LinearColor',round(r,4),round(g,4),round(bl,4),round(a,4))
            if sname == 'Color':
                return ('Color(BGRA)', b[q],b[q+1],b[q+2],b[q+3])
            return (sname,'struct')
    except Exception:
        return None
    return None

def scan(want):
    want=set(want); hits=[]; o=0
    while o < N-24:
        ni = struct.unpack_from('<i', b, o)[0]
        if 0 <= ni < nameCount and names[ni] in want:
            if struct.unpack_from('<i',b,o+4)[0]==0 and struct.unpack_from('<i',b,o+12)[0]==0:
                tn=nm(struct.unpack_from('<i',b,o+8)[0])
                if tn in PROP_TYPES:
                    size=struct.unpack_from('<i',b,o+16)[0]
                    if 0<=size<100000:
                        v=decode_value(tn,size,o+24)
                        if v is not None:
                            hits.append((o,names[ni],tn,v)); o+=1; continue
        o+=1
    return hits

props=['Intensity','IntensityUnits','AttenuationRadius','OuterConeAngle','InnerConeAngle',
       'LightColor','SourceRadius','SourceLength','Temperature','bUseTemperature',
       'LightFalloffExponent','bUseInverseSquaredFalloff','VolumetricScatteringIntensity']
for h in sorted(scan(props), key=lambda x:x[0]):
    cls = ''
    print('  off=%-9d %-26s %-15s %s' % (h[0], h[1], h[2], h[3]))
