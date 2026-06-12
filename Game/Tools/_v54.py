import struct, sys
import importlib.util
spec=importlib.util.spec_from_file_location('m','_uasset_full.py'); m=importlib.util.module_from_spec(spec); spec.loader.exec_module(m)

def parse_typename(b,nm,o,depth=0):
    """UE5.4 FPropertyTypeName: FName(8) + int32 paramCount, recursive."""
    idx=struct.unpack_from('<i',b,o)[0]; num=struct.unpack_from('<i',b,o+4)[0]; o+=8
    cnt=struct.unpack_from('<i',b,o)[0]; o+=4
    if cnt<0 or cnt>8 or depth>6:
        raise ValueError(f"bad typename cnt={cnt} idx={idx} at {o-4}")
    params=[]
    for _ in range(cnt):
        c,o=parse_typename(b,nm,o,depth+1)
        params.append(c)
    return {'name':nm(idx),'params':params}, o

def read_prop(b,nm,nidx,o,limit):
    """Read one 5.4 tagged property. Returns dict or None (if None terminator)."""
    if o+8>limit: return None,o
    ni=struct.unpack_from('<i',b,o)[0]; o2=o+8
    if ni==nidx.get('None'): return None,o2
    name=nm(ni)
    tn,o2=parse_typename(b,nm,o2)
    size=struct.unpack_from('<i',b,o2)[0]; o2+=4
    o2+=1                              # 1 flag byte (UE5.4 override-op), then value
    val_off=o2
    return {'name':name,'type':tn['name'],'tn':tn,'size':size,'off':val_off}, val_off+size

def walk_struct(b,nm,nidx,o,limit):
    props=[]
    while True:
        p,o=read_prop(b,nm,nidx,o,limit)
        if p is None: break
        props.append(p)
    return props,o

def find_linkvalue(b,nm,nidx,lo,hi):
    """Walk a struct range and return its top-level LinkValue float, or None."""
    o=lo
    while True:
        try: p,o=read_prop(b,nm,nidx,o,hi)
        except Exception: return None
        if p is None: return None
        if p['name']=='LinkValue' and p['type']=='FloatProperty':
            return round(struct.unpack_from('<f',b,p['off'])[0],4)

def sound_cue_in_export(b,imports,ex):
    eo=ex['off']
    while eo+4<=ex['off']+ex['size']:
        idx=struct.unpack_from('<i',b,eo)[0]
        if idx<0:
            ii=-idx-1
            if ii<len(imports) and imports[ii]['class'] in('SoundCue','SoundWave','MetaSoundSource'):
                return imports[ii]['name']
        eo+=1
    return None

def playsound_vol_pitch(b,nm,nidx,ex):
    """Read VolumeMultiplier/PitchMultiplier from a PlaySound export (default 1.0)."""
    vol=1.0; pitch=1.0
    try:
        props,_=walk_struct(b,nm,nidx,ex['off']+1,ex['off']+ex['size'])
    except Exception:
        return vol,pitch
    for p in props:
        if p['name']=='VolumeMultiplier' and p['type']=='FloatProperty':
            vol=round(struct.unpack_from('<f',b,p['off'])[0],4)
        elif p['name']=='PitchMultiplier' and p['type']=='FloatProperty':
            pitch=round(struct.unpack_from('<f',b,p['off'])[0],4)
    return vol,pitch

def extract(path):
    b,names,nm,imports,exports=m.load(path)
    nidx={n:i for i,n in enumerate(names)}
    mont=[e for e in exports if m.resolve(e['classIdx'],imports,exports)[0]=='AnimMontage']
    mont=mont[0] if mont else max(exports,key=lambda e:e['size'])
    lo,hi=mont['off'],mont['off']+mont['size']
    # montage play length
    length=None
    mp,_=walk_struct(b,nm,nidx,find_props_start(b,nm,nidx,lo,hi),hi) if False else ([],0)
    # scan montage for CachedPlayLength / SequenceLength FloatProperty
    for key in('CachedPlayLength','SequenceLength'):
        ki=nidx.get(key); FP=nidx.get('FloatProperty')
        if ki is None: continue
        o=lo
        while o+12<=hi:
            if struct.unpack_from('<i',b,o)[0]==ki and struct.unpack_from('<i',b,o+8)[0]==FP:
                # value: name(8)+typename(12)+size(4)+flag(1)
                length=round(struct.unpack_from('<f',b,o+25)[0],4); break
            o+=1
        if length: break
    # notifies array
    NOT=nidx['Notifies']; ARR=nidx['ArrayProperty']
    pos=None; o=lo
    while o+12<=hi:
        if struct.unpack_from('<i',b,o)[0]==NOT and struct.unpack_from('<i',b,o+8)[0]==ARR:
            pos=o; break
        o+=1
    p,_=read_prop(b,nm,nidx,pos,hi)
    ec_off=p['off']; o=ec_off+4
    count=struct.unpack_from('<i',b,ec_off)[0]
    rows=[]
    for e in range(count):
        props,o=walk_struct(b,nm,nidx,o,hi)
        link=0.0; ref=None; tto=0.0; nname=None; sref=None; endlink=None
        for pr in props:
            if pr['name']=='LinkValue' and pr['type']=='FloatProperty':
                link=struct.unpack_from('<f',b,pr['off'])[0]
            elif pr['name']=='TriggerTimeOffset' and pr['type']=='FloatProperty':
                tto=struct.unpack_from('<f',b,pr['off'])[0]
            elif pr['name']=='Notify' and pr['type']=='ObjectProperty':
                ref=struct.unpack_from('<i',b,pr['off'])[0]
            elif pr['name']=='NotifyStateClass' and pr['type']=='ObjectProperty':
                sref=struct.unpack_from('<i',b,pr['off'])[0]
            elif pr['name']=='NotifyName' and pr['type']=='NameProperty':
                nname=nm(struct.unpack_from('<i',b,pr['off'])[0])
            elif pr['name']=='EndLink' and pr['type']=='StructProperty':
                endlink=find_linkvalue(b,nm,nidx,pr['off'],pr['off']+pr['size'])
        cue=None; kind='audio'; vol=1.0; pitch=1.0
        if ref and ref>0 and ref-1<len(exports):
            ex=exports[ref-1]
            cue=sound_cue_in_export(b,imports,ex)
            if cue is None:
                cue=m.resolve(ex['classIdx'],imports,exports)[0]; kind='notify'
            else:
                vol,pitch=playsound_vol_pitch(b,nm,nidx,ex)
        if cue is None and sref and sref<0:
            cue=imports[-sref-1]['name']; kind='state'
        if cue is None and nname and nname!='None':
            cue=nname; kind='name'
        rows.append({'t':round(link,4),'end':endlink,'cue':cue,'kind':kind,'vol':vol,'pitch':pitch})
    rows.sort(key=lambda x:x['t'])
    return {'length':length,'count':count,'events':rows}

def find_props_start(*a): return 0

if __name__=='__main__':
    import json
    out={}
    for path in sys.argv[1:]:
        name=path.replace('\\','/').split('/')[-1].replace('AM_TFA_FP_WEP_AR_','').replace('.uasset','')
        out[name]=extract(path)
    print(json.dumps(out,indent=1))
