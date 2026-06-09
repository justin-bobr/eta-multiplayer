import re, sys

PATH = r'd:/Godot/Game/maps/training/traning.tscn'
raw = open(PATH, 'r', encoding='utf-8').read().split('\n')

# --- split into chunks: each chunk = a '[' header line + body until next '[' ---
chunks = []
cur = None
for ln in raw:
    if ln.startswith('['):
        if cur is not None: chunks.append(cur)
        cur = [ln]
    else:
        if cur is None: cur = [ln]      # preamble (gd_scene line handled as its own)
        else: cur.append(ln)
if cur is not None: chunks.append(cur)

def attr(h, key):
    m = re.search(key + r'="([^"]*)"', h)
    return m.group(1) if m else None

IDENT = ([1,0,0, 0,1,0, 0,0,1], [0.0,0.0,0.0])

def parse_xform(body):
    for l in body:
        if l.startswith('transform = Transform3D('):
            nums = [float(x) for x in l[l.find('(')+1:l.rfind(')')].split(',')]
            return (nums[0:9], nums[9:12])
    return (IDENT[0][:], IDENT[1][:])

def compose(A, B):
    ab, ao = A; bb, bo = B
    rb = [sum(ab[r*3+k]*bb[k*3+c] for k in range(3)) for r in range(3) for c in range(3)]
    ro = [sum(ab[r*3+k]*bo[k] for k in range(3)) + ao[r] for r in range(3)]
    return (rb, ro)

# --- index node chunks by path ---
nodes = {}      # path -> dict
root_path = None
for ch in chunks:
    h = ch[0]
    if not h.startswith('[node '): continue
    name = attr(h, 'name'); par = attr(h, 'parent')
    if par is None:
        path = '.'; root_path = '.'
    elif par == '.':
        path = name
    else:
        path = par + '/' + name
    nodes[path] = {'chunk': ch, 'name': name, 'parent': par, 'path': path,
                   'type': attr(h, 'type'), 'local': parse_xform(ch)}

gcache = {}
def gtrans(path):
    if path in gcache: return gcache[path]
    if path == '.' or path is None:
        g = nodes.get('.', {'local': IDENT})['local'] if '.' in nodes else IDENT
        gcache['.'] = g; return g
    nd = nodes[path]
    par = nd['parent']
    pg = gtrans('.' if par == '.' else par)
    g = compose(pg, nd['local'])
    gcache[path] = g
    return g

def body_get(body, key):
    for l in body:
        if l.startswith(key):
            return l.split('=', 1)[1].strip()
    return None

# --- classify lights ---
GROUPS = ['BP_Hanging_Rect','Bp_Hanging_Point','Bp_Hanging_Point2',
          'SM_Lane_PointLight','SM_Lane_PointLight2',
          'BP_Spotlight_SpotLight','BP_Spotlight_LightNode']
buckets = {g: [] for g in GROUPS}
move_paths = set()

for path, nd in list(nodes.items()):
    t = nd['type']; par = nd['parent'] or ''
    if t not in ('OmniLight3D', 'SpotLight3D'): continue
    body = nd['chunk']
    energy = body_get(body, 'light_energy')
    color = body_get(body, 'light_color') or ''
    grp = None
    if t == 'SpotLight3D' and par.startswith('hanging lights/'):
        e = float(energy) if energy else 0
        grp = 'BP_Hanging_Rect' if abs(e-1.5)<0.01 else ('Bp_Hanging_Point' if e>1.0 else 'Bp_Hanging_Point2')
    elif t == 'SpotLight3D' and (par.startswith('Spotlights2/') or par.startswith('SpotLights/')):
        grp = 'BP_Spotlight_SpotLight'
    elif t == 'OmniLight3D' and par.startswith('PointLights/'):
        grp = 'SM_Lane_PointLight' if '0.443137' in color else 'SM_Lane_PointLight2'
    elif t == 'OmniLight3D' and par.startswith('SpotLights/'):
        grp = 'BP_Spotlight_LightNode'
    if grp:
        buckets[grp].append(nd); move_paths.add(path)

# --- rebuild moved light chunks with global transform + new parent, unique names per folder ---
def fmt(v): return '%.9g' % v

def rebuild_light(nd, grp, used):
    name = nd['name']
    base = name; k = 2
    while name in used: name = '%s_%d' % (base, k); k += 1
    used.add(name)
    g = gtrans(nd['path'])
    xline = 'transform = Transform3D(' + ', '.join(fmt(v) for v in (g[0]+g[1])) + ')'
    out = []
    h = nd['chunk'][0]
    h = re.sub(r'name="[^"]*"', 'name="%s"' % name, h, count=1)
    h = re.sub(r'parent="[^"]*"', 'parent="Lights/%s"' % grp, h, count=1)
    out.append(h)
    placed = False
    for l in nd['chunk'][1:]:
        if l.startswith('transform = Transform3D('):
            out.append(xline); placed = True
        elif l.strip() == '' :
            continue
        else:
            out.append(l)
    if not placed:
        out.insert(1, xline)
    out.append('')   # trailing blank
    return out

# --- assemble output: keep all non-moved chunks; append Lights structure ---
out_lines = []
for ch in chunks:
    h = ch[0]
    if h.startswith('[node ') :
        name = attr(h, 'name'); par = attr(h, 'parent')
        path = '.' if par is None else (name if par=='.' else par+'/'+name)
        if path in move_paths:
            continue   # drop moved light from old location
    out_lines.extend(ch)

# append new structure
def node_chunk(name, parent):
    return ['[node name="%s" type="Node3D" parent="%s"]' % (name, parent), '']

while out_lines and out_lines[-1] == '': out_lines.pop()
out_lines.append('')
out_lines.extend(node_chunk('Lights', '.'))
for g in GROUPS:
    out_lines.extend(node_chunk(g, 'Lights'))
total = 0
report = {}
for g in GROUPS:
    used = set()
    for nd in buckets[g]:
        out_lines.extend(rebuild_light(nd, g, used))
        total += 1
    report[g] = len(buckets[g])

open(PATH, 'w', encoding='utf-8', newline='').write('\n'.join(out_lines))

print('moved total:', total)
for g in GROUPS: print('  %-26s %d' % (g, report[g]))
# sanity: Y range of hanging spots (should be high = ceiling)
ys = [gtrans(nd['path'])[1][1] for nd in buckets['BP_Hanging_Rect']]
print('Hanging-Rect world-Y range: %.2f .. %.2f' % (min(ys), max(ys)))
ys2 = [gtrans(nd['path'])[1][1] for nd in buckets['SM_Lane_PointLight']]
print('Lane-orange world-Y range: %.2f .. %.2f' % (min(ys2), max(ys2)))
