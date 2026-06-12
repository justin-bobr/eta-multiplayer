import re, sys
PATH = r'd:/Godot/Game/maps/training/traning.tscn'
SCOPE = sys.argv[1] if len(sys.argv) > 1 else 'SpotLights'   # top-container to flatten

raw = open(PATH, 'r', encoding='utf-8').read().split('\n')
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

def attr(h, k):
    m = re.search(k + r'="([^"]*)"', h); return m.group(1) if m else None
def info(ch):
    h = ch[0]
    if not h.startswith('[node '): return None
    nm = attr(h, 'name'); par = attr(h, 'parent'); ty = attr(h, 'type')
    path = '.' if par is None else (nm if par == '.' else par + '/' + nm)
    return nm, ty, par, path
IDENT = [1,0,0,0,1,0,0,0,1]
def xform(ch):
    for l in ch:
        if l.startswith('transform = Transform3D('):
            n = [float(x) for x in l[l.find('(')+1:l.rfind(')')].split(',')]
            return n[0:9], n[9:12]
    return IDENT[:], [0.0,0.0,0.0]
def compose(A, B):
    ab, ao = A; bb, bo = B
    rb = [sum(ab[r*3+k]*bb[k*3+c] for k in range(3)) for r in range(3) for c in range(3)]
    ro = [sum(ab[r*3+k]*bo[k] for k in range(3)) + ao[r] for r in range(3)]
    return rb, ro
def fmt(v): return '%.9g' % v

# children count by parent path
kids = {}
for ch in chunks:
    ni = info(ch)
    if ni and ni[2] not in (None, '.'):
        kids.setdefault(ni[2], []).append(ni)

flattened = 0
del_idx = set()
for idx, ch in enumerate(chunks):
    ni = info(ch)
    if not ni: continue
    nm, ty, par, path = ni
    if ty != 'Node3D' or par is None: continue
    if par.split('/')[0] != SCOPE: continue
    ck = kids.get(path, [])
    if len(ck) != 1 or ck[0][1] != 'MeshInstance3D': continue
    mesh = ck[0]
    mname = mesh[0]
    old_mpath = path + '/' + mname
    new_mpath = par + '/' + mname
    wlocal = xform(ch)
    # update the mesh chunk
    for j, c2 in enumerate(chunks):
        n2 = info(c2)
        if not n2: continue
        if n2[3] == old_mpath:                       # the mesh itself
            mlocal = xform(c2)
            g = compose(wlocal, mlocal)
            xline = 'transform = Transform3D(' + ', '.join(fmt(v) for v in (g[0]+g[1])) + ')'
            h = c2[0]
            h = re.sub(r'parent="[^"]*"', 'parent="%s"' % par, h, count=1)
            c2[0] = h
            placed = False
            for k, l in enumerate(c2):
                if l.startswith('transform = Transform3D('):
                    c2[k] = xline; placed = True
            if not placed:
                c2.insert(1, xline)
        elif n2[2] == old_mpath:                      # direct child of mesh
            c2[0] = re.sub(r'parent="[^"]*"', 'parent="%s"' % new_mpath, c2[0], count=1)
        elif n2[2] and n2[2].startswith(old_mpath + '/'):   # deeper descendant
            newpar = new_mpath + n2[2][len(old_mpath):]
            c2[0] = re.sub(r'parent="[^"]*"', 'parent="%s"' % newpar, c2[0], count=1)
    del_idx.add(idx)
    flattened += 1

out = []
for idx, ch in enumerate(chunks):
    if idx in del_idx: continue
    out.extend(ch)
open(PATH, 'w', encoding='utf-8', newline='').write('\n'.join(out))
print('aufgeloeste Wrapper in %s: %d' % (SCOPE, flattened))
