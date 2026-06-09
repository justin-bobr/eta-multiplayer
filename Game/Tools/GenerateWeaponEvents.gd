@tool
extends EditorScript

const SAVE_PATH = "res://character/fps/weapon/m4/animations/weapon/events/events.tres"

func _run() -> void:
	var lib := AnimationLibrary.new()

	lib.add_animation(&"fire", _make_fire())
	lib.add_animation(&"equip", _make_equip())
	lib.add_animation(&"inspect", _make_inspect())
	lib.add_animation(&"reload", _make_reload())
	lib.add_animation(&"reload_empty", _make_reload_empty())
	lib.add_animation(&"reload_quick", _make_reload_quick())
	lib.add_animation(&"mag_check", _make_mag_check())
	lib.add_animation(&"clear_jam_mag_swipe", _make_clear_jam_mag_swipe())
	lib.add_animation(&"clear_jam_rack", _make_clear_jam_rack())

	var err := ResourceSaver.save(lib, SAVE_PATH)
	if err == OK:
		print("[GenerateWeaponEvents] Saved: ", SAVE_PATH)
	else:
		push_error("[GenerateWeaponEvents] Save failed: ", error_string(err))


func _anim(len: float) -> Animation:
	var a := Animation.new()
	a.length = len
	a.loop_mode = Animation.LOOP_NONE
	return a


func _track(anim: Animation) -> int:
	var t := anim.add_track(Animation.TYPE_METHOD)
	anim.track_set_path(t, NodePath("."))
	return t


func _key(anim: Animation, time: float, method: StringName) -> void:
	var t := _track(anim)
	anim.track_insert_key(t, time, {"method": method, "args": []})


func _make_fire() -> Animation:
	var a := _anim(0.8)
	_key(a, 0, &"PlayAudioFire")
	_key(a, 0, &"PlayAudioFireTail")
	_key(a, 0.0397, &"EjectCasing")
	_key(a, 0.1121, &"PlayAudioClick")
	return a


func _make_equip() -> Animation:
	var a := _anim(2.3333)
	_key(a, 0.0901, &"PlayAudioFoleyCloth")
	_key(a, 0.2762, &"PlayAudioClick")
	_key(a, 0.3817, &"PlayAudioBoltOpen")
	_key(a, 1.0091, &"PlayAudioBoltClose")
	_key(a, 1.4101, &"PlayAudioGunSmack")
	return a


func _make_inspect() -> Animation:
	var a := _anim(5.4)
	_key(a, 0.1153, &"PlayAudioFoleyCloth")
	_key(a, 0.1463, &"PlayAudioGunSmack")
	_key(a, 0.9657, &"PlayAudioFoleyCloth")
	_key(a, 1.8869, &"PlayAudioFoleyCloth")
	_key(a, 2.0626, &"PlayAudioClick")
	_key(a, 2.18, &"PlayAudioBoltOpen")
	_key(a, 3.1213, &"PlayAudioBoltClose")
	_key(a, 3.2323, &"PlayAudioClick")
	_key(a, 3.5165, &"PlayAudioFoleyCloth")
	_key(a, 3.552, &"PlayAudioGunSmack")
	_key(a, 4.2003, &"PlayAudioFoleyCloth")
	return a


func _make_reload() -> Animation:
	var a := _anim(3.6667)
	_key(a, 0.0843, &"PlayAudioFoleyCloth")
	_key(a, 0.1295, &"PlayAudioGunSmack")
	_key(a, 0.4551, &"ShowReserveMag")
	_key(a, 0.5768, &"PlayAudioFoleyCloth")
	_key(a, 0.6843, &"PlayAudioClick")
	_key(a, 0.8471, &"PlayAudioMagInsert")
	_key(a, 1.6943, &"PlayAudioFoleyCloth")
	_key(a, 2.1495, &"PlayAudioGunSmack")
	_key(a, 2.5204, &"HideMainMag")
	_key(a, 3.6657, &"HideReserveMag")
	_key(a, 3.6657, &"ShowMainMag")
	return a


