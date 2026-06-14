using Godot;
using System.Collections.Generic;

namespace Vantix.Weapon;

public enum WeaponMode { FPS, TPS }

[Tool, GlobalClass]
public partial class WeaponAnimation : Node3D
{
	[ExportGroup("Weapon Settings")]
	[Export] public StringName WeaponName;
	[Export] public WeaponMode Mode = WeaponMode.FPS;
	private static readonly HashSet<string> AnimProps = new()
	{
		nameof(ReferencePose), nameof(FireModeStates),
		nameof(FpFire),
		nameof(FpReload), nameof(FpReloadAimed),
		nameof(FpReloadEmpty), nameof(FpReloadEmptyAimed),
		nameof(FpReloadQuick), nameof(FpReloadQuickAimed),
		nameof(FpMagCheck), nameof(FpMagCheckAimed),
		nameof(FpInspect), nameof(FpEquip),
		nameof(FpClearJamMagSwipe), nameof(FpClearJamRack),
		nameof(TpFire),
		nameof(TpReload), nameof(TpReloadAimed),
		nameof(TpReloadEmpty), nameof(TpReloadEmptyAimed),
		nameof(TpReloadQuick), nameof(TpReloadQuickAimed),
		nameof(TpMagCheck), nameof(TpMagCheckAimed),
		nameof(TpInspect), nameof(TpEquip),
		nameof(TpClearJamMagSwipe), nameof(TpClearJamRack),
	};
	private static readonly HashSet<string> EventProps = new()
	{
		nameof(EvFire), nameof(EvReload), nameof(EvReloadEmpty), nameof(EvReloadQuick),
		nameof(EvMagCheck), nameof(EvInspect), nameof(EvEquip),
		nameof(EvClearJamMagSwipe), nameof(EvClearJamRack),
	};

	[ExportGroup("Animation Player")]
	[Export] public NodePath AnimationPlayerPath = new("MergedAnimationPlayer");
	[Export] public NodePath EventPlayerPath = new("EventPlayer");
	[Export] public bool RebuildAnimationTree { get => false; set { if (value) EditorRebuildTree(); } }

	[ExportGroup("Fire Modes")]
	[Export] public Godot.Collections.Dictionary FireModes = new() { { "Safety", 0.0f }, { "Semi", 0.0333333f }, { "Auto", 0.0666667f } };
	[Export] public string ActualFireMode = "Semi";

	[ExportGroup("Animations")]

	[ExportSubgroup("Base")]
	[Export] public StringName ReferencePose;
	[Export] public StringName FireModeStates;

	[ExportSubgroup("Fire")]
	[Export] public StringName FpFire;
	[Export] public StringName TpFire;
	[Export] public string EvFire = "events/fire";

	[ExportSubgroup("Reload")]
	[Export] public StringName FpReload;
	[Export] public StringName FpReloadAimed;
	[Export] public StringName TpReload;
	[Export] public StringName TpReloadAimed;
	[Export] public string EvReload = "events/reload";

	[ExportSubgroup("Reload Empty")]
	[Export] public StringName FpReloadEmpty;
	[Export] public StringName FpReloadEmptyAimed;
	[Export] public StringName TpReloadEmpty;
	[Export] public StringName TpReloadEmptyAimed;
	[Export] public string EvReloadEmpty = "events/reload_empty";

	[ExportSubgroup("Reload Quick")]
	[Export] public StringName FpReloadQuick;
	[Export] public StringName FpReloadQuickAimed;
	[Export] public StringName TpReloadQuick;
	[Export] public StringName TpReloadQuickAimed;
	[Export] public string EvReloadQuick = "events/reload_quick";

	[ExportSubgroup("Mag Check")]
	[Export] public StringName FpMagCheck;
	[Export] public StringName FpMagCheckAimed;
	[Export] public StringName TpMagCheck;
	[Export] public StringName TpMagCheckAimed;
	[Export] public string EvMagCheck = "events/mag_check";

	[ExportSubgroup("Inspect")]
	[Export] public StringName FpInspect;
	[Export] public StringName TpInspect;
	[Export] public string EvInspect = "events/inspect";

	[ExportSubgroup("Equip")]
	[Export] public StringName FpEquip;
	[Export] public StringName TpEquip;
	[Export] public string EvEquip = "events/equip";

	[ExportSubgroup("Clear Jam Mag Swipe")]
	[Export] public StringName FpClearJamMagSwipe;
	[Export] public StringName TpClearJamMagSwipe;
	[Export] public string EvClearJamMagSwipe = "events/clear_jam_mag_swipe";

	[ExportSubgroup("Clear Jam Rack")]
	[Export] public StringName FpClearJamRack;
	[Export] public StringName TpClearJamRack;
	[Export] public string EvClearJamRack = "events/clear_jam_rack";

	[ExportSubgroup("Malfunctions")]
	[Export] public StringName[] FpMalfunctions = [];
	[Export] public StringName[] TpMalfunctions = [];

	[ExportGroup("Test (Editor)")]
	[Export] public bool TestFire { get => false; set { if (value) EditorPlay(IsTPS ? TpFire : FpFire, EvFire); } }
	[Export] public bool TestReload { get => false; set { if (value) EditorPlay(IsTPS ? TpReload : FpReload, EvReload); } }
	[Export] public bool TestReloadEmpty { get => false; set { if (value) EditorPlay(IsTPS ? TpReloadEmpty : FpReloadEmpty, EvReloadEmpty); } }
	[Export] public bool TestReloadQuick { get => false; set { if (value) EditorPlay(IsTPS ? TpReloadQuick : FpReloadQuick, EvReloadQuick); } }
	[Export] public bool TestMagCheck { get => false; set { if (value) EditorPlay(IsTPS ? TpMagCheck : FpMagCheck, EvMagCheck); } }
	[Export] public bool TestInspect { get => false; set { if (value) EditorPlay(IsTPS ? TpInspect : FpInspect, EvInspect); } }
	[Export] public bool TestEquip { get => false; set { if (value) EditorPlay(IsTPS ? TpEquip : FpEquip, EvEquip); } }
	[Export] public bool TestClearJamMagSwipe { get => false; set { if (value) EditorPlay(IsTPS ? TpClearJamMagSwipe : FpClearJamMagSwipe, EvClearJamMagSwipe); } }
	[Export] public bool TestClearJamRack { get => false; set { if (value) EditorPlay(IsTPS ? TpClearJamRack : FpClearJamRack, EvClearJamRack); } }
	[Export] public bool TestCycleFireMode { get => false; set { if (value) { EnsureTree(); CycleFireMode(); } } }

