import re, sys
PATH = r'd:/Godot/Game/maps/training/traning.tscn'
SCOPE = sys.argv[1] if len(sys.argv) > 1 else 'FlatLights'

def load():
    raw = open(PATH, 'r', encoding='utf-8').read().split('\n')
    chunks, cur = [], None
    for ln in raw:
        if ln.startswith('['):
            if cur is not None: chunks.append(cur)
            cur = [ln]
        else:
            if cur is None: cur = [ln]
            else: cur.append(ln)
    if cur is not None: chunks.append(cur)
    return chunks

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

total = 0
while True:
    chunks = load()
    kids = {}
    for ch in chunks:
        ni = info(ch)
        if ni and ni[2] not in (None, '.'):
            kids.setdefault(ni[2], []).append(ni)
    target = None
    for idx, ch in enumerate(chunks):
        ni = info(ch)
        if not ni: continue
        nm, ty, par, path = ni
        if ty != 'Node3D' or par is None: continue
        # within scope subtree but not the scope container itself
        if not (path == SCOPE or path.startswith(SCOPE + '/')): continue
        if path == SCOPE: continue
        ck = kids.get(path, [])
        if len(ck) == 1:
            target = (idx, ch, ni, ck[0]); break
    if target is None: break
    idx, ch, (nm, ty, par, path), child = target
    cname = child[0]
    old_cp = path + '/' + cname
    new_cp = par + '/' + cname
    wlocal = xform(ch)
    for c2 in chunks:
        n2 = info(c2)
        if not n2: continue
        if n2[3] == old_cp:
            mlocal = xform(c2)
            g = compose(wlocal, mlocal)
            xline = 'transform = Transform3D(' + ', '.join(fmt(v) for v in (g[0]+g[1])) + ')'
            c2[0] = re.sub(r'parent="[^"]*"', 'parent="%s"' % par, c2[0], count=1)
            placed = False
            for k, l in enumerate(c2):
                if l.startswith('transform = Transform3D('): c2[k] = xline; placed = True
            if not placed: c2.insert(1, xline)
        elif n2[2] == old_cp:
            c2[0] = re.sub(r'parent="[^"]*"', 'parent="%s"' % new_cp, c2[0], count=1)
        elif n2[2] and n2[2].startswith(old_cp + '/'):
            c2[0] = re.sub(r'parent="[^"]*"', 'parent="%s"' % (new_cp + n2[2][len(old_cp):]), c2[0], count=1)
    del chunks[idx]
    open(PATH, 'w', encoding='utf-8', newline='').write('\n'.join(l for c in chunks for l in c))
    total += 1

print('kollabierte Single-Child-Node3D in %s: %d' % (SCOPE, total))
