using Godot;
using System.Collections.Generic;

namespace Vantix.Fx;

/// <summary>
/// Client-side, cosmetic weapon audio bank. Plays shoot/reload/dry-fire sounds triggered by the
/// controller via <see cref="NetworkPlayer"/>. Clip paths live per weapon in <see cref="WeaponStats"/>
/// and are passed in on playback, so swapping weapon swaps all sounds.
/// A shot layers Body (main, reverb bus) + Mech (dry) + Tail (reverb), sharing one per-shot pitch roll.
/// Reverb uses three environment buses (Outdoor/Indoor/Tunnel) selected via <see cref="ReverbEnv"/>.
/// Clips load async into a process-wide cache shared by all players (<see cref="Preload"/> warms the
/// starting weapon); not-yet-loaded clips are silent. Mirrors <see cref="FootstepAudio"/> for local
/// (non-positional) vs remote (3D + distant clips + occlusion) playback.
/// </summary>
public partial class WeaponAudio : Node3D
{
	[Export] public bool IsLocalPlayer = true;
	[Export] public StringName Bus = "Master";
	[Export(PropertyHint.Range, "1,12,1")] public int PoolSize = 6;

	[ExportGroup("Mixing")]
	[Export(PropertyHint.Range, "0,0.3,0.01")] public float PitchRandomness = 0.06f;
	[Export] public float MechLayerVolumeDb = -8f;
	[Export] public float TailLayerVolumeDb = -5f;
	[Export] public float VolumeDb3D = 6f;

	[ExportGroup("3D audibility (remote players only)")]
	[Export] public float MaxHearDistance = 120f;
	[Export] public float ReloadMaxHearDistance = 24f;
	[Export] public float DryFireMaxHearDistance = 14f;
	[Export(PropertyHint.Range, "1,40,0.5")] public float UnitSize = 18f;

	[ExportGroup("Occlusion (dampen blocked enemy shots)")]
	[Export] public bool OcclusionEnabled = true;
	[Export] public uint OcclusionMask = 1;
	[Export] public float OcclusionLowPassHz = 1400f;
	[Export] public float OcclusionVolumeDb = -6f;

	[ExportGroup("Reverb (environment-adaptive gunshot tail)")]
	[Export] public bool ReverbEnabled = true;
	[Export(PropertyHint.Range, "0,1,0.05")] public float OutdoorWet = 0.18f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float OutdoorRoom = 0.40f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float IndoorWet = 0.45f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float IndoorRoom = 0.60f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float TunnelWet = 0.70f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float TunnelRoom = 0.85f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float ReverbDamping = 0.40f;

	private const string OccludedBusName = "WeaponOccluded";
	private const string ReverbOutdoorBusName = "WeaponReverbOutdoor";
	private const string ReverbIndoorBusName = "WeaponReverbIndoor";
	private const string ReverbTunnelBusName = "WeaponReverbTunnel";
	private static bool _busesReady;

	private Node[] _pool;
	private int _poolCursor;

	private PhysicsRayQueryParameters3D _occlusionQuery;
	private readonly PhysicsRayQueryResult3D _occlusionResult = new();
	private readonly RandomNumberGenerator _rng = new();

	private static readonly Dictionary<string, AudioStream> _clipCache = new();
	private static readonly HashSet<string> _requested = new();
	private static readonly List<string> _pending = new();

	/// <summary>Disables _Process until a load is in flight — saves per-tick overhead when idle.</summary>
	public override void _Ready() => SetProcess(false);

	/// <summary>Pre-loads all clips referenced by a weapon (typical caller: NetworkPlayer._Ready with the starting weapon).</summary>
	public void Preload(WeaponStats weapon)
	{
		if (weapon == null) return;
		EnsureLoaded(weapon.ShootBodyClips);
		EnsureLoaded(weapon.ShootMechClips);
		EnsureLoaded(weapon.ShootTailClips);
		EnsureLoaded(weapon.ShootDistantClips);
		EnsureLoaded(weapon.ReloadClips);
		EnsureLoaded(weapon.DryFireClips);
	}

	/// <summary>Kicks off threaded loads for any not-yet-loaded paths, deduplicated process-wide.</summary>
	private void EnsureLoaded(string[] paths)
	{
		if (paths == null) return;
		bool added = false;
		foreach (string path in paths)
		{
			if (string.IsNullOrEmpty(path) || _clipCache.ContainsKey(path) || !_requested.Add(path)) continue;
			if (ResourceLoader.LoadThreadedRequest(path) == Error.Ok) { _pending.Add(path); added = true; }
			else GD.PushWarning($"[WeaponAudio] Path not loadable: {path}");
		}
		if (added) SetProcess(true);
	}

