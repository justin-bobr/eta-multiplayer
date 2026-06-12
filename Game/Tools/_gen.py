import sys, importlib.util
s2=importlib.util.spec_from_file_location('v','_v54.py'); v=importlib.util.module_from_spec(s2)
sys.argv=['v']; s2.loader.exec_module(v)

D=r"C:\Users\stefa\Documents\Unreal Projects\extract\Content\InfimaGames\TacticalFPSAnimations\Weapons\AssaultRifle\Animations\Weapon\FP"

# UE montage name -> GD animation key (and generator func name)
ANIMS=[
    ("Fire","fire"),
    ("Equip","equip"),
    ("Inspect","inspect"),
    ("Reload","reload"),
    ("Reload_Empty","reload_empty"),
    ("Reload_Quick","reload_quick"),
    ("MagCheck","mag_check"),
    ("ClearJam_MagSwipe","clear_jam_mag_swipe"),
    ("ClearJam_Rack","clear_jam_rack"),
]

# UE cue / notify class -> GD method
CUE2METHOD={
    "A_TFA_AR_Fire_Single_Cue":"PlayAudioFire",
    "A_TFA_AR_Fire_Tail_Long_Cue":"PlayAudioFireTail",
    "A_TFA_AR_Click_Cue":"PlayAudioClick",
    "A_TFA_Foley_Cloth_Cue":"PlayAudioFoleyCloth",
    "A_TFA_AR_Bolt_Open_Cue":"PlayAudioBoltOpen",
    "A_TFA_AR_Bolt_Close_Cue":"PlayAudioBoltClose",
    "A_TFA_AR_GunSmack_Cue":"PlayAudioGunSmack",
    "A_TFA_AR_Mag_Insert_Cue":"PlayAudioMagInsert",
    "A_TFA_AR_Mag_Remove_Empty_Cue":"PlayAudioMagRemoveEmpty",
    "A_TFA_AR_Mag_Remove_Full_Cue":"PlayAudioMagRemoveFull",
    "A_TFA_AR_Mag_Remove_Full_001":"PlayAudioMagRemoveFull",
    "A_TFA_AR_Malfunction_Cue":"PlayAudioMalfunction",
    "A_TFA_AR_EmptyCasing_Cue":"PlayAudioEmptyCasing",
    "AN_TFA_EjectCasing_C":"EjectCasing",
    "AN_TFA_DropMagazine_C":"DropMagazine",
}

# NotifyState cue -> (begin method, end method)
STATE2METHODS={
    "ANS_HideMainMag_C":("HideMainMag","ShowMainMag"),
    "ANS_ShowReserveMag_C":("ShowReserveMag","HideReserveMag"),
}

def fmtf(x):
    s=f"{x:g}"
    if "." not in s and "e" not in s: s+=".0"
    return s

def gen():
    out=[]
    out.append("@tool")
    out.append("extends EditorScript")
    out.append("")
    out.append('const SAVE_PATH = "res://character/fps/weapon/m4/animations/weapon/events/events.tres"')
    out.append("")
    out.append("func _run() -> void:")
    out.append("\tvar lib := AnimationLibrary.new()")
    out.append("")
    for ue,key in ANIMS:
        out.append(f'\tlib.add_animation(&"{key}", _make_{key}())')
    out.append("")
    out.append("\tvar err := ResourceSaver.save(lib, SAVE_PATH)")
    out.append("\tif err == OK:")
    out.append('\t\tprint("[GenerateWeaponEvents] Saved: ", SAVE_PATH)')
    out.append("\telse:")
    out.append('\t\tpush_error("[GenerateWeaponEvents] Save failed: ", error_string(err))')
    out.append("")
    out.append("")
    out.append("func _anim(len: float) -> Animation:")
    out.append("\tvar a := Animation.new()")
    out.append("\ta.length = len")
    out.append("\ta.loop_mode = Animation.LOOP_NONE")
    out.append("\treturn a")
    out.append("")
    out.append("")
    out.append("func _track(anim: Animation) -> int:")
    out.append("\tvar t := anim.add_track(Animation.TYPE_METHOD)")
    out.append('\tanim.track_set_path(t, NodePath("."))')
    out.append("\treturn t")
    out.append("")
    out.append("")
    out.append("func _key(anim: Animation, time: float, method: StringName) -> void:")
    out.append("\tvar t := _track(anim)")
    out.append('\tanim.track_insert_key(t, time, {"method": method, "args": []})')

    for ue,key in ANIMS:
        data=v.extract(rf"{D}\AM_TFA_FP_WEP_AR_{ue}.uasset")
        length=data['length']
        out.append("")
        out.append("")
        out.append(f"func _make_{key}() -> Animation:")
        out.append(f"\tvar a := _anim({length})")
        unknown=[]
        keys=[]
        for ev in data['events']:
            if ev['cue'] in STATE2METHODS:
                begin_m,end_m=STATE2METHODS[ev['cue']]
                keys.append((ev['t'], begin_m))
                end_t=ev['end'] if ev['end'] is not None else length
                end_t=min(end_t, length-0.001)        # ensure the reset fires before the clip ends
                keys.append((end_t, end_m))
                continue
            mname=CUE2METHOD.get(ev['cue'])
            if mname is None:
                unknown.append((ev['t'],ev['cue'])); continue
            keys.append((ev['t'], mname))
        for t,mname in sorted(keys, key=lambda x:x[0]):
            out.append(f'\t_key(a, {t:g}, &"{mname}")')
        out.append("\treturn a")
        if unknown:
            sys.stderr.write(f"[{key}] UNMAPPED: {unknown}\n")
    return "\n".join(out)+"\n"

if __name__=='__main__':
    txt=gen()
    open(r"d:\Godot\Game\Tools\GenerateWeaponEvents.gd","w",encoding="utf-8").write(txt)
    sys.stderr.write("written GenerateWeaponEvents.gd\n")
