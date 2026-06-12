import struct, sys

PATH = r'C:\Users\stefa\Downloads\Military Training Facility (5.1 - 5.3)\Military Training Facility (5.1 - 5.3)\FiringRange 5.7\Content\MilitaryFiringRange\FiringRange\Maps\Demonstration.umap'

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

def try_names(off, count):
    o = off; out = []
    for _ in range(count):
        if o + 4 > N: return None
        n = struct.unpack_from('<i', b, o)[0]
        if n < -2000 or n > 2000 or n == 0: return None
        s, o2 = fstr(o)
        if not all(32 <= ord(c) < 127 for c in s): return None
        o = o2 + 4  # skip 2x uint16 hash
    return o

# Locate name table as the longest chain of valid FName entries.
# Entry layout: int32 len (incl null term), len bytes (ascii+null), 4 bytes hash.
def entry_at(p):
    if p + 4 > N: return None
    n = struct.unpack_from('<i', b, p)[0]
    if n < 2 or n > 200: return None
    s = p + 4
    if s + n + 4 > N: return None
    if b[s + n - 1] != 0: return None
    body = b[s:s + n - 1]
    if not body or not all(32 <= c < 127 for c in body): return None
    return p + 4 + n + 4  # next entry offset

# scan all valid entry starts
nexts = {}
p = 0
while p < N - 8:
    nx = entry_at(p)
    if nx is not None:
        nexts[p] = nx
        p = nx if nx > p else p + 1
    else:
        p += 1

# find longest chain
best_start, best_len = None, 0
visited = {}
for start in nexts:
    cur = start; cnt = 0
    while cur in nexts:
        cnt += 1; cur = nexts[cur]
    if cnt > best_len:
        best_len, best_start = cnt, start

if best_start is None or best_len < 50:
    print('FAILED to locate name table'); sys.exit(1)

names = []; o = best_start
while o in nexts:
    s, o2 = fstr(o); o += 0
    names.append(s); o = nexts[o]
nameCount = len(names)
idx = {n: i for i, n in enumerate(names)}
print(f'# names={nameCount} at off={best_start}')

PROP_TYPES = {'FloatProperty','DoubleProperty','IntProperty','BoolProperty','StructProperty',
              'ByteProperty','EnumProperty','NameProperty','ObjectProperty','StrProperty'}

def nm(i):
    return names[i] if 0 <= i < nameCount else None

# Scan whole file for tagged properties of interest
def scan_props(prop_names):
    want = set(prop_names)
    hits = []
    o = 0
    end = N - 24
    while o < end:
        ni = struct.unpack_from('<i', b, o)[0]
        if 0 <= ni < nameCount and names[ni] in want:
            nn = struct.unpack_from('<i', b, o+4)[0]      # name number
            ti = struct.unpack_from('<i', b, o+8)[0]
            tn = struct.unpack_from('<i', b, o+12)[0]
            tname = nm(ti)
            if nn == 0 and tn == 0 and tname in PROP_TYPES:
                size = struct.unpack_from('<i', b, o+16)[0]
                arr = struct.unpack_from('<i', b, o+20)[0]
                if 0 <= size < 100000 and 0 <= arr < 16:
                    val = decode_value(names[ni], tname, size, o+24)
                    if val is not None:
                        hits.append((o, names[ni], tname, val))
                        o += 1; continue
        o += 1
    return hits

def decode_value(pname, tname, size, p):
    try:
        if tname in ('FloatProperty',):
            p += 1  # HasPropertyGuid
            return round(struct.unpack_from('<f', b, p)[0], 6)
        if tname == 'DoubleProperty':
            p += 1
            return round(struct.unpack_from('<d', b, p)[0], 6)
        if tname == 'IntProperty':
            p += 1
            return struct.unpack_from('<i', b, p)[0]
        if tname == 'BoolProperty':
            return bool(b[p])  # value stored in tag, before guid flag
        if tname in ('ByteProperty', 'EnumProperty'):
            ei = struct.unpack_from('<i', b, p)[0]; p += 8  # EnumName FName
            p += 1  # guid flag
            vi = struct.unpack_from('<i', b, p)[0]
            return nm(vi)
        if tname == 'NameProperty':
            p += 1
            vi = struct.unpack_from('<i', b, p)[0]
            return nm(vi)
        if tname == 'StructProperty':
            si = struct.unpack_from('<i', b, p)[0]; p += 8  # StructName FName
            sname = nm(si)
            p += 16  # struct guid
            p += 1   # guid flag
            if sname == 'LinearColor':
                r,g,bl,a = struct.unpack_from('<4f', b, p)
                return ('LinearColor', round(r,5), round(g,5), round(bl,5), round(a,5))
            if sname == 'Color':
                bb,gg,rr,aa = b[p],b[p+1],b[p+2],b[p+3]  # BGRA
                return ('Color', rr, gg, bb, aa)
            return (sname, 'struct', size)
    except Exception:
        return None
    return None

print('\n===== EXPONENTIAL HEIGHT FOG =====')
fog_props = ['FogDensity','FogHeightFalloff','FogInscatteringColor','FogMaxOpacity',
             'StartDistance','FogCutoffDistance','DirectionalInscatteringExponent',
             'DirectionalInscatteringStartDistance','DirectionalInscatteringColor',
             'VolumetricFogScatteringDistribution','VolumetricFogAlbedo','VolumetricFogEmissive',
             'VolumetricFogExtinctionScale','VolumetricFogDistance','bEnableVolumetricFog',
             'SecondFogDensity','InscatteringColorCubemapAngle']
for h in scan_props(fog_props):
    print('  %-38s %-15s %s' % (h[1], h[2], h[3]))

print('\n===== SKY LIGHT (isolated by proximity to CubemapResolution/SkyDistanceThreshold) =====')
sky_props = ['Intensity','IntensityType','LightColor','SourceType','CubemapResolution',
             'LowerHemisphereColor','bLowerHemisphereIsBlack','SkyDistanceThreshold',
             'OcclusionMaxDistance','Contrast','OcclusionExponent','MinOcclusion',
             'OcclusionTint','bAffectsWorld','bRealTimeCapture','Cubemap']
sky_hits = scan_props(sky_props)
# anchors that ONLY exist on a SkyLightComponent
anchors = [h[0] for h in sky_hits if h[1] in ('CubemapResolution','SkyDistanceThreshold')]
for h in sorted(sky_hits, key=lambda x: x[0]):
    near = any(abs(h[0]-a) < 600 for a in anchors)
    flag = '  <-- SKYLIGHT' if near else ''
    if near or h[1] != 'Intensity':
        print('  off=%-9d %-26s %-15s %s%s' % (h[0], h[1], h[2], h[3], flag))