	/// <summary>Polls in-flight threaded loads and disables itself once the queue is empty.</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("WeaponAudio._Process");
		for (int i = _pending.Count - 1; i >= 0; i--)
		{
			string path = _pending[i];
			var status = ResourceLoader.LoadThreadedGetStatus(path);
			if (status == ResourceLoader.ThreadLoadStatus.InProgress)
				continue;

			_pending.RemoveAt(i);
			if (status == ResourceLoader.ThreadLoadStatus.Loaded
				&& ResourceLoader.LoadThreadedGet(path) is AudioStream stream)
				_clipCache[path] = stream;
			else
				GD.PushWarning($"[WeaponAudio] Clip load failed: {path} ({status})");
		}
		if (_pending.Count == 0) SetProcess(false);
	}

	/// <summary>Plays a layered shot (Body + Mech + Tail) with environment reverb; remote players also use the distant clip set and occlusion.</summary>
	public void PlayShoot(WeaponStats weapon, Vector3 muzzleWorldPos, ReverbEnv environment)
	{
		if (weapon == null) return;
		EnsureLoaded(weapon.ShootBodyClips);
		EnsureLoaded(weapon.ShootMechClips);
		EnsureLoaded(weapon.ShootTailClips);
		EnsureLoaded(weapon.ShootDistantClips);
		EnsurePool();

		StringName reverbBus = ReverbEnabled ? ReverbBusFor(environment) : Bus;
		bool occluded = !IsLocalPlayer && OcclusionEnabled && IsOccluded(muzzleWorldPos);
		bool distant = !IsLocalPlayer && weapon.ShootDistantClips.Length > 0
			&& ListenerDistance(muzzleWorldPos) >= weapon.DistantCrossoverM;

		StringName shotBus = occluded ? OccludedBusName : reverbBus;
		float occPenalty = occluded ? OcclusionVolumeDb : 0f;
		float pitch = 1f + _rng.RandfRange(-PitchRandomness, PitchRandomness);

		string playedMain;
		if (distant)
		{
			playedMain = EmitLayer(weapon.ShootDistantClips, weapon.ShootVolumeDb + occPenalty,
				shotBus, muzzleWorldPos, pitch, MaxHearDistance);
		}
		else
		{
			playedMain = EmitLayer(weapon.ShootBodyClips, weapon.ShootVolumeDb + occPenalty,
				shotBus, muzzleWorldPos, pitch, MaxHearDistance);
			EmitLayer(weapon.ShootMechClips, weapon.ShootVolumeDb + MechLayerVolumeDb + occPenalty,
				occluded ? (StringName)OccludedBusName : Bus, muzzleWorldPos, pitch, MaxHearDistance);
		}
		if (!occluded)
			EmitLayer(weapon.ShootTailClips, weapon.ShootVolumeDb + TailLayerVolumeDb,
				reverbBus, muzzleWorldPos, pitch, MaxHearDistance);

		if (Dbg.Enabled)
		{
			string ear = IsLocalPlayer ? "local" : "remote";
			string tags = (distant ? " | distant" : "") + (occluded ? " | occluded" : "");
			if (playedMain != null)
				Dbg.Print($"[WeaponAudio] Shoot {weapon.Name} | env={environment} | {ear}{tags} → {System.IO.Path.GetFileName(playedMain)}");
			else
				Dbg.Print($"[WeaponAudio] Shoot {weapon.Name} | env={environment} | {ear}{tags} → clip not loaded yet, silent");
		}
	}

	/// <summary>Plays the reload sound (single layer, dry). Triggered on the reload rising edge.</summary>
	public void PlayReload(WeaponStats weapon, Vector3 weaponWorldPos)
	{
		if (weapon == null) return;
		EnsureLoaded(weapon.ReloadClips);
		EnsurePool();
		float pitch = 1f + _rng.RandfRange(-PitchRandomness, PitchRandomness);
		string played = EmitLayer(weapon.ReloadClips, 0f, Bus, weaponWorldPos, pitch, ReloadMaxHearDistance);
		Dbg.Print($"[WeaponAudio] Reload {weapon.Name} | {(IsLocalPlayer ? "local" : "remote")} → "
			+ (played != null ? System.IO.Path.GetFileName(played) : "clip not loaded yet, silent"));
	}

	/// <summary>Plays the dry-fire click (empty magazine). Very short 3D hearing range.</summary>
	public void PlayDryFire(WeaponStats weapon, Vector3 muzzleWorldPos)
	{
		if (weapon == null) return;
		EnsureLoaded(weapon.DryFireClips);
		EnsurePool();
		float pitch = 1f + _rng.RandfRange(-PitchRandomness, PitchRandomness);
		string played = EmitLayer(weapon.DryFireClips, 0f, Bus, muzzleWorldPos, pitch, DryFireMaxHearDistance);
		Dbg.Print($"[WeaponAudio] DryFire {weapon.Name} | {(IsLocalPlayer ? "local" : "remote")} → "
			+ (played != null ? System.IO.Path.GetFileName(played) : "clip not loaded yet, silent"));
	}

	/// <summary>Plays a random clip from <paramref name="clips"/> on the next pool node; returns the path played or null.</summary>
	private string EmitLayer(string[] clips, float volumeDb, StringName busName,
		Vector3 worldPos, float pitch, float maxDist3D)
	{
		if (clips == null || clips.Length == 0) return null;

		int start = _rng.RandiRange(0, clips.Length - 1);
		AudioStream clip = null;
		string chosenPath = null;
		for (int i = 0; i < clips.Length; i++)
		{
			string path = clips[(start + i) % clips.Length];
			if (!string.IsNullOrEmpty(path) && _clipCache.TryGetValue(path, out var s)) { clip = s; chosenPath = path; break; }
		}
		if (clip == null) return null;

		var node = _pool[_poolCursor];
		_poolCursor = (_poolCursor + 1) % _pool.Length;

		if (node is AudioStreamPlayer3D p3d)
		{
			p3d.MaxDistance = maxDist3D;
			p3d.GlobalPosition = worldPos;
			p3d.Stream = clip;
			p3d.VolumeDb = volumeDb + VolumeDb3D;
			p3d.PitchScale = pitch;
			p3d.Bus = busName;
			p3d.Play();
		}
		else if (node is AudioStreamPlayer p2d)
		{
			p2d.Stream = clip;
			p2d.VolumeDb = volumeDb;
			p2d.PitchScale = pitch;
			p2d.Bus = busName;
			p2d.Play();
		}
		return chosenPath;
	}

	/// <summary>Maps a <see cref="ReverbEnv"/> value to the matching reverb bus name.</summary>
	private StringName ReverbBusFor(ReverbEnv env) => env switch
	{
		ReverbEnv.Tunnel => ReverbTunnelBusName,
		ReverbEnv.Indoor => ReverbIndoorBusName,
		_ => ReverbOutdoorBusName,
	};

	/// <summary>Distance from the audio listener (active camera) to a sound source.</summary>
	private float ListenerDistance(Vector3 sourcePos)
	{
		Camera3D cam = GetViewport()?.GetCamera3D();
		return cam == null ? 0f : cam.GlobalPosition.DistanceTo(sourcePos);
	}

	/// <summary>True if a raycast from the active camera to the source hits the map well before it (occluded).</summary>
	private bool IsOccluded(Vector3 sourcePos)
	{
		Camera3D cam = GetViewport()?.GetCamera3D();
		if (cam == null) return false;
		Vector3 ear = cam.GlobalPosition;
		float full = ear.DistanceTo(sourcePos);
		if (full < 1.5f) return false;

		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null) return false;
		if (_occlusionQuery == null)
		{
			_occlusionQuery = PhysicsRayQueryParameters3D.Create(ear, sourcePos, OcclusionMask);
		}
		_occlusionQuery.From = ear;
		_occlusionQuery.To = sourcePos;
		_occlusionQuery.CollisionMask = OcclusionMask;
		if (!space.IntersectRayInto(_occlusionQuery, _occlusionResult)) return false;
		float hitDist = ear.DistanceTo(_occlusionResult.GetPosition());
		return hitDist < full - 1.0f;
	}

	/// <summary>Builds the player pool lazily on first sound, so <see cref="IsLocalPlayer"/> (set by NetworkPlayer) is final.</summary>
	private void EnsurePool()
	{
		if (_pool != null) return;
		EnsureHelperBuses();

		int n = Mathf.Max(1, PoolSize);
		_pool = new Node[n];
		for (int i = 0; i < n; i++)
		{
			if (IsLocalPlayer)
			{
				var p = new AudioStreamPlayer { Bus = Bus };
				AddChild(p);
				_pool[i] = p;
			}
			else
			{
				var p = new AudioStreamPlayer3D
				{
					Bus = Bus,
					MaxDistance = MaxHearDistance,
					UnitSize = UnitSize,
					AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
				};
				AddChild(p);
				_pool[i] = p;
			}
		}
	}

	/// <summary>Creates the occlusion low-pass bus and the three reverb buses once; all send to <see cref="Bus"/>.</summary>
	private void EnsureHelperBuses()
	{
		if (_busesReady) return;
		_busesReady = true;

		if (AudioServer.GetBusIndex(OccludedBusName) < 0)
		{
			int idx = AudioServer.BusCount;
			AudioServer.AddBus(idx);
			AudioServer.SetBusName(idx, OccludedBusName);
			AudioServer.SetBusSend(idx, Bus);
			AudioServer.AddBusEffect(idx, new AudioEffectLowPassFilter { CutoffHz = OcclusionLowPassHz });
		}
		AddReverbBus(ReverbOutdoorBusName, OutdoorRoom, OutdoorWet);
		AddReverbBus(ReverbIndoorBusName, IndoorRoom, IndoorWet);
		AddReverbBus(ReverbTunnelBusName, TunnelRoom, TunnelWet);
	}

	/// <summary>Adds a reverb bus sending to <see cref="Bus"/>, configured with the given room size and wet level.</summary>
	private void AddReverbBus(string name, float roomSize, float wet)
	{
		if (AudioServer.GetBusIndex(name) >= 0) return;
		int idx = AudioServer.BusCount;
		AudioServer.AddBus(idx);
		AudioServer.SetBusName(idx, name);
		AudioServer.SetBusSend(idx, Bus);
		AudioServer.AddBusEffect(idx, new AudioEffectReverb
		{
			RoomSize = roomSize,
			Wet = wet,
			Damping = ReverbDamping,
		});
	}
}