	[ExportGroup("Eject Casing")]
	[Export] public PackedScene EjectCasingScene;
	[Export] public int EjectCasingPoolSize = 4;
	[Export] public Vector3 EjectMinRotation = new(-15f, -45f, -5f);
	[Export] public Vector3 EjectMaxRotation = new(15f, 45f, 35f);
	[Export] public float EjectMinForce = 1.5f;
	[Export] public float EjectMaxForce = 3.5f;
	[Export] public float EjectRotationSpeed = 20f;
	[Export] public float EjectLifetime = 15f;
	// Eject-bone-local direction the casing is flung along before random spread (flip X for left-ejecting weapons).
	[Export] public Vector3 EjectDirectionLocal = new(1f, 0.4f, 0.15f);

	[ExportGroup("Drop Magazine")]
	[Export] public PackedScene DropMagazineScene;
	[Export] public float DropImpulseForce = 1.5f;       // m/s along the mag socket up axis (UE: ImpulseForce, VelChange)
	[Export] public float DropRotationForce = 360f;      // deg/s about a random axis (UE: RotationForce, VelChange)
	[Export] public float DropMagazineLifetime = 8f;

	[ExportGroup("Magazine")]
	[Export] public NodePath MainMagPath = new("Skeleton3D/SOCKET_Magazine/Magazine");
	[Export] public NodePath ReserveMagPath = new("Skeleton3D/SOCKET_Magazine_Reserve/Magazine");

	[ExportGroup("Audio")]
	[Export] public NodePath AudioPlayerPath = new("AudioStreamPlayer");
	[Export] public int AudioVoices = 8;
	[Export] public StringName AudioBus = "Master";

	[ExportSubgroup("Firing")]
	[Export] public AudioStream[] AudioFire = [];
	[Export(PropertyHint.Range, "0,1,0.01")] public float VolumeFire = 1.0f;
	[Export] public AudioStream[] AudioFireTail = [];
	[Export(PropertyHint.Range, "0,1,0.01")] public float VolumeFireTail = 1.0f;
	[Export] public AudioStream[] AudioEmptyCasing = [];
	[Export(PropertyHint.Range, "0,1,0.01")] public float VolumeEmptyCasing = 1.0f;

	[ExportSubgroup("Handling")]
	[Export] public AudioStream[] AudioClick = [];
	[Export(PropertyHint.Range, "0,1,0.01")] public float VolumeClick = 0.5f;
	[Export] public AudioStream[] AudioFoleyCloth = [];
	[Export(PropertyHint.Range, "0,1,0.01")] public float VolumeFoleyCloth = 0.5f;
	[Export] public AudioStream[] AudioBoltOpen = [];
	[Export(PropertyHint.Range, "0,1,0.01")] public float VolumeBoltOpen = 1.0f;
	[Export] public AudioStream[] AudioBoltClose = [];
	[Export(PropertyHint.Range, "0,1,0.01")] public float VolumeBoltClose = 0.5f;
	[Export] public AudioStream[] AudioGunSmack = [];
	[Export(PropertyHint.Range, "0,1,0.01")] public float VolumeGunSmack = 0.4f;
	[Export] public AudioStream[] AudioMalfunction = [];
	[Export(PropertyHint.Range, "0,1,0.01")] public float VolumeMalfunction = 1.0f;

	[ExportSubgroup("Magazine")]
	[Export] public AudioStream[] AudioMagInsert = [];
	[Export(PropertyHint.Range, "0,1,0.01")] public float VolumeMagInsert = 1.0f;
	[Export] public AudioStream[] AudioMagRemoveFull = [];
	[Export(PropertyHint.Range, "0,1,0.01")] public float VolumeMagRemoveFull = 1.0f;
	[Export] public AudioStream[] AudioMagRemoveEmpty = [];
	[Export(PropertyHint.Range, "0,1,0.01")] public float VolumeMagRemoveEmpty = 1.0f;

	// Per-weapon recoil + view-kick feel, read by NetworkPlayer. Recoil is a damped spring (kick displaces, spring recovers).
	[ExportGroup("Recoil & View Kick")]
	[Export] public Vector3 RecoilImpulseHipfire = new(-0.8f, 0.28f, 0f);
	[Export] public Vector3 RecoilImpulseAimed = new(-0.15f, 0.05f, 0f);
	[Export(PropertyHint.Range, "10,600,5")] public float RecoilStiffness = 200f;
	[Export(PropertyHint.Range, "0.1,1.5,0.05")] public float RecoilDamping = 0.6f;
	[Export(PropertyHint.Range, "0.2,4,0.1")] public float RecoilMass = 1f;
	[Export(PropertyHint.Range, "1,45,0.5")] public float RecoilMaxDegrees = 10f;
	// Recoil view-kick scale while aiming (lerped by aim blend).
	[Export(PropertyHint.Range, "0,1,0.05")] public float AimRecoilMultiplier = 0.5f;
	// Procedural weapon-bone recoil on top of camera kick: rotation scales the spring (deg), kickback pushes toward the player (m/deg).
	[Export(PropertyHint.Range, "0,1,0.02")] public float WeaponRecoilRotScale = 0.15f;
	[Export(PropertyHint.Range, "0,0.05,0.001")] public float WeaponRecoilKickback = 0.005f;

	// Per-weapon ADS calibration (iron-sight offset via WeaponBoneModifier + FOV zoom + crouch/canted offsets); the character composes these with its blend state.
	[ExportGroup("ADS (Viewmodel)")]
	[Export(PropertyHint.Range, "30,120,0.5")] public float AimFov = 78f;
	[Export(PropertyHint.Range, "20,90,1")] public float TpsAimFov = 50f;   // third-person / spectator ADS zoom
	[Export(PropertyHint.Range, "1,30,0.1")] public float AimBlendSpeed = 12f;
	[Export(PropertyHint.Range, "-1,1,0.0001,or_less,or_greater")] public Vector3 AdsOffsetPosition = new(-0.02f, 0.06f, 0.0205f);
	[Export(PropertyHint.Range, "-180,180,0.01,or_less,or_greater")] public Vector3 AdsOffsetRotation = new(0f, -8.4f, 0f);
	[Export(PropertyHint.Range, "-1,1,0.0001,or_less,or_greater")] public Vector3 CrouchOffsetPosition = new(0.015f, 0.02f, -0.015f);
	[Export(PropertyHint.Range, "-180,180,0.01,or_less,or_greater")] public Vector3 CrouchOffsetRotation = new(0f, 4.3f, 0f);
	[Export(PropertyHint.Range, "-1,1,0.0001,or_less,or_greater")] public Vector3 CantedOffsetPosition = new(-0.05f, -0.015f, -0.01f);
	[Export(PropertyHint.Range, "-180,180,0.01,or_less,or_greater")] public Vector3 CantedOffsetRotation = new(0f, 35.0f, 0f);

