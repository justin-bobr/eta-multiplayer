import re
PATH = r'd:/Godot/Game/maps/training/traning.tscn'
raw = open(PATH, 'r', encoding='utf-8').read().split('\n')

# split into chunks: header '[' line + body until next '['
chunks = []
cur = None
for ln in raw:
    if ln.startswith('['):
        if cur is not None: chunks.append(cur)
        cur = [ln]
    else:
        if cur is None: cur = [ln]
        else: cur.append(ln)
if cur is not None: chunks.append(cur)

def attr(h, key):
    m = re.search(key + r'="([^"]*)"', h)
    return m.group(1) if m else None

def info(ch):
    h = ch[0]
    if not h.startswith('[node '): return None
    name = attr(h, 'name'); par = attr(h, 'parent'); typ = attr(h, 'type')
    path = '.' if par is None else (name if par == '.' else par + '/' + name)
    return name, typ, par, path

removed_all = []
while True:
    # build set of parent paths in use
    parents = set()
    for ch in chunks:
        ni = info(ch)
        if ni and ni[2] not in (None, '.'):
            parents.add(ni[2])
    # find deletable empties
    to_del = []
    for idx, ch in enumerate(chunks):
        ni = info(ch)
        if not ni: continue
        name, typ, par, path = ni
        if typ != 'Node3D': continue
        if par is None: continue                 # root
        if path in parents: continue             # has children
        low = path.lower()
        if 'decal' in low: continue              # decals excepted
        if path == 'Lights' or path.startswith('Lights/'): continue
        to_del.append(idx)
    if not to_del: break
    for idx in sorted(to_del, reverse=True):
        removed_all.append(info(chunks[idx])[3])
        del chunks[idx]

open(PATH, 'w', encoding='utf-8', newline='').write('\n'.join(l for ch in chunks for l in ch))
print('geloeschte leere Nodes:', len(removed_all))
from collections import Counter
print('nach Top-Container:', dict(Counter(p.split('/')[0] for p in removed_all)))
# list non-positioner deletions (anything not named *Light*)
others = [p for p in removed_all if not re.search(r'PointLight|SpotLight|RectLight|LightNode', p.split('/')[-1])]
print('sonstige geloeschte (kein Licht-Positioner):', others)
