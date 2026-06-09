import struct, sys, os, json

class R:
    def __init__(s,buf): s.b=buf; s.o=0
    def seek(s,o): s.o=o
    def i32(s): v=struct.unpack_from('<i',s.b,s.o)[0]; s.o+=4; return v
    def u32(s): v=struct.unpack_from('<I',s.b,s.o)[0]; s.o+=4; return v
    def i64(s): v=struct.unpack_from('<q',s.b,s.o)[0]; s.o+=8; return v
    def f32(s): v=struct.unpack_from('<f',s.b,s.o)[0]; s.o+=4; return v
    def u8(s): v=s.b[s.o]; s.o+=1; return v

def fstr(b,o):
    n=struct.unpack_from('<i',b,o)[0]; o+=4
    if n==0: return '',o
    if n<0: raw=b[o:o-n*2]; o+=-n*2; return raw.decode('utf-16-le','replace').rstrip('\x00'),o
    raw=b[o:o+n]; o+=n; return raw.decode('latin-1','replace').rstrip('\x00'),o

def load(path):
    b=open(path,'rb').read(); r=R(b)
    assert r.u32()==0x9E2A83C1
    lg=r.i32(); r.i32(); r.i32()
    if lg<=-8: r.i32()
    r.i32(); cv=r.i32(); r.o+=cv*20
    r.i32(); s,r.o=fstr(b,r.o); r.u32()
    nameCount=r.i32(); nameOffset=r.i32()
    r.i32(); r.i32()                 # soft
    _,r.o=fstr(b,r.o)                # locId
    r.i32(); r.i32()                 # gather
    exportCount=r.i32(); exportOffset=r.i32()
    importCount=r.i32(); importOffset=r.i32()
    names=[]; o=nameOffset
    for _ in range(nameCount):
        s,o=fstr(b,o); o+=4; names.append(s)
    def nm(i): return names[i] if 0<=i<nameCount else f"?{i}"
    # imports: stride 40, ObjectName FName idx at +20
    imports=[]
    for k in range(importCount):
        o=importOffset+k*40
        objIdx=struct.unpack_from('<i',b,o+20)[0]
        clsIdx=struct.unpack_from('<i',b,o+8)[0]
        imports.append({'name':nm(objIdx),'class':nm(clsIdx)})
    # exports: stride 112; layout cls@0 super@4 tmpl@8 outer@12 name@16(+num@20) flags@24 serialSize@28(i64) serialOffset@36(i64)
    exports=[]
    for k in range(exportCount):
        o=exportOffset+k*112
        clsIdx=struct.unpack_from('<i',b,o)[0]
        nameIdx=struct.unpack_from('<i',b,o+16)[0]
        serialSize=struct.unpack_from('<q',b,o+28)[0]
        serialOffset=struct.unpack_from('<q',b,o+36)[0]
        exports.append({'name':nm(nameIdx),'classIdx':clsIdx,'off':serialOffset,'size':serialSize})
    return b,names,nm,imports,exports

def resolve(idx,imports,exports):
    if idx>0: return exports[idx-1]['name'] if idx-1<len(exports) else f"exp{idx}", idx-1
    if idx<0:
        ii=-idx-1; return (imports[ii]['name'] if ii<len(imports) else f"imp{idx}"), None
    return None,None