	// Editor-only ADS calibration preview: each *TestMode forces its blend so the matching offset can be tuned. No runtime effect.
	[ExportSubgroup("Test (Editor)")]
	[Export] public bool AdsTestMode = false;
	[Export] public bool CrouchTestMode = false;
	[Export] public bool CantedTestMode = false;
	[Export(PropertyHint.Range, "0.1,5,0.05")] public float AdsCalibrationDistance = 1.0f;
	[Export(PropertyHint.Range, "0.001,0.05,0.0005")] public float AdsCalibrationSize = 0.004f;
	[Export] public Color AdsCalibrationColor = new(1f, 0f, 0f, 1f);

	[ExportGroup("Tracer")]
	[Export] public bool TracerEnabled = true;
	/// <summary>Spawn a visible tracer every Nth fired round (1 = every shot).</summary>
	[Export(PropertyHint.Range, "1,10,1")] public int TracerEveryNShots = 2;
	[Export(PropertyHint.Range, "0.002,0.05,0.001")] public float TracerWidth = 0.006f;
	[Export] public Color TracerColor = new(2.5f, 1.6f, 0.5f, 1f);
	[Export(PropertyHint.Range, "20,300,5")] public float TracerSpeed = 80f;
	[Export(PropertyHint.Range, "0.2,5,0.1")] public float TracerStreakLength = 2f;
	private int _tracerShotCount;
	/// <summary>True on every Nth fired round (<see cref="TracerEveryNShots"/>); advances the shot counter, so call exactly once per fired round.</summary>
	public bool ShouldSpawnTracer() => TracerEnabled && _tracerShotCount++ % Mathf.Max(1, TracerEveryNShots) == 0;

	[ExportGroup("Debug")]
	[Export] public bool LogEvents = true;