func _make_reload_empty() -> Animation:
	var a := _anim(3.6667)
	_key(a, 0.0602, &"PlayAudioFoleyCloth")
	_key(a, 0.1778, &"PlayAudioMagRemoveEmpty")
	_key(a, 0.4333, &"HideMainMag")
	_key(a, 0.4333, &"DropMagazine")
	_key(a, 0.7325, &"PlayAudioFoleyCloth")
	_key(a, 0.8608, &"ShowReserveMag")
	_key(a, 1.0551, &"PlayAudioMagInsert")
	_key(a, 1.3626, &"PlayAudioClick")
	_key(a, 1.9686, &"PlayAudioBoltClose")
	_key(a, 2.4088, &"PlayAudioFoleyCloth")
	_key(a, 2.6138, &"PlayAudioGunSmack")
	_key(a, 3.6657, &"ShowMainMag")
	_key(a, 3.6657, &"HideReserveMag")
	return a


func _make_reload_quick() -> Animation:
	var a := _anim(2.6333)
	_key(a, 0.0844, &"PlayAudioFoleyCloth")
	_key(a, 0.2316, &"PlayAudioMagRemoveEmpty")
	_key(a, 0.3218, &"PlayAudioClick")
	_key(a, 0.44, &"HideMainMag")
	_key(a, 0.44, &"DropMagazine")
	_key(a, 0.8608, &"ShowReserveMag")
	_key(a, 0.8682, &"PlayAudioFoleyCloth")
	_key(a, 1.0934, &"PlayAudioMagInsert")
	_key(a, 1.3575, &"PlayAudioClick")
	_key(a, 1.5697, &"PlayAudioFoleyCloth")
	_key(a, 1.7451, &"PlayAudioGunSmack")
	_key(a, 2.6287, &"ShowMainMag")
	_key(a, 2.6323, &"HideReserveMag")
	return a


func _make_mag_check() -> Animation:
	var a := _anim(4.5667)
	_key(a, 0.1275, &"PlayAudioFoleyCloth")
	_key(a, 0.1838, &"PlayAudioMagRemoveFull")
	_key(a, 0.2514, &"PlayAudioClick")
	_key(a, 0.4392, &"PlayAudioFoleyCloth")
	_key(a, 1.3817, &"PlayAudioFoleyCloth")
	_key(a, 2.4744, &"PlayAudioMagInsert")
	_key(a, 2.5345, &"PlayAudioFoleyCloth")
	_key(a, 3.3155, &"PlayAudioFoleyCloth")
	_key(a, 3.3643, &"PlayAudioGunSmack")
	return a


func _make_clear_jam_mag_swipe() -> Animation:
	var a := _anim(5.4667)
	_key(a, 0, &"PlayAudioMalfunction")
	_key(a, 0.9707, &"PlayAudioMagRemoveFull")
	_key(a, 1.2314, &"PlayAudioClick")
	_key(a, 2.0675, &"PlayAudioGunSmack")
	_key(a, 2.1619, &"PlayAudioMalfunction")
	_key(a, 2.3732, &"PlayAudioEmptyCasing")
	_key(a, 2.58, &"PlayAudioMagInsert")
	_key(a, 3.4026, &"PlayAudioGunSmack")
	_key(a, 3.5509, &"PlayAudioClick")
	_key(a, 3.9734, &"PlayAudioBoltOpen")
	_key(a, 4.3645, &"PlayAudioBoltClose")
	_key(a, 4.6791, &"PlayAudioGunSmack")
	return a


func _make_clear_jam_rack() -> Animation:
	var a := _anim(4.2)
	_key(a, 0, &"PlayAudioMalfunction")
	_key(a, 0.0517, &"PlayAudioClick")
	_key(a, 1.0428, &"PlayAudioClick")
	_key(a, 1.0843, &"PlayAudioGunSmack")
	_key(a, 1.7542, &"PlayAudioBoltOpen")
	_key(a, 1.9304, &"PlayAudioClick")
	_key(a, 2.0271, &"PlayAudioEmptyCasing")
	_key(a, 2.103, &"EjectCasing")
	_key(a, 2.1652, &"PlayAudioBoltClose")
	_key(a, 2.5554, &"PlayAudioGunSmack")
	return a