def walk_props(b,nm,nidx,start,limit):
    """Yield (name,type,offset_of_value,size) for tagged props until None or limit."""
    o=start
    NONE=nidx.get('None')
    out=[]
    while o+8<=limit:
        ni=struct.unpack_from('<i',b,o)[0]; num=struct.unpack_from('<i',b,o+4)[0]; o+=8
        if ni==NONE: break
        pname=nm(ni)
        ti=struct.unpack_from('<i',b,o)[0]; o+=8
        ptype=nm(ti)
        size=struct.unpack_from('<i',b,o)[0]; o+=4
        arridx=struct.unpack_from('<i',b,o)[0]; o+=4
        struct_name=None; inner_type=None
        if ptype=='StructProperty':
            struct_name=nm(struct.unpack_from('<i',b,o)[0]); o+=8; o+=16
        elif ptype in ('ByteProperty','EnumProperty'):
            o+=8
        elif ptype=='ArrayProperty':
            inner_type=nm(struct.unpack_from('<i',b,o)[0]); o+=8
        elif ptype=='BoolProperty':
            boolval=b[o]; o+=1
        elif ptype in ('SetProperty','MapProperty'):
            o+=8
            if ptype=='MapProperty': o+=8
        # property guid flag
        hasguid=b[o]; o+=1
        if hasguid: o+=16
        val_off=o
        out.append({'name':pname,'type':ptype,'size':size,'off':val_off,'struct':struct_name,'inner':inner_type})
        o=val_off+size
    return out,o

def parse_notifies(b,nm,nidx,montage,imports,exports):
    NOTIFIES=nidx.get('Notifies'); ARR=nidx.get('ArrayProperty'); STRUCTP=nidx.get('StructProperty')
    lo=montage['off']; hi=montage['off']+montage['size']
    # find Notifies ArrayProperty whose inner type is StructProperty
    target=None
    o=lo
    while o+32<=hi:
        if (struct.unpack_from('<i',b,o)[0]==NOTIFIES and
            struct.unpack_from('<i',b,o+8)[0]==ARR and
            struct.unpack_from('<i',b,o+24)[0]==STRUCTP):
            target=o; break
        o+=1
    if target is None: return []
    o=target
    o+=8                                        # name
    o+=8                                        # type
    size=struct.unpack_from('<i',b,o)[0]; o+=4
    o+=4                                         # arrayindex
    o+=8                                         # inner type (StructProperty)
    o+=1                                         # guid flag
    arr_end=o+size
    count=struct.unpack_from('<i',b,o)[0]; o+=4
    # inner struct property tag
    o+=8                                         # name
    o+=8                                         # type
    o+=4                                         # size
    o+=4                                         # arrayindex
    o+=8                                         # struct name
    o+=16                                        # guid
    o+=1                                         # guid flag
    elems=[]
    for e in range(count):
        eprops,o=walk_props(b,nm,nidx,o,arr_end)
        link=None; notifyref=None
        for ep in eprops:
            if ep['name']=='LinkValue' and ep['type']=='FloatProperty':
                link=round(struct.unpack_from('<f',b,ep['off'])[0],4)
            if ep['name']=='Notify' and ep['type']=='ObjectProperty':
                notifyref=struct.unpack_from('<i',b,ep['off'])[0]
        elems.append((link,notifyref))
    return elems

def playsound_cue(b,nm,nidx,exp,imports,exports):
    props,_=walk_props(b,nm,nidx,exp['off'],exp['off']+exp['size'])
    for p in props:
        if p['name'] in ('Sound','SoundCue') and p['type']=='ObjectProperty':
            idx=struct.unpack_from('<i',b,p['off'])[0]
            name,_=resolve(idx,imports,exports)
            return name
    return None

def main(path):
    b,names,nm,imports,exports=load(path)
    nidx={n:i for i,n in enumerate(names)}
    montage=None
    for e in exports:
        cname,_=resolve(e['classIdx'],imports,exports)
        if cname=='AnimMontage': montage=e
    if montage is None:
        # fallback: largest export
        montage=max(exports,key=lambda e:e['size'])
    elems=parse_notifies(b,nm,nidx,montage,imports,exports)
    rows=[]
    for link,ref in elems:
        cue=None
        if ref and ref>0:
            exp=exports[ref-1]
            cue=playsound_cue(b,nm,nidx,exp,imports,exports)
        rows.append((link,cue))
    rows.sort(key=lambda x:(x[0] if x[0] is not None else 1e9))
    print(f"== {os.path.basename(path)}  ({len(rows)} notifies)")
    for t,c in rows:
        print(f"   {t:>8}   {c}")
    return rows

if __name__=='__main__':
    for p in sys.argv[1:]:
        main(p)