	public override void _ValidateProperty(Godot.Collections.Dictionary property)
	{
		string name = (string)property["name"];
		if (AnimProps.Contains(name))
		{
			var player = GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);
			if (player == null)
				return;
			property["hint"] = (int)PropertyHint.Enum;
			property["hint_string"] = string.Join(",", player.GetAnimationList());
		}
		else if (EventProps.Contains(name))
		{
			var ep = GetNodeOrNull<AnimationPlayer>(EventPlayerPath);
			if (ep == null)
				return;
			property["hint"] = (int)PropertyHint.Enum;
			property["hint_string"] = string.Join(",", ep.GetAnimationList());
		}
		else if (name == nameof(AudioBus))
		{
			var buses = new string[AudioServer.BusCount];
			for (int i = 0; i < buses.Length; i++)
				buses[i] = AudioServer.GetBusName(i);
			property["hint"] = (int)PropertyHint.Enum;
			property["hint_string"] = string.Join(",", buses);
		}
	}

	// Player body that ejected casings/dropped mags must not collide with. Set by NetworkPlayer; auto-resolved as fallback.
	public CollisionObject3D OwnerBody;

	// On the FPS viewmodel weapon: reproject prop spawn transforms from the SubViewport's own world into the main world
	// via worldCam * viewmodelCam^-1 * local. Null on TPS weapons (already in the main world).
	public Camera3D RemapFromCamera;
	public Camera3D RemapToCamera;

	private const string TreeNodeName = "AnimationTree";
	private static readonly StringName _pActionRequest = "parameters/Action/request";
	private AnimationTree _tree;
	private AnimationPlayer _player;
	private AnimationPlayer _eventPlayer;
	private AnimationNodeAnimation _actionAnim;
	private Skeleton3D _skeleton;
	private int _fireSelectorBone = -1;
	private int _ejectionBone = -1;
	private FireSelectorModifier _fireSelectorModifier;
	private Node3D _mainMag;
	private Node3D _reserveMag;
	private Bullet[] _bulletPool;
	private int _bulletPoolIdx;
	private AudioStreamPlayer3D _audioPlayer;
	private AudioStreamPlayer3D[] _audioPool;
	private int _audioVoiceIdx;
	private AnimatedMagazin _mainMagAnim;
	private AnimatedMagazin _reserveMagAnim;

	public override void _Ready()
	{
		if (Engine.IsEditorHint())
			return;
		BuildAnimationTree();
		BuildMuzzleSmoke();
		BuildMuzzleFlash();
	}

	private bool _weaponActive;

	/// <summary>Enables animation by switching the tree callback to Idle. Inactive instances stay Manual (frozen, zero cost).</summary>
	public void ActivateWeapon() { _weaponActive = true; ApplyWeaponActive(); }

	/// <summary>Freezes animation (tree back to Manual). See <see cref="ActivateWeapon"/>.</summary>
	public void DeactivateWeapon() { _weaponActive = false; ApplyWeaponActive(); }

	private void ApplyWeaponActive()
	{
		if (_tree == null)
			return;
		_tree.CallbackModeProcess = _weaponActive
			? AnimationMixer.AnimationCallbackModeProcess.Idle
			: AnimationMixer.AnimationCallbackModeProcess.Manual;
	}

	private void BuildAnimationTree()
	{
		_player = GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);
		_eventPlayer = GetNodeOrNull<AnimationPlayer>(EventPlayerPath);
		_audioPlayer = GetNodeOrNull<AudioStreamPlayer3D>(AudioPlayerPath);
		BuildAudioPool();
		OwnerBody ??= FindOwnerBody();
		_skeleton = GetNodeOrNull<Skeleton3D>("Skeleton3D");
		_fireSelectorBone = _skeleton?.FindBone("Fire_Selector") ?? -1;
		_ejectionBone = _skeleton?.FindBone("Eject_Casing") ?? -1;
		_mainMag = GetNodeOrNull<Node3D>(MainMagPath);
		_reserveMag = GetNodeOrNull<Node3D>(ReserveMagPath);
		if (_reserveMag != null)
			_reserveMag.Visible = false;
		if (_eventPlayer != null)
		{
			Callable cb = Callable.From<StringName>(OnEventAnimFinished);
			if (!_eventPlayer.IsConnected(AnimationMixer.SignalName.AnimationFinished, cb))
				_eventPlayer.AnimationFinished += OnEventAnimFinished;
		}
		if (LogEvents)
		{
			Dbg.Print($"[WeaponAnimation] MainMag '{MainMagPath}' -> {(_mainMag != null ? _mainMag.GetPath() : "NULL")}");
			Dbg.Print($"[WeaponAnimation] ReserveMag '{ReserveMagPath}' -> {(_reserveMag != null ? _reserveMag.GetPath() : "NULL")}");
			Dbg.Print($"[WeaponAnimation] EventPlayer '{EventPlayerPath}' -> {(_eventPlayer != null ? "OK" : "NULL")}, AudioPlayer -> {(_audioPlayer != null ? "OK" : "NULL")}");
		}

		_tree = GetNodeOrNull<AnimationTree>(TreeNodeName);
		if (_tree?.TreeRoot is not AnimationNodeBlendTree bt || !bt.HasNode("Action"))
		{
			GD.PushWarning("[WeaponAnimation] No AnimationTree with an 'Action' node found in scene.");
			return;
		}
		GenerateWeaponRestPose();
		AssignTreeAnimations(bt);
		_tree.AnimPlayer = _tree.GetPathTo(_player);
		_actionAnim = bt.GetNode("ActionAnim") as AnimationNodeAnimation;
		_tree.Active = true;
		ApplyWeaponActive();

		if (_skeleton != null && _fireSelectorModifier == null)
		{
			_fireSelectorModifier = new FireSelectorModifier { Name = "FireSelectorModifier", Weapon = this };
			_skeleton.AddChild(_fireSelectorModifier);
		}

		_mainMagAnim = _mainMag?.GetNodeOrNull<AnimatedMagazin>("AnimatedMagazin");
		_reserveMagAnim = _reserveMag?.GetNodeOrNull<AnimatedMagazin>("AnimatedMagazin");

		BuildBulletPool();
		SetFireMode(ActualFireMode);
		ResolveFireSelector();
	}


	private void GenerateWeaponRestPose()
	{
		if (_player == null || _skeleton == null || _player.HasAnimation("common/A_TFA_WEP_AR_Reference"))
			return;
		string refKey = null;
		foreach (StringName lib in _player.GetAnimationLibraryList())
		{
			var library = _player.GetAnimationLibrary(lib);
			if (library == null)
				continue;
			foreach (StringName a in library.GetAnimationList())
			{ refKey = lib.ToString().Length > 0 ? $"{lib}/{a}" : a.ToString(); break; }
			if (refKey != null)
				break;
		}
		if (refKey == null || !_player.HasAnimation(refKey))
			return;
		Animation src = _player.GetAnimation(refKey);
		var rest = new Animation { Length = 0.25f, LoopMode = Animation.LoopModeEnum.Linear };
		for (int t = 0; t < src.GetTrackCount(); t++)
		{
			var type = src.TrackGetType(t);
			if (type != Animation.TrackType.Rotation3D && type != Animation.TrackType.Position3D && type != Animation.TrackType.Scale3D)
				continue;
			var path = src.TrackGetPath(t);
			if (path.GetSubNameCount() < 1)
				continue;
			var bi = _skeleton.FindBone(path.GetSubName(0));
			if (bi < 0)
				continue;
			var r = _skeleton.GetBoneRest(bi);
			var ti = rest.AddTrack(type);
			rest.TrackSetPath(ti, path);
			if (type == Animation.TrackType.Rotation3D)
				rest.RotationTrackInsertKey(ti, 0.0, r.Basis.GetRotationQuaternion());
			else if (type == Animation.TrackType.Position3D)
				rest.PositionTrackInsertKey(ti, 0.0, r.Origin);
			else
				rest.ScaleTrackInsertKey(ti, 0.0, r.Basis.Scale);
		}
		var commonLib = _player.HasAnimationLibrary("common") ? _player.GetAnimationLibrary("common") : new AnimationLibrary();
		if (commonLib.HasAnimation("A_TFA_WEP_AR_Reference"))
			commonLib.RemoveAnimation("A_TFA_WEP_AR_Reference");
		commonLib.AddAnimation("A_TFA_WEP_AR_Reference", rest);
		if (!_player.HasAnimationLibrary("common"))
			_player.AddAnimationLibrary("common", commonLib);
		if (ReferencePose == null || string.IsNullOrEmpty(ReferencePose.ToString()))
			ReferencePose = "common/A_TFA_WEP_AR_Reference";
	}

	public void SetMagazineFill(float fill01)
	{
		_mainMagAnim?.SetFill(fill01);
		_reserveMagAnim?.SetFill(fill01);
	}

	private CollisionObject3D FindOwnerBody()
	{
		Node n = GetParent();
		while (n != null)
		{
			if (n is CharacterBody3D cb)
				return cb;
			n = n.GetParent();
		}
		return null;
	}

	private void BuildBulletPool()
	{
		if (Engine.IsEditorHint() || EjectCasingScene == null || EjectCasingPoolSize <= 0)
			return;
		if (_bulletPool != null)
			foreach (var b in _bulletPool)
				b?.QueueFree();
		_bulletPool = new Bullet[EjectCasingPoolSize];
		for (var i = 0; i < EjectCasingPoolSize; i++)
		{
			if (EjectCasingScene.Instantiate() is not Bullet b)
				continue;
			b.Visible = false;
			b.Freeze = true;
			if (OwnerBody != null)
				b.AddCollisionExceptionWith(OwnerBody);
			_bulletPool[i] = b;
			GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, b);
		}
		_bulletPoolIdx = 0;
	}

	private void AssignTreeAnimations(AnimationNodeBlendTree bt)
	{
		if (_player == null)
			return;
		if (bt.GetNode("Reference") is AnimationNodeAnimation r && HasAnim(ReferencePose))
			r.Animation = ReferencePose;
	}

	public void SetFireMode(string name)
	{
		if (FireModes == null || string.IsNullOrEmpty(name) || !FireModes.ContainsKey(name))
			return;
		ActualFireMode = name;
		_fireSelectorTime = FireModes[name].AsSingle();
	}

	public void CycleFireMode()
	{
		if (FireModes == null || FireModes.Count == 0)
			return;
		var keys = new List<string>();
		foreach (var k in FireModes.Keys)
			keys.Add(k.AsString());
		int cur = keys.IndexOf(ActualFireMode);
		SetFireMode(keys[(cur + 1) % keys.Count]);
	}

	private Animation _fireSelectorAnim;
	private int[] _fireSelectorTracks = [];
	private float _fireSelectorTime;

	/// <summary>Pre-resolves the FireModeStates animation, its Fire_Selector tracks, and the current mode time so the per-frame pass stays allocation-free.</summary>
	private void ResolveFireSelector()
	{
		_fireSelectorAnim = null;
		_fireSelectorTracks = [];
		if (_skeleton == null || _fireSelectorBone < 0 || !HasAnim(FireModeStates))
			return;
		_fireSelectorAnim = _player.GetAnimation(FireModeStates);
		var tracks = new List<int>();
		for (int i = 0; i < _fireSelectorAnim.GetTrackCount(); i++)
			if (((string)_fireSelectorAnim.TrackGetPath(i)).Contains("Fire_Selector"))
				tracks.Add(i);
		_fireSelectorTracks = [.. tracks];
		_fireSelectorTime = FireModes != null && FireModes.ContainsKey(ActualFireMode)
			? FireModes[ActualFireMode].AsSingle() : 0f;
	}

	/// <summary>Overrides the Fire_Selector bone to match <see cref="ActualFireMode"/>. Called by <see cref="FireSelectorModifier"/> post-mix so it survives the tree's bone writes.</summary>
	public void ApplyFireSelectorPose()
	{
		var anim = _fireSelectorAnim;
		if (anim == null || _skeleton == null)
			return;
		float t = _fireSelectorTime;
		foreach (int i in _fireSelectorTracks)
		{
			switch (anim.TrackGetType(i))
			{
				case Animation.TrackType.Rotation3D:
					_skeleton.SetBonePoseRotation(_fireSelectorBone, anim.RotationTrackInterpolate(i, t));
					break;
				case Animation.TrackType.Position3D:
					_skeleton.SetBonePosePosition(_fireSelectorBone, anim.PositionTrackInterpolate(i, t));
					break;
				case Animation.TrackType.Scale3D:
					_skeleton.SetBonePoseScale(_fireSelectorBone, anim.ScaleTrackInterpolate(i, t));
					break;
			}
		}
	}

	private void EditorRebuildTree()
	{
		if (!Engine.IsEditorHint())
			return;
		NotifyPropertyListChanged();
		_player = GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);
		if (_player == null)
		{ GD.PushWarning("[WeaponAnimation] AnimationPlayerPath unresolved"); return; }
		var tree = GetNodeOrNull<AnimationTree>(TreeNodeName);
		var bt = tree?.TreeRoot as AnimationNodeBlendTree;
		if (tree == null || bt == null)
		{ GD.PushWarning("[WeaponAnimation] No AnimationTree in scene."); return; }
		AssignTreeAnimations(bt);
		tree.AnimPlayer = tree.GetPathTo(_player);
		Dbg.Print("[WeaponAnimation] Animations assigned — Ctrl+S to save.");
	}

	private bool HasAnim(StringName n) =>
		n != null && !string.IsNullOrEmpty(n.ToString()) && _player != null && _player.HasAnimation(n);

	private void ResetMagazines()
	{
		if (_mainMag != null)
			_mainMag.Visible = true;
		if (_reserveMag != null)
			_reserveMag.Visible = false;
	}

	// Safety net: restore mags to idle when any event clip finishes, in case the end-of-clip restore key is missed.
	private void OnEventAnimFinished(StringName _) => ResetMagazines();

	public void PlayAction(StringName anim, string eventAnim = null)
	{
		ResetMagazines();
		if (_tree != null && _actionAnim != null && HasAnim(anim))
		{
			_actionAnim.Animation = anim;
			_tree.Set(_pActionRequest, (int)AnimationNodeOneShot.OneShotRequest.Fire);
		}
		if (eventAnim != null && _eventPlayer != null && _eventPlayer.HasAnimation(eventAnim))
		{
			// Re-firing the same clip must restart from 0 so its t=0 keys (shot sound, casing) replay.
			if (_eventPlayer.CurrentAnimation == eventAnim)
				_eventPlayer.Stop();
			_eventPlayer.Play(eventAnim);
		}
		else if (eventAnim != null)
			GD.PrintErr($"[WeaponAnimation] event skip: player={_eventPlayer != null}, anim='{eventAnim}', has={_eventPlayer?.HasAnimation(eventAnim)}");
	}

	private void EnsureTree()
	{
		if (_tree == null || _actionAnim == null)
			BuildAnimationTree();
	}

	private void EditorPlay(StringName anim, string eventAnim = null)
	{
		EnsureTree();
		ActivateWeapon();   // editor preview: tree self-runs on Idle
		PlayAction(anim, eventAnim);
	}

	public bool Aiming { get; set; }
	private bool IsTPS => Mode == WeaponMode.TPS;
	private StringName Aimed(StringName hip, StringName aimed) =>
		Aiming && !string.IsNullOrEmpty(aimed?.ToString()) ? aimed : hip;

	public void Fire() => PlayAction(IsTPS ? TpFire : FpFire, EvFire);
	public void Reload() => PlayAction(IsTPS ? Aimed(TpReload, TpReloadAimed) : Aimed(FpReload, FpReloadAimed), EvReload);
	public void ReloadEmpty() => PlayAction(IsTPS ? Aimed(TpReloadEmpty, TpReloadEmptyAimed) : Aimed(FpReloadEmpty, FpReloadEmptyAimed), EvReloadEmpty);
	public void ReloadQuick() => PlayAction(IsTPS ? Aimed(TpReloadQuick, TpReloadQuickAimed) : Aimed(FpReloadQuick, FpReloadQuickAimed), EvReloadQuick);
	public void Inspect() => PlayAction(IsTPS ? TpInspect : FpInspect, EvInspect);
	public void MagCheck() => PlayAction(IsTPS ? Aimed(TpMagCheck, TpMagCheckAimed) : Aimed(FpMagCheck, FpMagCheckAimed), EvMagCheck);
	public void Equip() => PlayAction(IsTPS ? TpEquip : FpEquip, EvEquip);
	public void ClearJamMagSwipe() => PlayAction(IsTPS ? TpClearJamMagSwipe : FpClearJamMagSwipe, EvClearJamMagSwipe);
	public void ClearJamRack() => PlayAction(IsTPS ? TpClearJamRack : FpClearJamRack, EvClearJamRack);
	public string[] GetPreloadList()
	{
		var paths = new HashSet<string>();

		void Add(AudioStream[] arr)
		{
			if (arr == null)
				return;
			foreach (var s in arr)
			{
				if (s != null && !string.IsNullOrEmpty(s.ResourcePath))
					paths.Add(s.ResourcePath);
			}
		}

		Add(AudioFire);
		Add(AudioFireTail);
		Add(AudioEmptyCasing);
		Add(AudioClick);
		Add(AudioFoleyCloth);
		Add(AudioBoltOpen);
		Add(AudioBoltClose);
		Add(AudioGunSmack);
		Add(AudioMalfunction);
		Add(AudioMagInsert);
		Add(AudioMagRemoveFull);
		Add(AudioMagRemoveEmpty);

		if (EjectCasingScene != null && !string.IsNullOrEmpty(EjectCasingScene.ResourcePath))
			paths.Add(EjectCasingScene.ResourcePath);

		return [.. paths];
	}

	public Dictionary<string, string[]> GetAttachments()
	{
		var groups = new Dictionary<string, List<string>>();

		void Scan(Node node)
		{
			foreach (Node child in node.GetChildren())
			{
				if (child is WeaponAttachment wa)
				{
					var key = wa.Group.ToString();
					if (!groups.ContainsKey(key))
						groups[key] = [];
					groups[key].Add(GetPathTo(wa).ToString());
				}
				Scan(child);
			}
		}

		Scan(this);

		var result = new Dictionary<string, string[]>(groups.Count);
		foreach (var kv in groups)
			result[kv.Key] = kv.Value.ToArray();
		return result;
	}

	private void BuildAudioPool()
	{
		if (_audioPool != null)
			foreach (var p in _audioPool)
				p?.QueueFree();
		_audioPool = null;
		_audioVoiceIdx = 0;
		if (_audioPlayer == null || AudioVoices <= 0)
			return;
		var parent = _audioPlayer.GetParent() ?? this;
		_audioPool = new AudioStreamPlayer3D[AudioVoices];
		for (int i = 0; i < AudioVoices; i++)
		{
			var voice = (AudioStreamPlayer3D)_audioPlayer.Duplicate();
			voice.Name = $"{_audioPlayer.Name}_Voice{i}";
			if (!string.IsNullOrEmpty(AudioBus.ToString()))
				voice.Bus = AudioBus;
			parent.AddChild(voice);
			_audioPool[i] = voice;
		}
	}

	private AudioStreamPlayer3D NextVoice()
	{
		if (_audioPool == null || _audioPool.Length == 0)
			return _audioPlayer;
		for (int i = 0; i < _audioPool.Length; i++)
		{
			var p = _audioPool[(_audioVoiceIdx + i) % _audioPool.Length];
			if (p != null && !p.Playing)
			{
				_audioVoiceIdx = (_audioVoiceIdx + i + 1) % _audioPool.Length;
				return p;
			}
		}
		var voice = _audioPool[_audioVoiceIdx];
		_audioVoiceIdx = (_audioVoiceIdx + 1) % _audioPool.Length;
		return voice;
	}

	private void Log(string msg) { if (LogEvents) Dbg.Print($"[WeaponAnimation] {msg}"); }

	private void PlayRandom(AudioStream[] streams, float volume, [System.Runtime.CompilerServices.CallerMemberName] string label = "")
	{
		if (streams == null || streams.Length == 0)
		{
			Log($"{label} — no AudioStream assigned");
			return;
		}
		var player = NextVoice();
		if (player == null)
			return;
		player.Stream = streams[GD.RandRange(0, streams.Length - 1)];
		player.VolumeDb = volume > 0f ? Mathf.LinearToDb(volume) : -80f;
		player.Play();
		Log($"{label} (vol={volume:0.##})");
	}

	public virtual void PlayAudioClick() => PlayRandom(AudioClick, VolumeClick);
	public virtual void PlayAudioFire() => PlayRandom(AudioFire, VolumeFire);
	public virtual void PlayAudioFireTail() => PlayRandom(AudioFireTail, VolumeFireTail);
	public virtual void PlayAudioBoltClose() => PlayRandom(AudioBoltClose, VolumeBoltClose);
	public virtual void PlayAudioBoltOpen() => PlayRandom(AudioBoltOpen, VolumeBoltOpen);
	public virtual void PlayAudioGunSmack() => PlayRandom(AudioGunSmack, VolumeGunSmack);
	public virtual void PlayAudioFoleyCloth() => PlayRandom(AudioFoleyCloth, VolumeFoleyCloth);
	public virtual void PlayAudioMagInsert() => PlayRandom(AudioMagInsert, VolumeMagInsert);
	public virtual void PlayAudioMagRemoveFull() => PlayRandom(AudioMagRemoveFull, VolumeMagRemoveFull);
	public virtual void PlayAudioMagRemoveEmpty() => PlayRandom(AudioMagRemoveEmpty, VolumeMagRemoveEmpty);
	public virtual void PlayAudioEmptyCasing() => PlayRandom(AudioEmptyCasing, VolumeEmptyCasing);
	public virtual void PlayAudioMalfunction() => PlayRandom(AudioMalfunction, VolumeMalfunction);
	// Reprojects a viewmodel-world transform into the main world (identity on TPS weapons), preserving apparent screen position.
	private Transform3D RemapToWorld(Transform3D vmSpace)
	{
		if (RemapFromCamera != null && GodotObject.IsInstanceValid(RemapFromCamera)
			&& RemapToCamera != null && GodotObject.IsInstanceValid(RemapToCamera))
			return RemapToCamera.GlobalTransform * RemapFromCamera.GlobalTransform.AffineInverse() * vmSpace;
		return vmSpace;
	}

	public virtual void EjectCasing()
	{
		Log("EjectCasing");
		if (_bulletPool == null || _skeleton == null || _ejectionBone < 0)
			return;
		var bullet = _bulletPool[_bulletPoolIdx];
		_bulletPoolIdx = (_bulletPoolIdx + 1) % _bulletPool.Length;
		if (bullet == null)
			return;

		var boneWorld = RemapToWorld(_skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_ejectionBone));
		var randEuler = new Vector3(
			Mathf.DegToRad((float)GD.RandRange(EjectMinRotation.X, EjectMaxRotation.X)),
			Mathf.DegToRad((float)GD.RandRange(EjectMinRotation.Y, EjectMaxRotation.Y)),
			Mathf.DegToRad((float)GD.RandRange(EjectMinRotation.Z, EjectMaxRotation.Z))
		);
		var dir = (boneWorld.Basis * Basis.FromEuler(randEuler) * EjectDirectionLocal).Normalized();
		var randomUnit = new Vector3((float)GD.RandRange(-1f, 1f), (float)GD.RandRange(-1f, 1f), (float)GD.RandRange(-1f, 1f)).Normalized();

		bullet.Launch(
			boneWorld,
			dir * (float)GD.RandRange(EjectMinForce, EjectMaxForce) + randomUnit,
			dir * Mathf.DegToRad(EjectRotationSpeed),
			EjectLifetime
		);
	}
	public virtual void HideMainMag()
	{
		Log(_mainMag != null ? "HideMainMag" : "HideMainMag — _mainMag NULL");
		if (_mainMag != null)
			_mainMag.Visible = false;
	}
	public virtual void ShowMainMag()
	{
		Log(_mainMag != null ? "ShowMainMag" : "ShowMainMag — _mainMag NULL");
		if (_mainMag != null)
			_mainMag.Visible = true;
	}
	public virtual void ShowReserveMag()
	{
		Log(_reserveMag != null ? "ShowReserveMag" : "ShowReserveMag — _reserveMag NULL");
		if (_reserveMag != null)
			_reserveMag.Visible = true;
	}
	public virtual void HideReserveMag()
	{
		Log(_reserveMag != null ? "HideReserveMag" : "HideReserveMag — _reserveMag NULL");
		if (_reserveMag != null)
			_reserveMag.Visible = false;
	}
	public virtual void DropMagazine()
	{
		if (_mainMag == null)
		{ Log("DropMagazine — _mainMag NULL"); return; }

		MeshInstance3D srcMesh = FindMesh(_mainMag);
		Transform3D spawn = RemapToWorld(srcMesh != null ? srcMesh.GlobalTransform : _mainMag.GlobalTransform);

		RigidBody3D mag = DropMagazineScene?.Instantiate() as RigidBody3D ?? BuildRuntimeMagBody(srcMesh);
		if (mag == null)
		{ Log("DropMagazine — no DropMagazineScene and no mag mesh to build from"); return; }

		// The dropped mag must collide and simulate (in-socket mags default to frozen/non-colliding).
		if (mag is AnimatedMagazin am)
			am.CollisionEnabled = true;
		GetTree().CurrentScene.AddChild(mag);
		mag.Freeze = false;
		mag.CollisionLayer = 1u << 3;   // DEBRIS layer; players don't collide with dropped mags
		mag.CollisionMask = 1u;         // collide with WORLD only so it rests on the floor
		mag.GlobalTransform = spawn;
		mag.LinearVelocity = spawn.Basis.Y.Normalized() * DropImpulseForce;
		Vector3 axis = new Vector3(
			(float)GD.RandRange(-1.0, 1.0),
			(float)GD.RandRange(-1.0, 1.0),
			(float)GD.RandRange(-1.0, 1.0)).Normalized();
		mag.AngularVelocity = axis * Mathf.DegToRad(DropRotationForce);
		Log($"DropMagazine @ {spawn.Origin}");
		GetTree().CreateTimer(DropMagazineLifetime).Timeout += () => { if (IsInstanceValid(mag)) mag.QueueFree(); };
	}

	private static MeshInstance3D FindMesh(Node root)
	{
		if (root is MeshInstance3D mi)
			return mi;
		foreach (Node child in root.GetChildren())
		{
			var found = FindMesh(child);
			if (found != null)
				return found;
		}
		return null;
	}

	private static RigidBody3D BuildRuntimeMagBody(MeshInstance3D srcMesh)
	{
		if (srcMesh?.Mesh == null)
			return null;
		var body = new RigidBody3D();
		body.AddChild(new MeshInstance3D { Mesh = srcMesh.Mesh });
		body.AddChild(new CollisionShape3D { Shape = srcMesh.Mesh.CreateConvexShape() });
		return body;
	}

	// Main-world position of the active barrel tip for tracers/muzzle FX (reprojected for the FPS viewmodel, identity on TPS). Resolved per call so handguard swaps are picked up.
	public Vector3 GetMuzzleWorldPosition()
	{
		Node3D tip = FindMuzzleTip(this);
		Vector3 pos = tip != null ? tip.GlobalPosition : GlobalPosition;
		return RemapToWorld(new Transform3D(Basis.Identity, pos)).Origin;
	}

	// Finds the active variant's visible "Muzzle" node and returns its "SOCKET_Emitter" tip; gates on the Muzzle's visibility (the emitter may hang under a hidden silencer mesh).
	private static Node3D FindMuzzleTip(Node root)
	{
		Node3D muzzle = FindVisibleNamed(root, "Muzzle");
		if (muzzle == null)
			return FindVisibleNamed(root, "SOCKET_Emitter");
		return FindNamed(muzzle, "SOCKET_Emitter") ?? muzzle;
	}

	private static Node3D FindVisibleNamed(Node root, string name)
	{
		if (root is Node3D n3 && root.Name == name && n3.IsVisibleInTree())
			return n3;
		foreach (Node child in root.GetChildren())
		{
			var found = FindVisibleNamed(child, name);
			if (found != null)
				return found;
		}
		return null;
	}

	private static Node3D FindNamed(Node root, string name)
	{
		if (root is Node3D n3 && root.Name == name)
			return n3;
		foreach (Node child in root.GetChildren())
		{
			var found = FindNamed(child, name);
			if (found != null)
				return found;
		}
		return null;
	}

	private GpuParticles3D _muzzleSmoke;
	private Transform3D _pendingSmokeMuzzle;
	private int _smokeBurstGen;
	private const float SmokeTrailDelay = 0.13f;

	// Muzzle smoke puff. Lives in the MAIN world (not the camera-anchored FPS SubViewport) with LocalCoords=false,
	// so particles hang where fired; EmitSmokePuff teleports the emitter to the remapped muzzle.
	private void BuildMuzzleSmoke()
	{
		if (NetMain.Instance?.Cli?.Mode == NetMode.Server)
			return;
		var mat = ResourceLoader.Load<Material>("res://fx/smoke/smoke.tres");
		if (mat == null)
			return;

		var ppm = new ParticleProcessMaterial
		{
			Direction = new Vector3(0, 0.12f, -1),   // shoves out of the barrel, only slightly upward
			Spread = 28f,                            // wider cone so edges read as a wispy plume, not a clean sphere
			InitialVelocityMin = 0.6f,
			InitialVelocityMax = 1.5f,               // forward push elongates the puff into a barrel plume
			Gravity = new Vector3(0, 1.1f, 0),       // buoys upward AFTER the forward shove, not straight up
			LinearAccelMin = 0f,
			LinearAccelMax = 0f,
			DampingMin = 1.4f,
			DampingMax = 2.6f,                        // forward shove dies fast, then buoyancy lifts the puff
			ScaleMin = 2.7f,
			ScaleMax = 3.8f,                          // one big billowing mass
			ScaleCurve = MakeCurve((0f, 0.4f), (0.25f, 1f), (1f, 2.2f)),    // blooms out fast then keeps growing as it thins
			Color = new Color(0.6f, 0.61f, 0.64f, 1f),                     // light cool grey, not dark tobacco
			AlphaCurve = MakeCurve((0f, 0f), (0.12f, 0.065f), (1f, 0f)),   // thin, semi-transparent; slow fade-out
			AngleMin = -360f,
			AngleMax = 360f,
			AnimOffsetMax = 1f,
			InheritVelocityRatio = 0f,                // emitter is teleported per shot; inheriting that delta would fling particles
		};
		ppm.SetParticleFlag(ParticleProcessMaterial.ParticleFlags.DampingAsFriction, true);

		_muzzleSmoke = new GpuParticles3D
		{
			Name = "MuzzleSmoke",
			Amount = 5,                                // a few staggered cards form a trailing plume, not one round ball
			Lifetime = 1.0,
			OneShot = true,
			Emitting = false,
			LocalCoords = false,
			Explosiveness = 0.7f,                      // slight stagger = a short forward trail, not an instant round pop
			ProcessMaterial = ppm,
			DrawPass1 = new QuadMesh { Size = new Vector2(0.6f, 0.6f) },
			MaterialOverride = mat,
		};
		GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, _muzzleSmoke);
	}

	private void EmitSmokePuff()
	{
		if (_muzzleSmoke == null || !GodotObject.IsInstanceValid(_muzzleSmoke) || !_muzzleSmoke.IsInsideTree())
			return;
		_muzzleSmoke.GlobalTransform = _pendingSmokeMuzzle;
		_muzzleSmoke.Restart();
	}

	/// <summary>Records the muzzle transform and re-arms the trailing smoke puff, delayed by <see cref="SmokeTrailDelay"/> so full-auto yields one cloud after the last round. Cosmetic.</summary>
	public void MuzzleSmoke()
	{
		if (_muzzleSmoke == null || !GodotObject.IsInstanceValid(_muzzleSmoke))
			BuildMuzzleSmoke();
		if (_muzzleSmoke == null || !_muzzleSmoke.IsInsideTree())
			return;
		Node3D tip = FindMuzzleTip(this);
		Transform3D muzzle = RemapToWorld(tip != null ? tip.GlobalTransform : GlobalTransform);
		_pendingSmokeMuzzle = new Transform3D(muzzle.Basis.Orthonormalized(), muzzle.Origin);

		int gen = ++_smokeBurstGen;
		var timer = GetTree().CreateTimer(SmokeTrailDelay);
		timer.Timeout += () =>
		{
			if (gen == _smokeBurstGen)
				EmitSmokePuff();
		};
	}

	private static CurveTexture MakeCurve(params (float X, float Y)[] points)
	{
		var c = new Curve();
		foreach (var p in points)
			c.AddPoint(new Vector2(p.X, p.Y));
		return new CurveTexture { Curve = c };
	}

	private GpuParticles3D _muzzleFlash;
	private OmniLight3D _muzzleLight;
	private const string MuzzleFlameTexture = "res://weapons/AR15/animations/weapon/textures/T_TFA_MuzzleFlash_Flame_E.EXR";
	private const float MuzzleFlashSize = 0.52f;
	private const float MuzzleFlashEnergy = 0.75f;
	private const float MuzzleFlashSaturation = 0.45f;
	private const float MuzzleFlashLightEnergy = 1.5f;
	private const int MuzzleFlashHFrames = 8;
	private const int MuzzleFlashVFrames = 8;
	private static readonly Color MuzzleFlashColor = new(1f, 0.72f, 0.35f);

	// Textured muzzle flash: additive flame card + point-light pulse. Lives in the MAIN world (not the FPS SubViewport)
	// and is teleported to the reprojected muzzle per shot so the light hits the real environment.
	private void BuildMuzzleFlash()
	{
		if (NetMain.Instance?.Cli?.Mode == NetMode.Server)
			return;
		var tex = ResourceLoader.Load<Texture2D>(MuzzleFlameTexture);
		var shader = GD.Load<Shader>("res://fx/muzzle/muzzle_flash_add.gdshader");
		if (tex == null || shader == null)
			return;

		var mat = new ShaderMaterial { Shader = shader };
		mat.SetShaderParameter("texture_albedo", tex);
		mat.SetShaderParameter("particles_anim_h_frames", MuzzleFlashHFrames);
		mat.SetShaderParameter("particles_anim_v_frames", MuzzleFlashVFrames);
		mat.SetShaderParameter("energy", MuzzleFlashEnergy);
		mat.SetShaderParameter("saturation", MuzzleFlashSaturation);

		var ppm = new ParticleProcessMaterial
		{
			Direction = new Vector3(0, 0, -1),
			Spread = 0f,
			InitialVelocityMin = 0f,
			InitialVelocityMax = 0f,
			Gravity = Vector3.Zero,
			ScaleMin = 0.85f,
			ScaleMax = 1.25f,
			AngleMin = -180f,
			AngleMax = 180f,
			AnimOffsetMin = 0f,
			AnimOffsetMax = 1f,
			Color = Colors.White,
			AlphaCurve = MakeCurve((0f, 0.2f), (0.55f, 0.2f), (1f, 0f)),
		};

		_muzzleLight = new OmniLight3D
		{
			Name = "MuzzleLight",
			LightColor = MuzzleFlashColor,
			LightEnergy = 0f,
			OmniRange = 6.0f,
			ShadowEnabled = false,
			Visible = false,
		};

		_muzzleFlash = new GpuParticles3D
		{
			Name = "MuzzleFlash",
			Amount = 1,
			Lifetime = 0.045,
			OneShot = true,
			Emitting = false,
			LocalCoords = false,
			Explosiveness = 1f,
			ProcessMaterial = ppm,
			DrawPass1 = new QuadMesh { Size = new Vector2(MuzzleFlashSize, MuzzleFlashSize), Material = mat },
		};
		_muzzleFlash.AddChild(_muzzleLight);
		GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, _muzzleFlash);
	}

	/// <summary>Per-shot muzzle flash: teleports the main-world flame card + light to the reprojected barrel tip and pulses the light. Cosmetic.</summary>
	public void MuzzleFlash()
	{
		if (_muzzleFlash == null || !GodotObject.IsInstanceValid(_muzzleFlash))
			BuildMuzzleFlash();
		if (_muzzleFlash == null || !_muzzleFlash.IsInsideTree())
			return;
		Node3D tip = FindMuzzleTip(this);
		Transform3D m = RemapToWorld(tip != null ? tip.GlobalTransform : GlobalTransform);
		_muzzleFlash.GlobalTransform = new Transform3D(m.Basis.Orthonormalized(), m.Origin);
		_muzzleFlash.Restart();
		_muzzleLight.Visible = true;
		_muzzleLight.LightEnergy = MuzzleFlashLightEnergy;
		var t = CreateTween();
		t.TweenProperty(_muzzleLight, "light_energy", 0f, 0.05);
		t.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(_muzzleLight)) _muzzleLight.Visible = false; }));
	}
}
